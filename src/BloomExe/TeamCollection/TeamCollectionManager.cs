using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Registration;
using Bloom.Utils;
using L10NSharp;
using Sentry;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.TeamCollection
{
	public interface ITeamCollectionManager
	{
		void RaiseBookStatusChanged(BookStatusChangeEventArgs eventInfo);
		BookSelection BookSelection { get; }
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
		// Normally the same as CurrentCollection, but CurrentCollection is only
		// non-null when we have a fully functional Team Collection operating.
		// Sometimes a TC may be disconnected, that is, we know this is a TC,
		// but we can't currently do TC operations, for example, because we don't
		// find the folder where the repo lives, or it's a dropbox folder but
		// Dropbox is not running or we can't ping dropbox.com.
		// A collection we know is a TC may also be disabled because there is no
		// enterprise subscription. Another possibility is that we can't do TC
		// operations because the user has not registered; I've been calling this
		// disabled also, but it's not just that we choose not to allow it; we
		// actually need the missing information to make things work.
		// In all these situations, most TC operations simply don't happen because
		// CurrentCollection is null, but there are a few operations that still need
		// to be aware of the TC (for example, we still don't allow editing books
		// that are in the Repo and not checked out, and still show the TC status icon)
		// and it is easiest to achieve this by having a (Disconnected)TC object.
		// This property allows us to find the TC whether or not it is disconnected.
		// I can't find a good word that covers both disconnected and disabled,
		// so in places where it is ambiguous I'm just using disconnected.
		public TeamCollection CurrentCollectionEvenIfDisconnected { get; private set; }

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
		public BookSelection BookSelection { get; private set; }

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
		/// a TC; if it is a TC (even a disconnected one), it's true if the book is
		/// NOT checked out.
		/// </summary>
		/// <remarks>if bookFolderPath is null or empty, it currently returns false.
		/// This is a bit arbitrary. If there's no book currently selected, then we can't
		/// do editing operations...in that sense this situation is similar to a selected
		/// book that needs to be checked out. But strictly it's not true that we need
		/// to check out the selected book to edit...we need a book to be selected!
		/// I wanted to settle on some answer so that callers don't each have to be careful
		/// not to pass null, so I settled on false.</remarks>
		public bool NeedCheckoutToEdit(string bookFolderPath)
		{
			// We use the EvenIfDisconnected version here because we want
			// editing attempts to FAIL if we are in a disconnected TC and don't already have it
			// checked out; we don't just want to edit it as if the collection was not a TC at all.
			if (CurrentCollectionEvenIfDisconnected == null || string.IsNullOrEmpty(bookFolderPath))
				return false;
			return CurrentCollectionEvenIfDisconnected.NeedCheckoutToEdit(bookFolderPath);
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
			if (CurrentCollectionEvenIfDisconnected == null)
				return false;
			return CurrentCollectionEvenIfDisconnected.CannotDeleteBecauseDisconnected(bookFolderPath);
		}

		public TeamCollectionStatus CollectionStatus
		{
			get
			{
				if (CurrentCollectionEvenIfDisconnected != null)
				{
					return CurrentCollectionEvenIfDisconnected.CollectionStatus;
				}

				return TeamCollectionStatus.None;
			}
		}

		public TeamCollectionMessageLog MessageLog
		{
			get
			{
				if (CurrentCollectionEvenIfDisconnected != null)
					return CurrentCollectionEvenIfDisconnected.MessageLog;
				return null;
			}
		}

		public TeamCollectionManager(string localCollectionPath, BloomWebSocketServer webSocketServer,
			BookRenamedEvent bookRenamedEvent, BookStatusChangeEvent bookStatusChangeEvent,
			BookSelection bookSelection, LibraryClosing libraryClosingEvent)
		{
			_webSocketServer = webSocketServer;
			_bookStatusChangeEvent = bookStatusChangeEvent;
			_localCollectionFolder = Path.GetDirectoryName(localCollectionPath);
			BookSelection = bookSelection;
			libraryClosingEvent?.Subscribe((x) =>
			{
				// When closing the collection...especially if we're restarting due to
				// changed settings!...we need to save any settings changes to the repo.
				// In such cases we can't safely wait for the change watcher to write things,
				// because (a) if we're shutting down for good, we just might not detect the
				// change before everything shuts down; and (b) if we're reopening the collection,
				// we might overwrite the change with current collection settings before we
				// save the new ones.
				CurrentCollection?.SyncLocalAndRepoCollectionFiles(false);
			});
			bookRenamedEvent.Subscribe(pair =>
			{
				CurrentCollectionEvenIfDisconnected?.HandleBookRename(Path.GetFileName(pair.Key), Path.GetFileName(pair.Value));
			});
			var impersonatePath = Path.Combine(_localCollectionFolder, "impersonate.txt");
			if (RobustFile.Exists(impersonatePath))
			{
				var lines = RobustFile.ReadAllLines(impersonatePath);
				_overrideCurrentUser = lines.FirstOrDefault();
				if (lines.Length > 1)
					_overrideMachineName = lines[1];
				if (lines.Length > 2)
					_overrideCurrentUserFirstName = lines[2];
				if (lines.Length > 3)
					_overrideCurrentUserSurname = lines[3];
			}

			var localCollectionLinkPath = Path.Combine(_localCollectionFolder, TeamCollectionLinkFileName);
			if (RobustFile.Exists(localCollectionLinkPath))
			{
				try
				{
					var repoFolderPath = RobustFile.ReadAllText(localCollectionLinkPath).Trim();
					CurrentCollection = new FolderTeamCollection(this, _localCollectionFolder, repoFolderPath); // will be replaced if CheckConnection fails
					if (CheckConnection())
					{
						CurrentCollectionEvenIfDisconnected = CurrentCollection;
						CurrentCollection.SocketServer = SocketServer;
						CurrentCollection.TCManager = this;
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
					// else CheckConnection has set up a DisconnectedRepo if that is relevant.
				}
				catch (Exception ex)
				{
					NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "Bloom found Team Collection settings but could not process them", null, ex, true);
					CurrentCollection = null;
					CurrentCollectionEvenIfDisconnected = null;
				}
			}
		}

		/// <summary>
		/// Check that we are still connected to a current team collection. Answer false if we are not,
		/// as well as switching things to the disconnected state.
		/// </summary>
		/// <returns></returns>
		public bool CheckConnection()
		{
			if (CurrentCollection == null)
				return false; // we're already disconnected, or not a TC at all.
			TeamCollectionMessage connectionProblem;
			try
			{
				connectionProblem = CurrentCollection.CheckConnection();
			}
			catch (Exception ex)
			{
				SentrySdk.CaptureException(ex);
				// Unless whatever went wrong left us disconnected, we may as well go ahead and try
				// whatever we were about to do.
				return CurrentCollection != null;
			}

			if (connectionProblem != null)
			{
				MakeDisconnected(connectionProblem, CurrentCollection.RepoDescription);
				RaiseTeamCollectionStatusChanged(); // make the TC icon update
				return false;
			}

			return true;
		}

		public void MakeDisconnected(TeamCollectionMessage message, string repoDescription)
		{
			CurrentCollection = null;
			// This will show the TC icon in error state, and if the dialog is shown it will have this one message.
			CurrentCollectionEvenIfDisconnected = new DisconnectedTeamCollection(this, _localCollectionFolder, repoDescription);
			CurrentCollectionEvenIfDisconnected.SocketServer = SocketServer;
			CurrentCollectionEvenIfDisconnected.TCManager = this;
			CurrentCollectionEvenIfDisconnected.MessageLog.WriteMessage(message);
			CurrentCollectionEvenIfDisconnected.MessageLog.WriteMessage(MessageAndMilestoneType.Error, "TeamCollection.OperatingDisconnected", "When you have resolved this problem, please click \"Reload Collection\". Until then, your Team Collection will operate in \"Disconnected\" mode.",
				null, null);
			// This is normally ensured by pushing an Error message into the log. But in this case,
			// before the user gets a chance to open the dialog, we will run SyncAtStartup, push a Reloaded
			// milestone into the log, and thus suppress it. If we're disconnected, whatever gets in the
			// message log, we want to offer Reload...after all, the message says to use it.
			MessageLog.NextTeamCollectionDialogShouldForceReloadButton = true;
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
			newTc.TCManager = this;
			newTc.SetupTeamCollectionWithProgressDialog(repoFolderPath);
			CurrentCollection = newTc;
			CurrentCollectionEvenIfDisconnected = newTc;
		}

		public string PlannedRepoFolderPath(string repoFolderParentPath)
		{
			return Path.Combine(repoFolderParentPath, Path.GetFileName(_localCollectionFolder)+ " - TC");
		}

		public const string TeamCollectionLinkFileName = "TeamCollectionLink.txt";

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
				msg = "Bloom Enterprise is not enabled.";
			}

			if (!IsRegistrationSufficient())
			{
				l10nId = "TeamCollection.DisabledForRegistration";
				msg = "You have not registered Bloom with at least an email address to identify who is making changes.";
			}

			if (msg != null)
			{
				MakeDisconnected(new TeamCollectionMessage(MessageAndMilestoneType.Error, l10nId, msg), CurrentCollection.RepoDescription);
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
			if (CurrentCollectionEvenIfDisconnected != null)
				CurrentCollectionEvenIfDisconnected.CollectionId = collectionSettingsCollectionId;
		}
	}
}
