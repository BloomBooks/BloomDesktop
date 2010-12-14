using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Properties;
using Palaso.IO;

namespace Bloom.Library
{
	public partial class LibraryListView : UserControl
	{
		public delegate LibraryListView Factory();//autofac uses this

		private readonly LibraryModel _model;
		private bool _reshowPending = false;

		public LibraryListView(LibraryModel model)
		{
			_model = model;
			InitializeComponent();
			_listView.BackColor = Color.FromArgb(0xe5, 0xee, 0xf6);
			_listView.Font = new Font(SystemFonts.DialogFont.FontFamily, (float)10.0);
		}

		public int PreferredWidth
		{
			get { return 200; }
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			_listView.Items.Clear();
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
			foreach (Book book in collection.GetBooks())
			{
				AddOneBook(group, book);
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

		private void AddOneBook(ListViewGroup group, Book book)
		{
			ListViewItem item = new ListViewItem(book.Title, 0);
			item.Tag=book;
			item.Group = group;

			Image thumbnail = GetThumbnail(book);
			_pageThumbnails.Images.Add(book.Id,thumbnail);
			item.ImageIndex = _pageThumbnails.Images.Count - 1;
			_listView.Items.Add(item);
		}

		private Image GetThumbnail(Book book)
		{
			Image thumbnail=null;
			if (book.Type != Book.BookType.Template)
			{ //for templates, let's just show a blank cover
				thumbnail = book.GetThumbNailOfBookCover();
			}

			thumbnail = ComposeCoverPageWithBooklet(thumbnail, GetBookletImage(book));
			return thumbnail;
		}

		int _vernacularBookletColorIndex = 0;
		int _templateBookletColorIndex = 0;

		private Image GetBookletImage(Book book)
		{
			var vernacularBookColors = new string[] {"green", "yellow", "pink"};
			var templateBookColors = new string[] { "blue","purple" };
			string name;
			switch (book.SizeAndShape)
			{
				case Book.SizeAndShapeChoice.A5Landscape:
					name = "A5LandscapeBooklet";
					break;
				case Book.SizeAndShapeChoice.A5Portrait:
					name = "A5PortraitBooklet";
					break;
				case Book.SizeAndShapeChoice.A4Landscape:
					name = "A5PortraitBooklet";
					break;
				case Book.SizeAndShapeChoice.A4Portrait:
					name = "A5PortraitBooklet";
					break;
				case Book.SizeAndShapeChoice.A3Landscape:
					name = "A5PortraitBooklet";
					break;
				default:
					name = "A5PortraitBooklet";
					break;
			}

			string path;
			do
			{
				string color;
				if (book.Type == Book.BookType.Template)
				{
					color = templateBookColors[_templateBookletColorIndex++];
					if (_templateBookletColorIndex == templateBookColors.Length)
						_templateBookletColorIndex = 0;
				}
				else
				{
					color = vernacularBookColors[_vernacularBookletColorIndex++];
					if (_vernacularBookletColorIndex == vernacularBookColors.Length)
						_vernacularBookletColorIndex = 0;
				}

				var images = FileLocator.GetDirectoryDistributedWithApplication("images");
				path = Path.Combine(images, name + color + ".png");

			} while (!File.Exists(path));
			return Image.FromFile(path);
		}

		private Image ComposeCoverPageWithBooklet(Image thumbnail, Image booklet)
		{
			Image book = new Bitmap(70, 70);
			using (var g = Graphics.FromImage(book))
			{
				g.CompositingMode = CompositingMode.SourceOver;
				g.CompositingQuality = CompositingQuality.HighQuality;

				Rectangle destRect = new Rectangle(0,0, booklet.Width, book.Height);
				g.DrawImage(booklet, destRect, 0,0, booklet.Width, book.Height,
					GraphicsUnit.Pixel,MagentaToPaperColor(Color.LightGreen));

				if (thumbnail != null) //no cover page thumnail was available
				{
					g.DrawImage(thumbnail, -10, -5);
				}
			}
			return book;
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
			if (_listView.SelectedItems.Count == 0)
				return;
			Book book = SelectedBook();
			if (book == null)
				return;
			_model.SelectBook(book);
			book.ContentsChanged -= new EventHandler(OnSelectedBookChanged);//in case we're already subscribed
			book.ContentsChanged += new EventHandler(OnSelectedBookChanged);
		}

		private Book SelectedBook()
		{
			if (_listView.SelectedItems.Count == 0)
				return null;
			return (Book) _listView.SelectedItems[0].Tag;
		}

		private void OnSelectedBookChanged(object sender, EventArgs e)
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
				Book book = SelectedBook();
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
					var imageIndex = _pageThumbnails.Images.IndexOfKey(book.Id);
					if (imageIndex > -1)
					{
						_pageThumbnails.Images[imageIndex] = GetThumbnail(book);
						_listView.Refresh();
					}
				}
			}
		}


	}
}