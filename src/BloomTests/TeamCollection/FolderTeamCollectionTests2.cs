using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
	/// <summary>
	/// The main FolderTeamCollectionTests class is mostly made up of tests that benefit from a OneTimeSetup
	/// function that creates a collection already connected to local and TeamCollection folders that
	/// already have some content created in the OneTimeSetup. This class is for tests
	/// where that setup gets in the way.
	/// </summary>
	public class FolderTeamCollectionTests2
	{
		[Test]
		public void ConnectToTeamCollection_SetsUpRequiredFiles()
		{
			using (var collectionFolder = new TemporaryFolder("FolderTeamCollectionTests2_Collection")) {
				using (var sharedFolder = new TemporaryFolder("FolderTeamCollectionTests2_Shared"))
				{
					TeamCollectionTests.MakeFakeBook(collectionFolder.FolderPath, "Some book", "Something");
					var settingsFileName = Path.ChangeExtension(Path.GetFileName(collectionFolder.FolderPath), "bloomCollection");
					var settingsPath = Path.Combine(collectionFolder.FolderPath, settingsFileName);

					// As an aside, this is a convenient place to check that a TC manager created when TC settings does not exist
					// functions and does not have a current collection.
					var tcManager = new TeamCollectionManager(settingsPath, null);
					Assert.That(tcManager.CurrentCollection, Is.Null);

					RobustFile.WriteAllText(settingsPath, "This is a fake settings file");
					FolderTeamCollection.CreateTeamCollectionSettingsFile(collectionFolder.FolderPath, sharedFolder.FolderPath);

					var nonBookFolder = Path.Combine(collectionFolder.FolderPath, "Some other folder");
					Directory.CreateDirectory(nonBookFolder);
					tcManager = new TeamCollectionManager(settingsPath, null);
					var collection = tcManager.CurrentCollection;

					// sut
					(collection as FolderTeamCollection)?.ConnectToTeamCollection(sharedFolder.FolderPath);

					Assert.That(collection, Is.Not.Null);
					var joinCollectioPath =
						Path.Combine(sharedFolder.FolderPath, "Join this Team Collection.JoinBloomTC");
					Assert.That(File.Exists(joinCollectioPath));

					var teamCollectionSettingsPath =
						Path.Combine(collectionFolder.FolderPath, TeamCollectionManager.TeamCollectionSettingsFileName);
					Assert.That(File.Exists(teamCollectionSettingsPath));
					Assert.That(RobustFile.ReadAllText(teamCollectionSettingsPath), Contains.Substring("<TeamCollectionFolder>" + sharedFolder.FolderPath+"</TeamCollectionFolder>"));
					var sharedSettingsPath = Path.Combine(collectionFolder.FolderPath, settingsFileName);
					Assert.That(RobustFile.ReadAllText(sharedSettingsPath), Is.EqualTo("This is a fake settings file"));
					var bookPath = Path.Combine(sharedFolder.FolderPath, "Some book.bloom");
					Assert.That(File.Exists(bookPath));

				}
			}

		}
	}
}
