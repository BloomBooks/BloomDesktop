using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Palaso.Code;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom.Publish
{
	/// <summary>
	/// Given a path to html, creates a pdf according to theh bookletStyle parameter
	/// </summary>
	public class PdfMaker
	{
		/// <summary>
		///
		/// </summary>
		/// <param name="inputHtmlPath"></param>
		/// <param name="outputPdfPath"></param>
		/// <param name="paperSizeName">A0,A1,A2,A3,A4,A5,A6,A7,A8,A9,B0,B1,B10,B2,B3,B4,B5,B6,B7,B8,B9,C5E,Comm10E,DLE,Executive,Folio,Ledger,Legal,Letter,Tabloid</param>
		/// <param name="getIsLandscape"></param>
		/// <param name="bookletStyle"></param>
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName, bool landscape, PublishModel.BookletStyleChoices bookletStyle)
		{
			Guard.Against(Path.GetExtension(inputHtmlPath) != ".htm",
						  "wkhtmtopdf will croak if the input file doesn't have an htm extension.");
			MakeSimplePdf(inputHtmlPath, outputPdfPath, paperSizeName, landscape);
			if (bookletStyle != PublishModel.BookletStyleChoices.None)
			{
				MakeBooklet(outputPdfPath);
			}
		}

		private void MakeSimplePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName,bool landscape)
		{
			var customSizes = new Dictionary<string, string>();
			customSizes.Add("Halfletter", "--page-width 8.5 --page-height 5.5");
			string pageSizeArguments;
			if(!customSizes.TryGetValue(paperSizeName, out pageSizeArguments))
			{
				pageSizeArguments = "--page-size " + paperSizeName; ; //this works too " --page-width 14.8cm --page-height 21cm"
			}

			string exePath = FindWkhtmlToPdf();
			ProcessStartInfo info = new ProcessStartInfo(exePath,
														 string.Format(
															"--print-media-type "+
															pageSizeArguments +
															(landscape? " -O Landscape ":"")+
															"  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm "+
															"--disable-smart-shrinking --zoom 1.091 \"{0}\" \"{1}\"",
															 Path.GetFileName(inputHtmlPath), outputPdfPath));
			info.WorkingDirectory = Path.GetDirectoryName(inputHtmlPath);
			info.ErrorDialog = true;
			info.WindowStyle = ProcessWindowStyle.Hidden;

			var proc = System.Diagnostics.Process.Start(info);
			proc.WaitForExit(20 * 1000);
			if (!proc.HasExited)
			{
				proc.Kill();
				throw new ApplicationException("Making the PDF took too long.");
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

		private void MakeBooklet(string inAndOutPath)
		{
			var tempPath = Path.GetTempFileName();
			File.Delete(tempPath);
			File.Move(inAndOutPath, tempPath);
			using (var incoming = TempFile.TrackExisting(tempPath))
			{
				var converter = new Converter();
				bool rightToLeft = false;
				var paperTarget = new DoublePaperTarget();
				converter.Convert(incoming.Path, inAndOutPath, paperTarget, rightToLeft);
			}
		}


	}
}
