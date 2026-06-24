using System;
using System.Collections;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    [DisallowMultipleComponent]
    public class Coin : MonoBehaviour
    {
        /// <summary>Name given to the instantiated model child by <see cref="ApplyModel"/>.</summary>
        const string ModelChildName = "Coin Model";

        [Header("Coin Definition")]
        public CoinSize size = CoinSize.Medium;
        public float riskContribution = GameConstants.MediumCoinRisk;
        public int basePayout = GameConstants.MediumCoinBasePayout;
        public bool isPlayerCoin = true;

        [Header("Selection Feedback")]
        [SerializeField] float selectedYOffset = 0.12f;

        [Header("Audio")]
        [SerializeField] AK.Wwise.Event coinOnWood;

        // Under-damped spring for the selection lift: snappy with a small overshoot ("pop") instead of a
        // hard snap. Stepped with unscaled time in Update so it keeps gliding during the slow-mo verdict.
        const float LiftStiffness = 260f;
        const float LiftDamping = 20f;
        Spring liftSpring = new Spring(0f, LiftStiffness, LiftDamping);

        // A short side-to-side shudder used to signal a refused click (e.g. the per-turn coin limit is
        // reached). Decaying sine wiggle along the coin's resting X, no lift — purely cosmetic.
        const float RejectShakeSeconds = 0.22f;
        const float RejectShakeAmplitude = 0.03f;
        const float RejectShakeFrequency = 40f;

        Vector3 originalLocalPosition;
        bool hasCachedOriginalPosition;
        bool isSelected;
        bool isSpent;
        GameObject activeModelInstance;
        SelectionGlow glow;
        Coroutine rejectShake;

        public bool IsSelected => isSelected;
        public bool IsSpent => isSpent;

        /// <summary>
        /// The instantiated coin model currently shown, or null when the placeholder cylinder is showing.
        /// Lets presentation code (e.g. the drop animation) mirror the coin's real visible mesh without
        /// reaching into its child hierarchy by name.
        /// </summary>
        public GameObject ActiveModel => activeModelInstance;

        void Awake()
        {
            CacheOriginalPosition();
        }

        void OnValidate()
        {
            riskContribution = Mathf.Max(0f, riskContribution);
            basePayout = Mathf.Max(0, basePayout);
            selectedYOffset = Mathf.Max(0f, selectedYOffset);
        }

        void Update()
        {
            // Spring the selection lift toward its target (play mode only — edit-mode tests have no tick,
            // and SetSelected places the coin directly there). Unscaled time so the lift keeps gliding
            // during the slow-motion verdict, matching the selection glow and the desk-item raise. Idle
            // when already settled, so a resting coin never fights layout that re-homes it.
            if (!Application.isPlaying || !hasCachedOriginalPosition)
                return;

            var target = isSelected ? selectedYOffset : 0f;

            if (Mathf.Approximately(liftSpring.Value, target) && Mathf.Approximately(liftSpring.Velocity, 0f))
                return;

            liftSpring.Step(target, Time.unscaledDeltaTime);
            transform.localPosition = originalLocalPosition + Vector3.up * liftSpring.Value;
        }

        public void Configure(CoinSize newSize, float newRiskContribution, int newBasePayout, bool belongsToPlayer)
        {
            size = newSize;
            riskContribution = Mathf.Max(0f, newRiskContribution);
            basePayout = Mathf.Max(0, newBasePayout);
            isPlayerCoin = belongsToPlayer;
            isSpent = false;
            ApplyVisualsForSize();
            EnsureClickCollider();
            ResetVisualSelection();
        }

        public void ApplyDefaultsForSize()
        {
            riskContribution = GameConstants.GetRiskForSize(size);
            basePayout = GameConstants.GetBasePayoutForSize(size);
            ApplyVisualsForSize();
        }

        public void SetSelected(bool selected)
        {
            if (isSpent)
                return;

            CacheOriginalPosition();
            isSelected = selected;

            // In play mode Update springs the lift in/out for a "pop"; outside play mode (edit-mode tests,
            // no Update tick) place it at the target immediately so logic and tests see the final position.
            if (!Application.isPlaying)
            {
                liftSpring.Snap(selected ? selectedYOffset : 0f);
                transform.localPosition = originalLocalPosition + Vector3.up * liftSpring.Value;
            }

            EnsureGlow();
            glow?.SetActive(selected);

            if (selected)
                coinOnWood?.Post(gameObject);
        }

        public void ResetVisualSelection()
        {
            CacheOriginalPosition();
            isSelected = false;
            liftSpring.Snap(0f);
            transform.localPosition = originalLocalPosition;
            glow?.SetActive(false);
        }

        /// <summary>
        /// A brief side-to-side shudder signalling a refused click — e.g. the player is already holding
        /// the max coins they may pour this turn. Purely cosmetic: selection state is untouched. Safe to
        /// run on a resting (unselected) coin because Update idles once the lift has settled, so it won't
        /// fight the shake. No-op outside play mode or on a spent/inactive coin.
        /// </summary>
        public void FlashRejected()
        {
            if (!Application.isPlaying || isSpent || !isActiveAndEnabled)
                return;

            CacheOriginalPosition();

            if (rejectShake != null)
                StopCoroutine(rejectShake);

            rejectShake = StartCoroutine(RejectShakeRoutine());
        }

        IEnumerator RejectShakeRoutine()
        {
            var elapsed = 0f;

            while (elapsed < RejectShakeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var decay = 1f - Mathf.Clamp01(elapsed / RejectShakeSeconds);
                var offset = Mathf.Sin(elapsed * RejectShakeFrequency) * RejectShakeAmplitude * decay;
                transform.localPosition = originalLocalPosition + new Vector3(offset, 0f, 0f);
                yield return null;
            }

            transform.localPosition = originalLocalPosition;
            rejectShake = null;
        }

        /// <summary>
        /// Re-homes the coin to <paramref name="localPosition"/> and records it as the resting position that
        /// selection lifts from and returns to. Used when layout code repositions coins after configuring a
        /// round (each round re-rolls sizes and the active count, so the authored slot no longer fits), so a
        /// later select/deselect can't snap the coin back to its stale authored position.
        /// </summary>
        public void SetRestingLocalPosition(Vector3 localPosition)
        {
            transform.localPosition = localPosition;
            originalLocalPosition = localPosition;
            hasCachedOriginalPosition = true;
            isSelected = false;
            liftSpring.Snap(0f);
        }

        public void MarkSpent()
        {
            isSpent = true;
            ResetVisualSelection();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Swaps the placeholder cylinder visual for a coin model instance, scaled uniformly to fit the
        /// table. Passing a null prefab restores the primitive visuals, so a coin whose size has no model
        /// assigned still shows the placeholder. Logic (risk, payout, selection) is untouched.
        /// </summary>
        public void ApplyModel(GameObject modelPrefab, Vector3 modelScale)
        {
            if (activeModelInstance != null)
            {
                DestroySafely(activeModelInstance);
                activeModelInstance = null;
            }

            // Also clear any model child left by an editor preview (or a reloaded domain) so a coin never
            // shows two stacked models when ApplyModel runs at the start of a round.
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);

                if (child.name == ModelChildName)
                    DestroySafely(child.gameObject);
            }

            var ownRenderer = GetComponent<MeshRenderer>();

            // Build the model first, and only hide the placeholder once a model is actually showing.
            // A null prefab or a *failed* instantiation must both leave the placeholder visible: a model
            // slot wired to a non-GameObject sub-asset (e.g. an FBX's Mesh instead of its GameObject root)
            // makes Instantiate throw, and that exception must never bubble up — it would abort the coin
            // pool setup mid-round and stop the game from ever reaching the player's turn.
            var instance = TryInstantiateModel(modelPrefab);

            if (instance == null)
            {
                // No model for this size: fall back to the placeholder primitive on this object.
                if (ownRenderer != null)
                    ownRenderer.enabled = true;

                return;
            }

            activeModelInstance = instance;

            // The model carries the look now, so hide the placeholder cylinder mesh. The root scale and
            // collider are left as authored so coin selection (a raycast against the collider) is unchanged.
            if (ownRenderer != null)
                ownRenderer.enabled = false;

            activeModelInstance.name = ModelChildName;
            activeModelInstance.transform.localPosition = Vector3.zero;
            activeModelInstance.transform.localRotation = Quaternion.identity;

            FitModelToCoinFootprint(activeModelInstance, modelScale);
        }

        /// <summary>
        /// Scales a freshly-instantiated coin model so its world footprint matches the placeholder coin it
        /// replaces (this object's per-size root scale), times the optional uniform tuning in
        /// <paramref name="tuning"/> (1 = exact match). The model is measured at ~unit world scale first, so
        /// any FBX — whatever its native units — ends up coin-sized, instead of rendering at its raw model
        /// scale (which is metres-big and dwarfs the table).
        /// </summary>
        void FitModelToCoinFootprint(GameObject model, Vector3 tuning)
        {
            var rootScale = transform.localScale;
            var multiplier = tuning == Vector3.zero
                ? 1f
                : Mathf.Max(tuning.x, Mathf.Max(tuning.y, tuning.z));
            var targetWorldDiameter = Mathf.Max(rootScale.x, rootScale.z) * Mathf.Max(0.0001f, multiplier);

            // Neutralise the coin root's squashed, coin-shaped scale so the model sits at ~unit world scale
            // while we measure its true footprint.
            model.transform.localScale = new Vector3(
                SafeDivide(1f, rootScale.x),
                SafeDivide(1f, rootScale.y),
                SafeDivide(1f, rootScale.z));

            var bounds = CalculateWorldRendererBounds(model);
            var modelWorldDiameter = Mathf.Max(bounds.size.x, bounds.size.z);
            var fitWorldScale = modelWorldDiameter > 1e-5f ? targetWorldDiameter / modelWorldDiameter : 1f;

            // Render the model uniformly at fitWorldScale in world space (counter the root scale again so the
            // flat coin collider never squashes the model).
            model.transform.localScale = new Vector3(
                SafeDivide(fitWorldScale, rootScale.x),
                SafeDivide(fitWorldScale, rootScale.y),
                SafeDivide(fitWorldScale, rootScale.z));

            // Re-centre the model on the coin. An FBX whose pivot isn't at its geometric centre would
            // otherwise render offset from the coin's transform — and from its click collider — so the coin
            // looks mis-placed and clicking the visible model selects a neighbour. Shifting by the measured
            // bounds offset puts the model's centre exactly on the coin's position (and collider centre).
            var centeredBounds = CalculateWorldRendererBounds(model);
            model.transform.position += transform.position - centeredBounds.center;
        }

        static Bounds CalculateWorldRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        GameObject TryInstantiateModel(GameObject modelPrefab)
        {
            if (modelPrefab == null)
                return null;

            try
            {
                return Instantiate(modelPrefab, transform);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[Coin] Could not instantiate the {size} coin model '{modelPrefab.name}'. The model slot " +
                    "is likely wired to a non-GameObject sub-asset (e.g. an FBX Mesh) instead of the FBX's " +
                    "GameObject root; re-assign it by dragging the .fbx onto the slot. Showing the placeholder " +
                    $"instead. ({exception.GetType().Name}: {exception.Message})",
                    this);
                return null;
            }
        }

        /// <summary>
        /// Guarantees the coin's click target is a thin <see cref="BoxCollider"/> sized to the unit cube, so
        /// it tracks the coin's real footprint via the root scale. A <see cref="CapsuleCollider"/> (the
        /// default on a primitive cylinder, and what the scene coins were authored with) collapses into an
        /// oversized *sphere* once the flat per-size scale squashes its height below its diameter — it bulges
        /// above the coin and into its neighbours, so an angled-camera raycast selects the wrong coin.
        /// </summary>
        public void EnsureClickCollider()
        {
            var colliders = GetComponents<Collider>();

            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] is not BoxCollider)
                    DestroySafely(colliders[i]);
            }

            if (!TryGetComponent<BoxCollider>(out var box))
                box = gameObject.AddComponent<BoxCollider>();

            box.center = Vector3.zero;
            box.size = Vector3.one;
        }

        static void DestroySafely(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        static float SafeDivide(float numerator, float denominator) =>
            Mathf.Approximately(denominator, 0f) ? numerator : numerator / denominator;

        // A warm halo that follows the coin and fades in while selected. Built lazily as a standalone
        // follower (see SelectionGlow) so the coin's squashed scale never distorts it.
        void EnsureGlow()
        {
            if (glow == null)
                glow = SelectionGlow.Attach(transform, GlowWorldSize(), new Color(1f, 0.86f, 0.4f, 1f));
        }

        float GlowWorldSize() => Mathf.Max(transform.localScale.x, transform.localScale.z) * 1.45f;

        void CacheOriginalPosition()
        {
            if (hasCachedOriginalPosition)
                return;

            originalLocalPosition = transform.localPosition;
            hasCachedOriginalPosition = true;
        }

        void ApplyVisualsForSize()
        {
            // Always size the root: the click collider is sized off this scale, and ApplyModel counters it
            // on the model child so the collider stays coin-shaped for selection.
            transform.localScale = GameConstants.GetVisualScaleForSize(size);

            // A model coin keeps the model's own material; only the placeholder cylinder gets the size tint.
            if (activeModelInstance != null)
                return;

            var coinRenderer = GetComponentInChildren<Renderer>();

            if (coinRenderer == null)
                return;

            var sourceMaterial = coinRenderer.sharedMaterial;
            var shader = sourceMaterial != null
                ? sourceMaterial.shader
                : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            var instanceMaterial = new Material(shader)
            {
                name = $"{GameConstants.GetDisplayNameForSize(size)} Coin Runtime Material",
                color = GameConstants.GetMaterialColorForSize(size)
            };

            coinRenderer.sharedMaterial = instanceMaterial;
        }
    }
}
