using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.WebLibraryIntegration;
using DesktopAnalytics;
using Palaso.IO;
using Palaso.Reporting;


namespace Bloom.Publish
{
	public partial class PublishView : UserControl, IBloomTabArea
    {		
		private readonly PublishModel _model;
	    private readonly ComposablePartCatalog _extensionCatalog;
	    private bool _activated;
		private BloomLibraryPublishControl _libraryPublishControl;
		private BookTransfer _bookTransferrer;
		private LoginDialog _loginDialog;

		public delegate PublishView Factory();//autofac uses this

        public PublishView(PublishModel model,
			SelectedTabChangedEvent selectedTabChangedEvent, BookTransfer bookTransferrer, LoginDialog login)
        {
	        _bookTransferrer = bookTransferrer;
	        _loginDialog = login;

			InitializeComponent();
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
													else if (c.To!=this && IsMakingPdf)
														_makePdfBackgroundWorker.CancelAsync();
			                                  	});

			//TODO: find a way to call this just once, at the right time:

			//			DeskAnalytics.Track("Publish");

//#if DEBUG
//        	var linkLabel = new LinkLabel() {Text = "DEBUG"};
//			linkLabel.Click+=new EventHandler((x,y)=>_model.DebugCurrentPDFLayout());
//        	tableLayoutPanel1.Controls.Add(linkLabel);
//#endif

			_menusToolStrip.Renderer = new EditingView.FixedToolStripRenderer();
        }


		private void Activate()
		{
			Logger.WriteEvent("Entered Publish Tab");
			if (IsMakingPdf)
				return;

			_activated = true;

//			_model.BookletPortion = PublishModel.BookletPortions.BookletPages;


			_model.RefreshValuesUponActivation();

            //reload items from extension(s), as they may differ by book (e.g. if the extension comes from the template of the book)
            var toolStripItemCollection = new List<ToolStripItem>(from ToolStripItem x in _contextMenuStrip.Items select x);
            foreach (ToolStripItem item in toolStripItemCollection)
		    {
		        if (item.Tag == "extension")
                    _contextMenuStrip.Items.Remove(item);
		    }
            foreach (var item in _model.GetExtensionMenuItems())
            {
                item.Tag = "extension";
                _contextMenuStrip.Items.Add(item);
            }

			UpdateDisplay();
			MakeBooklet();
		}

		internal bool IsMakingPdf
		{
			get { return _makePdfBackgroundWorker.IsBusy; }
		}


		public Control TopBarControl
    	{
    		get { return _topBarPanel; }
    	}

    	void _makePdfBackgroundWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
    	{
    		_model.PdfGenerationSucceeded = false;
			if(e.Result is Exception)
			{
				var error = e.Result as Exception;
				if(error is ApplicationException)
				{
					//For common exceptions, we catch them earlier (in the worker thread) and give a more helpful message
					//note, we don't want to include the original, as it leads to people sending in reports we don't
					//actually want to see. E.g., we don't want a bug report just because they didn't have Acrobat
					//installed, or they had the PDF open in Word, or something like that.
					ErrorReport.NotifyUserOfProblem(error.Message);
				}
				else // for others, just give a generic message and include the original exception in the message
				{
					ErrorReport.NotifyUserOfProblem(error, "Sorry, Bloom had a problem creating the PDF.");
				}
				// We CAN upload even without a preview.
				_model.DisplayMode = (_cloudRadio.Checked ? PublishModel.DisplayModes.Upload : PublishModel.DisplayModes.NoBook);
				UpdateDisplay();
				return;
			}
			_model.PdfGenerationSucceeded = true; // should be the only place this is set, when we generated successfully.
			_model.DisplayMode = (_cloudRadio.Checked ? PublishModel.DisplayModes.Upload : PublishModel.DisplayModes.ShowPdf);
			UpdateDisplay();
			if(_model.BookletPortion != (PublishModel.BookletPortions) e.Result )
			{
				MakeBooklet();
			}
		}



        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
        }

		private void UpdateDisplay()
		{
			if (_model == null || _model.BookSelection.CurrentSelection==null)
				return;

			_layoutChoices.Text = _model.PageLayout.ToString();

			_coverRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletCover && !_model.UploadMode;
			_bodyRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletPages && !_model.UploadMode;
			_noBookletRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.AllPagesNoBooklet && !_model.UploadMode;
			_cloudRadio.Checked = _model.UploadMode;
			// No reason to update from model...we only change the model when the user changes the check box,
			// or when uploading...and we do NOT want to update the check box when uploading temporarily changes the model.
			//_showCropMarks.Checked = _model.ShowCropMarks;


			var layoutChoices = _model.BookSelection.CurrentSelection.GetLayoutChoices();
			_layoutChoices.DropDownItems.Clear();
//			_layoutChoices.Items.AddRange(layoutChoices.ToArray());
//			_layoutChoices.SelectedText = _model.BookSelection.CurrentSelection.GetLayout().ToString();
			foreach (var l in layoutChoices)
			{
				ToolStripMenuItem item = (ToolStripMenuItem)_layoutChoices.DropDownItems.Add(l.ToString());
				item.Tag = l;
				item.Text = l.ToString();
				item.Checked = l.ToString() == _model.PageLayout.ToString();
				item.CheckOnClick = true;
				item.Click += new EventHandler(OnLayoutChosen);
			}
			_layoutChoices.Text = _model.PageLayout.ToString();
		}

		private void OnLayoutChosen(object sender, EventArgs e)
		{
			var item = (ToolStripMenuItem)sender;
			_model.PageLayout = ((Layout)item.Tag);
			_layoutChoices.Text = _model.PageLayout.ToString();
			ControlsChanged();
		}

		public void SetDisplayMode(PublishModel.DisplayModes displayMode)
        {
			if (displayMode != PublishModel.DisplayModes.Upload && _libraryPublishControl != null)
			{
				Controls.Remove(_libraryPublishControl);
				_libraryPublishControl = null;
				_adobeReaderControl.Visible = true;
			}
            switch (displayMode)
            {
                case PublishModel.DisplayModes.NoBook:
                    _printButton.Enabled = _saveButton.Enabled = false;
					Cursor = Cursors.Default;
					_workingIndicator.Visible = false;
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
				case PublishModel.DisplayModes.Upload:
	            {
		            _workingIndicator.Visible = false; // If we haven't finished creating the PDF, we will indicate that in the progress window.
		            _saveButton.Enabled = _printButton.Enabled = false; // Can't print or save in this mode...wouldn't be obvious what would be saved.
		            _adobeReaderControl.Visible = false; // We will replace it with another control.
					Cursor = Cursors.Default;
					if (_libraryPublishControl == null)
		            {
			            _libraryPublishControl = new BloomLibraryPublishControl(this, _bookTransferrer, _loginDialog, _model.BookSelection.CurrentSelection);
			            _libraryPublishControl.SetBounds(_adobeReaderControl.Left, _adobeReaderControl.Top,
				            _adobeReaderControl.Width, _adobeReaderControl.Height);
			            _libraryPublishControl.Anchor = _adobeReaderControl.Anchor;
			            Controls.Add(_libraryPublishControl);
		            }
		            break;
	            }
            }
        }


        private void OnBookletRadioChanged(object sender, EventArgs e)
        {
			if (!_activated)
				return;

        	var oldPortion = _model.BookletPortion;
	        var oldCrop = _model.ShowCropMarks; // changing to or from cloud radio CAN change this.
			SetModelFromButtons();
	        if (oldPortion == _model.BookletPortion && oldCrop == _model.ShowCropMarks)
	        {
				// no changes detected
		        if (_cloudRadio.Checked)
		        {
					_model.DisplayMode = PublishModel.DisplayModes.Upload;
		        }
				else if (_model.DisplayMode == PublishModel.DisplayModes.Upload)
				{
					// no change because the PREVIOUS button was the cloud one. Need to restore the appropriate
					// non-cloud display
					_model.DisplayMode = _model.PdfGenerationSucceeded
						? PublishModel.DisplayModes.ShowPdf
						: PublishModel.DisplayModes.NoBook;
				}
		        return;
	        }

	        ControlsChanged();
        }

		private void OnShowCropMarks_CheckedChanged(object sender, EventArgs e)
		{
			if (!_activated)
				return;

			var oldSetting = _model.ShowCropMarks;
			SetModelFromButtons();
			if (oldSetting == _model.ShowCropMarks)
				return; // no changes detected

			ControlsChanged();
		}

		private void ControlsChanged()
		{
			if (IsMakingPdf)
			{
				_makePdfBackgroundWorker.CancelAsync();
			}
			else
				MakeBooklet();
		}

		private void SetModelFromButtons()
		{
			if (_coverRadio.Checked)
				_model.BookletPortion = PublishModel.BookletPortions.BookletCover;
			else if (_bodyRadio.Checked)
				_model.BookletPortion = PublishModel.BookletPortions.BookletPages;
			else // no booklet radio, or cloud radio (We want to upload the all-pages version.)
				_model.BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet;
			_model.UploadMode = _cloudRadio.Checked;
			_model.ShowCropMarks = _showCropMarks.Checked && !_cloudRadio.Checked; // don't want crop-marks on upload PDF
		}

		internal string PdfPreviewPath { get { return _model.PdfFilePath; } }

    	public void MakeBooklet()
    	{
			if (IsMakingPdf)
			{
				// Can't start again until this one finishes
				return;
			}
    		_model.PdfGenerationSucceeded = false; // and so it stays unless we generate it successfully.

    		SetDisplayMode(PublishModel.DisplayModes.Working);
    		_makePdfBackgroundWorker.RunWorkerAsync();
    	}

		private void OnPrint_Click(object sender, EventArgs e)
		{

			_adobeReaderControl.Print();
			Logger.WriteEvent("Calling Print on Adobe Reader");
			Analytics.Track("Print PDF");
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

		private void _openinBrowserMenuItem_Click(object sender, EventArgs e)
		{
			_model.DebugCurrentPDFLayout();
		}

		private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
		{

		}
    }
}
