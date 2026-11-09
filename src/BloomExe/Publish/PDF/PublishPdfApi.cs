using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using L10NSharp;
using SIL.Reporting;
using Application = System.Windows.Forms.Application;

namespace Bloom.Publish.PDF
{
    public class PublishPdfApi
    {
        private readonly BloomWebSocketServer _webSocketServer;
        private PublishModel _publishModel;
        private BackgroundWorker _makePdfBackgroundWorker;

        private string PdfFilePath => _publishModel.PdfFilePath;

        // This is to support orientation/page size changing during publishing, but we're not doing
        // that yet, so just defer to the current book.
        public Layout PageLayout => _publishModel.PageLayout;

        public PublishModel.BookletPortions BookletPortion
        {
            get { return _publishModel.BookletPortion; }
            set { _publishModel.BookletPortion = value; }
        }

        public PublishPdfApi(BloomWebSocketServer webSocketServer, PublishModel model)
        {
            _webSocketServer = webSocketServer;
            _publishModel = model;
        }

        private const string kApiUrlPart = "publish/pdf/";

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "licenseOK",
                request => String.IsNullOrEmpty(CheckForLicenseWarning()),
                (writeRequest, value) => { },
                true
            );
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "simple", HandleCreateSimplePdf, true);
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "cover", HandleCreateCoverPdf, true);
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "cancel", HandleCancel, true);
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "pages", HandleCreatePagesPdf, true);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "showPrintInstructions",
                HandleShowPrintInstructions,
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "closePrintInstructions",
                request =>
                {
                    ClosePrintInstructions();
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "save", HandleSavePdf, true);
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "allowBooklet",
                request => _publishModel.AllowPdfBooklet,
                null,
                false
            );
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "allowFullBleed",
                request => CurrentBook?.FullBleed ?? false,
                null,
                false
            );
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "includeBackgroundColors",
                request => CurrentBook?.UserPrefs.IncludeBackgroundColors ?? false,
                (
                    (writeRequest, value) =>
                    {
                        if (CurrentBook != null)
                        {
                            CurrentBook.UserPrefs.IncludeBackgroundColors = value;
                        }
                    }
                ),
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "printAnalytics",
                HandlePrintAnalytics,
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "excludePrintBleed",
                request =>
                    !(
                        (CurrentBook?.FullBleed ?? false)
                        && (CurrentBook?.UserPrefs.FullBleed ?? false)
                    ),
                (
                    (writeRequest, value) =>
                    {
                        if (CurrentBook != null)
                        {
                            CurrentBook.UserPrefs.FullBleed = !value;
                        }
                    }
                ),
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "colorProfile",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        var profile = CurrentBook?.UserPrefs.ColorProfileForPdf;
                        if (string.IsNullOrEmpty(profile))
                            profile = "none";
                        request.ReplyWithText(profile);
                    }
                    else
                    {
                        var value = request.RequiredPostString();
                        if (CurrentBook != null)
                        {
                            if (value == "none")
                                value = "";
                            CurrentBook.UserPrefs.ColorProfileForPdf = value;
                        }
                        request.PostSucceeded();
                    }
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "colorProfiles",
                HandleColorProfiles,
                false
            );
        }

        private void HandleColorProfiles(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Get)
            {
                var result = new StringBuilder();
                result.Append($"[\"none\"");
                var distFolder = PdfMaker.GetDistributedColorProfilesFolder();
                if (Directory.Exists(distFolder))
                {
                    var files = Directory.GetFiles(distFolder, "*.icc");
                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        result.AppendFormat(",\"{0}\"", name);
                    }
                }
                var folder = PdfMaker.GetUserColorProfilesFolder();
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder, "*.icc");
                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        result.AppendFormat(",\"{0}\"", name);
                    }
                }
                result.Append("]");
                request.ReplyWithText(result.ToString());
            }
            else
            {
                request.Failed();
            }
        }

        private string CheckForLicenseWarning()
        {
            return new LicenseChecker().CheckBook(
                _publishModel.BookSelection.CurrentSelection,
                _publishModel.BookSelection.CurrentSelection.ActiveLanguages.ToArray()
            );
        }

        private void HandleCancel(ApiRequest request)
        {
            _makePdfBackgroundWorker.CancelAsync();
            request.PostSucceeded();
        }

        private void HandleSavePdf(ApiRequest request)
        {
            _publishModel.SavePdf();
            request.PostSucceeded();
        }

        // The window showing the booklet print settings while the print dialog is open, if any.
        private ReactDialog _printInstructionsWindow;

        /// <summary>
        /// Open a small window showing the print settings this booklet needs. It has to be a
        /// window of its own: the browser's print dialog covers the whole of the WebView2 that
        /// asked to print, so nothing in that page can be seen while it is open. The window is
        /// owned by the Shell, so it stays above Bloom (print dialog included) without floating
        /// over other programs. It goes at the top right of Bloom's window, where it covers
        /// neither the print dialog's settings (on the left) nor its buttons (at the bottom).
        /// The user can drag it or close it; otherwise it stays until they print again or leave
        /// the PDF &amp; Print screen, since nothing tells us when the print dialog closes.
        /// </summary>
        private void HandleShowPrintInstructions(ApiRequest request)
        {
            ClosePrintInstructions();
            var shell = Application.OpenForms.Cast<Form>().First(f => f is Shell);
            var window = new ReactDialog(
                "printInstructionsBundle",
                GetPrintSettingsHelp(),
                LocalizationManager.GetString("PublishTab.PrintButton", "Print...")
            );
            // A normal caption, so it can be dragged and has a full-size close button.
            window.FormBorderStyle = FormBorderStyle.FixedDialog;
            window.ControlBox = true;
            window.MinimizeBox = false;
            window.MaximizeBox = false;
            window.ShowIcon = false;
            window.ShowWithoutTakingFocus = true;
            // Big enough for the longest text we have (the four-pages-a-sheet note, in Portuguese);
            // the page scrolls for anything longer.
            window.SetScaledSize(520, 250);
            var shellArea = shell.RectangleToScreen(shell.ClientRectangle);
            // Start inside Bloom's window, so OnLoad scales by the DPI of the monitor it is on.
            window.Location = shellArea.Location;
            // When Bloom is narrower than the window, keep its left edge (where the text starts)
            // on the screen.
            var screenLeft = Screen.FromControl(shell).WorkingArea.Left;
            window.PlaceAfterSizing(size => new System.Drawing.Point(
                Math.Max(screenLeft, shellArea.Right - size.Width - shell.LogicalToDeviceUnits(30)),
                shellArea.Top + shell.LogicalToDeviceUnits(5)
            ));
            window.FormClosed += (sender, args) =>
            {
                if (_printInstructionsWindow == sender)
                    _printInstructionsWindow = null;
            };
            _printInstructionsWindow = window;
            window.Show(shell);
            request.PostSucceeded();
        }

        /// <summary>
        /// Close the print instructions window, if it is open.
        /// </summary>
        private void ClosePrintInstructions()
        {
            _printInstructionsWindow?.Close();
            _printInstructionsWindow = null;
        }

        // The localized print settings for the current book's page size: 'helps' (one string per
        // setting, missing for a size not recommended for booklets) and 'note'.
        private dynamic GetPrintSettingsHelp()
        {
            var needA4Paper = false;
            var needLandscape = false;
            var needLongEdge = false;
            var needCutAndStack = Book.Book.UsesCutAndStackBooklet(PageLayout.SizeAndOrientation);
            var needCutAndFold = false;
            var needHelps = true;
            dynamic result = new ExpandoObject();
            switch (PageLayout.SizeAndOrientation.PageSizeName)
            {
                case "A5":
                    needA4Paper = true;
                    if (PageLayout.SizeAndOrientation.IsLandScape) // Portrait, long edge, A4; cut and stack
                    {
                        needLongEdge = true;
                    }
                    else // Landscape, short edge, A4
                    {
                        needLandscape = true;
                    }
                    break;
                case "A6":
                    needA4Paper = true;
                    needCutAndFold = true;
                    if (PageLayout.SizeAndOrientation.IsLandScape) // Landscape, short edge, A4; cut and fold
                        needLandscape = true;
                    else // Portrait, long edge, A4; cut and fold
                        needLongEdge = true;
                    break;
                case "HalfLetter":
                    if (PageLayout.SizeAndOrientation.IsLandScape) // Portrait, long edge, Letter; cut and stack
                    {
                        needLongEdge = true;
                    }
                    else // Landscape, short edge, Letter
                    {
                        needLandscape = true;
                    }
                    break;
                case "QuarterLetter":
                    if (PageLayout.SizeAndOrientation.IsLandScape) // Landscape, short edge, Letter; cut and fold
                        needLandscape = true;
                    else // Portrait, long edge, Letter; cut and fold
                        needLongEdge = true;
                    needCutAndFold = true;
                    break;
                default:
                    needHelps = false;
                    result.note = "This paper size is not recommended for booklets.";
                    break;
            }
            if (needCutAndFold)
                result.note = LocalizationManager.GetString(
                    "PublishTab.PDF.Booklet.FourPages.CutAndFold",
                    "This will put four pages on each sheet of paper. Cut the stack, then fold to get two booklets."
                );
            else if (needCutAndStack)
                result.note = LocalizationManager.GetString(
                    "PublishTab.PDF.Booklet.TwoPages.CutAndStack",
                    "This will put two pages on each sheet of paper. Cut horizontally and stack to make one booklet."
                );
            if (needHelps)
            {
                result.helps = new string[4];
                if (needLandscape)
                    result.helps[0] = LocalizationManager.GetString(
                        "PublishTab.PDF.Booklet.Layout.Landscape",
                        "1. Layout: Landscape"
                    );
                else
                    result.helps[0] = LocalizationManager.GetString(
                        "PublishTab.PDF.Booklet.Layout.Portrait",
                        "1. Layout: Portrait"
                    );
                if (needLongEdge)
                    result.helps[1] = LocalizationManager.GetString(
                        "PublishTab.PDF.Booklet.Flip.LongEdge",
                        "2. Print on both sides: \"Flip on long edge\""
                    );
                else
                    result.helps[1] = LocalizationManager.GetString(
                        "PublishTab.PDF.Booklet.Flip.ShortEdge",
                        "2. Print on both sides: \"Flip on short edge\""
                    );
                if (needA4Paper)
                    result.helps[2] = LocalizationManager.GetString(
                        "PublishTab.PDF.Booklet.PaperSize.A4",
                        "3. More settings > Paper size: \"A4\""
                    );
                else
                    result.helps[2] = LocalizationManager.GetString(
                        "PublishTab.PDF.Booklet.PaperSize.Letter",
                        "3. More settings > Paper size: \"Letter\""
                    );
                result.helps[3] = LocalizationManager.GetString(
                    "PublishTab.PDF.Booklet.Scale.Actual Size",
                    "4. More settings > Scale: \"Actual size\""
                );
            }
            return result;
        }

        private void HandlePrintAnalytics(ApiRequest request)
        {
            BloomAnalytics.Track(
                "Print PDF",
                new Dictionary<string, string>()
                {
                    { "BookId", CurrentBook.ID },
                    { "Country", CurrentBook.CollectionSettings.Country },
                }
            );
            CurrentBook.ReportSimplisticFontAnalytics(
                FontAnalytics.FontEventType.PublishPdf,
                "Print PDF"
            );

            request.PostSucceeded();
        }

        private bool isBooklet()
        {
            return BookletPortion == PublishModel.BookletPortions.BookletCover
                || BookletPortion == PublishModel.BookletPortions.BookletPages;
        }

        private void HandleCreatePagesPdf(ApiRequest request)
        {
            BookletPortion = PublishModel.BookletPortions.BookletPages;
            MakePdf();
            request.PostSucceeded();
        }

        private void HandleCreateCoverPdf(ApiRequest request)
        {
            BookletPortion = PublishModel.BookletPortions.BookletCover;
            MakePdf();
            request.PostSucceeded();
        }

        private void HandleCreateSimplePdf(ApiRequest request)
        {
            BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet;
            MakePdf();
            request.PostSucceeded();
        }

        private Book.Book CurrentBook => _publishModel.CurrentBook;

        // Make the actual PDF file with current settings. Slightly adapted from the PublishView code
        // that calls PublishModel.LoadBook.
        public void MakePdf()
        {
            // Crop marks are automatic when print bleed is enabled.
            // This applies to both Simple and booklet outputs.
            _publishModel.ShowCropMarks =
                (CurrentBook?.FullBleed ?? false) && (CurrentBook?.UserPrefs.FullBleed ?? false);

            var message = CheckForLicenseWarning();
            if (!String.IsNullOrEmpty(message))
            {
                MessageBox.Show(
                    message,
                    LocalizationManager.GetString("Common.Warning", "Warning"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }
            var shell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
            _makePdfBackgroundWorker = new BackgroundWorker();
            _makePdfBackgroundWorker.WorkerReportsProgress = true;
            _makePdfBackgroundWorker.WorkerSupportsCancellation = true;
            _makePdfBackgroundWorker.RunWorkerCompleted +=
                _makePdfBackgroundWorker_RunWorkerCompleted;
            _makePdfBackgroundWorker.DoWork += (
                (sender, doWorkEventArgs) =>
                {
                    _publishModel.LoadBook(sender as BackgroundWorker, doWorkEventArgs, shell);
                }
            );
            _makePdfBackgroundWorker.RunWorkerAsync();
        }

        void _makePdfBackgroundWorker_RunWorkerCompleted(
            object sender,
            System.ComponentModel.RunWorkerCompletedEventArgs e
        )
        {
            _publishModel.PdfGenerationSucceeded = false;
            if (e.Cancelled)
            {
                dynamic messageBundleCancel = new DynamicJson();
                messageBundleCancel.path = "";
                _webSocketServer.SendBundle("publish", "pdfReady", messageBundleCancel);
            }
            else
            {
                if (e.Result is Exception)
                {
                    PublishModel.ReportPdfGenerationError(e.Result as Exception);
                    dynamic messageBundleCancel = new DynamicJson();
                    messageBundleCancel.path = "";
                    _webSocketServer.SendBundle("publish", "pdfReady", messageBundleCancel);
                    return;
                }
                _publishModel.PdfGenerationSucceeded = true;
                dynamic messageBundle = new DynamicJson();
                messageBundle.path = PdfFilePath.ToLocalhost();
                _webSocketServer.SendBundle("publish", "pdfReady", messageBundle);
            }
        }
    }
}
