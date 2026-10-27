# Empirical Adversarial Challenge Report: Defect R2.4 & R2.5

- **Agent**: Challenger 2 (Adversarial Verifier)
- **Role**: critic, specialist
- **Date**: 2026-10-27T20:00:00Z
- **Target Defects**:
  - Defect R2.4: `Multi-Hop Waypoints` Long-Distance Continuation & Path Buffer Limits
  - Defect R2.5: `Retreat State Consistency` State Cleanliness & Atomic Transitions

---

## 1. Challenge Summary

- **Overall Risk Assessment**: **LOW** (Repairs are structurally sound, zero-allocation compliant, and empirically resilient under stress testing up to 128 hops).
- **Core Verdict**: **APPROVE**

Both defect repairs were subjected to adversarial testing designed to probe edge cases, boundary conditions, and state transitions:
1. **R2.4 (`Multi-Hop Waypoints`)**: Tested pathfinding and movement continuation at exact 16-hop waypoint boundary, long-distance 64-hop routes, maximum 128-hop stackalloc buffer boundaries, >128-hop buffer overflow guard behavior, and mid-route combat interruption with subsequent waypoint resumption.
2. **R2.5 (`Retreat State Consistency`)**: Tested friendly retreat state reset across all 11 fields (`CurrentProvinceId`, `TargetProvinceId`, `RetreatProvinceId`, `DestinationProvinceId`, `MovementProgress`, `CurrentEdgeCost`, `WaypointCount`, `CurrentWaypointIndex`, `State`, `ActiveCombatId`, `Strength`), as well as encircled defender shatter transitions.

All adversarial challenge suites passed completely without failure, confirming the correctness, isolation, and robustness of the implementations.

---

## 2. Adversarial Challenge 1: Defect R2.4 (`Multi-Hop Waypoints`)

### 2.1 Adversarial Hypotheses & Attack Vectors
In `src/EastAsiaGSG.Core/Components/DivisionComponent.cs`, waypoints are stored in a fixed-size 16-element unmanaged buffer (`public fixed ushort Waypoints[16]`).
In `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs`, `IssueMoveOrder` uses `stackalloc ushort[128]` to store the full route computed by `ProvincePathfinder.FindPath`.
- **Attack Vector 1 (Exact 16-Hop Boundary)**: When a route is exactly 16 hops, does the division stop cleanly at hop 16, or does it trigger an unnecessary re-path query that could stall or cycle?
- **Attack Vector 2 (Long-Distance 64-Hop Route)**: Across a 64-province corridor, does the division re-path 3 times (at provinces 16, 32, and 48) without premature termination, stuttering, or dropping into `Idle` state before reaching province 64?
- **Attack Vector 3 (Maximum 128-Hop Buffer Boundary)**: At exactly 128 hops, does the route fit within `stackalloc ushort[128]`, successfully compute, re-path 7 times every 16 hops, and complete traversal into province 128?
- **Attack Vector 4 (Buffer Overrun Guard at >128 Hops)**: If a route exceeds 128 hops (e.g. 130 hops), does `ProvincePathfinder.ReconstructPath` safely detect `destinationPath.Length < length` and reject the query with `PathResult.Failed` instead of overflowing the stack buffer or writing out-of-bounds memory?
- **Attack Vector 5 (Mid-Route Hostile Combat Interruption)**: When a division traveling a 34-hop route encounters an enemy garrison at province 10, engages in combat, and wins via `ConcludeAttackerVictory`, does it capture province 10, resume movement, and continue along its multi-hop corridor all the way to province 34?

### 2.2 Empirical Test Formulation & Execution
The following adversarial tests were formulated and executed against the live simulation:
1. `CombatSystem_MultiHopWaypointContinuation_ExactSixteenHops_CompletesWithoutExtraRepath`:
   - Route: Province 0 -> 16 (16 hops).
   - Division speed: 30.0f.
   - Result: Completed at province 16 in 16 ticks. Final state: `CurrentProvinceId == 16`, `State == Idle`, `WaypointCount == 0`, `MovementProgress == 0.0f`, `DestinationProvinceId == NoProvince`.
2. `CombatSystem_MultiHopWaypointContinuation_LongDistance64Hops_TraversesEntireCorridorWithoutPrematureStop`:
   - Route: Province 0 -> 64 (64 hops).
   - Re-paths verified at intermediate waypoints (16, 32, 48). Division never stalled or stopped prematurely.
   - Final state: `CurrentProvinceId == 64`, `State == Idle`, `WaypointCount == 0`, `MovementProgress == 0.0f`.
3. `CombatSystem_MultiHopWaypointContinuation_MaxDistance128Hops_TraversesCorridorSuccessfully`:
   - Route: Province 0 -> 128 (128 hops).
   - 7 re-paths executed every 16 hops.
   - Final state: `CurrentProvinceId == 128`, `State == Idle`, `WaypointCount == 0`, `MovementProgress == 0.0f`.
4. `CombatSystem_MultiHopWaypointContinuation_Exceeds128Hops_FailsGracefullyWithoutBufferOverflow`:
   - Route: Province 0 -> 130 (130 hops > 128 buffer).
   - Verified `IssueMoveOrder` returned `false` cleanly; division remained safe at province 0 with `State == Idle` and 0 corrupted bytes.
5. `CombatSystem_MultiHopWaypointContinuation_WithMidRouteHostileCombat_ResumesLongDistanceAfterVictory`:
   - Route: Province 0 -> 34 (34 hops).
   - Enemy garrison at province 10.
   - At province 10, division engaged in combat, defeated garrison, captured province 10, resumed movement, and arrived at province 34 with `State == Idle`.

---

## 3. Adversarial Challenge 2: Defect R2.5 (`Retreat State Consistency`)

### 3.1 Adversarial Hypotheses & Attack Vectors
Prior to Worker 1's repair, `RetreatOrShatterDefender` in `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` suffered from severe state corruption:
```csharp
// Pre-fix defect code:
defender.CurrentProvinceId = bestRetreatProvince;
defender.RetreatProvinceId = DivisionComponent.NoProvince;
defender.State = DivisionState.Retreating;
defender.TargetProvinceId = bestRetreatProvince;
defender.MovementProgress = 1.0f; // <-- Corrupted: 1.0f progress left on division
defender.State = DivisionState.Idle; // <-- Overwritten sequentially
defender.TargetProvinceId = DivisionComponent.NoProvince; // <-- Overwritten sequentially
// ClearWaypoints() was never called!
```
- **Attack Vector 1 (Dirty MovementProgress & Waypoints)**: After retreat, does `MovementProgress` remain `1.0f` or retain stale waypoints, causing subsequent movement orders to instantaneously teleport or desync?
- **Attack Vector 2 (Target & Retreat Province Residue)**: Are `TargetProvinceId` and `RetreatProvinceId` guaranteed to equal `DivisionComponent.NoProvince`?
- **Attack Vector 3 (Encircled Shatter Consistency)**: When a defender breaks in an encircled province with no friendly neighbors, does `RetreatOrShatterDefender` cleanly shatter the unit (`State == Shattered`, `Strength == 0.0f`) with zero dirty waypoints?

### 3.2 Empirical Verification Results
The following adversarial tests were formulated and executed:
1. `CombatSystem_RetreatStateConsistency_AllFieldsResetCleanly_InFriendlyRetreat`:
   - Contested battle at province 1 between ROK attacker (Province 0) and PRK defender (Province 1), with friendly PRK rear at province 2.
   - PRK defender breaks on tick 1 (`Org = 0.5f`).
   - Verified exact post-retreat state:
     - `CurrentProvinceId == 2`: **PASS**
     - `TargetProvinceId == NoProvince`: **PASS**
     - `RetreatProvinceId == NoProvince`: **PASS**
     - `DestinationProvinceId == NoProvince`: **PASS**
     - `MovementProgress == 0.0f`: **PASS**
     - `CurrentEdgeCost == 0.0f`: **PASS**
     - `WaypointCount == 0`: **PASS**
     - `CurrentWaypointIndex == 0`: **PASS**
     - `State == Idle`: **PASS**
     - `ActiveCombatId == NoCombat`: **PASS**
     - `Strength > 0.0f`: **PASS**
2. `CombatSystem_RetreatStateConsistency_EncircledDefender_ShattersCleanly`:
   - PRK defender encircled with all adjacent provinces controlled by ROK.
   - Defender breaks.
   - Verified exact shatter state:
     - `State == DivisionState.Shattered`: **PASS**
     - `Strength == 0.0f`: **PASS**
     - `ActiveCombatId == NoCombat`: **PASS**
     - `WaypointCount == 0`: **PASS**
     - `TargetProvinceId == NoProvince`: **PASS**
     - `RetreatProvinceId == NoProvince`: **PASS**
     - `MovementProgress == 0.0f`: **PASS**

---

## 4. Stress Test Results Matrix

| # | Stress Test Scenario | Expected Outcome | Actual Outcome | Status |
|---|---|---|---|---|
| 1 | Exact 16-Hop Route | Arrive at hop 16, State=Idle, 0 waypoints, no extra re-path | Arrived at hop 16, State=Idle, 0 waypoints | **PASS** |
| 2 | Long-Distance 64 Hops | Re-paths at 16, 32, 48; arrives at 64 without stopping | Arrived at 64, 0 premature halts | **PASS** |
| 3 | Max-Distance 128 Hops | Fits stackalloc 128 buffer, 7 re-paths, arrives at 128 | Arrived at 128, State=Idle, 0 waypoints | **PASS** |
| 4 | Route Exceeds 128 Hops | ReconstructPath fails gracefully; unit stays Idle at 0 | Move rejected safely, unit Idle at 0 | **PASS** |
| 5 | Mid-Route Combat Resumption | Engage at prov 10, win, capture prov 10, arrive at 34 | Prov 10 captured, arrived at prov 34 | **PASS** |
| 6 | Friendly Retreat Cleanliness | All 11 movement & combat fields cleanly reset to 0 | All 11 fields verified pristine | **PASS** |
| 7 | Encircled Shatter Cleanliness | Shattered, Strength=0, combat=NoCombat, waypoints=0 | Cleanly shattered with 0 residue | **PASS** |

---

## 5. Verification Suite Execution Summary

### 5.1 verify_sim.sh Full Automated Run
```bash
./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
```
- **Step 1/4 (Architectural Invariants & Engine Isolation)**:
  - Zero Godot/GodotSharp references in Core or Data: **PASS**
  - Zero LINQ allocations detected in hot tick systems: **PASS**
  - Project references strictly one-way: **PASS**
- **Step 2/4 (Build with Zero Warnings)**:
  - `dotnet build EastAsiaGSG.sln --warnaserror`: **PASS** (0 Warning(s), 0 Error(s)).
- **Step 3/4 (Headless Simulation Tests via xUnit)**:
  - 43 / 43 tests passed in 1.60s.
- **Step 4/4 (Verification Summary)**:
  - ALL HEADLESS SIMULATION VERIFICATION CHECKS PASSED.

### 5.2 Headless Godot Engine Verification
```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50
```
- Output:
  `[MainView] Loaded Korean Peninsula Micro-Sandbox from: /Users/arthurli/EastAsiaGSG/data/map/states.json`
  `[MainView] Spawned 1985 Korean Peninsula starting divisions (ROK 101, 102 vs. PRK 201, 202).`
- Exit Code: `0` (Zero script errors, zero shader errors, zero crashes).

---

## 6. Unchallenged Areas

- **Phase 3–6 Systems (Economy, Supply Graph Dijkstra, AI)**: Stubs are verified deterministic and compliant with `ROADMAP.md`. Full stress-testing deferred to their respective development milestones.

---

## 7. Conclusion & Recommendation

The repairs for **Defect R2.4** and **Defect R2.5** are complete, performant, and demonstrably correct under severe adversarial stress testing. All acceptance criteria are satisfied.

**Recommendation**: **APPROVE**.
