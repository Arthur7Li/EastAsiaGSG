using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 2 (Daily at 00:00) system that transfers equipment (InfantryKit through AttackHeli) and manpower
/// from national stockpiles to depleted field divisions in valid supply, and recovers daily Organization.
/// </summary>
public static class ReinforcementSystem
{
    public const float MinReinforcementSupplyRatio = 0.15f;
    public const float BaseDailyStrengthRecovery = 0.10f;
    public const float BaseDailyOrgRecovery = 4.0f;
    public const float GreenReplacementExperience = 0.20f;

    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    /// <summary>
    /// Resolves the DivisionTemplateRecord for a field division, falling back to hardness-inferred defaults
    /// if the division's TemplateId is out of bounds or unconfigured.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DivisionTemplateRecord GetTemplateForDivision(SimulationWorld world, in DivisionComponent div)
    {
        ushort templateId = div.TemplateId;
        if (templateId < SimulationWorld.MaxDivisionTemplates && world.Templates[templateId].IsConfigured != 0)
        {
            return world.Templates[templateId];
        }

        if (div.Hardness >= 0.65f)
        {
            return DivisionTemplateRecord.CreateArmoredDivision(templateId);
        }

        if (div.Hardness >= 0.35f)
        {
            return DivisionTemplateRecord.CreateMechanizedDivision(templateId);
        }

        return DivisionTemplateRecord.CreateInfantryDivision(templateId);
    }

    /// <summary>
    /// Consumes up to requestedUnits of the specified EquipmentType from a country's stockpile,
    /// drawing from newest generation (Gen 3 -> Gen 0) first. Returns the actual number of units deducted.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ConsumeArchetypeFromStockpile(
        ref CountryState country,
        EquipmentType archetype,
        int requestedUnits)
    {
        if (requestedUnits <= 0)
        {
            return 0;
        }

        int remaining = requestedUnits;
        for (int gen = (int)TechGeneration.Gen3_1995DigitalStealth; gen >= 0 && remaining > 0; gen--)
        {
            TechGeneration techGen = (TechGeneration)gen;
            int available = country.GetStockpile(archetype, techGen);
            if (available > 0)
            {
                int deduct = available < remaining ? available : remaining;
                country.AddStockpile(archetype, techGen, -deduct);
                remaining -= deduct;
            }
        }

        return requestedUnits - remaining;
    }

    /// <summary>
    /// Attempts to reinforce a single depleted division for one daily tick using national EquipmentStockpile and Manpower.
    /// Returns the actual Strength delta gained (0.0f if ineligible or out of supply/stockpile/manpower).
    /// </summary>
    public static unsafe float ReinforceSingleDivision(SimulationWorld world, ref DivisionComponent div)
    {
        if (div.State == DivisionState.Shattered
            || div.State == DivisionState.Attacking
            || div.State == DivisionState.Defending
            || div.State == DivisionState.Retreating
            || div.Strength <= 0.0f
            || div.Strength >= 1.0f
            || div.CurrentProvinceId >= SimulationWorld.MaxProvinces
            || div.CountryTag >= SimulationWorld.MaxCountries)
        {
            return 0.0f;
        }

        ref readonly ProvinceData prov = ref world.Provinces[div.CurrentProvinceId];
        bool inFriendlyTerritory = prov.ControllerTag == CountryTag.None
            || DivisionMovementSystem.IsFriendlyOrAllied(world, div.CountryTag, prov.ControllerTag);

        if (!inFriendlyTerritory || prov.SupplyRatio < MinReinforcementSupplyRatio)
        {
            return 0.0f;
        }

        ref CountryState country = ref world.Countries[div.CountryTag];
        if (country.Manpower <= 0)
        {
            return 0.0f;
        }

        float supplyFactor = System.Math.Clamp(prov.SupplyRatio, 0.0f, 1.0f);
        // Moving divisions in friendly territory reinforce at 50% of the stationary Idle rate
        float stateRateFactor = div.State == DivisionState.Idle ? 1.0f : 0.50f;
        float maxDailyStep = BaseDailyStrengthRecovery * supplyFactor * stateRateFactor;
        float missingStrength = 1.0f - div.Strength;
        float targetStrengthDelta = missingStrength < maxDailyStep ? missingStrength : maxDailyStep;

        if (targetStrengthDelta <= 0.0001f)
        {
            return 0.0f;
        }

        DivisionTemplateRecord tpl = GetTemplateForDivision(world, in div);
        int fullManpower = tpl.TotalManpower > 0 ? tpl.TotalManpower : 10_000;
        int neededManpower = System.Math.Max(1, (int)System.MathF.Round(fullManpower * targetStrengthDelta));

        float manpowerFulfillment = country.Manpower >= neededManpower
            ? 1.0f
            : (float)country.Manpower / neededManpower;

        if (manpowerFulfillment <= 0.0f)
        {
            return 0.0f;
        }

        Span<int> neededPerArchetype = stackalloc int[DivisionTemplateRecord.LandArchetypeCount];
        Span<int> deliverablePerArchetype = stackalloc int[DivisionTemplateRecord.LandArchetypeCount];
        int totalNeededEquipment = 0;
        int totalDeliverableEquipment = 0;

        for (int k = 0; k < DivisionTemplateRecord.LandArchetypeCount; k++)
        {
            ushort fullReq = tpl.RequiredEquipment[k];
            if (fullReq == 0)
            {
                continue;
            }

            int needed = System.Math.Max(1, (int)System.MathF.Round(fullReq * targetStrengthDelta));
            int maxCrewable = System.Math.Max(1, (int)System.MathF.Ceiling(needed * manpowerFulfillment));
            int available = country.GetTotalArchetypeStockpile((EquipmentType)k);
            int deliverable = available < maxCrewable ? available : maxCrewable;

            neededPerArchetype[k] = needed;
            deliverablePerArchetype[k] = deliverable;
            totalNeededEquipment += needed;
            totalDeliverableEquipment += deliverable;
        }

        if (totalNeededEquipment <= 0 || totalDeliverableEquipment <= 0)
        {
            return 0.0f;
        }

        float equipFulfillment = (float)totalDeliverableEquipment / totalNeededEquipment;
        float effectiveFulfillment = manpowerFulfillment < equipFulfillment
            ? manpowerFulfillment
            : equipFulfillment;

        if (effectiveFulfillment <= 0.0f)
        {
            return 0.0f;
        }

        // Deduct delivered equipment across all required land archetypes (newest generation first)
        for (int k = 0; k < DivisionTemplateRecord.LandArchetypeCount; k++)
        {
            int toConsume = deliverablePerArchetype[k];
            if (toConsume > 0)
            {
                ConsumeArchetypeFromStockpile(ref country, (EquipmentType)k, toConsume);
            }
        }

        // Deduct manpower transferred to field division
        int consumedManpower = System.Math.Min(
            country.Manpower,
            System.Math.Max(1, (int)System.MathF.Round(neededManpower * effectiveFulfillment)));
        country.Manpower -= consumedManpower;

        // Apply strength recovery and proportional veteran experience dilution from green replacements
        float oldStrength = div.Strength;
        float actualStrengthGain = targetStrengthDelta * effectiveFulfillment;
        float newStrength = System.MathF.Min(1.0f, oldStrength + actualStrengthGain);
        float realizedGain = newStrength - oldStrength;

        if (realizedGain > 0.0f && newStrength > 0.0f)
        {
            float blendedExp = (div.Experience * oldStrength + GreenReplacementExperience * realizedGain) / newStrength;
            div.Experience = System.Math.Clamp(blendedExp, 0.0f, 1.0f);
        }

        div.Strength = newStrength;
        return realizedGain;
    }

    /// <summary>
    /// Executes daily Organization recovery and Strength/Equipment reinforcement across all divisions in SimulationWorld.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        Query query = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<DivisionComponent> divisions = chunk.GetSpan<DivisionComponent>();
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                ref DivisionComponent div = ref divisions[i];
                if (div.State == DivisionState.Shattered
                    || div.Strength <= 0.0f
                    || div.CurrentProvinceId >= SimulationWorld.MaxProvinces)
                {
                    continue;
                }

                // 1. Reinforce depleted Strength using national EquipmentStockpile & Manpower when in valid supply
                if (div.Strength < 1.0f)
                {
                    ReinforceSingleDivision(world, ref div);
                }

                // 2. Recover Organization for stationary Idle divisions in valid supply
                if (div.State == DivisionState.Idle && div.Organization < div.MaxOrganization)
                {
                    ref readonly ProvinceData prov = ref world.Provinces[div.CurrentProvinceId];
                    bool inFriendlyTerritory = prov.ControllerTag == CountryTag.None
                        || DivisionMovementSystem.IsFriendlyOrAllied(world, div.CountryTag, prov.ControllerTag);

                    if (inFriendlyTerritory && prov.SupplyRatio >= MinReinforcementSupplyRatio)
                    {
                        float supplyRatio = System.Math.Clamp(prov.SupplyRatio, 0.10f, 1.0f);
                        float dailyOrgRegen = BaseDailyOrgRecovery * supplyRatio;
                        div.Organization = System.MathF.Min(div.MaxOrganization, div.Organization + dailyOrgRegen);
                    }
                }
            }
        }
    }
}
