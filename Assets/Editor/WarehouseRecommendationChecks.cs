using System;
using ARLogistics.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class WarehouseRecommendationChecks
{
    private static readonly BoxMeasurement Cargo = new BoxMeasurement(
        0.4f, 0.3f, 0.25f, 0, "대표 규격");

    [Test]
    public void LargerWarehouseFitsMorePalletsAndCargo()
    {
        WarehouseRecommendation small = Calculate(100f, 1000f);
        WarehouseRecommendation large = Calculate(400f, 1000f);
        Assert.Greater(large.PalletCount, small.PalletCount);
        Assert.Greater(large.TotalCargoCapacity, small.TotalCargoCapacity);
    }

    [Test]
    public void PalletLoadLimitReducesCapacity()
    {
        WarehouseRecommendation normal = Calculate(100f, 1000f);
        WarehouseRecommendation restricted = Calculate(100f, 10f);
        Assert.Less(restricted.TotalCargoCapacity, normal.TotalCargoCapacity);
    }

    [Test]
    public void LargerCargoFitsFewerUnits()
    {
        WarehouseRecommendation baseline = Calculate(100f, 1000f);
        var larger = new BoxMeasurement(0.8f, 0.6f, 0.5f, 0, "큰 규격");
        bool success = WarehouseRecommendationCalculator.TryCalculate(
            100f, 3f, 1.2f, 1f, 1000f, larger, 5f,
            "큰 화물", out WarehouseRecommendation result);
        Assert.IsTrue(success);
        Assert.Less(result.TotalCargoCapacity, baseline.TotalCargoCapacity);
    }

    [Test]
    public void ZonesConserveUsableFloorArea()
    {
        WarehouseRecommendation result = Calculate(100f, 1000f);
        Assert.AreEqual(3, result.Zones.Count);
        Assert.AreEqual(20f, result.ExcludedAreaM2, 0.01f);
        Assert.AreEqual(result.UsableAreaM2,
            result.OccupiedAreaM2 + result.FreeAreaM2, 0.01f);
        Assert.AreEqual(result.OccupiedAreaM2 / result.WarehouseAreaM2 * 100f,
            result.AreaUtilizationPercent, 0.01f);
        Assert.AreEqual(result.FreeAreaM2 / result.WarehouseAreaM2 * 100f,
            result.RemainingAreaPercent, 0.01f);
        Assert.AreEqual(result.TotalCargoCapacity,
            result.Zones[0].CargoCapacity + result.Zones[1].CargoCapacity +
            result.Zones[2].CargoCapacity);
    }

    [MenuItem("WiseStack/Run Recommendation Checks")]
    public static void RunChecks()
    {
        var checks = new WarehouseRecommendationChecks();
        checks.LargerWarehouseFitsMorePalletsAndCargo();
        checks.PalletLoadLimitReducesCapacity();
        checks.LargerCargoFitsFewerUnits();
        checks.ZonesConserveUsableFloorArea();
        Debug.Log("[WarehouseRecommendationChecks] 4 edit-mode checks passed.");
    }

    private static WarehouseRecommendation Calculate(float areaM2, float maxLoadKg)
    {
        bool success = WarehouseRecommendationCalculator.TryCalculate(
            areaM2, 3f, 1.2f, 1f, maxLoadKg,
            Cargo, 5f, "대표 화물", out WarehouseRecommendation result);
        if (!success) throw new InvalidOperationException("Recommendation calculation failed.");
        return result;
    }
}
