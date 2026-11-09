# EastAsiaGSG — East Asia 1985–2000 Grand Strategy Game

A high-performance, regional Cold War Gone Hot Grand Strategy Game (1985–2000) covering the East Asian and Western Pacific theatre (618 operational provinces, 41 states, 18 strategic air/sea regions, and 15 playable nation tags).

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
│   ├── ARCHITECTURE.md              # Stratified tick pipeline, spatial indexing & binary save/load
│   ├── GAME_DESIGN_SPEC.md          # Combat math, doctrines, capitulation & AI specification
│   ├── DATA_SCHEMAS.md              # Modding JSON & binary .eagsav schemas
│   └── ROADMAP.md                   # Grand architecture execution roadmap (Phases 1–11)
├── data/
│   ├── focus_trees/                 # 1985 Era I Focus Trees (PRC, USA, SOV, JPN, ROK, PRK, TWN, GENERIC)
│   ├── oob_1985/                    # 1985 Historical OOBs & Division Templates (All 15 Playable Nations)
│   ├── saves/                       # QuickSave & user binary save snapshots (.eagsav)
│   └── map/
│       ├── states.json              # 10-state Korean Peninsula sandbox definitions
│       ├── east_asia_1985_states.json # 41-state East Asia theatre definitions across all 15 tags
│       ├── provinces.json           # 120-province Korean Peninsula graph & coordinates
│       ├── provinces.png            # Lossless 1024x1024 16-bit RG encoded province ID map
│       ├── east_asia_1985_provinces.json # 618-province full East Asian theatre graph
│       └── east_asia_1985_provinces.png  # Full theatre 2048x2048 16-bit RG province ID texture
├── src/
│   ├── EastAsiaGSG.Core/            # Headless Data-Oriented simulation kernel (zero engine deps)
│   │   ├── Components/              # Blittable fixed-size structs & domain enums (DivisionComponent, ActiveCombatComponent, ProductionLineComponent, NavalTaskForceComponent, AirWingComponent, DivisionTemplateRecord, FocusNodeData, etc.)
│   │   ├── Math/                    # Compressed Sparse Row (CSR) ProvinceGraph & zero-allocation A* ProvincePathfinder
│   │   ├── Commands/                # Unmanaged SimulationCommand & CommandRingBuffer (13 deterministic commands)
│   │   ├── Systems/                 # Pure simulation systems (DivisionMovementSystem, CombatResolutionSystem, AirAndNavalRegionSystem, CapitulationSystem, OperationalPlanSystem, SupplyGraphSystem, ConstructionSystem, ProductionSystem, ReinforcementSystem, SuperpowerEscalationSystem, FocusTreeSystem, FocusEffectExecutor, TacticalFrontlineAISystem, MacroEconomySystem, StrategicMinisterAISystem)
│   │   └── ECS/                     # SimulationWorld, ProvinceSpatialIndex & stratified StepSixHours() pipeline
│   ├── EastAsiaGSG.Data/            # Source-generated JSON loaders, metadata registry & snapshot serialization
│   │   ├── Schemas/                 # System.Text.Json DTOs & MapJsonContext (Map, FocusTreeDto, OobDto)
│   │   ├── Loaders/                 # JsonMapLoader, JsonFocusTreeLoader & JsonOobLoader with FK validation
│   │   ├── Serialization/           # WorldSnapshotSerializer (< 5ms .eagsav binary save/load engine)
│   │   ├── Metadata/                # MapMetadataRegistry (authentic names for Provinces, States, Regions, Templates, News)
│   │   └── Map/                     # ProvinceMapBuffer (O(1) picking) & MapLutBuffer (2048x1 RGBA8)
│   └── EastAsiaGSG.GodotClient/     # Godot 4.x (.NET 8) reactive presentation client
│       ├── Map/                     # MapShader.gdshader, MapRenderer.cs, CameraController.cs
│       ├── Tactics/                 # TacticalMapOverlay.cs (frustum-culled NATO counters, xN stack aggregation, movement vectors, HOI4 Battle Bubbles)
│       ├── UI/                      # NationalStockpileTopBar, ConstructionQueuePanel, ProductionLinePanel, FocusTreeViewer, ProvinceInfoPanel, MapModeToolbar, TimeControlsToolbar, CombatTelemetryPopup, DiplomacyAndNewsModal
│       ├── MainView.cs              # Root scene controller wiring world, map, camera, overlay, AI & HOI4 Side-Drawer HUD
│       └── MainView.tscn            # Main Godot 4 presentation scene
└── tests/
    └── EastAsiaGSG.SimTests/        # Headless xUnit verification & benchmark suite (135 tests)
        ├── CombatSystemTests.cs     # 72-hour battles, combat width, Combined Arms, 4 doctrines & zero-alloc checks
        ├── SupplyGraphSystemTests.cs # Overland Dijkstra supply, Trans-Siberian bottleneck & submarine wolfpack sea-lane choke
        ├── AirAndNavalRegionSystemTests.cs # Contested air/sea superiority, A2/AD & strait crossing gates
        ├── SuperpowerEscalationSystemTests.cs # PACOM Homeland Escalation, DEFCON & CONUS off-map factories
        ├── FocusTreeSystemTests.cs  # Focus prerequisites, mutual exclusions, all 15 EffectType actions & zero-alloc loop
        ├── Content1985BatchTests.cs # 1985 Era I Focus Trees, 7 Major Power OOBs & 41-state East Asia dataset verification
        ├── TwoTierAiAndSoakTests.cs # StrategicMinisterAI, TacticalFrontlineAI & 15-year (21,912-tick) headless soak test (> 3,400 ticks/sec)
        ├── ProductionSystemTests.cs # Hyperbolic efficiency growth, state construction queue & 15-factory cap
        ├── ReinforcementSystemTests.cs # Newest-gen stockpile draw, manpower/XP dilution & end-to-end IC-to-frontline loop
        ├── PathfinderTests.cs       # A* benchmarks (< 5µs/query), DMZ/strait gating & thread-safety
        ├── SimulationWorldTests.cs  # Calendar progression, entity resolution & CSR graph lookups
        ├── MapLoaderTests.cs        # JSON schema validation & asymmetric edge detection
        ├── MapRendererTests.cs      # 16-bit RG color roundtripping & LUT dynamic updates
        ├── WorldSnapshotSerializerTests.cs # Bit-exact .eagsav roundtrip, dynamic ECS restoration & < 5ms benchmark
        ├── NavalTaskForceAndAirWingTests.cs # Task force missions, carrier strikes & convoy interdiction
        ├── CapitulationAndDiplomacyTests.cs # Surrender gates, stockpile transfer & new simulation commands
        └── OperationalPlanAndPincerAiTests.cs # Schwerpunkt spearheads, multi-province pincer attacks & entrenchment

```

### Strict Architectural Invariants
1. **Zero Engine Leakage in Core & Data:** `EastAsiaGSG.Core` and `EastAsiaGSG.Data` never reference `GodotSharp` or any rendering library. The entire simulation is 100% runnable headlessly via xUnit and CLI soak tests.
2. **Data-Oriented Memory Layout:**
   - Fixed-count topology and nation states live in pre-allocated flat contiguous arrays (`Provinces[1500]`, `States[140]`, `Countries[16]`, `Regions[18]`) using explicit-layout blittable `struct` types (`ProvinceData` 32B, `StateData` 56B, `CountryState` 22,296B, `StrategicRegionData` 128B).
   - Monotonic dirty-revision counters (`OwnershipRevision`, `SupplyRevision`, `EconomyRevision`, `AirNavalRevision`) decouple simulation mutation from GPU LUT texture uploads.
   - Zero-allocation spatial indexing (`ProvinceSpatialIndex`) maps provinces to division entity slices in a single $O(D)$ pass per tick.
   - Province adjacency is packed into a global **Compressed Sparse Row (CSR)** graph (`ProvinceGraph`), providing $O(1)$ zero-allocation `ReadOnlySpan<ushort>` neighbor slices.
   - Dynamic entities (Divisions, Active Combats, Production Lines, Naval Task Forces, Air Wings) reside as unmanaged value-type structs in `Arch.Core.World` (Arch ECS v2.1.0).
   - Zero LINQ or heap allocations inside 6-hour or daily tick loops.
3. **One-Way UI Data Binding:** The Godot client reads simulation state arrays to update UI controls and the GPU 1D Lookup Texture (`MapLutBuffer`). Player and AI actions enqueue unmanaged `SimulationCommand` structs (16B) into `CommandRingBuffer`, which are drained deterministically at the start of the next 6-hour tick.

---

## Stratified Tick Pipeline (`SimulationWorld.StepSixHours()`)

Simulation work is stratified by frequency to guarantee **1,000+ ticks/second** in headless mode (15 full years / 21,912 six-hour ticks executes in **$< 6.5\text{ seconds}$** / $> 3,400\text{ ticks/sec}$):

- **Tier 1 — Every 6 Hours (`00:00, 06:00, 12:00, 18:00`):**
  1. `ProvinceSpatialIndex.Rebuild()`: Zero-allocation $O(D)$ bucket sort mapping each province to active divisions.
  2. `CommandProcessor`: Drains queued player and AI commands across all 13 command types (`MoveDivision`, `TogglePgm`, `SelectFocus`, `AssignCivFactories`, `AssignMilFactories`, `ShiftDefcon`, `QueueConstruction`, `CancelConstruction`, `DeclareWar`, `RecruitDivision`, `SwitchProductionEquipment`, `CreateProductionLine`, `AssignNavalMission`).
  3. `DivisionMovementSystem`: Advances division `MovementProgress` along province adjacency edges; initiates combat upon entering hostile territory; automatically replenishes waypoints for long-distance routes.
  4. `CombatResolutionSystem`: Resolves 6-hour HOI4 land combat math:
     - **Hardness Blend:** $A_{\text{base}} = A_s \times (1 - H_t) + A_h \times H_t$.
     - **Combined Arms Synergy:** $+15\%$ combat bonus when combining infantry, armor, and artillery/air support.
     - **Combat Doctrines & Tactical Maneuver Rolls:** 24-hour tactical doctrine counter-play rolls (*AirLand Battle*, *Deep Operations*, *Mass Assault*, *Elastic Defense*).
     - **PGM Authorization:** $+35\%$ combat power multiplier consuming national precision-guided munitions stockpiles.
     - **Armor & Piercing Gate:** $50\%$ damage penalty if target armor exceeds attacker piercing; $+40\%$ org damage if attacker armor pierces target.
     - **Defense & Breakthrough Mitigation:** $10\%$ blocked hit roll, $40\%$ unblocked hit roll.
     - **Combat Width & Reserve Commitment:** Base width 80 (+20 per flanking province) with continuous reserve filling and contiguous slice compaction.
     - **Retreat & Shatter:** Broken divisions withdraw to adjacent friendly provinces or shatter completely if encircled.
  5. `AirAndNavalRegionSystem`: Computes contested air superiority and naval control ratios across the 18 Strategic Regions based on discrete `NavalTaskForceComponent` missions (`Patrol`, `Strike`, `ConvoyEscort`, `ConvoyInterdiction`, `AmphibiousSupport`) and `AirWingComponent` power (`FighterAirDefense`, `CasGroundSupport`, `NavalStrike`, `StrategicInterdiction`), applies weather modifiers, accounts for coastal A2/AD missile batteries (Radar/SAM Lv 3+ with PGM), deploys Carrier Strike Groups (CSGs), and enforces the Strait & Amphibious Control Gate (> 60% Naval Control and > 50% Air Superiority required for crossing).
- **Tier 2 — Daily Tick (`00:00`):**
  1. `CapitulationSystem`: Evaluates province `VictoryPoints` (0..50) and national `Stability` daily; when an enemy controls the capital and VP ratio drops below stability threshold, the country surrenders, transferring all provinces, capturing $50\%$ of equipment stockpiles, clearing active combats, and triggering global news events.
  2. `OperationalPlanSystem`: Evaluates frontlines to concentrate armored/mechanized *Schwerpunkt* spearhead objectives on enemy capitals and supply hubs while assigning infantry screens to hold defensive borders.
  3. `SupplyGraphSystem`: Executes multi-source Dijkstra overland supply distribution from operational Supply Hubs, Ports, and Railways; tracks Trans-Siberian Artery bottleneck throughput; applies submarine wolfpack convoy interdiction penalties to overseas ports; applies local factory trickle to isolated pockets; calculates division supply consumption and updates `ProvinceSupplyRatio[1500]`.
  4. `ConstructionSystem`: Calculates available Civilian Factories ($C_{\text{free}} = \max(0, C_{\text{total}} - \lceil \text{TotalFactories} \times \text{ConsumerGoodsRatio} \rceil + C_{\text{trade}})$), enforces the 15-factory per-state cap, advances State building progress ($\Delta P = C_{\text{assigned}} \times 5.0 \times (1 + \text{SpeedMod})$), and increments building levels.
  5. `ProductionSystem`: Evaluates priority-ordered `ProductionLineComponent` entities across the 8 Equipment Archetypes (`InfantryKit`, `SupportVehicles`, `IFV`, `MBT`, `Artillery`, `AttackHeli`, `TacticalAir`, `PGM`), applies strategic resource shortage penalties (`Steel`, `Semiconductors`, `Oil`, `Aluminum`), updates hyperbolic Production Efficiency ($\Delta E_i = 0.001 \times (E_{\text{cap}}^2 / E_i)$), and increments national `EquipmentStockpile`.
  6. `ReinforcementSystem`: Transfers manpower and equipment (`Gen 3 -> Gen 0` priority) from national stockpiles to depleted field divisions in valid supply (`SupplyRatio >= 0.15f`), dilutes division experience proportionally when absorbing green replacements, and recovers daily `Organization`.
  7. `SuperpowerEscalationSystem`: Evaluates theatre threat scoring (Seoul, Taiwan, US forward bases, active Bluefor combat), updates US PACOM `HomelandEscalation` (15..100) and theatre `DEFCON` (5..1), scales CONUS off-map military factories (10..200), ships daily materiel convoys at DEFCON 3 or below, and mobilizes coalition readiness postures.
  8. `FocusTreeSystem`: Advances active National Focuses (`FocusDaysRemaining`), accrues daily Political Power (`+2.0/day` base, `-1.0/day` while executing an active focus), evaluates all 11 `FocusConditionType` gates, and fires `FocusEffectExecutor` across all 15 `FocusEffectType` actions when `FocusDaysRemaining == 0`.
  9. `TacticalFrontlineAISystem`: Computes daily border threat/desirability gradients for AI-controlled nations, coordinates multi-province pincer attacks (+20 combat width bonus), respects entrenchment hysteresis (units with $> 5.0$ entrenchment defend tenaciously), and issues wartime offensive movement commands with automatic PGM authorization when combat power ratio exceeds `1.15x`.
- **Tier 3 — Monthly Tick (`Day 1 at 00:00`):**
  1. `MacroEconomySystem`: Applies historical Cold War macro-economic trends—Soviet/DPRK 1990s economic overheating & Arduous March consumer goods pressure, and East Asian export miracle growth (`ROK`, `TWN`, `JPN`, `PRC`) with periodic civilian factory expansion.
  2. `StrategicMinisterAISystem`: Evaluates monthly geopolitical utility scores for AI nations to select available National Focuses, queue State construction projects, balance Military Factory allocations, autonomously recruit new divisions via `CommandType.RecruitDivision`, and declare war via `CommandType.DeclareWar` when wargoals exist and readiness thresholds are met.

---

## Tactical Sandbox, Industrial HUD & Controls (`EastAsiaGSG.GodotClient`)

The Godot presentation client provides an interactive 2D RTS map interface backed by a two-texture GPU shader pipeline (`MapShader.gdshader`), reactive tactical overlays, and an HOI4-inspired Left Side-Drawer & Modal Focus Tree HUD:

### GPU Two-Texture Pipeline & $O(1)$ Picking
1. **`provinces.png` (Static Lossless RGB8 Texture, `2048 x 2048`, `Filter = Nearest`):**
   - Encodes `ProvinceId` directly in the Red and Green channels: `ProvinceId = (R << 8) | G` (`0..1499` for land provinces; `0xFFFF = 65535` for ocean pixels).
   - **$O(1)$ CPU Mouse Picking:** `ProvinceMapBuffer` converts normalized mouse UV $[0, 1]^2$ on the map quad to pixel coordinates $(x, y)$ in a cached CPU byte buffer, resolving `ProvinceId` in $O(1)$ time with zero physics raycasting across all 618 provinces.
2. **`map_lut_tex` (Dynamic 1D Texture, `2048 x 1`, `RGBA8`, `Filter = Nearest`):**
   - Maintained on the CPU by `MapLutBuffer` (`Color32[2048]` / `byte[8192]`).
   - Encodes each province's active Map Mode RGB color in bytes `0..2` and its `ControllerTag` (`0..14` or `255`) in the Alpha channel (`byte 3`).
   - **Revision-Gated Uploads:** Re-uploaded to GPU memory only when monotonic revision counters change (`OwnershipRevision`, `SupplyRevision`, `EconomyRevision`, `AirNavalRevision`).
   - **GPU Border Detection:** `MapShader.gdshader` samples 1-texel and 2-texel cardinal neighbors in `provinces_tex`, drawing thick international borders where `ControllerTag` differs, coastline borders along ocean transitions, and subtle internal province borders where `ProvinceId` differs within the same nation.

### Interactive Tactical Map Overlay (`TacticalMapOverlay.cs`)
- **Frustum Culling & Viewport Optimization:** Decoupled from 144Hz camera pan/zoom; collects Arch ECS dynamic entities only on 6-hour simulation ticks and culls entities outside the camera viewport.
- **HOI4-Style `xN` Stack Aggregation:** Aggregates multiple divisions stationed in the same province into a single counter badge with an `xN` stack indicator, expanding upon click.
- **Division NATO Counters:** Displays country banners, template icons, organization (green) and strength (brown) status bars, and selection halos.
- **Dynamic Movement & Attack Vectors:** Real-time green movement arrows, amber retreat vectors, and red attack vectors with dashed lines along planned waypoint corridors.
- **HOI4-Style Battle Bubbles:** Color-coded 0–100 battle indicators (Green $\ge 55$, Amber $45\text{--}54$, Red $\le 44$) reflecting real-time attacker combat advantage and line breach progress.
- **Combat Telemetry Popup (`CombatTelemetryPopup.cs`):** Click any Battle Bubble to inspect active combat telemetry: committed attacker/defender divisions, organization & strength loss per tick, combat width utilization, active tactical stances, and national PGM stockpile consumption.

### HOI4-Style Industrial, Strategic & Stockpile HUD
- **`NationalStockpileTopBar.cs` (Top-Left HUD):** Displays active nation switcher (`ROK ⇄ PRK`, hotkey `Tab`), Political Power (`PP`), `Stability`, `War Support`, `Manpower`, free/total Civilian & Military Factories, live national equipment stockpiles across all archetypes, US PACOM `HomelandEscalation` telemetry (`Esc: XX%` with DEFCON tooltip), **Active National Focus badge**, and the **Template Recruitment Selector** (`➕ Deploy`).
- **`TimeControlsToolbar.cs` (Top-Right HUD):** Simulation calendar date, Play/Pause/Step buttons, speed controls (`1x`, `2x`, `5x`), dynamic color-coded **DEFCON Alert Badge**, and **Binary Save/Load Controls** (`💾 Save` [F5], `📂 Load` [F9]).
- **`DiplomacyAndNewsModal.cs` (Modal Window, hotkey `D`):** 15-nation diplomatic ledger showing faction allegiances, war statuses, interactive wargoal execution, and historical Cold War news event bulletins.
- **Persistent Left Tab Strip (`MainView.cs`):** Positioned directly below the top bar with mutually-exclusive view modes and active-drawer-only tick refreshes:
  - **`📍 Map Info` (`Esc`):** Displays `ProvinceInfoPanel.cs` with authentic province, state, and region names from `MapMetadataRegistry`, terrain, infrastructure, factories, supply ratio, resources, stationed units, and strategic region balance.
  - **`🏗 Construct (C)` (`C`):** Opens `ConstructionQueuePanel.cs` left side-drawer displaying controlled States, $C_{\text{free}}$ / Consumer Goods breakdown, active State construction progress bars, and one-way command buttons.
  - **`⚙ Produce (P)` (`P`):** Opens `ProductionLinePanel.cs` left side-drawer displaying Military Factory allocation, strategic resources, efficiency bars, daily output rates, factory assignment buttons, and **Equipment Generation Upgrade / Create Line controls**.
  - **`🎯 Focuses (F)` (`F`):** Opens procedural `FocusTreeViewer.cs` modal canvas displaying the active nation's focus tree with prerequisite connector lines, mutual-exclusion indicators, and one-click dispatch.

### Player & Camera Controls
- **Select Division:** Left-Click any on-map division counter (highlights division in cyan and displays its active route).
- **Issue Move / Attack Order:** Right-Click any target province with a division selected ($O(1)$ mouse picking resolves destination and dispatches `MoveDivisionCommand`).
- **Inspect Province:** Left-Click any unselected province to view telemetry in `ProvinceInfoPanel`.
- **Switch Inspected Nation:** Press `Tab` or click the top-left nation button.
- **QuickSave / QuickLoad:** Press `F5` to QuickSave, `F9` to QuickLoad (saves/loads `data/saves/quicksave.eagsav` in $< 2.5\ \text{ms}$).
- **Diplomacy & News Modal:** Press `D` or click `⚔ War`.
- **Toggle Drawers:** Press `C` for Construction, `P` for Production, `F` for Focus Trees, `Esc` for Map Info.
- **Pan Camera:** `W` / `A` / `S` / `D`, Arrow Keys, Viewport Edge Pan, or Middle/Right Mouse Drag (`Shift` for $2\times$ pan speed).
- **Zoom Camera:** Mouse Scroll Wheel (cursor-centered zoom clamped to `[0.25x, 4.0x]`).
- **Time Controls:** `Spacebar` (Play/Pause), `1`/`2`/`3` (1x/2x/5x speed), `.` / `>` (Single Step 6-Hour Tick).
- **Map Modes:** `F1` (Political), `F2` (Terrain), `F3` (Supply), `F4` (Industrial), `F5` (Air Superiority).

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
5. **Headless xUnit Suite:** Runs all **135** unit, integration, and soak tests in `tests/EastAsiaGSG.SimTests` across all modules:
   - `WorldSnapshotSerializerTests` (5 tests: bit-exact state roundtrip, dynamic ECS entity restoration, sub-5ms performance benchmark, invalid header handling, and disk roundtrip).
   - `NavalTaskForceAndAirWingTests` (5 tests: discrete task forces, carrier strike dominance, submarine wolfpack sea-lane interdiction, superpower fleet escalation, and zero-allocation hot loops).
   - `CapitulationAndDiplomacyTests` (6 tests: VP surrender thresholds, capital loss gate, 50% stockpile capture, simulation commands `CreateProductionLine`, `SwitchProductionEquipment`, `AssignNavalMission`, and zero-allocation daily tick).
   - `OperationalPlanAndPincerAiTests` (6 tests: armored/mechanized *Schwerpunkt* spearhead assignment, multi-province coordinated pincer attacks, entrenchment hysteresis, autonomous division recruitment, autonomous wargoal execution, and zero-allocation AI loops).
   - `CombatSystemTests` (15 tests: 72-hour battles, combat width & reserve filling, encirclement shattering, PGM depletion, Combined Arms synergy, 4 combat doctrines, zero-heap allocation checks, and 1,000-battle benchmark in $< 100\text{ ms}$).
   - `SupplyGraphSystemTests` (8 tests: two-tier Dijkstra overland supply, Trans-Siberian bottleneck, island sea lanes, local factory trickle, zero-allocation daily loop checks, and $< 1\text{ ms}$ throughput benchmark).
   - `AirAndNavalRegionSystemTests` (13 tests: contested air superiority, weather attenuation, coastal A2/AD missile suppression, airbase capacity, Strait & Amphibious Control Gates, sea zone LUT rendering, and zero-allocation 6-hour loop checks).
   - `SuperpowerEscalationSystemTests` (8 tests: PACOM threat scoring, Homeland Escalation scaling, DEFCON transitions, CONUS off-map military factories, peacetime decay, Carrier Strike Group deployments, and $< 1\text{ ms}$ throughput benchmark).
   - `FocusTreeSystemTests` (5 tests: JSON loading & FK validation, prerequisites & mutual-exclusion enforcement, all 11 `FocusConditionType` gates, all 15 `FocusEffectType` actions, and zero-allocation daily tick verification).
   - `Content1985BatchTests` (3 tests: All 7 Major Power + Generic 1985 Era I Focus Trees, all 7 Major Power 1985 OOB files, and multi-nation simulation across the 41-state East Asia dataset).
   - `TwoTierAiAndSoakTests` (8 tests: `StrategicMinisterAISystem` monthly focus/construction/factory scoring, `TacticalFrontlineAISystem` garrisoning & offensive execution, automatic resource shortage evaluation, state province capture, full 618-province theatre bijective validation, and the **15-year / 21,912-tick headless soak test** verifying zero exceptions, bounded memory, and $> 3,400\text{ ticks/sec}$ throughput).
   - `ProductionSystemTests` (7 tests: 32-byte blittable layouts, $C_{\text{free}}$ formulas, State construction queue & 15-factory cap, hyperbolic efficiency growth, resource shortage penalties, equipment switch retention, and zero-allocation daily loop checks).
   - `ReinforcementSystemTests` (5 tests: 24-byte `DivisionTemplateRecord` layout, newest-generation-first equipment draw & experience dilution, partial stockpile/manpower/supply gates, end-to-end production-to-frontline reinforcement + PGM resupply, and zero-allocation daily loop checks).
   - `PathfinderTests` (10 tests: zero-allocation verification, $< 5\ \mu\text{s}$ A* performance, DMZ cross-border gating, strait access control, multi-thread safety).
   - `SimulationWorldTests` (10 tests: full 1-year calendar progression, command ring buffer deterministic draining, two-phase division lookup, CSR adjacency graph).
   - `MapLoaderTests` (8 tests: 618-province dataset, foreign key integrity, topological validation).
   - `MapRendererTests` (3 tests: 16-bit RG encoding/decoding, $O(1)$ mouse picking across 618 provinces, dynamic LUT buffer updates).

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

## Development Roadmap Status (Phases 1–11 Complete)

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
- [x] **Phase 5: JSON Focus Engine, Tech Tree & 1985 Content Generation**
  - [x] **Task 5.1:** Implement `FocusTreeSystem` and `FocusEffectExecutor` handling all 15 enum `EffectType` actions.
  - [x] **Task 5.2:** Build the procedural Godot `FocusTreeViewer.tscn` that auto-lays out nodes and prerequisite lines from `/data/focus_trees/*.json`.
  - [x] **Task 5.3:** Batch-generate 1985 OOBs, State definitions, and Era I Focus Trees for `PRC`, `USA`, `SOV`, `JPN`, `ROK`, `PRK`, and `TWN`.
- [x] **Phase 6: Two-Tier Utility AI & Polish**
  - [x] **Task 6.1:** Implement `StrategicMinisterAISystem` (monthly focus/production scoring) and `TacticalFrontlineAISystem` (daily frontline assignment & attack execution).
  - [x] **Task 6.2:** Run 15-year headless soak tests (21,900 six-hour ticks) to verify zero exceptions, zero memory leaks, and historical plausibility.
- [x] **Phase 7: Presentation Optimization, Snapshot Decoupling & Map Metadata**
  - [x] **Task 7.1:** Expanded `ProvinceData` to 32 bytes (`VictoryPoints`, `AirfieldLevel`, `FortificationLevel`) and added monotonic dirty-revision counters (`OwnershipRevision`, `SupplyRevision`, `EconomyRevision`, `AirNavalRevision`).
  - [x] **Task 7.2:** Implemented `MapMetadataRegistry` in `EastAsiaGSG.Data` surfacing authentic State, Province, Strategic Region, Template, and News Event names across all Godot HUD panels.
  - [x] **Task 7.3:** Optimized `MainView` (active-drawer-only refresh on tick), `MapRenderer` (revision-gated GPU LUT uploads), and `TacticalMapOverlay` (frustum culling, tick-gated ECS collection, and HOI4-style `xN` unit stack aggregation).
- [x] **Phase 8: $O(1)$ Spatial Index & Three-Tier Operational AI**
  - [x] **Task 8.1:** Implemented zero-allocation `ProvinceSpatialIndex` in `EastAsiaGSG.Core/ECS` rebuilt in a single $O(D)$ pass per tick, eliminating nested $O(P \times D)$ ECS queries.
  - [x] **Task 8.2:** Implemented daily `OperationalPlanSystem` to concentrate armored/mechanized *Schwerpunkt* spearheads on enemy capitals and supply hubs using reserved bytes in `DivisionComponent`.
  - [x] **Task 8.3:** Upgraded `TacticalFrontlineAISystem` with multi-province coordinated pincer attacks (+20 combat width bonus) and entrenchment hysteresis, and enabled `StrategicMinisterAISystem` to autonomously recruit divisions (`CommandType.RecruitDivision`) and declare war (`CommandType.DeclareWar`).
- [x] **Phase 9: Discrete Pacific Air/Naval Task Forces & Combat Doctrines**
  - [x] **Task 9.1:** Implemented 64-byte `NavalTaskForceComponent` and 32-byte `AirWingComponent` in `EastAsiaGSG.Core/Components`.
  - [x] **Task 9.2:** Upgraded `AirAndNavalRegionSystem` and `SupplyGraphSystem` for task force missions, physical US PACOM / Soviet Pacific Fleet spawns, and submarine sea-lane convoy interdiction reducing port supply throughput.
  - [x] **Task 9.3:** Enhanced `CombatResolutionSystem` with $+15\%$ Combined Arms synergy modifier and 24-hour tactical doctrine counter-play rolls (*AirLand Battle*, *Deep Operations*, *Mass Assault*, *Elastic Defense*).
- [x] **Phase 10: Victory Points, Capitulation, Interactive Diplomacy & Force Designer UI**
  - [x] **Task 10.1:** Implemented daily `CapitulationSystem` evaluating `VictoryPoints` and `Stability` to trigger surrender, province control transfer, 50% stockpile capture, and global news events.
  - [x] **Task 10.2:** Added `SwitchProductionEquipment`, `CreateProductionLine`, and `AssignNavalMission` commands to `SimulationCommand` and `SimulationWorld.DrainCommandQueue`.
  - [x] **Task 10.3:** Built `DiplomacyAndNewsModal.cs` (15-nation faction/war ledger, war declarations, interactive news popups), added OOB Template Recruitment selector to `NationalStockpileTopBar`, and added equipment generation upgrade/creation controls to `ProductionLinePanel`.
- [x] **Phase 11: High-Density Theatre Scaling (600+ Provinces) & Binary Save/Load**
  - [x] **Task 11.1:** Implemented `WorldSnapshotSerializer` in `EastAsiaGSG.Data/Serialization` using `MemoryMarshal.AsBytes` and `ZLibStream` for $< 2.5\ \text{ms}$ `.eagsav` binary save/load roundtripping, wired to `Save` [F5] / `Load` [F9] buttons in `TimeControlsToolbar`.
  - [x] **Task 11.2:** Scaled theatre to **618 provinces** across 41 states with Voronoi relaxation, preserved river/rail/strait networks, and generated 2048x2048 RGB8 texture with 100% $O(1)$ mouse picking accuracy.
  - [x] **Task 11.3:** Executed 15-year headless soak benchmark (21,912 ticks) on 618 provinces in **6.4 seconds** (> 3,420 ticks/sec) with zero errors, zero warnings, and zero memory leaks.

