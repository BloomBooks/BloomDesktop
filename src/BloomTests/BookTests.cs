using System;
using System.Collections.Generic;
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
		private DeletePageCommand _deletePageCommand;
		private PageListChangedEvent _pageListChangedEvent;
		private RelocatePageEvent _relocatePageEvent;

		[SetUp]
		public void Setup()
		{
			_storage = new Moq.Mock<IBookStorage>();
			_storage.SetupGet(x => x.LooksOk).Returns(true);
			XmlDocument storageDom = GetThreePageDom();
			_storage.SetupGet(x => x.Dom).Returns(storageDom);
			_storage.SetupGet(x => x.Key).Returns("testkey");
			_storage.SetupGet(x => x.FileName).Returns("testTitle");
			_storage.SetupGet(x => x.BookType).Returns(Book.BookType.Publication);
			_storage.Setup(x => x.GetRelocatableCopyOfDom()).Returns((XmlDocument)storageDom.Clone());// review: the real thing does more than just clone

			_templateFinder = new Moq.Mock<ITemplateFinder>();
			_fileLocator = new Moq.Mock<IFileLocator>();
			_fileLocator.Setup(x => x.LocateFile("previewMode.css")).Returns("../notareallocation/previewMode.css");

			_thumbnailer = new Moq.Mock<HtmlThumbNailer>(new object[] { 60 });
			_pageSelection = new Mock<PageSelection>();
			_deletePageCommand=new DeletePageCommand();
			_pageListChangedEvent = new PageListChangedEvent();
			_relocatePageEvent = new RelocatePageEvent();
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
			Assert.IsTrue(CreateBook(true).GetPreviewHtmlFileForWholeBook().InnerXml.Contains("previewMode.css"));
		}

		[Test]
		public void GetPreviewHtmlFileForWholeBook_BookHasThreePages_ResultHasAll()
		{
			var result = CreateBook(true).GetPreviewHtmlFileForWholeBook().StripXHtmlNameSpace();
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'page')]",3);
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

		[Test]
		public void SavePage_ChangeMadeToInputBox_StorageUpdatedAndToldToSave()
		{
			var book = CreateBook(true);
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
			var inputBox = dom.SelectSingleNodeHonoringDefaultNS("//input[@id='testInput']");
			//nb: we dont' have to simulate the business wehre the browser actually puts
			//new values into a "newValue" attribute, since it is required to pull those back
			//into 'value' before the Book's SavePage is called.
			Assert.AreEqual("one", inputBox.Attributes["value"].Value, "the test conditions aren't correct");
			inputBox.Attributes["value"].Value = "two";
			book.SavePage(dom);
			var inputBoxInStorageDom = _storage.Object.Dom.SelectSingleNodeHonoringDefaultNS("//input[@id='testInput']");

			Assert.AreEqual("two", inputBoxInStorageDom.Attributes["value"].Value,
							"the value didn't get copied to  the storage dom");
			_storage.Verify(s => s.Save(), Times.Once());
		}

		[Test]
		public void MakeAllFieldsConsistent_VernacularTitleChanged_TitleCopiedToAnotherPage()
		{
			var book = CreateBook(true);
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			var textarea1 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='vtitle']");
			textarea1.InnerText = "peace";
			book.MakeAllFieldsConsistent();
			var textarea2 = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='copyOfVTitle']");
			Assert.AreEqual("peace", textarea2.InnerText);
		}

		[Test]
		public void MakeAllFieldsConsistent_InputWithUnderscoreClass_CopiedToAnotherInputWithSameClass()
		{
			var book = CreateBook(true);
			var dom = book.RawDom;// book.GetEditableHtmlDomForPage(book.GetPages().First());
			XmlElement input = (XmlElement) dom.SelectSingleNodeHonoringDefaultNS("//input[@id='foo1']");
			input.SetAttribute("value","blue");
			book.MakeAllFieldsConsistent();
			XmlElement input2 = (XmlElement) dom.SelectSingleNodeHonoringDefaultNS("//input[@id='foo2']");
			Assert.AreEqual("blue", input2.GetAttribute("value"));
		}

		[Test]
		public void SavePage_ChangeMade_StorageToldToSave()
		{
			var book = CreateBook(true);
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().First());
			book.SavePage(dom);
			_storage.Verify(s => s.Save(), Times.Once());
		}

		[Test]
		public void SavePage_ChangeMadeToSrcOfImg_StorageUpdated()
		{
			var book = CreateBook(true);
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
			var book = CreateBook(true);
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[1]);
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='testText']");
			Assert.AreEqual("original1", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "changed";
			book.SavePage(dom);
			var textNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@id='testText']");

			Assert.AreEqual("changed", textNodesInStorage.Item(0).InnerText, "the value didn't get copied to  the storage dom");
			Assert.AreEqual("original2", textNodesInStorage.Item(1).InnerText, "the second copy of this page should not have been changed");
		}

		[Test]
		public void SavePage_ChangeMadeToTextAreaOfSecondTwin_StorageUpdated()
		{
			var book = CreateBook(true);
			var dom = book.GetEditableHtmlDomForPage(book.GetPages().ToArray()[2]);
			var textArea = dom.SelectSingleNodeHonoringDefaultNS("//textarea[@id='testText']");
			Assert.AreEqual("original2", textArea.InnerText, "the test conditions aren't correct");
			textArea.InnerText = "changed";
			book.SavePage(dom);
			var textNodesInStorage = _storage.Object.Dom.SafeSelectNodes("//textarea[@id='testText']");

			Assert.AreEqual("original1", textNodesInStorage.Item(0).InnerText, "the first copy of this page should not have been changed");
			Assert.AreEqual("changed", textNodesInStorage.Item(1).InnerText, "the value didn't get copied to  the storage dom");
		}

		[Test]
		public void InsertPageAfter_OnFirstPage_NewPageInsertedAsSecond()
		{
			var book = CreateBook(true);
			var existingPage=book.GetPages().First();
			TestTemplateInsertion(book, existingPage, 1);
		}
		[Test]
		public void InsertPageAfter_OnLastPage_NewPageInsertedAtEnd()
		{
			var book = CreateBook(true);
			var existingPage = book.GetPages().First();
			TestTemplateInsertion(book, existingPage, 1);
		}

		private void TestTemplateInsertion(Book book, IPage existingPage, int expectedLocation)
		{
			Mock<IPage> templatePage = CreateTemplatePage();

			book.InsertPageAfter(existingPage, templatePage.Object);
			AssertPageCount(book, 4);
			Assert.AreEqual("page somekind", GetPageFromBookDom(book, 1).GetStringAttribute("class"));
		}

		private XmlNode GetPageFromBookDom(Book book, int pageNumber0Based)
		{
			var result = book.RawDom.StripXHtmlNameSpace();
			return result.SafeSelectNodes("//div[contains(@class, 'page')]", null)[pageNumber0Based];
		}

		private void AssertPageCount(Book book, int expectedCount)
		{
			var result = book.RawDom.StripXHtmlNameSpace();
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'page')]", expectedCount);
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
			var book = CreateBook(true);
			var original= book.GetPages().Count();
			var existingPage = book.GetPages().Last();
			book.DeletePage(existingPage);
			AssertPageCount(book,original-1);
		}

		[Test]
		public void DeletePage_AttemptDeleteLastRemaingPage_DoesntDelete()
		{
			var book = CreateBook(true);
			foreach (var page in book.GetPages())
			{
				book.DeletePage(page);
			}
			AssertPageCount(book, 1);
		}
		[Test]
		public void RelocatePage_FirstPageToSecond_DoesRelocate()
		{
			var book = CreateBook(true);
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
			var book = CreateBook(true);
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
			var book = CreateBook(true);
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
			var book = CreateBook(true);
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
			var book = CreateBook(true);
			Assert.IsTrue(book.CanDelete);
		}

		[Test]
		public void CanDelete_TemplateBook_False()
		{
			var book = CreateBook(false);
			Assert.IsFalse(book.CanDelete);
		}

		[Test]
		public void Delete_IsDeleted()
		{
			var book = CreateBook(false);
			Assert.IsTrue(false);
		}


		private Mock<IPage> CreateTemplatePage()
		{
			var templatePage = new Moq.Mock<IPage>();
			XmlDocument d = new XmlDocument();
			d.LoadXml("<wrapper><div class='page somekind'>hello</div></wrapper>");
			templatePage.Setup(x=>x.GetDivNodeForThisPage()).Returns(d.FirstChild);
			return templatePage;
		}

		private Book CreateBook(bool b)
		{
			return new Book(_storage.Object, true, _templateFinder.Object, _fileLocator.Object,
				_thumbnailer.Object, _pageSelection.Object, _pageListChangedEvent);
		}

		private XmlDocument GetThreePageDom()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html  xmlns='http://www.w3.org/1999/xhtml'><head></head><body>
				<div class='page' id='guid1'><input id='testInput' class='Blah' value='one' /><textarea id='vtitle' class='_vernacularBookTitle'>war</textarea></div>
				<div class='page' id='guid2'><textarea id='testText'>original1</textarea><input id='foo1' class='_copyMe' value='red'/><img id='img1' src='original.png'/></div>
				<div class='page' id='guid3'><textarea id='testText'>original2</textarea><input id='foo2' class='somethingInTheWay _copyMe' value='red'/><textarea id='copyOfVTitle' class='_vernacularBookTitle'>war</textarea></div>
				</body></html>");
			return dom;
		}


	}
}
