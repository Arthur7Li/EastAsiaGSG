using EastAsiaGSG.Core.Commands;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Map;
using Godot;

namespace EastAsiaGSG.GodotClient.UI;

/// <summary>
/// Centered modal dialog providing a 15-nation diplomatic ledger, one-click war declarations,
/// faction status, and a chronological news event ticker.
/// </summary>
public partial class DiplomacyAndNewsModal : PanelContainer
{
    private enum ActiveTab
    {
        Diplomacy = 0,
        News = 1
    }

    private SimulationWorld? _world;
    private MapMetadataRegistry? _metadata;
    private byte _activeCountryTag = CountryTag.ROK;
    private ActiveTab _currentTab = ActiveTab.Diplomacy;

    private Label? _headerLabel;
    private Button? _diplomacyTabBtn;
    private Button? _newsTabBtn;
    private ScrollContainer? _diplomacyScroll;
    private ScrollContainer? _newsScroll;
    private VBoxContainer? _diplomacyListContainer;
    private VBoxContainer? _newsListContainer;

    public void Initialize(SimulationWorld world, byte countryTag, MapMetadataRegistry? metadata = null)
    {
        _world = world;
        _activeCountryTag = countryTag;
        _metadata = metadata;
        Refresh();
    }

    public void SetActiveCountry(byte countryTag)
    {
        _activeCountryTag = countryTag;
        Refresh();
    }

    public override void _Ready()
    {
        Visible = false;
        BuildUi();
        Refresh();
    }

    public void ToggleModal()
    {
        Visible = !Visible;
        if (Visible)
        {
            Refresh();
        }
    }

    public void OpenToTab(int tabIndex)
    {
        _currentTab = tabIndex == 1 ? ActiveTab.News : ActiveTab.Diplomacy;
        Visible = true;
        Refresh();
    }

    private void BuildUi()
    {
        CustomMinimumSize = new Vector2(860, 580);

        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.14f, 0.98f),
            BorderColor = new Color(0.30f, 0.40f, 0.52f, 0.95f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 14,
            ContentMarginBottom = 14
        };
        AddThemeStyleboxOverride("panel", bgStyle);

        var rootVBox = new VBoxContainer();
        rootVBox.AddThemeConstantOverride("separation", 10);
        AddChild(rootVBox);

        // Header Row: Title, Tab Buttons, Close Button
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 12);
        rootVBox.AddChild(headerRow);

        _headerLabel = new Label
        {
            Text = "⚔ THEATRE DIPLOMACY & GLOBAL FLASHPOINTS",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _headerLabel.AddThemeFontSizeOverride("font_size", 14);
        _headerLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.88f, 0.50f));
        headerRow.AddChild(_headerLabel);

        _diplomacyTabBtn = new Button
        {
            Text = "⚔ Diplomatic Ledger",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(150, 28)
        };
        _diplomacyTabBtn.AddThemeFontSizeOverride("font_size", 11);
        _diplomacyTabBtn.Pressed += () =>
        {
            _currentTab = ActiveTab.Diplomacy;
            Refresh();
        };
        headerRow.AddChild(_diplomacyTabBtn);

        _newsTabBtn = new Button
        {
            Text = "📰 Flashpoints Feed",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(140, 28)
        };
        _newsTabBtn.AddThemeFontSizeOverride("font_size", 11);
        _newsTabBtn.Pressed += () =>
        {
            _currentTab = ActiveTab.News;
            Refresh();
        };
        headerRow.AddChild(_newsTabBtn);

        var closeBtn = new Button
        {
            Text = "✕",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(32, 28),
            TooltipText = "Close Modal [Hotkey: D / Esc]"
        };
        closeBtn.AddThemeFontSizeOverride("font_size", 12);
        closeBtn.Pressed += () => Visible = false;
        headerRow.AddChild(closeBtn);

        rootVBox.AddChild(new HSeparator());

        // Body Scroll Containers
        _diplomacyScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        rootVBox.AddChild(_diplomacyScroll);

        _diplomacyListContainer = new VBoxContainer();
        _diplomacyListContainer.AddThemeConstantOverride("separation", 6);
        _diplomacyListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _diplomacyScroll.AddChild(_diplomacyListContainer);

        _newsScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Visible = false
        };
        rootVBox.AddChild(_newsScroll);

        _newsListContainer = new VBoxContainer();
        _newsListContainer.AddThemeConstantOverride("separation", 8);
        _newsListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _newsScroll.AddChild(_newsListContainer);
    }

    public void Refresh()
    {
        if (_world == null) return;

        string countryName = GetCountryFullName(_activeCountryTag);
        if (_headerLabel != null)
        {
            _headerLabel.Text = $"⚔ THEATRE DIPLOMACY & GLOBAL FLASHPOINTS — [{CountryTag.GetCode(_activeCountryTag)}: {countryName}]";
        }

        if (_currentTab == ActiveTab.Diplomacy)
        {
            if (_diplomacyScroll != null) _diplomacyScroll.Visible = true;
            if (_newsScroll != null) _newsScroll.Visible = false;
            RefreshDiplomacyTab();
        }
        else
        {
            if (_diplomacyScroll != null) _diplomacyScroll.Visible = false;
            if (_newsScroll != null) _newsScroll.Visible = true;
            RefreshNewsTab();
        }
    }

    private void RefreshDiplomacyTab()
    {
        if (_diplomacyListContainer == null || _world == null) return;

        foreach (Node child in _diplomacyListContainer.GetChildren())
        {
            child.QueueFree();
        }

        for (byte tag = 0; tag < CountryTag.ActiveCount; tag++)
        {
            ref readonly CountryState country = ref _world.Countries[tag];
            string code = CountryTag.GetCode(tag);
            string fullName = GetCountryFullName(tag);
            string factionName = GetFactionName(country.Faction);
            bool isSelf = tag == _activeCountryTag;
            bool isAtWar = _world.IsAtWarWith(_activeCountryTag, tag);
            bool hasCapitulated = _world.HasCapitulated(tag);

            var rowCard = new PanelContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };

            Color cardBg = isSelf
                ? new Color(0.14f, 0.18f, 0.24f, 0.95f)
                : (isAtWar ? new Color(0.24f, 0.12f, 0.14f, 0.95f) : new Color(0.10f, 0.13f, 0.18f, 0.90f));

            var cardStyle = new StyleBoxFlat
            {
                BgColor = cardBg,
                BorderColor = isAtWar ? new Color(0.85f, 0.25f, 0.25f, 0.90f) : new Color(0.22f, 0.28f, 0.36f, 0.85f),
                BorderWidthLeft = isSelf || isAtWar ? 3 : 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 8,
                ContentMarginBottom = 8
            };
            rowCard.AddThemeStyleboxOverride("panel", cardStyle);

            var rowHBox = new HBoxContainer();
            rowHBox.AddThemeConstantOverride("separation", 14);
            rowCard.AddChild(rowHBox);

            // 1. Tag & Name
            var nameVBox = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) };
            var tagLabel = new Label
            {
                Text = $"[{code}] {fullName}"
            };
            tagLabel.AddThemeFontSizeOverride("font_size", 12);
            tagLabel.AddThemeColorOverride("font_color", isSelf ? new Color(0.96f, 0.88f, 0.50f) : (isAtWar ? new Color(1.0f, 0.45f, 0.45f) : Colors.White));
            nameVBox.AddChild(tagLabel);

            var factionLabel = new Label
            {
                Text = $"Faction: {factionName} | DEFCON {country.Defcon}"
            };
            factionLabel.AddThemeFontSizeOverride("font_size", 10);
            factionLabel.AddThemeColorOverride("font_color", new Color(0.60f, 0.75f, 0.90f));
            nameVBox.AddChild(factionLabel);
            rowHBox.AddChild(nameVBox);

            // 2. Stats
            var statsVBox = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) };
            var stabLabel = new Label
            {
                Text = $"Stability: {country.Stability:P0} | War Support: {country.WarSupport:P0}"
            };
            stabLabel.AddThemeFontSizeOverride("font_size", 10);
            stabLabel.AddThemeColorOverride("font_color", new Color(0.80f, 0.85f, 0.90f));
            statsVBox.AddChild(stabLabel);

            var mpLabel = new Label
            {
                Text = $"Manpower: {country.Manpower:N0} | Off-Map Mil: {country.OffMapMilitaryFactories}"
            };
            mpLabel.AddThemeFontSizeOverride("font_size", 10);
            mpLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.80f, 0.70f));
            statsVBox.AddChild(mpLabel);
            rowHBox.AddChild(statsVBox);

            // 3. Status Badge
            var statusLabel = new Label
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusLabel.AddThemeFontSizeOverride("font_size", 11);

            if (hasCapitulated)
            {
                statusLabel.Text = "🏳 CAPITULATED";
                statusLabel.AddThemeColorOverride("font_color", new Color(0.70f, 0.70f, 0.70f));
            }
            else if (isSelf)
            {
                statusLabel.Text = "★ YOUR NATION";
                statusLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.88f, 0.45f));
            }
            else if (isAtWar)
            {
                statusLabel.Text = "⚔ AT WAR";
                statusLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.35f, 0.35f));
            }
            else
            {
                statusLabel.Text = "🕊 PEACE";
                statusLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 0.65f));
            }
            rowHBox.AddChild(statusLabel);

            // 4. Action Button
            if (!isSelf && !hasCapitulated && !isAtWar)
            {
                byte targetTag = tag;
                var declareWarBtn = new Button
                {
                    Text = "⚔ Declare War",
                    FocusMode = FocusModeEnum.None,
                    CustomMinimumSize = new Vector2(110, 26),
                    TooltipText = $"Declare diplomatic war against {fullName}"
                };
                declareWarBtn.AddThemeFontSizeOverride("font_size", 10);
                declareWarBtn.Pressed += () =>
                {
                    _world.EnqueueCommand(SimulationCommand.CreateDeclareWar(_activeCountryTag, targetTag));
                    Refresh();
                };
                rowHBox.AddChild(declareWarBtn);
            }

            _diplomacyListContainer.AddChild(rowCard);
        }
    }

    private void RefreshNewsTab()
    {
        if (_newsListContainer == null || _world == null) return;

        foreach (Node child in _newsListContainer.GetChildren())
        {
            child.QueueFree();
        }

        int count = _world.TriggeredNewsEventCount;
        if (count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No major global flashpoints triggered yet.\nHistorical focus achievements and national capitulations will appear here.",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyLabel.AddThemeFontSizeOverride("font_size", 12);
            emptyLabel.AddThemeColorOverride("font_color", new Color(0.60f, 0.65f, 0.75f));
            _newsListContainer.AddChild(emptyLabel);
            return;
        }

        int maxEntries = count < SimulationWorld.MaxTriggeredNewsEvents
            ? count
            : SimulationWorld.MaxTriggeredNewsEvents;

        for (int i = count - 1; i >= count - maxEntries; i--)
        {
            int slot = i % SimulationWorld.MaxTriggeredNewsEvents;
            int hash = _world.TriggeredNewsEventHashes[slot];
            byte countryTag = _world.TriggeredNewsEventCountries[slot];

            string title = _metadata?.GetNewsEventTitle(hash) ?? $"Global Flashpoint #{hash:X8}";
            string countryCode = CountryTag.GetCode(countryTag);

            var newsCard = new PanelContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            var cardStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.15f, 0.20f, 0.95f),
                BorderColor = new Color(0.32f, 0.42f, 0.56f, 0.85f),
                BorderWidthLeft = 3,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 14,
                ContentMarginRight = 14,
                ContentMarginTop = 10,
                ContentMarginBottom = 10
            };
            newsCard.AddThemeStyleboxOverride("panel", cardStyle);

            var cardVBox = new VBoxContainer();
            cardVBox.AddThemeConstantOverride("separation", 4);
            newsCard.AddChild(cardVBox);

            var titleRow = new HBoxContainer();
            var iconLabel = new Label { Text = "📰" };
            titleRow.AddChild(iconLabel);

            var titleLabel = new Label
            {
                Text = title,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 12);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.90f, 0.65f));
            titleRow.AddChild(titleLabel);

            var countryBadge = new Label
            {
                Text = $"[{countryCode}]"
            };
            countryBadge.AddThemeFontSizeOverride("font_size", 11);
            countryBadge.AddThemeColorOverride("font_color", new Color(0.40f, 0.80f, 1.0f));
            titleRow.AddChild(countryBadge);
            cardVBox.AddChild(titleRow);

            var metaLabel = new Label
            {
                Text = $"Flashpoint Dispatch #{i + 1} — Sovereign Theatre Update"
            };
            metaLabel.AddThemeFontSizeOverride("font_size", 10);
            metaLabel.AddThemeColorOverride("font_color", new Color(0.60f, 0.70f, 0.80f));
            cardVBox.AddChild(metaLabel);

            _newsListContainer.AddChild(newsCard);
        }
    }

    private static string GetCountryFullName(byte tag) => tag switch
    {
        CountryTag.PRC => "People's Republic of China",
        CountryTag.USA => "United States (USPACOM)",
        CountryTag.SOV => "Soviet Far East (USSR)",
        CountryTag.JPN => "Japan (JSDF)",
        CountryTag.ROK => "Republic of Korea",
        CountryTag.PRK => "DPR of Korea",
        CountryTag.TWN => "Republic of China (Taiwan)",
        CountryTag.VNM => "Socialist Republic of Vietnam",
        CountryTag.PHL => "Republic of the Philippines",
        CountryTag.MNG => "Mongolian People's Republic",
        CountryTag.THA => "Kingdom of Thailand",
        CountryTag.LAO => "Lao People's Democratic Republic",
        CountryTag.KHM => "PR of Kampuchea",
        CountryTag.MMR => "Socialist Republic of Myanmar",
        CountryTag.MYS => "Federation of Malaysia",
        _ => "Unknown Nation"
    };

    private static string GetFactionName(FactionId faction) => faction switch
    {
        FactionId.BlueforPacom => "US PACOM Coalition",
        FactionId.RedforSocialist => "Soviet Socialist Bloc",
        FactionId.PrcSphere => "PRC Independent Sphere",
        _ => "Non-Aligned / Neutral"
    };
}
