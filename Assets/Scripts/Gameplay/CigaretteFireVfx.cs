using System.Collections;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// The fire, ember and smoke for lighting the Tschick (the Steady Hand item) — built entirely from
    /// primitives and runtime materials, no authored particle asset, to match the project's
    /// <see cref="ParticleBurst"/> style. It lives parented at the cigarette tip (so it follows the held
    /// prop) and is driven by <see cref="ItemUsePresentationController.PlaySteadyHandSmoke"/>:
    ///   • <see cref="Strike"/> flares the lighter — a quick burst of sparks, a flickering flame and a warm
    ///     light flash — and ramps the tip ember in.
    ///   • <see cref="SetEmber"/> / <see cref="Puff"/> carry the drag and the exhale (the ember brightens on
    ///     the pull, a cloud of smoke billows out).
    ///   • <see cref="BeginSmoke"/> starts a thin idle wisp rising off the tip while it's lit.
    ///   • <see cref="Extinguish"/> fades the ember and light out and self-destructs; smoke wisps are world
    ///     objects that own their own lifetime, so lingering smoke keeps drifting and clears on its own.
    /// </summary>
    public class CigaretteFireVfx : MonoBehaviour
    {
        static readonly Color SparkColor = new(1f, 0.66f, 0.22f, 1f);   // bright lighter sparks (additive)
        static readonly Color FlameColor = new(1f, 0.74f, 0.30f, 1f);   // the lighter flame
        static readonly Color EmberColor = new(1f, 0.42f, 0.12f, 1f);   // the glowing cherry at the tip
        static readonly Color SmokeColor = new(0.70f, 0.68f, 0.66f, 1f); // wisps (alpha set per wisp)
        static readonly Color WarmLight = new(1f, 0.64f, 0.34f);        // the light it throws on the face

        const float EmberDiameter = 0.013f;     // the cherry's size at full glow (metres)
        const float StrikeLightIntensity = 4.2f;
        const float EmberLightIntensity = 0.7f;
        const float LightRange = 0.55f;

        Material sparkMat;
        Material flameMat;
        Material emberMat;

        Transform ember;
        Renderer emberRenderer;
        Light glow;
        float emberLevel;
        bool striking;
        bool smoking;

        /// <summary>Spawn the VFX as a child of <paramref name="parent"/> (the cigarette pivot) at the tip.</summary>
        public static CigaretteFireVfx Create(Transform parent, Vector3 localTipPos)
        {
            var host = new GameObject("Cigarette Fire VFX");

            if (parent != null)
                host.transform.SetParent(parent, false);

            host.transform.localPosition = localTipPos;

            var vfx = host.AddComponent<CigaretteFireVfx>();
            vfx.Build();
            return vfx;
        }

        void Build()
        {
            sparkMat = CreateAdditiveMaterial("Tschick Spark", SparkColor);
            flameMat = CreateAdditiveMaterial("Tschick Flame", FlameColor);
            emberMat = CreateAdditiveMaterial("Tschick Ember", EmberColor);

            var emberObject = MakeSphere("Cigarette Ember", emberMat, EmberDiameter, transform);
            ember = emberObject.transform;
            emberRenderer = emberObject.GetComponent<Renderer>();

            var lightObject = new GameObject("Ember Light");
            lightObject.transform.SetParent(transform, false);
            glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = WarmLight;
            glow.range = LightRange;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;

            SetEmber(0f);
        }

        /// <summary>
        /// The strike of the lighter: a spark burst flies off the tip, a small flame flickers for a beat and
        /// a warm light flashes and settles, and the ember rises in. Yields for <paramref name="seconds"/>.
        /// </summary>
        public IEnumerator Strike(float seconds)
        {
            seconds = Mathf.Max(0.12f, seconds);
            striking = true;

            SpawnSparkBurst(14);
            StartCoroutine(FlickerFlame(seconds));

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var f = Mathf.Clamp01(t / seconds);
                SetEmber(Mathf.SmoothStep(0f, 1f, f));   // the cherry catches over the strike

                // The light flashes hard at the strike, then settles toward the steady ember glow, flickering.
                var flick = 0.6f + 0.4f * Mathf.PerlinNoise(t * 34f, 0.7f);
                glow.intensity = Mathf.Lerp(StrikeLightIntensity, EmberLightIntensity, f) * flick;
                yield return null;
            }

            SetEmber(1f);
            glow.intensity = EmberLightIntensity;
            striking = false;
        }

        /// <summary>Set the ember glow, 0 (out) .. 1 (full cherry). Drives the tip sphere and the warm light.</summary>
        public void SetEmber(float level)
        {
            emberLevel = Mathf.Clamp01(level);

            if (emberRenderer != null)
                SetColor(emberRenderer, EmberColor * (0.12f + 0.88f * emberLevel));

            if (ember != null)
                ember.localScale = Vector3.one * (EmberDiameter * (0.65f + 0.35f * emberLevel));

            if (glow != null && !striking)
                glow.intensity = EmberLightIntensity * emberLevel;
        }

        /// <summary>Start a thin wisp of smoke rising off the tip while the cigarette is lit.</summary>
        public void BeginSmoke()
        {
            if (smoking)
                return;

            smoking = true;
            StartCoroutine(SmokeTrail());
        }

        /// <summary>Exhale: a cloud of smoke billows out and up from the tip. Strength scales count and size.</summary>
        public void Puff(float strength)
        {
            var clamped = Mathf.Clamp(strength, 0.25f, 3f);
            var count = Mathf.RoundToInt(7f * clamped) + 4;
            var cam = Camera.main != null ? Camera.main.transform : null;
            var toward = cam != null ? -cam.forward : Vector3.forward;   // billows back toward the player POV

            for (var i = 0; i < count; i++)
            {
                var spread = Random.insideUnitSphere * 0.5f;
                var vel = (Vector3.up * Random.Range(0.16f, 0.26f)
                           + toward * Random.Range(0.05f, 0.13f)
                           + spread * 0.12f) * clamped;
                EmitWisp(
                    transform.position + spread * 0.01f,
                    vel,
                    life: Random.Range(1.7f, 2.7f),
                    startDiameter: 0.02f * clamped,
                    endDiameter: Random.Range(0.10f, 0.16f) * clamped,
                    peakAlpha: Random.Range(0.16f, 0.24f));
            }
        }

        /// <summary>Fade the ember and light out over <paramref name="seconds"/>, then self-destruct.</summary>
        public IEnumerator Extinguish(float seconds)
        {
            smoking = false;
            seconds = Mathf.Max(0.05f, seconds);
            var startLevel = emberLevel;

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                SetEmber(Mathf.Lerp(startLevel, 0f, t / seconds));
                yield return null;
            }

            SetEmber(0f);

            if (glow != null)
                glow.intensity = 0f;

            // Smoke wisps own their own lifetime (world objects), so they keep drifting after we're gone.
            Destroy(gameObject);
        }

        void Update()
        {
            // A live cherry breathes a little even when it isn't being drawn on.
            if (!striking && emberLevel > 0f && emberRenderer != null)
            {
                var flick = 0.86f + 0.14f * Mathf.PerlinNoise(Time.time * 6f, 0.2f);
                SetColor(emberRenderer, EmberColor * (0.12f + 0.88f * emberLevel) * flick);
            }
        }

        // ── lighter flame + sparks ──────────────────────────────────────────────────

        // A couple of small additive blobs at the tip that jitter and flicker for the strike, then fade out.
        IEnumerator FlickerFlame(float seconds)
        {
            var core = MakeSphere("Flame Core", flameMat, 0.02f, transform);
            var tip = MakeSphere("Flame Tip", flameMat, 0.012f, transform);
            tip.transform.localPosition = new Vector3(0f, 0.014f, 0f);
            var coreRenderer = core.GetComponent<Renderer>();
            var tipRenderer = tip.GetComponent<Renderer>();

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var f = Mathf.Clamp01(t / seconds);
                var fade = 1f - f * f;                                   // burns down over the strike
                var flick = 0.7f + 0.3f * Mathf.PerlinNoise(t * 40f, 1.3f);
                var wobble = Mathf.Sin(t * 50f) * 0.004f;

                core.transform.localScale = Vector3.one * (0.02f * (0.8f + 0.5f * flick));
                core.transform.localPosition = new Vector3(wobble, 0.004f * flick, 0f);
                tip.transform.localScale = Vector3.one * (0.012f * flick);
                tip.transform.localPosition = new Vector3(-wobble, 0.014f + 0.006f * flick, 0f);

                SetColor(coreRenderer, FlameColor * (fade * flick));
                SetColor(tipRenderer, SparkColor * (fade * flick));
                yield return null;
            }

            Destroy(core);
            Destroy(tip);
        }

        // A one-shot ballistic burst of bright sparks off the tip, faded together and self-destructed —
        // a miniature ParticleBurst kept in the tip's local space so it sits on the held cigarette.
        void SpawnSparkBurst(int count)
        {
            var burst = new GameObject("Spark Burst");
            burst.transform.SetParent(transform, false);
            burst.AddComponent<SparkBurst>().Launch(burst, sparkMat, count);
        }

        // ── smoke ───────────────────────────────────────────────────────────────────

        // A slow wisp every so often while lit — a thin idle trail off the cherry.
        IEnumerator SmokeTrail()
        {
            while (smoking)
            {
                EmitWisp(
                    transform.position,
                    Vector3.up * Random.Range(0.10f, 0.16f) + Random.insideUnitSphere * 0.02f,
                    life: Random.Range(1.6f, 2.4f),
                    startDiameter: 0.012f,
                    endDiameter: Random.Range(0.05f, 0.085f),
                    peakAlpha: Random.Range(0.10f, 0.16f));

                var wait = Random.Range(0.2f, 0.34f);
                for (var t = 0f; t < wait && smoking; t += Time.deltaTime)
                    yield return null;
            }
        }

        // A single smoke puff: a soft grey sphere that rises, swells and fades, owning its own lifetime so it
        // outlives this VFX (world-parented). Each wisp gets its own material, destroyed with it.
        void EmitWisp(Vector3 worldPos, Vector3 velocity, float life, float startDiameter, float endDiameter, float peakAlpha)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Smoke Wisp";

            var wispCollider = go.GetComponent<Collider>();
            if (wispCollider != null)
                Destroy(wispCollider);

            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * startDiameter;

            var mat = CreateSmokeMaterial("Tschick Smoke");
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            go.AddComponent<SmokeWisp>().Launch(mat, velocity, life, startDiameter, endDiameter, peakAlpha, SmokeColor);
        }

        // ── primitives + materials ──────────────────────────────────────────────────

        static GameObject MakeSphere(string name, Material material, float diameter, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;

            var sphereCollider = go.GetComponent<Collider>();
            if (sphereCollider != null)
                Destroy(sphereCollider);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * diameter;

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // Per-renderer colour tint via a shared block — no per-piece material instance, no EditMode material
        // leak (these only ever run in play mode). For additive pieces, fading = driving the colour to black.
        static MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }

        // A bright additive material (URP-safe): adds its colour over the scene so it reads as glowing fire,
        // and a runtime primitive's default no longer renders magenta under URP. Mirrors the explicit blend
        // setup in GlassVisualController.CreateTransparentLiquidMaterial.
        static Material CreateAdditiveMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = materialName, color = color };

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);   // Transparent

            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 2f);      // Additive

            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);

            if (material.HasProperty("_ZWriteControl"))
                material.SetFloat("_ZWriteControl", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        // A soft alpha-blended grey for the smoke (ZWrite off so wisps blend through one another).
        static Material CreateSmokeMaterial(string materialName)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = materialName };

            var clear = new Color(SmokeColor.r, SmokeColor.g, SmokeColor.b, 0f);
            material.color = clear;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", clear);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);      // Alpha

            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);

            if (material.HasProperty("_ZWriteControl"))
                material.SetFloat("_ZWriteControl", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        void OnDestroy()
        {
            if (sparkMat != null) Destroy(sparkMat);
            if (flameMat != null) Destroy(flameMat);
            if (emberMat != null) Destroy(emberMat);
        }
    }

    /// <summary>A short ballistic burst of bright spark pieces in its host's local space; fades and self-destructs.</summary>
    public class SparkBurst : MonoBehaviour
    {
        struct Spark
        {
            public Transform Transform;
            public Vector3 Velocity;
        }

        Spark[] sparks;
        Renderer[] renderers;
        Color baseColor;
        float lifetime;

        public void Launch(GameObject host, Material material, int count)
        {
            count = Mathf.Clamp(count, 4, 40);
            baseColor = material != null ? material.color : Color.white;
            lifetime = 0.55f;
            sparks = new Spark[count];
            renderers = new Renderer[count];

            for (var i = 0; i < count; i++)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                piece.name = "Spark";

                var pieceCollider = piece.GetComponent<Collider>();
                if (pieceCollider != null)
                    Destroy(pieceCollider);

                piece.transform.SetParent(host.transform, false);
                piece.transform.localPosition = Vector3.zero;
                piece.transform.localScale = Vector3.one * Random.Range(0.0035f, 0.007f);

                var renderer = piece.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderers[i] = renderer;

                // Mostly up-and-out in a tight cone, like sparks off a flint wheel.
                var radial = ParticleBurst.RadialDirection(i, count, Random.value);
                var vel = Vector3.up * Random.Range(0.5f, 1.0f) + radial * Random.Range(0.15f, 0.5f);
                sparks[i] = new Spark { Transform = piece.transform, Velocity = vel };
            }

            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            for (var elapsed = 0f; elapsed < lifetime; elapsed += Time.deltaTime)
            {
                var life = Mathf.Clamp01(elapsed / lifetime);

                for (var i = 0; i < sparks.Length; i++)
                {
                    if (sparks[i].Transform == null)
                        continue;

                    sparks[i].Velocity += Vector3.down * (2.2f * Time.deltaTime);   // light gravity in local space
                    sparks[i].Transform.localPosition += sparks[i].Velocity * Time.deltaTime;
                }

                // Additive sparks fade by burning down to black over the back half of their life.
                var k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((life - 0.4f) / 0.6f));
                SetColor(baseColor * k);
                yield return null;
            }

            Destroy(gameObject);
        }

        static MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void SetColor(Color color)
        {
            block ??= new MaterialPropertyBlock();

            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                renderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                renderers[i].SetPropertyBlock(block);
            }
        }
    }

    /// <summary>
    /// A single drifting smoke puff that owns its own lifetime, so it keeps rising and fading after the
    /// <see cref="CigaretteFireVfx"/> that spawned it is gone. Rises with a little buoyancy and drag, swells,
    /// fades in then out, and destroys itself and its material.
    /// </summary>
    public class SmokeWisp : MonoBehaviour
    {
        Material material;
        Renderer rend;
        Vector3 velocity;
        Color baseColor;
        float life;
        float age;
        float startDiameter;
        float endDiameter;
        float peakAlpha;

        public void Launch(Material wispMaterial, Vector3 worldVelocity, float lifeSeconds, float startD, float endD, float peak, Color color)
        {
            material = wispMaterial;
            rend = GetComponent<Renderer>();
            velocity = worldVelocity;
            life = Mathf.Max(0.2f, lifeSeconds);
            startDiameter = startD;
            endDiameter = endD;
            peakAlpha = peak;
            baseColor = color;
        }

        void Update()
        {
            age += Time.deltaTime;
            var f = Mathf.Clamp01(age / life);

            transform.position += velocity * Time.deltaTime;
            velocity *= 1f - 0.7f * Time.deltaTime;            // air drag slows it
            velocity += Vector3.up * (0.05f * Time.deltaTime); // buoyancy keeps it climbing

            transform.localScale = Vector3.one * Mathf.Lerp(startDiameter, endDiameter, f);

            if (material != null)
            {
                var c = baseColor;
                c.a = peakAlpha * Mathf.Sin(f * Mathf.PI);     // ramp in then out
                material.color = c;

                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", c);
            }

            if (f >= 1f)
            {
                if (material != null)
                    Destroy(material);

                Destroy(gameObject);
            }
        }
    }
}
