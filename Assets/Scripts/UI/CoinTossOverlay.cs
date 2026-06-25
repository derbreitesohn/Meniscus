using System;
using System.Collections;
using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// The start-of-round coin toss. The player calls GOLD or COPPER, a two-faced coin (gold on one side,
    /// copper on the other, same size) tumbles in front of the camera and settles, and whoever called the
    /// landed face takes the first turn. The coin lives in world space in front of the camera (over the live
    /// scene, no backdrop), because a screen-space overlay would always paint over a 3D object; only the
    /// title/subtitle/call buttons are UI, drawn on top. The coin is two copies of the gold model placed
    /// back-to-back, the rear one tinted copper, so both faces are the same size and sculpt. Play-mode only:
    /// in EditMode there is no overlay and the GameManager falls back to the player starting.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoinTossOverlay : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        [SerializeField] CanvasGroup group;
        [SerializeField] Text titleText;
        [SerializeField] Text subtitleText;
        [SerializeField] Button goldButton;
        [SerializeField] Button copperButton;

        AK.Wwise.Event coinFlipEvent;

        [Header("3D Coin Staging")]
        [Tooltip("Distance in front of the camera at which the coin tumbles.")]
        [SerializeField] float coinDistance = 1.2f;
        [Tooltip("World-space diameter the coin is auto-fit to, regardless of the model's authored size.")]
        [SerializeField] float coinTargetDiameter = 0.4f;
        [Tooltip("Separation between the gold and copper faces as a multiple of the coin's thickness. 1 = " +
                 "flush back-to-back; small values clip them into each other so the pair reads as one coin. " +
                 "Keep it just above 0 so the front face doesn't z-fight.")]
        [SerializeField, Range(0f, 1f)] float faceSeparationScale = 0.2f;
        [Tooltip("Tint multiplied onto the rear face to make it read as copper.")]
        [SerializeField] Color copperTint = new(0.80f, 0.46f, 0.20f, 1f);
        [SerializeField] int flipFullTurns = 5;

        Action<TurnActor> onDecided;
        Coroutine routine;

        GameObject coinModelPrefab;
        Vector3 coinModelScale = Vector3.one;

        GameObject rig;            // parented to the camera; tracks it if it moves
        Transform coinTransform;   // the spinning coin (gold + copper faces)
        Quaternion coinFaceRotation = Quaternion.identity;

        void Awake()
        {
            WireButtons();
            Hide();
        }

        public static CoinTossOverlay CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime Coin Toss Overlay");
            var overlay = root.AddComponent<CoinTossOverlay>();

            var canvas = RuntimeUiFactory.CreateOverlayCanvas(root.transform, "Runtime Coin Toss Canvas", enabled: false);
            // Above the round intro card (40), below the round-won banner (50): the toss is its own gate.
            canvas.sortingOrder = 45;

            var group = canvas.gameObject.AddComponent<CanvasGroup>();

            var title = RuntimeUiFactory.CreateText(
                canvas.transform, "Runtime Coin Toss Title", "CALL THE TOSS",
                new Vector2(0f, 300f), new Vector2(1100f, 120f), 70,
                new Color(0.96f, 0.86f, 0.55f), TextAnchor.MiddleCenter, bold: true);

            var subtitle = RuntimeUiFactory.CreateText(
                canvas.transform, "Runtime Coin Toss Subtitle", "Winner takes the first turn",
                new Vector2(0f, 220f), new Vector2(1100f, 70f), 30,
                new Color(0.82f, 0.72f, 0.58f));

            var goldButton = RuntimeUiFactory.CreateButton(
                canvas.transform, "Runtime Gold Button", "GOLD",
                new Vector2(240f, 64f), new Vector2(-140f, -250f), 26,
                onClick: null, boldLabel: true);

            var copperButton = RuntimeUiFactory.CreateButton(
                canvas.transform, "Runtime Copper Button", "COPPER",
                new Vector2(240f, 64f), new Vector2(140f, -250f), 26,
                onClick: null, boldLabel: true);

            overlay.Configure(canvas, group, title, subtitle, goldButton, copperButton);
            return overlay;
        }

        public void Configure(
            Canvas overlayCanvas, CanvasGroup overlayGroup, Text title, Text subtitle,
            Button gold, Button copper)
        {
            canvas = overlayCanvas;
            group = overlayGroup;
            titleText = title;
            subtitleText = subtitle;
            goldButton = gold;
            copperButton = copper;
            WireButtons();
            Hide();
        }

        /// <summary>
        /// Reveals the toss and waits on a GOLD/COPPER call. The two faces are built from the supplied 3D
        /// model (the gold coin), the rear one tinted copper; <paramref name="decidedCallback"/> fires with
        /// the actor who won the toss once it settles. A null model falls back to a styled cylinder.
        /// </summary>
        public void Show(GameObject modelPrefab, Vector3 modelScale, Action<TurnActor> decidedCallback, AK.Wwise.Event flipEvent)
        {
            onDecided = decidedCallback;
            coinFlipEvent = flipEvent;
            coinModelPrefab = modelPrefab;
            coinModelScale = modelScale == Vector3.zero ? Vector3.one : modelScale;

            // EditMode (tests) has no coroutine tick and never wires the overlay: decide instantly so the
            // round can still progress. The GameManager guards on a null overlay too; this is belt-and-braces.
            if (!Application.isPlaying)
            {
                ResolveDecision(TurnActor.Player);
                return;
            }

            ResetForCall();
            BuildCoinRig();

            if (canvas != null)
                canvas.enabled = true;

            if (group != null)
            {
                group.alpha = 1f;
                group.blocksRaycasts = true;
                group.interactable = true;
            }
        }

        public void Hide()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            TearDownCoinRig();

            if (group != null)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            if (canvas != null)
                canvas.enabled = false;
        }

        // Re-arm the buttons and reset the copy so a re-show reads cleanly.
        void ResetForCall()
        {
            SetButtonsVisible(true);

            if (titleText != null)
                titleText.text = "CALL THE TOSS";

            if (subtitleText != null)
                subtitleText.text = "Winner takes the first turn";
        }

        void OnGoldCalled() => OnCalled(playerCalledGold: true);
        void OnCopperCalled() => OnCalled(playerCalledGold: false);

        void OnCalled(bool playerCalledGold)
        {
            // Ignore a second click while the coin is already spinning.
            if (routine != null)
                return;

            coinFlipEvent?.Post(gameObject);

            SetButtonsVisible(false);

            // The landed face is rolled up front; the spin settles to show it.
            var landedGold = UnityEngine.Random.value < 0.5f;
            var winner = playerCalledGold == landedGold ? TurnActor.Player : TurnActor.Enemy;

            if (titleText != null)
                titleText.text = $"YOU CALLED {(playerCalledGold ? "GOLD" : "COPPER")}";

            if (subtitleText != null)
                subtitleText.text = string.Empty;

            routine = StartCoroutine(FlipRoutine(landedGold, winner));
        }

        // Tumble the coin around the camera-horizontal axis, decelerating to rest on the landed face, then
        // announce the outcome and hand control back. Unscaled time so a slow-mo verdict can't drag it.
        IEnumerator FlipRoutine(bool landedGold, TurnActor winner)
        {
            const float spinDuration = 1.5f, holdAfterReveal = 1.4f;

            // Gold faces the camera at a whole turn; a half turn extra lands on the copper face.
            var finalAngle = flipFullTurns * 360f + (landedGold ? 0f : 180f);

            for (var t = 0f; t < spinDuration; t += Time.unscaledDeltaTime)
            {
                var p = Mathf.Clamp01(t / spinDuration);
                var eased = 1f - Mathf.Pow(1f - p, 3f);   // ease-out cubic decel
                ApplyCoinSpin(finalAngle * eased);
                yield return null;
            }

            ApplyCoinSpin(finalAngle);

            var playerWon = winner == TurnActor.Player;
            if (titleText != null)
                titleText.text = landedGold ? "GOLD" : "COPPER";

            if (subtitleText != null)
                subtitleText.text = playerWon ? "YOU START" : "DEALER STARTS";

            for (var t = 0f; t < holdAfterReveal; t += Time.unscaledDeltaTime)
                yield return null;

            routine = null;
            ResolveDecision(winner);
        }

        // Spin in the rig's local X (the camera's right axis), layered on the resting "gold faces camera" pose.
        void ApplyCoinSpin(float angle)
        {
            if (coinTransform != null)
                coinTransform.localRotation = Quaternion.AngleAxis(angle, Vector3.right) * coinFaceRotation;
        }

        void BuildCoinRig()
        {
            TearDownCoinRig();

            var cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (cam == null)
                return;   // No camera to stage against: buttons still work, the coin is simply skipped.

            rig = new GameObject("Coin Toss Rig");
            rig.transform.SetParent(cam.transform, false);
            rig.transform.localPosition = Vector3.forward * coinDistance;
            rig.transform.localRotation = Quaternion.identity;

            BuildCoin();
        }

        // The coin is two copies of the model, back-to-back: gold facing the camera, copper directly behind
        // it along the coin's own thin axis (never beside it). A half-flip swings the copper face into view,
        // so the landed side is genuinely what the player sees.
        void BuildCoin()
        {
            var coin = new GameObject("Coin Toss Coin");
            coin.transform.SetParent(rig.transform, false);
            coin.transform.localPosition = Vector3.zero;

            var gold = InstantiateCoinModel();
            FitToDiameter(gold, coinTargetDiameter, out var thickness, out var thinAxis);

            // Point the coin's flat (thin) axis at the camera so the gold face reads, and seat copper along
            // that exact axis. Tying both to the detected axis is what prevents the "two coins side by side".
            coinFaceRotation = Quaternion.FromToRotation(thinAxis, Vector3.back);
            AttachFace(gold, coin.transform, Vector3.zero, tint: null);

            var copper = InstantiateCoinModel();
            copper.transform.localScale = gold.transform.localScale;
            AttachFace(copper, coin.transform, -thinAxis * (thickness * faceSeparationScale), tint: copperTint);

            coinTransform = coin.transform;
            ApplyCoinSpin(0f);
        }

        GameObject InstantiateCoinModel()
        {
            if (coinModelPrefab != null)
            {
                var instance = Instantiate(coinModelPrefab);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, coinModelScale);
                return instance;
            }

            // No model wired: a flat gold cylinder reads as a coin well enough to flip.
            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            placeholder.transform.localScale = new Vector3(1f, 0.06f, 1f);
            var renderer = placeholder.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial.color = new Color(0.86f, 0.68f, 0.28f, 1f);
            return placeholder;
        }

        void AttachFace(GameObject instance, Transform parent, Vector3 localPosition, Color? tint)
        {
            StripColliders(instance);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;

            if (tint.HasValue)
                ApplyTint(instance, tint.Value);
        }

        static void ApplyTint(GameObject instance, Color color)
        {
            // MaterialPropertyBlock keeps us from leaking material instances; _BaseColor (URP) and _Color
            // (built-in) cover the shaders the coin model is likely to use.
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var block = new MaterialPropertyBlock();

            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                renderers[i].SetPropertyBlock(block);
            }
        }

        // Uniformly scale the instance so its largest dimension matches the target world diameter (measured
        // at identity, before parenting, so the camera's orientation can't skew it), and report the coin's
        // thickness plus its flat (thin) axis so the copper face can be seated straight behind.
        void FitToDiameter(GameObject instance, float targetDiameter, out float thickness, out Vector3 thinAxis)
        {
            thickness = targetDiameter * 0.08f;
            thinAxis = Vector3.up;

            var size = TryGetLocalSize(instance, out var hasBounds);
            if (!hasBounds)
                return;

            var largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (largest <= 1e-4f)
                return;

            var factor = targetDiameter / largest;
            instance.transform.localScale *= factor;

            // The smallest extent is the coin's flat axis (its thickness).
            var thin = size.y;
            if (size.x <= thin) { thin = size.x; thinAxis = Vector3.right; }
            if (size.z <= thin) { thin = size.z; thinAxis = Vector3.forward; }

            thickness = Mathf.Max(thin * factor, targetDiameter * 0.02f);
        }

        static Vector3 TryGetLocalSize(GameObject instance, out bool hasBounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            hasBounds = renderers.Length > 0;

            if (!hasBounds)
                return Vector3.zero;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.size;
        }

        static void StripColliders(GameObject instance)
        {
            // The toss is presentation-only; keep its colliders out of gameplay raycasts.
            var colliders = instance.GetComponentsInChildren<Collider>();
            for (var i = 0; i < colliders.Length; i++)
                Destroy(colliders[i]);
        }

        void TearDownCoinRig()
        {
            coinTransform = null;

            if (rig != null)
                Destroy(rig);

            rig = null;
        }

        void ResolveDecision(TurnActor winner)
        {
            // Capture and clear first so a stray re-entry can't fire the callback twice.
            var callback = onDecided;
            onDecided = null;
            Hide();
            callback?.Invoke(winner);
        }

        void SetButtonsVisible(bool visible)
        {
            if (goldButton != null)
                goldButton.gameObject.SetActive(visible);

            if (copperButton != null)
                copperButton.gameObject.SetActive(visible);
        }

        void WireButtons()
        {
            if (goldButton != null)
            {
                goldButton.onClick.RemoveListener(OnGoldCalled);
                goldButton.onClick.AddListener(OnGoldCalled);
            }

            if (copperButton != null)
            {
                copperButton.onClick.RemoveListener(OnCopperCalled);
                copperButton.onClick.AddListener(OnCopperCalled);
            }
        }
    }
}
