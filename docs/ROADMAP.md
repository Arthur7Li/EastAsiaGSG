# Execution Roadmap (`EastAsiaGSG`)

## Phase 1: Core Data Structures & GPU Map Renderer
- [x] **Task 1.1:** Implement `ProvinceData`, `StateData`, `CountryState`, `StrategicRegionData`, and `SimulationWorld` in `EastAsiaGSG.Core`.
- [x] **Task 1.2:** Implement `JsonMapLoader` in `EastAsiaGSG.Data` and write an xUnit test verifying loading of a mock 120-province Korean Peninsula dataset.
- [x] **Task 1.3:** Write `MapShader.gdshader` and `MapRenderer.cs` in `EastAsiaGSG.GodotClient` supporting 16-bit RG `provinces.png` ID decoding, 1D `map_lut.png` dynamic coloring, and $O(1)$ mouse picking.
- [x] **Task 1.4:** Build `CameraController.cs` (smooth WASD/edge pan, cursor-centered zoom) and Map Mode toolbar (Political, Terrain, Supply, Industry).

## Phase 2: Korean Peninsula Tactical Sandbox (Movement & Combat)
- [x] **Task 2.1:** Implement CSR Adjacency Graph & zero-allocation A* pathfinder in `EastAsiaGSG.Core/Math/ProvincePathfinder.cs`.
- [x] **Task 2.2:** Implement `DivisionComponent`, `ActiveCombatComponent`, `DivisionMovementSystem`, and `CombatResolutionSystem` (6-hour tick math with Hardness, Piercing, and PGM multipliers).
- [x] **Task 2.3:** Write headless xUnit combat balance tests (`SimTests/CombatSystemTests.cs`) simulating a 1985 ROK vs. PRK division battle over 72 hours.
- [x] **Task 2.4:** Build Godot Division On-Map Counters, movement arrows, and HOI4-style green/red 0–100 Battle Bubbles.

## Phase 3: Industrial Economy, Equipment Stockpiles & Reinforcements
- [x] **Task 3.1:** Implement `ProductionLineComponent`, `ProductionSystem` (hyperbolic efficiency growth), and `ConstructionSystem` (State building queue).
- [x] **Task 3.2:** Implement `ReinforcementSystem` connecting field division strength/PGM consumption to `CountryState.EquipmentStockpile`.
- [x] **Task 3.3:** Build the Godot Production Line UI, Construction Queue UI, and National Stockpile TopBar.

## Phase 4: Supply Graph, Air/Naval Regions & Off-Map Superpowers
- [x] **Task 4.1:** Implement `SupplyGraphSystem` (Hub/Rail/Port Dijkstra + Trans-Siberian Artery bottleneck logic).
- [x] **Task 4.2:** Implement `AirAndNavalRegionSystem` and the 60% Naval / 50% Air Superiority gate for strait crossings and amphibious invasions.
- [x] **Task 4.3:** Implement US PACOM `HomelandEscalation` and DEFCON thresholds.

## Phase 5: JSON Focus Engine, Tech Tree & 1985 Content Generation
- [x] **Task 5.1:** Implement `FocusTreeSystem` and `FocusEffectExecutor` handling all 15 enum `EffectType` actions.
- [x] **Task 5.2:** Build the procedural Godot `FocusTreeViewer.tscn` that auto-lays out nodes and prerequisite lines from `/data/focus_trees/*.json`.
- [x] **Task 5.3:** Batch-generate 1985 OOBs, State definitions, and Era I Focus Trees for `PRC`, `USA`, `SOV`, `JPN`, `ROK`, `PRK`, and `TWN`.

## Phase 6: Two-Tier Utility AI & Polish
- [x] **Task 6.1:** Implement `StrategicMinisterAISystem` (monthly focus/production scoring) and `TacticalFrontlineAISystem` (daily frontline assignment & attack execution).
- [x] **Task 6.2:** Run 15-year headless soak tests (21,900 six-hour ticks) to verify zero exceptions, zero memory leaks, and historical plausibility.

---

# Grand Architecture Expansion Roadmap (Phases 7–11)

## Phase 7: Presentation Optimization, Snapshot Decoupling & Map Metadata
- [x] **Task 7.1:** Expand `ProvinceData` to 32 bytes (`VictoryPoints`, `AirfieldLevel`, `FortificationLevel`) and add monotonic dirty-revision counters (`OwnershipRevision`, `SupplyRevision`, `EconomyRevision`, `AirNavalRevision`) to `SimulationWorld`.
- [x] **Task 7.2:** Implement `MapMetadataRegistry` in `EastAsiaGSG.Data` to surface authentic State, Province, Strategic Region, Template, and News Event names across all Godot HUD panels (`ProvinceInfoPanel`, `ConstructionQueuePanel`, `CombatTelemetryPopup`).
- [x] **Task 7.3:** Optimize `MainView` (active-drawer-only refresh on tick), `MapRenderer` (revision-gated GPU LUT uploads), and `TacticalMapOverlay` (tick-gated ECS collection decoupled from 144Hz camera pan/zoom, viewport frustum culling, and HOI4-style `xN` unit stack counter aggregation).

## Phase 8: $O(1)$ Spatial Index & Three-Tier Operational AI
- [x] **Task 8.1:** Implement zero-allocation `ProvinceSpatialIndex` in `EastAsiaGSG.Core/ECS` rebuilt in a single $O(D)$ pass per tick, replacing all nested $O(P \times D)$ ECS queries in `DivisionMovementSystem` and `TacticalFrontlineAISystem`.
- [x] **Task 8.2:** Repurpose `DivisionComponent` reserved bytes (`FieldOffset 116..127`, preserving 128-byte size) for `ObjectiveProvinceId`, `ArmyGroupId`, and `OperationalOrderStance`, and implement daily `OperationalPlanSystem` to concentrate armored/mechanized *Schwerpunkt* spearheads on enemy capitals and supply hubs.
- [x] **Task 8.3:** Upgrade `TacticalFrontlineAISystem` with multi-province coordinated pincer attacks (+20 combat width bonus) and entrenchment hysteresis, and expand `StrategicMinisterAISystem` to autonomously recruit new divisions (`CommandType.RecruitDivision`) and execute wargoals (`CommandType.DeclareWar`).

## Phase 9: Discrete Pacific Air/Naval Task Forces & Combat Doctrines
- [x] **Task 9.1:** Implement 64-byte `NavalTaskForceComponent` (`CarrierStrikeGroup`, `SurfaceActionGroup`, `SubmarineWolfpack`, `AmphibiousReadyGroup`) and 32-byte `AirWingComponent` in `EastAsiaGSG.Core/Components`.
- [x] **Task 9.2:** Upgrade `AirAndNavalRegionSystem` and `SupplyGraphSystem` to integrate task force missions, physical US PACOM / Soviet Pacific Fleet spawns, and submarine sea-lane convoy interdiction reducing overseas port supply capacity.
- [x] **Task 9.3:** Enhance `CombatResolutionSystem` with a $+15\%$ Combined Arms synergy modifier and 24-hour tactical doctrine counter-play rolls (*AirLand Battle*, *Deep Operations*, *Mass Assault*, *Elastic Defense*).

## Phase 10: Victory Points, Capitulation, Interactive Diplomacy & Force Designer UI
- [x] **Task 10.1:** Implement daily `CapitulationSystem` in `EastAsiaGSG.Core/Systems` evaluating province `VictoryPoints` and national `Stability` to trigger surrender, province control transfer, 50% stockpile capture, and global capitulation news events.
- [x] **Task 10.2:** Add `SwitchProductionEquipment`, `CreateProductionLine`, and `AssignNavalMission` commands to `SimulationCommand` and `SimulationWorld.DrainCommandQueue`.
- [x] **Task 10.3:** Build `DiplomacyAndNewsModal.cs` (15-nation faction/war ledger, targetable war declarations, and interactive Cold War News Event popups), upgrade `NationalStockpileTopBar` with a historical OOB Template Recruitment selector, and add equipment generation upgrade/creation controls to `ProductionLinePanel`.

## Phase 11: High-Density Theatre Scaling (600+ Provinces) & Binary Save/Load
- [x] **Task 11.1:** Implement `WorldSnapshotSerializer` in `EastAsiaGSG.Data/Serialization` using `MemoryMarshal.AsBytes` and `ZLibStream` for $< 5\ \text{ms}$ `.eagsav` binary save/load roundtripping, wired to `Save` / `Load` buttons in `TimeControlsToolbar`.
- [x] **Task 11.2:** Upgrade `scripts/generate_east_asia_theatre.py` with bounded Lloyd-relaxed Voronoi tessellation to subdivide the 41 states into 650+ operational provinces while preserving river, strait, and railway arteries and city Victory Points.
- [x] **Task 11.3:** Execute full-scale 15-year soak benchmarks and headless balance verification across the expanded theatre.

