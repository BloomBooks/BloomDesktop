using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.MiscUI;
using Bloom.Workspace;
using L10NSharp;
using Newtonsoft.Json;
using SIL.PlatformUtilities;
using SIL.Reporting;
using SIL.Windows.Forms.Miscellaneous;
using ApplicationException = System.ApplicationException;
using Timer = System.Windows.Forms.Timer;

namespace Bloom.web.controllers
{
    /// <summary>
    /// API functions common to various areas of Bloom's HTML UI.
    /// </summary>
    public class CommonApi
    {
        private readonly BookSelection _bookSelection;

        public EditingModel Model { get; set; }

        // Needed so we can implement CheckForUpdates. Set by the WorkspaceView in its constructor, since
        // Autofac was not able to pass us one.
        public static WorkspaceView WorkspaceView { get; set; }

        // Called by autofac, which creates the one instance and registers it with the server.
        public CommonApi(BookSelection bookSelection, BloomWebSocketServer webSocketServer)
        {
            _bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("uiLanguages", HandleUiLanguages, false); // App
            apiHandler.RegisterEndpointHandler("currentUiLanguage", HandleCurrentUiLanguage, false); // App
            apiHandler.RegisterEndpointHandler("bubbleLanguages", HandleBubbleLanguages, false); // Move to EditingViewApi
            apiHandler.RegisterEndpointHandler("common/error", HandleJavascriptError, false); // Common
            apiHandler.RegisterEndpointHandler(
                "common/preliminaryError",
                HandlePreliminaryJavascriptError,
                false
            ); // Common
            apiHandler.RegisterEndpointHandler(
                "common/saveChangesAndRethinkPageEvent",
                RethinkPageAndReloadIt,
                true
            ); // Move to EditingViewApi

            apiHandler.RegisterEndpointHandler(
                "common/canModifyCurrentBook",
                HandleCanModifyCurrentBook,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "common/hasPreserveCoverColor",
                HandleHasPreserveCoverColor,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "common/showSettingsDialog",
                HandleShowSettingsDialog,
                false
            ); // Common
            apiHandler.RegisterEndpointHandler("common/logger/writeEvent", HandleLogEvent, false);
            apiHandler.RegisterEndpointHandler(
                "common/problemWithBookMessage",
                request =>
                {
                    request.ReplyWithText(
                        CommonMessages.GetProblemWithBookMessage(
                            Path.GetFileName(_bookSelection.CurrentSelection?.FolderPath)
                        )
                    );
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                "common/clickHereForHelp",
                request =>
                {
                    var problemFilePath = UrlPathString
                        .CreateFromUrlEncodedString(request.RequiredParam("problem"))
                        .NotEncoded;
                    request.ReplyWithText(
                        CommonMessages.GetPleaseClickHereForHelpMessage(problemFilePath)
                    );
                },
                false
            );
            // Used when something in JS land wants to copy text to or from the clipboard. For POST, the text to be put on the
            // clipboard is passed as the 'text' property of a JSON requestData.
            // Somehow the get version of this fires while initializing a page (probably hooking up CkEditor, an unwanted
            // invocation of the code that decides whether to enable the paste hyperlink button). This causes a deadlock
            // unless we make this endpoint requiresSync:false. I think this is safe as it doesn't interact with any other
            // Bloom objects.
            apiHandler.RegisterEndpointHandler(
                "common/clipboardText",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        string result = ""; // initial value is not used, delegate will set it.
                        Program.MainContext.Send(
                            o =>
                            {
                                try
                                {
                                    result = PortableClipboard.GetText();
                                }
                                catch (Exception e)
                                {
                                    // Need to make sure to handle exceptions.
                                    // If the worker thread dies with an unhandled exception,
                                    // it causes the whole program to immediately crash without opportunity for error reporting
                                    NonFatalProblem.Report(
                                        ModalIf.All,
                                        PassiveIf.None,
                                        "Error pasting text",
                                        exception: e
                                    );
                                }
                            },
                            null
                        );
                        request.ReplyWithText(result);
                    }
                    else
                    {
                        // post
                        var requestData = DynamicJson.Parse(request.RequiredPostJson());
                        string content = requestData.text;
                        if (!string.IsNullOrEmpty(content))
                        {
                            Program.MainContext.Post(
                                o =>
                                {
                                    try
                                    {
                                        PortableClipboard.SetText(content);
                                    }
                                    catch (Exception e)
                                    {
                                        // Need to make sure to handle exceptions.
                                        // If the worker thread dies with an unhandled exception,
                                        // it causes the whole program to immediately crash without opportunity for error reporting
                                        NonFatalProblem.Report(
                                            ModalIf.All,
                                            PassiveIf.None,
                                            "Error copying text",
                                            exception: e
                                        );
                                    }
                                },
                                null
                            );
                        }
                        request.PostSucceeded();
                    }
                },
                false,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "common/checkForUpdates",
                request =>
                {
                    WorkspaceView.CheckForUpdates();
                    request.PostSucceeded();
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                "common/channel",
                request =>
                {
                    request.ReplyWithText(ApplicationUpdateSupport.ChannelName);
                },
                false,
                false
            );
            // This is useful for debugging TypeScript code, especially on Linux.  I wouldn't necessarily expect
            // to see it used anywhere in code that gets submitted and merged.
            apiHandler.RegisterEndpointHandler(
                "common/debugMessage",
                request =>
                {
                    var message = request.RequiredPostString();
                    Debug.WriteLine("FROM JS: " + message);
                    request.PostSucceeded();
                },
                false
            );

            // At this point we open dialogs from c# code; if we opened dialogs from javascript, we wouldn't need this
            // api to do it. We just need a way to close a c#-opened dialog from javascript (e.g. the Close button of the dialog).
            //
            // This must set requiresSync:false because the API call which opened the dialog may already have
            // the lock in which case we would be deadlocked.
            // ErrorReport.NotifyUserOfProblem is a particularly problematic case. We tried to come up with some
            // other solutions for that including opening the dialog on Application.Idle. But the dialog needs
            // to give a real-time result so callers can know what do with button presses. Since some of those
            // callers are in libpalaso, we can't just ignore the result and handle the actions ourselves.
            apiHandler.RegisterEndpointHandler(
                "common/closeReactDialog",
                request =>
                {
                    ReactDialog.CloseCurrentModal(request.GetPostStringOrNull());
                    request.PostSucceeded();
                },
                true,
                requiresSync: false
            );

            // TODO: move to the new App API (BL-9635)
            apiHandler.RegisterEndpointHandler(
                "common/reloadCollection",
                HandleReloadCollection,
                true
            );
        }

        /// <summary>
        /// Whether any modifications to the current book may currently be saved.
        /// This is used by many things that don't otherwise need to know about Team Collections,
        /// so I decided the API call belongs here; however, TeamCollection must be involved
        /// in the answer, so the actual implementation is in TeamCollectionApi.
        /// </summary>
        /// <param name="request"></param>
        private void HandleCanModifyCurrentBook(ApiRequest request)
        {
            request.ReplyWithBoolean(request.CurrentBook?.IsSaveable ?? false);
        }

        /// <summary>
        /// The 2 Comic templates and the Video template insist on black cover color. They also have a meta tag
        /// that tells Bloom to preserve that cover color. This method lets js-land find out about that tag so that
        /// color pickers won't let the user change that color and so they'll know what color the cover is (black).
        /// </summary>
        private void HandleHasPreserveCoverColor(ApiRequest request)
        {
            request.ReplyWithBoolean(
                request.CurrentBook.OurHtmlDom.HasMetaElement("preserveCoverColor")
            );
        }

        public Action ReloadProjectAction { get; set; }

        private void HandleReloadCollection(ApiRequest request)
        {
            // Does nothing if there is no current dialog.
            ReactDialog.CloseAllReactDialogs();

            // On Linux, the main window doesn't close if we invoke ReloadProjectAction immediately here.
            // Waiting for Idle processing allows the underlying dialog to actually close before its parent
            // tries to close.  Without this slight delay on Linux, the user has to manually close the main
            // window before the program reopens and reloads the collection.
            Application.Idle += ReloadOnIdle;
            request.PostSucceeded();
        }

        private void ReloadOnIdle(object sender, EventArgs e)
        {
            Application.Idle -= ReloadOnIdle;
            ReloadProjectAction?.Invoke();
        }

        /// <summary>
        /// Handle showing the settings dialog, opening it to the desired tab.
        /// </summary>
        /// <remarks>
        /// This is here instead of CollectionSettingsApi because we have easier access to
        /// showing the dialog via the WorkspaceView object.  But it's a bit tricky getting
        /// the dialog to display, and allowing the dialog to display help or to restart
        /// the program if the user changes the settings.  Starting the dialog after a very
        /// brief delay, and being sure in WorkSpaceView to Invoke it on the UI thread,
        /// allows the full functionality without any crashes or annoying yellow dialog boxes.
        /// </remarks>
        private void HandleShowSettingsDialog(ApiRequest request)
        {
            lock (request)
            {
                var tab = request.Parameters["tab"];
                _timerForOpenSettingsDialog = new System.Threading.Timer(
                    OpenSettingsDialog,
                    tab,
                    100,
                    System.Threading.Timeout.Infinite
                );
                request.PostSucceeded();
            }
        }

        System.Threading.Timer _timerForOpenSettingsDialog;

        private void OpenSettingsDialog(Object state)
        {
            _timerForOpenSettingsDialog.Dispose();
            _timerForOpenSettingsDialog = null;
            var tab = state as String;
            WorkspaceView.OpenLegacySettingsDialog(tab);
        }

        private void HandleLogEvent(ApiRequest request)
        {
            var message = request.RequiredPostString();
            Logger.WriteEvent(message);
            request.PostSucceeded();
        }

        /// <summary>
        /// Open the folder containing the specified file and select it.
        /// </summary>
        /// <param name="filePath"></param>
        private static void SelectFileInExplorer(string filePath)
        {
            try
            {
                ToPalaso.ProcessExtra.ShowFileInExplorerInFront(
                    filePath.Replace("/", Path.DirectorySeparatorChar.ToString())
                );
            }
            catch (System.Runtime.InteropServices.COMException e)
            {
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    e,
                    $"Bloom had a problem asking your operating system to show {filePath}. Sorry!"
                );
            }
            var folderName = Path.GetFileName(Path.GetDirectoryName(filePath));
            BringFolderToFrontInLinux(folderName);
        }

        /// <summary>
        /// Make sure the specified folder (typically one we just opened an explorer on)
        /// is brought to the front in Linux (BL-673). This is automatic in Windows.
        /// </summary>
        /// <param name="folderName"></param>
        public static void BringFolderToFrontInLinux(string folderName)
        {
            if (Platform.IsLinux)
            {
                // allow the external process to execute
                Thread.Sleep(100);

                // if the system has wmctrl installed, use it to bring the folder to the front
                // This process is not affected by the current culture, so we don't need to adjust it.
                // We don't wait for this to finish, so we don't use the CommandLineRunner methods.
                Process.Start(
                    new ProcessStartInfo()
                    {
                        FileName = "wmctrl",
                        Arguments = "-a \"" + folderName + "\"",
                        UseShellExecute = false,
                        ErrorDialog = false // do not show a message if not successful
                    }
                );
            }
        }

        private void RethinkPageAndReloadIt(ApiRequest request)
        {
            Model.SavePageAndReloadIt(request);
        }

        /// <summary>
        /// Returns json with property languages, an array of objects (one for each UI language Bloom knows about)
        /// each having label (what to show in a menu) and tag (the language code).
        /// Used in language select control in hint bubbles tab of text box properties dialog
        /// brought up from cog control in origami mode.
        /// </summary>
        /// <param name="request"></param>
        public void HandleUiLanguages(ApiRequest request)
        {
            lock (request)
            {
                var langs = new List<object>();
                foreach (var code in L10NSharp.LocalizationManager.GetAvailableLocalizedLanguages())
                {
                    var langItem = WorkspaceView.CreateLanguageItem(code);
                    langs.Add(new { label = langItem.MenuText, tag = code });
                }
                request.ReplyWithJson(JsonConvert.SerializeObject(new { languages = langs }));
            }
        }

        /// <summary>
        /// Returns a simple string with the current UI lanuage
        /// </summary>
        /// <param name="request"></param>
        public void HandleCurrentUiLanguage(ApiRequest request)
        {
            lock (request)
            {
                request.ReplyWithText(L10NSharp.LocalizationManager.UILanguageId);
            }
        }

        public void HandleBubbleLanguages(ApiRequest request)
        {
            lock (request)
            {
                var bubbleLangs = new List<string>();
                bubbleLangs.Add(LocalizationManager.UILanguageId);
                bubbleLangs.Add(_bookSelection.CurrentSelection.BookData.MetadataLanguage1Tag);
                if (_bookSelection.CurrentSelection.Language2Tag != null)
                    bubbleLangs.Add(_bookSelection.CurrentSelection.Language2Tag);
                if (_bookSelection.CurrentSelection.Language3Tag != null)
                    bubbleLangs.Add(_bookSelection.CurrentSelection.Language3Tag);
                bubbleLangs.AddRange(new[] { "en", "fr", "sp", "ko", "zh-Hans" });
                // If we don't have a hint in the UI language or any major language, it's still
                // possible the page was made just for this langauge and has a hint in that language.
                // Not sure whether this should be before or after the list above.
                // Definitely wants to be after UILangage, otherwise we get the surprising result
                // that in a French collection these hints stay French even when all the rest of the
                // UI changes to English.
                bubbleLangs.Add(_bookSelection.CurrentSelection.BookData.Language1.Tag);
                // if it isn't available in any of those we'll arbitrarily take the first one.
                request.ReplyWithJson(JsonConvert.SerializeObject(new { langs = bubbleLangs }));
            }
        }

        public void HandleJavascriptError(ApiRequest request)
        {
            lock (lockJsError)
            {
                preliminaryJavascriptError = null; // got a real report.
            }
            lock (request)
            {
                ReportJavascriptError(request.RequiredPostJson());
                request.PostSucceeded();
            }
        }

        private static void ReportJavascriptError(string detailsJson)
        {
            string detailsMessage;
            string detailsStack;
            try
            {
                var details = DynamicJson.Parse(detailsJson);
                detailsMessage = details.message;
                detailsStack = details.stack;
            }
            catch (Exception e)
            {
                // Somehow a problem here seems to kill Bloom. So in desperation we catch everything.
                detailsMessage = "Javascript error reporting failed: " + e.Message;
                detailsStack = detailsJson;
            }

            var ex = new ApplicationException(detailsMessage + Environment.NewLine + detailsStack);
            // For now unimportant JS errors are still quite common, sadly. Per BL-4301, we don't want
            // more than a toast, even for developers.
            // It would seem logical that we should consider Browser.SuppressJavaScriptErrors here,
            // but somehow none are being reported while making an epub preview, which was its main
            // purpose. So I'm leaving that out until we know we need it.
            NonFatalProblem.Report(
                ModalIf.None,
                PassiveIf.Alpha,
                "A JavaScript error occurred",
                detailsMessage,
                ex
            );
        }

        object lockJsError = new object();
        private string preliminaryJavascriptError;
        private Timer jsErrorTimer;

        // This api receives javascript errors with stack dumps that have not been converted to source.
        // Javascript code will then attempt to convert them and report using HandleJavascriptError.
        // In case that fails, after 200ms we will make the report using the unconverted stack.
        public void HandlePreliminaryJavascriptError(ApiRequest request)
        {
            lock (request)
            {
                lock (lockJsError)
                {
                    if (preliminaryJavascriptError != null)
                    {
                        // If we get more than one of these without a real report, the first is most likely to be useful, I think.
                        // This also avoids ever having more than one timer running.
                        request.PostSucceeded();
                        return;
                    }
                    preliminaryJavascriptError = request.RequiredPostJson();
                }

                var form = Application.OpenForms.Cast<Form>().Last();
                // If we don't have an active Bloom form, I think we can afford to discard this report.
                if (form != null)
                {
                    form.BeginInvoke(
                        (Action)(
                            () =>
                            {
                                // Arrange to report the error if we don't get a better report of it in 200ms.
                                jsErrorTimer?.Stop(); // probably redundant
                                jsErrorTimer?.Dispose(); // left over from previous report that had follow-up?
                                jsErrorTimer = new Timer { Interval = 200 };
                                jsErrorTimer.Tick += (sender, args) =>
                                {
                                    jsErrorTimer.Stop(); // probably redundant?
                                    // not well documented but found some evidence this is OK inside event handler.
                                    jsErrorTimer.Dispose();
                                    jsErrorTimer = null;

                                    dynamic temp;
                                    lock (lockJsError)
                                    {
                                        temp = preliminaryJavascriptError;
                                        preliminaryJavascriptError = null;
                                    }

                                    if (temp != null)
                                    {
                                        ReportJavascriptError(temp);
                                    }
                                };
                                jsErrorTimer.Start();
                            }
                        )
                    );
                }
                request.PostSucceeded();
            }
        }
    }
}
