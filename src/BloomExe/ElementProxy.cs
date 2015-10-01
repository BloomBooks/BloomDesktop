using System.Xml;
using Gecko;
using Palaso.Code;

namespace Bloom
{
	/// <summary>
	/// Takes a Gecko element or an XmlElement, and then provides a few simple operations on
	/// the element so that the client doesn't have to know which it has.
	/// This is useful because it would be hard to introduce gecko elements in many simple
	/// unit tests.
	/// </summary>
	public class ElementProxy
	{
		private readonly GeckoHtmlElement _geckoElement;
		private readonly XmlElement _xmlElement;

		public ElementProxy(GeckoHtmlElement element)
		{

			_geckoElement = element;
		}
		public ElementProxy(XmlElement element)
		{
			Guard.AgainstNull(element, "element");
			_xmlElement = element;
		}

		/// <summary>
		/// Sets the named attribute
		/// </summary>
		public void SetAttribute(string attributeName, string value)
		{
			if (_xmlElement == null)
			{
				_geckoElement.SetAttribute(attributeName, value);
			}
			else 
			{
				_xmlElement.SetAttribute(attributeName, value);
			}
		}

		/// <summary>
		/// Gets the class name
		/// </summary>
		public string Name
		{
			get
			{
				if (_xmlElement == null)
				{
					return _geckoElement.ClassName;
				}
				else
				{
					return _xmlElement.Name;
				}
			}
		}

		/// <summary>
		/// Gets the named attribute
		/// </summary>
		/// <returns>An empty string is returned if a matching attribute is not found or if the attribute does not have a specified or default value.</returns>
		public string GetAttribute(string attributeName)
		{
			if (_xmlElement == null)
			{
				return _geckoElement.GetAttribute(attributeName);
			}
			else
			{
				return _xmlElement.GetAttribute(attributeName);
			}
		}
	}
}