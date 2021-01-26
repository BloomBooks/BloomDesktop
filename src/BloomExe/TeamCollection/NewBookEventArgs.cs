using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.TeamCollection
{
	// Arguments for the NewBook event in ITeamRepo
	public class NewBookEventArgs:EventArgs
	{
		public string BookName { get; set; }
	}
}
