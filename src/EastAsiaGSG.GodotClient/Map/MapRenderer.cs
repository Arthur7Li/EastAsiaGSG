using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Map;
using Godot;

namespace EastAsiaGSG.GodotClient.Map;

/// <summary>
/// GPU Map Renderer owning the 16-bit RG provinces.png texture, the dynamic 2048x1 RGBA8 map_lut.png
/// lookup texture, and O(1) CPU mouse picking with zero physics raycasting.
/// </summary>
public partial class MapRenderer : Node2D
{
    private static readonly StringName UniformProvincesTex = new("provinces_tex");
    private static readonly StringName UniformMapLutTex = new("map_lut_tex");
    private static readonly StringName UniformHoveredProvinceId = new("hovered_province_id");
    private static readonly StringName UniformSelectedProvinceId = new("selected_province_id");

    private SimulationWorld? _world;
    private ProvinceMapBuffer? _provinceBuffer;
    private readonly MapLutBuffer _lutBuffer = new();

    private Sprite2D? _mapSprite;
    private ShaderMaterial? _shaderMaterial;
    private Image? _lutImage;
    private ImageTexture? _lutTexture;
    private ImageTexture? _provincesTexture;

    public MapMode CurrentMapMode { get; private set; } = MapMode.Political;
    public ushort HoveredProvinceId { get; private set; } = ProvinceMapBuffer.OceanProvinceId;
    public ushort SelectedProvinceId { get; private set; } = ProvinceMapBuffer.OceanProvinceId;

    public ProvinceMapBuffer? ProvinceBuffer => _provinceBuffer;
    public MapLutBuffer LutBuffer => _lutBuffer;

    public event Action<ushort>? ProvinceHovered;
    public event Action<ushort>? ProvinceSelected;

    /// <summary>
    /// Initializes the map renderer from a populated SimulationWorld and either loads provinces.png from disk
    /// or procedurally rasterizes the Voronoi province ID map from world province coordinates.
    /// </summary>
    public void Initialize(
        SimulationWorld world,
        string? provincesPngPath = null,
        string shaderResourcePath = "res://Map/MapShader.gdshader")
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;

        if (!string.IsNullOrWhiteSpace(provincesPngPath) && File.Exists(provincesPngPath))
        {
            _provinceBuffer = ProvinceMapBuffer.LoadFromPngFile(provincesPngPath);
        }
        else
        {
            _provinceBuffer = ProvinceMapBuffer.GenerateVoronoiFromWorld(world);
        }

        // Build static GPU texture for provinces.png (RGB8, Filter = Nearest)
        using Image provincesImage = Image.CreateFromData(
            _provinceBuffer.Width,
            _provinceBuffer.Height,
            useMipmaps: false,
            Image.Format.Rgb8,
            _provinceBuffer.RawRgbBytes);
        _provincesTexture = ImageTexture.CreateFromImage(provincesImage);

        // Build dynamic 1D GPU Lookup Texture (Width = 2048, Height = 1, RGBA8, Filter = Nearest)
        _lutBuffer.UpdateFromWorld(_world, CurrentMapMode);
        _lutImage = Image.CreateFromData(
            MapLutBuffer.LutWidth,
            height: 1,
            useMipmaps: false,
            Image.Format.Rgba8,
            _lutBuffer.RawRgbaBytes);
        _lutTexture = ImageTexture.CreateFromImage(_lutImage);

        // Configure ShaderMaterial & Sprite2D quad
        _shaderMaterial = new ShaderMaterial();
        if (ResourceLoader.Exists(shaderResourcePath))
        {
            _shaderMaterial.Shader = ResourceLoader.Load<Shader>(shaderResourcePath);
        }

        _shaderMaterial.SetShaderParameter(UniformProvincesTex, _provincesTexture);
        _shaderMaterial.SetShaderParameter(UniformMapLutTex, _lutTexture);
        _shaderMaterial.SetShaderParameter(UniformHoveredProvinceId, -1);
        _shaderMaterial.SetShaderParameter(UniformSelectedProvinceId, -1);

        if (_mapSprite == null)
        {
            _mapSprite = new Sprite2D
            {
                Name = "MapQuadSprite",
                Centered = false,
                TextureFilter = TextureFilterEnum.Nearest,
                Texture = _provincesTexture,
                Material = _shaderMaterial
            };
            AddChild(_mapSprite);
        }
        else
        {
            _mapSprite.Texture = _provincesTexture;
            _mapSprite.Material = _shaderMaterial;
        }
    }

    /// <summary>
    /// Switches the active MapMode and immediately refreshes the 1D GPU Lookup Texture.
    /// </summary>
    public void SetMapMode(MapMode mode)
    {
        CurrentMapMode = mode;
        RefreshMapLut();
    }

    /// <summary>
    /// Recomputes all 1,500 province colors on the CPU and uploads the 2048x1 RGBA8 LUT to the GPU.
    /// </summary>
    public void RefreshMapLut()
    {
        if (_world == null)
        {
            return;
        }

        _lutBuffer.UpdateFromWorld(_world, CurrentMapMode);

        if (_lutImage != null && _lutTexture != null)
        {
            _lutImage.SetData(
                MapLutBuffer.LutWidth,
                height: 1,
                useMipmaps: false,
                Image.Format.Rgba8,
                _lutBuffer.RawRgbaBytes);
            _lutTexture.Update(_lutImage);
        }
    }

    /// <summary>
    /// Converts normalized UV coordinates [0, 1] on the map quad to pixel coordinates (x, y)
    /// in the cached CPU copy of provinces.png, returning the ProvinceId in O(1) time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort PickProvinceAtUv(Vector2 uv)
    {
        if (_provinceBuffer == null)
        {
            return ProvinceMapBuffer.OceanProvinceId;
        }
        return _provinceBuffer.PickProvinceAtUv(uv.X, uv.Y);
    }

    /// <summary>
    /// Reads the ProvinceId at pixel coordinates (x, y) in O(1) time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort PickProvinceAtPixel(int x, int y)
    {
        if (_provinceBuffer == null)
        {
            return ProvinceMapBuffer.OceanProvinceId;
        }
        return _provinceBuffer.PickProvinceAtPixel(x, y);
    }

    /// <summary>
    /// Converts a world/global position into local map UV coordinates and performs O(1) CPU province picking.
    /// </summary>
    public ushort PickProvinceAtGlobalPosition(Vector2 globalPosition)
    {
        if (_provinceBuffer == null)
        {
            return ProvinceMapBuffer.OceanProvinceId;
        }

        Vector2 localPos = _mapSprite != null
            ? _mapSprite.ToLocal(globalPosition)
            : ToLocal(globalPosition);

        float u = localPos.X / _provinceBuffer.Width;
        float v = localPos.Y / _provinceBuffer.Height;
        return _provinceBuffer.PickProvinceAtUv(u, v);
    }

    public void SetHoveredProvince(ushort provinceId)
    {
        if (HoveredProvinceId == provinceId)
        {
            return;
        }

        HoveredProvinceId = provinceId;
        int uniformVal = provinceId < SimulationWorld.MaxProvinces ? provinceId : -1;
        _shaderMaterial?.SetShaderParameter(UniformHoveredProvinceId, uniformVal);
        ProvinceHovered?.Invoke(provinceId);
    }

    public void SetSelectedProvince(ushort provinceId)
    {
        if (SelectedProvinceId == provinceId)
        {
            return;
        }

        SelectedProvinceId = provinceId;
        int uniformVal = provinceId < SimulationWorld.MaxProvinces ? provinceId : -1;
        _shaderMaterial?.SetShaderParameter(UniformSelectedProvinceId, uniformVal);
        ProvinceSelected?.Invoke(provinceId);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_provinceBuffer == null)
        {
            return;
        }

        if (@event is InputEventMouseMotion)
        {
            ushort hovered = PickProvinceAtGlobalPosition(GetGlobalMousePosition());
            SetHoveredProvince(hovered);
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            ushort clicked = PickProvinceAtGlobalPosition(GetGlobalMousePosition());
            SetSelectedProvince(clicked);
        }
    }
}
