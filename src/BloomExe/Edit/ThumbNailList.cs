using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class ThumbNailList : UserControl
	{
		//public Action<Page> PageSelectedMethod { get; set; }
		public event EventHandler PageSelectedChanged;

		private void InvokePageSelectedChanged(Page page)
		{
			EventHandler handler = PageSelectedChanged;
			if (handler != null)
			{
				handler(page, null);
			}
		}

		public ThumbNailList()
		{
			InitializeComponent();
			this.Font = new System.Drawing.Font(SystemFonts.DialogFont.FontFamily, 9F);

		}

		public void SetItems(IEnumerable<Page> items)
		{
			SuspendLayout();
			listView1.Items.Clear();
			_thumbnailImageList.Images.Clear();

			foreach (Page page in items)
			{
				var item = new ListViewItem(page.Caption);
				item.Tag = page;
				//nb IndexOf is not supported, throws exception
				var index = 0;
				item.ImageIndex = -1;
				foreach (var image in _thumbnailImageList.Images)
				{
					if (image == page.Thumbnail)
					{
						item.ImageIndex = index;
						break;
					}
					index++;
				}
				if (item.ImageIndex < 0)
				{
					_thumbnailImageList.Images.Add(page.Thumbnail);
					item.ImageIndex = _thumbnailImageList.Images.Count - 1;
				}
				listView1.Items.Add(item);
			}
			ResumeLayout();
		}

		private void listView1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (listView1.SelectedItems.Count == 0)
			{
				InvokePageSelectedChanged(null);
			}
			else
			{
				Page page = listView1.SelectedItems[0].Tag as Page;
				InvokePageSelectedChanged(page);
			}
		}

		private void ThumbNailList_BackColorChanged(object sender, EventArgs e)
		{
			listView1.BackColor = BackColor;
		}

		private void listView1_BackColorChanged(object sender, EventArgs e)
		{

		}

	}
}
