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
using L10NSharp;
using SIL.Code;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;
using PdfDroplet.LayoutMethods;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

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

		///  <summary>
		/// 
		///  </summary>
		/// <param name="inputHtmlPath"></param>
		/// <param name="outputPdfPath"></param>
		/// <param name="paperSizeName">A0,A1,A2,A3,A4,A5,A6,A7,A8,A9,B0,B1,B10,B2,B3,B4,B5,B6,B7,B8,B9,C5E,Comm10E,DLE,Executive,Folio,Ledger,Legal,Letter,Tabloid</param>
		/// <param name="landscape">true if landscape orientation, false if portrait orientation</param>
		/// <param name="saveMemoryMode">true if PDF file is to be produced using less memory (but more time)</param>
		/// <param name="layoutPagesForRightToLeft">true if RTL, false if LTR layout</param>
		/// <param name="booketLayoutMethod">NoBooklet,SideFold,CutAndStack,Calendar</param>
		/// <param name="bookletPortion">None,AllPagesNoBooklet,BookletCover,BookletPages,InnerContent</param>
		/// <param name="worker">If not null, the Background worker which is running this task, and may be queried to determine whether a cancel is being attempted</param>
		/// <param name="doWorkEventArgs">The event passed to the worker when it was started. If a cancel is successful, it's Cancel property should be set true.</param>
		/// <param name="owner">A control which can be used to invoke parts of the work which must be done on the ui thread.</param>
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, bool saveMemoryMode, bool layoutPagesForRightToLeft,
			PublishModel.BookletLayoutMethod booketLayoutMethod, PublishModel.BookletPortions bookletPortion, BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs, Control owner)
		{
			// Try up to 4 times. This is a last-resort attempt to handle BL-361.
			// Most likely that was caused by a race condition in MakePdfUsingGeckofxHtmlToPdfComponent.MakePdf,
			// but as it was an intermittent problem and we're not sure that was the cause, this might help.
			for (int i = 0; i < 4; i++)
			{
				new MakePdfUsingGeckofxHtmlToPdfProgram().MakePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape, saveMemoryMode,
					owner, worker, doWorkEventArgs);

				if (doWorkEventArgs.Cancel || (doWorkEventArgs.Result != null && doWorkEventArgs.Result is Exception))
					return;
				if (RobustFile.Exists(outputPdfPath))
					break; // normally the first time
			}
			if (!RobustFile.Exists(outputPdfPath) && owner != null)
			{
				// Should never happen, but...
				owner.Invoke((Action) (() =>
				{
					// Review: should we localize this? Hopefully the user never sees it...don't want to increase burden on localizers...
					MessageBox.Show(
						"Bloom unexpectedly failed to create the PDF. If this happens repeatedy please report it to the developers. Probably it will work if you just try again.",
						"Pdf creation failed", MessageBoxButtons.OK);
				}));
			}

			try
			{
				if (bookletPortion != PublishModel.BookletPortions.AllPagesNoBooklet)
				{
					//remake the pdf by reording the pages (and sometimes rotating, shrinking, etc)
					MakeBooklet(outputPdfPath, paperSizeName, booketLayoutMethod, layoutPagesForRightToLeft);
				}
				else
				{
					 // Just check that we got a valid, readable PDF. (MakeBooklet has to read the PDF itself,
					// so we don't need to do this check if we're calling that.)
					// If we get a reliable fix to BL-932 we can take this 'else' out altogether.
					CheckPdf(outputPdfPath);
				}
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
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(e,
					LocalizationManager.GetString("PublishTab.PdfMaker.BadPdf", "Bloom had a problem making a PDF of this book. You may need technical help or to contact the developers. But here are some things you can try:")
						+ Environment.NewLine + "- "
						+ LocalizationManager.GetString("PublishTab.PdfMaker.TryRestart", "Restart your computer and try this again right away")
						+ Environment.NewLine + "- "
						+
						LocalizationManager.GetString("PublishTab.PdfMaker.TrySmallerImages",
							"Replace large, high-resolution images in your document with lower-resolution ones")
						+ Environment.NewLine + "- "
						+ LocalizationManager.GetString("PublishTab.PdfMaker.TryMoreMemory", "Try doing this on a computer with more memory"));

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

		///  <summary>
		/// 
		///  </summary>
		/// <param name="pdfPath">this is the path where it already exists, and the path where we leave the transformed version</param>
		/// <param name="incomingPaperSize"></param>
		/// <param name="booketLayoutMethod"></param>
		/// <param name="layoutPagesForRightToLeft"></param>
		private void MakeBooklet(string pdfPath, string incomingPaperSize, PublishModel.BookletLayoutMethod booketLayoutMethod, bool layoutPagesForRightToLeft)
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
					pageSize = PageSize.Ledger;
					break;
				case "HalfLetter":
					pageSize = PageSize.Letter;
					break;
				case "QuarterLetter":
					pageSize = PageSize.Statement;	// ?? Wikipedia says HalfLetter is aka Statement
					break;
				case "Legal":
					pageSize = PageSize.Legal;//TODO... what's reasonable?
					break;
				case "HalfLegal":
					pageSize = PageSize.Legal;
					break;
				default:
					throw new ApplicationException("PdfMaker.MakeBooklet() does not contain a map from " + incomingPaperSize + " to a PdfSharp paper size.");
			}

			using (var incoming = new TempFile())
			{
				RobustFile.Delete(incoming.Path);
				RobustFile.Move(pdfPath, incoming.Path);

				LayoutMethod method;
				switch (booketLayoutMethod)
				{
					case PublishModel.BookletLayoutMethod.NoBooklet:
						method = new NullLayoutMethod();
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
				var paperTarget = new PaperTarget("ZZ"/*we're not displaying this anyhwere, so we don't need to know the name*/, pageSize);
				var pdf = XPdfForm.FromFile(incoming.Path);//REVIEW: this whole giving them the pdf and the file too... I checked once and it wasn't wasting effort...the path was only used with a NullLayout option
				method.Layout(pdf, incoming.Path, pdfPath, paperTarget, layoutPagesForRightToLeft, ShowCropMarks);
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
