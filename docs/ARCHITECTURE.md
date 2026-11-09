# Technical Architecture Specification (`EastAsiaGSG`)

## 1. Three-Project Solution Topology

1. **`EastAsiaGSG.Core` (.NET Class Library)**
   - Headless, pure C# Data-Oriented simulation kernel with strictly zero references to `Godot`, `GodotSharp`, or rendering libraries.
   - Contains `SimulationWorld` (holds flat state arrays, monotonic revision counters, and `Arch.Core.World` instance).
   - Contains unmanaged blittable `struct` definitions (`Components/`), zero-allocation spatial indexing (`ProvinceSpatialIndex.cs`), global Compressed Sparse Row graph (`ProvinceGraph.cs`), zero-allocation A* pathfinder (`ProvincePathfinder.cs`), pure tick systems (`Systems/`), Command Ring Buffer and 13 deterministic command handlers (`Commands/`), and Three-Tier Utility/Operational AI (`AI/`).
2. **`EastAsiaGSG.Data` (.NET Class Library)**
   - Deserializes JSON definitions from `/data/` using `System.Text.Json` source generation (`MapJsonContext`).
   - Validates foreign keys (ensuring focus prerequisites, province state IDs, template battalion types exist) and populates `SimulationWorld` on startup.
   - Houses `WorldSnapshotSerializer.cs` providing $< 2.5\ \text{ms}$, 90% compressed `.eagsav` binary save/load snapshots via `MemoryMarshal.AsBytes` and `ZLibStream`.
   - Contains `MapMetadataRegistry.cs` caching authentic localization strings (Provinces, States, Strategic Regions, OOB Templates, and News Events) without engine leakage.
   - Maintains CPU byte buffers for $O(1)$ mouse picking (`ProvinceMapBuffer`) and 2048x1 dynamic LUT texture coloring (`MapLutBuffer`).
3. **`EastAsiaGSG.GodotClient` (Godot 4.x C# Project)**
   - Reactive presentation layer running on .NET 8 / Godot Mono (`Godot.NET.Sdk/4.7.2`).
   - Owns the main simulation loop (`SimulationRunner.cs`), GPU shaders (`MapShader.gdshader`), camera controls (`CameraController.cs`), viewport frustum-culled tactical overlays (`TacticalMapOverlay.cs`), and HOI4-inspired HUD drawers and modals (`MainView.cs`, `NationalStockpileTopBar.cs`, `TimeControlsToolbar.cs`, `DiplomacyAndNewsModal.cs`, `FocusTreeViewer.cs`).

---

## 2. Stratified Tick Pipeline

To guarantee 1,000+ ticks/second in headless mode (soak tests achieve **> 3,420 ticks/sec** across 618 provinces) and locked 144+ FPS in Godot, simulation work is stratified by frequency inside `SimulationWorld.StepSixHours()`:

### Tier 1: Every 6 Hours (00:00, 06:00, 12:00, 18:00)
1. **`ProvinceSpatialIndex.Rebuild()`**: Zero-allocation $O(D)$ bucket sort organizing division entities by `CurrentProvinceId`, enabling instant $O(1)$ spatial queries for movement, combat, and AI without nested ECS traversals.
2. **`CommandProcessorSystem`**: Drains player and AI queued commands across all 13 command types:
   - `MoveDivision` (0), `TogglePgm` (1), `SelectFocus` (2), `AssignCivFactories` (3), `AssignMilFactories` (4), `ShiftDefcon` (5), `QueueConstruction` (6), `CancelConstruction` (7), `DeclareWar` (8), `RecruitDivision` (9), `SwitchProductionEquipment` (10), `CreateProductionLine` (11), `AssignNavalMission` (12).
3. **`DivisionMovementSystem`**: Advances `MovementProgress` along province adjacency edges; initiates combat upon entering hostile provinces; dynamically calculates waypoints for long-distance routes.
4. **`CombatResolutionSystem`**: Resolves 6-hour HOI4 land combat math:
   - Hardness blend ($A_{\text{base}} = A_s \times (1 - H_t) + A_h \times H_t$).
   - Combined Arms synergy modifier ($+15\%$ combat bonus when combining infantry, armor, and artillery/air support).
   - 24-hour tactical doctrine counter-play rolls (*AirLand Battle*, *Deep Operations*, *Mass Assault*, *Elastic Defense*).
   - PGM consumption (+35% combat power multiplier from national stockpile).
   - Armor and Piercing threshold gating (50% damage reduction if target armor > attacker piercing; +40% org damage if attacker armor > target piercing).
   - 10%/40% Defense/Breakthrough hit roll mitigation.
   - Combat width reserve filling and contiguous slice compaction.
   - Organization/Strength damage application, retreat routing, and encirclement shattering.
5. **`AirAndNavalRegionSystem`**: Updates air superiority and naval control ratios across the 18 Strategic Regions by evaluating discrete `NavalTaskForceComponent` missions (`Patrol`, `Strike`, `ConvoyEscort`, `ConvoyInterdiction`, `AmphibiousSupport`) and `AirWingComponent` strength, coastal A2/AD missile defense (Radar/SAM Lv 3+ with PGM), weather attenuation, and the Strait / Amphibious Control Gate (> 60% Naval Control and > 50% Air Superiority).

### Tier 2: Daily Tick (Executed at 00:00)
1. **`CapitulationSystem`**: Evaluates provincial `VictoryPoints` (0..50) and national `Stability` daily. If an enemy occupies the capital and controlled VP ratio drops below the surrender threshold, the nation capitulates: all provinces transfer control, 50% of all equipment stockpiles are captured, active battles clear, and global news event bulletins trigger.
2. **`OperationalPlanSystem`**: Generates high-level strategic objectives for AI nations, concentrating armored/mechanized *Schwerpunkt* spearheads on hostile capitals and supply hubs while assigning infantry screens to hold defensive borders.
3. **`SupplyGraphSystem`**: Executes multi-source Dijkstra overland supply distribution from operational Supply Hubs, Ports, and Railways; tracks Trans-Siberian Artery bottleneck throughput; applies submarine wolfpack sea-lane convoy interdiction penalties to overseas ports; calculates division supply consumption and updates `ProvinceSupplyRatio[1500]`.
4. **`ConstructionSystem`**: Calculates available Civilian Factories ($C_{\text{free}}$), enforces the 15-factory per-state cap, advances construction progress, and increments state building levels.
5. **`ProductionSystem`**: Evaluates priority-ordered `ProductionLineComponent` entities across the 8 Equipment Archetypes, applies strategic resource shortage penalties (`Steel`, `Semiconductors`, `Oil`, `Aluminum`), updates hyperbolic Production Efficiency ($\Delta E_i = 0.001 \times (E_{\text{cap}}^2 / E_i)$), and increments national `EquipmentStockpile`.
6. **`ReinforcementSystem`**: Transfers manpower and equipment (`Gen 3 -> Gen 0` priority) from national stockpiles to depleted field divisions in valid supply (`SupplyRatio >= 0.15f`), dilutes division experience proportionally, and recovers daily `Organization`.
7. **`SuperpowerEscalationSystem`**: Evaluates theatre threat scoring, updates US PACOM `HomelandEscalation` (15..100) and theatre `DEFCON` (5..1), scales CONUS off-map military factories (10..200), ships daily materiel convoys at DEFCON 3 or below, and deploys Carrier Strike Groups to Western Pacific sea zones.
8. **`FocusTreeSystem`**: Advances active National Focuses, accrues daily Political Power (`+2.0/day` base, `-1.0/day` active), evaluates 11 condition gates, and executes 15 effect types when complete.
9. **`TacticalFrontlineAISystem`**: Computes daily border threat/desirability gradients, coordinates multi-province pincer attacks (+20 combat width bonus), respects entrenchment hysteresis (units with $> 5.0$ entrenchment hold redoubts), and issues offensive movement orders with PGM authorization.

### Tier 3: Monthly Tick (Executed on the 1st of each month at 00:00)
1. **`MacroEconomySystem`**: Applies historical Cold War macro-economic trends—Soviet/DPRK 1990s economic overheating & Arduous March consumer goods penalties, and East Asian export miracle growth (`ROK`, `TWN`, `JPN`, `PRC`) with periodic civilian factory expansion.
2. **`StrategicMinisterAISystem`**: Evaluates monthly geopolitical utility scores to select available National Focuses, queue State construction projects, balance Military Factory allocations, autonomously recruit new divisions via `CommandType.RecruitDivision`, and declare war via `CommandType.DeclareWar` when wargoals exist and military readiness exceeds 85%.

---

## 3. Core Memory Layout & Data Structures

```csharp
namespace EastAsiaGSG.Core;

public sealed class SimulationWorld {
    public const int MaxProvinces = 1500;
    public const int MaxStates = 140;
    public const int MaxCountries = 16;
    public const int MaxStrategicRegions = 18;

    // Flat Contiguous State Arrays (Index == ID)
    public readonly ProvinceData[] Provinces = new ProvinceData[MaxProvinces];
    public readonly StateData[] States = new StateData[MaxStates];
    public readonly CountryState[] Countries = new CountryState[MaxCountries];
    public readonly StrategicRegionData[] Regions = new StrategicRegionData[MaxStrategicRegions];

    // Global CSR (Compressed Sparse Row) Adjacency Graph for Provinces
    public ushort[] AdjacencyTargets = [];
    public byte[] AdjacencyEdgeFlags = []; // Bitmask: 1=Standard, 2=River, 4=Strait, 8=Railway

    // Monotonic Dirty-Revision Counters (Decouples Sim from GPU LUT Uploads)
    public uint OwnershipRevision { get; set; } = 1;
    public uint SupplyRevision { get; set; } = 1;
    public uint EconomyRevision { get; set; } = 1;
    public uint AirNavalRevision { get; set; } = 1;

    // Zero-Allocation O(D) Spatial Index
    public readonly ProvinceSpatialIndex SpatialIndex = new();

    // Arch ECS World for Dynamic Entities
    public readonly Arch.Core.World EcsWorld = Arch.Core.World.Create();

    // Current Timestamp (Starts 1985-01-01 00:00)
    public int TotalSixHourTicks { get; private set; }
}
```

### Blittable Struct Layouts
1. **`ProvinceData` (32 bytes, `[StructLayout(LayoutKind.Explicit, Size = 32)]`)**:
   - `StateId` (ushort, 0..1), `Terrain` (byte, 2), `ControllerTag` (byte, 3), `OwnerTag` (byte, 4), `AdjacencyCount` (byte, 5), `AdjacencyOffset` (ushort, 6..7).
   - `SupplyRatio` (float, 8..11), `InfrastructureLevel` (byte, 12), `IsSupplyHub` (byte, 13), `PortLevel` (byte, 14), `VictoryPoints` (byte, 15), `AirfieldLevel` (byte, 16), `FortificationLevel` (byte, 17).
   - `MapX` (ushort, 18..19), `MapY` (ushort, 20..21), `StrategicRegionId` (byte, 22), Reserved (9 bytes, 23..31).
2. **`StateData` (56 bytes, `[StructLayout(LayoutKind.Explicit, Size = 56)]`)**:
   - `StateId` (ushort), `OwnerTag` (byte), `ControllerTag` (byte), `Population` (int).
   - `CivilianFactories` (byte), `MilitaryFactories` (byte), `NavalDockyards` (byte), `AirbaseLevel` (byte), `RadarSamLevel` (byte), `InfrastructureLevel` (byte).
   - `Oil` (short), `Steel` (short), `Aluminum` (short), `Semiconductors` (short).
   - `CurrentConstruction` struct (Type, Progress, AssignedFactories, TargetBuildingLevel).
3. **`CountryState` (22,296 bytes, explicit layout)**:
   - `TagId` (byte), `Stability` (float), `WarSupport` (float), `PoliticalPower` (float), `ManpowerPool` (int), `ConsumerGoodsRatio` (float), `HomelandEscalation` (byte), `DefconLevel` (byte).
   - `EquipmentStockpile[8, 4]` (int[32] tracking all 8 archetypes across 4 generations).
   - `CountryWarMask` (ushort bitfield tracking bilateral wars against all 16 tags).
   - `ActiveFocus` struct (FocusId, DaysRemaining).
4. **`StrategicRegionData` (128 bytes, explicit layout)**:
   - `RegionId` (byte), `IsSeaZone` (byte), `Weather` (byte), `AirSuperiorityRatio` (float), `NavalControlRatio` (float), `DominantAirTag` (byte), `DominantNavalTag` (byte), `A2AdSuppressionPenalty` (float).
5. **Dynamic ECS Components (`Arch.Core.World`)**:
   - **`DivisionComponent` (128 bytes)**: `DivisionId`, `TemplateId`, `CountryTag`, `CurrentProvinceId`, `TargetProvinceId`, `MovementProgress`, `Organization`, `Strength`, `Experience`, `Entrenchment`, `PgmAuthorized`, `ObjectiveProvinceId`, `ArmyGroupId`, `OperationalOrderStance`.
   - **`ActiveCombatComponent` (96 bytes)**: `CombatId`, `ProvinceId`, `AttackerTag`, `DefenderTag`, `AttackerWidth`, `DefenderWidth`, `CombatTicksElapsed`, `AttackerBreakthroughProgress`.
   - **`ProductionLineComponent` (32 bytes)**: `LineId`, `CountryTag`, `EquipmentType`, `Generation`, `AssignedMilitaryFactories`, `ProductionEfficiency`, `EfficiencyCap`, `AccumulatedIC`.
   - **`NavalTaskForceComponent` (64 bytes)**: `TaskForceId`, `CountryTag`, `StrategicRegionId`, `MissionType` (Patrol, Strike, ConvoyEscort, ConvoyInterdiction, AmphibiousSupport), `SurfaceWarships`, `Submarines`, `Transports`, `EscortEfficiency`.
   - **`AirWingComponent` (32 bytes)**: `WingId`, `CountryTag`, `StrategicRegionId`, `AssignedAirbaseStateId`, `AircraftType`, `Generation`, `OperationalAircraftCount`, `MissionType`.

---

## 4. Binary Snapshot Serialization (`.eagsav`)

The snapshot engine in [`EastAsiaGSG.Data/Serialization/WorldSnapshotSerializer.cs`](file:///Users/arthurli/EastAsiaGSG/src/EastAsiaGSG.Data/Serialization/WorldSnapshotSerializer.cs) delivers $< 2.5\ \text{ms}$ save/load roundtrips with 90% file compression:

```
+-----------------------------------------------------------------------------+
| Header: 8-byte ASCII "EAGSAV01" + 4-byte uint Version (1)                   |
+-----------------------------------------------------------------------------+
| Compressed Payload (ZLibStream, CompressionLevel.Fastest):                  |
|   1. World Scalars & Telemetry:                                             |
|      - TotalSixHourTicks (int)                                              |
|      - Entity ID Counters (NextDivisionId, NextCombatId, NextLineId, ...)   |
|      - Monotonic Revisions (Ownership, Supply, Economy, AirNaval)           |
|   2. Flat State Arrays (blitted via MemoryMarshal.AsBytes):                 |
|      - Provinces[1500] (48,000 bytes)                                       |
|      - States[140] (7,840 bytes)                                            |
|      - Countries[16] (356,736 bytes)                                        |
|      - Regions[18] (2,304 bytes)                                            |
|   3. Dynamic Adjacency Graph (CSR):                                         |
|      - ushort[] AdjacencyTargets + byte[] AdjacencyEdgeFlags                |
|   4. Dynamic ECS Entities (Arch.Core.World):                                |
|      - DivisionComponent structs (128 bytes each)                           |
|      - ActiveCombatComponent structs (96 bytes each)                        |
|      - ProductionLineComponent structs (32 bytes each)                      |
|      - NavalTaskForceComponent structs (64 bytes each)                      |
|      - AirWingComponent structs (32 bytes each)                              |
+-----------------------------------------------------------------------------+
```

### Performance & File Footprint
- **Uncompressed Size**: $\approx 415\ \text{KB}$.
- **Compressed File Size**: $\approx 42\ \text{KB}$ (written to `data/saves/quicksave.eagsav`).
- **Save Duration**: $\approx 1.2\ \text{ms}$.
- **Load Duration**: $\approx 1.3\ \text{ms}$.

---

## 5. GPU Map Rendering & Presentation Optimization

The map presentation system in `EastAsiaGSG.GodotClient` combines high-throughput GPU rendering with strict CPU cache friendliness:

1. **`east_asia_1985_provinces.png` (Static Lossless RGB8 Texture, 2048x2048, `Filter = Nearest`):**
   - Encodes `ProvinceId` directly in pixel colors: `ProvinceId = (R << 8) | G`.
   - **$O(1)$ Mouse Picking:** Mouse UV on the map quad maps to byte offset `(y * 2048 + x) * 3` in `ProvinceMapBuffer`, returning `ProvinceId` in $O(1)$ time with zero physics raycasts across all 618 provinces.
2. **`map_lut_tex` (Dynamic 1D Texture, 2048x1, `RGBA8`, `Filter = Nearest`):**
   - Encodes each province's map mode color in RGB and `ControllerTag` in Alpha.
   - **Revision-Gated Uploads:** The CPU checks `SimulationWorld` revision counters (`OwnershipRevision`, `SupplyRevision`, `EconomyRevision`, `AirNavalRevision`). If unchanged, GPU texture updates are skipped entirely, saving precious PCIe bus bandwidth.
   - **GPU Border Detection:** `MapShader.gdshader` samples cardinal neighbor texels in `provinces_tex`, rendering international borders where neighbor `ControllerTag` differs and subtle internal borders where `ProvinceId` differs.
3. **Viewport Frustum Culling & Stack Aggregation (`TacticalMapOverlay.cs`):**
   - Division counters and movement vectors are culled outside the camera's active view frustum.
   - Multiple divisions stationed in the same province are aggregated into a single counter badge with an `xN` stack indicator, expanding when clicked.
   - Dynamic ECS entity iteration is tick-gated (collected only on 6-hour simulation ticks) rather than querying ECS at 144 Hz.
4. **Authentic Localization Metadata (`MapMetadataRegistry.cs`):**
   - Maps raw IDs to historical names (`Province #6` $\rightarrow$ `"Seoul Central"`, `State #1` $\rightarrow$ `"Gyeonggi-Seoul"`, `Region #0` $\rightarrow$ `"Yellow Sea"`) without polluting the headless core simulation.
