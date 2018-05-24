using System.Xml;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// Some potentially generally useful functions for working with XML (especialy XHTML)
	/// </summary>
	public static class XmlExtensions
	{
		/// <summary>
		/// Find the closest parent of the recipient that has the indicated class.
		/// Will not match if the targetClass is a substring of a parent class.
		/// Will not return the recipient, even if it has the class.
		/// Returns null if there is no such parent.
		/// </summary>
		/// <param name="start"></param>
		/// <param name="targetClass"></param>
		/// <returns></returns>
		public static XmlElement ParentWithClass(this XmlElement start, string targetClass)
		{
			var current = start.ParentNode as XmlElement;
			while (current != null && !(" " + (current.Attributes["class"]?.Value?? "") + " ").Contains(" " + targetClass + " "))
				current = current.ParentNode as XmlElement;
			return current;
		}
	}
}
