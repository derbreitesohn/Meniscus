using System.Collections;
using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// The lose beat: when the player overflows, the camera starts circling the glass, the world fades to
    /// black around it, and it is revealed alone in a void — still spilling over — until the player restarts.
    /// The hide is masked by a brief fade so it never pops, and everything is reversible: <see cref="End"/>
    /// re-enables what it hid and restores the camera, so an in-place restart (GameManager.StartMatch) brings
    /// the table back without reloading the scene. Built and owned at runtime; play-mode only, so EditMode
    /// tests keep the plain end-screen path.
    /// </summary>
    [DisallowMultipleComponent]
    public class LossSequence : MonoBehaviour
    {
        const string MenuSceneName = "MainMenu";

        [SerializeField, Min(0f)] float fadeToBlackSeconds = 0.7f;   // world darkens around the orbiting glass
        [SerializeField, Min(0f)] float revealSeconds = 0.9f;        // black lifts to reveal the glass in the void
        [SerializeField, Min(0f)] float promptDelay = 1.2f;          // beat before the restart prompt appears
        [SerializeField] Color voidColor = new Color(0.02f, 0.01f, 0.01f, 1f);

        readonly List<Renderer> hiddenRenderers = new();
        readonly List<Canvas> hiddenCanvases = new();

        GameObject overlayRoot;
        GameObject promptRoot;
        Image fadeImage;
        GlassVisualController glass;
        Coroutine runRoutine;
        bool active;

        Camera affectedCamera;
        CameraClearFlags savedClearFlags;
        Color savedBackground;
        bool savedCameraState;

        public bool IsActive => active;

        public static LossSequence CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime Loss Sequence");
            return root.AddComponent<LossSequence>();
        }

        /// <summary>Start the orbit, fade the world to black, reveal the spilling glass alone, show the prompt.</summary>
        public void Begin(GameManager gameManager, CameraController cameraController, string title)
        {
            if (active)
                return;

            active = true;
            glass = FindAnyObjectByType<GlassVisualController>();

            cameraController?.BeginGlassOrbit();
            runRoutine = StartCoroutine(Run(gameManager, title));
        }

        /// <summary>Undo everything Begin changed (called on restart) so the table comes back in place.</summary>
        public void End(CameraController cameraController)
        {
            if (!active)
                return;

            active = false;

            if (runRoutine != null) { StopCoroutine(runRoutine); runRoutine = null; }

            glass?.Thaw();   // resume the liquid animation and clear the held spill

            if (overlayRoot != null) { Destroy(overlayRoot); overlayRoot = null; }
            if (promptRoot != null) { Destroy(promptRoot); promptRoot = null; }
            fadeImage = null;

            foreach (var r in hiddenRenderers)
                if (r != null) r.enabled = true;

            foreach (var c in hiddenCanvases)
                if (c != null) c.enabled = true;

            hiddenRenderers.Clear();
            hiddenCanvases.Clear();

            RestoreBackdrop();
            cameraController?.StopGlassOrbit();
        }

        IEnumerator Run(GameManager gameManager, string title)
        {
            BuildOverlay();
            yield return Fade(0f, 1f, fadeToBlackSeconds);   // darkness closes in while the camera orbits

            // Hidden under full black so the cut is never seen: strip the world to the glass, void the backdrop.
            HideWorldExceptGlass();
            DarkenBackdrop();
            glass?.FreezeAtMaxSpill();                        // freeze at the peak: rivulets run to full, then hold

            yield return Fade(1f, 0f, revealSeconds);         // lift the black: the frozen, spilled-over glass remains

            var elapsed = 0f;
            while (elapsed < promptDelay)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            BuildPrompt(gameManager, title);
            runRoutine = null;
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

        void HideWorldExceptGlass()
        {
            var keep = new HashSet<Renderer>();

            // Keep the ACTUAL glass the player was using, in its spilled state — not a stripped-down version.
            // The visible cup (authored glass / spawned model) lives under the presentation controller's anchor,
            // alongside the liquid body (a child GlassVisualController). Keep that whole subtree so the orbit
            // circles the real losing glass; also keep the tagged interactable in case it sits outside it.
            var presentation = FindAnyObjectByType<GlassPresentationController>();
            if (presentation != null)
                CollectRenderers(keep, presentation.transform);

            var tagged = GameObject.FindGameObjectWithTag("Glass");
            if (tagged != null)
                CollectRenderers(keep, tagged.transform);

            if (glass != null)
            {
                CollectRenderers(keep, glass.transform);
                CollectRenderers(keep, glass.WaterTransform);
            }

            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || keep.Contains(r))
                    continue;

                r.enabled = false;
                hiddenRenderers.Add(r);
            }

            // Hide every canvas (HUD, shop, banners) except our own fade/prompt overlays.
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c == null || !c.enabled || c.transform.IsChildOf(transform))
                    continue;

                c.enabled = false;
                hiddenCanvases.Add(c);
            }
        }

        static void CollectRenderers(HashSet<Renderer> set, Transform root)
        {
            if (root == null)
                return;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                set.Add(r);
        }

        void DarkenBackdrop()
        {
            affectedCamera = Camera.main;

            if (affectedCamera == null)
                return;

            savedClearFlags = affectedCamera.clearFlags;
            savedBackground = affectedCamera.backgroundColor;
            savedCameraState = true;

            // A solid void so the orbiting glass reads alone, with no skybox or table behind it.
            affectedCamera.clearFlags = CameraClearFlags.SolidColor;
            affectedCamera.backgroundColor = voidColor;
        }

        void RestoreBackdrop()
        {
            if (affectedCamera != null && savedCameraState)
            {
                affectedCamera.clearFlags = savedClearFlags;
                affectedCamera.backgroundColor = savedBackground;
            }

            savedCameraState = false;
            affectedCamera = null;
        }

        void BuildOverlay()
        {
            var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Loss Fade Canvas");
            canvas.sortingOrder = 70;   // above the prompt while it fades
            overlayRoot = canvas.gameObject;

            var image = RuntimeUiFactory.CreateImage(
                canvas.transform, "Loss Fade", new Vector2(1920f, 1080f), Vector2.zero, new Color(0f, 0f, 0f, 0f));
            fadeImage = image.GetComponent<Image>();
            fadeImage.raycastTarget = false;   // never block the restart buttons once it is transparent
        }

        void BuildPrompt(GameManager gameManager, string title)
        {
            var canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Loss Prompt Canvas");
            canvas.sortingOrder = 60;
            promptRoot = canvas.gameObject;

            // Title near the top, actions near the bottom — the centre stays clear for the orbiting glass.
            RuntimeUiFactory.CreateText(
                canvas.transform, "Loss Title", title,
                new Vector2(0f, 360f), new Vector2(1400f, 180f), 96,
                new Color(0.86f, 0.16f, 0.12f), TextAnchor.MiddleCenter, bold: true);

            RuntimeUiFactory.CreateText(
                canvas.transform, "Loss Subtitle", "the glass ran over",
                new Vector2(0f, 268f), new Vector2(1400f, 80f), 30,
                new Color(0.7f, 0.6f, 0.5f));

            RuntimeUiFactory.CreateButton(
                canvas.transform, "Loss Restart", "RESTART",
                new Vector2(260f, 64f), new Vector2(0f, -340f), 26,
                onClick: () => { if (gameManager != null) gameManager.StartMatch(); },
                boldLabel: true);

            RuntimeUiFactory.CreateButton(
                canvas.transform, "Loss Main Menu", "MAIN MENU",
                new Vector2(260f, 64f), new Vector2(0f, -416f), 22,
                onClick: () => SceneManager.LoadScene(MenuSceneName));
        }
    }
}
