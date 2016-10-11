using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// NB: html class names are case sensitive! In this code, we want to accept stuff regardless of case, but always generate Capitalized paper size and orientation names
	/// </summary>
	public class SizeAndOrientation
	{
		public string PageSizeName;
		//public Dictionary<string /*option label*/, List<string>/*choices for that option*/> Options { get; set; }
		public bool IsLandScape { get; set; }

		public SizeAndOrientation()
		{
			//Options = new Dictionary<string, List<string>>();
		}
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

		/// <summary>
		/// THe normal descriptors are things like "a5portrait". This would turn that in "A5 Portrait" (in the current UI lang, eventually)
		/// </summary>
		/// <param name="sizeAndOrientationDescriptor"></param>
		/// <returns></returns>
		public static string GetDisplayName(string sizeAndOrientationDescriptor)
		{
			var so = FromString(sizeAndOrientationDescriptor);
			return so.PageSizeName.ToUpperFirstLetter() + " " + so.OrientationName.ToUpperFirstLetter();
		}

		public static SizeAndOrientation FromString(string name)
		{
			var nameLower = name.ToLowerInvariant();
			var startOfOrientationName = Math.Max(nameLower.IndexOf("landscape"), nameLower.IndexOf("portrait"));
			if(startOfOrientationName == -1)
			{
				Debug.Fail("No orientation name found in '"+nameLower+"'");
				return new SizeAndOrientation()
					{
						IsLandScape=false,
						PageSizeName = "A5"
					};
			}

			return new SizeAndOrientation()
					{
						IsLandScape = nameLower.Contains("landscape"),
						PageSizeName = ExtractPageSizeName(name, startOfOrientationName),
					};
		}

		private static string ExtractPageSizeName(string nameLower, int startOfOrientationName)
		{
			var name = nameLower.Substring(0, startOfOrientationName).ToUpperFirstLetter();
			//these are needed so that "HalfLetter" doesn't come out "Halfletter"
			name = name.Replace("letter", "Letter");
			name = name.Replace("legal", "Legal");
			return name;
		}

		public static void AddClassesForLayout(HtmlDom dom, Layout layout)
		{
			UpdatePageSizeAndOrientationClasses(dom.RawDom, layout);
		}

		public static IEnumerable<Layout> GetLayoutChoices(HtmlDom dom, IFileLocator fileLocator)
		{
			//here we walk through all the stylesheets, looking for one with the special style which tells us which page/orientations it supports
			foreach (XmlElement link in dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				if (fileName.ToLowerInvariant().Contains("mode") || fileName.ToLowerInvariant().Contains("page") ||
					fileName.ToLowerInvariant().Contains("matter") || fileName.ToLowerInvariant().Contains("languagedisplay"))
					continue;

				fileName = fileName.Replace("file://", "").Replace("%5C", "/").Replace("%20", " ");
				var path = fileLocator.LocateFile(fileName);
				if(string.IsNullOrEmpty(path))
				{
					// We're looking for a block of json that is typically found in Basic Book.css or a comparable place for
					// a book based on some other template. Caling code is prepared for not finding this block.
					// It seems safe to ignore a reference to some missing style sheet.
					NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, "Could not find " + fileName+" while looking for size choices");
					continue;
				}
				var contents = RobustFile.ReadAllText(path);
				var start = contents.IndexOf("STARTLAYOUTS");
				if (start < 0)
					 continue; //yield break; // continue;//move on to the next stylesheet
				start += "STARTLAYOUTS".Length;
				var end = contents.IndexOf("ENDLAYOUTS",start);
				var s = contents.Substring(start, end - start);

				IEnumerable<Layout> layouts = null;

				try
				{
					layouts = Layout.GetConfigurationsFromConfigurationOptionsString(s);
				}
				catch (Exception e)
				{
					throw new ApplicationException("Problem parsing the 'layouts' comment of " + fileName + ". The contents were\r\n" + s, e);
				}


				foreach (var p in layouts)
				{
					yield return p;
				}
				yield break;

			}

			//default to A5Portrait
			yield return new Layout {SizeAndOrientation = FromString("A5Portrait")};
		}


		public static SizeAndOrientation GetSizeAndOrientation(XmlDocument dom, string defaultIfMissing)
		{
			var firstPage = dom.SelectSingleNode("descendant-or-self::div[contains(@class,'bloom-page')]");
			if (firstPage == null)
				return FromString(defaultIfMissing);
			string sao = defaultIfMissing;
			foreach (var part in firstPage.GetStringAttribute("class").SplitTrimmed(' '))
			{
				if (part.ToLowerInvariant().Contains("portrait") || part.ToLowerInvariant().Contains("landscape"))
				{
					sao = part;
					break;
				}
			}
			return FromString(sao);
		}

		public static void UpdatePageSizeAndOrientationClasses(XmlNode node, Layout layout)
		{
			foreach (XmlElement pageDiv in node.SafeSelectNodes("descendant-or-self::div[contains(@class,'bloom-page')]"))
			{
				RemoveClassesContaining(pageDiv, "layout-");
				RemoveClassesContaining(pageDiv, "Landscape");
				RemoveClassesContaining(pageDiv, "Portrait");

				foreach (var cssClassName in layout.ClassNames)
				{
					AddClass(pageDiv, cssClassName);
				}
			}
		}

		public string ClassName
		{
			get { return PageSizeName + (IsLandScape ? "Landscape" : "Portrait"); }

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
				if (!part.ToLowerInvariant().Contains(substring.ToLower()))
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
