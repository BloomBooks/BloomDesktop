using System;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Api;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// This class, created by autofac as part of the project context, handles determining
	/// whether the current collection has an associated TeamCollection, and if so, creating it.
	/// Autofac classes needing access to the TeamCollection (if any) should be constructed
	/// with an instance of this.
	/// </summary>
	public class TeamCollectionManager: IDisposable
	{
		private readonly BloomWebSocketServer _webSocketServer;
		public TeamCollection CurrentCollection { get; private set; }
		private string _localCollectionFolder;
		private static string _overrideCurrentUser;
		private static string _overrideMachineName;

		public TeamCollectionManager(string localCollectionPath, BloomWebSocketServer webSocketServer)
		{
			_webSocketServer = webSocketServer;
			_localCollectionFolder = Path.GetDirectoryName(localCollectionPath);
			var impersonatePath = Path.Combine(_localCollectionFolder, "impersonate.txt");
			if (File.Exists(impersonatePath))
			{
				var lines = File.ReadAllLines(impersonatePath);
				_overrideCurrentUser = lines.FirstOrDefault();
				if (lines.Length > 1)
				{
					_overrideMachineName = lines[1];
				}
			}

			var localSettingsPath = Path.Combine(_localCollectionFolder, TeamCollectionSettingsFileName);
			if (File.Exists(localSettingsPath))
			{
				try
				{
					var doc = new XmlDocument();
					doc.Load(localSettingsPath);
					var repoFolderPath = doc.DocumentElement.GetElementsByTagName("TeamCollectionFolder").Cast<XmlElement>()
						.First().InnerText;
					if (Directory.Exists(repoFolderPath))
					{
						CurrentCollection = new FolderTeamCollection(_localCollectionFolder, repoFolderPath);
						CurrentCollection.SocketServer = SocketServer;
						// Later, we will sync everything else, but we want the current collection settings before
						// we create the CollectionSettings object.
						CurrentCollection.CopyRepoCollectionFilesToLocal(_localCollectionFolder);
					}
					else
					{
						NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found team collection settings but could not find the team collection folder " + repoFolderPath, null, null, true);
					}
				}
				catch (Exception ex)
				{
					NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found team collection settings but could not process them", null, ex, true);
				}
			}
		}

		/// <summary>
		/// This gets set when we join a new TeamCollection so that the merge we do
		/// later as we open it gets the special behavior for this case.
		/// </summary>
		public static bool NextMergeIsJoinCollection { get; set; }

		public BloomWebSocketServer SocketServer => _webSocketServer;

		public void ConnectToTeamCollection(string repoFolderParentPath)
		{
			var repoFolderPath = Path.Combine(repoFolderParentPath, Path.GetFileName(_localCollectionFolder)+ " - TC");
			Directory.CreateDirectory(repoFolderPath);
			var newTc = new FolderTeamCollection(_localCollectionFolder, repoFolderPath);
			newTc.ConnectToTeamCollection(repoFolderPath);
			CurrentCollection = newTc;
		}

		public const string TeamCollectionSettingsFileName = "TeamCollectionSettings.xml";

		// This is the value the book must be locked to for a local checkout.
		// For all the Sharing code, this should be the one place we know how to find that user.
		public static string CurrentUser => _overrideCurrentUser ?? SIL.Windows.Forms.Registration.Registration.Default.Email;

		/// <summary>
		/// This is what the BookStatus.lockedWhere must be for a book to be considered
		/// checked out locally. For all sharing code, this should be the one place to get this.
		/// </summary>
		public static string CurrentMachine => _overrideMachineName ?? Environment.MachineName;

		public void Dispose()
		{
			CurrentCollection?.Dispose();
		}
	}
}
