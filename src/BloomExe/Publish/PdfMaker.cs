using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Edit;
using Bloom.ToPalaso;
using Bloom.Workspace;
using Palaso.Code;
using Palaso.CommandLineProcessing;
using Palaso.IO;
using Palaso.Progress;
using PdfDroplet.LayoutMethods;
using PdfSharp;
using PdfSharp.Drawing;

namespace Bloom.Publish
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
        /// 
        /// </summary>
        /// <param name="inputHtmlPath"></param>
        /// <param name="outputPdfPath"></param>
        /// <param name="paperSizeName">A0,A1,A2,A3,A4,A5,A6,A7,A8,A9,B0,B1,B10,B2,B3,B4,B5,B6,B7,B8,B9,C5E,Comm10E,DLE,Executive,Folio,Ledger,Legal,Letter,Tabloid</param>
        /// <param name="landscape"> </param>
        /// <param name="booketLayoutMethod"> </param>
        /// <param name="bookletPortion"></param>
        /// <param name="doWorkEventArgs"> </param>
        /// <param name="getIsLandscape"></param>
        public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, PublishModel.BookletLayoutMethod booketLayoutMethod, PublishModel.BookletPortions bookletPortion, DoWorkEventArgs doWorkEventArgs)
        {
            Guard.Against(Path.GetExtension(inputHtmlPath) != ".htm",
                          "wkhtmtopdf will croak if the input file doesn't have an htm extension.");

            MakeSimplePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape, doWorkEventArgs);
            if (doWorkEventArgs.Cancel || (doWorkEventArgs.Result != null && doWorkEventArgs.Result is Exception))
                return;
            if (bookletPortion != PublishModel.BookletPortions.AllPagesNoBooklet)
            {
                //remake the pdf by reording the pages (and sometimes rotating, shrinking, etc)
                MakeBooklet(outputPdfPath, paperSizeName, booketLayoutMethod);
            }
        }

		private void MakeSimplePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, DoWorkEventArgs doWorkEventArgs)
		{
			// NOTE: This method creates a ProgressDialogBackground. On Linux this has to happen
			// on the thread that is running our main window, otherwise Gecko might crash. Since
			// we're already running on a background thread we have to use Invoke.
			// The solution implemented here is a hack; it would be better and more efficient to
			// directly create the progress dialog in the calling class (PublishView) and then
			// run this code in the background. Currently we run this code in a background thread
			// and then have ProgressDialogBackground do the work on yet another background
			// thread. However, when porting to Linux this seemed to be to big of a change to do
			// it immediately, therefore this hack.
			if (RunningOnBackgroundThread && FirstForm != null)
			{
				FirstForm.Invoke((Action)(() => MakeSimplePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape, doWorkEventArgs)));
				return;
			}

            var customSizes = new Dictionary<string, string>();
            customSizes.Add("Halfletter", "--page-width 8.5 --page-height 5.5");
            string pageSizeArguments;
            if (!customSizes.TryGetValue(paperSizeName, out pageSizeArguments))
            {
                pageSizeArguments = "--page-size " + paperSizeName; ; //this works too " --page-width 14.8cm --page-height 21cm"
            }

            //wkhtmltopdf chokes on stuff like chinese file names, even if we put the console code page to UTF 8 first (CHCP 65001)
            //so now, we just deal in temp files
            using (var tempInput = TempFile.WithExtension(".htm"))
            {
                File.Delete(tempInput.Path);
                var source = File.ReadAllText(inputHtmlPath);
                //hide all placeholders

                File.WriteAllText(tempInput.Path, source.Replace("placeholder.png", "").Replace("placeHolder.png", ""));
                //File.Copy(inputHtmlPath, tempInput.Path);
                var tempOutput = TempFile.WithExtension(".pdf"); //we don't want to dispose of this
                File.Delete(tempOutput.Path);

                /*--------------------------------DEVELOPERS -----------------------------
                 * 
                 *	Are you trying to debug a disparity between the HTML preview and 
                 *	the PDF output, which should be identical? Some notes:
                 *	
                 * 1) Wkhtmltopdf requires different handling of file names for the local
                 * file system than firefox. So if you open this html, do so in Chrome
                 * instead of Firefox.
                 * 
                 * 2) Wkhtmltopdf violates the HTML requirement that classes are case
                 * sensitive. So it could be that it is using a rule you forgot you
                 * had, and which is not being triggered by the better browsers.
                 * 
                 */
                string exePath = FindWkhtmlToPdf();

                var arguments = string.Format(
                    //	"--no-background " + //without this, we get a thin line on the right side, which turned into a line in the middle when made into a booklet. You could only see it on paper or by zooming in.
                    //Nov 2013: the --no-background cure is worse than the disease. It makes it impossible to have, e.g., grey backgrounds in boxes. The line produced in book lets falls on the fold, 
                    //so that's ok.

                    " --print-media-type " +
                    pageSizeArguments +
                    (landscape ? " -O Landscape " : "") +
#if DEBUG
					" --debug-javascript " +
#endif

 "  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm " +
                    "--disable-smart-shrinking --zoom {0} \"{1}\" \"{2}\"",
                    GetZoomBasedOnScreenDPISettings().ToString(),
                    Path.GetFileName(tempInput.Path), tempOutput.Path);

                ExecutionResult result = null;
                using (var dlg = new ProgressDialogBackground())
                {

                    /* This isn't really working yet (Aug 2012)... I put a day's work into getting Palaso.CommandLineRunner to
                     * do asynchronous reading of the 
                     * nice progress that wkhtml2pdf puts out, and feeding it to the UI. It worked find with a sample utility
                     * (PalasoUIWindowsForms.TestApp.exe). But try as I might, it seems
                     * that the Process doesn't actually deliver wkhtml2pdf's outputs to me until it's all over.
                     * If I run wkhtml2pdf from a console, it gives the progress just fine, as it works.
                     * So there is either a bug in Palaso.CommandLineRunner & friends, or.... ?
                     */


                    //this proves that the ui part here is working... it's something about the wkhtml2pdf that we're not getting the updates in real time... 
                    //		dlg.ShowAndDoWork(progress => result = CommandLineRunner.Run("PalasoUIWindowsForms.TestApp.exe", "CommandLineRunnerTest", null, string.Empty, 60, progress
                    dlg.ShowAndDoWork((progress, args) =>
                    {
                        progress.WriteStatus("Making PDF...");
                        //this is a trick... since we are so far failing to get
                        //the progress out of wkhtml2pdf until it is done,
                        //we at least have this indicator which on win 7 does
                        //grow from 0 to the set percentage with some animation

                        //nb: Later, on a 100page doc, I did get good progress at the end
                        progress.ProgressIndicator.PercentCompleted = 70;
                        result = CommandLineRunner.Run(exePath, arguments, null, Path.GetDirectoryName(tempInput.Path),
                            5 * 60, progress
                            , (s) =>
                            {
                                progress.WriteStatus(s);

                                try
                                {
                                    //this will hopefully avoid the exception below (which we'll swallow anyhow)
                                    if (((BackgroundWorker)args.Argument).IsBusy)
                                    {
                                        //this wakes up the dialog, which then calls the Refresh() we need
                                        ((BackgroundWorker)args.Argument).ReportProgress(100);
                                    }
                                    else
                                    {
#if DEBUG
			                            Debug.Fail("Wanna look into this? Why is the process still reporting back?");
#endif
                                    }
                                }
                                catch (InvalidOperationException error)
                                //"This operation has already had OperationCompleted called on it and further calls are illegal"
                                {
#if DEBUG
			                        Palaso.Reporting.ErrorReport.ReportNonFatalException(error);
#endif
                                    //if not in debug, swallow an complaints about it already being completed (bl-233)
                                }
                            });
                    });
                }

                //var progress = new CancellableNullProgress(doWorkEventArgs);


                Debug.WriteLine(result.StandardError);
                Debug.WriteLine(result.StandardOutput);

                if (!File.Exists(tempOutput.Path))
					throw new ApplicationException(string.Format("Bloom was not able to create the PDF.{0}{0}Details: Wkhtml2pdf did not produce the expected document.", Environment.NewLine));

                try
                {
                    File.Move(tempOutput.Path, outputPdfPath);
                }
                catch (IOException e)
                {
                    //I can't figure out how it happened (since GetPdfPath makes sure the file name is unique),
                    //but we had a report (BL-211) of that move failing.
                    throw new ApplicationException(
						string.Format("Bloom tried to save the file to {0}, but {2} said that it was locked. Please try again.{3}{3}Details: {1}",
							outputPdfPath, e.Message, Palaso.PlatformUtilities.Platform.IsWindows ? "Windows" : "Linux", Environment.NewLine));
				}
			}
		}

		private static bool RunningOnBackgroundThread
		{
			get
			{
				if (Application.OpenForms == null || Application.OpenForms.Count < 1)
					return false;

				return Application.OpenForms[0].InvokeRequired;
			}
		}

		private static Form FirstForm
		{
			get 
			{
				if (Application.OpenForms == null || Application.OpenForms.Count < 1)
					return null;
				return Application.OpenForms[0];
			}
		}

        //About the --zoom parameter. It's a hack to get the pages chopped properly.
        //Notes: Remember, a page border *will make the page that much larger!*
        //		One way to see what's happening without a page border is to make the marginBox visible,
        //		then scroll through and you can see it moving up (if the page (zoom factor) is too small) or down (if page (zoom factor) is too large)
        //		Until Aug 2012, I had 1.091. But with large a4 landscape docs (e.g. calendar), I saw 
        //		that the page was too big, leading to an extra page at the end.
        //		Experimentation showed that 1.041 kept the marge box steady.
        //
        //	In July 2013, I needed to get a 200-300 page b5 book out. With the prior 96DPI setting of 1.041, it was drifting upwards (too small). Upping it to 1.042 solved it.

        // In Auguest 2013, we discovere dthat 1.042 was giving us an extra page even when just doing the cover.
        // I quickly tested 1.0415, and it solved it. I'm putting off doing a "real" solution because the geckofxhtmltopdf
        // solution is already working in another branch.

        private double GetZoomBasedOnScreenDPISettings()
        {
            if (WorkspaceView.DPIOfThisAccount == 96)
            {
                return 1.0415;
            }
            if (WorkspaceView.DPIOfThisAccount == 120)
            {
                return 1.249;
            }
            if (WorkspaceView.DPIOfThisAccount == 144)
            {
                return 1.562;
            }
            return 1.04;
        }

		private string FindWkhtmlToPdf()
		{
			return FileLocator.LocateExecutable("wkhtmltopdf", "wkhtmltopdf.exe");
		}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pdfPath">this is the path where it already exists, and the path where we leave the transformed version</param>
        /// <param name="incomingPaperSize"></param>
        /// <param name="booketLayoutMethod"></param>
        private void MakeBooklet(string pdfPath, string incomingPaperSize, PublishModel.BookletLayoutMethod booketLayoutMethod)
        {
            //TODO: we need to let the user chose the paper size, as they do in PdfDroplet.
            //For now, just assume a size double the original

            PageSize pageSize;
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
                    pageSize = PageSize.Letter;//TODO... what's reasonable?
                    break;
                case "HalfLetter":
                    pageSize = PageSize.Letter;
                    break;
                case "Legal":
                    pageSize = PageSize.Legal;//TODO... what's reasonable?
                    break;
                default:
                    throw new ApplicationException("PdfMaker.MakeBooklet() does not contain a map from " + incomingPaperSize + " to a PdfSharp paper size.");
            }



            using (var incoming = new TempFile())
            {
                File.Delete(incoming.Path);
                File.Move(pdfPath, incoming.Path);

                LayoutMethod method;
                switch (booketLayoutMethod)
                {
                    case PublishModel.BookletLayoutMethod.NoBooklet:
                        method = new NullLayoutMethod();
                        break;
                    case PublishModel.BookletLayoutMethod.SideFold:
                        method = new SideFoldBookletLayouter();
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
                var paperTarget = new PaperTarget("ZZ"/*we're not displaying this anyhwere, so we don't need to know the name*/, pageSize);
                var pdf = XPdfForm.FromFile(incoming.Path);//REVIEW: this whole giving them the pdf and the file too... I checked once and it wasn't wasting effort...the path was only used with a NullLayout option
                method.Layout(pdf, incoming.Path, pdfPath, paperTarget, /*TODO: rightToLeft*/ false, ShowCropMarks);
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
            set
            {
                base.CancelRequested = value;
            }
        }
    }
}
