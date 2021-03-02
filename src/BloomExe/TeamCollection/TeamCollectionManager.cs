using System;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Api;

namespace Bloom.TeamCollection
{
	public interface ITeamCollectionManager
	{
		void RaiseBookStatusChanged(BookStatusChangeEventArgs eventInfo);
	}

	/// <summary>
	/// This class, created by autofac as part of the project context, handles determining
	/// whether the current collection has an associated TeamCollection, and if so, creating it.
	/// Autofac classes needing access to the TeamCollection (if any) should be constructed
	/// with an instance of this.
	/// </summary>
	public class TeamCollectionManager: IDisposable, ITeamCollectionManager
	{
		private readonly BloomWebSocketServer _webSocketServer;
		private readonly BookStatusChangeEvent _bookStatusChangeEvent;
		public TeamCollection CurrentCollection { get; private set; }
		private readonly string _localCollectionFolder;
		private static string _overrideCurrentUser;
		private static string _overrideCurrentUserFirstName;
		private static string _overrideCurrentUserSurname;
		private static string _overrideMachineName;

		/// <summary>
		/// Force the startup sync of collection files to be FROM the repo TO local.
		/// </summary>
		public static bool ForceNextSyncToLocal { set; get; }

		internal static void ForceCurrentUserForTests(string user)
		{
			_overrideCurrentUser = user;
		}

		public TeamCollectionManager(string localCollectionPath, BloomWebSocketServer webSocketServer, BookRenamedEvent bookRenamedEvent, BookStatusChangeEvent bookStatusChangeEvent)
		{
			_webSocketServer = webSocketServer;
			_bookStatusChangeEvent = bookStatusChangeEvent;
			_localCollectionFolder = Path.GetDirectoryName(localCollectionPath);
			bookRenamedEvent.Subscribe(pair =>
			{
				CurrentCollection?.HandleBookRename(Path.GetFileName(pair.Key), Path.GetFileName(pair.Value));
			});
			var impersonatePath = Path.Combine(_localCollectionFolder, "impersonate.txt");
			if (File.Exists(impersonatePath))
			{
				var lines = File.ReadAllLines(impersonatePath);
				_overrideCurrentUser = lines.FirstOrDefault();
				if (lines.Length > 1)
					_overrideMachineName = lines[1];
				if (lines.Length > 2)
					_overrideCurrentUserFirstName = lines[2];
				if (lines.Length > 3)
					_overrideCurrentUserSurname = lines[3];
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
						CurrentCollection = new FolderTeamCollection(this, _localCollectionFolder, repoFolderPath);
						CurrentCollection.SocketServer = SocketServer;
						// Later, we will sync everything else, but we want the current collection settings before
						// we create the CollectionSettings object.
						if (ForceNextSyncToLocal)
						{
							ForceNextSyncToLocal = false;
							CurrentCollection.CopyRepoCollectionFilesToLocal(_localCollectionFolder);
						}
						else
						{
							CurrentCollection.SyncLocalAndRepoCollectionFiles();
						}
					}
					else
					{
						NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found team collection settings but could not find the team collection folder " + repoFolderPath, null, null, true);
					}
				}
				catch (Exception ex)
				{
					NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found team collection settings but could not process them", null, ex, true);
					CurrentCollection = null;
				}
			}
		}

		public static string GetTcLogPathFromLcPath(string localCollectionFolder)
		{
			return Path.Combine(localCollectionFolder, "log.txt");
		}

		/// <summary>
		/// This gets set when we join a new TeamCollection so that the merge we do
		/// later as we open it gets the special behavior for this case.
		/// </summary>
		public static bool NextMergeIsJoinCollection { get; set; }

		public BloomWebSocketServer SocketServer => _webSocketServer;

		public void ConnectToTeamCollection(string repoFolderParentPath)
		{
			var repoFolderPath = PlannedRepoFolderPath(repoFolderParentPath);
			Directory.CreateDirectory(repoFolderPath);
			var newTc = new FolderTeamCollection(this, _localCollectionFolder, repoFolderPath);
			newTc.ConnectToTeamCollection(repoFolderPath);
			CurrentCollection = newTc;
		}

		public string PlannedRepoFolderPath(string repoFolderParentPath)
		{
			return Path.Combine(repoFolderParentPath, Path.GetFileName(_localCollectionFolder)+ " - TC");
		}

		public const string TeamCollectionSettingsFileName = "TeamCollectionSettings.xml";

		// This is the value the book must be locked to for a local checkout.
		// For all the Team Collection code, this should be the one place we know how to find that user.
		public static string CurrentUser => _overrideCurrentUser ?? SIL.Windows.Forms.Registration.Registration.Default.Email;

		// CurrentUser is the email address and is used as the key, but this is
		// used to display a more friendly name and avatar initials.
		// For all the Team Collection code, this should be the one place we know how to find the current user's first name.
		public static string CurrentUserFirstName => _overrideCurrentUserFirstName ?? SIL.Windows.Forms.Registration.Registration.Default.FirstName;

		// CurrentUser is the email address and is used as the key, but this is
		// used to display a more friendly name and avatar initials.
		// For all the Team Collection code, this should be the one place we know how to find the current user's surname.
		public static string CurrentUserSurname => _overrideCurrentUserSurname ?? SIL.Windows.Forms.Registration.Registration.Default.Surname;

		/// <summary>
		/// This is what the BookStatus.lockedWhere must be for a book to be considered
		/// checked out locally. For all sharing code, this should be the one place to get this.
		/// </summary>
		public static string CurrentMachine => _overrideMachineName ?? Environment.MachineName;

		public void Dispose()
		{
			CurrentCollection?.Dispose();
		}

		public void RaiseBookStatusChanged(BookStatusChangeEventArgs eventInfo)
		{
			_bookStatusChangeEvent.Raise(eventInfo);
		}
	}
}
