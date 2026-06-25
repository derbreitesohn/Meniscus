using System;
using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// The diegetic shop: a menu/ledger that, when the round ends, lifts up and opens like a book
    /// held in front of the camera. A procedural prop (back cover + hinged front cover) plays the
    /// open/close animation while it rises into a held pose, and a world-space canvas floating above
    /// it shows the catalog as clickable orders. Everything is built at runtime so a hand-modelled
    /// book can later replace the prop without touching the shop logic. The held-placement fields are
    /// the bits most likely to want tuning against the real scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class BookShopView : MonoBehaviour
    {
        [SerializeField] Camera worldCamera;

        [Tooltip("Uniform size of the whole held book — pages, printed menu and item pictures. 1 = the " +
                 "original size; raise it to hold a bigger book closer to a billboard in front of the camera.")]
        [SerializeField, Min(0.1f)] float bookScale = 1.3f;

        [Header("Held Placement (relative to the camera)")]
        [Tooltip("Metres in front of the camera the open book is held.")]
        [SerializeField] float holdDistance = 0.62f;
        [Tooltip("Vertical offset of the held book from screen centre (negative = below centre).")]
        [SerializeField] float holdVerticalOffset = 0f;
        [Tooltip("Horizontal offset of the held book from screen centre.")]
        [SerializeField] float holdHorizontalOffset = 0f;
        [Tooltip("Local rotation of the held book relative to the camera. -90 stands the open pages " +
                 "square to the camera; nudge off -90 to lean the book back or forward.")]
        [SerializeField] Vector3 heldEulerOffset = new(-90f, 0f, 0f);
        [Tooltip("How far below the held pose the book starts before it lifts into view.")]
        [SerializeField] float stowDrop = 0.28f;
        [Tooltip("How much further from the camera the book starts before it lifts in.")]
        [SerializeField] float stowBack = 0.12f;
        [Tooltip("How far the printed menu sits proud of the open page (small, so it reads as ink on the page).")]
        [SerializeField] float menuFloatHeight = 0.012f;

        [Header("Audio")]
        [SerializeField] AK.Wwise.Event pageFlip;
        [SerializeField] AK.Wwise.Event pageClose;
         [SerializeField] AK.Wwise.Event buySound;
     [SerializeField] AK.Wwise.Event uiClick;



        [Header("Desk Placement")]
        [Tooltip("Where the closed book rests on the desk. Place this component's GameObject on the desk " +
                 "(or assign a separate empty here) and the book sits at that transform, lifting toward " +
                 "the camera when opened. Orient it so +Z is the top of the page (away from the player) " +
                 "and +Y is up; keep its scale at 1.")]
        [SerializeField] Transform deskAnchor;

        [Tooltip("Pre-authored physical book prop (built by Tools > Meniscus > Author Book Shop). When " +
                 "set, the runtime uses it and only builds the menu canvas on top; left empty, the prop is " +
                 "built at runtime.")]
        [SerializeField] Transform authoredBook;

        [Header("Desk Rest Fallback")]
        [Tooltip("Used only when no anchor is placed: the desk/table the book is auto-rested on.")]
        [SerializeField] string deskObjectName = "Saloon Table";
        [Tooltip("Clearance above the desk surface so the resting book does not sink into it.")]
        [SerializeField] float deskRestClearance = 0.02f;
        [Tooltip("How far toward the player (from the desk centre) the book rests.")]
        [SerializeField] float deskRestForwardOffset = 0.2f;
        [Tooltip("How far to the player's right the book rests, to keep it clear of the play area.")]
        [SerializeField] float deskRestLateralOffset = 0.14f;

        [Header("Debug")]
        [Tooltip("Log lifecycle, desk resolution and click events to the console for diagnosis.")]
        [SerializeField] bool logDiagnostics = true;

        [Header("Book Prop")]
        [Tooltip("Width of a single page. The open book is a spread two pages wide.")]
        [SerializeField] float pageWidth = 0.30f;
        [SerializeField] float pageDepth = 0.38f;
        [SerializeField] float coverThickness = 0.02f;
        // 180° lays the hinged front cover flat (tucked beneath the back cover on the left) so the open book
        // is just the back cover + two pages. A smaller angle leaves the cover standing up at the spine.
        [SerializeField] float openAngle = 180f;
        [SerializeField, Min(0.05f)] float openSeconds = 0.55f;

        [Header("Menu")]
        [Tooltip("World width of the printed menu, spread across both pages. Keep it at or inside the full " +
                 "two-page width so the ink stays on the paper.")]
        [SerializeField] float menuWorldWidth = 0.56f;
        [Tooltip("World depth bound for the printed menu so it stays within the page depth.")]
        [SerializeField] float menuWorldDepth = 0.34f;
        [Tooltip("Seconds for a page-turn animation.")]
        [SerializeField, Min(0.05f)] float pageTurnSeconds = 0.35f;

        [Tooltip("Max distance for the click raycast that opens/closes the book during a round.")]
        [SerializeField] float clickRaycastDistance = 100f;

        static readonly Color CoverColor = new(0.34f, 0.16f, 0.08f);
        static readonly Color PageColor = new(0.86f, 0.78f, 0.6f);

        // The menu is printed onto the open page: a warm paper fill with dark ink, not a floating UI panel.
        static readonly Color PaperColor = new(0.91f, 0.84f, 0.66f, 1f);
        static readonly Color InkColor = new(0.20f, 0.10f, 0.04f);
        static readonly Color InkSoftColor = new(0.34f, 0.20f, 0.10f);
        static readonly Color RuleColor = new(0.34f, 0.20f, 0.10f, 0.35f);
        static readonly Color RowTransparent = new(0f, 0f, 0f, 0f);
        static readonly Color RowHover = new(0.30f, 0.17f, 0.08f, 0.30f);
        static readonly Color RowPressed = new(0.30f, 0.17f, 0.08f, 0.24f);
        static readonly Color RowSelected = new(0.30f, 0.17f, 0.08f, 0.55f);
        static readonly Color StampColor = new(0.30f, 0.15f, 0.07f, 0.92f);
        static readonly Color StampHover = new(0.42f, 0.23f, 0.10f, 0.95f);
        static readonly Color StampPressed = new(0.20f, 0.10f, 0.04f, 1f);

        Bounds deskBounds;
        bool deskResolved;
        bool hasDeskBounds;

        Transform root;
        Transform hingePivot;
        Transform turningPivot;
        Transform menuCanvasTransform;
        CanvasGroup menuGroup;
        GameObject finishButtonObject;
        Text finishLabel;
        Text browseHint;
        ShopManager shopManager;
        float menuScaleBase = 1f;
        // Vertical squash applied to the menu's pixel layout so the (otherwise portrait) content matches the
        // landscape page aspect and the canvas fills the full spread width. 1 = the original 1280px design.
        float menuVy = 1f;
        bool built;
        bool isOpen;

        // Menu presentation: a fixed-size canvas with a left "order ticket" page and a right paginated
        // item list. The selection model is pure view-model state (catalog, current page, selection).
        readonly MenuSelectionModel model = new();

        // Right-page list container (rows are rebuilt into this per page) and the per-page row lookup
        // used to repaint selection/hover backgrounds and toggle the ▸ selection pointer.
        Transform rightListContainer;
        readonly Dictionary<ItemDefinition, Image> rowBackgrounds = new();
        readonly Dictionary<ItemDefinition, GameObject> rowPointers = new();
        readonly Dictionary<ItemDefinition, Text> rowBadges = new();

        // Left-page ticket references, refreshed on select/buy.
        Image ticketIcon;
        Text ticketName;
        Text ticketDesc;
        Text ticketCostOwn;
        Button buyButton;
        Text buyLabel;

        // Right-page paging chrome.
        Button prevArrow;
        Button nextArrow;
        Text prevArrowLabel;
        Text nextArrowLabel;
        Text pageIndicator;

        // Geometry needed to rebuild the right page each turn (set in BuildMenuCanvas).
        float rowHeightPx;
        float rowWidthPx;

        // True while the book was opened by clicking it mid-round (rather than via the between-rounds
        // shop phase). The catalog is fully buyable either way — mid-round purchases spend banked cash —
        // but closing a mid-round open just sets the book back on the desk and lets the round continue,
        // whereas closing the shop-phase book finishes the shop phase.
        bool previewMode;

        // Supplied by ShopManager: returns true while the player may open the book to browse
        // (during a round) and false otherwise (shop phase / game over). Null means "allowed".
        Func<bool> canBrowse;
        float animT;

        // Page-flip animation state. A flip sweeps the turning sheet across the spine and, at the
        // halfway point, swaps the right-page content to the next/prev page exactly once.
        bool isTurning;
        float turnT;
        float prevTurnT;
        int pendingDir;

        void Awake()
        {
            ResolveReferences();
        }

        public void Open(IReadOnlyList<ItemDefinition> catalog, ShopManager shop)
{
    shopManager = shop;
    ResolveReferences();

    if (!built)
        Build(catalog);

    var wasOpen = isOpen;          // merken, ob schon offen

    root.gameObject.SetActive(true);
    isOpen = true;
    previewMode = false;

    if (!wasOpen)                  // nur beim echten Übergang zu→auf
        pageFlip?.Post(gameObject);

    Log($"Open (shop phase, buyable). catalogCount={catalog?.Count ?? 0}");
    ApplyHeldPose(Mathf.SmoothStep(0f, 1f, animT));
}   
        /// <summary>
        /// Builds the book (if needed) and seats it closed on the desk without opening it, so it is a
        /// visible prop on the table during the rounds and is then lifted from there when the shop opens.
        /// </summary>
        public void PrepareOnDesk(IReadOnlyList<ItemDefinition> catalog, ShopManager shop)
        {
            shopManager = shop;
            ResolveReferences();

            if (!built)
                Build(catalog);

            root.gameObject.SetActive(true);
            isOpen = false;
            previewMode = false;
            animT = 0f;

            ApplyHeldPose(0f);

            var anchorName = deskAnchor != null
                ? deskAnchor.name
                : (transform.position.sqrMagnitude > 0.0001f ? $"{name} (self)" : "none → desk-bounds fallback");
            Log($"PrepareOnDesk: book built and seated. anchor={anchorName}, restPos={root.position}, restRot={root.rotation.eulerAngles}");
        }

        public void Close()
{
    Log("Close");

    if (isOpen)                    
        pageClose?.Post(gameObject);

    isOpen = false;
    previewMode = false;
    ResetPageTurn();
}

        /// <summary>
        /// Sets the predicate deciding whether a book-prop click may open a browse preview.
        /// </summary>
        public void SetBrowseGate(Func<bool> gate) => canBrowse = gate;

        void ResetPageTurn()
        {
            isTurning = false;
            turnT = 0f;
            prevTurnT = 0f;

            if (turningPivot != null)
                turningPivot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Pure transition for a book-prop click. Closed → opens as a read-only preview;
        /// previewing → closes; shop-phase-open (open, not preview) → unchanged (the player
        /// closes that via "Finish Drink"). No side effects, so it is unit-testable.
        /// </summary>
        public static (bool isOpen, bool previewMode) ComputeBrowseToggle(bool isOpen, bool previewMode)
        {
            if (!isOpen)
                return (true, true);

            if (previewMode)
                return (false, false);

            return (isOpen, previewMode);
        }

        /// <summary>
        /// Whether a fully-revealed menu accepts purchases for the given open mode. Buying is allowed in
        /// BOTH the between-rounds shop phase (<paramref name="previewMode"/> false) and a mid-round open
        /// (true) — mid-round shopping spends banked cash. Pure, so it is the unit-tested regression guard
        /// that opening the book mid-round stays buyable rather than read-only.
        /// </summary>
        public static bool PurchasingAllowedInMode(bool previewMode) => true;

        /// <summary>
        /// Applies a book-prop click. Closed on the desk, a click lifts it open as a read-only
        /// preview; while previewing, a click closes it again. Ignored once the shop phase has
        /// opened it for real, where closing is done via "Finish Drink".
        /// </summary>
   void OnBookClicked()
{
    Log($"OnBookClicked received. built={built}, isOpen={isOpen}, previewMode={previewMode}");

    if (!built)
        return;

    var wasOpen = isOpen;
    (isOpen, previewMode) = ComputeBrowseToggle(isOpen, previewMode);

    if (!wasOpen && isOpen)
        pageFlip?.Post(gameObject);
    else if (wasOpen && !isOpen)
        pageClose?.Post(gameObject);

    Log($"-> isOpen={isOpen}, previewMode={previewMode}");
}

        void Update()
        {
            if (!built)
                return;

            PollBrowseClick();
            UpdatePageTurn();

            var target = isOpen ? 1f : 0f;
            animT = Mathf.MoveTowards(animT, target, Time.deltaTime / Mathf.Max(0.05f, openSeconds));

            var openProgress = Mathf.SmoothStep(0f, 1f, animT);
            // Right leaf folds shut (openAngle) when closed and unfolds flat (0°) when open, so the book
            // genuinely folds open about its spine instead of a cover flipping on top.
            hingePivot.localRotation = Quaternion.Euler(0f, 0f, (1f - openProgress) * openAngle);

            // Lift the book from its stowed pose up into the held pose as the cover opens.
            ApplyHeldPose(openProgress);

            var reveal = Mathf.Clamp01((animT - 0.5f) / 0.5f);

            // The catalog is buyable once it has finished revealing, in BOTH the between-rounds shop
            // phase and a mid-round open: opening the book mid-round now lets the player spend banked
            // cash on the fly rather than only browse.
            var canPurchase = reveal > 0.95f && PurchasingAllowedInMode(previewMode);

            if (menuGroup != null)
            {
                menuGroup.alpha = reveal;
                menuGroup.interactable = canPurchase;
                menuGroup.blocksRaycasts = canPurchase;
            }

            // The close action doubles as "Finish Drink" between rounds (it ends the shop phase) and
            // "Set It Down" mid-round (it just lowers the book back to the desk and the round carries on).
            if (finishLabel != null)
                finishLabel.text = previewMode ? "Set It Down" : "Finish Drink";

            // The old "just looking" hint is retired now that mid-round browsing can buy.
            if (browseHint != null)
                browseHint.enabled = false;

            if (menuCanvasTransform != null)
            {
                // Hold a constant scale so the catalog reads as printing fading up on the page, not a UI panel
                // that springs open.
                menuCanvasTransform.localScale = Vector3.one * menuScaleBase;

                if (reveal > 0f)
                {
                    var cam = ActiveCamera();

                    // A world-space canvas reads correctly when its forward matches the camera's, so
                    // it is viewed along its own normal rather than mirrored.
                    if (cam != null)
                        menuCanvasTransform.rotation = cam.transform.rotation;
                }
            }

            // When closed the book is left resting on the desk (not deactivated) so it stays a physical
            // object the player can see it lift from next time, rather than respawning each shop phase.
        }

        /// <summary>
        /// Detects a click on the book prop using the Input System (the project's input backend
        /// does not deliver legacy OnMouse* messages) and toggles the read-only browse preview,
        /// provided the gate currently allows browsing.
        /// </summary>
        void PollBrowseClick()
        {
            var mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            // A click on the menu's own controls (Buy / page arrows / Set It Down) must not also fall
            // through the physics raycast to the book collider and toggle it closed, now that the
            // mid-round menu is interactive. Mirrors how DeskItemTray guards clicks behind the book.
            var events = EventSystem.current;
            if (events != null && events.IsPointerOverGameObject())
                return;

            if (canBrowse != null && !canBrowse())
                return;

            var cam = ActiveCamera();

            if (cam == null)
                return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());

            if (!Physics.Raycast(ray, out var hit, clickRaycastDistance))
                return;

            // Only our own book counts: the marker sits on the click collider's root.
            var marker = hit.collider.GetComponentInParent<BookClickTarget>();

            if (marker == null || marker.transform != root)
                return;

            OnBookClicked();
        }

        void Build(IReadOnlyList<ItemDefinition> catalog)
        {
            if (authoredBook != null)
                ResolveAuthoredProp();
            else
                BuildProp();

            // Scale the whole held book (prop + printed menu + pictures) up uniformly so the pages and
            // their images read larger. Done on the root so every child — including the world-space menu
            // canvas — grows together and the menu stays within the page.
            root.localScale = Vector3.one * Mathf.Max(0.1f, bookScale);

            // A page-sized sheet hung on the spine, swept across the spread during a flip. Prefer an
            // authored sheet under the prop (built by Tools > Meniscus > Author Book Shop, so it too can
            // carry a model/material); otherwise build it over the prop at runtime. Either way it pivots
            // on the spine (X = 0) and starts inactive until a turn begins.
            turningPivot = root.Find("Book Turning Pivot");

            if (turningPivot == null)
                turningPivot = BookShopBuilder.BuildTurningSheet(root, new BookShopBuilder.BookPropParams
                {
                    pageWidth = pageWidth,
                    pageDepth = pageDepth,
                    coverThickness = coverThickness,
                    coverColor = CoverColor,
                    pageColor = PageColor,
                });

            turningPivot.gameObject.SetActive(false);

            BuildMenuCanvas(catalog);

            built = true;
            root.gameObject.SetActive(false);
        }

        void BuildProp()
        {
            root = BookShopBuilder.BuildProp(transform, new BookShopBuilder.BookPropParams
            {
                pageWidth = pageWidth,
                pageDepth = pageDepth,
                coverThickness = coverThickness,
                coverColor = CoverColor,
                pageColor = PageColor,
                closedHingeAngle = openAngle,
            });

            hingePivot = root.Find("Book Hinge");
        }

        void ResolveAuthoredProp()
        {
            root = authoredBook;
            hingePivot = root.Find("Book Hinge");

            // The raycast identifies the book by this marker, so guarantee an authored prop has one.
            if (root.GetComponent<BookClickTarget>() == null)
                root.gameObject.AddComponent<BookClickTarget>();
        }

        void BuildMenuCanvas(IReadOnlyList<ItemDefinition> catalog)
        {
            // The canvas is a FIXED-size spread now (not grown by item count): a left "order ticket"
            // page and a right paginated item list. Sizes are in canvas pixels; the whole canvas is
            // scaled to world via menuScaleBase so it fits within both the page width and depth.
            const float pixelWidth = 1040f;

            // Match the canvas aspect to the page rectangle so ComputeScale fills the full spread width
            // instead of being letter-boxed by the page depth (which left the menu a narrow centre strip).
            // The content was designed against a 1280px-tall canvas, so menuVy squashes those vertical
            // metrics into the shorter landscape canvas while the higher fill scale keeps their world size.
            var pixelHeight = Mathf.Max(1f, pixelWidth * menuWorldDepth / menuWorldWidth);
            menuVy = pixelHeight / 1280f;
            var pixelSize = new Vector2(pixelWidth, pixelHeight);

            var canvas = RuntimeUiFactory.CreateWorldCanvas(root, "Book Menu Canvas", pixelSize, ActiveCamera());
            menuCanvasTransform = canvas.transform;

            // Centred over the spine, just proud of the page (which sits on its cover board, so the page top
            // is at coverThickness*1.5) so the menu reads as printing on the spread.
            menuCanvasTransform.localPosition = new Vector3(0f, coverThickness * 1.5f + menuFloatHeight, 0f);

            menuScaleBase = MenuLayout.ComputeScale(menuWorldWidth, menuWorldDepth, pixelWidth, pixelHeight);
            menuCanvasTransform.localScale = Vector3.one * menuScaleBase;

            menuGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            menuGroup.alpha = 0f;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;

            var leftCenter = -pixelWidth * 0.25f;
            var rightCenter = pixelWidth * 0.25f;
            var pageWidthPx = pixelWidth * 0.47f;
            var pageTextWidth = pageWidthPx - 70f;

            // Two parchment pages with a darkened spine gutter between them.
            Decor(RuntimeUiFactory.CreateImage(canvas.transform, "Left Page Paper", new Vector2(pageWidthPx, pixelHeight), new Vector2(leftCenter, 0f), PaperColor));
            Decor(RuntimeUiFactory.CreateImage(canvas.transform, "Right Page Paper", new Vector2(pageWidthPx, pixelHeight), new Vector2(rightCenter, 0f), PaperColor));

            // A soft, layered spine shadow (wide+faint under narrow+dark) reads as a real gutter where the
            // two leaves meet, instead of a single hard line.
            Decor(RuntimeUiFactory.CreateImage(canvas.transform, "Spine Soft", new Vector2(36f, pixelHeight), Vector2.zero, new Color(0.20f, 0.12f, 0.06f, 0.07f)));
            Decor(RuntimeUiFactory.CreateImage(canvas.transform, "Spine Mid", new Vector2(18f, pixelHeight), Vector2.zero, new Color(0.20f, 0.12f, 0.06f, 0.16f)));
            Decor(RuntimeUiFactory.CreateImage(canvas.transform, "Spine Gutter", new Vector2(8f, pixelHeight), Vector2.zero, RuleColor));

            // A ruled border inset on each leaf, like a printed ledger page.
            BuildPageFrame(canvas.transform, leftCenter, pageWidthPx, pixelHeight);
            BuildPageFrame(canvas.transform, rightCenter, pageWidthPx, pixelHeight);

            var top = pixelHeight * 0.5f;

            BuildLeftTicket(canvas.transform, leftCenter, pageTextWidth, top);
            BuildRightList(canvas, catalog, rightCenter, pageTextWidth, top);

            RebuildRightPage();
            RefreshTicket();
        }

        /// <summary>
        /// Left page: the title plus a fixed "order ticket" showing the selected item's detail and the
        /// Buy / Finish Drink actions. The ticket fields are kept so <see cref="RefreshTicket"/> can
        /// repaint them as the selection changes.
        /// </summary>
        void BuildLeftTicket(Transform canvas, float leftCenter, float pageTextWidth, float top)
        {
            // Vertical offsets, heights and fonts are squashed by menuVy so the portrait design fits the
            // landscape page; the higher fill scale restores their on-page world size. Horizontal stays.
            var v = menuVy;
            int F(float design) => Mathf.Max(1, Mathf.RoundToInt(design * v));

            // Title.
            InkShadow(RuntimeUiFactory.CreateText(
                canvas, "Menu Title", "Saloon Menu",
                new Vector2(leftCenter, top - 110f * v), new Vector2(pageTextWidth, 110f * v), F(64), InkColor,
                TextAnchor.MiddleCenter, bold: true), 3f);

            Decor(RuntimeUiFactory.CreateImage(
                canvas, "Title Rule", new Vector2(pageTextWidth, 3f * v),
                new Vector2(leftCenter, top - 180f * v), RuleColor));

            // Selected item picture (baked thumbnail), upper-right of the order page beside the name.
            // Hidden until an item with a baked icon is selected (see RefreshTicket).
            ticketIcon = RuntimeUiFactory.CreateImage(
                canvas, "Ticket Thumb", new Vector2(180f * v, 180f * v),
                new Vector2(leftCenter + pageTextWidth * 0.5f - 100f * v, top - 300f * v), Color.white)
                .GetComponent<Image>();
            ticketIcon.preserveAspect = true;
            ticketIcon.enabled = false;

            // Selected item name.
            ticketName = RuntimeUiFactory.CreateText(
                canvas, "Ticket Name", "",
                new Vector2(leftCenter, top - 270f * v), new Vector2(pageTextWidth, 100f * v), F(48), InkColor,
                TextAnchor.UpperLeft, bold: true);
            InkShadow(ticketName, 2.5f);

            // Description body.
            ticketDesc = RuntimeUiFactory.CreateText(
                canvas, "Ticket Desc", "",
                new Vector2(leftCenter, 40f * v), new Vector2(pageTextWidth, 360f * v), F(32), InkSoftColor,
                TextAnchor.UpperLeft);

            // Cost / owned line.
            ticketCostOwn = RuntimeUiFactory.CreateText(
                canvas, "Ticket Cost/Own", "",
                new Vector2(leftCenter, -top + 320f * v), new Vector2(pageTextWidth, 60f * v), F(34), InkColor,
                TextAnchor.MiddleLeft, bold: true);

            // Buy then Finish Drink, stacked near the bottom of the page.
            buyButton = RuntimeUiFactory.CreateButton(
                canvas, "Buy Button", "", new Vector2(pageTextWidth * 0.9f, 96f * v),
                new Vector2(leftCenter, -top + 210f * v), F(38), OnBuyClicked, boldLabel: true,
                normalColor: StampColor,
                highlightedColor: StampHover,
                pressedColor: StampPressed,
                labelColor: PaperColor);
            buyLabel = buyButton.transform.Find("Label").GetComponent<Text>();
            InkShadow(buyLabel, 2f);

            var finishButton = RuntimeUiFactory.CreateButton(
                canvas, "Finish Drink Button", "Finish Drink",
                new Vector2(pageTextWidth * 0.9f, 96f * v),
                new Vector2(leftCenter, -top + 90f * v), F(36), OnFinish, boldLabel: true,
                normalColor: StampColor,
                highlightedColor: StampHover,
                pressedColor: StampPressed,
                labelColor: PaperColor);
            finishButtonObject = finishButton.gameObject;
            finishLabel = finishButton.transform.Find("Label").GetComponent<Text>();
            InkShadow(finishLabel, 2f);

            // Shown only while previewing mid-round (purchasing disabled); hidden during the shop phase.
            browseHint = RuntimeUiFactory.CreateText(
                canvas, "Browse Hint", "— Just looking · click the book to close —",
                new Vector2(leftCenter, -top + 90f * v), new Vector2(pageTextWidth, 60f * v), F(26), InkSoftColor);
            browseHint.enabled = false;
        }

        /// <summary>
        /// Right page chrome: the list container, the corner page arrows and the page indicator. The
        /// rows themselves are (re)built per page by <see cref="RebuildRightPage"/>. Also sizes the
        /// list area and derives itemsPerPage so the model can split the catalog into pages.
        /// </summary>
        void BuildRightList(Canvas canvas, IReadOnlyList<ItemDefinition> catalog, float rightCenter, float pageTextWidth, float top)
        {
            var v = menuVy;
            int F(float design) => Mathf.Max(1, Mathf.RoundToInt(design * v));

            // Running head mirroring the left page's "Saloon Menu" title + rule, so the open spread reads
            // as a real two-page book rather than a list bolted onto a blank leaf.
            InkShadow(RuntimeUiFactory.CreateText(
                canvas.transform, "Right Page Title", "Goods",
                new Vector2(rightCenter, top - 110f * v), new Vector2(pageTextWidth, 110f * v), F(64), InkColor,
                TextAnchor.MiddleCenter, bold: true), 3f);

            Decor(RuntimeUiFactory.CreateImage(
                canvas.transform, "Right Title Rule", new Vector2(pageTextWidth, 3f * v),
                new Vector2(rightCenter, top - 180f * v), RuleColor));

            var headerPad = 200f * v;   // top: the running head + rule
            var footerPad = 165f * v;   // bottom: the page navigator (arrows + indicator)
            var rowHeight = 168f * v;   // taller rows so the picture + description beneath read large

            var listDepthPx = (top * 2f) - headerPad - footerPad;
            var itemsPerPage = MenuLayout.ItemsPerPage(listDepthPx, rowHeight);

            // Stash the geometry RebuildRightPage needs (rows are positioned within the list container).
            rowHeightPx = rowHeight;
            rowWidthPx = pageTextWidth;

            // The rows live under a dedicated container so a page rebuild only clears the list.
            var containerObject = RuntimeUiFactory.CreateImage(
                canvas.transform, "Right List", new Vector2(pageTextWidth, listDepthPx),
                new Vector2(rightCenter, top - headerPad - listDepthPx * 0.5f), RowTransparent);
            rightListContainer = containerObject.transform;

            // Page navigator pinned to the foot of the page: two solid ink-stamp arrows flanking the page
            // count, in the SAME button language as Buy / Finish Drink so paging reads unmistakably as a
            // control. (The old version was a bare ‹ / › glyph on a transparent background tucked in the top
            // corner — almost invisible, and it didn't even change when a direction was unavailable.) The
            // whole navigator is hidden on a single-page catalog; an unavailable direction is faded but kept
            // visible (see SetArrowState) so the player still sees that pages exist.
            var pagerY = -top + 100f * v;
            var arrowSize = new Vector2(74f, 58f);

            prevArrow = BuildPagerArrow("Prev Arrow", "‹", new Vector2(rightCenter - 150f, pagerY), arrowSize, F(60), -1);
            prevArrowLabel = prevArrow.transform.Find("Label").GetComponent<Text>();

            nextArrow = BuildPagerArrow("Next Arrow", "›", new Vector2(rightCenter + 150f, pagerY), arrowSize, F(60), 1);
            nextArrowLabel = nextArrow.transform.Find("Label").GetComponent<Text>();

            pageIndicator = RuntimeUiFactory.CreateText(
                canvas.transform, "Page Indicator", "",
                new Vector2(rightCenter, pagerY), new Vector2(pageTextWidth - 220f, 60f * v), F(30), InkSoftColor);
            InkShadow(pageIndicator, 1.5f);

            model.SetCatalog(catalog, itemsPerPage);
        }

        /// <summary>
        /// One foot-of-page navigator arrow: a solid ink-stamp button (matching Buy / Finish) with a faded
        /// disabled tint so the unavailable direction still reads as a real, if greyed, control.
        /// </summary>
        Button BuildPagerArrow(string name, string glyph, Vector2 position, Vector2 size, int glyphFont, int dir)
        {
            var button = RuntimeUiFactory.CreateButton(
                menuCanvasTransform, name, glyph, size, position, glyphFont, () => OnTurn(dir),
                boldLabel: true,
                normalColor: StampColor,
                highlightedColor: StampHover,
                pressedColor: StampPressed,
                labelColor: PaperColor);

            var colors = button.colors;
            colors.disabledColor = new Color(0.30f, 0.15f, 0.07f, 0.30f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            InkShadow(button.transform.Find("Label").GetComponent<Text>(), 2f);
            return button;
        }

        /// <summary>
        /// Rebuilds the right-page rows for the current page: one selectable row per item with its price
        /// and an owned badge, plus the page indicator and arrow interactability. Selecting a row does
        /// NOT purchase — it calls <see cref="OnSelect"/>.
        /// </summary>
        void RebuildRightPage()
        {
            if (rightListContainer == null)
                return;

            for (var i = rightListContainer.childCount - 1; i >= 0; i--)
                Destroy(rightListContainer.GetChild(i).gameObject);

            rowBackgrounds.Clear();
            rowPointers.Clear();
            rowBadges.Clear();

            var v = menuVy;
            int F(float design) => Mathf.Max(1, Mathf.RoundToInt(design * v));

            var pageItems = model.CurrentPageItems();
            var rowSize = new Vector2(rowWidthPx, rowHeightPx - 16f * v);

            // Rows are positioned relative to the list container's centre.
            var halfList = ((RectTransform)rightListContainer).sizeDelta.y * 0.5f;
            var rowY = halfList - rowHeightPx * 0.5f;

            foreach (var item in pageItems)
            {
                if (item == null)
                    continue;

                var captured = item;

                // A dedicated background Image holds the persistent selection bar: the row Button uses
                // its own (transparent) graphic for hover/press tinting, so the selected colour set in
                // ApplySelectionVisual is not clobbered by the Button's colour transition.
                var background = RuntimeUiFactory.CreateImage(
                    rightListContainer, $"{item.Id} Row BG", rowSize, new Vector2(0f, rowY), RowTransparent);
                rowBackgrounds[item] = background.GetComponent<Image>();

                // The whole row is one transparent button with an empty label: the picture, name and
                // description below are drawn as its children, and clicks on those bubble up to the button.
                var row = RuntimeUiFactory.CreateButton(
                    background.transform, $"{item.Id} Order", "", rowSize,
                    Vector2.zero, F(38), () => OnSelect(captured),
                    normalColor: RowTransparent,
                    highlightedColor: RowHover,
                    pressedColor: RowPressed,
                    labelColor: InkColor);

                // Left: the baked "screenshot" of the item's model (hidden until a thumbnail is baked).
                var thumbSize = rowSize.y * 0.92f;
                var thumb = RuntimeUiFactory.CreateImage(
                    row.transform, "Thumb", new Vector2(thumbSize, thumbSize),
                    new Vector2(-rowWidthPx * 0.5f + 10f + thumbSize * 0.5f, 0f), Color.white);
                var thumbImage = thumb.GetComponent<Image>();
                thumbImage.raycastTarget = false;
                var icon = shopManager != null ? shopManager.IconFor(item) : null;

                if (icon != null)
                {
                    thumbImage.sprite = icon;
                    thumbImage.preserveAspect = true;
                }
                else
                {
                    thumb.SetActive(false);
                }

                // Text block: name (upper) and small description (beneath), between the picture and price.
                const float priceColumnWidth = 120f;
                var textLeft = -rowWidthPx * 0.5f + thumbSize + 26f;
                var textWidth = Mathf.Max(40f, rowWidthPx * 0.5f - priceColumnWidth - textLeft);
                var textCenterX = textLeft + textWidth * 0.5f;

                InkShadow(RuntimeUiFactory.CreateText(
                    row.transform, "Name", item.DisplayName,
                    new Vector2(textCenterX, rowSize.y * 0.22f), new Vector2(textWidth, rowSize.y * 0.5f),
                    F(38), InkColor, TextAnchor.MiddleLeft, bold: true), 2f).raycastTarget = false;

                RuntimeUiFactory.CreateText(
                    row.transform, "Desc", item.Description,
                    new Vector2(textCenterX, -rowSize.y * 0.22f), new Vector2(textWidth, rowSize.y * 0.52f),
                    F(30), InkSoftColor, TextAnchor.UpperLeft).raycastTarget = false;

                // Price (upper-right) and the owned badge ✓×N beneath it (shown only when owned > 0).
                InkShadow(RuntimeUiFactory.CreateText(
                    row.transform, "Price", $"${item.Cost}",
                    new Vector2(-24f, 18f * v), rowSize, F(34), InkSoftColor, TextAnchor.MiddleRight), 1.5f)
                    .raycastTarget = false;

                var badge = RuntimeUiFactory.CreateText(
                    row.transform, "Owned Badge", "",
                    new Vector2(-24f, -28f * v), rowSize, F(26), InkSoftColor, TextAnchor.MiddleRight);
                badge.raycastTarget = false;
                rowBadges[item] = badge;

                // Ruled line under each order.
                RuntimeUiFactory.CreateImage(
                    row.transform, "Rule", new Vector2(rowWidthPx, 2f),
                    new Vector2(0f, -rowHeightPx * 0.5f + 8f * v), RuleColor)
                    .GetComponent<Image>().raycastTarget = false;

                rowY -= rowHeightPx;
            }

            // Page indicator (1-based) hidden on a single-page catalog.
            if (pageIndicator != null)
            {
                var single = model.PageCount <= 1;
                pageIndicator.enabled = !single;

                if (!single)
                    pageIndicator.text = $"Page {model.CurrentPage + 1} / {model.PageCount}";
            }

            // Show the navigator only when there is more than one page; fade (don't hide) the direction
            // you can't currently take, so the player still sees that pages exist on either side.
            var multiPage = model.PageCount > 1;
            SetArrowState(prevArrow, prevArrowLabel, multiPage, model.CanTurnPrev);
            SetArrowState(nextArrow, nextArrowLabel, multiPage, model.CanTurnNext);

            RefreshOwnedBadges();
            ApplySelectionVisual();
        }
        void PlayClick() => uiClick?.Post(gameObject);

        /// <summary>Row click: focus the item for its detail and toggle it in the buy cart (multi-select).</summary>
        void OnSelect(ItemDefinition item)
        {
             PlayClick();
            model.ToggleCart(item);
            RefreshTicket();
            ApplySelectionVisual();
        }

        /// <summary>Buy button: purchase every item in the cart that is still affordable; the book is the
        /// only buyer. Bought items leave the cart; unaffordable ones (or once the desk fills) stay.</summary>
        void OnBuyClicked()
        {
            if (shopManager == null || model.CartCount == 0)
                return;

            // Snapshot the cart so we can mutate the model's cart while iterating.
            var pending = new List<ItemDefinition>(model.Cart);
            var boughtAny = false;

            foreach (var item in pending)
            {
                if (shopManager.TryBuyItem(item))
                {
                    model.RemoveFromCart(item);
                    boughtAny = true;
                }
            }

            if (boughtAny)
            {
                buySound?.Post(gameObject);
                RefreshOwnedBadges();
                ApplySelectionVisual();
                // Confirm flourish: pop the Buy stamp so a purchase lands with a beat instead of silently.
                buyButton.GetComponent<UiPressPunch>()?.Punch();
            }

            RefreshTicket();
        }

        /// <summary>
        /// Corner arrow: start an animated page flip. Input is ignored while a flip is already running,
        /// and a flip only starts if the model can turn that way. The actual page swap happens at the
        /// flip's midpoint (see <see cref="Update"/>); here we only kick off the sheet sweep.
        /// </summary>
        void OnTurn(int dir)
        {
            if (isTurning)
                return;

            PlayClick();

            var canTurn = dir < 0 ? model.CanTurnPrev : model.CanTurnNext;

            if (!canTurn)
                return;

            isTurning = true;
            turnT = 0f;
            prevTurnT = 0f;
            pendingDir = dir;

            if (turningPivot != null)
            {
                turningPivot.gameObject.SetActive(true);
                turningPivot.localRotation = TurnRotation(dir, 0f);
            }
        }

        /// <summary>
        /// Sheet rotation about the spine (Z) for a flip in <paramref name="dir"/> at eased progress
        /// <paramref name="eased"/> in [0,1]. A "next" turn sweeps the sheet from the right page (0°)
        /// across to the left (180°); a "prev" turn sweeps back from the left (180°) to the right (0°).
        /// </summary>
        static Quaternion TurnRotation(int dir, float eased)
        {
            var angle = dir < 0
                ? Mathf.Lerp(180f, 0f, eased)
                : Mathf.Lerp(0f, 180f, eased);

            return Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Drives the in-progress page flip: advances the timer, sweeps the turning sheet across the
        /// spine with the same eased curve as the cover open, swaps the right-page content exactly once
        /// as the flip passes its midpoint, and parks the sheet (inactive) when the flip completes.
        /// </summary>
        void UpdatePageTurn()
        {
            if (!isTurning)
                return;

            turnT += Time.deltaTime / Mathf.Max(0.05f, pageTurnSeconds);

            var clamped = Mathf.Clamp01(turnT);
            var eased = Mathf.SmoothStep(0f, 1f, clamped);

            if (turningPivot != null)
                turningPivot.localRotation = TurnRotation(pendingDir, eased);

            // Swap the underlying page once, as the sheet crosses the spread's midpoint, so the new
            // page is revealed behind the sheet for the second half of the sweep.
            if (MenuLayout.CrossedMidpoint(prevTurnT, turnT))
            {
                if (pendingDir < 0)
                    model.TurnPrev();
                else
                    model.TurnNext();

                RebuildRightPage();
            }

            prevTurnT = turnT;

            if (turnT >= 1f)
            {
                isTurning = false;

                if (turningPivot != null)
                    turningPivot.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Repaints the left ticket from the current selection: a hint when nothing is selected (Buy
        /// hidden), else the item detail with its cost/owned line and the Buy button state.
        /// </summary>
        void RefreshTicket()
        {
            if (ticketName == null)
                return;

            if (model.Selected == null)
            {
                ticketName.text = "Pick an order from the menu →";
                ticketDesc.text = "";
                ticketCostOwn.text = "";

                if (ticketIcon != null)
                    ticketIcon.enabled = false;

                if (buyButton != null)
                {
                    buyButton.gameObject.SetActive(false);
                    buyButton.interactable = false;
                }

                return;
            }

            var item = model.Selected;
            ticketName.text = item.DisplayName;
            ticketDesc.text = item.Description;

            if (ticketIcon != null)
            {
                var icon = shopManager != null ? shopManager.IconFor(item) : null;
                ticketIcon.sprite = icon;
                ticketIcon.enabled = icon != null;
            }

            var owned = shopManager != null ? shopManager.OwnedCount(item) : 0;
            var inCart = model.IsInCart(item);
            var cartNote = model.CartCount > 0 ? $"     ·  Cart {model.CartCount} (${model.CartTotalCost})" : "";
            ticketCostOwn.text = $"Cost: ${item.Cost}    Own: {owned}    {(inCart ? "[in cart]" : "[tap to add]")}{cartNote}";

            // The Buy button purchases the whole cart, not just the focused item.
            var spendableCash = shopManager != null ? shopManager.SpendableCash : 0;
            var deskFull = shopManager != null && shopManager.IsDeskFull;
            var state = model.EvaluateCart(spendableCash, deskFull);

            if (buyButton != null)
            {
                buyButton.gameObject.SetActive(true);
                buyButton.interactable = state.CanBuy;

                if (buyLabel != null)
                    buyLabel.text = state.Label;
            }
        }

        /// <summary>Updates each visible row's ✓×N badge from the owned count (blank when 0).</summary>
        void RefreshOwnedBadges()
        {
            foreach (var pair in rowBadges)
            {
                var owned = shopManager != null ? shopManager.OwnedCount(pair.Key) : 0;
                pair.Value.text = owned > 0 ? $"✓×{owned}" : "";
            }
        }

        /// <summary>
        /// Paints every row that is in the buy cart as a solid bar with its ▸ pointer shown, so the
        /// multi-selection reads strongly against the page.
        /// </summary>
        void ApplySelectionVisual()
        {
            foreach (var pair in rowBackgrounds)
            {
                var selected = model.IsInCart(pair.Key);
                pair.Value.color = selected ? RowSelected : RowTransparent;

                if (rowPointers.TryGetValue(pair.Key, out var pointer))
                    pointer.SetActive(selected);
            }
        }

        void OnFinish()
        {
            // Mid-round the book was opened to shop on the fly: closing just sets it back on the desk
            // and the round carries on (no game-state change). In the between-rounds shop phase, closing
            // finishes the phase and advances the game.
            PlayClick();
            if (previewMode)
            {
                Close();
                return;
            }

            if (shopManager != null)
                shopManager.FinishOrdering();
        }

        /// <summary>
        /// Poses the book between resting closed on the desk (<paramref name="raise"/> 0) and held open
        /// in the reading position (1). The rest pose is the real desk surface when it can be found, so
        /// the book reads as being picked up off the desk rather than spawning in front of the camera.
        /// Both ends are recomputed from the camera each frame so the held book stays planted in view.
        /// </summary>
        void ApplyHeldPose(float raise)
        {
            if (root == null)
                return;

            var cam = ActiveCamera();

            if (cam == null)
                return;

            var camTransform = cam.transform;
            var basePosition = camTransform.position
                + camTransform.forward * holdDistance
                + camTransform.right * holdHorizontalOffset;
            var heldPosition = basePosition + camTransform.up * holdVerticalOffset;
            var heldRotation = Quaternion.LookRotation(camTransform.forward, camTransform.up)
                * Quaternion.Euler(heldEulerOffset);

            if (!TryGetRestPose(cam, out var restPosition, out var restRotation))
            {
                // No anchor or desk found: fall back to stowing lower and further from the camera.
                restPosition = basePosition
                    + camTransform.up * (holdVerticalOffset - stowDrop)
                    + camTransform.forward * stowBack;
                restRotation = heldRotation;
            }

            root.position = Vector3.Lerp(restPosition, heldPosition, raise);
            root.rotation = Quaternion.Slerp(restRotation, heldRotation, raise);
        }

        /// <summary>
        /// Resting pose for the closed book on the desk. Prefers the hand-placed <see cref="deskAnchor"/>
        /// (or this component's own transform when it is placed on the desk); otherwise auto-rests it on
        /// the resolved desk bounds. Returns false when neither is available.
        /// </summary>
        bool TryGetRestPose(Camera cam, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;

            // Hand-placed anchor wins. Fall back to this object's own transform when it has been placed
            // somewhere meaningful (i.e. not the runtime-spawned instance sitting at the origin).
            var anchor = deskAnchor != null
                ? deskAnchor
                : (transform.position.sqrMagnitude > 0.0001f ? transform : null);

            if (anchor != null)
            {
                position = anchor.position;
                rotation = anchor.rotation;
                return true;
            }

            ResolveDesk();

            if (!hasDeskBounds || cam == null)
                return false;

            var toCamera = Vector3.ProjectOnPlane(cam.transform.position - deskBounds.center, Vector3.up);
            toCamera = toCamera.sqrMagnitude > 1e-4f ? toCamera.normalized : -Vector3.forward;
            var right = Vector3.Cross(Vector3.up, toCamera).normalized;

            position = new Vector3(deskBounds.center.x, deskBounds.max.y + deskRestClearance, deskBounds.center.z)
                + toCamera * deskRestForwardOffset
                + right * deskRestLateralOffset;

            // +Z is the top of the page; point it away from the reader so the lifted book is upright.
            rotation = Quaternion.LookRotation(-toCamera, Vector3.up);
            return true;
        }

        /// <summary>
        /// World position at which the closed book rests on the desk (its anchor, this transform, or the
        /// resolved desk fallback). The desk item tray uses it to keep item boxes clear of the book's
        /// footprint, so a bought item never ends up sitting underneath it. Returns false when no rest
        /// pose can be resolved yet (no anchor and the desk has not been found).
        /// </summary>
        public bool TryGetDeskRestPosition(out Vector3 position)
            => TryGetRestPose(ActiveCamera(), out position, out _);

        void ResolveDesk()
        {
            if (deskResolved)
                return;

            deskResolved = true;

            var deskObject = GameObject.Find(deskObjectName);

            if (deskObject == null)
                deskObject = GameObject.Find("Saloon Table") ?? GameObject.Find("Table");

            if (deskObject == null)
            {
                Log($"ResolveDesk: desk '{deskObjectName}' (and fallbacks) NOT found; will use camera-relative stow.");
                return;
            }

            var renderers = deskObject.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
            {
                Log($"ResolveDesk: desk '{deskObject.name}' found but has no Renderers; cannot size it.");
                return;
            }

            deskBounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                deskBounds.Encapsulate(renderers[i].bounds);

            hasDeskBounds = true;
            Log($"ResolveDesk: using '{deskObject.name}', center={deskBounds.center}, topY={deskBounds.max.y}, size={deskBounds.size}");
        }

        /// <summary>
        /// Shows/hides a pager arrow and sets its usable state: an unavailable direction stays visible but
        /// fades its glyph (the stamp itself fades via the button's disabledColor) so the control still
        /// reads as a button, just not one you can press here.
        /// </summary>
        static void SetArrowState(Button arrow, Text label, bool visible, bool enabled)
        {
            if (arrow == null)
                return;

            arrow.gameObject.SetActive(visible);
            arrow.interactable = enabled;

            if (label != null)
                label.color = enabled
                    ? PaperColor
                    : new Color(PaperColor.r, PaperColor.g, PaperColor.b, 0.30f);
        }

        /// <summary>A printed look: a soft warm-dark shadow under ink, as if pressed into the paper.</summary>
        static Text InkShadow(Text text, float distance = 2.5f)
        {
            if (text == null)
                return text;

            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.10f, 0.05f, 0.02f, 0.35f);
            shadow.effectDistance = new Vector2(distance, -distance);
            return text;
        }

        /// <summary>Marks a built image as pure decoration so it never eats a click meant for a control.</summary>
        static GameObject Decor(GameObject built)
        {
            if (built != null && built.TryGetComponent<Image>(out var image))
                image.raycastTarget = false;

            return built;
        }

        /// <summary>A thin ruled border inset on a page, drawn as four strips, like a printed ledger frame.</summary>
        void BuildPageFrame(Transform canvas, float centerX, float pageWidthPx, float pageHeightPx)
        {
            const float inset = 20f;
            const float thickness = 3f;
            var spanX = pageWidthPx - inset * 2f;
            var spanY = pageHeightPx - inset * 2f;
            var halfX = pageWidthPx * 0.5f - inset;
            var halfY = pageHeightPx * 0.5f - inset;

            Decor(RuntimeUiFactory.CreateImage(canvas, "Frame Top", new Vector2(spanX, thickness), new Vector2(centerX, halfY), RuleColor));
            Decor(RuntimeUiFactory.CreateImage(canvas, "Frame Bottom", new Vector2(spanX, thickness), new Vector2(centerX, -halfY), RuleColor));
            Decor(RuntimeUiFactory.CreateImage(canvas, "Frame Left", new Vector2(thickness, spanY), new Vector2(centerX - halfX, 0f), RuleColor));
            Decor(RuntimeUiFactory.CreateImage(canvas, "Frame Right", new Vector2(thickness, spanY), new Vector2(centerX + halfX, 0f), RuleColor));
        }

        void Log(string message)
        {
            if (logDiagnostics)
                Debug.Log($"[BookShopView] {message}");
        }

        Camera ActiveCamera() => worldCamera != null ? worldCamera : Camera.main;

        void ResolveReferences()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;
        }
    }

    /// <summary>
    /// Marker placed on the book's click collider so <see cref="BookShopView"/>'s raycast can
    /// identify the prop it hit. Clicks are detected by <see cref="BookShopView"/> via the Input
    /// System (legacy OnMouse* messages do not fire under the new input backend), so this type
    /// carries no behaviour — it is purely a tag.
    /// </summary>
    [DisallowMultipleComponent]
    public class BookClickTarget : MonoBehaviour
    {
    }
}
