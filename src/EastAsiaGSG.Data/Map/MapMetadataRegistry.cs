using EastAsiaGSG.Core.ECS;

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
    }

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
