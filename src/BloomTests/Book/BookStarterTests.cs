using System.IO;
using System.Xml;
using Bloom;
using Bloom.Book;
using Moq;
using NUnit.Framework;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;
using Palaso.Xml;

namespace BloomTests.Book
{
	[TestFixture]
	public class BookStarterTests
	{
		private FileLocator _fileLocator;
		private BookStarter _starter;
		private TemporaryFolder _shellCollectionFolder;
		private TemporaryFolder _projectFolder;
		private Mock<LibrarySettings> _librarySettings;

		[SetUp]
		public void Setup()
		{
			_librarySettings = new Moq.Mock<LibrarySettings>();
			_librarySettings.SetupGet(x => x.IsShellLibrary).Returns(false);
			_librarySettings.SetupGet(x => x.VernacularIso639Code).Returns("xyz");
			_librarySettings.SetupGet(x => x.NationalLanguage1Iso639Code).Returns("fr");
			_librarySettings.SetupGet(x => x.NationalLanguage2Iso639Code).Returns("es");
			_librarySettings.SetupGet(x => x.XMatterPackName).Returns("Factory");
			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												FileLocator.GetDirectoryDistributedWithApplication("root"),
												FileLocator.GetDirectoryDistributedWithApplication("xMatter"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "BasicBook"),
												FileLocator.GetDirectoryDistributedWithApplication( "xMatter", "Factory-XMatter")
											});
			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), _librarySettings.Object);

			_shellCollectionFolder = new TemporaryFolder("BookStarterTests_ShellCollection");
			_projectFolder = new TemporaryFolder("BookStarterTests_ProjectCollection");
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

		[Test, Ignore("Current architecture gives spreads this responsibility for updating to Book, so can't be tested here.")]
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
																			"BasicBook");

			_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_CreatesWithCoverAndTitle()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"BasicBook");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover ')]", 1);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'titlePage')]", 1);

			//should only get these two pages
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-page')]", 3);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5AndXMatter_CoverTitleIsIntiallyEmpty()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"BasicBook");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover ')]//div[@lang='xyz' and contains(@data-book, 'bookTitle') and not(text())]", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryBasicBook_CreatesWithCorrectStylesheets()
		{
				 var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "BasicBook");

				 var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
				AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'BasicBook')]", 1);
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
//			//var library = new Moq.Mock<LibrarySettings>();
//			_librarySettings.SetupGet(x => x.IsShellLibrary).Returns(true);
//			//_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), library.Object);
//			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
//			AssertThatXmlIn.HtmlFile(path).HasAtLeastOneMatchForXpath("//meta[@name='editability' and @content='translationOnly']");
//		}
//
//		[Test]
//		public void CreateBookOnDiskFromTemplate_NotInShellMakingMode_editabilityMetaOpen()
//		{
//			var library = new Moq.Mock<LibrarySettings>();
//			library.SetupGet(x => x.IsShellLibrary).Returns(false);
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
			string sourceTemplateFolder = GetShellBookFolder(body);
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
			string sourceTemplateFolder = GetShellBookFolder(body);
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
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz' and text()='original']", 1);
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
			string sourceTemplateFolder = GetShellBookFolder(body);
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
			string sourceTemplateFolder = GetShellBookFolder(body);
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
						<div class='bloom-page'>keep me</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasNoMatchForXpath("//div[contains(@class,'bloom-frontMatter')]");
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'bloom-page')]",1);
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
		public void CreateBookOnDiskFromTemplate_ShellHasNoNameDirective_NameSameAsShell()
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
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_GetsExpectedName()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"BasicBook");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
			var path = GetPathToHtml(bookFolderPath);

			Assert.AreEqual("My Book.htm", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(bookFolderPath));
			Assert.IsTrue(File.Exists(path));
		}


		[Test]
		public void UpdateContentLanguageClasses_NewPage_AddsContentLanguageClasses()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							<textarea lang='222'></textarea>
							<textarea lang='333'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement) dom.SafeSelectNodes("//div[@class='bloom-page']")[0];
			BookStarter.UpdateContentLanguageClasses(pageDiv, "xyz", _librarySettings.Object.NationalLanguage1Iso639Code, _librarySettings.Object.NationalLanguage2Iso639Code, "222", "333");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='222' and contains(@class, 'bloom-content2')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'bloom-content3')]", 1);
		}


		[Test]
		public void UpdateContentLanguageClasses_FrontMatterPage_AddsNationalLanguageClasses()
		{
			var contents = @"<div class='bloom-page bloom-frontMatter'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							<textarea lang='fr'></textarea>
							<textarea lang='es'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			BookStarter.UpdateContentLanguageClasses(pageDiv, "xyz", _librarySettings.Object.NationalLanguage1Iso639Code, _librarySettings.Object.NationalLanguage2Iso639Code, "222", "333");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='fr' and contains(@class, 'bloom-contentNational1')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='es' and contains(@class, 'bloom-contentNational2')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_ExistingPageWith3rdLangRemoved_RemovesContentLanguageClassButLeavesOtherClasses()
		{
			var contents = @"<div class='bloom-page'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							<textarea class='bloom-content2' lang='222'></textarea>
							<textarea  class='foo bloom-content3 bar' lang='333'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			BookStarter.UpdateContentLanguageClasses(pageDiv, "xyz", _librarySettings.Object.NationalLanguage1Iso639Code, _librarySettings.Object.NationalLanguage2Iso639Code, "222", null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='222' and contains(@class, 'bloom-content2')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'bloom-content3')]", 0);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'foo')]", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='333' and contains(@class, 'bar')]", 1);
		}


		[Test]
		public void UpdateContentLanguageClasses_MonoLingualBook_AddsBloomMonolingualClassToTranslationGroup()
		{
			var contents = @"<div class='bloom-page bloom-bilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			BookStarter.UpdateContentLanguageClasses(pageDiv, "xyz", _librarySettings.Object.NationalLanguage1Iso639Code, _librarySettings.Object.NationalLanguage2Iso639Code, null, null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-bilingual')]", 0);//should remove that one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-monolingual')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_BilingualBook_AddsBloomBilingualClassToTranslationGroup()
		{
			var contents = @"<div class='bloom-page  bloom-trilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			BookStarter.UpdateContentLanguageClasses(pageDiv, "xyz", _librarySettings.Object.NationalLanguage1Iso639Code, _librarySettings.Object.NationalLanguage2Iso639Code, "222", null);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-monolingual')]", 0);//should remove that one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-bilingual')]", 1);
		}

		[Test]
		public void UpdateContentLanguageClasses_TrilingualBook_AddsBloomTrilingualClassToTranslationGroup()
		{
			var contents = @"<div class='bloom-page  bloom-bilingual'>
						<div class='bloom-translationGroup'>
							<textarea lang='en'></textarea>
							</div>
						</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);
			var pageDiv = (XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0];
			BookStarter.UpdateContentLanguageClasses(pageDiv, "xyz", _librarySettings.Object.NationalLanguage1Iso639Code, _librarySettings.Object.NationalLanguage2Iso639Code, "222", "333");
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-bilingual')]", 0);//should remove that one
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'bloom-trilingual')]", 1);
		}



		[Test]
		public void PrepareElementsOnPage_HasNonEditableDiv_LeavesAlone()
		{
			var contents = @"<div class='bloom-page'>
						<table class='bloom-translationGroup'> <!-- table is used for vertical alignment of the div on some pages -->
						 <td>
							<div lang='en'>This is some English</div>
						</td>
						</table>
					</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			BookStarter.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[@class='bloom-page']")[0], _librarySettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//td/div", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//td/div[@lang='en']", 1);
		}

		[Test]
		public void PrepareElementsOnPage_HasTextAreaInsideTranslationGroup_MakesVernacularAndNational()
		{
			var contents = @"<div class='bloom-page bloom-translationGroup'>
						<textarea lang='en'>This is some English</textarea>
					</div>";
			var dom = new XmlDocument();
			dom.LoadXml(contents);

			BookStarter.PrepareElementsInPageOrDocument((XmlElement)dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")[0],_librarySettings.Object);

			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea", 4);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='fr']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='es']", 1);
			AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz']", 1);
		}



		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryTemplate_SameNameAlreadyUsed_FindsUsableNumberSuffix()
		{
			Directory.CreateDirectory(_projectFolder.Combine("My Book"));
			Directory.CreateDirectory(_projectFolder.Combine("My Book1"));
			Directory.CreateDirectory(_projectFolder.Combine("My Book3"));

			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"BasicBook");

			var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

			Assert.AreEqual("My Book2", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(path));
			Assert.IsTrue(File.Exists(Path.Combine(path, "My Book2.htm")));
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
					</div>");
		}
		private string GetShellBookFolder(string bodyContents)
		{
			var content =
				@"<?xml version='1.0' encoding='utf-8' ?>
				<!DOCTYPE html>
				<html>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='BasicBook.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />
					<meta name='defaultNameForDerivedBooks' content='guitar'/>
				</head>
				<body class='a5Portrait'>" +
				bodyContents + "</body></html>";
			string folder = _shellCollectionFolder.Combine("guitar");
			Directory.CreateDirectory(folder);
			string shellFolderPath = Path.Combine(folder, "guitar.htm");
			File.WriteAllText(shellFolderPath, content);
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
