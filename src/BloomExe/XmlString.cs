using System;
using System.Collections.Generic;
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
		public string NotEncoded { get; private set; }

		/// <summary>
		/// Private constructor. To create an instance, use FromXml or FromNotEncoded instead.
		/// </summary>
		/// <param name="xmlFragment">XML text that is ready to go and already appropriately encoded for XML</param>
		private XmlString(string xmlFragment, string notEncoded)
		{
			Xml = xmlFragment;
			NotEncoded = notEncoded;
		}

		#region Initializers
		public static XmlString FromXml(string xmlFragment) => new XmlString(xmlFragment, Decode(xmlFragment));
		public static XmlString FromNotEncoded(string notEncodedText) => new XmlString(Encode(notEncodedText), notEncodedText);
		public static XmlString Empty => new XmlString("", "");
		#endregion

		#region Equality Boilerplate
		public override int GetHashCode()
		{
			return Xml?.GetHashCode() ?? 0;
		}

		public override bool Equals(object obj)
		{
			var x = obj as XmlString;
			if (x == null)
				return false;
			return this.Xml == x.Xml;
		}

		public static bool operator ==(XmlString a, XmlString b)
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
			return a.Xml == b.Xml;
		}

		public static bool operator !=(XmlString a, XmlString b)
		{
			return !(a == b);
		}
		#endregion

		private static string Decode(string encoded) => HttpUtility.HtmlDecode(encoded);
		private static string Encode(string decoded) => HttpUtility.HtmlEncode(decoded);
		public static bool IsNullOrEmpty(XmlString xmlString) => String.IsNullOrEmpty(xmlString?.Xml);

	}
}
