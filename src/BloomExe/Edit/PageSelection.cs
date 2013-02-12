using System;
using Bloom.Book;

namespace Bloom.Edit
{
	public class PageSelection
	{
		private IPage _currentSelection;
		public event EventHandler SelectionChanged;

		public bool SelectPage(IPage page)
		{
			//enhance... send out cancellable pre-change event

			_currentSelection = page;

			InvokeSelectionChanged();
			return true;
		}

		public IPage CurrentSelection
		{
			get { return _currentSelection; }
		}

		private void InvokeSelectionChanged()
		{
			EventHandler handler = SelectionChanged;
			if (handler != null)
			{
				handler(this, null);
			}
		}
	}
}