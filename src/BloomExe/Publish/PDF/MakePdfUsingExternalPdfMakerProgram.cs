using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.ToPalaso;
using Bloom.web;
using L10NSharp;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.Publish.PDF
{
    /// <summary>
    /// This wrapper uses the WebView2PdfMaker.exe program, which we rename to
    /// BloomPdfMaker.exe because AVG likes to quarantine it and we want to
    /// make it look less scary.  Isolating the embedded WebView2 browser in a
    /// separate process appears to let us produce the desired PDF files without
    /// any memory concerns.
    /// </summary>
    class MakePdfUsingExternalPdfMakerProgram
    {
        private BackgroundWorker _worker;

        public void MakePdf(
            PdfMakingSpecs specs,
            BackgroundWorker worker,
            DoWorkEventArgs doWorkEventArgs
        )
        {
            _worker = worker;
#if !__MonoCS__
            // Mono doesn't current provide System.Printing.  Leave the 'if' here to emphasize the
            // system specific nature of the following check.
            if (Platform.IsWindows)
            {
                // Check whether we have a default printer set (or for that matter, any printers).
                // Gecko on Windows requires a default printer for any print operation, even one
                // to a file.  See https://jira.sil.org/browse/BL-1237.
                string errorMessage = null;
                System.Printing.LocalPrintServer printServer = null;
                try
                {
                    printServer = new System.Printing.LocalPrintServer();
                }
                catch (Exception) // System.Printing.PrintQueueException isn't in our System.Printing assembly, so... using Exception
                {
                    // http://issues.bloomlibrary.org/youtrack/issue/BL-4060
                    Logger.WriteEvent(
                        "reproduced BL-4060 when trying to create LocalPrinterServer"
                    );
                }
                if (printServer == null || !printServer.GetPrintQueues().Any())
                {
                    errorMessage = GetNoDefaultPrinterErrorMessage();
                }
                else
                {
                    System.Printing.PrintQueue defaultPrinter;
                    // BL-2535 it's possible get past the above printQueues.Any() but then get
                    // a System.Printing.PrintQueueException exception with "Access Denied" error here, if
                    // the default printer for some reason is no longer "allowed".
                    try
                    {
                        defaultPrinter = System.Printing.LocalPrintServer.GetDefaultPrintQueue();

                        if (defaultPrinter == null || String.IsNullOrEmpty(defaultPrinter.FullName))
                        {
                            errorMessage = GetNoDefaultPrinterErrorMessage();
                        }
                    }
                    catch (Exception error) // System.Printing.PrintQueueException isn't in our System.Printing assembly, so... using Exception
                    {
                        defaultPrinter = null;
                        errorMessage = L10NSharp.LocalizationManager.GetString(
                            @"PublishTab.PDF.Error.PrinterError",
                            "Bloom requires access to a printer in order to make a PDF, even though you are not printing.  Windows gave this error when Bloom tried to access the default printer: {0}",
                            @"Error message displayed in a message dialog box"
                        );
                        errorMessage = string.Format(errorMessage, error.Message);
                    }
                }

                if (errorMessage != null)
                {
                    var exception = new ApplicationException(errorMessage);
                    // Note that if we're being run by a BackgroundWorker, it will catch the exception.
                    // If not, but the caller provides a DoWorkEventArgs, pass the exception through
                    // that object rather than throwing it.
                    if (worker != null || doWorkEventArgs == null)
                        throw exception;
                    doWorkEventArgs.Result = exception;
                    return;
                }
            }
#endif
            if (_worker != null)
                _worker.ReportProgress(
                    0,
                    L10NSharp.LocalizationManager.GetString(
                        @"PublishTab.PdfMaker.MakingFromHtml",
                        "Making PDF from HTML",
                        @"Message displayed in a progress report dialog box"
                    )
                );

            var runner = new SIL.CommandLineProcessing.CommandLineRunner();
            string exePath;
            var bldr = new StringBuilder();
            // Codebase is reliable even when Resharper copies the EXE somewhere else for testing.
            var execDir = BloomFileLocator.GetCodeBaseFolder();
            var fromDirectory = String.Empty;
            var filePath = Path.Combine(execDir, "BloomPdfMaker.exe");
            if (!RobustFile.Exists(filePath))
            {
                var msg = LocalizationManager.GetString(
                    "InstallProblem.BloomPdfMaker",
                    "A component of Bloom, BloomPdfMaker.exe, seems to be missing. This prevents previews and printing. Antivirus software sometimes does this. You may need technical help to repair the Bloom installation and protect this file from being deleted again."
                );
                throw new FileNotFoundException(msg, "BloomPdfMaker.exe"); // must be this class to trigger the right reporting mechanism.
            }
            if (Platform.IsMono)
            {
                exePath = Path.ChangeExtension(filePath, "sh");
            }
            else
            {
                exePath = filePath;
            }

            SetArguments(bldr, specs);
            var arguments = bldr.ToString();

            Logger.WriteEvent($"Running {exePath} with arguments: {arguments}");
            Console.WriteLine($"Running {exePath} with arguments: {arguments}");

            var timeoutInSeconds = 3600;
            if (Program.RunningUnitTests)
                timeoutInSeconds = 20;

            var creating = LocalizationManager.GetString(
                "PublishTab.PdfMaker.Creating",
                "Creating PDF..."
            );
            var socketProgress = new WebSocketProgress(BloomWebSocketServer.Instance, "progress");
            socketProgress.MessageWithoutLocalizing(creating);

            var progress = new NullProgress();
            // NB: WebView2 does not appear to support progress reporting while making PDFs.
            var res = runner.StartWithInvariantCulture(
                exePath,
                arguments,
                Encoding.UTF8,
                fromDirectory,
                timeoutInSeconds,
                progress,
                (msg) =>
                {
                    var parts = msg.Split('|');
                    if (parts.Length == 2 && parts[1].StartsWith("Percent: "))
                    {
                        var percent = int.Parse(parts[1].Substring(@"Percent: ".Length));
                        socketProgress.SendPercent(
                            percent * (100 - ProcessPdfWithGhostscript.kPdfCompressionShare) / 100
                        );
                        if (worker?.CancellationPending ?? false)
                        {
                            doWorkEventArgs.Cancel = true;
                            try
                            {
                                runner.Abort(1);
                            }
                            catch (InvalidOperationException)
                            {
                                // Typically means the process already stopped.
                                // Possibly a race condition between aborting the process and getting another
                                // line of output from it.
                            }
                        }
                    }
                }
            );

            Logger.WriteEvent($"Call to {exePath} completed");
            Console.WriteLine($"Call to {exePath} completed");

            if (res.DidTimeOut || !RobustFile.Exists(specs.OutputPdfPath))
            {
                Logger.WriteEvent(
                    @"***ERROR PDF generation failed: res.StandardOutput = " + res.StandardOutput
                );
                Console.Error.WriteLine(
                    @"***ERROR PDF generation failed: res.StandardOutput = " + res.StandardOutput
                );
                Logger.WriteEvent(
                    @"***ERROR PDF generation failed: res.StandardError = " + res.StandardError
                );
                Console.Error.WriteLine(
                    @"***ERROR PDF generation failed: res.StandardError = " + res.StandardError
                );

                var msg = L10NSharp.LocalizationManager.GetString(
                    @"PublishTab.PDF.Error.Failed",
                    "Bloom was not able to create the PDF file ({0}).{1}{1}Details: BloomPdfMaker (command line) did not produce the expected document.",
                    @"Error message displayed in a message dialog box. {0} is the filename, {1} is a newline character."
                );

                // This message string is intentionally separate because it was added after the previous string had already been localized in most languages.
                // It's not useful to add if we're already in save memory mode.
                var msg2 = specs.SaveMemoryMode
                    ? ""
                    : L10NSharp.LocalizationManager.GetString(
                        @"PublishTab.PDF.Error.TrySinglePage",
                        "The book's images might have exceeded the amount of RAM memory available. Please turn on the \"Use Less Memory\" option which is slower but uses less memory.",
                        @"Error message displayed in a message dialog box"
                    ) + Environment.NewLine;

                var fullMsg =
                    String.Format(msg, specs.OutputPdfPath, Environment.NewLine)
                    + Environment.NewLine
                    + msg2
                    + res.StandardOutput;

                var except = new ApplicationException(fullMsg);
                // Note that if we're being run by a BackgroundWorker, it will catch the exception.
                // If not, but the caller provides a DoWorkEventArgs, pass the exception through
                // that object rather than throwing it.
                if (worker != null || doWorkEventArgs == null)
                    throw except;
                else
                    doWorkEventArgs.Result = except;
            }
            else
            {
                Console.WriteLine(
                    "DEBUG PDF success: res.StandardOutput=\r\n{0}\r\nres.StandardError=\r\n{1}\r\n",
                    res.StandardOutput,
                    res.StandardError
                );
            }
        }

        private static string GetNoDefaultPrinterErrorMessage()
        {
            return L10NSharp.LocalizationManager.GetString(
                @"PublishTab.PDF.Error.NoPrinter",
                "Bloom needs you to have a printer selected on this computer before it can make a PDF, even though you are not printing.  It appears that you might not have a printer set as the default.  Please go to Devices and Printers and select a printer as a default. If you don't have a real printer attached, just select the Microsoft XPS or PDF printers.",
                @"Error message displayed in a message dialog box"
            );
        }

        const double inchesToMM = 25.4;

        const double A4PortraitHeight = 297; // mm
        private const double A4PortraitWidth = 210; // mm
        private const double A3PortraitWidth = A4PortraitHeight;
        private const double A3PortraitHeight = A4PortraitWidth * 2d;
        const double bleedWidth = 3; // mm
        private const double bleedExtra = bleedWidth * 2;

        // Trim size from Kingstone: 10.25 x 6.625 inches
        private const double USComicPortraitHeight = 10.25 * inchesToMM;
        private const double USComicPortraitWidth = 6.625 * inchesToMM;

        private const double Size6x9PortraitHeight = 9 * inchesToMM; // mm
        private const double Size6x9PortraitWidth = 6 * inchesToMM; // mm

        //BottomMarginInMillimeters = 0,
        //TopMarginInMillimeters = 0,
        //LeftMarginInMillimeters = 0,
        //RightMarginInMillimeters = 0,
        //Landscape = landscape,
        //InputHtmlPath = inputHtmlPath,
        //OutputPdfPath = tempOutput.Path,
        //PageSizeName = paperSizeName
        void SetArguments(StringBuilder bldr, PdfMakingSpecs specs)
        {
            bldr.AppendFormat("\"{0}\" \"{1}\"", specs.InputHtmlPath, specs.OutputPdfPath);
            bldr.Append(" --quiet"); // turn off its progress dialog (BL-3721)
            bldr.Append(" -B 0 -T 0 -L 0 -R 0");
            if (specs.PrintWithFullBleed)
            {
                ConfigureFullBleedPageSize(bldr, specs);
            }
            else if (specs.PaperSizeName == "USComic")
            {
                bldr.Append($" -h {USComicPortraitHeight} -w {USComicPortraitWidth}");
            }
            else if (specs.PaperSizeName == "Size6x9")
            {
                bldr.Append($" -h {Size6x9PortraitHeight} -w {Size6x9PortraitWidth}");

                if (specs.Landscape)
                    bldr.Append(" -O landscape");
            }
            else
            {
                var match = Regex.Match(
                    specs.PaperSizeName,
                    @"^(cm|in)(\d+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
                if (match.Success)
                {
                    // Irregular (square) paper size
                    var size = int.Parse(match.Groups[2].Value);
                    if (match.Groups[1].Value == "in")
                        size = (int)(size * inchesToMM); // convert from inches to millimeters
                    else
                        size = size * 10; // convert from cm to mm
                    bldr.AppendFormat(" -h {0} -w {0}", size);
                }
                else
                {
                    bldr.AppendFormat(" -s {0}", specs.PaperSizeName);
                    if (specs.Landscape)
                        bldr.Append(" -O landscape");
                }
            }
            if (!string.IsNullOrEmpty(WebView2Browser.AlternativeWebView2Path))
            {
                // tell webview2 to use the alternative path from WebView2Browser.AlternativeWebView2Path
                bldr.Append($" --webview2-path \"{WebView2Browser.AlternativeWebView2Path}\"");
            }
            //bldr.Append(" --debug");
        }

        private static void ConfigureFullBleedPageSize(StringBuilder bldr, PdfMakingSpecs specs)
        {
            // We will make a non-standard page size that is 6mm bigger in each dimension than the size indicated
            // by the paperSizeName. Unfortunately doing that means we can't just pass the name, we have to figure
            // out the size.
            double height;
            double width;
            switch (specs.PaperSizeName.ToLowerInvariant())
            {
                case "a5":
                    height = A4PortraitWidth + bleedExtra;
                    // we floor because that actually gives us the 148mm that is official
                    width = Math.Floor(A4PortraitHeight / 2) + bleedExtra;
                    break;
                case "a4":
                    height = A4PortraitHeight + bleedExtra;
                    width = A4PortraitWidth + bleedExtra;
                    break;
                case "a3":
                    height = A3PortraitHeight + bleedExtra;
                    width = A3PortraitWidth + bleedExtra;
                    break;
                case "uscomic":
                    height = USComicPortraitHeight + bleedExtra;
                    width = USComicPortraitWidth + bleedExtra;
                    break;
                case "size6x9":
                    height = Size6x9PortraitHeight + bleedExtra;
                    width = Size6x9PortraitWidth + bleedExtra;
                    break;
                default:
                    throw new ArgumentException(
                        "Full bleed printing of paper sizes other than A5, A4, A3, USComic, and Size6x9 is not yet implemented"
                    );
            }

            if (specs.Landscape)
            {
                var temp = height;
                height = width;
                width = temp;
            }

            bldr.Append($" -h {height} -w {width}");
        }
    }

    public class PdfMakingSpecs
    {
        public string InputHtmlPath;
        public string OutputPdfPath;
        public string PaperSizeName; //A0,A1,A2,A3,A4,A5,A6,A7,A8,A9,B0,B1,B10,B2,B3,B4,B5,B6,B7,B8,B9,C5E,Comm10E,DLE,Executive,Folio,Ledger,Legal,Letter,Tabloid,Cm13,USComic
        public bool Landscape; //true if landscape orientation, false if portrait orientation
        public PublishModel.BookletLayoutMethod BooketLayoutMethod; // NoBooklet,SideFold,CutAndStack,Calendar
        public PublishModel.BookletPortions BookletPortion; // None,AllPagesNoBooklet,BookletCover,BookletPages,InnerContent
        public bool LayoutPagesForRightToLeft; // true if RTL, false if LTR layout
        public bool SaveMemoryMode; // true if PDF file is to be produced using less memory (but more time)
        public bool BookIsFullBleed; // True if the book is laid out for full-bleed printing (and Enterprise is enabled)
        public bool PrintWithFullBleed; // True if (BookIsFullBleed and) full bleed is requested in the PdfOptions menu and we're not making a booklet
        public bool Cmyk; // true if the Cmyk option is checked in the PdfOptions menu
        public int HtmlPageCount;

        // metadata
        public string Author;
        public string Title;
        public string Summary;
        public string Keywords;
    }
}
