#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Meniscus.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Top-left debug menu (editor / development builds only — never shipped) for jumping the match
    /// straight to a state instead of playing it out: chiefly to hear and see the loss beat (the loss
    /// sequence + the Piano_Ende lose sting) and the win end screen on demand. Self-installs in any scene
    /// that owns a <see cref="GameManager"/>, the same way <see cref="SpillDebugReadout"/> does, and stacks
    /// just beneath that readout so the two never overlap.
    /// </summary>
    [DisallowMultipleComponent]
    public class StateDebugMenu : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // Subscribe (rather than install once) so a gameplay scene reached later via the menu still
            // gets the panel; the guards in Install keep it to match scenes and avoid duplicates.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            if (Object.FindAnyObjectByType<GameManager>() == null)
                return;
            if (Object.FindAnyObjectByType<StateDebugMenu>() != null)
                return;

            new GameObject("State Debug Menu").AddComponent<StateDebugMenu>();
        }

        static readonly Color HeaderColor = new(0.95f, 0.78f, 0.45f, 1f); // matches the spill readout
        static readonly Color LossTint = new(0.52f, 0.12f, 0.09f, 0.95f);
        static readonly Color WinTint = new(0.15f, 0.40f, 0.18f, 0.95f);
        static readonly Color NeutralTint = new(0.22f, 0.14f, 0.07f, 0.95f);

        const float Width = 232f;
        const float Height = 30f;
        const float Gap = 6f;
        const float LeftX = 18f;

        GameManager game;
        Canvas canvas;
        float nextY;

        void OnEnable()
        {
            ResolveGame();
            BuildUi();
        }

        // The GameManager may not exist yet when we install; keep looking so the buttons stay live.
        void Update()
        {
            if (game == null)
                ResolveGame();
        }

        void ResolveGame() => game = FindAnyObjectByType<GameManager>();

        void BuildUi()
        {
            if (canvas != null)
                return;

            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "State Debug Canvas");
            canvas.sortingOrder = 121; // one above the spill readout (120) so it's never hidden

            // Stack downward from beneath the spill readout (which sits at y=-16, ~44 tall).
            nextY = -58f;

            AddHeader("DEBUG · TRIGGER STATE");
            AddButton("LOSS  (sequence + sting)", LossTint,
                () => game?.DebugForceOutcome(MatchOutcome.PlayerLost));
            AddButton("WIN  (end screen)", WinTint,
                () => game?.DebugForceOutcome(MatchOutcome.PlayerWon));
            AddButton("RESTART MATCH", NeutralTint, ReloadActiveScene);

            canvas.enabled = true;
        }

        void AddHeader(string text)
        {
            var label = RuntimeUiFactory.CreateText(
                canvas.transform, "Debug Header", text, Vector2.zero, new Vector2(Width, 22f),
                15, HeaderColor, TextAnchor.UpperLeft, bold: true);
            label.raycastTarget = false;

            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            PinTopLeft(label.rectTransform, new Vector2(LeftX, nextY));
            nextY -= 22f + Gap;
        }

        void AddButton(string label, Color tint, UnityAction onClick)
        {
            var button = RuntimeUiFactory.CreateButton(
                canvas.transform, $"Debug · {label}", label, new Vector2(Width, Height),
                Vector2.zero, 15, onClick, boldLabel: true,
                normalColor: tint,
                highlightedColor: Lighten(tint, 0.12f),
                pressedColor: Lighten(tint, -0.06f));

            PinTopLeft(button.GetComponent<RectTransform>(), new Vector2(LeftX, nextY));
            nextY -= Height + Gap;
        }

        // Reset to the top of the same scene; clear any slow-mo / freeze the loss beat may have left.
        void ReloadActiveScene()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        static void PinTopLeft(RectTransform rt, Vector2 anchoredPosition)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPosition;
        }

        static Color Lighten(Color c, float delta) => new(
            Mathf.Clamp01(c.r + delta), Mathf.Clamp01(c.g + delta), Mathf.Clamp01(c.b + delta), c.a);
    }
}
#endif
