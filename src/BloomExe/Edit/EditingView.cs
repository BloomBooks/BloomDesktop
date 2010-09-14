using System;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl
	{
		private readonly EditingModel _model;
		private bool _updatePending;
		private PageListView _pageListView;
		private TemplatePagesView _templatePagesView;
		public delegate EditingView Factory();//autofac uses this


		public EditingView(EditingModel model, PageListView pageListView, TemplatePagesView templatePagesView)
		{
			_model = model;
			_pageListView = pageListView;
			_templatePagesView = templatePagesView;
			InitializeComponent();
			model.UpdateDisplay += new EventHandler(OnUpdateDisplay);
			model.UpdatePageList += new EventHandler((s,e)=>_pageListView.SetBook(_model.CurrentBook));
			splitContainer1.Tag = splitContainer1.SplitterDistance;//save it
			//don't let it grow automatically
			splitContainer1.SplitterMoved+= ((object sender, SplitterEventArgs e) => splitContainer1.SplitterDistance = (int)splitContainer1.Tag);

			_pageListView.Dock=DockStyle.Fill;
			_pageListView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			_pageListView.BackColor = splitContainer1.Panel1.BackColor;
			splitContainer1.Panel1.Controls.Add(_pageListView);

			_templatePagesView.Dock = DockStyle.Fill;
			_templatePagesView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			splitContainer2.Panel2.Controls.Add(_templatePagesView);
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			OnUpdateDisplay(this,null);
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);
			if(_updatePending)
			{
				OnUpdateDisplay(this,null);
			}
		}
		void OnUpdateDisplay(object sender, EventArgs e)
		{
		   if(!Visible)
		   {
			   _updatePending = true;
			   return;
		   }
		   _updatePending = false;
		   if (_model.HaveCurrentEditableBook)
		   {
			var dom = _model.GetXmlDocumentForCurrentPage();
			_browser1.Navigate(dom);
		   }
		}



		private void _browser1_Validating(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (_model.HaveCurrentEditableBook)
			{
				_model.SaveNow();
			}
		}
	}
}