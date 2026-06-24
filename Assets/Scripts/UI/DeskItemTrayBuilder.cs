using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Builds one desk item box: a placeholder cube body (with the collider that receives clicks), a
    /// camera-facing name/count label, and two hidden Use/Cancel tiles. Pure geometry — shared by the
    /// runtime tray fallback and the authoring tool, which freezes a box built here into an editable
    /// prefab (Tools > Meniscus > Author Desk Item Box Prefab) the tray then instantiates per stack.
    /// Splits geometry from logic (see DeskItemTray).
    /// </summary>
    public static class DeskItemTrayBuilder
    {
        // Sized for the saloon's scale (the book spread is ~0.6 wide). Tune alongside DeskItemTray.BoxSpacing.
        public const float BoxSize = 0.22f;

        public static void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            var mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", color); // URP Lit
            mpb.SetColor("_Color", color);     // built-in fallback
            renderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// Builds an item-agnostic box (geometry + relays + bound part references) WITHOUT tying it to a
        /// tray or item. This is what the authoring tool freezes into the editable box prefab, and what
        /// <see cref="BuildBox"/> defers to for the runtime fallback. A designer can replace the cube body
        /// in that prefab with a modelled box (mesh/material/Animator) and the wiring still holds.
        /// </summary>
        public static DeskItemBox BuildBoxGeometry(Transform parent)
        {
            // Container (unscaled) carries the DeskItemBox; children carry geometry/colliders.
            var container = new GameObject("Desk Item Box");
            container.transform.SetParent(parent, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(container.transform, false);
            body.transform.localScale = Vector3.one * BoxSize;
            var bodyRenderer = body.GetComponent<Renderer>();
            var bodyColor = new Color(0.55f, 0.4f, 0.25f);
            ApplyColor(bodyRenderer, bodyColor);
            body.AddComponent<DeskItemTileRelay>(); // wired below

            var label = CreateLabel(container.transform, new Vector3(0f, BoxSize * 0.9f, 0f));

            var box = container.AddComponent<DeskItemBox>();

            // Selecting the body is the only per-box interaction now; committing is the shared desk USE
            // button (DeskUseButton), so no per-box Use/Cancel tiles are built.
            body.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Body);

            box.Bind(label, null, null, bodyRenderer, bodyColor);
            return box;
        }

        /// <summary>
        /// Builds the single shared "USE" control — 2D text on the desk with a click collider — that
        /// commits the player's current selection (see <see cref="DeskUseButton"/> and DeskItemTray).
        /// </summary>
        public static DeskUseButton BuildUseButton(Transform parent)
        {
            var go = new GameObject("Use Button");
            go.transform.SetParent(parent, false);
            // The tray faces the player (local +Z toward the camera), so place the text in the clear
            // centre gap, a touch above the desk and toward the player.
            go.transform.localPosition = new Vector3(0f, 0.015f, 0.05f);
            // A TextMesh reads from its -Z side, but the tray's +Z faces the camera, so flip 180° about
            // up — otherwise the text renders mirrored.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var text = go.AddComponent<TextMesh>();
            text.text = "USE";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = 0.012f;
            text.fontStyle = FontStyle.Bold;

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.34f, 0.14f, 0.04f);

            var button = go.AddComponent<DeskUseButton>();
            button.Initialize(text);
            return button;
        }

        /// <summary>
        /// Runtime fallback used only when the tray has no authored box prefab: builds the placeholder
        /// box (see <see cref="BuildBoxGeometry"/>) and initialises it for <paramref name="item"/>.
        /// </summary>
        public static DeskItemBox BuildBox(Transform parent, DeskItemTray tray, ItemDefinition item)
        {
            var box = BuildBoxGeometry(parent);
            box.gameObject.name = $"Desk Item Box ({item.Id})";
            box.Initialize(tray, item);
            return box;
        }

        static TextMesh CreateLabel(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // Same orientation fix as the USE text: a TextMesh reads from its -Z side and the tray's +Z
            // faces the camera, so flip 180° about up or the label renders mirrored.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // Scale the text with the box so it stays readable (a fixed scale looked tiny on a bigger box).
            go.transform.localScale = Vector3.one * (BoxSize * 0.2f);

            var text = go.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.1f;
            text.color = Color.white;
            return text;
        }

    }
}
