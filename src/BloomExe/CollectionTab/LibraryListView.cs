using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Palaso.Reporting;

namespace Bloom.CollectionTab
{
	public partial class LibraryListView : UserControl
	{
		public delegate LibraryListView Factory();//autofac uses this

		private readonly LibraryModel _model;
		private readonly BookSelection _bookSelection;
		private readonly HistoryAndNotesDialog.Factory _historyAndNotesDialogFactory;
		private Font _headerFont;
		private Font _editableBookFont;
		private Font _collectionBookFont;
		private bool _reshowPending;
		private DateTime _lastClickTime;
		private bool _vernacularCollectionReloadPending;
		private LinkLabel _missingBooksLink;
		private bool _disposed;

		/// <summary>
		/// we go through these at idle time, doing slow things like actually instantiating the book to get the title in prefered language
		/// A stack would be better for updating "the thing I just changed", but we're using a queue at the moment simply because we
		/// want you'd see at the top of the screen to update before what's at the bottom or offscreen
		/// </summary>
		private ConcurrentQueue<Button> _buttonsNeedingSlowUpdate;

		public LibraryListView(LibraryModel model, BookSelection bookSelection, SelectedTabChangedEvent selectedTabChangedEvent,
			HistoryAndNotesDialog.Factory historyAndNotesDialogFactory)
		{
			_model = model;
			_bookSelection = bookSelection;
			_historyAndNotesDialogFactory = historyAndNotesDialogFactory;
			_buttonsNeedingSlowUpdate = new ConcurrentQueue<Button>();
			selectedTabChangedEvent.Subscribe(OnSelectedTabChanged);
			InitializeComponent();
			_vernacularBooksFlow.HorizontalScroll.Visible = false;

			_vernacularBooksFlow.Controls.Clear();
			_vernacularBooksFlow.HorizontalScroll.Visible = false;
			_sourceBooksFlow.Controls.Clear();
			_sourceBooksFlow.HorizontalScroll.Visible = false;

			_headerFont = new Font(SystemFonts.DialogFont.FontFamily, (float)10.0, FontStyle.Bold);
			_editableBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);//, FontStyle.Bold);
			_collectionBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);

			//enhance: move to model
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);

			_settingsProtectionHelper.ManageComponent(_openFolderOnDisk);

			_showHistoryMenu.Visible = _showNotesMenu.Visible = Settings.Default.ShowSendReceive;

		}


		private void OnBookSelectionChanged(object sender, EventArgs e)
		{
//TODO
//            foreach (ListViewItem item in _listView.Items)
//            {
//                if(item.Tag == _bookSelection.CurrentSelection)
//                {
//                    item.Selected = true;
//                    break;
//                }
//            }
		}

		public int PreferredWidth
		{
			get { return 300; }
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			_vernacularCollectionReloadPending = true;
			Application.Idle += new EventHandler(LoadCollectionsOnFirstIdleTime);
		}

		void LoadCollectionsOnFirstIdleTime(object sender, EventArgs e)
		{
			if(_disposed) //could happen if a version update was detected on app launch
				return;

			Application.Idle -= new EventHandler(LoadCollectionsOnFirstIdleTime);
			if (!_vernacularCollectionReloadPending)
				return;
			ReloadCollectionButtons();

			Application.Idle += new EventHandler(ImproveBookButtonsAtIdleTime);
		}

		private void ImproveBookButtonsAtIdleTime(object sender, EventArgs e)
		{
			Button button;
			if(!_buttonsNeedingSlowUpdate.TryDequeue(out button))
				return;

			BookInfo bookInfo = button.Tag as BookInfo;
			var book = _model.GetBookFromBookInfo(bookInfo);
			var titleBestForUserDisplay = ShortenTitleIfNeeded(book.TitleBestForUserDisplay);
			if (titleBestForUserDisplay != button.Text)
			{
				Debug.WriteLine(button.Text +" --> "+titleBestForUserDisplay);
				button.Text = titleBestForUserDisplay;
			}
			if (button.ImageIndex==999)//!bookInfo.TryGetPremadeThumbnail(out unusedImage))
			{
				RecreateOneThumbnail(book);
			}
		}

		private void ReloadCollectionButtons()
		{
			_vernacularCollectionReloadPending = false;
			Cursor = Cursors.WaitCursor;
			Application.DoEvents(); //needed to get the wait cursor to show

			_vernacularBooksFlow.SuspendLayout();

			_vernacularBooksFlow.Controls.Clear();

			var collections = _model.GetBookCollections();

			var library = collections.First();
			//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
			var invisibleHackPartner = new Label() {Text = "", Width = 0};
			_vernacularBooksFlow.Controls.Add(invisibleHackPartner);
			var vernacularBooksHeader = new ListHeader() {ForeColor = Palette.TextAgainstDarkBackground};
			vernacularBooksHeader.Label.Text = _model.VernacularLibraryNamePhrase;
			_vernacularBooksFlow.Controls.Add(vernacularBooksHeader);
			_vernacularBooksFlow.SetFlowBreak(vernacularBooksHeader, true);
			LoadOneCollection(library, _vernacularBooksFlow);
			_vernacularBooksFlow.ResumeLayout();

			//NB: at this point we don't support refreshing the source collections (it's slow and we don't currently have a *reason* to ever do it)
			if (_sourceBooksFlow.Controls.Count < 2)
			{
				_sourceBooksFlow.Controls.Clear();
				var bookSourcesHeader = new ListHeader() {ForeColor = Palette.TextAgainstDarkBackground};

				string shellSourceHeading = L10NSharp.LocalizationManager.GetString("CollectionTab.sourcesForNewShellsHeading",
																					   "Sources For New Shells");
				string bookSourceHeading = L10NSharp.LocalizationManager.GetString("CollectionTab.bookSourceHeading",
																					  "Sources For New Books");
				bookSourcesHeader.Label.Text = _model.IsShellProject ? shellSourceHeading : bookSourceHeading;
				invisibleHackPartner = new Label() {Text = "", Width = 0};
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
						invisibleHackPartner = new Label() {Text = "", Width = 0};
						_sourceBooksFlow.Controls.Add(invisibleHackPartner);
						_sourceBooksFlow.Controls.SetChildIndex(invisibleHackPartner, indexForHeader);

						//We showed at least one book, so now go back and insert the header
						var collectionHeader = new Label()
							{
								Text = collection.Name,
								Size = new Size(_sourceBooksFlow.Width - 20, 15),
								ForeColor = Palette.TextAgainstDarkBackground,
								Padding = new Padding(10, 0, 0, 0)
							};
						collectionHeader.Margin = new Padding(0, 10, 0, 0);
						collectionHeader.Font = _headerFont;
						_sourceBooksFlow.Controls.Add(collectionHeader);
						_sourceBooksFlow.Controls.SetChildIndex(collectionHeader, indexForHeader + 1);
						_sourceBooksFlow.SetFlowBreak(collectionHeader, true);
					}
				}

				if (_model.IsShellProject)
				{
					_missingBooksLink = new LinkLabel()
						{
							Text =
								L10NSharp.LocalizationManager.GetString("CollectionTab.hiddenBooksNotice",
																		"Where's the rest?",
																		"Shown at the bottom of the list of books. User can click on it and get some explanation of why some books are hidden"),
							Width = 200,
							Margin = new Padding(0, 30, 0, 0),
							TextAlign = ContentAlignment.TopCenter,
							LinkColor = Palette.TextAgainstDarkBackground
						};

					_missingBooksLink.Click += new EventHandler(OnMissingBooksLink_Click);
					_sourceBooksFlow.Controls.Add(_missingBooksLink);
					_sourceBooksFlow.SetFlowBreak(_missingBooksLink, true);
				}
			}

			Cursor = Cursors.Default;
		}

		void OnMissingBooksLink_Click(object sender, EventArgs e)
		{
			if (_model.IsShellProject)
			{
				MessageBox.Show(L10NSharp.LocalizationManager.GetString("CollectionTab.hiddenBookExplanationForSourceCollections", "Because this is a source collection, Bloom isn't offering any existing shells as sources for new shells. If you want to add a language to a shell, instead you need to edit the collection containing the shell, rather than making a copy of it. Also, the Wall Calendar currently can't be used to make a new Shell."), _missingBooksLink.Text);
			}
			else
			{
				//MessageBox.Show(L10NSharp.LocalizationManager.GetString("hiddenBookExplanationForVernacularCollections", "Because this is a vernacular collection, Bloom isn't offering all the same."));
			}
		}

		private bool LoadOneCollection(BookCollection collection, FlowLayoutPanel flowLayoutPanel)
		{
			collection.CollectionChanged += OnCollectionChanged;
			bool loadedAtLeastOneBook = false;
			foreach (Book.BookInfo bookInfo in collection.GetBookInfos())
			{
				try
				{
					var isSuitableSourceForThisEditableCollection = (_model.IsShellProject && bookInfo.IsSuitableForMakingShells) ||
							  (!_model.IsShellProject && bookInfo.IsSuitableForVernacularLibrary);

					if (isSuitableSourceForThisEditableCollection || collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
					{
						if (!bookInfo.IsExperimental || Settings.Default.ShowExperimentalBooks)
						{
							loadedAtLeastOneBook = true;
							AddOneBook(bookInfo, flowLayoutPanel);
						}
					}
				}
				catch (Exception error)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,"Could not load the book at "+bookInfo.FolderPath);
				}
			}
			return loadedAtLeastOneBook;
		}

		private void AddOneBook(Book.BookInfo bookInfo, FlowLayoutPanel flowLayoutPanel)
		{
			var button = new Button(){Size=new Size(90,110)};
			button.Text = ShortenTitleIfNeeded(bookInfo.QuickTitleUserDisplay);
			button.TextImageRelation = TextImageRelation.ImageAboveText;
			button.ImageAlign = ContentAlignment.TopCenter;
			button.TextAlign = ContentAlignment.BottomCenter;
			button.FlatStyle = FlatStyle.Flat;
			button.ForeColor = Palette.TextAgainstDarkBackground ;
			button.FlatAppearance.BorderSize = 0;
			button.ContextMenuStrip = _bookContextMenu;
			button.MouseDown += OnClickBook; //we need this for right-click menu selection, which needs to 1st select the book
			//doesn't work: item.DoubleClick += (sender,arg)=>_model.DoubleClickedBook();

			button.Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont;


			button.Tag=bookInfo;


			Image thumbnail = Resources.PagePlaceHolder;;
			_bookThumbnails.Images.Add(bookInfo.Id, thumbnail);
			button.ImageIndex = _bookThumbnails.Images.Count - 1;
			flowLayoutPanel.Controls.Add(button);

			Image img;

			//review: we could do this at idle time, too:
			if (bookInfo.TryGetPremadeThumbnail(out img))
			{
				RefreshOneThumbnail(bookInfo, img);
			}
			else
			{
				//show this one for now, in the background someone will do the slow work of getting us a better one
				RefreshOneThumbnail(bookInfo,Resources.placeHolderBookThumbnail);
				//hack to signal that we need to make a real one when get a chance
				button.ImageIndex = 999;
			}
			_buttonsNeedingSlowUpdate.Enqueue(button);

//			bookInfo.GetThumbNailOfBookCoverAsync(bookInfo.Type != Book.Book.BookType.Publication,
//				                                  image => RefreshOneThumbnail(bookInfo, image),
//												  error=> RefreshOneThumbnail(bookInfo, Resources.Error70x70));

		}

		private string ShortenTitleIfNeeded(string title)
		{
			int kMaxCaptionLetters = 17;
			return title.Length > kMaxCaptionLetters ? title.Substring(0, kMaxCaptionLetters-2) + "…" : title;
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
			_vernacularCollectionReloadPending = true;
		}

		private void OnClickBook(object sender, EventArgs e)
		{
			try
			{
				BookInfo bookInfo = ((Button)sender).Tag as BookInfo;
				if (bookInfo == null)
					return;

				if (SelectedBook!=null && bookInfo == SelectedBook.BookInfo)
				{
					//I couldn't get the DoubleClick event to work, so I rolled my own
					if (Control.MouseButtons == MouseButtons.Left  && DateTime.Now.Subtract(_lastClickTime).Milliseconds < 500)
					{
						_model.DoubleClickedBook();
						return;
					}
				}
				else
				{
					_bookSelection.SelectBook(_model.GetBookFromBookInfo(bookInfo));
				}

				_lastClickTime = DateTime.Now;

				_bookContextMenu.Enabled = true;
				Debug.WriteLine("before selecting " + SelectedBook.Title);
				_model.SelectBook(SelectedBook);
				Debug.WriteLine("after selecting " + SelectedBook.Title);
				//didn't help: _listView.Focus();//hack we were losing clicks
				SelectedBook.ContentsChanged -= new EventHandler(OnContentsOfSelectedBookChanged); //in case we're already subscribed
				SelectedBook.ContentsChanged += new EventHandler(OnContentsOfSelectedBookChanged);

				deleteMenuItem.Enabled = _model.CanDeleteSelection;
				_updateThumbnailMenu.Visible = _model.CanUpdateSelection;
				_updateFrontMatterToolStripMenu.Visible = _model.CanUpdateSelection;
			}
			catch (Exception err)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(err, "Bloom cannot display that book.");
			}
		}

		private Book.Book SelectedBook
		{
			set
			{
				foreach (var btn in AllBookButtons())
				{
					btn.BackColor = btn.Tag==value ? Color.DarkGray : _vernacularBooksFlow.BackColor;
				}
			}
			get { return _bookSelection.CurrentSelection; }
		}

		private Button SelectedButton
		{
			get
			{
				return AllBookButtons().FirstOrDefault(b => b.Tag == SelectedBook);
			}
		}

		/// <summary>
		/// The image to show on the cover might have changed. Just make a note ot re-show it next time we're visible
		/// </summary>
		private void OnContentsOfSelectedBookChanged(object sender, EventArgs e)
		{
			_reshowPending = true;
		}

		private void OnBackColorChanged(object sender, EventArgs e)
		{
			_vernacularBooksFlow.BackColor = BackColor;
		}

		private void OnSelectedTabChanged(TabChangedDetails obj)
		{
			if(obj.To is LibraryView)
			{
				Book.Book book = SelectedBook;
				if (book != null && SelectedButton != null)
				{
					SelectedButton.Text = ShortenTitleIfNeeded(book.TitleBestForUserDisplay);

					if (_reshowPending)
					{
						_reshowPending = false;
						RecreateOneThumbnail(book);
					}
				}
				if (_vernacularCollectionReloadPending)
				{
					ReloadCollectionButtons();
				}
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
					_bookThumbnails.Images[imageIndex] = image;
					var button = FindBookButton(bookInfo);
					button.Image = image;
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

		private Button FindBookButton(Book.BookInfo bookInfo)
		{
			return AllBookButtons().FirstOrDefault(b => b.Tag == bookInfo);
		}

		private IEnumerable<Button> AllBookButtons()
		{
			foreach(var btn in _vernacularBooksFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}

			foreach (var btn in _sourceBooksFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}
		}

		private void RecreateOneThumbnail(Book.Book book)
		{
			_model.UpdateThumbnailAsync(book, RefreshOneThumbnail, HandleThumbnailerErrror);
		}

		private void HandleThumbnailerErrror(Book.BookInfo bookInfo, Exception error)
		{
			RefreshOneThumbnail(bookInfo, Resources.Error70x70);
		}

		private void deleteMenuItem_Click(object sender, EventArgs e)
		{
			var button = AllBookButtons().FirstOrDefault(b => b.Tag == SelectedBook.BookInfo);
			_model.DeleteBook(SelectedBook);
			//ReloadCollectionButtons();
			Debug.Assert(button != null && _vernacularBooksFlow.Controls.Contains(button));
			if (button != null && _vernacularBooksFlow.Controls.Contains(button))
			{
				_vernacularBooksFlow.Controls.Remove(button);
			}
		}

		private void _updateThumbnailMenu_Click(object sender, EventArgs e)
		{
			RecreateOneThumbnail(SelectedBook);
		}

		private void OnBringBookUpToDate_Click(object sender, EventArgs e)
		{
			_model.BringBookUpToDate();
		}

		private void _openFolderOnDisk_Click(object sender, EventArgs e)
		{
			_model.OpenFolderOnDisk();
		}

		private void OnOpenAdditionalCollectionsFolderClick(object sender, EventArgs e)
		{
			Process.Start(ProjectContext.InstalledCollectionsDirectory);
		}

		private void OnVernacularProjectHistoryClick(object sender, EventArgs e)
		{
			using(var dlg = _historyAndNotesDialogFactory())
			{
				dlg.ShowDialog();
			}
		}

		private void OnShowNotesMenu(object sender, EventArgs e)
		{
			using (var dlg = _historyAndNotesDialogFactory())
			{
				dlg.ShowNotesFirst = true;
				dlg.ShowDialog();
			}
		}

		private void label3_Click(object sender, EventArgs e)
		{

		}

		private void _vernacularCollectionMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
		{

		}

		private void _doChecksAndUpdatesOfAllBooksToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_model.DoChecksAndUpdatesOfAllBooks();
		}


		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
			_disposed = true;
		}

		/// <summary>
		/// Occasionally, when select a book, the Bloom App itself loses focus. I assume this is a gecko-related issue.
		/// You can see it happen because the title bar of the application changes to the Windows unselected color (lighter).
		/// And then, if you click on a tab, the click is swallowed selecting the app, and you have to click again.
		///
		/// So, this occasionally checks that the Workspace control has focus, and if it doesn't, pulls it back here.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
//		private void _keepFocusTimer_Tick(object sender, EventArgs e)
//		{
//			if(Visible)
//			{
//				var findForm = FindForm();//visible is worthless, but FindForm() happily does fail when we aren't visible.
//
//				if (findForm != null && !findForm.ContainsFocus)
//				{
//				//	Focus();
//
//					//Debug.WriteLine("Grabbing back focus");
//				}
//			}
//		}
	}
}