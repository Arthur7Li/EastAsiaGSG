# Data & Modding JSON Schemas (`EastAsiaGSG.Data`)

All game content lives in `/data/` as plain UTF-8 JSON files deserialized via `System.Text.Json`. Never hardcode historical country stats or focus trees in C#.

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

## 2. State Definition Schema (`/data/map/states.json` & `east_asia_1985_states.json`)

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

## 3. Division Template & 1985 OOB Schema (`/data/oob_1985/{TAG}_oob.json`)

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

## 4. Full East Asia 1985 Theatre Datasets

### A. Theatre Topology & Coverage
- **States File**: `data/map/east_asia_1985_states.json` (36 States across 15 nations).
- **Provinces File**: `data/map/east_asia_1985_provinces.json` (232 Provinces covering all 36 states and 18 strategic regions).
- **GPU 16-Bit RG Texture**: `data/map/east_asia_1985_provinces.png` (1024x1024 lossless RGB8/RG8 texture with $O(1)$ pixel coordinate mouse picking).

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

### C. Simulation Commands
- `CommandType.DeclareWar = 8`: Dispatches `CreateDeclareWar(actorTag, targetTag)`. Updates bidirectional `CountryWarMasks` and activates war economies.
- `CommandType.RecruitDivision = 9`: Dispatches `CreateRecruitDivision(countryTag, templateId, spawnProvinceId)`. Deducts manpower and equipment from national stockpiles and deploys field division.
