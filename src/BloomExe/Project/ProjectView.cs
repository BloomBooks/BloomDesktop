using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Edit;
using Bloom.Publish;
using Palaso.IO;

namespace Bloom.Project
{
	public partial class ProjectView : UserControl
	{
		private readonly ProjectModel _model;
		private LibraryView _libraryView;
		private EditingView _editingView;
		private PdfView _pdfView;
		public event EventHandler CloseCurrentProject;

		public delegate ProjectView Factory();//autofac uses this

		public ProjectView(ProjectModel model,
			LibraryView.Factory libraryViewFactory,
			EditingView.Factory editingViewFactory,
			PdfView.Factory pdfViewFactory)
		{
			_model = model;
			_model.UpdateDisplay += new System.EventHandler(OnUpdateDisplay);
			InitializeComponent();

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
			this._pdfView = pdfViewFactory();
			this._pdfView.Dock = System.Windows.Forms.DockStyle.Fill;
			tabPage3.Controls.Add(_pdfView);

			tabPage2.Tag = this.tabControl1.TabPages.IndexOf(tabPage2); //remember initial location
			tabPage3.Tag = this.tabControl1.TabPages.IndexOf(tabPage3); //remember initial location
			SetTabVisibility(tabPage3, false);
			SetTabVisibility(tabPage2, false);

			this.tabPage1.Controls.Add(_libraryView);
			this.tabPage2.Controls.Add(this._editingView);
			this.tabPage3.Controls.Add(this._pdfView);

		}

		private void SetupTabIcons()
		{
			tabControl1.ImageList = new ImageList();
			tabControl1.ImageList.ColorDepth = ColorDepth.Depth24Bit;
			tabControl1.ImageList.ImageSize = new Size(32,32);
			tabControl1.ImageList.Images.Add(
				Image.FromFile(FileLocator.GetFileDistributedWithApplication("images", "library.png")));
			tabControl1.ImageList.Images.Add(
				Image.FromFile(FileLocator.GetFileDistributedWithApplication( "images", "edit.png")));
			tabControl1.ImageList.Images.Add(
				Image.FromFile(FileLocator.GetFileDistributedWithApplication("images", "publish.png")));

			tabPage1.ImageIndex = 0;
			tabPage2.ImageIndex = 1;
			tabPage3.ImageIndex = 2;
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
				if(tabControl1.TabPages.Contains(page))
				{
					tabControl1.TabPages.Remove(page);
				}
			}
			else
			{
				if (!tabControl1.TabPages.Contains(page))
				{
					var index = (int)page.Tag;
					tabControl1.TabPages.Insert(index,page);
				}
			}
		}

		private void _openButton_Click(object sender, EventArgs e)
		{
			if(_model.CloseRequested())
			{
				Invoke(CloseCurrentProject);
			}
		}
	}
}