using System;
using System.IO;
using System.Web;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using Newtonsoft.Json;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.Book
{
    [TestFixture]
    public class BookStarterTests
    {
        private BloomFileLocator _fileLocator;
        private BookStarter _starter;
        private TemporaryFolder _shellCollectionFolder;
        private TemporaryFolder _projectFolder;
        private CollectionSettings _defaultCollectionSettings;
        private CollectionSettings _alternateCollectionSettings;

        [SetUp]
        public void Setup()
        {
            // These settings are used in the default BookStarter object under test.
            _defaultCollectionSettings = new CollectionSettings
            {
                Language1Tag = "xyz",
                Language2Tag = "fr",
                Language3Tag = "es",
                XMatterPackName = "Factory"
            };
            ErrorReport.IsOkToInteractWithUser = false;
            _projectFolder = new TemporaryFolder("BookStarterTests_ProjectCollection");
            var collectionSettings = new CollectionSettings(
                Path.Combine(_projectFolder.Path, "test.bloomCollection")
            );

            var xmatterFinder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
            );

            _fileLocator = new BloomFileLocator(
                collectionSettings,
                xmatterFinder,
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );

            _starter = new BookStarter(
                _fileLocator,
                (dir) =>
                    new BookStorage(dir, _fileLocator, new BookRenamedEvent(), collectionSettings),
                _defaultCollectionSettings
            );
            _shellCollectionFolder = new TemporaryFolder("BookStarterTests_ShellCollection");

            // These settings are used in some tests, especially of static BookStarter methods.
            // The tests could probably be changed to use _librarySettings now, but it may be useful to retain
            // testing with slightly different settings in different tests.
            _alternateCollectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        new TemporaryFolder("BookDataTests").Path,
                        "test"
                    ),
                    Language1Tag = "xyz",
                    Language2Tag = "en",
                    Language3Tag = "fr"
                }
            );
        }

        [TearDown]
        public void TearDown()
        {
            _shellCollectionFolder.Dispose();
            _projectFolder.Dispose();
        }

        private string GetPathToHtml(string bookFolderPath)
        {
            return Path.Combine(bookFolderPath, Path.GetFileName(bookFolderPath)) + ".htm";
        }

        private string GetPathToMetaJson(string bookFolderPath)
        {
            return Path.Combine(bookFolderPath, "meta.json");
        }

        //
        //        [Test]
        //        public void CreateBookOnDiskFromTemplate_HasConfigurationPage_xxxxxxxxxxxx()
        //        {
        //            var source = FileLocationUtilities.GetDirectoryDistributedWithApplication("Sample Shells",
        //                                                                            "Calendar");
        //
        //            var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
        //        }

        [Test]
        public void CreateBookOnDiskFromTemplate_OriginalHasNoISBN_CopyDoesNotHaveISBN()
        {
            var source = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );

            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );

            AssertThatXmlIn
                .HtmlFile(path)
                .HasNoMatchForXpath("//div[@id='bloomDataDiv' and @data-book='ISBN']");
            AssertThatXmlIn
                .HtmlFile(path)
                .HasAtLeastOneMatchForXpath("//div[@data-book='ISBN' and not(text())]");
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_OriginalSpecifiesXMatter_CopyDoesNotSpecifyXMatter()
        {
            var extraHeadMaterial = @"<meta name='xmatter' content='TemplateStarter'/>";
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(
                    GetShellBookFolder("", extraHeadMaterial: extraHeadMaterial),
                    _projectFolder.Path
                )
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='xmatter']", 0);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_OriginalSpecifiesXMatterForChildren_CopyGetsThatXMatterName()
        {
            var extraHeadMaterial =
                @"<meta name='xmatter-for-children' content='TemplateStarter'/>";
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(
                    GetShellBookFolder("", extraHeadMaterial: extraHeadMaterial),
                    _projectFolder.Path
                )
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//meta[@name='xmatter' and @content='TemplateStarter']",
                    1
                );
            // but then this should not pass on to grandchildren
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='xmatter-for-children']", 0);
        }

        //regression
        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_CoverHasOneVisibleVernacularTitle()
        {
            var source = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );

            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );

            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'cover')]//*[@data-book='bookTitle' and @lang='xyz']",
                    1
                );
        }

        //regression
        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_DoesNotLoseVernacularTitle()
        {
            var source = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );

            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );

            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='bo']/p[text()='ཟླ་དཀར་དང་ཞྭ་མོ།།']",
                    1
                );
        }

        private BookServer CreateBookServer()
        {
            var collectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(
                        _projectFolder.Path,
                        "test"
                    ),
                    Language1Tag = "xyz"
                }
            );
            var server = new BookServer(
                (bookInfo, storage) =>
                {
                    return new Bloom.Book.Book(
                        bookInfo,
                        storage,
                        null,
                        collectionSettings,
                        new PageListChangedEvent(),
                        new BookRefreshEvent()
                    );
                },
                (path) => new BookStorage(path, _fileLocator, null, collectionSettings),
                () => _starter,
                null
            );
            return server;
        }

        //For Bloom 3.1, we decided to retire this feature. Now, new books are just called "book"
        /*[Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_InitialFolderNameIsCalledVaccinations()
        {
            var source = Path.Combine(BloomFileLocator.SampleShellsDirectory,"Vaccinations");

            var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
            Assert.AreEqual("Vaccinations", Path.GetFileName(path));

            //NB: although the clas under test here may produce a folder with the right name, the Book class may still mess it up based on variables
            //But that is a different set of unit tests.
        }*/

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryMoonAndCap_InitialFolderNameIsJustBook()
        {
            var source = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );

            var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
            Assert.AreEqual("Book", Path.GetFileName(path));
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasDataDivIntact()
        {
            AssertThatXmlIn
                .HtmlFile(GetNewMoonAndCapBookPath())
                .HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasTitlePage()
        {
            AssertThatXmlIn
                .HtmlFile(GetNewMoonAndCapBookPath())
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'titlePage')]", 1);
        }

        [
            Test,
            Ignore(
                "Current architecture gives responsibility for updating to Book, so can't be tested here."
            )
        ]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasCorrectImageOnCover()
        {
            AssertThatXmlIn
                .HtmlFile(GetNewMoonAndCapBookPath())
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'cover')]//img[@src='HL0014-1.png']",
                    1
                );
        }

        [
            Test,
            Ignore(
                "Current architecture spreads this responsibility for updating to Book, so can't be tested here."
            )
        ]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasCorrectTopicOnCover()
        {
            AssertThatXmlIn
                .HtmlFile(GetNewMoonAndCapBookPath())
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'cover')]//*[@data-derived='topic' and text()='Health']",
                    1
                );
        }

        private string GetNewMoonAndCapBookPath()
        {
            var source = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );

            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );
            return path;
        }

        // Strip all license info from books made from templates. (Code that runs later will fill in default.)
        [Test]
        public void CreateBookOnDiskFromTemplate_OriginalCC0_LicenseRemoved()
        {
            var originalSource = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");
            using (var tempFolder = new TemporaryFolder("BasicBookCc0"))
            using (var destFolder = new TemporaryFolder("OriginalCC0_BookIsBy"))
            {
                var source = Path.Combine(tempFolder.Path, "Basic Book");
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
                DirectoryUtilities.CopyDirectory(originalSource, tempFolder.Path);
                var htmPath = Path.Combine(source, "Basic Book.html");
                var content = RobustFile.ReadAllText(htmPath);
                // insert cc0 stuff in data div
                var replacement = @"<div id='bloomDataDiv'><div data-book='licenseUrl' lang='*'>
            http://creativecommons.org/publicdomain/zero/1.0/
        </div>
		<div data-book='licenseUrl' lang='en'>
            http://creativecommons.org/publicdomain/zero/1.0/
        </div>
		<div data-book='licenseUrl'>
            http://creativecommons.org/publicdomain/zero/1.0/
        </div>
        <div data-book='licenseDescription' lang='en'>
            You can copy, modify, and distribute this work, even for commercial purposes, all without asking permission.
        </div>
		<div data-book='licenseNotes'>This should be removed too</div>".Replace("'", "\"");
                var patched = content.Replace("<div id=\"bloomDataDiv\">", replacement);
                RobustFile.WriteAllText(htmPath, patched);
                var bookPath = GetPathToHtml(
                    _starter.CreateBookOnDiskFromTemplate(source, destFolder.Path)
                );
                var assertThatBook = AssertThatXmlIn.HtmlFile(bookPath);
                assertThatBook.HasNoMatchForXpath("//div[@data-book='licenseUrl']");
                assertThatBook.HasNoMatchForXpath("//div[@data-book='licenseDescription']");
                assertThatBook.HasNoMatchForXpath("//div[@data-book='licenseNotes']");
            }
        }

        // We shouldn't mess with license if the original is a shell.
        [Test]
        public void CreateBookOnDiskFromShell_OriginalCC0_BookIsCC0()
        {
            var originalSource = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );
            using (var tempFolder = new TemporaryFolder("MoonCapCc0"))
            using (var destFolder = new TemporaryFolder("MoonCap_BookIsCC0"))
            {
                var source = Path.Combine(tempFolder.Path, "The Moon and the Cap");
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
                DirectoryUtilities.CopyDirectory(originalSource, tempFolder.Path);
                var htmPath = Path.Combine(source, "The Moon and the Cap.htm");
                var content = RobustFile.ReadAllText(htmPath);
                // insert cc0 stuff in data div
                var patched = content.Replace(
                    "http://creativecommons.org/licenses/by/4.0/",
                    "http://creativecommons.org/publicdomain/zero/1.0/"
                );
                RobustFile.WriteAllText(htmPath, patched);
                var bookPath = GetPathToHtml(
                    _starter.CreateBookOnDiskFromTemplate(source, destFolder.Path)
                );
                var assertThatBook = AssertThatXmlIn.HtmlFile(bookPath);
                // For some reason Vaccinations specifies licenseUrl in three ways (no lang, lang="en", lang="*").
                // We don't want any of them messed with.
                assertThatBook.HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@data-book='licenseUrl' and contains(text(), '/zero/1.0')]",
                    1
                );
            }
        }

        [Test]
        public void CreateBookOnDiskFromShell_PublisherBecomesOriginalPublisherInDerivative()
        {
            var originalSource = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );
            using (var tempFolder = new TemporaryFolder("MoonCapWithPub"))
            using (var destFolder = new TemporaryFolder("MoonCap_Derivative"))
            {
                var source = Path.Combine(tempFolder.Path, "The Moon and the Cap");
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
                DirectoryUtilities.CopyDirectory(originalSource, tempFolder.Path);
                var metaDataPath = Path.Combine(source, "meta.json");
                var content = RobustFile.ReadAllText(metaDataPath);
                // Publisher (original) is Pratham Books
                const string publisher = "Pratham Books";
                var existingMetadata = BookMetaData.FromString(content);
                Assert.AreEqual(
                    publisher,
                    existingMetadata.Publisher,
                    "Moon & Cap Publisher should be 'Pratham Books'"
                );
                var derivativeMetaJsonPath = GetPathToMetaJson(
                    _starter.CreateBookOnDiskFromTemplate(source, destFolder.Path)
                );
                var derivedMetaData = BookMetaData.FromString(
                    RobustFile.ReadAllText(derivativeMetaJsonPath)
                );
                Assert.AreEqual(
                    publisher,
                    derivedMetaData.OriginalPublisher,
                    "Derivation should move Publisher to OriginalPublisher"
                );
                Assert.AreEqual(
                    string.Empty,
                    derivedMetaData.Publisher,
                    "Derivation should leave Publisher empty"
                );
            }
        }

        [Test]
        public void CreateBookOnDiskFromShell_PublisherDoesNotOverwriteOriginalPublisherInDerivative()
        {
            var originalSource = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );
            using (var tempFolder = new TemporaryFolder("MoonCapWithOrigPub"))
            using (var destFolder = new TemporaryFolder("MoonCap_Derivative"))
            {
                var source = Path.Combine(tempFolder.Path, "The Moon and the Cap");
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
                DirectoryUtilities.CopyDirectory(originalSource, tempFolder.Path);
                var metaDataPath = Path.Combine(source, "meta.json");
                var content = RobustFile.ReadAllText(metaDataPath);
                // OriginalPublisher is Pratham Books
                const string origPublisher = "Pratham Books";
                const string publisher = "Me, myself, and I";
                var existingMetadata = BookMetaData.FromString(content);
                existingMetadata.OriginalPublisher = origPublisher;
                existingMetadata.Publisher = publisher;
                Assert.AreEqual(
                    origPublisher,
                    existingMetadata.OriginalPublisher,
                    "Initial Original Publisher is 'Pratham Books'"
                );
                Assert.AreEqual(
                    publisher,
                    existingMetadata.Publisher,
                    "Initial Publisher is 'Me, myself, and I'"
                );
                var derivativeMetaJsonPath = GetPathToMetaJson(
                    _starter.CreateBookOnDiskFromTemplate(source, destFolder.Path)
                );
                var derivedMetaData = BookMetaData.FromString(
                    RobustFile.ReadAllText(derivativeMetaJsonPath)
                );
                Assert.AreEqual(
                    origPublisher,
                    derivedMetaData.OriginalPublisher,
                    "Derivation should not overwrite OriginalPublisher"
                );
                Assert.AreEqual(
                    string.Empty,
                    derivedMetaData.Publisher,
                    "Derivation should leave Publisher empty"
                );
            }
        }

        [Test]
        public void CreateBookOnDiskFromShell_MissingPublisher_DoesNotCauseProblemInDerivative()
        {
            var originalSource = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );
            using (var tempFolder = new TemporaryFolder("MoonCapWithNoPub"))
            using (var destFolder = new TemporaryFolder("MoonCap_Derivative"))
            {
                var source = Path.Combine(tempFolder.Path, "The Moon and the Cap");
                if (Directory.Exists(source))
                    Directory.Delete(source, true);
                DirectoryUtilities.CopyDirectory(originalSource, tempFolder.Path);
                var metaDataPath = Path.Combine(source, "meta.json");
                var content = RobustFile.ReadAllText(metaDataPath);
                // Remove publisher
                var metadataObject = BookMetaData.FromString(content);
                metadataObject.Publisher = null;
                var patched = JsonConvert.SerializeObject(metadataObject);
                RobustFile.WriteAllText(metaDataPath, patched);
                var derivativeMetaJsonPath = GetPathToMetaJson(
                    _starter.CreateBookOnDiskFromTemplate(source, destFolder.Path)
                );
                var derivedMetaData = BookMetaData.FromString(
                    RobustFile.ReadAllText(derivativeMetaJsonPath)
                );
                Assert.AreEqual(
                    string.Empty,
                    derivedMetaData.OriginalPublisher,
                    "Derivation of book with no Publisher should leave this empty"
                );
                Assert.AreEqual(
                    string.Empty,
                    derivedMetaData.Publisher,
                    "Derivation of book with no Publisher should leave this empty"
                );
            }
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryA5_Validates()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
        }

        [Test]
        public void CreateBookOnDiskFromTemplateStarter_IsTemplate_ButNotTemplateFactory()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Template Starter");

            var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
            var newMetaData = BookMetaData.FromFolder(path);
            Assert.That(newMetaData.IsSuitableForMakingShells, Is.True);
            Assert.That(newMetaData.IsSuitableForMakingTemplates, Is.False);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_OriginalIsTemplate_CopyIsNotTemplate()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");
            var originalMetaData = BookMetaData.FromFolder(source);
            Assert.That(originalMetaData.IsSuitableForMakingShells);
            var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
            var newMetaData = BookMetaData.FromFolder(path);
            Assert.That(newMetaData.IsSuitableForMakingShells, Is.False);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_OriginalIsTemplate_CopyHasNoTitle()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");
            var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

            var server = CreateBookServer();
            var book = server.GetBookFromBookInfo(new BookInfo(path, true));
            Assert.AreEqual("Title Missing", book.TitleBestForUserDisplay);
            Assert.That(book.GetDataItem("Title").Empty);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryA5_CreatesWithCoverAndTitle()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );

            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover ')]", 4);
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'titlePage')]", 1);
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-page')]", 5);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryA5AndXMatter_CoverTitleIsIntiallyEmpty()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );

            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'cover ')]//div[@lang='xyz' and contains(@data-book, 'bookTitle') and not(text())]",
                    1
                );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryBasicBook_CreatesWithCorrectStylesheets()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'Basic Book')]", 1);
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'basePage')]", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_PagesLabeledExtraAreNotAdded()
        {
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(
                    GetShellBookFolder(makeTemplate: true),
                    _projectFolder.Path
                )
            );
            AssertThatXmlIn.HtmlFile(path).HasNoMatchForXpath("//div[contains(text(), '_extra_')]");
        }

        [Test]
        public void CreateBookOnDiskFromShell_PagesLabeledExtraAreAdded()
        {
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(
                    GetShellBookFolder(makeTemplate: false),
                    _projectFolder.Path
                )
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasAtLeastOneMatchForXpath("//div[contains(text(), '_extra_')]");
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_HasEnglishTextArea_VernacularTextAreaAdded()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
						 <div lang='en'>This is some English</div>
						</div>
					</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            //nb: testid is used rather than id because id is replaced with a guid when the copy is made

            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class,'bloom-translationGroup')]/div[@lang='en']",
                    1
                );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class,'bloom-translationGroup')]/div[@lang='xyz']",
                    1
                );
            //the new text should also have been emptied of English
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div/div[contains(@class,'bloom-translationGroup')]/div[@lang='xyz' and not(text())]",
                    1
                );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_ConflictingCss_UsesLegacy()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
						 <div lang='en'>This is some English</div>
						</div>
					</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "customBookStyles.css"),
                @".marginBox {margin: 20px}"
            );
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            //nb: testid is used rather than id because id is replaced with a guid when the copy is made

            var bookFolder = Path.GetDirectoryName(path);
            var appearanceSettings = new AppearanceSettings();
            appearanceSettings.UpdateFromFolder(bookFolder);
            Assert.That(appearanceSettings.CssThemeName == "legacy-5-6");

            // Would like to test this, but we don't update the stylesheets in the DOM until we actually create the book.
            //AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[@href='basePage.css']", 1);
            //AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[@href='appearance.css']", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_UnwantedFiles_AreNotCopied()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
						 <div lang='en'>This is some English</div>
						</div>
					</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "book.userPrefs"),
                @"some nonsense"
            );
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "book.userPrefs.bak"),
                @"some nonsense"
            );
            File.WriteAllText(Path.Combine(sourceTemplateFolder, "ReadMe-en.md"), @"some nonsense");
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "ReadMe-en.htm"),
                @"some nonsense"
            );
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "ReadMe-zh-CN.htm"),
                @"some nonsense"
            );
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "ReadMe-tpi.htm"),
                @"some nonsense"
            );
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "something.jade"),
                @"some nonsense"
            );
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "something.less"),
                @"some nonsense"
            );
            File.WriteAllText(Path.Combine(sourceTemplateFolder, "readme.txt"), @"some nonsense");
            File.WriteAllText(Path.Combine(sourceTemplateFolder, "history.db"), @"some nonsense");
            File.WriteAllText(
                Path.Combine(sourceTemplateFolder, "template name.css.map"),
                @"some nonsense"
            );
            var bookPath = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            var folderPath = Path.GetDirectoryName(bookPath);
            Assert.That(File.Exists(Path.Combine(folderPath, "book.userPrefs")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "book.userPrefs.bak")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "ReadMe-en.md")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "ReadMe-en.htm")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "ReadMe-zh-CN.htm")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "ReadMe-tpi.htm")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "something.jade")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "something.less")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "template name.css.map")), Is.False);
            Assert.That(File.Exists(Path.Combine(folderPath, "history.db")), Is.False);
            // And just to be sure, we're not skipping EVERYTHING!
            Assert.That(File.Exists(Path.Combine(folderPath, "readme.txt")), Is.True);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_HasReadmeImagesFolder_NotCopied()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
						 <div lang='en'>This is some English</div>
						</div>
					</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            Directory.CreateDirectory(
                Path.Combine(sourceTemplateFolder, Bloom.Book.Book.ReadMeImagesFolderName)
            );
            var bookPath = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            var folderPath = Path.Combine(
                Path.GetDirectoryName(bookPath),
                Bloom.Book.Book.ReadMeImagesFolderName
            );
            Assert.IsFalse(Directory.Exists(folderPath));
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_HasTokPisinTextAreaSurroundedByParagraph_VernacularTextAreaAdded()
        {
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='tpi']",
                    1
                );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='xyz']",
                    1
                );
        }

        //        [Test]
        //        public void CreateBookOnDiskFromTemplate_HasTokPisinTextArea_StyleAddedToHide()
        //        {
        //            var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
        //            AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='tpi' and contains(@class,'hideMe')]", 1);
        //        }


        [Test]
        public void CreateBookOnDiskFromTemplate_NoTranslationGroupClass_LeavesUntouched()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page'>
						<p>
							<textarea lang='en'>LanguageName</textarea>
						</p>
					</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//textarea", 1);
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en']", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_AlreadyHasVernacular_LeavesUntouched()
        {
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='en']",
                    1
                );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz']",
                    1
                );
            // this, the original version started failing when the xml started putting the closing tag on the next line, so I changed it to 'starts-with'
            //AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz' and text()='original']", 1);
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz' and starts-with(text(),'original')]",
                    1
                );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_Has2SourceLanguagesTextArea_OneVernacularTextAreaAdded()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page '>
							<p class='bloom-translationGroup'>
								<textarea lang='en'> When you plant a garden you always make a fence.</textarea>
								<textarea lang='tpi'> Taim yu planim gaden yu save wokim banis.</textarea>
							</p>
						</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_HasBookTitleWithEnglish_HasItWithVernacular()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page'>
							<p class='bloom-translationGroup'>
								<textarea data-book='bookTitle' class='vernacularBookTitle' lang='en'>Book Name</textarea>
							 </p>
						</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
        }

        /// <summary>
        /// we want to remove any front-matter left in the shell book
        /// </summary>
        [Test]
        public void CreateBookOnDiskFromTemplate_SourceHasXMatter_Removed()
        {
            _starter.TestingSoSkipAddingXMatter = true;
            var body =
                @"<div class='bloom-page bloom-frontMatter'>don't keep me</div>
						<div class='bloom-page'>keep me</div>
						<div class='bloom-page bloom-backMatter'>don't keep me</div>";
            string sourceTemplateFolder = GetShellBookFolder(body, null);
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-frontMatter')]");
            AssertThatXmlIn
                .HtmlFile(path)
                .HasNoMatchForXpath("//div[contains(@class,'bloom-backMatter')]");
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_TextAreaHasNoText_VernacularLangAttrSet()
        {
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@testid='pageWithNoLanguageTags']/p/textarea",
                    3
                );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@testid='pageWithNoLanguageTags']/p/textarea[@lang='xyz']",
                    1
                );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_PagesNotLabeledExtraAreAdded()
        {
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(text(), '_normal_')]", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_PagesLabeledRequiredIsAdded()
        {
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path)
            );
            AssertThatXmlIn
                .HtmlFile(path)
                .HasSpecifiedNumberOfMatchesForXpath("//div[contains(text(), '_required_')]", 1);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_ShellHasNoNameDirective_FileNameJust_Book_()
        {
            string folderPath = _starter.CreateBookOnDiskFromTemplate(
                GetShellBookFolder(),
                _projectFolder.Path
            );
            Assert.AreEqual("Book", Path.GetFileName(folderPath));
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_BookWithDefaultNameAlreadyExists_NameGetsNumberSuffix()
        {
            string firstPath = _starter.CreateBookOnDiskFromTemplate(
                GetShellBookFolder(),
                _projectFolder.Path
            );
            string secondPath = _starter.CreateBookOnDiskFromTemplate(
                GetShellBookFolder(),
                _projectFolder.Path
            );

            Assert.IsTrue(File.Exists(firstPath.CombineForPath("Book.htm")));
            Assert.IsTrue(File.Exists(secondPath.CombineForPath("Book 2.htm")));
            Assert.IsTrue(Directory.Exists(secondPath), "it clobbered the first one!");
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromBasicBook_GetsExpectedFileName()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(
                source,
                _projectFolder.Path
            );
            var path = GetPathToHtml(bookFolderPath);

            Assert.AreEqual("Book.htm", Path.GetFileName(path));
            Assert.IsTrue(Directory.Exists(bookFolderPath));
            Assert.IsTrue(File.Exists(path));
        }

        //		[Test]
        //		public void CreateBookOnDiskFromTemplate_FromBasicBook_GetsExpectedEnglishTitleInDataDivAndJson()
        //		{
        //			var source = BloomFileLocator.GetBookTemplateDirectory("Basic Book");
        //
        //			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
        //			var path = GetPathToHtml(bookFolderPath);
        //
        //			//see  <meta name="defaultNameForDerivedBooks" content="My Book" />
        //			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='en' and text()='My Book']",1);
        //
        //			var metadata = new BookInfo(bookFolderPath, false);
        //			Assert.That(metadata.Title, Is.EqualTo("My Book"));
        //		}

        [Test]
        public void CreateBookOnDiskFromTemplate_FromBasicBook_GetsNoDefaultNameMetaElement()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(
                source,
                _projectFolder.Path
            );

            //see  <meta name="defaultNameForDerivedBooks" content="My Book" />
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(bookFolderPath))
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//meta[@name='defaultNameForDerivedBooks']",
                    0
                );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromBasicBook_CreatesCorrectMetaJson()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(
                source,
                _projectFolder.Path
            );

            var jsonFile = Path.Combine(bookFolderPath, BookInfo.MetaDataFileName);
            Assert.That(
                File.Exists(jsonFile),
                "Creating a book should create a metadata json file meta.json"
            );

            var metadata = new BookInfo(bookFolderPath, false);
            Assert.That(metadata.Id, Is.Not.Null, "New book should get an ID");
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromBasicBook_AllowUploadIsTrue()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");
            string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(
                source,
                _projectFolder.Path
            );
            var metadata = new BookInfo(bookFolderPath, false);
            Assert.IsTrue(metadata.AllowUploading);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromBasicBook_BookletMakingIsAppropriateIsTrue()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");
            string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(
                source,
                _projectFolder.Path
            );
            var metadata = new BookInfo(bookFolderPath, false);
            Assert.IsTrue(metadata.BookletMakingIsAppropriate);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromBasicBook_BookLineageSetToIdOfSourceBook()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(
                source,
                _projectFolder.Path
            );

            var jsonFile = Path.Combine(bookFolderPath, BookInfo.MetaDataFileName);
            Assert.That(
                File.Exists(jsonFile),
                "Creating a book should create a metadata json file meta.json"
            );
            var metadata = new BookInfo(bookFolderPath, false);

            var kIdOfBasicBook = "056B6F11-4A6C-4942-B2BC-8861E62B03B3";
            Assert.That(metadata.BookLineage, Is.EqualTo(kIdOfBasicBook));
            //we should get our own id, not reuse our parent's
            Assert.That(
                metadata.Id,
                Is.Not.EqualTo(kIdOfBasicBook),
                "New book should get its own ID, not reuse parent's"
            );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook()
        {
            // Try it when we have json and no lineage metadata in the html.
            CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook(
                null,
                true
            );
            // Should be able to get it from the old metadata if there is no json
            CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook(
                "bloomBookLineage",
                false
            );
            // And from the even older metadata.
            CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook(
                "bookLineage",
                false
            );
        }

        public void CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook(
            string lineage,
            bool includeJson
        )
        {
            var shellFolderPath = GetShellBookFolder(@" ", null, lineage, includeJson);
            string folderPath = _starter.CreateBookOnDiskFromTemplate(
                shellFolderPath,
                _projectFolder.Path
            );

            var metadata = new BookInfo(folderPath, false);
            Assert.That(metadata.BookLineage, Is.EqualTo("first,second,thisNewGuy"));
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_ShellHasNoDefaultNameDirective_bookTitleAttibutesSameAsShell()
        {
            var shellFolderPath = GetShellBookFolder(
                @" <div id='bloomDataDiv'>
					  <div data-book='bookTitle' lang='en'>Vaccinations</div>
					  <div data-book='bookTitle' lang='tpi'>Tambu Sut</div>
					</div>",
                null
            );
            string folderPath = _starter.CreateBookOnDiskFromTemplate(
                shellFolderPath,
                _projectFolder.Path
            );
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(folderPath))
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='en' and text()='Vaccinations']",
                    1
                );
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(folderPath))
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='tpi' and text()='Tambu Sut']",
                    1
                );
        }

        [TestCase("Vaccinations")]
        [TestCase("Vaccinations & Immunizations")] // Test characters which have special meaning in HTML (the "&")
        [TestCase(
            "<p><span id=\"s1\" class=\"audio-sentence\">Vaccinations & Immunizations.</span> <span id=\"s2\" class=\"audio-sentence\">Prevention > Treatment</span>",
            "Vaccinations & Immunizations. Prevention > Treatment"
        )] // Talking Book
        public void CreateBookOnDiskFromTemplate_ShellHasNoOriginalTitle_CopyFromEnglish(
            string englishTitle,
            string expectedOriginalTitle = null
        )
        {
            var shellFolderPath = GetShellBookFolder(
                $@" <div id='bloomDataDiv'>
					  <div data-book='bookTitle' lang='tpi'>Tambu Sut</div>
					  <div data-book='bookTitle' lang='en'>{englishTitle}</div>
					</div>",
                null
            );
            string folderPath = _starter.CreateBookOnDiskFromTemplate(
                shellFolderPath,
                _projectFolder.Path
            );
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(folderPath))
                .HasSpecifiedNumberOfMatchesForXpath(
                    $"//div[@id='bloomDataDiv']/div[@data-book='originalTitle' and @lang='*' and text()='{expectedOriginalTitle ?? englishTitle}']",
                    1
                );
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(folderPath))
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']/div[@data-book='originalTitle']",
                    1
                );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_ShellHasNoOriginalTitleOrEnglishTitle_LeavesOriginalTitleMissing()
        {
            var shellFolderPath = GetShellBookFolder(
                @" <div id='bloomDataDiv'>
					  <div data-book='bookTitle' lang='es'>Vaccinations</div>
					  <div data-book='bookTitle' lang='tpi'>Tambu Sut</div>
					</div>",
                null
            );
            string folderPath = _starter.CreateBookOnDiskFromTemplate(
                shellFolderPath,
                _projectFolder.Path
            );
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(folderPath))
                .HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='originalTitle']");
            var info = new BookInfo(folderPath, true);
            Assert.That(info.OriginalTitle, Is.Null);
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_OriginalIsTemplate_NoOriginalTitle()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");
            var folderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(folderPath))
                .HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='originalTitle']");
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_ShellHasBookshelf_DerivativeDoesNot()
        {
            var shellFolderPath = GetShellBookFolder(
                @" ",
                includeJson: true,
                bookshelf: "mybookshelf/subdirectory"
            );
            string folderPath = _starter.CreateBookOnDiskFromTemplate(
                shellFolderPath,
                _projectFolder.Path
            );
            var oldJsonString = RobustFile.ReadAllText(GetPathToMetaJson(shellFolderPath));
            var newJsonString = RobustFile.ReadAllText(GetPathToMetaJson(folderPath));
            Assert.That(oldJsonString.Contains("bookshelf"));
            Assert.That(!newJsonString.Contains("bookshelf"));
            Assert.That(newJsonString.Contains("topic:Fiction"));
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryTemplate_SameNameAlreadyUsed_FindsUsableNumberSuffix()
        {
            Directory.CreateDirectory(_projectFolder.Combine("Book"));
            Directory.CreateDirectory(_projectFolder.Combine("Book 2"));
            Directory.CreateDirectory(_projectFolder.Combine("Book 4"));

            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");

            var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

            Assert.AreEqual("Book 3", Path.GetFileName(path));
            Assert.IsTrue(Directory.Exists(path));
            Assert.IsTrue(File.Exists(Path.Combine(path, "Book 3.htm")));
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_CreationFailsForSomeReason_DoesNotLeaveIncompleteFolderAround()
        {
            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book");
            var goodPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
            SIL.IO.RobustIO.DeleteDirectoryAndContents(goodPath); //remove that good one. We just did it to get an idea of what the path is

            //now fail while making a book

            _starter.OnNextRunSimulateFailureMakingBook = true;
            Assert.Throws<ApplicationException>(
                () => _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path)
            );

            Assert.IsFalse(
                Directory.Exists(goodPath),
                "Should not have left the folder there, after a failed book creation"
            );
        }

        // The work of moving the copyright and license to a spot reserved for an original
        // is done by BookCopyrightAndLicense, and is thoroughly tested on that class. Here,
        // we just want to have one test that operates on an actual shell that we ship.
        // What we're testing: when we use a translation, it may have its own copyright. However that doesn't mean that we ever replace
        // the "original" copyright and license. Those stick with the book through all adaptations.
        [Test]
        public void CreateBookOnDiskFromTemplate_SourceIsAlsoAnAdaptation_OriginalCopyrightAndLicensePreserved()
        {
            var firstAdaptation = GetFolderPathToCreatedBook("The Moon and the Cap");
            var secondAdaptation = _starter.CreateBookOnDiskFromTemplate(
                firstAdaptation,
                _projectFolder.Path
            );
            // Note: an earlier version of this test validated things about originalCopyrightAndLicense. But that is now derived
            // information, never in the data-div and only inserted into the book when we bring it up to date or a couple of other
            // post-book-starter operations. So all we can do at this point is verify what the book starter is supposed to leave
            // in the data-div.
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(secondAdaptation))
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']"
                        + "//div[@data-book='originalCopyright' and @lang='*' and "
                        + "contains(text(), 'Copyright © 2007, Pratham Books"
                        + "')]",
                    1
                );
            AssertThatXmlIn
                .HtmlFile(GetPathToHtml(secondAdaptation))
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='bloomDataDiv']"
                        + "//div[@data-book='originalLicenseUrl' and @lang='*' and "
                        + "contains(text(), 'http://creativecommons.org/licenses/by/4.0/')]",
                    1
                );
        }

        private string GetFolderPathToCreatedBook(string sourceBookName)
        {
            var source = Path.Combine(BloomFileLocator.SampleShellsDirectory, sourceBookName);
            return _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
        }

        private string GetShellBookFolder(bool makeTemplate = false)
        {
            return GetShellBookFolder(
                @"<div class='bloom-page' data-page='required' id='1'>
						_required_ The user will not be allowed to remove this page.
					  </div>
					<div class='bloom-page' id='2'>
						_normal_ It would be ok for the user to remove this page.
					</div>

					<div class='bloom-page'  data-page='extra' id='3'>
						_extra_
					</div>
					<div class='bloom-page' testid='pageWithNoLanguageTags'>
						<p class='bloom-translationGroup'>
							<textarea>Text of a simple template</textarea>
						</p>
					</div>
					<div class='bloom-page' testid='pageAlreadyHasVernacular'>
						 <p class='bloom-translationGroup'>
							<textarea lang='en'>This is some English</textarea>
							<textarea lang='xyz'>original</textarea>
						</p>
					</div>
					<div class='bloom-page' testid='pageWithJustTokPisin'>
						 <p class='bloom-translationGroup'>
							<textarea lang='tpi'> Taim yu planim gaden yu save wokim banis.</textarea>
						</p>
					</div>",
                null, //no defaultNameForDerivedBooks
                makeTemplate: makeTemplate
            );
        }

        private string GetShellBookFolder(
            string bodyContents,
            string defaultNameForDerivedBooks = null,
            string lineageName = null,
            bool includeJson = true,
            string extraHeadMaterial = "",
            bool makeTemplate = false,
            string bookshelf = ""
        )
        {
            var lineageMeta = "";
            if (lineageName != null)
                lineageMeta = @"<meta name='" + lineageName + "' content='first,second' />";
            var bookIdMeta = "";
            if (!includeJson)
                bookIdMeta = @"<meta name='bloomBookId' content='thisNewGuy' />";

            var content = String.Format(
                @"<?xml version='1.0' encoding='utf-8' ?>
				<!DOCTYPE html>
				<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					{0}
					{1}
					{2}
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />",
                lineageMeta,
                bookIdMeta,
                extraHeadMaterial
            );
            if (!String.IsNullOrEmpty(defaultNameForDerivedBooks))
            {
                content +=
                    @"<meta name='defaultNameForDerivedBooks' content='"
                    + defaultNameForDerivedBooks
                    + "'/>";
            }
            content +=
                @"</head>
				<body class='a5Portrait'>"
                + bodyContents
                + "</body></html>";

            string folder = _shellCollectionFolder.Combine("guitar");
            Directory.CreateDirectory(folder);
            string shellFolderPath = Path.Combine(folder, "guitar.htm");
            File.WriteAllText(shellFolderPath, content);
            if (includeJson)
            {
                var json =
                    "{\"bookLineage\":\"first,second\",\"bookInstanceId\":\"thisNewGuy\",\"title\":\"Test Shell\", \"suitableForMakingShells\":"
                    + (makeTemplate ? "true" : "false")
                    + (
                        string.IsNullOrEmpty(bookshelf)
                            ? ""
                            : ",\"tags\":[\"topic:Fiction\",\"bookshelf:" + bookshelf + "\"]"
                    )
                    + "}";
                File.WriteAllText(Path.Combine(folder, BookInfo.MetaDataFileName), json);
            }
            else
            {
                File.Delete(Path.Combine(folder, BookInfo.MetaDataFileName)); // in case an earlier run created it
            }
            return folder;
        }

        //		[Test]
        //		public void CopyToFolder_HasSubfolders_AllCopied()
        //		{
        //			using (var source = new TemporaryFolder("SourceBookStorage"))
        //			using (var dest = new TemporaryFolder("DestBookStorage"))
        //			{
        //				File.WriteAllText(source.Combine("zero.txt"), "zero");
        //				Directory.CreateDirectory(source.Combine("inner"));
        //				File.WriteAllText(source.Combine("inner", "one.txt"), "one");
        //				Directory.CreateDirectory(source.Combine("inner", "more inner"));
        //				File.WriteAllText(source.Combine("inner", "more inner", "two.txt"), "two");
        //
        //				var storage = new BookStorage(source.FolderPath, null);
        //				storage.CopyToFolder(dest.FolderPath);
        //
        //				Assert.That(Directory.Exists(dest.Combine("inner", "more inner")));
        //				Assert.That(File.Exists(dest.Combine("zero.txt")));
        //				Assert.That(File.Exists(dest.Combine("inner", "one.txt")));
        //				Assert.That(File.Exists(dest.Combine("inner", "more inner", "two.txt")));
        //			}
        //		}


        [Test]
        public void SetupPage_LanguageSettingsHaveChanged_LangAttributesUpdated()
        {
            var contents =
                @"<html><body><div class='bloom-page'>
						<div class='bloom-translationGroup' data-book='foo'>
							<div class='bloom-editable' lang='en'></div>
							<div class='bloom-editable' lang='en'></div>
							<div class='bloom-editable' lang='en'></div>
						</div>
					</div></body></html>";

            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _defaultCollectionSettings, null);

            BookStarter.SetupPage(
                (SafeXmlElement)dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo']/div[@lang='fr']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo']/div[@lang='es']", 1);
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='foo']/div[@lang='xyz']", 1);
        }

        [Test]
        public void SetupPage_PageHadDescription_DescriptionClearedButEnglishStillThere()
        {
            var contents =
                @"<html><body><div class='bloom-page'>
						 <div class='pageDescription' lang='en'>hello</div>
						 <div class='pageDescription' lang='fr'>bonjour</div>
					</div></body></html>";

            var dom = new HtmlDom(contents);
            var bookData = new BookData(dom, _alternateCollectionSettings, null);

            BookStarter.SetupPage(
                (SafeXmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],
                bookData
            );
            //should remove the French (I don't see that we actually have any templates that have anything but English, but just in case)
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'pageDescription') and @lang != 'en']",
                    0
                );
            //should leave English as a placeholder
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class, 'pageDescription') and not(normalize-space(.))]",
                    1
                );
        }

        [Test]
        public void CreateBookOnDiskFromTemplate_FromTranslatedMoonAndCap_HasNoCopyrightOrVersionAcknowledgements()
        {
            // Get the moon and cap book. This is a better sample than Vaccinations, which lacks the
            // typical meta.json.
            var source1 = Path.Combine(
                BloomFileLocator.SampleShellsDirectory,
                "The Moon and the Cap"
            );
            // Make a 'translation' of moon and cap
            var derivedBook = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source1, _projectFolder.Path)
            );
            // Modify it to have its own copyright and versionAcknowledgements.
            var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(derivedBook));
            var source2 = Path.GetDirectoryName(derivedBook);
            var bookdata = new BookData(
                dom,
                _defaultCollectionSettings,
                imgNode =>
                    ImageUpdater.UpdateImgMetadataAttributesToMatchImage(
                        source2,
                        imgNode,
                        new NullProgress()
                    )
            );
            bookdata.Set("copyright", XmlString.FromXml("Copyright 2018 some translator"), "*");
            bookdata.Set(
                "versionAcknowledgments",
                XmlString.FromXml("some translator worked on this"),
                "en"
            );
            bookdata.UpdateDomFromDataset();
            XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, derivedBook);
            var metadata = BookMetaData.FromFolder(source2);
            metadata.Copyright = "Copyright 2018 some translator";
            metadata.WriteToFolder(source2);

            // derive a new book from the modified translation.
            var path = GetPathToHtml(
                _starter.CreateBookOnDiskFromTemplate(source2, _projectFolder.Path)
            );

            var assertThatHtmlInBook = AssertThatXmlIn.HtmlFile(path);
            assertThatHtmlInBook.HasNoMatchForXpath("//div[@data-book='copyright']");
            // Book typically has some empty versionAcknowledgements inserted as xmatter.
            assertThatHtmlInBook.HasNoMatchForXpath(
                "//div[@data-book='versionAcknowledgments' and text()]"
            );
            metadata = BookMetaData.FromFolder(Path.GetDirectoryName(path));
            Assert.That(metadata.Copyright, Is.Null);
        }

        [TestCase("How to manage titles")]
        [TestCase("HTML Lesson 1: <strong> & <em> tags")] // Test '&', '<', '>' chars
        public void SetOriginalCopyrightAndLicense_HasCopyrightAndLicense_MovesToOriginalCopyrightAndLicense(
            string unencodedOriginalTitle
        )
        {
            string encodedOriginalTitle = HttpUtility.HtmlEncode(unencodedOriginalTitle);
            var dom = SetOriginalCopyrightAndLicense(
                $@" <div id='bloomDataDiv'>
					<div data-book='copyright' lang='*'> Copyright © 2007, Foo Publishing </div>
					<div data-book='licenseUrl' lang='*'> http://creativecommons.org/licenses/by-nc/3.0/ </div>
					<div data-book='originalTitle' lang='*'>{encodedOriginalTitle}</div>
				</div>"
            );

            // Need to make sure we get the unencoded form here, otherwise we will add more and more layers of escaping when the book is saved.
            Assert.AreEqual(
                $"This book is an adaptation of the original, <cite data-book=\"originalTitle\">{unencodedOriginalTitle}</cite>, Copyright © 2007, Foo Publishing. Licensed under CC BY-NC 3.0.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
            AssertOriginalCopyrightAndLicense(
                dom,
                "Copyright © 2007, Foo Publishing",
                "http://creativecommons.org/licenses/by-nc/3.0/"
            );
        }

        [Test]
        public void SetOriginalCopyrightAndLicense_HasCopyrightAndLicenseAndExtra_CopyrightIsNowEmpty()
        {
            var dom = TransformCreditPageData(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'> Copyright © 2007, Foo Publishing </div>
						<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
					</div>"
            );
            AssertThatXmlIn
                .Dom(dom.RawDom)
                .HasNoMatchForXpath("//div[@id='bloomDataDiv']/div[@data-book='copyright']");
        }

        [Test]
        public void SetOriginalCopyrightAndLicense_HasCreativeCommonsLicenseAndNotes_NotesRetainedInOriginal()
        {
            var dom = SetOriginalCopyrightAndLicense(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
					<div data-derived='licenseNotes' lang='*'>
						You can do anything you want if your name is Fred.
					</div>
					<div data-book='originalTitle' lang='*'>How to manage titles</div>
				</div>"
            );
            Assert.AreEqual(
                "This book is an adaptation of the original without a copyright notice, <cite data-book=\"originalTitle\">How to manage titles</cite>. Licensed under CC BY 4.0. You can do anything you want if your name is Fred.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
            AssertOriginalCopyrightAndLicense(
                dom,
                "",
                "http://creativecommons.org/licenses/by/4.0/",
                "You can do anything you want if your name is Fred."
            );
        }

        [Test]
        public void SetOriginalCopyrightAndLicense_HasCustomLicenseAndNotes_NotesRetainedInOriginal()
        {
            var dom = SetOriginalCopyrightAndLicense(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
					<div data-derived='licenseNotes' lang='*'>
						You can do anything you want if your name is Fred.
					</div>
					<div data-book='originalTitle' lang='*'>This is another title</div>
				</div>"
            );
            Assert.AreEqual(
                "This book is an adaptation of the original without a copyright notice, <cite data-book=\"originalTitle\">This is another title</cite>. You can do anything you want if your name is Fred.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
            AssertOriginalCopyrightAndLicense(
                dom,
                "",
                "",
                "You can do anything you want if your name is Fred."
            );
        }

        // It's rather arbitrary what happens in this case, since we don't think it's possible for the original
        // to have a null license AND no copyright (short of hand-editing the HTML).
        public void SetOriginalCopyrightAndLicense_HasNoCopyrightOrLicense_GetsNoOriginalNotice()
        {
            var dom = SetOriginalCopyrightAndLicense(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
				</div>"
            );
            Assert.IsNull(GetEnglishOriginalCopyrightAndLicense(dom));
            AssertOriginalCopyrightAndLicense(dom, "", "", "");
        }

        [Test]
        public void SetOriginalCopyrightAndLicense_HasLicenseButNoCopyright_GetsExpectedOriginalCopyrightAndLicense()
        {
            var dom = SetOriginalCopyrightAndLicense(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
					<div data-book='originalTitle' lang='*'>How to manage titles</div>
					</div>"
            );
            Assert.AreEqual(
                "This book is an adaptation of the original without a copyright notice, <cite data-book=\"originalTitle\">How to manage titles</cite>. Licensed under CC BY 4.0.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
            AssertOriginalCopyrightAndLicense(
                dom,
                "",
                "http://creativecommons.org/licenses/by/4.0/"
            );
        }

        [Test]
        public void SetOriginalCopyrightAndLicense_NullLicense_GetsExpectedOriginalCopyrightAndLicense()
        {
            var dom = SetOriginalCopyrightAndLicense(
                @" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'> Copyright © 2007, Some Old Publisher </div>
					<div data-book='originalTitle' lang='*'>How to manage titles</div>
					</div>"
            );

            Assert.AreEqual(
                "This book is an adaptation of the original, <cite data-book=\"originalTitle\">How to manage titles</cite>, Copyright © 2007, Some Old Publisher.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
            AssertOriginalCopyrightAndLicense(dom, "Copyright © 2007, Some Old Publisher", "", "");
        }

        // when we use a translation, it may have its own copyright. However that doesn't mean that we ever replace
        // the "original" copyright and license. Those stick with the book through all adaptations.
        [TestCase("How to manage titles")]
        [TestCase("HTML Lesson 1: <strong> & <em> tags")] // Test '&', '<', '>' chars
        public void SetOriginalCopyrightAndLicense_SourceIsAlsoAnAdaptation_OriginalCopyrightAndLicensePreserved(
            string unencodedOriginalTitle
        )
        {
            string encodedOriginalTitle = HttpUtility.HtmlEncode(unencodedOriginalTitle);
            var dom = SetOriginalCopyrightAndLicense(
                $@" <div id='bloomDataDiv'>
					<div data-book='bookTitle' lang='en'>A really really empty book</div>
						<div data-book='copyright' lang='*'>
						Copyright © 2007, Foo Publishers
						</div>
					<div data-book='licenseUrl' lang='*'>
						http://creativecommons.org/licenses/by/4.0/
						</div>
						<div data-derived='licenseNotes' lang='*'>
							You can do anything you want if your name is Fred.
						</div>
					<div data-book='originalTitle' lang='*'>{encodedOriginalTitle}</div>
					</div>"
            );
            // now do it again, simulating adaptation from the translation with different copyright etc.
            var dataDiv = dom.SelectSingleNode("//div[@id='bloomDataDiv']");
            AppendDataDivElement(dataDiv, "copyright", "*", "Copyright © 2008, Bar Translators");
            AppendDataDivElement(
                dataDiv,
                "licenseUrl",
                "*",
                "http://creativecommons.org/licenses/by-nc/4.0/"
            );
            AppendDataDivElement(
                dataDiv,
                "licenseNotes",
                "*",
                "You can do almost anything if your name is John"
            );
            var bookData = new BookData(dom, _alternateCollectionSettings, null);
            BookStarter.SetOriginalCopyrightAndLicense(dom, bookData, _alternateCollectionSettings);
            Assert.AreEqual(
                $"This book is an adaptation of the original, <cite data-book=\"originalTitle\">{unencodedOriginalTitle}</cite>, Copyright © 2007, Foo Publishers. Licensed under CC BY 4.0. You can do anything you want if your name is Fred.",
                GetEnglishOriginalCopyrightAndLicense(dom)
            );
            AssertOriginalCopyrightAndLicense(
                dom,
                "Copyright © 2007, Foo Publishers",
                "http://creativecommons.org/licenses/by/4.0/",
                "You can do anything you want if your name is Fred."
            );
        }

        // Tests Ampersand both in the copyright and originalTitle components
        [Test]
        public void AmpersandInOriginalCopyrightHandledProperly()
        {
            // See http://issues.bloomlibrary.org/youtrack/issue/BL-4585.
            var dom = new HtmlDom(
                @"<html>
				  <head></head>
				  <body>
				    <div id='bloomDataDiv'>
				      <div data-book='copyright' lang='*'>
				        Copyright © 2011, LASI &amp; SILA
				      </div>
				      <div data-book='licenseUrl' lang='en'>
				        http://creativecommons.org/licenses/by-nc-sa/4.0/
				      </div>
					<div data-book='originalTitle' lang='*'>HTML Lesson 1: &lt;strong&gt; &amp; &lt;em&gt; tags</div>
				    </div>
				    <div class='bloom-page cover frontCover outsideFrontCover coverColor bloom-frontMatter A4Landscape layout-style-Default' data-page='required singleton' data-export='front-matter-cover' id='2c97f5ad-24a1-47f0-8b5c-fa2181e1b129'>
				      <div class='bloom-page cover frontCover outsideFrontCover coverColor bloom-frontMatter verso A4Landscape layout-style-Default' data-page='required singleton' data-export='front-matter-credits' id='7a220c97-07e4-47c5-835a-e37dc921f98f'>
				        <div class='marginBox'>
				          <div data-functiononhintclick='bookMetadataEditor' data-hint='Click to Edit Copyright &amp; License' id='versoLicenseAndCopyright' class='bloom-metaData'>
				            <div data-derived='copyright' lang='*' class='copyright'></div>
				            <div data-derived='originalCopyrightAndLicense' lang='en' class='copyright'></div>
				          </div>
				        </div>
				      </div>
				    </div>
				  </body>
				</html>"
            );
            var bookData = new BookData(dom, _alternateCollectionSettings, null);

            var metadata = BookCopyrightAndLicense.GetMetadata(dom, bookData);
            var initialCopyright = metadata.CopyrightNotice;
            Assert.AreEqual("Copyright © 2011, LASI & SILA", initialCopyright);

            BookStarter.SetOriginalCopyrightAndLicense(dom, bookData, _alternateCollectionSettings);
            var originalCopyright = GetEnglishOriginalCopyrightAndLicense(dom);
            Assert.AreEqual(
                "This book is an adaptation of the original, <cite data-book=\"originalTitle\">HTML Lesson 1: <strong> & <em> tags</cite>, Copyright © 2011, LASI & SILA. Licensed under CC BY-NC-SA 4.0.",
                originalCopyright
            );

            BookCopyrightAndLicense.UpdateDomFromDataDiv(dom, null, bookData, false);
            var nodes1 = dom.RawDom.SafeSelectNodes(
                "/html/body//div[@data-derived='originalCopyrightAndLicense']"
            );
            Assert.AreEqual(1, nodes1.Length);
            Assert.AreEqual(
                "This book is an adaptation of the original, HTML Lesson 1: <strong> & <em> tags, Copyright © 2011, LASI & SILA. Licensed under CC BY-NC-SA 4.0.",
                nodes1[0].InnerText
            );
            Assert.AreEqual(
                "This book is an adaptation of the original, <cite data-book=\"originalTitle\">HTML Lesson 1: &lt;strong&gt; &amp; &lt;em&gt; tags</cite>, Copyright © 2011, LASI &amp; SILA. Licensed under CC BY-NC-SA 4.0.",
                nodes1[0].InnerXml
            );
            BookStarterTests.AssertOriginalCopyrightAndLicense(
                dom,
                "Copyright © 2011, LASI &amp; SILA",
                "http://creativecommons.org/licenses/by-nc-sa/4.0/"
            );
        }

        private string GetEnglishOriginalCopyrightAndLicense(HtmlDom dom)
        {
            var bookData = new BookData(dom, _alternateCollectionSettings, null);
            return BookCopyrightAndLicense.GetOriginalCopyrightAndLicenseNotice(bookData, dom);
        }

        void AppendDataDivElement(SafeXmlElement dataDiv, string dataBook, string lang, string val)
        {
            var newDiv = dataDiv.OwnerDocument.CreateElement("div");
            newDiv.SetAttribute("data-book", dataBook);
            newDiv.SetAttribute("lang", lang);
            newDiv.InnerText = val;
            dataDiv.AppendChild(newDiv);
        }

        private HtmlDom TransformCreditPageData(string dataDivString)
        {
            // All of the tests using this method require that the book is locked down (that is, a derivative that
            // is expected to have original copyright and license information).
            var html = "<html><head></head><body>" + dataDivString + "</body></html>";
            var dom = new HtmlDom(html);
            using (var folder = new TemporaryFolder("TransformCreditPageData"))
            {
                File.WriteAllText(
                    Path.Combine(folder.Path, "TransformCreditPageData.htm"),
                    XmlHtmlConverter.ConvertDomToHtml5(dom.RawDom)
                );
                var storage = new BookStorage(
                    folder.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    _alternateCollectionSettings
                );
                var bookData = new BookData(dom, _defaultCollectionSettings, null);
                BookStarter.TransformCreditPageData(
                    dom,
                    bookData,
                    _alternateCollectionSettings,
                    storage,
                    true
                );
            }
            return dom;
        }

        private HtmlDom SetOriginalCopyrightAndLicense(string dataDivString)
        {
            // All of the tests using this method require that the book is locked down (that is, a derivative that
            // is expected to have original copyright and license information).
            var html = "<html><head></head><body>" + dataDivString + "</body></html>";
            var dom = new HtmlDom(html);
            var bookData = new BookData(dom, _alternateCollectionSettings, null);
            BookStarter.SetOriginalCopyrightAndLicense(dom, bookData, _defaultCollectionSettings);
            return dom;
        }

        public static void AssertOriginalCopyrightAndLicense(
            HtmlDom dom,
            string copyright,
            string license,
            string licenseNotes = ""
        )
        {
            Assert.That(dom.GetBookSetting("originalCopyright")["*"], Is.EqualTo(copyright));
            if (String.IsNullOrEmpty(copyright))
                AssertThatXmlIn
                    .Dom(dom.RawDom)
                    .HasNoMatchForXpath(
                        "//div[@id='bloomDataDiv']/div[@data-book='originalCopyright']"
                    );
            Assert.That(dom.GetBookSetting("originalLicenseUrl")["*"], Is.EqualTo(license));
            if (String.IsNullOrEmpty(license))
                AssertThatXmlIn
                    .Dom(dom.RawDom)
                    .HasNoMatchForXpath(
                        "//div[@id='bloomDataDiv']/div[@data-book='originalLicenseUrl']"
                    );
            Assert.That(dom.GetBookSetting("originalLicenseNotes")["*"], Is.EqualTo(licenseNotes));
            if (String.IsNullOrEmpty(licenseNotes))
                AssertThatXmlIn
                    .Dom(dom.RawDom)
                    .HasNoMatchForXpath(
                        "//div[@id='bloomDataDiv']/div[@data-book='originalLicenseNotes']"
                    );
        }

        /// <summary>
        /// This is a regression test for bl-1210, where the translation-group for title would only
        /// get bloom-editables with the *current* languages. The problem with that is that then the
        /// source bubble didn't offer up the title in *other* languages.
        /// </summary>
        /* At the moment, it doesn't appear that we need BookStarter to deal with this.
        Keeping this test in case we decide that it does
        [Test]
        public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_CoverHasVariousBookTitles()
        {
            var source = Path.Combine(BloomFileLocator.SampleShellsDirectory,"Vaccinations");

            var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

            AssertThatXmlIn.HtmlFile(path).HasAtLeastOneMatchForXpath("//div[contains(@class,'outsideFrontCover')]//div[@data-book='bookTitle' and @lang='es' and text()]");
            AssertThatXmlIn.HtmlFile(path).HasAtLeastOneMatchForXpath("//div[contains(@class,'outsideFrontCover')]//div[@data-book='bookTitle' and @lang='tpi' and text()]");
        }*/
    }
}
