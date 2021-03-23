namespace Bloom.TeamCollection
{
	/// <summary>
	/// Arguments for the DeleteRepoBookFile event in TeamCollection.
	/// </summary>
	/// <remarks>Don't confuse with the higher-level BookDeletedEventArgs. No new
	/// behavior in this class, but we do need to be able to distinguish them</remarks>
	public class DeleteRepoBookFileEventArgs : BookRepoChangeEventArgs
	{
	}
}
