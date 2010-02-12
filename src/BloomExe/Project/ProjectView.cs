using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Project
{
	public partial class ProjectView : UserControl
	{
		private readonly ProjectModel _model;
		private LibraryView libraryView1;
		private EdittingView edittingView1;
		private PdfView pdfView1;

		public delegate ProjectView Factory();//autofac uses this

		public ProjectView(ProjectModel model, LibraryModel libraryModel)
		{
			_model = model;
			InitializeComponent();

			this.libraryView1 = new Bloom.LibraryView(libraryModel);
			this.edittingView1 = new Bloom.EdittingView();
			this.pdfView1 = new Bloom.PdfView();
			//
			// libraryView1
			//
			this.libraryView1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.libraryView1.Location = new System.Drawing.Point(3, 3);
			this.libraryView1.Name = "libraryView1";
			this.libraryView1.Size = new System.Drawing.Size(871, 508);
			this.libraryView1.TabIndex = 10;
			//
			// edittingView1
			//
			this.edittingView1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.edittingView1.Location = new System.Drawing.Point(3, 3);
			this.edittingView1.Name = "edittingView1";
			this.edittingView1.Size = new System.Drawing.Size(871, 508);
			this.edittingView1.TabIndex = 0;
			//
			// pdfView1
			//
			this.pdfView1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.pdfView1.Location = new System.Drawing.Point(3, 3);
			this.pdfView1.Name = "pdfView1";
			this.pdfView1.Size = new System.Drawing.Size(871, 508);
			this.pdfView1.TabIndex = 0;
		}
	}
}