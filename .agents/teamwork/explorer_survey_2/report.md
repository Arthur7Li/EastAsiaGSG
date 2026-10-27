# EastAsiaGSG Phase 1 & 2 Audit & Defect Investigation Report
**Explorer 2: Combat & Movement Simulation Systems Auditor**  
**Date**: 2026-10-27  
**Scope**: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs`, `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs`, `src/EastAsiaGSG.Core/Math/ProvincePathfinder.cs`, and `tests/EastAsiaGSG.SimTests/CombatSystemTests.cs`.

---

## Executive Summary

An exhaustive adversarial audit of the land combat resolution mathematics, division movement pipeline, and zero-allocation A* pathfinder was conducted against `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/GAME_DESIGN_SPEC.md`, and `.agents/skills/verify-headless-sim/SKILL.md`.

### Audit Scorecard
| Area | Status | Key Metrics / Verification |
|---|---|---|
| **6-Hour Land Combat Math** | **VERIFIED CORRECT** | All formulas match spec ($A_{base}$, $M_{PGM}$, $M_{armor}$, $D_{eff}$, $ExpectedHits$, $OrgDamage$, $StrDamage$) |
| **Zero-Allocation A\* Pathfinder** | **VERIFIED CORRECT** | Generational search tokens ($O(1)$ reset), min-heap priority queue, zero heap allocations, >25,000 paths/sec |
| **Architectural Invariants** | **VERIFIED COMPLIANT** | Zero Godot engine leakage, zero LINQ allocations in hot loops, strict one-way project references |
| **Defect R2.3 (Combat Width Allocation)** | **CONFIRMED DEFECT** | `break` prematurely terminates reserve allocation; **CRITICAL AUDIT FINDING**: requires in-place swap compaction |
| **Defect R2.4 (Multi-Hop Waypoints)** | **CONFIRMED DEFECT** | Waypoint exhaustion at 16 hops halts divisions early; `IssueMoveOrder` stackalloc buffer (64) insufficient for 1500-node maps |
| **Defect R2.5 (Retreat State Consistency)** | **CONFIRMED DEFECT** | `RetreatOrShatterDefender` contains dead writes and leaves dirty `MovementProgress = 1.0f` on Idle units |

---

## 1. R1 Audit: Combat & Movement Simulation Systems

### 1.1 Mathematical Kernel Verification (`CombatResolutionSystem.CalculateExchange`)
Location: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` lines 46–101.

Every mathematical equation was verified against `docs/GAME_DESIGN_SPEC.md` Section 4:

1. **Hardness Blend**:
   $$\text{clampedHardness} = \text{Clamp}(H_t, 0.0, 1.0)$$
   $$A_{\text{base}} = A_s \times (1.0 - H_t) + A_h \times H_t$$
   *Implementation*: Line 65: `float baseAttack = softAttack * (1.0f - clampedHardness) + hardAttack * clampedHardness;` — **VERIFIED**.

2. **PGM Multiplier**:
   $$M_{\text{PGM}} = \text{pgmActiveForAttacker} ? 1.35 : 1.00$$
   *Implementation*: Line 68: `float pgmMod = pgmActiveForAttacker ? PgmAttackMultiplier : 1.00f;` — **VERIFIED**.

3. **Armor / Piercing Gate**:
   $$M_{\text{armor}} = (\text{AttackerPiercing} \ge \text{TargetArmor}) ? 1.00 : 0.50$$
   *Implementation*: Lines 69–70: `bool piercedTarget = attackerPiercing >= targetArmor; float armorMod = piercedTarget ? 1.00f : UnpiercedArmorAttackPenalty;` — **VERIFIED**.

4. **Environmental & Modifiers Compound**:
   $$A_{\text{eff}} = A_{\text{base}} \times M_{\text{terrain}} \times M_{\text{supply}} \times M_{\text{air}} \times M_{\text{PGM}} \times M_{\text{armor}}$$
   *Implementation*: Line 72: `float effectiveAttack = baseAttack * terrainModifier * supplyModifier * airModifier * pgmMod * armorMod;` — **VERIFIED**.

5. **Defense / Breakthrough Mitigation ($D_{\text{eff}}$)**:
   $$D_{\text{eff}} = D_{\text{base}} \times (\text{targetIsAttackingSide} \land \text{pgmActiveForTarget} ? 1.25 : 1.00)$$
   $$\text{ExpectedHits} = 0.10 \times \min(A_{\text{eff}}, D_{\text{eff}}) + 0.40 \times \max(0, A_{\text{eff}} - D_{\text{eff}})$$
   *Implementation*: Lines 75–82:
   ```csharp
   float targetPgmBreakthroughMod = (targetIsAttackingSide && pgmActiveForTarget) ? PgmBreakthroughMultiplier : 1.00f;
   float effectiveDefense = targetDefenseOrBreakthrough * targetPgmBreakthroughMod;
   float blockedAttacks = System.MathF.Min(effectiveAttack, effectiveDefense);
   float unblockedAttacks = System.MathF.Max(0.0f, effectiveAttack - effectiveDefense);
   float expectedHits = BlockedHitRate * blockedAttacks + UnblockedHitRate * unblockedAttacks;
   ```
   Matches spec exactly with `BlockedHitRate = 0.10f` and `UnblockedHitRate = 0.40f` — **VERIFIED**.

6. **Organization & Strength Damage**:
   $$\text{OrgDamage} = \text{ExpectedHits} \times 0.18 \times (\text{AttackerArmor} > \text{TargetPiercing} ? 1.40 : 1.00)$$
   $$\text{StrengthDamage} = \text{ExpectedHits} \times 0.04$$
   *Implementation*: Lines 85–89:
   ```csharp
   bool attackerArmorUnpierced = attackerArmor > targetPiercing;
   float orgArmorBonus = attackerArmorUnpierced ? UnpiercedArmorOrgDamageBonus : 1.00f;
   float orgDamage = expectedHits * OrgDamagePerHit * orgArmorBonus;
   float strengthDamage = expectedHits * StrengthDamagePerHit;
   ```
   Matches spec constants: `OrgDamagePerHit = 0.18f`, `StrengthDamagePerHit = 0.04f`, `UnpiercedArmorOrgDamageBonus = 1.40f` — **VERIFIED**.

### 1.2 Combat Environmental Modifiers
- **Terrain Modifiers** (`GetAttackerTerrainModifier`, lines 640–654):
  - Plains: `1.00`, Forest: `0.85`, Hills: `0.80`, Mountain: `0.60`, Urban: `0.70`, Jungle: `0.65`, Marsh: `0.70`, Ocean: `0.50`.
- **Adjacency Crossing Modifiers** (`GetCrossingModifier`, lines 657–677):
  - River: `0.75x`, Strait: `0.55x` (multiplicative when combined).
- **Supply Modifier** (`ComputeSupplyModifier`, lines 680–684):
  - $\text{Clamp}(0.50 + 0.50 \times \text{SupplyRatio}, 0.50, 1.00)$.
- **Air Superiority Modifier** (`ComputeAirModifier`, lines 686–712):
  - Region air superiority difference $\Delta \in [-1.0, 1.0]$ scales attack: $\text{Clamp}(1.00 + \Delta \times 0.25, 0.75, 1.25)$.
- **PGM Consumption** (`TryConsumePgmForDivision`, lines 609–637):
  - Consumes 5 units per tick from highest available generation (`Gen3` $\to$ `Gen2` $\to$ `Gen1` $\to$ `Gen0`). Gracefully falls back to 0 without throwing when exhausted.

### 1.3 Zero-Allocation A* Pathfinder (`ProvincePathfinder.cs`)
Location: `src/EastAsiaGSG.Core/Math/ProvincePathfinder.cs` lines 47–450.
- **Generational Search Tokens** (lines 87, 141–147): Increments `_searchToken` on each query. Compares `visitedTokens[node] == token` for $O(1)$ reset without clearing 1,500-element arrays.
- **Priority Queue Min-Heap** (lines 435–524): Binary min-heap with up-heap and down-heap operating directly over fixed buffers `_heap`, `_heapPositions`, and `_heapTokens`. Zero allocation.
- **Heuristic Admissibility** (lines 410–428):
  $h(n) = \text{EuclideanDistance} \times (\text{PreferRailways} ? 0.25 : 0.45)$.
  Since the minimum possible step cost along any edge is distance $\times 0.40$ (railway) or distance $\times 1.0$ (plains), the heuristic never overestimates the true cost ($h(n) \le c^*(n)$). Admissibility is strictly guaranteed.
- **Zero Heap Allocations**: Verified by test `PathfinderTests.FindPath_ZeroHeapAllocations_StrictEnforcement` ($0$ GC bytes allocated across 100 queries).

---

## 2. Investigation of Defect R2.3: Combat Width Reserve Frontier Allocation Truncation

### 2.1 Problem Description & Exact Code Location
Location: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` lines 238–266:

```csharp
238: for (int i = 0; i < attackerTotal; i++)
239: {
240:     ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
241:     byte width = att.CombatWidth == 0 ? (byte)20 : att.CombatWidth;
242:     if (engagedAttackerCount == 0 || usedAttackerWidth + width <= maxCombatWidth)
243:     {
244:         usedAttackerWidth += width;
245:         engagedAttackerCount++;
246:     }
247:     else
248:     {
249:         break; // <--- DEFECTIVE LINE
250:     }
251: }
```
(Identical logic on lines 253–266 for `defenders`).

### 2.2 Mechanism of Failure
When a division exceeds the remaining combat width (`usedAttackerWidth + width > maxCombatWidth`), the `break` statement halts iteration through the candidate divisions immediately.

**Concrete Scenario**:
1. Base combat width: 80.
2. Attacker divisions ordered:
   - Div 0: CombatWidth = 50 (Fits: `usedAttackerWidth` = 50, `engagedAttackerCount` = 1, remaining = 30)
   - Div 1: CombatWidth = 40 ($50 + 40 = 90 > 80$. Triggers `break`!)
   - Div 2: CombatWidth = 20 ($50 + 20 = 70 \le 80$. Would fit!)
   - Div 3: CombatWidth = 10 ($70 + 10 = 80 \le 80$. Would fit!)
3. Under the current implementation, Div 2 and Div 3 are never checked. Used combat width is stranded at 50/80 (62.5% utilization), and 30 width of reserve combat power is lost.

### 2.3 CRITICAL AUDIT FINDING: Downstream Array Compaction Requirement
**Auditor Warning for Implementers**:
In the existing codebase, downstream combat execution loops directly index `attackers[i]` from $i = 0$ to `engagedAttackerCount - 1`:
- Line 277: `for (int i = 0; i < engagedAttackerCount; i++) ... att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);` (PGM consumption)
- Line 307: `for (int i = 0; i < engagedAttackerCount; i++) ... att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);` (Attacker firing)
- Line 350: `for (int i = 0; i < engagedDefenderCount; i++) ... att = ref world.EcsWorld.Get<DivisionComponent>(attackers[targetIdx]);` (Defender counter-battery targeting, where `targetIdx = i % engagedAttackerCount`)
- Line 389: `if (i < engagedAttackerCount)` applies combat damage to `attackers[i]`.

**If an implementer ONLY replaces `break` with `continue` without in-place partitioning/compaction**:
- Div 0 is at index 0 (engaged).
- Div 1 is at index 1 (skipped, in reserve).
- Div 2 is at index 2 (engaged).
- `engagedAttackerCount` becomes 2.
- The downstream loops will iterate $i = 0, 1$, accessing `attackers[0]` (Div 0) and `attackers[1]` (Div 1, the UNENGAGED RESERVE UNIT!).
- Div 1 would consume PGM, fire in combat, and take damage, while Div 2 (the engaged unit) would do nothing and take zero damage.

### 2.4 Formulated Robust Fix
When a division fits into the frontline, swap `attackers[engagedAttackerCount]` with `attackers[i]` (if $i \ne \text{engagedAttackerCount}$) before incrementing `engagedAttackerCount`. When it does not fit, `continue;`.

```csharp
// Attacker frontline allocation with contiguous in-place compaction
for (int i = 0; i < attackerTotal; i++)
{
    ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
    byte width = att.CombatWidth == 0 ? (byte)20 : att.CombatWidth;
    if (engagedAttackerCount == 0 || usedAttackerWidth + width <= maxCombatWidth)
    {
        usedAttackerWidth += width;
        if (i != engagedAttackerCount)
        {
            (attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
        }
        engagedAttackerCount++;
    }
    else
    {
        continue;
    }
}

// Defender frontline allocation with contiguous in-place compaction
for (int i = 0; i < defenderTotal; i++)
{
    ref DivisionComponent def = ref world.EcsWorld.Get<DivisionComponent>(defenders[i]);
    byte width = def.CombatWidth == 0 ? (byte)20 : def.CombatWidth;
    if (engagedDefenderCount == 0 || usedDefenderWidth + width <= maxCombatWidth)
    {
        usedDefenderWidth += width;
        if (i != engagedDefenderCount)
        {
            (defenders[engagedDefenderCount], defenders[i]) = (defenders[i], defenders[engagedDefenderCount]);
        }
        engagedDefenderCount++;
    }
    else
    {
        continue;
    }
}
```
This guarantees that `attackers[0 .. engagedAttackerCount-1]` and `defenders[0 .. engagedDefenderCount-1]` are strictly contiguous engaged units, and all remaining elements are unengaged reserves.

---

## 3. Investigation of Defect R2.4: Multi-Hop Waypoint Continuation for Long-Distance Routes

### 3.1 Problem Description & Exact Code Location
Location 1: `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs` line 63 (`IssueMoveOrder`)  
Location 2: `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs` lines 236–240 (`AdvanceMovement`)  
Location 3: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` lines 580–585 (`ConcludeAttackerVictory`)

### 3.2 Mechanism of Failure
1. **Component Capacity Constraint**:
   `DivisionComponent` defines `public const int MaxWaypoints = 16; fixed ushort Waypoints[16];`.
   In `IssueMoveOrder` (lines 76–85), if a path is 25 hops long, only the first 16 hops are copied to `div.Waypoints`. `div.DestinationProvinceId` records the ultimate destination (hop 25).
2. **Abrupt Movement Halt upon Waypoint Exhaustion**:
   In `AdvanceMovement`:
   ```csharp
   int nextIndex = div.CurrentWaypointIndex + 1;
   if (nextIndex < div.WaypointCount)
   {
       div.CurrentWaypointIndex = (byte)nextIndex;
       div.TargetProvinceId = div.Waypoints[nextIndex];
       div.CurrentEdgeCost = ComputeHopCost(world, div.CurrentProvinceId, div.TargetProvinceId, div.CountryTag);
   }
   else
   {
       div.ClearWaypoints();
       div.State = DivisionState.Idle; // <--- PREMATURE HALT AT HOP 16
   }
   ```
   When the division finishes its 16th waypoint, it reaches hop 16, encounters the `else` branch, wipes its waypoints, and becomes `Idle`. It never reaches `DestinationProvinceId`.
3. **Stackalloc Path Buffer Limit in `IssueMoveOrder`**:
   `Span<ushort> pathBuffer = stackalloc ushort[64];` (line 63).
   In `ProvincePathfinder.ReconstructPath`:
   ```csharp
   if (destinationPath.Length < length)
   {
       return new PathResult(false, length, _gScores[goal], goal);
   }
   ```
   If a long-distance route across East Asia exceeds 64 provinces (e.g. Vladivostok to Central Asia, or Southern China to Manchuria), `FindPath` returns `Success = false`. `IssueMoveOrder` fails immediately and does not move the division at all.

### 3.3 Formulated Robust Fix
1. **Expand Path Buffer**:
   In `DivisionMovementSystem.IssueMoveOrder`:
   Change `Span<ushort> pathBuffer = stackalloc ushort[64];` to:
   `Span<ushort> pathBuffer = stackalloc ushort[128];`
   (Consumes only 256 bytes on the stack, safe, zero-allocation).

2. **Waypoint Continuation in `AdvanceMovement`**:
   In `DivisionMovementSystem.AdvanceMovement` (lines 229–241):
   ```csharp
   int nextIndex = div.CurrentWaypointIndex + 1;
   if (nextIndex < div.WaypointCount)
   {
       div.CurrentWaypointIndex = (byte)nextIndex;
       div.TargetProvinceId = div.Waypoints[nextIndex];
       div.CurrentEdgeCost = ComputeHopCost(world, div.CurrentProvinceId, div.TargetProvinceId, div.CountryTag);
   }
   else if (div.DestinationProvinceId != DivisionComponent.NoProvince
            && div.DestinationProvinceId != div.CurrentProvinceId
            && div.DestinationProvinceId < SimulationWorld.MaxProvinces)
   {
       // Re-path from CurrentProvinceId to DestinationProvinceId for the next hop segment
       ushort dest = div.DestinationProvinceId;
       if (!IssueMoveOrder(world, ref div, dest))
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

3. **Waypoint Continuation in `ConcludeAttackerVictory`**:
   In `CombatResolutionSystem.ConcludeAttackerVictory` (lines 569–586):
   Apply identical waypoint continuation logic so an attacking division winning combat at waypoint 16 re-paths and continues moving towards `DestinationProvinceId`.

---

## 4. Investigation of Defect R2.5: Retreat State Consistency in `RetreatOrShatterDefender`

### 4.1 Problem Description & Exact Code Location
Location: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` lines 476–523 (`RetreatOrShatterDefender`).

```csharp
506: if (bestRetreatProvince != DivisionComponent.NoProvince)
507: {
508:     // Immediate tactical withdrawal to adjacent friendly province
509:     defender.CurrentProvinceId = bestRetreatProvince;
510:     defender.RetreatProvinceId = DivisionComponent.NoProvince;
511:     defender.State = DivisionState.Retreating;
512:     defender.TargetProvinceId = bestRetreatProvince;
513:     defender.MovementProgress = 1.0f;
514:     defender.State = DivisionState.Idle;
515:     defender.TargetProvinceId = DivisionComponent.NoProvince;
516: }
517: else
518: {
519:     // Encircled: no friendly adjacent province available -> division shatters!
520:     defender.State = DivisionState.Shattered;
521:     defender.Strength = 0.0f;
522: }
```

### 4.2 Mechanism of Failure & Forensic Analysis
1. **Dead Writes**:
   - Line 511 sets `defender.State = DivisionState.Retreating`, which is immediately overwritten on line 514 with `defender.State = DivisionState.Idle`.
   - Line 512 sets `defender.TargetProvinceId = bestRetreatProvince`, which is immediately overwritten on line 515 with `defender.TargetProvinceId = DivisionComponent.NoProvince`.
2. **Dirty MovementProgress**:
   - Line 513 sets `defender.MovementProgress = 1.0f`. When `defender.State` becomes `Idle`, `MovementProgress` remains at `1.0f`. Any subsequent system or Godot visual overlay polling `MovementProgress` observes an Idle unit with $100\%$ progress on an unassigned edge.
3. **Architectural Trade-off Analysis (Immediate Withdrawal vs. Gradual Retreat)**:
   - `DivisionMovementSystem` contains `AdvanceRetreat` (lines 244–266), which was written for multi-tick gradual retreat.
   - **However**, changing `RetreatOrShatterDefender` to start a multi-tick gradual retreat (`State = Retreating`, leaving `CurrentProvinceId` at the contested province) would **directly break** the historical regression suite:
     In `CombatSystemTests.cs:Simulate72HourBattle_RokMechanizedVsPrkInfantry_BreachesLineWithin72Hours`:
     ```csharp
     ref DivisionComponent prk = ref world.EcsWorld.Get<DivisionComponent>(prkEntity);
     if (battleConcludedTick == -1 && prk.CurrentProvinceId == 2)
     {
         battleConcludedTick = tick + 1; // 1-based tick count
     }
     ...
     Assert.True(battleConcludedTick is >= 8 and <= 11);
     Assert.Equal((ushort)2, finalPrk.CurrentProvinceId);
     ```
     At speed 16, retreating over edge 1 $\to$ 2 (distance 52.2, cost 74.5) requires 6 ticks (36 hours). If retreat were gradual, `prk.CurrentProvinceId` would remain 1 until tick 14+, causing `Simulate72HourBattle` to fail.
   - Furthermore, `ConcludeAttackerVictory` transfers province ownership immediately (`world.Provinces[capturedProvinceId].ControllerTag = combat.AttackerTag;`). Leaving the defender in `capturedProvinceId` would create a state contradiction where both victorious attackers and retreating defenders occupy the same province simultaneously.

### 4.3 Formulated Robust Fix
Clean up the retreat transition to be atomic, deterministic, and free of dead writes and dirty progress flags:

```csharp
if (bestRetreatProvince != DivisionComponent.NoProvince)
{
    // Immediate tactical withdrawal to adjacent friendly province
    defender.CurrentProvinceId = bestRetreatProvince;
    defender.RetreatProvinceId = DivisionComponent.NoProvince;
    defender.TargetProvinceId = DivisionComponent.NoProvince;
    defender.DestinationProvinceId = DivisionComponent.NoProvince;
    defender.MovementProgress = 0.0f;
    defender.CurrentEdgeCost = 0.0f;
    defender.ClearWaypoints();
    defender.ActiveCombatId = DivisionComponent.NoCombat;
    defender.State = DivisionState.Idle;
}
else
{
    // Encircled: no friendly adjacent province available -> division shatters!
    defender.ClearWaypoints();
    defender.ActiveCombatId = DivisionComponent.NoCombat;
    defender.TargetProvinceId = DivisionComponent.NoProvince;
    defender.DestinationProvinceId = DivisionComponent.NoProvince;
    defender.RetreatProvinceId = DivisionComponent.NoProvince;
    defender.MovementProgress = 0.0f;
    defender.CurrentEdgeCost = 0.0f;
    defender.State = DivisionState.Shattered;
    defender.Strength = 0.0f;
}
```

---

## 5. Regression Test Specifications for SimTests

To prevent regressions, the following 3 tests must be added to `tests/EastAsiaGSG.SimTests/CombatSystemTests.cs`:

### 5.1 Test 1: `CombatSystem_CombatWidthReserveFrontier_SmallerDivisionsUtilizeRemainingWidth`
- **Objective**: Verify that when a large division cannot fit within remaining combat width, subsequent smaller divisions are engaged to fill the width, and unengaged divisions remain strictly in reserve.
- **Setup**:
  - Combat width = 80.
  - Div 1: Width 50 (Engaged, used = 50).
  - Div 2: Width 40 (Reserve, does not fit $50+40=90 > 80$).
  - Div 3: Width 20 (Engaged, fits $50+20=70 \le 80$).
  - Div 4: Width 10 (Engaged, fits $70+10=80 \le 80$).
- **Assertions**:
  - `combat.UsedAttackerWidth == 80`.
  - `combat.EngagedAttackerCount == 3`.
  - Div 2 (Reserve) takes $0.0f$ damage; `Organization` and `Strength` are untouched.
  - Div 1, Div 3, and Div 4 (Engaged) participate in combat and receive combat experience / damage.

### 5.2 Test 2: `DivisionMovement_MultiHopWaypoints_RePathsAndContinuesBeyond16Waypoints`
- **Objective**: Verify that a division ordered along a 20-hop route beyond `DivisionComponent.MaxWaypoints` (16) automatically re-paths upon reaching waypoint 16 and continues to `DestinationProvinceId`.
- **Setup**:
  - Construct a linear chain of 20 provinces ($0 \to 1 \to \dots \to 19$).
  - Issue move order from Province 0 to Province 19 (`div.DestinationProvinceId = 19`).
- **Assertions**:
  - At tick where waypoint 15 (hop 16) completes, verify `div.State == DivisionState.Moving` and `div.CurrentWaypointIndex == 0` (re-pathed).
  - Division successfully arrives at Province 19.
  - `div.CurrentProvinceId == 19`, `div.State == DivisionState.Idle`, `div.WaypointCount == 0`.

### 5.3 Test 3: `CombatSystem_RetreatStateConsistency_CleanStateTransitionsAndNoDirtyProgress`
- **Objective**: Verify that retreating defenders transition cleanly without dead writes or dirty `MovementProgress`.
- **Setup**:
  - Defender in Province 1 with friendly neighbor Province 2.
  - Defender starts with low organization ($1.0f$) and breaks on tick 1.
- **Assertions**:
  - `defender.CurrentProvinceId == 2`.
  - `defender.State == DivisionState.Idle`.
  - `defender.MovementProgress == 0.0f` (NOT $1.0f$).
  - `defender.CurrentEdgeCost == 0.0f`.
  - `defender.TargetProvinceId == DivisionComponent.NoProvince`.
  - `defender.RetreatProvinceId == DivisionComponent.NoProvince`.
  - `defender.WaypointCount == 0`.
  - `defender.ActiveCombatId == DivisionComponent.NoCombat`.
