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
        // The description has no auto-wrap (TextMesh never wraps), so it's broken into short lines for a
        // tidy card block.
        const int DescriptionWrapChars = 22;
        // Breathing room added around the measured text when sizing the backing plate.
        const float CardBackingPadding = 0.04f;

        [SerializeField] TextMesh label;
        // The name + description card, parented above the box. Hidden at rest; shown only while selected.
        [SerializeField] GameObject descriptionCard;
        // Dark plate behind the card text; auto-sized to the text in play mode (see FitBackingToText).
        [SerializeField] Transform cardBacking;
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
        /// While true the delivery hand owns this box's transform (it's being carried onto the desk), so
        /// the box's own slot-easing pauses and <see cref="SetRestPosition"/> records the slot without
        /// snapping there. The hand clears this when it sets the box down. See <see cref="DeskHandDelivery"/>.
        /// </summary>
        public bool IsBeingDelivered { get; set; }

        /// <summary>
        /// The fixed layout slot this box occupies, assigned once when the box is created and held for
        /// its lifetime. A box never changes slot, so using or removing another item never shifts it —
        /// the tray maps slot -> position independently of how many boxes are present.
        /// </summary>
        public int Slot { get; set; } = -1;

        /// <summary>
        /// Binds this box to its parts. Called by <see cref="DeskItemTrayBuilder"/> while building the
        /// placeholder box (in code at runtime, or once when the authoring tool freezes it as a prefab),
        /// so the same serialized references back both the runtime-built and prefab-instanced box.
        /// </summary>
        public void Bind(TextMesh labelText, GameObject card, Transform backing, Renderer body, Color color)
        {
            label = labelText;
            descriptionCard = card;
            cardBacking = backing;
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

            RefreshCard();
            SetSelected(false);
        }

        public void SetCount(int count)
        {
            Count = count;
            RefreshCard();
        }

        // Composes the card shown on selection: a bold name + count header over the item's wrapped
        // description ("what it does"). Written even while the card is hidden so it's ready on the next
        // pick-up. Rich text (the &lt;b&gt; header) needs richText enabled on the TextMesh (the builder does).
        void RefreshCard()
        {
            if (label == null || Item == null)
                return;

            var header = $"<b>{Item.DisplayName.ToUpperInvariant()}</b>   x{Count}";
            var body = Wrap(Item.Description, DescriptionWrapChars);

            label.text = string.IsNullOrEmpty(body) ? header : $"{header}\n{body}";
        }

        // Greedy word wrap to at most maxChars per line (TextMesh has no wrapping of its own).
        static string Wrap(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var builder = new System.Text.StringBuilder(text.Length + 8);
            var lineLength = 0;

            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0)
                    continue;

                if (lineLength > 0 && lineLength + 1 + word.Length > maxChars)
                {
                    builder.Append('\n');
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    builder.Append(' ');
                    lineLength++;
                }

                builder.Append(word);
                lineLength += word.Length;
            }

            return builder.ToString();
        }

        // The slot the box eases toward: its row position, lifted by RaiseHeight while selected.
        Vector3 TargetLocalPos => IsSelected ? restLocalPos + Vector3.up * RaiseHeight : restLocalPos;

        /// <summary>Sets the box's resting local position (its row slot); preserves the raised offset.</summary>
        public void SetRestPosition(Vector3 localPos)
        {
            restLocalPos = localPos;

            // While the hand is carrying this box in, it drives the transform — record the slot but don't
            // teleport there. The hand snaps the box to the slot when it sets it down.
            if (IsBeingDelivered)
                return;

            // Snap to the slot: this is a structural (re)placement, not a selection change, so it should
            // not slide in from a stale position.
            transform.localPosition = TargetLocalPos;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;

            // The name + description card pops up only while the box is picked up; the desk stays clear
            // at rest. The shared desk USE button (not per-box tiles) commits the selection.
            if (descriptionCard != null)
                descriptionCard.SetActive(selected);

            // In play mode Update eases the lift for a smooth pick-up; outside play mode (edit-mode
            // tests, no Update tick) set the height immediately so the box is positioned correctly.
            if (!Application.isPlaying)
                transform.localPosition = TargetLocalPos;

            EnsureGlow();
            glow?.SetActive(selected);
        }

        void Update()
        {
            // The delivery hand owns the transform while it carries the box in — don't fight it.
            if (IsBeingDelivered)
                return;

            // Ease toward the selected/rest height. Unscaled so the lift still glides during the
            // slow-motion verdict, matching the selection glow's pulse.
            var t = 1f - Mathf.Exp(-RaiseLerpSpeed * Time.unscaledDeltaTime);
            transform.localPosition = Vector3.Lerp(transform.localPosition, TargetLocalPos, t);
        }

        // Keep the backing plate hugging the (variable-length) card text while it's up. Play mode only:
        // the text geometry the fit measures is generated during rendering, which edit-mode lacks.
        void LateUpdate()
        {
            if (IsSelected && Application.isPlaying)
                FitBackingToText();
        }

        // Size the plate to the rendered text's world bounds, converted back through the plate parent's
        // scale, so it fits whatever description the item carries instead of a guessed fixed size.
        void FitBackingToText()
        {
            if (cardBacking == null || label == null)
                return;

            var textRenderer = label.GetComponent<Renderer>();
            if (textRenderer == null)
                return;

            var size = textRenderer.bounds.size;
            var parentScale = cardBacking.parent != null ? cardBacking.parent.lossyScale : Vector3.one;

            cardBacking.localScale = new Vector3(
                (size.x + CardBackingPadding) / Mathf.Max(Mathf.Abs(parentScale.x), 1e-4f),
                (size.y + CardBackingPadding) / Mathf.Max(Mathf.Abs(parentScale.y), 1e-4f),
                cardBacking.localScale.z);
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
