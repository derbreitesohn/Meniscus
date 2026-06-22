using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;
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

        [Header("Held Placement (relative to the camera)")]
        [Tooltip("Metres in front of the camera the open book is held.")]
        [SerializeField] float holdDistance = 0.95f;
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
        [SerializeField] float openAngle = 100f;
        [SerializeField, Min(0.05f)] float openSeconds = 0.55f;

        [Header("Menu")]
        [Tooltip("World width of the printed menu, spread across both pages. Keep it at or inside the full " +
                 "two-page width so the ink stays on the paper.")]
        [SerializeField] float menuWorldWidth = 0.56f;
        [Tooltip("World depth bound for the printed menu so it stays within the page depth.")]
        [SerializeField] float menuWorldDepth = 0.34f;
        [Tooltip("Seconds for a page-turn animation.")]
        [SerializeField, Min(0.05f)] float pageTurnSeconds = 0.35f;

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
        Text browseHint;
        ShopManager shopManager;
        float menuScaleBase = 1f;
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
        Text ticketName;
        Text ticketDesc;
        Text ticketCostOwn;
        Button buyButton;
        Text buyLabel;

        // Right-page paging chrome.
        Button prevArrow;
        Button nextArrow;
        Text pageIndicator;

        // Geometry needed to rebuild the right page each turn (set in BuildMenuCanvas).
        float rowHeightPx;
        float rowWidthPx;

        // True while the book was opened by clicking it mid-round: the catalog is shown to read, but
        // purchasing is disabled and the only action is to close it again.
        bool previewMode;
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

            root.gameObject.SetActive(true);
            isOpen = true;
            previewMode = false;

            Log($"Open (shop phase, buyable). catalogCount={catalog?.Count ?? 0}");

            // Seat it on the desk immediately so the first frame shows it resting there; Update lifts it.
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
            isOpen = false;
            previewMode = false;
        }

        /// <summary>
        /// Click handler for the book prop. Closed on the desk, a click lifts it open as a read-only
        /// preview; while previewing, a click closes it again. Ignored once the shop phase has opened it
        /// for real, where closing is done via "Finish Drink".
        /// </summary>
        void OnBookClicked()
        {
            Log($"OnBookClicked received. built={built}, isOpen={isOpen}, previewMode={previewMode}");

            if (!built)
                return;

            if (!isOpen)
            {
                isOpen = true;
                previewMode = true;
                Log("-> opening read-only preview");
            }
            else if (previewMode)
            {
                isOpen = false;
                previewMode = false;
                Log("-> closing preview");
            }
            else
            {
                Log("-> ignored (shop phase open; close via Finish Drink)");
            }
        }

        void Update()
        {
            if (!built)
                return;

            UpdatePageTurn();

            var target = isOpen ? 1f : 0f;
            animT = Mathf.MoveTowards(animT, target, Time.deltaTime / Mathf.Max(0.05f, openSeconds));

            var openProgress = Mathf.SmoothStep(0f, 1f, animT);
            hingePivot.localRotation = Quaternion.Euler(0f, 0f, openProgress * openAngle);

            // Lift the book from its stowed pose up into the held pose as the cover opens.
            ApplyHeldPose(openProgress);

            var reveal = Mathf.Clamp01((animT - 0.5f) / 0.5f);

            // While previewing mid-round the catalog is readable but not purchasable: keep the menu
            // non-interactive so buy buttons do nothing and clicks fall through to close the book.
            var canPurchase = reveal > 0.95f && !previewMode;

            if (menuGroup != null)
            {
                menuGroup.alpha = reveal;
                menuGroup.interactable = canPurchase;
                menuGroup.blocksRaycasts = canPurchase;
            }

            // Hide the "Finish Drink" action when only previewing; show a hint to close instead.
            if (finishButtonObject != null)
                finishButtonObject.SetActive(!previewMode);

            if (browseHint != null)
                browseHint.enabled = previewMode && reveal > 0.5f;

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

        void Build(IReadOnlyList<ItemDefinition> catalog)
        {
            if (authoredBook != null)
                ResolveAuthoredProp();
            else
                BuildProp();

            // A page-sized sheet hung on the spine, swept across the spread during a flip. Built over
            // the prop (so it pivots on the spine, X = 0) and kept inactive until a turn starts.
            turningPivot = BookShopBuilder.BuildTurningSheet(root, new BookShopBuilder.BookPropParams
            {
                pageWidth = pageWidth,
                pageDepth = pageDepth,
                coverThickness = coverThickness,
                coverColor = CoverColor,
                pageColor = PageColor,
            });

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
            });

            hingePivot = root.Find("Book Hinge");

            var clickTarget = root.GetComponent<BookClickTarget>();
            clickTarget.Clicked = OnBookClicked;
            clickTarget.LogDiagnostics = logDiagnostics;
        }

        void ResolveAuthoredProp()
        {
            root = authoredBook;
            hingePivot = root.Find("Book Hinge");

            // onClick/Clicked delegates are not serialized, so re-wire the authored prop's click target.
            var clickTarget = root.GetComponent<BookClickTarget>();

            if (clickTarget == null)
                clickTarget = root.gameObject.AddComponent<BookClickTarget>();

            clickTarget.Clicked = OnBookClicked;
            clickTarget.LogDiagnostics = logDiagnostics;
        }

        void BuildMenuCanvas(IReadOnlyList<ItemDefinition> catalog)
        {
            // The canvas is a FIXED-size spread now (not grown by item count): a left "order ticket"
            // page and a right paginated item list. Sizes are in canvas pixels; the whole canvas is
            // scaled to world via menuScaleBase so it fits within both the page width and depth.
            const float pixelWidth = 1040f;
            const float pixelHeight = 1280f;
            var pixelSize = new Vector2(pixelWidth, pixelHeight);

            var canvas = RuntimeUiFactory.CreateWorldCanvas(root, "Book Menu Canvas", pixelSize, ActiveCamera());
            menuCanvasTransform = canvas.transform;

            // Centred over the spine, just proud of the paper so it reads as printing on the spread.
            menuCanvasTransform.localPosition = new Vector3(0f, coverThickness + menuFloatHeight, 0f);

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
            RuntimeUiFactory.CreateImage(canvas.transform, "Left Page Paper", new Vector2(pageWidthPx, pixelHeight), new Vector2(leftCenter, 0f), PaperColor);
            RuntimeUiFactory.CreateImage(canvas.transform, "Right Page Paper", new Vector2(pageWidthPx, pixelHeight), new Vector2(rightCenter, 0f), PaperColor);
            RuntimeUiFactory.CreateImage(canvas.transform, "Spine Gutter", new Vector2(8f, pixelHeight), Vector2.zero, RuleColor);

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
            // Title.
            RuntimeUiFactory.CreateText(
                canvas, "Menu Title", "Saloon Menu",
                new Vector2(leftCenter, top - 110f), new Vector2(pageTextWidth, 110f), 64, InkColor,
                TextAnchor.MiddleCenter, bold: true);

            RuntimeUiFactory.CreateImage(
                canvas, "Title Rule", new Vector2(pageTextWidth, 3f),
                new Vector2(leftCenter, top - 180f), RuleColor);

            // Selected item name.
            ticketName = RuntimeUiFactory.CreateText(
                canvas, "Ticket Name", "",
                new Vector2(leftCenter, top - 270f), new Vector2(pageTextWidth, 100f), 48, InkColor,
                TextAnchor.UpperLeft, bold: true);

            // Description body.
            ticketDesc = RuntimeUiFactory.CreateText(
                canvas, "Ticket Desc", "",
                new Vector2(leftCenter, 40f), new Vector2(pageTextWidth, 360f), 32, InkSoftColor,
                TextAnchor.UpperLeft);

            // Cost / owned line.
            ticketCostOwn = RuntimeUiFactory.CreateText(
                canvas, "Ticket Cost/Own", "",
                new Vector2(leftCenter, -top + 320f), new Vector2(pageTextWidth, 60f), 34, InkColor,
                TextAnchor.MiddleLeft, bold: true);

            // Buy then Finish Drink, stacked near the bottom of the page.
            buyButton = RuntimeUiFactory.CreateButton(
                canvas, "Buy Button", "", new Vector2(pageTextWidth * 0.9f, 96f),
                new Vector2(leftCenter, -top + 210f), 38, OnBuyClicked, boldLabel: true,
                normalColor: StampColor,
                highlightedColor: StampHover,
                pressedColor: StampPressed,
                labelColor: PaperColor);
            buyLabel = buyButton.transform.Find("Label").GetComponent<Text>();

            finishButtonObject = RuntimeUiFactory.CreateButton(
                canvas, "Finish Drink Button", "Finish Drink",
                new Vector2(pageTextWidth * 0.9f, 96f),
                new Vector2(leftCenter, -top + 90f), 36, OnFinish, boldLabel: true,
                normalColor: StampColor,
                highlightedColor: StampHover,
                pressedColor: StampPressed,
                labelColor: PaperColor).gameObject;

            // Shown only while previewing mid-round (purchasing disabled); hidden during the shop phase.
            browseHint = RuntimeUiFactory.CreateText(
                canvas, "Browse Hint", "— Just looking · click the book to close —",
                new Vector2(leftCenter, -top + 90f), new Vector2(pageTextWidth, 60f), 26, InkSoftColor);
            browseHint.enabled = false;
        }

        /// <summary>
        /// Right page chrome: the list container, the corner page arrows and the page indicator. The
        /// rows themselves are (re)built per page by <see cref="RebuildRightPage"/>. Also sizes the
        /// list area and derives itemsPerPage so the model can split the catalog into pages.
        /// </summary>
        void BuildRightList(Canvas canvas, IReadOnlyList<ItemDefinition> catalog, float rightCenter, float pageTextWidth, float top)
        {
            const float headerPad = 130f;   // space at the top of the page for the arrows + indicator
            const float footerPad = 110f;   // space at the bottom for the page indicator
            const float rowHeight = 150f;   // readable rows; ≈ 5 rows fit the list area

            var listDepthPx = (top * 2f) - headerPad - footerPad;
            var itemsPerPage = MenuLayout.ItemsPerPage(listDepthPx, rowHeight);

            // Stash the geometry RebuildRightPage needs (rows are positioned within the list container).
            rowHeightPx = rowHeight;
            rowWidthPx = pageTextWidth;

            // Corner arrows.
            prevArrow = RuntimeUiFactory.CreateButton(
                canvas.transform, "Prev Arrow", "‹", new Vector2(70f, 70f),
                new Vector2(rightCenter - pageTextWidth * 0.5f + 24f, top - 70f), 56, () => OnTurn(-1),
                boldLabel: true,
                normalColor: RowTransparent,
                highlightedColor: RowHover,
                pressedColor: RowPressed,
                labelColor: InkColor);

            nextArrow = RuntimeUiFactory.CreateButton(
                canvas.transform, "Next Arrow", "›", new Vector2(70f, 70f),
                new Vector2(rightCenter + pageTextWidth * 0.5f - 24f, top - 70f), 56, () => OnTurn(1),
                boldLabel: true,
                normalColor: RowTransparent,
                highlightedColor: RowHover,
                pressedColor: RowPressed,
                labelColor: InkColor);

            pageIndicator = RuntimeUiFactory.CreateText(
                canvas.transform, "Page Indicator", "",
                new Vector2(rightCenter, -top + 60f), new Vector2(pageTextWidth, 50f), 30, InkSoftColor);

            // The rows live under a dedicated container so a page rebuild only clears the list.
            var containerObject = RuntimeUiFactory.CreateImage(
                canvas.transform, "Right List", new Vector2(pageTextWidth, listDepthPx),
                new Vector2(rightCenter, top - headerPad - listDepthPx * 0.5f), RowTransparent);
            rightListContainer = containerObject.transform;

            model.SetCatalog(catalog, itemsPerPage);
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

            var pageItems = model.CurrentPageItems();
            var rowSize = new Vector2(rowWidthPx, rowHeightPx - 16f);

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

                var row = RuntimeUiFactory.CreateButton(
                    background.transform, $"{item.Id} Order", item.DisplayName, rowSize,
                    Vector2.zero, 38, () => OnSelect(captured),
                    normalColor: RowTransparent,
                    highlightedColor: RowHover,
                    pressedColor: RowPressed,
                    labelColor: InkColor,
                    labelAlignment: TextAnchor.MiddleLeft,
                    labelPadding: new Vector2(60f, 0f));

                // ▸ selection pointer in the left margin (hidden until selected).
                var pointer = RuntimeUiFactory.CreateText(
                    row.transform, "Pointer", "▸",
                    new Vector2(-rowWidthPx * 0.5f + 24f, 0f), new Vector2(40f, rowSize.y), 40, InkColor,
                    TextAnchor.MiddleCenter, bold: true);
                pointer.gameObject.SetActive(false);
                rowPointers[item] = pointer.gameObject;

                // Price.
                RuntimeUiFactory.CreateText(
                    row.transform, "Price", $"${item.Cost}",
                    new Vector2(-24f, 18f), rowSize, 34, InkSoftColor, TextAnchor.MiddleRight);

                // Owned badge ✓×N (only shown when owned > 0).
                var badge = RuntimeUiFactory.CreateText(
                    row.transform, "Owned Badge", "",
                    new Vector2(-24f, -28f), rowSize, 26, InkSoftColor, TextAnchor.MiddleRight);
                rowBadges[item] = badge;

                // Ruled line under each order.
                RuntimeUiFactory.CreateImage(
                    row.transform, "Rule", new Vector2(rowWidthPx, 2f),
                    new Vector2(0f, -rowHeightPx * 0.5f + 8f), RuleColor);

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

            if (prevArrow != null)
                prevArrow.interactable = model.CanTurnPrev;

            if (nextArrow != null)
                nextArrow.interactable = model.CanTurnNext;

            RefreshOwnedBadges();
            ApplySelectionVisual();
        }

        /// <summary>Row click: select the item (read its detail), never purchase.</summary>
        void OnSelect(ItemDefinition item)
        {
            model.Select(item);
            RefreshTicket();
            ApplySelectionVisual();
        }

        /// <summary>Buy button: purchase the selected item if affordable; the book is the only buyer.</summary>
        void OnBuyClicked()
        {
            if (model.Selected == null || shopManager == null)
                return;

            if (shopManager.TryBuyItem(model.Selected))
            {
                RefreshTicket();
                RefreshOwnedBadges();
            }
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

            var owned = shopManager != null ? shopManager.OwnedCount(item) : 0;
            ticketCostOwn.text = $"Cost: ${item.Cost}    Own: {owned}";

            var bankedCash = shopManager != null ? shopManager.BankedCash : 0;
            var deskFull = shopManager != null && shopManager.IsDeskFull;
            var state = model.EvaluateBuy(bankedCash, deskFull);

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
        /// Paints the selected row as a solid bar with its ▸ pointer shown and clears the others, so the
        /// current selection reads strongly against the page.
        /// </summary>
        void ApplySelectionVisual()
        {
            foreach (var pair in rowBackgrounds)
            {
                var selected = pair.Key == model.Selected;
                pair.Value.color = selected ? RowSelected : RowTransparent;

                if (rowPointers.TryGetValue(pair.Key, out var pointer))
                    pointer.SetActive(selected);
            }
        }

        void OnFinish()
        {
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
    /// Tiny relay placed on the book's collider so a mouse click on the 3D prop can be forwarded back
    /// to <see cref="BookShopView"/>. (OnMouseUpAsButton is only delivered to the GameObject carrying
    /// the collider, so the behaviour cannot live on the view itself.)
    /// </summary>
    [DisallowMultipleComponent]
    public class BookClickTarget : MonoBehaviour
    {
        public System.Action Clicked;
        public bool LogDiagnostics = true;

        void OnMouseDown()
        {
            if (LogDiagnostics)
                Debug.Log("[BookClickTarget] OnMouseDown (book collider pressed)");
        }

        void OnMouseUpAsButton()
        {
            if (LogDiagnostics)
                Debug.Log("[BookClickTarget] OnMouseUpAsButton (book click)");

            Clicked?.Invoke();
        }
    }
}
