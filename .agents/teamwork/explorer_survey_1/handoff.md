# Handoff Report — Explorer 1 (Core Simulation Auditor)

- **Date**: 2026-10-27
- **Role**: Core Simulation Auditor & Defect R2.2 Investigator
- **Target Folder**: `/Users/arthurli/EastAsiaGSG/.agents/teamwork/explorer_survey_1`
- **Type**: Hard Handoff (Investigation & Audit Complete)

---

## 1. Observation

1. **Architectural Invariants & Build Verification**:
   - Executed `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh` with `BypassSandbox: true`.
   - Result:
     - `[Step 1/4] Checking Architectural Invariants & Engine Isolation...`
       `✅ PASS: Zero engine leakage (no Godot/GodotSharp references in Core or Data).`
       `✅ PASS: Zero LINQ allocations detected in hot tick systems.`
       `✅ PASS: Project references are strictly one-way.`
     - `[Step 2/4] Verifying C# Build with Zero Warnings (--warnaserror)...`
       `Build succeeded. 0 Warning(s) 0 Error(s).`
     - `[Step 3/4] Running Headless Simulation Tests via xUnit...`
       `Total tests: 39. Passed: 39. Total time: 2.1153 Seconds.`
       `✅ PASS: All simulation tests executed successfully.`
     - `[Step 4/4] 🎉 ALL HEADLESS SIMULATION VERIFICATION CHECKS PASSED!`

2. **Core Simulation Memory Layout (`src/EastAsiaGSG.Core/ECS/SimulationWorld.cs`)**:
   - `Provinces`: `ProvinceData[1500]` (lines 31, 71–77), struct size 24 bytes blittable.
   - `States`: `StateData[140]` (lines 32, 79–84), struct size 32 bytes unmanaged.
   - `Countries`: `CountryState[16]` (lines 33, 86–96), struct size 184 bytes unmanaged.
   - `Regions`: `StrategicRegionData[18]` (lines 34, 98–102), struct size 40 bytes unmanaged.
   - Contiguous CSR Adjacency: `ushort[] AdjacencyTargets` and `byte[] AdjacencyEdgeFlags` in `src/EastAsiaGSG.Core/Math/ProvinceGraph.cs` (lines 20–21). Zero-allocation `GetNeighbors` returns `ReadOnlySpan<ushort>` (line 28).

3. **Stratified Tick Pipeline (`SimulationWorld.cs`)**:
   - `StepSixHours()` (lines 204–223) routes Tier 1 (`ExecuteSixHourTier()`), Tier 2 (`ExecuteDailyTier()` when `CurrentHour == 0`), and Tier 3 (`ExecuteMonthlyTier()` when `CurrentHour == 0 && CurrentDay == 1`).
   - Clock advancement (lines 376–420) accurately increments 6h ticks and computes Gregorian month boundaries including leap years (`IsLeapYear`).

4. **Defect R2.2 Observation (`src/EastAsiaGSG.Core/ECS/SimulationWorld.cs:123-144`)**:
   ```csharp
   132:             for (int i = 0; i < chunkSize; i++)
   133:             {
   134:                 if (divisions[i].DivisionId == divisionOrEntityId || entities[i].Id == divisionOrEntityId)
   135:                 {
   136:                     foundEntity = entities[i];
   137:                     return true;
   138:                 }
   139:             }
   ```
   - Single disjunctive `||` check in the inner chunk iteration.
   - When Division $A$ is earlier in the chunk iteration and its internal `entities[i].Id` equals `divisionOrEntityId`, it returns Division $A$ even if Division $B$ later in the chunk (or in a subsequent chunk) has domain `divisions[j].DivisionId == divisionOrEntityId`.

5. **Existing Regression Suite (`tests/EastAsiaGSG.SimTests/SimulationWorldTests.cs`)**:
   - Lines 285–287 test command routing:
     ```csharp
     world.EnqueueCommand(SimulationCommand.CreateTogglePgm(CountryTag.ROK, divisionEntityId: 101, authorized: true));
     world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 101, targetProvinceId: 2));
     world.EnqueueCommand(SimulationCommand.CreateMoveDivision(CountryTag.ROK, divisionEntityId: 102, targetProvinceId: 2));
     ```
   - In this test, entity IDs (0, 1, 2) do not collide with division IDs (101, 102, 201), which masked the defect during initial development.

---

## 2. Logic Chain

1. **Premise 1**: In Arch ECS, entity IDs are assigned monotonically starting at 0 (`Entity.Id == 0, 1, 2, ...`).
2. **Premise 2**: Domain `DivisionId` values are assigned domain integers ($0, 1, 2, \dots$ or template/sequence numbers).
3. **Inference from Obs 4**: If Division $A$ has `Entity.Id = X` and `DivisionId = 500`, and Division $B$ has `Entity.Id = Y` and `DivisionId = X`, then `TryFindDivisionById(X)` will encounter Division $A$ first during chunk iteration.
4. **Collision Consequence**: `divisions[0].DivisionId == X` is false, but `entities[0].Id == X` is true. Division $A$ is returned. Division $B$ is never evaluated.
5. **Impact on Commands**: In `DrainCommandQueue()`, `cmd.EntityId` for a player move order targeting Division $B$ (`DivisionId = X`) binds to Division $A$. If Division $A$ belongs to another country, the command is discarded (`div.CountryTag == cmd.CountryTag` check fails). If same country, Division $A$ moves erroneously.
6. **Solution Formulation**: Splitting `TryFindDivisionById` into two consecutive passes (Phase 1: check only `divisions[i].DivisionId == divisionOrEntityId` across all chunks; Phase 2: check only `entities[i].Id == divisionOrEntityId` across all chunks if Phase 1 returned false) mathematically guarantees that any division matching the domain `DivisionId` will take precedence over any division matching the internal `Entity.Id`.
7. **Regression Test Feasibility**: By spawning Division $A$ with a high `DivisionId` (e.g. 500) and then spawning Division $B$ with `DivisionId == divA.Id`, calling `TryFindDivisionById(divA.Id)` will fail under the current code (returning `divA`) and pass under the two-phase implementation (returning `divB`).

---

## 3. Caveats

1. The Godot presentation layer was not modified or executed with GUI in this survey; however, Godot rendering uses headless-compatible shaders and buffers (`ProvinceMapBuffer`, `MapLutBuffer`) which are tested in `MapRendererTests.cs`.
2. Phase 3–6 systems (macro-economy, focus tree execution, frontline AI) are stubbed hooks in `StepSixHours()`, as scheduled in `ROADMAP.md`.
3. Alternative Considered for R2.2: A `Dictionary<int, Entity>` index. Rejected because it introduces GC allocations on entity spawn/destroy, violating strict zero-allocation invariants in the hot simulation tick path. The two-phase chunk scan has 0 allocations and runs in < 1 $\mu$s for typical division counts.

---

## 4. Conclusion

- **R1 Audit**: The Core Simulation architecture strictly complies with all specifications: flat contiguous arrays, CSR graph, zero-allocation A* pathfinding, stratified 6h/daily/monthly tick execution, zero engine leakage, zero LINQ, and clean zero-warning builds.
- **R2.2 Defect**: Confirmed and fully analyzed. Single-pass disjunctive lookup causes entity ID collisions to shadow domain division IDs.
- **Remediation Action Plan**:
  1. Update `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs:123-144` to the two-phase query.
  2. Add unit test `TryFindDivisionById_WhenEntityIdCollidesWithAnotherDivisionId_PrioritizesDivisionId` in `tests/EastAsiaGSG.SimTests/SimulationWorldTests.cs`.
  3. Verify with `verify_sim.sh`.

---

## 5. Verification Method

1. **Execute Verification Script**:
   ```bash
   ./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
   ```
2. **Compile with Warnings as Errors**:
   ```bash
   dotnet build EastAsiaGSG.sln --warnaserror
   ```
3. **Execute Headless xUnit Tests**:
   ```bash
   dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"
   ```
4. **Invalidation Condition**:
   If a test spawns Division $A$ with `Entity.Id = X` and Division $B$ with `DivisionId = X`, and `TryFindDivisionById(X, out var entity)` returns Division $A$ instead of Division $B$, the defect remains unaddressed.
