# Project Plan: EastAsiaGSG Phase 1 & 2 Audit, Defect Repair, and Verification

## Objective
Audit the completed Phase 1 & 2 implementation against AGENTS.md, ARCHITECTURE.md, GAME_DESIGN_SPEC.md, DATA_SCHEMAS.md, ROADMAP.md, and verify-headless-sim skill. Reproduce and repair confirmed in-scope defects R2.1 - R2.5. Implement comprehensive regression unit tests. Execute all automated verification checks (verify_sim.sh, dotnet build with warnaserror, dotnet test SimTests, Godot headless check). Produce a complete evidence-based report.

## Milestones
1. **Milestone 1: Survey & Architecture Audit (R1)**
   - Explorer 1: Core Simulation, ECS, Invariants, and TryFindDivisionById (Defect 2)
   - Explorer 2: Combat Math, Movement Systems, Combat Width (Defect 3), Multi-Hop Waypoints (Defect 4), and Retreat State Consistency (Defect 5)
   - Explorer 3: Godot Presentation Client, Map Shader & Rendering, Command Buffer, and Combat Telemetry UI (Defect 1)

2. **Milestone 2: Defect Repairs & Regression Tests (R2)**
   - Worker: Implement minimal, robust fixes for Defects 1–5:
     1. Combat Telemetry Popup UI (`* 100f` multiplier removal)
     2. `TryFindDivisionById` two-phase ID matching prioritization
     3. Combat Width Reserve Frontier allocation (`break` -> `continue`)
     4. Multi-Hop Waypoint Continuation & path buffer expansion (64 -> 128)
     5. Retreat State Consistency in `RetreatOrShatterDefender`
   - Test Writer / Worker: Add dedicated unit regression tests in `SimulationWorldTests.cs` and `CombatSystemTests.cs`.

3. **Milestone 3: Rigorous Automated Verification (R3)**
   - Worker / Reviewer: Run `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh`
   - Worker / Reviewer: Run `dotnet build EastAsiaGSG.sln --warnaserror`
   - Worker / Reviewer: Run `dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"`
   - Worker / Reviewer: Run Godot headless check: `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50`

4. **Milestone 4: Independent Review, Challenger Testing & Forensic Audit**
   - Reviewer 1 & 2: Independent code review across all changes.
   - Challenger 1 & 2: Adversarial stress testing for movement, combat width, retreat, and ID lookup.
   - Forensic Auditor: Verify zero engine leakage, zero LINQ, no hardcoded cheating, authentic implementations.

5. **Milestone 5: Synthesis & Final Report Delivery**
   - Synthesize all findings into the required Section 11 structure.
