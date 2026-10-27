## 2026-10-27T19:27:31Z
You are Worker 1: Systems Repair & Regression Test Implementer.
Your working directory is /Users/arthurli/EastAsiaGSG/.agents/teamwork/worker_repair_1

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A forensic auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

MANDATORY READS BEFORE STARTING:
- Read /Users/arthurli/EastAsiaGSG/.agents/teamwork/ORIGINAL_REQUEST.md
- Read /Users/arthurli/EastAsiaGSG/.agents/teamwork/orchestrator/PROJECT.md
- Read /Users/arthurli/EastAsiaGSG/.agents/skills/verify-headless-sim/SKILL.md
- Read Explorer 1 handoff: /Users/arthurli/EastAsiaGSG/.agents/teamwork/explorer_survey_1/handoff.md
- Read Explorer 2 handoff: /Users/arthurli/EastAsiaGSG/.agents/teamwork/explorer_survey_2/handoff.md
- Read Explorer 3 handoff: /Users/arthurli/EastAsiaGSG/.agents/teamwork/explorer_survey_3/handoff.md

FILE EXCLUSIVE OWNERSHIP:
You own and may edit the following files:
1. `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs` (R2.1)
2. `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs` (R2.2)
3. `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (R2.3, R2.5)
4. `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs` (R2.4)
5. `tests/EastAsiaGSG.SimTests/SimulationWorldTests.cs` (Regression tests)
6. `tests/EastAsiaGSG.SimTests/CombatSystemTests.cs` (Regression tests)
7. If needed for solution build consistency, `EastAsiaGSG.sln`.

IMPLEMENTATION SPECIFICATION:
1. Defect R2.1 (Combat Telemetry UI):
   In `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs:267`, remove the erroneous `* 100f` multiplier for both `LastTickAttackerStrengthDamage` and `LastTickDefenderStrengthDamage`.

2. Defect R2.2 (Division Identifier Ambiguity in TryFindDivisionById):
   In `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs` lines 123-144, implement a zero-allocation two-phase search across all chunks:
   - Pass 1: check if `divisions[i].DivisionId == divisionOrEntityId`. If found, set `foundEntity = entities[i]` and return true.
   - Pass 2: if Pass 1 found nothing, check if `entities[i].Id == divisionOrEntityId`. If found, set `foundEntity = entities[i]` and return true.
   - Return false if not found.

3. Defect R2.3 (Combat Width Reserve Frontier Allocation Truncation):
   In `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` lines 248-264:
   - Replace `break` with `continue`.
   - Perform in-place array swap compaction so engaged divisions occupy indices `0..engagedAttackerCount-1` (and `0..engagedDefenderCount-1`):
     ```csharp
     (attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
     usedAttackerWidth += width;
     engagedAttackerCount++;
     ```
     Apply identical logic for defenders.

4. Defect R2.4 (Multi-Hop Waypoint Continuation for Long-Distance Routes):
   In `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs`:
   - Line 63: expand stackalloc buffer from 64 to 128 elements: `Span<ushort> pathBuffer = stackalloc ushort[128];`.
   - In `AdvanceMovement` (around line 236): when `nextIndex >= div.WaypointCount`, check if `div.DestinationProvinceId != div.CurrentProvinceId && div.DestinationProvinceId != DivisionComponent.NoProvince`. If so, call `IssueMoveOrder(ref div, div.DestinationProvinceId, provinces, adjacencyTargets, pathfinder)`. If that succeeds, the division continues moving toward destination; if it returns false or already at destination, clear waypoints and set `State = DivisionState.Idle`.
   - In `CombatResolutionSystem.cs:581-585` (`ConcludeAttackerVictory`), apply consistent waypoint continuation check.

5. Defect R2.5 (Retreat State Consistency):
   In `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs:509-515` (`RetreatOrShatterDefender`):
   - Remove dead sequential writes.
   - Set:
     ```csharp
     defender.CurrentProvinceId = bestRetreatProvince;
     defender.TargetProvinceId = DivisionComponent.NoProvince;
     defender.RetreatProvinceId = DivisionComponent.NoProvince;
     defender.MovementProgress = 0.0f;
     defender.ClearWaypoints();
     defender.State = DivisionState.Idle;
     ```

6. Regression Unit Tests:
   - In `tests/EastAsiaGSG.SimTests/SimulationWorldTests.cs`:
     Add unit test proving `TryFindDivisionById` prioritizes `DivisionId` when entity ID collides with another division's `DivisionId`.
   - In `tests/EastAsiaGSG.SimTests/CombatSystemTests.cs`:
     Add regression tests for:
     a) Combat width reserve filling when a larger division exceeds width but a smaller one fits.
     b) Multi-hop waypoint continuation past 16 hops.
     c) Retreat state consistency verifying `MovementProgress == 0.0f`, `State == Idle`, `TargetProvinceId == NoProvince`.

7. Automated Verification:
   Run and verify clean execution of:
   - `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh`
   - `dotnet build EastAsiaGSG.sln --warnaserror`
   - `dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"`
   - `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50`

DELIVERABLES:
- Write detailed change report to `/Users/arthurli/EastAsiaGSG/.agents/teamwork/worker_repair_1/report.md`
- Write handoff report to `/Users/arthurli/EastAsiaGSG/.agents/teamwork/worker_repair_1/handoff.md`
- Use `send_message` to communicate completion back to the orchestrator.
