using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// NB: html class names are case sensitive! In this code, we want to accept stuff regardless of case, but always generate Capitalized paper size and orientation names
	/// </summary>
	public class SizeAndOrientation
	{
		public string PageSizeName;
		public bool IsLandScape { get; set; }

		public string OrientationName
		{
			get { return IsLandScape ? "Landscape" : "Portrait"; }

		}
//
//		public static SizeAndOrientation FromDom(XmlDocument dom)
//		{
//			var soa = new SizeAndOrientation();
//
//			var css = GetPaperStyleSheetName(dom);
//			int i = css.ToLower().IndexOf("portrait");
//			if (i > 0)
//			{
//				soa.IsLandScape = false;
//				soa.PageSizeName = css.Substring(0, i).ToUpperFirstLetter();
//				return soa;
//			}
//			i = css.ToLower().IndexOf("landscape");
//			if (i > 0)
//			{
//				soa.IsLandScape = true;
//				soa.PageSizeName = css.Substring(0, i).ToUpperFirstLetter();
//				return soa;
//			}
//			throw new ApplicationException(
//				"Bloom could not determine the paper size because it could not find a stylesheet in the document which contained the words 'portrait' or 'landscape'");
//		}

//		/// <summary>
//		/// looks for the css which sets the paper size/orientation
//		/// </summary>
//		/// <param name="dom"></param>
//		private static string GetPaperStyleSheetName(XmlDocument dom)
//		{
//			foreach (XmlElement linkNode in dom.SafeSelectNodes("/html/head/link"))
//			{
//				var href = linkNode.GetAttribute("href");
//				if (href == null)
//				{
//					continue;
//				}
//
//				var fileName = Path.GetFileName(href);
//				if (fileName.ToLower().Contains("portrait") || fileName.ToLower().Contains("landscape"))
//				{
//					return fileName;
//				}
//			}
//			return String.Empty;
//		}

		public override string ToString()
		{
			return PageSizeName + OrientationName;
		}

		public static SizeAndOrientation FromString(string name)
		{
			name = name.ToLower();
			var startOfOrientationName = Math.Max(name.ToLower().IndexOf("landscape"), name.ToLower().IndexOf("portrait"));
			if(startOfOrientationName == -1)
			{
				Debug.Fail("No orientation name found in '"+name+"'");
				return new SizeAndOrientation()
					{
						IsLandScape=false,
						PageSizeName = "A5"
					};
			}
			return new SizeAndOrientation()
					{
						IsLandScape = name.ToLower().Contains("landscape"),
						PageSizeName = name.Substring(0, startOfOrientationName).ToUpperFirstLetter()
					};
		}

		public static void SetPaperSizeAndOrientation(XmlNode node, string paperSizeAndOrientationName)
		{
			UpdatePageSizeAndOrientationClasses(node, paperSizeAndOrientationName);
		}

		public static IEnumerable<string> GetPageSizeAndOrientationChoices(XmlNode node, IFileLocator fileLocator)
		{
			//here we walk through all the stylesheets, looking for one with the special style which tells us which page/orientations it supports
			foreach (XmlElement link in node.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				if (fileName.ToLower().Contains("mode") || fileName.ToLower().Contains("page") ||
					fileName.ToLower().Contains("matter"))
					continue;


				var path = fileLocator.LocateFile(fileName);
				if(string.IsNullOrEmpty(path))
				{
					throw new ApplicationException("Could not locate "+fileName);
				}
				var contents = File.ReadAllText(path);
				var i = contents.IndexOf("#bloom-supportedPageConfigurations");
				if (i < 0)
					continue;//move on to the next stylesheet
				i = contents.IndexOf("content:", i);
				var start = 1 + contents.IndexOf("\"", i);
				var end = contents.IndexOf("\"", start);
				var s = contents.Substring(start, end - start);
				foreach (var part in s.SplitTrimmed(','))
				{
					yield return part;
				}
				yield break;
			}

			//default to A5Portrait
			yield return "A5Portrait";
		}

		public static SizeAndOrientation GetSizeAndOrientation(XmlDocument dom, string defaultIfMissing)
		{
			var firstPage = dom.SelectSingleNode("descendant-or-self::div[contains(@class,'bloom-page')]");
			if (firstPage == null)
				return FromString(defaultIfMissing);
			string sao = defaultIfMissing;
			foreach (var part in firstPage.GetStringAttribute("class").SplitTrimmed(' '))
			{
				if (part.ToLower().Contains("portrait") || part.ToLower().Contains("landscape"))
				{
					sao = part;
					break;
				}
			}
			return FromString(sao);
		}

		public static void UpdatePageSizeAndOrientationClasses(XmlNode node, string sizeAndOrientation)
		{
			foreach (XmlElement pageDiv in node.SafeSelectNodes("descendant-or-self::div[contains(@class,'bloom-page')]"))
			{
				RemoveClassesContaining(pageDiv, "Landscape");
				RemoveClassesContaining(pageDiv, "Portrait");
				AddClass(pageDiv, sizeAndOrientation);
			}
		}

		private static void RemoveClassesContaining(XmlElement xmlElement, string substring)
		{
			var classes = xmlElement.GetAttribute("class");
			if (string.IsNullOrEmpty(classes))
				return;
			var parts = classes.SplitTrimmed(' ');

			classes = "";
			foreach (var part in parts)
			{
				if (!part.ToLower().Contains(substring.ToLower()))
					classes += part + " ";
			}
			xmlElement.SetAttribute("class", classes.Trim());
		}
		private static void AddClass(XmlElement e, string className)
		{
			e.SetAttribute("class", (e.GetAttribute("class") + " " + className).Trim());
		}
	}
}
