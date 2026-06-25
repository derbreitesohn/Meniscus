using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Gameplay;
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
        [SerializeField] PlayerController playerController;
        [SerializeField, Min(0f)] float clickRaycastDistance = 100f;

        [Tooltip("Pre-authored desk item box prefab (built by Tools > Meniscus > Author Desk Item Box " +
                 "Prefab). When set, the tray instantiates it per held stack, so you can give the box a " +
                 "model / materials / Animator in the editor; left empty, a placeholder cube box is built " +
                 "in code at runtime.")]
        [SerializeField] DeskItemBox boxPrefab;

        [Header("Hand Delivery")]
        [Tooltip("When on (play mode only), a newly ordered item is carried onto the desk by a hand that " +
                 "reaches in from the side instead of popping into place. Tune the hand's reach, side and " +
                 "timing on the DeskHandDelivery component (auto-added here if not assigned).")]
        [SerializeField] bool deliverItemsByHand = true;
        [Tooltip("The hand that delivers ordered items. Leave empty to auto-add one to this object at " +
                 "runtime; assign one to tune it in the editor before play.")]
        [SerializeField] DeskHandDelivery handDelivery;

        [Header("Book Avoidance")]
        [Tooltip("The desk book prop. Item boxes skip the slots its footprint covers, so a bought item is " +
                 "never placed sitting under the book on the desk. Auto-found at runtime if left empty.")]
        [SerializeField] BookShopView bookShop;
        [Tooltip("Half-width (in metres, along the desk) of the band kept clear around the book's centre " +
                 "when placing item boxes — roughly the book's half-width plus a box's half-width.")]
        [SerializeField, Min(0f)] float bookClearance = 0.45f;

        const float BoxSpacing = 0.3f;
        const float DeskClearance = 0.01f;

        readonly List<DeskItemBox> boxes = new();
        DeskItemBox selected;
        DeskUseButton useButton;

        bool subscribedManager;
        bool subscribedInventory;
        bool subscribedPlayer;

        // True only while servicing a genuine inventory change (a grant/use) at runtime, so the hand
        // carries in just the boxes that newly arrived — not the ones rebuilt on enable / reconfigure.
        bool deliverArrivals;

        Bounds deskBounds;
        bool deskResolved;
        bool hasDeskBounds;
        bool positioned;

        // The slice of the tray's local X axis covered by the book resting on the desk; slots whose box
        // would fall inside it are skipped so an item is never placed under the book.
        float bookLocalX;
        bool bookBandResolved;
        bool hasBookBand;

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
            // Keep the shared USE button bright when there's something to commit (a held item or coins).
            if (useButton != null)
                useButton.SetReady(selected != null || (playerController != null && playerController.HasSelectedCoins));

            var mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
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

            // The shared USE button commits the whole selection (item, then coins).
            var pressedUseButton = hit.collider.GetComponentInParent<DeskUseButton>();
            if (pressedUseButton != null)
            {
                pressedUseButton.PlayPress();
                CommitSelection();
                return;
            }

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
                inventory.Changed += OnInventoryChanged;
                subscribedInventory = true;
            }

            if (!subscribedPlayer && playerController != null)
            {
                playerController.SelectionChanged += OnPlayerSelectionChanged;
                subscribedPlayer = true;
            }
        }

        void Unsubscribe()
        {
            if (subscribedManager && gameManager != null)
                gameManager.StateChanged -= OnStateChanged;

            if (subscribedInventory && inventory != null)
                inventory.Changed -= OnInventoryChanged;

            if (subscribedPlayer && playerController != null)
                playerController.SelectionChanged -= OnPlayerSelectionChanged;

            subscribedManager = false;
            subscribedInventory = false;
            subscribedPlayer = false;
        }

        // Coins and a held item are mutually exclusive: when the player lifts a coin, drop any raised item.
        // (The reverse — picking an item clears coins — is done in OnBoxClicked.)
        void OnPlayerSelectionChanged()
        {
            if (selected != null && playerController != null && playerController.HasSelectedCoins)
                Deselect();
        }

        void OnStateChanged(GameState state)
        {
            // Leaving the player's turn cancels any pending selection (mirrors PlayerController).
            if (state != GameState.PlayerTurn)
                Deselect();

            // The shared USE button is only live on the player's turn (built on demand so it appears the
            // first time it's the player's turn, even before the first inventory rebuild).
            EnsureUseButton();
            useButton.gameObject.SetActive(state == GameState.PlayerTurn);
        }

        // A real grant/use: at runtime, let the hand carry in any boxes this change creates. The initial
        // OnEnable / Configure rebuilds go through Rebuild() directly with the flag off, so pre-owned
        // items just appear without a delivery animation.
        void OnInventoryChanged()
        {
            deliverArrivals = Application.isPlaying && deliverItemsByHand;
            Rebuild();
            deliverArrivals = false;
        }

        public void Rebuild()
        {
            ResolveReferences();

            if (inventory == null)
                return;

            PositionTrayOnDesk();
            ResolveBookBand();
            EnsureUseButton();

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
            List<DeskItemBox> arrivals = null;

            for (var i = 0; i < contents.Count; i++)
            {
                var stack = contents[i];
                var box = FindBox(stack.Item);

                if (box == null)
                {
                    box = CreateBox(stack.Item);
                    box.Slot = NextFreeSlot();
                    boxes.Add(box);

                    if (deliverArrivals && EnsureHandDelivery() != null)
                    {
                        // Mark it before Layout so Layout records its slot without snapping it into view;
                        // the hand carries it there from off-desk instead.
                        box.IsBeingDelivered = true;
                        (arrivals ??= new List<DeskItemBox>()).Add(box);
                    }
                }

                box.SetCount(stack.Count);
            }

            Layout();

            if (arrivals != null)
                for (var i = 0; i < arrivals.Count; i++)
                    handDelivery.Deliver(arrivals[i], SlotPosition(arrivals[i].Slot));
        }

        // Prefer an authored box prefab (so the box carries a model / materials / Animator set in the
        // editor) and fall back to the placeholder cube built in code. Mirrors the glass-spill / coin
        // prefab pattern. instantiateInWorldSpace:false keeps the instance's local transform so Layout
        // can slot it; Initialize binds it to this tray + item using the prefab's serialized parts.
        DeskItemBox CreateBox(ItemDefinition item)
        {
            DeskItemBox box;

            if (boxPrefab != null)
            {
                box = Instantiate(boxPrefab, transform, false);
                box.gameObject.name = $"Desk Item Box ({item.Id})";
                box.Initialize(this, item);
            }
            else
            {
                box = DeskItemTrayBuilder.BuildBox(transform, this, item);
            }

            ApplyItemModel(box, item);
            return box;
        }

        // Show the wired 3D model for this item (e.g. spyglass for Bartender's Spectacles, bandana for Step
        // Outside) in place of the placeholder cube. Items with no library entry keep the cube. Mirrors how
        // coins resolve their per-size model from GameManager.CoinModels.
        void ApplyItemModel(DeskItemBox box, ItemDefinition item)
        {
            var library = gameManager != null ? gameManager.ItemModels : null;

            if (library == null || item == null)
                return;

            if (library.TryGetEntry(item.Id, out var entry))
                box.ApplyBodyModel(entry.model, entry.material, library.ModelScale);
        }

        // Stacks sit to the left and right of centre, with a clear gap in the middle so the play area /
        // glass stays unobstructed (instead of a single row across the centre).
        const float CenterGap = 0.6f;

        // Each box owns a fixed slot for its lifetime, so a box never moves when another item is used or
        // removed. Slots fill outward from the centre, alternating right then left:
        //   slot 0 -> right ring 1, slot 1 -> left ring 1, slot 2 -> right ring 2, slot 3 -> left ring 2 ...
        void Layout()
        {
            for (var i = 0; i < boxes.Count; i++)
                boxes[i].SetRestPosition(SlotPosition(boxes[i].Slot));
        }

        static Vector3 SlotPosition(int slot)
        {
            var ring = slot / 2 + 1;                 // 1, 1, 2, 2, 3, 3, ...
            var sign = (slot % 2 == 0) ? 1f : -1f;   // even -> right, odd -> left
            return new Vector3(sign * (CenterGap * 0.5f + ring * BoxSpacing), 0f, 0f);
        }

        // The lowest slot index not currently held by a box and not covered by the book. Reusing a freed
        // slot keeps new items as close to centre as possible, while existing boxes stay put; skipping the
        // book's slots means the leftward column flows around it instead of stacking an item underneath.
        int NextFreeSlot()
        {
            for (var slot = 0; ; slot++)
            {
                if (SlotBlockedByBook(slot))
                    continue;

                var taken = false;

                for (var i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Slot == slot)
                    {
                        taken = true;
                        break;
                    }
                }

                if (!taken)
                    return slot;
            }
        }

        // The book rests on one side of the desk; resolve the slice of the tray's local X axis its
        // footprint covers (lazy + cached) so NextFreeSlot can skip those slots. Needs the tray positioned
        // and the book seated first — both happen at scene load, so this resolves on the first Rebuild.
        void ResolveBookBand()
        {
            if (bookBandResolved)
                return;

            if (bookShop == null)
                bookShop = FindAnyObjectByType<BookShopView>();

            // Retry next Rebuild until the book exists and the tray has taken its place on the desk
            // (the book position is only meaningful in the tray's final local space). Only latch once
            // the band is resolved, so a book seated a frame later is still picked up.
            if (bookShop == null || !positioned)
                return;

            if (bookShop.TryGetDeskRestPosition(out var bookWorld))
            {
                bookLocalX = transform.InverseTransformPoint(bookWorld).x;
                hasBookBand = true;
                bookBandResolved = true;
            }
        }

        bool SlotBlockedByBook(int slot)
            => hasBookBand && Mathf.Abs(SlotPosition(slot).x - bookLocalX) < bookClearance;

        public void OnBoxClicked(DeskItemBox box)
        {
            // Ignore a box that's still being carried in — it isn't on its slot yet.
            if (box == null || box.IsBeingDelivered)
                return;

            // The Dealer's turn is theirs to act: the player can't raise/select an item during it
            // (the shared desk USE button is hidden then too). Mirrors coins, which only lift on the
            // player's turn. Leaving the player's turn already drops any held box in OnStateChanged.
            if (gameManager != null && gameManager.CurrentState == GameState.EnemyTurn)
                return;

            // Click a raised box again to set it back down.
            if (selected == box)
            {
                Deselect();
                return;
            }

            Deselect();
            selected = box;
            box.SetSelected(true);

            // Picking an item drops any coins the player had lifted (the two selections are mutually
            // exclusive). This clears coins without pouring; HasSelectedCoins is then false, so the
            // SelectionChanged it raises won't bounce back and deselect the box we just picked.
            playerController?.ClearCoinSelection();
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

        /// <summary>
        /// Commits the player's whole selection in one press (the shared desk USE button): apply the
        /// selected item, then pour the selected coins. Both are gated to the player's turn inside the
        /// manager, so an off-turn press just shows the unavailable cue.
        /// </summary>
        public void CommitSelection()
        {
            if (selected != null && gameManager != null)
            {
                if (gameManager.TryUseItem(selected.Item))
                    Deselect();              // stack may have emptied; Deselect is null-safe
                else
                    selected.FlashUnavailable();
            }

            playerController?.CommitSelection();
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

        // Build the single shared USE control once, parked in the tray's clear centre gap; visible only
        // on the player's turn.
        void EnsureUseButton()
        {
            if (useButton != null)
                return;

            useButton = DeskItemTrayBuilder.BuildUseButton(transform);
            useButton.gameObject.SetActive(gameManager != null && gameManager.CurrentState == GameState.PlayerTurn);
        }

        // The hand lives on the tray so it shares the tray's local space (its slot coordinates). Use an
        // assigned one if a designer placed/tuned it; otherwise add one on demand (play mode only, so
        // edit-mode tests don't pick up a stray component).
        DeskHandDelivery EnsureHandDelivery()
        {
            if (handDelivery == null)
                handDelivery = GetComponent<DeskHandDelivery>();

            if (handDelivery == null && Application.isPlaying)
                handDelivery = gameObject.AddComponent<DeskHandDelivery>();

            return handDelivery;
        }

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

            if (playerController == null)
                playerController = FindAnyObjectByType<PlayerController>();
        }
    }
}
