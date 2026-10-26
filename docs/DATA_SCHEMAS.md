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

---

## 2. State Definition Schema (`/data/map/states.json`)

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
      "startingProvinceId": 102,
      "startingExperience": 0.65,
      "startingStrength": 1.0,
      "pgmAuthorized": true
    }
  ]
}
```