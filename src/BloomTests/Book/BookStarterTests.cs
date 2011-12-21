using System.IO;
using System.Xml;
using Bloom;
using Bloom.Book;
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
		[SetUp]
		public void Setup()
		{
			var library = new Moq.Mock<LibrarySettings>();
			library.SetupGet(x => x.IsShellLibrary).Returns(false);
			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												FileLocator.GetDirectoryDistributedWithApplication("root"),
												FileLocator.GetDirectoryDistributedWithApplication("xMatter"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "A5Portrait"),
												FileLocator.GetDirectoryDistributedWithApplication( "xMatter", "Factory-XMatter")
											});
			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), library.Object);

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
			AssertThatXmlIn.HtmlFile(GetNewVaccinationsBookPath()).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class,'-bloom-dataDiv')]", 1);
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
																			"A5Portrait");

			_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_CreatesWithCoverAndTitle()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"A5Portrait");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover ')]", 1);
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'titlePage')]", 1);

			//should only get these two pages
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, '-bloom-page')]", 3);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5Portrait_CreatesWithCorrectStylesheets()
		{
				 var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "A5Portrait");

				 var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
				AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'A5Portrait')]", 1);
				AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
				AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'basePage')]", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_PagesLabeledExtraAreNotAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
		   AssertThatXmlIn.HtmlFile(path).HasNoMatchForXpath("//div[contains(text(), '_extra_')]");
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_InShellMakingMode_editabilityMetaIsTranslationOnly()
		{
			var library = new Moq.Mock<LibrarySettings>();
			library.SetupGet(x => x.IsShellLibrary).Returns(true);
			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), library.Object);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasAtLeastOneMatchForXpath("//meta[@name='editability' and @content='translationOnly']");
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_NotInShellMakingMode_editabilityMetaOpen()
		{
			var library = new Moq.Mock<LibrarySettings>();
			library.SetupGet(x => x.IsShellLibrary).Returns(false);
			_starter = new BookStarter(_fileLocator, dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]), library.Object);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasAtLeastOneMatchForXpath("//meta[@name='editability' and @content='open']");
		}



		[Test]
		public void CreateBookOnDiskFromTemplate_HasEnglishTextArea_VernacularTextAreaAdded()
		{
			_starter.TestingSoSkipAddingXMatter = true;
			var body = @"<div class='-bloom-page'>
						<p>
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
		public void CreateBookOnDiskFromTemplate_ExistingEnglishHasHideClass_NewVernacularHasNoClass()
		{
			var body = @"<div class='-bloom-page'>
<p>
						<textarea lang='en'>blah</textarea>
</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz' and not(@class)]", 1);
		}


		/// <summary>
		/// NB: It's not clear what the behavior should eventually be... how do we know it isn't supposed to be in english?
		/// But for now, this gives us the behavior we want on the title page
		/// </summary>
		[Test]
		public void CreateBookOnDiskFromTemplate_HasEnglishParagraph_ConvertsToVernacular()//??????????????
		{
			_starter.TestingSoSkipAddingXMatter = true;
			var body = @"<div class='-bloom-page'>
						<p id='bookTitle' lang='en' data-book='bookTitle'>Book Title</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//p[@lang='xyz']", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_NationalLanguageField_LeavesUntouched()
		{
			_starter.TestingSoSkipAddingXMatter = true;
			var body = @"<div class='-bloom-page' testid='pageWithNoLanguageTags'>
						<p>
							<textarea lang='en' class='-bloom-showNational'>LanguageName</textarea>
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
			var body = @"<div class='-bloom-page'>
							<p>
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
			var body = @"<div class='-bloom-page'>
							<p>
								<textarea data-book='bookTitle' class='vernacularBookTitle' lang='en'>Book Name</textarea>
							 </p>
						</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
		}


	   [Test]
		public void CreateBookOnDiskFromTemplate_TextAreaHasNoText_VernacularLangAttrSet()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithNoLanguageTags']/p/textarea", 1);
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
																			"A5Portrait");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
			var path = GetPathToHtml(bookFolderPath);

			Assert.AreEqual("My Book.htm", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(bookFolderPath));
			Assert.IsTrue(File.Exists(path));
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryTemplate_SameNameAlreadyUsed_FindsUsableNumberSuffix()
		{
			Directory.CreateDirectory(_projectFolder.Combine("My Book"));
			Directory.CreateDirectory(_projectFolder.Combine("My Book1"));
			Directory.CreateDirectory(_projectFolder.Combine("My Book3"));

			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"A5Portrait");

			var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

			Assert.AreEqual("My Book2", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(path));
			Assert.IsTrue(File.Exists(Path.Combine(path, "My Book2.htm")));
		}

		private string GetShellBookFolder()
		{
			return
				GetShellBookFolder(
					@"<div class='-bloom-page' data-page='required' id='1'>
						_required_ The user will not be allowed to remove this page.
					  </div>
					<div class='-bloom-page' id='2'>
						_normal_ It would be ok for the user to remove this page.
					</div>

					<div class='-bloom-page'  data-page='extra' id='3'>
						_extra_
					</div>
					<div class='-bloom-page' testid='pageWithNoLanguageTags'>
						<p>
							<textarea>Text of a simple template</textarea>
						</p>
					</div>
					<div class='-bloom-page' testid='pageAlreadyHasVernacular'>
						 <p>
							<textarea lang='en'>This is some English</textarea>
							<textarea lang='xyz'>original</textarea>
						</p>
					</div>
					<div class='-bloom-page' testid='pageWithJustTokPisin'>
						 <p>
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
					<link rel='stylesheet' href='A5Portrait.css' type='text/css' />
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
