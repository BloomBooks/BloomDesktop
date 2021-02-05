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
	/// The main FolderTeamRepoTests class is mostly made up of tests that benefit from a OneTimeSetup
	/// function that creates a repo already connected to local and TeamCollection folders that
	/// already have some content created in the OneTimeSetup. This class is for tests
	/// where that setup gets in the way.
	/// </summary>
	public class FolderTeamRepoTests2
	{
		[Test]
		public void ConnectToTeamCollection_SetsUpRequiredFiles()
		{
			using (var collectionFolder = new TemporaryFolder("FolderTeamRepoTests2_Collection")) {
				using (var sharedFolder = new TemporaryFolder("FolderTeamRepoTests2_Shared"))
				{
					TeamRepoTests.MakeFakeBook(collectionFolder.FolderPath, "Some book", "Something");
					var settingsFileName = Path.ChangeExtension(Path.GetFileName(collectionFolder.FolderPath), "bloomCollection");
					var settingsPath = Path.Combine(collectionFolder.FolderPath, settingsFileName);
					RobustFile.WriteAllText(settingsPath, "This is a fake settings file");

					var nonBookFolder = Path.Combine(collectionFolder.FolderPath, "Some other folder");
					Directory.CreateDirectory(nonBookFolder);
					var repo = new FolderTeamRepo(collectionFolder.FolderPath);

					Assert.That(repo.HasTeamCollection, Is.False);

					// sut
					repo.ConnectToTeamCollection(sharedFolder.FolderPath);

					Assert.That(repo.HasTeamCollection, Is.True);
					var joinCollectioPath =
						Path.Combine(sharedFolder.FolderPath, "Join this Team Collection.JoinBloomTC");
					Assert.That(File.Exists(joinCollectioPath));

					var teamCollectionSettingsPath =
						Path.Combine(collectionFolder.FolderPath, FolderTeamRepo.TeamCollectionSettingsFileName);
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
