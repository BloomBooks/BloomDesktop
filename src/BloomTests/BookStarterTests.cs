using System.IO;
using System.Xml;
using Bloom;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;


namespace BloomTests
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
			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "A5Portrait")
											});
			_starter = new BookStarter(dir => new BookStorage(dir, _fileLocator), new LanguageSettings("xyz", new string[0]));
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

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_CreatesWithCoverAndTitle()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"A5Portrait");

			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));

			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'cover')]", 1);
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'titlePage')]", 1);

			//should only get these two pages
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(@class, 'page')]", 2);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5Portrait_CreatesWithCorrectStylesheets()
		{
				 var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates", "A5Portrait");

				 var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path));
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'A5Portrait')]", 1);
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'basePage')]", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_PagesLabeledExtraAreNotAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
		   AssertThatXmlIn.File(path).HasNoMatchForXpath("//div[contains(text(), '_extra_')]");
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_HasEnglishTextArea_VernacularTextAreaAdded()
		{
			var body = @"<div class='page'>
						<p>
						 <textarea lang='en' id='text1' class='hideMe'>This is some English</textarea>
						</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			//nb: testid is used rather than id because id is replaced with a guid when the copy is made

			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='en']", 1);
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
			//the new text should also have been emptied of English
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz' and not(text())]", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_HasTokPisinTextAreaSurroundedByParagraph_VernacularTextAreaAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='tpi']", 1);
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='xyz']", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_HasTokPisinTextArea_StyleAddedToHide()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithJustTokPisin']/p/textarea[@lang='tpi' and contains(@class,'hideMe')]", 1);
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_ExistingEnglishHasHideClass_NewVernacularHasNoClass()
		{
			var body = @"<div class='page'>
<p>
						<textarea lang='en' class='hideMe'>blah</textarea>
</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='xyz' and not(@class)]", 1);
		}


		/// <summary>
		/// NB: It's not clear what the behavior should eventually be... how do we know it isn't supposed to be in english?
		/// But for now, this gives us the behavior we want on the title page
		/// </summary>
		[Test]
		public void CreateBookOnDiskFromTemplate_HasEnglishParagraph_ConvertsToVernacular()//??????????????
		{
			var body = @"<div class='page'>
						<p id='bookTitle' lang='en' class='_vernacularBookTitle'>Book Title</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//p[@lang='xyz']", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_NationalLanguageField_LeavesUntouched()
		{
			var body = @"<div class='page' testid='pageWithNoLanguageTags'>
						<p>
							<textarea lang='en' id='text1' class='showNational'>LanguageName</textarea>
						</p>
					</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//textarea", 1);
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//textarea[@lang='en']", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_AlreadyHasVernacular_LeavesUntouched()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='en']", 1);
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz']", 1);
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageAlreadyHasVernacular']/p/textarea[@lang='xyz' and text()='original']", 1);
		}
		[Test]
		public void CreateBookOnDiskFromTemplate_Has2SourceLanguagesTextArea_OneVernacularTextAreaAdded()
		{
			var body = @"<div class='page'>
							<p>
								<textarea lang='en'  class='text'> When you plant a garden you always make a fence.</textarea>
								<textarea lang='tpi' class='text'> Taim yu planim gaden yu save wokim banis.</textarea>
							</p>
						</div>";
			string sourceTemplateFolder = GetShellBookFolder(body);
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div/p/textarea[@lang='xyz']", 1);
		}

	   [Test]
		public void CreateBookOnDiskFromTemplate_TextAreaHasNoText_VernacularLangAttrSet()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithNoLanguageTags']/p/textarea", 1);
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[@testid='pageWithNoLanguageTags']/p/textarea[@lang='xyz']", 1);
		}
		[Test]
		public void CreateBookOnDiskFromTemplate_PagesNotLabeledExtraAreAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(text(), '_normal_')]", 1);
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_PagesLabeledRequiredIsAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
			AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//div[contains(text(), '_required_')]", 1);
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
			Assert.AreEqual("guitar1", Path.GetFileName(secondPath));
			Assert.IsTrue(File.Exists(Path.Combine(secondPath, "guitar1.htm")));


			Assert.IsTrue(Directory.Exists(firstPath));
			Assert.IsTrue(File.Exists(Path.Combine(firstPath, "guitar.htm")));

			Assert.IsTrue(Directory.Exists(secondPath),"it clobbered the first one!");
		}

		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryA5_GetsExpectedName()
		{
			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"A5Portrait");

			string bookFolderPath = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
			var path = GetPathToHtml(bookFolderPath);

			Assert.AreEqual("book.htm", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(bookFolderPath));
			Assert.IsTrue(File.Exists(path));
		}


		[Test]
		public void CreateBookOnDiskFromTemplate_FromFactoryTemplate_SameNameAlreadyUsed_FindsUsableNumberSuffix()
		{
			Directory.CreateDirectory(_projectFolder.Combine("book"));
			Directory.CreateDirectory(_projectFolder.Combine("book1"));
			Directory.CreateDirectory(_projectFolder.Combine("book3"));

			var source = FileLocator.GetDirectoryDistributedWithApplication("factoryCollections", "Templates",
																			"A5Portrait");

			var path = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);

			Assert.AreEqual("book2", Path.GetFileName(path));
			Assert.IsTrue(Directory.Exists(path));
			Assert.IsTrue(File.Exists(Path.Combine(path, "book2.htm")));
		}

		private string GetShellBookFolder()
		{
			return
				GetShellBookFolder(
					@"<div class='page required' id='1'>
						_required_ The user will not be allowed to remove this page.
					  </div>
					<div class='page' id='2'>
						_normal_ It would be ok for the user to remove this page.
					</div>

					<div class='page extraPage' id='3'>
						_extra_
					</div>
					<div class='page' testid='pageWithNoLanguageTags'>
						<p>
							<textarea id='text1' class='text'>Text of a simple template</textarea>
						</p>
					</div>
					<div class='page' testid='pageAlreadyHasVernacular'>
						 <p>
							<textarea lang='en' id='text1' class='text'>This is some English</textarea>
							<textarea lang='xyz' id='text1' class='text'>original</textarea>
						</p>
					</div>
					<div class='page' testid='pageWithJustTokPisin'>
						 <p>
							<textarea lang='tpi' id='text1' class='text'> Taim yu planim gaden yu save wokim banis.</textarea>
						</p>
					</div>");
		}
		private string GetShellBookFolder(string bodyContents)
		{
			var content =
				@"<?xml version='1.0' encoding='utf-8' ?>
				<html xmlns='http://www.w3.org/1999/xhtml'>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='A5Portrait.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />
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
