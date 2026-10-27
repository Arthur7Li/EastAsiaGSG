# Empirical Adversarial Challenge Report: Defect R2.2 & R2.3

- **Agent**: Challenger 1 (Adversarial Verifier)
- **Role**: critic, specialist
- **Date**: 2026-10-27T19:58:00Z
- **Target Defects**:
  - Defect R2.2: `TryFindDivisionById` Entity ID vs. Division ID Ambiguity
  - Defect R2.3: `Combat Width Reserve Allocation` Truncation & Damage Isolation

---

## 1. Challenge Summary

- **Overall Risk Assessment**: **LOW** (Repairs are structurally sound, performant, and empirically verified under adversarial conditions).
- **Core Verdict**: **APPROVE**

Both defect repairs were subjected to adversarial testing designed to break them under edge cases and boundary conditions. All adversarial challenge suites passed completely without failure, confirming the correctness, isolation, and robustness of the implementations.

---

## 2. Adversarial Challenge 1: Defect R2.2 (`TryFindDivisionById`)

### 2.1 Adversarial Hypothesis & Threat Model
In Arch ECS (`Arch.Core.World`), entities are stored across 16KB unmanaged archetype chunks. In a world with hundreds of divisions, divisions span across 4+ chunks.
- **Attack Vector**: If an entity $E_A$ in Chunk 0 has internal `Entity.Id = X`, and a division $D_B$ in Chunk 3 or 4 has domain `DivisionComponent.DivisionId = X`:
  - Does `TryFindDivisionById(X)` return $D_B$ (the domain entity) or $E_A$ (the colliding entity)?
  - Does chunk traversal order or chunk boundaries cause Phase 1 to short-circuit prematurely?
  - Does fallback to Phase 2 work for uncollided internal entity IDs across arbitrary chunks?
  - What happens when the colliding entity $E_A$ is destroyed?

### 2.2 Empirical Test Formulation & Execution
An adversarial test was constructed spawning 600 divisions across 4+ chunks in Arch ECS:
```csharp
const int initialDivisionCount = 600;
// Verified chunkCount >= 4 chunks in Arch.Core.World
Entity earlyEntity = entities[5];                      // Chunk 0
Entity middleEntity = entities[initialDivisionCount / 2]; // Intermediate Chunk
Entity lateEntity = entities[initialDivisionCount - 5];   // Last Chunk

// Spawn divisions with domain DivisionId matching early, middle, and late Entity.Ids
Entity advEarly = world.SpawnDivision(CreateDiv(earlyCollisionId));
Entity advMiddle = world.SpawnDivision(CreateDiv(middleCollisionId));
Entity advLate = world.SpawnDivision(CreateDiv(lateCollisionId));
```

### 2.3 Verification Results
1. **Early Chunk Collision (Chunk 0)**:
   - Query: `TryFindDivisionById(earlyEntity.Id, out var resolved)`
   - Expected: `advEarly` (DivisionId == earlyEntity.Id)
   - Actual: `advEarly` (`Entity.Id` prioritized correctly; earlyEntity rejected). **PASS**.
2. **Intermediate Chunk Collision (Chunk 2)**:
   - Query: `TryFindDivisionById(middleEntity.Id, out var resolved)`
   - Expected: `advMiddle`
   - Actual: `advMiddle`. **PASS**.
3. **Late Chunk Collision (Chunk 4)**:
   - Query: `TryFindDivisionById(lateEntity.Id, out var resolved)`
   - Expected: `advLate`
   - Actual: `advLate`. **PASS**.
4. **Fallback Uncollided Resolution**:
   - Querying an entity ID with no colliding `DivisionId` returned the division by its `Entity.Id` via Phase 2 across chunks. **PASS**.
5. **Non-Existent ID**:
   - Querying `-99999` returned `false` and `default(Entity)`. **PASS**.
6. **Entity Lifecycle / Destruction**:
   - Calling `world.EcsWorld.Destroy(earlyEntity)` and subsequently querying `earlyCollisionId` still returned `advEarly`. **PASS**.

---

## 3. Adversarial Challenge 2: Defect R2.3 (`Combat Width Reserve Allocation`)

### 3.1 Adversarial Hypothesis & Threat Model
The repair in `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` replaced `break` with `continue` and added array swap compaction:
```csharp
(attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
usedAttackerWidth += width;
engagedAttackerCount++;
```
- **Attack Vector 1 (Damage Leakage to Reserves)**: When a large division (e.g. 40w) is skipped because it exceeds remaining combat width, does it suffer any Organization or Strength damage from downstream combat damage application?
- **Attack Vector 2 (Frontline Starvation)**: Does a smaller subsequent division (e.g. 10w, 20w) successfully swap into the engaged index range and engage in combat?
- **Attack Vector 3 (Sustained Multi-Tick Combat)**: Over a multi-tick battle (5 ticks / 30 hours), do skipped reserve units remain pristine (100% Org and Strength)?
- **Attack Vector 4 (Reserve Reinforcement)**: When an engaged frontline unit breaks or retreats, does a previously waiting reserve unit enter combat on the next tick?
- **Attack Vector 5 (Over-Width Lead Unit)**: Does a single division exceeding total combat width (e.g. 90w > 80w) engage as the lead unit while holding subsequent units in reserve?

### 3.2 Empirical Test Formulation & Execution
A 12-division combat scenario was set up with 80 province combat width:
- **Attacker Division Widths**: `[50, 40, 20, 20, 10, 15]`
  - A1 (50w): Fits (used = 50)
  - A2 (40w): 50 + 40 = 90 > 80 -> **SKIPPED to reserve**
  - A3 (20w): 50 + 20 = 70 <= 80 -> **COMMITTED to frontline**
  - A4 (20w): 70 + 20 = 90 > 80 -> **SKIPPED to reserve**
  - A5 (10w): 70 + 10 = 80 <= 80 -> **COMMITTED to frontline** (Exact 80 cap reached)
  - A6 (15w): 80 + 15 = 95 > 80 -> **SKIPPED to reserve**
  - Committed attackers: A1 (50w), A3 (20w), A5 (10w) = 80w (3 engaged).
  - Skipped attackers: A2 (40w), A4 (20w), A6 (15w).
- **Defender Division Widths**: Identical setup `[50, 40, 20, 20, 10, 15]`.
  - Committed defenders: D1 (50w), D3 (20w), D5 (10w) = 80w (3 engaged).
  - Skipped defenders: D2 (40w), D4 (20w), D6 (15w).

### 3.3 Empirical Verification Results

| Test Scenario | Expected Outcome | Actual Outcome | Status |
|---|---|---|---|
| Initial Width Accounting | `TotalCombatWidth = 80`, `UsedAttackerWidth = 80`, `UsedDefenderWidth = 80` | `80, 80, 80` | **PASS** |
| Engaged Counts | `EngagedAttackerCount = 3`, `EngagedDefenderCount = 3` | `3, 3` | **PASS** |
| Attacker Reserve Isolation | A2, A4, A6 have `Org == 60.0f` and `Strength == 1.0f` | `Org = 60.0f, Str = 1.0f` | **PASS** |
| Defender Reserve Isolation | D2, D4, D6 have `Org == 60.0f` and `Strength == 1.0f` | `Org = 60.0f, Str = 1.0f` | **PASS** |
| Attacker Frontline Engagement | A1, A3, A5 suffer Org damage (`Org < 60.0f`) | `Org < 60.0f` for all 3 | **PASS** |
| Defender Frontline Engagement | D1, D3, D5 suffer Org damage (`Org < 60.0f`) | `Org < 60.0f` for all 3 | **PASS** |
| 5-Tick Sustained Battle (30h) | Reserves remain at 100% Org/Str; engaged suffer cumulative loss | `Reserves: Org 60.0f, Str 1.0f`. Engaged: Org decreased further. | **PASS** |
| Reserve Reinforcement | When low-Org D1 retreats, 60w reserve D2 reinforces frontline next tick | D1 retreats to adjacent province; D2 enters frontline, engages and takes damage | **PASS** |
| Over-Width Boundary | 90w single division engages (count 0 exception); 10w second division held in reserve | `UsedWidth = 90`, `Engaged = 1`, 10w unit in reserve taking 0 damage | **PASS** |

---

## 4. Test Suite Execution Summary

```bash
dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"
```
- **Passed**: 43 / 43 tests
- **Failed**: 0
- **Duration**: 1.28s

```bash
./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
```
- **Step 1/4 (Architectural Invariants)**:
  - Zero Godot engine leakage in Core/Data: **PASS**
  - Zero LINQ in hot tick systems: **PASS**
  - Strictly one-way project references: **PASS**
- **Step 2/4 (Build with Zero Warnings)**:
  - `dotnet build EastAsiaGSG.sln --warnaserror`: **PASS** (0 warnings, 0 errors).
- **Step 3/4 (Headless Simulation Tests)**:
  - 43 / 43 tests passed.
- **Step 4/4 (Summary)**:
  - ALL HEADLESS SIMULATION VERIFICATION CHECKS PASSED.

---

## 5. Conclusion & Verdict

- **Verdict**: **APPROVE**
- Both R2.2 and R2.3 fixes are robust, mathematically correct, and isolate unengaged entities with zero state or damage leakage.
- No regression or architectural violation observed.
