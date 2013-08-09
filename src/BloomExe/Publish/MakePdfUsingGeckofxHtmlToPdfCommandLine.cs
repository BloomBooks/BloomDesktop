using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Bloom.ToPalaso;
using Palaso.CommandLineProcessing;
using Palaso.IO;

namespace Bloom.Publish
{
	class MakePdfUsingGeckofxHtmlToPdfCommandLine
	{
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape)
		{
			var customSizes = new Dictionary<string, string>();
			customSizes.Add("Halfletter", "--page-width 8.5 --page-height 5.5");
			string pageSizeArguments;
			if (!customSizes.TryGetValue(paperSizeName, out pageSizeArguments))
			{
				pageSizeArguments = "--page-size " + paperSizeName; ; //this works too " --page-width 14.8cm --page-height 21cm"
			}

			//REVIEW: does geckofxthmltopdf need this special treatment?


			//wkhtmltopdf chokes on stuff like chinese file names, even if we put the console code page to UTF 8 first (CHCP 65001)
			//so now, we just deal in temp files
			using (var tempInput = TempFile.WithExtension(".htm"))
			{
				File.Delete(tempInput.Path);
				var source = File.ReadAllText(inputHtmlPath);

				//hide all placeholders
				source = source.Replace("placeholder.png", "").Replace("placeHolder.png", "");

				File.WriteAllText(tempInput.Path, source);

				var tempOutput = TempFile.WithExtension(".pdf"); //we don't want to dispose of this
				File.Delete(tempOutput.Path);

				string exePath = @"c:\dev\geckofxhtmltopdf\output\debug\geckofxhtmltopdf.exe";// FindWkhtmlToPdf();

				var arguments = string.Format(
					pageSizeArguments +
					(landscape ? " -O Landscape " : "") +
					"  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm " +
					"\"{0}\" \"{1}\"",
					tempInput.Path, tempOutput.Path);

				ExecutionResult result = null;
				using (var dlg = new ProgressDialogBackground())
				{
					dlg.ShowAndDoWork((progress, args) =>
					{
						progress.WriteStatus("Making PDF...");
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
																   try
																   {
																	   ((BackgroundWorker)args.Argument).ReportProgress(100);
																   }
																   catch (Exception)
																   {
																	   //else swallow; we've gotten this error:
																	   //"This operation has already had OperationCompleted called on it and further calls are illegal"
#if DEBUG
																	   throw;
#endif
																   }
															   }
															   else
															   {
#if DEBUG
																   Debug.Fail("Wanna look into this? Why is the process still reporting back?");
#endif
															   }
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

				Debug.WriteLine(result.StandardError);
				Debug.WriteLine(result.StandardOutput);

				if (!File.Exists(tempOutput.Path))
					throw new ApplicationException("Bloom was not able to create the PDF.\r\n\r\nDetails: GeckofxHtmlToPdf (command line) did not produce the expected document.");

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
