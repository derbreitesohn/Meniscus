using System.Collections.Generic;
using System.Text;
using Meniscus.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meniscus.Editor
{
    /// <summary>
    /// One-shot menu command that points the <see cref="GameManager"/>'s coin model slots at the real
    /// coin FBX roots. The slots were hand-wired in the scene YAML to <c>fileID: 100100000</c>, which for
    /// these Props FBXs resolves to a Mesh rather than the GameObject root, so <c>Instantiate</c> threw and
    /// every coin fell back to the placeholder cylinder. <see cref="AssetDatabase.LoadAssetAtPath"/> returns
    /// the GameObject root Unity actually expects, which is the only reliable way to get that reference.
    /// </summary>
    static class CoinModelWiring
    {
        const string MenuPath = "Tools/Meniscus/Wire Coin Models";

        // Slot serialized-property path (nested inside the GameManager's CoinModelLibrary field) -> FBX asset.
        static readonly (string Property, string AssetPath)[] Slots =
        {
            ("coinModels.smallCoinModel", "Assets/Art/Models/Props/Small_coin.fbx"),
            ("coinModels.mediumCoinModel", "Assets/Art/Models/Props/Medium_coin.fbx"),
            ("coinModels.largeCoinModel", "Assets/Art/Models/Props/Big_coin.fbx"),
        };

        [MenuItem(MenuPath)]
        static void WireCoinModels()
        {
            // Load every FBX root up front; abort before touching the scene if any is missing so we never
            // leave the GameManager half-wired.
            var roots = new GameObject[Slots.Length];

            for (var i = 0; i < Slots.Length; i++)
            {
                roots[i] = AssetDatabase.LoadAssetAtPath<GameObject>(Slots[i].AssetPath);

                if (roots[i] == null)
                {
                    Debug.LogError($"[CoinModelWiring] Could not load coin FBX root at '{Slots[i].AssetPath}'. " +
                                   "Aborting without changing the scene.");
                    return;
                }
            }

            var managers = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (managers.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Wire Coin Models",
                    "No GameManager is in the open scene(s).\n\nOpen Assets/Scenes/Saloon.unity first, then run " +
                    "Tools ▸ Meniscus ▸ Wire Coin Models again.",
                    "OK");
                return;
            }

            var touchedScenes = new HashSet<Scene>();
            var summary = new StringBuilder();

            foreach (var manager in managers)
            {
                var serialized = new SerializedObject(manager);

                for (var i = 0; i < Slots.Length; i++)
                {
                    var property = serialized.FindProperty(Slots[i].Property);

                    if (property == null)
                    {
                        Debug.LogError($"[CoinModelWiring] '{Slots[i].Property}' not found on {manager.name}. " +
                                       "Did the CoinModelLibrary field names change?", manager);
                        continue;
                    }

                    property.objectReferenceValue = roots[i];
                }

                serialized.ApplyModifiedProperties();

                var scene = manager.gameObject.scene;
                touchedScenes.Add(scene);
                summary.AppendLine($"  {manager.name} ({scene.name}): Small/Medium/Big coin roots assigned.");
            }

            foreach (var scene in touchedScenes)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"[CoinModelWiring] Wired coin models on {managers.Length} GameManager(s):\n{summary}" +
                      "Enter play mode to confirm coins render as models; tune modelScale if the on-table size is off.");
        }
    }
}
