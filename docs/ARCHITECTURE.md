# Technical Architecture Specification (`EastAsiaGSG`)

## 1. Three-Project Solution Topology

1. **`EastAsiaGSG.Core` (.NET Class Library)**
   - Contains `SimulationWorld` (holds flat state arrays + `Arch.Core.World` instance).
   - Contains unmanaged `struct` Definitions (`Components/`), pure tick logic (`Systems/`), Command Queue processor (`Commands/`), and the Two-Tier Utility AI (`AI/`).
2. **`EastAsiaGSG.Data` (.NET Class Library)**
   - Deserializes JSON definitions from `/data/` using `System.Text.Json` source generation.
   - Validates foreign keys (e.g., ensuring every Focus prerequisite ID and Province StateId exists) and populates `SimulationWorld` on startup.
3. **`EastAsiaGSG.GodotClient` (Godot 4.x C# Project)**
   - Owns the main loop timer (`SimulationRunner.cs`), rendering shaders (`MapShader.gdshader`), and Control UI scenes (`.tscn`).

---

## 2. Stratified Tick Pipeline

To guarantee 1,000+ ticks/second in headless mode and locked 144+ FPS in Godot, simulation work is stratified by frequency inside `SimulationWorld.StepSixHours()`:

### Tier 1: Every 6 Hours (00:00, 06:00, 12:00, 18:00)
1. `CommandProcessorSystem`: Drains player and AI queued commands (`MoveDivisionCommand`, `TogglePgmCommand`, `AssignFactoryCommand`).
2. `DivisionMovementSystem`: Advances `MovementProgress` along province adjacency edges; triggers combat if entering a hostile province.
3. `CombatResolutionSystem`: Resolves 6-hour HOI4 land combat math (Soft/Hard attack blend, Armor/Piercing gate, 10%/40% Defense/Breakthrough hit roll, PGM consumption, Org/Strength damage).
4. `AirAndNavalRegionSystem`: Updates air superiority and naval control ratios across the 18 Strategic Regions.

### Tier 2: Daily Tick (Executed at 00:00)
1. `SupplyGraphSystem`: Runs Dijkstra/Max-Flow over the ~150 Supply Hub / Port / Rail nodes (including the Soviet Trans-Siberian entry node and US PACOM sea lanes); updates `ProvinceSupplyRatio[1500]`.
2. `ProductionSystem`: Calculates factory output across the 8 Equipment Archetypes, updates hyperbolic Production Efficiency, and increments national `EquipmentStockpile`.
3. `ReinforcementSystem`: Transfers equipment and manpower from national stockpiles to depleted field divisions in valid supply.
4. `FocusAndResearchSystem`: Advances active National Focuses (+1 day) and Tech slots; fires `FocusEffectExecutor` when `FocusDaysRemaining == 0`.
5. `TacticalFrontlineAISystem`: Computes border threat/desirability gradients and issues division movement orders for AI countries.

### Tier 3: Monthly Tick (Executed on the 1st of each month)
1. `MacroEconomySystem`: Recalculates Consumer Goods ratios, Soviet/DPRK economic overheating penalties, East Asian export growth bonuses, and US/Soviet Off-Map reinforcement arrivals.
2. `StrategicMinisterAISystem`: Evaluates geopolitical utility scores to select National Focuses, adjust factory allocations, and shift diplomatic/DEFCON stances.

---

## 3. Core Memory Layout (`SimulationWorld.cs`)

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
    // Provinces[id].AdjacencyOffset indexes into AdjacencyTargets & AdjacencyEdgeFlags
    public ushort[] AdjacencyTargets = [];
    public byte[] AdjacencyEdgeFlags = []; // Bitmask: 1=Standard, 2=River, 4=Strait, 8=Railway

    // Arch ECS World for Dynamic Entities (Divisions, ActiveCombats, ProductionLines)
    public readonly Arch.Core.World EcsWorld = Arch.Core.World.Create();

    // Current Timestamp (Starts 1985-01-01 00:00)
    public int TotalSixHourTicks { get; private set; }
}
```

---

## 4. GPU Map Rendering & $O(1)$ Picking Protocol

The map renderer inside `EastAsiaGSG.GodotClient/Map/` uses a two-texture shader pipeline:
1. **`provinces.png` (Static RG8/RGB8 Texture, lossless, `Filter = Nearest`):**
   - Encodes `ProvinceId` directly in pixel colors: `ProvinceId = (R << 8) | G` (supporting up to 65,535 provinces with just two channels).
   - **Mouse Picking:** Convert mouse UV on the map quad to pixel coordinates $(x, y)$ in a cached CPU `byte[]` copy of `provinces.png`. Read `ProvinceId` in $O(1)$ time with zero physics raycasting.
2. **`map_lut.png` (Dynamic 1D Texture, `Width = 2048, Height = 1, Format = RGBA8, Filter = Nearest`):**
   - Maintained on the CPU as `Color32[2048]`.
   - Whenever province ownership changes or the player switches Map Modes (Political, Terrain, Supply, Industrial, Air Superiority), the CPU loops `for (int i = 0; i < 1500; i++)` to write each province's target color into `lutBuffer[i]` and calls `lutTexture.Update(lutImage)`.
   - **Shader Border Detection:** The fragment shader samples the 4 cardinal neighbor pixels in `provinces.png`. If a neighbor has a different `ProvinceId`, it draws a subtle province border; if the neighbor's `ProvinceId` belongs to a different `ControllerTag` (encoded in the LUT alpha channel or secondary row), it draws a thick country border.