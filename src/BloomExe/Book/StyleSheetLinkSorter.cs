using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml;
using Palaso.Xml;

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
				_values.Add("languagedisplay.css", 20); // the opening bid
				_values.Add("editmode.css", 30);
				_values.Add("editoriginalmode.css", 40);
				_values.Add("previewmode.css", 50);

				//Note that kDefaultValueForStyleSheetsThatShouldListInTheMiddle should fall in between here
				//for the template-specific stuff, but we don't know those names

				_values.Add("settingsCollectionStyles.css".ToLower(), 1000);
				_values.Add("customCollectionStyles.css".ToLower(), 2000); // the almost last word
				_values.Add("customBookStyles.css".ToLower(), 3000); // the very last word
			}
		}

		public int Compare(XmlElement a, XmlElement b)
		{
			Init();

			var x = a.GetStringAttribute("href").ToLower();
			var y = b.GetStringAttribute("href").ToLower();

			int xValue = GetValue(x);
			int yValue = GetValue(y);

			Debug.WriteLine(string.Format("Comparing {0}({1}) and {2}({3})", x,xValue,y,yValue));
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
				if (s.ToLower() == pair.Key	//no path in there
					|| (s.ToLower().EndsWith("/"+pair.Key))
					|| (s.ToLower().EndsWith("\\"+pair.Key)))
					return pair.Value;
			}
			return kDefaultValueForStyleSheetsThatShouldListInTheMiddle;
		}
	}
}