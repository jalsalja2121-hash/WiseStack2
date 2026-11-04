using UnityEngine;

namespace ARLogistics.UI
{
    /// <summary>Shared mobile palette for the four operational screens.</summary>
    public static class WiseStackTheme
    {
        public const float NavigationHeight = 104;
        public static readonly Color Ink = new Color32(23,38,56,255);
        public const int PageTitle = 34, SectionTitle = 28, Body = 26, Caption = 22, ButtonText = 28;
        public const int PagePadding = 24, CardPadding = 24, Gap = 16;
        public static readonly Color Navy = new Color32(20,36,58,255);
        public static readonly Color Blue = new Color32(23,105,210,255);
        public static readonly Color Pale = new Color32(235,243,255,255);
        public static readonly Color Background = new Color32(245,247,251,255);
        public static readonly Color Border = new Color32(226,232,240,255);
        public static readonly Color Card = Color.white;
        public static readonly Color Muted = new Color32(86,103,124,255);
        public static readonly Color Success = new Color32(22,133,106,255);
        public static readonly Color Warning = new Color32(155,101,16,255);
        public static readonly Color Error = new Color32(186,46,46,255);
    }
}
