using System.IO;
using Bloom.TeamCollection;
using BloomTemp;
using BloomTests.DataBuilders;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
	public class MigrateAtStartupTests
	{
		protected TemporaryFolder _repoFolder;
		protected TemporaryFolder _collectionFolder;
		protected Mock<ITeamCollectionManager> _mockTcManager;
		protected FolderTeamCollection _collection;
		private TeamCollectionMessageLog _tcLog;

		[OneTimeSetUp]
		public void OneTimeSetup()
		{
			_repoFolder = new TemporaryFolder("MigrateAtStartup_Repo");
			_collectionFolder = new TemporaryFolder("MigrateAtStartup_Local");
			FolderTeamCollection.CreateTeamCollectionLinkFile(_collectionFolder.FolderPath,
				_repoFolder.FolderPath);
			_mockTcManager = new Mock<ITeamCollectionManager>();
			_tcLog = new TeamCollectionMessageLog(TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath));
			_collection = new FolderTeamCollection(_mockTcManager.Object, _collectionFolder.FolderPath, _repoFolder.FolderPath, tcLog:_tcLog);
			_collection.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
			TeamCollectionManager.ForceCurrentUserForTests("migrate@somewhere.org");

			// Simulate a book that needs nothing done to it. It's the same locally and on the repo.
			MakeBook("Keep me", "This needs nothing done to it");

			// Simlulate a book that is only in the team repo
			MakeBook("Add me", "Fetch to local", onlyRepo: true);

			// Simulate a book that is not checked out locally and has been modified elsewhere
			MakeBook("Update me", "Needs to be become this locally");
			UpdateLocalBook("Update me", "This is supposed to be an older value, not edited locally");

			// sut for the whole suite!
			// First, simulate having old names for the books in the repo.
			RobustFile.Move(Path.Combine(_collection.RepoFolderPath, "Books", "Keep me.bloomSource"), Path.Combine(_collection.RepoFolderPath, "Books", "Keep me.bloom"));
			RobustFile.Move(Path.Combine(_collection.RepoFolderPath, "Books", "Add me.bloomSource"), Path.Combine(_collection.RepoFolderPath, "Books", "Add me.bloom"));
			RobustFile.Move(Path.Combine(_collection.RepoFolderPath, "Books", "Update me.bloomSource"), Path.Combine(_collection.RepoFolderPath, "Books", "Update me.bloom"));
			Assert.That(_collection.MigrateDotBloomFiles(), Is.True, "MigrateDotBloomFiles() returns true");
		}

		protected virtual bool FirstTimeJoin()
		{
			return false;
		}

		[Test]
		public virtual void MigrateDotBloomFiles_RenameToDotBloomSource()
		{
			Assert.That(RobustFile.Exists(Path.Combine(_collection.RepoFolderPath, "Books", "Keep me.bloomSource")), Is.True, "Keep me.bloomSource exists in repo folder");
			Assert.That(RobustFile.Exists(Path.Combine(_collection.RepoFolderPath, "Books", "Add me.bloomSource")), Is.True, "Add me.bloomSource exists in repo folder");
			Assert.That(RobustFile.Exists(Path.Combine(_collection.RepoFolderPath, "Books", "Update me.bloomSource")), Is.True, "Update me.bloomSource exists in repo folder");
			Assert.That(RobustFile.Exists(Path.Combine(_collection.RepoFolderPath, "Books", "Keep me.bloom")), Is.False, "Keep me.bloomSource exists in repo folder");
			Assert.That(RobustFile.Exists(Path.Combine(_collection.RepoFolderPath, "Books", "Add me.bloom")), Is.False, "Add me.bloomSource exists in repo folder");
			Assert.That(RobustFile.Exists(Path.Combine(_collection.RepoFolderPath, "Books", "Update me.bloom")), Is.False, "Update me.bloomSource exists in repo folder");
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_collectionFolder.Dispose();
			_repoFolder.Dispose();
			TeamCollectionManager.ForceCurrentUserForTests(null);
		}

		void MakeBook(string name, string content, bool toRepo = true, bool onlyRepo = false)
		{
			var folderPath = MakeFakeBook(_collectionFolder.FolderPath, name, content);
			if (toRepo)
				_collection.PutBook(folderPath);
			if (onlyRepo)
				SIL.IO.RobustIO.DeleteDirectoryAndContents(folderPath);
		}

		// Make a very trivial fake book. Not nearly good enough to make a Book object from,
		// but enough for most purposes of testing TeamCollection.
		public static string MakeFakeBook(string collectionFolder, string name, string content, string folderNameIfDifferent = null)
		{
			var bookBuilder = new BookFolderBuilder()
				.WithRootFolder(collectionFolder)
				.WithBookFolderName(folderNameIfDifferent)
				.WithTitle(name)
				.WithHtm("<html><body>" + content + "</body></html>")
				.Build();

			return bookBuilder.BuiltBookFolderPath;
		}

		void UpdateLocalBook(string name, string content, bool updateChecksum = true)
		{
			var folderPath = Path.Combine(_collectionFolder.FolderPath, name);
			var bookPath = Path.Combine(folderPath, Path.ChangeExtension(name, "htm"));
			RobustFile.WriteAllText(bookPath, "<html><body>" + content + "</body></html>");
			if (updateChecksum)
			{
				var status = _collection.GetLocalStatus(name);
				status.checksum = Bloom.TeamCollection.TeamCollection.MakeChecksum(folderPath);
				_collection.WriteLocalStatus(name, status);
			}
		}
	}
}
