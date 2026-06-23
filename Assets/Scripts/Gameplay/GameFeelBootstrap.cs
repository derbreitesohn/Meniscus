using Meniscus.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Installs the feel layer at runtime so it needs no scene or Inspector setup: when a gameplay
    /// scene loads (one that has a <see cref="GlassManager"/> to read danger from), a single "Game Feel"
    /// object is created carrying the <see cref="GameFeelDirector"/> and <see cref="DangerVignette"/>.
    /// Guarded so it never duplicates components a scene already provides.
    /// </summary>
    public static class GameFeelBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // RuntimeInitializeOnLoadMethod fires once per launch, so an AfterSceneLoad install misses a
            // gameplay scene reached later via the menu (LoadScene). Install on every scene load instead;
            // the guards below keep it to gameplay scenes and avoid duplicates.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            // Only act in a gameplay scene, and only when the feel layer isn't already present.
            if (Object.FindAnyObjectByType<GlassManager>() == null)
                return;

            var host = ResolveHost();
            EnsureComponent<GameFeelDirector>(host);
            EnsureComponent<DangerVignette>(host);
        }

        static GameObject ResolveHost()
        {
            var existingDirector = Object.FindAnyObjectByType<GameFeelDirector>();
            if (existingDirector != null)
                return existingDirector.gameObject;

            var existingVignette = Object.FindAnyObjectByType<DangerVignette>();
            if (existingVignette != null)
                return existingVignette.gameObject;

            return new GameObject("Game Feel");
        }

        static void EnsureComponent<T>(GameObject host) where T : Component
        {
            if (Object.FindAnyObjectByType<T>() == null)
                host.AddComponent<T>();
        }
    }
}
