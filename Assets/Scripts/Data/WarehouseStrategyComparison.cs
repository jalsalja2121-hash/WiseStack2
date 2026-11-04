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
        public float SpaceUtilizationPercent;
        public float HeightReserveM;
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

        public WarehouseStrategy Get(int index) => index == 0 ? A : index == 1 ? B : C;
    }

    /// <summary>Both tabs use the same inputs and the same calculated strategy objects.</summary>
    public static class WarehouseStrategySession
    {
        private static int pendingArStrategy = -1;

        public static bool TryBuild(out WarehouseRecommendation baseline,
            out WarehouseStrategyComparison comparison, out string error)
        {
            baseline = null;
            comparison = null;
            error = null;
            if (!BoxMeasurementStore.TryGet(out BoxMeasurement cargo))
            {
                error = "측정 화면에서 화물을 분류하고 규격을 저장해 주세요.";
                return false;
            }
            if (cargo.ClassId < 0 || cargo.ClassId >= ProductSpecTable.ClassNames.Length)
            {
                error = "저장된 화물 분류를 확인할 수 없습니다. 다시 분류해 주세요.";
                return false;
            }

            string name = string.IsNullOrEmpty(cargo.SourceName)
                ? ProductSpecTable.ClassName(cargo.ClassId) : cargo.SourceName;
            if (!WarehouseRecommendationCalculator.TryCalculate(
                AppSettings.WarehouseAreaM2, AppSettings.CeilingHeightM,
                AppSettings.PalletWidth, AppSettings.PalletLength, AppSettings.PalletMaxLoadKg,
                cargo, ProductSpecTable.RecommendationWeightKg(cargo.ClassId), name, out baseline))
            {
                error = "창고, 팔레트, 화물의 입력값을 확인해 주세요.";
                return false;
            }
            comparison = WarehouseStrategyCalculator.Calculate(baseline);
            WarehouseRecommendationStore.Save(baseline);
            return comparison != null;
        }

        public static void OpenInAr(int strategyIndex) => pendingArStrategy = strategyIndex;
        public static int ConsumeArStrategy()
        {
            int selected = pendingArStrategy;
            pendingArStrategy = -1;
            return selected;
        }
    }

    public sealed class WarehouseStrategyDecision
    {
        public int Index = -1;
        public string Reason;
        public string Criteria;
        public readonly StrategyEvaluation[] Evaluations = new StrategyEvaluation[3];
        public bool HasRecommendation => Index >= 0;
        public bool RequiresLoadConfirmation;
    }

    public sealed class StrategyEvaluation
    {
        public bool Eligible;
        public bool EquivalentToWinner;
        public string Exclusion;
        public bool LoadVerified;
    }

    /// <summary>WiseStack quantity-first lexicographic policy, not an ISO formula.</summary>
    public static class WarehouseStrategyEvaluator
    {
        public static WarehouseStrategyDecision Evaluate(WarehouseRecommendation source,
            WarehouseStrategyComparison comparison)
        {
            var result = new WarehouseStrategyDecision
            {
                Criteria = "검증 가능한 높이·하중·경계 조건 위반은 제외합니다. 수량 최대 → 바닥면 활용률 최대 → 총 높이 최소 → 기본 방향 → A/B/C 순으로 선정합니다. " +
                    "유효 높이 한도는 min(1.8m, 천장 높이 − 0.3m), 팔레트 포함입니다. 무게 누락 시 하중 확인이 필요한 조건부 예상안입니다. " +
                    "WiseStack2 자체 최적화 정책이며 ISO 추천 공식이 아닙니다."
            };
            for (int i = 0; i < 3; i++)
            {
                var candidate = comparison?.Get(i);
                string exclusion = source == null || comparison == null || comparison.PalletCount <= 0
                    ? "입력값이나 배치 가능한 팔레트 수가 부족합니다." : Exclusion(source, candidate);
                result.Evaluations[i] = new StrategyEvaluation
                {
                    Eligible = exclusion == null, Exclusion = exclusion,
                    LoadVerified = exclusion == null && source.AssumedCargoWeightKg > 0
                };
                if (exclusion == null && (result.Index < 0 || Better(candidate, comparison.Get(result.Index))))
                    result.Index = i;
            }
            if (!result.HasRecommendation)
            {
                result.Reason = "현재 규격·높이·하중 조건에 적합한 전략이 없습니다. 입력 조건을 확인해 주세요.";
                return result;
            }
            var selected = comparison.Get(result.Index);
            result.RequiresLoadConfirmation = !result.Evaluations[result.Index].LoadVerified;
            result.Reason = "검증 가능한 조건을 충족하는 후보 중 수량을 우선하고, 동률이면 바닥면 활용률과 낮은 높이를 비교해 선정했습니다.";
            for (int i = 0; i < 3; i++)
            {
                if (!result.Evaluations[i].Eligible) continue;
                result.Evaluations[i].EquivalentToWinner = Equivalent(selected, comparison.Get(i));
                if (i != result.Index && result.Evaluations[i].EquivalentToWinner)
                    result.Reason = $"{(char)('A' + result.Index)}와 {(char)('A' + i)}는 수량·높이·바닥면 활용률이 동일합니다. 기본 방향 우선, 동일 방향이면 A/B/C 순으로 선정했습니다.";
            }
            if (result.RequiresLoadConfirmation) result.Reason += " 화물 무게 누락으로 하중 확인이 필요합니다.";
            return result;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static string Exclusion(WarehouseRecommendation source, WarehouseStrategy strategy)
        {
            if (!Finite(source.PalletWidthM) || source.PalletWidthM <= 0 ||
                !Finite(source.PalletLengthM) || source.PalletLengthM <= 0 ||
                !Finite(source.Cargo.WidthM) || source.Cargo.WidthM <= 0 ||
                !Finite(source.Cargo.DepthM) || source.Cargo.DepthM <= 0 ||
                !Finite(source.Cargo.HeightM) || source.Cargo.HeightM <= 0 ||
                !Finite(source.AssumedCargoWeightKg) || source.AssumedCargoWeightKg < 0 ||
                source.PalletMaxLoadKg < 0) return "유효하지 않은 입력값";
            if (strategy == null || !strategy.Available) return strategy?.Message ?? "계산 결과 없음";
            if (!Finite(source.CeilingHeightM) || !Finite(source.PalletMaxLoadKg) ||
                !Finite(strategy.StackHeightM) || !Finite(strategy.WeightKg) ||
                !Finite(strategy.FloorUtilizationPercent) || strategy.WeightKg < 0 ||
                strategy.StackHeightM <= StackingCalculator.PalletHeight ||
                strategy.FloorUtilizationPercent <= 0 || strategy.FloorUtilizationPercent > 100)
                return "유효하지 않은 계산값";
            if (strategy.WeightKg > source.PalletMaxLoadKg + 0.01f) return "최대 하중 초과";
            if (strategy.StackHeightM > Math.Min(StackingCalculator.PreviewHeightLimit,
                source.CeilingHeightM - StackingCalculator.Headroom) + 0.001f) return "적재 높이 한도 초과";
            var p = strategy.Plan;
            if (p == null || p.Total != strategy.PerPallet || p.Layers != strategy.Layers ||
                p.PerLayer != strategy.PerLayer || p.Columns <= 0 || p.Rows <= 0 ||
                (long)p.Columns * p.Rows * p.Layers != strategy.PerPallet ||
                Math.Abs(strategy.StackHeightM - (StackingCalculator.PalletHeight + strategy.Layers * source.Cargo.HeightM)) > 0.001f ||
                Math.Abs(strategy.WeightKg - (double)strategy.PerPallet * source.AssumedCargoWeightKg) > 0.01f ||
                Math.Abs(p.BoxWidth - (strategy.Orientation == StackOrientation.Rotated ? source.Cargo.DepthM : source.Cargo.WidthM)) > 0.00001f ||
                Math.Abs(p.BoxLength - (strategy.Orientation == StackOrientation.Rotated ? source.Cargo.WidthM : source.Cargo.DepthM)) > 0.00001f ||
                Math.Abs(strategy.FloorUtilizationPercent - (double)p.PerLayer * p.BoxWidth * p.BoxLength / source.PalletWidthM / source.PalletLengthM * 100) > 0.01f || !Finite(p.StackHeight) || !Finite(p.TotalWeight) ||
                Math.Abs(p.StackHeight - strategy.StackHeightM) > 0.001f ||
                Math.Abs(p.TotalWeight - strategy.WeightKg) > 0.01f ||
                !Finite(p.BoxWidth) || !Finite(p.BoxLength) || p.BoxWidth <= 0 || p.BoxLength <= 0 ||
                p.Columns * p.BoxWidth > source.PalletWidthM + 0.001f ||
                p.Rows * p.BoxLength > source.PalletLengthM + 0.001f) return "배치 또는 AR 계획 불일치";
            return null;
        }

        public static bool Equivalent(WarehouseStrategy a, WarehouseStrategy b) =>
            a != null && b != null && a.PerPallet == b.PerPallet && a.Layers == b.Layers &&
            Math.Abs(a.StackHeightM - b.StackHeightM) <= 0.001f &&
            Math.Abs(a.FloorUtilizationPercent - b.FloorUtilizationPercent) <= 0.01f;

        public static bool Better(WarehouseStrategy candidate, WarehouseStrategy current)
        {
            if (candidate.PerPallet != current.PerPallet)
                return candidate.PerPallet > current.PerPallet;
            if (Math.Abs(candidate.FloorUtilizationPercent - current.FloorUtilizationPercent) > 0.01f)
                return candidate.FloorUtilizationPercent > current.FloorUtilizationPercent;
            if (Math.Abs(candidate.StackHeightM - current.StackHeightM) > 0.001f)
                return candidate.StackHeightM < current.StackHeightM;
            return candidate.Orientation == StackOrientation.Original &&
                current.Orientation != StackOrientation.Original;
        }
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
                comparison.C.Plan = new StackingPlan
                {
                    Columns = best.Columns, Rows = best.Rows, Layers = layers,
                    HeightLayers = best.HeightLayers, WeightLayers = best.WeightLayers,
                    BoxWidth = best.BoxWidth, BoxLength = best.BoxLength,
                    Utilization = best.Utilization,
                    Message = best.Message,
                    StackHeight = StackingCalculator.PalletHeight + layers * source.Cargo.HeightM,
                    LoadVerified = best.LoadVerified,
                    TotalWeight = best.PerLayer * layers * source.AssumedCargoWeightKg
                };
                comparison.C.Layers = layers;
                comparison.C.PerPallet = best.PerLayer * layers;
                comparison.C.StackHeightM = StackingCalculator.PalletHeight + layers * source.Cargo.HeightM;
                comparison.C.WeightKg = comparison.C.PerPallet * source.AssumedCargoWeightKg;
                comparison.C.WarehouseCapacity = (long)pallets * comparison.C.PerPallet;
                comparison.C.Message = best.Layers == 1
                    ? "기존 최대치가 1층이므로 높이를 더 낮출 수 없습니다."
                    : "최대 적재 층수보다 1층 낮춘 비교안입니다. 안전성을 보장하지 않습니다.";
                if (!best.LoadVerified) comparison.C.Message += " 화물 무게 누락: 하중 확인 필요.";
                comparison.C.Plan.Message = comparison.C.Message;
            }
            float limit = Math.Min(StackingCalculator.PreviewHeightLimit, source.CeilingHeightM - StackingCalculator.Headroom);
            double available = limit - StackingCalculator.PalletHeight;
            for (int i = 0; i < 3; i++)
            {
                var option = comparison.Get(i);
                option.HeightReserveM = limit - option.StackHeightM;
                option.SpaceUtilizationPercent = available > 0
                    ? (float)((double)option.PerPallet * source.Cargo.WidthM * source.Cargo.DepthM * source.Cargo.HeightM /
                        ((double)source.PalletWidthM * source.PalletLengthM * available) * 100) : 0;
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
