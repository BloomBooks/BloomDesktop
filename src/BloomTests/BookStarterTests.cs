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
			_starter = new BookStarter(dir => new BookStorage(dir, _fileLocator));
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
				//AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
				AssertThatXmlIn.File(path).HasSpecifiedNumberOfMatchesForXpath("//link", 1);
		 }


		[Test]
		public void CreateBookOnDiskFromTemplate_PagesLabeledExtraAreNotAdded()
		{
			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(GetShellBookFolder(), _projectFolder.Path));
		   AssertThatXmlIn.File(path).HasNoMatchForXpath("//div[contains(text(), '_extra_')]");
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
			var content = @"<?xml version='1.0' encoding='utf-8' ?>
				<html xmlns='http://www.w3.org/1999/xhtml'>
				<head>
					<meta content='text/html; charset=utf-8' http-equiv='content-type' />
					<title>Test Shell</title>
					<link rel='stylesheet' href='A5Portrait.css' type='text/css' />
					<link rel='stylesheet' href='../../previewMode.css' type='text/css' />
				</head>
				<body class='a5Portrait'>
					  <div class='page required' id='1'>
						_required_ The user will not be allowed to remove this page.
					  </div>
					<div class='page' id='2'>
						_normal_ It would be ok for the user to remove this page.
					</div>
					<div class='page extraPage' id='3'>
						_extra_
					</div>
				</body>
				</html>
		";
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
