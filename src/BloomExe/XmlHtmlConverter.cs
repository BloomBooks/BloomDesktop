using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.SafeXml;
using Bloom.Utils;
using SIL.IO;
using HtmlAgilityPack;
using System.Linq;

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

        public static string CreateHtmlDocument(string headContent, string bodyContent)
        {
            string nonEmptyHeadContent = headContent ?? "<title></title>";
            return $"<!DOCTYPE html>\n<html>\n<head>\n{nonEmptyHeadContent}\n</head>\n<body>\n{bodyContent}\n</body>\n</html>";
        }

        public static SafeXmlDocument GetXmlDomFromHtmlFile(
            string path,
            bool includeXmlDeclaration = false
        )
        {
            return GetXmlDomFromHtml(RobustFile.ReadAllText(path), includeXmlDeclaration);
        }

        /// <summary></summary>
        /// <param name="content">Must be a complete valid HTML document</param>
        /// <param name="includeXmlDeclaration"></param>
        /// <exception>Throws if there are parsing errors</exception>
        /// <returns></returns>
        public static SafeXmlDocument GetXmlDomFromHtml(
            string content,
            bool includeXmlDeclaration = false
        )
        {
            var dom = SafeXmlDocument.Create();

            //in BL-2250, we found that in previous versions, this method would return, for example, "<u> </u>" REMOVEWHITESPACE.
            //That is fixed now, but this is needed to give to clean up existing books.
            content = content.Replace(@"REMOVEWHITESPACE", "");

            // fix for <br></br> tag doubling
            content = content.Replace("<br></br>", "<br />");

            // TODO can we handle svgs now?
            // var removedSvgs = new List<string>();
            // content = RemoveSvgs(content, removedSvgs);

            // Hand editing HTML files can sometimes, with some editors, introduce a BOM after the first character in the HTML file.
            // These can cause problems in creating PDFs (see https://issues.bloomlibrary.org/youtrack/issue/BL-11127).
            // Either having a BOM or not having a BOM as the first character in the file doesn't really matter.  (File storage tends
            // to use UTF-8, which doesn't need BOMs.)  And the tidy processing below strips an initial BOM anyway, so it doesn't
            // hurt to do it here if it exists.  (Starting the search after the first character is perhaps a trivial optimization.)
            if (!String.IsNullOrEmpty(content) && content.IndexOf('\uFEFF', 1) > 0)
                content = content.Replace("\uFEFF", "");

            // Remove this now, as it'll be harder once Agility Pack wraps everything in the XMl declaration.
            if (content.StartsWith("<!DOCTYPE html>"))
            {
                content = content.Substring("<!DOCTYPE html>".Length);
            }

            // remove any of the starting <!-- ... --> comments. Otherwise agility pack treats them as sibling nodes
            // and wraps both them and the real <html> in a parent span :(
            while (
                Regex.IsMatch(
                    content,
                    @"^\s*<!--.*?-->\s*",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase
                )
            )
            {
                content = Regex.Replace(
                    content,
                    @"^\s*<!--.*?-->\s*",
                    "",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase
                );
            }

            // Otherwise agility pack treats leading blank lines as sibling nodes also
            content = content.Trim();

            RobustFile.WriteAllText("../../../../beforeXml.txt", content, Encoding.UTF8);
            var doc = new HtmlDocument();

            // We have (currently one) element protected by CDATA and need this to preserve it
            doc.OptionTreatCDataBlockAsComment = true;
            // Without this, <!DOCTYPE html> is weirdly turning into <!--CTYPE ht-->, not sure why. We will remove it anyway though
            doc.OptionXmlForceOriginalComment = true;

            /// <summary>
            /// Defines if LI, TR, TH, TD tags must be partially fixed when nesting errors are detected. Default is false.
            /// </summary>
            // TODO Do we want this? I think not, less invasive is better.
            // doc.OptionFixNestedTags = true;

            doc.OptionOutputAsXml = true;

            /// <summary>
            /// If used together with <see cref="OptionOutputAsXml"/> and enabled, Xml namespaces in element names are preserved. Default is false.
            /// </summary>
            // / TODO do we want this?
            // doc.OptionPreserveXmlNamespaces = true;

            doc.OptionOutputOriginalCase = true;
            doc.OptionDefaultUseOriginalName = true;

            // writes empty nodes as closed
            doc.OptionWriteEmptyNodes = true;

            // Agility pack sets a flag to wrap the contents of style tag with <![CDATA[ ... ]]>. We don't want this.
            // https://github.com/zzzprojects/html-agility-pack/blob/8490ad1321e378582aec156668888511d3010b33/src/HtmlAgilityPack.Shared/HtmlNode.cs#L99
            // We may need to do this for other element types also
            HtmlNode.ElementsFlags.Remove("style");

            doc.LoadHtml(content);

            string newContents = doc.DocumentNode.OuterHtml;
            var firstChild =
                doc.DocumentNode.ChildNodes.Count > 0 ? doc.DocumentNode.ChildNodes[0] : null;
            if (firstChild.Name != "html")
            {
                throw new ApplicationException(
                    "The HTML document must have a top-level <html> element."
                );
            }
            // TODO if doc has top level comments, remove them. If no top level html element, create?
            //else if (firstChild.Name == "span")
            //{
            //    // agility pack thought there were multiple top level nodes and wrapped them in a span. Probably one is the real html and the rest are comments
            //    // see if firstChild has a child that is an html element
            //    foreach (var child in firstChild.ChildNodes)
            //    {
            //        if (child.Name == "html")
            //        {
            //            newContents = child.OuterHtml;
            //            break;
            //        }
            //    }
            //}
            //if (newContents == null)
            //{
            //    newContents =
            //        "<html><head><title></title></head><body>"
            //        + firstChild.OuterHtml
            //        + "</body></html>";
            //}

            RobustFile.WriteAllText(
                "../../../../afterXml.txt",
                doc.DocumentNode.OuterHtml,
                Encoding.UTF8
            );

            try
            {
                var xmlDeclaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
                if (!includeXmlDeclaration && newContents.StartsWith(xmlDeclaration))
                {
                    newContents = newContents.Substring(xmlDeclaration.Length);
                }

                // newContents = RestoreSvgs(newContents, removedSvgs);

                // Agility Pack encodes the & in &nbsp;
                newContents = newContents.Replace("&amp;nbsp;", "&#160;");

                // remove blank lines at the end of style blocks
                newContents = Regex.Replace(newContents, @"\s+<\/style>", "</style>");

                // remove <br> elements immediately preceding </p> close tag (BL-2557)
                // These are apparently inserted by ckeditor as far as we can tell.  They don't show up on
                // fields that have never had a ckeditor activated, and always show up on fields that have
                // received focus and activated an inline ckeditor.  The ideal ckeditor use case appears
                // to be for data entry as part of a web page that get stored separately, with the data
                // obtained something like the following in javascript:
                //        ckedit.on('blur', function(evt) {
                //            var editor = evt['editor'];
                //            var data = editor.getData();
                //            <at this point, the data looks okay, with any <br> element before the </p> tag.>
                //            <store the data somewhere: the following lines have no effect, and may be silly.>
                //            var div = mapCkeditDiv[editor.id];
                //            div.innerHTML = data;
                //        });
                // Examining the initial value of div.innerHTML shows the unwanted <br> element, but it is
                // not in the data returned by editor.getData().  Since assigning to div.innerHTML doesn't
                // affect what gets written to the file, this hack was implemented instead.
                newContents = Regex.Replace(newContents, @"(<br></br>|<br ?/>)[\r\n]*</p>", "</p>");
                RobustFile.WriteAllText(
                    "../../../../postProcessing.txt",
                    newContents,
                    Encoding.UTF8
                );

                // Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
                dom.PreserveWhitespace = true;
                //try
                //{

                dom.LoadXml(newContents);
            }
            catch (Exception e)
            {
                var exceptionWithHtmlContents = new Exception(
                    string.Format("{0}{2}{2}{1}", e.Message, newContents, Environment.NewLine),
                    innerException: e
                );
                throw exceptionWithHtmlContents;
            }

            //this is a hack... each time we write the content, we add a new <meta http-equiv="Content-Type" content="text/html; charset=utf-8">
            //so for now, we remove it when we read it in. It'll get added again when we write it out
            RemoveAllContentTypesMetas(dom);

            return dom;
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
            // TODO add head and title

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
            // var removedSvgs = new List<string>();
            // xml = RemoveSvgs(xml, removedSvgs);

            var doc = new HtmlDocument();
            doc.OptionTreatCDataBlockAsComment = true;
            doc.OptionXmlForceOriginalComment = true;
            doc.OptionOutputOriginalCase = true;
            doc.OptionDefaultUseOriginalName = true;

            // <img...></img> to <img... />
            doc.OptionWriteEmptyNodes = true;
            doc.OptionOutputAsXml = false;

            doc.LoadHtml(xml);
            string html = doc.DocumentNode.OuterHtml;
            // TODO do we get or need the doctype delcaration?
            doc.Save("../../../../testXhtmlToHtml5.txt");

            // Now re-write as html, indented nicely
            // string html;
            // using (var tidy = Document.FromString(xml))
            // {
            //     tidy.ShowWarnings = false;
            //     tidy.Quiet = true;

            //     // Removing comments is unfortunate, I can imagine cases where it would be helpful to be able to
            //     // have comments. But currently our ckeditor instances are never "destroy()"ed, and are dumping
            //     // e.g. 50 k of comment text into a single field, when you paste from MS Word. So we're
            //     // going to dump all comments for now.
            //     tidy.RemoveComments = true;

            //     tidy.AddTidyMetaElement = false;
            //     tidy.OutputXml = false;
            //     tidy.OutputHtml = true;
            //     tidy.DocType = DocTypeMode.Html5;
            //     tidy.MergeDivs = AutoBool.No;
            //     tidy.MergeSpans = AutoBool.No;
            //     tidy.PreserveEntities = true;
            //     tidy.JoinStyles = false;
            //     tidy.IndentBlockElements = AutoBool.Auto; //instructions say avoid 'yes'
            //     tidy.WrapAt = 9999;
            //     tidy.IndentSpaces = 4;
            //     tidy.CharacterEncoding = EncodingType.Utf8;
            //     tidy.CleanAndRepair();
            //     using (var stream = new MemoryStream())
            //     {
            //         tidy.Save(stream);
            //         stream.Flush();
            //         stream.Seek(0L, SeekOrigin.Begin);
            //         using (var sr = new StreamReader(stream, Encoding.UTF8))
            //             html = sr.ReadToEnd();
            //     }
            // }

            // html = RestoreSvgs(html, removedSvgs);

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
    }
}
