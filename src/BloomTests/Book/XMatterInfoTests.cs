using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Bloom;
using Bloom.Book;
using L10NSharp;
using L10NSharp.Windows.Forms;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.Book
{
    /// <summary>
    /// Tests of the front/back matter pack descriptions, which for the packs that ship with Bloom
    /// live in the English XLF files rather than in description-en.txt files in the pack folders.
    /// </summary>
    [TestFixture]
    public class XMatterInfoTests
    {
        /// <summary>
        /// Every id in the three English XLF files, with its English text.
        /// </summary>
        internal static Dictionary<string, string> GetEnglishXlfSources()
        {
            var englishFolder = Path.Combine(
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization"),
                "en"
            );
            XNamespace xliff = "urn:oasis:names:tc:xliff:document:1.2";
            var sources = new Dictionary<string, string>();
            foreach (
                var file in new[] { "Bloom.xlf", "BloomMediumPriority.xlf", "BloomLowPriority.xlf" }
            )
            {
                var doc = XDocument.Load(Path.Combine(englishFolder, file));
                foreach (var unit in doc.Descendants(xliff + "trans-unit"))
                    sources[unit.Attribute("id").Value] = unit.Element(xliff + "source").Value;
            }
            return sources;
        }

        [Test]
        public void DescriptionL10nId_IsNextToTheNameId()
        {
            var info = new XMatterInfo(Path.Combine("somewhere", "Factory-XMatter"));

            Assert.That(
                info.NameL10nId,
                Is.EqualTo(
                    "CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack.Paper Saver"
                ),
                "Test setup: the name id should match the existing name entries in Bloom.xlf"
            );
            Assert.That(info.DescriptionL10nId, Is.EqualTo(info.NameL10nId + ".Description"));
        }

        /// <summary>
        /// The packs that ship with Bloom: the ones a user can choose, plus the project-specific
        /// ones a branding can choose.
        /// </summary>
        internal static XMatterInfo[] GetShippedPacks()
        {
            var finder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
            );
            var packs = finder
                .GetXMattersToOfferInSettings(null)
                .Concat(
                    finder.Factory.Where(p =>
                        p.PathToFolder.Contains("project-specific")
                        && !p.Key.StartsWith("unit-test", StringComparison.Ordinal)
                    )
                )
                .ToArray();
            Assert.That(
                packs.Select(p => p.Key),
                Does.Contain("Traditional").And.Contain("Factory").And.Contain("MXBBook"),
                "Test setup: the finder should see both standard and project-specific packs"
            );
            return packs;
        }

        [Test]
        public void EveryShippedPack_HasItsDescriptionInTheXlf()
        {
            var sources = GetEnglishXlfSources();
            var missing = GetShippedPacks()
                .Where(p => !sources.ContainsKey(p.DescriptionL10nId))
                .Select(p => p.DescriptionL10nId)
                .ToArray();

            Assert.That(missing, Is.Empty, "These pack descriptions are missing from the XLF");
        }
    }

    /// <summary>
    /// XMatterInfo.GetDescription, the lookup Collection Settings uses, against a real
    /// LocalizationManager over Bloom's own English XLF files.
    /// </summary>
    [TestFixture]
    public class XMatterInfoGetDescriptionTests
    {
        private ILocalizationManager[] _localizationManagers;
        private Dictionary<string, string> _englishSources;
        private TemporaryFolder _packsFolder;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            ErrorReport.IsOkToInteractWithUser = false;
            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationFolder = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                "localization"
            );
            // As in Bloom itself: I18NApi looks a string up in all three, in priority order.
            _localizationManagers = new[] { "Bloom", "BloomMediumPriority", "BloomLowPriority" }
                .Select(appId =>
                    LocalizationManagerWinforms.Create(
                        "en",
                        appId,
                        appId,
                        "1.0.0",
                        localizationFolder,
                        TestTempDirectory.LocalizationSettingPath,
                        null,
                        new string[] { }
                    )
                )
                .ToArray();
            _englishSources = XMatterInfoTests.GetEnglishXlfSources();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            foreach (var lm in _localizationManagers)
                lm.Dispose();
            LocalizationManager.SetUILanguage(LocalizationManager.kDefaultLang);
        }

        [SetUp]
        public void Setup()
        {
            _packsFolder = new TemporaryFolder("XMatterInfoGetDescriptionTests");
        }

        [TearDown]
        public void TearDown()
        {
            _packsFolder.Dispose();
        }

        [Test]
        public void GetDescription_ShippedPacks_ComeFromTheXlf()
        {
            // L10NSharp gives a multi-line string the platform's line breaks; the XLF text,
            // as XDocument reads it, has plain "\n".
            var wrong = XMatterInfoTests
                .GetShippedPacks()
                .Where(p =>
                    p.GetDescription().Replace("\r\n", "\n") != _englishSources[p.DescriptionL10nId]
                )
                .Select(p => p.Key + ": " + p.GetDescription())
                .ToArray();

            Assert.That(wrong, Is.Empty, "These packs did not get their description from the XLF");
        }

        [Test]
        public void GetDescription_UserInstalledPackWithoutXlfEntry_ComesFromItsFile()
        {
            var folder = Path.Combine(_packsFolder.Path, "SomeoneElses-XMatter");
            Directory.CreateDirectory(folder);
            var pack = new XMatterInfo(folder);
            Assert.That(
                _englishSources.ContainsKey(pack.DescriptionL10nId),
                Is.False,
                "Test setup: the user's pack should have no XLF entry"
            );
            RobustFile.WriteAllText(
                Path.Combine(folder, "description-en.txt"),
                "[V1]The user's own description"
            );

            Assert.That(pack.GetDescription(), Is.EqualTo("The user's own description"));
        }
    }
}
