using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Meniscus.UI
{
    /// <summary>
    /// The diegetic replacement for the on-screen item bar: a row of placeholder boxes on the desk,
    /// one per held stack. Subscribes to the same inventory/state events the bar used. Boxes are always
    /// visible; clicking one selects it (raising it + showing Use/Cancel), and Use routes through the
    /// unchanged <see cref="GameManager.TryUseItem"/> (which itself only succeeds on the player's turn).
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemTray : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] PlayerInventory inventory;
        [SerializeField] Transform deskItemsAnchor;
        [SerializeField] string deskObjectName = "Saloon Table";
        [SerializeField] Camera worldCamera;
        [SerializeField, Min(0f)] float clickRaycastDistance = 100f;

        const float BoxSpacing = 0.3f;
        const float DeskClearance = 0.01f;

        readonly List<DeskItemBox> boxes = new();
        DeskItemBox selected;

        bool subscribedManager;
        bool subscribedInventory;

        Bounds deskBounds;
        bool deskResolved;
        bool hasDeskBounds;
        bool positioned;

        public IReadOnlyList<DeskItemBox> Boxes => boxes;
        public DeskItemBox Selected => selected;

        void Awake() => ResolveReferences();

        void OnEnable()
        {
            Subscribe();
            Rebuild();
        }

        void OnDisable() => Unsubscribe();

        // Boxes are clicked via an Input System raycast (legacy OnMouse* messages don't fire under this
        // project's input backend — same reason the book polls its own raycast).
        void Update()
        {
            var mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || boxes.Count == 0)
                return;

            // Don't let clicks fall through the book menu (or any uGUI) onto a box behind it.
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events != null && events.IsPointerOverGameObject())
                return;

            var cam = worldCamera != null ? worldCamera : Camera.main;

            if (cam == null)
                return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());

            if (!Physics.Raycast(ray, out var hit, clickRaycastDistance))
                return;

            var relay = hit.collider.GetComponentInParent<DeskItemTileRelay>();
            relay?.Trigger();
        }

        public void Configure(GameManager manager, PlayerInventory playerInventory)
        {
            Unsubscribe();
            gameManager = manager;
            inventory = playerInventory;
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

        void OnStateChanged(GameState state)
        {
            // Leaving the player's turn cancels any pending selection (mirrors PlayerController).
            if (state != GameState.PlayerTurn)
                Deselect();
        }

        public void Rebuild()
        {
            ResolveReferences();

            if (inventory == null)
                return;

            PositionTrayOnDesk();

            var contents = inventory.Contents();

            // Remove boxes for stacks that no longer exist.
            for (var i = boxes.Count - 1; i >= 0; i--)
            {
                if (StackIndexOf(contents, boxes[i].Item) < 0)
                {
                    if (selected == boxes[i])
                        selected = null;

                    DestroyBox(boxes[i].gameObject);
                    boxes.RemoveAt(i);
                }
            }

            // Add new boxes and refresh counts.
            for (var i = 0; i < contents.Count; i++)
            {
                var stack = contents[i];
                var box = FindBox(stack.Item);

                if (box == null)
                {
                    box = DeskItemTrayBuilder.BuildBox(transform, this, stack.Item);
                    boxes.Add(box);
                }

                box.SetCount(stack.Count);
            }

            Layout();
        }

        void Layout()
        {
            var start = -(boxes.Count - 1) * 0.5f * BoxSpacing;

            for (var i = 0; i < boxes.Count; i++)
                boxes[i].SetRestPosition(new Vector3(start + i * BoxSpacing, 0f, 0f));
        }

        public void OnBoxClicked(DeskItemBox box)
        {
            if (box == null || selected == box)
                return;

            Deselect();
            selected = box;
            box.SetSelected(true);
        }

        public void OnUseClicked(DeskItemBox box)
        {
            if (box == null || box != selected)
                return;

            var used = gameManager != null && gameManager.TryUseItem(box.Item);

            if (used)
                Deselect();         // box may already be gone (stack emptied); Deselect is null-safe
            else
                box.FlashUnavailable();   // not the player's turn — keep selected, show the cue
        }

        public void OnCancelClicked(DeskItemBox box)
        {
            if (box == selected)
                Deselect();
        }

        void Deselect()
        {
            if (selected != null)
                selected.SetSelected(false);

            selected = null;
        }

        DeskItemBox FindBox(ItemDefinition item)
        {
            for (var i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Item == item)
                    return boxes[i];
            }

            return null;
        }

        static int StackIndexOf(IReadOnlyList<ItemStack> contents, ItemDefinition item)
        {
            for (var i = 0; i < contents.Count; i++)
            {
                if (contents[i].Item == item)
                    return i;
            }

            return -1;
        }

        void DestroyBox(GameObject go)
        {
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        // --- Placement (mirrors BookShopView's anchor -> own-transform -> desk-bounds fallback) ---

        void PositionTrayOnDesk()
        {
            if (positioned)
                return;

            if (deskItemsAnchor != null)
            {
                transform.SetPositionAndRotation(deskItemsAnchor.position, deskItemsAnchor.rotation);
                positioned = true;
                return;
            }

            if (transform.position.sqrMagnitude > 0.0001f)
            {
                positioned = true; // already hand-placed on the desk
                return;
            }

            ResolveDesk();

            var cam = Camera.main;

            if (!hasDeskBounds || cam == null)
                return; // try again next Rebuild once a desk/camera exists

            var toCamera = Vector3.ProjectOnPlane(cam.transform.position - deskBounds.center, Vector3.up);
            toCamera = toCamera.sqrMagnitude > 1e-4f ? toCamera.normalized : -Vector3.forward;

            var pos = new Vector3(deskBounds.center.x, deskBounds.max.y + DeskClearance, deskBounds.center.z)
                      + toCamera * (deskBounds.extents.magnitude * 0.35f);

            // Local +X runs along the desk's near edge; boxes lay out along it in Layout().
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(toCamera, Vector3.up));
            positioned = true;
        }

        void ResolveDesk()
        {
            if (deskResolved)
                return;

            deskResolved = true;

            var deskObject = GameObject.Find(deskObjectName)
                             ?? GameObject.Find("Saloon Table")
                             ?? GameObject.Find("Table");

            if (deskObject == null)
            {
                deskResolved = false; // desk not loaded yet — retry on next Rebuild
                return;
            }

            var renderers = deskObject.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return;

            deskBounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                deskBounds.Encapsulate(renderers[i].bounds);

            hasDeskBounds = true;
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
