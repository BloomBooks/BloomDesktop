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

        /// <summary>
        /// Close the listeners of any servers still waiting to be closed. Everything they were
        /// serving finished long ago, so this is the safe moment; see RetiredTestServers.
        /// </summary>
        [OneTimeTearDown]
        public void CloseRetiredServers()
        {
            RetiredTestServers.CloseAllNow();
        }
    }
}
