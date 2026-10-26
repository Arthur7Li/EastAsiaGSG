using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Loaders;
using EastAsiaGSG.Data.Map;
using EastAsiaGSG.GodotClient.Map;
using EastAsiaGSG.GodotClient.UI;
using Godot;

namespace EastAsiaGSG.GodotClient;

/// <summary>
/// Root presentation scene controller initializing the headless simulation world,
/// the GPU MapRenderer, the 2D RTS CameraController, and the interactive HUD overlays.
/// </summary>
public partial class MainView : Node2D
{
    private SimulationWorld? _world;

    private MapRenderer? _mapRenderer;
    private CameraController? _camera;
    private MapModeToolbar? _toolbar;
    private ProvinceInfoPanel? _infoPanel;

    public override void _Ready()
    {
        // 1. Initialize Headless Simulation World
        _world = new SimulationWorld();

        string? statesPath = ResolvePath("data/map/states.json");
        string? provincesPath = ResolvePath("data/map/provinces.json");

        if (statesPath != null && provincesPath != null)
        {
            JsonMapLoader.LoadMap(_world, statesPath, provincesPath);
            GD.Print($"[MainView] Loaded Korean Peninsula Micro-Sandbox from: {statesPath}");
        }
        else
        {
            GD.PushWarning("[MainView] Could not locate data/map JSON files; running with default world state.");
        }

        // 2. Setup MapRenderer
        _mapRenderer = GetNodeOrNull<MapRenderer>("MapRenderer");
        if (_mapRenderer == null)
        {
            _mapRenderer = new MapRenderer { Name = "MapRenderer" };
            AddChild(_mapRenderer);
        }
        _mapRenderer.Initialize(_world);

        // 3. Setup CameraController
        _camera = GetNodeOrNull<CameraController>("CameraController");
        if (_camera == null)
        {
            _camera = new CameraController { Name = "CameraController" };
            AddChild(_camera);
        }
        _camera.MakeCurrent();

        int mapWidth = _mapRenderer.ProvinceBuffer?.Width ?? 1024;
        int mapHeight = _mapRenderer.ProvinceBuffer?.Height ?? 1024;
        _camera.SetMapBounds(new Rect2(0, 0, mapWidth, mapHeight), margin: 250f);

        // Focus camera on Korean Peninsula central theater (Seoul / DMZ sector ~ 450, 600)
        _camera.FocusOnPosition(new Vector2(450, 600), 1.0f);

        // 4. Setup HUD CanvasLayer
        var hudLayer = GetNodeOrNull<CanvasLayer>("HUD");
        if (hudLayer == null)
        {
            hudLayer = new CanvasLayer { Name = "HUD" };
            AddChild(hudLayer);
        }

        // Setup MapModeToolbar at bottom-right
        _toolbar = hudLayer.GetNodeOrNull<MapModeToolbar>("MapModeToolbar");
        if (_toolbar == null)
        {
            _toolbar = new MapModeToolbar { Name = "MapModeToolbar" };
            hudLayer.AddChild(_toolbar);
        }
        _toolbar.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _toolbar.Position = new Vector2(-600, -50);
        _toolbar.ModeSelected += mode => _mapRenderer.SetMapMode(mode);

        // Setup ProvinceInfoPanel at top-left
        _infoPanel = hudLayer.GetNodeOrNull<ProvinceInfoPanel>("ProvinceInfoPanel");
        if (_infoPanel == null)
        {
            _infoPanel = new ProvinceInfoPanel { Name = "ProvinceInfoPanel" };
            hudLayer.AddChild(_infoPanel);
        }
        _infoPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _infoPanel.Position = new Vector2(20, 20);
        _infoPanel.Initialize(_world);

        // Wire map interactions to info panel
        _mapRenderer.ProvinceHovered += id => _infoPanel.DisplayProvince(id);
        _mapRenderer.ProvinceSelected += id => _infoPanel.DisplayProvince(id);
    }

    private static string? ResolvePath(string relativePath)
    {
        // Check AppContext.BaseDirectory and walk upwards to find repo root
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, relativePath);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == null || parent == current)
            {
                break;
            }
            current = parent;
        }

        // Check relative to ProjectSettings if in Godot editor/runtime
        try
        {
            string globalized = ProjectSettings.GlobalizePath("res://../../" + relativePath);
            if (File.Exists(globalized))
            {
                return globalized;
            }
        }
        catch
        {
            // Fallback
        }

        return null;
    }

    public override void _ExitTree()
    {
        _world?.Dispose();
        _world = null;
    }
}
