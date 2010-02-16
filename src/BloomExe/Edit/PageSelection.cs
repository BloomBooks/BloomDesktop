using System;

namespace Bloom.Edit
{
	public class PageSelection
	{
		private Page _currentSelection;
		public event EventHandler SelectionChanged;


		public bool SelectPage(Page page)
		{
			//enhance... send out cancellable pre-change event

			_currentSelection = page;

			InvokeSelectionChanged();
			return true;
		}

		public Page CurrentSelection
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