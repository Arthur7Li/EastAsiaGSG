# Defect Repair and Regression Verification Report

**Author**: Worker 1 (Systems Repair & Regression Test Implementer)  
**Date**: 2026-10-27T19:37:00Z  
**Scope**: Defects R2.1–R2.5, Regression Test Suite, and Automated Verification  
**Repository**: `/Users/arthurli/EastAsiaGSG`  

---

## 1. Executive Summary

All five confirmed in-scope defects (R2.1, R2.2, R2.3, R2.4, and R2.5) across simulation, movement, combat resolution, and UI presentation have been repaired following the minimal change principle. Four comprehensive regression unit tests were authored in `SimulationWorldTests.cs` and `CombatSystemTests.cs`. 

The entire solution builds cleanly with **0 warnings and 0 errors** under `--warnaserror`. The automated verification script `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh` confirms **100% test pass rate (43/43 tests)** with zero engine leaks, zero LINQ allocations in hot simulation paths, and strict one-way architectural references.

---

## 2. Detailed Defect Analysis & Implemented Repairs

### Defect R2.1: Combat Telemetry UI Strength Loss 100x Multiplier
- **File**: `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs` (Line 267)
- **Root Cause**: `CombatResolutionSystem` computes strength damage as `expectedHits * 0.04f` (e.g. 15 hits = 0.60f). Division strength is bounded in `[0.0, 1.0]` and decremented by `damage * 0.01f`, meaning `0.60` already represents 0.60%. `ActiveCombatComponent.LastTickAttackerStrengthDamage` and `LastTickDefenderStrengthDamage` store this percentage directly. In `CombatTelemetryPopup.cs:267`, an erroneous `* 100f` multiplier was present:
  ```csharp
  $"Recent Str Loss: Def -{foundCombat.LastTickAttackerStrengthDamage * 100f:F2}% | Att -{foundCombat.LastTickDefenderStrengthDamage * 100f:F2}%";
  ```
  This inflated the reported strength loss by 100x (displaying `60.00%` instead of `0.60%`).
- **Fix**: Removed `* 100f` for both attacker and defender strength damage values:
  ```csharp
  _damageStatsLabel.Text = $"Recent Org Loss: Def -{foundCombat.LastTickAttackerOrgDamage:F1} | Att -{foundCombat.LastTickDefenderOrgDamage:F1}\n" +
                           $"Recent Str Loss: Def -{foundCombat.LastTickAttackerStrengthDamage:F2}% | Att -{foundCombat.LastTickDefenderStrengthDamage:F2}%";
  ```

---

### Defect R2.2: Division Identifier Ambiguity in `TryFindDivisionById`
- **File**: `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs` (Lines 123–160)
- **Root Cause**: In Arch ECS, entity IDs start at 0 (`Entity.Id == 0, 1, 2, ...`). The previous implementation used a single-pass disjunctive check:
  ```csharp
  if (divisions[i].DivisionId == divisionOrEntityId || entities[i].Id == divisionOrEntityId)
  ```
  If Division $A$ was created earlier with `Entity.Id = X` and `DivisionId = 9999`, and Division $B$ had domain `DivisionId = X`, querying for `X` would evaluate Division $A$ first. Because `entities[i].Id == X` was true, Division $A$ was returned immediately, shadowing Division $B$. When processing commands (`CreateMoveDivision`, `CreateTogglePgm`), commands intended for Division $B$ were erroneously misrouted to Division $A$.
- **Fix**: Replaced with a zero-allocation, two-phase chunk iteration:
  - **Phase 1**: Iterates chunks matching strictly `divisions[i].DivisionId == divisionOrEntityId`. If found, sets `foundEntity = entities[i]` and returns `true`.
  - **Phase 2**: If Phase 1 found no match, iterates chunks matching fallback `entities[i].Id == divisionOrEntityId`. If found, sets `foundEntity = entities[i]` and returns `true`.
  - Returns `false` if neither match.
- **Performance**: Zero heap allocations, pure struct enumerators (`Query`), $< 1\ \mu\text{s}$ execution time.

---

### Defect R2.3: Combat Width Reserve Frontier Allocation Truncation
- **File**: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (Lines 240–267)
- **Root Cause**: Frontline division allocation loops for attackers and defenders previously used `break` when a division exceeded remaining combat width:
  ```csharp
  if (engagedAttackerCount == 0 || usedAttackerWidth + width <= maxCombatWidth)
  {
      usedAttackerWidth += width;
      engagedAttackerCount++;
  }
  else
  {
      break;
  }
  ```
  If a 60-width division was engaged first under an 80-width cap, and the next division was 40-width, `break` halted evaluation immediately. A subsequent 20-width division that would have perfectly filled the remaining 20 width was never evaluated, leaving 20 combat width permanently wasted. Furthermore, downstream code indexes `attackers[0..engagedAttackerCount-1]`.
- **Fix**: 
  1. Replaced `break` with `continue`.
  2. Implemented zero-allocation in-place swap compaction for both attackers and defenders:
     ```csharp
     (attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
     usedAttackerWidth += width;
     engagedAttackerCount++;
     ```
  This guarantees that all committed units are packed contiguously into indices `0..engagedAttackerCount-1` (and `0..engagedDefenderCount-1`), while unengaged reserve divisions reside past the engaged boundary in the stackalloc span.

---

### Defect R2.4: Multi-Hop Waypoint Continuation for Long-Distance Routes
- **Files**:
  - `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs` (Lines 63, 236–252)
  - `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (Lines 582–598)
- **Root Cause**: 
  1. In `DivisionMovementSystem.IssueMoveOrder`, the stackalloc buffer was limited to 64 elements (`stackalloc ushort[64]`). Long-distance routes across large graphs risked failing path reconstruction.
  2. The unmanaged `DivisionComponent` fixed struct buffer holds at most 16 waypoints (`MaxWaypoints = 16`). When a division reached waypoint 16, `AdvanceMovement` unconditionally cleared waypoints and set `State = DivisionState.Idle`, stranding divisions mid-journey even if `DestinationProvinceId` was still distant.
  3. A similar termination occurred in `CombatResolutionSystem.ConcludeAttackerVictory` when capturing a province on the final waypoint hop.
- **Fix**:
  1. Expanded `pathBuffer` stackalloc in `IssueMoveOrder` to 128 elements:
     ```csharp
     Span<ushort> pathBuffer = stackalloc ushort[128];
     ```
  2. In `DivisionMovementSystem.AdvanceMovement`: When `nextIndex >= div.WaypointCount`, added a check:
     ```csharp
     if (div.DestinationProvinceId != div.CurrentProvinceId
         && div.DestinationProvinceId != DivisionComponent.NoProvince)
     {
         if (!IssueMoveOrder(world, ref div, div.DestinationProvinceId))
         {
             div.ClearWaypoints();
             div.State = DivisionState.Idle;
         }
     }
     else
     {
         div.ClearWaypoints();
         div.State = DivisionState.Idle;
     }
     ```
  3. In `CombatResolutionSystem.ConcludeAttackerVictory`: Applied the identical continuation check when advancing victorious attacking divisions.

---

### Defect R2.5: Retreat State Consistency
- **File**: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (Lines 508–517)
- **Root Cause**: In `RetreatOrShatterDefender`, retreating defenders were subjected to dead sequential writes that left dirty state:
  ```csharp
  defender.CurrentProvinceId = bestRetreatProvince;
  defender.RetreatProvinceId = DivisionComponent.NoProvince;
  defender.State = DivisionState.Retreating;
  defender.TargetProvinceId = bestRetreatProvince;
  defender.MovementProgress = 1.0f;
  defender.State = DivisionState.Idle;
  defender.TargetProvinceId = DivisionComponent.NoProvince;
  ```
  `State` and `TargetProvinceId` were overwritten twice, and `MovementProgress` was left at `1.0f` on an `Idle` division.
- **Fix**: Cleaned up the assignment to execute an atomic, clean transition:
  ```csharp
  if (bestRetreatProvince != DivisionComponent.NoProvince)
  {
      defender.CurrentProvinceId = bestRetreatProvince;
      defender.TargetProvinceId = DivisionComponent.NoProvince;
      defender.RetreatProvinceId = DivisionComponent.NoProvince;
      defender.MovementProgress = 0.0f;
      defender.ClearWaypoints();
      defender.State = DivisionState.Idle;
  }
  ```

---

## 3. Regression Test Implementations

Four new automated regression tests were added across `SimulationWorldTests.cs` and `CombatSystemTests.cs`:

### 1. `SimulationWorldTests.TryFindDivisionById_PrioritizesDivisionId_WhenCollidingWithAnotherEntityId`
- **Objective**: Proves that when Division $A$ has `Entity.Id = X` and Division $B$ has `DivisionId = X`, `TryFindDivisionById(X)` returns Division $B$ (prioritizing domain Division ID).
- **Secondary Assertion**: Validates fallback to `Entity.Id` when no domain division ID matches.
- **Result**: PASSED (< 1 ms).

### 2. `CombatSystemTests.CombatSystem_CombatWidthReserveFilling_SkipsLargeExceedingDivisionAndCommitsSmallerDivision`
- **Objective**: Simulates 3 attacking divisions with combat widths 60, 40, and 20 under an 80-width cap.
- **Verification**: Division 1 (60 width) and Division 3 (20 width) are committed to combat (`UsedAttackerWidth = 80`, `EngagedAttackerCount = 2`). Division 2 (40 width) is skipped to reserve, taking 0 damage and retaining 55.0 organization and 1.0 strength.
- **Result**: PASSED (< 1 ms).

### 3. `CombatSystemTests.CombatSystem_MultiHopWaypointContinuation_ContinuesPastSixteenHopsUntilDestination`
- **Objective**: Sets up a 25-province linear graph and orders a division to move from province 0 to province 20 (> 16 hops).
- **Verification**: Confirms initial 16 waypoints are populated on tick 1, followed by automatic re-pathing at waypoint exhaustion to traverse the remaining hops until reaching province 20 with `State == DivisionState.Idle` and `WaypointCount == 0`.
- **Result**: PASSED (< 1 ms).

### 4. `CombatSystemTests.CombatSystem_RetreatStateConsistency_ResetsProgressAndWaypointsCleanly`
- **Objective**: Initiates combat against a low-organization defender adjacent to a friendly rear province.
- **Verification**: Asserts defender breaks and withdraws to rear province with `MovementProgress == 0.0f`, `State == DivisionState.Idle`, `TargetProvinceId == NoProvince`, `RetreatProvinceId == NoProvince`, and `WaypointCount == 0`.
- **Result**: PASSED (< 1 ms).

---

## 4. Verification Results

### Build Verification (`--warnaserror`)
```bash
dotnet build EastAsiaGSG.sln --warnaserror
```
**Output**: `Build succeeded. 0 Warning(s) 0 Error(s).`

### Full Automated Verification Script (`verify_sim.sh`)
```bash
./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
```
**Output**:
- Architectural Invariants:
  - Zero engine leakage: PASS
  - Zero LINQ allocations: PASS
  - Strictly one-way project references: PASS
- C# Build with `--warnaserror`: PASS (0 Warnings, 0 Errors)
- xUnit Test Suite: Total tests: 43. Passed: 43. Failed: 0. Total time: 1.83s.
- Overall Verdict: **ALL HEADLESS SIMULATION VERIFICATION CHECKS PASSED!**

---

## 5. Notes & Caveats

1. **Godot Headless Command**: Explorer 3 audited and executed the Godot client headless command (`/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50`), which produced exit code 0 and confirmed clean map loading and division spawning. In this worker session, the external application launch prompt for the unsandboxed binary was declined by policy; however, all Godot client C# code (`CombatTelemetryPopup.cs` and client assembly) compiles with 0 warnings in `EastAsiaGSG.sln`.
2. **Phase 3–6 Roadmap**: Systems scheduled for Phases 3 through 6 remain modularly stubbed in `SimulationWorld.StepSixHours()`, strictly in accordance with `ROADMAP.md`.
