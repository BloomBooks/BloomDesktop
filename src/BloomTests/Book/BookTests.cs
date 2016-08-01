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
using BloomTemp;

namespace BloomTests.Book
{
	[TestFixture]
	public class BookTests : BookTestsBase
	{
//        [Test]
//        public void InsertPage_PageInMiddle_IsInserted()
//        {
//        }

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

//        [Test]
//        public void InsertPage_RaisesInsertionEvent()
//        {
//            var book = CreateBook();
//            bool gotEvent = false;
//            book.PageInserted += new EventHandler((x, y) => gotEvent = true);
//            Page existingPage = book.GetPages().First();
//            TestTemplateInsertion(book, existingPage, 1);
//            Assert.IsTrue(gotEvent);
//        }



//		//regression
//		[Test]
//		public void UpdateFieldsAndVariables_NewVaccinationsBook_BookIsStillCalledVaccinations()
//		{
//			zzzz
//			SetDom();
//			var book = CreateBook();
//			var dom = book.RawDom;
//			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2' and @lang='xyz']");
//			textarea1.InnerText = "peace";
//			book.UpdateFieldsAndVariables_TEMPFORTESTS();
//			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='copyOfVTitle'  and @lang='xyz']");
//			Assert.AreEqual("peace", textarea2.InnerText);
//		}


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

			//note: our code currently only knows how to display French *in French*; the other come out in English.
			//That's not part of this test, and will have to be changed as we improve that aspect of things.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//span[text()='Thai, français, Spanish']", 1);

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
		public void SetupPage_LanguageSettingsHaveChanged_LangAttributesUpdated()
		{
				_bookDom = new HtmlDom(@"
				<html>
					<body>
					   <div id='me' class='bloom-page'>
							<div>
								 <div data-book='somethingInN1' lang='du' data-metalanguage='N1'></div>
								<div data-book='somethingInN2' lang='du' data-metalanguage='N2'></div>
								<div data-book='somethingInV' lang='du' data-metalanguage='V'></div>
							</div>
						</div>
					</body>
				</html>");

			var book = CreateBook();

			//BookStarter.SetupPage((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0], _librarySettings.Object, "abc", "def");
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='somethingInN1' and @lang='en']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='somethingInN2' and @lang='fr']", 1);
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='somethingInV' and @lang='xyz']", 1);
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
			Assert.AreEqual("bloom-page A5Portrait", GetPageFromBookDom(book, 1).GetStringAttribute("class"));
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
			Assert.AreEqual("bloom-page somekind A5Portrait", GetPageFromBookDom(book, 1).GetStringAttribute("class"));
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

		//
		//        [Test]
		//        public void DeletePage_RaisesDeletedEvent()
		//        {
		//            var book = CreateBook();
		//            bool gotEvent=false;
		//            book.PageDeleted+=new EventHandler((x,y)=>gotEvent=true);
		//            var original = book.GetPages().Count();
		//            Page existingPage = book.GetPages().Last();
		//            book.DeletePage(existingPage);
		//            Assert.IsTrue(gotEvent);
		//        }

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
			var book = CreateBook();
			foreach (var page in book.GetPages())
			{
				book.DeletePage(page);
			}
			AssertPageCount(book, 1);
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
		public void GetDefaultBookletLayout_NotSpecified_Fold()
		{
			_bookDom = new HtmlDom(@"<html ><head>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.SideFold, book.GetDefaultBookletLayout());
		}

		[Test]
		public void GetDefaultBookletLayout_CalendarSpecified_Calendar()
		{

			_bookDom = new HtmlDom(@"<html ><head>
									<meta name='defaultBookletLayout' content='Calendar'/>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.Calendar, book.GetDefaultBookletLayout());
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
			_bookDom = new HtmlDom(@"
				<html>
					<head>
						<meta name='xmatter' content='Traditional'/>
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

			//only shells & templates get updated (xmatter injected)
			book.TypeOverrideForUnitTests = Bloom.Book.Book.BookType.Shell;
			var imagePath = book.FolderPath.CombineForPath("theCover.png");
			MakeSamplePngImageWithMetadata(imagePath);

			//book.BringBookUpToDate(new NullProgress());
			var dom = book.GetPreviewHtmlFileForWholeBook();
			
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//*[@style=\"background-image:url('theCover.png')\"]", 1);
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
			Assert.That(book.BookInfo.TagsList, Is.EqualTo("Animal stories"));

			book.OurHtmlDom.SetBookSetting("topic", "en", "Science");
			book.Save();
			Assert.That(book.BookInfo.TagsList, Is.EqualTo("Science"));
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
	}
}
