using System;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// Used for reporting changes to a book's status, typically one of the options
	/// for its checkout status, but can also indicate that the book has been
	/// deleted altogether.
	/// </summary>
	public enum CheckedOutBy
	{
		None,
		Self,
		Other,
		Deleted
	}

	public class BookStatusChangeEventArgs
	{
		public string BookName { get; set; }
		public CheckedOutBy CheckedOutByWhom { get; set; }

		public BookStatusChangeEventArgs(string bookName, CheckedOutBy checkedOutByWhom)
		{
			BookName = bookName;
			CheckedOutByWhom = checkedOutByWhom;
		}
	}
}
