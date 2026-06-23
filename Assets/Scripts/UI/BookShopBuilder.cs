using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Builds the physical book prop (covers, hinged front cover, pages, click collider) for
    /// <see cref="BookShopView"/>. The data-driven menu canvas is NOT built here — it stays runtime
    /// (its buttons' onClick handlers are wired per-catalog at runtime and are not serializable).
    /// Shared by the runtime fallback and the editor authoring tool.
    /// </summary>
    public static class BookShopBuilder
    {
        public struct BookPropParams
        {
            public float pageWidth;
            public float pageDepth;
            public float coverThickness;
            public Color coverColor;
            public Color pageColor;
        }

        public static Transform BuildProp(Transform parent, BookPropParams p)
        {
            var root = new GameObject("Diegetic Book Shop").transform;
            root.SetParent(parent, false);

            // The book is just its two pages (no separate cover): a fixed LEFT leaf and a RIGHT leaf that
            // folds about the spine. Closed, the right leaf is folded flat on top of the left (a shut book);
            // open, it lies level with the left to form the double-page spread the menu prints across.
            var halfPage = p.pageWidth * 0.5f;
            var pageThickness = p.coverThickness * 0.5f;
            var pageCenterY = pageThickness * 0.5f;            // left leaf rests on the root plane
            var leafSize = new Vector3(p.pageWidth, pageThickness, p.pageDepth);

            CreateCoverCube(root, "Book Left Page", p.pageColor, leafSize, new Vector3(-halfPage, pageCenterY, 0f));

            // Hinge one page-thickness above the left page: folding the right leaf 180° lands it exactly on
            // top of the left page (closed); at 0° it sits level with it (open). See BookShopView for the
            // closed→open drive (1 - openProgress).
            var hingePivot = new GameObject("Book Hinge").transform;
            hingePivot.SetParent(root, false);
            hingePivot.localPosition = new Vector3(0f, pageCenterY + pageThickness * 0.5f, 0f);

            CreateCoverCube(hingePivot, "Book Right Page", p.pageColor, leafSize, new Vector3(halfPage, -pageThickness * 0.5f, 0f));

            var clickCollider = root.gameObject.AddComponent<BoxCollider>();
            clickCollider.center = new Vector3(0f, p.coverThickness, 0f);
            clickCollider.size = new Vector3(p.pageWidth * 2.1f, p.coverThickness * 3f, p.pageDepth * 1.05f);
            clickCollider.isTrigger = true;

            root.gameObject.AddComponent<BookClickTarget>();
            return root;
        }

        /// <summary>
        /// Builds a single page-sized, paper-coloured sheet hung off a pivot at the spine (local X = 0)
        /// so the view can sweep it across the spread to sell a page turn. The pivot is parented under
        /// <paramref name="parent"/> at the spine; the sheet itself is offset outward by half a page so
        /// it lies over a page rather than on the spine. Returned INACTIVE — the view activates it for
        /// the duration of a flip and rotates the pivot around its Z axis (the spine). The sheet carries
        /// no live UI; it only carries the motion. Matches <see cref="BuildProp"/>'s page geometry/units.
        /// </summary>
        public static Transform BuildTurningSheet(Transform parent, BookPropParams p)
        {
            var pivot = new GameObject("Book Turning Pivot").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = Vector3.zero;

            // Same thin page slab as BuildProp's pages, sat just above them so it reads over the spread.
            var pageSize = new Vector3(p.pageWidth * 0.94f, p.coverThickness * 0.5f, p.pageDepth * 0.92f);
            CreateCoverCube(
                pivot, "Book Turning Sheet", p.pageColor, pageSize,
                new Vector3(p.pageWidth * 0.5f, p.coverThickness * 1.1f, 0f));

            pivot.gameObject.SetActive(false);
            return pivot;
        }

        static Transform CreateCoverCube(Transform parent, string name, Color color, Vector3 size, Vector3 localPosition)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = size;

            var collider = cube.GetComponent<Collider>();

            if (collider != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }

            var renderer = cube.GetComponent<Renderer>();

            if (renderer != null)
                renderer.sharedMaterial = CreateOpaqueMaterial($"{name} Material", color);

            return cube.transform;
        }

        static Material CreateOpaqueMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(shader) { name = materialName, color = color };
        }
    }
}
