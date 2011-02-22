using System;
using System.Xml;
using Palaso.UI.WindowsForms.ImageToolbox;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;
		private XmlDocument _domForCurrentPage;
		private bool _visible;
		private bool _bookSelectionChangedPending;

		public event EventHandler UpdateDisplay;
		public event EventHandler UpdatePageList;

		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection,
			TemplateInsertionCommand templateInsertionCommand,
			PageListChangedEvent pageListChangedEvent,
			RelocatePageEvent relocatePageEvent)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_templateInsertionCommand = templateInsertionCommand;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);
			pageListChangedEvent.Subscribe(x=>  InvokeUpdatePageList());
			relocatePageEvent.Subscribe(OnRelocatePage);
		}

		private void OnRelocatePage(RelocatePageInfo info)
		{
			_bookSelection.CurrentSelection.RelocatePage(info.Page, info.IndexOfPageAfterMove);

		}


		private void OnInsertTemplatePage(object sender, EventArgs e)
		{
			_bookSelection.CurrentSelection.InsertPageAfter(_pageSelection.CurrentSelection, sender as Page);
			if(UpdatePageList!=null)
			{
				UpdatePageList(this, null);
			}
			//_pageSelection.SelectPage(newPage);
		}

		public string CurrentBookName
		{
			get
			{
				if (_bookSelection.CurrentSelection == null)
					return "----";
				return _bookSelection.CurrentSelection.Title;
			}
		}

		public bool HaveCurrentEditableBook
		{
			get { return _bookSelection.CurrentSelection != null; }
		}

		public Book CurrentBook
		{
			get { return _bookSelection.CurrentSelection;  }
		}

		//public PageEditingModel PageEditor{ get; set;}

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			//we don't want to spend time setting up our thumnails and such when actually the
			//user is on another tab right now
			if (!_visible)
			{
				_bookSelectionChangedPending = true;
				return;
			}

			if (_bookSelection.CurrentSelection.Type == Book.BookType.Publication)
			{
				var page = _bookSelection.CurrentSelection.FirstPage;
				if(page!=null)
					_pageSelection.SelectPage(page);
			}


			if (UpdateDisplay != null)
			{
				UpdateDisplay(this, null);
			}

			InvokeUpdatePageList();
		}

		public void StartOfLoad()
		{
			VisibilityChanged(true);//hack
		}

		public void VisibilityChanged(bool visible)
		{
			_visible = visible;
			if(_bookSelectionChangedPending)
			{
				_bookSelectionChangedPending = false;
				OnBookSelectionChanged(this, null);
			}
		}

		private void InvokeUpdatePageList()
		{
			if (UpdatePageList != null)
			{
				UpdatePageList(this, null);
			}
		}

		void OnPageSelectionChanged(object sender, EventArgs e)
		{
			EventHandler handler = UpdateDisplay;
			if (handler != null)
			{
				handler(this, null);
			}
		}

		public XmlDocument GetXmlDocumentForCurrentPage()
		{
			_domForCurrentPage = _bookSelection.CurrentSelection.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			return _domForCurrentPage;
		}

		public void SaveNow()
		{
			_bookSelection.CurrentSelection.SavePage(_domForCurrentPage);
		}

		public void ChangePicture(string id, PalasoImage imageInfo)
		{
			var editor = new PageEditingModel();

			editor.ChangePicture(_bookSelection.CurrentSelection.FolderPath, _domForCurrentPage, id, imageInfo);
			SaveNow();

			//review: this is spagetti
			_bookSelection.CurrentSelection.UpdatePagePreview(_pageSelection.CurrentSelection);

			UpdateDisplay(this, null);
			InvokeUpdatePageList();
		}
	}


	public class TemplateInsertionCommand
	{
		public event EventHandler InsertPage;

		public void Insert(Page page)
		{
			if (InsertPage != null)
			{
				InsertPage.Invoke(page, null);
			}
		}
	}
}
