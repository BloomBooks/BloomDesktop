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
using DesktopAnalytics;
using SIL.IO;
using SIL.Xml;
using PdfDroplet.LayoutMethods;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Bloom.Publish
{
	/// <summary>
	/// Contains the logic behind the PublishView control, which involves creating a pdf from the html book and letting you print it.
	/// </summary>
	public class PublishModel : IDisposable
	{
		public BookSelection BookSelection { get; private set; }

		public string PdfFilePath { get; private set; }

		private EpubMaker _epubMaker;

		public enum DisplayModes
		{
			WaitForUserToChooseSomething,
			Working,
			ShowPdf,
			Upload,
			EPUB,
			Printing,
			ResumeAfterPrint
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
		private NavigationIsolator _isoloator;

		public PublishModel(BookSelection bookSelection, PdfMaker pdfMaker, CurrentEditableCollectionSelection currentBookCollectionSelection, CollectionSettings collectionSettings,
			BookServer bookServer, BookThumbNailer thumbNailer, NavigationIsolator isolator)
		{
			BookSelection = bookSelection;
			_pdfMaker = pdfMaker;
			//_pdfMaker.EngineChoice = collectionSettings.PdfEngineChoice;
			_currentBookCollectionSelection = currentBookCollectionSelection;
			ShowCropMarks=false;
			_collectionSettings = collectionSettings;
			_bookServer = bookServer;
			_thumbNailer = thumbNailer;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			_isoloator = isolator;
			//we don't want to default anymore: BookletPortion = BookletPortions.BookletPages;
		}

		public PublishView View { get; set; }

		// True when we are showing the controls for uploading. (Review: does this belong in the model or view?)
		public bool UploadMode { get; set; }

		// True when showing an ePUB preview.
		public bool EpubMode { get; set; }

		public bool PdfGenerationSucceeded { get; set; }

		private void OnBookSelectionChanged(object sender, EventArgs e)
		{
			//some of this checking is about bl-272, which was replicated by having one book, going to publish, then deleting that last book.
			if (BookSelection != null && View != null && BookSelection.CurrentSelection!=null && _currentlyLoadedBook != BookSelection.CurrentSelection && View.Visible)
			{
				PageLayout = BookSelection.CurrentSelection.GetLayout();
			}
		}


		public void LoadBook(BackgroundWorker worker, DoWorkEventArgs doWorkEventArgs)
		{
			_currentlyLoadedBook = BookSelection.CurrentSelection;

			try
			{
				using(var tempHtml = MakeFinalHtmlForPdfMaker())
				{
					if (doWorkEventArgs.Cancel)
						return;

					BookletLayoutMethod layoutMethod;
					if (this.BookletPortion == BookletPortions.AllPagesNoBooklet)
						layoutMethod = BookletLayoutMethod.NoBooklet;
					else
						layoutMethod = BookSelection.CurrentSelection.GetDefaultBookletLayout();

					// Check memory for the benefit of developers.  The user won't see anything.
					SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "about to create PDF file", false);
					_pdfMaker.MakePdf(tempHtml.Key, PdfFilePath, PageLayout.SizeAndOrientation.PageSizeName,
						PageLayout.SizeAndOrientation.IsLandScape, LayoutPagesForRightToLeft,
						layoutMethod, BookletPortion, worker, doWorkEventArgs, View);
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

		private bool LayoutPagesForRightToLeft
		{
			get { return _collectionSettings.IsLanguage1Rtl;  }

		}

		private SimulatedPageFile MakeFinalHtmlForPdfMaker()
		{
			PdfFilePath = GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));

			var dom = BookSelection.CurrentSelection.GetDomForPrinting(BookletPortion, _currentBookCollectionSelection.CurrentSelection, _bookServer);

			AddStylesheetClasses(dom.RawDom);

			//we do this now becuase the publish ui allows the user to select a different layout for the pdf than what is in the book file
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(dom.RawDom, PageLayout);
			PageLayout.UpdatePageSplitMode(dom.RawDom);

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom.RawDom);
			ShrinkImagesIfNeeded(dom.RawDom, _currentlyLoadedBook.FolderPath);
			dom.UseOriginalImages = true; // don't want low-res images or transparency in PDF.
			return EnhancedImageServer.MakeSimulatedPageFileInBookFolder(dom);
		}

		private void AddStylesheetClasses(XmlDocument dom)
		{
			HtmlDom.AddPublishClassToBody(dom);
			if (LayoutPagesForRightToLeft)
				HtmlDom.AddRightToLeftClassToBody(dom);
			HtmlDom.AddHidePlaceHoldersClassToBody(dom);
			if (BookSelection.CurrentSelection.GetDefaultBookletLayout() == PublishModel.BookletLayoutMethod.Calendar)
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
				if (!File.Exists(path))
					break;

				try
				{
					File.Delete(path);
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
			if (File.Exists(PdfFilePath))
			{
				try
				{
					File.Delete(PdfFilePath);
				}
				catch (Exception)
				{

				}
			}
			if (_epubMaker != null)
			{
				_epubMaker.Dispose();
				_epubMaker = null;
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

		public bool AllowUpload {
			get { return BookSelection.CurrentSelection.BookInfo.AllowUploading; }
		}

		public bool ShowBookletOption
		{
			get
			{
				return BookSelection.CurrentSelection.BookInfo.BookletMakingIsAppropriate &&
				       BookSelection.CurrentSelection.GetLayout().SizeAndOrientation.PageSizeName != "Letter";
			}
		}

		public bool ShowCoverOption
		{
			//currently the only cover option we have is a booklet one
			get { return BookSelection.CurrentSelection.BookInfo.BookletMakingIsAppropriate; }
		}


		public void Save()
		{
			if (EpubMode)
			{
				try
				{
					SaveAsEpub();
				}
				catch (Exception err)
				{
					SIL.Reporting.ErrorReport.NotifyUserOfProblem("Bloom was not able to save the ePUB.  {0}", err.Message);
				}
				return;
			}
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

				using (var dlg = new SaveFileDialog())
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
					string suggestedName = string.Format("{0}-{1}-{2}.pdf", Path.GetFileName(_currentlyLoadedBook.FolderPath),
														 _collectionSettings.GetLanguage1Name("en"), portion);
					dlg.FileName = suggestedName;
					dlg.Filter = "PDF|*.pdf";
					if (DialogResult.OK == dlg.ShowDialog())
					{
						_lastDirectory = Path.GetDirectoryName(dlg.FileName);
						File.Copy(PdfFilePath, dlg.FileName, true);
						Analytics.Track("Save PDF", new Dictionary<string, string>()
							{
								{"Portion",  Enum.GetName(typeof(BookletPortions), BookletPortion)},
								{"Layout", PageLayout.ToString()}
							});
					}
				}
			}
			catch (Exception err)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem("Bloom was not able to save the PDF.  {0}", err.Message);
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
				Process.Start("xdg-open", '"' + htmlFilePath + '"');
		}

		public void RefreshValuesUponActivation()
		{
			if (BookSelection.CurrentSelection != null)
			{
				PageLayout = BookSelection.CurrentSelection.GetLayout();
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
				container.ComposeExportedValue<string>("Language1Iso639Code", _collectionSettings.Language1Iso639Code);
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

		// PrepareToStageEpub must be called first
		internal bool BookHasAudio
		{
 			get { return _epubMaker.BookHasAudio; }
		}

		// PrepareToStageEpub must be called first
		internal void StageEpub(bool publishWithoutAudio)
		{
			_epubMaker.StageEpub(publishWithoutAudio);
		}

		internal void PrepareToStageEpub()
		{
			if (_epubMaker != null)
			{
				//it has state that we don't want to reuse, so make a new one
				_epubMaker.Dispose();
				_epubMaker = null;
			}
			_epubMaker = new EpubMaker(_thumbNailer, _isoloator);
			_epubMaker.Book = BookSelection.CurrentSelection;
			_epubMaker.Unpaginated = true; // Enhance: UI?
		}

		// PrepareToStageEpub must be called first
		internal bool IsCompressedAudioMissing
		{
			get { return _epubMaker.IsCompressedAudioMissing; }
		}

		internal string StagingDirectory { get { return _epubMaker.StagingDirectory; } }

		internal void SaveAsEpub()
		{
			using (var dlg = new SaveFileDialog())
			{
				if (!string.IsNullOrEmpty(_lastDirectory) && Directory.Exists(_lastDirectory))
					dlg.InitialDirectory = _lastDirectory;

				string suggestedName = string.Format("{0}-{1}.epub", Path.GetFileName(BookSelection.CurrentSelection.FolderPath),
													 _collectionSettings.GetLanguage1Name("en"));
				dlg.FileName = suggestedName;
				dlg.Filter = "EPUB|*.epub";
				if (DialogResult.OK == dlg.ShowDialog())
				{
					_lastDirectory = Path.GetDirectoryName(dlg.FileName);
					_epubMaker.FinishEpub(dlg.FileName);
					Analytics.Track("Save ePUB");
				}
			}
		}

		/// <summary>
		/// Shrink any image that has an effective DPI greater than 500 to have an effective DPI of 400 or so.  (Effective DPI means at
		/// the image size given by height and width in pixels.)
		/// This is an attempt to minimize the size of the generated PDF file.
		/// </summary>
		/// <remarks>
		/// Design/implementation questions that should be at least thought about.
		/// 1) The method currently creates new files as needed for printing and leaves them on the disk.  Should the new files be
		///    removed after the PDF has been created?  (Leaving them means we don't need to recreate them each time.)
		/// 2) The method uses the width and height attributes of the img element for the desired size.  Is this reliable enough
		///    in practice?
		/// 3) The method tries to save file access by using the title attribute of the parent div element.  Is this reliable
		///    enough to depend on in practice?
		/// 4) What about images that don't fit the standard pattern?  (cover page for sure, are there others?)  How can we handle
		///    them?
		/// 5) The estimated DPI for printing is a bit sloppy.  It assumes screen DPI ranges between 96 and 120, and that the print
		///    DPI can safely be set to 4 * screen DPI.  Is this a safe assumption?
		/// 6) Do we need progress reporting for this process?
		/// There may be other questions to answer as well.  The initial result of using this code reduced a PDF containing
		/// about 20 photographs from 378,195,191 (!) bytes on the disk to "only" 103,465,177 bytes.
		/// Note that this initial work was done on Linux/Mono, so we know it works there.
		/// </remarks>
		private void ShrinkImagesIfNeeded(XmlDocument dom, string folder)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//div/img"))
			{
				var parent = node.ParentNode;
				var title = parent.GetOptionalStringAttribute("title", null);	// "100_10431.jpg 888.85 KB 3264 x 2448 770 DPI (should be 300-600) Bit Depth: 24"
				if (String.IsNullOrEmpty (title))
					continue;
				var src = node.GetOptionalStringAttribute("src", null);			// "100_10431.jpg"
				if (String.IsNullOrEmpty(src))
					continue;
				var srcDecoded = System.Web.HttpUtility.UrlDecode(src);
				// height and width (which are in screen pixels) may not be accurate, but they're the best
				// information we have without going through a lot more effort.
				var height = node.GetOptionalStringAttribute("height", null);	// "305"
				var width = node.GetOptionalStringAttribute("width", null);		// "407"
				if (String.IsNullOrEmpty(height) || String.IsNullOrEmpty(width))
					continue;
				int pxHeight;
				int pxWidth;
				if (!Int32.TryParse(height, out pxHeight) || !Int32.TryParse(width, out pxWidth))
					continue;
				int rawDPI = GetDPIFromTitleAttribute(title, srcDecoded);
				if (rawDPI > 0 && rawDPI < 500)
					continue;	// The file's effective DPI seems to be reasonable already, so don't change anything.
				var oldFilepath = Path.Combine(folder, srcDecoded);
				if (!File.Exists(oldFilepath))
					continue;
				if (rawDPI < 0)
					rawDPI = GetDPIFromBitmap(oldFilepath, pxWidth, pxHeight);
				if (rawDPI < 500)
					continue;
				// TODO: We really need a graphics object to get the DpiX and DpiY values, but we'll assume 96 (or maybe 120)
				// as the best guess for a computer screen to get started.
				int desiredWidth = pxWidth * 4;		// give ~ 384 - 480 DPI depending on screen resolution
				int desiredHeight = pxHeight * 4;
				var newFilename = String.Format("{0}-{1}x{2}{3}", Path.GetFileNameWithoutExtension(srcDecoded), desiredWidth, desiredHeight, Path.GetExtension(srcDecoded));
				var newFilepath = Path.Combine(folder, newFilename);
				if (File.Exists(newFilepath))	// If we've already created a file at the desired size, don't bother doing so again.
					continue;
				// Create the new image file at the desired size and update the src attribute.
				using (var oldImage = new Bitmap(oldFilepath))
				{
					using (var newImage = ResizeImage(oldImage, new Size(desiredWidth, desiredHeight)))
					{
						var fmt = GetImageFormat(oldImage, Path.GetExtension(src));
						newImage.Save(newFilepath, fmt);
					}
				}
				var newSrc = String.Format("{0}-{1}x{2}{3}", Path.GetFileNameWithoutExtension(src), desiredWidth, desiredHeight, Path.GetExtension(src));
				node.SetAttribute("src", newSrc);
				Console.WriteLine("new filepath = {0}, new src = {1}", newFilepath, newSrc);
			}
		}

		/// <summary>
		/// Get the proper ImageFormat from either the current image's format or from the filename extension.
		/// </summary>
		/// <remarks>
		/// It may be a bug in the Mono library that keeps image.RawFormat from working directly.  But this
		/// method is needed, at least on Linux.
		/// </remarks>
		ImageFormat GetImageFormat(Bitmap image, string extension)
		{
			// Try to preserve the old file's format if possible.
			// Note that having the same guid isn't quite enough to ensure proper behaviour on output.
			if (image.RawFormat.Guid == ImageFormat.Jpeg.Guid)		return ImageFormat.Jpeg;
			if (image.RawFormat.Guid == ImageFormat.Png.Guid)		return ImageFormat.Png;
			if (image.RawFormat.Guid ==  ImageFormat.Tiff.Guid)		return ImageFormat.Tiff;
			if (image.RawFormat.Guid == ImageFormat.Bmp.Guid)		return ImageFormat.Bmp;
			if (image.RawFormat.Guid == ImageFormat.Emf.Guid)		return ImageFormat.Emf;
			if (image.RawFormat.Guid == ImageFormat.Exif.Guid)		return ImageFormat.Exif;
			if (image.RawFormat.Guid ==  ImageFormat.Gif.Guid)		return ImageFormat.Gif;
			if (image.RawFormat.Guid ==  ImageFormat.Icon.Guid)		return ImageFormat.Icon;
			if (image.RawFormat.Guid ==  ImageFormat.Wmf.Guid)		return ImageFormat.Wmf;
			// okay, try to guess from the filename extension (probably redundant code here)
			switch (extension.ToLowerInvariant())
			{
			case ".bmp":	return ImageFormat.Bmp;
			case ".gif":	return ImageFormat.Gif;
			case ".jpg":
			case ".jpeg":	return ImageFormat.Jpeg;
			case ".png":	return ImageFormat.Png;
			case ".tif":
			case ".tiff":	return ImageFormat.Tiff;
			}
			return ImageFormat.Png;		// seems to be the default, at least on Linux...
		}

		/// <summary>
		/// Parse the title attribute from the owning div element to get the estimated DPI of the image
		/// at the desired size.
		/// </summary>
		private static int GetDPIFromTitleAttribute(string title, string srcDecoded)
		{
			if (!title.StartsWith(srcDecoded))
				return -1;
			string titleSizes = title.Substring(srcDecoded.Length + 1);
			var sizes = titleSizes.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (sizes[3] != "x" || sizes[6] != "DPI")
				return -1;
			int rawWidth;
			int rawHeight;
			int rawDPI;
			if (!Int32.TryParse(sizes[2], out rawWidth) || !Int32.TryParse(sizes[4], out rawHeight) || !Int32.TryParse(sizes[5], out rawDPI))
				return -1;
			Console.WriteLine("{0} : size={1}x{2} rawDpi={3}", srcDecoded, rawWidth, rawHeight, rawDPI);
			return rawDPI;
		}

		/// <summary>
		/// Load the image into memory to get its estimated DPI at the desired size.
		/// </summary>
		/// <remarks>
		/// Is there some way to redesign this so that we only load the file into memory once when we need to reduce it?
		/// </remarks>
		private static int GetDPIFromBitmap(string oldFilepath, int pxWidth, int pxHeight)
		{
			using (var oldImage = new Bitmap(oldFilepath))
			{
				var g = Graphics.FromImage(oldImage);
				var inchWidth = pxWidth / g.DpiX;
				var inchHeight = pxHeight / g.DpiY;
				var dpiX = oldImage.Width / inchWidth;
				var dpiY = oldImage.Height / inchHeight;
				int rawDPI = (int)((dpiX + dpiY) / 2.0);
				Console.WriteLine("{0} : size={1}x{2} ({3}x{4}), g.DpiX={5}, g.DpiY={6}, rawDpiX={7}, rawDpiY={8}, rawDPI={9}",
					Path.GetFileName(oldFilepath), oldImage.Width, oldImage.Height, inchWidth, inchHeight, g.DpiX, g.DpiY, dpiX, dpiY, rawDPI);
				return rawDPI;
			}
		}

		/// <summary>
		/// Generate a new Bitmap of the desired size from the old one.
		/// </summary>
		/// <remarks>
		/// This code was adapted from some found at
		/// https://social.msdn.microsoft.com/Forums/en-US/e2e59871-f888-4d41-8aa2-fa7f3572c1ce/change-the-resolution-of-png-image-and-save-it?forum=csharplanguage.
		/// </remarks>
		private static Bitmap ResizeImage(Bitmap oldImage, Size newSize)
		{
			// The use of ratio, myHeight, myWidth, mySize, x, y are an attempt to smooth the differences
			// in the old size and the new size.  In practice, x and y should be in the order of 0 or 1, and
			// certainly no more than 2 or so.
			double ratio;
			if ((oldImage.Width / Convert.ToDouble(newSize.Width)) > (oldImage.Height / Convert.ToDouble(newSize.Height)))
				ratio = Convert.ToDouble(oldImage.Width) / Convert.ToDouble(newSize.Width);
			else
				ratio = Convert.ToDouble(oldImage.Height) / Convert.ToDouble(newSize.Height);
			var myHeight = Math.Ceiling(oldImage.Height / ratio);
			var myWidth = Math.Ceiling(oldImage.Width / ratio);
			var mySize = new Size((int)myWidth, (int)myHeight);
			var x = (newSize.Width - mySize.Width) / 2;
			var y = (newSize.Height - mySize.Height);

			var newImage = new Bitmap(newSize.Width, newSize.Height);
			var g = Graphics.FromImage(newImage);
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;
			var rect = new Rectangle(x, y, mySize.Width, mySize.Height);
			g.DrawImage(oldImage, rect, 0, 0, oldImage.Width, oldImage.Height, GraphicsUnit.Pixel);
			if (oldImage.HorizontalResolution != newImage.HorizontalResolution ||
				oldImage.VerticalResolution != newImage.VerticalResolution)
			{
				newImage.SetResolution(oldImage.HorizontalResolution, oldImage.VerticalResolution);
			}
			return newImage;
		}
	}
}
