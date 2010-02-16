using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Bloom.Edit;

namespace Bloom
{
	public partial class PageListView : UserControl
	{
		private readonly PageSelection _pageSelection;
		private Book _currentBook;

	//    public delegate PageListView Factory();//autofac uses this

		public PageListView(PageSelection pageSelection)
		{
			_pageSelection = pageSelection;
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			this.Font= SystemFonts.MessageBoxFont;
			InitializeComponent();
		}

		void OnPageSelectionChanged(object sender, EventArgs e)
		{

		}

		private void PageListView_BackColorChanged(object sender, EventArgs e)
		{
			listView1.BackColor = BackColor;
		}

		public void SetBook(Book book)
		{
			_currentBook = book;
			listView1.Items.Clear();
			if(book==null)
				return;
			SuspendLayout();
			foreach (Page page in book.GetPages())
			{
				var item = new ListViewItem(page.Caption);
				item.Tag = page;
				//nb IndexOf is not supported, throws exception
				var index=0;
				item.ImageIndex=-1;
				foreach (var image in _pageThumbnails.Images)
				{
					if(image == page.Thumbnail)
					{
						item.ImageIndex = index;
						break;
					}
					index++;
				}
				if(item.ImageIndex < 0)
				{
					_pageThumbnails.Images.Add(page.Thumbnail);
					item.ImageIndex = _pageThumbnails.Images.Count -1;
				}
				listView1.Items.Add(item);
			}
			ResumeLayout();
		}

		private void listView1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(listView1.SelectedItems.Count ==0)
				return;
			Page page = (Page) listView1.SelectedItems[0].Tag;
			_pageSelection.SelectPage(page);
		}
	}


}
