using Meniscus.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Meniscus.Editor
{
    /// <summary>
    /// One-shot wiring for the desk item models. Run Tools ▸ Meniscus ▸ Wire Item Models once and it binds
    /// the existing Spyglass / Bandana FBXs (and their painted materials) into the
    /// <see cref="GameManager"/>'s <c>ItemModelLibrary</c> as real serialized references — so the spyglass
    /// shows on Bartender's Spectacles and the bandana on Step Outside, with nothing placed in Resources and
    /// nothing extra dragged by hand. Idempotent: re-running just refreshes the two entries. Can also be run
    /// headlessly via <c>-executeMethod Meniscus.Editor.ItemModelWiring.WireItemModels</c>.
    /// </summary>
    public static class ItemModelWiring
    {
        // Asset GUIDs (from the .fbx.meta / .mat.meta files). GUIDs survive moves/renames, so this keeps
        // working even if the art is reorganised.
        const string SpyglassFbxGuid = "8d6e7694f7ea6494fa911a8842955029";
        const string BandanaFbxGuid = "1e6bd347bb7b444789c7d56712034dc3";
        const string SpyglassMatGuid = "9b91b9e9bc45041ad916b749a2867e9f";
        const string BandanaMatGuid = "f6f6cfa1dfe7e41718fe48317c7b7220";

        // item id (matches ShopCatalog) -> the model/material it should show on the desk.
        const string SpectaclesItemId = "bartenders_spectacles";
        const string StepOutsideItemId = "step_outside";

        [MenuItem("Tools/Meniscus/Wire Item Models")]
        public static void WireItemModels()
        {
            var spyglass = Load<GameObject>(SpyglassFbxGuid, "Spyglass.fbx");
            var bandana = Load<GameObject>(BandanaFbxGuid, "Bandana.fbx");
            var spyglassMat = Load<Material>(SpyglassMatGuid, "Spyglass.mat");
            var bandanaMat = Load<Material>(BandanaMatGuid, "Bandana.mat");

            if (spyglass == null || bandana == null)
                return; // Load already logged what was missing.

            var gameManager = FindGameManager();

            if (gameManager == null)
            {
                Debug.LogError(
                    "[Meniscus] Wire Item Models: no GameManager found. Open Assets/Scenes/Saloon.unity " +
                    "and run Tools ▸ Meniscus ▸ Wire Item Models again.");
                return;
            }

            var serialized = new SerializedObject(gameManager);
            var library = serialized.FindProperty("itemModels");

            if (library == null)
            {
                Debug.LogError(
                    "[Meniscus] Wire Item Models: GameManager has no 'itemModels' field. Let scripts " +
                    "recompile, then try again.");
                return;
            }

            var entries = library.FindPropertyRelative("entries");
            UpsertEntry(entries, SpectaclesItemId, spyglass, spyglassMat);
            UpsertEntry(entries, StepOutsideItemId, bandana, bandanaMat);
            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(gameManager);
            var scene = gameManager.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                $"[Meniscus] Wired item models on '{gameManager.name}' in scene '{scene.name}' and saved: " +
                $"{SpectaclesItemId} → Spyglass, {StepOutsideItemId} → Bandana.");
        }

        // GameManager in any currently-open scene. Kept deliberately simple: rather than auto-opening
        // scenes (and prompting to save unsaved work), the caller asks you to open Saloon.unity if none
        // is loaded — you will have it open when wiring anyway.
        static GameManager FindGameManager() => Object.FindAnyObjectByType<GameManager>();

        // Updates the entry for itemId in place, or appends one if absent, so re-running never duplicates
        // and never clobbers entries for other items.
        static void UpsertEntry(SerializedProperty entries, string itemId, GameObject model, Material material)
        {
            SerializedProperty target = null;

            for (var i = 0; i < entries.arraySize; i++)
            {
                var element = entries.GetArrayElementAtIndex(i);

                if (element.FindPropertyRelative("itemId").stringValue == itemId)
                {
                    target = element;
                    break;
                }
            }

            if (target == null)
            {
                entries.arraySize++;
                target = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                target.FindPropertyRelative("itemId").stringValue = itemId;
            }

            target.FindPropertyRelative("model").objectReferenceValue = model;
            target.FindPropertyRelative("material").objectReferenceValue = material;
        }

        static T Load<T>(string guid, string label) where T : Object
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[Meniscus] Wire Item Models: could not locate {label} (guid {guid}).");
                return null;
            }

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset == null)
                Debug.LogError(
                    $"[Meniscus] Wire Item Models: found {label} at '{path}' but could not load it as " +
                    $"{typeof(T).Name}.");

            return asset;
        }
    }
}
