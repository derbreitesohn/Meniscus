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
    /// telegraphs whether the next pour overflows. As overflow nears the whiskey also deepens from
    /// its near-clear calm state into a hot, throbbing amber — a colour tell pushed through a
    /// <see cref="MaterialPropertyBlock"/> so the authored material is never mutated. On overflow it
    /// spawns a <see cref="GlassSpillEffect"/> that runs down the glass exterior.
    /// </summary>
    [ExecuteAlways]
    public class GlassVisualController : MonoBehaviour
    {
        [SerializeField] GlassManager glassManager;
        [SerializeField] Transform waterTransform;
        // Calm surface height. The live Saloon scene still stores the old value and can't be rewritten
        // while the editor is open, so this is re-applied at play start (see OnEnable) and stays tunable
        // live in the dev panel. Bake into the scene and drop the OnEnable line once it is settled.
        const float DefaultStableSurfaceLocalY = 1.3f;
        [SerializeField] float stableSurfaceLocalY = DefaultStableSurfaceLocalY;
        [SerializeField] float maxRiskSurfaceRise = 0.05f;
        [SerializeField] float riseSpeed = 1.1f;
        [SerializeField] float dangerWobbleAmplitude = 0.008f;
        [SerializeField] float spillPuddleLifetime = 1.8f;

        [Header("Surface Mesh")]
        [SerializeField, Min(0.05f)] float surfaceRadius = 0.39f;
        [SerializeField, Range(2, 64)] int radialRings = 32;
        [SerializeField, Range(3, 96)] int angularSegments = 64;

        [Header("Liquid Body")]
        [SerializeField] float fillBottomLocalY = 0.06f;
        [SerializeField, Range(0.1f, 1f)] float fillBottomRadiusScale = 0.82f;
        [SerializeField, Range(1, 16)] int wallLevels = 5;
        [Tooltip("Authored liquid material (e.g. Water_Surface.mat). When set it is used directly so the look " +
                 "is editor-tunable; left empty, a transparent material is built from Liquid Color at runtime.")]
        [SerializeField] Material liquidMaterial;
        [Tooltip("Fallback tint used when no Liquid Material is assigned, and for the run-down spill rivulets.")]
        [SerializeField] Color liquidColor = new Color(0.55f, 0.27f, 0.05f, 0.82f);

        [Header("Danger Tint")]
        [Tooltip("Hot, saturated colour the whiskey shifts toward as overflow nears. Pushed through a " +
                 "MaterialPropertyBlock, so the authored liquid material is never edited at runtime.")]
        [SerializeField] Color dangerTint = new Color(0.92f, 0.30f, 0.05f, 1f);
        [Tooltip("Surface opacity at full danger. The calm glass keeps its authored (near-clear) alpha; as " +
                 "overflow nears the whiskey deepens toward this, so a brimming glass reads as a heavy amber.")]
        [SerializeField, Range(0f, 1f)] float dangerAlpha = 0.82f;
        [Tooltip("Danger fraction below which the liquid keeps its calm colour. The hot tint ramps in above this.")]
        [SerializeField, Range(0f, 0.95f)] float dangerTintOnset = 0.2f;
        [Tooltip("Throb speed of the hot tint when the glass is near overflow.")]
        [SerializeField, Min(0f)] float dangerPulseSpeed = 8f;
        [Tooltip("How hard the hot tint throbs at full danger (0 = steady glow, 1 = strong pulse).")]
        [SerializeField, Range(0f, 1f)] float dangerPulseStrength = 0.35f;

        [Header("Meniscus")]
        [SerializeField] float meniscusRimClimb = 0.05f;
        [SerializeField, Range(0f, 0.99f)] float meniscusRimStart = 0.5f;
        [SerializeField] float dangerDomeHeight = 0.14f;
        [SerializeField] float dangerTrembleAmplitude = 0.006f;

        [Header("Surface Motion")]
        [SerializeField] float ambientAmplitude = 0.0035f;
        [SerializeField] float ambientSpatialScale = 2.4f;
        [SerializeField] float ambientSpeed = 0.35f;
        [SerializeField] float rippleAmplitude = 0.035f;
        [SerializeField] float rippleWavelength = 22f;
        [SerializeField] float rippleSpeed = 9f;
        [SerializeField] float rippleDecay = 1.9f;
        [SerializeField] float sloshAmplitude = 0.035f;
        [SerializeField] float sloshFrequency = 9.5f;
        [SerializeField] float sloshDecay = 1.3f;

        [Header("Overspill")]
        [SerializeField, Range(1, 12)] int spillRivuletCount = 7;
        [SerializeField] float spillRunDownDuration = 0.6f;
        [SerializeField] float spillSurfaceDip = 0.02f;
        [SerializeField] float spillTableDrop = 0.34f;
        [Tooltip("Authored spill prefab (built by Tools > Meniscus > Author Spill Effect Prefab). When set " +
                 "it is instantiated per spill; left empty, a runtime overspill object is built instead.")]
        [SerializeField] GlassSpillEffect spillPrefab;

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
        bool frozen;                 // held at the peak spill frame during the loss orbit (see FreezeAtMaxSpill)
        GlassSpillEffect heldSpill;  // the persistent loss spill effect; destroyed on Thaw

        // --- Procedural ripple field (see KickRipple / UpdateSurfaceVertices) ---
        // Each coin impact spawns one decaying, radially-travelling sine wave. Several can be alive at
        // once and their heights are summed (linear superposition), so overlapping rings interfere -
        // reinforcing where crests meet, cancelling where a crest meets a trough - instead of one kick
        // simply replacing the last. A small round-robin buffer caps the live count; a new kick past
        // the cap overwrites the oldest (and faintest) wave.
        struct RippleWave
        {
            public float StartTime; // when this wave was kicked (seconds)
            public float Amplitude; // initial crest height before time decay
        }

        const int MaxConcurrentRipples = 6;
        readonly RippleWave[] rippleWaves = new RippleWave[MaxConcurrentRipples];
        readonly float[] rippleElapsedScratch = new float[MaxConcurrentRipples]; // per-frame age cache
        readonly float[] rippleEnvelopeScratch = new float[MaxConcurrentRipples]; // per-frame decayed amplitude
        int nextRippleSlot;

        float sloshKick;
        float sloshStartTime;
        Vector2 sloshDirection = Vector2.right;
        bool needsRebuild;

        MeshRenderer waterRenderer;
        MaterialPropertyBlock liquidMpb;
        Color calmBaseColor = new Color(0.55f, 0.27f, 0.05f, 0f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Local-space height of the calm liquid surface (top of the whiskey column).</summary>
        public float StableSurfaceLocalY => stableSurfaceLocalY;

        /// <summary>Root of the runtime liquid body (the whiskey mesh); kept visible during the loss orbit.</summary>
        public Transform WaterTransform => waterTransform;

        /// <summary>Local-space height of the interior floor the liquid - and dropped coins - rest on.</summary>
        public float FloorLocalY => fillBottomLocalY;

        /// <summary>Local radius of the liquid surface disc.</summary>
        public float SurfaceLocalRadius => surfaceRadius;

        /// <summary>Fraction of <see cref="SurfaceLocalRadius"/> the interior floor spans.</summary>
        public float FloorRadiusScale => fillBottomRadiusScale;

        // How far the surface climbs from calm to a maxed meter. Floored so the rising whiskey reads
        // clearly and lifts toward the rim even while the scene still stores the old, smaller authored
        // value. (Drop the floor once a value is baked into the scene.)
        float RiskSurfaceRise => Mathf.Max(maxRiskSurfaceRise, 0.045f);

        void OnEnable()
        {
            ResolveReferences();

            // DEV: re-apply the dev surface height at play start; the open scene still serializes the old
            // value and can't be rewritten while the editor is open. Still tunable live in the dev panel.
            // Remove this once the value is baked into the scene.
            if (Application.isPlaying)
                stableSurfaceLocalY = DefaultStableSurfaceLocalY;

            if (waterTransform == null)
                waterTransform = WaterSurfaceBuilder.Build(transform, WaterSurfaceBuilder.DefaultName);

            SetupSurfaceRendererAndMesh();

            PinWaterTransform();
            currentSurfaceLocalY = stableSurfaceLocalY;
            targetSurfaceLocalY = stableSurfaceLocalY;

            // Lay out the rest pose immediately so the liquid is visible in the editor (not just at runtime).
            UpdateSurfaceVertices();

            // Game-only wiring: don't hook gameplay events while editing. The surface-rise telegraph follows
            // ProbabilityChanged; the reactive beats (ripple/slosh/spill) are driven explicitly by the drop
            // presentation conductor so they fire when the coin actually hits the water, not at commit time.
            if (Application.isPlaying && glassManager != null)
            {
                glassManager.ProbabilityChanged += OnProbabilityChanged;
                OnProbabilityChanged(glassManager.CurrentOverflowProbability);
            }
        }

        void OnDisable()
        {
            if (glassManager != null)
                glassManager.ProbabilityChanged -= OnProbabilityChanged;

            SafeDestroy(waterMesh);
            waterMesh = null;
        }

        // Inspector tweak while editing -> rebuild the rest pose so changes show live (handled in Update,
        // since OnValidate must not create/destroy objects directly).
        void OnValidate()
        {
            if (!Application.isPlaying)
                needsRebuild = true;
        }

        void Update()
        {
            if (!Application.isPlaying)
            {
                if (needsRebuild || waterMesh == null)
                {
                    needsRebuild = false;
                    RebuildLiquid();
                }

                return; // edit mode shows a static rest pose; no per-frame animation
            }

            if (waterTransform == null || waterMesh == null)
                return;

            if (frozen)
                return;   // held at the peak spill frame (FreezeAtMaxSpill) until Thaw on restart

            // Stable Surface Local Y is the authoritative calm-surface height (tunable live in the dev
            // panel). Recompute the target from it each frame so an edit animates in immediately; the
            // glass rim auto-fit no longer moves it (see ConfigureSurface).
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(currentRisk, stableSurfaceLocalY, RiskSurfaceRise);

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
            ApplyDangerTint(CurrentDangerNormalized());
        }

        // Rebuild the liquid body from the current serialized parameters and re-lay the rest pose. Used in the
        // editor so adjusting fields updates the visible mesh.
        void RebuildLiquid()
        {
            if (waterTransform == null)
                return;

            SafeDestroy(waterMesh);
            waterMesh = null;

            SetupSurfaceRendererAndMesh();
            PinWaterTransform();
            currentSurfaceLocalY = stableSurfaceLocalY;
            targetSurfaceLocalY = stableSurfaceLocalY;
            UpdateSurfaceVertices();
        }

        static void SafeDestroy(Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }

        /// <summary>
        /// Re-fits the liquid to a glass shape: sets the calm surface height, disc radius, and the
        /// interior floor, then rebuilds the body mesh in place. Safe before or after OnEnable, so
        /// <see cref="GlassPresentationController"/> can call it when switching glass types.
        /// </summary>
        public void ConfigureSurface(float surfaceLocalY, float discRadius, float floorLocalY, float floorRadiusScale)
        {
            // Surface height is driven by stableSurfaceLocalY directly (dev-tunable in the panel), so the
            // glass rim auto-fit no longer overrides it — it only fits the disc radius and interior floor.
            surfaceRadius = Mathf.Max(0.05f, discRadius);
            fillBottomLocalY = floorLocalY;
            fillBottomRadiusScale = Mathf.Clamp(floorRadiusScale, 0.1f, 1f);

            // Not enabled yet (or no surface): OnEnable will build with the new values.
            if (!isActiveAndEnabled || waterTransform == null)
                return;

            SafeDestroy(waterMesh);
            waterMesh = null;

            SetupSurfaceRendererAndMesh();
            PinWaterTransform();

            currentSurfaceLocalY = stableSurfaceLocalY;
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(currentRisk, stableSurfaceLocalY, RiskSurfaceRise);

            // Reflect the new fit immediately (matters in the editor, where Update doesn't animate).
            UpdateSurfaceVertices();
        }

        void OnProbabilityChanged(float probability)
        {
            frozen = false;   // a probability change means the glass is live again (e.g. a round reset)
            currentRisk = probability;
            targetSurfaceLocalY = CalculateWaterSurfaceLocalY(probability, stableSurfaceLocalY, RiskSurfaceRise);

            if (Mathf.Approximately(probability, 0f))
                currentSurfaceLocalY = targetSurfaceLocalY;
        }

        /// <summary>
        /// Overflow reveal: flash the surface, dip the level, run the spill rivulets down the glass and play
        /// the spill sound. Called by the drop presentation conductor at the dramatic reveal beat (after the
        /// suspense), not the instant coins are committed.
        /// </summary>
        public void PlaySpill()
        {
            spillFlashTimer = 1f;

            // The surface lurches hard as the liquid tips over the rim; big reactive kicks sell the heave,
            // then the dip drops the body to a lower level once it has poured out.
            KickRipple(2.6f);
            KickSlosh(2.2f);
            currentSurfaceLocalY = Mathf.Max(fillBottomLocalY, currentSurfaceLocalY - spillSurfaceDip);

            SpawnSpillEffect();
            SpawnSpillSplash();
            waterSpill?.Post(gameObject);
        }

        /// <summary>
        /// Hold the glass at its most-overflowed look for the loss screen: snap the body to its brim, paint
        /// one peak frame (full danger dome + hot tint), stop animating, and spawn a spill that runs to full
        /// and then stays. <see cref="Thaw"/> reverses it when the match restarts.
        /// </summary>
        public void FreezeAtMaxSpill()
        {
            if (!Application.isPlaying || frozen)
                return;

            // Snap the body up to its current brim target (undo any post-pour dip) and paint one peak frame.
            spillFlashTimer = 0f;
            currentSurfaceLocalY = targetSurfaceLocalY;

            if (waterMesh != null)
                UpdateSurfaceVertices();

            ApplyDangerTint(CurrentDangerNormalized());
            frozen = true;

            // A held spill: rivulets run to full and then stay (no fade, no self-destruct) until Thaw.
            heldSpill = SpawnSpillEffect(hold: true);
        }

        /// <summary>Resume animation and clear the held loss spill (called when the match restarts).</summary>
        public void Thaw()
        {
            frozen = false;

            if (heldSpill != null)
            {
                Destroy(heldSpill.gameObject);
                heldSpill = null;
            }
        }

        void UpdateSurfaceVertices()
        {
            // In the editor (not playing) show a calm, static rest pose so the shape is easy to judge and tweak;
            // the time-driven motion (ambient/ripple/slosh/dome/spill) only runs at runtime.
            var playing = Application.isPlaying;
            var t = playing ? Time.time : 0f;
            var danger = !playing
                ? 0f
                : glassManager == null
                    ? Mathf.Clamp01(currentRisk / GameConstants.MaxOverflowProbability)
                    : Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxOverflowProbability);

            // Precompute every active ripple's age and time-decayed amplitude once per frame, so the
            // per-vertex loop below only evaluates the cheap spatial term (sin of distance) and sums.
            for (var k = 0; k < rippleWaves.Length; k++)
            {
                var elapsed = t - rippleWaves[k].StartTime;
                rippleElapsedScratch[k] = elapsed;
                rippleEnvelopeScratch[k] = rippleWaves[k].Amplitude > 0f
                    ? rippleWaves[k].Amplitude * Mathf.Exp(-elapsed * rippleDecay)
                    : 0f;
            }

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

                        // Convex crown: the whiskey mounds up and stands proud of the rim as overflow nears
                        // — an over-filled glass held together by surface tension. Driven across the full
                        // spill-chance range (like the colour tell) so it climbs the whole way instead of
                        // topping out half-way, with a bold height floor so the bulge clearly stands out of
                        // the glass even while the scene still stores the old, smaller authored value.
                        var crownDanger = glassManager == null
                            ? danger
                            : Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxSpillChance);
                        var crownHeight = Mathf.Max(dangerDomeHeight, 0.13f);
                        var dome = crownHeight * crownDanger * Mathf.Clamp01(1f - r * r);

                        var tremble = danger > 0f
                            ? Mathf.Sin(t * Mathf.Lerp(7f, 20f, danger) + (nx + nz) * 5f)
                                * (dangerTrembleAmplitude + dangerWobbleAmplitude) * danger
                            : 0f;

                        var ambient = playing
                            ? (Mathf.PerlinNoise(
                                nx * ambientSpatialScale + t * ambientSpeed,
                                nz * ambientSpatialScale - t * ambientSpeed) - 0.5f) * ambientAmplitude
                            : 0f;

                        // Superpose every live ripple: each contributes a travelling sine wave whose
                        // phase is (distance * wavenumber - age * speed). Summing the active waves makes
                        // overlapping rings interfere. The radial falloff keeps the rim a touch calmer.
                        var ripple = 0f;
                        var rippleFalloff = Mathf.Clamp01(1f - r * 0.15f);
                        for (var k = 0; k < rippleWaves.Length; k++)
                        {
                            if (rippleEnvelopeScratch[k] <= 0.0001f)
                                continue;

                            ripple += Mathf.Sin(r * rippleWavelength - rippleElapsedScratch[k] * rippleSpeed)
                                * rippleEnvelopeScratch[k] * rippleFalloff;
                        }

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

        /// <summary>
        /// Spawns a new radial ripple from a coin impact. Added to the live set rather than replacing
        /// the previous one, so rapid drops build an interfering wave field (see <see cref="RippleWave"/>).
        /// </summary>
        public void KickRipple(float strength)
        {
            rippleWaves[nextRippleSlot] = new RippleWave
            {
                StartTime = Time.time,
                Amplitude = rippleAmplitude * Mathf.Max(0f, strength)
            };
            nextRippleSlot = (nextRippleSlot + 1) % rippleWaves.Length;
        }

        public void KickSlosh(float strength)
        {
            sloshKick = sloshAmplitude * Mathf.Max(0f, strength);
            sloshStartTime = Time.time;

            var angle = Random.Range(0f, Mathf.PI * 2f);
            sloshDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        // Danger normalised 0..1. Unlike the dome/tremble (which scale against the fill meter and so
        // top out around half), the colour tell scales against the spill-chance ceiling, so it spans
        // its full range as the real overflow odds climb to the asymptote.
        float CurrentDangerNormalized()
        {
            if (!Application.isPlaying)
                return 0f;

            return glassManager == null
                ? Mathf.Clamp01(currentRisk / GameConstants.MaxOverflowProbability)
                : Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxSpillChance);
        }

        void CaptureCalmBaseColor()
        {
            var material = waterRenderer != null ? waterRenderer.sharedMaterial : null;

            if (material == null)
                return;

            if (material.HasProperty(BaseColorId))
                calmBaseColor = material.GetColor(BaseColorId);
            else if (material.HasProperty(ColorId))
                calmBaseColor = material.GetColor(ColorId);
            else
                calmBaseColor = liquidColor;
        }

        // Shift the whiskey from its authored calm colour toward the hot danger tint as overflow nears,
        // raising opacity (the calm glass is near-clear) and throbbing near the brim. Driven entirely
        // through a MaterialPropertyBlock so the shared authored material is never mutated.
        void ApplyDangerTint(float danger01)
        {
            if (waterRenderer == null)
                return;

            var tintT = dangerTintOnset >= 1f
                ? 0f
                : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((danger01 - dangerTintOnset) / (1f - dangerTintOnset)));

            // Throb only once in the danger band, and only at runtime (the editor shows a steady rest pose).
            var pulse = Application.isPlaying && tintT > 0f
                ? 1f + dangerPulseStrength * tintT * Mathf.Sin(Time.time * dangerPulseSpeed)
                : 1f;

            var rgb = Color.Lerp(calmBaseColor, dangerTint, tintT);
            var alpha = Mathf.Clamp01(Mathf.Lerp(calmBaseColor.a, dangerAlpha, tintT) * pulse);
            var tinted = new Color(rgb.r, rgb.g, rgb.b, alpha);

            liquidMpb ??= new MaterialPropertyBlock();
            waterRenderer.GetPropertyBlock(liquidMpb);
            liquidMpb.SetColor(BaseColorId, tinted);
            liquidMpb.SetColor(ColorId, tinted);
            waterRenderer.SetPropertyBlock(liquidMpb);
        }

        void SetupSurfaceRendererAndMesh()
        {
            BuildLiquidMesh();

            var collider = waterTransform.GetComponent<Collider>();

            if (collider != null)
                SafeDestroy(collider);

            var meshFilter = waterTransform.GetComponent<MeshFilter>();

            if (meshFilter == null)
                meshFilter = waterTransform.gameObject.AddComponent<MeshFilter>();

            meshFilter.sharedMesh = waterMesh;

            var meshRenderer = waterTransform.GetComponent<MeshRenderer>();

            if (meshRenderer == null)
                meshRenderer = waterTransform.gameObject.AddComponent<MeshRenderer>();

            // Prefer the authored material asset (editor-tunable, no per-play allocation). Only build one in
            // code when none is assigned — e.g. the fully runtime-created water object.
            meshRenderer.sharedMaterial = liquidMaterial != null
                ? liquidMaterial
                : CreateTransparentLiquidMaterial("Runtime Whiskey Liquid Material", liquidColor);
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Vertex heights carry the surface detail, so the authored y-squash must not flatten them.
            waterTransform.localScale = Vector3.one;

            // Cache the renderer and the authored calm colour, then lay down the calm tint so the rest
            // pose matches the material exactly (no visible change until overflow risk climbs).
            waterRenderer = meshRenderer;
            CaptureCalmBaseColor();
            ApplyDangerTint(0f);
        }

        void BuildLiquidMesh()
        {
            var floorRadius = surfaceRadius * Mathf.Clamp(fillBottomRadiusScale, 0.1f, 1f);

            // Tessellate finely enough to actually resolve the ripple wave. With too few radial rings the
            // crests step from ring to ring, which reads as a jittery, "laggy" shake rather than a smooth
            // travelling wave. Require ~6 rings per ripple cycle across the radius (comfortably above the
            // Nyquist minimum) so a short wavelength stays smooth regardless of the authored ring count.
            var ripplesAcrossRadius = Mathf.Max(1f, rippleWavelength / (2f * Mathf.PI));
            var rings = Mathf.Max(radialRings, Mathf.CeilToInt(ripplesAcrossRadius * 6f), 24);
            var segments = Mathf.Max(angularSegments, 56);
            var data = LiquidBodyMesh.Build(surfaceRadius, floorRadius, rings, segments, wallLevels);

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

        GlassSpillEffect SpawnSpillEffect(bool hold = false)
        {
            // The glass sits under a non-uniform scale, so convert the surface radius to world
            // space and let the effect live at identity world scale (it world-roots itself).
            var scale = transform.lossyScale;
            var rimWorldY = transform.TransformPoint(new Vector3(0f, currentSurfaceLocalY, 0f)).y;
            var worldRimRadius = surfaceRadius * Mathf.Max(scale.x, scale.z);

            if (spillPrefab != null)
            {
                var effect = Instantiate(spillPrefab);
                effect.Begin(
                    transform.position,
                    worldRimRadius,
                    rimWorldY,
                    transform.position.y - spillTableDrop,
                    liquidColor,
                    spillRivuletCount,
                    spillRunDownDuration,
                    spillPuddleLifetime,
                    hold);
                return effect;
            }

            return GlassSpillEffect.Spawn(
                transform.position,
                worldRimRadius,
                rimWorldY,
                transform.position.y - spillTableDrop,
                liquidColor,
                spillRivuletCount,
                spillRunDownDuration,
                spillPuddleLifetime,
                hold);
        }

        // Droplets flung off the rim as the liquid breaches it — reuses the coin-impact splash burst so an
        // overflow throws amber, not just runs down. Runtime only (the burst builds GameObjects).
        void SpawnSpillSplash()
        {
            if (!Application.isPlaying)
                return;

            var scale = transform.lossyScale;
            var rimWorldY = transform.TransformPoint(new Vector3(0f, currentSurfaceLocalY, 0f)).y;
            var worldRimRadius = surfaceRadius * Mathf.Max(scale.x, scale.z);
            var rimCenter = new Vector3(transform.position.x, rimWorldY, transform.position.z);

            ParticleBurst.SpawnSplash(rimCenter, worldRimRadius, liquidColor, 1.8f);
        }

        void PinWaterTransform()
        {
            var lp = waterTransform.localPosition;
            waterTransform.localPosition = new Vector3(lp.x, 0f, lp.z);
            waterLocalPosition = waterTransform.localPosition;
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

            // The liquid body is a closed solid (cap + walls + bottom). Rendering it two-sided with
            // ZWrite off lets the back/inner faces blend through the front, so the amber reads as a
            // hollow "cup" sitting inside the glass. Writing depth and culling back faces keeps only
            // the nearest front surface, so it reads as a solid whiskey column.
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_ZWriteControl", 1f); // URP: 1 = ForceEnabled (don't let auto override it)
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            material.SetFloat("_Smoothness", 0.9f);
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
