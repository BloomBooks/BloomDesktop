using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Publish.Epub;
using BloomTemp;
using BloomTests.Book;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;

namespace BloomTests.Publish
{
	[TestFixture]
	// This class implements what is conceptually one test in the ExportEpubTests group.
	// But as there are many different outcomes to verify, it's cleaner to make a distinct class,
	// and do the setup and test running in OneTimeSetup.
	public class ExportEpubWithLinksTests : ExportEpubTestsBaseClass
	{
		private AssertXmlString _assertThatPage1Data;
		private string _page2Data;
		private AssertXmlString _assertThatPage2Data;

		[OneTimeSetUp]
		public override void OneTimeSetup()
		{
			base.OneTimeSetup();
			base.Setup(); // since this class represents just one test, we can do it here.
			_ns = ExportEpubTestsBaseClass.GetNamespaceManager();
			_bookServer = CreateBookServer();
			var image3 = MakeImageContainer("image3", "This describes image 3 on page 2", "xyz");
			var book = SetupBookLong("This is a simple page", "xyz", images: new[] { "image1", "image2"},
				imageDescriptions: new[] { "This describes image 1", "This describes image 2" },
				extraPages: @"
					<div class='bloom-page numberedPage' data-page-number='2'>
				        <div id = 'anotherId' class='marginBox'>
							<div id = 'test' class='bloom-translationGroup bloom-requiresParagraphs' lang=''>
								<div aria-describedby='qtip-1' class='bloom-editable' lang='en'>
				                                                           Page two text
								</div>
								<div lang = '*' > more text</div>
							</div>
						</div>"
						+ image3
					+ @"</div>");
			MakeImageFiles(book, "image1", "image2", "image3");
			MakeEpub("output", "ExportEpubWithLinksTests", book, EpubMaker.HowToPublishImageDescriptions.Links);
			GetPageOneData();
			_page2Data = GetPageNData(2);
			_assertThatPage1Data = AssertThatXmlIn.String(_page1Data);
			_assertThatPage2Data = AssertThatXmlIn.String(_page2Data);
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
		public void CreatesLink()
		{
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:a[@href='ImageDesc1.xhtml#imageDesc1' and @id='figdesc1' and text()='Image Description']", _ns, 1);
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:a[@href='ImageDesc2.xhtml#imageDesc2' and @id='figdesc2' and text()='Image Description']", _ns, 1);
			_assertThatPage2Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:a[@href='ImageDesc3.xhtml#imageDesc3' and @id='figdesc3' and text()='Image Description']", _ns, 1);
		}

		[Test]
		public void CreatesExtraPages()
		{
			VerifyEntryExists("ImageDesc1.xhtml");
			VerifyEntryExists("ImageDesc2.xhtml");
			VerifyEntryExists("ImageDesc3.xhtml");
		}

		[Test]
		public void ExtraPagesAreInManifest()
		{
			var assertThatManifest = AssertThatXmlIn.String(_manifestContent);
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/manifest/item[@id='ImageDesc1' and @href='ImageDesc1.xhtml']", 1);
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/manifest/item[@id='ImageDesc2' and @href='ImageDesc2.xhtml']", 1);
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/manifest/item[@id='ImageDesc3' and @href='ImageDesc3.xhtml']", 1);
		}

		[Test]
		public void ExtraPagesAreInSpineButNotLinear()
		{
			var assertThatManifest = AssertThatXmlIn.String(_manifestContent);
			// These xpaths attempt to verify that the extra pages occur last in reading order. It's a rather fragile way to do it
			// but I can't find a better one, at least with xpath. With two real pages before them (and no xmatter, since the book
			// is just a test stub) they occupy these positions.
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/spine/itemref[@idref='ImageDesc1' and @linear='no' and position()=3]", 1);
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/spine/itemref[@idref='ImageDesc2' and @linear='no' and position()=4]", 1);
			assertThatManifest.HasSpecifiedNumberOfMatchesForXpath("package/spine/itemref[@idref='ImageDesc3' and @linear='no' and position()=5]", 1);
		}

		[TestCase("ImageDesc1", "This describes image 1", "imageDesc1", "1.xhtml", "bookfig1")]
		[TestCase("ImageDesc2", "This describes image 2", "imageDesc2", "1.xhtml", "bookfig2")]
		[TestCase("ImageDesc3", "This describes image 3 on page 2", "imageDesc3", "2.xhtml", "bookfig3")]
		public void ExtraPagesHaveValidContent(string fileName, string asideContent, string asideId, string sourceDocName, string sourceId)
		{
			string content = GetFileData(fileName + ".xhtml");
			XDocument doc = null;
			Assert.DoesNotThrow(() => doc = XDocument.Parse(content));
			Assert.That(doc.Root.Attribute("lang")?.Value, Is.EqualTo(_collectionSettings.Language1Iso639Code));
			XNamespace xmlns = "http://www.w3.org/XML/1998/namespace";
			Assert.That(doc.Root.Attribute(xmlns + "lang")?.Value, Is.EqualTo(_collectionSettings.Language1Iso639Code));
			var assertThatPage = AssertThatXmlIn.String(content);
			assertThatPage.HasSpecifiedNumberOfMatchesForXpath(
				"//xhtml:div[contains(@class, 'bloom-page')]/xhtml:div[@class='marginBox']/xhtml:aside[text()='" + asideContent +
				"' and @id='" + asideId + "' and @role='doc-footnote' and @epub:type='footnote']", _ns, 1);
			assertThatPage.HasSpecifiedNumberOfMatchesForXpath("//xhtml:a[@href='" + sourceDocName + "#" + sourceId + "' and text()='Back' and @role='doc-backlink' and @epub:type='backlink']", _ns, 1);
		}

		[Test]
		public void ImagesGetAriaDetails()
		{
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@src='image1.png' and @aria-details='figdesc1']", _ns, 1);
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@src='image2.png' and @aria-details='figdesc2']", _ns, 1);
			_assertThatPage2Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@src='image3.png' and @aria-details='figdesc3']", _ns, 1);
		}

		[Test]
		public void ImagesGetIds()
		{
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@src='image1.png' and @id='bookfig1']", _ns, 1);
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@src='image2.png' and @id='bookfig2']", _ns, 1);
			_assertThatPage2Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:img[@src='image3.png' and @id='bookfig3']", _ns, 1);
		}

		[Test]
		public void ExtraPagesNotInNavigationPageList()
		{
			var nav = StripXmlHeader(GetFileData("nav.xhtml"));
			var assertThatNav = AssertThatXmlIn.String(nav);
			assertThatNav.HasNoMatchForXpath("//xhtml:a[contains(@href, 'ImageDesc1.xhtml')]", _ns);
		}

		[Test]
		public void ForwardLinkHasRoleAndEpubTypeNoteRef()
		{
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:a[@href='ImageDesc1.xhtml#imageDesc1' and @role='doc-noteref' and @epub:type='noteref']", _ns, 1);
			_assertThatPage1Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:a[@href='ImageDesc2.xhtml#imageDesc2' and @role='doc-noteref' and @epub:type='noteref']", _ns, 1);
			_assertThatPage2Data.HasSpecifiedNumberOfMatchesForXpath("//xhtml:a[@href='ImageDesc3.xhtml#imageDesc3' and @role='doc-noteref' and @epub:type='noteref']", _ns, 1);
		}
	}
}
