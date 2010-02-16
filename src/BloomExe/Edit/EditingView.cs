using System;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl
	{
		private readonly EditingModel _model;
		private bool _updatePending;
		private PageListView _pageListView;
		public delegate EditingView Factory();//autofac uses this


		public EditingView(EditingModel model, PageListView pageListView)
		{
			_model = model;
			_pageListView = pageListView;
			InitializeComponent();
			model.UpdateDisplay += new EventHandler(OnUpdateDisplay);
//            splitContainer1.SplitterMoving += ((object sender, SplitterCancelEventArgs e) => e.Cancel = true);
//            splitContainer2.SplitterMoving += ((object sender, SplitterCancelEventArgs e) => e.Cancel = true);
			splitContainer1.Tag = splitContainer1.SplitterDistance;//save it
			//don't let it grow automatically
			splitContainer1.SplitterMoved+= ((object sender, SplitterEventArgs e) => splitContainer1.SplitterDistance = (int)splitContainer1.Tag);

//            this._pageListView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
//                        | System.Windows.Forms.AnchorStyles.Left)
//                        | System.Windows.Forms.AnchorStyles.Right)));
			_pageListView.Dock=DockStyle.Fill;
			this._pageListView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._pageListView.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(47)))), ((int)(((byte)(55)))), ((int)(((byte)(63)))));
			this._pageListView.Font = new System.Drawing.Font("Segoe UI", 9F);

			this.splitContainer1.Panel1.Controls.Add(_pageListView);

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
				var path = _model.GetPathToHtmlFileForCurrentPage();
				_browser1.Navigate(path);
				this._pageListView.SetBook(_model.CurrentBook);
			}
		}
	}
}