using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Interactive HUD panel displaying controlled States, Civilian Factory allocation (C_free / 15 cap per state),
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

    private SimulationWorld? _world;
    private byte _activeCountryTag = CountryTag.ROK;

    private Label? _titleLabel;
    private Label? _summaryLabel;
    private VBoxContainer? _statesListContainer;
    private readonly List<StateRowWidgets> _stateRows = [];

    // Pending UI feedback preview before the next 6-hour tick drains the command queue
    private readonly Dictionary<ushort, (BuildingType Type, byte Levels, ushort CivAssigned)> _pendingQueues = [];

    public event Action? CommandEnqueued;

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
            BgColor = new Color(0.08f, 0.10f, 0.13f, 0.94f),
            BorderColor = new Color(0.30f, 0.42f, 0.36f, 0.92f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        AddThemeStyleboxOverride("panel", style);

        var rootVBox = new VBoxContainer();
        rootVBox.AddThemeConstantOverride("separation", 6);
        AddChild(rootVBox);

        // Header Row
        var headerHBox = new HBoxContainer();
        rootVBox.AddChild(headerHBox);

        _titleLabel = new Label
        {
            Text = "🏗 State Construction Queue [ROK]",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.86f, 0.42f));
        headerHBox.AddChild(_titleLabel);

        var closeBtn = new Button { Text = "✕", Flat = true };
        closeBtn.Pressed += () => Visible = false;
        headerHBox.AddChild(closeBtn);

        _summaryLabel = new Label
        {
            Text = "Free Civ Factories: 0 / 0 (Consumer Goods: 0) | Max 15 Civ / State"
        };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 11);
        _summaryLabel.AddThemeColorOverride("font_color", new Color(0.60f, 0.92f, 0.72f));
        rootVBox.AddChild(_summaryLabel);

        rootVBox.AddChild(new HSeparator());

        // Scrollable State Construction Rows
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
        _statesListContainer.AddThemeConstantOverride("separation", 6);
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
            BgColor = new Color(0.12f, 0.15f, 0.19f, 0.92f),
            BorderColor = new Color(0.24f, 0.30f, 0.38f, 0.85f),
            BorderWidthLeft = 1,
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
        card.AddThemeStyleboxOverride("panel", cardStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 3);
        card.AddChild(vbox);

        var headerLabel = new Label { Text = $"State #{stateId}" };
        headerLabel.AddThemeFontSizeOverride("font_size", 12);
        headerLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.65f));
        vbox.AddChild(headerLabel);

        var levelsLabel = new Label { Text = "Civ: 0 | Mil: 0 | Dock: 0 | Infra: 0 | Air: 0 | SAM: 0" };
        levelsLabel.AddThemeFontSizeOverride("font_size", 11);
        levelsLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.86f, 0.92f));
        vbox.AddChild(levelsLabel);

        var progressHBox = new HBoxContainer();
        progressHBox.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(progressHBox);

        var queueStatusLabel = new Label
        {
            Text = "Idle (Queue building below)",
            CustomMinimumSize = new Vector2(185, 0)
        };
        queueStatusLabel.AddThemeFontSizeOverride("font_size", 11);
        queueStatusLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.88f, 0.98f));
        progressHBox.AddChild(queueStatusLabel);

        var progressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(80, 16)
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

        var minusCivBtn = new Button
        {
            Text = "-5 Civ",
            CustomMinimumSize = new Vector2(52, 22),
            TooltipText = "Reduce assigned Civilian Factories by 5"
        };
        minusCivBtn.AddThemeFontSizeOverride("font_size", 10);
        ushort capturedStateId = stateId;
        minusCivBtn.Pressed += () => AdjustStateCivFactories(capturedStateId, -5);
        btnHBox.AddChild(minusCivBtn);

        var plusCivBtn = new Button
        {
            Text = "+5 Civ",
            CustomMinimumSize = new Vector2(52, 22),
            TooltipText = "Increase assigned Civilian Factories by 5 (up to 15 max)"
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
            CustomMinimumSize = new Vector2(46, 22),
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

        // One-Way UI Data Binding: Enqueue deterministic QueueConstruction command
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

        // One-Way UI Data Binding: Enqueue deterministic AssignFactory command
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
        _titleLabel.Text = $"🏗 State Construction Queue [{tagCode}]";

        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(_world, _activeCountryTag);
        float speedMod = _world.Countries[_activeCountryTag].ConstructionSpeedModifier;

        _summaryLabel.Text =
            $"Free Civ: {summary.FreeCivilianFactories}/{summary.TotalCivilianFactories} (CG: {summary.ConsumerGoodsFactories}) | Speed: {speedMod:+0%;-0%;0%} | Max 15/State";

        for (int i = 0; i < _stateRows.Count; i++)
        {
            StateRowWidgets row = _stateRows[i];
            if (row.StateId >= SimulationWorld.MaxStates)
            {
                continue;
            }

            ref readonly StateData state = ref _world.States[row.StateId];
            row.HeaderLabel.Text = $"State #{state.StateId} (Pop: {state.Population:N0})";
            row.LevelsLabel.Text =
                $"Civ: {state.CivilianFactories} | Mil: {state.MilitaryFactories} | Dock: {state.NavalDockyards} | Infra: {state.InfrastructureLevel} | Air: {state.AirbaseLevel} | SAM: {state.RadarSamLevel}";

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
                row.QueueStatusLabel.Text = $"Building {activeType} (x{queuedLevels}) [{assignedCiv}/15 Civ]";
                row.ProgressBar.Value = pct;
            }
            else
            {
                row.QueueStatusLabel.Text = "Idle — Click +Civ/+Mil/+SAM to build";
                row.ProgressBar.Value = 0;
            }
        }
    }
}
