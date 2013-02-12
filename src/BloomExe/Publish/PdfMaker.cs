using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Bloom.Edit;
using Bloom.ToPalaso;
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
			if (doWorkEventArgs.Cancel || (doWorkEventArgs.Result!=null && doWorkEventArgs.Result is Exception))
				return;
			if (bookletPortion != PublishModel.BookletPortions.None)
			{
				//remake the pdf by reording the pages (and sometimes rotating, shrinking, etc)
				MakeBooklet(outputPdfPath, paperSizeName, booketLayoutMethod);
			}
		}

		private void MakeSimplePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, DoWorkEventArgs doWorkEventArgs)
		{
			var customSizes = new Dictionary<string, string>();
			customSizes.Add("Halfletter", "--page-width 8.5 --page-height 5.5");
			string pageSizeArguments;
			if(!customSizes.TryGetValue(paperSizeName, out pageSizeArguments))
			{
				pageSizeArguments = "--page-size " + paperSizeName; ; //this works too " --page-width 14.8cm --page-height 21cm"
			}

			//wkhtmltopdf chokes on stuff like chinese file names, even if we put the console code page to UTF 8 first (CHCP 65001)
			//so now, we just deal in temp files
			using(var tempInput = TempFile.WithExtension(".htm"))
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

				//About the --zoom parameter. It's a hack to get the pages chopped properly.
				//Notes: Remember, a page border *will make the page that much larger!*
				//		One way to see what's happening without a page border is to make the marginBox visible,
				//		then scroll through and you can see it moving up (if the page (zoom factor) is too small) or down (if page (zoom factor) is too large)
				//		Until Aug 2012, I had 1.091. But with large a4 landscape docs (e.g. calendar), I saw
				//		that the page was too big, leading to an extra page at the end.
				//		Experimentation showed that 1.041 kept the marge box steady.
				var arguments = string.Format(
					"--no-background "+ //without this, we get a thin line on the right side, which turned into a line in the middle when made into a booklet. You could only see it on paper or by zooming in.
					" --print-media-type " +
					pageSizeArguments +
					(landscape ? " -O Landscape " : "") +
#if DEBUG
					" --debug-javascript "+
#endif
					"  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm " +
					"--disable-smart-shrinking --zoom 1.041 \"{0}\" \"{1}\"",
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
					dlg.ShowAndDoWork((progress,args) =>
										{
											progress.WriteStatus("Making PDF...");
											//this is a trick... since we are so far failing to get
											//the progress otu of wkhtml2pdf until it is done,
											//we at least have this indicator which on win 7 does
											//grow from 0 to the set percentage with some animation
											progress.ProgressIndicator.PercentCompleted = 70;
											result = CommandLineRunner.Run(exePath, arguments, null, Path.GetDirectoryName(tempInput.Path),
																		   5*60, progress
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
				switch(booketLayoutMethod)
				{
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
				method.Layout(pdf, incoming.Path, pdfPath, paperTarget, /*TODO: rightToLeft*/ false);
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
