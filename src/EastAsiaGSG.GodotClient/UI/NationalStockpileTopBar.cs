using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Compact Top-Left HUD Bar displaying the active country's macro-industrial indicators
/// (PP, Stability, War Support, Manpower, Civilian/Military factories), key equipment stockpiles,
/// player agency actions (Deploy Division, Declare War), and a 15-country tag switcher.
/// </summary>
public partial class NationalStockpileTopBar : PanelContainer
{
    private static readonly byte[] PlayableCountryTags =
    [
        CountryTag.ROK, CountryTag.PRK, CountryTag.PRC, CountryTag.USA,
        CountryTag.SOV, CountryTag.JPN, CountryTag.TWN, CountryTag.VNM,
        CountryTag.PHL, CountryTag.MNG, CountryTag.THA, CountryTag.LAO,
        CountryTag.KHM, CountryTag.MMR, CountryTag.MYS
    ];

    private SimulationWorld? _world;

    private Button? _countrySwitchButton;
    private Button? _deployDivButton;
    private Button? _declareWarButton;
    private Label? _newsLabel;
    private Label? _macroStatsLabel;
    private Label? _stockpileStatsLabel;

    public byte ActiveCountryTag { get; private set; } = CountryTag.ROK;

    public event Action<byte>? ActiveCountryChanged;
    public event Action? MapInfoTabRequested;
    public event Action? ToggleFocusTreeRequested;
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

    private void BuildUi()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.14f, 0.95f),
            BorderColor = new Color(0.28f, 0.36f, 0.46f, 0.95f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 10,
            ContentMarginRight = 12,
            ContentMarginTop = 5,
            ContentMarginBottom = 5
        };
        AddThemeStyleboxOverride("panel", style);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 10);
        AddChild(hbox);

        // 1. Country Tag Switcher Button (Cycles 15 nations)
        _countrySwitchButton = new Button
        {
            Text = "ROK ⇄",
            CustomMinimumSize = new Vector2(68, 34),
            FocusMode = FocusModeEnum.None,
            TooltipText = "Switch inspected nation between 15 theatre powers [Hotkey: Tab]"
        };
        _countrySwitchButton.AddThemeFontSizeOverride("font_size", 12);
        _countrySwitchButton.Pressed += CycleActiveCountry;
        hbox.AddChild(_countrySwitchButton);

        hbox.AddChild(new VSeparator());

        // 2. Two-row compact telemetry container (~380px width)
        var telemetryVBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        telemetryVBox.AddThemeConstantOverride("separation", 1);
        hbox.AddChild(telemetryVBox);

        _macroStatsLabel = new Label
        {
            Text = "PP: 0 | Stab: 70% | War: 50% | MP: 420k | Civ: 0/0 | Mil: 0"
        };
        _macroStatsLabel.AddThemeFontSizeOverride("font_size", 11);
        _macroStatsLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.88f, 0.48f));
        telemetryVBox.AddChild(_macroStatsLabel);

        _stockpileStatsLabel = new Label
        {
            Text = "Stockpile — Inf: 0 | IFV: 0 | MBT: 0 | Art: 0 | Air: 0 | PGM: 0"
        };
        _stockpileStatsLabel.AddThemeFontSizeOverride("font_size", 11);
        _stockpileStatsLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.86f, 0.98f));
        telemetryVBox.AddChild(_stockpileStatsLabel);

        hbox.AddChild(new VSeparator());

        // 3. Compact Player Agency Actions: Deploy Division, Declare War, and News Indicator
        var actionsHBox = new HBoxContainer();
        actionsHBox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(actionsHBox);

        _deployDivButton = new Button
        {
            Text = "➕ Deploy",
            FocusMode = FocusModeEnum.None,
            TooltipText = "Deploy field division at capital province from national manpower & equipment stockpile"
        };
        _deployDivButton.AddThemeFontSizeOverride("font_size", 11);
        _deployDivButton.Pressed += OnDeployDivisionPressed;
        actionsHBox.AddChild(_deployDivButton);

        _declareWarButton = new Button
        {
            Text = "⚔ War",
            FocusMode = FocusModeEnum.None,
            TooltipText = "Issue diplomatic wargoal / declare war on theatre rival"
        };
        _declareWarButton.AddThemeFontSizeOverride("font_size", 11);
        _declareWarButton.Pressed += OnDeclareWarPressed;
        actionsHBox.AddChild(_declareWarButton);

        _newsLabel = new Label
        {
            Text = "📰 0",
            Visible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _newsLabel.AddThemeFontSizeOverride("font_size", 11);
        _newsLabel.AddThemeColorOverride("font_color", new Color(0.40f, 0.85f, 1.0f));
        actionsHBox.AddChild(_newsLabel);
    }

    private void CycleActiveCountry()
    {
        int currentIndex = Array.IndexOf(PlayableCountryTags, ActiveCountryTag);
        if (currentIndex < 0) currentIndex = 0;
        int nextIndex = (currentIndex + 1) % PlayableCountryTags.Length;
        SetActiveCountry(PlayableCountryTags[nextIndex]);
    }

    private void OnDeployDivisionPressed()
    {
        if (_world == null) return;
        ushort capitalProv = _world.GetCapitalProvince(ActiveCountryTag);
        _world.EnqueueCommand(SimulationCommand.CreateRecruitDivision(ActiveCountryTag, DivisionTemplateRecord.DefaultInfantryTemplateId, capitalProv));
    }

    private void OnDeclareWarPressed()
    {
        if (_world == null) return;
        byte rivalTag = GetDefaultRivalTag(ActiveCountryTag);
        _world.EnqueueCommand(SimulationCommand.CreateDeclareWar(ActiveCountryTag, rivalTag));
    }

    private static byte GetDefaultRivalTag(byte tag) => tag switch
    {
        CountryTag.ROK => CountryTag.PRK,
        CountryTag.PRK => CountryTag.ROK,
        CountryTag.TWN => CountryTag.PRC,
        CountryTag.PRC => CountryTag.TWN,
        CountryTag.USA => CountryTag.SOV,
        CountryTag.SOV => CountryTag.USA,
        CountryTag.JPN => CountryTag.SOV,
        CountryTag.VNM => CountryTag.KHM,
        CountryTag.KHM => CountryTag.VNM,
        _ => CountryTag.PRC
    };

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            if (keyEvent.Keycode == Key.Tab)
            {
                CycleActiveCountry();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Keycode == Key.F)
            {
                ToggleFocusTreeRequested?.Invoke();
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
            else if (keyEvent.Keycode == Key.Escape)
            {
                MapInfoTabRequested?.Invoke();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>
    /// Reads current nation state, factory summary, and equipment stockpiles from SimulationWorld (one-way read).
    /// </summary>
    public void Refresh()
    {
        if (_world == null || _macroStatsLabel == null || _stockpileStatsLabel == null)
        {
            return;
        }

        byte tag = ActiveCountryTag;
        string code = CountryTag.GetCode(tag);
        if (_countrySwitchButton != null)
        {
            _countrySwitchButton.Text = $"{code} ⇄";
        }

        if (_declareWarButton != null)
        {
            byte rival = GetDefaultRivalTag(tag);
            _declareWarButton.TooltipText = $"Declare War on theatre rival [{CountryTag.GetCode(rival)}]";
        }

        if (_deployDivButton != null)
        {
            _deployDivButton.TooltipText = $"Deploy 1 Division at capital ({_world.GetCapitalProvince(tag)}) using national manpower & stockpile";
        }

        if (_newsLabel != null)
        {
            int newsCount = _world.TriggeredNewsEventCount;
            if (newsCount > 0)
            {
                _newsLabel.Visible = true;
                _newsLabel.Text = $"📰 {newsCount}";
                _newsLabel.TooltipText = $"{newsCount} active news events recorded in simulation";
            }
            else
            {
                _newsLabel.Visible = false;
            }
        }

        ref readonly CountryState country = ref _world.Countries[tag];
        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(_world, tag);

        string mpFormatted = country.Manpower >= 1_000
            ? $"{country.Manpower / 1000.0f:F0}k"
            : country.Manpower.ToString("N0");

        float pacomEsc = _world.CountryHomelandEscalation[CountryTag.USA];
        byte defcon = _world.Countries[CountryTag.USA].Defcon;
        if (defcon == 0 || defcon > 5) defcon = 5;

        _macroStatsLabel.Text =
            $"PP: {country.PoliticalPower:F0} | Stab: {country.Stability:P0} | War: {country.WarSupport:P0} | MP: {mpFormatted} | Civ: {summary.FreeCivilianFactories}/{summary.TotalCivilianFactories} | Mil: {summary.TotalMilitaryFactories} | Esc: {pacomEsc:F0}%";
        _macroStatsLabel.TooltipText = $"Inspecting [{code}] | US PACOM Homeland Escalation: {pacomEsc:F0}% | DEFCON {defcon}";

        int inf = country.GetTotalArchetypeStockpile(EquipmentType.InfantryKit);
        int ifv = country.GetTotalArchetypeStockpile(EquipmentType.IFV);
        int mbt = country.GetTotalArchetypeStockpile(EquipmentType.MBT);
        int art = country.GetTotalArchetypeStockpile(EquipmentType.Artillery);
        int air = country.GetTotalArchetypeStockpile(EquipmentType.TacticalAir);
        int pgm = country.GetTotalArchetypeStockpile(EquipmentType.PGM);

        _stockpileStatsLabel.Text =
            $"Stockpile — Inf: {inf:N0} | IFV: {ifv:N0} | MBT: {mbt:N0} | Art: {art:N0} | Air: {air:N0} | PGM: {pgm:N0}";
    }
}
