using Meniscus.Gameplay;
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
