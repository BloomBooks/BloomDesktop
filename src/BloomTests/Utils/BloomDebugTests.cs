using System;
using Bloom.Utils;
using NUnit.Framework;

namespace BloomTests.Utils
{
    /// <summary>
    /// The one branch of BloomDebug.Fail that a test run can and must exercise. The other two
    /// cannot be: one needs a debugger attached, and the other puts up a modal dialog, which in an
    /// automated run would block until something killed the suite. That is precisely why the
    /// under-test branch exists, so it is worth pinning.
    /// </summary>
    [TestFixture]
    public class BloomDebugTests
    {
        [Test]
        public void Fail_UnderTest_ThrowsRatherThanShowingADialog()
        {
            var e = Assert.Throws<ApplicationException>(() =>
                BloomDebug.Fail("a definite programming error")
            );
            Assert.That(
                e.Message,
                Does.Contain("a definite programming error"),
                "the message a developer passed in is what makes the failure worth reading, so it "
                    + "has to survive into the exception"
            );
        }

        [Test]
        public void Fail_UnderTestWithNoMessage_StillThrowsSomethingReadable()
        {
            var e = Assert.Throws<ApplicationException>(() => BloomDebug.Fail());
            Assert.That(e.Message, Does.Contain("BloomDebug.Fail"));
        }
    }
}
