using System.Xml;
using Bloom.Book;
using Bloom.SafeXml;
using L10NSharp;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public class LayoutTests
    {
        [SetUp]
        public void Setup()
        {
            // Layout.DisplayName routes its English display names through LocalizationManager.
            // For an English UI the in-code name we pass wins, but under any other UI language
            // these (currently untranslated) dynamic ids resolve to the source value stored in
            // the XLF instead. Other fixtures (e.g. BookDataTests) load a "Bloom"
            // LocalizationManager with a non-English UI language and leave that language set in
            // the shared static state, so without this our DisplayName assertions would depend
            // on test order and pass locally but fail in CI. Pin English for determinism.
            // (BloomFileLocatorTests guards against the same cross-test pollution.)
            LocalizationManager.SetUILanguage("en");
        }

        [TestCase("Device16x9Portrait", "Ebook 9x16 Portrait")]
        [TestCase("Device16x9Landscape", "Ebook 16x9 Landscape")]
        public void DisplayName_Device16x9Layouts_UsesEbookLabels(
            string layoutClassName,
            string expectedDisplayName
        )
        {
            var layout = new Layout
            {
                SizeAndOrientation = SizeAndOrientation.FromString(layoutClassName),
            };

            Assert.That(layout.DisplayName, Is.EqualTo(expectedDisplayName));
        }

        [Test]
        public void UpdatePageSplitMode_WasCombinedAndShouldStayThatWay_PageUntouched()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body><div id='foo'></div><div class='bloom-page A5Landscape bloom-combinedPage'></div></body></html>"
            );
            var layout = new Layout()
            {
                ElementDistribution = Layout.ElementDistributionChoices.CombinedPages,
            };
            layout.UpdatePageSplitMode(dom);

            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-combinedPage')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-leadingPage')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-trailingPage')]",
                    0
                );
        }

        [Test]
        public void UpdatePageSplitMode_WasCombined_IsNowSplitIntoTwoPages()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body><div id='somemarginbox'><div class='bloom-page A5Landscape bloom-combinedPage'></div></div></body></html>"
            );
            var layout = new Layout()
            {
                ElementDistribution = Layout.ElementDistributionChoices.SplitAcrossPages,
            };
            layout.UpdatePageSplitMode(dom);

            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-combinedPage')]",
                    0
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-leadingPage')]",
                    1
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-trailingPage')]",
                    1
                );
        }

        [Test]
        public void UpdatePageSplitMode_WasCombined_IndividualPagesHaveOwnIds()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body><div id='foo'></div><div id='1' class='bloom-page A5Landscape bloom-combinedPage'></div></body></html>"
            );
            var layout = new Layout()
            {
                ElementDistribution = Layout.ElementDistributionChoices.SplitAcrossPages,
            };
            layout.UpdatePageSplitMode(dom);

            AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[@id=1]", 0);
        }

        [Test]
        public void UpdatePageSplitMode_WasCombined_ElementNowDividedBetweenTwoPages()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html ><body>
					<div class='bloom-page A5Landscape bloom-combinedPage'>
						<div id='themarginbox'>
							<div class='bloom-leadingElement'>top1</div>
							<div class='bloom-trailingElement'>bottom1</div>
							<div class='bloom-leadingElement'>top2</div>
							<div class='bloom-trailingElement'>bottom2</div>
						</div>
					</div>
			</body></html>"
            );
            var layout = new Layout()
            {
                ElementDistribution = Layout.ElementDistributionChoices.SplitAcrossPages,
            };
            layout.UpdatePageSplitMode(dom);

            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-leadingPage')]/div/div",
                    2
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-trailingPage')]/div/div",
                    2
                );

            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-leadingPage')]/div/div[contains(@class,'bloom-leadingElement')]",
                    2
                );
            AssertThatXmlIn
                .Dom(dom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-trailingPage')]/div/div[contains(@class,'bloom-trailingElement')]",
                    2
                );
        }
    }
}
