using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.SafeXml;
using SIL.Xml;

namespace Bloom.Spreadsheet
{
    /// <summary>
    /// This class is a representation of text which may have parts
    /// of it bolded, italicized, underlined, and/or superscripted
    /// </summary>
    public class MarkedUpText
    {
        private List<MarkedUpTextRun> _runList;
        public IEnumerable Runs => _runList;
        public bool HasFormatting => _runList.Any(r => r.HasFormatting);

        public MarkedUpText()
        {
            _runList = new List<MarkedUpTextRun>();
        }

        public string PlainText()
        {
            var stringBuilder = new StringBuilder();
            foreach (MarkedUpTextRun run in this._runList)
            {
                stringBuilder.Append(run.Text);
            }
            return stringBuilder.ToString();
        }

        public override string ToString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("<p>");
            foreach (var run in _runList)
            {
                if (run.Text.Equals("\n"))
                {
                    stringBuilder.Append($"</p>{Environment.NewLine}<p>");
                    continue;
                }
                List<string> endTags = new List<string>();
                if (run.Bold)
                {
                    AddTags(stringBuilder, "strong", endTags);
                }
                if (run.Italic)
                {
                    AddTags(stringBuilder, "em", endTags);
                }
                if (run.Underlined)
                {
                    AddTags(stringBuilder, "u", endTags);
                }
                if (run.Superscript)
                {
                    AddTags(stringBuilder, "sup", endTags);
                }

                stringBuilder.Append(run.Text);

                endTags.Reverse();
                foreach (var endTag in endTags)
                {
                    stringBuilder.Append(endTag);
                }
            }
            stringBuilder.Append("</p>");
            return stringBuilder.ToString();
        }

        private void AddTags(StringBuilder stringBuilder, string tagName, List<string> endTag)
        {
            stringBuilder.Append("<" + tagName + ">");
            endTag.Add("</" + tagName + ">");
        }

        public int Count
        {
            get { return _runList.Count; }
        }

        public MarkedUpTextRun GetRun(int index)
        {
            return _runList[index];
        }

        /// <summary>
        /// Extract the text and any bold, italic, underline, and/or superscript formatting
        /// Adds newlines after paragraphs except for the last one
        /// </summary>
        public static MarkedUpText ParseXml(string xmlString)
        {
            var doc = SafeXmlDocument.Create();
            doc.PreserveWhitespace = true;

            var wrappedXmlString = "<wrapper>" + xmlString + "</wrapper>";
            doc.LoadXml(wrappedXmlString);
            var root = doc.DocumentElement;

            MarkedUpText result = new MarkedUpText();
            MarkedUpText pending = new MarkedUpText();

            //There are no paragraph elements, just keep all whitespace
            if (((SafeXmlElement)root).GetElementsByTagName("p").Length == 0)
            {
                return ParseXmlRecursive(root);
            }

            foreach (var x in root.ChildNodes.Cast<SafeXmlNode>())
            {
                if (x.Name == "#whitespace")
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(x.InnerText) && x.Name != "p")
                {
                    if (result.Count > 0)
                        pending._runList.Add(new MarkedUpTextRun(x.InnerText));
                    continue;
                }
                result.AddAllFrom(pending);
                result.AddAllFrom(ParseXmlRecursive(x));
                pending = new MarkedUpText();
                if (x.Name == "p")
                {
                    // We want a line break here, but only if something follows...we don't need a blank line at
                    // the end of the cell, which is what Excel will do with a trailing newline.
                    // It's important to use a simple \n here, not something that is (or might be) \r\n.
                    // The latter looks fine in Excel, but when converted to Google Docs the \r shows up
                    // as an explicit \x000D. \n works in both places.
                    pending._runList.Add(new MarkedUpTextRun("\n"));
                }
            }
            return result;
        }

        private void AddAllFrom(MarkedUpText m)
        {
            foreach (MarkedUpTextRun r in m.Runs)
            {
                this._runList.Add(r);
            }
        }

        private static MarkedUpText ParseXmlRecursive(SafeXmlNode node)
        {
            MarkedUpText markedUpText;
            if (
                (node.Name == "br")
                || (
                    node.Name == "span"
                    && (node.GetAttribute("class") ?? "").Equals("bloom-linebreak")
                )
            )
            {
                // not \r\n or something that might translate to that. See comment in ParseXml()
                MarkedUpTextRun run = new MarkedUpTextRun("\n");
                markedUpText = new MarkedUpText();
                markedUpText._runList.Add(run);
            }
            // replace audio split markers with pipes to retain audio segment markers
            else if (
                node.Name == "span"
                && (node.GetAttribute("class") ?? "").Equals("bloom-audio-split-marker")
            )
            {
                MarkedUpTextRun run = new MarkedUpTextRun("|");
                markedUpText = new MarkedUpText();
                markedUpText._runList.Add(run);
            }
            else if (!node.HasChildNodes)
            {
                MarkedUpTextRun run = new MarkedUpTextRun(node.InnerText);
                markedUpText = new MarkedUpText();
                markedUpText._runList.Add(run);
            }
            else
            {
                markedUpText = new MarkedUpText();
                foreach (SafeXmlNode child in node.ChildNodes)
                {
                    MarkedUpText markedUpChild = ParseXmlRecursive(child);
                    ApplyFormatting(
                        node.Name,
                        node.GetOptionalStringAttribute("style", ""),
                        markedUpChild
                    );
                    markedUpText._runList.AddRange(markedUpChild._runList);
                }
            }
            return markedUpText;
        }

        private static void ApplyFormatting(
            string formatName,
            string styleAttribute,
            MarkedUpText markedUpText
        )
        {
            foreach (MarkedUpTextRun run in markedUpText._runList)
            {
                run.setProperty(formatName, styleAttribute);
            }
        }
    }

    /// <summary>
    /// A run of text which has a certain formatting, within a larger blurb of text
    /// e.g. a phrase which is italicized in the middle of a sentence
    /// </summary>
    public class MarkedUpTextRun
    {
        public MarkedUpTextRun(string textContent)
        {
            // Windows-type newlines cause problems when an excel spreasheet is converted to a Google doc.
            Text = textContent.Replace("\r\n", "\n");
        }

        public string Text { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underlined { get; set; }
        public bool Superscript { get; set; }
        public Color Color { get; set; }

        public bool HasFormatting => Bold | Italic | Underlined | Superscript | Color != null;

        public void setProperty(string propertyName, string styleAttribute)
        {
            if (propertyName.Equals("strong") || propertyName.Equals("b"))
            {
                Bold = true;
            }
            else if (propertyName.Equals("em") || propertyName.Equals("i"))
            {
                Italic = true;
            }
            else if (propertyName.Equals("sup"))
            {
                Superscript = true;
            }
            else if (propertyName.Equals("u"))
            {
                Underlined = true;
            }
            else
            {
                // remove spaces from the style attribute
                styleAttribute = styleAttribute.Replace(" ", "");
                int colorFoundIndex = styleAttribute.IndexOf("color:#");
                if (colorFoundIndex > -1)
                {
                    // There is a color specified
                    int colorCodeStartIndex = colorFoundIndex + 6;
                    // color code end index is the next semicolon after the start, or the end of the style string
                    int colorCodeEndIndex = styleAttribute.IndexOf(';', colorCodeStartIndex);
                    if (colorCodeEndIndex == -1)
                    {
                        colorCodeEndIndex = styleAttribute.Length;
                    }
                    string colorString = styleAttribute.Substring(
                        colorCodeStartIndex,
                        colorCodeEndIndex - colorCodeStartIndex
                    );
                    Color = ColorTranslator.FromHtml(colorString);
                }
            }
        }
    }
}
