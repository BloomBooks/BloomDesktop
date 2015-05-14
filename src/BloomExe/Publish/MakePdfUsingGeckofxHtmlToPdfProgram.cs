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
using Palaso.CommandLineProcessing;
using Palaso.IO;
using Palaso.Progress;
using Palaso.PlatformUtilities;
using System.Text;

namespace Bloom.Publish
{
	/// <summary>
	/// This wrapper uses the GeckoFxHtmlToPdf program.  Trying to use the component
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
			bool landscape, Control owner, BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs)
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
				var printQueues = printServer.GetPrintQueues();
				bool fOkay = printQueues.Any();
				if (fOkay)
				{
					var defaultPrinter = System.Printing.LocalPrintServer.GetDefaultPrintQueue();
					fOkay = defaultPrinter != null && !String.IsNullOrEmpty(defaultPrinter.FullName);
				}
				if (!fOkay)
				{
					var msg = L10NSharp.LocalizationManager.GetString(@"Errors.MakePDF.NoPrinter",
						"Bloom needs you to have a printer selected on this computer before it can make a PDF, even though you are not printing.  It appears that you do not have a printer selected.  Please go to Devices and Printers and add a printer.");
					var except = new ApplicationException(msg);
					// Note that if we're being run by a BackgroundWorker, it will catch the exception.
					// If not, but the caller provides a DoWorkEventArgs, pass the exception through
					// that object rather than throwing it.
					if (worker != null || doWorkEventArgs == null)
						throw except;
					doWorkEventArgs.Result = except;
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
			var filePath = Path.Combine(execDir, "GeckofxHtmlToPdf.exe");
			if (!File.Exists(filePath))
			{
				var msg = LocalizationManager.GetString("Errors.MakePDF.MissingGenenerator",
					"A component of Bloom, GeckofxHtmlToPdf.exe, seems to be missing. This prevents previews and printing. Check your antivirus quarantine to see if it got put there. You may need technical help to repair the Bloom installation and protect this file from being deleted again.");
				throw new FileNotFoundException(msg, "GeckofxHtmlToPdf.exe"); // must be this class to trigger the right reporting mechanism.
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
			SetArguments(bldr, inputHtmlPath, outputPdfPath, paperSizeName, landscape);
			var arguments = bldr.ToString();
			var progress = new NullProgress();
			var res = runner.Start(exePath, arguments, Encoding.UTF8, fromDirectory, 3600, progress, null);
			
			if (res.DidTimeOut || !File.Exists(outputPdfPath))
			{
				//We're leaving it to the higher levels to give the localized "uh oh" message. Here, we're constructing the 
				//details that will end up in the report if they send it.
				
				var msg = res.DidTimeOut ? "Timed Out" : "GeckofxHtmlToPdf did not produce the expected document at "+outputPdfPath;
				msg += "\r\n\r\nGeckofxHtmlToPdf Output:\r\n"+res.StandardOutput;

				var error = new ApplicationException(msg);
				// Note that if we're being run by a BackgroundWorker, it will catch the exception.
				// If not, but the caller provides a DoWorkEventArgs, pass the exception through
				// that object rather than throwing it.
				if (worker != null || doWorkEventArgs == null)
					throw error;
				else
					doWorkEventArgs.Result = error;
			}
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
			string paperSizeName, bool landscape)
		{
			bldr.AppendFormat("\"{0}\" \"{1}\"", inputHtmlPath, outputPdfPath);
			bldr.AppendFormat(" -B0 -T0 -L0 -R0 -s {0}", paperSizeName);
			bldr.Append(" --graphite");
			if (landscape)
				bldr.Append(" -Landscape");
		}
	}
}
