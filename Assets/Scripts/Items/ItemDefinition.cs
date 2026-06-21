using UnityEngine;

namespace Meniscus.Items
{
    /// <summary>
    /// Data for one purchasable item. Authorable as a ScriptableObject asset (Create > Meniscus > Item
    /// Definition) so designers can add/tune items without code, and also constructible at runtime via
    /// <see cref="Create"/> for the code-built default catalog. Behaviour lives in
    /// <see cref="ItemEffectApplier"/>; this type is pure data.
    /// </summary>
    [CreateAssetMenu(menuName = "Meniscus/Item Definition", fileName = "Item")]
    public class ItemDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName = "New Item";
        [SerializeField, TextArea] string description;
        [SerializeField, Min(0)] int cost = 25;
        [SerializeField] ItemEffectKind effect = ItemEffectKind.PayoutMultiplier;
        [SerializeField] float magnitude = 2f;
        [SerializeField] Sprite icon;

        public string Id => string.IsNullOrEmpty(id) ? displayName : id;
        public string DisplayName => displayName;
        public string Description => description;
        public int Cost => cost;
        public ItemEffectKind Effect => effect;
        public float Magnitude => magnitude;
        public Sprite Icon => icon;

        public static ItemDefinition Create(
            string id,
            string displayName,
            string description,
            int cost,
            ItemEffectKind effect,
            float magnitude)
        {
            var item = CreateInstance<ItemDefinition>();
            item.id = id;
            item.displayName = displayName;
            item.description = description;
            item.cost = Mathf.Max(0, cost);
            item.effect = effect;
            item.magnitude = magnitude;
            item.name = string.IsNullOrEmpty(id) ? displayName : id;
            return item;
        }
    }
}
