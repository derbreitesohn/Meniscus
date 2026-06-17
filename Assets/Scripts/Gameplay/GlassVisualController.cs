using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    public class GlassVisualController : MonoBehaviour
    {
        [SerializeField] GlassManager glassManager;
        [SerializeField] Transform waterTransform;
        [SerializeField] Vector3 waterSurfaceScale = new(0.82f, 0.025f, 0.82f);
        [SerializeField, Range(0f, 1f)] float baseFillRatio = 0.45f;
        [SerializeField] float lowSurfaceLocalY = -0.34f;
        [SerializeField] float highSurfaceLocalY = 0.72f;
        [SerializeField] float riseSpeed = 1.1f;

        Vector3 waterLocalPosition;
        float currentSurfaceLocalY;
        float targetSurfaceLocalY;

        void OnEnable()
        {
            ResolveReferences();

            if (waterTransform == null)
                waterTransform = transform;

            waterLocalPosition = waterTransform.localPosition;
            currentSurfaceLocalY = waterLocalPosition.y;
            targetSurfaceLocalY = currentSurfaceLocalY;

            if (glassManager != null)
            {
                glassManager.ProbabilityChanged += OnProbabilityChanged;
                OnProbabilityChanged(glassManager.CurrentOverflowProbability);
            }
        }

        void OnDisable()
        {
            if (glassManager != null)
                glassManager.ProbabilityChanged -= OnProbabilityChanged;
        }

        void Update()
        {
            if (waterTransform == null)
                return;

            if (Mathf.Approximately(currentSurfaceLocalY, targetSurfaceLocalY))
                return;

            currentSurfaceLocalY = Mathf.MoveTowards(
                currentSurfaceLocalY,
                targetSurfaceLocalY,
                riseSpeed * Time.deltaTime);
            ApplyWaterTransform(currentSurfaceLocalY);
        }

        void OnProbabilityChanged(float probability)
        {
            var riskNormalized = Mathf.Clamp01(probability / GameConstants.MaxOverflowProbability);
            var fillRatio = Mathf.Lerp(baseFillRatio, 1f, riskNormalized);
            targetSurfaceLocalY = Mathf.Lerp(lowSurfaceLocalY, highSurfaceLocalY, fillRatio);

            if (Mathf.Approximately(probability, 0f))
            {
                currentSurfaceLocalY = targetSurfaceLocalY;
                ApplyWaterTransform(currentSurfaceLocalY);
            }

            Debug.Log(
                $"[GlassVisualController] Probability visual target updated. " +
                $"probability={probability:0.##}%, targetSurfaceY={targetSurfaceLocalY:0.###}.");
        }

        void ApplyWaterTransform(float surfaceLocalY)
        {
            waterTransform.localScale = waterSurfaceScale;
            waterTransform.localPosition = new Vector3(
                waterLocalPosition.x,
                surfaceLocalY,
                waterLocalPosition.z);
        }

        void ResolveReferences()
        {
            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();
        }
    }
}
