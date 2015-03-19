using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Bloom.ToPalaso;
using Palaso.CommandLineProcessing;
using Palaso.IO;
using Palaso.Progress;
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
			var runner = new CommandLineRunner();
			string exePath;
			var bldr = new StringBuilder();
			var loc = Assembly.GetExecutingAssembly().Location;
			var execDir = Path.GetDirectoryName(loc);
			var fromDirectory = String.Empty;
			if (Environment.OSVersion.Platform == PlatformID.Unix)
			{
				exePath = "mono";
				bldr.AppendFormat("--debug \"{0}\" ", Path.Combine(execDir, "GeckofxHtmlToPdf.exe"));
			}
			else
			{
				exePath = Path.Combine(execDir, "GeckofxHtmlToPdf.exe");
			}
			SetArguments(bldr, inputHtmlPath, outputPdfPath, paperSizeName, landscape);
			var arguments = bldr.ToString();
			var progress = new NullProgress();
			var res = runner.Start(exePath, arguments, Encoding.UTF8, fromDirectory, 3600, progress, null);
			if (res.DidTimeOut || !File.Exists (outputPdfPath))
			{
#if DEBUG
				Console.WriteLine(@"PDF generation failed: res.StandardOutput =");
				Console.WriteLine(res.StandardOutput);
#endif
				var except = new ApplicationException(string.Format(
					"Bloom was not able to create the PDF file ({0}).{1}{1}" +
					"Details: GeckofxHtmlToPdf (command line) did not produce the expected document.",
					outputPdfPath, Environment.NewLine));
				// Note that if we're being run by a BackgroundWorker, it will catch the exception.
				// If not, but the caller provides a DoWorkEventArgs, pass the exception through
				// that object rather than throwing it.
				if (worker != null || doWorkEventArgs == null)
					throw except;
				else
					doWorkEventArgs.Result = except;
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
