using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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

		/*	file: red & green, One + One
			URL-in-query-portion encoded? red+%26+green, One+%2B+One
			HTML/XML encoded: red &amp; green
			HttpUtility.UrlPathEncode: red%20&%20green,   One%20+%20One
		*/

		/// <summary>
		/// NOTE: Assumes '+' is literal. See BL-3259
		/// </summary>
		public static UrlPathString CreateFromUrlEncodedString(string encoded)
		{
			encoded = encoded.Replace("+", "%2B");
			return new UrlPathString(HttpUtility.UrlDecode(encoded));
		}

		public static UrlPathString CreateFromUnencodedString(string unencoded, bool strictlyTreatAsEncoded=false)
		{
			unencoded = unencoded.Trim();

			// During the refactoring that lead to this class, one code path
			// essentially didn't trust that the string was already decoded.
			// Assuming that was done for a good reason, that behavior is
			// formalized here. It would seem to be a small risk (makes it
			// impossible to have, say "%20" in your actual file name).
			// However, a '+' in the name is much more likely, and so blindly
			// re-encoding is a problem. So the algorithm is that if the
			// symbol is ambiguous (like '+'), assume it is unencoded (because that's
			// the name of the method) but if it's obviously encoded, then
			// decode it.
			
			if(!strictlyTreatAsEncoded && Regex.IsMatch(unencoded,"%[A-Fa-f0-9]{2}"))
					unencoded = HttpUtility.UrlDecode(unencoded);
			return new UrlPathString(unencoded);
		}

		/// <summary>
		/// In these strings, "&" would be &amp;  space would just be " "
		/// </summary>
		public static UrlPathString CreateFromHtmlXmlEncodedString(string encoded)
		{
			return new UrlPathString(HttpUtility.HtmlDecode(encoded));
		}

		public string UrlEncoded
		{
			get
			{
				//HttpUtility.UrlEncode gives spaces as "+" which is only good for query strings, not @src attributes
				//HttpUtility.UrlPathEncode, on the other hand, encodes % as %, when it we want %25.
				//Neither seems right.  We have to do a hack either way.
				//Since the docs ask you not to use UrlPathEncode, we'll use the other and hack it
				string standInForSpace = "_SpAcE_";
				//protect spaces from UrlEncode()
				var x = _notEncoded.Replace(" ",standInForSpace);
				x  = HttpUtility.UrlEncode(x);
				//now do our own encoding for the protected space
				return x.Replace(standInForSpace,"%20");
			}
		}

		public string HtmlXmlEncoded
		{
			get { return HttpUtility.HtmlEncode(_notEncoded); }
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
				//the 'true' here is to prevent us from getting a string with the instruction
				//to be strict about assuming it is unencoded, and then accidentally re-unencoding
				//it against that previous instruction, when we spil out the paths.
				return CreateFromUnencodedString(_notEncoded.Split('?')[0], true);
			}
		}

		private UrlPathString(string notEncodedString)
		{
			Debug.Assert(!notEncodedString.Contains("&amp;"));
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

		/// <summary>
		/// Some library books have been uploaded with the cover image filename URL encoded in the file instead of HTML/XML encoded.
		/// So if the file doesn't exist, try one more level of decoding to see if that may be the problem, but preserve the original
		/// path in case an error message is still needed.
		/// </summary>
		/// <param name="directory">path of the containing folder</param>
		/// <param name="filename">base filename to be combined with directory.  This may be modified by HttpUtility.UrlDecode().</param>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3901.
		/// </remarks>
		public static string GetFullyDecodedPath(string directory, ref string filename)
		{
			var path = System.IO.Path.Combine(directory, filename);
			if (!SIL.IO.RobustFile.Exists(path) && filename.Contains("%"))
			{
				var filename1 = System.Web.HttpUtility.UrlDecode(filename);
				var path1 = System.IO.Path.Combine(directory, filename1);
				if (SIL.IO.RobustFile.Exists(path1))
				{
					filename = filename1;
					return path1;
				}
			}
			return path;
		}
	}
}
