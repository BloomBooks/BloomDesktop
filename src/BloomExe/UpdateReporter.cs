using System;
using System.Collections.Generic;
using System.Threading;
using Bloom.ErrorReporter;
using Bloom.web;
using SIL.Reporting;

namespace Bloom
{
    /// <summary>
    /// What came of an update attempt.
    /// </summary>
    internal enum UpdateAttemptOutcome
    {
        /// A newer Bloom was downloaded and will be installed when Bloom exits.
        Downloaded,

        /// We reached the update feed, and there is nothing newer on this channel.
        NothingNewer,

        /// There IS something newer and the user has been offered it, but has not said yes yet.
        /// Distinct from NothingNewer because a caller that reports the outcome to the user would
        /// otherwise tell them the exact opposite of the truth.
        Offered,

        /// We can't update this copy of Bloom at all: a developer build, one an administrator
        /// manages, or one running under the debugger.
        CannotUpdateThisBloom,

        /// We tried and something went wrong -- most likely we couldn't reach the feed.
        Failed,

        /// The user stopped it. Nothing was downloaded, nothing is waiting to install, and a later
        /// attempt in the same session can start from scratch.
        Cancelled,
    }

    /// <summary>
    /// Where the update code's running commentary goes.
    ///
    /// Everything it has to say is worked out in one place, ApplicationUpdateSupport's message
    /// methods, and arrives here already localized; this class only decides where the words are
    /// shown. Normally that is a toast, which is why the update code grew up calling ToastService
    /// directly. But toasts are drawn by ToastHost, which is only mounted in the main workspace, so
    /// before a collection is open they go nowhere at all -- and the "this collection needs a newer
    /// Bloom" dialog runs exactly there. Rather than teach that dialog to reproduce the update
    /// code's wording, it hands in a reporter that puts the very same sentences into a progress
    /// dialog. See BL-16690.
    /// </summary>
    internal abstract class UpdateReporter
    {
        /// <summary>
        /// Ordinary news: looking, downloading, already up to date.
        /// </summary>
        public abstract void Say(string message);

        /// <summary>
        /// Something is not right, but the attempt is not over.
        /// </summary>
        public abstract void SayWarning(string message);

        /// <summary>
        /// Something went wrong. The exception, where we have one, is what a problem report would
        /// be built from.
        /// </summary>
        public abstract void SayProblem(string message, Exception exception);

        /// <summary>
        /// There is an update to be had, if the user wants it.
        /// </summary>
        public abstract void OfferToDownload(string message, string acceptLabel, Action accept);

        /// <summary>
        /// It is downloaded, and takes effect when Bloom restarts.
        /// </summary>
        public abstract void OfferToRestart(string message, string acceptLabel, Action accept);

        /// <summary>
        /// The attempt is over, one way or another, and nothing further will be said. The words in
        /// <paramref name="message"/> are the ones that were used to explain a failure, so a caller
        /// that wants to repeat them somewhere else says the same thing we did.
        /// </summary>
        public virtual void Finished(
            UpdateAttemptOutcome outcome,
            string downloadedVersion,
            string message
        ) { }

        /// <summary>
        /// How far along the download is, 0-100. Ignored by the toasts, which have nowhere to put it.
        /// </summary>
        public virtual void Percent(int percent) { }

        /// <summary>
        /// Cancelled when whoever is watching this attempt gives up on it, so the download really
        /// stops rather than carrying on unwatched. Never cancelled for a route with no way to ask.
        /// </summary>
        public virtual CancellationToken CancellationToken => CancellationToken.None;

        /// <summary>
        /// Whether to be told what an attempt already said, when joining one late.
        ///
        /// A window that has just opened needs it, or it sits there empty. Toasts do not: they are
        /// for news, and replaying a backlog of them would put messages on screen about a check the
        /// user never asked for.
        /// </summary>
        public virtual bool WantsCatchingUp => false;
    }

    /// <summary>
    /// Everyone watching one update attempt.
    ///
    /// There is normally one watcher, but two can want the same attempt: Bloom checks for updates by
    /// itself a minute after a collection opens, and if a Team Collection then starts demanding a
    /// newer Bloom in that same minute, the user clicks Upgrade Bloom on top of a check that is
    /// already running. Turning them away is what we used to do, and it reads as a refusal even
    /// though the upgrade they asked for is under way. So instead they join it, and everything the
    /// update code says reaches both.
    ///
    /// A late joiner is replayed what has been said so far, so a progress dialog opening halfway
    /// through a download shows the download rather than an empty box.
    /// </summary>
    internal class AttemptWatchers : UpdateReporter
    {
        private readonly object _lock = new object();
        private readonly List<UpdateReporter> _watchers = new List<UpdateReporter>();

        // One list, one attempt, one stop-signal for its whole life. Registrations capture this
        // source explicitly rather than reading the field, so the rule stays true if that ever
        // changes.
        private readonly CancellationTokenSource _anyoneCancelled = new CancellationTokenSource();

        // What to replay to someone who joins late.
        private readonly List<Action<UpdateReporter>> _saidSoFar =
            new List<Action<UpdateReporter>>();
        private int? _lastPercent;
        private bool _finished;

        public AttemptWatchers(UpdateReporter first)
        {
            Add(first);
        }

        /// <summary>
        /// Start telling this reporter what happens too, and catch it up on what it missed.
        /// </summary>
        /// <returns>false if the attempt is already over, in which case there is nothing to join
        /// and the caller should deal with the finished state instead.</returns>
        public bool Add(UpdateReporter watcher)
        {
            // Adding this list to itself would make every message recurse until the stack ran out,
            // and a silent hang is a poor way to find out about a wiring mistake.
            if (ReferenceEquals(watcher, this))
                throw new ArgumentException("An attempt's watcher list cannot watch itself.");

            List<Action<UpdateReporter>> replay;
            int? percent;
            CancellationTokenSource cancelThisAttempt;
            lock (_lock)
            {
                if (_finished)
                    return false;
                // Already watching: say yes, but do not sign them up twice and hear everything
                // double. Callers legitimately cannot always tell whether they are in the list.
                if (_watchers.Contains(watcher))
                    return true;
                _watchers.Add(watcher);
                replay = new List<Action<UpdateReporter>>(_saidSoFar);
                percent = _lastPercent;
                cancelThisAttempt = _anyoneCancelled;
            }
            // Outside the lock: these reach the UI. Only for a watcher that wants catching up --
            // replaying to the toast route would announce a check the user never asked about.
            if (watcher.WantsCatchingUp)
            {
                foreach (var said in replay)
                    said(watcher);
                if (percent.HasValue)
                    watcher.Percent(percent.Value);
            }

            HookUpCancellation(watcher, cancelThisAttempt);
            return true;
        }

        /// <summary>
        /// One watcher cancelling stops the download for all of them. That is deliberate: the only
        /// watcher that can cancel is a user who pressed a button, and an explicit "no" outranks a
        /// background convenience nobody asked for.
        /// </summary>
        /// <remarks>The source is passed in rather than read from the field, so a registration made
        /// for one attempt can never cancel a later one.</remarks>
        private static void HookUpCancellation(
            UpdateReporter watcher,
            CancellationTokenSource cancelThisAttempt
        )
        {
            if (!watcher.CancellationToken.CanBeCanceled)
                return;
            watcher.CancellationToken.Register(() =>
            {
                try
                {
                    cancelThisAttempt.Cancel();
                }
                catch (ObjectDisposedException) { }
            });
        }

        // There is deliberately no way to re-arm one of these for a second attempt. An earlier
        // version had one, and it needed a fresh CancellationTokenSource (they cannot be
        // un-cancelled) and a decision about what to do with a watcher who had already cancelled --
        // and either answer to that was wrong. Dropping them threw away a cancellation the user had
        // asked for; keeping them cancelled the new attempt before it began. A new attempt gets a
        // new list instead, so there is no stale stop-signal and no stale replay log to reason
        // about. See BL-16690.

        private void ToEach(Action<UpdateReporter> what, bool remember)
        {
            UpdateReporter[] now;
            lock (_lock)
            {
                if (remember)
                    _saidSoFar.Add(what);
                now = _watchers.ToArray();
            }
            foreach (var w in now)
                what(w);
        }

        public override void Say(string message) => ToEach(w => w.Say(message), true);

        public override void SayWarning(string message) => ToEach(w => w.SayWarning(message), true);

        public override void SayProblem(string message, Exception exception) =>
            ToEach(w => w.SayProblem(message, exception), true);

        // Offers are not replayed to a joiner: by the time anyone joins, an offer has either been
        // taken up or overtaken by the state the attempt is now in.
        public override void OfferToDownload(string message, string acceptLabel, Action accept) =>
            ToEach(w => w.OfferToDownload(message, acceptLabel, accept), false);

        public override void OfferToRestart(string message, string acceptLabel, Action accept) =>
            ToEach(w => w.OfferToRestart(message, acceptLabel, accept), false);

        public override void Percent(int percent)
        {
            lock (_lock)
            {
                _lastPercent = percent;
            }
            ToEach(w => w.Percent(percent), false);
        }

        public override void Finished(
            UpdateAttemptOutcome outcome,
            string downloadedVersion,
            string message
        )
        {
            lock (_lock)
            {
                _finished = true; // nobody may join after this
            }
            ToEach(w => w.Finished(outcome, downloadedVersion, message), false);
        }

        public override CancellationToken CancellationToken => _anyoneCancelled.Token;
    }

    /// <summary>
    /// The normal route: everything the update code says becomes a toast in the workspace, exactly
    /// as it did before there was any other route.
    /// </summary>
    internal class ToastUpdateReporter : UpdateReporter
    {
        public override void Say(string message)
        {
            ToastService.ShowToast(type: ToastType.Update, text: message, durationSeconds: 5);
        }

        public override void SayWarning(string message)
        {
            ToastService.ShowToast(ToastType.Warning, text: message, durationSeconds: 5);
        }

        public override void SayProblem(string message, Exception exception)
        {
            ToastService.ShowToast(
                ToastType.Error,
                text: message,
                durationSeconds: 10,
                action: new ToastAction
                {
                    Callback = () => ErrorReport.NotifyUserOfProblem(exception, message),
                }
            );
        }

        public override void OfferToDownload(string message, string acceptLabel, Action accept)
        {
            ToastService.ShowToast(
                type: ToastType.Update,
                text: message,
                durationSeconds: 10,
                action: new ToastAction { Label = acceptLabel, Callback = () => accept() }
            );
        }

        public override void OfferToRestart(string message, string acceptLabel, Action accept)
        {
            // Deliberately no duration: this one stays until the user deals with it.
            ToastService.ShowToast(
                type: ToastType.Update,
                text: message,
                action: new ToastAction { Label = acceptLabel, Callback = () => accept() }
            );
        }
    }

    /// <summary>
    /// The route for an update the user asked for before any collection is open: the same sentences,
    /// written into a progress dialog, plus a real percentage while the download runs.
    ///
    /// It also collects the outcome, because the caller has to know what to do next -- and, unlike
    /// the toasts, has to do it itself.
    /// </summary>
    internal class ProgressUpdateReporter : UpdateReporter
    {
        // Until the progress dialog is up there is nowhere to write, and a reporter that exists
        // before its dialog is what lets the caller always have one to ask about.
        private IWebSocketProgress _progress = new NullWebSocketProgress();
        private readonly ManualResetEventSlim _finished = new ManualResetEventSlim(false);

        /// <summary>
        /// Yes: a progress dialog that joins an attempt already under way must be caught up, or the
        /// user watches an empty window while a download they asked for runs invisibly.
        /// </summary>
        public override bool WantsCatchingUp => true;

        /// <summary>
        /// Start writing to the progress dialog, once there is one.
        /// </summary>
        public void WriteTo(IWebSocketProgress progress)
        {
            _progress = progress;
        }

        /// <summary>
        /// Get ready to be told about another attempt, having already been told about one. The caller
        /// does that when the first attempt ended merely by OFFERING an update: it then asks again,
        /// this time saying the user has consented. Without resetting, the wait would return
        /// immediately on the previous attempt's signal.
        /// </summary>
        public void StartAnotherAttempt()
        {
            Outcome = UpdateAttemptOutcome.Failed; // until the new attempt says otherwise
            FailureMessage = null;
            _finished.Reset();
        }

        public UpdateAttemptOutcome Outcome { get; private set; } = UpdateAttemptOutcome.Failed;
        public string DownloadedVersion { get; private set; }

        /// <summary>
        /// How we explained a failure, so the caller can repeat it in a message box.
        /// </summary>
        public string FailureMessage { get; private set; }

        /// <summary>
        /// The user pressed Cancel. Worth knowing separately from the outcome, because a download
        /// can finish in the very moment they cancel, and a caller acting on the outcome alone would
        /// restart Bloom under someone who had just said no.
        /// </summary>
        public bool UserCancelled { get; private set; }

        private readonly CancellationTokenSource _cancelDownload = new CancellationTokenSource();

        public override CancellationToken CancellationToken => _cancelDownload.Token;

        /// <summary>
        /// Stop the download, because the user pressed Cancel. Velopack is given this through
        /// <see cref="CancellationToken"/> and abandons the transfer, so nothing is left downloaded
        /// and nothing is waiting to install -- which is the only reading of a button labelled
        /// Cancel that a user could be expected to accept.
        /// </summary>
        public void CancelTheDownload()
        {
            UserCancelled = true;
            _cancelDownload.Cancel();
        }

        /// <summary>
        /// The last thing we told the user, so that Finished does not repeat it.
        /// </summary>
        private string _lastSaid;

        public override void Say(string message)
        {
            _lastSaid = message;
            _progress.MessageWithoutLocalizing(message);
        }

        public override void SayWarning(string message)
        {
            _lastSaid = message;
            _progress.MessageWithoutLocalizing(message, ProgressKind.Warning);
        }

        public override void SayProblem(string message, Exception exception)
        {
            _lastSaid = message;
            _progress.MessageWithoutLocalizing(message, ProgressKind.Error);
            if (exception != null)
                Logger.WriteError("Bloom was unable to update itself", exception);
        }

        // There are no buttons here, so an offer cannot be made. Whether to take one up is the
        // caller's decision, made from what Finished tells it, which is why neither of these
        // quietly accepts on the user's behalf either.
        public override void OfferToDownload(string message, string acceptLabel, Action accept) =>
            Say(message);

        /// <summary>
        /// Deliberately silent. "Update for 6.4.108 is ready" is a fine thing for a toast to say,
        /// because a toast is all the user gets; here the caller says what is actually about to
        /// happen ("Bloom will now close, install it, and start up again") on the very next line,
        /// and having both read like the same news told twice.
        /// </summary>
        public override void OfferToRestart(string message, string acceptLabel, Action accept) { }

        private int _lastPercentReported = -1;

        public override void Percent(int percent)
        {
            // Velopack calls this for every chunk it reads, which for a ninety-megabyte download is
            // a great many times per percentage point. Each one would be a websocket message, so
            // only pass on the ones that would actually change what the user sees.
            if (percent == _lastPercentReported)
                return;
            _lastPercentReported = percent;
            _progress.SendPercent(percent);
        }

        public override void Finished(
            UpdateAttemptOutcome outcome,
            string downloadedVersion,
            string message
        )
        {
            Outcome = outcome;
            DownloadedVersion = downloadedVersion;
            FailureMessage = message;

            // Say it if nobody has. The update code decides what to show from `verbosity`, and we
            // ask for Quiet -- so on the paths where it stays quiet (it cannot reach the update
            // server, or another attempt is already running) the explanation reaches us here and
            // nowhere else. Without this, someone with no internet clicks Upgrade Bloom and gets
            // an empty dialog. Toasts have no equivalent gap: there, staying quiet IS the answer,
            // because the user did not ask for anything.
            if (!string.IsNullOrEmpty(message) && message != _lastSaid)
            {
                _progress.MessageWithoutLocalizing(
                    message,
                    outcome == UpdateAttemptOutcome.NothingNewer
                        ? ProgressKind.Progress
                        : ProgressKind.Error
                );
            }

            _finished.Set();
        }

        /// <summary>
        /// Wait up to <paramref name="timeout"/> for the update attempt to report back.
        /// </summary>
        /// <returns>false if it has not reported back yet</returns>
        public bool WaitForFinish(TimeSpan timeout)
        {
            return _finished.Wait(timeout);
        }

        // _finished and _cancelDownload are deliberately never disposed, and this was reviewed and
        // kept as it stands (BL-16690, 2026-08-19).
        //
        // Waiting on _finished with a timeout does allocate a kernel handle, so one leaks per
        // upgrade the user asks for. Two attempts at releasing it safely each had their own race:
        // the "has it reported yet" flag has to be set either before or after Set(), and both sides
        // are wrong -- before, and a cancel in that instant disposes the event as the download is
        // about to signal it; after, and the waiter can dispose before the flag is set. That is a
        // lot of delicate reasoning to buy back one handle in a process usually about to exit and
        // reinstall itself.
        //
        // Cancel now really cancels, so the window in which anything still touches this object
        // after the caller has finished with it is much smaller than it was -- but "much smaller"
        // is not "closed", and nothing here is worth another race.
    }
}
