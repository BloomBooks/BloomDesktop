using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Publish;
using Palaso.IO;

namespace Bloom.Project
{
	public partial class ProjectView : UserControl
	{
		private readonly ProjectModel _model;
		private LibraryView _libraryView;
		private EditingView _editingView;
		private PublishView _publishView;
		private InfoView _infoView;

		public event EventHandler CloseCurrentProject;

		public delegate ProjectView Factory();//autofac uses this

		public ProjectView(ProjectModel model,
			LibraryView.Factory libraryViewFactory,
			EditingView.Factory editingViewFactory,
			PublishView.Factory pdfViewFactory,
			InfoView.Factory infoViewFactory)
		{
			_model = model;
			_model.UpdateDisplay += new System.EventHandler(OnUpdateDisplay);
			InitializeComponent();

			//Cursor = Cursors.AppStarting;
			Application.Idle += new EventHandler(Application_Idle);
			Text = _model.ProjectName;

			SetupTabIcons();

			//
			// _libraryView
			//
			this._libraryView = libraryViewFactory();
			this._libraryView.Dock = System.Windows.Forms.DockStyle.Fill;
			tabPage1.Controls.Add(_libraryView);

			//
			// _editingView
			//
			this._editingView = editingViewFactory();
			this._editingView.Dock = System.Windows.Forms.DockStyle.Fill;
			tabPage2.Controls.Add(_editingView);
			//
			// _pdfView
			//
			this._publishView = pdfViewFactory();
			this._publishView.Dock = System.Windows.Forms.DockStyle.Fill;
			tabPage3.Controls.Add(_publishView);

			//
			// info view
			//
			this._infoView = infoViewFactory();
			this._infoView.Dock = System.Windows.Forms.DockStyle.Fill;
			_infoTab.Controls.Add(_infoView);

			tabPage2.Tag = this._tabControl.TabPages.IndexOf(tabPage2); //remember initial location
			tabPage3.Tag = this._tabControl.TabPages.IndexOf(tabPage3); //remember initial location
			_infoTab.Tag = this._tabControl.TabPages.IndexOf(_infoTab); //remember initial location

		   //NB: don't optimize this without testing... it's something of a hack to get it to display correctly
			if(!Program.StartUpWithFirstOrNewVersionBehavior)
			   SetTabVisibility(_infoTab, false);
			SetTabVisibility(tabPage3, false);
			SetTabVisibility(tabPage2, false);

			this.tabPage1.Controls.Add(_libraryView);
			this.tabPage2.Controls.Add(this._editingView);
			this.tabPage3.Controls.Add(this._publishView);
			this._infoTab.Controls.Add(this._infoView);

			if (Program.StartUpWithFirstOrNewVersionBehavior)
			{
				_tabControl.SelectedTab = _infoTab;
			}
		}

		void Application_Idle(object sender, EventArgs e)
		{
			//this didn't work... we got to idle when the lists were still populating
		   Application.Idle -= new EventHandler(Application_Idle);
//            Cursor = Cursors.Default;
		}

		private void SetupTabIcons()
		{
			_tabControl.ImageList = new ImageList();
			_tabControl.ImageList.ColorDepth = ColorDepth.Depth24Bit;
			_tabControl.ImageList.ImageSize = new Size(32,32);
			_tabControl.ImageList.Images.Add(
				Image.FromFile(FileLocator.GetFileDistributedWithApplication("Images", "library.png")));
			_tabControl.ImageList.Images.Add(
				Image.FromFile(FileLocator.GetFileDistributedWithApplication( "Images", "edit.png")));
			_tabControl.ImageList.Images.Add(
				Image.FromFile(FileLocator.GetFileDistributedWithApplication("Images", "publish.png")));
			_tabControl.ImageList.Images.Add(
				Image.FromFile(FileLocator.GetFileDistributedWithApplication("Images", "info.png")));
			tabPage1.ImageIndex = 0;
			tabPage2.ImageIndex = 1;
			tabPage3.ImageIndex = 2;
			_infoTab.ImageIndex = 3;
		}

		void OnUpdateDisplay(object sender, System.EventArgs e)
		{
			SetTabVisibility(tabPage2, _model.ShowEditPage);
			SetTabVisibility(tabPage3, _model.ShowPublishPage);
		}

		private void SetTabVisibility(TabPage page, bool visible)
		{
			if (!visible)
			{
				if(_tabControl.TabPages.Contains(page))
				{
					_tabControl.TabPages.Remove(page);
				}
			}
			else
			{
				if (!_tabControl.TabPages.Contains(page))
				{
					var index = _tabControl.TabCount;//(int)page.Tag;
					_tabControl.TabPages.Insert(index,page);
				}
			}
		}

		private void _openButton1_Click(object sender, EventArgs e)
		{
			if(_model.CloseRequested())
			{
				Invoke(CloseCurrentProject);
			}
		}

		private void _infoButton_Click(object sender, EventArgs e)
		{
			SetTabVisibility(_infoTab, true);
			_tabControl.SelectedTab = _infoTab;
		}

		private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(_tabControl.SelectedTab !=_infoTab)
				SetTabVisibility(_infoTab, false);//we always hide this after it is used

		}

	}
}