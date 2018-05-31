using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition.Primitives;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using SIL.Reporting;
using SIL.IO;
using System.Drawing;
using System.Dynamic;
using Bloom.Api;
using Bloom.Publish.Android;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.Epub;
using Bloom.Publish.PDF;
using Bloom.web.controllers;
using Fleck;
using Newtonsoft.Json;
using RestSharp.Extensions;

namespace Bloom.Publish
{
	public partial class PublishView : UserControl, IBloomTabArea
	{
		public readonly PublishModel _model;
		private readonly ComposablePartCatalog _extensionCatalog;
		private bool _activated;
		private BloomLibraryUploadControl _uploadControl;
		private BookTransfer _bookTransferrer;
		private LoginDialog _loginDialog;
		private PictureBox _previewBox;
		private EpubView _epubPreviewControl;
		private HtmlPublishPanel _htmlControl;
		private NavigationIsolator _isolator;
		private PublishToAndroidApi _publishApi;
		private BloomWebSocketServer _webSocketServer;
		// This constant must match the ID that is used for the listener set up in the React component EpubPreview
		private const string kWebsocketPreviewId = "epubPreview";
		private EpubMaker.ImageDescriptionPublishing _desiredImageDescriptionPublishing = EpubMaker.ImageDescriptionPublishing.None;
		private Boolean _desiredPrioritizeUserSize = false;
		private bool _needNewPreview; // Used when asked to update preview while in the middle of using the current one (e.g., to save it).
		private Action<EpubMaker> _doWhenPreviewComplete; // Something to do when the current preview is complete (e.g., save it)
		private BackgroundWorker _previewWorker;
		private string _previewSrc;


		public delegate PublishView Factory();//autofac uses this

		public PublishView(PublishModel model,
			SelectedTabChangedEvent selectedTabChangedEvent, LocalizationChangedEvent localizationChangedEvent, BookTransfer bookTransferrer, LoginDialog login, NavigationIsolator isolator,
			PublishToAndroidApi publishApi, BloomWebSocketServer webSocketServer, PublishEpubApi publishEpubApi)
		{
			_bookTransferrer = bookTransferrer;
			_loginDialog = login;
			_isolator = isolator;
			_publishApi = publishApi;
			_webSocketServer = webSocketServer;
			// This works as long as we only have one PublishView. If we one day support multiple windows, we'll need to do something
			// to tell it the current one.
			publishEpubApi.CurrentView = this;

			InitializeComponent();

			if (this.DesignMode)
				return;

			_model = model;
			_model.View = this;

			_makePdfBackgroundWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(_makePdfBackgroundWorker_RunWorkerCompleted);
			_pdfViewer.PrintProgress += new System.EventHandler<PdfPrintProgressEventArgs>(OnPrintProgress);

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
			if (SIL.PlatformUtilities.Platform.IsMono)
			{
				BackgroundColorsForLinux();
			}

			// Adding this renderer prevents a white line from showing up under the components.
			_menusToolStrip.Renderer = new EditingView.FixedToolStripRenderer();

			// As far as I can tell, this is not needed anymore, and its presence,
			// at least in this place in the code, causes errors when running command-line tools
			// like UploadCommand which needs a PublishView but must not have something fully initialized.
			//GeckoPreferences.Default["pdfjs.disabled"] = false;
			SetupLocalization();
			localizationChangedEvent.Subscribe(o =>
			{
				SetupLocalization();
				UpdateLayoutChoiceLabels();
				UpdateSaveButton();
			});

			// Make this extra box available to show when wanted.
			_previewBox = new PictureBox();
			_previewBox.Visible = false;
			Controls.Add(_previewBox);
			_previewBox.BringToFront();
			_electronicPublishView = new ElectronicPublishView(_model);
		}

		public EpubMaker.ImageDescriptionPublishing CurrentImageDescriptionPublishing => _desiredImageDescriptionPublishing;
		public bool CurrentPrioritizeUserSize => _desiredPrioritizeUserSize;

		public void SetStateOfNonUploadRadios(bool enable)
		{
			_epubRadio.Enabled = enable;
			_epub2Radio.Enabled = enable;
			_bookletBodyRadio.Enabled = enable;
			_bookletCoverRadio.Enabled = enable;
			_simpleAllPagesRadio.Enabled = enable;
			_androidRadio.Enabled = enable;
		}

		private void Deactivate()
		{
			if (IsMakingPdf)
				_makePdfBackgroundWorker.CancelAsync();
			// This allows various cleanup of controls which we won't use again, since we
			// always switch to this state when we reactivate the view.
			// In particular, it is part of the solution to BL-4901 that the HtmlPublishPanel,
			// if it is active, is removed (hence deactivated) and disposed.
			SetDisplayMode(PublishModel.DisplayModes.WaitForUserToChooseSomething);
			// This is only supposed to be active in one mode of PublishView.
			Browser.SuppressJavaScriptErrors = false;
		}

		private void BackgroundColorsForLinux() {

			var bmp = new Bitmap(_menusToolStrip.Width, _menusToolStrip.Height);
			using (var g = Graphics.FromImage(bmp))
			{
				using (var b = new SolidBrush(_menusToolStrip.BackColor))
				{
					g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
				}
			}
			_menusToolStrip.BackgroundImage = bmp;
		}

		private void SetAutoCheck(bool autoCheck)
		{
			if (_simpleAllPagesRadio.AutoCheck == autoCheck)
				return;

			_simpleAllPagesRadio.AutoCheck = autoCheck;
			_bookletCoverRadio.AutoCheck = autoCheck;
			_bookletBodyRadio.AutoCheck = autoCheck;
			_uploadRadio.AutoCheck = autoCheck;
			_epubRadio.AutoCheck = _epub2Radio.AutoCheck = autoCheck;
			_androidRadio.AutoCheck = autoCheck;
		}

		private void SetupLocalization()
		{
			LocalizeSuperToolTip(_simpleAllPagesRadio, "PublishTab.OnePagePerPaperRadio");
			LocalizeSuperToolTip(_bookletCoverRadio, "PublishTab.CoverOnlyRadio");
			LocalizeSuperToolTip(_bookletBodyRadio, "PublishTab.BodyOnlyRadio");
			LocalizeSuperToolTip(_uploadRadio, "PublishTab.ButtonThatShowsUploadForm");
			LocalizeSuperToolTip(_epubRadio, "PublishTab.EpubRadio");
			LocalizeSuperToolTip(_androidRadio, "PublishTab.AndroidButton");
		}

		// Used by LocalizeSuperToolTip to remember original English keys
		Dictionary<Control, string> _originalSuperToolTips = new Dictionary<Control, string>();
		private readonly ElectronicPublishView _electronicPublishView;

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

			Logger.WriteEvent("Entered Publish Tab");
			if (IsMakingPdf)
				return;


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

			// We choose not to remember the last state this tab might have been in.
			// Also since we don't know if the pdf is out of date, we assume it is, and don't show the prior pdf.
			// SetModelFromButtons takes care of both of these things for the model
			ClearRadioButtons();
			SetModelFromButtons();
			_model.DisplayMode = PublishModel.DisplayModes.WaitForUserToChooseSomething;

			UpdateDisplay();

			_activated = true;
		}

		private void ClearRadioButtons()
		{
			_bookletCoverRadio.Checked = _bookletBodyRadio.Checked =
				_simpleAllPagesRadio.Checked = _uploadRadio.Checked = _epubRadio.Checked = _epub2Radio.Checked = _androidRadio.Checked = false;
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
					else if (error is FileNotFoundException && ((FileNotFoundException) error).FileName == "BloomPdfMaker.exe")
					{
						ErrorReport.NotifyUserOfProblem(error, error.Message);
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
			if(e.Cancelled || _model.BookletPortion != (PublishModel.BookletPortions) e.Result )
			{
				MakeBooklet();
			}
		}

		private void UpdateDisplayMode()
		{
			if (IsHandleCreated) // May not be when bulk uploading
			{
				// Upload and ePUB display modes simply depend on the appropriate button being checked.
				// If any of the other buttons is checked, we display the preview IF we have it.
				if (_uploadRadio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.Upload;
				else if (_epubRadio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.EPUB;
				else if (_epub2Radio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.EPUB2;
				else if (_androidRadio.Checked)
					_model.DisplayMode = PublishModel.DisplayModes.Android;
				else if (_model.PdfGenerationSucceeded)
					_model.DisplayMode = PublishModel.DisplayModes.ShowPdf;
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

			_bookletCoverRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletCover && !_model.UploadMode;
			_bookletBodyRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletPages && !_model.UploadMode;
			_simpleAllPagesRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.AllPagesNoBooklet && !_model.UploadMode;
			_uploadRadio.Checked = _model.UploadMode;
			_epubRadio.Checked = _model.EpubMode;
			_epub2Radio.Checked = _model.Epub2Mode;

			if (!_model.AllowUpload)
			{
			   //this doesn't actually show when disabled		        _superToolTip.GetSuperStuff(_uploadRadio).SuperToolTipInfo.BodyText = "This creator of this book, or its template, has marked it as not being appropriate for upload to BloomLibrary.org";
			}
			_uploadRadio.Enabled = _model.AllowUpload;
			_simpleAllPagesRadio.Enabled = _model.AllowPdf;
			_bookletBodyRadio.Enabled = _model.AllowPdfBooklet;
			_bookletCoverRadio.Enabled = _model.AllowPdfCover;
			_openinBrowserMenuItem.Enabled = _openPDF.Enabled = _model.PdfGenerationSucceeded;

			// No reason to update from model...we only change the model when the user changes the check box,
			// or when uploading...and we do NOT want to update the check box when uploading temporarily changes the model.
			//_showCropMarks.Checked = _model.ShowCropMarks;

			UpdateLayoutChoiceLabels();
		}

		private void UpdateLayoutChoiceLabels()
		{
			if (_model == null || _model.BookSelection == null || _model.BookSelection.CurrentSelection == null)
				return; // May get called when localization changes even though tab is not visible.
			var layout = _model.PageLayout;
			var layoutChoices = _model.BookSelection.CurrentSelection.GetLayoutChoices();
			_layoutChoices.DropDownItems.Clear();
			_layoutChoices.DropDownItems.Add(new ToolStripSeparator());
			var headerText = LocalizationManager.GetString(@"PublishTab.OptionsMenu.SizeLayout", "Size/Layout",
				@"Header for a region of the menu which lists various standard page layout sizes");
			var headerItem2 = (ToolStripMenuItem) _layoutChoices.DropDownItems.Add(headerText);
			headerItem2.Enabled = false;
			foreach (var lc in layoutChoices)
			{
				var text = LocalizationManager.GetDynamicString("Bloom", "LayoutChoices." + lc, lc.ToString());
				ToolStripMenuItem item = (ToolStripMenuItem) _layoutChoices.DropDownItems.Add(text);
				item.Tag = lc;
				item.Text = text;
				item.Checked = lc.ToString() == layout.ToString();
				item.CheckOnClick = true;
				item.Click += OnLayoutChosen;
			}

			_layoutChoices.DropDownItems.Add(new ToolStripSeparator());
			var textItem = LocalizationManager.GetString("PublishTab.LessMemoryPdfMode", "Use less memory (slower)");
			var menuItem = (ToolStripMenuItem) _layoutChoices.DropDownItems.Add(textItem);
			menuItem.Checked = _model.BookSelection.CurrentSelection.UserPrefs.ReducePdfMemoryUse;
			menuItem.CheckOnClick = true;
			menuItem.CheckedChanged += OnSinglePageModeChanged;

			// "EditTab" because it is the same text.  No sense in having it listed twice.
			_layoutChoices.ToolTipText = LocalizationManager.GetString("EditTab.PageSizeAndOrientation.Tooltip",
				"Choose a page size and orientation");
		}

		private void OnLayoutChosen(object sender, EventArgs e)
		{
			var item = (ToolStripMenuItem)sender;
			_model.PageLayout = ((Layout)item.Tag);
			ClearRadioButtons();
			UpdateDisplay();
			SetDisplayMode(PublishModel.DisplayModes.WaitForUserToChooseSomething);
		}

		private void OnSinglePageModeChanged(object sender, EventArgs e)
		{
			var item = (ToolStripMenuItem)sender;
			_model.BookSelection.CurrentSelection.UserPrefs.ReducePdfMemoryUse = item.Checked;
		}

		public void SetDisplayMode(PublishModel.DisplayModes displayMode)
		{
			// This is only supposed to be active in one mode of PublishView.
			Browser.SuppressJavaScriptErrors = false;

			if (displayMode != PublishModel.DisplayModes.Upload && _uploadControl != null)
			{
				Controls.Remove(_uploadControl);
				_uploadControl = null;
			}
			if (displayMode != PublishModel.DisplayModes.EPUB && _epubPreviewControl != null && Controls.Contains(_epubPreviewControl))
			{
				Controls.Remove(_epubPreviewControl);
			}
			if(displayMode != PublishModel.DisplayModes.Android || displayMode != PublishModel.DisplayModes.EPUB2 && _htmlControl != null && Controls.Contains(_htmlControl))
			{
				Controls.Remove(_htmlControl);

				// disposal of the browser is good but it hides a multitude of sins that we'd rather catch and fix during development. E.g. BL-4901
				if(!ApplicationUpdateSupport.IsDevOrAlpha)
				{
					_htmlControl.Dispose();
					_htmlControl = null;
				}
			}
				
			switch (displayMode)
			{
				case PublishModel.DisplayModes.WaitForUserToChooseSomething:
					// _pdfViewer.Visible must be set true at least once momentarily for other display controls to get the right size.  (BL-6006)
					_pdfViewer.Visible = true;
					_printButton.Enabled = _saveButton.Enabled = false;
					Cursor = Cursors.Default;
					_workingIndicator.Visible = false;
					_pdfViewer.Visible = false;
					break;
				case PublishModel.DisplayModes.Working:
					_printButton.Enabled = _saveButton.Enabled = false;
					_workingIndicator.Cursor = Cursors.WaitCursor;
					Cursor = Cursors.WaitCursor;
					_workingIndicator.Visible = true;
					_pdfViewer.Visible = false;
					break;
				case PublishModel.DisplayModes.ShowPdf:
					Logger.WriteEvent("Entering Publish PDF Screen");
					if (RobustFile.Exists(_model.PdfFilePath))
					{
						_pdfViewer.Visible = true;
						_workingIndicator.Visible = false;
						Cursor = Cursors.Default;
						_saveButton.Enabled = true;
						_printButton.Enabled = _pdfViewer.ShowPdf(_model.PdfFilePath);
					}
					break;
				case PublishModel.DisplayModes.Printing:
					_simpleAllPagesRadio.Enabled = false;
					_bookletCoverRadio.Enabled = false;
					_bookletBodyRadio.Enabled = false;
					_printButton.Enabled = _saveButton.Enabled = false;
					_workingIndicator.Cursor = Cursors.WaitCursor;
					Cursor = Cursors.WaitCursor;
					_workingIndicator.Visible = true;
					_pdfViewer.Visible = true;
					break;
				case PublishModel.DisplayModes.ResumeAfterPrint:
					_simpleAllPagesRadio.Enabled = true;
					_pdfViewer.Visible = true;
					_workingIndicator.Visible = false;
					Cursor = Cursors.Default;
					_saveButton.Enabled = true;
					_printButton.Enabled = true;
					_pdfViewer.Visible = true;
					break;
				case PublishModel.DisplayModes.Upload:
				{
					Logger.WriteEvent("Entering Publish Upload Screen");
					_workingIndicator.Visible = false; // If we haven't finished creating the PDF, we will indicate that in the progress window.
					_saveButton.Enabled = _printButton.Enabled = false; // Can't print or save in this mode...wouldn't be obvious what would be saved.
					_pdfViewer.Visible = false;
					Cursor = Cursors.Default;

					if (_uploadControl == null)
					{
						SetupPublishControl();
					}

					break;
				}
				case PublishModel.DisplayModes.EPUB:
				{
					Logger.WriteEvent("Entering Publish Epub Screen");
					// We may reuse this for the process of generating the ePUB staging files. For now, skip it.
					_workingIndicator.Visible = false;
					_printButton.Enabled = false; // don't know how to print an ePUB
					_pdfViewer.Visible = false;
					Cursor = Cursors.WaitCursor;
					_epubPreviewControl = ElectronicPublishView.SetupEpubControl(_epubPreviewControl, _isolator, () => _saveButton.Enabled = _model.EpubMaker.ReadyToSave());
					_epubPreviewControl.SetBounds(_pdfViewer.Left, _pdfViewer.Top,
							_pdfViewer.Width, _pdfViewer.Height);
					_epubPreviewControl.Dock = _pdfViewer.Dock;
					_epubPreviewControl.Anchor = _pdfViewer.Anchor;
					var saveBackGround = _epubPreviewControl.BackColor; // changed to match parent during next statement
					Controls.Add(_epubPreviewControl);
					_epubPreviewControl.BackColor = saveBackGround; // keep own color.
														// Typically this control is dock.fill. It has to be in front of tableLayoutPanel1 (which is Left) for Fill to work.
					_epubPreviewControl.BringToFront();
					Cursor = Cursors.Default;

					// We rather mangled the Readium code in the process of cutting away its own navigation
					// and other controls. It produces all kinds of JavaScript errors, but it seems to do
					// what we want. So just suppress the toasts for all of them.
					Browser.SuppressJavaScriptErrors = true;
					break;
				}
				case PublishModel.DisplayModes.Android:
					ShowHtmlPanel(BloomFileLocator.GetBrowserFile(false, "publish", "android", "androidPublishUI.html"));
					break;
				case PublishModel.DisplayModes.EPUB2:
					// We rather mangled the Readium code in the process of cutting away its own navigation
					// and other controls. It produces all kinds of JavaScript errors, but it seems to do
					// what we want in our preview. So just suppress the toasts for all of them. This is unfortunate because
					// we'll lose them for all the other JS code in this pane. But I don't have a better solution.
					// We still get them in the output window, in case we really want to look for one.
					Browser.SuppressJavaScriptErrors = true;
					ShowHtmlPanel(BloomFileLocator.GetBrowserFile(false, "publish", "epub", "epubPublishUI.html"));
					bool firstTime = true;
					_htmlControl.Browser.WebBrowser.DocumentCompleted += (sender, args) =>
					{
						// Wait until the document is sufficiently initialized to receive websocket broadcasts
						if (firstTime)
						{
							// We get multiple DocumentCompleted events, e.g., when setting a new preview.
							// Just do this the first time.
							firstTime = false;
							PublishEpubApi.ReportProgress(this,_webSocketServer,
								LocalizationManager.GetString("PublishTab.Epub.PreparingPreview", "Preparing Preview"));
							_webSocketServer.Send("publish/epub/state", GetEpubState(_desiredImageDescriptionPublishing, _desiredPrioritizeUserSize));
						}
					};
					_model.PrepareToStageEpub(); // let's get the epub maker and its browser created on the UI thread
					_model.EpubMaker.PublishImageDescriptions = _desiredImageDescriptionPublishing;
					_model.EpubMaker.PrioritizeUserSize = _desiredPrioritizeUserSize;
					_previewWorker = new BackgroundWorker();
					_previewWorker.RunWorkerCompleted += _previewWorker_RunWorkerCompleted;
					_previewWorker.DoWork += (sender, args) => SetupEpubPreview();
					_previewWorker.RunWorkerAsync();
					break;
			}
			UpdateSaveButton();
		}

		string GetEpubState(EpubMaker.ImageDescriptionPublishing input, bool prioritizeUserSize)
		{
			dynamic state = new ExpandoObject();
			state.imageDescriptionPublishing = GetImageDescriptionState(input);
			state.prioritizeUserSize = prioritizeUserSize;
			return JsonConvert.SerializeObject(state);
		}

		string GetImageDescriptionState(EpubMaker.ImageDescriptionPublishing input)
		{
			switch (input)
			{
				case EpubMaker.ImageDescriptionPublishing.None: return "none";
				case EpubMaker.ImageDescriptionPublishing.OnPage: return "onPage";
				case EpubMaker.ImageDescriptionPublishing.Links: return "links";
				default: throw new InvalidOperationException("enumeration in invalid state");
			}
		}

		public void UpdatePreview(EpubMaker.ImageDescriptionPublishing newImageMode, bool newPrioritizeUserSize, bool retry)
		{
			lock (this)
			{
				if (_desiredImageDescriptionPublishing == newImageMode && _desiredPrioritizeUserSize == newPrioritizeUserSize && !retry)
					return; // getting a request really from the browser, and already in that state.
				_desiredImageDescriptionPublishing = newImageMode;
				_desiredPrioritizeUserSize = newPrioritizeUserSize;
				if (_previewWorker != null)
				{
					// Something changed before we even finished generating the preview! abort the current attempt, which will lead
					// to trying again.
					_model.EpubMaker.AbortRequested = true;
					return;
				}

				if (_doWhenPreviewComplete != null)
				{
					// We're committed to doing something with a completed preview...and we're done making the preview...
					// so probably we're in the middle of doing the completed preview action.
					// We need to let it complete; THEN we should update the preview again.
					_needNewPreview = true;
					return;
				}
				_previewWorker = new BackgroundWorker();
			}

			_model.EpubMaker.PublishImageDescriptions = newImageMode;
			_model.EpubMaker.PrioritizeUserSize = newPrioritizeUserSize;
			// clear the obsolete preview, if any; this also ensures that when the new one gets done,
			// we will really be changing the src attr in the preview iframe so the display will update.
			_webSocketServer.Send(kWebsocketPreviewId, "");
			PublishEpubApi.ReportProgress(this, _webSocketServer,
				LocalizationManager.GetString("PublishTab.Epub.PreparingPreview", "Preparing Preview"));
			_previewWorker.RunWorkerCompleted += _previewWorker_RunWorkerCompleted;
			_previewWorker.DoWork += (sender, args) =>
			{
				_previewSrc = _model.UpdateEpubControlContent();
			};
			_previewWorker.RunWorkerAsync();
		}

		private void _previewWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			bool abortRequested;
			// I'm not absolutely sure that this and UpdatePreview will always run on the UI thread.
			// So there is a possible race condition:
			// while we are running this method, the user does something which results in
			// a new UpdatePreview on another thread.
			// If updatePreview gets the lock first, it will set the abort flag and quit;
			// this method will clean up and call UpdatePreview again.
			// If this method gets the lock first, it will proceed to update the preview
			// with the successful results it obtained. The new update will proceed in the background.
			// That should be OK, though there is probably a rare pathological case where progress shows
			// two Preparing messages followed by two Done messages.
			lock (this)
			{
				_previewWorker.Dispose();
				_previewWorker = null; // allows UpdatePrevew to know nothing is in progress
				abortRequested = _model.EpubMaker.AbortRequested;
				// Either we just made a successful preview, or we're just about to try again.
				// Either way, we don't need yet another new one later (unless another change happens)
				_needNewPreview = false;
			}

			if (abortRequested || _model.EpubMaker.PublishImageDescriptions != _desiredImageDescriptionPublishing
				|| _model.EpubMaker.PrioritizeUserSize != _desiredPrioritizeUserSize)
			{
				UpdatePreview(_desiredImageDescriptionPublishing, _desiredPrioritizeUserSize, true);
				return;
			}

			if (_doWhenPreviewComplete != null)
			{
				Debug.Assert(!_model.EpubMaker.AbortRequested);
				Debug.Assert(_model.EpubMaker.PublishImageDescriptions == _desiredImageDescriptionPublishing);
				Debug.Assert(_model.EpubMaker.PrioritizeUserSize == _desiredPrioritizeUserSize);
				_doWhenPreviewComplete(_model.EpubMaker);
				_doWhenPreviewComplete = null;

				if (_needNewPreview)
				{
					// We got a request somewhere in the process of running the action.
					UpdatePreview(_desiredImageDescriptionPublishing, _desiredPrioritizeUserSize, true);
					return;
				}
			}

			Invoke((Action) (() =>
			{
				_webSocketServer.Send(kWebsocketPreviewId, _previewSrc);
				PublishEpubApi.ReportProgress(this,_webSocketServer,
					LocalizationManager.GetString("PublishTab.Epub.Done", "Done"));
			}));
		}

		/// <summary>
		/// Perform the requested action (currently the only example is, save the epub) when we have an up-to-date
		/// preview. Pass it the EpubMaker that generated the preview.
		/// </summary>
		/// <param name="doWhenReady"></param>
		public void RequestPreviewOutput(Action<EpubMaker> doWhenReady)
		{
			lock (this)
			{
				_doWhenPreviewComplete = doWhenReady;
				if (_previewWorker != null)
					return; // in process of making, can't do it now; will be done in _previewWorker_RunWorkerCompleted.
			}

			Debug.Assert(!_model.EpubMaker.AbortRequested);
			Debug.Assert(_model.EpubMaker.PublishImageDescriptions == _desiredImageDescriptionPublishing);
			Debug.Assert(_model.EpubMaker.PrioritizeUserSize == _desiredPrioritizeUserSize);
			_doWhenPreviewComplete(_model.EpubMaker);

			_doWhenPreviewComplete = null;

			if (_needNewPreview) // we got a request during action processing
				UpdatePreview(_desiredImageDescriptionPublishing, _desiredPrioritizeUserSize, true);
		}

		private void SetupEpubPreview()
		{
			_model.DoAnyNeededAudioCompression();
			_previewSrc = _model.SetupEpubControlContent();
		}

		private void ShowHtmlPanel(string pathToHtml)
		{
			Logger.WriteEvent("Entering Publish Screen: "+ pathToHtml);
			_workingIndicator.Visible = false;
			_printButton.Enabled = false;
			_pdfViewer.Visible = false;
			Cursor = Cursors.WaitCursor;
			_htmlControl = new HtmlPublishPanel(_isolator, pathToHtml);
			_htmlControl.SetBounds(_pdfViewer.Left, _pdfViewer.Top,
				_pdfViewer.Width, _pdfViewer.Height);
			_htmlControl.Dock = _pdfViewer.Dock;
			_htmlControl.Anchor = _pdfViewer.Anchor;
			var saveBackGround = _htmlControl.BackColor; // changed to match parent during next statement
			Controls.Add(_htmlControl);
			_htmlControl.BackColor = saveBackGround; // keep own color.
			// Typically this control is dock.fill. It has to be in front of tableLayoutPanel1 (which is Left) for Fill to work.
			_htmlControl.BringToFront();
			Cursor = Cursors.Default;
		}

		private void UpdateSaveButton()
		{
			if (Controls.Contains(_epubPreviewControl))
				_saveButton.Text = LocalizationManager.GetString("PublishTab.SaveEpub", "&Save ePUB...");
			else
				_saveButton.Text = LocalizationManager.GetString("PublishTab.SaveButton", "&Save PDF...");
		}

		private void SetupPublishControl()
		{
			if (_uploadControl != null)
			{
				//we currently rebuild it to update contents, as currently the constructor is where setup logic happens (we could change that)
				Controls.Remove(_uploadControl); ;
			}

			var libaryPublishModel = new BloomLibraryPublishModel(_bookTransferrer, _model.BookSelection.CurrentSelection);
			_uploadControl = new BloomLibraryUploadControl(this, libaryPublishModel, _loginDialog);
			_uploadControl.SetBounds(_pdfViewer.Left, _pdfViewer.Top,
				_pdfViewer.Width, _pdfViewer.Height);
			_uploadControl.Dock = _pdfViewer.Dock;
			_uploadControl.Anchor = System.Windows.Forms.AnchorStyles.Left|System.Windows.Forms.AnchorStyles.Top|System.Windows.Forms.AnchorStyles.Right|System.Windows.Forms.AnchorStyles.Bottom;
			var saveBackColor = _uploadControl.BackColor;
			Controls.Add(_uploadControl); // somehow this changes the backcolor
			_uploadControl.BackColor = saveBackColor; // Need a normal back color for this so links and text can be seen
			// Typically this control is dock.fill. It has to be in front of tableLayoutPanel1 (which is Left) for Fill to work.
			_uploadControl.BringToFront();
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
			_model.Epub2Mode = _epub2Radio.Checked;
			bool pdfPreviewMode = false;
			if (_simpleAllPagesRadio.Checked)
			{
				_model.BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet;
				pdfPreviewMode = true;
			}
			else if (_bookletCoverRadio.Checked)
			{
				_model.BookletPortion = PublishModel.BookletPortions.BookletCover;
				pdfPreviewMode = true;
			}
			else if (_bookletBodyRadio.Checked)
			{
				_model.BookletPortion = PublishModel.BookletPortions.BookletPages;
				pdfPreviewMode = true;
			}
			else if (_epubRadio.Checked)
			{
				_model.DisplayMode = PublishModel.DisplayModes.EPUB;
			}
			else if (_epub2Radio.Checked)
			{
				_model.DisplayMode = PublishModel.DisplayModes.EPUB2;
			}
			else if (_uploadRadio.Checked)
			{
				// We want to upload the simple PDF with the book, but we don't make it
				// until we actually start the upload.
				_model.BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet;
				_model.DisplayMode = PublishModel.DisplayModes.Upload;
			}
			else if (_androidRadio.Checked)
			{
				_model.DisplayMode = PublishModel.DisplayModes.Android;
			}
			else // no buttons selected
			{
				_model.DisplayMode = PublishModel.DisplayModes.WaitForUserToChooseSomething;
			}
			if (pdfPreviewMode)
			{
				if (_model.DisplayMode == PublishModel.DisplayModes.Upload)
				{
					// We've transitioned away from upload to a PDF preview.
					_model.DisplayMode = _model.PdfGenerationSucceeded
						? PublishModel.DisplayModes.ShowPdf
						: PublishModel.DisplayModes.WaitForUserToChooseSomething;
				}
				if (IsMakingPdf)
				{
					_makePdfBackgroundWorker.CancelAsync();
					UpdateDisplay();
				}
				else
				{
					MakeBooklet();
				}
			}
			else // not PDF preview mode
			{
				if (_model.DisplayMode != PublishModel.DisplayModes.Upload)
					_model.BookletPortion = PublishModel.BookletPortions.None;
			}
			_model.ShowCropMarks = false;
		}

		internal string PdfPreviewPath { get { return _model.PdfFilePath; } }

		SIL.Windows.Forms.Progress.ProgressDialog _progress;

		public void MakeBooklet()
		{
			if (IsMakingPdf)
			{
				// Can't start again until this one finishes
				return;
			}
			_model.PdfGenerationSucceeded = false; // and so it stays unless we generate it successfully.
			if (_uploadRadio.Checked)
			{
				// We aren't going to display it, so don't bother generating it unless the user actually uploads.
				// Unfortunately, the completion of the generation process is normally responsible for putting us into
				// the right display mode for what we generated (or failed to), after this routine puts us into the
				// mode that shows generation is pending. For the upload button case, we want to go straight to the Upload
				// mode, so the upload control appears. This is a bizarre place to do it, but I can't find a better one.
				SetDisplayMode(PublishModel.DisplayModes.Upload);
				return;
			}
			if (_epubRadio.Checked)
			{
				// We aren't going to display it, so don't bother generating it.
				// Unfortunately, the completion of the generation process is normally responsible for putting us into
				// the right display mode for what we generated (or failed to), after this routine puts us into the
				// mode that shows generation is pending. For the ePUB button case, we want to go straight to the ePUB preview.
				SetDisplayMode(PublishModel.DisplayModes.EPUB);
				return;
			}

			SetDisplayMode(PublishModel.DisplayModes.Working);

			using (_progress = new SIL.Windows.Forms.Progress.ProgressDialog())
			{
				_progress.Overview = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.Creating",
					"Creating PDF...",
					@"Message displayed in a progress report dialog box");
				_progress.BackgroundWorker = _makePdfBackgroundWorker;
				_makePdfBackgroundWorker.ProgressChanged += UpdateProgress;
				_progress.ShowDialog();	// will start the background process when loaded/showing
				_makePdfBackgroundWorker.ProgressChanged -= UpdateProgress;
				_progress.BackgroundWorker = null;
				if (_progress.ProgressStateResult != null && _progress.ProgressStateResult.ExceptionThatWasEncountered != null)
				{
					string shortMsg = L10NSharp.LocalizationManager.GetString(@"PublishTab.PdfMaker.ErrorProcessing",
						"Error creating, compressing, or recoloring the PDF file",
						@"Message briefly displayed to the user in a toast");
					var longMsg = String.Format("Exception encountered processing the PDF file: {0}", _progress.ProgressStateResult.ExceptionThatWasEncountered);
					NonFatalProblem.Report(ModalIf.None, PassiveIf.All, shortMsg, longMsg, _progress.ProgressStateResult.ExceptionThatWasEncountered);
				}
			}
			_progress = null;
		}

		private void UpdateProgress(object sender, ProgressChangedEventArgs e)
		{
			if (_progress == null || _progress.IsDisposed)
				return;
			_progress.Progress = e.ProgressPercentage;
			var status = e.UserState as string;
			if (status != null)
				_progress.StatusText = status;
		}

		private bool isBooklet()
		{
			return _model.BookletPortion == PublishModel.BookletPortions.BookletCover
					|| _model.BookletPortion == PublishModel.BookletPortions.BookletPages
					|| _model.BookletPortion == PublishModel.BookletPortions.InnerContent; // Not sure this last is used, but play safe...
		}

		private void OnPrint_Click(object sender, EventArgs e)
		{
			var printSettingsPreviewFolder = FileLocator.GetDirectoryDistributedWithApplication("printer settings images");
			var printSettingsSamplePrefix = Path.Combine(printSettingsPreviewFolder,
				_model.PageLayout.SizeAndOrientation + "-" + (isBooklet() ? "Booklet-" : ""));
			string printSettingsSampleName = null;
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				printSettingsSampleName = printSettingsSamplePrefix + "Linux-" + LocalizationManager.UILanguageId + ".png";
				if (!RobustFile.Exists(printSettingsSampleName))
					printSettingsSampleName = printSettingsSamplePrefix + "Linux-en.png";
			}
			if (printSettingsSampleName == null || !RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = printSettingsSamplePrefix + LocalizationManager.UILanguageId + ".png";
			if (!RobustFile.Exists(printSettingsSampleName))
				printSettingsSampleName = printSettingsSamplePrefix + "en" + ".png";
			if (RobustFile.Exists(printSettingsSampleName))
			{
				// We display the _previewBox to show sample print settings. We need to get rid of it when the
				// print dialog goes away. For Windows, the only way I've found to know when that happens is
				// that the main Bloom form gets activated again.  For Linux, waiting for process spawned off
				// to print the pdf file to finish seems to be the only way to know it's safe to hide the
				// sample print settings.  (On Linux/Mono, the form activates almost as soon as the print
				// dialog appears.)
#if __MonoCS__
				_pdfViewer.PrintFinished += FormActivatedAfterPrintDialog;
#else
				var form = FindForm();
				form.Activated += FormActivatedAfterPrintDialog;
#endif
				_previewBox.Image = Image.FromFile(printSettingsSampleName);
				_previewBox.Bounds = GetPreviewBounds();
				_previewBox.SizeMode = PictureBoxSizeMode.Zoom;
				_previewBox.BringToFront(); // prevents BL-6001
				_previewBox.Show();
				if (!Settings.Default.DontShowPrintNotification)
				{
					using (var dlg = new SamplePrintNotification())
					{
						dlg.StartPosition = FormStartPosition.CenterParent;
#if __MonoCS__
						_pdfViewer.PrintFinished -= FormActivatedAfterPrintDialog;
						dlg.ShowDialog(this);
						_pdfViewer.PrintFinished += FormActivatedAfterPrintDialog;
#else
						form.Activated -= FormActivatedAfterPrintDialog; // not wanted when we close the dialog.
						dlg.ShowDialog(this);
						form.Activated += FormActivatedAfterPrintDialog;
#endif
						if (dlg.StopShowing)
						{
							Settings.Default.DontShowPrintNotification = true;
							Settings.Default.Save();
						}
					}
				}
			}
			_pdfViewer.Print();
			Logger.WriteEvent("Calling Print on PDF Viewer");
			_model.ReportAnalytics("Print PDF");
		}

		/// <summary>
		/// Computes the preview bounds (since the image may be bigger than what we have room
		/// for).
		/// </summary>
		Rectangle GetPreviewBounds()
		{
			double horizontalScale = 1.0;
			double verticalScale = 1.0;
			if (_previewBox.Image.Width > ClientRectangle.Width)
				horizontalScale = (double)(ClientRectangle.Width) / (double)(_previewBox.Image.Width);
			if (_previewBox.Image.Height > ClientRectangle.Height)
				verticalScale = (double)(ClientRectangle.Height) / (double)(_previewBox.Image.Height);
			double scale = Math.Min(horizontalScale, verticalScale);
			int widthPreview = (int)(_previewBox.Image.Width * scale);
			int heightPreview = (int)(_previewBox.Image.Height * scale);
			var sizePreview = new Size(widthPreview, heightPreview);
			var xPreview = ClientRectangle.Width - widthPreview;
			var yPreview = ClientRectangle.Height - heightPreview;
			var originPreview = new Point(xPreview, yPreview);
			return new Rectangle(originPreview, sizePreview);
		}

		private void FormActivatedAfterPrintDialog(object sender, EventArgs eventArgs)
		{
#if __MonoCS__
			_pdfViewer.PrintFinished -= FormActivatedAfterPrintDialog;
#else
			var form = FindForm();
			form.Activated -= FormActivatedAfterPrintDialog;
#endif
			_previewBox.Hide();
			if (_previewBox.Image != null)
			{
				_previewBox.Image.Dispose();
				_previewBox.Image = null;
			}
		}

		private void OnPrintProgress(object sender, PdfPrintProgressEventArgs e)
		{
			// BL-788 Only called in Linux version.  Protects against button
			// pushes while print is in progress.
			if (e.PrintInProgress)
			{
				SetDisplayMode(PublishModel.DisplayModes.Printing);
			}
			else
			{
				SetDisplayMode(PublishModel.DisplayModes.ResumeAfterPrint);
				UpdateDisplay ();
			}

		}
		private void _makePdfBackgroundWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			e.Result = _model.BookletPortion; //record what our parameters were, so that if the user changes the request and we cancel, we can detect that we need to re-run
			_model.LoadBook(sender as BackgroundWorker, e);
		}

		private void OnSave_Click(object sender, EventArgs e)
		{
			_model.Save();
		}
		public string HelpTopicUrl
		{
			get { return "/Tasks/Publish_tasks/Publish_tasks_overview.htm"; }
		}

		public ElectronicPublishView ElectronicPublishView
		{
			get { return _electronicPublishView; }
		}

		private void _openinBrowserMenuItem_Click(object sender, EventArgs e)
		{
			_model.DebugCurrentPDFLayout();
		}

		private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e)
		{

		}
		private void _openPDF_Click(object sender, EventArgs e)
		{
			PathUtilities.OpenFileInApplication(_model.PdfFilePath);
		}

		/// <summary>
		/// Make the preview required for publishing the book.
		/// </summary>
		internal void MakePublishPreview()
		{
			if (IsMakingPdf)
			{
				// Can't start another until current attempt finishes.
				_makePdfBackgroundWorker.CancelAsync();
				while (IsMakingPdf)
					Thread.Sleep(100);
			}
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
		}
	}
}
