using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Publish;
using Palaso.IO;

namespace Bloom.Workspace
{
	public partial class WorkspaceView : UserControl
	{
		private readonly WorkspaceModel _model;
		private readonly SettingsDialog.Factory _settingsDialogFactory;
		private LibraryView _libraryView;
		private EditingView _editingView;
		private PublishView _publishView;
		private InfoView _infoView;

		public event EventHandler CloseCurrentProject;

		public delegate WorkspaceView Factory();//autofac uses this

		public WorkspaceView(WorkspaceModel model,
			LibraryView.Factory libraryViewFactory,
			EditingView.Factory editingViewFactory,
			PublishView.Factory pdfViewFactory,
			InfoView.Factory infoViewFactory,
			SettingsDialog.Factory settingsDialogFactory,
			EditBookCommand editBookCommand)
		{
			_model = model;
			_settingsDialogFactory = settingsDialogFactory;
			_model.UpdateDisplay += new System.EventHandler(OnUpdateDisplay);
			InitializeComponent();


			editBookCommand.Subscribe(OnEditBook);

			//Cursor = Cursors.AppStarting;
			Application.Idle += new EventHandler(Application_Idle);
			Text = _model.ProjectName;

			SetupTabIcons();

			//
			// _libraryView
			//
			this._libraryView = libraryViewFactory();
			this._libraryView.Dock = System.Windows.Forms.DockStyle.Fill;
			_libraryTabPage.Controls.Add(_libraryView);

			//
			// _editingView
			//
			this._editingView = editingViewFactory();
			this._editingView.Dock = System.Windows.Forms.DockStyle.Fill;
			_editTabPage.Controls.Add(_editingView);
			//
			// _pdfView
			//
			this._publishView = pdfViewFactory();
			this._publishView.Dock = System.Windows.Forms.DockStyle.Fill;
			_publishTabPage.Controls.Add(_publishView);

			//
			// info view
			//
			this._infoView = infoViewFactory();
			this._infoView.Dock = System.Windows.Forms.DockStyle.Fill;
			_infoTabPage.Controls.Add(_infoView);

			_editTabPage.Tag = this._tabControl.TabPages.IndexOf(_editTabPage); //remember initial location
			_publishTabPage.Tag = this._tabControl.TabPages.IndexOf(_publishTabPage); //remember initial location
			_infoTabPage.Tag = this._tabControl.TabPages.IndexOf(_infoTabPage); //remember initial location

		   //NB: don't optimize this without testing... it's something of a hack to get it to display correctly
			if(!Program.StartUpWithFirstOrNewVersionBehavior)
			   SetTabVisibility(_infoTabPage, false);
			SetTabVisibility(_publishTabPage, false);
			SetTabVisibility(_editTabPage, false);

			this._libraryTabPage.Controls.Add(_libraryView);
			this._editTabPage.Controls.Add(this._editingView);
			this._publishTabPage.Controls.Add(this._publishView);
			this._infoTabPage.Controls.Add(this._infoView);

			if (Program.StartUpWithFirstOrNewVersionBehavior)
			{
				_tabControl.SelectedTab = _infoTabPage;
			}
		}


		private void OnEditBook(Book.Book book)
		{
			_tabControl.SelectedTab = _editTabPage;
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
			_libraryTabPage.ImageIndex = 0;
			_editTabPage.ImageIndex = 1;
			_publishTabPage.ImageIndex = 2;
			_infoTabPage.ImageIndex = 3;
		}

		void OnUpdateDisplay(object sender, System.EventArgs e)
		{
			SetTabVisibility(_editTabPage, _model.ShowEditPage);
			SetTabVisibility(_publishTabPage, _model.ShowPublishPage);
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
			SetTabVisibility(_infoTabPage, true);
			_tabControl.SelectedTab = _infoTabPage;
		}

		private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(_tabControl.SelectedTab !=_infoTabPage)
				SetTabVisibility(_infoTabPage, false);//we always hide this after it is used

		}

		private void _settingsButton_Click(object sender, EventArgs e)
		{
			using(var dlg = _settingsDialogFactory())
			{
				dlg.ShowDialog();
			}
		}

	}
}