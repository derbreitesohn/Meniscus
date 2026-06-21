using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Drives the whiskey inside the glass as a closed liquid body: an animated top surface
    /// (surface-tension meniscus when calm, a convex danger dome as overflow nears, decaying
    /// ripples/slosh after each coin, plus a constant ambient micro-wobble), a cylindrical side
    /// wall visible through the transparent glass, and a bottom. Fill height is baked into the mesh
    /// vertices — not a moving transform — so the whole amber column visibly grows with overflow
    /// risk. The dome is driven by the same spill chance the camera and HUD use, so the liquid
    /// telegraphs whether the next pour overflows. On overflow it spawns a
    /// <see cref="GlassSpillEffect"/> that runs down the glass exterior.
    /// </summary>
    public class GlassVisualController : MonoBehaviour
    {
        [SerializeField] GlassManager glassManager;
        [SerializeField] Transform waterTransform;
        [SerializeField] float stableSurfaceLocalY = 0.62f;
        [SerializeField] float maxRiskSurfaceRise = 0.015f;
        [SerializeField] float riseSpeed = 1.1f;
        [SerializeField] float dangerWobbleAmplitude = 0.008f;
        [SerializeField] float spillPuddleLifetime = 1.4f;

        [Header("Surface Mesh")]
        [SerializeField, Min(0.05f)] float surfaceRadius = 0.39f;
        [SerializeField, Range(2, 64)] int radialRings = 6;
        [SerializeField, Range(3, 96)] int angularSegments = 28;

        [Header("Liquid Body")]
        [SerializeField] float fillBottomLocalY = 0.06f;
        [SerializeField, Range(0.1f, 1f)] float fillBottomRadiusScale = 0.82f;
        [SerializeField, Range(1, 16)] int wallLevels = 5;
        [SerializeField] Color liquidColor = new Color(0.55f, 0.27f, 0.05f, 0.82f);

        [Header("Meniscus")]
        [SerializeField] float meniscusRimClimb = 0.012f;
        [SerializeField, Range(0f, 0.99f)] float meniscusRimStart = 0.62f;
        [SerializeField] float dangerDomeHeight = 0.05f;
        [SerializeField] float dangerTrembleAmplitude = 0.006f;

        [Header("Surface Motion")]
        [SerializeField] float ambientAmplitude = 0.0035f;
        [SerializeField] float ambientSpatialScale = 2.4f;
        [SerializeField] float ambientSpeed = 0.35f;
        [SerializeField] float rippleAmplitude = 0.02f;
        [SerializeField] float rippleWavelength = 26f;
        [SerializeField] float rippleSpeed = 7f;
        [SerializeField] float rippleDecay = 2.6f;
        [SerializeField] float sloshAmplitude = 0.02f;
        [SerializeField] float sloshFrequency = 7.5f;
        [SerializeField] float sloshDecay = 1.8f;

        [Header("Overspill")]
        [SerializeField, Range(1, 12)] int spillRivuletCount = 4;
        [SerializeField] float spillRunDownDuration = 0.4f;
        [SerializeField] float spillSurfaceDip = 0.02f;
        [SerializeField] float spillTableDrop = 0.34f;

        [Header("Audio")]
        [SerializeField] AK.Wwise.Event waterSpill;

        Mesh waterMesh;
        Vector3[] baseVertices;
        Vector3[] workingVertices;
        float[] vertexRadius;
        float[] vertexNx;
        float[] vertexNz;
        LiquidVertexKind[] vertexKind;
        float[] vertexWallT;

        Vector3 waterLocalPosition;
        float currentSurfaceLocalY;
        float targetSurfaceLocalY;
        float currentRisk;
        float spillFlashTimer;

        float rippleKick;
        float rippleStartTime;
        float sloshKick;
        float sloshStartTime;
        Vector2 sloshDirection = Vector2.right;

        void OnEnable()
        {
            ResolveReferences();

            if (waterTransform == null)
                waterTransform = CreateRuntimeWaterSurface();

            SetupSurfaceRendererAndMesh();

            PinWaterTransform();
            currentSurfaceLocalY = stableSurfaceLocalY;
            targetSurfaceLocalY = stableSurfaceLocalY;

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

            if (waterMesh != null)
            {
                Destroy(waterMesh);
                waterMesh = null;
            }
        }

        void Update()
        {
            if (waterTransform == null || waterMesh == null)
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

            UpdateSurfaceVertices();
        }

        /// <summary>
        /// Re-fits the liquid to a glass shape: sets the calm surface height, disc radius, and the
        /// interior floor, then rebuilds the body mesh in place. Safe before or after OnEnable, so
        /// <see cref="GlassPresentationController"/> can call it when switching glass types.
        /// </summary>
        public void ConfigureSurface(float surfaceLocalY, float discRadius, float floorLocalY, float floorRadiusScale)
        {
            stableSurfaceLocalY = surfaceLocalY;
            surfaceRadius = Mathf.Max(0.05f, discRadius);
            fillBottomLocalY = floorLocalY;
            fillBottomRadiusScale = Mathf.Clamp(floorRadiusScale, 0.1f, 1f);

            // Not enabled yet (or no surface): OnEnable will build with the new values.
            if (!isActiveAndEnabled || waterTransform == null)
                return;

            if (waterMesh != null)
            {
                Destroy(waterMesh);
                waterMesh = null;
            }

            SetupSurfaceRendererAndMesh();
            PinWaterTransform();

            currentSurfaceLocalY = stableSurfaceLocalY;
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(currentRisk, stableSurfaceLocalY, maxRiskSurfaceRise);
        }

        void OnProbabilityChanged(float probability)
        {
            currentRisk = probability;
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(probability, stableSurfaceLocalY, maxRiskSurfaceRise);

            if (Mathf.Approximately(probability, 0f))
                currentSurfaceLocalY = targetSurfaceLocalY;
        }

        void OnDropResolved(GlassDropResult result)
        {
            KickRipple(result.Overflowed ? 1.6f : 1f);
            KickSlosh(result.Overflowed ? 1.5f : 1f);

            if (result.Overflowed)
            {
                spillFlashTimer = 1f;
                currentSurfaceLocalY = Mathf.Max(fillBottomLocalY, currentSurfaceLocalY - spillSurfaceDip);
                SpawnSpillEffect();
                waterSpill?.Post(gameObject);
                return;
            }

            var trueSpillChance = glassManager == null
                ? result.TrueSpillChance
                : glassManager.CurrentTrueSpillChance;

            if (trueSpillChance > 0f)
                spillFlashTimer = 0.25f;
        }

        void UpdateSurfaceVertices()
        {
            var t = Time.time;
            var danger = glassManager == null
                ? Mathf.Clamp01(currentRisk / GameConstants.MaxOverflowProbability)
                : Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxOverflowProbability);

            var rippleElapsed = t - rippleStartTime;
            var rippleEnvelope = rippleKick > 0f ? rippleKick * Mathf.Exp(-rippleElapsed * rippleDecay) : 0f;
            var sloshElapsed = t - sloshStartTime;
            var sloshEnvelope = sloshKick > 0f ? sloshKick * Mathf.Exp(-sloshElapsed * sloshDecay) : 0f;
            var sloshPhase = Mathf.Sin(sloshElapsed * sloshFrequency);

            var surfaceBaseY = currentSurfaceLocalY;
            var floorY = Mathf.Min(fillBottomLocalY, surfaceBaseY);

            for (var i = 0; i < workingVertices.Length; i++)
            {
                float y;

                switch (vertexKind[i])
                {
                    case LiquidVertexKind.Wall:
                        y = CalculateWallRingY(floorY, surfaceBaseY, vertexWallT[i]);
                        break;

                    case LiquidVertexKind.BottomCenter:
                        y = floorY;
                        break;

                    default: // CapCenter, Cap
                        var r = vertexRadius[i];
                        var nx = vertexNx[i];
                        var nz = vertexNz[i];

                        // Concave meniscus: liquid clings up the wall while calm, giving way to the dome
                        // as danger rises.
                        var rimT = meniscusRimStart >= 1f
                            ? 0f
                            : Mathf.Clamp01((r - meniscusRimStart) / (1f - meniscusRimStart));
                        var concave = meniscusRimClimb * Mathf.SmoothStep(0f, 1f, rimT) * (1f - danger);

                        // Convex dome: surface tension straining a bulge above the rim as overflow nears.
                        var dome = dangerDomeHeight * danger * Mathf.Clamp01(1f - r * r);

                        var tremble = danger > 0f
                            ? Mathf.Sin(t * Mathf.Lerp(7f, 20f, danger) + (nx + nz) * 5f)
                                * (dangerTrembleAmplitude + dangerWobbleAmplitude) * danger
                            : 0f;

                        var ambient = (Mathf.PerlinNoise(
                            nx * ambientSpatialScale + t * ambientSpeed,
                            nz * ambientSpatialScale - t * ambientSpeed) - 0.5f) * ambientAmplitude;

                        var ripple = rippleEnvelope != 0f
                            ? Mathf.Sin(r * rippleWavelength - rippleElapsed * rippleSpeed)
                                * rippleEnvelope * Mathf.Clamp01(1f - r * 0.15f)
                            : 0f;

                        var slosh = sloshEnvelope != 0f
                            ? (nx * sloshDirection.x + nz * sloshDirection.y) * sloshPhase * sloshEnvelope
                            : 0f;

                        var spill = spillFlashTimer > 0f
                            ? Mathf.Sin(spillFlashTimer * Mathf.PI * 8f) * 0.01f * (1f - r)
                            : 0f;

                        y = surfaceBaseY + concave + dome + tremble + ambient + ripple + slosh + spill;
                        break;
                }

                workingVertices[i] = new Vector3(baseVertices[i].x, y, baseVertices[i].z);
            }

            waterMesh.vertices = workingVertices;
            waterMesh.RecalculateNormals();
            waterMesh.RecalculateBounds();
        }

        void KickRipple(float strength)
        {
            rippleKick = rippleAmplitude * Mathf.Max(0f, strength);
            rippleStartTime = Time.time;
        }

        void KickSlosh(float strength)
        {
            sloshKick = sloshAmplitude * Mathf.Max(0f, strength);
            sloshStartTime = Time.time;

            var angle = Random.Range(0f, Mathf.PI * 2f);
            sloshDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        void SetupSurfaceRendererAndMesh()
        {
            BuildLiquidMesh();

            var collider = waterTransform.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var meshFilter = waterTransform.GetComponent<MeshFilter>();

            if (meshFilter == null)
                meshFilter = waterTransform.gameObject.AddComponent<MeshFilter>();

            meshFilter.sharedMesh = waterMesh;

            var meshRenderer = waterTransform.GetComponent<MeshRenderer>();

            if (meshRenderer == null)
                meshRenderer = waterTransform.gameObject.AddComponent<MeshRenderer>();

            meshRenderer.sharedMaterial = CreateTransparentLiquidMaterial(
                "Runtime Whiskey Liquid Material", liquidColor);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Vertex heights carry the surface detail, so the authored y-squash must not flatten them.
            waterTransform.localScale = Vector3.one;
        }

        void BuildLiquidMesh()
        {
            var floorRadius = surfaceRadius * Mathf.Clamp(fillBottomRadiusScale, 0.1f, 1f);
            var data = LiquidBodyMesh.Build(surfaceRadius, floorRadius, radialRings, angularSegments, wallLevels);

            waterMesh = data.Mesh;
            baseVertices = data.BaseVertices;
            workingVertices = new Vector3[baseVertices.Length];
            System.Array.Copy(baseVertices, workingVertices, baseVertices.Length);
            vertexRadius = data.RadiusNorm;
            vertexNx = data.Nx;
            vertexNz = data.Nz;
            vertexKind = data.Kind;
            vertexWallT = data.WallT;
        }

        void SpawnSpillEffect()
        {
            // The glass sits under a non-uniform scale, so convert the surface radius to world
            // space and let the effect live at identity world scale (it world-roots itself).
            var scale = transform.lossyScale;
            var rimWorldY = transform.TransformPoint(new Vector3(0f, currentSurfaceLocalY, 0f)).y;
            var worldRimRadius = surfaceRadius * Mathf.Max(scale.x, scale.z);

            GlassSpillEffect.Spawn(
                transform.position,
                worldRimRadius,
                rimWorldY,
                transform.position.y - spillTableDrop,
                liquidColor,
                spillRivuletCount,
                spillRunDownDuration,
                spillPuddleLifetime);
        }

        void PinWaterTransform()
        {
            var lp = waterTransform.localPosition;
            waterTransform.localPosition = new Vector3(lp.x, 0f, lp.z);
            waterLocalPosition = waterTransform.localPosition;
        }

        Transform CreateRuntimeWaterSurface()
        {
            var waterObject = new GameObject("Runtime Water Surface", typeof(MeshFilter), typeof(MeshRenderer));
            waterObject.transform.SetParent(transform, false);
            waterObject.transform.localPosition = Vector3.zero;
            waterObject.transform.localScale = Vector3.one;
            return waterObject.transform;
        }

        public static Material CreateTransparentLiquidMaterial(string materialName, Color color)
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
            material.SetFloat("_Smoothness", 0.9f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
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

        public static float CalculateWallRingY(float floorY, float rimY, float wallT)
        {
            return Mathf.Lerp(floorY, rimY, Mathf.Clamp01(wallT));
        }
    }
}
