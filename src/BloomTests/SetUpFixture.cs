using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// The methods in this class run once before and after each test run, i.e. they get
    /// executed exactly once.
    /// </summary>
    [SetUpFixture]
    public class SetupFixture
    {
        [OneTimeSetUp]
        public void Setup()
        {
            L10NSharp.LocalizationManager.StrictInitializationMode = false;
        }
    }
}
