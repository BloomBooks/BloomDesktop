using System.IO;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Bloom.Publish
{
	public class Converter
	{
		private double _outputWidth;
		private double _outputHeight;
		private XPdfForm _inputPdf;
		private bool _rightToLeft;
		private bool _landscapeMode=false;

		public void Convert(string inputPath, string outputPath, PaperTarget paperTarget, bool rightToLeft)
		{
			_rightToLeft = rightToLeft;

			PdfDocument outputDocument = new PdfDocument();

			// Show single pages
			// (Note: one page contains two pages from the source document.
			//  If the number of pages of the source document can not be
			//  divided by 4, the first pages of the output document will
			//  each contain only one page from the source document.)
			outputDocument.PageLayout = PdfPageLayout.SinglePage;

			XGraphics gfx;

			// Open the external document as XPdfForm object
			_inputPdf = OpenDocumentForPdfSharp(inputPath);
			// Determine width and height
			_outputWidth = paperTarget.GetOutputDimensions(_inputPdf.PixelWidth,_inputPdf.PixelHeight).X;
			_outputHeight = paperTarget.GetOutputDimensions(_inputPdf.PixelWidth, _inputPdf.PixelHeight).Y;
			_landscapeMode = _inputPdf.PixelWidth > _inputPdf.PixelHeight;

			int inputPages = _inputPdf.PageCount;
			int sheets = inputPages / 4;
			if (sheets * 4 < inputPages)
				sheets += 1;
			int allpages = 4 * sheets;
			int vacats = allpages - inputPages;

			for (int idx = 1; idx <= sheets; idx++)
			{
				// Front page of a sheet:
				using (gfx = GetGraphicsForNewPage(outputDocument))
				{
					//Left side of front
					if (vacats > 0) // Skip if left side has to remain blank
						vacats -= 1;
					else
					{
						DrawSuperiorSide( gfx,  allpages + 2*(1 - idx));
					}

					//Right side of the front
					DrawInferiorSide(gfx, 2 * idx - 1);
				}

				// Back page of a sheet
				using (gfx = GetGraphicsForNewPage(outputDocument))
				{
					if (2*idx <= _inputPdf.PageCount) //prevent asking for page 2 with a single page document (JH Oct 2010)
					{
						//Left side of back
						DrawSuperiorSide( gfx, 2*idx);
					}

					//Right side of the Back
					if (vacats > 0) // Skip if right side has to remain blank
						vacats -= 1;
					else
					{
						DrawInferiorSide(gfx, allpages + 1 - 2 * idx);
					}
				}
			}

			outputDocument.Save(outputPath);
		}

		/// With the portrait, left-to-right-language mode, this is the Right side.
		/// With the landscape, this is the bottom half.
		private void DrawInferiorSide(XGraphics gfx, int pageNumber /* NB: page number is one-based*/)
		{
			_inputPdf.PageNumber = pageNumber;
			XRect box;
			if (_landscapeMode)
			{
				box = new XRect(0, _outputHeight/2, _outputWidth, _outputHeight/2);
			}
			else
			{
				var leftEdge = _rightToLeft ? 0 : _outputWidth / 2;
				box = new XRect(leftEdge, 0, _outputWidth / 2, _outputHeight);
			}
			gfx.DrawImage(_inputPdf, box);
		}

		/// <summary>
		/// With the portrait, left-to-right-language mode, this is the Left side.
		/// With the landscape, this is the top half.
		/// </summary>
		private  void DrawSuperiorSide( XGraphics gfx, int pageNumber)
		{
			_inputPdf.PageNumber = pageNumber;
			XRect box;
			if (_landscapeMode)
			{
				box = new XRect(0, 0, _outputWidth, _outputHeight/2);
			}
			else
			{
				var leftEdge = _rightToLeft ? _outputWidth/2 : 0;
				box = new XRect(leftEdge, 0, _outputWidth/2, _outputHeight);
			}
			gfx.DrawImage(_inputPdf, box);
		}

		private  XGraphics GetGraphicsForNewPage(PdfDocument outputDocument)
		{
			XGraphics gfx;
			PdfPage page = outputDocument.AddPage();
			page.Orientation = PageOrientation.Landscape;
			page.Width =  _outputWidth;
			page.Height = _outputHeight;

			gfx = XGraphics.FromPdfPage(page);
			return gfx;
		}

		/// <summary>
		/// from http://forum.pdfsharp.net/viewtopic.php?p=2069
		/// Get a version of the document which pdfsharp can open, downgrading if necessary
		/// </summary>
		static private XPdfForm OpenDocumentForPdfSharp(string path)
		{
//            try
//            {
				var form = XPdfForm.FromFile(path);
				//this causes it to notice if can't actually read it
				//int dummy = form.PixelWidth;
				return form;
//            }
//            catch (PdfSharp.Pdf.IO.PdfReaderException)
//            {
				//workaround if pdfsharp doesnt dupport this pdf
//                return XPdfForm.FromFile(WritePdf1pt4Version(path));
//            }
		}


#if itextsharpAsPartOfBloom

		itextsharp is commercial or GPL... so we'll leave it out as long as possible.
		It would be needed to handle the newest pdfs, but so long as our pdf creation method doesn't
		output them, we don't need it.

		/// <summary>
		/// from http://forum.pdfsharp.net/viewtopic.php?p=2069
		/// uses itextsharp to convert any pdf to 1.4 compatible pdf
		/// </summary>
		static private string WritePdf1pt4Version(string inputPath)
		{
			var tempFileName = Path.GetTempFileName();
			File.Delete(tempFileName);
			string outputPath = tempFileName + ".pdf";

			iTextSharp.text.pdf.PdfReader reader = new iTextSharp.text.pdf.PdfReader(inputPath);

			// we retrieve the total number of pages
			int n = reader.NumberOfPages;
			// step 1: creation of a document-object
			iTextSharp.text.Document document = new iTextSharp.text.Document(reader.GetPageSizeWithRotation(1));
			// step 2: we create a writer that listens to the document
			iTextSharp.text.pdf.PdfWriter writer = iTextSharp.text.pdf.PdfWriter.GetInstance(document, new FileStream(outputPath, FileMode.Create));
			//write pdf that pdfsharp can understand
			writer.SetPdfVersion(iTextSharp.text.pdf.PdfWriter.PDF_VERSION_1_4);
			// step 3: we open the document
			document.Open();
			iTextSharp.text.pdf.PdfContentByte cb = writer.DirectContent;
			iTextSharp.text.pdf.PdfImportedPage page;

			int rotation;

			int i = 0;
			while (i < n)
			{
				i++;
				document.SetPageSize(reader.GetPageSizeWithRotation(i));
				document.NewPage();
				page = writer.GetImportedPage(reader, i);
				rotation = reader.GetPageRotation(i);
				if (rotation == 90 || rotation == 270)
				{
					cb.AddTemplate(page, 0, -1f, 1f, 0, 0, reader.GetPageSizeWithRotation(i).Height);
				}
				else
				{
					cb.AddTemplate(page, 1f, 0, 0, 1f, 0, 0);
				}
			}
			// step 5: we close the document
			document.Close();
			return outputPath;
		}
#endif
	}
}
