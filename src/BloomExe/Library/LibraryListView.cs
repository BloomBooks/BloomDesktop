using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
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

			//enhance: move to model
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
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
			foreach (Book book in collection.GetBooks())
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
//            if (book.Type != Book.BookType.Template)
//            {
			return book.GetThumbNailOfBookCover(book.Type != Book.BookType.Publication);
//            }
//            else
//            {
//                return ComposeCoverPageWithBooklet(null, GetBookletImage(book));//for templates, let's just show a blank cover
//            }

			//thumbnail = ComposeCoverPageWithBooklet(thumbnail, GetBookletImage(book));
			//return thumbnail;
		}

		int _vernacularBookletColorIndex = 0;
		int _templateBookletColorIndex = 0;

		private Image GetBookletImage(Book book)
		{
			var imagesDirectory = FileLocator.GetDirectoryDistributedWithApplication("images");
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


				path = Path.Combine(imagesDirectory, name + color + ".png");

			} while (!File.Exists(path));
			Bitmap image= (Bitmap) Image.FromFile(path);
			return image;
#if SkipTHis

//            Bitmap coloredImage = new Bitmap(image);
//		    var newColor = Color.Pink;
//
			//svg.
//		    using(var g = Graphics.FromImage(coloredImage))
//		    {
//		       g.DrawImage(image, 0,0);
//               for (int x = 0; x < coloredImage.Width; x++)
//               {
//                   for (int y = 0; y < coloredImage.Height; y++)
//                   {
//                       Color bitColor = image.GetPixel(x, y);
					   //compare without respect to transparency
//                       if (bitColor.R == 255 && bitColor.G == 0) // && bitColor.G==Color.Red.G && bitColor.B == Color.Red.B)
//                       {
						   //Sets all the pixels to white but with the original alpha value
//                           coloredImage.SetPixel(x, y, Color.FromArgb(bitColor.A, newColor.R, newColor.G,newColor.B));
//                       }
//                   }
//               }
//
//		    }
		  //  return coloredImage;


			What this is all about: I was trying to get gecko to make the booklet bitmap for me via svg. I was
			then going to change the svg at runtime to have the color I wanted, because it's not a simple mater
			of replacing a color (as I attempted in the code above). Where the black lines come near the color,
			the svg renderer blurs them, making color replacement impossible. Anyhow, xulrunner 1.9.1 would just
			give me a broken image icon.  Could try again someday with a newer xulrunner, because firefox 3.6
			did show it, when I fed that same page in.

			var dom = new XmlDocument();
			dom.LoadXml(
				string.Format(
					@"<html xmlns='http://www.w3.org/1999/xhtml'>
  <body><img src='file://{0}' />
  </body>
</html>",
					@"C:\dev\Bloom\DistFiles\images\a5Portrait.svg"));// Path.Combine(imagesDirectory,"a5Portrait.svg")));
			return _thumbnailProvider.GetThumbnail("pink", dom, Color.Transparent);
#endif
		}

		private Image ComposeCoverPageWithBooklet(Image thumbnail, Image booklet)
		{
			Image book = new Bitmap(70, 70);
			using (var g = Graphics.FromImage(book))
			{
				g.CompositingMode = CompositingMode.SourceOver;
				g.CompositingQuality = CompositingQuality.HighQuality;

				if (booklet != null)
				{
					Rectangle destRect = new Rectangle(0, 0, booklet.Width, book.Height);
					g.DrawImage(booklet, destRect, 0, 0, booklet.Width, book.Height,
								GraphicsUnit.Pixel, MagentaToPaperColor(Color.LightGreen));
				}
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
			Book book = SelectedBook;
			if (book == null)
				return;
			Debug.WriteLine("before selecting "+book.Title);
			_model.SelectBook(book);
			Debug.WriteLine("after selecting "+book.Title);
			//didn't help: _listView.Focus();//hack we were losing clicks
			book.ContentsChanged -= new EventHandler(OnContentsOfSelectedBookChanged);//in case we're already subscribed
			book.ContentsChanged += new EventHandler(OnContentsOfSelectedBookChanged);

			deleteMenuItem.Enabled = _model.CanDeleteSelection;
		}

		private Book SelectedBook
		{
			get
			{
				if (_listView.SelectedItems.Count == 0)
					return null;
				return (Book) _listView.SelectedItems[0].Tag;
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
				Book book = SelectedBook;
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

		private void deleteMenuItem_Click(object sender, EventArgs e)
		{
			BookCollection collection = _listView.SelectedItems[0].Group.Tag as BookCollection;
			if (collection != null)
			{
				_model.DeleteBook((Book) _listView.SelectedItems[0].Tag, collection);
				//_listView.SelectedItems.Clear();
			}
		}

		private void _listView_MouseEnter(object sender, EventArgs e)
		{

		}
	}
}