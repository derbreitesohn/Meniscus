using Meniscus.Core;
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class ShopBrowseGateTests
    {
        [Test]
        public void ComputeBrowseToggle_FromClosed_OpensReadOnlyPreview()
        {
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(false, false);

            Assert.IsTrue(isOpen, "Clicking the closed book opens it.");
            Assert.IsTrue(previewMode, "Mid-round it opens as a read-only preview.");
        }

        [Test]
        public void ComputeBrowseToggle_FromPreview_Closes()
        {
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(true, true);

            Assert.IsFalse(isOpen, "Clicking while previewing closes the book.");
            Assert.IsFalse(previewMode);
        }

        [Test]
        public void ComputeBrowseToggle_FromShopPhaseOpen_IsUnchanged()
        {
            // Shop-phase open is (isOpen=true, previewMode=false): a click is ignored
            // (the player closes it via "Finish Drink").
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(true, false);

            Assert.IsTrue(isOpen);
            Assert.IsFalse(previewMode);
        }

        [Test]
        public void BrowsingAllowed_DuringRoundStates_IsTrue()
        {
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.StartRound));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.PlayerTurn));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.EnemyTurn));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.Resolution));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.RestockPhase));
        }

        [Test]
        public void BrowsingAllowed_InShopPhaseOrGameOver_IsFalse()
        {
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.ShopPhase));
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.GameOver));
        }
    }
}
