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
│   │   ├── Components/              # Blittable fixed-size structs & domain enums
│   │   ├── Math/                    # Compressed Sparse Row (CSR) ProvinceGraph
│   │   ├── Commands/                # Unmanaged SimulationCommand & CommandRingBuffer
│   │   └── ECS/                     # SimulationWorld & stratified StepSixHours() pipeline
│   ├── EastAsiaGSG.Data/            # Source-generated JSON loaders & CPU map/LUT buffers
│   │   ├── Schemas/                 # System.Text.Json DTOs & MapJsonContext
│   │   ├── Loaders/                 # JsonMapLoader with bijective graph validation
│   │   └── Map/                     # ProvinceMapBuffer (O(1) picking) & MapLutBuffer (2048x1 RGBA8)
│   └── EastAsiaGSG.GodotClient/     # Godot 4.x (.NET 8) reactive presentation client
│       ├── Map/                     # MapShader.gdshader, MapRenderer.cs, CameraController.cs
│       ├── UI/                      # MapModeToolbar.cs & ProvinceInfoPanel.cs
│       ├── MainView.cs              # Root scene controller wiring world, map, camera & HUD
│       └── MainView.tscn            # Main Godot 4 presentation scene
└── tests/
    └── EastAsiaGSG.SimTests/        # Headless xUnit verification & benchmark suite
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
  1. `CommandProcessor`: Drains queued player and AI commands (`SelectFocus`, `ShiftDefcon`, `AssignFactory`, `MoveDivision`, `TogglePgm`).
  2. `DivisionMovementSystem`, `CombatResolutionSystem`, and `AirAndNavalRegionSystem` hooks.
- **Tier 2 — Daily Tick (`00:00`):**
  1. `SupplyGraphSystem`, `ProductionSystem`, `ReinforcementSystem`, `FocusAndResearchSystem`, and `TacticalFrontlineAISystem`.
- **Tier 3 — Monthly Tick (`Day 1 at 00:00`):**
  1. `MacroEconomySystem` and `StrategicMinisterAISystem`.

---

## GPU Two-Texture Map Renderer & $O(1)$ Picking

The map renderer (`src/EastAsiaGSG.GodotClient/Map/`) uses a two-texture GPU shader pipeline (`MapShader.gdshader`):
1. **`provinces.png` (Static Lossless RGB8 Texture, `Filter = Nearest`):**
   - Encodes `ProvinceId` directly in the Red and Green channels: `ProvinceId = (R << 8) | G` (`0..1499` for land provinces; `0xFFFF = 65535` for ocean pixels).
   - **$O(1)$ CPU Mouse Picking:** `ProvinceMapBuffer` converts normalized mouse UV $[0, 1]^2$ on the map quad to pixel coordinates $(x, y)$ in a cached CPU byte buffer, resolving `ProvinceId` in $O(1)$ time with zero physics raycasting.
2. **`map_lut_tex` (Dynamic 1D Texture, `2048 x 1`, `RGBA8`, `Filter = Nearest`):**
   - Maintained on the CPU by `MapLutBuffer` (`Color32[2048]` / `byte[8192]`).
   - Encodes each province's active Map Mode RGB color in bytes `0..2` and its `ControllerTag` (`0..14` or `255`) in the Alpha channel (`byte 3`).
   - **GPU Border Detection:** `MapShader.gdshader` samples 1-texel and 2-texel cardinal neighbors in `provinces_tex`, drawing thick international/DMZ borders where `ControllerTag` differs, coastline borders along ocean transitions, and subtle internal province borders where `ProvinceId` differs within the same nation.

### Interactive Camera & Map Mode Controls (`EastAsiaGSG.GodotClient`)
- **Pan Camera:** `W` / `A` / `S` / `D`, Arrow Keys, Viewport Edge Pan, or Middle/Right Mouse Drag (hold `Shift` for $2\times$ speed).
- **Zoom Camera:** Mouse Scroll Wheel (cursor-centered zoom clamped to `[0.25x, 4.0x]`).
- **Inspect / Select Province:** Hover or Left-Click any province to highlight it on the GPU shader and inspect its state, owner/controller, terrain, supply ratio, infrastructure, factories, and strategic resources (`Oil`, `Steel`, `Aluminum`, `Semiconductors`) in `ProvinceInfoPanel`.
- **Switch Map Modes:** Click the bottom-right `MapModeToolbar` or press function hotkeys:
  - `F1` — **Political Map Mode**
  - `F2` — **Terrain Map Mode**
  - `F3` — **Supply Map Mode**
  - `F4` — **Industrial Map Mode**
  - `F5` — **Air Superiority Map Mode**

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
5. **Headless xUnit Suite:** Runs all unit and benchmark tests in `tests/EastAsiaGSG.SimTests`.

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
- [ ] **Phase 2: Korean Peninsula Tactical Sandbox (Movement & Combat)**
- [ ] **Phase 3: Industrial Economy, Equipment Stockpiles & Reinforcements**
- [ ] **Phase 4: Supply Graph, Air/Naval Regions & Off-Map Superpowers**
- [ ] **Phase 5: JSON Focus Engine, Tech Tree & 1985 Content Generation**
- [ ] **Phase 6: Two-Tier Utility AI & 15-Year Headless Soak Tests**
