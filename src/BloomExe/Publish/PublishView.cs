using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Palaso.Reporting;


namespace Bloom.Publish
{
	public partial class PublishView : UserControl
	{

		private readonly PublishModel _model;

		public delegate PublishView Factory();//autofac uses this

		private bool _selectionChangedWhileWeWereInvisible;
		private XmlDocument _pleaseWaitPage;

		public PublishView(PublishModel model)
		{
			InitializeComponent();
			if(this.DesignMode)
				return;

			_model = model;
			_model.View = this;

			_pleaseWaitPage = new XmlDocument();
			_pleaseWaitPage.InnerXml = "<html><body>Please Wait</body></html>";
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			_loadTimer.Enabled = true;
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			_coverRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletCover;
			_bodyRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletPages;
			_noBookletRadio.Checked = _model.BookletPortion== PublishModel.BookletPortions.None;
		}

		public void SetDisplayMode(PublishModel.DisplayModes displayMode)
		{
			switch (displayMode)
			{
				case PublishModel.DisplayModes.NoBook:
					Cursor = Cursors.Default;
					_browser.Navigate("about:blank", false);
					break;
				case PublishModel.DisplayModes.Working:
					_browser.Cursor = Cursors.WaitCursor;
					Cursor = Cursors.WaitCursor;
					_browser.Navigate(_pleaseWaitPage);
					break;
				case PublishModel.DisplayModes.ShowPdf:
					Cursor = Cursors.Default;
					if (File.Exists(_model.PdfFilePath))
					{
						//http://wwwimages.adobe.com/www.adobe.com/content/dam/Adobe/en/devnet/acrobat/pdfs/pdf_open_parameters.pdf
						var path = _model.PdfFilePath;
						//TODO: on delete, this file:\\ gives an error
						//var path = "file:\\" + _model.PdfFilePath + "#view=Fit&navpanes=0&pagemode=thumbs&toolbar=1";
						_browser.Navigate(path, true);
					}

					break;
			}
		}

		private void _browser_VisibleChanged(object sender, EventArgs e)
		{
			if (Visible)
			{
				_browser.Navigate(_pleaseWaitPage);
				_loadTimer.Enabled = true;
				UsageReporter.SendNavigationNotice("Publish");
			}
		}

		private void _loadTimer_Tick(object sender, EventArgs e)
		{
			_loadTimer.Enabled = false;
//            if (_currentlyLoadedBook != BookSelection.CurrentSelection)
//            {
//                LoadBook();
//            }
			if (!Visible)
			{
			  //  _selectionChangedWhileWeWereInvisible = true;
				return;
			}
			_model.LoadBook();
			UpdateDisplay();
		}

		private void _bookletRadio_CheckedChanged(object sender, EventArgs e)
		{
			if (_noBookletRadio.Checked)
				_model.SetBookletStyle(PublishModel.BookletPortions.None);
			else if (_coverRadio.Checked)
				_model.SetBookletStyle(PublishModel.BookletPortions.BookletCover);
			else
				_model.SetBookletStyle(PublishModel.BookletPortions.BookletPages);
			UpdateDisplay();
		}
	}
}