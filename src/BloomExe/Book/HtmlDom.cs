using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Xml;
using System.Xml.Xsl;
using Amazon;
using Bloom.Api;
using Bloom.Publish; // for DynamicJson
using Bloom.Publish.Epub;
using Bloom.ToPalaso;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.VisualBasic;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Text;
using SIL.Xml;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

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
        private static readonly Regex s_regexBangImportant = new Regex(
            "\\s*!\\s*important\\s*",
            RegexOptions.Compiled
        );
        private XmlDocument _dom;

        public HtmlDom()
        {
            _dom = new XmlDocument();
            _dom.LoadXml("<html><head></head><body></body></html>");
        }

        public HtmlDom(XmlDocument domToClone)
        {
            _dom = (XmlDocument)domToClone.Clone();
        }

        /// <summary>
        /// Make a DOM out of the input
        /// </summary>
        /// <param name="xhtml"></param>
        /// <param name="preserveWhiteSpace">True to set PreserveWhiteSpace. My intuition is that this
        /// should always be true, as otherwise, it seems we can lose white space between spans in
        /// paragraphs. Moreover, our main XmlHtmlConverter.GetXmlDomFromHtml() sets it.
        /// But several of our unit tests fail if we set it here. So for now, just
        /// set it where we know we do need it.</param>
        public HtmlDom(string xhtml, bool preserveWhiteSpace = false)
        {
            _dom = new XmlDocument();
            if (preserveWhiteSpace)
                _dom.PreserveWhitespace = true;
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
            get { return XmlUtils.GetTitleOfHtml(_dom, null); }
            set
            {
                var t = value.Trim();
                var titleNode = XmlUtils.GetOrCreateElement(_dom, "html/head", "title");
                if (titleNode == null && t.Length == 0)
                    return; // must be in a unit test.
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
            foreach (XmlElement node in _dom.SafeSelectNodes("/html/body/div"))
            {
                //in the beta, 0.8, the ID of the page in the front-matter template was used for the 1st
                //page of every book. This screws up thumbnail caching.
                const string guidMistakenlyUsedForEveryCoverPage =
                    "74731b2d-18b0-420f-ac96-6de20f659810";
                if (
                    String.IsNullOrEmpty(node.GetAttribute("id"))
                    || (node.GetAttribute("id") == guidMistakenlyUsedForEveryCoverPage)
                )
                    node.SetAttribute("id", Guid.NewGuid().ToString());
            }
        }

        public static string ReplaceAllIdValues(string xmlContent)
        {
            if (String.IsNullOrEmpty(xmlContent))
                return xmlContent;
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml("<div>" + xmlContent + "</div>"); // may be multiple paragraphs
                var nodes = doc.FirstChild
                    .SafeSelectNodes("(.//div|.//span)[@id]")
                    .Cast<XmlElement>()
                    .ToList();
                foreach (var node in nodes)
                {
                    // Change the id since every element must have a different id value.
                    HtmlDom.SetNewHtmlIdValue(node);
                }
                //Console.WriteLine("DEBUG ReplaceAllIdValues(\"{0}\") => \"{1}\"", xmlContent, doc.FirstChild.InnerXml);
                xmlContent = doc.FirstChild.InnerXml; // exclude the outer div we introduced
            }
            catch (Exception e)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);

                // Ignore any errors: maybe it's not really XML after all?
            }
            return xmlContent;
        }

        public static string SetNewHtmlIdValue(XmlElement element)
        {
            // HTML ids must start with a letter.  This is true of audio ids in Bloom, and possibly
            // other id attribute values.  Page id values do not have this requirement.
            var newId = Guid.NewGuid().ToString();
            if (Char.IsDigit(newId[0]))
                newId = "i" + newId;
            element.SetAttribute("id", newId);
            return newId;
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
            foreach (
                XmlElement node in _dom.SafeSelectNodes(
                    "/html/body/div[contains(concat(' ', normalize-space(@class), ' '),' bloom-page ') and contains(concat(' ', normalize-space(@class), ' '),' customPage ')and @data-page='']"
                )
            )
            {
                node.SetAttribute("data-page", "extra");
                foreach (XmlElement label in GetAllDivsWithClass(node, "pageLabel"))
                {
                    label.RemoveAttribute("data-i18n");
                    break;
                }
                foreach (XmlElement description in GetAllDivsWithClass(node, "pageDescription"))
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
        /// This method is designed to be used in conjunction with BloomServer.MakeInMemoryHtmlFileInBookFolder().
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
                if (head == null)
                    return;
                foreach (XmlNode baseNode in head.SafeSelectNodes("base"))
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
            // This version is in libpalaso, and it does weird things that are file:/// oriented, like looking for the file and giving it file path
            // RawDom.AddStyleSheet(path);

            // We don't need any link to be there twice. If we're already linked to this stylesheet, don't add it again.
            // Review: the libpalaso code instead removes the old link before adding the new one.
            // The code here is more efficient, but it means that if the stylesheet is in the wrong order, or even if the old
            // link looked for it in the wrong folder, we don't fix it.
            // Generally we sort the links after adding all of them, so order doesn't matter.
            // I'm fairly sure we're not depending on this code to fix links to moved stylesheets.
            // Most of our stylesheets live directly in the book folder.
            foreach (XmlElement linkNode in RawDom.SafeSelectNodes("/html/head/link"))
            {
                var href = linkNode.GetAttribute("href");
                if (Path.GetFileName(href) == Path.GetFileName(path))
                {
                    return;
                }
            }

            var head = XmlUtils.GetOrCreateElement(RawDom, "//html", "head");
            var link = RawDom.CreateElement("link");
            link.SetAttribute("rel", "stylesheet");
            link.SetAttribute("href", path);
            link.SetAttribute("type", "text/css");
            head.AppendChild(link);
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

        public void RemoveModeStyleSheets()
        {
            foreach (XmlElement linkNode in RawDom.SafeSelectNodes("/html/head/link"))
            {
                var href = linkNode.GetAttribute("href");
                if (String.IsNullOrEmpty(href))
                {
                    continue;
                }

                var fileName = Path.GetFileName(href);
                if (fileName.Contains("edit") || fileName.Contains("preview"))
                {
                    linkNode.ParentNode.RemoveChild(linkNode);
                }
            }
            // If present, remove the editMode attribute that tells use which mode we're editing in (original or translation)
            var body = RawDom.SafeSelectNodes("/html/body")[0] as XmlElement;
            if (body.HasAttribute("editMode"))
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
                    if (String.IsNullOrEmpty(id) && Program.RunningUnitTests)
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
            if (x.Length == 0)
                Logger.WriteEvent(
                    "HtmlDom.ValidateBook({0}): No Errors",
                    descriptionOfBookForErrorLog
                );
            else
            {
                Logger.WriteEvent(
                    "HtmlDom.ValidateBook({0}): {1}",
                    descriptionOfBookForErrorLog,
                    x
                );
            }

            return builder.ToString();
        }

        private static void Ensure(bool passes, string message, StringBuilder builder)
        {
            if (!passes)
                builder.AppendLine(message);
        }

        private static void EnsureIdsAreUnique(
            HtmlDom dom,
            string elementTag,
            List<string> ids,
            StringBuilder builder
        )
        {
            foreach (XmlElement element in dom.SafeSelectNodes("//" + elementTag + "[@id]"))
            {
                // TODO: Maybe it should ignore empty strings?
                // This can throw an error which prevents saving the book, but the error is arguably a false positive.
                // Documentation says: "If the id value is not the empty string, it must be unique in a document."
                // https://developer.mozilla.org/en-US/docs/Web/API/Element/id
                //
                // On the other hand, the W3 validator reports "An ID must not be the empty string", so alternatively we can return an error for any empty string, not just the 2nd one.

                var id = element.GetAttribute("id");
                if (ids.Contains(id))
                    builder.AppendLine(
                        "The id of this "
                            + elementTag
                            + " must be unique, but is not: "
                            + element.OuterXml
                    );
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
                var href = xmlElement.GetStringAttribute("href");
                if (!BookStorage.CssFilesThatAreObsolete.Contains(href))
                {
                    headNode.AppendChild(xmlElement);
                }
            }
        }

        /// <summary>
        /// gecko 11 required the file://, but modern browsers can't handle it.
        /// </summary>
        public void RemoveFileProtocolFromStyleSheetLinks()
        {
            foreach (XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
            {
                var href = link.GetAttribute("href");
                link.SetAttribute("href", href.Replace("file:///", "").Replace("file://", ""));
            }
        }

        public void RemoveDirectorySpecificationFromStyleSheetLinks()
        {
            foreach (XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
            {
                var href = link.GetAttribute("href");
                link.SetAttribute("href", Path.GetFileName(href));
            }
        }

        /// <summary>
        /// Adds a string to the class attribute, if there isn't already an exact match.
        /// </summary>
        public static void AddClass(XmlElement e, string className)
        {
            if (HasClass(e, className))
                return;
            e.SetAttribute("class", (e.GetAttribute("class").Trim() + " " + className).Trim());
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
            var node = _dom.SafeSelectNodes(
                "//head/meta[@name='" + name + "' or @name='" + name.ToLowerInvariant() + "']"
            );
            if (node.Count > 0)
            {
                return ((XmlElement)node[0]).GetAttribute("content");
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
        /// Can be called without knowing that the old exists.
        /// If it already has the new, the old is just removed.
        /// This is just for migration.
        /// </summary>
        public void RemoveMetaElement(string oldName, Func<string> read, Action<string> write)
        {
            if (!HasMetaElement(oldName))
                return;

            if (!String.IsNullOrEmpty(read()))
            {
                RemoveMetaElement(oldName);
                return;
            }

            //ok, so we do have to transfer the value over

            write(GetMetaValue(oldName, ""));

            //and remove any of the old name
            foreach (
                XmlElement node in _dom.SafeSelectNodes("//head/meta[@name='" + oldName + "']")
            )
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
            var genericBookNames = new[] { "Basic Book", "Nupela Buk", "Buku Dasar" };
            foreach (XmlElement n in _dom.SafeSelectNodes("//*[@data-book='bookTitle']"))
            {
                if (genericBookNames.Contains(n.InnerText.Trim()))
                {
                    n.ParentNode.RemoveChild(n);
                }
            }
        }

        public void RemoveExtraContentTypesMetas()
        {
            bool first = true;
            foreach (
                XmlElement n in _dom.SafeSelectNodes("//head/meta[@http-equiv='Content-Type']")
            )
            {
                if (first) //leave one
                {
                    first = false;
                    continue;
                }

                n.ParentNode.RemoveChild(n);
            }
        }

        private static HashSet<string> stylesheetsToIgnoreAdding = new HashSet<string>(
            new[] { "previewMode.css", "editMode.css" }
        );

        /// <summary>
        /// Add a reference to the specified stylesheet if we don't already have it.
        /// Return true if added, and if it's not a known stylesheet that is automatically
        /// present in a page being edited.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool AddStyleSheetIfMissing(string path)
        {
            // Remember, Linux filenames are case sensitive.
            var pathToCheck = path;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                pathToCheck = pathToCheck.ToLowerInvariant();
            foreach (XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
            {
                var fileName = link.GetStringAttribute("href");
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    fileName = fileName.ToLowerInvariant();
                if (fileName == pathToCheck)
                    return false;
            }
            //_dom.AddStyleSheet(path.Replace("file://", "")); this used the libpalaso version which is slow and looks for the file and such
            AddStyleSheet(path);
            return !stylesheetsToIgnoreAdding.Contains(path);
        }

        /// <summary>
        /// We're trying to get names of style sheets that need to be copied from one book to another.
        /// So not the standard ones that every book has, but things like one specific to the particular
        /// template the source was made from, or that got added becuase we used an Activity page that
        /// has an associated one.
        /// </summary>
        /// <returns></returns>
        public virtual IEnumerable<string> GetTemplateStyleSheets()
        {
            var stylesheetsToIgnore = new List<string>();
            // Remember, Linux filenames are case sensitive!
            stylesheetsToIgnore.Add("basePage"); // will work for basePage.css, basePage-legacy-5-6.css, etc.
            stylesheetsToIgnore.Add("editMode.css");
            stylesheetsToIgnore.Add("previewMode.css");
            stylesheetsToIgnore.Add("XMatter");
            stylesheetsToIgnore.AddRange(BookStorage.CssFilesThatAreDynamicallyUpdated);

            foreach (XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
            {
                var fileName = link.GetStringAttribute("href");
                var nameToCheck = fileName;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    nameToCheck = fileName.ToLowerInvariant();
                bool match = false;
                foreach (var nameOrFragment in stylesheetsToIgnore)
                {
                    var nameStyle = nameOrFragment;
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        nameStyle = nameStyle.ToLowerInvariant();
                    if (nameToCheck.Contains(nameStyle))
                    {
                        match = true;
                        break;
                    }
                }
                if (!match)
                    yield return fileName;
            }
        }

        public static void CopyMissingStylesheetFiles(
            HtmlDom sourceDom,
            string sourceFolder,
            string destFolder
        )
        {
            foreach (string sheetName in sourceDom.GetTemplateStyleSheets())
            {
                var destinationPath = Path.Combine(destFolder, sheetName);
                if (!RobustFile.Exists(destinationPath))
                {
                    var sourcePath = Path.Combine(sourceFolder, sheetName);
                    if (RobustFile.Exists(sourcePath))
                        RobustFile.Copy(sourcePath, destinationPath);
                }
            }
        }

        public void AddPublishClassToBody(string kindOfPublication = null)
        {
            AddPublishClassToBody(_dom, kindOfPublication);
        }

        /// <summary>
        /// By including this class, we help stylesheets do something different for edit vs. publish mode.
        /// </summary>
        public static void AddPublishClassToBody(XmlDocument dom, string kindOfPublication = null)
        {
            AddClassToBody(dom, "publishMode");
            if (null != kindOfPublication)
                AddClassToBody(dom, kindOfPublication);
        }

        public static void AddClassToBody(XmlDocument dom, string className)
        {
            AddClass((XmlElement)dom.SelectSingleNode("//body"), className);
        }

        public static void RemoveClassFromBody(XmlDocument dom, string className)
        {
            RemoveClass((XmlElement)dom.SelectSingleNode("//body"), className);
        }

        public static void AddRightToLeftClassToBody(XmlDocument dom)
        {
            AddClass((XmlElement)dom.SelectSingleNode("//body"), "rightToLeft");
        }

        public static void AddHidePlaceHoldersClassToBody(XmlDocument dom)
        {
            AddClass((XmlElement)dom.SelectSingleNode("//body"), "hidePlaceHolders");
        }

        public static void AddCalendarFoldClassToBody(XmlDocument dom)
        {
            AddClass((XmlElement)dom.SelectSingleNode("//body"), "calendarFold");
        }

        /// <summary>
        /// clear out any old stylesheet links before we add them back. Things like xmatter.css and basePage might be replaced with different versions
        /// </summary>
        public void RemoveNormalStyleSheetsLinks()
        {
            if (Monitor.IsEntered(RawDom))
            {
                Debug.Fail(
                    "RemoveNormalStyleSheetsLinks: RawDom is locked, which we don't expect. In BL-12919 we added the lock on a hunch but did not have confirmation that it was ever used."
                );
            }

            //mystery: without this lock, some async process sometimes leads the linkNode to have a null parent when we go to remove it
            lock (RawDom)
            {
                var links = RawDom.SafeSelectNodes("/html/head/link").Cast<XmlElement>().ToArray();
                foreach (XmlElement linkNode in links)
                {
                    var href = linkNode.GetAttribute("href");
                    var name = Path.GetFileName(href).ToLowerInvariant();
                    if (
                        name.EndsWith("xmatter.css")
                        || BookStorage.KnownCssFilePrefixesInOrder.Any(
                            prefix => name.StartsWith(prefix.ToLowerInvariant())
                        )
                    )
                    {
                        linkNode.ParentNode.RemoveChild(linkNode);
                    }
                }
            }
        }

        internal void RemoveStyleSheetIfFound(string path)
        {
            XmlDomExtensions.RemoveStyleSheetIfFound(RawDom, path);
        }

        public bool UpdatePageToTemplate(
            HtmlDom pageDom,
            XmlElement templatePageDiv,
            string pageId,
            bool allowDataLoss = true
        )
        {
            var pageDiv = pageDom
                .SafeSelectNodes("//body/div[@id='" + pageId + "']")
                .Cast<XmlElement>()
                .FirstOrDefault();
            if (pageDiv != null)
            {
                var idAttr = templatePageDiv.Attributes["id"];
                var templateId = idAttr == null ? "" : idAttr.Value;
                bool didChange;
                var oldLineage = MigrateEditableData(
                    pageDiv,
                    templatePageDiv,
                    templateId,
                    allowDataLoss,
                    out didChange
                );
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

        public static (
            int textCount,
            int imageCount,
            int videoCount,
            int widgetCount
        ) GetEditableDataCounts(XmlElement page)
        {
            return (
                // See comment on GetEltsWithClassNotInImageContainerInternal() below.
                GetTranslationGroupNotInImageContainerCount(page),
                GetEltsWithClassNotInImageContainer(page, "bloom-imageContainer").Count,
                GetEltsWithClassNotInImageContainer(page, "bloom-videoContainer").Count,
                GetEltsWithClassNotInImageContainer(page, "bloom-widgetContainer").Count
            );
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
        internal string MigrateEditableData(
            XmlElement page,
            XmlElement template,
            string lineage,
            bool allowDataLoss,
            out bool didChange
        )
        {
            if (!allowDataLoss)
            {
                var (oldTextCount, oldImageCount, oldVideoCount, oldWidgetCount) =
                    GetEditableDataCounts(page);
                var (newTextCount, newImageCount, newVideoCount, newWidgetCount) =
                    GetEditableDataCounts(template);
                if (
                    newTextCount < oldTextCount
                    || newImageCount < oldImageCount
                    || newVideoCount < oldVideoCount
                    || newWidgetCount < oldWidgetCount
                )
                {
                    didChange = false;
                    return null;
                }
            }

            var newPage = (XmlElement)page.OwnerDocument.ImportNode(template, true);
            page.ParentNode.ReplaceChild(newPage, page);
            newPage.SetAttribute("id", page.Attributes["id"].Value);
            var oldLineageAttr = page.Attributes["data-pagelineage"];
            var oldLineage = oldLineageAttr == null ? "" : oldLineageAttr.Value;
            newPage.SetAttribute("data-pagelineage", lineage);

            //preserve the data-page attribute of the old page, which will normally be empty or missing
            var dataPageValue = page.GetAttribute("data-page");
            if (String.IsNullOrEmpty(dataPageValue))
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
                newPage.SetAttribute(
                    "class",
                    newPage.Attributes["class"].Value + " " + sideMatch.Value
                );
            }

            // preserve the page size and orientation of the old page
            newPage.SetAttribute(
                "class",
                TransferOrientation(classes, newPage.Attributes["class"].Value)
            );

            //the leading '.'s here are needed because newPage is an element in a larger DOM, and we only want to search in this page
            // migrate text (between visible translation groups!)
            // enhance: I wish there was a better way to detect invisible translation groups than just knowing about one class
            // that currently hides them.
            MigrateChildren(
                GetTranslationGroupsNotInImageContainer(page),
                GetTranslationGroupsNotInImageContainer(newPage)
            );
            // migrate images
            MigrateChildrenWithCommonClass(page, "bloom-imageContainer", newPage);
            // migrate videos
            MigrateChildrenWithCommonClass(page, "bloom-videoContainer", newPage);
            // migrate HTML widgets
            MigrateChildrenWithCommonClass(page, "bloom-widgetContainer", newPage);
            RemovePlaceholderVideoClass(newPage);
            RemovePlaceholderWidgetClass(newPage);
            didChange = true;
            return oldLineage;
        }

        private static int GetTranslationGroupNotInImageContainerCount(XmlElement pageElement)
        {
            var result = GetTranslationGroupsNotInImageContainer(pageElement);

            return result.Count;
        }

        private static List<XmlElement> GetTranslationGroupsNotInImageContainer(
            XmlElement pageElement
        )
        {
            return GetEltsWithClassNotInImageContainer(pageElement, "bloom-translationGroup");
        }

        private static List<XmlElement> GetEltsWithClassNotInImageContainer(
            XmlElement pageElement,
            string targetClass
        )
        {
            var result = new List<XmlElement>();
            GetEltsWithClassNotInImageContainerInternal(pageElement, ref result, targetClass);

            return result;
        }

        // We want to count all the translationGroups that do not occur inside of a bloom-imageContainer div.
        // The reason for this is that images can have textOverPicture divs and imageDescription divs inside of them
        // and these are completely independent of the template page. We need to count regular translationGroups and
        // also ensure that translationGroups inside of images get migrated correctly. If this algorithm changes, be
        // sure to also change 'countTranslationGroupsForChangeLayout()' in PageChooserDialog.tsx.
        // We could just do this with an xpath if bloom-textOverPicture divs and bloom-imageDescription divs had
        // the same structure internally, but text over picture CONTAINS a translationGroup,
        // whereas image description IS a translationGroup.
        private static void GetEltsWithClassNotInImageContainerInternal(
            XmlElement currentElement,
            ref List<XmlElement> result,
            string targetClass
        )
        {
            if (currentElement.HasAttribute("class"))
            {
                var classes = currentElement.Attributes["class"].Value;
                if (classes.Contains(targetClass))
                {
                    // box-header-off/on appears to be vestigial at this point,
                    // but suffice it to say "box-header-off" translationGroups are not visible.
                    if (!classes.Contains("box-header-off"))
                    {
                        result.Add(currentElement);
                    }
                    return; // don't drill down further
                }
                // Test this AFTER looking for targetClass; we do want to find the TOP bloom-imageContainers.
                if (classes.Contains("bloom-imageContainer"))
                    return; // don't drill down inside of this one
            }

            if (!currentElement.HasChildNodes)
                return;
            foreach (XmlNode childNode in currentElement.ChildNodes)
            {
                var childElement = childNode as XmlElement;
                if (childElement == null) // if the node is not castable to XmlElement
                    continue;

                GetEltsWithClassNotInImageContainerInternal(childElement, ref result, targetClass);
            }
        }

        /// <summary>
        /// Gets a list of colors used in the current book.
        /// </summary>
        public List<string> GetColorsUsedInBookBubbleElements()
        {
            var colorElementList = new List<string>();
            var textOverPictureElements = GetTextOverPictureElements(Body);
            foreach (var node in textOverPictureElements)
            {
                var styleAttr = node.GetOptionalStringAttribute("style", "");
                if (!String.IsNullOrEmpty(styleAttr))
                {
                    // Possible bubble text color
                    var textColorValue = GetColorValueFromStyle(styleAttr);
                    if (!String.IsNullOrEmpty(textColorValue))
                    {
                        var textColorString = DynamicJson.Serialize(
                            new { colors = new[] { textColorValue } }
                        );
                        colorElementList.Add(textColorString);
                    }
                }
                var dataBubbleAttr = node.GetOptionalStringAttribute("data-bubble", "");
                if (String.IsNullOrEmpty(dataBubbleAttr))
                    continue;

                // Possible bubble background color
                var jsonObject = GetJsonObjectFromDataBubble(dataBubbleAttr);
                if (jsonObject == null)
                    continue; // only happens if it fails to parse the "json"

                // Note: background color strings with opacity will be in the form "rgba(r, g, b, a)",
                // while text color strings and background strings w/o opacity could be hex strings or even named colors.
                string[] backgroundColorArray = GetBackgroundColorsFromDataBubbleJsonObj(
                    jsonObject
                );
                if (backgroundColorArray == null || backgroundColorArray.Length == 0)
                    continue;

                var backgroundColorString = DynamicJson.Serialize(
                    new { colors = backgroundColorArray }
                );

                colorElementList.Add(backgroundColorString);
            }

            return colorElementList;
        }

        private static string GetColorValueFromStyle(string styleAttrVal)
        {
            // Looking for something like "color: rgb(x,y,z);" or "color: #aaaaaa;"
            var styleRegex = new Regex(@"\s*color\s*:\s*(.+)\s*;");
            var match = styleRegex.Match(styleAttrVal);
            // If successful, group 1 should be everything between "color: " and ";"
            return match.Success ? match.Groups[1].Value : String.Empty;
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
                Logger.WriteEvent(
                    "HtmlDom.GetJsonObjectFromDataBubble() failed to parse data-bubble: "
                        + dataBubbleAttrVal
                );
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
            const string xpath =
                ".//div[contains(concat(' ', normalize-space(@class), ' '), ' {0} ')]";
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
            var langDivs = pageElements.SelectMany(
                page => page.SafeSelectNodes(".//div[@class and @lang]").Cast<XmlElement>()
            );

            // Check each language div against some additional criteria
            langDivs = langDivs
                // BL-8228. Don't proceed if this is a text without normal parentage, e.g. boilerplate text from a Branding pack.
                // Before we added this line, the next one (testing for bloom-ignoreChildrenForBookLanguageList) would throw with
                // the Juarez and Associates (Guatemala) Branding Pack.
                .Where(div => div.ParentNode?.Attributes?["class"] != null)
                // At least up through 4.7, bloom-ignoreChildrenForBookLanguageList is used to prevent counting localized
                // headers in comprehension quiz as languages of the book.
                .Where(
                    div =>
                        !div.ParentNode.Attributes["class"].Value.Contains(
                            "bloom-ignoreChildrenForBookLanguageList"
                        )
                )
                .Where(
                    div =>
                        div.Attributes["class"].Value.IndexOf(
                            "bloom-editable",
                            StringComparison.InvariantCulture
                        ) >= 0
                )
                .Where(div => HtmlDom.IsLanguageValid(div.Attributes["lang"].Value));

            return langDivs;
        }

        /// <summary>
        /// Get all the language tags which have visible content in this DOM
        /// </summary>
        /// <returns>Set of language tags</returns>
        public ISet<string> GetLanguagesWithContent()
        {
            ISet<string> result = new HashSet<string>();
            var langDivSet = new HashSet<XmlElement>();
            // If xmatter text isn't visible, its language data doesn't really matter.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-9831.
            langDivSet.AddRange(
                GetLanguageDivs(true)
                    .Where(
                        div => div.Attributes["class"].Value.Contains("bloom-visibility-code-on")
                    )
            );
            // Now add other languages from content pages only that have text that are not marked
            // as visible, but could be made visible by the user in Bloom Reader.
            langDivSet.AddRange(GetLanguageDivs(false));
            foreach (var div in langDivSet)
            {
                if (!String.IsNullOrWhiteSpace(div.InnerText))
                    result.Add(div.Attributes["lang"].Value);
            }
            return result;
        }

        public static bool DivHasContent(XmlElement div)
        {
            var divClone = div.CloneNode(true);

            // Don't count a language if it only has text in a label.
            // For some xmatter fields, the label is outside the editables.
            // But for back cover, at least, it is inside.
            var labels = divClone.SafeSelectNodes("label").Cast<XmlElement>();
            foreach (var label in labels)
                divClone.RemoveChild(label);

            return !string.IsNullOrWhiteSpace(divClone.InnerText);
        }

        /// <summary>
        /// Checks if the specified language is considered valid (e.g. non-empty, not "*", not "z")
        /// </summary>
        internal static bool IsLanguageValid(string lang)
        {
            return !String.IsNullOrWhiteSpace(lang) && lang != "*" && lang != "z"; // Not valid languages, though we sometimes use them for special purposes
        }

        private static void RemovePlaceholderVideoClass(XmlElement newPage)
        {
            const string videoPlaceholderClass = "bloom-noVideoSelected";
            var nodesWithPlaceholder = newPage.SelectNodes(
                "//div[contains(@class,'" + videoPlaceholderClass + "')]"
            );
            foreach (XmlNode placeholderDiv in nodesWithPlaceholder)
            {
                if (placeholderDiv.HasChildNodes && placeholderDiv.FirstChild.Name == "video")
                {
                    // We migrated a video node into here, delete the placeholder class.
                    XmlUtils.SetAttribute(
                        placeholderDiv,
                        "class",
                        XmlUtils
                            .GetStringAttribute(placeholderDiv, "class")
                            .Replace(videoPlaceholderClass, String.Empty)
                    );
                }
            }
        }

        private static void RemovePlaceholderWidgetClass(XmlElement newPage)
        {
            const string widgetPlaceholderClass = "bloom-noWidgetSelected";
            var nodesWithPlaceholder = newPage.SelectNodes(
                "//div[contains(@class,'" + widgetPlaceholderClass + "')]"
            );
            foreach (XmlNode placeholderDiv in nodesWithPlaceholder)
            {
                if (placeholderDiv.HasChildNodes && placeholderDiv.FirstChild.Name == "iframe")
                {
                    // We migrated a widget node into here, delete the placeholder class.
                    XmlUtils.SetAttribute(
                        placeholderDiv,
                        "class",
                        XmlUtils
                            .GetStringAttribute(placeholderDiv, "class")
                            .Replace(widgetPlaceholderClass, String.Empty)
                    );
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
        private static void MigrateChildrenWithCommonClass(
            XmlElement page,
            string className,
            XmlElement newPage
        )
        {
            var oldParents = new List<XmlElement>(
                GetEltsWithClassNotInImageContainer(page, className).Cast<XmlElement>()
            );
            var newParents = new List<XmlElement>(
                GetEltsWithClassNotInImageContainer(newPage, className).Cast<XmlElement>()
            );
            MigrateChildren(oldParents, newParents);
        }

        private const string defaultLangKey = "z";

        // Migrate container (translationGroup, video, image) children from an old page to their new template equivalent.
        private static void MigrateChildren(
            IReadOnlyList<XmlElement> oldParentElements,
            IReadOnlyList<XmlElement> templateParentElements
        )
        {
            // 'xParentElements' are either 'bloom-translationGroup', 'bloom-imageContainer', 'bloom-widgetContainer', or 'bloom-videoContainer'
            // The Math.Min is not needed yet; in fact, we don't yet have any cases where there is more than one
            // thing to copy or where the numbers are not equal. It's just a precaution.
            for (
                var i = 0;
                i < Math.Min(templateParentElements.Count, oldParentElements.Count);
                i++
            )
            {
                var oldParent = oldParentElements[i];
                var newParent = templateParentElements[i];
                // Our template page may have style classes on bloom-editables (or possibly other elements
                // inside of the container element). We want to preserve them when we copy the child nodes into the new page,
                // so we collect them by lang attribute value.
                // It is possible (using Template Starter or the ChangeLayout toggle) to create a situation where different
                // lang attribute bloom-editables have different style classes ('x-style').
                var childClassesFromTemplateByLang = new Dictionary<string, string>();
                foreach (
                    var templateContainerChild in newParent.ChildNodes.Cast<XmlNode>().ToArray()
                )
                {
                    // Look for an 'x-style' class in the child and squirrel it away in our dictionary, keyed on language.
                    var langAttr = GetLangAttr(templateContainerChild);
                    var childClassFromTemplate = GetStyle(templateContainerChild);
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
                foreach (
                    XmlNode oldContainerChild in oldParent.ChildNodes.Cast<XmlNode>().ToArray()
                )
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
                if (_stylesToDefine == null)
                {
                    _stylesToDefine = new Dictionary<string, string>();
                    _stylesToDefine["BigWords"] =
                        ".BigWords-style { font-size: 45pt !important; text-align: center !important; }";
                }
                return _stylesToDefine;
            }
        }

        private static void AddKnownStyleIfMissing(XmlNode child)
        {
            if (child.Attributes == null)
                return; // e.g., whitespace
            var classAttr = child.Attributes["class"];
            if (classAttr == null)
                return;
            foreach (var style in classAttr.Value.Split(' ').Where(x => x.EndsWith("-style")))
            {
                var key = style.Substring(0, style.Length - ".style".Length);
                string defaultDefn;
                if (!StylesToDefine.TryGetValue(key, out defaultDefn))
                    continue; // I don't think there should be more than one -style item, but just in case...
                // Todo: conditions...
                var headElt = child.OwnerDocument.DocumentElement.ChildNodes
                    .Cast<XmlNode>()
                    .First(x => x.Name == "head");
                var userStyles = GetUserModifiedStyleElement(headElt);
                if (userStyles == null)
                {
                    userStyles = AddEmptyUserModifiedStylesNode(headElt);
                    userStyles.InnerText = defaultDefn;
                    continue;
                }
                var content = userStyles.InnerText;
                var lookFor = new Regex("\\." + style + "\\s*{");
                if (lookFor.IsMatch(content))
                    continue; // style already defined
                userStyles.InnerText += String.IsNullOrEmpty(content)
                    ? defaultDefn
                    : " " + defaultDefn;
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

        private static void FixStyle(
            XmlNode child,
            string requiredClass,
            Dictionary<string, string> desiredStyleByLang
        )
        {
            if (
                desiredStyleByLang.Count == 0
                || child.Attributes?["class"] == null
                || !child.Attributes["class"].Value.Contains(requiredClass)
            )
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
            ((XmlElement)child).SetAttribute("class", newclass);
        }

        // Both of these are relative to the DOM's Head element
        private const string CoverColorStyleXPath =
            "./style[@type='text/css' and contains(.,'coverColor')]";
        public const string UserModifiedStyleXPath =
            "./style[@type='text/css' and @title='userModifiedStyles']";

        /// <summary>
        /// Finds the style element that contains css rules for 'userModifiedStyles',
        /// or null if it doesn't exist.
        /// </summary>
        /// <param name="headElement"></param>
        internal static XmlElement GetUserModifiedStyleElement(XmlNode headElement)
        {
            return headElement
                .SafeSelectNodes(UserModifiedStyleXPath)
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
            return headElement
                .SafeSelectNodes(CoverColorStyleXPath)
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
        /// Return the definitions of the user-modifiable styles used in the page.
        /// </summary>
        /// <param name="domForInsertedPage"></param>
        /// <returns></returns>
        internal static string GetUserModifiableStylesUsedOnPage(HtmlDom domForInsertedPage)
        {
            return GetUserModifiableStylesUsedOnPage(
                domForInsertedPage.Head,
                domForInsertedPage.Body
            );
        }

        internal static string GetUserModifiableStylesUsedOnPage(
            XmlElement head,
            XmlElement contentToSearch
        )
        {
            // there should only be one userModifiedStyles node, so this will only grab the first one
            var userStyleElementFromTemplate = GetUserModifiedStyleElement(head);
            if (userStyleElementFromTemplate == null)
                return "";

            var keyDict = GetUserStyleKeyDict(userStyleElementFromTemplate);
            var keysUsedOnPage = new Dictionary<string, string>();
            foreach (var keyPair in keyDict)
            {
                var style = GetStyleNameFromRuleSelector(keyPair.Key);
                var searchResult = GetAllDivsWithClass(contentToSearch, style);
                if (searchResult.Count > 0)
                    keysUsedOnPage.Add(keyPair.Key, keyPair.Value);
            }
            return GetCompleteFilteredUserStylesInnerText(keysUsedOnPage);
        }

        private static string GetStyleNameFromRuleSelector(string selector)
        {
            // Key.Substring(1) strips off initial period from class name
            // Stripping off everything after -style removes [lang] stuff and >p stuff.
            var indexOfStyle = selector.LastIndexOf("-style", StringComparison.InvariantCulture);
            return selector.Substring(1, indexOfStyle + "-style".Length - 1);
        }

        public static string MergeUserStylesOnInsertion(
            XmlNode existingUserStyleNode,
            XmlNode insertedPageUserStyleNode
        )
        {
            return MergeUserStylesOnInsertion(
                existingUserStyleNode,
                insertedPageUserStyleNode?.InnerText ?? "",
                out bool dummy
            );
        }

        /// <summary>
        /// Merges the user modified styles from an existing book with the ones used on a page inserted from a different template.
        /// This method will not overwrite a style already defined with the same name in the "receptor" book.
        /// It might, however, add a style where a pre-existing style differed only in language attribute.
        /// </summary>
        /// <param name="existingUserStyleNode">From current book's storage</param>
        /// <param name="insertedPageUserStyles">Should be the InnerText (not InnerXml) of a style node.</param>
        /// <returns>The InnerXml to which the user modified styles element should be set.</returns>
        public static string MergeUserStylesOnInsertion(
            XmlNode existingUserStyleNode,
            string insertedPageUserStyles,
            out bool didAdd
        )
        {
            didAdd = false;
            // this method in production is currently always called just after
            // CurrentBook.GetOrCreateUserModifiedStyleElementFromStorage()
            Guard.AgainstNull(existingUserStyleNode, "existingUserStyleNode");

            if (insertedPageUserStyles == null || insertedPageUserStyles == String.Empty)
                return WrapUserStyleInCdata(existingUserStyleNode.InnerText);

            var existingStyleKeyDict = GetUserStyleKeyDict(existingUserStyleNode);
            var existingStyleNames = new HashSet<string>();
            foreach (var key in existingStyleKeyDict.Keys)
            {
                existingStyleNames.Add(GetStyleNameFromRuleSelector(key));
            }
            var insertedPageStyleKeyDict = GetUserStyleKeyDict(insertedPageUserStyles); // could be empty
            foreach (var keyPair in insertedPageStyleKeyDict)
            {
                if (existingStyleNames.Contains(GetStyleNameFromRuleSelector(keyPair.Key)))
                    continue;
                existingStyleKeyDict.Add(keyPair);
                didAdd = true;
            }
            return WrapUserStyleInCdata(
                GetCompleteFilteredUserStylesInnerText(existingStyleKeyDict)
            );
        }

        /// <summary>
        /// Wraps the inner css styles for userModifiedStyles in commented CDATA so we can handle invalid
        /// xhtml characters like >.
        /// </summary>
        private static string WrapUserStyleInCdata(string innerCssStyles)
        {
            if (innerCssStyles.StartsWith(XmlHtmlConverter.CdataPrefix))
            {
                // For some reason, we are already wrapped in CDATA.
                // Could happen in HtmlDom.MergeUserStylesOnInsertion().
                return innerCssStyles;
            }
            // Now, our styles string may contain invalid xhtml characters like >
            // We shouldn't have &gt; in XHTML because the content of <style> is supposed to be CSS, and &gt; is an HTML escape.
            // And in XElement we can't just have > like we can in HTML (<style> is PCDATA, not CDATA).
            // So, we want to mark the main body of the rules as <![CDATA[ ...]]>, within which we CAN have >.
            // But, once again, that's HTML markup that's not valid CSS. To fix it we wrap each of the markers
            // in CSS comments, so the wrappers end up as /*<![CDATA[*/.../*]]>*/.
            var cdataString = new StringBuilder();
            cdataString.AppendLine(XmlHtmlConverter.CdataPrefix);
            cdataString.Append(innerCssStyles); // Not using AppendLine, since innerCssStyles is likely several lines
            cdataString.AppendLine(XmlHtmlConverter.CdataSuffix);
            return cdataString.ToString();
        }

        public static string CreateUserModifiedStyles(string userCssContent)
        {
            var outerStyleElementString = new StringBuilder();
            outerStyleElementString.AppendLine(
                "<style title='userModifiedStyles' type='text/css'>"
            );
            outerStyleElementString.Append(WrapUserStyleInCdata(userCssContent));
            outerStyleElementString.AppendLine("</style>");
            return outerStyleElementString.ToString();
        }

        private static string GetCompleteFilteredUserStylesInnerText(
            IDictionary<string, string> desiredKeys
        )
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
            return GetUserStyleKeyDict(userStyleNode.InnerText);
        }

        private static IDictionary<string, string> GetUserStyleKeyDict(string userStyles)
        {
            var keyDict = new Dictionary<string, string>();
            var styleStrings = GetStyles(userStyles); // skips empty lines
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
        public void SetBookFeature(
            string featureName,
            string orientationConstraint,
            string mediaConstraint
        )
        {
            Debug.Assert(
                featureName == featureName.ToLowerInvariant(),
                "HTML requires attribute names to be all lower case (feature)"
            );
            Body.SetAttribute(
                "data-bf" + featureName,
                orientationConstraint + ";" + mediaConstraint
            );
        }

        /// <summary>
        /// Remove the specified feature. I included the constraints for consistency with SetBookFeature,
        /// but actually we don't currently support more than one constraint pair per feature, so any remove
        /// removes that featre completely, irrespective of constraints.
        /// </summary>
        /// <param name="featureName"></param>
        /// <param name="orientationConstraint"></param>
        /// <param name="mediaConstraint"></param>
        public void ClearBookFeature(
            string featureName,
            string orientationConstraint,
            string mediaConstraint
        )
        {
            Body.RemoveAttribute("data-bf" + featureName);
        }

        /// <summary>
        /// Returns true if the specified feature name matches the specified orientationConstraint, mediaConstraint pair
        /// </summary>
        /// <param name="featureName">The data-bf feature to check if its value matches</param>
        /// <param name="orientationConstraint">The orientation constraint to match</param>
        /// <param name="mediaConstraint">The media constraint to match</param>
        public bool BookHasFeature(
            string featureName,
            string orientationConstraint,
            string mediaConstraint
        )
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
            var label =
                editedPageDiv.SelectSingleNode("//div[contains(@class,'pageLabel')]") as XmlElement;
            if (label != null)
            {
                label.RemoveAttribute("contenteditable");
            }

            editedPageDiv.SetAttribute(
                "class",
                editedPageDiv.GetAttribute("class").Replace(" bloom-templateMode", "")
            );
        }

        // duplicates information in musicToolsControl.tsx
        public const string musicAttrName = "data-backgroundaudio";
        public const string musicVolumeName = musicAttrName + "volume";

        public static void ProcessPageAfterEditing(
            XmlElement destinationPageDiv,
            XmlElement edittedPageDiv
        )
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
            var selectorsToUnion = classNamesToUnion.Select(
                className => $"//*[contains(concat(' ', @class, ' '), ' {className} ')]"
            );
            var unionedXPathExpression = String.Join(" | ", selectorsToUnion);
            foreach (
                var node in edittedPageDiv
                    .SafeSelectNodes(unionedXPathExpression)
                    .Cast<XmlNode>()
                    .ToArray()
            )
                node.ParentNode.RemoveChild(node);
            RemoveTemplateEditingMarkup(edittedPageDiv);
            RemoveCkEditorMarkup(edittedPageDiv);

            destinationPageDiv.InnerXml = edittedPageDiv.InnerXml;

            //Enhance: maybe we should just copy over all attributes?
            destinationPageDiv.SetAttribute("class", edittedPageDiv.GetAttribute("class"));
            //The SIL LEAD SHRP templates rely on "lang" on some ancestor to trigger the correct rules in labels.css.
            //Those get set by putting data-metalanguage on Page, which then leads to a lang='xyz'. Let's save that
            //back to the html in keeping with our goal of having the page look right if you were to just open the
            //html file in a browser.
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
                var node in destinationPageDiv
                    .SafeSelectNodes(
                        ".//*[contains(concat(' ', @class, ' '), ' origami-layout-mode ')]"
                    )
                    .Cast<XmlNode>()
                    .ToArray()
            )
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
            foreach (
                XmlElement elt in edittedPageDiv.SafeSelectNodes("//*[contains(@class, 'cke_')]")
            )
            {
                elt.SetAttribute(
                    "class",
                    String.Join(
                        " ",
                        elt.GetAttribute("class").Split(' ').Where(c => !c.StartsWith("cke_"))
                    )
                );
            }
        }

        /// <summary>
        /// Gives all the unique language codes found in datadiv elements that have data-book
        /// </summary>
        /// <returns></returns>
        public List<string> GatherDataBookLanguages()
        {
            var dataBookElements = RawDom.SafeSelectNodes(
                "//div[@id='bloomDataDiv']/div[@data-book]"
            );
            return dataBookElements
                .Cast<XmlElement>()
                .Select(node => node.GetOptionalStringAttribute("lang", null))
                .Where(lang => !String.IsNullOrEmpty(lang) && (lang != "*" && lang != "z"))
                .Distinct()
                .ToList();
        }

        public MultiTextBase GetBookSetting(string key)
        {
            var result = new MultiTextBase();
            foreach (
                XmlElement e in RawDom.SafeSelectNodes(
                    "//div[@id='bloomDataDiv']/div[@data-book='"
                        + key
                        + "' or @data-derived='"
                        + key
                        + "']"
                )
            )
            {
                var lang = e.GetAttribute("lang");
                result.SetAlternative(lang ?? "", e.InnerXml.Trim());
            }
            return result;
        }

        public void RemoveBookSetting(string key)
        {
            foreach (
                XmlElement e in RawDom
                    .SafeSelectNodes(
                        "//div[@id='bloomDataDiv']/div[@data-book='"
                            + key
                            + "' or @data-derived='"
                            + key
                            + "']"
                    )
                    .Cast<XmlElement>()
                    .ToList()
            )
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
                dataDiv.SelectSingleNode(String.Format("div[@data-book='{0}' and not(@lang)]", key))
                as XmlElement;
            if (obsoleteNode != null)
                dataDiv.RemoveChild(obsoleteNode);

            XmlElement node =
                dataDiv.SelectSingleNode(
                    String.Format("div[@data-book='{0}' and @lang='{1}']", key, writingSystemId)
                ) as XmlElement;

            if (String.IsNullOrEmpty(form))
            {
                if (null != node)
                    dataDiv.RemoveChild(node);
                return;
            }

            if (null == node)
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
                withBreaksHidden = withBreaksHidden
                    .Replace(match.Groups[1].Value, kCiteBegin)
                    .Replace("</cite>", kCiteEnd);
            }

            //going to innertext means we treat everything literally, for better or worse (definitely safer)
            node.InnerText = withBreaksHidden;
            // finally, restore the breaks and <cite> markup
            var safeText = node.InnerXml; // anything XML-ish has been escaped
            if (match.Success)
            {
                safeText = safeText
                    .Replace(kCiteEnd, "</cite>")
                    .Replace(kCiteBegin, match.Groups[1].Value);
            }
            safeText = safeText.Replace(kBR, "<br/>");
            node.InnerXml = safeText;
        }

        internal static void StripUnwantedTagsPreservingText(
            XmlDocument dom,
            XmlNode element,
            string[] tagsToPreserve
        )
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
        public static void MergeClassesIntoNewPage(
            XmlElement sourcePage,
            XmlElement targetPage,
            string[] classesToDrop
        )
        {
            foreach (var c in GetClasses(sourcePage))
            {
                if (!classesToDrop.Contains(c))
                    AddClass(targetPage, c);
            }
        }

        private static IEnumerable<string> GetClasses(XmlElement element)
        {
            var classes = element.GetAttribute("class");
            if (String.IsNullOrEmpty(classes))
                return new string[] { };
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
            foreach (var node in parent.ChildNodes)
            {
                var elt = node as XmlElement;
                if (elt == null)
                    continue;
                var eltClass = " " + GetAttributeValue(elt, "class") + " ";
                if (eltClass.Contains(" " + classVal + " "))
                    return elt;
            }
            return null;
        }

        public static string GetAttributeValue(XmlElement elt, string name)
        {
            var attr = elt.Attributes[name];
            if (attr == null)
                return "";
            return attr.Value;
        }

        /// <summary>
        /// Finds a list of fonts used in the given css
        /// </summary>
        /// <param name="cssContent">Content of either a CSS file or an HTML file</param>
        /// <param name="result">set of fonts found in the CSS markup</param>
        /// <param name="includeFallbackFonts">true to include fallback fonts, false to include only the first font in each font family</param>
        public static void FindFontsUsedInCss(
            string cssContent,
            HashSet<string> result,
            bool includeFallbackFonts
        )
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
            foreach (Match match in findFF.Matches(cssContent))
            {
                foreach (var family in match.Groups[1].Value.Split(','))
                {
                    var name = family.Trim();
                    // Removing !important must come before stripping surrounding quotes.  For instance,
                    //   font-family: "Andika New Basic" !important, Comic Sans MS ! important;
                    // should extract Andika New Basic and Comic Sans MS (two strings without any quotes).
                    name = s_regexBangImportant.Replace(name, "");
                    // Strip matched quotes
                    if ((name[0] == '\'' || name[0] == '"') && name[0] == name[name.Length - 1])
                        name = name.Substring(1, name.Length - 2);
                    if (
                        name.ToLowerInvariant() != "inherit"
                        && name.ToLowerInvariant() != "segoe ui"
                    )
                        result.Add(name);
                    if (!includeFallbackFonts)
                        break;
                }
            }
        }

        private static void FindFontsUsedInEmbeddedCss(
            string htmlContent,
            HashSet<string> result,
            bool includeFallbackFonts
        )
        {
            var dom = XmlHtmlConverter.GetXmlDomFromHtml(htmlContent);
            var styles = dom.SafeSelectNodes("/html/head/style");
            foreach (XmlElement style in styles)
            {
                var cssContent = style.InnerText;
                FindFontsUsedInCss(cssContent, result, includeFallbackFonts);
            }
            var elementsWithStyleAttribute = dom.SafeSelectNodes("/html/body//*[@style]");
            foreach (XmlElement element in elementsWithStyleAttribute)
            {
                var cssContent = element.GetAttribute("style");
                if (
                    !String.IsNullOrEmpty(cssContent)
                    && cssContent.Contains("font-family:")
                    && !String.IsNullOrEmpty(element.InnerText)
                )
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
            for (
                var idxStart = FindCommentStartOutsideQuotes(cssContent, 0);
                idxStart >= 0;
                idxStart = FindCommentStartOutsideQuotes(cssContent, idxStart)
            )
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
                return idxComment; // no comment found, inside or outside any quotes
            var idxQuote = content.IndexOfAny(quotes, start);
            if (idxQuote < 0 || idxQuote > idxComment)
                return idxComment;
            var endQuote = content.IndexOf(content[idxQuote], idxQuote + 1);
            if (endQuote < 0)
                return idxComment; // ignore unterminated quote
            return FindCommentStartOutsideQuotes(content, endQuote + 1);
        }

        /// <summary>
        /// Remove unwanted language rules from the css text.
        /// </summary>
        /// <remarks>
        /// If our CSS parsing needs get much greater, we may have to start using a CSS parser.
        /// I think we can still get by with regular expressions for the CSS generated by Bloom.
        /// It's only the CSS in customCollectionStyles.css that may baffle our efforts.
        /// </remarks>
        /// <returns>possibly modified css text</returns>
        public static string RemoveUnwantedLanguageRulesFromCss(
            string cssText,
            IEnumerable<string> languagesToInclude
        )
        {
            var wantedLanguages = new HashSet<string>(languagesToInclude);
            var unwantedLanguages = new HashSet<string>();
            var matches = Regex.Matches(
                cssText,
                @"[\r\n][-.\s\w]*\[lang=(['""])([-a-zA-Z]+)\1\]\s*{[^}]*}"
            );
            foreach (Match match in matches)
            {
                var lang = match.Groups[2].Value;
                if (!wantedLanguages.Contains(lang))
                    unwantedLanguages.Add(lang);
            }
            foreach (var lang in unwantedLanguages)
                cssText = Regex.Replace(
                    cssText,
                    @"([\r\n]+)[-.\s\w]*\[lang=(['""])" + lang + @"\2\]\s*{[^}]*}",
                    "$1"
                );

            return cssText;
        }

        /// <summary>
        /// Gets the url for the image, either from an img element or any other element that has
        /// an inline style with background-image set.
        /// </summary>
        public static UrlPathString GetImageElementUrl(XmlElement imgOrDivWithBackgroundImage)
        {
            if (imgOrDivWithBackgroundImage.Name.ToLower() == "img")
            {
                var src = imgOrDivWithBackgroundImage.GetAttribute("src");
                return UrlPathString.CreateFromUrlEncodedString(src);
            }
            else
            {
                var styleRule = imgOrDivWithBackgroundImage.GetAttribute("style") ?? "";
                var regex = new Regex(
                    "background-image\\s*:\\s*url\\((.*)\\)",
                    RegexOptions.IgnoreCase
                );
                var match = regex.Match(styleRule);
                if (match.Groups.Count == 2)
                {
                    return UrlPathString.CreateFromUrlEncodedString(
                        match.Groups[1].Value.Trim(new[] { '\'', '"' })
                    );
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
            var videoElt = videoContainer.GetChildWithName("video");
            // we choose to return an empty path for failure instead of null to reduce errors created by things like
            // HtmlDom.GetImageElementUrl(element).UrlEncoded.
            if (videoElt == null)
                return UrlPathString.CreateFromUnencodedString(String.Empty);
            var srcElt = videoElt.GetChildWithName("source");
            if (srcElt == null)
                return UrlPathString.CreateFromUnencodedString(String.Empty);

            var src = srcElt.GetAttribute("src");
            string dummy;
            var fileName = SignLanguageApi.StripTimingFromVideoUrl(src, out dummy);
            return UrlPathString.CreateFromUrlEncodedString(fileName);
        }

        /// <summary>
        /// Gets the url for the audio, either from an audio-sentence class or any other element that has
        /// an inline style with data-backgroundaudio set.
        /// </summary>
        public static UrlPathString GetAudioElementUrl(XmlElement audioOrDivWithBackgroundMusic)
        {
            var classStr = audioOrDivWithBackgroundMusic.GetAttribute("class");
            if (classStr.Contains("audio-sentence"))
            {
                var id = audioOrDivWithBackgroundMusic.GetAttribute("id");
                return UrlPathString.CreateFromUrlEncodedString(id);
            }

            var backgroundAudioFileName =
                audioOrDivWithBackgroundMusic.GetAttribute("data-backgroundaudio") ?? String.Empty;
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
        /// or a div with an inline style with an background-image rule.
        /// This logic is duplicated in TypeScript, currently in a listener inside
        /// the SetupElements method in bloomEditing.ts. We need it there so that
        /// we can immediately update the current edit DOM, live in Javascript,
        /// when we change an image in edit mode. And there are many callers where we need
        /// to set the source of an image from C#. I don't see any likelihood
        /// that we can get rid of this duplication, though if we end up needing it
        /// in more than one place on the JS side, we should extract a common method there.
        /// Note: in both places, the code assumes that background-image is the ONLY
        /// thing that needs to be set in the element's explicit style attribute.
        /// </summary>
        public static void SetImageElementUrl(
            XmlElement imgOrDivWithBackgroundImage,
            UrlPathString url,
            bool urlEncode = true
        )
        {
            string urlFormToUse = urlEncode ? url.UrlEncoded : url.NotEncoded;

            if (imgOrDivWithBackgroundImage.Name.ToLower() == "img")
            {
                // This does not need to be encoded until sent over the network.
                // Indeed, encoding it breaks links within epubs.

                // This is really convoluted to process, but regardless of whether it's UrlEncoded or NotEncoded,
                // that is URL encoding and irrelevant to the XML Encoding.
                // From the XML perspective, neither of these strings have been XML-Encoded yet, so we should call XmlString.FromUnencoded.
                // (Also, from a legacy perspective, the old SetAttribute function would pass these into subfunctions that expect unencoded values, so that's a 2nd reason)
                XmlString urlAttributeValue = XmlString.FromUnencoded(urlFormToUse);
                imgOrDivWithBackgroundImage.SetAttribute("src", urlAttributeValue.Unencoded);
            }
            else
            {
                // The string formatting must match BookCompressor.kBackgroundImage
                imgOrDivWithBackgroundImage.SetAttribute(
                    "style",
                    XmlString
                        .FromUnencoded(String.Format("background-image:url('{0}')", urlFormToUse))
                        .Unencoded
                );
            }
        }

        /// <summary>
        /// Set the video that will play for this container.
        /// Includes creating the video and source elements if they don't already exist.
        /// Does not yet handle setting to an empty url and restoring the bloom-noVideoSelected state.
        /// </summary>
        public static void SetVideoElementUrl(
            XmlElement videoContainer,
            UrlPathString url,
            bool urlEncode = true
        )
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
                srcElement.SetAttribute("type", XmlString.FromUnencoded("video/mp4").Unencoded);
            }
            SetSrcOfVideoElement(url, srcElement, urlEncode);
            // Hides the placeholder.

            videoContainer.SetAttribute(
                "class",
                XmlString
                    .FromUnencoded(
                        RemoveClass("bloom-noVideoSelected", videoContainer.GetAttribute("class"))
                    )
                    .Unencoded
            );
        }

        /// <summary>
        /// Set the src attribute of the element to the path indicated by the url argument, optionally
        /// adding the supplied params. If urlEncodePath is true, we will take the encoded path version
        /// of the url argument. Caller is responsible to encode the paramString if necessary.
        /// </summary>
        public static void SetSrcOfVideoElement(
            UrlPathString url,
            XmlElement srcElement,
            bool urlEncodePath,
            string encodedParamString = ""
        )
        {
            if (encodedParamString == null)
                encodedParamString = "";
            if (!String.IsNullOrEmpty(encodedParamString) && !(encodedParamString.StartsWith("?")))
                encodedParamString = "?" + encodedParamString;
            string srcUrl =
                (urlEncodePath ? url.PathOnly.UrlEncodedForHttpPath : url.PathOnly.NotEncoded)
                + encodedParamString;
            srcElement.SetAttribute("src", XmlString.FromUnencoded(srcUrl).Unencoded);
        }

        public static string RemoveClass(string className, string input)
        {
            // We can remove the class name with a following space at the start,
            // the class name with a preceding space and following word boundary anywhere,
            // or just the class name itself if that's all there is.
            return new Regex(
                "(^" + className + " | " + className + "\\b|^" + className + "$)"
            ).Replace(input, "");
        }

        public static void RemoveClass(XmlElement element, string classNameToRemove)
        {
            string classAttributeValue = element?.Attributes["class"]?.Value;
            if (classAttributeValue == null)
            {
                return;
            }

            var classes = classAttributeValue
                .Split(HtmlDom.kHtmlClassDelimiters, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
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

        /// <summary>
        /// Returns all file names for background music which are referenced in the DOM.
        /// This should include items from the data div.
        /// </summary>
        public IEnumerable<string> GetBackgroundMusicFileNamesReferencedInBook()
        {
            return GetAudioSourceIdentifiers(
                    HtmlDom.SelectChildBackgroundMusicElements(RawDom.DocumentElement)
                )
                .Where(AudioProcessor.HasBackgroundMusicFileExtension)
                .Select(BookStorage.GetNormalizedPathForOS);
        }

        /// <summary>
        /// Could be simply an ID without an extension (as for narration)
        /// or an actual file name (as for background music)
        /// </summary>
        private static List<string> GetAudioSourceIdentifiers(XmlNodeList nodeList)
        {
            return (
                from XmlElement audio in nodeList
                select HtmlDom.GetAudioElementUrl(audio).PathOnly.NotEncoded
            )
                .Distinct()
                .ToList();
        }

        public static IEnumerable<XmlElement> SelectRealChildNarrationAudioElements(
            XmlElement element,
            bool includeSplitTextBoxAudio,
            IEnumerable<string> langsToExclude = null
        )
        {
            return SelectChildNarrationAudioElements(
                    element,
                    includeSplitTextBoxAudio,
                    langsToExclude
                )
                .Cast<XmlElement>()
                .Where(e => e.ParentOrSelfWithClass("bloom-editable") != null);
        }

        /// <summary>
        /// Note that this currently includes elements with this class in the data-div
        /// </summary>
        public static XmlNodeList SelectChildNarrationAudioElements(
            XmlElement element,
            bool includeSplitTextBoxAudio,
            IEnumerable<string> langsToExclude = null
        )
        {
            string xPathToEditable = "";
            if (langsToExclude != null && langsToExclude.Any())
            {
                var langXPath = String.Join(
                    " and ",
                    langsToExclude.Select(lang => $"@lang!='{lang}'")
                );
                xPathToEditable =
                    $"descendant-or-self::div[contains(concat(' ', @class, ' '), ' bloom-editable ') and {langXPath}]/";
            }

            string xPathToAudioSentence =
                xPathToEditable
                + "descendant-or-self::node()[contains(concat(' ', @class, ' '), ' audio-sentence ')]";
            string xPath = xPathToAudioSentence;
            if (includeSplitTextBoxAudio)
            {
                // This will select bloom-editables (i.e. text boxes) recorded in TextBox mode which might not be audio-sentences, but do contain descendants which are.
                // (That is, find cases where RecordingMode=TextBox but PlaybackMode=Sentence, a.k.a. the result of a Hard Split.
                xPath +=
                    $" | {xPathToAudioSentence}/ancestor::div[contains(concat(' ', @class, ' '), ' bloom-editable ') and @data-audiorecordingmode='TextBox']";
            }
            return element.SelectNodes(xPath);
        }

        public static XmlNodeList SelectChildBackgroundMusicElements(XmlElement element)
        {
            return element.SelectNodes(
                ".//div[@data-backgroundaudio and string-length(@data-backgroundaudio)!=0]"
            );
        }

        public static XmlNodeList SelectChildVideoElements(XmlElement element)
        {
            return element.SelectNodes(".//div[contains(@class,'bloom-videoContainer')]");
        }

        public static XmlNodeList SelectChildVideoSourceElements(XmlElement element)
        {
            return element.SafeSelectNodes(
                ".//div[contains(@class,'bloom-videoContainer')]/video/source"
            );
        }

        public IEnumerable<string> GetAllVideoPaths()
        {
            var paths = new List<string>();
            foreach (
                var source in SelectChildVideoSourceElements(RawDom.DocumentElement)
                    .Cast<XmlElement>()
            )
            {
                var src = source.GetAttribute("src");
                if (!String.IsNullOrEmpty(src))
                    paths.Add(src);
            }
            return paths;
        }

        private static readonly string kAudioSentenceElementsXPath =
            "descendant-or-self::node()[contains(@class,'audio-sentence') and string-length(@id) > 0]";

        public static XmlNodeList SelectRecordableDivOrSpans(XmlElement element)
        {
            string xpath1 = kAudioSentenceElementsXPath;
            // We only want the ones with ids, so add check for string-length(@id)
            string xpath2 =
                "descendant-or-self::node()[contains(@class,'bloom-editable') and not(contains(@class,'bloom-noAudio'))  and string-length(@id) > 0 ]";
            // Also add on any other spans. (e.g., bloom-highlight segments, which won't be matched by xpath1.
            string xpath3 =
                "descendant-or-self::node()[contains(@class,'bloom-editable') and not(contains(@class,'bloom-noAudio'))]//span[string-length(@id) > 0]";
            return element.SafeSelectNodes($"{xpath1}|{xpath2}|{xpath3}");
        }

        public static XmlNodeList SelectAudioSentenceElements(XmlElement element)
        {
            // It's unexpected for a book to have nodes with class audio-sentence and no id to link them to a file, but
            // if they do occur, it's better to ignore them than for other code to crash when looking for the ID.
            return element.SafeSelectNodes(kAudioSentenceElementsXPath);
        }

        public IEnumerable<XmlElement> GetRecordedAudioSentences(string bookFolder)
        {
            return HtmlDom
                .SelectAudioSentenceElements(RawDom.DocumentElement)
                .Cast<XmlElement>()
                .Where(
                    span =>
                        AudioProcessor.DoesAudioExistForSegment(
                            bookFolder,
                            span.Attributes["id"]?.Value
                        )
                );
        }

        public static XmlNodeList SelectAudioSentenceElementsWithDataDuration(XmlElement element)
        {
            return element.SafeSelectNodes(
                "descendant-or-self::node()[contains(@class,'audio-sentence') and @data-duration]"
            );
        }

        public static XmlNodeList SelectAudioSentenceElementsWithRecordingMd5(XmlElement element)
        {
            return element.SafeSelectNodes(
                "descendant-or-self::node()[@recordingmd5 and (contains(@class,'audio-sentence') or contains(@class,'bloom-highlightSegment'))]"
            );
        }

        public static bool HasAudioSentenceElementsWithoutId(XmlElement element)
        {
            // It's unexpected for a book to have nodes with class audio-sentence and no id to link them to a file, but
            // if they do occur, it's better to ignore them than for other code to crash when looking for the ID.
            var nodes = element.SafeSelectNodes(
                "descendant-or-self::node()[contains(@class,'audio-sentence') and string-length(@id) = 0]"
            );
            return nodes?.Count >= 1;
        }

        public bool HasMotionPages()
        {
            var nodes = _dom.SafeSelectNodes("//*[@data-initialrect]");
            return nodes?.Count >= 1;
        }

        public bool HasQuizPages()
        {
            return HasQuizPageContent(_dom.DocumentElement);
        }

        public static bool HasQuizPageContent(XmlElement element)
        {
            // Current style comprehension quiz pages
            if (
                element
                    .SafeSelectNodes(".//*[contains(@class, 'simple-comprehension-quiz')]")
                    ?.Count >= 1
            )
                return true;
            // Legacy style comprehension quiz pages
            return element.SafeSelectNodes(".//*[contains(@class, 'questions')]")?.Count >= 1;
        }

        // "Widgets" are HTML Activities that the user creates outside of Bloom, as distinct from our built-in activities.
        public bool HasWidgetPages()
        {
            var nodes = _dom.SafeSelectNodes("//*[@data-activity='iframe']");
            return nodes?.Count >= 1;
        }

        // "simple-dom-choice" are simple built-in activities where the prompt is a text
        // or an image, and the choices are a text or a image.
        public bool HasSimpleDomChoicePages()
        {
            var nodes = _dom.SafeSelectNodes("//*[@data-activity='simple-dom-choice']");
            return nodes?.Count >= 1;
        }

        // An Activity can be either a user-supplied html widget or a built-in interactive page like quizzes or multiple-choice pages
        public bool HasActivityPages()
        {
            // I think the test for bloom-interactivePage is sufficient for all current activity pages,
            // but for compatibility with legacy books (and in case I'm wrong) I'm keeping the other checks.
            // This test should be consistent with the isActivity method in bloom player's bloom-player-core code.
            return _dom.SelectSingleNode("//div[contains(@class, 'bloom-interactive-page')]")
                    != null
                || HasQuizPages()
                || HasWidgetPages();
        }

        public static bool IsActivityPage(XmlElement pageElement)
        {
            var classes = pageElement.GetAttribute("class");
            // I'd say it's impossible for this to be empty or null, but...
            Debug.Assert(!string.IsNullOrEmpty(classes), "How did we get a page with no classes!?");
            // The class is for 4.6, the attribute is for later versions.
            if (
                classes.Contains("bloom-interactive-page")
                || pageElement.HasAttribute("data-activity")
            )
                return true;

            return HasQuizPageContent(pageElement);
        }

        public bool HasOverlayPages()
        {
            // This feature is entirely determined by the presence of an SVG image generated by the comical.js npm package.
            // The actual feature name stored in the meta.json file remains "comic" for backwards compatibility with
            // older versions of Bloom, the Parse database, and Bloom Library.
            return _dom.SelectSingleNode(BookStorage.ComicalXpath) != null;
        }

        public bool HasImageDescriptions =>
            _dom.SafeSelectNodes("//div[contains(@class, 'bloom-imageDescription')]")
                .Cast<XmlElement>()
                // This is needed because it's possible to get elements with the class just by taking a look
                // at the tool, without ever actually creating an image description.
                .Any(x => !string.IsNullOrWhiteSpace(x.InnerText));

        public XmlNodeList SelectVideoSources()
        {
            return RawDom.SafeSelectNodes(
                "//div[contains(@class, 'bloom-videoContainer')]//source"
            );
        }

        public static IEnumerable<XmlElement> GetWidgetIframes(XmlElement element)
        {
            return element
                .SafeSelectNodes(".//div[contains(@class, 'bloom-widgetContainer')]//iframe")
                .Cast<XmlElement>();
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
            var langDivs = pageElements
                .SelectMany(
                    page =>
                        page.SafeSelectNodes(
                                ".//*[contains(@class, 'bloom-imageDescription')]/div[@lang]"
                            )
                            .Cast<XmlElement>()
                )
                .ToList();

            var langCodes = langDivs
                .Where(node => !String.IsNullOrWhiteSpace(node.InnerText)) // Note that it is common for InnerText to contain whitespace like "\r\n"
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
            return element
                    .SelectNodes("self::img | self::*[contains(@style,'background-image')]")
                    .Count == 1;
        }

        public static XmlElement GetOrCreateDataDiv(XmlNode dom)
        {
            var dataDiv = dom.SelectSingleNode("//div[@id='bloomDataDiv']") as XmlElement;
            if (dataDiv == null)
            {
                XmlDocument doc = dom as XmlDocument;
                if (doc == null)
                    doc = dom.OwnerDocument;
                dataDiv = doc.CreateElement("div");
                dataDiv.SetAttribute("id", "bloomDataDiv");
                dom.SelectSingleNode("//body").InsertAfter(dataDiv, null);
            }
            return dataDiv;
        }

        /// <summary>
        /// Add to targetBookDom a reference to any stylesheet that sourceBookDom
        /// refers to and which it does not already refer to. Return true if anything
        /// was added.
        /// </summary>
        /// <returns></returns>
        public static bool AddStylesheetFromAnotherBook(
            HtmlDom sourceBookDom,
            HtmlDom targetBookDom
        )
        {
            var addedModifiedStyleSheets = new List<string>();
            //This was refactored from book, where there was these notes:
            //     NB: at this point this code can't handle the "userModifiedStyles" from children, it'll ignore them (they would conflict with each other)
            //     NB: at this point custom styles (e.g. larger/smaller font rules) from children will be lost.
            bool anyAdded = false;

            //At this point, this addedModifiedStyleSheets is just used as a place to track which stylesheets we already have
            foreach (string sheetName in sourceBookDom.GetTemplateStyleSheets())
            {
                if (!addedModifiedStyleSheets.Contains(sheetName))
                //nb: if two books have stylesheets with the same name, we'll only be grabbing the 1st one.
                {
                    addedModifiedStyleSheets.Add(sheetName);
                    anyAdded |= targetBookDom.AddStyleSheetIfMissing(sheetName);
                }
            }
            return anyAdded;
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
                    "/html/body/div[contains(@class,'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]"
                )
                .OfType<XmlElement>();
        }

        public IEnumerable<XmlElement> GetPageElements()
        {
            return _dom.SafeSelectNodes("/html/body/div[contains(@class,'bloom-page')]")
                .OfType<XmlElement>();
        }

        /// <summary>
        /// Can switch a page from being a template page or back to a normal page.
        /// </summary>
        /// <param name="areTemplatePages"></param>
        public void MarkPagesWithTemplateStatus(bool areTemplatePages)
        {
            foreach (var page in GetContentPageElements())
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

        /// <summary>
        /// Given a page or a child of a page, return the page
        /// </summary>
        private XmlElement GetPageDivOfElement(XmlElement element)
        {
            return element.SelectSingleNode("ancestor-or-self::div[contains(@class,'bloom-page')]")
                as XmlElement;
        }

        /// <summary>
        /// Figure out what page number would be shown on the page
        /// </summary>
        public int GetPageNumberOfPage(XmlElement pageDiv)
        {
            var allPages = RawDom
                .SelectNodes("html/body/div[contains(@class,'bloom-page')]")
                .Cast<XmlElement>();

            var pageNumber = 0;
            foreach (var p in allPages)
            {
                if (
                    HtmlDom.HasClass(p, "numberedPage")
                    || HtmlDom.HasClass(p, "countPageButDoNotShowNumber")
                )
                {
                    ++pageNumber;
                }
                if (HtmlDom.HasClass(p, "bloom-startPageNumbering"))
                {
                    pageNumber = 1;
                }
                if (p == pageDiv)
                    return pageNumber;
            }
            return -1;
        }

        public XmlElement FindPageById(string pageId)
        {
            return RawDom
                .SafeSelectNodes("//body/div[@id='" + pageId + "']")
                .Cast<XmlElement>()
                .FirstOrDefault();
        }

        /// <summary>
        /// Updates the side-right and side-left classes of every page div in the supplied dom.
        /// These will only be correct if the dom is the full book; from a dom made of one page,
        /// we cannot determine if it is left or right, nor what page.
        /// </summary>
        public void UpdatePageNumberAndSideClassOfPages(
            string charactersForDigits,
            bool languageIsRightToLeft
        )
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
                var numberInScript =
                    (number > 0 && HtmlDom.HasClass(pageDiv, "numberedPage"))
                        ? GetNumberStringRepresentation(number, charactersForDigits)
                        : "";
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
            if (String.IsNullOrEmpty(charactersForDigits))
                return postiveInteger.ToString(CultureInfo.InvariantCulture);

            // normal charactersForDigits.length gives 20 for chakma's 10 characters... I gather because it is converted to utf 16  and then
            // those bytes are counted? Here's all the info:
            // "In short, the length of a string is actually a ridiculously complex question and calculating it can take a lot of CPU time as well as data tables."
            // https://stackoverflow.com/questions/26975736/why-is-the-length-of-this-string-longer-than-the-number-of-characters-in-it
            var infoOnDigitsCharacters = new StringInfo(charactersForDigits);
            Debug.Assert(infoOnDigitsCharacters.LengthInTextElements == 10);

            return String.Join(
                "",
                postiveInteger
                    .ToString(CultureInfo.InvariantCulture)
                    .Select(x =>
                    {
                        if ("1234567890".Contains(x.ToString()))
                            return infoOnDigitsCharacters.SubstringByTextElements(x - '0', 1);
                        else
                            return x.ToString();
                    })
            );
        }

        private static void UpdateSideClass(
            XmlElement pageDiv,
            int indexOfPageZeroBased,
            bool languageIsRightToLeft
        )
        {
            RemoveClassesBeginingWith(pageDiv, "side-");
            var rightSideRemainder = languageIsRightToLeft ? 1 : 0;
            AddClass(
                pageDiv,
                indexOfPageZeroBased % 2 == rightSideRemainder ? "side-right" : "side-left"
            );
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

        public static string GetNumberOrLabelOfPageWhereElementLives(
            XmlElement childElement,
            IEnumerable<string> langs = null
        )
        {
            var pageElement =
                childElement.SelectSingleNode(
                    "ancestor-or-self::div[contains(@class,'bloom-page')]"
                ) as XmlElement;
            if (pageElement == null)
            {
#if DEBUG
                throw new ApplicationException("Don't feed non-page images to this method!");
#else
                return "unknown";
#endif
            }
            // optional because unit tests might be missing data-page-number
            var pageNumber = pageElement.GetOptionalStringAttribute("data-page-number", "unknown");
            if (
                // front matter won't have a pageNumber
                String.IsNullOrWhiteSpace(pageNumber)
                // back matter pages actually have page numbers (maybe that's an oversight in some other code?)
                // but it's clearer to just call them by name.
                || HtmlDom.IsBackMatterPage(pageElement)
            )
            {
                var labelNode = pageElement.SelectSingleNode("./div[@class='pageLabel']");
                return LocalizePageLabel(labelNode?.InnerText.Trim(), langs);
            }
            return pageNumber;
        }

        private static string LocalizePageLabel(string label, IEnumerable<string> langs)
        {
            if (langs == null || String.IsNullOrEmpty(label))
                return label;
            var id = "TemplateBooks.PageLabel." + label;
            return LocalizationManager.GetString(id, label, "", langs, out _);
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
                var description = parent.ChildNodes
                    .Cast<XmlNode>()
                    .FirstOrDefault(
                        n => n is XmlElement && HasClass((XmlElement)n, "bloom-imageDescription")
                    );
                if (description != null)
                {
                    foreach (var node in description.ChildNodes)
                    {
                        var editable = node as XmlElement;
                        if (editable == null)
                            continue;
                        if (!HasClass(editable, "bloom-editable"))
                            continue;
                        if (
                            editable.Attributes["lang"] == null
                            || editable.Attributes["lang"].Value != descriptionLang
                        )
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
            string localizedFormatString = LocalizationManager.GetString(
                "EditTab.Image.AltMsg",
                "This picture, {0}, is missing or was loading too slowly."
            );
            string localizedString = String.Format(
                localizedFormatString,
                imageElement.GetAttribute("src")
            );

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
                    localizedSubstring = localizedFormatString.Substring(
                        0,
                        indexOfFormatReplacement
                    );
                }
                else
                {
                    localizedSubstring = localizedFormatString.Substring(
                        indexOfFormatReplacement + "{0}".Length
                    );
                }

                return altText.Contains(
                    localizedSubstring,
                    StringComparison.CurrentCultureIgnoreCase
                );
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
            if (!String.IsNullOrWhiteSpace(styleString))
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
            FixDivOrdering(
                RawDom.DocumentElement
                    .SelectNodes("//div[contains(@class, 'translationGroup')]")
                    .Cast<XmlElement>()
            );
        }

        private static void FixDivOrdering(IEnumerable<XmlElement> nodeList)
        {
            foreach (var multilingualDiv in nodeList)
            {
                var divs = multilingualDiv
                    .SelectNodes("./div[contains(@class, 'bloom-content')]")
                    .Cast<XmlElement>()
                    .ToList();
                divs.Sort(CompareMultilingualDivs);
                for (var i = divs.Count - 1; i >= 1; --i)
                    multilingualDiv.InsertBefore(divs[i - 1], divs[i]);
            }
        }

        private static int CompareMultilingualDivs(XmlElement x, XmlElement y)
        {
            string xKey = ExtractKeyForMultilingualDivs(x);
            string yKey = ExtractKeyForMultilingualDivs(y);
            return String.Compare(xKey, yKey, StringComparison.Ordinal);
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
            bool isMatch = DoesSelfOrAncestorMatchCondition(
                node,
                n =>
                {
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
                }
            );

            return isMatch;
        }

        public static bool DoesSelfOrAncestorMatchCondition(
            XmlNode node,
            Func<XmlNode, bool> matcher
        )
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

        /// <summary>
        /// Returns the first node, starting from {startNode} and going up toward the earliest ancestor, that matchesthe condition (i.e. the provided {matcher} function returns true
        /// </summary>
        /// <param name="startNode">The XmlNode to start the search from. The search is inclusive.</param>
        /// <param name="matcher">A function that, given a node in the tree, returns true if it matches the desired condition</param>
        /// <returns></returns>
        public static XmlNode FindSelfOrAncestorMatchingCondition(
            XmlNode startNode,
            Func<XmlNode, bool> matcher
        )
        {
            if (startNode == null)
            {
                return null;
            }

            var ancestor = startNode;

            while (ancestor != null)
            {
                if (matcher(ancestor))
                {
                    return ancestor;
                }

                ancestor = ancestor.ParentNode;
            }

            return null;
        }

        public static void InsertFullBleedMarkup(XmlElement body)
        {
            AddClass(body, "bloom-fullBleed");
            foreach (
                XmlElement page in body.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
                    .Cast<XmlElement>()
                    .ToArray()
            )
            {
                var mediaBoxDiv = page.OwnerDocument.CreateElement("div");
                (page.ParentNode as XmlElement).ReplaceChild(mediaBoxDiv, page);
                mediaBoxDiv.AppendChild(page);
                // In an ideal world, the page size class would be on the body, and style rules for
                // the media box could simply use it. As it is, the page size class is on the page,
                // and to write style rules that use it, we need to copy it to the new container.
                var pageSizeClass = page.Attributes["class"].Value
                    .Split(' ')
                    .First(x => x.EndsWith("Portrait") || x.EndsWith("Landscape"));
                mediaBoxDiv.SetAttribute("class", $"bloom-mediaBox {pageSizeClass}");
            }
        }

        public static string NumberOfPage(XmlElement page)
        {
            return (page?.Attributes["data-page-number"]?.Value) ?? "";
        }

        /// <summary>
        /// Remove from the argument (presumed to be a translation group) any children not in the list
        /// of languages to keep.
        /// </summary>
        public static void RemoveOtherLanguages(XmlElement group, List<string> languagesToKeep)
        {
            var deletableChildren = @group.ChildNodes
                .Cast<XmlNode>()
                .Where(
                    x =>
                        x is XmlElement e
                        && (e.Attributes["class"]?.Value ?? "").Contains("bloom-editable")
                )
                .Cast<XmlElement>()
                .ToList();

            RemoveOtherLanguages(deletableChildren, group, languagesToKeep);
        }

        /// <summary>
        /// Remove from the parent any of the listed children which are not in the list
        /// of languages to keep.
        /// </summary>
        public static void RemoveOtherLanguages(
            List<XmlElement> deletableChildren,
            XmlElement parent,
            List<string> languagesToKeep
        )
        {
            foreach (var element in deletableChildren)
            {
                var lang = element.Attributes["lang"]?.Value;
                if (lang == "z")
                    continue;
                if (!languagesToKeep.Contains(lang))
                    parent.RemoveChild(element);
            }
        }

        public static XmlElement GetEditableChildInLang(XmlElement parent, string lang)
        {
            return parent.ChildNodes
                    .Cast<XmlNode>()
                    .FirstOrDefault(
                        x =>
                            x is XmlElement e
                            && (lang == null || e.Attributes["lang"]?.Value == lang)
                            && (e.Attributes["class"]?.Value ?? "").Contains("bloom-editable")
                    ) as XmlElement;
        }

        // Note, this doesn't do XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml()
        // which would be needed if we were going to come back to xml at some point.
        public string getHtmlStringDisplayOnly()
        {
            var output = new StringBuilder();
            using (var writer = XmlWriter.Create(output, GetXmlWriterSettingsForHtml5()))
            {
                this.RawDom.WriteContentTo(writer);
                writer.Close();
            }

            return CleanupHtml5(output.ToString());
        }

        // xml output will produce things like <title /> or <div /> for empty elements, which are not valid HTML 5 and produce
        // weird results; for example, the browser interprets <title /> as the beginning of an element that is not terminated
        // until the end of the whole document. Thus, everything becomes part of the title. This then causes errors in our
        // thumbnail generation because gecko thinks the document has an empty  body (the real one is lost inside the title).
        // Also, embedded controls (like ReaderTools.htm) now pass through this xml-to-html conversion, and this file contains
        // several more kinds of empty element, some of which have attributes.
        // There are probably more elements than these which may not be empty. However we can't just use [^ ]* in place of title|div
        // because there are some elements that never have content like <br /> which should NOT be converted.
        // It seems safest to just list the ones that can occur empty in Bloom...if we can't find a more reliable way to convert to HTML5.
        private static string CleanupHtml5(string xhtml)
        {
            var re = new Regex("<(title|div|i|table|td|span|style|script|textarea) ([^<]*)/>");
            xhtml = re.Replace(xhtml, "<$1 $2></$1>");
            //now insert the non-xml-ish <!doctype html>
            return string.Format("<!DOCTYPE html>{0}{1}", Environment.NewLine, xhtml);
        }

        /// <summary>
        /// Return the settings that should be used for an XmlWriter to write a DOM as HTML5.
        /// (The writer results should then be passed through CleanupHtml5.)
        /// </summary>
        /// <returns></returns>
        private static XmlWriterSettings GetXmlWriterSettingsForHtml5()
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.CheckCharacters = true;
            settings.OmitXmlDeclaration = true; //we're aiming at normal html5, here. Not xhtml.
            //CAN'T DO THIS: settings.OutputMethod = XmlOutputMethod.Html; // JohnT: someone please explain why not?
            return settings;
        }

        public IEnumerable<XmlAttribute> GetBodyAttributesThatMayAffectDisplay()
        {
            //example: [(data-bookshelfurlkey, "kyrgyzstan2020-grade2")]
            return this.Body.Attributes.Cast<XmlAttribute>().Where(a => a.Name.StartsWith("data-"));
        }

        internal static void AddMissingAudioHighlightRules(XmlElement userStylesNode)
        {
            var userStyleKeyDict = GetUserStyleKeyDict(userStylesNode);
            var rulesToCheck = new HashSet<string>();
            var updatedRule = false;
            foreach (var key in userStyleKeyDict.Keys)
            {
                if (key.EndsWith(" span.ui-audioCurrent"))
                    rulesToCheck.Add(key);
            }
            foreach (var key in rulesToCheck)
            {
                var newKey = key + " > span.ui-enableHighlight";
                if (!userStyleKeyDict.Keys.Contains(newKey))
                {
                    // The value includes the key, so we can't just add a new key with the old value.
                    var value = userStyleKeyDict[key].Replace(
                        ".ui-audioCurrent {",
                        ".ui-audioCurrent > span.ui-enableHighlight {"
                    );
                    userStyleKeyDict.Add(newKey, value);
                    updatedRule = true;
                }
            }
            if (updatedRule)
            {
                // Update the DOM to reflect the changes.
                var rawCSS = GetCompleteFilteredUserStylesInnerText(userStyleKeyDict);
                userStylesNode.InnerXml = WrapUserStyleInCdata(rawCSS);
            }
        }

        public bool HasClassOnBody(string className)
        {
            return HasClass(Body, className);
        }

        public void SetClassOnBody(bool shouldHaveClass, string className)
        {
            if (Body == null)
                return;

            if (shouldHaveClass && !HasClassOnBody(className))
            {
                AddClass(Body, className);
            }
            else if (!shouldHaveClass && HasClassOnBody(className))
            {
                RemoveClass(Body, className);
            }
        }
    }
}
