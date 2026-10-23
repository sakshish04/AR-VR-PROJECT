using System;
using System.Globalization;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Unity-independent rules of the lab task, so they can be tested without the Editor.
    /// An item counts as "on" a dock when its bottom is over the dock's pad (within the pad radius) and not far above or below the pad surface.
    /// </summary>
    public static class LabPlacementRules
    {
        /// <summary>Lowest allowed height of the item's bottom relative to the pad top (it may sink a little into the pad).</summary>
        public const float MinHeightAbovePad = -0.15f;
        /// <summary>Highest allowed height of the item's bottom above the pad top (dropped from about hand height onto the pad).</summary>
        public const float MaxHeightAbovePad = 0.90f;

        public static bool IsOverPad(float horizontalDistance, float heightAbovePad, float padRadius)
        {
            if (padRadius <= 0f || float.IsNaN(horizontalDistance) || float.IsNaN(heightAbovePad)) return false;
            return horizontalDistance <= padRadius && heightAbovePad >= MinHeightAbovePad && heightAbovePad <= MaxHeightAbovePad;
        }

        /// <summary>True once an item has been placed on a dock and then moved clearly away from it (picked up again).</summary>
        public static bool HasLeftPad(float horizontalDistance, float padRadius, float factor = 1.6f)
        {
            return horizontalDistance > padRadius * factor;
        }

        public static string Progress(int placed, int total)
        {
            return Math.Max(0, placed).ToString(CultureInfo.InvariantCulture) + " / " + Math.Max(0, total).ToString(CultureInfo.InvariantCulture);
        }

        public static string FormatDuration(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            int s = (int)Math.Round(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
