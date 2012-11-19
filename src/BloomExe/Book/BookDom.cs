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

		public string InnerXml
		{
			get { return _dom.InnerXml; }
		}

		public BookDom Clone()
		{
			return new BookDom(RawDom);
		}

		public void UpdatePageDivs()
		{
			//add a unique id for our use
			//review: bookstarter sticks in the ids, this one updates (and skips if it it didn't have an id before). At a minimum, this needs explanation
			foreach (XmlElement node in _dom.SafeSelectNodes("/html/body/div"))
			{
				//in the beta, 0.8, the ID of the page in the front-matter template was used for the 1st
				//page of every book. This screws up thumbnail caching.
				const string guidMistakenlyUsedForEveryCoverPage = "74731b2d-18b0-420f-ac96-6de20f659810";
				if (String.IsNullOrEmpty(node.GetAttribute("id"))
					|| (node.GetAttribute("id") == guidMistakenlyUsedForEveryCoverPage))
					node.SetAttribute("id", Guid.NewGuid().ToString());
			}
		}

		/// <summary>
		/// creates if necessary, then updates the named <meta></meta> in the head of the html
		/// </summary>
		/// <param name="dom"></param>
		/// <param name="name"></param>
		/// <param name="value"></param>
		public void UpdateMetaElement(string name, string value)
		{
			XmlElement n = _dom.SelectSingleNode("//meta[@name='" + name + "']") as XmlElement;
			if (n == null)
			{
				n = _dom.CreateElement("meta");
				n.SetAttribute("name", name);
				_dom.SelectSingleNode("//head").AppendChild(n);
			}
			n.SetAttribute("content", value);
		}


		public void SetBaseForRelativePaths(string path)
		{
			var head = _dom.SelectSingleNodeHonoringDefaultNS("//head");
			if (head == null)
				return;

			foreach (XmlNode baseNode in head.SafeSelectNodes("base"))
			{
				head.RemoveChild(baseNode);
			}
			var baseElement = _dom.CreateElement("base");
			baseElement.SetAttribute("href", path);
			head.AppendChild(baseElement);
		}

		public void AddStyleSheet(string locateFile)
		{
			RawDom.AddStyleSheet(locateFile);
		}

		public XmlNodeList SafeSelectNodes(string xpath)
		{
			return RawDom.SafeSelectNodes(xpath);
		}

		public XmlElement SelectSingleNodeHonoringDefaultNS(string xpath)
		{
			return _dom.SelectSingleNodeHonoringDefaultNS(xpath) as XmlElement;
		}
	}
}
