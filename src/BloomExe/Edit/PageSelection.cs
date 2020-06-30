using System;
using Bloom.Book;

namespace Bloom.Edit
{
	public class PageSelection
	{
//#if __MonoCS__	// See https://issues.bloomlibrary.org/youtrack/issue/BL-8619 for why we need this for Version4.8 on Windows.
		private bool _stillChanging = false;	// whether in the process of changing the displayed page
//#endif
		private IPage _currentSelection;
		public event EventHandler SelectionChanging; // before it changes
		public event EventHandler SelectionChanged; // after it changed

		/// <summary>
		/// Should pass prepareAlreadyDone true iff you previously called PrepareToSelectPage
		/// </summary>
		/// <param name="page"></param>
		/// <param name="prepareAlreadyDone"></param>
		/// <returns></returns>
		public bool SelectPage(IPage page, bool prepareAlreadyDone = false)
		{
//#if __MonoCS__	// See https://issues.bloomlibrary.org/youtrack/issue/BL-8619 for why we need this for Version4.8 on Windows.
			// If we haven't finished displaying the previously selected page, we can't select another page yet.
			// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3586.
			if (_stillChanging)
				return false;
//#endif
			//enhance... make pre-change event cancellable
			if (!prepareAlreadyDone)
				PrepareToSelectPage();
			_currentSelection = page;

			InvokeSelectionChanged();
			return true;
		}

		public IPage CurrentSelection
		{
			get { return _currentSelection; }
		}

		/// <summary>
		/// If you call this, you should later call SelectPage(..., true).
		/// </summary>
		public void PrepareToSelectPage()
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

//#if __MonoCS__	// See https://issues.bloomlibrary.org/youtrack/issue/BL-8619 for why we need these for Version4.8 on Windows.
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
//#endif
	}
}
