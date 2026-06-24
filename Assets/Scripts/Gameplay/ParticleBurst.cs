using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// One-shot burst of simple ballistic pieces, built entirely at runtime (no authored prefab,
    /// no <see cref="ParticleSystem"/>) so it matches the project's runtime-mesh style and needs no
    /// scene wiring. Each piece is launched on a parabolic arc, spins, optionally shrinks, and the
    /// whole burst fades out and self-destructs.
    ///
    /// Two flavours are exposed:
    ///   • <see cref="SpawnSplash"/> — amber droplets thrown up and out as a coin breaks the water,
    ///     scaled to the glass and to the drop's "strength" (bigger/riskier coins splash harder).
    ///   • <see cref="SpawnCoinShower"/> — a celebratory rain of gold coins on a won round.
    ///
    /// Pieces move on <see cref="Time.deltaTime"/>, so a slow-motion beat (see
    /// <see cref="GameFeelDirector"/>) slows the splash with everything else, keeping the moment
    /// coherent.
    /// </summary>
    public class ParticleBurst : MonoBehaviour
    {
        struct Piece
        {
            public Transform Transform;
            public Vector3 Velocity;
            public Vector3 StartScale;
            public Vector3 SpinAxis;
            public float SpinSpeed;
        }

        Material material;
        Color baseColor;
        bool ownsMaterial;
        readonly List<Piece> pieces = new();

        /// <summary>
        /// Amber droplets kicked up and outward as a coin breaks the surface. Sizes and speeds are
        /// derived from <paramref name="surfaceWorldRadius"/> so the splash reads the same whatever
        /// scale the glass is authored at; <paramref name="strength"/> (≈1 for a normal coin) scales
        /// the droplet count and how high they leap.
        /// </summary>
        public static ParticleBurst SpawnSplash(
            Vector3 surfaceCenterWorld,
            float surfaceWorldRadius,
            Color liquidColor,
            float strength)
        {
            var scale = Mathf.Clamp(surfaceWorldRadius, 0.05f, 1.5f);
            var clampedStrength = Mathf.Clamp(strength, 0.25f, 3f);
            var count = DropletCountForStrength(clampedStrength);

            // Droplets read better than the near-clear calm liquid, so spawn them at a confident
            // amber regardless of how transparent the surface tint currently is.
            var dropletColor = new Color(liquidColor.r, liquidColor.g, liquidColor.b, 0.9f);

            var host = new GameObject("Splash Burst");
            var burst = host.AddComponent<ParticleBurst>();
            burst.baseColor = dropletColor;
            burst.material = GlassVisualController.CreateTransparentLiquidMaterial("Runtime Splash Material", dropletColor);
            burst.ownsMaterial = true;

            var pieceSize = scale * 0.085f;
            var velocities = new Vector3[count];
            var scales = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                var radial = RadialDirection(i, count, Random.value);
                var up = Mathf.Lerp(1.3f, 2.2f, Random.value) * clampedStrength;
                var outward = scale * Mathf.Lerp(1.4f, 2.4f, Random.value);
                velocities[i] = radial * outward + Vector3.up * up;

                var jitter = Mathf.Lerp(0.6f, 1.25f, Random.value);
                scales[i] = Vector3.one * (pieceSize * jitter);
            }

            // Start the droplets just inside the rim so they fan out of the surface, not the centre.
            var ringStart = scale * 0.35f;
            burst.Build(
                PrimitiveType.Sphere,
                surfaceCenterWorld + Vector3.up * (scale * 0.04f),
                ringStart,
                velocities,
                scales,
                gravity: 9.81f,
                lifetime: 0.55f,
                spinSpeed: 220f,
                shrinkOverLife: true);

            return burst;
        }

        /// <summary>
        /// Celebratory rain of gold coins, bursting up from above the glass and tumbling back down.
        /// Spawned when the player wins a round.
        /// </summary>
        public static ParticleBurst SpawnCoinShower(
            Vector3 centerWorld,
            float spreadRadius,
            Color coinColor,
            int coinCount)
        {
            var count = Mathf.Clamp(coinCount, 1, 64);
            var radius = Mathf.Max(0.05f, spreadRadius);

            var host = new GameObject("Coin Shower Burst");
            var burst = host.AddComponent<ParticleBurst>();
            burst.baseColor = coinColor;
            burst.material = CreateCoinMaterial(coinColor);
            burst.ownsMaterial = true;

            var velocities = new Vector3[count];
            var scales = new Vector3[count];

            for (var i = 0; i < count; i++)
            {
                var radial = RadialDirection(i, count, Random.value);
                var up = Mathf.Lerp(2.6f, 4.2f, Random.value);
                var outward = Mathf.Lerp(0.6f, 1.6f, Random.value);
                velocities[i] = radial * outward + Vector3.up * up;
                scales[i] = new Vector3(radius * 0.28f, radius * 0.05f, radius * 0.28f);
            }

            burst.Build(
                PrimitiveType.Cylinder,
                centerWorld + Vector3.up * (radius * 0.6f),
                radius * 0.5f,
                velocities,
                scales,
                gravity: 9.81f,
                lifetime: 1.5f,
                spinSpeed: 540f,
                shrinkOverLife: false);

            return burst;
        }

        void Build(
            PrimitiveType shape,
            Vector3 origin,
            float ringStartRadius,
            Vector3[] velocities,
            Vector3[] scales,
            float gravity,
            float lifetime,
            float spinSpeed,
            bool shrinkOverLife)
        {
            for (var i = 0; i < velocities.Length; i++)
            {
                var radial = RadialDirection(i, velocities.Length, 0f);
                var startPosition = origin + radial * ringStartRadius;
                var piece = CreatePiece(shape, startPosition, scales[i]);

                pieces.Add(new Piece
                {
                    Transform = piece,
                    Velocity = velocities[i],
                    StartScale = scales[i],
                    SpinAxis = Random.onUnitSphere,
                    SpinSpeed = spinSpeed * Mathf.Lerp(0.6f, 1.4f, Random.value),
                });
            }

            StartCoroutine(Run(origin, ringStartRadius, gravity, Mathf.Max(0.05f, lifetime), shrinkOverLife));
        }

        IEnumerator Run(Vector3 origin, float ringStartRadius, float gravity, float lifetime, bool shrinkOverLife)
        {
            var elapsed = 0f;

            while (elapsed < lifetime)
            {
                elapsed += Time.deltaTime;
                var life = Mathf.Clamp01(elapsed / lifetime);

                for (var i = 0; i < pieces.Count; i++)
                {
                    var piece = pieces[i];

                    if (piece.Transform == null)
                        continue;

                    var pieceOrigin = origin + RadialDirection(i, pieces.Count, 0f) * ringStartRadius;
                    piece.Transform.position = BallisticPosition(pieceOrigin, piece.Velocity, gravity, elapsed);
                    piece.Transform.Rotate(piece.SpinAxis, piece.SpinSpeed * Time.deltaTime, Space.Self);

                    if (shrinkOverLife)
                        piece.Transform.localScale = piece.StartScale * (1f - life);
                }

                // Fade the whole burst out over the back half of its life (shared material → one set).
                SetAlpha(Mathf.Lerp(baseColor.a, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((life - 0.45f) / 0.55f))));
                yield return null;
            }

            Destroy(gameObject);
        }

        Transform CreatePiece(PrimitiveType shape, Vector3 position, Vector3 scale)
        {
            var piece = GameObject.CreatePrimitive(shape);
            piece.name = "Burst Piece";
            piece.transform.SetParent(transform, worldPositionStays: true);
            piece.transform.position = position;
            piece.transform.localScale = scale;

            var collider = piece.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = piece.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            return piece.transform;
        }

        void SetAlpha(float alpha)
        {
            if (material == null)
                return;

            var c = baseColor;
            c.a = alpha;
            material.color = c;
        }

        void OnDestroy()
        {
            if (ownsMaterial && material != null)
                Destroy(material);
        }

        // ── pure helpers (unit-tested) ────────────────────────────────────────────────

        /// <summary>
        /// Evenly-spaced unit direction on the horizontal ring for piece <paramref name="index"/>,
        /// offset by <paramref name="seed"/> (turns 0..1) so successive bursts don't line up.
        /// Deterministic for a given seed, so the layout can be reasoned about and tested.
        /// </summary>
        public static Vector3 RadialDirection(int index, int count, float seed)
        {
            var safeCount = Mathf.Max(1, count);
            var angle = ((index + 0.5f) / safeCount + seed) * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>
        /// Position of a piece launched from <paramref name="origin"/> at <paramref name="velocity"/>
        /// under constant downward <paramref name="gravity"/>, at time <paramref name="t"/> seconds.
        /// </summary>
        public static Vector3 BallisticPosition(Vector3 origin, Vector3 velocity, float gravity, float t)
        {
            var time = Mathf.Max(0f, t);
            return origin + velocity * time + Vector3.down * (0.5f * gravity * time * time);
        }

        /// <summary>Droplet count for a splash of the given strength (≈1 normal), clamped to a sane range.</summary>
        public static int DropletCountForStrength(float strength) =>
            Mathf.Clamp(Mathf.RoundToInt(6f * Mathf.Max(0f, strength)) + 2, 4, 20);

        static Material CreateCoinMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                name = "Runtime Coin Shower Material",
                color = color,
            };

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.85f);

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0.8f);

            return material;
        }
    }
}
