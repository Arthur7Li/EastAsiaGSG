using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Core.Systems;

namespace EastAsiaGSG.Data.Map;

/// <summary>
/// Houses authentic human-readable string metadata (Province, State, Strategic Region, Template, and News Event names)
/// deserialized from JSON content datasets, completely isolated from EastAsiaGSG.Core unmanaged blittable memory.
/// </summary>
public sealed class MapMetadataRegistry
{
    public readonly string[] ProvinceNames = new string[SimulationWorld.MaxProvinces];
    public readonly string[] StateNames = new string[SimulationWorld.MaxStates];
    public readonly string[] StrategicRegionNames = new string[SimulationWorld.MaxStrategicRegions];
    public readonly string[] TemplateNames = new string[SimulationWorld.MaxDivisionTemplates];
    public readonly Dictionary<int, string> NewsEventTitles = new();

    public MapMetadataRegistry()
    {
        InitializeDefaults();
    }

    public void InitializeDefaults()
    {
        for (int i = 0; i < ProvinceNames.Length; i++)
        {
            ProvinceNames[i] = $"Province #{i}";
        }

        for (int i = 0; i < StateNames.Length; i++)
        {
            StateNames[i] = $"State #{i}";
        }

        for (int i = 0; i < TemplateNames.Length; i++)
        {
            TemplateNames[i] = $"Template #{i}";
        }

        // Default 18 authentic strategic region designations
        StrategicRegionNames[0] = "Yellow Sea & Bohai Gulf";
        StrategicRegionNames[1] = "Sea of Japan / East Sea";
        StrategicRegionNames[2] = "East China Sea";
        StrategicRegionNames[3] = "Taiwan Strait";
        StrategicRegionNames[4] = "South China Sea";
        StrategicRegionNames[5] = "Philippine Sea & Mariana Trench";
        StrategicRegionNames[6] = "Sea of Okhotsk & Kuril Basin";
        StrategicRegionNames[7] = "Korean Peninsula";
        StrategicRegionNames[8] = "Japanese Home Islands";
        StrategicRegionNames[9] = "North China Plain";
        StrategicRegionNames[10] = "Northeast China (Manchuria)";
        StrategicRegionNames[11] = "South China & Pearl River Delta";
        StrategicRegionNames[12] = "Taiwan & Ryukyu Arc";
        StrategicRegionNames[13] = "Russian Far East & Primorye";
        StrategicRegionNames[14] = "Indochina Peninsula";
        StrategicRegionNames[15] = "Philippine Archipelago";
        StrategicRegionNames[16] = "Mongolian Plateau";
        StrategicRegionNames[17] = "Western Pacific Sea Lanes";

        // Default historical news flashpoint headlines
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.PRC), "Fall of Beijing: People's Republic of China Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.USA), "PACOM Evacuated: United States Forced from the Western Pacific!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.SOV), "Far Eastern Collapse: Soviet Forces Surrender!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.JPN), "Tokyo Surrenders: Japan Sues for Peace!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.ROK), "Fall of Seoul: Republic of Korea Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.PRK), "Pyongyang Overrun: North Korean Regime Collapses!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.TWN), "Taipei Subdued: Republic of China (Taiwan) Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.VNM), "Fall of Hanoi: Socialist Republic of Vietnam Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.PHL), "Manila Accord: Republic of the Philippines Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.MNG), "Ulaanbaatar Captured: Mongolian People's Republic Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.THA), "Bangkok Surrenders: Kingdom of Thailand Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.LAO), "Vientiane Falls: Lao People's Democratic Republic Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.KHM), "Phnom Penh Falls: People's Republic of Kampuchea Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.MMR), "Yangon Secured: Socialist Republic of Myanmar Capitulates!");
        RegisterNewsEventTitle(CapitulationSystem.GetCapitulationNewsHash(CountryTag.MYS), "Kuala Lumpur Falls: Federation of Malaysia Capitulates!");

        RegisterNewsEventTitle("NEWS_JPN_BREAKS_1PCT_CAP".GetHashCode(), "Tokyo Policy Shift: Japan Lifts 1% Defense Budget Ceiling");
        RegisterNewsEventTitle("NEWS_PRC_863_PROGRAM".GetHashCode(), "Beijing Launches High-Tech State Development (Project 863)");
        RegisterNewsEventTitle("NEWS_PRC_MILLION_TROOP_CUT".GetHashCode(), "PLA Modernization: Deng Xiaoping Announces One-Million Troop Demobilization");
        RegisterNewsEventTitle("NEWS_USA_TRA_PACKAGE".GetHashCode(), "Washington Passes Taiwan Relations Act Defense Package");
        RegisterNewsEventTitle("NEWS_1988_SEOUL_OLYMPICS".GetHashCode(), "World Gathers in Seoul: XXIV Summer Olympic Games Inaugurated");
        RegisterNewsEventTitle("NEWS_SOV_VLADIVOSTOK_SPEECH".GetHashCode(), "Gorbachev's Vladivostok Initiative Proposes Asia-Pacific Security Architecture");
        RegisterNewsEventTitle("NEWS_TWN_TSMC_FOUNDED".GetHashCode(), "Silicon Shield: Taiwan Semiconductor Manufacturing Company (TSMC) Founded");
        RegisterNewsEventTitle("NEWS_TWN_MARTIAL_LAW_LIFTED".GetHashCode(), "Democratization Era: Taiwan Lifts 38-Year Martial Law");
        RegisterNewsEventTitle("NEWS_MITI_VLSI_BREAKTHROUGH".GetHashCode(), "MITI VLSI Project Breakthrough: Japanese Microelectronics Lead the World");
    }

    public static int GetCapitulationNewsHash(byte countryTag) => CapitulationSystem.GetCapitulationNewsHash(countryTag);

    public string GetProvinceName(ushort provinceId)
    {
        if (provinceId < ProvinceNames.Length && !string.IsNullOrWhiteSpace(ProvinceNames[provinceId]))
        {
            return ProvinceNames[provinceId];
        }
        return $"Province #{provinceId}";
    }

    public string GetStateName(ushort stateId)
    {
        if (stateId < StateNames.Length && !string.IsNullOrWhiteSpace(StateNames[stateId]))
        {
            return StateNames[stateId];
        }
        return $"State #{stateId}";
    }

    public string GetRegionName(byte regionId)
    {
        if (regionId < StrategicRegionNames.Length && !string.IsNullOrWhiteSpace(StrategicRegionNames[regionId]))
        {
            return StrategicRegionNames[regionId];
        }
        return $"Region #{regionId}";
    }

    public string GetTemplateName(ushort templateId)
    {
        if (templateId < TemplateNames.Length && !string.IsNullOrWhiteSpace(TemplateNames[templateId]))
        {
            return TemplateNames[templateId];
        }
        return $"Division Template #{templateId}";
    }

    public string GetNewsEventTitle(int hash)
    {
        if (NewsEventTitles.TryGetValue(hash, out string? title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }
        return $"Global Flashpoint #{hash:X8}";
    }

    public void RegisterProvinceName(ushort provinceId, string name)
    {
        if (provinceId < ProvinceNames.Length && !string.IsNullOrWhiteSpace(name))
        {
            ProvinceNames[provinceId] = name;
        }
    }

    public void RegisterStateName(ushort stateId, string name)
    {
        if (stateId < StateNames.Length && !string.IsNullOrWhiteSpace(name))
        {
            StateNames[stateId] = name;
        }
    }

    public void RegisterTemplateName(ushort templateId, string name)
    {
        if (templateId < TemplateNames.Length && !string.IsNullOrWhiteSpace(name))
        {
            TemplateNames[templateId] = name;
        }
    }

    public void RegisterNewsEventTitle(int hash, string title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            NewsEventTitles[hash] = title;
        }
    }
}
