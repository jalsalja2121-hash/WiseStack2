using System;

namespace ARLogistics.Data
{
    public enum StackOrientation { Recommended, Original, Rotated }

    public sealed class StackingPlan
    {
        public int Columns, Rows, Layers, HeightLayers, WeightLayers;
        public float BoxWidth, BoxLength, StackHeight, TotalWeight, Utilization;
        public string Message;
        public int PerLayer { get { return Columns * Rows; } }
        public int Total { get { return PerLayer * Layers; } }
    }

    /// <summary>Shared preview policy. Geometry/load estimates, not a certified stacking safety assessment.</summary>
    public static class StackingCalculator
    {
        public const float PalletHeight = 0.15f;
        public const float Headroom = 0.3f;
        // App policy, including the pallet. Ceiling height is NOT a cargo stacking limit.
        // Packaging compression strength and load securing must be verified separately.
        public const float PreviewHeightLimit = 1.8f;

        public static StackingPlan Calculate(float width, float length, float height, float weight,
            float palletWidth, float palletLength, float ceiling, float maxLoad,
            StackOrientation orientation = StackOrientation.Recommended, bool isPallet = false)
        {
            var p = new StackingPlan { Message = "크기와 무게, 적재 조건을 확인해 주세요." };
            if (!Positive(width) || !Positive(length) || !Positive(height) ||
                !Positive(palletWidth) || !Positive(palletLength) || !Positive(ceiling) ||
                !Finite(weight) || weight < 0 || !Finite(maxLoad) || maxLoad < 0) return p;
            int c = Fit(palletWidth, width), r = Fit(palletLength, length);
            int rc = Fit(palletWidth, length), rr = Fit(palletLength, width);
            bool rotate = orientation == StackOrientation.Rotated ||
                (orientation == StackOrientation.Recommended && (long)rc * rr > (long)c * r);
            p.Columns = rotate ? rc : c;
            p.Rows = rotate ? rr : r;
            p.BoxWidth = rotate ? length : width;
            p.BoxLength = rotate ? width : length;
            // Reject pathological inputs instead of allowing count overflow or huge render loops.
            if ((long)p.Columns * p.Rows > 1000000) { p.Columns = p.Rows = 0; return p; }
            if (p.PerLayer == 0) { p.Message = "이 방향으로는 상자가 팔레트 안에 들어가지 않아요."; return p; }
            float heightLimit = Math.Min(PreviewHeightLimit, ceiling - Headroom);
            p.HeightLayers = Fit(Math.Max(0, heightLimit - PalletHeight), height);
            p.WeightLayers = weight > 0.01f ? Fit(maxLoad, weight * p.PerLayer) : p.HeightLayers;
            p.Layers = Math.Min(p.HeightLayers, p.WeightLayers);
            if (isPallet) p.Layers = Math.Min(1, p.Layers);
            p.Layers = Math.Min(p.Layers, int.MaxValue / p.PerLayer);
            p.StackHeight = PalletHeight + p.Layers * height;
            p.TotalWeight = p.Total * weight;
            p.Utilization = Math.Min(100f, p.PerLayer * width * length / (palletWidth * palletLength) * 100f);
            p.Message = p.Layers == 0
                ? (p.HeightLayers == 0 ? "높이가 부족해 한 단도 쌓을 수 없어요." : "한 단의 무게가 팔레트 허용 하중을 넘어요.")
                : (p.WeightLayers < p.HeightLayers ? "하중 제한에 맞춰 단수를 줄였어요."
                    : (heightLimit < PreviewHeightLimit ? "천장 아래 30cm를 비워 두었어요." : "팔레트 포함 1.8m 이내로 제한했어요."));
            return p;
        }

        public static int Fit(float available, float required)
        {
            if (!Finite(available) || available <= 0 || !Positive(required)) return 0;
            // Float-derived metre dimensions can be just below an exact multiple.
            return (int)Math.Min(1000000, Math.Floor((double)available / required + 0.000001));
        }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool Positive(float v) { return Finite(v) && v > 0.01f; }
    }
}
