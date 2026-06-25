using System;
using System.Collections;
using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.UI;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Conducts the whole drop presentation as one timed sequence: it closes the book menu, chooses the
    /// camera framing (a straight-on front close-up by default, swinging to the dramatic side angle to bait
    /// the player and always on a real overflow), then picks the coins up, carries them over the glass,
    /// holds them poised above the water for a beat, plunges them in with a ripple, lets the water tremble
    /// through a suspense beat, and finally reveals the overflow (or not). When the sequence finishes it
    /// calls back so <see cref="GameManager"/> advances the turn — the logical outcome was decided up front,
    /// but every visual/audio beat is staged here so it lands at the right dramatic moment.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoinDropPresentationController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] Transform glassTarget;
        [SerializeField] GlassVisualController glassVisual;
        [SerializeField] CameraController cameraController;
        [SerializeField] BookShopView bookShop;
        [SerializeField] GameFeelDirector gameFeel;

        [Header("Timing")]
        [SerializeField, Min(0.05f)] float approachSeconds = 0.9f;       // pick up + carry over to poised above water
        [SerializeField, Min(1f)] float holdAboveWaterSeconds = 1.15f;   // poise over the surface before letting go (>= 1s)
        [SerializeField, Min(0.05f)] float plungeSeconds = 0.55f;        // drop from poised down into the resting slot
        [SerializeField, Min(0f)] float suspenseSeconds = 1.35f;         // watch the water still shaking before the reveal
        [SerializeField, Min(0f)] float overflowRevealHoldSeconds = 1.4f; // hold on the overflow close-up so the spill is seen

        [Header("Shape")]
        [SerializeField, Min(0f)] float liftArcHeight = 0.34f;
        [SerializeField, Min(0f)] float hoverHeightAboveSurface = 0.16f; // coins poise / are released well above the water, then fall through air before the splash (never grazing it before the plunge)
        [SerializeField, Min(0f)] float hoverBobAmplitude = 0.012f;      // gentle bob while poised (lifts only, never dips toward the water)
        [SerializeField, Range(0.1f, 1f)] float coinInGlassFillFraction = 0.3f; // coin diameter vs interior diameter
        [SerializeField, Tooltip("Lifts the resting coin pile this many glass-local units above the liquid's " +
            "modeled floor, so coins settle on the glass's real interior bottom (e.g. a thick 'Heavy Base') " +
            "instead of sinking to the liquid floor, which can be authored below the visible cup.")]
        float coinRestFloorLift; // glass-local units; leaves the liquid look untouched
        [SerializeField, Range(0f, 45f)] float carryTiltDegrees = 12f;   // gentle bank while the coin is carried
        [SerializeField, Range(0f, 120f)] float releaseTipDegrees = 40f; // leading edge dips in as it is released
        [SerializeField, Min(0f)] float proxyLifetimeAfterDrop = 0.08f;  // fallback path only (no glass geometry)

        [Header("Camera Baiting")]
        [Tooltip("On a SAFE drop, if its spill chance (as a fraction of the max) is at or above this, the camera " +
                 "always swings to the dramatic side angle. Below it, the side swing is a random fake-out.")]
        [SerializeField, Range(0f, 1f)] float highRiskSideThreshold = 0.5f;
        [Tooltip("Chance the camera swings to the bait side angle on a lower-risk safe drop, to keep the side " +
                 "shot from reliably telegraphing an overflow.")]
        [SerializeField, Range(0f, 1f)] float baitChanceWhenSafe = 0.34f;

        [SerializeField] AK.Wwise.Event coinIntoWater; 
        [SerializeField] AK.Wwise.Event waterSpill; 

        // The coin shrinks to its cup-fit size early in the carry — begun just after pickup and finished
        // well before the coin nears the glass — so a full-size coin is never over/inside the cup and can
        // never punch through the side wall mid-flight.
        const float ShrinkStartFraction = 0.08f;
        const float ShrinkEndFraction = 0.45f;
        // Coins are clamped to stay within this fraction of the interior radius, so even authored slack
        // between the liquid disc and the real glass wall can't let a resting coin touch the side.
        const float WallSafety = 0.92f;
        const int PileSlotsPerLayer = 3;

        Transform pileContainer;
        int pileCount;

        // Precomputed per-coin flight data for one drop, so the whole batch is animated together by a single
        // coroutine (one shared splash, suspense and reveal) instead of independent per-coin coroutines.
        sealed class DropState
        {
            public GameObject Proxy;
            public Vector3 Start;
            public Vector3 Poised;          // hover point just above the surface
            public Vector3 Rest;            // resting slot on the interior floor
            public Vector3 StartScale;
            public Vector3 FittedScale;
            public Quaternion StartRotation;
            public Quaternion CarryRotation;
            public Quaternion ReleaseRotation;
            public Quaternion RestRotation;
            public float SurfaceCrossFraction; // 0..1 of the plunge at which the coin breaks the surface
            public float FittedRadius;          // world radius once cup-fitted, used to keep its edge inside the wall
        }

        void OnEnable()
        {
            ResolveReferences();

            if (gameManager != null)
                gameManager.RoundStarted += OnRoundStarted;
        }

        void OnDisable()
        {
            if (gameManager != null)
                gameManager.RoundStarted -= OnRoundStarted;
        }

        public void Configure(GameManager manager, Transform target)
        {
            if (gameManager != null)
                gameManager.RoundStarted -= OnRoundStarted;

            gameManager = manager;
            glassTarget = target;
            glassVisual = null;   // re-resolve against the new target on next drop

            if (isActiveAndEnabled && gameManager != null)
                gameManager.RoundStarted += OnRoundStarted;
        }

        /// <summary>
        /// Plays the full dramatic drop sequence for <paramref name="coins"/> and invokes
        /// <paramref name="onComplete"/> once it finishes (so the caller resolves the turn). Proxies are
        /// cloned synchronously here, while the source coins are still active — the caller may hide the
        /// originals immediately after this returns.
        /// </summary>
        public void PlayDropSequence(
            TurnActor actor,
            IReadOnlyList<Coin> coins,
            GlassDropResult result,
            Action onComplete)
        {
            if (coins == null || coins.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            CloseBook();

            // Decide the framing up front so the bait reads through the hold and suspense.
            var side = ShouldUseSideCamera(
                result.Overflowed,
                result.TrueSpillChance,
                GameConstants.MaxSpillChance,
                highRiskSideThreshold,
                baitChanceWhenSafe,
                UnityEngine.Random.value);

            EnsureReferences();
            cameraController?.FocusGlass(side);

            ResolveGlassGeometry(
                out var canPile,
                out var surfaceCenter,
                out var surfaceWorldRadius,
                out var floorCenter,
                out var floorWorldRadius);

            var states = BuildDropStates(
                coins, actor, canPile, surfaceCenter, surfaceWorldRadius, floorCenter, floorWorldRadius);

            StartCoroutine(canPile
                ? RunDropSequence(states, coins, surfaceCenter, surfaceWorldRadius, floorWorldRadius, result, onComplete)
                : RunFlightOnlySequence(states, surfaceCenter, onComplete));
        }

        List<DropState> BuildDropStates(
            IReadOnlyList<Coin> coins,
            TurnActor actor,
            bool canPile,
            Vector3 surfaceCenter,
            float surfaceWorldRadius,
            Vector3 floorCenter,
            float floorWorldRadius)
        {
            var states = new List<DropState>(coins.Count);

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                var proxy = CreateCoinProxy(coins[i], actor, i);
                var start = proxy.transform.position;
                var startScale = proxy.transform.localScale;
                var startRotation = proxy.transform.rotation;

                if (!canPile)
                {
                    states.Add(new DropState
                    {
                        Proxy = proxy,
                        Start = start,
                        Poised = surfaceCenter + new Vector3((i - 0.5f) * 0.035f, 0f, i * 0.02f),
                        StartScale = startScale,
                        FittedScale = startScale,
                        StartRotation = startRotation,
                    });
                    continue;
                }

                // Measure the proxy so it shrinks to fit the cup and stacks without clipping the wall. Use the
                // largest dimension (not just x/z) so a coin authored on a different axis still shrinks to fit.
                var bounds = WorldRendererBounds(proxy);
                var coinDiameter = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                var fitScale = CalculateFitScale(coinDiameter, floorWorldRadius * 2f, coinInGlassFillFraction);
                var fittedRadius = coinDiameter * fitScale * 0.5f;
                var fittedThickness = Mathf.Max(bounds.size.y * fitScale, 0.004f);

                var slotIndex = pileCount++;
                var slotOffset = CalculatePileSlotLocalOffset(slotIndex, floorWorldRadius, fittedRadius, fittedThickness);

                // Final hard guarantee: pull the resting and poised points inside the wall so the coin's outer
                // edge can never cross it, whatever the placement math produced or how the liquid disc was
                // authored relative to the real glass. The pile already sits well inside, so this is a net.
                var rest = ClampInsideWall(
                    floorCenter + slotOffset, floorCenter, floorWorldRadius * WallSafety, fittedRadius);

                // Poise directly above where the coin will rest, a little above the surface.
                var poised = ClampInsideWall(
                    surfaceCenter + new Vector3(slotOffset.x, 0f, slotOffset.z) + Vector3.up * hoverHeightAboveSurface,
                    surfaceCenter,
                    surfaceWorldRadius * WallSafety,
                    fittedRadius);

                var carryRotation = CalculateCarryRotation(startRotation, start, poised, carryTiltDegrees);
                var releaseRotation = CalculateReleaseRotation(carryRotation, start, poised, releaseTipDegrees);

                states.Add(new DropState
                {
                    Proxy = proxy,
                    Start = start,
                    Poised = poised,
                    Rest = rest,
                    StartScale = startScale,
                    FittedScale = startScale * fitScale,
                    StartRotation = startRotation,
                    CarryRotation = carryRotation,
                    ReleaseRotation = releaseRotation,
                    RestRotation = CalculateRestRotation(slotIndex),
                    SurfaceCrossFraction = CalculateSurfaceCrossFraction(poised.y, rest.y, surfaceCenter.y),
                    FittedRadius = fittedRadius,
                });
            }

            return states;
        }

        IEnumerator RunDropSequence(
            List<DropState> states,
            IReadOnlyList<Coin> coins,
            Vector3 surfaceCenter,
            float surfaceWorldRadius,
            float floorWorldRadius,
            GlassDropResult result,
            Action onComplete)
        {
            // Phase 1 — pick up & carry: one continuous arc from each coin's spot up and over to its poised
            // point above the water, banking then tipping its leading edge down, and shrinking to its cup-fit
            // size over the final descent so it is interior-sized before it reaches the rim.
            var elapsed = 0f;
            while (elapsed < approachSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / approachSeconds);

                foreach (var s in states)
                {
                    if (s.Proxy == null)
                        continue;

                    s.Proxy.transform.position = CalculateArcPosition(s.Start, s.Poised, t, liftArcHeight);
                    s.Proxy.transform.rotation = CalculateCarryReleaseRotation(s.StartRotation, s.CarryRotation, s.ReleaseRotation, t);
                    s.Proxy.transform.localScale = CalculateStagedDropScale(s.StartScale, s.FittedScale, t);
                }

                yield return null;
            }

            SnapPoised(states);

            // Phase 2 — hold poised over the water for a beat (>= 1s) with a gentle bob: the coins hang,
            // ready, building the tension before they are let go. The bob only ever lifts above the poised
            // point (0..amplitude), so the coins never dip down and touch the water before the plunge.
            var hold = 0f;
            while (hold < holdAboveWaterSeconds)
            {
                hold += Time.deltaTime;
                var bob = (Mathf.Sin(hold * 5f) * 0.5f + 0.5f) * hoverBobAmplitude;

                foreach (var s in states)
                {
                    if (s.Proxy != null)
                        s.Proxy.transform.position = s.Poised + Vector3.up * bob;
                }

                yield return null;
            }

            // Phase 3 — plunge: drop from poised down into the resting slot, accelerating into the water.
            // The splash (ripple + slosh + sound) fires the instant the coins break the surface.
            var splashed = false;
            var crossFraction = states.Count > 0 ? states[0].SurfaceCrossFraction : 0f;
            var plunge = 0f;
            while (plunge < plungeSeconds)
            {
                plunge += Time.deltaTime;
                var raw = Mathf.Clamp01(plunge / plungeSeconds);
                var eased = raw * raw;                       // accelerate downward, like a real drop
                var spin = Mathf.SmoothStep(0f, 1f, raw);

                foreach (var s in states)
                {
                    if (s.Proxy == null)
                        continue;

                    var sinking = Vector3.Lerp(s.Poised, s.Rest, eased);

                    // As the coin sinks, the cup narrows toward the floor; keep its edge inside the wall at the
                    // height it has reached, so it can never clip the side on the way down.
                    var interiorRadius = InteriorWorldRadiusAtHeight(
                        sinking.y, s.Rest.y, floorWorldRadius, surfaceCenter.y, surfaceWorldRadius);
                    s.Proxy.transform.position = ClampInsideWall(
                        sinking, surfaceCenter, interiorRadius * WallSafety, s.FittedRadius);
                    s.Proxy.transform.rotation = Quaternion.Slerp(s.ReleaseRotation, s.RestRotation, spin);
                }

                if (!splashed && raw >= crossFraction)
                {
                    splashed = true;
                    Splash();
                    gameFeel?.PlaySurfaceBreak(coins);   // droplet splash + slow-mo on a tense drop
                }

                yield return null;
            }

            if (!splashed)
            {
                Splash();
                gameFeel?.PlaySurfaceBreak(coins);
            }

            SnapRest(states);
            ParkInPile(states);

            // Phase 4 — suspense: the coins have settled; the water keeps shaking hard while the player waits
            // to see whether it spills. Two decaying aftershocks keep the surface lurching through the beat
            // instead of settling instantly, so it reads as agitated water on the brink.
            var suspense = 0f;
            var aftershocks = 0;
            while (suspense < suspenseSeconds)
            {
                suspense += Time.deltaTime;
                var frac = suspenseSeconds > 0f ? suspense / suspenseSeconds : 1f;

                if (aftershocks == 0 && frac >= 0.33f)
                {
                    aftershocks = 1;
                    glassVisual?.KickRipple(1.6f);
                    glassVisual?.KickSlosh(1.4f);
                }
                else if (aftershocks == 1 && frac >= 0.66f)
                {
                    aftershocks = 2;
                    glassVisual?.KickRipple(1f);
                    glassVisual?.KickSlosh(0.9f);
                }

                yield return null;
            }

            // Phase 5 — reveal: spill over the rim only when it really overflowed. Cut to the dramatic
            // overflow close-up, spill, shake, then HOLD so the cascade is actually seen before the turn
            // resolves (onComplete hands off to the round-won banner / game-over screen).
            if (result.Overflowed)
            {
                cameraController?.FocusOverflow();
                glassVisual?.PlaySpill();
                cameraController?.Shake();

                var revealHold = 0f;
                while (revealHold < overflowRevealHoldSeconds)
                {
                    revealHold += Time.deltaTime;
                    yield return null;
                }
            }

            onComplete?.Invoke();
        }

        // No glass geometry to aim at: keep the old fly-to-a-point-and-fade behaviour, then resolve.
        IEnumerator RunFlightOnlySequence(List<DropState> states, Vector3 surfaceCenter, Action onComplete)
        {
            var elapsed = 0f;
            while (elapsed < approachSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / approachSeconds);

                foreach (var s in states)
                {
                    if (s.Proxy != null)
                        s.Proxy.transform.position = CalculateArcPosition(s.Start, s.Poised, t, liftArcHeight);
                }

                yield return null;
            }

            Splash();

            foreach (var s in states)
            {
                if (s.Proxy != null)
                    Destroy(s.Proxy, proxyLifetimeAfterDrop);
            }

            onComplete?.Invoke();
        }

        void Splash()
        {
            var emitter = glassTarget != null ? glassTarget.gameObject : gameObject;
            coinIntoWater?.Post(emitter);
            glassVisual?.KickRipple(3f);    // hard impact: rings burst out across the surface
            glassVisual?.KickSlosh(2.6f);   // and the whole body lurches and sways
        }

        static void SnapPoised(List<DropState> states)
        {
            foreach (var s in states)
            {
                if (s.Proxy == null)
                    continue;

                s.Proxy.transform.position = s.Poised;
                s.Proxy.transform.rotation = s.ReleaseRotation;
                s.Proxy.transform.localScale = s.FittedScale;
            }
        }

        static void SnapRest(List<DropState> states)
        {
            foreach (var s in states)
            {
                if (s.Proxy == null)
                    continue;

                s.Proxy.transform.position = s.Rest;
                s.Proxy.transform.rotation = s.RestRotation;
                s.Proxy.transform.localScale = s.FittedScale;
            }
        }

        void ParkInPile(List<DropState> states)
        {
            EnsurePileContainer();

            foreach (var s in states)
            {
                if (s.Proxy != null)
                    s.Proxy.transform.SetParent(pileContainer, true);
            }
        }

        GameObject CreateCoinProxy(Coin sourceCoin, TurnActor actor, int index)
        {
            // Fly the coin's *visible* mesh so a model coin flies as itself and a placeholder coin still
            // flies as its cylinder. When a model is showing, the root cylinder renderer is disabled, so we
            // clone the coin's actual model (preserving multi-part meshes and materials) and only fall back
            // to mirroring the cylinder body when no model is present.
            var visibleModel = sourceCoin.ActiveModel;
            GameObject visual;
            Transform visualTransform;

            if (visibleModel != null)
            {
                visual = Instantiate(visibleModel);
                visualTransform = visibleModel.transform;
            }
            else
            {
                var bodyRenderer = sourceCoin.GetComponentInChildren<Renderer>();
                var bodyFilter = sourceCoin.GetComponentInChildren<MeshFilter>();
                visualTransform = bodyRenderer != null ? bodyRenderer.transform : sourceCoin.transform;

                visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);

                var proxyFilter = visual.GetComponent<MeshFilter>();
                if (bodyFilter != null && bodyFilter.sharedMesh != null && proxyFilter != null)
                    proxyFilter.sharedMesh = bodyFilter.sharedMesh;

                var proxyRenderer = visual.GetComponent<Renderer>();
                if (bodyRenderer != null && proxyRenderer != null)
                    proxyRenderer.sharedMaterial = bodyRenderer.sharedMaterial;
            }

            visual.transform.position = visualTransform.position;
            visual.transform.rotation = visualTransform.rotation;
            visual.transform.localScale = visualTransform.lossyScale;

            // A proxy is purely visual; strip any colliders the primitive or model brought along.
            foreach (var proxyCollider in visual.GetComponentsInChildren<Collider>())
                Destroy(proxyCollider);

            // Wrap the visible mesh in a pivot rooted at its measured centre, so the proxy's transform
            // position IS the coin's *visible* centre (and rotation/scale pivot about it). Coin models can
            // carry a large off-centre pivot; without this, placing the proxy transform inside the cup still
            // renders the mesh swung out — straight through the glass wall. The wrapper starts at unit scale,
            // so the staged-shrink (StartScale 1 -> fitScale) scales the visual about its own centre.
            var bounds = WorldRendererBounds(visual);
            var proxy = new GameObject($"{actor} Drop Proxy {sourceCoin.name}");
            proxy.transform.position = bounds.center;
            proxy.transform.rotation = visual.transform.rotation;
            visual.transform.SetParent(proxy.transform, true);

            proxy.transform.position += new Vector3(index * 0.03f, 0.02f, -index * 0.02f);
            return proxy;
        }

        void CloseBook()
        {
            if (bookShop == null)
                bookShop = FindAnyObjectByType<BookShopView>();

            bookShop?.Close();
        }

        void EnsureReferences()
        {
            if (cameraController == null)
                cameraController = FindAnyObjectByType<CameraController>();

            if (gameFeel == null)
                gameFeel = FindAnyObjectByType<GameFeelDirector>();
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
            // Coins rest on the glass's real interior bottom (lifted above the liquid's modeled floor),
            // not at the liquid floor itself — which may be authored below the visible cup.
            floorCenter = ColumnPointToWorld(glassTarget, glassVisual.FloorLocalY + coinRestFloorLift);
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

        public static Vector3 CalculateArcPosition(Vector3 start, Vector3 end, float normalizedTime, float height)
        {
            var t = Mathf.Clamp01(normalizedTime);
            var linear = Vector3.Lerp(start, end, t);
            var arc = Mathf.Sin(t * Mathf.PI) * Mathf.Max(0f, height);
            return linear + Vector3.up * arc;
        }

        /// <summary>
        /// Scale across the drop arc: the coin is picked up at its full table size, then shrinks to its
        /// cup-fit <paramref name="fittedScale"/> early in the flight — between <see cref="ShrinkStartFraction"/>
        /// and <see cref="ShrinkEndFraction"/> of the arc, while it is still out over the table — so it is
        /// already interior-sized long before it nears the glass. A full-size coin (almost as wide as the cup)
        /// is therefore never carried over or into the glass, so it can't punch through the side wall in
        /// flight; it then stays fitted through the poise and the plunge.
        /// </summary>
        public static Vector3 CalculateStagedDropScale(Vector3 startScale, Vector3 fittedScale, float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);

            if (t <= ShrinkStartFraction)
                return startScale;

            var shrinkT = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((t - ShrinkStartFraction) / (ShrinkEndFraction - ShrinkStartFraction)));
            return Vector3.Lerp(startScale, fittedScale, shrinkT);
        }

        /// <summary>
        /// Continuous coin rotation for the single-arc carry: banks toward the direction of travel over the
        /// first half of the flight, then tips the leading edge down to dive into the surface over the
        /// second half. There is no held middle segment.
        /// </summary>
        public static Quaternion CalculateCarryReleaseRotation(
            Quaternion start,
            Quaternion carry,
            Quaternion release,
            float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);

            if (t <= 0.5f)
                return Quaternion.Slerp(start, carry, Mathf.SmoothStep(0f, 1f, t / 0.5f));

            return Quaternion.Slerp(carry, release, Mathf.SmoothStep(0f, 1f, (t - 0.5f) / 0.5f));
        }

        /// <summary>
        /// Decides whether the camera swings to the dramatic side angle for this drop. Always on a real
        /// overflow; on a safe drop, always when the spill chance is at or above
        /// <paramref name="highRiskThreshold01"/> of <paramref name="maxSpillChance"/>, otherwise a random
        /// fake-out with probability <paramref name="baitChance"/> (so the side shot never reliably
        /// telegraphs an overflow). <paramref name="roll"/> is expected in [0,1).
        /// </summary>
        public static bool ShouldUseSideCamera(
            bool overflowed,
            float spillChance,
            float maxSpillChance,
            float highRiskThreshold01,
            float baitChance,
            float roll)
        {
            if (overflowed)
                return true;

            var danger = maxSpillChance > 0f ? Mathf.Clamp01(spillChance / maxSpillChance) : 0f;

            if (danger >= highRiskThreshold01)
                return true;

            return roll < baitChance;
        }

        /// <summary>
        /// Fraction of the plunge (poised height <paramref name="poisedY"/> down to rest height
        /// <paramref name="restY"/>) at which the coin passes the surface height <paramref name="surfaceY"/>.
        /// 0 if it starts at or below the surface; clamped to [0,1].
        /// </summary>
        public static float CalculateSurfaceCrossFraction(float poisedY, float restY, float surfaceY)
        {
            var span = poisedY - restY;

            if (span <= 1e-5f)
                return 0f;

            return Mathf.Clamp01((poisedY - surfaceY) / span);
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
            var tilt = slotIndex * 11f % 7f - 3f; // roughly -3..+3 degrees (flatter, so edges don't clip the floor)
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
        /// Pulls a world point's horizontal position toward the glass's central axis (<paramref name="axisWorld"/>,
        /// only its X/Z are used) so the coin's outer edge — its centre plus <paramref name="coinRadius"/> — can
        /// never reach past the interior wall of radius <paramref name="interiorRadius"/>. The height is left
        /// untouched. A point already inside is returned unchanged, so this only ever moves a coin that would
        /// otherwise clip the side. This is the hard guarantee that a coin can't poke out, independent of the
        /// pile-placement math.
        /// </summary>
        public static Vector3 ClampInsideWall(Vector3 worldPoint, Vector3 axisWorld, float interiorRadius, float coinRadius)
        {
            var maxCenter = Mathf.Max(0f, interiorRadius - coinRadius);
            var offsetX = worldPoint.x - axisWorld.x;
            var offsetZ = worldPoint.z - axisWorld.z;
            var distance = Mathf.Sqrt(offsetX * offsetX + offsetZ * offsetZ);

            if (distance <= maxCenter || distance < 1e-6f)
                return worldPoint;

            var scale = maxCenter / distance;
            return new Vector3(axisWorld.x + offsetX * scale, worldPoint.y, axisWorld.z + offsetZ * scale);
        }

        /// <summary>
        /// Interior wall radius of the (tumbler-tapered) liquid body at world height <paramref name="worldY"/>:
        /// the floor radius at the floor, the surface radius at the surface, linearly between, and clamped to
        /// that span outside it. Used to keep a sinking coin inside the cup as it narrows toward the floor.
        /// </summary>
        public static float InteriorWorldRadiusAtHeight(
            float worldY, float floorY, float floorRadius, float surfaceY, float surfaceRadius)
        {
            if (surfaceY - floorY <= 1e-6f)
                return Mathf.Max(floorRadius, surfaceRadius);

            var t = Mathf.Clamp01((worldY - floorY) / (surfaceY - floorY));
            return Mathf.Lerp(floorRadius, surfaceRadius, t);
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
            // A tight ring that draws further inward with each layer, so the coins mound into a heap centred
            // on the pile rather than a wide spread or an even column.
            var taper = Mathf.Max(0.4f, 1f - layer * 0.2f);
            var ringRadius = availableRadius * 0.22f * taper;

            // Rotate each layer so stacked coins are offset, not perfectly aligned.
            var angle = slotInLayer * (Mathf.PI * 2f / PileSlotsPerLayer) + layer * 0.7f;
            var x = Mathf.Cos(angle) * ringRadius;
            var z = Mathf.Sin(angle) * ringRadius;

            // Each layer rests on top of the previous (first layer sits half a coin above the floor), with a
            // small per-slot bump so coins in one layer are never exactly coplanar (which would z-fight). The
            // extra clearance lifts the coin so its tilted edge can't dip below the floor / through the glass.
            var safeThickness = Mathf.Max(0f, coinThickness);
            var clearance = coinRadius * 0.06f;
            var y = clearance + safeThickness * (layer + 0.5f) + slotInLayer * safeThickness * 0.18f;

            return new Vector3(x, y, z);
        }
    }
}
