using Bloom;
using Bloom.Workspace;
using L10NSharp;
using NUnit.Framework;

namespace BloomTests.Workspace
{
    /// <summary>
    /// Tests for the Bloom side of the "Pseudo-English" UI language (BL-16748). The transform
    /// itself lives in (and is tested by) L10NSharp; what is Bloom's business is how we name the
    /// pseudo-locale in the UI language menu and which channels we offer it on.
    /// </summary>
    [TestFixture]
    public class PseudoLocalizationTests
    {
        [Test]
        public void CreateLanguageItem_PseudoLocale_GetsOurOwnNameAndCountsAsComplete()
        {
            var item = WorkspaceView.CreateLanguageItem(
                LocalizationManager.PseudoLocalizationLanguageId
            );

            Assert.That(item.LangTag, Is.EqualTo("qps-ploc"));
            Assert.That(item.MenuText, Is.EqualTo(WorkspaceView.kPseudoLocalizationMenuText));
            Assert.That(item.EnglishName, Is.EqualTo(WorkspaceView.kPseudoLocalizationMenuText));
            // It is derived from the English at lookup time, so it is never partly "translated".
            Assert.That(item.FractionApproved, Is.EqualTo(1.0F));
            Assert.That(item.FractionTranslated, Is.EqualTo(1.0F));
        }

        [Test]
        public void OfferPseudoLocalizationForI18nTesting_NotOfferedOnANonDevOrAlphaChannel()
        {
            // Unit tests report the channel as kChannelNameForUnitTests, which is neither
            // developer nor alpha, so this stands in for a release channel.
            Assert.That(
                ApplicationUpdateSupport.IsDevOrAlpha,
                Is.False,
                "test setup problem: the unit-test channel should not count as dev or alpha"
            );
            Assert.That(Program.OfferPseudoLocalizationForI18nTesting, Is.False);
        }
    }
}
