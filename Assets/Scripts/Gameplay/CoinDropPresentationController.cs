using System.Collections;
using System.Collections.Generic;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    [DisallowMultipleComponent]
    public class CoinDropPresentationController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] Transform glassTarget;
        [SerializeField] GlassVisualController glassVisual;
        [SerializeField, Min(0.05f)] float dropAnimationSeconds = 0.98f;
        [SerializeField, Min(0.05f)] float sinkSeconds = 0.45f;       // surface -> resting slot at the bottom
        [SerializeField, Min(0f)] float liftArcHeight = 0.34f;
        [SerializeField, Min(0f)] float entryHeightAboveSurface = 0.03f; // where the coin breaks the surface
        [SerializeField, Range(0.1f, 1f)] float coinInGlassFillFraction = 0.3f; // coin diameter vs interior diameter
        [SerializeField, Min(0f)] float proxyLifetimeAfterDrop = 0.08f;   // fallback path only (no glass geometry)
        [SerializeField, Range(0f, 45f)] float carryTiltDegrees = 12f;   // gentle bank while the coin is carried
        [SerializeField, Range(0f, 120f)] float releaseTipDegrees = 40f; // leading edge dips in as it is released
        [SerializeField, Min(0f)] float surfaceEntryPauseSeconds = 0.32f;  // dramatic hold as coin breaks the surface
        [SerializeField] AK.Wwise.Event coinIntoWater;   // assign Play_Coin_IntoWater in the Inspector

        const float LiftPhaseEnd = 0.58f;
        const float HoldPhaseEnd = 0.72f;
        const int PileSlotsPerLayer = 3;

        Transform pileContainer;
        int pileCount;

        void OnEnable()
        {
            ResolveReferences();

            if (gameManager != null)
            {
                gameManager.DropCommitted += OnDropCommitted;
                gameManager.RoundStarted += OnRoundStarted;
            }
        }

        void OnDisable()
        {
            if (gameManager != null)
            {
                gameManager.DropCommitted -= OnDropCommitted;
                gameManager.RoundStarted -= OnRoundStarted;
            }
        }

        public void Configure(GameManager manager, Transform target)
        {
            if (gameManager != null)
            {
                gameManager.DropCommitted -= OnDropCommitted;
                gameManager.RoundStarted -= OnRoundStarted;
            }

            gameManager = manager;
            glassTarget = target;
            glassVisual = null;   // re-resolve against the new target on next drop

            if (isActiveAndEnabled && gameManager != null)
            {
                gameManager.DropCommitted += OnDropCommitted;
                gameManager.RoundStarted += OnRoundStarted;
            }
        }

        void OnDropCommitted(TurnActor actor, IReadOnlyList<Coin> coins)
        {
            if (coins == null || coins.Count == 0)
                return;

            ResolveGlassGeometry(
                out var canPile,
                out var surfaceCenter,
                out var surfaceWorldRadius,
                out var floorCenter,
                out var floorWorldRadius);

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                var proxy = CreateCoinProxy(coins[i], actor, i);

                if (!canPile)
                {
                    // No glass geometry to aim at: keep the old fly-to-a-point-and-fade behaviour.
                    StartCoroutine(AnimateProxyFlightOnly(proxy, surfaceCenter, actor, i));
                    continue;
                }

                // Measure the proxy so it can be shrunk to fit the cup and stacked without clipping the wall.
                var bounds = WorldRendererBounds(proxy);
                // Use the largest dimension (not just x/z) so a coin model authored on a different axis
                // still shrinks to fit instead of staying oversized (the gold coin read as huge).
                var coinDiameter = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                var fitScale = CalculateFitScale(coinDiameter, floorWorldRadius * 2f, coinInGlassFillFraction);
                var fittedRadius = coinDiameter * fitScale * 0.5f;
                var fittedThickness = Mathf.Max(bounds.size.y * fitScale, 0.004f);

                // Reserve a unique slot synchronously so coins dropped together never share one.
                var slotIndex = pileCount++;
                var slotOffset = CalculatePileSlotLocalOffset(slotIndex, floorWorldRadius, fittedRadius, fittedThickness);
                var rest = floorCenter + slotOffset;

                // Enter the surface directly above where the coin will come to rest.
                var entry = surfaceCenter
                    + new Vector3(slotOffset.x, 0f, slotOffset.z)
                    + Vector3.up * entryHeightAboveSurface;

                StartCoroutine(AnimateProxyIntoGlass(proxy, surfaceCenter, entry, rest, fitScale, actor, slotIndex));
            }
        }

        GameObject CreateCoinProxy(Coin sourceCoin, TurnActor actor, int index)
        {
            // Fly the coin's *visible* mesh so a model coin flies as itself and a placeholder coin still
            // flies as its cylinder. When a model is showing, the root cylinder renderer is disabled, so we
            // clone the coin's actual model (preserving multi-part meshes and materials) and only fall back
            // to mirroring the cylinder body when no model is present.
            var visibleModel = sourceCoin.ActiveModel;
            GameObject proxy;
            Transform visualTransform;

            if (visibleModel != null)
            {
                proxy = Instantiate(visibleModel);
                visualTransform = visibleModel.transform;
            }
            else
            {
                var bodyRenderer = sourceCoin.GetComponentInChildren<Renderer>();
                var bodyFilter = sourceCoin.GetComponentInChildren<MeshFilter>();
                visualTransform = bodyRenderer != null ? bodyRenderer.transform : sourceCoin.transform;

                proxy = GameObject.CreatePrimitive(PrimitiveType.Cylinder);

                var proxyFilter = proxy.GetComponent<MeshFilter>();
                if (bodyFilter != null && bodyFilter.sharedMesh != null && proxyFilter != null)
                    proxyFilter.sharedMesh = bodyFilter.sharedMesh;

                var proxyRenderer = proxy.GetComponent<Renderer>();
                if (bodyRenderer != null && proxyRenderer != null)
                    proxyRenderer.sharedMaterial = bodyRenderer.sharedMaterial;
            }

            proxy.name = $"{actor} Drop Proxy {sourceCoin.name}";
            proxy.transform.position = visualTransform.position;
            proxy.transform.rotation = visualTransform.rotation;
            proxy.transform.localScale = visualTransform.lossyScale;

            // A proxy is purely visual; strip any colliders the primitive or model brought along.
            foreach (var proxyCollider in proxy.GetComponentsInChildren<Collider>())
                Destroy(proxyCollider);

            proxy.transform.position += new Vector3(index * 0.03f, 0.02f, -index * 0.02f);
            return proxy;
        }

        IEnumerator AnimateProxyIntoGlass(
            GameObject proxy,
            Vector3 surfaceCenter,
            Vector3 entry,
            Vector3 rest,
            float fitScale,
            TurnActor actor,
            int slotIndex)
        {
            if (proxy == null)
                yield break;

            var start = proxy.transform.position;
            var startScale = proxy.transform.localScale;
            var hold = CalculateRimHoldPosition(surfaceCenter, actor, slotIndex);

            var startRotation = proxy.transform.rotation;
            var carryRotation = CalculateCarryRotation(startRotation, start, hold, carryTiltDegrees);
            var releaseRotation = CalculateReleaseRotation(carryRotation, hold, entry, releaseTipDegrees);

            // Phase 1: carry the coin over the rim and release it at the entry point above the surface.
            var elapsed = 0f;

            while (elapsed < dropAnimationSeconds && proxy != null)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / dropAnimationSeconds);
                proxy.transform.position = CalculateStagedDropPosition(start, hold, entry, normalized, liftArcHeight);
                proxy.transform.rotation = CalculateStagedDropRotation(startRotation, carryRotation, releaseRotation, normalized);
                yield return null;
            }

            if (proxy == null)
                yield break;

            proxy.transform.position = entry;
            proxy.transform.rotation = releaseRotation;

            var emitter = glassTarget != null ? glassTarget.gameObject : gameObject;
            coinIntoWater?.Post(emitter);   // splash as the coin breaks the surface

            // Kick the ripple at the exact visual moment the coin breaks the surface, then hold for drama.
            glassVisual?.KickRipple(1.5f);

            var pauseElapsed = 0f;
            while (pauseElapsed < surfaceEntryPauseSeconds && proxy != null)
            {
                pauseElapsed += Time.deltaTime;
                yield return null;
            }

            if (proxy == null)
                yield break;

            // Phase 2: sink from the surface down to the resting slot, shrinking to fit and settling flat.
            var fittedScale = startScale * fitScale;
            var restRotation = CalculateRestRotation(slotIndex);
            var sinkStart = proxy.transform.position;
            var sinkStartRotation = proxy.transform.rotation;
            var sinkElapsed = 0f;

            while (sinkElapsed < sinkSeconds && proxy != null)
            {
                sinkElapsed += Time.deltaTime;
                var normalized = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sinkElapsed / sinkSeconds));
                proxy.transform.position = Vector3.Lerp(sinkStart, rest, normalized);
                proxy.transform.localScale = Vector3.Lerp(startScale, fittedScale, normalized);
                proxy.transform.rotation = Quaternion.Slerp(sinkStartRotation, restRotation, normalized);
                yield return null;
            }

            if (proxy == null)
                yield break;

            proxy.transform.position = rest;
            proxy.transform.localScale = fittedScale;
            proxy.transform.rotation = restRotation;

            // Park the coin in the cup so it stays visible and accumulates; cleared on the next round.
            EnsurePileContainer();
            proxy.transform.SetParent(pileContainer, true);
        }

        // Fallback used only when no glass geometry can be resolved: fly to a point and fade, as before.
        IEnumerator AnimateProxyFlightOnly(GameObject proxy, Vector3 targetPosition, TurnActor actor, int index)
        {
            if (proxy == null)
                yield break;

            var start = proxy.transform.position;
            var hold = CalculateRimHoldPosition(targetPosition, actor, index);
            var end = targetPosition + new Vector3((index - 0.5f) * 0.035f, -0.02f, index * 0.02f);

            var startRotation = proxy.transform.rotation;
            var carryRotation = CalculateCarryRotation(startRotation, start, hold, carryTiltDegrees);
            var releaseRotation = CalculateReleaseRotation(carryRotation, hold, end, releaseTipDegrees);
            var elapsed = 0f;

            while (elapsed < dropAnimationSeconds && proxy != null)
            {
                elapsed += Time.deltaTime;
                var normalized = Mathf.Clamp01(elapsed / dropAnimationSeconds);
                proxy.transform.position = CalculateStagedDropPosition(start, hold, end, normalized, liftArcHeight);
                proxy.transform.rotation = CalculateStagedDropRotation(startRotation, carryRotation, releaseRotation, normalized);
                yield return null;
            }

            if (proxy == null)
                yield break;

            proxy.transform.position = end;
            proxy.transform.rotation = releaseRotation;

            var emitter = glassTarget != null ? glassTarget.gameObject : gameObject;
            coinIntoWater?.Post(emitter);

            Destroy(proxy, proxyLifetimeAfterDrop);
        }

        void OnRoundStarted(int round) => ClearPile();

        void ClearPile()
        {
            pileCount = 0;

            if (pileContainer == null)
                return;

            for (var i = pileContainer.childCount - 1; i >= 0; i--)
                Destroy(pileContainer.GetChild(i).gameObject);
        }

        void EnsurePileContainer()
        {
            if (pileContainer != null)
                return;

            // Identity scale/rotation so coins parked by world position stay round (the glass itself is
            // under a non-uniform squash that would otherwise flatten them).
            pileContainer = new GameObject("Coins In Glass").transform;
            pileContainer.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            pileContainer.localScale = Vector3.one;
        }

        void ResolveGlassGeometry(
            out bool canPile,
            out Vector3 surfaceCenter,
            out float surfaceWorldRadius,
            out Vector3 floorCenter,
            out float floorWorldRadius)
        {
            EnsureGlassReferences();

            if (glassVisual == null || glassTarget == null)
            {
                // No source of truth for the liquid: best-effort target above whatever we found.
                canPile = false;
                surfaceCenter = glassTarget != null ? glassTarget.position + Vector3.up * 0.42f : Vector3.up;
                surfaceWorldRadius = 0.1f;
                floorCenter = surfaceCenter;
                floorWorldRadius = 0.1f;
                return;
            }

            surfaceCenter = ColumnPointToWorld(glassTarget, glassVisual.StableSurfaceLocalY);
            surfaceWorldRadius = HorizontalWorldRadius(glassTarget, glassVisual.SurfaceLocalRadius);
            floorCenter = ColumnPointToWorld(glassTarget, glassVisual.FloorLocalY);
            floorWorldRadius = HorizontalWorldRadius(
                glassTarget,
                glassVisual.SurfaceLocalRadius * glassVisual.FloorRadiusScale);
            canPile = true;
        }

        void EnsureGlassReferences()
        {
            if (glassTarget == null)
            {
                var glassObject = GameObject.FindGameObjectWithTag("Glass");

                if (glassObject != null)
                    glassTarget = glassObject.transform;
            }

            if (glassVisual == null && glassTarget != null)
                glassVisual = glassTarget.GetComponent<GlassVisualController>();

            if (glassVisual == null)
                glassVisual = FindAnyObjectByType<GlassVisualController>();
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();
        }

        static Bounds WorldRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        static Vector3 CalculateRimHoldPosition(Vector3 targetPosition, TurnActor actor, int index)
        {
            var side = index % 2 == 0 ? -1f : 1f;
            var actorDepth = actor == TurnActor.Player ? -0.15f : 0.15f;
            return targetPosition + new Vector3(side * 0.24f, 0.22f, actorDepth);
        }

        public static Vector3 CalculateArcPosition(Vector3 start, Vector3 end, float normalizedTime, float height)
        {
            var t = Mathf.Clamp01(normalizedTime);
            var linear = Vector3.Lerp(start, end, t);
            var arc = Mathf.Sin(t * Mathf.PI) * Mathf.Max(0f, height);
            return linear + Vector3.up * arc;
        }

        public static Vector3 CalculateStagedDropPosition(
            Vector3 start,
            Vector3 hold,
            Vector3 end,
            float normalizedTime,
            float liftArcHeight)
        {
            var t = Mathf.Clamp01(normalizedTime);

            if (t <= LiftPhaseEnd)
            {
                var liftT = Mathf.SmoothStep(0f, 1f, t / LiftPhaseEnd);
                return CalculateArcPosition(start, hold, liftT, liftArcHeight);
            }

            if (t <= HoldPhaseEnd)
                return hold;

            var releaseT = Mathf.SmoothStep(0f, 1f, (t - HoldPhaseEnd) / (1f - HoldPhaseEnd));
            var sideSag = Mathf.Sin(releaseT * Mathf.PI) * 0.035f;
            return Vector3.Lerp(hold, end, releaseT) + Vector3.down * sideSag;
        }

        public static Quaternion CalculateStagedDropRotation(
            Quaternion start,
            Quaternion carry,
            Quaternion release,
            float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);

            if (t <= LiftPhaseEnd)
            {
                var liftT = Mathf.SmoothStep(0f, 1f, t / LiftPhaseEnd);
                return Quaternion.Slerp(start, carry, liftT);
            }

            if (t <= HoldPhaseEnd)
                return carry;

            var releaseT = Mathf.SmoothStep(0f, 1f, (t - HoldPhaseEnd) / (1f - HoldPhaseEnd));
            return Quaternion.Slerp(carry, release, releaseT);
        }

        static Quaternion CalculateCarryRotation(Quaternion start, Vector3 from, Vector3 to, float tiltDegrees)
        {
            var travel = to - from;
            travel.y = 0f;

            if (travel.sqrMagnitude < 1e-5f)
                return start;

            // Bank the coin toward the way it is being carried, as if pinched steady between fingers.
            var bankAxis = Vector3.Cross(Vector3.up, travel.normalized);
            return Quaternion.AngleAxis(tiltDegrees, bankAxis) * start;
        }

        static Quaternion CalculateReleaseRotation(Quaternion carry, Vector3 from, Vector3 to, float tipDegrees)
        {
            var travel = to - from;
            travel.y = 0f;

            // Tip the leading edge down so the coin slides into the water as the fingers let go, not spinning.
            var tipAxis = travel.sqrMagnitude < 1e-5f
                ? Vector3.right
                : Vector3.Cross(Vector3.up, travel.normalized);

            return Quaternion.AngleAxis(tipDegrees, tipAxis) * carry;
        }

        // A coin resting at the bottom lies roughly flat; a small deterministic tilt/spin keeps a stack
        // from looking mechanically aligned (deterministic - no RNG - so behaviour stays reproducible).
        static Quaternion CalculateRestRotation(int slotIndex)
        {
            var spin = slotIndex * 47f % 360f;
            var tilt = slotIndex * 13f % 17f - 8f; // roughly -8..+8 degrees
            return Quaternion.Euler(tilt, spin, 0f);
        }

        /// <summary>
        /// Converts a point on the glass's central axis (given as a local Y height) to world space, so the
        /// drop aims at the glass's *real* scaled liquid surface/floor instead of a hardcoded world offset.
        /// </summary>
        public static Vector3 ColumnPointToWorld(Transform glass, float localY) =>
            glass.TransformPoint(new Vector3(0f, localY, 0f));

        /// <summary>
        /// Converts a horizontal local radius to world units using the glass's (possibly non-uniform) XZ
        /// footprint, so coins are sized and placed against the cup's true opening.
        /// </summary>
        public static float HorizontalWorldRadius(Transform glass, float localRadius)
        {
            var scale = glass.lossyScale;
            return Mathf.Abs(localRadius) * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        /// <summary>
        /// Largest uniform scale multiplier so a coin of <paramref name="coinWorldDiameter"/> occupies at
        /// most <paramref name="fillFraction"/> of the interior diameter. Never enlarges a coin (clamped to
        /// 1) and is safe for a zero-diameter (unmeasurable) coin.
        /// </summary>
        public static float CalculateFitScale(float coinWorldDiameter, float interiorWorldDiameter, float fillFraction)
        {
            if (coinWorldDiameter <= 1e-5f)
                return 1f;

            var maxDiameter = Mathf.Max(0f, interiorWorldDiameter) * Mathf.Clamp01(fillFraction);
            return Mathf.Clamp(maxDiameter / coinWorldDiameter, 0f, 1f);
        }

        /// <summary>
        /// Resting offset (relative to the interior floor centre, world-axis aligned) for the
        /// <paramref name="index"/>-th coin in the pile: coins fan out on a ring and stack in layers as the
        /// floor fills, so they neither z-fight in one spot nor poke through the wall. Deterministic.
        /// </summary>
        public static Vector3 CalculatePileSlotLocalOffset(
            int index,
            float interiorRadius,
            float coinRadius,
            float coinThickness)
        {
            var safeIndex = Mathf.Max(0, index);
            var layer = safeIndex / PileSlotsPerLayer;
            var slotInLayer = safeIndex % PileSlotsPerLayer;

            // Keep the whole coin inside the wall: its centre can sit at most (interiorRadius - coinRadius) out.
            var availableRadius = Mathf.Max(0f, interiorRadius - coinRadius);
            var ringRadius = availableRadius * 0.55f;

            // Rotate each layer so stacked coins are offset, not perfectly aligned.
            var angle = slotInLayer * (Mathf.PI * 2f / PileSlotsPerLayer) + layer * 0.7f;
            var x = Mathf.Cos(angle) * ringRadius;
            var z = Mathf.Sin(angle) * ringRadius;

            // Each layer rests on top of the previous (first layer sits half a coin above the floor), with a
            // small per-slot bump so coins in one layer are never exactly coplanar (which would z-fight).
            var safeThickness = Mathf.Max(0f, coinThickness);
            var y = safeThickness * (layer + 0.5f) + slotInLayer * safeThickness * 0.18f;

            return new Vector3(x, y, z);
        }
    }
}
