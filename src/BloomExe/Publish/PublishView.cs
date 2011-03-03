using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml;


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
					Cursor = Cursors.WaitCursor;
					_browser.Navigate(_pleaseWaitPage);
					break;
				case PublishModel.DisplayModes.ShowPdf:
					Cursor = Cursors.Default;
					if (File.Exists(_model.PdfFilePath))
					{
						_browser.Navigate(_model.PdfFilePath, true);
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
		}
	}
}