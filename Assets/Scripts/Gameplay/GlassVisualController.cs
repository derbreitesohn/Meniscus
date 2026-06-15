using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    public class GlassVisualController : MonoBehaviour
    {
        [SerializeField] GameStateManager stateManager;
        [SerializeField] Transform waterTransform;
        [SerializeField] Vector3 fullLocalScale = new(0.25f, 0.21f, 0.25f);
        [SerializeField] float waterBottomLocalY = 0.99f;
        [SerializeField, Range(0.7f, 0.98f)] float baseFillRatio = 0.88f;
        [SerializeField] float jitterPerCoin = 0.35f;
        [SerializeField] float riseSpeed = 0.55f;

        Vector3 waterLocalPosition;
        float currentScaleY;
        float targetScaleY;

        void OnEnable()
        {
            if (stateManager == null)
                stateManager = FindAnyObjectByType<GameStateManager>();

            if (waterTransform != null)
                waterLocalPosition = waterTransform.localPosition;

            if (stateManager != null)
            {
                stateManager.StateChanged += OnStateChanged;
                OnStateChanged(stateManager.State);
            }
        }

        void OnDisable()
        {
            if (stateManager != null)
                stateManager.StateChanged -= OnStateChanged;
        }

        void Update()
        {
            if (waterTransform == null)
                return;

            if (Mathf.Approximately(currentScaleY, targetScaleY))
                return;

            currentScaleY = Mathf.MoveTowards(currentScaleY, targetScaleY, riseSpeed * Time.deltaTime);
            ApplyWaterTransform(currentScaleY);
        }

        void OnStateChanged(MatchState state)
        {
            if (waterTransform == null)
                return;

            targetScaleY = GetTargetScaleY(state.Glass);

            if (state.Glass.CoinsInGlass <= 0)
            {
                currentScaleY = targetScaleY;
                ApplyWaterTransform(currentScaleY);
            }
        }

        float GetTargetScaleY(GlassState glass)
        {
            var coinRange = 1f - baseFillRatio;
            var fill = baseFillRatio + glass.FillNormalized * coinRange;

            if (glass.OverflowThreshold > 0 && glass.CoinsInGlass > 0)
            {
                var heightPerCoin = fullLocalScale.y * coinRange / glass.OverflowThreshold;
                var jitter = (Mathf.PerlinNoise(glass.CoinsInGlass * 1.9f, glass.OverflowThreshold * 0.6f) - 0.5f)
                    * heightPerCoin * jitterPerCoin;
                fill = Mathf.Clamp01(fill + jitter / fullLocalScale.y);
            }

            return fullLocalScale.y * fill;
        }

        void ApplyWaterTransform(float scaleY)
        {
            waterTransform.localScale = new Vector3(fullLocalScale.x, scaleY, fullLocalScale.z);
            waterTransform.localPosition = new Vector3(
                waterLocalPosition.x,
                waterBottomLocalY + scaleY,
                waterLocalPosition.z);
        }
    }
}
