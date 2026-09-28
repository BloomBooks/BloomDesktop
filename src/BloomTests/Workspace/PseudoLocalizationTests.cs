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
        public void OfferPseudoLocalizationForI18nTesting_NotOfferedOnANonTestingChannel()
        {
            // Unit tests report the channel as kChannelNameForUnitTests, which is none of the
            // channels that offer it, so this stands in for a release channel.
            Assert.That(
                ApplicationUpdateSupport.ChannelName,
                Is.EqualTo(ApplicationUpdateSupport.kChannelNameForUnitTests),
                "test setup problem: not running on the unit-test channel"
            );
            Assert.That(Program.OfferPseudoLocalizationForI18nTesting, Is.False);
        }

        // These are the names ApplicationUpdateSupport.ChannelName actually returns.
        [TestCase("Developer/Debug", true)]
        [TestCase("Developer/Release", true)]
        [TestCase("Alpha", true)]
        [TestCase("BetaInternal", true)]
        [TestCase("ReleaseInternal", true)]
        [TestCase("Beta", false)]
        [TestCase("Release", false)]
        public void OffersPseudoLocalizationOnChannel_OnlyWhereDevelopersAndTestersAre(
            string channelName,
            bool expected
        )
        {
            Assert.That(
                Program.OffersPseudoLocalizationOnChannel(channelName),
                Is.EqualTo(expected)
            );
        }
    }
}
