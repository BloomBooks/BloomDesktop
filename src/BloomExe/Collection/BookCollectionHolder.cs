using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Collection
{
	/// <summary>
	/// This is a class which can be cheaply and without circularity requested from Autofac by
	/// things that need to know the current editable collection. (We might eventually want to make
	/// other collections accessible through it, too.)
	/// </summary>
	public class BookCollectionHolder
	{
		public BookCollection TheOneEditableCollection { get; set; }
	}
}
