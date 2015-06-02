using System;
using Bloom.Book;

namespace Bloom.Edit
{
	public class PageSelection
	{
		private IPage _currentSelection;
		public event EventHandler SelectionChanging; // before it changes
		public event EventHandler SelectionChanged; // after it changed

		public bool SelectPage(IPage page)
		{
			//enhance... make pre-change event cancellable
			InvokeSelectionChanging();
			_currentSelection = page;

			InvokeSelectionChanged();
			return true;
		}

		public IPage CurrentSelection
		{
			get { return _currentSelection; }
		}

		private void InvokeSelectionChanging()
		{
			EventHandler handler = SelectionChanging;
			if (handler != null)
			{
				handler(this, null);
			}
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