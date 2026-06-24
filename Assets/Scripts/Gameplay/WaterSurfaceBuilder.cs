using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Builds the liquid surface host (a MeshFilter+MeshRenderer child of the glass) for
    /// <see cref="GlassVisualController"/>. Shared by the runtime fallback and the editor authoring
    /// tool so the authored object is identical to the runtime-built one.
    /// </summary>
    public static class WaterSurfaceBuilder
    {
        public const string DefaultName = "Runtime Water Surface";
        public const string AuthoredName = "Liquid";

        public static Transform Build(Transform glass, string name)
        {
            var waterObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            waterObject.transform.SetParent(glass, false);
            waterObject.transform.localPosition = Vector3.zero;
            waterObject.transform.localScale = Vector3.one;
            return waterObject.transform;
        }
    }
}
