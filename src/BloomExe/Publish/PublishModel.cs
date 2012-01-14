using System;
using System.ComponentModel;
using System.IO;
using System.Xml;
using Bloom.Book;
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
		public BookSelection BookSelection { get; set; }

		public string PdfFilePath{ get; private set;}
		public enum DisplayModes
		{
			NoBook, Working, ShowPdf
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

		private readonly BookSelection _bookSelection;
		private Book.Book _currentlyLoadedBook;
		private PdfMaker _pdfMaker;

		public PublishModel(BookSelection bookSelection, PdfMaker pdfMaker)
		{
			BookSelection = bookSelection;
			_bookSelection = bookSelection;
			_pdfMaker = pdfMaker;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			BookletPortion = BookletPortions.BookletPages;
		}

		public PublishView View { get; set; }


		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			if (_currentlyLoadedBook != BookSelection.CurrentSelection && View.Visible)
			{
			//	View.MakeBooklet();
			}
		}


		public void LoadBook(DoWorkEventArgs doWorkEventArgs)
		{
			_currentlyLoadedBook = BookSelection.CurrentSelection;

			try
			{

				PdfFilePath = GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));

				XmlDocument dom =   _bookSelection.CurrentSelection.GetDomForPrinting(BookletPortion);

				//wkhtmltopdf can't handle file://
				dom.InnerXml = dom.InnerXml.Replace("file://", "");
				XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);

				 using (var tempHtml = TempFile.WithExtension(".htm"))
				{
					XmlWriterSettings settings = new XmlWriterSettings();
					settings.Indent = true;
					settings.CheckCharacters = true;

					using (var writer = XmlWriter.Create(tempHtml.Path, settings))
					{
						dom.WriteContentTo(writer);
						writer.Close();
					}
					var sizeAndOrientation = SizeAndOrientation.FromDom(_bookSelection.CurrentSelection.RawDom);
					if (doWorkEventArgs.Cancel)
						return;

					_pdfMaker.MakePdf(tempHtml.Path, PdfFilePath, sizeAndOrientation.PageSizeName, sizeAndOrientation.IsLandScape, _bookSelection.CurrentSelection.GetDefaultBookletLayout(), BookletPortion, doWorkEventArgs);
				}
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "There was a problem creating a PDF from this book.");
				SetDisplayMode(DisplayModes.NoBook);
				return;
			}
		}

		private string GetPdfPath(string fileName)
		{
			string path=null;

			for (int i = 0; i < 100; i++)
			{
				path = Path.Combine(Path.GetTempPath(), fileName + i + ".pdf");
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
			if(View!=null)
				View.SetDisplayMode(displayMode);
		}

		public void Dispose()
		{
			if(File.Exists(PdfFilePath))
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

		public BookletPortions BookletPortion
		{ get; set; }

	}
}
