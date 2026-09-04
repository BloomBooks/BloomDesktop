using System;
using System.Configuration;
using System.IO;
using Bloom;
using Bloom.Properties;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests
{
    /// <summary>
    /// BloomSettingsProvider keeps user.config where libpalaso's provider does, unless a folder is
    /// named (--user-settings-folder), in which case that folder is used instead. These tests drive
    /// the provider directly, the way ApplicationSettingsBase does, so they need no Bloom running
    /// and touch no real settings file.
    /// </summary>
    [TestFixture]
    public class BloomSettingsProviderTests
    {
        private const string kGroupName = "Bloom.Properties.Settings";
        private TemporaryFolder _folder;

        [SetUp]
        public void Setup()
        {
            // Settings.Default constructs its providers the first time any setting is read, and a
            // provider fixes its location when it is constructed. Make sure that has already
            // happened, so that naming a folder below moves only the providers these tests
            // construct, never the test run's own settings.
            var _ = Settings.Default.ShowExperimentalFeatures;
            _folder = new TemporaryFolder("BloomSettingsProviderTests");
        }

        [TearDown]
        public void TearDown()
        {
            BloomSettingsProvider.UserSettingsFolder = null;
            _folder.Dispose();
        }

        [Test]
        public void GetUserSettingsFolder_NoFolderNamed_IsThePerVersionFolderUnderLocalAppData()
        {
            BloomSettingsProvider.UserSettingsFolder = null;

            var folder = BloomSettingsProvider.GetUserSettingsFolder();

            Assert.That(
                folder,
                Does.StartWith(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                )
            );
            // libpalaso names the last folder after the entry assembly's version.
            Assert.That(
                Path.GetFileName(folder),
                Is.EqualTo(
                    System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString()
                )
            );
        }

        [Test]
        public void GetUserSettingsFolder_FolderNamed_IsThatFolder()
        {
            BloomSettingsProvider.UserSettingsFolder = _folder.Path;

            Assert.That(BloomSettingsProvider.GetUserSettingsFolder(), Is.EqualTo(_folder.Path));
            Assert.That(
                BloomSettingsProvider.GetUserConfigPath(),
                Is.EqualTo(Path.Combine(_folder.Path, "user.config"))
            );
        }

        [Test]
        public void SetPropertyValues_FolderNamed_WritesUserConfigThereAndReadsItBack()
        {
            BloomSettingsProvider.UserSettingsFolder = _folder.Path;
            var userConfig = Path.Combine(_folder.Path, "user.config");
            Assert.That(File.Exists(userConfig), Is.False, "test setup: the folder starts empty");

            var property = new SettingsProperty("UserInterfaceLanguage")
            {
                PropertyType = typeof(string),
                SerializeAs = SettingsSerializeAs.String,
                DefaultValue = "",
            };
            var context = new SettingsContext { ["GroupName"] = kGroupName };

            var writer = new BloomSettingsProvider();
            writer.Initialize(null, null);
            writer.SetPropertyValues(
                context,
                new SettingsPropertyValueCollection
                {
                    new SettingsPropertyValue(property) { SerializedValue = "fr" },
                }
            );

            Assert.That(
                File.Exists(userConfig),
                Is.True,
                "user.config was not written to the named folder"
            );
            Assert.That(
                File.ReadAllText(userConfig),
                Does.Contain("<setting name=\"UserInterfaceLanguage\"")
            );

            // A second provider, as a later Bloom run would construct, reads the same folder.
            var reader = new BloomSettingsProvider();
            reader.Initialize(null, null);
            var values = reader.GetPropertyValues(
                context,
                new SettingsPropertyCollection { property }
            );
            Assert.That(values["UserInterfaceLanguage"].SerializedValue, Is.EqualTo("fr"));
        }
    }
}
