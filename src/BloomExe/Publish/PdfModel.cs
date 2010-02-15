using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom.Publish
{
	public class PdfModel
	{
		private readonly BookSelection _bookSelection;
		public event EventHandler CurrentBookChanged;
		public delegate PdfModel Factory();//autofac uses this

		public PdfModel(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
			bookSelection.SelectionChanged += new EventHandler(OnSelectionChanged);
		}

		void OnSelectionChanged(object sender, EventArgs e)
		{

		}
	}
}
