//#define MEMORYCHECK
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
using Bloom.web.controllers;
using Bloom.Workspace;
using L10NSharp;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Miscellaneous;
using Gecko;
using TempFile = SIL.IO.TempFile;
using Gecko.DOM;
using SIL.IO;
using SIL.Windows.Forms.ImageToolbox.ImageGallery;
using SIL.Windows.Forms.Widgets;
using System.Globalization;
using Bloom.web;
using ICSharpCode.SharpZipLib.Zip;
using SIL.Extensions;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl, IBloomTabArea, IZoomManager
	{
		private readonly EditingModel _model;
		private PageListView _pageListView;
		private readonly CutCommand _cutCommand;
		private readonly CopyCommand _copyCommand;
		private readonly PasteCommand _pasteCommand;
		private readonly UndoCommand _undoCommand;
		private readonly DuplicatePageCommand _duplicatePageCommand;
		private readonly DeletePageCommand _deletePageCommand;
		private readonly SignLanguageApi _signLanguageApi;
		private Action _pendingMessageHandler;
		private bool _updatingDisplay;
		private Color _enabledToolbarColor = Palette.DarkTextAgainstBackgroundColor;
		private Color _disabledToolbarColor = Color.FromArgb(114, 74, 106);
		private bool _visible;
		private BloomWebSocketServer _webSocketServer;
		private ZoomControl _zoomControl;
		private PageListApi _pageListApi;

		public delegate EditingView Factory(); //autofac uses this

		public EditingView(EditingModel model, PageListView pageListView, CutCommand cutCommand, CopyCommand copyCommand,
			PasteCommand pasteCommand, UndoCommand undoCommand, DuplicatePageCommand duplicatePageCommand,
			DeletePageCommand deletePageCommand, NavigationIsolator isolator, ControlKeyEvent controlKeyEvent,
			SignLanguageApi signLanguageApi, CommonApi commonApi, EditingViewApi editingViewApi, PageListApi pageListApi, BookRenamedEvent bookRenamedEvent)
		{
			_model = model;
			_pageListView = pageListView;
			_cutCommand = cutCommand;
			_copyCommand = copyCommand;
			_pasteCommand = pasteCommand;
			_undoCommand = undoCommand;
			_duplicatePageCommand = duplicatePageCommand;
			_deletePageCommand = deletePageCommand;
			_webSocketServer = model.EditModelSocketServer;
			_pageListApi = pageListApi;
			InitializeComponent();

			this._splitContainer2.BackColor = Palette.GeneralBackground;
			SetupThumnailLists();
			_model.SetView(this);
			// We will need to handle this in another way if we ever have multiple projects open and thus
			// multiple models and views.
			_signLanguageApi = signLanguageApi;
			signLanguageApi.Model = _model;
			signLanguageApi.View = this;
			editingViewApi.View = this;
			commonApi.Model = _model;
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
			// BL-5071 We don't want a hover border on the items either.
			_menusToolStrip.Renderer = new NoBorderToolStripRenderer();

			//we're giving it to the parent control through the TopBarControls property
			Controls.Remove(_topBarPanel);
			SetupBrowserContextMenu();
			bookRenamedEvent.Subscribe((book) =>
			{
				UpdatePageList(true);
			});
#if __MonoCS__
// The inactive button images look garishly pink on Linux/Mono, but look okay on Windows.
// Merely introducing an "identity color matrix" to the image attributes appears to fix
// this problem.  (The active form looks okay with or without this fix.)
// See http://issues.bloomlibrary.org/youtrack/issue/BL-3714.
			float[][] colorMatrixElements = {
				new float[] {1,  0,  0,  0,  0},		// red scaling factor of 1
				new float[] {0,  1,  0,  0,  0},		// green scaling factor of 1
				new float[] {0,  0,  1,  0,  0},		// blue scaling factor of 1
				new float[] {0,  0,  0,  1,  0},		// alpha scaling factor of 1
				new float[] {0,  0,  0,  0,  1}};		// three translations of 0.0
			var colorMatrix = new ColorMatrix(colorMatrixElements);
			_undoButton.ImageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
			_cutButton.ImageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
			_pasteButton.ImageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
			_copyButton.ImageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

			// Prevent the layout choices and book language dropdown menus from closing
			// immediately in Linux/Gnome (default for ubuntu 18.04 aka bionic).
			_layoutChoices.DropDown.Closing += DropDown_Closing;
			_contentLanguagesDropdown.DropDown.Closing += DropDown_Closing;
			_layoutChoices.DropDown.Opening += DropDown_Opening;
			_contentLanguagesDropdown.DropDown.Opening += DropDown_Opening;
#endif
		}

#if __MonoCS__
		private bool _ignoreNextAppFocusChange;
		/// <summary>
		/// Prevent the book language and layout dropdown menus from closing prematurely.
		/// This is a big problem for Gnome, which is the default window manager in Ubuntu 18.04.
		/// (This must be due to the way Gnome sends out various windowing messages.)
		/// </summary>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6107.
		/// See also WorkspaceView.DropDown_Closing (UI language menu dropdown), which has largely
		/// been in place for some time.  This is similar to that method.
		/// The side-effect of this method on systems other than Gnome is that the first click
		/// outside the menu will not close it.  But since we don't have a good way to detect
		/// Gnome, this seems like a minimal price to pay for allowing Gnome to work.
		/// </remarks>
		void DropDown_Closing(object sender, ToolStripDropDownClosingEventArgs e)
		{
			// ReSharper disable once SwitchStatementMissingSomeCases
			switch (e.CloseReason)
			{
				case ToolStripDropDownCloseReason.AppFocusChange:
					// With Linux/Gnome, a spurious focus change happens as soon as the menu is opened.
					// So we want to ignore the first closing caused by AppFocusChange.
					e.Cancel = _ignoreNextAppFocusChange;
					break;
				case ToolStripDropDownCloseReason.Keyboard:
					// "reason" is Keyboard, but this seems to be generated just by moving the mouse over
					// the adjacent (visible) button.
					var mousePos = _layoutChoices.Owner.PointToClient(MousePosition);
					var bounds = (sender == _layoutChoices.DropDown) ? _contentLanguagesDropdown.Bounds : _layoutChoices.Bounds;
					if (bounds.Contains(mousePos))
					{
						e.Cancel = true; // probably a false positive
					}
					break;
				default: // includes ItemClicked, CloseCalled, AppClicked
					break;
			}
			_ignoreNextAppFocusChange = false;
			Debug.WriteLine("DEBUG EditingView.DropDown_Closing: reason={0}, cancel={1}", e.CloseReason.ToString(), e.Cancel);
		}

		void DropDown_Opening(object sender, System.ComponentModel.CancelEventArgs e)
		{
			_ignoreNextAppFocusChange = true;
		}
#endif

		public EditingModel Model => _model;

		/// <summary>
		/// Might add a menu item to the Gecko context menu.
		/// If the current book is LockedDown, we don't add any text over picture options.
		/// If we are in a "bloom-imageContainer" div the menu item will be to add a text box to the image.
		/// If we are in a "bloom-textOverPicture" div the menu item will be to delete a text box from the image.
		/// Otherwise no menu item is added.
		/// </summary>
		private void SetupBrowserContextMenu()
		{
			// "return false" means we don't want to override other menu items that might be added
			_browser1.ContextMenuProvider = args =>
			{
				var targetNode = args.TargetNode;
				if (targetNode.NodeName.ToLowerInvariant() == "svg")
				{
					// This case can be generated by the comic tool. It inserts an SVG of all the bubbles/tails/etc,
					// But that is not convertable to a GeckoHtmlElement.
					// So, try to move the click onto a sibling or parent instead.
					//
					// This is an example of the anticipated layout of this scenario.
					//
					// <DIV class="bloom-imageContainer">
					//     <svg>...</svg>
					//     <DIV class="bloom-textOverPicture">...</DIV>
					//     ...
					//     <IMG ... />
					// </DIV>
					targetNode = targetNode.ParentNode;

					if (targetNode != null && targetNode.ChildNodes != null)
					{
						foreach (var childNode in targetNode.ChildNodes)
						{
							if (childNode.NodeName.ToUpperInvariant() == "IMG")
							{
								targetNode = childNode;
								break;
							}
						}
					}
				}

				// don't allow changes if locked down; Also check for GeckoHtmlElement to be safe.
				if (_model.CurrentBook.LockedDown || !(targetNode is GeckoHtmlElement))
					return false;

				var targetProxy = new ElementProxy((GeckoHtmlElement) targetNode);

				// Since at this point we don't have a way to keep TextOverPicture textboxes that the user adds to xMatter pages
				// we won't give them the opportunity. If we later add that capability, besides removing this 'if', make sure that
				// textboxes appear above cover images.
				if (targetProxy.SelfOrAncestorHasClass("bloom-frontMatter") || targetProxy.SelfOrAncestorHasClass("bloom-backMatter"))
				{
					return false;
				}
				return false;
			};
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

		// The full width of the TopBarControl is a bit much, because the actual values in the _menusToolStrip are usually narrower
		// than the control name shown in design mode. The "5" just gives a little margin.
		public int WidthToReserveForTopBarControl => _menusToolStrip.Left + 5
			+ Math.Max(_contentLanguagesDropdown.Bounds.Right, _layoutChoices.Bounds.Right);

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

			_pageListView.Select();
			//_browser1.Select();
			_browser1.WebBrowser.Select();

			_editButtonsUpdateTimer.Enabled = Parent != null;
		}

		public void PlaceTopBarControl()
		{
			_topBarPanel.Dock = DockStyle.Left;
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
				_model.SaveNow();
				//in case we were in this dialog already and made changes, which haven't found their way out to the Book yet

				var metadata = _model.CurrentBook.GetLicenseMetadata();
				var originalMetadata = BookCopyrightAndLicense.GetOriginalMetadata(_model.CurrentBook.Storage.Dom, _model.CurrentBook.BookData);

				Logger.WriteEvent("Showing Metadata Editor Dialog");
				var isDerivedBook = BookCopyrightAndLicense.IsDerivative(originalMetadata);
				using (var dlg = new BloomMetadataEditorDialog(metadata, isDerivedBook))
				{
					dlg.ShowCreator = false;
					dlg.ReplaceOriginalCopyright = !_model.CurrentBook.BookInfo.MetaData.UseOriginalCopyright;
					if (DialogResult.OK == dlg.ShowDialog())
					{
						_model.CurrentBook.BookInfo.MetaData.UseOriginalCopyright = isDerivedBook && !dlg.ReplaceOriginalCopyright;
						if (!isDerivedBook || dlg.ReplaceOriginalCopyright)
						{
							Logger.WriteEvent("For BL-3166 Investigation");
							if (metadata.License == null)
								Logger.WriteEvent("old LicenseUrl was null ");
							else
								Logger.WriteEvent("old LicenseUrl was " + metadata.License.Url);
							if (dlg.Metadata.License == null)
								Logger.WriteEvent("new LicenseUrl was null ");
							else
								Logger.WriteEvent("new LicenseUrl: " + dlg.Metadata.License.Url);
							_model.ChangeBookLicenseMetaData(dlg.Metadata);
						}
						else
						{
							_model.ChangeBookLicenseMetaData(originalMetadata);
						}
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
			_pageListView.Dock = DockStyle.Left;
			_pageListView.Width = 200;
			this.Controls.Add(_pageListView);
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

			CheckFontAvailability();

			Cursor = Cursors.WaitCursor;
			_model.ViewVisibleNowDoSlowStuff();

			Cursor = Cursors.Default;
		}


		private void CheckFontAvailability()
		{
			var fontMessage = _model.GetFontAvailabilityMessage();
			if(!string.IsNullOrEmpty(fontMessage))
			{
				// Yes, we experienced a font name in the wild which contained curly brackets and crashed Bloom. BL-9340.
				fontMessage = fontMessage.Replace("{", "{{").Replace("}", "}}");
				ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), fontMessage);
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
				_model.ClearBookForToolboxContent(); // there's no longer a frame ready for a new page displayed in the browser.
			}
		}

		DateTime _beginPageLoad;

		public void UpdateSingleDisplayedPage(IPage page)
		{
			if(!_model.Visible)
			{
				return;
			}

#if MEMORYCHECK
			// Check memory for the benefit of developers.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "EditingView - about to update the displayed page", false);
#endif
			if(_model.HaveCurrentEditableBook)
			{
				_beginPageLoad = DateTime.Now;
				_pageListView.SelectThumbnailWithoutSendingEvent(page);
				_pageListView.UpdateThumbnailAsync(page);
				_model.SetupServerWithCurrentPageIframeContents();
				HtmlDom domForCurrentPage = _model.GetXmlDocumentForCurrentPage();
				// A page can't be 'dirty' in the interval between when we start to navigate to it and when it's visible.
				// (The previous page should have been fully saved before we begin such navigation, so we don't
				// need to worry about losing changes there.)
				// So we don't want anything trying to save it during that time. Apart from wasting time, such a save
				// might fail (BL-2634, BL-6296) if the new page is not yet loaded enough to have the right ID.
				// To detect that the page IS loaded, we're looking for a notification from Javascript
				// code (which detects that the page has first loaded and then been painted).
				// We only get one notification per call to this function, so we need
				// to set it up again each time we load a page. It's important to set it up before we start
				// navigation; otherwise, we might miss the event and never enable saving for this page.
				Browser.RequestJsNotification("editPagePainted", () => _model.NavigatingSoSuspendSaving = false);
				_model.NavigatingSoSuspendSaving = true;
				if (_model.AreToolboxAndOuterFrameCurrent())
				{
					_browser1.SetEditDom(domForCurrentPage);
					if (ReloadCurrentPage())
					{
						Logger.WriteEvent("changing page via Navigate(\"CURRENTPAGE.htm\")");
						_browser1.WebBrowser.Navigate(BloomServer.ServerUrlWithBloomPrefixEndingInSlash + "CURRENTPAGE.htm");
					}
					else
					{
						Logger.WriteEvent("changing page via FrameExports.switchContentPage()");
						var pageUrl = _model.GetUrlForCurrentPage();
						RunJavaScript("FrameExports.switchContentPage('" + pageUrl + "');");
					}
				}
				else
				{
					_model.SetupServerWithCurrentBookToolboxContents();
					var dom = _model.GetXmlDocumentForEditScreenWebPage();
					_model.RemoveStandardEventListeners();
					_browser1.Navigate(dom, domForCurrentPage, setAsCurrentPageForDebugging: true, source:BloomServer.SimulatedPageFileSource.Frame);
				}
				_model.CheckForBL2634("navigated to page");
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
#if MEMORYCHECK
			// Check memory for the benefit of developers.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "EditingView - UpdateSingleDisplayedPage() about to call UpdateDisplay()", false);
#endif
			UpdateDisplay();
#if MEMORYCHECK
			// Check memory for the benefit of developers.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "EditingView - UpdateSingleDisplayedPage() finished", false);
#endif
		}

		private bool ReloadCurrentPage()
		{
			// Note that ModifierKeys does not seem to work on Linux.
			return ((ModifierKeys & Keys.Shift) == Keys.Shift) || RobustFile.Exists("/tmp/UseCURRENTPAGE");
		}

		private bool UseBackgroundGC()
		{
			// Note that ModifierKeys does not seem to work on Linux.
			return ((ModifierKeys & Keys.Alt) == Keys.Alt) || RobustFile.Exists("/tmp/UseBackgroundGC");
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
			var beginGarbageCollect = DateTime.Now;
			if (UseBackgroundGC())
			{
				Logger.WriteEvent("performing backgound garbage collection without finalizers");
				GC.Collect(2, GCCollectionMode.Optimized, false, true);
				//GC.WaitForPendingFinalizers();
				MemoryService.MinimizeHeap(false);
			}
			else
			{
				Logger.WriteEvent("performing blocking garbage collection with finalizers");
				GC.Collect(/*2, GCCollectionMode.Optimized, false, true*/);
				GC.WaitForPendingFinalizers();
				MemoryService.MinimizeHeap(true);
			}
			var endPageLoad = DateTime.Now;
			Logger.WriteEvent($"update page elapsed time = {endPageLoad - _beginPageLoad} (garbage collect took {endPageLoad - beginGarbageCollect}");
//#if MEMORYCHECK
			// Check memory for the benefit of developers.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "EditingView - display page updated", false);
//#endif
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
			if (emptyThumbnailCache)
			{
				_pageListView.EmptyThumbnailCache();
			}
			_pageListApi.ClearPagesCache();
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
			GeckoHtmlElement target;
			try
			{
				target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			}
			catch (InvalidCastException)
			{
				// Some things...e.g., SVG elements...can't be cast like this. I can't find any way to
				// predict it. But if we click on one of those, just take the default behavior.
				return;
			}

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
			// (similar changeWidgetButton handled in modern way in javascript)

			var anchor = target as GeckoAnchorElement;
			if (anchor == null)
			{
				// Might be a span inside an anchor
				anchor = target.Parent as GeckoAnchorElement;
			}
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

				if(anchor.Href.ToLowerInvariant().StartsWith("http") || anchor.Href.ToLowerInvariant().StartsWith("mailto")) //will cover https also
				{
					// do not open in external browser if localhost...except for some links in the toolbox
					if(anchor.Href.ToLowerInvariant().StartsWith(BloomServer.ServerUrlWithBloomPrefixEndingInSlash))
					{
						ge.Handled = false; // let gecko handle it
						return;
					}

					SIL.Program.Process.SafeStart(anchor.Href);
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
			string fileName = HtmlDom.GetImageElementUrl(imageElement).PathOnly.NotEncoded;

			var imageInfo = ImageUpdater.GetImageInfoSafelyFromFilePath(_model.CurrentBook.FolderPath, fileName);
			if (imageInfo == null)
			{
				return; // exception handled in ImageUpdater
			}

			using(imageInfo)
			{
				if(ImageUpdater.ImageHasMetadata(imageInfo))
				{
					// If we have metadata with an official collectionUri or we are translating a shell
					// just give a summary of the metadata
					if(ImageUpdater.ImageIsFromOfficialCollection(imageInfo.Metadata) || !_model.CanEditCopyrightAndLicense)
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
				// N.B. It is unnecessary to check for the existence of this file, since selecting a book in
				// collection view triggers an automatic book update process that ensures that the file
				// is put there if not already present.
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
				try
				{
					clipboardImage = PortableClipboard.GetImageFromClipboardWithExceptions();
				}
				catch (Exception ex)
				{
					MessageBox.Show(
						LocalizationManager.GetString("EditTab.NoValidImageFoundOnClipboard",
							"Bloom failed to interpret the clipboard contents as an image. Possibly it was a damaged file, or too large. Try copying something else."));
					return;
				}

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
							SIL.IO.RobustImageIO.SaveImage(clipboardImage.Image, pathToPngVersion, ImageFormat.Png);

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
				if(String.IsNullOrEmpty(url.PathOnly.NotEncoded))
					return false;

				var path = Path.Combine(bookFolderPath, url.PathOnly.NotEncoded);
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
			// This shouldn't happen, but BL-5278 reports that it did. To allow the image toolbox
			// to be opened so at least a new image can be inserted, we need to put some img element
			// there, and it has to have a source.
			var repairedImg = imageContainer.OwnerDocument.CreateElement("img");
			repairedImg.SetAttribute("src", "placeHolder.png");
			repairedImg.SetAttribute("data-problem", "inserted to repair missing img BL-5278");
			imageContainer.AppendChild(repairedImg);
			NonFatalProblem.Report(ModalIf.None, PassiveIf.All, "Missing picture partly repaired", "An <img> element on this page was missing and things were repaired enough to let you choose a new one. We would appreciate help in figuring out how to make this happen so that we can fix it. This is issue BL-5278 in our bug tracking system");
			return (GeckoHtmlElement) repairedImg;
		}

		/// <summary>
		/// Returns true if it is either: a) OK to change images, or b) user overrides
		/// Returns false if user cancels message box
		/// </summary>
		/// <param name="imagePath"></param>
		/// <returns></returns>
		internal bool CheckIfLockedAndWarn(string imagePath)
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
			string currentPath = HtmlDom.GetImageElementUrl(imageElement).PathOnly.NotEncoded;

			if(!CheckIfLockedAndWarn(currentPath))
				return;
			var target = (GeckoHtmlElement) ge.Target.CastToGeckoElement();
			if(target.ClassName.Contains("licenseImage"))
				return;

			Cursor = Cursors.WaitCursor;

			var imageInfo = new PalasoImage();
			var existingImagePath = Path.Combine(_model.CurrentBook.FolderPath, currentPath);
			string newImagePath = null;

			//don't send the placeholder to the imagetoolbox... we get a better user experience if we admit we don't have an image yet.
			if(!currentPath.ToLowerInvariant().Contains("placeholder") && RobustFile.Exists(existingImagePath))
			{
				try
				{
					// Copy the old file we're passing in to the dialog.  It's possible that we'll just crop it, and return the modified
					// image file with an unchanged path.  That's okay, but what if it's from a copied/duplicated page?  Then all the
					// pages with that image get the modification, which is probably not what is wanted.  Excess files will get trimmed
					// later when the book is reopened for editing.  See http://issues.bloomlibrary.org/youtrack/issue/BL-3689.
					var folder = Path.GetDirectoryName(existingImagePath);
					var newFilename = ImageUtils.GetUnusedFilename(folder, Path.GetFileNameWithoutExtension(existingImagePath), Path.GetExtension(existingImagePath));
					newImagePath = Path.Combine(folder, newFilename);
					RobustFile.Copy(existingImagePath, newImagePath);
					Debug.WriteLine("Created image copy: " + newImagePath);
					Logger.WriteEvent("Created image copy: " + newImagePath);
					imageInfo = PalasoImage.FromFileRobustly(newImagePath);
				}
				catch(Exception e)
				{
					Logger.WriteMinorEvent("Not able to load image for ImageToolboxDialog: " + e.Message);
				}
			}
			Logger.WriteEvent("Showing ImageToolboxDialog Editor Dialog");
#if MEMORYCHECK
			// Check memory for the benefit of developers.  The user won't see anything.
			SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "about to choose picture", false);
#endif
			// Deep in the ImageToolboxDialog, when the user asks to see images from the ArtOfReading,
			// We need to use the Gecko version of the thumbnail viewer, since the original ListView
			// one has a sticky scroll bar in applications that are using Gecko.
			ThumbnailViewer.UseWebViewer = true;
			// The Gecko version of the text box keeps causing trouble on Linux, first with Wasta 14
			// (Trusty/Ubuntu 14.04 + Mint + Cinnamon), now with Bionic/Ubuntu 18.04 + Gnome.
			// See https://silbloom.myjetbrains.com/youtrack/issue/BL-1147 and
			// https://silbloom.myjetbrains.com/youtrack/issue/BL-6126.
			var useWebTextBox = TextInputBox.UseWebTextBox;
			if (SIL.PlatformUtilities.Platform.IsLinux)
				TextInputBox.UseWebTextBox = false;
			using(var dlg = new ImageToolboxDialog(imageInfo, null))
			{
				var searchLanguage = Settings.Default.ImageSearchLanguage;
				dlg.ImageLoadingExceptionReporter = (path, ex, msg) =>
				{
					ReportFailureToLoadImage(path, ex);
				};
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
#if MEMORYCHECK
				// Check memory for the benefit of developers.  The user won't see anything.
				SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "picture chosen or canceled", false);
#endif
				if (DialogResult.OK == result && dlg.ImageInfo != null)
				{
					// Save the possibly modified (by cropping) image to a file before processing further.
					// The code for ensuring non-transparency uses GraphicsMagick on the file content if possible, so the
					// file must be in sync with the imageInfo.  See https://issues.bloomlibrary.org/youtrack/issue/BL-8638.
					// This applies to newly selected files as well as cropping previously selected files.
					if (newImagePath == null || dlg.ImageInfo.OriginalFilePath != newImagePath)
					{
						var originalImagePath = dlg.ImageInfo.OriginalFilePath;
						var extension = Path.GetExtension(originalImagePath).ToLowerInvariant();
						// ImageInfo.Save does throws an exception for .bmp files because they can't store metadata.
						// ImageInfo.Save doesn't save .tif file properly, creating a blank image file.  So always
						// save images in PNG format if they aren't originally JPEG format.
						// (Since Bloom always saves images in either PNG or JPEG format anyway, we don't need to
						// worry about the extension of newImagePath outside this block of code.)
						if (extension != ".jpg" && extension != ".jpeg")
							extension = ".png";
						var newFilename = ImageUtils.GetUnusedFilename(Path.GetTempPath(), Path.GetFileNameWithoutExtension(originalImagePath), extension);
						newImagePath = Path.Combine(Path.GetTempPath(), newFilename);
					}
					var exceptionMsg = "Bloom had a problem including that image";
					try
					{
						dlg.ImageInfo.Save(newImagePath);
						dlg.ImageInfo.SetCurrentFilePath(newImagePath);
						SaveChangedImage(imageElement, dlg.ImageInfo, exceptionMsg);
					}
					catch (Exception error)
					{
						var path = dlg.ImageInfo.OriginalFilePath;
						if (dlg.ImageInfo.OriginalFilePath != newImagePath)
							path += $" (or {newImagePath})";
						ReportFailureToLoadImage(path, error);
					}
					finally
					{
						dlg.ImageInfo.SetCurrentFilePath(null); // clears internal cache
						if (newImagePath != dlg.ImageInfo.OriginalFilePath)
						{
							RobustFile.Delete(newImagePath);
						}
					}
#if MEMORYCHECK
					// Warn the user if we're starting to use too much memory.
					SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "picture chosen and saved", true);
#endif
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
			TextInputBox.UseWebTextBox = useWebTextBox;
			Logger.WriteMinorEvent("Emerged from ImageToolboxDialog Editor Dialog");
			Cursor = Cursors.Default;
			imageInfo.Dispose(); // ensure memory doesn't leak
		}

		void ReportFailureToLoadImage(string path, Exception ex)
		{
			var caption = LocalizationManager.GetString("EditTab.Image.ImageFailed", "Failed to load image");
			var form = Form.ActiveForm;
			var oom = ex as OutOfMemoryException;
			if (oom != null)
			{
				// It should be very unusual not to get imageSize...LibPalaso is not supposed to send OOM
				// exceptions for this function without providing the information. Conceivably Bloom
				// ran out of memory at some other point than trying to load the image? Anyway I don't
				// think it's worth another version of the message for this case.
				Tuple<int, int> imageSize = ex.Data["imageSize"] as Tuple<int, int>;
				var width = imageSize == null ? "unknown" : imageSize.Item1.ToString();
				var height = imageSize == null ? "unknown" : imageSize.Item2.ToString(); ;
				try
				{
					if (form != null)
						form.HelpRequested += FormOnHelpRequested;
					var msgFmt = LocalizationManager.GetString("EditTab.Image.TooLarge",
						"Bloom ran out of memory loading this image ({0}), which is quite large ({1} by {2} pixels). Click Help for suggestions.");
					var msg = String.Format(msgFmt, path, width, height);
					MessageBox.Show(msg, caption, MessageBoxButtons.OK, MessageBoxIcon.Information,
						MessageBoxDefaultButton.Button1, 0, form != null);
				}
				finally
				{
					if (form != null)
						form.HelpRequested -= FormOnHelpRequested;
				}
			}
			else
			{
				var msgFmt = LocalizationManager.GetString("EditTab.Image.Corrupt",
					"Bloom was not able to load {0}. The file may be corrupted. Please try another image. Here are some technical details: ");
				var msg = String.Format(msgFmt, path) + Environment.NewLine + Environment.NewLine + ex.Message;
				MessageBox.Show(msg, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void FormOnHelpRequested(object sender, HelpEventArgs hlpevent)
		{
			System.Diagnostics.Process.Start("http://community.bloomlibrary.org/t/running-out-of-memory-loading-images/3956");
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

		public void UpdateAllThumbnails()
		{
			_pageListView.UpdateAllThumbnails();
		}

		/// <summary>
		/// this started as an experiment, where our textareas were not being read when we saved because of the need
		/// to change the picture
		/// </summary>
		public void CleanHtmlAndCopyToPageDom()
		{
			RunJavaScript("if (typeof(FrameExports) !=='undefined') {FrameExports.getToolboxFrameExports().removeToolboxMarkup();}");
			RunJavaScript("if (typeof(FrameExports) !=='undefined') {FrameExports.getPageFrameExports().prepareToSavePage();}");
			_browser1.ReadEditableAreasNow();
		}

		public GeckoInputElement GetShowToolboxCheckbox()
		{
			return _browser1.WebBrowser.Window.Document.GetElementById("pure-toggle-right") as GeckoInputElement;
		}

		/// <summary>
		/// Currently only called by EditingModel.SavePageFrameState()
		/// </summary>
		/// <returns>May return null if the GeckoWebBrowser's Window isn't fully initialized somehow</returns>
		public GeckoElement GetPageBody()
		{
			GeckoElementCollection elements;
			try
			{
				var frame = _browser1.WebBrowser.Window.Document.GetElementById("page") as GeckoIFrameElement;
				if(frame == null)
					return null;
				// The following line looks like it should work, but it doesn't (at least not reliably in Geckofx45).
				// return frame.ContentDocument.Body;
				// On a fast shutdown of Bloom, while it is redisplaying, we can get an empty enumeration.
				// See http://issues.bloomlibrary.org/youtrack/issue/BL-3988.
				elements = frame.ContentWindow.Document.GetElementsByTagName("body");
				if (elements.Length == 0)
					return null;
			}
			catch (ArgumentException ex)
			{
				if (ex.Source != "Geckofx-Core")
					throw;
				Logger.WriteEvent("Geckofx-Core ArgumentException thrown (BL-4633). Probably a GeckoWindow.get_Document() failure, not fully initialized?");
				return null;
			}
			return elements.First();
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
					var text = l.DisplayName;
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
						"There are no other options for this template.",
						"Show in the size/orientation chooser dropdown of the edit tab, if there was only a single choice");
					var item = AddDropdownItemSafely(_layoutChoices, text);
					item.Tag = null;
					item.Enabled = false;
				}

				_layoutChoices.Text = layout.DisplayName;

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
			// Checking whether to enable these buttons is done by javascript code.  This check
			// will fail with a javascript error while pages are being changed.  The code for
			// changing pages makes the browser invisible while the change is happening, so
			// that's what we have to check to prevent spurious javascript errors.
			// Note that this method is called by a timer (probably about 110msec cycle).
			if (!_browser1.Visible)
				return;	
			_browser1.UpdateEditButtons();
			UpdateButtonEnabled(_cutButton, _cutCommand);
			UpdateButtonEnabled(_copyButton, _copyCommand);
			UpdateButtonEnabled(_pasteButton, _pasteCommand);
			UpdateButtonEnabled(_undoButton, _undoCommand);
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
					// Save when we leave the main window, even just switching to the epub a11y check window.
					// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6228. This control can lose/regain
					// focus erratically on Linux, so we don't want this save on its LostFocus event.
					// But we do NOT want to Save right this instant. Deactivate happens a lot, especially while
					// debugging. There's some evidence (BL-6303, BL-6296) that COM message pumping can
					// cause the Deactivate event to be handled at apparently random moments, in the middle
					// of doing something else. We might be trying to Save when the system isn't in a valid
					// state, e.g., in the middle of refreshing everything because the user edited the title
					// and the HTML file and containing folder changed name. Instead, arrange to Save when
					// next idle.
					// However, saving while not active runs into issues like BL-6299. Apparently running
					// Javascript (which is also done elsewhere in SaveNow()) while the main window is
					// inactive is quite disastrous in GeckoFx45, to the point of access violations that
					// stop the program with a green screen. Pending decisions about possible UI changes or
					// other ways of fixing this, we're just disabling it. One hope is that GeckoFx60,
					// which is supposed to have a "headless" capability, may fix this.
					//Application.Idle += SaveWhenIdle;
				};
			}
		}

		private void SaveWhenIdle(object o, EventArgs eventArgs)
		{
			Application.Idle -= SaveWhenIdle; // don't need to do again till next Deactivate.
			_model.SaveNow();
			// Restore any tool state removed by CleanHtmlAndCopyToPageDom(), which is called by _model.SaveNow().
			RunJavaScript("if (typeof(FrameExports) !=='undefined') {FrameExports.getToolboxFrameExports().applyToolboxStateToPage();}");
		}

		public string HelpTopicUrl => "/Tasks/Edit_tasks/Edit_tasks_overview.htm";

		/// <summary>
		/// Prevent navigation, e.g. while a dialog box is showing in the browser control
		/// </summary>
		internal void SetModalState(bool isModal)
		{
			_pageListView.Enabled = !isModal;
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
			PageTemplatesApi.ForPageLayout = false;
			//if the dialog is already showing, it is up to this method we're calling to detect that and ignore our request
			RunJavaScript("FrameExports.showAddPageDialog(false);");
		}

		internal void ShowChangeLayoutDialog()
		{
			PageTemplatesApi.ForPageLayout = true;
			//if the dialog is already showing, it is up to this method we're calling to detect that and ignore our request
			RunJavaScript("FrameExports.showAddPageDialog(true);");
		}


		public static string GetInstructionsForUnlockingBook()
		{
			return LocalizationManager.GetString("EditTab.HowToUnlockBook",
							"To unlock this shellbook, click the lock icon in the lower left corner.");
		}

		// The zoom factor that is shown in the top right of the toolbar (a percent).
		public int Zoom
		{
			// Whatever the user may have saved (e.g., from earlier use of ctrl-wheel), we'll make this an expected multiple-of-10 percent.
			get
			{
				// we used to store floating point numbers, but we now store integer percentages (30%-300% or more?).
				var zoomString = Settings.Default.PageZoom;
				if (String.IsNullOrWhiteSpace(zoomString))
					return 100;
				int zoomInt;
				// This value may have been stored as floating point with the invariant culture, not the current culture.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-5579.
				if (zoomString.Contains(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator))
				{
					float zoomFloat;
					if (float.TryParse(zoomString, NumberStyles.Float, CultureInfo.InvariantCulture, out zoomFloat))
					{
						zoomInt = (int) Math.Round(zoomFloat * 10F) * 10;
						if (zoomInt < ZoomControl.kMinimumZoom || zoomInt > ZoomControl.kMaximumZoom)
							return 100; // bad antique value - normalize to real size.
						return zoomInt;
					}
					else
					{
						return 100;
					}
				}

				if (int.TryParse(zoomString, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out zoomInt))
				{
					// we can't go below 30 (30%), so those must be old floating point values that rounded to an integer.
					if (zoomInt < ZoomControl.kMinimumZoom)
						zoomInt = zoomInt * 100;
					if (zoomInt > ZoomControl.kMaximumZoom)
						return ZoomControl.kMaximumZoom;
					return zoomInt;
				}
				else
				{
					return 100;
				}
			}
		}

		// If SetZoom (and hence _model.RethinkPageAndReloadIt) is called repeatedly too
		// frequently, Bloom can crash, with the program closing spontaneously.  So we
		// need to slow things down when the user is rapidly clicking on the zoom button,
		// without losing track of the desired zoom level.
		// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6580.
		// These variables are used to ensure that zooming works okay regardless of
		// how fast the user clicks the zoom buttons.  Note that calls to SetZoom can
		// either be "successful" or "delayed".  "Delayed" calls result in SetZoom
		// being called after the browser has been updated for the most recent
		// "successful" call.  The "delayed" call uses the most recent requested
		// zoom level, possibly skipping one or more requests along the way if the
		// user is clicking fast enough.

		/// <summary>
		/// timestamp of most recent successful call to SetZoom  (currently used only
		/// for Debug logging)
		/// </summary>
		private DateTime _previousZoomTime = DateTime.MinValue;
		/// <summary>
		/// zoom level set by the most recent successful call to SetZoom
		/// </summary>
		private int _previousZoomLevel = -1;
		/// <summary>
		/// zoom level requested by the most recent call to SetZoom, whether delayed or successful.
		/// It may or may not be the same as _previousZoomLevel.
		/// </summary>
		private int _desiredZoomLevel;
		/// <summary>
		/// Timeout timer for HandleDelayedZoom (handler for _browser1.WebBrowser.DocumentFinished)
		/// _zoomTimer.Enabled flags that a previous SetZoom request is still being processed, and
		/// that the current request needs to be delayed.
		/// </summary>
		private Timer _zoomTimer = new Timer();

		public void SetZoom(int zoom)
		{
			// We need to synchronize between user clicks and browser DocumentCompleted events.
			lock (_zoomTimer)
			{
				if (_zoomTimer.Enabled)
				{
					// Store the desired zoom level for use later when the previous request has
					// finished its UI refresh.
					_desiredZoomLevel = zoom;
					return;
				}
				_previousZoomTime = DateTime.Now;
				_previousZoomLevel = zoom;
				_desiredZoomLevel = zoom;
				_browser1.WebBrowser.DocumentCompleted += ZoomDocumentCompleted;
				// Provide a timeout for the DocumentCompleted handler in case the event somehow
				// gets lost between javascript and C#.  (I never saw this happen while testing.)
				// Pages should redraw in less than 6 seconds.  On my 5 year old developer machine, the
				// longest time I measured was 2.961 seconds for ZoomDocumentCompleted to fire.  The
				// shortest time interval measured was 0.431 seconds.  The average was somewhere around
				// 0.500-0.600 seconds.
				_zoomTimer.Interval = 6000;
				_zoomTimer.Tick += HandleDelayedZoom;
				_zoomTimer.Start();

				Settings.Default.PageZoom = zoom.ToString(CultureInfo.InvariantCulture);
				Settings.Default.Save();
				// The main current reason a zoom change requires us to reload the page is that
				// Text-over-picture boxes don't otherwise adjust their size and position properly.
				// If that gets fixed, we could consider reinstating a JS function we used to call
				// here, SetZoom, which originally just changed the transform on the scaling container.
				// However, when it was later changed to post a request for reloading the page,
				// it became cleaner to just do the reload directly here.
				_model.RethinkPageAndReloadIt();
			}
		}

		private void HandleDelayedZoom(object sender, EventArgs e)
		{
			// We need to synchronize between user clicks and browser DocumentCompleted events.
			lock (_zoomTimer)
			{
				_zoomTimer.Stop();
				_zoomTimer.Tick -= HandleDelayedZoom;
			}
			if (_desiredZoomLevel != _previousZoomLevel)
				SetZoom(_desiredZoomLevel);
		}

		void ZoomDocumentCompleted(object sender, Gecko.Events.GeckoDocumentCompletedEventArgs e)
		{
			_browser1.WebBrowser.DocumentCompleted -= ZoomDocumentCompleted;
			Debug.WriteLine("EditingView.ZoomDocumentCompleted() after SetZoom({0}): desired Zoom = {1}, time interval = {2} ms",
				_previousZoomLevel, _desiredZoomLevel, (DateTime.Now - _previousZoomTime).TotalMilliseconds);
			// short-circuit the timer.  The only purpose of the timer is a time-out for this event to occur.
			HandleDelayedZoom(sender, e);
		}

		public void AdjustPageZoom(int delta)
		{
			var currentZoom = _zoomControl.Zoom;
			if (delta < 0 && currentZoom <= Bloom.Workspace.ZoomControl.kMinimumZoom ||
				delta > 0 && currentZoom >= Bloom.Workspace.ZoomControl.kMaximumZoom)
			{
				return;
			}
			_zoomControl.Zoom = currentZoom + delta;
		}

		internal void SetZoomControl(ZoomControl zoomCtl)
		{
			_zoomControl = zoomCtl;
		}

		// intended for use only by the EditingModel
		internal Browser Browser => _browser1;
	}
}
