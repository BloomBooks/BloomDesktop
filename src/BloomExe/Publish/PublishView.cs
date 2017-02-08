using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition.Primitives;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using DesktopAnalytics;
using L10NSharp;
using SIL.Reporting;
using Gecko;
using SIL.IO;
using System.Drawing;

namespace Bloom.Publish
{
	public partial class PublishView : UserControl, IBloomTabArea
	{
		public readonly PublishModel _model;
		private readonly ComposablePartCatalog _extensionCatalog;
		private bool _activated;
		private BloomLibraryPublishControl _publishControl;
		private BookTransfer _bookTransferrer;
		private LoginDialog _loginDialog;
		private PictureBox _previewBox;
		private EpubView _epubPreviewControl;
		private NavigationIsolator _isolator;

		public delegate PublishView Factory();//autofac uses this

		public PublishView(PublishModel model,
			SelectedTabChangedEvent selectedTabChangedEvent, LocalizationChangedEvent localizationChangedEvent, BookTransfer bookTransferrer, LoginDialog login, NavigationIsolator isolator)
		{
			_bookTransferrer = bookTransferrer;
			_loginDialog = login;
			_isolator = isolator;

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
			if (SIL.PlatformUtilities.Platform.IsMono)
			{
				BackgroundColorsForLinux();
			}

			// Adding this renderer prevents a white line from showing up under the components.
			_menusToolStrip.Renderer = new EditingView.FixedToolStripRenderer();

			GeckoPreferences.Default["pdfjs.disabled"] = false;
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

		public void SetStateOfNonUploadRadios(bool enable)
		{
			_epubRadio.Enabled = enable;
			_bookletBodyRadio.Enabled = enable;
			_bookletCoverRadio.Enabled = enable;
			_simpleAllPagesRadio.Enabled = enable;
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
			_epubRadio.AutoCheck = autoCheck;
		}

		private void SetupLocalization()
		{
			LocalizeSuperToolTip(_simpleAllPagesRadio, "PublishTab.OnePagePerPaperRadio");
			LocalizeSuperToolTip(_bookletCoverRadio, "PublishTab.CoverOnlyRadio");
			LocalizeSuperToolTip(_bookletBodyRadio, "PublishTab.BodyOnlyRadio");
			LocalizeSuperToolTip(_uploadRadio, "PublishTab.ButtonThatShowsUploadForm");
			LocalizeSuperToolTip(_epubRadio, "PublishTab.EpubRadio");
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
			_bookletCoverRadio.Checked = _bookletBodyRadio.Checked = _simpleAllPagesRadio.Checked = _uploadRadio.Checked = _epubRadio.Checked = false;
		}

		internal bool IsMakingPdf
		{
			get { return _makePdfBackgroundWorker.IsBusy; }
		}


		public Control TopBarControl
		{
			get { return _topBarPanel; }
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

			_layoutChoices.Text = _model.PageLayout.ToString();

			_bookletCoverRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletCover && !_model.UploadMode;
			_bookletBodyRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.BookletPages && !_model.UploadMode;
			_simpleAllPagesRadio.Checked = _model.BookletPortion == PublishModel.BookletPortions.AllPagesNoBooklet && !_model.UploadMode;
			_uploadRadio.Checked = _model.UploadMode;
			_epubRadio.Checked = _model.EpubMode;

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
//			_layoutChoices.Items.AddRange(layoutChoices.ToArray());
//			_layoutChoices.SelectedText = _model.BookSelection.CurrentSelection.GetLayout().ToString();
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
			_layoutChoices.Text = LocalizationManager.GetDynamicString("Bloom", "LayoutChoices." + layout, layout.ToString());

			_layoutChoices.DropDownItems.Add(new ToolStripSeparator());
			var textItem = LocalizationManager.GetDynamicString("Bloom", "lessMemoryPdfMode", "Use less memory (slower)");
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
			_layoutChoices.Text = _model.PageLayout.ToString();
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
			if (displayMode != PublishModel.DisplayModes.Upload && _publishControl != null)
			{
				Controls.Remove(_publishControl);
				_publishControl = null;
			}
			if (displayMode != PublishModel.DisplayModes.EPUB && _epubPreviewControl != null && Controls.Contains(_epubPreviewControl))
			{
				Controls.Remove(_epubPreviewControl);
			}
			if (displayMode != PublishModel.DisplayModes.Upload && displayMode != PublishModel.DisplayModes.EPUB)
				_pdfViewer.Visible = true;
			switch (displayMode)
			{
				case PublishModel.DisplayModes.WaitForUserToChooseSomething:
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
					break;
				case PublishModel.DisplayModes.ShowPdf:
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
					break;
				case PublishModel.DisplayModes.ResumeAfterPrint:
					_simpleAllPagesRadio.Enabled = true;
					_pdfViewer.Visible = true;
					_workingIndicator.Visible = false;
					Cursor = Cursors.Default;
					_saveButton.Enabled = true;
					_printButton.Enabled = true;
					break;
				case PublishModel.DisplayModes.Upload:
				{
					_workingIndicator.Visible = false; // If we haven't finished creating the PDF, we will indicate that in the progress window.
					_saveButton.Enabled = _printButton.Enabled = false; // Can't print or save in this mode...wouldn't be obvious what would be saved.
					_pdfViewer.Visible = false;
					Cursor = Cursors.Default;

					if (_publishControl == null)
					{
						SetupPublishControl();
					}

					break;
				}
				case PublishModel.DisplayModes.EPUB:
				{
					// We may reuse this for the process of generating the ePUB staging files. For now, skip it.
					_workingIndicator.Visible = false;
					_printButton.Enabled = false; // don't know how to print an ePUB
					_pdfViewer.Visible = false;
					Cursor = Cursors.WaitCursor;
					_epubPreviewControl = ElectronicPublishView.SetupEpubControl(_epubPreviewControl, _isolator, () => _saveButton.Enabled = _model.EpubMaker.ReadyToSave());
					_saveButton.Enabled = _model.EpubMaker.ReadyToSave();
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

						break;
				}
			}
			UpdateSaveButton();
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
			if (_publishControl != null)
			{
				//we currently rebuild it to update contents, as currently the constructor is where setup logic happens (we could change that)
				Controls.Remove(_publishControl); ;
			}

			_publishControl = new BloomLibraryPublishControl(this, _bookTransferrer, _loginDialog,
				_model.BookSelection.CurrentSelection);
			_publishControl.SetBounds(_pdfViewer.Left, _pdfViewer.Top,
				_pdfViewer.Width, _pdfViewer.Height);
			_publishControl.Dock = _pdfViewer.Dock;
			_publishControl.Anchor = _pdfViewer.Anchor;
			var saveBackColor = _publishControl.BackColor;
			Controls.Add(_publishControl); // somehow this changes the backcolor
			_publishControl.BackColor = saveBackColor; // Need a normal back color for this so links and text can be seen
			// Typically this control is dock.fill. It has to be in front of tableLayoutPanel1 (which is Left) for Fill to work.
			_publishControl.BringToFront();
		}



		private void OnBookletRadioChanged(object sender, EventArgs e)
		{
			if (!_activated)
				return;

			// BL-625: One of the RadioButtons is now checked, so it is safe to re-enable AutoCheck.
			if (SIL.PlatformUtilities.Platform.IsMono)
				SetAutoCheck(true);

			var oldPortion = _model.BookletPortion;
			var oldCrop = _model.ShowCropMarks; // changing to or from cloud radio CAN change this.
			SetModelFromButtons();
			if (oldPortion == _model.BookletPortion && oldCrop == _model.ShowCropMarks)
			{
				// no changes detected
				if (_uploadRadio.Checked)
				{
					_model.DisplayMode = PublishModel.DisplayModes.Upload;
				}
				else if (_epubRadio.Checked)
				{
					_model.DisplayMode = PublishModel.DisplayModes.EPUB;
				}
				else if (_model.DisplayMode == PublishModel.DisplayModes.Upload)
				{
					// no change because the PREVIOUS button was the cloud one. Need to restore the appropriate
					// non-cloud display
					_model.DisplayMode = _model.PdfGenerationSucceeded
						? PublishModel.DisplayModes.ShowPdf
						: PublishModel.DisplayModes.WaitForUserToChooseSomething;
				}
				else if (_model.DisplayMode == PublishModel.DisplayModes.WaitForUserToChooseSomething)
				{
					// This happens if user went directly to Upload and then chooses Simple layout
					// We haven't actually built a pdf yet, so do it.
					ControlsChanged();
				}
				return;
			}

			ControlsChanged();
		}

		private void ControlsChanged()
		{
			if (IsMakingPdf || _model.BookletPortion == PublishModel.BookletPortions.None)
			{
				_makePdfBackgroundWorker.CancelAsync();
				UpdateDisplay(); // We may need to uncheck a layout item here
			}
			else
				MakeBooklet();
		}

		private void SetModelFromButtons()
		{
			if (_bookletCoverRadio.Checked)
				_model.BookletPortion = PublishModel.BookletPortions.BookletCover;
			else if (_bookletBodyRadio.Checked)
				_model.BookletPortion = PublishModel.BookletPortions.BookletPages;
			// The version we want to upload for web previews is the one that is shown for
			// the _simpleAllPagesRadio button, so pick AllPagesNoBooklet for both of these.
			else if (_simpleAllPagesRadio.Checked || _uploadRadio.Checked)
				_model.BookletPortion = PublishModel.BookletPortions.AllPagesNoBooklet;
			// otherwise, we don't yet know what version to show, so we don't show one.
			else
				_model.BookletPortion = PublishModel.BookletPortions.None;
			_model.UploadMode = _uploadRadio.Checked;
			_model.EpubMode = _epubRadio.Checked;
			_model.ShowCropMarks = false; // obsolete: _showCropMarks.Checked && !_uploadRadio.Checked; // don't want crop-marks on upload PDF
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
			_makePdfBackgroundWorker.RunWorkerAsync();
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
				_previewBox.Show();
				if (!Settings.Default.DontShowPrintNotification)
				{
					using (var dlg = new SamplePrintNotification())
					{
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
