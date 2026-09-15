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
        Text volumeLabel;

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

            // Controls grow for a fingertip, and the rows are spaced from the resulting
            // heights so the larger buttons cannot overlap each other.
            var scale = UiScale.ControlScale;
            var width = 320f * (UiScale.Touch ? 1.7f : 1f);
            var playHeight = 76f * scale;
            var rowHeight = 56f * scale;
            var gap = 20f * scale;

            RuntimeUiFactory.CreateButton(
                canvas.transform,
                "Play Button",
                "PLAY",
                new Vector2(width, playHeight),
                new Vector2(0f, 0f),
                Mathf.RoundToInt(32 * scale),
                PlayGame,
                boldLabel: true,
                normalColor: ButtonTransparent,
                highlightedColor: ButtonHoverTint,
                pressedColor: ButtonPressTint);

            soundButtonLabel = RuntimeUiFactory.CreateButton(
                canvas.transform,
                "Sound Button",
                string.Empty,
                new Vector2(width, rowHeight),
                new Vector2(0f, -(playHeight * 0.5f + gap + rowHeight * 0.5f)),
                Mathf.RoundToInt(22 * scale),
                ToggleSound,
                normalColor: ButtonTransparent,
                highlightedColor: ButtonHoverTint,
                pressedColor: ButtonPressTint).GetComponentInChildren<Text>();

            // Volume sits directly under the sound toggle, the two audio controls together.
            var volumeY = -(playHeight * 0.5f + gap * 2f + rowHeight * 1.5f);

            volumeLabel = RuntimeUiFactory.CreateText(
                canvas.transform,
                "Volume Label",
                string.Empty,
                new Vector2(0f, volumeY),
                new Vector2(width, 30f * scale),
                Mathf.RoundToInt(16 * scale),
                new Color(0.72f, 0.63f, 0.52f));

            RuntimeUiFactory.CreateSlider(
                canvas.transform,
                "Volume",
                new Vector2(width * 0.8f, 30f * scale),
                new Vector2(0f, volumeY - 30f * scale),
                session != null ? session.MasterVolume : 0.7f,
                SetMasterVolume);

            RuntimeUiFactory.CreateButton(
                canvas.transform,
                "Quit Button",
                "QUIT",
                new Vector2(width, rowHeight),
                new Vector2(0f, volumeY - 30f * scale - gap * 2f - rowHeight * 0.5f),
                Mathf.RoundToInt(22 * scale),
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
                var playingId = auftakt.Post(gameObject);
                if (playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                    StartCoroutine(LoadAfterDelay(auftaktDauer));
                else
                    SceneManager.LoadScene(GameSceneName);
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
                // Nothing to report before the first match: leave the line blank.
                statsText.text = string.Empty;
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

            if (volumeLabel != null)
                volumeLabel.text = $"VOLUME  {Mathf.RoundToInt((session != null ? session.MasterVolume : 0f) * 100f)}%";
        }

        void SetMasterVolume(float value)
        {
            if (session != null)
                session.SetMasterVolume(value);

            RefreshSoundLabel();
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
