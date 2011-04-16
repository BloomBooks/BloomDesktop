using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom;
using Bloom.Edit;
using Moq;
using NUnit.Framework;
using Palaso.IO;
using Palaso.TestUtilities;
using Palaso.Xml;

namespace BloomTests
{
	[TestFixture]
	public class BookTests
	{
		private Mock<IBookStorage> _storage;
		private Mock<ITemplateFinder> _templateFinder;
		private Mock<IFileLocator> _fileLocator;
		private Mock<HtmlThumbNailer> _thumbnailer;
		private Mock<PageSelection> _pageSelection;
		private PageListChangedEvent _pageListChangedEvent;
		private XmlDocument _documentDom;
		private TemporaryFolder _testFolder;

		[SetUp]
		public void Setup()
		{
			_storage = new Moq.Mock<IBookStorage>();
			_storage.SetupGet(x => x.LooksOk).Returns(true);
			_documentDom = GetThreePageDom();
			_storage.SetupGet(x => x.Dom).Returns(()=>_documentDom);
			_storage.SetupGet(x => x.Key).Returns("testkey");
			_storage.SetupGet(x => x.FileName).Returns("testTitle");
			_storage.SetupGet(x => x.BookType).Returns(Book.BookType.Publication);
			_storage.Setup(x => x.GetRelocatableCopyOfDom()).Returns((XmlDocument)_documentDom.Clone());// review: the real thing does more than just clone

			_templateFinder = new Moq.Mock<ITemplateFinder>();
			_fileLocator = new Moq.Mock<IFileLocator>();
			_fileLocator.Setup(x => x.LocateFile("previewMode.css")).Returns("../notareallocation/previewMode.css");
			_fileLocator.Setup(x => x.LocateFile("editMode.css")).Returns("../notareallocation/editMode.css");
			_fileLocator.Setup(x => x.LocateFile("basePage.css")).Returns("../notareallocation/basePage.css");
			_fileLocator.Setup(x => x.LocateFile("Edit-TimeScripts.js")).Returns("../notareallocation/Edit-TimeScripts.js");


			_thumbnailer = new Moq.Mock<HtmlThumbNailer>(new object[] { 60 });
			_pageSelection = new Mock<PageSelection>();
			_pageListChangedEvent = new PageListChangedEvent();
			_testFolder = new TemporaryFolder("BookTests");

	  }

		private Book CreateBook()
		{
			return new Book(_storage.Object, true, _templateFinder.Object, _fileLocator.Object,
				new ProjectSettings(new NewProjectInfo() {PathToSettingsFile=ProjectSettings.GetPathForNewSettings(_testFolder.Path,"test"), Iso639Code = "xyz" }),
				_thumbnailer.Object, _pageSelection.Object, _pageListChangedEvent);
		}

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
			var result = CreateBook().GetPreviewHtmlFileForWholeBook().StripXHtmlNameSpace();
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, '-bloom-page')]",3);
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

		/// <summary>
		/// What we're testing here is that boxes that are supposed to show in the national language
		/// are saved when changed.
		/// </summary>
		[Test]
		public void SavePage_ChangeMadeToTexAreaWhichIsLabelledShowNational_StorageUpdatedAndToldToSave()
		{
			SetDom(@"<div class='-bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='testsNeedIds' class='-bloom-showNational'>one</textarea>
						</p>
					</div>
			");

			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea");
			Assert.AreEqual("one", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "two";
			book.SavePage(dom);
			var textAreaInStorageDom = _storage.Object.Dom.SelectSingleNodeHonoringDefaultNS("//textarea");

			Assert.AreEqual("two", textAreaInStorageDom.InnerText,
							"the value didn't get copied to  the storage dom");
			_storage.Verify(s => s.Save(), Times.Once());
		}

		[Test]
		public void MakeAllFieldsConsistent_VernacularTitleChanged_TitleCopiedToTextAreaOnAnotherPage()
		{
			var book = CreateBook();
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2' and @lang='xyz']");
			textarea1.InnerText = "peace";
			book.MakeAllFieldsConsistent();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='copyOfVTitle'  and @lang='xyz']");
			Assert.AreEqual("peace", textarea2.InnerText);
		}

		[Test]
		public void MakeAllFieldsConsistent_VernacularTitleChanged_TitleCopiedToParagraphAnotherPage()
		{
			SetDom(@"<div class='-bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' class='-bloom-vernacularBookTitle'>original</textarea>
						</p>
					</div>
				<div class='-bloom-page' id='0a99fad3-0a17-4240-a04e-86c2dd1ec3bd'>
						<p class='centered -bloom-vernacularBookTitle' lang='xyz' id='P1'>originalButNoExactlyCauseItShouldn'tMatter</p>
				</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[contains(@class,'-bloom-vernacularBookTitle') and @lang='xyz']");
			textarea1.InnerText = "peace";
			book.MakeAllFieldsConsistent();
			var paragraph = dom.SelectSingleNodeHonoringDefaultNS("//p[contains(@class,'-bloom-vernacularBookTitle')  and @lang='xyz']");
			Assert.AreEqual("peace", paragraph.InnerText);
		}


		[Test]
		public void MakeAllFieldsConsistent_ElementHasMultipleLanguages_OnlyTheVernacularChanged()
		{
			var book = CreateBook();
			var dom = book.RawDom;
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='tree']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='dog']", 1);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='2']");
			textarea1.InnerText = "peace";
			book.MakeAllFieldsConsistent();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='copyOfVTitle']");
			Assert.AreEqual("peace", textarea2.InnerText);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and text()='tree']",1);
		}

		[Test]
		public void MakeAllFieldsConsistent_ElementIsNationalLanguage_UpdatesOthers()
		{
			var book = CreateBook();
			var dom = book.RawDom;
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='tree']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='dog']", 1);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='2']");
			textarea1.InnerText = "peace";
			book.MakeAllFieldsConsistent();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='copyOfVTitle']");
			Assert.AreEqual("peace", textarea2.InnerText);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and text()='tree']", 1);
		}


		[Test]
		public void MakeAllFieldsConsistent_HadNoTitleChangeVernacularTitle_SetTitleElement()
		{
			SetDom(@"<div class='-bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' class='-bloom-vernacularBookTitle'>original</textarea>
						</p>
					</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			XmlElement textArea = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//textarea[@class='-bloom-vernacularBookTitle']");
			textArea.InnerText ="blue";
			book.MakeAllFieldsConsistent();
			XmlElement title = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//title");
			Assert.AreEqual("blue", title.InnerText);
		}

		[Test]
		public void MakeAllFieldsConsistent_HadTitleChangeVernacularTitle_ChangesTitleElement()
		{
			var book = CreateBook();
			var dom = book.RawDom;
			XmlElement head = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//head");
			head.AppendChild(dom.CreateElement("title", "http://www.w3.org/1999/xhtml")).InnerText = "original";
		   // node.SetAttribute("class", "-bloom-vernacularBookTitle");
			XmlElement textArea = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//textarea[@class='-bloom-vernacularBookTitle']");
			textArea.InnerText = "blue";
			book.MakeAllFieldsConsistent();
			XmlElement title = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//title");
			Assert.AreEqual("dog", title.InnerText);
		}

		[Test]
		public void MakeAllFieldsConsistent_ChangeVernacularTitle_TellsStorageToChangeName()
		{
			SetDom(@"<div class='-bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' class='-bloom-vernacularBookTitle'>red</textarea>
						</p>
					</div>
			");

			var book = CreateBook();
			var dom = book.RawDom;
			XmlElement textArea = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//textarea[@class='-bloom-vernacularBookTitle']");
			textArea.InnerText = "blue";
			_storage.Setup(s => s.SetBookName("blue"));
			book.MakeAllFieldsConsistent();
			_storage.Verify(s=>s.SetBookName("blue"));
		}

		[Test]
		public void SavePage_ChangeMade_StorageToldToSave()
		{
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
			book.SavePage(dom);
			_storage.Verify(s => s.Save(), Times.Once());
		}

		[Test]
		public void SavePage_ChangeMadeToSrcOfImg_StorageUpdated()
		{
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
			var imgInEditingDom = dom.SelectSingleNodeHonoringDefaultNS("//img[@id='img1']") as XmlElement;
			imgInEditingDom.SetAttribute("src", "changed.png");

			book.SavePage(dom);
			var imgInStorage = _storage.Object.Dom.SelectSingleNodeHonoringDefaultNS("//img[@id='img1']") as XmlElement;

			Assert.AreEqual("changed.png", imgInStorage.GetAttribute("src"));
		}



		[Test]
		public void SavePage_ChangeMadeToTextAreaOfFirstTwin_StorageUpdated()
		{
			SetDom(@"<div class='-bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='testText'>english</textarea>
							<textarea lang='xyz' id='testText'>originalVernacular</textarea>
						</p>
					</div>
					<div class='-bloom-page' id='guid3'>
						<p>
							<textarea  lang='xyz' id='testText'>original2</textarea>
						</p>
					</div>
			");
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='testText' and @lang='xyz']");
			Assert.AreEqual("originalVernacular", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "changed";
			book.SavePage(dom);
			var vernacularTextNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@id='testText' and @lang='xyz']");

			Assert.AreEqual("changed", vernacularTextNodesInStorage.Item(0).InnerText, "the value didn't get copied to  the storage dom");
			Assert.AreEqual("original2", vernacularTextNodesInStorage.Item(1).InnerText, "the second copy of this page should not have been changed");
		}


		[Test]
		public void SavePage_ChangeMadeToTextAreaOfSecondTwin_StorageUpdated()
		{
			SetDom(@"<div class='-bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='testText'>english</textarea>
							<textarea lang='xyz' id='testText'>original1</textarea>
						</p>
					</div>
					<div class='-bloom-page' id='guid3'>
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
			var textNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@id='testText' and @lang='xyz']");

			Assert.AreEqual("original1", textNodesInStorage.Item(0).InnerText, "the first copy of this page should not have been changed");
			Assert.AreEqual("changed", textNodesInStorage.Item(1).InnerText, "the value didn't get copied to  the storage dom");
		}

		[Test]
		public void SavePage_ChangeMadeToTextAreaWithMultipleLanguages_CorrectOneInStorageUpdated()
		{
			SetDom(@"<div class='-bloom-page' id='guid2'>
						<p>
							<textarea lang='en' id='testText'>english</textarea>
							<textarea lang='xyz' id='testText'>originalVernacular</textarea>
							<textarea lang='tpi' id='testText'>tokpsin</textarea>
						</p>
					</div>
			");
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[0]);
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='testText' and @lang='xyz']");
			Assert.AreEqual("originalVernacular", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "changed";
			book.SavePage(dom);
			var vernacularTextNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@id='testText' and @lang='xyz']");

			Assert.AreEqual("changed", vernacularTextNodesInStorage.Item(0).InnerText, "the value didn't get copied to  the storage dom");
		 }





		[Test]
		public void GetEditableHtmlDomForPage_HasInjectedElementForEditTimeScript()
		{
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
			var scriptNodes = dom.SafeSelectNodes("//script");
			Assert.AreEqual(1, scriptNodes.Count);
			Assert.IsNotEmpty(scriptNodes[0].Attributes["src"].Value);
			Assert.IsTrue(scriptNodes[0].Attributes["src"].Value.Contains(".js"));
		}

		[Test]
		public void InsertPageAfter_OnFirstPage_NewPageInsertedAsSecond()
		{
			var book = CreateBook();
			var existingPage=book.GetPages().First();
			TestTemplateInsertion(book, existingPage, "<div class='-bloom-page somekind'>hello</div>");
		}
		[Test]
		public void InsertPageAfter_OnLastPage_NewPageInsertedAtEnd()
		{
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			TestTemplateInsertion(book, existingPage,"<div class='-bloom-page somekind'>hello</div>");
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
			Mock<IPage> templatePage = CreateTemplatePage("<div class='-bloom-page -bloom-extraPage'>hello</div>");
			book.InsertPageAfter(existingPage, templatePage.Object);
			Assert.AreEqual("-bloom-page", GetPageFromBookDom(book, 1).GetStringAttribute("class"));
		}


		[Test]
		public void InsertPageAfter_SourcePageHasLineage_GetsLineageOfSourcePlusItsAncestor()
		{
			//enhance: move to book starter tests, since that's what implements the actual behavior
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			Mock<IPage> templatePage = CreateTemplatePage("<div class='-bloom-page -bloom-extraPage' id='ma'><a href='grandma' class='-bloom-pageLineage'></a>hello</div>");
			book.InsertPageAfter(existingPage, templatePage.Object);
			XmlElement page = (XmlElement) GetPageFromBookDom(book, 1);
			AssertThatXmlIn.String(page.OuterXml).HasSpecifiedNumberOfMatchesForXpath("//div/a[@class='-bloom-pageLineage']", 1);
			string[] guids = GetLineageGuids(page);
			Assert.AreEqual("grandma",guids[0]);
			Assert.AreEqual("ma", guids[1]);
			Assert.AreEqual(2, guids.Length);
		}

		private string[] GetLineageGuids(XmlElement page)
		{
			XmlElement node = (XmlElement) page.SelectSingleNodeHonoringDefaultNS("//div/a[@class='-bloom-pageLineage']");
			var href = node.GetAttribute("href");
			return href.Split(new char[]{';'});
		}

		[Test]
		public void InsertPageAfter_SourcePageHasNoLineage_IdOfSourceBecomesLineageOfNewPage()
		{
			//enhance: move to book starter tests, since that's what implements the actual behavior
			var book = CreateBook();
			var existingPage = book.GetPages().First();
			Mock<IPage> templatePage = CreateTemplatePage("<div class='-bloom-page -bloom-extraPage' id='ma'>hello</div>");
			book.InsertPageAfter(existingPage, templatePage.Object);
			XmlElement page = (XmlElement)GetPageFromBookDom(book, 1);
			AssertThatXmlIn.String(page.OuterXml).HasSpecifiedNumberOfMatchesForXpath("//div/a[@class='-bloom-pageLineage']", 1);
			string[] guids = GetLineageGuids(page);
			Assert.AreEqual("ma", guids[0]);
			Assert.AreEqual(1, guids.Length);
		}

		private void TestTemplateInsertion(Book book, IPage existingPage, string divContent)
		{
			Mock<IPage> templatePage = CreateTemplatePage(divContent);

		   book.InsertPageAfter(existingPage, templatePage.Object);
			AssertPageCount(book, 4);
			Assert.AreEqual("-bloom-page somekind", GetPageFromBookDom(book, 1).GetStringAttribute("class"));
		}

		private XmlNode GetPageFromBookDom(Book book, int pageNumber0Based)
		{
			var result = book.RawDom.StripXHtmlNameSpace();
			return result.SafeSelectNodes("//div[contains(@class, '-bloom-page')]", null)[pageNumber0Based];
		}

		private void AssertPageCount(Book book, int expectedCount)
		{
			var result = book.RawDom.StripXHtmlNameSpace();
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, '-bloom-page')]", expectedCount);
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




		private Mock<IPage> CreateTemplatePage(string divContent)
		{
			var templatePage = new Moq.Mock<IPage>();
			XmlDocument d = new XmlDocument();
			d.LoadXml("<wrapper>"+divContent+"</wrapper>");
			XmlElement x1 = (XmlElement) d.SelectSingleNode("//div");
			templatePage.Setup(x=>x.GetDivNodeForThisPage()).Returns(x1);
			return templatePage;
		}



		private XmlDocument GetThreePageDom()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html  xmlns='http://www.w3.org/1999/xhtml'><head></head><body>
				<div class='-bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1' class='-bloom-vernacularBookTitle'>tree</textarea>
						<textarea lang='xyz' id='2' class='-bloom-vernacularBookTitle'>dog</textarea>
					</p>
				</div>
				<div class='-bloom-page' id='guid2'>
					<p>
						<textarea lang='en' id='3'>english</textarea>
						<textarea lang='xyz' id='4'>originalVernacular</textarea>
						<textarea lang='tpi' id='5'>tokpsin</textarea>
					</p>
					<img id='img1' src='original.png'/>
				</div>
				<div class='-bloom-page' id='guid3'>
					<p>
						<textarea id='6' lang='xyz'>original2</textarea>
					</p>
					<p>
						<textarea lang='xyz' id='copyOfVTitle' class='-bloom-vernacularBookTitle'>tree</textarea>
					</p>
				</div>
				</body></html>");
			return dom;
		}

		private void SetDom(string bodyContents)
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html  xmlns='http://www.w3.org/1999/xhtml'><head></head><body>" + bodyContents + "</body></html>");
		}
	}
}
