using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Web;

namespace Bloom
{
	/// <summary>
	/// Allows safer handling of strings representing an XML Fragment
	/// One noteworthy thing to mention compared to using a class (like UrlPathString) that stores the unencoded base form
	/// and converts to/from other formats is that if the string includes actual XML markup,
	/// you will get the wrong result form calling HtmlEncode(HtmlDecode(xml))!!!
	/// The text inside XML tags is fine to decode and re-encode, but the actual markup itself will not.
	/// e.g. Assume string xml = "<p>Hello & Bye</p>";
	/// The "Hello & Bye" part is fine to decode and encode.
	/// The "<p>" part will decode to "<p>"... which is technically correct, but will re-encode to "&lt;p&gt;", which is not what we wanted.
	/// So, this class stores string in their XML form, but provides an interface to make it easier to get it correctly.
	/// </summary>
	public class XmlString
	{
		public string Xml {get; private set; }

		private string _unencoded;
		public string Unencoded => GetUnencoded(true);

		/// <summary>
		/// Private constructor. To create an instance, use FromXml or FromNotEncoded instead.
		/// </summary>
		/// <param name="xmlFragment">XML text that is ready to go and already appropriately encoded for XML</param>
		private XmlString(string xmlFragment, string notEncoded)
		{
			Xml = xmlFragment;
			_unencoded = notEncoded;
		}

		#region Initializers
		/// <summary>
		/// Creates an XMLString object from an XML-ready string.
		/// </summary>
		/// <param name="xmlFragment">A string that could be written to an XML file(that is, it has been XML-encoded). e.g. "AT&amp;T is acceptable, but "AT&T" is not.
		/// It need not be a complete XML document though, nor do all tags need to having matching end tags. </param>
		/// <returns>The corresponding XML string object</returns>
		public static XmlString FromXml(string xmlFragment) => new XmlString(xmlFragment, Decode(xmlFragment));

		/// <summary>
		/// Creates an XMLString object from a simple, unencoded string.
		/// </summary>
		/// <param name="notEncodedText">A simple string with the user-facing text. For example, "AT&T" is fine. Generally, you should not use "AT&amp;T"
		/// <returns>The corresponding XML string object</returns>
		public static XmlString FromUnencoded(string notEncodedText) => new XmlString(Encode(notEncodedText), notEncodedText);

		/// <summary>
		/// Creates an XML String equivalent representing String.Empty
		/// </summary>
		public static XmlString Empty => new XmlString("", "");
		#endregion

		public string GetUnencoded(bool warnIfContainsMarkup = true)
		{
			if (warnIfContainsMarkup)
			{
				Debug.Assert(!this.ContainsMarkup(), "XmlString::NotEncoded() called on object containing markup. " +
					"This may be incorrect. (). XML tags like \"<p>\" will still be present in the result of NotEncoded. " +
					"Did you mean InnerText() instead? If not, set warnIfContainsMarkup to false to disable this warning.");
			}

			return this._unencoded;
		}

		/// <summary>
		/// This function is likely more meaningful when you want the non-XML version of strings containing XmlMarkup()
		/// </summary>
		/// <returns></returns>
		public string InnerText()
		{
			// TODO: Implement me when there's a caller that needs this instead of NotEncoded.
			throw new NotImplementedException();
		}

		#region Equality Boilerplate
		public override int GetHashCode()
		{
			return Xml?.GetHashCode() ?? 0;
		}

		public override bool Equals(object other)
		{
			// Treat the wrapper being null as equivalent to the underlying data being null
			// FYI: if obj is a literal null, its type is not XmlString. So, easier to handle this right away.
			if (other == null)
				return this.Xml == null;	

			//We now know that obj is non-null, and this is also non-null.
			if (!(other is XmlString))	
				return false;

			var x = (XmlString)other;
			return this.Xml == x.Xml;
		}

		public static bool operator ==(XmlString a, XmlString b)
		{
			// If both are null, or both are same instance, return true.
			if (System.Object.ReferenceEquals(a, b))
			{
				return true;
			}

			// Note: Need to cast to object so that a == null doesn't cause infinite recursion.
			if ((object)a == null && b?.Xml == null)
			{
				return true;
			}

			return a.Equals(b);
		}

		public static bool operator !=(XmlString a, XmlString b)
		{
			return !(a == b);
		}
		#endregion

		private static string Decode(string encoded) => HttpUtility.HtmlDecode(encoded);
		private static string Encode(string decoded) => HttpUtility.HtmlEncode(decoded);
		public static bool IsNullOrEmpty(XmlString xmlString) => String.IsNullOrEmpty(xmlString?.Xml);

		private bool ContainsMarkup() => Xml?.Contains("<") == true;
	}
}
