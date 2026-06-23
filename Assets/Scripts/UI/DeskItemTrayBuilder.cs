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
        const float TileSize = 0.11f;

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
            var use = CreateTile(container.transform, "USE",
                new Vector3(-BoxSize, BoxSize * 0.6f, 0f), new Color(0.2f, 0.55f, 0.25f));
            var cancel = CreateTile(container.transform, "CANCEL",
                new Vector3(BoxSize, BoxSize * 0.6f, 0f), new Color(0.6f, 0.25f, 0.2f));

            var box = container.AddComponent<DeskItemBox>();

            body.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Body);
            use.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Use);
            cancel.GetComponent<DeskItemTileRelay>().Initialize(box, DeskItemTileRelay.Kind.Cancel);

            box.Bind(label, use, cancel, bodyRenderer, bodyColor);
            return box;
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

        static GameObject CreateTile(Transform parent, string text, Vector3 localPos, Color color)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = text;
            tile.transform.SetParent(parent, false);
            tile.transform.localPosition = localPos;
            tile.transform.localScale = new Vector3(TileSize * 1.6f, TileSize, TileSize * 0.4f);
            ApplyColor(tile.GetComponent<Renderer>(), color);
            tile.AddComponent<DeskItemTileRelay>();

            var labelGo = new GameObject("Caption");
            labelGo.transform.SetParent(tile.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            labelGo.transform.localScale = Vector3.one * 0.6f;
            var caption = labelGo.AddComponent<TextMesh>();
            caption.text = text;
            caption.anchor = TextAnchor.MiddleCenter;
            caption.alignment = TextAlignment.Center;
            caption.fontSize = 48;
            caption.characterSize = 0.02f;
            caption.color = Color.white;

            tile.SetActive(false); // hidden until the box is selected
            return tile;
        }
    }
}
