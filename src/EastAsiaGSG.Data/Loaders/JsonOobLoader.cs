using System.Text.Json;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;
using EastAsiaGSG.Data.Map;
using EastAsiaGSG.Data.Schemas;

namespace EastAsiaGSG.Data.Loaders;

/// <summary>
/// Metadata returned after loading a nation's 1985 Order of Battle into SimulationWorld.
/// </summary>
public sealed class LoadedOob
{
    public required byte CountryTag { get; init; }
    public required string CountryCode { get; init; }
    public required IReadOnlyList<DivisionTemplateDto> Templates { get; init; }
    public required IReadOnlyList<StartingDivisionDto> StartingDivisions { get; init; }
    public required IReadOnlyList<int> SpawnedDivisionIds { get; init; }
}

/// <summary>
/// Deserializes, validates, and registers 1985 Order of Battle (OOB) JSON files (/data/oob_1985/{TAG}_oob.json)
/// into SimulationWorld (populating DivisionTemplateRecord entries and spawning StartingDivisions).
/// </summary>
public static class JsonOobLoader
{
    public const int MaxBattalionsPerTemplate = 6;
    public const int MaxSupportCompaniesPerTemplate = 4;

    public static LoadedOob LoadOob(
        SimulationWorld world,
        string jsonFilePath,
        byte? overrideCountryTag = null,
        MapMetadataRegistry? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);

        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"OOB JSON file not found at '{jsonFilePath}'.", jsonFilePath);
        }

        using FileStream stream = File.OpenRead(jsonFilePath);
        return LoadOob(world, stream, overrideCountryTag, metadata);
    }

    public static LoadedOob LoadOob(
        SimulationWorld world,
        Stream stream,
        byte? overrideCountryTag = null,
        MapMetadataRegistry? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stream);

        OobDto? dto = JsonSerializer.Deserialize(stream, MapJsonContext.Default.OobDto);
        return CompileAndRegister(world, dto, overrideCountryTag, metadata);
    }

    public static LoadedOob LoadOob(
        SimulationWorld world,
        ReadOnlySpan<byte> utf8Json,
        byte? overrideCountryTag = null,
        MapMetadataRegistry? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        OobDto? dto = JsonSerializer.Deserialize(utf8Json, MapJsonContext.Default.OobDto);
        return CompileAndRegister(world, dto, overrideCountryTag, metadata);
    }

    public static LoadedOob CompileAndRegister(
        SimulationWorld world,
        OobDto? dto,
        byte? overrideCountryTag = null,
        MapMetadataRegistry? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (dto == null)
        {
            throw new MapValidationException("OOB JSON could not be deserialized.");
        }

        byte countryTag;
        if (overrideCountryTag.HasValue)
        {
            if (overrideCountryTag.Value >= SimulationWorld.MaxCountries)
            {
                throw new MapValidationException($"Override CountryTag {overrideCountryTag.Value} exceeds MaxCountries.");
            }
            countryTag = overrideCountryTag.Value;
        }
        else if (!CountryTag.TryParse(dto.CountryTag, out countryTag))
        {
            throw new MapValidationException($"OOB has invalid or unknown countryTag '{dto.CountryTag}'.");
        }

        if (dto.Templates == null || dto.Templates.Count == 0)
        {
            throw new MapValidationException($"OOB for '{dto.CountryTag}' contains zero division templates.");
        }

        Span<EquipmentType> battalionSpan = stackalloc EquipmentType[MaxBattalionsPerTemplate];
        Span<EquipmentType> supportSpan = stackalloc EquipmentType[MaxSupportCompaniesPerTemplate];

        var registeredTemplateIds = new HashSet<ushort>(dto.Templates.Count);

        // 1. Validate & Register Division Templates
        foreach (DivisionTemplateDto tplDto in dto.Templates)
        {
            if (tplDto.TemplateId == 0 || tplDto.TemplateId >= SimulationWorld.MaxDivisionTemplates)
            {
                throw new MapValidationException(
                    $"Division template '{tplDto.Name}' in '{dto.CountryTag}' has invalid templateId {tplDto.TemplateId} (must be 1..{SimulationWorld.MaxDivisionTemplates - 1}).");
            }

            if (!registeredTemplateIds.Add(tplDto.TemplateId))
            {
                throw new MapValidationException(
                    $"Duplicate templateId {tplDto.TemplateId} in OOB for '{dto.CountryTag}'.");
            }

            if (tplDto.CombatWidth == 0 || tplDto.CombatWidth > 80)
            {
                throw new MapValidationException(
                    $"Division template '{tplDto.Name}' has invalid combatWidth {tplDto.CombatWidth}.");
            }

            if (tplDto.Battalions == null || tplDto.Battalions.Count == 0 || tplDto.Battalions.Count > MaxBattalionsPerTemplate)
            {
                throw new MapValidationException(
                    $"Division template '{tplDto.Name}' must have 1..{MaxBattalionsPerTemplate} combat battalions.");
            }

            int bCount = 0;
            foreach (string bStr in tplDto.Battalions)
            {
                if (!Enum.TryParse<EquipmentType>(bStr, ignoreCase: true, out EquipmentType bEq))
                {
                    throw new MapValidationException(
                        $"Division template '{tplDto.Name}' references unknown battalion EquipmentType '{bStr}'.");
                }
                battalionSpan[bCount++] = bEq;
            }

            int sCount = 0;
            if (tplDto.SupportCompanies != null)
            {
                if (tplDto.SupportCompanies.Count > MaxSupportCompaniesPerTemplate)
                {
                    throw new MapValidationException(
                        $"Division template '{tplDto.Name}' exceeds max support companies ({MaxSupportCompaniesPerTemplate}).");
                }

                foreach (string sStr in tplDto.SupportCompanies)
                {
                    if (!Enum.TryParse<EquipmentType>(sStr, ignoreCase: true, out EquipmentType sEq))
                    {
                        throw new MapValidationException(
                            $"Division template '{tplDto.Name}' references unknown support company EquipmentType '{sStr}'.");
                    }
                    supportSpan[sCount++] = sEq;
                }
            }

            DivisionTemplateRecord record = DivisionTemplateRecord.FromBattalions(
                tplDto.TemplateId,
                tplDto.CombatWidth,
                battalionSpan[..bCount],
                supportSpan[..sCount]);

            world.RegisterDivisionTemplate(in record);
            metadata?.RegisterTemplateName(tplDto.TemplateId, tplDto.Name);
        }

        // 2. Validate & Spawn Starting Divisions
        var spawnedIds = new List<int>(dto.StartingDivisions?.Count ?? 0);
        if (dto.StartingDivisions != null)
        {
            foreach (StartingDivisionDto divDto in dto.StartingDivisions)
            {
                if (divDto.TemplateId == 0 ||
                    divDto.TemplateId >= SimulationWorld.MaxDivisionTemplates ||
                    world.Templates[divDto.TemplateId].IsConfigured == 0)
                {
                    throw new MapValidationException(
                        $"Starting division '{divDto.Name}' in '{dto.CountryTag}' references unregistered templateId {divDto.TemplateId}.");
                }

                if (divDto.StartingProvinceId >= SimulationWorld.MaxProvinces)
                {
                    throw new MapValidationException(
                        $"Starting division '{divDto.Name}' has out-of-bounds startingProvinceId {divDto.StartingProvinceId}.");
                }

                if (divDto.StartingStrength is <= 0.0f or > 1.0f)
                {
                    throw new MapValidationException(
                        $"Starting division '{divDto.Name}' has invalid startingStrength {divDto.StartingStrength} (must be in (0, 1]).");
                }

                if (divDto.StartingExperience is < 0.0f or > 1.0f)
                {
                    throw new MapValidationException(
                        $"Starting division '{divDto.Name}' has invalid startingExperience {divDto.StartingExperience} (must be in [0, 1]).");
                }

                ref readonly DivisionTemplateRecord tpl = ref world.Templates[divDto.TemplateId];
                TechGeneration techGen = world.GetUnlockedTechGeneration(countryTag, EquipmentType.InfantryKit);
                tpl.ComputeCombatStats(
                    techGen,
                    out float softAttack,
                    out float hardAttack,
                    out float defense,
                    out float breakthrough,
                    out float armor,
                    out float piercing,
                    out float hardness,
                    out float speed);

                ushort ifvReq = tpl.GetRequired(EquipmentType.IFV);
                ushort mbtReq = tpl.GetRequired(EquipmentType.MBT);
                ushort heliReq = tpl.GetRequired(EquipmentType.AttackHeli);

                int divId = world.AllocateNextDivisionId();
                DivisionComponent div = DivisionComponent.Create(
                    divisionId: divId,
                    countryTag: countryTag,
                    startingProvinceId: divDto.StartingProvinceId,
                    templateId: divDto.TemplateId,
                    combatWidth: tpl.CombatWidth,
                    softAttack: softAttack,
                    hardAttack: hardAttack,
                    defense: defense,
                    breakthrough: breakthrough,
                    armor: armor,
                    piercing: piercing,
                    hardness: hardness,
                    maxOrganization: 60.0f,
                    startingStrength: divDto.StartingStrength,
                    startingExperience: divDto.StartingExperience,
                    speed: speed,
                    pgmAuthorized: divDto.PgmAuthorized);

                world.SpawnDivision(in div);
                spawnedIds.Add(divId);
            }
        }

        return new LoadedOob
        {
            CountryTag = countryTag,
            CountryCode = CountryTag.GetCode(countryTag),
            Templates = dto.Templates,
            StartingDivisions = dto.StartingDivisions ?? [],
            SpawnedDivisionIds = spawnedIds
        };
    }
}
