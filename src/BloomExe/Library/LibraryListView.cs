using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Palaso.IO;


namespace Bloom.Library
{
	public partial class LibraryListView : UserControl
	{
		public delegate LibraryListView Factory();//autofac uses this

		private readonly LibraryModel _model;
		private readonly BookSelection _bookSelection;
		private readonly SelectedTabChangedEvent _selectedTabChangedEvent;
		private Pen _boundsPen;
		private Font _headerFont;
		private Font _editableBookFont;
		private Font _collectionBookFont;
		private bool _reshowPending;
		private DateTime _lastClickTime;
		private bool _collectionLoadPending;

		public LibraryListView(LibraryModel model, BookSelection bookSelection, SelectedTabChangedEvent selectedTabChangedEvent)
		{
			_model = model;
			_bookSelection = bookSelection;
			selectedTabChangedEvent.Subscribe(OnSelectedTabChanged);
			InitializeComponent();
			_libraryFlow.HorizontalScroll.Visible = false;

			_libraryFlow.Controls.Clear();
			_libraryFlow.HorizontalScroll.Visible = false;
			_collectionFlow.Controls.Clear();
			_collectionFlow.HorizontalScroll.Visible = false;

			_headerFont = new Font(SystemFonts.DialogFont.FontFamily, (float)10.0, FontStyle.Bold);
			_editableBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);//, FontStyle.Bold);
			_collectionBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);

			//_listView.OwnerDraw = true;
			 //_listView.DrawItem+=new DrawListViewItemEventHandler(_listView_DrawItem);
			_boundsPen = new Pen(Brushes.DarkGray, 2);
			//enhance: move to model
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);

			_settingsProtectionHelper.ManageComponent(_openFolderOnDisk);

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

			_collectionLoadPending = true;
			Application.Idle += new EventHandler(LoadCollectionsAtIdleTime);
		}

		void LoadCollectionsAtIdleTime(object sender, EventArgs e)
		{
			if (!_collectionLoadPending)
				return;
			_collectionLoadPending = false;
			Cursor = Cursors.WaitCursor;
			Application.DoEvents();//needed to get the wait cursor to show

			_libraryFlow.SuspendLayout();

			_libraryFlow.Controls.Clear();

			var collections = _model.GetBookCollections();

			var library = collections.First();
			//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
			var invisibleHackPartner = new Label() { Text = "", Width = 0 };
			_libraryFlow.Controls.Add(invisibleHackPartner);
			var libraryHeader = new ListHeader() {ForeColor = Palette.TextAgainstDarkBackground};
			libraryHeader.Label.Text = _model.VernacularLibraryNamePhrase;
			_libraryFlow.Controls.Add(libraryHeader);
			_libraryFlow.SetFlowBreak(libraryHeader, true);
			LoadOneCollection(library, _libraryFlow);

			_collectionFlow.Controls.Clear();
			var bookSourcesHeader = new ListHeader() { ForeColor = Palette.TextAgainstDarkBackground };

			string shellSourceHeading = Localization.LocalizationManager.GetString("sourcesForNewShellsHeading", "Sources For New Shells");
			string bookSourceHeading = Localization.LocalizationManager.GetString("bookSourceHeading", "Sources For New Books");
			bookSourcesHeader.Label.Text = _model.IsShellProject ? shellSourceHeading : bookSourceHeading;
			 invisibleHackPartner = new Label() { Text = "", Width = 0 };
			 _collectionFlow.Controls.Add(invisibleHackPartner);
			 _collectionFlow.Controls.Add(bookSourcesHeader);
			_collectionFlow.SetFlowBreak(bookSourcesHeader,true);

			foreach (BookCollection collection in _model.GetBookCollections().Skip(1))
			{
				if (_collectionFlow.Controls.Count > 0)
					_collectionFlow.SetFlowBreak(_collectionFlow.Controls[_collectionFlow.Controls.Count - 1], true);

				int indexForHeader = _collectionFlow.Controls.Count;
				if(LoadOneCollection(collection, _collectionFlow))
				{
					//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
					invisibleHackPartner = new Label() { Text = "", Width = 0 };
					_collectionFlow.Controls.Add(invisibleHackPartner);
					_collectionFlow.Controls.SetChildIndex(invisibleHackPartner, indexForHeader);

					//We showed at least one book, so now go back and insert the header
					var collectionHeader = new Label() { Text = collection.Name, Size = new Size(_collectionFlow.Width - 20, 15), ForeColor = Palette.TextAgainstDarkBackground, Padding = new Padding(10, 0, 0, 0) };
					collectionHeader.Margin = new Padding(0, 10, 0, 0);
					collectionHeader.Font = _headerFont;
					_collectionFlow.Controls.Add(collectionHeader);
					_collectionFlow.Controls.SetChildIndex(collectionHeader, indexForHeader+1);
					_collectionFlow.SetFlowBreak(collectionHeader, true);
				}
			}
			_libraryFlow.ResumeLayout();
			Cursor = Cursors.Default;
		}

		private bool LoadOneCollection(BookCollection collection, FlowLayoutPanel flowLayoutPanel)
		{
			collection.CollectionChanged += OnCollectionChanged;
			bool loadedAtLeastOneBook = false;
			foreach (Book.Book book in collection.GetBooks())
			{
				try
				{
					var isSuitableSourceForThisEditableCollection = (_model.IsShellProject && book.IsSuitableForMakingShells) ||
							  (!_model.IsShellProject && book.IsSuitableForVernacularLibrary);

					if(isSuitableSourceForThisEditableCollection || collection.Type== BookCollection.CollectionType.TheOneEditableCollection)
					{
						loadedAtLeastOneBook = true;
						AddOneBook(book, flowLayoutPanel);
					}
				}
				catch (Exception error)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,"Could not load the book at "+book.FolderPath);
				}

			}
			return loadedAtLeastOneBook;
		}

		private void AddOneBook(Book.Book book, FlowLayoutPanel flowLayoutPanel)
		{
			var item = new Button(){Size=new Size(90,110)};
			item.Text = GetTitleToDisplay(book);
			item.TextImageRelation = TextImageRelation.ImageAboveText;
			item.ImageAlign = ContentAlignment.TopCenter;
			item.TextAlign = ContentAlignment.BottomCenter;
			item.FlatStyle = FlatStyle.Flat;
			item.ForeColor = Palette.TextAgainstDarkBackground;
			item.FlatAppearance.BorderSize = 0;
			item.ContextMenuStrip = contextMenuStrip1;
			item.MouseDown += OnClickBook; //we need this for right-click menu selection, which needs to 1st select the book
			//doesn't work: item.DoubleClick += (sender,arg)=>_model.DoubleClickedBook();

			item.Font = book.IsInEditableLibrary ? _editableBookFont : _collectionBookFont;


			item.Tag=book;


			Image thumbnail = Resources.PagePlaceHolder;;
			_bookThumbnails.Images.Add(book.Id, thumbnail);
			item.ImageIndex = _bookThumbnails.Images.Count - 1;
			flowLayoutPanel.Controls.Add(item);

			book.GetThumbNailOfBookCoverAsync(book.Type != Book.Book.BookType.Publication,
												  image => RefreshOneThumbnail(book, image),
												  error=> RefreshOneThumbnail(book, Resources.Error70x70));

		}

		private string GetTitleToDisplay(Book.Book book)
		{
			int kMaxCaptionLetters = 17;
			var title = book.TitleBestForUserDisplay;
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
			_collectionLoadPending = true;
		}

		private void OnClickBook(object sender, EventArgs e)
		{
			try
			{
				Book.Book book = ((Button)sender).Tag as Book.Book;
				if (book == null)
					return;

				//I couldn't get the DoubleClick event to work, so I rolled my own
				if(Control.MouseButtons == MouseButtons.Left && book==SelectedBook && DateTime.Now.Subtract(_lastClickTime).Milliseconds<500)
				{
					_model.DoubleClickedBook();
					return;
				}
				_lastClickTime = DateTime.Now;

				SelectedBook = book;
				contextMenuStrip1.Enabled = true;
				Debug.WriteLine("before selecting " + book.Title);
				_model.SelectBook(book);
				Debug.WriteLine("after selecting " + book.Title);
				//didn't help: _listView.Focus();//hack we were losing clicks
				book.ContentsChanged -= new EventHandler(OnContentsOfSelectedBookChanged); //in case we're already subscribed
				book.ContentsChanged += new EventHandler(OnContentsOfSelectedBookChanged);

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
					btn.BackColor = btn.Tag==value ? Color.DarkGray : _libraryFlow.BackColor;
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
			_libraryFlow.BackColor = BackColor;
		}


		private void OnSelectedTabChanged(TabChangedDetails obj)
		{
			if(obj.To is LibraryView)
			{
				Book.Book book = SelectedBook;
				if (book == null || SelectedButton == null)
					return;

				SelectedButton.Text = GetTitleToDisplay(book);

				if (_reshowPending)
				{
					_reshowPending = false;
					RecreateOneThumbnail(book);
				}
			}
		}


		private void RefreshOneThumbnail(Book.Book book, Image image)
		{
			if (IsDisposed)
				return;
			var imageIndex = _bookThumbnails.Images.IndexOfKey(book.Id);
			if (imageIndex > -1)
			{
				_bookThumbnails.Images[imageIndex] = image;
				var button = FindBookButton(book);
				button.Image = image;
			}
		}

		private Button FindBookButton(Book.Book book)
		{
			return AllBookButtons().FirstOrDefault(b => b.Tag == book);
		}

		private IEnumerable<Button> AllBookButtons()
		{
			foreach(var btn in _libraryFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}

			foreach (var btn in _collectionFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}
		}

		private void RecreateOneThumbnail(Book.Book book)
		{
			_model.UpdateThumbnailAsync(RefreshOneThumbnail, HandleThumbnailerErrror);
		}

		private void HandleThumbnailerErrror(Book.Book book, Exception error)
		{
			RefreshOneThumbnail(book, Resources.Error70x70);
		}

		private void deleteMenuItem_Click(object sender, EventArgs e)
		{
			_model.DeleteBook(SelectedBook);
		}

		private void _updateThumbnailMenu_Click(object sender, EventArgs e)
		{
		RecreateOneThumbnail(SelectedBook);
		}

		private void _updateFrontMatterToolStripMenu_Click(object sender, EventArgs e)
		{
			_model.UpdateFrontMatter();
		}

		private void _openFolderOnDisk_Click(object sender, EventArgs e)
		{
			_model.OpenFolderOnDisk();
		}

		private void OnOpenAdditionalCollectionsFolderClick(object sender, EventArgs e)
		{
			Process.Start(ProjectContext.InstalledCollectionsDirectory);
		}

		private void LibraryListView_Load(object sender, EventArgs e)
		{

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