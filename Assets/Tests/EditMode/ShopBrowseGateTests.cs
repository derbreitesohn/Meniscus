using Meniscus.Core;
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class ShopBrowseGateTests
    {
        [Test]
        public void ComputeBrowseToggle_FromClosed_OpensMidRoundShop()
        {
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(false, false);

            Assert.IsTrue(isOpen, "Clicking the closed book opens it.");
            Assert.IsTrue(previewMode, "Mid-round it opens flagged as a mid-round open (buyable; closing just sets it down).");
        }

        [Test]
        public void ComputeBrowseToggle_FromMidRoundOpen_Closes()
        {
            var (isOpen, previewMode) = BookShopView.ComputeBrowseToggle(true, true);

            Assert.IsFalse(isOpen, "Clicking the open mid-round book sets it back down.");
            Assert.IsFalse(previewMode);
        }

        [Test]
        public void PurchasingAllowedInMode_BothMidRoundAndShopPhase_IsTrue()
        {
            Assert.IsTrue(BookShopView.PurchasingAllowedInMode(previewMode: true),
                "Opening the book mid-round can buy (spends banked cash), not just browse.");
            Assert.IsTrue(BookShopView.PurchasingAllowedInMode(previewMode: false),
                "The between-rounds shop phase can buy.");
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
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.Resolution));
            Assert.IsTrue(ShopManager.BrowsingAllowed(GameState.RestockPhase));
        }

        [Test]
        public void BrowsingAllowed_DuringDealerTurnRoundWonShopPhaseOrGameOver_IsFalse()
        {
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.EnemyTurn));
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.RoundWon));
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.ShopPhase));
            Assert.IsFalse(ShopManager.BrowsingAllowed(GameState.GameOver));
        }
    }
}
