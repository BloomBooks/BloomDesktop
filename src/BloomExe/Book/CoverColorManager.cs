using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Bloom.Book
{

	// This is a (hopefully) temporary collection of all the cover-color handling code that was current spread around,
	// except for that in AppearanceSettings, which is where we probably want things to end up.
	internal class CoverColorManager
	{
		//We only randomize the initial value for each run. Without it, we were making a lot
		// more red books than any other color, because the
		//first book for a given run would always be red, and people are unlikely to make more
		//than one book per session.
		private static int s_coverColorIndex = 0;// new Random().Next(CoverColors.Length - 1);

		private static string[] CoverColors = {"#E48C84", "#B0DEE4", "#98D0B9", "#C2A6BF"};

		AppearanceSettings _appearanceSettings;
		public CoverColorManager(HtmlDom dom, BookInfo bookInfo, AppearanceSettings appearanceSettings)
		{
			_dom = dom;
			_bookInfo = bookInfo;
			_appearanceSettings = appearanceSettings;
		}

		private void foo()
		{

			// If we're showing the user one of our built-in templates (like Basic Book), pick a color for it.
			// If it is editable or from bloomlibrary or a BloomPack, then we don't want to change to the next color,
			// we want to use the color that we used for the sample shell/template we showed them previously.
			// (BL-11490 Even shells or downloaded books should preserve the original cover color.)
			if (!_bookInfo.IsEditable && Path.GetDirectoryName(_bookInfo.FolderPath) == BloomFileLocator.FactoryTemplateBookDirectory)
			{
				SelectNextCoverColor(); // we only increment when showing a built-in template
				InitCoverColor();
			}

			// If it doesn't already have a cover color, give it one.
			if (HtmlDom.GetCoverColorStyleElement(_dom.Head) == null)
			{
				InitCoverColor(); // should use the same color as what they saw in the preview of the template/shell
			}
		}

		/// <summary>
		/// This just increments the color index so that the next book to be constructed that doesn't already have a color will use it
		/// </summary>
		public static void SelectNextCoverColor()
		{
			s_coverColorIndex = s_coverColorIndex + 1;
			if (s_coverColorIndex >= CoverColors.Length)
				s_coverColorIndex = 0;
		}

		public HtmlDom _dom { get; }

		private BookInfo _bookInfo;

		public void InitCoverColor()
		{
			// for digital comic template, we want a black cover.
			// NOTE as this writing, at least, xmatter cannot set <meta> values, so this isn't a complete solution. It's only
			// useful for starting off a book from a template book.
			var preserve = this._dom.GetMetaValue("preserveCoverColor", "false");
			if (preserve == "false")
			{
				WriteCoverColorToDom(this._dom, _appearanceSettings.CoverColor);
			}
		}

		public static void WriteCoverColorToDom(HtmlDom dom, string coverColor)
		{
			// remove any existing styles that set the cover color
			var regex =
					new Regex(
						@"((DIV|div).(coverColor\s*TEXTAREA|bloom-page.coverColor)\s*{\s*background-color:\s*)([#,\w]*)");

			var x = dom.SafeSelectNodes("//style");
			foreach (XmlElement stylesheet in x)
			{
				if (regex.Match(stylesheet.InnerText).Success)
				{
					stylesheet.ParentNode.RemoveChild(stylesheet);
				}
			}

			// add the new style rule
			XmlElement colorStyle = dom.RawDom.CreateElement("style");
			colorStyle.SetAttribute("type", "text/css");
			// let's see if we can get away from this !important
			//			colorStyle.InnerXml = $"DIV.bloom-page.coverColor {{ background-color: {coverColor} !important;}}";
			colorStyle.InnerXml = $"DIV.bloom-page.coverColor {{ background-color: {coverColor}}}";
			dom.Head.AppendChild(colorStyle);

			/* Note, in a unit test I see vestages of rule for textarea, which we're not currently handling
			 			DIV.coverColor  TEXTAREA {
						background-color: #B2CC7D !important;
					}
			*/
		}

		public String GetCoverColor()
		{
			return _appearanceSettings.CoverColor;
		}

		public static String GetCoverColorFromDom(HtmlDom dom)
		{
			foreach (XmlElement stylesheet in dom.SafeSelectNodes("//style"))
			{
				var content = stylesheet.InnerText;
				// Our XML representation of an HTML DOM doesn't seem to have any object structure we can
				// work with. The Stylesheet content is just raw CDATA text.
				// Regex updated to handle comments and lowercase 'div' in the cover color rule.
				var match = new Regex(
					@"(DIV|div).bloom-page.coverColor\s*{.*?background-color:\s*(#[0-9a-fA-F]*|[a-z]*)",
					RegexOptions.Singleline).Match(content);
				if (match.Success)
				{
					return match.Groups[2].Value;
				}
			}
			return "#FFFFFF";
		}

		/// <summary>
		/// Internal method is testable
		/// </summary>
		/// <param name="color"></param>
		/// <returns>true if a change was made</returns>
		internal bool SetCoverColorInternal(string color)
		{
			foreach (XmlElement stylesheet in _dom.SafeSelectNodes("//style"))
			{
				string content = stylesheet.InnerXml;
				var regex =
					new Regex(
						@"(DIV.(coverColor\s*TEXTAREA|bloom-page.coverColor)\s*{\s*background-color:\s*)([#,\w]*)",
						RegexOptions.IgnoreCase | RegexOptions.Multiline);
				if (!regex.IsMatch(content))
					continue;
				var newContent = regex.Replace(content, "$1" + color);
				stylesheet.InnerXml = newContent;
				return true;
			}

			return false;
		}
	}
}
