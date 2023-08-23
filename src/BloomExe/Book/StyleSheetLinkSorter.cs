using System;
using System.Collections.Generic;
using System.Xml;
using SIL.Linq;
using SIL.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// Since the precedence of stylesheet rules is influence by the their order (and thus the order of
	/// the various stylesheet declarations), we sort them to in a way that make sense and is consistent
	/// </summary>
	public class StyleSheetLinkSorter : IComparer<XmlElement>
	{
		const int kDefaultValueForStyleSheetsThatShouldListInTheMiddle = 100;
		private static Dictionary<string, int> _values;

		private static void Init()
		{
			if(_values==null)
			{
				_values = new Dictionary<string, int>();
				var weight = 0;
				BookStorage.KnownCssFilePrefixesInOrder.ForEach(x =>
				{
					if (x == "UNKNOWN_STYLESHEETS_HERE")
						weight = kDefaultValueForStyleSheetsThatShouldListInTheMiddle + 1000;
					else
						_values.Add(x, weight++);
				});
			}
		}

		public int Compare(XmlElement a, XmlElement b)
		{
			Init();

			var x = a.GetStringAttribute("href").ToLowerInvariant();
			var y = b.GetStringAttribute("href").ToLowerInvariant();

			int xValue = GetValue(x);
			int yValue = GetValue(y);

		   // Debug.WriteLine(string.Format("Comparing {0}({1}) and {2}({3})", x,xValue,y,yValue));
			if (xValue == yValue)
				return String.Compare(x, y);

			if (xValue < yValue)
				return -1;
			return 1;

		}

		private int GetValue(string s)
		{
			foreach (var pair in _values)
			{
				var key = pair.Key.ToLowerInvariant();
				if (s.ToLowerInvariant().StartsWith(key)	//no path in there
					|| (s.ToLowerInvariant().EndsWith("/" + key))
					|| (s.ToLowerInvariant().EndsWith("\\" + key)))
					return pair.Value;
			}


			// "SHRP Labels.css" is used by the SIL LEAD SHRP project to inject vernacular labels for sections of the book
			// we just need it to always come after the other stylesheet(s) of the book, which may supply default
			// labels
			if (s.ToLowerInvariant().EndsWith("labels.css"))
				return kDefaultValueForStyleSheetsThatShouldListInTheMiddle + 1;

			return kDefaultValueForStyleSheetsThatShouldListInTheMiddle;
		}
	}
}
