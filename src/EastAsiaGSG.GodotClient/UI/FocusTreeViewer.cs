using System.Text;
using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;
using EastAsiaGSG.Data.Loaders;
using EastAsiaGSG.Data.Schemas;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Procedural HOI4-style National Focus Tree Viewer.
/// Auto-lays out focus nodes from <see cref="LoadedFocusTree"/> (gridX, gridY),
/// draws orthogonal prerequisite lines and mutually-exclusive connectors on a custom 2D canvas,
/// and dispatches <see cref="SimulationCommand.CreateSelectFocus"/> via one-way command binding.
/// </summary>
public partial class FocusTreeViewer : PanelContainer
{
    private const float NodeWidth = 176f;
    private const float NodeHeight = 76f;
    private const float DefaultGridStepX = 108f;
    private const float CompactGridStepX = 200f;
    private const float GridStepY = 116f;
    private const float CanvasPaddingX = 28f;
    private const float CanvasPaddingY = 24f;

    private sealed class FocusCardWidgets
    {
        public short FocusIndex;
        public FocusNodeDto Dto = null!;
        public Vector2 Position;
        public PanelContainer CardPanel = null!;
        public StyleBoxFlat CardStyle = null!;
        public Label TitleLabel = null!;
        public Label StatusLabel = null!;
        public ProgressBar ProgressBar = null!;
        public Button SelectOverlayButton = null!;
    }

    /// <summary>
    /// Custom 2D drawing layer rendered behind focus node cards inside the ScrollContainer canvas.
    /// Draws orthogonal prerequisite lines with arrowheads and mutually-exclusive dashed links.
    /// </summary>
    private sealed partial class FocusConnectorCanvas : Control
    {
        public FocusTreeViewer? OwnerViewer { get; set; }

        public override void _Draw()
        {
            OwnerViewer?.DrawConnectors(this);
        }
    }

    private static readonly byte[] MajorTags =
    [
        CountryTag.ROK,
        CountryTag.PRK,
        CountryTag.PRC,
        CountryTag.USA,
        CountryTag.SOV,
        CountryTag.JPN,
        CountryTag.TWN
    ];

    private SimulationWorld? _world;
    private byte _activeCountryTag = CountryTag.ROK;
    private readonly Dictionary<byte, LoadedFocusTree> _treesByCountry = [];
    private readonly Dictionary<byte, short> _selectedFocusByCountry = [];
    private readonly Dictionary<byte, short> _pendingFocusByCountry = [];
    private readonly Dictionary<byte, Button> _countryPillButtons = [];

    private Label? _titleLabel;
    private Label? _activeFocusBannerLabel;
    private ProgressBar? _activeFocusProgressBar;
    private ScrollContainer? _scrollContainer;
    private Control? _graphCanvas;
    private FocusConnectorCanvas? _connectorLayer;

    // Bottom Inspector Widgets
    private Label? _inspectorTitleLabel;
    private Label? _inspectorDescriptionLabel;
    private Label? _inspectorDetailsLabel;
    private Button? _startFocusButton;

    private readonly List<FocusCardWidgets> _cards = [];

    public event Action? CommandEnqueued;
    public event Action? CloseRequested;

    public override void _Ready()
    {
        BuildUi();
        RebuildGraphForActiveCountry();
        Refresh();
    }

    public void Initialize(SimulationWorld world, byte activeCountryTag = CountryTag.ROK)
    {
        _world = world;
        _activeCountryTag = activeCountryTag;
        RebuildGraphForActiveCountry();
        Refresh();
    }

    public void RegisterFocusTree(LoadedFocusTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _treesByCountry[tree.CountryTag] = tree;
        if (!_selectedFocusByCountry.ContainsKey(tree.CountryTag) && tree.Focuses.Count > 0)
        {
            _selectedFocusByCountry[tree.CountryTag] = 0;
        }

        if (tree.CountryTag == _activeCountryTag && IsInsideTree())
        {
            RebuildGraphForActiveCountry();
            Refresh();
        }
    }

    public void SetActiveCountry(byte countryTag)
    {
        _activeCountryTag = countryTag;
        RebuildGraphForActiveCountry();
        Refresh();
    }

    public void ClearPendingPreviews()
    {
        _pendingFocusByCountry.Clear();
    }

    private void BuildUi()
    {
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.09f, 0.13f, 0.97f),
            BorderColor = new Color(0.28f, 0.38f, 0.50f, 0.96f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        AddThemeStyleboxOverride("panel", panelStyle);

        var rootVBox = new VBoxContainer();
        rootVBox.AddThemeConstantOverride("separation", 6);
        AddChild(rootVBox);

        // 1. Header Row
        var headerHBox = new HBoxContainer();
        headerHBox.AddThemeConstantOverride("separation", 10);
        rootVBox.AddChild(headerHBox);

        _titleLabel = new Label
        {
            Text = "National Focus Tree [ROK]",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.86f, 0.42f));
        headerHBox.AddChild(_titleLabel);

        var tagPillsHBox = new HBoxContainer();
        tagPillsHBox.AddThemeConstantOverride("separation", 3);
        headerHBox.AddChild(tagPillsHBox);

        _countryPillButtons.Clear();
        foreach (byte mTag in MajorTags)
        {
            string code = CountryTag.GetCode(mTag);
            var pillBtn = new Button
            {
                Text = code,
                ToggleMode = true,
                ButtonPressed = mTag == _activeCountryTag,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(38, 24),
                TooltipText = $"Inspect [{code}] 1985 Era I Focus Tree"
            };
            pillBtn.AddThemeFontSizeOverride("font_size", 10);
            byte capturedTag = mTag;
            pillBtn.Pressed += () => SetActiveCountry(capturedTag);
            tagPillsHBox.AddChild(pillBtn);
            _countryPillButtons[mTag] = pillBtn;
        }

        var closeBtn = new Button
        {
            Text = " ✕ ",
            FocusMode = FocusModeEnum.None,
            Flat = true,
            TooltipText = "Close Focus Tree [Esc / F]"
        };
        closeBtn.Pressed += () => CloseRequested?.Invoke();
        headerHBox.AddChild(closeBtn);

        // 2. Active Focus Status Strip
        var statusHBox = new HBoxContainer();
        statusHBox.AddThemeConstantOverride("separation", 10);
        rootVBox.AddChild(statusHBox);

        _activeFocusBannerLabel = new Label
        {
            Text = "Active Focus: None | Political Power: 0 (+1.0/day)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _activeFocusBannerLabel.AddThemeFontSizeOverride("font_size", 11);
        _activeFocusBannerLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.88f, 0.98f));
        statusHBox.AddChild(_activeFocusBannerLabel);

        _activeFocusProgressBar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(160, 16),
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            ShowPercentage = true
        };
        statusHBox.AddChild(_activeFocusProgressBar);

        rootVBox.AddChild(new HSeparator());

        // 3. 2D Scrollable Graph Canvas
        _scrollContainer = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        rootVBox.AddChild(_scrollContainer);

        _graphCanvas = new Control
        {
            CustomMinimumSize = new Vector2(960, 420)
        };
        _scrollContainer.AddChild(_graphCanvas);

        _connectorLayer = new FocusConnectorCanvas
        {
            OwnerViewer = this,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _graphCanvas.AddChild(_connectorLayer);

        rootVBox.AddChild(new HSeparator());

        // 4. Bottom Focus Detail Inspector & Action Bar
        var inspectorPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 122)
        };
        var inspectorStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.13f, 0.18f, 0.96f),
            BorderColor = new Color(0.24f, 0.32f, 0.44f, 0.95f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        inspectorPanel.AddThemeStyleboxOverride("panel", inspectorStyle);
        rootVBox.AddChild(inspectorPanel);

        var inspectorHBox = new HBoxContainer();
        inspectorHBox.AddThemeConstantOverride("separation", 12);
        inspectorPanel.AddChild(inspectorHBox);

        var detailsVBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        detailsVBox.AddThemeConstantOverride("separation", 3);
        inspectorHBox.AddChild(detailsVBox);

        _inspectorTitleLabel = new Label
        {
            Text = "Select a National Focus Node"
        };
        _inspectorTitleLabel.AddThemeFontSizeOverride("font_size", 13);
        _inspectorTitleLabel.AddThemeColorOverride("font_color", new Color(0.98f, 0.90f, 0.50f));
        detailsVBox.AddChild(_inspectorTitleLabel);

        _inspectorDescriptionLabel = new Label
        {
            Text = "Click any focus node above to inspect its strategic prerequisites, conditions, and national effects.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _inspectorDescriptionLabel.AddThemeFontSizeOverride("font_size", 11);
        _inspectorDescriptionLabel.AddThemeColorOverride("font_color", new Color(0.82f, 0.86f, 0.92f));
        detailsVBox.AddChild(_inspectorDescriptionLabel);

        _inspectorDetailsLabel = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _inspectorDetailsLabel.AddThemeFontSizeOverride("font_size", 11);
        _inspectorDetailsLabel.AddThemeColorOverride("font_color", new Color(0.58f, 0.90f, 0.72f));
        detailsVBox.AddChild(_inspectorDetailsLabel);

        var actionVBox = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        inspectorHBox.AddChild(actionVBox);

        _startFocusButton = new Button
        {
            Text = "▶ Start Focus",
            CustomMinimumSize = new Vector2(148, 42),
            FocusMode = FocusModeEnum.None,
            Disabled = true
        };
        _startFocusButton.AddThemeFontSizeOverride("font_size", 12);
        _startFocusButton.Pressed += OnStartSelectedFocusPressed;
        actionVBox.AddChild(_startFocusButton);
    }

    private void RebuildGraphForActiveCountry()
    {
        if (_graphCanvas == null || _connectorLayer == null)
        {
            return;
        }

        foreach (FocusCardWidgets card in _cards)
        {
            card.CardPanel.QueueFree();
        }
        _cards.Clear();

        if (!_treesByCountry.TryGetValue(_activeCountryTag, out LoadedFocusTree? tree) || tree.Focuses.Count == 0)
        {
            _graphCanvas.CustomMinimumSize = new Vector2(680, 320);
            _connectorLayer.Size = _graphCanvas.CustomMinimumSize;
            _connectorLayer.QueueRedraw();
            return;
        }

        // Determine horizontal grid step so even trees with adjacent GridX (dx=1) never overlap
        float gridStepX = ComputeOptimalGridStepX(tree.Focuses);

        float maxRight = 680f;
        float maxBottom = 360f;

        for (short i = 0; i < tree.Focuses.Count; i++)
        {
            FocusNodeDto dto = tree.Focuses[i];
            float posX = CanvasPaddingX + (dto.GridX * gridStepX);
            float posY = CanvasPaddingY + (dto.GridY * GridStepY);

            maxRight = MathF.Max(maxRight, posX + NodeWidth + CanvasPaddingX);
            maxBottom = MathF.Max(maxBottom, posY + NodeHeight + CanvasPaddingY);

            FocusCardWidgets card = CreateFocusCard(i, dto, new Vector2(posX, posY));
            _graphCanvas.AddChild(card.CardPanel);
            _cards.Add(card);
        }

        var canvasSize = new Vector2(maxRight, maxBottom);
        _graphCanvas.CustomMinimumSize = canvasSize;
        _graphCanvas.Size = canvasSize;
        _connectorLayer.Position = Vector2.Zero;
        _connectorLayer.Size = canvasSize;
        _connectorLayer.QueueRedraw();

        if (!_selectedFocusByCountry.ContainsKey(_activeCountryTag))
        {
            _selectedFocusByCountry[_activeCountryTag] = 0;
        }
    }

    private static float ComputeOptimalGridStepX(IReadOnlyList<FocusNodeDto> focuses)
    {
        int minPositiveDxOnSameRow = int.MaxValue;
        for (int i = 0; i < focuses.Count; i++)
        {
            for (int j = i + 1; j < focuses.Count; j++)
            {
                if (focuses[i].GridY == focuses[j].GridY)
                {
                    int dx = Math.Abs(focuses[i].GridX - focuses[j].GridX);
                    if (dx > 0 && dx < minPositiveDxOnSameRow)
                    {
                        minPositiveDxOnSameRow = dx;
                    }
                }
            }
        }

        return minPositiveDxOnSameRow == 1 ? CompactGridStepX : DefaultGridStepX;
    }

    private FocusCardWidgets CreateFocusCard(short focusIndex, FocusNodeDto dto, Vector2 position)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.16f, 0.22f, 0.96f),
            BorderColor = new Color(0.32f, 0.42f, 0.54f, 0.95f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };

        var cardPanel = new PanelContainer
        {
            Position = position,
            CustomMinimumSize = new Vector2(NodeWidth, NodeHeight),
            Size = new Vector2(NodeWidth, NodeHeight)
        };
        cardPanel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        vbox.AddThemeConstantOverride("separation", 3);
        cardPanel.AddChild(vbox);

        var titleLabel = new Label
        {
            Text = dto.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(NodeWidth - 18f, 32f),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 11);
        titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.96f, 0.98f));
        vbox.AddChild(titleLabel);

        var progressBar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 6),
            MinValue = 0,
            MaxValue = Math.Max(1, (int)dto.CostDays),
            Value = 0,
            ShowPercentage = false
        };
        vbox.AddChild(progressBar);

        var statusLabel = new Label
        {
            Text = $"{dto.CostDays} days",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        statusLabel.AddThemeFontSizeOverride("font_size", 10);
        statusLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.80f, 0.92f));
        vbox.AddChild(statusLabel);

        // Transparent overlay button so clicking anywhere on the card selects the focus
        var overlayBtn = new Button
        {
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = $"{dto.Name} ({dto.CostDays} days)\n{dto.Description}"
        };
        overlayBtn.SetAnchorsPreset(LayoutPreset.FullRect);
        short capturedIndex = focusIndex;
        overlayBtn.Pressed += () =>
        {
            _selectedFocusByCountry[_activeCountryTag] = capturedIndex;
            Refresh();
        };
        cardPanel.AddChild(overlayBtn);

        return new FocusCardWidgets
        {
            FocusIndex = focusIndex,
            Dto = dto,
            Position = position,
            CardPanel = cardPanel,
            CardStyle = style,
            TitleLabel = titleLabel,
            StatusLabel = statusLabel,
            ProgressBar = progressBar,
            SelectOverlayButton = overlayBtn
        };
    }

    private void DrawConnectors(Control canvas)
    {
        if (_world == null ||
            !_treesByCountry.TryGetValue(_activeCountryTag, out LoadedFocusTree? tree) ||
            _cards.Count == 0)
        {
            return;
        }

        ref readonly CountryState country = ref _world.Countries[_activeCountryTag];

        // 1. Draw Prerequisite Lines (Orthogonal parent bottom-center -> child top-center)
        for (int i = 0; i < _cards.Count; i++)
        {
            FocusCardWidgets childCard = _cards[i];
            if (childCard.Dto.Prerequisites == null)
            {
                continue;
            }

            Vector2 childTop = new(
                childCard.Position.X + (NodeWidth * 0.5f),
                childCard.Position.Y);

            foreach (string prereqId in childCard.Dto.Prerequisites)
            {
                short prereqIdx = tree.GetFocusIndex(prereqId);
                if (prereqIdx < 0 || prereqIdx >= _cards.Count)
                {
                    continue;
                }

                FocusCardWidgets parentCard = _cards[prereqIdx];
                Vector2 parentBottom = new(
                    parentCard.Position.X + (NodeWidth * 0.5f),
                    parentCard.Position.Y + NodeHeight);

                bool prereqCompleted = country.HasCompletedFocus(prereqIdx);
                Color lineColor = prereqCompleted
                    ? new Color(0.32f, 0.86f, 0.54f, 0.95f)
                    : new Color(0.42f, 0.54f, 0.68f, 0.85f);
                float lineWidth = prereqCompleted ? 2.5f : 2.0f;

                float midY = (parentBottom.Y + childTop.Y) * 0.5f;
                Vector2 p1 = new(parentBottom.X, midY);
                Vector2 p2 = new(childTop.X, midY);

                canvas.DrawLine(parentBottom, p1, lineColor, lineWidth);
                canvas.DrawLine(p1, p2, lineColor, lineWidth);
                canvas.DrawLine(p2, childTop, lineColor, lineWidth);

                // Draw downward arrowhead at childTop
                Vector2[] arrow =
                [
                    childTop,
                    new Vector2(childTop.X - 5f, childTop.Y - 7f),
                    new Vector2(childTop.X + 5f, childTop.Y - 7f)
                ];
                canvas.DrawColoredPolygon(arrow, lineColor);
            }
        }

        // 2. Draw Mutually Exclusive Connectors (Horizontal dashed crimson/amber line with midpoint diamond)
        for (int i = 0; i < _cards.Count; i++)
        {
            FocusCardWidgets cardA = _cards[i];
            if (cardA.Dto.MutuallyExclusive == null)
            {
                continue;
            }

            foreach (string exclId in cardA.Dto.MutuallyExclusive)
            {
                short exclIdx = tree.GetFocusIndex(exclId);
                if (exclIdx <= i || exclIdx >= _cards.Count)
                {
                    continue;
                }

                FocusCardWidgets cardB = _cards[exclIdx];
                FocusCardWidgets leftCard = cardA.Position.X <= cardB.Position.X ? cardA : cardB;
                FocusCardWidgets rightCard = cardA.Position.X <= cardB.Position.X ? cardB : cardA;

                Vector2 start = new(
                    leftCard.Position.X + NodeWidth,
                    leftCard.Position.Y + (NodeHeight * 0.5f));
                Vector2 end = new(
                    rightCard.Position.X,
                    rightCard.Position.Y + (NodeHeight * 0.5f));

                Color exclColor = new(0.92f, 0.36f, 0.34f, 0.90f);
                canvas.DrawDashedLine(start, end, exclColor, width: 2.0f, dash: 5.0f);

                Vector2 mid = (start + end) * 0.5f;
                Vector2[] diamond =
                [
                    new Vector2(mid.X, mid.Y - 6f),
                    new Vector2(mid.X + 6f, mid.Y),
                    new Vector2(mid.X, mid.Y + 6f),
                    new Vector2(mid.X - 6f, mid.Y)
                ];
                canvas.DrawColoredPolygon(diamond, exclColor);
            }
        }
    }

    public void Refresh()
    {
        if (_world == null || _titleLabel == null || _activeFocusBannerLabel == null)
        {
            return;
        }

        string countryCode = CountryTag.GetCode(_activeCountryTag);
        ref readonly CountryState country = ref _world.Countries[_activeCountryTag];

        foreach (var (pillTag, pillBtn) in _countryPillButtons)
        {
            pillBtn.SetPressedNoSignal(pillTag == _activeCountryTag);
        }

        if (!_treesByCountry.TryGetValue(_activeCountryTag, out LoadedFocusTree? tree))
        {
            _titleLabel.Text = $"National Focus Tree [{countryCode}] — No Tree Loaded";
            _activeFocusBannerLabel.Text = $"Political Power: {country.PoliticalPower:F0} (+1.0/day)";
            return;
        }

        _titleLabel.Text = $"National Focus Tree — [{countryCode}] ({tree.Era})";

        short activeIdx = country.ActiveFocusId;
        bool hasPending = _pendingFocusByCountry.TryGetValue(_activeCountryTag, out short pendingIdx);

        if (hasPending && pendingIdx >= 0 && pendingIdx < tree.Focuses.Count)
        {
            FocusNodeDto pendingDto = tree.Focuses[pendingIdx];
            _activeFocusBannerLabel.Text =
                $"Queued Focus: {pendingDto.Name} ({pendingDto.CostDays}d) | PP: {country.PoliticalPower:F0} (+1.0/day)";
            if (_activeFocusProgressBar != null)
            {
                _activeFocusProgressBar.MaxValue = Math.Max(1, (int)pendingDto.CostDays);
                _activeFocusProgressBar.Value = 0;
            }
        }
        else if (activeIdx >= 0 && activeIdx < tree.Focuses.Count)
        {
            FocusNodeDto activeDto = tree.Focuses[activeIdx];
            int totalDays = Math.Max(1, (int)activeDto.CostDays);
            int elapsed = Math.Clamp(totalDays - country.FocusDaysRemaining, 0, totalDays);
            _activeFocusBannerLabel.Text =
                $"Active Focus: {activeDto.Name} ({country.FocusDaysRemaining}d remaining) | PP: {country.PoliticalPower:F0} (+1.0/day)";
            if (_activeFocusProgressBar != null)
            {
                _activeFocusProgressBar.MaxValue = totalDays;
                _activeFocusProgressBar.Value = elapsed;
            }
        }
        else
        {
            _activeFocusBannerLabel.Text =
                $"Active Focus: None Selected | Political Power: {country.PoliticalPower:F0} (+1.0/day)";
            if (_activeFocusProgressBar != null)
            {
                _activeFocusProgressBar.MaxValue = 100;
                _activeFocusProgressBar.Value = 0;
            }
        }

        short selectedIdx = _selectedFocusByCountry.GetValueOrDefault(_activeCountryTag, (short)0);

        // Update each Focus Card's visual state
        for (int i = 0; i < _cards.Count; i++)
        {
            FocusCardWidgets card = _cards[i];
            short idx = card.FocusIndex;
            ref readonly FocusNodeData nodeData = ref _world.GetFocusNode(_activeCountryTag, idx);

            bool isSelected = idx == selectedIdx;
            bool isCompleted = country.HasCompletedFocus(idx);
            bool isActive = activeIdx == idx;
            bool isQueued = hasPending && pendingIdx == idx;
            bool isExcluded = IsMutuallyExcluded(in country, in nodeData, activeIdx, hasPending ? pendingIdx : (short)-1);
            bool canStart = FocusTreeSystem.CanStartFocus(_world, _activeCountryTag, idx);

            card.ProgressBar.MaxValue = Math.Max(1, (int)card.Dto.CostDays);

            if (isCompleted)
            {
                card.CardStyle.BgColor = new Color(0.10f, 0.24f, 0.16f, 0.96f);
                card.CardStyle.BorderColor = isSelected
                    ? new Color(0.55f, 0.98f, 0.72f, 1.0f)
                    : new Color(0.24f, 0.74f, 0.44f, 0.95f);
                card.StatusLabel.Text = "✓ COMPLETED";
                card.StatusLabel.AddThemeColorOverride("font_color", new Color(0.48f, 0.96f, 0.66f));
                card.ProgressBar.Value = card.ProgressBar.MaxValue;
            }
            else if (isQueued)
            {
                card.CardStyle.BgColor = new Color(0.12f, 0.22f, 0.30f, 0.96f);
                card.CardStyle.BorderColor = new Color(0.38f, 0.88f, 0.98f, 1.0f);
                card.StatusLabel.Text = $"⏳ QUEUED ({card.Dto.CostDays}d)";
                card.StatusLabel.AddThemeColorOverride("font_color", new Color(0.48f, 0.92f, 0.98f));
                card.ProgressBar.Value = 0;
            }
            else if (isActive)
            {
                int elapsed = Math.Clamp(card.Dto.CostDays - country.FocusDaysRemaining, 0, card.Dto.CostDays);
                card.CardStyle.BgColor = new Color(0.24f, 0.20f, 0.09f, 0.96f);
                card.CardStyle.BorderColor = new Color(0.98f, 0.82f, 0.28f, 1.0f);
                card.StatusLabel.Text = $"⏳ {country.FocusDaysRemaining}d left";
                card.StatusLabel.AddThemeColorOverride("font_color", new Color(0.98f, 0.86f, 0.36f));
                card.ProgressBar.Value = elapsed;
            }
            else if (isExcluded)
            {
                card.CardStyle.BgColor = new Color(0.18f, 0.10f, 0.12f, 0.92f);
                card.CardStyle.BorderColor = isSelected
                    ? new Color(0.92f, 0.44f, 0.44f, 1.0f)
                    : new Color(0.58f, 0.22f, 0.24f, 0.90f);
                card.StatusLabel.Text = "✕ EXCLUDED";
                card.StatusLabel.AddThemeColorOverride("font_color", new Color(0.90f, 0.45f, 0.45f));
                card.ProgressBar.Value = 0;
            }
            else if (canStart)
            {
                card.CardStyle.BgColor = new Color(0.12f, 0.19f, 0.29f, 0.96f);
                card.CardStyle.BorderColor = isSelected
                    ? new Color(0.98f, 0.90f, 0.46f, 1.0f)
                    : new Color(0.36f, 0.68f, 0.96f, 0.96f);
                card.StatusLabel.Text = $"▶ AVAILABLE ({card.Dto.CostDays}d)";
                card.StatusLabel.AddThemeColorOverride("font_color", new Color(0.56f, 0.84f, 0.98f));
                card.ProgressBar.Value = 0;
            }
            else
            {
                card.CardStyle.BgColor = new Color(0.11f, 0.13f, 0.17f, 0.94f);
                card.CardStyle.BorderColor = isSelected
                    ? new Color(0.78f, 0.84f, 0.92f, 1.0f)
                    : new Color(0.28f, 0.34f, 0.42f, 0.90f);
                card.StatusLabel.Text = $"🔒 LOCKED ({card.Dto.CostDays}d)";
                card.StatusLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.68f, 0.76f));
                card.ProgressBar.Value = 0;
            }

            int borderWidth = isSelected ? 3 : 2;
            card.CardStyle.BorderWidthLeft = borderWidth;
            card.CardStyle.BorderWidthTop = borderWidth;
            card.CardStyle.BorderWidthRight = borderWidth;
            card.CardStyle.BorderWidthBottom = borderWidth;
        }

        _connectorLayer?.QueueRedraw();
        RefreshInspector(tree, in country, selectedIdx, activeIdx, hasPending ? pendingIdx : (short)-1);
    }

    private unsafe static bool IsMutuallyExcluded(
        in CountryState country,
        in FocusNodeData node,
        short activeIdx,
        short pendingIdx)
    {
        for (int i = 0; i < node.MutuallyExclusiveCount; i++)
        {
            short exclIdx = node.MutuallyExclusive[i];
            if (exclIdx >= 0 && (country.HasCompletedFocus(exclIdx) || activeIdx == exclIdx || pendingIdx == exclIdx))
            {
                return true;
            }
        }
        return false;
    }

    private void RefreshInspector(
        LoadedFocusTree tree,
        in CountryState country,
        short selectedIdx,
        short activeIdx,
        short pendingIdx)
    {
        if (_world == null ||
            _inspectorTitleLabel == null ||
            _inspectorDescriptionLabel == null ||
            _inspectorDetailsLabel == null ||
            _startFocusButton == null)
        {
            return;
        }

        if (selectedIdx < 0 || selectedIdx >= tree.Focuses.Count)
        {
            _inspectorTitleLabel.Text = "Select a National Focus Node";
            _inspectorDescriptionLabel.Text = "Click any focus node above to inspect its prerequisites and effects.";
            _inspectorDetailsLabel.Text = "";
            _startFocusButton.Disabled = true;
            return;
        }

        FocusNodeDto dto = tree.Focuses[selectedIdx];
        bool isCompleted = country.HasCompletedFocus(selectedIdx);
        bool isActive = activeIdx == selectedIdx;
        bool isQueued = pendingIdx == selectedIdx;
        bool canStart = FocusTreeSystem.CanStartFocus(_world, _activeCountryTag, selectedIdx) && pendingIdx < 0;

        _inspectorTitleLabel.Text = $"{dto.Name}  —  [{dto.CostDays} Days]";
        _inspectorDescriptionLabel.Text = dto.Description;
        _inspectorDetailsLabel.Text = FormatRequirementsAndEffects(dto);

        if (isCompleted)
        {
            _startFocusButton.Text = "✓ Completed";
            _startFocusButton.Disabled = true;
        }
        else if (isQueued)
        {
            _startFocusButton.Text = "⏳ Queued";
            _startFocusButton.Disabled = true;
        }
        else if (isActive)
        {
            _startFocusButton.Text = $"⏳ Active ({country.FocusDaysRemaining}d)";
            _startFocusButton.Disabled = true;
        }
        else if (canStart)
        {
            _startFocusButton.Text = $"▶ Start ({dto.CostDays}d)";
            _startFocusButton.Disabled = false;
        }
        else
        {
            _startFocusButton.Text = activeIdx >= 0 || pendingIdx >= 0 ? "Focus Busy" : "🔒 Locked";
            _startFocusButton.Disabled = true;
        }
    }

    private static string FormatRequirementsAndEffects(FocusNodeDto dto)
    {
        var sb = new StringBuilder();

        if (dto.Prerequisites is { Count: > 0 })
        {
            sb.Append("Requires: ").Append(string.Join(", ", dto.Prerequisites)).Append("   ");
        }

        if (dto.Conditions is { Count: > 0 })
        {
            sb.Append("Conditions: ");
            for (int i = 0; i < dto.Conditions.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                FocusConditionDto c = dto.Conditions[i];
                sb.Append($"{c.Type}({c.Target}{(c.Value != 0 ? $":{c.Value}" : "")})");
            }
            sb.Append("   ");
        }

        if (dto.Effects is { Count: > 0 })
        {
            sb.Append("Effects: ");
            for (int i = 0; i < dto.Effects.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                FocusEffectDto e = dto.Effects[i];
                string sign = e.Value >= 0 ? "+" : "";
                sb.Append($"{e.Type} [{e.Target}: {sign}{e.Value:0.##}]");
            }
        }

        return sb.ToString();
    }

    private void OnStartSelectedFocusPressed()
    {
        if (_world == null || !_treesByCountry.TryGetValue(_activeCountryTag, out LoadedFocusTree? tree))
        {
            return;
        }

        short selectedIdx = _selectedFocusByCountry.GetValueOrDefault(_activeCountryTag, (short)-1);
        if (selectedIdx < 0 || selectedIdx >= tree.Focuses.Count)
        {
            return;
        }

        if (!FocusTreeSystem.CanStartFocus(_world, _activeCountryTag, selectedIdx))
        {
            return;
        }

        ref readonly FocusNodeData node = ref _world.GetFocusNode(_activeCountryTag, selectedIdx);
        _world.EnqueueCommand(SimulationCommand.CreateSelectFocus(_activeCountryTag, selectedIdx, node.CostDays));
        _pendingFocusByCountry[_activeCountryTag] = selectedIdx;

        Refresh();
        CommandEnqueued?.Invoke();
    }
}
