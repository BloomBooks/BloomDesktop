using System;
using System.Globalization;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Runs the tests the way Bloom runs: with the process culture set to the invariant culture by
    /// UserCulture.UseInvariantCulture, and the user's regional format kept in UserCulture.Current
    /// (BL-16981).
    ///
    /// Setting BLOOM_TEST_CULTURE (e.g. BLOOM_TEST_CULTURE=fr-FR) before starting a run makes that
    /// culture the user's regional format, as if the machine were set to it. The process culture
    /// stays invariant, exactly as it does for a user with that format. With the variable unset,
    /// which is how developers and the PR build run, the user's format is whatever this machine has.
    ///
    /// The point of the sweep is to catch code that shows or reads numbers, dates or sorted lists
    /// in the wrong format for the user, and code that sneaks the user's format into machine data.
    /// Before Bloom ran invariant it caught BL-15064, where PublishHelper.pxToNumber parsed CSS
    /// measurements with a culture-sensitive double.Parse, so publishing to BloomPUB in French
    /// produced uncropped images.
    ///
    /// Note what this deliberately does NOT do: it leaves CurrentUICulture alone. Changing that
    /// would make L10NSharp serve translated strings, and the very many tests that assert on
    /// English text would fail for a reason nobody cares about.
    /// </summary>
    /// <remarks>
    /// Like TestTempDirectory, this is a [SetUpFixture] in the BloomTests namespace, so it applies
    /// to that namespace and everything under it and runs before any fixture does. A fixture added
    /// outside BloomTests would not be covered.
    /// </remarks>
    [SetUpFixture]
    public class TestCulture
    {
        /// <summary>The environment variable naming the user's culture to run as, if any.</summary>
        internal const string kEnvironmentVariable = "BLOOM_TEST_CULTURE";

        /// <summary>
        /// Makes the process culture invariant, as Program.Main does, and makes the culture named by
        /// BLOOM_TEST_CULTURE, if it is set, the user's regional format. NUnit calls this once,
        /// before any test runs.
        /// </summary>
        [OneTimeSetUp]
        public void RunTheWayBloomRuns()
        {
            UserCulture.UseInvariantCulture();

            var name = Environment.GetEnvironmentVariable(kEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(name))
                return;

            // Deliberately not caught: being asked for a culture we cannot honour must stop the run,
            // not quietly continue in the default culture and report a green result that means
            // nothing.
            var culture = CultureInfo.GetCultureInfo(name.Trim());
            UserCulture.SetForTests(culture);

            // Standard error, because it is the only channel `dotnet test` shows at its default
            // verbosity. Worth saying out loud: a run whose failures are all about decimal points is
            // baffling until you know it was not running in English.
            Console.Error.WriteLine(
                $"{kEnvironmentVariable} is set, so this run's user culture (UserCulture.Current) is "
                    + $"'{culture.Name}' ({culture.EnglishName}), whose decimal separator is "
                    + $"'{culture.NumberFormat.NumberDecimalSeparator}'. The process culture is "
                    + "invariant, as in Bloom, and CurrentUICulture is left alone, so messages are "
                    + "still English."
            );
        }
    }
}
