using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// On-screen bar of the player's held desk items. Visible only during the player's turn; each
    /// entry is a button that uses one of that item via <see cref="GameManager.TryUseItem"/>. Built
    /// procedurally at runtime (like the saloon HUD) so a diegetic desk presentation can replace it
    /// later without touching the use flow.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemBar : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] PlayerInventory inventory;
        [SerializeField] Canvas barCanvas;

        [SerializeField] Transform rowRoot;
        bool subscribedManager;
        bool subscribedInventory;

        void Awake()
        {
            ResolveReferences();
            EnsureCanvas();
        }

        void OnEnable()
        {
            Subscribe();
            Rebuild();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(GameManager manager, PlayerInventory playerInventory)
        {
            Unsubscribe();
            gameManager = manager;
            inventory = playerInventory;
            EnsureCanvas();
            Subscribe();
            Rebuild();
        }

        void Subscribe()
        {
            ResolveReferences();

            if (!subscribedManager && gameManager != null)
            {
                gameManager.StateChanged += OnStateChanged;
                subscribedManager = true;
            }

            if (!subscribedInventory && inventory != null)
            {
                inventory.Changed += Rebuild;
                subscribedInventory = true;
            }
        }

        void Unsubscribe()
        {
            if (subscribedManager && gameManager != null)
                gameManager.StateChanged -= OnStateChanged;

            if (subscribedInventory && inventory != null)
                inventory.Changed -= Rebuild;

            subscribedManager = false;
            subscribedInventory = false;
        }

        void OnStateChanged(GameState state) => Rebuild();

        void Rebuild()
        {
            ResolveReferences();
            EnsureCanvas();

            if (rowRoot == null || barCanvas == null)
                return;

            for (var i = rowRoot.childCount - 1; i >= 0; i--)
            {
                var child = rowRoot.GetChild(i).gameObject;

                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            var isPlayerTurn = gameManager != null && gameManager.CurrentState == GameState.PlayerTurn;
            barCanvas.enabled = isPlayerTurn && inventory != null && inventory.TotalCount > 0;

            if (!barCanvas.enabled)
                return;

            var contents = inventory.Contents();
            const float buttonWidth = 220f;
            const float spacing = 12f;
            var step = buttonWidth + spacing;
            var startX = -(contents.Count - 1) * 0.5f * step;

            for (var i = 0; i < contents.Count; i++)
            {
                var stack = contents[i];
                var captured = stack.Item;
                var label = $"{stack.Item.DisplayName.ToUpperInvariant()}  x{stack.Count}";

                RuntimeUiFactory.CreateButton(
                    rowRoot,
                    $"{stack.Item.Id} Use",
                    label,
                    new Vector2(buttonWidth, 64f),
                    new Vector2(startX + i * step, 0f),
                    16,
                    () => OnUse(captured));
            }
        }

        void OnUse(ItemDefinition item)
        {
            // TryUseItem consumes from the inventory, whose Changed event re-runs Rebuild.
            if (gameManager != null)
                gameManager.TryUseItem(item);
        }

        void EnsureCanvas()
        {
            if (barCanvas != null && rowRoot != null)
                return;

            (barCanvas, rowRoot) = DeskItemBarBuilder.Build(transform);
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (inventory == null && gameManager != null)
                inventory = gameManager.Inventory;

            if (inventory == null)
                inventory = FindAnyObjectByType<PlayerInventory>();
        }
    }
}
