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

            // The book is two cover boards hinged at the spine, each with a page on top: a fixed LEFT leaf and
            // a RIGHT leaf that folds about the spine. Closed, the right leaf folds flat on top of the left so
            // its cover board faces up (a shut book); open, it lies level with the left to form the double-page
            // spread the menu prints across. The cover IS the folding structure — there is no separate cover.
            var halfPage = p.pageWidth * 0.5f;
            var ct = p.coverThickness;
            var pageThk = ct * 0.5f;
            var boardCenterY = ct * 0.5f;          // cover board rests on the root plane
            var pageCenterY = ct + pageThk * 0.5f; // page sits on top of its board
            var hingeY = ct + pageThk;             // = left leaf top, so the folded right leaf stacks cleanly
            var boardSize = new Vector3(p.pageWidth, ct, p.pageDepth);
            var pageSize = new Vector3(p.pageWidth * 0.96f, pageThk, p.pageDepth * 0.96f);

            // Left leaf (fixed).
            CreateCoverCube(root, "Book Back Cover Left", p.coverColor, boardSize, new Vector3(-halfPage, boardCenterY, 0f));
            CreateCoverCube(root, "Book Left Page", p.pageColor, pageSize, new Vector3(-halfPage, pageCenterY, 0f));

            // Right leaf folds about the spine (see BookShopView: closed = openAngle, open = 0°).
            var hingePivot = new GameObject("Book Hinge").transform;
            hingePivot.SetParent(root, false);
            hingePivot.localPosition = new Vector3(0f, hingeY, 0f);

            CreateCoverCube(hingePivot, "Book Back Cover Right", p.coverColor, boardSize, new Vector3(halfPage, boardCenterY - hingeY, 0f));
            CreateCoverCube(hingePivot, "Book Right Page", p.pageColor, pageSize, new Vector3(halfPage, pageCenterY - hingeY, 0f));

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
