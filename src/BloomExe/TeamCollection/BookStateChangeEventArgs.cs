using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.TeamCollection
{
	// Arguments for the BookStateChange event in ITeamRepo
	public class BookStateChangeEventArgs
		: EventArgs
	{
		public string BookName;
	}
}
