using Meniscus.Core;
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class CoinVisualTests
    {
        [Test]
        public void Configure_AppliesDistinctScaleForEachCoinSize()
        {
            var small = CreateRenderedCoin("Small Coin");
            var medium = CreateRenderedCoin("Medium Coin");
            var large = CreateRenderedCoin("Large Coin");

            small.Configure(CoinSize.Small, 5f, 10, true);
            medium.Configure(CoinSize.Medium, 10f, 20, true);
            large.Configure(CoinSize.Large, 15f, 30, true);

            Assert.AreEqual(GameConstants.GetVisualScaleForSize(CoinSize.Small), small.transform.localScale);
            Assert.AreEqual(GameConstants.GetVisualScaleForSize(CoinSize.Medium), medium.transform.localScale);
            Assert.AreEqual(GameConstants.GetVisualScaleForSize(CoinSize.Large), large.transform.localScale);
            Assert.Less(small.transform.localScale.x, medium.transform.localScale.x);
            Assert.Less(medium.transform.localScale.x, large.transform.localScale.x);

            Object.DestroyImmediate(small.gameObject);
            Object.DestroyImmediate(medium.gameObject);
            Object.DestroyImmediate(large.gameObject);
        }

        [Test]
        public void Configure_AppliesCopperSilverGoldColorsForCoinSizes()
        {
            var small = CreateRenderedCoin("Copper Coin");
            var medium = CreateRenderedCoin("Silver Coin");
            var large = CreateRenderedCoin("Gold Coin");

            small.Configure(CoinSize.Small, 5f, 10, true);
            medium.Configure(CoinSize.Medium, 10f, 20, true);
            large.Configure(CoinSize.Large, 15f, 30, true);

            AssertColorsApproximatelyEqual(GameConstants.GetMaterialColorForSize(CoinSize.Small), small.GetComponent<Renderer>().sharedMaterial.color);
            AssertColorsApproximatelyEqual(GameConstants.GetMaterialColorForSize(CoinSize.Medium), medium.GetComponent<Renderer>().sharedMaterial.color);
            AssertColorsApproximatelyEqual(GameConstants.GetMaterialColorForSize(CoinSize.Large), large.GetComponent<Renderer>().sharedMaterial.color);

            Object.DestroyImmediate(small.gameObject);
            Object.DestroyImmediate(medium.gameObject);
            Object.DestroyImmediate(large.gameObject);
        }

        [Test]
        public void GetDisplayNameForSize_MapsSmallMediumLargeToCopperSilverGold()
        {
            Assert.AreEqual("Copper", GameConstants.GetDisplayNameForSize(CoinSize.Small));
            Assert.AreEqual("Silver", GameConstants.GetDisplayNameForSize(CoinSize.Medium));
            Assert.AreEqual("Gold", GameConstants.GetDisplayNameForSize(CoinSize.Large));
        }

        static Coin CreateRenderedCoin(string name)
        {
            var coinObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coinObject.name = name;
            return coinObject.AddComponent<Coin>();
        }

        static void AssertColorsApproximatelyEqual(Color expected, Color actual)
        {
            const float tolerance = 0.01f;
            Assert.AreEqual(expected.r, actual.r, tolerance);
            Assert.AreEqual(expected.g, actual.g, tolerance);
            Assert.AreEqual(expected.b, actual.b, tolerance);
            Assert.AreEqual(expected.a, actual.a, tolerance);
        }
    }
}
