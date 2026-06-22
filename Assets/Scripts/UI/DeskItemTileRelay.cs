using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Sits on a box's body / Use / Cancel collider and relays its click to the owning
    /// <see cref="DeskItemBox"/>. Keeps the box component free of per-collider click wiring and
    /// mirrors the book's <c>BookClickTarget</c> relay approach. Clicks are delivered by
    /// <see cref="DeskItemTray"/>'s Input System raycast (legacy OnMouse* messages do not fire under
    /// this project's input backend), so this type only carries its box + role.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskItemTileRelay : MonoBehaviour
    {
        public enum Kind { Body, Use, Cancel }

        DeskItemBox box;
        Kind kind;

        public void Initialize(DeskItemBox owner, Kind tileKind)
        {
            box = owner;
            kind = tileKind;
        }

        /// <summary>Routes a click on this tile to the owning box. Called by the tray's raycast.</summary>
        public void Trigger()
        {
            if (box == null)
                return;

            switch (kind)
            {
                case Kind.Body: box.NotifySelectClicked(); break;
                case Kind.Use: box.NotifyUseClicked(); break;
                case Kind.Cancel: box.NotifyCancelClicked(); break;
            }
        }
    }
}
