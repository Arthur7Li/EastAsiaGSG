using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Immutable telemetry result of a single 6-hour fire exchange between an attacking/shooting division and a target division.
/// </summary>
public readonly record struct CombatExchangeResult(
    float BaseAttack,
    float EffectiveAttack,
    float EffectiveDefense,
    float ExpectedHits,
    float OrgDamage,
    float StrengthDamage,
    bool PgmUsed,
    bool AttackerPiercedTarget,
    bool AttackerArmorUnpiercedByTarget);

/// <summary>
/// Tier 1 (Every 6 Hours) system resolving HOI4 land combat math:
/// Soft/Hard attack hardness blend, Armor/Piercing gate, 10%/40% Defense/Breakthrough hit roll,
/// PGM stockpile consumption (+35% attack / +25% breakthrough), Org/Strength damage, and retreat/encirclement.
/// </summary>
public static class CombatResolutionSystem
{
    public const float PgmAttackMultiplier = 1.35f;
    public const float PgmBreakthroughMultiplier = 1.25f;
    public const float UnpiercedArmorAttackPenalty = 0.50f;
    public const float UnpiercedArmorOrgDamageBonus = 1.40f;
    public const float BlockedHitRate = 0.10f;
    public const float UnblockedHitRate = 0.40f;
    public const float OrgDamagePerHit = 0.18f;
    public const float StrengthDamagePerHit = 0.04f;
    public const int PgmUnitsConsumedPerSixHourTick = 5;

    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();

    /// <summary>
    /// Pure mathematical kernel computing the exact 6-hour combat exchange specified in GAME_DESIGN_SPEC.md Section 4.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CombatExchangeResult CalculateExchange(
        float softAttack,
        float hardAttack,
        float attackerPiercing,
        float attackerArmor,
        float targetHardness,
        float targetArmor,
        float targetPiercing,
        float targetDefenseOrBreakthrough,
        float terrainModifier = 1.0f,
        float supplyModifier = 1.0f,
        float airModifier = 1.0f,
        bool pgmActiveForAttacker = false,
        bool pgmActiveForTarget = false,
        bool targetIsAttackingSide = false)
    {
        float clampedHardness = System.Math.Clamp(targetHardness, 0.0f, 1.0f);

        // 1. Hardness Blend: A_base = A_s * (1 - H_t) + A_h * H_t
        float baseAttack = softAttack * (1.0f - clampedHardness) + hardAttack * clampedHardness;

        // 2. Modifiers & PGM Multiplier
        float pgmMod = pgmActiveForAttacker ? PgmAttackMultiplier : 1.00f;
        bool piercedTarget = attackerPiercing >= targetArmor;
        float armorMod = piercedTarget ? 1.00f : UnpiercedArmorAttackPenalty;

        float effectiveAttack = baseAttack * terrainModifier * supplyModifier * airModifier * pgmMod * armorMod;

        // 3. Defense / Breakthrough Mitigation (D_eff)
        float targetPgmBreakthroughMod = (targetIsAttackingSide && pgmActiveForTarget)
            ? PgmBreakthroughMultiplier
            : 1.00f;
        float effectiveDefense = targetDefenseOrBreakthrough * targetPgmBreakthroughMod;

        float blockedAttacks = System.MathF.Min(effectiveAttack, effectiveDefense);
        float unblockedAttacks = System.MathF.Max(0.0f, effectiveAttack - effectiveDefense);
        float expectedHits = BlockedHitRate * blockedAttacks + UnblockedHitRate * unblockedAttacks;

        // 4. Organization & Strength Damage
        bool attackerArmorUnpierced = attackerArmor > targetPiercing;
        float orgArmorBonus = attackerArmorUnpierced ? UnpiercedArmorOrgDamageBonus : 1.00f;

        float orgDamage = expectedHits * OrgDamagePerHit * orgArmorBonus;
        float strengthDamage = expectedHits * StrengthDamagePerHit;

        return new CombatExchangeResult(
            BaseAttack: baseAttack,
            EffectiveAttack: effectiveAttack,
            EffectiveDefense: effectiveDefense,
            ExpectedHits: expectedHits,
            OrgDamage: orgDamage,
            StrengthDamage: strengthDamage,
            PgmUsed: pgmActiveForAttacker,
            AttackerPiercedTarget: piercedTarget,
            AttackerArmorUnpiercedByTarget: attackerArmorUnpierced);
    }

    /// <summary>
    /// Resolves one 6-hour combat step across all active land battles in SimulationWorld.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        Span<Entity> combatEntities = stackalloc Entity[64];
        int combatCount = 0;

        Query query = world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in query)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (combats[i].Outcome == CombatOutcome.Ongoing && combatCount < combatEntities.Length)
                {
                    combatEntities[combatCount++] = entities[i];
                }
            }
        }

        for (int i = 0; i < combatCount; i++)
        {
            ref ActiveCombatComponent combat = ref world.EcsWorld.Get<ActiveCombatComponent>(combatEntities[i]);
            ResolveSingleCombat(world, ref combat);
        }
    }

    private static unsafe void ResolveSingleCombat(SimulationWorld world, ref ActiveCombatComponent combat)
    {
        ushort targetProvinceId = combat.TargetProvinceId;
        if (targetProvinceId >= SimulationWorld.MaxProvinces)
        {
            combat.Outcome = CombatOutcome.DefenderVictory;
            return;
        }

        Span<Entity> attackers = stackalloc Entity[32];
        Span<Entity> defenders = stackalloc Entity[32];
        int attackerTotal = 0;
        int defenderTotal = 0;

        combat.ClearOriginProvinces();

        // Collect all participating attackers and defenders for targetProvinceId
        Query divQuery = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divisions = chunk.GetSpan<DivisionComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                ref DivisionComponent div = ref divisions[i];
                if (div.State == DivisionState.Retreating || div.State == DivisionState.Shattered)
                {
                    continue;
                }

                if (div.State == DivisionState.Attacking && div.TargetProvinceId == targetProvinceId)
                {
                    if (div.Strength <= 0.0f)
                    {
                        div.State = DivisionState.Shattered;
                        div.ActiveCombatId = DivisionComponent.NoCombat;
                        div.ClearWaypoints();
                        continue;
                    }

                    if (div.Organization <= 0.0f)
                    {
                        div.State = DivisionState.Idle;
                        div.ActiveCombatId = DivisionComponent.NoCombat;
                        div.ClearWaypoints();
                        continue;
                    }

                    if (attackerTotal < attackers.Length)
                    {
                        attackers[attackerTotal++] = entities[i];
                        div.ActiveCombatId = combat.CombatId;
                        combat.RegisterAttackOrigin(div.CurrentProvinceId);
                    }
                }
                else if (div.CurrentProvinceId == targetProvinceId
                         && !DivisionMovementSystem.IsFriendlyOrAllied(world, combat.AttackerTag, div.CountryTag))
                {
                    if (div.Strength <= 0.0f)
                    {
                        div.State = DivisionState.Shattered;
                        div.ActiveCombatId = DivisionComponent.NoCombat;
                        div.ClearWaypoints();
                        continue;
                    }

                    if (div.Organization <= 0.0f)
                    {
                        RetreatOrShatterDefender(world, ref div, targetProvinceId);
                        continue;
                    }

                    if (defenderTotal < defenders.Length)
                    {
                        div.State = DivisionState.Defending;
                        div.ActiveCombatId = combat.CombatId;
                        defenders[defenderTotal++] = entities[i];
                    }
                }
            }
        }

        // Check immediate victory conditions prior to exchange
        if (attackerTotal == 0)
        {
            ConcludeDefenderVictory(world, ref combat, defenders[..defenderTotal]);
            return;
        }

        if (defenderTotal == 0)
        {
            ConcludeAttackerVictory(world, ref combat, attackers[..attackerTotal]);
            return;
        }

        // Allocate frontline divisions within TotalCombatWidth (80 + 20 * extra_directions)
        ushort maxCombatWidth = combat.TotalCombatWidth;
        ushort usedAttackerWidth = 0;
        ushort usedDefenderWidth = 0;
        int engagedAttackerCount = 0;
        int engagedDefenderCount = 0;

        for (int i = 0; i < attackerTotal; i++)
        {
            ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
            byte width = att.CombatWidth == 0 ? (byte)20 : att.CombatWidth;
            if (engagedAttackerCount == 0 || usedAttackerWidth + width <= maxCombatWidth)
            {
                (attackers[engagedAttackerCount], attackers[i]) = (attackers[i], attackers[engagedAttackerCount]);
                usedAttackerWidth += width;
                engagedAttackerCount++;
            }
            else
            {
                continue;
            }
        }

        for (int i = 0; i < defenderTotal; i++)
        {
            ref DivisionComponent def = ref world.EcsWorld.Get<DivisionComponent>(defenders[i]);
            byte width = def.CombatWidth == 0 ? (byte)20 : def.CombatWidth;
            if (engagedDefenderCount == 0 || usedDefenderWidth + width <= maxCombatWidth)
            {
                (defenders[engagedDefenderCount], defenders[i]) = (defenders[i], defenders[engagedDefenderCount]);
                usedDefenderWidth += width;
                engagedDefenderCount++;
            }
            else
            {
                continue;
            }
        }

        combat.UsedAttackerWidth = usedAttackerWidth;
        combat.UsedDefenderWidth = usedDefenderWidth;
        combat.EngagedAttackerCount = (byte)engagedAttackerCount;
        combat.EngagedDefenderCount = (byte)engagedDefenderCount;

        // Determine PGM availability and consume national PGM stockpile for authorized engaged divisions
        Span<bool> attackerPgmActive = stackalloc bool[engagedAttackerCount];
        Span<bool> defenderPgmActive = stackalloc bool[engagedDefenderCount];

        for (int i = 0; i < engagedAttackerCount; i++)
        {
            ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
            attackerPgmActive[i] = TryConsumePgmForDivision(world, in att);
        }

        for (int i = 0; i < engagedDefenderCount; i++)
        {
            ref DivisionComponent def = ref world.EcsWorld.Get<DivisionComponent>(defenders[i]);
            defenderPgmActive[i] = TryConsumePgmForDivision(world, in def);
        }

        // Pre-allocate simultaneous damage accumulators
        Span<float> attackerOrgDamageTaken = stackalloc float[engagedAttackerCount];
        Span<float> attackerStrDamageTaken = stackalloc float[engagedAttackerCount];
        Span<float> defenderOrgDamageTaken = stackalloc float[engagedDefenderCount];
        Span<float> defenderStrDamageTaken = stackalloc float[engagedDefenderCount];

        ref readonly ProvinceData targetProv = ref world.Provinces[targetProvinceId];
        float defenderSupplyMod = ComputeSupplyModifier(targetProv.SupplyRatio);
        float baseTerrainAttackMod = GetAttackerTerrainModifier(targetProv.Terrain);

        float totalAttackerEffAttack = 0.0f;
        float totalDefenderEffAttack = 0.0f;
        float totalOrgDamageToDefenders = 0.0f;
        float totalStrDamageToDefenders = 0.0f;
        float totalOrgDamageToAttackers = 0.0f;
        float totalStrDamageToAttackers = 0.0f;

        // 1. Engaged Attackers fire on Engaged Defenders
        for (int i = 0; i < engagedAttackerCount; i++)
        {
            ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
            int targetIdx = i % engagedDefenderCount;
            ref DivisionComponent def = ref world.EcsWorld.Get<DivisionComponent>(defenders[targetIdx]);

            ref readonly ProvinceData originProv = ref world.Provinces[att.CurrentProvinceId];
            float attackerSupplyMod = ComputeSupplyModifier(originProv.SupplyRatio);
            float edgePenalty = GetCrossingModifier(world, in originProv, targetProvinceId);
            float terrainMod = baseTerrainAttackMod * edgePenalty;
            float airMod = ComputeAirModifier(world, targetProv.StrategicRegionId, att.CountryTag, def.CountryTag);

            float strFactor = System.Math.Clamp(att.Strength, 0.10f, 1.0f);
            float defStrFactor = System.Math.Clamp(def.Strength, 0.10f, 1.0f);
            float defEntrenchmentMod = 1.0f + def.Entrenchment;

            CombatExchangeResult exchange = CalculateExchange(
                softAttack: att.SoftAttack * strFactor,
                hardAttack: att.HardAttack * strFactor,
                attackerPiercing: att.Piercing,
                attackerArmor: att.Armor,
                targetHardness: def.Hardness,
                targetArmor: def.Armor,
                targetPiercing: def.Piercing,
                targetDefenseOrBreakthrough: def.Defense * defStrFactor * defEntrenchmentMod,
                terrainModifier: terrainMod,
                supplyModifier: attackerSupplyMod,
                airModifier: airMod,
                pgmActiveForAttacker: attackerPgmActive[i],
                pgmActiveForTarget: defenderPgmActive[targetIdx],
                targetIsAttackingSide: false);

            defenderOrgDamageTaken[targetIdx] += exchange.OrgDamage;
            defenderStrDamageTaken[targetIdx] += exchange.StrengthDamage;
            totalOrgDamageToDefenders += exchange.OrgDamage;
            totalStrDamageToDefenders += exchange.StrengthDamage;
            totalAttackerEffAttack += exchange.EffectiveAttack;
        }

        // 2. Engaged Defenders return fire on Engaged Attackers
        for (int i = 0; i < engagedDefenderCount; i++)
        {
            ref DivisionComponent def = ref world.EcsWorld.Get<DivisionComponent>(defenders[i]);
            int targetIdx = i % engagedAttackerCount;
            ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[targetIdx]);

            float airMod = ComputeAirModifier(world, targetProv.StrategicRegionId, def.CountryTag, att.CountryTag);
            float defStrFactor = System.Math.Clamp(def.Strength, 0.10f, 1.0f);
            float attStrFactor = System.Math.Clamp(att.Strength, 0.10f, 1.0f);
            float defEntrenchmentMod = 1.0f + def.Entrenchment;

            CombatExchangeResult exchange = CalculateExchange(
                softAttack: def.SoftAttack * defStrFactor * defEntrenchmentMod,
                hardAttack: def.HardAttack * defStrFactor * defEntrenchmentMod,
                attackerPiercing: def.Piercing,
                attackerArmor: def.Armor,
                targetHardness: att.Hardness,
                targetArmor: att.Armor,
                targetPiercing: att.Piercing,
                targetDefenseOrBreakthrough: att.Breakthrough * attStrFactor,
                terrainModifier: 1.0f,
                supplyModifier: defenderSupplyMod,
                airModifier: airMod,
                pgmActiveForAttacker: defenderPgmActive[i],
                pgmActiveForTarget: attackerPgmActive[targetIdx],
                targetIsAttackingSide: true);

            attackerOrgDamageTaken[targetIdx] += exchange.OrgDamage;
            attackerStrDamageTaken[targetIdx] += exchange.StrengthDamage;
            totalOrgDamageToAttackers += exchange.OrgDamage;
            totalStrDamageToAttackers += exchange.StrengthDamage;
            totalDefenderEffAttack += exchange.EffectiveAttack;
        }

        // 3. Apply simultaneous Organization and Strength damage & evaluate retreats/shatters
        int remainingAttackers = 0;
        float attackerOrgSum = 0.0f;
        float attackerMaxOrgSum = 0.0f;

        for (int i = 0; i < attackerTotal; i++)
        {
            ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackers[i]);
            if (i < engagedAttackerCount)
            {
                att.Organization = System.MathF.Max(0.0f, att.Organization - attackerOrgDamageTaken[i]);
                att.Strength = System.MathF.Max(0.0f, att.Strength - attackerStrDamageTaken[i] * 0.01f);
                att.Experience = System.MathF.Min(1.0f, att.Experience + 0.002f);
            }

            if (att.Strength <= 0.0f)
            {
                att.State = DivisionState.Shattered;
                att.ActiveCombatId = DivisionComponent.NoCombat;
                att.ClearWaypoints();
            }
            else if (att.Organization <= 0.0f)
            {
                // Attacking division breaks off the attack and stays in its origin province
                att.State = DivisionState.Idle;
                att.ActiveCombatId = DivisionComponent.NoCombat;
                att.ClearWaypoints();
            }
            else
            {
                remainingAttackers++;
                attackerOrgSum += att.Organization;
                attackerMaxOrgSum += System.MathF.Max(1.0f, att.MaxOrganization);
            }
        }

        int remainingDefenders = 0;
        float defenderOrgSum = 0.0f;
        float defenderMaxOrgSum = 0.0f;

        for (int i = 0; i < defenderTotal; i++)
        {
            ref DivisionComponent def = ref world.EcsWorld.Get<DivisionComponent>(defenders[i]);
            if (i < engagedDefenderCount)
            {
                def.Organization = System.MathF.Max(0.0f, def.Organization - defenderOrgDamageTaken[i]);
                def.Strength = System.MathF.Max(0.0f, def.Strength - defenderStrDamageTaken[i] * 0.01f);
                def.Experience = System.MathF.Min(1.0f, def.Experience + 0.002f);
            }

            if (def.Strength <= 0.0f)
            {
                def.State = DivisionState.Shattered;
                def.ActiveCombatId = DivisionComponent.NoCombat;
                def.ClearWaypoints();
            }
            else if (def.Organization <= 0.0f)
            {
                RetreatOrShatterDefender(world, ref def, targetProvinceId);
            }
            else
            {
                remainingDefenders++;
                defenderOrgSum += def.Organization;
                defenderMaxOrgSum += System.MathF.Max(1.0f, def.MaxOrganization);
            }
        }

        combat.ElapsedSixHourTicks++;
        combat.LastTickAttackerOrgDamage = totalOrgDamageToDefenders;
        combat.LastTickDefenderOrgDamage = totalOrgDamageToAttackers;
        combat.LastTickAttackerStrengthDamage = totalStrDamageToDefenders;
        combat.LastTickDefenderStrengthDamage = totalStrDamageToAttackers;

        // 4. Resolve final battle outcome or update 0..100 BattleProgress bubble
        if (remainingDefenders == 0 && remainingAttackers > 0)
        {
            ConcludeAttackerVictory(world, ref combat, attackers[..attackerTotal]);
        }
        else if (remainingAttackers == 0)
        {
            ConcludeDefenderVictory(world, ref combat, defenders[..defenderTotal]);
        }
        else
        {
            float attOrgRatio = attackerOrgSum / System.MathF.Max(1.0f, attackerMaxOrgSum);
            float defOrgRatio = defenderOrgSum / System.MathF.Max(1.0f, defenderMaxOrgSum);
            float attackPowerRatio = totalAttackerEffAttack / System.MathF.Max(1.0f, totalAttackerEffAttack + totalDefenderEffAttack);
            float orgBalance = attOrgRatio / System.MathF.Max(0.01f, attOrgRatio + defOrgRatio);

            float progress = (attackPowerRatio * 0.45f + orgBalance * 0.55f) * 100.0f;
            combat.BattleProgress = System.Math.Clamp(progress, 1.0f, 99.0f);
        }
    }

    private static void RetreatOrShatterDefender(
        SimulationWorld world,
        ref DivisionComponent defender,
        ushort contestedProvinceId)
    {
        defender.ActiveCombatId = DivisionComponent.NoCombat;
        defender.ClearWaypoints();

        ref readonly ProvinceData contestedProv = ref world.Provinces[contestedProvinceId];
        ReadOnlySpan<ushort> neighbors = world.Graph.GetNeighbors(in contestedProv);

        ushort bestRetreatProvince = DivisionComponent.NoProvince;
        for (int i = 0; i < neighbors.Length; i++)
        {
            ushort neighborId = neighbors[i];
            if (neighborId >= SimulationWorld.MaxProvinces)
            {
                continue;
            }

            ref readonly ProvinceData neighborProv = ref world.Provinces[neighborId];
            if (neighborProv.Terrain != TerrainType.Ocean
                && DivisionMovementSystem.IsFriendlyOrAllied(world, defender.CountryTag, neighborProv.ControllerTag)
                && !HasHostileAttackersInProvince(world, neighborId, defender.CountryTag))
            {
                bestRetreatProvince = neighborId;
                break;
            }
        }

        if (bestRetreatProvince != DivisionComponent.NoProvince)
        {
            // Immediate tactical withdrawal to adjacent friendly province
            defender.CurrentProvinceId = bestRetreatProvince;
            defender.TargetProvinceId = DivisionComponent.NoProvince;
            defender.RetreatProvinceId = DivisionComponent.NoProvince;
            defender.MovementProgress = 0.0f;
            defender.ClearWaypoints();
            defender.State = DivisionState.Idle;
        }
        else
        {
            // Encircled: no friendly adjacent province available -> division shatters!
            defender.State = DivisionState.Shattered;
            defender.Strength = 0.0f;
        }
    }

    private static bool HasHostileAttackersInProvince(SimulationWorld world, ushort provinceId, byte defenderTag)
    {
        Query divQuery = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divisions = chunk.GetSpan<DivisionComponent>();
            int chunkSize = chunk.Count;
            for (int i = 0; i < chunkSize; i++)
            {
                ref DivisionComponent div = ref divisions[i];
                if (div.CurrentProvinceId == provinceId
                    && div.IsCombatCapable
                    && !DivisionMovementSystem.IsFriendlyOrAllied(world, defenderTag, div.CountryTag))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static unsafe void ConcludeAttackerVictory(
        SimulationWorld world,
        ref ActiveCombatComponent combat,
        ReadOnlySpan<Entity> attackerEntities)
    {
        combat.Outcome = CombatOutcome.AttackerVictory;
        combat.BattleProgress = 100.0f;

        ushort capturedProvinceId = combat.TargetProvinceId;
        world.Provinces[capturedProvinceId].ControllerTag = combat.AttackerTag;

        for (int i = 0; i < attackerEntities.Length; i++)
        {
            ref DivisionComponent att = ref world.EcsWorld.Get<DivisionComponent>(attackerEntities[i]);
            if (!att.IsCombatCapable)
            {
                continue;
            }

            att.CurrentProvinceId = capturedProvinceId;
            att.MovementProgress = 0.0f;
            att.ActiveCombatId = DivisionComponent.NoCombat;

            int nextIndex = att.CurrentWaypointIndex + 1;
            if (nextIndex < att.WaypointCount)
            {
                att.CurrentWaypointIndex = (byte)nextIndex;
                att.TargetProvinceId = att.Waypoints[nextIndex];
                att.CurrentEdgeCost = DivisionMovementSystem.ComputeHopCost(
                    world,
                    att.CurrentProvinceId,
                    att.TargetProvinceId,
                    att.CountryTag);
                att.State = DivisionState.Moving;
            }
            else
            {
                if (att.DestinationProvinceId != att.CurrentProvinceId
                    && att.DestinationProvinceId != DivisionComponent.NoProvince)
                {
                    if (!DivisionMovementSystem.IssueMoveOrder(world, ref att, att.DestinationProvinceId))
                    {
                        att.ClearWaypoints();
                        att.State = DivisionState.Idle;
                    }
                }
                else
                {
                    att.ClearWaypoints();
                    att.State = DivisionState.Idle;
                }
            }
        }
    }

    private static void ConcludeDefenderVictory(
        SimulationWorld world,
        ref ActiveCombatComponent combat,
        ReadOnlySpan<Entity> defenderEntities)
    {
        combat.Outcome = CombatOutcome.DefenderVictory;
        combat.BattleProgress = 0.0f;

        for (int i = 0; i < defenderEntities.Length; i++)
        {
            ref DivisionComponent def = ref world.EcsWorld.Get<DivisionComponent>(defenderEntities[i]);
            if (def.IsCombatCapable)
            {
                def.ActiveCombatId = DivisionComponent.NoCombat;
                def.State = DivisionState.Idle;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConsumePgmForDivision(SimulationWorld world, in DivisionComponent div)
    {
        if (!div.IsPgmAuthorized || div.CountryTag >= SimulationWorld.MaxCountries)
        {
            return false;
        }

        ref CountryState country = ref world.Countries[div.CountryTag];
        int totalPgm = country.GetTotalArchetypeStockpile(EquipmentType.PGM);
        if (totalPgm <= 0)
        {
            return false;
        }

        int remainingToConsume = PgmUnitsConsumedPerSixHourTick;
        for (int gen = (int)TechGeneration.Gen3_1995DigitalStealth; gen >= 0 && remainingToConsume > 0; gen--)
        {
            TechGeneration techGen = (TechGeneration)gen;
            int available = country.GetStockpile(EquipmentType.PGM, techGen);
            if (available > 0)
            {
                int deduct = available < remainingToConsume ? available : remainingToConsume;
                country.AddStockpile(EquipmentType.PGM, techGen, -deduct);
                remainingToConsume -= deduct;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetAttackerTerrainModifier(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Plains => 1.00f,
            TerrainType.Forest => 0.85f,
            TerrainType.Hills => 0.80f,
            TerrainType.Mountain => 0.60f,
            TerrainType.Urban => 0.70f,
            TerrainType.Jungle => 0.65f,
            TerrainType.Marsh => 0.70f,
            TerrainType.Ocean => 0.50f,
            _ => 1.00f
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetCrossingModifier(
        SimulationWorld world,
        in ProvinceData fromProv,
        ushort targetProvinceId)
    {
        if (!world.Graph.TryGetEdgeFlags(in fromProv, targetProvinceId, out AdjacencyEdgeFlags flags))
        {
            return 1.00f;
        }

        float mod = 1.00f;
        if ((flags & AdjacencyEdgeFlags.River) != 0)
        {
            mod *= 0.75f;
        }
        if ((flags & AdjacencyEdgeFlags.Strait) != 0)
        {
            mod *= 0.55f;
        }
        return mod;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeSupplyModifier(float supplyRatio)
    {
        return System.Math.Clamp(0.50f + 0.50f * supplyRatio, 0.50f, 1.00f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeAirModifier(
        SimulationWorld world,
        byte strategicRegionId,
        byte shooterTag,
        byte targetTag)
    {
        if (strategicRegionId >= SimulationWorld.MaxStrategicRegions
            || shooterTag >= SimulationWorld.MaxCountries
            || targetTag >= SimulationWorld.MaxCountries)
        {
            return 1.00f;
        }

        FactionId shooterFaction = world.Countries[shooterTag].Faction;
        FactionId targetFaction = world.Countries[targetTag].Faction;
        if (shooterFaction == FactionId.Neutral && targetFaction == FactionId.Neutral)
        {
            return 1.00f;
        }

        ref readonly StrategicRegionData region = ref world.Regions[strategicRegionId];
        float ourAir = shooterFaction != FactionId.Neutral ? region.GetAirSuperiority(shooterFaction) : 0.0f;
        float enemyAir = targetFaction != FactionId.Neutral ? region.GetAirSuperiority(targetFaction) : 0.0f;

        float delta = ourAir - enemyAir;
        return System.Math.Clamp(1.00f + delta * 0.25f, 0.75f, 1.25f);
    }
}
