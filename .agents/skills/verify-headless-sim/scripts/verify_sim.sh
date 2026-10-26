#!/usr/bin/env bash
set -euo pipefail

# Find repository root
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || (cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd))"
cd "${REPO_ROOT}"

echo "=========================================================="
echo " Running EastAsiaGSG Headless Simulation Verification"
echo " Repo root: ${REPO_ROOT}"
echo "=========================================================="

FAILED=0

echo ""
echo "[Step 1/4] Checking Architectural Invariants & Engine Isolation..."

# Invariant 1: No GodotSharp or Godot references in Core or Data
GODOT_REFS=$(grep -rnI --include="*.cs" --include="*.csproj" -E "\b(Godot|GodotSharp)\b" src/EastAsiaGSG.Core src/EastAsiaGSG.Data || true)
if [ -n "${GODOT_REFS}" ]; then
    echo "❌ ERROR: Invariant Violation - Engine leakage found in Core/Data:"
    echo "${GODOT_REFS}"
    FAILED=1
else
    echo "✅ PASS: Zero engine leakage (no Godot/GodotSharp references in Core or Data)."
fi

# Invariant 2: No LINQ or heap-allocating LINQ extensions in hot tick paths (Systems)
LINQ_REFS=$(grep -rnI --include="*.cs" -E "using System\.Linq;|\.(Where|Select|ToList|ToArray|OrderBy|GroupBy|Count)\(" src/EastAsiaGSG.Core/Systems || true)
if [ -n "${LINQ_REFS}" ]; then
    echo "❌ ERROR: Invariant Violation - LINQ usage found in hot tick systems (src/EastAsiaGSG.Core/Systems):"
    echo "${LINQ_REFS}"
    FAILED=1
else
    echo "✅ PASS: Zero LINQ allocations detected in hot tick systems."
fi

# Invariant 3: Strict one-way project references
if grep -q "EastAsiaGSG.Data" src/EastAsiaGSG.Core/EastAsiaGSG.Core.csproj; then
    echo "❌ ERROR: Invariant Violation - EastAsiaGSG.Core references EastAsiaGSG.Data!"
    FAILED=1
fi
if grep -q "EastAsiaGSG.GodotClient" src/EastAsiaGSG.Core/EastAsiaGSG.Core.csproj; then
    echo "❌ ERROR: Invariant Violation - EastAsiaGSG.Core references EastAsiaGSG.GodotClient!"
    FAILED=1
fi
if grep -q "EastAsiaGSG.GodotClient" src/EastAsiaGSG.Data/EastAsiaGSG.Data.csproj; then
    echo "❌ ERROR: Invariant Violation - EastAsiaGSG.Data references EastAsiaGSG.GodotClient!"
    FAILED=1
fi
echo "✅ PASS: Project references are strictly one-way."

echo ""
echo "[Step 2/4] Verifying C# Build with Zero Warnings (--warnaserror)..."
BUILD_OUTPUT=$(dotnet build EastAsiaGSG.sln --warnaserror)
echo "${BUILD_OUTPUT}"
if echo "${BUILD_OUTPUT}" | grep -qi "warning(s)"; then
    WARNING_COUNT=$(echo "${BUILD_OUTPUT}" | grep -oE "[0-9]+ Warning\(s\)" | head -n1 || echo "")
    if [ "${WARNING_COUNT}" != "0 Warning(s)" ] && [ -n "${WARNING_COUNT}" ]; then
        echo "❌ ERROR: Build generated compiler warnings: ${WARNING_COUNT}"
        FAILED=1
    else
        echo "✅ PASS: 0 Warning(s) in build."
    fi
else
    echo "✅ PASS: Build succeeded with zero warnings."
fi

echo ""
echo "[Step 3/4] Running Headless Simulation Tests via xUnit..."
dotnet test tests/EastAsiaGSG.SimTests/EastAsiaGSG.SimTests.csproj --logger "console;verbosity=detailed"
echo "✅ PASS: All simulation tests executed successfully."

echo ""
echo "[Step 4/4] Verification Summary"
if [ ${FAILED} -ne 0 ]; then
    echo "❌ VERIFICATION FAILED: One or more checks did not pass."
    exit 1
fi

echo "=========================================================="
echo "🎉 ALL HEADLESS SIMULATION VERIFICATION CHECKS PASSED!"
echo "=========================================================="
exit 0
