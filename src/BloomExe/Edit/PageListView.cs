using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Edit;

namespace Bloom
{
	public partial class PageListView : UserControl
	{
		private readonly PageSelection _pageSelection;

		public PageListView(PageSelection pageSelection)
		{
			_pageSelection = pageSelection;
			this.Font= SystemFonts.MessageBoxFont;
			InitializeComponent();
			thumbNailList1.PageSelectedChanged+=new EventHandler(thumbNailList1_PageSelectedChanged);
		}

		private void thumbNailList1_PageSelectedChanged(object page, EventArgs e)
		{
			if (page == null)
				return;
			_pageSelection.SelectPage(page as Page);
		}

		private void PageListView_BackColorChanged(object sender, EventArgs e)
		{
			thumbNailList1.BackColor = BackColor;
		}

		public void SetBook(Book book)//review: could do this instead by giving this class the bookselection object
		{
			if (book == null)
			{
				thumbNailList1.SetItems(new Page[] { });
			}
			else
			{
				thumbNailList1.SetItems(book.GetPages());
			}
		}
	}


}
