using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Palaso.IO;

namespace Bloom.Publish
{
	public class PublishModel : IDisposable
	{
		public BookSelection BookSelection { get; set; }

		public string PdfFilePath{ get; private set;}

		private readonly BookSelection _bookSelection;
		private Book _currentlyLoadedBook;

		public PublishModel(BookSelection bookSelection)
		{
			BookSelection = bookSelection;
			_bookSelection = bookSelection;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
		}

		private PublishView _view;
		public PublishView View
		{
			get { return _view; }
			set
			{
				_view = value;
				//_view.VisibleChanged +=new EventHandler(OnVisibleChanged);
			}
		}

		private void OnVisibleChanged(object sender, EventArgs e)
		{

		}

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			if (_currentlyLoadedBook != BookSelection.CurrentSelection && _view.Visible)
			{
				LoadBook();
			}
		}

		public enum DisplayModes
		{
			NoBook, Working, ShowPdf
		}
		public void LoadBook()
		{
			_currentlyLoadedBook = BookSelection.CurrentSelection;

			PdfFilePath =  GetPdfPath(Path.GetFileName(_currentlyLoadedBook.FolderPath));
			try
			{
				//_selectionChangedWhileWeWereInvisible = false;

				if (_bookSelection.CurrentSelection == null)
				{
					SetDisplayMode(DisplayModes.NoBook);
					return;
				}

				SetDisplayMode(DisplayModes.Working);

				var path = _bookSelection.CurrentSelection.GetHtmlFileForPrintingWholeBook();
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
							Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
								"Could not find a file that should have been installed with Bloom: " + exePath);
							return;
						}
					}
					ProcessStartInfo info = new ProcessStartInfo(exePath,
																 string.Format(
																	 "--print-media-type --page-width 14.5cm --page-height 21cm  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm --disable-smart-shrinking {0} {1}",
																	 Path.GetFileName(path), PdfFilePath));
					info.WorkingDirectory = Path.GetDirectoryName(path);
					info.ErrorDialog = true;
					info.WindowStyle = ProcessWindowStyle.Hidden;



					//_browser.Visible = false;
					var proc = System.Diagnostics.Process.Start(info);
					proc.WaitForExit(20 * 1000);
					if (!proc.HasExited)
					{
						proc.Kill();
						PdfFilePath = Path.GetTempFileName(); //change it so we aren't competing
						File.WriteAllText(PdfFilePath, "<html><body>Making the PDF took too long</body></html>");
					}
				}
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "There was a problem creating a PDF from this book.");
				return;
			}
			finally
			{
			}
			SetDisplayMode(DisplayModes.ShowPdf);
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
	}
}
