using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Moq;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.Book
{
	[TestFixture]
	public class BookStorageTests
	{
		private FileLocator _fileLocator;
		private TemporaryFolder _fixtureFolder;
		private TemporaryFolder _folder;
		private string _bookPath;

		[SetUp]
		public void Setup()
		{
			ErrorReport.IsOkToInteractWithUser = false;
			_fileLocator = new FileLocator(new string[]
											{
												//FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book"),
												BloomFileLocator.GetInstalledXMatterDirectory()
											});
			_fixtureFolder = new TemporaryFolder("BloomBookStorageTest");
			_folder = new TemporaryFolder(_fixtureFolder, "theBook");

			_bookPath = _folder.Combine("theBook.htm");
		}

		[TearDown]
		public void TearDown()
		{
			_fixtureFolder.Dispose();
		}

		[Test]
		public void Save_BookHadOnlyPaperSizeStyleSheet_StillHasIt()
		{
			GetInitialStorageWithCustomHtml("<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
			AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'Basic Book')]", 1);
		}
		[Test]
		public void Save_HasEmptyParagraphs_RetainsEmptyParagraphs()
		{
			var pattern = "<p></p><p></p><p>a</p><p></p><p>b</p>";
			GetInitialStorageWithCustomHtml("<html><body><div class='bloom-page'><div class='bloom-translationGroup'><div class='bloom-editable'>" +
				pattern +
				"</div></div></div></body></html>");
			AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//p", 5);
		}
		[Test]
		public void Save_BookHadEditStyleSheet_NowHasPreviewAndBase()
		{
			GetInitialStorageWithCustomHtml("<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
			AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'basePage')]", 1);
			AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
		}


		[Test]
		public  void CleanupUnusedImageFiles_BookHadUnusedImages_ImagesRemoved()
		{
			var storage =
				GetInitialStorageWithCustomHtml(
					"<html><body><div class='bloom-page'><div class='marginBox'>" +
					"<div style='background-image:url(\"keepme.png\")'></div>" +
					"<img src='keepme2.png'></img>" +
					"</div></div></body></html>");
			var keepName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "KeEpMe.pNg" : "keepme.png";
			var keepNameImg = Environment.OSVersion.Platform == PlatformID.Win32NT ? "KeEpMe2.pNg" : "keepme2.png";
			var keepTempDiv = MakeSamplePngImage(Path.Combine(_folder.Path, keepName));
            var keepTempImg = MakeSamplePngImage(Path.Combine(_folder.Path, keepNameImg));
			var dropmeTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "dropme.png"));
			storage.CleanupUnusedImageFiles();
			Assert.IsTrue(File.Exists(keepTempDiv.Path));
			Assert.IsTrue(File.Exists(keepTempImg.Path));
			Assert.IsFalse(File.Exists(dropmeTemp.Path));
		}

		[Test]
		public void CleanupUnusedImageFiles_ImageHasQuery_ImagesNotRemoved()
		{
			var storage =
				GetInitialStorageWithCustomHtml(
					"<html><body><div class='bloom-page'><div class='marginBox'>" +
					"<img src='keepme.png?1234'></img>" +
					"</div></div></body></html>");
			var keepTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "keepme.png"));
			storage.CleanupUnusedImageFiles();
			Assert.IsTrue(File.Exists(keepTemp.Path));
		}
		[Test]
		public void CleanupUnusedImageFiles_ImageOnlyReferencedInDataDiv_ImageNotRemoved()
		{
			 var storage =
				GetInitialStorageWithCustomHtml(
					"<html><body>"+
					"<div id ='bloomDataDiv'><div data-book='coverImage'>keepme.png</div>"+
					"<div data-book='coverImage'> keepme.jpg </div></div>" +
					"<div class='bloom-page'><div class='marginBox'>" +
					"</div></div></body></html>");
			var keepTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "keepme.png"));
			var keepTempJPG = MakeSamplePngImage(Path.Combine(_folder.Path, "keepme.jpg"));
			storage.CleanupUnusedImageFiles();
			Assert.IsTrue(File.Exists(keepTemp.Path));
			Assert.IsTrue(File.Exists(keepTempJPG.Path));
		}
		[Test]
		public void CleanupUnusedImageFiles_ThumbnailsAndPlaceholdersNotRemoved()
		{
			var storage =
				GetInitialStorageWithCustomHtml(
					"<html><body><div class='bloom-page'><div class='marginBox'>" +
					"</div></div></body></html>");
			var p1 = MakeSamplePngImage(Path.Combine(_folder.Path, "thumbnail.png"));
			var p2 = MakeSamplePngImage(Path.Combine(_folder.Path, "thumbnail88.png"));
			var p3 = MakeSamplePngImage(Path.Combine(_folder.Path, "placeholder.png"));
			var dropmeTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "dropme.png"));
			storage.CleanupUnusedImageFiles();
			Assert.IsTrue(File.Exists(p1.Path));
			Assert.IsTrue(File.Exists(p2.Path));
			Assert.IsTrue(File.Exists(p3.Path));
			Assert.IsFalse(File.Exists(dropmeTemp.Path));
		}
		[Test]
		public void CleanupUnusedImageFiles_UnusedImageIsLocked_NotException()
		{
			var storage = GetInitialStorageWithCustomHtml("<html><body><div class='bloom-page'><div class='marginBox'></div></body></html>");
			var dropmeTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "dropme.png"));
			//make it undelete-able
			using (Image.FromFile(dropmeTemp.Path))
			{
				storage.CleanupUnusedImageFiles();
			}
		}
		[Test]
		public void Save_BookHasMissingImages_NoCrash()
		{
			var storage = GetInitialStorageWithCustomHtml("<html><body><div class='bloom-page'><div class='marginBox'><img src='keepme.png'></img></div></div></body></html>");
			storage.Save();
		}
		private TempFile MakeSamplePngImage(string name)
		{
			var temp = TempFile.WithFilename(name);
			var x = new Bitmap(10, 10);
			x.Save(temp.Path, ImageFormat.Png);
			x.Dispose();
			return temp;
		}
		//
		//        [Test]
		//        public void Delete_IsDeleted()
		//        {
		//            BookStorage storage = GetInitialStorageWithCustomHtml();
		//            Assert.IsTrue(Directory.Exists(_folder.Path));
		//            Assert.IsTrue(storage.DeleteBook());
		//            Thread.Sleep(2000);
		//            Assert.IsFalse(Directory.Exists(_folder.Path));
		//        }

		[Test]
		[Platform(Exclude = "Linux", Reason = "UNC paths for network drives are only used on Windows")]
		public void Save_PathIsUNCRatherThanDriveLetter()
		{
			var storage = GetInitialStorageUsingUNCPath();
			storage.Save();
		}

		private BookStorage GetInitialStorageUsingUNCPath()
		{
			var testFolder = new TemporaryFolder();
			var bookPath = testFolder.Combine("theBook.htm");
			File.WriteAllText(bookPath,
				"<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
			var collectionSettings = new CollectionSettings(Path.Combine(testFolder.Path, "test.bloomCollection"));
			var folderPath = ConvertToNetworkPath(testFolder.Path);
			Debug.WriteLine(Path.GetPathRoot(folderPath));
			var storage = new BookStorage(folderPath, _fileLocator, new BookRenamedEvent(), collectionSettings);
			return storage;
		}

		private string ConvertToNetworkPath(string drivePath)
		{
			string driveLetter = Directory.GetDirectoryRoot(drivePath);
			return drivePath.Replace(driveLetter, "//localhost/" + driveLetter.Replace(":\\", "") + "$/");
		}

		private BookStorage GetInitialStorageWithCustomHtml(string html)
		{
			RobustFile.WriteAllText(_bookPath, html);
			var projectFolder = new TemporaryFolder("BookStorageTests_ProjectCollection");
			var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));
			var storage = new BookStorage(_folder.Path, _fileLocator, new BookRenamedEvent(), collectionSettings);
			storage.Save();
			return storage;
		}

		private BookStorage GetInitialStorage()
		{
			return GetInitialStorageWithCustomHtml("<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
		}

		private BookStorage GetInitialStorageWithCustomHead(string head)
		{
			File.WriteAllText(_bookPath, "<html><head>" + head + " </head></body></html>");
			var storage = new BookStorage(_folder.Path, _fileLocator, new BookRenamedEvent(), new CollectionSettings());
			storage.Save();
			return storage;
		}

		private BookStorage GetInitialStorageWithDifferentFileName(string bookName)
		{
			var bookPath = _folder.Combine(bookName + ".htm");
			File.WriteAllText(bookPath, "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
			var projectFolder = new TemporaryFolder("BookStorageTests_ProjectCollection");
			var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));
			var storage = new BookStorage(_folder.Path, _fileLocator, new BookRenamedEvent(), collectionSettings);
			storage.Save();
			return storage;
		}

		[Test]
		public void SetBookName_EasyCase_ChangesFolderAndFileName()
		{
			var storage = GetInitialStorage();
			using (var newFolder = new TemporaryFolder(_fixtureFolder, "newName"))
			{
				Directory.Delete(newFolder.Path);
				ChangeNameAndCheck(newFolder, storage);
			}
		}

		[Test]
		public void SetBookName_FolderWithNameAlreadyExists_AddsANumberToName()
		{
			using (var original = new TemporaryFolder(_folder, "original"))
			using (var x = new TemporaryFolder(_folder, "foo"))
			using (var y = new TemporaryFolder(_folder, "foo1"))
			using (var z = new TemporaryFolder(_folder, "foo2"))
			{
				File.WriteAllText(Path.Combine(original.Path, "original.htm"), "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");

				var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection");
				var collectionSettings = new CollectionSettings(Path.Combine(projectFolder.Path, "test.bloomCollection"));
				var storage = new BookStorage(original.Path, _fileLocator, new BookRenamedEvent(), collectionSettings);
				storage.Save();

				Directory.Delete(z.Path);
				//so, we ask for "foo", but should get "foo2", because there is already a foo and foo1
				var newBookName = Path.GetFileName(x.Path);
				storage.SetBookName(newBookName);
				var newPath = z.Combine("foo2.htm");
				Assert.IsTrue(Directory.Exists(z.Path), "Expected folder:" + z.Path);
				Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
			}
		}

		[Test]
		public void SetBookName_FolderNameWasDifferentThanFileName_ChangesFolderAndFileName()
		{
			var storage = GetInitialStorageWithDifferentFileName("foo");
			using (var newFolder = new TemporaryFolder(_fixtureFolder, "newName"))
			{
				Directory.Delete(newFolder.Path);
				ChangeNameAndCheck(newFolder, storage);
			}
		}

		[Test]
		public void SetBookName_NameIsNotValidFileName_UsesSanitizedName()
		{
			var storage = GetInitialStorage();
			storage.SetBookName("/b?loom*test/");
			Assert.IsTrue(Directory.Exists(_fixtureFolder.Combine("b loom test")));
			Assert.IsTrue(File.Exists(_fixtureFolder.Combine("b loom test", "b loom test.htm")));
		}

		[Test]
		public void SetBookName_NameHasTrailingPeriods_UsesSanitizedName()
		{
			var storage = GetInitialStorage();
			storage.SetBookName("Whenever...");
			Assert.IsTrue(Directory.Exists(_fixtureFolder.Combine("Whenever")));
			Assert.IsTrue(File.Exists(_fixtureFolder.Combine("Whenever", "Whenever.htm")));
			Assert.That(Path.GetFileName(storage.FolderPath), Is.EqualTo("Whenever"));
		}

		/// <summary>
		/// regression test
		/// </summary>
		[Test]
		[Platform(Exclude = "Linux", Reason = "UNC paths for network drives are only used on Windows")]
		public void SetBookName_PathIsAUNCToLocalHost_NoErrors()
		{
			var storage = GetInitialStorageUsingUNCPath();
			var path = storage.FolderPath;
			var newName = Guid.NewGuid().ToString();
			path = path.Replace(Path.GetFileName(path), newName);
			storage.SetBookName(newName);

			Assert.IsTrue(Directory.Exists(path));
			Assert.IsTrue(File.Exists(Path.Combine(path, newName + ".htm")));
		}

		[Test]
		public void PathToExistingHtml_WorksWithFullHtmlName()
		{
			var filenameOnly = "BigBook";
			var fullFilename = "BigBook.html";
			var storage = GetInitialStorageWithDifferentFileName(filenameOnly);
			var oldFullPath = Path.Combine(storage.FolderPath, filenameOnly + ".htm");
			var newFullPath = Path.Combine(storage.FolderPath, fullFilename);
			File.Move(oldFullPath, newFullPath); // rename to .html
			var path = storage.PathToExistingHtml;
			Assert.AreEqual(fullFilename, Path.GetFileName(path), "If this fails, 'path' will be empty string.");
		}

		/// <summary>
		/// This is really testing some Book.cs functionality, but it has to manipulate real files with a real storage,
		/// so it seems to fit better here.
		/// </summary>
		[Test]
		public void BringBookUpToDate_ConvertsTagsToJsonWithExpectedDefaults()
		{
			var storage = GetInitialStorage();
			var locator = (FileLocator) storage.GetFileLocator();
			string root = FileLocator.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot);
			locator.AddPath(root.CombineForPath("bookLayout"));
			var folder = storage.FolderPath;
			var tagsPath = Path.Combine(folder, "tags.txt");
			File.WriteAllText(tagsPath, "suitableForMakingShells\nexperimental\nfolio\n");
			var collectionSettings =
				new CollectionSettings(new NewCollectionSettings()
				{
					PathToSettingsFile = CollectionSettings.GetPathForNewSettings(folder, "test"),
					Language1Iso639Code = "xyz",
					Language2Iso639Code = "en",
					Language3Iso639Code = "fr"
				});
			var book = new Bloom.Book.Book(new BookInfo(folder, true), storage, new Mock<ITemplateFinder>().Object,
				collectionSettings,
				new Mock<PageSelection>().Object, new PageListChangedEvent(), new BookRefreshEvent());

			book.BringBookUpToDate(new NullProgress());

			Assert.That(!File.Exists(tagsPath), "The tags.txt file should have been removed");
			// BL-2163, we are no longer migrating suitableForMakingShells
			Assert.That(storage.MetaData.IsSuitableForMakingShells, Is.False);
			Assert.That(storage.MetaData.IsFolio, Is.True);
			Assert.That(storage.MetaData.IsExperimental, Is.True);
			Assert.That(storage.MetaData.BookletMakingIsAppropriate, Is.True);
			Assert.That(storage.MetaData.AllowUploading, Is.True);
		}


		[Test]
		public void BringBookUpToDate_MigratesReaderToolsAvailableToToolboxIsOpen()
		{
			var oldMetaData =
				"{\"bookInstanceId\":\"3328aa4a - 2ef3 - 43a8 - a656 - 1d7c6f00444c\",\"folio\":false,\"title\":\"Landscape basic book\",\"baseUrl\":null,\"bookOrder\":null,\"isbn\":\"\",\"bookLineage\":\"056B6F11-4A6C-4942-B2BC-8861E62B03B3\",\"downloadSource\":null,\"license\":\"cc-by\",\"formatVersion\":\"2.0\",\"licenseNotes\":null,\"copyright\":null,\"authors\":null,\"credits\":\"\",\"tags\":[\"<p>\r\n</p>\"],\"pageCount\":0,\"languages\":[],\"langPointers\":null,\"summary\":null,\"allowUploadingToBloomLibrary\":true,\"bookletMakingIsAppropriate\":true,\"uploader\":null,\"tools\":null,\"readerToolsAvailable\":true}";
			var storage = GetInitialStorage();

			// This seems to be needed to let it locate some kind of collection settings.
			var folder = storage.FolderPath;
			var locator = (FileLocator)storage.GetFileLocator();
			string root = FileLocator.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot);

			locator.AddPath(root.CombineForPath("bookLayout"));
			var collectionSettings =
				new CollectionSettings(new NewCollectionSettings()
				{
					PathToSettingsFile = CollectionSettings.GetPathForNewSettings(folder, "test"),
					Language1Iso639Code = "xyz",
					Language2Iso639Code = "en",
					Language3Iso639Code = "fr"
				});
			var book = new Bloom.Book.Book(new BookInfo(folder, true), storage, new Mock<ITemplateFinder>().Object,
				collectionSettings,
				new Mock<PageSelection>().Object, new PageListChangedEvent(), new BookRefreshEvent());
			var jsonPath = book.BookInfo.MetaDataPath;
			File.WriteAllText(jsonPath, oldMetaData);

			book.BringBookUpToDate(new NullProgress());

			Assert.That(book.BookInfo.ToolboxIsOpen, Is.True);
		}

		[Test]
		public void Save_SetsJsonFormatVersion()
		{
			var storage = GetInitialStorage();
			Assert.That(storage.MetaData.FormatVersion, Is.EqualTo(BookStorage.kBloomFormatVersion));
		}

		private void ChangeNameAndCheck(TemporaryFolder newFolder, BookStorage storage)
		{
			var newBookName = Path.GetFileName(newFolder.Path);
			storage.SetBookName(newBookName);
			var newPath = newFolder.Combine(newBookName + ".htm");
			Assert.IsTrue(Directory.Exists(newFolder.Path), "Expected folder:" + newFolder.Path);
			Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
		}
	}
}