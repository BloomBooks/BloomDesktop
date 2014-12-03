﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Properties;
using Gecko;
using Palaso.Xml;
#if !__MonoCS__
using IWshRuntimeLibrary;
#endif
using L10NSharp;

namespace Bloom.Edit
{
	public partial class WebThumbNailList : UserControl
	{
		private bool _inSelectionAlready;
		private bool _intentionallyChangingSelection;


		private ListViewItem _currentDraggingItem;
		private ListViewItem _currentTarget;
		private Pen _boundsPen;
		private int _numberofEmptyListItemsAtStart;
		public HtmlThumbNailer Thumbnailer;
		private Image _placeHolderImage;
		public event EventHandler PageSelectedChanged;
		private Bloom.Browser _browser;
		private int _verticalScrollDistance;

		public WebThumbNailList()
		{
			InitializeComponent();
//            this.Font = new System.Drawing.Font(SystemFonts.DialogFont.FontFamily, 9F);
//        	_listView.LargeImageList = _thumbnailImageList;
//        	_listView.Sorting = SortOrder.Ascending;
//        	_listView.ListViewItemSorter = new SortListViewItemByIndex();
//              _listView.OwnerDraw = true;
//             _listView.DrawItem+=new DrawListViewItemEventHandler(_listView_DrawItem);
//             _boundsPen = new Pen(Brushes.DarkGray, 2);
//
//			_placeHolderImage = new Bitmap(32, 32);


			if (!ReallyDesignMode)
			{
				_browser = new Browser();
				this._browser.BackColor = System.Drawing.Color.DarkGray;
				this._browser.Dock = System.Windows.Forms.DockStyle.Fill;
				this._browser.Location = new System.Drawing.Point(0, 0);
				this._browser.Name = "_browser";
				this._browser.Size = new System.Drawing.Size(150, 491);
				this._browser.TabIndex = 0;
				_browser.ScaleToFullWidthOfPage = false;
				_browser.VerticalScroll.Visible = false;
				this.Controls.Add(_browser);
			}
		}

		protected bool ReallyDesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
					(LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
		}

//        void _listView_DrawItem(object sender, DrawListViewItemEventArgs e)
//        {
//            e.DrawDefault = true;
//			if (e.Item == _currentTarget && e.Item != _currentDraggingItem)
//			{
//				e.Graphics.DrawLine(Pens.Red, e.Bounds.Left, e.Bounds.Bottom, e.Bounds.Left, e.Bounds.Top);
//			}
//            //indicate selection in a more obvious way than just the grey screen we get by default
//            if(e.Item.Selected )
//            {
//                var r = e.Bounds;
//                r.Inflate(-1,-1);
//                e.Graphics.DrawRectangle(_boundsPen,r);
//            }
//
//
//            if (e.Item == ItemWhichWouldPrecedeANewPageInsertion)
//            {
//                e.Graphics.DrawLine(Pens.White, e.Bounds.Right-8, e.Bounds.Bottom-2, e.Bounds.Right-5, e.Bounds.Bottom-6);
//                e.Graphics.DrawLine(Pens.White, e.Bounds.Right - 2, e.Bounds.Bottom-2, e.Bounds.Right - 5, e.Bounds.Bottom - 6);
//            }
//        }

		public bool KeepShowingSelection
		{
			set
			{
				//_listView.HideSelection = !value;
			}
		}

	   private void InvokePageSelectedChanged(IPage page)
		{
			EventHandler handler = PageSelectedChanged;
			if (handler != null && /*REVIEW */ page!=null )
			{
				handler(page, null);
			}
		}
//        public void SetItems(IEnumerable<IPage> items)
//        {
//            _listView.ListViewItemSorter = null;
//            SuspendLayout();
//			_listView.BeginUpdate();
//            _listView.Items.Clear();
//            _thumbnailImageList.Images.Clear();
//
//            _numberofEmptyListItemsAtStart = 0;
//        	int pageNumber = 0;
//            foreach (IPage page in items)
//            {
//                if (_listView == null)//hack... once I saw this go null in the middle of working, when I tabbed away from the control
//                    return;
//
//                if (page is PlaceHolderPage)
//                    ++_numberofEmptyListItemsAtStart;
//
//				AddOnePage(page, ref pageNumber);
//            }
//            _listView.ListViewItemSorter = new SortListViewItemByIndex();
//        	_listView.EndUpdate();
//            ResumeLayout();
//        }
//
//		public void UpdateThumbnailCaptions()
//		{
//			_listView.BeginUpdate();
//			int pageNumber = 0;
//			foreach (ListViewItem item in _listView.Items)
//			{
//				IPage page = (IPage) item.Tag;
//			    var captionOrPageNumber = page.GetCaptionOrPageNumber(ref pageNumber);
//                item.Text = LocalizationManager.GetDynamicString("Bloom", "EditTab.ThumbnailCaptions."+captionOrPageNumber, captionOrPageNumber);
//			}
//			_listView.EndUpdate();
//		}

//		private void AddOnePage(IPage page, ref int pageNumber)
//		{
//			var label = PreferPageNumbers ? page.GetCaptionOrPageNumber(ref pageNumber) : page.Caption;
//            label = LocalizationManager.GetDynamicString("Bloom", "EditTab.ThumbnailCaptions." + label, label);
//
//            ListViewItem item = new ListViewItem(label, 0);
//			item.Tag = page;
//
//			Image thumbnail = Resources.PagePlaceHolder; ;
//			if (page is PlaceHolderPage)
//				thumbnail = _placeHolderImage;
//			_thumbnailImageList.Images.Add(page.Id, thumbnail);
//			item.ImageIndex = _thumbnailImageList.Images.Count - 1;
//			_listView.Items.Add(item);
//			if (!(page is PlaceHolderPage))
//			{
//				UpdateThumbnailAsync(page);
//			}
//		}

//		public void UpdateThumbnailAsync(IPage page)
//		{
//			XmlDocument pageDom = page.Book.GetPreviewXmlDocumentForPage(page).RawDom;
//
//			Thumbnailer.GetThumbnailAsync(String.Empty, page.Id, pageDom,
//													  Palette.TextAgainstDarkBackground,
//													  false, image => RefreshOneThumbnailCallback(page, image),
//													  error=> HandleThumbnailerError(page, error));
//		}
//
//	    private void HandleThumbnailerError(IPage page, Exception error)
//    	{
//#if DEBUG
//
//			//NOTE!!!!!!!!!!!!!!!!!!!!!!!!!!!!
//			//Javascript errors in the *editable* page (the one on screen) can show up here. Bizarre... haven't figured out how/why. Maybe due to calling application.doevents()
//			//in the thumbnail generator. Symptom is that even passing a blank html file to the thumbnailer still gives javascript errors here, but taking the javascript out of
//			//the editable page (in Book.GetEditableHtmlDomForPage() ) fixes it.
//			//Note, even though you'll get the error once for everythumbnail, don't let that fool you.
//
//			Debug.Fail("Debug only" + error.Message);
//#endif
//    		RefreshOneThumbnailCallback(page, Resources.Error70x70);
//    	}
//
//    	private void RefreshOneThumbnailCallback(IPage page, Image image)
//		{
//			if (IsDisposed)
//				return;
//			var imageIndex = _thumbnailImageList.Images.IndexOfKey(page.Id);
//			if (imageIndex > -1)
//			{
//				_thumbnailImageList.Images[imageIndex] = image;
//
//				//at one time the page we just inserted would have the same id, but be a different IPage object.
//				//Now, the above checks for id equality too (never did track down why the objects change, but this is robust, so I'm not worried about it)
//
//				var listItem = (from ListViewItem i in _listView.Items where ((i.Tag == page) || ((IPage)i.Tag).Id == page.Id) select i).FirstOrDefault();
//				if(listItem!=null)
//				{
//					_listView.Invalidate(listItem.Bounds);
//				}
//				else
//				{
//					Debug.Fail("Did not find a matching page."); //theoretically, this could happen if you managed to delete the page before its thumnbail could be built
//					var lastPage = _listView.Items[_listView.Items.Count - 1];
//				}
//
//			}
//		}


		public bool CanSelect { get; set; }
		public bool PreferPageNumbers { get; set; }

		public RelocatePageEvent RelocatePageEvent { get; set; }
//
//    	private void listView1_SelectedIndexChanged(object sender, EventArgs e)
//        {
//			if (_inSelectionAlready)
//				return;
//			if (!_intentionallyChangingSelection)//yes, having painful phantom selections when the cursor leaves this control
//			{
//				_listView.SelectedIndices.Clear();
//			}
//
//        	_inSelectionAlready = true;
//			try
//			{
//				if (_listView.SelectedItems.Count == 0)
//				{
//					InvokePageSelectedChanged(null);
//				}
//				else
//				{
//					Page page = _listView.SelectedItems[0].Tag as Page;
//					if(!CanSelect)
//					{
//						//leads to two apparent clicks... (hence the _mouseDidGoDown thing)
//						_listView.SelectedIndices.Clear();
//					}
//					InvokePageSelectedChanged(page);
//				}
//			}
//			finally
//			{
//				_inSelectionAlready = false;
//			}
//        }
//
//        private void ThumbNailList_BackColorChanged(object sender, EventArgs e)
//        {
//            _listView.BackColor = BackColor;
//        }
//
//		private void _listView_MouseDown(object sender, MouseEventArgs e)
//		{
//			_intentionallyChangingSelection = true;
//		    //_mouseDownLocation = e.Location;
//			if (this.RelocatePageEvent !=null)
//			{
//                var listItem = _listView.GetItemAt(e.X, e.Y);
//                if (listItem == null)
//                    return;
//                if (!((IPage)listItem.Tag).CanRelocate)
//                {
//                    return;
//                }
//                Capture = true;
//
//			    _currentDraggingItem = listItem;
//				Cursor = Cursors.Hand;
//			}
//		}
//
//        /// <summary>
//        /// used to visually indicate where the page would show up, if we add a new one
//        /// </summary>
//        public ListViewItem ItemWhichWouldPrecedeANewPageInsertion
//        {
//            get; set;
//        }
//
//		private void _listView_MouseUp(object sender, MouseEventArgs e)
//		{
////            if (_mouseDownLocation == default(Point))
////            {
////                _currentTarget = null;
////                _currentDraggingItem = null;
////                return;
////            }
////
////		    var mouseDownLocation = _mouseDownLocation;
////		    _mouseDownLocation = default(Point);
//
//			Capture = false;
//			Debug.WriteLine("MouseUp");
//            _intentionallyChangingSelection = false;
//
//			if (Control.MouseButtons == MouseButtons.Left)
//				return;
//
//			Cursor = Cursors.Default;
//
//		    bool notPointingAtOriginalLocation= _listView.GetItemAt(e.X, e.Y) != _currentDraggingItem;
//
////		    var horizontalMovement = Math.Abs(mouseDownLocation.X - e.X);
////            var verticalMovement = Math.Abs(mouseDownLocation.Y - e.Y);
////		    bool sufficientDistance = horizontalMovement > _thumbnailImageList.ImageSize.Width
////                || verticalMovement > _thumbnailImageList.ImageSize.Height;
//
//            if (notPointingAtOriginalLocation &&  RelocatePageEvent != null && _currentDraggingItem != null)
//			{
//				Debug.WriteLine("Re-ordering");
//				if (_currentTarget == null ||
//						_currentTarget == _currentDraggingItem) //should never happen, but to be safe
//				{
//					_currentTarget = null;
//					_currentDraggingItem = null;
//					return;
//				}
//
//				var relocatePageInfo = new RelocatePageInfo((IPage) _currentDraggingItem.Tag, _currentTarget.Index - _numberofEmptyListItemsAtStart);
//				RelocatePageEvent.Raise(relocatePageInfo);
//				if (relocatePageInfo.Cancel)
//					return;
//
//				_listView.BeginUpdate();
//				_listView.Items.Remove(_currentDraggingItem);
//				_listView.Items.Insert(_currentTarget.Index, _currentDraggingItem);
//				_listView.EndUpdate();
//				_currentTarget = null;
//				_currentDraggingItem = null;
//
//				UpdateThumbnailCaptions();
//				_listView.Invalidate();
//			}
//			else
//			{
//				_currentTarget = null;
//				_currentDraggingItem = null;
//			}
//		}
//
//
//        private void _listView_MouseMove(object sender, MouseEventArgs e)
//		{
//
//            if (_listView.GetItemAt(e.X, e.Y) == _currentDraggingItem)
//                return; //not really a "move" if we're still pointing at the original item
//
//			if (this.RelocatePageEvent != null && _currentDraggingItem != null)
//			{
//			    if (Control.MouseButtons != MouseButtons.Left)
//			    {
////hack trying to get a correct notion of when the mouse is up
//			        _listView_MouseUp(null, e);
//			        return;
//			    }
//
//			    Debug.WriteLine("Dragging");
//			    Cursor = Cursors.Hand;
//			    ListViewItem  target=null;
//                if (null == _listView.GetItemAt(e.X, e.Y))
//                {
//                    target = _listView.GetItemAt(e.X+20, e.Y);
//                }
//                else
//                {
//                    target = _listView.GetItemAt(e.X, e.Y);
//                }
//
//                if (target == null)
//                {
//                    //when we point right in the middle, we'll get a null target, but we sure want one,
//                    //so try looking to one side
//
//                    Debug.WriteLine("null target");
//                }
//                else
//                {
//                    Debug.WriteLine("target: " + target.Text);
//
//                    //if we're pointing to the right of some item, we want to insert *after* it.
//                    var middle = target.Position.X + (_thumbnailImageList.ImageSize.Width/2);
//                    if (e.X > middle && _listView.Items.Count - 1 > target.Index)
//                    {
//                        target = _listView.Items[target.Index + 1]; //choose the next item
//                    }
//                }
//			    if (_currentDraggingItem == target)//doesn't count to drag on yourself
//				{
//					return;
//				}
//
//				if (target != _currentTarget)
//				{
//					_listView.Invalidate(); //repaint
//				}
//				_currentTarget = target;
//			}
//		}
//
//        public void SelectPage(IPage page)
//        {
//            if (_listView == null)
//                return;
//
//            foreach (ListViewItem listViewItem in _listView.Items)
//            {
//                var itemPage = listViewItem.Tag as IPage;
//                if (itemPage == null)
//                    continue;
//
//                if(itemPage.Id == page.Id) //actual page object may change between book loads, but the id is consistent
//                {
//                    try
//                    {
//                        _intentionallyChangingSelection = true;
//                        listViewItem.Selected = true;
//                        ItemWhichWouldPrecedeANewPageInsertion = listViewItem;
//                    	listViewItem.EnsureVisible();
//                    }
//                    finally
//                    {
//                        _intentionallyChangingSelection = false;
//                    }
//                    return;
//                }
//            }
//// actually, this is common because we might not yet have been told to update our list   Debug.Fail("Did not find item to select");
//        }
//
//        public void SetPageInsertionPoint(IPage pageBeforeInsertion)
//        {
//            ItemWhichWouldPrecedeANewPageInsertion = _listView.Items.OfType<ListViewItem>().FirstOrDefault(i => i.Tag == pageBeforeInsertion);
//        }
//
//    	public void EmptyThumbnailCache()
//    	{
//    		foreach (ListViewItem item in _listView.Items)
//    		{
//
//    			var pageId = (item.Tag as IPage).Id;
//				if(!(item.Tag is PlaceHolderPage))
//					Thumbnailer.PageChanged(pageId);
//    		}
//    	}
		public void EmptyThumbnailCache()
		{

		}

		public void SetPageInsertionPoint(IPage determinePageWhichWouldPrecedeNextInsertion)
		{

		}

		private IPage _selectedPage;

		public void SelectPage(IPage page)
		{
			if (_selectedPage != null && _selectedPage != page)
			{
				var oldGridElt = GetGridElementForPage(_selectedPage);
				if (oldGridElt != null)
				{
					var oldClassContent = oldGridElt.GetAttribute("class");
					oldGridElt.SetAttribute("class", oldClassContent.Replace(" gridSelected", ""));
				}
			}
			_selectedPage = page;
			if (page == null)
				return;
			var gridElt = GetGridElementForPage(page);
			if (gridElt == null)
				return; // Can't find it yet, will try again after we next build pages.
			var classContent = gridElt.GetAttribute("class");
			if (classContent.Contains("gridSelected"))
				return;
			gridElt.SetAttribute("class", classContent + " gridSelected");
		}

		string ColorToHtmlCode(Color color)
		{
			// thanks to http://stackoverflow.com/questions/982028/convert-net-color-objects-to-hex-codes-and-back
			return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
		}
		Dictionary<string, IPage> _pageMap = new Dictionary<string, IPage>();
		private List<IPage> _pages;

		public void SetItems(IEnumerable<IPage> pages)
		{
			_pages = UpdateItems(pages);
		}

		bool RoomForTwoColumns
		{
			get { return Width > 199; }
		}

		protected override void OnSizeChanged(EventArgs e)
		{
			base.OnSizeChanged(e);
			if (_pages != null && _pages.Count > 0 && RoomForTwoColumns != _usingTwoColumns)
				UpdateItems(_pages);
		}

		private bool _usingTwoColumns;
		private string PageContainerClass = "pageContainer";

		private List<IPage> UpdateItems(IEnumerable<IPage> pages)
		{
			var result = new List<IPage>();
			var frame = BloomFileLocator.GetFileDistributedWithApplication("BloomBrowserUI", "bookEdit", "BookPagesThumbnailList", "BookPagesThumbnailList.htm");
			var backColor = ColorToHtmlCode(BackColor);
			var htmlText = System.IO.File.ReadAllText(frame, Encoding.UTF8).Replace("DarkGray", backColor);
			_usingTwoColumns = RoomForTwoColumns;
			if (!RoomForTwoColumns)
				htmlText = htmlText.Replace("columns: 4", "columns: 2").Replace("<div class=\"gridItem placeholder\" id=\"placeholder\"></div>", "");
			var dom = new HtmlDom(htmlText);
			var firstRealPage = pages.FirstOrDefault(p => p.Book != null);
			if (firstRealPage == null)
			{
				_browser.Navigate(@"about:blank", false); // no pages, we just want a blank screen, if anything.
				return result;
			}
			dom = firstRealPage.Book.GetHtmlDomReadyToAddPages(dom);
			var pageDoc = dom.RawDom;
			var body = pageDoc.GetElementsByTagName("body")[0];
			var gridlyParent = body.FirstChild; // too simplistic?
			int pageNumber = 0;
			_pageMap.Clear();
			foreach (var page in pages)
			{
				var node = page.GetDivNodeForThisPage();
				if (node == null)
					continue; // or crash? How can this happen?
				result.Add(page);
				var pageThumbnail = pageDoc.ImportNode(node, true);
				var cellDiv = pageDoc.CreateElement("div");
				cellDiv.SetAttribute("class", "gridItem");
				var gridId = GridId(page);
				cellDiv.SetAttribute("id", gridId);
				_pageMap[gridId] = page;
				gridlyParent.AppendChild(cellDiv);


				//we wrap our incredible-shrinking page in a plain 'ol div so that we
				//have something to give a border to when this page is selected
				var pageContainer = pageDoc.CreateElement("div");
				pageContainer.SetAttribute("class", PageContainerClass);
				pageContainer.AppendChild(pageThumbnail);

				/* And here it gets fragile (for not).
					The nature of how we're doing the thumbnails (relying on scaling) seems to mess up
					the browser's normal ability to assign a width to the parent div. So our parent
					here, .pageContainer, doesn't grow with the size of its child. Sigh. So for the
					moment, we assign appropriate sizes, by hand. We rely on c# code to add these
					classes, since we can't write a rule in css3 that peeks into a child attribute.
				*/
				var pageClasses = pageThumbnail.GetStringAttribute("class").Split(new[] {' '});
				var cssClass = pageClasses.FirstOrDefault(c => c.ToLower().EndsWith("portrait") || c.ToLower().EndsWith("landscape"));
				if (!string.IsNullOrEmpty(cssClass))
					pageContainer.SetAttribute("class", "pageContainer " + cssClass);

				cellDiv.AppendChild(pageContainer);
				var captionDiv = pageDoc.CreateElement("div");
				captionDiv.SetAttribute("class", "thumbnailCaption");
				cellDiv.AppendChild(captionDiv);
				var captionOrPageNumber = page.GetCaptionOrPageNumber(ref pageNumber);
				captionDiv.InnerText = LocalizationManager.GetDynamicString("Bloom", "EditTab.ThumbnailCaptions." + captionOrPageNumber, captionOrPageNumber);
			}
			_browser.WebBrowser.DocumentCompleted += WebBrowser_DocumentCompleted;
			_verticalScrollDistance = _browser.VerticalScrollDistance;
			_browser.Navigate(pageDoc, null);
			return result;
		}

		private static string GridId(IPage page)
		{
			return "page-" + page.Id;
		}

		void WebBrowser_DocumentCompleted(object sender, Gecko.Events.GeckoDocumentCompletedEventArgs e)
		{
			_browser.AddMessageEventListener("gridClick", ItemClick);
			_browser.AddMessageEventListener("gridReordered", GridReordered);
			SelectPage(_selectedPage);
			_browser.VerticalScrollDistance = _verticalScrollDistance;
		}

		private void ItemClick(string s)
		{
			IPage page;
			if (_pageMap.TryGetValue(s, out page))
				InvokePageSelectedChanged(page);
		}

		private void GridReordered(string s)
		{
			var newSeq = new List<IPage>();
			var keys = s.Split(new [] {','}, StringSplitOptions.RemoveEmptyEntries);
			foreach (var key in keys)
			{
				IPage page;
				if (_pageMap.TryGetValue(key, out page))
					newSeq.Add(page);
			}
			Debug.Assert(newSeq.Count == _pages.Count);
			// Now, which one moved?
			int firstDiff = 0;
			while (firstDiff < _pages.Count && _pages[firstDiff] == newSeq[firstDiff])
				firstDiff++;
			int limDiff = _pages.Count;
			while (limDiff > firstDiff && _pages[limDiff-1] == newSeq[limDiff-1])
				limDiff--;
			if (firstDiff == limDiff)
				return; // spurious notification somehow? Nothing changed.
			// We have the subsequence that altered.
			// Is the change legal?
			for (int i = firstDiff; i < limDiff; i++)
			{
				if (!_pages[i].CanRelocate)
				{
					var msg = LocalizationManager.GetString("PageList.CantMoveXMatter",
						"That change is not allowed. Front matter and back matter pages must remain where they are");
					var caption = LocalizationManager.GetString("PageList.CantMoveXMatterCaption",
						"Invalid Move");
					MessageBox.Show(msg, caption);
					UpdateItems(_pages); // reset to old state
					return;
				}
			}
			// There are two possibilities: the user dragged the item that used to be at the start to the end,
			// or the item that used to be the end to the start.
			IPage movedPage;
			int newPageIndex;
			if (_pages[firstDiff] == newSeq[limDiff - 1])
			{
				// Move forward
				movedPage = _pages[firstDiff];
				newPageIndex = limDiff - 1;
			}
			else
			{
				Debug.Assert(_pages[limDiff - 1] == newSeq[firstDiff]); // moved last page forwards
				movedPage = _pages[limDiff - 1];
				newPageIndex = firstDiff;
			}
			var relocatePageInfo = new RelocatePageInfo(movedPage, newPageIndex);
			RelocatePageEvent.Raise(relocatePageInfo);
			if (relocatePageInfo.Cancel)
				UpdateItems(_pages);
			else
			{
				_pages = newSeq;
				UpdatePageNumbers();
				// This is only needed if left and right pages are styled differently.
				// Unfortunately gecko does not re-apply the styles when things are re-ordered!
				UpdateItems(_pages);
			}
		}

		private void UpdatePageNumbers()
		{
			int pageNumber = 0;
			foreach (var page in _pages)
			{
				var node = page.GetDivNodeForThisPage();
				if (node == null)
					continue; // or crash? How can this happen?
				var gridElt = _browser.WebBrowser.Document.GetElementById(GridId(page));
				var titleElt = GetFirstChildWithClass(gridElt, "gridTitle") as GeckoElement;
				var captionOrPageNumber = page.GetCaptionOrPageNumber(ref pageNumber);
				var desiredText = LocalizationManager.GetDynamicString("Bloom", "EditTab.ThumbnailCaptions." + captionOrPageNumber, captionOrPageNumber);
				if (titleElt == null || titleElt.TextContent == desiredText)
					continue;
				titleElt.TextContent = desiredText;
			}
		}

		public void UpdateThumbnailAsync(IPage page)
		{
			var targetClass = "bloom-page";
			var gridElt = GetGridElementForPage(page);
			var pageContainerElt = GetFirstChildWithClass(gridElt, PageContainerClass) as GeckoElement;
			if (pageContainerElt == null)
			{
				Debug.Fail("Can't update page...missing pageContainer element");
				return; // for end user we just won't update the thumbnail.
			}
			var pageElt = GetFirstChildWithClass(pageContainerElt, targetClass);
			if (pageElt == null)
			{
				Debug.Fail("Can't update page...missing page element");
				return; // for end user we just won't update the thumbnail.
			}
			pageContainerElt.ReplaceChild(MakeGeckoNodeFromXmlNode(_browser.WebBrowser.Document, page.GetDivNodeForThisPage()), pageElt);
		}

		private GeckoElement GetGridElementForPage(IPage page)
		{
			return _browser.WebBrowser.Document.GetElementById(GridId(page));
		}

		private GeckoNode GetFirstChildWithClass(GeckoElement parentElement, string targetClass)
		{
			// Something here can be null when adding pages very quickly, possibly because something
			// is incompletely constructed or in the course of being disposed? So be very careful.
			if (parentElement == null || parentElement.ChildNodes == null)
				return null;
			var targetWithSpaces = " " + targetClass + " "; // search for this to avoid partial word matches
			return parentElement.ChildNodes.FirstOrDefault(e =>
			{
				var ge = e as GeckoElement;
				if (ge == null)
					return false;
				var attr = ge.Attributes["class"];
				if (attr == null)
					return false;
				var content = " " + attr.TextContent + " "; // wrapping spaces allow us to find targetWithSpaces at start or end
				if (content == null)
					return false;
				return content.Contains(targetWithSpaces);
			});
		}

		private Gecko.GeckoNode MakeGeckoNodeFromXmlNode(Gecko.GeckoDocument doc, XmlNode xmlElement)
		{
			var result = doc.CreateElement(xmlElement.LocalName);
			foreach (XmlAttribute attr in xmlElement.Attributes)
				result.SetAttribute(attr.LocalName, attr.Value);
			foreach (var child in xmlElement.ChildNodes)
			{
				if (child is XmlElement)
					result.AppendChild(MakeGeckoNodeFromXmlNode(doc, (XmlElement)child));
				else if (child is XmlText)
					result.AppendChild(doc.CreateTextNode(((XmlText) child).InnerText));
				else
				{
					result = result;
				}
			}
			return result;
		}

		public NavigationIsolator Isolator
		{
			get { return _browser == null ? null : _browser.Isolator; }
			set
			{
				if (_browser != null)
					_browser.Isolator = value;
			}
		}
	}
}
