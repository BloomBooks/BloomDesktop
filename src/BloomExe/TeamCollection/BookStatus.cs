using System;
using Newtonsoft.Json;

namespace Bloom.TeamCollection
{
	/// Encapsulates the information we store about the status of a book
	/// (In the context of sharing that book in a Team Collection).
	/// I think of this object as readonly, though for convenience of converting to
	/// and from Json, it isn't enforced. But please don't modify them after initial
	/// creation.
	public class BookStatus
	{
		public string checksum; // a checksum that helps us detect whether the book has been modified
		public string lockedBy; // email
		public string lockedByFirstName; // registration first name
		public string lockedBySurname; // registration surname
		public string lockedWhen; // string.Format("{0:yyyy-MM-ddTHH:mm:ss.fffZ}", DateTime.UtcNow)
		public string lockedWhere; // Environment.MachineName
		public string oldName; // When a book is renamed, we store the previous name here until checkin.
		public string collectionId; // used only locally, distinguishes which collection stored the book.

		public string ToJson()
		{
			return JsonConvert.SerializeObject(this);
		}

		public static BookStatus FromJson(string input)
		{
			return JsonConvert.DeserializeObject<BookStatus>(input);
		}

		/// <summary>
		/// A status indicating a newly created local-only book, which is automatically considered
		/// checked out to the current user.
		/// </summary>
		public static BookStatus NewBookStatus
		{
			get
			{
				return new BookStatus() { lockedBy = TeamCollection.FakeUserIndicatingNewBook, lockedWhere = TeamCollectionManager.CurrentMachine };
			}
		}

		/// <summary>
		/// Return a new status which is the same as this one except for having
		/// the specified version code.
		/// </summary>
		/// <param name="versionCode"></param>
		/// <returns></returns>
		public BookStatus WithChecksum(string versionCode)
		{
			var result = (BookStatus)this.MemberwiseClone();
			result.checksum = versionCode;
			return result;
		}

		/// <summary>
		/// Return a new status which is the same as this one except for being locked
		/// by the specified user on this machine now (or not locked at all if lockedBy
		/// is null).
		/// </summary>
		/// <returns></returns>
		public BookStatus WithLockedBy(string lockedBy, string firstName = null, string surname = null)
		{
			var result = (BookStatus)this.MemberwiseClone();
			result.lockedBy = lockedBy;
			result.lockedByFirstName = firstName;
			result.lockedBySurname = surname;
			if (!result.IsCheckedOut())
			{
				result.lockedWhen = result.lockedWhere = null;
			}
			else
			{
				result.lockedWhen = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}";
				result.lockedWhere = TeamCollectionManager.CurrentMachine;
			}

			return result;
		}

		public BookStatus WithOldName(string oldName)
		{
			var result = (BookStatus) MemberwiseClone();
			result.oldName = oldName;
			return result;
		}

		/// <summary>
		/// Master definition of what it means to be checked out and able to edit.
		/// </summary>
		/// <param name="whoBy"></param>
		/// <returns></returns>
		public bool IsCheckedOutHereBy(string whoBy)
		{
			if (lockedBy == TeamCollection.FakeUserIndicatingNewBook)
				return true; // a new local book is always "checked out here"
			return lockedBy == whoBy && lockedWhere == TeamCollectionManager.CurrentMachine;
		}

		// <summary>
		/// Returns true is the book is checked out by anybody, false otherwise
		/// </summary>
		public bool IsCheckedOut() => !String.IsNullOrEmpty(lockedBy);

		public BookStatus WithCollectionId(string id)
		{
			var result = (BookStatus)MemberwiseClone();
			result.collectionId = id;
			return result;
		}
	}
}
