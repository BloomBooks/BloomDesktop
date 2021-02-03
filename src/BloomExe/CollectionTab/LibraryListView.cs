using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.ToPalaso;
using Bloom.web;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using DesktopAnalytics;
using SIL.Reporting;
using L10NSharp;
using SIL.IO;

namespace Bloom.CollectionTab
{
	public partial class LibraryListView : UserControl
	{
		private const int ButtonHeight = 112;
		private const int ButtonWidth = 92;
		private readonly Size MaxThumbnailSize = new Size(HtmlThumbNailer.ThumbnailOptions.DefaultHeight, HtmlThumbNailer.ThumbnailOptions.DefaultHeight);

		public delegate LibraryListView Factory();//autofac uses this

		private readonly LibraryModel _model;
		private readonly BookSelection _bookSelection;
		//private readonly HistoryAndNotesDialog.Factory _historyAndNotesDialogFactory;
		private Font _headerFont;
		private Font _editableBookFont;
		private Font _collectionBookFont;
		private bool _thumbnailRefreshPending;
		private DateTime _lastClickTime;
		private bool _primaryCollectionReloadPending;
		private bool _disposed;
		private BookCollection _downloadedBookCollection;
		private Image _dropdownImage;
		private string _previousTargetSaveAs = null;

		enum ButtonManagementStage
		{
			LoadPrimary, ImprovePrimary, LoadSourceCollections, ImproveAndRefresh, FinalizeSetup
		}

		private ButtonManagementStage _buttonManagementStage = ButtonManagementStage.LoadPrimary;

		/// <summary>
		/// we go through these at idle time, doing slow things like actually instantiating the book to get the title in preferred language
		/// A stack would be better for updating "the thing I just changed", but we're using a queue at the moment simply because we
		/// want you'd see at the top of the screen to update before what's at the bottom or offscreen
		/// </summary>
		private readonly ConcurrentQueue<ButtonRefreshInfo> _buttonsNeedingSlowUpdate;

		private bool _alreadyReportedErrorDuringImproveAndRefreshBookButtons;

		public LibraryListView(LibraryModel model, BookSelection bookSelection, SelectedTabChangedEvent selectedTabChangedEvent, LocalizationChangedEvent localizationChangedEvent)
			//HistoryAndNotesDialog.Factory historyAndNotesDialogFactory)
		{
			_model = model;
			_bookSelection = bookSelection;
			localizationChangedEvent.Subscribe(unused=>LoadSourceCollectionButtons());
			//_historyAndNotesDialogFactory = historyAndNotesDialogFactory;
			_buttonsNeedingSlowUpdate = new ConcurrentQueue<ButtonRefreshInfo>();
			selectedTabChangedEvent.Subscribe(OnSelectedTabChanged);
			InitializeComponent();
			_primaryCollectionFlow.HorizontalScroll.Visible = false;

			_primaryCollectionFlow.Controls.Clear();
			_primaryCollectionFlow.HorizontalScroll.Visible = false;
			_sourceBooksFlow.Controls.Clear();
			_sourceBooksFlow.HorizontalScroll.Visible = false;
			_sourceBooksFlow.SizeChanged += _sourceBooksFlow_SizeChanged;
			_sourceBooksFlow.BackColor = Palette.GeneralBackground;
			splitContainer1.BackColor =  Palette.BookListSplitterColor;
			if (!_model.ShowSourceCollections)
			{
				splitContainer1.Panel2Collapsed = true;
			}
			
			_headerFont = new Font(SystemFonts.DialogFont.FontFamily, (float)10.0, FontStyle.Bold);
			_editableBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);//, FontStyle.Bold);
			_collectionBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);

			this.BackColor =  Palette.GeneralBackground;

			//enhance: move to model
			bookSelection.SelectionChanged += OnBookSelectionChanged;

			_settingsProtectionHelper.ManageComponent(_openFolderOnDisk);

			_showHistoryMenu.Visible = _showNotesMenu.Visible = Settings.Default.ShowSendReceive;

			if(Settings.Default.ShowExperimentalFeatures)
				_settingsProtectionHelper.ManageComponent(_exportToXMLForInDesignToolStripMenuItem);//we are restricting it because it opens a folder from which the user could do damage
			_exportToXMLForInDesignToolStripMenuItem.Visible = Settings.Default.ShowExperimentalFeatures;

			SetupBookDropdownIcon();
			_bookContextMenu.Closed += _bookContextMenu_Closed;
			_bookContextMenu.Opening += _bookContextMenu_Opening;
		}

		// BL-2678 Adjust the context menu item visibility based on what sort of collection we're in
		// If we're in factory-installed templates or the sample shell, don't show a menu at all
		// If we're in a book downloaded from BloomLibrary.org, only show "Open Folder on Disk" and "Delete"
		// If we're in our one editable collection, show everything
		// Otherwise (which should be bloompacks or other user-installed stuff not downloaded):
		//   only show "Open Folder on Disk"
		private void _bookContextMenu_Opening(object sender, CancelEventArgs e)
		{
			_leveledReaderMenuItem.Checked = _model.IsBookLeveled;
			_decodableReaderMenuItem.Checked = _model.IsBookDecodable;

			var btn = (sender as ContextMenuStrip).SourceControl as Button;
			if (btn == null)
			{
				// At least in Mono, the button selection doesn't always survive from the click to this point.
				// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3424 and various internet posts like
				// http://stackoverflow.com/questions/3026380/getting-the-highest-owner-of-a-toolstripdropdownitem.
				// This might be needed only on Linux/Mono, but is safe for windows.
				btn = _clickedButton;
				_clickedButton = null;
				if (btn == null)
				{
					e.Cancel = true; // don't show the menu at all
					return;
				}
			}
			var btnInfo = btn.Tag as BookButtonInfo;
			if (btnInfo.IsEditable)
				return; // leave them all on
			if (btnInfo.HasNoContextMenu)
			{
				e.Cancel = true; // don't show the menu at all (but leave them visible for next time)
				return;
			}
			foreach (ToolStripItem menuItem in (sender as ContextMenuStrip).Items)
			{
				if (menuItem == deleteMenuItem && btnInfo.IsBLibraryBook)
					continue; // leave this one on for BloomLibrary books
				if (menuItem == _openFolderOnDisk)
					continue; // leave this one on (for both BloomLibrary and BloomPack books)
				menuItem.Visible = false;
			}
		}

		private void _bookContextMenu_Closed(object sender, ToolStripDropDownClosedEventArgs e)
		{
			// Not sure which ones are visible at this point
			// So make them all visible so they are available in the right-click menu
			foreach (ToolStripItem menuItem in (sender as ContextMenuStrip).Items)
			{
				// Except that we do know that this one's visibility has already been set correctly;
				// Don't change it! (BL-4176)
				if (menuItem == _exportToXMLForInDesignToolStripMenuItem)
					menuItem.Visible = _exportToXMLForInDesignToolStripMenuItem.Visible;
				else menuItem.Visible = true;
			}
		}

		private static BookInfo GetBookInfoFromButton(Button bookButton)
		{
			var bookButtonInfo = bookButton.Tag as BookButtonInfo;
			return bookButtonInfo == null ? null : bookButtonInfo.BookInfo;
		}

		private void SetupBookDropdownIcon()
		{
			// we just need the bottom part of the image for this button
			_dropdownImage = new Bitmap(10, 8, PixelFormat.Format32bppArgb);
			var src = (Bitmap)_menuTriangle.Image;
			using (var g = Graphics.FromImage(_dropdownImage))
			{
				g.DrawImage(src, new Rectangle(0, 0, 10, 8), new Rectangle(0, 7, 13, 11), GraphicsUnit.Pixel);
			}
		}

		Button _clickedButton;	// safety net for ContextMenuStrip to know caller.  (BL-3424)

		void _bookTriangle_Click(Button btn, Point clickLocation)
		{
			// hide these controls in the triangle menu
			toolStripSeparator1.Visible = false;
			_updateThumbnailMenu.Visible = false;
			_updateFrontMatterToolStripMenu.Visible = false;

			_clickedButton = btn;
			btn.ContextMenuStrip.Show(btn, clickLocation);
		}

		private void OnExportToXmlForInDesign(object sender, EventArgs e)
		{
			using(var d = new InDesignXmlInformationDialog())
			{
				d.ShowDialog();
			}
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				dlg.FileName = Path.GetFileNameWithoutExtension(SelectedBook.GetPathHtmlFile())+".xml";
				dlg.InitialDirectory = SelectedBook.FolderPath;
				dlg.OverwritePrompt = true;
				if(DialogResult.OK == dlg.ShowDialog())
				{
					try
					{
						_model.ExportInDesignXml(dlg.FileName);
						PathUtilities.SelectFileInExplorer(dlg.FileName);
						Analytics.Track("Exported XML For InDesign");
					}
					catch (Exception error)
					{
						SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "Could not export the book to XML");
						Analytics.ReportException(error);
					}
				}
			}
		}

		private void OnBookSelectionChanged(object sender, BookSelectionChangedEventArgs bookSelectionChangedEventArgs)
		{
			if (sender == null) return;

			var selection = (BookSelection)sender;
			if ((selection.CurrentSelection != null) && (selection.CurrentSelection.BookInfo != null))
			{
				HighlightBookButtonAndShowContextMenuButton(selection.CurrentSelection.BookInfo);
			}
		}

		public int PreferredWidth
		{
			get { return 300; }
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			Application.Idle += ManageButtonsAtIdleTime;
		}

		private void ManageButtonsAtIdleTime(object sender, EventArgs e)
		{
			if (_disposed) //could happen if a version update was detected on app launch
				return;

			switch (_buttonManagementStage)
			{
				case ButtonManagementStage.LoadPrimary:
					LoadPrimaryCollectionButtons();
					_buttonManagementStage = ButtonManagementStage.ImprovePrimary;
					_primaryCollectionFlow.Refresh();
					break;

				//here we do any expensive fix up of the buttons in the primary collection (typically, getting vernacular captions, which requires reading their html)
				case ButtonManagementStage.ImprovePrimary:
					if (_buttonsNeedingSlowUpdate.IsEmpty)
					{
						_buttonManagementStage = ButtonManagementStage.LoadSourceCollections;
					}
					else
					{
						ImproveAndRefreshBookButtons();
					}
					break;
				case ButtonManagementStage.LoadSourceCollections:
					LoadSourceCollectionButtons();
					_buttonManagementStage = ButtonManagementStage.ImproveAndRefresh;
					if (Program.PathToBookDownloadedAtStartup != null)
					{
						// We started up with a command to downloaded a book...Select it.
						SelectBook(new BookInfo(Program.PathToBookDownloadedAtStartup, false));
					}
					break;
				case ButtonManagementStage.ImproveAndRefresh:
					// GJM Sept 23 2015: BL-2778 Concern about memory leaks led to not updating thumbnails on
					// source collections for new books. To undo, uncomment ImproveAndRefreshBookButtons()
					// and comment out removing the event handler.
					//ImproveAndRefreshBookButtons();
					_buttonManagementStage = ButtonManagementStage.FinalizeSetup;
					break;
				case ButtonManagementStage.FinalizeSetup:
					// If we repair duplicates and there is a reason to toast (e.g. locked meta.json file),
					// The ongoing UI activity focuses Bloom over top of the toast after a brief flash.
					// For that reason, we add a new stage for tasks that need to happen after the UI is updated.
					RepairDuplicates();
					Application.Idle -= ManageButtonsAtIdleTime; // stop running to this to do nothing.
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void RepairDuplicates()
		{
			var collectionPath = _model.TheOneEditableCollection.PathToDirectory;
			BookInfo.RepairDuplicateInstanceIds(collectionPath);
		}

		/// <summary>
		/// the primary could as well be called "the one editable collection"... the one at the top
		/// </summary>
		private void LoadPrimaryCollectionButtons()
		{
			_primaryCollectionReloadPending = false;
			_primaryCollectionFlow.SuspendLayout();
			_primaryCollectionFlow.Controls.Clear();
			//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
			var invisibleHackPartner = new Label() {Text = "", Width = 0};
			_primaryCollectionFlow.Controls.Add(invisibleHackPartner);
			var primaryCollectionHeader = new ListHeader() {ForeColor = Palette.LightTextAgainstDarkBackground};
			primaryCollectionHeader.Label.Text = _model.VernacularLibraryNamePhrase;
			primaryCollectionHeader.AdjustWidth();
			_primaryCollectionFlow.Controls.Add(primaryCollectionHeader);
			//_primaryCollectionFlow.SetFlowBreak(primaryCollectionHeader, true);
			_primaryCollectionFlow.Controls.Add(_menuTriangle);//NB: we're using a picture box instead of a button because the former can have transparency.
			LoadOneCollection(_model.GetBookCollections().First(), _primaryCollectionFlow);
			_primaryCollectionFlow.ResumeLayout();
		}

		private void LoadSourceCollectionButtons()
		{
			if (!_model.ShowSourceCollections)
			{
				_sourceBooksFlow.Visible = false;
				string lockNotice = LocalizationManager.GetString("CollectionTab.BookSourcesLockNotice",
																			   "This collection is locked, so new books cannot be added/removed.");

				var lockNoticeLabel = new Label()
					{
						Text = lockNotice,
						Size = new Size(_primaryCollectionFlow.Width - 20, 15),
						ForeColor = Palette.LightTextAgainstDarkBackground,
						Padding = new Padding(10, 0, 0, 0)
					};
				_primaryCollectionFlow.Controls.Add(lockNoticeLabel);
				return;
			}

			var collections = _model.GetBookCollections();
			//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
			var invisibleHackPartner = new Label() {Text = "", Width = 0, Anchor = AnchorStyles.None};

			_sourceBooksFlow.SuspendLayout();
			_sourceBooksFlow.Controls.Clear();
			var bookSourcesHeader = new ListHeader() { ForeColor = Palette.LightTextAgainstDarkBackground, Width = 450, Anchor = AnchorStyles.None };

			string shellSourceHeading = LocalizationManager.GetString("CollectionTab.SourcesForNewShellsHeading",
																				"Sources For New Shells");
			string bookSourceHeading = LocalizationManager.GetString("CollectionTab.BookSourceHeading",
																			   "Sources For New Books");
			bookSourcesHeader.Label.Text = _model.IsShellProject ? shellSourceHeading : bookSourceHeading;
			// Don't truncate the heading: see https://jira.sil.org/browse/BL-250.
			// But do allow it to wrap
			bookSourcesHeader.AdjustSize(_sourceBooksFlow.Width - SystemInformation.VerticalScrollBarWidth);
			invisibleHackPartner = new Label() {Text = "", Width = 0, Anchor = AnchorStyles.None };
			_sourceBooksFlow.Controls.Add(invisibleHackPartner);
			_sourceBooksFlow.Controls.Add(bookSourcesHeader);
			_sourceBooksFlow.SetFlowBreak(bookSourcesHeader, true);


			foreach (BookCollection collection in collections.Skip(1))
			{
				if (_sourceBooksFlow.Controls.Count > 0)
					_sourceBooksFlow.SetFlowBreak(_sourceBooksFlow.Controls[_sourceBooksFlow.Controls.Count - 1], true);

				int indexForHeader = _sourceBooksFlow.Controls.Count;
				if (LoadOneCollection(collection, _sourceBooksFlow))
				{
					//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
					invisibleHackPartner = new Label() {Text = "", Width = 0, Anchor = AnchorStyles.None };
					_sourceBooksFlow.Controls.Add(invisibleHackPartner);
					_sourceBooksFlow.Controls.SetChildIndex(invisibleHackPartner, indexForHeader);

					//We showed at least one book, so now go back and insert the header
					var collectionName = L10NSharp.LocalizationManager.GetDynamicString("Bloom", "CollectionTab." + collection.Name, collection.Name);
					var collectionHeader = new Label()
					{
						Text = collectionName,
						Size = GetSizeForLabel(collectionName, _headerFont),
						ForeColor = Palette.LightTextAgainstDarkBackground,
						Padding = new Padding(10, 0, 0, 0),
						Anchor = AnchorStyles.None
					};
					collectionHeader.Margin = new Padding(0, 10, 0, 0);
					collectionHeader.Font = _headerFont;
					_sourceBooksFlow.Controls.Add(collectionHeader);
					_sourceBooksFlow.Controls.SetChildIndex(collectionHeader, indexForHeader + 1);
					_sourceBooksFlow.SetFlowBreak(collectionHeader, true);
				}
			}

			AddFinalLinks();
			_sourceBooksFlow.ResumeLayout();
		}

		// We want to make the label a size that fits in the parent width.
		// Otherwise, the flow won't be any narrower than the label, other
		// things may not wrap properly, and part of the label will be hidden.
		Size GetSizeForLabel(string text, Font font)
		{
			if (string.IsNullOrEmpty(text))
				return new Size(20,0);
			var textSize = TextRenderer.MeasureText(text, font,
				// We need to leave room for the scroll bar, the margin outside the labels (6), the padding inside (10),
				// and a fudge factor for the frame of the control.
				new Size(_sourceBooksFlow.Width - SystemInformation.VerticalScrollBarWidth - 6 - 10 - 4, Int32.MaxValue),
				TextFormatFlags.WordBreak);
			return new Size(textSize.Width + 10, textSize.Height + 3);
		}

		private void _sourceBooksFlow_SizeChanged(object sender, EventArgs e)
		{
			_sourceBooksFlow.SuspendLayout();
			// We turn AutoScroll off and back on again as the only workaround I've found
			// for a bizarre problem in FlowLayoutPanel where the only time it notices
			// that the width of the widest child control changed is when the visibility of
			// the horizontal scroll bar changes. And since turning it off and on again
			// resets the scroll position, we remember it and try to put it back where it should be.
			// Note: it doesn't work to turn it off and straight on again at the end of the loop,
			// (perhaps only if some child changed size). I tried several variations.
			// It's probably possible to do a separate pass to figure out whether anything
			// is going to change size, but that feels like too much trouble, especially
			// since we eventually hope to replace this whole control with HTML.
			var oldPosition = _sourceBooksFlow.AutoScrollPosition;
			_sourceBooksFlow.AutoScroll = false;
			foreach (Control c in _sourceBooksFlow.Controls)
			{
				var label = c as Label;
				var header = c as ListHeader;
				if (header != null)
					header.AdjustSize(_sourceBooksFlow.Width - SystemInformation.VerticalScrollBarWidth);
				else if (label != null && !string.IsNullOrEmpty(label.Text))
					label.Size = GetSizeForLabel(label.Text, label.Font);
			}
			_sourceBooksFlow.ResumeLayout(true);
			_sourceBooksFlow.AutoScroll = true;
			_sourceBooksFlow.AutoScrollPosition = new Point(-oldPosition.X, -oldPosition.Y);
		}

		private void AddFinalLinks()
		{
			// Nothing to do currently. This was used to display the missing books link in a source collection.
		}

		/// <summary>
		/// Called at idle time after everything else is set up, and only when this tab is visible
		/// </summary>
		private void ImproveAndRefreshBookButtons()
		{
			ButtonRefreshInfo buttonRefreshInfo;
			if (!_buttonsNeedingSlowUpdate.TryDequeue(out buttonRefreshInfo))
				return;

			var button = buttonRefreshInfo.Button;
			var bookInfo = GetBookInfoFromButton(button);
			if (bookInfo == null)
				return;	// without a valid BookInfo, there's nothing we can do: we can't even report an error for the book.

			Book.Book book = null;	// This is really expensive to initialize, so we won't unless we really need it.
			bool badBook = false;	// Helps us not to try to load an invalid book twice.

			//Only go looking for a better title if the book hasn't already been localized when we first showed it.
			//The idea is, if we already have a localization mapping for this name, then
			// we're not going to get a better title by digging into the document itself and overriding what the localizer
			// chose to call it.
			// Note: currently (August 2014) the books that will have been localized are are those in the main "templates" section: Basic Book, Calendar, etc.
			if (button.Text == ShortenTitleIfNeeded(bookInfo.QuickTitleUserDisplay, button))
			{
				// Actually getting the HtmlDom may be rather expensive due to finding the actual HTML file and
				// converting it to XHTML first.
				// Maybe in this context we can rely on the collection settings for the languages?
				var langCodes = _model.CollectionSettings.GetAllLanguageCodes().ToList();
				var bestTitle = bookInfo.GetBestTitleForUserDisplay(langCodes);
				if (String.IsNullOrEmpty(bestTitle))
				{
					// Getting the book can be very slow for large books: do we really want to update the title enough to make the user wait?
					book = LoadBookAndBringItUpToDate(bookInfo, out badBook);
					if (book != null)
						bestTitle = book.TitleBestForUserDisplay;
					else
						bestTitle = bookInfo.QuickTitleUserDisplay;
				}
				var titleBestForUserDisplay = ShortenTitleIfNeeded(bestTitle, button);
				if (titleBestForUserDisplay != button.Text)
				{
					Debug.WriteLine(button.Text + " --> " + titleBestForUserDisplay);
					button.Text = titleBestForUserDisplay;
					toolTip1.SetToolTip(button, bestTitle);
				}
			}
			if (buttonRefreshInfo.ThumbnailRefreshNeeded)
			{
				// This is relatively rare, so we'll risk the really slow loading of the book.
				if (book == null && !badBook)
					book = LoadBookAndBringItUpToDate(bookInfo, out badBook);
				if (book != null)
					ScheduleRefreshOfOneThumbnail(book);
			}
		}

		private Book.Book LoadBookAndBringItUpToDate(BookInfo bookInfo, out bool badBook)
		{
			try
			{
				badBook = false;
				return _model.GetBookFromBookInfo(bookInfo);
			}
			catch (Exception error)
			{
				//skip over the dependency injection layer
				if (error.Source == "Autofac" && error.InnerException != null)
					error = error.InnerException;
				Logger.WriteEvent("There was a problem with the book at " + bookInfo.FolderPath + ". " + error.Message);
				if (!_alreadyReportedErrorDuringImproveAndRefreshBookButtons)
				{
					_alreadyReportedErrorDuringImproveAndRefreshBookButtons = true;
					SIL.Reporting.ErrorReport.NotifyUserOfProblem(error,
						"There was a problem with the book at {0}. \r\n\r\nClick the 'Details' button for more information.\r\n\r\nOther books may have this problem, but this is the only notice you will receive.\r\n\r\nSee 'Help:Show Event Log' for any further errors.",
						bookInfo.FolderPath);
				}
				badBook = true;
				return null;
			}
		}

		void OnBloomLibrary_Click(object sender, EventArgs e)
		{
			if (_model.IsShellProject)
			{
				// Display dialog making sure they know what they're doing
				var dialogResult = ShowBloomLibraryLinkVerificationDialog();
				if (dialogResult != DialogResult.OK)
					return;
			}
			SIL.Program.Process.SafeStart(UrlLookup.LookupUrl(UrlType.LibrarySite, BookTransfer.UseSandbox) + "/books");
		}

		DialogResult ShowBloomLibraryLinkVerificationDialog()
		{
			var dlg = new BloomLibraryLinkVerification();
			return dlg.GetVerification(this);
		}

		/// <summary>
		///
		/// </summary>
		/// <returns>True if the collection should be shown</returns>
		private bool LoadOneCollection(BookCollection collection, FlowLayoutPanel flowLayoutPanel)
		{
			collection.CollectionChanged += OnCollectionChanged;
			bool loadedAtLeastOneBook = false;
			foreach (var bookInfo in collection.GetBookInfos())
			{
				try
				{
					if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection || bookInfo.ShowThisBookAsSource())
					{
						AddOneBook(bookInfo, flowLayoutPanel, collection);
						loadedAtLeastOneBook = true;
					}
				}
				catch (Exception error)
				{
					ErrorReport.NotifyUserOfProblem(error, "Could not load the book at " + bookInfo.FolderPath);
				}
			}
			if (collection.ContainsDownloadedBooks)
			{
				_downloadedBookCollection = collection;
				collection.FolderContentChanged += DownLoadedBooksChanged;
				collection.WatchDirectory(); // In case another instance downloads a book.
				var bloomLibrayLink = new LinkLabel()
				{
					Text =
						LocalizationManager.GetString("CollectionTab.BloomLibraryLinkLabel",
																"Get more source books at BloomLibrary.org",
																"Shown at the bottom of the list of books. User can click on it and it will attempt to open a browser to show the Bloom Library"),
					Margin = new Padding(17, 0, 0, 0),
					LinkColor = Palette.LightTextAgainstDarkBackground
				};
				bloomLibrayLink.Size = GetSizeForLabel(bloomLibrayLink.Text, bloomLibrayLink.Font);
				bloomLibrayLink.Click += new EventHandler(OnBloomLibrary_Click);
				flowLayoutPanel.Controls.Add(bloomLibrayLink);
				loadedAtLeastOneBook = true;
			}
			// We could look for it in the main loop, but it seems safer to let the collection be fully constructed before
			// we start trying to select things in it.
			// Check the collection type to only allow the main, editable collection to select a book in the right preview pane.
			//   Collections loaded to populate "Sources for New Shells" will not be allowed to select a book.
			//   (FYI, when you load a book from BloomLibrary.org, it goes through a different code path to select the path)
			if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection && _bookSelection.CurrentSelection == null && !string.IsNullOrEmpty(Settings.Default.CurrentBookPath))
			{
				foreach (var bookInfo in collection.GetBookInfos())
				{
					if (bookInfo.FolderPath == Settings.Default.CurrentBookPath)
					{
						_bookSelection.SelectBook(_model.GetBookFromBookInfo(bookInfo, true));
						break;
					}
				}
			}
			return loadedAtLeastOneBook;
		}

	   private bool IsSuitableSourceForThisEditableCollection(BookInfo bookInfo)
		{
			return (_model.IsShellProject && bookInfo.IsSuitableForMakingShells) ||
				   (!_model.IsShellProject && bookInfo.IsSuitableForVernacularLibrary);
		}

		private Timer _newDownloadTimer;
		private HashSet<string> _changingFolders = new HashSet<string>();
		/// <summary>
		/// Called when a file system watcher notices a new book (or some similar change) in our downloaded books folder.
		/// This will happen on a thread-pool thread.
		/// Since we are updating the UI in response we want to deal with it on the main thread.
		/// This also has the effect that it can't happen in the middle of another LoadSourceCollectionButtons().
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="eventArgs"></param>
		private void DownLoadedBooksChanged(object sender, ProjectChangedEventArgs eventArgs)
		{
			SafeInvoke.InvokeIfPossible("LibraryListView update downloaded books",this,true,(Action) (() =>
			{
				// We may notice a change to the downloaded books directory before the other Bloom instance has finished
				// copying the new book there. Finishing should not take long, because the download is done...at worst
				// we have to copy the book on our own filesystem. Typically we only have to move the directory.
				// As a safeguard, wait half a second before we update things.
				if (_newDownloadTimer != null)
				{
					// Things changed again before we started the update! Forget the old update and wait until things
					// are stable for the required interval.
					_newDownloadTimer.Stop();
					_newDownloadTimer.Dispose();
				}
				_newDownloadTimer = new Timer();
				_newDownloadTimer.Tick += (o, args) =>
				{
					_newDownloadTimer.Stop();
					_newDownloadTimer.Dispose();
					_newDownloadTimer = null;

					// Updating the books involves selecting the modified book, which might involve changing
					// some files (e.g., adding a branding image, BL-4914), which could trigger this again.
					// So don't allow it to be triggered by changes to a folder we're already sending
					// notifications about.
					// (It's PROBABLY overkill to maintain a set of these...don't expect a notification about
					// one folder to trigger a change to another...but better safe than sorry.)
					// (Note that we don't need synchronized access to this dictionary, because all this
					// happens only on the UI thread.)
					if (!_changingFolders.Contains(eventArgs.Path))
					{
						try
						{
							_changingFolders.Add(eventArgs.Path);
							UpdateDownloadedBooks(eventArgs.Path);
						}
						finally
						{
							// Now we need to arrange to remove it again. But the critical time to suppress
							// a notification on the same folder isn't DURING the notification, but just AFTER
							// it, when the system is idle enough to notice the change in the folder.
							// I'm giving it a generous 10 seconds before paying attention to new changes to
							// this folder. Probably excessive, but this monitor is meant to catch new downloads;
							// it's unlikely the same book can be downloaded again within 10 seconds.
							var waitTimer = new Timer();
							waitTimer.Interval = 10000;
							waitTimer.Tick += (sender1, args1) =>
							{
								_changingFolders.Remove(eventArgs.Path);
								waitTimer.Stop();
								waitTimer.Dispose();
							};
							waitTimer.Start();
						}
					}
				};
				_newDownloadTimer.Interval = 500;
				_newDownloadTimer.Start();
			}));
		}

		private void UpdateDownloadedBooks(string pathToChangedBook)
		{
			var newBook = new BookInfo(pathToChangedBook, false);
			// It's always worth reloading...maybe we didn't have a button before because it was not
			// suitable for making vernacular books, but now it is! Or maybe the metadata changed some
			// other way...we want the button to have valid metadata for the book.
			// Optimize: maybe it would be worth trying to find the right place to insert or replace just one button?
			LoadSourceCollectionButtons();
			if (Enabled && CollectionTabIsActive)
				SelectBook(newBook);
		}

		/// <summary>
		/// Tells whether the collections tab is visible. If it isn't, we don't try to switch to show the selected book.
		/// In the current configuration, Parent.Parent.Parent is the LibraryView; this is added and removed from
		/// the higher level view depending on whether it is wanted, so if it has no higher parent it is hidden
		/// (although Visible is still true!) and we should not try to switch.
		/// One day we may enhance it so that we switch tabs and show it, but there are states where that would
		/// be dangerous.
		/// </summary>
		private bool CollectionTabIsActive
		{
			get { return Parent.Parent.Parent.Parent != null; }
		}

		private void AddOneBook(BookInfo bookInfo, FlowLayoutPanel flowLayoutPanel, BookCollection collection)
		{
			string title = bookInfo.QuickTitleUserDisplay;
			if (collection.IsFactoryInstalled)
				title = LocalizationManager.GetDynamicString("Bloom", "TemplateBooks.BookName." + title, title);

			var button = new Button
			{
				Size = new Size(ButtonWidth, ButtonHeight),
				Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont,
				TextImageRelation = TextImageRelation.ImageAboveText,
				ImageAlign = ContentAlignment.TopCenter,
				TextAlign = ContentAlignment.BottomCenter,
				FlatStyle = FlatStyle.Flat,
				ForeColor = Palette.LightTextAgainstDarkBackground,
				UseMnemonic = false, //otherwise, it tries to interpret '&' as a shortcut
				ContextMenuStrip = _bookContextMenu,
				AutoSize = false,
				Anchor = AnchorStyles.None,
				Tag = new BookButtonInfo(bookInfo, collection, collection == _model.TheOneEditableCollection)
			};

			button.MouseDown += OnClickBook; //we need this for right-click menu selection, which needs to 1st select the book
			//doesn't work: item.DoubleClick += (sender,arg)=>_model.DoubleClickedBook();

			button.Text = ShortenTitleIfNeeded(title, button);
			button.FlatAppearance.BorderSize = 1;
			button.FlatAppearance.BorderColor = BackColor;

			toolTip1.SetToolTip(button, title);

			Image thumbnail = Resources.PagePlaceHolder;
			_bookThumbnails.Images.Add(bookInfo.Id, thumbnail);
			button.ImageIndex = _bookThumbnails.Images.Count - 1;
			flowLayoutPanel.Controls.Add(button); // important to add it before RefreshOneThumbnail; uses parent flow to decide whether primary

			// Can't use this test until after we add button (uses parent info)
			if (!IsUsableBook(button))
				button.ForeColor = Palette.DisabledTextAgainstDarkBackColor;

			Image img;
			var refreshThumbnail = false;
			//review: we could do this at idle time, too:
			if (bookInfo.TryGetPremadeThumbnail(out img))
			{
				RefreshOneThumbnail(bookInfo, img);
			}
			else
			{
				//show this one for now, in the background someone will do the slow work of getting us a better one
				RefreshOneThumbnail(bookInfo,Resources.placeHolderBookThumbnail);
				refreshThumbnail = true;
			}
			_buttonsNeedingSlowUpdate.Enqueue(new ButtonRefreshInfo(button, refreshThumbnail));
		}

		private string ShortenTitleIfNeeded(string title, Button button)
		{
			var maxHeight = ButtonHeight - HtmlThumbNailer.ThumbnailOptions.DefaultHeight - (button.FlatAppearance.BorderSize * 2);

			// -2 because the text will wrap if there is not at least one pixel between the text and the border
			var width = button.Width - button.Margin.Horizontal - (button.FlatAppearance.BorderSize * 2) - 2;

			var targetSize = new Size(width, int.MaxValue);
			// WordBreak is necessary for sensible measurment of line widths...otherwise it ignores the width
			// constraint and puts all the text on one line.
			// NoPrefix suppresses special treatment of ampersand.
			var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
			var source = title;
			var firstLine = ""; // May be used if the first line starts with a long word; see below.
			using (var g = this.CreateGraphics())
			{
				var size = TextRenderer.MeasureText(g, source, button.Font, targetSize, flags);
				if (size.Height <= maxHeight && size.Width <= width)
					return source;
				var tooBig = source;
				var fits = source.Substring(0, 4); // include something not entirely trivial
				for (int i = 0; i < 2; i++) // trick to get a second iteration for long word on first line
				{
					while (tooBig.Length > fits.Length + 1)
					{
						var probe = source.Substring(0, (tooBig.Length + fits.Length)/2);
						size = TextRenderer.MeasureText(g, probe + "…", button.Font, targetSize, flags);
						if (size.Height <= maxHeight && size.Width <= width)
							fits = probe;
						else
							tooBig = probe;
					}
					if (i == 0 && size.Height <= maxHeight/2)
					{
						// Pesky TextRenderer won't break long words, but button layout code will.
						// If we got a long word on first line, the algorithm above will truncate
						// all the way down to ONE line. See if we can fit some more on the second line.
						// (Note that we don't need to consider the case of a one-line result that
						// contains white space. If it's possible to put even one short word on the
						// first line, that's what the TextRenderer and the button layout code both do.)

						// Enhance: this fix assumes that we are only showing two lines, even though
						// most of the code here is designed to be more general. If there is room for
						// three or more lines, the current code could still truncate to two if there
						// is a long word on the second. It's somewhat tricky to make it handle n lines:
						// we start to really need to know the line height to tell whether truncation
						// happened. The space that's available for more lines may not be exactly
						// the total minus the height of the first line. I decided to apply YAGNI.
						maxHeight -= size.Height;
						firstLine = fits;
						source = source.Substring(firstLine.Length);
						// Rather arbitrary, but 4 are pretty sure to fit, and trying to measure an
						// empty string might be a problem.
						if (source.Length > 4)
							fits = source.Substring(0, 4);
						else
							fits = source;

						tooBig = source;
					}
					else
					{
						// Already iterated, or what we have already takes two lines
						// (maybe the long word was on the second line).
						break;
					}
				}
				return firstLine + fits + "…";
			}
		}

		/// <summary>
		/// Make the result look like it's on a colored paper, or make it transparent for composing on top
		/// of some other image.
		/// </summary>
		private ImageAttributes MagentaToPaperColor(Color paperColor)
		{
			ImageAttributes imageAttributes = new ImageAttributes();
			ColorMap map = new ColorMap();
			map.OldColor =  Color.Magenta;
			map.NewColor = paperColor;
			imageAttributes.SetRemapTable(new ColorMap[] {map});
			return imageAttributes;
		}

		private void OnCollectionChanged(object sender, EventArgs e)
		{
			_primaryCollectionReloadPending = true;
		}


		private void OnClickBook(object sender, EventArgs e)
		{
			var thisBtn = (Button)sender;

			if (!IsUsableBook(thisBtn))
			{
				MessageBox.Show(LocalizationManager.GetString("CollectionTab.HiddenBookExplanationForSourceCollections", "Because this is a source collection, Bloom isn't offering any existing shells as sources for new shells. If you want to add a language to a shell, instead you need to edit the collection containing the shell, rather than making a copy of it. Also, the Wall Calendar currently can't be used to make a new Shell."));
				return;
			}
			var bookInfo = GetBookInfoFromButton(thisBtn);
			if (bookInfo == null)
				return;

			var lastClickTime = _lastClickTime;
			_lastClickTime = DateTime.Now;

			try
			{
				if (SelectedBook != null && bookInfo == SelectedBook.BookInfo)
				{
					//I couldn't get the DoubleClick event to work, so I rolled my own
					if (Control.MouseButtons == MouseButtons.Left &&
					    DateTime.Now.Subtract(lastClickTime).TotalMilliseconds < SystemInformation.DoubleClickTime)
					{
						_model.DoubleClickedBook();
					}
					else
					{
						// detect click on book dropdown menu
						var pt = thisBtn.PointToClient(MousePosition);
						if ((pt.X > thisBtn.Width - 12) && (pt.Y > thisBtn.Height - 12))
						{
							_bookTriangle_Click(thisBtn, pt);
						}
					}
					return; // already selected, nothing to do.
				}
			}
			catch (Exception error) // Review: is this needed now bulk of method refactored into SelectBook?
			{
				//skip over the dependency injection layer
				if (error.Source == "Autofac" && error.InnerException != null)
					error = error.InnerException;

				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom cannot display that book.");
			}
			SelectBook(bookInfo);
		}

		private void HighlightBookButtonAndShowContextMenuButton(BookInfo bookInfo)
		{
			foreach (var btn in AllBookButtons())
			{
				var bookButtonInfo = btn.Tag as BookButtonInfo;
				if (bookButtonInfo.BookInfo == bookInfo)
				{
					// BL-2678 don't display menu triangle if there's no menu to display
					if(!bookButtonInfo.HasNoContextMenu) btn.Paint += btn_Paint;
					btn.FlatAppearance.BorderColor = Palette.LightTextAgainstDarkBackground;
				}
				else
				{
					btn.Paint -= btn_Paint;
					btn.FlatAppearance.BorderColor = BackColor;
				}
			}
		}

		void btn_Paint(object sender, PaintEventArgs e)
		{
			var obj = (Button) sender;
			var rect = new Rectangle
			{
				X = obj.Width - _dropdownImage.Width - 3,
				Y = obj.Height - _dropdownImage.Height - 3,
				Width = _dropdownImage.Width,
				Height = _dropdownImage.Height
			};
			e.Graphics.DrawImage(_dropdownImage, rect);
		}

		private void SelectBook(BookInfo bookInfo)
		{
			try
			{
				_bookSelection.SelectBook(_model.GetBookFromBookInfo(bookInfo, true));

				_bookContextMenu.Enabled = true;
				//Debug.WriteLine("before selecting " + SelectedBook.Title);
				_model.SelectBook(SelectedBook);
				//Debug.WriteLine("after selecting " + SelectedBook.Title);
				//didn't help: _listView.Focus();//hack we were losing clicks
				SelectedBook.ContentsChanged -= new EventHandler(OnContentsOfSelectedBookChanged); //in case we're already subscribed
				SelectedBook.ContentsChanged += new EventHandler(OnContentsOfSelectedBookChanged);

				deleteMenuItem.Enabled = _model.CanDeleteSelection;
				_updateThumbnailMenu.Visible = _model.CanUpdateSelection;
				exportToWordOrLibreOfficeToolStripMenuItem.Visible = _model.CanExportSelection;
				_updateFrontMatterToolStripMenu.Visible = _model.CanUpdateSelection;
			}
			catch (Exception error)
			{
				//skip over the dependency injection layer
				if (error.Source == "Autofac" && error.InnerException != null)
					error = error.InnerException;

				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom cannot display that book.");
			}
		}

		private Book.Book SelectedBook
		{
			get { return _bookSelection.CurrentSelection; }
		}

		private Button SelectedButton
		{
			get
			{
				return AllBookButtons().FirstOrDefault(b => GetBookInfoFromButton(b) == SelectedBook.BookInfo);
			}
		}

		/// <summary>
		/// The image to show on the cover might have changed. Just make a note ot re-show it next time we're visible
		/// </summary>
		private void OnContentsOfSelectedBookChanged(object sender, EventArgs e)
		{
			_thumbnailRefreshPending = true;
		}

		private void OnBackColorChanged(object sender, EventArgs e)
		{
			_primaryCollectionFlow.BackColor = BackColor;
		}

		private void OnSelectedTabChanged(TabChangedDetails obj)
		{
			if(obj.To is LibraryView)
			{
				Application.Idle -= ManageButtonsAtIdleTime;
				Application.Idle += ManageButtonsAtIdleTime;
				Book.Book book = SelectedBook;
				if (book != null && SelectedButton != null)
				{
					var bestTitle = book.TitleBestForUserDisplay;
					SelectedButton.Text = ShortenTitleIfNeeded(bestTitle, SelectedButton);
					toolTip1.SetToolTip(SelectedButton, bestTitle);
					if (_thumbnailRefreshPending)
					{
						_thumbnailRefreshPending = false;
						ScheduleRefreshOfOneThumbnail(book);
					}
				}
				if (_primaryCollectionReloadPending)
				{
					LoadPrimaryCollectionButtons();
					// One reason to reload is that we created a new book. We need to go through the steps of selecting it
					// so that e.g. its menu options are properly configured.
					if (SelectedBook != null)
					{
						SelectBook(SelectedBook.BookInfo);
						ScheduleRefreshOfOneThumbnail(book);
					}
				}
			}
			else
			{
				Application.Idle -= ManageButtonsAtIdleTime;
			}
		}

		private void RefreshOneThumbnail(Book.BookInfo bookInfo, Image image)
		{
			if (IsDisposed)
				return;
			try
			{
				var imageIndex = _bookThumbnails.Images.IndexOfKey(bookInfo.Id);
				if (imageIndex > -1)
				{
					var button = FindBookButton(bookInfo);
					if (button == null || button.IsDisposed)
						return; // I (gjm) found that this condition occurred sometimes when testing BL-6100
					image = ImageUtils.CenterImageIfNecessary(MaxThumbnailSize, image, bookInfo.IsSuitableForMakingTemplates);
					_bookThumbnails.Images[imageIndex] = image;
					button.Image = IsUsableBook(button) ? image : MakeDim(image);
				}
			}

			catch (Exception e)
			{
				Logger.WriteEvent("Error refreshing thumbnail. "+e.Message);
#if DEBUG
				throw;
#endif
			}
		}

		bool IsUsableBook(Button bookButton)
		{
			//This caused more problems then it was worth for people editing source books they got off of BloomLibrary, or making a Bloompack of shells, etc.
			return true;

			// We'd prefer to use collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
			// but we don't have access to the collection at all the points where we need to evaluate this.
			// Depending on the parent like this unfortunately means we can't use this method until the button
			// has its parent.
			// Either way, the basic idea is that books in the main collection you are now editing are always usable.
			if (bookButton.Parent == _primaryCollectionFlow)
				return true;
			var bookInfo = GetBookInfoFromButton(bookButton);
			return IsSuitableSourceForThisEditableCollection(bookInfo);
		}

		// Adapted from http://tech.pro/tutorial/660/csharp-tutorial-convert-a-color-image-to-grayscale
		// Author claims this is about 20x faster than manipulating pixels directly (62 vs 1135ms for some image on some hardware).
		public static Bitmap MakeDim(Image original)
		{
			//create a blank bitmap the same size as original
			Bitmap newBitmap = new Bitmap(original.Width, original.Height);

			//get a graphics object from the new image
			using (Graphics g = Graphics.FromImage(newBitmap))
			{
				//create the grayscale ColorMatrix
				var colorMatrix = new ColorMatrix(
					new float[][]
					{
						// convert to greyscale: this (original) version leaves them too bright, and the distinction may be lost on color-blind
						//new float[] {.3f, .3f, .3f, 0, 0},
						//new float[] {.59f, .59f, .59f, 0, 0},
						//new float[] {.11f, .11f, .11f, 0, 0},
						//new float[] {0, 0, 0, 1, 0},
						//new float[] {0, 0, 0, 0, 1}

						// halve all color values to make darker--very similar to the chosen variant, but dark colors are strengthened.
						//new float[] {0.5f, 0, 0, 0, 0},
						//new float[] {0, 0.5f, 0, 0, 0},
						//new float[] {0, 0, 0.5f, 0, 0},
						//new float[] {0, 0, 0, 1, 0},
						//new float[] {0, 0, 0, 0, 1}

						// make it semi-transparent; this reduces contrast with background for all colors.
						new float[] {1.0f, 0, 0, 0, 0},
						new float[] {0, 1.0f, 0, 0, 0},
						new float[] {0, 0, 1.0f, 0, 0},
						new float[] {0, 0, 0, 0.4f, 0}, // the 0.4 here is what really does it.
						new float[] {0, 0, 0, 0, 1}
					});

				ImageAttributes attributes = new ImageAttributes();
				attributes.SetColorMatrix(colorMatrix);

				//draw the original image on the new image using the color matrix to adapt the colors
				g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
					0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
			}
			return newBitmap;
		}

		private Button FindBookButton(BookInfo bookInfo)
		{
			return AllBookButtons().FirstOrDefault(b => GetBookInfoFromButton(b) == bookInfo);
		}

		private IEnumerable<Button> AllBookButtons()
		{
			foreach(var btn in _primaryCollectionFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}

			foreach (var btn in _sourceBooksFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}
		}

		private void ScheduleRefreshOfOneThumbnail(Book.Book book)
		{
			_model.UpdateThumbnailAsync(book, new HtmlThumbNailer.ThumbnailOptions(), RefreshOneThumbnail, HandleThumbnailerErrror);
		}

		private void HandleThumbnailerErrror(Book.BookInfo bookInfo, Exception error)
		{
			RefreshOneThumbnail(bookInfo, Resources.Error70x70);
		}

		private void deleteMenuItem_Click(object sender, EventArgs e)
		{
			var button = FindBookButton(SelectedBook.BookInfo);
			if (_model.DeleteBook(SelectedBook))
			{
				Debug.Assert(button != null);	// what if we just deleted the last book that had been downloaded?
				if (button != null)
				{
					// BL-2678 it must be in one or the other, but now it could be
					// a book downloaded from BloomLibrary.org
					if (_primaryCollectionFlow.Controls.Contains(button))
						_primaryCollectionFlow.Controls.Remove(button);
					else
						_sourceBooksFlow.Controls.Remove(button);
				}
			}
		}

		private void _updateThumbnailMenu_Click(object sender, EventArgs e)
		{
			ScheduleRefreshOfOneThumbnail(SelectedBook);
		}

		private void OnBringBookUpToDate_Click(object sender, EventArgs e)
		{
			try
			{
				_model.BringBookUpToDate();
			}
			catch (Exception error)
			{
				var msg = LocalizationManager.GetString("Errors.ErrorUpdating",
					"There was a problem updating the book.  Restarting Bloom may fix the problem.  If not, please click the 'Details' button and report the problem to the Bloom Developers.");
				ErrorReport.NotifyUserOfProblem(error, msg);
			}
		}

		private void _openFolderOnDisk_Click(object sender, EventArgs e)
		{
			_model.OpenFolderOnDisk();
		}

		private void OnOpenAdditionalCollectionsFolderClick(object sender, EventArgs e)
		{
			PathUtilities.SelectFileInExplorer(ProjectContext.GetInstalledCollectionsDirectory());
		}

		private void OnVernacularProjectHistoryClick(object sender, EventArgs e)
		{
		#if Chorus
			using(var dlg = _historyAndNotesDialogFactory())
			{
				dlg.ShowDialog();
			}
		#endif
		}

		private void OnShowNotesMenu(object sender, EventArgs e)
		{
			#if Chorus
			using (var dlg = _historyAndNotesDialogFactory())
			{
				dlg.ShowNotesFirst = true;
				dlg.ShowDialog();
			}
			#endif
		}

		private void OnMakeBloomPackOfBook(object sender, EventArgs e)
		{
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				var extension = Path.GetExtension(_model.GetSuggestedBloomPackPath());
				var filename = _bookSelection.CurrentSelection.Storage.FileName;
				dlg.FileName = Path.ChangeExtension(filename, extension);
				dlg.Filter = "BloomPack|*.BloomPack";
				dlg.RestoreDirectory = true;
				dlg.OverwritePrompt = true;
				if (DialogResult.Cancel == dlg.ShowDialog())
				{
					return;
				}
				var folder = _bookSelection.CurrentSelection.Storage.FolderPath;
				_model.MakeSingleBookBloomPack(dlg.FileName, _bookSelection.CurrentSelection.Storage.FolderPath);
			}
		}

		private void _doChecksAndUpdatesOfAllBooksToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_model.DoUpdatesOfAllBooks();
		}

		private void _doChecksOfAllBooksToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_model.DoChecksOfAllBooks();
		}

		private void _rescueMissingImagesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using (var dlg = new FolderBrowserDialog())
			{
				dlg.ShowNewFolderButton = false;
				dlg.Description = "Select the folder where replacement images can be found";
				if (DialogResult.OK == dlg.ShowDialog())
				{
					_model.AttemptMissingImageReplacements(dlg.SelectedPath);
				}
			}
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (_newDownloadTimer != null))
			{
				_newDownloadTimer.Stop();
				_newDownloadTimer.Dispose();
			}
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			if (disposing && _downloadedBookCollection != null)
			{
				_downloadedBookCollection.StopWatchingDirectory();
				_downloadedBookCollection.FolderContentChanged -= DownLoadedBooksChanged;
			}
			base.Dispose(disposing);
			_disposed = true;
		}

		internal void MakeBloomPack(bool forReaderTools)
		{
			using (var dlg = new DialogAdapters.SaveFileDialogAdapter())
			{
				dlg.FileName = _model.GetSuggestedBloomPackPath();
				dlg.Filter = "BloomPack|*.BloomPack";
				dlg.RestoreDirectory = true;
				dlg.OverwritePrompt = true;
				if (DialogResult.Cancel == dlg.ShowDialog())
				{
					return;
				}
				_model.MakeBloomPack(dlg.FileName, forReaderTools);
			}
		}

		private void exportToWordOrLibreOfficeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			try
			{
				MessageBox.Show(LocalizationManager.GetString("CollectionTab.BookMenu.ExportDocMessage",
					"Bloom will now open this HTML document in your word processing program (normally Word or LibreOffice). You will be able to work with the text and images of this book. These programs normally don't do well with preserving the layout, so don't expect much."));
				var destPath = _bookSelection.CurrentSelection.GetPathHtmlFile().Replace(".htm", ".doc");
				_model.ExportDocFormat(destPath);
				PathUtilities.OpenFileInApplication(destPath);
				Analytics.Track("Exported To Doc format");
			}
			catch (IOException error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error.Message, "Could not export the book");
				Analytics.ReportException(error);
			}
			catch (Exception error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, "Could not export the book");
				Analytics.ReportException(error);
			}
		}

		private void makeReaderTemplateBloomPackToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using (var dlg = new MakeReaderTemplateBloomPackDlg())
			{
				dlg.SetLanguage(_model.LanguageName);
				dlg.SetTitles(_model.BookTitles);
				if (dlg.ShowDialog(this) != DialogResult.OK)
					return;
				MakeBloomPack(true);
			}
		}

		private void _menuButton_Click(object sender, EventArgs e)
		{
			_vernacularCollectionMenuStrip.Show(_menuTriangle, new Point(0, 0));
		}

		private class ButtonRefreshInfo
		{
			public ButtonRefreshInfo(Button button, bool thumbnailRefreshNeeded)
			{
				Button = button;
				ThumbnailRefreshNeeded = thumbnailRefreshNeeded;
			}
			public Button Button { get; set; }
			public bool ThumbnailRefreshNeeded { get; set; }
		}

		private void openCreateCollectionToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var workspaceView = GetWorkspaceView(this, typeof(WorkspaceView));
			if (workspaceView != null)
				workspaceView.OpenCreateLibrary();
		}

		private static WorkspaceView GetWorkspaceView(Control ctrl, Type workspaceViewType)
		{
			while (true)
			{
				var parent = ctrl.Parent;

				if (parent == null)
					return null;

				if (parent.GetType() == workspaceViewType)
					return (WorkspaceView) parent;

				ctrl = parent;
			}
		}


		private void _copyBook_Click(object sender, EventArgs e)
		{
			if (SelectedBook == null) return;

			var newBookDir = SelectedBook.Storage.Duplicate();

			// reload the collection
			_model.ReloadCollections();
			LoadPrimaryCollectionButtons();

			// select the new book
			var bookInfo = AllBookButtons().Select(GetBookInfoFromButton).FirstOrDefault(info => info.FolderPath == newBookDir);
			if (bookInfo != null)
			{
				SelectBook(bookInfo);
				HighlightBookButtonAndShowContextMenuButton(bookInfo);
			}
		}

		private void SaveAsBloomToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (SelectedBook == null) return;

			const string bloomFilter = "Bloom files (*.bloom)|*.bloom|All files (*.*)|*.*";

			OnBringBookUpToDate_Click(sender, e);

			var srcFolderName = SelectedBook.StoragePageFolder;

			// Save As dialog, initially proposing My Documents, then defaulting to last target folder
			// Review: Do we need to persist this to some settings somewhere, or just for the current run?
			var dlg = new SaveFileDialog
			{
				AddExtension = true,
				OverwritePrompt = true,
				DefaultExt = "bloom",
				FileName = Path.GetFileName(srcFolderName),
				Filter = bloomFilter,
				InitialDirectory = _previousTargetSaveAs != null ?
					_previousTargetSaveAs :
					Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
			};
			var result = dlg.ShowDialog(this);
			if (result != DialogResult.OK)
				return;

			var destFileName = dlg.FileName;
			_previousTargetSaveAs = Path.GetDirectoryName(destFileName);

			_model.SaveAsBloomFile(srcFolderName, destFileName);
		}

		private void _leveledReaderMenuItem_Click(object sender, EventArgs e)
		{
			_model.SetIsBookLeveled(!_model.IsBookLeveled);
		}

		private void _decodableReaderMenuItem_Click(object sender, EventArgs e)
		{
			_model.SetIsBookDecodable(!_model.IsBookDecodable);
		}
	}

	internal class BookButtonInfo
	{
		private readonly BookInfo _bookInfo;
		internal BookInfo BookInfo { get { return _bookInfo; } }

		private readonly BookCollection _collection;

		internal bool IsEditable { get { return _bookInfo.IsEditable; } }

		internal bool IsBLibraryBook { get { return _collection.ContainsDownloadedBooks; } }

		internal bool HasNoContextMenu { get { return _collection.IsFactoryInstalled; } }

		public BookButtonInfo(BookInfo bookInfo, BookCollection collection, bool isVernacular)
		{
			_bookInfo = bookInfo;
			_collection = collection;
		}
	}
}
