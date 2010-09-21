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
				ListViewItem item = new ListViewItem(book.Title, 0);
				item.Tag=book;
				item.Group = group;

				var thumbnail = book.GetThumbNail();
				if(thumbnail !=null)
				{
					_pageThumbnails.Images.Add(thumbnail);
					item.ImageIndex = _pageThumbnails.Images.Count - 1;
				}
				_listView.Items.Add(item);
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
			_model.SelectBook((Book) _listView.SelectedItems[0].Tag);
		}

		private void LibraryListView_BackColorChanged(object sender, EventArgs e)
		{
			_listView.BackColor = BackColor;
		}
	}
}