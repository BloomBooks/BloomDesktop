using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using BloomTemp;

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


				using (var tempHtml = _bookSelection.CurrentSelection.GetHtmlTempFileForPrintingWholeBook())
				{

					ProcessStartInfo info = new ProcessStartInfo("wkhtmltopdf.exe",
																 string.Format(
																	 "--print-media-type --page-width 14.5cm --page-height 21cm  --margin-bottom 0mm  --margin-top 0mm  --margin-left 0mm  --margin-right 0mm --disable-smart-shrinking {0} {1}",
																	 Path.GetFileName(tempHtml.Path), tempFile));
					info.WorkingDirectory = Path.GetDirectoryName(tempHtml.Path);
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
				if(e.Message.Contains("not find"))
				{
					MessageBox.Show("To get pdfs, wkhtmltopdf must be installed and in the PATH environment variable.");
				}
				else
				{
					MessageBox.Show("There was a problem generating the pdf: "+Environment.NewLine+e.Message);
				}
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