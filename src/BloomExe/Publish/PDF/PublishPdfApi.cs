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
		private BookSelection _bookSelection;
		private readonly CurrentEditableCollectionSelection _currentBookCollectionSelection;
		private readonly BookServer _bookServer;
		private PdfMaker _pdfMaker;
		private readonly BloomWebSocketServer _webSocketServer;

		private string PdfFilePath;

		// This is to support orientation/page size changing during publishing, but we're not doing
		// that yet, so just defer to the current book.
		public Layout PageLayout => CurrentBook?.GetLayout() ?? Layout.A5Portrait;

		public PublishModel.BookletPortions BookletPortion { get; set; }

		public PublishPdfApi(BookSelection selection, CurrentEditableCollectionSelection currentBookCollectionSelection,
			BookServer bookServer, PdfMaker pdfMaker, BloomWebSocketServer webSocketServer)
		{
			_bookSelection = selection;
			_currentBookCollectionSelection = currentBookCollectionSelection;
			_bookServer = bookServer;
			_pdfMaker = pdfMaker;
			_webSocketServer = webSocketServer;
		}


		private const string kApiUrlPart = "publish/pdf/";

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "simple", HandleCreateSimplePdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cover", HandleCreateCoverPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "pages", HandleCreatePagesPdf, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "printSettingsPath", HandlePrintSettingsPath, true);
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "save", HandleSavePdf, true);
			apiHandler.RegisterBooleanEndpointHandler(kApiUrlPart + "allowBooklet", request => AllowPdfBooklet, null, false);
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

		private void HandleSavePdf(ApiRequest request)
		{
			SavePdf();
			request.PostSucceeded();
		}

		private string _lastDirectory;
		// Adapted from PublishModel.Save()
		public void SavePdf()
		{
			try
			{
				// Give a slight preference to USB keys, though if they used a different directory last time, we favor that.

				if (string.IsNullOrEmpty(_lastDirectory) || !Directory.Exists(_lastDirectory))
				{
					try
					{
						var drives = SIL.UsbDrive.UsbDriveInfo.GetDrives();
						if (drives != null && drives.Count > 0)
						{
							_lastDirectory = drives[0].RootDirectory.FullName;
						}
					}
					catch (Exception err)
					{
						// If an error occurs while trying to get the USB drive info,
						// it's not a big deal and doesn't need to terminate the save operation.
						// Let's just log it and fall through to the rest of the Save() function
						SIL.Reporting.Logger.WriteError("Bloom encountered an error while getting list of USB drives.", err);
					}
				}

				var portion = "";
				switch (BookletPortion)
				{
					case PublishModel.BookletPortions.None:
						Debug.Fail("Save should not be enabled");
						return;
					case PublishModel.BookletPortions.AllPagesNoBooklet:
						portion = "Pages";
						break;
					case PublishModel.BookletPortions.BookletCover:
						portion = "Cover";
						break;
					case PublishModel.BookletPortions.BookletPages:
						portion = "Inside";
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
				string forPrintShop =
					CurrentBook.UserPrefs.CmykPdf || CurrentBook.UserPrefs.FullBleed
						? "-printshop"
						: "";
				string suggestedName = string.Format($"{Path.GetFileName(CurrentBook.FolderPath)}-{CurrentBook.GetFilesafeLanguage1Name("en")}-{portion}{forPrintShop}.pdf");
				var pdfFileLabel = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.PdfFile",
					"PDF File",
					@"displayed as file type for Save File dialog.");

				pdfFileLabel = pdfFileLabel.Replace("|", "");
				var pdfFilter = String.Format("{0}|*.pdf", pdfFileLabel);

				var startingFolder = (!string.IsNullOrEmpty(_lastDirectory) && Directory.Exists(_lastDirectory)) ?
							_lastDirectory :
							Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				var initialPath = Path.Combine(startingFolder, suggestedName);

				var destFileName = Utils.MiscUtils.GetOutputFilePathOutsideCollectionFolder(initialPath, pdfFilter);
				if (String.IsNullOrEmpty(destFileName))
					return;

				_lastDirectory = Path.GetDirectoryName(destFileName);
				if (CurrentBook.UserPrefs.CmykPdf)
				{
					// PDF for Printshop (CMYK US Web Coated V2)
					PublishModel.ProcessPdfFurtherAndSave(PdfFilePath, ProcessPdfWithGhostscript.OutputType.Printshop, destFileName);
				}
				else
				{
					// we want the simple PDF we already made.
					RobustFile.Copy(PdfFilePath, destFileName, true);
				}
				Analytics.Track("Save PDF", new Dictionary<string, string>()
								{
									{"Portion",  Enum.GetName(typeof(PublishModel.BookletPortions), BookletPortion)},
									{"Layout", PageLayout.ToString()},
									{"BookId", CurrentBook.ID },
									{"Country", CurrentBook.CollectionSettings.Country}
								});
				this.CurrentBook.ReportSimplisticFontAnalytics(FontAnalytics.FontEventType.PublishPdf, "Save PDF");
			}
			catch (Exception err)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(err, "Bloom was not able to save the PDF.  {0}", err.Message);
			}
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

		private Book.Book CurrentBook => _bookSelection.CurrentSelection;

		/// <summary>
		/// Is it reasonable to make a booklet out of the current book with the current settings?
		/// Roughly duplicates a method of PublishModel, though that includes a concept of
		/// AllowPdf which returns CanPublish which is derived from DeterminePublishability.
		/// (But that has to do with whether the book can be published at all, not whether
		/// it can be published as a booklet.)
		/// </summary>
		public bool AllowPdfBooklet
		{
			get
			{
				// Large page sizes can't make booklets.  See http://issues.bloomlibrary.org/youtrack/issue/BL-4155.
				var size = PageLayout.SizeAndOrientation.PageSizeName;
				return CurrentBook.BookInfo.BookletMakingIsAppropriate &&
				       (size != "A4" && size != "A3" && size != "B5" && size != "Letter" && size != "Device16x9");
			}
		}

		// Adapted from PublishModel.DeterminePublishability. Not sure if we will need it here.
		//private bool BookIsPublishable
		//{
		//	get
		//	{
		//		// At this point (5.1), this should only be false iff:
		//		// - User is not in Enterprise mode AND
		//		// - Book contains overlay elements AND
		//		// - Book is not a translated shell

		//		var overlayElementNodes =
		//			CurrentBook?.RawDom.SelectNodes("//div[contains(@class, 'bloom-textOverPicture')]");
		//		var bookContainsOverlayElements = (overlayElementNodes?.Count ?? 0) > 0;

		//		var bookIsTranslatedFromShell = CurrentBook?.BookData?.BookIsDerivative() ?? false;
		//		return (CurrentBook?.CollectionSettings?.HaveEnterpriseFeatures ?? false) ||
		//		       !bookContainsOverlayElements || bookIsTranslatedFromShell;
		//	}
		//}

		// Make the actual PDF file with current settings. Slightly adapted from PublishModel.LoadBook.
		public void MakePdf()
		{
			var shell = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is Shell);
			var worker = new BackgroundWorker();
			worker.WorkerReportsProgress = true;
			worker.WorkerSupportsCancellation = true;
			worker.DoWork += new System.ComponentModel.DoWorkEventHandler((sender, doWorkEventArgs) =>
			{
				doWorkEventArgs.Result = BookletPortion; // cf PublishView._makePdfBackgroundWorker_DoWork
				try
				{
					using (var tempHtml = MakeFinalHtmlForPdfMaker())
					{
						//if (doWorkEventArgs.Cancel)
						//	return;
						PublishModel.BookletLayoutMethod layoutMethod = GetBookletLayoutMethod();

						// Check memory for the benefit of developers.  The user won't see anything.
						Bloom.Utils.MemoryManagement.CheckMemory(true, "about to create PDF file", false);
						_pdfMaker.MakePdf(new PdfMakingSpecs()
							{
								InputHtmlPath = tempHtml.Key,
								OutputPdfPath = PdfFilePath,
								PaperSizeName = PageLayout.SizeAndOrientation.PageSizeName,
								Landscape = PageLayout.SizeAndOrientation.IsLandScape,
								SaveMemoryMode = CurrentBook.UserPrefs.ReducePdfMemoryUse,
								LayoutPagesForRightToLeft = LayoutPagesForRightToLeft,
								BooketLayoutMethod = layoutMethod,
								BookletPortion = BookletPortion,
								BookIsFullBleed = CurrentBook.FullBleed,
								PrintWithFullBleed = GetPrintingWithFullBleed(),
								Cmyk = CurrentBook.UserPrefs.CmykPdf
							},
							worker, doWorkEventArgs, shell);
						dynamic messageBundle = new DynamicJson();
						messageBundle.path = PdfFilePath.ToLocalhost();
						// Todo: what should clean up this file when??
						_webSocketServer.SendBundle("publish", "pdfReady", messageBundle);
						// Warn the user if we're starting to use too much memory.
						Bloom.Utils.MemoryManagement.CheckMemory(false, "finished creating PDF file", true);
					}
				}
				catch (Exception e)
				{
					//we can't safely do any ui-related work from this thread, like putting up a dialog
					doWorkEventArgs.Result = e;
					//                SIL.Reporting.ErrorReport.NotifyUserOfProblem(e, "There was a problem creating a PDF from this book.");
					//                SetDisplayMode(DisplayModes.WaitForUserToChooseSomething);
					//                return;
				}
			});
			worker.RunWorkerAsync();
		}

		// Slightly adapted from the same method in PublishModel.
		private SimulatedPageFile MakeFinalHtmlForPdfMaker()
		{
			PdfFilePath = GetPdfPath(Path.GetFileName(CurrentBook.FolderPath));

			//var orientationChanging = CurrentBook.GetLayout().SizeAndOrientation.IsLandScape !=
			//                          PageLayout.SizeAndOrientation.IsLandScape;
			var orientationChanging = true;
			var dom = CurrentBook.GetDomForPrinting(BookletPortion, _currentBookCollectionSelection.CurrentSelection,
				_bookServer, orientationChanging, PageLayout);

			AddStylesheetClasses(dom.RawDom);
			HtmlDom.AddClassToBody(dom.RawDom, "pdfPublishMode");

			PageLayout.UpdatePageSplitMode(dom.RawDom);
			if (CurrentBook.FullBleed && !GetPrintingWithFullBleed())
			{
				ClipBookToRemoveFullBleed(dom);
			}

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom.RawDom);
			dom.UseOriginalImages = true; // don't want low-res images or transparency in PDF.
			return BloomServer.MakeSimulatedPageFileInBookFolder(dom, source: BloomServer.SimulatedPageFileSource.Pub);
		}

		private string _lastPath = null;

		// Copied from PublishModel
		private string GetPdfPath(string fname)
		{
			string path = null;

			// Sanitize fileName first
			string fileName = BookStorage.SanitizeNameForFileSystem(fname);

			for (int i = 0; i < 100; i++)
			{
				path = Path.Combine(Path.GetTempPath(), string.Format("{0}-{1}.pdf", fileName, i));
				if (path == _lastPath)
				{
					// don't use the same path twice in a row; react-pdf won't realize it's a new file
					// and won't update, and other render effects don't happen because it hasn't changed.
					// But it's pretty surely one of ours, so try to clean it up.
					// (It doesn't matter if we use the same name in two different runs of Bloom. This is mainly
					// about switching between cover and inside pages or simple.)
					if (File.Exists(path))
					{
						try
						{
							RobustFile.Delete(path);
						} catch (Exception)
						{}
					}
					continue;
				}

				if (!RobustFile.Exists(path))
					break;

				try
				{
					RobustFile.Delete(path);
					break;
				}
				catch (Exception)
				{
					//couldn't delete it? then increment the suffix and try again
				}
			}
			_lastPath = path;
			return path;
		}


		private void AddStylesheetClasses(XmlDocument dom)
		{
			if (this.GetPrintingWithFullBleed())
			{
				HtmlDom.AddClassToBody(dom, "publishingWithFullBleed");
			}
			else
			{
				HtmlDom.AddClassToBody(dom, "publishingWithoutFullBleed");
			}
			HtmlDom.AddPublishClassToBody(dom);


			if (LayoutPagesForRightToLeft)
				HtmlDom.AddRightToLeftClassToBody(dom);
			HtmlDom.AddHidePlaceHoldersClassToBody(dom);
			if (CurrentBook.GetDefaultBookletLayoutMethod() == PublishModel.BookletLayoutMethod.Calendar)
			{
				HtmlDom.AddCalendarFoldClassToBody(dom);
			}
		}

		private bool LayoutPagesForRightToLeft
		{
			get { return CurrentBook.BookData.Language1.IsRightToLeft; }
		}

		private bool GetPrintingWithFullBleed()
		{
			return CurrentBook.FullBleed && GetBookletLayoutMethod() == PublishModel.BookletLayoutMethod.NoBooklet && CurrentBook.UserPrefs.FullBleed;
		}

		private PublishModel.BookletLayoutMethod GetBookletLayoutMethod()
		{
			PublishModel.BookletLayoutMethod layoutMethod;
			if (this.BookletPortion == PublishModel.BookletPortions.AllPagesNoBooklet)
				layoutMethod = PublishModel.BookletLayoutMethod.NoBooklet;
			else
				layoutMethod = CurrentBook.GetBookletLayoutMethod(PageLayout);
			return layoutMethod;
		}

		private void ClipBookToRemoveFullBleed(HtmlDom dom)
		{
			// example: A5 book is full bleed. What the user saw and configured in Edit mode is RA5 paper, 3mm larger on each side.
			// But we're not printing for full bleed. We will create an A5 page with no inset trim box.
			// We want it to hold the trim box part of the RA5 page.
			// to do this, we simply need to move the bloom-page element up and left by 3mm. Clipping to the page will do the rest.
			// It would be more elegant to do this by introducing a CSS rule involving .bloom-page, but to introduce a new stylesheet
			// we have to make it findable in the book folder, which is messy. Or, we could add a stylesheet element to the DOM;
			// but that's messy, too, we need stuff like /*<![CDATA[*/ to make the content survive the trip from XML to HTML.
			// So it's easiest just to stick it in the style attribute of each page.
			foreach (var page in dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]").Cast<XmlElement>())
			{
				page.SetAttribute("style", "margin-left: -3mm; margin-top: -3mm;");
			}
		}
	}
}
