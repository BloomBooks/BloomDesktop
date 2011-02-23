using System;
using System.Windows.Forms;

namespace Bloom.Library
{
	/// <summary>
	/// this is an un-editable preview of a book in the library; either vernacular or template
	/// </summary>
	public partial class LibraryBookView : UserControl
	{
		private readonly BookSelection _bookSelection;
		private readonly CreateFromTemplateCommand _createFromTemplateCommand;
		private bool _reshowPending = false;

		public delegate LibraryBookView Factory();//autofac uses this

		public LibraryBookView(BookSelection bookSelection, CreateFromTemplateCommand createFromTemplateCommand)
		{
			InitializeComponent();
			_bookSelection = bookSelection;
			_createFromTemplateCommand = createFromTemplateCommand;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			LoadBook();
		}

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			LoadBook();
		}

		private void LoadBook()
		{
			_addToLibraryButton.Enabled = _bookSelection.CurrentSelection != null;
			if(_bookSelection.CurrentSelection==null)
				return;
			_addToLibraryButton.Image = _bookSelection.CurrentSelection.GetThumbNailOfBookCover(true);
			ShowBook();
			_bookSelection.CurrentSelection.ContentsChanged += new EventHandler(CurrentSelection_ContentsChanged);
		}

		void CurrentSelection_ContentsChanged(object sender, EventArgs e)
		{
			if( Visible)
				ShowBook();
			else
			{
				_reshowPending = true;
			}
		}

		private void ShowBook()
		{
			_browser.Navigate(_bookSelection.CurrentSelection.GetPreviewHtmlFileForWholeBook());
			_addToLibraryButton.Visible = _bookSelection.CurrentSelection.IsShellOrTemplate;
			_reshowPending = false;
		}

		private void OnAddToLibraryClick(object sender, EventArgs e)
		{
			if (_bookSelection.CurrentSelection != null)
			{
				_createFromTemplateCommand.Raise(_bookSelection.CurrentSelection);
			}
		}

		private void LibraryBookView_VisibleChanged(object sender, EventArgs e)
		{
			if(Visible && _reshowPending)
				ShowBook();// changed while we were hidden
		}

	}
}
