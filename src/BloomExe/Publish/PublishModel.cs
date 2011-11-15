using System;
using System.IO;
using System.Xml;
using Bloom.Book;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom.Publish
{
	/// <summary>
	/// Contains the logic behind the PublishView control, which involves creating a pdf from the html book and
	/// letting you print it.
	/// </summary>
	public class PublishModel : IDisposable
	{
		public BookSelection BookSelection { get; set; }

		public string PdfFilePath{ get; private set;}
		public enum DisplayModes
		{
			NoBook, Working, ShowPdf
		}

		public enum BookletStyleChoices
		{
			None,
			BookletCover,
			BookletPages
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
			BookletStyle = BookletStyleChoices.BookletPages;
		}

		public PublishView View { get; set; }


		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			if (_currentlyLoadedBook != BookSelection.CurrentSelection && View.Visible)
			{
				LoadBook();
			}
		}


		public void LoadBook()
		{
			_currentlyLoadedBook = BookSelection.CurrentSelection;

			try
			{
				if (_bookSelection.CurrentSelection == null)
				{
					SetDisplayMode(DisplayModes.NoBook);
					return;
				}

				SetDisplayMode(DisplayModes.Working);
				PdfFilePath = GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));

				XmlDocument dom =   _bookSelection.CurrentSelection.GetDomForPrinting(BookletStyle);

				//wkhtmltopdf can't handle file://
				dom.InnerXml = dom.InnerXml.Replace("file://", "");
				MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(dom);

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
					_pdfMaker.MakePdf(tempHtml.Path, PdfFilePath, _bookSelection.CurrentSelection.GetPageSizeName(), _bookSelection.CurrentSelection.GetIsLandscape(), BookletStyle);
				}
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "There was a problem creating a PDF from this book.");
				SetDisplayMode(DisplayModes.NoBook);
				return;
			}
			SetDisplayMode(DisplayModes.ShowPdf);
		}

		private void MakeSafeForBrowserWhichDoesntUnderstandXmlSingleElements(XmlDocument dom)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//textarea"))
			{
				if (string.IsNullOrEmpty(node.InnerText))
				{
					node.InnerText = " ";
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//script"))
			{
				if (string.IsNullOrEmpty(node.InnerText))
				{
					node.InnerText = " ";
				}
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

		public BookletStyleChoices BookletStyle
		{ get; private set; }

		public void SetBookletStyle(BookletStyleChoices booklet)
		{
			if (BookletStyle == booklet)
				return;

			BookletStyle = booklet;
			LoadBook();
		}
	}
}
