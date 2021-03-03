using System;

namespace Bloom.TeamCollection
{
	public enum CheckedOutBy
	{
		None,
		Self,
		Other
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
