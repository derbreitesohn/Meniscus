using Meniscus.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Drives the main menu scene. Builds its UI at runtime (matching the rest of the game's
    /// procedurally-built fallback UI) so the scene only needs a camera and this one component.
    /// PLAY loads the gameplay scene; the sound toggle and stats line read/write the persistent
    /// <see cref="GameSession"/>, demonstrating settings and stats that survive the scene change.
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuController : MonoBehaviour
    {
        const string GameSceneName = "Saloon";

        // Buttons read as plain gold text: no filled box, just a faint warm highlight on hover/press.
        static readonly Color ButtonTransparent = new(0f, 0f, 0f, 0f);
        static readonly Color ButtonHoverTint = new(0.34f, 0.15f, 0.07f, 0.3f);
        static readonly Color ButtonPressTint = new(0.08f, 0.03f, 0.02f, 0.45f);

        [Header("Background Coin Models (Copper / Silver / Gold)")]
        [SerializeField] GameObject copperCoinModel;
        [SerializeField] GameObject silverCoinModel;
        [SerializeField] GameObject goldCoinModel;



        [Header("Audio")]
        [SerializeField] AK.Wwise.Event auftakt;
        [SerializeField] float auftaktDauer = 2f;
        [SerializeField] AK.Wwise.Event uiClick;



        GameSession session;
        Text statsText;
        Text soundButtonLabel;

        void Awake()
        {
            session = GameSession.EnsureExists();
            EnsureEventSystem();
            // The real copper/silver/gold coins tumble in 3D in front of the camera; they render behind
            // the screen-space menu UI, and the camera's solid dark clear colour is the background.
            MenuCoinBackground.Create(copperCoinModel, silverCoinModel, goldCoinModel);
            BuildMenu();
        }

        void BuildMenu()
        {
            var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Main Menu Canvas");

            RuntimeUiFactory.CreateText(
                canvas.transform,
                "Title",
                "MENISCUS",
                new Vector2(0f, 300f),
                new Vector2(1400f, 170f),
                110,
                new Color(0.95f, 0.82f, 0.52f),
                TextAnchor.MiddleCenter,
                bold: true);

            RuntimeUiFactory.CreateText(
                canvas.transform,
                "Tagline",
                "Don't let it spill.",
                new Vector2(0f, 196f),
                new Vector2(1000f, 60f),
                30,
                new Color(0.78f, 0.66f, 0.5f));

            statsText = RuntimeUiFactory.CreateText(
                canvas.transform,
                "Stats",
                string.Empty,
                new Vector2(0f, 112f),
                new Vector2(1100f, 90f),
                24,
                new Color(0.72f, 0.63f, 0.52f));

            RuntimeUiFactory.CreateButton(

                canvas.transform,
                "Play Button",
                "PLAY",
                new Vector2(320f, 76f),
                new Vector2(0f, 0f),
                32,
                PlayGame,
                boldLabel: true,
                normalColor: ButtonTransparent,
                highlightedColor: ButtonHoverTint,
                pressedColor: ButtonPressTint);
                

            soundButtonLabel = RuntimeUiFactory.CreateButton(
                canvas.transform,
                "Sound Button",
                string.Empty,
                new Vector2(320f, 56f),
                new Vector2(0f, -96f),
                22,
                ToggleSound,
                normalColor: ButtonTransparent,
                highlightedColor: ButtonHoverTint,
                pressedColor: ButtonPressTint).GetComponentInChildren<Text>();

            RuntimeUiFactory.CreateButton(
                canvas.transform,
                "Quit Button",
                "QUIT",
                new Vector2(320f, 56f),
                new Vector2(0f, -172f),
                22,
                QuitGame,
                normalColor: ButtonTransparent,
                highlightedColor: ButtonHoverTint,
                pressedColor: ButtonPressTint);

            RefreshStats();
            RefreshSoundLabel();
        }

        void PlayClick() => uiClick?.Post(gameObject);


        void PlayGame()
        {
             PlayClick();
            if (auftakt != null && auftakt.IsValid())
            {
                auftakt.Post(gameObject);
                StartCoroutine(LoadAfterDelay(auftaktDauer));
            }
            else
            {
                SceneManager.LoadScene(GameSceneName);
            }
        }

        System.Collections.IEnumerator LoadAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            SceneManager.LoadScene(GameSceneName);
        }

        void ToggleSound()
        {
                 PlayClick();
            session.ToggleSound();
            RefreshSoundLabel();
        }

        void QuitGame()

        {
                 PlayClick();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void RefreshStats()
        {
            if (statsText == null)
                return;

            if (session == null || session.MatchesPlayed == 0)
            {
                statsText.text = "First pour. Good luck.";
                return;
            }

            statsText.text =
                $"Matches: {session.MatchesPlayed}   Won: {session.MatchesWon}   Lost: {session.MatchesLost}\n" +
                $"Best bank: ${session.BestBankedCash}   Last bank: ${session.LastBankedCash}";
        }

        void RefreshSoundLabel()
        {
            if (soundButtonLabel != null)
                soundButtonLabel.text = session != null && session.SoundEnabled ? "SOUND: ON" : "SOUND: OFF";
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }
    }
}
