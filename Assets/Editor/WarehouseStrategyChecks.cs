using ARLogistics.Data;
using NUnit.Framework;

public sealed class WarehouseStrategyChecks
{
    private static WarehouseStrategyComparison Calculate(float width, float depth, float height,
        float weight = 10f, float load = 1000f)
    {
        var cargo = new BoxMeasurement(width, depth, height, 0, "test");
        Assert.IsTrue(WarehouseRecommendationCalculator.TryCalculate(500f, 3f,
            1.2f, 1f, load, cargo, weight, "test", out WarehouseRecommendation source));
        return WarehouseStrategyCalculator.Calculate(source);
    }

    [Test]
    public void A_UsesOriginalPlan_AndWholeWarehouseCapacity()
    {
        var result = Calculate(0.4f, 0.3f, 0.3f);
        Assert.AreEqual(StackOrientation.Original, result.A.Orientation);
        Assert.AreEqual(result.A.PerLayer * result.A.Layers, result.A.PerPallet);
        Assert.AreEqual((long)result.PalletCount * result.A.PerPallet, result.A.WarehouseCapacity);
        Assert.Greater(result.PalletCount, 0);
    }

    [Test]
    public void B_ChoosesHigherCapacityOrientation()
    {
        var result = Calculate(0.7f, 0.4f, 0.25f);
        Assert.AreEqual(StackOrientation.Rotated, result.B.Orientation);
        Assert.Greater(result.B.PerPallet, result.A.PerPallet);
        Assert.AreEqual(308, result.PalletCount);
        Assert.AreEqual((long)result.PalletCount * result.B.PerPallet, result.B.WarehouseCapacity);
    }

    [Test]
    public void C_DropsOneLayer_AndRespectsOneLayerFloor()
    {
        var result = Calculate(0.4f, 0.3f, 0.3f);
        Assert.AreEqual(result.B.Layers - 1, result.C.Layers);
        Assert.AreEqual(result.C.PerLayer * result.C.Layers, result.C.PerPallet);
        Assert.Less(result.C.WeightKg, result.B.WeightKg);
        var oneLayer = Calculate(0.4f, 0.3f, 1.3f);
        Assert.AreEqual(1, oneLayer.B.Layers);
        Assert.AreEqual(1, oneLayer.C.Layers);
        Assert.AreEqual(oneLayer.B.PerPallet, oneLayer.C.PerPallet);
    }

    [Test]
    public void UnavailablePlan_DoesNotInventCapacity()
    {
        var result = Calculate(0.4f, 0.3f, 0.3f, 100f, 50f);
        Assert.IsFalse(result.A.Available);
        Assert.IsFalse(result.B.Available);
        Assert.IsFalse(result.C.Available);
        Assert.AreEqual(0, result.B.WarehouseCapacity);
    }

    [Test]
    public void HeightLimit_DoesNotAllowAnOversizedLayer()
    {
        var result = Calculate(0.4f, 0.3f, 1.7f);
        Assert.IsFalse(result.A.Available);
        Assert.IsFalse(result.B.Available);
        Assert.IsFalse(result.C.Available);
    }
}
