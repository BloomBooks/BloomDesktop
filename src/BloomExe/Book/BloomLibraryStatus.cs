using System;

namespace Bloom.Book
{
	public enum HarvesterState
	{
		Done,
		InProgress,			// includes New or Updated in parse status
		Failed,
		FailedIndefinitely	// marked by staff as not worth trying again
	}
	public interface IBloomLibraryStatus
	{
		public bool Draft { get; }
		public bool NotInCirculation { get; }
		public HarvesterState HarvesterState { get; }
		public string BloomLibraryBookUrl { get; }
	}

	public class BloomLibraryStatus : IBloomLibraryStatus
	{
		public bool Draft { get; private set; }
		public bool NotInCirculation { get; private set; }
		public HarvesterState HarvesterState { get; private set; }
		public string BloomLibraryBookUrl { get; private set; }

		/// <summary>
		/// Record a summarized status of a book in BloomLibrary.
		/// </summary>
		public BloomLibraryStatus(bool draft, bool notInCirculation, HarvesterState harvesterState, string bloomLibraryBookUrl)
		{
			Draft = draft;
			NotInCirculation = notInCirculation;
			HarvesterState = harvesterState;
			BloomLibraryBookUrl = bloomLibraryBookUrl;
		}

		public static bool operator ==(BloomLibraryStatus a, BloomLibraryStatus b)
		{
			if (Object.ReferenceEquals(a, null))
				return Object.ReferenceEquals(b, null);
			return a.Equals(b);
		}
		public static bool operator !=(BloomLibraryStatus a, BloomLibraryStatus b)
		{
			return !(a == b);
		}

		public override bool Equals(object obj)
		{
			var that = obj as BloomLibraryStatus;
			if (that == null)
				return false;
			return this.Draft == that.Draft
				&& this.NotInCirculation == that.NotInCirculation
				&& this.HarvesterState == that.HarvesterState
				&& this.BloomLibraryBookUrl == that.BloomLibraryBookUrl;
		}
		public override int GetHashCode()
		{
			return base.GetHashCode();
		}

		public override string ToString()
		{
			return $"BlorgStatus: Draft={Draft}, NotInCirculation={NotInCirculation}, HarvesterState={HarvesterState}, BloomLibraryBookUrl={BloomLibraryBookUrl}";
		}
	}
}
