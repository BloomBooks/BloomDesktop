using System;
using System.Globalization;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Lets a whole test run be executed under a different culture, by setting BLOOM_TEST_CULTURE
    /// (e.g. BLOOM_TEST_CULTURE=fr-FR) before starting it. With the variable unset — which is how
    /// developers and the PR build run — this does nothing at all.
    ///
    /// The point is to catch code that formats or parses machine data using whatever culture the
    /// user happens to have, which produces silently wrong output rather than an exception. Bloom
    /// has shipped exactly that bug: BL-15064 found that PublishHelper.pxToNumber parsed CSS
    /// measurements (always period-decimal) with a culture-sensitive double.Parse, so publishing to
    /// BloomPUB in a comma-decimal locale such as French produced *uncropped images*. That was found
    /// only because a developer's machine happened to be set to fr-FR.
    ///
    /// Note what this deliberately does NOT do: it leaves CurrentUICulture alone. Changing that
    /// would make L10NSharp serve translated strings, and the very many tests that assert on
    /// English text would fail for a reason nobody cares about, burying the formatting and parsing
    /// bugs this is meant to surface.
    /// </summary>
    /// <remarks>
    /// Like TestTempDirectory, this is a [SetUpFixture] in the BloomTests namespace, so it applies
    /// to that namespace and everything under it and runs before any fixture does. A fixture added
    /// outside BloomTests would not be covered.
    /// </remarks>
    [SetUpFixture]
    public class TestCulture
    {
        /// <summary>The environment variable naming the culture to run under, if any.</summary>
        internal const string kEnvironmentVariable = "BLOOM_TEST_CULTURE";

        /// <summary>
        /// Switches the run over to the culture named by BLOOM_TEST_CULTURE, if it is set. NUnit
        /// calls this once, before any test runs.
        /// </summary>
        [OneTimeSetUp]
        public void UseTheCultureWeWereAskedFor()
        {
            var name = Environment.GetEnvironmentVariable(kEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(name))
                return;

            // Deliberately not caught: being asked for a culture we cannot honour must stop the run,
            // not quietly continue in the default culture and report a green result that means
            // nothing.
            var culture = CultureInfo.GetCultureInfo(name.Trim());

            // DefaultThreadCurrentCulture covers threads the run starts later; CurrentCulture covers
            // the one we are on now, which NUnit's setup and the tests themselves run on.
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.CurrentCulture = culture;

            // Standard error, because it is the only channel `dotnet test` shows at its default
            // verbosity. Worth saying out loud: a run whose failures are all about decimal points is
            // baffling until you know it was not running in English.
            Console.Error.WriteLine(
                $"{kEnvironmentVariable} is set, so this run uses the culture "
                    + $"'{culture.Name}' ({culture.EnglishName}), whose decimal separator is "
                    + $"'{culture.NumberFormat.NumberDecimalSeparator}'. "
                    + "CurrentUICulture is left alone, so messages are still English."
            );
        }
    }
}
