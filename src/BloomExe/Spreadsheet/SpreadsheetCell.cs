using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// One cell in a row of a spreadsheet
	/// </summary>
	public class SpreadsheetCell
	{
		public string Content;

		public string Text
		{
			get
			{
				try
				{
					return XDocument.Parse(Content, LoadOptions.PreserveWhitespace).Root?.Value;
				}
				catch (XmlException)
				{
					// It's not XML. Just return it.
					return Content;
				}
			}
		}

		// Currently we only write comments, and the author is always 'Bloom'
		public string Comment;
	}
}
