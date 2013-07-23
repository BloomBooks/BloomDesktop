using System;
using Bloom.Collection;

namespace Bloom.Book
{
	public class CurrentEditableCollectionSelection
	{
		private BookCollection _currentSelection;
		public event EventHandler SelectionChanged;


		public void SelectCollection(BookCollection collection)
		{
			if (_currentSelection == collection)
				return;

			//enhance... send out cancellable pre-change event

			_currentSelection = collection;

			InvokeSelectionChanged();
		}



		public BookCollection CurrentSelection
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