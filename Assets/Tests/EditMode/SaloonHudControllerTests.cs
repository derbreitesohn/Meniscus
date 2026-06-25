using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class SaloonHudControllerTests
    {
        [Test]
        public void BuildStatusLine_ShowsRoundProgressHeader()
        {
            var line = SaloonHudController.BuildStatusLine(2, 3, "Your turn");

            StringAssert.Contains("ROUND 2 / 3", line);
        }

        [Test]
        public void BuildStatusLine_ShowsTurnLabelWhenProvided()
        {
            var line = SaloonHudController.BuildStatusLine(2, 3, "Dealer's turn");

            StringAssert.Contains("Dealer's turn", line);
        }

        [Test]
        public void BuildStatusLine_OmitsSecondLineWhenTurnLabelEmpty()
        {
            var line = SaloonHudController.BuildStatusLine(2, 3, string.Empty);

            Assert.IsFalse(line.Contains("\n"));
        }

        [Test]
        public void BuildStatusLine_ShowsNoSpillOddsRiskOrMoney()
        {
            var line = SaloonHudController.BuildStatusLine(2, 3, "Your turn");

            Assert.IsFalse(line.Contains("Spill"));
            Assert.IsFalse(line.Contains("Risk"));
            Assert.IsFalse(line.Contains("$"));
        }
    }
}
