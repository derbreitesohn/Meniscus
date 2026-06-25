using System.Collections.Generic;
using Meniscus.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Meniscus.Editor
{
    /// <summary>
    /// One-shot wiring for the desk item models. Run Tools ▸ Meniscus ▸ Wire Item Models once and it binds
    /// a model into every catalog item's row of the <see cref="GameManager"/>'s <c>ItemModelLibrary</c> as
    /// real serialized references — the spyglass on "Spyglass", the bandana on "Bandana", the treats
    /// (Leckerlis) on "Taro", a coin on the coin-themed items and a single clean glass on the drink items — with nothing
    /// in Resources and nothing dragged by hand. Items that have their own object get a dedicated model;
    /// the rest reuse a coin or glass so nothing is left showing the placeholder box. Idempotent: re-running
    /// refreshes each entry in place, never duplicates rows, and leaves any baked icon untouched. Can also
    /// be run headlessly via <c>-executeMethod Meniscus.Editor.ItemModelWiring.WireItemModels</c>.
    /// </summary>
    public static class ItemModelWiring
    {
        // Asset GUIDs (from the .fbx.meta / .mat.meta files). GUIDs survive moves/renames, so this keeps
        // working even if the art is reorganised.
        const string SpyglassFbxGuid = "8d6e7694f7ea6494fa911a8842955029";
        const string BandanaFbxGuid = "1e6bd347bb7b444789c7d56712034dc3";
        const string LeckerlisFbxGuid = "d8989f0d05b09466096d3c4f6031bc53";
        const string PfeifiFbxGuid = "946f48768b3134517960fe6b5221de38";
        const string SpyglassMatGuid = "9b91b9e9bc45041ad916b749a2867e9f";
        const string BandanaMatGuid = "f6f6cfa1dfe7e41718fe48317c7b7220";
        const string LeckerlisMatGuid = "cd533f3aa04ff40af83f2a5c896f071e";

        // The three coin FBXs (also used by the in-play CoinModelLibrary) and the glass sheet the desk
        // glass-prop is extracted from.
        const string SmallCoinFbxGuid = "8e28eec41df33c34e85e75dee8245bc7";
        const string MediumCoinFbxGuid = "d3c007945f46fe74a93b2ee2d1b825bf";
        const string BigCoinFbxGuid = "4e32d91ea09d0b940990447b018ea67d";
        const string GlasTypesFbxGuid = "359d88fc9e1aa95479ad9455774a4c76";
        const string BrownGlassMatGuid = "dea9e2be6b3f04e638e6b57141a3e4dc";

        // The one glass shape (of several in the sheet) the play glass uses; picked by its stable local
        // file id so the extracted prop matches the glass the player already sees.
        const long GlassMeshFileId = 7765577175999339204L;
        const string GlassItemPrefabPath = "Assets/Art/Models/Props/Glass_Item.prefab";

        [MenuItem("Tools/Meniscus/Wire Item Models")]
        public static void WireItemModels()
        {
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
            var wired = 0;

            foreach (var binding in Bindings())
            {
                if (binding.Model == null)
                    continue; // Load / EnsureGlassItemPrefab already logged what was missing.

                UpsertEntry(entries, binding.ItemId, binding.Model, binding.Material);
                wired++;
            }

            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(gameManager);
            var scene = gameManager.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                $"[Meniscus] Wired {wired} item model(s) on '{gameManager.name}' in scene '{scene.name}' and " +
                "saved. Spyglass/Bandana/Taro show their own prop, coin items a coin, drink items a glass. " +
                "Re-run after adding a catalog item, then bake thumbnails (Tools ▸ Meniscus ▸ Bake Item Thumbnails).");
        }

        readonly struct Binding
        {
            public readonly string ItemId;
            public readonly GameObject Model;
            public readonly Material Material;

            public Binding(string itemId, GameObject model, Material material)
            {
                ItemId = itemId;
                Model = model;
                Material = material;
            }
        }

        // Every catalog item paired with the art it should show. Shared FBXs are loaded once and reused
        // across items that share a look (coins, glass). The coins and glass take no material override —
        // they carry their own painted/glass materials and forcing one would flatten them.
        static IEnumerable<Binding> Bindings()
        {
            var spyglass = Load<GameObject>(SpyglassFbxGuid, "Spyglass.fbx");
            var bandana = Load<GameObject>(BandanaFbxGuid, "Bandana.fbx");
            var leckerlis = Load<GameObject>(LeckerlisFbxGuid, "Leckerlis.fbx");
            var pfeifi = Load<GameObject>(PfeifiFbxGuid, "Pfeifi.fbx");
            var smallCoin = Load<GameObject>(SmallCoinFbxGuid, "Small_coin.fbx");
            var mediumCoin = Load<GameObject>(MediumCoinFbxGuid, "Medium_coin.fbx");
            var bigCoin = Load<GameObject>(BigCoinFbxGuid, "Big_coin.fbx");
            var glass = EnsureGlassItemPrefab();

            var spyglassMat = Load<Material>(SpyglassMatGuid, "Spyglass.mat");
            var bandanaMat = Load<Material>(BandanaMatGuid, "Bandana.mat");
            var leckerlisMat = Load<Material>(LeckerlisMatGuid, "Leckerlis.mat");

            return new[]
            {
                // Object-named items get their own prop.
                new Binding("bartenders_spectacles", spyglass, spyglassMat),  // "Spyglass"
                new Binding("step_outside", bandana, bandanaMat),             // "Bandana"
                new Binding("taro_laps", leckerlis, leckerlisMat),            // "Taro" (the treats / Leckerlis)
                new Binding("dog_whistle", pfeifi, null),                     // "Pfeifi" (the whistle)

                // Coin-themed items reuse a coin.
                new Binding("marked_coin", mediumCoin, null),
                new Binding("loaded_dice", bigCoin, null),                    // "Lucky Coin"
                new Binding("dealers_debt", smallCoin, null),

                // Drink / pour-easing items reuse the single clean glass.
                new Binding("round_for_the_dealer", glass, null),
                new Binding("steady_hand", glass, null),
            };
        }

        // Builds (once) a one-mesh glass prop from the multi-shape Glas_Types sheet so drink items show a
        // single clean glass rather than the whole sheet of shapes. Returns the existing prefab on reruns.
        static GameObject EnsureGlassItemPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(GlassItemPrefabPath);

            if (existing != null)
                return existing;

            var fbxPath = AssetDatabase.GUIDToAssetPath(GlasTypesFbxGuid);

            if (string.IsNullOrEmpty(fbxPath))
            {
                Debug.LogWarning(
                    "[Meniscus] Wire Item Models: could not locate the Glas_Types FBX; drink items keep the " +
                    "placeholder box until a glass model is wired by hand.");
                return null;
            }

            Mesh glassMesh = null;
            Mesh firstMesh = null;

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is Mesh mesh)
                {
                    firstMesh ??= mesh;

                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out _, out long localId)
                        && localId == GlassMeshFileId)
                    {
                        glassMesh = mesh;
                        break;
                    }
                }
            }

            glassMesh ??= firstMesh; // re-export safety: any glass beats none.

            if (glassMesh == null)
            {
                Debug.LogWarning($"[Meniscus] Wire Item Models: '{fbxPath}' has no meshes; no glass prop built.");
                return null;
            }

            var go = new GameObject("Glass_Item");

            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = glassMesh;
                var meshRenderer = go.AddComponent<MeshRenderer>();
                var material = Load<Material>(BrownGlassMatGuid, "Brown_Glass.mat");

                if (material != null)
                    meshRenderer.sharedMaterial = material;

                var prefab = PrefabUtility.SaveAsPrefabAsset(go, GlassItemPrefabPath);
                Debug.Log($"[Meniscus] Wire Item Models: built single-glass prop at '{GlassItemPrefabPath}'.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // GameManager in any currently-open scene. Kept deliberately simple: rather than auto-opening
        // scenes (and prompting to save unsaved work), the caller opens Saloon.unity if none is loaded.
        static GameManager FindGameManager() => Object.FindAnyObjectByType<GameManager>();

        // Updates the entry for itemId in place, or appends one if absent, so re-running never duplicates
        // and never clobbers entries for other items. Deliberately does NOT touch the icon slot, so baked
        // thumbnails survive a re-wire.
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
