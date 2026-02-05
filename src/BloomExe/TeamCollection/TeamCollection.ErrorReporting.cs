using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.History;
using Bloom.web;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;

namespace Bloom.TeamCollection
{
    public abstract partial class TeamCollection
    {
        // During Startup, we want messages to go to both the current progress dialog and the permanent
        // change log. This method handles sending to both.
        // Note that errors logged here will not result in the TC dialog showing the Reload Collection
        // button, because we are here doing a reload, so all errors are logged as ErrorNoReload.
        void ReportProgressAndLog(
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null
        )
        {
            var fullL10nId = string.IsNullOrEmpty(l10nIdSuffix)
                ? ""
                : "TeamCollection." + l10nIdSuffix;
            var msg = string.IsNullOrEmpty(l10nIdSuffix)
                ? message
                : string.Format(LocalizationManager.GetString(fullL10nId, message), param0, param1);
            progress.MessageWithoutLocalizing(msg, kind);
            _tcLog.WriteMessage(
                (kind == ProgressKind.Progress)
                    ? MessageAndMilestoneType.History
                    : MessageAndMilestoneType.ErrorNoReload,
                fullL10nId,
                message,
                param0,
                param1
            );
        }

        const string kDropboxSettingsWarningEnglish =
            "Important: synchronization problems can be caused when one or more members of your team have incorrect Dropbox settings. Please ensure all members of your team collection are using the correct settings. See [critical Dropbox settings](https://docs.bloomlibrary.org/critical-dropbox-settings/).";

        static string ConvertMarkdownLinksToHtml(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;
            return Regex.Replace(message, @"\[(.*?)\]\((.*?)\)", "<a href='$2'>$1</a>");
        }

        /// <summary>
        /// This overload reports the problem to the progress box, log, and Analytics. It should not be
        /// called directly; it is the common part of the two versions of ReportProblemSyncingBook which also
        /// save the report either in the book or collection history. Returns the message shown to the user.
        /// </summary>
        string CoreReportProblemSyncingBook(
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null
        )
        {
            var warning =
                GetBackendType() == "DropBox"
                    ? LocalizationManager.GetString(
                        "TeamCollection.DropboxSyncSettingsWarning",
                        kDropboxSettingsWarningEnglish
                    )
                    : null;
            if (string.IsNullOrEmpty(warning))
            {
                ReportProgressAndLog(progress, kind, l10nIdSuffix, message, param0, param1);
                var msgForAnalytics = string.Format(message, param0, param1);
                Analytics.Track(
                    "TeamCollectionError",
                    new Dictionary<string, string> { { "message", msgForAnalytics } }
                );
                return msgForAnalytics;
            }

            var fullL10nId = string.IsNullOrEmpty(l10nIdSuffix)
                ? ""
                : "TeamCollection." + l10nIdSuffix;
            var localizedTemplate = string.IsNullOrEmpty(l10nIdSuffix)
                ? message
                : LocalizationManager.GetString(fullL10nId, message);
            var formattedMessage = string.Format(localizedTemplate, param0, param1);
            var fullMessage = $"{formattedMessage}\n\n{warning}";
            var progressMessage = ConvertMarkdownLinksToHtml(fullMessage);

            // Keep a single user-facing message by logging the combined, already-localized text.
            progress.MessageWithoutLocalizing(progressMessage, kind);
            var messageType =
                (kind == ProgressKind.Progress)
                    ? MessageAndMilestoneType.History
                    : MessageAndMilestoneType.ErrorNoReload;
            _tcLog.WriteMessage(messageType, "", fullMessage, null, null);

            var msg =
                $"{string.Format(message, param0, param1)}\n\n{kDropboxSettingsWarningEnglish}";
            Analytics.Track(
                "TeamCollectionError",
                new Dictionary<string, string> { { "message", msg } }
            );
            return msg;
        }

        /// <summary>
        /// This overload reports the problem to the progress box, log, and Analytics, and also makes an entry in
        /// the book's history.
        /// </summary>
        void ReportProblemSyncingBook(
            string folderPath,
            string bookId,
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null,
            bool alsoMakeYouTrackIssue = false
        )
        {
            var msg = CoreReportProblemSyncingBook(
                progress,
                kind,
                l10nIdSuffix,
                message,
                param0,
                param1
            );
            // The second argument is not the ideal name for the book, but unless it has no previous history,
            // the bookName will not be used. I don't think this is the place to be trying to instantiate
            // a Book object to get the ideal name for it. So I decided to live with using the file name.
            BookHistory.AddEvent(
                folderPath,
                Path.GetFileNameWithoutExtension(folderPath),
                bookId,
                BookHistoryEventType.SyncProblem,
                msg
            );
            if (alsoMakeYouTrackIssue)
            {
                MakeYouTrackIssue(progress, msg, folderPath);
            }
        }

        /// <summary>
        /// This overload reports the problem to the progress box, log, and Analytics, and also makes an entry in
        /// the collection's book history. Use it when the problem will result in the book going away, so
        /// it can't be recorded in the book's own history.
        /// </summary>
        void ReportProblemSyncingBook(
            string collectionPath,
            string bookName,
            string bookId,
            IWebSocketProgress progress,
            ProgressKind kind,
            string l10nIdSuffix,
            string message,
            string param0 = null,
            string param1 = null,
            bool alsoMakeYouTrackIssue = false
        )
        {
            var msg = CoreReportProblemSyncingBook(
                progress,
                kind,
                l10nIdSuffix,
                message,
                param0,
                param1
            );
            CollectionHistory.AddBookEvent(
                collectionPath,
                bookName,
                bookId,
                BookHistoryEventType.SyncProblem,
                msg
            );
            if (alsoMakeYouTrackIssue)
                MakeYouTrackIssue(progress, msg, Path.Combine(collectionPath, bookName));
        }

        /// <summary>
        /// Make a YouTrack issue (unless we're running unit tests, or the user is unregistered,
        /// in which case don't bother, since the main point of creating the issue is so we
        /// can get in touch and offer help).
        /// </summary>
        private void MakeYouTrackIssue(IWebSocketProgress progress, string msg, string folderPath)
        {
            if (
                !Program.RunningUnitTests
                && !string.IsNullOrWhiteSpace(Bloom.Registration.Registration.Default.Email)
            )
            {
                var issue = new YouTrackIssueSubmitter(ProblemReportApi.YouTrackProjectKey);
                try
                {
                    var email = Bloom.Registration.Registration.Default.Email;
                    var standardUserInfo = ProblemReportApi.GetStandardUserInfo(
                        email,
                        Bloom.Registration.Registration.Default.FirstName,
                        Bloom.Registration.Registration.Default.Surname
                    );
                    var lostAndFoundUrl =
                        "https://docs.bloomlibrary.org/team-collections-advanced-topics/#2488e17a8a6140bebcef068046cc57b7";
                    var admins = string.Join(
                        ", ",
                        (_tcManager?.Settings?.Administrators ?? new string[0]).Select(e =>
                            ProblemReportApi.GetObfuscatedEmail(e)
                        )
                    );
                    var extraInfo =
                        $"This is a {GetBackendType()} Repo at {RepoDescription}\n{ProblemReportApi.GetBookHistoryAsString(folderPath)}";
                    // Note: there is deliberately no period after {msg} since msg usually ends with one already.
                    var fullMsg =
                        $"{standardUserInfo} \n(Admins: {admins}):\n\nThere was a book synchronization problem that required putting a version in Lost and Found:\n{msg}\n\nSee {lostAndFoundUrl}.\n\n{extraInfo}";
                    var issueId = issue.SubmitToYouTrack("Book synchronization failed", fullMsg);
                    var issueLink = "https://issues.bloomlibrary.org/youtrack/issue/" + issueId;
                    ReportProgressAndLog(
                        progress,
                        ProgressKind.Note,
                        "ProblemReported",
                        "Bloom reported this problem to the developers."
                    );
                    // Originally added " You can see the report at {0}. Also see {1}", issueLink, lostAndFoundUrl); but JohnH says not to (BL-11867)
                }
                catch (Exception e)
                {
                    Debug.Fail(
                        "Submitting problem report to YouTrack failed with '" + e.Message + "'."
                    );
                }
            }
        }

        public void AddHelpMessageIfProblems(IWebSocketProgress progress)
        {
            if (progress.HaveProblemsBeenReported)
            {
                var message = LocalizationManager.GetString(
                    "TeamCollection.GetHelp",
                    "For help with Team Collection problems, click {here}."
                );
                message = message
                    .Replace(
                        "{",
                        "<a href='https://docs.bloomlibrary.org/team-collections-problems'>"
                    )
                    .Replace("}", "</a>");
                progress.MessageWithoutLocalizing(message);
            }
        }
    }
}
