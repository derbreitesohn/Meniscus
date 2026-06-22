// Assets/Scripts/UI/MenuLayout.cs
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Pure layout math for the book menu: how big the printed canvas may be so it fits within the
    /// page rectangle, and how the catalog divides into pages. No scene or UnityEngine.UI dependency,
    /// so it is unit-tested directly.
    /// </summary>
    public static class MenuLayout
    {
        /// <summary>World units per canvas pixel that keeps the canvas inside both page bounds.</summary>
        public static float ComputeScale(float worldWidth, float worldDepth, float pixelWidth, float pixelHeight)
        {
            if (pixelWidth <= 0f || pixelHeight <= 0f)
                return 0f;

            return Mathf.Min(worldWidth / pixelWidth, worldDepth / pixelHeight);
        }

        public static int ItemsPerPage(float listDepthPx, float rowHeight)
        {
            if (rowHeight <= 0f)
                return 1;

            return Mathf.Max(1, Mathf.FloorToInt(listDepthPx / rowHeight));
        }

        public static int PageCount(int itemCount, int itemsPerPage)
        {
            if (itemsPerPage <= 0)
                return 1;

            return Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)itemsPerPage));
        }

        public static int ClampPage(int page, int pageCount) =>
            Mathf.Clamp(page, 0, Mathf.Max(0, pageCount - 1));
    }
}
