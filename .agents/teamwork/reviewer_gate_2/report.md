# Independent Code Review & Adversarial Audit Report — Reviewer 2

- **Reviewer**: Reviewer 2 (`reviewer_gate_2` — Independent Code Reviewer & Adversarial Critic)
- **Date**: 2026-10-27T19:56:00Z
- **Target Work Product**: Worker 1 Defect Repairs (R2.1–R2.5) & Regression Tests
- **Repository**: `/Users/arthurli/EastAsiaGSG`

---

## 1. Review Summary

**Verdict**: **APPROVE**

Worker 1's repairs for defects R2.1 through R2.5 across simulation core, pathfinding, combat resolution, and Godot UI presentation are complete, architecturally sound, and rigorously verified. No integrity violations (hardcoding, facade logic, fake tests, or fabricated outputs) exist. The solution compiles with zero warnings under `--warnaserror`, all 43 xUnit headless simulation tests pass cleanly in 1.18s, and the Godot presentation client executes headlessly for 50 frames with zero errors.

---

## 2. In-Depth Quality Review

### 2.1 Array Bounds and Swap Safety in `CombatResolutionSystem.cs` (R2.3)
- **File & Lines**: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs:238–268`
- **Assessment**: **PASSED (Completely Safe)**
- **Detailed Mechanics**:
  - `attackers` and `defenders` are sized at 32 entities via `stackalloc Entity[32]`.
  - In the allocation loops, `attackerTotal` and `defenderTotal` are bounded by `attackers.Length` (`<= 32`) via upfront capacity gates (`if (attackerTotal < attackers.Length)`).
  - The in-place swap compaction:
    ```csharp
    (attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
    usedAttackerWidth += width;
    engagedAttackerCount++;
    ```
    maintains the invariant `0 <= engagedAttackerCount <= i < attackerTotal <= 32` at all times.
  - When `engagedAttackerCount == i`, the tuple swap is an in-register self-swap (no-op). When `engagedAttackerCount < i`, the committed entity at `i` is swapped into the contiguous engaged prefix `[0..engagedAttackerCount]`, moving the unengaged reserve unit to index `i`.
  - All subsequent downstream loops (PGM consumption, simultaneous fire, and damage application) strictly index `[0..engagedAttackerCount - 1]` and `[0..engagedDefenderCount - 1]`.
  - Since `engagedAttackerCount >= 1` and `engagedDefenderCount >= 1` (as long as `attackerTotal > 0` and `defenderTotal > 0`, guaranteed by early exit checks at lines 219 and 225), modulo operators `i % engagedDefenderCount` and `i % engagedAttackerCount` never divide by zero.

### 2.2 Re-Pathing Termination in `DivisionMovementSystem.cs` (R2.4)
- **Files & Lines**: `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs:236–252` and `CombatResolutionSystem.cs:582–598`
- **Assessment**: **PASSED (Termination Strictly Guaranteed)**
- **Detailed Mechanics**:
  - When a division exhausts its 16 waypoints without reaching `DestinationProvinceId`:
    1. If `DestinationProvinceId == CurrentProvinceId` or `DestinationProvinceId == NoProvince`, the branch evaluates `false`, executing:
       ```csharp
       div.ClearWaypoints();
       div.State = DivisionState.Idle;
       ```
    2. If `DestinationProvinceId != CurrentProvinceId`, it invokes `IssueMoveOrder(world, ref div, div.DestinationProvinceId)`.
    3. If the destination is unreachable (disconnected graph component, blocked straits, or path > 128 hops), `world.FindPath` returns `PathResult.Failed` (`Success == false`). `IssueMoveOrder` immediately returns `false`.
    4. Upon `IssueMoveOrder` returning `false`, `div.ClearWaypoints()` and `div.State = DivisionState.Idle` are invoked.
    5. Because `div.State` becomes `DivisionState.Idle`, on all subsequent ticks `DivisionMovementSystem.Execute` skips `AdvanceMovement` for this division.
  - Therefore, zero infinite re-pathing loops can occur under any destination unreachability condition.

### 2.3 Retreat State Consistency in `CombatResolutionSystem.cs` (R2.5)
- **File & Lines**: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs:480–524` (`RetreatOrShatterDefender`)
- **Assessment**: **PASSED (Completely Clean State)**
- **Detailed Mechanics**:
  - The previous implementation contained duplicate sequential overwrites leaving `MovementProgress = 1.0f` on an idle division.
  - The repaired implementation resets:
    ```csharp
    defender.CurrentProvinceId = bestRetreatProvince;
    defender.TargetProvinceId = DivisionComponent.NoProvince;
    defender.RetreatProvinceId = DivisionComponent.NoProvince;
    defender.MovementProgress = 0.0f;
    defender.ClearWaypoints();
    defender.State = DivisionState.Idle;
    ```
  - And prior to neighbor search, `defender.ActiveCombatId = DivisionComponent.NoCombat; defender.ClearWaypoints();`.
  - If encircled (`bestRetreatProvince == NoProvince`), `defender.State = DivisionState.Shattered; defender.Strength = 0.0f;`.
  - No dirty or intermediate state leaks into subsequent ticks.

### 2.4 Division Identifier Ambiguity in `SimulationWorld.cs` (R2.2)
- **File & Lines**: `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs:123–162` (`TryFindDivisionById`)
- **Assessment**: **PASSED (Correct Two-Phase Resolution)**
- **Detailed Mechanics**:
  - Phase 1 iterates all ECS chunks matching strictly `divisions[i].DivisionId == divisionOrEntityId`.
  - Phase 2 iterates all ECS chunks matching fallback `entities[i].Id == divisionOrEntityId`.
  - This guarantees domain division IDs are never shadowed by internal ECS entity IDs, while preserving backwards compatibility for queries using raw entity IDs.

### 2.5 Combat Telemetry UI Strength Damage Multiplier (R2.1)
- **File & Lines**: `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs:266–268`
- **Assessment**: **PASSED (Accurate Percent Display)**
- **Detailed Mechanics**:
  - Removed erroneous `* 100f` multiplier. Values now accurately format recent strength percentage loss (e.g. `0.60%` instead of `60.00%`).

### 2.6 Zero GC Allocations in Hot Tick Paths
- **Assessment**: **PASSED (0 bytes allocated)**
- **Detailed Mechanics**:
  - `ProvincePathfinder`: Uses pre-allocated static thread-local arrays with generational search tokens. Confirmed zero allocations across 1,000 iterations via `FindPath_ZeroHeapAllocations_StrictEnforcement`.
  - `CombatResolutionSystem`: Uses `stackalloc` spans for local exchange calculation and tuple swap compaction. Confirmed zero allocations across 11 continuous battle ticks via `CombatSystem_72HourBattle_ZeroHeapAllocations_StrictEnforcement`.
  - `SimulationWorld.TryFindDivisionById`: Operates on ref struct enumerators and `Span<T>` chunk buffers. Zero heap allocations.

---

## 3. Adversarial Review & Stress-Testing

**Overall Risk Assessment**: **LOW**

### Challenge 1: Compaction Reordering of Reserve Units
- **Assumption Challenged**: Does swapping uncommitted divisions into `[engagedAttackerCount..attackerTotal-1]` corrupt reserve ordering or future combat iterations?
- **Attack Scenario**: 4 divisions in attack queue (Widths: 60, 40, 20, 10). Cap is 80. Div 1 (60) engages. Div 2 (40) skipped. Div 3 (20) engages and swaps with Div 2. Div 4 (10) skipped.
- **Resulting Span State**: Index 0 (60, engaged), Index 1 (20, engaged), Index 2 (40, reserve), Index 3 (10, reserve).
- **Assessment**: In subsequent ticks, `ResolveSingleCombat` rebuilds the `attackers` span fresh from the Arch ECS query. The temporary ordering in the local span has zero persistent side-effects. Frontline receives exactly 80 width.
- **Status**: PASSED.

### Challenge 2: Long-Distance Movement Across Map With Hostile Territory / Sea Disconnection
- **Assumption Challenged**: Can a long-distance move order across >16 hops crash or hang if the route becomes blocked mid-journey?
- **Attack Scenario**: Division travels from province A to distant province B. Midway through, an intermediate province is captured by enemy with no allowable transit, or the destination is an island with no strait.
- **Predicted & Verified Behavior**: On waypoint exhaustion, `IssueMoveOrder` calls `FindPath`. `FindPath` returns `Success = false`. `IssueMoveOrder` returns `false`. `AdvanceMovement` clears waypoints and transitions division to `DivisionState.Idle`. Zero hanging, zero infinite loops.
- **Status**: PASSED.

### Challenge 3: Encircled Defender with Low Org in Contested Province
- **Assumption Challenged**: If a defender's org reaches 0 while completely surrounded by enemy provinces or hostile divisions, does it shatter cleanly or attempt invalid retreat?
- **Attack Scenario**: Defender in province 1 surrounded by enemy provinces 0, 2, 3 all occupied by enemy attackers.
- **Predicted & Verified Behavior**: `HasHostileAttackersInProvince` and `IsFriendlyOrAllied` reject all neighbors. `bestRetreatProvince` remains `NoProvince`. Defender transitions to `DivisionState.Shattered` with `Strength = 0.0f` and `ActiveCombatId = NoCombat`.
- **Status**: PASSED.

---

## 4. Integrity Violation Audit

An exhaustive forensic inspection for integrity violations was conducted:
1. **Hardcoded Test Outputs**: None found. All assertions test dynamic simulation outputs generated by systems.
2. **Dummy or Facade Implementations**: None found. All 5 fixes contain real operational logic.
3. **Task Shortcuts**: None found.
4. **Fabricated Verification Logs**: Independently executed `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh`, `dotnet build --warnaserror`, `dotnet test`, and Godot headless 50-frame check. All tool outputs matched verbatim.
5. **Self-Certifying Work**: Work independently validated across test suites and code paths.

**Integrity Finding**: **NONE (ZERO INTEGRITY VIOLATIONS)**.

---

## 5. Verified Claims

| Claim | Verification Method | Result |
| :--- | :--- | :--- |
| Zero Godot engine leakage in Core / Data | `verify_sim.sh` Step 1 (grep for Godot/GodotSharp) | **PASS** |
| Zero LINQ allocations in Systems | `verify_sim.sh` Step 1 (grep for System.Linq / LINQ methods) | **PASS** |
| Strictly one-way project references | `verify_sim.sh` Step 1 (grep csproj project references) | **PASS** |
| Solution builds with 0 warnings under `--warnaserror` | `dotnet build EastAsiaGSG.sln --warnaserror` | **PASS (0W, 0E)** |
| 100% test pass rate across xUnit test suite | `dotnet test tests/EastAsiaGSG.SimTests` | **PASS (43/43)** |
| Godot presentation client headless stability | `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50` | **PASS (Exit Code 0)** |
| R2.1 UI strength loss multiplier removal | Inspection of `CombatTelemetryPopup.cs:267` | **PASS** |
| R2.2 DivisionId prioritization over Entity.Id | `SimulationWorldTests.TryFindDivisionById_PrioritizesDivisionId_WhenCollidingWithAnotherEntityId` | **PASS** |
| R2.3 Combat width reserve filling & swap compaction | `CombatSystemTests.CombatSystem_CombatWidthReserveFilling_SkipsLargeExceedingDivisionAndCommitsSmallerDivision` | **PASS** |
| R2.4 Multi-hop waypoint continuation past 16 hops | `CombatSystemTests.CombatSystem_MultiHopWaypointContinuation_ContinuesPastSixteenHopsUntilDestination` | **PASS** |
| R2.5 Retreat state consistency & progress zeroing | `CombatSystemTests.CombatSystem_RetreatStateConsistency_ResetsProgressAndWaypointsCleanly` | **PASS** |

---

## 6. Coverage Gaps & Unverified Items

- **Coverage Gaps**: None within Phase 1 & 2 scope. Phase 3–6 systems are scheduled for future milestones according to `ROADMAP.md`.
- **Unverified Items**: None. All commands and artifacts were directly and independently verified.

---

## 7. Conclusion

Reviewer 2 confirms that the changes implemented by Worker 1 meet all architectural invariants, eliminate the five identified defects, preserve zero-allocation performance, and pass all verification checks. Final verdict is **APPROVE**.
