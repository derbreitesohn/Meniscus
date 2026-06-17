using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Meniscus.Tests.EditMode
{
    public class InputSystemConfigurationTests
    {
        const string SaloonScenePath = "Assets/Scenes/Saloon.unity";
        const string PlayerControllerPath = "Assets/Scripts/Gameplay/PlayerController.cs";

        [Test]
        public void SaloonScene_UsesInputSystemUiModuleInsteadOfStandaloneInputModule()
        {
            var scene = EditorSceneManager.OpenScene(SaloonScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), $"Could not open {SaloonScenePath}.");

            var eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            Assert.IsNotNull(eventSystem, "Saloon scene should have an EventSystem for shop buttons.");
            Assert.IsNull(eventSystem.GetComponent<StandaloneInputModule>(), "StandaloneInputModule uses legacy UnityEngine.Input and must not be present.");
            Assert.IsNotNull(eventSystem.GetComponent<InputSystemUIInputModule>(), "InputSystemUIInputModule is required when active input handling is Input System.");
        }

        [Test]
        public void PlayerController_DoesNotReadLegacyUnityEngineInput()
        {
            var source = File.ReadAllText(PlayerControllerPath);

            Assert.False(source.Contains("Input.GetMouseButtonDown"), "PlayerController must not use legacy Input.GetMouseButtonDown.");
            Assert.False(source.Contains("Input.mousePosition"), "PlayerController must not use legacy Input.mousePosition.");
        }
    }
}
