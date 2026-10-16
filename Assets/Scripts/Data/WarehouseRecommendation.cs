using System;
using System.Collections.Generic;

namespace ARLogistics.Data
{
    [Serializable]
    public sealed class WarehouseZoneRecommendation
    {
        public string Name;
        public float AreaM2;
        public float WidthM;
        public float DepthM;
        public int PalletCount;
        public string PalletOrientation;
        public long CargoCapacity;
        public float OccupiedAreaM2;
        public float FreeAreaM2;
    }

    [Serializable]
    public sealed class WarehouseRecommendation
    {
        public float WarehouseAreaM2;
        public float CeilingHeightM;
        public float AssumedSideM;
        public float ExcludedAreaM2;
        public float UsableAreaM2;
        public float OccupiedAreaM2;
        public float FreeAreaM2;
        public float AreaUtilizationPercent;
        public float RemainingAreaPercent;
        public float PalletWidthM;
        public float PalletLengthM;
        public float PalletMaxLoadKg;
        public int PalletCount;
        public long TotalCargoCapacity;
        public string CargoName;
        public BoxMeasurement Cargo;
        public float AssumedCargoWeightKg;
        public StackingPlan Plan;
        public readonly List<WarehouseZoneRecommendation> Zones = new();
        public string Assumptions;
    }

    /// <summary>면적 기반의 개략 계획. 실제 창고 좌표나 안전 적재 판정은 제공하지 않습니다.</summary>
    public static class WarehouseRecommendationCalculator
    {
        public const float AssumedExcludedAreaFraction = 0.2f;
        public const int ZoneCount = 3;

        public static bool TryCalculate(float warehouseAreaM2, float ceilingHeightM,
            float palletWidthM, float palletLengthM, float palletMaxLoadKg,
            BoxMeasurement cargo, float cargoWeightKg, string cargoName,
            out WarehouseRecommendation result)
        {
            result = null;
            if (!Valid(warehouseAreaM2) || !Valid(ceilingHeightM) ||
                !Valid(palletWidthM) || !Valid(palletLengthM) ||
                !Valid(cargoWeightKg) || !Valid(cargo.WidthM) ||
                !Valid(cargo.DepthM) || !Valid(cargo.HeightM) ||
                float.IsNaN(palletMaxLoadKg) || float.IsInfinity(palletMaxLoadKg) ||
                palletMaxLoadKg < 0f || warehouseAreaM2 > 1000000f)
                return false;

            float palletArea = palletWidthM * palletLengthM;
            float side = (float)Math.Sqrt(warehouseAreaM2);
            float excluded = warehouseAreaM2 * AssumedExcludedAreaFraction;
            float usable = warehouseAreaM2 - excluded;
            float zoneWidth = side / ZoneCount;
            float zoneDepth = side * (1f - AssumedExcludedAreaFraction);
            var plan = StackingCalculator.Calculate(cargo.WidthM, cargo.DepthM,
                cargo.HeightM, cargoWeightKg, palletWidthM, palletLengthM,
                ceilingHeightM, palletMaxLoadKg);
            result = new WarehouseRecommendation
            {
                WarehouseAreaM2 = warehouseAreaM2,
                CeilingHeightM = ceilingHeightM,
                AssumedSideM = side,
                ExcludedAreaM2 = excluded,
                UsableAreaM2 = usable,
                PalletWidthM = palletWidthM,
                PalletLengthM = palletLengthM,
                PalletMaxLoadKg = palletMaxLoadKg,
                CargoName = cargoName,
                Cargo = cargo,
                AssumedCargoWeightKg = cargoWeightKg,
                Plan = plan,
                Assumptions = "면적의 제곱근을 한 변으로 둔 가상 정사각형 창고. 한쪽 깊이 20%를 통로·설비용으로 제외하고 남은 공간을 동일 폭의 3구역으로 가정했습니다. " +
                    "실제 가로·세로·통로·기둥·출입구와 재고 수량은 입력되지 않았습니다. " +
                    "화물 치수와 무게는 클래스별 대표 규격 기준 예상값입니다. 위치는 실제 좌표가 아닌 개략 구역입니다."
            };

            for (int index = 0; index < ZoneCount; index++)
            {
                float zoneArea = zoneWidth * zoneDepth;
                long original = (long)StackingCalculator.Fit(zoneWidth, palletWidthM) *
                    StackingCalculator.Fit(zoneDepth, palletLengthM);
                long rotated = (long)StackingCalculator.Fit(zoneWidth, palletLengthM) *
                    StackingCalculator.Fit(zoneDepth, palletWidthM);
                bool rotate = rotated > original;
                int palletCount = (int)Math.Min(int.MaxValue / ZoneCount,
                    rotate ? rotated : original);
                float occupied = Math.Min(zoneArea, palletCount * palletArea);
                var zone = new WarehouseZoneRecommendation
                {
                    Name = ((char)('A' + index)).ToString(),
                    AreaM2 = zoneArea,
                    WidthM = zoneWidth,
                    DepthM = zoneDepth,
                    PalletCount = palletCount,
                    PalletOrientation = rotate ? "90° 회전" : "기본 방향",
                    CargoCapacity = (long)palletCount * plan.Total,
                    OccupiedAreaM2 = occupied,
                    FreeAreaM2 = Math.Max(0f, zoneArea - occupied)
                };
                result.Zones.Add(zone);
                result.PalletCount += palletCount;
                result.TotalCargoCapacity += zone.CargoCapacity;
                result.OccupiedAreaM2 += occupied;
                result.FreeAreaM2 += zone.FreeAreaM2;
            }

            result.AreaUtilizationPercent = result.OccupiedAreaM2 / warehouseAreaM2 * 100f;
            result.RemainingAreaPercent = result.FreeAreaM2 / warehouseAreaM2 * 100f;
            return true;
        }

        private static bool Valid(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.01f;
    }

    /// <summary>추천 Scene과 AR Scene이 같은 계산 결과 객체를 재사용하는 세션 저장소.</summary>
    public static class WarehouseRecommendationStore
    {
        public static WarehouseRecommendation Current { get; private set; }
        public static void Save(WarehouseRecommendation recommendation) => Current = recommendation;
        public static void Clear() => Current = null;
    }
}
