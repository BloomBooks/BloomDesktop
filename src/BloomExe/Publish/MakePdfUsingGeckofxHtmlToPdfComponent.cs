using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Bloom.ToPalaso;
using Palaso.CommandLineProcessing;
using Palaso.IO;
using geckofxHtmlToPdf;

namespace Bloom.Publish
{
	/// <summary>
	/// This wrapper uses a component out of he GeckoFxHtmlToPdf, rather than running the exe via command line
	/// </summary>
	class MakePdfUsingGeckofxHtmlToPdfComponent
	{
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape)
		{

			//REVIEW: does geckofxthmltopdf need this special treatment?

			//wkhtmltopdf choked on stuff like chinese file names, even if we put the console code page to UTF 8 first (CHCP 65001)
			//so now, we just deal in temp files. Might not be needed by geckofxthmltopdf
			using (var tempInput = TempFile.WithExtension(".htm"))
			{
				File.Delete(tempInput.Path);
				var source = File.ReadAllText(inputHtmlPath);

				//hide all placeholders
				source = source.Replace("placeholder.png", "").Replace("placeHolder.png", "");

				File.WriteAllText(tempInput.Path, source);

				var tempOutput = TempFile.WithExtension(".pdf"); //we don't want to dispose of this
				File.Delete(tempOutput.Path);

				var conversionOrder = new ConversionOrder()
					{
						BottomMarginInMillimeters = 0,
						TopMarginInMillimeters = 0,
						LeftMarginInMillimeters = 0,
						RightMarginInMillimeters = 0,
						EnableGraphite = true,
						Landscape = landscape,
						InputPath = tempInput.Path,
						OutputPath = tempOutput.Path,
						PageSizeName = paperSizeName
					};


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
