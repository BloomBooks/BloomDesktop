using System.Collections.Generic;
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
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageToolbox;
using Palaso.Xml;
using System;

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
		private TemporaryFolder _testFolder;
		private TemporaryFolder _tempFolder;
		private CollectionSettings _collectionSettings;
		private HtmlDom _bookDom;
		private BookInfo _metadata;

		[SetUp]
		public void Setup()
		{
			_storage = new Moq.Mock<IBookStorage>();
			_storage.Setup(x => x.GetLooksOk()).Returns(true);
			_bookDom = new HtmlDom(GetThreePageDom());
			_storage.SetupGet(x => x.Dom).Returns(() => _bookDom);
			_storage.SetupGet(x => x.Key).Returns("testkey");
			_storage.SetupGet(x => x.FileName).Returns("testTitle");
			_storage.Setup(x => x.GetRelocatableCopyOfDom(It.IsAny<IProgress>())).Returns(()=>
																							  {
																								  return
																									  _bookDom.Clone();
																							  });// review: the real thing does more than just clone
			_storage.Setup(x => x.MakeDomRelocatable(It.IsAny<HtmlDom>(),It.IsAny<IProgress>())).Returns(
				(HtmlDom x, IProgress y) => {return x.Clone();});// review: the real thing does more than just clone

			_storage.Setup(x => x.GetFileLocator()).Returns(()=>_fileLocator.Object);

			_testFolder = new TemporaryFolder("BookTests");
			_tempFolder = new TemporaryFolder(_testFolder, "book");
			MakeSamplePngImageWithMetadata(Path.Combine(_tempFolder.Path,"original.png"));
			_storage.SetupGet(x => x.FolderPath).Returns(_tempFolder.Path);// review: the real thing does more than just clone
			_metadata = new BookInfo(_tempFolder.Path, true);
			_storage.SetupGet(x => x.MetaData).Returns(_metadata);

			_templateFinder = new Moq.Mock<ITemplateFinder>();
			_fileLocator = new Moq.Mock<IFileLocator>();
			string root = FileLocator.GetDirectoryDistributedWithApplication("BloomBrowserUI");
			string xMatter = FileLocator.GetDirectoryDistributedWithApplication("xMatter");
			string factoryCollections = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections");
			string templates = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections","Templates");
			_fileLocator.Setup(x => x.LocateFileWithThrow("languageDisplayTemplate.css")).Returns(root.CombineForPath("bookLayout","languageDisplayTemplate.css"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("previewMode.css")).Returns("../notareallocation/previewMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editMode.css")).Returns("../notareallocation/editMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editTranslationMode.css")).Returns("../notareallocation/editTranslationMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editOriginalMode.css")).Returns("../notareallocation/editOriginalMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editPaneGlobal.css")).Returns("../notareallocation/editPaneGlobal.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("basePage.css")).Returns("../notareallocation/basePage.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("bloomBootstrap.js")).Returns("../notareallocation/bloomBootstrap.js");
			_fileLocator.Setup(x => x.LocateFileWithThrow("bloomPreviewBootstrap.js")).Returns("../notareallocation/bloomPreviewBootstrap.js");
			_fileLocator.Setup(x => x.LocateDirectory("Factory-XMatter")).Returns(xMatter.CombineForPath("Factory-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectory("Factory-XMatter", It.IsAny<string>())).Returns(xMatter.CombineForPath("Factory-XMatter"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("Factory-XMatter".CombineForPath("Factory-XMatter.htm"))).Returns(xMatter.CombineForPath("Factory-XMatter", "Factory-XMatter.htm"));

			//warning: we're neutering part of what the code under test is trying to do here:
			_fileLocator.Setup(x => x.CloneAndCustomize(It.IsAny<IEnumerable<string>>())).Returns(_fileLocator.Object);

			_thumbnailer = new Moq.Mock<HtmlThumbNailer>(new object[] { 60, 60, new MonitorTarget() });
			_pageSelection = new Mock<PageSelection>();
			_pageListChangedEvent = new PageListChangedEvent();
	  }

		[TearDown]
		public void TearDown()
		{
			if (_testFolder != null)
			{
				_testFolder.Dispose();
				_testFolder = null;
			}
		}

		private Bloom.Book.Book CreateBook()
		{
			_collectionSettings = new CollectionSettings(new NewCollectionSettings() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_testFolder.Path, "test"), Language1Iso639Code = "xyz", Language2Iso639Code = "en", Language3Iso639Code = "fr" });
			return new Bloom.Book.Book(_metadata, _storage.Object, _templateFinder.Object,
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
			var result = CreateBook().GetPreviewHtmlFileForWholeBook().RawDom.StripXHtmlNameSpace();
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
		public void UpdateFieldsAndVariables_InsertsRegionalLanguageNameInAsWrittenInNationalLanguage1()
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
		public void GetEditableHtmlDomForPage_HasInjectedElementForEditTimeScript()
		{
			var book = CreateBook();
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
			var scriptNodes = dom.SafeSelectNodes("//script");
			Assert.AreEqual(3, scriptNodes.Count);
			Assert.IsNotEmpty(scriptNodes[0].Attributes["src"].Value);
			Assert.IsTrue(scriptNodes[0].Attributes["src"].Value.Contains(".js"));
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



		/// <summary>
		/// regression test... when we rebuild the xmatter, we also need to update the html attributes that let us
		/// know the state of the image metadata without having to open the image up (slow).
		/// </summary>
		[Test, Ignore("breaks on team city for some reason")]
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
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div/div/div/img[@data-creator='joe']",1);
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
			AssertThatXmlIn.Dom(dom.RawDom).HasSpecifiedNumberOfMatchesForXpath("//img[@src='theCover.png']", 1);
		}

		[Test, Ignore("breaks on team city for some reason")]
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
			AssertThatXmlIn.Dom(book.RawDom).HasSpecifiedNumberOfMatchesForXpath("//div/div/div/img[@data-creator='joe']", 1);
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

			Assert.That(_metadata.IsSuitableForMakingShells, Is.True);
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
			titleElt.InnerText = "changed";
			book.Save();
			Assert.That(_metadata.Title, Is.EqualTo("changed"));
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

		[Test]
		public void Save_UpdatesMetadataTags()
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
						<textarea lang='en' data-book='topic'>original</textarea>
					</div>
				</body></html>");

			var book = CreateBook();

			var topicElt = _bookDom.SelectSingleNode("//textarea");
			topicElt.InnerText = "Animal stories";
			book.Save();
			Assert.That(book.BookInfo.TagsList, Is.EqualTo("Animal stories"));

			// We'd like to check what happens when it is edited again.
			// Problem is, the first save has created a BloomDataDiv which comes before the div we are modifying and
			// has the old value. (This isn't a problem editing the real topic area because editing happens on a
			// cut-down document that only has one page and thus no data-div.)
			var datadiv = _bookDom.SelectSingleNode("//div[@id='bloomDataDiv']");
			datadiv.ParentNode.RemoveChild(datadiv);
			topicElt.InnerText = "Science";
			book.Save();
			Assert.That(book.BookInfo.TagsList, Is.EqualTo("Science"));
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
						</div>
					</div>
					<div class='bloom-page' id='guid3'>
					   <div class='bloom-translationGroup bloom-trailingElement'>
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
				</body></html>");

			var book = CreateBook();
			var allLanguages = book.AllLanguages;
			Assert.That(allLanguages, Has.Member("en"));
			Assert.That(allLanguages, Has.Member("de"));
			Assert.That(allLanguages, Has.Member("fr"));
			Assert.That(allLanguages, Has.Member("es"));
			Assert.That(allLanguages, Has.Member("xkal"));
			Assert.That(allLanguages.Count(), Is.EqualTo(5));
		}

		[Test]
		public void UpdateLicenseMetdata_UpdatesJson()
		{
			var book = CreateBook();

			// Creative Commons License
			var licenseData = new Metadata();
			licenseData.License = CreativeCommonsLicense.FromLicenseUrl("http://creativecommons.org/licenses/by-sa/3.0/");
			licenseData.License.RightsStatement = "Please acknowledge nicely";

			book.UpdateLicenseMetdata(licenseData);

			Assert.That(_metadata.License, Is.EqualTo("cc-by-sa"));
			Assert.That(_metadata.LicenseNotes, Is.EqualTo("Please acknowledge nicely"));

			// Custom License
			licenseData.License = new CustomLicense {RightsStatement = "Use it if you dare"};

			book.UpdateLicenseMetdata(licenseData);

			Assert.That(_metadata.License, Is.EqualTo("custom"));
			Assert.That(_metadata.LicenseNotes, Is.EqualTo("Use it if you dare"));

			// Null License (ask the user)
			licenseData.License = new NullLicense { RightsStatement = "Ask me" };

			book.UpdateLicenseMetdata(licenseData);

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
						<textarea lang='xyz' id='aa'  data-collection='testLibraryVariable'>aa</textarea>
					   <textarea lang='xyz' id='bb'  data-collection='testLibraryVariable'>bb</textarea>

					</p>
				</div>
				</body></html>");
			return dom;
		}

		private void SetDom(string bodyContents)
		{
			_bookDom = new HtmlDom(@"<html ><head></head><body>" + bodyContents + "</body></html>");
		}
	}
}
