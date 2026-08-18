using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Bloom.Api;
using Bloom.MiscUI;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Collection
{
    /// <summary>
    /// A collection can declare, via the MinimumBloomVersion element of its .bloomCollection file,
    /// the oldest version of Bloom that is allowed to open it. This is in preparation for Cloud
    /// syncing: once a collection has been touched by a version of Bloom that knows how to sync it,
    /// letting an older Bloom loose on it could do real damage.
    ///
    /// At this point there is no UI for setting the flag; it has to be added to the file by hand.
    /// See BL-16690.
    /// </summary>
    public static class MinimumBloomVersionCheck
    {
        /// <summary>
        /// Decide whether this Bloom is allowed to open the given collection.
        /// </summary>
        /// <param name="settingsFilePath">Path to the .bloomCollection file</param>
        /// <param name="minimumVersion">What the collection asked for, as major.minor, when we say no</param>
        /// <returns>true if the collection demands a newer Bloom than we are</returns>
        public static bool IsThisBloomTooOld(string settingsFilePath, out string minimumVersion)
        {
            // When this is a Team Collection and we can actually read the repository, the repository
            // decides -- including when it says there is no requirement at all. It has to be able to
            // LIFT a requirement, not just impose one: a member who is being refused never opens the
            // collection, so the startup sync that would refresh their own copy never runs, and an
            // administrator's mistaken requirement would shut them out on every launch for ever,
            // fixable only by hand-editing the file on each machine.
            //
            // Otherwise -- an ordinary collection, or a shared folder we cannot reach right now --
            // we fall back to what we have: the file on this computer and anything the repository
            // told us earlier this session, taking whichever demands more. Not being able to see the
            // repository is not the same as the repository saying "no requirement", so in that case
            // we keep the protection rather than quietly dropping it.
            var declaredVersion =
                MinimumVersionInTeamCollectionRepo(settingsFilePath)
                ?? MoreDemandingOf(
                    ReadMinimumBloomVersion(settingsFilePath),
                    MinimumVersionLearnedFromRepo(settingsFilePath)
                );
            if (IsVersionSufficient(declaredVersion, RunningBloomVersion))
            {
                minimumVersion = "";
                return false;
            }
            // We can only get here if it parsed, since anything we can't parse counts as satisfied.
            minimumVersion = ToMajorMinor(Version.Parse(declaredVersion.Trim()));
            return true;
        }

        /// <summary>
        /// Compare a collection's declared minimum version against the version we are running.
        /// We compare only major and minor, like the other version gates in Bloom
        /// (BookStorage's feature requirements and BookDownload's minVersion), so a minimum of
        /// "6.5" is satisfied by any 6.5.x or later, and a build number in the minimum is ignored.
        ///
        /// The channel (Alpha/Beta/Release) deliberately plays no part: 6.5 means the same thing on
        /// every channel, so an alpha tester who is running ahead of the release is correctly let in.
        /// </summary>
        /// <remarks>Separated out from the file reading so it can be unit tested.</remarks>
        internal static bool IsVersionSufficient(string minimumVersion, Version runningVersion)
        {
            if (string.IsNullOrWhiteSpace(minimumVersion))
                return true; // the normal case: the collection doesn't care

            // Version.TryParse needs at least "major.minor", so a bare "6" lands here too.
            if (!Version.TryParse(minimumVersion.Trim(), out var requiredVersion))
            {
                // Someone hand-edited the file and mistyped. Locking them out of their own
                // collection over a typo would be worse than ignoring it, so just complain to the log.
                Logger.WriteEvent(
                    $"Ignoring unparseable {CollectionSettings.kMinimumBloomVersionElementName} '{minimumVersion}' in collection settings."
                );
                return true;
            }

            if (runningVersion.Major != requiredVersion.Major)
                return runningVersion.Major > requiredVersion.Major;
            return runningVersion.Minor >= requiredVersion.Minor;
        }

        /// <summary>
        /// What a Team Collection's repository says, read straight from the shared folder, or null
        /// for an ordinary collection or a repository we cannot reach.
        ///
        /// This is what closes the first-launch gap. The local copy of a Team Collection's settings
        /// is only refreshed from the repository later in startup, well after this gate, so a member
        /// whose administrator set a minimum version while they were closed would otherwise be
        /// judged on yesterday's file, let in, and only stopped the launch after that. Asking the
        /// repository directly costs one small read, and only for collections that are actually in
        /// a Team Collection. See BL-16690.
        /// </summary>
        private static string MinimumVersionInTeamCollectionRepo(string settingsFilePath)
        {
            var collectionFolder = System.IO.Path.GetDirectoryName(settingsFilePath);
            string repoSettings;
            try
            {
                // The shared folder may be on a network share that has gone away, where even asking
                // whether a folder exists can block for a long time -- and we are at the one moment
                // in startup with no window and nothing to show a user who is wondering why nothing
                // is happening. So give it a few seconds and no more. Not answering in time means
                // "we don't know", which falls back to the local copy, and the next launch will
                // very likely get a straight answer.
                var read = Task.Run(() =>
                    Bloom.TeamCollection.FolderTeamCollection.GetRepoCollectionSettingsForCollectionFolder(
                        collectionFolder
                    )
                );
                if (!read.Wait(TimeSpan.FromSeconds(5)))
                {
                    Logger.WriteEvent(
                        "Timed out reading the Team Collection repo settings while checking the minimum Bloom version; using the local copy instead."
                    );
                    return null;
                }
                repoSettings = read.Result;
            }
            catch (Exception ex)
            {
                Logger.WriteEvent(
                    $"Could not read the Team Collection repo settings while checking the minimum Bloom version: {ex.Message}"
                );
                return null;
            }

            if (string.IsNullOrWhiteSpace(repoSettings))
                return null;
            try
            {
                return ParseMinimumBloomVersion(repoSettings);
            }
            catch (Exception ex)
            {
                // Settings we can't parse tell us nothing; let the local file speak.
                Logger.WriteEvent(
                    $"Could not read {CollectionSettings.kMinimumBloomVersionElementName} from the Team Collection repo settings: {ex.Message}"
                );
                return null;
            }
        }

        /// <summary>
        /// Of two declared minimums, whichever demands more. Anything empty or unparseable counts as
        /// no requirement at all, the same rule IsVersionSufficient applies, so a typo in one source
        /// simply leaves the other to speak.
        /// </summary>
        private static string MoreDemandingOf(string one, string other)
        {
            var oneVersion = ParseOrNull(one?.Trim());
            var otherVersion = ParseOrNull(other?.Trim());
            if (oneVersion == null)
                return other;
            if (otherVersion == null)
                return one;
            return oneVersion >= otherVersion ? one : other;
        }

        /// <summary>
        /// Minimum versions we have learned from a Team Collection repository during this run, keyed
        /// by the collection's settings file path.
        ///
        /// Bloom deliberately does not rewrite local collection settings mid-session, so once an
        /// administrator sets a minimum version the file on this computer goes on saying nothing
        /// about it until the next startup sync. Without this record, someone we had just shut out
        /// could walk straight back in by choosing the same collection from the chooser: the gate
        /// would read the stale file, see no requirement, and open it. See BL-16690.
        ///
        /// Concurrent because the two writers are on different threads: the Team Collection startup
        /// sync runs on the progress dialog's background worker, while a repository change
        /// notification is handled on the UI thread.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            string,
            string
        > _minimumVersionsFromRepo = new System.Collections.Concurrent.ConcurrentDictionary<
            string,
            string
        >(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Record what the repository says this collection's minimum version is, which may be newer
        /// than what its file on this computer says.
        ///
        /// Pass the empty string when the repository declares no requirement. This record only
        /// matters when we cannot reach the shared folder at the moment of the check: if we can,
        /// IsThisBloomTooOld reads the repository afresh and that answer governs. When we cannot,
        /// this and the local file are all we have, and we take whichever demands more, so a
        /// withdrawal recorded here does not by itself cancel a requirement the local file still
        /// carries.
        /// </summary>
        public static void RememberMinimumVersionFromRepo(
            string settingsFilePath,
            string minimumVersion
        )
        {
            if (string.IsNullOrEmpty(settingsFilePath))
                return;
            _minimumVersionsFromRepo[NormalizePath(settingsFilePath)] = minimumVersion ?? "";
        }

        /// <summary>
        /// What the repository told us about this collection during this run, or null if it never
        /// did. Both null and "" end up meaning "no requirement from this source" once
        /// MoreDemandingOf has had them, so the distinction is only for the reader: null is silence,
        /// "" is the repository actively declaring no minimum.
        /// </summary>
        private static string MinimumVersionLearnedFromRepo(string settingsFilePath)
        {
            if (string.IsNullOrEmpty(settingsFilePath))
                return null;
            return _minimumVersionsFromRepo.TryGetValue(
                NormalizePath(settingsFilePath),
                out var version
            )
                ? version
                : null;
        }

        /// <summary>
        /// So that the same collection reached by two spellings of its path lands on one entry.
        /// The 8.3 short form matters here and GetFullPath does not expand it: the startup gate is
        /// handed a path that has already been through LongPathAware.GetLongPath, while what we
        /// record comes straight from the Team Collection's own folder, so the two could otherwise
        /// disagree and the remembered requirement would simply not be found.
        /// </summary>
        private static string NormalizePath(string path)
        {
            try
            {
                return Utils.LongPathAware.GetLongPath(System.IO.Path.GetFullPath(path));
            }
            catch (Exception)
            {
                // A path we can't even canonicalize is not going to match anything either way.
                return path;
            }
        }

        /// <summary>
        /// Read just the minimum version out of the settings file. We can't use CollectionSettings
        /// for this, because we have to answer the question before we commit to building a
        /// ProjectContext around the collection.
        /// </summary>
        internal static string ReadMinimumBloomVersion(string settingsFilePath)
        {
            try
            {
                if (!RobustFile.Exists(settingsFilePath))
                    return "";
                return ParseMinimumBloomVersion(
                    RobustFile.ReadAllText(settingsFilePath, Encoding.UTF8)
                );
            }
            catch (Exception ex)
            {
                // A settings file we can't even parse is a real problem, but this is not the place to
                // report it. Let the normal open fail and give the user its much better error report.
                Logger.WriteEvent(
                    $"Could not read {CollectionSettings.kMinimumBloomVersionElementName} from {settingsFilePath}: {ex.Message}"
                );
                return "";
            }
        }

        /// <summary>
        /// Pull the minimum version out of the text of a collection settings file. Separate from
        /// reading the file so that a Team Collection can ask about the copy in the repository, which
        /// lives inside a zip and has never been written to this computer.
        /// </summary>
        internal static string ParseMinimumBloomVersion(string settingsXml)
        {
            if (string.IsNullOrEmpty(settingsXml))
                return "";
            return CollectionSettings.ReadString(
                XElement.Parse(settingsXml),
                CollectionSettings.kMinimumBloomVersionElementName,
                ""
            );
        }

        /// <summary>
        /// Like IsThisBloomTooOld, but for settings we have in hand rather than in a file.
        /// </summary>
        public static bool IsThisBloomTooOldForSettings(
            string settingsXml,
            out string minimumVersion
        )
        {
            minimumVersion = "";
            string declaredVersion;
            try
            {
                declaredVersion = ParseMinimumBloomVersion(settingsXml);
            }
            catch (Exception ex)
            {
                Logger.WriteEvent(
                    $"Could not read {CollectionSettings.kMinimumBloomVersionElementName} from settings content: {ex.Message}"
                );
                return false;
            }
            if (IsVersionSufficient(declaredVersion, RunningBloomVersion))
                return false;
            minimumVersion = ToMajorMinor(Version.Parse(declaredVersion.Trim()));
            return true;
        }

        /// <summary>
        /// Tell the user that this collection needs a newer Bloom, and give them the two ways forward:
        /// upgrade, or open some other collection. Upgrading happens in place; if we can't manage it
        /// we say why and they are left to choose a different collection instead. We never send them
        /// off to the website.
        /// </summary>
        /// <returns>true if we downloaded an upgrade, in which case we have already asked Bloom to
        /// quit so it can be installed, and the caller must not start any more UI.</returns>
        public static bool ReportCollectionNeedsNewerBloom(
            string collectionName,
            string minimumVersion
        )
        {
            var header = LocalizationManager.GetString(
                "Collection.NewerVersionNeededHeader",
                "This collection needs a newer version of Bloom."
            );
            var explanation = string.Format(
                LocalizationManager.GetString(
                    "Collection.CollectionRequiresNewerVersion",
                    "The collection \"{0}\" requires Bloom {1} or greater. You are running Bloom {2}.",
                    "{0} is the name of the collection, {1} is the version it requires, {2} is the version of Bloom that is running."
                ),
                // The message is HTML, and a collection name can perfectly well contain an ampersand.
                System.Net.WebUtility.HtmlEncode(collectionName),
                minimumVersion,
                ToMajorMinor(RunningBloomVersion)
            );
            var upgradeButtonText = LocalizationManager.GetString(
                "Collection.UpgradeBloom",
                "Upgrade Bloom"
            );
            var chooseOtherButtonText = LocalizationManager.GetString(
                "Collection.OpenDifferentCollection",
                "Open a Different Collection"
            );

            // Order matters: the buttons appear left to right in this order, and the default one is
            // drawn filled and takes the initial focus. Upgrading is what we actually want the user
            // to do, so it goes last (the rightmost, primary position) and is the default.
            // No external-link icon on this button: this upgrades Bloom in place and never opens a
            // browser, so marking it as leaving Bloom would simply be wrong.
            var buttons = new[]
            {
                new MessageBoxButton() { Text = chooseOtherButtonText, Id = "chooseOther" },
                new MessageBoxButton()
                {
                    Text = upgradeButtonText,
                    Id = kUpgradeButtonId,
                    Default = true,
                },
            };

            var result = BloomMessageBox.Show(
                null,
                $"<strong>{header}</strong><br/><br/>{explanation}",
                buttons,
                MessageBoxIcon.Warning
            );

            if (result == kUpgradeButtonId && UpgradeBloom(minimumVersion))
            {
                ProgramExit.Exit();
                return true;
            }
            // Either they asked for a different collection, or they wanted to upgrade and we could
            // not manage it -- in which case we have already explained why. Either way the caller
            // takes over, and puts the collection chooser up.
            return false;
        }

        private const string kUpgradeButtonId = "upgrade";

        /// <summary>
        /// The collection we are in the middle of shutting someone out of, if any. Keyed by name
        /// rather than a plain flag for two reasons: the repository can report the same change more
        /// than once, including while Bloom is shutting down, and we must not stack up dialogs; but
        /// if the user moves on to a *different* collection in the same session, that one deserves
        /// its own lock-out.
        /// </summary>
        private static string _collectionBeingLockedOut;

        /// <summary>
        /// Shut the user out of a collection they already have open, because a minimum version they
        /// don't meet has just arrived -- in practice, a Team Collection administrator set one while
        /// they were working. They get the same dialog as at startup, and the same two ways out:
        /// upgrade, or go to a different collection. There is deliberately no third option: the
        /// dialog has no close box, and cancelling the collection chooser brings the dialog back
        /// rather than dropping them into a collection this Bloom is not allowed to touch.
        /// </summary>
        /// <returns>false if a lock-out for this collection is already under way, so the caller
        /// should carry on as usual rather than assume the collection is being torn down.</returns>
        public static bool LockUserOutOfOpenCollection(string collectionName, string minimumVersion)
        {
            if (_collectionBeingLockedOut == collectionName)
                return false;
            _collectionBeingLockedOut = collectionName;

            if (ReportCollectionNeedsNewerBloom(collectionName, minimumVersion))
                return true; // upgrading; Bloom is already on its way down

            // They want a different collection -- or they wanted to upgrade and we couldn't manage
            // it, and have already been told why. This is the same route as Open/Create Collection
            // on the toolbar: it closes the current one and puts up the chooser.
            if (Program.ChooseACollection(Shell.GetShellOrOtherOpenForm() as Shell))
                return true;

            // They cancelled the chooser. Staying in this collection is the one thing they can't
            // do -- but quitting must stay possible. Someone with nothing newer to upgrade to and
            // no other collection to open would otherwise have no way out of Bloom at all except
            // Task Manager, since this dialog deliberately has no close box. Cancelling here means
            // "none of the above", which is exactly how the startup path reads it too.
            ProgramExit.Exit();
            return true;
        }

        /// <summary>
        /// Called when a collection has actually been opened, which ends any lock-out we were in
        /// the middle of. The flag has to be cleared by something outside the lock-out itself:
        /// clearing it on the way out would let a repeat notification (they arrive during shutdown)
        /// put the dialog straight back up.
        /// </summary>
        public static void NoteCollectionOpened()
        {
            _collectionBeingLockedOut = null;
        }

        /// <summary>
        /// Get the user onto a newer Bloom, using exactly the machinery the "an update is available"
        /// toast drives, and show them what it is doing while it happens.
        /// </summary>
        /// <returns>true if a new Bloom was downloaded and the caller should now shut Bloom down so
        /// it can be installed.</returns>
        private static bool UpgradeBloom(string minimumVersion)
        {
            using (var reporter = RunTheUpdateBehindAProgressDialog(minimumVersion))
            {
                if (reporter.Outcome != UpdateAttemptOutcome.Downloaded)
                    return false;

                // Someone who pressed Cancel does not get restarted, even if the download turned
                // out to have finished in the same moment. Without this the two race: the wait
                // gives up on a cancel, and by the time we look at the outcome it says Downloaded,
                // so Bloom would quit and reinstall itself under a user who had just said no.
                if (reporter.UserCancelled)
                    return false;

                // Ask for the restarting flavour of the install: the user asked to be upgraded, so
                // leaving them at a closed program to start again themselves would be a poor end to
                // it. This also gets Velopack's own progress bar while it installs.
                ApplicationUpdateSupport.ArrangeToApplyUpdateAndRestart();
                return true;
            }
        }

        /// <summary>
        /// The last thing the progress dialog says before the user closes it. Everything else it
        /// has to say the update code has already said through the reporter, including how any
        /// failure was explained -- these are the two endings the update code has no words for,
        /// because only we know what this collection was asking for.
        /// </summary>
        private static void SayHowTheUpgradeEnded(
            ProgressUpdateReporter reporter,
            string minimumVersion
        )
        {
            switch (reporter.Outcome)
            {
                case UpdateAttemptOutcome.Downloaded:
                    // It installs whatever version it turns out to be. Velopack can only offer the
                    // newest build on this user's channel -- it has no notion of "at least version
                    // X" -- so what we have may still be short of what this collection needs. Take
                    // it anyway: getting as far as the channel allows is real progress. We
                    // deliberately do not say anything about whether it went far enough. If it
                    // didn't, the upgraded Bloom meets this same dialog next launch, still asking
                    // for the same version, and the user can decide again from there. That is a
                    // little confusing and much simpler than a second message explaining a case
                    // most people will never be in.
                    reporter.Say(
                        string.Format(
                            LocalizationManager.GetString(
                                "Collection.UpgradeDownloaded",
                                "Bloom {0} has been downloaded. Bloom will now close, install it, and start up again.",
                                "{0} is the version number of the Bloom that was downloaded."
                            ),
                            reporter.DownloadedVersion
                        )
                    );
                    break;
                case UpdateAttemptOutcome.NothingNewer:
                    // Being specific matters: "your Bloom is up to date", which is what the update
                    // code says here, would be a baffling thing to read right after being told this
                    // Bloom is too old.
                    reporter.Say(
                        string.Format(
                            LocalizationManager.GetString(
                                "Collection.NothingNewerAvailable",
                                "This collection needs Bloom {0}, but there is no newer Bloom available to you yet. You are already on the newest one for the kind of Bloom you have installed.",
                                "{0} is the version the collection requires."
                            ),
                            minimumVersion
                        )
                    );
                    break;
            }
        }

        /// <summary>
        /// The same three refusals, in the same words, that WorkspaceView.CheckForUpdatesImpl
        /// applies before it drives this machinery from the menu. They belong to the caller rather
        /// than to the update code, so we have to repeat them, and the user is owed the same
        /// explanation whichever route they came by.
        /// </summary>
        private static bool CanThisBloomUpdateItself(out string whyNot)
        {
            if (Debugger.IsAttached)
            {
                whyNot = "Sorry, you cannot check for updates from the debugger.";
                return false;
            }
            if (InstallerSupport.SharedByAllUsers())
            {
                whyNot = LocalizationManager.GetString(
                    "CollectionTab.AdminManagesUpdates",
                    "Your system administrator manages Bloom updates for this computer."
                );
                return false;
            }
            if (ApplicationUpdateSupport.IsDev)
            {
                whyNot =
                    "Checking for updates is disabled on developer builds. No relevant channel.";
                return false;
            }
            whyNot = null;
            return true;
        }

        /// <summary>
        /// How long we are prepared to watch an update that never reports back. This is not a
        /// judgement about how long an upgrade should take -- it is a ninety-megabyte download, and
        /// some of our users are on links where that really does take hours, which is what the
        /// Cancel button is for. It is only here so that a download that has genuinely wedged cannot
        /// leave someone with a progress dialog, no collection, and no way out of Bloom.
        /// </summary>
        private static readonly TimeSpan kHowLongToWaitForAnUpdate = TimeSpan.FromHours(2);

        /// <summary>
        /// Run the ordinary update, showing the user what it is doing in a progress dialog.
        ///
        /// The dialog is here because this runs before any collection is open. The update code says
        /// what it is doing through toasts, and toasts are drawn by ToastHost, which is mounted only
        /// in the main workspace -- so on this path they would go nowhere at all, and the user would
        /// watch an empty screen for the length of a large download. Handing the update code a
        /// ProgressUpdateReporter instead puts those very same sentences, plus Velopack's
        /// percentage, into a dialog.
        ///
        /// It is also the only window we get. A second one -- a message box afterwards to say how it
        /// went -- does not come to the front and has no taskbar button of its own (ReactDialog sets
        /// ShowInTaskbar false, which is right for a dialog with a parent window and leaves an
        /// orphan at startup with nowhere to be). By the time it opens, this dialog has closed and
        /// Bloom has no foreground window left to hand the activation on from, so the message ends
        /// up behind whatever the user was doing and Bloom looks like it has hung. So everything
        /// gets said here, in the window that is already in front, and the user closes it when they
        /// have read it.
        /// </summary>
        private static ProgressUpdateReporter RunTheUpdateBehindAProgressDialog(
            string minimumVersion
        )
        {
            // Built before the dialog, not inside its worker, so that the caller always gets a
            // reporter to ask about. If the dialog never runs its worker body, this one still
            // answers -- as a failure with nothing to say, which lands the user back at the
            // collection chooser rather than on a null reference.
            var reporter = new ProgressUpdateReporter();

            // This dialog is reached from two quite different places, and they differ in whether a
            // websocket server already exists.
            //
            // At startup there is none: Instance is set by the ProjectContext of a collection, and
            // this gate runs before we build one. So we make a server for the duration, as
            // WorkspaceView does for the language chooser (BL-15230).
            //
            // Mid-session there is, because a Team Collection's repository has started demanding a
            // newer Bloom while the member has the collection open. Standing up a second listener
            // on the port the first one holds throws, and BloomWebSocketServer.Init answers a
            // SocketException by telling the user Bloom "cannot start properly" and quitting -- so
            // there we use the server that is already running.
            //
            // Instance being non-null is a trustworthy test of that only because Dispose now clears
            // it; otherwise a collection the user had closed would leave it pointing at a dead
            // server, and this dialog would wait forever for a reply that could never come.
            if (BloomWebSocketServer.Instance != null)
            {
                ShowTheDialog(BloomWebSocketServer.Instance, reporter, minimumVersion);
                return reporter;
            }

            using (var socketServer = new BloomWebSocketServer())
            {
                socketServer.Init(BloomServer.WebSocketPort.ToString(CultureInfo.InvariantCulture));
                ShowTheDialog(socketServer, reporter, minimumVersion);
            }
            // Disposing it clears Instance, so the next collection's ProjectContext starts clean.

            return reporter;
        }

        /// <summary>
        /// Put up the progress dialog and run the update inside it, reporting through
        /// <paramref name="reporter"/>. Returns when the dialog has closed.
        /// </summary>
        private static void ShowTheDialog(
            Bloom.web.IBloomWebSocketServer socketServer,
            ProgressUpdateReporter reporter,
            string minimumVersion
        )
        {
            BrowserProgressDialog.DoWorkWithProgressDialog(
                socketServer,
                () =>
                    new ReactDialog(
                        "progressDialogBundle",
                        new
                        {
                            // The same words as the button they just clicked.
                            title = LocalizationManager.GetString(
                                "Collection.UpgradeBloom",
                                "Upgrade Bloom"
                            ),
                            titleColor = "white",
                            titleBackgroundColor = Palette.kBloomBlueHex,
                            showReportButton = "never",
                            showCancelButton = true,
                            // Velopack tells us how far along the download is, so show a
                            // real bar rather than a spinner that says nothing.
                            determinate = true,
                            linearProgress = true,
                        },
                        "Upgrade Bloom"
                    )
                    {
                        Width = 620,
                        // Only a few lines ever appear here, and 400 left half the dialog
                        // empty. The message area scrolls, so a failure with more to say
                        // still fits.
                        Height = 260,
                    },
                (progress, worker) =>
                {
                    reporter.WriteTo(progress);
                    if (!CanThisBloomUpdateItself(out var whyNot))
                    {
                        reporter.SayProblem(whyNot, null);
                        reporter.Finished(UpdateAttemptOutcome.CannotUpdateThisBloom, null, whyNot);
                        return true;
                    }

                    ApplicationUpdateSupport.CheckForAVelopackUpdate(
                        ApplicationUpdateSupport.BloomUpdateMessageVerbosity.Quiet,
                        restartBloom: null, // nothing to click here, and we quit ourselves
                        reporter: reporter,
                        userHasAlreadyAgreedToUpdate: true // they clicked Upgrade Bloom
                    );
                    if (!WaitForTheUpdate(reporter, worker))
                    {
                        // The user clicked Cancel, or we gave up waiting. Either way they want to be
                        // rid of this window, not asked to close it again.
                        return false;
                    }

                    SayHowTheUpgradeEnded(reporter, minimumVersion);
                    // Leave the dialog up, with a Close button, so the user can read how it went. It
                    // is the only window we have that is reliably in front.
                    return true;
                }
            );
        }

        /// <summary>
        /// Block the progress dialog's worker until the update reports back, watching for a click on
        /// Cancel as we go.
        ///
        /// Cancelling stops us watching; it does not stop the download, which Velopack goes on with
        /// in the background and applies when Bloom exits. That is exactly what an ordinary
        /// background update does, so there is nothing here to undo.
        /// </summary>
        /// <returns>true if the update reported back; false if the user cancelled or we gave up</returns>
        private static bool WaitForTheUpdate(
            ProgressUpdateReporter reporter,
            BackgroundWorker worker
        )
        {
            var deadline = DateTime.UtcNow + kHowLongToWaitForAnUpdate;
            while (DateTime.UtcNow < deadline)
            {
                if (reporter.WaitForFinish(TimeSpan.FromMilliseconds(250)))
                {
                    // A cancel that arrived while we were in that wait still counts. The user
                    // pressed it before they could possibly know the download had just landed, and
                    // taking the download's word for it here is what would restart Bloom under
                    // someone who had said no. Report it as a cancel, so they are not told we are
                    // about to close and restart either.
                    if (worker.CancellationPending)
                    {
                        reporter.NoteUserCancelled();
                        return false;
                    }
                    return true;
                }
                if (worker.CancellationPending)
                {
                    reporter.NoteUserCancelled();
                    return false;
                }
            }
            Logger.WriteEvent(
                "Gave up waiting for the Velopack update started from the minimum version dialog."
            );
            return false;
        }

        private static Version ParseOrNull(string version) =>
            Version.TryParse(version ?? "", out var v) ? v : null;

        /// <summary>
        /// Format a version the way we actually compare it: major and minor only. Showing the user a
        /// build number we don't look at would misrepresent the rule, and invites them to compare
        /// the wrong digits ("6.5 required, but I have 6.4.900, and 900 is more than 5").
        /// </summary>
        private static string ToMajorMinor(Version version) => $"{version.Major}.{version.Minor}";

        /// <summary>
        /// The version of Bloom we are running. We use the assembly version rather than
        /// Application.ProductVersion because the latter can carry a non-numeric suffix.
        /// A loaded assembly always has a version, so we don't guard against it being missing:
        /// any fallback we picked would be a lie, and a low one would lock the user out of every
        /// collection that declares a minimum -- the opposite of what this class is careful to do.
        /// </summary>
        private static Version RunningBloomVersion =>
            typeof(MinimumBloomVersionCheck).Assembly.GetName().Version;
    }
}
