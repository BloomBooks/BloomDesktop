using System;
using System.IO;
using System.Xml;
using Palaso.Xml;


namespace Bloom
{
	public class XmlUtilities
	{

		//todo: what's the diff between this one and the next?
		static public XmlElement GetOrCreateElementPredicate(XmlDocument dom, XmlElement parent, string predicate, string name)
		{
			XmlElement element = (XmlElement)parent.SelectSingleNodeHonoringDefaultNS("/" + predicate);
			if (element == null)
			{
				element = parent.OwnerDocument.CreateElement(name, parent.NamespaceURI);
				parent.AppendChild(element);
			}
			return element;
		}

		static public XmlElement GetOrCreateElement(XmlDocument dom, string parentPath, string name)
		{
			XmlElement element = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS(parentPath + "/" + name);
			if (element == null)
			{
				XmlElement parent = (XmlElement)dom.SelectSingleNodeHonoringDefaultNS(parentPath);
				if (parent == null)
					return null;
				element = parent.OwnerDocument.CreateElement(name, parent.NamespaceURI);
				parent.AppendChild(element);
			}
			return element;
		}

		public static string GetStringAttribute(XmlNode form, string attr)
		{
			try
			{
				return form.Attributes[attr].Value;
			}
			catch (NullReferenceException)
			{
				throw new XmlFormatException(string.Format("Expected a {0} attribute on {1}.", attr, form.OuterXml));
			}
		}

		public static string GetOptionalAttributeString(XmlNode xmlNode, string attributeName)
		{
			XmlAttribute attr = xmlNode.Attributes[attributeName];
			if (attr == null)
				return null;
			return attr.Value;
		}

		public static XmlNode GetDocumentNodeFromRawXml(string outerXml, XmlNode nodeMaker)
		{
			if (string.IsNullOrEmpty(outerXml))
			{
				throw new ArgumentException();
			}
			XmlDocument doc = nodeMaker as XmlDocument;
			if (doc == null)
			{
				doc = nodeMaker.OwnerDocument;
			}
			using (StringReader sr = new StringReader(outerXml))
			{
				using (XmlReader r = XmlReader.Create(sr))
				{
					r.Read();
					return doc.ReadNode(r);
				}
			}
		}

		public static string GetXmlForShowingInHtml(string xml)
		{
			var s = Palaso.Xml.XmlUtils.GetIndendentedXml(xml).Replace("<", "&lt;");
			s = s.Replace("\r\n", "<br/>");
			s = s.Replace("  ", "&nbsp;&nbsp;");
			return s;
		}


		public static string GetTitleOfHtml(XmlDocument dom, string defaultIfMissing)
		{
			var title = dom.SelectSingleNode("//head/title");
			if (title != null && !string.IsNullOrEmpty(title.InnerText) && !string.IsNullOrEmpty(title.InnerText.Trim()))
			{
				return title.InnerText.Trim();
			}
			return defaultIfMissing;
		}
	}
}