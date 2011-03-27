using System;
using System.Windows.Forms;
using Palaso.Reporting;

namespace Bloom.Library
{
	public partial class LibraryView : UserControl
	{
		public delegate LibraryView Factory();//autofac uses this


		private LibraryListView libraryListView1;
		private LibraryBookView _bookView;

		public LibraryView(LibraryModel unused, LibraryListView.Factory libraryListViewFactory, LibraryBookView.Factory templateBookViewFactory)
		{
			InitializeComponent();

			libraryListView1 = libraryListViewFactory();
			libraryListView1.Dock = DockStyle.Fill;
			splitContainer1.Panel1.Controls.Add(libraryListView1);

			_bookView = templateBookViewFactory();
			_bookView.Dock = DockStyle.Fill;
			splitContainer1.Panel2.Controls.Add(_bookView);

			splitContainer1.SplitterDistance = libraryListView1.PreferredWidth;
		}

		private void LibraryView_VisibleChanged(object sender, EventArgs e)
		{
			if(Visible)
			{
				UsageReporter.SendNavigationNotice("Library");
			}
		}
	}
}
