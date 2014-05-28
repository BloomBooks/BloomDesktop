using System;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Moq;
using NUnit.Framework;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;

namespace BloomTests.Book
{
	[TestFixture]
	public class BookStarterTests
	{
		private FileLocator _fileLocator;
		private BookStarter _starter;
		private TemporaryFolder _shellCollectionFolder;
		private TemporaryFolder _projectFolder;
		private Mock<CollectionSettings> _librarySettings;

		[SetUp]
		public void Setup()
		{
			_librarySettings = new Moq.Mock<CollectionSettings>();
			_librarySettings.SetupGet(x => x.IsSourceCollection).Returns(false);
			_librarySettings.SetupGet(x => x.Language1Iso639Code).Returns("xyz");
			_librarySettings.SetupGet(x => x.Language2Iso639Code).Returns("fr");
			_librarySettings.SetupGet(x => x.Language3Iso639Code).Returns("es");
			_librarySettings.SetupGet(x => x.XMatterPackName).Returns("Factory");
			ErrorReport.IsOkToInteractWithUser = false;
			_projectFolder = new TemporaryFolder("BookStarterTests_ProjectCollection");
			var collectionSettings = new CollectionSettings(Path.Combine(_projectFolder.Path, "test.bloomCollection"));

			var xmatterFinder = new XMatterPackFinder(new []{FileLocator.GetDirectoryDistributedWithApplication("xMatter")});

			_fileLocator = new BloomFileLocator(collectionSettings, xmatterFinder, ProjectContext.GetFactoryFileLocations(), ProjectContext.GetFoundFileLocations());


			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator, new BookRenamedEvent(), collectionSettings), _librarySettings.Object);
			_shellCollectionFolder = new TemporaryFolder("BookStarterTests_ShellCollection");
		}

		[TearDown]
		public void TearDown()
		{
			_shellCollectionFolder.Dispose();
			_projectFolder.Dispose();
		}

		private string GetPathToHtml(string bookFolderPath)
		{
			return Path.Combine(bookFolderPath, Path.GetFileName(bookFolderPath))+".htm";
		}
//
//        [Test]
//        public void CreateBookOnDiskFromTemplate_HasConfigurationPage_xxxxxxxxxxxx()
//        {
//            var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells",
//                                                                            "Calendar");
//
//            var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
//        }

		[Test]
		public void CreateBookOnDiskFromTemplate_OriginalHasISBN_CopyDoesNotHaveISBN()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells","Vaccinations");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasNoMatchForXpath("//div[@id='bloomDataDiv' and @data-book='ISBN']");
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@data-book='ISBN' and not(text())]", 1);
		}

		//regression
		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_CoverHasOneVisibleVernacularTitle()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells",
																			"Vaccinations");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'cover')]//*[@data-book='bookTitle' and @lang='xyz']", 1);
		}

		//regression
		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_DoesNotLoseTokPisinTitle()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells",
																			"Vaccinations");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='tpi' and text()='Tambu Sut']", 1);
		}

		//regression
		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_InitialFolderNameIsCalledVaccinations()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells",
																			"Vaccinations");

			var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
			Assert.AreEqual("Vaccinations", Path.GetFileName(path));

			//NB: although the clas under test here may produce a folder with the right name, the Book class may still mess it up based on variables
			//But that is a different set of unit tests.
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasDataDivIntact()
		{
			AssertThatXmlIn.HtmlFile(GetNewVaccinationsBookPath()).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasTitlePage()
		{
			AssertThatXmlIn.HtmlFile(GetNewVaccinationsBookPath()).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'titlePage')]", 1);
		}

		[Test, Ignore("Current architecture gives responsibility for updating to Book, so can't be tested here.")]
		public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasCorrectImageOnCover()
		{
			AssertThatXmlIn.HtmlFile(GetNewVaccinationsBookPath()).HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class,'cover')]//img[@src='HL0014-1.png']", 1);
		}

		[Test, Ignore("Current architecture spreads this responsibility for updating to Book, so can't be tested here.")]
		public void CreateBookOnDiskFromTemplate_FromFactoryVaccinations_HasCorrectTopicOnCover()
		{
			AssertThatXmlIn.HtmlFile(GetNewVaccinationsBookPath()).HasSpecifiedNumberOfMatchesForXpath(
				"//div[contains(@class,'cover')]//*[@data-book='topic' and text()='Health']", 1);
		}


		private string GetNewVaccinationsBookPath()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Sample Shells",
																			"Vaccinations");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
			return path;
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_Validates()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"Basic Book");

			_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_CreatesWithCoverAndTitle()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"Basic Book");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover ')]", 4);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'titlePage')]", 1);

			//should only get these two pages
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-page')]", 5);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5AndXMatter_CoverTitleIsIntiallyEmpty()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"Basic Book");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover ')]//div[@lang='xyz' and contains(@data-book, 'bookTitle') and not(text())]", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryBasicBook_CreatesWithCorrectStylesheets()
		{
				 var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "Basic Book");

				 var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
				AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'Basic Book')]", 1);
				AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
				AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'basePage')]", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_PagesLabeledExtraAreNotAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
		   AssertThatXmlIn.HtmlFile(path).HasNoMatchForXpath("//div[contains(text(), '_extra_')]");
		}


//		[Test]
//		public void CreateBookOnDiskFromTemplate_InShellMakingMode_editabilityMetaIsTranslationOnly()
//		{
//			//var library = new Moq.Mock<CollectionSettings>();
//			_librarySettings.SetupGet(x => x.IsSourceCollection).Returns(true);
//			//_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), library.Object);
//			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
//			AssertThatXmlIn.HtmlFile(path).HasAtLeastOneMatchForXpath("//meta[@name='editability' and @content='translationOnly']");
//		}
//
//		[Test]
//		public void CreateBookOnDiskFromTemplate_NotInShellMakingMode_editabilityMetaOpen()
//		{
//			var library = new Moq.Mock<CollectionSettings>();
//			library.SetupGet(x => x.IsSourceCollection).Returns(false);
//			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), library.Object);
//			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
//			AssertThatXmlIn.HtmlFile(path).HasAtLeastOneMatchForXpath("//meta[@name='editability' and @content='open']");
//		}



		[Test]
		public void CreateBookOnDiskFromTemplate_HasEnglishTextArea_VernacularTextAreaAdded()
		{
			_starter.TestingSoSkipAddingXMatter = true;
			var body = @"<div class='bloom-page'>
						<p class='bloom-translationGroup'>
						 <textarea lang='en'>This is some English</textarea>
						</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body,null);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			//nb: testid is used rather than id because id is replaced with a guid when the copy is made

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='en']", 1);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
			//the new text should also have been emptied of English
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz' and not(text())]", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_HasTokPisinTextAreaSurroundedByParagraph_VernacularTextAreaAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='tpi']", 1);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='xyz']", 1);
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
			var body = @"<div class='bloom-page'>
						<p>
							<textarea lang='en'>LanguageName</textarea>
						</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body, null);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//textarea", 1);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en']", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_AlreadyHasVernacular_LeavesUntouched()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='en']", 1);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz']", 1);
// this, the original version started failing when the xml started putting the closing tag on the next line, so I changed it to 'starts-with'
			//AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz' and text()='original']", 1);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz' and starts-with(text(),'original')]", 1);
		}
		[Test]
		public void CreateBookOnDiskFromTemplate_Has2SourceLanguagesTextArea_OneVernacularTextAreaAdded()
		{
			_starter.TestingSoSkipAddingXMatter = true;
			var body = @"<div class='bloom-page '>
							<p class='bloom-translationGroup'>
								<textarea lang='en'> When you plant a garden you always make a fence.</textarea>
								<textarea lang='tpi'> Taim yu planim gaden yu save wokim banis.</textarea>
							</p>
						</div>";
			string sourceTemplateFolder = GetShellBookFolder(body, null);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_HasBookTitleWithEnglish_HasItWithVernacular()
		{
			_starter.TestingSoSkipAddingXMatter = true;
			var body = @"<div class='bloom-page'>
							<p class='bloom-translationGroup'>
								<textarea data-book='bookTitle' class='vernacularBookTitle' lang='en'>Book Name</textarea>
							 </p>
						</div>";
			string sourceTemplateFolder = GetShellBookFolder(body, null);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
		}

		/// <summary>
		/// we want to remove any front-matter left in the shell book
		/// </summary>
		[Test]
		public void CreateBookOnDiskFromTemplate_SourceHasXMatter_Removed()
		{
			_starter.TestingSoSkipAddingXMatter = true;
			var body = @"<div class='bloom-page bloom-frontMatter'>don't keep me</div>
						<div class='bloom-page'>keep me</div>
						<div class='bloom-page bloom-backMatter'>don't keep me</div>";
			string sourceTemplateFolder = GetShellBookFolder(body, null);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasNoMatchForXpath("//div[contains(@class,'bloom-frontMatter')]");
			AssertThatXmlIn.HtmlFile(path).HasNoMatchForXpath("//div[contains(@class,'bloom-backMatter')]");
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]", 1);
		}

	   [Test]
		public void CreateBookOnDiskFromTemplate_TextAreaHasNoText_VernacularLangAttrSet()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithNoLanguageTags']/p/textarea", 3);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithNoLanguageTags']/p/textarea[@lang='xyz']", 1);
		}
		[Test]
		public void CreateBookOnDiskFromTemplate_PagesNotLabeledExtraAreAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(text(), '_normal_')]", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_PagesLabeledRequiredIsAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(text(), '_required_')]", 1);
		}



		[Test]
		public void CreateBookOnDiskFromTemplate_ShellHasNoNameDirective_FileNameSameAsShell()
		{
			string folderPath = _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path);
			Assert.AreEqual("guitar", Path.GetFileName(folderPath));
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_BookWithDefaultNameAlreadyExists_NameGetsNumberSuffix()
		{
			string firstPath = _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path);
			string secondPath = _starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path);

			Assert.IsTrue(File.Exists(firstPath.CombineForPath("guitar.htm")));
			Assert.IsTrue(File.Exists(secondPath.CombineForPath("guitar1.htm")));
			Assert.IsTrue(Directory.Exists(secondPath),"it clobbered the first one!");
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromBasicBook_GetsExpectedFileName()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates","Basic Book");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
			var path = GetPathToHtml(bookFolderPath);

			Assert.AreEqual("My Book.htm", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(bookFolderPath));
			Assert.IsTrue(File.Exists(path));
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromBasicBook_GetsExpectedEnglishTitleInDataDivAndJson()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates","Basic Book");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
			var path = GetPathToHtml(bookFolderPath);

			//see  <meta name="defaultNameForDerivedBooks" content="My Book" />
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='en' and text()='My Book']",1);

			var metadata = new BookInfo(bookFolderPath, false);
			Assert.That(metadata.Title, Is.EqualTo("My Book"));
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromBasicBook_GetsNoDefaultNameMetaElement()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "Basic Book");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

			//see  <meta name="defaultNameForDerivedBooks" content="My Book" />
			AssertThatXmlIn.HtmlFile(GetPathToHtml(bookFolderPath)).HasSpecifiedNumberOfMatchesForXpath("//meta[@name='defaultNameForDerivedBooks']", 0);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromBasicBook_CreatesCorrectMetaJson()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "Basic Book");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

			var jsonFile = Path.Combine(bookFolderPath, BookInfo.MetaDataFileName);
			Assert.That(File.Exists(jsonFile), "Creating a book should create a metadata json file meta.json");

			var metadata = new BookInfo(bookFolderPath, false);
			Assert.That(metadata.Id, Is.Not.Null, "New book should get an ID");
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromBasicBook_BookLineageSetToIdOfSourceBook()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "Basic Book");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

			var jsonFile = Path.Combine(bookFolderPath, BookInfo.MetaDataFileName);
			Assert.That(File.Exists(jsonFile), "Creating a book should create a metadata json file meta.json");
			var metadata = new BookInfo(bookFolderPath, false);

			var kIdOfBasicBook = "056B6F11-4A6C-4942-B2BC-8861E62B03B3";
			Assert.That(metadata.BookLineage, Is.EqualTo(kIdOfBasicBook));
			//we should get our own id, not reuse our parent's
			Assert.That(metadata.Id, Is.Not.EqualTo(kIdOfBasicBook), "New book should get its own ID, not reuse parent's");
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook()
		{
			// Try it when we have json and no lineage metadata in the html.
			CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook(null, true);
			// Should be able to get it from the old metadata if there is no json
			CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook("bloomBookLineage", false);
			// And from the even older metadata.
			CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook("bookLineage", false);
		}
		public void CreateBookOnDiskFromTemplate_SourceHasTwoInLineage_BookLineageExtedsThoseWithIdOfSourceBook(string lineage, bool includeJson)
		{
			var shellFolderPath = GetShellBookFolder(
				@" ", null, lineage, includeJson);
			string folderPath = _starter.CreateBookOnDiskFromTemplate(shellFolderPath, _projectFolder.Path);

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
					</div>", null);
			string folderPath = _starter.CreateBookOnDiskFromTemplate(shellFolderPath, _projectFolder.Path);
			AssertThatXmlIn.HtmlFile(GetPathToHtml(folderPath)).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='en' and text()='Vaccinations']", 1);
			AssertThatXmlIn.HtmlFile(GetPathToHtml(folderPath)).HasSpecifiedNumberOfMatchesForXpath("//div[@id='bloomDataDiv']/div[@data-book='bookTitle' and @lang='tpi' and text()='Tambu Sut']", 1);
		}







		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryTemplate_SameNameAlreadyUsed_FindsUsableNumberSuffix()
		{
			Directory.CreateDirectory(_projectFolder.Combine("My Book"));
			Directory.CreateDirectory(_projectFolder.Combine("My Book1"));
			Directory.CreateDirectory(_projectFolder.Combine("My Book3"));

			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"Basic Book");

			var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

			Assert.AreEqual("My Book2", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(path));
			Assert.IsTrue(File.Exists(Path.Combine(path, "My Book2.htm")));
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_CreationFailsForSomeReason_DoesNotLeaveIncompleteFolderAround()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "Basic Book");
			var goodPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
			Directory.Delete(goodPath, true); //remove that good one. We just did it to get an idea of what the path is

			//now fail while making a book

			_starter.OnNextRunSimulateFailureMakingBook = true;
			Assert.Throws<ApplicationException>(() => _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			Assert.IsFalse(Directory.Exists(goodPath), "Should not have left the folder there, after a failed book creation");
		}


		private string GetShellBookFolder()
		{
			return
				GetShellBookFolder(
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
						   null//no defaultNameForDerivedBooks
						   );
		}

		private string GetShellBookFolder(string bodyContents, string defaultNameForDerivedBooks, string lineageName = null, bool includeJson = true)
		{
			var lineage = "";
			if (lineageName != null)
				lineage = @"<meta name='" + lineageName + "' content='first,second' />";
			var idString = "";
			if (!includeJson)
				idString = @"<meta name='bloomBookId' content='thisNewGuy' />";
			var content = String.Format(
				@"<?xml version='1.0' encoding='utf-8' ?>
				<!DOCTYPE html>
				<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					{0}
					{1}
					<title>Test Shell</title>
					<link rel='stylesheet' href='Basic Book.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />", lineage, idString);
			if(!string.IsNullOrEmpty(defaultNameForDerivedBooks))
			{
				content += @"<meta name='defaultNameForDerivedBooks' content='"+defaultNameForDerivedBooks+"'/>";
			}
			content+= @"</head>
				<body class='a5Portrait'>" +
				bodyContents + "</body></html>";


			string folder = _shellCollectionFolder.Combine("guitar");
			Directory.CreateDirectory(folder);
			string shellFolderPath = Path.Combine(folder, "guitar.htm");
			File.WriteAllText(shellFolderPath, content);
			if (includeJson)
			{
				var json = "{'bookLineage':'first,second','bookInstanceId':'thisNewGuy','title':'Test Shell'}";
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
	}
}
