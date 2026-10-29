using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 1 (Every 6 Hours: 00:00, 06:00, 12:00, 18:00) system that updates Air Superiority
/// and Naval Control ratios across the 18 Strategic Regions, resolves coastal A2/AD missile suppression,
/// and enforces the Strait &amp; Amphibious Control Gate (> 60% Naval Control and > 50% Air Superiority).
/// </summary>
public static class AirAndNavalRegionSystem
{
    public const float StraitNavalControlThreshold = StrategicRegionData.StraitNavalControlThreshold; // 0.60f
    public const float StraitAirSuperiorityThreshold = StrategicRegionData.StraitAirSuperiorityThreshold; // 0.50f

    public const ushort MaxCoastalA2adPenaltyBps = 4000; // 40% maximum A2/AD naval control penalty
    public const ushort BpsPerSamLevel = 1000;           // 10% penalty per SAM level above 2
    public const byte MinRadarSamForA2ad = 3;

    public const float AirbaseCapacityPerLevel = 100.0f;
    public const float NavalDockyardPower = 25.0f;
    public const float PortLevelPower = 12.0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetTechGenerationAirMultiplier(TechGeneration generation)
    {
        return generation switch
        {
            TechGeneration.Gen0_1970Reserve => 0.70f,
            TechGeneration.Gen1_1980Baseline => 1.00f,
            TechGeneration.Gen2_1988LateColdWar => 1.30f,
            TechGeneration.Gen3_1995DigitalStealth => 1.70f,
            _ => 1.00f
        };
    }

    /// <summary>
    /// Executes the 6-hour air superiority and naval control computation across all 18 Strategic Regions.
    /// Operates entirely with stack-allocated buffers for zero GC allocations.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        // 18 regions x 4 factions = 72 elements
        const int regionCount = SimulationWorld.MaxStrategicRegions;
        const int factionCount = StrategicRegionData.FactionSlotCount;

        Span<float> airPower = stackalloc float[regionCount * factionCount];
        Span<float> navalPower = stackalloc float[regionCount * factionCount];
        Span<ushort> a2adBps = stackalloc ushort[regionCount * factionCount];

        // Track state-level processing per region to avoid counting multi-province states redundantly
        Span<bool> stateProcessedPerRegion = stackalloc bool[regionCount * SimulationWorld.MaxStates];

        // Track remaining deployable TacticalAir per country across regions
        Span<int> remainingAirStockpile = stackalloc int[SimulationWorld.MaxCountries];
        Span<int> remainingGenAirStockpile = stackalloc int[SimulationWorld.MaxCountries * 4];
        for (byte c = 0; c < SimulationWorld.MaxCountries; c++)
        {
            ref readonly CountryState country = ref world.Countries[c];
            remainingAirStockpile[c] = country.GetTotalArchetypeStockpile(EquipmentType.TacticalAir);
            for (int g = 0; g < 4; g++)
            {
                remainingGenAirStockpile[(c * 4) + g] = country.GetStockpile(EquipmentType.TacticalAir, (TechGeneration)g);
            }
        }

        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan();
        ReadOnlySpan<StateData> states = world.States.AsSpan();

        // Single pass over 1,500 provinces to accumulate regional air and naval power
        for (int p = 0; p < provinces.Length; p++)
        {
            ref readonly ProvinceData prov = ref provinces[p];
            byte regionId = prov.StrategicRegionId;
            if (regionId >= regionCount)
            {
                continue;
            }

            // Province-level Port contribution to Naval Power
            if (prov.PortLevel > 0 && prov.ControllerTag < SimulationWorld.MaxCountries)
            {
                FactionId portFaction = world.Countries[prov.ControllerTag].Faction;
                if (portFaction != FactionId.Neutral)
                {
                    int fIdx = (int)portFaction & 0x3;
                    int navalOffset = (regionId * factionCount) + fIdx;
                    navalPower[navalOffset] += prov.PortLevel * PortLevelPower;
                }
            }

            // State-level facility processing (Airbases, SAM sites, Dockyards)
            ushort stateId = prov.StateId;
            if (stateId < SimulationWorld.MaxStates)
            {
                int stateRegionIdx = (regionId * SimulationWorld.MaxStates) + stateId;
                if (!stateProcessedPerRegion[stateRegionIdx])
                {
                    stateProcessedPerRegion[stateRegionIdx] = true;
                    ref readonly StateData state = ref states[stateId];

                    if (state.ControllerTag < SimulationWorld.MaxCountries)
                    {
                        byte countryTag = state.ControllerTag;
                        FactionId faction = world.Countries[countryTag].Faction;

                        if (faction != FactionId.Neutral)
                        {
                            int fIdx = (int)faction & 0x3;
                            int airOffset = (regionId * factionCount) + fIdx;
                            int navalOffset = (regionId * factionCount) + fIdx;
                            int a2adOffset = (regionId * factionCount) + fIdx;

                            // 1. Airbase Capacity & Stockpiled Aircraft Deployment
                            if (state.AirbaseLevel > 0)
                            {
                                int maxAirCapacity = (int)(state.AirbaseLevel * AirbaseCapacityPerLevel);
                                int availableStock = remainingAirStockpile[countryTag];
                                int deployedCount = availableStock < maxAirCapacity ? availableStock : maxAirCapacity;

                                if (deployedCount > 0)
                                {
                                    remainingAirStockpile[countryTag] -= deployedCount;

                                    float stateAirPower = 0.0f;
                                    int remainingToWeight = deployedCount;
                                    int countryGenBase = countryTag * 4;

                                    // Weight deployed aircraft by tech generation (Gen 3 -> Gen 0), consuming from remaining gen pool
                                    for (int gen = (int)TechGeneration.Gen3_1995DigitalStealth; gen >= 0 && remainingToWeight > 0; gen--)
                                    {
                                        int genStock = remainingGenAirStockpile[countryGenBase + gen];
                                        if (genStock > 0)
                                        {
                                            int slice = genStock < remainingToWeight ? genStock : remainingToWeight;
                                            float genMultiplier = GetTechGenerationAirMultiplier((TechGeneration)gen);
                                            stateAirPower += slice * genMultiplier;
                                            remainingToWeight -= slice;
                                            remainingGenAirStockpile[countryGenBase + gen] -= slice;
                                        }
                                    }

                                    // Fallback if generational breakdown was unspecified
                                    if (remainingToWeight > 0)
                                    {
                                        stateAirPower += remainingToWeight * 1.0f;
                                    }

                                    // Radar/SAM C4I coordination bonus (+10% per SAM level)
                                    float radarBonus = 1.0f + (0.10f * state.RadarSamLevel);
                                    airPower[airOffset] += stateAirPower * radarBonus;
                                }
                            }

                            // 2. Coastal Naval Dockyards
                            if (state.NavalDockyards > 0)
                            {
                                navalPower[navalOffset] += state.NavalDockyards * NavalDockyardPower;
                            }

                            // 3. Coastal A2/AD Missile Threat (RadarSamLevel >= 3 + PGM Stockpile)
                            if (state.RadarSamLevel >= MinRadarSamForA2ad)
                            {
                                int pgmStock = world.Countries[countryTag].GetTotalArchetypeStockpile(EquipmentType.PGM);
                                if (pgmStock > 0)
                                {
                                    ushort bonusBps = (ushort)System.Math.Min(MaxCoastalA2adPenaltyBps, (state.RadarSamLevel - 2) * BpsPerSamLevel);
                                    a2adBps[a2adOffset] = (ushort)System.Math.Min(MaxCoastalA2adPenaltyBps, a2adBps[a2adOffset] + bonusBps);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Second pass: Calculate normalized ratios across all 18 strategic regions
        byte usEscalation = world.Countries[CountryTag.USA].HomelandEscalation;
        int activeCsgs = SuperpowerEscalationSystem.GetActiveCsgCount(usEscalation);

        for (byte r = 0; r < regionCount; r++)
        {
            ref StrategicRegionData region = ref world.Regions[r];
            int regionBaseOffset = r * factionCount;

            // If this is a maritime sea zone, surge US Carrier Strike Groups
            if (region.IsSeaZone != 0 && activeCsgs > 0)
            {
                int blueforIdx = regionBaseOffset + (int)FactionId.BlueforPacom;
                airPower[blueforIdx] += activeCsgs * SuperpowerEscalationSystem.CsgAirPower;
                navalPower[blueforIdx] += activeCsgs * SuperpowerEscalationSystem.CsgNavalPower;
            }

            // Apply weather efficiency modifier to air power (clamped 0.10f to 1.00f)
            float weather = System.Math.Clamp(region.WeatherEfficiencyModifier, 0.10f, 1.00f);
            float totalAirPower = 0.0f;

            for (int f = 0; f < factionCount; f++)
            {
                airPower[regionBaseOffset + f] *= weather;
                totalAirPower += airPower[regionBaseOffset + f];
            }

            // Set normalized Air Superiority ratios
            if (totalAirPower > 0.0001f)
            {
                for (int f = 0; f < factionCount; f++)
                {
                    region.SetAirSuperiority((FactionId)f, airPower[regionBaseOffset + f] / totalAirPower);
                }
            }
            else
            {
                for (int f = 0; f < factionCount; f++)
                {
                    region.SetAirSuperiority((FactionId)f, 0.0f);
                }
            }

            // Find peak A2/AD penalty in the region
            ushort peakA2adBps = 0;
            for (int f = 0; f < factionCount; f++)
            {
                ushort bps = a2adBps[regionBaseOffset + f];
                if (bps > peakA2adBps)
                {
                    peakA2adBps = bps;
                }
            }
            region.CoastalRadarSamPenaltyBps = peakA2adBps;

            // Apply Air Support and A2/AD Penalties to Naval Power
            float totalNavalPower = 0.0f;
            for (int f = 0; f < factionCount; f++)
            {
                int navalIdx = regionBaseOffset + f;
                float currentNaval = navalPower[navalIdx];

                if (currentNaval > 0.0f)
                {
                    // Air support bonus: 50% to 100% effectiveness based on air superiority
                    float ourAirRatio = region.GetAirSuperiority((FactionId)f);
                    currentNaval *= (0.50f + (0.50f * ourAirRatio));

                    // Apply enemy A2/AD penalty
                    ushort enemyA2adBps = 0;
                    for (int otherF = 0; otherF < factionCount; otherF++)
                    {
                        if (otherF != f)
                        {
                            ushort enemyBps = a2adBps[regionBaseOffset + otherF];
                            if (enemyBps > enemyA2adBps)
                            {
                                enemyA2adBps = enemyBps;
                            }
                        }
                    }

                    if (enemyA2adBps > 0)
                    {
                        float penaltyFactor = System.Math.Max(0.0f, 1.0f - (enemyA2adBps / 10000.0f));
                        currentNaval *= penaltyFactor;
                    }

                    navalPower[navalIdx] = currentNaval;
                    totalNavalPower += currentNaval;
                }
            }

            // Set normalized Naval Control ratios
            if (totalNavalPower > 0.0001f)
            {
                for (int f = 0; f < factionCount; f++)
                {
                    region.SetNavalControl((FactionId)f, navalPower[regionBaseOffset + f] / totalNavalPower);
                }
            }
            else
            {
                for (int f = 0; f < factionCount; f++)
                {
                    region.SetNavalControl((FactionId)f, 0.0f);
                }
            }
        }
    }
}
