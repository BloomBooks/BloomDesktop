using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
	public class TeamCollectionManagerTests
	{
		[Test]
		public void TeamCollectionCreatedFromSettingsFile_HasCorrectId()
		{
			using (var localCollectionFolder = new TemporaryFolder("TeamCollectionCreatedFromSettingsFile_HasCorrectId"))
			{
				using (var repoFolder = new TemporaryFolder("TeamCollectionCreatedFromSettingsFile_HasCorrectId_repo"))
				{
					var collectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
					FolderTeamCollection.CreateTeamCollectionSettingsFile(localCollectionFolder.FolderPath,
						repoFolder.FolderPath, collectionId);
					var manager = new TeamCollectionManager(Path.Combine(localCollectionFolder.FolderPath, "CollectionSettings.bloomCollection"),
						null, new BookRenamedEvent(), null);
					var collection = manager.CurrentCollection;
					Assert.That(collection, Is.Not.Null);
					Assert.That(collection.CollectionId, Is.EqualTo(collectionId));
				}
			}
		}

		[Test]
		public void TeamCollectionCreatedFromSettingsFile_NoIdInFile_Repairs()
		{
			using (var localCollectionFolder = new TemporaryFolder("TeamCollectionCreatedFromSettingsFile_HasCorrectId"))
			{
				using (var repoFolder = new TemporaryFolder("TeamCollectionCreatedFromSettingsFile_HasCorrectId_repo"))
				{
					
					// deliberately create one without an ID in the file.
					FolderTeamCollection.CreateTeamCollectionSettingsFile(localCollectionFolder.FolderPath,
						repoFolder.FolderPath, null);
					// It also has a book with a status that has no collection ID
					SyncAtStartupTests.MakeFakeBook(localCollectionFolder.FolderPath, "Some book",
						"Content of some book");
					var statusFilePath =
						Bloom.TeamCollection.TeamCollection.GetStatusFilePath("Some book",
							localCollectionFolder.FolderPath);
					RobustFile.WriteAllText(statusFilePath, new BookStatus().ToJson(), Encoding.UTF8);

					var manager = new TeamCollectionManager(Path.Combine(localCollectionFolder.FolderPath, "CollectionSettings.bloomCollection"),
						null, new BookRenamedEvent(), null);
					var collection = manager.CurrentCollection;
					Assert.That(collection, Is.Not.Null);
					var collectionId = collection.CollectionId;
					Assert.That(collectionId, Is.Not.Null.And.Not.Empty);

					// But, did the SAME ID get recorded in the file?
					manager = new TeamCollectionManager(Path.Combine(localCollectionFolder.FolderPath, "CollectionSettings.bloomCollection"),
						null, new BookRenamedEvent(), null);
					collection = manager.CurrentCollection;
					Assert.That(collection, Is.Not.Null);
					Assert.That(collection.CollectionId, Is.EqualTo(collectionId));

					// And, the existing book should have been fixed, too
					Assert.That(collection.GetLocalStatus("Some book").collectionId, Is.EqualTo(collectionId));
				}
			}
		}
	}
}
