namespace ARLogistics
{
    public static class AppSettings
    {
        public const float MinPalletDimensionM = 0.3f;
        public const float MaxPalletDimensionM = 3f;

        private static float palletWidth = 1.2f;
        private static float palletLength = 1.0f;

        public static float WarehouseAreaM2 = 500f;
        public static float CeilingHeightM  = 6f;
        public static float PalletWidth
        {
            get => palletWidth;
            set => palletWidth = SanitizePalletDimension(value, 1.2f);
        }

        public static float PalletLength
        {
            get => palletLength;
            set => palletLength = SanitizePalletDimension(value, 1.0f);
        }

        public static float PalletMaxLoadKg = 1000f;

        public static float SanitizePalletDimension(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ||
                   value < MinPalletDimensionM || value > MaxPalletDimensionM
                ? fallback
                : value;
        }
    }
}
