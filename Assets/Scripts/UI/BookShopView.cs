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

        static readonly Color CoverColor = new(0.34f, 0.16f, 0.08f);
        static readonly Color PageColor = new(0.86f, 0.78f, 0.6f);

        // The menu is printed onto the open page: a warm paper fill with dark ink, not a floating UI panel.
        static readonly Color PaperColor = new(0.91f, 0.84f, 0.66f, 1f);
        static readonly Color InkColor = new(0.20f, 0.10f, 0.04f);
        static readonly Color InkSoftColor = new(0.34f, 0.20f, 0.10f);
        static readonly Color RuleColor = new(0.34f, 0.20f, 0.10f, 0.35f);
        static readonly Color RowTransparent = new(0f, 0f, 0f, 0f);
        static readonly Color RowHover = new(0.30f, 0.17f, 0.08f, 0.14f);
        static readonly Color RowPressed = new(0.30f, 0.17f, 0.08f, 0.24f);
        static readonly Color StampColor = new(0.30f, 0.15f, 0.07f, 0.92f);
        static readonly Color StampHover = new(0.42f, 0.23f, 0.10f, 0.95f);
        static readonly Color StampPressed = new(0.20f, 0.10f, 0.04f, 1f);

        Bounds deskBounds;
        bool deskResolved;
        bool hasDeskBounds;

        Transform root;
        Transform hingePivot;
        Transform menuCanvasTransform;
        CanvasGroup menuGroup;
        GameObject finishButtonObject;
        Text browseHint;
        ShopManager shopManager;
        float menuScaleBase = 1f;
        bool built;
        bool isOpen;

        // True while the book was opened by clicking it mid-round: the catalog is shown to read, but
        // purchasing is disabled and the only action is to close it again.
        bool previewMode;
        float animT;

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
            root = new GameObject("Diegetic Book Shop").transform;
            root.SetParent(transform, false);

            // An open book lying as a spread: the spine sits at the local origin with a page either side.
            CreateCoverCube(
                root, "Book Back Cover", CoverColor,
                new Vector3(pageWidth * 2f + coverThickness * 2f, coverThickness, pageDepth + coverThickness * 2f),
                Vector3.zero);

            var pageSize = new Vector3(pageWidth * 0.94f, coverThickness * 0.5f, pageDepth * 0.92f);

            CreatePageSheet(
                root, "Book Left Page", pageSize,
                new Vector3(-pageWidth * 0.5f, coverThickness * 0.75f, 0f));
            CreatePageSheet(
                root, "Book Right Page", pageSize,
                new Vector3(pageWidth * 0.5f, coverThickness * 0.75f, 0f));

            hingePivot = new GameObject("Book Hinge").transform;
            hingePivot.SetParent(root, false);
            hingePivot.localPosition = Vector3.zero;

            // Front cover starts over the right page and flips open across the spine as the book opens.
            CreateCoverCube(
                hingePivot, "Book Front Cover", CoverColor,
                new Vector3(pageWidth, coverThickness, pageDepth),
                new Vector3(pageWidth * 0.5f, coverThickness, 0f));

            BuildMenuCanvas(catalog);

            // A click target over the whole book so it can be picked up off the desk to preview the
            // catalog mid-round (and clicked again to close). Sized to the closed spread footprint.
            var clickCollider = root.gameObject.AddComponent<BoxCollider>();
            clickCollider.center = new Vector3(0f, coverThickness, 0f);
            clickCollider.size = new Vector3(pageWidth * 2.1f, coverThickness * 3f, pageDepth * 1.05f);
            // A trigger so it never blocks the coins on the desk; it still receives mouse clicks.
            clickCollider.isTrigger = true;

            var clickTarget = root.gameObject.AddComponent<BookClickTarget>();
            clickTarget.Clicked = OnBookClicked;
            clickTarget.LogDiagnostics = logDiagnostics;

            built = true;
            root.gameObject.SetActive(false);
        }

        void BuildMenuCanvas(IReadOnlyList<ItemDefinition> catalog)
        {
            var itemCount = catalog?.Count ?? 0;

            // The canvas spans the whole spread; content is divided into a left page (heading) and a
            // right page (the order list). Sizes are in canvas pixels and scaled to world by menuScaleBase.
            const float pixelWidth = 1040f;
            const float topPad = 120f;
            const float rowHeight = 96f;
            const float footerHeight = 150f;
            var pixelHeight = topPad + Mathf.Max(1, itemCount) * rowHeight + footerHeight;
            var pixelSize = new Vector2(pixelWidth, pixelHeight);

            var canvas = RuntimeUiFactory.CreateWorldCanvas(root, "Book Menu Canvas", pixelSize, ActiveCamera());
            menuCanvasTransform = canvas.transform;

            // Centred over the spine, just proud of the paper so it reads as printing on the spread.
            menuCanvasTransform.localPosition = new Vector3(0f, coverThickness + menuFloatHeight, 0f);

            menuScaleBase = menuWorldWidth / pixelWidth;
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

            // Left page: the heading.
            RuntimeUiFactory.CreateText(
                canvas.transform, "Menu Title", "Saloon\nMenu",
                new Vector2(leftCenter, 60f), new Vector2(pageTextWidth, 240f), 72, InkColor,
                TextAnchor.MiddleCenter, bold: true);
            RuntimeUiFactory.CreateText(
                canvas.transform, "Menu Subtitle", "~ Order between rounds ~",
                new Vector2(leftCenter, -120f), new Vector2(pageTextWidth, 50f), 30, InkSoftColor);

            // Right page: the order list.
            var rowY = top - topPad;
            var rowSize = new Vector2(pageTextWidth, rowHeight - 16f);

            if (catalog != null)
            {
                foreach (var item in catalog)
                {
                    if (item == null)
                        continue;

                    var captured = item;
                    var price = $"${item.Cost}";

                    var row = RuntimeUiFactory.CreateButton(
                        canvas.transform, $"{item.Id} Order", item.DisplayName, rowSize,
                        new Vector2(rightCenter, rowY), 36, () => OnBuy(captured),
                        normalColor: RowTransparent,
                        highlightedColor: RowHover,
                        pressedColor: RowPressed,
                        labelColor: InkColor,
                        labelAlignment: TextAnchor.MiddleLeft,
                        labelPadding: new Vector2(24f, 0f));

                    // Price to the right, like a menu line.
                    RuntimeUiFactory.CreateText(
                        row.transform, "Price", price,
                        new Vector2(-24f, 0f), rowSize, 36, InkSoftColor, TextAnchor.MiddleRight);

                    // Ruled line under each order.
                    RuntimeUiFactory.CreateImage(
                        canvas.transform, $"{item.Id} Rule", new Vector2(pageTextWidth, 2f),
                        new Vector2(rightCenter, rowY - rowHeight * 0.5f + 8f), RuleColor);

                    rowY -= rowHeight;
                }
            }

            var footerPosition = new Vector2(rightCenter, -top + 84f);

            finishButtonObject = RuntimeUiFactory.CreateButton(
                canvas.transform, "Finish Drink Button", "Finish Drink",
                new Vector2(pageTextWidth * 0.85f, 90f), footerPosition, 36, OnFinish, boldLabel: true,
                normalColor: StampColor,
                highlightedColor: StampHover,
                pressedColor: StampPressed,
                labelColor: PaperColor).gameObject;

            // Shown only while previewing mid-round (purchasing disabled); hidden during the shop phase.
            browseHint = RuntimeUiFactory.CreateText(
                canvas.transform, "Browse Hint", "— Just looking · click the book to close —",
                footerPosition, new Vector2(pageTextWidth, 60f), 26, InkSoftColor);
            browseHint.enabled = false;
        }

        void OnBuy(ItemDefinition item)
        {
            if (shopManager != null)
                shopManager.TryBuyItem(item);
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

        Transform CreateCoverCube(Transform parent, string name, Color color, Vector3 size, Vector3 localPosition)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = size;

            var collider = cube.GetComponent<Collider>();

            if (collider != null)
                Destroy(collider);

            var renderer = cube.GetComponent<Renderer>();

            if (renderer != null)
                renderer.sharedMaterial = CreateOpaqueMaterial($"{name} Material", color);

            return cube.transform;
        }

        void CreatePageSheet(Transform parent, string name, Vector3 size, Vector3 localPosition) =>
            CreateCoverCube(parent, name, PageColor, size, localPosition);

        static Material CreateOpaqueMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader)
            {
                name = materialName,
                color = color
            };
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
