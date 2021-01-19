using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.Xsl;
using Bloom.Api; // for DynamicJson
using Bloom.Publish.Epub;
using Bloom.web.controllers;
using DesktopAnalytics;
using Gecko;
using Microsoft.CSharp.RuntimeBinder;
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
		public static readonly char[] kHtmlClassDelimiters = new char[] { ' ' };
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
			}
			set
			{
				var t = value.Trim();
				var titleNode = XmlUtils.GetOrCreateElement(_dom, "html/head", "title");
				//ah, but maybe that contains html element in there, like <br/> where the user typed a return in the title,
				//so we set the xhtml (not the text) of the node
				titleNode.InnerXml = t;
				// then ask it for the text again (which drops the xhtml) and ensure that each run of whitespace
				// (which includes newline characters) is condensed to a single space character.
				// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5979.
				var justTheText = Regex.Replace(titleNode.InnerText, "\\s+", " ");
				//then clear it
				titleNode.InnerXml = "";
				//and set the text again!
				titleNode.InnerText = justTheText;
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
				foreach (XmlElement label in GetAllDivsWithClass(node, "pageLabel"))
				{
					label.RemoveAttribute("data-i18n");
					break;
				}
				foreach (XmlElement description in GetAllDivsWithClass(node, "pageDescription"))
				{
					description.InnerText = string.Empty;
					break;
				}
			}
		}

		private string _baseForRelativePaths = null;

		/// <summary>
		/// This property records the folder in which the browser needs to find files referred to using
		/// non-absolute locations.
		/// This method is designed to be used in conjunction with BloomServer.MakeSimulatedPageFileInBookFolder().
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

		public void AddOrReplaceMetaElement(string name, string content)
		{
			var meta = _dom.SelectSingleNode($"/html/head/meta[@name='{name}']") as XmlElement;
			if (meta != null)
			{
				meta.SetAttribute("content", content);
				return;
			}
			meta = Head.AppendChild(_dom.CreateElement("meta")) as XmlElement;
			meta.SetAttribute("name", name);
			meta.SetAttribute("content", content);
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
				var pages = RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]");
				Ensure(pages.Count > 0, "Must have at least one page", builder);

				// Tried a couple of 3rd-party Html parsers (HtmlAgilityPack and AngleSharp) and they either
				// found errors where they shouldn't have (Wall Calendar htm) or didn't find any errors at all.
				// The below method verifies that each page is still at the right level of the DOM, which actually
				// goes a long way to making sure that the htm parses correctly.
				// BL-6273 hand-edited html was causing crashes because we didn't catch invalid html of page, so
				// at least check that we can find each page.
				foreach (XmlNode pageNode in pages)
				{
					var id = pageNode.Attributes["id"]?.Value;
					// if there is a .bloom-page div with no id attribute, we're probably in a test.
					if (string.IsNullOrEmpty(id) && Program.RunningUnitTests)
						continue;
					// test that the "page" is at the right level in the DOM
					if (pageNode.ParentNode?.Name.ToLowerInvariant() != "body")
						builder.AppendLine("Bloom-page element not found at root level: " + id);
				}
			}
			EnsureIdsAreUnique(this, "textarea", ids, builder);
			EnsureIdsAreUnique(this, "p", ids, builder);
			EnsureIdsAreUnique(this, "img", ids, builder);

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
				// TODO: Maybe it should ignore empty strings?
				// This can throw an error which prevents saving the book, but the error is arguably a false positive.
				// Documentation says: "If the id value is not the empty string, it must be unique in a document."
				// https://developer.mozilla.org/en-US/docs/Web/API/Element/id
				//
				// On the other hand, the W3 validator reports "An ID must not be the empty string", so alternatively we can return an error for any empty string, not just the 2nd one.

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
			stylesheetsToIgnore.Add("langVisibility.css");
			stylesheetsToIgnore.Add("editMode.css");
			stylesheetsToIgnore.Add("editOriginalMode.css");
			stylesheetsToIgnore.Add("previewMode.css");
			stylesheetsToIgnore.Add("defaultLangStyles.css");
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


		public void AddPublishClassToBody(string kindOfPublication=null)
		{
			AddPublishClassToBody(_dom, kindOfPublication);
		}


		/// <summary>
		/// By including this class, we help stylesheets do something different for edit vs. publish mode.
		/// </summary>
		public static void AddPublishClassToBody(XmlDocument dom, string kindOfPublication = null)
		{
			AddClassToBody(dom, "publishMode");
			if(null!=kindOfPublication)
				AddClassToBody(dom, kindOfPublication);
		}
		public static void AddClassToBody(XmlDocument dom,string className)
		{
			AddClass((XmlElement)dom.SelectSingleNode("//body"), className);
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

		public bool UpdatePageToTemplate(HtmlDom pageDom, XmlElement templatePageDiv, string pageId, bool allowDataLoss = true)
		{
			var pageDiv = pageDom.SafeSelectNodes("//body/div[@id='" + pageId + "']").Cast<XmlElement>().FirstOrDefault();
			if(pageDiv != null)
			{
				var idAttr = templatePageDiv.Attributes["id"];
				var templateId = idAttr == null ? "" : idAttr.Value;
				bool didChange;
				var oldLineage = MigrateEditableData(pageDiv, templatePageDiv, templateId, allowDataLoss, out didChange);
				if (!didChange)
					return false;
				var props = new Dictionary<string, string>();
				props["newLayout"] = templateId;
				props["oldLineage"] = oldLineage;
				Analytics.Track("Change Page Layout", props);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Replace page in its parent with an element which is a clone of template, but with the contents
		/// of page transferred as far as possible. Retain the id of the page. Set its lineage to the supplied value
		/// </summary>
		/// <param name="page"></param>
		/// <param name="template"></param>
		/// <param name="lineage"></param>
		/// <param name="allowDataLoss"></param>
		/// <param name="didChange"></param>
		internal string MigrateEditableData(XmlElement page, XmlElement template, string lineage, bool allowDataLoss, out bool didChange)
		{
			if (!allowDataLoss)
			{
				// See comment on GetTranslationGroupsInternal() below.
				var oldTextCount = GetTranslationGroupCount(page);
				var newTextCount = GetTranslationGroupCount(template);
				var oldImageCount = GetAllDivsWithClass(page, "bloom-imageContainer").Count;
				var newImageCount = GetAllDivsWithClass(template, "bloom-imageContainer").Count;
				var oldVideoCount = GetAllDivsWithClass(page, "bloom-videoContainer").Count;
				var newVideoCount = GetAllDivsWithClass(template, "bloom-videoContainer").Count;
				if (newTextCount < oldTextCount || newImageCount < oldImageCount || newVideoCount < oldVideoCount)
				{
					didChange = false;
					return null;
				}
			}

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

			// preserve the page size and orientation of the old page
			newPage.SetAttribute("class", TransferOrientation(classes, newPage.Attributes["class"].Value));

			//the leading '.'s here are needed because newPage is an element in a larger DOM, and we only want to search in this page
			// migrate text (between visible translation groups!)
			// enhance: I wish there was a better way to detect invisible translation groups than just knowing about one class
			// that currently hides them.
			MigrateChildren(GetTranslationGroups(page), GetTranslationGroups(newPage));
			// migrate images
			MigrateChildrenWithCommonClass(page, "bloom-imageContainer", newPage);
			// migrate videos
			MigrateChildrenWithCommonClass(page, "bloom-videoContainer", newPage);
			RemovePlaceholderVideoClass(newPage);
			didChange = true;
			return oldLineage;
		}

		private int GetTranslationGroupCount(XmlElement pageElement)
		{
			var result = GetTranslationGroups(pageElement);

			return result.Count;
		}

		private List<XmlElement> GetTranslationGroups(XmlElement pageElement)
		{
			var result = new List<XmlElement>();
			GetTranslationGroupsInternal(pageElement, ref result);

			return result;
		}

		// We want to count all the translationGroups that do not occur inside of a bloom-imageContainer div.
		// The reason for this is that images can have textOverPicture divs and imageDescription divs inside of them
		// and these are completely independent of the template page. We need to count regular translationGroups and
		// also ensure that translationGroups inside of images get migrated correctly. If this algorithm changes, be
		// sure to also change 'countTranslationGroupsForChangeLayout()' in page-chooser.ts.
		// We could just do this with an xpath if bloom-textOverPicture divs and bloom-imageDescription divs had
		// the same structure internally, but text over picture CONTAINS a translationGroup,
		// whereas image description IS a translationGroup.
		private void GetTranslationGroupsInternal(XmlElement currentElement, ref List<XmlElement> result)
		{
			if (currentElement.HasAttribute("class"))
			{
				var classes = currentElement.Attributes["class"].Value;
				if (classes.Contains("bloom-imageContainer"))
					return; // don't drill down inside of this one
				if (classes.Contains("bloom-translationGroup"))
				{
					// box-header-off/on appears to be vestigial at this point,
					// but suffice it to say "box-header-off" translationGroups are not visible.
					if (!classes.Contains("box-header-off"))
					{
						result.Add(currentElement);
					}
					return; // don't drill down further
				}
			}

			if (!currentElement.HasChildNodes)
				return;
			foreach (XmlNode childNode in currentElement.ChildNodes)
			{
				var childElement = childNode as XmlElement;
				if (childElement == null) // if the node is not castable to XmlElement
					continue;

				GetTranslationGroupsInternal(childElement, ref result);
			}
		}

		/// <summary>
		/// Gets a JSON string of colors used in the current book.
		/// </summary>
		public string GetColorsUsedInBookBubbleElements()
		{
			var colorElementList = new List<string>();
			var textOverPictureElements = GetTextOverPictureElements(Body);
			foreach (var node in textOverPictureElements)
			{
				var styleAttr = node.GetOptionalStringAttribute("style", "");
				if (!string.IsNullOrEmpty(styleAttr))
				{
					// Possible bubble text color
					var textColorValue = GetColorValueFromStyle(styleAttr);
					if (!string.IsNullOrEmpty(textColorValue))
					{
						var textColorString = DynamicJson.Serialize(new
						{
							colors = new [] { textColorValue }
						});
						colorElementList.Add(textColorString);
					}
				}
				var dataBubbleAttr = node.GetOptionalStringAttribute("data-bubble", "");
				if (string.IsNullOrEmpty(dataBubbleAttr))
					continue;

				// Possible bubble background color
				var jsonObject = GetJsonObjectFromDataBubble(dataBubbleAttr);
				if (jsonObject == null)
					continue; // only happens if it fails to parse the "json"

				// Note: background color strings with opacity will be in the form "rgba(r, g, b, a)",
				// while text color strings and background strings w/o opacity could be hex strings or even named colors.
				string[] backgroundColorArray = GetBackgroundColorsFromDataBubbleJsonObj(jsonObject);
				if (backgroundColorArray == null || backgroundColorArray.Length == 0)
					continue;

				var backgroundColorString = DynamicJson.Serialize(new
				{
					colors = backgroundColorArray
				});

				colorElementList.Add(backgroundColorString);
			}

			return "[" + string.Join(",", colorElementList) + "]";
		}

		private static string GetColorValueFromStyle(string styleAttrVal)
		{
			// Looking for something like "color: rgb(x,y,z);" or "color: #aaaaaa;"
			var styleRegex = new Regex(@"\s*color\s*:\s*(.+)\s*;");
			var match = styleRegex.Match(styleAttrVal);
			// If successful, group 1 should be everything between "color: " and ";"
			return match.Success ? match.Groups[1].Value : string.Empty;
		}

		internal static dynamic GetJsonObjectFromDataBubble(string dataBubbleAttrVal)
		{
			dynamic result;
			try
			{
				result = DynamicJson.Parse(dataBubbleAttrVal.Replace("`", "\""));
			}
			catch (Exception)
			{
				Logger.WriteEvent("HtmlDom.GetJsonObjectFromDataBubble() failed to parse data-bubble: " + dataBubbleAttrVal);
				result = null;
			}
			return result;
		}

		private static string[] GetBackgroundColorsFromDataBubbleJsonObj(dynamic jsonObject)
		{
			if (jsonObject == null)
				return null;
			try
			{
				return jsonObject.backgroundColors;
			}
			catch (RuntimeBinderException)
			{
				return null; // This is the 'normal' branch if backgroundColors aren't defined.
			}
		}

		internal static string GetStyleFromDataBubbleJsonObj(dynamic jsonObject)
		{
			if (jsonObject == null)
				return "none";
			try
			{
				return jsonObject.style;
			}
			catch (RuntimeBinderException)
			{
				return "none";
			}
		}

		private static XmlNodeList GetAllDivsWithClass(XmlNode containerElement, string className)
		{
			const string xpath = ".//div[contains(concat(' ', normalize-space(@class), ' '), ' {0} ')]";
			var classPath = xpath.Replace("{0}", className);
			return containerElement.SafeSelectNodes(classPath);
		}

		private static IEnumerable<XmlElement> GetTextOverPictureElements(XmlNode bookBodyElement)
		{
			return GetAllDivsWithClass(bookBodyElement, "bloom-textOverPicture").Cast<XmlElement>();
		}

		/// <summary>
		/// Returns the bloom-editable divs that have valid (e.g. non-empty) language attributes on them.
		/// Ignores divs that are under bloom-ignoreChildrenForBookLanguageList
		/// </summary>
		/// <param name="includeXMatter">True to include divs in xmatter pages, false to exclude them</param>
		internal IEnumerable<XmlElement> GetLanguageDivs(bool includeXMatter)
		{
			// These are the elements that represent a bloom-page
			var pageElements = includeXMatter ? GetPageElements() : GetContentPageElements();

			// Search the bloom-page for which elements are the language divs,
			// then flattern the list of lists into a single list.
			var langDivs = pageElements.SelectMany(page => page.SafeSelectNodes(".//div[@class and @lang]").Cast<XmlElement>());

			// Check each language div against some additional criteria
			langDivs = langDivs
				// BL-8228. Don't proceed if this is a text without normal parentage, e.g. boilerplate text from a Branding pack.
				// Before we added this line, the next one (testing for bloom-ignoreChildrenForBookLanguageList) would throw with
				// the Juarez and Associates (Guatemala) Branding Pack.
				.Where(div => div.ParentNode?.Attributes?["class"] != null)
				// At least up through 4.7, bloom-ignoreChildrenForBookLanguageList is used to prevent counting localized 
				// headers in comprehension quiz as languages of the book.
				.Where(div => !div.ParentNode.Attributes["class"].Value.Contains("bloom-ignoreChildrenForBookLanguageList"))
				.Where(div => div.Attributes["class"].Value.IndexOf("bloom-editable", StringComparison.InvariantCulture) >= 0)
				.Where(div => HtmlDom.IsLanguageValid(div.Attributes["lang"].Value));

			return langDivs;
		}

		/// <summary>
		/// Checks if the specified language is considered valid (e.g. non-empty, not "*", not "z")
		/// </summary>
		internal static bool IsLanguageValid(string lang)
		{
			return !String.IsNullOrWhiteSpace(lang) && lang != "*" && lang != "z";  // Not valid languages, though we sometimes use them for special purposes
		}

		private static void RemovePlaceholderVideoClass(XmlElement newPage)
		{
			const string videoPlaceholderClass = "bloom-noVideoSelected";
			var nodesWithPlaceholder = newPage.SelectNodes("//div[contains(@class,'" + videoPlaceholderClass + "')]");
			foreach (XmlNode placeholderDiv in nodesWithPlaceholder)
			{
				if (placeholderDiv.HasChildNodes && placeholderDiv.FirstChild.Name == "video")
				{
					// We migrated a video node into here, delete the placeholder class.
					XmlUtils.SetAttribute(placeholderDiv, "class", XmlUtils.GetStringAttribute(placeholderDiv, "class").
						Replace(videoPlaceholderClass, string.Empty));
				}
			}
		}

		internal static string TransferOrientation(string classes, string newClasses)
		{
			var soRegex = new Regex(@"\b\S*(Portrait|Landscape)\b");
			var oldMatch = soRegex.Match(classes);
			if (oldMatch.Success)
			{
				var newMatch = soRegex.Match(newClasses);
				if (newMatch.Success)
				{
					newClasses = soRegex.Replace(newClasses, oldMatch.Value);
				}
				else
				{
					newClasses = newClasses + " " + oldMatch.Value;
				}
			}
			return newClasses;
		}

		/// <summary>
		/// For each div in the page which has the specified class, find the corresponding div with that class in newPage,
		/// and replace its contents with the contents of the source page.
		/// For translation groups, also updates the bloom-editable divs to have the expected class.
		/// </summary>
		/// <param name="page"></param>
		/// <param name="className"></param>
		/// <param name="newPage"></param>
		private static void MigrateChildrenWithCommonClass(XmlElement page, string className, XmlElement newPage)
		{
			var oldParents = new List<XmlElement>(GetAllDivsWithClass(page, className).Cast<XmlElement>());
			var newParents = new List<XmlElement>(GetAllDivsWithClass(newPage, className).Cast<XmlElement>());
			MigrateChildren(oldParents, newParents);
		}

		private const string defaultLangKey = "z";

		// Migrate container (translationGroup, video, image) children from an old page to their new template equivalent.
		private static void MigrateChildren(IReadOnlyList<XmlElement> oldParentElements,
			IReadOnlyList<XmlElement> templateParentElements)
		{
			// 'xParentElements' are either 'bloom-translationGroup', 'bloom-imageContainer', or 'bloom-videoContainer'
			// The Math.Min is not needed yet; in fact, we don't yet have any cases where there is more than one
			// thing to copy or where the numbers are not equal. It's just a precaution.
			for (var i = 0; i < Math.Min(templateParentElements.Count, oldParentElements.Count); i++)
			{
				var oldParent = oldParentElements[i];
				var newParent = templateParentElements[i];
				// Our template page may have style classes on bloom-editables (or possibly other elements
				// inside of the container element). We want to preserve them when we copy the child nodes into the new page,
				// so we collect them by lang attribute value.
				// It is possible (using Template Starter or the ChangeLayout toggle) to create a situation where different
				// lang attribute bloom-editables have different style classes ('x-style').
				var childClassesFromTemplateByLang = new Dictionary<string, string>();
				foreach (var templateContainerChild in newParent.ChildNodes.Cast<XmlNode>().ToArray())
				{
					// Look for an 'x-style' class in the child and squirrel it away in our dictionary, keyed on language.
					var langAttr = GetLangAttr(templateContainerChild);
					var	childClassFromTemplate = GetStyle(templateContainerChild);
					// langAttr check probably only possible to fail if the child is NOT a div
					if (langAttr != null && childClassFromTemplate != null)
					{
						string dummy;
						if (langAttr == "*" || langAttr == "z")
							langAttr = defaultLangKey;
						if (childClassesFromTemplateByLang.TryGetValue(langAttr, out dummy)) // paranoia
							continue;
						childClassesFromTemplateByLang.Add(langAttr, childClassFromTemplate);
					}
					// Whether we found a style class or not, remove the template container's children to make way for the
					// children of the old page's matching container.
					newParent.RemoveChild(templateContainerChild);
				}
				// apparently we are modifying the ChildNodes collection by removing the child from there to insert in the new location,
				// which messes things up unless we make a copy of the collection.
				foreach (XmlNode oldContainerChild in oldParent.ChildNodes.Cast<XmlNode>().ToArray())
				{
					// add the old container's children to the new container matching the template
					newParent.AppendChild(oldContainerChild);
					// .bloom-editable divs should have the user-defined class specified in the template if there is one.
					FixStyle(oldContainerChild, "bloom-editable", childClassesFromTemplateByLang);
					AddKnownStyleIfMissing(oldContainerChild);
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

		public static string GetStyle(XmlNode elt)
		{
			var classAttr = elt.Attributes?["class"];
			return classAttr?.Value.Split(' ').FirstOrDefault(x => x.EndsWith("-style"));
		}

		private static string GetLangAttr(XmlNode elt)
		{
			var langAttr = elt.Attributes?["lang"];
			return langAttr?.Value;
		}

		private static void FixStyle(XmlNode child, string requiredClass, Dictionary<string, string> desiredStyleByLang)
		{
			if (desiredStyleByLang.Count == 0 || child.Attributes?["class"] == null ||
			    !child.Attributes["class"].Value.Contains(requiredClass))
				return;
			var childStyle = GetStyle(child);
			var langAttr = GetLangAttr(child);
			if (langAttr == null || langAttr == "*" || langAttr == "z")
				langAttr = defaultLangKey;
			string defaultStyle;
			desiredStyleByLang.TryGetValue(defaultLangKey, out defaultStyle); // if unsuccessful, 'defaultStyle' will be ""
			string newStyle;
			if (!desiredStyleByLang.TryGetValue(langAttr, out newStyle))
				newStyle = defaultStyle;
			string newclass;
			if (childStyle != null)
				newclass = child.Attributes["class"].Value.Replace(childStyle, newStyle);
			else
				newclass = child.Attributes["class"].Value + " " + newStyle;
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
				var searchResult = GetAllDivsWithClass(domForInsertedPage.Body, style);
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

		/// <summary>
		///  See BLoom Book File Format: Book Features
		///  https://docs.google.com/document/d/16M8Fvt1SLYgUX5UbWy3q9s2_ab0Ni39mVumtIYzxKm4/edit#heading=h.pdn1blelp3ds
		/// </summary>
		/// <remarks>Currently, this is only setting the values on the body element, where it can be read by BloomReader
		/// and CSS. We plan to make the standard "data-div" be "official" registry of feature settings.
		/// We will still be echoing them on the body, though, because CSS could never get at them in the data-div.  </remarks>
		/// <param name="dom"></param>
		/// <param name="featureName"></param>
		/// <param name="orientationConstraint"></param>
		/// <param name="mediaConstraint"></param>
		public  void SetBookFeature(string featureName, string orientationConstraint, string mediaConstraint)
		{
			Debug.Assert(featureName == featureName.ToLowerInvariant(),"HTML requires attribute names to be all lower case (feature)");
			Body.SetAttribute("data-bf" +featureName, orientationConstraint +";"+mediaConstraint);
		}

		/// <summary>
		/// Remove the specified feature. I included the constraints for consistency with SetBookFeature,
		/// but actually we don't currently support more than one constraint pair per feature, so any remove
		/// removes that featre completely, irrespective of constraints.
		/// </summary>
		/// <param name="featureName"></param>
		/// <param name="orientationConstraint"></param>
		/// <param name="mediaConstraint"></param>
		public void ClearBookFeature(string featureName, string orientationConstraint, string mediaConstraint)
		{
			Body.RemoveAttribute("data-bf" + featureName);
		}

		/// <summary>
		/// Returns true if the specified feature name matches the specified orientationConstraint, mediaConstraint pair
		/// </summary>
		/// <param name="featureName">The data-bf feature to check if its value matches</param>
		/// <param name="orientationConstraint">The orientation constraint to match</param>
		/// <param name="mediaConstraint">The media constraint to match</param>
		public bool BookHasFeature(string featureName, string orientationConstraint, string mediaConstraint)
		{
			var attr = Body.Attributes["data-bf" + featureName];
			if (attr == null)
				return false;
			// If we generalize this it should be something like contains, or else split at commas and then see if present.
			// But it may be more complicated than that...Does a book have a feature in landscape orientation
			// if it has it in all orientations, or not?
			return attr.Value == orientationConstraint + ";" + mediaConstraint;
		}

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

		// duplicates information in musicToolsControl.tsx
		public const string musicAttrName = "data-backgroundaudio";
		public const string musicVolumeName = musicAttrName + "volume";

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
			string[] classNamesToUnion = new string[] { "bloom-ui", "ui-resizable-handle" };
			var selectorsToUnion = classNamesToUnion.Select(className => $"//*[contains(concat(' ', @class, ' '), ' {className} ')]");
			var unionedXPathExpression = String.Join(" | ", selectorsToUnion);
			foreach (
				var node in
					edittedPageDiv.SafeSelectNodes(unionedXPathExpression).Cast<XmlNode>().ToArray())
				node.ParentNode.RemoveChild(node);
			RemoveTemplateEditingMarkup(edittedPageDiv);
			RemoveCkEditorMarkup(edittedPageDiv);

			destinationPageDiv.InnerXml = edittedPageDiv.InnerXml;

			//Enhance: maybe we should just copy over all attributes?
			destinationPageDiv.SetAttribute("class", edittedPageDiv.GetAttribute("class"));
			//The SIL LEAD SHRP templates rely on "lang" on some ancestor to trigger the correct rules in labels.css.
			//Those get set by putting data-metalanguage on Page, which then leads to a lang='xyz'. Let's save that
			//back to the html in keeping with our goal of having the page look right if you were to just open the
			//html file in Firefox.
			destinationPageDiv.SetAttribute("lang", edittedPageDiv.GetAttribute("lang"));

			// Copy the two background audio attributes which can be set using the music toolbox.
			// Ensuring that volume is missing unless the main attribute is non-empty is
			// currently redundant, everything should work if we just copied all attributes.
			// (But, it IS imporant to DELETE any old versions of these attributes if the edited page div
			// does NOT have them.)
			var music = edittedPageDiv.Attributes[musicAttrName]?.Value;
			var musicVolume = edittedPageDiv.Attributes[musicVolumeName]?.Value;
			if (music != null)
			{
				destinationPageDiv.SetAttribute(musicAttrName, music);
				if (musicVolume != null)
				{
					destinationPageDiv.SetAttribute(musicVolumeName, musicVolume);
				}
				else
				{
					destinationPageDiv.RemoveAttribute(musicVolumeName);
				}
			}
			else
			{
				destinationPageDiv.RemoveAttribute(musicAttrName);
				destinationPageDiv.RemoveAttribute(musicVolumeName);
			}

			var dataActivityName = "data-activity";

			// copy the data-activity attribute which may be set if the user adds an activity
			var dataActivity = edittedPageDiv.Attributes[dataActivityName]?.Value;
			if (dataActivity == null)
			{
				destinationPageDiv.RemoveAttribute(dataActivityName);
			}
			else
			{
				destinationPageDiv.SetAttribute(dataActivityName, dataActivity);
			}

			// Upon save, make sure we are not in layout mode.  Otherwise we show the sliders.
			foreach (
				var node in
					destinationPageDiv.SafeSelectNodes(".//*[contains(concat(' ', @class, ' '), ' origami-layout-mode ')]")
						.Cast<XmlNode>()
						.ToArray())
			{
				string currentValue = node.Attributes["class"].Value;
				node.Attributes["class"].Value = currentValue.Replace("origami-layout-mode", "");
			}

			// Remove any empty <a> elements left by editing.  These cause trouble when the book/page is reopened.
			// Also remove the extraneous data-cke-saved-href attribute gratuitously inserted.
			CleanupAnchorElements(destinationPageDiv);
		}

		/// <summary>
		/// Remove any empty &lt;a&gt; elements left by editing.  These cause trouble when the book/page is reopened.
		/// Also remove the gratuitous data-cke-saved-href attribute added by ckeditor.  (It may have been involved
		/// with the troublesome behavior noticed by the programmer.  It's certainly not needed.)
		/// </summary>
		public static void CleanupAnchorElements(XmlElement topElement)
		{
			foreach (var element in topElement.SafeSelectNodes(".//a").Cast<XmlElement>().ToArray())
			{
				if (element.InnerText == "")
					element.ParentNode.RemoveChild(element);
				else if (element.HasAttribute("data-cke-saved-href"))
					element.RemoveAttribute("data-cke-saved-href");
			}
		}

		internal static void RemoveCkEditorMarkup(XmlElement edittedPageDiv)
		{
			foreach (XmlElement elt in edittedPageDiv.SafeSelectNodes("//*[contains(@class, 'cke_')]"))
			{
				elt.SetAttribute("class",
					string.Join(" ",
						elt.GetAttribute("class")
							.Split(' ')
							.Where(c => !c.StartsWith("cke_"))
					));
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
				.Where(lang => !String.IsNullOrEmpty(lang) && (lang != "*" && lang != "z"))
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
			SetElementFromUserStringSafely(node, form);
			dataDiv.AppendChild(node);
		}

		public static void SetElementFromUserStringSafely(XmlElement node, string form)
		{
			//Note: this method is a compromise... it replaces a couple instances where we were
			//explicitly using innerText instead of innerXml, presumably on purpose. Of course that
			//makes it impossible to have any html markup. My particular need right now (BL-3832) is to
			//allow <br> to get through this filter. And later (BL-8221) to allow <cite> . A future alternative
			//might be to remove the filter altogether and see if there's a better way to handle
			//whatever scenarios the filtering was designed to prevent.

			// not InnerXml as it may contain things like SILA & LASI that are not valid XML
			const string kBR = "LINEBREAKHERE";
			var withBreaksHidden = form.Replace("<br />", kBR).Replace("<br/>", kBR);

			var match = new Regex(".*(<cite[^>]*>).*</cite>.*").Match(withBreaksHidden);
			const string kCiteBegin = "BEGINCITEHERE";
			const string kCiteEnd = "ENDCITEHERE";
			if (match.Success)
			{
				withBreaksHidden = withBreaksHidden.Replace(match.Groups[1].Value, kCiteBegin)
					.Replace("</cite>", kCiteEnd);
			}

			//going to innertext means we treat everything literally, for better or worse (definitely safer)
			node.InnerText = withBreaksHidden;
			// finally, restore the breaks and <cite> markup
			var safeText = node.InnerXml; // anything XML-ish has been escaped
			if (match.Success)
			{
				safeText = safeText.Replace(kCiteEnd, "</cite>").Replace(kCiteBegin, match.Groups[1].Value);
			}
			safeText = safeText.Replace(kBR, "<br/>");
			node.InnerXml = safeText;
		}

		internal static void StripUnwantedTagsPreservingText(XmlDocument dom, XmlNode element, string[] tagsToPreserve)
		{
			if (element.HasChildNodes)
			{
				var countOfChildren = element.ChildNodes.Count;
				for (var i = 0; i < countOfChildren; i++)
				{
					var childNode = element.ChildNodes[i];
					if (childNode is XmlText)
						continue;

					StripUnwantedTagsPreservingText(dom, childNode, tagsToPreserve);
				}
			}
			if (tagsToPreserve.Contains(element.Name))
				return;
			var replacementNode = dom.CreateTextNode(element.InnerText);
			element.ParentNode.ReplaceChild(replacementNode, element);
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
		/// <param name="cssContent">Content of either a CSS file or an HTML file</param>
		/// <param name="result">set of fonts found in the CSS markup</param>
		/// <param name="includeFallbackFonts">true to include fallback fonts, false to include only the first font in each font family</param>
		public static void FindFontsUsedInCss(string cssContent, HashSet<string> result, bool includeFallbackFonts)
		{
			// The actual content may be an HTML file instead of a CSS file.  HTML can contain embedded CSS
			// in either style elements or style attributes.
			if (cssContent.Contains("<html>") && cssContent.Contains("</html>"))
			{
				FindFontsUsedInEmbeddedCss(cssContent, result, includeFallbackFonts);
				return;
			}
			cssContent = RemoveCommentsFromCss(cssContent);
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

		private static void FindFontsUsedInEmbeddedCss(string htmlContent, HashSet<string> result, bool includeFallbackFonts)
		{
			// Remove any HTML comments from the HTML string.
			for (var idx = htmlContent.IndexOf("<!--"); idx >= 0; idx = htmlContent.IndexOf("<!--", idx))
			{
				var endIdx = htmlContent.IndexOf("-->", idx + 4);
				if (endIdx > idx)
					htmlContent = htmlContent.Remove(idx, endIdx + 3 - idx);
			}
			// Capturing the content of a <style> element is too hard for Regex.  But we can capture
			// the start tag okay, and work from there.
			var styleElements = new Regex("<style[^>]*type=[\"']text/css[\"'][^>]*>");
			foreach (Match match in styleElements.Matches(htmlContent))
			{
				var idxStart = match.Index + match.Length;
				var idxEnd = htmlContent.IndexOf("</style>", idxStart);
				if (idxEnd > idxStart)
				{
					var cssContent = htmlContent.Substring(idxStart, idxEnd - idxStart);
					Console.WriteLine("DEBUG cssContent from HTML <style> = \"{0}\"", cssContent);
					FindFontsUsedInCss(cssContent, result, includeFallbackFonts);
				}
			}
			var styleAttributes1 = new Regex(" style=\"([^\"]*)\"");
			foreach (Match match in styleAttributes1.Matches(htmlContent))
			{
				var cssContent = HttpUtility.HtmlDecode(match.Groups[1].Value);
				FindFontsUsedInCss(cssContent, result, includeFallbackFonts);
			}
			var styleAttributes2 = new Regex(" style='([^']*)'");
			foreach (Match match in styleAttributes2.Matches(htmlContent))
			{
				var cssContent = HttpUtility.HtmlDecode(match.Groups[1].Value);
				FindFontsUsedInCss(cssContent, result, includeFallbackFonts);
			}
		}

		/// <summary>
		/// Find the ranges of either //  and /*...*/ comments in the css data, taking into account
		/// quoted strings along the way.  Then remove each comment from the string and return the
		/// string without any comments in it.  This is still a bit naive compared to a full css parser,
		/// but good enough for what we need.
		/// </summary>
		public static string RemoveCommentsFromCss(string cssContent)
		{
			for (var idxStart = FindCommentStartOutsideQuotes(cssContent, 0);
				idxStart >= 0;
				idxStart = FindCommentStartOutsideQuotes(cssContent, idxStart))
			{
				int idxEnd = 0;
				if (cssContent[idxStart + 1] == '*')
				{
					idxEnd = cssContent.IndexOf("*/", idxStart + 2);
					if (idxEnd < 0)
						idxEnd = cssContent.Length;
					else
						idxEnd += 2;
					}
				else
				{
					idxEnd = cssContent.IndexOf("\n", idxStart + 2);
					if (idxEnd < 0)
						idxEnd = cssContent.Length;
				}
				cssContent = cssContent.Remove(idxStart, idxEnd - idxStart);
			}
			return cssContent;
		}

		private static int FindCommentStartOutsideQuotes(string content, int start)
		{
			char[] quotes = { '"', '\'' };
			var idxComment = content.IndexOf("//", start);
			var idxComment2 = content.IndexOf("/*", start);
			if (idxComment2 >= 0 && (idxComment2 < idxComment || idxComment < 0))
				idxComment = idxComment2;
			if (idxComment < 0)
				return idxComment;	// no comment found, inside or outside any quotes
			var idxQuote = content.IndexOfAny(quotes, start);
			if (idxQuote < 0 || idxQuote > idxComment)
				return idxComment;
			var endQuote = content.IndexOf(content[idxQuote], idxQuote + 1);
			if (endQuote < 0)
				return idxComment;   // ignore unterminated quote
			return FindCommentStartOutsideQuotes(content, endQuote + 1);
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
		/// Gets the url for a video, starting from a parent div which may or may not contain a video element.
		/// </summary>
		public static UrlPathString GetVideoElementUrl(XmlElement videoContainer)
		{
			return GetVideoElementUrl(new ElementProxy(videoContainer));
		}

		/// <summary>
		/// Gets the url for a video, starting from a parent div which may or may not contain a video element.
		/// </summary>
		public static UrlPathString GetVideoElementUrl(GeckoHtmlElement videoContainer)
		{
			return GetVideoElementUrl(new ElementProxy(videoContainer));
		}

		/// <summary>
		/// Gets the url for a video, starting from a parent div which may or may not contain a video element.
		/// </summary>
		public static UrlPathString GetVideoElementUrl(ElementProxy videoContainer)
		{
			var videoElt = videoContainer.GetChildWithName("video");
			// we choose to return an empty path for failure instead of null to reduce errors created by things like
			// HtmlDom.GetImageElementUrl(element).UrlEncoded.
			if (videoElt == null)
				return UrlPathString.CreateFromUnencodedString(string.Empty);
			var srcElt = videoElt.GetChildWithName("source");
			if (srcElt == null)
				return UrlPathString.CreateFromUnencodedString(string.Empty);

			var src = srcElt.GetAttribute("src");
			string dummy;
			var fileName = SignLanguageApi.StripTimingFromVideoUrl(src, out dummy);
			return UrlPathString.CreateFromUrlEncodedString(fileName);
		}

		/// <summary>
		/// Gets the url for the audio, either from an audio-sentence class or any other element that has
		/// an inline style with data-backgroundaudio set.
		/// </summary>
		public static UrlPathString GetAudioElementUrl(GeckoHtmlElement audioElement)
		{
			return GetAudioElementUrl(new ElementProxy(audioElement));
		}

		/// <summary>
		/// Gets the url for the audio, either from an audio-sentence class or any other element that has
		/// an inline style with data-backgroundaudio set.
		/// </summary>
		public static UrlPathString GetAudioElementUrl(XmlElement audioElement)
		{
			return GetAudioElementUrl(new ElementProxy(audioElement));
		}

		/// <summary>
		/// Gets the url for the audio, either from an audio-sentence class or any other element that has
		/// an inline style with data-backgroundaudio set.
		/// </summary>
		public static UrlPathString GetAudioElementUrl(ElementProxy audioOrDivWithBackgroundMusic)
		{
			var classStr = audioOrDivWithBackgroundMusic.GetAttribute("class");
			if (classStr.Contains("audio-sentence"))
			{
				var id = audioOrDivWithBackgroundMusic.GetAttribute("id");
				return UrlPathString.CreateFromUrlEncodedString(id);
			}

			var backgroundAudioFileName = audioOrDivWithBackgroundMusic.GetAttribute("data-backgroundaudio") ?? String.Empty;
			if (backgroundAudioFileName != String.Empty)
			{
				return UrlPathString.CreateFromUrlEncodedString(backgroundAudioFileName);
			}

			//we choose to return this instead of null to reduce errors created by things like
			// HtmlDom.GetAudioElementUrl(element).UrlEncoded. If we just returned null, that has to be written
			// as something that checks for null, like:
			//  var url = HtmlDom.GetAudioElementUrl(element). if(url!=null) url.UrlEncoded
			return UrlPathString.CreateFromUnencodedString(String.Empty);
		}

		/// <summary>
		/// Returns true if the element name could potentially contain a valid attribute called "audio-sentence" (used by Talking Books).
		/// </summary>
		/// <param name="elementName">HTML element name such as "span"</param>
		public static bool DoesElementAllowAudioSentence(string elementName)
		{
			return elementName == "span" || elementName == "div";
		}
		/// <summary>
		/// Sets the url attribute either of an img (the src attribute)
		/// or a div with an inline style with an background-image rule
		/// </summary>
		public static void SetImageElementUrl(ElementProxy imgOrDivWithBackgroundImage, UrlPathString url, bool urlEncode = true)
		{
			if(imgOrDivWithBackgroundImage.Name.ToLower() == "img")
			{
				// This does not need to be encoded until sent over the network.
				// Indeed, encoding it breaks links within epubs.
				imgOrDivWithBackgroundImage.SetAttribute("src", urlEncode ? url.UrlEncoded : url.NotEncoded);
			}
			else
			{
				// The string formatting must match BookCompressor.kBackgroundImage
				imgOrDivWithBackgroundImage.SetAttribute("style", String.Format("background-image:url('{0}')", urlEncode ? url.UrlEncoded : url.NotEncoded));
			}
		}

		/// <summary>
		/// Set the video that will play for this container.
		/// Includes creating the video and source elements if they don't already exist.
		/// Does not yet handle setting to an empty url and restoring the bloom-noVideoSelected state.
		/// </summary>
		public static void SetVideoElementUrl(ElementProxy videoContainer, UrlPathString url, bool urlEncode = true)
		{
			var videoElt = videoContainer.GetChildWithName("video");
			if (videoElt == null)
			{
				videoElt = videoContainer.AppendChild("video");
			}

			var srcElement = videoElt.GetChildWithName("source");
			if (srcElement == null)
			{
				srcElement = videoElt.AppendChild("source");
				srcElement.SetAttribute("type", "video/mp4");
			}
			SetSrcOfVideoElement(url, srcElement, urlEncode);
			// Hides the placeholder.

			videoContainer.SetAttribute("class",
				RemoveClass("bloom-noVideoSelected",
					videoContainer.GetAttribute("class")));
		}

		/// <summary>
		/// Set the src attribute of the element to the path indicated by the url argument, optionally
		/// adding the supplied params. If urlEncodePath is true, we will take the encoded path version
		/// of the url argument. Caller is responsible to encode the paramString if necessary.
		/// </summary>
		public static void SetSrcOfVideoElement(UrlPathString url, ElementProxy srcElement, bool urlEncodePath, string encodedParamString = "")
		{
			if (encodedParamString == null)
				encodedParamString = "";
			if (!string.IsNullOrEmpty(encodedParamString) && !(encodedParamString.StartsWith("?")))
				encodedParamString = "?" + encodedParamString;
			srcElement.SetAttribute("src",
				(urlEncodePath ? url.PathOnly.UrlEncodedForHttpPath : url.PathOnly.NotEncoded) + encodedParamString);
		}

		public static string RemoveClass(string className, string input)
		{
			// We can remove the class name with a following space at the start,
			// the class name with a preceding space and following word boundary anywhere,
			// or just the class name itself if that's all there is.
			return new Regex("(^" + className + " | " + className +"\\b|^" + className + "$)").Replace(input, "");
		}

		public static void RemoveClass(XmlElement element, string classNameToRemove)
		{
			string classAttributeValue = element?.Attributes["class"]?.Value;
			if (classAttributeValue == null)
			{
				return;
			}

			var classes = classAttributeValue.Split(HtmlDom.kHtmlClassDelimiters, StringSplitOptions.RemoveEmptyEntries).ToList();
			classes.Remove(classNameToRemove);
			string newClassAttributeValue = String.Join(" ", classes);
			element.SetAttribute("class", newClassAttributeValue);
		}

		/// <summary>
		/// Move the content of the element to its parent, then remove the now-empty element.
		/// </summary>
		public static void RemoveElementLayer(XmlElement element)
		{
			foreach (var node in element.ChildNodes.Cast<XmlNode>().ToList())
			{
				element.RemoveChild(node);
				element.ParentNode.InsertBefore(node, element);
			}
			element.ParentNode.RemoveChild(element);
		}

		public static XmlNodeList SelectChildImgAndBackgroundImageElements(XmlElement element)
		{
			return element.SelectNodes(".//img | .//*[contains(@style,'background-image')]");
		}

		public static XmlNodeList SelectChildNarrationAudioElements(XmlElement element, bool includeSplitTextBoxAudio)
		{
			string xPath = "descendant-or-self::node()[contains(concat(' ', @class, ' '), ' audio-sentence ')]";
			if (includeSplitTextBoxAudio)
			{
				// This will select bloom-editables (i.e. text boxes) recorded in TextBox mode which might not be audio-sentences, but do contain descendants which are.
				// (That is, find cases where RecordingMode=TextBox but PlaybackMode=Sentence, a.k.a. the result of a Hard Split.
				xPath += " | descendant-or-self::node()[contains(concat(' ', @class, ' '), ' audio-sentence ')]/ancestor::div[contains(concat(' ', @class, ' '), ' bloom-editable ') and @data-audiorecordingmode='TextBox']";
			}
			return element.SelectNodes(xPath);
		}

		public static XmlNodeList SelectChildBackgroundMusicElements(XmlElement element)
		{
			return element.SelectNodes(".//div[@data-backgroundaudio and string-length(@data-backgroundaudio)!=0]");
		}

		public static XmlNodeList SelectChildVideoElements(XmlElement element)
		{
			return element.SelectNodes(".//div[contains(@class,'bloom-videoContainer')]");
		}

		public static XmlNodeList SelectChildVideoSourceElements(XmlElement element)
		{
			return element.SafeSelectNodes(".//div[contains(@class,'bloom-videoContainer')]/video/source");
		}

		private static readonly string kAudioSentenceElementsXPath = "descendant-or-self::node()[contains(@class,'audio-sentence') and string-length(@id) > 0]";
		
		public static XmlNodeList SelectRecordableDivOrSpans(XmlElement element)
		{
			string xpath1 = kAudioSentenceElementsXPath;
			// We only want the ones with ids, so add check for string-length(@id)
			string xpath2 = "descendant-or-self::node()[contains(@class,'bloom-editable') and not(contains(@class,'bloom-noAudio'))  and string-length(@id) > 0 ]";
			// Also add on any other spans. (e.g., bloom-highlight segments, which won't be matched by xpath1.
			string xpath3 = "descendant-or-self::node()[contains(@class,'bloom-editable') and not(contains(@class,'bloom-noAudio'))]//span[string-length(@id) > 0]";
			return element.SafeSelectNodes($"{xpath1}|{xpath2}|{xpath3}");
		}

		public static XmlNodeList SelectAudioSentenceElements(XmlElement element)
		{
			// It's unexpected for a book to have nodes with class audio-sentence and no id to link them to a file, but
			// if they do occur, it's better to ignore them than for other code to crash when looking for the ID.
			return element.SafeSelectNodes(kAudioSentenceElementsXPath);
		}

		public static XmlNodeList SelectAudioSentenceElementsWithDataDuration(XmlElement element)
		{
			return element.SafeSelectNodes("descendant-or-self::node()[contains(@class,'audio-sentence') and @data-duration]");
		}

		public static XmlNodeList SelectAudioSentenceElementsWithRecordingMd5(XmlElement element)
		{
			return element.SafeSelectNodes("descendant-or-self::node()[@recordingmd5 and (contains(@class,'audio-sentence') or contains(@class,'bloom-highlightSegment'))]");
		}

		public static bool HasAudioSentenceElementsWithoutId(XmlElement element)
		{
			// It's unexpected for a book to have nodes with class audio-sentence and no id to link them to a file, but
			// if they do occur, it's better to ignore them than for other code to crash when looking for the ID.
			var nodes = element.SafeSelectNodes("descendant-or-self::node()[contains(@class,'audio-sentence') and string-length(@id) = 0]");
			return nodes?.Count >= 1;
		}

		public static bool HasMotionFeature(XmlElement element)
		{
			var nodes = element.SafeSelectNodes("//*[@data-initialrect]");
			return nodes?.Count >= 1;
		}

		public static bool HasQuizFeature(XmlElement element)
		{
			// Current style comprehension quiz pages
			var nodes1 = element.SafeSelectNodes("//*[contains(@class, 'simple-comprehension-quiz')]");
			// Legacy style comprehension quiz pages
			var nodes2 = element.SafeSelectNodes("//*[contains(@class, 'questions')]");
			return nodes1?.Count >= 1 || nodes2?.Count >= 1;
		}

		public static bool HasComicFeature(XmlElement element)
		{
			return element.SelectSingleNode(BookStorage.ComicalXpath) != null;
		}

		public XmlNodeList SelectVideoElements()
		{
			return RawDom.SafeSelectNodes("//div[contains(@class, 'bloom-videoContainer')]//source");
		}

		/// <summary>
		/// Determines which languages contain at least one meaningful image description
		/// Image descriptions in XMatter don't count
		/// </summary>
		/// <returns>Returns a distinct list of the language codes that do</returns>
		public IEnumerable<string> GetLangCodesWithImageDescription()
		{
			var pageElements = GetContentPageElements();

			// Search the bloom-page for which elements are the language divs (under image descriptions),
			// then flattern the list of lists into a single list.
			var langDivs = pageElements.SelectMany(page => page.SafeSelectNodes(".//*[contains(@class, 'bloom-imageDescription')]/div[@lang]").Cast<XmlElement>()).ToList();

			var langCodes = langDivs.Where(node => !String.IsNullOrWhiteSpace(node.InnerText))  // Note that it is common for InnerText to contain whitespace like "\r\n"
				.Select(node => node.GetAttribute("lang"))
				.Where(IsLanguageValid)
				.Distinct();

			return langCodes;
		}

		/// <summary>
		/// Finds the lanaguage code which is closest to the startElement
		/// The search begins at (and includes) the startElement and continues up through its ancestors
		/// </summary>
		/// <param name="startElement">The element to start at</param>
		/// <returns>The first lang code found on a "lang" attribute, or "" if none found.</returns>
		internal static string GetClosestLangCode(XmlElement startElement)
		{
			XmlElement currentElement = startElement;
			while (currentElement != null)
			{
				if (currentElement.HasAttribute("lang"))
				{
					return currentElement.GetAttribute("lang");
				}

				currentElement = currentElement.ParentNode as XmlElement;
			}

			return "";
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

		public IEnumerable<XmlElement> GetContentPageElements()
		{
			return _dom.SafeSelectNodes(
					"/html/body/div[contains(@class,'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]")
				.OfType<XmlElement>();
		}

		public IEnumerable<XmlElement> GetPageElements()
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
				// Don't set data-page-number for xmatter (or other unnumbered) pages.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7303.
				var numberInScript = (number > 0 && HtmlDom.HasClass(pageDiv, "numberedPage")) ? GetNumberStringRepresentation(number, charactersForDigits) : "";
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

			// normal charactersForDigits.length gives 20 for chakma's 10 characters... I gather because it is converted to utf 16  and then
			// those bytes are counted? Here's all the info:
			// "In short, the length of a string is actually a ridiculously complex question and calculating it can take a lot of CPU time as well as data tables."
			// https://stackoverflow.com/questions/26975736/why-is-the-length-of-this-string-longer-than-the-number-of-characters-in-it
			var infoOnDigitsCharacters = new StringInfo(charactersForDigits);
			Debug.Assert(infoOnDigitsCharacters.LengthInTextElements == 10);

			return String.Join("", postiveInteger.ToString(CultureInfo.InvariantCulture)
				.Select(x =>
				{
					if ("1234567890".Contains(x.ToString()))
						return infoOnDigitsCharacters.SubstringByTextElements(x - '0', 1);
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

		/// <summary>
		/// Sets an attribute that css can use, kind of like media queries; those we can't actually control or set
		/// to things like "bloomreader", so we basically do our own on the body element. Envisionsed choices include
		/// print, bloomReader, epub, video.
		/// </summary>
		/// <param name="media"></param>
		public void SetMedia(string media)
		{
			Body.SetAttribute("data-media", media);
		}

		public void SetImageAltAttrsFromDescriptions(string descriptionLang)
		{
			SetImageAltAttrsFromDescriptions(RawDom.DocumentElement, descriptionLang);
		}

		public static void SetImageAltAttrsFromDescriptions(XmlElement root, string descriptionLang)
		{
			foreach (XmlElement img in root.SelectNodes("//img"))
			{
				SetImageAltAttrFromDescription(img, descriptionLang);
			}
		}

		public static string GetNumberOrLabelOfPageWhereElementLives(XmlElement childElement, IEnumerable<string> langs = null)
		{
			var pageElement = childElement.SelectSingleNode("ancestor-or-self::div[contains(@class,'bloom-page')]") as XmlElement;
			if (pageElement == null)
			{
#if DEBUG
				throw new ApplicationException("Don't feed non-page images to this method!");
#endif
				return "unknown";
			}
			// optional because unit tests might be missing data-page-number
			var pageNumber = pageElement.GetOptionalStringAttribute("data-page-number","unknown");
			if (
				// front matter won't have a pageNumber
				string.IsNullOrWhiteSpace(pageNumber)

			    // back matter pages actually have page numbers (maybe that's an oversight in some other code?)
			    // but it's clearer to just call them by name.
			    || HtmlDom.IsBackMatterPage(pageElement))
			{
				var labelNode = pageElement.SelectSingleNode("./div[@class='pageLabel']");
				return LocalizePageLabel(labelNode?.InnerText.Trim(), langs);
			}
			return pageNumber;
		}

		private static string LocalizePageLabel(string label, IEnumerable<string> langs)
		{
			if (langs == null || string.IsNullOrEmpty(label))
				return label;
			var id = "TemplateBooks.PageLabel." + label;
			return L10NSharp.LocalizationManager.GetString(id, label, "", langs, out _);
		}

		public static bool IsBackMatterPage(XmlElement pageElement)
		{
			return HtmlDom.HasClass(pageElement, "bloom-backMatter");
		}

		// Make the image's alt attr match the image description for the specified language.
		// If we don't have one, make the alt attr exactly an empty string (except branding images may be allowed to have custom alt text).
		private static void SetImageAltAttrFromDescription(XmlElement img, string descriptionLang)
		{
			var parent = img.ParentNode as XmlElement;
			if (HasClass(parent, "bloom-imageContainer"))
			{
				var description = parent.ChildNodes.Cast<XmlNode>()
					.FirstOrDefault(n => n is XmlElement && HasClass((XmlElement) n, "bloom-imageDescription"));
				if (description != null)
				{
					foreach (var node in description.ChildNodes)
					{
						var editable = node as XmlElement;
						if (editable == null)
							continue;
						if (!HasClass(editable, "bloom-editable"))
							continue;
						if (editable.Attributes["lang"] == null || editable.Attributes["lang"].Value != descriptionLang)
							continue;
						// We found the relevant description. (Even if it's empty, we won't find a better one,
						// so stop here anyway.)
						// Enhance: we may want to do something about over-long descriptions. Epub accessibility
						// guidelines recommend alt should not be more than about 200 characters. There is
						// a mechanism in WCAG 2.0 for providing a 'long description' by means of a link in
						// a longdesc attr (does anything implement this?) or a separate link element whose 'alt'
						// indicates that it is a link to a long description. Not sure how that could work in an
						// epub, either.
						img.SetAttribute("alt", editable.InnerText.Trim());
						return;
					}
				}
			}

			if (!EpubMaker.IsBranding(img) || IsPlaceholderImageAltText(img))
			{
				// Images in accessible epubs should have explicit empty alt attr if no useful description
				img.SetAttribute("alt", "");
			}
		}

		/// <summary>
		/// Check if the alt text looks like Bloom Editor placeholder alt text (which we don't want in the published version)
		/// Looks like: "The picture, {0}, is missing or was loading too slowly"
		/// </summary>
		/// <param name="altText"></param>
		/// <returns>True if it appears to be some sort of placeholder alt text, false otherwise</returns>
		public static bool IsPlaceholderImageAltText(XmlElement imageElement)
		{
			// Note: It's (currently) better for this to be more aggressive about returning true, based on how the callers use it.
			//       False positives don't really hurt, but false negatives are not ideal.

			string altText = imageElement.GetAttribute("alt");
			if (String.IsNullOrEmpty(altText))
			{
				return true;
			}

			// Check some partial match on English strings
			if (altText.Contains(" is missing or was loading too slowly")) // Text from bloomImages.ts::SetAlternateTextOnImages(), need to update both
			{
				return true;
			}

			// Check for an exact match on localized string.
			string localizedFormatString = L10NSharp.LocalizationManager.GetString("EditTab.Image.AltMsg", "This picture, {0}, is missing or was loading too slowly.");
			string localizedString = String.Format(localizedFormatString, imageElement.GetAttribute("src"));

			if (altText.Equals(localizedString, StringComparison.CurrentCultureIgnoreCase))
			{
				return true;
			}

			// Test for Partial match based on localized string
			int indexOfFormatReplacement = localizedFormatString.IndexOf("{0}");
			if (indexOfFormatReplacement >= 0)
			{
				string localizedSubstring;
				// There is a replacement somewhere in the string (well, supposed to be, but can't guarantee...)
				// Split the string in half around the location of the replacement
				// Then pick the longer half so that we have more text to match
				if (indexOfFormatReplacement > localizedFormatString.Length / 2)
				{
					localizedSubstring = localizedFormatString.Substring(0, indexOfFormatReplacement);
				}
				else
				{
					localizedSubstring = localizedFormatString.Substring(indexOfFormatReplacement + "{0}".Length);
				}

				return altText.Contains(localizedSubstring, StringComparison.CurrentCultureIgnoreCase);
			}

			return false;
		}

		/// <summary>
		/// Intended for Export to Word/Libre Office only. Sets an inline style to a given value, adding to what
		/// might already be there.
		/// </summary>
		/// <param name="element"></param>
		/// <param name="styleToSet"></param>
		public static void SetInlineStyle(XmlElement element, string styleToSet)
		{
			var styleString = GetAttributeValue(element, "style");
			if (!string.IsNullOrWhiteSpace(styleString))
				styleString += " ";
			styleString += styleToSet;
			element.SetAttribute("style", styleString);
		}



		/// <summary>
		/// Reorder any div elements that need to be reordered for proper use in publications.
		/// The different-language children of a translation group are ordered in Bloom's displays by flex-box CSS
		/// that puts the div with class bloom-content1 before the one with bloom-content2 etc.  Since we don't
		/// (and can't) rely on flex-box in epubs, we need to actually put the elements in the right order.
		/// In addition, the published audio playback on all platforms currently relies on the html being in the correct order.
		/// </summary>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6299
		/// and
		/// https://issues.bloomlibrary.org/youtrack/issue/BL-7300
		/// </remarks>
		public void FixDivOrdering()
		{
			FixDivOrdering(RawDom.DocumentElement.SelectNodes("//div[contains(@class, 'translationGroup')]").Cast<XmlElement>());
		}

		private static void FixDivOrdering(IEnumerable<XmlElement> nodeList)
		{
			foreach (var multilingualDiv in nodeList)
			{
				var divs = multilingualDiv.SelectNodes("./div[contains(@class, 'bloom-content')]").Cast<XmlElement>().ToList();
				divs.Sort(CompareMultilingualDivs);
				for (var i = divs.Count - 1; i >= 1; --i)
					multilingualDiv.InsertBefore(divs[i - 1], divs[i]);
			}
		}

		private static int CompareMultilingualDivs(XmlElement x, XmlElement y)
		{
			string xKey = ExtractKeyForMultilingualDivs(x);
			string yKey = ExtractKeyForMultilingualDivs(y);
			return string.Compare(xKey, yKey, StringComparison.Ordinal);
		}

		private static string ExtractKeyForMultilingualDivs(XmlElement x)
		{
			// bloom-content[23] do not seem to be reliable.  "1", "National1", and "National2" sort correctly.
			// But I think we do want the newer markup to be reliable, so I'm leaving this line commented out.
			//var xClass = x.GetAttribute("class").Replace("bloom-contentNational", "");
			var xClass = x.GetAttribute("class");
			var idx = xClass.IndexOf("bloom-content", StringComparison.Ordinal);
			Debug.Assert(idx >= 0);
			return xClass.Substring(idx);
		}

		public static bool IsNodePartOfDataBookOrDataCollection(XmlNode node)
		{
			bool isMatch = DoesSelfOrAncestorMatchCondition(node, n => {
				if (n.Attributes == null)
				{
					return false;
				}
				else if (n.GetOptionalStringAttribute("data-book", null) != null)
				{
					return true;
				}
				else if (n.GetOptionalStringAttribute("data-collection", null) != null)
				{
					return true;
				}

				return false;
			});

			return isMatch;
		}

		public static bool DoesSelfOrAncestorMatchCondition(XmlNode node, Func<XmlNode, bool> matcher)
		{
			if (node == null)
			{
				return false;
			}

			var ancestor = node;

			while (ancestor != null)
			{
				if (matcher(ancestor))
				{
					return true;
				}
								
				ancestor = ancestor.ParentNode;
			}

			return false;			
		}

		public static void InsertFullBleedMarkup(XmlElement body)
		{
			AddClassIfMissing(body, "bloom-fullBleed");
			foreach (XmlElement page in body.SafeSelectNodes("//div[contains(@class, 'bloom-page')]").Cast<XmlElement>().ToArray())
			{
				var mediaBoxDiv = page.OwnerDocument.CreateElement("div");
				(page.ParentNode as XmlElement).ReplaceChild(mediaBoxDiv, page);
				mediaBoxDiv.AppendChild(page);
				// In an ideal world, the page size class would be on the body, and style rules for
				// the media box could simply use it. As it is, the page size class is on the page,
				// and to write style rules that use it, we need to copy it to the new container.
				var pageSizeClass = page.Attributes["class"].Value.Split(' ')
					.First(x => x.EndsWith("Portrait") || x.EndsWith("Landscape"));
				mediaBoxDiv.SetAttribute("class", $"bloom-mediaBox {pageSizeClass}");
			}
		}
	}
}
