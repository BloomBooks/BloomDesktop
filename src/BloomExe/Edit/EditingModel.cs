using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Library;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;

		public event EventHandler CurrentBookChanged;


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

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			EventHandler handler = CurrentBookChanged;
			if (handler != null)
			{
				handler(this, null);
			}
		}

	}
}
