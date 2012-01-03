using System;
using System.IO;
using System.Xml;
using Palaso.Extensions;
using Palaso.Xml;

namespace Bloom.Book
{
	public class SizeAndOrientation
	{
		public string PageSizeName;
		public bool IsLandScape { get; private set; }

		public string OrientationName
		{
			get { return IsLandScape ? "landscape" : "portrait"; }

		}

		public static SizeAndOrientation FromDom(XmlDocument dom)
		{
			var soa = new SizeAndOrientation();

			var css = GetPaperStyleSheetName(dom);
			int i = css.ToLower().IndexOf("portrait");
			if (i > 0)
			{
				soa.IsLandScape = false;
				soa.PageSizeName = css.Substring(0, i).ToUpperFirstLetter();
				return soa;
			}
			i = css.ToLower().IndexOf("landscape");
			if (i > 0)
			{
				soa.IsLandScape = true;
				soa.PageSizeName = css.Substring(0, i).ToUpperFirstLetter();
				return soa;
			}
			throw new ApplicationException(
				"Bloom could not determine the paper size because it could not find a stylesheet in the document which contained the words 'portrait' or 'landscape'");
		}

		/// <summary>
		/// looks for the css which sets the paper size/orientation
		/// </summary>
		/// <param name="dom"></param>
		private static string GetPaperStyleSheetName(XmlDocument dom)
		{
			foreach (XmlElement linkNode in dom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (href == null)
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if (fileName.ToLower().Contains("portrait") || fileName.ToLower().Contains("landscape"))
				{
					return fileName;
				}
			}
			return String.Empty;
		}
	}
}
