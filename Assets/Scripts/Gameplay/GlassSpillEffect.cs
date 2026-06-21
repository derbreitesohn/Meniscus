using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// One-shot overspill visual: amber rivulets run down the outside of the glass to the table,
    /// then a puddle spreads and fades. Spawned by <see cref="GlassVisualController"/> on overflow
    /// and self-destructs when finished. All primitives are built at runtime — no authored prefabs.
    /// </summary>
    public class GlassSpillEffect : MonoBehaviour
    {
        const float RivuletWidth = 0.05f;
        const float RivuletDepth = 0.015f;
        const float RivuletRadiusOffset = 0.015f;

        Material material;
        Color baseColor;
        readonly List<Renderer> renderers = new();

        public static GlassSpillEffect Spawn(
            Vector3 glassWorldPosition,
            float rimRadius,
            float rimWorldY,
            float tableWorldY,
            Color color,
            int rivuletCount,
            float runDownDuration,
            float puddleLifetime)
        {
            // World-rooted (no parent): the glass sits under a non-uniform scale, so the effect must
            // live at identity world scale for rivulet world positions and sizes to match the glass.
            // The coroutine self-destructs, so no parent is needed for cleanup.
            var host = new GameObject("Runtime Overspill Effect");

            var effect = host.AddComponent<GlassSpillEffect>();
            effect.baseColor = color;
            effect.material = GlassVisualController.CreateTransparentLiquidMaterial("Runtime Overspill Material", color);
            effect.StartCoroutine(effect.Run(
                glassWorldPosition,
                rimRadius,
                rimWorldY,
                tableWorldY,
                Mathf.Max(1, rivuletCount),
                Mathf.Max(0.05f, runDownDuration),
                Mathf.Max(0.1f, puddleLifetime)));

            return effect;
        }

        IEnumerator Run(
            Vector3 glassWorldPosition,
            float rimRadius,
            float rimWorldY,
            float tableWorldY,
            int rivuletCount,
            float runDownDuration,
            float puddleLifetime)
        {
            var runHeight = Mathf.Max(0.01f, rimWorldY - tableWorldY);
            var streaks = new Transform[rivuletCount];
            var directions = new Vector3[rivuletCount];

            for (var i = 0; i < rivuletCount; i++)
            {
                var angle = (i + Random.value) / rivuletCount * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                directions[i] = dir;

                var streak = CreatePrimitive(PrimitiveType.Cube, "Overspill Rivulet").transform;
                streak.rotation = Quaternion.LookRotation(dir, Vector3.up);
                streaks[i] = streak;
            }

            // Phase 1 — rivulets grow downward from the rim to the table.
            var elapsed = 0f;
            while (elapsed < runDownDuration)
            {
                elapsed += Time.deltaTime;
                var length = runHeight * RunDownProgress(elapsed, runDownDuration);
                var centreY = rimWorldY - length * 0.5f;

                for (var i = 0; i < rivuletCount; i++)
                {
                    var dir = directions[i];
                    streaks[i].position = new Vector3(
                        glassWorldPosition.x + dir.x * (rimRadius + RivuletRadiusOffset),
                        centreY,
                        glassWorldPosition.z + dir.z * (rimRadius + RivuletRadiusOffset));
                    streaks[i].localScale = new Vector3(RivuletWidth, Mathf.Max(0.001f, length), RivuletDepth);
                }

                yield return null;
            }

            // Phase 2 — puddle spreads on the table while everything fades out.
            var puddle = CreatePrimitive(PrimitiveType.Cylinder, "Overspill Puddle").transform;
            puddle.position = new Vector3(glassWorldPosition.x, tableWorldY, glassWorldPosition.z);

            var fade = 0f;
            while (fade < puddleLifetime)
            {
                fade += Time.deltaTime;
                var k = Mathf.Clamp01(fade / puddleLifetime);
                var spread = Mathf.Lerp(0.3f, 1f, Mathf.Sqrt(k)) * rimRadius * 2.6f;
                puddle.localScale = new Vector3(spread, 0.006f, spread);
                SetAlpha(Mathf.Lerp(baseColor.a, 0f, k));
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>Eased 0..1 reveal of how far the rivulets have run from the rim toward the table.</summary>
        public static float RunDownProgress(float elapsed, float duration)
        {
            if (duration <= 0f)
                return 1f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
        }

        GameObject CreatePrimitive(PrimitiveType type, string primitiveName)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = primitiveName;
            primitive.transform.SetParent(transform, worldPositionStays: true);

            var collider = primitive.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var renderer = primitive.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers.Add(renderer);
            }

            return primitive;
        }

        void SetAlpha(float alpha)
        {
            var c = baseColor;
            c.a = alpha;

            if (material != null)
                material.color = c;
        }

        void OnDestroy()
        {
            if (material != null)
                Destroy(material);
        }
    }
}
