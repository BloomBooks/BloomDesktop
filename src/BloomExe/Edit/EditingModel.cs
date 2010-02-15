using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Skybound.Gecko;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;

		public event EventHandler UpdateDisplay;


		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
		}

		public string CurrentBookName
		{
			get
			{
				if (_bookSelection.CurrentSelection == null)
					return "----";
				return _bookSelection.CurrentSelection.Title;
			}
		}

		public bool HaveCurrentEditableBook
		{
			get { return _bookSelection.CurrentSelection != null; }
		}

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			EventHandler handler = UpdateDisplay;
			if (handler != null)
			{
				handler(this, null);
			}
		}

		public string GetPathToHtmlFileForCurrentPage()
		{
			return _bookSelection.CurrentSelection.GetHtmlFileForCurrentPage();
		}
	}
}
