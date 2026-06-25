using Meniscus.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Meniscus.Editor
{
    /// <summary>
    /// One-shot wiring for the Tschick (Steady Hand) light-up beat. Run Tools ▸ Meniscus ▸ Wire Tschick
    /// Animation once with the Saloon scene open and it binds the cigarette pack and the single-cigarette
    /// models (plus their painted materials) onto the scene's <see cref="ItemUsePresentationController"/> as
    /// real serialized references — no Resources lookup, nothing dragged by hand. Mirrors
    /// <see cref="ItemModelWiring"/>; idempotent (re-running just refreshes the four fields). Can also be run
    /// headlessly via <c>-executeMethod Meniscus.Editor.CigaretteAnimationWiring.WireTschickAnimation</c>.
    /// </summary>
    public static class CigaretteAnimationWiring
    {
        // Asset GUIDs (from the .fbx.meta / .mat.meta files). GUIDs survive moves/renames.
        const string TschickBoxFbxGuid = "5d0f44e517efd4c4cacdbd49bc52fa64";   // Tschick_box.fbx
        const string TschickBoxMatGuid = "ed13c8d46d8c4491cbbafb647cfe1a2e";   // TschickPacherl.mat
        const string SingleCigFbxGuid = "42eb851fa2b274a0abfa52c388b0933c";    // Eizelne_tschick.fbx
        const string SingleCigMatGuid = "81f441b848b354e1fb0c60836f64c47f";    // Tschick_eine.mat

        [MenuItem("Tools/Meniscus/Wire Tschick Animation")]
        public static void WireTschickAnimation()
        {
            var controller = Object.FindAnyObjectByType<ItemUsePresentationController>();

            if (controller == null)
            {
                Debug.LogError(
                    "[Meniscus] Wire Tschick Animation: no ItemUsePresentationController found. Open " +
                    "Assets/Scenes/Saloon.unity and run Tools ▸ Meniscus ▸ Wire Tschick Animation again.");
                return;
            }

            var boxModel = Load<GameObject>(TschickBoxFbxGuid, "Tschick_box.fbx");
            var boxMaterial = Load<Material>(TschickBoxMatGuid, "TschickPacherl.mat");
            var cigModel = Load<GameObject>(SingleCigFbxGuid, "Eizelne_tschick.fbx");
            var cigMaterial = Load<Material>(SingleCigMatGuid, "Tschick_eine.mat");

            if (boxModel == null || cigModel == null)
                return; // Load already logged what was missing; bail rather than half-wire.

            var serialized = new SerializedObject(controller);

            if (!Assign(serialized, "cigaretteBoxModel", boxModel) ||
                !Assign(serialized, "cigaretteModel", cigModel))
            {
                Debug.LogError(
                    "[Meniscus] Wire Tschick Animation: the controller is missing the cigarette model " +
                    "fields. Let scripts recompile, then try again.");
                return;
            }

            Assign(serialized, "cigaretteBoxMaterial", boxMaterial);
            Assign(serialized, "cigaretteMaterial", cigMaterial);

            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(controller);
            var scene = controller.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                $"[Meniscus] Wired the Tschick pack + single cigarette onto '{controller.name}' in scene " +
                $"'{scene.name}' and saved. Buy the Tschick (Steady Hand) and use it to play the light-up.");
        }

        static bool Assign(SerializedObject serialized, string fieldName, Object value)
        {
            var property = serialized.FindProperty(fieldName);

            if (property == null)
                return false;

            property.objectReferenceValue = value;
            return true;
        }

        static T Load<T>(string guid, string label) where T : Object
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[Meniscus] Wire Tschick Animation: could not locate {label} (guid {guid}).");
                return null;
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
                Debug.LogError(
                    $"[Meniscus] Wire Tschick Animation: found {label} at '{path}' but could not load it as " +
                    $"{typeof(T).Name}.");

            return asset;
        }
    }
}
