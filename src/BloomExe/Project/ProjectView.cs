using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Edit;
using Bloom.Library;
using Bloom.Publish;

namespace Bloom.Project
{
	public partial class ProjectView : UserControl
	{
		private readonly ProjectModel _model;
		private LibraryView libraryView1;
		private EditingView _editingView;
		private PdfView pdfView1;

		public delegate ProjectView Factory();//autofac uses this

		public ProjectView(ProjectModel model,
			LibraryView.Factory libraryViewFactory,
			EditingView.Factory editingViewFactory,
			PdfView.Factory pdfViewFactory)
		{
			_model = model;
			InitializeComponent();

			//
			// libraryView1
			//
			this.libraryView1 = libraryViewFactory();
			this.libraryView1.Dock = System.Windows.Forms.DockStyle.Fill;
			tabPage1.Controls.Add(libraryView1);

			//
			// _editingView
			//
			this._editingView = editingViewFactory();
			this._editingView.Dock = System.Windows.Forms.DockStyle.Fill;
			tabPage2.Controls.Add(_editingView);
			//
			// pdfView1
			//
			this.pdfView1 = pdfViewFactory();
			this.pdfView1.Dock = System.Windows.Forms.DockStyle.Fill;
			tabPage3.Controls.Add(pdfView1);
		}
	}
}