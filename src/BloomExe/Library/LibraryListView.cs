using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Library
{
	public partial class LibraryListView : UserControl
	{
		public delegate LibraryListView Factory();//autofac uses this

		private readonly LibraryModel _model;

		public LibraryListView(LibraryModel model)
		{
			_model = model;
			InitializeComponent();
		}

		public int PreferredWidth
		{
			get { return 200; }
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			listView1.Items.Clear();
			listView1.Groups.Clear();
			foreach (BookCollection collection in _model.GetBookCollections())
			{
				ListViewGroup group = new ListViewGroup(collection.Name);
				listView1.Groups.Add(group);

				foreach (Book book in collection.GetBooks())
				{
					ListViewItem item = new ListViewItem(book.Title, 0);
					item.Tag=book;
					item.Group = group;
					var thumbnail = book.GetThumbNail();
					if(thumbnail !=null)
					{
						_pageThumbnails.Images.Add(thumbnail);
						item.ImageIndex = _pageThumbnails.Images.Count - 1;
					}
					listView1.Items.Add(item);
				}
			}
		}

		private void listView1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (listView1.SelectedItems.Count == 0)
				return;
			_model.SelectBook((Book) listView1.SelectedItems[0].Tag);
		}
	}
}