using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.Items;
using Meniscus.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meniscus.Editor
{
    /// <summary>
    /// Authors the objects that GlassVisualController/GameManager/ShopManager otherwise build at
    /// runtime as persistent scene objects/prefabs, wiring each controller's serialized reference.
    /// Idempotent: resolves an existing object (by serialized ref, then by name) before creating.
    /// Mirrors CoinSceneSetup's conventions (open scene, SerializedObject wiring, Undo, save).
    /// </summary>
    static class RuntimeObjectAuthoring
    {
        const string ScenePath = "Assets/Scenes/Saloon.unity";
        const string WaterMaterialPath = "Assets/Art/Materials/Water_Surface.mat";
        const string PrefabsDir = "Assets/Prefabs";
        const string SpillPrefabPath = "Assets/Prefabs/SpillEffect.prefab";
        const string MenuRoot = "Tools/Meniscus/Author Runtime Objects/";

        [MenuItem(MenuRoot + "Author Glass Water Surface")]
        static void AuthorWaterSurfaceMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorWaterSurface())
                SaveScene(scene);
        }

        public static bool AuthorWaterSurface()
        {
            var glass = Object.FindAnyObjectByType<GlassVisualController>();

            if (glass == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GlassVisualController in the scene. Aborting.");
                return false;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);

            if (material == null)
            {
                Debug.LogError($"[RuntimeObjectAuthoring] Missing '{WaterMaterialPath}'. Aborting without changes.");
                return false;
            }

            var serialized = new SerializedObject(glass);
            var waterProp = serialized.FindProperty("waterTransform");
            var materialProp = serialized.FindProperty("liquidMaterial");

            var water = waterProp.objectReferenceValue as Transform;

            if (water == null)
                water = glass.transform.Find(WaterSurfaceBuilder.AuthoredName);

            if (water == null)
            {
                water = WaterSurfaceBuilder.Build(glass.transform, WaterSurfaceBuilder.AuthoredName);
                Undo.RegisterCreatedObjectUndo(water.gameObject, "Author Water Surface");
            }

            waterProp.objectReferenceValue = water;
            materialProp.objectReferenceValue = material;
            serialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] Water surface authored ('Liquid') and wired (waterTransform + Water_Surface.mat).");
            return true;
        }

        [MenuItem(MenuRoot + "Author Player Inventory")]
        static void AuthorPlayerInventoryMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorPlayerInventory())
                SaveScene(scene);
        }

        public static bool AuthorPlayerInventory()
        {
            var manager = Object.FindAnyObjectByType<GameManager>();

            if (manager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GameManager in the scene. Aborting.");
                return false;
            }

            // The managers all live on the GameManager's GameObject (same object GameManager.AddComponent uses).
            var inventory = Object.FindAnyObjectByType<PlayerInventory>();

            if (inventory == null)
            {
                Undo.RegisterCompleteObjectUndo(manager.gameObject, "Author Player Inventory");
                inventory = manager.gameObject.AddComponent<PlayerInventory>();
            }

            var serialized = new SerializedObject(manager);
            serialized.FindProperty("playerInventory").objectReferenceValue = inventory;
            serialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] PlayerInventory authored on the Managers object and wired.");
            return true;
        }

        [MenuItem(MenuRoot + "Author Desk Item Tray")]
        static void AuthorDeskItemTrayMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorDeskItemTray())
                SaveScene(scene);
        }

        public static bool AuthorDeskItemTray()
        {
            var manager = Object.FindAnyObjectByType<GameManager>();

            if (manager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GameManager in the scene. Aborting.");
                return false;
            }

            // The retired DeskItemBar serializes onto the Managers object as a missing script once its
            // class is deleted; purge it so re-authoring leaves a clean component set.
            var removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(manager.gameObject);

            // The tray lives on its OWN positionable object (like the Book Shop), so dragging it in the
            // scene moves only the desk boxes — not the manager stack. A tray already authored onto the
            // Managers object (legacy) is left alone; we only create a standalone one when none exists.
            var tray = Object.FindAnyObjectByType<DeskItemTray>();

            if (tray == null)
            {
                var go = new GameObject("Desk Item Tray");
                Undo.RegisterCreatedObjectUndo(go, "Author Desk Item Tray");
                tray = go.AddComponent<DeskItemTray>();
                PlaceOnDeskFront(go.transform);
            }

            var inventory = Object.FindAnyObjectByType<PlayerInventory>();

            var traySerialized = new SerializedObject(tray);
            traySerialized.FindProperty("gameManager").objectReferenceValue = manager;
            traySerialized.FindProperty("inventory").objectReferenceValue = inventory;
            traySerialized.ApplyModifiedProperties();

            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("deskItemTray").objectReferenceValue = tray;
            managerSerialized.ApplyModifiedProperties();

            Debug.Log($"[RuntimeObjectAuthoring] DeskItemTray authored on a standalone 'Desk Item Tray' object " +
                      $"and wired (removed {removed} missing script(s)). Drag it (or set deskItemsAnchor) to place the boxes.");
            return true;
        }

        // Seats a freshly-authored object on the desk surface toward the player, clear of the glass, so it
        // starts somewhere sensible the designer can then nudge. The desk surface height is taken from the
        // glass's base (it rests on the desk) rather than the table's bounds — the table's bounds enclose
        // its child props (the tall glass), so their top is up at the rim, not the surface.
        static void PlaceOnDeskFront(Transform t)
        {
            var glass = GameObject.FindWithTag("Glass") ?? Object.FindAnyObjectByType<GlassVisualController>()?.gameObject;
            var cam = Camera.main;

            if (glass == null || cam == null)
                return;

            var renderers = glass.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return;

            var glassBounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                glassBounds.Encapsulate(renderers[i].bounds);

            var deskSurfaceY = glassBounds.min.y;             // glass base sits on the desk
            var toCamera = Vector3.ProjectOnPlane(cam.transform.position - glassBounds.center, Vector3.up);
            toCamera = toCamera.sqrMagnitude > 1e-4f ? toCamera.normalized : -Vector3.forward;
            var right = Vector3.Cross(Vector3.up, toCamera).normalized;

            // In front of the glass (toward the player) and off to one side, on the desk surface.
            var pos = new Vector3(glassBounds.center.x, deskSurfaceY, glassBounds.center.z)
                      + toCamera * (glassBounds.size.magnitude * 0.6f)
                      + right * (glassBounds.size.x * 0.6f);

            t.SetPositionAndRotation(pos, Quaternion.LookRotation(toCamera, Vector3.up));
        }

        [MenuItem(MenuRoot + "Author Book Shop")]
        static void AuthorBookShopMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorBookShop())
                SaveScene(scene);
        }

        public static bool AuthorBookShop()
        {
            var shopManager = Object.FindAnyObjectByType<ShopManager>();

            if (shopManager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No ShopManager in the scene. Aborting.");
                return false;
            }

            var view = Object.FindAnyObjectByType<BookShopView>();

            if (view == null)
            {
                var go = new GameObject("Book Shop");
                Undo.RegisterCreatedObjectUndo(go, "Author Book Shop");
                view = go.AddComponent<BookShopView>();
            }

            var viewSerialized = new SerializedObject(view);
            var authoredProp = viewSerialized.FindProperty("authoredBook");

            var prop = authoredProp.objectReferenceValue as Transform;

            if (prop == null)
                prop = view.transform.Find("Diegetic Book Shop");

            if (prop == null)
            {
                prop = BookShopBuilder.BuildProp(view.transform, new BookShopBuilder.BookPropParams
                {
                    pageWidth = 0.30f,
                    pageDepth = 0.38f,
                    coverThickness = 0.02f,
                    coverColor = new Color(0.34f, 0.16f, 0.08f),
                    pageColor = new Color(0.86f, 0.78f, 0.6f),
                });
                Undo.RegisterCreatedObjectUndo(prop.gameObject, "Author Book Prop");
            }

            authoredProp.objectReferenceValue = prop;
            viewSerialized.ApplyModifiedProperties();

            var shopSerialized = new SerializedObject(shopManager);
            shopSerialized.FindProperty("bookShop").objectReferenceValue = view;
            shopSerialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] Book Shop authored (prop) and wired (BookShopView.authoredBook + ShopManager.bookShop). " +
                      "Position the 'Book Shop' object / set its deskAnchor in the Inspector.");
            return true;
        }

        [MenuItem(MenuRoot + "Author Spill Effect Prefab")]
        static void AuthorSpillPrefabMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorSpillPrefab())
                SaveScene(scene);
        }

        public static bool AuthorSpillPrefab()
        {
            var glass = Object.FindAnyObjectByType<GlassVisualController>();

            if (glass == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GlassVisualController in the scene. Aborting.");
                return false;
            }

            if (!AssetDatabase.IsValidFolder(PrefabsDir))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(SpillPrefabPath);

            if (existing == null)
            {
                // Build a transient host carrying the component + authored material, save it as a prefab, discard the host.
                var temp = new GameObject("SpillEffect");
                temp.AddComponent<GlassSpillEffect>();

                var material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);

                if (material != null)
                {
                    var tempSerialized = new SerializedObject(temp.GetComponent<GlassSpillEffect>());
                    tempSerialized.FindProperty("overspillMaterial").objectReferenceValue = material;
                    tempSerialized.ApplyModifiedProperties();
                }

                existing = PrefabUtility.SaveAsPrefabAsset(temp, SpillPrefabPath);
                Object.DestroyImmediate(temp);
            }

            var glassSerialized = new SerializedObject(glass);
            glassSerialized.FindProperty("spillPrefab").objectReferenceValue = existing.GetComponent<GlassSpillEffect>();
            glassSerialized.ApplyModifiedProperties();

            Debug.Log($"[RuntimeObjectAuthoring] Spill prefab authored at '{SpillPrefabPath}' and wired to GlassVisualController.spillPrefab.");
            return true;
        }

        [MenuItem(MenuRoot + "Author All")]
        static void AuthorAllMenu() => Run();

        /// <summary>
        /// Headless: Unity -batchmode -executeMethod Meniscus.Editor.RuntimeObjectAuthoring.Run -quit
        /// Authors all targets in dependency order (PlayerInventory before the DeskItemTray that references it).
        /// </summary>
        public static void Run()
        {
            if (!TryOpenScene(out var scene))
                return;

            var changed = false;
            changed |= AuthorWaterSurface();
            changed |= AuthorPlayerInventory();
            changed |= AuthorDeskItemTray();
            changed |= AuthorBookShop();
            changed |= AuthorSpillPrefab();

            if (changed)
                SaveScene(scene);

            Debug.Log("[RuntimeObjectAuthoring] Author All complete.");
        }

        static bool TryOpenScene(out Scene scene)
        {
            var active = EditorSceneManager.GetActiveScene();
            scene = active.IsValid() && active.path == ScenePath
                ? active
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (!scene.IsValid())
            {
                Debug.LogError($"[RuntimeObjectAuthoring] Could not open '{ScenePath}'. Aborting.");
                return false;
            }

            return true;
        }

        static void SaveScene(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
