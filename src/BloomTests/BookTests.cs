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

		[SetUp]
		public void Setup()
		{
			_storage = new Moq.Mock<IBookStorage>();
			_storage.SetupGet(x => x.LooksOk).Returns(true);
			_storage.SetupGet(x => x.Dom).Returns(GetTwoPageDom());
			_storage.SetupGet(x => x.Key).Returns("testkey");
			_storage.SetupGet(x => x.Title).Returns("testTitle");
			_storage.SetupGet(x => x.BookType).Returns(Book.BookType.Publication);

			_templateFinder = new Moq.Mock<ITemplateFinder>();
			_fileLocator = new Moq.Mock<IFileLocator>();
			_fileLocator.Setup(x => x.LocateFile("previewMode.css")).Returns("../notareallocation/previewMode.css");

			_thumbnailer = new Moq.Mock<HtmlThumbNailer>(new object[] { 60 });
			_pageSelection = new Mock<PageSelection>();
			 DeletePageCommand _deletePageCommand=new DeletePageCommand();
			PageListChangedEvent _pageListChangedEvent = new PageListChangedEvent();
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
		public void GetPreviewHtmlFileForWholeBook_BookHasTwoPages_ResultHasBothPages()
		{
			var result = CreateBook().GetPreviewHtmlFileForWholeBook().StripXHtmlNameSpace();
			AssertThatXmlIn.Dom(result).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'page')]",2);
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
		public void InsertPageAfter_OnFirstPage_NewPageInsertedAsSecond()
		{
			var book = CreateBook();
			Page existingPage=book.GetPages().First();
			TestTemplateInsertion(book, existingPage, 1);
		}
		[Test]
		public void InsertPageAfter_OnLastPage_NewPageInsertedAtEnd()
		{
			var book = CreateBook();
			Page existingPage = book.GetPages().First();
			TestTemplateInsertion(book, existingPage, 1);
		}

		private void TestTemplateInsertion(Book book, Page existingPage, int expectedLocation)
		{
			Mock<IPage> templatePage = CreateTemplatePage();

			book.InsertPageAfter(existingPage, templatePage.Object);
			AssertPageCount(book, 3);
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
			var book = CreateBook();
			var original= book.GetPages().Count();
			Page existingPage = book.GetPages().Last();
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

		private Mock<IPage> CreateTemplatePage()
		{
			var templatePage = new Moq.Mock<IPage>();
			XmlDocument d = new XmlDocument();
			d.LoadXml("<wrapper><div class='page somekind'>hello</div></wrapper>");
			templatePage.Setup(x=>x.GetDivNode()).Returns(d.FirstChild);
			return templatePage;
		}

		private Book CreateBook()
		{
			return new Book(_storage.Object, _templateFinder.Object, _fileLocator.Object,
				_thumbnailer.Object, _pageSelection.Object, _pageListChangedEvent);
		}

		private XmlDocument GetTwoPageDom()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html  xmlns='http://www.w3.org/1999/xhtml'><head></head><body>
				<div id='1' class='page'>one</div>
				<div id='2' class='page'>two</div>
				</body></html>");
			return dom;
		}
	}
}
