using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.TeamCollection
{
	/// <summary>
	/// Arguments for the BookRepoChange event in TeamCollection.
	/// </summary>
	/// <remarks>Don't confuse with the higher-level BookStatusChangeEventArgs</remarks>
	public class BookRepoChangeEventArgs
		: EventArgs
	{
		public string BookFileName;
	}
}
