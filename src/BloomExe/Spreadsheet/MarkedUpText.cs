using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

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

		public int Count
		{
			get
			{
				return _runList.Count;
			}
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
			XmlDocument doc = new XmlDocument();
			doc.PreserveWhitespace = true;

			var wrappedXmlString = "<wrapper>" + xmlString + "</wrapper>";
			//TODO maybe instead of wrapping, we could export the outerxml instead of the innerxml in SpreadsheetExporter,
			//though this would break a lot of the tests, but would let us detect textarea tags
			doc.LoadXml(wrappedXmlString);
			XmlNode root = doc.DocumentElement;

			MarkedUpText result = new MarkedUpText();
			MarkedUpText pending = new MarkedUpText();

			//There are no paragraph elements, it is probably an  old bloom book using <textarea> blocks,
			//so keep all whitespace
			if (((XmlElement) root).GetElementsByTagName("p").Count == 0)
			{
				return ParseXmlRecursive(root);
			}

			foreach (XmlNode x in root.ChildNodes.Cast<XmlNode>())
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
				if (x.Name == "p")
				{
					// We want a line break here, but only if something follows...we don't need a blank line at
					// the end of the cell, which is what Excel will do with a trailing newline.
					// Review or Environment.Newline? But I'd rather generate something consistent.
					// Linux: what line break is best to use when constructing an Excel spreadsheet in Linux?
					result.AddAllFrom(pending);
					result.AddAllFrom(ParseXmlRecursive(x));
					pending = new MarkedUpText();
					pending._runList.Add(new MarkedUpTextRun("\r\n"));
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

		private static MarkedUpText ParseXmlRecursive(XmlNode node)
		{
			MarkedUpText markedUpText;
			if ((node.Name == "br")
				|| (node.Name == "span" && node.Attributes.GetNamedItem("class").Value.Equals("bloom-linebreak")))
			{
				//TODO Is there a way to roundtrip these distinguishably from new paragraphs?
				// Will excel render \n or an escape seqence as a detectably different newline?
				MarkedUpTextRun run = new MarkedUpTextRun("\r\n");
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
				foreach (XmlNode child in node.ChildNodes)
				{
					MarkedUpText markedUpChild = ParseXmlRecursive(child);
					applyFormatting(node.Name, markedUpChild);
					markedUpText._runList.AddRange(markedUpChild._runList);
				}
			}
			return markedUpText;

		}

		private static void applyFormatting(string formatName, MarkedUpText markedUpText)
		{
			foreach (MarkedUpTextRun run in markedUpText._runList)
			{
				run.setProperty(formatName);
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
			Text = textContent;
		}

		public string Text { get; set; }
		public bool Bold { get; set; }
		public bool Italic { get; set; }
		public bool Underlined { get; set; }
		public bool Superscript { get; set; }
		public bool HasFormatting => Bold | Italic | Underlined | Superscript;


		public void setProperty(string propertyName)
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
		}

	}
}


