using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Data for one glass variant the table can use. Authorable as a ScriptableObject asset
    /// (Create &gt; Meniscus &gt; Glass Type) so new glass shapes can be added without code, and also
    /// constructible at runtime via <see cref="Create"/>. Carries the visual model plus the water-surface
    /// fit, so each glass shape holds its water at the right height and radius. One "normal" type is
    /// enough today; drop in more assets later and the presentation switches between them.
    /// </summary>
    [CreateAssetMenu(menuName = "Meniscus/Glass Type", fileName = "GlassType")]
    public class GlassTypeDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName = "New Glass";

        [Header("Model")]
        [Tooltip("Glass model spawned for this type. Drag a glass model here (e.g. the Glas_Types FBX).")]
        [SerializeField] GameObject modelPrefab;
        [Tooltip("If the model holds several glasses (like Glas_Types), name the child to show, e.g. Standard_Glas. " +
                 "Empty shows the whole model.")]
        [SerializeField] string modelChildName;
        [SerializeField] Vector3 modelLocalPosition = Vector3.zero;
        [SerializeField] Vector3 modelLocalEuler = Vector3.zero;
        [SerializeField] Vector3 modelLocalScale = Vector3.one;

        [Header("Water Fit")]
        [Tooltip("Local Y of the calm water surface inside this glass.")]
        [SerializeField] float waterSurfaceLocalY = 0.62f;
        [Tooltip("Radius of the water disc so it just meets the inner wall of this glass.")]
        [SerializeField, Min(0.01f)] float waterSurfaceRadius = 0.39f;
        [Tooltip("Local Y of the glass interior floor; the liquid body fills from here up to the surface.")]
        [SerializeField] float fillBottomLocalY = 0.06f;
        [Tooltip("Liquid radius at the floor as a fraction of the surface radius (gentle tumbler taper).")]
        [SerializeField, Range(0.1f, 1f)] float fillBottomRadiusScale = 0.82f;

        public string Id => string.IsNullOrEmpty(id) ? displayName : id;
        public string DisplayName => displayName;
        public GameObject ModelPrefab => modelPrefab;
        public string ModelChildName => modelChildName;
        public Vector3 ModelLocalPosition => modelLocalPosition;
        public Quaternion ModelLocalRotation => Quaternion.Euler(modelLocalEuler);
        public Vector3 ModelLocalScale => modelLocalScale == Vector3.zero ? Vector3.one : modelLocalScale;
        public float WaterSurfaceLocalY => waterSurfaceLocalY;
        public float WaterSurfaceRadius => Mathf.Max(0.01f, waterSurfaceRadius);
        public float FillBottomLocalY => fillBottomLocalY;
        public float FillBottomRadiusScale => Mathf.Clamp(fillBottomRadiusScale, 0.1f, 1f);

        public static GlassTypeDefinition Create(
            string id,
            string displayName,
            GameObject modelPrefab,
            string modelChildName = "",
            float waterSurfaceLocalY = 0.62f,
            float waterSurfaceRadius = 0.39f,
            float fillBottomLocalY = 0.06f,
            float fillBottomRadiusScale = 0.82f)
        {
            var glass = CreateInstance<GlassTypeDefinition>();
            glass.id = id;
            glass.displayName = displayName;
            glass.modelPrefab = modelPrefab;
            glass.modelChildName = modelChildName;
            glass.modelLocalScale = Vector3.one;
            glass.waterSurfaceLocalY = waterSurfaceLocalY;
            glass.waterSurfaceRadius = waterSurfaceRadius;
            glass.fillBottomLocalY = fillBottomLocalY;
            glass.fillBottomRadiusScale = fillBottomRadiusScale;
            glass.name = string.IsNullOrEmpty(id) ? displayName : id;
            return glass;
        }
    }
}
