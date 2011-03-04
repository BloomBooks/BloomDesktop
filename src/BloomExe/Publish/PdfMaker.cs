using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Palaso.Code;
using Palaso.IO;

namespace Bloom.Publish
{
	/// <summary>
	/// Given a path to html, creates a pdf according to theh bookletStyle parameter
	/// </summary>
	public class PdfMaker
	{
		public void MakePdf(string inputHtmlPath, string outputPdfPath, PublishModel.BookletStyleChoices bookletStyle)
		{
			Guard.Against(Path.GetExtension(inputHtmlPath) != ".htm",
						  "wkhtmtopdf will croak if the input file doesn't have an htm extension.");
			MakeSimplePdf(inputHtmlPath,  outputPdfPath);
			if (bookletStyle == PublishModel.BookletStyleChoices.Booklet)
			{
				MakeBooklet(outputPdfPath);
			}
		}

		private void MakeSimplePdf(string inputHtmlPath, string outputPdfPath)
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
			ProcessStartInfo info = new ProcessStartInfo(exePath,
														 string.Format(
															 "--print-media-type --page-width 14.5cm --page-height 21cm  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm --disable-smart-shrinking {0} {1}",
															 Path.GetFileName(inputHtmlPath), outputPdfPath));
			info.WorkingDirectory = Path.GetDirectoryName(inputHtmlPath);
			info.ErrorDialog = true;
			info.WindowStyle = ProcessWindowStyle.Hidden;

			//_browser.Visible = false;
			var proc = System.Diagnostics.Process.Start(info);
			proc.WaitForExit(20 * 1000);
			if (!proc.HasExited)
			{
				proc.Kill();
				throw new ApplicationException("Making the PDF took too long.");
			}
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
