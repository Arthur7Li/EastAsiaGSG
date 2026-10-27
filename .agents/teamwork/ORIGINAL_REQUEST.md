# Original User Request

## 2026-10-27T19:10:23Z

# EastAsiaGSG — Principal Engineer Audit, Adversarial Review, and Verified Repair

## MISSION
Act as the principal engineer, simulation-systems specialist, C# performance engineer, game-mechanics auditor, and Godot integration reviewer for EastAsiaGSG.
Independently determine whether the current implementation (Phase 1 & 2) is correct, consistent with the project's specifications, architecturally sound, performant enough for its intended workload, and properly verified.
Optimize for demonstrable correctness: inspect the repository, reproduce meaningful defects, repair verified in-scope problems, execute tests, independently audit changes, and deliver a precise evidence-based report.

Working directory: /Users/arthurli/EastAsiaGSG
Integrity mode: development

## Requirements

### R1. Independent Adversarial Audit Across Simulation, Data/Rendering, and Architecture
Audit the completed Phase 1 & 2 implementation against:
- `AGENTS.md`
- `docs/ARCHITECTURE.md`
- `docs/GAME_DESIGN_SPEC.md`
- `docs/DATA_SCHEMAS.md`
- `docs/ROADMAP.md`
- `.agents/skills/verify-headless-sim/SKILL.md`

Verify tick scheduling (stratified 6h, daily, monthly), CSR province adjacency graph, zero-allocation A* pathfinding, 6-hour HOI4 land combat math (Soft/Hard blend, Armor/Piercing gate, 10%/40% hit rolls, PGM consumption, Org/Strength damage, retreat/shatter), Godot GPU map rendering shader pipeline (`provinces.png` 16-bit RG encoding, `map_lut.png` 1D 2048x1 dynamic texture, border detection), and command ring buffer flow.

### R2. Reproduce and Repair Confirmed In-Scope Defects
Implement minimal, robust, verified fixes:
1. **Combat Telemetry Popup UI Strength Loss (P2)**: In `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs`, `LastTickAttackerStrengthDamage` is already the percentage of strength damage taken per tick. Remove the erroneous `* 100f` multiplier that inflates strength losses by 100x.
2. **Division Identifier Ambiguity in `TryFindDivisionById` (P1)**: In `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs`, two-phase search to prioritize `divisions[i].DivisionId == divisionOrEntityId` over `entities[i].Id == divisionOrEntityId` to avoid command misrouting when an internal ECS entity ID collides with a division ID.
3. **Combat Width Reserve Frontier Allocation Truncation (P2)**: In `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs`, replace `break` with `continue` when allocating frontline divisions within `maxCombatWidth`, so smaller reserve divisions can utilize remaining combat width.
4. **Multi-Hop Waypoint Continuation for Long-Distance Routes (P2)**: In `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs`, when a division completes its current 16 waypoints but has not yet reached `DestinationProvinceId`, re-path and continue towards `DestinationProvinceId`. Expand stackalloc path buffer in `IssueMoveOrder` from 64 to 128 elements.
5. **Retreat State Consistency**: Clean up retreat transitions in `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs` (`RetreatOrShatterDefender`) ensuring state and waypoint integrity.

### R3. Rigorous Automated Verification
Execute the project verification tools:
1. `./.agents/skills/verify-headless-sim/scripts/verify_sim.sh`
2. `dotnet build EastAsiaGSG.sln --warnaserror`
3. `dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"`
4. Godot engine headless check:
   `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50`
5. Add regression unit tests in `SimulationWorldTests.cs` and `CombatSystemTests.cs` validating the repaired behaviors.

## Acceptance Criteria

### Audit & Correctness
- [ ] Zero engine leakage in Core and Data projects verified.
- [ ] Zero LINQ allocations in hot simulation tick paths verified.
- [ ] 0 build warnings with `--warnaserror` across the entire solution.
- [ ] 100% test pass rate across all headless xUnit tests.
- [ ] Regression tests added and passing for all repaired defects.
- [ ] Final evidence-based report delivered conforming strictly to Section 11 format (Verdict, Acceptance Criteria, Findings & Repairs, Verification Results, Remaining Risks, Final Change Summary).
