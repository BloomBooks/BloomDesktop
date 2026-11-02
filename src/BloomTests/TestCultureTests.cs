using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Guards TestCulture against becoming a no-op. It sets the culture in a [SetUpFixture], but
    /// NUnit re-establishes each test's culture from its own execution context, so a future NUnit
    /// could quietly undo that, and the tests would run in the machine's culture instead of the
    /// invariant one Bloom runs in. The BLOOM_TEST_CULTURE cases fail if the variable is set but
    /// did not reach UserCulture.Current; with it unset they don't exist at all, so nothing is
    /// reported as passed, failed or ignored.
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

        [Test]
        public void CurrentCulture_InsideATest_IsInvariant()
        {
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo(""));
        }

        [Test]
        public async Task CurrentCulture_OnAWorkerThread_IsInvariant()
        {
            // Production code under test often does its work on the thread pool.
            var onWorker = await Task.Run(() => CultureInfo.CurrentCulture.Name);
            Assert.That(onWorker, Is.EqualTo(""));
        }

        [Test]
        public void CheckProcessCultureIsInvariant_InsideATest_DoesNotThrow()
        {
            Assert.DoesNotThrow(UserCulture.CheckProcessCultureIsInvariant);
        }

        [TestCaseSource(nameof(RequestedCulture))]
        public void UserCulture_InsideATest_IsTheRequestedCulture(string requested)
        {
            Assert.That(UserCulture.Current.Name, Is.EqualTo(requested));
        }

        [TestCaseSource(nameof(RequestedCulture))]
        public async Task UserCulture_OnAWorkerThread_IsTheRequestedCulture(string requested)
        {
            var onWorker = await Task.Run(() => UserCulture.Current.Name);
            Assert.That(onWorker, Is.EqualTo(requested));
        }
    }
}
