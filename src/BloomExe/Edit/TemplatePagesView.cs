using System;
using System.Drawing;
using System.Windows.Forms;

namespace Bloom
{
	public partial class TemplatePagesView : UserControl
	{
		private readonly BookSelection _bookSelection;

		public delegate TemplatePagesView Factory();//autofac uses this

		public TemplatePagesView(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;

			this.Font = SystemFonts.MessageBoxFont;
			InitializeComponent();
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
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
