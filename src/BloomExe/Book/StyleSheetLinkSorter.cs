using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml;
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
				_values.Add("basepage.css", 10); // the opening bid
				_values.Add("editmode.css", 20);
				_values.Add("editoriginalmode.css", 30);
				_values.Add("previewmode.css", 40);
				_values.Add("origami.css", 50);
				_values.Add("branding.css", 60);


				//Note that kDefaultValueForStyleSheetsThatShouldListInTheMiddle should fall in between here
				//for the template-specific stuff, but we don't know those names

				//NB: I (JH) don't for sure know yet what the order of this should be. I think it should be last-ish.
				_values.Add("langVisibility.css".ToLowerInvariant(), 1000);
				_values.Add("settingsCollectionStyles.css".ToLowerInvariant(), 1500);
				_values.Add("customCollectionStyles.css".ToLowerInvariant(), 2000); // the almost last word
				_values.Add("customBookStyles.css".ToLowerInvariant(), 3000); // the very last word
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
				if (s.ToLowerInvariant() == pair.Key	//no path in there
					|| (s.ToLowerInvariant().EndsWith("/" + pair.Key))
					|| (s.ToLowerInvariant().EndsWith("\\" + pair.Key)))
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
