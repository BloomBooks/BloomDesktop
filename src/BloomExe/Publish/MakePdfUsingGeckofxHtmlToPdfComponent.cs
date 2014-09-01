using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
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
		public void MakePdf(string inputHtmlPath, string outputPdfPath, string paperSizeName,
			bool landscape, Control owner, BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs)
		{
			ConversionProgress progress = null;
			try
			{
				var tempOutput = TempFile.WithExtension(".pdf"); //we don't want to dispose of this (since we will move it)
				File.Delete(tempOutput.Path);

				var conversionOrder = new ConversionOrder()
				{
					BottomMarginInMillimeters = 0,
					TopMarginInMillimeters = 0,
					LeftMarginInMillimeters = 0,
					RightMarginInMillimeters = 0,
					EnableGraphite = true,
					Landscape = landscape,
					InputHtmlPath = inputHtmlPath,
					OutputPdfPath = tempOutput.Path,
					PageSizeName = paperSizeName
				};
				using (var waitHandle = new AutoResetEvent(false))
				{
					var mainThreadTask = (Action)(() => {
						progress = new ConversionProgress(conversionOrder);
						progress.Finished += (sender, args) => {
							waitHandle.Set();
							if (!File.Exists(tempOutput.Path))
							{
								throw new ApplicationException(
									string.Format("Bloom was not able to create the PDF.{0}{0}" +
									"Details: GeckofxHtmlToPdf (command line) did not produce the expected document.",
										Environment.NewLine));
							}
							try
							{
								File.Move(tempOutput.Path, outputPdfPath);
							}
							catch (IOException e)
							{
								//I can't figure out how it happened (since GetPdfPath makes sure the file name is unique),
								//but we had a report (BL-211) of that move failing.
								throw new ApplicationException(
									string.Format(
										"Bloom tried to save the file to {0}, but Windows said that it was locked. Please try again.{2}{2}Details: {1}",
										outputPdfPath, e.Message, Environment.NewLine));
							}
						};
						progress.Show(owner);
					});
					if (owner == null) // typically tests; should be the main UI thread.
						mainThreadTask();
					else
						owner.Invoke(mainThreadTask);

					while (true)
					{
						// The background thread can't actually do any work...all happens in the gecko
						// component on the UI thread...but it must wait until we're done.
						if (waitHandle.WaitOne(100))
							break; // background thread signaled that it is finished
						if (progress != null && worker != null && worker.CancellationPending)
						{
							owner.Invoke((Action)(progress.Cancel));
							doWorkEventArgs.Cancel = true;
							break;
						}
					}
				}
			}
			finally
			{
				if (progress != null)
					progress.Dispose();
			}
		}
	}
}
