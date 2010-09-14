using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Edit;

namespace Bloom
{
	public partial class TemplatePagesView : UserControl
	{
		private readonly BookSelection _bookSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;

		public delegate TemplatePagesView Factory();//autofac uses this

		public TemplatePagesView(BookSelection bookSelection, TemplateInsertionCommand templateInsertionCommand)
		{
			_bookSelection = bookSelection;
			_templateInsertionCommand = templateInsertionCommand;

			this.Font = SystemFonts.MessageBoxFont;
			InitializeComponent();
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			_thumbNailList.PageSelectedChanged += new EventHandler(OnPageClicked);
		 }

		void OnPageClicked(object sender, EventArgs e)
		{

			_templateInsertionCommand.Insert(sender as Page);
		}

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			if (_bookSelection.CurrentSelection == null || _bookSelection.CurrentSelection.TemplateBook==null)
			{
				_thumbNailList.SetItems(new Page[] { });
			}
			else
			{
				_thumbNailList.SetItems(((Book) _bookSelection.CurrentSelection.TemplateBook).GetPages());
			}
		}

		private void TemplatePagesView_BackColorChanged(object sender, EventArgs e)
		{
			_thumbNailList.BackColor = BackColor;
		}



	}
}
