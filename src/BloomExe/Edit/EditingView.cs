using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.web;
using L10NSharp;
using Palaso.Extensions;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageGallery;
using Palaso.UI.WindowsForms.ImageToolbox;
using Gecko;
using TempFile = Palaso.IO.TempFile;
using Bloom.Workspace;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl, IBloomTabArea
	{
		private readonly EditingModel _model;
		private PageListView _pageListView;
		private TemplatePagesView _templatePagesView;
		private readonly CutCommand _cutCommand;
		private readonly CopyCommand _copyCommand;
		private readonly PasteCommand _pasteCommand;
		private readonly UndoCommand _undoCommand;
		private readonly DuplicatePageCommand _duplicatePageCommand;
		private readonly DeletePageCommand _deletePageCommand;
		private Action _pendingMessageHandler;
		private bool _updatingDisplay;
		private Color _enabledToolbarColor = Color.FromArgb(49, 32, 46);
		private Color _disabledToolbarColor = Color.FromArgb(114, 74, 106);
		private bool _visible;

		public delegate EditingView Factory(); //autofac uses this


		public EditingView(EditingModel model, PageListView pageListView, TemplatePagesView templatePagesView,
			CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand, DuplicatePageCommand duplicatePageCommand,
			DeletePageCommand deletePageCommand, NavigationIsolator isolator)
		{
			_model = model;
			_pageListView = pageListView;
			_templatePagesView = templatePagesView;
			_cutCommand = cutCommand;
			_copyCommand = copyCommand;
			_pasteCommand = pasteCommand;
			_undoCommand = undoCommand;
			_duplicatePageCommand = duplicatePageCommand;
			_deletePageCommand = deletePageCommand;
			InitializeComponent();
			_browser1.Isolator = isolator;
			_splitContainer1.Tag = _splitContainer1.SplitterDistance; //save it
			//don't let it grow automatically
//            _splitContainer1.SplitterMoved+= ((object sender, SplitterEventArgs e) => _splitContainer1.SplitterDistance = (int)_splitContainer1.Tag);
			SetupThumnailLists();
			_model.SetView(this);
			_browser1.SetEditingCommands(cutCommand, copyCommand, pasteCommand, undoCommand);

			_browser1.GeckoReady += new EventHandler(OnGeckoReady);

			if (Palaso.PlatformUtilities.Platform.IsMono)
			{
				RepositionButtonsForMono();
				BackgroundColorsForLinux();
			}

			// Adding this renderer prevents a white line from showing up under the components.
			_menusToolStrip.Renderer = new FixedToolStripRenderer();

			//we're giving it to the parent control through the TopBarControls property
			Controls.Remove(_topBarPanel);
		}

#if TooExpensive
		void OnBrowserFocusChanged(object sender, GeckoDomEventArgs e)
		{
			//prevent recursion
			_browser1.WebBrowser.DomFocus -= new EventHandler<GeckoDomEventArgs>(OnBrowserFocusChanged);
			_model.BrowserFocusChanged();
			_browser1.WebBrowser.DomFocus += new EventHandler<GeckoDomEventArgs>(OnBrowserFocusChanged);

		}
#endif

		private void RepositionButtonsForMono()
		{
			// Shift toolstrip controls right to prevent overlapping disable buttons, which causes the
			// overlapped region to not paint.
			var shift = _pasteButton.Left + _pasteButton.Width - _cutButton.Left;
			_cutButton.Left += shift;
			_copyButton.Left += shift;
			_undoButton.Left += shift;
			_duplicatePageButton.Left += shift;
			_deletePageButton.Left += shift;
			_menusToolStrip.Left += shift;
			_topBarPanel.Width = _menusToolStrip.Left + _menusToolStrip.Width + 1;
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

		public Control TopBarControl
		{
			get
			{
				return _topBarPanel;
			}
		}

		/// <summary>
		/// Prevents a white line from appearing below the tool strip
		/// Be careful if using this on Linux; it can have strange side-effects (https://jira.sil.org/browse/BL-509).
		/// </summary>
		public class FixedToolStripRenderer : ToolStripProfessionalRenderer
		{
			protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
			{
				//just don't draw a border
			}
		}

		void ParentForm_Activated(object sender, EventArgs e)
		{
			if (!_visible) //else you get a totally non-responsive Bloom, if you come back to a Bloom that isn't in the Edit tab
				return;

			Debug.WriteLine("EditTab.ParentForm_Activated(): Selecting Browser");
//			Debug.WriteLine("browser focus: "+ (_browser1.Focused ? "true": "false"));
//			Debug.WriteLine("active control: " + ActiveControl.Name);
//			Debug.WriteLine("split container's control: " + _splitContainer1.ActiveControl.Name);
//			Debug.WriteLine("_splitContainer1.ContainsFocus: " + (_splitContainer1.ContainsFocus ? "true" : "false"));
//			Debug.WriteLine("_splitContainer2.ContainsFocus: " + (_splitContainer2.ContainsFocus ? "true" : "false"));
//			Debug.WriteLine("_browser.ContainsFocus: " + (_browser1.ContainsFocus ? "true" : "false"));
//			//focus() made it worse, select has no effect

			/* These two lines are the result of several hours of work. The problem this solves is that when
			 * you're switching between applications (e.g., building a shell book), the browser would highlight
			 * the box you were in, but not really focus on it. So no red border (from the css :focus), and typing/pasting
			 * was erratic.
			 * So now, when we come back to Bloom (this activated event), we *deselect* the browser, then reselect it, and it's happy.
			 */

			_splitContainer1.Select();
			//_browser1.Select();
			_browser1.WebBrowser.Select();

			_editButtonsUpdateTimer.Enabled = Parent != null;
		}

		public Bitmap ToolStripBackground { get; set; }

		private void _handleMessageTimer_Tick(object sender, EventArgs e)
		{
			_handleMessageTimer.Enabled = false;
			_pendingMessageHandler();
			_pendingMessageHandler = null;
		}

		private void OnGeckoReady(object sender, EventArgs e)
		{
#if TooExpensive
			_browser1.WebBrowser.DomFocus += new EventHandler<GeckoDomEventArgs>(OnBrowserFocusChanged);
#endif
			//_browser1.WebBrowser.AddMessageEventListener("PreserveHtmlOfElement", elementHtml => _model.PreserveHtmlOfElement(elementHtml));
		}

		private void OnShowBookMetadataEditor()
		{
			try
			{
				if (!_model.CanEditCopyrightAndLicense)
				{
					MessageBox.Show(LocalizationManager.GetString("EditTab.CannotChangeCopyright", "Sorry, the copyright and license for this book cannot be changed."));
					return;
				}

				_model.SaveNow();//in case we were in this dialog already and made changes, which haven't found their way out to the Book yet
				Metadata metadata = _model.CurrentBook.GetLicenseMetadata();
				if (metadata.License is NullLicense && string.IsNullOrWhiteSpace(metadata.CopyrightNotice))
				{
					//looks like the first time. Nudge them with a nice default license.
					metadata.License = new CreativeCommonsLicense(true, true, CreativeCommonsLicense.DerivativeRules.Derivatives);
				}

				Logger.WriteEvent("Showing Metadata Editor Dialog");
				using (var dlg = new Palaso.UI.WindowsForms.ClearShare.WinFormsUI.MetadataEditorDialog(metadata))
				{
					dlg.ShowCreator = false;
					if (DialogResult.OK == dlg.ShowDialog())
					{
						ChangeBookMetadata(dlg.Metadata);
					}
				}
				Logger.WriteMinorEvent("Emerged from Metadata Editor Dialog");
			}
			catch (Exception error)
			{
#if DEBUG
				throw;
#endif
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem recording your changes to the copyright and license.");
			}
		}

		private void ChangeBookMetadata(Metadata metadata)
		{
			string imagePath = _model.CurrentBook.FolderPath.CombineForPath("license.png");
			if (File.Exists(imagePath))
				File.Delete(imagePath);
			Image licenseImage = metadata.License.GetImage();
			if (licenseImage != null)
			{
				licenseImage.Save(imagePath);
			}
			else if (File.Exists(imagePath))
			{
				File.Delete(imagePath);
			}

			// Both LicenseNotes and Copyright By could have user-entered html characters that need escaping.
			var copyright = metadata.CopyrightNotice;
			metadata.CopyrightNotice = copyright;
			//NB: we are mapping "RightsStatement" (which comes from XMP-dc:Rights) to "LicenseNotes" in the html.
			//note that the only way currently to recognize a custom license is that RightsStatement is non-empty while description is empty
			var rights = metadata.License.RightsStatement;
			metadata.License.RightsStatement = rights;
			string idOfLanguageUsed;

			string description = metadata.License.GetDescription(_model.LicenseDescriptionLanguagePriorities, out idOfLanguageUsed).Replace("'", "\\'");
			string licenseImageName = licenseImage == null ? string.Empty : "license.png";
			string result =
				string.Format(
					"{{ copyright: '{0}', licenseImage: '{1}', licenseUrl: '{2}',  licenseNotes: '{3}', licenseDescription: '{4}' }}",
					MakeJavaScriptContent(metadata.CopyrightNotice),
					licenseImageName,
					metadata.License.Url, MakeJavaScriptContent(rights), description);
			_browser1.RunJavaScript("if (calledByCSharp) { calledByCSharp.setCopyrightAndLicense(" + result + "); }");

			//ok, so the the dom for *that page* is updated, but if the page doesn't display some of those values, they won't get
			//back to the data div in the actual html file even when the page is read and saved, because individual pages don't
			//have the data div.
			_model.CurrentBook.UpdateLicenseMetdata(metadata);
			_model.SaveNow();
			_model.RefreshDisplayOfCurrentPage(); //the cleanup() that is part of Save removes qtips, so let' redraw everything
		}

		// Make a string which, when compiled as a JavaScript literal embedded in single quotes, will produce the original.
		private string MakeJavaScriptContent(string input)
		{
			if (input == null)
				return "";
			// Order is important here...we do NOT want to double the backslash we insert before a single quote.
			// Review: is the NewLine replace safe for Linux?
			return input.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n");
		}

		private void SetupThumnailLists()
		{
			_pageListView.Dock = DockStyle.Fill;
			_pageListView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			_templatePagesView.BackColor = _pageListView.BackColor = _splitContainer1.Panel1.BackColor;
			_splitContainer1.Panel1.Controls.Add(_pageListView);

			_templatePagesView.Dock = DockStyle.Fill;
			_templatePagesView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		}


		private void SetTranslationPanelVisibility()
		{
			_splitContainer2.Panel2.Controls.Clear();
			_splitTemplateAndSource.Panel1.Controls.Clear();
			_splitTemplateAndSource.Panel2.Controls.Clear();

			if (_model.ShowTemplatePanel) //Templates only
			{
				_splitContainer2.Panel2Collapsed = false;
				_splitContainer2.Panel2.Controls.Add(_templatePagesView);
			}
			else
			{
				_splitContainer2.Panel2Collapsed = true;
			}
		}

		void VisibleNowAddSlowContents(object sender, EventArgs e)
		{
			//TODO: this is causing green boxes when you quit while it is still working
			//we should change this to a proper background task, with good
			//cancellation in case we switch documents.  Note we may also switch
			//to some other way of making the thumbnails... e.g. it would be nice
			//to have instant placeholders, with thumbnails later.

			Application.Idle -= new EventHandler(VisibleNowAddSlowContents);

			CheckFontAvailablility();

			Cursor = Cursors.WaitCursor;
			_model.ViewVisibleNowDoSlowStuff();
			Cursor = Cursors.Default;
		}

		private void CheckFontAvailablility()
		{
			var fontMessage = _model.GetFontAvailabilityMessage();
			if (!string.IsNullOrEmpty(fontMessage))
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(),
					fontMessage);
			}
		}


		/// <summary>
		/// this is called by our model, as a result of a "SelectedTabChangedEvent". So it's a lot more reliable than the normal winforms one.
		/// </summary>
		public void OnVisibleChanged(bool visible)
		{
			_visible = visible;
			if (visible)
			{
				if (_model.GetBookHasChanged())
				{
					//now we're doing it based on the focus textarea: ShowOrHideSourcePane(_model.ShowTranslationPanel);
					SetTranslationPanelVisibility();
					//even before showing, we need to clear some things so the user doesn't see the old stuff
					_pageListView.Clear();
					_templatePagesView.Clear();
				}
				Application.Idle += new EventHandler(VisibleNowAddSlowContents);
				Cursor = Cursors.WaitCursor;
				Logger.WriteEvent("Entered Edit Tab");
			}
			else
			{
				Application.Idle -= new EventHandler(VisibleNowAddSlowContents); //make sure
				_browser1.Navigate("about:blank", false); //so we don't see the old one for moment, the next time we open this tab
			}
		}

		public void UpdateSingleDisplayedPage(IPage page)
		{
			if (!_model.Visible)
			{
				return;
			}

			if (_model.HaveCurrentEditableBook)
			{
				_pageListView.SelectThumbnailWithoutSendingEvent(page);
				_model.SetupServerWithCurrentPageIframeContents();
				HtmlDom domForCurrentPage = _model.GetXmlDocumentForCurrentPage();
				var dom = _model.GetXmlDocumentForEditScreenWebPage();
				_browser1.Focus();
				_browser1.Navigate(dom.RawDom, domForCurrentPage.RawDom);
				_pageListView.Focus();
				_browser1.Focus();
				// So far, the most reliable way I've found to detect that the page is fully loaded and we can call
				// initialize() is the ReadyStateChanged event (combined with checking that ReadyState is "complete").
				// This works for most pages but not all...some (e.g., the credits page in a basic book) seem to just go on
				// being "interactive". As a desperate step I tried looking for DocumentCompleted (which fires too soon and often),
				// but still, we never get one where the ready state is completed. This page just stays 'interactive'.
				// A desperate expedient would be to try running some Javascript to test whether the 'initialize' function
				// has actually loaded. If you try that, be careful...this function seems to be used in cases where that
				// never happens.
				_browser1.WebBrowser.DocumentCompleted += WebBrowser_ReadyStateChanged;
				_browser1.WebBrowser.ReadyStateChange += WebBrowser_ReadyStateChanged;
			}
			UpdateDisplay();
		}

		void WebBrowser_ReadyStateChanged(object sender, EventArgs e)
		{
			if (_browser1.WebBrowser.Document.ReadyState != "complete")
				return; // Keep receiving until it is complete.
			_browser1.WebBrowser.ReadyStateChange -= WebBrowser_ReadyStateChanged; // just do this once
			_browser1.WebBrowser.DocumentCompleted -= WebBrowser_ReadyStateChanged;
			_model.DocumentCompleted();
		}

		public void AddMessageEventListener(string eventName, Action<string> action)
		{
			_browser1.AddMessageEventListener(eventName, action);
		}

		public void UpdateTemplateList()
		{
			_templatePagesView.Update();
		}

		public void UpdatePageList(bool emptyThumbnailCache)
		{
			if (emptyThumbnailCache)
				_pageListView.EmptyThumbnailCache();
			_pageListView.SetBook(_model.CurrentBook);
		}

		internal string RunJavaScript(string script)
		{
			return _browser1.RunJavaScript(script);
		}

		private void _browser1_OnBrowserClick(object sender, EventArgs e)
		{
			var ge = e as DomEventArgs;
			if (ge == null || ge.Target == null)
				return; //I've seen this happen

			var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			if (target.ClassName.Contains("sourceTextTab"))
			{
				RememberSourceTabChoice(target);
				return;
			}
			if (target.ClassName.Contains("changeImageButton"))
				OnChangeImage(ge);
			if (target.ClassName.Contains("pasteImageButton"))
				OnPasteImage(ge);
			if (target.ClassName.Contains("editMetadataButton"))
				OnEditImageMetdata(ge);

			var anchor = target as Gecko.DOM.GeckoAnchorElement;
			if (anchor != null && anchor.Href != "" && anchor.Href != "#")
			{
				if (anchor.Href.Contains("bookMetadataEditor"))
				{
					OnShowBookMetadataEditor();
					ge.Handled = true;
					return;
				}

				// Let Gecko handle hrefs that are explicitly tagged "javascript"
				if (anchor.Href.StartsWith("javascript")) //tied to, for example, data-functionOnHintClick="ShowTopicChooser()"
				{
					ge.Handled = false; // let gecko handle it
					return;
				}

				if (anchor.Href.ToLower().StartsWith("http")) //will cover https also
				{
					// do not open in external browser if localhost...except for some links in the accordion
					if (anchor.Href.ToLowerInvariant().StartsWith(ServerBase.PathEndingInSlash))
					{
						ge.Handled = false; // let gecko handle it
						return;
					}

					Process.Start(anchor.Href);
					ge.Handled = true;
					return;
				}
				if (anchor.Href.ToLower().StartsWith("file")) //source bubble tabs
				{
					ge.Handled = false; //let gecko handle it
					return;
				}
				else
				{
					ErrorReport.NotifyUserOfProblem("Bloom did not understand this link: " + anchor.Href);
					ge.Handled = true;
				}

			}
//			if (ge.Target.ClassName.Contains("bloom-metaData") || (ge.Target.ParentElement!=null && ge.Target.ParentElement.ClassName.Contains("bloom-metaData")))
		}


		private void RememberSourceTabChoice(GeckoHtmlElement target)
		{
			//"<a class="sourceTextTab" href="#tpi">Tok Pisin</a>"
			var start = 1 + target.OuterHtml.IndexOf("#");
			var end = target.OuterHtml.IndexOf("\">");
			Settings.Default.LastSourceLanguageViewed = target.OuterHtml.Substring(start, end - start);
		}


		private void OnEditImageMetdata(DomEventArgs ge)
		{
			var imageElement = GetImageNode(ge);
			if (imageElement == null)
				return;
			string fileName = imageElement.GetAttribute("src").Replace("%20", " ");

			var path = Path.Combine(_model.CurrentBook.FolderPath, fileName);
			using (var imageInfo = PalasoImage.FromFile(path))
			{
				bool looksOfficial = imageInfo.Metadata != null && !string.IsNullOrEmpty(imageInfo.Metadata.CollectionUri);
				if (looksOfficial)
				{
					MessageBox.Show(imageInfo.Metadata.GetSummaryParagraph("en"));
					return;
				}
				Logger.WriteEvent("Showing Metadata Editor For Image");
				using (var dlg = new Palaso.UI.WindowsForms.ClearShare.WinFormsUI.MetadataEditorDialog(imageInfo.Metadata))
				{
					if (DialogResult.OK == dlg.ShowDialog())
					{
						imageInfo.Metadata = dlg.Metadata;
						imageInfo.SaveUpdatedMetadataIfItMakesSense();
						imageInfo.Metadata.StoreAsExemplar(Metadata.FileCategory.Image);
						//update so any overlays on the image are brough up to data
						var editor = new PageEditingModel();
						editor.UpdateMetdataAttributesOnImgElement(imageElement, imageInfo);

						var answer = MessageBox.Show(LocalizationManager.GetString("EditTab.copyImageIPMetdataQuestion","Copy this information to all other pictures in this book?", "get this after you edit the metadata of an image"), LocalizationManager.GetString("EditTab.titleOfCopyIPToWholeBooksDialog","Picture Intellectual Property Information"),  MessageBoxButtons.YesNo, MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
						if (answer == DialogResult.Yes)
						{
							Cursor = Cursors.WaitCursor;
							try
							{
								_model.CopyImageMetadataToWholeBook(dlg.Metadata);
								// There might be more than one image on this page. Update overlays.
								_model.RefreshDisplayOfCurrentPage();
							}
							catch (Exception e)
							{
								ErrorReport.NotifyUserOfProblem(e, "There was a problem copying the metadata to all the images.");
							}
							Cursor = Cursors.Default;
						}
					}
				}
			}

			//_model.SaveNow();
			//doesn't work: _browser1.WebBrowser.Reload();
		}

		private void OnPasteImage(DomEventArgs ge)
		{
			if (!_model.CanChangeImages())
			{
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.CantPasteImageLocked","Sorry, this book is locked down so that images cannot be changed."));
				return;
			}

			PalasoImage clipboardImage = null;
			try
			{
				clipboardImage = GetImageFromClipboard();
				if (clipboardImage == null)
				{
					MessageBox.Show(
						LocalizationManager.GetString("EditTab.NoImageFoundOnClipboard","Before you can paste an image, copy one onto your 'clipboard', from another program."));
					return;
				}


				var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
				if (target.ClassName.Contains("licenseImage"))
					return;

				var imageElement = GetImageNode(ge);
				if (imageElement == null)
					return;
				Cursor = Cursors.WaitCursor;

				//nb: Taglib# requires an extension that matches the file content type.
				if (ImageUtils.AppearsToBeJpeg(clipboardImage))
				{
					if(ShouldBailOutBecauseUserAgreedNotToUseJpeg(clipboardImage))
						return;
					Logger.WriteMinorEvent("[Paste Image] Pasting jpeg image {0}", clipboardImage.OriginalFilePath);
					_model.ChangePicture(imageElement, clipboardImage, new NullProgress());
				}
				else
				{
					//At this point, it could be a bmp, tiff, or PNG. We want it to be a PNG.
					if(clipboardImage.OriginalFilePath == null) //they pasted an image, not a path
					{
						Logger.WriteMinorEvent("[Paste Image] Pasting image directly from clipboard (e.g. screenshot)");
						_model.ChangePicture(imageElement, clipboardImage, new NullProgress());
					}
					//they pasted a path to a png
					else if(Path.GetExtension(clipboardImage.OriginalFilePath).ToLower() == ".png")
					{
						Logger.WriteMinorEvent("[Paste Image] Pasting png file {0}", clipboardImage.OriginalFilePath);
						_model.ChangePicture(imageElement, clipboardImage, new NullProgress());
					}
					else // they pasted a path to some other bitmap format
					{
						var pathToPngVersion = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(clipboardImage.FileName) + ".png");
						Logger.WriteMinorEvent("[Paste Image] Saving {0} ({1}) as {2} and converting to PNG", clipboardImage.FileName,
							clipboardImage.OriginalFilePath, pathToPngVersion);
						if (File.Exists(pathToPngVersion))
						{
							File.Delete(pathToPngVersion);
						}
						using(var temp = TempFile.TrackExisting(pathToPngVersion))
						{
							clipboardImage.Image.Save(pathToPngVersion, ImageFormat.Png);

							using (var palasoImage = PalasoImage.FromFile(temp.Path))
							{
								_model.ChangePicture(imageElement, palasoImage, new NullProgress());
							}
						}
					}
				}
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "The program had trouble getting an image from the clipboard.");
			}
			finally
			{
				if (clipboardImage != null)
					clipboardImage.Dispose();
			}
			Cursor = Cursors.Default;
		}

		private static PalasoImage GetImageFromClipboard()
		{
			return BloomClipboard.GetImageFromClipboard();
		}


		private static GeckoHtmlElement GetImageNode(DomEventArgs ge)
		{
			GeckoHtmlElement imageElement = null;
			var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			foreach (var n in target.Parent.ChildNodes)
			{
				imageElement = n as GeckoHtmlElement;
				if (imageElement != null && imageElement.TagName.ToLower() == "img")
				{
					return imageElement;
				}
			}

			Debug.Fail("Could not find image element");
			return null;
		}

		/// <summary>
		/// Returns true if it is either: a) OK to change images, or b) user overrides
		/// Returns false if user cancels message box
		/// </summary>
		/// <param name="imagePath"></param>
		/// <returns></returns>
		private bool CheckIfLockedAndWarn(string imagePath)
		{
			// Enhance: we may want to reinstate some sort of (disableable) warning when they edit a picture while translating.
			// Original comment:  this would let them set it once without us bugging them, but after that if they
			//go to change it, we would bug them because we don't have a way of knowing that it was a placeholder before.
			//if (!imagePath.ToLower().Contains("placeholder")  //always allow them to put in something over a placeholder
			//	&& !_model.CanChangeImages())
			//{
			//	if (DialogResult.Cancel == MessageBox.Show(LocalizationManager.GetString("EditTab.ImageChangeWarning", "This book is locked down as shell. Are you sure you want to change the picture?"), LocalizationManager.GetString("EditTab.ChangeImage", "Change Image"), MessageBoxButtons.OKCancel))
			//	{
			//		return false;
			//	}
			//}
			return true;
		}

		private void OnChangeImage(DomEventArgs ge)
		{
			var imageElement = GetImageNode(ge);
			if (imageElement == null)
				return;
			string currentPath = imageElement.GetAttribute("src").Replace("%20", " ");

			if (!CheckIfLockedAndWarn(currentPath))
				return;
			var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			if (target.ClassName.Contains("licenseImage"))
				return;

			Cursor = Cursors.WaitCursor;

			var imageInfo = new PalasoImage();
			var existingImagePath = Path.Combine(_model.CurrentBook.FolderPath, currentPath);

			//don't send the placeholder to the imagetoolbox... we get a better user experience if we admit we don't have an image yet.
			if (!currentPath.ToLower().Contains("placeholder") && File.Exists(existingImagePath))
			{
				try
				{
					imageInfo = PalasoImage.FromFile(existingImagePath);
				}
				catch (Exception e)
				{
					Logger.WriteMinorEvent("Not able to load image for ImageToolboxDialog: " + e.Message);
				}
			}
			Logger.WriteEvent("Showing ImageToolboxDialog Editor Dialog");
			// Deep in the ImageTooboxDialog, when the user asks to see images from the ArtOfReading,
			// We need to use the Gecko version of the thumbnail viewer, since the original ListView
			// one has a sticky scroll bar in applications that are using Gecko.
			ThumbnailViewer.UseWebViewer = true;
			using (var dlg = new ImageToolboxDialog(imageInfo, null))
			{
				if (DialogResult.OK == dlg.ShowDialog())
				{

					// var path = MakePngOrJpgTempFileForImage(dlg.ImageInfo.Image);
					try
					{
						if(ShouldBailOutBecauseUserAgreedNotToUseJpeg(dlg.ImageInfo))
							return;
						_model.ChangePicture(imageElement, dlg.ImageInfo, new NullProgress());
					}
					catch (System.IO.IOException error)
					{
						ErrorReport.NotifyUserOfProblem(error, error.Message);
					}
					catch (ApplicationException error)
					{
						ErrorReport.NotifyUserOfProblem(error, error.Message);
					}
					catch (Exception error)
					{
						ErrorReport.NotifyUserOfProblem(error, "Bloom had a problem including that image");
					}
				}
			}
			Logger.WriteMinorEvent("Emerged from ImageToolboxDialog Editor Dialog");
			Cursor = Cursors.Default;
		}

		private bool ShouldBailOutBecauseUserAgreedNotToUseJpeg(PalasoImage imageInfo)
		{
			if(ImageUtils.AppearsToBeJpeg(imageInfo) && JpegWarningDialog.ShouldWarnAboutJpeg(imageInfo.Image))
			{
				using(var jpegDialog = new JpegWarningDialog())
				{
					return jpegDialog.ShowDialog() == DialogResult.Cancel;
				}
			}
			else
			{
				return false;
			}
		}

		public void UpdateThumbnailAsync(IPage page)
		{
			_pageListView.UpdateThumbnailAsync(page);
		}

		/// <summary>
		/// this started as an experiment, where our textareas were not being read when we saved because of the need
		/// to change the picture
		/// </summary>
		public void CleanHtmlAndCopyToPageDom()
		{
			RunJavaScript("if (calledByCSharp) { calledByCSharp.removeSynphonyMarkup(); }");
			_browser1.ReadEditableAreasNow();
		}

		private void _copyButton_Click(object sender, EventArgs e)
		{
			_copyCommand.Execute();
		}

		private void _pasteButton_Click(object sender, EventArgs e)
		{
			_pasteCommand.Execute();
		}

		public void UpdateDisplay()
		{
			try
			{
				_updatingDisplay = true;

				_contentLanguagesDropdown.DropDownItems.Clear();
				foreach (var l in _model.ContentLanguages)
				{
					ToolStripMenuItem item = (ToolStripMenuItem) _contentLanguagesDropdown.DropDownItems.Add(l.ToString());
					item.Tag = l;
					item.Enabled = !l.Locked;
					item.Checked = l.Selected;
					item.CheckOnClick = true;
					item.CheckedChanged += new EventHandler(OnContentLanguageDropdownItem_CheckedChanged);
				}

				_layoutChoices.DropDownItems.Clear();
				var layout = _model.GetCurrentLayout();
				var layoutChoices = _model.GetLayoutChoices();
				foreach (var l in layoutChoices)
				{
					var text = LocalizationManager.GetDynamicString("Bloom", "LayoutChoices." + l.ToString(), l.ToString());
					ToolStripMenuItem item = (ToolStripMenuItem) _layoutChoices.DropDownItems.Add(text);
					item.Tag = l;
					//we don't allow the split options here
					if (l.ElementDistribution == Book.Layout.ElementDistributionChoices.SplitAcrossPages)
					{
						item.Enabled = false;
						item.ToolTipText = LocalizationManager.GetString("EditTab.layoutInPublishTabOnlyNotice","This option is only available in the Publish tab.");
					}
					item.Text = text;
					item.Click += new EventHandler(OnPaperSizeAndOrientationMenuClick);
				}

				if (layoutChoices.Count() < 2)
				{
					ToolStripMenuItem item = (ToolStripMenuItem)_layoutChoices.DropDownItems.Add(LocalizationManager.GetString("EditTab.noOtherLayouts","There are no other layout options for this template.","Show in the layout chooser dropdown of the edit tab, if there was only a single layout choice"));
					item.Tag = null;
					item.Enabled = false;
				}

				_layoutChoices.Text = LocalizationManager.GetDynamicString("Bloom", "LayoutChoices." + layout, layout.ToString());

				switch (_model.NumberOfDisplayedLanguages)
				{
					case 1:
						_contentLanguagesDropdown.Text = LocalizationManager.GetString("EditTab.monolingual", "One Language", "Shown in edit tab multilingualism chooser, for monolingual mode, one language per page");
						break;
					case 2:
						_contentLanguagesDropdown.Text = LocalizationManager.GetString("EditTab.bilingual", "Two Languages", "Shown in edit tab multilingualism chooser, for bilingual mode, 2 languages per page");
						break;
					case 3:
						_contentLanguagesDropdown.Text = LocalizationManager.GetString("EditTab.trilingual", "Three Languages", "Shown in edit tab multilingualism chooser, for trilingual mode, 3 languages per page");
						break;
				}

				//I'm surprised that L10NSharp (in aug 2014) doesn't automatically make tooltips localizable, but this is how I got it to work
				_layoutChoices.ToolTipText = LocalizationManager.GetString("EditTab.PageSizeAndOrientation.Tooltip",
					//_layoutChoices.ToolTipText); doesn't work because the scanner needs literals
					"Choose a page size and orientation");
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem updating the edit display.");
			}
			finally
			{
				_updatingDisplay = false;
			}
		}

		void OnPaperSizeAndOrientationMenuClick(object sender, EventArgs e)
		{
			var item = (ToolStripMenuItem) sender;
			_model.SetLayout((Layout) item.Tag);
			UpdateDisplay();
		}

		void OnContentLanguageDropdownItem_CheckedChanged(object sender, EventArgs e)
		{
			if (_updatingDisplay)
				return;
			var item = (ToolStripMenuItem) sender;
			((EditingModel.ContentLanguage) item.Tag).Selected = item.Checked;

			_model.ContentLanguagesSelectionChanged();
		}

		public void UpdateEditButtons()
		{
			_browser1.UpdateEditButtons();
			UpdateButtonEnabled(_cutButton, _cutCommand);
			UpdateButtonEnabled(_copyButton, _copyCommand);
			UpdateButtonEnabled(_pasteButton, _pasteCommand);
			UpdateButtonEnabled(_undoButton, _undoCommand);
			UpdateButtonEnabled(_duplicatePageButton, _duplicatePageCommand);
			UpdateButtonEnabled(_deletePageButton, _deletePageCommand);
		}

		public void UpdateButtonLocalizations()
		{
			// This seems to be the only way to ensure that BetterToolTip updates itself
			// with new localization strings.
			CycleEditButtons();
		}

		private void CycleEditButtons()
		{
			_browser1.UpdateEditButtons();
			CycleOneButton(_cutButton, _cutCommand);
			CycleOneButton(_copyButton, _copyCommand);
			CycleOneButton(_pasteButton, _pasteCommand);
			CycleOneButton(_undoButton, _undoCommand);
			CycleOneButton(_duplicatePageButton, _duplicatePageCommand);
			CycleOneButton(_deletePageButton, _deletePageCommand);
		}

		private void CycleOneButton(Button button, Command command)
		{
			var isEnabled = command.Enabled;
			button.Enabled = !isEnabled;
			UpdateButtonEnabled(button, command);
		}

		private void UpdateButtonEnabled(Button button, Command command)
		{
			button.Enabled = command != null && command.Enabled;
			//doesn't work because the forecolor is ignored when disabled...
			button.ForeColor = button.Enabled ? _enabledToolbarColor : _disabledToolbarColor; //.DimGray;
			button.Invalidate();
		}

		protected override void OnParentChanged(EventArgs e)
		{
			base.OnParentChanged(e);
			_editButtonsUpdateTimer.Enabled = Parent != null;
		}

		private void _editButtonsUpdateTimer_Tick(object sender, EventArgs e)
		{
			UpdateEditButtons();
		}

		private void _cutButton_Click(object sender, EventArgs e)
		{
			_cutCommand.Execute();
		}

		private void _undoButton_Click(object sender, EventArgs e)
		{
			_undoCommand.Execute();
		}

		public void ClearOutDisplay()
		{
			_pageListView.Clear();
			_browser1.Navigate("about:blank", false);
		}

		private void _deletePageButton_Click_1(object sender, EventArgs e)
		{
			if (ConfirmRemovePageDialog.Confirm())
			{
				_deletePageCommand.Execute();
			}
		}

		private void _duplicatePageButton_Click(object sender, EventArgs e)
		{
			_duplicatePageCommand.Execute();
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			//Why the check for null? In bl-283, user had been in settings dialog, which caused a closing down, but something
			//then did a callback to this view, such that ParentForm was null, and this died
			Debug.Assert(ParentForm != null);
			if (ParentForm != null)
			{
				ParentForm.Activated += new EventHandler(ParentForm_Activated);
				ParentForm.Deactivate += (sender, e1) => {
					_editButtonsUpdateTimer.Enabled = false;
				};
			}
		}

		public string HelpTopicUrl
		{
			get { return "/Tasks/Edit_tasks/Edit_tasks_overview.htm"; }
		}

		/// <summary>
		/// Prevent navigation while a dialog box is showing in the browser control
		/// </summary>
		/// <param name="isModal"></param>
		internal void SetModalState(bool isModal)
		{
			_templatePagesView.Enabled = !isModal;
			_pageListView.Enabled = !isModal;
		}
	}
}