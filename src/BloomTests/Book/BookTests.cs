using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Publish;
using Moq;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Windows.Forms.ClearShare;
using SIL.Xml;
using System;
using System.Collections.Generic;
using System.Web;
using BloomTemp;

namespace BloomTests.Book
{
	[TestFixture]
	public class BookTests : BookTestsBase
	{
		/// <summary>
		/// this test is weak... it doesn't *really* tell us that the preview will look right (e.g., that
		/// the css will be properly found, based on the <base></base>, etc.)
		/// </summary>
		[Test]
		public void GetPreviewHtmlFileForWholeBook_what_UsesPreviewCss()
		{
			Assert.IsTrue(CreateBook().GetPreviewHtmlFileForWholeBook().InnerXml.Contains("previewMode.css"));
		}

		[Test]
		public void GetPreviewHtmlFileForWholeBook_BookHasThreePages_ResultHasAll()
		{
			var result = CreateBook().GetPreviewHtmlFileForWholeBook().RawDom.StripXHtmlNameSpace();
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter') or contains(@class,'bloom-backMatter') )]", 3);
		}

		[Test]
		public void BringBookUpToDate_EmbeddedXmlImgTagRemoved()
		{
			// Some older books had XML img tags inside the coverImage data-book value. This resulted in an
			// html-encoded background-image url with XML inside it.
			// BL-4586 and old Thai Big Book had this in it. Need to handle it for backwards compatibility.
			const string imgTag = "<img style='width: 360px;' src='myImage.png' height='360' alt='missing'></img>";
			SetDom(@"<div id='bloomDataDiv'>
						<div data-book='coverImage' lang='*'>
							" + imgTag + @"
						</div>
					</div>
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<div class='bloom-imageContainer' data-book='coverImage'>
								" + imgTag + @"
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			var dataBookImage = dom.SelectSingleNodeHonoringDefaultNS("//div[@id='bloomDataDiv']/div[@data-book='coverImage']");
			Assert.AreEqual("myImage.png", dataBookImage.InnerText);
			var pageImage = dom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage']");
			Assert.IsTrue(pageImage.Attributes["src"].Value.Equals("myImage.png"));
		}

		[Test]
		public void BringBookUpToDate_DataCkeTempRemoved()
		{
			// Some books got corrupted with CKE temp data, possibly before we prevented this happening when
			// pasting HTML (e.g., from Word). This tests that we clean it up.
			SetDom(@"<div class='bloom-page numberedPage customPage A5Portrait'>
						<div id='testDiv' class='marginBox'>
							<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style'>
								<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
									<div data-cke-hidden-sel='1' data-cke-temp='1' style='position:fixed;top:0;left:-1000px' class='bloom-contentNational2'>
										<br />
									</div>
								</div>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			//AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@data-cke-hidden-sel]");
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='testDiv']//br");
		}


		[Test]
		public void BringBookUpToDate_EmbeddedEmptyImgTagRemoved()
		{
			const string imgTag = "<img>bad tag contents</img>";
			const string placeHolderFile = "placeHolder.png";
			SetDom(@"<div id='bloomDataDiv'>
						<div data-book='coverImage' lang='*'>
							" + imgTag + @"
						</div>
					</div>
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<div class='bloom-imageContainer' data-book='coverImage'>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			var dataBookImage = dom.SelectSingleNodeHonoringDefaultNS("//div[@id='bloomDataDiv']/div[@data-book='coverImage']");
			Assert.AreEqual(placeHolderFile, dataBookImage.InnerText);
			var pageImage = dom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage']");
			Assert.IsTrue(pageImage.Attributes["src"].Value.Equals(placeHolderFile));
		}

		// Unless it's part of an image container that has an image description, an image
		// should have an alt attr that is exactly an empty string.
		[Test]
		public void BringBookUpToDate_AltNotImageDescription_SetEmpty()
		{
			SetDom(@"
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<img class='branding' src='title-page.svg' type='image/svg' alt='loading slowly'></img>
							<img class='licenseImage' src='license.png' data-derived='licenseImage' alt='License image'></img>
						</div>
					</div>
					<div class='bloom-page numberedPage customPage A5Portrait'>
						<div class='marginBox'>
							<img src='junk' alt = 'more junk'></img>
							<img src='rubbish'></img>
							<div style='min-height: 42px;' class='split-pane horizontal-percent'>
								<div title='aor_1B-E1.png' data-hasqtip='true' class='bloom-imageContainer bloom-leadingElement'>
									 <img data-license='cc-by-sa' data-creator='Susan Rose' data-copyright='Copyright SIL International 2009' src='aor_1B-E1.png' alt='This picture, aor_1B-E1.png, is missing or was loading too slowly.'></img>
								</div>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			var images = dom.SelectNodes("//img");
			Assert.That(images, Has.Count.AtLeast(4)); // may get extras from xmatter update
			foreach (XmlElement img in images)
			{
				Assert.That(img.Attributes["alt"], Is.Not.Null);
				Assert.That(img.Attributes["alt"].Value, Is.EqualTo(""));
			}
		}

		[Test]
		public void BringBookUpToDate_ImgWithDescription_CopiedToAlt()
		{
			SetDom(@"
					<div class='bloom-page numberedPage customPage A5Portrait'>
						<div class='marginBox'>
							<div style='min-height: 42px;' class='split-pane horizontal-percent'>
								<div title='aor_1B-E1.png' data-hasqtip='true' class='bloom-imageContainer'>
									 <img data-license='cc-by-sa' data-creator='Susan Rose' data-copyright='Copyright SIL International 2009' src='aor_1B-E1.png' alt='This picture, aor_1B-E1.png, is missing or was loading too slowly.'></img>
									<div class='bloom-translationGroup bloom-imageDescription bloom-trailingElement normal-style'>
										<div class='bloom-editable normal-style cke_focus bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='xyz'>
											<p>Bird with wings stretched wide</p>
										</div>
									</div>
								</div>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			var img = dom.SelectSingleNode("//div[@class='bloom-imageContainer']/img[@src='aor_1B-E1.png']");
			Assert.That(img.Attributes["alt"].Value, Is.EqualTo("Bird with wings stretched wide"));
		}

		[Test]
		public void BringBookUpToDate_EmbeddedEncodedXmlImgTagRemoved()
		{
			// I haven't seen this, but it could happen that the src attribute in the embedded img tag was html encoded.
			// So we'll make sure it works.
			const string imageFilename = "my ǆñImageﭳ.png";
			var encodedFilename = HttpUtility.UrlEncode(imageFilename);
			var noPlusEncodedName = encodedFilename.Replace("+", "%20");
			var imgTag = "<img style='width: 360px;' src='" + encodedFilename + "' height='360' alt='missing'></img>";
			SetDom(@"<div id='bloomDataDiv'>
						<div data-book='coverImage' lang='*'>
							" + imgTag + @"
						</div>
					</div>
					<div class='bloom-page bloom-frontMatter'>
						<div class='marginBox'>
							<div class='bloom-imageContainer' data-book='coverImage'>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			var dataBookImage = dom.SelectSingleNodeHonoringDefaultNS("//div[@id='bloomDataDiv']/div[@data-book='coverImage']");
			Assert.AreEqual(imageFilename, dataBookImage.InnerText);
			var pageImage = dom.SelectSingleNodeHonoringDefaultNS("//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage']");
			Assert.IsTrue(pageImage.Attributes["src"].Value.Equals(noPlusEncodedName));
		}

		[Test]
		public void BringBookUpToDate_VernacularTitleChanged_TitleCopiedToTextAreaOnAnotherPage()
		{
			var book = CreateBook();
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2' and @lang='xyz']");
			textarea1.InnerText = "peace";
			book.BringBookUpToDate(new NullProgress());
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='copyOfVTitle'  and @lang='xyz']");
			Assert.AreEqual("peace", textarea2.InnerText);
		}

		[Test]
		public void UpdateTextsNewlyChangedToRequiresParagraph_HasOneBR()
		{
			SetDom(@"<div class='bloom-page'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									a<br/>c
								</div>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-editable') and @lang='en']/p", 2);
		}

		[Test]
		public void BookWithUnknownLayout_GetsUpdatedToA5Portrait()
		{
			SetDom(@"<div class='bloom-page bloom-frontMatter QX9Landscape'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									a<br/>c
								</div>
							</div>
						</div>
					</div>
					<div class='bloom-page QX9Landscape'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									a<br/>c
								</div>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			var assertThat = AssertThatXmlIn.Dom(dom);
			// All bloom-page divs should now have class A5Portrait. Can't predict the exact number, it depends exactly
			// what is in the currently inserted Xmatter.
			assertThat.HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'A5Portrait') and contains(@class,'bloom-page')]",
				assertThat.CountOfMatchesForXPath("//div[contains(@class,'A5Portrait')]"));
			// And there should be none left with the unknown class.
			AssertThatXmlIn.Dom(dom).HasNoMatchForXpath("//div[contains(@class,'QX9Landscape')]");
		}

		//Removing extra lines is of interest in case the user was entering blank lines by hand to separate the paragraphs, which now will
		//be separated by the styling of the new paragraphs
		[Test]
		public void UpdateTextsNewlyChangedToRequiresParagraph_RemovesEmptyLines()
		{
			SetDom(@"<div class='bloom-page'>
						<div id='somewrapper'>
							<div id='test' class='bloom-translationGroup bloom-requiresParagraphs'>
								<div class='bloom-editable' lang='en'>
									<br/>a<br/>
								</div>
							</div>
						</div>
					</div>");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-editable') and @lang='en']/p", 1);
		}

		[Test]
		public void BringBookUpToDate_InsertsRegionalLanguageNameInAsWrittenInNationalLanguage1()
		{
			SetDom(@"<div class='bloom-page'>
						 <span data-collection='nameOfNationalLanguage2' lang='en'>{Regional}</span>
					</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//span[text()='French']",1);
		}


		[Test]
		public void SetMultilingualContentLanguages_UpdatesLanguagesOfBookFieldInDOM()
		{
			SetDom(@"<div class='bloom-page'>
						 <span data-book='languagesOfBook' lang='*'></span>
					</div>
			");

			_collectionSettings = new CollectionSettings(new NewCollectionSettings() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_testFolder.Path, "test"),
				Language1Iso639Code = "th", Language2Iso639Code = "fr", Language3Iso639Code = "es" });
			var book =  new Bloom.Book.Book(_metadata, _storage.Object, _templateFinder.Object,
				_collectionSettings,
				_pageSelection.Object, _pageListChangedEvent, new BookRefreshEvent());

			book.SetMultilingualContentLanguages(_collectionSettings.Language2Iso639Code, _collectionSettings.Language3Iso639Code);

			//note: our code currently only knows how to display French *in French* and Spanish *in Spanish*; Thai comes out in English.
			//It may be better to be writing "Thai" in Thai (or possibly French) or "Spanish" in French.
			//That's not part of this test, and will have to be changed as we improve that aspect of things.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//span[text()='Thai, français, español']", 1);

			book.SetMultilingualContentLanguages(_collectionSettings.Language2Iso639Code, null);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//span[text()='Thai, français']", 1);

			book.SetMultilingualContentLanguages("", null);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//span[text()='Thai']", 1);
		}

		[Test]
		public void SavePage_ChangeMade_StorageToldToSave()
		{
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
			book.SavePage(dom);
			_storage.Verify(s => s.Save(), Times.AtLeastOnce());
		}

		[Test]
		public void SavePage_ChangeMadeToSrcOfImg_StorageUpdated()
		{
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
			var imgInEditingDom = dom.SelectSingleNodeHonoringDefaultNS("//img[@id='img1']") as XmlElement;
			imgInEditingDom.SetAttribute("src", "changed.png");

			book.SavePage(dom);
			var imgInStorage = _storage.Object.Dom.RawDom.SelectSingleNodeHonoringDefaultNS("//img[@id='img1']") as XmlElement;

			Assert.AreEqual("changed.png", imgInStorage.GetAttribute("src"));
		}

		[Test]
		public void SavePage_ChangeMadeToTextAreaOfFirstTwin_StorageUpdated()
		{
			SetDom(@"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='1'>english</textarea>
							<textarea lang='xyz' id='2'>originalVernacular</textarea>
						</p>
					</div>
					<div class='bloom-page' id='guid3'>
						<p>
							<textarea  lang='xyz' id='3'>original2</textarea>
						</p>
					</div>
			");
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2']");
			Assert.AreEqual("originalVernacular", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "changed";
			book.SavePage(dom);
			var vernacularTextNodesInStorage = _storage.Object.Dom.RawDom.SafeSelectNodes("//textarea[@lang='xyz']");

			Assert.AreEqual("changed", vernacularTextNodesInStorage.Item(0).InnerText, "the value didn't get copied to  the storage dom");
			Assert.AreEqual("original2", vernacularTextNodesInStorage.Item(1).InnerText, "the second copy of this page should not have been changed");
		}

		[Test]
		public void SavePage_ChangeMadeToTextAreaOfSecondTwin_StorageUpdated()
		{
			SetDom(@"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='testText'>english</textarea>
							<textarea lang='xyz' id='testText'>original1</textarea>
						</p>
					</div>
					<div class='bloom-page' id='guid3'>
						<p>
							<textarea  lang='xyz' id='testText'>original2</textarea>
						</p>
					</div>
			");
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='testText' and @lang='xyz']");
			Assert.AreEqual("original2", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "changed";
			book.SavePage(dom);
			var textNodesInStorage = _storage.Object.Dom.RawDom.SafeSelectNodes("//textarea[@id='testText' and @lang='xyz']");

			Assert.AreEqual("original1", textNodesInStorage.Item(0).InnerText, "the first copy of this page should not have been changed");
			Assert.AreEqual("changed", textNodesInStorage.Item(1).InnerText, "the value didn't get copied to  the storage dom");
		}

		[Test]
		public void SavePage_ChangeMadeToTextAreaWithMultipleLanguages_CorrectOneInStorageUpdated()
		{
			SetDom(@"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='1'>english</textarea>
							<textarea lang='xyz' id='2'>originalVernacular</textarea>
							<textarea lang='tpi' id='3'>tokpsin</textarea>
						</p>
					</div>
			");
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[ @lang='xyz']");
			Assert.AreEqual("originalVernacular", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "changed";
			book.SavePage(dom);
			var vernacularTextNodesInStorage = _storage.Object.Dom.RawDom.SafeSelectNodes("//textarea[@id='2' and @lang='xyz']");

			Assert.AreEqual("changed", vernacularTextNodesInStorage.Item(0).InnerText, "the value didn't get copied to  the storage dom");
		 }

		[Test]
		public void GetEditableHtmlDomForPage_BasicBook_HasA5PortraitClass()
		{
			var book = CreateBook();
			book.SetLayout(new Layout() { SizeAndOrientation = SizeAndOrientation.FromString("A5Portrait") });
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'A5Portrait') and contains(@class,'bloom-page')]", 1);
		}

		[Test]
		public void GetEditableHtmlDomForPage_TemplateBook_NonXMatterLabelMadeEditable()
		{
			SetDom(@"<div class='bloom-page bloom-frontMatter' id='guid1'>
						<div class='pageLabel'></div>
						<p>
						</p>
					</div>
					<div class='bloom-page' id='guid2'>
						<div class='pageLabel'></div>
						<p>
						</p>
					</div>
					<div class='bloom-page bloom-backMatter' id='guid3'>
						<div class='pageLabel'></div>
						<p>
						</p>
					</div>
			");
			var book = CreateBook();
			// Even a content page doesn't get this unless it's a template book
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@class='pageLabel' and @contenteditable='true']");
			book.IsSuitableForMakingShells = true;
			// content page in template should get editable label
			dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@class='pageLabel' and @contenteditable='true']", 1);
			// but not in front or back matter.
			dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@class='pageLabel' and @contenteditable='true']");
			dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
			AssertThatXmlIn.Dom(dom.RawDom).HasNoMatchForXpath("//div[@class='pageLabel' and @contenteditable='true']");
		}

		[Test]
		public void InsertPageAfter_OnFirstPage_NewPageInsertedAsSecond()
		{
			var book = CreateBook();
			var existingPage=book.GetPages().First();
			TestTemplateInsertion(book, existingPage, "<div class='bloom-page somekind'>hello</div>");
		}
		[Test]
		public void InsertPageAfter_OnLastPage_NewPageInsertedAtEnd()
		{
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			TestTemplateInsertion(book, existingPage, "<div class='bloom-page somekind'>hello</div>");
		}

		/// <summary>
		/// a page might be "extra" as far as the template is concerned, but
		/// once a page is inserted into book (which may become a shell), it's
		/// just a normal page
		/// </summary>
		[Test]
		public void InsertPageAfter_PageWasMarkedExtra_NewPageIsNotMarkedExtra()
		{
			//enhance: move to book starter tests, since that's what implements the actual behavior
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page'  data-page='extra' >hello</div>");
			book.InsertPageAfter(existingPage, templatePage.Object);
			Assert.IsTrue(GetPageFromBookDom(book, 1).GetStringAttribute("class").Contains("bloom-page A5Portrait"));
		}

		[Test]
		public void InsertPageAfter_InTemplateBook_NewPageIsMarkedExtra()
		{
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page'>hello</div>");
			book.IsSuitableForMakingShells = true;
			book.InsertPageAfter(existingPage, templatePage.Object);
			Assert.That(GetPageFromBookDom(book, 1).GetStringAttribute("data-page"), Is.EqualTo("extra") );
		}


		[Test]
		public void InsertPageAfter_FromDifferentBook_MergesStyles()
		{

			using (var destBookWrapper = new TestBook("current book", @"<!DOCTYPE html>
<html>
<head>
	<style type='text/css' title='userModifiedStyles'>
	/*<![CDATA[*/
	.BigWords-style { font-size: 45pt ! important; text-align: center ! important; }
	/*]]>*/
	</style>
</head>

<body>
	<div class='bloom-page'><div class='bloom-translationGroup BigWords-style'></div></div>
</body>
</html>"))
			{
				var destBook = destBookWrapper.Book;
				using (var sourceBookWrapper = new TestBook("source book", @"<!DOCTYPE html>
<html>
<head>
	<style type='text/css' title='userModifiedStyles'>
	/*<![CDATA[*/
	.FancyText-style { font-size: 45pt ! important; text-align: center ! important; }
	.FancyText-style > p {margin-left: 20px !important}
	.FancyText-style[lang='en'] {font-size: 42pt; }
	.FancyText-style[lang='he'] {font-size: 50pt; }
	.BigWords-style {font-size:70pt !important; }
	.BigWords-style[lang='en'] {font-size:65pt !important; }
	.BigWords-style > p {margin-left: 20px !important}
	/*]]>*/
	</style>
</head>

<body>
	<div class='bloom-page'><div class='bloom-translationGroup FancyText-style'></div></div>
</body>
</html>"))
				{
					var sourceBook = sourceBookWrapper.Book;
					var existingPage = destBook.GetPages().First();
					var templatePage = sourceBook.GetPages().First();
					destBook.InsertPageAfter(existingPage, templatePage);
					var dom = destBook.RawDom.StripXHtmlNameSpace();
					var style = dom.SafeSelectNodes("//style")[0];
					Assert.That(style.InnerText, Does.Contain(".FancyText-style { font-size: 45pt ! important; text-align: center ! important; }"));
					Assert.That(style.InnerText, Does.Contain(".FancyText-style > p {margin-left: 20px !important}"));
					Assert.That(style.InnerText, Does.Contain(".FancyText-style[lang='en'] {font-size: 42pt; }"));
					Assert.That(style.InnerText, Does.Contain(".FancyText-style[lang='he'] {font-size: 50pt; }"));
					// Original BigWords style should survive unchanged.
					Assert.That(style.InnerText, Does.Contain(".BigWords-style { font-size: 45pt ! important; text-align: center ! important; }"));
					Assert.That(style.InnerText, Does.Not.Contain(".BigWords-style {font-size:70pt !important; }"));
					Assert.That(style.InnerText, Does.Not.Contain(".BigWords-style[lang='en']"));
					Assert.That(style.InnerText, Does.Not.Contain(".BigWords-style > p"));
				}
			}
		}

		[Test]
		public void InsertPageAfter_TemplateRefsPicture_PictureCopied()
		{
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page'  data-page='extra' >hello<img src='read.png'/></div>");
			using (var tempFolder = new TemporaryFolder("InsertPageAfter_TemplateRefsPicture_PictureCopied"))
			{
				File.WriteAllText(Path.Combine(tempFolder.FolderPath, "read.png"),"This is a test");
				var mockTemplateBook = new Moq.Mock<Bloom.Book.Book>();
				mockTemplateBook.Setup(x => x.FolderPath).Returns(tempFolder.FolderPath);
				mockTemplateBook.Setup(x => x.OurHtmlDom.GetTemplateStyleSheets()).Returns(new string[] {});
				templatePage.Setup(x => x.Book).Returns(mockTemplateBook.Object);
				book.InsertPageAfter(existingPage, templatePage.Object);
			}
			Assert.That(File.Exists(Path.Combine(book.FolderPath, "read.png")));
		}

		[Test]
		public void InsertPageAfter_SourcePageHasLineage_GetsLineageOfSourcePlusItsAncestor()
		{
			//enhance: move to book starter tests, since that's what implements the actual behavior
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page'  data-page='extra'  data-pagelineage='grandma' id='ma'>hello</div>");
			book.InsertPageAfter(existingPage, templatePage.Object);
			XmlElement page = (XmlElement) GetPageFromBookDom(book, 1);
			AssertThatXmlIn.String(page.OuterXml).HasSpecifiedNumberOfMatchesForXpath("//div[@data-pagelineage]", 1);
			string[] guids = GetLineageGuids(page);
			Assert.AreEqual("grandma",guids[0]);
			Assert.AreEqual("ma", guids[1]);
			Assert.AreEqual(2, guids.Length);
		}

		private string[] GetLineageGuids(XmlElement page)
		{
			XmlAttribute node = (XmlAttribute) page.SelectSingleNodeHonoringDefaultNS("//div/@data-pagelineage");
			return node.Value.Split(new char[]{';'});
		}

		[Test]
		public void InsertPageAfter_SourcePageHasNoLineage_IdOfSourceBecomesLineageOfNewPage()
		{
			//enhance: move to book starter tests, since that's what implements the actual behavior
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page' data-page='extra' id='ma'>hello</div>");
			book.InsertPageAfter(existingPage, templatePage.Object);
			XmlElement page = (XmlElement)GetPageFromBookDom(book, 1);
			AssertThatXmlIn.String(page.OuterXml).HasSpecifiedNumberOfMatchesForXpath("//div[@data-pagelineage='ma']", 1);
			string[] guids = GetLineageGuids(page);
			Assert.AreEqual("ma", guids[0]);
			Assert.AreEqual(1, guids.Length);
		}

		[Test]
		public void InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetLinkAdded()
		{
			using(var bookFolder = new TemporaryFolder("InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetLinkAdded"))
			{
				var templatePage = MakeTemplatePageThatHasABookWithStylesheets(bookFolder, new[] {"foo.css"});
				SetDom("<div class='bloom-page' id='1'></div>", ""); //but no special stylesheets in the target book
				var targetBook = CreateBook();
				targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

				Assert.NotNull(targetBook.OurHtmlDom.GetTemplateStyleSheets().First(name => name == "foo.css"));
			}
		}


		[Test]
		public void InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetFileCopied()
		{
			//we need an actual templateBookFolder to contain the stylesheet we need to see copied into the target book
			using(var templateBookFolder = new TemporaryFolder("InsertPageAfter_PageRequiresStylesheetWeDontHave_StylesheetFileCopied"))
			{
				//just a boring simple target book
				SetDom("<div class='bloom-page' id='1'></div>", "");
				var targetBook = CreateBook();

				//our template folder will have this stylesheet file
				File.WriteAllText(templateBookFolder.Combine("foo.css"), ".dummy{width:100px}");


				//we're going to reference one stylesheet that is actually available in the template folder, and one that isn't

				var templatePage = MakeTemplatePageThatHasABookWithStylesheets( templateBookFolder, new [] {"foo.css","notthere.css"});

				targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

				Assert.True(File.Exists(targetBook.FolderPath.CombineForPath("foo.css")));

				//Now add it again, to see if that causes problems
				targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

				//Have the template list a file it doesn't actually have
				var templatePage2 = MakeTemplatePageThatHasABookWithStylesheets( templateBookFolder, new[] { "notthere.css" });

					//for now, we just want it to not crash
				targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage2);
			}
		}

		[Test]
		public void InsertPageAfter_PageRequiresStylesheetWeAlreadyHave_StylesheetNotAdded()
		{
			using(var templateBookFolder = new TemporaryFolder("InsertPageAfter_PageRequiresStylesheetWeAlreadyHave_StylesheetNotAdded"))
			{
				var templatePage = MakeTemplatePageThatHasABookWithStylesheets(templateBookFolder, new string[] {"foo.css"});
					//it's in the template
				var link = "<link rel='stylesheet' href='foo.css' type='text/css'></link>";
				SetDom("<div class='bloom-page' id='1'></div>", link); //and we already have it in the target book
				var targetBook = CreateBook();
				targetBook.InsertPageAfter(targetBook.GetPages().First(), templatePage);

				Assert.AreEqual(1, targetBook.OurHtmlDom.GetTemplateStyleSheets().Count(name => name == "foo.css"));
			}
		}

		private IPage MakeTemplatePageThatHasABookWithStylesheets(TemporaryFolder bookFolder, IEnumerable<string> stylesheetNames )
		{
			var headContents = "";
			foreach(var stylesheetName in stylesheetNames)
			{
				headContents += "<link rel='stylesheet' href='"+stylesheetName+"' type='text/css'></link>";
			}

			var templateDom =
				new HtmlDom("<html><head>" + headContents + "</head><body><div class='bloom-page' id='1'></div></body></html>");
			var templateBook = new Moq.Mock<Bloom.Book.Book>();
			templateBook.Setup(x => x.FolderPath).Returns(bookFolder.FolderPath);
			templateBook.Setup(x => x.OurHtmlDom).Returns(templateDom);
			Mock<IPage> templatePage = CreateTemplatePage("<div class='bloom-page' id='1'></div>");
			templatePage.Setup(x => x.Book).Returns(templateBook.Object);
			return templatePage.Object;
		}

		private void TestTemplateInsertion(Bloom.Book.Book book, IPage existingPage, string divContent)
		{
			Mock<IPage> templatePage = CreateTemplatePage(divContent);

		   book.InsertPageAfter(existingPage, templatePage.Object);
			AssertPageCount(book, 4);
			Assert.IsTrue(GetPageFromBookDom(book, 1).GetStringAttribute("class").Contains("bloom-page somekind A5Portrait"));
		}

		private XmlNode GetPageFromBookDom(Bloom.Book.Book book, int pageNumber0Based)
		{
			var result = book.RawDom.StripXHtmlNameSpace();
			return result.SafeSelectNodes("//div[contains(@class, 'bloom-page')]", null)[pageNumber0Based];
		}

		private void AssertPageCount(Bloom.Book.Book book, int expectedCount)
		{
			var result = book.RawDom.StripXHtmlNameSpace();
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-page')]", expectedCount);
		}

		[Test]
		public void CreateBook_AlreadyHasCoverColor_GetsEmptyUserStyles()
		{
			var coverStyle = @"<style type='text/css'>
	DIV.coverColor  TEXTAREA  { background-color: #98D0B9 !important; }
	DIV.bloom-page.coverColor { background-color: #98D0B9 !important; }
			</style>";
			SetDom("<div class='bloom-page' id='1'></div>", coverStyle);

			// SUT
			var book = CreateBook();

			var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
			Assert.AreEqual(2, styleNodes.Count);
			Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
			Assert.AreEqual(string.Empty, styleNodes[0].InnerText);
			// verify that the 'coverColor' rules are still there
			Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
		}

		[Test]
		public void CreateBook_AlreadyHasCoverColorAndUserStyles_KeepsExistingStyles()
		{
			var userStyle = @"<style type='text/css' title='userModifiedStyles'>
	.normal-style[lang='fr'] { font-size: 9pt ! important; }
	.normal-style { font-size: 9pt !important; }
			</style>";
			var coverStyle = @"<style type='text/css'>
	DIV.coverColor  TEXTAREA  { background-color: #98D0B9 !important; }
	DIV.bloom-page.coverColor { background-color: #98D0B9 !important; }
			</style>";
			SetDom("<div class='bloom-page' id='1'></div>", userStyle + coverStyle);

			// SUT
			var book = CreateBook();

			var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
			Assert.AreEqual(2, styleNodes.Count);
			Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
			Assert.IsTrue(styleNodes[0].InnerText.Contains(".normal-style[lang='fr'] { font-size: 9pt ! important; }"));
			Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
		}

		[Test]
		public void CreateBook_HasNeitherStyle_GetsEmptyUserStyles()
		{
			SetDom("<div class='bloom-page' id='1'></div>");

			// SUT
			var book = CreateBook();

			var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
			Assert.AreEqual(2, styleNodes.Count); // also gets a new 'coverColor' style element
			Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
			Assert.AreEqual(string.Empty, styleNodes[0].InnerText);
			Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
		}

		[Test]
		public void CreateBook_AlreadyHasCoverColorAndUserStyles_InWrongOrder_GetsStyleElementsReversed()
		{
			var coverStyle = @"<style type='text/css'>
	DIV.coverColor  TEXTAREA  { background-color: #98D0B9 !important; }
	DIV.bloom-page.coverColor { background-color: #98D0B9 !important; }
			</style>";
			var userStyle = @"<style type='text/css' title='userModifiedStyles'>
	.normal-style[lang='fr'] { font-size: 9pt ! important; }
	.normal-style { font-size: 9pt !important; }
			</style>";
			SetDom("<div class='bloom-page' id='1'></div>", coverStyle + userStyle);

			// SUT
			var book = CreateBook();

			var styleNodes = book.OurHtmlDom.Head.SafeSelectNodes("./style");
			Assert.AreEqual(2, styleNodes.Count);
			Assert.AreEqual("userModifiedStyles", styleNodes[0].Attributes["title"].Value);
			Assert.IsTrue(styleNodes[0].InnerText.Contains(".normal-style[lang='fr'] { font-size: 9pt ! important; }"));
			Assert.IsTrue(styleNodes[1].InnerText.Contains("coverColor"));
		}

		[Test]
		public void DuplicatePage()
		{
			var book = CreateBook();
			var original = book.GetPages().Count();
			var existingPage = book.GetPages().Last();
			book.DuplicatePage(existingPage);
			AssertPageCount(book, original + 1);

			var newPage = book.GetPages().Last();
			Assert.AreNotEqual(existingPage, newPage);
			Assert.AreNotEqual(existingPage.Id, newPage.Id);

			var existingDivNode = existingPage.GetDivNodeForThisPage();
			var newDivNode = newPage.GetDivNodeForThisPage();

			Assert.AreEqual(existingPage.Id, newDivNode.Attributes["data-pagelineage"].Value);
			Assert.AreEqual(existingDivNode.InnerXml, newDivNode.InnerXml);

			Assert.AreEqual(original.ToString(), existingDivNode.Attributes["data-page-number"].Value);
			Assert.AreEqual((original+1).ToString(), newDivNode.Attributes["data-page-number"].Value);
		}

		[Test]
		public void DuplicatePage_WithAudio_OmitsAudioMarkup()
		{
			var book = CreateBook(); // has pages from  BookTestsBase.GetThreePageDom()
			var original = book.GetPages().Count();
			var existingPage = book.GetPages().Last();
			var pageDiv = book.GetPageElements().Cast<XmlElement>().Last();
			var extraPara = pageDiv.OwnerDocument.CreateElement("p");
			pageDiv.AppendChild(extraPara);
			var sentenceSpan = pageDiv.OwnerDocument.CreateElement("span");
			extraPara.AppendChild(sentenceSpan);
			sentenceSpan.SetAttribute("class", "audio-sentence");
			sentenceSpan.SetAttribute("id", Guid.NewGuid().ToString());
			sentenceSpan.InnerText = "This was a sentence span";
			book.DuplicatePage(existingPage);
			AssertPageCount(book, original + 1);

			var newPage = book.GetPages().Last();
			Assert.AreNotEqual(existingPage, newPage);
			Assert.AreNotEqual(existingPage.Id, newPage.Id);

			var newDivNode = newPage.GetDivNodeForThisPage();

			var newFirstPara = newDivNode.ChildNodes.Cast<XmlElement>().Last();
			Assert.That(newFirstPara.InnerXml, Is.EqualTo("This was a sentence span")); // no <span> element wrapped around it
		}

		[Test]
		public void DuplicatePageAfterRelocatePage()
		{
			var book = CreateBook();
			var pages = book.GetPages().ToArray();

			book.RelocatePage(pages[1], 2);
			var rearrangedPages = book.GetPages().ToArray();

			book.DuplicatePage(pages[2]);
			var newPages = book.GetPages().ToArray();

			Assert.AreEqual(3, rearrangedPages.Length);
			Assert.AreEqual(4, newPages.Length);

			// New page (with its own, unique Id) should be directly after the page we copied it from.
			// It was getting inserted first (BL-467)
			Assert.AreEqual("guid1", rearrangedPages[0].Id);
			Assert.AreEqual("guid3", rearrangedPages[1].Id);
			Assert.AreEqual("guid2", rearrangedPages[2].Id);

			Assert.AreEqual("guid1", newPages[0].Id);
			Assert.AreEqual("guid3", newPages[1].Id);
			Assert.AreEqual("guid2", newPages[3].Id);
		}

		[Test]
		public void DeletePage_OnLastPage_Deletes()
		{
			var book = CreateBook();
			var original= book.GetPages().Count();
			var existingPage = book.GetPages().Last();
			book.DeletePage(existingPage);
			AssertPageCount(book,original-1);
		}

		[Test]
		public void DeletePage_AttemptDeleteLastRemaingPage_DoesntDelete()
		{
			// On a book with Null xmatter (for instance), we don't want Bloom to let us delete the last page.
			var book = CreateBook();
			foreach (var page in book.GetPages())
			{
				book.DeletePage(page);
			}
			AssertPageCount(book, 1);
		}

		[Test]
		public void DeletePage_OnFirstPage_Renumbers()
		{
			var book = CreateBook();
			var original = book.GetPages().Count();
			var firstPage = book.GetPages().First();
			book.DeletePage(firstPage);
			AssertPageCount(book, original-1);
			var newFirstPage = book.GetPages().First();
			var newFirstDiv = newFirstPage.GetDivNodeForThisPage();
			Assert.AreEqual("1", newFirstDiv.Attributes["data-page-number"].Value);
		}

		[Test]
		public void RelocatePage_FirstPageToSecond_DoesRelocate()
		{
			var book = CreateBook();
			var pages = book.GetPages().ToArray();
			book.RelocatePage(pages[0], 1);
			var newPages = book.GetPages().ToArray();
			Assert.AreEqual(pages[0].Id, newPages[1].Id);
			Assert.AreEqual(pages[1].Id, newPages[0].Id);
			Assert.AreEqual(pages[2].Id, newPages[2].Id);
			Assert.AreEqual(3, pages.Length);
		}

		[Test]
		public void RelocatePage_FirstPageToLast_DoesRelocate()
		{
			var book = CreateBook();
			var pages = book.GetPages().ToArray();
			book.RelocatePage(pages[0], 2);
			var newPages = book.GetPages().ToArray();
			Assert.AreEqual(pages[1].Id, newPages[0].Id);
			Assert.AreEqual(pages[2].Id, newPages[1].Id);
			Assert.AreEqual(pages[0].Id, newPages[2].Id);
			Assert.AreEqual(3, pages.Length);
		}

		[Test]
		public void RelocatePage_LastPageToSecond_DoesRelocate()
		{
			var book = CreateBook();
			var pages = book.GetPages().ToArray();
			book.RelocatePage(pages[2], 1);
			var newPages = book.GetPages().ToArray();
			Assert.AreEqual(pages[0].Id, newPages[0].Id);
			Assert.AreEqual(pages[2].Id, newPages[1].Id);
			Assert.AreEqual(pages[1].Id, newPages[2].Id);
			Assert.AreEqual(3, pages.Length);
		}

		/// <summary>
		/// regression test
		/// </summary>
		[Test]
		public void RelocatePage_SuccessiveRelocates_BothWork()
		{
			var book = CreateBook();
			var pages = book.GetPages().ToArray();
			book.RelocatePage(pages[1], 0);
			book.RelocatePage(pages[2], 1);
			var newPages = book.GetPages().ToArray();
			Assert.AreEqual(pages[1].Id, newPages[0].Id);
			Assert.AreEqual(pages[2].Id, newPages[1].Id);
			Assert.AreEqual(pages[0].Id, newPages[2].Id);
			Assert.AreEqual(3, pages.Length);
		}
		[Test]
		public void RelocatePage_LastPageToFirst_DoesRelocate()
		{
			var book = CreateBook();
			var pages = book.GetPages().ToArray();
			book.RelocatePage(pages[2], 0);
			var newPages = book.GetPages().ToArray();
			Assert.AreEqual(pages[2].Id, newPages[0].Id);
			Assert.AreEqual(pages[0].Id, newPages[1].Id);
			Assert.AreEqual(pages[1].Id, newPages[2].Id);
			Assert.AreEqual(3, pages.Length);
		}

		[Test]
		public void CanDelete_VernacularBook_True()
		{
			var book = CreateBook();
			Assert.IsTrue(book.CanDelete);
		}

		[Test, Ignore("broken")]
		public void CanDelete_TemplateBook_False()
		{
			var book = CreateBook();
			Assert.IsFalse(book.CanDelete);
		}

		[Test]
		public void GetBookletLayoutMethod_A5Portrait_NotCalendar_Fold()
		{
			_bookDom = new HtmlDom(@"<html ><head>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.SideFold, book.GetBookletLayoutMethod(Layout.A5Portrait));
		}

		[Test]
		public void GetBookletLayoutMethod_CalendarSpecifiedInBook_Calendar()
		{

			_bookDom = new HtmlDom(@"<html ><head>
									<meta name='defaultBookletLayout' content='Calendar'/>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.Calendar, book.GetBookletLayoutMethod(Layout.A5Portrait));
			Assert.AreEqual(PublishModel.BookletLayoutMethod.Calendar, book.GetBookletLayoutMethod(A5Landscape));
		}

		private Layout A5Landscape => new Layout() {SizeAndOrientation = SizeAndOrientation.FromString("A5Landscape")};

		[Test]
		public void GetBookletLayoutMethod_A5Landscape_NotCalendar_CutAndStack()
		{
			_bookDom = new HtmlDom(@"<html ><head>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.CutAndStack, book.GetBookletLayoutMethod(A5Landscape));
		}

		[Test]
		public void GetDefaultBookletLayoutMethod_NotSpecified_Fold()
		{
			_bookDom = new HtmlDom(@"<html ><head>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.SideFold, book.GetDefaultBookletLayoutMethod());
		}

		[Test]
		public void GetDefaultBookletLayoutMethod_CalendarSpecified_Calendar()
		{

			_bookDom = new HtmlDom(@"<html ><head>
									<meta name='defaultBookletLayout' content='Calendar'/>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.Calendar, book.GetDefaultBookletLayoutMethod());
		}


		[Test]
		public void BringBookUpToDate_DomHas2ContentLanguages_PulledIntoBookProperties()
		{

			_bookDom = new HtmlDom(@"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage2'>okm</div><div data-book='contentLanguage3'>kbt</div></div></head><body></body></html>");
			var book = CreateBook();
			book.BringBookUpToDate(new NullProgress());
			Assert.AreEqual("okm", book.MultilingualContentLanguage2);
			Assert.AreEqual("kbt", book.MultilingualContentLanguage3);
		}

		//regression test
		[Test]
		public void BringBookUpToDate_A4LandscapeWithNoContentPages_RemainsA4Landscape()
		{
			// We need the reference to basic book.css because that's where the list of valid page layouts lives,
			// and Bloom will force the book to A5Portrait if it can't verify that A4Landscape is valid.
			_bookDom = new HtmlDom(@"
				<html>
					<head>
						<meta name='xmatter' content='Traditional'/>
						<link rel='stylesheet' href='Basic Book.css' type='text / css'></link>
					</head>
					<body>
						<div class='bloom-page cover coverColor bloom-frontMatter A4Landscape' data-page='required'>
						</div>
					</body>
				</html>");
			var book = CreateBook();
		   // AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'A4Landscape') and contains(@class,'bloom-page')]", 5);
			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'A4Landscape') and contains(@class,'bloom-page')]", 6);
		}


		/// <summary>
		/// regression test... when we rebuild the xmatter, we also need to update the html attributes that let us
		/// know the state of the image metadata without having to open the image up (slow).
		/// </summary>
		[Test]
		[Category("SkipOnTeamCity")]
		public void BringBookUpToDate_CoverImageHasMetaData_HtmlForCoverPageHasMetaDataAttributes()
		{
			_bookDom = new HtmlDom(@"
				<html>
					<body>
						<div id='bloomDataDiv'>
							<div data-book='coverImage'>test.png</div>
						</div>
					</body>
				</html>");

			var book = CreateBook();
			var imagePath = book.FolderPath.CombineForPath("test.png");
			MakeSamplePngImageWithMetadata(imagePath);

			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[@data-book='coverImage' and @data-creator='joe']",1);
		}

		[Test]
		public void BringBookUpToDate_RepairQuestionsPages_DoesNotMessUpGoodPages()
		{
			const string xpathQuestionsPrefix = "//div[contains(@class,'questions')]";
			_bookDom = new HtmlDom(@"
				<html>
					<head />
					<body>
						<div class='bloom-page bloom-nonprinting questions'>
							<div class='marginBox'>
								<div>
									<div class='quizInstructions'>Some gobbledy-gook</div>
								</div>
								<div>
									<div class='bloom-translationGroup quiz-style quizContents bloom-noAudio bloom-userCannotModifyStyles'>
										<div class='bloom-editable bloom-content1 bloom-contentNational1' contenteditable='true' lang='en'>
											My test question. <br/>
											<p>‌Answer 1 </p>
											<p>‌*Answer 2 </p>
											<p>‌</p>
											<p>‌Second test question </p>
											<p>‌*Some right answer </p>
											<p>‌Some wrong answer </p>
										</div>
									</div>
								</div>
							</div>
						</div>
					</body>
				</html>");

			var book = CreateBook();

			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div[contains(@class,'bloom-noAudio') and contains(@class,'bloom-userCannotModifyStyles')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div//p", 6);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div//br", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-nonprinting')]", 1);
		}

		[Test]
		public void BringBookUpToDate_RepairQuestionsPages_Works()
		{
			// tests migrating nonprinting class to bloom-nonprinting
			// tests cleaning out audio spans from questions
			// tests adding two classes to question content divs: 'bloom-noAudio' and 'bloom-userCannotModifyStyles'
			const string xpathQuestionsPrefix = "//div[contains(@class,'questions')]";
			_bookDom = new HtmlDom(@"
				<html>
					<head />
					<body>
						<div class='bloom-page questions nonprinting'>
							<div class='marginBox'>
								<div>
									<div class='quizInstructions'>Some gobbledy-gook</div>
								</div>
								<div>
									<div class='bloom-translationGroup quiz-style quizContents'>
										<div class='bloom-editable bloom-content1 bloom-contentNational1' contenteditable='true' lang='en'>
											<h1>My test question.</h1> <br/>
											<p>‌Answer 1 </p>
											<p>‌*Ans<span class='audio-sentence'>wer 2</span></p>
											<p>‌</p>
											<p>‌Second test question <em>weird stuff!</em></p>
											<p>*Some right answer</p>
											<p><span data-duration='1.600227' id='i125f143d-7c30-44c1-8d23-0e000f484e08' class='audio-sentence' recordingmd5='undefined'>My test text.</span></p>
										</div>
									</div>
								</div>
							</div>
						</div>
					</body>
				</html>");

			var book = CreateBook();

			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div[contains(@class,'bloom-noAudio') and contains(@class,'bloom-userCannotModifyStyles')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath(xpathQuestionsPrefix + "//div//span");
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath(xpathQuestionsPrefix + "//div//em");
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath(xpathQuestionsPrefix + "//div//h1");
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div//p[.='‌*Answer 2']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(xpathQuestionsPrefix + "//div//p[.='My test text.']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-nonprinting')]", 1);
		}

		[Test]
		public void BringBookUpToDate_LanguagesOfBookUpdated()
		{
			_bookDom = new HtmlDom(@"
				<html>
					<head>
						<meta name='xmatter' content='Traditional'/>
					</head>
					<body>
						<div id='bloomDataDiv'>
							<div data-book='languagesOfBook' lang='*'>
								English
							</div>
						</div>
					</body>
				</html>");
			var book = CreateBook();
			book.CollectionSettings.Language1Name = "My Language Name";
			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='languagesOfBook' and text()='My Language Name' and not(@lang='en')]", 3);
		}

		private TempFile MakeTempImage(string name)
		{
			using (var x = new Bitmap(100, 100))
			{
				x.Save(Path.Combine(Path.GetTempPath(), name), ImageFormat.Png);
			}
			return TempFile.TrackExisting(name);
		}

		[Test]
		public void GetPreviewHtmlFileForWholeBook_InjectedCoverHasCorrectImage()
		{
			_bookDom =
				new HtmlDom(
					@"
				<html>
					<body>
						<div id='bloomDataDiv'>
							<div data-book='coverImage'>theCover.png</div>
						</div>
					</body>
				</html>");

			var book = CreateBook();

			var imagePath = book.FolderPath.CombineForPath("theCover.png");
			MakeSamplePngImageWithMetadata(imagePath);

			//book.BringBookUpToDate(new NullProgress());
			var dom = book.GetPreviewHtmlFileForWholeBook();

			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-imageContainer')]/img[@data-book='coverImage' and @src='theCover.png']", 1);
		}

		[Test]
		[Category("SkipOnTeamCity")]
		public void UpdateImgMetdataAttributesToMatchImage_HtmlForImgGetsMetaDataAttributes()
		{
			_bookDom = new HtmlDom(@"
				<html>
					<body>
					   <div class='bloom-page'>
							<div class='marginBox'>
								<div class='bloom-imageContainer'>
								  <img src='test.png'/>
								</div>
							</div>
						</div>
					</body>
				</html>");

			var book = CreateBook();
			var imagePath = book.FolderPath.CombineForPath("test.png");
			MakeSamplePngImageWithMetadata(imagePath);

			book.BringBookUpToDate(new NullProgress());
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[@src='test.png' and @data-creator='joe']", 1);
		}

		[Test]
		public void BringBookUpToDate_UpdatesRtlInMetaJson()
		{
			_bookDom = new HtmlDom(@"<html ><head>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.That(book.BookInfo.IsRtl, Is.False);
			var old = _collectionSettings.IsLanguage1Rtl;
			try
			{
				_collectionSettings.IsLanguage1Rtl = true;
				book.BringBookUpToDate(new NullProgress());
			}
			finally
			{
				_collectionSettings.IsLanguage1Rtl = old;
			}
			Assert.That(book.BookInfo.IsRtl, Is.True);
		}

		[Test]
		public void BringBookUpToDate_MovesMetaDataToJson()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='bookLineage' content='old rubbish' />
					<meta name='bloomBookLineage' content='first,second' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid2'>
							<textarea lang='en' data-book='bookTitle'>my nice title</textarea>
						</div>
					</div>
				</body></html>");

			var book = CreateBook();
			book.BringBookUpToDate(new NullProgress());

			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookLineage']", 0);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bookLineage']", 0);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookId']", 0);

			Assert.That(_metadata.Id, Is.EqualTo("MyId"));
			Assert.That(_metadata.BookLineage, Is.EqualTo("first,second"));
			Assert.That(_metadata.Title, Is.EqualTo("my nice title"));
			// Checking the defaults, when not specified in the metadata
			Assert.That(_metadata.IsSuitableForMakingShells, Is.False);
			Assert.That(_metadata.IsSuitableForVernacularLibrary, Is.True);

			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='SuitableForMakingShells' content='yes' />
					<meta name='SuitableForMakingVernacularBooks' content='no' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid2'>
							<textarea lang='en' data-book='bookTitle'>my nice title</textarea>
						</div>
					</div>
				</body></html>");

			book = CreateBook();
			book.BringBookUpToDate(new NullProgress());
			// BL-2163, we are no longer migrating suitableForMakingShells
			Assert.That(_metadata.IsSuitableForMakingShells, Is.False);
			Assert.That(_metadata.IsSuitableForVernacularLibrary, Is.False);
		}

		[Test]
		public void FixBookIdAndLineageIfNeeded_WithPageTemplateSourceBasicBook_SetsMissingLineageToBasicBook()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='pageTemplateSource' content='Basic Book' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
					</div>
				</body></html>");

			_metadata.BookLineage = ""; // not sure if these could be left from another test
			_metadata.Id = "";
			var book = CreateBook();

			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookLineage' and @content='" + Bloom.Book.Book.kIdOfBasicBook + "']", 1);
			//Assert.That(_metadata.bloom.bookLineage, Is.EqualTo(Bloom.Book.Book.kIdOfBasicBook));
		}

		[Test]
		public void FixBookIdAndLineageIfNeeded_WithPageTemplateSourceBasicBook_OnBookThatHasJsonLineage_DoesNotSetLineage()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='pageTemplateSource' content='Basic Book' />
					<meta name='bloomBookId' content='MyId' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
					</div>
				</body></html>");

			_metadata.BookLineage = "something current";
			_metadata.Id = "";
			var book = CreateBook();

			// 0 because it should NOT make the change.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookLineage' and @content='" + Bloom.Book.Book.kIdOfBasicBook + "']", 0);
			Assert.That(_metadata.BookLineage, Is.EqualTo("something current"));
		}
		[Test]
		public void Save_UpdatesMetadataTitle()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid3'>
							<textarea lang='en' data-book='bookTitle'>original</textarea>
						</div>
					</div>
				</body></html>");

			var book = CreateBook();

			var titleElt = _bookDom.SelectSingleNode("//textarea");
			titleElt.InnerText = "changed & <mangled>";
			book.Save();
			Assert.That(_metadata.Title, Is.EqualTo("changed & <mangled>"));
		}


		[Test]
		public void Save_UpdatesBookInfoMetadataTags()
		{
			_bookDom = new HtmlDom(
				@"<html><body>
					<div class='bloom-page' id='guid3'>
						<div lang='en' data-derived='topic'>original</div>
					</div>
				</body></html>");

			var book = CreateBook();
			book.OurHtmlDom.SetBookSetting("topic", "en", "Animal stories");
			book.Save();
			Assert.That(book.BookInfo.TopicsList, Is.EqualTo("Animal stories"));

			book.OurHtmlDom.SetBookSetting("topic", "en", "Science");
			book.Save();
			Assert.That(book.BookInfo.TopicsList, Is.EqualTo("Science"));
		}

		[Test]
		public void Save_UpdatesMetadataCreditsRemovingBreaks()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid3'>
							<textarea lang='en' data-book='originalAcknowledgments'>original</textarea>
						</div>
					</div>
				</body></html>");

			var book = CreateBook();

			var acksElt = _bookDom.SelectSingleNode("//textarea");
			acksElt.InnerXml = "changed" + Environment.NewLine + "<br />more changes";
			book.Save();
			Assert.That(_metadata.Credits, Is.EqualTo("changed" + Environment.NewLine + "more changes"));
		}

		[Test]
		public void Save_UpdatesMetadataCreditsRemovingP()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid3'>
							<textarea lang='en' data-book='originalAcknowledgments'><p>original</p></textarea>
						</div>
					</div>
				</body></html>");

			var book = CreateBook();

			var acksElt = _bookDom.SelectSingleNode("//textarea");
#if __MonoCS__	// may not be needed for Mono 4.x
			acksElt.OwnerDocument.PreserveWhitespace = true;	// Does not preserve newlines on Linux without this
#endif
			acksElt.InnerXml = "<p>changed</p>" + Environment.NewLine + "<p>more changes</p>";
			book.Save();
			Assert.That(_metadata.Credits, Is.EqualTo("changed" + Environment.NewLine + "more changes"));
		}

		[Test]
		public void Save_UpdatesMetadataIsbnAndPageCount()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page' id='guid3'>
						<textarea lang='en' data-book='ISBN'>original</textarea>
					</div>
				</body></html>");

			var book = CreateBook();

			var isbnElt = _bookDom.SelectSingleNode("//textarea");
			isbnElt.InnerText = "978-0-306-40615-7";
			book.Save();
			Assert.That(book.BookInfo.Isbn, Is.EqualTo("978-0-306-40615-7"));

			var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
			isbnElt = dom.SelectSingleNode("//textarea");
			isbnElt.InnerText = " ";
			book.SavePage(dom);
			book.Save();
			Assert.That(_metadata.Isbn, Is.EqualTo(""));
		}

		public void Save_UpdatesAllTitles()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
						<div class='bloom-page' id='guid2'>
							<textarea lang='en' data-book='bookTitle'>my nice title</textarea>
							<textarea lang='de' data-book='bookTitle'>Mein schönen Titel</textarea>
							<textarea lang='es' data-book='bookTitle'>мy buen título</textarea>
						</div>
					</div>
				</body></html>".Replace("nice title", "\"nice\" title\\topic"));

			var book = CreateBook();

			book.Save();

			// Enhance: the order is not critical.
			Assert.That(_metadata.AllTitles, Is.EqualTo("{\"de\":\"Mein schönen Titel\",\"en\":\"my \\\"nice\\\" title\\\\topic\",\"es\":\"мy buen título\"}"));
		}

		[Test]
		public void AllLanguages_FindsBloomEditableElements()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
				   <title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page bloom-frontMatter'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='tr'>
								Some Thai in front matter. Should not count at all.
							</div>
						</div>
					</div>
					<div class='bloom-page' id='guid3'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Bloom ist ein Programm zum Erstellen von Sammlungen der Bucher. Es ist eine Hilfe zur Alphabetisierung.
							</div>

							<div class='bloom-editable' contenteditable='true' lang='en'>
								Bloom is a program for creating collections of books. It is an aid to literacy.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='fr'>
								Whatever.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='es'>
							</div>
						</div>
					</div>
					<div class='bloom-page' id='guid3'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='de'>
								Some German.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='en'>
								Some English.
							</div>
							<div class='bloom-editable' contenteditable='true' lang='fr'>
								Some French.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='es'>
								Something or other.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='xkal'>
								Something or other.
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='*'>
								This is not in any known language
							</div>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='z'>
								We use z for some special purpose, seems to occur in every book, don't want it.
							</div>
						</div>
					</div>
					<div class='bloom-page bloom-backMatter'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
							<div class='bloom-editable bloom-content1' contenteditable='true' lang='tr'>
								Some Thai in back matter. Should not count at all.
							</div>
						</div>
					</div>
				</body></html>");

			var book = CreateBook();
			var allLanguages = book.AllLanguages;
			Assert.That(allLanguages["en"], Is.True);
			Assert.That(allLanguages["de"], Is.True);
			Assert.That(allLanguages["fr"], Is.True);
			Assert.That(allLanguages["es"], Is.False); // in first group this is empty
			Assert.That(allLanguages["xkal"], Is.False); // not in first group at all
			Assert.That(allLanguages.Count(), Is.EqualTo(5)); // no * or z or tr
		}

		[Test]
		public void UpdateLicenseMetdata_UpdatesJson()
		{
			var book = CreateBook();

			// Creative Commons License
			var licenseData = new Metadata();
			licenseData.License = CreativeCommonsLicense.FromLicenseUrl("http://creativecommons.org/licenses/by-sa/3.0/");
			licenseData.License.RightsStatement = "Please acknowledge nicely to joe.blow@example.com";

			book.SetMetadata(licenseData);

			Assert.That(_metadata.License, Is.EqualTo("cc-by-sa"));
			Assert.That(_metadata.LicenseNotes, Is.EqualTo("Please acknowledge nicely to joe.blow@ex(download book to read full email address)"));

			// Custom License
			licenseData.License = new CustomLicense {RightsStatement = "Use it if you dare"};

			book.SetMetadata(licenseData);

			Assert.That(_metadata.License, Is.EqualTo("custom"));
			Assert.That(_metadata.LicenseNotes, Is.EqualTo("Use it if you dare"));

			// Null License (ask the user)
			licenseData.License = new NullLicense { RightsStatement = "Ask me" };

			book.SetMetadata(licenseData);

			Assert.That(_metadata.License, Is.EqualTo("ask"));
			Assert.That(_metadata.LicenseNotes, Is.EqualTo("Ask me"));
		}

		[Test]
		public void FixBookIdAndLineageIfNeeded_FixesBasicBookId()
		{
			_bookDom = new HtmlDom(
				@"<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<meta name='bloomBookId' content='" + Bloom.Book.Book.kIdOfBasicBook + @"' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />;
				</head>
				<body>
					<div class='bloom-page'>
					</div>
				</body></html>");

			_metadata.Id = "";
			var book = CreateBook();

			// 0 indicates it should NOT match, that is, that it doesn't have the mistaken ID any more.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookId' and @content='" + Bloom.Book.Book.kIdOfBasicBook + "']", 0);
			// but it should have SOME ID. Hopefully a new one, but that is hard to verify.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='bloomBookId']", 1);
		}

		[Test]
		public void Constructor_HadNoTitleButDOMHasItInADataItem_TitleElementIsSet()
		{
			SetDom(@"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' data-book='bookTitle'>original</textarea>
						</p>
					</div>");
			var book = CreateBook();
			var title = (XmlElement)book.RawDom.SelectSingleNodeHonoringDefaultNS("//title");
			Assert.AreEqual("original", title.InnerText);
		}

		[Test]
		public void Constructor_LanguagesOfBookIsSet()
		{
			var collectionSettings = CreateDefaultCollectionsSettings();
			collectionSettings.Language1Iso639Code = "en";
			var book = CreateBook(collectionSettings);
			var langs = book.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']/div[@data-book='languagesOfBook']") as XmlElement;
			Assert.AreEqual("English", langs.InnerText);
		}


		[Test]
		public void SavePage_HadTitleChangeEnglishTitle_ChangesTitleElement()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						  <div data-book='bookTitle' lang='en'>original</div>
					</div>
					<div class='bloom-page' id='guid1'>
						 <div data-book='bookTitle' lang='en'>original</div>
					</div>
				  </body></html>");

			var book = CreateBook();
			Assert.AreEqual("original", book.Title);

			//simulate editing the page
			var pageDom = new HtmlDom(@"
				<html><head></head><body>
					  <div class='bloom-page' id='guid1'>
							<div data-book='bookTitle' lang='en'>newTitle</div>
					   </div>
				  </body></html>");

			book.SavePage(pageDom);
			Assert.AreEqual("newTitle", book.Title);
		}

		[Test]
		public void SavePage_HasTitleTemplate_ChangesTitleElement()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						  <div data-book='bookTitle' lang='en'>blaah</div>
						<div data-book='bookTitleTemplate' lang='en'>a {book.flavor} book</div>
					</div>
					<div class='bloom-page' id='guid1'>
						 <div data-book='book.flavor' lang='en'>sweet</div>
					</div>
				  </body></html>");

			var book = CreateBook();
			Assert.AreEqual("a sweet book", book.Title);

			//simulate editing the page
			var pageDom = new HtmlDom(@"
				<html><head></head><body>
					  <div class='bloom-page' id='guid1'>
						 <div data-book='book.flavor' lang='en'>sour</div>
					   </div>
				  </body></html>");

			book.SavePage(pageDom);
			Assert.AreEqual("a sour book", book.Title);
		}

		/*
		 * TranslationGroupManager.UpdateContentLanguageClasses() sees that we have three active languages and adds
		 * bloom-trilingual as a class at the page level.  However, it was not getting added to the stored version
		 * of the page.  Thus, we are now checking that SavePage() adds it.
		 */
		[Test]
		public void SavePage_MultiLingualClassUpdated()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						<div data-book='contentLanguage1' lang='*'>
							xyz
						</div>
						<div data-book='contentLanguage2' lang='*'>
							en
						</div>
						<div data-book='contentLanguage3' lang='*'>
							fr
						</div>
					</div>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<div class='bloom-editable bloom-content2' contenteditable='true'></div>
						<div class='bloom-editable bloom-content3' contenteditable='true'></div>
					</div>
				  </body></html>");

			var book = CreateBook();

			// Initially, bloom-trilingual isn't there
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page') and contains(@class,'bloom-trilingual')]", 0);

			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);

			// bloom-trilingual was added to the temp version of the page
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page') and contains(@class,'bloom-trilingual')]", 1);

			book.SavePage(dom);

			// bloom-trilingual was also added to the stored version of the page
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page') and contains(@class,'bloom-trilingual')]", 1);
		}

		[Test]
		public void RepairBrokenSmallCoverCredits_Works()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div id='bloomDataDiv'>
						<div data-book='contentLanguage1' lang='*'>
							xyz
						</div>
						<div data-book='contentLanguage2' lang='*'>
							en
						</div>
						<div data-book='contentLanguage3' lang='*'>
							fr
						</div>
						<div data-book='smallCoverCredits' lang='*'>
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='en'>
								<p>Dr. Stephen McConnel, Ph.D.</p>
							</div>
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-contentNational2' contenteditable='true' lang='mix' />
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-contentNational1' contenteditable='true' lang='es'>
								<p />
							</div>
							<div class='bloom-editable' contenteditable='true' lang='z' />
							<div data-languagetipcontent='English' aria-label='false' role='textbox' spellcheck='true' tabindex='0' class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='fr'>
								<p>M. Stephen McConnel</p>
							</div>
						</div>
						<div data-book='smallCoverCredits' lang='fr'>
							<p>Stephen McConnel</p>
						</div>
					</div>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<div class='bloom-editable bloom-content2' contenteditable='true'></div>
						<div class='bloom-editable bloom-content3' contenteditable='true'></div>
					</div>
				  </body></html>");
			var book = CreateBook();
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits']", 2);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='*']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='en']");
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='mix']");
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='es']");
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='fr']", 1);
			Bloom.Book.Book.RepairBrokenSmallCoverCredits(book.OurHtmlDom);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits']", 2);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='*']");
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='mix']");
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='es']");
			// Now test code that probably never will be exercised in the wild.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='fr']", 1);
			var div = book.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']/div[@data-book='smallCoverCredits' and @lang='fr']");
			Assert.AreEqual("Stephen McConnel", div.InnerText.Trim());
		}

		[Test]
		public void RepairPageLabelLocalization_Works()
		{
			_bookDom = new HtmlDom(@"
			<html><head></head>
				<body>
					<!-- bare pageLabel, xmatter, no l18n attribute -->
					<div class='bloom-page frontCover bloom-frontMatter' id='guid1'>
						<div lang='en' class='pageLabel'>
							Front Cover
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<!-- old l18n attribute in pageLabel, needs to be replaced -->
					<div class='bloom-page' id='guid2'>
						<div class='pageLabel' data-i18n='EditTab.ThumbnailCaptions.Custom' lang='en'>
							Custom
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<div class='bloom-page' id='guid3'>
						<!-- proper pageLabel, preserve it -->
						<div class='pageLabel' data-i18n='TemplateBooks.PageLabel.Basic Text &amp; Picture' lang='en'>
							Possibly already translated text
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
					<div class='bloom-page outsideBackCover bloom-backMatter' id='guid4'>
						<!-- no l18n attribute on back cover xmatter pageLabel -->
						<div lang='en' class='pageLabel'>
							Outside Back Cover
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
					</div>
				</body>
			</html>");
			var book = CreateBook();
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid1']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='guid1']/div[@data-i18n]");
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid1']/div[@class='pageLabel' and contains(text(),'Front Cover')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid2']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid2']/div[@data-i18n='EditTab.ThumbnailCaptions.Custom']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid2']/div[@class='pageLabel' and contains(text(),'Custom')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid3']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid3']/div[@data-i18n='TemplateBooks.PageLabel.Basic Text & Picture']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid3']/div[@class='pageLabel' and contains(text(),'Possibly already translated text')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid4']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='guid4']/div[@data-i18n]");
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid4']/div[@class='pageLabel' and contains(text(),'Outside Back Cover')]", 1);
			Bloom.Book.Book.RepairPageLabelLocalization(book.OurHtmlDom);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid1']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid1']/div[@data-i18n='TemplateBooks.PageLabel.Front Cover']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid1']/div[@class='pageLabel' and contains(text(),'Front Cover')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid2']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid2']/div[@data-i18n='TemplateBooks.PageLabel.Custom']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid2']/div[@class='pageLabel' and contains(text(),'Custom')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid3']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid3']/div[@data-i18n='TemplateBooks.PageLabel.Basic Text & Picture']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid3']/div[@class='pageLabel' and contains(text(),'Possibly already translated text')]", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid4']/div[@class='pageLabel' and @lang='en']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid4']/div[@data-i18n='TemplateBooks.PageLabel.Outside Back Cover']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='guid4']/div[@class='pageLabel' and contains(text(),'Outside Back Cover')]", 1);
		}

		[Test]
		public void RemoveBlankPages_BlankPageRemoved()
		{
			// This page should be deleted, despite containing a visible div (but with only whitespace content),
			// a div with text (but hidden, since it doesn't have bloom-visibility-code-on),
			// and two kinds of image (but both pointing at placeholders).
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>
						</div>
						<div class='bloom-editable bloom-content1' contenteditable='true'>This is hidden.</div>
						<div class='bloom-imageContainer'>
							<img src='placeHolder.png'></img>
						</div>
						<div class='bloom-imageContainer bloom-backgroundImage' style=" + "\"background-image:url('placeHolder.png')\"" + @" ></div>
					</div>
				  </body></html>");
			var book = CreateBook();
			book.RemoveBlankPages();
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[contains(@class,'bloom-page')]");
		}

		[Test]
		public void RemoveBlankPages_ImagePagesKept()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<img src='somePicture.png'></img>
					</div>
					<div class='bloom-page' id='guid2'>
						<div class='bloom-editable bloom-content1' contenteditable='true'></div>
						<div class='bloom-imageContainer bloom-backgroundImage' style=" + "\"background-image:url('someImage.png')\"" + @" ></div>
					</div>
				</body></html>");
			var book = CreateBook();
			book.RemoveBlankPages();
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
		}

		[Test]
		public void RemoveBlankPages_TextPagesKept()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div class='bloom-page' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>some real text!</div>
					</div>
					<div class='bloom-page' id='guid2'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>");
			var book = CreateBook();
			book.RemoveBlankPages();
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
		}

		[Test]
		public void RemoveBlankPages_RenumbersPages()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1' data-page-number='1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>some real text!</div>
					</div>
					<div class='bloom-page numberedPage' id='guid2' data-page-number='2'>
					</div>
					<div class='bloom-page numberedPage' id='guid2' data-page-number='3'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>");
			var book = CreateBook();
			book.RemoveBlankPages();
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 2);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page') and @data-page-number='2']", 1);
		}

		[TestCase("span")]
		[TestCase("div")]
		public void SetAnimationDurationsFromAudioDurations_SetsExpectedDuration(string elementName)
		{
			// page 2 is left with no image to test that it doesn't choke on that.
			// page 4 has an image with no animation; a data-duration should not be set.
			_bookDom = new HtmlDom($@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1' data-page-number='1'>
						<div class='bloom-imageContainer some other classes' data-initialrect='0.0,0.0,0.5,0.5' data-finalrect='0.5,0.5,0.5,0.5'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'><{elementName} class='audio-sentence' data-duration='1.200'>some real text!</{elementName}> <{elementName} class='audio-sentence' data-duration='0.400'>another sentence!</{elementName}></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'><{elementName} class='audio-sentence' data-duration='1.500'>some more text!</{elementName}></div>
						<div class='bloom-editable bloom-visibility-code-on' contenteditable='true'><{elementName} class='audio-sentence' data-duration='1.800'>text not in content1, duration does not count</{elementName}></div>
					</div>
					<div class='bloom-page numberedPage' id='guid2' data-page-number='2'>
					</div>
					<div class='bloom-page numberedPage' id='guid3' data-page-number='3'>
						<div class='bloom-imageContainer some other classes' data-initialrect='0.0,0.0,0.5,0.5' data-finalrect='0.5,0.5,0.5,0.5'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
					<div class='bloom-page numberedPage' id='guid4' data-page-number='4'>
						<div class='bloom-imageContainer some other classes'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>");
			var book = CreateBook();
			book.SetAnimationDurationsFromAudioDurations();
			// Note: these tests are rather too picky about the formatting of the output floats. We'd be quite happy if the result
			// was 3.10000 and 4.0. If this proves problematic we can make the test smarter.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(@"//div[@id='guid1']/div[contains(@class,'bloom-imageContainer') and @data-duration='3.1']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(@"//div[@id='guid3']/div[contains(@class,'bloom-imageContainer') and @data-duration='4']",1);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath(@"//div[@id='guid4']/div[contains(@class,'bloom-imageContainer') and @data-duration]");
		}

		private Mock<IPage> CreateTemplatePage(string divContent)
		{

			var mockTemplateBook = new Moq.Mock<Bloom.Book.Book>();
			mockTemplateBook.Setup(x => x.OurHtmlDom.GetTemplateStyleSheets()).Returns(new string[] { });

			var templatePage = new Moq.Mock<IPage>();

			templatePage.Setup(x => x.Book).Returns(mockTemplateBook.Object);
			var d = new XmlDocument();
			d.LoadXml("<wrapper>" + divContent + "</wrapper>");
			var pageContentElement = (XmlElement)d.SelectSingleNode("//div");
			templatePage.Setup(x=>x.GetDivNodeForThisPage()).Returns(pageContentElement);

			return templatePage;
		}

		[Test]
		public void RemoveNonPublishablePages_Works()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='guid1' data-page-number='1'>
						<div />
					</div>
					<div class='bloom-page numberedPage bloom-noAudio' id='guid2' data-page-number='2'>
					</div>
					<div class='bloom-page numberedPage bloom-noreader' id='guid3' data-page-number='3'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
					<div class='bloom-page numberedPage screen-only' id='guid4' data-page-number='4'>
						<div class='bloom-imageContainer some other classes'></div>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true'>This is visible!</div>
					</div>
				</body></html>");
			var book = CreateBook();

			// SUT
			book.RemoveNonPublishablePages();

			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath(@"//div[contains(@class, 'bloom-page')]", 3);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath(@"//div[contains(@class,'bloom-noreader')]");
		}

		[Test]
		public void HasAudio_OnlyNonAudioSpans_ReturnsFalse()
		{
			// Test setup
			string id = "guid1";
			_bookDom = new HtmlDom($@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='guid1' data-page-number='1'>
						<p><span id='{id}' class='video-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
					</div>
				</body></html>");
			var book = CreateBook();

			BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, id, ".mp4");


			// System under test //
			bool result = book.HasAudio();


			// Verification
			Assert.AreEqual(false, result);
		}

		[Test]
		public void HasAudio_ContainsAudioSpans_ReturnsTrue()
		{
			// Test setup
			string id = "guid1";
			_bookDom = new HtmlDom($@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='guid1' data-page-number='1'>
						<p><span id='{id}' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</span></p>
					</div>
				</body></html>");
			var book = CreateBook();

			BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, id, ".wav", ".mp3");


			// System under test //
			bool result = book.HasAudio();


			// Verification // 
			Assert.AreEqual(true, result);
		}

		[Test]
		public void HasAudio_ContainsAudioDivs_ReturnsTrue()
		{
			// Test setup
			string id = "guid1";
			_bookDom = new HtmlDom($@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<div id='guid1' class='audio-sentence'>
							<p>Page 1 Paragraph 1 Sentence 1</p>
							<p>Page 1 Paragraph 2 Sentence 1</p>
						</div>
					</div>
				</body></html>");
			var book = CreateBook();

			BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, id, ".wav", ".mp3");


			// System under test //
			bool result = book.HasAudio();


			// Verification // 
			Assert.AreEqual(true, result);
		}


		[TestCase("span")]
		[TestCase("div")]
		public void HasFullAudioCoverage_ContainsMissingAudioElements_ReturnsFalse(string elementName)
		{
			// Test setup
			string lang = CreateDefaultCollectionsSettings().Language1Iso639Code;
			_bookDom = new HtmlDom($@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='{lang}'>
							<p><{elementName} id='id1' class='audio-sentence'>Sentence 1.</{elementName}>
							   <{elementName} id='id2' class='audio-sentence'>Sentence 2.</{elementName}></p>
						</div>
					</div>
				  </body></html>");
			var book = CreateBook();

			BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, "id1", ".wav", ".mp3");


			// System under test //
			bool result = book.HasFullAudioCoverage();


			// Verification // 
			Assert.AreEqual(false, result, $"ElementName: {elementName}");
		}

		[TestCase("span")]
		[TestCase("div")]
		public void HasFullAudioCoverage_ContainsAllAudioElements_ReturnsTrue(string elementName)
		{
			// Test setup
			string lang = CreateDefaultCollectionsSettings().Language1Iso639Code;
			_bookDom = new HtmlDom($@"
				<html><head></head><body>
					<div class='bloom-page numberedPage' id='guid1'>
						<div class='bloom-editable bloom-content1 bloom-visibility-code-on' contenteditable='true' lang='{lang}'>
							<p><{elementName} id='id1' class='audio-sentence'>Sentence 1.</{elementName}>
							   <{elementName} id='id2' class='audio-sentence'>Sentence 2.</{elementName}></p>
						</div>
					</div>
				  </body></html>");
			var book = CreateBook();

			BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, "id1", ".wav", ".mp3");
			BookStorageTests.MakeSampleAudioFiles(_tempFolder.Path, "id2", ".wav", ".mp3");


			// System under test //
			bool result = book.HasFullAudioCoverage();


			// Verification // 
			Assert.AreEqual(true, result, $"ElementName: {elementName}");
		}

		[TestCase("span")]
		[TestCase("div")]
		public void RemoveAudioMarkup_ContainsAudioElements_AllElementsRemoved(string elementName)
		{
			_bookDom = new HtmlDom($@"
				<html><head></head><body>
					<div class='bloom-page numberedPage bloom-nonprinting' id='page1' data-page-number='1'>
						<p><{elementName} id='id1' class='audio-sentence'>Page 1 Paragraph 1 Sentence 1</{elementName}></p>
						<p><{elementName} id='id2' class='audio-sentence'>Page 1 Paragraph 2 Sentence 1</{elementName}></p>
					</div>
				</body></html>");
			var book = CreateBook();

			// System under test
			var runner = new Microsoft.VisualStudio.TestTools.UnitTesting.PrivateType(typeof(global::Bloom.Book.Book));
			runner.InvokeStatic("RemoveAudioMarkup", book.RawDom.DocumentElement);

			// Test verification
			Assert.AreEqual(0, HtmlDom.SelectAudioSentenceElements(book.RawDom.DocumentElement)?.Count ?? 0, "Count did not match expectation");

			string expectedInnerHtml = "<p>Page 1 Paragraph 1 Sentence 1</p><p>Page 1 Paragraph 2 Sentence 1</p>";
			string expectedOuterHtml = $"<div class=\"bloom-page numberedPage bloom-nonprinting\" id=\"page1\" data-page-number=\"1\">{expectedInnerHtml}</div>";
			var page1Div = book.RawDom.SelectSingleNode("//div[@id='page1']") as XmlElement;
			Assert.AreEqual(expectedInnerHtml, page1Div.InnerXml, $"Case: {elementName}, Inner HTML");
			Assert.AreEqual(expectedOuterHtml, page1Div.OuterXml, $"Case: {elementName}, Outer HTML");
		}


#if UserControlledTemplate
		[Test]
		public void SetType_WasPublicationSetToTemplate_HasTemplateFeatures()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div id='bloomDataDiv'>
					</div>
					<div class='bloom-page bloom-frontMatter' id='1'>
						<div class='pageLabel'></div>
					</div>
					<div class='bloom-page' id='2'>
						<div class='pageLabel'></div>
					</div>
					<div class='bloom-page' id='3'>
						<div class='pageLabel'></div>
					</div>
					<div class='bloom-page bloom-backMatter' id='4'> </div>
				  </body></html>");

			var book = CreateBook();
			book.SwitchSuitableForMakingShells(true);
			Assert.IsTrue(book.BookInfo.IsSuitableForMakingShells);
			Assert.IsFalse(book.LockedDown);

			//don't change the number of pages
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 4);

			//Mark content pages as extra (but not xmatter pages)
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-page='extra']", 2);

			//Should point to itself as the pageTemplateSource
			book.Save();
			Assert.AreEqual(Path.GetFileName(book.FolderPath), book.OurHtmlDom.GetMetaValue("pageTemplateSource", ""));
		}

		[Test]
		public void SetType_WasTemplateSetToPublication_RemovesTemplateFeatures()
		{
			_bookDom = new HtmlDom(@"
				<html><head></head><body>
					<div id='bloomDataDiv'>
					</div>
					<div class='bloom-page bloom-frontMatter' id='1'>
						<div class='pageLabel'></div>
					</div>
					<div class='bloom-page' id='2'>
						<div class='pageLabel'></div>
					</div>
					<div class='bloom-page' id='3'>
						<div class='pageLabel'></div>
					</div>
					<div class='bloom-page bloom-backMatter' id='4'> </div>
				  </body></html>");

			var book = CreateBook();
			book.CollectionSettings.IsSourceCollection = true;
			book.SwitchSuitableForMakingShells(false);
			Assert.IsFalse(book.BookInfo.IsSuitableForMakingShells);
			Assert.IsFalse(book.LockedDown);
			Assert.IsTrue(book.RecordedAsLockedDown);

			//don't change the number of pages
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 4);

			//Mark content pages as extra (but not xmatter pages)
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@data-page='extra']");
		}
#endif
	}
}
