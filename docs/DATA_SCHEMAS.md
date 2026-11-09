# Data & Modding JSON & Binary Schemas (`EastAsiaGSG.Data`)

All game content lives in `/data/` as plain UTF-8 JSON files deserialized via `System.Text.Json` source generation (`MapJsonContext`), paired with ultra-fast binary `.eagsav` snapshots for saved games. Never hardcode historical country stats or focus trees in C#.

---

## 1. Focus Tree Schema (`/data/focus_trees/{TAG}_1985.json`)

```json
{
  "countryTag": "JPN",
  "era": "1985_LateColdWar",
  "focuses": [
    {
      "id": "JPN_plaza_accord_response",
      "name": "Navigate the Plaza Accord",
      "description": "The surging Yen reshapes our export economy and overseas capital power.",
      "icon": "focus_economy_yen",
      "gridX": 4,
      "gridY": 0,
      "costDays": 35,
      "prerequisites": [],
      "mutuallyExclusive": [],
      "aiBaseWeight": 100,
      "conditions": [
        { "type": "DateAfter", "target": "1985.09.01", "value": 0 }
      ],
      "effects": [
        { "type": "AddPoliticalPower", "target": "SELF", "value": 120 },
        { "type": "AddCivilianFactories", "target": "STATE_KANTO", "value": 3 }
      ]
    }
  ]
}
```

### Valid `ConditionType` Enum Values
- `DateAfter`, `DateBefore`, `AtWar`, `AtPeace`, `ControlsState`, `OwnsState`, `StabilityGreaterThan`, `WarSupportGreaterThan`, `DefconAtMost`, `HasCompletedFocus`, `HomelandEscalationGreaterThan`.

### Valid `EffectType` Enum Values
- `AddPoliticalPower`, `AddStability`, `AddWarSupport`, `ModifyConsumerGoods`, `AddCivilianFactories`, `AddMilitaryFactories`, `AddNavalDockyards`, `AddRadarSamLevel`, `AddEquipmentStockpile`, `SpawnDivisionTemplate`, `GrantTechBonus`, `CreateWargoal`, `AddHomelandEscalation`, `ShiftDefcon`, `TriggerNewsEvent`.

### Graph Validation Invariants
- **DAG Acyclicity**: Focus prerequisite graphs are verified using 3-color DFS to guarantee no circular prerequisite chains exist.
- **Coordinate Collision**: Every focus node within a tree must occupy a unique `(GridX, GridY)` coordinate pair.

---

## 2. State Definition Schema (`/data/map/east_asia_1985_states.json`)

Contains 41 states across the 15 playable nations:

```json
[
  {
    "stateId": 1,
    "name": "Gyeonggi-Seoul Metropolitan Area",
    "ownerTag": "ROK",
    "coreTags": ["ROK", "PRK"],
    "provinces": [101, 102, 103, 104, 105, 106, 107, 108],
    "population": 15200000,
    "civilianFactories": 8,
    "militaryFactories": 5,
    "navalDockyards": 2,
    "airbaseLevel": 6,
    "radarSamLevel": 3,
    "resources": {
      "oil": 0,
      "steel": 14,
      "aluminum": 6,
      "semiconductors": 12
    }
  }
]
```

---

## 3. Province Definition Schema (`/data/map/east_asia_1985_provinces.json`)

Contains **618 provinces** across all 41 states and 18 strategic regions:

```json
[
  {
    "provinceId": 6,
    "name": "Seoul Central",
    "stateId": 1,
    "terrain": "Urban",
    "ownerTag": "ROK",
    "controllerTag": "ROK",
    "mapX": 450,
    "mapY": 380,
    "strategicRegionId": 0,
    "infrastructureLevel": 7,
    "isSupplyHub": true,
    "portLevel": 0,
    "victoryPoints": 50,
    "airfieldLevel": 5,
    "fortificationLevel": 3,
    "adjacencies": [
      { "targetProvinceId": 5, "edgeFlags": 9 },
      { "targetProvinceId": 7, "edgeFlags": 1 }
    ]
  }
]
```

### `edgeFlags` Bitmask
- `1`: Standard Overland Adjacency
- `2`: River Crossing (Attacker penalty)
- `4`: Strait Crossing (Requires > 60% Naval Control & > 50% Air Superiority)
- `8`: Railway Line (Enables Trans-Siberian high-throughput supply flow)

---

## 4. Division Template & 1985 OOB Schema (`/data/oob_1985/{TAG}_oob.json`)

```json
{
  "countryTag": "ROK",
  "templates": [
    {
      "templateId": 1,
      "name": "ROKA Mechanized Infantry Division",
      "combatWidth": 20,
      "battalions": ["IFV", "IFV", "IFV", "MBT", "Artillery", "InfantryKit"],
      "supportCompanies": ["SupportVehicles", "Artillery"]
    }
  ],
  "startingDivisions": [
    {
      "name": "Capital Mechanized Infantry Division ('Tiger')",
      "templateId": 1,
      "startingProvinceId": 6,
      "startingExperience": 0.65,
      "startingStrength": 1.0,
      "pgmAuthorized": true
    }
  ]
}
```

---

## 5. Binary Save/Load Snapshot Schema (`.eagsav`)

Saved games are stored in `data/saves/*.eagsav` via `WorldSnapshotSerializer.cs`:

```
+-----------------------------------------------------------------------------+
| Header: 8-byte ASCII "EAGSAV01" + 4-byte uint Version (1)                   |
+-----------------------------------------------------------------------------+
| Compressed Payload (ZLibStream, CompressionLevel.Fastest):                  |
|   1. World Scalars & Telemetry:                                             |
|      - TotalSixHourTicks (int)                                              |
|      - Entity ID Counters (NextDivisionId, NextCombatId, NextLineId, ...)   |
|      - Monotonic Revisions (Ownership, Supply, Economy, AirNaval)           |
|   2. Flat State Arrays (blitted via MemoryMarshal.AsBytes):                 |
|      - Provinces[1500] (ProvinceData, 32 bytes each = 48,000 bytes)         |
|      - States[140] (StateData, 56 bytes each = 7,840 bytes)                 |
|      - Countries[16] (CountryState, 22,296 bytes each = 356,736 bytes)      |
|      - Regions[18] (StrategicRegionData, 128 bytes each = 2,304 bytes)      |
|   3. Dynamic Adjacency Graph (CSR):                                         |
|      - TargetCount (int) + ushort[] AdjacencyTargets                        |
|      - FlagCount (int) + byte[] AdjacencyEdgeFlags                          |
|   4. Dynamic ECS Entities (Arch.Core.World):                                |
|      - DivisionComponent count + structs (128 bytes each)                   |
|      - ActiveCombatComponent count + structs (96 bytes each)                |
|      - ProductionLineComponent count + structs (32 bytes each)              |
|      - NavalTaskForceComponent count + structs (64 bytes each)              |
|      - AirWingComponent count + structs (32 bytes each)                      |
+-----------------------------------------------------------------------------+
```

---

## 6. Localization & Map Metadata Schema (`MapMetadataRegistry.cs`)

`MapMetadataRegistry` provides authentic names and tooltips for all simulation entities without leaking into the headless simulation kernel:
- **Province Names**: `GetProvinceName(ushort id)` (e.g. `"Seoul Central"`, `"Vladivostok Port"`, `"Taipei Metro"`).
- **State Names**: `GetStateName(ushort id)` (e.g. `"Gyeonggi-Seoul"`, `"Maritime Primorsky"`, `"Kanto Plain"`).
- **Strategic Region Names**: `GetStrategicRegionName(byte id)` (e.g. `"Yellow Sea"`, `"Sea of Japan / East Sea"`).
- **Division Template Names**: `GetTemplateName(byte tag, ushort tplId)`.
- **News Event Titles & Bulletins**: `GetNewsTitle(string id)`, `GetNewsDescription(string id)`.

---

## 7. Full East Asia 1985 Theatre Datasets

### A. Theatre Topology & Coverage
- **States File**: `data/map/east_asia_1985_states.json` (41 States across 15 nations).
- **Provinces File**: `data/map/east_asia_1985_provinces.json` (618 Provinces covering all 41 states and 18 strategic regions).
- **GPU 16-Bit RG Texture**: `data/map/east_asia_1985_provinces.png` (2048x2048 lossless RGB8 texture with 100% $O(1)$ pixel coordinate mouse picking).

### B. 15 Playable Nations & OOB Roster
1. **The 7 Major Powers** (`data/oob_1985/{TAG}_oob.json` & `data/focus_trees/{TAG}_1985.json`):
   - `ROK` (Republic of Korea) — Templates 1, 2, 5, 6
   - `PRK` (Democratic People's Republic of Korea) — Templates 3, 4, 7, 8
   - `PRC` (People's Republic of China) — Templates 10..13
   - `USA` (United States Pacific Command) — Templates 14..17
   - `SOV` (Soviet Far Eastern Military District) — Templates 18..21
   - `JPN` (Japan Self-Defense Forces) — Templates 22..25
   - `TWN` (Republic of China / Taiwan) — Templates 26..29
2. **The 8 Regional Minors** (`data/oob_1985/{TAG}_oob.json` & `data/focus_trees/GENERIC_1985.json`):
   - `VNM` (Vietnam), `PHL` (Philippines), `MNG` (Mongolia), `THA` (Thailand), `LAO` (Laos), `KHM` (Cambodia), `MMR` (Myanmar), `MYS` (Malaysia) — Templates 30..43

### C. Simulation Commands (13 Commands)
- `CommandType.MoveDivision = 0`: Dispatches `CreateMoveDivision(divisionId, targetProvinceId)`.
- `CommandType.TogglePgm = 1`: Dispatches `CreateTogglePgm(divisionId, isAuthorized)`.
- `CommandType.SelectFocus = 2`: Dispatches `CreateSelectFocus(countryTag, focusId)`.
- `CommandType.AssignCivFactories = 3`: Dispatches `CreateAssignCivFactories(stateId, assignedCount)`.
- `CommandType.AssignMilFactories = 4`: Dispatches `CreateAssignMilFactories(lineId, assignedCount)`.
- `CommandType.ShiftDefcon = 5`: Dispatches `CreateShiftDefcon(newDefcon)`.
- `CommandType.QueueConstruction = 6`: Dispatches `CreateQueueConstruction(stateId, buildingType)`.
- `CommandType.CancelConstruction = 7`: Dispatches `CreateCancelConstruction(stateId)`.
- `CommandType.DeclareWar = 8`: Dispatches `CreateDeclareWar(actorTag, targetTag)`. Updates bidirectional `CountryWarMasks` and activates war economies.
- `CommandType.RecruitDivision = 9`: Dispatches `CreateRecruitDivision(countryTag, templateId, spawnProvinceId)`. Deducts manpower and equipment from national stockpiles and deploys field division.
- `CommandType.SwitchProductionEquipment = 10`: Dispatches `CreateSwitchProductionEquipment(lineId, equipmentType, generation)`. Retains 70% efficiency on gen upgrade.
- `CommandType.CreateProductionLine = 11`: Dispatches `CreateCreateProductionLine(countryTag, equipmentType, generation)`.
- `CommandType.AssignNavalMission = 12`: Dispatches `CreateAssignNavalMission(taskForceId, regionId, missionType)`.
