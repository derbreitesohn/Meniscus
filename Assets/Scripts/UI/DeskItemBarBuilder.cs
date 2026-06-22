using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Builds the desk item bar's overlay canvas + transparent row container. Shared by the runtime
    /// fallback (<see cref="DeskItemBar.EnsureCanvas"/>) and the editor authoring tool.
    /// </summary>
    public static class DeskItemBarBuilder
    {
        public static (Canvas barCanvas, Transform rowRoot) Build(Transform parent)
        {
            var barCanvas = RuntimeUiFactory.CreateOverlayCanvas(parent, "Desk Item Bar Canvas", enabled: false);

            var row = RuntimeUiFactory.CreateImage(
                barCanvas.transform,
                "Desk Item Row",
                new Vector2(1500f, 84f),
                new Vector2(0f, 130f),
                new Color(0f, 0f, 0f, 0f));   // transparent container; buttons carry the look

            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 130f);

            return (barCanvas, row.transform);
        }
    }
}
