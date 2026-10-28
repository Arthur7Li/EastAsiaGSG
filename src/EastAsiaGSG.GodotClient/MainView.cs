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

        // 5. Setup HUD CanvasLayer
        var hudLayer = GetNodeOrNull<CanvasLayer>("HUD");
        if (hudLayer == null)
        {
            hudLayer = new CanvasLayer { Name = "HUD" };
            AddChild(hudLayer);
        }

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

        // Setup ProvinceInfoPanel at top-left
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
        _infoPanel.OffsetTop = 20;
        _infoPanel.OffsetRight = 360;
        _infoPanel.OffsetBottom = 280;
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
        _timeToolbar.OffsetTop = 20;
        _timeToolbar.OffsetRight = -20;
        _timeToolbar.OffsetBottom = 68;
        _timeToolbar.Initialize(_world);

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

        // Wire map interactions to info panel
        _mapRenderer.ProvinceHovered += id => _infoPanel.DisplayProvince(id);
        _mapRenderer.ProvinceSelected += id => _infoPanel.DisplayProvince(id);

        // Wire tactical overlay interactions to HUD
        _tacticalOverlay.DivisionSelected += div => _infoPanel.DisplaySelectedDivision(div);
        _tacticalOverlay.DivisionDeselected += () => _infoPanel.DisplaySelectedDivision(null);
        _tacticalOverlay.CombatSelected += combat => _combatPopup.DisplayCombat(_world, combat.CombatId);

        // Wire time controls tick event to refresh map colors, tactical overlay, and combat telemetry
        _timeToolbar.Ticked += () =>
        {
            _mapRenderer.RefreshMapLut();
            _combatPopup?.RefreshIfActive();
            _tacticalOverlay?.QueueRedraw();
        };

        // 6. Spawn Starting 1985 Historical Sandbox Divisions
        SpawnStartingSandboxDivisions();
    }

    private void SpawnStartingSandboxDivisions()
    {
        if (_world == null)
        {
            return;
        }

        // Grant ROK national PGM stockpile
        _world.Countries[CountryTag.ROK].SetStockpile(EquipmentType.PGM, TechGeneration.Gen2_1988LateColdWar, 120);

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
