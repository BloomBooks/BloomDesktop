﻿using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Publish;
using Moq;
using NUnit.Framework;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Progress;
using Palaso.TestUtilities;
using Palaso.UI.WindowsForms.ImageToolbox;
using Palaso.Xml;

namespace BloomTests.Book
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
		private TemporaryFolder _tempFolder;
		private CollectionSettings _collectionSettings;

		[SetUp]
		public void Setup()
		{
			_storage = new Moq.Mock<IBookStorage>();
			_storage.SetupGet(x => x.LooksOk).Returns(true);
			_documentDom = GetThreePageDom();
			_storage.SetupGet(x => x.Dom).Returns(()=>_documentDom);
			_storage.SetupGet(x => x.Key).Returns("testkey");
			_storage.SetupGet(x => x.FileName).Returns("testTitle");
			_storage.SetupGet(x => x.BookType).Returns(Bloom.Book.Book.BookType.Publication);
			_storage.Setup(x => x.GetRelocatableCopyOfDom(It.IsAny<IProgress>())).Returns(()=>
																						{
																							return (XmlDocument) _documentDom.Clone();
																						});// review: the real thing does more than just clone
			_storage.Setup(x => x.GetFileLocator()).Returns(()=>_fileLocator.Object);

			_testFolder = new TemporaryFolder("BookTests");
			_tempFolder = new TemporaryFolder(_testFolder, "book");
			_storage.SetupGet(x => x.FolderPath).Returns(_tempFolder.Path);// review: the real thing does more than just clone


			_templateFinder = new Moq.Mock<ITemplateFinder>();
			_fileLocator = new Moq.Mock<IFileLocator>();
			string root = FileLocator.GetDirectoryDistributedWithApplication("root");
			string xMatter = FileLocator.GetDirectoryDistributedWithApplication("xMatter");
			string factoryCollections = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections");
			string templates = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections","templates");
			_fileLocator.Setup(x => x.LocateFile("languageDisplayTemplate.css")).Returns(root.CombineForPath("languageDisplayTemplate.css"));
			_fileLocator.Setup(x => x.LocateFile("previewMode.css")).Returns("../notareallocation/previewMode.css");
			_fileLocator.Setup(x => x.LocateFile("editMode.css")).Returns("../notareallocation/editMode.css");
			_fileLocator.Setup(x => x.LocateFile("editTranslationMode.css")).Returns("../notareallocation/editTranslationMode.css");
			_fileLocator.Setup(x => x.LocateFile("editOriginalMode.css")).Returns("../notareallocation/editOriginalMode.css");
			_fileLocator.Setup(x => x.LocateFile("basePage.css")).Returns("../notareallocation/basePage.css");
			_fileLocator.Setup(x => x.LocateFile("bloomBootstrap.js")).Returns("../notareallocation/bloomBootstrap.js");
			_fileLocator.Setup(x => x.LocateDirectory("Factory-XMatter")).Returns(xMatter.CombineForPath("Factory-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectory("Factory-XMatter", It.IsAny<string>())).Returns(xMatter.CombineForPath("Factory-XMatter"));
			_fileLocator.Setup(x => x.LocateFile("Factory-XMatter".CombineForPath("Factory-XMatter.htm"))).Returns(xMatter.CombineForPath("Factory-XMatter", "Factory-XMatter.htm"));

			//warning: we're neutering part of what the code under test is trying to do here:
			_fileLocator.Setup(x => x.CloneAndCustomize(It.IsAny<IEnumerable<string>>())).Returns(_fileLocator.Object);

			_thumbnailer = new Moq.Mock<HtmlThumbNailer>(new object[] { 60 });
			_pageSelection = new Mock<PageSelection>();
			_pageListChangedEvent = new PageListChangedEvent();
	  }
		[TearDown]
		public void TearDown()
		{
			_testFolder.Dispose();
		}

		private Bloom.Book.Book CreateBook()
		{
			_collectionSettings = new CollectionSettings(new NewCollectionSettings() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_testFolder.Path, "test"), Language1Iso639Code = "xyz", Language2Iso639Code = "en", Language3Iso639Code = "fr" });
			return new Bloom.Book.Book(_storage.Object, true, _templateFinder.Object,
				_collectionSettings,
				_thumbnailer.Object, _pageSelection.Object, _pageListChangedEvent, new BookRefreshEvent());
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
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter'))]", 3);
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
//			book.UpdateFieldsAndVariables();
//			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='copyOfVTitle'  and @lang='xyz']");
//			Assert.AreEqual("peace", textarea2.InnerText);
//		}


		[Test]
		public void UpdateFieldsAndVariables_VernacularTitleChanged_TitleCopiedToTextAreaOnAnotherPage()
		{
			var book = CreateBook();
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='2' and @lang='xyz']");
			textarea1.InnerText = "peace";
			book.UpdateFieldsAndVariables(null,dom);
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='copyOfVTitle'  and @lang='xyz']");
			Assert.AreEqual("peace", textarea2.InnerText);
		}


		[Test]
		public void UpdateFieldsAndVariables_CustomLibraryVariable_CopiedToOtherElement()
		{
			var book = CreateBook();
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			book.UpdateFieldsAndVariables(null,dom);
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='bb']");
			Assert.AreEqual("aa", textarea2.InnerText);
		}


		[Test]
		public void UpdateFieldsAndVariables_VernacularTitleChanged_TitleCopiedToParagraphAnotherPage()
		{
			SetDom(@"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' data-book='bookTitle'>original</textarea>
						</p>
					</div>
				<div class='bloom-page' id='0a99fad3-0a17-4240-a04e-86c2dd1ec3bd'>
						<p class='centered' lang='xyz' data-book='bookTitle' id='P1'>originalButNoExactlyCauseItShouldn'tMatter</p>
				</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@data-book='bookTitle' and @lang='xyz']");
			textarea1.InnerText = "peace";
			book.UpdateFieldsAndVariables(null,dom);
			var paragraph = dom.SelectSingleNodeHonoringDefaultNS("//p[@data-book='bookTitle'  and @lang='xyz']");
			Assert.AreEqual("peace", paragraph.InnerText);
		}


		[Test]
		public void UpdateFieldsAndVariables_ElementHasMultipleLanguages_OnlyTheVernacularChanged()
		{
			var book = CreateBook();
			var dom = book.RawDom;
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='tree']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='dog']", 1);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='2']");
			textarea1.InnerText = "peace";
			book.UpdateFieldsAndVariables(null,dom);
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='copyOfVTitle']");
			Assert.AreEqual("peace", textarea2.InnerText);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and text()='tree']",1);
		}

		[Test]
		public void UpdateFieldsAndVariables_ElementIsNationalLanguage_UpdatesOthers()
		{
			var book = CreateBook();
			var dom = book.RawDom;
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and @id='1' and text()='tree']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz'  and @id='2' and text()='dog']", 1);
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='2']");
			textarea1.InnerText = "peace";
			book.UpdateFieldsAndVariables(null,dom);
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@lang='xyz' and @id='copyOfVTitle']");
			Assert.AreEqual("peace", textarea2.InnerText);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en' and text()='tree']", 1);
		}


		[Test]
		public void UpdateFieldsAndVariables_HadNoTitleChangeVernacularTitle_SetTitleElement()
		{
			SetDom(@"<div class='bloom-page' id='guid2'>
						<p>
							<textarea lang='xyz' data-book='bookTitle'>original</textarea>
						</p>
					</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			XmlElement textArea = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//textarea[@data-book='bookTitle']");
			textArea.InnerText ="blue";
			book.UpdateFieldsAndVariables(null,dom);
			XmlElement title = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//title");
			Assert.AreEqual("blue", title.InnerText);
		}



		[Test]
		public void UpdateFieldsAndVariables_BookTitleInSpanOnSecondPage_UpdatesH2OnFirstWithCurrentNationalLang()
		{
			SetDom(@"<div class='bloom-page titlePage'>
						<div class='pageContent'>
							<h2 data-book='bookTitle' lang='N1'>{national book title}</h2>
						</div>
					</div>
				<div class='bloom-page verso'>
					<div class='pageContent'>
						(<span lang='en' data-book='bookTitle'>Vaccinations</span><span lang='tpi' data-book='bookTitle'>Tambu Sut</span>)
						<br />
					</div>
				</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			book.UpdateFieldsAndVariables(null,dom);
			XmlElement nationalTitle = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
			Assert.AreEqual("Vaccinations", nationalTitle.InnerText);

			//now switch the national language to Tok Pisin

			_collectionSettings.Language2Iso639Code = "tpi";
			book.UpdateFieldsAndVariables(null,dom);
			nationalTitle = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//h2[@data-book='bookTitle']");
			Assert.AreEqual("Tambu Sut", nationalTitle.InnerText);
		}

		[Test]
		public void UpdateFieldsAndVariables_InsertsRegionalLanguageNameInAsWrittenInNationalLanguage1()
		{
			SetDom(@"<div class='bloom-page'>
						 <span data-library='nameOfNationalLanguage2' lang='en'>{Regional}</span>
					</div>
			");
			var book = CreateBook();
			var dom = book.RawDom;
			book.UpdateFieldsAndVariables(null,dom);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//span[text()='French']",1);
		}


		[Test]
		public void UpdateFieldsAndVariables_HadTitleChangeEnglishTitle_ChangesTitleElement()
		{
			var book = CreateBook();
			var dom = book.RawDom;
			XmlElement head = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//head");
			head.AppendChild(dom.CreateElement("title")).InnerText = "original";

			XmlElement title = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//title");
			Assert.AreEqual("tree", title.InnerText);

			XmlElement textArea = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//textarea[@data-book='bookTitle' and @lang='en']");
			textArea.InnerText = "shrub";
			book.UpdateFieldsAndVariables(null,dom);
			title = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS("//title");
			Assert.AreEqual("shrub", title.InnerText);
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
			var vernacularTextNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@lang='xyz']");

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
			var textNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@id='testText' and @lang='xyz']");

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
			var vernacularTextNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@id='2' and @lang='xyz']");

			Assert.AreEqual("changed", vernacularTextNodesInStorage.Item(0).InnerText, "the value didn't get copied to  the storage dom");
		 }


		[Test]
		public void GetEditableHtmlDomForPage_HasInjectedElementForEditTimeScript()
		{
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
			var scriptNodes = dom.SafeSelectNodes("//script");
			Assert.AreEqual(3, scriptNodes.Count);
			Assert.IsNotEmpty(scriptNodes[2].Attributes["src"].Value);
			Assert.IsTrue(scriptNodes[2].Attributes["src"].Value.Contains(".js"));
		}


		[Test]
		public void GetEditableHtmlDomForPage_BasicBook_HasA5PortraitClass()
		{
			var book = CreateBook();
			book.SetLayout(new Layout() { SizeAndOrientation = SizeAndOrientation.FromString("A5Portrait") });
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'A5Portrait') and contains(@class,'bloom-page')]", 1);
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
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html ><head>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.SideFold, book.GetDefaultBookletLayout());
		}

		[Test]
		public void GetDefaultBookletLayout_CalendarSpecified_Calendar()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html ><head>
									<meta name='defaultBookletLayout' content='Calendar'/>
									</head><body></body></html>");
			var book = CreateBook();
			Assert.AreEqual(PublishModel.BookletLayoutMethod.Calendar, book.GetDefaultBookletLayout());
		}

		[Test]
		public void UpdateDataDiv_DoesNotExist_MakesOne()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html><head></head><body><div data-book='hello'>world</div></body></html>");
			var book = CreateBook();
			book.UpdateVariablesAndDataDiv(_documentDom);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']",1);//NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='hello' and text()='world']",1);
		}

		[Test]
		public void UpdateDataDiv_HasTrilingualLanguages_AddsToDataDiv()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html><head></head><body></body></html>");
			var book = CreateBook();
			book.SetMultilingualContentLanguages("okm", "kbt");
			book.UpdateVariablesAndDataDiv(_documentDom);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage1' and text()='xyz']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage2' and text()='okm']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3' and text()='kbt']", 1);
		}
		[Test]
		public void UpdateDataDiv_ThirdContentLangTurnedOff_RemovedFromDataDiv()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage2'>xyz</div><div data-book='contentLanguage3'>kbt</div></div></head><body></body></html>");
			var book = CreateBook();
			book.SetMultilingualContentLanguages(null, null);
			book.UpdateVariablesAndDataDiv(_documentDom);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='contentLanguage3']", 0);
		}

		[Test]
		public void UpdateDataDiv_DomHas2ContentLanguages_PulledIntoBookProperties()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html><head><div id='bloomDataDiv'><div data-book='contentLanguage2'>okm</div><div data-book='contentLanguage3'>kbt</div></div></head><body></body></html>");
			var book = CreateBook();
			book.UpdateVariablesAndDataDiv(_documentDom);
			Assert.AreEqual("okm", book.MultilingualContentLanguage2);
			Assert.AreEqual("kbt", book.MultilingualContentLanguage3);
		}


		[Test]
		public void UpdateDataDiv_NewLangAdded_AddedToDataDiv()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html><head></head><body><div data-book='hello' lang='en'>hi</div></body></html>");
			var book = CreateBook();

			var e = book.RawDom.CreateElement("div");
			e.SetAttribute("data-book", "hello");
			e.SetAttribute("lang", "fr");
			e.InnerText = "bonjour";
			book.RawDom.SelectSingleNode("//body").AppendChild(e);

			book.UpdateVariablesAndDataDiv(_documentDom);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1);//NB microsoft uses 1 as the first. W3c uses 0.
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='hello' and @lang='en' and text()='hi']", 1);
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='hello' and @lang='fr' and text()='bonjour']", 1);
		}

		[Test]
		public void UpdateDataDiv_HasDataLibraryValues_LibraryValuesNotPutInDataDiv()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html><head></head><body><div data-book='hello' lang='en'>hi</div><div data-library='user' lang='en'>john</div></body></html>");
			var book = CreateBook();


			book.UpdateVariablesAndDataDiv(_documentDom);
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='user']");
			AssertThatXmlIn.Dom(book.RawDom).HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-library]");
		}

		/// <summary>
		/// regression test... when we rebuild the xmatter, we also need to update the html attributes that let us
		/// know the state of the image metadata without having to open the image up (slow).
		/// </summary>
		[Test, Ignore("breaks on team city for some reason")]
		public void UpdateXMatter_CoverImageHasMetaData_HtmlForCoverPageHasMetaDataAttributes()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"
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
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div/div/div/img[@data-creator='joe']",1);
		}

		[Test, Ignore("breaks on team city for some reason")]
		public void UpdateImgMetdataAttributesToMatchImage_HtmlForImgGetsMetaDataAttributes()
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"
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
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div/div/div/img[@data-creator='joe']", 1);
		}

		private void MakeSamplePngImageWithMetadata(string path)
		{
			var x = new Bitmap(10, 10);
			x.Save(path, ImageFormat.Png);
			x.Dispose();
			using (var img = PalasoImage.FromFile(path))
			{
				img.Metadata.Creator = "joe";
				img.Metadata.CopyrightNotice = "Copyright 1999 by me";
				img.SaveUpdatedMetadataIfItMakesSense();
			}
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
			dom.LoadXml(@"<html ><head></head><body>
				<div class='bloom-page' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>dog</textarea>
					</p>
				</div>
				<div class='bloom-page' id='guid2'>
					<p>
						<textarea lang='en' id='3'>english</textarea>
						<textarea lang='xyz' id='4'>originalVernacular</textarea>
						<textarea lang='tpi' id='5'>tokpsin</textarea>
					</p>
					<img id='img1' src='original.png'/>
				</div>
				<div class='bloom-page' id='guid3'>
					<p>
						<textarea id='6' lang='xyz'>original2</textarea>
					</p>
					<p>
						<textarea lang='xyz' id='copyOfVTitle'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='aa'  data-library='testLibraryVariable'>aa</textarea>
					   <textarea lang='xyz' id='bb'  data-library='testLibraryVariable'>bb</textarea>

					</p>
				</div>
				</body></html>");
			return dom;
		}

		private void SetDom(string bodyContents)
		{
			_documentDom = new XmlDocument();
			_documentDom.LoadXml(@"<html ><head></head><body>" + bodyContents + "</body></html>");
		}
	}
}
