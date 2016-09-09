using System;
using Bloom.Book;

namespace Bloom.Edit
{
	public class PageSelection
	{
#if __MonoCS__
		private bool _stillChanging = false;	// whether in the process of changing the displayed page
#endif
		private IPage _currentSelection;
		public event EventHandler SelectionChanging; // before it changes
		public event EventHandler SelectionChanged; // after it changed

		public bool SelectPage(IPage page)
		{
#if __MonoCS__
			// If we haven't finished displaying the previously selected page, we can't select another page yet.
			// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3586.
			if (_stillChanging)
				return false;
#endif
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

#if __MonoCS__
		/// <summary>
		/// Flag that a page selection is currently under way.
		/// </summary>
		internal void StartChangingPage()
		{
			_stillChanging = true;
		}

		/// <summary>
		/// Flag that the current (former) page selection has finished, so it's safe to select another page.
		/// </summary>
		internal void ChangingPageFinished()
		{
			_stillChanging = false;
		}
#endif
	}
}