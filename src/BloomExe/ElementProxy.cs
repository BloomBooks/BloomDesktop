using System;
using System.Collections;
using System.Linq;
using System.Xml;
using SIL.Code;

namespace Bloom
{
	/// <summary>
	/// Takes a Gecko element or an XmlElement, and then provides a few simple operations on
	/// the element so that the client doesn't have to know which it has.
	/// This is useful because it would be hard to introduce gecko elements in many simple
	/// unit tests.
	/// </summary>
	/// <remarks>
	/// The Gecko element part of this class is no longer used and has been removed.
	/// We probably want to just remove this class altogether as part of the Geckofx cleanup.
	/// </remarks>
	public class ElementProxy
	{
		private readonly XmlElement _xmlElement;

		public ElementProxy(XmlElement element)
		{
			Guard.AgainstNull(element, "element");
			_xmlElement = element;
		}

		/// <summary>
		/// Sets the named attribute to {value}
		/// </summary>
		public void SetAttribute(string attributeName, XmlString value)
		{
			_xmlElement.SetAttribute(attributeName, value.Unencoded);	// This method will apply XML-encoding to its {value} parameter
		}

		/// <summary>
		/// Gets the class name
		/// </summary>
		public string Name
		{
			get {
				return _xmlElement?.Name;
			}
		}

		/// <summary>
		/// Gets the named attribute
		/// </summary>
		/// <returns>An empty string is returned if a matching attribute is not found or if the attribute does not have a specified or default value.</returns>
		public string GetAttribute(string attributeName)
		{
			return _xmlElement.GetAttribute(attributeName);
		}

		public override int GetHashCode()
		{
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
			return a._xmlElement == b._xmlElement;
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
			var result =_xmlElement.OwnerDocument.GetElementById(id);
			if (result == null)
				return null;
			return new ElementProxy(result);
		}

		public ElementProxy GetChildWithName(string name)
		{
			var result = _xmlElement.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.Name.ToLowerInvariant() == name) as XmlElement;
			if (result == null)
				return null;
			return new ElementProxy(result);
		}

		public ElementProxy AppendChild(string name)
		{
			var result = _xmlElement.OwnerDocument.CreateElement(name);
			_xmlElement.AppendChild(result);
			return new ElementProxy(result);
		}

		/// <summary>
		/// Return true if the element is a proxy for an input element that is checked.
		/// </summary>
		public bool Checked
		{
			get
			{
				return _xmlElement.Name == "input" &&  _xmlElement.GetAttribute("checked") != null;
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
				var parentX = _xmlElement.ParentNode as XmlElement;
				return parentX == null ? null : new ElementProxy(parentX);
			}
		}

		private static bool HasClass(ElementProxy element, string className)
		{
			var elementClassName = element._xmlElement.Attributes["class"].Value;
			return ((IList) elementClassName.Split(' ')).Contains(className);
		}

		public void AddClass(string className) => AddOrRemoveClass(className, true);
		public void RemoveClass(string className) => AddOrRemoveClass(className, false);

		public void AddOrRemoveClass(string className, bool wanted)
		{
			var classes = GetAttribute("class").Split(' ').ToList();
			if (wanted)
			{
				if (!classes.Contains(className))
					classes.Add(className);
			}
			else
			{
				classes.Remove(className);
			}

			// CSS class names can't contain punctuation other than underscore or hyphen,
			// so in terms of XML encoding, both FromUnencoded or FromXml will work fine because there's no special characters.
			// Just pick whichever one you want, don't fret about it.
			var classAttributeValue = XmlString.FromUnencoded(string.Join(" ", classes));
			SetAttribute("class", classAttributeValue);
		}

		public bool SelfOrAncestorHasClass(string className)
		{
			if (HasClass(this, className))
			{
				return true;
			}
			var parent = Parent;
			while (parent != null)
			{
				if (HasClass(parent, className))
					return true;

				parent = parent.Parent;
			}
			return false;
		}

		public ElementProxy SelfOrAncestorWithClass(string className)
		{
			for (var test = this; test != null; test = test.Parent)
			{
				if (HasClass(test,className))
					return test;
			}
			return null;
		}
	}
}
