using System;
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

        /// We can't update this copy of Bloom at all: a developer build, one an administrator
        /// manages, or one running under the debugger.
        CannotUpdateThisBloom,

        /// We tried and something went wrong -- most likely we couldn't reach the feed.
        Failed,
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
        private readonly IWebSocketProgress _progress;
        private readonly ManualResetEventSlim _finished = new ManualResetEventSlim(false);

        public ProgressUpdateReporter(IWebSocketProgress progress)
        {
            _progress = progress;
        }

        public UpdateAttemptOutcome Outcome { get; private set; } = UpdateAttemptOutcome.Failed;
        public string DownloadedVersion { get; private set; }

        /// <summary>
        /// How we explained a failure, so the caller can repeat it in a message box.
        /// </summary>
        public string FailureMessage { get; private set; }

        public override void Say(string message)
        {
            _progress.MessageWithoutLocalizing(message);
        }

        public override void SayWarning(string message)
        {
            _progress.MessageWithoutLocalizing(message, ProgressKind.Warning);
        }

        public override void SayProblem(string message, Exception exception)
        {
            _progress.MessageWithoutLocalizing(message, ProgressKind.Error);
            if (exception != null)
                Logger.WriteError("Bloom was unable to update itself", exception);
        }

        // There are no buttons here, so an offer can only be reported. Whether to take it up is the
        // caller's decision, made from what Finished tells it -- which is also why neither of these
        // quietly accepts on the user's behalf.
        public override void OfferToDownload(string message, string acceptLabel, Action accept) =>
            Say(message);

        public override void OfferToRestart(string message, string acceptLabel, Action accept) =>
            Say(message);

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
    }
}
