using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Book;

namespace Bloom.Library
{
	/// <summary>
	/// this is an un-editable preview of a book in the library; either vernacular or template
	/// </summary>
	public partial class LibraryBookView : UserControl
	{
		private readonly BookSelection _bookSelection;
		private readonly CreateFromTemplateCommand _createFromTemplateCommand;
		private readonly EditBookCommand _editBookCommand;
		private bool _reshowPending = false;

		public delegate LibraryBookView Factory();//autofac uses this

		public LibraryBookView(BookSelection bookSelection,
			CreateFromTemplateCommand createFromTemplateCommand,
			EditBookCommand editBookCommand)
		{
			InitializeComponent();
			_bookSelection = bookSelection;
			_createFromTemplateCommand = createFromTemplateCommand;
			_editBookCommand = editBookCommand;
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);

			_addToLibraryButton_MouseLeave(this, null);

			_editBookButton.Visible = false;
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
			_editBookButton.Visible = _addToLibraryButton.Visible =  _addToLibraryButton.Enabled = _bookSelection.CurrentSelection != null;
			ShowBook();
			if (_bookSelection.CurrentSelection != null)
			{
				_bookSelection.CurrentSelection.ContentsChanged += new EventHandler(CurrentSelection_ContentsChanged);
			}
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
			if (_bookSelection.CurrentSelection == null)
			{
				_browser.Navigate("about:blank", false);
				_browser.Visible = false;
				BackColor = Color.FromArgb(64,64,64);
			}
			else
			{
				_browser.Visible = true;
				_browser.Navigate(_bookSelection.CurrentSelection.GetPreviewHtmlFileForWholeBook());
				_addToLibraryButton.Visible = _bookSelection.CurrentSelection.IsShellOrTemplate && !_bookSelection.CurrentSelection.HasFatalError;
				_editBookButton.Visible = _bookSelection.CurrentSelection.IsInEditableLibrary && !_bookSelection.CurrentSelection.HasFatalError;
				_reshowPending = false;
			}
		}

		private void OnAddToLibraryClick(object sender, EventArgs e)
		{
			if (_bookSelection.CurrentSelection != null)
			{
				try
				{
					_createFromTemplateCommand.Raise(_bookSelection.CurrentSelection);
				}
				catch(Exception error)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,"Bloom could not add that book to the library.");
				}
			}
		}

		private void LibraryBookView_VisibleChanged(object sender, EventArgs e)
		{
			if(Visible && _reshowPending)
				ShowBook();// changed while we were hidden
		}

		private void _addToLibraryButton_MouseEnter(object sender, EventArgs e)
		{
			_addToLibraryButton.Text = "Add this book to my library";
			_addToLibraryButton.Width = 250;
		}

		private void _addToLibraryButton_MouseLeave(object sender, EventArgs e)
		{
			_addToLibraryButton.Text="";
			_addToLibraryButton.Width = 50;
		}

		private void _editBookButton_Click(object sender, EventArgs e)
		{
			_editBookCommand.Raise(_bookSelection.CurrentSelection);
		}

	}
}
