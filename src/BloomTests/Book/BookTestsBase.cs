using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Moq;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Code;
using SIL.TestUtilities;
using SIL.Windows.Forms.ImageToolbox;
using RobustIO = Bloom.RobustIO;

namespace BloomTests.Book
{
	public class BookTestsBase
	{
		protected Mock<IBookStorage> _storage;
		protected Mock<ITemplateFinder> _templateFinder;
		private Mock<IFileLocator> _fileLocator;
		protected Mock<HtmlThumbNailer> _thumbnailer;
		protected Mock<PageSelection> _pageSelection;
		protected PageListChangedEvent _pageListChangedEvent;
		protected TemporaryFolder _testFolder;
		private TemporaryFolder _tempFolder;
		protected CollectionSettings _collectionSettings;
		protected HtmlDom _bookDom;
		protected BookInfo _metadata;

		[SetUp]
		public virtual void Setup()
		{
			_storage = new Moq.Mock<IBookStorage>();
			_storage.Setup(x => x.GetLooksOk()).Returns(true);
			_bookDom = new HtmlDom(GetThreePageDom());
			_storage.SetupGet(x => x.Dom).Returns(() => _bookDom);
			_storage.SetupGet(x => x.Key).Returns("testkey");
			_storage.SetupGet(x => x.FileName).Returns("testTitle");
			_storage.Setup(x => x.GetRelocatableCopyOfDom(It.IsAny<IProgress>())).Returns(() =>
			{
				return
					_bookDom.Clone();
			});// review: the real thing does more than just clone
			_storage.Setup(x => x.MakeDomRelocatable(It.IsAny<HtmlDom>(), It.IsAny<IProgress>())).Returns(
				(HtmlDom x, IProgress y) => { return x.Clone(); });// review: the real thing does more than just clone

			_storage.Setup(x => x.GetFileLocator()).Returns(() => _fileLocator.Object);

			_testFolder = new TemporaryFolder("BookTests");
			_tempFolder = new TemporaryFolder(_testFolder, "book");
			MakeSamplePngImageWithMetadata(Path.Combine(_tempFolder.Path, "original.png"));
			_storage.SetupGet(x => x.FolderPath).Returns(_tempFolder.Path);// review: the real thing does more than just clone
			_metadata = new BookInfo(_tempFolder.Path, true);
			_storage.SetupGet(x => x.MetaData).Returns(_metadata);
			_storage.Setup(x => x.HandleRetiredXMatterPacks(It.IsAny<HtmlDom>(), It.IsAny<string>()))
				.Returns((HtmlDom dom, string y) => { return y == "BigBook" ? "Factory" : y; });

			_templateFinder = new Moq.Mock<ITemplateFinder>();
			_fileLocator = new Moq.Mock<IFileLocator>();
			string root = FileLocator.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot);
			string xMatter = BloomFileLocator.GetInstalledXMatterDirectory();
			_fileLocator.Setup(x => x.LocateFileWithThrow("languageDisplayTemplate.css")).Returns(root.CombineForPath("bookLayout", "languageDisplayTemplate.css"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("languageDisplay.css")).Returns(root.CombineForPath(_tempFolder.Path, "languageDisplay.css"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("previewMode.css")).Returns("../notareallocation/previewMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("origami.css")).Returns("../notareallocation/origami.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editMode.css")).Returns("../notareallocation/editMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editTranslationMode.css")).Returns("../notareallocation/editTranslationMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editOriginalMode.css")).Returns("../notareallocation/editOriginalMode.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("editPaneGlobal.css")).Returns("../notareallocation/editPaneGlobal.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("basePage.css")).Returns("../notareallocation/basePage.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("bloomBootstrap.js")).Returns("../notareallocation/bloomBootstrap.js");
			_fileLocator.Setup(x => x.LocateFileWithThrow("bloomPreviewBootstrap.js")).Returns("../notareallocation/bloomPreviewBootstrap.js");
			_fileLocator.Setup(x => x.LocateFileWithThrow("baseEPUB.css")).Returns("../notareallocation/baseEPUB.css");
			_fileLocator.Setup(x => x.LocateFileWithThrow("customBookStyles.css")).Returns(Path.Combine(_tempFolder.Path, "customBookStyles.css"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("settingsCollectionStyles.css")).Returns(Path.Combine(_testFolder.Path, "settingsCollectionStyles.css"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("customCollectionStyles.css")).Returns(Path.Combine(_testFolder.Path, "customCollectionStyles.css"));

			_fileLocator.Setup(x => x.LocateDirectory("Factory-XMatter")).Returns(xMatter.CombineForPath("Factory-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectoryWithThrow("Factory-XMatter")).Returns(xMatter.CombineForPath("Factory-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectory("Factory-XMatter", It.IsAny<string>())).Returns(xMatter.CombineForPath("Factory-XMatter"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("Factory-XMatter".CombineForPath("Factory-XMatter.htm"))).Returns(xMatter.CombineForPath("Factory-XMatter", "Factory-XMatter.htm"));

			_fileLocator.Setup(x => x.LocateDirectory("Traditional-XMatter")).Returns(xMatter.CombineForPath("Traditional-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectoryWithThrow("Traditional-XMatter")).Returns(xMatter.CombineForPath("Traditional-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectory("Traditional-XMatter", It.IsAny<string>())).Returns(xMatter.CombineForPath("Traditional-XMatter"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("Traditional-XMatter".CombineForPath("Traditional-XMatter.htm"))).Returns(xMatter.CombineForPath("Traditional-XMatter", "Factory-XMatter.htm"));

		
			_fileLocator.Setup(x => x.LocateDirectory("BigBook-XMatter")).Returns(xMatter.CombineForPath("BigBook-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectoryWithThrow("BigBook-XMatter")).Returns(xMatter.CombineForPath("BigBook-XMatter"));
			_fileLocator.Setup(x => x.LocateDirectory("BigBook-XMatter", It.IsAny<string>())).Returns(xMatter.CombineForPath("BigBook-XMatter"));
			_fileLocator.Setup(x => x.LocateFileWithThrow("BigBook-XMatter".CombineForPath("BigBook-XMatter.htm"))).Returns(xMatter.CombineForPath("BigBook-XMatter", "BigBook-XMatter.htm"));

			//warning: we're neutering part of what the code under test is trying to do here:
			_fileLocator.Setup(x => x.CloneAndCustomize(It.IsAny<IEnumerable<string>>())).Returns(_fileLocator.Object);

			_thumbnailer = new Moq.Mock<HtmlThumbNailer>(new object[] { new NavigationIsolator() });
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
			_thumbnailer.Object.Dispose();
		}

		protected Bloom.Book.Book CreateBook(CollectionSettings collectionSettings)
		{
			_collectionSettings = collectionSettings;
			return new Bloom.Book.Book(_metadata, _storage.Object, _templateFinder.Object,
				_collectionSettings,
				_pageSelection.Object, _pageListChangedEvent, new BookRefreshEvent());
		}

		protected virtual Bloom.Book.Book CreateBook()
		{
			return CreateBook(CreateDefaultCollectionsSettings());
		}

		protected CollectionSettings CreateDefaultCollectionsSettings()
		{
			return new CollectionSettings(new NewCollectionSettings() { PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_testFolder.Path, "test"), Language1Iso639Code = "xyz", Language2Iso639Code = "en", Language3Iso639Code = "fr" });
		}

		protected void MakeSamplePngImageWithMetadata(string path)
		{
			var x = new Bitmap(10, 10);
			SIL.IO.RobustIO.SaveImage(x, path, ImageFormat.Png);
			x.Dispose();
			using (var img = PalasoImage.FromFileRobustly(path))
			{
				img.Metadata.Creator = "joe";
				img.Metadata.CopyrightNotice = "Copyright 1999 by me";
				RetryUtility.Retry(() => img.SaveUpdatedMetadataIfItMakesSense());
			}
		}

		private XmlDocument GetThreePageDom()
		{
			var dom = new XmlDocument();
			dom.LoadXml(@"<html><head></head><body>
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

		protected void SetDom(string bodyContents, string headContents = "")
		{
			_bookDom = MakeDom(bodyContents, headContents);
		}

		public static HtmlDom MakeDom(string bodyContents, string headContents="")
		{
			return new HtmlDom(@"<html ><head>" + headContents + "</head><body>" + bodyContents + "</body></html>");
		}
	}
}