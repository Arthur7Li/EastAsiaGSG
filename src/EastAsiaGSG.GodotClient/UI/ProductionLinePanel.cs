using Arch.Core;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Interactive HUD panel displaying Military Factory production lines, hyperbolic Production Efficiency bars,
/// daily IC and unit output rates, strategic resource availability, and one-way factory assignment buttons.
/// </summary>
public partial class ProductionLinePanel : PanelContainer
{
    private static readonly QueryDescription ProductionLineQuery = new QueryDescription().WithAll<ProductionLineComponent>();

    private sealed class LineRowWidgets
    {
        public int LineId;
        public PanelContainer Container = null!;
        public Label TitleLabel = null!;
        public Label OutputLabel = null!;
        public ProgressBar EfficiencyBar = null!;
        public Label EfficiencyLabel = null!;
    }

    private SimulationWorld? _world;
    private byte _activeCountryTag = CountryTag.ROK;

    private Label? _titleLabel;
    private Label? _summaryLabel;
    private VBoxContainer? _linesContainer;
    private readonly List<LineRowWidgets> _lineRows = [];

    // Pending UI preview before next 6-hour tick processes AssignProductionFactories command
    private readonly Dictionary<int, ushort> _pendingFactoryCounts = [];

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
        RebuildLineRows();
        Refresh();
    }

    public void SetActiveCountry(byte countryTag)
    {
        _activeCountryTag = countryTag;
        _pendingFactoryCounts.Clear();
        RebuildLineRows();
        Refresh();
    }

    public void ClearPendingPreviews()
    {
        _pendingFactoryCounts.Clear();
    }

    private void BuildUi()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.14f, 0.94f),
            BorderColor = new Color(0.36f, 0.40f, 0.52f, 0.92f),
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
            Text = "⚙ Military Production Lines [ROK]",
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
            Text = "Mil Factories: 0 / 0 | Steel: 0 | Semi: 0 | Oil: 0 | Alum: 0"
        };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 11);
        _summaryLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 0.98f));
        rootVBox.AddChild(_summaryLabel);

        rootVBox.AddChild(new HSeparator());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        rootVBox.AddChild(scroll);

        _linesContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _linesContainer.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_linesContainer);
    }

    private void RebuildLineRows()
    {
        if (_linesContainer == null || _world == null)
        {
            return;
        }

        foreach (Node child in _linesContainer.GetChildren())
        {
            child.QueueFree();
        }
        _lineRows.Clear();

        List<ProductionLineComponent> countryLines = [];
        Query query = _world.EcsWorld.Query(in ProductionLineQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ProductionLineComponent> lines = chunk.GetSpan<ProductionLineComponent>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                if (lines[i].CountryTag == _activeCountryTag)
                {
                    countryLines.Add(lines[i]);
                }
            }
        }

        countryLines.Sort(static (a, b) =>
        {
            int cmp = b.Priority.CompareTo(a.Priority);
            return cmp != 0 ? cmp : a.LineId.CompareTo(b.LineId);
        });

        for (int i = 0; i < countryLines.Count; i++)
        {
            LineRowWidgets row = CreateLineRow(countryLines[i].LineId);
            _lineRows.Add(row);
            _linesContainer.AddChild(row.Container);
        }
    }

    private LineRowWidgets CreateLineRow(int lineId)
    {
        var card = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.15f, 0.20f, 0.92f),
            BorderColor = new Color(0.26f, 0.32f, 0.42f, 0.85f),
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

        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(topRow);

        var titleLabel = new Label
        {
            Text = $"Line #{lineId}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 12);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.60f));
        topRow.AddChild(titleLabel);

        int capturedLineId = lineId;

        var minus1Btn = new Button
        {
            Text = "-1 Mil",
            CustomMinimumSize = new Vector2(48, 22),
            TooltipText = "Remove 1 Military Factory from this production line"
        };
        minus1Btn.AddThemeFontSizeOverride("font_size", 10);
        minus1Btn.Pressed += () => AdjustLineFactories(capturedLineId, -1);
        topRow.AddChild(minus1Btn);

        var plus1Btn = new Button
        {
            Text = "+1 Mil",
            CustomMinimumSize = new Vector2(48, 22),
            TooltipText = "Assign +1 Military Factory to this production line"
        };
        plus1Btn.AddThemeFontSizeOverride("font_size", 10);
        plus1Btn.Pressed += () => AdjustLineFactories(capturedLineId, +1);
        topRow.AddChild(plus1Btn);

        var plus5Btn = new Button
        {
            Text = "+5 Mil",
            CustomMinimumSize = new Vector2(48, 22),
            TooltipText = "Assign +5 Military Factories to this production line"
        };
        plus5Btn.AddThemeFontSizeOverride("font_size", 10);
        plus5Btn.Pressed += () => AdjustLineFactories(capturedLineId, +5);
        topRow.AddChild(plus5Btn);

        var outputLabel = new Label
        {
            Text = "Factories: 0/0 | Output: 0.0 IC/d (0.0 units/d)"
        };
        outputLabel.AddThemeFontSizeOverride("font_size", 11);
        outputLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.88f, 0.95f));
        vbox.AddChild(outputLabel);

        var effRow = new HBoxContainer();
        effRow.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(effRow);

        var effLabel = new Label
        {
            Text = "Eff: 25.0% / 80%",
            CustomMinimumSize = new Vector2(130, 0)
        };
        effLabel.AddThemeFontSizeOverride("font_size", 11);
        effLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.65f));
        effRow.AddChild(effLabel);

        var effBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 25,
            ShowPercentage = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(80, 14)
        };
        effRow.AddChild(effBar);

        return new LineRowWidgets
        {
            LineId = lineId,
            Container = card,
            TitleLabel = titleLabel,
            OutputLabel = outputLabel,
            EfficiencyBar = effBar,
            EfficiencyLabel = effLabel
        };
    }

    private void AdjustLineFactories(int lineId, int delta)
    {
        if (_world == null || !_world.TryFindProductionLineById(lineId, out Entity lineEntity))
        {
            return;
        }

        ref readonly ProductionLineComponent line = ref _world.EcsWorld.Get<ProductionLineComponent>(lineEntity);
        int currentAssigned = _pendingFactoryCounts.TryGetValue(lineId, out ushort pendingCount)
            ? pendingCount
            : line.AssignedMilitaryFactories;

        int updated = Math.Clamp(currentAssigned + delta, 0, ProductionLineComponent.MaxFactoriesPerLine);

        // One-Way UI Data Binding: Enqueue deterministic AssignProductionFactories command
        SimulationCommand cmd = SimulationCommand.CreateAssignProductionFactories(
            countryTag: _activeCountryTag,
            lineId: lineId,
            militaryFactories: (ushort)updated);

        if (_world.EnqueueCommand(in cmd))
        {
            _pendingFactoryCounts[lineId] = (ushort)updated;
            Refresh();
            CommandEnqueued?.Invoke();
        }
    }

    /// <summary>
    /// Refreshes all production line telemetry from SimulationWorld (one-way read).
    /// </summary>
    public void Refresh()
    {
        if (_world == null || _titleLabel == null || _summaryLabel == null)
        {
            return;
        }

        string tagCode = CountryTag.GetCode(_activeCountryTag);
        _titleLabel.Text = $"⚙ Military Production Lines [{tagCode}]";

        CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(_world, _activeCountryTag);

        int steel = 0, semi = 0, oil = 0, alum = 0;
        ReadOnlySpan<StateData> states = _world.States;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].ControllerTag == _activeCountryTag)
            {
                steel += states[i].GetResource(ResourceType.Steel);
                semi += states[i].GetResource(ResourceType.Semiconductors);
                oil += states[i].GetResource(ResourceType.Oil);
                alum += states[i].GetResource(ResourceType.Aluminum);
            }
        }

        int totalAssigned = 0;
        int totalActive = 0;
        float factoryMod = _world.Countries[_activeCountryTag].FactoryOutputModifier;

        for (int i = 0; i < _lineRows.Count; i++)
        {
            LineRowWidgets row = _lineRows[i];
            if (!_world.TryFindProductionLineById(row.LineId, out Entity lineEntity))
            {
                continue;
            }

            ref readonly ProductionLineComponent line = ref _world.EcsWorld.Get<ProductionLineComponent>(lineEntity);
            ushort assigned = _pendingFactoryCounts.TryGetValue(row.LineId, out ushort pending)
                ? pending
                : line.AssignedMilitaryFactories;

            totalAssigned += assigned;
            totalActive += line.ActiveMilitaryFactories;

            string genStr = line.Generation switch
            {
                TechGeneration.Gen0_1970Reserve => "Gen0 '70",
                TechGeneration.Gen1_1980Baseline => "Gen1 '80",
                TechGeneration.Gen2_1988LateColdWar => "Gen2 '88",
                TechGeneration.Gen3_1995DigitalStealth => "Gen3 '95",
                _ => "Gen1"
            };

            row.TitleLabel.Text = $"#{line.LineId} {line.Archetype} ({genStr}) [Prio {line.Priority}]";

            float dailyIc = ProductionSystem.CalculateDailyIc(
                activeMilitaryFactories: assigned,
                factoryOutputModifier: factoryMod,
                efficiency: line.Efficiency,
                resourcePenalty: line.ResourcePenalty);
            float costIc = line.EquipmentCostIc > 0.001f
                ? line.EquipmentCostIc
                : ProductionLineComponent.GetDefaultEquipmentCostIc(line.Archetype, line.Generation);
            float dailyUnits = dailyIc / costIc;

            string penStr = line.ResourcePenalty > 0.001f ? $" | ResPen: -{line.ResourcePenalty:P0}" : "";
            row.OutputLabel.Text =
                $"Mil: {line.ActiveMilitaryFactories}/{assigned} | {dailyIc:F1} IC/d -> +{dailyUnits:F2} units/d (Cost {costIc:F1} IC){penStr}";

            row.EfficiencyLabel.Text = $"Eff: {line.Efficiency:P1} (Cap {line.EfficiencyCap:P0})";
            row.EfficiencyBar.Value = Mathf.Clamp(line.Efficiency * 100.0f, 0.0f, 100.0f);
        }

        _summaryLabel.Text =
            $"Mil Assigned: {totalAssigned}/{summary.TotalMilitaryFactories} (Active: {totalActive}) | Steel: {steel} | Semi: {semi} | Oil: {oil} | Alum: {alum}";
    }
}
