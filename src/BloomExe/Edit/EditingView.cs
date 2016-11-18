using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.Api;
using L10NSharp;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageGallery;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Miscellaneous;
using Gecko;
using TempFile = SIL.IO.TempFile;
using Bloom.Workspace;
using Gecko.DOM;
using SIL.IO;
using SIL.Windows.Forms.Widgets;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl, IBloomTabArea
	{
		private readonly EditingModel _model;
		private PageListView _pageListView;
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

		public EditingView(EditingModel model, PageListView pageListView,
			CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand,
			DuplicatePageCommand duplicatePageCommand,
			DeletePageCommand deletePageCommand, NavigationIsolator isolator, ControlKeyEvent controlKeyEvent)
		{
			_model = model;
			_pageListView = pageListView;
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

			_browser1.ControlKeyEvent = controlKeyEvent;

			if(SIL.PlatformUtilities.Platform.IsMono)
			{
				RepositionButtonsForMono();
				BackgroundColorsForLinux();
			}

			controlKeyEvent.Subscribe(HandleControlKeyEvent);

			// Adding this renderer prevents a white line from showing up under the components.
			_menusToolStrip.Renderer = new FixedToolStripRenderer();

			//we're giving it to the parent control through the TopBarControls property
			Controls.Remove(_topBarPanel);
			SetupBrowserContextMenu();
		}

		private void SetupBrowserContextMenu()
		{
			// currently nothing to do.
		}

		private void HandleControlKeyEvent(object keyData)
		{
			if(_visible && (Keys) keyData == (Keys.Control | Keys.N))
			{
				// This is for now a TODO
				//_model.HandleAddNewPageKeystroke(null);
			}
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

		private void BackgroundColorsForLinux()
		{

			var bmp = new Bitmap(_menusToolStrip.Width, _menusToolStrip.Height);
			using(var g = Graphics.FromImage(bmp))
			{
				using(var b = new SolidBrush(_menusToolStrip.BackColor))
				{
					g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
				}
			}
			_menusToolStrip.BackgroundImage = bmp;
		}

		public Control TopBarControl
		{
			get { return _topBarPanel; }
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
			if(!_visible) //else you get a totally non-responsive Bloom, if you come back to a Bloom that isn't in the Edit tab
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
				if(!_model.CanEditCopyrightAndLicense)
				{
					MessageBox.Show(LocalizationManager.GetString("EditTab.CannotChangeCopyright",
						"Sorry, the copyright and license for this book cannot be changed."));
					return;
				}

				_model.SaveNow();
				//in case we were in this dialog already and made changes, which haven't found their way out to the Book yet

				var metadata = _model.CurrentBook.GetLicenseMetadata();

				Logger.WriteEvent("Showing Metadata Editor Dialog");
				using(var dlg = new SIL.Windows.Forms.ClearShare.WinFormsUI.MetadataEditorDialog(metadata))
				{
					dlg.ShowCreator = false;
					if(DialogResult.OK == dlg.ShowDialog())
					{
						Logger.WriteEvent("For BL-3166 Investigation");
						if(metadata.License == null)
						{
							Logger.WriteEvent("old LicenseUrl was null ");
						}
						else
						{
							Logger.WriteEvent("old LicenseUrl was " + metadata.License.Url);
						}
						if(dlg.Metadata.License == null)
						{
							Logger.WriteEvent("new LicenseUrl was null ");
						}
						else
						{
							Logger.WriteEvent("new LicenseUrl: " + dlg.Metadata.License.Url);
						}

						_model.ChangeBookLicenseMetaData(dlg.Metadata);
					}
				}
				Logger.WriteMinorEvent("Emerged from Metadata Editor Dialog");
			}
			catch(Exception error)
			{
				// Throwing this exception is causing it to be swallowed.  It results in the web browser just showing a blank white page, but no
				// message is displayed and no exception is caught by the debugger.
				//#if DEBUG
				//				throw;
				//#endif
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error,
					"There was a problem recording your changes to the copyright and license.");
			}
		}

		private void SetupThumnailLists()
		{
			_pageListView.Dock = DockStyle.Fill;
			_pageListView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			_splitContainer1.Panel1.Controls.Add(_pageListView);
		}

		// TODO: this _splitContainer2 could be eliminated now that we no longer have the TemplatePagesView
		private void SetTranslationPanelVisibility()
		{
			_splitContainer2.Panel2.Controls.Clear();
			_splitTemplateAndSource.Panel1.Controls.Clear();
			_splitTemplateAndSource.Panel2.Controls.Clear();
			_splitContainer2.Panel2Collapsed = true; // used to hold TemplatesPagesView
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

			AddMessageEventListener("setModalStateEvent", SetModalState);
			Cursor = Cursors.Default;
		}


		private void CheckFontAvailablility()
		{
			var fontMessage = _model.GetFontAvailabilityMessage();
			if(!string.IsNullOrEmpty(fontMessage))
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(),
					fontMessage);
			}
		}


		/// <summary>
		/// this is called by our model, as a result of a "SelectedTabChangedEvent". So it's a lot more reliable than the normal winforms one.
		/// </summary>
		public void OnVisibleChanged(bool visible)
		{
			_visible = visible;
			if(visible)
			{
				if(_model.GetBookHasChanged())
				{
					//now we're doing it based on the focus textarea: ShowOrHideSourcePane(_model.ShowTranslationPanel);
					SetTranslationPanelVisibility();
					//even before showing, we need to clear some things so the user doesn't see the old stuff
					_pageListView.Clear();
				}
				Application.Idle += new EventHandler(VisibleNowAddSlowContents);
				Cursor = Cursors.WaitCursor;
				Logger.WriteEvent("Entered Edit Tab");
			}
			else
			{
				RemoveMessageEventListener("setModalStateEvent");
				Application.Idle -= new EventHandler(VisibleNowAddSlowContents); //make sure
				_browser1.Navigate("about:blank", false); //so we don't see the old one for moment, the next time we open this tab
			}
		}

		public void UpdateSingleDisplayedPage(IPage page)
		{
			if(!_model.Visible)
			{
				return;
			}

			if(_model.HaveCurrentEditableBook)
			{
#if MEMORYCHECK
	// Check memory for the benefit of developers.
				SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "EditingView - about to change the page", false);
#endif
				_pageListView.SelectThumbnailWithoutSendingEvent(page);
				_model.SetupServerWithCurrentPageIframeContents();
				HtmlDom domForCurrentPage = _model.GetXmlDocumentForCurrentPage();
				var dom = _model.GetXmlDocumentForEditScreenWebPage();
				_model.RemoveStandardEventListeners();
				_browser1.Navigate(dom, domForCurrentPage, setAsCurrentPageForDebugging: true);
				_model.CheckForBL2364("navigated to page");
				_pageListView.Focus();
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
#if __MonoCS__
				// On Linux/Mono, the user can click between pages too fast in Edit mode, resulting
				// in a warning dialog popping up.  I've never seen this happen on Windows, but it's
				// happening fairly often on Linux when I just try to move around a book.  The fix
				// here is to set a flag that page selection is still processing and block any
				// further page selecting until the current page has finished loading.
				_model.PageSelectionStarted();
				_browser1.WebBrowser.DocumentCompleted += WebBrowser_DocumentCompleted;
#endif
			}
			UpdateDisplay();
		}

#if __MonoCS__
		/// <summary>
		/// Flag the PageSelection object that the current (former?) page selection has completed,
		/// so it's safe to select another page now.
		/// </summary>
		void WebBrowser_DocumentCompleted(object sender, EventArgs e)
		{
			_model.PageSelectionFinished();
			_browser1.WebBrowser.DocumentCompleted -= WebBrowser_DocumentCompleted;
		}
#endif

		void WebBrowser_ReadyStateChanged(object sender, EventArgs e)
		{
			if(_browser1.WebBrowser.Document.ReadyState != "complete")
				return; // Keep receiving until it is complete.
			_browser1.WebBrowser.ReadyStateChange -= WebBrowser_ReadyStateChanged; // just do this once
			_browser1.WebBrowser.DocumentCompleted -= WebBrowser_ReadyStateChanged;
			ChangingPages = false;
			_model.DocumentCompleted();
			_browser1.Focus(); //fix BL-3078 No Initial Insertion Point when any page shown

#if MEMORYCHECK
	// Check memory for the benefit of developers.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "EditingView - page change completed", false);
#endif
		}

		public void AddMessageEventListener(string eventName, Action<string> action)
		{
			_browser1.AddMessageEventListener(eventName, action);
		}

		public void RemoveMessageEventListener(string eventName)
		{
			_browser1.RemoveMessageEventListener(eventName);
		}

		public void UpdatePageList(bool emptyThumbnailCache)
		{
			if(emptyThumbnailCache)
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
			if(ge == null || ge.Target == null)
				return; //I've seen this happen

			var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			if(target.ClassName.Contains("sourceTextTab"))
			{
				RememberSourceTabChoice(target);
				return;
			}
			if(target.ClassName.Contains("changeImageButton"))
				OnChangeImage(ge);
			if(target.ClassName.Contains("pasteImageButton"))
				OnPasteImage(ge);
			if(target.ClassName.Contains("cutImageButton"))
				OnCutImage(ge);
			if(target.ClassName.Contains("copyImageButton"))
				OnCopyImage(ge);
			if(target.ClassName.Contains("editMetadataButton"))
				OnEditImageMetdata(ge);

			var anchor = target as Gecko.DOM.GeckoAnchorElement;
			if(anchor != null && anchor.Href != "" && anchor.Href != "#")
			{
				if(anchor.Href.Contains("bookMetadataEditor"))
				{
					OnShowBookMetadataEditor();
					ge.Handled = true;
					return;
				}

				// Let Gecko handle hrefs that are explicitly tagged "javascript"
				if(anchor.Href.StartsWith("javascript")) //tied to, for example, data-functionOnHintClick="ShowTopicChooser()"
				{
					ge.Handled = false; // let gecko handle it
					return;
				}

				if(anchor.Href.ToLowerInvariant().StartsWith("http")) //will cover https also
				{
					// do not open in external browser if localhost...except for some links in the toolbox
					if(anchor.Href.ToLowerInvariant().StartsWith(ServerBase.ServerUrlWithBloomPrefixEndingInSlash))
					{
						ge.Handled = false; // let gecko handle it
						return;
					}

					Process.Start(anchor.Href);
					ge.Handled = true;
					return;
				}
				if(anchor.Href.ToLowerInvariant().StartsWith("file")) //source bubble tabs
				{
					ge.Handled = false; //let gecko handle it
					return;
				}
				ErrorReport.NotifyUserOfProblem("Bloom did not understand this link: " + anchor.Href);
				ge.Handled = true;
			}
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
			if(imageElement == null)
				return;
			string fileName = HtmlDom.GetImageElementUrl(imageElement).NotEncoded;

			//enhance: this all could be done without loading the image into memory
			//could just deal with the metadata
			//e.g., var metadata = Metadata.FromFile(path)
			var path = Path.Combine(_model.CurrentBook.FolderPath, fileName);
			PalasoImage imageInfo = null;
			try
			{
				imageInfo = PalasoImage.FromFileRobustly(path);
			}
			catch(TagLib.CorruptFileException e)
			{
				ErrorReport.NotifyUserOfProblem(e,
					"Bloom ran into a problem while trying to read the metadata portion of this image, " + path);
				return;
			}

			using(imageInfo)
			{
				var hasMetadata = !(imageInfo.Metadata == null || imageInfo.Metadata.IsEmpty);
				if(hasMetadata)
				{
					// If we have metadata with an official collectionUri or we are translating a shell
					// just give a summary of the metadata
					var looksOfficial = !string.IsNullOrEmpty(imageInfo.Metadata.CollectionUri);
					if(looksOfficial || !_model.CanEditCopyrightAndLicense)
					{
						MessageBox.Show(imageInfo.Metadata.GetSummaryParagraph("en"));
						return;
					}
				}
				else
				{
					// If we don't have metadata, but we are translating a shell
					// don't allow the metadata to be edited
					if(!_model.CanEditCopyrightAndLicense)
					{
						MessageBox.Show(LocalizationManager.GetString("EditTab.CannotChangeCopyright",
							"Sorry, the copyright and license for this book cannot be changed."));
						return;
					}
				}
				// Otherwise, bring up the dialog to edit the metadata
				Logger.WriteEvent("Showing Metadata Editor For Image");
				using(var dlg = new SIL.Windows.Forms.ClearShare.WinFormsUI.MetadataEditorDialog(imageInfo.Metadata))
				{
					if(DialogResult.OK == dlg.ShowDialog())
					{
						imageInfo.Metadata = dlg.Metadata;
						imageInfo.Metadata.StoreAsExemplar(Metadata.FileCategory.Image);
						//update so any overlays on the image are brought up to data
						PageEditingModel.UpdateMetadataAttributesOnImage(new ElementProxy(imageElement), imageInfo);
						imageElement.Click(); //wake up javascript to update overlays
						SaveChangedImage(imageElement, imageInfo, "Bloom had a problem updating the image metadata");

						var answer =
							MessageBox.Show(
								LocalizationManager.GetString("EditTab.CopyImageIPMetadataQuestion",
									"Copy this information to all other pictures in this book?", "get this after you edit the metadata of an image"),
								LocalizationManager.GetString("EditTab.TitleOfCopyIPToWholeBooksDialog",
									"Picture Intellectual Property Information"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
								MessageBoxDefaultButton.Button2);
						if(answer == DialogResult.Yes)
						{
							Cursor = Cursors.WaitCursor;
							try
							{
								_model.CopyImageMetadataToWholeBook(dlg.Metadata);
								// There might be more than one image on this page. Update overlays.
								_model.RefreshDisplayOfCurrentPage();
							}
							catch(Exception e)
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

		private void OnCutImage(DomEventArgs ge)
		{
			// NB: bloomImages.js contains code that prevents us arriving here
			// if our image is simply the placeholder flower
			if(!_model.CanChangeImages())
			{
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.CantPasteImageLocked",
						"Sorry, this book is locked down so that images cannot be changed."));
				return;
			}

			var bookFolderPath = _model.CurrentBook.FolderPath;

			if(CopyImageToClipboard(ge, bookFolderPath)) // returns 'true' if successful
			{
				// Replace current image with placeHolder.png
				var path = Path.Combine(bookFolderPath, "placeHolder.png");
				using(var palasoImage = PalasoImage.FromFileRobustly(path))
				{
					_model.ChangePicture(GetImageNode(ge), palasoImage, new NullProgress());
				}
			}
		}

		private void OnCopyImage(DomEventArgs ge)
		{
			// NB: bloomImages.js contains code that prevents us arriving here
			// if our image is simply the placeholder flower

			CopyImageToClipboard(ge, _model.CurrentBook.FolderPath);
		}

		private void OnPasteImage(DomEventArgs ge)
		{
			if(!_model.CanChangeImages())
			{
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.CantPasteImageLocked",
						"Sorry, this book is locked down so that images cannot be changed."));
				return;
			}

			PalasoImage clipboardImage = null;
			try
			{
				clipboardImage = GetImageFromClipboard();
				if(clipboardImage == null)
				{
					MessageBox.Show(
						LocalizationManager.GetString("EditTab.NoImageFoundOnClipboard",
							"Before you can paste an image, copy one onto your 'clipboard', from another program."));
					return;
				}

				var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
				if(target.ClassName.Contains("licenseImage"))
					return;

				var imageElement = GetImageNode(ge);
				if(imageElement == null)
					return;
				Cursor = Cursors.WaitCursor;

				//nb: Taglib# requires an extension that matches the file content type.
				if(ImageUtils.AppearsToBeJpeg(clipboardImage))
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
					else if(Path.GetExtension(clipboardImage.OriginalFilePath).ToLowerInvariant() == ".png")
					{
						Logger.WriteMinorEvent("[Paste Image] Pasting png file {0}", clipboardImage.OriginalFilePath);
						_model.ChangePicture(imageElement, clipboardImage, new NullProgress());
					}
					else // they pasted a path to some other bitmap format
					{
						var pathToPngVersion = Path.Combine(Path.GetTempPath(),
							Path.GetFileNameWithoutExtension(clipboardImage.FileName) + ".png");
						Logger.WriteMinorEvent("[Paste Image] Saving {0} ({1}) as {2} and converting to PNG", clipboardImage.FileName,
							clipboardImage.OriginalFilePath, pathToPngVersion);
						if(RobustFile.Exists(pathToPngVersion))
						{
							RobustFile.Delete(pathToPngVersion);
						}
						using(var temp = TempFile.TrackExisting(pathToPngVersion))
						{
							SIL.IO.RobustIO.SaveImage(clipboardImage.Image, pathToPngVersion, ImageFormat.Png);

							using(var palasoImage = PalasoImage.FromFileRobustly(temp.Path))
							{
								_model.ChangePicture(imageElement, palasoImage, new NullProgress());
							}
						}
					}
				}
			}
			catch(Exception error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "The program had trouble getting an image from the clipboard.");
			}
			finally
			{
				if(clipboardImage != null)
					clipboardImage.Dispose();
			}
			Cursor = Cursors.Default;
		}

		private static PalasoImage GetImageFromClipboard()
		{
			return PortableClipboard.GetImageFromClipboard();
		}

		private static bool CopyImageToClipboard(DomEventArgs ge, string bookFolderPath)
		{
			var imageElement = GetImageNode(ge);
			if(imageElement != null)
			{
				var url = HtmlDom.GetImageElementUrl(imageElement);
				if(String.IsNullOrEmpty(url.NotEncoded))
					return false;

				var path = Path.Combine(bookFolderPath, url.NotEncoded);
				try
				{
					using(var image = PalasoImage.FromFileRobustly(path))
					{
						PortableClipboard.CopyImageToClipboard(image);
					}
					return true;
				}
				catch (NotImplementedException)
				{
					var msg = LocalizationManager.GetDynamicString("Bloom", "ImageToClipboard",
						"Copying an image to the clipboard is not yet implemented in Bloom for Linux.",
						"message for messagebox warning to user");
					var header = LocalizationManager.GetDynamicString("Bloom", "NotImplemented",
						"Not Yet Implemented", "header for messagebox warning to user");
					MessageBox.Show(msg, header);
				}
				catch (ExternalException e)
				{
					Logger.WriteEvent("CopyImageToClipboard -> ExternalException: " + e.Message);
					var msg = LocalizationManager.GetDynamicString("Bloom", "EditTab.Image.CopyImageFailed",
						"Bloom had problems using your computer's clipboard. Some other program may be interfering.") +
						Environment.NewLine + Environment.NewLine +
						LocalizationManager.GetDynamicString("Bloom", "EditTab.Image.TryRestart",
						"Try closing other programs and restart your computer if necessary.");
					MessageBox.Show(msg);
				}
				catch (Exception e)
				{
					Debug.Fail(e.Message);
					Logger.WriteEvent("CopyImageToClipboard:" + e.Message);
				}
			}
			return false;
		}

		private static GeckoHtmlElement GetImageNode(DomEventArgs ge)
		{
			var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			var imageContainer = target.Parent;
			if(imageContainer.OuterHtml.Contains("background-image"))
				return imageContainer; // using a background-image instead of child <img> element
			foreach(var node in imageContainer.ChildNodes)
			{
				var imageElement = node as GeckoHtmlElement;
				if(imageElement != null && (imageElement.TagName.ToLowerInvariant() == "img" ||
				                            imageElement.OuterHtml.Contains("background-image")))
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
			if(!_model.CanChangeImages())
			{
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.CantPasteImageLocked",
						"Sorry, this book is locked down so that images cannot be changed."));
				return;
			}

			var imageElement = GetImageNode(ge);
			if(imageElement == null)
				return;
			string currentPath = HtmlDom.GetImageElementUrl(imageElement).NotEncoded;

			if(!CheckIfLockedAndWarn(currentPath))
				return;
			var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			if(target.ClassName.Contains("licenseImage"))
				return;

			Cursor = Cursors.WaitCursor;

			var imageInfo = new PalasoImage();
			var existingImagePath = Path.Combine(_model.CurrentBook.FolderPath, currentPath);

			//don't send the placeholder to the imagetoolbox... we get a better user experience if we admit we don't have an image yet.
			if(!currentPath.ToLowerInvariant().Contains("placeholder") && RobustFile.Exists(existingImagePath))
			{
				try
				{
					imageInfo = PalasoImage.FromFileRobustly(existingImagePath);
				}
				catch(Exception e)
				{
					Logger.WriteMinorEvent("Not able to load image for ImageToolboxDialog: " + e.Message);
				}
			}
			Logger.WriteEvent("Showing ImageToolboxDialog Editor Dialog");
			// Check memory for the benefit of developers.  The user won't see anything.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "about to choose picture", false);
			// Deep in the ImageToolboxDialog, when the user asks to see images from the ArtOfReading,
			// We need to use the Gecko version of the thumbnail viewer, since the original ListView
			// one has a sticky scroll bar in applications that are using Gecko.  On Linux, we also
			// need to use the Gecko version of the text box.  Except that the Gecko version of the
			// text box totally freezes the system if the user is using LinuxMint/cinnamon (ie, Wasta).
			// See https://jira.sil.org/browse/BL-1147.
			ThumbnailViewer.UseWebViewer = true;
			if(SIL.PlatformUtilities.Platform.IsUnix &&
			   !(SIL.PlatformUtilities.Platform.IsWasta || SIL.PlatformUtilities.Platform.IsCinnamon))
			{
				TextInputBox.UseWebTextBox = true;
			}
			using(var dlg = new ImageToolboxDialog(imageInfo, null))
			{
				var searchLanguage = Settings.Default.ImageSearchLanguage;
				if(String.IsNullOrWhiteSpace(searchLanguage))
				{
					// Pass in the current UI language.  We want only the main language part of the tag.
					// (for example, "zh-Hans" should pass in as "zh".)
					searchLanguage = Settings.Default.UserInterfaceLanguage;
					var idx = searchLanguage.IndexOfAny(new char[] {'-', '_'});
					if(idx > 0)
						searchLanguage = searchLanguage.Substring(0, idx);
				}

				dlg.SearchLanguage = searchLanguage;
				var result = dlg.ShowDialog();
				// Check memory for the benefit of developers.  The user won't see anything.
				SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "picture chosen or canceled", false);
				if(DialogResult.OK == result && dlg.ImageInfo != null)
				{
					// var path = MakePngOrJpgTempFileForImage(dlg.ImageInfo.Image);
					SaveChangedImage(imageElement, dlg.ImageInfo, "Bloom had a problem including that image");
					// Warn the user if we're starting to use too much memory.
					SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "picture chosen and saved", true);
				}

				// If the user changed the search language for art of reading, remember their change. But if they didn't
				// touch it, don't remember it. Instead, let it continue to track the UI language so that if
				// they are new and just haven't got around to setting the main UI Language,
				// AOR can automatically start using that when they do.
				if(searchLanguage != dlg.SearchLanguage)
				{
					//store their language selection even if they hit "cancel"
					Settings.Default.ImageSearchLanguage = dlg.SearchLanguage;
					Settings.Default.Save();
				}
			}
			Logger.WriteMinorEvent("Emerged from ImageToolboxDialog Editor Dialog");
			Cursor = Cursors.Default;
			imageInfo.Dispose(); // ensure memory doesn't leak
		}

		void SaveChangedImage(GeckoHtmlElement imageElement, PalasoImage imageInfo, string exceptionMsg)
		{
			try
			{
				if(ShouldBailOutBecauseUserAgreedNotToUseJpeg(imageInfo))
					return;
				_model.ChangePicture(imageElement, imageInfo, new NullProgress());
			}
			catch(System.IO.IOException error)
			{
				ErrorReport.NotifyUserOfProblem(error, error.Message);
			}
			catch(ApplicationException error)
			{
				ErrorReport.NotifyUserOfProblem(error, error.Message);
			}
			catch(Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error, exceptionMsg);
			}
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
			RunJavaScript("if (typeof(FrameExports) !=='undefined') {FrameExports.getToolboxFrameExports().removeToolboxMarkup();}");
			_browser1.ReadEditableAreasNow();
		}

		public GeckoInputElement GetShowToolboxCheckbox()
		{
			return _browser1.WebBrowser.Window.Document.GetElementById("pure-toggle-right") as GeckoInputElement;
		}

		public GeckoElement GetPageBody()
		{
			var frame = _browser1.WebBrowser.Window.Document.GetElementById("page") as GeckoIFrameElement;
			if(frame == null)
				return null;
			// The following line looks like it should work, but it doesn't (at least not reliably in Geckofx45).
			// return frame.ContentDocument.Body;
			return frame.ContentWindow.Document.GetElementsByTagName("body").First();
		}

		/// <summary>
		/// Return the HTML element that represents the body of the toolbox
		/// </summary>
		public ElementProxy ToolBoxElement
		{
			get
			{
				var toolboxFrame = _browser1.WebBrowser.Window.Document.GetElementById("toolbox") as GeckoIFrameElement;
				if(toolboxFrame == null)
					return null;
				return new ElementProxy(toolboxFrame.ContentDocument.Body);
			}
		}

		private void _copyButton_Click(object sender, EventArgs e)
		{
			ExecuteCommandSafely(_copyCommand);
		}

		private void _pasteButton_Click(object sender, EventArgs e)
		{
			ExecuteCommandSafely(_pasteCommand);
		}

		/// <summary>
		/// Add a menu item to a dropdown button and return it.  Avoid creating a ToolStripSeparator instead of a
		/// ToolStripMenuItem even for a hyphen.
		/// </summary>
		/// <returns>the dropdown menu item</returns>
		/// <remarks>See https://silbloom.myjetbrains.com/youtrack/issue/BL-3796.</remarks>
		private ToolStripMenuItem AddDropdownItemSafely(ToolStripDropDownButton button, string text)
		{
			// A single hyphen triggers a ToolStripSeparator instead of a ToolStripMenuItem, so change it minimally.
			// (Surely localizers wouldn't do this to us, but it has happened to a user.)
			if (text == "-")
				text = "- ";
			return (ToolStripMenuItem) button.DropDownItems.Add(text);
		}

		public void UpdateDisplay()
		{
			try
			{
				_updatingDisplay = true;

				_contentLanguagesDropdown.DropDownItems.Clear();
				// L10NSharp doesn't do this automatically
				_contentLanguagesDropdown.ToolTipText = LocalizationManager.GetString("EditTab.ContentLanguagesDropdown.ToolTip",
					//_contentLanguagesDropdown.ToolTipText); doesn't work because the scanner needs literals
					"Choose language to make this a bilingual or trilingual book");

				foreach(var l in _model.ContentLanguages)
				{
					var item = AddDropdownItemSafely(_contentLanguagesDropdown, l.ToString());
					item.Tag = l;
					item.Enabled = !l.Locked;
					item.Checked = l.Selected;
					item.CheckOnClick = true;
					item.CheckedChanged += new EventHandler(OnContentLanguageDropdownItem_CheckedChanged);
				}

				_layoutChoices.DropDownItems.Clear();
				var layout = _model.GetCurrentLayout();
				var layoutChoices = _model.GetLayoutChoices();
				foreach(var l in layoutChoices)
				{
					var text = LocalizationManager.GetDynamicString("Bloom", "LayoutChoices." + l.ToString(), l.ToString());
					var item = AddDropdownItemSafely(_layoutChoices, text);
					item.Tag = l;
					//we don't allow the split options here
					if(l.ElementDistribution == Book.Layout.ElementDistributionChoices.SplitAcrossPages)
					{
						item.Enabled = false;
						item.ToolTipText = LocalizationManager.GetString("EditTab.LayoutInPublishTabOnlyNotice",
							"This option is only available in the Publish tab.");
					}
					item.Text = text;
					item.Click += new EventHandler(OnPaperSizeAndOrientationMenuClick);
				}

				if(layoutChoices.Count() < 2)
				{
					var text = LocalizationManager.GetString("EditTab.NoOtherLayouts",
						"There are no other layout options for this template.",
						"Show in the layout chooser dropdown of the edit tab, if there was only a single layout choice");
					var item = AddDropdownItemSafely(_layoutChoices, text);
					item.Tag = null;
					item.Enabled = false;
				}

				_layoutChoices.Text = LocalizationManager.GetDynamicString("Bloom", "LayoutChoices." + layout, layout.ToString());

				switch(_model.NumberOfDisplayedLanguages)
				{
					case 1:
						_contentLanguagesDropdown.Text = LocalizationManager.GetString("EditTab.Monolingual", "One Language",
							"Shown in edit tab multilingualism chooser, for monolingual mode, one language per page");
						break;
					case 2:
						_contentLanguagesDropdown.Text = LocalizationManager.GetString("EditTab.Bilingual", "Two Languages",
							"Shown in edit tab multilingualism chooser, for bilingual mode, 2 languages per page");
						break;
					case 3:
						_contentLanguagesDropdown.Text = LocalizationManager.GetString("EditTab.Trilingual", "Three Languages",
							"Shown in edit tab multilingualism chooser, for trilingual mode, 3 languages per page");
						break;
				}

				//I'm surprised that L10NSharp (in aug 2014) doesn't automatically make tooltips localizable, but this is how I got it to work
				_layoutChoices.ToolTipText = LocalizationManager.GetString("EditTab.PageSizeAndOrientation.Tooltip",
					//_layoutChoices.ToolTipText); doesn't work because the scanner needs literals
					"Choose a page size and orientation");

				_pageListView.UpdateDisplay();
			}
			catch(Exception error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem updating the edit display.");
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
			if(_updatingDisplay)
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
			var enabled = command != null && command.Enabled;
			// DuplicatePage and DeletePage are a bit tricky to get right.
			// See https://silbloom.myjetbrains.com/youtrack/issue/BL-2183.
			if(enabled && command.Implementer != null)
			{
				var target = command.Implementer.Target as EditingModel;
				if(target != null)
				{
					if(command is DuplicatePageCommand)
						enabled = target.CanDuplicatePage;
					else if(command is DeletePageCommand)
						enabled = target.CanDeletePage;
				}
			}
			//doesn't work because the forecolor is ignored when disabled...
			var foreColor = enabled ? _enabledToolbarColor : _disabledToolbarColor; //.DimGray;
			// BL-2338: signficant button flashing is apparently caused by setting these and
			// invalidating when nothing actually changed. So only do it if something DID change.
			if(enabled != button.Enabled || button.ForeColor != foreColor)
			{
				button.Enabled = enabled;
				button.ForeColor = foreColor;
				button.Invalidate();
			}
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
			ExecuteCommandSafely(_cutCommand);
		}

		private void _undoButton_Click(object sender, EventArgs e)
		{
			ExecuteCommandSafely(_undoCommand);
		}

		private void ExecuteCommandSafely(Command cmdObject)
		{
			try
			{
				cmdObject.Execute();
			}
			catch(Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error,
					LocalizationManager.GetString("Errors.SomethingWentWrong", "Sorry, something went wrong."));
			}
		}

		public void ClearOutDisplay()
		{
			_pageListView.Clear();
			_browser1.Navigate("about:blank", false);
		}

		private void _deletePageButton_Click_1(object sender, EventArgs e)
		{
			if(ConfirmRemovePageDialog.Confirm())
			{
				ExecuteCommandSafely(_deletePageCommand);
			}
		}

		private void _duplicatePageButton_Click(object sender, EventArgs e)
		{
			ExecuteCommandSafely(_duplicatePageCommand);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			//Why the check for null? In bl-283, user had been in settings dialog, which caused a closing down, but something
			//then did a callback to this view, such that ParentForm was null, and this died
			//This assert was driving me crazy (which is a short trip). I'd hit it every time I quit Bloom, but this things are fine. Debug.Assert(ParentForm != null);
			if(ParentForm != null)
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
		internal void SetModalState(string isModal)
		{
			_pageListView.Enabled = isModal != "true";
		}

		/// <summary>
		/// BL-2153: This is to provide visual feedback to the user that the program has received their
		///          page change click and is actively processing the request.
		/// </summary>
		public bool ChangingPages
		{
			set
			{
				if(_browser1.Visible != value) return;

				_browser1.Visible = !value;
				_pageListView.Enabled = !value;
				Cursor = value ? Cursors.WaitCursor : Cursors.Default;
				_pageListView.Cursor = Cursor;
			}
		}

		public void ShowAddPageDialog()
		{
			//if the dialog is already showing, it is up to this method we're calling to detect that and ignore our request
			RunJavaScript("FrameExports.showAddPageDialog(false);");
		}

		internal void ShowChangeLayoutDialog(IPage page)
		{
			//if the dialog is already showing, it is up to this method we're calling to detect that and ignore our request
			RunJavaScript("FrameExports.showAddPageDialog(true);");
		}


		public static string GetInstructionsForUnlockingBook()
		{
			return LocalizationManager.GetString("EditTab.HowToUnlockBook",
							"To unlock this shellbook, go into the toolbox on the right, find the gear icon, and click 'Allow changes to this shellbook'.");
		}
	}
}
