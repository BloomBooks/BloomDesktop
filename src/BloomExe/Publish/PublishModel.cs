using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Api;
using Bloom.Publish.PDF;
using BloomTemp;
using DesktopAnalytics;
using SIL.IO;
using SIL.Progress;
using SIL.Xml;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;

namespace Bloom.Publish
{
	/// <summary>
	/// Contains the logic behind the PublishView control, which involves creating a pdf from the html book and letting you print it,
	/// making epubs, and various other publication paths.
	/// </summary>
	public class PublishModel : IDisposable
	{
		public BookSelection BookSelection { get; private set; }

		public BookServer BookServer { get { return _bookServer; } }

		public string PdfFilePath { get; private set; }

		public enum DisplayModes
		{
			WaitForUserToChooseSomething,
			Working,
			ShowPdf,
			Upload,
			Printing,
			ResumeAfterPrint,
			Android,
			EPUB
		}

		public enum BookletPortions
		{
			None,
			AllPagesNoBooklet,
			BookletCover,
			BookletPages,//include front and back matter that isn't coverstock
			InnerContent//excludes all front and back matter
		}

		public enum BookletLayoutMethod
		{
			NoBooklet,
			SideFold,
			CutAndStack,
			Calendar
		}

		private Book.Book _currentlyLoadedBook;
		private PdfMaker _pdfMaker;
		private readonly CurrentEditableCollectionSelection _currentBookCollectionSelection;
		private readonly CollectionSettings _collectionSettings;
		private readonly BookServer _bookServer;
		private readonly BookThumbNailer _thumbNailer;
		private string _lastDirectory;


		public PublishModel(BookSelection bookSelection, PdfMaker pdfMaker, CurrentEditableCollectionSelection currentBookCollectionSelection, CollectionSettings collectionSettings,
			BookServer bookServer, BookThumbNailer thumbNailer)
		{
			BookSelection = bookSelection;
			_pdfMaker = pdfMaker;
			_pdfMaker.CompressPdf = true;	// See http://issues.bloomlibrary.org/youtrack/issue/BL-3721.
			//_pdfMaker.EngineChoice = collectionSettings.PdfEngineChoice;
			_currentBookCollectionSelection = currentBookCollectionSelection;
			ShowCropMarks=false;
			_collectionSettings = collectionSettings;
			_bookServer = bookServer;
			_thumbNailer = thumbNailer;
			bookSelection.SelectionChanged += OnBookSelectionChanged;
			//we don't want to default anymore: BookletPortion = BookletPortions.BookletPages;
		}

		public PublishView View { get; set; }

		// True when we are showing the controls for uploading. (Review: does this belong in the model or view?)
		public bool UploadMode { get; set; }

		// True when showing an ePUB preview.
		public bool EpubMode;

		public bool PdfGenerationSucceeded { get; set; }

		private void OnBookSelectionChanged(object sender, BookSelectionChangedEventArgs bookSelectionChangedEventArgs)
		{
			//some of this checking is about bl-272, which was replicated by having one book, going to publish, then deleting that last book.
			if (BookSelection != null && View != null && BookSelection.CurrentSelection!=null && _currentlyLoadedBook != BookSelection.CurrentSelection && View.Visible)
			{
				PageLayout = BookSelection.CurrentSelection.GetLayout();
			}
		}

		internal static string GetPreparingImageFilter()
		{
			var msgFmt = L10NSharp.LocalizationManager.GetString("ImageUtils.PreparingImage", "Preparing image: {0}", "{0} is a placeholder for the image file name");
			var idx = msgFmt.IndexOf("{0}");
			return idx >= 0 ? msgFmt.Substring(0,idx) : msgFmt; // translated string is missing the filename placeholder?
		}

		public void LoadBook(BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs)
		{
			try
			{
				using (var tempHtml = MakeFinalHtmlForPdfMaker())
				{
					if (doWorkEventArgs.Cancel)
						return;

					BookletLayoutMethod layoutMethod = GetBookletLayoutMethod();

					// Check memory for the benefit of developers.  The user won't see anything.
					SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "about to create PDF file", false);
					_pdfMaker.MakePdf(new PdfMakingSpecs() {InputHtmlPath = tempHtml.Key,
							OutputPdfPath=PdfFilePath,
							PaperSizeName=PageLayout.SizeAndOrientation.PageSizeName,
							Landscape=PageLayout.SizeAndOrientation.IsLandScape,
							SaveMemoryMode=_currentlyLoadedBook.UserPrefs.ReducePdfMemoryUse,
							LayoutPagesForRightToLeft=LayoutPagesForRightToLeft,
							BooketLayoutMethod=layoutMethod,
							BookletPortion=BookletPortion,
							BookIsFullBleed = _currentlyLoadedBook.FullBleed,
							PrintWithFullBleed = GetPrintingWithFullBleed(),
							Cmyk = _currentlyLoadedBook.UserPrefs.CmykPdf},
						worker, doWorkEventArgs, View );
					// Warn the user if we're starting to use too much memory.
					SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "finished creating PDF file", true);
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
		}

		private BookletLayoutMethod GetBookletLayoutMethod()
		{
			BookletLayoutMethod layoutMethod;
			if (this.BookletPortion == BookletPortions.AllPagesNoBooklet)
				layoutMethod = BookletLayoutMethod.NoBooklet;
			else
				layoutMethod = BookSelection.CurrentSelection.GetBookletLayoutMethod(PageLayout);
			return layoutMethod;
		}

		public bool IsCurrentBookFullBleed => _currentlyLoadedBook != null && _currentlyLoadedBook.FullBleed;

		private bool GetPrintingWithFullBleed()
		{
			return _currentlyLoadedBook.FullBleed && GetBookletLayoutMethod() == BookletLayoutMethod.NoBooklet && _currentlyLoadedBook.UserPrefs.FullBleed;
		}

		private bool LayoutPagesForRightToLeft
		{
			get { return _currentlyLoadedBook.BookData.Language1.IsRightToLeft;  }
		}

		private SimulatedPageFile MakeFinalHtmlForPdfMaker()
		{
			PdfFilePath = GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));

			var orientationChanging = BookSelection.CurrentSelection.GetLayout().SizeAndOrientation.IsLandScape !=
			                          PageLayout.SizeAndOrientation.IsLandScape;
			var dom = BookSelection.CurrentSelection.GetDomForPrinting(BookletPortion, _currentBookCollectionSelection.CurrentSelection,
				_bookServer, orientationChanging, PageLayout);

			AddStylesheetClasses(dom.RawDom);

			PageLayout.UpdatePageSplitMode(dom.RawDom);
			if (_currentlyLoadedBook.FullBleed && !GetPrintingWithFullBleed())
			{
				ClipBookToRemoveFullBleed(dom);
			}

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom.RawDom);
			dom.UseOriginalImages = true; // don't want low-res images or transparency in PDF.
			return BloomServer.MakeSimulatedPageFileInBookFolder(dom, source:BloomServer.SimulatedPageFileSource.Pub);
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
			if (BookSelection.CurrentSelection.GetDefaultBookletLayoutMethod() == PublishModel.BookletLayoutMethod.Calendar)
			{
				HtmlDom.AddCalendarFoldClassToBody(dom);
			}
		}

		private string GetPdfPath(string fname)
		{
			string path = null;

			// Sanitize fileName first
			string fileName = SanitizeFileName(fname);

			for (int i = 0; i < 100; i++)
			{
				path = Path.Combine(Path.GetTempPath(), string.Format("{0}-{1}.pdf", fileName, i));
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
			return path;
		}

		/// <summary>
		/// Ampersand in book title was causing Publish problems
		/// </summary>
		/// <param name="fileName"></param>
		/// <returns></returns>
		private static string SanitizeFileName(string fileName)
		{
			fileName = Path.GetInvalidFileNameChars().Aggregate(
				fileName, (current, character) => current.Replace(character, ' '));
			// I (GJM) set this up to keep ampersand out of the book title,
			// but discovered that ampersand isn't one of the characters that GetInvalidFileNameChars returns!
			fileName = fileName.Replace('&', ' ');
			return fileName;
		}

		DisplayModes _currentDisplayMode = DisplayModes.WaitForUserToChooseSomething;
		internal DisplayModes DisplayMode
		{
			get
			{
				return _currentDisplayMode;
			}
			set
			{
				_currentDisplayMode = value;
				if (View != null)
					View.Invoke((Action) (() => View.SetDisplayMode(value)));
			}
		}

		public void Dispose()
		{
			if (RobustFile.Exists(PdfFilePath))
			{
				try
				{
					RobustFile.Delete(PdfFilePath);
				}
				catch (Exception)
				{

				}
			}

			GC.SuppressFinalize(this);
		}

		public BookletPortions BookletPortion { get; set; }

		/// <summary>
		/// The book itself has a layout, but we can override it here during publishing
		/// </summary>
		public Layout PageLayout { get; set; }

		public bool ShowCropMarks
		{
			get { return _pdfMaker.ShowCropMarks; }
			set { _pdfMaker.ShowCropMarks = value; }
		}

		public bool AllowUpload => BookSelection.CurrentSelection.BookInfo.AllowUploading;

		public bool AllowPdf => true;

		public bool AllowPdfBooklet
		{
			get
			{
				// Large page sizes can't make booklets.  See http://issues.bloomlibrary.org/youtrack/issue/BL-4155.
				var size = PageLayout.SizeAndOrientation.PageSizeName;
				return AllowPdf && BookSelection.CurrentSelection.BookInfo.BookletMakingIsAppropriate &&
					(size != "A4" && size != "A3" && size != "B5" && size != "Letter" && size != "Device16x9");
			}
		}

		// currently the only cover option we have is a booklet one
		public bool AllowPdfCover => AllowPdfBooklet;

		public void Save()
		{
			try
			{
				// Give a slight preference to USB keys, though if they used a different directory last time, we favor that.

				if (string.IsNullOrEmpty(_lastDirectory) || !Directory.Exists(_lastDirectory))
				{
					var drives = SIL.UsbDrive.UsbDriveInfo.GetDrives();
					if (drives != null && drives.Count > 0)
					{
						_lastDirectory = drives[0].RootDirectory.FullName;
					}
				}

				using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
				{
					if (!string.IsNullOrEmpty(_lastDirectory) && Directory.Exists(_lastDirectory))
						dlg.InitialDirectory = _lastDirectory;
					var portion = "";
					switch (BookletPortion)
					{
						case BookletPortions.None:
							Debug.Fail("Save should not be enabled");
							return;
						case BookletPortions.AllPagesNoBooklet:
							portion = "Pages";
							break;
						case BookletPortions.BookletCover:
							portion = "Cover";
							break;
						case BookletPortions.BookletPages:
							portion = "Inside";
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}

					string forPrintShop =
						_currentlyLoadedBook.UserPrefs.CmykPdf || _currentlyLoadedBook.UserPrefs.FullBleed
							? "-printshop"
							: "";
					string suggestedName = string.Format($"{Path.GetFileName(_currentlyLoadedBook.FolderPath)}-{_currentlyLoadedBook.GetFilesafeLanguage1Name("en")}-{portion}{forPrintShop}.pdf");
					dlg.FileName = suggestedName;
					var pdfFileLabel = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.PdfFile",
						"PDF File",
						@"displayed as file type for Save File dialog.");

					pdfFileLabel = pdfFileLabel.Replace("|", "");
					dlg.Filter = String.Format("{0}|*.pdf", pdfFileLabel);
					dlg.OverwritePrompt = true;
					if (DialogResult.OK == dlg.ShowDialog())
					{
						_lastDirectory = Path.GetDirectoryName(dlg.FileName);
						if (_currentlyLoadedBook.UserPrefs.CmykPdf)
						{
							// PDF for Printshop (CMYK US Web Coated V2)
							ProcessPdfFurtherAndSave(ProcessPdfWithGhostscript.OutputType.Printshop, dlg.FileName);
						} else {
							// we want the simple PDF we already made.
							RobustFile.Copy(PdfFilePath, dlg.FileName, true);
						}
						Analytics.Track("Save PDF", new Dictionary<string, string>()
							{
								{"Portion",  Enum.GetName(typeof(BookletPortions), BookletPortion)},
								{"Layout", PageLayout.ToString()},
								{"BookId", BookSelection.CurrentSelection.ID },
								{"Country", _collectionSettings.Country}
							});
					}
				}
			}
			catch (Exception err)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem("Bloom was not able to save the PDF.  {0}", err.Message);
			}
		}

		private void ProcessPdfFurtherAndSave(ProcessPdfWithGhostscript.OutputType type, string outputPath)
		{
			if (type == ProcessPdfWithGhostscript.OutputType.Printshop &&
				!Bloom.Properties.Settings.Default.AdobeColorProfileEula2003Accepted)
			{
				var prolog = L10NSharp.LocalizationManager.GetString(@"PublishTab.PrologToAdobeEula",
					"Bloom uses Adobe color profiles to convert PDF files from using RGB color to using CMYK color.  This is part of preparing a \"PDF for a print shop\".  You must agree to the following license in order to perform this task in Bloom.",
					@"Brief explanation of what this license is and why the user needs to agree to it");
				using (var dlg = new Bloom.Registration.LicenseDialog("AdobeColorProfileEULA.htm", prolog))
				{
					dlg.Text = L10NSharp.LocalizationManager.GetString(@"PublishTab.AdobeEulaTitle",
						"Adobe Color Profile License Agreement", @"dialog title for license agreement");
					if (dlg.ShowDialog() != DialogResult.OK)
					{
						var msg = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfNotSavedWhy",
							"The PDF file has not been saved because you chose not to allow producing a \"PDF for print shop\".",
							@"explanation that file was not saved displayed in a message box");
						var heading = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfNotSaved",
							"PDF Not Saved", @"title for the message box");
						MessageBox.Show(msg, heading, MessageBoxButtons.OK, MessageBoxIcon.Information);
						return;
					}
				}
				Bloom.Properties.Settings.Default.AdobeColorProfileEula2003Accepted = true;
				Bloom.Properties.Settings.Default.Save();
			}
			using (var progress = new SIL.Windows.Forms.Progress.ProgressDialog())
			{
				progress.ProgressRangeMinimum = 0;
				progress.ProgressRangeMaximum = 100;
				progress.Overview = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.Saving",
					"Saving PDF...",
					@"Message displayed in a progress report dialog box");
				progress.BackgroundWorker = new BackgroundWorker();
				progress.BackgroundWorker.DoWork += (object sender, DoWorkEventArgs e) => {
					var pdfProcess = new ProcessPdfWithGhostscript(type, sender as BackgroundWorker);
					pdfProcess.ProcessPdfFile(PdfFilePath, outputPath, _currentlyLoadedBook.FullBleed);
				};
				progress.BackgroundWorker.ProgressChanged += (object sender, ProgressChangedEventArgs e) => {
					progress.Progress = e.ProgressPercentage;
					var status = e.UserState as string;
					if (!String.IsNullOrWhiteSpace(status))
						progress.StatusText = status;
				};
				progress.ShowDialog();	// will start the background process when loaded/showing
				if (progress.ProgressStateResult != null && progress.ProgressStateResult.ExceptionThatWasEncountered != null)
				{
					string shortMsg = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.ErrorSaving",
						"Error compressing or recoloring the PDF file",
						@"Message briefly displayed to the user in a toast");
					var longMsg = String.Format("Exception encountered processing the PDF file: {0}", progress.ProgressStateResult.ExceptionThatWasEncountered);
					NonFatalProblem.Report(ModalIf.None, PassiveIf.All, shortMsg, longMsg, progress.ProgressStateResult.ExceptionThatWasEncountered);
				}
			}
		}

		public void DebugCurrentPDFLayout()
		{

//			var dom = BookSelection.CurrentSelection.GetDomForPrinting(BookletPortion, _currentBookCollectionSelection.CurrentSelection, _bookServer);
//
//			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(dom, PageLayout);
//			PageLayout.UpdatePageSplitMode(dom);
//
//			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
//			var tempHtml = BloomTemp.TempFile.CreateHtm5FromXml(dom); //nb: we intentially don't ever delete this, to aid in debugging
//			//var tempHtml = TempFile.WithExtension(".htm");
//
//			var settings = new XmlWriterSettings {Indent = true, CheckCharacters = true};
//			using (var writer = XmlWriter.Create(tempHtml.Path, settings))
//			{
//				dom.WriteContentTo(writer);
//				writer.Close();
//			}

//			System.Diagnostics.Process.Start(tempHtml.Path);

			var htmlFilePath = MakeFinalHtmlForPdfMaker().Key;
			if (SIL.PlatformUtilities.Platform.IsWindows)
				Process.Start("Firefox.exe", '"' + htmlFilePath + '"');
			else
				SIL.Program.Process.SafeStart("xdg-open", '"' + htmlFilePath + '"');
		}

		public void UpdateModelUponActivation()
		{
			if (BookSelection.CurrentSelection == null)
				return;
			_currentlyLoadedBook = BookSelection.CurrentSelection;
			PageLayout = _currentlyLoadedBook.GetLayout();
			// BL-8648: In case we have an older version of a book (downloaded, e.g.) and the user went
			// straight to the Publish tab avoiding the Edit tab, we could arrive here needing to update
			// things. We choose to do the original book update here (when the user clicks on the Publish tab),
			// instead of the various places that we publish the book.
			// Note that the BringBookUpToDate() called by PublishHelper.MakeDeviceXmatterTempBook() and
			// called by BloomReaderFileMaker.PrepareBookForBloomReader() applies to a copy of the book
			// and is done in a way that explicitly avoids updating images. This call updates the images,
			// if needed, as a permanent fix.
			using (var dlg = new ProgressDialogForeground())
			{
				dlg.ShowAndDoWork(progress => _currentlyLoadedBook.BringBookUpToDate(progress));
			}
		}

		[Import("GetPublishingMenuCommands")]//, AllowDefault = true)]
		private Func<IEnumerable<ToolStripItem>> _getExtensionMenuItems;

		public IEnumerable<HtmlDom> GetPageDoms()
		{
			if (BookSelection.CurrentSelection.IsFolio)
			{
				foreach (var bi in _currentBookCollectionSelection.CurrentSelection.GetBookInfos())
				{
					var book = _bookServer.GetBookFromBookInfo(bi);
					//need to hide the "notes for illustrators" on SHRP, which is controlled by the layout
					book.SetLayout(new Layout()
					{
						SizeAndOrientation =  SizeAndOrientation.FromString("B5Portrait"),
						Style = "HideProductionNotes"
					});
					foreach (var page in  book.GetPages())
					{
						//yield return book.GetPreviewXmlDocumentForPage(page);

						var previewXmlDocumentForPage = book.GetPreviewXmlDocumentForPage(page);
						BookStorage.SetBaseForRelativePaths(previewXmlDocumentForPage, book.FolderPath);

						AddStylesheetClasses(previewXmlDocumentForPage.RawDom);

						yield return previewXmlDocumentForPage;
					}
				}
			}
			else //this one is just for testing, it's not especially fruitful to export for a single book
			{
				//need to hide the "notes for illustrators" on SHRP, which is controlled by the layout
				BookSelection.CurrentSelection.SetLayout(new Layout()
				{
					SizeAndOrientation = SizeAndOrientation.FromString("B5Portrait"),
					Style = "HideProductionNotes"
				});

				foreach (var page in BookSelection.CurrentSelection.GetPages())
				{
					var previewXmlDocumentForPage = BookSelection.CurrentSelection.GetPreviewXmlDocumentForPage(page);
					//get the original images, not compressed ones (just in case the thumbnails are, like, full-size & they want quality)
					BookStorage.SetBaseForRelativePaths(previewXmlDocumentForPage, BookSelection.CurrentSelection.FolderPath);
					AddStylesheetClasses(previewXmlDocumentForPage.RawDom);
					yield return previewXmlDocumentForPage;
				}
			}
		}

		public void GetThumbnailAsync(int width, int height, HtmlDom dom,Action<Image> onReady ,Action<Exception> onError)
		{
			var thumbnailOptions = new HtmlThumbNailer.ThumbnailOptions()
			{
				BackgroundColor = Color.White,
				BorderStyle = HtmlThumbNailer.ThumbnailOptions.BorderStyles.None,
				CenterImageUsingTransparentPadding = false,
				Height = height,
				Width = width
			};
			dom.UseOriginalImages = true; // apparently these thumbnails can be big...anyway we want printable images.
			_thumbNailer.HtmlThumbNailer.GetThumbnailAsync(String.Empty, string.Empty, dom, thumbnailOptions,onReady, onError);
		}

		public IEnumerable<ToolStripItem> GetExtensionMenuItems()
		{
			//for now we're not doing real extension dlls, just kind of faking it. So we will limit this load
			//to books we know go with this currently "built-in" "extension" for SIL LEAD's SHRP Project.
			if (SHRP_PupilBookExtension.ExtensionIsApplicable(BookSelection.CurrentSelection))
			{
				//load any extension assembly found in the template's root directory
				//var catalog = new DirectoryCatalog(this.BookSelection.CurrentSelection.FindTemplateBook().FolderPath, "*.dll");
				var catalog = new AssemblyCatalog(Assembly.GetExecutingAssembly());
				var container = new CompositionContainer(catalog);
				//inject what we have to offer for the extension to consume
				container.ComposeExportedValue<string>("PathToBookFolder",BookSelection.CurrentSelection.FolderPath);
				container.ComposeExportedValue<string>("Language1Iso639Code", _currentlyLoadedBook.BookData.Language1.Iso639Code);
				container.ComposeExportedValue<Func<IEnumerable<HtmlDom>>>(GetPageDoms);
			  //  container.ComposeExportedValue<Func<string>>("pathToPublishedHtmlFile",GetFileForPrinting);
				//get the original images, not compressed ones (just in case the thumbnails are, like, full-size & they want quality)
				container.ComposeExportedValue<Action<int, int, HtmlDom, Action<Image>, Action<Exception>>>(GetThumbnailAsync);
				container.SatisfyImportsOnce(this);
				return _getExtensionMenuItems == null ? new List<ToolStripItem>() : _getExtensionMenuItems();
			}
			else
			{
				return new List<ToolStripMenuItem>();
			}
		}

		public void ReportAnalytics(string eventName)
		{
			Analytics.Track(eventName, new Dictionary<string, string>()
			{
				{"BookId", BookSelection.CurrentSelection.ID},
				{"Country", _collectionSettings.Country}
			});
		}

		/// <summary>
		/// Remove all text data that is not in a desired language.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-7124.
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-7998 for when we need to prune xmatter pages.
		/// </remarks>
		public static void RemoveUnwantedLanguageData(HtmlDom dom, IEnumerable<string> languagesToInclude, string nationalLang=null)
		{
			//Debug.Write("PublishModel.RemoveUnwantedLanguageData(): languagesToInclude =");
			//foreach (var lang in languagesToInclude)
			//	Debug.Write($" {lang}");
			//Debug.WriteLine();
			// Place the desired language tags plus the two standard pseudolanguage tags in a HashSet
			// for fast access.
			var contentLanguages = new HashSet<string>();
			foreach (var lang in languagesToInclude)
				contentLanguages.Add(lang);
			contentLanguages.Add("*");
			contentLanguages.Add("z");
			// Don't change the div#bloomDataDiv:  thus we have an outer loop that
			// selects only xmatter and user content pages.
			// While we could probably safely remove elements from div#bloomDataDiv,
			// we decided to play it very safe for now and leave it all intact.
			// The default behavior is also to not touch xmatter pages.  But if the code for the national language (aka L2) is
			// provided, then we prune xmatter pages as well but add the national language to the list of languages whose data
			// we keep in the xmatter.
			// We can always come back to this if we realize we should be removing more.
			// If that happens, removing the outer loop and checking the data-book attribute (and
			// maybe the data-derived attribute) may become necessary.
			foreach (var page in dom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]").Cast<XmlElement>().ToList())
			{
				var isXMatter = !String.IsNullOrWhiteSpace(page.GetAttribute("data-xmatter-page"));
				if (isXMatter && nationalLang == null)
					continue;	// default behavior is to skip pruning data from xmatter
				foreach (var div in page.SafeSelectNodes(".//div[@lang]").Cast<XmlElement>().ToList())
				{
					var lang = div.GetAttribute("lang");
					if (String.IsNullOrEmpty(lang) || contentLanguages.Contains(lang) || (isXMatter && lang == nationalLang))
						continue;
					var classAttr = div.GetAttribute("class");
					// retain the .pageLabel and .pageDescription divs (which are always lang='en')
					// Also retain any .Instructions-style divs, which may have the original with lang='en', and
					// which are usually translated to the national language.
					// REVIEW: are there any other classes that should be checked here?
					if (classAttr.Contains("pageLabel") || classAttr.Contains("pageDescription") || classAttr.Contains("Instructions-style"))
						continue;
					// check whether any descendant divs are desired before deleting this div.
					bool deleteDiv = true;
					foreach (var subdiv in div.SafeSelectNodes(".//div[@lang]").Cast<XmlElement>().ToList())
					{
						var sublang = subdiv.GetAttribute("lang");
						if (String.IsNullOrEmpty(sublang))
							continue;
						if (contentLanguages.Contains(sublang) || (isXMatter && sublang == nationalLang))
						{
							deleteDiv = false;
							break;
						}
					}
					// Remove this div
					if (deleteDiv)
						div.ParentNode.RemoveChild(div);
				}
			}
		}

		// This is a highly experimental export which may evolve as we work on this with Age of Learning.
		public void ExportAudioFiles1PerPage()
		{
			var container = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bloom audio export");
			Directory.CreateDirectory(container);
			var parentFolderForAllOfTheseExports  = TemporaryFolder.TrackExisting(container);
			var  folderForThisBook = new TemporaryFolder(parentFolderForAllOfTheseExports, Path.GetFileName(this.BookSelection.CurrentSelection.FolderPath));
			var pageIndex = 0;

			foreach (XmlElement pageElement in this.BookSelection.CurrentSelection.GetPageElements())
			{
				++pageIndex;
				//var durations = new StringBuilder();
				//var accumulatedDuration = 0;
				try
				{
					// These elements are marked as audio-sentence but we're not sure yet if the user actually recorded them yet
					var audioSentenceElements = HtmlDom.SelectAudioSentenceElements(pageElement)
						.Cast<XmlElement>();

					var mergeFiles =
						audioSentenceElements
							.Select(s =>
								AudioProcessor.GetOrCreateCompressedAudio(
									this.BookSelection.CurrentSelection.FolderPath, s.Attributes["id"]?.Value))
							.Where(s => !string.IsNullOrEmpty(s));
					if (mergeFiles.Any())
					{
						// enhance: it would be nice if we could somehow provide info on what should be highlighted and when,
						// though I don't know how that would work with Age of Learning's PDF viewer.
						// The following was a start on that before I realized that I don't know how that would be accomplished,
						// but I'm leaving it here in case I pick it up again.
						// foreach (var audioSentenceElement in audioSentenceElements)
						//{
						//	var id = HtmlDom.GetAttributeValue(audioSentenceElement, "id");
						//	var element = this.BookSelection.CurrentSelection.OurHtmlDom.SelectSingleNode($"//div[@id='{id}']");
						//	var duration = HtmlDom.GetAttributeValue(audioSentenceElement, "data-duration");
						//   Here we would need to determine the duration if data-duration is empty.
						//	accumulatedDuration += int.Parse(duration);
						//	durations.AppendLine(accumulatedDuration.ToString() + "\t" + duration);
						//}
						var filename =
							$"{this.BookSelection.CurrentSelection.Title}_{this._currentlyLoadedBook.BookData.Language1.Name}_{pageIndex:0000}.mp3".Replace(' ','_');
						var combinedAudioPath = Path.Combine(folderForThisBook.FolderPath, filename);
						var errorMessage = AudioProcessor.MergeAudioFiles(mergeFiles, combinedAudioPath);
						if (errorMessage != null)
						{
							File.WriteAllText(Path.Combine(folderForThisBook.FolderPath, $"error page{pageIndex}.txt"),
								errorMessage);
						}
						//File.WriteAllText(Path.Combine(folderForThisBook.FolderPath, $"page{pageIndex} timings.txt"),
						//	durations.ToString());
					}
				}
				catch (Exception e)
				{
					File.WriteAllText(Path.Combine(folderForThisBook.FolderPath, $"error page{pageIndex}.txt"),
						e.Message);
				}
			}

			Process.Start(folderForThisBook.FolderPath);
		}
	}
}
