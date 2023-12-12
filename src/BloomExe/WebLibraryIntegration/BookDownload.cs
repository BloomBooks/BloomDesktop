using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Windows.Forms;
using Amazon.Runtime;
using Bloom.Book;
using Bloom.Collection;
using DesktopAnalytics;
using L10NSharp;
using SIL.Extensions;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.Progress;
using Bloom.web.controllers;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Microsoft.VisualBasic;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// Gets book files from Amazon S3.
    /// </summary>
    public class BookDownload
    {
        private readonly BloomS3Client _s3Client;
        public IProgress Progress;

        public event EventHandler<BookDownloadedEventArgs> BookDownLoaded;

        public BookDownload(BloomS3Client bloomS3Client)
        {
            _s3Client = bloomS3Client;
        }

        public string LastBookDownloadedPath { get; set; }

        /// <summary>
        /// Download a book, given a bookOrder URL
        /// </summary>
        /// <param name="orderUrl">bloom://localhost/order?orderFile=BloomLibraryBooks-UnitTests/unittest%40example.com%2fa211f07b-2c9f-4b97-b0b1-71eb24fd%2f</param>
        public string DownloadFromOrderUrl(
            string orderUrl,
            string destPath,
            string bookTitleForAnalytics = "unknown"
        )
        {
            string storageKeyOfBookFolderParentOnS3 = "unknown";
            try
            {
                var uri = new Uri(orderUrl);
                var order = HttpUtility.ParseQueryString(uri.Query)["orderFile"];

                // Starting in 5.6, we simplified the bookOrder URL to not include the full path to the obsolete .BloomBookOrder file.
                // Instead, the meaningful info includes just the bucket name and the prefix (folder) where we can locate the book folder.
                // (Until these changes for 5.6, that was always the user email address followed by the book instance ID, with a slash between.)
                // Unfortunately, older Blooms assume that the prefix part must have two slashes. So we'll have to maintain 2 slashes in the URL
                // as long as we want to keep download working in older versions.
                //
                // But this code is ready for some day when we may stop enforcing two slashes.
                var index = order.IndexOf('/');
                var bucket = order.Substring(0, index);
                storageKeyOfBookFolderParentOnS3 = order.Substring(index + 1);

                // getting the url is considered step 1. (Note, this used to take longer because we actually downloaded a file.)
                _progressDialog?.Invoke(() =>
                {
                    _progressDialog.Progress = 1;
                });

                // uncomment line below to simulate bad internet connection
                // throw new WebException();

                var destinationPath = DownloadBook(
                    bucket,
                    storageKeyOfBookFolderParentOnS3,
                    destPath
                );
                LastBookDownloadedPath = destinationPath;

                Analytics.Track(
                    "DownloadedBook-Success",
                    new Dictionary<string, string>()
                    {
                        { "url", storageKeyOfBookFolderParentOnS3 },
                        { "title", bookTitleForAnalytics }
                    }
                );
                return destinationPath;
            }
            catch (Exception e)
            {
                try
                {
                    // We want to try this before we give a report that may terminate the program. But if something
                    // more goes wrong, ignore it.
                    Analytics.Track(
                        "DownloadedBook-Failure",
                        new Dictionary<string, string>()
                        {
                            { "url", storageKeyOfBookFolderParentOnS3 },
                            { "title", bookTitleForAnalytics }
                        }
                    );
                    Analytics.ReportException(e);
                }
                catch (Exception) { }
                var showSendReport = true;
                // For most types of error, we will set 'showSendReport' to false to avoid an ugly yellow dialog box.
                var message = LocalizationManager.GetString(
                    "Download.ProblemNotice",
                    "There was a problem downloading your book. You may need to restart Bloom or get technical help."
                );
                // BL-1233, we've seen what appear to be timeout exceptions, can't confirm the actual Exception subclass though.
                // It's likely that S3 wraps the original TimeoutException from .net with its own AmazonServiceException.
                if (e is TimeoutException || e.InnerException is TimeoutException)
                {
                    message = LocalizationManager.GetString(
                        "Download.TimeoutProblemNotice",
                        "There was a problem downloading the book: something took too long. You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location."
                    );
                    showSendReport = false;
                }
                if (e is AmazonServiceException || e is WebException || e is IOException) // Network problems, not an internal error, less alarming message called for
                {
                    Logger.WriteError("Bloom had a download problem", e);
                    message = LocalizationManager.GetString(
                        "Download.GenericNetworkProblemNotice",
                        "There was a problem downloading the book.  You can try again at a different time, or write to us at issues@bloomlibrary.org if you cannot get the download to work from your location."
                    );
                    var secondPart = string.Format(
                        LocalizationManager.GetString(
                            "Download.PleaseSendLogFile",
                            "Please also send us the latest log file found at {0}."
                        ),
                        Logger.LogPath
                    );
                    message += Environment.NewLine + Environment.NewLine + secondPart;
                    showSendReport = false;
                }
                DisplayProblem(e, message, showSendReport);
                return "";
            }
        }

        private static void DisplayProblem(Exception e, string message, bool showSendReport = true)
        {
            var action = new Action(
                () =>
                    NonFatalProblem.Report(
                        ModalIf.Alpha,
                        PassiveIf.All,
                        message,
                        null,
                        e,
                        showSendReport
                    )
            );
            var shellWindow = ShellWindow;
            if (shellWindow != null)
                shellWindow.Invoke(action);
            else
                action.Invoke();
        }

        public static string DownloadFolder
        {
            get
            {
                return Environment
                    .GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                    .CombineForPath(
                        ProjectContext.GetInstalledCollectionsDirectory(),
                        BookCollection.DownloadedBooksCollectionNameInEnglish
                    );
            }
        }

        private IProgressDialog _progressDialog;
        private string _bookOrderUrl;

        internal void HandleBloomBookOrder(string bookOrderUrl)
        {
            _bookOrderUrl = bookOrderUrl;

            if (!IsThisVersionAllowedToDownload(bookOrderUrl))
            {
                // We can't use a browser here; we haven't gotten that far in the setup.
                MessageBox.Show(
                    LocalizationManager.GetString(
                        "PublishTab.Upload.OldVersion",
                        "Sorry, this version of Bloom Desktop is not compatible with the current version of BloomLibrary.org. Please upgrade to a newer version."
                    )
                );
                ProcessExtra.SafeStartInFront("https://bloomlibrary.org/download");
                return;
            }

            using (var progressDialog = new ProgressDialog())
            {
                _progressDialog = new ProgressDialogWrapper(progressDialog);
                progressDialog.CanCancel = true;
                progressDialog.Overview = LocalizationManager.GetString(
                    "Download.DownloadingDialogTitle",
                    "Downloading book"
                );
                progressDialog.ProgressRangeMaximum = 14; // a somewhat minimal file count. We will fine-tune it when we know.
                if (IsUrlOrder(bookOrderUrl))
                {
                    var link = new BloomLinkArgs(bookOrderUrl);
                    progressDialog.StatusText = link.Title;
                }
                else
                {
                    // There is no other kind anymore. We used to handle a .BloomBookOrder file.
                }

                // We must do the download in a background thread, even though the whole process is doing nothing else,
                // so we can invoke stuff on the main thread to (e.g.) update the progress bar.
                BackgroundWorker worker = new BackgroundWorker();
                worker.DoWork += OnDoDownload;
                progressDialog.BackgroundWorker = worker;
                progressDialog.ShowDialog(); // hidden automatically when task completes
                if (
                    progressDialog.ProgressStateResult != null
                    && progressDialog.ProgressStateResult.ExceptionThatWasEncountered != null
                )
                {
                    var exc = progressDialog.ProgressStateResult.ExceptionThatWasEncountered;
                    ProblemReportApi.ShowProblemDialog(null, exc, "", "fatal");
                }
            }
        }

        private static bool IsThisVersionAllowedToDownload(string bookOrderUrl)
        {
            // This awkwardness is so we can unit test the main logic without getting messed up by a "real" Application.ProductVersion.
            return IsThisVersionAllowedToDownloadInner(bookOrderUrl, Application.ProductVersion);
        }

        internal static bool IsThisVersionAllowedToDownloadInner(
            string bookOrderUrl,
            string appVersion
        )
        {
            int requiredMajorVersion;
            int requiredMinorVersion;
            try
            {
                var minVersionStr = HttpUtility.ParseQueryString(new Uri(bookOrderUrl).Query)[
                    "minVersion"
                ];
                Version requiredVersion = Version.Parse(minVersionStr);
                requiredMajorVersion = requiredVersion.Major;
                requiredMinorVersion = requiredVersion.Minor;
            }
            catch
            {
                // Three possibilities:
                // 1. minVersion is missing.
                //    Allow download.
                // 2. minVersion is invalid.
                //    Allow download. (This could be argued either way, but this is easier. Since we control the other end, we don't expect this to happen.)
                // 3. Something is wrong with the url itself.
                //    Likely, something else will go wrong, but we don't want to put up a message about the version needing to be updated.
                return true;
            }
            var ourVersion = Version.Parse(appVersion);
            var ourMajorVersion = ourVersion.Major;
            var ourMinorVersion = ourVersion.Minor;

            if (ourMajorVersion == requiredMajorVersion)
                return ourMinorVersion >= requiredMinorVersion;
            return ourMajorVersion >= requiredMajorVersion;
        }

        /// <summary>
        /// url is typically something like https://s3.amazonaws.com/BloomLibraryBooks/somebody@example.com/0a2745dd-ca98-47ea-8ba4-2cabc67022e
        /// It is harmless if there are more elements in it (e.g. address to a particular file in the folder)
        /// Note: if you copy the url from part of the link to a file in the folder from AWS,
        /// you typically need to change %40 to @ in the uploader's email.
        /// </summary>
        internal string HandleDownloadWithoutProgress(string url, string destRoot)
        {
            const string BloomS3UrlPrefix = "https://s3.amazonaws.com/";

            _progressDialog = new ConsoleProgress();
            if (!url.StartsWith(BloomS3UrlPrefix))
            {
                Console.WriteLine($"Url unexpectedly does not start with {BloomS3UrlPrefix}");
                return "";
            }
            var bookOrder = url.Substring(BloomS3UrlPrefix.Length);
            var index = bookOrder.IndexOf('/');
            var bucket = bookOrder.Substring(0, index);
            var folder = bookOrder.Substring(index + 1);

            return DownloadBook(bucket, folder, destRoot);
        }

        /// <summary>
        /// this runs in a worker thread
        /// </summary>
        private void OnDoDownload(object sender, DoWorkEventArgs args)
        {
            // If we are passed a bloom book order URL, download the corresponding book and open it.
            if (IsUrlOrder(_bookOrderUrl))
            {
                var link = new BloomLinkArgs(_bookOrderUrl);
                DownloadFromOrderUrl(_bookOrderUrl, DownloadFolder, link.Title);
            }
            else
            {
                // There is no other kind anymore. We used to handle a .BloomBookOrder file.
            }
        }

        private static Form ShellWindow
        {
            get { return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell); }
        }

        private static bool IsUrlOrder(string argument)
        {
            return argument.ToLowerInvariant().StartsWith(BloomLinkArgs.kBloomUrlPrefix);
        }

        // Internal for testing
        internal string DownloadBook(
            string bucket,
            string storageKeyOfBookFolderParentOnS3,
            string dest
        )
        {
            var destinationPath = _s3Client.DownloadBook(
                bucket,
                storageKeyOfBookFolderParentOnS3,
                dest,
                _progressDialog
            );
            if (BookDownLoaded != null)
            {
                var bookInfo = new BookInfo(destinationPath, false); // A downloaded book is a template, so never editable.
                BookDownLoaded(this, new BookDownloadedEventArgs() { BookDetails = bookInfo });
            }
            var htmlFile = BookStorage.FindBookHtmlInFolder(destinationPath);
            if (htmlFile == "")
                return destinationPath; //argh! it didn't really download the book!
            var xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(htmlFile, false);
            var dom = new HtmlDom(xmlDomFromHtmlFile);
            bool needToSave = false;
            // If the book is downloaded from Bloom Library, we don't want to treat it as though
            // it were directly created from a Reader bloomPack.  So relax the formatting lock.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-9996.
            if (dom.HasMetaElement("lockFormatting"))
            {
                dom.RemoveMetaElement("lockFormatting");
                needToSave = true;
            }
            if (needToSave)
                XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, htmlFile);

            return destinationPath;
        }
    }
}
