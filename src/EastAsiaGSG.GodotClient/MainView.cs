using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Loaders;
using EastAsiaGSG.Data.Map;
using EastAsiaGSG.GodotClient.Map;
using EastAsiaGSG.GodotClient.Tactics;
using EastAsiaGSG.GodotClient.UI;
using Godot;

namespace EastAsiaGSG.GodotClient;

/// <summary>
/// Root presentation scene controller initializing the headless simulation world,
/// the GPU MapRenderer, the 2D RTS CameraController, the TacticalMapOverlay,
/// the TimeControlsToolbar, and the interactive HUD overlays.
/// </summary>
public partial class MainView : Node2D
{
    private SimulationWorld? _world;

    private MapRenderer? _mapRenderer;
    private CameraController? _camera;
    private TacticalMapOverlay? _tacticalOverlay;
    private MapModeToolbar? _toolbar;
    private ProvinceInfoPanel? _infoPanel;
    private TimeControlsToolbar? _timeToolbar;
    private CombatTelemetryPopup? _combatPopup;
    private NationalStockpileTopBar? _stockpileTopBar;
    private ConstructionQueuePanel? _constructionPanel;
    private ProductionLinePanel? _productionPanel;

    public override void _Ready()
    {
        // 1. Initialize Headless Simulation World
        _world = new SimulationWorld();

        string? statesPath = ResolvePath("data/map/states.json");
        string? provincesPath = ResolvePath("data/map/provinces.json");
        string? provincesPngPath = ResolvePath("data/map/provinces.png");

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
        _mapRenderer.Initialize(_world, provincesPngPath);

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

        // 4. Setup TacticalMapOverlay (On-map Division Counters, Movement Arrows, Battle Bubbles)
        _tacticalOverlay = GetNodeOrNull<TacticalMapOverlay>("TacticalMapOverlay");
        if (_tacticalOverlay == null)
        {
            _tacticalOverlay = new TacticalMapOverlay { Name = "TacticalMapOverlay" };
            AddChild(_tacticalOverlay);
        }
        _tacticalOverlay.Initialize(_world, _mapRenderer, _camera);

        // 4.5. Seed Starting 1985 Historical Divisions, Stockpiles & Production Lines
        SpawnStartingSandboxDivisions();

        // 5. Setup HUD CanvasLayer
        var hudLayer = GetNodeOrNull<CanvasLayer>("HUD");
        if (hudLayer == null)
        {
            hudLayer = new CanvasLayer { Name = "HUD" };
            AddChild(hudLayer);
        }

        // Setup NationalStockpileTopBar at top-left
        _stockpileTopBar = hudLayer.GetNodeOrNull<NationalStockpileTopBar>("NationalStockpileTopBar");
        if (_stockpileTopBar == null)
        {
            _stockpileTopBar = new NationalStockpileTopBar { Name = "NationalStockpileTopBar" };
            hudLayer.AddChild(_stockpileTopBar);
        }
        _stockpileTopBar.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _stockpileTopBar.GrowHorizontal = Control.GrowDirection.End;
        _stockpileTopBar.GrowVertical = Control.GrowDirection.End;
        _stockpileTopBar.OffsetLeft = 20;
        _stockpileTopBar.OffsetTop = 16;
        _stockpileTopBar.OffsetRight = 760;
        _stockpileTopBar.OffsetBottom = 66;
        _stockpileTopBar.Initialize(_world, CountryTag.ROK);

        // Setup MapModeToolbar at bottom-right
        _toolbar = hudLayer.GetNodeOrNull<MapModeToolbar>("MapModeToolbar");
        if (_toolbar == null)
        {
            _toolbar = new MapModeToolbar { Name = "MapModeToolbar" };
            hudLayer.AddChild(_toolbar);
        }
        _toolbar.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _toolbar.GrowHorizontal = Control.GrowDirection.Begin;
        _toolbar.GrowVertical = Control.GrowDirection.Begin;
        _toolbar.OffsetLeft = -620;
        _toolbar.OffsetTop = -60;
        _toolbar.OffsetRight = -20;
        _toolbar.OffsetBottom = -15;
        _toolbar.ModeSelected += mode => _mapRenderer.SetMapMode(mode);

        // Setup ProvinceInfoPanel at top-left below NationalStockpileTopBar
        _infoPanel = hudLayer.GetNodeOrNull<ProvinceInfoPanel>("ProvinceInfoPanel");
        if (_infoPanel == null)
        {
            _infoPanel = new ProvinceInfoPanel { Name = "ProvinceInfoPanel" };
            hudLayer.AddChild(_infoPanel);
        }
        _infoPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _infoPanel.GrowHorizontal = Control.GrowDirection.End;
        _infoPanel.GrowVertical = Control.GrowDirection.End;
        _infoPanel.OffsetLeft = 20;
        _infoPanel.OffsetTop = 74;
        _infoPanel.OffsetRight = 360;
        _infoPanel.OffsetBottom = 334;
        _infoPanel.Initialize(_world);

        // Setup TimeControlsToolbar at top-right
        _timeToolbar = hudLayer.GetNodeOrNull<TimeControlsToolbar>("TimeControlsToolbar");
        if (_timeToolbar == null)
        {
            _timeToolbar = new TimeControlsToolbar { Name = "TimeControlsToolbar" };
            hudLayer.AddChild(_timeToolbar);
        }
        _timeToolbar.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _timeToolbar.GrowHorizontal = Control.GrowDirection.Begin;
        _timeToolbar.GrowVertical = Control.GrowDirection.End;
        _timeToolbar.OffsetLeft = -620;
        _timeToolbar.OffsetTop = 16;
        _timeToolbar.OffsetRight = -20;
        _timeToolbar.OffsetBottom = 64;
        _timeToolbar.Initialize(_world);

        // Setup ConstructionQueuePanel at bottom-left
        _constructionPanel = hudLayer.GetNodeOrNull<ConstructionQueuePanel>("ConstructionQueuePanel");
        if (_constructionPanel == null)
        {
            _constructionPanel = new ConstructionQueuePanel { Name = "ConstructionQueuePanel" };
            hudLayer.AddChild(_constructionPanel);
        }
        _constructionPanel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _constructionPanel.GrowHorizontal = Control.GrowDirection.End;
        _constructionPanel.GrowVertical = Control.GrowDirection.Begin;
        _constructionPanel.OffsetLeft = 20;
        _constructionPanel.OffsetTop = -345;
        _constructionPanel.OffsetRight = 445;
        _constructionPanel.OffsetBottom = -15;
        _constructionPanel.Initialize(_world, CountryTag.ROK);

        // Setup ProductionLinePanel at bottom-center-left
        _productionPanel = hudLayer.GetNodeOrNull<ProductionLinePanel>("ProductionLinePanel");
        if (_productionPanel == null)
        {
            _productionPanel = new ProductionLinePanel { Name = "ProductionLinePanel" };
            hudLayer.AddChild(_productionPanel);
        }
        _productionPanel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _productionPanel.GrowHorizontal = Control.GrowDirection.End;
        _productionPanel.GrowVertical = Control.GrowDirection.Begin;
        _productionPanel.OffsetLeft = 458;
        _productionPanel.OffsetTop = -345;
        _productionPanel.OffsetRight = 895;
        _productionPanel.OffsetBottom = -15;
        _productionPanel.Initialize(_world, CountryTag.ROK);

        // Setup CombatTelemetryPopup at center-right
        _combatPopup = hudLayer.GetNodeOrNull<CombatTelemetryPopup>("CombatTelemetryPopup");
        if (_combatPopup == null)
        {
            _combatPopup = new CombatTelemetryPopup { Name = "CombatTelemetryPopup" };
            hudLayer.AddChild(_combatPopup);
        }
        _combatPopup.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        _combatPopup.GrowHorizontal = Control.GrowDirection.Begin;
        _combatPopup.GrowVertical = Control.GrowDirection.Both;
        _combatPopup.OffsetLeft = -360;
        _combatPopup.OffsetTop = -140;
        _combatPopup.OffsetRight = -20;
        _combatPopup.OffsetBottom = 140;

        // Wire TopBar drawer toggles & country switching
        _stockpileTopBar.ActiveCountryChanged += tag =>
        {
            _constructionPanel.SetActiveCountry(tag);
            _productionPanel.SetActiveCountry(tag);
        };
        _stockpileTopBar.ToggleConstructionRequested += () =>
        {
            _constructionPanel.Visible = !_constructionPanel.Visible;
            _stockpileTopBar.SetDrawerStates(_constructionPanel.Visible, _productionPanel.Visible);
        };
        _stockpileTopBar.ToggleProductionRequested += () =>
        {
            _productionPanel.Visible = !_productionPanel.Visible;
            _stockpileTopBar.SetDrawerStates(_constructionPanel.Visible, _productionPanel.Visible);
        };
        _constructionPanel.CommandEnqueued += () => _stockpileTopBar.Refresh();
        _productionPanel.CommandEnqueued += () => _stockpileTopBar.Refresh();

        // Wire map interactions to info panel
        _mapRenderer.ProvinceHovered += id => _infoPanel.DisplayProvince(id);
        _mapRenderer.ProvinceSelected += id => _infoPanel.DisplayProvince(id);

        // Wire tactical overlay interactions to HUD
        _tacticalOverlay.DivisionSelected += div => _infoPanel.DisplaySelectedDivision(div);
        _tacticalOverlay.DivisionDeselected += () => _infoPanel.DisplaySelectedDivision(null);
        _tacticalOverlay.CombatSelected += combat => _combatPopup.DisplayCombat(_world, combat.CombatId);

        // Wire time controls tick event to refresh map colors, tactical overlay, combat telemetry, and industrial HUD
        _timeToolbar.Ticked += () =>
        {
            _mapRenderer.RefreshMapLut();
            _combatPopup?.RefreshIfActive();
            _tacticalOverlay?.QueueRedraw();
            _constructionPanel?.ClearPendingPreviews();
            _productionPanel?.ClearPendingPreviews();
            _stockpileTopBar?.Refresh();
            _constructionPanel?.Refresh();
            _productionPanel?.Refresh();
        };
    }

    private void SpawnStartingSandboxDivisions()
    {
        if (_world == null)
        {
            return;
        }

        // Grant ROK & PRK starting 1985 equipment stockpiles and manpower
        ref CountryState rok = ref _world.Countries[CountryTag.ROK];
        rok.Manpower = 420_000;
        rok.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 2_400);
        rok.SetStockpile(EquipmentType.SupportVehicles, TechGeneration.Gen1_1980Baseline, 450);
        rok.SetStockpile(EquipmentType.IFV, TechGeneration.Gen1_1980Baseline, 320);
        rok.SetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline, 180);
        rok.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 260);
        rok.SetStockpile(EquipmentType.AttackHeli, TechGeneration.Gen1_1980Baseline, 45);
        rok.SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 90);
        rok.SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 120);

        ref CountryState prk = ref _world.Countries[CountryTag.PRK];
        prk.Manpower = 580_000;
        prk.SetStockpile(EquipmentType.InfantryKit, TechGeneration.Gen1_1980Baseline, 3_800);
        prk.SetStockpile(EquipmentType.SupportVehicles, TechGeneration.Gen1_1980Baseline, 380);
        prk.SetStockpile(EquipmentType.IFV, TechGeneration.Gen1_1980Baseline, 240);
        prk.SetStockpile(EquipmentType.MBT, TechGeneration.Gen1_1980Baseline, 290);
        prk.SetStockpile(EquipmentType.Artillery, TechGeneration.Gen1_1980Baseline, 520);
        prk.SetStockpile(EquipmentType.TacticalAir, TechGeneration.Gen1_1980Baseline, 110);
        prk.SetStockpile(EquipmentType.PGM, TechGeneration.Gen1_1980Baseline, 40);

        // Spawn 1985 ROK Military Production Lines (23 Military Factories across States 0..4)
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 101,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.InfantryKit,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 6,
            priority: 200,
            initialEfficiency: 0.65f,
            efficiencyCap: 0.85f));
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 102,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.IFV,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 5,
            priority: 180,
            initialEfficiency: 0.55f,
            efficiencyCap: 0.85f));
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 103,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.MBT,
            generation: TechGeneration.Gen2_1988LateColdWar,
            assignedMilitaryFactories: 5,
            priority: 160,
            initialEfficiency: 0.45f,
            efficiencyCap: 0.85f));
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 104,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.Artillery,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 4,
            priority: 140,
            initialEfficiency: 0.60f,
            efficiencyCap: 0.85f));
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 105,
            countryTag: CountryTag.ROK,
            archetype: EquipmentType.PGM,
            generation: TechGeneration.Gen2_1988LateColdWar,
            assignedMilitaryFactories: 3,
            priority: 120,
            initialEfficiency: 0.50f,
            efficiencyCap: 0.85f));

        // Spawn 1985 PRK Military Production Lines (34 Military Factories across States 5..9)
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 201,
            countryTag: CountryTag.PRK,
            archetype: EquipmentType.InfantryKit,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 10,
            priority: 200,
            initialEfficiency: 0.70f,
            efficiencyCap: 0.80f));
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 202,
            countryTag: CountryTag.PRK,
            archetype: EquipmentType.Artillery,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 10,
            priority: 180,
            initialEfficiency: 0.70f,
            efficiencyCap: 0.80f));
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 203,
            countryTag: CountryTag.PRK,
            archetype: EquipmentType.MBT,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 8,
            priority: 160,
            initialEfficiency: 0.60f,
            efficiencyCap: 0.80f));
        _world.SpawnProductionLine(ProductionLineComponent.Create(
            lineId: 204,
            countryTag: CountryTag.PRK,
            archetype: EquipmentType.IFV,
            generation: TechGeneration.Gen1_1980Baseline,
            assignedMilitaryFactories: 6,
            priority: 140,
            initialEfficiency: 0.55f,
            efficiencyCap: 0.80f));

        // 1. ROK Capital Mechanized Infantry Division ('Tiger') at Paju / Munsan (Province 6)
        _world.SpawnDivision(DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 6,
            templateId: 1,
            combatWidth: 20,
            softAttack: 65f,
            hardAttack: 40f,
            defense: 50f,
            breakthrough: 50f,
            armor: 45f,
            piercing: 50f,
            hardness: 0.50f,
            maxOrganization: 60f,
            startingStrength: 1.0f,
            startingExperience: 0.65f,
            speed: 28f,
            pgmAuthorized: true));

        // 2. ROK 1st Infantry Division ('Forward') at Seoul Metropolitan (Province 0)
        _world.SpawnDivision(DivisionComponent.Create(
            divisionId: 102,
            countryTag: CountryTag.ROK,
            startingProvinceId: 0,
            templateId: 2,
            combatWidth: 20,
            softAttack: 45f,
            hardAttack: 20f,
            defense: 55f,
            breakthrough: 30f,
            armor: 20f,
            piercing: 45f,
            hardness: 0.20f,
            maxOrganization: 55f,
            startingStrength: 1.0f,
            startingExperience: 0.50f,
            speed: 16f,
            pgmAuthorized: false));

        // 3. PRK 6th Infantry Division at Kaesong City (Province 92)
        _world.SpawnDivision(DivisionComponent.Create(
            divisionId: 201,
            countryTag: CountryTag.PRK,
            startingProvinceId: 92,
            templateId: 3,
            combatWidth: 20,
            softAttack: 40f,
            hardAttack: 15f,
            defense: 45f,
            breakthrough: 20f,
            armor: 10f,
            piercing: 20f,
            hardness: 0.15f,
            maxOrganization: 45f,
            startingStrength: 1.0f,
            startingExperience: 0.45f,
            speed: 16f,
            pgmAuthorized: false));

        // 4. PRK 820th Armored Division at Sariwon Redoubt (Province 91)
        _world.SpawnDivision(DivisionComponent.Create(
            divisionId: 202,
            countryTag: CountryTag.PRK,
            startingProvinceId: 91,
            templateId: 4,
            combatWidth: 20,
            softAttack: 45f,
            hardAttack: 45f,
            defense: 40f,
            breakthrough: 45f,
            armor: 35f,
            piercing: 35f,
            hardness: 0.65f,
            maxOrganization: 40f,
            startingStrength: 1.0f,
            startingExperience: 0.50f,
            speed: 24f,
            pgmAuthorized: false));

        GD.Print("[MainView] Spawned 1985 Korean Peninsula starting divisions (ROK 101, 102 vs. PRK 201, 202).");
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
