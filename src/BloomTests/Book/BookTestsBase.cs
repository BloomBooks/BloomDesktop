using System;
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
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.TestUtilities;
using SIL.Windows.Forms.ImageToolbox;

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
        protected TemporaryFolder _tempFolder;
        protected CollectionSettings _collectionSettings;
        protected HtmlDom _bookDom;
        protected BookInfo _metadata;
        protected BookData _bookData;

        protected Mock<IBookStorage> MakeMockStorage(string tempFolderPath, Func<HtmlDom> domGetter)
        {
            var storage = new Moq.Mock<IBookStorage>();
            storage.Setup(x => x.GetLooksOk()).Returns(true);

            storage.SetupGet(x => x.Dom).Returns(domGetter);
            storage.SetupGet(x => x.Key).Returns("testkey");
            storage
                .Setup(x => x.GetRelocatableCopyOfDom())
                .Returns(() => storage.Object.Dom.Clone()); // review: the real thing does more than just clone
            storage
                .Setup(x => x.MakeDomRelocatable(It.IsAny<HtmlDom>()))
                .Returns(
                    (HtmlDom x) =>
                    {
                        return x.Clone();
                    }
                ); // review: the real thing does more than just clone

            storage.Setup(x => x.GetFileLocator()).Returns(() => _fileLocator.Object);

            MakeSamplePngImageWithMetadata(Path.Combine(tempFolderPath, "original.png"));
            storage.SetupGet(x => x.FolderPath).Returns(tempFolderPath); // review: the real thing does more than just clone
            var metadata = new BookInfo(tempFolderPath, true);
            storage.SetupGet(x => x.BookInfo).Returns(metadata);
            storage
                .Setup(x => x.HandleRetiredXMatterPacks(It.IsAny<HtmlDom>(), It.IsAny<string>()))
                .Returns(
                    (HtmlDom dom, string y) =>
                    {
                        return y == "BigBook" ? "Factory" : y;
                    }
                );
            return storage;
        }

        protected virtual string GetTestFolderName() => "BookTests";

        [SetUp]
        public virtual void Setup()
        {
            _testFolder = new TemporaryFolder(GetTestFolderName());
            _tempFolder = new TemporaryFolder(_testFolder, "book");

            _bookDom = new HtmlDom(GetThreePageDom()); // a default, many tests replace this
            // Note that we're passing a function which returns the _bookDom member variable. By the time we return it,
            // it may have a different value than the _bookDom created on the previous line.
            // Thus, we are making a mock storage which, unlike a real one, doesn't itself
            // store a DOM set at creation; many tests update _bookDom to something else AFTER
            // this setup routine makes the Storage they use in their books.
            _storage = MakeMockStorage(_tempFolder.Path, () => _bookDom);
            _bookDom = _storage.Object.Dom;
            _metadata = _storage.Object.BookInfo;

            _templateFinder = new Moq.Mock<ITemplateFinder>();
            _fileLocator = new Moq.Mock<IFileLocator>();
            string root = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                BloomFileLocator.BrowserRoot
            );
            string xMatter = BloomFileLocator.GetFactoryXMatterDirectory();
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("previewMode.css"))
                .Returns("../notareallocation/previewMode.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("origami.css"))
                .Returns("../notareallocation/origami.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("origamiEditing.css"))
                .Returns("../notareallocation/origamiEditing.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("editMode.css"))
                .Returns("../notareallocation/editMode.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("editPaneGlobal.css"))
                .Returns("../notareallocation/editPaneGlobal.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("basePage.css"))
                .Returns("../notareallocation/basePage.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("bloomBootstrap.js"))
                .Returns("../notareallocation/bloomBootstrap.js");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("bloomPreviewBootstrap.js"))
                .Returns("../notareallocation/bloomPreviewBootstrap.js");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("baseEPUB.css"))
                .Returns("../notareallocation/baseEPUB.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("Device-XMatter.css"))
                .Returns("../notareallocation/Device-XMatter.css");
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("customBookStyles.css"))
                .Returns(Path.Combine(_tempFolder.Path, "customBookStyles.css"));
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("defaultLangStyles.css"))
                .Returns(Path.Combine(_testFolder.Path, "defaultLangStyles.css"));
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("customCollectionStyles.css"))
                .Returns(Path.Combine(_testFolder.Path, "customCollectionStyles.css"));
            var basicBookPath =
                BloomFileLocator.GetCodeBaseFolder()
                + "/../browser/templates/template books/Basic Book/Basic Book.css";
            _fileLocator.Setup(x => x.LocateFile("Basic Book.css")).Returns(basicBookPath);
            _fileLocator.Setup(x => x.LocateFileWithThrow("Basic Book.css")).Returns(basicBookPath);

            _fileLocator
                .Setup(x => x.LocateDirectory("Factory-XMatter"))
                .Returns(xMatter.CombineForPath("Factory-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectoryWithThrow("Factory-XMatter"))
                .Returns(xMatter.CombineForPath("Factory-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectory("Factory-XMatter", It.IsAny<string>()))
                .Returns(xMatter.CombineForPath("Factory-XMatter"));
            _fileLocator
                .Setup(
                    x =>
                        x.LocateFileWithThrow(
                            "Factory-XMatter".CombineForPath("Factory-XMatter.htm")
                        )
                )
                .Returns(xMatter.CombineForPath("Factory-XMatter", "Factory-XMatter.htm"));

            _fileLocator
                .Setup(x => x.LocateDirectory("Traditional-XMatter"))
                .Returns(xMatter.CombineForPath("Traditional-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectoryWithThrow("Traditional-XMatter"))
                .Returns(xMatter.CombineForPath("Traditional-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectory("Traditional-XMatter", It.IsAny<string>()))
                .Returns(xMatter.CombineForPath("Traditional-XMatter"));
            _fileLocator
                .Setup(
                    x =>
                        x.LocateFileWithThrow(
                            "Traditional-XMatter".CombineForPath("Traditional-XMatter.htm")
                        )
                )
                .Returns(xMatter.CombineForPath("Traditional-XMatter", "Factory-XMatter.htm"));
            _fileLocator
                .Setup(x => x.LocateFileWithThrow("Traditional-XMatter.css"))
                .Returns(xMatter.CombineForPath("Traditional-XMatter", "Traditional-XMatter.css"));

            _fileLocator
                .Setup(x => x.LocateDirectory("BigBook-XMatter"))
                .Returns(xMatter.CombineForPath("BigBook-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectoryWithThrow("BigBook-XMatter"))
                .Returns(xMatter.CombineForPath("BigBook-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectory("BigBook-XMatter", It.IsAny<string>()))
                .Returns(xMatter.CombineForPath("BigBook-XMatter"));
            _fileLocator
                .Setup(
                    x =>
                        x.LocateFileWithThrow(
                            "BigBook-XMatter".CombineForPath("BigBook-XMatter.htm")
                        )
                )
                .Returns(xMatter.CombineForPath("BigBook-XMatter", "BigBook-XMatter.htm"));

            _fileLocator
                .Setup(x => x.LocateDirectory("Device-XMatter"))
                .Returns(xMatter.CombineForPath("Device-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectoryWithThrow("Device-XMatter"))
                .Returns(xMatter.CombineForPath("Device-XMatter"));
            _fileLocator
                .Setup(x => x.LocateDirectory("Device-XMatter", It.IsAny<string>()))
                .Returns(xMatter.CombineForPath("Device-XMatter"));
            _fileLocator
                .Setup(
                    x =>
                        x.LocateFileWithThrow("Device-XMatter".CombineForPath("Device-XMatter.htm"))
                )
                .Returns(xMatter.CombineForPath("Device-XMatter", "Device-XMatter.htm"));

            //warning: we're neutering part of what the code under test is trying to do here:
            _fileLocator
                .Setup(x => x.CloneAndCustomize(It.IsAny<IEnumerable<string>>()))
                .Returns(_fileLocator.Object);

            _thumbnailer = new Moq.Mock<HtmlThumbNailer>();
            _pageSelection = new Mock<PageSelection>();
            _pageListChangedEvent = new PageListChangedEvent();
        }

        [TearDown]
        public virtual void TearDown()
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
            return new Bloom.Book.Book(
                _metadata,
                _storage.Object,
                _templateFinder.Object,
                _collectionSettings,
                _pageSelection.Object,
                _pageListChangedEvent,
                new BookRefreshEvent()
            );
        }

        protected Bloom.Book.Book CreateBookWithPhysicalFile(
            string bookHtml,
            CollectionSettings collectionSettings
        )
        {
            _collectionSettings = collectionSettings;
            var fileLocator = new BloomFileLocator(
                new CollectionSettings(),
                new XMatterPackFinder(new string[] { }),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );

            File.WriteAllText(Path.Combine(_tempFolder.Path, "book.htm"), bookHtml);

            var storage = new BookStorage(
                this._tempFolder.Path,
                fileLocator,
                new BookRenamedEvent(),
                _collectionSettings
            );

            var b = new Bloom.Book.Book(
                _metadata,
                storage,
                _templateFinder.Object,
                _collectionSettings,
                _pageSelection.Object,
                _pageListChangedEvent,
                new BookRefreshEvent()
            );
            // Some tests need this file early on so it can be copied to the book folder.
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(_tempFolder.Path), "customCollectionStyles.css"),
                @"/*This is wanted*/"
            );
            return b;
        }

        protected virtual Bloom.Book.Book CreateBookWithPhysicalFile(
            string bookHtml,
            bool bringBookUpToDate = false
        )
        {
            var book = CreateBookWithPhysicalFile(bookHtml, CreateDefaultCollectionsSettings());
            if (bringBookUpToDate)
                book.BringBookUpToDate(new NullProgress());
            return book;
        }

        protected virtual Bloom.Book.Book CreateBook(bool bringBookUpToDate = false)
        {
            var book = CreateBook(CreateDefaultCollectionsSettings());
            if (bringBookUpToDate)
                book.BringBookUpToDate(new NullProgress());
            else
            {
                // We have to at least fake this, or basePage.css won't find the default
                // variables it expects.
                new AppearanceSettings().WriteToFolder(book.FolderPath);
            }
            return book;
        }

        protected CollectionSettings CreateDefaultCollectionsSettings()
        {
            return new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        _testFolder.Path,
                        "test"
                    ),
                    Language1Tag = "xyz",
                    Language2Tag = "en",
                    Language3Tag = "fr"
                }
            );
        }

        protected BookData CreateDefaultBookData()
        {
            if (_collectionSettings == null)
                _collectionSettings = CreateDefaultCollectionsSettings();
            if (_bookDom == null)
                _bookDom = new HtmlDom(GetThreePageDom());
            return new BookData(_bookDom, _collectionSettings, null);
        }

        protected void MakeSamplePngImageWithMetadata(string path, int width = 10, int height = 10)
        {
            var x = new Bitmap(width, height);
            // Fill the bitmap with a solid color.  The default is transparent black if we don't do anything.
            using (var gfx = Graphics.FromImage(x))
            using (var brush = new SolidBrush(Color.FromArgb(200, 100, 0)))
                gfx.FillRectangle(brush, 0, 0, width, height);
            RobustImageIO.SaveImage(x, path, ImageFormat.Png);
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
            dom.LoadXml(ThreePageHtml);
            return dom;
        }

        protected const string ThreePageHtml =
            @"<html><head></head><body>
				<div class='bloom-page numberedPage' id='guid1'>
					<p>
						<textarea lang='en' id='1'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='2'  data-book='bookTitle'>dog</textarea>
					</p>
				</div>
				<div class='bloom-page numberedPage' id='guid2'>
					<p>
						<textarea lang='en' id='3'>english</textarea>
						<textarea lang='xyz' id='4'>originalVernacular</textarea>
						<textarea lang='tpi' id='5'>tokpsin</textarea>
					</p>
					<img id='img1' src='original.png'/>
				</div>
				<div class='bloom-page numberedPage' id='guid3'>
					<p>
						<textarea id='6' lang='xyz'>original2</textarea>
					</p>
					<p>
						<textarea lang='xyz' idc='copyOfVTitle'  data-book='bookTitle'>tree</textarea>
						<textarea lang='xyz' id='aa'  data-collection='testLibraryVariable'>aa</textarea>
					   <textarea lang='xyz' id='bb'  data-collection='testLibraryVariable'>bb</textarea>

					</p>
				</div>
				</body></html>";

        protected void SetDom(string bodyContents, string headContents = "")
        {
            _bookDom = MakeDom(bodyContents, headContents);
        }

        public static HtmlDom MakeDom(string bodyContents, string headContents = "")
        {
            return new HtmlDom(MakeBookHtml(bodyContents, headContents));
        }

        protected static string MakeBookHtml(string bodyContents, string headContents)
        {
            return @"<html ><head>"
                + headContents
                + "</head><body>"
                + bodyContents
                + "</body></html>";
        }

        public BookServer CreateBookServer()
        {
            _collectionSettings = CreateDefaultCollectionsSettings();
            var xmatterFinder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
            );
            var fileLocator = new BloomFileLocator(
                _collectionSettings,
                xmatterFinder,
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            var starter = new BookStarter(
                fileLocator,
                (dir, fullyUpdateBookFiles) =>
                    new BookStorage(dir, fileLocator, new BookRenamedEvent(), _collectionSettings),
                _collectionSettings
            );

            return new BookServer(
                //book factory
                (bookInfo, storage) =>
                {
                    return new Bloom.Book.Book(
                        bookInfo,
                        storage,
                        null,
                        _collectionSettings,
                        new PageSelection(),
                        new PageListChangedEvent(),
                        new BookRefreshEvent()
                    );
                },
                // storage factory
                (info, fullyUpdateBookFiles) =>
                {
                    var storage = new BookStorage(
                        info,
                        fileLocator,
                        new BookRenamedEvent(),
                        _collectionSettings
                    );
                    return storage;
                },
                // book starter factory
                () => starter,
                // configurator factory
                null
            );
        }
    }
}
