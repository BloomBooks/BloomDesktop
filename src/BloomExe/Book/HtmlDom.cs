using System;
using System.Xml;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// HtmlDom manages the lower-level operations on a Bloom XHTML DOM.
	/// These doms can be a whole book, or just one page we're currently editing.
	/// They are actually XHTML, though when we save or send to a browser, we always convert to plain html.
	/// </summary>
	public class HtmlDom
	{
		private XmlDocument _dom;

		public HtmlDom()
		{
			_dom = new XmlDocument();
			_dom.LoadXml("<html></html>");
		}

		public HtmlDom(XmlDocument domToClone)
		{
			_dom = (XmlDocument) domToClone.Clone();
		}

		public HtmlDom(string xhtml)
		{
			_dom = new XmlDocument();
			_dom.LoadXml(xhtml);
		}

		public XmlElement Head
		{
			get
			{
				return XmlUtils.GetOrCreateElement(_dom, "html", "head");
			}
		}

		public string Title
		{
			get { return XmlUtils.GetTitleOfHtml(_dom, null); ; }
			set
			{
				var t = value.Trim();
				if (!String.IsNullOrEmpty(t))
				{
					var makeSureItsThere = Head;
					var titleNode = XmlUtils.GetOrCreateElement(_dom, "html/head", "title");
					//ah, but maybe that contains html element in there, like <br/> where the user typed a return in the title,

					//so we set the xhtml (not the text) of the node
					titleNode.InnerXml = t;
					//then ask it for the text again (will drop the xhtml)
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

		public HtmlDom Clone()
		{
			return new HtmlDom(RawDom);
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
		public XmlElement SelectSingleNode(string xpath)
		{
			return RawDom.SelectSingleNode(xpath) as XmlElement;
		}

		public XmlElement SelectSingleNodeHonoringDefaultNS(string xpath)
		{
			return _dom.SelectSingleNodeHonoringDefaultNS(xpath) as XmlElement;
		}

		public void AddJavascriptFile(string pathToJavascript)
		{
			XmlElement element = Head.AppendChild(_dom.CreateElement("script")) as XmlElement;
			element.SetAttribute("type", "text/javascript");
			element.SetAttribute("src", "file://" + pathToJavascript);
			Head.AppendChild(element);
		}


	}
}
