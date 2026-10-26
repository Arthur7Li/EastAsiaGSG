# Project: East Asia 1985–2000 Grand Strategy Game (EastAsiaGSG)

A high-performance, regional Cold War Gone Hot Grand Strategy Game (1985–2000) covering the East Asian and Western Pacific theatre (~1,500 provinces, 140 states, 15 nation tags). Inspired by vanilla Hearts of Iron IV (HOI4) core loops, stripped of DLC bloat, and built on a headless Data-Oriented C# simulation core with a Godot 4.x (.NET) reactive presentation client.

## Contextual Documentation References
- @[Architecture Specification](docs/ARCHITECTURE.md)
- @[Game Design & Mathematical Formulas](docs/GAME_DESIGN_SPEC.md)
- @[JSON Data Schemas](docs/DATA_SCHEMAS.md)
- @[Development Roadmap](docs/ROADMAP.md)

## Build, Test & Verification Commands
Always run headless simulation unit tests after modifying any file in `src/EastAsiaGSG.Core` or `src/EastAsiaGSG.Data`:
- **Build Core & Data:** `dotnet build src/EastAsiaGSG.Core src/EastAsiaGSG.Data`
- **Run Headless Simulation Tests:** `dotnet test tests/EastAsiaGSG.SimTests --logger "console;verbosity=detailed"`

## Strict Architectural Invariants (DO NOT VIOLATE)
1. **Zero Engine Leakage in Core/Data:** `EastAsiaGSG.Core` and `EastAsiaGSG.Data` MUST NEVER reference `GodotSharp` or any rendering library. The simulation must be 100% runnable headlessly via xUnit and CLI soak tests.
2. **Data-Oriented Memory Layout:**
   - Static/fixed-count topology and nation states MUST reside in flat, pre-allocated contiguous arrays (`ProvinceData[1500]`, `StateData[140]`, `CountryState[16]`, `StrategicRegion[18]`).
   - Dynamic entities (Brigades/Divisions, Active Combats, Air Wings, Naval Task Forces) MUST be stored as unmanaged `struct` components in `Arch.Core.World` (Arch ECS v2.1.0).
   - Never use class inheritance hierarchies, LINQ allocations (`Where`, `Select`, `ToList`), or heap allocations inside 6-hour or daily tick loops. Use `Span<T>`, `ReadOnlySpan<T>`, and value-type structs.
3. **Pure Systems:** Every simulation system (`CombatSystem`, `SupplyGraphSystem`, `ProductionSystem`, `FocusTreeSystem`) must be deterministic and operate solely on `SimulationState` and `Arch.Core.World`.
4. **One-Way UI Data Binding:** `EastAsiaGSG.GodotClient` reads simulation arrays to update UI Control nodes and the GPU 1D Color Lookup Texture (`MapLut`). Player actions in Godot UI MUST NOT mutate `SimulationState` directly; they must enqueue a deterministic `ICommand` struct (e.g., `SelectFocusCommand`, `MoveDivisionCommand`, `AssignFactoriesCommand`) processed at the start of the next simulation tick.

## Coding Conventions
- **Language:** C# 12 / .NET 8+ with `<Nullable>enable</Nullable>` and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for fixed-size struct buffers where appropriate.
- **IDs & Indexing:** Use `ushort` for `ProvinceId` (0..1499) and `StateId` (0..139). Use `byte` for `CountryTag` (0..15) and `EquipmentType` (0..7). Array index always equals the ID.
- **Testing Requirement:** Every new System in `EastAsiaGSG.Core/Systems` requires a corresponding headless deterministic test in `tests/EastAsiaGSG.SimTests` verifying edge cases and performance.