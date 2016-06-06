using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using L10NSharp;
using SIL.Reporting;

namespace Bloom.Edit
{
	public partial class PageListView : UserControl
	{
		private readonly PageSelection _pageSelection;
		private readonly EditingModel _model;
		private bool _dontForwardSelectionEvent;
		private IPage _pageWeThinkShouldBeSelected;
		private DateTime _lastButtonClickedTime = DateTime.Now; // initially, instance creation time

		public PageListView(PageSelection pageSelection,  RelocatePageEvent relocatePageEvent, EditingModel model,
			HtmlThumbNailer thumbnailProvider, NavigationIsolator isolator, ControlKeyEvent controlKeyEvent)
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
			_thumbNailList.Isolator = isolator;
			_thumbNailList.ControlKeyEvent = controlKeyEvent;
			// First action determines whether the menu item is enabled, second performs it.
			var menuItems = new List<WebThumbNailList.MenuItemSpec>();
			menuItems.Add(
				new WebThumbNailList.MenuItemSpec() {
					Label = LocalizationManager.GetString("EditTab.DuplicatePageButton", "Duplicate Page"), // same ID as button in toolbar));
					EnableFunction = (page) => page != null && !page.Required && !_model.CurrentBook.LockedDown,
					ExecuteCommand = (page) => _model.DuplicatePage(page)});
			menuItems.Add(
				new WebThumbNailList.MenuItemSpec() {
					Label = LocalizationManager.GetString("EditTab.DeletePageButton", "Remove Page"),  // same ID as button in toolbar));
					EnableFunction = (page) => page != null && !page.Required && !_model.CurrentBook.LockedDown,
					ExecuteCommand = (page) =>
					{
						if (ConfirmRemovePageDialog.Confirm())
							_model.DeletePage(page);
					}});
			menuItems.Add(
				new WebThumbNailList.MenuItemSpec() {
					Label = LocalizationManager.GetString("EditTab.ChooseLayoutButton", "Choose Different Layout"),
					EnableFunction = (page) => page != null && !page.Required && !_model.CurrentBook.LockedDown,
					ExecuteCommand = (page) => _model.ChangePageLayout(page)});
			// This adds the desired menu items to the Gecko context menu that happens when we right-click
			_thumbNailList.ContextMenuProvider = args =>
			{
				var page = _thumbNailList.GetPageContaining(args.TargetNode);
				if (page == null)
					return true; // no page-related commands if we didn't click on one.
				if(page != _pageSelection.CurrentSelection)
				{
					return true; //it's too dangerous to let users do thing to a page they aren't seeing
				}
				foreach (var item in menuItems)
				{
					var menuItem = new MenuItem(item.Label, (sender, eventArgs) => item.ExecuteCommand(page));
					args.ContextMenu.MenuItems.Add(menuItem);
					menuItem.Enabled = item.EnableFunction(page);
				}
				return true;
			};
			// This sets up the context menu items that will be shown when the user clicks the
			// arrow in the thumbnail list.
			_thumbNailList.ContextMenuItems = menuItems;
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

		public void UpdateDisplay()
		{
			//Enhance: when you go to another book, currently this shows briefly before we get a 
			//chance to select how to display it. I haven't found any existing event I can use
			//to hide it first.

			//What we're doing here is unusual; we want to always get clicks, so that if the button is
			//disabled, we can at least tell the user *why* its disabled.
			//Whereas this class has an ImageNormal and ImageDisabled, in order to never be truly
			//disabled, we don't use that. The button always thinks its in the "Normal" (enabled) state.
			//But we switch its "normal" image and forecolor in order to get this "soft disabled" state
			_addPageButton.ImageNormal = _model.CanAddPages ? Resources.AddPageButton : Resources.AddPageButtonDisabled;
			_addPageButton.ForeColor = _model.CanAddPages ? Palette.BloomRed : Color.FromArgb(87,87,87);
		}

		public void SetBook(Book.Book book)//review: could do this instead by giving this class the bookselection object
		{
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
			UpdateDisplay();
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
			// Turn double-click into a single-click
			if (_lastButtonClickedTime > DateTime.Now.AddSeconds(-1))
				return;
			_lastButtonClickedTime = DateTime.Now;

			if (_model.CanAddPages)
			{
				_model.ShowAddPageDialog();
			}
			else
			{
				// TODO: localize buttons
				MessageBox.Show(EditingView.GetInstructionsForUnlockingBook()  , "Bloom", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}
	}
}
