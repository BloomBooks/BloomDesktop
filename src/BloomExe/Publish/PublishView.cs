using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Palaso.Reporting;


namespace Bloom.Publish
{
	public partial class PublishView : UserControl, IBloomTabArea
	{
		private readonly PublishModel _model;
		private bool _activated;

		public delegate PublishView Factory();//autofac uses this

		public PublishView(PublishModel model,
			SelectedTabChangedEvent selectedTabChangedEvent)
		{
				InitializeComponent();
				adobeReaderProblemControl1.Visible = false;
				Controls.Remove(_saveButton);//our parent will retrieve this
				Controls.Remove(_printButton);//our parent will retrieve this


			if(this.DesignMode)
				return;

			_model = model;
			_model.View = this;

			_makePdfBackgroundWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(_makePdfBackgroundWorker_RunWorkerCompleted);

			//NB: just triggering off "VisibilityChanged" was unreliable. So now we trigger
			//off the tab itself changing, either to us or away from us.
			selectedTabChangedEvent.Subscribe(c=>
												{
													if (c.To == this)
													{
														Activate();
													}
													else if (c.To!=this && _makePdfBackgroundWorker.IsBusy)
														_makePdfBackgroundWorker.CancelAsync();
												});

			//TODO: find a way to call this just once, at the right time:

			//			UsageReporter.SendNavigationNotice("Publish");


		}


		private void Activate()
		{
			if (_makePdfBackgroundWorker.IsBusy)
				return;

//			if(_adobeReader==null)
//			{
//				try
//				{
//					AddAdobeReader();
//				}
//				catch (Exception e)
//				{
//					_adobeReader = null;
//					_printButton.Enabled = false;
//					_workingIndicatorGif.Visible = false;
//					adobeReaderProblemControl1.Visible = true;
//				}
//			}



			_activated = true;

			_model.BookletPortion = PublishModel.BookletPortions.BookletPages;
			MakeBooklet();
		}



		public Control TopBarControl
		{
			get { return _topBarPanel; }
		}

		void _makePdfBackgroundWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
		{
			SetDisplayMode(PublishModel.DisplayModes.ShowPdf);
			UpdateDisplay();
			if(_model.BookletPortion != (PublishModel.BookletPortions) e.Result )
			{
				MakeBooklet();
			}
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			//_loadTimer.Enabled = true;
			//UpdateEditButtons();
		}

		private void UpdateDisplay()
		{
			if(_model==null)
				return;

			_coverRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletCover;
			_bodyRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletPages;
			_noBookletRadio.Checked = _model.BookletPortion== PublishModel.BookletPortions.None;

			}

		public void SetDisplayMode(PublishModel.DisplayModes displayMode)
		{
			switch (displayMode)
			{
				case PublishModel.DisplayModes.NoBook:
					_printButton.Enabled = _saveButton.Enabled = false;
					Cursor = Cursors.Default;
					break;
				case PublishModel.DisplayModes.Working:
					_printButton.Enabled = _saveButton.Enabled = false;
					_workingIndicator.Cursor = Cursors.WaitCursor;
					Cursor = Cursors.WaitCursor;
					_workingIndicator.Visible = true;
					break;
				case PublishModel.DisplayModes.ShowPdf:
					if (File.Exists(_model.PdfFilePath))
					{
						_saveButton.Enabled = true;
						_workingIndicator.Visible = false;
						Cursor = Cursors.Default;
						_printButton.Enabled = _adobeReaderControl.ShowPdf(_model.PdfFilePath);
					}
					break;
			}
		}


		private void _bookletRadio_CheckedChanged(object sender, EventArgs e)
		{
			if (!_activated)
				return;

			var old = _model.BookletPortion;
			SetModelFromRadioButtons();
			if (old == _model.BookletPortion)
				return;

			if(_makePdfBackgroundWorker.IsBusy)
			{
				_makePdfBackgroundWorker.CancelAsync();
			}
			else
				MakeBooklet();
		}

		private void SetModelFromRadioButtons()
		{
			if (_noBookletRadio.Checked)
				_model.BookletPortion = PublishModel.BookletPortions.None;
			else if (_coverRadio.Checked)
				_model.BookletPortion = PublishModel.BookletPortions.BookletCover;
			else
				_model.BookletPortion = PublishModel.BookletPortions.BookletPages;
		}

		public void MakeBooklet()
		{
			SetDisplayMode(PublishModel.DisplayModes.Working);
			_makePdfBackgroundWorker.RunWorkerAsync();
		}

		private void OnPrint_Click(object sender, EventArgs e)
		{
			_adobeReaderControl.Print();
			UsageReporter.SendNavigationNotice("Print");
			Logger.WriteEvent("Calling Print on Adobe Reader");
		}

		private void _makePdfBackgroundWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			e.Result = _model.BookletPortion; //record what our parameters were, so that if the user changes the request and we cancel, we can detect that we need to re-run
			_model.LoadBook(e);
		}

		private void OnSave_Click(object sender, EventArgs e)
		{
			_model.Save();
		}
		public string HelpTopicUrl
		{
			get { return "/Tasks/Publish_tasks/Publish_tasks_overview.htm"; }
		}

		private void pictureBox1_Click(object sender, EventArgs e)
		{

		}
	}
}