using System;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using SIL.IO;

namespace Bloom.TeamCollection
{
    public interface ITeamCollectionManager
    {
        void RaiseBookStatusChanged(BookStatusChangeEventArgs eventInfo);
        BookSelection BookSelection { get; }
        TeamCollection CurrentCollection { get; }
        TeamCollection CurrentCollectionEvenIfDisconnected { get; }
        ITeamCollectionMessageLog MessageLog { get; }
        CollectionSettings Settings { get; }
        CollectionLock Lock { get; }
        bool CheckConnection();
        void ConnectToTeamCollection(string repoFolderParentPath, string collectionId);
        string PlannedRepoFolderPath(string repoFolderParentPath);

        bool OkToEditCollectionSettings { get; }

        bool UserMayChangeEmail { get; }

        // ENHANCE: Add other properties and methods as needed
    }

    /// <summary>
    /// This class, created by autofac as part of the project context, handles determining
    /// whether the current collection has an associated TeamCollection, and if so, creating it.
    /// Autofac classes needing access to the TeamCollection (if any) should be constructed
    /// with an instance of this.
    /// </summary>
    public class TeamCollectionManager : IDisposable, ITeamCollectionManager
    {
        private readonly BloomWebSocketServer _webSocketServer;
        private readonly BookStatusChangeEvent _bookStatusChangeEvent;
        private BookCollectionHolder _bookCollectionHolder;
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

        // We'd prefer to have Autofac just pass us the collection settings
        // in our constructor, as is done for most classes that need one.
        // But TCM must be created by AutoFac before CollectionSettings,
        // so it can sync the latest version of the CS file
        // before the CS is loaded. So instead ProjectContext is hard-coded
        // to set it when available. (The internal is not really a useful
        // restriction, but serves as a hint that nothing else is supposed
        // to use the setter.)
        public CollectionSettings Settings { get; internal set; }

        public bool OkToEditCollectionSettings
        {
            get
            {
                if (CurrentCollectionEvenIfDisconnected == null)
                    return true; // restrictions only apply to TCs
                var settings = Settings;
                if (settings == null)
                {
                    // We can be asked this during startup, before we have settings.
                    // This is rare, so we can afford to be slow.
                    // Conceivably, of course, there could be a newer version of settings
                    // in the TC, which (even more remotely) might change the administrators.
                    // But (a) we're not trying to be perfectly foolproof, and (b) we
                    // don't make the change that this case handles if the repo settings
                    // are newer than the most recent sync.
                    var projectSettingsPath = Path.Combine(
                        _localCollectionFolder,
                        Path.GetFileName(_localCollectionFolder) + ".bloomCollection"
                    );
                    settings = ProjectContext.GetCollectionSettings(projectSettingsPath);
                }
                return CollectionSettingsCanBeEdited(settings);
            }
        }

        public static bool CollectionSettingsCanBeEdited(CollectionSettings settings)
        {
            if (settings.Administrators == null || settings.Administrators.Length == 0)
                return true; // legacy TC, no admin recorded (or not TC)
            return settings.Administrators.Contains(
                CurrentUser,
                StringComparer.InvariantCultureIgnoreCase
            );
        }

        public static void RaiseTeamCollectionStatusChanged()
        {
            TeamCollectionStatusChanged?.Invoke(null, new EventArgs());
        }

        public bool UserMayChangeEmail
        {
            get
            {
                if (CurrentCollection == null)
                    return true;
                return !CurrentCollection.AnyBooksCheckedOutHereByCurrentUser;
            }
        }

        /// <summary>
        /// This is an additional check on delete AFTER we make sure the book is checked out.
        /// If the collection is actually NOT a Team Collection, or if the book actually exists
        /// only locally (and thus has never been checked in to the Team Collection), it's okay
        /// to delete.  If the book is checked out, and the Team Collection knows about it, we
        /// can't delete it while disconnected because we don't have a way to actually remove it
        /// from the TC while disconnected.  Our current Delete mechanism, unlike git etc., does
        /// not postpone delete until commit.
        /// </summary>
        /// <remarks>
        /// This method has to handle the case of the computer getting disconnected from the
        /// internet after Bloom has started and CurrentCollectionEvenIfDisconnected has been
        /// initialized in the "connected" state.
        /// </remarks>
        /// <returns>true if the book should not be deleted, false if it's okay to delete</returns>
        public bool CannotDeleteBecauseDisconnected(Book.Book book)
        {
            // not in a team collection?  delete away.
            if (CollectionStatus == TeamCollectionStatus.None)
                return false;
            // check whether book is known to team collection and we're disconnected at this point in time (not just at startup)
            if (TeamCollection.IsBookKnownToTeamCollection(book.FolderPath) && !CheckConnection())
                return true;
            if (CurrentCollectionEvenIfDisconnected == null)
                return false;
            return CurrentCollectionEvenIfDisconnected.CannotDeleteBecauseDisconnected(
                book.FolderPath
            );
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

        public ITeamCollectionMessageLog MessageLog
        {
            get
            {
                if (CurrentCollectionEvenIfDisconnected != null)
                    return CurrentCollectionEvenIfDisconnected.MessageLog;
                return null;
            }
        }

        public CollectionLock Lock { get; }

        public TeamCollectionManager(
            string localCollectionPath,
            BloomWebSocketServer webSocketServer,
            BookRenamedEvent bookRenamedEvent,
            BookStatusChangeEvent bookStatusChangeEvent,
            BookSelection bookSelection,
            CollectionClosing collectionClosingEvent,
            BookCollectionHolder bookCollectionHolder,
            CollectionLock theLock = null
        )
        {
            Lock = theLock;
            _webSocketServer = webSocketServer;
            _bookStatusChangeEvent = bookStatusChangeEvent;
            _localCollectionFolder = Path.GetDirectoryName(localCollectionPath);
            _bookCollectionHolder = bookCollectionHolder;
            BookSelection = bookSelection;
            collectionClosingEvent?.Subscribe(
                (x) =>
                {
                    // When closing the collection...especially if we're restarting due to
                    // changed settings!...we need to save any settings changes to the repo.
                    // In such cases we can't safely wait for the change watcher to write things,
                    // because (a) if we're shutting down for good, we just might not detect the
                    // change before everything shuts down; and (b) if we're reopening the collection,
                    // we might overwrite the change with current collection settings before we
                    // save the new ones.
                    if (CurrentCollection != null)
                    {
                        CurrentCollection.SyncLocalAndRepoCollectionFiles(false);
                    }
                    else if (
                        Settings.HaveEnterpriseFeatures
                        && CurrentCollectionEvenIfDisconnected != null
                        && CurrentCollectionEvenIfDisconnected
                            is DisconnectedTeamCollection disconnectedTC
                        && disconnectedTC.DisconnectedBecauseNoEnterprise
                    )
                    {
                        // We were disconnected because of Enterprise being off, but now the user has
                        // turned Enterprise on again. We really need to save that, even though we usually don't
                        // save settings changes when disconnected. Otherwise, restarting will restore the
                        // no-enterprise state, and we will be stuck.
                        // Note: We don't need to check for admin privileges here. If the user isn't an admin,
                        // he could not have made any changes to settings, including turning on enterprise.
                        var tempCollectionLinkPath = GetTcLinkPathFromLcPath(
                            _localCollectionFolder
                        );
                        if (RobustFile.Exists(tempCollectionLinkPath))
                        {
                            try
                            {
                                var repoFolderPath = RepoFolderPathFromLinkPath(
                                    tempCollectionLinkPath
                                );
                                var tempCollection = new FolderTeamCollection(
                                    this,
                                    _localCollectionFolder,
                                    repoFolderPath,
                                    bookCollectionHolder: _bookCollectionHolder,
                                    collectionLock: Lock
                                );
                                var problemWithConnection = tempCollection.CheckConnection();
                                if (problemWithConnection == null)
                                {
                                    tempCollection.SyncLocalAndRepoCollectionFiles(false);
                                }
                                else
                                {
                                    NonFatalProblem.Report(
                                        ModalIf.All,
                                        PassiveIf.All,
                                        "Bloom could not save your settings to the Team Collection: "
                                            + problemWithConnection.TextForDisplay,
                                        null,
                                        null,
                                        true
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                NonFatalProblem.Report(
                                    ModalIf.All,
                                    PassiveIf.All,
                                    "Bloom could not save your settings to the Team Collection",
                                    null,
                                    ex,
                                    true
                                );
                            }
                        }
                        // What if there's NOT a TC link file? Then it would be pathological to have a CurrentCollectionEvenIfDisconnected.
                        // It's no longer a TC, so we don't need to save the settings to the TC. For now I'm just not going to do anything.
                    }
                }
            );
            bookRenamedEvent.Subscribe(pair =>
            {
                CurrentCollectionEvenIfDisconnected?.HandleBookRename(
                    Path.GetFileName(pair.Key),
                    Path.GetFileName(pair.Value)
                );
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

            var localCollectionLinkPath = GetTcLinkPathFromLcPath(_localCollectionFolder);
            if (RobustFile.Exists(localCollectionLinkPath))
            {
                try
                {
                    var repoFolderPath = RepoFolderPathFromLinkPath(localCollectionLinkPath);
                    CurrentCollection = new FolderTeamCollection(
                        this,
                        _localCollectionFolder,
                        repoFolderPath,
                        bookCollectionHolder: _bookCollectionHolder,
                        collectionLock: Lock
                    ); // will be replaced if CheckConnection fails
                    // BL-10704: We set this to the CurrentCollection BEFORE checking the connection,
                    // so that there will be a valid MessageLog if we need it during CheckConnection().
                    // If CheckConnection() fails, it will reset this to a DisconnectedTeamCollection.
                    CurrentCollectionEvenIfDisconnected = CurrentCollection;
                    if (CheckConnection())
                    {
                        CurrentCollection.SocketServer = SocketServer;
                        CurrentCollection.TCManager = this;
                        // Later, we will sync everything else, but we want the current collection settings before
                        // we create the CollectionSettings object.
                        if (ForceNextSyncToLocal)
                        {
                            ForceNextSyncToLocal = false;
                            CurrentCollection.CopyRepoCollectionFilesToLocal(
                                _localCollectionFolder
                            );
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
                    NonFatalProblem.Report(
                        ModalIf.All,
                        PassiveIf.All,
                        "Bloom found Team Collection settings but could not process them",
                        null,
                        ex,
                        true
                    );
                    CurrentCollection = null;
                    CurrentCollectionEvenIfDisconnected = null;
                }
            }
        }

        public static string RepoFolderPathFromLinkPath(string localCollectionLinkPath)
        {
            return RobustFile.ReadAllText(localCollectionLinkPath).Trim();
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
                NonFatalProblem.ReportSentryOnly(ex);
                // Unless whatever went wrong left us disconnected, we may as well go ahead and try
                // whatever we were about to do.
                return CurrentCollection != null;
            }

            if (connectionProblem != null)
            {
                MakeDisconnected(connectionProblem, CurrentCollection.RepoDescription);
                return false;
            }

            return true;
        }

        public void MakeDisconnected(TeamCollectionMessage message, string repoDescription)
        {
            CurrentCollection = null;
            // This will show the TC icon in error state, and if the dialog is shown it will have this one message.
            CurrentCollectionEvenIfDisconnected = new DisconnectedTeamCollection(
                this,
                _localCollectionFolder,
                repoDescription
            );
            CurrentCollectionEvenIfDisconnected.SocketServer = SocketServer;
            CurrentCollectionEvenIfDisconnected.TCManager = this;
            // Every call to MessageLog.WriteMessage() also raises the TeamCollectionStatusChanged event.
            CurrentCollectionEvenIfDisconnected.MessageLog.WriteMessage(message);
            CurrentCollectionEvenIfDisconnected.MessageLog.WriteMessage(
                MessageAndMilestoneType.Error,
                "TeamCollection.OperatingDisconnected",
                "When you have resolved this problem, please click \"Reload Collection\". Until then, your Team Collection will operate in \"Disconnected\" mode.",
                null,
                null
            );
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

        public static string GetTcLinkPathFromLcPath(string localCollectionFolder)
        {
            return Path.Combine(localCollectionFolder, TeamCollectionLinkFileName);
        }

        /// <summary>
        /// This gets set when we join a new TeamCollection so that the merge we do
        /// later as we open it gets the special behavior for this case.
        /// </summary>
        public static bool NextMergeIsFirstTimeJoinCollection { get; set; }

        public BloomWebSocketServer SocketServer => _webSocketServer;

        public void ConnectToTeamCollection(string repoFolderParentPath, string collectionId)
        {
            var repoFolderPath = PlannedRepoFolderPath(repoFolderParentPath);
            Directory.CreateDirectory(repoFolderPath);
            // The creator of a TC is its first and, for now, usually only administrator.
            Settings.Administrators = new[] { CurrentUser };
            Settings.Save();
            var newTc = new FolderTeamCollection(
                this,
                _localCollectionFolder,
                repoFolderPath,
                bookCollectionHolder: _bookCollectionHolder,
                collectionLock: Lock
            );
            newTc.CollectionId = collectionId;
            newTc.SocketServer = SocketServer;
            newTc.TCManager = this;
            newTc.SetupTeamCollectionWithProgressDialog(repoFolderPath);
            CurrentCollection = newTc;
            CurrentCollectionEvenIfDisconnected = newTc;
        }

        public string PlannedRepoFolderPath(string repoFolderParentPath)
        {
            return Path.Combine(repoFolderParentPath, Settings.CollectionName + " - TC");
        }

        public const string TeamCollectionLinkFileName = "TeamCollectionLink.txt";

        // This is the value the book must be locked to for a local checkout.
        // For all the Team Collection code, this should be the one place we know how to find that user.
        public static string CurrentUser =>
            _overrideCurrentUser ?? SIL.Windows.Forms.Registration.Registration.Default.Email;

        // CurrentUser is the email address and is used as the key, but this is
        // used to display a more friendly name and avatar initials.
        // For all the Team Collection code, this should be the one place we know how to find the current user's first name.
        public static string CurrentUserFirstName =>
            _overrideCurrentUserFirstName
            ?? SIL.Windows.Forms.Registration.Registration.Default.FirstName;

        // CurrentUser is the email address and is used as the key, but this is
        // used to display a more friendly name and avatar initials.
        // For all the Team Collection code, this should be the one place we know how to find the current user's surname.
        public static string CurrentUserSurname =>
            _overrideCurrentUserSurname
            ?? SIL.Windows.Forms.Registration.Registration.Default.Surname;

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
                msg =
                    "You have not registered Bloom with at least an email address to identify who is making changes.";
            }

            if (msg != null)
            {
                MakeDisconnected(
                    new TeamCollectionMessage(MessageAndMilestoneType.Error, l10nId, msg),
                    CurrentCollection.RepoDescription
                );
                if (!settings.HaveEnterpriseFeatures)
                    (
                        CurrentCollectionEvenIfDisconnected as DisconnectedTeamCollection
                    ).DisconnectedBecauseNoEnterprise = true;
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
