## 2026-10-27T19:13:37Z

You are Explorer 2: Combat & Movement Auditor.
Your working directory is /Users/arthurli/EastAsiaGSG/.agents/teamwork/explorer_survey_2
You MUST read the original request at /Users/arthurli/EastAsiaGSG/.agents/teamwork/ORIGINAL_REQUEST.md before doing anything else.
Also inspect /Users/arthurli/EastAsiaGSG/.agents/teamwork/orchestrator/PROJECT.md.

YOUR MISSION:
1. Conduct an audit of the Combat and Movement simulation systems against requirements R1 and specifications:
   - Inspect `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs`, `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs`, and `src/EastAsiaGSG.Core/Math/ProvincePathfinder.cs`.
   - Verify 6-hour HOI4 land combat math (Soft/Hard blend, Armor/Piercing gate, 10%/40% hit rolls, PGM consumption, Org/Strength damage, retreat/shatter) and zero-allocation A*.
2. Investigate Defect R2.3: Combat Width Reserve Frontier Allocation Truncation in `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs`.
   - Locate the combat width allocation loop. Analyze why `break` truncates allocation when a large division doesn't fit, preventing subsequent smaller reserve divisions from filling remaining width. Formulate the exact fix (`continue`).
3. Investigate Defect R2.4: Multi-Hop Waypoint Continuation for Long-Distance Routes in `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs`.
   - Analyze what happens when a division finishes its current 16 waypoints but hasn't reached `DestinationProvinceId`.
   - Formulate how re-pathing from current province to DestinationProvinceId should occur, and verify expanding the stackalloc path buffer in `IssueMoveOrder` from 64 to 128 elements.
4. Investigate Defect R2.5: Retreat State Consistency in `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (`RetreatOrShatterDefender`).
   - Analyze how retreating divisions transition state, reset combat/movement waypoints, and ensure consistent movement towards the friendly retreat province.
5. Inspect `tests/EastAsiaGSG.SimTests/CombatSystemTests.cs`.
   - Design regression tests for combat width reserve filling, multi-hop waypoints continuation, and retreat state consistency.

CONSTRAINTS:
- You are read-only: do NOT modify any source files.
- Produce your detailed findings report in `/Users/arthurli/EastAsiaGSG/.agents/teamwork/explorer_survey_2/report.md` and summary in `handoff.md`.
- Communicate back with send_message to the orchestrator.
