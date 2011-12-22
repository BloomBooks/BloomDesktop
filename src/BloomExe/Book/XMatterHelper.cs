using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// Handles chores related to Front and Back Matter packs.
	/// These allow users to have a set which fits there country/organizational situation, and have it applied
	/// to all books they create, whether original or from shells created in other country/organization contexts.
	/// </summary>
	public class XMatterHelper
	{
		private readonly XmlDocument _dom;
		private readonly string _nameOfXMatterPack;

		/// <summary>
		/// Constructs by finding the file and folder of the xmatter pack, given the its key name e.g. "Factory", "SILIndonesia".
		/// The name of the file should be (key)-XMatter.htm. The name and the location of the folder is not our problem...
		/// we leave it to the supplied fileLocator to find it.
		/// </summary>
		/// <param name="nameOfXMatterPack">e.g. "Factory", "SILIndonesia"</param>
		/// <param name="fileLocator">The locator needs to be able tell use the path to an xmater html file, given its name</param>
		public XMatterHelper(XmlDocument dom, string nameOfXMatterPack, IFileLocator fileLocator)
		{
			_dom = dom;
			_nameOfXMatterPack = nameOfXMatterPack;
			var fileName = nameOfXMatterPack + "-XMatter.htm";
			PathToXMatterHtml = fileLocator.LocateFile(fileName, "xmatter html file named " + fileName);
			string styleSheetFileName = GetStyleSheetFileName();
			PathToStyleSheetForPaperAndOrientation = fileLocator.LocateFile(styleSheetFileName, "css stylesheet named " + styleSheetFileName);
		}


		public string GetStyleSheetFileName()
		{
			var sizeAndOrientation = SizeAndOrientation.FromDom(_dom);
			return sizeAndOrientation.PageSizeName + "-" + sizeAndOrientation.OrientationName + "-" + _nameOfXMatterPack + "-XMatter.css";
		}

		/// <summary>
		/// Set this if you want the xmatter stuff copied into a document folder. This makes sense when setting up or
		/// modifying one of the users books, but not when just displaying a book from a shell collection.
		/// </summary>
		public string FolderPathForCopyingXMatterFiles { get; set; }

		/// <summary>
		/// Give the detected paper size and orientation, this is the verified location of the corresponding stylesheet for the selected xmatter pack.
		/// </summary>
		public string PathToStyleSheetForPaperAndOrientation { get; set; }

		/// <summary>
		/// This exists in an XMatter-pack folder, which we infer from this file location.
		/// </summary>
		public string PathToXMatterHtml { get; set; }

		public void InjectXMatter( DataSet data)
		{
			//don't want to pollute shells with this content
			if (!string.IsNullOrEmpty(FolderPathForCopyingXMatterFiles))
			{
				//copy over any image files used by this front matter
				foreach (var file in Directory.GetFiles(Path.GetDirectoryName(PathToXMatterHtml), "*.png;*.jpg;*.gif"))
				{
					File.Copy(file, FolderPathForCopyingXMatterFiles.CombineForPath(Path.GetFileName(file)), true);
				}
			}

			//note: for debugging the template/css purposes, it makes our life easier if, at runtime, the html is pointing the original.
			//makes it easy to drop into a css editor and fix it up with the content we're looking at.
			//TODO:But then later, we want to save it so that these are found in the same dir as the book.
			_dom.AddStyleSheet(PathToStyleSheetForPaperAndOrientation);

			var frontMatterDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(PathToXMatterHtml);
			//it's important that we append *after* this, so that these values take precendance (the template will just have empty values for this stuff)
			XmlNode divBeforeNextFrontMattterPage = _dom.SelectSingleNode("//body/div[contains(@class,'-bloom-dataDiv')]");

			foreach (XmlElement frontMatterPage in frontMatterDom.SafeSelectNodes("/html/body/div[contains(@data-page,'required')]"))
			{
				var newPageDiv = _dom.ImportNode(frontMatterPage, true) as XmlElement;
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("'V'", '"' + data.WritingSystemCodes["V"] + '"');
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("\"V\"", '"' + data.WritingSystemCodes["V"] + '"');
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("'N1'", '"' + data.WritingSystemCodes["N1"] + '"');
				newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("\"N1\"", '"' + data.WritingSystemCodes["N1"] + '"');
				if (!String.IsNullOrEmpty(data.WritingSystemCodes["N2"]))  //otherwise, styleshee will hide it
				{
					newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("'N2'", '"' + data.WritingSystemCodes["N2"] + '"');
					newPageDiv.InnerXml = newPageDiv.InnerXml.Replace("\"N2\"", '"' + data.WritingSystemCodes["N2"] + '"');
				}
				_dom.SelectSingleNode("//body").InsertAfter(newPageDiv, divBeforeNextFrontMattterPage);
				divBeforeNextFrontMattterPage = newPageDiv;

				foreach (XmlElement e in newPageDiv.SafeSelectNodes("//*[starts-with(text(),'{')]"))
				{
					//REVIEW: might this nuke xml in there? If so, try to select the text node ( /text() ) and then remove it and add back an empty one: node.AppendChild(node.OwnerDocument.CreateTextNode(""));
					e.InnerText = "";
				}

			}
		}
	}
}
