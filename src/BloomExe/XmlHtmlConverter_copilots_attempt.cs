using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.SafeXml;
using Bloom.Utils;
using SIL.IO;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Bloom
{
    public static class XmlHtmlConverter
    {
        public const string CdataPrefix = "/*<![CDATA[*/";
        public const string CdataSuffix = "/*]]>*/";

        private const string SvgPlaceholder = "****RestoreSvgHere****";

        private static readonly Regex _selfClosingRegex = new Regex(
            @"<([ubi]|em|strong|span)(\s+[^><]+\s*)/>"
        );
        private static readonly Regex _emptySelfClosingElementsToRemoveRegex = new Regex(
            @"<([ubi]|em|strong|span)\s*/>"
        );
        private static readonly Regex _emptyElementsWithAttributesRegex = new Regex(
            @"<([ubi]|em|strong|span|cite)(\s+[^><]+\s*)>(\s*)</\1>"
        );
        private static readonly Regex _inlineElementsWithLeadingSpaceRegex = new Regex(
            @"<([ubi]|em|strong|span|cite)([^>]*)> "
        );
        private static readonly Regex _inlineElementsWithTrailingSpaceRegex = new Regex(
            @" </([ubi]|em|strong|span|cite)>"
        );
        private static readonly Regex _emptyElementsToPreserveRegex = new Regex(
            @"<(p|cite)\s*>(\s*)</\1>"
        );
        private static readonly Regex _selfClosingElementsToPreserveRegex = new Regex(
            @"<(p|cite)(\s+[^><]*\s*)/>"
        );

        public static SafeXmlDocument GetXmlDomFromHtmlFile(
            string path,
            bool includeXmlDeclaration = false
        )
        {
            return GetXmlDomFromHtml(RobustFile.ReadAllText(path), includeXmlDeclaration);
        }

        /// <summary></summary>
        /// <param name="content"></param>
        /// <param name="includeXmlDeclaration"></param>
        /// <exception>Throws if there are parsing errors</exception>
        /// <returns></returns>
        public static SafeXmlDocument GetXmlDomFromHtml(
            string content,
            bool includeXmlDeclaration = false
        )
        {
            var dom = SafeXmlDocument.Create();
            content = AddFillerToKeepTidyFromRemovingEmptyElementsAndWhiteSpace(content);

            //in BL-2250, we found that in previous versions, this method would return, for example, "<u> </u>" REMOVEWHITESPACE.
            //That is fixed now, but this is needed to give to clean up existing books.
            content = content.Replace(@"REMOVEWHITESPACE", "");

            // tidy likes to insert newlines before <b>, <u>, <i>, and these other elements and convert any existing whitespace
            // there to a space.  (<span...> was found by pursuing BL-7558, <a...> from BL-12248)
            content = new Regex(@"<([ubi]|em|strong|sup|sub|span[^>]*|a[^>]*)>").Replace(
                content,
                "REMOVEWHITESPACE<$1>"
            );

            // fix for <br></br> tag doubling
            content = content.Replace("<br></br>", "<br />");

            // fix for > and similar in <style> element protected by CDATA.
            // At present we only need to account for this occurring once.
            // See Browser.SaveCustomizedCssRules.
            var startOfCdata = content.IndexOf(CdataPrefix, StringComparison.InvariantCulture);
            const string restoreCdataHere = "/****RestoreCDATAHere*****/";
            var endOfCdata = content.IndexOf(CdataSuffix, StringComparison.InvariantCulture);
            var savedCdata = "";
            if (startOfCdata >= 0 && endOfCdata >= startOfCdata)
            {
                endOfCdata += CdataSuffix.Length;
                savedCdata = content.Substring(startOfCdata, endOfCdata - startOfCdata);
                content =
                    content.Substring(0, startOfCdata)
                    + restoreCdataHere
                    + content.Substring(endOfCdata, content.Length - endOfCdata);
            }

            var removedSvgs = new List<string>();
            content = RemoveSvgs(content, removedSvgs);

            // Hand editing HTML files can sometimes, with some editors, introduce a BOM after the first character in the HTML file.
            // These can cause problems in creating PDFs (see https://issues.bloomlibrary.org/youtrack/issue/BL-11127).
            // Either having a BOM or not having a BOM as the first character in the file doesn't really matter.  (File storage tends
            // to use UTF-8, which doesn't need BOMs.)  And the tidy processing below strips an initial BOM anyway, so it doesn't
            // hurt to do it here if it exists.  (Starting the search after the first character is perhaps a trivial optimization.)
            if (!String.IsNullOrEmpty(content) && content.IndexOf('\uFEFF', 1) > 0)
                content = content.Replace("\uFEFF", "");

            try
            {
                // Use AngleSharp to parse HTML and convert to XML
                var config = Configuration.Default.WithDefaultLoader();
                using var context = BrowsingContext.New(config);
                var parser = context.GetService<IHtmlParser>();
                var document = parser.ParseDocument(content);

                // Apply similar transformations that TidyManaged used to do
                var newContents = ProcessAngleSharpDocument(document, removedSvgs, savedCdata, restoreCdataHere);

                // Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
                dom.PreserveWhitespace = true;
                dom.LoadXml(newContents);
            }
            catch (Exception error)
            {
                var exceptionWithHtmlContents = new Exception(
                    string.Format(
                        "{0}{2}{2}{1}",
                        error.Message,
                        content,
                        Environment.NewLine
                    ),
                    innerException: error
                );
                throw exceptionWithHtmlContents;
            }

            //this is a hack... each time we write the content, we add a new <meta http-equiv="Content-Type" content="text/html; charset=utf-8">
            //so for now, we remove it when we read it in. It'll get added again when we write it out
            RemoveAllContentTypesMetas(dom);

            return dom;
        }

        /// <summary>
        /// Process AngleSharp document to produce XML similar to TidyManaged output
        /// </summary>
        private static string ProcessAngleSharpDocument(IHtmlDocument document, List<string> removedSvgs, string savedCdata, string restoreCdataHere)
        {
            // Apply cleanup transformations similar to TidyManaged
            CleanupAngleSharpDocument(document);
            
            // Convert to XML-like string
            var newContents = ConvertAngleSharpDocumentToXml(document);
            
            // Apply the same post-processing that TidyManaged output received
            newContents = RestoreSvgs(newContents, removedSvgs);
            newContents = RemoveFillerInEmptyElements(newContents);

            newContents = newContents.Replace("&nbsp;", "&#160;");
            //REVIEW: 1) are there others? &amp; and such are fine.  2) shoul we to convert back to &nbsp; on save?

            // The regex here is mainly for the \s* as a convenient way to remove whatever whitespace TIDY
            // has inserted. It's a fringe benefit that we can use the[biu]|... to deal with all these elements in one replace.
            newContents = Regex.Replace(
                newContents,
                @"REMOVEWHITESPACE\s*<([biu]|em|strong|sup|sub|span[^>]*|a[^>]*)>",
                "<$1>"
            );

            //In BL2250, we still had REMOVEWHITESPACE sticking around sometimes. The way we reproduced it was
            //with <u> </u>. That is, we started with
            //"REMOVEWHITESPACE <u> </u>", then libtidy (properly) removed the <u></u>, leaving us with only
            //"REMOVEWHITESPACE".
            newContents = Regex.Replace(newContents, @"REMOVEWHITESPACE", "");

            // remove blank lines at the end of style blocks
            newContents = Regex.Replace(newContents, @"\s+<\/style>", "</style>");

            // remove <br> elements immediately preceding </p> close tag (BL-2557)
            newContents = Regex.Replace(
                newContents,
                @"(<br></br>|<br ?/>)[\r\n]*</p>",
                "</p>"
            );

            newContents = newContents.Replace(restoreCdataHere, savedCdata);

            return newContents;
        }

        /// <summary>
        /// Apply cleanup transformations to AngleSharp document
        /// </summary>
        private static void CleanupAngleSharpDocument(IHtmlDocument document)
        {
            // Remove empty inline elements without attributes (similar to TidyManaged behavior)
            RemoveEmptyInlineElements(document);
            
            // Ensure textarea, div, span, etc. are never completely empty
            EnsureNonEmptyElements(document);
        }

        /// <summary>
        /// Remove empty inline elements without attributes
        /// </summary>
        private static void RemoveEmptyInlineElements(IHtmlDocument document)
        {
            var emptyInlineElements = new[] { "u", "i", "b", "em", "strong", "span" };
            
            foreach (var tagName in emptyInlineElements)
            {
                var elements = document.QuerySelectorAll(tagName).ToList();
                foreach (var element in elements)
                {
                    // Only remove if empty and no attributes
                    if (string.IsNullOrEmpty(element.TextContent?.Trim()) && 
                        element.Attributes.Length == 0)
                    {
                        element.Remove();
                    }
                }
            }
        }

        /// <summary>
        /// Ensure certain elements are never completely empty to prevent browser issues
        /// </summary>
        private static void EnsureNonEmptyElements(IHtmlDocument document)
        {
            var elementsToEnsure = new[] { "textarea", "div", "p", "span", "cite", "script", "style", "iframe" };
            
            foreach (var tagName in elementsToEnsure)
            {
                var elements = document.QuerySelectorAll(tagName).ToList();
                foreach (var element in elements)
                {
                    if (string.IsNullOrEmpty(element.TextContent) && !element.HasChildNodes)
                    {
                        // Add a text node to prevent empty elements
                        var textContent = tagName == "script" || tagName == "style" ? " " : "";
                        element.AppendChild(document.CreateTextNode(textContent));
                    }
                }
            }
        }

        /// <summary>
        /// Convert AngleSharp document to XML string
        /// </summary>
        private static string ConvertAngleSharpDocumentToXml(IHtmlDocument document)
        {
            var xmlBuilder = new StringBuilder();
            ConvertElementToXml(document.DocumentElement, xmlBuilder, 0);
            return xmlBuilder.ToString();
        }

        /// <summary>
        /// Convert AngleSharp element to XML format
        /// </summary>
        private static void ConvertElementToXml(IElement element, StringBuilder xmlBuilder, int indentLevel)
        {
            if (element == null) return;
            
            var indent = new string(' ', indentLevel * 2);
            
            // Start tag
            xmlBuilder.Append('<');
            xmlBuilder.Append(element.TagName.ToLowerInvariant());
            
            // Add attributes
            foreach (var attr in element.Attributes)
            {
                xmlBuilder.Append(' ');
                xmlBuilder.Append(attr.Name);
                xmlBuilder.Append("=\"");
                xmlBuilder.Append(System.Web.HttpUtility.HtmlEncode(attr.Value));
                xmlBuilder.Append("\"");
            }
            
            // Check if element has children or text content
            var hasContent = element.HasChildNodes || !string.IsNullOrEmpty(element.TextContent?.Trim());
            
            if (!hasContent && IsVoidElement(element.TagName))
            {
                // Self-closing for void elements
                xmlBuilder.AppendLine(" />");
            }
            else if (!hasContent)
            {
                // Empty element - close immediately for XML compatibility
                xmlBuilder.Append("></");
                xmlBuilder.Append(element.TagName.ToLowerInvariant());
                xmlBuilder.AppendLine(">");
            }
            else
            {
                xmlBuilder.AppendLine(">");
                
                // Add children
                foreach (var child in element.ChildNodes)
                {
                    if (child is IElement childElement)
                    {
                        ConvertElementToXml(childElement, xmlBuilder, indentLevel + 1);
                    }
                    else if (child is IText textNode)
                    {
                        var text = textNode.TextContent;
                        if (!string.IsNullOrEmpty(text))
                        {
                            xmlBuilder.AppendLine(System.Web.HttpUtility.HtmlEncode(text));
                        }
                    }
                }
                
                // End tag
                xmlBuilder.Append("</");
                xmlBuilder.Append(element.TagName.ToLowerInvariant());
                xmlBuilder.AppendLine(">");
            }
        }

        /// <summary>
        /// Check if an element is a void element (self-closing in HTML)
        /// </summary>
        private static bool IsVoidElement(string tagName)
        {
            var voidElements = new HashSet<string>
            {
                "area", "base", "br", "col", "embed", "hr", "img", "input", 
                "link", "meta", "param", "source", "track", "wbr"
            };
            return voidElements.Contains(tagName.ToLowerInvariant());
        }

        /// <summary>
        /// Tidy is over-zealous. This is a work-around. After running Tidy, then call RemoveFillerInEmptyElements() on the same text
        /// </summary>
        /// <returns></returns>
        private static string AddFillerToKeepTidyFromRemovingEmptyElementsAndWhiteSpace(
            string content
        )
        {
            // This handles empty elements in the form of XML contractions like <i some-important-attributes />
            content = ConvertSelfClosingTags(content, "REMOVEME");

            // hack. Tidy deletes <span data-libray='somethingImportant'></span>
            // and also (sometimes...apparently only the first child in a parent) <i some-important-attributes></i>.
            // $1 is the tag name.
            // $2 is the tag attributes.
            // $3 is the blank space between the opening and closing tags, if any.
            content = _emptyElementsWithAttributesRegex.Replace(content, "<$1$2>REMOVEME$3</$1>");

            // Tidy deletes <p></p>, though that's obviously not something to delete!
            content = _emptyElementsToPreserveRegex.Replace(content, "<$1$2>REMOVEME</$1>");

            // Prevent Tidy from deleting <p/> too
            content = _selfClosingElementsToPreserveRegex.Replace(content, "<$1$2>REMOVEME</$1>");

            // And, finding something like <strong> bold </strong>, Tidy removes the leading space,
            // and moves the trailing one outside the element.
            content = _inlineElementsWithLeadingSpaceRegex.Replace(content, "<$1$2>REMOVEME ");
            content = _inlineElementsWithTrailingSpaceRegex.Replace(content, " REMOVEME</$1>");
            return content;
        }

        /// <summary>
        /// This is to be run after running tidy
        /// </summary>
        private static string RemoveFillerInEmptyElements(string contents)
        {
            return contents.Replace("REMOVEME", "").Replace("\0", "");
        }

        private static string ConvertSelfClosingTags(string html, string innerHtml = "")
        {
            html = RemoveEmptySelfClosingTags(html);

            // $1 is the tag name.
            // $2 is the tag attributes.
            return _selfClosingRegex.Replace(html, "<$1$2>" + innerHtml + "</$1>");
        }

        public static string RemoveEmptySelfClosingTags(string html)
        {
            return _emptySelfClosingElementsToRemoveRegex.Replace(html, "");
        }

        /// <summary>
        /// Beware... htmltidy doesn't consider such things as a second <body> element to warrant any more than a "warning", so this won't throw!
        /// </summary>
        /// <param name="content"></param>
        public static void ThrowIfHtmlHasErrors(string content)
        {
            // Tidy chokes on embedded svgs so just take them out. Here we don't use the removed svgs.
            var dummy = new List<string>();
            content = RemoveSvgs(content, dummy);

            try
            {
                // Create AngleSharp configuration
                var config = Configuration.Default.WithDefaultLoader();
                using var context = BrowsingContext.New(config);
                
                // Parse HTML with AngleSharp
                var parser = context.GetService<IHtmlParser>();
                var document = parser.ParseDocument(content);
                
                // AngleSharp is more forgiving than TidyManaged, so we need to do some basic validation
                // Check for unclosed tags or other structural issues
                ValidateHtmlStructure(document);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"HTML parsing error: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Validate HTML structure for basic errors
        /// </summary>
        private static void ValidateHtmlStructure(IHtmlDocument document)
        {
            // Basic validation - can be expanded as needed
            // For now, just ensure we have a valid document structure
            if (document.DocumentElement == null)
            {
                throw new ApplicationException("Invalid HTML: no document element found");
            }
        }

        /// <summary>
        /// If an element has empty contents, like <textarea></textarea>, browsers will sometimes drop the end tag, so that now, when we read it back into xml,
        /// anything following the <textarea> will be interpreted as part of the <textarea>!  This method makes sure such tags are never totally empty.
        /// </summary>
        /// <param name="dom"></param>
        public static void MakeXmlishTagsSafeForInterpretationAsHtml(SafeXmlDocument dom)
        {
            foreach (SafeXmlElement node in dom.SafeSelectNodes("//textarea"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }
            foreach (SafeXmlElement node in dom.SafeSelectNodes("//div"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (SafeXmlElement node in dom.SafeSelectNodes("//p"))
            //without  this, an empty paragraph suddenly takes over the subsequent elements. Browser sees <p></p> and thinks... let's just make it <p>, shall we? Stupid optional-closing language, html is....
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (SafeXmlElement node in dom.SafeSelectNodes("//span"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (SafeXmlElement node in dom.SafeSelectNodes("//cite"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (SafeXmlElement node in dom.SafeSelectNodes("//script"))
            {
                if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Length == 0)
                {
                    node.InnerText = " ";
                }
            }

            foreach (SafeXmlElement node in dom.SafeSelectNodes("//style"))
            {
                if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Length == 0)
                {
                    node.InnerText = " ";
                }
            }
            foreach (SafeXmlElement node in dom.SafeSelectNodes("//iframe"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(
                        node.OwnerDocument.CreateTextNode("Must have a closing tag in HTML")
                    );
                }
            }
        }

        /// <summary>
        /// Convert the DOM (which is expected to be XHTML5) to HTML5
        /// </summary>
        public static string SaveDOMAsHtml5(SafeXmlDocument dom, string targetPath)
        {
            var html = ConvertDomToHtml5(dom);
            try
            {
                RobustFile.WriteAllText(targetPath, html, Encoding.UTF8);
            }
            catch (UnauthorizedAccessException e)
            {
                // Re-throw with some additional debugging info.
                throw new BloomUnauthorizedAccessException(targetPath, e);
            }

            return targetPath;
        }

        /// <summary>
        /// Convert a single element into equivalent HTML.
        /// </summary>
        /// <param name="elt"></param>
        /// <returns></returns>
        public static string ConvertElementToHtml5(SafeXmlElement elt)
        {
            var xmlStringBuilder = new StringBuilder();
            // There may be some way to make Tidy work on something that isn't a whole HTML document,
            // but adding and removing this doesn't cost much.
            xmlStringBuilder.Append("<html><body>");
            var settings = new XmlWriterSettings
            {
                Indent = true,
                CheckCharacters = true,
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment
            };
            using (var writer = XmlWriter.Create(xmlStringBuilder, settings))
            {
                elt.WriteTo(writer);
                writer.Close();
            }

            xmlStringBuilder.Append("</body></html>");

            var docHtml = ConvertXhtmlToHtml5(xmlStringBuilder.ToString());
            int bodyIndex = docHtml.IndexOf("<body>", StringComparison.InvariantCulture);
            int endBodyIndex = docHtml.LastIndexOf("</body>", StringComparison.InvariantCulture);
            int start = bodyIndex + "<body>".Length;
            return docHtml.Substring(start, endBodyIndex - start);
        }

        public static string ConvertDomToHtml5(SafeXmlDocument dom)
        {
            // First we write the DOM out to string

            var settings = new XmlWriterSettings
            {
                Indent = true,
                CheckCharacters = true,
                OmitXmlDeclaration = true
            };
            var xmlStringBuilder = new StringBuilder();
            using (var writer = XmlWriter.Create(xmlStringBuilder, settings))
            {
                dom.WriteContentTo(writer);
                writer.Close();
            }

            return ConvertXhtmlToHtml5(xmlStringBuilder.ToString());
        }

        static string ConvertXhtmlToHtml5(string input)
        {
            var xml = input;
            // HTML Tidy will mess many things up, so we have these work arounds to make it "safe from libtidy"
            xml = AddFillerToKeepTidyFromRemovingEmptyElementsAndWhiteSpace(xml);

            // Tidy will convert <br /> to <br></br> which is not valid and produces an unexpected double line break.
            var BrPlaceholder = "$$ConvertThisBackToBr$$";
            xml = xml.Replace("<br />", BrPlaceholder);

            var removedSvgs = new List<string>();
            xml = RemoveSvgs(xml, removedSvgs);

            // Use AngleSharp to parse and convert to HTML5
            string html;
            try
            {
                var config = Configuration.Default.WithDefaultLoader();
                using var context = BrowsingContext.New(config);
                var parser = context.GetService<IHtmlParser>();
                var document = parser.ParseDocument(xml);
                
                // Apply cleanup
                CleanupAngleSharpDocument(document);
                
                // Convert to HTML5
                html = document.ToHtml();
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Error converting XHTML to HTML5: {ex.Message}", ex);
            }

            // Now revert the stuff we did to make it "safe from libtidy"
            html = RestoreSvgs(html, removedSvgs);
            html = html.Replace(BrPlaceholder, "<br />");
            html = RemoveFillerInEmptyElements(html);
            html = FixVoidElementEndTags(html);
            return html;
        }

        /// <summary>
        /// Void elements are elements that are not allowed to have any child nodes.  They are not
        /// allowed to have an end tag: they only have a start tag.  Self-closing tags are not a concept
        /// in HTML, but XHTML allows them.  HTML5 allows void element tags to be self-closing, because
        /// it just ignores a slash at the end of a tag.  This method fixes up void elements that have
        /// been given a full end tag, like <img...></img> or <meta...></meta>, to be self-closing.  This
        /// form is acceptable to both HTML and XML parsers.
        /// </summary>
        /// <remarks>
        /// See BL-14695.  Also see https://developer.mozilla.org/en-US/docs/Glossary/Void_element.
        /// </remarks>
        static string FixVoidElementEndTags(string html)
        {
            // list of tags taken from https://developer.mozilla.org/en-US/docs/Glossary/Void_element
            string[] voidEndTags = new string[] {
                "></area>",
                "></base>",
                "></br>",   // handled separately above, but here for completeness
                "></col>",
                "></embed>",
                "></hr>",
                "></img>",
                "></input>",
                "></link>",
                "></meta>",
                "></param>",
                "></source>",
                "></track>",
                "></wbr>",
            };
            foreach (var tag in voidEndTags)
            {
                // The space before the / is important, otherwise the / may be interpreted as part
                // of an attribute value.
                html = html.Replace(tag, " />");
            }
            return html;
        }

        public static void RemoveAllContentTypesMetas(SafeXmlDocument dom)
        {
            foreach (
                SafeXmlElement n in dom.SafeSelectNodes("//head/meta[@http-equiv='Content-Type']")
            )
            {
                n.ParentNode.RemoveChild(n);
            }
        }

        private static readonly Regex _svgMatch = new Regex(
            "<svg\\s.*?</svg>",
            RegexOptions.Singleline | RegexOptions.Compiled
        );

        public static string RemoveSvgs(string input, List<string> svgs)
        {
            // Tidy utterly chokes (exits the whole program without even a green screen) on SVGs.
            // This may not the most efficient way to remove and restore them but the common case is that none are found,
            // and this is not too bad for that case.
            return _svgMatch.Replace(
                input,
                (Match match) =>
                {
                    svgs.Add(match.Value);
                    return SvgPlaceholder;
                }
            );
        }

        public static string RestoreSvgs(string input, List<string> svgs)
        {
            if (svgs.Count == 0)
                return input;
            if (svgs.Count == 1)
                return input.Replace(SvgPlaceholder, svgs[0]);
            var splitInput = input.Split(
                new[] { SvgPlaceholder },
                svgs.Count + 1,
                StringSplitOptions.None
            );
            var builder = new StringBuilder();
            for (int i = 0; i < svgs.Count; i++)
            {
                builder.Append(splitInput[i]);
                builder.Append(svgs[i]);
            }
            builder.Append(splitInput[svgs.Count]);
            return builder.ToString();
        }
    }
}
