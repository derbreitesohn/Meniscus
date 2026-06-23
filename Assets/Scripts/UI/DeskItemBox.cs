using System.Collections;
using Meniscus.Gameplay;
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// One held item stack on the desk. Visuals only: it shows the item name + count, raises when
    /// selected and reveals Use/Cancel tiles, and relays all clicks to the owning
    /// <see cref="DeskItemTray"/>, which makes every decision (select, use, turn-gating).
    ///
    /// The parts (body renderer, label, Use/Cancel tiles) are SERIALIZED so a box authored as a prefab
    /// is self-contained: the tray instantiates that prefab per stack and only needs to bind it to a
    /// tray + item via <see cref="Initialize"/>. The placeholder cube box built in code goes through
    /// the same fields via <see cref="Bind"/>, so a designer can replace the cube with a modelled box
    /// (mesh, materials, Animator, lighting) in the prefab without touching this logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemBox : MonoBehaviour
    {
        // How far a selected box lifts above its row slot. Sits just above the coin's own selection
        // lift so a held item reads as clearly "picked up", not nudged.
        const float RaiseHeight = 0.14f;
        // Exponential ease rate toward the raised/rest height; higher = snappier. The lift glides in
        // Update instead of popping.
        const float RaiseLerpSpeed = 14f;
        const float FlashSeconds = 0.3f;

        [SerializeField] TextMesh label;
        [SerializeField] GameObject useTile;
        [SerializeField] GameObject cancelTile;
        [SerializeField] Renderer bodyRenderer;
        [SerializeField] Color restColor = new(0.55f, 0.4f, 0.25f);

        DeskItemTray tray;
        Vector3 restLocalPos;
        Coroutine flash;
        SelectionGlow glow;

        public ItemDefinition Item { get; private set; }
        public int Count { get; private set; }
        public bool IsSelected { get; private set; }

        /// <summary>
        /// Binds this box to its parts. Called by <see cref="DeskItemTrayBuilder"/> while building the
        /// placeholder box (in code at runtime, or once when the authoring tool freezes it as a prefab),
        /// so the same serialized references back both the runtime-built and prefab-instanced box.
        /// </summary>
        public void Bind(TextMesh labelText, GameObject use, GameObject cancel, Renderer body, Color color)
        {
            label = labelText;
            useTile = use;
            cancelTile = cancel;
            bodyRenderer = body;
            restColor = color;
        }

        /// <summary>
        /// Attaches a (built or prefab-instanced) box to its owner tray and the item it stands for. Parts
        /// come from the serialized references, so this works equally for the code-built box and an
        /// authored prefab instance.
        /// </summary>
        public void Initialize(DeskItemTray owner, ItemDefinition item)
        {
            tray = owner;
            Item = item;

            // Apply the placeholder tint via the renderer's property block (no material asset is created,
            // so edit-mode tests don't leak a material — see DeskItemTrayBuilder.ApplyColor).
            DeskItemTrayBuilder.ApplyColor(bodyRenderer, restColor);

            SetSelected(false);
        }

        public void SetCount(int count)
        {
            Count = count;

            if (label != null && Item != null)
                label.text = $"{Item.DisplayName.ToUpperInvariant()}\nx{count}";
        }

        // The slot the box eases toward: its row position, lifted by RaiseHeight while selected.
        Vector3 TargetLocalPos => IsSelected ? restLocalPos + Vector3.up * RaiseHeight : restLocalPos;

        /// <summary>Sets the box's resting local position (its row slot); preserves the raised offset.</summary>
        public void SetRestPosition(Vector3 localPos)
        {
            restLocalPos = localPos;
            // Snap to the slot: this is a structural (re)placement, not a selection change, so it should
            // not slide in from a stale position.
            transform.localPosition = TargetLocalPos;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;

            // The shared desk USE button replaces per-box Use/Cancel; keep any tiles a prefab still
            // carries hidden, and show the lift + glow instead.
            if (useTile != null) useTile.SetActive(false);
            if (cancelTile != null) cancelTile.SetActive(false);

            // In play mode Update eases the lift for a smooth pick-up; outside play mode (edit-mode
            // tests, no Update tick) set the height immediately so the box is positioned correctly.
            if (!Application.isPlaying)
                transform.localPosition = TargetLocalPos;

            EnsureGlow();
            glow?.SetActive(selected);
        }

        void Update()
        {
            // Ease toward the selected/rest height. Unscaled so the lift still glides during the
            // slow-motion verdict, matching the selection glow's pulse.
            var t = 1f - Mathf.Exp(-RaiseLerpSpeed * Time.unscaledDeltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, TargetLocalPos, t);
        }

        // The glow is a standalone follower object built on first use (see SelectionGlow), so the box's
        // own transform never distorts it.
        void EnsureGlow()
        {
            if (glow == null)
                glow = SelectionGlow.Attach(
                    transform,
                    DeskItemTrayBuilder.BoxSize * 1.45f,
                    new Color(1f, 0.82f, 0.35f, 1f),
                    outline: true);
        }

        /// <summary>Brief red blink when Use is pressed off-turn (placeholder "not your turn" cue).</summary>
        public void FlashUnavailable()
        {
            if (bodyRenderer == null || !gameObject.activeInHierarchy)
                return;

            if (flash != null)
                StopCoroutine(flash);

            flash = StartCoroutine(FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            DeskItemTrayBuilder.ApplyColor(bodyRenderer, new Color(0.7f, 0.2f, 0.2f));
            var elapsed = 0f;

            while (elapsed < FlashSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            DeskItemTrayBuilder.ApplyColor(bodyRenderer, restColor);
            flash = null;
        }

        public void NotifySelectClicked() => tray?.OnBoxClicked(this);
        public void NotifyUseClicked() => tray?.OnUseClicked(this);
        public void NotifyCancelClicked() => tray?.OnCancelClicked(this);
    }
}
