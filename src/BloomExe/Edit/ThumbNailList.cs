using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class ThumbNailList : UserControl
	{
		private bool _inSelectionAlready;
		private bool _mouseDidGoDown;


		//public Action<Page> PageSelectedMethod { get; set; }
		public event EventHandler PageSelectedChanged;

		public ThumbNailList()
		{
			InitializeComponent();
			this.Font = new System.Drawing.Font(SystemFonts.DialogFont.FontFamily, 9F);
			_listView.LargeImageList = _thumbnailImageList;
			_listView.Sorting = SortOrder.Ascending;
			_listView.ListViewItemSorter = new SortListViewItemByIndex();
		}

	   private void InvokePageSelectedChanged(Page page)
		{
			EventHandler handler = PageSelectedChanged;
			if (handler != null && /*REVIEW */ page!=null )
			{
				handler(page, null);
			}
		}
		public void SetItems(IEnumerable<IPage> items)
		{
			SuspendLayout();
			_listView.BeginUpdate();
			_listView.Items.Clear();
			_thumbnailImageList.Images.Clear();

			foreach (IPage page in items)
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
			_listView.EndUpdate();
			ResumeLayout();
		}


		public bool CanSelect { get; set; }

		public RelocatePageEvent RelocatePageEvent { get; set; }

		private void listView1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_inSelectionAlready)
				return;
			if (!_mouseDidGoDown)//yes, having painful phantom selections when the cursor leaves this control
			{
				_listView.SelectedIndices.Clear();
			}

			_inSelectionAlready = true;
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
				_inSelectionAlready = false;
			}
		}

		private void ThumbNailList_BackColorChanged(object sender, EventArgs e)
		{
			_listView.BackColor = BackColor;
		}

		private void _listView_MouseDown(object sender, MouseEventArgs e)
		{
			_mouseDidGoDown = true;

			if (this.RelocatePageEvent !=null)
			{
				Capture = true;
				_currentDraggingItem = _listView.GetItemAt(e.X, e.Y);
				Cursor = Cursors.Hand;
			}
		}

		private void _listView_DrawItem(object sender, DrawListViewItemEventArgs e)
		{
			e.DrawDefault = true;
			if (e.Item == _currentTarget && e.Item != _currentDraggingItem)
			{
				e.Graphics.DrawLine(Pens.Red, e.Bounds.Left, e.Bounds.Top, e.Bounds.Right, e.Bounds.Top);
			}
		}

		private void _listView_MouseUp(object sender, MouseEventArgs e)
		{
			Capture = false;
			Debug.WriteLine("MouseUp");
			_mouseDidGoDown = false;

			if (Control.MouseButtons == MouseButtons.Left)
				return;

			Cursor = Cursors.Default;

			if (RelocatePageEvent != null && _currentDraggingItem != null)
			{
				Debug.WriteLine("Re-ordering");
				if (_currentTarget == null ||
						_currentTarget == _currentDraggingItem) //should never happen, but to be safe
				{
					_currentTarget = null;
					_currentDraggingItem = null;
					return;
				}

				RelocatePageEvent.Raise(new RelocatePageInfo((IPage)_currentDraggingItem.Tag, _currentTarget.Index));

				_listView.BeginUpdate();
				_listView.Items.Remove(_currentDraggingItem);
				_listView.Items.Insert(_currentTarget.Index, _currentDraggingItem);
				_listView.EndUpdate();
				_currentTarget = null;
				_currentDraggingItem = null;
				_listView.Invalidate();
			}
			else
			{
				_currentTarget = null;
				_currentDraggingItem = null;
			}
		}

		private ListViewItem _currentDraggingItem;
		private ListViewItem _currentTarget;

		private void _listView_MouseMove(object sender, MouseEventArgs e)
		{
			if (this.RelocatePageEvent != null && _currentDraggingItem != null)
			{
				if (Control.MouseButtons != MouseButtons.Left)
				{//hack trying to get a correct notion of when the mouse is up
					_listView_MouseUp(null, e);
					return;
				}
				Debug.WriteLine("Dragging");
				Cursor = Cursors.Hand;
				var target = _listView.GetItemAt(e.X, e.Y);
				if(target == null)
				{
					Debug.WriteLine("null target");
				}
				else
					Debug.WriteLine("target: "+target.Text);

				if (_currentDraggingItem == target)//doesn't count to drag on yourself
				{
					return;
				}
				if (target != _currentTarget)
				{
					_listView.Invalidate(); //repaint
				}
				_currentTarget = target;
			}
		}
	}

	/// <summary>
	/// This makes a list view act, well, like one would expect; the items
	/// are ordered according to their index, so that inserting an item actually
	/// doesn't something other than always whowing it at the end!
	/// </summary>
	class SortListViewItemByIndex : IComparer
	{
		public int Compare(object x, object y)
		{
			ListViewItem X = x as ListViewItem;
			ListViewItem Y = y as ListViewItem;
			ListView listView = X.ListView;
			if (listView != null && X != null && Y != null)
			{
				return listView.Items.IndexOf(X) - listView.Items.IndexOf(Y);
			}
			return 0;
		}
	}
}
