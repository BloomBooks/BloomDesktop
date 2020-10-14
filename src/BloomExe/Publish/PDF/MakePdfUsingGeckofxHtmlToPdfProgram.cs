using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using L10NSharp;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.Publish.PDF
{
	/// <summary>
	/// This wrapper uses the GeckoFxHtmlToPdf program, which we rename to
	/// BloomPdfMaker.exe because AVG likes to quarantine it and we want to
	/// make it look less scary.  Trying to use the component
	/// directly leads to obscure bugs, at least on Windows.  Isolating the embedded
	/// Gecko browser in a separate process appears to at least let us produce the
	/// desired PDF files.
	/// </summary>
	/// <remarks>
	/// The program always seems to fail after it finishes while xulrunner is trying
	/// to clean up its COM objects.  Running the program standalone reveals this on
	/// Linux by printing a scary SIGSEGV message and stack trace.  On Windows, it
	/// brings up the "The Program Has Stopped Working" dialog which is even scarier.
	/// (I have seen the corresponding Linux dialog once after running this program.)
	/// However, the problem occurs after the desired output file has been safely
	/// written to the disk, and is nicely hidden away from the user by running it
	/// via CommandLineRunner.
	/// </remarks>
	class MakePdfUsingGeckofxHtmlToPdfProgram
	{
		private BackgroundWorker _worker;

		public void MakePdf(PdfMakingSpecs specs, Control owner, BackgroundWorker worker,
			DoWorkEventArgs doWorkEventArgs)
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
					Logger.WriteEvent("reproduced BL-4060 when trying to create LocalPrinterServer");
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

						if(defaultPrinter == null || String.IsNullOrEmpty(defaultPrinter.FullName))
						{
							errorMessage = GetNoDefaultPrinterErrorMessage();
						}
					}
					catch(Exception error) // System.Printing.PrintQueueException isn't in our System.Printing assembly, so... using Exception
					{
						defaultPrinter = null;
						errorMessage = L10NSharp.LocalizationManager.GetString(@"PublishTab.PDF.Error.PrinterError",
							"Bloom requires access to a printer in order to make a PDF, even though you are not printing.  Windows gave this error when Bloom tried to access the default printer: {0}",
							@"Error message displayed in a message dialog box");
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
				_worker.ReportProgress(0, L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.MakingFromHtml",
					"Making PDF from HTML",
					@"Message displayed in a progress report dialog box"));

			var runner = new CommandLineRunner();
			string exePath;
			var bldr = new StringBuilder();
			// Codebase is reliable even when Resharper copies the EXE somewhere else for testing.
			var execDir = BloomFileLocator.GetCodeBaseFolder();
			var fromDirectory = String.Empty;
			var filePath = Path.Combine(execDir, "BloomPdfMaker.exe");
			if (!RobustFile.Exists(filePath))
			{
				var msg = LocalizationManager.GetString("InstallProblem.BloomPdfMaker",
					"A component of Bloom, BloomPdfMaker.exe, seems to be missing. This prevents previews and printing. Antivirus software sometimes does this. You may need technical help to repair the Bloom installation and protect this file from being deleted again.");
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
			var progress = new NullProgress();
			var res = runner.Start(exePath, arguments, Encoding.UTF8, fromDirectory, 3600, progress,
				ProcessGeckofxReporting);
			if (res.DidTimeOut || !RobustFile.Exists(specs.OutputPdfPath))
			{
				Logger.WriteEvent(@"***ERROR PDF generation failed: res.StandardOutput = " + res.StandardOutput);

				var msg = L10NSharp.LocalizationManager.GetString(@"PublishTab.PDF.Error.Failed",
					"Bloom was not able to create the PDF file ({0}).{1}{1}Details: BloomPdfMaker (command line) did not produce the expected document.",
					@"Error message displayed in a message dialog box. {0} is the filename, {1} is a newline character.");

				// This message string is intentionally separate because it was added after the previous string had already been localized in most languages.
				// It's not useful to add if we're already in save memory mode.
				var msg2 = specs.SaveMemoryMode ? "" : L10NSharp.LocalizationManager.GetString(@"PublishTab.PDF.Error.TrySinglePage",
					"The book's images might have exceeded the amount of RAM memory available. Please turn on the \"Use Less Memory\" option which is slower but uses less memory.",
					@"Error message displayed in a message dialog box") + Environment.NewLine;

				var fullMsg = String.Format(msg, specs.OutputPdfPath, Environment.NewLine) + Environment.NewLine +
				              msg2 + res.StandardOutput;

				var except = new ApplicationException(fullMsg);
				// Note that if we're being run by a BackgroundWorker, it will catch the exception.
				// If not, but the caller provides a DoWorkEventArgs, pass the exception through
				// that object rather than throwing it.
				if (worker != null || doWorkEventArgs == null)
					throw except;
				else
					doWorkEventArgs.Result = except;
			}
		}

		private static string GetNoDefaultPrinterErrorMessage()
		{
			return L10NSharp.LocalizationManager.GetString(@"PublishTab.PDF.Error.NoPrinter",
				"Bloom needs you to have a printer selected on this computer before it can make a PDF, even though you are not printing.  It appears that you might not have a printer set as the default.  Please go to Devices and Printers and select a printer as a default. If you don't have a real printer attached, just select the Microsoft XPS or PDF printers.",
				@"Error message displayed in a message dialog box");
		}

		const double A4PortraitHeight = 297; // mm
		private const double A4PortraitWidth = 210; // mm
		private const double A3PortraitWidth = A4PortraitHeight;
		private const double A3PortraitHeight = A4PortraitWidth * 2d;
		const double bleedWidth = 3; // mm
		private const double bleedExtra = bleedWidth * 2;
		private const double USComicPortraitHeight = 10.5 * 25.4;
		private const double USComicPortraitWidth = 6.75 * 25.4;

		//BottomMarginInMillimeters = 0,
		//TopMarginInMillimeters = 0,
		//LeftMarginInMillimeters = 0,
		//RightMarginInMillimeters = 0,
		//EnableGraphite = true,
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
				bldr.Append(" -h 266.7 -w 171.45"); // 10.5" x 6.75"
			}
			else
			{
				var match = Regex.Match(specs.PaperSizeName, @"^(cm|in)(\d+)$",
					RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
				if (match.Success)
				{
					// Irregular (square) paper size
					var size = int.Parse(match.Groups[2].Value);
					if (match.Groups[1].Value == "in")
						size = (int) (size * 25.4); // convert from inches to millimeters
					else
						size = size * 10; // convert from cm to mm
					bldr.AppendFormat(" -h {0} -w {0}", size);
				}
				else
				{
					bldr.AppendFormat(" -s {0}", specs.PaperSizeName);
					if (specs.Landscape)
						bldr.Append(" -Landscape");
				}
			}

			bldr.Append(" --graphite");
			if (specs.SaveMemoryMode)
				bldr.Append(" --reduce-memory-use");
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
				case "a3":
					height = A3PortraitHeight + bleedExtra;
					width = A3PortraitWidth + bleedExtra;
					break;
				case "uscomic":
					height = USComicPortraitHeight + bleedExtra;
					width = USComicPortraitWidth + bleedExtra;
					break;
				default:
					throw new ArgumentException("Full bleed printing of paper sizes other than A5, A3, and USComic is not yet implemented");
			}

			if (specs.Landscape)
			{
				var temp = height;
				height = width;
				width = temp;
			}

			bldr.Append($" -h {height} -w {width}");
		}

		// Progress report lines from GeckofxHtmlToPdf/BloomPdfMaker look like the following:
		// "Status: Making PDF..|Percent: 100"
		// "Status: Making Page 1 of PDF...|Percent: 100"
		// "Status: Making Page 2 of PDF...|Percent: 0"
		// "Status: Finished|Percent: 100"
		private const string kStatus = "Status: ";
		private const string kPercent = "|Percent: ";
		private const string kMakingPDF = "Making PDF";
		private const string kMakingPage = "Making Page ";
		private const string kOfPDF = " of PDF";
		private const string kFinished = "Finished";

		private void ProcessGeckofxReporting(string line)
		{
			//Debug.WriteLine(String.Format("DEBUG GeckofxHtmlToPdf report line = \"{0}\"", line));
			if (_worker == null || !line.StartsWith(kStatus) || !line.Contains(kPercent))
				return;
			int statusLength = line.IndexOf(kPercent) - kStatus.Length;
			var status = line.Substring(kStatus.Length, statusLength);
			if (String.IsNullOrWhiteSpace(status))
			{
				status = null;
			}
			else
			{
				if (status.StartsWith(kMakingPDF))
				{
					status = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.MakingFromHtml",
						"Making PDF from HTML ...",
						@"Message displayed in a progress report dialog box");
									}
				else if (status.StartsWith(kMakingPage) && status.Contains(kOfPDF))
				{
					int page;
					if (Int32.TryParse(status.Substring(kMakingPage.Length, status.IndexOf(kOfPDF) - kMakingPage.Length), out page))
					{
						status = String.Format(L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.MakingPageOfPdf",
							"Making Page {0} of the PDF",
							@"Message displayed in a progress report dialog box, {0} is replaced by the page number"), page);
					}
					else
					{
						status = null;
					}
				}
				else if (status == kFinished)
				{
					status = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.Finished",
						"Finished making PDF from HTML",
						@"Message displayed in a progress report dialog box");
				}
			}
			int percent;
			if (Int32.TryParse(line.Substring(line.IndexOf(kPercent) + kPercent.Length), out percent))
			{
				_worker.ReportProgress(percent, status);
			}
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
	}
}
