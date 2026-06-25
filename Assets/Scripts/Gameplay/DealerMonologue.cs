using System;
using System.Collections;
using Meniscus.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// The dealer's talking beats that bookend a match. The intro replaces the old "walk in and sit" seating
    /// intro: the camera lifts off the table (waking up), then the dealer's monologue plays as dialogue boxes,
    /// with a camera nod on the cued lines. The outro plays his losing monologue, then dims the screen to
    /// black, brings up the end screen behind it and lifts the black to reveal it. Owns the beat and delegates
    /// the text to <see cref="DialogueController"/> and the camera moves to <see cref="CameraController"/> —
    /// the same split the seating intro / loss sequence use. Built and owned at runtime, play-mode only, so
    /// EditMode tests keep the instant StartMatch → StartRound / plain end-screen paths.
    /// </summary>
    [DisallowMultipleComponent]
    public class DealerMonologue : MonoBehaviour
    {
        [Header("Intro pacing")]
        [Tooltip("Beat the head rests on the table before it lifts (waking up).")]
        [SerializeField, Min(0f)] float wakeHoldSeconds = 0.9f;
        [Tooltip("Beat after the head has lifted before the first line appears.")]
        [SerializeField, Min(0f)] float beatBeforeFirstLine = 0.4f;
        [Tooltip("Beat the camera nod is allowed to read before the next box appears.")]
        [SerializeField, Min(0f)] float nodInterludeSeconds = 0.6f;
        [Tooltip("Length of the silent dramatic beat held before a line flagged for it (e.g. \"Well, well. " +
                 "Too bad…\") — the pause before the camera pushes to the dealer's face.")]
        [SerializeField, Min(0f)] float pauseBeatSeconds = 1.2f;

        [Header("Intro camera shots")]
        [Tooltip("Lean-in close-up of the dealer (used on \"Well it's easy to…\"). Offset from the resting " +
                 "pose: +z pushes toward him, +x right, +y up.")]
        [SerializeField] Vector3 dealerCloseUpPosition = new(0f, -0.05f, 2.25f);
        [Tooltip("Rotation offset for the lean-in close-up (degrees, camera space): +x pitches DOWN, " +
                 "-x looks UP toward his face.")]
        [SerializeField] Vector3 dealerCloseUpRotation = new(-7f, 0f, 0f);
        [Tooltip("Tight, straight-on close-up of the dealer's face (the beat after the silent pause).")]
        [SerializeField] Vector3 dealerFrontPosition = new(0f, 0.12f, 3.2f);
        [SerializeField] Vector3 dealerFrontRotation = new(-13f, 0f, 0f);

        [Header("Wake-up blink (eyelids)")]
        [Tooltip("Black bars close from top & bottom like eyelids; the screen starts shut and blinks open as " +
                 "the head lifts. Number of groggy blinks before the eyes settle fully open.")]
        [SerializeField, Min(0)] int eyelidBlinks = 2;
        [Tooltip("Seconds the eyes first crack open from fully shut.")]
        [SerializeField, Min(0f)] float eyelidCrackSeconds = 0.35f;
        [Tooltip("Seconds the lid takes to drop on a blink.")]
        [SerializeField, Min(0f)] float eyelidCloseSeconds = 0.12f;
        [Tooltip("Seconds the lid takes to lift after a blink.")]
        [SerializeField, Min(0f)] float eyelidOpenSeconds = 0.2f;
        [Tooltip("Seconds the eyes take to settle fully open after the last blink.")]
        [SerializeField, Min(0f)] float eyelidFinalOpenSeconds = 0.3f;
        [Tooltip("Height (reference px) of each bar when the eyes are fully shut. ~half of 1080 plus overscan.")]
        [SerializeField, Min(0f)] float eyelidClosedHeight = 600f;

        [Header("Intro lines")]
        [SerializeField] DialogueLine[] introLines = BuildDefaultIntro();

        [Header("Outro lines")]
        [SerializeField] DialogueLine[] outroLines = BuildDefaultOutro();

        [Header("Outro fade to black")]
        [SerializeField] Color fadeColor = Color.black;
        [SerializeField, Min(0f)] float outroFadeSeconds = 1.4f;     // image dims to black
        [SerializeField, Min(0f)] float outroHoldBlackSeconds = 0.7f; // beat held on black
        [SerializeField, Min(0f)] float outroRevealSeconds = 0.6f;    // black lifts to reveal the end screen

        Coroutine routine;
        GameObject fadeRoot;
        Image fadeImage;
        GameObject eyelidRoot;
        RectTransform topLid;
        RectTransform bottomLid;
        float eyelidOpen = 1f;     // 0 = fully shut (black), 1 = fully open (bars gone)
        bool playing;

        public bool IsPlaying => playing;

        public static DealerMonologue CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime Dealer Monologue");
            return root.AddComponent<DealerMonologue>();
        }

        /// <summary>Wake-up head lift, then the intro monologue, then <paramref name="onDone"/> (start the round).</summary>
        public void PlayIntro(CameraController camera, DialogueController dialogue, Action onDone)
        {
            if (playing)
                return;

            if (dialogue == null)
            {
                onDone?.Invoke();
                return;
            }

            playing = true;
            ClearFade();
            ClearEyelids();
            routine = StartCoroutine(IntroRoutine(camera, dialogue, onDone));
        }

        /// <summary>The losing monologue, then a dim to black; <paramref name="onDone"/> brings up the end screen.</summary>
        public void PlayOutro(CameraController camera, DialogueController dialogue, Action onDone)
        {
            if (playing)
                return;

            if (dialogue == null)
            {
                onDone?.Invoke();
                return;
            }

            playing = true;
            ClearFade();
            ClearEyelids();
            routine = StartCoroutine(OutroRoutine(camera, dialogue, onDone));
        }

        IEnumerator IntroRoutine(CameraController camera, DialogueController dialogue, Action onDone)
        {
            camera?.BeginWakeUp();
            BuildEyelids();
            SetEyelids(0f);                 // eyes shut while slumped on the table (screen opens on black)
            yield return WaitUnscaled(wakeHoldSeconds);

            var risen = false;
            if (camera != null) camera.PlayWakeUp(() => risen = true);
            else risen = true;

            // The blink plays over the head lift (the camera rises in parallel via its own update).
            yield return BlinkAwake();
            while (!risen) yield return null;
            ClearEyelids();

            yield return WaitUnscaled(beatBeforeFirstLine);

            var done = false;
            dialogue.Play(
                introLines,
                () => done = true,
                cue => NodInterlude(camera, cue),
                line => LineEnter(camera, line));
            while (!done) yield return null;

            camera?.ClearMonologueShot();   // back to the resting framing for the round (defensive: last shot already did)

            routine = null;
            playing = false;
            onDone?.Invoke();
        }

        IEnumerator OutroRoutine(CameraController camera, DialogueController dialogue, Action onDone)
        {
            var done = false;
            dialogue.Play(outroLines, () => done = true, cue => NodInterlude(camera, cue));
            while (!done) yield return null;

            BuildFadeOverlay();
            yield return Fade(0f, 1f, outroFadeSeconds);     // dim to black
            yield return WaitUnscaled(outroHoldBlackSeconds);

            onDone?.Invoke();                                // build the end screen behind the black
            yield return Fade(1f, 0f, outroRevealSeconds);   // lift the black to reveal it
            ClearFade();

            routine = null;
            playing = false;
        }

        // Played as each box appears, before it types: holds the silent dramatic beat (if flagged) and then
        // pushes the camera to the box's framing, so the close-up eases in while the dealer speaks the line.
        IEnumerator LineEnter(CameraController camera, DialogueLine line)
        {
            if (line == null)
                yield break;

            if (line.pauseOnEnter && pauseBeatSeconds > 0f)
                yield return WaitUnscaled(pauseBeatSeconds);

            ApplyShot(camera, line.shotOnEnter);
        }

        void ApplyShot(CameraController camera, DialogueShot shot)
        {
            if (camera == null)
                return;

            switch (shot)
            {
                case DialogueShot.Default:
                    camera.ClearMonologueShot();
                    break;
                case DialogueShot.DealerCloseUp:
                    camera.FrameMonologueShot(dealerCloseUpPosition, dealerCloseUpRotation);
                    break;
                case DialogueShot.DealerFront:
                    camera.FrameMonologueShot(dealerFrontPosition, dealerFrontRotation);
                    break;
                case DialogueShot.MainCharacterFace:
                    camera.FrameMonologueFace();
                    break;
                case DialogueShot.Keep:
                default:
                    break;
            }
        }

        IEnumerator NodInterlude(CameraController camera, DialogueCue cue)
        {
            if (camera == null || cue == DialogueCue.None)
                yield break;

            // The camera stays at the normal seated framing through the whole monologue, so the nod simply
            // tilts in place (the player nodding) before the next box.
            camera.NodCamera(cue == DialogueCue.StrongNod);
            yield return WaitUnscaled(nodInterludeSeconds);
        }

        void BuildFadeOverlay()
        {
            if (fadeRoot != null)
                return;

            var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Outro Fade Canvas");
            canvas.sortingOrder = 80;   // above everything during the dim; lifted to reveal the end screen
            fadeRoot = canvas.gameObject;

            var image = RuntimeUiFactory.CreateImage(
                canvas.transform, "Outro Fade", new Vector2(1920f, 1080f), Vector2.zero,
                new Color(fadeColor.r, fadeColor.g, fadeColor.b, 0f));
            fadeImage = image.GetComponent<Image>();
            fadeImage.raycastTarget = false;
        }

        IEnumerator Fade(float from, float to, float duration)
        {
            if (fadeImage == null)
                yield break;

            var color = fadeImage.color;

            if (duration <= 0f)
            {
                color.a = to;
                fadeImage.color = color;
                yield break;
            }

            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                color.a = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                fadeImage.color = color;
                yield return null;
            }

            color.a = to;
            fadeImage.color = color;
        }

        void ClearFade()
        {
            if (fadeRoot != null)
                Destroy(fadeRoot);

            fadeRoot = null;
            fadeImage = null;
        }

        void BuildEyelids()
        {
            if (eyelidRoot != null)
                return;

            var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Eyelid Canvas");
            canvas.sortingOrder = 79;   // above the dialogue, just under the outro fade
            eyelidRoot = canvas.gameObject;

            topLid = MakeLid(canvas.transform, "Top Eyelid", true);
            bottomLid = MakeLid(canvas.transform, "Bottom Eyelid", false);
            SetEyelids(eyelidOpen);
        }

        // A full-width black bar pinned to the top or bottom edge; its height is driven by SetEyelids.
        static RectTransform MakeLid(Transform parent, string name, bool top)
        {
            var go = RuntimeUiFactory.CreateImage(parent, name, Vector2.zero, Vector2.zero, Color.black);
            go.GetComponent<Image>().raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        void SetEyelids(float open)
        {
            eyelidOpen = Mathf.Clamp01(open);
            var h = (1f - eyelidOpen) * eyelidClosedHeight;

            if (topLid != null) topLid.sizeDelta = new Vector2(0f, h);
            if (bottomLid != null) bottomLid.sizeDelta = new Vector2(0f, h);
        }

        // Eyes shut → crack open → a couple of groggy blinks, each settling a little wider → fully open.
        IEnumerator BlinkAwake()
        {
            SetEyelids(0f);
            yield return LerpEyelids(0f, 0.55f, eyelidCrackSeconds);

            for (var i = 0; i < eyelidBlinks; i++)
            {
                var openTo = Mathf.Lerp(0.62f, 0.92f, (i + 1f) / Mathf.Max(1, eyelidBlinks));
                yield return LerpEyelids(eyelidOpen, 0.06f, eyelidCloseSeconds);   // lid drops (blink)
                yield return LerpEyelids(0.06f, openTo, eyelidOpenSeconds);        // lid lifts, a little wider
            }

            yield return LerpEyelids(eyelidOpen, 1f, eyelidFinalOpenSeconds);      // settle fully open
            SetEyelids(1f);
        }

        IEnumerator LerpEyelids(float from, float to, float seconds)
        {
            if (seconds <= 0f)
            {
                SetEyelids(to);
                yield break;
            }

            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                SetEyelids(Mathf.SmoothStep(from, to, Mathf.Clamp01(t / seconds)));
                yield return null;
            }

            SetEyelids(to);
        }

        void ClearEyelids()
        {
            if (eyelidRoot != null)
                Destroy(eyelidRoot);

            eyelidRoot = null;
            topLid = null;
            bottomLid = null;
            eyelidOpen = 1f;
        }

        static IEnumerator WaitUnscaled(float seconds)
        {
            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // Seed copy (editable in the inspector). The two nods land on the lines the script marks *nodding*.
        // The camera leans into the dealer on "Well it's easy to…", pulls back, then — after a silent beat —
        // cuts to a reaction close-up of the main character's face on "Well, well. Too bad…" (the pixel-exact
        // CameraController.monologueFaceAnchor) and holds it through his gloating before returning to the
        // resting framing for the deal.
        static DialogueLine[] BuildDefaultIntro() => new[]
        {
            new DialogueLine("Ah, so you're finally awake."),
            new DialogueLine("Wondering where you are? How you got here?"),
            new DialogueLine("Well it's easy to... let's say... pick up a lonely traveller while they rest. " +
                             "And your two little pals are quite bribable.",
                             DialogueCue.None, DialogueShot.DealerCloseUp),
            new DialogueLine("See it like this: I just helped you find a nice place to stay."),
            new DialogueLine("And now that we're here, well – you wouldn't want to leave already, would you?",
                             DialogueCue.Nod, DialogueShot.Default),
            new DialogueLine("Well, well. Too bad this place is locked up and there's nowhere to go. I took " +
                             "your horse too, and your gear. A lone lamb wouldn't make it far in the desert anyway.",
                             DialogueCue.None, DialogueShot.MainCharacterFace, pauseOnEnter: true),
            new DialogueLine("But I see that you might find this situation uncomfortable.",
                             DialogueCue.StrongNod),
            new DialogueLine("Ha, ha. So, let's make a deal.",
                             DialogueCue.None, DialogueShot.Default),
            new DialogueLine("Play my favourite game with me. If you win three rounds, I'll let you go. If not, " +
                             "I'll keep you here for now. Don't worry – I have a nice room ready for you to stay."),
        };

        static DialogueLine[] BuildDefaultOutro() => new[]
        {
            new DialogueLine("I can't believe you— you beat me at my favourite game! I've had years to practice " +
                             "this! No! No, no no!"),
            new DialogueLine("..."),
            new DialogueLine("But alas, I am a reptile of my word. So you, little lamb, are free to go."),
            new DialogueLine("It was... nice playing with you. Fun even. The dunes and tumbleweeds make for bad company."),
            new DialogueLine("Thank you... friend. I've got what I wanted from you. If you ever feel like playing " +
                             "again, come visit this lonely lizard. You know where to find me."),
            new DialogueLine("Farewell!"),
        };
    }
}
