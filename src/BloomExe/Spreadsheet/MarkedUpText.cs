using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using OfficeOpenXml;
using OfficeOpenXml.Style;

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
		/// Adds newlines after paragraphs, but drops trailing, but not intermediate, white space.
		/// </summary>
		public static MarkedUpText ParseXml(string xmlString)
		{
			XmlDocument doc = new XmlDocument();
			doc.PreserveWhitespace = true;
			//wrap xml in another tag to make sure it has only one root
			//this will fail if there is a < or > not part of a tag...
			//var xmlStart = xmlString.IndexOf("<");
			//if (xmlStart == -1)
			//{
			//	xmlStart = 0;
			//}
			//var xmlEnd = xmlString.LastIndexOf(">");
			//if (xmlEnd == -1)
			//{
			//	xmlEnd = xmlString.Length;
			//}
			//var xmlSubString = xmlString.Substring(xmlStart, xmlEnd - xmlStart);
			var wrappedXmlString = "<wrapper>" + xmlString + "</wrapper>";
			//TODO maybe instead of wrapping, we could export the outerxml instead of the innerxml in SpreadsheetExporter,
			//though this would break a lot of the tests
			doc.LoadXml(wrappedXmlString);
			XmlNode root = doc.DocumentElement;

			MarkedUpText result = new MarkedUpText();
			MarkedUpText pending = new MarkedUpText();
			//we need to ignore whitespace between children but not grandchildren?
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
				//TODO copy contents?
				result.AddAllFrom(pending);
				pending = new MarkedUpText();
				result.AddAllFrom(ParseXmlRecursive(x));
				if (x.Name == "p")
				{
					// We want a line break here, but only if something follows...we don't need a blank line at
					// the end of the cell, which is what Excel will do with a trailing newline.
					// Review or Environment.Newline? But I'd rather generate something consistent.
					// Linux: what line break is best to use when constructing an Excel spreadsheet in Linux?
					pending = new MarkedUpText();
					pending._runList.Add(new MarkedUpTextRun("\r\n"));
				}
			}

			//MarkedUpText markedUpText = ParseXmlRecursive(root);

			//remove trailing whitespace. We don't want a trailing newline which
			//will make excel put a blank line at the end of the cell
			//while (markedUpText._runList.Count > 0
			//		&& string.IsNullOrWhiteSpace(markedUpText._runList[markedUpText._runList.Count - 1].Text))
			//{
			//	markedUpText._runList.RemoveAt(markedUpText._runList.Count - 1);
			//}
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
			if (!node.HasChildNodes)
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
			//if (node.Name == "p")
			//{
			//	// add a newline
			//	markedUpText._runList.Add(new MarkedUpTextRun("\r\n"));
			//	// Review or Environment.Newline? But I'd rather generate something consistent.
			//	// Linux: what line break is best to use when constructing an Excel spreadsheet in Linux?
			//}
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


