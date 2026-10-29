using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// HOI4-style Left Side-Drawer displaying controlled States, Civilian Factory allocation (C_free / 15 cap per state),
/// active construction progress bars, and strictly one-way command buttons (QueueConstruction, AssignFactory).
/// </summary>
public partial class ConstructionQueuePanel : PanelContainer
{
    private sealed class StateRowWidgets
    {
        public ushort StateId;
        public PanelContainer Container = null!;
        public Label HeaderLabel = null!;
        public Label LevelsLabel = null!;
        public Label QueueStatusLabel = null!;
        public ProgressBar ProgressBar = null!;
    }

    private static readonly string[] KoreanStateNames =
    [
        "Gyeonggi-Seoul Capital Area",
        "Gangwon Eastern Mountains",
        "Chungcheong Central Corridor",
        "Jeolla Southwestern Plains",
        "Gyeongsang-Busan Industrial Belt",
        "Pyongyang-Nampo Capital District",
        "Hwanghae Forward DMZ Sector",
        "Chagang-Kanggye Northern Arsenal",
        "South Hamgyong-Wonsan Coast",
        "North Hamgyong-Chongjin Steel Zone"
    ];

    private SimulationWorld? _world;
    private byte _activeCountryTag = CountryTag.ROK;

    private Label? _titleLabel;
    private Label? _summaryLabel;
    private VBoxContainer? _statesListContainer;
    private readonly List<StateRowWidgets> _stateRows = [];

    // Pending UI feedback preview before the next 6-hour tick drains the command queue
    private readonly Dictionary<ushort, (BuildingType Type, byte Levels, ushort CivAssigned)> _pendingQueues = [];

    public event Action? CommandEnqueued;
    public event Action? CloseRequested;

    public override void _Ready()
    {
        BuildUi();
        Refresh();
    }

    public void Initialize(SimulationWorld world, byte activeCountryTag = CountryTag.ROK)
    {
        _world = world;
        _activeCountryTag = activeCountryTag;
        RebuildStateRows();
        Refresh();
    }

    public void SetActiveCountry(byte countryTag)
    {
        _activeCountryTag = countryTag;
        _pendingQueues.Clear();
        RebuildStateRows();
        Refresh();
    }

    public void ClearPendingPreviews()
    {
        _pendingQueues.Clear();
    }

    private void BuildUi()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.14f, 0.96f),
            BorderColor = new Color(0.28f, 0.36f, 0.46f, 0.95f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 2,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        AddThemeStyleboxOverride("panel", style);

        var rootVBox = new VBoxContainer();
        rootVBox.AddThemeConstantOverride("separation", 6);
        AddChild(rootVBox);

        // Header Row with Title & Close Button
        var headerHBox = new HBoxContainer();
        rootVBox.AddChild(headerHBox);

        _titleLabel = new Label
        {
            Text = "State Construction & Civilian Industry [ROK]",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.86f, 0.40f));
        headerHBox.AddChild(_titleLabel);

        var closeBtn = new Button
        {
            Text = " ✕ ",
            FocusMode = FocusModeEnum.None,
            Flat = true,
            TooltipText = "Return to Map Info [Esc]"
        };
        closeBtn.Pressed += () => CloseRequested?.Invoke();
        headerHBox.AddChild(closeBtn);

        _summaryLabel = new Label
        {
            Text = "Available Civilian Factories: 0 / 0 (Consumer Goods: 0) | Speed: +0%"
        };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 11);
        _summaryLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.92f, 0.70f));
        rootVBox.AddChild(_summaryLabel);

        rootVBox.AddChild(new HSeparator());

        // Scrollable State Construction Cards
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        rootVBox.AddChild(scroll);

        _statesListContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _statesListContainer.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_statesListContainer);
    }

    private void RebuildStateRows()
    {
        if (_statesListContainer == null || _world == null)
        {
            return;
        }

        foreach (Node child in _statesListContainer.GetChildren())
        {
            child.QueueFree();
        }
        _stateRows.Clear();

        ReadOnlySpan<StateData> states = _world.States;
        for (ushort stateId = 0; stateId < states.Length; stateId++)
        {
            ref readonly StateData state = ref states[stateId];
            if (state.ControllerTag != _activeCountryTag)
            {
                continue;
            }

            StateRowWidgets row = CreateStateRow(stateId);
            _stateRows.Add(row);
            _statesListContainer.AddChild(row.Container);
        }
    }

    private StateRowWidgets CreateStateRow(ushort stateId)
    {
        var card = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.15f, 0.20f, 0.95f),
            BorderColor = new Color(0.24f, 0.31f, 0.40f, 0.90f),
            BorderWidthLeft = 2,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 7,
            ContentMarginBottom = 7
        };
        card.AddThemeStyleboxOverride("panel", cardStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        card.AddChild(vbox);

        var headerLabel = new Label { Text = GetStateDisplayName(stateId) };
        headerLabel.AddThemeFontSizeOverride("font_size", 12);
        headerLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.90f, 0.60f));
        vbox.AddChild(headerLabel);

        var levelsLabel = new Label { Text = "Civ: 0 | Mil: 0 | Dock: 0 | Infra: 0 | Air: 0 | SAM: 0" };
        levelsLabel.AddThemeFontSizeOverride("font_size", 11);
        levelsLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.86f, 0.92f));
        vbox.AddChild(levelsLabel);

        var progressHBox = new HBoxContainer();
        progressHBox.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(progressHBox);

        var queueStatusLabel = new Label
        {
            Text = "No active project",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        queueStatusLabel.AddThemeFontSizeOverride("font_size", 11);
        queueStatusLabel.AddThemeColorOverride("font_color", new Color(0.68f, 0.88f, 0.98f));
        progressHBox.AddChild(queueStatusLabel);

        var progressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = true,
            CustomMinimumSize = new Vector2(110, 16)
        };
        progressHBox.AddChild(progressBar);

        // Action Buttons Row (Strictly enqueues deterministic SimulationCommand structs)
        var btnHBox = new HBoxContainer();
        btnHBox.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(btnHBox);

        AddBuildButton(btnHBox, "+Civ", stateId, BuildingType.CivilianFactory);
        AddBuildButton(btnHBox, "+Mil", stateId, BuildingType.MilitaryFactory);
        AddBuildButton(btnHBox, "+Infra", stateId, BuildingType.Infrastructure);
        AddBuildButton(btnHBox, "+SAM", stateId, BuildingType.RadarSam);
        AddBuildButton(btnHBox, "+Air", stateId, BuildingType.Airbase);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        btnHBox.AddChild(spacer);

        ushort capturedStateId = stateId;
        var minusCivBtn = new Button
        {
            Text = "-5 Civ",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(52, 24),
            TooltipText = "Reduce assigned Civilian Factories on this state by 5"
        };
        minusCivBtn.AddThemeFontSizeOverride("font_size", 10);
        minusCivBtn.Pressed += () => AdjustStateCivFactories(capturedStateId, -5);
        btnHBox.AddChild(minusCivBtn);

        var plusCivBtn = new Button
        {
            Text = "+5 Civ",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(52, 24),
            TooltipText = "Assign +5 Civilian Factories to this state (up to 15 max)"
        };
        plusCivBtn.AddThemeFontSizeOverride("font_size", 10);
        plusCivBtn.Pressed += () => AdjustStateCivFactories(capturedStateId, +5);
        btnHBox.AddChild(plusCivBtn);

        return new StateRowWidgets
        {
            StateId = stateId,
            Container = card,
            HeaderLabel = headerLabel,
            LevelsLabel = levelsLabel,
            QueueStatusLabel = queueStatusLabel,
            ProgressBar = progressBar
        };
    }

    private void AddBuildButton(HBoxContainer parent, string text, ushort stateId, BuildingType buildingType)
    {
        var btn = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(48, 24),
            TooltipText = $"Queue 1x {buildingType} ({ConstructionSystem.GetBuildingCostIc(buildingType):N0} IC)"
        };
        btn.AddThemeFontSizeOverride("font_size", 10);
        btn.Pressed += () => EnqueueBuildingOrder(stateId, buildingType);
        parent.AddChild(btn);
    }

    private void EnqueueBuildingOrder(ushort stateId, BuildingType buildingType)
    {
        if (_world == null || stateId >= SimulationWorld.MaxStates)
        {
            return;
        }

        SimulationCommand cmd = SimulationCommand.CreateQueueConstruction(
            countryTag: _activeCountryTag,
            stateId: stateId,
            buildingType: buildingType,
            levels: 1,
            assignedCivFactories: ConstructionSystem.MaxCivFactoriesPerState);

        if (_world.EnqueueCommand(in cmd))
        {
            ref readonly StateData current = ref _world.States[stateId];
            byte previewLevels = current.ActiveBuildingType == buildingType
                ? (byte)Math.Min(255, current.QueuedBuildingLevels + 1)
                : (byte)1;

            _pendingQueues[stateId] = (buildingType, previewLevels, ConstructionSystem.MaxCivFactoriesPerState);
            Refresh();
            CommandEnqueued?.Invoke();
        }
    }

    private void AdjustStateCivFactories(ushort stateId, int delta)
    {
        if (_world == null || stateId >= SimulationWorld.MaxStates)
        {
            return;
        }

        ref readonly StateData state = ref _world.States[stateId];
        int currentCiv = _pendingQueues.TryGetValue(stateId, out var pending)
            ? pending.CivAssigned
            : state.AssignedCivFactories;

        int updated = Math.Clamp(currentCiv + delta, 0, ConstructionSystem.MaxCivFactoriesPerState);

        SimulationCommand cmd = SimulationCommand.CreateAssignFactory(
            countryTag: _activeCountryTag,
            stateOrLineId: stateId,
            factoryCount: updated);

        if (_world.EnqueueCommand(in cmd))
        {
            BuildingType activeType = _pendingQueues.TryGetValue(stateId, out var p)
                ? p.Type
                : state.ActiveBuildingType;
            byte levels = _pendingQueues.TryGetValue(stateId, out var p2)
                ? p2.Levels
                : state.QueuedBuildingLevels;

            _pendingQueues[stateId] = (activeType, levels, (ushort)updated);
            Refresh();
            CommandEnqueued?.Invoke();
        }
    }

    private static string GetStateDisplayName(ushort stateId)
    {
        return stateId < KoreanStateNames.Length
            ? $"State #{stateId}: {KoreanStateNames[stateId]}"
            : $"State #{stateId}";
    }

    /// <summary>
    /// Refreshes all state construction telemetry from SimulationWorld (one-way read).
    /// </summary>
    public void Refresh()
    {
        if (_world == null || _titleLabel == null || _summaryLabel == null)
        {
            return;
        }

        string tagCode = CountryTag.GetCode(_activeCountryTag);
        _titleLabel.Text = $"State Construction & Civilian Industry [{tagCode}]";

        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(_world, _activeCountryTag);
        float speedMod = _world.Countries[_activeCountryTag].ConstructionSpeedModifier;

        _summaryLabel.Text =
            $"Free Civ Factories: {summary.FreeCivilianFactories} / {summary.TotalCivilianFactories} (Consumer Goods: {summary.ConsumerGoodsFactories}) | Speed: {speedMod:+0%;-0%;+0%}";

        for (int i = 0; i < _stateRows.Count; i++)
        {
            StateRowWidgets row = _stateRows[i];
            if (row.StateId >= SimulationWorld.MaxStates)
            {
                continue;
            }

            ref readonly StateData state = ref _world.States[row.StateId];
            row.HeaderLabel.Text = $"{GetStateDisplayName(state.StateId)}  (Pop: {state.Population / 1_000_000.0f:F1}M)";
            row.LevelsLabel.Text =
                $"Civ: {state.CivilianFactories} | Mil: {state.MilitaryFactories} | Dock: {state.NavalDockyards} | Infra: Lv.{state.InfrastructureLevel} | Air: Lv.{state.AirbaseLevel} | SAM: Lv.{state.RadarSamLevel}";

            BuildingType activeType = state.ActiveBuildingType;
            byte queuedLevels = state.QueuedBuildingLevels;
            ushort assignedCiv = state.AssignedCivFactories;

            if (_pendingQueues.TryGetValue(row.StateId, out var pending))
            {
                activeType = pending.Type;
                queuedLevels = pending.Levels;
                assignedCiv = pending.CivAssigned;
            }

            if (activeType != BuildingType.None && queuedLevels > 0)
            {
                float costIc = ConstructionSystem.GetBuildingCostIc(activeType);
                float pct = costIc > 0.0f ? Mathf.Clamp((state.ConstructionProgress / costIc) * 100.0f, 0.0f, 100.0f) : 0.0f;
                row.QueueStatusLabel.Text = $"🔨 {activeType} (x{queuedLevels}) — {assignedCiv}/15 Civ ({state.ConstructionProgress:F0}/{costIc:F0} IC)";
                row.ProgressBar.Value = pct;
            }
            else
            {
                row.QueueStatusLabel.Text = "Idle — Select a building below to queue construction";
                row.ProgressBar.Value = 0;
            }
        }
    }
}
