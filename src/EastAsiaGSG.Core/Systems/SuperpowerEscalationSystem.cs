using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 2 Daily (00:00) system that evaluates theater security threats, updates US PACOM HomelandEscalation
/// (15..100), shifts DEFCON alert levels (5..1), scales CONUS off-map military factories (10..200),
/// ships daily homeland stockpile convoys to the theater, and surges Carrier Strike Groups (CSGs).
/// </summary>
public static class SuperpowerEscalationSystem
{
    public const byte BaselineEscalation = 15;
    public const byte MaxEscalation = 100;
    public const byte MinEscalation = 0;

    public const float PeacetimeDecayRate = 0.25f;

    // Threat escalation weights
    public const float ThreatSeoulProvince = 5.0f;
    public const float ThreatRokProvince = 1.5f;
    public const float ThreatTaiwanProvince = 4.0f;
    public const float ThreatUsBase = 10.0f;
    public const float ThreatCombat = 0.25f;

    // DEFCON Thresholds
    public const byte Defcon1Threshold = 90;
    public const byte Defcon2Threshold = 70;
    public const byte Defcon3Threshold = 45;
    public const byte Defcon4Threshold = 25;

    // Off-Map Military Factory scaling bounds
    public const ushort PeacetimeOffMapFactories = 10;
    public const ushort MaxOffMapFactories = 200;

    // CSG Escalation thresholds
    public const byte Csg1Threshold = 30;
    public const byte Csg2Threshold = 60;
    public const byte Csg3Threshold = 85;

    public const float CsgNavalPower = 150.0f;
    public const float CsgAirPower = 80.0f;

    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetDefconForEscalation(byte escalation)
    {
        if (escalation >= Defcon1Threshold) return 1;
        if (escalation >= Defcon2Threshold) return 2;
        if (escalation >= Defcon3Threshold) return 3;
        if (escalation >= Defcon4Threshold) return 4;
        return 5;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetMinEscalationForDefcon(byte defcon)
    {
        return defcon switch
        {
            1 => Defcon1Threshold,
            2 => Defcon2Threshold,
            3 => Defcon3Threshold,
            4 => Defcon4Threshold,
            _ => BaselineEscalation
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort CalculateOffMapMilitaryFactories(byte escalation)
    {
        if (escalation <= BaselineEscalation)
        {
            return PeacetimeOffMapFactories;
        }

        float factor = (float)(escalation - BaselineEscalation) / (MaxEscalation - BaselineEscalation);
        factor = System.Math.Clamp(factor, 0.0f, 1.0f);
        return (ushort)System.MathF.Round(PeacetimeOffMapFactories + ((MaxOffMapFactories - PeacetimeOffMapFactories) * factor));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetActiveCsgCount(byte escalation)
    {
        if (escalation >= Csg3Threshold) return 3;
        if (escalation >= Csg2Threshold) return 2;
        if (escalation >= Csg1Threshold) return 1;
        return 0;
    }

    /// <summary>
    /// Executes daily theater threat assessment and escalation updates across SimulationWorld.
    /// Operates with zero heap allocations.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        float threatPoints = 0.0f;

        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan();

        // 1. Scan territory for hostile incursions
        for (int p = 0; p < provinces.Length; p++)
        {
            ref readonly ProvinceData prov = ref provinces[p];
            byte owner = prov.OwnerTag;
            byte controller = prov.ControllerTag;

            if (controller == CountryTag.None)
            {
                continue;
            }

            // Incursion into ROK territory
            if (owner == CountryTag.ROK && !DivisionMovementSystem.IsFriendlyOrAllied(world, CountryTag.ROK, controller))
            {
                // Seoul Metropolitan Area (State 0) incursions produce higher escalation
                threatPoints += prov.StateId == 0 ? ThreatSeoulProvince : ThreatRokProvince;
            }
            // Incursion into Taiwan territory
            else if (owner == CountryTag.TWN && !DivisionMovementSystem.IsFriendlyOrAllied(world, CountryTag.TWN, controller))
            {
                threatPoints += ThreatTaiwanProvince;
            }
            // Direct attack/occupation of US forward bases
            else if (owner == CountryTag.USA && !DivisionMovementSystem.IsFriendlyOrAllied(world, CountryTag.USA, controller))
            {
                threatPoints += ThreatUsBase;
            }
        }

        // 2. Scan active combats involving Bluefor forces
        Query query = world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            int chunkSize = chunk.Count;
            for (int i = 0; i < chunkSize; i++)
            {
                ref readonly ActiveCombatComponent combat = ref combats[i];
                if (combat.Outcome == CombatOutcome.Ongoing)
                {
                    bool attackerIsBlue = combat.AttackerTag < SimulationWorld.MaxCountries
                        && world.Countries[combat.AttackerTag].Faction == FactionId.BlueforPacom;
                    bool defenderIsBlue = combat.DefenderTag < SimulationWorld.MaxCountries
                        && world.Countries[combat.DefenderTag].Faction == FactionId.BlueforPacom;

                    if (attackerIsBlue || defenderIsBlue)
                    {
                        threatPoints += ThreatCombat;
                    }
                }
            }
        }

        // 3. Update US PACOM Homeland Escalation
        ref CountryState usa = ref world.Countries[CountryTag.USA];
        float currentEsc = world.CountryHomelandEscalation[CountryTag.USA];

        // If external caller directly set HomelandEscalation (e.g., test or focus tree effect), synchronize
        if ((byte)System.Math.Clamp((int)System.MathF.Round(currentEsc), MinEscalation, MaxEscalation) != usa.HomelandEscalation)
        {
            currentEsc = usa.HomelandEscalation;
        }

        if (threatPoints > 0.0f)
        {
            currentEsc = System.MathF.Min(MaxEscalation, currentEsc + threatPoints);
        }
        else
        {
            // Peacetime decay towards baseline
            if (currentEsc > BaselineEscalation)
            {
                currentEsc = System.MathF.Max(BaselineEscalation, currentEsc - PeacetimeDecayRate);
            }
        }

        // If manual ShiftDefcon command set a higher alert than current escalation, synchronize escalation floor
        byte manualDefcon = usa.Defcon;
        if (manualDefcon is >= 1 and <= 5)
        {
            byte minEscForDefcon = GetMinEscalationForDefcon(manualDefcon);
            if (minEscForDefcon > currentEsc)
            {
                currentEsc = minEscForDefcon;
            }
        }

        world.CountryHomelandEscalation[CountryTag.USA] = currentEsc;
        byte finalEsc = (byte)System.Math.Clamp((int)System.MathF.Round(currentEsc), MinEscalation, MaxEscalation);
        usa.HomelandEscalation = finalEsc;

        // 4. Update DEFCON
        byte finalDefcon = GetDefconForEscalation(finalEsc);
        usa.Defcon = finalDefcon;

        // 5. Scale CONUS Off-Map Military Factories
        usa.OffMapMilitaryFactories = CalculateOffMapMilitaryFactories(finalEsc);

        // 6. Macro-economic & War Mobilization modifiers
        float escalationFactor = (float)(finalEsc - BaselineEscalation) / (MaxEscalation - BaselineEscalation);
        escalationFactor = System.Math.Clamp(escalationFactor, 0.0f, 1.0f);

        usa.ConsumerGoodsRatio = System.Math.Clamp(0.35f - (0.20f * escalationFactor), 0.15f, 0.35f);
        usa.WarSupport = System.Math.Clamp(0.50f + (0.45f * escalationFactor), 0.50f, 0.95f);
        usa.FactoryOutputModifier = System.Math.Clamp(0.25f * escalationFactor, 0.00f, 0.25f);

        // 7. Homeland Equipment Convoys (Daily materiel arrival at DEFCON 3 or lower)
        if (finalDefcon <= 3)
        {
            ShipHomelandStockpileConvoys(ref usa);
        }

        // 8. Synchronize coalition partner alert posture
        if (finalDefcon < 5)
        {
            for (byte c = 0; c < SimulationWorld.MaxCountries; c++)
            {
                if (c != CountryTag.USA && world.Countries[c].Faction == FactionId.BlueforPacom)
                {
                    ref CountryState ally = ref world.Countries[c];
                    if (ally.Defcon > finalDefcon)
                    {
                        ally.Defcon = finalDefcon;
                    }
                }
            }
        }
    }

    private static void ShipHomelandStockpileConvoys(ref CountryState usa)
    {
        TechGeneration gen = usa.Defcon == 1
            ? TechGeneration.Gen3_1995DigitalStealth
            : TechGeneration.Gen2_1988LateColdWar;

        if (usa.Defcon == 3)
        {
            usa.AddStockpile(EquipmentType.TacticalAir, gen, 2);
            usa.AddStockpile(EquipmentType.MBT, gen, 5);
            usa.AddStockpile(EquipmentType.IFV, gen, 10);
            usa.AddStockpile(EquipmentType.InfantryKit, gen, 15);
            usa.AddStockpile(EquipmentType.PGM, gen, 5);
        }
        else if (usa.Defcon == 2)
        {
            usa.AddStockpile(EquipmentType.TacticalAir, gen, 5);
            usa.AddStockpile(EquipmentType.MBT, gen, 12);
            usa.AddStockpile(EquipmentType.IFV, gen, 25);
            usa.AddStockpile(EquipmentType.InfantryKit, gen, 35);
            usa.AddStockpile(EquipmentType.PGM, gen, 15);
        }
        else if (usa.Defcon == 1)
        {
            usa.AddStockpile(EquipmentType.TacticalAir, gen, 10);
            usa.AddStockpile(EquipmentType.MBT, gen, 25);
            usa.AddStockpile(EquipmentType.IFV, gen, 50);
            usa.AddStockpile(EquipmentType.InfantryKit, gen, 75);
            usa.AddStockpile(EquipmentType.PGM, gen, 30);
        }
    }
}
