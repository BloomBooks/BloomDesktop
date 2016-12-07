using System.Drawing.Imaging;
using SIL.Extensions;
using SIL.Windows.Forms.ClearShare;
using System;
using System.IO;

namespace Bloom.Book
{
	public class BookSelection
	{
		private Book _currentSelection;
		public event EventHandler SelectionChanged;


		public void SelectBook(Book book)
		{
			if (_currentSelection == book)
				return;

			//enhance... send out cancellable pre-change event

		   _currentSelection = book;

			InvokeSelectionChanged();
		}

		public Book CurrentSelection
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
