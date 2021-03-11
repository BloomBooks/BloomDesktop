using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.TeamCollection
{
	// General base class for three kinds of repo changes detected in event handlers.
	// This class doesn't have any interesting properties, but it allows us to declare
	// a list of RepoChangeEventArgs which can hold one of these (currently used for
	// changes at the collection settings level), or its subclasses
	// BookRepoChangeEventArgs, or NewBookEventArgs, and tell which of the three
	// kinds of events it represents. (We could instead use some enumeration of
	// possible types of Repo changes, but two of them need a book name while the
	// third does not, so on the whole I think the three classes works best.)
	public class RepoChangeEventArgs
	{
	}
}
