using System;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// The Cloud Team Collections checkbox on the Advanced settings tab is hidden for 6.5 unless a
    /// developer/tester opts in by setting the `cloudCollections` environment variable to "true".
    /// This verifies CollectionSettingsApi.CloudTeamCollectionOptionVisible — the visibility flag
    /// the host reports to AdvancedSettingsPanel — testing the pure env-var logic directly, per the
    /// CollectionApiTests/SharingApiTests pattern (no API/dialog construction needed).
    /// </summary>
    [TestFixture]
    public class CollectionSettingsApiTests
    {
        private string _originalValue;

        [SetUp]
        public void SetUp()
        {
            _originalValue = Environment.GetEnvironmentVariable("cloudCollections");
        }

        [TearDown]
        public void TearDown()
        {
            // Restore whatever the environment had before, so we don't leak state to other tests.
            Environment.SetEnvironmentVariable("cloudCollections", _originalValue);
        }

        [Test]
        public void CloudTeamCollectionOptionVisible_EnvVarNotSet_IsFalse()
        {
            Environment.SetEnvironmentVariable("cloudCollections", null);
            // Sanity: confirm it really is unset before asserting the behavior.
            Assert.That(
                Environment.GetEnvironmentVariable("cloudCollections"),
                Is.Null,
                "test setup failed to clear the environment variable"
            );

            Assert.That(CollectionSettingsApi.CloudTeamCollectionOptionVisible, Is.False);
        }

        [TestCase("true", true)]
        [TestCase("TRUE", true)] // case-insensitive
        [TestCase("True", true)]
        [TestCase("false", false)]
        [TestCase("1", false)] // only the literal "true" opts in
        [TestCase("yes", false)]
        [TestCase("", false)]
        public void CloudTeamCollectionOptionVisible_ReflectsEnvVar(string value, bool expected)
        {
            Environment.SetEnvironmentVariable("cloudCollections", value);
            Assert.That(
                CollectionSettingsApi.CloudTeamCollectionOptionVisible,
                Is.EqualTo(expected)
            );
        }
    }
}
