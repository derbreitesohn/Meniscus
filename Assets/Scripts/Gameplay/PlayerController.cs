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
        [Tooltip("How far beside a coin a tap may land and still pick it up, in metres. 0 turns the " +
                 "forgiveness off and demands a hit on the collider itself.")]
        [SerializeField, Min(0f)] float tapRadius = 0.035f;
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
            // A tap is not reported through Mouse unless touch simulation is on, so
            // reading only Mouse.current meant coins could not be picked up at all on
            // a phone. Take the press from whichever device actually reported it.
            if (!TryGetPressPosition(out var pressPosition))
                return;

            ResolveReferences();

            if (gameManager == null)
            {
                Debug.LogWarning("[PlayerController] Click ignored: GameManager reference is missing.");
                return;
            }

            if (gameManager.CurrentState != GameState.PlayerTurn)
                return;

            HandleClick(pressPosition);
        }

        /// <summary>Screen position of a press this frame, from the mouse or a fingertip.</summary>
        static bool TryGetPressPosition(out Vector2 position)
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                position = touch.primaryTouch.position.ReadValue();
                return true;
            }

            position = default;
            return false;
        }

        void HandleClick(Vector2 screenPosition)
        {
            var cameraToUse = raycastCamera != null ? raycastCamera : Camera.main;

            if (cameraToUse == null)
            {
                Debug.LogWarning("[PlayerController] Click ignored: no raycast camera or Camera.main found.");
                return;
            }

            var ray = cameraToUse.ScreenPointToRay(screenPosition);
            var exact = Physics.Raycast(ray, out var hit, maxRaycastDistance, interactionMask);

            // An exact hit always wins, so a deliberate click stays as precise as it was.
            if (exact)
            {
                var coin = hit.collider.GetComponentInParent<Coin>();
                if (coin != null)
                {
                    ToggleCoinSelection(coin);
                    return;
                }

                if (IsGlassHit(hit.collider))
                {
                    SubmitSelectedCoins();
                    return;
                }
            }

            // Only then forgive a near miss. A fingertip covers far more of the table than a
            // cursor does and the coins are small props on it, so a tap that landed a few
            // millimetres wide - onto the table, or nothing at all - simply did nothing.
            // Sweeping a small sphere down the same ray picks up the coin the player was
            // plainly aiming at. The glass is deliberately not forgiven this way: pouring is
            // the irreversible move, and it should take a tap that actually lands on it.
            var grazed = NearestCoinAlong(ray);
            if (grazed != null)
                ToggleCoinSelection(grazed);
        }

        /// <summary>Nearest selectable player coin the ray passes within <see cref="tapRadius"/> of.</summary>
        Coin NearestCoinAlong(Ray ray)
        {
            if (tapRadius <= 0f)
                return null;

            var hits = Physics.SphereCastAll(ray, tapRadius, maxRaycastDistance, interactionMask);
            Coin best = null;
            var bestDistance = float.MaxValue;

            foreach (var candidate in hits)
            {
                var coin = candidate.collider.GetComponentInParent<Coin>();

                if (coin == null || !coin.isPlayerCoin || coin.IsSpent || !coin.gameObject.activeInHierarchy)
                    continue;

                if (candidate.distance < bestDistance)
                {
                    bestDistance = candidate.distance;
                    best = coin;
                }
            }

            return best;
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

        /// <summary>
        /// Drops any lifted coins without pouring them. The desk item tray calls this when the player
        /// picks an item, so coins and a held item are never both selected (their commit paths are
        /// mutually exclusive). No-op when nothing is selected.
        /// </summary>
        public void ClearCoinSelection() => ClearSelection();

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
