using Bloom.TeamCollection;
using BloomTemp;
using Moq;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
	public class DisconnectedTeamCollectionTests
	{
		[Test]
		public void GetStatus_NoLocalStatus_ReturnsNewBookStatus()
		{
			using (var collectionFolder =
				new TemporaryFolder("GetStatus_NoLocalStatus_ReturnsNewBookStatus"))
			{
				var mockTcManager = new Mock<ITeamCollectionManager>();
				SyncAtStartupTests.MakeFakeBook(collectionFolder.FolderPath, "Local book", "This is only local");
				var tc = new DisconnectedTeamCollection(mockTcManager.Object, collectionFolder.FolderPath, "my collection");
				var status = tc.GetStatus("Local book");
				Assert.That(status.lockedBy, Is.EqualTo(Bloom.TeamCollection.TeamCollection.FakeUserIndicatingNewBook));
				Assert.That(status.lockedWhere, Is.EqualTo(TeamCollectionManager.CurrentMachine));
				Assert.That(tc.CannotDeleteBecauseDisconnected("Local book"), Is.False);
			}
		}

		[Test]
		public void GetStatus_LocalStatusWrongId_ReturnsNewBookStatus()
		{
			using (var collectionFolder =
				new TemporaryFolder("GetStatus_LocalStatusWrongId_ReturnsNewBookStatus"))
			{
				var mockTcManager = new Mock<ITeamCollectionManager>();
				SyncAtStartupTests.MakeFakeBook(collectionFolder.FolderPath, "Copied book", "This is only local but has status");
				var tc = new DisconnectedTeamCollection(mockTcManager.Object, collectionFolder.FolderPath, "my collection");
				tc.WriteLocalStatus("Copied book", new BookStatus(), collectionId:"nonsence");
				var status = tc.GetStatus("Copied book");
				Assert.That(status.lockedBy, Is.EqualTo(Bloom.TeamCollection.TeamCollection.FakeUserIndicatingNewBook));
				Assert.That(status.lockedWhere, Is.EqualTo(TeamCollectionManager.CurrentMachine));
				Assert.That(tc.CannotDeleteBecauseDisconnected("Copied book"), Is.False);
			}
		}

		[Test]
		public void GetStatus_LocalStatus_ReturnsLocalStatus_WithoutOldName()
		{
			using (var collectionFolder =
				new TemporaryFolder("GetStatus_LocalStatus_ReturnsLocalStatus"))
			{
				var mockTcManager = new Mock<ITeamCollectionManager>();
				SyncAtStartupTests.MakeFakeBook(collectionFolder.FolderPath, "Repo book", "This is simulating a book that's in repo");
				var tc = new DisconnectedTeamCollection(mockTcManager.Object, collectionFolder.FolderPath, "my collection");
				tc.WriteLocalStatus("Repo book", new BookStatus().WithChecksum("a checksum").WithLockedBy("fred@somewhere.org").WithOldName("Renamed from"));
				var status = tc.GetStatus("Repo book");
				Assert.That(status.lockedBy, Is.EqualTo("fred@somewhere.org"));
				Assert.That(status.lockedWhere, Is.EqualTo(TeamCollectionManager.CurrentMachine));
				Assert.That(status.checksum, Is.EqualTo("a checksum"));
				Assert.That(status.oldName, Is.Null);
				Assert.That(tc.CannotDeleteBecauseDisconnected("Repo book"), Is.True);
			}
		}
	}
}
