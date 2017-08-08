using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Book
{
	/// <summary>
	/// EventArgs for BookSelection SelectionChanged event.
	/// </summary>
	public class BookSelectionChangedEventArgs
	{
		/// <summary>
		/// True if we are about to edit (a newly created) book; used to suppress creating a preview of it
		/// </summary>
		public bool AboutToEdit { get; set; }
	}
}
