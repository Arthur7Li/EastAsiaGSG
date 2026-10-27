# Project: EastAsiaGSG Phase 1 & 2 Audit & Defect Repair

## Architecture
- `EastAsiaGSG.Core`: Pure simulation, Arch ECS, contiguous state arrays, 6-hour/daily/monthly tick logic.
- `EastAsiaGSG.Data`: System.Text.Json deserialization, map/state data loading, strictly references Core.
- `EastAsiaGSG.GodotClient`: Godot 4.x (.NET) presentation layer, GPU map shaders (provinces.png, map_lut.png), UI controls, one-way command dispatch.
- `tests/EastAsiaGSG.SimTests`: xUnit tests for simulation systems, pathfinding, combat balance.

## Feature Inventory
| # | Feature | Description | Milestone | Source | Status |
|---|---------|-------------|-----------|--------|--------|
| 1 | R1 Audit: Tick Pipeline & Invariants | Verify 6h/daily/monthly tick hierarchy, zero engine leakage, zero LINQ in systems | M1 | Survey | VERIFIED CLEAN |
| 2 | R1 Audit: CSR Graph & Pathfinding | Verify CSR province adjacency representation, zero-alloc A* | M1 | Survey | VERIFIED CLEAN |
| 3 | R1 Audit: 6h Combat Math | Verify soft/hard blend, armor/piercing, hit rolls, PGM, damage, retreat | M1 | Survey | VERIFIED CLEAN |
| 4 | R1 Audit: Godot Map Pipeline | Verify 16-bit RG provinces.png, 1D map_lut.png, borders, O(1) picking | M1 | Survey | VERIFIED CLEAN |
| 5 | R2.1 Defect: Combat Telemetry UI | Remove * 100f multiplier in CombatTelemetryPopup.cs:267 | M2 | Defect | REPAIRED & VERIFIED |
| 6 | R2.2 Defect: TryFindDivisionById Ambiguity | Implement two-phase matching prioritizing DivisionId over EntityId | M2 | Defect | REPAIRED & VERIFIED |
| 7 | R2.3 Defect: Combat Width Frontier Truncation | Replace break with continue + swap compaction in frontier allocation loop | M2 | Defect | REPAIRED & VERIFIED |
| 8 | R2.4 Defect: Multi-Hop Waypoint Continuation | Re-path on waypoint exhaustion towards DestinationProvinceId + stackalloc 128 | M2 | Defect | REPAIRED & VERIFIED |
| 9 | R2.5 Defect: Retreat State Consistency | Clean up state, movement progress, and waypoints in RetreatOrShatterDefender | M2 | Defect | REPAIRED & VERIFIED |
| 10 | R3.1 Automated Verification: verify_sim.sh | Execute .agents/skills/verify-headless-sim/scripts/verify_sim.sh | M3 | Verification | PASSED (4/4 Steps) |
| 11 | R3.2 Automated Verification: warnaserror build | dotnet build EastAsiaGSG.sln --warnaserror | M3 | Verification | PASSED (0 Warnings, 0 Errors) |
| 12 | R3.3 Automated Verification: SimTests | dotnet test tests/EastAsiaGSG.SimTests | M3 | Verification | PASSED (47/47 Tests Passed) |
| 13 | R3.4 Automated Verification: Godot Headless | Run Godot headless for 50 frames to confirm no script crashes | M3 | Verification | PASSED (Exit code 0) |
| 14 | Regression Tests Suite | Add targeted unit tests in SimulationWorldTests.cs & CombatSystemTests.cs | M2 | Verification | ADDED & PASSED (4 New Tests) |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Survey & Architecture Audit | Full codebase audit against docs and specs, inspect defect locations | none | DONE |
| 2 | Defect Repairs & Regression Tests | Implement fixes for R2.1-R2.5 and new regression unit tests | M1 | DONE |
| 3 | Automated Verification | Execute verify_sim.sh, build --warnaserror, SimTests, Godot headless | M2 | DONE |
| 4 | Review, Adversarial & Forensic Gate | 2 Reviewers, 2 Challengers, 1 Forensic Auditor | M3 | DONE (PASS) |
| 5 | Synthesis & Final Reporting | Deliver comprehensive evidence-based report | M4 | IN_PROGRESS |

## Code Layout
- `src/EastAsiaGSG.Core/ECS/SimulationWorld.cs`: Core ECS world, TryFindDivisionById (two-phase query), command ring buffer
- `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs`: Combat width allocation (swap compaction), 6h tick combat math, RetreatOrShatterDefender (clean state reset)
- `src/EastAsiaGSG.Core/Systems/DivisionMovementSystem.cs`: Multi-hop movement continuation, path buffer allocation (128), adjacency traversal
- `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs`: Combat telemetry display, strength loss multiplier removed
- `tests/EastAsiaGSG.SimTests/SimulationWorldTests.cs`: ECS and world regression tests (ID collision test added)
- `tests/EastAsiaGSG.SimTests/CombatSystemTests.cs`: Combat resolution, retreat, and width regression tests (3 tests added)
