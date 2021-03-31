using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Collection;
using Bloom.Registration;
using Bloom.Utils;
using L10NSharp;
using SIL.Reporting;

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
		// Normally the same as CurrentCollection, but when TC behavior is disabled
		// (See CheckDisablingTeamCollections) but we actually DO have a TC, this
		// hangs on to it. A few things, mainly showing the TC status button
		// and launching the TC dialog, are permitted and need it.
		// Also holds the DisconnectedTeamCollection when we can't connect
		public TeamCollection CurrentCollectionEvenIfDisabled { get; private set; }

		/// <summary>
		/// Raised when the status of the whole collection (this.TeamCollectionStatus) might have changed.
		/// (That is, when a new message or milestone arrives...currently we don't ensure that the status
		/// actually IS different from before.)
		/// </summary>
		public static event EventHandler TeamCollectionStatusChanged;
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

		public static void RaiseTeamCollectionStatusChanged()
		{
			TeamCollectionStatusChanged?.Invoke(null, new EventArgs());
		}

		/// <summary>
		/// Return true if the user must check this book out before editing it,
		/// deleting it, etc. This is automatically false if the collection is not
		/// a TC; if it is a TC (even a disconnected or disabled one),
		/// it's true if the book is NOT checked out.
		/// </summary>
		/// <param name="bookFolderPath"></param>
		/// <returns></returns>
		public bool NeedCheckoutToEdit(string bookFolderPath)
		{
			if (CurrentCollectionEvenIfDisabled == null)
				return false;
			return CurrentCollectionEvenIfDisabled.NeedCheckoutToEdit(bookFolderPath);
		}

		/// <summary>
		/// This is an additional check on delete AFTER we make sure the book is checked out.
		/// Even if it is, we can't delete it while disconnected because we don't have a way
		/// to actually remove it from the TC. Our current Delete mechanism, unlike git etc.,
		/// does not postpone delete until commit.
		/// </summary>
		/// <param name="bookFolderPath"></param>
		/// <returns></returns>
		public bool CannotDeleteBecauseDisconnected(string bookFolderPath)
		{
			if (CurrentCollectionEvenIfDisabled == null)
				return false;
			return CurrentCollectionEvenIfDisabled.CannotDeleteBecauseDisconnected(bookFolderPath);
		}

		public TeamCollectionStatus CollectionStatus
		{
			get
			{
				if (CurrentCollectionEvenIfDisabled != null)
				{
					return CurrentCollectionEvenIfDisabled.CollectionStatus;
				}

				return TeamCollectionStatus.None;
			}
		}

		public TeamCollectionMessageLog MessageLog
		{
			get
			{
				if (CurrentCollectionEvenIfDisabled != null)
					return CurrentCollectionEvenIfDisabled.MessageLog;
				return null;
			}
		}

		public TeamCollectionManager(string localCollectionPath, BloomWebSocketServer webSocketServer, BookRenamedEvent bookRenamedEvent, BookStatusChangeEvent bookStatusChangeEvent)
		{
			_webSocketServer = webSocketServer;
			_bookStatusChangeEvent = bookStatusChangeEvent;
			_localCollectionFolder = Path.GetDirectoryName(localCollectionPath);
			bookRenamedEvent.Subscribe(pair =>
			{
				CurrentCollectionEvenIfDisabled?.HandleBookRename(Path.GetFileName(pair.Key), Path.GetFileName(pair.Value));
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
						if (DropboxUtils.IsPathInDropboxFolder(repoFolderPath))
						{
							if (!DropboxUtils.IsDropboxProcessRunning)
							{
								MakeDisconnected(repoFolderPath, "TeamCollection.NeedDropboxRunning",
									"This Team Collection is in “Disconnected” mode because Dropbox does not appear to be running. Please start Dropbox and then restart Bloom.",
									null,null);
								return;
							}

							if (!DropboxUtils.CanAccessDropbox())
							{
								MakeDisconnected(repoFolderPath, "TeamCollection.NeedDropboxAccess",
									"This Team Collection is in “Disconnected” mode because Bloom cannot reach Dropbox.com. Once that internet connection is restored, please restart Bloom.",
									null, null);
								return;
							}
						}
						CurrentCollection = new FolderTeamCollection(this, _localCollectionFolder, repoFolderPath);
						CurrentCollectionEvenIfDisabled = CurrentCollection;
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
						MakeDisconnected( repoFolderPath, "TeamCollection.MissingRepo",
							"This Team Collection is in \"Disconnected\" mode because Bloom could not find the team collection folder at '{0}'. If that drive or network is disconnected, re-connect it and then restart Bloom.{1}{1}If you have moved where that folder is located, 1) quit Bloom 2) go to the Team Collection folder and double-click “Join this Team Collection”.", repoFolderPath, Environment.NewLine);
					}
				}
				catch (Exception ex)
				{
					NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found team collection settings but could not process them", null, ex, true);
					CurrentCollection = null;
					CurrentCollectionEvenIfDisabled = null;
				}
			}
		}

		public void MakeDisconnected(string repoFolderPath, string messageId, string message, string param0, string param1)
		{
			CurrentCollection = null;
			// This will show the TC icon in error state, and if the dialog is shown it will have this one message.
			CurrentCollectionEvenIfDisabled = new DisconnectedTeamCollection(this, _localCollectionFolder, repoFolderPath);
			CurrentCollectionEvenIfDisabled.SocketServer = SocketServer;
			CurrentCollectionEvenIfDisabled.MessageLog.WriteMessage(MessageAndMilestoneType.Error, messageId, message,
				param0, param1);
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

		public void ConnectToTeamCollection(string repoFolderParentPath, string collectionId)
		{
			var repoFolderPath = PlannedRepoFolderPath(repoFolderParentPath);
			Directory.CreateDirectory(repoFolderPath);
			var newTc = new FolderTeamCollection(this, _localCollectionFolder, repoFolderPath);
			newTc.CollectionId = collectionId;
			newTc.SocketServer = SocketServer;
			newTc.SetupTeamCollectionWithProgressDialog(repoFolderPath);
			CurrentCollection = newTc;
			CurrentCollectionEvenIfDisabled = newTc;
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

		/// <summary>
		/// Disable most TC functionality under various conditions. Put a warning in
		/// the log.
		/// </summary>
		public void CheckDisablingTeamCollections(CollectionSettings settings)
		{
			if (CurrentCollection == null)
				return; // already disabled, or not a TC
			string msg = null;
			string l10nId = null;
			if (!settings.HaveEnterpriseFeatures)
			{
				l10nId = "TeamCollection.DisabledForEnterprise";
				msg = "Most Team Collection functions are unavailable because Bloom Enterprise is not enabled.";
			}

			if (!IsRegistrationSufficient())
			{
				l10nId = "TeamCollection.DisabledForRegistration";
				msg = "Most Team Collection functions are unavailable because you have not registered Bloom with at least an email address to identify who is making changes.";
			}

			if (msg != null)
			{
				MakeDisconnected(CurrentCollection.RepoDescription, l10nId, msg,
					null, null);
			}
		}

		/// <summary>
		/// Returns true if registration is sufficient to use Team Collections; false otherwise
		/// </summary>
		public static bool IsRegistrationSufficient()
		{
			// We're normally checking SIL.Windows.Forms.Registration.Registration.Default.Email,
			// but getting it via TCM.CurrentUser allows overriding for testing.
			return !String.IsNullOrWhiteSpace(CurrentUser);
		}

		public void SetCollectionId(string collectionSettingsCollectionId)
		{
			if (CurrentCollectionEvenIfDisabled != null)
				CurrentCollectionEvenIfDisabled.CollectionId = collectionSettingsCollectionId;
		}
	}
}
