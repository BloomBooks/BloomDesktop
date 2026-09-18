using System;
using System.Globalization;
using System.Threading.Tasks;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Guards the culture sweep against becoming a no-op. TestCulture sets the culture in a
    /// [SetUpFixture], but NUnit re-establishes each test's culture from its own execution context,
    /// so a future NUnit could quietly undo that switch and the weekly culture-sweep workflow would
    /// go green while testing nothing. These tests fail if BLOOM_TEST_CULTURE is set but did not
    /// reach the code under test; with the variable unset they are ignored.
    /// </summary>
    [TestFixture]
    public class TestCultureTests
    {
        private static CultureInfo RequestedCultureOrIgnore()
        {
            var name = Environment.GetEnvironmentVariable(TestCulture.kEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(name))
                Assert.Ignore($"{TestCulture.kEnvironmentVariable} is not set; nothing to check.");
            return CultureInfo.GetCultureInfo(name.Trim());
        }

        [Test]
        public void CurrentCulture_InsideATest_IsTheRequestedCulture()
        {
            var requested = RequestedCultureOrIgnore();
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo(requested.Name));
        }

        [Test]
        public async Task CurrentCulture_OnAWorkerThread_IsTheRequestedCulture()
        {
            var requested = RequestedCultureOrIgnore();
            // Production code under test often does its work on the thread pool.
            var onWorker = await Task.Run(() => CultureInfo.CurrentCulture.Name);
            Assert.That(onWorker, Is.EqualTo(requested.Name));
        }
    }
}
