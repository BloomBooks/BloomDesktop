using System;
using System.Collections.Generic;
using System.ComponentModel;
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

		public void SetItems(IEnumerable<IPage> items)
		{
			SuspendLayout();
			_listView.Items.Clear();
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
				_listView.Items.Add(item);
			}
			ResumeLayout();
		}

		[Description("If false, acts like  list of buttons"),
			 Category("Misc"),
			 DefaultValue(0),
			 Browsable(true)]

		public bool CanSelect { get; set; }

		private bool inSelectionAlready;
		private bool _mouseDidGoDown;

		private void listView1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (inSelectionAlready)
				return;
			if (!_mouseDidGoDown)//yes, having painful phantom selections when the cursor leaves this control
			{
				_listView.SelectedIndices.Clear();
			}

			inSelectionAlready = true;
			try
			{
				if (_listView.SelectedItems.Count == 0)
				{
					InvokePageSelectedChanged(null);
				}
				else
				{
					Page page = _listView.SelectedItems[0].Tag as Page;
					if(!CanSelect)
					{
						//leads to two apparent clicks... (hence the _mouseDidGoDown thing)
						_listView.SelectedIndices.Clear();
					}
					InvokePageSelectedChanged(page);
				}
			}
			finally
			{
				inSelectionAlready = false;
			}
		}

		private void ThumbNailList_BackColorChanged(object sender, EventArgs e)
		{
			_listView.BackColor = BackColor;
		}

		private void listView1_BackColorChanged(object sender, EventArgs e)
		{

		}

		private void _clearSelectionTimer_Tick(object sender, EventArgs e)
		{
//			if(!CanSelect && _listView.SelectedItems.Count>0)
//				_listView.SelectedItems.Clear();
		}

		private void _listView_MouseDown(object sender, MouseEventArgs e)
		{
			_mouseDidGoDown = true;
		}

		private void _listView_MouseUp(object sender, MouseEventArgs e)
		{
			_mouseDidGoDown = false;
		}

	}
}
