using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bloom.SafeXml;
using SIL.Linq;
using SIL.Xml;

namespace Bloom.Book
{
    /// <summary>
    /// Since the precedence of stylesheet rules is influence by the their order (and thus the order of
    /// the various stylesheet declarations), we sort them to in a way that make sense and is consistent
    /// </summary>
    public class StyleSheetLinkSorter : IComparer<SafeXmlElement>
    {
        public static readonly string[] KnownCssFilePrefixesInOrder =
            BookStorage.OrderedPrefixesOfCssFilesToSortBeforeUnknownStylesheets
                .Append("UNKNOWN_STYLESHEETS_HERE")
                .Concat(BookStorage.OrderedPrefixesOfCssFilesToSortAfterUnknownStylesheets)
                .Append("pageControls.css")
                .Append("pageThumbnailList.css")
                .ToArray();

        const int kDefaultValueForStyleSheetsThatShouldListInTheMiddle = 100;

        // This is set up as a dictionary, but it's really just a list of pairs.
        // The value is the order in which the stylesheets should appear in the list.
        // The key can be a prefix of a filename directly in the book folder
        // (e.g., basePage, which matches basePage.css or basePage-legacy-5-6.css)
        // or a complete filename, which matches hrefs that have a path with exactly
        // that as the last component (e.g., ../customCollectionStyles.css).
        // The need to do prefix matching rules out actually using it as a map.
        private static Dictionary<string, int> _values;

        private static void Init()
        {
            if (_values == null)
            {
                _values = new Dictionary<string, int>();
                var weight = 0;
                KnownCssFilePrefixesInOrder.ForEach(x =>
                {
                    if (x == "UNKNOWN_STYLESHEETS_HERE")
                        weight = kDefaultValueForStyleSheetsThatShouldListInTheMiddle + 1000;
                    else
                        _values.Add(x.ToLowerInvariant(), weight++);
                });
            }
        }

        public int Compare(SafeXmlElement a, SafeXmlElement b)
        {
            Init();

            var x = a.GetAttribute("href").ToLowerInvariant();
            var y = b.GetAttribute("href").ToLowerInvariant();

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
                if (
                    s.StartsWith(key) //no path in there
                    || (s.EndsWith("/" + key))
                    || (s.EndsWith("\\" + key))
                )
                    return pair.Value;
            }

            // "SHRP Labels.css" is used by the SIL LEAD SHRP project to inject vernacular labels for sections of the book
            // we just need it to always come after the other stylesheet(s) of the book, which may supply default
            // labels
            if (s.EndsWith("labels.css"))
                return kDefaultValueForStyleSheetsThatShouldListInTheMiddle + 1;

            return kDefaultValueForStyleSheetsThatShouldListInTheMiddle;
        }
    }
}
