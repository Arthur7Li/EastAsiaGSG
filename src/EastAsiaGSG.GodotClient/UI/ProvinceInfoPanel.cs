using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Map;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Reactive HUD overlay displaying detailed geopolitical, military, and infrastructure statistics
/// for the currently hovered or selected province and its parent state.
/// </summary>
public partial class ProvinceInfoPanel : PanelContainer
{
    private SimulationWorld? _world;

    private Label? _headerLabel;
    private Label? _stateLabel;
    private Label? _nationLabel;
    private Label? _terrainLabel;
    private Label? _infraLabel;
    private Label? _economyLabel;
    private Label? _resourcesLabel;

    public override void _Ready()
    {
        BuildUi();
    }

    public void Initialize(SimulationWorld world)
    {
        _world = world;
    }

    private void BuildUi()
    {
        var styleBox = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.12f, 0.90f),
            BorderColor = new Color(0.25f, 0.32f, 0.40f, 0.80f),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        AddThemeStyleboxOverride("panel", styleBox);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        AddChild(vbox);

        _headerLabel = new Label
        {
            Text = "No Province Selected",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _headerLabel.AddThemeFontSizeOverride("font_size", 16);
        _headerLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.45f));
        vbox.AddChild(_headerLabel);

        _stateLabel = new Label { Text = "State: —" };
        _stateLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_stateLabel);

        _nationLabel = new Label { Text = "Owner: — | Controller: —" };
        _nationLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_nationLabel);

        _terrainLabel = new Label { Text = "Terrain: —" };
        _terrainLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_terrainLabel);

        _infraLabel = new Label { Text = "Supply Hub: — | Port: — | Infra: —" };
        _infraLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_infraLabel);

        _economyLabel = new Label { Text = "Civ: 0 | Mil: 0 | Dock: 0 | Pop: 0" };
        _economyLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_economyLabel);

        _resourcesLabel = new Label { Text = "Steel: 0 | Semi: 0 | Oil: 0 | Alum: 0" };
        _resourcesLabel.AddThemeFontSizeOverride("font_size", 13);
        _resourcesLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 0.95f));
        vbox.AddChild(_resourcesLabel);
    }

    /// <summary>
    /// Updates the panel with data from the specified province ID.
    /// </summary>
    public void DisplayProvince(ushort provinceId)
    {
        if (_headerLabel == null)
        {
            return;
        }

        if (provinceId >= SimulationWorld.MaxProvinces || provinceId == ProvinceMapBuffer.OceanProvinceId)
        {
            _headerLabel.Text = "Ocean / International Waters";
            _stateLabel!.Text = "State: None (Sea Zone)";
            _nationLabel!.Text = "Owner: Open Sea";
            _terrainLabel!.Text = "Terrain: Ocean";
            _infraLabel!.Text = "Supply Hub: 0 | Port: 0 | Infra: 0";
            _economyLabel!.Text = "Civ: 0 | Mil: 0 | Dock: 0 | Pop: 0";
            _resourcesLabel!.Text = "Steel: 0 | Semi: 0 | Oil: 0 | Alum: 0";
            return;
        }

        if (_world == null)
        {
            _headerLabel.Text = $"Province #{provinceId}";
            return;
        }

        ref readonly ProvinceData prov = ref _world.Provinces[provinceId];
        ushort stateId = prov.StateId;
        ref readonly StateData state = ref _world.States[stateId];

        string ownerTag = CountryTag.GetCode(prov.OwnerTag);
        string ctrlTag = CountryTag.GetCode(prov.ControllerTag);

        _headerLabel.Text = $"Province #{provinceId} ({prov.Terrain})";
        _stateLabel!.Text = $"State #{stateId} (Region #{prov.StrategicRegionId})";
        _nationLabel!.Text = $"Owner: {ownerTag} | Controller: {ctrlTag}";
        _terrainLabel!.Text = $"Terrain: {prov.Terrain} (Supply Ratio: {prov.SupplyRatio:P0})";
        _infraLabel!.Text = $"Hub: Lv.{prov.SupplyHubLevel} | Port: Lv.{prov.PortLevel} | Infra: Lv.{prov.InfrastructureLevel}";
        _economyLabel!.Text = $"Civ: {state.CivilianFactories} | Mil: {state.MilitaryFactories} | Dock: {state.NavalDockyards} | Pop: {state.Population:N0}";
        _resourcesLabel!.Text = $"Steel: {state.GetResource(ResourceType.Steel)} | Semi: {state.GetResource(ResourceType.Semiconductors)} | Oil: {state.GetResource(ResourceType.Oil)} | Alum: {state.GetResource(ResourceType.Aluminum)}";
    }
}
