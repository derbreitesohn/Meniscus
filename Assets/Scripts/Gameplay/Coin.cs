using System;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    [DisallowMultipleComponent]
    public class Coin : MonoBehaviour
    {
        [Header("Coin Definition")]
        public CoinSize size = CoinSize.Medium;
        public float riskContribution = GameConstants.MediumCoinRisk;
        public int basePayout = GameConstants.MediumCoinBasePayout;
        public bool isPlayerCoin = true;

        [Header("Selection Feedback")]
        [SerializeField] float selectedYOffset = 0.12f;

        [Header("Audio")]
        [SerializeField] AK.Wwise.Event coinOnWood;

        Vector3 originalLocalPosition;
        bool hasCachedOriginalPosition;
        bool isSelected;
        bool isSpent;
        GameObject activeModelInstance;

        public bool IsSelected => isSelected;
        public bool IsSpent => isSpent;

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

        public void Configure(CoinSize newSize, float newRiskContribution, int newBasePayout, bool belongsToPlayer)
        {
            size = newSize;
            riskContribution = Mathf.Max(0f, newRiskContribution);
            basePayout = Mathf.Max(0, newBasePayout);
            isPlayerCoin = belongsToPlayer;
            isSpent = false;
            ApplyVisualsForSize();
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
            transform.localPosition = selected
                ? originalLocalPosition + Vector3.up * selectedYOffset
                : originalLocalPosition;

            if (selected)
                coinOnWood?.Post(gameObject);
        }

        public void ResetVisualSelection()
        {
            CacheOriginalPosition();
            isSelected = false;
            transform.localPosition = originalLocalPosition;
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
                if (Application.isPlaying)
                    Destroy(activeModelInstance);
                else
                    DestroyImmediate(activeModelInstance);

                activeModelInstance = null;
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

            activeModelInstance.name = "Coin Model";
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

        static float SafeDivide(float numerator, float denominator) =>
            Mathf.Approximately(denominator, 0f) ? numerator : numerator / denominator;

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
