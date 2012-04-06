using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
		private Pen _boundsPen;
		private Font _headerFont;
		private Font _editableBookFont;
		private Font _collectionBookFont;
		private bool _reshowPending;

		public LibraryListView(LibraryModel model,  BookSelection bookSelection)
		{
			_model = model;
			_bookSelection = bookSelection;
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
		}

		void _listView_DrawItem(object sender, DrawListViewItemEventArgs e)
		{
			if(e.Item.Selected )
			{
				var r = e.Bounds;
				r.Inflate(-1,-1);
				e.Graphics.DrawRectangle(_boundsPen,r);
			}
			e.DrawDefault = true;
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

			Application.Idle += new EventHandler(LoadCollectionsAtIdleTime);

		}

		void LoadCollectionsAtIdleTime(object sender, EventArgs e)
		{
			Application.Idle -= new EventHandler(LoadCollectionsAtIdleTime);
			_libraryFlow.Controls.Clear();

			var collections = _model.GetBookCollections();

			var library = collections.First();
			//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
			var invisibleHackPartner = new Label() { Text = "", Width = 0 };
			_libraryFlow.Controls.Add(invisibleHackPartner);
			var libraryHeader = new ListHeader() {ForeColor = Color.White};
			libraryHeader.Label.Text = string.Format("{0} Books", _model.LanguageName);
			//libraryHeader.BorderStyle = BorderStyle.FixedSingle;
			_libraryFlow.Controls.Add(libraryHeader);
			_libraryFlow.SetFlowBreak(libraryHeader, true);
			LoadOneCollection(library, _libraryFlow);

			var bookSourcesHeader = new ListHeader() { ForeColor = Color.White };
			bookSourcesHeader.Label.Text = "Sources For New Books";
			 invisibleHackPartner = new Label() { Text = "", Width = 0 };
			 _collectionFlow.Controls.Add(invisibleHackPartner);
			 _collectionFlow.Controls.Add(bookSourcesHeader);
			_collectionFlow.SetFlowBreak(bookSourcesHeader,true);

			foreach (BookCollection collection in _model.GetBookCollections().Skip(1))
			{
				if (_collectionFlow.Controls.Count > 0)
					_collectionFlow.SetFlowBreak(_collectionFlow.Controls[_collectionFlow.Controls.Count - 1], true);

				//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
				 invisibleHackPartner = new Label() { Text = "", Width=0 };
				_collectionFlow.Controls.Add(invisibleHackPartner);

				var collectionHeader = new Label() {Text = collection.Name, ForeColor=Color.White, Padding=new Padding(10,0,0,0)};
				//collectionHeader.Height = 15;
				collectionHeader.Margin = new Padding(0, 10, 0, 0);
				collectionHeader.Font = _headerFont;
				_collectionFlow.Controls.Add(collectionHeader);
				_collectionFlow.SetFlowBreak(collectionHeader, true);

				LoadOneCollection(collection, _collectionFlow);
			}
		}

		private void LoadOneCollection(BookCollection collection, FlowLayoutPanel flowLayoutPanel)
		{
		/*	if (group.Tag == collection)
			{
				//this code I wrote is so lame...
				var x = new List<ListViewItem>();
				foreach (ListViewItem item in _listView.Items)
				{
					x.Add(item);
				}

				foreach (var listViewItem in x)
				{
					if(listViewItem.Group == group)
					{
						_listView.Items.Remove(listViewItem);
					}
				}
				group.Items.Clear(); //we are just updating this group
			}
			else
			{
				if (collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
				{
					collection.CollectionChanged += OnCollectionChanged;
				}
				group.Tag = collection;
			}
		 */
			collection.CollectionChanged += OnCollectionChanged;

			foreach (Book.Book book in collection.GetBooks())
			{
				try
				{
					AddOneBook(book, flowLayoutPanel);
				}
				catch (Exception error)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,"Could not load the book at "+book.FolderPath);
				}

			}
//			if(group.Items.Count ==0)
//			{
//    			ListViewItem item = new ListViewItem(" ", 0);
//    			item.Tag=null;
//				item.ImageIndex = -1;
//    			item.Group = group;
//				_listView.Items.Add(item);
//			}
		}

		private void AddOneBook(Book.Book book, FlowLayoutPanel flowLayoutPanel)
		{
			var item = new Button(){Size=new Size(90,110)};
			item.Text = book.Title;
			item.TextImageRelation = TextImageRelation.ImageAboveText;
			item.FlatStyle = FlatStyle.Flat;
			item.ForeColor = Color.White;
			item.FlatAppearance.BorderSize = 0;
			item.ContextMenuStrip = contextMenuStrip1;
			item.MouseDown += OnClickBook; //we need this for right-click menu selection, which needs to 1st select the book

			item.Font = book.IsInEditableLibrary ? _editableBookFont : _collectionBookFont;


			item.Tag=book;


			Image thumbnail = Resources.PagePlaceHolder;;
			_bookThumbnails.Images.Add(book.Id, thumbnail);
			item.ImageIndex = _bookThumbnails.Images.Count - 1;
			flowLayoutPanel.Controls.Add(item);

			book.GetThumbNailOfBookCoverAsync(book.Type != Book.Book.BookType.Publication,
												  image => RefreshOneThumbnail(book, image));

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
			Application.Idle += new EventHandler(LoadCollectionsAtIdleTime);
//    		foreach (ListViewGroup group in _listView.Groups)
//    		{
//    			if(group.Tag == sender)
//    			{
//    				LoadOneCollection((BookCollection) sender, group);
//    			    break;
//    			}
//    		}
		}

		private void OnClickBook(object sender, EventArgs e)
		{
			try
			{
//				if (_listView.SelectedItems.Count == 0)
//				{
//					return;
//				}
				Book.Book book = ((Button)sender).Tag as Book.Book;
				if (book == null)
					return;
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

		private void OnVisibleChanged(object sender, EventArgs e)
		{
			if(Visible )
			{
				Book.Book book = SelectedBook;
				if (book == null)
					return;

				//we don't currently have a "reshow" flag for just updating the title
				//update the label from the title
 //TODO
//				var listItem = (from ListViewItem i in _listView.Items where i.Tag == book select i).FirstOrDefault();
//                Debug.Assert(listItem!=null);
//                if (listItem!=null)
//                    listItem.Text = book.Title;
//
//                if (_reshowPending)
//                {
//                	_reshowPending = false;
//                	RecreateOneThumbnail(book);
//                }
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
//    			//_listView.Refresh();
//				var listItem = (from ListViewItem i in _listView.Items where i.Tag == book select i).FirstOrDefault();
//				if(listItem!=null)
//				{
//					listItem.Text = book.Title;
//					_listView.Invalidate(listItem.Bounds);
//				}
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
			_model.UpdateThumbnailAsync(RefreshOneThumbnail);
		}

		private void deleteMenuItem_Click(object sender, EventArgs e)
		{
//TODO
//			BookCollection collection = _listView.SelectedItems[0].Group.Tag as BookCollection;
//            if (collection != null)
//            {
//                _model.DeleteBook((Book.Book) _listView.SelectedItems[0].Tag, collection);
//                //_listView.SelectedItems.Clear();
//            }
		}

		private void _listView_MouseEnter(object sender, EventArgs e)
		{

		}

		private void _listView_DoubleClick(object sender, EventArgs e)
		{
			_model.DoubleClickedBook();
		}

		private void _updateThumbnailMenu_Click(object sender, EventArgs e)
		{
			//TODO
//			RecreateOneThumbnail((Book.Book)_listView.SelectedItems[0].Tag);
		}

		private void _updateFrontMatterToolStripMenu_Click(object sender, EventArgs e)
		{
			_model.UpdateFrontMatter();
		}

		private void _openFolderOnDisk_Click(object sender, EventArgs e)
		{
			_model.OpenFolderOnDisk();
		}

		private void _listView_MouseDown(object sender, MouseEventArgs e)
		{
			//TODO
//			contextMenuStrip1.Enabled = _listView.SelectedItems.Count > 0 && _listView.SelectedItems[0].Tag != null /*dummy item when collection is empty */;
		}
	}
}