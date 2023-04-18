using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using SIL.Reporting;
using SIL.IO;
using System.Drawing;
using Bloom.Api;
using Bloom.Publish.BloomPub;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.Epub;
using Bloom.Publish.PDF;
using Bloom.Publish.Video;
using SIL.Progress;
using Bloom.web.controllers;

namespace Bloom.Publish
{
	public partial class PublishView : UserControl, IBloomTabArea
	{
		public readonly PublishModel _model;
		private bool _activated;
		private BookUpload _bookTransferrer;
		private PictureBox _previewBox;
		private HtmlPublishPanel _htmlControl;
		private PublishToBloomPubApi _publishApi;
		private PublishAudioVideoAPI _publishToVideoApi;
		private PublishEpubApi _publishEpubApi;
		private BloomWebSocketServer _webSocketServer;
		private readonly string _cantPublishPageWithPlaceholder;
		private readonly string _cantPublishProblemWithPlaceholder;


		public delegate PublishView Factory();//autofac uses this

		public PublishView(PublishModel model,
			SelectedTabChangedEvent selectedTabChangedEvent, LocalizationChangedEvent localizationChangedEvent, BookUpload bookTransferrer,			PublishToBloomPubApi publishApi, PublishEpubApi publishEpubApi, BloomWebSocketServer webSocketServer,
			PublishAudioVideoAPI publishToVideoApi)
		{
			_bookTransferrer = bookTransferrer;
			_publishApi = publishApi;
			_publishEpubApi = publishEpubApi;
			_webSocketServer = webSocketServer;
			_publishToVideoApi = publishToVideoApi;

			InitializeComponent();

			if (this.DesignMode)
				return;

			_model = model;
			_model.View = this;

			_cantPublishPageWithPlaceholder = _publishReqEntOverlayPage.Text;
			_cantPublishProblemWithPlaceholder = _publishReqEntProblem.Text;
			_makePdfBackgroundWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(_makePdfBackgroundWorker_RunWorkerCompleted);

			// BL-625: With mono, if a RadioButton group has its AutoCheck properties set to true, the default RadioButton.OnEnter
			//         event checks to make sure one of the RadioButtons is checked. If none are checked, the one the mouse pointer
			//         is over is checked, causing the CheckChanged event to fire.
			if (SIL.PlatformUtilities.Platform.IsMono)
				SetAutoCheck(false);

			//NB: just triggering off "VisibilityChanged" was unreliable. So now we trigger
			//off the tab itself changing, either to us or away from us.
			selectedTabChangedEvent.Subscribe(c=>
												{
													if (c.To == this)
													{
														Activate();
													}
													else if (c.To != this)
													{
														Deactivate();
													}
												});

			//TODO: find a way to call this just once, at the right time:

			//			DeskAnalytics.Track("Publish");

//#if DEBUG
//        	var linkLabel = new LinkLabel() {Text = "DEBUG"};
//			linkLabel.Click+=new EventHandler((x,y)=>_model.DebugCurrentPDFLayout());
//        	tableLayoutPanel1.Controls.Add(linkLabel);
//#endif
			tableLayoutPanel1.BackColor = Palette.GeneralBackground;

			SetupLocalization();
			localizationChangedEvent.Subscribe(o =>
			{
				SetupLocalization();
			});

			// Make this extra box available to show when wanted.
			_previewBox = new PictureBox();
			_previewBox.Visible = false;
			Controls.Add(_previewBox);
			_previewBox.BringToFront();
		}

		public void SetStateOfNonUploadRadios(bool enable)
		{
			_epubRadio.Enabled = enable;
			_pdfPrintRadio.Enabled = enable;
			_bloomPUBRadio.Enabled = enable;
			_recordVideoRadio.Enabled = enable;
		}

		private void Deactivate()
		{
			if (IsMakingPdf)
				_makePdfBackgroundWorker.CancelAsync();
			_publishEpubApi?.AbortMakingEpub();
			_publishToVideoApi.AbortMakingVideo();
			// This allows various cleanup of controls which we won't use again, since we
			// always switch to this state when we reactivate the view.
			// In particular, it is part of the solution to BL-4901 that the HtmlPublishPanel,
			// if it is active, is removed (hence deactivated) and disposed.
			SetDisplayMode(PublishModel.DisplayModes.WaitForUserToChooseSomething);
			// This is only supposed to be active in one mode of PublishView.
			if (_htmlControl != null)
			{
				Controls.Remove(_htmlControl);
				_htmlControl.Dispose();
				_htmlControl = null;
			}
			// TODO-WV2: Can we clear the cache for WV2? Do we need to?
			PublishHelper.Cancel();
			PublishHelper.InPublishTab = false;
		}

		private void SetAutoCheck(bool autoCheck)
		{
			if (_pdfPrintRadio.AutoCheck == autoCheck)
				return;

			_pdfPrintRadio.AutoCheck = autoCheck;
			_uploadRadio.AutoCheck = autoCheck;
			_epubRadio.AutoCheck = autoCheck;
			_bloomPUBRadio.AutoCheck = autoCheck;
			_recordVideoRadio.AutoCheck = autoCheck;
		}

		private void SetupLocalization()
		{
			LocalizeSuperToolTip(_pdfPrintRadio, "PublishTab.PdfPrint.Button");
			LocalizeSuperToolTip(_uploadRadio, "PublishTab.ButtonThatShowsUploadForm");
			LocalizeSuperToolTip(_bloomPUBRadio, "PublishTab.bloomPUBButton");
			LocalizeSuperToolTip(_recordVideoRadio, "PublishTab.RecordVideoButton");
		}

		// Used by LocalizeSuperToolTip to remember original English keys
		Dictionary<Control, string> _originalSuperToolTips = new Dictionary<Control, string>();

		private void LocalizeSuperToolTip(Control controlThatHasSuperTooltipAttached, string l10nIdOfControl)
		{
			var tooltipinfo = _superToolTip.GetSuperStuff(controlThatHasSuperTooltipAttached);
			string english;
			if (!_originalSuperToolTips.TryGetValue(controlThatHasSuperTooltipAttached, out english))
			{
				english = tooltipinfo.SuperToolTipInfo.BodyText;
				_originalSuperToolTips[controlThatHasSuperTooltipAttached] = english;
			}
			//enhance: GetLocalizingId didn't work: var l10nidForTooltip = _L10NSharpExtender.GetLocalizingId(controlThatHasSuperTooltipAttached) + ".tooltip";
			var l10nidForTooltip = l10nIdOfControl + "-tooltip";
			tooltipinfo.SuperToolTipInfo.BodyText = LocalizationManager.GetDynamicString("Bloom", l10nidForTooltip, english);
			_superToolTip.SetSuperStuff(controlThatHasSuperTooltipAttached, tooltipinfo);
		}


		private void Activate()
		{
			_activated = false;
			PublishHelper.InPublishTab = true;

			Logger.WriteEvent("Entered Publish Tab");
			if (IsMakingPdf)
				return;

			_model.UpdateModelUponActivation();

			//reload items from extension(s), as they may differ by book (e.g. if the extension comes from the template of the book)
			var toolStripItemCollection = new List<ToolStripItem>(from ToolStripItem x in _contextMenuStrip.Items select x);
			foreach (ToolStripItem item in toolStripItemCollection)
			{
				if (((string)item.Tag) == "extension")
					_contextMenuStrip.Items.Remove(item);
			}
			foreach (var item in _model.GetExtensionMenuItems())
			{
				item.Tag = "extension";
				_contextMenuStrip.Items.Add(item);
			}

			// We choose not to remember the last state this tab might have been in.
			// Also since we don't know if the pdf is out of date, we assume it is, and don't show the prior pdf.
			// SetModelFromButtons takes care of both of these things for the model
			ClearRadioButtons();
			SetModelFromButtons();
			_model.DisplayMode = _model.CanPublish ? PublishModel.DisplayModes.WaitForUserToChooseSomething : PublishModel.DisplayModes.NotPublishable;

			UpdateDisplay();

			_activated = true;
		}

		private void ClearRadioButtons()
		{
			_pdfPrintRadio.Checked = _uploadRadio.Checked = _epubRadio.Checked = _bloomPUBRadio.Checked = _recordVideoRadio.Checked = false;
		}

		internal bool IsMakingPdf
		{
			get { return _makePdfBackgroundWorker.IsBusy; }
		}


		public Control TopBarControl
		{
			get { return _topBarPanel; }
		}

		public int WidthToReserveForTopBarControl => TopBarControl.Width;

		public void PlaceTopBarControl()
		{
			_topBarPanel.Dock = DockStyle.Left;
		}

		public Bitmap ToolStripBackground { get; set; }
		public bool CanChangeTo()
		{
			return true;
		}

		void _makePdfBackgroundWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
		{
			_model.PdfGenerationSucceeded = false;
			if (!e.Cancelled)
			{
				if (e.Result is Exception)
				{
					var error = e.Result as Exception;
					if (error is ApplicationException)
					{
						//For common exceptions, we catch them earlier (in the worker thread) and give a more helpful message
						//note, we don't want to include the original, as it leads to people sending in reports we don't
						//actually want to see. E.g., we don't want a bug report just because they didn't have Acrobat
						//installed, or they had the PDF open in Word, or something like that.
						ErrorReport.NotifyUserOfProblem(error.Message);
					}
					else if (error is PdfMaker.MakingPdfFailedException)
					{
						// Ignore this error here.  It will be reported elsewhere if desired.
					}
					else if (error is FileNotFoundException && ((FileNotFoundException) error).FileName == "BloomPdfMaker.exe")
					{
						ErrorReport.NotifyUserOfProblem(error, error.Message);
					}
					else if (error is OutOfMemoryException)
					{
						// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5467.
						var fmt = LocalizationManager.GetString("PublishTab.PdfMaker.OutOfMemory",
							"Bloom ran out of memory while making the PDF. See {0}this article{1} for some suggestions to try.",
							"{0} and {1} are HTML link markup.  You can think of them as fancy quotation marks.");
						var msg = String.Format(fmt, "<a href='https://community.software.sil.org/t/solving-memory-problems-while-printing/500'>", "</a>");
						using (var f = new MiscUI.HtmlLinkDialog(msg))
						{
							f.ShowDialog();
						}
					}
					else // for others, just give a generic message and include the original exception in the message
					{
						ErrorReport.NotifyUserOfProblem(error, "Sorry, Bloom had a problem creating the PDF.");
					}
					UpdateDisplayMode();
					return;
				}
				_model.PdfGenerationSucceeded = true;
					// should be the only place this is set, when we generated successfully.
				UpdateDisplayMode();
			}
		}

		private void UpdateDisplayMode()
		{
			if (IsHandleCreated) // May not be when bulk uploading
			{
				if (_uploadRadio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.Upload;
				else if (_epubRadio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.EPUB;
				else if (_bloomPUBRadio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.BloomPUB;
				else if (_recordVideoRadio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.AudioVideo;
				else
					_model.DisplayMode = PublishModel.DisplayModes.WaitForUserToChooseSomething;
				Invoke((Action) (UpdateDisplay));
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

			_pdfPrintRadio.Checked = _model.PdfPrintMode;
			_uploadRadio.Checked = _model.UploadMode;
			_epubRadio.Checked = _model.EpubMode;

			_pdfPrintRadio.Enabled = _model.AllowPdf;
			_uploadRadio.Enabled = _model.AllowUpload;
			_openinBrowserMenuItem.Enabled = _openPDF.Enabled = _model.PdfGenerationSucceeded;
			_bloomPUBRadio.Enabled = _model.AllowBloomPub;
			_epubRadio.Enabled = _model.AllowEPUB;

			// No reason to update from model...we only change the model when the user changes the check box,
			// or when uploading...and we do NOT want to update the check box when uploading temporarily changes the model.
			//_showCropMarks.Checked = _model.ShowCropMarks;
		}

		public void SetDisplayMode(PublishModel.DisplayModes displayMode)
		{
			// TODO-WV2: Can we clear the cache for WV2? Do we need to?
			// Abort any work we're doing to prepare a preview (at least stop it interfering with other navigation).
			PublishHelper.Cancel();

			// Suspending/resuming layout makes the transition between modes a bit smoother.
			SuspendLayout();
			if (displayMode != PublishModel.DisplayModes.BloomPUB &&
				displayMode != PublishModel.DisplayModes.EPUB &&
				displayMode != PublishModel.DisplayModes.AudioVideo &&
				displayMode != PublishModel.DisplayModes.PdfPrint &&
				displayMode != PublishModel.DisplayModes.Upload &&
				_htmlControl != null &&
				Controls.Contains(_htmlControl))
			{
				Controls.Remove(_htmlControl);

				// disposal of the browser is good but it hides a multitude of sins that we'd rather catch and fix during development. E.g. BL-4901
				if(!ApplicationUpdateSupport.IsDevOrAlpha)
				{
					_htmlControl.Dispose();
					_htmlControl = null;
				}
			}

			ResetCantPublishMessage();
			_publishToVideoApi.AbortMakingVideo();

			switch (displayMode)
			{
				case PublishModel.DisplayModes.WaitForUserToChooseSomething:
					break;
				case PublishModel.DisplayModes.Upload:
					Logger.WriteEvent("Entering Publish Web Upload Screen");
					LibraryPublishApi.Model = PublishApi.Model = new BloomLibraryPublishModel(_bookTransferrer, _model.BookSelection.CurrentSelection, _model);
					ShowHtmlPanel(BloomFileLocator.GetBrowserFile(false, "publish", "LibraryPublish", "loader.html"));
					break;
				case PublishModel.DisplayModes.PdfPrint:
					BloomPubMaker.ControlForInvoke = ParentForm; // something created on UI thread that won't go away
					ShowHtmlPanel(BloomFileLocator.GetBrowserFile(false, "publish", "PDFPrintPublish", "PublishPdfPrint.html"));
					break;
				case PublishModel.DisplayModes.BloomPUB:
					PublishApi.Model = new BloomLibraryPublishModel(_bookTransferrer, _model.BookSelection.CurrentSelection, _model);
					BloomPubMaker.ControlForInvoke = ParentForm; // something created on UI thread that won't go away
					ShowHtmlPanel(BloomFileLocator.GetBrowserFile(false, "publish", "ReaderPublish", "loader.html"));
					break;
				case PublishModel.DisplayModes.AudioVideo:
					BloomPubMaker.ControlForInvoke = ParentForm; // something created on UI thread that won't go away
					ShowHtmlPanel(BloomFileLocator.GetBrowserFile(false, "publish", "video", "PublishAudioVideo.html"));
					break;
				case PublishModel.DisplayModes.EPUB:
					PublishEpubApi.ControlForInvoke = ParentForm; // something created on UI thread that won't go away
					ShowHtmlPanel(BloomFileLocator.GetBrowserFile(false, "publish", "ePUBPublish", "loader.html"));
					break;
				case PublishModel.DisplayModes.NotPublishable:
					ShowCantPublishMessage();
					break;
			}
			ResumeLayout(true);
		}

		private void ShowCantPublishMessage()
		{
			// Turn off usually visible things.
			tableLayoutPanel1.Visible = false;

			// If we're showing this panel, we must have a current selection.
			Debug.Assert(_model.BookSelection?.CurrentSelection != null,
				"We tried to show a message without a CurrentSelection");
			var book = _model.BookSelection.CurrentSelection;
			var firstOverlayPageNum = book.GetNumberOfFirstPageWithOverlay();
			_publishReqEntOverlayPage.Text = _cantPublishPageWithPlaceholder.Replace("{0}", firstOverlayPageNum);
			_publishReqEntProblem.Text = _cantPublishProblemWithPlaceholder.Replace("{0}", book.TitleBestForUserDisplay);

			// Turn on the hidden panel
			_publishRequiresEnterprisePanel.Visible = true;
		}

		private void ResetCantPublishMessage()
		{
			_publishRequiresEnterprisePanel.Visible = false;
			tableLayoutPanel1.Visible = true;
		}

		private void ShowHtmlPanel(string pathToHtml)
		{
			_model.BookSelection.CurrentSelection.ReportIfBrokenAudioSentenceElements();
			Logger.WriteEvent("Entering Publish Screen: "+ pathToHtml);
			Cursor = Cursors.WaitCursor;
			if (_htmlControl != null)
			{
				Controls.Remove(_htmlControl);
				_htmlControl.Dispose();
			}
			_htmlControl = new HtmlPublishPanel(pathToHtml);
			// Setting the location explicitly makes the transition a bit smoother.
			_htmlControl.Location = new Point(tableLayoutPanel1.Width, 0);
			_htmlControl.Dock = DockStyle.Fill;
			var saveBackGround = _htmlControl.BackColor; // changed to match parent during next statement
			Controls.Add(_htmlControl);
			_htmlControl.BackColor = saveBackGround; // keep own color.
			// This control is dock.fill. It has to be in front of tableLayoutPanel1 (which is Left) for Fill to work.
			_htmlControl.BringToFront();
			Cursor = Cursors.Default;
		}

		private void OnPublishRadioChanged(object sender, EventArgs e)
		{
			if (!_activated)
				return;

			// This method is triggered both by a radio button being set and by a button being cleared.
			// Since we share the same method across all radio buttons, it gets called twice whenever
			// a user changes the publish mode, once for the new mode being set and once for the old
			// mode being cleared.  We want to respond only to the new mode being set.
			// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6009.
			if (!((RadioButton)sender).Checked)
				return;

			// BL-625: One of the RadioButtons is now checked, so it is safe to re-enable AutoCheck.
			if (SIL.PlatformUtilities.Platform.IsMono)
				SetAutoCheck(true);

			SetModelFromButtons();
		}

		/// <summary>
		/// This method sets up the model from the current state of the radio buttons.  It is called on
		/// two occasions: all the buttons have just been cleared or one of the buttons has just been
		/// selected.  The code assumes that no more than one button can be selected at a time.
		/// </summary>
		private void SetModelFromButtons()
		{
			_model.UploadMode = _uploadRadio.Checked;
			_model.EpubMode = _epubRadio.Checked;
			_model.PdfPrintMode = false;
			if (_pdfPrintRadio.Checked)
			{
				_model.PdfPrintMode = _pdfPrintRadio.Checked;
				_model.DisplayMode = PublishModel.DisplayModes.PdfPrint;
			}
			else if (_epubRadio.Checked)
			{
				_model.DisplayMode = PublishModel.DisplayModes.EPUB;
				// TODO-WV2: Can we delete cookies in WV2?  Do we need to?
			}
			else if (_uploadRadio.Checked)
			{
				_model.DisplayMode = PublishModel.DisplayModes.Upload;
			}
			else if (_bloomPUBRadio.Checked)
			{
				_model.DisplayMode = PublishModel.DisplayModes.BloomPUB;
			}
			else if (_recordVideoRadio.Checked)
			{
				_model.DisplayMode = PublishModel.DisplayModes.AudioVideo;
			}
			else // no buttons selected
			{
				_model.DisplayMode = PublishModel.DisplayModes.WaitForUserToChooseSomething;
			}			
			_model.ShowCropMarks = false;
		}

		internal string PdfPreviewPath { get { return _model.PdfFilePath; } }

		private void _makePdfBackgroundWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			e.Result = _model.BookletPortion; //record what our parameters were, so that if the user changes the request and we cancel, we can detect that we need to re-run
			_model.LoadBook(sender as BackgroundWorker, e);
		}

		private void OnSave_Click(object sender, EventArgs e)
		{
			_model.SavePdf();
		}
		public string HelpTopicUrl
		{
			get { return "/Tasks/Publish_tasks/Publish_tasks_overview.htm"; }
		}

		private void _openinBrowserMenuItem_Click(object sender, EventArgs e)
		{
			_model.DebugCurrentPDFLayout();
		}

		private void _openPDF_Click(object sender, EventArgs e)
		{
			PathUtilities.OpenFileInApplication(_model.PdfFilePath);
		}

		/// <summary>
		/// Make the preview required for publishing the book.
		/// </summary>
		internal void MakePDFForUpload(IProgress progress)
		{
			if (IsMakingPdf)
			{
				// Can't start another until current attempt finishes.
				_makePdfBackgroundWorker.CancelAsync();
				while (IsMakingPdf)
					Thread.Sleep(100);
			}

			var message = new LicenseChecker().CheckBook(_model.BookSelection.CurrentSelection,
				_model.BookSelection.CurrentSelection.ActiveLanguages.ToArray());
			if (message != null)
			{
				MessageBox.Show(message, LocalizationManager.GetString("Common.Warning", "Warning"));
				return;
			}

			_previewProgress = progress;
			_makePdfBackgroundWorker.ProgressChanged += UpdatePreviewProgress;

			// Usually these will have been set by SetModelFromButtons, but the publish button might already be showing when we go to this page.
			_model.ShowCropMarks = false; // don't want in online preview
			_model.BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet; // has all the pages and cover in form suitable for online use
			_makePdfBackgroundWorker.RunWorkerAsync();
			// We normally generate PDFs in the background, but this routine should not return until we actually have one.
			while (IsMakingPdf)
			{
				Thread.Sleep(100);
				Application.DoEvents(); // Wish we didn't need this, but without it bulk upload freezes making 'preview' which is really the PDF to upload.
			}
			_makePdfBackgroundWorker.ProgressChanged -= UpdatePreviewProgress;
			_previewProgress = null;
			_previousStatus = null;
		}

		IProgress _previewProgress;
		string _previousStatus;
		private void UpdatePreviewProgress(object sender, ProgressChangedEventArgs e)
		{
			if (_previewProgress == null)
				return;
			if (_previewProgress.ProgressIndicator != null)
				_previewProgress.ProgressIndicator.PercentCompleted = e.ProgressPercentage;
			var status = e.UserState as string;
			// Don't repeat a status message, even if modified by trailing spaces or periods or ellipses.
			if (status != null && status != _previousStatus && status.Trim(new[] {' ', '.', '\u2026'}) != _previousStatus)
				_previewProgress.WriteStatus(status);
			_previousStatus = status;
		}

		private void ExportAudioFiles1PerPageToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_model.ExportAudioFiles1PerPage();
		}
	}
}
