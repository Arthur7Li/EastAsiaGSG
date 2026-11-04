using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Core.Systems;

/// <summary>
/// Tier 3 (Monthly on the 1st at 00:00) system that recalculates Cold War macro-economic modifiers:
/// - Soviet / DPRK military-industrial overheating penalties in the late 1980s and 1990s.
/// - East Asian export-led industrialization growth bonuses (JPN, ROK, TWN, PRC) during peacetime.
/// - Baseline Stability and Consumer Goods clamping across all 15 active nations.
/// Zero heap allocations in hot loop.
/// </summary>
public static class MacroEconomySystem
{
    public const float MinConsumerGoodsRatio = 0.08f;
    public const float MaxConsumerGoodsRatio = 0.55f;
    public const float MaxExportOutputBonus = 0.35f;
    public const float MaxExportConstructionBonus = 0.30f;
    public const float MinOverheatingOutputPenalty = -0.25f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Execute(SimulationWorld world)
    {
        Span<CountryState> countries = world.Countries.AsSpan();
        int currentYear = world.CurrentYear;

        for (byte tag = 0; tag < CountryTag.ActiveCount; tag++)
        {
            ref CountryState country = ref countries[tag];
            CountryFactorySummary summary = ConstructionSystem.CalculateFactorySummary(world, tag);

            if (summary.TotalFactories <= 0)
            {
                continue;
            }

            bool atWar = world.IsAtWar(tag);

            // 1. Command-Economy Military Overheating (SOV, PRK)
            if (tag == CountryTag.SOV || tag == CountryTag.PRK)
            {
                float milRatio = (float)summary.TotalMilitaryFactories / summary.TotalFactories;
                if (milRatio > 0.52f && !atWar)
                {
                    // Chronic defense burden increases consumer goods pressure in the late Cold War / 1990s
                    float pressure = currentYear >= 1990 ? 0.004f : 0.002f;
                    country.ConsumerGoodsRatio = System.Math.Clamp(
                        country.ConsumerGoodsRatio + pressure,
                        MinConsumerGoodsRatio,
                        0.40f);

                    if (currentYear >= 1991 && country.Stability < 0.65f)
                    {
                        country.FactoryOutputModifier = System.Math.Clamp(
                            country.FactoryOutputModifier - 0.005f,
                            MinOverheatingOutputPenalty,
                            MaxExportOutputBonus);
                    }
                }
            }
            // 2. East Asian Export Miracle Growth (JPN, ROK, TWN, PRC)
            else if (tag == CountryTag.JPN || tag == CountryTag.ROK || tag == CountryTag.TWN || tag == CountryTag.PRC)
            {
                if (!atWar && country.Stability >= 0.55f)
                {
                    // JPN 1985-1990 Bubble Boom vs 1992+ Asset Deflation plateau
                    float monthlyOutputGain = (tag == CountryTag.JPN && currentYear >= 1992)
                        ? 0.0005f
                        : 0.0025f;
                    float monthlyConstrGain = (tag == CountryTag.JPN && currentYear >= 1992)
                        ? 0.0002f
                        : 0.0020f;

                    country.FactoryOutputModifier = System.Math.Clamp(
                        country.FactoryOutputModifier + monthlyOutputGain,
                        MinOverheatingOutputPenalty,
                        MaxExportOutputBonus);
                    country.ConstructionSpeedModifier = System.Math.Clamp(
                        country.ConstructionSpeedModifier + monthlyConstrGain,
                        -0.20f,
                        MaxExportConstructionBonus);
                }
            }

            // Clamp ConsumerGoodsRatio, Stability, and WarSupport within valid simulation bounds
            country.ConsumerGoodsRatio = System.Math.Clamp(
                country.ConsumerGoodsRatio,
                MinConsumerGoodsRatio,
                MaxConsumerGoodsRatio);
            country.Stability = System.Math.Clamp(country.Stability, 0.0f, 1.0f);
            country.WarSupport = System.Math.Clamp(country.WarSupport, 0.0f, 1.0f);
        }
    }
}
