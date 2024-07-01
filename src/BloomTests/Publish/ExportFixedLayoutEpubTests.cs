using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.Publish
{
    // Tests stuff that is different when we export a fixed-layout epub
    public class ExportFixedLayoutEpubTests : ExportEpubTestsBaseClass
    {
        [OneTimeSetUp]
        public override void OneTimeSetup()
        {
            base.OneTimeSetup();
            base.Setup(); // since this class represents just one test, we can do it here.

            var book = SetupBookLong(
                "This is some text",
                "en",
                extraPageClass: " A5Landscape ",
                extraPages: @"<div class='bloom-page numberedPage' data-page-number='2'>
						<div id='anotherId' class='marginBox'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
									Page two text
								</div>
								<div lang = '*'>more text</div>
							</div>
						</div>
					</div>",
                createPhysicalFile: true
            );
            MakeEpub("output", "ExportFixedLayoutEpub", book, unpaginated: false);
            GetPageOneData();
            _ns = GetNamespaceManager();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            base.TearDown(); // since we did Setup in OneTimeSetup
            base.OneTimeTearDown();
        }

        public override void Setup()
        {
            // do nothing; we call base.Setup() for this class in OneTimeSetup().
        }

        public override void TearDown()
        {
            // do nothing; we call base.TearDown() for this class in OneTimeTearDown().
        }

        [Test]
        public void CheckEpubBasics()
        {
            CheckBasicsInPage();
            CheckBasicsInManifest();
            CheckAccessibilityInManifest(
                false,
                false,
                false,
                _defaultSourceValue.Replace("Portrait", "Landscape"),
                false
            );
            CheckFolderStructure();
        }

        [Test]
        public void SetsViewportInHtmlFiles()
        {
            // Using contains here because the actual strings we're generating have a lot of decimal places and
            // I don't want to depend on too much precision.
            // [Later: we limit the 'contains' in our test to whole pixels, since running the test in a different
            // "culture" would fail.]
            AssertThatXmlIn
                .String(_page1Data)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//meta[@name='viewport' and contains(@content, 'height=559') and contains(@content, 'width=793')]",
                    1
                );
            AssertThatXmlIn
                .String(GetPageNData(2))
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//meta[@name='viewport' and contains(@content, 'height=559') and contains(@content, 'width=793')]",
                    1
                );
        }

        [Test]
        public void SetsRenditionLayout()
        {
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//metadata/meta[@property='rendition:layout' and text() = 'pre-paginated']",
                    1
                );
        }

        [Test]
        public void SetsRenditionLandscape()
        {
            // Ideally, we'd have another test exporting a portrait book in fixed layout, and verify that it does not have these.
            // But I don't think it's worth the extra test time.
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//metadata/meta[@property='rendition:orientation' and text() = 'landscape']",
                    1
                );
            AssertThatXmlIn
                .String(_manifestContent)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//metadata/meta[@property='rendition:spread' and text() = 'none']",
                    1
                );
        }
    }
}
