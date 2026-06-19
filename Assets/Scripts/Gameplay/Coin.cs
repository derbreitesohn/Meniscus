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

            Debug.Log(
                $"[Coin] Configured {name}: type={GameConstants.GetDisplayNameForSize(size)}, size={size}, risk={riskContribution:0.##}, " +
                $"basePayout={basePayout}, owner={(isPlayerCoin ? "Player" : "Enemy")}.");
        }

        public void ApplyDefaultsForSize()
        {
            riskContribution = GameConstants.GetRiskForSize(size);
            basePayout = GameConstants.GetBasePayoutForSize(size);
            ApplyVisualsForSize();

            Debug.Log(
                $"[Coin] Applied defaults to {name}: type={GameConstants.GetDisplayNameForSize(size)}, size={size}, risk={riskContribution:0.##}, " +
                $"basePayout={basePayout}.");
        }

        public void SetSelected(bool selected)
        {
            if (isSpent)
            {
                Debug.Log($"[Coin] Selection ignored for spent coin {name}.");
                return;
            }

            CacheOriginalPosition();
            isSelected = selected;
            transform.localPosition = selected
                ? originalLocalPosition + Vector3.up * selectedYOffset
                : originalLocalPosition;

                 if (selected)                      
                coinOnWood?.Post(gameObject);

            Debug.Log($"[Coin] {(selected ? "Selected" : "Deselected")} {name}.");
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

            Debug.Log(
                $"[Coin] Marked spent and hidden: {name}, size={size}, risk={riskContribution:0.##}, " +
                $"basePayout={basePayout}, owner={(isPlayerCoin ? "Player" : "Enemy")}.");
        }

        void CacheOriginalPosition()
        {
            if (hasCachedOriginalPosition)
                return;

            originalLocalPosition = transform.localPosition;
            hasCachedOriginalPosition = true;
        }

        void ApplyVisualsForSize()
        {
            transform.localScale = GameConstants.GetVisualScaleForSize(size);

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
