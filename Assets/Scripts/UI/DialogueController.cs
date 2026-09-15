using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>A camera reaction played between dialogue boxes.</summary>
    public enum DialogueCue { None, Nod, StrongNod }

    /// <summary>
    /// A camera framing switched to as a box appears (before it types). The actual poses are owned by the
    /// monologue so writers pick the intent here and dial the feel there.
    /// </summary>
    public enum DialogueShot
    {
        Keep,               // leave the camera wherever it is
        Default,            // return to the resting table framing
        DealerCloseUp,      // lean-in close-up of the dealer
        DealerFront,        // tight, straight-on close-up of the dealer's face
        MainCharacterFace   // reaction close-up of the main character's face (uses the camera's face anchor)
    }

    /// <summary>
    /// One dialogue box: a block of text plus an optional camera reaction fired once the box is dismissed.
    /// Authored in the inspector (multi-line <see cref="text"/>) so writers can edit copy without touching code.
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        [TextArea(2, 6)] public string text;

        [Tooltip("A camera reaction played AFTER this box is dismissed, before the next one appears.")]
        public DialogueCue cueAfter = DialogueCue.None;

        [Tooltip("Camera framing switched to as this box APPEARS (before it types). Keep = leave it as-is.")]
        public DialogueShot shotOnEnter = DialogueShot.Keep;

        [Tooltip("Hold a silent dramatic beat before this box types (the duration is owned by the speaker).")]
        public bool pauseOnEnter;

        public DialogueLine() { }

        public DialogueLine(
            string text,
            DialogueCue cueAfter = DialogueCue.None,
            DialogueShot shotOnEnter = DialogueShot.Keep,
            bool pauseOnEnter = false)
        {
            this.text = text;
            this.cueAfter = cueAfter;
            this.shotOnEnter = shotOnEnter;
            this.pauseOnEnter = pauseOnEnter;
        }
    }

    /// <summary>
    /// Undertale-style dialogue box: a bottom-of-screen panel that reveals each line one character at a time
    /// (with a looping voice gibberish while it types) and waits for a click / key before advancing. A press
    /// while a line is still typing completes it instantly; the next press advances. Lines that carry a
    /// <see cref="DialogueCue"/> hand off to an <c>interlude</c> coroutine (the monologue uses this to nod the
    /// camera) before the next box appears. The 3D scene stays visible above the box, so the speaker reads as
    /// talking in-world. Built and owned at runtime, play-mode only — mirrors the other saloon overlays.
    /// </summary>
    [DisallowMultipleComponent]
    public class DialogueController : MonoBehaviour
    {
        [Header("Typewriter")]
        [SerializeField, Min(1f)] float charactersPerSecond = 45f;
        [SerializeField, Min(0f)] float boxFadeSeconds = 0.25f;

        [Header("Audio")]
        [Tooltip("Looping voice gibberish that plays WHILE a line types. Started as the line begins, stopped " +
                 "when it finishes (or is skipped). Injected at runtime by the GameManager (this box has no " +
                 "inspector of its own); see SetVoice. Empty = silent text.")]
        [SerializeField] AK.Wwise.Event dealerVoiceLoop;
        [Tooltip("Stops the looping voice gibberish. Posted when a line finishes typing, is skipped, or the " +
                 "dialogue ends. Injected at runtime by the GameManager; see SetVoice.")]
        [SerializeField] AK.Wwise.Event dealerVoiceStop;

        Canvas canvas;
        CanvasGroup group;
        Text bodyText;
        Text hintText;
        Coroutine routine;
        bool playing;
        bool voicePlaying;   // guards against double Start / orphaned Stop

        public bool IsPlaying => playing;

        public static DialogueController CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime Dialogue");
            var controller = root.AddComponent<DialogueController>();
            controller.BuildUi();
            return controller;
        }

        /// <summary>Wire an authored box (canvas + group + text) instead of the runtime one.</summary>
        public void Configure(Canvas dialogueCanvas, CanvasGroup canvasGroup, Text body, Text hint)
        {
            canvas = dialogueCanvas;
            group = canvasGroup;
            bodyText = body;
            hintText = hint;

            if (canvas != null)
                canvas.enabled = false;
        }

        /// <summary>Inject the typewriter voice loop + its stop. The GameManager owns the events so they stay
        /// inspector-assignable (this box is built at runtime); the loop plays while a line types and is stopped
        /// when it finishes.</summary>
        public void SetVoice(AK.Wwise.Event voiceLoop, AK.Wwise.Event voiceStop)
        {
            dealerVoiceLoop = voiceLoop;
            dealerVoiceStop = voiceStop;
        }

        // Start the talking gibberish loop (once), if it isn't already going.
        void StartVoice()
        {
            if (voicePlaying)
                return;

            dealerVoiceLoop?.Post(gameObject);
            voicePlaying = true;
        }

        // Stop the talking gibberish loop, if it's going. Safe to call any number of times.
        void StopVoice()
        {
            if (!voicePlaying)
                return;

            dealerVoiceStop?.Post(gameObject);
            voicePlaying = false;
        }

        /// <summary>
        /// Play a sequence of lines, calling <paramref name="onComplete"/> once the last box is dismissed. If
        /// <paramref name="interlude"/> is supplied it is yielded between boxes whenever a line carries a cue.
        /// If <paramref name="onEnter"/> is supplied it is yielded as each box appears, before it types (the
        /// monologue uses this to push the camera into a close-up / hold a dramatic beat).
        /// </summary>
        public void Play(
            IReadOnlyList<DialogueLine> lines,
            Action onComplete,
            Func<DialogueCue, IEnumerator> interlude = null,
            Func<DialogueLine, IEnumerator> onEnter = null)
        {
            if (lines == null || lines.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            if (canvas == null)
                BuildUi();

            Stop();
            routine = StartCoroutine(PlayRoutine(lines, onComplete, interlude, onEnter));
        }

        public void Hide()
        {
            Stop();
            if (group != null) group.alpha = 0f;
            if (canvas != null) canvas.enabled = false;
            playing = false;
        }

        void Stop()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            // Never leave the gibberish looping if we were torn down mid-line.
            StopVoice();
        }

        void BuildUi()
        {
            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Dialogue Canvas", enabled: false);
            canvas.sortingOrder = 66;   // above the HUD / round cards, below the loss & outro fades

            group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;   // advance uses raw input, so the box never eats clicks

            // A warm gold border behind a dark wood panel, parked in the lower third of the screen. Smaller
            // and tighter than a full-width bar, with larger text so each box fills out more. Kept fairly
            // see-through so the scene (the dealer talking) reads behind the box.
            var touch = UiScale.Touch;

            var border = RuntimeUiFactory.CreateImage(
                canvas.transform, "Dialogue Border",
                new Vector2(1180f, 250f), new Vector2(0f, -300f), new Color(0.78f, 0.62f, 0.36f, 0.7f));
            var panel = RuntimeUiFactory.CreateImage(
                canvas.transform, "Dialogue Panel",
                new Vector2(1144f, 214f), new Vector2(0f, -300f), new Color(0.06f, 0.035f, 0.025f, 0.6f));

            bodyText = RuntimeUiFactory.CreateText(
                canvas.transform, "Dialogue Body", string.Empty,
                new Vector2(0f, -296f), new Vector2(1056f, 168f), touch ? 46 : 42,
                new Color(0.97f, 0.93f, 0.84f), TextAnchor.UpperLeft);
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            bodyText.lineSpacing = 1.05f;

            hintText = RuntimeUiFactory.CreateText(
                canvas.transform, "Dialogue Hint",
                touch ? "tap to continue  \u25bc" : "click to continue  \u25bc",
                new Vector2(360f, -398f), new Vector2(320f, 28f), touch ? 24 : 17,
                new Color(0.78f, 0.62f, 0.36f, 0.8f), TextAnchor.MiddleRight);
            hintText.enabled = false;

            // The box was parked a fixed distance below the screen centre, which only lands
            // in the lower third at the 16:9 it was authored for. Pin it to the bottom edge
            // instead so it sits there at any aspect, and let it span the width on a phone,
            // where a 1180-wide box would otherwise run off both sides.
            PinToBottom(border, -300f, 250f, touch ? 24f : 370f);
            PinToBottom(panel, -300f, 214f, touch ? 36f : 388f);
            PinToBottom(bodyText, -296f, 168f, touch ? 60f : 432f);
            PinToBottom(hintText, -398f, 28f, touch ? 60f : 432f);
        }

        IEnumerator PlayRoutine(
            IReadOnlyList<DialogueLine> lines,
            Action onComplete,
            Func<DialogueCue, IEnumerator> interlude,
            Func<DialogueLine, IEnumerator> onEnter)
        {
            playing = true;
            canvas.enabled = true;
            bodyText.text = string.Empty;
            if (hintText != null) hintText.enabled = false;

            yield return FadeBox(0f, 1f, boxFadeSeconds);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // Let the speaker reframe the camera / hold a beat as the box appears, before it types.
                if (onEnter != null && line != null)
                    yield return onEnter(line);

                yield return ShowLine(line != null ? line.text : string.Empty);

                var cue = line != null ? line.cueAfter : DialogueCue.None;
                if (cue != DialogueCue.None && interlude != null)
                {
                    if (hintText != null) hintText.enabled = false;
                    yield return interlude(cue);
                }
            }

            if (hintText != null) hintText.enabled = false;
            yield return FadeBox(1f, 0f, boxFadeSeconds);

            canvas.enabled = false;
            routine = null;
            playing = false;
            onComplete?.Invoke();
        }

        // Reveal one line character by character; a press completes it instantly, the next press returns. The
        // voice gibberish loops while the line types and is stopped the moment it's complete (or skipped).
        IEnumerator ShowLine(string text)
        {
            text ??= string.Empty;
            bodyText.text = string.Empty;
            if (hintText != null) hintText.enabled = false;

            var shown = 0;
            var acc = 0f;
            var complete = text.Length == 0;

            if (complete)
            {
                bodyText.text = text;
                if (hintText != null) hintText.enabled = true;
            }
            else
            {
                // The dealer starts talking as the line begins to type.
                StartVoice();
            }

            while (true)
            {
                var pressed = AdvancePressed();

                if (!complete)
                {
                    if (pressed)
                    {
                        // Snap the whole line in; yield a frame so this same press can't also advance.
                        bodyText.text = text;
                        shown = text.Length;
                        complete = true;
                        StopVoice();                 // talking stops the instant the line is done
                        if (hintText != null) hintText.enabled = true;
                        yield return null;
                        continue;
                    }

                    acc += Time.unscaledDeltaTime * charactersPerSecond;
                    shown = Mathf.Min(text.Length, Mathf.FloorToInt(acc));
                    bodyText.text = text.Substring(0, shown);

                    if (shown >= text.Length)
                    {
                        complete = true;
                        StopVoice();                 // talking stops when the line finishes typing
                        if (hintText != null) hintText.enabled = true;
                    }
                }
                else if (pressed)
                {
                    yield break;
                }

                yield return null;
            }
        }

        IEnumerator FadeBox(float from, float to, float seconds)
        {
            if (group == null)
                yield break;

            if (seconds <= 0f)
            {
                group.alpha = to;
                yield break;
            }

            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
                yield return null;
            }

            group.alpha = to;
        }

        /// Re-anchors an element that was authored as an offset from the screen centre so it
        /// hangs off the bottom edge instead, stretching horizontally between side margins.
        /// <paramref name="centreY"/> and <paramref name="height"/> are the authored values;
        /// the gap to the bottom edge is derived from them, so 16:9 looks exactly as before.
        static void PinToBottom(GameObject element, float centreY, float height, float sideMargin)
            => PinToBottom(element == null ? null : element.transform, centreY, height, sideMargin);

        static void PinToBottom(Component element, float centreY, float height, float sideMargin)
        {
            if (element == null)
                return;

            var rect = element.GetComponent<RectTransform>();
            if (rect == null)
                return;

            var bottom = UiScale.ReferenceHalfHeight + centreY - height * 0.5f;

            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(sideMargin, bottom);
            rect.offsetMax = new Vector2(-sideMargin, bottom + height);
        }

        static bool AdvancePressed()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return true;

            // A tap is not reported through Mouse unless touch simulation is on, so without
            // this the dialogue cannot be advanced at all on a phone.
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                return true;

            var keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.zKey.wasPressedThisFrame))
                return true;

            return false;
        }
    }
}