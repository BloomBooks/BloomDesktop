using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom
{
	public partial class TemplateBookView : UserControl
	{
		private readonly BookSelection _bookSelection;
		private readonly CreateFromTemplateCommand _createFromTemplateCommand;

		public delegate TemplateBookView Factory();//autofac uses this

		public TemplateBookView(BookSelection bookSelection, CreateFromTemplateCommand createFromTemplateCommand)
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
			if(_bookSelection.CurrentSelection==null)
				return;
			_browser.Navigate(_bookSelection.CurrentSelection.GetPreviewHtmlFileForWholeBook());
			_addToLibraryButton.Visible = _bookSelection.CurrentSelection.Type == Book.BookType.Template;
		}

		private void OnAddToLibraryClick(object sender, EventArgs e)
		{
			_createFromTemplateCommand.Raise(_bookSelection.CurrentSelection);
		}
	}
}
