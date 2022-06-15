using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Utils
{
	/// <summary>
	/// This class just exists to mark that a Fatal exception has occurred.
	/// </summary>
	public class FatalException: ApplicationException
	{
		public FatalException(string msg) : base(msg)
		{}
	}
}
