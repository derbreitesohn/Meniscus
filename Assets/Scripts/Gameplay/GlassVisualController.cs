using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    public class GlassVisualController : MonoBehaviour
    {
        [SerializeField] GlassManager glassManager;
        [SerializeField] Transform waterTransform;
        [SerializeField] Vector3 waterSurfaceScale = new(0.78f, 0.018f, 0.78f);
        [SerializeField] float stableSurfaceLocalY = 0.62f;
        [SerializeField] float maxRiskSurfaceRise = 0.015f;
        [SerializeField] float riseSpeed = 1.1f;
        [SerializeField] float dangerWobbleAmplitude = 0.008f;
        [SerializeField] float spillPuddleLifetime = 1.4f;
        [Header("Audio")]
[       SerializeField] AK.Wwise.Event waterSpill; 


        Vector3 waterLocalPosition;
        float currentSurfaceLocalY;
        float targetSurfaceLocalY;
        float currentRisk;
        float spillFlashTimer;

        void OnEnable()
        {
            ResolveReferences();

            if (waterTransform == null)
                waterTransform = CreateRuntimeWaterSurface();

            ApplyWaterMeshPolish();
            ApplyWaterMaterialPolish();

            waterLocalPosition = waterTransform.localPosition;
            currentSurfaceLocalY = waterLocalPosition.y;
            targetSurfaceLocalY = currentSurfaceLocalY;

            if (glassManager != null)
            {
                glassManager.ProbabilityChanged += OnProbabilityChanged;
                glassManager.DropResolved += OnDropResolved;
                OnProbabilityChanged(glassManager.CurrentOverflowProbability);
            }
        }

        void OnDisable()
        {
            if (glassManager != null)
            {
                glassManager.ProbabilityChanged -= OnProbabilityChanged;
                glassManager.DropResolved -= OnDropResolved;
            }
        }

        void Update()
        {
            if (waterTransform == null)
                return;

            if (!Mathf.Approximately(currentSurfaceLocalY, targetSurfaceLocalY))
            {
                currentSurfaceLocalY = Mathf.MoveTowards(
                    currentSurfaceLocalY,
                    targetSurfaceLocalY,
                    riseSpeed * Time.deltaTime);
            }

            if (spillFlashTimer > 0f)
                spillFlashTimer = Mathf.Max(0f, spillFlashTimer - Time.deltaTime);

            ApplyWaterTransform(currentSurfaceLocalY);
        }

        void OnProbabilityChanged(float probability)
        {
            currentRisk = probability;
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(
                probability,
                stableSurfaceLocalY,
                maxRiskSurfaceRise);

            if (Mathf.Approximately(probability, 0f))
            {
                currentSurfaceLocalY = targetSurfaceLocalY;
                ApplyWaterTransform(currentSurfaceLocalY);
            }

            Debug.Log(
                $"[GlassVisualController] Probability visual target updated. " +
                $"probability={probability:0.##}%, targetSurfaceY={targetSurfaceLocalY:0.###}.");
        }

        void OnDropResolved(GlassDropResult result)
        {
            var trueSpillChance = glassManager == null
                ? result.TrueSpillChance
                : glassManager.CurrentTrueSpillChance;

            if (result.Overflowed)
            {
                spillFlashTimer = 1f;
                CreateSpillPuddle();
                waterSpill?.Post(gameObject);
                Debug.Log("[GlassVisualController] Overflow visual triggered: puddle and water surge.");
                return;
            }

            if (trueSpillChance > 0f)
            {
                spillFlashTimer = 0.25f;
                Debug.Log(
                    $"[GlassVisualController] Safe but dangerous drop wobble. " +
                    $"trueSpillChance={trueSpillChance:0.##}%.");
            }
        }

        void ApplyWaterTransform(float surfaceLocalY)
        {
            var danger = glassManager == null
                ? Mathf.Clamp01(currentRisk / GameConstants.MaxOverflowProbability)
                : Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxOverflowProbability);
            var wobble = danger > 0f
                ? Mathf.Sin(Time.time * Mathf.Lerp(8f, 18f, danger)) * dangerWobbleAmplitude * danger
                : 0f;
            var spillPulse = spillFlashTimer > 0f
                ? Mathf.Sin(spillFlashTimer * Mathf.PI * 8f) * 0.018f
                : 0f;

            var ripplePulse = wobble + spillPulse;
            var waterScale = CalculateWaterPulseScale(waterSurfaceScale, ripplePulse);
            var rippleOffset = ripplePulse == 0f
                ? Vector3.zero
                : new Vector3(
                    Mathf.Sin(Time.time * 11f) * ripplePulse * 0.1f,
                    0f,
                    Mathf.Cos(Time.time * 9f) * ripplePulse * 0.1f);
            var waterPosition = new Vector3(
                waterLocalPosition.x,
                surfaceLocalY,
                waterLocalPosition.z) + rippleOffset;

            waterTransform.localScale = waterScale;
            waterTransform.localPosition = waterPosition;
        }

        void CreateSpillPuddle()
        {
            var puddle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            puddle.name = "Temporary Water Spill Puddle";
            puddle.transform.position = new Vector3(transform.position.x, transform.position.y - 0.34f, transform.position.z);
            puddle.transform.localScale = new Vector3(1.08f, 0.008f, 1.08f);

            var collider = puddle.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var renderer = puddle.GetComponent<Renderer>();

            if (renderer != null)
                renderer.sharedMaterial = CreateTransparentWaterMaterial(
                    "Temporary Spill Water Material",
                    new Color(0.16f, 0.42f, 0.62f, 0.22f));

            Destroy(puddle, spillPuddleLifetime);
        }

        void ApplyWaterMaterialPolish()
        {
            var renderer = waterTransform.GetComponent<Renderer>();

            if (renderer == null)
                return;

            renderer.sharedMaterial = CreateTransparentWaterMaterial(
                "Runtime Polished Water Material",
                new Color(0.14f, 0.42f, 0.62f, 0.38f));
        }

        void ApplyWaterMeshPolish()
        {
            var meshFilter = waterTransform.GetComponent<MeshFilter>();

            if (meshFilter == null)
                return;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphereMeshFilter = sphere.GetComponent<MeshFilter>();

            if (sphereMeshFilter != null)
                meshFilter.sharedMesh = sphereMeshFilter.sharedMesh;

            var collider = waterTransform.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            Destroy(sphere);
        }

        Transform CreateRuntimeWaterSurface()
        {
            var waterObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            waterObject.name = "Runtime Water Surface";
            waterObject.transform.SetParent(transform, false);
            waterObject.transform.localPosition = new Vector3(0f, stableSurfaceLocalY, 0f);
            waterObject.transform.localScale = waterSurfaceScale;

            var collider = waterObject.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            Debug.Log("[GlassVisualController] Created runtime water surface.");
            return waterObject.transform;
        }

        static Material CreateTransparentWaterMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = materialName,
                color = color
            };

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        void ResolveReferences()
        {
            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();
        }

        public static Vector3 CalculateWaterPulseScale(Vector3 waterScale, float pulseAmount)
        {
            var pulse = Mathf.Max(0f, pulseAmount);
            return new Vector3(
                waterScale.x + pulse,
                waterScale.y,
                waterScale.z + pulse);
        }

        public static float CalculateWaterSurfaceLocalY(
            float probability,
            float stableSurfaceLocalY,
            float maxRise)
        {
            var riskNormalized = Mathf.Clamp01(probability / GameConstants.MaxOverflowProbability);
            return stableSurfaceLocalY + Mathf.Max(0f, maxRise) * riskNormalized;
        }
    }
}
