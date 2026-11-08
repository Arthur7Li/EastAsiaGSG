using System.Runtime.CompilerServices;
using Arch.Core;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.SimTests;

public sealed class ProvinceSpatialIndexTests
{
    [Fact]
    public void ProvinceSpatialIndex_SinglePassRebuild_AccuratelyIndexesDivisions()
    {
        using SimulationWorld world = new();

        Entity e1 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 101,
            countryTag: CountryTag.ROK,
            startingProvinceId: 5,
            softAttack: 50.0f,
            defense: 60.0f));

        Entity e2 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 102,
            countryTag: CountryTag.ROK,
            startingProvinceId: 5,
            softAttack: 40.0f,
            defense: 55.0f));

        Entity e3 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 201,
            countryTag: CountryTag.PRK,
            startingProvinceId: 12,
            softAttack: 30.0f,
            defense: 45.0f));

        Entity e4 = world.SpawnDivision(DivisionComponent.Create(
            divisionId: 301,
            countryTag: CountryTag.USA,
            startingProvinceId: 100));

        world.SpatialIndex.Rebuild(world);

        Assert.Equal(4, world.SpatialIndex.TotalItemCount);

        // Province 5 should contain e1 and e2
        Assert.Equal(2, world.SpatialIndex.GetDivisionCountInProvince(5));
        ReadOnlySpan<SpatialDivisionItem> prov5 = world.SpatialIndex.GetDivisionsInProvince(5);
        Assert.Equal(2, prov5.Length);
        Assert.True(prov5[0].DivisionId == 101 || prov5[0].DivisionId == 102);
        Assert.True(prov5[1].DivisionId == 101 || prov5[1].DivisionId == 102);
        Assert.NotEqual(prov5[0].DivisionId, prov5[1].DivisionId);

        // Province 12 should contain e3
        Assert.Equal(1, world.SpatialIndex.GetDivisionCountInProvince(12));
        ReadOnlySpan<SpatialDivisionItem> prov12 = world.SpatialIndex.GetDivisionsInProvince(12);
        Assert.Equal(1, prov12.Length);
        Assert.Equal(201, prov12[0].DivisionId);
        Assert.Equal(CountryTag.PRK, prov12[0].CountryTag);
        Assert.Equal(30.0f, prov12[0].SoftAttack);

        // Province 100 should contain e4
        Assert.Equal(1, world.SpatialIndex.GetDivisionCountInProvince(100));
        ReadOnlySpan<SpatialDivisionItem> prov100 = world.SpatialIndex.GetDivisionsInProvince(100);
        Assert.Equal(1, prov100.Length);
        Assert.Equal(301, prov100[0].DivisionId);

        // Province 0 (empty)
        Assert.Equal(0, world.SpatialIndex.GetDivisionCountInProvince(0));
        Assert.True(world.SpatialIndex.GetDivisionsInProvince(0).IsEmpty);
    }

    [Fact]
    public void ProvinceSpatialIndex_EmptyAndOutOfBoundsProvinces_ReturnsEmptySpan()
    {
        using SimulationWorld world = new();
        world.SpatialIndex.Rebuild(world);

        Assert.Equal(0, world.SpatialIndex.TotalItemCount);
        Assert.True(world.SpatialIndex.GetDivisionsInProvince(0).IsEmpty);
        Assert.True(world.SpatialIndex.GetDivisionsInProvince(SimulationWorld.MaxProvinces).IsEmpty);
        Assert.True(world.SpatialIndex.GetDivisionsInProvince(ushort.MaxValue).IsEmpty);
        Assert.Equal(0, world.SpatialIndex.GetDivisionCountInProvince(SimulationWorld.MaxProvinces));
    }

    [Fact]
    public void ProvinceSpatialIndex_ShatteredDivisions_AreExcludedFromIndex()
    {
        using SimulationWorld world = new();

        DivisionComponent active = DivisionComponent.Create(1, CountryTag.ROK, startingProvinceId: 10);
        DivisionComponent shattered = DivisionComponent.Create(2, CountryTag.ROK, startingProvinceId: 10);
        shattered.State = DivisionState.Shattered;

        world.SpawnDivision(active);
        world.SpawnDivision(shattered);

        world.SpatialIndex.Rebuild(world);

        Assert.Equal(1, world.SpatialIndex.TotalItemCount);
        Assert.Equal(1, world.SpatialIndex.GetDivisionCountInProvince(10));
        ReadOnlySpan<SpatialDivisionItem> items = world.SpatialIndex.GetDivisionsInProvince(10);
        Assert.Equal(1, items.Length);
        Assert.Equal(1, items[0].DivisionId);
    }

    [Fact]
    public void ProvinceSpatialIndex_ZeroHeapAllocations_InRebuildAndQuery()
    {
        using SimulationWorld world = new();

        for (int i = 0; i < 16; i++)
        {
            world.SpawnDivision(DivisionComponent.Create(
                divisionId: i + 1,
                countryTag: (byte)(i % CountryTag.ActiveCount),
                startingProvinceId: (ushort)(i * 5)));
        }

        // Warm up JIT
        for (int w = 0; w < 128; w++)
        {
            world.SpatialIndex.Rebuild(world);
            for (ushort p = 0; p < 80; p++)
            {
                _ = world.SpatialIndex.GetDivisionsInProvince(p);
                _ = world.SpatialIndex.GetDivisionCountInProvince(p);
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int iter = 0; iter < 1000; iter++)
        {
            world.SpatialIndex.Rebuild(world);
            for (ushort p = 0; p < 80; p++)
            {
                _ = world.SpatialIndex.GetDivisionsInProvince(p);
                _ = world.SpatialIndex.GetDivisionCountInProvince(p);
            }
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, afterAlloc - beforeAlloc);
    }
}
