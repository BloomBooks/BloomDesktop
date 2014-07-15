using System;
using System.Globalization;
using System.Xml;
using Chorus.merge.xml.generic;
using Palaso.Code;

namespace Bloom_ChorusPlugin
{
	class BloomPageContextGenerator : IGenerateContextDescriptor, IGenerateContextDescriptorFromNode, IGenerateHtmlContext
	{
		#region Implementation of IGenerateContextDescriptor

		/// <summary>
		/// IGenerateContextDescriptor requires this method to exist, but if IGenerateContextDescriptorFromNode is implemented,
		/// this one won't be used. Chorus will call the XmlNode version instead.
		/// </summary>
		/// <param name="mergeElement"></param>
		/// <param name="filePath"></param>
		/// <returns></returns>
		public ContextDescriptor GenerateContextDescriptor(string mergeElement, string filePath)
		{
			return null; // to satisfy compiler
		}

		#endregion

		#region Implementation of IGenerateContextDescriptorFromNode

		/// <summary>
		/// If the ContextDescriptorGenerator implements this interface, it will be called instead of
		/// the IGenerateContextDescriptor version.
		/// </summary>
		public ContextDescriptor GenerateContextDescriptor(XmlNode mergeNode, string filePath)
		{
			var name = mergeNode.Name;
			var label = "unknown";
			switch (name)
			{
				case "html":
					label = "BloomBook";
					break;
				case "head":
					label = "BloomBook header";
					break;
				case "meta":
					label = "BloomBook meta element";
					break;
				case "title":
					label = "BloomBook title";
					break;
				case "link":
					label = "BloomBook stylesheet";
					break;
				case "body":
					label = "BloomBook body";
					break;
				case "img":
					label = "BloomBook page imageContainer image";
					break;
				case "div":
					label = "BloomBook body unknownDiv";
					var mergeElement = mergeNode as XmlElement;
					if (mergeElement == null)
						break;

					if (mergeElement.HasAttribute("id") && mergeElement.GetAttribute("id") == "bloomDataDiv")
					{
						label = "BloomBook DataDiv";
						break;
					}
					var classes = mergeElement.Attributes["class"].Value;
					if (classes.Contains("bloom-page"))
					{
						label = "BloomBook page";
						if (classes.Contains("bloom-frontMatter"))
							label = "BloomBook page frontMatter";
						else if (classes.Contains("bloom-backMatter"))
							label = "BloomBook page backMatter";
					}
					else if (classes.Contains("marginBox"))
						label = "BloomBook page margins";
					else if (classes.Contains("bloom-translationGroup"))
						label = "BloomBook page group";
					else if (classes.Contains("bloom-editable") && mergeElement.HasAttribute("lang"))
						label = "BloomBook page group lang";
					break;
			}

			return new ContextDescriptor(label, filePath);
		}

		#endregion

		#region Implementation of IGenerateHtmlContext

		public string HtmlContext(XmlNode mergeElement)
		{
			Guard.Against(mergeElement == null, "mergeElement was null");

			var pageElement = FindNearestBloomPageElement(mergeElement);
			var pageId = pageElement.GetAttribute("id"); // guid of bloom-page
			var pageNumber = FindPageNumber(mergeElement, pageId);
			var context = "<div><div class='pageInfo' id='" + pageId +"'>Page number: " + pageNumber + "</div>" + mergeElement.OuterXml + "</div>";
			return XmlUtilities.GetXmlForShowingInHtml(context);
		}

		private string FindPageNumber(XmlNode mergeElement, string pageId)
		{
			var pageNumber = 0;
			const string xpath = "//div[contains(@class,'bloom-page')]";
			var document = mergeElement.OwnerDocument;
			if (document == null)
				return pageNumber.ToString(CultureInfo.CurrentUICulture);
			foreach (XmlElement page in document.SelectNodes(xpath))
			{
				pageNumber++;
				if (page.HasAttribute("id") && page.GetAttribute("id") == pageId)
					return pageNumber.ToString(CultureInfo.CurrentUICulture);
			}
			return "not found";
		}

		private static XmlElement FindNearestBloomPageElement(XmlNode mergeElement)
		{
			const string xpath = "self::div[contains(@class,'bloom-page')]";
			string elementName;
			do
			{
				elementName = mergeElement.Name;
				if (elementName == "div")
				{
					var pageNode = mergeElement.SelectSingleNode(xpath);
					if (pageNode != null)
						return pageNode as XmlElement;
				}
				mergeElement = mergeElement.ParentNode;
			} while (elementName != "body");
			throw new ArgumentException("No bloom-page element found", "mergeElement");
		}

		public string HtmlContextStyles(XmlNode mergeElement)
		{
			// TODO: Do we need anything here?
			return "";
		}

		#endregion
	}
}
