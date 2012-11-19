using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// BookDom just encapsulates the really low-level DOM operations; it doesn't really know
	/// about Books per se, but it has DOM operators so that code operating on the DOM can be
	/// simpler
	/// </summary>
	public class BookDom
	{
		private XmlDocument _dom;

		public BookDom()
		{
			_dom = new XmlDocument();
			_dom.LoadXml("<html></html>");
		}

		public BookDom(XmlDocument domToClone)
		{
			_dom = (XmlDocument) domToClone.Clone();
		}
		public BookDom(string xml)
		{
			_dom = new XmlDocument();
			_dom.LoadXml(xml);
		}

		public string Title
		{
			get { return XmlUtils.GetTitleOfHtml(_dom, null); ; }
			set
			{
				var t = value.Trim();
				if (!String.IsNullOrEmpty(t))
				{
					var headNode = XmlUtils.GetOrCreateElement(_dom,"html", "head");
					var titleNode = XmlUtils.GetOrCreateElement(_dom, "html/head", "title");
					//ah, but maybe that contains html element in there, like <br/> where the user typed a return in the title,

					//so we set the xml (not the text) of the node
					titleNode.InnerXml = t;
					//then ask it for the text again (will drop the xml)
					var justTheText = titleNode.InnerText.Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ");
					//then clear it
					titleNode.InnerXml = "";
					//and set the text again!
					titleNode.InnerText = justTheText;
				}
			}
		}

		public XmlDocument RawDom
		{
			get { return _dom; }
		}

		public BookDom Clone()
		{
			return new BookDom(RawDom);
		}
	}
}
