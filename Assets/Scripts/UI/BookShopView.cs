using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Items;
using UnityEngine;

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
        [Tooltip("How far the menu floats off the book's open face toward the reader.")]
        [SerializeField] float menuFloatHeight = 0.03f;

        [Header("Book Prop")]
        [SerializeField] float pageWidth = 0.26f;
        [SerializeField] float pageDepth = 0.34f;
        [SerializeField] float coverThickness = 0.02f;
        [SerializeField] float openAngle = 100f;
        [SerializeField, Min(0.05f)] float openSeconds = 0.55f;

        [Header("Menu")]
        [SerializeField] float menuWorldWidth = 0.46f;

        static readonly Color CoverColor = new(0.34f, 0.16f, 0.08f);
        static readonly Color PageColor = new(0.86f, 0.78f, 0.6f);
        static readonly Color MenuBackgroundColor = new(0.16f, 0.1f, 0.05f, 0.97f);
        static readonly Color MenuTitleColor = new(0.97f, 0.86f, 0.58f);
        static readonly Color MenuBodyColor = new(0.85f, 0.76f, 0.6f);

        Transform root;
        Transform hingePivot;
        Transform menuCanvasTransform;
        CanvasGroup menuGroup;
        ShopManager shopManager;
        float menuScaleBase = 1f;
        bool built;
        bool isOpen;
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

            // Place it stowed immediately so the first frame doesn't flash at the origin; Update lifts it.
            ApplyHeldPose(Mathf.SmoothStep(0f, 1f, animT));
        }

        public void Close()
        {
            isOpen = false;
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

            if (menuGroup != null)
            {
                menuGroup.alpha = reveal;
                menuGroup.interactable = reveal > 0.95f;
                menuGroup.blocksRaycasts = reveal > 0.95f;
            }

            if (menuCanvasTransform != null)
            {
                menuCanvasTransform.localScale = Vector3.one * (menuScaleBase * Mathf.Lerp(0.7f, 1f, reveal));

                if (reveal > 0f)
                {
                    var cam = ActiveCamera();

                    // A world-space canvas reads correctly when its forward matches the camera's, so
                    // it is viewed along its own normal rather than mirrored.
                    if (cam != null)
                        menuCanvasTransform.rotation = cam.transform.rotation;
                }
            }

            if (!isOpen && animT <= 0f && root.gameObject.activeSelf)
                root.gameObject.SetActive(false);
        }

        void Build(IReadOnlyList<ItemDefinition> catalog)
        {
            root = new GameObject("Diegetic Book Shop").transform;
            root.SetParent(transform, false);

            CreateCoverCube(
                root, "Book Back Cover", CoverColor,
                new Vector3(pageWidth, coverThickness, pageDepth),
                new Vector3(pageWidth * 0.5f, 0f, 0f));

            CreatePageSheet(
                root, "Book Right Page",
                new Vector3(pageWidth * 0.9f, coverThickness * 0.5f, pageDepth * 0.9f),
                new Vector3(pageWidth * 0.5f, coverThickness * 0.75f, 0f));

            hingePivot = new GameObject("Book Hinge").transform;
            hingePivot.SetParent(root, false);
            hingePivot.localPosition = Vector3.zero;

            CreateCoverCube(
                hingePivot, "Book Front Cover", CoverColor,
                new Vector3(pageWidth, coverThickness, pageDepth),
                new Vector3(pageWidth * 0.5f, coverThickness, 0f));

            BuildMenuCanvas(catalog);

            built = true;
            root.gameObject.SetActive(false);
        }

        void BuildMenuCanvas(IReadOnlyList<ItemDefinition> catalog)
        {
            var itemCount = catalog?.Count ?? 0;

            const float headerHeight = 150f;
            const float rowHeight = 74f;
            const float footerHeight = 96f;
            var pixelWidth = 560f;
            var pixelHeight = headerHeight + itemCount * rowHeight + footerHeight;
            var pixelSize = new Vector2(pixelWidth, pixelHeight);

            var canvas = RuntimeUiFactory.CreateWorldCanvas(root, "Book Menu Canvas", pixelSize, ActiveCamera());
            menuCanvasTransform = canvas.transform;
            menuCanvasTransform.localPosition = new Vector3(0f, coverThickness + menuFloatHeight, 0f);

            menuScaleBase = menuWorldWidth / pixelWidth;
            menuCanvasTransform.localScale = Vector3.one * menuScaleBase;

            menuGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            menuGroup.alpha = 0f;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;

            RuntimeUiFactory.CreateImage(canvas.transform, "Menu Background", pixelSize, Vector2.zero, MenuBackgroundColor);

            var top = pixelHeight * 0.5f;

            RuntimeUiFactory.CreateText(
                canvas.transform, "Menu Title", "SALOON MENU",
                new Vector2(0f, top - 48f), new Vector2(pixelWidth - 60f, 48f), 34, MenuTitleColor,
                TextAnchor.MiddleCenter, bold: true);
            RuntimeUiFactory.CreateText(
                canvas.transform, "Menu Subtitle", "Order between rounds",
                new Vector2(0f, top - 96f), new Vector2(pixelWidth - 60f, 32f), 18, MenuBodyColor);

            var rowY = top - headerHeight;
            var buttonSize = new Vector2(pixelWidth - 80f, rowHeight - 14f);

            if (catalog != null)
            {
                foreach (var item in catalog)
                {
                    if (item == null)
                        continue;

                    var captured = item;
                    var label = $"{item.DisplayName.ToUpperInvariant()}   ${item.Cost}";

                    RuntimeUiFactory.CreateButton(
                        canvas.transform, $"{item.Id} Order", label, buttonSize,
                        new Vector2(0f, rowY), 18, () => OnBuy(captured));

                    rowY -= rowHeight;
                }
            }

            RuntimeUiFactory.CreateButton(
                canvas.transform, "Finish Drink Button", "FINISH DRINK",
                new Vector2(240f, 56f), new Vector2(0f, -top + 50f), 20, OnFinish, boldLabel: true);
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
        /// Poses the book in front of the active camera. <paramref name="raise"/> 0 keeps it stowed
        /// (lower and further, as if down at the desk); 1 holds it up in the reading position. The pose
        /// is recomputed from the camera each frame so the book stays planted in front of the view.
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
            var stowedPosition = basePosition
                + camTransform.up * (holdVerticalOffset - stowDrop)
                + camTransform.forward * stowBack;

            root.position = Vector3.Lerp(stowedPosition, heldPosition, raise);
            root.rotation = Quaternion.LookRotation(camTransform.forward, camTransform.up)
                * Quaternion.Euler(heldEulerOffset);
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
}
