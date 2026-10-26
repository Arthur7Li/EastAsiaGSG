# Game Design & Mathematical Specification (1985–2000 East Asia)

## 1. Country Tags Roster (`byte TagId` 0..14)

### The 7 Major Powers (Custom Era I & Era II Focus Trees)
- `00` **`PRC`** — People's Republic of China (Deng Xiaoping Reform Era / PLA Modernization)
- `01` **`USA`** — United States Pacific Command (`USPACOM` — On-map bases in Okinawa, Guam, ROK, Philippines + Off-Map Homeland Escalation Pool)
- `02` **`SOV`** — Soviet Union / Russian Far East (`Far Eastern Military District` — Vladivostok/Khabarovsk + Trans-Siberian Rail Artery)
- `03` **`JPN`** — Japan (Economic Bubble Titan, Article 9 Constraints vs. JSDF Remilitarization)
- `04` **`ROK`** — South Korea (Fifth Republic Authoritarianism -> 1987 Democratization & Chaebol Boom)
- `05` **`PRK`** — North Korea (1985 Conventional Parity -> 1990s Arduous March & Nuclear Brinkmanship)
- `06` **`TWN`** — Taiwan / Republic of China (Martial Law -> Democratization, Silicon Shield & Strait Defense)

### The 8 Regional Minors (Shared 4-Branch Generic Focus Tree)
- `07` **`VNM`** (Vietnam), `08` **`PHL`** (Philippines), `09` **`MNG`** (Mongolia), `10` **`THA`** (Thailand), `11` **`LAO`** (Laos), `12` **`KHM`** (Cambodia), `13` **`MMR`** (Myanmar), `14` **`MYS`** (Malaysia).

---

## 2. The 8 Equipment Archetypes & 4 Tech Generations

All military production and division templates map strictly to 8 archetypes (`EquipmentType` 0..7) across 4 generational tiers (`Gen 0`: 1970 Reserve, `Gen 1`: 1980 Baseline, `Gen 2`: 1988 Late Cold War, `Gen 3`: 1995 Digital/Stealth):

| ID | Enum Name | Role in Templates / Simulation | Examples (Gen 1 -> Gen 3) |
| :--- | :--- | :--- | :--- |
| `0` | `InfantryKit` | Core soft attack & defense for all foot/motorized battalions | AKM/M16A1 -> AK-74/M16A2 + Milan/TOW/HJ-8 ATGM -> Javelin/3rd-Gen NVG |
| `1` | `SupportVehicles` | Motorized mobility, recon, C4I & logistics support | Ural-375/M35 -> HMMWV/BTR-80 Command -> Digital C4I Trucks |
| `2` | `IFV` | Mechanized infantry hardness, piercing & breakthrough | BMP-1/M113 -> BMP-2/M2 Bradley/Type 89 -> BMP-3/M2A2 ODS/ZBD-97 |
| `3` | `MBT` | High armor, hard attack & breakthrough spearheads | T-55/Type 59/M60 -> T-72B/M1A1/Type 88 -> T-90/M1A2/Type 90/ZTZ-99 |
| `4` | `Artillery` | High soft attack & suppression support/line battalions | D-30/M109A2 -> 2S19 Msta/M270 MLRS -> Smart Counter-Battery MLRS |
| `5` | `AttackHeli` | High hard attack & air-mobile breakthrough support | Mi-8/AH-1G -> Mi-24V Hind/AH-1W -> Ka-50/AH-64D Longbow |
| `6` | `TacticalAir` | Contests Strategic Air Regions & provides CAS combat bonus | MiG-21/J-7/F-4 -> MiG-29/Su-27/F-15C/F-16C -> F-22/Su-30MKK/F-2 |
| `7` | `PGM` | Consumable national stockpile for +35% combat strike & naval A2/AD | Early Laser/TV Bombs -> Tomahawk/Harpoon/Kh-31 -> JDAM/JSOW |

---

## 3. Economic & Production Formulas (`ProductionSystem.cs`)

### Available Civilian Factories ($C_{\text{free}}$)
$$\text{TotalFactories} = C_{\text{total}} + M_{\text{total}} + N_{\text{dockyards}}$$
$$C_{\text{consumer}} = \lceil \text{TotalFactories} \times \text{ConsumerGoodsRatio} \rceil$$
$$C_{\text{free}} = \max(0, C_{\text{total}} - C_{\text{consumer}} + C_{\text{trade\_balance}})$$
- Up to 15 $C_{\text{free}}$ can work on a single State construction project concurrently. Daily construction progress = $C_{\text{assigned}} \times 5.0 \times (1 + \text{ConstructionSpeedMod})$.

### Daily Military Factory Output ($O_i$)
For a production line $i$ with $M_i$ assigned Military Factories, current efficiency $E_i \in [0.10, 1.00]$, efficiency cap $E_{\text{cap}}$, and resource shortage penalty $R_{\text{pen}} \in [0.0, 1.0]$:
$$\text{DailyIC}_i = M_i \times 4.5 \times (1 + \text{FactoryOutputMod}) \times E_i \times (1 - R_{\text{pen}})$$
$$\text{UnitsProduced}_i = \frac{\text{DailyIC}_i}{\text{EquipmentCostIC}}$$
$$\Delta E_i = 0.001 \times \left(\frac{E_{\text{cap}}^2}{E_i}\right) \quad \text{(Clamped to } E_{\text{cap}}\text{)}$$

---

## 4. Six-Hour Land Combat Math (`CombatResolutionSystem.cs`)

### Combat Width
- Base Province Combat Width = `80` (+`20` per additional attacking province direction).
- Division templates consist of up to 6 combat battalions (each 2–3 width; typical Brigade Combat Team / Division width = `20`) and up to 4 support companies (0 width).

### Step-by-Step 6-Hour Tick Resolution
For each active division on the attacking and defending sides:
1. **Hardness Blend:** Given attacker Soft Attack ($A_s$), Hard Attack ($A_h$), and target Hardness ($H_t \in [0, 1]$):
   $$A_{\text{base}} = A_s \times (1.0 - H_t) + A_h \times H_t$$
2. **Modifiers & PGM Multiplier:**
   $$M_{\text{PGM}} = \text{PgmAuthorized and Stockpile} > 0 ? 1.35 : 1.00$$
   $$M_{\text{armor}} = (\text{TargetArmor} > \text{AttackerPiercing}) ? 0.50 : 1.00$$
   $$A_{\text{eff}} = A_{\text{base}} \times M_{\text{terrain}} \times M_{\text{supply}} \times M_{\text{air}} \times M_{\text{PGM}} \times M_{\text{armor}}$$
3. **Defense / Breakthrough Mitigation ($D_{\text{eff}}$):**
   - Defenders use `Defense`; Attackers use `Breakthrough` (boosted $+25\%$ if `PgmAuthorized`).
   - Blocked attacks hit at $10\%$; unblocked attacks hit at $40\%$:
   $$\text{ExpectedHits} = 0.10 \times \min(A_{\text{eff}}, D_{\text{eff}}) + 0.40 \times \max(0, A_{\text{eff}} - D_{\text{eff}})$$
4. **Organization & Strength Damage:**
   $$\text{OrgDamage} = \text{ExpectedHits} \times 0.18 \times (\text{AttackerArmor} > \text{TargetPiercing} ? 1.40 : 1.00)$$
   $$\text{StrengthDamage} = \text{ExpectedHits} \times 0.04$$
   - When a division's `Organization <= 0.0`, it retreats to an adjacent friendly province (or shatters if encircled).

---

## 5. Theatre Signature Mechanics

1. **Soviet Trans-Siberian Artery (`StateId 0` Entry Node):**
   - Off-map Soviet factories/reserves enter at the westernmost Irkutsk/Chita rail node. Supply and equipment flow along edges flagged with `AdjacencyEdgeFlags.Railway` (BAM and Trans-Siberian lines) to Khabarovsk and Vladivostok. If hostile forces occupy or bomb a bottleneck state's railway infrastructure to 0, Far East units lose off-map reinforcement.
2. **US PACOM Homeland Escalation:**
   - The `USA` tag tracks `HomelandEscalation` (0..100). Peacetime starts at 15. Completing Pentagon Focuses or enemy actions (e.g., DPRK crossing the DMZ, PLA blockading Taiwan, Soviet naval activity) raises Escalation, unlocking off-map Military Factories and spawning Carrier Strike Groups in the Western Pacific Entry Sea Zone.
3. **Strait & Amphibious Control Gate:**
   - Crossing a `Strait` edge (e.g., Taiwan Strait, Korea Strait, Tartary Strait) or launching a Naval Invasion requires the attacker's faction to hold **greater than 60% Naval Control** and **greater than 50% Air Superiority** in the corresponding `StrategicRegion`. Coastal states with `RadarSamLevel >= 3` and `PGM` stockpiles project an A2/AD penalty against enemy naval control.