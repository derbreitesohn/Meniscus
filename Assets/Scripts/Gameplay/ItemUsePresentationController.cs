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
    ///  • Generic EnemySafeZonePenalty beat (<see cref="ItemEffectKind.EnemySafeZonePenalty"/>) — a bottle tips
    ///    over the cup and pours a stream in (built, currently UNWIRED — no bottle model yet; wire it to a
    ///    future item with this effect).
    ///  • Taro (item id "taro_laps", a ReduceCurrentRisk) — shake the held Leckerlis treats to call Taro the
    ///    cat over; he laps the glass (<see cref="CatController.SummonToDrink"/> + the Cat_Drink clip) and the
    ///    whiskey settles. Keyed by id, since Buy a Round shares the ReduceCurrentRisk effect.
    ///  • Bandana (<see cref="ItemEffectKind.SkipTurn"/>) — pull it up over the camera; the screen blacks out
    ///    for a beat (see <see cref="ScreenFadeOverlay"/>), masking the cut to the dealer's turn that SkipTurn
    ///    already triggered.
    ///  • Pfeifi whistle (item id "dog_whistle", an EnemySafeZonePenalty) — the whistle flies to the mouth and
    ///    is blown; the dog runs to the dealer's side and barks (<see cref="DogController.SummonToHarass"/> +
    ///    the Dog_Steal clip standing in for a bark), rattling the dealer. Keyed by id (its EnemySafeZonePenalty
    ///    effect has no switch case, so the id routes it here).
    ///  • Tschick (<see cref="ItemEffectKind.SafeZoneBonus"/> = Steady Hand) — a POV smoke ritual: the cigarette
    ///    box rises into view, a single cigarette is drawn out of it, the box drops away as the cigarette swings
    ///    up to the lips (close to camera), and it's lit — a spark/flame flare, a warm light and a glowing ember
    ///    (see <see cref="CigaretteFireVfx"/>) — then a drag and a cloud of exhaled smoke as the nerves settle
    ///    (the SafeZoneBonus relief was already applied on use).
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

        [Header("Bottle pour — EnemySafeZonePenalty (parked, unwired)")]
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

        [Header("Pfeifi whistle — pick up from the desk to the mouth (dog_whistle)")]
        [Tooltip("The Pfeifi whistle GameObject resting on the desk. If empty, a scene object named " +
                 "'Whistle Prop Name' is found (even if inactive); failing that, the wired model is instantiated. " +
                 "It is picked up to the mouth POV-style, then put back exactly where it was.")]
        [SerializeField] Transform whistleSceneProp;
        [Tooltip("Scene object name used as the whistle when none is assigned above. Matched leniently: " +
                 "case-insensitive, a 'Pfeifi (1)' clone, the German 'Pfeif…' stem, or 'whistle' all count.")]
        [SerializeField] string whistlePropName = "Pfeifi";
        [Tooltip("Where the whistle is held near the mouth (camera-local). Kept well past the camera near-plane " +
                 "so it doesn't clip / fill the view; a bit below centre, toward the lips.")]
        [SerializeField] Vector3 whistleMouthLocalPos = new(0f, -0.12f, 0.42f);
        [Tooltip("Tilt at the mouth used ONLY for the instantiated fallback model. A scene whistle keeps its " +
                 "own authored rotation (the mouthpiece you aimed at the cam) — this is ignored for it.")]
        [SerializeField] Vector3 whistleHeldEuler = new(35f, 0f, 0f);
        [Tooltip("Extra turn applied to the SCENE whistle as it reaches the mouth, relative to its authored " +
                 "rotation. Default = turned 180° (around up). Restored to the authored rotation on the way back.")]
        [SerializeField] Vector3 whistleMouthTurnEuler = new(0f, 180f, 0f);
        [Tooltip("Desk start pose used ONLY when instantiating the wired model (no scene object found): low + forward, on the desk.")]
        [SerializeField] Vector3 whistleDeskLocalOffset = new(0.14f, -0.42f, 0.62f);
        [SerializeField, Min(0.01f)] float whistleHeldSize = 0.2f;
        [SerializeField, Min(0.05f)] float whistleFlySeconds = 0.55f;
        [Tooltip("Lift of the pickup arc (camera-local up). Bigger = a more pronounced arch up to the face.")]
        [SerializeField, Min(0f)] float whistleFlyArc = 0.2f;
        [Tooltip("Beat held at the mouth as the whistle is blown (audio added later).")]
        [SerializeField, Min(0f)] float whistleBlowSeconds = 0.35f;
        [Tooltip("Optional explicit floor marker where the dog stands to bark at the dealer. If set, it wins " +
                 "outright — drop an empty on open floor beside the dealer (clear of the bar) and the dog goes " +
                 "exactly there. If empty, the spot is computed from the bar's bounds (see below).")]
        [SerializeField] Transform dogHarassSpot;
        [Tooltip("Name of the table/bar object used to size the bar footprint so the dog clears it. Falls back " +
                 "to 'Table' if not found.")]
        [SerializeField] string tableObjectName = "Saloon Table";
        [Tooltip("How far BEYOND the dealer-side edge of the bar the dog stands, so it clears the desk/rails and " +
                 "ends up next to the dealer on open floor (metres).")]
        [SerializeField, Min(0f)] float dogDeskClearance = 0.5f;
        [Tooltip("Sideways offset toward whichever side the dog approached from, so it stands beside the dealer " +
                 "rather than dead-centre in front of him (metres).")]
        [SerializeField, Min(0f)] float dogLateralNudge = 0.7f;
        [SerializeField, Min(0f)] float dogHoldSeconds = 1.6f;

        [Header("Bandana — pull it over the camera (SkipTurn)")]
        [Tooltip("Resting pose of the held bandana in camera-local space, just before it covers the lens.")]
        [SerializeField] Vector3 bandanaHeldLocalPos = new(0f, -0.04f, 0.3f);
        [SerializeField] Vector3 bandanaHeldEuler = Vector3.zero;
        [SerializeField, Min(0.01f)] float bandanaHeldSize = 0.5f;
        [Tooltip("How far below the held pose the bandana starts — it's pulled up from here over the face.")]
        [SerializeField, Min(0f)] float bandanaRaiseFrom = 0.4f;
        [SerializeField, Min(0.01f)] float bandanaRaiseSeconds = 0.22f;
        [Tooltip("How fast the screen blacks out as the bandana covers the camera.")]
        [SerializeField, Min(0.01f)] float coverSeconds = 0.15f;
        [SerializeField, Min(0f)] float holdBlackSeconds = 0.55f;
        [Tooltip("How fast the blackout lifts again onto the dealer's turn.")]
        [SerializeField, Min(0.01f)] float uncoverSeconds = 0.35f;

        [Header("Tschick — light a cigarette (Steady Hand, SafeZoneBonus)")]
        [Tooltip("The cigarette pack model. If empty, the box wired for 'steady_hand' in the item library is " +
                 "used; assign here (Tools ▸ Meniscus ▸ Wire Tschick Animation) to drive the beat directly.")]
        [SerializeField] GameObject cigaretteBoxModel;
        [Tooltip("Material forced onto the pack model's renderers (leave empty to keep the model's own).")]
        [SerializeField] Material cigaretteBoxMaterial;
        [Tooltip("The single cigarette model that is drawn from the pack and lit.")]
        [SerializeField] GameObject cigaretteModel;
        [Tooltip("Material forced onto the single-cigarette renderers (leave empty to keep the model's own).")]
        [SerializeField] Material cigaretteMaterial;

        [Header("Tschick — poses (camera-local: x right, y up, z forward into the view)")]
        [Tooltip("Resting pose of the pack when it's first raised into view.")]
        [SerializeField] Vector3 boxHeldLocalPos = new(-0.17f, -0.19f, 0.52f);
        [SerializeField] Vector3 boxHeldEuler = new(10f, 22f, -8f);
        [SerializeField, Min(0.01f)] float boxHeldSize = 0.14f;
        [Tooltip("How far below the held pose the pack starts — it rises up into view from here.")]
        [SerializeField, Min(0f)] float boxStowDrop = 0.45f;
        [Tooltip("Where the cigarette sits while still tucked in the pack (it slides out from here).")]
        [SerializeField] Vector3 cigInPackLocalPos = new(-0.13f, -0.12f, 0.5f);
        [Tooltip("Where the cigarette ends up once fully drawn out of the pack, before going to the lips.")]
        [SerializeField] Vector3 cigDrawnLocalPos = new(-0.08f, 0.05f, 0.46f);
        [Tooltip("Orientation of the cigarette while it's drawn from the pack (camera-local euler). Dial this " +
                 "in for the model's own axes.")]
        [SerializeField] Vector3 cigDrawnEuler = new(0f, 0f, 70f);
        [Tooltip("Where the cigarette rests at the lips, close to the camera, for the light-up.")]
        [SerializeField] Vector3 cigMouthLocalPos = new(0.015f, -0.135f, 0.32f);
        [Tooltip("Orientation of the cigarette at the lips (camera-local euler).")]
        [SerializeField] Vector3 cigMouthEuler = new(0f, 0f, 12f);
        [SerializeField, Min(0.01f)] float cigaretteHeldSize = 0.17f;
        [Tooltip("Which end of the cigarette lights — flip if the flame sits on the lips end instead of the tip.")]
        [SerializeField] bool flipCigaretteTip;
        [Tooltip("Fine nudge of the flame off the auto-detected tip (cigarette-local metres).")]
        [SerializeField] Vector3 cigaretteTipLocalOffset = Vector3.zero;

        [Header("Tschick — timing")]
        [SerializeField, Min(0.05f)] float boxRaiseSeconds = 0.5f;     // pack rises into view
        [SerializeField, Min(0.05f)] float cigDrawSeconds = 0.55f;     // single cigarette slides out
        [SerializeField, Min(0.05f)] float toMouthSeconds = 0.6f;      // pack drops, cigarette to the lips
        [SerializeField, Min(0.05f)] float flareSeconds = 0.5f;        // the strike of the lighter
        [SerializeField, Min(0f)] float dragSeconds = 0.7f;            // the pull — ember brightens
        [SerializeField, Min(0f)] float exhaleHoldSeconds = 1.5f;      // hold on the exhaled smoke
        [SerializeField, Min(0.05f)] float cigLowerSeconds = 0.5f;     // lower the cigarette out of frame

        [Header("Effect banner (floating text)")]
        [SerializeField, Min(0f)] float bannerFadeSeconds = 0.25f;
        [SerializeField, Min(0f)] float bannerHoldSeconds = 1.1f;
        [Tooltip("How far above the glass the effect text floats (metres in world space).")]
        [SerializeField] float bannerWorldLift = 0.35f;

        // Keyed by item id (critter-summon beats) since they share an effect with another item.
        const string TaroItemId = "taro_laps";
        const string WhistleItemId = "dog_whistle";

        SpyglassScopeView scopeView;
        ItemEffectBanner banner;
        ScreenFadeOverlay fade;
        CatController cat;
        DogController dog;
        Transform resolvedWhistle;
        Bounds barBounds;
        bool barBoundsResolved;
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

            // Taro (ReduceCurrentRisk) and the whistle (EnemySafeZonePenalty) are keyed by id, not effect:
            // they summon a specific critter, and their effects aren't handled in the switch below (they'd
            // fall to default and get no performance), so the id routes them to their bespoke beat.
            if (item.Id == TaroItemId)
            {
                StartCoroutine(PlayTaroDrink(item));
                return;
            }

            if (item.Id == WhistleItemId)
            {
                StartCoroutine(PlayWhistleSummon(item));
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

                // Bandana (SkipTurn): pull it up over the camera — a short blackout masking the cut to the
                // dealer's turn (SkipTurn already advanced the turn before this beat).
                case ItemEffectKind.SkipTurn:
                    StartCoroutine(PlayBandanaCover(item));
                    break;

                // Tschick / Steady Hand (SafeZoneBonus): draw a cigarette from the box and light it — a POV
                // smoke ritual. SafeZoneBonus is unique to this item, so the effect kind routes it cleanly.
                case ItemEffectKind.SafeZoneBonus:
                    StartCoroutine(PlaySteadyHandSmoke(item));
                    break;

                // EnemySafeZonePenalty has no case here: PlayBottlePourReveal is built and parked, but left
                // UNWIRED for now (there is no bottle model, so it would tip the generic glass prop and read
                // as a glass pouring into a glass). Wire it to a future EnemySafeZonePenalty item once a bottle
                // model exists — the whistle (dog_whistle) routes to its own beat by id above.

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

        // Instantiates the item's own wired model from the library and builds it into a prop. Null when
        // there's no wired model — the beat still plays without the prop.
        GameObject CreateProp(ItemDefinition item, Transform parent, float targetSize)
        {
            var library = gameManager != null ? gameManager.ItemModels : null;

            if (library == null || item == null)
                return null;

            if (!library.TryGetEntry(item.Id, out var entry) || entry.model == null)
                return null;

            return BuildProp(entry.model, entry.material, parent, targetSize, item.Id);
        }

        // Instantiates <paramref name="model"/>, strips colliders, forces <paramref name="material"/> onto its
        // renderers (when given), and wraps it in a pivot centred on its measured bounds (FBX pivots are often
        // off-centre). A null <paramref name="parent"/> makes a free world prop (the caller then positions the
        // pivot); a camera parent makes a held prop in camera-local space. Null when no model is given, so a
        // beat with a missing reference still plays without the prop.
        GameObject BuildProp(GameObject model, Material material, Transform parent, float targetSize, string label = null)
        {
            if (model == null)
                return null;

            GameObject instance;

            try
            {
                instance = Instantiate(model);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[ItemUsePresentation] Could not instantiate model '{model.name}': {exception.Message}");
                return null;
            }

            // A prop is purely visual; strip any colliders the model brought along.
            foreach (var modelCollider in instance.GetComponentsInChildren<Collider>())
                Destroy(modelCollider);

            if (material != null)
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                for (var i = 0; i < renderers.Length; i++)
                    renderers[i].sharedMaterial = material;
            }

            var pivot = new GameObject($"Item Prop {label ?? model.name}");

            if (parent != null)
                pivot.transform.SetParent(parent, false);

            instance.transform.SetParent(pivot.transform, true);

            FitProp(instance, pivot.transform, targetSize);
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

        // Generic EnemySafeZonePenalty beat (parked, unwired — no bottle model yet): a bottle tips over the
        // cup, pours a stream of liquid in (the glass ripples), and a line of text names the effect — then the
        // bottle rights itself and leaves.
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

        // Pfeifi (item id "dog_whistle", an EnemySafeZonePenalty — keyed by id so it routes here rather than
        // falling to the switch's default): the whistle flies up to the mouth and is blown (sound added later), which calls
        // the dog over to the dealer's side to bark at him (his Dog_Steal clip stands in for a bark — no bark
        // clip exists). A line of text + a camera kick sell that the dealer is rattled and will overflow more
        // easily next round (EnemySafeZonePenalty, applied on use — it has no live glass tell, so the text carries it).
        IEnumerator PlayWhistleSummon(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            var camTransform = Camera.main != null ? Camera.main.transform : null;

            // Prefer the whistle GameObject sitting on the desk (a POV pickup); else instantiate the wired model.
            var sceneProp = ResolveWhistleProp();
            Transform prop = null;
            var usingSceneProp = false;
            Transform origParent = null;
            var origLocalPos = Vector3.zero;
            var origLocalRot = Quaternion.identity;
            var origLocalScale = Vector3.one;
            var origActive = true;
            GameObject instantiated = null;

            if (sceneProp != null && camTransform != null)
            {
                prop = sceneProp;
                usingSceneProp = true;
                origParent = prop.parent;
                origLocalPos = prop.localPosition;
                origLocalRot = prop.localRotation;
                origLocalScale = prop.localScale;
                origActive = prop.gameObject.activeSelf;

                prop.gameObject.SetActive(true);
                // Keep it where it sits on the desk, but now in camera space so we can lift it to the mouth.
                prop.SetParent(camTransform, worldPositionStays: true);
                Debug.Log($"[Meniscus] Whistle beat: picking up the authored scene object '{prop.name}'.");
            }
            else if (camTransform != null)
            {
                instantiated = CreateProp(item, camTransform, whistleHeldSize);
                if (instantiated != null)
                {
                    prop = instantiated.transform;
                    prop.localPosition = whistleDeskLocalOffset;
                    Debug.LogWarning(
                        $"[Meniscus] Whistle beat: no scene object resembling '{whistlePropName}' was found, so the " +
                        "wired Pfeifi.fbx was instantiated in code instead. To use your placed prop, name it " +
                        "'Pfeifi' (or assign it to the Item Use Presentation Controller's 'Whistle Scene Prop' field).");
                }
            }

            // Reach down to the desk and bring the whistle up to the mouth in an arc (POV pickup).
            if (prop != null)
            {
                var startLocalPos = prop.localPosition;
                var startLocalRot = prop.localRotation;

                // The scene whistle keeps its authored orientation, turned by whistleMouthTurnEuler (default
                // 180°) once at the mouth. The instantiated fallback just gets the authored down-tilt.
                var endRot = usingSceneProp
                    ? startLocalRot * Quaternion.Euler(whistleMouthTurnEuler)
                    : Quaternion.Euler(whistleHeldEuler);

                for (var t = 0f; t < whistleFlySeconds; t += Time.deltaTime)
                {
                    var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / whistleFlySeconds));
                    prop.localPosition = CoinDropPresentationController.CalculateArcPosition(startLocalPos, whistleMouthLocalPos, e, whistleFlyArc);
                    prop.localRotation = Quaternion.Slerp(startLocalRot, endRot, e);
                    yield return null;
                }
                prop.SetLocalPositionAndRotation(whistleMouthLocalPos, endRot);
            }

            // The blow (audio later) — a short beat held at the mouth.
            for (var t = 0f; t < whistleBlowSeconds; t += Time.deltaTime)
                yield return null;

            // Keep the camera where it is — no move/rise. The dog runs over from wherever it was wandering.
            var pup = ResolveDog();
            var barking = false;

            if (pup != null)
            {
                var floorY = pup.transform.position.y;
                var dealerSpot = ResolveDealerSpot(floorY);
                var standSpot = ResolveDogStandSpot(dealerSpot, floorY, pup.transform.position);
                pup.SummonToHarass(standSpot, dealerSpot, dogHoldSeconds, () => barking = true);
            }
            else
            {
                barking = true;   // no dog to wait on — still play the text beat
            }

            // Keep the whistle at the mouth while the dog runs over (timeout-guarded).
            var waited = 0f;
            while (!barking && waited < maxSummonWait)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            // The dog barks at the dealer: a camera kick + the effect text. Anchored to the glass (the camera
            // hasn't moved, so the dog may be off to the side) so the text always reads.
            cameraController?.Shake();
            EnsureBanner();
            banner.Show(
                $"DEALER RATTLED\n<size=34>+{Mathf.RoundToInt(item.Magnitude)} RISK NEXT ROUND</size>",
                GlassAnchorTransform(),
                Vector3.up * bannerWorldLift);
            yield return FadeBanner(0f, 1f, bannerFadeSeconds);

            for (var t = 0f; t < dogHoldSeconds; t += Time.deltaTime)
                yield return null;

            yield return FadeBanner(1f, 0f, bannerFadeSeconds);
            banner.Hide();

            // Put the desk whistle back exactly where it was (or remove the instantiated stand-in).
            if (usingSceneProp && prop != null)
            {
                prop.SetParent(origParent, worldPositionStays: false);
                prop.localPosition = origLocalPos;
                prop.localRotation = origLocalRot;
                prop.localScale = origLocalScale;
                prop.gameObject.SetActive(origActive);
            }
            else if (instantiated != null)
            {
                Destroy(instantiated);
            }

            // The camera was never moved, so nothing to hand back — it's still the player's turn.
            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        // The whistle to pick up: the assigned desk object, else a scene object whose name looks like the
        // authored whistle (found even if inactive), else null (the caller instantiates the wired model
        // instead). Cached after first find.
        Transform ResolveWhistleProp()
        {
            if (whistleSceneProp != null)
                return whistleSceneProp;

            if (resolvedWhistle != null)
                return resolvedWhistle;

            // Name match is deliberately lenient (see IsWhistleName) so a hand-placed object is used even if
            // it's named "pfeifi", the German "Pfeife", or a "Pfeifi (1)" clone — rather than silently
            // instantiating the FBX. Inactive objects are included so it works before the scene is saved.
            var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && IsWhistleName(transforms[i].name))
                {
                    resolvedWhistle = transforms[i];
                    return resolvedWhistle;
                }
            }

            return null;
        }

        // True when a scene object's name reads as the authored whistle: a case-insensitive match on the
        // configured whistlePropName (also matching a "Name (1)" clone), the German "Pfeif…" stem (covers
        // Pfeife / Pfeifi / pfeifi), or "whistle" (an English rename). Lenient on purpose so the prop you
        // placed in the scene is always preferred over instantiating one in code.
        bool IsWhistleName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            var n = name.Trim().ToLowerInvariant();

            if (!string.IsNullOrEmpty(whistlePropName))
            {
                var wanted = whistlePropName.Trim().ToLowerInvariant();
                if (n == wanted || n.StartsWith(wanted))
                    return true;
            }

            return n.StartsWith("pfeif") || n.Contains("whistle");
        }

        DogController ResolveDog()
        {
            if (dog == null)
                dog = FindAnyObjectByType<DogController>();

            return dog;
        }

        // The dealer sits across the table; aim the dog at the dealer's side of the table — the centre of the
        // enemy's coin row — projected to the dog's (floor) height. Falls back to the glass column if the
        // enemy has no coins out (e.g. between deals).
        Vector3 ResolveDealerSpot(float floorY)
        {
            var enemyCoins = gameManager != null ? gameManager.EnemyCoins : null;

            if (enemyCoins != null && enemyCoins.Count > 0)
            {
                var sum = Vector3.zero;
                var n = 0;

                for (var i = 0; i < enemyCoins.Count; i++)
                {
                    if (enemyCoins[i] != null && enemyCoins[i].gameObject.activeInHierarchy)
                    {
                        sum += enemyCoins[i].transform.position;
                        n++;
                    }
                }

                if (n > 0)
                {
                    var spot = sum / n;
                    spot.y = floorY;
                    return spot;
                }
            }

            if (TryGetGlassSurface(out var surface, out _))
                return new Vector3(surface.x, floorY, surface.z);

            return new Vector3(0f, floorY, 0f);
        }

        // Where the dog actually stands to bark. The dealer's coins sit on the bar, so aiming the dog straight
        // at that point parks it on top of the desk (it clips through). Instead stand it just BEYOND the bar's
        // dealer-side edge, on open floor next to the dealer, nudged to whichever side the dog approached from
        // (so it's beside him, not dead-centre). An explicit dogHarassSpot marker overrides the whole thing.
        Vector3 ResolveDogStandSpot(Vector3 dealerSpot, float floorY, Vector3 dogPos)
        {
            if (dogHarassSpot != null)
            {
                var p = dogHarassSpot.position;
                p.y = floorY;
                return p;
            }

            // The side of the dealer to stand on — keep the dog on the side it's already wandering.
            var side = dogPos.x >= dealerSpot.x ? 1f : -1f;

            if (TryGetBarBounds(out var bar))
            {
                // Dealer is across the table from the player, so the dealer side is whichever face is further
                // from the camera. Stand just past that face. Cameras look down +Z here, so that's max.z, but
                // pick by the dealer spot to stay correct if the layout changes.
                var camZ = Camera.main != null ? Camera.main.transform.position.z : bar.center.z;
                var standZ = dealerSpot.z >= camZ ? bar.max.z + dogDeskClearance : bar.min.z - dogDeskClearance;
                var standX = Mathf.Clamp(dealerSpot.x + side * dogLateralNudge,
                                         bar.min.x - dogDeskClearance, bar.max.x + dogDeskClearance);
                return new Vector3(standX, floorY, standZ);
            }

            // No bar found: push the spot away from the camera (past the dealer) and out to the side.
            var away = Camera.main != null
                ? (dealerSpot.z >= Camera.main.transform.position.z ? 1f : -1f)
                : 1f;
            return new Vector3(dealerSpot.x + side * dogLateralNudge,
                               floorY,
                               dealerSpot.z + away * (dogDeskClearance + 0.8f));
        }

        // World-space bounds of the bar (table + rails) so the dog can clear its whole footprint, not just the
        // tabletop. Encapsulates every renderer under the named group; cached after the first resolve.
        bool TryGetBarBounds(out Bounds bounds)
        {
            if (barBoundsResolved)
            {
                bounds = barBounds;
                return barBounds.size != Vector3.zero;
            }

            barBoundsResolved = true;
            var group = (!string.IsNullOrEmpty(tableObjectName) ? GameObject.Find(tableObjectName) : null)
                        ?? GameObject.Find("Table");

            if (group != null)
            {
                var renderers = group.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds;
                    for (var i = 1; i < renderers.Length; i++)
                        b.Encapsulate(renderers[i].bounds);
                    barBounds = b;
                }
            }

            bounds = barBounds;
            return barBounds.size != Vector3.zero;
        }

        // Bandana (SkipTurn): pull the bandana up over the camera so the screen blacks out for a beat, then
        // lift it onto the dealer's turn. SkipTurn already advanced the turn (and switched to DealerFocus)
        // before this beat fired, so the blackout neatly masks that cut — we never touch the camera here.
        IEnumerator PlayBandanaCover(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            EnsureFade();

            // Pull the bandana up from below toward the lens, like raising it over your face.
            var camTransform = Camera.main != null ? Camera.main.transform : null;
            var bandana = camTransform != null ? CreateProp(item, camTransform, bandanaHeldSize) : null;
            var heldRot = Quaternion.Euler(bandanaHeldEuler);

            if (bandana != null)
            {
                var from = bandanaHeldLocalPos - Vector3.up * bandanaRaiseFrom;
                bandana.transform.SetLocalPositionAndRotation(from, heldRot);
                yield return AnimateProp(bandana, from, bandanaHeldLocalPos, heldRot, bandanaRaiseSeconds, easeOut: true);
            }

            // It comes over the eyes — black the camera out.
            yield return FadeScreen(0f, 1f, coverSeconds);

            if (bandana != null)
                Destroy(bandana);   // hidden behind the black now

            for (var t = 0f; t < holdBlackSeconds; t += Time.deltaTime)
                yield return null;

            // Lift it again — onto the dealer's turn.
            yield return FadeScreen(1f, 0f, uncoverSeconds);

            // Once the blindfold lifts, name what happened.
            EnsureBanner();
            banner.Show("TURN SKIPPED", GlassAnchorTransform(), Vector3.up * bannerWorldLift);
            yield return FadeBanner(0f, 1f, bannerFadeSeconds);
            for (var t = 0f; t < bannerHoldSeconds; t += Time.deltaTime)
                yield return null;
            yield return FadeBanner(1f, 0f, bannerFadeSeconds);
            banner.Hide();

            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        IEnumerator FadeScreen(float from, float to, float seconds)
        {
            if (fade == null)
                yield break;

            if (seconds <= 0f)
            {
                fade.SetAlpha(to);
                yield break;
            }

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                fade.SetAlpha(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }

            fade.SetAlpha(to);
        }

        void EnsureFade()
        {
            if (fade == null)
                fade = ScreenFadeOverlay.Create();
        }

        // Tschick / Steady Hand (SafeZoneBonus): a first-person smoke ritual. The pack rises into view, a
        // single cigarette is drawn from it, the pack drops away as the cigarette swings up to the lips
        // (close to the camera), and it's lit — sparks, a flame and a glowing ember (CigaretteFireVfx) — then
        // a drag brightens the cherry and a cloud of smoke is exhaled. The SafeZoneBonus relief was applied on
        // use; the ritual is its tell (it has no live glass change), with a line of text naming it.
        IEnumerator PlaySteadyHandSmoke(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            var camTransform = Camera.main != null ? Camera.main.transform : null;

            // No camera to stage the POV against — still name the effect so it doesn't read as instant.
            if (camTransform == null)
            {
                yield return ShowEffectBanner(item, exhaleHoldSeconds);
                gameManager?.SetItemPresentationActive(false);
                playing = false;
                yield break;
            }

            // The pack model: the wired box reference, or the library entry for this item as a fallback.
            var boxModel = cigaretteBoxModel;
            var boxMaterial = cigaretteBoxMaterial;

            if (boxModel == null)
            {
                var library = gameManager != null ? gameManager.ItemModels : null;
                if (library != null && library.TryGetEntry(item.Id, out var entry) && entry.model != null)
                {
                    boxModel = entry.model;
                    boxMaterial = entry.material;
                }
            }

            // 1. Raise the pack into view.
            var box = BuildProp(boxModel, boxMaterial, camTransform, boxHeldSize, "tschick_box");
            var boxRot = Quaternion.Euler(boxHeldEuler);
            var boxStow = boxHeldLocalPos - Vector3.up * boxStowDrop;

            if (box != null)
                box.transform.SetLocalPositionAndRotation(boxStow, boxRot);

            yield return AnimateProp(box, boxStow, boxHeldLocalPos, boxRot, boxRaiseSeconds, easeOut: true);

            // 2. Draw a single cigarette up out of the pack.
            var cig = BuildProp(cigaretteModel, cigaretteMaterial, camTransform, cigaretteHeldSize, "tschick_cig");
            var cigDrawnRot = Quaternion.Euler(cigDrawnEuler);

            if (cig != null)
                cig.transform.SetLocalPositionAndRotation(cigInPackLocalPos, cigDrawnRot);

            yield return AnimateProp(cig, cigInPackLocalPos, cigDrawnLocalPos, cigDrawnRot, cigDrawSeconds, easeOut: true);

            // 3. Drop the pack away while the cigarette swings up to the lips, close to the camera.
            var cigMouthRot = Quaternion.Euler(cigMouthEuler);
            var boxExit = boxHeldLocalPos - Vector3.up * (boxStowDrop + 0.2f);
            yield return MovePackAwayAndCigToMouth(
                box, boxHeldLocalPos, boxExit, boxRot,
                cig, cigDrawnLocalPos, cigMouthLocalPos, cigDrawnRot, cigMouthRot, toMouthSeconds);

            if (box != null)
                Destroy(box);

            // 4. Light it: strike the lighter at the tip and catch the ember.
            CigaretteFireVfx fire = null;

            if (cig != null)
            {
                var tipLocal = CigaretteTipLocal(cig, flipCigaretteTip) + cigaretteTipLocalOffset;
                fire = CigaretteFireVfx.Create(cig.transform, tipLocal);
                yield return fire.Strike(flareSeconds);
                fire.BeginSmoke();
            }

            // 5. The drag and the exhale — the nerves settle. Name the effect over it.
            EnsureBanner();
            banner.Show(DescribeEffect(item), GlassAnchorTransform(), Vector3.up * bannerWorldLift);
            yield return FadeBanner(0f, 1f, bannerFadeSeconds);

            // Draw on it: the cherry glows up as the player pulls.
            for (var t = 0f; t < dragSeconds; t += Time.deltaTime)
            {
                var f = dragSeconds > 0f ? Mathf.Clamp01(t / dragSeconds) : 1f;
                fire?.SetEmber(0.55f + 0.45f * Mathf.SmoothStep(0f, 1f, f));
                yield return null;
            }

            // Exhale.
            fire?.SetEmber(1f);
            fire?.Puff(1.5f);

            for (var t = 0f; t < exhaleHoldSeconds; t += Time.deltaTime)
                yield return null;

            yield return FadeBanner(1f, 0f, bannerFadeSeconds);
            banner.Hide();

            // 6. Lower the cigarette out of frame; the ember dies and lingering smoke clears on its own.
            if (cig != null)
            {
                if (fire != null)
                    StartCoroutine(fire.Extinguish(cigLowerSeconds));

                var lowered = cigMouthLocalPos - Vector3.up * 0.45f;
                yield return AnimateProp(cig, cigMouthLocalPos, lowered, cigMouthRot, cigLowerSeconds, easeOut: false);
                Destroy(cig);
            }

            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        // Lower the pack out of frame while the cigarette travels from its drawn pose to the lips, both eased
        // together so it reads as one motion (put the box down, bring the smoke up).
        IEnumerator MovePackAwayAndCigToMouth(
            GameObject box, Vector3 boxFrom, Vector3 boxTo, Quaternion boxRot,
            GameObject cig, Vector3 cigFrom, Vector3 cigTo, Quaternion cigFromRot, Quaternion cigToRot, float seconds)
        {
            if (seconds > 0f)
            {
                for (var t = 0f; t < seconds; t += Time.deltaTime)
                {
                    var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));

                    if (box != null)
                        box.transform.SetLocalPositionAndRotation(Vector3.LerpUnclamped(boxFrom, boxTo, e), boxRot);

                    if (cig != null)
                        cig.transform.SetLocalPositionAndRotation(
                            Vector3.LerpUnclamped(cigFrom, cigTo, e), Quaternion.Slerp(cigFromRot, cigToRot, e));

                    yield return null;
                }
            }

            if (box != null)
                box.transform.SetLocalPositionAndRotation(boxTo, boxRot);

            if (cig != null)
                cig.transform.SetLocalPositionAndRotation(cigTo, cigToRot);
        }

        // The lit end of the held cigarette, in the prop pivot's local space: the far end along the model's
        // longest local axis (a cigarette is long and thin, so that axis is its length). <paramref name="flip"/>
        // picks the other end if the model points the other way. Robust to whatever axis the FBX was authored on.
        static Vector3 CigaretteTipLocal(GameObject pivot, bool flip)
        {
            var filters = pivot.GetComponentsInChildren<MeshFilter>();
            var worldToPivot = pivot.transform.worldToLocalMatrix;
            var have = false;
            var local = new Bounds(Vector3.zero, Vector3.zero);

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;

                var meshBounds = filter.sharedMesh.bounds;
                var meshToPivot = worldToPivot * filter.transform.localToWorldMatrix;

                for (var corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f);
                    var point = meshToPivot.MultiplyPoint3x4(meshBounds.center + Vector3.Scale(meshBounds.extents, sign));

                    if (!have)
                    {
                        local = new Bounds(point, Vector3.zero);
                        have = true;
                    }
                    else
                    {
                        local.Encapsulate(point);
                    }
                }
            }

            if (!have)
                return Vector3.zero;

            var extents = local.extents;
            Vector3 axis;
            float half;

            if (extents.x >= extents.y && extents.x >= extents.z) { axis = Vector3.right; half = extents.x; }
            else if (extents.y >= extents.z) { axis = Vector3.up; half = extents.y; }
            else { axis = Vector3.forward; half = extents.z; }

            return local.center + axis * (half * (flip ? -1f : 1f));
        }

        // Name what the item just did, hold for a beat, then clear — the no-camera fallback for the Tschick.
        IEnumerator ShowEffectBanner(ItemDefinition item, float holdSeconds)
        {
            EnsureBanner();
            banner.Show(DescribeEffect(item), GlassAnchorTransform(), Vector3.up * bannerWorldLift);
            yield return FadeBanner(0f, 1f, bannerFadeSeconds);

            for (var t = 0f; t < holdSeconds; t += Time.deltaTime)
                yield return null;

            yield return FadeBanner(1f, 0f, bannerFadeSeconds);
            banner.Hide();
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
