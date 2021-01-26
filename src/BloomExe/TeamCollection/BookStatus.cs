using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
		public string lockedWhen; // string.Format("{0:yyyy-MM-ddTHH:mm:ss.fffZ}", DateTime.UtcNow)
		public string lockedWhere; // Environment.MachineName

		public string ToJson()
		{
			return JsonConvert.SerializeObject(this);
		}

		public static BookStatus FromJson(string input)
		{
			return JsonConvert.DeserializeObject<BookStatus>(input);
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
		/// <param name="lockedBy"></param>
		/// <returns></returns>
		public BookStatus WithLockedBy(string lockedBy)
		{
			var result = (BookStatus)this.MemberwiseClone();
			result.lockedBy = lockedBy;
			if (String.IsNullOrEmpty(lockedBy))
			{
				result.lockedWhen = result.lockedWhere = null;
			}
			else
			{
				result.lockedWhen = string.Format("{0:yyyy-MM-ddTHH:mm:ss.fffZ}", DateTime.UtcNow);
				result.lockedWhere = Environment.MachineName;
			}

			return result;
		}

		/// <summary>
		/// Master definition of what it means to be checked out and able to edit.
		/// </summary>
		/// <param name="whoBy"></param>
		/// <returns></returns>
		public bool IsCheckedOutHereBy(string whoBy)
		{
			if (lockedBy == TeamRepo.FakeUserIndicatingNewBook)
				return true; // a new local book is always "checked out here"
			return lockedBy == whoBy && lockedWhere == Environment.MachineName;
		}
	}
}
