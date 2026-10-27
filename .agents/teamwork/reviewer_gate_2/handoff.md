# Handoff Report — Reviewer 2 (Independent Code Reviewer & Adversarial Critic)

- **Date**: 2026-10-27T19:57:00Z
- **Role**: Independent Code Reviewer & Adversarial Critic
- **Folder**: `/Users/arthurli/EastAsiaGSG/.agents/teamwork/reviewer_gate_2`
- **Handoff Type**: Hard Handoff (Independent Review Complete)
- **Recipient**: Orchestrator (`50e3c984-ecdf-4083-b717-1763964755ed`)
- **Verdict**: **APPROVE**

---

## 1. Observation

1. **Defect R2.1 (Combat Telemetry UI)**:
   - File: `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs` (Line 267).
   - Observed change: `* 100f` multiplier removed:
     ```csharp
     $"Recent Str Loss: Def -{foundCombat.LastTickAttackerStrengthDamage:F2}% | Att -{foundCombat.LastTickDefenderStrengthDamage:F2}%";
     ```

2. **Defect R2.2 (Division Identifier Ambiguity in `TryFindDivisionById`)**:
   - File: `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs` (Lines 123–162).
   - Observed change: Replaced single-pass disjunction with two-phase query across Arch ECS chunks:
     - Phase 1: `divisions[i].DivisionId == divisionOrEntityId`
     - Phase 2: `entities[i].Id == divisionOrEntityId`

3. **Defect R2.3 (Combat Width Reserve Frontier Allocation Truncation)**:
   - File: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (Lines 238–268).
   - Observed change: Replaced `break` with `continue` and added tuple swap compaction:
     ```csharp
     (attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
     usedAttackerWidth += width;
     engagedAttackerCount++;
     ```
     with matching logic for defenders.

4. **Defect R2.4 (Multi-Hop Waypoint Continuation for Long-Distance Routes)**:
   - Files: `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs` (Lines 63, 236–252) and `CombatResolutionSystem.cs` (Lines 582–598).
   - Observed change: `pathBuffer` expanded to `stackalloc ushort[128]`. On waypoint exhaustion (`nextIndex >= div.WaypointCount`), if `DestinationProvinceId != CurrentProvinceId && DestinationProvinceId != NoProvince`, calls `IssueMoveOrder(world, ref div, div.DestinationProvinceId)`. If `IssueMoveOrder` fails, executes `div.ClearWaypoints(); div.State = DivisionState.Idle;`.

5. **Defect R2.5 (Retreat State Consistency)**:
   - File: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (Lines 480–524).
   - Observed change: In `RetreatOrShatterDefender`, duplicate sequential writes removed; sets `defender.CurrentProvinceId = bestRetreatProvince; defender.TargetProvinceId = NoProvince; defender.RetreatProvinceId = NoProvince; defender.MovementProgress = 0.0f; defender.ClearWaypoints(); defender.State = DivisionState.Idle;`.

6. **Regression Unit Tests**:
   - Files: `tests/EastAsiaGSG.SimTests/SimulationWorldTests.cs` (Lines 374–432) and `tests/EastAsiaGSG.SimTests/CombatSystemTests.cs` (Lines 748–936).
   - Observed: 4 new tests exercising R2.2, R2.3, R2.4, and R2.5.

7. **Independent Automated Verification Commands & Outputs**:
   - Command: `dotnet build EastAsiaGSG.sln --warnaserror`
     - Result: `Build succeeded. 0 Warning(s) 0 Error(s). Time Elapsed 00:00:09.07`
   - Command: `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh`
     - Result:
       - Step 1: Zero engine leakage: PASS; Zero LINQ: PASS; One-way references: PASS.
       - Step 2: 0 Warnings in build: PASS.
       - Step 3: Headless tests: Total tests: 43. Passed: 43. Failed: 0. Time: 1.29s: PASS.
       - Step 4: Verification Summary: ALL HEADLESS SIMULATION VERIFICATION CHECKS PASSED.
   - Command: `dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"`
     - Result: `Passed: 43. Failed: 0. Total time: 1.1755 Seconds.`
   - Command: `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50`
     - Result: Exit code 0, 50 frames completed, map and starting divisions loaded cleanly.

8. **Integrity Audit**:
   - Verified zero hardcoded test constants, zero dummy/facade implementations, zero task bypasses, zero fabricated outputs.

---

## 2. Logic Chain

1. *From Observation 1*: Removing `* 100f` eliminates the 100x display inflation in `CombatTelemetryPopup.cs`.
2. *From Observation 2*: Two-phase search ensures user/JSON-defined `DivisionId` is evaluated before internal ECS `Entity.Id`, resolving ID collisions without heap allocation.
3. *From Observation 3*: Replacing `break` with `continue` ensures smaller reserve units behind larger units are evaluated. The in-place tuple swap compaction `(attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount])` packs all committed units into `[0..engagedAttackerCount-1]`, guaranteeing downstream exchange loops process engaged units and skip unengaged reserves. Bounds are strictly preserved since `0 <= engagedAttackerCount <= i < attackerTotal <= 32`.
4. *From Observation 4*: When a division traverses 16 waypoints without reaching its destination, re-pathing via `IssueMoveOrder` continues movement towards `DestinationProvinceId`. If the destination is unreachable, `IssueMoveOrder` returns `false`, causing the division to clear waypoints and transition to `DivisionState.Idle`, strictly preventing infinite re-pathing loops. Expanding `stackalloc ushort` buffer from 64 to 128 prevents stack buffer truncation on long paths across the 1500-province graph.
5. *From Observation 5*: Atomically assigning `CurrentProvinceId = bestRetreatProvince`, `TargetProvinceId = NoProvince`, `RetreatProvinceId = NoProvince`, `MovementProgress = 0.0f`, `ClearWaypoints()`, and `State = Idle` cleans up all retreat state, preventing dirty lingering progress or double-state transitions.
6. *From Observations 6 & 7*: The 4 regression tests and 39 existing unit tests pass cleanly in 1.18s with zero warnings, zero engine leaks, and zero LINQ allocations. Godot runs headlessly with 0 errors.
7. *From Observation 8*: No integrity violations exist.

---

## 3. Caveats

No caveats. All verification commands, architectural invariants, and edge cases were independently executed, audited, and confirmed.

---

## 4. Conclusion

- **Defects R2.1–R2.5**: Repaired completely and correctly.
- **Architectural Invariants**: 100% compliant (0 engine leaks, 0 LINQ in hot tick loops, strictly one-way references).
- **Array Bounds & Swap Safety (R2.3)**: Fully verified and mathematically safe.
- **Re-Pathing Termination (R2.4)**: Termination strictly guaranteed upon unreachable destinations.
- **Retreat State Consistency (R2.5)**: Clean, zero dirty state.
- **Performance & Zero Allocation**: Preserved across all tick systems.
- **Verification**: 43/43 tests pass, 0 compiler warnings, Godot headless check passes.
- **Integrity**: Clean.
- **Final Verdict**: **APPROVE**.

---

## 5. Verification Method

To reproduce and verify this review independently:
1. **Full Verification Script**:
   ```bash
   ./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
   ```
   *Expected: All 4 steps PASS, 43/43 tests pass, 0 warnings.*
2. **WarnAsError Build**:
   ```bash
   dotnet build EastAsiaGSG.sln --warnaserror
   ```
   *Expected: Build succeeded. 0 Warning(s) 0 Error(s).*
3. **Headless Unit Tests**:
   ```bash
   dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"
   ```
   *Expected: 43 Passed, 0 Failed.*
4. **Godot Headless Client**:
   ```bash
   /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50
   ```
   *Expected: Exit code 0.*
5. **Invalidation Conditions**:
   - If `attackers[engagedAttackerCount]` exceeds `attackerTotal` or throws `IndexOutOfRangeException`, R2.3 is invalidated.
   - If a division whose destination is unreachable enters an infinite re-pathing loop, R2.4 is invalidated.
   - If an unengaged division takes damage in `CombatWidthReserveFilling`, R2.3 is invalidated.
   - If a retreated defender retains `MovementProgress > 0.0f` or dirty `TargetProvinceId`, R2.5 is invalidated.
   - If `TryFindDivisionById` resolves an entity ID when a division with that division ID exists, R2.2 is invalidated.
