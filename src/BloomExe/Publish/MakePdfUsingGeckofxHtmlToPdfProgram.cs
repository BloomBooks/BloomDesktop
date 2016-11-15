using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Bloom.ToPalaso;
using L10NSharp;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;
using SIL.PlatformUtilities;
using System.Text;
using SIL.Reporting;

namespace Bloom.Publish
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
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName,
			bool landscape, bool saveMemoryMode, Control owner, BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs)
		{
#if !__MonoCS__
			// Mono doesn't current provide System.Printing.  Leave the 'if' here to emphasize the
			// system specific nature of the following check.
			if (Platform.IsWindows)
			{
				// Check whether we have a default printer set (or for that matter, any printers).
				// Gecko on Windows requires a default printer for any print operation, even one
				// to a file.  See https://jira.sil.org/browse/BL-1237.
				var printServer = new System.Printing.LocalPrintServer();
				string errorMessage = null;
				if (!printServer.GetPrintQueues().Any())
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
						errorMessage = L10NSharp.LocalizationManager.GetDynamicString(@"Bloom", @"MakePDF.PrinterError",
							"Bloom requires access to a printer in order to make a PDF, even though you are not printing.  Windows gave this error when Bloom tried to access the default printer: {0}",
							@"Error message displayed in a message dialog box");
						errorMessage = string.Format(errorMessage, error.Message);
					}
				}
				if (errorMessage !=null)
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
			var runner = new CommandLineRunner();
			string exePath;
			var bldr = new StringBuilder();
			// Codebase is reliable even when Resharper copies the EXE somewhere else for testing.
			var loc = Assembly.GetExecutingAssembly().CodeBase.Substring((Platform.IsUnix ? "file://" : "file:///").Length);
			var execDir = Path.GetDirectoryName(loc);
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
				exePath = "mono";
				bldr.AppendFormat("--debug \"{0}\" ", filePath);
			}
			else
			{
				exePath = filePath;
			}
			SetArguments(bldr, inputHtmlPath, outputPdfPath, paperSizeName, landscape, saveMemoryMode);
			var arguments = bldr.ToString();
			var progress = new NullProgress();
			var res = runner.Start(exePath, arguments, Encoding.UTF8, fromDirectory, 3600, progress, null);
			if (res.DidTimeOut || !RobustFile.Exists (outputPdfPath))
			{
				Logger.WriteEvent(@"***ERROR PDF generation failed: res.StandardOutput = "+res.StandardOutput);

				var msg = L10NSharp.LocalizationManager.GetDynamicString(@"Bloom", @"MakePDF.Failed",
					"Bloom was not able to create the PDF file ({0}).{1}{1}Details: BloomPdfMaker (command line) did not produce the expected document.",
					@"Error message displayed in a message dialog box. {0} is the filename, {1} is a newline character.");

				// This message string is intentionally separate because it was added after the previous string had already been localized in most languages.
				var msg2 = L10NSharp.LocalizationManager.GetDynamicString(@"Bloom", @"MakePDF.TrySinglePage",
					"The book's images might have exceeded the amount of RAM memory available. Please turn on the \"Use Less Memory\" option which is slower but uses less memory.",
					@"Error message displayed in a message dialog box");

				var fullMsg = String.Format(msg, outputPdfPath, Environment.NewLine) + Environment.NewLine + msg2 + Environment.NewLine + res.StandardOutput;

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
			return L10NSharp.LocalizationManager.GetDynamicString(@"Bloom", @"MakePDF.NoPrinter",
				"Bloom needs you to have a printer selected on this computer before it can make a PDF, even though you are not printing.  It appears that you might not have a printer set as the default.  Please go to Devices and Printers and select a printer as a default. If you don't have a real printer attached, just select the Microsoft XPS or PDF printers.",
				@"Error message displayed in a message dialog box");
		}

		//BottomMarginInMillimeters = 0,
		//TopMarginInMillimeters = 0,
		//LeftMarginInMillimeters = 0,
		//RightMarginInMillimeters = 0,
		//EnableGraphite = true,
		//Landscape = landscape,
		//InputHtmlPath = inputHtmlPath,
		//OutputPdfPath = tempOutput.Path,
		//PageSizeName = paperSizeName
		void SetArguments(StringBuilder bldr, string inputHtmlPath, string outputPdfPath,
			string paperSizeName, bool landscape, bool saveMemoryMode)
		{
			bldr.AppendFormat("\"{0}\" \"{1}\"", inputHtmlPath, outputPdfPath);
			bldr.AppendFormat(" -B 0 -T 0 -L 0 -R 0 -s {0}", paperSizeName);
			bldr.Append(" --graphite");
			if (landscape)
				bldr.Append(" -Landscape");
			if (saveMemoryMode)
				bldr.Append(" --reduce-memory-use");
		}
	}
}
