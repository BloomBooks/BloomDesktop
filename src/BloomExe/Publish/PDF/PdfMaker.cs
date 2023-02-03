﻿using Bloom.Workspace;
using L10NSharp;
using PdfDroplet.LayoutMethods;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SIL.IO;
using SIL.Progress;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;

namespace Bloom.Publish.PDF
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
		/// Flag whether to compress the PDF file.
		/// </summary>
		/// <remarks>
		/// Do we ever NOT want to do this?
		/// </remarks>
		public bool CompressPdf;

		///  <summary>
		///
		///  </summary>
		/// <param name="specs">All the information about what sort of PDF file to make where</param>
		/// <param name="worker">If not null, the Background worker which is running this task, and may be queried to determine whether a cancel is being attempted</param>
		/// <param name="doWorkEventArgs">The event passed to the worker when it was started. If a cancel is successful, it's Cancel property should be set true.</param>
		/// <param name="owner">A control which can be used to invoke parts of the work which must be done on the ui thread.</param>
		public void MakePdf(PdfMakingSpecs specs, BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs, Control owner)
		{
			// Try up to 4 times. This is a last-resort attempt to handle BL-361.
			// Most likely that was caused by a race condition in MakePdfUsingGeckofxHtmlToPdfComponent.MakePdf,
			// but as it was an intermittent problem and we're not sure that was the cause, this might help.
			for (int i = 0; i < 4; i++)
			{
				new MakePdfUsingExternalPdfMakerProgram().MakePdf(specs,
					owner, worker, doWorkEventArgs);

				if (doWorkEventArgs.Cancel || (doWorkEventArgs.Result != null && doWorkEventArgs.Result is Exception))
					return;
				if (RobustFile.Exists(specs.OutputPdfPath))
					break; // normally the first time
			}
			if (!RobustFile.Exists(specs.OutputPdfPath) && owner != null)
			{
				// Should never happen, but...
				owner.Invoke((Action) (() =>
				{
					// Review: should we localize this? Hopefully the user never sees it...don't want to increase burden on localizers...
					MessageBox.Show(
						"Bloom unexpectedly failed to create the PDF. If this happens repeatedy please report it to the developers. Probably it will work if you just try again.",
						"Pdf creation failed", MessageBoxButtons.OK);
				}));
				doWorkEventArgs.Result = MakingPdfFailedException.CreatePdfException();
				return;
			}

			try
			{
				// Shrink the PDF file, especially if it has large color images.  (BL-3721)
				// Also if the book is full bleed we need to remove some spurious pages.
				// Removing spurious pages must be done BEFORE we switch pages around to make a booklet!
				// Note: previously compression was the last step, after making a booklet. We moved it before for
				// the reason above. Seems like it would also have performance benefits, if anything, to shrink
				// the file before manipulating it further. Just noting it in case there are unexpected issues.
				var fixPdf = new ProcessPdfWithGhostscript(ProcessPdfWithGhostscript.OutputType.DesktopPrinting, worker);
				fixPdf.ProcessPdfFile(specs.OutputPdfPath, specs.OutputPdfPath, specs.BookIsFullBleed);
				if (specs.BookletPortion != PublishModel.BookletPortions.AllPagesNoBooklet || specs.PrintWithFullBleed)
				{
					//remake the pdf by reordering the pages (and sometimes rotating, shrinking, etc)
					MakeBooklet(specs);
				}

				// Check that we got a valid, readable PDF.
				// If we get a reliable fix to BL-932 we can take this out altogether.
				// It's probably redundant, since the compression process would probably fail with this
				// sort of corruption, and we are many generations beyond gecko29 where we observed it.
				// However, we don't have data to reliably reproduce the BL-932, and the check doesn't take
				// long, so leaving it in for now.
				CheckPdf(specs.OutputPdfPath);
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

				RobustFile.Move(specs.OutputPdfPath, specs.OutputPdfPath + "-BAD");
				doWorkEventArgs.Result = MakingPdfFailedException.CreatePdfException();
			}

		}

		public class MakingPdfFailedException : Exception
		{
			private MakingPdfFailedException(string message) : base(message)
			{
			}

			public static MakingPdfFailedException CreatePdfException()
			{
				return new MakingPdfFailedException(LocalizationManager.GetString("PublishTab.PdfMaker.BadPdfShort", "Bloom had a problem making a PDF of this book."));
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

		private void MakeBooklet(PdfMakingSpecs specs)
		{
			//TODO: we need to let the user chose the paper size, as they do in PdfDroplet.
			//For now, just assume a size double the original

			var incomingPaperSize = specs.PaperSizeName;

			PageSize pageSize;
			System.Drawing.Printing.PaperSize customPageSize = null;
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
				case "Cm13":
					pageSize = PageSize.A3;
					break;
				case "USComic":
					pageSize = PageSize.A3;	// Ledger would work as well.
					break;
				case "Size6x9":
					// 9"x12" can hold two 6"x9" pages.
					pageSize = PageSize.Undefined;
					customPageSize = new System.Drawing.Printing.PaperSize("9\"x12\"", 9*100, 12*100);
					break;
				default:
					throw new ApplicationException("PdfMaker.MakeBooklet() does not contain a map from " + incomingPaperSize + " to a PdfSharp paper size.");
			}

			using (var incoming = new TempFile())
			{
				RobustFile.Delete(incoming.Path);
				RobustFile.Move(specs.OutputPdfPath, incoming.Path);

				LayoutMethod method;
				switch (specs.BooketLayoutMethod)
				{
					case PublishModel.BookletLayoutMethod.NoBooklet:
						method = new NullLayoutMethod(specs.PrintWithFullBleed ? 3 : 0);
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
						else if (incomingPaperSize == "Cm13")
						{
							method = new Square6UpBookletLayouter();
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

				PaperTarget paperTarget;
				const string paperTargetName = "ZZ"; // we're not displaying this anywhere, so we don't need to know the name
				if (pageSize != PageSize.Undefined)
				{
					paperTarget = new PaperTarget(paperTargetName, pageSize);
				}
				else
				{
					if (customPageSize == null)
						throw new NullReferenceException("customPageSize must be set if pageSize is Undefined, but customPageSize was null.");

					paperTarget = new PaperTarget(paperTargetName, customPageSize);
				}

				var pdf = XPdfForm.FromFile(incoming.Path);//REVIEW: this whole giving them the pdf and the file too... I checked once and it wasn't wasting effort...the path was only used with a NullLayout option
				method.Layout(pdf, incoming.Path, specs.OutputPdfPath, paperTarget, specs.LayoutPagesForRightToLeft, ShowCropMarks);
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
