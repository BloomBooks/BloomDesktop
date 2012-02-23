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
		private readonly HtmlThumbNailer _thumbnailProvider;
		private readonly BookSelection _bookSelection;
		private bool _reshowPending = false;

		public LibraryListView(LibraryModel model, HtmlThumbNailer thumbnailProvider, BookSelection bookSelection)
		{
			_model = model;
			_thumbnailProvider = thumbnailProvider;
			_bookSelection = bookSelection;
			InitializeComponent();
			_listView.BackColor = Color.FromArgb(0xe5, 0xee, 0xf6);
			_listView.Font = new Font(SystemFonts.DialogFont.FontFamily, (float)10.0);
			_listView.OwnerDraw = true;
			 _listView.DrawItem+=new DrawListViewItemEventHandler(_listView_DrawItem);
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
			foreach (ListViewItem item in _listView.Items)
			{
				if(item.Tag == _bookSelection.CurrentSelection)
				{
					item.Selected = true;
					break;
				}
			}
		}

		public int PreferredWidth
		{
			get { return 200; }
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			Application.Idle += new EventHandler(Application_Idle);

		}

		void Application_Idle(object sender, EventArgs e)
		{
			Application.Idle -= new EventHandler(Application_Idle);
			_listView.Groups.Clear();
			foreach (BookCollection collection in _model.GetBookCollections())
			{
				ListViewGroup group = new ListViewGroup(collection.Name);

				_listView.Groups.Add(group);

				LoadOneCollection(collection, group);
			}
		}

		private void LoadOneCollection(BookCollection collection, ListViewGroup group)
		{
			if (group.Tag == collection)
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
			foreach (Book.Book book in collection.GetBooks())
			{
				try
				{
					AddOneBook(group, book);
				}
				catch (Exception error)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,"Could not load the book at "+book.FolderPath);
				}

			}
			if(group.Items.Count ==0)
			{
				ListViewItem item = new ListViewItem(" ", 0);
				item.Tag=null;
				item.ImageIndex = -1;
				item.Group = group;
				_listView.Items.Add(item);
			}
		}

		private void AddOneBook(ListViewGroup group, Book.Book book)
		{
			ListViewItem item = new ListViewItem(book.Title, 0);
			item.Tag=book;
			item.Group = group;


			Image thumbnail = Resources.PagePlaceHolder;;
			_bookThumbnails.Images.Add(book.Id, thumbnail);
			item.ImageIndex = _bookThumbnails.Images.Count - 1;
			_listView.Items.Add(item);

				book.GetThumbNailOfBookCoverAsync(book.Type != Book.Book.BookType.Publication,
												  image => RefreshOneThumbnail(book, image));

		}

		private Pen _boundsPen;


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
			foreach (ListViewGroup group in _listView.Groups)
			{
				if(group.Tag == sender)
				{
					LoadOneCollection((BookCollection) sender, group);
					break;
				}
			}
		}

		private void listView1_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				if (_listView.SelectedItems.Count == 0)
					return;
				Book.Book book = SelectedBook;
				if (book == null)
					return;
				Debug.WriteLine("before selecting " + book.Title);
				_model.SelectBook(book);
				Debug.WriteLine("after selecting " + book.Title);
				//didn't help: _listView.Focus();//hack we were losing clicks
				book.ContentsChanged -= new EventHandler(OnContentsOfSelectedBookChanged); //in case we're already subscribed
				book.ContentsChanged += new EventHandler(OnContentsOfSelectedBookChanged);

				deleteMenuItem.Enabled = _model.CanDeleteSelection;
				_updateThumbnailMenu.Visible = _model.CanDeleteSelection;
				_updateFrontMatterToolStripMenu.Visible = _model.CanDeleteSelection;
			}
			catch (Exception err)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(err, "Bloom cannot display that book.");
			}
		}

		private Book.Book SelectedBook
		{
			get
			{
				if (_listView.SelectedItems.Count == 0)
					return null;
				return (Book.Book) _listView.SelectedItems[0].Tag;
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
			_listView.BackColor = BackColor;
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
				var listItem = (from ListViewItem i in _listView.Items where i.Tag == book select i).FirstOrDefault();
				Debug.Assert(listItem!=null);
				if (listItem!=null)
					listItem.Text = book.Title;

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
				//_listView.Refresh();
				var listItem = (from ListViewItem i in _listView.Items where i.Tag == book select i).FirstOrDefault();
				if(listItem!=null)
				{
					listItem.Text = book.Title;
					_listView.Invalidate(listItem.Bounds);
				}
			}
		}
		private void RecreateOneThumbnail(Book.Book book)
		{
			_model.UpdateThumbnailAsync(RefreshOneThumbnail);
		}

		private void deleteMenuItem_Click(object sender, EventArgs e)
		{
			BookCollection collection = _listView.SelectedItems[0].Group.Tag as BookCollection;
			if (collection != null)
			{
				_model.DeleteBook((Book.Book) _listView.SelectedItems[0].Tag, collection);
				//_listView.SelectedItems.Clear();
			}
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
			RecreateOneThumbnail((Book.Book)_listView.SelectedItems[0].Tag);
		}

		private void _updateFrontMatterToolStripMenu_Click(object sender, EventArgs e)
		{
			_model.UpdateFrontMatter();
		}

		private void _openFolderOnDisk_Click(object sender, EventArgs e)
		{
			_model.OpenFolderOnDisk();
		}
	}
}