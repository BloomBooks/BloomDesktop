using Bloom.ImageProcessing;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Registers Bloom's TagLib# workaround for Thai-style collation (see TagLibCultureFix) before
    /// any test runs, just as Program.Main does before Bloom does anything else, so that tests
    /// touching image metadata behave as Bloom does when the culture sweep runs them under th-TH.
    /// </summary>
    /// <remarks>
    /// Like TestTempDirectory, this is a [SetUpFixture] in the BloomTests namespace, so it applies
    /// to that namespace and everything under it.
    /// </remarks>
    [SetUpFixture]
    public class TestTagLibCultureFix
    {
        /// <summary>NUnit calls this once, before any test runs.</summary>
        [OneTimeSetUp]
        public void RegisterTagLibCultureFix()
        {
            TagLibCultureFix.Register();
        }
    }
}
