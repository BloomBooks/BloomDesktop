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
			var wrappedXmlString = "<wrapper>" + xmlString + "</wrapper>";
			doc.LoadXml(wrappedXmlString);
			XmlNode root = (XmlNode)doc.DocumentElement;
			MarkedUpText markedUpText = ParseXmlRecursive(root);

			//remove trailing whitespace. We don't want a trailing newline which
			//will make excel put a blank line at the end of the cell
			while (markedUpText._runList.Count > 0
					&& string.IsNullOrWhiteSpace(markedUpText._runList[markedUpText._runList.Count - 1].Text))
			{
				markedUpText._runList.RemoveAt(markedUpText._runList.Count - 1);
			}
			return markedUpText;
		}

		//TODO somewhere around here we are adding leading whitespace
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
			if (node.Name == "p")
			{
				// add a newline
				markedUpText._runList.Add(new MarkedUpTextRun("\r\n"));
				// Review or Environment.Newline? But I'd rather generate something consistent.
				// Linux: what line break is best to use when constructing an Excel spreadsheet in Linux?
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


