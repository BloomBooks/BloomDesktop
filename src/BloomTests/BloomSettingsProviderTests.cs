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
            BloomSettingsProvider.SetUserSettingsFolder(null);
            _folder.Dispose();
        }

        [Test]
        public void GetUserSettingsFolder_NoFolderNamed_IsThePerVersionFolderUnderLocalAppData()
        {
            BloomSettingsProvider.SetUserSettingsFolder(null);

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
            BloomSettingsProvider.SetUserSettingsFolder(_folder.Path);

            Assert.That(BloomSettingsProvider.GetUserSettingsFolder(), Is.EqualTo(_folder.Path));
            Assert.That(
                BloomSettingsProvider.GetUserConfigPath(),
                Is.EqualTo(Path.Combine(_folder.Path, "user.config"))
            );
        }

        [Test]
        public void CommandLineArgumentsForChildBloom_NoFolderNamed_IsEmpty()
        {
            BloomSettingsProvider.SetUserSettingsFolder(null);

            Assert.That(BloomSettingsProvider.CommandLineArgumentsForChildBloom, Is.Empty);
        }

        [Test]
        public void CommandLineArgumentsForChildBloom_FolderNamed_NamesItForTheChild()
        {
            BloomSettingsProvider.SetUserSettingsFolder(_folder.Path);

            Assert.That(
                BloomSettingsProvider.CommandLineArgumentsForChildBloom,
                Is.EqualTo($"--user-settings-folder \"{_folder.Path}\"")
            );
        }

        [Test]
        public void SetPropertyValues_FolderNamed_WritesUserConfigThereAndReadsItBack()
        {
            BloomSettingsProvider.SetUserSettingsFolder(_folder.Path);
            var userConfig = Path.Combine(_folder.Path, "user.config");
            Assert.That(File.Exists(userConfig), Is.False, "test setup: the folder starts empty");

            var property = MakeStringProperty();
            var context = MakeContext();

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

        [Test]
        public void Upgrade_FolderNamed_BringsNothingIn()
        {
            // A named folder holds exactly the settings its owner put there, so upgrading (which
            // ApplicationSettingsBase does through IApplicationSettingsProvider) must not copy a
            // previous version's user.config into it. On a machine with no earlier Bloom version
            // there is nothing to copy anyway; on a developer's machine there usually is, which is
            // exactly the case an automated run must be protected from.
            BloomSettingsProvider.SetUserSettingsFolder(_folder.Path);
            var userConfig = Path.Combine(_folder.Path, "user.config");
            Assert.That(File.Exists(userConfig), Is.False, "test setup: the folder starts empty");

            var provider = new BloomSettingsProvider();
            provider.Initialize(null, null);
            var property = MakeStringProperty();
            var asSettingsProvider = (IApplicationSettingsProvider)provider;

            asSettingsProvider.Upgrade(MakeContext(), new SettingsPropertyCollection { property });

            Assert.That(
                File.Exists(userConfig),
                Is.False,
                "Upgrade copied a previous version's user.config into the named folder"
            );
            Assert.That(
                asSettingsProvider.GetPreviousVersion(MakeContext(), property),
                Is.Null,
                "a named folder has no previous version"
            );
        }

        private static SettingsProperty MakeStringProperty()
        {
            return new SettingsProperty("UserInterfaceLanguage")
            {
                PropertyType = typeof(string),
                SerializeAs = SettingsSerializeAs.String,
                DefaultValue = "",
            };
        }

        private static SettingsContext MakeContext()
        {
            return new SettingsContext { ["GroupName"] = kGroupName };
        }
    }
}
