namespace Bloom.TeamCollection
{
	/// <summary>
	/// Arguments for the BookRepoChange event in TeamCollection.
	/// </summary>
	/// <remarks>Don't confuse with the higher-level BookStatusChangeEventArgs</remarks>
	public class BookRepoChangeEventArgs
		: RepoChangeEventArgs
	{
		public string BookFileName;
	}
}
