using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using BloomTemp;
using Palaso.Reporting;

namespace Bloom.Edit
{
	public partial class PageListView : UserControl
	{
		private readonly PageSelection _pageSelection;
		private readonly EditingModel _model;
		private bool _dontForwardSelectionEvent;
		private IPage _pageWeThinkShouldBeSelected;

		public PageListView(PageSelection pageSelection,  RelocatePageEvent relocatePageEvent, EditingModel model,HtmlThumbNailer thumbnailProvider)
		{
			_pageSelection = pageSelection;
			_model = model;
			this.Font= SystemFonts.MessageBoxFont;
			InitializeComponent();
			_thumbNailList.Thumbnailer = thumbnailProvider;
			_thumbNailList.CanSelect = true;
			_thumbNailList.PreferPageNumbers = true;
			_thumbNailList.KeepShowingSelection = true;
			_thumbNailList.RelocatePageEvent = relocatePageEvent;
			_thumbNailList.PageSelectedChanged+=new EventHandler(OnPageSelectedChanged);
		}


		private void OnPageSelectedChanged(object page, EventArgs e)
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

		public void SetBook(Book.Book book)//review: could do this instead by giving this class the bookselection object
		{
		  //  return;

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

		public void UpdateThumbnailAsync(IPage page)
		{
			Logger.WriteMinorEvent("Updating thumbnail for page");

			//else, it just gives us the cached copy
			_thumbNailList.Thumbnailer.PageChanged(page.Id);
			_thumbNailList.UpdateThumbnailAsync(page);
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

			_thumbNailList.SetPageInsertionPoint(_model.DeterminePageWhichWouldPrecedeNextInsertion());
		}

		protected IPage SelectedPage
		{
			get { return _pageWeThinkShouldBeSelected; }
		}

		public void EmptyThumbnailCache()
		{
			_thumbNailList.EmptyThumbnailCache();
		}
	}
}
