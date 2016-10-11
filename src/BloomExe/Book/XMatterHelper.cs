﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// Handles chores related to Front and Back Matter packs.
	/// These allow users to have a set which fits there country/organizational situation, and have it applied
	/// to all books they create, whether original or from shells created in other country/organization contexts.
	/// </summary>
	public class XMatterHelper
	{
		private readonly HtmlDom _bookDom;
		private readonly string _nameOfXMatterPack;

		/// <summary>
		/// Constructs by finding the file and folder of the xmatter pack, given the its key name e.g. "Factory", "SILIndonesia".
		/// The name of the file should be (key)-XMatter.htm. The name and the location of the folder is not our problem...
		/// we leave it to the supplied fileLocator to find it.
		/// </summary>
		/// <param name="nameOfXMatterPack">e.g. "Factory", "SILIndonesia"</param>
		/// <param name="fileLocator">The locator needs to be able tell us the path to an xmater html file, given its name</param>
		public XMatterHelper(HtmlDom bookDom, string nameOfXMatterPack, IFileLocator fileLocator)
		{		
			_bookDom = bookDom;
			_nameOfXMatterPack = nameOfXMatterPack;

			string directoryName = nameOfXMatterPack + "-XMatter";
			string directoryPath;
			try
			{
				directoryPath = fileLocator.LocateDirectoryWithThrow(directoryName);
			}
			catch(ApplicationException error)
			{
				var errorTemplate = LocalizationManager.GetString("Errors.XMatterNotFound",
					"This Book called for Front/Back Matter pack named '{0}', but Bloom couldn't find that on this computer. You can either install a BloomPack that will give you '{0}', or go to Settings:Book Making and change to another Front/Back Matter Pack.");
				var msg = string.Format(errorTemplate, nameOfXMatterPack);

				ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), msg);
				//NB: we don't want to put up a dialog for each one; one failure here often means 20 more are coming as the other books are loaded!
				throw new ApplicationException(msg);
			}
			var htmName = nameOfXMatterPack + "-XMatter.html";
			PathToXMatterHtml = directoryPath.CombineForPath(htmName);
			if(!RobustFile.Exists(PathToXMatterHtml))
			{
				htmName = nameOfXMatterPack + "-XMatter.htm"; // pre- Bloom 3.7
				PathToXMatterHtml = directoryPath.CombineForPath(htmName);
			}
			if (!RobustFile.Exists(PathToXMatterHtml))
			{
				ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), "Could not locate the file {0} in {1} (also checked .html)", htmName, directoryPath);
				throw new ApplicationException();
			}
			PathToStyleSheetForPaperAndOrientation = directoryPath.CombineForPath(GetStyleSheetFileName());
			if (!RobustFile.Exists(PathToXMatterHtml))
			{
				ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), "Could not locate the file {0} in {1}", GetStyleSheetFileName(), directoryPath);
				throw new ApplicationException();
			}
			XMatterDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(PathToXMatterHtml, false);
		}


		public string GetStyleSheetFileName()
		{
//			var layout = SizeAndOrientation.FromDom(_dom);
//			return layout.PageSizeName + "-" + layout.OrientationName + "-" + _nameOfXMatterPack + "-XMatter.css";
			return _nameOfXMatterPack + "-XMatter.css";
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

		/// <summary>
		/// this is separated out to ease unit testing
		/// </summary>
		public XmlDocument XMatterDom { get; set; }

		public void InjectXMatter(Dictionary<string, string> writingSystemCodes, Layout layout)
		{
			//don't want to pollute shells with this content
			if (!string.IsNullOrEmpty(FolderPathForCopyingXMatterFiles))
			{
				//copy over any image files used by this front matter
				string path = Path.GetDirectoryName(PathToXMatterHtml);
				foreach (var file in Directory.GetFiles(path, "*.png").Concat(Directory.GetFiles(path, "*.jpg").Concat(Directory.GetFiles(path, "*.gif").Concat(Directory.GetFiles(path, "*.bmp")))))
				{
					RobustFile.Copy(file, FolderPathForCopyingXMatterFiles.CombineForPath(Path.GetFileName(file)), true);
				}
			}

			//note: for debugging the template/css purposes, it makes our life easier if, at runtime, the html is pointing the original.
			//makes it easy to drop into a css editor and fix it up with the content we're looking at.
			//TODO:But then later, we want to save it so that these are found in the same dir as the book.
			_bookDom.AddStyleSheet(PathToStyleSheetForPaperAndOrientation.ToLocalhost());

			//it's important that we append *after* this, so that these values take precendance (the template will just have empty values for this stuff)
			//REVIEW: I think all stylesheets now get sorted once they are all added: see HtmlDoc.SortStyleSheetLinks()
			XmlNode divBeforeNextFrontMattterPage = _bookDom.RawDom.SelectSingleNode("//body/div[@id='bloomDataDiv']");

			foreach (XmlElement xmatterPage in XMatterDom.SafeSelectNodes("/html/body/div[contains(@data-page,'required')]"))
			{
				var newPageDiv = _bookDom.RawDom.ImportNode(xmatterPage, true) as XmlElement;
				//give a new id, else thumbnail caches get messed up becuase every book has, for example, the same id for the cover.
				newPageDiv.SetAttribute("id", Guid.NewGuid().ToString());

				if (IsBackMatterPage(xmatterPage))
				{
					//note: this is redundant unless this is the 1st backmatterpage in the list
					divBeforeNextFrontMattterPage = _bookDom.RawDom.SelectSingleNode("//body/div[last()]");
				}

				//we want the xmatter pages to match what we found in the source book
				SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPageDiv, layout);

				//any @lang attributes that have a metalanguage code (N1, N2, V) get filled with the actual code.
				//note that this older method is crude, as you're in trouble if the user changes one of those to
				//a different language. Instead, use data-metalanguage.
				foreach ( XmlElement node in newPageDiv.SafeSelectNodes("//*[@lang]"))
				{
					var lang = node.GetAttribute("lang");
					if (writingSystemCodes.ContainsKey(lang))
					{
						node.SetAttribute("lang", writingSystemCodes[lang]);
					}
				}

				_bookDom.RawDom.SelectSingleNode("//body").InsertAfter(newPageDiv, divBeforeNextFrontMattterPage);
				divBeforeNextFrontMattterPage = newPageDiv;

				//enhance... this is really ugly. I'm just trying to clear out any remaining "{blah}" left over from the template
				foreach (XmlElement e in newPageDiv.SafeSelectNodes("//*[starts-with(text(),'{')]"))
				{
					foreach ( var node in e.ChildNodes)
					{
						XmlText t = node as XmlText;
						if(t!=null && t.Value.StartsWith("{"))
							t.Value =""; //otherwise html tidy will through away span's (at least) that are empty, so we never get a chance to fill in the values.
					}
				}
			}
		}

//		//in the beta, 0.8, the ID of the page in the front-matter template was used for the 1st
//		//page of every book. This screws up thumbnail caching.
//		private void FixPageId(XmlDocument bookDom)
//		{
//			XmlElement page = bookDom.SelectSingleNode("//div[@id='74731b2d-18b0-420f-ac96-6de20f659810']") as XmlElement;
//			if (page != null)
//			{
//				page.SetAttribute("id", Guid.NewGuid().ToString());
//			}
//		}

		public static bool IsFrontMatterPage(XmlElement pageDiv)
		{
			return pageDiv.SelectSingleNode("self::div[contains(@class, 'bloom-frontMatter')]") != null;
		}

		public static bool IsBackMatterPage(XmlElement pageDiv)
		{
			return pageDiv.SelectSingleNode("self::div[contains(@class, 'bloom-backMatter')]") != null;
		}


		/// <summary>
		///remove any x-matter in the book
		/// </summary>
		public static void RemoveExistingXMatter(HtmlDom dom)
		{
			foreach (XmlElement div in dom.SafeSelectNodes("//div[contains(@class,'bloom-frontMatter') or contains(@class,'bloom-backMatter')]"))
			{
				div.ParentNode.RemoveChild(div);
			}
		}
	}
}
