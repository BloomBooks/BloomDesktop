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
		/// turns on crop marks and TrimBox
		/// </summary>
		public bool ShowCropMarks;

		/// <summary>
		///
		/// </summary>
		/// <param name="inputHtmlPath"></param>
		/// <param name="outputPdfPath"></param>
		/// <param name="paperSizeName">A0,A1,A2,A3,A4,A5,A6,A7,A8,A9,B0,B1,B10,B2,B3,B4,B5,B6,B7,B8,B9,C5E,Comm10E,DLE,Executive,Folio,Ledger,Legal,Letter,Tabloid</param>
		/// <param name="landscape"> </param>
		/// <param name="booketLayoutMethod"> </param>
		/// <param name="bookletPortion"></param>
		/// <param name="worker">If not null, the Background worker which is running this task, and may be queried to determine whether a cancel is being attempted</param>
		/// <param name="doWorkEventArgs">The event passed to the worker when it was started. If a cancel is successful, it's Cancel property should be set true.</param>
		/// <param name="owner">A control which can be used to invoke parts of the work which must be done on the ui thread.</param>
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, PublishModel.BookletLayoutMethod booketLayoutMethod, PublishModel.BookletPortions bookletPortion, BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs, Control owner)
		{
			Guard.Against(Path.GetExtension(inputHtmlPath) != ".htm",
						  "wkhtmtopdf will croak if the input file doesn't have an htm extension.");

			new MakePdfUsingGeckofxHtmlToPdfComponent().MakePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape, owner, worker, doWorkEventArgs);

			if (doWorkEventArgs.Cancel || (doWorkEventArgs.Result != null && doWorkEventArgs.Result is Exception))
				return;
			if (bookletPortion != PublishModel.BookletPortions.AllPagesNoBooklet)
			{
				//remake the pdf by reording the pages (and sometimes rotating, shrinking, etc)
				MakeBooklet(outputPdfPath, paperSizeName, booketLayoutMethod);
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
				case "B5":
					pageSize = PageSize.B4;
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
				switch (booketLayoutMethod)
				{
					case PublishModel.BookletLayoutMethod.NoBooklet:
						method = new NullLayoutMethod();
						break;
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
				method.Layout(pdf, incoming.Path, pdfPath, paperTarget, /*TODO: rightToLeft*/ false, ShowCropMarks);
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
