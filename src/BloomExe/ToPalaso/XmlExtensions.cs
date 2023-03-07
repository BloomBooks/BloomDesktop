﻿using System.Linq;
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
			return ParentOrSelfWithClass(start.ParentNode as XmlElement, targetClass);
		}

		public static XmlElement ParentOrSelfWithClass(this XmlElement start, string targetClass)
		{
			var current = start;
			while (current != null &&
			       !(" " + (current.Attributes["class"]?.Value ?? "") + " ").Contains(" " + targetClass + " "))
				current = current.ParentNode as XmlElement;
			return current;
		}

		/// <summary>
		/// Find the closest ancestor (not 'start' itself) that has the specified value for the specified attribute.
		/// If no parent does, answer null.
		/// </summary>
		/// <returns></returns>
		public static XmlElement AncestorWithAttributeValue(this XmlElement start, string targetAttr, string targetVal)
		{
			var current = start.ParentNode as XmlElement;
			while (current != null && current.Attributes[targetAttr]?.Value != targetVal)
				current = current.ParentNode as XmlElement;
			return current;
		}

		/// <summary>
		/// Replace the element with its children.
		/// </summary>
		/// <param name="unwrapMe"></param>
		public static void UnwrapElement(this XmlElement unwrapMe)
		{
			var content = unwrapMe.ChildNodes.Cast<XmlNode>().Reverse().ToArray();
			var parent = unwrapMe.ParentNode as XmlElement;
			foreach (var child in content)
			{
				parent.InsertAfter(child, unwrapMe);
			}

			parent.RemoveChild(unwrapMe);
		}
	}
}
