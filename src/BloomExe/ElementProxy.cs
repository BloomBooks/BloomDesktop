using System;
using System.Collections;
using System.Xml;
using Gecko;
using Gecko.DOM;
using SIL.Code;

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
			get {
				return _xmlElement?.Name ?? _geckoElement.NodeName;
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

		public override int GetHashCode()
		{
			if (_xmlElement == null)
				return _geckoElement.GetHashCode();
			else
				return _xmlElement.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			var otherProxy = obj as ElementProxy;
			return this == otherProxy;
		}
		protected bool Equals(UrlPathString other)
		{
			throw new NotImplementedException();
		}

		public static bool operator ==(ElementProxy a, ElementProxy b)
		{
			if (object.ReferenceEquals(a, null))
				return object.ReferenceEquals(b, null);
			if (object.ReferenceEquals(b, null))
				return false;
			return a._xmlElement == b._xmlElement && a._geckoElement == b._geckoElement;
		}

		public static bool operator !=(ElementProxy a, ElementProxy b)
		{
			return !(a == b);
		}

		/// <summary>
		/// Get the element with the specified ID. Note that it may be anywhere in the containing document,
		/// not necessarily a child of the recipient element.
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		public ElementProxy GetElementById(string id)
		{
			if (_xmlElement == null)
			{
				var result = _geckoElement.OwnerDocument.GetElementById(id) as GeckoHtmlElement;
				if (result == null)
					return null;
				return new ElementProxy(result);
			}
			else
			{
				var result =_xmlElement.OwnerDocument.GetElementById(id);
				if (result == null)
					return null;
				return new ElementProxy(result);
			}
		}

		/// <summary>
		/// Return true if the element is a proxy for an input element that is checked.
		/// </summary>
		public bool Checked
		{
			get
			{
				if (_xmlElement == null)
				{
					var input = _geckoElement as GeckoInputElement;
					if (input == null)
						return false;
					return input.Checked;
				}
				else
				{
					return _xmlElement.Name == "input" &&  _xmlElement.GetAttribute("checked") != null;
				}

			}
		}

		/// <summary>
		/// Generates a new ElementProxy for the parent of the current ElementProxy up the relevant tree structure.
		/// Can return null.
		/// </summary>
		private ElementProxy Parent
		{
			get
			{
				if (_xmlElement == null)
				{
					var parentG = _geckoElement.Parent;
					return parentG == null ? null : new ElementProxy(parentG);
				}
				else
				{
					var parentX = _xmlElement.ParentNode as XmlElement;
					return parentX == null ? null : new ElementProxy(parentX);
				}
			}
		}

		private static bool HasClass(ElementProxy element, string className)
		{
			var elementClassName = element._geckoElement?.ClassName ?? element._xmlElement.Attributes["class"].Value;
			return ((IList) elementClassName.Split(' ')).Contains(className);
		}

		public bool SelfOrAncestorHasClass(string className)
		{
			if (HasClass(this, className))
			{
				return true;
			}
			var parent = Parent;
			while (parent != null && parent.Name != "BODY")
			{
				if (HasClass(parent, className))
					return true;

				parent = parent.Parent;
			}
			return false;
		}
	}
}
