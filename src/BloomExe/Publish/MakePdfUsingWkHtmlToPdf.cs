using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Bloom.ToPalaso;
using Bloom.Workspace;
using Palaso.CommandLineProcessing;
using Palaso.IO;

namespace Bloom.Publish
{
	/// <summary>
	/// Uses wkhtmltopdf, a command-line program that uses QT and Webkit.
	/// </summary>
	internal class MakePdfUsingWkHtmlToPdf
	{
		//About the --zoom parameter. It's a hack to get the pages chopped properly.
		//Notes: Remember, a page border *will make the page that much larger!*
		//		One way to see what's happening without a page border is to make the marginBox visible,
		//		then scroll through and you can see it moving up (if the page (zoom factor) is too small) or down (if page (zoom factor) is too large)
		//		Until Aug 2012, I had 1.091. But with large a4 landscape docs (e.g. calendar), I saw
		//		that the page was too big, leading to an extra page at the end.
		//		Experimentation showed that 1.041 kept the marge box steady.
		//
		//	In July 2013, I needed to get a 200-300 page b5 book out. With the prior 96DPI setting of 1.041, it was drifting upwards (too small). Upping it to 1.042 solved it.

		private double GetZoomBasedOnScreenDPISettings()
		{
			if (WorkspaceView.DPIOfThisAccount == 96)
			{
				return 1.042;
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
			var exePath = Path.Combine(FileLocator.DirectoryOfTheApplicationExecutable, "wkhtmltopdf");
			exePath = Path.Combine(exePath, "wkhtmltopdf.exe");
			if (!File.Exists(exePath))
			{
				//if this is a programmer, it should be in the lib directory
				exePath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution, Path.Combine("lib", "wkhtmltopdf"));
				exePath = Path.Combine(exePath, "wkhtmltopdf.exe");
				if (!File.Exists(exePath))
				{
					throw new ApplicationException("Could not find a file that should have been installed with Bloom: " + exePath);
				}
			}
			return exePath;
		}

		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape)
		{
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
				//wkhtmltopdf can't handle file://
				source = source.Replace("file://", "");

				//hide all placeholders
				source = source.Replace("placeholder.png", "").Replace("placeHolder.png", "");

				File.WriteAllText(tempInput.Path, source);
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
					"--no-background " +
					//without this, we get a thin line on the right side, which turned into a line in the middle when made into a booklet. You could only see it on paper or by zooming in.
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
						//the progress otu of wkhtml2pdf until it is done,
						//we at least have this indicator which on win 7 does
						//grow from 0 to the set percentage with some animation
						progress.ProgressIndicator.PercentCompleted = 70;
						result = CommandLineRunner.Run(exePath, arguments, null, Path.GetDirectoryName(tempInput.Path),
													   5 * 60, progress
													   , (s) =>
													   {
														   progress.WriteStatus(s);

														   try
														   {
															   //this wakes up the dialog, which then calls the Refresh() we need
															   ((BackgroundWorker)args.Argument).ReportProgress(100);
														   }
														   catch (Exception)
														   {
#if DEBUG
															   throw;
#endif
															   //swallow an complaints about it already being completed (bl-233)
														   }
													   });
					});
				}

				//var progress = new CancellableNullProgress(doWorkEventArgs);


				Debug.WriteLine(result.StandardError);
				Debug.WriteLine(result.StandardOutput);

				if (!File.Exists(tempOutput.Path))
					throw new ApplicationException("Bloom was not able to create the PDF.\r\n\r\nDetails: Wkhtml2pdf did not produce the expected document.");

				try
				{
					File.Move(tempOutput.Path, outputPdfPath);
				}
				catch (IOException e)
				{
					//I can't figure out how it happened (since GetPdfPath makes sure the file name is unique),
					//but we had a report (BL-211) of that move failing.
					throw new ApplicationException(
							string.Format("Bloom tried to save the file to {0}, but Windows said that it was locked. Please try again.\r\n\r\nDetails: {1}",
										  outputPdfPath, e.Message));

				}


			}
		}
	}
}
