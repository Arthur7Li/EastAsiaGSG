# Game Design & Mathematical Specification (1985–2000 East Asia)

## 1. Country Tags Roster (`byte TagId` 0..14)

The theatre covers **15 Playable Nations** across **41 States**, **618 Operational Provinces**, and **18 Strategic Air/Sea Regions**:

### The 7 Major Powers (Custom Era I & Era II Focus Trees)
- `00` **`PRC`** — People's Republic of China (Deng Xiaoping Reform Era / PLA Modernization)
- `01` **`USA`** — United States Pacific Command (`USPACOM` — Bases in Okinawa, Guam, ROK, Philippines + Off-Map Homeland Escalation Pool)
- `02` **`SOV`** — Soviet Union / Russian Far East (`Far Eastern Military District` — Vladivostok/Khabarovsk + Trans-Siberian Rail Artery)
- `03` **`JPN`** — Japan (Economic Bubble Titan, Article 9 Constraints vs. JSDF Remilitarization)
- `04` **`ROK`** — Republic of Korea / South Korea (Fifth Republic Authoritarianism -> 1987 Democratization & Chaebol Industrial Boom)
- `05` **`PRK`** — Democratic People's Republic of Korea (1985 Conventional Parity -> 1990s Arduous March & Nuclear Brinkmanship)
- `06` **`TWN`** — Republic of China / Taiwan (Martial Law -> Democratization, Silicon Shield & Strait Defense)

### The 8 Regional Minors (Shared 4-Branch Generic Focus Tree)
- `07` **`VNM`** (Vietnam), `08` **`PHL`** (Philippines), `09` **`MNG`** (Mongolia), `10` **`THA`** (Thailand), `11` **`LAO`** (Laos), `12` **`KHM`** (Cambodia), `13` **`MMR`** (Myanmar), `14` **`MYS`** (Malaysia).

---

## 2. The 8 Equipment Archetypes & 4 Tech Generations

All military production, reinforcement pipelines, and division templates map strictly to 8 archetypes (`EquipmentType` 0..7) across 4 generational tiers (`Gen 0`: 1970 Reserve, `Gen 1`: 1980 Baseline, `Gen 2`: 1988 Late Cold War, `Gen 3`: 1995 Digital/Stealth):

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
- Up to 15 $C_{\text{free}}$ can work on a single State construction project concurrently. Daily construction progress:
  $$\Delta P = C_{\text{assigned}} \times 5.0 \times (1 + \text{ConstructionSpeedMod})$$

### Daily Military Factory Output ($O_i$)
For a production line $i$ with $M_i$ assigned Military Factories, current efficiency $E_i \in [0.10, 1.00]$, efficiency cap $E_{\text{cap}}$, and resource shortage penalty $R_{\text{pen}} \in [0.0, 1.0]$:
$$\text{DailyIC}_i = M_i \times 4.5 \times (1 + \text{FactoryOutputMod}) \times E_i \times (1 - R_{\text{pen}})$$
$$\text{UnitsProduced}_i = \frac{\text{DailyIC}_i}{\text{EquipmentCostIC}}$$
$$\Delta E_i = 0.001 \times \left(\frac{E_{\text{cap}}^2}{E_i}\right) \quad \text{(Clamped to } E_{\text{cap}}\text{)}$$

When switching equipment archetype or upgrading generation, efficiency is partially retained:
- Upgrading Generation ($G \rightarrow G+1$): Retains $70\%$ of current efficiency.
- Changing Equipment Type: Resets efficiency to base $10\%$.

---

## 4. Six-Hour Land Combat Math (`CombatResolutionSystem.cs`)

### Combat Width & Reserve Mechanics
- **Base Combat Width**: `80` (+`20` per additional attacking province direction / flanking pincer).
- **Template Composition**: Up to 6 combat battalions (each 2–3 width; typical division = `20` width) and up to 4 support companies (0 width).
- **Reserve Commitment**: When frontline divisions retreat, reserve divisions roll every 6 hours to fill vacant combat width until capped.

### Step-by-Step 6-Hour Tick Resolution
For each active division on the attacking and defending sides:
1. **Hardness Blend:** Given attacker Soft Attack ($A_s$), Hard Attack ($A_h$), and target Hardness ($H_t \in [0, 1]$):
   $$A_{\text{base}} = A_s \times (1.0 - H_t) + A_h \times H_t$$
2. **Combined Arms Synergy:**
   $$\text{HasCombinedArms} = (\text{HasInfantry} \land \text{HasArmor} \land \text{HasArtilleryOrAir})$$
   $$M_{\text{synergy}} = \text{HasCombinedArms} ? 1.15 : 1.00$$
3. **Combat Doctrines & Tactical Maneuver Rolls:**
   Every 24 hours, active combats roll a tactical maneuver counter-play match based on each nation's assigned Cold War doctrine:
   - **`AirLand Battle`** (US, ROK, TWN): $+15\%$ Breakthrough when attacking; $+10\%$ CAS coordination bonus.
   - **`Deep Operations`** (SOV): $+20\%$ Soft Attack on armored breakthrough corridors; $+15\%$ reserve reinforcement rate.
   - **`Mass Assault`** (PRC, PRK, VNM): $+20\%$ Defense; $-20\%$ supply consumption penalty in low-supply provinces.
   - **`Elastic Defense`** (JPN, Regional Minors): $+15\%$ Defense; $+10\%$ counter-attack organization damage.
4. **Precision-Guided Munitions (PGM) Multiplier:**
   $$M_{\text{PGM}} = (\text{PgmAuthorized} \land \text{Stockpile} > 0) ? 1.35 : 1.00$$
   *(Deducts 1 PGM unit per 6-hour tick per active division from national stockpile).*
5. **Armor & Piercing Threshold Gate:**
   $$M_{\text{armor}} = (\text{TargetArmor} > \text{AttackerPiercing}) ? 0.50 : 1.00$$
   $$A_{\text{eff}} = A_{\text{base}} \times M_{\text{terrain}} \times M_{\text{supply}} \times M_{\text{air}} \times M_{\text{synergy}} \times M_{\text{doctrine}} \times M_{\text{PGM}} \times M_{\text{armor}}$$
6. **Defense / Breakthrough Mitigation ($D_{\text{eff}}$):**
   - Defenders use `Defense`; Attackers use `Breakthrough` (boosted $+25\%$ if `PgmAuthorized`).
   - Blocked attacks hit at $10\%$; unblocked attacks hit at $40\%$:
   $$\text{ExpectedHits} = 0.10 \times \min(A_{\text{eff}}, D_{\text{eff}}) + 0.40 \times \max(0, A_{\text{eff}} - D_{\text{eff}})$$
7. **Organization & Strength Damage:**
   $$\text{OrgDamage} = \text{ExpectedHits} \times 0.18 \times (\text{AttackerArmor} > \text{TargetPiercing} ? 1.40 : 1.00)$$
   $$\text{StrengthDamage} = \text{ExpectedHits} \times 0.04$$
   - When a division's `Organization <= 0.0`, it retreats to an adjacent friendly province (or shatters if encircled).

---

## 5. Victory Points, Stability & Capitulation (`CapitulationSystem.cs`)

Every midnight (`00:00`), `CapitulationSystem` evaluates the strategic integrity of all 15 nations:
1. **Provincial Victory Points:** Each province holds $0\text{--}50$ Victory Points (Capitals = 50, Major Hubs = 20–30, Industrial Centers = 10, Rural = 1–5).
2. **Capitulation Criteria:**
   A nation capitulates if and only if:
   - Its designated capital province is currently occupied by a hostile power.
   - Its ratio of held Victory Points to total core Victory Points drops below its surrender threshold:
     $$\text{HeldVpRatio} < \max(0.20, \text{Stability} \times 0.60)$$
3. **Surrender Execution:**
   - **Territorial Transfer:** All core and occupied provinces transfer controller status to the conquering nation.
   - **Stockpile Seizure:** The conqueror captures $50\%$ of the capitulating nation's total equipment stockpiles across all 8 archetypes and 4 generations.
   - **Combat Resolution:** All active combats in capitulating provinces are dissolved.
   - **News Bulletin:** A global news event is published (`TriggerNewsEvent`) notifying all nations of the capitulation.

---

## 6. Discrete Pacific Air/Naval Task Forces & Convoy Interdiction

### Task Force Types & Missions (`NavalTaskForceComponent.cs`)
- **`CarrierStrikeGroup` (CSG)**: Deploys up to 90 aircraft, projecting massive Air Superiority and Naval Control across maritime sea zones.
- **`SurfaceActionGroup` (SAG)**: Patrols and defends surface sea lanes, screening convoys and coastal facilities.
- **`SubmarineWolfpack` (SSN/SSK)**: Conducts covert sea-lane interdiction.
- **`AmphibiousReadyGroup` (ARG)**: Transports landing forces to execute naval invasions.

### Submarine Convoy Interdiction & Port Supply Choke
When a `SubmarineWolfpack` is set to `ConvoyInterdiction` in a maritime Strategic Region:
- It rolls daily against enemy escort efficiency (`ConvoyEscort` missions).
- Successful interdiction reduces overseas port supply throughput in adjacent coastal states by up to $60\%$, starving island bases (e.g., Okinawa, Taiwan, Sakhalin) of overland supply.

### Discrete Air Wings (`AirWingComponent.cs`)
Air wings operate from state airbases up to maximum airfield capacity, executing 4 tactical roles:
- `FighterAirDefense`: Contests air superiority, intercepting hostile strikes.
- `CasGroundSupport`: Delivers CAS damage bonus to friendly divisions in land combat within the region.
- `NavalStrike`: Attacks hostile naval task forces and coastal installations.
- `StrategicInterdiction`: Damages enemy infrastructure, airbases, and supply hubs.

---

## 7. Three-Tier Operational & Utility AI

### Tier 1: Tactical Frontline AI (`TacticalFrontlineAISystem.cs`)
- **Frontline Evaluation**: Computes daily border threat/desirability gradients.
- **Multi-Province Coordinated Pincer Attacks**: Coordinates attacks from multiple adjacent provinces against a single enemy province, widening combat width by $+20$ per flanking direction.
- **Entrenchment Hysteresis**: Units with Entrenchment $> 5.0$ hold defensive redoubts and do not abandon fortresses unless combat odds heavily favor an offensive breakthrough ($> 2.5\times$).

### Tier 2: Operational Plan AI (`OperationalPlanSystem.cs`)
- **Schwerpunkt Spearhead Concentration**: Assigns armored and mechanized divisions (`MBT`, `IFV`) to offensive spearheads targeting hostile capitals, supply hubs, and port bottlenecks (`ObjectiveProvinceId`).
- **Defensive Screen Assignment**: Assigns infantry and motorized formations to hold frontier provinces and secure flanks.

### Tier 3: Strategic Minister AI (`StrategicMinisterAISystem.cs`)
- **National Focus Selection**: Evaluates monthly geopolitical utility scores based on DEFCON, war status, and national objectives.
- **Industrial Allocation**: Adjusts factory allocation between civilian construction projects and military production lines.
- **Autonomous Recruitment (`CommandType.RecruitDivision`)**: Evaluates stockpile reserves; when manpower and equipment are sufficient, automatically queues division recruitment in safe interior provinces.
- **Autonomous War Goals (`CommandType.DeclareWar`)**: Evaluates wargoals; when military readiness exceeds $85\%$ and border superiority is achieved, executes formal war declarations.

---

## 8. Theatre Signature Mechanics

1. **Soviet Trans-Siberian Artery (`StateId 0` Entry Node):**
   - Westernmost rail entry node (Irkutsk/Chita) feeds supply along `AdjacencyEdgeFlags.Railway` edges across Siberia to Khabarovsk and Vladivostok. Hostile interdiction of Siberian rail nodes chokes off-map Soviet reinforcements.
2. **US PACOM Homeland Escalation:**
   - Tracks `HomelandEscalation` (15..100). Peacetime baseline is 15. Hostile actions (DPRK crossing the DMZ, PLA blockading Taiwan, Soviet naval strikes) raise Escalation, transitioning DEFCON from 5 to 1, unlocking off-map military factories (up to 200), and deploying Carrier Strike Groups.
3. **Strait & Amphibious Control Gate:**
   - Crossing a `Strait` edge (Taiwan Strait, Korea Strait, Tartary Strait) requires **$> 60\%$ Naval Control** and **$> 50\%$ Air Superiority** in the strategic sea region. Coastal states with `RadarSamLevel >= 3` and PGM stockpiles project A2/AD suppression penalties.

---

## 9. Deterministic Simulation Commands (13 Commands)

All simulation state mutations are driven by deterministic, unmanaged `SimulationCommand` structs (16 bytes) processed from `CommandRingBuffer`:

| ID | Command Name | Arguments | Description |
| :---: | :--- | :--- | :--- |
| `0` | `MoveDivision` | DivisionId, TargetProvinceId | Issues division pathfinding and movement order. |
| `1` | `TogglePgm` | DivisionId, Value | Toggles PGM authorization for an active division. |
| `2` | `SelectFocus` | CountryTag, FocusId | Starts execution of a national focus node. |
| `3` | `AssignCivFactories` | StateId, Value | Adjusts civilian factories assigned to a state construction project. |
| `4` | `AssignMilFactories` | LineId, Value | Adjusts military factories assigned to an equipment line. |
| `5` | `ShiftDefcon` | NewDefconLevel | Shifts theatre DEFCON alert posture. |
| `6` | `QueueConstruction` | StateId, BuildingType | Enqueues a new construction project in a state. |
| `7` | `CancelConstruction` | StateId | Cancels active state construction. |
| `8` | `DeclareWar` | ActorTag, TargetTag | Sets bilateral war bitmask, initiates combat state, and activates war economies. |
| `9` | `RecruitDivision` | CountryTag, TemplateId, ProvinceId | Spawns a new field division, deducting stockpile materiel and manpower. |
| `10` | `SwitchProductionEquipment` | LineId, EquipmentType, Gen | Changes production line equipment, retaining 70% efficiency on gen upgrade. |
| `11` | `CreateProductionLine` | CountryTag, EquipmentType, Gen | Spawns a new dynamic ECS equipment production line. |
| `12` | `AssignNavalMission` | TaskForceId, RegionId, MissionType | Assigns a naval task force to a strategic sea region and operational mission. |
