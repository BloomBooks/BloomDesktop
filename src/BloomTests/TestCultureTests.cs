using System;
using System.Collections.Generic;
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
    /// reach the code under test. With the variable unset there is nothing to check, so no test
    /// cases exist at all: nothing is reported as passed, failed or ignored.
    /// </summary>
    [TestFixture]
    public class TestCultureTests
    {
        /// <summary>
        /// The requested culture's name as the single test case, or no test cases at all when
        /// BLOOM_TEST_CULTURE is unset.
        /// </summary>
        private static IEnumerable<string> RequestedCulture()
        {
            var name = Environment.GetEnvironmentVariable(TestCulture.kEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(name))
                yield return CultureInfo.GetCultureInfo(name.Trim()).Name;
        }

        [TestCaseSource(nameof(RequestedCulture))]
        public void CurrentCulture_InsideATest_IsTheRequestedCulture(string requested)
        {
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo(requested));
        }

        [TestCaseSource(nameof(RequestedCulture))]
        public async Task CurrentCulture_OnAWorkerThread_IsTheRequestedCulture(string requested)
        {
            // Production code under test often does its work on the thread pool.
            var onWorker = await Task.Run(() => CultureInfo.CurrentCulture.Name);
            Assert.That(onWorker, Is.EqualTo(requested));
        }
    }
}
