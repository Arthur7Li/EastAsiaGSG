using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Top-Left HUD Bar displaying the active country's macro-industrial indicators (PP, Stability, War Support, Manpower,
/// Civilian/Military/Dockyard factories), live national EquipmentStockpile counts across archetypes,
/// a country tag switcher (ROK / PRK), and toggle buttons for Construction and Production panels.
/// </summary>
public partial class NationalStockpileTopBar : PanelContainer
{
    private SimulationWorld? _world;

    private Button? _countrySwitchButton;
    private Label? _macroStatsLabel;
    private Label? _factoryStatsLabel;
    private Label? _stockpileStatsLabel;
    private Button? _toggleConstructionButton;
    private Button? _toggleProductionButton;

    public byte ActiveCountryTag { get; private set; } = CountryTag.ROK;

    public event Action<byte>? ActiveCountryChanged;
    public event Action? ToggleConstructionRequested;
    public event Action? ToggleProductionRequested;

    public override void _Ready()
    {
        BuildUi();
        Refresh();
    }

    public void Initialize(SimulationWorld world, byte initialCountryTag = CountryTag.ROK)
    {
        _world = world;
        ActiveCountryTag = initialCountryTag;
        Refresh();
    }

    public void SetActiveCountry(byte countryTag)
    {
        if (countryTag >= CountryTag.ActiveCount)
        {
            return;
        }

        ActiveCountryTag = countryTag;
        Refresh();
        ActiveCountryChanged?.Invoke(ActiveCountryTag);
    }

    public void SetDrawerStates(bool constructionVisible, bool productionVisible)
    {
        _toggleConstructionButton?.SetPressedNoSignal(constructionVisible);
        _toggleProductionButton?.SetPressedNoSignal(productionVisible);
    }

    private void BuildUi()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.14f, 0.94f),
            BorderColor = new Color(0.32f, 0.40f, 0.50f, 0.95f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 5,
            ContentMarginBottom = 5
        };
        AddThemeStyleboxOverride("panel", style);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 10);
        AddChild(hbox);

        // 1. Country Tag Switcher Button (ROK <-> PRK)
        _countrySwitchButton = new Button
        {
            Text = "🇰🇷 ROK (Tab)",
            CustomMinimumSize = new Vector2(96, 32),
            TooltipText = "Click or press Tab to switch inspected nation between ROK and PRK"
        };
        _countrySwitchButton.Pressed += CycleActiveCountry;
        hbox.AddChild(_countrySwitchButton);

        hbox.AddChild(new VSeparator());

        // 2. Two-row compact telemetry container (Row 1: Macro & Factories, Row 2: Equipment Stockpiles)
        var telemetryVBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        telemetryVBox.AddThemeConstantOverride("separation", 1);
        hbox.AddChild(telemetryVBox);

        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 14);
        telemetryVBox.AddChild(topRow);

        _macroStatsLabel = new Label
        {
            Text = "PP: 0 | Stab: 70% | War: 50% | MP: 250,000"
        };
        _macroStatsLabel.AddThemeFontSizeOverride("font_size", 12);
        _macroStatsLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.50f));
        topRow.AddChild(_macroStatsLabel);

        _factoryStatsLabel = new Label
        {
            Text = "Civ: 0/0 (CG: 0) | Mil: 0 | Dock: 0"
        };
        _factoryStatsLabel.AddThemeFontSizeOverride("font_size", 12);
        _factoryStatsLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.92f, 0.70f));
        topRow.AddChild(_factoryStatsLabel);

        _stockpileStatsLabel = new Label
        {
            Text = "Stockpile — Inf: 0 | Sup: 0 | IFV: 0 | MBT: 0 | Art: 0 | Heli: 0 | Air: 0 | PGM: 0"
        };
        _stockpileStatsLabel.AddThemeFontSizeOverride("font_size", 11);
        _stockpileStatsLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 0.96f));
        telemetryVBox.AddChild(_stockpileStatsLabel);

        hbox.AddChild(new VSeparator());

        // 3. Drawer Toggle Buttons for Construction & Production Panels
        _toggleConstructionButton = new Button
        {
            Text = "🏗 Construct (C)",
            ToggleMode = true,
            ButtonPressed = true,
            CustomMinimumSize = new Vector2(118, 32)
        };
        _toggleConstructionButton.Pressed += () => ToggleConstructionRequested?.Invoke();
        hbox.AddChild(_toggleConstructionButton);

        _toggleProductionButton = new Button
        {
            Text = "⚙ Produce (P)",
            ToggleMode = true,
            ButtonPressed = true,
            CustomMinimumSize = new Vector2(110, 32)
        };
        _toggleProductionButton.Pressed += () => ToggleProductionRequested?.Invoke();
        hbox.AddChild(_toggleProductionButton);
    }

    private void CycleActiveCountry()
    {
        byte nextTag = ActiveCountryTag == CountryTag.ROK ? CountryTag.PRK : CountryTag.ROK;
        SetActiveCountry(nextTag);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            if (keyEvent.Keycode == Key.Tab)
            {
                CycleActiveCountry();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Keycode == Key.C)
            {
                ToggleConstructionRequested?.Invoke();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Keycode == Key.P)
            {
                ToggleProductionRequested?.Invoke();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>
    /// Reads current nation state, factory summary, and equipment stockpiles from SimulationWorld (one-way read).
    /// </summary>
    public void Refresh()
    {
        if (_world == null || _macroStatsLabel == null || _factoryStatsLabel == null || _stockpileStatsLabel == null)
        {
            return;
        }

        byte tag = ActiveCountryTag;
        string code = CountryTag.GetCode(tag);
        if (_countrySwitchButton != null)
        {
            string flagPrefix = tag == CountryTag.ROK ? "ROK" : (tag == CountryTag.PRK ? "PRK" : code);
            _countrySwitchButton.Text = $"[{flagPrefix}] Switch (Tab)";
        }

        ref readonly CountryState country = ref _world.Countries[tag];
        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(_world, tag);

        _macroStatsLabel.Text =
            $"PP: {country.PoliticalPower:F0} | Stab: {country.Stability:P0} | War: {country.WarSupport:P0} | MP: {country.Manpower:N0}";

        _factoryStatsLabel.Text =
            $"Civ Free: {summary.FreeCivilianFactories}/{summary.TotalCivilianFactories} (CG: {summary.ConsumerGoodsFactories}) | Mil: {summary.TotalMilitaryFactories} | Dock: {summary.TotalNavalDockyards}";

        int inf = country.GetTotalArchetypeStockpile(EquipmentType.InfantryKit);
        int sup = country.GetTotalArchetypeStockpile(EquipmentType.SupportVehicles);
        int ifv = country.GetTotalArchetypeStockpile(EquipmentType.IFV);
        int mbt = country.GetTotalArchetypeStockpile(EquipmentType.MBT);
        int art = country.GetTotalArchetypeStockpile(EquipmentType.Artillery);
        int heli = country.GetTotalArchetypeStockpile(EquipmentType.AttackHeli);
        int air = country.GetTotalArchetypeStockpile(EquipmentType.TacticalAir);
        int pgm = country.GetTotalArchetypeStockpile(EquipmentType.PGM);

        _stockpileStatsLabel.Text =
            $"Stockpile — Inf: {inf:N0} | Sup: {sup:N0} | IFV: {ifv:N0} | MBT: {mbt:N0} | Art: {art:N0} | Heli: {heli:N0} | Air: {air:N0} | PGM: {pgm:N0}";
    }
}
