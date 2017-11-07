using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Xsl;
using DesktopAnalytics;
using Gecko;
using SIL.Code;
using SIL.Extensions;
using SIL.Reporting;
using SIL.Text;
using SIL.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// HtmlDom manages the lower-level operations on a Bloom XHTML DOM.
	/// These doms can be a whole book, or just one page we're currently editing.
	/// They are actually XHTML, though when we save or send to a browser, we always convert to plain html.
	/// May also contain a BookInfo, which for certain operations should be kept in sync with the HTML.
	/// </summary>
	public class HtmlDom
	{
		public const string RelativePathAttrName = "data-base";
		private static readonly Regex s_regexBangImportant = new Regex("\\s*!\\s*important\\s*", RegexOptions.Compiled);
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

		public XmlElement Body
		{
			get { return XmlUtils.GetOrCreateElement(_dom, "html", "body"); }
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
				//if (!String.IsNullOrEmpty(t))
				//{
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
				//}
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
			foreach(XmlElement node in _dom.SafeSelectNodes("/html/body/div"))
			{
				//in the beta, 0.8, the ID of the page in the front-matter template was used for the 1st
				//page of every book. This screws up thumbnail caching.
				const string guidMistakenlyUsedForEveryCoverPage = "74731b2d-18b0-420f-ac96-6de20f659810";
				if(String.IsNullOrEmpty(node.GetAttribute("id"))
				   || (node.GetAttribute("id") == guidMistakenlyUsedForEveryCoverPage))
					node.SetAttribute("id", Guid.NewGuid().ToString());
			}
		}

		/// <summary>
		/// If the user added any custom pages in a version of bloom before 3.9 to a user defined template book created by
		/// Bloom 3.9, that page is unusable as a template page later in Bloom 3.9.  Fix it so that is is useable.
		/// </summary>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4491.
		/// Note that if we change how template pages are generated, this code may well need to change.
		/// </remarks>
		public void FixAnyAddedCustomPages()
		{
			foreach (XmlElement node in _dom.SafeSelectNodes("/html/body/div[contains(concat(' ', normalize-space(@class), ' '),' bloom-page ') and contains(concat(' ', normalize-space(@class), ' '),' customPage ')and @data-page='']"))
			{
				node.SetAttribute("data-page", "extra");
				foreach (XmlElement label in node.SafeSelectNodes("div[contains(concat(' ', normalize-space(@class), ' '), ' pageLabel ')]"))
				{
					label.RemoveAttribute("data-i18n");
					break;
				}
				foreach (XmlElement description in node.SafeSelectNodes("div[contains(concat(' ', normalize-space(@class), ' '), ' pageDescription ')]"))
				{
					description.InnerText = String.Empty;
					break;
				}
			}
		}

		private string _baseForRelativePaths = null;

		/// <summary>
		/// This property records the folder in which the browser needs to find files referred to using
		/// non-absolute locations.
		/// This method is designed to be used in conjunction with EnhancedImageServer.MakeSimulatedPageFileInBookFolder().
		/// which generates URLs that give the browser the content of this DOM, and also handles derived urls
		/// relative to that one.
		/// </summary>
		/// <remarks>Originally, this method created a 'base' element in the DOM, and a real
		/// temporary file would typically be created. The base element caused the browser to
		/// redirect things in much the way described above. However, this strategy fails
		/// for internal links within the document: a url like #mybookmark is translated
		/// into localhost://c:/users/someone/bloom/mycollection/mybookfolder#mybookmark, with no
		/// document specified at all, and passed to the server, which fails to find anything.
		/// Later it was discovered that Configurator (for Wall Calendar) put in a 'base' element,
		/// so we still need the parts that remove any 'base' element.</remarks>
		public string BaseForRelativePaths
		{
			get { return _baseForRelativePaths; }
			set
			{
				var path = value;
				_baseForRelativePaths = path ?? String.Empty;
				var head = _dom.SelectSingleNodeHonoringDefaultNS("//head");
				if(head == null)
					return;
				foreach(XmlNode baseNode in head.SafeSelectNodes("base"))
				{
					head.RemoveChild(baseNode);
				}
			}
		}

		/// <summary>
		/// Set this for DOMs that should not get the on-screen enhancements (transparency, possibly compression)
		/// of images. Typically for generating print-quality PDFs.
		/// </summary>
		internal bool UseOriginalImages { get; set; }


		public void AddStyleSheet(string path)
		{
			RawDom.AddStyleSheet(path);
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
			Head.AppendChild(MakeJavascriptElement(pathToJavascript));
		}

		private XmlElement MakeJavascriptElement(string pathToJavascript)
		{
			XmlElement element = Head.AppendChild(_dom.CreateElement("script")) as XmlElement;

			element.IsEmpty = false;
			element.SetAttribute("type", "text/javascript");
			element.SetAttribute("src", pathToJavascript.ToLocalhost());
			return element;
		}

		public void AddJavascriptFileToBody(string pathToJavascript)
		{
			Body.AppendChild(MakeJavascriptElement(pathToJavascript));
		}

		/// <summary>
		/// The Creation Type is either "translation" or "original". This is used to protect fields that should
		/// normally not be editable in one or the other.
		/// This is a bad name, and we know it!
		/// </summary>
		public void AddCreationType(string mode)
		{
			// RemoveModeStyleSheets() should have already removed any editMode attribute on the body element
			Body.SetAttribute("bookcreationtype", mode);
		}

		public void RemoveModeStyleSheets()
		{
			foreach(XmlElement linkNode in RawDom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if(String.IsNullOrEmpty(href))
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if(fileName.Contains("edit") || fileName.Contains("preview"))
				{
					linkNode.ParentNode.RemoveChild(linkNode);
				}
			}
			// If present, remove the editMode attribute that tells use which mode we're editing in (original or translation)
			var body = RawDom.SafeSelectNodes("/html/body")[0] as XmlElement;
			if(body.HasAttribute("editMode"))
				body.RemoveAttribute("editMode");
		}

		public string ValidateBook(string descriptionOfBookForErrorLog, bool mustHavePages)
		{
			var ids = new List<string>();
			var builder = new StringBuilder();

			if (mustHavePages)
			{
				Ensure(RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]").Count > 0, "Must have at least one page",
					builder);
			}
			EnsureIdsAreUnique(this, "textarea", ids, builder);
			EnsureIdsAreUnique(this, "p", ids, builder);
			EnsureIdsAreUnique(this, "img", ids, builder);

			//TODO: validate other things, including html
			var x = builder.ToString().Trim();
			if(x.Length == 0)
				Logger.WriteEvent("HtmlDom.ValidateBook({0}): No Errors", descriptionOfBookForErrorLog);
			else
			{
				Logger.WriteEvent("HtmlDom.ValidateBook({0}): {1}", descriptionOfBookForErrorLog, x);
			}

			return builder.ToString();
		}


		private static void Ensure(bool passes, string message, StringBuilder builder)
		{
			if(!passes)
				builder.AppendLine(message);
		}

		private static void EnsureIdsAreUnique(HtmlDom dom, string elementTag, List<string> ids, StringBuilder builder)
		{
			foreach(XmlElement element in dom.SafeSelectNodes("//" + elementTag + "[@id]"))
			{
				var id = element.GetAttribute("id");
				if(ids.Contains(id))
					builder.AppendLine("The id of this " + elementTag + " must be unique, but is not: " + element.OuterXml);
				else
					ids.Add(id);
			}
		}

		public void SortStyleSheetLinks()
		{
			List<XmlElement> links = new List<XmlElement>();
			foreach(XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				links.Add(link);
			}
			if(links.Count < 2)
				return;

			var headNode = links[0].ParentNode;

			//clear them out
			foreach(var xmlElement in links)
			{
				headNode.RemoveChild(xmlElement);
			}

			links.Sort(new StyleSheetLinkSorter());

			//add them back
			foreach(var xmlElement in links)
			{
				headNode.AppendChild(xmlElement);
			}
		}

		/// <summary>
		/// gecko 11 requires the file://, but modern firefox and chrome can't handle it. Checked also that IE10 works without it.
		/// </summary>
		public void RemoveFileProtocolFromStyleSheetLinks()
		{
			foreach(XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var href = link.GetAttribute("href");
				link.SetAttribute("href", href.Replace("file:///", "").Replace("file://", ""));
			}
		}

		public void RemoveDirectorySpecificationFromStyleSheetLinks()
		{
			foreach(XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var href = link.GetAttribute("href");
				link.SetAttribute("href", Path.GetFileName(href));
			}
		}

		public static void AddClass(XmlElement e, string className)
		{
			e.SetAttribute("class", (e.GetAttribute("class").Replace(className, "").Trim() + " " + className).Trim());
		}

		public static void AddRtlDir(XmlElement e)
		{
			e.SetAttribute("dir", "rtl");
		}

		public static void RemoveRtlDir(XmlElement e)
		{
			e.RemoveAttribute("dir");
		}

		public static void RemoveClassesBeginingWith(XmlElement xmlElement, string classPrefix)
		{

			var classes = xmlElement.GetAttribute("class");
			var original = classes;

			if(String.IsNullOrEmpty(classes))
				return;
			var parts = classes.SplitTrimmed(' ');

			classes = "";
			foreach(var part in parts)
			{
				if(!part.StartsWith(classPrefix))
					classes += part + " ";
			}
			xmlElement.SetAttribute("class", classes.Trim());

			//	Debug.WriteLine("RemoveClassesBeginingWith    " + xmlElement.InnerText+"     |    "+original + " ---> " + classes);
		}


		public static void AddClassIfMissing(XmlElement element, string className)
		{
			string classes = element.GetAttribute("class");
			if(classes.Contains(className))
				return;
			element.SetAttribute("class", (classes + " " + className).Trim());
		}

		public static bool HasClass(XmlElement element, string className)
		{
			return GetClasses(element).Contains(className);
		}

		/// <summary>
		/// Applies the XSLT, and returns an XML dom
		/// </summary>
		public XmlDocument ApplyXSLT(string pathToXSLT)
		{
			var transform = new XslCompiledTransform();
			transform.Load(pathToXSLT);
			using(var stringWriter = new StringWriter())
			using(var writer = XmlWriter.Create(stringWriter))
			{
				transform.Transform(RawDom.CreateNavigator(), writer);
				var result = new XmlDocument();
				result.LoadXml(stringWriter.ToString());
				return result;
			}
		}

		public string GetMetaValue(string name, string defaultValue)
		{
			var node = _dom.SafeSelectNodes("//head/meta[@name='" + name + "' or @name='" + name.ToLowerInvariant() + "']");
			if(node.Count > 0)
			{
				return ((XmlElement) node[0]).GetAttribute("content");
			}
			return defaultValue;
		}

		public void RemoveMetaElement(string name)
		{
			foreach(XmlElement n in _dom.SafeSelectNodes("//head/meta[@name='" + name + "']"))
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
			if(n == null)
			{
				n = _dom.CreateElement("meta");
				n.SetAttribute("name", name);
				_dom.SelectSingleNode("//head").AppendChild(n);
			}
			n.SetAttribute("content", value);
		}

		/// <summary>
		/// Can be called without knowing that the old exists.
		/// If it already has the new, the old is just removed.
		/// This is just for migration.
		/// </summary>
		public void RemoveMetaElement(string oldName, Func<string> read, Action<string> write)
		{
			if(!HasMetaElement(oldName))
				return;

			if(!String.IsNullOrEmpty(read()))
			{
				RemoveMetaElement(oldName);
				return;
			}

			//ok, so we do have to transfer the value over

			write(GetMetaValue(oldName, ""));

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

		/// <summary>
		/// Fix BL-2789, where Tok Pisin and Indonesian would show up in the source bubble for book titles,
		/// saying the equivalent of "new book" in each language. BasicBook doesn't have that anymore,
		/// but this cleans it up in books made from old shells.
		/// </summary>
		public void RemoveExtraBookTitles()
		{
			//NB: here we're just keeping it simple, not even making sure, for example, that
			//"Nupela Book" is in a Tok Pisin div. If it was in English, we'd zap it as well.
			//This xpath will collect up both divs in the data-div, and also copies of this
			//that may be in a bloom-translationGroup in the cover and title pages.
			var genericBookNames = new[] {"Basic Book", "Nupela Buk", "Buku Dasar"};
			foreach(XmlElement n in _dom.SafeSelectNodes("//*[@data-book='bookTitle']"))
			{
				if(genericBookNames.Contains(n.InnerText.Trim()))
				{
					n.ParentNode.RemoveChild(n);
				}
			}
		}

		public void RemoveExtraContentTypesMetas()
		{
			bool first = true;
			foreach(XmlElement n in _dom.SafeSelectNodes("//head/meta[@http-equiv='Content-Type']"))
			{
				if(first) //leave one
				{
					first = false;
					continue;
				}

				n.ParentNode.RemoveChild(n);
			}
		}

		public void AddStyleSheetIfMissing(string path)
		{
			// Remember, Linux filenames are case sensitive.
			var pathToCheck = path;
			if(Environment.OSVersion.Platform == PlatformID.Win32NT)
				pathToCheck = pathToCheck.ToLowerInvariant();
			foreach(XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				if(Environment.OSVersion.Platform == PlatformID.Win32NT)
					fileName = fileName.ToLowerInvariant();
				if(fileName == pathToCheck)
					return;
			}
			_dom.AddStyleSheet(path.Replace("file://", ""));
		}

		public virtual IEnumerable<string> GetTemplateStyleSheets()
		{
			var stylesheetsToIgnore = new List<string>();
			// Remember, Linux filenames are case sensitive!
			stylesheetsToIgnore.Add("basePage.css");
			stylesheetsToIgnore.Add("languageDisplay.css");
			stylesheetsToIgnore.Add("editMode.css");
			stylesheetsToIgnore.Add("editOriginalMode.css");
			stylesheetsToIgnore.Add("previewMode.css");
			stylesheetsToIgnore.Add("settingsCollectionStyles.css");
			stylesheetsToIgnore.Add("customCollectionStyles.css");
			stylesheetsToIgnore.Add("customBookStyles.css");
			stylesheetsToIgnore.Add("XMatter");

			foreach(XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				var nameToCheck = fileName;
				if(Environment.OSVersion.Platform == PlatformID.Win32NT)
					nameToCheck = fileName.ToLowerInvariant();
				bool match = false;
				foreach(var nameOrFragment in stylesheetsToIgnore)
				{
					var nameStyle = nameOrFragment;
					if(Environment.OSVersion.Platform == PlatformID.Win32NT)
						nameStyle = nameStyle.ToLowerInvariant();
					if(nameToCheck.Contains(nameStyle))
					{
						match = true;
						break;
					}
				}
				if(!match)
					yield return fileName;
			}
		}


		public void AddPublishClassToBody()
		{
			AddPublishClassToBody(_dom);
		}


		/// <summary>
		/// By including this class, we help stylesheets do something different for edit vs. publish mode.
		/// </summary>
		public static void AddPublishClassToBody(XmlDocument dom)
		{
			AddClass((XmlElement) dom.SelectSingleNode("//body"), "publishMode");
		}

		public static void AddRightToLeftClassToBody(XmlDocument dom)
		{
			AddClass((XmlElement) dom.SelectSingleNode("//body"), "rightToLeft");
		}

		public static void AddHidePlaceHoldersClassToBody(XmlDocument dom)
		{
			AddClass((XmlElement) dom.SelectSingleNode("//body"), "hidePlaceHolders");
		}

		public static void AddCalendarFoldClassToBody(XmlDocument dom)
		{
			AddClass((XmlElement) dom.SelectSingleNode("//body"), "calendarFold");
		}

		/// <summary>
		/// The chosen xmatter changes, so we need to clear out any old ones
		/// </summary>
		public void RemoveXMatterStyleSheets()
		{
			foreach(XmlElement linkNode in RawDom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if(Path.GetFileName(href).ToLowerInvariant().EndsWith("xmatter.css"))
				{
					linkNode.ParentNode.RemoveChild(linkNode);
				}
			}
		}

		internal void RemoveStyleSheetIfFound(string path)
		{
			XmlDomExtensions.RemoveStyleSheetIfFound(RawDom, path);
		}

		public void UpdatePageToTemplate(HtmlDom pageDom, XmlElement templatePageDiv, string pageId)
		{
			var pageDiv = pageDom.SafeSelectNodes("//body/div[@id='" + pageId + "']").Cast<XmlElement>().FirstOrDefault();
			if(pageDiv != null)
			{
				var idAttr = templatePageDiv.Attributes["id"];
				var templateId = idAttr == null ? "" : idAttr.Value;
				var oldLineage = MigrateEditableData(pageDiv, templatePageDiv, templateId);
				var props = new Dictionary<string, string>();
				props["newLayout"] = templateId;
				props["oldLineage"] = oldLineage;
				Analytics.Track("Change Page Layout", props);
			}
		}

		/// <summary>
		/// Replace page in its parent with an element which is a clone of template, but with the contents
		/// of page transferred as far as possible. Retain the id of the page. Set its lineage to the supplied value
		/// </summary>
		/// <param name="page"></param>
		/// <param name="template"></param>
		/// <param name="lineage"></param>
		/// <param name="originalTemplateGuid"></param>
		/// <param name="updateTo"></param>
		internal string MigrateEditableData(XmlElement page, XmlElement template, string lineage)
		{
			var newPage = (XmlElement) page.OwnerDocument.ImportNode(template, true);
			page.ParentNode.ReplaceChild(newPage, page);
			newPage.SetAttribute("id", page.Attributes["id"].Value);
			var oldLineageAttr = page.Attributes["data-pagelineage"];
			var oldLineage = oldLineageAttr == null ? "" : oldLineageAttr.Value;
			newPage.SetAttribute("data-pagelineage", lineage);

			//preserve the data-page attribute of the old page, which will normally be empty or missing
			var dataPageValue = page.GetAttribute("data-page");
			if(String.IsNullOrEmpty(dataPageValue))
			{
				newPage.RemoveAttribute("data-page");
			}
			else
			{
				newPage.SetAttribute("data-page", dataPageValue); //the template has these as data-page='extra'
			}

			// preserve the 'side' setting of the old page
			var classes = page.Attributes["class"].Value;
			var sideMatch = new Regex(@"\bside-\w*\b").Match(classes);
			if (sideMatch.Success)
			{
				newPage.SetAttribute("class", newPage.Attributes["class"].Value + " " + sideMatch.Value);
			}

			//the leading '.'s here are needed because newPage is an element in a larger DOM, and we only want to search in this page
			// migrate text (between visible translation groups!)
			// enhance: I wish there was a better way to detect invisible translation groups than just knowing about one class
			// that currently hides them.
			MigrateChildren(page, ".//div[contains(concat(' ', @class, ' '), ' bloom-translationGroup ') and not(contains(@class, 'box-header-off'))]", newPage);
			// migrate images
			MigrateChildren(page, ".//div[contains(concat(' ', @class, ' '), ' bloom-imageContainer ')]", newPage);
			return oldLineage;
		}

		/// <summary>
		/// For each div in the page which has the specified class, find the corresponding div with that class in newPage,
		/// and replace its contents with the contents of the source page.
		/// For translation groups, also updates the bloom-editable divs to have the expected class.
		/// </summary>
		/// <param name="page"></param>
		/// <param name="xpath"></param>
		/// <param name="newPage"></param>
		private static void MigrateChildren(XmlElement page, string xpath, XmlElement newPage)
		{
			var oldParents = page.SafeSelectNodes(xpath);
			var newParents = newPage.SafeSelectNodes(xpath);
			// The Math.Min is not needed yet; in fact, we don't yet have any cases where there is more than one
			// thing to copy or where the numbers are not equal. It's just a precaution.
			for(int i = 0; i < Math.Min(newParents.Count, oldParents.Count); i++)
			{
				var oldParent = (XmlElement) oldParents[i];
				var newParent = (XmlElement) newParents[i];
				string childClass = null;
				foreach (var child in newParent.ChildNodes.Cast<XmlNode>().ToArray())
				{
					if (childClass == null)
						childClass = GetStyle(child);
					newParent.RemoveChild(child);
				}
				// apparently we are modifying the ChildNodes collection by removing the child from there to insert in the new location,
				// which messes things up unless we make a copy of the collection.
				foreach(XmlNode child in oldParent.ChildNodes.Cast<XmlNode>().ToArray())
				{
					newParent.AppendChild(child);
					// Bloom-editable divs should have the user-defined class specified in the template if there is one.
					FixStyle(child, "bloom-editable", childClass);
					AddKnownStyleIfMissing(child);
				}
			}
		}

		private static Dictionary<string, string> _stylesToDefine;

		private static Dictionary<string, string> StylesToDefine
		{
			get
			{
				if(_stylesToDefine == null)
				{
					_stylesToDefine = new Dictionary<string, string>();
					_stylesToDefine["BigWords"] = ".BigWords-style { font-size: 45pt !important; text-align: center !important; }";
				}
				return _stylesToDefine;
			}
		}

		private static void AddKnownStyleIfMissing(XmlNode child)
		{
			if(child.Attributes == null)
				return; // e.g., whitespace
			var classAttr = child.Attributes["class"];
			if(classAttr == null)
				return;
			foreach(var style in classAttr.Value.Split(' ').Where(x => x.EndsWith("-style")))
			{
				var key = style.Substring(0, style.Length - ".style".Length);
				string defaultDefn;
				if(!StylesToDefine.TryGetValue(key, out defaultDefn))
					continue; // I don't think there should be more than one -style item, but just in case...
				// Todo: conditions...
				var headElt = child.OwnerDocument.DocumentElement.ChildNodes.Cast<XmlNode>().First(x => x.Name == "head");
				var userStyles = GetUserModifiedStyleElement(headElt);
				if(userStyles == null)
				{
					userStyles = AddEmptyUserModifiedStylesNode(headElt);
					userStyles.InnerText = defaultDefn;
					continue;
				}
				var content = userStyles.InnerText;
				var lookFor = new Regex("\\." + style + "\\s*{");
				if(lookFor.IsMatch(content))
					continue; // style already defined
				userStyles.InnerText += String.IsNullOrEmpty(content) ? defaultDefn : " " + defaultDefn;
			}
		}

		private static string GetStyle(XmlNode elt)
		{
			if (elt.Attributes == null)
				return null;
			var classAttr = elt.Attributes["class"];
			if (classAttr == null)
				return null;
			return classAttr.Value.Split(' ').FirstOrDefault(x => x.EndsWith("-style"));
		}

		private static void FixStyle(XmlNode child, string requiredClass, string desiredStyle)
		{
			if (desiredStyle == null || child.Attributes?["class"] == null || !child.Attributes["class"].Value.Contains(requiredClass) )
				return;
			var childStyle = GetStyle(child);
			string newclass;
			if (childStyle != null)
				newclass= child.Attributes["class"].Value.Replace(childStyle, desiredStyle);
			else
				newclass = child.Attributes["class"].Value + " " + desiredStyle;
			((XmlElement) child).SetAttribute("class", newclass);
		}

		// Both of these are relative to the DOM's Head element
		private const string CoverColorStyleXPath = "./style[@type='text/css' and contains(.,'coverColor')]";
		private const string UserModifiedStyleXPath = "./style[@type='text/css' and @title='userModifiedStyles']";

		/// <summary>
		/// Finds the style element that contains css rules for 'userModifiedStyles',
		/// or null if it doesn't exist.
		/// </summary>
		/// <param name="headElement"></param>
		internal static XmlElement GetUserModifiedStyleElement(XmlNode headElement)
		{
			return headElement.SafeSelectNodes(UserModifiedStyleXPath)
				.Cast<XmlElement>()
				.FirstOrDefault();
		}

		/// <summary>
		/// Finds the style element that contains css rules for 'coverColor',
		/// or null if it doesn't exist.
		/// </summary>
		/// <param name="headElement"></param>
		internal static XmlElement GetCoverColorStyleElement(XmlNode headElement)
		{
			return headElement.SafeSelectNodes(CoverColorStyleXPath)
				.Cast<XmlElement>()
				.FirstOrDefault();
		}

		internal static XmlElement AddEmptyUserModifiedStylesNode(XmlNode headElement)
		{
			var styleNode = headElement.OwnerDocument.CreateElement("style");
			styleNode.SetAttribute("type", "text/css");
			styleNode.SetAttribute("title", "userModifiedStyles");

			// apparently to make sure that the user's css rules get back to the dom
			// we need to ensure that the 'userModifiedStyles' element comes BEFORE the 'coverColor' element in the Head.
			var existingCoverColorNode = GetCoverColorStyleElement(headElement);
			if (existingCoverColorNode == null)
				headElement.AppendChild(styleNode);
			else
				headElement.InsertBefore(styleNode, existingCoverColorNode);
			return styleNode;
		}

		/// <summary>
		/// This method should only be used on the page DOM being inserted into a book by the Add Page/Change Layout dialog.
		/// It compares the styles in the head's user-defined styles section (inherited from the template book)
		/// with the ones referenced in the class attributes in the domForInsertedPage's body to see which styles need to be
		/// copied over to the new book.
		/// </summary>
		/// <param name="domForInsertedPage"></param>
		/// <returns></returns>
		internal static XmlNode GetUserModifiableStylesUsedOnPage(HtmlDom domForInsertedPage)
		{
			// there should only be one userModifiedStyles node, so this will only grab the first one
			var userStyleElementFromTemplate = GetUserModifiedStyleElement(domForInsertedPage.Head);
			if (userStyleElementFromTemplate == null)
				return AddEmptyUserModifiedStylesNode(domForInsertedPage.Head);

			var keyDict = GetUserStyleKeyDict(userStyleElementFromTemplate);
			var keysUsedOnPage = new Dictionary<string, string>();
			foreach (var keyPair in keyDict)
			{
				var style = GetStyleNameFromRuleSelector(keyPair.Key);
				var searchResult = domForInsertedPage.SafeSelectNodes(
					"//div[contains(concat(' ', @class, ' '), ' " + style + " ')]");
				if (searchResult.Count > 0)
					keysUsedOnPage.Add(keyPair.Key, keyPair.Value);
			}
			userStyleElementFromTemplate.InnerText =
				GetCompleteFilteredUserStylesInnerText(keysUsedOnPage);
			return userStyleElementFromTemplate;
		}

		private static string GetStyleNameFromRuleSelector(string selector)
		{
			// Key.Substring(1) strips off initial period from class name
			// Stripping off everything after -style removes [lang] stuff and >p stuff.
			var indexOfStyle = selector.LastIndexOf("-style", StringComparison.InvariantCulture);
			return selector.Substring(1, indexOfStyle + "-style".Length - 1);
		}

		/// <summary>
		/// Merges the user modified styles from an existing book with the ones used on a page inserted from a different template.
		/// This method will not overwrite a style already defined with the same name in the "receptor" book.
		/// It might, however, add a style where a pre-existing style differed only in language attribute.
		/// </summary>
		/// <param name="existingUserStyleNode">From current book's storage</param>
		/// <param name="insertedPageUserStyleNode"></param>
		/// <returns>The InnerXml to which the user modified styles element should be set.</returns>
		public static string MergeUserStylesOnInsertion(XmlNode existingUserStyleNode, XmlNode insertedPageUserStyleNode)
		{
			// this method in production is currently always called just after
			// CurrentBook.GetOrCreateUserModifiedStyleElementFromStorage()
			Guard.AgainstNull(existingUserStyleNode, "existingUserStyleNode");

			if (insertedPageUserStyleNode == null || insertedPageUserStyleNode.InnerXml == String.Empty)
				return Browser.WrapUserStyleInCdata(existingUserStyleNode.InnerText);

			var existingStyleKeyDict = GetUserStyleKeyDict(existingUserStyleNode);
			var existingStyleNames = new HashSet<string>();
			foreach (var key in existingStyleKeyDict.Keys)
			{
				existingStyleNames.Add(GetStyleNameFromRuleSelector(key));
			}
			var insertedPageStyleKeyDict = GetUserStyleKeyDict(insertedPageUserStyleNode); // could be empty
			foreach (var keyPair in insertedPageStyleKeyDict)
			{
				if (existingStyleNames.Contains(GetStyleNameFromRuleSelector(keyPair.Key)))
					continue;
				existingStyleKeyDict.Add(keyPair);
			}
			return Browser.WrapUserStyleInCdata(GetCompleteFilteredUserStylesInnerText(existingStyleKeyDict));
		}

		private static string GetCompleteFilteredUserStylesInnerText(IDictionary<string, string> desiredKeys )
		{
			var sb = new StringBuilder();
			foreach (var keyPair in desiredKeys)
			{
				sb.AppendLine(keyPair.Value);
			}
			return sb.ToString();
		}

		private const int minStyleLength = 6; // "-style".Length

		private static IDictionary<string, string> GetUserStyleKeyDict(XmlNode userStyleNode)
		{
			var keyDict = new Dictionary<string, string>();
			var styleStrings = GetStyles(userStyleNode.InnerText); // skips empty lines
			foreach (var styleString in styleStrings)
			{
				if (styleString.Length < minStyleLength)
					continue; // not sure how we'd get this... but just in case.
				var indexOfBrace = styleString.IndexOf("{", StringComparison.InvariantCulture);
				if (indexOfBrace < 0)
					continue; // doesn't have a rule...unlikely...anyway not useful.
				var key = styleString.Substring(0, indexOfBrace).Trim();
				keyDict[key] = styleString;
			}
			return keyDict;
		}

		private static IEnumerable<string> GetStyles(string innerTextOfStyleElement)
		{
			var styleLines = new List<string>();
			using (var sr = new StringReader(innerTextOfStyleElement))
			{
				string line;
				while ((line = sr.ReadLine()) != null)
				{
					if (String.IsNullOrWhiteSpace(line))
					{
						continue;
					}
					styleLines.Add(line.Trim()); // e.g. could have leading tabs
				}
			}
			// Handle possibility of multi-line style rules; side-effect: eliminates rules that don't START with a period (after trimming)
			var completeRule = String.Empty;
			foreach (var nextLine in styleLines)
			{
				if (nextLine.StartsWith("."))
				{
					if (!String.IsNullOrEmpty(completeRule))
					{
						yield return completeRule;
					}
					completeRule = nextLine;
				}
				else
				{
					if (String.IsNullOrEmpty(completeRule))
					{
						continue;
					}
					completeRule += Environment.NewLine + nextLine;
				}
			}
			yield return completeRule;
		}

		/* The following, to use normal url query parameters to say if we wanted transparency,
		 * was a nice idea, but turned out to not be necessary. I'm leave the code here in
		 * case in the future we do find a need to add query parameters.
		public  void SetImagesForMode(bool editMode)
		{
			SetImagesForMode((XmlNode)RawDom, editMode);
		}

		public static void SetImagesForMode(XmlNode pageNode, bool editMode)
		{
			foreach(XmlElement imgNode in pageNode.SafeSelectNodes(".//img"))
			{
				var src = imgNode.GetAttribute("src");
				const string kTransparent = "?makeWhiteTransparent=true";
				src = src.Replace(kTransparent, "");
				if (editMode)
					src = src + kTransparent;
				imgNode.SetAttribute("src",src);
			}
		}
		*/

		public static void MakeEditableDomShowAsTemplate(HtmlDom dom)
		{
			var label = dom.SelectSingleNode("//div[contains(@class,'pageLabel')]");
			if (label != null)
			{
				label.SetAttribute("contenteditable", "true");
			}
			var page = dom.SelectSingleNode("//div[contains(@class, 'bloom-page')]");
			page.SetAttribute("class", page.GetAttribute("class") + " bloom-templateMode");
		}

		// This should reverse what MakeEditableDomShowAsTemplate does.
		public static void RemoveTemplateEditingMarkup(XmlElement editedPageDiv)
		{
			var label = editedPageDiv.SelectSingleNode("//div[contains(@class,'pageLabel')]") as XmlElement;
			if (label != null)
			{
				label.RemoveAttribute("contenteditable");
			}

			editedPageDiv.SetAttribute("class", editedPageDiv.GetAttribute("class").Replace(" bloom-templateMode", ""));
		}

		public static void ProcessPageAfterEditing(XmlElement destinationPageDiv, XmlElement edittedPageDiv)
		{
			// strip out any elements that are part of bloom's UI; we don't want to save them in the document or show them in thumbnails etc.
			// Thanks to http://stackoverflow.com/questions/1390568/how-to-match-attributes-that-contain-a-certain-string for the xpath.
			// The idea is to match class attriutes which have class bloom-ui, but may have other classes. We don't want to match
			// classes where bloom-ui is a substring, though, if there should be any. So we wrap spaces around the class attribute
			// and then see whether it contains bloom-ui surrounded by spaces.
			// However, we need to do this in the edited page before copying to the storage page, since we are about to suck
			// info from the edited page into the dataDiv and we don't want the bloom-ui elements in there either!
			// Note that EditingView.CleanHtmlAndCopyToPageDom() also removes bits
			// of html that are used during editing but are not saved to disk.  (It calls javascript to deal with items inserted
			// by javascript.)
			foreach(
				var node in
					edittedPageDiv.SafeSelectNodes("//*[contains(concat(' ', @class, ' '), ' bloom-ui ')]").Cast<XmlNode>().ToArray())
				node.ParentNode.RemoveChild(node);
			RemoveTemplateEditingMarkup(edittedPageDiv);

			destinationPageDiv.InnerXml = edittedPageDiv.InnerXml;

			//Enhance: maybe we should just copy over all attributes?
			destinationPageDiv.SetAttribute("class", edittedPageDiv.GetAttribute("class"));
			//The SIL LEAD SHRP templates rely on "lang" on some ancestor to trigger the correct rules in labels.css.
			//Those get set by putting data-metalanguage on Page, which then leads to a lang='xyz'. Let's save that
			//back to the html in keeping with our goal of having the page look right if you were to just open the
			//html file in Firefox.
			destinationPageDiv.SetAttribute("lang", edittedPageDiv.GetAttribute("lang"));

			// Upon save, make sure we are not in layout mode.  Otherwise we show the sliders.
			foreach(
				var node in
					destinationPageDiv.SafeSelectNodes(".//*[contains(concat(' ', @class, ' '), ' origami-layout-mode ')]")
						.Cast<XmlNode>()
						.ToArray())
			{
				string currentValue = node.Attributes["class"].Value;
				node.Attributes["class"].Value = currentValue.Replace("origami-layout-mode", "");
			}
		}

		/// <summary>
		/// Gives all the unique language codes found in datadiv elements that have data-book
		/// </summary>
		/// <returns></returns>
		public List<string> GatherDataBookLanguages()
		{
			var dataBookElements = RawDom.SafeSelectNodes("//div[@id='bloomDataDiv']/div[@data-book]");
			return dataBookElements.Cast<XmlElement>()
				.Select(node => node.GetOptionalStringAttribute("lang", null))
				.Where(lang => !String.IsNullOrEmpty(lang) && (lang != "*" || lang != "z"))
				.Distinct()
				.ToList();
		}

		public MultiTextBase GetBookSetting(string key)
		{
			var result = new MultiTextBase();
			foreach(XmlElement e in RawDom.SafeSelectNodes("//div[@id='bloomDataDiv']/div[@data-book='" + key + "' or @data-derived='" + key + "']"))
			{
				var lang = e.GetAttribute("lang");
				result.SetAlternative(lang ?? "", e.InnerXml.Trim());
			}
			return result;
		}

		public void RemoveBookSetting(string key)
		{
			foreach(
				XmlElement e in
					RawDom.SafeSelectNodes("//div[@id='bloomDataDiv']/div[@data-book='" + key + "' or @data-derived='" + key + "']").Cast<XmlElement>().ToList())
			{
				e.ParentNode.RemoveChild(e);
			}
		}

		public void SetBookSetting(string key, string writingSystemId, string form)
		{
			var dataDiv = GetOrCreateDataDiv(RawDom);

			// Some old books may have values for this key with no language. Because GetBookSetting is coded to
			// find these, they may take precendence over the one we are setting. To prevent this, once a book
			// is updated using the new system, any obsolete no-language versions will be removed.
			XmlElement obsoleteNode =
				dataDiv.SelectSingleNode(String.Format("div[@data-book='{0}' and not(@lang)]", key)) as XmlElement;
			if(obsoleteNode != null)
				dataDiv.RemoveChild(obsoleteNode);

			XmlElement node =
				dataDiv.SelectSingleNode(String.Format("div[@data-book='{0}' and @lang='{1}']", key,
					writingSystemId)) as XmlElement;

			if(String.IsNullOrEmpty(form))
			{
				if(null != node)
					dataDiv.RemoveChild(node);
				return;
			}

			if(null == node)
			{
				node = RawDom.CreateElement("div");
				node.SetAttribute("data-book", key);
				node.SetAttribute("lang", writingSystemId);
			}
			SetElementFromUserStringPreservingLineBreaks(node, form);
			dataDiv.AppendChild(node);
		}

		public static void SetElementFromUserStringPreservingLineBreaks(XmlElement node, string form)
		{
			//Note: this method is a compromise... it replaces a couple instances where we were
			//explicitly using innerText instead of innerXml, presumably on purpose. Of course that
			//makes it impossible to have any html markup. My particular need right now (BL-3832) is to
			//allow <br> to get through this filter. So that's all this does. A future alternative
			//might be to remove the filter altogether and see if there's a better way to handle
			//whatever scenarios the filtering was designed to prevent.

			// not InnerXml as it may contain things like SILA & LASI that are not valid XML
			const string kBR = "LINEBREAKHERE";
			var withBreaksHidden = form.Replace("<br />", kBR).Replace("<br/>", kBR);

			//going to innertext means we treat everything literally, for better or worse (definitely safer)
			node.InnerText = withBreaksHidden;
			// finally, unhide the breaks
			node.InnerXml = node.InnerXml.Replace(kBR, "<br/>");
		}

		/// <summary>
		/// Blindly merge the classes from the source into the target.
		/// </summary>
		/// <param name="sourcePage"></param>
		/// <param name="targetPage"></param>
		/// <param name="classesToDrop"></param>
		public static void MergeClassesIntoNewPage(XmlElement sourcePage, XmlElement targetPage, string[] classesToDrop)
		{
			foreach(var c in GetClasses(sourcePage))
			{
				if(!classesToDrop.Contains(c))
					AddClassIfMissing(targetPage, c);
			}
		}

		private static IEnumerable<string> GetClasses(XmlElement element)
		{
			var classes = element.GetAttribute("class");
			if(String.IsNullOrEmpty(classes))
				return new string[] {};
			return classes.SplitTrimmed(' ');
		}

		/// <summary>
		/// Find the first child of parent that has the specified class as (one of) its classes.
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="classVal"></param>
		/// <returns></returns>
		public static XmlElement FindChildWithClass(XmlElement parent, string classVal)
		{
			// Can probably be done with xpath ./*[contains(concat(" ", normalize-space(@class), " "), " classVal ")]
			// (plus something to get the first one).
			// But I'm more confident of this version and suspect it might be faster for such a simple case.
			foreach(var node in parent.ChildNodes)
			{
				var elt = node as XmlElement;
				if(elt == null)
					continue;
				var eltClass = " " + GetAttributeValue(elt, "class") + " ";
				if(eltClass.Contains(" " + classVal + " "))
					return elt;
			}
			return null;
		}

		public static string GetAttributeValue(XmlElement elt, string name)
		{
			var attr = elt.Attributes[name];
			if(attr == null)
				return "";
			return attr.Value;
		}

		/// <summary>
		/// Finds a list of fonts used in the given css
		/// </summary>
		/// <param name="cssContent"></param>
		/// <param name="result"></param>
		/// <param name="includeFallbackFonts">true to include fallback fonts, false to include only the first font in each font family</param>
		public static void FindFontsUsedInCss(string cssContent, HashSet<string> result, bool includeFallbackFonts)
		{
			var findFF = new Regex("font-family:\\s*([^;}]*)[;}]");
			foreach(Match match in findFF.Matches(cssContent))
			{
				foreach(var family in match.Groups[1].Value.Split(','))
				{
					var name = family.Trim();
					// Strip matched quotes
					if(name[0] == '\'' || name[0] == '"' && name[0] == name[name.Length - 1])
						name = name.Substring(1, name.Length - 2);
					name = s_regexBangImportant.Replace(name, "");
					if (name.ToLowerInvariant() != "inherit" && name.ToLowerInvariant() != "segoe ui")
						result.Add(name);
					if(!includeFallbackFonts)
						break;
				}
			}
		}

		/// <summary>
		/// Gets the url for the image, either from an img element or any other element that has
		/// an inline style with background-image set.
		/// </summary>
		public static UrlPathString GetImageElementUrl(GeckoHtmlElement imageElement)
		{
			return GetImageElementUrl(new ElementProxy(imageElement));
		}

		/// <summary>
		/// Gets the url for the image, either from an img element or any other element that has
		/// an inline style with background-image set.
		/// </summary>
		public static UrlPathString GetImageElementUrl(XmlElement imageElement)
		{
			return GetImageElementUrl(new ElementProxy(imageElement));
		}

		/// <summary>
		/// Gets the url for the image, either from an img element or any other element that has
		/// an inline style with background-image set.
		/// </summary>
		public static UrlPathString GetImageElementUrl(ElementProxy imgOrDivWithBackgroundImage)
		{
			if(imgOrDivWithBackgroundImage.Name.ToLower() == "img")
			{
				var src = imgOrDivWithBackgroundImage.GetAttribute("src");
				return UrlPathString.CreateFromUrlEncodedString(src);
			}
			else
			{
				var styleRule = imgOrDivWithBackgroundImage.GetAttribute("style") ?? "";
				var regex = new Regex("background-image\\s*:\\s*url\\((.*)\\)", RegexOptions.IgnoreCase);
				var match = regex.Match(styleRule);
				if(match.Groups.Count == 2)
				{
					return UrlPathString.CreateFromUrlEncodedString(match.Groups[1].Value.Trim(new[] {'\'', '"'}));
				}
			}
			//we choose to return this instead of null to reduce errors created by things like
			// HtmlDom.GetImageElementUrl(element).UrlEncoded. If we just returned null, that has to be written
			// as something that checks for null, like:
			//  var url = HtmlDom.GetImageElementUrl(element). if(url!=null) url.UrlEncoded
			return UrlPathString.CreateFromUnencodedString(String.Empty);
		}

		/// <summary>
		/// Sets the url attribute either of an img (the src attribute)
		/// or a div with an inline style with an background-image rule
		/// </summary>
		public static void SetImageElementUrl(ElementProxy imgOrDivWithBackgroundImage, UrlPathString url)
		{
			if(imgOrDivWithBackgroundImage.Name.ToLower() == "img")
			{
				imgOrDivWithBackgroundImage.SetAttribute("src", url.UrlEncoded);
			}
			else
			{
				imgOrDivWithBackgroundImage.SetAttribute("style", String.Format("background-image:url('{0}')", url.UrlEncoded));
			}
		}

		public static XmlNodeList SelectChildImgAndBackgroundImageElements(XmlElement element)
		{
			return element.SelectNodes(".//img | .//*[contains(@style,'background-image')]");
		}

		public static bool IsImgOrSomethingWithBackgroundImage(XmlElement element)
		{
			return element.SelectNodes("self::img | self::*[contains(@style,'background-image')]").Count == 1;
		}

		public static XmlElement GetOrCreateDataDiv(XmlNode dom)
		{
			var dataDiv = dom.SelectSingleNode("//div[@id='bloomDataDiv']") as XmlElement;
			if(dataDiv == null)
			{
				XmlDocument doc = dom as XmlDocument;
				if(doc == null)
					doc = dom.OwnerDocument;
				dataDiv = doc.CreateElement("div");
				dataDiv.SetAttribute("id", "bloomDataDiv");
				dom.SelectSingleNode("//body").InsertAfter(dataDiv, null);
			}
			return dataDiv;
		}

		public static void AddStylesheetFromAnotherBook(HtmlDom sourceBookDom, HtmlDom targetBookDom)
		{
			var addedModifiedStyleSheets = new List<string>();
			//This was refactored from book, where there was these notes:
			//     NB: at this point this code can't handle the "userModifiedStyles" from children, it'll ignore them (they would conflict with each other)
			//     NB: at this point custom styles (e.g. larger/smaller font rules) from children will be lost.

			//At this point, this addedModifiedStyleSheets is just used as a place to track which stylesheets we already have
			foreach(string sheetName in sourceBookDom.GetTemplateStyleSheets())
			{
				if(!addedModifiedStyleSheets.Contains(sheetName))
					//nb: if two books have stylesheets with the same name, we'll only be grabbing the 1st one.
				{
					addedModifiedStyleSheets.Add(sheetName);
					targetBookDom.AddStyleSheetIfMissing(sheetName);
				}
			}
		}

		public static string ConvertHtmlBreaksToNewLines(string html)
		{
			// newlines had no meaning in html-land, may well have been introduced for readability of the xml/html
			html = html.Replace("\r", "").Replace("\n", ""); // works for \n (linux) and \r\n (windows)

			//now we can move from the html br to newlines for non-html use
			return html.Replace("<br/>", Environment.NewLine)
				.Replace("<br />", Environment.NewLine)
				.Replace("<br>", Environment.NewLine);
		}

		private IEnumerable<XmlElement> GetContentPageElements()
		{
			return _dom.SafeSelectNodes(
					"/html/body/div[contains(@class,'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]")
				.OfType<XmlElement>();
		}

		private IEnumerable<XmlElement> GetPageElements()
		{
			return _dom.SafeSelectNodes(
					"/html/body/div[contains(@class,'bloom-page')]")
				.OfType<XmlElement>();
		}

		/// <summary>
		/// Can switch a page from being a template page or back to a normal page.
		/// </summary>
		/// <param name="areTemplatePages"></param>
		public void MarkPagesWithTemplateStatus(bool areTemplatePages)
		{
			foreach(var page in GetContentPageElements())
			{
				MakePageWithTemplateStatus(areTemplatePages, page);
			}
		}

		/// <summary>
		/// Can switch a page from being a template page or back to a normal page.
		/// </summary>
		public static void MakePageWithTemplateStatus(bool isTemplatePage, XmlElement page)
		{
			page.SetAttribute("data-page", isTemplatePage ? "extra" : "");
			if (!isTemplatePage)
				return;
			var label = page.SelectSingleNode("div[contains(@class,'pageLabel')]") as XmlElement;
			if (label != null)
			{
				// Assume that they are going to change the name. Note as of 3.9 at least, we don't have a way of localizing these.
				label.RemoveAttribute("data-i18n");
			}
		}

		/// <summary>
		/// Reads the Generator meta tag.
		/// </summary>
		/// <returns> the version if it can find it, else version 0.0</returns>
		public Version GetGeneratorVersion()
		{
			var generator = GetMetaValue("Generator", "");
			var match = Regex.Match(generator, "[0-9]+(\\.[0-9]+)+");
			if (match.Success)
				return new Version(match.Captures[0].Value);
			return new Version(0, 0);
		}

		public bool RecordedAsLockedDown => SafeSelectNodes(String.Format("//meta[@name='lockedDownAsShell' and @content='true']")).Count > 0;

		public void RecordAsLockedDown(bool locked)
		{
			if (locked)
			{
				UpdateMetaElement("lockedDownAsShell", "true");
			}
			else
			{
				RemoveMetaElement("lockedDownAsShell");
			}
		}

		/// <summary>
		/// Given a page or a child of a page, return the page
		/// </summary>
		private XmlElement GetPageDivOfElement(XmlElement element)
		{
			return element.SelectSingleNode("ancestor-or-self::div[contains(@class,'bloom-page')]") as XmlElement;
		}

		/// <summary>
		/// Figure out what page number would be shown on the page
		/// </summary>
		public int GetPageNumberOfPage(XmlElement pageDiv )
		{
			var allPages = RawDom.SelectNodes("html/body/div[contains(@class,'bloom-page')]").Cast<XmlElement>();

			var pageNumber = 0;
			foreach(var p in allPages)
			{
				if(HtmlDom.HasClass(p, "numberedPage") || HtmlDom.HasClass(p, "countPageButDoNotShowNumber"))
				{
					++pageNumber;
				}
				if(HtmlDom.HasClass(p, "bloom-startPageNumbering"))
				{
					pageNumber=1;
				}
				if (p == pageDiv)
					return pageNumber;
			}
			return -1;
		}

		public XmlElement FindPageById(string pageId)
		{
			return RawDom.SafeSelectNodes("//body/div[@id='" + pageId + "']").Cast<XmlElement>().FirstOrDefault();
		}

		/// <summary>
		/// Updates the side-right and side-left classes of every page div in the supplied dom.
		/// These will only be correct if the dom is the full book; from a dom made of one page,
		/// we cannot determine if it is left or right, nor what page.
		/// </summary>
		public void UpdatePageNumberAndSideClassOfPages(string charactersForDigits, bool languageIsRightToLeft)
		{
			var i = 0;
			foreach (var pageDiv in GetPageElements())
			{
				UpdateSideClass(pageDiv, i, languageIsRightToLeft);
				// enhance: we could optimize this since we are doing them all in sequence...
				// we'd have to call this until we get a non-empty page number, but perhaps
				// we could keep going through the back cover so long as the stylesheet
				// ignores the numbers on those so no one will see them?
				var number = GetPageNumberOfPage(pageDiv);
				var numberInScript = number > 0 ? GetNumberStringRepresentation(number, charactersForDigits) : "";
				pageDiv.SetAttribute("data-page-number", numberInScript);
				i++;
			}
		}

		/// <summary>
		/// Simplified from https://stackoverflow.com/a/35099462/723299
		/// If there are number systems that can't be represented by just exchanging digits, this won't work for them.
		/// </summary>
		private string GetNumberStringRepresentation(int postiveInteger, string charactersForDigits)
		{
			if(string.IsNullOrEmpty(charactersForDigits))
				return postiveInteger.ToString(CultureInfo.InvariantCulture);

			Debug.Assert(charactersForDigits.Length==10);

			return String.Join("", postiveInteger.ToString(CultureInfo.InvariantCulture)
				.Select(x =>
				{
					if ("1234567890".Contains(x.ToString()))
						return charactersForDigits.Substring(x - '0', 1);
					else
						return x.ToString();
				}));
		}

		private static void UpdateSideClass(XmlElement pageDiv, int indexOfPageZeroBased, bool languageIsRightToLeft)
		{
			RemoveClassesBeginingWith(pageDiv, "side-");
			var rightSideRemainder = languageIsRightToLeft ? 1 : 0;
			AddClassIfMissing(pageDiv, indexOfPageZeroBased % 2 == rightSideRemainder ? "side-right" : "side-left");
		}
	}
}
