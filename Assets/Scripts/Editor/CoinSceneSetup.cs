using System.Collections.Generic;
using System.Linq;
using System.Text;
using Meniscus.Core;
using Meniscus.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meniscus.Editor
{
    /// <summary>
    /// One-shot setup that wires the <see cref="GameManager"/>'s coin references to the coins already authored
    /// in the scene. The Saloon scene shipped with <c>handAuthoredPlayerCoins</c> as empty slots and no spawn
    /// roots assigned, so the player's authored coins were never pooled and the game fell back to spawning
    /// stray runtime cylinders. This points the player coin list at the real coins and assigns each actor's
    /// spawn root to the container the authored coins live under, so runtime top-up coins land on the table.
    /// </summary>
    static class CoinSceneSetup
    {
        const string MenuPath = "Tools/Meniscus/Set Up Coin References";
        const string ScenePath = "Assets/Scenes/Saloon.unity";

        [MenuItem(MenuPath)]
        static void RunFromMenu() => Run();

        /// <summary>
        /// Bakes the per-size coin models into the scene so the coins show as models in the editor instead of
        /// placeholder cylinders. Runtime ApplyModel re-applies the round's rolled size and clears these, so
        /// this is purely an authoring-time preview. Run "Clear Coin Model Previews" to revert to placeholders.
        /// </summary>
        [MenuItem("Tools/Meniscus/Preview Coin Models In Editor")]
        static void PreviewCoinModels()
        {
            if (!TryGetSetup(out var scene, out var manager))
                return;

            var coins = Object.FindObjectsByType<Coin>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var coin in coins)
            {
                if (coin == null)
                    continue;

                Undo.RegisterFullObjectHierarchyUndo(coin.gameObject, "Preview Coin Models");
                coin.ApplyModel(manager.CoinModels.GetModelForSize(coin.size), manager.CoinModels.ModelScale);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CoinSceneSetup] Baked model previews onto {coins.Length} coins.");
        }

        [MenuItem("Tools/Meniscus/Clear Coin Model Previews")]
        static void ClearCoinModelPreviews()
        {
            if (!TryGetSetup(out var scene, out _))
                return;

            var coins = Object.FindObjectsByType<Coin>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var coin in coins)
            {
                if (coin == null)
                    continue;

                Undo.RegisterFullObjectHierarchyUndo(coin.gameObject, "Clear Coin Model Previews");
                coin.ApplyModel(null, Vector3.one);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CoinSceneSetup] Cleared model previews from {coins.Length} coins.");
        }

        static bool TryGetSetup(out UnityEngine.SceneManagement.Scene scene, out GameManager manager)
        {
            scene = EnsureSaloonSceneOpen();
            manager = null;

            if (!scene.IsValid())
            {
                Debug.LogError($"[CoinSceneSetup] Could not open '{ScenePath}'.");
                return false;
            }

            manager = Object.FindAnyObjectByType<GameManager>();

            if (manager == null)
            {
                Debug.LogError("[CoinSceneSetup] No GameManager found in the Saloon scene.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Headless entry point: Unity -batchmode -executeMethod Meniscus.Editor.CoinSceneSetup.Run -quit
        /// </summary>
        public static void Run()
        {
            var scene = EnsureSaloonSceneOpen();

            if (!scene.IsValid())
            {
                Debug.LogError($"[CoinSceneSetup] Could not open '{ScenePath}'. Aborting.");
                return;
            }

            var managers = Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (managers.Length == 0)
            {
                Debug.LogError("[CoinSceneSetup] No GameManager found in the Saloon scene. Aborting.");
                return;
            }

            var allCoins = Object.FindObjectsByType<Coin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var playerCoins = allCoins.Where(c => c != null && c.isPlayerCoin).OrderBy(c => c.name).ToList();
            var enemyCoins = allCoins.Where(c => c != null && !c.isPlayerCoin).OrderBy(c => c.name).ToList();

            // Refuse to touch the scene if no coins are present. Without this guard the tool would clear the
            // coin lists and save an empty scene — turning a transient "coins not loaded" state into permanent
            // data loss.
            if (playerCoins.Count == 0)
            {
                Debug.LogError(
                    "[CoinSceneSetup] No player coins found in the scene. Aborting WITHOUT saving so nothing is " +
                    "overwritten. Open Saloon.unity and confirm the coins exist before running this again.");
                return;
            }

            // Bake a flat box click-collider onto every coin so the authored scene data matches what the
            // runtime enforces (the authored capsule colliders collapse into oversized spheres and mis-select).
            foreach (var coin in allCoins)
            {
                if (coin == null)
                    continue;

                Undo.RegisterFullObjectHierarchyUndo(coin.gameObject, "Set Up Coin References");
                coin.EnsureClickCollider();
            }

            var summary = new StringBuilder();

            foreach (var manager in managers)
            {
                var serialized = new SerializedObject(manager);

                WireCoinList(serialized, "handAuthoredPlayerCoins", playerCoins);
                AssignSpawnRoot(serialized, "playerCoinSpawnRoot", playerCoins);
                AssignSpawnRoot(serialized, "enemyCoinSpawnRoot", enemyCoins);

                serialized.ApplyModifiedProperties();
                summary.AppendLine(
                    $"  {manager.name}: {playerCoins.Count} player coins wired; spawn roots assigned to coin containers.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[CoinSceneSetup] Saloon coin references set up:\n{summary}");
        }

        static Scene EnsureSaloonSceneOpen()
        {
            var active = EditorSceneManager.GetActiveScene();

            if (active.IsValid() && active.path == ScenePath)
                return active;

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void WireCoinList(SerializedObject serialized, string propertyName, IReadOnlyList<Coin> coins)
        {
            var listProperty = serialized.FindProperty(propertyName);

            if (listProperty == null)
            {
                Debug.LogError($"[CoinSceneSetup] '{propertyName}' not found on {serialized.targetObject.name}.");
                return;
            }

            listProperty.ClearArray();
            listProperty.arraySize = coins.Count;

            for (var i = 0; i < coins.Count; i++)
                listProperty.GetArrayElementAtIndex(i).objectReferenceValue = coins[i];
        }

        static void AssignSpawnRoot(SerializedObject serialized, string propertyName, IReadOnlyList<Coin> coins)
        {
            var property = serialized.FindProperty(propertyName);

            if (property == null)
            {
                Debug.LogError($"[CoinSceneSetup] '{propertyName}' not found on {serialized.targetObject.name}.");
                return;
            }

            // The authored coins of an actor all share one container; use it as the spawn root so runtime
            // top-up coins inherit the same table-local origin. (coins is pre-filtered of nulls by the caller.)
            property.objectReferenceValue = coins.Count > 0 ? coins[0].transform.parent : null;
        }
    }
}
