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

        Transform rowRoot;
        bool subscribed;

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
            gameManager = manager;
            inventory = playerInventory;
            EnsureCanvas();
            Subscribe();
            Rebuild();
        }

        void Subscribe()
        {
            if (subscribed)
                return;

            ResolveReferences();

            if (gameManager != null)
                gameManager.StateChanged += OnStateChanged;

            if (inventory != null)
                inventory.Changed += Rebuild;

            subscribed = gameManager != null || inventory != null;
        }

        void Unsubscribe()
        {
            if (gameManager != null)
                gameManager.StateChanged -= OnStateChanged;

            if (inventory != null)
                inventory.Changed -= Rebuild;

            subscribed = false;
        }

        void OnStateChanged(GameState state) => Rebuild();

        void Rebuild()
        {
            ResolveReferences();
            EnsureCanvas();

            if (rowRoot == null || barCanvas == null)
                return;

            for (var i = rowRoot.childCount - 1; i >= 0; i--)
                Destroy(rowRoot.GetChild(i).gameObject);

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

            barCanvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Desk Item Bar Canvas", enabled: false);

            var row = RuntimeUiFactory.CreateImage(
                barCanvas.transform,
                "Desk Item Row",
                new Vector2(1500f, 84f),
                new Vector2(0f, 130f),
                new Color(0f, 0f, 0f, 0f));   // transparent container; buttons carry the look

            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 130f);

            rowRoot = row.transform;
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
