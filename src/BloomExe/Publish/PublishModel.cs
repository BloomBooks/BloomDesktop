using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using DesktopAnalytics;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.Xml;

namespace Bloom.Publish
{
	/// <summary>
	/// Contains the logic behind the PublishView control, which involves creating a pdf from the html book and letting you print it.
	/// </summary>
	public class PublishModel : IDisposable
	{
		public BookSelection BookSelection { get; private set; }

		public string PdfFilePath { get; private set; }

		public enum DisplayModes
		{
			NoBook,
			Working,
			ShowPdf
		}

		public enum BookletPortions
		{
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
		private string _lastDirectory;

		public PublishModel(BookSelection bookSelection, PdfMaker pdfMaker, CurrentEditableCollectionSelection currentBookCollectionSelection, CollectionSettings collectionSettings, BookServer bookServer)
		{
			BookSelection = bookSelection;
			_pdfMaker = pdfMaker;
			_pdfMaker.EngineChoice = collectionSettings.PdfEngineChoice;
			_currentBookCollectionSelection = currentBookCollectionSelection;
			ShowCropMarks=false;
			_collectionSettings = collectionSettings;
			_bookServer = bookServer;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			BookletPortion = BookletPortions.BookletPages;
		}

		public PublishView View { get; set; }

		private void OnBookSelectionChanged(object sender, EventArgs e)
		{
			//some of this checking is about bl-272, which was replicated by having one book, going to publish, then deleting that last book.
			if (BookSelection != null && View != null && BookSelection.CurrentSelection!=null && _currentlyLoadedBook != BookSelection.CurrentSelection && View.Visible)
			{
				PageLayout = BookSelection.CurrentSelection.GetLayout();
			}
		}


		public void LoadBook(DoWorkEventArgs doWorkEventArgs)
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

					_pdfMaker.MakePdf(tempHtml.Path, PdfFilePath, PageLayout.SizeAndOrientation.PageSizeName, PageLayout.SizeAndOrientation.IsLandScape,
									  layoutMethod, BookletPortion,
										// note, we have this even if we're using some other engine... a bit awkward that, but it comes from
										// the fact that geckofx needs to be tied into the application event loop. The other engines work as
										// command line apps, in their own process.
										View.GeckofxHtmlToPdfComponent);
				}
			}
			catch (Exception e)
			{
				//we can't safely do any ui-related work from this thread, like putting up a dialog
				doWorkEventArgs.Result = e;
				//                Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "There was a problem creating a PDF from this book.");
				//                SetDisplayMode(DisplayModes.NoBook);
				//                return;
			}
		}

		private BloomTemp.TempFile MakeFinalHtmlForPdfMaker()
		{
			PdfFilePath = GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));

			XmlDocument dom = BookSelection.CurrentSelection.GetDomForPrinting(BookletPortion, _currentBookCollectionSelection.CurrentSelection, _bookServer);

			//we do this now becuase the publish ui allows the user to select a different layout for the pdf than what is in the book file
			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(dom, PageLayout);
			PageLayout.UpdatePageSplitMode(dom);

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
			return BloomTemp.TempFile.CreateHtm5FromXml(dom);
		}

		private string GetPdfPath(string fileName)
		{
			string path = null;

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

		private void SetDisplayMode(DisplayModes displayMode)
		{
			if (View != null)
				View.SetDisplayMode(displayMode);
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

		public override bool Equals(object obj)
		{
			if (obj == null)
				return false;

			var m = (PublishModel)obj ;
			return m.BookletPortion == BookletPortion && m.PageLayout == PageLayout;
		}

		public void Save()
		{
			try
			{
				// Give a slight preference to USB keys, though if they used a different directory last time, we favor that.

				if (string.IsNullOrEmpty(_lastDirectory) || !Directory.Exists(_lastDirectory))
				{
					var drives = Palaso.UsbDrive.UsbDriveInfo.GetDrives();
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
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem("Bloom was not able to save the PDF.  {0}", err.Message);
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

			System.Diagnostics.Process.Start("Chrome.exe",MakeFinalHtmlForPdfMaker().Path);
		}

		public void RefreshValuesUponActivation()
		{
			if (BookSelection.CurrentSelection!=null)
			{
				PageLayout = BookSelection.CurrentSelection.GetLayout();
			}

		}
	}
}
