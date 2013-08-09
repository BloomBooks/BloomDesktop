using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
		/// turns on crop marks and TrimBox
		/// </summary>
		public bool ShowCropMarks;

		public enum PdfEngineChoices
		{
			geckofxHtmlToPdfComponent,
			geckofxHtlmToPdfCommandLine,
			wkHtmlToPdfCommandLine
		};// could add PrinceXml someday if there's call for it

		public PdfEngineChoices EngineChoice { get; set; }

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
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, PublishModel.BookletLayoutMethod booketLayoutMethod, PublishModel.BookletPortions bookletPortion,  DoWorkEventArgs doWorkEventArgs)
		{
			Guard.Against(Path.GetExtension(inputHtmlPath) != ".htm",
						  "wkhtmtopdf will croak if the input file doesn't have an htm extension.");

			switch (EngineChoice)
			{
//				case PdfEngineChoices.geckofxHtmlToPdfComponent:
//					new MakePdfUsingGeckfxoHtmlToPdfComponent().MakePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape);
//					break;
				case PdfEngineChoices.geckofxHtlmToPdfCommandLine:
					new MakePdfUsingGeckofxHtmlToPdfCommandLine().MakePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape);
					break;
				case PdfEngineChoices.wkHtmlToPdfCommandLine:
					new MakePdfUsingWkHtmlToPdf().MakePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			//   if (bookletPortion != PublishModel.BookletPortions.None)
			{
				//remake the pdf by reording the pages (and sometimes rotating, shrinking, etc)
				RunThroughPDFDroplet(outputPdfPath, paperSizeName, booketLayoutMethod);
			}
		}


		/// <summary>
		///
		/// </summary>
		/// <param name="pdfPath">this is the path where it already exists, and the path where we leave the transformed version</param>
		/// <param name="incomingPaperSize"></param>
		/// <param name="booketLayoutMethod"></param>
		private void RunThroughPDFDroplet(string pdfPath, string incomingPaperSize, PublishModel.BookletLayoutMethod booketLayoutMethod)
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
				switch(booketLayoutMethod)
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
