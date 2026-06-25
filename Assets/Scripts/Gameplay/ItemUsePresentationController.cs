using System.Collections;
using Meniscus.Core;
using Meniscus.Items;
using Meniscus.UI;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Plays a short "use" performance when the player spends an item, so an item reads as actually DOING
    /// something instead of silently vanishing. One conductor that dispatches on the item's effect kind, so a
    /// new item's animation is a single new branch here — mirrors how <see cref="CoinDropPresentationController"/>
    /// stages the drop. The logical effect is already applied by <see cref="GameManager.TryUseItem"/> before
    /// the <see cref="GameManager.ItemUsed"/> event reaches us; this layer is purely the show.
    ///
    /// Built so far:
    ///  • Spyglass (<see cref="ItemEffectKind.RevealTrueOdds"/>) — raise it to the eye, it vanishes, the camera
    ///    pushes into the glass and a scope vignette irises over the view with the exact spill % the next pour
    ///    faces (see <see cref="SpyglassScopeView"/>).
    ///  • Coin items (<see cref="ItemEffectKind.PayoutMultiplier"/> = Marked / Lucky Coin,
    ///    <see cref="ItemEffectKind.ForceEnemyCoins"/> = Dealer's Debt) — a coin arcs into the glass and
    ///    splashes, with a line of text naming the effect (see <see cref="ItemEffectBanner"/>).
    ///  • Round for the Dealer (<see cref="ItemEffectKind.EnemySafeZonePenalty"/>) — a bottle tips over the cup
    ///    and pours a stream in (built, currently UNWIRED — no bottle model yet).
    ///  • Taro (item id "taro_laps", a ReduceCurrentRisk) — shake the held Leckerlis treats to call Taro the
    ///    cat over; he laps the glass (<see cref="CatController.SummonToDrink"/> + the Cat_Drink clip) and the
    ///    whiskey settles. Keyed by id, since Buy a Round shares the ReduceCurrentRisk effect.
    /// While a performance plays the <see cref="GameManager"/> holds an input lock so a pour can't cut it short.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemUsePresentationController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] CameraController cameraController;
        [SerializeField] GlassManager glassManager;
        [SerializeField] GlassVisualController glassVisual;
        Transform glassTransform;

        [Header("Spyglass — timing")]
        [SerializeField, Min(0f)] float pickupSeconds = 0.6f;   // raise the spyglass up to the eye
        [SerializeField, Min(0f)] float vanishSeconds = 0.22f;  // spyglass shrinks against the lens and is gone
        [SerializeField, Min(0f)] float irisSeconds = 0.45f;    // scope closes / opens around the view
        [SerializeField, Min(0f)] float readSeconds = 1.7f;     // hold on the odds so they are read

        [Header("Spyglass — held prop")]
        [Tooltip("Resting pose of the held spyglass in camera-local space (x right, y up, z forward into the view).")]
        [SerializeField] Vector3 propHeldLocalPos = new(0.12f, -0.11f, 0.42f);
        [Tooltip("Resting rotation of the held spyglass (camera-local euler). Dial this in for the model's own axes.")]
        [SerializeField] Vector3 propHeldEuler = new(6f, -96f, 4f);
        [Tooltip("Camera-space size (metres, largest dimension) the spyglass model is fitted to when held.")]
        [SerializeField, Min(0.01f)] float propHeldSize = 0.32f;
        [Tooltip("How far below the held pose the spyglass starts — it rises up into view from here.")]
        [SerializeField, Min(0f)] float propStowDrop = 0.5f;

        [Header("Coin drop — Marked Coin / Lucky Coin / Dealer's Debt")]
        [Tooltip("How high above the liquid surface the coin starts its drop (metres).")]
        [SerializeField, Min(0f)] float coinDropHeight = 0.42f;
        [SerializeField, Min(0.05f)] float coinDropSeconds = 0.55f;
        [Tooltip("Lift of the coin's drop arc (metres). 0 = straight down.")]
        [SerializeField, Min(0f)] float coinDropArc = 0.05f;
        [Tooltip("Coin size as a fraction of the glass's surface radius (largest dimension).")]
        [SerializeField, Min(0.05f)] float coinSizeFraction = 0.6f;
        [SerializeField, Min(0.05f)] float coinSinkSeconds = 0.45f;

        [Header("Bottle pour — Round for the Dealer")]
        [Tooltip("How high above the surface the bottle mouth pours from (metres).")]
        [SerializeField, Min(0f)] float pourHeight = 0.42f;
        [Tooltip("Bottle size as a multiple of the glass's surface radius (largest dimension).")]
        [SerializeField, Min(0.1f)] float bottleSizeFactor = 2.4f;
        [SerializeField, Range(0f, 160f)] float bottlePourAngle = 108f;
        [SerializeField, Min(0.05f)] float bottleTiltSeconds = 0.5f;
        [SerializeField, Min(0f)] float pourHoldSeconds = 1.2f;
        [SerializeField, Min(0.001f)] float streamWidth = 0.018f;
        [Tooltip("Colour of the poured liquid stream (matched to the whiskey by default).")]
        [SerializeField] Color pourLiquidColor = new(0.72f, 0.40f, 0.11f, 0.85f);

        [Header("Taro — shake the treats, summon the cat to drink")]
        [Tooltip("Resting pose of the held Leckerlis (treats) in camera-local space before they're shaken.")]
        [SerializeField] Vector3 treatsHeldLocalPos = new(0.16f, -0.15f, 0.5f);
        [SerializeField] Vector3 treatsHeldEuler = Vector3.zero;
        [SerializeField, Min(0.01f)] float treatsHeldSize = 0.22f;
        [Tooltip("How fast / hard / wide the treats are shaken in the air to call Taro over.")]
        [SerializeField, Min(0f)] float treatsShakeFreq = 22f;
        [SerializeField, Min(0f)] float treatsShakeAmp = 0.02f;
        [SerializeField, Range(0f, 45f)] float treatsShakeAngle = 16f;
        [Tooltip("How far back from the glass Taro stands to drink (metres).")]
        [SerializeField, Min(0f)] float catStandoff = 0.6f;
        [Tooltip("How long Taro holds the drink pose.")]
        [SerializeField, Min(0f)] float catDrinkHoldSeconds = 1.8f;
        [Tooltip("Safety cap on how long we shake the treats waiting for Taro to arrive.")]
        [SerializeField, Min(0f)] float maxSummonWait = 3.5f;

        [Header("Effect banner (floating text)")]
        [SerializeField, Min(0f)] float bannerFadeSeconds = 0.25f;
        [SerializeField, Min(0f)] float bannerHoldSeconds = 1.1f;
        [Tooltip("How far above the glass the effect text floats (metres in world space).")]
        [SerializeField] float bannerWorldLift = 0.35f;

        // Taro is keyed by item id (the cat-summon beat), since Buy a Round shares the ReduceCurrentRisk effect.
        const string TaroItemId = "taro_laps";

        SpyglassScopeView scopeView;
        ItemEffectBanner banner;
        CatController cat;
        bool playing;

        void OnEnable()
        {
            ResolveReferences();

            if (gameManager != null)
                gameManager.ItemUsed += OnItemUsed;
        }

        void OnDisable()
        {
            if (gameManager != null)
                gameManager.ItemUsed -= OnItemUsed;

            // Never strand the input lock if we're torn down mid-performance.
            if (playing)
                gameManager?.SetItemPresentationActive(false);
        }

        public void Configure(GameManager manager)
        {
            if (gameManager != null)
                gameManager.ItemUsed -= OnItemUsed;

            gameManager = manager;

            if (isActiveAndEnabled && gameManager != null)
                gameManager.ItemUsed += OnItemUsed;
        }

        void OnItemUsed(ItemDefinition item)
        {
            // Edit-mode tests apply effects with no performance; and never overlap two performances (the
            // GameManager lock also blocks a second use — this is the net for re-entrancy).
            if (item == null || !Application.isPlaying || playing)
                return;

            // Taro is keyed by id, not effect: it summons the cat, and Buy a Round shares ReduceCurrentRisk.
            if (item.Id == TaroItemId)
            {
                StartCoroutine(PlayTaroDrink(item));
                return;
            }

            switch (item.Effect)
            {
                case ItemEffectKind.RevealTrueOdds:
                    StartCoroutine(PlaySpyglassReveal(item));
                    break;

                // Coin-themed items (Marked Coin / Lucky Coin pay-multipliers, Dealer's Debt) drop a coin
                // into the glass with a line of text naming what just happened.
                case ItemEffectKind.PayoutMultiplier:
                case ItemEffectKind.ForceEnemyCoins:
                    StartCoroutine(PlayCoinDropReveal(item));
                    break;

                // Round for the Dealer (EnemySafeZonePenalty) → PlayBottlePourReveal is built and parked, but
                // left UNWIRED for now: there is no bottle model, so it would tip the generic glass prop and
                // read as a glass pouring into a glass. Re-add the case once a bottle model is wired.

                // Other items get their own bespoke beat here as they are built. Until then they simply
                // apply their effect with no performance (the previous behaviour), so nothing regresses.
                default:
                    break;
            }
        }

        IEnumerator PlaySpyglassReveal(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            // Bring the spyglass up to the eye: a prop held to the camera, rising into view from below.
            var prop = CreateHeldProp(item);
            var heldPos = propHeldLocalPos;
            var stowPos = propHeldLocalPos - Vector3.up * propStowDrop;
            var heldRot = Quaternion.Euler(propHeldEuler);

            if (prop != null)
                prop.transform.SetLocalPositionAndRotation(stowPos, heldRot);

            // 1. Raise the spyglass up to the eye.
            yield return AnimateProp(prop, stowPos, heldPos, heldRot, pickupSeconds, easeOut: true);

            // 2. The spyglass goes up against the lens and is gone — so it isn't in frame for the zoom.
            yield return VanishProp(prop, heldPos, heldRot, vanishSeconds);
            prop = null;

            // 3. Now push the camera into the glass close-up (front, straight on) and iris the scope over the
            // view, ticking the odds gauge up to the live spill chance — looking through the spyglass.
            cameraController?.FocusGlass(false);
            EnsureScopeView();
            scopeView.Show();

            for (var t = 0f; t < irisSeconds; t += Time.deltaTime)
            {
                var a = irisSeconds > 0f ? Mathf.SmoothStep(0f, 1f, t / irisSeconds) : 1f;
                scopeView.SetScopeAmount(a);
                PushLiveOdds(a);   // the number counts up as the scope closes in
                yield return null;
            }

            scopeView.SetScopeAmount(1f);

            // Hold on the reading so the player actually reads the odds the item bought.
            for (var t = 0f; t < readSeconds; t += Time.deltaTime)
            {
                PushLiveOdds(1f);
                yield return null;
            }

            // 4. Iris the scope back open and tear the overlay down — the read is a one-shot, nothing lingers.
            for (var t = 0f; t < irisSeconds; t += Time.deltaTime)
            {
                var a = irisSeconds > 0f ? 1f - Mathf.SmoothStep(0f, 1f, t / irisSeconds) : 0f;
                scopeView.SetScopeAmount(a);
                yield return null;
            }

            scopeView.Hide();

            // Hand the camera back to the player's resting framing.
            cameraController?.SwitchCamera(CameraState.PlayerFocus);

            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        // Feed the scope gauge the live spill chance, scaled by how far the scope has engaged so it reads as
        // counting up. CurrentTrueSpillChance is already a 0..100 % (the chance the next pour rolls against).
        void PushLiveOdds(float reveal01)
        {
            if (scopeView == null || glassManager == null)
                return;

            var chance = glassManager.CurrentTrueSpillChance;
            var danger = Mathf.Clamp01(chance / GameConstants.MaxSpillChance);
            scopeView.SetOdds(chance * Mathf.Clamp01(reveal01), danger);
        }

        IEnumerator AnimateProp(GameObject prop, Vector3 from, Vector3 to, Quaternion rot, float seconds, bool easeOut)
        {
            if (prop == null || seconds <= 0f)
            {
                if (prop != null)
                    prop.transform.SetLocalPositionAndRotation(to, rot);

                yield break;
            }

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var f = Mathf.Clamp01(t / seconds);
                // Raise eases out so it lands softly at the eye; lower eases in as it drops away.
                var e = easeOut ? 1f - (1f - f) * (1f - f) : f * f;
                prop.transform.SetLocalPositionAndRotation(Vector3.LerpUnclamped(from, to, e), rot);
                yield return null;
            }

            prop.transform.SetLocalPositionAndRotation(to, rot);
        }

        // The spyglass is pressed up against the lens and shrinks away to nothing, so it's gone before the
        // camera zooms in — the player then looks "through" it at the glass with no prop in frame. Scaling
        // the (camera-parented) pivot needs no transparent material, so it works for any model.
        IEnumerator VanishProp(GameObject prop, Vector3 heldPos, Quaternion rot, float seconds)
        {
            if (prop == null)
                yield break;

            if (seconds <= 0f)
            {
                Destroy(prop);
                yield break;
            }

            // Drift toward the lens (smaller z = closer to the camera) while shrinking out.
            var towardLens = new Vector3(heldPos.x * 0.4f, heldPos.y * 0.4f, heldPos.z * 0.4f);
            var startScale = prop.transform.localScale;

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                prop.transform.localPosition = Vector3.Lerp(heldPos, towardLens, e);
                prop.transform.localRotation = rot;
                prop.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, e);
                yield return null;
            }

            Destroy(prop);
        }

        // Held to the camera (rises to the eye for the spyglass). Null if there's no camera, so the reveal
        // still plays (camera + scope) without a prop in hand.
        GameObject CreateHeldProp(ItemDefinition item)
        {
            var camTransform = Camera.main != null ? Camera.main.transform : null;
            return camTransform == null ? null : CreateProp(item, camTransform, propHeldSize);
        }

        // Instantiates the item's own wired model, strips colliders, applies its material, and wraps it in a
        // pivot centred on its measured bounds (FBX pivots are often off-centre). A null <paramref name="parent"/>
        // makes a free world prop (the caller then positions the pivot); a camera parent makes a held prop in
        // camera-local space. Null when there's no wired model — the beat still plays without the prop.
        GameObject CreateProp(ItemDefinition item, Transform parent, float targetSize)
        {
            var library = gameManager != null ? gameManager.ItemModels : null;

            if (library == null || item == null)
                return null;

            if (!library.TryGetEntry(item.Id, out var entry) || entry.model == null)
                return null;

            GameObject model;

            try
            {
                model = Instantiate(entry.model);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[ItemUsePresentation] Could not instantiate model for '{item.Id}': {exception.Message}");
                return null;
            }

            // A prop is purely visual; strip any colliders the model brought along.
            foreach (var modelCollider in model.GetComponentsInChildren<Collider>())
                Destroy(modelCollider);

            if (entry.material != null)
            {
                var renderers = model.GetComponentsInChildren<Renderer>();
                for (var i = 0; i < renderers.Length; i++)
                    renderers[i].sharedMaterial = entry.material;
            }

            var pivot = new GameObject($"Item Prop {item.Id}");

            if (parent != null)
                pivot.transform.SetParent(parent, false);

            model.transform.SetParent(pivot.transform, true);

            FitProp(model, pivot.transform, targetSize);
            return pivot;
        }

        // Scale the model so its largest dimension matches the held size, then shift it so its measured centre
        // sits on the pivot origin (mirrors DeskItemBox.FitModelToBox). Camera scale is assumed ~1.
        void FitProp(GameObject model, Transform pivot, float targetSize)
        {
            model.transform.localScale = Vector3.one;

            var bounds = WorldBounds(model);
            var measured = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            var fit = measured > 1e-5f ? targetSize / measured : 1f;
            model.transform.localScale = Vector3.one * fit;

            var centered = WorldBounds(model);
            model.transform.position += pivot.position - centered.center;
        }

        static Bounds WorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        // Marked Coin / Lucky Coin / Dealer's Debt: a coin arcs down into the glass, splashes, and a line of
        // text names what just happened — then the coin sinks out of sight.
        IEnumerator PlayCoinDropReveal(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            cameraController?.FocusGlass(false);

            var haveGlass = TryGetGlassSurface(out var surface, out var radius);
            var target = haveGlass ? surface : FallbackPointInFront();
            var coinSize = haveGlass ? Mathf.Max(0.05f, radius * coinSizeFraction) : 0.12f;
            var coin = CreateProp(item, null, coinSize);

            if (coin != null)
            {
                var start = target + Vector3.up * coinDropHeight;
                coin.transform.position = start;

                for (var t = 0f; t < coinDropSeconds; t += Time.deltaTime)
                {
                    var f = Mathf.Clamp01(t / coinDropSeconds);
                    coin.transform.position = CoinDropPresentationController.CalculateArcPosition(start, target, f, coinDropArc);
                    coin.transform.Rotate(Vector3.up, 540f * Time.deltaTime, Space.World);
                    yield return null;
                }

                coin.transform.position = target;
                Splash();
            }

            EnsureBanner();
            banner.Show(DescribeEffect(item), GlassAnchorTransform(), Vector3.up * bannerWorldLift);
            yield return FadeBanner(0f, 1f, bannerFadeSeconds);

            // The coin sinks into the glass and shrinks away while the text holds.
            yield return SinkAndHold(coin, target);

            yield return FadeBanner(1f, 0f, bannerFadeSeconds);
            banner.Hide();

            if (coin != null)
                Destroy(coin);

            cameraController?.SwitchCamera(CameraState.PlayerFocus);
            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        IEnumerator SinkAndHold(GameObject coin, Vector3 surface)
        {
            var startScale = coin != null ? coin.transform.localScale : Vector3.one;
            var sunk = surface - Vector3.up * 0.06f;

            for (var t = 0f; t < bannerHoldSeconds; t += Time.deltaTime)
            {
                if (coin != null)
                {
                    var s = coinSinkSeconds > 0f ? Mathf.Clamp01(t / coinSinkSeconds) : 1f;
                    coin.transform.position = Vector3.Lerp(surface, sunk, s);
                    coin.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, s);
                }

                yield return null;
            }
        }

        // Round for the Dealer: a bottle tips over the cup, pours a stream of liquid in (the glass ripples),
        // and a line of text names the effect — then the bottle rights itself and leaves.
        IEnumerator PlayBottlePourReveal(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            cameraController?.FocusGlass(false);

            var haveGlass = TryGetGlassSurface(out var surface, out var radius);
            var target = haveGlass ? surface : FallbackPointInFront();
            var bottleSize = haveGlass ? Mathf.Max(0.12f, radius * bottleSizeFactor) : 0.3f;
            var bottle = CreateProp(item, null, bottleSize);

            GameObject stream = null;

            if (bottle != null)
            {
                // Stand the bottle to one side, above the rim, then tip its mouth toward the cup.
                var side = Camera.main != null ? Camera.main.transform.right : Vector3.right;
                var standPos = target + Vector3.up * pourHeight + side * (radius * 1.4f);
                bottle.transform.position = standPos;

                var upright = Quaternion.identity;
                var tipAxis = Vector3.Cross(Vector3.up, (target - standPos)).normalized;
                if (tipAxis.sqrMagnitude < 1e-5f) tipAxis = Vector3.right;
                var poured = Quaternion.AngleAxis(bottlePourAngle, tipAxis) * upright;

                for (var t = 0f; t < bottleTiltSeconds; t += Time.deltaTime)
                {
                    bottle.transform.rotation = Quaternion.Slerp(upright, poured, Mathf.SmoothStep(0f, 1f, t / bottleTiltSeconds));
                    yield return null;
                }
                bottle.transform.rotation = poured;

                // Liquid pours from the (tilted) mouth down into the cup.
                var mouth = standPos + (target - standPos).normalized * (bottleSize * 0.4f);
                stream = CreateStream(mouth, target);
                Splash();

                EnsureBanner();
                banner.Show(DescribeEffect(item), GlassAnchorTransform(), Vector3.up * bannerWorldLift);
                yield return FadeBanner(0f, 1f, bannerFadeSeconds);

                // Hold the pour, keeping the surface agitated.
                var nextKick = 0.25f;
                for (var t = 0f; t < pourHoldSeconds; t += Time.deltaTime)
                {
                    if (t >= nextKick)
                    {
                        nextKick += 0.3f;
                        glassVisual?.KickRipple(1.2f);
                    }
                    yield return null;
                }

                if (stream != null)
                    Destroy(stream);

                // Right the bottle and lift it away.
                for (var t = 0f; t < bottleTiltSeconds; t += Time.deltaTime)
                {
                    bottle.transform.rotation = Quaternion.Slerp(poured, upright, Mathf.SmoothStep(0f, 1f, t / bottleTiltSeconds));
                    yield return null;
                }
            }
            else
            {
                EnsureBanner();
                banner.Show(DescribeEffect(item), GlassAnchorTransform(), Vector3.up * bannerWorldLift);
                yield return FadeBanner(0f, 1f, bannerFadeSeconds);
                for (var t = 0f; t < pourHoldSeconds; t += Time.deltaTime)
                    yield return null;
            }

            yield return FadeBanner(1f, 0f, bannerFadeSeconds);
            banner.Hide();

            if (bottle != null)
                Destroy(bottle);

            cameraController?.SwitchCamera(CameraState.PlayerFocus);
            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        // A coin hitting the surface: ripple + slosh (no-op if there's no glass visual to drive).
        void Splash()
        {
            glassVisual?.KickRipple(2.6f);
            glassVisual?.KickSlosh(2f);
        }

        // A thin liquid column between two world points, built from a primitive so it needs no authored asset.
        GameObject CreateStream(Vector3 from, Vector3 to)
        {
            var stream = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stream.name = "Pour Stream";

            var streamCollider = stream.GetComponent<Collider>();
            if (streamCollider != null)
                Destroy(streamCollider);

            var dir = to - from;
            var length = Mathf.Max(0.001f, dir.magnitude);
            stream.transform.position = (from + to) * 0.5f;
            stream.transform.up = dir / length;                 // a unit cylinder is 2 tall along its Y
            stream.transform.localScale = new Vector3(streamWidth, length * 0.5f, streamWidth);

            // URP-friendly transparent material (a runtime primitive's default renders magenta under URP).
            var renderer = stream.GetComponent<Renderer>();
            renderer.sharedMaterial = GlassVisualController.CreateTransparentLiquidMaterial("Item Pour Stream", pourLiquidColor);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return stream;
        }

        IEnumerator FadeBanner(float from, float to, float seconds)
        {
            if (banner == null)
                yield break;

            if (seconds <= 0f)
            {
                banner.SetAlpha(to);
                yield break;
            }

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                banner.SetAlpha(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }

            banner.SetAlpha(to);
        }

        // A short line naming what the item just did, shown over the glass.
        static string DescribeEffect(ItemDefinition item)
        {
            switch (item.Effect)
            {
                case ItemEffectKind.PayoutMultiplier: return $"NEXT POUR ×{item.Magnitude:0.#}";
                case ItemEffectKind.RoundPayoutMultiplier: return $"PAYOUTS ×{item.Magnitude:0.#} THIS ROUND";
                case ItemEffectKind.ForceEnemyCoins: return $"DEALER POURS {Mathf.RoundToInt(item.Magnitude)}";
                case ItemEffectKind.EnemySafeZonePenalty: return "DEALER DRINKS";
                case ItemEffectKind.ReduceCurrentRisk: return "GLASS EASED";
                case ItemEffectKind.SafeZoneBonus: return $"SHRUG OFF {Mathf.RoundToInt(item.Magnitude)} RISK";
                case ItemEffectKind.SkipTurn: return "TURN PASSED";
                case ItemEffectKind.RevealTrueOdds: return "ODDS REVEALED";
                default: return item.DisplayName != null ? item.DisplayName.ToUpperInvariant() : string.Empty;
            }
        }

        // World point on the liquid surface + its radius, via the glass visual's authored geometry. False when
        // there's no glass visual to read (the beats then fall back to a point in front of the camera).
        bool TryGetGlassSurface(out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            radius = 0.1f;

            if (glassVisual == null)
                return false;

            var glass = glassVisual.transform;
            center = CoinDropPresentationController.ColumnPointToWorld(glass, glassVisual.StableSurfaceLocalY);
            radius = CoinDropPresentationController.HorizontalWorldRadius(glass, glassVisual.SurfaceLocalRadius);
            return true;
        }

        Transform GlassAnchorTransform() => glassTransform;

        Vector3 FallbackPointInFront()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform.position + cam.transform.forward * 1.5f : Vector3.zero;
        }

        void EnsureBanner()
        {
            if (banner == null)
                banner = ItemEffectBanner.Create();
        }

        // Taro: hold up the Leckerlis (treats) and shake them in the air, which calls Taro the cat over to the
        // glass to drink — his lapping settles the whiskey (the ReduceCurrentRisk effect, applied on use, eases
        // the glass) — with a line of text. The cat walk + drink is owned by CatController.SummonToDrink; here
        // we run the treats shake, the camera, and the text, timed to when Taro actually starts drinking.
        IEnumerator PlayTaroDrink(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            // Frame the whole table (so Taro is visible coming over), not a tight glass close-up.
            cameraController?.SwitchCamera(CameraState.TableOverview);

            // Hold the treats up to the camera, ready to shake.
            var camTransform = Camera.main != null ? Camera.main.transform : null;
            var treats = camTransform != null ? CreateProp(item, camTransform, treatsHeldSize) : null;
            var treatsRot = Quaternion.Euler(treatsHeldEuler);

            if (treats != null)
                treats.transform.SetLocalPositionAndRotation(treatsHeldLocalPos, treatsRot);

            // Call Taro over to drink; he handles his own walk + drink and tells us when the lapping starts.
            var taro = ResolveCat();
            var drinking = false;

            if (taro != null && TryGetGlassSurface(out var surface, out _))
            {
                var glassGround = new Vector3(surface.x, taro.transform.position.y, surface.z);
                taro.SummonToDrink(glassGround, catStandoff, catDrinkHoldSeconds, () => drinking = true);
            }
            else
            {
                drinking = true;   // no cat to wait on — still play the treats + text beat
            }

            // Shake the treats in the air until Taro starts drinking (with a safety timeout).
            var waited = 0f;
            while (!drinking && waited < maxSummonWait)
            {
                ShakeTreats(treats, treatsRot, waited);
                waited += Time.deltaTime;
                yield return null;
            }

            // Taro laps: a little splash + the effect text. The glass already eased (ReduceCurrentRisk applied
            // on use), so its danger dome visibly calms here.
            Splash();
            EnsureBanner();
            banner.Show(
                $"TARO DRINKS\n<size=34>−{Mathf.RoundToInt(item.Magnitude)} RISK</size>",
                GlassAnchorTransform(),
                Vector3.up * bannerWorldLift);
            yield return FadeBanner(0f, 1f, bannerFadeSeconds);

            // Keep shaking gently through the drink hold.
            for (var t = 0f; t < catDrinkHoldSeconds; t += Time.deltaTime)
            {
                ShakeTreats(treats, treatsRot, waited + t);
                yield return null;
            }

            yield return FadeBanner(1f, 0f, bannerFadeSeconds);
            banner.Hide();

            if (treats != null)
                Destroy(treats);

            cameraController?.SwitchCamera(CameraState.PlayerFocus);
            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        // A rapid wobble of the held treats — shaking the bag to get the cat's attention.
        void ShakeTreats(GameObject treats, Quaternion baseRot, float time)
        {
            if (treats == null)
                return;

            var sx = Mathf.Sin(time * treatsShakeFreq) * treatsShakeAmp;
            var sy = Mathf.Cos(time * treatsShakeFreq * 1.3f) * treatsShakeAmp;
            treats.transform.localPosition = treatsHeldLocalPos + new Vector3(sx, sy, 0f);
            treats.transform.localRotation = baseRot * Quaternion.Euler(0f, 0f, Mathf.Sin(time * treatsShakeFreq) * treatsShakeAngle);
        }

        CatController ResolveCat()
        {
            if (cat == null)
                cat = FindAnyObjectByType<CatController>();

            return cat;
        }

        void EnsureScopeView()
        {
            if (scopeView == null)
                scopeView = SpyglassScopeView.Create();
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (cameraController == null)
                cameraController = FindAnyObjectByType<CameraController>();

            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();

            if (glassVisual == null)
            {
                var glassObject = GameObject.FindGameObjectWithTag("Glass");

                if (glassObject != null)
                {
                    glassTransform = glassObject.transform;
                    glassVisual = glassObject.GetComponent<GlassVisualController>();
                }

                if (glassVisual == null)
                    glassVisual = FindAnyObjectByType<GlassVisualController>();
            }

            if (glassVisual != null && glassTransform == null)
                glassTransform = glassVisual.transform;
        }
    }
}
