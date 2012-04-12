using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Book;

namespace Bloom.Edit
{
	public partial class TemplatePagesView : UserControl
	{
		private readonly BookSelection _bookSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;

		public delegate TemplatePagesView Factory();//autofac uses this

		public TemplatePagesView(BookSelection bookSelection, TemplateInsertionCommand templateInsertionCommand, HtmlThumbNailer thumbnailProvider)
		{
			_bookSelection = bookSelection;
			_templateInsertionCommand = templateInsertionCommand;

			this.Font = SystemFonts.MessageBoxFont;
			InitializeComponent();
			_thumbNailList.PageSelectedChanged += new EventHandler(OnPageClicked);
			_thumbNailList.Thumbnailer = thumbnailProvider;
		 }

		void OnPageClicked(object sender, EventArgs e)
		{
			_templateInsertionCommand.Insert(sender as Page);
		}


		public void Update()
		{
			//we don't want to spend time setting up our thumnails and such when actually the
			//user is on another tab right now
			Debug.Assert(Visible, "Shouldn't be slowing things down by calling this when it isn't visible");

			if (_bookSelection.CurrentSelection == null)
			{
			   Clear();
				return;
			}

			var templateBook = _bookSelection.CurrentSelection.FindTemplateBook();
			if(templateBook ==null)
			{
				Clear();
			}
			else
			{
				_thumbNailList.SetItems(templateBook.GetTemplatePages());
			}
		}

		private void TemplatePagesView_BackColorChanged(object sender, EventArgs e)
		{
			_thumbNailList.BackColor = BackColor;
		}

		public void Clear()
		{
			_thumbNailList.SetItems(new IPage[] { });
		}

	}
}
