using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Library;

namespace Bloom
{
	public partial class LibraryView : UserControl
	{
		public delegate LibraryView Factory();//autofac uses this

		private readonly LibraryModel _model;
		private LibraryListView libraryListView1;
		private LibraryBookView _bookView;

		public LibraryView(LibraryModel model, LibraryListView.Factory libraryListViewFactory, LibraryBookView.Factory templateBookViewFactory)
		{
			_model = model;
			InitializeComponent();

			this.libraryListView1 = libraryListViewFactory();
			this.libraryListView1.Dock = System.Windows.Forms.DockStyle.Fill;
			splitContainer1.Panel1.Controls.Add(libraryListView1);

			_bookView = templateBookViewFactory();
			_bookView.Dock = System.Windows.Forms.DockStyle.Fill;
			splitContainer1.Panel2.Controls.Add(_bookView);

			splitContainer1.SplitterDistance = libraryListView1.PreferredWidth;
		}

		private void LibraryView_VisibleChanged(object sender, EventArgs e)
		{

		}
	}
}
