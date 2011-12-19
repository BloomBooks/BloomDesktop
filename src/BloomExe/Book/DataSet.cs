using System.Collections.Generic;
using Palaso.Text;

namespace Bloom.Book
{
	/// <summary>
	/// Holds the values we inject and gather from the document.
	/// </summary>
	public class DataSet
	{
		public DataSet()
		{
			WritingSystemCodes = new Dictionary<string, string>();
			TextVariables = new Dictionary<string, MultiTextBase>();
		}
		/// <summary>
		/// Depending on the context, the correct values for these change. E.g., "V" is the *actual* vernacular when looking at a book in the Vernacular library,
		/// but it should be the national language or UI language when looking a shell in a collection (where we'd want to see, for example, the French title)
		///
		/// Values in use currently are: "V", "N1", "N2"
		/// </summary>
		public Dictionary<string, string> WritingSystemCodes { get; private set; }

		public Dictionary<string, MultiTextBase> TextVariables { get; private set; }
	}
}