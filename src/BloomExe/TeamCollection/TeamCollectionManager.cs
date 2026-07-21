using System;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
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
        TeamCollectionStatus CollectionStatus { get; }
        CollectionSettings Settings { get; }
        CollectionLock Lock { get; }
        bool CheckConnection();
        void ConnectToTeamCollection(string repoFolderParentPath, string collectionId);

        /// <summary>
        /// Connect the current collection to a new cloud-backed Team Collection, using
        /// <paramref name="collectionId"/> as both this Bloom collection's own CollectionId GUID
        /// and the server's `collections.id` (CONTRACTS.md: "&lt;collectionId&gt; = the Bloom
        /// CollectionId GUID (also the server collections.id)"). Creates the server-side row and
        /// pushes every existing local book and collection-level file up.
        /// </summary>
        void ConnectToCloudCollection(string collectionId);

        string PlannedRepoFolderPath(string repoFolderParentPath);

        bool OkToEditCollectionSettings { get; }

        bool UserMayChangeEmail { get; }

        /// <summary>
        /// Sends the bookContent/reload websocket event so the collection-tab preview
        /// iframe refreshes its content, even when the selected book ID has not changed.
        /// Call this after silently re-copying a book's content from the repo so the UI
        /// reflects the updated files without requiring a manual Reload.
        /// </summary>
        void SendBookContentReload();

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
        // Dropbox is not running or we can't access dropbox.com.
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
                    var projectSettingsPath = CollectionSettings.GetSettingsFilePath(
                        _localCollectionFolder
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
            BookStatusChangeEvent bookStatusChangeEvent,
            BookSelection bookSelection,
            CollectionClosing collectionClosingEvent,
            BookCollectionHolder bookCollectionHolder,
            CollectionLock theLock = null,
            BookRenamedEvent bookRenamedEvent = null
        )
        {
            Lock = theLock;
            _webSocketServer = webSocketServer;
            _bookStatusChangeEvent = bookStatusChangeEvent;
            _localCollectionFolder = Path.GetDirectoryName(localCollectionPath);
            _bookCollectionHolder = bookCollectionHolder;
            BookSelection = bookSelection;
            // For cloud TCs, poll the server the moment a book is selected, so its checkout
            // status is current when the user is looking at it rather than up to a full poll
            // interval stale. The poll runs on a background thread (GetChanges is a network
            // round trip; SelectionChanged fires on the UI thread) and its results flow through
            // the same change-event pipeline as the timer-driven polls. PollNow itself coalesces:
            // a poll already in flight makes this a no-op, so rapid selection changes cannot
            // stack up server requests.
            // (bookSelection is null in several unit-test constructions of this class.)
            if (bookSelection != null)
                bookSelection.SelectionChanged += (sender, args) =>
                {
                    if (CurrentCollection is Cloud.CloudTeamCollection cloudCollection)
                        System.Threading.Tasks.Task.Run(() => cloudCollection.PollNow());
                };
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
                        FeatureStatus
                            .GetFeatureStatus(Settings.Subscription, FeatureName.TeamCollection)
                            .Enabled
                        && CurrentCollectionEvenIfDisconnected != null
                        && CurrentCollectionEvenIfDisconnected
                            is DisconnectedTeamCollection disconnectedTC
                        && disconnectedTC.DisconnectedBecauseOfSubscriptionTier
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
                                var tempLink = TeamCollectionLink.FromFile(tempCollectionLinkPath);
                                var tempCollection = CreateFolderTeamCollection(
                                    tempLink,
                                    bookCollectionHolder
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
            if (bookRenamedEvent != null)
            {
                bookRenamedEvent.Subscribe(pair =>
                {
                    CurrentCollectionEvenIfDisconnected?.HandleBookRename(
                        Path.GetFileName(pair.Key),
                        Path.GetFileName(pair.Value)
                    );
                });
            }
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
                // repoDescription is used when falling back to a DisconnectedTeamCollection.
                // For folder TCs it is the folder path; for cloud TCs it is the cloud URI.
                string repoDescription = null;
                try
                {
                    var link = TeamCollectionLink.FromFile(localCollectionLinkPath);
                    repoDescription = link?.RepoFolderPath ?? link?.ToFileContent();
                    CurrentCollection = CreateTeamCollectionFromLink(link, bookCollectionHolder); // will be replaced if CheckConnection fails
                    // BL-10704: We set this to the CurrentCollection BEFORE checking the connection,
                    // so that there will be a valid MessageLog if we need it during CheckConnection().
                    // If CheckConnection() fails, it will reset this to a DisconnectedTeamCollection.
                    CurrentCollectionEvenIfDisconnected = CurrentCollection;
                    // allowHardRefusal: true -- batch item 9 (account-switch behavior): opening a
                    // collection under an account that's not a server member of it must abort the
                    // open entirely (TeamCollectionAccessRefusedException, caught in
                    // Program.HandleErrorOpeningProjectWindow), not silently fall back to
                    // Disconnected mode like any other connection problem.
                    if (CheckConnection(allowHardRefusal: true))
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
                catch (TeamCollectionAccessRefusedException)
                {
                    // Batch item 9 (account-switch behavior): let this propagate all the way up
                    // to Program.cs (through ProjectContext's constructor), which aborts opening
                    // the collection entirely and shows the composed refusal message -- unlike
                    // every other exception here, this one must NOT be swallowed into an ordinary
                    // "TC initialization failed, fall back to Disconnected mode" outcome.
                    throw;
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
                    // Create a DisconnectedTeamCollection so we still have a TC object that prevents
                    // undesirable operations like editing un-checked-out books. This handles cases where
                    // the TC initialization fails, not just connection problems.
                    if (repoDescription != null)
                    {
                        var disconnectedTC = new DisconnectedTeamCollection(
                            this,
                            _localCollectionFolder,
                            repoDescription
                        );
                        disconnectedTC.SocketServer = SocketServer;
                        disconnectedTC.TCManager = this;
                        disconnectedTC.MessageLog.WriteMessage(
                            MessageAndMilestoneType.Error,
                            // MessageLog requires this API, but because TC is experimental, I haven't actually
                            // created this item in the XLF yet..not even with 'translate=no', since we
                            // expect this to be still experimental in the next release.
                            "TeamCollection.InitializationFailure",
                            "Bloom could not initialize the Team Collection. Some Team Collection operations will not be available.",
                            null,
                            null
                        );
                        disconnectedTC.MessageLog.NextTeamCollectionDialogShouldForceReloadButton =
                            true;
                        disconnectedTC.DisconnectedBecauseOfInitializationFailure = true;
                        CurrentCollectionEvenIfDisconnected = disconnectedTC;
                    }
                    else
                    {
                        CurrentCollectionEvenIfDisconnected = null;
                    }
                }
            }
        }

        /// <summary>
        /// Reads the repo folder path from the link file at <paramref name="localCollectionLinkPath"/>.
        /// Preserves the existing trimming behavior.
        /// </summary>
        public static string RepoFolderPathFromLinkPath(string localCollectionLinkPath)
        {
            return RobustFile.ReadAllText(localCollectionLinkPath).Trim();
        }

        /// <summary>
        /// Factory: create the appropriate <see cref="TeamCollection"/> subclass for the given
        /// parsed <see cref="TeamCollectionLink"/>. For folder links this returns a
        /// <see cref="FolderTeamCollection"/>; for cloud links, a
        /// <see cref="Bloom.TeamCollection.Cloud.CloudTeamCollection"/>.
        /// </summary>
        /// <param name="link">Parsed link; must not be null.</param>
        /// <param name="bookCollectionHolder">Passed through to the collection constructor.</param>
        private TeamCollection CreateTeamCollectionFromLink(
            TeamCollectionLink link,
            BookCollectionHolder bookCollectionHolder
        )
        {
            if (link == null)
                throw new ArgumentNullException(nameof(link));

            if (link.IsFolder)
                return CreateFolderTeamCollection(link, bookCollectionHolder);

            return CreateCloudTeamCollection(link, bookCollectionHolder);
        }

        /// <summary>
        /// Creates a <see cref="FolderTeamCollection"/> for a folder-backed link.
        /// </summary>
        /// <param name="link">Must be a folder link.</param>
        /// <param name="bookCollectionHolder">Passed through to the collection constructor.</param>
        private FolderTeamCollection CreateFolderTeamCollection(
            TeamCollectionLink link,
            BookCollectionHolder bookCollectionHolder
        )
        {
            if (link == null)
                throw new ArgumentNullException(nameof(link));
            if (!link.IsFolder)
                throw new ArgumentException("Expected a folder-backed link.", nameof(link));

            return new FolderTeamCollection(
                this,
                _localCollectionFolder,
                link.RepoFolderPath,
                bookCollectionHolder: bookCollectionHolder,
                collectionLock: Lock
            );
        }

        /// <summary>
        /// Creates a <see cref="Bloom.TeamCollection.Cloud.CloudTeamCollection"/> for a cloud-backed
        /// link.
        /// </summary>
        /// <param name="link">Must be a cloud link.</param>
        /// <param name="bookCollectionHolder">Passed through to the collection constructor.</param>
        private Bloom.TeamCollection.Cloud.CloudTeamCollection CreateCloudTeamCollection(
            TeamCollectionLink link,
            BookCollectionHolder bookCollectionHolder
        )
        {
            if (link == null)
                throw new ArgumentNullException(nameof(link));
            if (!link.IsCloud)
                throw new ArgumentException("Expected a cloud-backed link.", nameof(link));

            return new Bloom.TeamCollection.Cloud.CloudTeamCollection(
                this,
                _localCollectionFolder,
                link.CloudCollectionId,
                bookCollectionHolder: bookCollectionHolder,
                collectionLock: Lock
            );
        }

        /// <summary>
        /// Check that we are still connected to a current team collection. Answer false if we are not,
        /// as well as switching things to the disconnected state.
        /// </summary>
        /// <returns></returns>
        public bool CheckConnection() => CheckConnection(allowHardRefusal: false);

        /// <summary>
        /// As <see cref="CheckConnection()"/>, but when <paramref name="allowHardRefusal"/> is
        /// true, a connection problem flagged as <see cref="TeamCollectionMessage.IsAccessRefusal"/>
        /// (currently: CloudTeamCollection.CheckConnection's non-member case, batch item 9) throws
        /// <see cref="TeamCollectionAccessRefusedException"/> instead of falling back to
        /// Disconnected mode. Only the initial open (this class's constructor, below) passes
        /// true -- a membership loss discovered LATER in the session (e.g. via
        /// TeamCollectionApi's ordinary CheckConnection() calls) must still just disconnect, not
        /// crash the running app.
        /// </summary>
        public bool CheckConnection(bool allowHardRefusal)
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
                if (allowHardRefusal && connectionProblem.IsAccessRefusal)
                    throw new TeamCollectionAccessRefusedException(
                        connectionProblem.TextForDisplay
                    );
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

        /// <summary>
        /// Connect the current collection to a new folder-backed Team Collection stored under
        /// <paramref name="repoFolderParentPath"/>.
        /// </summary>
        public void ConnectToTeamCollection(string repoFolderParentPath, string collectionId)
        {
            var repoFolderPath = PlannedRepoFolderPath(repoFolderParentPath);
            Directory.CreateDirectory(repoFolderPath);
            // The creator of a TC is its first and, for now, usually only administrator.
            Settings.Administrators = new[] { CurrentUser };
            Settings.Save();
            var link = TeamCollectionLink.ForFolder(repoFolderPath);
            var newTc = CreateFolderTeamCollection(link, _bookCollectionHolder);
            newTc.CollectionId = collectionId;
            newTc.SocketServer = SocketServer;
            newTc.TCManager = this;
            newTc.SetupTeamCollectionWithProgressDialog(repoFolderPath);
            CurrentCollection = newTc;
            CurrentCollectionEvenIfDisconnected = newTc;
        }

        /// <summary>
        /// Throws <see cref="TeamCollectionLinkConflictException"/> if
        /// <paramref name="localCollectionFolder"/> already has a TeamCollectionLink.txt
        /// describing some OTHER Team Collection (folder- or cloud-backed) -- see
        /// <see cref="ConnectToCloudCollection"/>'s doc comment for why this situation means the
        /// "un-team" step wasn't finished. Static and file-system-only (no network calls) so it
        /// can be unit tested directly against a temp folder, unlike ConnectToCloudCollection as
        /// a whole, which also creates the server-side collection.
        /// Does nothing if there is no link file (the ordinary case: a plain local collection,
        /// or one already fully un-teamed).
        /// </summary>
        internal static void ThrowIfConflictingTeamCollectionLink(string localCollectionFolder)
        {
            var linkPath = GetTcLinkPathFromLcPath(localCollectionFolder);
            var existingLink = TeamCollectionLink.FromFile(linkPath);
            if (existingLink == null)
                return;

            if (existingLink.IsFolder)
                throw new TeamCollectionLinkConflictException(
                    "This collection still has an active Team Collection link to the "
                        + $"shared folder \"{existingLink.RepoFolderPath}\". Before enabling "
                        + "Cloud Team Collections, finish disconnecting from the old "
                        + $"(folder-based) Team Collection: delete \"{TeamCollectionLinkFileName}\" "
                        + "from this collection's folder, then try again."
                );
            // existingLink.IsCloud: already linked to some cloud collection (possibly this
            // very one, if this got called twice). Either way there's nothing to set up.
            throw new TeamCollectionLinkConflictException(
                "This collection is already linked to a cloud Team Collection "
                    + $"(id {existingLink.CloudCollectionId})."
            );
        }

        /// <summary>
        /// Connect the current collection to a new cloud-backed Team Collection, using
        /// <paramref name="collectionId"/> as both this Bloom collection's own CollectionId GUID
        /// and the server's `collections.id` (CONTRACTS.md: "&lt;collectionId&gt; = the Bloom
        /// CollectionId GUID (also the server collections.id)"). Creates the server-side row
        /// (create_collection), links the local collection to it, and pushes every existing local
        /// book and collection-level file up -- the cloud counterpart of
        /// <see cref="ConnectToTeamCollection"/>'s folder-backed flow.
        ///
        /// Guards the "adoption path" from a formerly-folder-based Team Collection (task 10):
        /// throws <see cref="TeamCollectionLinkConflictException"/> if TeamCollectionLink.txt
        /// still describes a different (folder or cloud) Team Collection -- a sign the user
        /// hasn't finished "un-teaming" this local collection yet -- and otherwise cleans up any
        /// stale per-book/per-collection artifacts the old TC left behind before pushing
        /// everything to the new cloud collection (<see
        /// cref="TeamCollection.CleanStaleTeamCollectionArtifacts"/>).
        /// </summary>
        public void ConnectToCloudCollection(string collectionId)
        {
            ThrowIfConflictingTeamCollectionLink(_localCollectionFolder);
            // No conflicting link -- but the folder may still carry stale per-book status files,
            // lastCollectionFileSyncData.txt, or log.txt from a Team Collection this local folder
            // used to belong to before being un-teamed. Clear those before the first Send so
            // every book uploads as a clean v1, not a stale checksum/lockedBy from the old TC.
            TeamCollection.CleanStaleTeamCollectionArtifacts(_localCollectionFolder);

            // The creator of a TC is its first and, for now, usually only administrator.
            Settings.Administrators = new[] { CurrentUser };
            Settings.Save();

            var environment = Cloud.CloudEnvironment.Current;
            var auth = Cloud.CloudAuth.CreateInitialized(environment);
            var client = new Cloud.CloudCollectionClient(environment, auth);
            client.CreateCollection(collectionId, Settings.CollectionName);

            var link = TeamCollectionLink.ForCloud(collectionId);
            link.WriteToFile(GetTcLinkPathFromLcPath(_localCollectionFolder));

            var newTc = new Cloud.CloudTeamCollection(
                this,
                _localCollectionFolder,
                collectionId,
                bookCollectionHolder: _bookCollectionHolder,
                collectionLock: Lock,
                environment: environment,
                auth: auth,
                client: client
            );
            newTc.SocketServer = SocketServer;
            newTc.TCManager = this;
            newTc.SetupCloudTeamCollectionWithProgressDialog();
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
            _overrideCurrentUser ?? Bloom.Registration.Registration.Default.Email;

        // CurrentUser is the email address and is used as the key, but this is
        // used to display a more friendly name and avatar initials.
        // For all the Team Collection code, this should be the one place we know how to find the current user's first name.
        public static string CurrentUserFirstName =>
            _overrideCurrentUserFirstName ?? Bloom.Registration.Registration.Default.FirstName;

        // CurrentUser is the email address and is used as the key, but this is
        // used to display a more friendly name and avatar initials.
        // For all the Team Collection code, this should be the one place we know how to find the current user's surname.
        public static string CurrentUserSurname =>
            _overrideCurrentUserSurname ?? Bloom.Registration.Registration.Default.Surname;

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

        /// <inheritdoc />
        public void SendBookContentReload()
        {
            _webSocketServer.SendEvent("bookContent", "reload");
        }

        /// <summary>
        /// Disable most TC functionality under various conditions. Put a warning in
        /// the log. Virtual only so a test-only subclass can record when it's called relative to
        /// TeamCollection.SynchronizeRepoAndLocal, pinning WorkspaceModel's call-ordering fix for
        /// the "tier-timing" bug (see the ordering tests alongside
        /// TeamCollectionTierTimingTests).
        /// </summary>
        public virtual void CheckDisablingTeamCollections(CollectionSettings settings)
        {
            if (CurrentCollection == null)
                return; // already disabled, or not a TC
            string msg = null;
            string l10nId = null;
            var subscriptionForCheck = GetSubscriptionForDisablingCheck();
            var tcFeatureStatus = FeatureStatus.GetFeatureStatus(
                subscriptionForCheck,
                FeatureName.TeamCollection
            );
            if (!tcFeatureStatus.Enabled)
            {
                l10nId = "TeamCollection.DisabledForSubscriptionTier";
                msg =
                    $"Team Collections require a Bloom subscription tier of at least \"{tcFeatureStatus.SubscriptionTier}\".";
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
                if (
                    !FeatureStatus
                        .GetFeatureStatus(subscriptionForCheck, FeatureName.TeamCollection)
                        .Enabled
                )
                    (
                        CurrentCollectionEvenIfDisconnected as DisconnectedTeamCollection
                    ).DisconnectedBecauseOfSubscriptionTier = true;
            }
        }

        /// <summary>
        /// The Subscription value to use for the tier check above. Ordinarily that's just
        /// Settings.Subscription -- the in-memory CollectionSettings snapshot, which for a FOLDER
        /// Team Collection is already trustworthy by the time this runs (its collection files live
        /// in the same synchronously-read local folder with no sign-in or network round trip
        /// standing between it and the shared file: see FolderTeamCollection's own
        /// LastRepoCollectionFileModifyTime, a plain file-timestamp read).
        ///
        /// A cloud Team Collection is different: Settings is captured ONCE, synchronously, before
        /// this check ever runs (ProjectContext resolves TeamCollectionManager, which pulls fresh
        /// collection files from the repo, before it resolves the CollectionSettings that reads
        /// them -- see ProjectContext's CollectionSettings registration), and is never reloaded for
        /// the rest of the session. But pulling those collection files (the only thing that can
        /// deliver an up-to-date SubscriptionCode into that snapshot) requires a signed-in cloud
        /// session (CloudTeamCollection.CheckConnection short-circuits on !_auth.IsSignedIn) and a
        /// successful S3 download that can silently no-op on failure
        /// (CloudTeamCollection.DownloadCollectionFileGroup reports-and-swallows exceptions rather
        /// than propagating them). Depending on ambient state at that moment (a persisted auth
        /// token being ready yet, this machine's first-ever sync of this collection, a transient
        /// network hiccup), Settings.Subscription can therefore still be blank/stale here even
        /// though CurrentCollection is already non-null (CurrentCollection is deliberately set
        /// BEFORE the connect-and-sync sequence completes; see CreateTeamCollectionFromLink's
        /// caller). That is the "subscription-tier check timing" bug (GOING-LIVE.md Phase 5):
        /// CheckDisablingTeamCollections' only readiness gate, CurrentCollection == null, does not
        /// mean the same thing for cloud TCs that it does for folder ones.
        ///
        /// The fix: for a cloud TC, re-read the SubscriptionCode directly from the on-disk
        /// .bloomCollection file instead of trusting the stale in-memory snapshot. Combined with
        /// WorkspaceModel.HandleTeamStuffBeforeGetBookCollections deferring the cloud call of this
        /// check until AFTER the collection-file sync (SynchronizeRepoAndLocal) has had a chance to
        /// run, this makes the check deterministic: it reflects whatever the sync actually
        /// delivered, not a snapshot that predates it.
        /// </summary>
        private Subscription GetSubscriptionForDisablingCheck()
        {
            if (!(CurrentCollection is Cloud.CloudTeamCollection))
                return Settings.Subscription;
            try
            {
                var settingsPath = CollectionSettings.GetSettingsFilePath(_localCollectionFolder);
                return ProjectContext.GetCollectionSettings(settingsPath).Subscription;
            }
            catch (Exception)
            {
                // No usable local .bloomCollection file to re-read (shouldn't normally happen once
                // we get this far, since CurrentCollection being a CloudTeamCollection implies we
                // already found one) -- fall back to the in-memory snapshot rather than crash a
                // startup check.
                return Settings.Subscription;
            }
        }

        /// <summary>
        /// Returns true if registration is sufficient to use Team Collections; false otherwise
        /// </summary>
        public static bool IsRegistrationSufficient()
        {
            // We're normally checking Bloom.Registration.Registration.Default.Email,
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
