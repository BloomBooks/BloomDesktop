using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.SafeXml;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;

namespace Bloom.Book
{
    /// <summary>
    /// A Layout is size and orientation, plus options. Currently, there is only one set of options allowed, named "styles"
    /// </summary>
    public class Layout
    {
        /// <summary>
        /// Style is what goes in the blank in the layout-style-______ css classes.
        /// </summary>
        private string _style;

        /// <summary>
        /// This is used for actually converting between single-page layouts and two-page layouts of the same material
        /// </summary>
        public ElementDistributionChoices ElementDistribution { get; set; }

        public enum ElementDistributionChoices
        {
            CombinedPages = 0,

            /// <summary>
            /// When we're making a book to be held up in class, we often want to take the picture and make it fill
            /// up the left page, and the text and make it large on the facing page.
            /// </summary>
            SplitAcrossPages = 1
        };

        /// <summary>
        /// E.g. A4 Landscape
        /// </summary>
        public SizeAndOrientation SizeAndOrientation;

        public IEnumerable<string> ClassNames
        {
            get
            {
                yield return SizeAndOrientation.ClassName;
                if (!String.IsNullOrEmpty(Style))
                {
                    yield return "layout-style-" + Style;
                }
            }
        }

        public static Layout A5Portrait
        {
            get
            {
                return new Layout()
                {
                    SizeAndOrientation = SizeAndOrientation.FromString("A5Portrait")
                };
            }
        }

        /// <summary>
        /// Style is what goes in the blank in the layout-style-______ css classes.
        /// </summary>
        public string Style
        {
            get { return _style; }
            set
            {
                _style = value;
                //TODO: can we jsut have ElementDist be a property, if it simply mirrors this???
                if (value == "SplitAcrossPages")
                    ElementDistribution = ElementDistributionChoices.SplitAcrossPages;
            }
        }

        public bool IsDeviceLayout
        {
            get { return SizeAndOrientation.ToString().StartsWith("Device"); }
        }

        public override string ToString()
        {
            var s = "";
            if (!String.IsNullOrEmpty(Style) && Style.ToLowerInvariant() != "default")
                s = Style;
            return (SizeAndOrientation.ToString() + " " + s).Trim();
        }

        public string DisplayName
        {
            get
            {
                var pageSizeName = SizeAndOrientation.PageSizeName;
                var orientationName = SizeAndOrientation.OrientationName;
                string englishName;
                // This regex generalizes what is currently just one special case: the Cm13Landscape layout, which is actually square.
                // Its display name should reflect that fact.  We have avoided giving it the internal name 13cmSquare 1) because far too
                // much code in BloomPlayer (and elsewhere in Bloom) would have to be extended to handle three cases instead of just two
                // and 2) because in various places where Cm13Landscape is used a name starting with a number would not work.  So we need
                // to replace the orientationName with "Square" and the PageSizeName with a user-friendly version.  It remains to be seen
                // whether we will have other page size classes that follow this pattern.  ("In5Layout/5in Square" is probably the prime
                // candidate if any users want to print square booklets on Ledger paper instead of A3 paper.)
                var match = Regex.Match(
                    pageSizeName,
                    @"^(cm|in)(\d+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                );
                if (match.Success)
                    englishName =
                        match.Groups[2].Value
                        + match.Groups[1].Value.ToLowerInvariant()
                        + " Square";
                else
                    englishName =
                        pageSizeName.ToUpperFirstLetter()
                        + " "
                        + orientationName.ToUpperFirstLetter();
                var id = "LayoutChoices." + SizeAndOrientation.ClassName;
                if (!String.IsNullOrEmpty(Style) && Style.ToLowerInvariant() != "default")
                {
                    id = id + " " + Style;
                    var splitStyle = Regex.Replace(
                        Style,
                        @"([a-z])([A-Z])",
                        @"$1 $2",
                        RegexOptions.CultureInvariant
                    );
                    englishName = englishName + " (" + splitStyle + ")";
                }

                var englishNameLowerCase = englishName.ToLowerInvariant();
                if (englishNameLowerCase == "uscomic portrait")
                {
                    englishName = "US Comic Portrait";
                }
                else if (englishNameLowerCase == "size6x9 portrait")
                {
                    // Note: Whatever you pass for englishName to Localizationmanager
                    // will win for English (over the value in the localization XLF,
                    // so we need to populate it correctly here.
                    englishName = "6\"x9\" Portrait";
                }
                else if (englishNameLowerCase == "size6x9 landscape")
                {
                    englishName = "6\"x9\" Landscape";
                }

                englishName = englishName.Replace("letter", " Letter");
                englishName = englishName.Replace("legal", " Legal");
                englishName = englishName.Replace("16x9", " 16x9");
                englishName = englishName.Trim();
                var displayName = L10NSharp.LocalizationManager.GetDynamicString(
                    "Bloom",
                    id,
                    englishName
                );

                return displayName;
            }
        }

        public static Layout FromDom(HtmlDom dom, Layout defaultIfMissing)
        {
            var firstPage = dom.SelectSingleNode(
                "descendant-or-self::div[contains(@class,'bloom-page')]"
            );
            if (firstPage == null)
                return defaultIfMissing;

            var layout = new Layout
            {
                SizeAndOrientation = defaultIfMissing.SizeAndOrientation,
                Style = defaultIfMissing.Style
            };

            return FromPage(firstPage, layout);
        }

        public static Layout FromPage(SafeXmlElement page, Layout layout)
        {
            foreach (var part in page.GetAttribute("class").SplitTrimmed(' '))
            {
                if (
                    part.ToLowerInvariant().Contains("portrait")
                    || part.ToLowerInvariant().Contains("landscape")
                )
                {
                    layout.SizeAndOrientation = SizeAndOrientation.FromString(part);
                }

                if (part.ToLowerInvariant().Contains("layout-style-"))
                {
                    int startIndex = "layout-style-".Length;
                    layout.Style = part.Substring(startIndex, part.Length - startIndex); //reivew: this might let us suck up a style that is no longer listed in any css
                }
            }

            return layout;
        }

        public static Layout FromDomAndChoices(
            HtmlDom dom,
            Layout defaultIfMissing,
            IFileLocator fileLocator
        )
        {
            // If the stylesheet's special style which tells us which page/orientations it supports matches the default
            // page size and orientation in the template's bloom-page class, we don't need this method.
            // Otherwise, we need to make sure that the book's layout updates to something that really is a possibility.
            var layout = FromDom(dom, defaultIfMissing);
            layout = EnsureLayoutIsAmongValidChoices(dom, layout, fileLocator);
            return layout;
        }

        private static Layout EnsureLayoutIsAmongValidChoices(
            HtmlDom dom,
            Layout layout,
            IFileLocator fileLocator
        )
        {
            var layoutChoices = SizeAndOrientation.GetSizeAndOrientationChoices(dom, fileLocator);
            if (
                layoutChoices.Any(
                    l => l.SizeAndOrientation.ClassName == layout.SizeAndOrientation.ClassName
                )
            )
                return layout;
            return layoutChoices.Any() ? layoutChoices.First() : layout;
        }

        /// <summary>
        /// At runtime, this string comes out of a dummy css 'content' line. For unit tests, it just comes from the test.
        /// </summary>
        /// <param name="contents"></param>
        /// <returns></returns>
        public static List<Layout> GetConfigurationsFromConfigurationOptionsString(string contents)
        {
            var layouts = new List<Layout>();

            contents = "{\"root\": " + contents + "}";
            //I found it really hard to work with the json libraries, so I just convert it to xml. It's weird xml, but at least it's not like trying to mold smoke.
            var doc = new SafeXmlDocument(JsonConvert.DeserializeXmlNode(contents));
            var root = doc.SelectSingleNode("root");

            foreach (SafeXmlElement element in root.SafeSelectNodes("layouts"))
            {
                foreach (var sizeAndOrientation in element.ChildNodes)
                {
                    if (sizeAndOrientation is SafeXmlText)
                    {
                        layouts.Add(
                            new Layout()
                            {
                                SizeAndOrientation = SizeAndOrientation.FromString(
                                    ((SafeXmlText)sizeAndOrientation).InnerText
                                )
                            }
                        );
                    }
                    else if (sizeAndOrientation is SafeXmlElement)
                    {
                        var soa = SizeAndOrientation.FromString(
                            ((SafeXmlElement)sizeAndOrientation).Name
                        );
                        foreach (SafeXmlElement option in ((SafeXmlElement)sizeAndOrientation).ChildNodes)
                        {
                            if (option.Name.ToLowerInvariant() != "styles")
                                continue; //we don't handle anything else yet
                            layouts.Add(
                                new Layout() { SizeAndOrientation = soa, Style = option.InnerText }
                            );
                        }
                    }
                }
            }

            return layouts;
        }

        public void UpdatePageSplitMode(SafeXmlNode node)
        {
            //NB: this can currently only split pages, not move them together. Doable, just not called for by the UI or unit tested yet.

            if (ElementDistribution == ElementDistributionChoices.CombinedPages)
                return;

            var combinedPages = node.SafeSelectNodes(
                "descendant-or-self::div[contains(@class,'bloom-combinedPage')]"
            );
            foreach (SafeXmlElement pageDiv in combinedPages)
            {
                SafeXmlElement trailer = (SafeXmlElement)pageDiv.CloneNode(true);
                pageDiv.ParentNode.InsertAfter(trailer, pageDiv);

                pageDiv.SetAttribute(
                    "class",
                    pageDiv.GetAttribute("class").Replace("bloom-combinedPage", "bloom-leadingPage")
                );
                var leader = pageDiv;
                trailer.SetAttribute(
                    "class",
                    trailer
                        .GetAttribute("class")
                        .Replace("bloom-combinedPage", "bloom-trailingPage")
                );

                //give all new ids to both pages

                leader.SetAttribute("id", Guid.NewGuid().ToString());
                trailer.SetAttribute("id", Guid.NewGuid().ToString());

                //now split the elements

                leader.DeleteNodes(
                    "descendant-or-self::*[contains(@class, 'bloom-trailingElement')]"
                );
                trailer.DeleteNodes(
                    "descendant-or-self::*[contains(@class, 'bloom-leadingElement')]"
                );
            }
        }
    }
}
