using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Edit;

namespace Bloom
{
	public partial class PageListView : UserControl
	{
		private readonly PageSelection _pageSelection;
		private readonly DeletePageCommand _deletePageCommand;
		private Book _book;
		private bool _dontForwardSelectionEventtry;
		private bool _dontForwardSelectionEvent;
		private IPage _pageWeThinkShouldBeSelected;

		public PageListView(PageSelection pageSelection, DeletePageCommand deletePageCommand, RelocatePageEvent relocatePageEvent)
		{
			_pageSelection = pageSelection;
			_deletePageCommand = deletePageCommand;
			this.Font= SystemFonts.MessageBoxFont;
			InitializeComponent();
			_thumbNailList.CanSelect = true;
			_thumbNailList.KeepShowingSelection = true;
			_thumbNailList.RelocatePageEvent = relocatePageEvent;
			_thumbNailList.PageSelectedChanged+=new EventHandler(OnSelectedThumbnailChanged);
		}

		private void OnSelectedThumbnailChanged(object page, EventArgs e)
		{
			if (page == null)
				return;
			if (!_dontForwardSelectionEvent)
			{
				_pageSelection.SelectPage(page as Page);
			}
		}

		private void PageListView_BackColorChanged(object sender, EventArgs e)
		{
			_thumbNailList.BackColor = BackColor;
		}

		public void SetBook(Book book)//review: could do this instead by giving this class the bookselection object
		{
		  //  return;

			_book = book;
			if (book == null)
			{
				_thumbNailList.SetItems(new Page[] { });
			}
			else
			{
				_thumbNailList.SetItems(new IPage[] { new PlaceHolderPage() }.Concat(book.GetPages()));
			  //  _thumbNailList.SetItems(book.GetPages());

				if(_pageWeThinkShouldBeSelected !=null)
				{
					//this var will be set previously when someone told us the page we're to select,
					//but had not yet given us leave to do the time-consuming process of actually
					//making the thumbnails and showing them.
					SelectThumbnailWithoutSendingEvent(_pageWeThinkShouldBeSelected);
				}
			}
		}

		private void deletePageToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_deletePageCommand.Raise(_pageSelection.CurrentSelection);
		}


		public void Clear()
		{
			_thumbNailList.SetItems(new IPage[]{});
		}

		public void SelectThumbnailWithoutSendingEvent(IPage page)
		{
			_pageWeThinkShouldBeSelected = page;
			try
			{
				_dontForwardSelectionEvent = true;
				_thumbNailList.SelectPage(page);
			}
			finally
			{
				_dontForwardSelectionEvent = false;
			}
		}
	}

	/// <summary>
	/// This is just so the first (top-left) thumbnail is empty, so that the cover page appears in the second column.
	/// </summary>
	public class PlaceHolderPage     : IPage
	{
		public string Id
		{
			get { return null; }
		}

		public string Caption
		{
			get { return null; }
		}

		public Image Thumbnail
		{
			get { return new Bitmap(32,32); }
		}

		public string XPathToDiv
		{
			get { return null; }
		}

		public XmlNode GetDivNodeForThisPage()
		{
			return null;
		}
	}
}
