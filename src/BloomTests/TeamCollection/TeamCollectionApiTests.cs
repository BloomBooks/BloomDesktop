using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
	public class TeamCollectionApiTests
	{
		private TeamCollectionApi _api;
		private TemporaryFolder _localCollection;
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			_localCollection = new TemporaryFolder("TeamCollectionApiTests");
			var collectionPath = Path.Combine(_localCollection.FolderPath,
				Path.ChangeExtension(Path.GetFileName(_localCollection.FolderPath), ".bloomCollection"));
			_api = new TeamCollectionApi(new CollectionSettings(collectionPath), new BookSelection(),
				new TeamCollectionManager(collectionPath, new BloomWebSocketServer(), new BookRenamedEvent(), null), null,  null);
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			_localCollection.Dispose();
		}
		[Test]
		public void ProblemsWithLocation_NoProblem_Succeeds()
		{
			using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
			{
				Assert.That(_api.ProblemsWithLocation(tempFolder.FolderPath), Is.EqualTo(""));
			}
		}

		[Test]
		public void ProblemsWithLocation_ExistingTC_Fails()
		{
			using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
			{
				File.WriteAllText(Path.Combine(tempFolder.FolderPath, "Join this Team Collection.JoinBloomTC"), "some random content");
				Assert.That(_api.ProblemsWithLocation(tempFolder.FolderPath), Is.EqualTo("There is a problem with this location"));
			}
		}

		[Test]
		public void ProblemsWithLocation_BloomCollection_Fails()
		{
			using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
			{
				File.WriteAllText(Path.Combine(tempFolder.FolderPath, "something.bloomCollection"), "some random content");
				Assert.That(_api.ProblemsWithLocation(tempFolder.FolderPath), Is.EqualTo("There is a problem with this location"));
			}
		}

		[Test]
		public void ProblemsWithLocation_TCExists_Fails()
		{
			using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
			{
				Directory.CreateDirectory(Path.Combine(tempFolder.FolderPath, Path.GetFileName(_localCollection.FolderPath) + " - TC"));
				Assert.That(_api.ProblemsWithLocation(tempFolder.FolderPath), Is.EqualTo("There is a problem with this location"));
			}
		}

		[Test]
		public void ProblemsWithLocation_FolderReadOnly_Fails()
		{
			using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
			{
				var di = new DirectoryInfo(tempFolder.FolderPath);
				var directorySecurity = di.GetAccessControl();
				var currentUserIdentity = WindowsIdentity.GetCurrent();
				var fileSystemRule = new FileSystemAccessRule(currentUserIdentity.Name,
					FileSystemRights.Write,
					InheritanceFlags.ObjectInherit |
					InheritanceFlags.ContainerInherit,
					PropagationFlags.None,
					AccessControlType.Deny);

				directorySecurity.AddAccessRule(fileSystemRule);
				di.SetAccessControl(directorySecurity);
				Assert.That(_api.ProblemsWithLocation(tempFolder.FolderPath),
					Contains.Substring("Bloom does not have permission to write to the selected folder"));
				directorySecurity.RemoveAccessRule(fileSystemRule);
				di.SetAccessControl(directorySecurity);
			}
		}
	}
}
