using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using L10NSharp;
using Palaso.Reporting;

namespace Bloom.Edit
{
	public partial class PageListView : UserControl
	{
		private readonly PageSelection _pageSelection;
		private readonly EditingModel _model;
		private bool _dontForwardSelectionEvent;
		private IPage _pageWeThinkShouldBeSelected;

		public PageListView(PageSelection pageSelection,  RelocatePageEvent relocatePageEvent, EditingModel model,HtmlThumbNailer thumbnailProvider, NavigationIsolator isolator)
		{
			_pageSelection = pageSelection;
			_model = model;
			this.Font= SystemFonts.MessageBoxFont;
			InitializeComponent();
			_addPageButton.Visible = false;
			_thumbNailList.Thumbnailer = thumbnailProvider;
			_thumbNailList.CanSelect = true;
			_thumbNailList.PreferPageNumbers = true;
			_thumbNailList.KeepShowingSelection = true;
			_thumbNailList.RelocatePageEvent = relocatePageEvent;
			_thumbNailList.PageSelectedChanged+=new EventHandler(OnPageSelectedChanged);
			_thumbNailList.Isolator = isolator;
			_thumbNailList.ContextMenuProvider = args =>
			{
				var page = _thumbNailList.GetPageContaining(args.TargetNode);
				if (page == null)
					return; // no page-related commands if we didn't click on one.

				var dupPage = LocalizationManager.GetString("EditTab._duplicatePageButton", "Duplicate Page"); // same ID as button in toolbar
				var dupItem = new MenuItem(dupPage, (sender, eventArgs) => _model.DuplicatePage(page));
				args.ContextMenu.MenuItems.Add(dupItem);
				var removePage = LocalizationManager.GetString("EditTab._deletePageButton", "Remove Page"); // same ID as button in toolbar
				var removeItem = new MenuItem(removePage, (sender, eventArgs) =>
				{
					if (ConfirmRemovePageDialog.Confirm())
					_model.DeletePage(page);
				});
				args.ContextMenu.MenuItems.Add(removeItem);
				dupItem.Enabled = removeItem.Enabled = page != null && !page.Required && !_model.CurrentBook.LockedDown;
			};
			_pageControlsPanel.Click += _pageControlsPanel_Click; // handles disabled button click
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
				_addPageButton.Enabled = _model.EnableAddPageFunction;
				_addPageButton.Visible = true;
			}
			finally
			{
				_dontForwardSelectionEvent = false;
			}

			_thumbNailList.SetPageInsertionPoint(_model.DeterminePageWhichWouldPrecedeNextInsertion());
		}

		public void EmptyThumbnailCache()
		{
			_thumbNailList.EmptyThumbnailCache();
		}

		public new bool Enabled
		{
			set { _thumbNailList.Enabled = value; }
		}

		private void _addPageButton_Click(object sender, EventArgs e)
		{
			// Call Add Page Dialog
			_model.ShowAddPageDialog();
		}

		private void _pageControlsPanel_Click(object sender, EventArgs e)
		{
			if (_model == null || _addPageButton.Enabled)
				return;
			// TODO: localize buttons
			//if(MessageBox.Show(_model.GetMessageForDisabledAddPageButton, "",
			//	MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
			//	_addPageButton_Click(null, null);
			MessageBox.Show(_model.GetMessageForDisabledAddPageButton, "", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}
}
