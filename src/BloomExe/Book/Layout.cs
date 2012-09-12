using System;
using System.Collections.Generic;
using System.Xml;
using Palaso.Extensions;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// A Layout is size and orientation, plus options. Currently, there is only one set of options allowed, named "styles"
	/// </summary>
	public class Layout
	{
		/// <summary>
		/// E.g. A4 Landscape
		/// </summary>
		public SizeAndOrientation SizeAndOrientation;

		/// <summary>
		/// Style is what goes in the blank in the layout-style-______ css classes.
		/// </summary>
		public string Style ;


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


		public IEnumerable<string> ClassNames
		{
			get
			{
				yield return SizeAndOrientation.ClassName;
				if(!string.IsNullOrEmpty(Style))
				{
					yield return "layout-style-" + Style;
				}
			}

		}

		public static Layout A5Portrait
		{
			get { return new Layout() {SizeAndOrientation = SizeAndOrientation.FromString("A5Portrait")}; }
		}

		public override string ToString()
		{
			var s = "";
			if (!string.IsNullOrEmpty(Style) && Style.ToLower() != "default")
				s = Style;
			return (SizeAndOrientation.ToString() + " " + s).Trim();
		}

		public static Layout FromDom(XmlDocument dom, Layout defaultIfMissing)
		{
			var firstPage = dom.SelectSingleNode("descendant-or-self::div[contains(@class,'bloom-page')]");
			if (firstPage == null)
				return defaultIfMissing;

			var layout = new Layout {SizeAndOrientation = defaultIfMissing.SizeAndOrientation, Style= defaultIfMissing.Style};

			foreach (var part in firstPage.GetStringAttribute("class").SplitTrimmed(' '))
			{
				if (part.ToLower().Contains("portrait") || part.ToLower().Contains("landscape"))
				{
					layout.SizeAndOrientation = SizeAndOrientation.FromString(part);
				}
				if (part.ToLower().Contains("layout-style-"))
				{
					int startIndex = "layout-style-".Length;
					layout.Style = part.Substring(startIndex, part.Length-startIndex);	//reivew: this might let us suck up a style that is no longer listed in any css
				}
			}
			return layout;
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

				foreach (XmlElement div in leader.SafeSelectNodes("*[contains(@class, 'bloom-trailingElement')]"))
				{
					leader.RemoveChild(div);
				}

				foreach (XmlElement div in trailer.SafeSelectNodes("*[contains(@class, 'bloom-leadingElement')]"))
				{
					trailer.RemoveChild(div);
				}
			}
		}
	}
}