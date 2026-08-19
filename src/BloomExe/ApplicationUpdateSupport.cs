using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Properties;
using Bloom.web;
using DesktopAnalytics;
using L10NSharp;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
#if !__MonoCS__
using Bloom.ErrorReporter;
using SIL.Reporting;
using Velopack;
#endif

namespace Bloom
{
    /// <summary>
    /// This class contains code to work with the Velopack package to handle automatic updating of
    /// Bloom to new versions. The key methods are called from WorkspaceView when Bloom is first idle or
    /// when the user requests an update.
    /// </summary>
    static class ApplicationUpdateSupport
    {
#if !__MonoCS__
        internal static UpdateManager _bloomUpdateManager;
        private static UpdateInfo _newVersion;
#endif

        internal enum BloomUpdateMessageVerbosity
        {
            Quiet,
            Verbose,
        }

        enum UploadStatus
        {
            // First call this session, or previous call(s) completed and found no updates
            // Transition to LookingForUpdates when we start looking
            NothingKnown,

            // gathering information
            // Transition to
            // - NothingKnown if we find no updates
            // - FoundUpdates if we find updates and autoupdate is off
            // - Downloading if we find updates and autoupdate is on
            LookingForUpdates,

            // We found updates, and autoupdate is off, so we are waiting for the user to say
            // whether to download and install them.
            // Transition to Downloading if the user agrees to go ahead.
            FoundUpdates,

            // Doing the download.
            // Transition to DownloadedWaitingForRestart when done.
            Downloading,

            // We have downloaded the updates and are waiting for the user to quit or restart Bloom
            // Never leaves this state until Bloom quits, at which point we install the new version.
            // If the user clicked a Restart toast, Bloom will restart automatically after the install.
            DownloadedWaitingForRestart,

            // something went wrong. We don't allow more tries this session.
            Failed,
        }

        static UploadStatus _status = UploadStatus.NothingKnown;
        private static Exception _updateException;

        /// <summary>
        /// Guards _status and everything decided from it. See the comment in CheckForAVelopackUpdate
        /// about why one lock is needed now that two threads can ask for an update at once.
        /// </summary>
        private static readonly object _statusLock = new object();

        /// <summary>
        /// Everyone watching the attempt currently in flight, or null when there is none. A second
        /// caller joins this rather than being turned away.
        /// </summary>
        private static AttemptWatchers _watchers;

        /// <summary>
        /// What to do about a request to check for updates, decided while holding _statusLock and
        /// carried out after releasing it.
        /// </summary>
        private enum WhatToDoAboutThisRequest
        {
            /// No attempt was in flight, so this request is the attempt.
            RunIt,

            /// One is in flight; try to watch it too, once the lock is released.
            TryToJoinTheOneInFlight,

            /// One was in flight and this caller is now watching it too.
            JoinedTheOneInFlight,

            /// It finished between our looking and our joining.
            SayItIsAlreadyBusy,

            /// We know of an update and the user has already agreed to it.
            DownloadWhatWeFound,

            /// We know of an update and should ask whether they want it.
            OfferWhatWeFound,

            /// It is downloaded already and installs when Bloom exits.
            SayItIsAlreadyDownloaded,

            /// An earlier attempt failed and we do not try again this session.
            SayWeGaveUpEarlier,
        }

        /// <summary>
        /// The attempt is over, so a later request starts a fresh one rather than joining watchers
        /// who have already been told how this one ended.
        /// </summary>
        private static void LetGoOfTheAttempt()
        {
            lock (_statusLock)
            {
                _watchers = null;
            }
        }

        /// <summary>
        /// True once we have arranged to install a downloaded update as Bloom exits. We must only do
        /// that once: applying the same update twice is how an upgrade fails, or restarts Bloom when
        /// nobody asked it to.
        /// </summary>
        private static bool _willInstallUpdateOnExit;

        // These three are all "this should not happen" cases, so we have never localized them.
        // They are constants only because each is said twice: once to the user, and once to the
        // caller through UpdateReporter.Finished, so that a caller which has to repeat it says
        // exactly what we said.
        private const string kRestartToTryAgainMessage =
            "Restart Bloom to try checking for updates again";
        private const string kUnableToCheckMessage =
            "Bloom was unable to check for updates. Restart to try again.";
        private const string kUnableToDownloadMessage =
            "Bloom was unable to download and install updates. Restart to try again.";

        /// <summary>
        /// See if any updates are available and if approved, download them. Once they are ready a notification
        /// pops up and the user can restart Bloom to run the new version. (Or if you don't, they will get installed
        /// when Bloom quits.)
        /// </summary>
        /// <param name="verbosity">Quiet if we are called from a timer; verbose if the user requested the check.</param>
        /// <param name="restartBloom">An action that is executed if the user clicks the toast that suggests
        /// a restart. This is the responsibility of the caller (the workspace view). It
        /// just shuts down Bloom; the update and restart are managed automatically by Velopack.</param>
        /// <param name="reporter">Where to say what is happening, and where to report the outcome.
        /// Toasts, if not given, which is the normal case.</param>
        /// <param name="userHasAlreadyAgreedToUpdate">Skip the "updates are available, do you want
        /// them?" step: the user has said yes somewhere else, so asking again would be odd.</param>
        internal static async void CheckForAVelopackUpdate(
            BloomUpdateMessageVerbosity verbosity,
            Action restartBloom,
            UpdateReporter reporter = null,
            bool userHasAlreadyAgreedToUpdate = false
        )
        {
            reporter = reporter ?? new ToastUpdateReporter();
            // In Bloom 6.3, we updated to DotNet 8. So at this point, there's no reason to check OS versions;
            // This Velopack-based update code only runs in 6.3, and 6.3 (at least by the time we release a beta)
            // only runs on an OS that is at least 10; in fact, it has to be quite a recent 10. But that check
            // needs to be in the old Squirrel code in 6.2, to prevent updating to 6.3 at all. If we're in 6.3,
            // we don't currently need to check OS version.
            // I'm keeping the old code to remember the last state of things for version checking. Sometime, 6.3
            // might need to check that someone has at least Windows 12 before updating to 7.0 or 8.0!
            // Earlier code just gave up without any message if the OS was too old.
            // The code here is written to display the "you are up to date" message, with the thought that
            // the user is as up-to-date as they can be on this OS. But that may not be true; we have no
            // way to know whether they are at the latest version for their OS. If JohnH agrees with my
            // idea that we should always say something when the user asks to check for updates, it should
            // be something like, "Bloom cannot automatically update to the latest version because your
            // operating system is too old to run it." It would be nice if we knew whether they have the
            // latest version for their OS, nicer still if we could update to it, but neither is currently
            // possible. Conceivably we could have a click action that opens Bloom's downloads page at the
            // section about latest version for each OS.

            // Here is the old code and earlier comment.
            // In early 2023, MS stopped updating WebView2 for Windows 7, 8, and 8.1. So for 5.4, we would like to "just get the latest 5.4".
            // But at the moment, we aren't investing in that. We're just stranding these users at whatever 5.4 version they have.
            // The other checks are for paranoia. We should not be calling this in those cases.
            //if (
            //    Environment.OSVersion.Version.Major < 10
            //    || InstallerSupport.SharedByAllUsers()
            //    || IsDev
            //)
            //{
            //    ShowToastForUpToDate(verbosity);
            //    return;
            //}

#if !__MonoCS__
            // Decide what to do, and claim the attempt, under the lock; then act outside it. Two
            // threads really do arrive here -- the workspace's timer and its Check For Updates menu
            // run on the UI thread, while the minimum-version dialog runs on a progress dialog's
            // background worker -- and everything they are deciding from (_status, _watchers,
            // _newVersion, _bloomUpdateManager) is static. Reading a state and then transitioning it
            // has to be one step, or both callers can pass the same gate. Nothing that touches the UI
            // or awaits happens while the lock is held.
            WhatToDoAboutThisRequest whatToDo;
            AttemptWatchers watchers;
            lock (_statusLock)
            {
                switch (_status)
                {
                    case UploadStatus.NothingKnown:
                        // Ours to run. Claim it here rather than after the update URL lookup, so a
                        // second caller arriving during that lookup joins us instead of starting a
                        // rival attempt.
                        _status = UploadStatus.LookingForUpdates;
                        _watchers = new AttemptWatchers(reporter);
                        whatToDo = WhatToDoAboutThisRequest.RunIt;
                        break;
                    case UploadStatus.LookingForUpdates:
                    case UploadStatus.Downloading:
                        // An attempt is already in flight. Join it: the caller then sees its
                        // messages, its percentage, and can cancel it, instead of being told Bloom
                        // is busy and sent away from the upgrade they just asked for. The joining
                        // itself happens after the lock, because it replays what has been said so
                        // far and that reaches the UI.
                        whatToDo =
                            _watchers == null
                                ? WhatToDoAboutThisRequest.SayItIsAlreadyBusy
                                : WhatToDoAboutThisRequest.TryToJoinTheOneInFlight;
                        break;
                    case UploadStatus.FoundUpdates:
                        whatToDo = userHasAlreadyAgreedToUpdate
                            ? WhatToDoAboutThisRequest.DownloadWhatWeFound
                            : WhatToDoAboutThisRequest.OfferWhatWeFound;
                        break;
                    case UploadStatus.DownloadedWaitingForRestart:
                        whatToDo = WhatToDoAboutThisRequest.SayItIsAlreadyDownloaded;
                        break;
                    default: // Failed
                        whatToDo = WhatToDoAboutThisRequest.SayWeGaveUpEarlier;
                        break;
                }
                watchers = _watchers;
            }

            if (whatToDo == WhatToDoAboutThisRequest.TryToJoinTheOneInFlight)
            {
                // Outside the lock, because catching the joiner up on what it missed reaches the UI.
                // Add says no if the attempt reported its outcome in the meantime, in which case
                // there is nothing to join and we fall through to saying Bloom is busy.
                whatToDo = watchers.Add(reporter)
                    ? WhatToDoAboutThisRequest.JoinedTheOneInFlight
                    : WhatToDoAboutThisRequest.SayItIsAlreadyBusy;

                if (
                    whatToDo == WhatToDoAboutThisRequest.JoinedTheOneInFlight
                    && verbosity == BloomUpdateMessageVerbosity.Verbose
                    && !reporter.WantsCatchingUp
                )
                {
                    // Someone who asked in as many words -- Help > Check For Updates -- gets an
                    // answer now, as they always did, rather than silence until the attempt they
                    // joined finishes minutes later. A joiner that wants catching up has just been
                    // given the backlog instead, which is a better answer than this one.
                    reporter.Say(
                        _status == UploadStatus.Downloading && _newVersion != null
                            ? DownloadingMessage()
                            : AlreadyCheckingMessage()
                    );
                }
            }

            switch (whatToDo)
            {
                case WhatToDoAboutThisRequest.JoinedTheOneInFlight:
                    // Nothing more to do. The attempt we joined will tell this reporter how it ends.
                    return;
                case WhatToDoAboutThisRequest.SayWeGaveUpEarlier:
                    // Hopefully we don't get into this state.
                    ReportFailure(reporter, kRestartToTryAgainMessage);
                    return;
                case WhatToDoAboutThisRequest.SayItIsAlreadyBusy:
                    // Only reachable if the in-flight attempt finished between our two locks.
                    if (verbosity == BloomUpdateMessageVerbosity.Verbose)
                        reporter.Say(AlreadyCheckingMessage());
                    reporter.Finished(UpdateAttemptOutcome.Failed, null, AlreadyCheckingMessage());
                    return;
                case WhatToDoAboutThisRequest.DownloadWhatWeFound:
                    // The user has already said yes elsewhere, so asking again by toast would be odd.
                    DownloadAndApplyUpdates(restartBloom, reporter);
                    return;
                case WhatToDoAboutThisRequest.OfferWhatWeFound:
                    // Conceivably the toast is still up, but it is harmless to show it again.
                    // `reporter` is still the caller's own here: the switch runs before we adopt the
                    // watcher list.
                    OfferFoundUpdates(reporter, reporter, restartBloom);
                    reporter.Finished(UpdateAttemptOutcome.Offered, null, null);
                    return;
                case WhatToDoAboutThisRequest.SayItIsAlreadyDownloaded:
                    OfferRestartToApplyDownload(
                        reporter,
                        _newVersion.TargetFullRelease.Version.ToString(),
                        restartBloom
                    );
                    // Already downloaded and already arranged to install on exit, so as far as the
                    // caller is concerned this is a success: quitting will install it.
                    reporter.Finished(
                        UpdateAttemptOutcome.Downloaded,
                        _newVersion?.TargetFullRelease?.Version?.ToString(),
                        null
                    );
                    return;
            }

            // From here on we are the attempt, and we SAY things to everyone watching it rather than
            // only to the reporter we were handed. But we keep hold of that one: anything we hand
            // onwards as "the caller" must be an individual reporter, never this list, or it ends up
            // being asked to watch itself.
            var callersOwnReporter = reporter;
            reporter = watchers;

            try
            {
                // We do not yet know of any updates. See if there are any.
                // (Conceivably, we could have found updates, but an even newer version has been
                // released since we checked. But if
                // we support checking again, we have to deal with the possibility that we already
                // downloaded updates for the version we found out about before. A very complicated
                // bit of code gets even more so. I decided that if we've detected a new version,
                // we won't actually look again during this run.)

                if (!GetUpdateUrl(verbosity, reporter, out var updateUrl))
                {
                    // Overwhelmingly the reason we can't work out where to look is that we can't
                    // reach the server. Give the attempt back, so the user can try again.
                    lock (_statusLock)
                    {
                        _status = UploadStatus.NothingKnown;
                    }
                    reporter.Finished(UpdateAttemptOutcome.Failed, null, CannotConnectMessage());
                    LetGoOfTheAttempt();
                    return;
                }

                // _status became LookingForUpdates when we claimed the attempt, above.
                var options = new UpdateOptions
                {
                    // this number is arbitrary. We just want to speed up alpha (and maybe beta?) channel updates.
                    MaximumDeltasBeforeFallback = 2,
                };
                _bloomUpdateManager = new UpdateManager(updateUrl, options);
                _newVersion = await _bloomUpdateManager.CheckForUpdatesAsync();
                if (_newVersion == null)
                {
                    if (verbosity == BloomUpdateMessageVerbosity.Verbose)
                    {
                        // Only say this if the user manually initiated the check.
                        reporter.Say(UpToDateMessage());
                    }
                    _bloomUpdateManager = null; // no updates, so no need to keep this object around
                    lock (_statusLock)
                    {
                        _status = UploadStatus.NothingKnown; // allows user to try again
                    }
                    reporter.Finished(UpdateAttemptOutcome.NothingNewer, null, null);
                    LetGoOfTheAttempt();
                    return;
                }

                // There are updates available. If the user is not installing updates automatically,
                // ask whether to download them -- unless they have already said yes somewhere else.
                if (!Settings.Default.AutoUpdate && !userHasAlreadyAgreedToUpdate)
                {
                    lock (_statusLock)
                    {
                        _status = UploadStatus.FoundUpdates;
                    }
                    // Show the offer to everyone watching, but if it is accepted, start the download
                    // for the individual who asked -- not for the watcher list itself.
                    OfferFoundUpdates(reporter, callersOwnReporter, restartBloom);
                    reporter.Finished(UpdateAttemptOutcome.Offered, null, null);
                    // Nothing is in flight while an offer sits unanswered, so let go of the watcher
                    // list: whoever accepts starts a new attempt with a new one. A caller arriving
                    // meanwhile does not need to join anything -- the FoundUpdates branch of the
                    // switch above already gives them the update we found.
                    LetGoOfTheAttempt();
                    return;
                }
            }
            catch (Exception e)
            {
                // Hopefully this is very rare. But we do want some indication of a problem if we
                // can't get updates.
                // Review: should we go straight to "NotifyUserOfProblem" if verbosity
                // is verbose (i.e., called by Check for Updates user action)?
                ReportFailure(reporter, kUnableToCheckMessage, e);
                LetGoOfTheAttempt();
                return;
            }

            // If autoupdate is true, we just go ahead and download the updates. Hand it the caller's
            // own reporter, not the watcher list -- it resolves the list for itself.
            DownloadAndApplyUpdates(restartBloom, callersOwnReporter);
#endif
        }

        /// <param name="reporter">Whoever wants to know: always an INDIVIDUAL reporter, never a
        /// watcher list. This method resolves the list for the attempt itself, and a list asked to
        /// watch itself throws. Every caller therefore passes the reporter it was originally handed,
        /// not the one it may since have swapped in for saying things to everybody.</param>
        private static async void DownloadAndApplyUpdates(
            Action restartBloom,
            UpdateReporter reporter
        )
        {
#if !__MonoCS__
            {
                // One download at a time. The switch in CheckForAVelopackUpdate guards the way in,
                // but not this method, which the "Update Now" toast also calls straight from its
                // click, so decide and transition here under the same lock.
                UploadStatus wasAlready;
                AttemptWatchers joinable;
                bool weAreTheDownload;
                lock (_statusLock)
                {
                    wasAlready = _status;
                    joinable = _watchers;
                    weAreTheDownload =
                        _status != UploadStatus.Downloading
                        && _status != UploadStatus.DownloadedWaitingForRestart;
                    if (weAreTheDownload)
                    {
                        _status = UploadStatus.Downloading;
                        // Keep the attempt's existing watcher list if there is one. Reaching here
                        // with one means the check we already claimed is now becoming its download --
                        // the same attempt, so the same watchers, and anyone who joined during the
                        // check must not be dropped. (Doing exactly that made the dialog sit there
                        // saying nothing, which is the case this whole change exists to fix.)
                        //
                        // It is null when nothing was in flight: a toast click, or an offer being
                        // taken up. Then the caller who asked is the first watcher of a fresh list,
                        // which is what keeps a previous attempt's stop-signal and replay log from
                        // leaking into this one.
                        if (_watchers == null)
                            _watchers = new AttemptWatchers(reporter);
                        joinable = _watchers;
                    }
                }

                if (wasAlready == UploadStatus.DownloadedWaitingForRestart)
                {
                    // Not a failure: it is already downloaded and already arranged to install when
                    // Bloom exits. Telling the caller otherwise would send someone who asked to be
                    // upgraded away to pick another collection, when the new Bloom is sitting ready.
                    reporter.Finished(
                        UpdateAttemptOutcome.Downloaded,
                        _newVersion?.TargetFullRelease?.Version?.ToString(),
                        null
                    );
                    return;
                }
                if (!weAreTheDownload)
                {
                    // Someone else's download is already running: watch it rather than start a
                    // second one. Joining happens outside the lock, since it reaches the UI.
                    if (joinable != null && joinable.Add(reporter))
                        return;
                    reporter.Say(DownloadingMessage());
                    reporter.Finished(UpdateAttemptOutcome.Failed, null, AlreadyCheckingMessage());
                    return;
                }

                // We are the download. Make sure whoever asked for it is actually among the watchers
                // before we start talking to the list instead of to them -- on the "an update was
                // found earlier and is now being accepted" path the list predates this caller, and
                // leaving them out of it left them watching a window that never moved.
                joinable.Add(reporter);
                reporter = joinable;
            }

            try
            {
                reporter.Say(DownloadingMessage());

                await _bloomUpdateManager.DownloadUpdatesAsync(
                    _newVersion,
                    reporter.Percent,
                    reporter.CancellationToken
                );

                // The transfer can finish in the very instant the user cancels, in which case the
                // await returns normally rather than throwing, and everything below would go on to
                // arrange an install they had just said no to. Checking here is what makes Cancel
                // mean it even in that sliver: the bits may be on disk, but no exit handler is
                // registered, so nothing installs.
                if (reporter.CancellationToken.IsCancellationRequested)
                {
                    lock (_statusLock)
                    {
                        _status = UploadStatus.NothingKnown;
                    }
                    reporter.Finished(UpdateAttemptOutcome.Cancelled, null, null);
                    LetGoOfTheAttempt();
                    return;
                }

                lock (_statusLock)
                {
                    _status = UploadStatus.DownloadedWaitingForRestart;
                }
                OfferRestartToApplyDownload(
                    reporter,
                    _newVersion.TargetFullRelease.Version.ToString(),
                    restartBloom
                );

                // When we exit, apply the updates. (If autoupdate is false, this is still appropriate,
                // because the user responded to the message about updates available by clicking "Update Now",
                // so we're just completing something already approved).
                // Only ever register one exit handler, however many times we come through here:
                // applying the same update twice is how an upgrade fails or restarts Bloom when
                // nobody asked it to.
                if (_willInstallUpdateOnExit)
                {
                    reporter.Finished(
                        UpdateAttemptOutcome.Downloaded,
                        _newVersion?.TargetFullRelease?.Version?.ToString(),
                        null
                    );
                    return;
                }
                _willInstallUpdateOnExit = true;
                Application.ApplicationExit += (sender, args) =>
                {
                    // Write a file so that if the update fails (e.g., a running process prevents it),
                    // we can detect it on the next launch and warn the user.
                    WriteUpdateAttemptFile(_newVersion.TargetFullRelease.Version.ToString());
                    if (!_restartingAfterToastClicked)
                    {
                        // If the user clicked the toast, we already made the call to WaitExitThenApplyUpdates,
                        // with arguments that WILL show a progress bar and restart Bloom.
                        // If that didn't happen, we call it now with different args, so that the updates are applied
                        // (but Bloom will not restart automatically).
                        _bloomUpdateManager.WaitExitThenApplyUpdates(null, true, false);
                    }
                };

                reporter.Finished(
                    UpdateAttemptOutcome.Downloaded,
                    _newVersion?.TargetFullRelease?.Version?.ToString(),
                    null
                );
                LetGoOfTheAttempt();
            }
            catch (OperationCanceledException)
            {
                // The user pressed Cancel, so Velopack abandoned the transfer. Leave no trace: no
                // exit handler was registered (we never got that far), nothing is downloaded, and
                // putting the status back to NothingKnown means a later attempt this session starts
                // cleanly rather than being told an update is already in progress.
                lock (_statusLock)
                {
                    _status = UploadStatus.NothingKnown;
                }
                reporter.Finished(UpdateAttemptOutcome.Cancelled, null, null);
                LetGoOfTheAttempt();
            }
            catch (Exception e)
            {
                // Hopefully this is very rare. But it's dangerous not to catch all exceptions in an
                // async void method, according to a VS popup.
                ReportFailure(reporter, kUnableToDownloadMessage, e);
                LetGoOfTheAttempt();
            }
#endif
        }

        /// <summary>
        /// Say that the attempt has failed, and remember that it has: having got here we are not
        /// confident of being in a state where it is safe to try again this session.
        /// </summary>
        private static void ReportFailure(
            UpdateReporter reporter,
            string message,
            Exception e = null
        )
        {
            lock (_statusLock)
            {
                _status = UploadStatus.Failed;
            }
            if (e != null)
                _updateException = e;
            // _updateException rather than e, deliberately. The one caller that passes no exception
            // is the "restart Bloom to try again" case, which only happens BECAUSE an earlier
            // attempt failed -- so the exception already on file is exactly the one a problem
            // report should carry.
            reporter.SayProblem(message, _updateException);
            reporter.Finished(UpdateAttemptOutcome.Failed, null, message);
        }

        // ------------------------------------------------------------------------------------
        // The words. Each of these is worked out once, here, and then given to whichever
        // UpdateReporter is in use, so that the toast route and the progress-dialog route say the
        // same thing without either knowing about the other.
        // ------------------------------------------------------------------------------------

        private static string UpToDateMessage() =>
            LocalizationManager.GetString("CollectionTab.UpToDate", "Your Bloom is up to date.");

        private static string AlreadyCheckingMessage() =>
            LocalizationManager.GetString(
                "CollectionTab.UpdateCheckInProgress",
                "Bloom is already working on checking for updates."
            );

        private static string CannotConnectMessage() =>
            LocalizationManager.GetString(
                "CollectionTab.UnableToCheckForUpdate",
                "Could not connect to the server to check for an update. Are you connected to the internet?",
                "Shown when Bloom tries to check for an update but can't, for example because it can't connect to the internet, or a problems with our server, etc."
            );

        private static string UpdatesAvailableMessage() =>
            LocalizationManager.GetString(
                "CollectionTab.UpdatesAvailable",
                "A new version of Bloom is available."
            );

        private static string DownloadingMessage()
        {
            // Velopack may use a more sophisticated algorithm to decide which to download,
            // but this should be good enough to give the user an idea.
            var fullSize = _newVersion.TargetFullRelease.Size;
            var deltasSize = _newVersion.DeltasToTarget.Sum(d => d.Size);
            // With no deltas to add up we have to quote the full release, or we claim the download
            // is 0K. That is what happened for every full download, and Bloom asks for a full one
            // whenever the user is more than MaximumDeltasBeforeFallback builds behind -- so an
            // ordinary two-releases-behind user saw "(0K)". It only ever flashed past in a
            // five-second toast before; now it is what they read while they wait.
            var downloadSize =
                _newVersion.DeltasToTarget.Length == 0 ? fullSize : Math.Min(deltasSize, fullSize);
            return DownloadingMessage(
                _newVersion.TargetFullRelease.Version.ToString(),
                downloadSize / 1024
            );
        }

        private static string DownloadingMessage(string version, long sizeInK) =>
            String.Format(
                LocalizationManager.GetString(
                    "CollectionTab.Updating",
                    "Downloading update to {0} ({1}K)"
                ),
                version,
                sizeInK
            );

        private static string DownloadedMessage(string version) =>
            String.Format(
                LocalizationManager.GetString(
                    "CollectionTab.UpdateInstalled",
                    "Update for {0} is ready",
                    "Appears after Bloom has downloaded a program update in the background and is ready to switch the user to it the next time they run Bloom."
                ),
                version
            );

        // ------------------------------------------------------------------------------------
        // The two things we say that come with something for the user to click.
        // ------------------------------------------------------------------------------------

        /// <param name="sayItTo">Who to show the offer to -- everyone watching, when we are the
        /// attempt.</param>
        /// <param name="downloadFor">Who to start the download on behalf of, if the offer is
        /// accepted. This must be an individual reporter and never a watcher list: the download
        /// resolves the list itself, and handing it its own list would ask it to watch itself.</param>
        private static void OfferFoundUpdates(
            UpdateReporter sayItTo,
            UpdateReporter downloadFor,
            Action restartBloom
        )
        {
            sayItTo.OfferToDownload(
                UpdatesAvailableMessage(),
                LocalizationManager.GetString("CollectionTab.UpdateNow", "Update Now"),
                () => DownloadAndApplyUpdates(restartBloom, downloadFor)
            );
        }

        private static bool _restartingAfterToastClicked = false;

        private static void OfferRestartToApplyDownload(
            UpdateReporter reporter,
            string version,
            Action restartBloom
        )
        {
            reporter.OfferToRestart(
                DownloadedMessage(version),
                LocalizationManager.GetString(
                    "CollectionTab.RestartToUpdate",
                    "Restart Bloom to Update",
                    "Restart the Bloom program, not Windows"
                ),
                () =>
                {
                    ArrangeToApplyUpdateAndRestart();
                    restartBloom();
                }
            );
        }

        /// <summary>
        /// Hand the downloaded update to Velopack the way the "Restart Bloom to Update" toast does:
        /// with the arguments that show Velopack's own progress bar while it installs and then bring
        /// Bloom back by itself. The caller shuts Bloom down straight afterwards.
        ///
        /// The alternative, which the exit handler uses, applies the update quietly and does NOT
        /// relaunch. That is right when the user was quitting anyway and wrong when they have just
        /// asked to be upgraded, because it leaves them looking at a closed program having to start
        /// it again themselves.
        /// </summary>
        internal static void ArrangeToApplyUpdateAndRestart()
        {
#if !__MonoCS__
            // Only once. On the mid-session path both routes to this exist at the same time -- the
            // restart toast the workspace can show, and the upgrade dialog -- and handing the same
            // update to Velopack twice is how an install fails or Bloom relaunches when nobody
            // asked it to.
            if (_restartingAfterToastClicked)
                return;
            _restartingAfterToastClicked = true;
            _bloomUpdateManager?.WaitExitThenApplyUpdates(null);
            Logger.WriteMinorEvent("shutting Bloom down in order to apply updates");
#endif
        }

        // returns true if we should proceed with the update check.
        private static bool GetUpdateUrl(
            BloomUpdateMessageVerbosity verbosity,
            UpdateReporter reporter,
            out string updateUrl
        )
        {
            // For local testing, uncomment and adjust the following line.
            // Note that you need an absolute path; when testing an installed Bloom, even installed by a
            // locally-built installer, we are not running in output/debug or output/release, so there
            // is no automatic way to find output/installer/result.
            //updateUrl = "C:\\github\\BloomDesktop\\output\\installer\\result";
            //return true;
            updateUrl = null; // default for when we return true
            if (Debugger.IsAttached)
            {
                // update'Url' can actually also just be a path to where the deltas and RELEASES file are found.
                // When debugging this function we want this to be the directory where we build installers.
                var location = Assembly.GetExecutingAssembly().Location; // typically in output\debug
                var output = Path.GetDirectoryName(Path.GetDirectoryName(location));
                updateUrl = Path.Combine(output, "installer\\result");
            }
            else
            {
                // Mostly, we're willing to use a cached value for this URL. But if verbosity is Verbose,
                // which means the user has asked us to check for updates and we're going to report even
                // if there is nothing to update, we need to know whether we are online NOW. I'm not sure what
                // Velopack will do if it can't access its URL, but it probably wouldn't be anything we'd like.
                var result = InstallerSupport.LookupUrlOfVelopackUpdate(
                    verbosity == BloomUpdateMessageVerbosity.Verbose
                );

                if (result.Error != null || string.IsNullOrEmpty(result.URL))
                {
                    // no need to tell them we can't connect, if they didn't explicitly ask us to look for an update
                    if (verbosity != BloomUpdateMessageVerbosity.Verbose)
                        return false;

                    // but if they did, try and give them a hint about what went wrong
                    if (result.IsConnectivityError)
                    {
                        reporter.SayWarning(CannotConnectMessage());
                    }
                    else if (
                        result.Error == null
                        || string.IsNullOrWhiteSpace(result.Error.Message)
                    )
                    {
                        SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                            "Bloom failed to find if there is an update available, for some unknown reason."
                        );
                    }
                    else
                    {
                        reporter.SayWarning(result.Error.Message);
                    }

                    return false;
                }

                updateUrl = result.URL;
            }
            return true;
        }

        /// <summary>
        /// Gets the path to the file we write when Bloom is about to exit for a Velopack update.
        /// The channel name is included so that parallel installations on different channels
        /// (e.g. Alpha and Release) do not interfere with each other.
        /// </summary>
        private static string UpdateAttemptFilePath
        {
            get
            {
                // Sanitize the channel name so it is safe to use in a file name.
                var safeChannel = Regex.Replace(ChannelName, @"[^A-Za-z0-9\-]", "-");
                var fileName = $"pending-velopack-update-{safeChannel}.txt";
                return Path.Combine(ProjectContext.GetBloomAppDataFolder(), fileName);
            }
        }

        /// <summary>
        /// Writes a file recording the current and target versions when Bloom is about to exit
        /// for a Velopack update. If the update fails, CheckForFailedUpdate() will detect it on
        /// the next launch and notify the user.
        /// </summary>
        private static void WriteUpdateAttemptFile(string targetVersion)
        {
            try
            {
                var fromVersion = Shell.GetShortVersionInfo();
                RobustFile.WriteAllText(
                    UpdateAttemptFilePath,
                    fromVersion + Environment.NewLine + targetVersion
                );
            }
            catch (Exception e)
            {
                Logger.WriteError("Could not write Velopack update attempt file", e);
            }
        }

        /// <summary>
        /// Called at startup to check whether a previous Velopack update attempt failed (e.g.,
        /// because a running process prevented it). If so, sends an analytics event and shows a
        /// warning toast.
        /// </summary>
        internal static void CheckForFailedUpdate()
        {
#if !__MonoCS__
            var filePath = UpdateAttemptFilePath;
            if (!RobustFile.Exists(filePath))
                return;

            string fromVersion = null;
            string targetVersion = null;
            try
            {
                var lines = RobustFile.ReadAllLines(filePath);
                if (lines.Length >= 2)
                {
                    fromVersion = lines[0].Trim();
                    targetVersion = lines[1].Trim();
                }
            }
            catch (Exception e)
            {
                Logger.WriteError("Could not read Velopack update attempt file", e);
            }

            // Always delete the file — whether the update succeeded or not we only want to
            // check once per recorded attempt.
            try
            {
                RobustFile.Delete(filePath);
            }
            catch (Exception e)
            {
                Logger.WriteError("Could not delete Velopack update attempt file", e);
            }

            if (fromVersion == null || targetVersion == null)
                return;

            // Check whether we have successfully updated to at least the expected version.
            var currentVersion = Shell.GetShortVersionInfo();
            if (
                Version.TryParse(currentVersion, out var current)
                && Version.TryParse(targetVersion, out var target)
                && current >= target
            )
            {
                // Update succeeded; nothing to report.
                return;
            }

            var sentryMessage =
                "Velopack update failed: from "
                + fromVersion
                + " to "
                + targetVersion
                + "; running version "
                + currentVersion;
            NonFatalProblem.ReportSentryOnly(sentryMessage);

            var msg = String.Format(
                LocalizationManager.GetString(
                    "CollectionTab.VelopackUpdateFailed",
                    "Bloom failed to update from version {0} to version {1}. If this continues to happen, try restarting your computer. If it still happens, please Report a Problem."
                ),
                fromVersion,
                targetVersion
            );
            ToastService.ShowToast(ToastType.Warning, text: msg, durationSeconds: 20);
#endif
        }

        internal static void DebugShowToastScenario(string scenario, Action restartBloom = null)
        {
            restartBloom ??= () => { };
            var reporter = new ToastUpdateReporter();

            switch (scenario)
            {
                case "looking":
                    reporter.Say(AlreadyCheckingMessage());
                    return;
                case "upToDate":
                    reporter.Say(UpToDateMessage());
                    return;
                case "foundUpdates":
                    OfferFoundUpdates(reporter, reporter, restartBloom);
                    return;
                case "downloading":
                    reporter.Say(DownloadingMessage("9.9.9", 123));
                    return;
                case "downloadedWaitingForRestart":
                    OfferRestartToApplyDownload(reporter, "9.9.9", restartBloom);
                    return;
                case "error":
                    reporter.SayProblem(
                        kUnableToDownloadMessage,
                        new ApplicationException("Debug update error")
                    );
                    return;
                case "failure":
                    reporter.SayWarning(CannotConnectMessage());
                    return;
                default:
                    throw new ArgumentException(
                        $"Unknown update toast debug scenario '{scenario}'"
                    );
            }
        }

        internal const string kChannelNameForUnitTests = "TestChannel";

        public static bool IsDevOrAlpha
        {
            get
            {
                var channel = ApplicationUpdateSupport.ChannelName.ToLowerInvariant();
                return channel.Contains("developer")
                    || channel.Contains("alpha")
                    || channel.Contains("unstable");
            }
        }
        public static bool IsDev
        {
            get
            {
                var channel = ApplicationUpdateSupport.ChannelName.ToLowerInvariant();
                return channel.Contains("developer");
            }
        }
        public static string ChannelName
        {
            get
            {
                if (Program.RunningUnitTests)
                    return kChannelNameForUnitTests;

                var path = Assembly
                    .GetEntryAssembly()
                    .ManifestModule.FullyQualifiedName.Replace('\\', '/');
                // Use a very specific channel name on developer machines based on build configuration.
                if (path.Contains("/output/Debug/") && path.EndsWith("/Bloom.dll"))
                    return "Developer/Debug"; // verifies this code is running on a developer machine.
                if (path.Contains("/output/Release/") && path.EndsWith("/Bloom.dll"))
                    return "Developer/Release"; // verifies this code is running on a developer machine.
                if (Platform.IsUnix)
                {
                    // The specific directories where the program is installed reflect
                    // the status ("channel") of the program on Linux.
                    if (path.Contains("/bloom-desktop-alpha/"))
                        return "Alpha";
                    if (path.Contains("/bloom-desktop-beta/"))
                        return "Beta";
                    // The next two have never existed yet, but maybe someday we'll want to use them.
                    if (path.Contains("/bloom-desktop-betainternal/"))
                        return "BetaInternal";
                    if (path.Contains("/bloom-desktop-internal/"))
                        return "ReleaseInternal";
                    return "Release";
                }
                // If path contains /BloomX/current/, then X is the channel name.
                // Note that the path is to the Bloom.dll, not to the Bloom{channel}.exe.
                // Also note that the Release will just have "/Bloom/current/"
                var match = Regex.Match(path, @"/Bloom([^/]*)/current/", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var channelName = match.Groups[1].Value;
                    if (!string.IsNullOrEmpty(channelName))
                        return channelName.Replace("-arm64", "");
                }
                return "Release";
            }
        }
    }
}
