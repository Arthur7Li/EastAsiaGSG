---
name: verify-headless-sim
description: >-
  Verifies the headless simulation test suite, builds the .NET solution with zero
  compiler warnings, and enforces architectural invariants (zero Godot engine
  leakage in Core/Data, zero LINQ allocations in hot tick paths, and strictly
  one-way project references). Use this skill whenever building or testing changes
  in EastAsiaGSG.Core, EastAsiaGSG.Data, or tests/EastAsiaGSG.SimTests.
---

# Verify Headless Simulation (`verify-headless-sim`)

This skill validates that the headless C# simulation compiles cleanly with zero warnings, all xUnit tests pass, and strict architectural constraints are maintained.

## Quick Execution

Run the automated verification script from the repository root:

```bash
./.agents/skills/verify-headless-sim/scripts/verify_sim.sh
```

---

## Manual Verification Steps

If executing manually or troubleshooting specific failures, follow these steps:

### Step 1: Architectural Invariant Verification

1. **Zero Engine Leakage in Core and Data**:
   Ensure no references to `Godot`, `GodotSharp`, or Godot rendering/node APIs exist in `src/EastAsiaGSG.Core` or `src/EastAsiaGSG.Data`:
   ```bash
   grep -rnI --include="*.cs" --include="*.csproj" -E "\b(Godot|GodotSharp)\b" src/EastAsiaGSG.Core src/EastAsiaGSG.Data
   ```
   *Expected: Zero matches.*

2. **Zero LINQ Allocations in Hot Tick Paths**:
   Hot simulation loops (6-hour and daily ticks in `src/EastAsiaGSG.Core/Systems`) must use `Span<T>`, `ReadOnlySpan<T>`, or flat indexing—never heap-allocating LINQ queries:
   ```bash
   grep -rnI --include="*.cs" -E "using System\.Linq;|\.(Where|Select|ToList|ToArray|OrderBy|GroupBy|Count)\(" src/EastAsiaGSG.Core/Systems
   ```
   *Expected: Zero matches.*

3. **Strict One-Way Project References**:
   - `src/EastAsiaGSG.Core` must not reference `EastAsiaGSG.Data`, `EastAsiaGSG.GodotClient`, or `EastAsiaGSG.SimTests`.
   - `src/EastAsiaGSG.Data` must only reference `EastAsiaGSG.Core`.
   - `tests/EastAsiaGSG.SimTests` references `EastAsiaGSG.Core` and `EastAsiaGSG.Data`.

### Step 2: Clean Build with Zero Compiler Warnings

Build the entire solution with warnings treated as errors:

```bash
dotnet build EastAsiaGSG.sln --warnaserror
```

*Expected output: `Build succeeded. 0 Warning(s) 0 Error(s)`.*

### Step 3: Run Headless Simulation Tests

Execute all xUnit tests with detailed console verbosity:

```bash
dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"
```

*Expected output: All tests pass with 0 failures.*
