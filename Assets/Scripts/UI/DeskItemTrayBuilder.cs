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

            var card = BuildDescriptionCard(container.transform, out var label, out var backing);

            var box = container.AddComponent<DeskItemBox>();

            // Selecting the body is the only per-box interaction now; committing is the shared desk USE
            // button (DeskUseButton), so no per-box Use/Cancel tiles are built.
            body.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Body);

            box.Bind(label, card, backing, bodyRenderer, bodyColor);
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

        /// <summary>
        /// Builds the selection card a box reveals only while it's picked up (DeskItemBox toggles it): a
        /// dark plate behind readable light text, floating above the box. The plate gives the text contrast
        /// on the busy desk; DeskItemBox auto-sizes it to whatever description the item carries. Both face
        /// the player via the same 180° flip as the USE text (a TextMesh reads from its -Z side while the
        /// tray's +Z faces the camera, so an unflipped card would render mirrored).
        /// </summary>
        static GameObject BuildDescriptionCard(Transform parent, out TextMesh text, out Transform backing)
        {
            var card = new GameObject("Description Card");
            card.transform.SetParent(parent, false);
            // Float clear above the box, which itself lifts when selected.
            card.transform.localPosition = new Vector3(0f, BoxSize * 1.6f, 0f);
            card.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Dark backing plate: a thin cube reads from any angle (no two-sided material needed). This is
            // just a sane starting scale — DeskItemBox.FitBackingToText sizes it to the text at runtime.
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Backing";
            plate.transform.SetParent(card.transform, false);
            plate.transform.localScale = new Vector3(0.3f, 0.14f, 0.004f);
            // Behind the text from the player's view (the card's local +Z points away from the camera).
            plate.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            StripCollider(plate); // never intercept the tray's click raycast
            ApplyColor(plate.GetComponent<Renderer>(), new Color(0.05f, 0.04f, 0.03f));
            backing = plate.transform;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(card.transform, false);
            // In front of the plate, toward the player.
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.002f);

            text = textGo.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 64;
            text.characterSize = 0.011f;
            text.richText = true; // bold name header via <b> in DeskItemBox.RefreshCard
            text.color = new Color(0.98f, 0.95f, 0.86f);
            return card;
        }

        static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();

            if (collider == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(collider);
            else
                Object.DestroyImmediate(collider);
        }

    }
}
