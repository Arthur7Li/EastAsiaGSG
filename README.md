# EastAsiaGSG — East Asia 1985–2000 Grand Strategy Game

A high-performance, regional Cold War Gone Hot Grand Strategy Game (1985–2000) covering the East Asian and Western Pacific theatre (~1,500 provinces, 140 states, 18 strategic air/sea regions, and 15 playable nation tags).

Inspired by core Hearts of Iron IV operational loops—stripped of DLC bloat and engineered from the ground up around a **headless, zero-allocation Data-Oriented C# simulation kernel** (`EastAsiaGSG.Core`) paired with a **reactive Godot 4.x (.NET 8) GPU presentation client** (`EastAsiaGSG.GodotClient`).

---

## Theatre Roster (15 Nation Tags)

### The 7 Major Powers
| Tag ID | Code | Nation / Theatre Command | Signature Mechanics |
| :---: | :---: | :--- | :--- |
| `00` | **`PRC`** | People's Republic of China | Deng Xiaoping Economic Reform Era & PLA Modernization |
| `01` | **`USA`** | United States Pacific Command (`USPACOM`) | On-map bases (Okinawa, Guam, ROK, PHL) + Off-Map `HomelandEscalation` Pool |
| `02` | **`SOV`** | Soviet Union / Russian Far East (`FEMD`) | Vladivostok/Khabarovsk fortress hubs + Trans-Siberian/BAM Rail Artery |
| `03` | **`JPN`** | Japan | Economic Bubble Titan, Semiconductor Dominance & Article 9 / JSDF Remilitarization |
| `04` | **`ROK`** | Republic of Korea (South Korea) | Fifth Republic -> 1987 Democratization, Chaebol Industrial Boom & DMZ Defense |
| `05` | **`PRK`** | Democratic People's Republic of Korea | 1985 Conventional Parity -> 1990s Arduous March & Asymmetric/Nuclear Brinkmanship |
| `06` | **`TWN`** | Republic of China (Taiwan) | Martial Law -> Democratization, Silicon Shield & Taiwan Strait A2/AD Defense |

### The 8 Regional Minors
`07` **`VNM`** (Vietnam), `08` **`PHL`** (Philippines), `09` **`MNG`** (Mongolia), `10` **`THA`** (Thailand), `11` **`LAO`** (Laos), `12` **`KHM`** (Cambodia), `13` **`MMR`** (Myanmar), `14` **`MYS`** (Malaysia).

---

## Solution Architecture & Repository Layout

```text
EastAsiaGSG/
├── EastAsiaGSG.sln
├── AGENTS.md                        # Architectural invariants & engineering rules
├── docs/
│   ├── ARCHITECTURE.md              # Stratified tick pipeline & GPU LUT protocol
│   ├── GAME_DESIGN_SPEC.md          # Combat math, IC formulas & theatre mechanics
│   ├── DATA_SCHEMAS.md              # Modding JSON schemas (States, OOBs, Focuses)
│   └── ROADMAP.md                   # Phased execution roadmap (Phases 1–6)
├── data/
│   └── map/
│       ├── states.json              # 10-state Korean Peninsula sandbox definitions
│       ├── provinces.json           # 120-province Korean Peninsula graph & coordinates
│       └── provinces.png            # Lossless 1024x1024 16-bit RG encoded province ID map
├── src/
│   ├── EastAsiaGSG.Core/            # Headless Data-Oriented simulation kernel (zero engine deps)
│   │   ├── Components/              # Blittable fixed-size structs & domain enums (DivisionComponent, ActiveCombatComponent, ProductionLineComponent, DivisionTemplateRecord, etc.)
│   │   ├── Math/                    # Compressed Sparse Row (CSR) ProvinceGraph & zero-allocation A* ProvincePathfinder
│   │   ├── Commands/                # Unmanaged SimulationCommand & CommandRingBuffer
│   │   ├── Systems/                 # Pure simulation systems (DivisionMovementSystem, CombatResolutionSystem, ConstructionSystem, ProductionSystem, ReinforcementSystem)
│   │   └── ECS/                     # SimulationWorld & stratified StepSixHours() pipeline
│   ├── EastAsiaGSG.Data/            # Source-generated JSON loaders & CPU map/LUT buffers
│   │   ├── Schemas/                 # System.Text.Json DTOs & MapJsonContext
│   │   ├── Loaders/                 # JsonMapLoader with bijective graph validation
│   │   └── Map/                     # ProvinceMapBuffer (O(1) picking) & MapLutBuffer (2048x1 RGBA8)
│   └── EastAsiaGSG.GodotClient/     # Godot 4.x (.NET 8) reactive presentation client
│       ├── Map/                     # MapShader.gdshader, MapRenderer.cs, CameraController.cs
│       ├── Tactics/                 # TacticalMapOverlay.cs (NATO counters, movement arrows, HOI4 Battle Bubbles)
│       ├── UI/                      # NationalStockpileTopBar, ConstructionQueuePanel, ProductionLinePanel, ProvinceInfoPanel, MapModeToolbar, TimeControlsToolbar, CombatTelemetryPopup
│       ├── MainView.cs              # Root scene controller wiring world, map, camera, overlay & HOI4 Side-Drawer HUD
│       └── MainView.tscn            # Main Godot 4 presentation scene
└── tests/
    └── EastAsiaGSG.SimTests/        # Headless xUnit verification & benchmark suite (55 tests)
        ├── CombatSystemTests.cs     # 72-hour ROK/PRK battle sims, width capping, PGM & zero-allocation checks
        ├── ProductionSystemTests.cs # Hyperbolic efficiency growth, state construction queue & 15-factory cap
        ├── ReinforcementSystemTests.cs # Newest-gen stockpile draw, manpower/XP dilution & end-to-end IC-to-frontline loop
        ├── PathfinderTests.cs       # A* benchmarks (< 5µs/query), DMZ/strait gating & thread-safety
        ├── SimulationWorldTests.cs  # Calendar progression, entity resolution & CSR graph lookups
        ├── MapLoaderTests.cs        # JSON schema validation & asymmetric edge detection
        └── MapRendererTests.cs      # 16-bit RG color roundtripping & LUT dynamic updates
```

### Strict Architectural Invariants
1. **Zero Engine Leakage in Core & Data:** `EastAsiaGSG.Core` and `EastAsiaGSG.Data` never reference `GodotSharp` or any rendering library. The entire simulation is 100% runnable headlessly via xUnit and CLI soak tests.
2. **Data-Oriented Memory Layout:**
   - Fixed-count topology and nation states live in pre-allocated flat contiguous arrays (`Provinces[1500]`, `States[140]`, `Countries[16]`, `Regions[18]`) using explicit-layout blittable `struct` types (`ProvinceData` 24B, `StateData` 32B, `CountryState` 184B, `StrategicRegionData` 40B).
   - Province adjacency is packed into a global **Compressed Sparse Row (CSR)** graph (`ProvinceGraph`), providing $O(1)$ zero-allocation `ReadOnlySpan<ushort>` neighbor slices.
   - Dynamic entities (Divisions, Active Combats, Production Lines) reside in `Arch.Core.World` (Arch ECS v2.1.0).
   - Zero LINQ or heap allocations inside 6-hour or daily tick loops.
3. **One-Way UI Data Binding:** The Godot client reads simulation state arrays to update UI controls and the GPU 1D Lookup Texture (`MapLutBuffer`). Player and AI actions enqueue unmanaged `SimulationCommand` structs (16B) into `CommandRingBuffer`, which are drained deterministically at the start of the next 6-hour tick.

---

## Stratified Tick Pipeline (`SimulationWorld.StepSixHours()`)

Simulation work is stratified by frequency to guarantee **1,000+ ticks/second** in headless mode (1 full year / 1,460 six-hour ticks executes in **$< 5\text{ ms}$**):

- **Tier 1 — Every 6 Hours (`00:00, 06:00, 12:00, 18:00`):**
  1. `CommandProcessor`: Drains queued player and AI commands (`SelectFocus`, `ShiftDefcon`, `AssignFactory`, `QueueConstruction`, `AssignProductionFactories`, `MoveDivision`, `TogglePgm`).
  2. `DivisionMovementSystem`: Advances division `MovementProgress` along province adjacency edges; initiates combat upon entering hostile territory; automatically replenishes waypoints for long-distance routes (> 16 hops).
  3. `CombatResolutionSystem`: Resolves 6-hour HOI4 land combat math:
     - **Hardness Blend:** $A_{\text{base}} = A_s \times (1 - H_t) + A_h \times H_t$.
     - **PGM Authorization:** +35% combat power multiplier consuming national precision-guided munitions stockpiles.
     - **Armor & Piercing Gate:** 50% damage penalty if target armor exceeds attacker piercing; +40% org damage if attacker armor pierces target.
     - **Defense & Breakthrough Mitigation:** 10% blocked hit roll, 40% unblocked hit roll.
     - **Combat Width & Reserve Commitment:** Base width 80 (+20 per flanking province) with continuous reserve filling and contiguous slice compaction.
     - **Retreat & Shatter:** Broken divisions withdraw to adjacent friendly provinces or shatter completely if encircled.
  4. `AirAndNavalRegionSystem`: Computes contested air superiority and naval control ratios across the 18 Strategic Regions, applies weather modifiers, accounts for coastal A2/AD missile batteries (Radar/SAM Lv 3+ with PGM), deploys Carrier Strike Groups (CSGs), and enforces the Strait & Amphibious Control Gate (> 60% Naval Control and > 50% Air Superiority required for crossing).
- **Tier 2 — Daily Tick (`00:00`):**
  1. `SupplyGraphSystem`: Executes multi-source Dijkstra overland supply distribution from operational Supply Hubs, Ports, and Railways; tracks Trans-Siberian Artery bottleneck throughput; applies local factory trickle to isolated pockets; calculates division supply consumption and updates `ProvinceSupplyRatio[1500]`.
  2. `ConstructionSystem`: Calculates available Civilian Factories ($C_{\text{free}} = \max(0, C_{\text{total}} - \lceil \text{TotalFactories} \times \text{ConsumerGoodsRatio} \rceil + C_{\text{trade}})$), enforces the 15-factory per-state cap, advances State building progress ($\Delta P = C_{\text{assigned}} \times 5.0 \times (1 + \text{SpeedMod})$), and increments `Civ`, `Mil`, `Dock`, `Airbase`, `Radar/SAM`, `Infra`, `SupplyHub`, and `Port` levels.
  3. `ProductionSystem`: Evaluates priority-ordered `ProductionLineComponent` entities across the 8 Equipment Archetypes (`InfantryKit`, `SupportVehicles`, `IFV`, `MBT`, `Artillery`, `AttackHeli`, `TacticalAir`, `PGM`), applies strategic resource shortage penalties (`Steel`, `Semiconductors`, `Oil`, `Aluminum`), updates hyperbolic Production Efficiency ($\Delta E_i = 0.001 \times (E_{\text{cap}}^2 / E_i)$), and increments national `EquipmentStockpile`.
  4. `ReinforcementSystem`: Transfers manpower and equipment (`Gen 3 -> Gen 0` priority) from national stockpiles to depleted field divisions in valid supply (`SupplyRatio >= 0.15f`), dilutes division experience proportionally when absorbing green replacements, and recovers daily `Organization`.
  5. `SuperpowerEscalationSystem`: Evaluates theatre threat scoring (Seoul, Taiwan, US forward bases, active Bluefor combat), updates US PACOM `HomelandEscalation` (15..100) and theatre `DEFCON` (5..1), scales CONUS off-map military factories (10..200), ships daily materiel convoys at DEFCON 3 or below, and mobilizes coalition readiness postures.
  6. `FocusAndResearchSystem` and `TacticalFrontlineAISystem` hooks.
- **Tier 3 — Monthly Tick (`Day 1 at 00:00`):**
  1. `MacroEconomySystem` and `StrategicMinisterAISystem` hooks.

---

## Tactical Sandbox, Industrial HUD & Controls (`EastAsiaGSG.GodotClient`)

The Godot presentation client provides an interactive 2D RTS map interface backed by a two-texture GPU shader pipeline (`MapShader.gdshader`), reactive tactical overlays, and an HOI4-inspired Left Side-Drawer HUD:

### GPU Two-Texture Pipeline & $O(1)$ Picking
1. **`provinces.png` (Static Lossless RGB8 Texture, `Filter = Nearest`):**
   - Encodes `ProvinceId` directly in the Red and Green channels: `ProvinceId = (R << 8) | G` (`0..1499` for land provinces; `0xFFFF = 65535` for ocean pixels).
   - **$O(1)$ CPU Mouse Picking:** `ProvinceMapBuffer` converts normalized mouse UV $[0, 1]^2$ on the map quad to pixel coordinates $(x, y)$ in a cached CPU byte buffer, resolving `ProvinceId` in $O(1)$ time with zero physics raycasting.
2. **`map_lut_tex` (Dynamic 1D Texture, `2048 x 1`, `RGBA8`, `Filter = Nearest`):**
   - Maintained on the CPU by `MapLutBuffer` (`Color32[2048]` / `byte[8192]`).
   - Encodes each province's active Map Mode RGB color in bytes `0..2` and its `ControllerTag` (`0..14` or `255`) in the Alpha channel (`byte 3`).
   - **GPU Border Detection:** `MapShader.gdshader` samples 1-texel and 2-texel cardinal neighbors in `provinces_tex`, drawing thick international/DMZ borders where `ControllerTag` differs, coastline borders along ocean transitions, and subtle internal province borders where `ProvinceId` differs within the same nation.

### Interactive Tactical Map Overlay (`TacticalMapOverlay.cs`)
- **Division NATO Counters:** Displays country banners, template icons, organization (green) and strength (brown) status bars, and selection halos.
- **Dynamic Movement & Attack Vectors:** Real-time green movement arrows, amber retreat vectors, and red attack vectors with dashed lines along planned waypoint corridors.
- **HOI4-Style Battle Bubbles:** Color-coded 0–100 battle indicators (Green $\ge 55$, Amber $45\text{--}54$, Red $\le 44$) reflecting real-time attacker combat advantage and line breach progress.
- **Combat Telemetry Popup (`CombatTelemetryPopup.cs`):** Click any Battle Bubble to inspect active combat telemetry: committed attacker/defender divisions, organization & strength loss per tick, combat width utilization, active tactical stances, and national PGM stockpile consumption.

### HOI4-Style Industrial, Strategic & Stockpile HUD
- **`NationalStockpileTopBar.cs` (Top-Left HUD):** Displays active nation switcher (`ROK ⇄ PRK`, hotkey `Tab`), Political Power (`PP`), `Stability`, `War Support`, `Manpower`, free/total Civilian & Military Factories, live national equipment stockpiles across all archetypes, and US PACOM `HomelandEscalation` telemetry (`Esc: XX%` with DEFCON tooltip).
- **`TimeControlsToolbar.cs` (Top-Right HUD):** Simulation calendar date, Play/Pause/Step buttons, speed controls (`1x`, `2x`, `5x`), and a dynamic color-coded **DEFCON Alert Badge** (`DEFCON 5: FADE OUT` through `DEFCON 1: COCKED PISTOL`).
- **Persistent Left Tab Strip (`MainView.cs`):** Positioned directly below the top bar with three mutually-exclusive view modes so left-side panels never overlap:
  - **`📍 Map Info` (`Esc`):** Displays `ProvinceInfoPanel.cs` with live-refreshed province inspection: State ID, Owner/Controller, Terrain, Supply Hub/Port/Infra levels, Civilian/Military/Dockyard counts, Population, Strategic Resources, Stationed Units, and **Strategic Region telemetry** (Air Superiority %, Naval Control %, Coastal A2/AD penalty, and Strait Crossing status).
  - **`🏗 Construct (C)` (`C`):** Opens `ConstructionQueuePanel.cs` left side-drawer displaying controlled States, $C_{\text{free}}$ / Consumer Goods breakdown, active State construction progress bars, and one-way command buttons (`+Civ`, `+Mil`, `+Infra`, `+SAM`, `+Air`, `-5`/`+5` Civ Factories).
  - **`⚙ Produce (P)` (`P`):** Opens `ProductionLinePanel.cs` left side-drawer displaying Military Factory allocation, strategic resource availability (`Steel`, `Semi`, `Oil`, `Alum`), hyperbolic Production Efficiency bars, daily IC & unit output rates, and one-way factory assignment buttons (`-1`, `+1`, `+5` Mil Factories).

### Player & Camera Controls
- **Select Division:** Left-Click any on-map division counter (highlights division in cyan, switches to `📍 Map Info` if a drawer is open, and displays its active route).
- **Issue Move / Attack Order:** Right-Click any target province with a division selected ($O(1)$ mouse picking resolves the destination and dispatches `MoveDivisionCommand` to the simulation ring buffer).
- **Inspect Province:** Hover or Left-Click any unselected province to view state ID, owner/controller, terrain, infrastructure, factories, supply ratio, strategic resources, and strategic region balance in `ProvinceInfoPanel`.
- **Switch Inspected Nation (`ROK` ⇄ `PRK`):** Press `Tab` or click the top-left `ROK ⇄` button.
- **Toggle Industrial Side-Drawers:** Press `C` for Construction Queue, `P` for Military Production Lines, or `Esc` to return to Map Info.
- **Pan Camera:** `W` / `A` / `S` / `D`, Arrow Keys, Viewport Edge Pan, or Middle/Right Mouse Drag (hold `Shift` for $2\times$ pan speed).
- **Zoom Camera:** Mouse Scroll Wheel (cursor-centered zoom clamped to `[0.25x, 4.0x]`).
- **Time & Speed Controls (`TimeControlsToolbar.cs`):**
  - `Spacebar`: Toggle Play / Pause.
  - `1` / `2` / `3`: Simulation tick speed multipliers (`1x`, `2x`, `5x`).
  - `.` / `>`: Step a single 6-hour simulation tick.
- **Switch Map Modes (`MapModeToolbar.cs`):**
  - `F1` — **Political Map Mode**
  - `F2` — **Terrain Map Mode**
  - `F3` — **Supply Map Mode** (Overland Dijkstra supply ratio gradient)
  - `F4` — **Industrial Map Mode** (Factory concentration density)
  - `F5` — **Air Superiority Map Mode** (Contested Red vs Contested vs Blue air power across land and maritime sea zones)

---

## Build, Test & Verification

### Prerequisites
- **.NET 8.0 SDK** (`net8.0`)
- **Godot Engine 4.x (.NET / Mono Edition)** (configured for `Godot.NET.Sdk/4.7.2`)

### Run Full Headless Verification Suite
From the repository root, run the automated verification script:
```bash
./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
```
This script verifies:
1. **Engine Isolation:** Zero `Godot` or `GodotSharp` references in `EastAsiaGSG.Core` or `EastAsiaGSG.Data`.
2. **Zero Hot-Path LINQ Allocations:** Zero `System.Linq` usage in `src/EastAsiaGSG.Core/Systems`.
3. **Strict One-Way Project Dependencies:** `Core` $\leftarrow$ `Data` $\leftarrow$ `GodotClient`.
4. **Zero-Warning Solution Build:** `dotnet build EastAsiaGSG.sln --warnaserror` (`0 Warning(s)`, `0 Error(s)`).
5. **Headless xUnit Suite:** Runs all **85** unit and benchmark tests in `tests/EastAsiaGSG.SimTests` across all modules:
   - `CombatSystemTests` (15 tests: 72-hour ROK vs PRK historical battles, combat width & reserve filling, encirclement shattering, PGM depletion, zero-heap allocation checks, and 1,000-battle throughput benchmark in $< 100\text{ ms}$).
   - `SupplyGraphSystemTests` (8 tests: Two-tier Dijkstra overland supply, Trans-Siberian Artery bottleneck, island port-to-port sea lanes, local state factory trickle, zero-allocation daily loop checks, and $< 1\text{ ms}$ throughput benchmark).
   - `AirAndNavalRegionSystemTests` (13 tests: Contested air superiority, weather attenuation, coastal A2/AD missile suppression, generational scarcity enforcement across multiple airbases, Strait & Amphibious Control Gates, sea zone dynamic LUT rendering, and zero-allocation 6-hour loop checks).
   - `SuperpowerEscalationSystemTests` (6 tests: PACOM threat scoring, Homeland Escalation scaling, DEFCON transitions, CONUS off-map military factories, peacetime decay, Carrier Strike Group deployments, and $< 1\text{ ms}$ throughput benchmark).
   - `ProductionSystemTests` (7 tests: 32-byte blittable layouts, $C_{\text{free}}$ & Consumer Goods formulas, State construction queue & 15-factory cap, hyperbolic efficiency growth, resource shortage penalties, equipment switch retention, and zero-allocation daily loop checks).
   - `ReinforcementSystemTests` (5 tests: 24-byte `DivisionTemplateRecord` layout, newest-generation-first equipment draw & experience dilution, partial stockpile/manpower/supply gates, end-to-end production-to-frontline reinforcement + PGM resupply, and zero-allocation daily loop checks).
   - `PathfinderTests` (10 tests: zero-allocation verification, $< 5\ \mu\text{s}$ A* performance, DMZ cross-border gating, strait access control, multi-thread safety).
   - `SimulationWorldTests` (7 tests: full 1-year calendar progression in $< 5\text{ ms}$, command ring buffer deterministic draining, two-phase division lookup, CSR adjacency graph).
   - `MapLoaderTests` (8 tests: Korean Peninsula 120-province dataset, foreign key integrity, topological validation).
   - `MapRendererTests` (3 tests: 16-bit RG encoding/decoding, $O(1)$ mouse picking, dynamic LUT buffer updates).

### Manual Build & Test Commands
```bash
# Build all projects with warnings treated as errors
dotnet build EastAsiaGSG.sln --warnaserror

# Run headless simulation, map loader, and LUT/picking unit tests
dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"
```

### Run the Godot Presentation Client
Open `src/EastAsiaGSG.GodotClient/project.godot` in Godot 4 (.NET) or launch directly from the command line:
```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path src/EastAsiaGSG.GodotClient
```

---

## Development Roadmap Status

- [x] **Phase 1: Core Data Structures & GPU Map Renderer**
  - [x] **Task 1.1:** Blittable structs (`ProvinceData`, `StateData`, `CountryState`, `StrategicRegionData`), CSR `ProvinceGraph`, `CommandRingBuffer`, and `SimulationWorld` in `EastAsiaGSG.Core`.
  - [x] **Task 1.2:** Source-generated `JsonMapLoader` in `EastAsiaGSG.Data` and 120-province / 10-state Korean Peninsula dataset (`states.json`, `provinces.json`).
  - [x] **Task 1.3:** `MapShader.gdshader`, `MapRenderer.cs`, 16-bit RG `provinces.png` ID encoding/decoding, 1D `map_lut` dynamic coloring, and $O(1)$ mouse picking.
  - [x] **Task 1.4:** `CameraController.cs` (smooth WASD/edge/drag pan, cursor-centered zoom), `MapModeToolbar.cs` (`F1–F5`), `ProvinceInfoPanel.cs`, and `MainView.tscn`.
- [x] **Phase 2: Korean Peninsula Tactical Sandbox (Movement & Combat)**
  - [x] **Task 2.1:** CSR Adjacency Graph & zero-allocation A* pathfinder in `EastAsiaGSG.Core/Math/ProvincePathfinder.cs`.
  - [x] **Task 2.2:** `DivisionComponent`, `ActiveCombatComponent`, `DivisionMovementSystem`, and `CombatResolutionSystem` (6-hour tick math with Hardness, Piercing, and PGM multipliers).
  - [x] **Task 2.3:** Headless xUnit combat balance tests (`SimTests/CombatSystemTests.cs`) simulating a 1985 ROK vs. PRK division battle over 72 hours, encirclement shattering, reserve width filling, and zero-allocation enforcement.
  - [x] **Task 2.4:** Godot Division On-Map NATO Counters, dynamic movement/attack vector arrows, HOI4-style green/amber/red 0–100 Battle Bubbles, and interactive `CombatTelemetryPopup.cs`.
- [x] **Phase 3: Industrial Economy, Equipment Stockpiles & Reinforcements**
  - [x] **Task 3.1:** Implement `ProductionLineComponent`, `ProductionSystem` (hyperbolic efficiency growth), and `ConstructionSystem` (State building queue).
  - [x] **Task 3.2:** Implement `ReinforcementSystem` connecting field division strength/PGM consumption to `CountryState.EquipmentStockpile`.
  - [x] **Task 3.3:** Build the Godot Production Line UI, Construction Queue UI, and National Stockpile TopBar with an HOI4-style mutually-exclusive Left Side-Drawer architecture.
- [x] **Phase 4: Supply Graph, Air/Naval Regions & Off-Map Superpowers**
  - [x] **Task 4.1:** Implement `SupplyGraphSystem` (Hub/Rail/Port Dijkstra + Trans-Siberian Artery bottleneck logic).
  - [x] **Task 4.2:** Implement `AirAndNavalRegionSystem` and the 60% Naval / 50% Air Superiority gate for strait crossings and amphibious invasions.
  - [x] **Task 4.3:** Implement US PACOM `HomelandEscalation` and DEFCON thresholds.
- [ ] **Phase 5: JSON Focus Engine, Tech Tree & 1985 Content Generation**
  - [ ] **Task 5.1:** Implement `FocusTreeSystem` and `FocusEffectExecutor` handling all 15 enum `EffectType` actions.
  - [ ] **Task 5.2:** Build the procedural Godot `FocusTreeViewer.tscn` that auto-lays out nodes and prerequisite lines from `/data/focus_trees/*.json`.
  - [ ] **Task 5.3:** Batch-generate 1985 OOBs, State definitions, and Era I Focus Trees for `PRC`, `USA`, `SOV`, `JPN`, `ROK`, `PRK`, and `TWN`.
- [ ] **Phase 6: Two-Tier Utility AI & Polish**
  - [ ] **Task 6.1:** Implement `StrategicMinisterAISystem` (monthly focus/production scoring) and `TacticalFrontlineAISystem` (daily frontline assignment & attack execution).
  - [ ] **Task 6.2:** Run 15-year headless soak tests (21,900 six-hour ticks) to verify zero exceptions, zero memory leaks, and historical plausibility.
