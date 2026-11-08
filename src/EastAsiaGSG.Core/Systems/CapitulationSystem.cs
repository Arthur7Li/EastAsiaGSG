using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Daily Tier 2 system resolving Victory Points, stability surrender thresholds,
/// and national capitulation with zero heap allocations.
/// </summary>
public static unsafe class CapitulationSystem
{
    private static readonly QueryDescription DivisionQuery = new QueryDescription().WithAll<DivisionComponent>();
    private static readonly QueryDescription CombatQuery = new QueryDescription().WithAll<ActiveCombatComponent>();
    private static readonly QueryDescription TaskForceQuery = new QueryDescription().WithAll<NavalTaskForceComponent>();
    private static readonly QueryDescription AirWingQuery = new QueryDescription().WithAll<AirWingComponent>();

    /// <summary>
    /// Deterministic hash code generator for global capitulation news events.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCapitulationNewsHash(byte countryTag) => 0x7C000000 | (countryTag & 0xFF);

    /// <summary>
    /// Evaluates capitulation conditions for all active countries engaged in war.
    /// </summary>
    public static void Execute(SimulationWorld world)
    {
        for (byte c = 0; c < CountryTag.ActiveCount; c++)
        {
            if (world.HasCapitulated(c) || !world.IsAtWar(c))
            {
                continue;
            }

            EvaluateCapitulation(world, c);
        }
    }

    /// <summary>
    /// Evaluates if countryTag has lost sufficient Victory Points and its capital province to trigger surrender.
    /// </summary>
    public static bool EvaluateCapitulation(SimulationWorld world, byte countryTag)
    {
        if (countryTag >= CountryTag.ActiveCount || world.HasCapitulated(countryTag))
        {
            return false;
        }

        int totalOwnedVp = 0;
        int controlledOwnedVp = 0;
        Span<int> vpCapturedByEnemy = stackalloc int[SimulationWorld.MaxCountries];

        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan();
        for (ushort p = 0; p < SimulationWorld.MaxProvinces; p++)
        {
            ref readonly ProvinceData prov = ref provinces[p];
            if (prov.OwnerTag != countryTag)
            {
                continue;
            }

            int vp = prov.VictoryPoints > 0 ? prov.VictoryPoints : 1;
            totalOwnedVp += vp;

            if (prov.ControllerTag == countryTag || DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, prov.ControllerTag))
            {
                controlledOwnedVp += vp;
            }
            else if (prov.ControllerTag < SimulationWorld.MaxCountries && world.IsAtWarWith(countryTag, prov.ControllerTag))
            {
                vpCapturedByEnemy[prov.ControllerTag] += vp;
            }
        }

        if (totalOwnedVp == 0)
        {
            return false;
        }

        ushort capitalProv = world.GetCapitalProvince(countryTag);
        bool capitalHeld = capitalProv < SimulationWorld.MaxProvinces
            && (provinces[capitalProv].ControllerTag == countryTag
                || DivisionMovementSystem.IsFriendlyOrAllied(world, countryTag, provinces[capitalProv].ControllerTag));

        ref CountryState country = ref world.Countries[countryTag];
        float stability = System.Math.Clamp(country.Stability, 0.0f, 1.0f);
        float surrenderThreshold = 0.20f + (0.15f * stability);
        float vpRatio = (float)controlledOwnedVp / totalOwnedVp;

        if (vpRatio < surrenderThreshold && !capitalHeld)
        {
            ExecuteSurrender(world, countryTag, vpCapturedByEnemy);
            return true;
        }

        return false;
    }

    private static void ExecuteSurrender(
        SimulationWorld world,
        byte countryTag,
        ReadOnlySpan<int> vpCapturedByEnemy)
    {
        // 1. Determine primary conqueror
        byte primaryConqueror = CountryTag.None;
        int maxVp = -1;
        for (byte enemy = 0; enemy < CountryTag.ActiveCount; enemy++)
        {
            if (enemy != countryTag && world.IsAtWarWith(countryTag, enemy))
            {
                if (vpCapturedByEnemy[enemy] > maxVp)
                {
                    maxVp = vpCapturedByEnemy[enemy];
                    primaryConqueror = enemy;
                }
            }
        }

        if (primaryConqueror == CountryTag.None)
        {
            ushort capitalProv = world.GetCapitalProvince(countryTag);
            if (capitalProv < SimulationWorld.MaxProvinces && world.Provinces[capitalProv].ControllerTag < SimulationWorld.MaxCountries)
            {
                byte ctrl = world.Provinces[capitalProv].ControllerTag;
                if (ctrl != countryTag && world.IsAtWarWith(countryTag, ctrl))
                {
                    primaryConqueror = ctrl;
                }
            }
        }

        if (primaryConqueror == CountryTag.None)
        {
            for (byte enemy = 0; enemy < CountryTag.ActiveCount; enemy++)
            {
                if (enemy != countryTag && world.IsAtWarWith(countryTag, enemy))
                {
                    primaryConqueror = enemy;
                    break;
                }
            }
        }

        // 2. Transfer territorial control to primary conqueror
        if (primaryConqueror < SimulationWorld.MaxCountries)
        {
            for (ushort p = 0; p < SimulationWorld.MaxProvinces; p++)
            {
                ref ProvinceData prov = ref world.Provinces[p];
                if (prov.OwnerTag == countryTag || prov.ControllerTag == countryTag)
                {
                    prov.ControllerTag = primaryConqueror;
                }
            }

            for (ushort s = 0; s < SimulationWorld.MaxStates; s++)
            {
                ref StateData state = ref world.States[s];
                if (state.OwnerTag == countryTag || state.ControllerTag == countryTag)
                {
                    state.ControllerTag = primaryConqueror;
                }
            }

            // 3. Seize 50% of remaining equipment stockpiles
            ref CountryState defeated = ref world.Countries[countryTag];
            ref CountryState victor = ref world.Countries[primaryConqueror];
            for (int slot = 0; slot < CountryState.StockpileSlotCount; slot++)
            {
                int seized = defeated.EquipmentStockpile[slot] / 2;
                victor.EquipmentStockpile[slot] += seized;
                defeated.EquipmentStockpile[slot] = 0;
            }
        }

        // 4. Disband remaining field divisions
        Span<Entity> divToDestroy = stackalloc Entity[256];
        int divDestroyCount = 0;

        Query divQuery = world.EcsWorld.Query(in DivisionQuery);
        foreach (ref Chunk chunk in divQuery)
        {
            Span<DivisionComponent> divs = chunk.GetSpan<DivisionComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (divs[i].CountryTag == countryTag)
                {
                    if (divDestroyCount < divToDestroy.Length)
                    {
                        divToDestroy[divDestroyCount++] = entities[i];
                    }
                }
            }
        }

        for (int i = 0; i < divDestroyCount; i++)
        {
            world.EcsWorld.Destroy(divToDestroy[i]);
        }

        // 5. Clean up active combats involving this nation
        Span<Entity> combatToDestroy = stackalloc Entity[64];
        int combatDestroyCount = 0;

        Query combatQuery = world.EcsWorld.Query(in CombatQuery);
        foreach (ref Chunk chunk in combatQuery)
        {
            Span<ActiveCombatComponent> combats = chunk.GetSpan<ActiveCombatComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            int chunkSize = chunk.Count;

            for (int i = 0; i < chunkSize; i++)
            {
                if (combats[i].AttackerTag == countryTag || combats[i].DefenderTag == countryTag)
                {
                    if (combatDestroyCount < combatToDestroy.Length)
                    {
                        combatToDestroy[combatDestroyCount++] = entities[i];
                    }
                }
            }
        }

        for (int i = 0; i < combatDestroyCount; i++)
        {
            world.EcsWorld.Destroy(combatToDestroy[i]);
        }

        // 6. Disband or dock naval task forces and air wings
        Span<Entity> tfToDestroy = stackalloc Entity[64];
        int tfDestroyCount = 0;

        Query tfQuery = world.EcsWorld.Query(in TaskForceQuery);
        foreach (ref Chunk chunk in tfQuery)
        {
            Span<NavalTaskForceComponent> tfs = chunk.GetSpan<NavalTaskForceComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            for (int i = 0; i < chunk.Count; i++)
            {
                if (tfs[i].CountryTag == countryTag && tfDestroyCount < tfToDestroy.Length)
                {
                    tfToDestroy[tfDestroyCount++] = entities[i];
                }
            }
        }
        for (int i = 0; i < tfDestroyCount; i++)
        {
            world.EcsWorld.Destroy(tfToDestroy[i]);
        }

        Span<Entity> wingToDestroy = stackalloc Entity[64];
        int wingDestroyCount = 0;

        Query wingQuery = world.EcsWorld.Query(in AirWingQuery);
        foreach (ref Chunk chunk in wingQuery)
        {
            Span<AirWingComponent> wings = chunk.GetSpan<AirWingComponent>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            for (int i = 0; i < chunk.Count; i++)
            {
                if (wings[i].CountryTag == countryTag && wingDestroyCount < wingToDestroy.Length)
                {
                    wingToDestroy[wingDestroyCount++] = entities[i];
                }
            }
        }
        for (int i = 0; i < wingDestroyCount; i++)
        {
            world.EcsWorld.Destroy(wingToDestroy[i]);
        }

        // 7. Mark Capitulated and make peace with all opposing nations
        world.SetCapitulated(countryTag, true);

        for (byte enemy = 0; enemy < SimulationWorld.MaxCountries; enemy++)
        {
            if (world.IsAtWarWith(countryTag, enemy))
            {
                world.MakePeace(countryTag, enemy);
            }
        }

        // 8. Record news event
        int capitulationHash = GetCapitulationNewsHash(countryTag);
        world.RecordNewsEvent(capitulationHash, countryTag);

        // 9. Invalidate map revisions
        world.InvalidateOwnership();
        world.InvalidateSupply();
        world.InvalidateEconomy();
        world.InvalidateAirNaval();
    }
}
