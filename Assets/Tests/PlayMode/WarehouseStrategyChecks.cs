using System;
using System.Reflection;
using NUnit.Framework;

public sealed class WarehouseStrategyChecks
{
    private static Type DataType(string name) =>
        Type.GetType("ARLogistics.Data." + name + ", Assembly-CSharp", true);

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance).GetValue(target);

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance).SetValue(target, value);

    private static (object source, object options, object decision) Calculate(
        float width, float depth, float maxLoad = 1000f)
    {
        object source = Activator.CreateInstance(DataType("WarehouseRecommendation"));
        object cargo = Activator.CreateInstance(DataType("BoxMeasurement"),
            width, depth, 0.2f, 0, "Test");
        Set(source, "WarehouseAreaM2", 100f);
        Set(source, "CeilingHeightM", 3f);
        Set(source, "AssumedSideM", 10f);
        Set(source, "UsableAreaM2", 80f);
        Set(source, "PalletWidthM", 1.2f);
        Set(source, "PalletLengthM", 1f);
        Set(source, "PalletMaxLoadKg", maxLoad);
        Set(source, "Cargo", cargo);
        Set(source, "AssumedCargoWeightKg", 10f);

        object options = DataType("WarehouseStrategyCalculator")
            .GetMethod("Calculate").Invoke(null, new[] { source });
        object decision = DataType("WarehouseStrategyEvaluator")
            .GetMethod("Evaluate").Invoke(null, new[] { source, options });
        return (source, options, decision);
    }

    [Test]
    public void EqualDirections_SelectsStandardA()
    {
        var result = Calculate(0.4f, 0.4f);
        Assert.AreEqual(0, Field(result.decision, "Index"));
        Assert.AreEqual(Field(Field(result.options, "A"), "PerPallet"),
            Field(Field(result.options, "B"), "PerPallet"));
    }

    [Test]
    public void MoreBoxesAfterRotation_SelectsB()
    {
        var result = Calculate(0.8f, 0.35f);
        Assert.AreEqual(1, Field(result.decision, "Index"));
        int a = (int)Field(Field(result.options, "A"), "PerPallet");
        int b = (int)Field(Field(result.options, "B"), "PerPallet");
        Assert.Greater(b, a);
    }

    [Test]
    public void LowerHeightC_UsesTheSameCountAsItsArPlan()
    {
        var result = Calculate(0.8f, 0.35f);
        object b = Field(result.options, "B");
        object c = Field(result.options, "C");
        Assert.AreEqual(Math.Max(1, (int)Field(b, "Layers") - 1), Field(c, "Layers"));
        object renderPlan = Field(c, "Plan");
        Assert.AreEqual(Field(c, "PerPallet"), renderPlan.GetType().GetProperty("Total").GetValue(renderPlan));
        Assert.AreEqual((float)Field(c, "StackHeightM"), (float)Field(renderPlan, "StackHeight"), 0.001f);
    }

    [Test]
    public void NoStrategyMeetsLoadLimit_LeavesRecommendationEmpty()
    {
        var result = Calculate(0.8f, 0.35f, 1f);
        Assert.AreEqual(-1, Field(result.decision, "Index"));
    }
}
