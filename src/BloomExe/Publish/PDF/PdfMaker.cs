using Bloom.Workspace;
using L10NSharp;
using PdfDroplet.LayoutMethods;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SIL.IO;
using SIL.Progress;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Bloom.Publish.PDF
{
    /// <summary>
    /// Creates a pdf from Html, optionally layed out in various booklet layouts
    /// </summary>
    public class PdfMaker
    {
        /// <summary>
        /// turns on crop marks and TrimBox
        /// </summary>
        public bool ShowCropMarks;

        /// <summary>
        /// Flag whether to compress the PDF file.
        /// </summary>
        /// <remarks>
        /// Do we ever NOT want to do this?
        /// </remarks>
        public bool CompressPdf;

        ///  <summary>
        ///
        ///  </summary>
        /// <param name="specs">All the information about what sort of PDF file to make where</param>
        /// <param name="worker">If not null, the Background worker which is running this task, and may be queried to determine whether a cancel is being attempted</param>
        /// <param name="doWorkEventArgs">The event passed to the worker when it was started. If a cancel is successful, it's Cancel property should be set true.</param>
        /// <param name="owner">A control which can be used to invoke parts of the work which must be done on the ui thread.</param>
        public void MakePdf(
            PdfMakingSpecs specs,
            BackgroundWorker worker,
            DoWorkEventArgs doWorkEventArgs,
            Control owner
        )
        {
            // Try up to 4 times. This is a last-resort attempt to handle BL-361.
            // Most likely that was caused by a race condition in MakePdfUsingGeckofxHtmlToPdfComponent.MakePdf,
            // but as it was an intermittent problem and we're not sure that was the cause, this might help.
            for (int i = 0; i < 4; i++)
            {
                new MakePdfUsingExternalPdfMakerProgram().MakePdf(specs, worker, doWorkEventArgs);

                if (
                    doWorkEventArgs.Cancel
                    || (doWorkEventArgs.Result != null && doWorkEventArgs.Result is Exception)
                )
                    return;
                if (worker?.CancellationPending ?? false)
                {
                    doWorkEventArgs.Cancel = true;
                    return;
                }
                if (RobustFile.Exists(specs.OutputPdfPath))
                    break; // normally the first time
            }
            if (!RobustFile.Exists(specs.OutputPdfPath))
            {
                // Should never happen, but...
                // Review: should we localize these? Hopefully the user never sees it...don't want to increase burden on localizers...
                var message =
                    "Bloom unexpectedly failed to create the PDF. If this happens repeatedly please report it to the developers. Probably it will work if you just try again.";
                var header = "PDF creation failed";
                if (owner != null)
                    owner.Invoke(() =>
                    {
                        MessageBox.Show(message, header, MessageBoxButtons.OK);
                    });
                else
                    Console.WriteLine(message);
                doWorkEventArgs.Result = MakingPdfFailedException.CreatePdfException();
                return;
            }

            try
            {
                // Copying the PDF file at each stage of the process, tagged for page layout, can be useful
                // when debugging the PDF creation process.  I'm reluctant to just delete these lines until
                // the time comes we're fully happy with how the process works.
                /*var pgid = (string.IsNullOrEmpty(specs.PaperSizeName) ? "Custom" : specs.PaperSizeName) + (specs.Landscape ? "-L" : "-P");*/
                /*RobustFile.Copy(specs.OutputPdfPath, System.IO.Path.ChangeExtension(specs.OutputPdfPath, pgid + "-0.pdf"), true);*/
                // Shrink the PDF file, especially if it has large color images.  (BL-3721)
                // Also if the book is full bleed we may need to remove some spurious pages.
                // Removing spurious pages must be done BEFORE we switch pages around to make a booklet!
                // Note: previously compression was the last step, after making a booklet. We moved it before for
                // the reason above. Seems like it would also have performance benefits, if anything, to shrink
                // the file before manipulating it further. Just noting it in case there are unexpected issues.
                var fixPdf = new ProcessPdfWithGhostscript(
                    ProcessPdfWithGhostscript.OutputType.DesktopPrinting,
                    specs.ColorProfile,
                    worker,
                    doWorkEventArgs
                );
                fixPdf.ProcessPdfFile(specs.OutputPdfPath, specs.OutputPdfPath);
                /*RobustFile.Copy(specs.OutputPdfPath, System.IO.Path.ChangeExtension(specs.OutputPdfPath, pgid + "-1.pdf"), true);*/
                AddMetadataAndRemoveBlankPagesIfNecessary(specs);
                /*RobustFile.Copy(specs.OutputPdfPath, System.IO.Path.ChangeExtension(specs.OutputPdfPath, pgid + "-2.pdf"), true);*/
                if (
                    specs.BookletPortion != PublishModel.BookletPortions.AllPagesNoBooklet
                    || specs.PrintWithFullBleed
                )
                {
                    //remake the pdf by reordering the pages (and sometimes rotating, shrinking, etc)
                    MakeBooklet(specs);
                    /*RobustFile.Copy(specs.OutputPdfPath, System.IO.Path.ChangeExtension(specs.OutputPdfPath, pgid + "-3.pdf"), true);*/
                }

                // Check that we got a valid, readable PDF.
                // If we get a reliable fix to BL-932 we can take this out altogether.
                // It's probably redundant, since the compression process would probably fail with this
                // sort of corruption, and we are many generations beyond gecko29 where we observed it.
                // However, we don't have data to reliably reproduce the BL-932, and the check doesn't take
                // long, so leaving it in for now.
                CheckPdf(specs.OutputPdfPath);
            }
            catch (KeyNotFoundException e)
            {
                // This is characteristic of BL-932, where Gecko29 fails to make a valid PDF, typically
                // because the user has embedded a really huge image, something like 4000 pixels wide.
                // We think it could also happen with a very long book or if the user is short of memory.
                // The resulting corruption of the PDF file takes the form of a syntax error in an embedded
                // object so that the parser finds an empty string where it expected a 'generationNumber'
                // (currently line 106 of Parser.cs). This exception is swallowed but leads to an empty
                // externalIDs dictionary in PdfImportedObjectTable, and eventually a new exception trying
                // to look up an object ID at line 121 of that class. We catch that exception here and
                // suggest possible actions the user can take until we find a better solution.
                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                    e,
                    LocalizationManager.GetString(
                        "PublishTab.PdfMaker.BadPdf",
                        "Bloom had a problem making a PDF of this book. You may need technical help or to contact the developers. But here are some things you can try:"
                    )
                        + Environment.NewLine
                        + "- "
                        + LocalizationManager.GetString(
                            "PublishTab.PdfMaker.TryRestart",
                            "Restart your computer and try this again right away"
                        )
                        + Environment.NewLine
                        + "- "
                        + LocalizationManager.GetString(
                            "PublishTab.PdfMaker.TrySmallerImages",
                            "Replace large, high-resolution images in your document with lower-resolution ones"
                        )
                        + Environment.NewLine
                        + "- "
                        + LocalizationManager.GetString(
                            "PublishTab.PdfMaker.TryMoreMemory",
                            "Try doing this on a computer with more memory"
                        )
                );

                RobustFile.Move(specs.OutputPdfPath, specs.OutputPdfPath + "-BAD");
                doWorkEventArgs.Result = MakingPdfFailedException.CreatePdfException();
            }
        }

        /// <summary>
        /// WebView2PdfMaker adds a blank page after each page in full bleed output for some paper sizes.
        /// This checks for the existence of twice as many pages as expected, and if that condition is true,
        /// deletes the even numbered pages.
        /// </summary>
        /// <remarks>
        /// This would be easy to move to a simple command line program if memory use proves to be a problem.
        /// </remarks>
        private void AddMetadataAndRemoveBlankPagesIfNecessary(PdfMakingSpecs specs)
        {
            //Bloom.Utils.MemoryManagement.CheckMemory(true, "about to check for blank pages in full bleed PDF file", false);
            using (var pdfDoc = PdfReader.Open(specs.OutputPdfPath, PdfDocumentOpenMode.Modify))
            {
                pdfDoc.Info.Author = specs.Author;
                pdfDoc.Info.Title = specs.Title;
                pdfDoc.Info.Subject = specs.Summary;
                pdfDoc.Info.Keywords = specs.Keywords;
                if (specs.BookIsFullBleed && pdfDoc.PageCount != specs.HtmlPageCount)
                {
                    var lastEven = 0;
                    if (pdfDoc.PageCount == 2 * specs.HtmlPageCount)
                        lastEven = pdfDoc.PageCount - 1;
                    else if (pdfDoc.PageCount == 2 * specs.HtmlPageCount - 1)
                        lastEven = pdfDoc.PageCount - 2;
                    if (lastEven == 0)
                    {
                        Debug.Assert(
                            pdfDoc.PageCount == specs.HtmlPageCount,
                            $"Unexpected PDF page count = {pdfDoc.PageCount}, html page count = {specs.HtmlPageCount}"
                        );
                        return; /* something is screwy */
                    }
                    for (int i = lastEven; i > 0; i -= 2)
                        pdfDoc.Pages.RemoveAt(i);
                }
                pdfDoc.Save(specs.OutputPdfPath);
            }
            //Bloom.Utils.MemoryManagement.CheckMemory(true, "done checking for blank pages in full bleed PDF file", false);
        }

        public static string GetDistributedColorProfilesFolder()
        {
            var baseFolder = FileLocationUtilities.DirectoryOfApplicationOrSolution;
			var distFolder = Path.Combine(baseFolder, "ColorProfiles", "CMYK");
			if (!Directory.Exists(distFolder))
				distFolder = Path.Combine(baseFolder, "DistFiles", "ColorProfiles", "CMYK");
            return distFolder;
		}

        public static string GetUserColorProfilesFolder()
        {
			var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return Path.Combine(baseFolder, "SIL", "Bloom", "ColorProfiles", "CMYK");
		}

		public class MakingPdfFailedException : Exception
        {
            private MakingPdfFailedException(string message)
                : base(message) { }

            public static MakingPdfFailedException CreatePdfException()
            {
                return new MakingPdfFailedException(
                    LocalizationManager.GetString(
                        "PublishTab.PdfMaker.BadPdfShort",
                        "Bloom had a problem making a PDF of this book."
                    )
                );
            }
        }

        // This is a subset of what MakeBooklet normally does, just enough to make it process the PDF to the
        // point where an exception will be thrown if the file is corrupt as in BL-932.
        // Possibly one day we will find a faster or more comprehensive way of validating a PDF, but this
        // at least catches the problem we know about.
        private static void CheckPdf(string outputPdfPath)
        {
            var pdf = XPdfForm.FromFile(outputPdfPath);
            PdfDocument outputDocument = new PdfDocument();
            outputDocument.PageLayout = PdfPageLayout.SinglePage;
            var page = outputDocument.AddPage();
            using (XGraphics gfx = XGraphics.FromPdfPage(page))
            {
                XRect sourceRect = new XRect(0, 0, pdf.PixelWidth, pdf.PixelHeight);
                // We don't really care about drawing the image of the page here, just forcing the
                // reader to process the PDF file enough to crash if it is corrupt.
                gfx.DrawImage(pdf, sourceRect);
            }
        }

        private void MakeBooklet(PdfMakingSpecs specs)
        {
            //TODO: we need to let the user chose the paper size, as they do in PdfDroplet.
            //For now, just assume a size double the original

            var incomingPaperSize = specs.PaperSizeName;

            PageSize pageSize;
            System.Drawing.Printing.PaperSize customPageSize = null;
            switch (incomingPaperSize)
            {
                case "A3":
                    pageSize = PageSize.A2;
                    break;
                case "A4":
                    pageSize = PageSize.A3;
                    break;
                case "A5":
                    pageSize = PageSize.A4;
                    break;
                case "A6":
                    pageSize = PageSize.A5;
                    break;
                case "B5":
                    pageSize = PageSize.B4;
                    break;
                case "Letter":
                    pageSize = PageSize.Ledger;
                    break;
                case "HalfLetter":
                    pageSize = PageSize.Letter;
                    break;
                case "HalfFolio":
                    pageSize = PageSize.Folio;
                    break;
                case "QuarterLetter":
                    pageSize = PageSize.Statement; // ?? Wikipedia says HalfLetter is aka Statement
                    break;
                case "Legal":
                    pageSize = PageSize.Legal; //TODO... what's reasonable?
                    break;
                case "HalfLegal":
                    pageSize = PageSize.Legal;
                    break;
                case "Cm13":
                    pageSize = PageSize.A3;
                    break;
                case "USComic":
                    pageSize = PageSize.A3; // Ledger would work as well.
                    break;
                case "Size6x9":
                    // 9"x12" can hold two 6"x9" pages.
                    pageSize = PageSize.Undefined;
                    customPageSize = new System.Drawing.Printing.PaperSize(
                        "9\"x12\"",
                        9 * 100,
                        12 * 100
                    );
                    break;
                default:
                    throw new ApplicationException(
                        "PdfMaker.MakeBooklet() does not contain a map from "
                            + incomingPaperSize
                            + " to a PdfSharp paper size."
                    );
            }

            using (var incoming = new TempFile())
            {
                RobustFile.Delete(incoming.Path);
                RobustFile.Move(specs.OutputPdfPath, incoming.Path);

                LayoutMethod method;
                switch (specs.BooketLayoutMethod)
                {
                    case PublishModel.BookletLayoutMethod.NoBooklet:
                        method = new NullLayoutMethod(specs.PrintWithFullBleed ? 3 : 0);
                        break;
                    case PublishModel.BookletLayoutMethod.SideFold:
                        // To keep the GUI simple, we assume that A6 page size for booklets
                        // implies 4up printing on A4 paper.  This feature was requested by
                        // https://jira.sil.org/browse/BL-1059 "A6 booklets should print 4
                        // to an A4 sheet".  The same is done for QuarterLetter booklets
                        // printing on Letter size sheets.
                        if (incomingPaperSize == "A6")
                        {
                            method = new SideFold4UpBookletLayouter();
                            pageSize = PageSize.A4;
                        }
                        else if (incomingPaperSize == "QuarterLetter")
                        {
                            method = new SideFold4UpBookletLayouter();
                            pageSize = PageSize.Letter;
                        }
                        else if (incomingPaperSize == "Cm13")
                        {
                            method = new Square6UpBookletLayouter();
                        }
                        else
                        {
                            method = new SideFoldBookletLayouter();
                        }
                        break;
                    case PublishModel.BookletLayoutMethod.CutAndStack:
                        method = new CutLandscapeLayout();
                        break;
                    case PublishModel.BookletLayoutMethod.Calendar:
                        method = new CalendarLayouter();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("booketLayoutMethod");
                }

                PaperTarget paperTarget;
                const string paperTargetName = "ZZ"; // we're not displaying this anywhere, so we don't need to know the name
                if (pageSize != PageSize.Undefined)
                {
                    paperTarget = new PaperTarget(paperTargetName, pageSize);
                }
                else
                {
                    if (customPageSize == null)
                        throw new NullReferenceException(
                            "customPageSize must be set if pageSize is Undefined, but customPageSize was null."
                        );

                    paperTarget = new PaperTarget(paperTargetName, customPageSize);
                }

                var pdf = XPdfForm.FromFile(incoming.Path); //REVIEW: this whole giving them the pdf and the file too... I checked once and it wasn't wasting effort...the path was only used with a NullLayout option
                method.Layout(
                    pdf,
                    incoming.Path,
                    specs.OutputPdfPath,
                    paperTarget,
                    specs.LayoutPagesForRightToLeft,
                    ShowCropMarks
                );
            }
        }
    }

    internal class CancellableNullProgress : NullProgress
    {
        private readonly DoWorkEventArgs _doWorkEventArgs;

        public CancellableNullProgress(DoWorkEventArgs doWorkEventArgs)
        {
            _doWorkEventArgs = doWorkEventArgs;
        }

        public override bool CancelRequested
        {
            get { return _doWorkEventArgs.Cancel; }
            set { base.CancelRequested = value; }
        }
    }
}
