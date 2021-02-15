using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using NUnit.Framework.Internal;
using SIL.IO;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace BloomTests.TeamCollection
{
	// While this makes considerable use of FolderTeamCollection, the tests here are focused on the code in the TeamCollection class.
	// Some of the code in TeamCollection is more easily tested by methods in FolderTeamCollectionTests. Most of the tests
	// here focus on SyncAtStartup.
	// Note: in a very early version, TeamCollection was called TeamRepo. While we've mostly gotten rid of the 'repo'
	// name, it was extensively used in test comments to indicate "the version of X in the shared location". I don't have
	// a better short name for that so for now I have kept it.
	public class TeamCollectionTests
	{

		private TemporaryFolder _sharedFolder;
		private TemporaryFolder _collectionFolder;
		private FolderTeamCollection _collection;

		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			_sharedFolder = new TemporaryFolder("TeamCollection_Shared");
			_collectionFolder = new TemporaryFolder("TeamCollection_Local");
			FolderTeamCollection.CreateTeamCollectionSettingsFile(_collectionFolder.FolderPath,
				_sharedFolder.FolderPath);
			_collection = new FolderTeamCollection(_collectionFolder.FolderPath, _sharedFolder.FolderPath);
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_collectionFolder.Dispose();
			_sharedFolder.Dispose();
		}

		[Test]
		public void HandleNewBook_CopiesBookAndShaToLocal()
		{
			var bookFolderPath = Path.Combine(_collectionFolder.FolderPath, "My book");
			Directory.CreateDirectory(bookFolderPath);
			var bookPath = Path.Combine(bookFolderPath, "My book.htm");
			RobustFile.WriteAllText(bookPath, "This is just a dummy");
			_collection.PutBook(bookFolderPath);
			SIL.IO.RobustIO.DeleteDirectoryAndContents(bookFolderPath);

			_collection.HandleNewBook(new NewBookEventArgs(){BookName="My book.bloom"});

			var destBookFolder = Path.Combine(_collectionFolder.FolderPath, "My book");
			var destBookPath = Path.Combine(destBookFolder, "My book.htm");
			Assert.That(File.ReadAllText(destBookPath), Is.EqualTo("This is just a dummy"));
			//AssertChecksumsMatch(_collectionFolder.FolderPath, "My book");
		}

		//void AssertChecksumsMatch(string destFolder, string bookName)
		//{
		//	var checksumFileName = Path.ChangeExtension(bookName, "checksum");
		//	var path1 = Path.Combine(_sharedFolder.FolderPath, checksumFileName);
		//	var path2 = Path.Combine(destFolder, checksumFileName);
		//	Assert.That(File.Exists(path1));
		//	Assert.That(File.Exists(path2));
		//	Assert.That(File.ReadAllBytes(path1), Is.EqualTo(File.ReadAllBytes(path2)));
		//}
	}
}
