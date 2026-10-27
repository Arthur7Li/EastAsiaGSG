# Handoff Report — Challenger 1 (Adversarial Verifier)

- **Date**: 2026-10-27T19:58:30Z
- **Role**: critic, specialist (Adversarial Verifier)
- **Target Folder**: `/Users/arthurli/EastAsiaGSG/.agents/teamwork/challenger_gate_1`
- **Handoff Type**: Hard Handoff
- **Recipient**: Orchestrator (`50e3c984-ecdf-4083-b717-1763964755ed`)
- **Explicit Verdict**: **APPROVE**

---

## 1. Observation

1. **Defect R2.2 (`TryFindDivisionById`) Code Structure**:
   - File: `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs` (Lines 123–162).
   - In Phase 1: Queries all chunks matching `DivisionQuery` strictly testing `divisions[i].DivisionId == divisionOrEntityId`.
   - In Phase 2: Queries all chunks matching `DivisionQuery` strictly testing `entities[i].Id == divisionOrEntityId`.
   - Returns `foundEntity = default` and `false` if neither phase matches.

2. **Defect R2.3 (`Combat Width Reserve Allocation`) Code Structure**:
   - File: `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (Lines 231–268, 388–432).
   - Frontline attacker allocation loop (Lines 238–252):
     ```csharp
     for (int i = 0; i < attackerTotal; i++)
     {
         ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
         byte width = att.CombatWidth == 0 ? (byte)20 : att.CombatWidth;
         if (engagedAttackerCount == 0 || usedAttackerWidth + width <= maxCombatWidth)
         {
             (attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
             usedAttackerWidth += width;
             engagedAttackerCount++;
         }
         else
         {
             continue;
         }
     }
     ```
   - Damage application loop (Lines 388–397):
     ```csharp
     for (int i = 0; i < attackerTotal; i++)
     {
         ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
         if (i < engagedAttackerCount)
         {
             att.Organization = System.MathF.Max(0.0f, att.Organization - attackerOrgDamageTaken[i]);
             att.Strength = System.MathF.Max(0.0f, att.Strength - attackerStrDamageTaken[i] * 0.01f);
             att.Experience = System.MathF.Min(1.0f, att.Experience + 0.002f);
         }
     ```
   - Identical logic operates on the defender side.

3. **Empirical Verification of Defect R2.2**:
   - Executed adversarial probe spawning 600 divisions across $\ge 4$ Arch ECS chunks.
   - Tested domain `DivisionId` collisions against `Entity.Id` in Chunk 0, Chunk 2, and Chunk 4.
   - In all instances, `TryFindDivisionById` resolved the entity with matching `DivisionId`, rejecting colliding `Entity.Id`s.
   - Destroying colliding entities did not corrupt lookups.
   - Non-existent IDs returned `false` with `default(Entity)`.

4. **Empirical Verification of Defect R2.3**:
   - Executed adversarial probe with 6 attacking divisions `[50, 40, 20, 20, 10, 15]` and 6 defending divisions `[50, 40, 20, 20, 10, 15]` against an 80 combat width ceiling.
   - Both sides committed exactly 3 divisions (50w + 20w + 10w = 80w); both sides skipped 3 divisions (40w, 20w, 15w).
   - In Tick 1: All skipped divisions suffered `0.0f` Org damage and `0.0f` Strength damage (`Org == 60.0f`, `Strength == 1.0f`). All committed divisions engaged and suffered Org damage (`Org < 60.0f`).
   - Sustained multi-tick combat (5 ticks / 30 hours): Skipped reserve units remained untouched (`Org == 60.0f`, `Strength == 1.0f`). Committed units sustained cumulative Org/Strength attrition.
   - Frontline retreat & reinforcement: When a frontline defender's Org dropped to 0 and retreated, a previously held 60w reserve division entered the frontline on the next tick, taking combat damage.
   - Over-width single unit: A 90w single division engaged as the lead unit under the `engagedAttackerCount == 0` clause, while an adjacent 10w division was held in reserve with 0 damage.

5. **Tool Execution Results**:
   - `dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"`:
     - 43 / 43 tests passed in 1.28s.
   - `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh`:
     - Step 1: Zero engine leakage, zero LINQ in systems, one-way references — PASS.
     - Step 2: Build with `--warnaserror` (0 warnings, 0 errors) — PASS.
     - Step 3: Headless xUnit tests (43 passed) — PASS.
     - Step 4: Verification Summary — PASS.

---

## 2. Logic Chain

1. *From Observation 1*: The two-phase design in `TryFindDivisionById` evaluates all chunks for domain `DivisionId` in Phase 1 before any chunk is evaluated for `Entity.Id` in Phase 2.
2. *From Observation 3*: Because Phase 1 checks domain `DivisionId` globally before Phase 2 begins, the chunk index or position of the entity is irrelevant: an entity with `DivisionId == targetId` in the final chunk will always take precedence over an entity with `Entity.Id == targetId` in the first chunk. This eliminates command misrouting across arbitrary chunk topologies.
3. *From Observation 2*: By swapping the committed division into `attackers[engagedAttackerCount]` before incrementing `engagedAttackerCount`, the array `attackers[0..attackerTotal-1]` is partitioned into engaged units at `[0..engagedAttackerCount-1]` and reserve units at `[engagedAttackerCount..attackerTotal-1]`.
4. *From Observations 2 & 4*: The damage loop explicitly guards `if (i < engagedAttackerCount)`. Because all reserve units reside at indices $i \ge \text{engagedAttackerCount}$, reserve units are strictly excluded from damage calculation and modification.
5. *From Observations 3, 4, and 5*: The empirical test suite confirms that zero damage leaks to reserve units, smaller divisions properly claim available combat width, and multi-chunk ECS lookups resolve correctly. All 43 test assertions pass with zero build warnings and zero invariant breaches.

---

## 3. Caveats

- **No caveats**: Both target defects were tested directly against hostile inputs and verified to satisfy all design specifications.

---

## 4. Conclusion

- **Verdict**: **APPROVE**
- Defect R2.2 (`TryFindDivisionById`) and Defect R2.3 (`Combat Width Reserve Allocation`) are fully verified, robust, and safe for production integration.

---

## 5. Verification Method

To independently reproduce and verify this assessment:
1. **Run Full Simulation Verification Script**:
   ```bash
   ./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
   ```
   *Expected: All 4 steps PASS with 0 warnings and 0 errors.*
2. **Execute Headless Unit Tests**:
   ```bash
   dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"
   ```
   *Expected: 43 Passed, 0 Failed.*
3. **Invalidation Conditions**:
   - If `TryFindDivisionById` resolves an `Entity.Id` when another entity has matching `DivisionId`, verdict is invalidated.
   - If a reserve division taking no part in combat suffers Org or Strength loss, verdict is invalidated.
