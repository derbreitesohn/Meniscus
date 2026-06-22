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

        [MenuItem(MenuRoot + "Author Desk Item Bar")]
        static void AuthorDeskItemBarMenu()
        {
            if (!TryOpenScene(out var scene))
                return;

            if (AuthorDeskItemBar())
                SaveScene(scene);
        }

        public static bool AuthorDeskItemBar()
        {
            var manager = Object.FindAnyObjectByType<GameManager>();

            if (manager == null)
            {
                Debug.LogError("[RuntimeObjectAuthoring] No GameManager in the scene. Aborting.");
                return false;
            }

            var bar = Object.FindAnyObjectByType<DeskItemBar>();

            if (bar == null)
            {
                Undo.RegisterCompleteObjectUndo(manager.gameObject, "Author Desk Item Bar");
                bar = manager.gameObject.AddComponent<DeskItemBar>();
            }

            // Author the canvas + row container if not already wired.
            var barSerialized = new SerializedObject(bar);
            var canvasProp = barSerialized.FindProperty("barCanvas");
            var rowRootProp = barSerialized.FindProperty("rowRoot");

            if (canvasProp.objectReferenceValue == null || rowRootProp.objectReferenceValue == null)
            {
                var (canvas, rowRoot) = DeskItemBarBuilder.Build(bar.transform);
                Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Author Desk Item Bar Canvas");
                canvasProp.objectReferenceValue = canvas;
                rowRootProp.objectReferenceValue = rowRoot;
            }

            barSerialized.FindProperty("gameManager").objectReferenceValue = manager;
            barSerialized.ApplyModifiedProperties();

            var managerSerialized = new SerializedObject(manager);
            managerSerialized.FindProperty("deskItemBar").objectReferenceValue = bar;
            managerSerialized.ApplyModifiedProperties();

            Debug.Log("[RuntimeObjectAuthoring] DeskItemBar authored (component + canvas + rowRoot) and wired.");
            return true;
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
