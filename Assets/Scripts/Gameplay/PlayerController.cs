using System;
using System.Collections.Generic;
using Meniscus.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Meniscus.Gameplay
{
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] Camera raycastCamera;
        [SerializeField] float maxRaycastDistance = 100f;
        [SerializeField] LayerMask interactionMask = ~0;
        [SerializeField] string glassTag = "Glass";

        readonly List<Coin> selectedCoins = new();

        /// <summary>True when the player has coins lifted/selected, ready to pour.</summary>
        public bool HasSelectedCoins => selectedCoins.Count > 0;

        /// <summary>The coins the player currently has lifted/selected (read-only view).</summary>
        public IReadOnlyList<Coin> SelectedCoins => selectedCoins;

        /// <summary>Raised whenever the selection changes (a coin added, removed, or the set cleared),
        /// so UI can preview the would-be pour.</summary>
        public event Action SelectionChanged;

        void OnEnable()
        {
            ResolveReferences();

            if (gameManager != null)
                gameManager.StateChanged += OnGameStateChanged;
        }

        void OnDisable()
        {
            if (gameManager != null)
                gameManager.StateChanged -= OnGameStateChanged;

            ClearSelection();
        }

        void Update()
        {
            var mouse = Mouse.current;

            if (mouse == null)
                return;

            if (!mouse.leftButton.wasPressedThisFrame)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // DEV: swallow clicks that land on the dev settings overlay. Remove with Assets/Scripts/Dev.
            if (Meniscus.Dev.DevSettingsPanel.IsPointerOverPanel)
                return;
#endif

            ResolveReferences();

            if (gameManager == null)
            {
                Debug.LogWarning("[PlayerController] Click ignored: GameManager reference is missing.");
                return;
            }

            if (gameManager.CurrentState != GameState.PlayerTurn)
                return;

            HandleClick();
        }

        void HandleClick()
        {
            var cameraToUse = raycastCamera != null ? raycastCamera : Camera.main;

            if (cameraToUse == null)
            {
                Debug.LogWarning("[PlayerController] Click ignored: no raycast camera or Camera.main found.");
                return;
            }

            var ray = cameraToUse.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (!Physics.Raycast(ray, out var hit, maxRaycastDistance, interactionMask))
                return;

            var coin = hit.collider.GetComponentInParent<Coin>();
            if (coin != null)
            {
                ToggleCoinSelection(coin);
                return;
            }

            if (IsGlassHit(hit.collider))
                SubmitSelectedCoins();
        }

        void ToggleCoinSelection(Coin coin)
        {
            if (coin == null)
                return;

            if (!coin.isPlayerCoin)
                return;

            if (coin.IsSpent || !coin.gameObject.activeInHierarchy)
                return;

            if (selectedCoins.Contains(coin))
            {
                selectedCoins.Remove(coin);
                coin.SetSelected(false);
                SelectionChanged?.Invoke();
                return;
            }

            // Cap how many coins the player can pour in a single turn (mirrors the enemy's per-turn cap).
            // At the limit, the click is refused with a shudder cue — deselect one first to swap.
            if (selectedCoins.Count >= GameConstants.MaxPlayerCoinsPerTurn)
            {
                coin.FlashRejected();
                return;
            }

            selectedCoins.Add(coin);
            coin.SetSelected(true);
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// Pours the player's selected coins. The shared desk USE button calls this, and clicking the
        /// glass still routes here too. No-op when nothing is selected.
        /// </summary>
        public void CommitSelection() => SubmitSelectedCoins();

        void SubmitSelectedCoins()
        {
            if (selectedCoins.Count == 0)
                return;

            gameManager.TryPlayerDropSelectedCoins(selectedCoins);
            ClearSelection();
        }

        void ClearSelection()
        {
            var hadSelection = selectedCoins.Count > 0;

            for (var i = selectedCoins.Count - 1; i >= 0; i--)
            {
                if (selectedCoins[i] != null)
                    selectedCoins[i].SetSelected(false);
            }

            selectedCoins.Clear();

            if (hadSelection)
                SelectionChanged?.Invoke();
        }

        bool IsGlassHit(Collider hitCollider)
        {
            var current = hitCollider.transform;

            while (current != null)
            {
                if (current.gameObject.tag == glassTag)
                    return true;

                current = current.parent;
            }

            return false;
        }

        void OnGameStateChanged(GameState newState)
        {
            if (newState != GameState.PlayerTurn && selectedCoins.Count > 0)
                ClearSelection();
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (raycastCamera == null)
                raycastCamera = Camera.main;
        }
    }
}
