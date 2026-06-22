using Meniscus.Gameplay;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class GlassSpillEffectTests
    {
        // Begin() starts a coroutine, which does not run reliably in EditMode (no play loop), so the
        // run-down animation is verified in-engine. RunDownProgress is pure and is unit-tested here.
        [Test]
        public void RunDownProgress_IsClampedZeroToOne_AndMonotonic()
        {
            Assert.AreEqual(0f, GlassSpillEffect.RunDownProgress(0f, 1f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(1f, 1f), 1e-4f);
            Assert.AreEqual(1f, GlassSpillEffect.RunDownProgress(5f, 1f), 1e-4f, "Past duration clamps to 1.");
            Assert.That(
                GlassSpillEffect.RunDownProgress(0.25f, 1f),
                Is.LessThan(GlassSpillEffect.RunDownProgress(0.75f, 1f)));
        }
    }
}
