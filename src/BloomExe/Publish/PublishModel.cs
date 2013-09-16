using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
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
			None,
			BookletCover,
			BookletPages
		}

		public enum BookletLayoutMethod
		{
			SideFold,
			CutAndStack,
			Calendar
		}

		private Book.Book _currentlyLoadedBook;
		private PdfMaker _pdfMaker;
		private readonly CollectionSettings _collectionSettings;
		private string _lastDirectory;

		public PublishModel(BookSelection bookSelection, PdfMaker pdfMaker, CollectionSettings collectionSettings)
		{
			BookSelection = bookSelection;
			_pdfMaker = pdfMaker;
			ShowCropMarks=false;
			_collectionSettings = collectionSettings;
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
				PdfFilePath = GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));

				XmlDocument dom = BookSelection.CurrentSelection.GetDomForPrinting(BookletPortion);

				//wkhtmltopdf can't handle file://
				dom.InnerXml = dom.InnerXml.Replace("file://", "");

				//we do this now becuase the publish ui allows the user to select a different layout for the pdf than what is in the book file
				SizeAndOrientation.UpdatePageSizeAndOrientationClasses(dom,PageLayout);
				PageLayout.UpdatePageSplitMode(dom);

				XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);

				using(var tempHtml = BloomTemp.TempFile.CreateHtm5FromXml(dom))
				{
					if (doWorkEventArgs.Cancel)
						return;

					_pdfMaker.MakePdf(tempHtml.Path, PdfFilePath, PageLayout.SizeAndOrientation.PageSizeName, PageLayout.SizeAndOrientation.IsLandScape,
									  BookSelection.CurrentSelection.GetDefaultBookletLayout(), BookletPortion, doWorkEventArgs);
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
						case BookletPortions.None:
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
			var dom = BookSelection.CurrentSelection.GetDomForPrinting(BookletPortion);

			SizeAndOrientation.UpdatePageSizeAndOrientationClasses(dom, PageLayout);
			PageLayout.UpdatePageSplitMode(dom);

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
			var tempHtml = BloomTemp.TempFile.CreateHtm5FromXml(dom); //nb: we intentially don't ever delete this, to aid in debugging
			//var tempHtml = TempFile.WithExtension(".htm");

			var settings = new XmlWriterSettings {Indent = true, CheckCharacters = true};
			using (var writer = XmlWriter.Create(tempHtml.Path, settings))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}

			System.Diagnostics.Process.Start(tempHtml.Path);
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
