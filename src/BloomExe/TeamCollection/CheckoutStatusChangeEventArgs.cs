using System;

namespace Bloom.TeamCollection
{
	public enum CheckedOutBy
	{
		None,
		Self,
		Other
	}

	public class CheckoutStatusChangeEventArgs
	{
		public string BookName { get; set; }
		public CheckedOutBy CheckedOutByWhom { get; set; }

		public CheckoutStatusChangeEventArgs(string bookName, CheckedOutBy checkedOutByWhom)
		{
			BookName = bookName;
			CheckedOutByWhom = checkedOutByWhom;
		}
	}
}
