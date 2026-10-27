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
- [ ] **Task 2.4:** Build Godot Division On-Map Counters, movement arrows, and HOI4-style green/red 0–100 Battle Bubbles.

## Phase 3: Industrial Economy, Equipment Stockpiles & Reinforcements
- [ ] **Task 3.1:** Implement `ProductionLineComponent`, `ProductionSystem` (hyperbolic efficiency growth), and `ConstructionSystem` (State building queue).
- [ ] **Task 3.2:** Implement `ReinforcementSystem` connecting field division strength/PGM consumption to `CountryState.EquipmentStockpile`.
- [ ] **Task 3.3:** Build the Godot Production Line UI, Construction Queue UI, and National Stockpile TopBar.

## Phase 4: Supply Graph, Air/Naval Regions & Off-Map Superpowers
- [ ] **Task 4.1:** Implement `SupplyGraphSystem` (Hub/Rail/Port Dijkstra + Trans-Siberian Artery bottleneck logic).
- [ ] **Task 4.2:** Implement `AirAndNavalRegionSystem` and the 60% Naval / 50% Air Superiority gate for strait crossings and amphibious invasions.
- [ ] **Task 4.3:** Implement US PACOM `HomelandEscalation` and DEFCON thresholds.

## Phase 5: JSON Focus Engine, Tech Tree & 1985 Content Generation
- [ ] **Task 5.1:** Implement `FocusTreeSystem` and `FocusEffectExecutor` handling all 15 enum `EffectType` actions.
- [ ] **Task 5.2:** Build the procedural Godot `FocusTreeViewer.tscn` that auto-lays out nodes and prerequisite lines from `/data/focus_trees/*.json`.
- [ ] **Task 5.3:** Batch-generate 1985 OOBs, State definitions, and Era I Focus Trees for `PRC`, `USA`, `SOV`, `JPN`, `ROK`, `PRK`, and `TWN`.

## Phase 6: Two-Tier Utility AI & Polish
- [ ] **Task 6.1:** Implement `StrategicMinisterAISystem` (monthly focus/production scoring) and `TacticalFrontlineAISystem` (daily frontline assignment & attack execution).
- [ ] **Task 6.2:** Run 15-year headless soak tests (21,900 six-hour ticks) to verify zero exceptions, zero memory leaks, and historical plausibility.