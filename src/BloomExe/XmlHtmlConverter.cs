using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.Utils;
using SIL.IO;
using SIL.Xml;
using TidyManaged;

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

        public static XmlDocument GetXmlDomFromHtmlFile(
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
        public static XmlDocument GetXmlDomFromHtml(
            string content,
            bool includeXmlDeclaration = false
        )
        {
            var dom = new XmlDocument();
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

            //using (var temp = new TempFile())
            var temp = new TempFile();
            {
                RobustFile.WriteAllText(temp.Path, content, Encoding.UTF8);
                using (var tidy = RobustFileIO.DocumentFromFile(temp.Path))
                {
                    tidy.ShowWarnings = false;
                    tidy.Quiet = true;
                    tidy.WrapAt = 0; // prevents textarea wrapping.
                    tidy.AddTidyMetaElement = false;
                    tidy.OutputXml = true;
                    tidy.CharacterEncoding = EncodingType.Utf8;
                    tidy.InputCharacterEncoding = EncodingType.Utf8;
                    tidy.OutputCharacterEncoding = EncodingType.Utf8;
                    tidy.DocType = DocTypeMode.Omit; //when it supports html5, then we will let it out it
                    //maybe try this? tidy.Markup = true;

                    tidy.AddXmlDeclaration = includeXmlDeclaration;

                    //NB: this does not prevent tidy from deleting <span data-libray='somethingImportant'></span>
                    tidy.MergeSpans = AutoBool.No;
                    tidy.DropEmptyParagraphs = false;
                    tidy.MergeDivs = AutoBool.No;

                    var errors = tidy.CleanAndRepair();
                    if (!string.IsNullOrEmpty(errors))
                    {
                        throw new ApplicationException(errors);
                    }
                    var newContents = tidy.Save();
                    try
                    {
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
                        newContents = Regex.Replace(
                            newContents,
                            @"(<br></br>|<br ?/>)[\r\n]*</p>",
                            "</p>"
                        );

                        newContents = newContents.Replace(restoreCdataHere, savedCdata);

                        // Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
                        dom.PreserveWhitespace = true;
                        dom.LoadXml(newContents);
                    }
                    catch (Exception e)
                    {
                        var exceptionWithHtmlContents = new Exception(
                            string.Format(
                                "{0}{2}{2}{1}",
                                e.Message,
                                newContents,
                                Environment.NewLine
                            ),
                            innerException: e
                        );
                        throw exceptionWithHtmlContents;
                    }
                }
            }
            try
            {
                //It's a mystery but http://jira.palaso.org/issues/browse/BL-46 was reported by several people on Win XP, even though a look at html tidy dispose indicates that it does dispose (and thus close) the stream.
                // Therefore, I'm moving the dispose to an explict call so that I can catch the error and ignore it, leaving an extra file in Temp.

                temp.Dispose();
                //enhance... could make a version of this which collects up any failed deletes and re-attempts them with each call to this
            }
            catch (Exception error)
            {
                //swallow
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(error);
                Debug.Fail("Repro of http://jira.palaso.org/issues/browse/BL-46 ");
            }

            //this is a hack... each time we write the content, we add a new <meta http-equiv="Content-Type" content="text/html; charset=utf-8">
            //so for now, we remove it when we read it in. It'll get added again when we write it out
            RemoveAllContentTypesMetas(dom);

            return dom;
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

            using (var tidy = Document.FromString(content))
            {
                tidy.ShowWarnings = false;
                tidy.Quiet = true;
                tidy.AddTidyMetaElement = false;
                tidy.OutputXml = true;
                tidy.DocType = DocTypeMode.Omit; //when it supports html5, then we will let it out it

                using (var log = new MemoryStream())
                {
                    tidy.CleanAndRepair(log);
                    string errors = ASCIIEncoding.ASCII.GetString(log.ToArray());
                    if (!string.IsNullOrEmpty(errors))
                    {
                        throw new ApplicationException(errors);
                    }
                }
            }
        }

        /// <summary>
        /// If an element has empty contents, like <textarea></textarea>, browsers will sometimes drop the end tag, so that now, when we read it back into xml,
        /// anything following the <textarea> will be interpreted as part of the <textarea>!  This method makes sure such tags are never totally empty.
        /// </summary>
        /// <param name="dom"></param>
        public static void MakeXmlishTagsSafeForInterpretationAsHtml(XmlDocument dom)
        {
            foreach (XmlElement node in dom.SafeSelectNodes("//textarea"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }
            foreach (XmlElement node in dom.SafeSelectNodes("//div"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (XmlElement node in dom.SafeSelectNodes("//p"))
            //without  this, an empty paragraph suddenly takes over the subsequent elements. Browser sees <p></p> and thinks... let's just make it <p>, shall we? Stupid optional-closing language, html is....
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (XmlElement node in dom.SafeSelectNodes("//span"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (XmlElement node in dom.SafeSelectNodes("//cite"))
            {
                if (!node.HasChildNodes)
                {
                    node.AppendChild(node.OwnerDocument.CreateTextNode(""));
                }
            }

            foreach (XmlElement node in dom.SafeSelectNodes("//script"))
            {
                if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Count == 0)
                {
                    node.InnerText = " ";
                }
            }

            foreach (XmlElement node in dom.SafeSelectNodes("//style"))
            {
                if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Count == 0)
                {
                    node.InnerText = " ";
                }
            }
            foreach (XmlElement node in dom.SafeSelectNodes("//iframe"))
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
        public static string SaveDOMAsHtml5(XmlDocument dom, string targetPath)
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
        public static string ConvertElementToHtml5(XmlElement elt)
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

        public static string ConvertDomToHtml5(XmlDocument dom)
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

            // Now re-write as html, indented nicely
            string html;
            using (var tidy = Document.FromString(xml))
            {
                tidy.ShowWarnings = false;
                tidy.Quiet = true;

                // Removing comments is unfortunate, I can imagine cases where it would be helpful to be able to
                // have comments. But currently our ckeditor instances are never "destroy()"ed, and are dumping
                // e.g. 50 k of comment text into a single field, when you paste from MS Word. So we're
                // going to dump all comments for now.
                tidy.RemoveComments = true;

                tidy.AddTidyMetaElement = false;
                tidy.OutputXml = false;
                tidy.OutputHtml = true;
                tidy.DocType = DocTypeMode.Html5;
                tidy.MergeDivs = AutoBool.No;
                tidy.MergeSpans = AutoBool.No;
                tidy.PreserveEntities = true;
                tidy.JoinStyles = false;
                tidy.IndentBlockElements = AutoBool.Auto; //instructions say avoid 'yes'
                tidy.WrapAt = 9999;
                tidy.IndentSpaces = 4;
                tidy.CharacterEncoding = EncodingType.Utf8;
                tidy.CleanAndRepair();
                using (var stream = new MemoryStream())
                {
                    tidy.Save(stream);
                    stream.Flush();
                    stream.Seek(0L, SeekOrigin.Begin);
                    using (var sr = new StreamReader(stream, Encoding.UTF8))
                        html = sr.ReadToEnd();
                }
            }

            // Now revert the stuff we did to make it "safe from libtidy"

            html = RestoreSvgs(html, removedSvgs);

            html = html.Replace(BrPlaceholder, "<br />");
            html = RemoveFillerInEmptyElements(html);
            return html;
        }

        public static void RemoveAllContentTypesMetas(XmlDocument dom)
        {
            foreach (XmlElement n in dom.SafeSelectNodes("//head/meta[@http-equiv='Content-Type']"))
            {
                n.ParentNode.RemoveChild(n);
            }
        }

        private static readonly Regex _svgMatch = new Regex("<svg\\s.*?</svg>", RegexOptions.Singleline|RegexOptions.Compiled);
        public static string RemoveSvgs(string input, List<string> svgs)
        {
            // Tidy utterly chokes (exits the whole program without even a green screen) on SVGs.
            // This may not the most efficient way to remove and restore them but the common case is that none are found,
            // and this is not too bad for that case.
            return _svgMatch.Replace(input, (Match match) => {
                svgs.Add(match.Value); return SvgPlaceholder;
            });
        }

        public static string RestoreSvgs(string input, List<string> svgs)
        {
            if (svgs.Count == 0)
                return input;
            if (svgs.Count == 1)
                return input.Replace(SvgPlaceholder, svgs[0]);
            var splitInput = input.Split(new[] { SvgPlaceholder }, svgs.Count + 1, StringSplitOptions.None);
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
