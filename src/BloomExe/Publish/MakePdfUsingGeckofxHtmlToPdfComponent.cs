using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Bloom.ToPalaso;
using Gecko;
using Palaso.CommandLineProcessing;
using Palaso.IO;
using GeckofxHtmlToPdf;

namespace Bloom.Publish
{
	/// <summary>
	/// This wrapper uses a component out of he GeckoFxHtmlToPdf, rather than running the exe via command line
	/// </summary>
	class MakePdfUsingGeckofxHtmlToPdfComponent
	{
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, GeckofxHtmlToPdfComponent geckofxHtmlToPdfComponent)
		{

			Debug.Fail("Read where I left this here in the code.");

			/* where I left this:
			 * I was facing 1 problem and one big todo:
			 * 1) problem: when the pdf generator tried to touch the geck prefs, it would get a COM crash. TOdo on this is to replicate it in the official geckofx sample app and report it.
			 * 2) TODO: the current system is overly complicated anyhow, and in particular with respect to running in a background, and this component approach precludes running in a background
			 * because we're sharing a geckofx (xpcom), and it can only run on the thread which it was created on (normally the ui thread).
			 */


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

				geckofxHtmlToPdfComponent.Start(conversionOrder);

				//TODO: The rest of this would have to be done on completion

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
