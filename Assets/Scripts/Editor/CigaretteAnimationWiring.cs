using Meniscus.Core;
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
    ///
    /// At runtime <see cref="GameManager"/> spins the controller up with <c>AddComponent</c>, so it normally
    /// never lives in the scene and its serialized model fields stay null — the single cigarette then has no
    /// model and the whole draw-and-light ritual is silently skipped (only the pack, which falls back to the
    /// item-model library, pops up). To fix that this tool authors the controller onto the GameManager's
    /// GameObject as a persistent component (matching where the runtime path puts it), so the wired models
    /// survive into play mode and every pose/timing field becomes inspector-tunable.
    /// </summary>
    public static class CigaretteAnimationWiring
    {
        // Asset GUIDs (from the .fbx.meta / .mat.meta files). GUIDs survive moves/renames.
        const string TschickBoxFbxGuid = "5d0f44e517efd4c4cacdbd49bc52fa64";   // Tschick_box.fbx
        const string TschickBoxMatGuid = "ed13c8d46d8c4491cbbafb647cfe1a2e";   // TschickPacherl.mat
        const string SingleCigFbxGuid = "42eb851fa2b274a0abfa52c388b0933c";    // Eizelne_tschick.fbx
        const string SingleCigMatGuid = "81f441b848b354e1fb0c60836f64c47f";    // Tschick_eine.mat
        const string SaloonSceneGuid = "99c9720ab356a0642a771bea13969a05";     // Saloon.unity

        // The lit end is the outer tip, not the lips end; and the held cigarette points away and up — filter
        // end down near the camera, burning tip leading into the table and a touch higher. Re-applied here (not
        // just left to code defaults) because a controller already in the scene keeps its old serialized
        // values — defaults only bite a fresh add. (Y yaw = swing into/out of screen, Z roll = up/down tilt.)
        static readonly bool LitTipIsFlipped = true;
        static readonly Vector3 CigMouthEuler = new(0f, 40f, 15f);
        static readonly Vector3 CigMouthLocalPos = new(0.02f, -0.13f, 0.36f);

        [MenuItem("Tools/Meniscus/Wire Tschick Animation")]
        public static void WireTschickAnimation()
        {
            var controller = Object.FindAnyObjectByType<ItemUsePresentationController>();

            // Headless (-executeMethod) starts with no scene loaded, so open Saloon first. Guarded to batch
            // mode so an interactive run never blows away whatever scene the user has open.
            if (controller == null && Application.isBatchMode)
            {
                var scenePath = AssetDatabase.GUIDToAssetPath(SaloonSceneGuid);

                if (!string.IsNullOrEmpty(scenePath))
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    controller = Object.FindAnyObjectByType<ItemUsePresentationController>();
                }
            }

            // The controller is created at runtime via AddComponent, so it's not in the scene at edit time.
            // Author it onto the GameManager's GameObject (where the runtime path puts it) so the wiring below
            // actually persists; OnEnable self-resolves its references and subscribes to ItemUsed, so it runs
            // without the GameManager's Configure() call.
            if (controller == null)
            {
                var gameManager = Object.FindAnyObjectByType<GameManager>();

                if (gameManager == null)
                {
                    Debug.LogError(
                        "[Meniscus] Wire Tschick Animation: no GameManager in the open scene to host the " +
                        "ItemUsePresentationController. Open Assets/Scenes/Saloon.unity and run again.");
                    return;
                }

                controller = Undo.AddComponent<ItemUsePresentationController>(gameManager.gameObject);
                Debug.Log(
                    $"[Meniscus] Wire Tschick Animation: added an ItemUsePresentationController to " +
                    $"'{gameManager.name}' (it was runtime-only before, which is why the cigarette never drew).");
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

            // Light the outer tip and stand the cigarette off toward the dealer.
            AssignBool(serialized, "flipCigaretteTip", LitTipIsFlipped);
            AssignVector3(serialized, "cigMouthEuler", CigMouthEuler);
            AssignVector3(serialized, "cigMouthLocalPos", CigMouthLocalPos);

            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(controller);
            var scene = controller.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                $"[Meniscus] Wired the Tschick pack + single cigarette onto '{controller.name}' in scene " +
                $"'{scene.name}', lit the outer tip, angled it at the dealer, and saved. Buy the Tschick " +
                $"(Steady Hand) and use it to play the light-up.");
        }

        static bool Assign(SerializedObject serialized, string fieldName, Object value)
        {
            var property = serialized.FindProperty(fieldName);

            if (property == null)
                return false;

            property.objectReferenceValue = value;
            return true;
        }

        static void AssignBool(SerializedObject serialized, string fieldName, bool value)
        {
            var property = serialized.FindProperty(fieldName);

            if (property != null)
                property.boolValue = value;
        }

        static void AssignVector3(SerializedObject serialized, string fieldName, Vector3 value)
        {
            var property = serialized.FindProperty(fieldName);

            if (property != null)
                property.vector3Value = value;
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
