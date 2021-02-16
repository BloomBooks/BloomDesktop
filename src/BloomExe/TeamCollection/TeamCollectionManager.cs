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

		public TeamCollectionManager(string localCollectionPath, BloomWebSocketServer webSocketServer)
		{
			_webSocketServer = webSocketServer;
			_localCollectionFolder = Path.GetDirectoryName(localCollectionPath);
			var sharedSettingsPath = Path.Combine(_localCollectionFolder, TeamCollectionSettingsFileName);
			if (File.Exists(sharedSettingsPath))
			{
				try
				{
					var doc = new XmlDocument();
					doc.Load(sharedSettingsPath);
					var sharedFolderPath = doc.DocumentElement.GetElementsByTagName("TeamCollectionFolder").Cast<XmlElement>()
						.First().InnerText;
					if (Directory.Exists(sharedFolderPath))
					{
						CurrentCollection = new FolderTeamCollection(_localCollectionFolder, sharedFolderPath);
						CurrentCollection.SocketServer = SocketServer;
						// Later, we will sync everything else, but we want the current collection settings before
						// we create the CollectionSettings object.
						CurrentCollection.CopySharedCollectionFilesToLocal(_localCollectionFolder);
					}
					else
					{
						NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found team collection settings but could not find the team collection folder " + sharedFolderPath, null, null, true);
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

		public void ConnectToTeamCollection(string sharedFolderParentPath)
		{
			var sharedFolderPath = Path.Combine(sharedFolderParentPath, Path.GetFileName(_localCollectionFolder)+ " - TC");
			Directory.CreateDirectory(sharedFolderPath);
			var newTc = new FolderTeamCollection(_localCollectionFolder, sharedFolderPath);
			newTc.ConnectToTeamCollection(sharedFolderPath);
			CurrentCollection = newTc;
		}

		public const string TeamCollectionSettingsFileName = "TeamCollectionSettings.xml";

		// This is the value the book must be locked to for a local checkout.
		// For all the Sharing code, this should be the one place we know how to find that user.
		public static string CurrentUser => SIL.Windows.Forms.Registration.Registration.Default.Email;

		public void Dispose()
		{
			CurrentCollection?.Dispose();
		}
	}
}
