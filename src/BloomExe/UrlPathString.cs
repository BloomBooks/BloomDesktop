using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Web;

namespace Bloom
{
	/// <summary>
	/// A wrapper around string designed to reduced bugs introduced by losing track of the encoded/unencoded state of a string.
	/// It does this by requiring users to specify what they are putting in/getting out, and it keeps track.
	/// </summary>
	public class UrlPathString
	{
		private readonly string _notEncoded;

		public static UrlPathString CreateFromUrlEncodedString(string encoded)
		{
			return new UrlPathString(HttpUtility.UrlDecode(encoded));
		}

		public static UrlPathString CreateFromUnencodedString(string unencoded)
		{
			unencoded = unencoded.Trim();

			// During the refactoring that lead to this class, one code path
			// essentially didn't trust that the string was already decoded.
			// Assuming that was done for a good reason, that behavior is
			// formalized here. It would seem to be a small risk (makes it
			// impossible to have, say "%20" in your actual file name).
			// However, a '+' in the name is much more likely, and so blindly
			// re-encoding is a problem. Not clear what's best, so at the moment
			// I'm going with detecting %20 an only encoding if we see that.
			
			// space % !	#	$	&	'	(	)	*	+	,	/	:	;	=	?	@	[	]
			if ("%20 %21 %23 %24 %25 %26 %27 %28 %29 %2A %2B %2C %2F %3A %3B %3D %3F %40 %5B %5D".Split(' ')
				.Any(s=>unencoded.Contains(s)))
					unencoded = HttpUtility.UrlDecode(unencoded);
			return new UrlPathString(unencoded);
		}

		public string UrlEncoded
		{
			get { return HttpUtility.UrlPathEncode(_notEncoded); }
		}

		public string NotEncoded
		{
			get { return _notEncoded; }
		}

		/// <summary>
		/// Gives the portion of the path up to and not including the query portion of the url
		/// </summary>
		public UrlPathString PathOnly
		{
			get
			{
				return CreateFromUnencodedString(_notEncoded.Split('?')[0]);
			}
		}

		private UrlPathString(string notEncodedString)
		{
			_notEncoded = notEncodedString;
		}

		public override bool Equals(object obj)
		{
			var x = obj as UrlPathString;
			if (x == null)
				return false;
			return this.NotEncoded == x.NotEncoded;
		}
		protected bool Equals(UrlPathString other)
		{
			return string.Equals(_notEncoded, other._notEncoded);
		}

		public static bool operator ==(UrlPathString a, UrlPathString b)
		{
			// If both are null, or both are same instance, return true.
			if (System.Object.ReferenceEquals(a, b))
			{
				return true;
			}

			// If one is null, but not both, return false.
			if (((object)a == null) || ((object)b == null))
			{
				return false;
			}

			// Return true if the fields match:
			return a.NotEncoded == b.NotEncoded;
		}

		public static bool operator !=(UrlPathString a, UrlPathString b)
		{
			return !(a == b);
		}

		public override int GetHashCode()
		{
			return (_notEncoded != null ? _notEncoded.GetHashCode() : 0);
		}
	}
}