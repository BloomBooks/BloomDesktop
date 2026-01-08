using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.SafeXml;
using Bloom.Utils;
using HtmlAgilityPack;
using SIL.IO;

namespace Bloom
{
    public static class XmlHtmlConverter
    {
        public const string CdataPrefix = "/*<![CDATA[*/";
        public const string CdataSuffix = "/*]]>*/";

        /// <summary>
        /// Does not convert between XML and HTML, only adds the html/head/body tags (and adds a title tag unless headContent is provided)
        /// </summary>
        public static string CreateHtmlString(string bodyContent, string headContent = null)
        {
            return $@"<html>
  <head>
  {headContent ?? @"
    <title>
    </title>"}
  </head>
  <body>
    {bodyContent}
  </body>
</html>";
        }

        public static SafeXmlDocument GetXmlDomFromHtmlFile(
            string path,
            bool includeXmlDeclaration = false
        )
        {
            return GetXmlDomFromHtml(RobustFile.ReadAllText(path), includeXmlDeclaration);
        }

        /// <summary></summary>
        /// <param name="html">Must be a complete valid HTML document string</param>
        /// <param name="includeXmlDeclaration"></param>
        /// <exception>Throws if there are parsing errors</exception>
        /// <returns></returns>
        public static SafeXmlDocument GetXmlDomFromHtml(
            string html,
            bool includeXmlDeclaration = false
        )
        {
            var dom = SafeXmlDocument.Create();

            //in BL-2250, we found that in previous versions, this method would return, for example, "<u> </u>" REMOVEWHITESPACE.
            //That is fixed now, but this is needed to give to clean up existing books.
            html = html.Replace(@"REMOVEWHITESPACE", "");

            // fix for <br></br> tag doubling
            html = html.Replace("<br></br>", "<br />");

            // Hand editing HTML files can sometimes, with some editors, introduce a BOM after the first character in the HTML file.
            // These can cause problems in creating PDFs (see https://issues.bloomlibrary.org/youtrack/issue/BL-11127).
            // Either having a BOM or not having a BOM as the first character in the file doesn't really matter.  (File storage tends
            // to use UTF-8, which doesn't need BOMs.)  And the tidy processing below strips an initial BOM anyway, so it doesn't
            // hurt to do it here if it exists.  (Starting the search after the first character is perhaps a trivial optimization.)
            if (!String.IsNullOrEmpty(html) && html.IndexOf('\uFEFF', 1) > 0)
                html = html.Replace("\uFEFF", "");

            // Remove comments (e.g. <!-- foobar -->) including multiline comments,
            // doctype declarations (<!DOCTYPE html>), and whitespace at the top of the file.
            // Otherwise html agility pack will treat these as sibling nodes of the html.
            var dtdAndCommentRegex =
                @"^(?:<!--[\s\S]*?-->|<!DOCTYPE\s+html>|<!DOCTYPE html\[\]>|\s+)";
            while (Regex.IsMatch(html, dtdAndCommentRegex, RegexOptions.IgnoreCase))
            {
                // For some reason we end up with the <!DOCTYPE html[]> if we put <!DOCTYPE html> into xml, which we shouldn't be doing
                Debug.Assert(!Regex.IsMatch(html, "<!DOCTYPE html\\[\\]>"));
                html = Regex.Replace(
                    html,
                    dtdAndCommentRegex,
                    "",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase
                );
            }

            // We need to catch trailing whitespace also
            html = html.Trim();

            var doc = new HtmlDocument();
            doc.OptionTreatCDataBlockAsComment = true; // We have (currently one) element protected by CDATA and need this to preserve it
            doc.OptionXmlForceOriginalComment = true; // Without this, <!DOCTYPE html> is weirdly turning into <!--CTYPE ht-->, not sure why. We will remove it anyway though
            doc.OptionOutputAsXml = true; // This also makes it fix duplicate attributes
            doc.OptionOutputOriginalCase = true;
            doc.OptionDefaultUseOriginalName = true;
            doc.OptionWriteEmptyNodes = true; // writes empty nodes as closed

            // Agility pack sets a flag to wrap the contents of style (and other) tags with <![CDATA[ ... ]]>. We already do this ourselves when we want
            // it, and often we don't. Removing the flags stops it from adding the CDATA wrapping.
            // https://github.com/zzzprojects/html-agility-pack/blob/8490ad1321e378582aec156668888511d3010b33/src/HtmlAgilityPack.Shared/HtmlNode.cs#L99
            foreach (var cDataFlag in new[] { "script", "style", "noxhtml", "textarea", "title" })
            {
                HtmlNode.ElementsFlags.Remove(cDataFlag);
            }

            doc.LoadHtml(html);

            string outputXml = doc.DocumentNode.OuterHtml;
            var firstChild = doc.DocumentNode.ChildNodes.FirstOrDefault();
            if (firstChild?.Name != "html")
            {
                throw new ApplicationException(
                    "Error converting HTML document to XML dom. The HTML document must have a top-level <html> element."
                );
            }

            try
            {
                var xmlDeclaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
                if (!includeXmlDeclaration && outputXml.StartsWith(xmlDeclaration))
                {
                    outputXml = outputXml.Substring(xmlDeclaration.Length);
                }

                // Agility Pack encodes the & in &nbsp;
                outputXml = outputXml.Replace("&amp;nbsp;", "&#160;");

                // remove blank lines at the end of style blocks
                outputXml = Regex.Replace(outputXml, @"\s+<\/style>", "</style>");

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
                // Enhance: Since this is a Bloom-specific edit rather than general xml/html conversion, consider moving
                // it elsewhere
                outputXml = Regex.Replace(outputXml, @"(<br></br>|<br ?/>)[\r\n]*</p>", "</p>");

                // Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
                dom.PreserveWhitespace = true;
                dom.LoadXml(outputXml);
            }
            catch (Exception e)
            {
                var exceptionWithHtmlContents = new Exception(
                    string.Format("{0}{2}{2}{1}", e.Message, outputXml, Environment.NewLine),
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
            var settings = new XmlWriterSettings
            {
                Indent = true,
                CheckCharacters = true,
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment,
            };
            using (var writer = XmlWriter.Create(xmlStringBuilder, settings))
            {
                elt.WriteTo(writer);
                writer.Close();
            }

            var docHtml = ConvertXhtmlToHtml5(CreateHtmlString(xmlStringBuilder.ToString()));
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
                OmitXmlDeclaration = true,
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

            var doc = new HtmlDocument();
            doc.OptionTreatCDataBlockAsComment = true;
            doc.OptionXmlForceOriginalComment = true;
            doc.OptionOutputOriginalCase = true;
            doc.OptionDefaultUseOriginalName = true;

            // converts e.g. <img...></img> to <img... />
            doc.OptionWriteEmptyNodes = true;
            doc.OptionOutputAsXml = false;

            doc.LoadHtml(xml);
            string html = doc.DocumentNode.OuterHtml;

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
