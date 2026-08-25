using System;
using Bloom.Utils;
using NUnit.Framework;

namespace BloomTests.Utils
{
#if DEBUG
    /// <summary>
    /// The one branch of BloomDebug.Fail that a test run can and must exercise. The other two
    /// cannot be: one needs a debugger attached, and the other puts up a modal dialog, which in an
    /// automated run would block until something killed the suite. That is precisely why the
    /// under-test branch exists, so it is worth pinning.
    ///
    /// This fixture compiles only in DEBUG, and the reason is the whole point of BloomDebug.Fail:
    /// [Conditional("DEBUG")] removes the CALL from the calling assembly -- which here is
    /// BloomTests itself. In a Release build of BloomTests the BloomDebug.Fail(...) inside each
    /// test below would simply not be emitted, nothing would throw, and Assert.Throws would fail.
    /// That failure would mean nothing: doing nothing is exactly what Fail is supposed to do in a
    /// release build. So in Release there is nothing to assert and the fixture stands down.
    ///
    /// Worth knowing, because it is easy to miss: build/agent-dotnet.sh builds Debug, but
    /// build/Bloom.proj defaults to Configuration=Release and the nightly runs BloomTests.dll out
    /// of output/Tests/Release/x64. Without this guard the nightly would be where it broke.
    /// </summary>
    [TestFixture]
    public class BloomDebugTests
    {
        /// <summary>
        /// Under test, Fail must throw rather than reach the MessageBox -- a modal dialog in an
        /// automated run blocks until something kills the suite.
        /// </summary>
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

        /// <summary>
        /// The message argument is optional, so the no-argument call must still produce something
        /// a developer can act on.
        /// </summary>
        [Test]
        public void Fail_UnderTestWithNoMessage_StillThrowsSomethingReadable()
        {
            var e = Assert.Throws<ApplicationException>(() => BloomDebug.Fail());
            Assert.That(e.Message, Does.Contain("BloomDebug.Fail"));
        }
    }
#endif
}
