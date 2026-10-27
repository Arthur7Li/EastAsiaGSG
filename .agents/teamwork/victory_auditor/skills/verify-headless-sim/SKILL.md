# Verify Headless Simulation (`verify-headless-sim`)
Source: /Users/arthurli/EastAsiaGSG/.agents/skills/verify-headless-sim/SKILL.md

Verifies the headless simulation test suite, builds the .NET solution with zero compiler warnings, and enforces architectural invariants (zero Godot engine leakage in Core/Data, zero LINQ allocations in hot tick paths, and strictly one-way project references).
