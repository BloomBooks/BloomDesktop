using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using L10NSharp;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom.Utils
{
    /// <summary>
    /// Clipboard access that reports its own failures, and the only clipboard API Bloom code
    /// should use for text.
    ///
    /// Windows lets just one thread have the clipboard open at a time, so any clipboard
    /// operation can fail simply because something else (a clipboard manager, a remote-desktop
    /// client, another program in the middle of its own copy) happens to be holding it at that
    /// instant. All the user can do about it is try again, so rather than crashing or popping a
    /// problem-report dialog, these methods toast and return false (BL-16459).
    ///
    /// Writes and reads must be checked differently, because .NET reports their failure
    /// differently -- and the read behaviour is genuinely surprising. Measured on .NET 8 with
    /// another process holding the clipboard, comparing a process that had recently read the
    /// clipboard (which Bloom always has: UpdateEditButtonsAsync polls it on a timer) against a
    /// freshly started one:
    ///
    ///                      recently read the clipboard   fresh process
    ///   ContainsText()     returned True                 threw ExternalException
    ///   GetText()          returned ""                   threw ExternalException
    ///   GetDataObject()    returned an object            threw ExternalException
    ///   SetText()          threw ExternalException       threw ExternalException
    ///
    /// OLE answers a read from a data object cached by that process's own earlier read. So inside
    /// Bloom, reading a locked clipboard silently looks like an *empty* clipboard -- worse than an
    /// error, because "there was nothing to paste" is a plausible lie. Only writes fail honestly.
    /// That is exactly why a failed copy from the Copy button was reported while a failed Ctrl+C
    /// or Ctrl+V was not.
    ///
    /// Hence writes are checked by catching, and anything read-shaped is checked by asking the OS
    /// directly whether the clipboard can be opened -- see TryOpenClipboardBriefly.
    ///
    /// Note that this is the *only* layer that can notice such a failure at all. Chromium
    /// (and therefore anything the browser does, including navigator.clipboard and a plain
    /// Ctrl+C) silently discards clipboard errors: with the clipboard held by another process,
    /// navigator.clipboard.writeText() still resolves, document.execCommand("copy") still
    /// returns true, and navigator.clipboard.readText() resolves with an empty string. So
    /// front-end code that wants to know whether a copy worked has to come through here.
    /// </summary>
    public static class BloomClipboard
    {
        /// <summary>
        /// Put text on the clipboard. Returns false (having told the user) if the clipboard
        /// could not be written.
        /// </summary>
        public static bool TrySetText(string text)
        {
            var succeeded = false;
            RunOnUiThread(() =>
            {
                try
                {
                    PortableClipboard.SetText(text);
                    succeeded = true;
                }
                catch (Exception e)
                {
                    ReportCopyFailure(e);
                }
            });
            return succeeded;
        }

        /// <summary>
        /// Read text from the clipboard. Returns false if the clipboard could not be read; note
        /// that an *empty* clipboard is a success, yielding "".
        /// </summary>
        /// <param name="reportFailure">
        /// Whether to tell the user when the read fails. Pass false when nothing was asked of the
        /// clipboard on the user's behalf -- code that is merely checking whether there is text to
        /// paste, so it can enable a menu item or a button. Such a check can run at any time (when
        /// a menu opens, or while a page loads), and a clipboard momentarily held by another
        /// program must not put an unexplained "Bloom was not able to paste" in front of someone
        /// who never tried to paste.
        /// </param>
        public static bool TryGetText(out string text, bool reportFailure = true)
        {
            text = "";

            // Ask the OS before believing the read: inside Bloom, reading a locked clipboard
            // returns "" rather than throwing (see the class comment), so a failed paste would
            // otherwise be indistinguishable from an empty clipboard and reported to nobody.
            if (!TryOpenClipboardBriefly(out var lastError))
            {
                if (reportFailure)
                    RunOnUiThread(() => ReportPasteFailure(new Win32Exception(lastError)));
                return false;
            }

            // A local, because a lambda may not assign to an out parameter.
            string result = null;
            RunOnUiThread(() =>
            {
                try
                {
                    // Windows programs put CRLF on the clipboard, and this read hands it back
                    // verbatim -- where navigator.clipboard.readText(), which the front end used
                    // before, normalized line endings for us. Keep doing that, so a multi-line
                    // paste doesn't start gaining stray breaks in the editor.
                    result = NormalizeLineEndings(PortableClipboard.GetText());
                }
                catch (Exception e)
                {
                    if (reportFailure)
                        ReportPasteFailure(e);
                }
            });
            text = result ?? "";
            return result != null;
        }

        /// <summary>
        /// Check whether the clipboard is usable right now, and if it isn't, tell the user that
        /// a copy failed. This exists for copies the browser performs itself (a plain Ctrl+C in
        /// a text box, which Chromium handles without telling us): the browser cannot report the
        /// failure, so the only thing we can do is look at the clipboard immediately afterwards
        /// and see whether it is accessible at all.
        ///
        /// Necessarily approximate, in both directions, because it is a second look rather than
        /// the operation itself. A lock that is released between Chromium's attempt and ours goes
        /// unreported; a lock that starts in that same gap makes us report a copy that actually
        /// succeeded. Both windows are the width of one API round trip, and the probe's own
        /// retries (see TryOpenClipboardBriefly) ride out momentary contention, so in practice
        /// this catches the sustained locks that generate real reports (BL-16459) without crying
        /// wolf.
        ///
        /// The probe does take the clipboard itself, for the microseconds between opening and
        /// closing it, which raises the fair question of whether the check could break the very
        /// copy it is checking on -- Chromium writes asynchronously in its own process, so the two
        /// can in principle race, and Chromium discards clipboard errors silently.
        ///
        /// Measured, because it is the kind of thing one should not reason about: a helper process
        /// opened and closed the clipboard continuously -- 7.4 million times in ten seconds --
        /// while Chromium wrote to it twenty times. All twenty writes landed. Whoever wants the
        /// clipboard retries for it, so a hold measured in microseconds is simply ridden over, and
        /// our real check takes it once per copy rather than millions of times. So the race exists
        /// on paper and does not bite; that is why this stays a plain open-and-close rather than
        /// something more elaborate.
        /// </summary>
        public static bool VerifyUsableAfterBrowserCopy()
        {
            return VerifyUsable(ReportCopyFailure);
        }

        /// <summary>
        /// The paste counterpart of VerifyUsableAfterBrowserCopy(), for a Ctrl+V that Chromium
        /// handles itself. Same reasoning and same caveats; the difference is only which message
        /// the user gets. This matters more than it might seem: a paste from an unreadable
        /// clipboard puts nothing in the document, which looks exactly like "the clipboard was
        /// empty", so without this the user is left believing they never copied anything.
        /// </summary>
        public static bool VerifyUsableAfterBrowserPaste()
        {
            return VerifyUsable(ReportPasteFailure);
        }

        private static bool VerifyUsable(Action<Exception> report)
        {
            if (TryOpenClipboardBriefly(out var lastError))
                return true;
            // Report from the UI thread: NonFatalProblem looks at open forms.
            RunOnUiThread(() => report(new Win32Exception(lastError)));
            return false;
        }

        /// <summary>
        /// Whether the OS will currently hand us the clipboard, asked by opening and immediately
        /// closing it.
        ///
        /// This is the raw Win32 call rather than anything in Clipboard/PortableClipboard because,
        /// as the class comment records, a .NET clipboard read inside Bloom does not fail when the
        /// clipboard is held by someone else -- OLE serves it from a cached data object, so we get
        /// a cheerful "True" or an empty string. OpenClipboard asks the OS itself: only one thread
        /// on the machine can hold the clipboard, so either we are granted it or we are not.
        ///
        /// Needs no UI thread: the lock belongs to whichever thread opened it, and we release it
        /// before returning.
        /// </summary>
        private static bool TryOpenClipboardBriefly(out int lastError)
        {
            lastError = 0;
            for (var attempt = 0; attempt < kOpenAttempts; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    CloseClipboard();
                    return true;
                }
                lastError = Marshal.GetLastWin32Error();
                // Something may be holding it for only an instant -- including the WebView2
                // process finishing the very operation we are checking on. Don't cry wolf over
                // that; we are looking for a clipboard held long enough to break the operation.
                Thread.Sleep(kOpenRetryDelayMs);
            }
            return false;
        }

        private const int kOpenAttempts = 4;
        private const int kOpenRetryDelayMs = 50;

        // Note: a NULL owner is fine for *asking* whether the clipboard is available, which is all
        // we do here. It is useless for *holding* the clipboard -- with a NULL owner the call
        // succeeds but grants no exclusivity at all (which is how Lock-Clipboard.ps1 came to
        // report that it had locked a clipboard that was in fact freely usable).
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        /// <summary>
        /// Turns any CRLF or lone CR into LF, matching what the browser's own clipboard read gave
        /// the front end before this class existed. Nothing downstream wants Windows line endings:
        /// the text goes into the editor, where a stray CR can show up as an extra line break.
        /// </summary>
        internal static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static void ReportCopyFailure(Exception e)
        {
            if (AlreadyJustReported(ref _lastCopyFailureReport))
                return;
            // A clipboard failure is not worth a modal problem-report dialog: whatever the
            // cause, all the user can do is try again (BL-16459). So we just toast, which
            // still offers a "Report" link and still tells Sentry.
            NonFatalProblem.Report(
                ModalIf.None,
                PassiveIf.All,
                LocalizationManager.GetDynamicString(
                    "BloomLowPriority",
                    "EditTab.CopyTextFailed",
                    "Bloom was not able to copy that."
                ),
                exception: e,
                // A second failed attempt is a new attempt: show it again rather than letting it
                // be swallowed as a duplicate, or the user reads the silence as success.
                showToastEvenIfDuplicate: true
            );
        }

        private static void ReportPasteFailure(Exception e)
        {
            if (AlreadyJustReported(ref _lastPasteFailureReport))
                return;
            NonFatalProblem.Report(
                ModalIf.None,
                PassiveIf.All,
                LocalizationManager.GetDynamicString(
                    "BloomLowPriority",
                    "EditTab.PasteTextFailed",
                    "Bloom was not able to paste."
                ),
                exception: e,
                // A second failed attempt is a new attempt: show it again rather than letting it
                // be swallowed as a duplicate, or the user reads the silence as success.
                showToastEvenIfDuplicate: true
            );
        }

        private static DateTime _lastCopyFailureReport = DateTime.MinValue;
        private static DateTime _lastPasteFailureReport = DateTime.MinValue;

        // One keystroke can legitimately reach us twice: the browser's own clipboard event and
        // the keydown handler both ask us to check, deliberately, because neither alone covers
        // every case. The user sees one toast either way (the toast host collapses identical
        // messages), but we don't want the log and Sentry to carry the same failure twice, so
        // ignore a repeat of the same kind that arrives while the first is still in flight.
        private static readonly TimeSpan kReportSuppressionWindow = TimeSpan.FromSeconds(1);

        private static bool AlreadyJustReported(ref DateTime lastReport)
        {
            var now = DateTime.Now;
            if (now - lastReport < kReportSuppressionWindow)
                return true;
            lastReport = now;
            return false;
        }

        /// <summary>
        /// The Windows clipboard has to be used from the UI thread, and most of our callers are
        /// API handlers running on a server thread. Send() runs the action inline when we are
        /// already on the UI thread, so this is safe from either.
        /// </summary>
        private static void RunOnUiThread(Action action)
        {
            if (Program.MainContext == null)
            {
                // No WinForms message loop: unit tests and console mode. Just run it here.
                action();
                return;
            }
            Program.MainContext.Send(_ => action(), null);
        }
    }
}
