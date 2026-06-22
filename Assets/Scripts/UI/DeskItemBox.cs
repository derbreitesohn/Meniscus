using System.Collections;
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// A placeholder cube on the desk standing in for one held item stack. Visuals only: it shows the
    /// item name + count, raises when selected and reveals Use/Cancel tiles, and relays all clicks to
    /// the owning <see cref="DeskItemTray"/>, which makes every decision (select, use, turn-gating).
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemBox : MonoBehaviour
    {
        const float RaiseHeight = 0.02f;
        const float FlashSeconds = 0.3f;

        DeskItemTray tray;
        TextMesh label;
        GameObject useTile;
        GameObject cancelTile;
        Renderer bodyRenderer;
        Vector3 restLocalPos;
        Color restColor;
        Coroutine flash;

        public ItemDefinition Item { get; private set; }
        public int Count { get; private set; }
        public bool IsSelected { get; private set; }

        public void Initialize(DeskItemTray owner, ItemDefinition item, TextMesh labelText,
            GameObject use, GameObject cancel, Renderer body)
        {
            tray = owner;
            Item = item;
            label = labelText;
            useTile = use;
            cancelTile = cancel;
            bodyRenderer = body;

            if (bodyRenderer != null)
                restColor = bodyRenderer.material.color;

            SetSelected(false);
        }

        public void SetCount(int count)
        {
            Count = count;

            if (label != null && Item != null)
                label.text = $"{Item.DisplayName.ToUpperInvariant()}\nx{count}";
        }

        /// <summary>Sets the box's resting local position (its row slot); preserves the raised offset.</summary>
        public void SetRestPosition(Vector3 localPos)
        {
            restLocalPos = localPos;
            transform.localPosition = IsSelected ? localPos + Vector3.up * RaiseHeight : localPos;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;

            if (useTile != null) useTile.SetActive(selected);
            if (cancelTile != null) cancelTile.SetActive(selected);

            transform.localPosition = selected ? restLocalPos + Vector3.up * RaiseHeight : restLocalPos;
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
            bodyRenderer.material.color = new Color(0.7f, 0.2f, 0.2f);
            var elapsed = 0f;

            while (elapsed < FlashSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            bodyRenderer.material.color = restColor;
            flash = null;
        }

        public void NotifySelectClicked() => tray?.OnBoxClicked(this);
        public void NotifyUseClicked() => tray?.OnUseClicked(this);
        public void NotifyCancelClicked() => tray?.OnCancelClicked(this);
    }
}
