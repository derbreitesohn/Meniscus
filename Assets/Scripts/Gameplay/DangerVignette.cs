using Meniscus.Core;
using Meniscus.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// A full-screen red vignette that darkens the edges of the screen as the glass nears the brim,
    /// throbbing once the spill chance is high — a screen-space companion to the liquid's amber colour
    /// tell, so the rising tension is felt everywhere, not just inside the glass.
    ///
    /// Builds its own overlay canvas and a runtime radial-gradient sprite, so it needs no scene or
    /// Inspector setup; <see cref="Core.GameManager"/> attaches one at runtime if the scene has none.
    /// The overlay never eats input (its image is not a raycast target).
    /// </summary>
    [DisallowMultipleComponent]
    public class DangerVignette : MonoBehaviour
    {
        [SerializeField] GlassManager glassManager;
        [Tooltip("Danger (0..1) below which the vignette stays invisible.")]
        [SerializeField, Range(0f, 0.95f)] float onset = 0.4f;
        [Tooltip("Edge opacity at full danger.")]
        [SerializeField, Range(0f, 1f)] float maxAlpha = 0.18f;
        [SerializeField] Color tint = new(0.62f, 0.05f, 0.03f, 1f);
        [Tooltip("Danger above which the vignette starts to throb.")]
        [SerializeField, Range(0f, 1f)] float pulseOnset = 0.72f;
        [SerializeField, Min(0f)] float pulseSpeed = 7f;
        [SerializeField, Range(0f, 1f)] float pulseStrength = 0.12f;
        [SerializeField, Min(0f)] float followSpeed = 6f;

        Image image;
        float currentDanger;

        void OnEnable()
        {
            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();

            BuildOverlay();

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

        public void Configure(GlassManager glass)
        {
            if (glassManager != null)
                glassManager.ProbabilityChanged -= OnProbabilityChanged;

            glassManager = glass;

            if (isActiveAndEnabled && glassManager != null)
            {
                glassManager.ProbabilityChanged += OnProbabilityChanged;
                OnProbabilityChanged(glassManager.CurrentOverflowProbability);
            }
        }

        void OnProbabilityChanged(float _)
        {
            // The colour tell scales against the spill-chance ceiling, like the liquid does.
            currentDanger = glassManager == null
                ? 0f
                : Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxSpillChance);
        }

        void Update()
        {
            if (image == null)
                return;

            var baseAlpha = VignetteAlpha(currentDanger, onset, maxAlpha);

            var pulse = 1f;
            if (currentDanger > pulseOnset)
            {
                var pulseRamp = Mathf.InverseLerp(pulseOnset, 1f, currentDanger);
                pulse = 1f + pulseStrength * pulseRamp * Mathf.Sin(Time.unscaledTime * pulseSpeed);
            }

            var target = Mathf.Clamp01(baseAlpha * pulse);
            var eased = followSpeed <= 0f
                ? target
                : Mathf.Lerp(image.color.a, target, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));

            image.color = new Color(tint.r, tint.g, tint.b, eased);
        }

        void BuildOverlay()
        {
            if (image != null)
                return;

            var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Danger Vignette Canvas");
            canvas.sortingOrder = 0; // behind the money HUD / shop / end screen

            var go = new GameObject("Danger Vignette", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            image = go.GetComponent<Image>();
            image.sprite = BuildRadialSprite();
            image.type = Image.Type.Simple;
            image.raycastTarget = false; // never intercept clicks on the table
            image.color = new Color(tint.r, tint.g, tint.b, 0f);
        }

        // A soft radial gradient: clear through the centre, opaque at the edges, so the tint reads as a
        // vignette framing the action rather than a flat wash over it.
        static Sprite BuildRadialSprite()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.Alpha8, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "Danger Vignette Texture",
            };

            var pixels = new Color[size * size];
            var center = (size - 1) * 0.5f;
            var maxDistance = center; // edge midpoints reach full strength; corners clamp past it

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - center) / maxDistance;
                    var dy = (y - center) / maxDistance;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((distance - 0.45f) / 0.55f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // ── pure helper (unit-tested) ─────────────────────────────────────────────────

        /// <summary>
        /// Edge opacity for a given danger (0..1): zero until <paramref name="onset"/>, then eased up to
        /// <paramref name="maxAlpha"/> at full danger.
        /// </summary>
        public static float VignetteAlpha(float danger01, float onset, float maxAlpha)
        {
            if (onset >= 1f)
                return 0f;

            var t = Mathf.Clamp01((Mathf.Clamp01(danger01) - onset) / (1f - onset));
            return Mathf.SmoothStep(0f, 1f, t) * Mathf.Max(0f, maxAlpha);
        }
    }
}
