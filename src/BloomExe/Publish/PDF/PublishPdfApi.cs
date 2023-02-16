using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Properties;
using DesktopAnalytics;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;
using Application = System.Windows.Forms.Application;

namespace Bloom.Publish.PDF
{
	public class PublishPdfApi
	{
		private readonly BloomWebSocketServer _webSocketServer;
		private PublishModel _publishModel;
		private BackgroundWorker _makePdfBackgroundWorker;

		private string PdfFilePath => _publishModel.PdfFilePath;

		// This is to support orientation/page size changing during publishing, but we're not doing
		// that yet, so just defer to the current book.
		public Layout PageLayout => _publishModel.PageLayout;

		public PublishModel.BookletPortions BookletPortion {
			get { return _publishModel.BookletPortion; }
			set
			{
				_publishModel.BookletPortion = value;
			}
		}

		public PublishPdfApi(BloomWebSocketServer webSocketServer, PublishModel model)
		{
			_webSocketServer = webSocketServer;
			_publishModel = model;
		}


		private const string kApiUrlPart = "publish/pdf/";

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "simple", HandleCreateSimplePdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cover", HandleCreateCoverPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cancel", HandleCancel, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "pages", HandleCreatePagesPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "printSettingsPath", HandlePrintSettingsPath, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "save", HandleSavePdf, true);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "allowBooklet", request => _publishModel.AllowPdfBooklet, null, false);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "allowFullBleed",
				request => CurrentBook?.FullBleed ?? false, null, false);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "printAnalytics", HandlePrintAnalytics, true);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "fullBleed",
				request => (CurrentBook?.FullBleed ?? false) && (CurrentBook?.UserPrefs.FullBleed ?? false),
				((writeRequest, value) =>
				{
					if (CurrentBook != null)
					{
						CurrentBook.UserPrefs.FullBleed = value;
					}
				}), false);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "cmyk",
				request => CurrentBook?.UserPrefs.CmykPdf ?? false,
				((writeRequest, value) =>
				{
					if (CurrentBook != null)
					{
						CurrentBook.UserPrefs.CmykPdf = value;
					}
				}), false);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "dontShowSamplePrint",
				request => Settings.Default.DontShowPrintNotification,
				((writeRequest, value) =>
				{
					Settings.Default.DontShowPrintNotification = value;
					Settings.Default.Save();
				}), false);
		}

		private void HandleCancel(ApiRequest request)
		{
			_makePdfBackgroundWorker.CancelAsync();
			request.PostSucceeded();
		}

		private void HandleSavePdf(ApiRequest request)
		{
			_publishModel.SavePdf();
			request.PostSucceeded();
		}

		/// <summary>
		/// Gets the url of the image we should show (if any) to help the user configure
		/// the printer to correctly print a booklet.
		/// </summary>
		private void HandlePrintSettingsPath(ApiRequest request)
		{
			var printSettingsPreviewFolder =
				FileLocationUtilities.GetDirectoryDistributedWithApplication("printer settings images");
			var printSettingsSamplePrefix = Path.Combine(printSettingsPreviewFolder,
				PageLayout.SizeAndOrientation + "-" + (isBooklet() ? "Booklet-" : ""));
			string printSettingsSampleName = null;
			if (printSettingsSampleName == null || !RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = printSettingsSamplePrefix + LocalizationManager.UILanguageId + ".png";
			if (!RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = printSettingsSamplePrefix + "en" + ".png";
			if (Settings.Default.DontShowPrintNotification || !RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = "";
			else
				printSettingsSampleName = printSettingsSampleName.ToLocalhost();
			request.ReplyWithText(printSettingsSampleName);
		}

		private void HandlePrintAnalytics(ApiRequest request)
		{
			Analytics.Track("Print PDF", new Dictionary<string, string>()
			{
				{ "BookId", CurrentBook.ID },
				{ "Country", CurrentBook.CollectionSettings.Country }
			});
			CurrentBook.ReportSimplisticFontAnalytics(FontAnalytics.FontEventType.PublishPdf, "Print PDF");

			request.PostSucceeded();
		}

		private bool isBooklet()
		{
			return BookletPortion == PublishModel.BookletPortions.BookletCover
			       || BookletPortion == PublishModel.BookletPortions.BookletPages;
		}

		private void HandleCreatePagesPdf(ApiRequest request)
		{
			BookletPortion = PublishModel.BookletPortions.BookletPages;
			MakePdf();
			request.PostSucceeded();
		}

		private void HandleCreateCoverPdf(ApiRequest request)
		{
			BookletPortion = PublishModel.BookletPortions.BookletCover;
			MakePdf();
			request.PostSucceeded();
		}

		private void HandleCreateSimplePdf(ApiRequest request)
		{
			BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet;
			MakePdf();
			request.PostSucceeded();
		}

		private Book.Book CurrentBook => _publishModel.CurrentBook;

		// Make the actual PDF file with current settings. Slightly adapted from the PublishView code
		// that calls PublishModel.LoadBook.
		public void MakePdf()
		{
			var shell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			_makePdfBackgroundWorker = new BackgroundWorker();
			_makePdfBackgroundWorker.WorkerReportsProgress = true;
			_makePdfBackgroundWorker.WorkerSupportsCancellation = true;
			_makePdfBackgroundWorker.RunWorkerCompleted += _makePdfBackgroundWorker_RunWorkerCompleted;
			_makePdfBackgroundWorker.DoWork += ((sender, doWorkEventArgs) =>
			{
				_publishModel.LoadBook(sender as BackgroundWorker, doWorkEventArgs, shell);
			});
			_makePdfBackgroundWorker.RunWorkerAsync();
		}

		void _makePdfBackgroundWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
		{
			_publishModel.PdfGenerationSucceeded = false;
			if (e.Cancelled)
			{
				dynamic messageBundleCancel = new DynamicJson();
				messageBundleCancel.path = "";
				_webSocketServer.SendBundle("publish", "pdfReady", messageBundleCancel);
			} else
			{
				if (e.Result is Exception)
				{
					var error = e.Result as Exception;
					if (error is ApplicationException)
					{
						//For common exceptions, we catch them earlier (in the worker thread) and give a more helpful message
						//note, we don't want to include the original, as it leads to people sending in reports we don't
						//actually want to see. E.g., we don't want a bug report just because they didn't have Acrobat
						//installed, or they had the PDF open in Word, or something like that.
						ErrorReport.NotifyUserOfProblem(error.Message);
					}
					else if (error is PdfMaker.MakingPdfFailedException)
					{
						// Ignore this error here.  It will be reported elsewhere if desired.
					}
					else if (error is FileNotFoundException && ((FileNotFoundException)error).FileName == "BloomPdfMaker.exe")
					{
						ErrorReport.NotifyUserOfProblem(error, error.Message);
					}
					else if (error is OutOfMemoryException)
					{
						// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5467.
						var fmt = LocalizationManager.GetString("PublishTab.PdfMaker.OutOfMemory",
							"Bloom ran out of memory while making the PDF. See {0}this article{1} for some suggestions to try.",
							"{0} and {1} are HTML link markup.  You can think of them as fancy quotation marks.");
						var msg = String.Format(fmt, "<a href='https://community.software.sil.org/t/solving-memory-problems-while-printing/500'>", "</a>");
						using (var f = new MiscUI.HtmlLinkDialog(msg))
						{
							f.ShowDialog();
						}
					}
					else // for others, just give a generic message and include the original exception in the message
					{
						ErrorReport.NotifyUserOfProblem(error, "Sorry, Bloom had a problem creating the PDF.");
					}
					dynamic messageBundleCancel = new DynamicJson();
					messageBundleCancel.path = "";
					_webSocketServer.SendBundle("publish", "pdfReady", messageBundleCancel);
					return;
				}
				_publishModel.PdfGenerationSucceeded = true;
				dynamic messageBundle = new DynamicJson();
				messageBundle.path = PdfFilePath.ToLocalhost();
				_webSocketServer.SendBundle("publish", "pdfReady", messageBundle);
			}
		}
	}
}
