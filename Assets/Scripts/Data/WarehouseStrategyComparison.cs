using System;

namespace ARLogistics.Data
{
    public sealed class WarehouseStrategy
    {
        public string Name;
        public StackOrientation Orientation;
        public StackingPlan Plan;
        public int PerLayer;
        public int Layers;
        public int PerPallet;
        public float StackHeightM;
        public float WeightKg;
        public float FloorUtilizationPercent;
        public long WarehouseCapacity;
        public string Message;
        public bool Available => PerPallet > 0 && Layers > 0;
    }

    public sealed class WarehouseStrategyComparison
    {
        public int PalletCount;
        public float OccupiedAreaM2;
        public float AreaUtilizationPercent;
        public WarehouseStrategy A;
        public WarehouseStrategy B;
        public WarehouseStrategy C;
    }

    /// <summary>Alternative plans for the same full warehouse footprint.</summary>
    public static class WarehouseStrategyCalculator
    {
        public static WarehouseStrategyComparison Calculate(WarehouseRecommendation source)
        {
            if (source == null) return null;

            float depth = source.AssumedSideM *
                (1f - WarehouseRecommendationCalculator.AssumedExcludedAreaFraction);
            long normal = (long)StackingCalculator.Fit(source.AssumedSideM, source.PalletWidthM) *
                          StackingCalculator.Fit(depth, source.PalletLengthM);
            long rotated = (long)StackingCalculator.Fit(source.AssumedSideM, source.PalletLengthM) *
                           StackingCalculator.Fit(depth, source.PalletWidthM);
            int pallets = (int)Math.Min(int.MaxValue, Math.Max(normal, rotated));
            float occupied = Math.Min(source.UsableAreaM2,
                pallets * source.PalletWidthM * source.PalletLengthM);
            var comparison = new WarehouseStrategyComparison
            {
                PalletCount = pallets,
                OccupiedAreaM2 = occupied,
                AreaUtilizationPercent = occupied / source.WarehouseAreaM2 * 100f
            };

            StackingPlan original = Plan(source, StackOrientation.Original);
            StackingPlan turned = Plan(source, StackOrientation.Rotated);
            comparison.A = Make("A · 표준 적재", StackOrientation.Original, original, pallets);
            bool chooseTurned = turned.Total > 0 && (original.Total == 0 ||
                turned.Total > original.Total ||
                turned.Total == original.Total && turned.Utilization > original.Utilization);
            StackingPlan best = chooseTurned ? turned : original;
            StackOrientation direction = chooseTurned ? StackOrientation.Rotated : StackOrientation.Original;
            comparison.B = Make("B · 공간 효율", direction, best, pallets);
            comparison.C = Make("C · 낮은 적재 높이", direction, best, pallets);
            if (comparison.C.Available)
            {
                int layers = Math.Max(1, best.Layers - 1);
                comparison.C.Layers = layers;
                comparison.C.PerPallet = best.PerLayer * layers;
                comparison.C.StackHeightM = StackingCalculator.PalletHeight + layers * source.Cargo.HeightM;
                comparison.C.WeightKg = comparison.C.PerPallet * source.AssumedCargoWeightKg;
                comparison.C.WarehouseCapacity = (long)pallets * comparison.C.PerPallet;
                comparison.C.Message = best.Layers == 1
                    ? "기존 최대치가 1층이므로 높이를 더 낮출 수 없습니다."
                    : "최대 적재 층수보다 1층 낮춘 비교안입니다. 안전성을 보장하지 않습니다.";
            }
            return comparison;
        }

        private static StackingPlan Plan(WarehouseRecommendation source, StackOrientation direction) =>
            StackingCalculator.Calculate(source.Cargo.WidthM, source.Cargo.DepthM,
                source.Cargo.HeightM, source.AssumedCargoWeightKg,
                source.PalletWidthM, source.PalletLengthM, source.CeilingHeightM,
                source.PalletMaxLoadKg, direction);

        private static WarehouseStrategy Make(string name, StackOrientation direction,
            StackingPlan plan, int pallets) => new WarehouseStrategy
            {
                Name = name,
                Orientation = direction,
                Plan = plan,
                PerLayer = plan.PerLayer,
                Layers = plan.Layers,
                PerPallet = plan.Total,
                StackHeightM = plan.StackHeight,
                WeightKg = plan.TotalWeight,
                FloorUtilizationPercent = plan.Utilization,
                WarehouseCapacity = (long)pallets * plan.Total,
                Message = plan.Message
            };
    }
}
