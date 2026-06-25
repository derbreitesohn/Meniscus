using System.Collections;
using Meniscus.Core;
using Meniscus.Items;
using Meniscus.UI;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Plays a short "use" performance when the player spends an item, so an item reads as actually DOING
    /// something instead of silently vanishing. One conductor that dispatches on the item's effect kind, so a
    /// new item's animation is a single new branch here — mirrors how <see cref="CoinDropPresentationController"/>
    /// stages the drop. The logical effect is already applied by <see cref="GameManager.TryUseItem"/> before
    /// the <see cref="GameManager.ItemUsed"/> event reaches us; this layer is purely the show.
    ///
    /// Built so far — the Spyglass (<see cref="ItemEffectKind.RevealTrueOdds"/>): bring the spyglass up to the
    /// eye, push the camera into a close-up of the whiskey, and iris a scope vignette over the view with the
    /// exact spill % the next pour faces (the odds the item reveals). The scope then lifts, leaving a compact
    /// gauge pinned by the glass for the rest of the round (see <see cref="SpyglassScopeView"/>). While the
    /// performance plays the <see cref="GameManager"/> holds an input lock so a pour can't cut it short.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemUsePresentationController : MonoBehaviour
    {
        [SerializeField] GameManager gameManager;
        [SerializeField] CameraController cameraController;
        [SerializeField] GlassManager glassManager;

        [Header("Spyglass — timing")]
        [SerializeField, Min(0f)] float pickupSeconds = 0.6f;   // raise the spyglass up to the eye
        [SerializeField, Min(0f)] float vanishSeconds = 0.22f;  // spyglass shrinks against the lens and is gone
        [SerializeField, Min(0f)] float irisSeconds = 0.45f;    // scope closes / opens around the view
        [SerializeField, Min(0f)] float readSeconds = 1.7f;     // hold on the odds so they are read

        [Header("Spyglass — held prop")]
        [Tooltip("Resting pose of the held spyglass in camera-local space (x right, y up, z forward into the view).")]
        [SerializeField] Vector3 propHeldLocalPos = new(0.12f, -0.11f, 0.42f);
        [Tooltip("Resting rotation of the held spyglass (camera-local euler). Dial this in for the model's own axes.")]
        [SerializeField] Vector3 propHeldEuler = new(6f, -96f, 4f);
        [Tooltip("Camera-space size (metres, largest dimension) the spyglass model is fitted to when held.")]
        [SerializeField, Min(0.01f)] float propHeldSize = 0.32f;
        [Tooltip("How far below the held pose the spyglass starts — it rises up into view from here.")]
        [SerializeField, Min(0f)] float propStowDrop = 0.5f;

        SpyglassScopeView scopeView;
        bool playing;

        void OnEnable()
        {
            ResolveReferences();

            if (gameManager != null)
                gameManager.ItemUsed += OnItemUsed;
        }

        void OnDisable()
        {
            if (gameManager != null)
                gameManager.ItemUsed -= OnItemUsed;

            // Never strand the input lock if we're torn down mid-performance.
            if (playing)
                gameManager?.SetItemPresentationActive(false);
        }

        public void Configure(GameManager manager)
        {
            if (gameManager != null)
                gameManager.ItemUsed -= OnItemUsed;

            gameManager = manager;

            if (isActiveAndEnabled && gameManager != null)
                gameManager.ItemUsed += OnItemUsed;
        }

        void OnItemUsed(ItemDefinition item)
        {
            // Edit-mode tests apply effects with no performance; and never overlap two performances (the
            // GameManager lock also blocks a second use — this is the net for re-entrancy).
            if (item == null || !Application.isPlaying || playing)
                return;

            switch (item.Effect)
            {
                case ItemEffectKind.RevealTrueOdds:
                    StartCoroutine(PlaySpyglassReveal(item));
                    break;

                // Other items get their own bespoke beat here as they are built. Until then they simply
                // apply their effect with no performance (the previous behaviour), so nothing regresses.
                default:
                    break;
            }
        }

        IEnumerator PlaySpyglassReveal(ItemDefinition item)
        {
            ResolveReferences();
            playing = true;
            gameManager?.SetItemPresentationActive(true);

            // Bring the spyglass up to the eye: a prop held to the camera, rising into view from below.
            var prop = CreateHeldProp(item);
            var heldPos = propHeldLocalPos;
            var stowPos = propHeldLocalPos - Vector3.up * propStowDrop;
            var heldRot = Quaternion.Euler(propHeldEuler);

            if (prop != null)
                prop.transform.SetLocalPositionAndRotation(stowPos, heldRot);

            // 1. Raise the spyglass up to the eye.
            yield return AnimateProp(prop, stowPos, heldPos, heldRot, pickupSeconds, easeOut: true);

            // 2. The spyglass goes up against the lens and is gone — so it isn't in frame for the zoom.
            yield return VanishProp(prop, heldPos, heldRot, vanishSeconds);
            prop = null;

            // 3. Now push the camera into the glass close-up (front, straight on) and iris the scope over the
            // view, ticking the odds gauge up to the live spill chance — looking through the spyglass.
            cameraController?.FocusGlass(false);
            EnsureScopeView();
            scopeView.Show();

            for (var t = 0f; t < irisSeconds; t += Time.deltaTime)
            {
                var a = irisSeconds > 0f ? Mathf.SmoothStep(0f, 1f, t / irisSeconds) : 1f;
                scopeView.SetScopeAmount(a);
                PushLiveOdds(a);   // the number counts up as the scope closes in
                yield return null;
            }

            scopeView.SetScopeAmount(1f);

            // Hold on the reading so the player actually reads the odds the item bought.
            for (var t = 0f; t < readSeconds; t += Time.deltaTime)
            {
                PushLiveOdds(1f);
                yield return null;
            }

            // 4. Iris the scope back open and tear the overlay down — the read is a one-shot, nothing lingers.
            for (var t = 0f; t < irisSeconds; t += Time.deltaTime)
            {
                var a = irisSeconds > 0f ? 1f - Mathf.SmoothStep(0f, 1f, t / irisSeconds) : 0f;
                scopeView.SetScopeAmount(a);
                yield return null;
            }

            scopeView.Hide();

            // Hand the camera back to the player's resting framing.
            cameraController?.SwitchCamera(CameraState.PlayerFocus);

            gameManager?.SetItemPresentationActive(false);
            playing = false;
        }

        // Feed the scope gauge the live spill chance, scaled by how far the scope has engaged so it reads as
        // counting up. CurrentTrueSpillChance is already a 0..100 % (the chance the next pour rolls against).
        void PushLiveOdds(float reveal01)
        {
            if (scopeView == null || glassManager == null)
                return;

            var chance = glassManager.CurrentTrueSpillChance;
            var danger = Mathf.Clamp01(chance / GameConstants.MaxSpillChance);
            scopeView.SetOdds(chance * Mathf.Clamp01(reveal01), danger);
        }

        IEnumerator AnimateProp(GameObject prop, Vector3 from, Vector3 to, Quaternion rot, float seconds, bool easeOut)
        {
            if (prop == null || seconds <= 0f)
            {
                if (prop != null)
                    prop.transform.SetLocalPositionAndRotation(to, rot);

                yield break;
            }

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var f = Mathf.Clamp01(t / seconds);
                // Raise eases out so it lands softly at the eye; lower eases in as it drops away.
                var e = easeOut ? 1f - (1f - f) * (1f - f) : f * f;
                prop.transform.SetLocalPositionAndRotation(Vector3.LerpUnclamped(from, to, e), rot);
                yield return null;
            }

            prop.transform.SetLocalPositionAndRotation(to, rot);
        }

        // The spyglass is pressed up against the lens and shrinks away to nothing, so it's gone before the
        // camera zooms in — the player then looks "through" it at the glass with no prop in frame. Scaling
        // the (camera-parented) pivot needs no transparent material, so it works for any model.
        IEnumerator VanishProp(GameObject prop, Vector3 heldPos, Quaternion rot, float seconds)
        {
            if (prop == null)
                yield break;

            if (seconds <= 0f)
            {
                Destroy(prop);
                yield break;
            }

            // Drift toward the lens (smaller z = closer to the camera) while shrinking out.
            var towardLens = new Vector3(heldPos.x * 0.4f, heldPos.y * 0.4f, heldPos.z * 0.4f);
            var startScale = prop.transform.localScale;

            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                prop.transform.localPosition = Vector3.Lerp(heldPos, towardLens, e);
                prop.transform.localRotation = rot;
                prop.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, e);
                yield return null;
            }

            Destroy(prop);
        }

        // Instantiates the item's own wired model (the spyglass FBX) as a prop held to the camera, fitted to a
        // sensible held size and centred on its measured bounds. Null when there's no camera or no wired model
        // — the reveal still plays (camera + scope), just without a prop in hand.
        GameObject CreateHeldProp(ItemDefinition item)
        {
            var camTransform = Camera.main != null ? Camera.main.transform : null;
            var library = gameManager != null ? gameManager.ItemModels : null;

            if (camTransform == null || library == null || item == null)
                return null;

            if (!library.TryGetEntry(item.Id, out var entry) || entry.model == null)
                return null;

            GameObject model;

            try
            {
                model = Instantiate(entry.model);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[ItemUsePresentation] Could not instantiate held model for '{item.Id}': {exception.Message}");
                return null;
            }

            // A held prop is purely visual; strip any colliders the model brought along.
            foreach (var modelCollider in model.GetComponentsInChildren<Collider>())
                Destroy(modelCollider);

            if (entry.material != null)
            {
                var renderers = model.GetComponentsInChildren<Renderer>();
                for (var i = 0; i < renderers.Length; i++)
                    renderers[i].sharedMaterial = entry.material;
            }

            // Wrap the model in a pivot parented to the camera, so its transform pivots about its measured
            // centre (FBX pivots are often off-centre) and we can pose it in clean camera-local space.
            var pivot = new GameObject($"Held {item.Id}");
            pivot.transform.SetParent(camTransform, false);
            model.transform.SetParent(pivot.transform, true);

            FitProp(model, pivot.transform, propHeldSize);
            return pivot;
        }

        // Scale the model so its largest dimension matches the held size, then shift it so its measured centre
        // sits on the pivot origin (mirrors DeskItemBox.FitModelToBox). Camera scale is assumed ~1.
        void FitProp(GameObject model, Transform pivot, float targetSize)
        {
            model.transform.localScale = Vector3.one;

            var bounds = WorldBounds(model);
            var measured = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            var fit = measured > 1e-5f ? targetSize / measured : 1f;
            model.transform.localScale = Vector3.one * fit;

            var centered = WorldBounds(model);
            model.transform.position += pivot.position - centered.center;
        }

        static Bounds WorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        void EnsureScopeView()
        {
            if (scopeView == null)
                scopeView = SpyglassScopeView.Create();
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();

            if (cameraController == null)
                cameraController = FindAnyObjectByType<CameraController>();

            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();
        }
    }
}
