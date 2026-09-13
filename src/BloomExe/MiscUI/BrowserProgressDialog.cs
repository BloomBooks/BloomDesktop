using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using SIL.Reporting;

namespace Bloom.MiscUI
{
    /// <summary>
    /// This class supports performing a task in the background while a progress dialog
    /// runs in the foreground. (This allows the UI thread to handle updating the dialog.)
    /// The doWhat action does the work and sends progress messages to the dialog.
    /// (A copy button is displayed while doWhat() is in progress, allowing the messages to
    /// be captured.)
    /// DoWhat() returns a boolean:
    /// - false: if doWhenMainActionFalse is provided, it is executed (on the UI thread); normally it
    /// should eventually close the dialog. Otherwise, the dialog closes and the UI thread
    /// resumes as soon as doWhat() returns false.
    /// - true: when doWhat returns this value, the dialog displays a Close button
    /// and a Report button. The Close button allows the user to close the dialog
    /// after studying the the progress messages until satisfied; the UI thread resumes
    /// after this is clicked. The Report button allows the user to send a report
    /// to the Bloom team if the progress messages are unexpected. Again, the code
    /// that initiated the doWhat() task resumes when the report is complete.
    /// </summary>
    /// <remarks>The action is performed in the background not so that the main program
    /// can keep going...it can't, DoWorkWithProgressDialogAsync does not return until doWhat()
    /// is complete...but so that the UI thread can run the progress dialog,
    /// responding to paint events, a click on the buttons, and and so forth.</remarks>
    public class BrowserProgressDialog
    {
        /// <summary>
        /// The dialogId of the EmbeddedProgressDialog the Edit tab's top document renders. It
        /// must match kEditViewProgressDialogId in App.tsx, which is the only document that
        /// answers a dialog opened with this id.
        /// </summary>
        public const string kEditViewProgressDialogId = "editView";

        private static WebSocketProgress _progress;

        // This overload is almost obsolete; please use it only if you need a progress dialog and there
        // is no available main web page that can host an embedded progress dialog.
        public static void DoWorkWithProgressDialog(
            IBloomWebSocketServer socketServer,
            Func<Form> makeDialog,
            Func<IWebSocketProgress, BackgroundWorker, bool> doWhat,
            Action<Form> doWhenMainActionFalse = null,
            IWin32Window owner = null
        )
        {
            // Decided to make this fixed, as it makes life much simpler when using ProgressDialog
            // embedded in a larger document.
            string socketContext = "progress";
            var progress = new WebSocketProgress(socketServer, socketContext);

            // NOTE: This (specifically ShowDialog) blocks the main thread until the dialog is closed.
            // Be careful to avoid deadlocks.
            using (var dlg = makeDialog())
            {
                // For now let's not try to handle letting the user abort.
                dlg.ControlBox = false;
                var worker = new BackgroundWorker();
                worker.WorkerSupportsCancellation = true;
                _readyForProgressReports = false;
                worker.DoWork += (sender, args) =>
                {
                    ProgressDialogApi.SetCancelHandler(() =>
                    {
                        worker.CancelAsync();
                    });

                    // A way of waiting until the dialog is ready to receive progress messages
                    while (!_readyForProgressReports)
                        Thread.Sleep(50);
                    bool waitForUserToCloseDialogOrReportProblems;
                    try
                    {
                        waitForUserToCloseDialogOrReportProblems = doWhat(progress, worker);
                    }
                    catch (Exception ex)
                    {
                        // depending on the nature of the problem, we might want to do more or less than this.
                        // But at least this lets the dialog reach one of the states where it can be closed,
                        // and gives the user some idea things are not right.
                        socketServer.SendEvent(socketContext, "finished");
                        waitForUserToCloseDialogOrReportProblems = true;
                        progress.MessageWithoutLocalizing(
                            "Something went wrong: " + ex.Message,
                            ex is FatalException ? ProgressKind.Fatal : ProgressKind.Error
                        );
                    }

                    // stop the spinner
                    socketServer.SendEvent(socketContext, "finished");
                    if (waitForUserToCloseDialogOrReportProblems)
                    {
                        // Now the user is allowed to close the dialog or report problems.
                        // (ProgressDialog in JS-land is watching for this message, which causes it to turn
                        // on the buttons that allow the dialog to be manually closed (or a problem to be reported).
                        socketServer.SendBundle(socketContext, "show-buttons", new DynamicJson());
                    }
                    else
                    {
                        // Just close the dialog
                        dlg.Invoke(
                            (Action)(
                                () =>
                                {
                                    if (doWhenMainActionFalse != null)
                                        doWhenMainActionFalse(dlg);
                                    else
                                        dlg.Close();
                                }
                            )
                        );
                    }
                };

                worker.RunWorkerAsync();
                dlg.ShowDialog(owner); // returns when dialog closed
                if (progress.HasFatalProblemBeenReported)
                {
                    ProgramExit.Exit();
                }

                ProgressDialogApi.SetCancelHandler(null);
            }
        }

        private static Action _doWhenProgressDialogCloses;

        public static async Task DoWorkWithProgressDialogAsync(
            IBloomWebSocketServer socketServer,
            Func<IWebSocketProgress, BackgroundWorker, Task<bool>> doWhat,
            string id,
            string title,
            bool showCancelButton = false, // true will add a cancel button, but the caller is still responsible for handling the clicks (either here in C# via checking worker.CancellationPending or on the Javascript/React side)
            Action doWhenMainActionFalse = null,
            Action doWhenDialogCloses = null,
            string titleIcon = null
        )
        {
            // Should correspond with IEmbeddedProgressDialogConfig in ProgressDialog.tsx
            var props = new DynamicJson();
            // same object, but the function call wants it to be DynamicJson,
            // while it's easier to set the props when it is typed as dynamic.
            dynamic props1 = props;
            // Which identifies a particular instance of EmbeddedProgressDialog.
            // I'd like to use "id" but that's already reserved in the props object
            // for the message ID.
            props1.which = id;
            props1.title = title;
            props1.titleColor = "white";
            props1.titleIcon = titleIcon;
            props1.titleBackgroundColor = Palette.kBloomBlueHex;
            props1.showReportButton = "if-error";
            props1.showCancelButton = showCancelButton;
            await DoWorkWithProgressDialogAsync(
                socketServer,
                props,
                doWhat,
                doWhenMainActionFalse,
                doWhenDialogCloses
            );
        }

        /// <summary>
        /// This is what a long-running task on the Edit tab calls to show its progress. It puts
        /// up a small dialog that is just the title and a determinate bar: no message list, no
        /// Cancel, no Report. doWhat reports how far it has got with progress.SendPercent(), and
        /// the dialog closes as soon as doWhat returns.
        /// The document that is to show the dialog must render an EmbeddedProgressDialog whose id
        /// is dialogId; the Edit tab's is kEditViewProgressDialogId, mounted in App.tsx.
        /// Note that, as with the DoWorkWithProgressDialogAsync it calls, the returned Task
        /// completes once the work has been started on a background thread, NOT when doWhat has
        /// finished.
        /// </summary>
        public static Task DoWorkWithDeterminateProgressDialogAsync(
            IBloomWebSocketServer socketServer,
            string dialogId,
            string title,
            Func<IWebSocketProgress, Task> doWhat
        )
        {
            // Should correspond with IEmbeddedProgressDialogConfig in ProgressDialog.tsx
            var props = new DynamicJson();
            dynamic props1 = props;
            props1.which = dialogId;
            props1.title = title;
            props1.titleColor = "white";
            props1.titleBackgroundColor = Palette.kBloomBlueHex;
            props1.determinate = true;
            props1.linearProgress = true;
            props1.noMessages = true;
            props1.size = "small";
            props1.showCancelButton = false;
            props1.showReportButton = "never";
            return DoWorkWithProgressDialogAsync(
                socketServer,
                props,
                async (progress, worker) =>
                {
                    await doWhat(progress);
                    // false: no buttons, just close the dialog.
                    return false;
                }
            );
        }

        private static bool _readyForProgressReports;

        // How long the background work waits for the dialog to post progress/ready before
        // going ahead without it.
        private const int kMaxMillisecondsToWaitForDialogReady = 5000;

        /// <summary>
        /// Wait until the dialog posts progress/ready, or until we have waited
        /// kMaxMillisecondsToWaitForDialogReady. Returns true if it reported ready.
        /// </summary>
        /// <remarks>The wait is bounded because the work must happen even when no document is
        /// hosting an EmbeddedProgressDialog with the "which" we sent: nothing then ever posts
        /// progress/ready, and an unbounded wait would mean the work never runs at all.</remarks>
        private static bool WaitForDialogReady()
        {
            var start = DateTime.Now;
            while (!_readyForProgressReports)
            {
                if (
                    (DateTime.Now - start).TotalMilliseconds >= kMaxMillisecondsToWaitForDialogReady
                )
                    return false;
                Thread.Sleep(50);
            }
            return true;
        }

        /// <summary>
        /// Show a progress dialog while doing a task.
        /// Initializes it by sending props to the socketServer with the given context.
        /// (props may include showCancelButton, a boolean.)
        /// (Some callers also pass height and width as props. These are not currently used in
        /// this overload.)
        /// This should cause the dialog to appear.
        /// Then this method begins executing doWhat on a background thread, and returns.
        /// NOTE that unlike other (and previous) overloads, this method typically returns BEFORE
        /// doWhat() finishes executing! (With the progress dialog embedded in the HTML side,
        /// there's no obvious way to make it fully modal.)
        /// When doWhat finishes, we send "finished" to the socket,
        /// - if it returns true, or an exception occurs, we send "show-buttons" to the socket.
        /// - if it returns false, execute doWhenMainActionFalse, or send "close-dialog" to the socket if
        ///   doWhenMainActionFalse is null.
        /// (Note: unlike other overloads, none of the actions is guaranteed to execute on the UI thread.)
        /// When the dialog closes, it will post progress/closed. At this point,
        ///  execute doWhenDialogCloses.
        /// </summary>
        public static async Task DoWorkWithProgressDialogAsync(
            IBloomWebSocketServer socketServer,
            DynamicJson props,
            Func<IWebSocketProgress, BackgroundWorker, Task<bool>> doWhat,
            Action doWhenMainActionFalse = null,
            Action doWhenDialogCloses = null
        )
        {
            // For now making this fixed for progress dialog. Making it configurable really complicates things,
            // since a lot of init is done BEFORE we open the dialog, using a websocket message which we have
            // to listen for, which PASSES the context!
            string socketContext = "progress";
            _doWhenProgressDialogCloses = doWhenDialogCloses;
            _progress = new WebSocketProgress(socketServer, socketContext);

            var worker = new BackgroundWorker();
            worker.WorkerSupportsCancellation = true;
            _readyForProgressReports = false;
            worker.DoWork += async (sender, args) =>
            {
                ProgressDialogApi.SetCancelHandler(() =>
                {
                    worker.CancelAsync();
                });

                // A way of waiting until the dialog is ready to receive progress messages
                if (!WaitForDialogReady())
                    Logger.WriteEvent(
                        "BrowserProgressDialog: the progress dialog never reported ready; doing the work without it."
                    );
                bool waitForUserToCloseDialogOrReportProblems;
                try
                {
                    waitForUserToCloseDialogOrReportProblems = await doWhat(_progress, worker);
                }
                catch (Exception ex)
                {
                    // depending on the nature of the problem, we might want to do more or less than this.
                    // But at least this lets the dialog reach one of the states where it can be closed,
                    // and gives the user some idea things are not right.
                    socketServer.SendEvent(socketContext, "finished");
                    waitForUserToCloseDialogOrReportProblems = true;
                    _progress.MessageWithoutLocalizing(
                        "Something went wrong: " + ex.Message,
                        ex is FatalException ? ProgressKind.Fatal : ProgressKind.Error
                    );
                }

                // stop the spinner
                socketServer.SendEvent(socketContext, "finished");
                if (waitForUserToCloseDialogOrReportProblems)
                {
                    // Now the user is allowed to close the dialog or report problems.
                    // (ProgressDialog in JS-land is watching for this message, which causes it to turn
                    // on the buttons that allow the dialog to be manually closed (or a problem to be reported).
                    socketServer.SendBundle(socketContext, "show-buttons", new DynamicJson());
                }
                else
                {
                    if (doWhenMainActionFalse != null)
                        doWhenMainActionFalse();
                    else
                        socketServer.SendBundle(socketContext, "close-progress", new DynamicJson());
                }
            };
            // If we go back to allowing socketContext to be configurable, and it is configured by a setting in
            // props, this might need to be literally "progress", since the JS side will not yet know to listen
            // on the configured context. (Or maybe there's some other way it knows...this difficulty is a lot
            // of the reason I decided it should NOT be configurable.)
            socketServer.SendBundle(socketContext, "open-progress", props);
            worker.RunWorkerAsync();
        }

        public static void HandleProgressDialogClosed(ApiRequest request)
        {
            if (_progress?.HasFatalProblemBeenReported ?? false)
            {
                ProgramExit.Exit();
            }

            _doWhenProgressDialogCloses?.Invoke();

            _progress = null;
            _doWhenProgressDialogCloses = null;

            ProgressDialogApi.SetCancelHandler(null);
            request.PostSucceeded();
        }

        public static void HandleProgressReady(ApiRequest request)
        {
            _readyForProgressReports = true;
            request.PostSucceeded();
        }
    }
}
