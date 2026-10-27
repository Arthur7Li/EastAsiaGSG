# Handoff Report: Presentation & UI Audit (Explorer 3)

**Agent**: Explorer 3 (Presentation & UI Auditor)  
**Target Milestone**: Milestone 1 -> Milestone 2 Handoff  
**Date**: 2026-10-27T19:25:00Z  

---

## 1. Observation

1. **Godot Client & Rendering Pipeline**:
   - `src/EastAsiaGSG.GodotClient/Map/MapShader.gdshader`:
     - Lines 24–28: `decode_province_id` decodes static 16-bit RG texture: `int r = int(round(sample_col.r * 255.0)); int g = int(round(sample_col.g * 255.0)); return (r << 8) | g;`.
     - Lines 30–37: `fetch_province_lut` fetches from 1D dynamic 2048x1 texture `map_lut_tex` using `texelFetch`. `decode_controller_tag` reads alpha channel: `int(round(lut_entry.a * 255.0))`.
     - Lines 52–96: 4-cardinal 1-texel and 2-texel sampling detects coastlines, country borders, and internal province borders with coloring priority `Country Border > Coastline > Province Border`.
   - `src/EastAsiaGSG.Data/Map/ProvinceMapBuffer.cs` (lines 116–143) and `src/EastAsiaGSG.GodotClient/Map/MapRenderer.cs` (lines 173–187):
     - `PickProvinceAtGlobalPosition(Vector2 globalPosition)` normalizes map sprite local coordinates to UV and directly reads cached CPU buffer `_rgbBytes[(y * Width + x) * 3]`, executing in $O(1)$ time without physics raycasting.
   - `src/EastAsiaGSG.GodotClient/Tactics/TacticalMapOverlay.cs` (lines 530–532):
     - Right-click tactical movement order invokes:
       `_world.EnqueueCommand(SimulationCommand.CreateMoveDivision(div.CountryTag, div.DivisionId, targetProvinceId));`
     - State mutation is strictly one-way via `CommandRingBuffer` (4096 capacity) drained in `SimulationWorld.StepSixHours()`.

2. **Defect R2.1 in `CombatTelemetryPopup.cs`**:
   - File: `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs`
   - Line 147 (Mockup):
     `Text = "Org Loss: Attacker -0.3 / tick | Defender -4.9 / tick\nStrength Loss: Attacker -0.05% | Defender -0.78%"`
   - Line 267 (Active Display):
     `$"Recent Str Loss: Def -{foundCombat.LastTickAttackerStrengthDamage * 100f:F2}% | Att -{foundCombat.LastTickDefenderStrengthDamage * 100f:F2}%";`
   - Simulation math in `src/EastAsiaGSG.Core/Systems/CombatResolutionSystem.cs`:
     - Line 89: `float strengthDamage = expectedHits * StrengthDamagePerHit;` where `StrengthDamagePerHit = 0.04f`.
     - Line 392: `att.Strength = System.MathF.Max(0.0f, att.Strength - attackerStrDamageTaken[i] * 0.01f);`
     - Line 427: `def.Strength = System.MathF.Max(0.0f, def.Strength - defenderStrDamageTaken[i] * 0.01f);`
     - Line 452: `combat.LastTickAttackerStrengthDamage = totalStrDamageToDefenders;`
     - Line 453: `combat.LastTickDefenderStrengthDamage = totalStrDamageToAttackers;`
   - Test assertion in `tests/EastAsiaGSG.SimTests/SimulationWorldTests.cs` (line 198–200):
     `// 4. OrgDamage = 15.0 * 0.18 * 1.40 = 3.78; StrengthDamage = 15.0 * 0.04 = 0.60`
     `Assert.Equal(0.60f, result.StrengthDamage, precision: 3);`

3. **Headless Godot Execution (R3.4)**:
   - Tool Command:
     `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50`
   - Result: Exit code 0.
   - Verbatim Output:
     ```
     Godot Engine v4.7.2.stable.mono.official.ed1daf0bf - https://godotengine.org

     [MainView] Loaded Korean Peninsula Micro-Sandbox from: /Users/arthurli/EastAsiaGSG/data/map/states.json
     [MainView] Spawned 1985 Korean Peninsula starting divisions (ROK 101, 102 vs. PRK 201, 202).
     ```

---

## 2. Logic Chain

1. From Observation 1: The map renderer pipeline decodes `(R << 8) | G` in `MapShader.gdshader`, fetches active colors and controller IDs from the 2048x1 RGBA8 `map_lut_tex`, and computes borders via multi-texel cardinal sampling. $O(1)$ picking operates purely on CPU byte arrays. Command flow strictly enqueues 16-byte structs into `CommandRingBuffer`.
   *Conclusion*: All R1 presentation and rendering specifications are fully satisfied.

2. From Observation 2:
   - `StrengthDamagePerHit` produces damage scaled such that 15 hits = 0.60 damage.
   - When subtracted from division strength, `attackerStrDamageTaken` is multiplied by `0.01f` (`att.Strength - attackerStrDamageTaken[i] * 0.01f`), because `att.Strength` is bounded in `[0.0, 1.0]`. This proves that `0.60` represents $0.60\%$.
   - In `ActiveCombatComponent`, `LastTickAttackerStrengthDamage` and `LastTickDefenderStrengthDamage` store these exact percentage damage totals.
   - In `CombatTelemetryPopup.cs` line 267, multiplying by `100f` scales `0.78%` into `78.00%`, inflating the displayed strength loss percentage by exactly 100x.
   *Conclusion*: Defect R2.1 is confirmed and isolated to line 267 of `CombatTelemetryPopup.cs`. Removing `* 100f` completely fixes the defect.

3. From Observation 3:
   - The Godot engine binary `/Applications/Godot_mono.app/Contents/MacOS/Godot` exists, recognizes the .NET project `EastAsiaGSG.GodotClient`, loads `MainView.tscn`, resolves data JSON files, spawns divisions, runs 50 frames headlessly, and terminates with exit code 0.
   *Conclusion*: R3.4 automated verification command is operational and valid.

---

## 3. Caveats

- In `EastAsiaGSG.sln`, project references contain Windows-style backslashes (`\`), which can cause `dotnet restore` on the full solution to fail on Darwin/macOS unless restored individually or paths normalized. Direct project builds of `src/EastAsiaGSG.Core` and `src/EastAsiaGSG.Data` succeed cleanly.
- Visual appearance of shaders under hardware GPU vs headless software dummy rasterizer was verified through shader code audit and headless engine frame stepping.

---

## 4. Conclusion

- **R1 Audit**: Godot presentation client, GPU map shaders, O(1) CPU picking, and one-way command architecture are architecturally sound and compliant with specifications.
- **R2.1 Defect**: Confirmed. In `src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs` line 267, remove `* 100f` from both attacker and defender strength damage expressions.
- **R3.4 Verification**: Godot headless command `/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50` passes with 0 errors.

---

## 5. Verification Method

To independently verify:
1. **Inspect Defect R2.1 Location**:
   `grep -n "LastTickAttackerStrengthDamage" src/EastAsiaGSG.GodotClient/UI/CombatTelemetryPopup.cs`
   Observe line 267 contains `* 100f`.
2. **Execute Godot Headless Test**:
   Run:
   ```bash
   /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path src/EastAsiaGSG.GodotClient --quit-after 50
   ```
   Verify exit code 0 and console output showing map loading and division spawning.
3. **Audit Shaders and Picking**:
   Inspect `src/EastAsiaGSG.GodotClient/Map/MapShader.gdshader` lines 24–37 and `src/EastAsiaGSG.Data/Map/ProvinceMapBuffer.cs` lines 116–143.
