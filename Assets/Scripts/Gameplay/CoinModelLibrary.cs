using System;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Maps each <see cref="CoinSize"/> to a coin model. Assign the Small/Medium/Big coin models here and
    /// the spawner shows the matching model instead of the placeholder cylinder. Any size left empty falls
    /// back to the primitive visuals, so a partly-filled library still works.
    /// </summary>
    [Serializable]
    public class CoinModelLibrary
    {
        [Tooltip("Model used for Small (Copper) coins.")]
        [SerializeField] GameObject smallCoinModel;
        [Tooltip("Model used for Medium (Silver) coins.")]
        [SerializeField] GameObject mediumCoinModel;
        [Tooltip("Model used for Large (Gold) coins.")]
        [SerializeField] GameObject largeCoinModel;
        [Tooltip("Uniform scale applied to every spawned coin model so they fit the table.")]
        [SerializeField] Vector3 modelScale = Vector3.one;

        public Vector3 ModelScale => modelScale == Vector3.zero ? Vector3.one : modelScale;

        public bool HasAnyModel =>
            smallCoinModel != null || mediumCoinModel != null || largeCoinModel != null;

        public GameObject GetModelForSize(CoinSize size) =>
            size switch
            {
                CoinSize.Small => smallCoinModel,
                CoinSize.Medium => mediumCoinModel,
                CoinSize.Large => largeCoinModel,
                _ => mediumCoinModel
            };
    }
}
