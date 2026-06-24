using System;
using UnityEngine;

namespace Meniscus.Items
{
    /// <summary>
    /// Maps an item id to the 3D model shown on the desk for that held item (e.g. the spyglass for
    /// "bartenders_spectacles", the bandana for "step_outside"). Mirrors
    /// <see cref="Meniscus.Gameplay.CoinModelLibrary"/>: any item with no entry falls back to the
    /// placeholder cube box, so a partly-filled library still works. The optional per-entry material is
    /// forced onto the instantiated model's renderers, so the painted look is guaranteed even when the FBX
    /// import has not bound its material itself. Authored inline on the <c>GameManager</c> and populated by
    /// Tools ▸ Meniscus ▸ Wire Item Models (so no Resources lookup and nothing extra in the build).
    /// </summary>
    [Serializable]
    public class ItemModelLibrary
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Item id this model stands for (e.g. 'bartenders_spectacles').")]
            public string itemId;
            [Tooltip("FBX / prefab shown on the desk for this item.")]
            public GameObject model;
            [Tooltip("Optional material forced onto the model's renderers; leave empty to keep the model's own.")]
            public Material material;
        }

        [SerializeField] Entry[] entries = Array.Empty<Entry>();
        [Tooltip("Uniform scale tuning applied to every item model so it fits the desk box (1 = exact box size).")]
        [SerializeField] Vector3 modelScale = Vector3.one;

        public Vector3 ModelScale => modelScale == Vector3.zero ? Vector3.one : modelScale;

        public bool HasAnyModel
        {
            get
            {
                if (entries == null)
                    return false;

                for (var i = 0; i < entries.Length; i++)
                    if (entries[i].model != null)
                        return true;

                return false;
            }
        }

        /// <summary>
        /// Finds the entry for <paramref name="itemId"/>. Returns true only when a model is actually wired,
        /// so a stray id with an empty model slot still falls back to the placeholder box.
        /// </summary>
        public bool TryGetEntry(string itemId, out Entry entry)
        {
            if (!string.IsNullOrEmpty(itemId) && entries != null)
            {
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].itemId == itemId)
                    {
                        entry = entries[i];
                        return entry.model != null;
                    }
                }
            }

            entry = default;
            return false;
        }

        public GameObject GetModelForItem(string itemId) =>
            TryGetEntry(itemId, out var entry) ? entry.model : null;
    }
}
