using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Bloom.Edit;
using Newtonsoft.Json;
using Palaso.Code;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Xml;
using Palaso.Extensions;

namespace Bloom.Book
{

	/// <summary>
	/// A Layout is size and orientation, plus options. Currently, there is only one set of options allowed, named "styles"
	/// </summary>
	public class Layout
	{
		public SizeAndOrientation SizeAndOrientation;
		public string Style ;

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
	}


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
			var nameLower = name.ToLower();
			var startOfOrientationName = Math.Max(nameLower.ToLower().IndexOf("landscape"), nameLower.ToLower().IndexOf("portrait"));
			if(startOfOrientationName == -1)
			{
				Debug.Fail("No orientation name found in '"+nameLower+"'");
				return new SizeAndOrientation()
					{
						IsLandScape=false,
						PageSizeName = "A5"
					};
			}
			int startOfAlternativeName=-1;
			if(nameLower.ToLower().Contains("landscape"))
				startOfAlternativeName = startOfOrientationName + "landscape".Length;
			else
				startOfAlternativeName = startOfOrientationName + "portrait".Length;

			return new SizeAndOrientation()
					{
						IsLandScape = nameLower.ToLower().Contains("landscape"),
						PageSizeName = nameLower.Substring(0, startOfOrientationName).ToUpperFirstLetter(),
						//AlternativeName = name.Substring(startOfAlternativeName, nameLower.Length - startOfAlternativeName)
					};
		}

		public static void AddClassesForLayout(XmlNode node, Layout layout)
		{
			UpdatePageSizeAndOrientationClasses(node, layout);
		}

		public static IEnumerable<Layout> GetLayoutChoices(XmlNode node, IFileLocator fileLocator)
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
				var start = contents.IndexOf("STARTLAYOUTS");
				if (start < 0)
					yield break; // continue;//move on to the next stylesheet
				start += "STARTLAYOUTS".Length;
				var end = contents.IndexOf("ENDLAYOUTS",start);
				var s = contents.Substring(start, end - start);

				IEnumerable<Layout> layouts = null;

				try
				{
					layouts = GetConfigurationsFromConfigurationOptionsString(s);
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

		/// <summary>
		/// At rutnime, this string comes out of a dummy css 'content' line. For unit tests, it just comes from the test.
		/// </summary>
		/// <param name="contents"></param>
		/// <returns></returns>
		public static List<Layout> GetConfigurationsFromConfigurationOptionsString(string contents)
		{
			var layouts = new List<Layout>();

				contents = "{\"root\": " + contents + "}";
			//I found it really hard to work with the json libraries, so I just convert it to xml. It's weird xml, but at least it's not like trying to mold smoke.
				XmlDocument doc = (XmlDocument) JsonConvert.DeserializeXmlNode(contents);
				var root = doc.SelectSingleNode("root");


				foreach (XmlElement element in root.SelectNodes("layouts"))
				{
					foreach (var sizeAndOrientation in element.ChildNodes)
					{
						if (sizeAndOrientation is XmlText)
						{
							layouts.Add(new Layout() {SizeAndOrientation = FromString(((XmlText) sizeAndOrientation).InnerText)});
						}
						else if (sizeAndOrientation is XmlElement)
						{
							SizeAndOrientation soa = FromString(((XmlElement) sizeAndOrientation).Name);
							foreach (XmlElement option in ((XmlElement) sizeAndOrientation).ChildNodes)
							{
								if (option.Name.ToLower() != "styles")
									continue;//we don't handle anything else yet
								layouts.Add(new Layout(){SizeAndOrientation = soa, Style=option.InnerText});
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
