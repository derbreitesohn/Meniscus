using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// A warm selection glow that fades in behind a selected coin or desk item and gently pulses, so a
    /// selection reads as "lifted and glowing on the outside". Two looks share this one follower: the
    /// default soft halo (a filled radial bloom, used by coins) and an <c>outline</c> ring (a bright rim
    /// with a clear centre, used by desk items so the item reads as outlined rather than washed out). It
    /// is a standalone follower object (not a child of the target) so the target's own scale — coins sit
    /// under a non-uniform squash — never distorts it, and it always faces the camera. Built entirely at
    /// runtime from cached sprites; self-destructs when its target is gone.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionGlow : MonoBehaviour
    {
        Transform target;
        SpriteRenderer sprite;
        Color tint = new(1f, 0.85f, 0.4f, 1f);
        float baseAlpha = 0.25f;
        bool on;
        Camera cam;

        static Sprite sharedHaloSprite;
        static Sprite sharedRingSprite;

        /// <summary>
        /// Creates a glow that follows <paramref name="target"/>, sized to roughly
        /// <paramref name="worldSize"/> across (a little larger than the object it haloes). Pass
        /// <paramref name="outline"/> for a crisp rim/outline instead of the soft filled halo. Starts hidden.
        /// </summary>
        public static SelectionGlow Attach(Transform target, float worldSize, Color glowTint, bool outline = false)
        {
            var go = new GameObject($"Selection Glow ({target.name})");
            var glow = go.AddComponent<SelectionGlow>();
            glow.target = target;
            glow.tint = glowTint;
            // The outline rim is thin, so push its alpha up to stay legible; the soft halo keeps its
            // subtler default.
            glow.baseAlpha = outline ? 0.95f : 0.25f;

            glow.sprite = go.AddComponent<SpriteRenderer>();
            glow.sprite.sprite = SharedSprite(outline);
            glow.sprite.color = new Color(glowTint.r, glowTint.g, glowTint.b, 0f);
            glow.sprite.enabled = false;

            go.transform.position = target.position;
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, worldSize);
            return glow;
        }

        public void SetActive(bool value)
        {
            on = value;

            if (sprite != null)
                sprite.enabled = value;
        }

        void LateUpdate()
        {
            // The coin/box was destroyed (round cleared) — take the halo with it.
            if (target == null)
            {
                Destroy(gameObject);
                return;
            }

            if (!target.gameObject.activeInHierarchy)
            {
                if (sprite != null)
                    sprite.enabled = false;
                return;
            }

            transform.position = target.position;

            if (!on)
                return;

            if (sprite != null && !sprite.enabled)
                sprite.enabled = true;

            if (cam == null)
                cam = Camera.main;

            if (cam != null)
                transform.rotation = cam.transform.rotation; // billboard toward the viewer

            // Gentle breathing pulse (unscaled, so it keeps glowing during the slow-motion verdict).
            var pulse = baseAlpha * (0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 6f));

            if (sprite != null)
                sprite.color = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(pulse));
        }

        // Two shared, constant textures (one filled halo, one outline ring), built lazily and reused by
        // every glow in the scene, so selecting many coins/items stays cheap.
        static Sprite SharedSprite(bool outline)
        {
            if (outline)
            {
                if (sharedRingSprite == null)
                    sharedRingSprite = BuildSprite("Selection Outline Texture", AlphaForOutline);

                return sharedRingSprite;
            }

            if (sharedHaloSprite == null)
                sharedHaloSprite = BuildSprite("Selection Glow Texture", AlphaForHalo);

            return sharedHaloSprite;
        }

        // Soft filled bloom: 1 at the centre → 0 at the rim, squared for a gentle falloff.
        static float AlphaForHalo(float distance)
        {
            var a = Mathf.Clamp01(1f - distance);
            return a * a;
        }

        // Outline ring: a bright rim near the edge with a clear centre, so the selected object reads as
        // outlined rather than washed out. A Gaussian band peaks at the rim radius and falls off both ways.
        static float AlphaForOutline(float distance)
        {
            // Kept inside the sprite so the ring fades fully to clear before the texture edge — otherwise
            // the round rim gets clipped square at the quad boundary and shows a faint box seam.
            const float peak = 0.7f;        // normalized radius of the rim
            const float thickness = 0.13f;  // how wide/soft the rim is
            var d = (distance - peak) / thickness;
            return Mathf.Clamp01(Mathf.Exp(-0.5f * d * d));
        }

        static Sprite BuildSprite(string name, System.Func<float, float> alphaAt)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };

            var pixels = new Color32[size * size];
            var center = (size - 1) * 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - center) / center;
                    var dy = (y - center) / center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(alphaAt(distance));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            // pixelsPerUnit = size so the sprite is ~1 world unit across at scale 1; the caller scales it.
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
