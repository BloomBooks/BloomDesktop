using Bloom.Api;
using L10NSharp;
using L10NSharp.Windows.Forms;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;

namespace BloomTests.web
{
    /// <summary>
    /// I18NApi's lookups under the "Pseudo-English" UI language (qps-ploc). See BL-16748.
    ///
    /// These need a real LocalizationManager over real XLIFF, because what is being tested is the
    /// interaction between Bloom's lookup helpers and L10NSharp's pseudo-locale handling -- which a
    /// stub would define away. src/BloomTests/TestLocalization holds one string, Test.L10N.ID,
    /// whose English is "English Text".
    /// </summary>
    [TestFixture]
    public class I18NApiPseudoLocalizationTests
    {
        private const string kTestId = "Test.L10N.ID";
        private const string kTestEnglish = "English Text";
        private ILocalizationManager _localizationManager;

        [SetUp]
        public void Setup()
        {
            ErrorReport.IsOkToInteractWithUser = false;
            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationDirectory =
                FileLocationUtilities.GetDirectoryDistributedWithApplication(
                    "src/BloomTests/TestLocalization"
                );
            _localizationManager = LocalizationManagerWinforms.Create(
                LocalizationManager.PseudoLocalizationLanguageId,
                "Bloom",
                "Bloom",
                "1.0.0",
                localizationDirectory,
                "SIL/BloomTests",
                null,
                new string[] { }
            );
        }

        [TearDown]
        public void TearDown()
        {
            _localizationManager.Dispose();
            _localizationManager = null;
            LocalizationManager.SetUILanguage(LocalizationManager.kDefaultLang);
        }

        /// <summary>
        /// GetTranslation asks L10NSharp for the string with a deliberately recognizable sentinel
        /// as the "English", so that getting the sentinel back means "no localization, go read the
        /// XLIFF instead". Under the pseudo-locale L10NSharp pseudolocalizes the caller's default
        /// rather than consulting the cache, so it answers with a *transformed sentinel* -- which
        /// does not equal the sentinel, and so used to be returned to the caller as the string to
        /// display. PublishHelper builds the "requires a higher subscription tier" message out of
        /// this, so the user saw the mangled sentinel where a feature name belonged.
        /// </summary>
        [Test]
        public void GetTranslation_PseudoLocale_ReturnsPseudolocalizedEnglishNotTheSentinel()
        {
            // Sanity check the setup: without this a bug in the fixture would look like a pass.
            Assert.That(
                LocalizationManager.UILanguageId,
                Is.EqualTo(LocalizationManager.PseudoLocalizationLanguageId),
                "test setup problem: the UI language is not the pseudo-locale"
            );

            var translation = I18NApi.GetTranslation(kTestId);

            Assert.That(
                translation,
                Does.Not.Contain("usethis"),
                "GetTranslation returned L10NSharp's transformed sentinel instead of the real string"
            );
            Assert.That(translation, Is.EqualTo(LocalizationManager.PseudoLocalize(kTestEnglish)));
        }

        /// <summary>
        /// The sentinel path is still the one a real language uses, so the fix must not have
        /// turned the pseudo-locale's special case into the general one.
        /// </summary>
        [Test]
        public void GetTranslation_RealLanguage_StillReturnsTheTranslationFromTheXliff()
        {
            LocalizationManager.SetUILanguage("es");
            var translation = I18NApi.GetTranslation(kTestId);
            Assert.That(translation, Does.Not.Contain("usethis"));
            // The sentinel round trip is what finds the Spanish at all: L10NSharp answers with the
            // translation rather than the sentinel, and GetTranslation returns it.
            Assert.That(translation, Is.EqualTo("Spanish Text"));
        }
    }
}
