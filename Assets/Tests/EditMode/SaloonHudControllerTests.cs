using Meniscus.Core;
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class SaloonHudControllerTests
    {
        [Test]
        public void BuildStatusLine_WithoutReveal_HasNoSpillSuffix()
        {
            var line = SaloonHudController.BuildStatusLine(
                GameState.PlayerTurn, 1, 3, 20f, 0, 0, 8, 8);

            Assert.IsFalse(line.Contains("Spill"));
        }

        [Test]
        public void BuildStatusLine_WithReveal_AppendsSpillPercent()
        {
            var line = SaloonHudController.BuildStatusLine(
                GameState.PlayerTurn, 1, 3, 20f, 0, 0, 8, 8,
                revealTrueOdds: true, trueSpillChance: 23.4f);

            Assert.IsTrue(line.Contains("Spill 23%"));
        }
    }
}
