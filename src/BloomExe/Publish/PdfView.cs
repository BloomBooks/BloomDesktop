using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using BloomTemp;
using Palaso.IO;

namespace Bloom.Publish
{
	public partial class PdfView : UserControl
	{
		public BookSelection BookSelection { get; set; }
		private readonly BookSelection _bookSelection;

		public delegate PdfView Factory();//autofac uses this

		private bool _selectionChangedWhileWeWereInvisible;
		private Book _currentlyLoadedBook;

		public PdfView(BookSelection bookSelection)
		{
			BookSelection = bookSelection;
			InitializeComponent();
			if(this.DesignMode)
				return;

			_bookSelection = bookSelection;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			_loadTimer.Enabled = true;
		}


		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			if(_currentlyLoadedBook != BookSelection.CurrentSelection)
			{
				LoadBook();
			}
		}


		private void LoadBook()
		{
			if (!Visible)
			{
				_selectionChangedWhileWeWereInvisible = true;
				return;
			}

			_currentlyLoadedBook = BookSelection.CurrentSelection;

			var pleaseWaitNotice = new PleaseWait();
			pleaseWaitNotice.Show(this);

			string tempFile = Path.GetTempFileName() + ".pdf";
			try
			{
				_selectionChangedWhileWeWereInvisible=false;

				if(_bookSelection.CurrentSelection ==null)
				{
					_browser.Navigate("about:blank", false);
					return;
				}


				var path = _bookSelection.CurrentSelection.GetHtmlFileForPrintingWholeBook();
				{
					var exePath=Path.Combine(FileLocator.DirectoryOfTheApplicationExecutable,"wkhtmltopdf");
					exePath = Path.Combine(exePath, "wkhtmltopdf.exe");
					if(!File.Exists(exePath))
					{
						//if this is a programmer, it should be in the lib directory
						exePath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution, Path.Combine("lib","wkhtmltopdf"));
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
																	 Path.GetFileName(path), tempFile));
					info.WorkingDirectory = Path.GetDirectoryName(path);
					info.ErrorDialog = true;
					info.WindowStyle = ProcessWindowStyle.Hidden;


					Cursor = Cursors.WaitCursor;
					_browser.Visible = false;
					var proc = System.Diagnostics.Process.Start(info);
					proc.WaitForExit(20*1000);
					if (!proc.HasExited)
					{
						proc.Kill();
						tempFile = Path.GetTempFileName(); //change it so we aren't competing
						File.WriteAllText(tempFile, "<html><body>Making the PDF took too long</body></html>");
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
				Cursor = Cursors.Default;
				_browser.Visible = true;
				pleaseWaitNotice.Dispose();
			}

			if (File.Exists(tempFile))
			{
				_browser.Navigate(tempFile, true);
			}
		}

		private void _browser_VisibleChanged(object sender, EventArgs e)
		{
			if(Visible && _selectionChangedWhileWeWereInvisible)
				LoadBook();
		}

		private void _loadTimer_Tick(object sender, EventArgs e)
		{
			_loadTimer.Enabled = false;
			if (_currentlyLoadedBook != BookSelection.CurrentSelection)
			{
				LoadBook();
			}
		}
	}
}