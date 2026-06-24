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

        [Test]
        public void ApplyModel_WithNullPrefab_KeepsPlaceholderVisible()
        {
            var coin = CreateRenderedCoin("Placeholder Coin");
            coin.Configure(CoinSize.Medium, 10f, 20, true);

            coin.ApplyModel(null, Vector3.one);

            Assert.IsTrue(coin.GetComponent<MeshRenderer>().enabled,
                "A coin with no assigned model must keep its placeholder visible.");
            Assert.IsNull(coin.transform.Find("Coin Model"),
                "No model child should be created when the model prefab is null.");

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void ApplyModel_WithValidPrefab_ShowsModelAndHidesPlaceholder()
        {
            var coin = CreateRenderedCoin("Modeled Coin");
            coin.Configure(CoinSize.Medium, 10f, 20, true);
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            coin.ApplyModel(prefab, Vector3.one);

            Assert.IsFalse(coin.GetComponent<MeshRenderer>().enabled,
                "The placeholder must hide once a real model is showing.");
            Assert.IsNotNull(coin.transform.Find("Coin Model"),
                "A model child should be instantiated for a valid prefab.");

            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void ApplyModel_SwappingModelBackToNull_RestoresPlaceholder()
        {
            var coin = CreateRenderedCoin("Swapped Coin");
            coin.Configure(CoinSize.Medium, 10f, 20, true);
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coin.ApplyModel(prefab, Vector3.one);

            coin.ApplyModel(null, Vector3.one);

            Assert.IsTrue(coin.GetComponent<MeshRenderer>().enabled,
                "Clearing the model must restore the placeholder so the coin never goes invisible.");
            Assert.IsNull(coin.transform.Find("Coin Model"),
                "The old model child must be destroyed when the model is cleared.");

            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void Configure_ReplacesBulgingCapsuleWithFlatBoxCollider()
        {
            var coin = CreateRenderedCoin("Collider Coin");
            Assert.IsNotNull(coin.GetComponent<CapsuleCollider>(),
                "A primitive cylinder starts with the bulging CapsuleCollider this fix is meant to replace.");

            coin.Configure(CoinSize.Medium, 10f, 20, true);

            Assert.IsNull(coin.GetComponent<CapsuleCollider>(),
                "The capsule collider (which squashes into an oversized sphere) must be removed.");
            var box = coin.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "A flat BoxCollider must back the click target.");
            Assert.AreEqual(Vector3.one, box.size);
            Assert.AreEqual(Vector3.zero, box.center);

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void ApplyModel_CentersAnOffPivotModelOnTheCoin()
        {
            var coin = CreateRenderedCoin("Centered Coin");
            coin.transform.position = new Vector3(1f, 2f, 3f);
            coin.Configure(CoinSize.Medium, 10f, 20, true);

            // A model whose visible mesh sits far from its own pivot — the case that made coins render
            // offset from their collider.
            var prefab = new GameObject("Off-Pivot Model");
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(prefab.transform);
            mesh.transform.localPosition = new Vector3(5f, 3f, -4f);

            coin.ApplyModel(prefab, Vector3.one);

            Assert.IsNotNull(coin.ActiveModel, "A model should be showing after ApplyModel.");
            var bounds = WorldBounds(coin.ActiveModel);
            Assert.AreEqual(coin.transform.position.x, bounds.center.x, 0.01f, "Model X must center on the coin.");
            Assert.AreEqual(coin.transform.position.y, bounds.center.y, 0.01f, "Model Y must center on the coin.");
            Assert.AreEqual(coin.transform.position.z, bounds.center.z, 0.01f, "Model Z must center on the coin.");

            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(coin.gameObject);
        }

        static Bounds WorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
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
