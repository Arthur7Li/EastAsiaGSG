# Project: East Asia 1985–2000 Grand Strategy Game (EastAsiaGSG)

A high-performance, regional Cold War Gone Hot Grand Strategy Game (1985–2000) covering the East Asian and Western Pacific theatre (618 operational provinces, 41 states, 18 strategic regions, 15 nation tags). Inspired by vanilla Hearts of Iron IV (HOI4) core loops, stripped of DLC bloat, and built on a headless Data-Oriented C# simulation core with a Godot 4.x (.NET) reactive presentation client.

## Contextual Documentation References
- @[Architecture Specification](docs/ARCHITECTURE.md)
- @[Game Design & Mathematical Formulas](docs/GAME_DESIGN_SPEC.md)
- @[JSON Data Schemas](docs/DATA_SCHEMAS.md)
- @[Development Roadmap](docs/ROADMAP.md)

## Build, Test & Verification Commands
Always run the headless simulation verification suite after modifying any file in `src/EastAsiaGSG.Core`, `src/EastAsiaGSG.Data`, or `src/EastAsiaGSG.GodotClient`:
- **Automated Verification Suite:** `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh`
- **Build with Zero Warnings:** `dotnet build EastAsiaGSG.sln --warnaserror`
- **Run Headless Simulation Tests:** `dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"`

## Strict Architectural Invariants (DO NOT VIOLATE)
1. **Zero Engine Leakage in Core/Data:** `EastAsiaGSG.Core` and `EastAsiaGSG.Data` MUST NEVER reference `GodotSharp` or any rendering library. The simulation must be 100% runnable headlessly via xUnit and CLI soak tests.
2. **Data-Oriented Memory Layout:**
   - Static/fixed-count topology and nation states MUST reside in flat, pre-allocated contiguous arrays (`ProvinceData[1500]` 32B, `StateData[140]` 56B, `CountryState[16]` 22,296B, `StrategicRegionData[18]` 128B).
   - Dynamic entities (Divisions 128B, Active Combats 96B, Production Lines 32B, Naval Task Forces 64B, Air Wings 32B) MUST be stored as unmanaged `struct` components in `Arch.Core.World` (Arch ECS v2.1.0).
   - Spatial indexing MUST use zero-allocation $O(D)$ single-pass bucket sorting via `ProvinceSpatialIndex`.
   - Monotonic dirty-revision counters (`OwnershipRevision`, `SupplyRevision`, `EconomyRevision`, `AirNavalRevision`) decouple simulation changes from GPU LUT texture updates.
   - Never use class inheritance hierarchies, LINQ allocations (`Where`, `Select`, `ToList`), or heap allocations inside 6-hour or daily tick loops. Use `Span<T>`, `ReadOnlySpan<T>`, and value-type structs.
3. **Pure Systems & Determinism:** Every simulation system (`CombatResolutionSystem`, `SupplyGraphSystem`, `ProductionSystem`, `CapitulationSystem`, `FocusTreeSystem`) must be deterministic and operate solely on `SimulationWorld` and `Arch.Core.World`.
4. **One-Way UI Data Binding:** `EastAsiaGSG.GodotClient` reads simulation arrays to update UI Control nodes and the GPU 1D Color Lookup Texture (`MapLutBuffer`). Player actions in Godot UI MUST NOT mutate simulation state directly; they must enqueue a deterministic `SimulationCommand` struct (16B) processed at the start of the next simulation tick.
5. **Binary Snapshot Serialization:** Saved games must be serialized via `WorldSnapshotSerializer.cs` into `.eagsav` files using unmanaged memory blitting (`MemoryMarshal.AsBytes`) and `ZLibStream(CompressionLevel.Fastest)` for sub-5ms roundtrips.

## Coding Conventions
- **Language:** C# 12 / .NET 8+ with `<Nullable>enable</Nullable>` and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for fixed-size struct buffers where appropriate.
- **IDs & Indexing:** Use `ushort` for `ProvinceId` (0..1499) and `StateId` (0..139). Use `byte` for `CountryTag` (0..15) and `EquipmentType` (0..7). Array index always equals the ID.
- **Testing Requirement:** Every new System in `EastAsiaGSG.Core/Systems` requires a corresponding headless deterministic test in `tests/EastAsiaGSG.SimTests` verifying edge cases and performance.
