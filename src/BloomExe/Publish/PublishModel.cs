using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.Publish.PDF;
using Bloom.SafeXml;
using Bloom.SubscriptionAndFeatures;
using Bloom.ToPalaso;
using Bloom.Utils;
using BloomTemp;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Bloom.Publish
{
    /// <summary>
    /// Contains the logic behind the publish tab, which involves creating a pdf from the html book and letting you print it,
    /// making epubs, and various other publication paths.
    /// </summary>
    public class PublishModel : IDisposable
    {
        public BookSelection BookSelection { get; private set; }

        public BookServer BookServer
        {
            get { return _bookServer; }
        }

        public string PdfFilePath { get; private set; }

        public int HtmlPageCount { get; private set; }

        public enum BookletPortions
        {
            None,
            AllPagesNoBooklet,
            BookletCover,
            BookletPages, //include front and back matter that isn't coverstock
            InnerContent //excludes all front and back matter
        }

        public enum BookletLayoutMethod
        {
            NoBooklet,
            SideFold,
            CutAndStack,
            Calendar
        }

        private Book.Book _currentlyLoadedBook;
        private PdfMaker _pdfMaker;
        private readonly CurrentEditableCollectionSelection _currentBookCollectionSelection;
        private readonly CollectionSettings _collectionSettings;
        private readonly BookServer _bookServer;
        private readonly BookThumbNailer _thumbNailer;
        private string _lastDirectory;

        private BackgroundWorker _makePdfBackgroundWorker = new BackgroundWorker();

        public PublishModel(
            BookSelection bookSelection,
            PdfMaker pdfMaker,
            CurrentEditableCollectionSelection currentBookCollectionSelection,
            CollectionSettings collectionSettings,
            BookServer bookServer,
            BookThumbNailer thumbNailer
        )
        {
            BookSelection = bookSelection;
            _pdfMaker = pdfMaker;
            _pdfMaker.CompressPdf = true; // See http://issues.bloomlibrary.org/youtrack/issue/BL-3721.
            //_pdfMaker.EngineChoice = collectionSettings.PdfEngineChoice;
            _currentBookCollectionSelection = currentBookCollectionSelection;
            ShowCropMarks = false;
            _collectionSettings = collectionSettings;
            _bookServer = bookServer;
            _thumbNailer = thumbNailer;
            //we don't want to default anymore: BookletPortion = BookletPortions.BookletPages;

            _makePdfBackgroundWorker.WorkerReportsProgress = true;
            _makePdfBackgroundWorker.WorkerSupportsCancellation = true;
            _makePdfBackgroundWorker.DoWork += new DoWorkEventHandler(
                _makePdfBackgroundWorker_DoWork
            );
            _makePdfBackgroundWorker.RunWorkerCompleted +=
                _makePdfBackgroundWorker_RunWorkerCompleted;
        }

        public PublishView View { get; set; }

        bool _pdfSucceeded;
        public bool PdfGenerationSucceeded
        {
            get { return _pdfSucceeded; }
            set { _pdfSucceeded = value; }
        }

        private void _makePdfBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = BookletPortion; //record what our parameters were, so that if the user changes the request and we cancel, we can detect that we need to re-run
            LoadBook(sender as BackgroundWorker, e);
        }

        /// <summary>
        /// Make the preview required for publishing the book.
        /// </summary>
        internal void MakePDFForUpload(IProgress progress)
        {
            if (_makePdfBackgroundWorker.IsBusy)
            {
                // Can't start another until current attempt finishes.
                _makePdfBackgroundWorker.CancelAsync();
                while (_makePdfBackgroundWorker.IsBusy)
                    System.Threading.Thread.Sleep(100);
            }

            var message = new LicenseChecker().CheckBook(
                BookSelection.CurrentSelection,
                BookSelection.CurrentSelection.ActiveLanguages.ToArray()
            );
            if (message != null)
            {
                MessageBox.Show(
                    message,
                    LocalizationManager.GetString("Common.Warning", "Warning")
                );
                return;
            }

            _previewProgress = progress;
            _makePdfBackgroundWorker.ProgressChanged += UpdatePreviewProgress;

            // Usually these will have been set by SetModelFromButtons, but the publish button might already be showing when we go to this page.
            ShowCropMarks = false; // don't want in online preview
            BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet; // has all the pages and cover in form suitable for online use
            _makePdfBackgroundWorker.RunWorkerAsync();
            // We normally generate PDFs in the background, but this routine should not return until we actually have one.
            while (_makePdfBackgroundWorker.IsBusy)
            {
                System.Threading.Thread.Sleep(100);
                Application.DoEvents(); // Wish we didn't need this, but without it bulk upload freezes making 'preview' which is really the PDF to upload.
            }
            _makePdfBackgroundWorker.ProgressChanged -= UpdatePreviewProgress;
            _previewProgress = null;
            _previousStatus = null;
        }

        IProgress _previewProgress;
        string _previousStatus;

        private void UpdatePreviewProgress(object sender, ProgressChangedEventArgs e)
        {
            if (_previewProgress == null)
                return;
            if (_previewProgress.ProgressIndicator != null)
                _previewProgress.ProgressIndicator.PercentCompleted = e.ProgressPercentage;
            var status = e.UserState as string;
            // Don't repeat a status message, even if modified by trailing spaces or periods or ellipses.
            if (
                status != null
                && status != _previousStatus
                && status.Trim(new[] { ' ', '.', '\u2026' }) != _previousStatus
            )
                _previewProgress.WriteStatus(status);
            _previousStatus = status;
        }

        internal bool IsMakingPdf
        {
            get { return _makePdfBackgroundWorker.IsBusy; }
        }

        internal void CancelMakingPdf()
        {
            _makePdfBackgroundWorker.CancelAsync();
        }

        void _makePdfBackgroundWorker_RunWorkerCompleted(
            object sender,
            System.ComponentModel.RunWorkerCompletedEventArgs e
        )
        {
            if (!e.Cancelled && e.Result is Exception)
                ReportPdfGenerationError(e.Result as Exception);
        }

        internal static void ReportPdfGenerationError(Exception error)
        {
            if (error is ApplicationException)
            {
                //For common exceptions, we catch them earlier (in the worker thread) and give a more helpful message
                //note, we don't want to include the original, as it leads to people sending in reports we don't
                //actually want to see. E.g., we don't want a bug report just because they didn't have Acrobat
                //installed, or they had the PDF open in Word, or something like that.
                ErrorReport.NotifyUserOfProblem(error.Message);
            }
            else if (error is PdfMaker.MakingPdfFailedException)
            {
                // Ignore this error here.  It will be reported elsewhere if desired.
            }
            else if (
                error is FileNotFoundException
                && ((FileNotFoundException)error).FileName == "BloomPdfMaker.exe"
            )
            {
                ErrorReport.NotifyUserOfProblem(error, error.Message);
            }
            else if (error is OutOfMemoryException)
            {
                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-5467.
                var fmt = LocalizationManager.GetString(
                    "PublishTab.PdfMaker.OutOfMemory",
                    "Bloom ran out of memory while making the PDF. See {0}this article{1} for some suggestions to try.",
                    "{0} and {1} are HTML link markup.  You can think of them as fancy quotation marks."
                );
                var msg = String.Format(
                    fmt,
                    "<a href='https://community.software.sil.org/t/solving-memory-problems-while-printing/500'>",
                    "</a>"
                );
                BloomMessageBox.ShowWarning(msg);
            }
            else // for others, just give a generic message and include the original exception in the message
            {
                ErrorReport.NotifyUserOfProblem(
                    error,
                    "Sorry, Bloom had a problem creating the PDF."
                );
            }
        }

        /// <summary>
        /// f the publish tab should show a message indicating that we can't publish
        /// the current book with the current subscription tier, returns the code of the first
        /// feature we find that is a problem, else "".
        /// </summary>
        public FeatureStatus GetFeaturePreventingPublishingOrNull()
        {
            if (BookSelection == null || BookSelection.CurrentSelection == null)
                return null;
            if (BookSelection.CurrentSelection.BookData?.BookIsDerivative() ?? false)
                return null;

            return FeatureStatus.GetFirstFeatureThatIsInvalidForNewBooks(
                // subscription of current collection
                _collectionSettings.Subscription,
                BookSelection.CurrentSelection.RawDom
            );
        }

        /// <summary>
        /// // As of 6.0, we have a problem if we can't bring the book up to date because we're not allowed to save it.
        /// Enhance: We could fairly easily allow publishing a book which was previously checked out
        /// and brought up to date and checked in and is still selected, since the Book object knows it
        /// is already up-to-date.
        /// With quite a bit more work we might be able to make a more permanent record of the version of
        /// Bloom that brought a book up-to-date, the branding, xmatter, and page size in effect at the time,
        /// and so forth, and determine that a book does not need updating.
        /// </summary>
        public bool CannotPublishWithoutCheckout =>
            _currentlyLoadedBook != null && !_currentlyLoadedBook.IsSaveable;

        internal static string GetPreparingImageFilter()
        {
            var msgFmt = L10NSharp.LocalizationManager.GetString(
                "ImageUtils.PreparingImage",
                "Preparing image: {0}",
                "{0} is a placeholder for the image file name"
            );
            var idx = msgFmt.IndexOf("{0}");
            return idx >= 0 ? msgFmt.Substring(0, idx) : msgFmt; // translated string is missing the filename placeholder?
        }

        public void LoadBook(
            BackgroundWorker worker,
            DoWorkEventArgs doWorkEventArgs,
            Control owner = null
        )
        {
            if (owner == null)
                owner = View;
            Debug.Assert(owner != null || Program.RunningInConsoleMode); // must pass if we don't have a view.
            try
            {
                using (var tempHtml = MakeFinalHtmlForPdfMaker())
                {
                    if (doWorkEventArgs.Cancel)
                        return;

                    BookletLayoutMethod layoutMethod = GetBookletLayoutMethod();

                    // Check memory for the benefit of developers.  The user won't see anything.
                    Bloom.Utils.MemoryManagement.CheckMemory(
                        true,
                        "about to create PDF file",
                        false
                    );
                    _pdfMaker.MakePdf(
                        new PdfMakingSpecs()
                        {
                            InputHtmlPath = tempHtml.Key,
                            OutputPdfPath = PdfFilePath,
                            PaperSizeName = PageLayout.SizeAndOrientation.PageSizeName,
                            Landscape = PageLayout.SizeAndOrientation.IsLandScape,
                            SaveMemoryMode = _currentlyLoadedBook.UserPrefs.ReducePdfMemoryUse,
                            LayoutPagesForRightToLeft = LayoutPagesForRightToLeft,
                            BooketLayoutMethod = layoutMethod,
                            BookletPortion = BookletPortion,
                            BookIsFullBleed = _currentlyLoadedBook.FullBleed,
                            PrintWithFullBleed = GetPrintingWithFullBleed(),
                            ColorProfile = _currentlyLoadedBook.UserPrefs.ColorProfileForPdf,
                            HtmlPageCount = this.HtmlPageCount,
                            Author = _currentlyLoadedBook.BookInfo.MetaData.Author,
                            Title = _currentlyLoadedBook.BookInfo.MetaData.Title,
                            Summary = _currentlyLoadedBook.BookInfo.MetaData.Summary,
                            Keywords = GetKeywords(_currentlyLoadedBook.BookInfo.MetaData)
                        },
                        worker,
                        doWorkEventArgs,
                        owner
                    );
                    // Warn the user if we're starting to use too much memory.
                    Bloom.Utils.MemoryManagement.CheckMemory(
                        false,
                        "finished creating PDF file",
                        true
                    );
                }
            }
            catch (Exception e)
            {
                //we can't safely do any ui-related work from this thread, like putting up a dialog
                doWorkEventArgs.Result = e;
                //                SIL.Reporting.ErrorReport.NotifyUserOfProblem(e, "There was a problem creating a PDF from this book.");
                //                SetDisplayMode(DisplayModes.WaitForUserToChooseSomething);
                //                return;
            }
        }

        private string GetKeywords(BookMetaData metaData)
        {
            var keywords = String.Join("\r\n", metaData.Tags);
            if (metaData.Subjects != null)
            {
                foreach (var subject in metaData.Subjects)
                    keywords = keywords + $"\r\n{subject.label}:{subject.value}";
            }
            return keywords.Trim();
        }

        private BookletLayoutMethod GetBookletLayoutMethod()
        {
            BookletLayoutMethod layoutMethod;
            if (this.BookletPortion == BookletPortions.AllPagesNoBooklet)
                layoutMethod = BookletLayoutMethod.NoBooklet;
            else
                layoutMethod = BookSelection.CurrentSelection.GetBookletLayoutMethod(PageLayout);
            return layoutMethod;
        }

        public bool IsCurrentBookFullBleed =>
            _currentlyLoadedBook != null && _currentlyLoadedBook.FullBleed;

        private bool GetPrintingWithFullBleed()
        {
            return _currentlyLoadedBook.FullBleed
                && GetBookletLayoutMethod() == BookletLayoutMethod.NoBooklet
                && _currentlyLoadedBook.UserPrefs.FullBleed;
        }

        private bool LayoutPagesForRightToLeft
        {
            get { return _currentlyLoadedBook.BookData.Language1.IsRightToLeft; }
        }

        public InMemoryHtmlFile MakeFinalHtmlForPdfMaker()
        {
            if (_currentlyLoadedBook == null)
                _currentlyLoadedBook = BookSelection.CurrentSelection;
            PdfFilePath = GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));

            var orientationChanging =
                BookSelection.CurrentSelection.GetLayout().SizeAndOrientation.IsLandScape
                != PageLayout.SizeAndOrientation.IsLandScape;
            if (orientationChanging)
            {
                throw new ApplicationException(
                    "We no longer support creating a PDF in a different orientation from the one set in edit mode"
                );
            }
            var dom = BookSelection.CurrentSelection.GetDomForPrinting(
                BookletPortion,
                _currentBookCollectionSelection.CurrentSelection,
                _bookServer,
                PageLayout
            );

            AddStylesheetClasses(dom.RawDom);
            dom.RawDom.AddClassToBody("pdfPublishMode");

            PageLayout.UpdatePageSplitMode(dom.RawDom);
            if (_currentlyLoadedBook.FullBleed && !GetPrintingWithFullBleed())
            {
                ClipBookToRemoveFullBleed(dom);
            }

            XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom.RawDom);
            dom.UseOriginalImages = true; // don't want low-res images or transparency in PDF.
            var pages = dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            HtmlPageCount = pages.Count;
            // Remove any content that has been generated by an AI engine. (BL-14339)
            foreach (var page in pages)
                PublishHelper.RemoveUnpublishableContent(page);
            PublishHelper.RemoveUnpublishableBookData(dom.RawDom);

            // Show any game pages in the "play" mode.  See BL-14620.
            // During editing, this class is added to the #page-scaling-container element
            // which sits above the page.  That doesn't exist in the PDF's HTML, so we
            // use the body element instead.
            dom.Body.AddClass("drag-activity-play");

            return BloomServer.MakeInMemoryHtmlFileInBookFolder(
                dom,
                source: InMemoryHtmlFileSource.Pub
            );
        }

        private void ClipBookToRemoveFullBleed(HtmlDom dom)
        {
            // example: A5 book is full bleed. What the user saw and configured in Edit mode is RA5 paper, 3mm larger on each side.
            // But we're not printing for full bleed. We will create an A5 page with no inset trim box.
            // We want it to hold the trim box part of the RA5 page.
            // to do this, we simply need to move the bloom-page element up and left by 3mm. Clipping to the page will do the rest.
            // It would be more elegant to do this by introducing a CSS rule involving .bloom-page, but to introduce a new stylesheet
            // we have to make it findable in the book folder, which is messy. Or, we could add a stylesheet element to the DOM;
            // but that's messy, too, we need stuff like /*<![CDATA[*/ to make the content survive the trip from XML to HTML.
            // So it's easiest just to stick it in the style attribute of each page.
            foreach (
                var page in dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
                    .Cast<SafeXmlElement>()
            )
            {
                page.SetAttribute("style", "margin-left: -3mm; margin-top: -3mm;");
            }
        }

        private void AddStylesheetClasses(SafeXmlDocument dom)
        {
            if (this.GetPrintingWithFullBleed())
            {
                dom.AddClassToBody("publishingWithFullBleed");
            }
            else
            {
                dom.AddClassToBody("publishingWithoutFullBleed");
            }
            HtmlDom.AddPublishClassToBody(dom);

            if (LayoutPagesForRightToLeft)
                HtmlDom.AddRightToLeftClassToBody(dom);
            HtmlDom.AddHidePlaceHoldersClassToBody(dom);
            if (
                BookSelection.CurrentSelection.GetDefaultBookletLayoutMethod()
                == PublishModel.BookletLayoutMethod.Calendar
            )
            {
                HtmlDom.AddCalendarFoldClassToBody(dom);
            }
        }

        private string _lastPath;

        private string GetPdfPath(string fname)
        {
            string path = null;

            // Sanitize fileName first
            string fileName = BookStorage.SanitizeNameForFileSystem(fname);

            for (int i = 0; i < 100; i++)
            {
                path = Path.Combine(Path.GetTempPath(), string.Format("{0}-{1}.pdf", fileName, i));
                if (path == _lastPath)
                {
                    // don't use the same path twice in a row; react-pdf won't realize it's a new file
                    // (in the new PDF & Print tab)
                    // and won't update, and other render effects don't happen because it hasn't changed.
                    // But it's pretty surely one of ours, so try to clean it up.
                    // (It doesn't matter if we use the same name in two different runs of Bloom. This is mainly
                    // about switching between cover and inside pages or simple.)
                    if (RobustFile.Exists(path))
                    {
                        try
                        {
                            RobustFile.Delete(path);
                        }
                        catch (Exception) { }
                    }
                    continue;
                }

                if (!RobustFile.Exists(path))
                    break;

                try
                {
                    RobustFile.Delete(path);
                    break;
                }
                catch (Exception)
                {
                    //couldn't delete it? then increment the suffix and try again
                }
            }
            _lastPath = path;
            return path;
        }

        public void Dispose()
        {
            if (RobustFile.Exists(PdfFilePath))
            {
                try
                {
                    RobustFile.Delete(PdfFilePath);
                }
                catch (Exception) { }
            }

            GC.SuppressFinalize(this);
        }

        public BookletPortions BookletPortion { get; set; }

        public Book.Book CurrentBook => BookSelection.CurrentSelection;

        /// <summary>
        /// The book itself has a layout. At one point we allowed it to be overridden
        /// during publishing; that could be reinstated if desired, but is not currently used.
        /// If we turn it back into a variable, it needs to track with the current book.
        /// The earlier code updated it when the book changed, but that was somewhat fragile.
        /// I think it would be better to write code such that it tracks the current book
        /// UNLESS it has been set since that book was selected.
        /// </summary>
        public Layout PageLayout => CurrentBook?.GetLayout() ?? Layout.A5Portrait;

        public bool ShowCropMarks
        {
            get { return _pdfMaker.ShowCropMarks; }
            set { _pdfMaker.ShowCropMarks = value; }
        }

        public bool AllowPdfBooklet
        {
            get
            {
                // Large page sizes can't make booklets.  See http://issues.bloomlibrary.org/youtrack/issue/BL-4155.
                var size = PageLayout.SizeAndOrientation.PageSizeName;
                return BookSelection.CurrentSelection.BookInfo.BookletMakingIsAppropriate
                    && (
                        size != "A4"
                        && size != "A3"
                        && size != "B5"
                        && size != "Letter"
                        && size != "Device16x9"
                    );
            }
        }

        // currently the only cover option we have is a booklet one
        public bool AllowPdfCover => AllowPdfBooklet;

        public void SavePdf()
        {
            try
            {
                // Give a slight preference to USB keys, though if they used a different directory last time, we favor that.

                if (string.IsNullOrEmpty(_lastDirectory) || !Directory.Exists(_lastDirectory))
                {
                    try
                    {
                        var drives = SIL.UsbDrive.UsbDriveInfo.GetDrives();
                        if (drives != null && drives.Count > 0)
                        {
                            _lastDirectory = drives[0].RootDirectory.FullName;
                        }
                    }
                    catch (Exception err)
                    {
                        // If an error occurs while trying to get the USB drive info,
                        // it's not a big deal and doesn't need to terminate the save operation.
                        // Let's just log it and fall through to the rest of the Save() function
                        SIL.Reporting.Logger.WriteError(
                            "Bloom encountered an error while getting list of USB drives.",
                            err
                        );
                    }
                }

                var portion = "";
                switch (BookletPortion)
                {
                    case BookletPortions.None:
                        Debug.Fail("Save should not be enabled");
                        return;
                    case BookletPortions.AllPagesNoBooklet:
                        portion = "Pages";
                        break;
                    case BookletPortions.BookletCover:
                        portion = "Cover";
                        break;
                    case BookletPortions.BookletPages:
                        portion = "Inside";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                string forPrintShop =
                    !string.IsNullOrEmpty(_currentlyLoadedBook.UserPrefs.ColorProfileForPdf)
                    || _currentlyLoadedBook.UserPrefs.FullBleed
                        ? "-printshop"
                        : "";
                var extraTag =
                    $"{_currentlyLoadedBook.GetFilesafeLanguage1Name("en")}-{portion}{forPrintShop}";
                var suggestedName =
                    $"{Path.GetFileName(_currentlyLoadedBook.FolderPath)}-{extraTag}";
                var pdfFileLabel = L10NSharp.LocalizationManager.GetString(
                    @"PublishTab.PdfMaker.PdfFile",
                    "PDF File",
                    @"displayed as file type for Save File dialog."
                );

                pdfFileLabel = pdfFileLabel.Replace("|", "");
                var pdfFilter = String.Format("{0}|*.pdf", pdfFileLabel);

                var initialPath = FilePathMemory.GetOutputFilePath(
                    _currentlyLoadedBook,
                    ".pdf",
                    suggestedName,
                    extraTag,
                    _lastDirectory
                );

                var destFileName = Utils.MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                    initialPath,
                    pdfFilter
                );
                if (String.IsNullOrEmpty(destFileName))
                    return;

                FilePathMemory.RememberOutputFilePath(
                    _currentlyLoadedBook,
                    ".pdf",
                    destFileName,
                    extraTag
                );
                _lastDirectory = Path.GetDirectoryName(destFileName);

                if (!String.IsNullOrEmpty(_currentlyLoadedBook.UserPrefs.ColorProfileForPdf))
                {
                    // PDF for Printshop (CMYK color conversion)
                    ProcessPdfFurtherAndSave(
                        PdfFilePath,
                        ProcessPdfWithGhostscript.OutputType.Printshop,
                        _currentlyLoadedBook.UserPrefs.ColorProfileForPdf,
                        destFileName
                    );
                }
                else
                {
                    // we want the simple PDF we already made.
                    RobustFile.Copy(PdfFilePath, destFileName, true);
                }
                Analytics.Track(
                    "Save PDF",
                    new Dictionary<string, string>()
                    {
                        { "Portion", Enum.GetName(typeof(BookletPortions), BookletPortion) },
                        { "Layout", PageLayout.ToString() },
                        { "BookId", BookSelection.CurrentSelection.ID },
                        { "Country", _collectionSettings.Country }
                    }
                );
                this._currentlyLoadedBook.ReportSimplisticFontAnalytics(
                    FontAnalytics.FontEventType.PublishPdf,
                    "Save PDF"
                );
            }
            catch (Exception err)
            {
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    err,
                    "Bloom was not able to save the PDF.  {0}",
                    err.Message
                );
            }
        }

        internal static void ProcessPdfFurtherAndSave(
            string pdfFilePath,
            ProcessPdfWithGhostscript.OutputType type,
            string colorProfile,
            string outputPath
        )
        {
            if (
                type == ProcessPdfWithGhostscript.OutputType.Printshop
                && !Bloom.Properties.Settings.Default.AdobeColorProfileEula2003Accepted
            )
            {
                var prolog = L10NSharp.LocalizationManager.GetString(
                    @"PublishTab.PrologToAdobeEula",
                    "Bloom uses Adobe color profiles to convert PDF files from using RGB color to using CMYK color.  This is part of preparing a \"PDF for a print shop\".  You must agree to the following license in order to perform this task in Bloom.",
                    @"Brief explanation of what this license is and why the user needs to agree to it"
                );
                using (
                    var dlg = new Bloom.Registration.LicenseDialog(
                        "AdobeColorProfileEULA.htm",
                        prolog
                    )
                )
                {
                    dlg.Text = L10NSharp.LocalizationManager.GetString(
                        @"PublishTab.AdobeEulaTitle",
                        "Adobe Color Profile License Agreement",
                        @"dialog title for license agreement"
                    );
                    if (dlg.ShowDialog() != DialogResult.OK)
                    {
                        var msg = L10NSharp.LocalizationManager.GetString(
                            @"PublishTab.PdfNotSavedWhy",
                            "The PDF file has not been saved because you chose not to allow producing a \"PDF for print shop\".",
                            @"explanation that file was not saved displayed in a message box"
                        );
                        var heading = L10NSharp.LocalizationManager.GetString(
                            @"PublishTab.PdfNotSaved",
                            "PDF Not Saved",
                            @"title for the message box"
                        );
                        MessageBox.Show(
                            msg,
                            heading,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                        return;
                    }
                }
                Bloom.Properties.Settings.Default.AdobeColorProfileEula2003Accepted = true;
                Bloom.Properties.Settings.Default.Save();
            }
            using (var progress = new SIL.Windows.Forms.Progress.ProgressDialog())
            {
                progress.ProgressRangeMinimum = 0;
                progress.ProgressRangeMaximum = 100;
                progress.Overview = L10NSharp.LocalizationManager.GetString(
                    @"PublishTab.PdfMaker.Saving",
                    "Saving PDF...",
                    @"Message displayed in a progress report dialog box"
                );
                progress.BackgroundWorker = new BackgroundWorker();
                progress.BackgroundWorker.DoWork += (object sender, DoWorkEventArgs e) =>
                {
                    var pdfProcess = new ProcessPdfWithGhostscript(
                        type,
                        colorProfile,
                        sender as BackgroundWorker,
                        e
                    );
                    pdfProcess.ProcessPdfFile(pdfFilePath, outputPath);
                };
                progress.BackgroundWorker.ProgressChanged += (
                    object sender,
                    ProgressChangedEventArgs e
                ) =>
                {
                    progress.Progress = e.ProgressPercentage;
                    var status = e.UserState as string;
                    if (!String.IsNullOrWhiteSpace(status))
                        progress.StatusText = status;
                };
                progress.ShowDialog(); // will start the background process when loaded/showing
                if (
                    progress.ProgressStateResult != null
                    && progress.ProgressStateResult.ExceptionThatWasEncountered != null
                )
                {
                    string shortMsg = L10NSharp.LocalizationManager.GetString(
                        @"PublishTab.PdfMaker.ErrorSaving",
                        "Error compressing or recoloring the PDF file",
                        @"Message briefly displayed to the user in a toast"
                    );
                    var longMsg = String.Format(
                        "Exception encountered processing the PDF file: {0}",
                        progress.ProgressStateResult.ExceptionThatWasEncountered
                    );
                    NonFatalProblem.Report(
                        ModalIf.None,
                        PassiveIf.All,
                        shortMsg,
                        longMsg,
                        progress.ProgressStateResult.ExceptionThatWasEncountered
                    );
                }
            }
        }

        public void DebugCurrentPDFLayout()
        {
            var htmlFilePath = MakeFinalHtmlForPdfMaker().Key;
            if (SIL.PlatformUtilities.Platform.IsWindows)
                ProcessExtra.SafeStartInFront(htmlFilePath);
            else
                ProcessExtra.SafeStartInFront("xdg-open", '"' + htmlFilePath + '"');
        }

        public void UpdateModelUponActivation()
        {
            if (BookSelection.CurrentSelection == null)
                return;
            _currentlyLoadedBook = BookSelection.CurrentSelection;

            // At one point this was important for immediately publishing a newly created book
            // (BL-8648). But selecting a book now brings it up to date if it is in the
            // editable collection and Saveable. However, it is possible that since selecting
            // it, the user has checked it out and so it is now possible to update it.
            if (_currentlyLoadedBook.IsSaveable)
                _currentlyLoadedBook.EnsureUpToDate();
        }

        public IEnumerable<HtmlDom> GetPageDoms()
        {
            if (BookSelection.CurrentSelection.IsFolio)
            {
                foreach (var bi in _currentBookCollectionSelection.CurrentSelection.GetBookInfos())
                {
                    var book = _bookServer.GetBookFromBookInfo(bi);
                    //need to hide the "notes for illustrators" on SHRP, which is controlled by the layout
                    book.SetLayout(
                        new Layout()
                        {
                            SizeAndOrientation = SizeAndOrientation.FromString("B5Portrait"),
                            Style = "HideProductionNotes"
                        }
                    );
                    foreach (var page in book.GetPages())
                    {
                        //yield return book.GetPreviewXmlDocumentForPage(page);

                        var previewXmlDocumentForPage = book.GetPreviewXmlDocumentForPage(page);
                        BookStorage.SetBaseForRelativePaths(
                            previewXmlDocumentForPage,
                            book.FolderPath
                        );

                        AddStylesheetClasses(previewXmlDocumentForPage.RawDom);

                        yield return previewXmlDocumentForPage;
                    }
                }
            }
            else //this one is just for testing, it's not especially fruitful to export for a single book
            {
                //need to hide the "notes for illustrators" on SHRP, which is controlled by the layout
                BookSelection.CurrentSelection.SetLayout(
                    new Layout()
                    {
                        SizeAndOrientation = SizeAndOrientation.FromString("B5Portrait"),
                        Style = "HideProductionNotes"
                    }
                );

                foreach (var page in BookSelection.CurrentSelection.GetPages())
                {
                    var previewXmlDocumentForPage =
                        BookSelection.CurrentSelection.GetPreviewXmlDocumentForPage(page);
                    //get the original images, not compressed ones (just in case the thumbnails are, like, full-size & they want quality)
                    BookStorage.SetBaseForRelativePaths(
                        previewXmlDocumentForPage,
                        BookSelection.CurrentSelection.FolderPath
                    );
                    AddStylesheetClasses(previewXmlDocumentForPage.RawDom);
                    yield return previewXmlDocumentForPage;
                }
            }
        }

        /// <summary>
        /// Remove all text data that is not in a desired language.
        /// Keeps all xmatter data if shouldPruneXmatter is false; if it is true, keeps xmatter data in xmatterLangsToKeep.
        /// This is typically all the active languages and metadata languages in the book.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-7124.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-7998 for when we need to prune xmatter pages.
        /// </remarks>
        public static void RemoveUnwantedLanguageData(
            HtmlDom dom,
            IEnumerable<string> languagesToInclude,
            bool shouldPruneXmatter,
            HashSet<string> additionalXmatterLangsToKeep
        )
        {
            //Debug.Write("PublishModel.RemoveUnwantedLanguageData(): languagesToInclude =");
            //foreach (var lang in languagesToInclude)
            //	Debug.Write($" {lang}");
            //Debug.WriteLine();
            // Place the desired language tags plus the two standard pseudolanguage tags in a HashSet
            // for fast access.
            var contentLanguages = new HashSet<string>(languagesToInclude);
            contentLanguages.Add("*");
            var styleLanguages = new HashSet<string>(contentLanguages);
            contentLanguages.Add("z");
            var xmatterLangsToKeep = new HashSet<string>(contentLanguages);
            xmatterLangsToKeep.UnionWith(additionalXmatterLangsToKeep);

            // Don't change the div#bloomDataDiv:  thus we have an outer loop that
            // selects only xmatter and user content pages.
            // While we could probably safely remove elements from div#bloomDataDiv,
            // we decided to play it very safe for now and leave it all intact.
            // The default behavior is also to not touch xmatter pages.  But if the code for the national language (aka L2) is
            // provided, then we prune xmatter pages as well but add the national language to the list of languages whose data
            // we keep in the xmatter.
            // We can always come back to this if we realize we should be removing more.
            // If that happens, removing the outer loop and checking the data-book attribute (and
            // maybe the data-derived attribute) may become necessary.
            foreach (
                var page in dom.RawDom
                    .SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                    .Cast<SafeXmlElement>()
                    .ToList()
            )
            {
                var isXMatter = !String.IsNullOrWhiteSpace(page.GetAttribute("data-xmatter-page"));

                foreach (
                    var div in page.SafeSelectNodes(".//div[@lang]").Cast<SafeXmlElement>().ToList()
                )
                {
                    var lang = div.GetAttribute("lang");
                    if (string.IsNullOrEmpty(lang))
                        continue;
                    if (isXMatter)
                    {
                        if (!shouldPruneXmatter || xmatterLangsToKeep.Contains(lang))
                        {
                            styleLanguages.Add(lang); // keeping data, so should keep any style rules for that data
                            continue;
                        }
                    }
                    else if (contentLanguages.Contains(lang))
                        continue; // keep it. Lang is already in stylesLanguages.
                    var classAttr = div.GetAttribute("class");
                    // retain the .pageLabel and .pageDescription divs (which are always lang='en')
                    // Also retain any .Instructions-style divs, which may have the original with lang='en', and
                    // which are usually translated to the national language.
                    // Also retain any XMatter .licenseDescription divs, which may exist only with lang='en'
                    // REVIEW: are there any other classes that should be checked here?
                    if (
                        classAttr.Contains("pageLabel")
                        || classAttr.Contains("pageDescription")
                        || classAttr.Contains("Instructions-style")
                        || (isXMatter && classAttr.Contains("licenseDescription"))
                    )
                    {
                        styleLanguages.Add(lang);
                        continue;
                    }
                    // check whether any descendant divs are desired before deleting this div.
                    bool deleteDiv = true;
                    foreach (
                        var subdiv in div.SafeSelectNodes(".//div[@lang]")
                            .Cast<SafeXmlElement>()
                            .ToList()
                    )
                    {
                        var sublang = subdiv.GetAttribute("lang");
                        if (String.IsNullOrEmpty(sublang))
                            continue;
                        // We don't need to consider shouldPruneXmatter here, since we won't get here on xmatter pages
                        // when that is false, we will already have decided to keep the outer div.
                        if (
                            contentLanguages.Contains(sublang)
                            || isXMatter && xmatterLangsToKeep.Contains(sublang)
                        )
                        {
                            deleteDiv = false;
                            break;
                        }
                    }
                    // Remove this div
                    if (deleteDiv)
                        div.ParentNode.RemoveChild(div);
                    else if (isXMatter)
                        styleLanguages.Add(lang); // keeping div, keep its lang styles.
                }
            }
            // Remove language-specific style settings for unwanted languages
            var stylesNode = dom.RawDom.SelectSingleNode(
                "//head/style[@type='text/css' and @title='userModifiedStyles']"
            );
            if (stylesNode != null)
            {
                var cssTextOrig = stylesNode.InnerXml; // InnerXml needed to preserve CDATA markup
                // For 5.3, we wholesale keep all L2/L3 rules even though this might result in incorrect error messages about fonts. (BL-11357)
                // In 5.4, we hope to clean up all this font determination stuff by using a real browser to determine what is used.
                var cssText = HtmlDom.RemoveUnwantedLanguageRulesFromCss(
                    cssTextOrig,
                    styleLanguages
                );
                if (cssText != cssTextOrig)
                    stylesNode.InnerXml = cssText;
            }
        }

        /// <summary>
        /// Remove language specific style settings for unwanted languages from all CSS files in the given directory.
        /// </summary>
        public static void RemoveUnwantedLanguageRulesFromCssFiles(
            string dirName,
            IEnumerable<string> wantedLanguages
        )
        {
            foreach (var filepath in Directory.EnumerateFiles(dirName, "*.css"))
            {
                var cssTextOrig = RobustFile.ReadAllText(filepath);
                var cssText = HtmlDom.RemoveUnwantedLanguageRulesFromCss(
                    cssTextOrig,
                    wantedLanguages
                );
                if (cssText != cssTextOrig)
                    RobustFile.WriteAllText(filepath, cssText);
            }
        }

        // This is a highly experimental export which may evolve as we work on this with Age of Learning.
        public void ExportAudioFiles1PerPage()
        {
            var container = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "bloom audio export"
            );
            Directory.CreateDirectory(container);
            var parentFolderForAllOfTheseExports = TemporaryFolder.TrackExisting(container);
            var folderForThisBook = new TemporaryFolder(
                parentFolderForAllOfTheseExports,
                Path.GetFileName(this.BookSelection.CurrentSelection.FolderPath)
            );
            var pageIndex = 0;

            foreach (
                SafeXmlElement pageElement in this.BookSelection.CurrentSelection.GetPageElements()
            )
            {
                ++pageIndex;
                //var durations = new StringBuilder();
                //var accumulatedDuration = 0;
                try
                {
                    // These elements are marked as audio-sentence but we're not sure yet if the user actually recorded them yet
                    var audioSentenceElements = HtmlDom
                        .SelectAudioSentenceElements(pageElement)
                        .Cast<SafeXmlElement>();

                    var mergeFiles = audioSentenceElements
                        .Select(
                            s =>
                                AudioProcessor.GetOrCreateCompressedAudio(
                                    this.BookSelection.CurrentSelection.FolderPath,
                                    s.GetAttribute("id")
                                )
                        )
                        .Where(s => !string.IsNullOrEmpty(s));
                    if (mergeFiles.Any())
                    {
                        // enhance: it would be nice if we could somehow provide info on what should be highlighted and when,
                        // though I don't know how that would work with Age of Learning's PDF viewer.
                        // The following was a start on that before I realized that I don't know how that would be accomplished,
                        // but I'm leaving it here in case I pick it up again.
                        // foreach (var audioSentenceElement in audioSentenceElements)
                        //{
                        //	var id = HtmlDom.GetAttributeValue(audioSentenceElement, "id");
                        //	var element = this.BookSelection.CurrentSelection.OurHtmlDom.SelectSingleNode($"//div[@id='{id}']");
                        //	var duration = HtmlDom.GetAttributeValue(audioSentenceElement, "data-duration");
                        //   Here we would need to determine the duration if data-duration is empty.
                        //	accumulatedDuration += int.Parse(duration);
                        //	durations.AppendLine(accumulatedDuration.ToString() + "\t" + duration);
                        //}
                        var bookName = Path.GetFileName(
                            this.BookSelection.CurrentSelection.FolderPath
                        ); // not title, that isn't sanitized to safe characters
                        var filename =
                            $"{bookName}_{this._currentlyLoadedBook.BookData.Language1.Name}_{pageIndex:0000}.mp3".Replace(
                                ' ',
                                '_'
                            );
                        var combinedAudioPath = Path.Combine(
                            folderForThisBook.FolderPath,
                            filename
                        );
                        var errorMessage = AudioProcessor.MergeAudioFiles(
                            mergeFiles,
                            combinedAudioPath
                        );
                        if (errorMessage != null)
                        {
                            RobustFile.WriteAllText(
                                Path.Combine(
                                    folderForThisBook.FolderPath,
                                    $"error page{pageIndex}.txt"
                                ),
                                errorMessage
                            );
                        }
                        //RobustFile.WriteAllText(Path.Combine(folderForThisBook.FolderPath, $"page{pageIndex} timings.txt"),
                        //	durations.ToString());
                    }
                }
                catch (Exception e)
                {
                    RobustFile.WriteAllText(
                        Path.Combine(folderForThisBook.FolderPath, $"error page{pageIndex}.txt"),
                        e.Message
                    );
                }
            }

            ProcessExtra.SafeStartInFront(folderForThisBook.FolderPath);
        }

        public static string RemoveUnwantedLanguageDataFromAllTitles(
            string allTitles,
            string[] langsWanted
        )
        {
            if (string.IsNullOrWhiteSpace(allTitles))
                return allTitles;
            var allTitlesDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                allTitles
            );
            var badKeys = allTitlesDict.Keys.Where(k => langsWanted.IndexOf(k) < 0).ToArray();
            foreach (var key in badKeys)
                allTitlesDict.Remove(key);
            return JsonConvert.SerializeObject(allTitlesDict);
        }
    }
}
