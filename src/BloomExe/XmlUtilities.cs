using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Xsl;


namespace Bloom
{
	public class NullXMlNodeList : XmlNodeList
	{
		public override XmlNode Item(int index)
		{
			throw new ArgumentOutOfRangeException();
		}

		public override IEnumerator GetEnumerator()
		{
			yield return null;
		}

		public override int Count
		{
			get { return 0; }
		}
	}

	public static class XmlNodeExtensions
	{
		public static XmlDocument StripXHtmlNameSpace(this XmlDocument node)
		{
//            var x = node.Clone() as XmlDocument;
			XmlDocument x = new XmlDocument();
			x.LoadXml(node.OuterXml.Replace("xmlns", "xmlnsNeutered"));
			return x;
		}

		/// <summary>
		/// this is safe to use with foreach, unlike SelectNodes
		/// </summary>
		public static XmlNodeList SafeSelectNodes(this XmlNode node, string path, XmlNamespaceManager namespaceManager)
		{
			var x = node.SelectNodes(path, namespaceManager);
			if (x == null)
				return new NullXMlNodeList();
			return x;
		}

		public static string SelectTextPortion(this XmlNode node, string path, params object[] args)
		{
			var x = node.SelectNodes(string.Format(path, args));
			if (x == null || x.Count ==0)
				return string.Empty;
			return x[0].InnerText;
		}

		public static string GetStringAttribute(this XmlNode node, string attr)
		{
			try
			{
				return node.Attributes[attr].Value;
			}
			catch (NullReferenceException)
			{
				throw new XmlFormatException(string.Format("Expected a '{0}' attribute on {1}.", attr, node.OuterXml));
			}
		}
		public static string GetOptionalStringAttribute(this XmlNode node, string attributeName, string defaultValue)
		{
			XmlAttribute attr = node.Attributes[attributeName];
			if (attr == null)
				return defaultValue;
			return attr.Value;
		}
	}

	public class XmlUtilities
	{



		public static string GetStringAttribute(XmlNode form, string attr)
		{
			try
			{
				return form.Attributes[attr].Value;
			}
			catch(NullReferenceException)
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
			if(string.IsNullOrEmpty(outerXml))
			{
				throw new ArgumentException();
			}
			XmlDocument doc = nodeMaker as XmlDocument;
			if(doc == null)
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
			var s = GetIndendentedXml(xml).Replace("<", "&lt;");
			s = s.Replace("\r\n", "<br/>");
			s = s.Replace("  ", "&nbsp;&nbsp;");
			return s;
		}

		/// <summary>
		/// lifted from http://www.knowdotnet.com/articles/indentxml.html
		/// </summary>
		/// <param name="xml"></param>
		/// <returns></returns>
		public static string GetIndendentedXml(string xml)
	  {
		 string outXml = string.Empty;
		 using(MemoryStream ms = new MemoryStream())
		 // Create a XMLTextWriter that will send its output to a memory stream (file)
		 using (XmlTextWriter xtw = new XmlTextWriter(ms, Encoding.Unicode))
		 {
			 XmlDocument doc = new XmlDocument();

//             try
//             {
				 // Load the unformatted XML text string into an instance
				 // of the XML Document Object Model (DOM)
				 doc.LoadXml(xml);

				 // Set the formatting property of the XML Text Writer to indented
				 // the text writer is where the indenting will be performed
				 xtw.Formatting = Formatting.Indented;

				 // write dom xml to the xmltextwriter
				 doc.WriteContentTo(xtw);
				 // Flush the contents of the text writer
				 // to the memory stream, which is simply a memory file
				 xtw.Flush();

				 // set to start of the memory stream (file)
				 ms.Seek(0, SeekOrigin.Begin);
				 // create a reader to read the contents of
				 // the memory stream (file)
				 StreamReader sr = new StreamReader(ms);
				 // return the formatted string to caller
				 return sr.ReadToEnd();
//             }
//             catch (Exception ex)
//             {
//                 return ex.Message;
//             }
		 }
	  }
	}

	public class XmlFormatException : ApplicationException
	{
		private string _filePath;
		public XmlFormatException(string message)
			: base(message)
		{
		}

		public string FilePath
		{
			get { return _filePath; }
			set { _filePath = value; }
		}
	}
}