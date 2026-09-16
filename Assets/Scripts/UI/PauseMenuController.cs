using Meniscus.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Escape-key pause overlay for the saloon: continue, sound toggle and a way
    /// back to the title. Built at runtime in the same warm gold-on-dark language as the rest
    /// of the fallback UI, so the scene needs nothing authored.
    ///
    /// The whole game is frozen with <c>Time.timeScale</c>, so everything here has to run on
    /// unscaled time and the canvas sorts above the dialogue box and HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuController : MonoBehaviour
    {
        const string MenuSceneName = "MainMenu";

        static readonly Color ButtonTransparent = new(0f, 0f, 0f, 0f);
        static readonly Color ButtonHoverTint = new(0.34f, 0.15f, 0.07f, 0.45f);
        static readonly Color ButtonPressTint = new(0.08f, 0.03f, 0.02f, 0.6f);
        static readonly Color Gold = new(0.95f, 0.82f, 0.52f);

        GameSession session;
        Canvas canvas;
        Text soundLabel;
        float resumeTimeScale = 1f;

        public bool IsPaused { get; private set; }

        void Awake()
        {
            session = GameSession.EnsureExists();
            BuildUi();
            SetVisible(false);
        }

        void OnDestroy()
        {
            // Never leave the game frozen if this is torn down while paused.
            if (IsPaused)
                Time.timeScale = resumeTimeScale;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Toggle();
        }

        public void Toggle()
        {
            if (IsPaused)
                Continue();
            else
                Pause();
        }

        public void Pause()
        {
            if (IsPaused)
                return;

            IsPaused = true;
            resumeTimeScale = Time.timeScale <= 0f ? 1f : Time.timeScale;
            Time.timeScale = 0f;
            RefreshLabels();
            SetVisible(true);
        }

        public void Continue()
        {
            if (!IsPaused)
                return;

            IsPaused = false;
            Time.timeScale = resumeTimeScale;
            SetVisible(false);
        }

        void QuitToMenu()
        {
            // Restore time before leaving, or the menu scene loads frozen.
            IsPaused = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(MenuSceneName);
        }

        void ToggleSound()
        {
            if (session != null)
                session.ToggleSound();

            RefreshLabels();
        }

        void RefreshLabels()
        {
            if (soundLabel != null)
                soundLabel.text = session != null && session.SoundEnabled ? "SOUND: ON" : "SOUND: OFF";
        }

        void SetVisible(bool visible)
        {
            if (canvas != null)
                canvas.enabled = visible;
        }

        void BuildUi()
        {
            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Pause Menu Canvas");
            canvas.sortingOrder = 200;   // above the dialogue box (66) and the HUD

            // Dim everything behind, including the dialogue box, so the panel is the only
            // thing asking to be read.
            var scrim = RuntimeUiFactory.CreateImage(
                canvas.transform, "Scrim", Vector2.zero, Vector2.zero, new Color(0.04f, 0.02f, 0.01f, 0.82f));
            var scrimRect = scrim.GetComponent<RectTransform>();
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = Vector2.zero;
            scrimRect.offsetMax = Vector2.zero;

            var scale = UiScale.ControlScale;
            var width = 400f * (UiScale.Touch ? 1.5f : 1f);
            var rowHeight = 58f * scale;
            var gap = 10f * scale;
            var titleHeight = 70f * scale;

            // Lay the rows out from a measured total so the panel wraps them exactly and the
            // block stays centred whatever the control scale is.
            var contentHeight = titleHeight + gap * 2f
                              + rowHeight + gap
                              + rowHeight + gap * 2f
                              + rowHeight;

            var panelWidth = width + 88f;
            var panelHeight = contentHeight + 72f;

            RuntimeUiFactory.CreateImage(
                canvas.transform, "Panel Border", new Vector2(panelWidth + 12f, panelHeight + 12f),
                Vector2.zero, new Color(0.78f, 0.62f, 0.36f, 0.75f));
            RuntimeUiFactory.CreateImage(
                canvas.transform, "Panel", new Vector2(panelWidth, panelHeight),
                Vector2.zero, new Color(0.07f, 0.04f, 0.025f, 0.97f));

            // Walk down from the top of the content block.
            var y = contentHeight * 0.5f;

            y -= titleHeight * 0.5f;
            RuntimeUiFactory.CreateText(
                canvas.transform, "Paused", "PAUSED",
                new Vector2(0f, y), new Vector2(panelWidth, titleHeight),
                Mathf.RoundToInt(40 * scale), Gold, TextAnchor.MiddleCenter, bold: true);
            y -= titleHeight * 0.5f + gap * 2f;

            y -= rowHeight * 0.5f;
            RuntimeUiFactory.CreateButton(
                canvas.transform, "Continue Button", "CONTINUE",
                new Vector2(width, rowHeight), new Vector2(0f, y),
                Mathf.RoundToInt(26 * scale), Continue, boldLabel: true,
                normalColor: ButtonTransparent, highlightedColor: ButtonHoverTint, pressedColor: ButtonPressTint);
            y -= rowHeight * 0.5f + gap;

            y -= rowHeight * 0.5f;
            soundLabel = RuntimeUiFactory.CreateButton(
                canvas.transform, "Sound Button", string.Empty,
                new Vector2(width, rowHeight), new Vector2(0f, y),
                Mathf.RoundToInt(22 * scale), ToggleSound,
                normalColor: ButtonTransparent, highlightedColor: ButtonHoverTint, pressedColor: ButtonPressTint)
                .GetComponentInChildren<Text>();
            y -= rowHeight * 0.5f + gap * 2f;

            y -= rowHeight * 0.5f;
            RuntimeUiFactory.CreateButton(
                canvas.transform, "Quit Button", "QUIT TO TITLE",
                new Vector2(width, rowHeight), new Vector2(0f, y),
                Mathf.RoundToInt(22 * scale), QuitToMenu,
                normalColor: ButtonTransparent, highlightedColor: ButtonHoverTint, pressedColor: ButtonPressTint);

            RefreshLabels();
        }
    }
}
