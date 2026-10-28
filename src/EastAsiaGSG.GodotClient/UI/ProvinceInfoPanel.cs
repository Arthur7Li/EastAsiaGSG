using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Map;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Reactive HUD overlay displaying detailed geopolitical, military, and infrastructure statistics
/// for the currently hovered or selected province and its parent state, as well as selected unit orders.
/// </summary>
public partial class ProvinceInfoPanel : PanelContainer
{
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    private SimulationWorld? _world;

    private Label? _headerLabel;
    private Label? _stateLabel;
    private Label? _nationLabel;
    private Label? _terrainLabel;
    private Label? _infraLabel;
    private Label? _economyLabel;
    private Label? _resourcesLabel;
    private Label? _stationedDivLabel;
    private PanelContainer? _selectedDivPanel;
    private Label? _selectedDivLabel;

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
            BgColor = new Color(0.08f, 0.10f, 0.12f, 0.92f),
            BorderColor = new Color(0.25f, 0.32f, 0.40f, 0.85f),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
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

        _stationedDivLabel = new Label { Text = "Units: None" };
        _stationedDivLabel.AddThemeFontSizeOverride("font_size", 13);
        _stationedDivLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.95f, 0.65f));
        vbox.AddChild(_stationedDivLabel);

        // Separator for Selected Division Order Banner
        vbox.AddChild(new HSeparator());

        _selectedDivPanel = new PanelContainer();
        var selStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.16f, 0.22f, 0.95f),
            BorderColor = new Color(0.95f, 0.80f, 0.25f, 0.90f),
            BorderWidthLeft = 2,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };
        _selectedDivPanel.AddThemeStyleboxOverride("panel", selStyle);

        _selectedDivLabel = new Label
        {
            Text = "👉 Click a division counter or province to select\n👉 Right-click any province to Move or Attack",
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _selectedDivLabel.AddThemeFontSizeOverride("font_size", 12);
        _selectedDivLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.90f, 0.95f));
        _selectedDivPanel.AddChild(_selectedDivLabel);

        vbox.AddChild(_selectedDivPanel);
    }

    /// <summary>
    /// Updates the selected division banner with active orders guidance.
    /// </summary>
    public void DisplaySelectedDivision(DivisionComponent? div)
    {
        if (_selectedDivLabel == null)
        {
            return;
        }

        if (div.HasValue)
        {
            DivisionComponent d = div.Value;
            string tag = CountryTag.GetCode(d.CountryTag);
            string stateStr = d.State switch
            {
                DivisionState.Attacking => "⚔ ATTACKING",
                DivisionState.Moving => "➔ MOVING",
                DivisionState.Retreating => "◀ RETREATING",
                DivisionState.Defending => "🛡 DEFENDING",
                _ => "● IDLE"
            };

            _selectedDivLabel.Text = $"★ [{tag}] Division #{d.DivisionId} ({stateStr})\n" +
                                     $"Org: {d.Organization:F0}/{d.MaxOrganization:F0} | Str: {d.Strength:P0} | Speed: {d.Speed:F0}km/h\n" +
                                     $"👉 Right-Click map to Move or Attack!";
            _selectedDivLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.90f, 0.40f));
        }
        else
        {
            _selectedDivLabel.Text = "👉 Click a division counter or province to select\n👉 Right-click any province to Move or Attack";
            _selectedDivLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.85f, 0.90f));
        }
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
            _stationedDivLabel!.Text = "Units: None";
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

        // Query stationed divisions in this province
        string stationedText = "";
        Query divQuery = _world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                if (divs[i].CurrentProvinceId == provinceId && divs[i].State != DivisionState.Shattered)
                {
                    string tag = CountryTag.GetCode(divs[i].CountryTag);
                    stationedText += (stationedText.Length > 0 ? ", " : "") + $"[{tag}] #{divs[i].DivisionId} ({divs[i].State})";
                }
            }
        }
        _stationedDivLabel!.Text = stationedText.Length > 0 ? $"Units: {stationedText}" : "Units: None";
    }
}
