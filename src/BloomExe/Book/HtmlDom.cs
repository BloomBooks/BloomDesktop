using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using System.Linq;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.Reporting;
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
			_dom.LoadXml("<html><head></head><body></body></html>");
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
			get { return XmlUtils.GetOrCreateElement(_dom, "html", "head"); }
		}

		public string Title
		{
			get
			{
				return XmlUtils.GetTitleOfHtml(_dom, null);
				;
			}
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




		public void SetBaseForRelativePaths(string path)
		{
			var head = _dom.SelectSingleNodeHonoringDefaultNS("//head");
			Guard.AgainstNull(head, "Expected the DOM to already have a head element");

			foreach (XmlNode baseNode in head.SafeSelectNodes("base"))
			{
				head.RemoveChild(baseNode);
			}

			if (path.Trim() != "") //jim (BL-323) reported a problem with  <base href="">
			{
				var baseElement = _dom.CreateElement("base");

				baseElement.SetAttribute("href", path);
				head.AppendChild(baseElement);
			}
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


		public void RemoveModeStyleSheets()
		{
			foreach (XmlElement linkNode in RawDom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (href == null)
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if (fileName.Contains("edit") || fileName.Contains("preview"))
				{
					linkNode.ParentNode.RemoveChild(linkNode);
				}
			}
		}

		public string ValidateBook(string descriptionOfBookForErrorLog)
		{
			var ids = new List<string>();
			var builder = new StringBuilder();

			Ensure(RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]").Count > 0, "Must have at least one page",
				   builder);
			EnsureIdsAreUnique(this, "textarea", ids, builder);
			EnsureIdsAreUnique(this, "p", ids, builder);
			EnsureIdsAreUnique(this, "img", ids, builder);

			//TODO: validate other things, including html
			var x = builder.ToString().Trim();
			if (x.Length == 0)
				Logger.WriteEvent("HtmlDom.ValidateBook({0}): No Errors", descriptionOfBookForErrorLog);
			else
			{
				Logger.WriteEvent("HtmlDom.ValidateBook({0}): {1}", descriptionOfBookForErrorLog, x);
			}

			return builder.ToString();
		}


		private static void Ensure(bool passes, string message, StringBuilder builder)
		{
			if (!passes)
				builder.AppendLine(message);
		}

		private static void EnsureIdsAreUnique(HtmlDom dom, string elementTag, List<string> ids, StringBuilder builder)
		{
			foreach (XmlElement element in dom.SafeSelectNodes("//" + elementTag + "[@id]"))
			{
				var id = element.GetAttribute("id");
				if (ids.Contains(id))
					builder.AppendLine("The id of this " + elementTag + " must be unique, but is not: " + element.OuterXml);
				else
					ids.Add(id);
			}
		}

		public void SortStyleSheetLinks()
		{
			List<XmlElement> links = new List<XmlElement>();
			foreach (XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				links.Add(link);
			}
			if (links.Count < 2)
				return;

			var headNode = links[0].ParentNode;

			//clear them out
			foreach (var xmlElement in links)
			{
				headNode.RemoveChild(xmlElement);
			}

			links.Sort(new StyleSheetLinkSorter());

			//add them back
			foreach (var xmlElement in links)
			{
				headNode.AppendChild(xmlElement);
			}
		}

		/// <summary>
		/// gecko 11 requires the file://, but modern firefox and chrome can't handle it. Checked also that IE10 works without it.
		/// </summary>
		public void RemoveFileProtocolFromStyleSheetLinks()
		{
			List<XmlElement> links = new List<XmlElement>();
			foreach (XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var linke = link.GetAttribute("href");
				link.SetAttribute("href", linke.Replace("file:///", "").Replace("file://", ""));
			}
		}

		//        /// <summary>
		//        /// the wkhtmltopdf thingy can't find stuff if we have any "file://" references (used for getting to pdf)
		//        /// </summary>
		//        /// <param name="dom"></param>
		//        private void StripStyleSheetLinkPaths(HtmlDom dom)
		//        {
		//            foreach (XmlElement linkNode in dom.SafeSelectNodes("/html/head/link"))
		//            {
		//                var href = linkNode.GetAttribute("href");
		//                if (href == null)
		//                {
		//                    continue;
		//                }
		//                linkNode.SetAttribute("href", Path.GetFileName(href));
		//            }
		//        }



		public static void AddClass(XmlElement e, string className)
		{
			e.SetAttribute("class", (e.GetAttribute("class") + " " + className).Trim());
		}

		public static void RemoveClassesBeginingWith(XmlElement xmlElement, string classPrefix)
		{

			var classes = xmlElement.GetAttribute("class");
			var original = classes;

			if (String.IsNullOrEmpty(classes))
				return;
			var parts = classes.SplitTrimmed(' ');

			classes = "";
			foreach (var part in parts)
			{
				if (!part.StartsWith(classPrefix))
					classes += part + " ";
			}
			xmlElement.SetAttribute("class", classes.Trim());

			//	Debug.WriteLine("RemoveClassesBeginingWith    " + xmlElement.InnerText+"     |    "+original + " ---> " + classes);
		}


		public static void AddClassIfMissing(XmlElement element, string className)
		{
			string classes = element.GetAttribute("class");
			if (classes.Contains(className))
				return;
			element.SetAttribute("class", (classes + " " + className).Trim());
		}


		/// <summary>
		/// Applies the XSLT, and returns an XML dom
		/// </summary>
		public XmlDocument ApplyXSLT(string pathToXSLT)
		{
			var transform = new XslCompiledTransform();
			transform.Load(pathToXSLT);
			using (var stringWriter = new StringWriter())
			using (var writer = XmlWriter.Create(stringWriter))
			{
				transform.Transform(RawDom.CreateNavigator(), writer);
				var result = new XmlDocument();
				result.LoadXml(stringWriter.ToString());
				return result;
			}
		}

		public string GetMetaValue(string name, string defaultValue)
		{
			var node = _dom.SafeSelectNodes("//head/meta[@name='" + name + "' or @name='" + name.ToLower() + "']");
			if (node.Count > 0)
			{
				return ((XmlElement) node[0]).GetAttribute("content");
			}
			return defaultValue;
		}

		public void RemoveMetaElement(string name)
		{
			foreach (XmlElement n in _dom.SafeSelectNodes("//head/meta[@name='" + name + "']"))
			{
				n.ParentNode.RemoveChild(n);
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

		/// <summary>
		/// Can be called without knowing that the old or new exists.
		/// If it already has the new, the old is just removed.
		/// This is just for migration.
		/// </summary>
		public void RenameMetaElement(string oldName, string newName)
		{
			if (!HasMetaElement(oldName))
				return;

			if (HasMetaElement(newName))
			{
				RemoveMetaElement(oldName);
				return;
			}

			//ok, so we do have to transfer the value over

			UpdateMetaElement(newName,GetMetaValue(oldName,""));

			//and remove any of the old name
			foreach(XmlElement node in _dom.SafeSelectNodes("//head/meta[@name='" + oldName + "']"))
			{
				node.ParentNode.RemoveChild(node);
			}

		}

		public bool HasMetaElement(string name)
		{
			return _dom.SafeSelectNodes("//head/meta[@name='" + name + "']").Count > 0;
		}

		public void RemoveExtraContentTypesMetas()
		{
			bool first=true;
			foreach (XmlElement n in _dom.SafeSelectNodes("//head/meta[@http-equiv='Content-Type']"))
			{
				if (first)//leave one
				{
					first = false;
					continue;
				}

				n.ParentNode.RemoveChild(n);
			}
		}

		public void AddStyleSheetIfMissing(string path)
		{
			foreach (XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href").ToLower();
				if (fileName == path.ToLower())
					return;
			}
			_dom.AddStyleSheet(path.Replace("file://", ""));
		}

		public IEnumerable<string> GetTemplateStyleSheets()
		{
			var stylesheetsToIgnore = new List<string>();
			stylesheetsToIgnore.Add("basepage.css");
			stylesheetsToIgnore.Add("languagedisplay.css");
			stylesheetsToIgnore.Add("editmode.css");
			stylesheetsToIgnore.Add("editoriginalmode.css");
			stylesheetsToIgnore.Add("previewmode.css");
			stylesheetsToIgnore.Add("settingsCollectionStyles.css".ToLower());
			stylesheetsToIgnore.Add("customCollectionStyles.css".ToLower());
			stylesheetsToIgnore.Add("customBookStyles.css".ToLower());
			stylesheetsToIgnore.Add("xmatter");

			foreach (XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href").ToLower();
				bool match = false;
				foreach (var nameOrFragment in stylesheetsToIgnore)
				{
					if (fileName.Contains(nameOrFragment))
					{
						match = true;
						break;
					}
				}
				if(!match)
					yield return fileName;
			}
		}
	}
}