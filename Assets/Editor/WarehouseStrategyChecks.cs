using ARLogistics.Data;
using NUnit.Framework;

public sealed class WarehouseStrategyChecks
{
    private static (WarehouseRecommendation source, WarehouseStrategyComparison options) Fixture(
        float width = .35f, float depth = .48f, float height = .315f, float load = 1000, float ceiling = 3)
    {
        Assert.IsTrue(WarehouseRecommendationCalculator.TryCalculate(500, ceiling, 1.2f, 1, load,
            new BoxMeasurement(width, depth, height, 6, "review"), 3.008f, "검증 화물", out var source));
        return (source, WarehouseStrategyCalculator.Calculate(source));
    }

    [Test]
    public void Evaluation_EquivalentAB_SelectsAAndExplainsTie()
    {
        var f = Fixture();
        var d = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        Assert.AreEqual(0, d.Index);
        Assert.IsTrue(d.Evaluations[1].EquivalentToWinner);
        StringAssert.Contains("동일", d.Reason);
        Assert.AreEqual(30, f.options.A.PerPallet);
        Assert.AreEqual(24, f.options.C.PerPallet);
        Assert.AreEqual(.315f, f.options.A.StackHeightM - f.options.C.StackHeightM, .001);
        Assert.AreEqual(9240L, f.options.A.WarehouseCapacity);
    }

    [Test]
    public void Evaluation_RotationWins_WithReproducibleMetrics()
    {
        var f = Fixture(.7f, .4f, .25f);
        var d = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        Assert.AreEqual(1, d.Index);
        var b = f.options.B;
        Assert.AreEqual(18, b.PerPallet);
        Assert.AreEqual(70, b.FloorUtilizationPercent, .001);
        Assert.AreEqual(18 * .7 * .4 * .25 / (1.2 * 1.65) * 100, b.SpaceUtilizationPercent, .001);
        Assert.AreEqual(.15, b.HeightReserveM, .001);
        var again = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        Assert.AreEqual(d.Index, again.Index);
    }

    [Test]
    public void Evaluation_RejectsLoadAndHeightBeforeScoring()
    {
        var f = Fixture();
        f.options.A.WeightKg = 1001;
        f.options.B.StackHeightM = 1.81f;
        var d = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        Assert.AreEqual(2, d.Index);
        Assert.IsFalse(d.Evaluations[0].Eligible);
        Assert.IsFalse(d.Evaluations[1].Eligible);
        StringAssert.Contains("하중", d.Evaluations[0].Exclusion);
    }

    [Test]
    public void Evaluation_RejectsAllInvalidAndNonFiniteValues()
    {
        var f = Fixture();
        f.options.A.WeightKg = float.NaN;
        f.options.B.FloorUtilizationPercent = float.PositiveInfinity;
        f.options.C.StackHeightM = -1;
        Assert.AreEqual(-1, WarehouseStrategyEvaluator.Evaluate(f.source, f.options).Index);
        f = Fixture(load: 0);
        Assert.AreEqual(-1, WarehouseStrategyEvaluator.Evaluate(f.source, f.options).Index);
        f = Fixture(ceiling: .5f);
        Assert.AreEqual(-1, WarehouseStrategyEvaluator.Evaluate(f.source, f.options).Index);
    }

    [Test]
    public void Evaluation_UsesCeilingAndRejectsPlanMismatch()
    {
        var f = Fixture(ceiling: 1.5f);
        f.options.A.StackHeightM = 1.3f;
        f.options.B.Plan.TotalWeight += 1;
        var d = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        Assert.IsFalse(d.Evaluations[0].Eligible);
        Assert.IsFalse(d.Evaluations[1].Eligible);
    }

    [Test]
    public void Evaluation_OneLayerStrategiesTie_AndArPlansMatch()
    {
        var f = Fixture(.4f, .4f, 1.3f);
        var d = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        Assert.AreEqual(0, d.Index);
        Assert.IsTrue(d.Evaluations[2].EquivalentToWinner);
        for (int i = 0; i < 3; i++)
        {
            var s = f.options.Get(i);
            Assert.AreEqual(s.PerPallet, s.Plan.Total);
            Assert.AreEqual(s.Layers, s.Plan.Layers);
            Assert.AreEqual(s.StackHeightM, s.Plan.StackHeight);
            Assert.AreEqual(s.WeightKg, s.Plan.TotalWeight);
            Assert.AreEqual((long)f.options.PalletCount * s.PerPallet, s.WarehouseCapacity);
        }
    }

    [Test]
    public void Evaluation_LowerHeightCannotOutweighCapacity()
    {
        var f = Fixture(.601f, .601f, .8f);
        var d = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        Assert.AreEqual(0, d.Index);
        Assert.Less(f.options.C.PerPallet, f.options.B.PerPallet);
        Assert.Less(f.options.C.StackHeightM, f.options.B.StackHeightM);
    }

    [Test]
    public void Evaluation_NoPalletFootprintHasNoRecommendation()
    {
        var f = Fixture();
        f.options.PalletCount = 0;
        Assert.AreEqual(-1, WarehouseStrategyEvaluator.Evaluate(f.source, f.options).Index);
    }

    private static WarehouseStrategyComparison Calculate(float width, float depth, float height,
        float weight = 10f, float load = 1000f)
    {
        var cargo = new BoxMeasurement(width, depth, height, 0, "test");
        Assert.IsTrue(WarehouseRecommendationCalculator.TryCalculate(500f, 3f,
            1.2f, 1f, load, cargo, weight, "test", out WarehouseRecommendation source));
        return WarehouseStrategyCalculator.Calculate(source);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidDimensions_AreRejected(float dimension)
    {
        Assert.IsFalse(WarehouseRecommendationCalculator.TryCalculate(500, 3, 1.2f, 1, 1000,
            new BoxMeasurement(dimension, .4f, .3f, 0, "test"), 10, "test", out _));
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidWeight_AreRejected(float weight)
    {
        Assert.IsFalse(WarehouseRecommendationCalculator.TryCalculate(500, 3, 1.2f, 1, 1000,
            new BoxMeasurement(.4f, .4f, .3f, 0, "test"), weight, "test", out _));
    }

    [Test]
    public void MissingWeight_IsConditionalAndNeverVerified()
    {
        Assert.IsTrue(WarehouseRecommendationCalculator.TryCalculate(500, 3, 1.2f, 1, 1000,
            new BoxMeasurement(.4f, .4f, .3f, 0, "test"), 0, "test", out var source));
        var options = WarehouseStrategyCalculator.Calculate(source);
        var decision = WarehouseStrategyEvaluator.Evaluate(source, options);
        Assert.IsTrue(decision.HasRecommendation);
        Assert.IsTrue(decision.RequiresLoadConfirmation);
        StringAssert.Contains("하중 확인", decision.Reason);
        for (int i = 0; i < 3; i++)
        {
            Assert.IsFalse(decision.Evaluations[i].LoadVerified);
            Assert.IsFalse(options.Get(i).Plan.LoadVerified);
            StringAssert.Contains("하중 확인", options.Get(i).Message);
        }
        Assert.AreEqual(0, ProductSpecTable.RecommendationWeightKg(-1));
        source.PalletMaxLoadKg = 0;
        Assert.IsFalse(WarehouseStrategyEvaluator.Evaluate(source,
            WarehouseStrategyCalculator.Calculate(source)).HasRecommendation);
    }

    [Test]
    public void OversizedFootprint_AllStrategiesExcluded()
    {
        var f = Fixture(1.3f, 1.3f);
        Assert.IsFalse(WarehouseStrategyEvaluator.Evaluate(f.source, f.options).HasRecommendation);
    }

    [Test]
    public void Comparator_UsesFloorThenHeightThenDirection()
    {
        var a = new WarehouseStrategy { PerPallet = 12, FloorUtilizationPercent = 80, StackHeightM = 1.5f, Orientation = StackOrientation.Rotated };
        var b = new WarehouseStrategy { PerPallet = 12, FloorUtilizationPercent = 70, StackHeightM = 1, Orientation = StackOrientation.Original };
        Assert.IsTrue(WarehouseStrategyEvaluator.Better(a, b));
        b.FloorUtilizationPercent = 80;
        Assert.IsTrue(WarehouseStrategyEvaluator.Better(b, a));
        a.StackHeightM = 1;
        Assert.IsTrue(WarehouseStrategyEvaluator.Better(b, a));
        a.Orientation = StackOrientation.Original;
        Assert.IsFalse(WarehouseStrategyEvaluator.Better(a, b));
        Assert.IsFalse(WarehouseStrategyEvaluator.Better(b, a));
    }

    [Test]
    public void TinyPositiveWeight_StillEnforcesLoad()
    {
        var p = StackingCalculator.Calculate(.4f, .4f, .3f, .001f, 1.2f, 1, 3, .006f);
        Assert.IsTrue(p.LoadVerified);
        Assert.AreEqual(1, p.Layers);
        Assert.LessOrEqual(p.TotalWeight, .006001f);
    }

    [Test]
    public void NonStandardPallet_IsAllowedAndHeightEnvelopeMustBePositive()
    {
        var cargo = new BoxMeasurement(.4f, .3f, .3f, 0, "test");
        Assert.IsTrue(WarehouseRecommendationCalculator.TryCalculate(500, 3, 1.17f, .93f, 1000, cargo, 10, "test", out var source));
        Assert.IsTrue(WarehouseStrategyEvaluator.Evaluate(source, WarehouseStrategyCalculator.Calculate(source)).HasRecommendation);
        source.CeilingHeightM = .4f;
        var options = WarehouseStrategyCalculator.Calculate(source);
        Assert.IsFalse(WarehouseStrategyEvaluator.Evaluate(source, options).HasRecommendation);
        Assert.AreEqual(0, options.A.SpaceUtilizationPercent);
    }

    [Test]
    public void ArHandoff_ConsumedOnceAndAllPlanMetricsMatch()
    {
        var f = Fixture(.7f, .4f, .25f);
        var d = WarehouseStrategyEvaluator.Evaluate(f.source, f.options);
        WarehouseStrategySession.OpenInAr(d.Index);
        Assert.AreEqual(d.Index, WarehouseStrategySession.ConsumeArStrategy());
        Assert.AreEqual(-1, WarehouseStrategySession.ConsumeArStrategy());
        for (int i = 0; i < 3; i++)
        {
            var s = f.options.Get(i);
            Assert.AreEqual(s.PerPallet, s.Plan.Total);
            Assert.AreEqual(s.Layers, s.Plan.Layers);
            Assert.AreEqual(s.StackHeightM, s.Plan.StackHeight);
            Assert.AreEqual(s.WeightKg, s.Plan.TotalWeight);
            Assert.AreEqual(s.FloorUtilizationPercent, s.Plan.Utilization);
            Assert.AreEqual(s.Orientation == StackOrientation.Rotated ? .4f : .7f, s.Plan.BoxWidth);
        }
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
