using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Xml;

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
		private string _style ;


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

		public Boolean IsFullBleed;

		public IEnumerable<string> ClassNames
		{
			get
			{
				yield return SizeAndOrientation.ClassName;
				if(!String.IsNullOrEmpty(Style))
				{
					yield return "layout-style-" + Style;
				}

				if (IsFullBleed)
				{
					yield return "bloom-fullBleed";
				}
			}

		}

		public static Layout A5Portrait
		{
			get { return new Layout() {SizeAndOrientation = SizeAndOrientation.FromString("A5Portrait")}; }
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
				var match = Regex.Match(pageSizeName, @"^(cm|in)(\d+)$",
					RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
				if (match.Success)
					englishName = match.Groups[2].Value + match.Groups[1].Value.ToLowerInvariant() + " Square";
				else
					englishName = pageSizeName.ToUpperFirstLetter() + " " + orientationName.ToUpperFirstLetter();
				var id = "LayoutChoices." + SizeAndOrientation.ClassName;
				if (!String.IsNullOrEmpty(Style) && Style.ToLowerInvariant() != "default")
				{
					id = id + " " + Style;
					var splitStyle = Regex.Replace(Style, @"([a-z])([A-Z])", @"$1 $2", RegexOptions.CultureInvariant);
					englishName = englishName + " (" + splitStyle + ")";
				}
				var displayName = L10NSharp.LocalizationManager.GetDynamicString("Bloom", id, englishName);
				return displayName;
			}
		}

		// I'm not sure we want to display Layouts with the Edge-to-edge at the end...at least, changing them
		// like that might have unexpected consequences and would need careful investigation. Currently, names in this
		// form are only used in STARTLAYOUT blocks to specify the layouts a book supports.
		public static Layout FromString(string name)
		{
			var fullBleed = false;
			const string fbMarker = " edge-to-edge";
			if (name.ToLowerInvariant().EndsWith(fbMarker))
			{
				name = name.Substring(0, name.Length - fbMarker.Length);
				fullBleed = true;
			}

			return new Layout()
				{SizeAndOrientation = SizeAndOrientation.FromString(name), IsFullBleed = fullBleed};
		}

		public static Layout FromDom(HtmlDom dom, Layout defaultIfMissing)
		{
			var firstPage = dom.SelectSingleNode("descendant-or-self::div[contains(@class,'bloom-page')]");
			if (firstPage == null)
				return defaultIfMissing;

			var layout = new Layout {SizeAndOrientation = defaultIfMissing.SizeAndOrientation, Style= defaultIfMissing.Style};

			return FromPage(firstPage, layout);
		}

		public static Layout FromPage(XmlElement page, Layout layout)
		{
			foreach (var part in page.GetStringAttribute("class").SplitTrimmed(' '))
			{
				if (part.ToLowerInvariant().Contains("portrait") || part.ToLowerInvariant().Contains("landscape"))
				{
					layout.SizeAndOrientation = SizeAndOrientation.FromString(part);
				}

				if (part.ToLowerInvariant().Contains("layout-style-"))
				{
					int startIndex = "layout-style-".Length;
					layout.Style =
						part.Substring(startIndex,
							part.Length -
							startIndex); //reivew: this might let us suck up a style that is no longer listed in any css
				}

				if (part == "bloom-fullBleed")
				{
					layout.IsFullBleed = true;
				}
			}

			return layout;
		}

		public static Layout FromDomAndChoices(HtmlDom dom, Layout defaultIfMissing, IFileLocator fileLocator, string firstStylesheetToSearch = null)
		{
			// If the stylesheet's special style which tells us which page/orientations it supports matches the default
			// page size and orientation in the template's bloom-page class, we don't need this method.
			// Otherwise, we need to make sure that the book's layout updates to something that really is a possibility.
			var layout = FromDom(dom, defaultIfMissing);
			layout = EnsureLayoutIsAmongValidChoices(dom, layout, fileLocator, firstStylesheetToSearch);
			return layout;
		}

		private static Layout EnsureLayoutIsAmongValidChoices(HtmlDom dom, Layout layout, IFileLocator fileLocator, string firstStylesheetToSearch = null)
		{
			var layoutChoices = SizeAndOrientation.GetLayoutChoices(dom, fileLocator, firstStylesheetToSearch);
			if (layoutChoices.Any(l => l.SizeAndOrientation.ClassName == layout.SizeAndOrientation.ClassName && l.IsFullBleed == layout.IsFullBleed))
				return layout;
			// Is there one that is the same size, just different fullBleed option?
			var sameSize = layoutChoices.FirstOrDefault(l =>
				l.SizeAndOrientation.ClassName == layout.SizeAndOrientation.ClassName);
			if (sameSize != null)
				return sameSize;
			return layoutChoices.Any() ?  layoutChoices.First() : layout;
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
			XmlDocument doc = (XmlDocument)JsonConvert.DeserializeXmlNode(contents);
			var root = doc.SelectSingleNode("root");


			foreach (XmlElement element in root.SelectNodes("layouts"))
			{
				foreach (var sizeAndOrientation in element.ChildNodes)
				{
					if (sizeAndOrientation is XmlText)
					{
						layouts.Add(Layout.FromString(((XmlText)sizeAndOrientation).InnerText));
					}
					else if (sizeAndOrientation is XmlElement)
					{
						SizeAndOrientation soa = SizeAndOrientation.FromString(((XmlElement)sizeAndOrientation).Name);
						foreach (XmlElement option in ((XmlElement)sizeAndOrientation).ChildNodes)
						{
							if (option.Name.ToLowerInvariant() != "styles")
								continue;//we don't handle anything else yet
							layouts.Add(new Layout() { SizeAndOrientation = soa, Style = option.InnerText });
							//								List<string> choices = null;
							//								if (!soa.Options.TryGetValue(option.Name, out choices))
							//								{
							//									choices = new List<string>();
							//								}
							//								else
							//								{
							//									soa.Options.Remove(option.Name);
							//								}
							//
							//								foreach (XmlText choice in option.ChildNodes)
							//								{
							//									choices.Add(choice.Value);
							//								}
							//								soa.Options.Add(option.Name, choices);
						}
						//							layouts.Add(soa);
					}
				}
			}



			return layouts;
		}

		public  void UpdatePageSplitMode(XmlNode node)
		{
			//NB: this can currently only split pages, not move them together. Doable, just not called for by the UI or unit tested yet.

			if (ElementDistribution == ElementDistributionChoices.CombinedPages)
				return;

			var combinedPages = node.SafeSelectNodes("descendant-or-self::div[contains(@class,'bloom-combinedPage')]");
			foreach (XmlElement pageDiv in combinedPages)
			{
				XmlElement trailer = (XmlElement) pageDiv.CloneNode(true);
				pageDiv.ParentNode.InsertAfter(trailer, pageDiv);

				pageDiv.SetAttribute("class", pageDiv.GetAttribute("class").Replace("bloom-combinedPage", "bloom-leadingPage"));
				var leader = pageDiv;
				trailer.SetAttribute("class", trailer.GetAttribute("class").Replace("bloom-combinedPage", "bloom-trailingPage"));

				//give all new ids to both pages

				leader.SetAttribute("id", Guid.NewGuid().ToString());
				trailer.SetAttribute("id", Guid.NewGuid().ToString());

				//now split the elements

				leader.DeleteNodes("descendant-or-self::*[contains(@class, 'bloom-trailingElement')]");
				trailer.DeleteNodes("descendant-or-self::*[contains(@class, 'bloom-leadingElement')]");
			}
		}
	}
}
