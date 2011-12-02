using System.IO;
using System.Threading;
using Bloom.Book;
using NUnit.Framework;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.TestUtilities;

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
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections"),
												FileLocator.GetDirectoryDistributedWithApplication( "factoryCollections", "Templates", "A5Portrait")
											});
			_fixtureFolder = new TemporaryFolder("BloomBookStorageTest");
			_folder = new TemporaryFolder(_fixtureFolder,"theBook");

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
			File.WriteAllText(_bookPath, "<html><head><link rel='stylesheet' href='A5Portrait.css' type='text/css' /></head><body><div class='-bloom-page'></div></body></html>");
			var storage = new BookStorage(_folder.FolderPath, _fileLocator);
			storage.Save();
			 AssertThatXmlIn.File(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'A5Portrait')]", 1);
		}

		[Test]
		public void Save_BookHadEditStyleSheet_NowHasPreviewAndBase()
		{
			File.WriteAllText(_bookPath, "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='-bloom-page'></div></body></html>");
			var storage = new BookStorage(_folder.FolderPath, _fileLocator);
			storage.Save();
			AssertThatXmlIn.File(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'basePage')]", 1);
			AssertThatXmlIn.File(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'preview')]", 1);
		}


		[Test]
		public void Delete_IsDeleted()
		{
			BookStorage storage = GetInitialStorage();
			Assert.IsTrue(Directory.Exists(_folder.Path));
			Assert.IsTrue(storage.DeleteBook());
			Thread.Sleep(2000);
			Assert.IsFalse(Directory.Exists(_folder.Path));
		}

		private BookStorage GetInitialStorage()
		{
			File.WriteAllText(_bookPath, "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='-bloom-page'></div></body></html>");
			var storage = new BookStorage(_folder.Path, _fileLocator);
			storage.Save();
			return storage;
		}

		private BookStorage GetInitialStorageWithDifferentFileName(string bookName)
		{
			var bookPath = _folder.Combine(bookName + ".htm");
			File.WriteAllText(bookPath, "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='-bloom-page'></div></body></html>");
			var storage = new BookStorage(_folder.Path, _fileLocator);
			storage.Save();
			return storage;
		}


		[Test]
		public void SetBookName_EasyCase_ChangesFolderAndFileName()
		{
		   var storage = GetInitialStorage();
		   using (var newFolder = new TemporaryFolder(_fixtureFolder,"newName"))
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
				File.WriteAllText(Path.Combine(original.Path, "original.htm"), "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='-bloom-page'></div></body></html>");
			var storage = new BookStorage(original.Path, _fileLocator);
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
			using (var newFolder = new TemporaryFolder(_fixtureFolder,"newName"))
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


		private void ChangeNameAndCheck(TemporaryFolder newFolder, BookStorage storage)
		{
			var newBookName = Path.GetFileName(newFolder.Path);
			storage.SetBookName(newBookName);
			var newPath = newFolder.Combine(newBookName+".htm");
			Assert.IsTrue(Directory.Exists(newFolder.Path), "Expected folder:" + newFolder.Path);
			Assert.IsTrue(File.Exists(newPath), "Expected file:" +newPath);
		}
	}
}
