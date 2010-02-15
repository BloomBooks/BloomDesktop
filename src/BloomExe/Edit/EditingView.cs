using System;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl
	{
		private readonly EditingModel _model;

		public delegate EditingView Factory();//autofac uses this


		public EditingView(EditingModel model)
		{
			_model = model;
			InitializeComponent();
			model.UpdateDisplay += new EventHandler(OnUpdateDisplay);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			OnUpdateDisplay(this,null);
		}


		void OnUpdateDisplay(object sender, EventArgs e)
		{
		   if (_model.HaveCurrentEditableBook)
			{
				var path = _model.GetPathToHtmlFileForCurrentPage();
				_browser1.Navigate(path);
			}
		}
	}
}