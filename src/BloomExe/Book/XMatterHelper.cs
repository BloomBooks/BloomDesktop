using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Api;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.Publish;
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
		/// The default key name is provided as a method parameter, but that can be overridden by a value from inside the book.
		/// The name of the file should be (key)-XMatter.htm. The name and the location of the folder is not our problem...
		/// we leave it to the supplied fileLocator to find it.
		/// </summary>
		/// <param name="xmatterNameFromCollectionSettings">e.g. "Factory", "SILIndonesia".  This can be overridden inside the bookDom.</param>
		/// <param name="fileLocator">The locator needs to be able tell us the path to an xmatter html file, given its name</param>
		public XMatterHelper(HtmlDom bookDom, string xmatterNameFromCollectionSettings, IFileLocator fileLocator)
		{
			string directoryPath = null;
			_bookDom = bookDom;
			var bookSpecificXMatterPack = bookDom.GetMetaValue("xmatter", null);
			if (!String.IsNullOrWhiteSpace(bookSpecificXMatterPack))
			{
				bookSpecificXMatterPack = MigrateXMatterName(bookSpecificXMatterPack);
				_nameOfXMatterPack = bookSpecificXMatterPack;
				var errorTemplate = LocalizationManager.GetString("Errors.XMatterSpecifiedByBookNotFound",
					"This book called for a Front/Back Matter pack named '{0}', but this version of Bloom does not have it, and Bloom could not find it on this computer. The book has been changed to use the Front/Back Matter pages from the Collection Settings.");
				directoryPath = GetXMatterDirectory(_nameOfXMatterPack, fileLocator, String.Format(errorTemplate, bookSpecificXMatterPack), false);
				if (directoryPath == null)
				{
					// Remove the xmatter specification from the DOM since it couldn't be found.
					_bookDom.RemoveMetaElement("xmatter");
				}
			}
			if (directoryPath == null)
			{
				_nameOfXMatterPack = xmatterNameFromCollectionSettings;
				directoryPath = GetXMatterDirectory(_nameOfXMatterPack, fileLocator, "It should not be possible to get an error here, because the collection verifies its xmatter name in CheckAndFixDependencies()", true);
			}
			var htmName = _nameOfXMatterPack + "-XMatter.html";
			PathToXMatterHtml = directoryPath.CombineForPath(htmName);
			if(!RobustFile.Exists(PathToXMatterHtml))
			{
				htmName = _nameOfXMatterPack + "-XMatter.htm"; // pre- Bloom 3.7
				PathToXMatterHtml = directoryPath.CombineForPath(htmName);
			}
			if (!RobustFile.Exists(PathToXMatterHtml))
			{
				ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), "Could not locate the file {0} in {1} (also checked .html)", htmName, directoryPath);
				throw new ApplicationException();
			}
			PathToXMatterStylesheet = directoryPath.CombineForPath(GetStyleSheetFileName());
			if (!RobustFile.Exists(PathToXMatterHtml))
			{
				ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), "Could not locate the file {0} in {1}", GetStyleSheetFileName(), directoryPath);
				throw new ApplicationException();
			}
			XMatterDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(PathToXMatterHtml, false);
		}

		public static string GetXMatterDirectory(string nameOfXMatterPack, IFileLocator fileLocator, string errorMsg, bool throwIfError)
		{
			try
			{
				var directoryName = nameOfXMatterPack + "-XMatter";
				return fileLocator.LocateDirectoryWithThrow(directoryName);
			}
			catch (ApplicationException error)
			{
				var frontBackMatterProblem = LocalizationManager.GetString("Errors.XMatterProblemLabel", "Front/Back Matter Problem", "This shows in the 'toast' that pops up to notify the user of a non-fatal problem.");
				NonFatalProblem.Report(ModalIf.None, PassiveIf.All, frontBackMatterProblem, errorMsg, error);
				if (throwIfError)
					throw new ApplicationException(errorMsg);
			}
			return null;
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
		/// The location of the stylesheet for the selected xmatter pack.
		/// </summary>
		public string PathToXMatterStylesheet { get; set; }

		/// <summary>
		/// This exists in an XMatter-pack folder, which we infer from this file location.
		/// </summary>
		public string PathToXMatterHtml { get; set; }

		/// <summary>
		/// this is separated out to ease unit testing
		/// </summary>
		public XmlDocument XMatterDom { get; set; }

		// for testing. Real code should supply the bookFolderPath argument.
		internal void InjectXMatter(Dictionary<string, string> writingSystemCodes, Layout layout)
		{
			InjectXMatter(writingSystemCodes, layout, null, null);
		}

		public void InjectXMatter(Dictionary<string, string> writingSystemCodes, Layout layout, string branding, string bookFolderPath)
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
			_bookDom.AddStyleSheet(PathToXMatterStylesheet.ToLocalhost());

			//it's important that we append *after* this, so that these values take precendance (the template will just have empty values for this stuff)
			//REVIEW: I think all stylesheets now get sorted once they are all added: see HtmlDoc.SortStyleSheetLinks()
			XmlNode divBeforeNextFrontMattterPage = _bookDom.RawDom.SelectSingleNode("//body/div[@id='bloomDataDiv']");

			foreach (XmlElement xmatterPage in XMatterDom.SafeSelectNodes("/html/body/div[contains(@data-page,'required')]"))
			{
				var newPageDiv = _bookDom.RawDom.ImportNode(xmatterPage, true) as XmlElement;
				//give a new id, else thumbnail caches get messed up becuase every book has, for example, the same id for the cover.
				newPageDiv.SetAttribute("id", Guid.NewGuid().ToString());

				CleanupBrandingImages(newPageDiv, branding, bookFolderPath, layout);

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
			InjectFlyleafIfNeeded(layout);
		}

		public bool TemporaryDom { get; set; }

		private void CleanupBrandingImages(XmlElement newPageDiv, string branding, string bookFolderPath, Layout layout)
		{
			if (branding == null)
				return; // in testing.
			if (BookStorage.IsStaticContent(bookFolderPath))
				return;
			var prefix = BrandingApi.kApiBrandingImage + "?id=";
			foreach (XmlElement imageElt in newPageDiv.SafeSelectNodes("//img").Cast<XmlElement>().ToArray())
			{
				var src = imageElt.Attributes["src"]?.Value;
				if (src == null || !src.StartsWith(prefix))
					continue;
				var fileName = src.Substring(prefix.Length);
				var pathToRealImage = BrandingApi.FindBrandingImageFileIfPossible(branding, fileName, layout);
				if (string.IsNullOrEmpty(pathToRealImage))
				{
					if (!TemporaryDom)
					{
						// If the book folder contains this file already, it's obsolete, from some previous branding choice.
						// Get rid of it to save space. We might also have an obsolete file with a png extension; get rid of
						// that too. Of course, if this is just for a temporary DOM, we don't want to mess with the real folder.
						var destFileName = Path.Combine(bookFolderPath, fileName);
						RobustFile.Delete(destFileName);
						RobustFile.Delete(Path.ChangeExtension(destFileName, ".png"));
					}
					// At this point the <img> element has src api/branding/something not in the current branding.
					// So it's not actually going to produce an image in Bloom itself. We could change it, as
					// in the following block, so that it points to a non-existent file in the book folder,
					// and do various tricks to try to prevent warnings about missing files.
					// But such an element does no good. Even if someone manually inserted the missing file
					// (presumably an attempt to circumvent the current branding?) the next bring-book-up-to-date
					// will remove it as not part of the current branding.
					// Meanwhile, we need tricks to prevent missing-image errors in Bloom itself, and more tricks
					// to prevent them in epubs, and .bloomd, and however else we might publish.
					// All this is made unnecessary by just deleting the <img> element if it's not used in the
					// current branding.
					// If we later switch to a different branding which does need it,
					// the next cycle of updating xmatter will start again with the original xmatter files
					// which contain all the api/branding urls, and this time we will find the file and not
					// delete the <img>.
					imageElt.ParentNode.RemoveChild(imageElt);
				} else
				{
					if (TemporaryDom)
					{
						// for a temporary DOM we don't care if the path is absolute and to something outside the book folder.
						// It just has to work. And we don't want to modify the book folder. It does need to go through the
						// image server, otherwise we'll get cross-domain problems. And we need the marker that prevents
						// creating screen-only quality images.
						imageElt.SetAttribute("src", (FileAndApiServer.OriginalImageMarker + "/" + pathToRealImage).ToLocalhost());
					}
					else
					{
						// We want to actually copy it into the book folder, where things will work right
						// even if someone just opens the HTML file in a context where our image server isn't
						// running at all.
						// We want to use the original name in the book folder...for one thing, works with
						// deletion code above...but the correct extension for the file we actually found.
						var destFileName = Path.ChangeExtension(Path.GetFileName(fileName), Path.GetExtension(pathToRealImage));
						RobustFile.Copy(pathToRealImage, Path.Combine(bookFolderPath, destFileName), true);
						// Point the <img> element at the local file...which should be available since we
						// just copied it there.
						imageElt.SetAttribute("src", destFileName);
					}
				}
			}
		}


		/// <summary>
		/// Some book layouts rely on the first page facing the second page. A Wall Calendar is one example.
		/// Here we check if the first content page has this requirement and, if so, we insert a blank "flyleaf"
		/// page.
		/// </summary>
		private void InjectFlyleafIfNeeded(Layout layout)
		{
			// the "inside" here means "not counting the cover"
			var numberOfFrontMatterPagesInside = XMatterDom.SafeSelectNodes("//div[contains(@class,'bloom-frontMatter')]").Count - 1;
			var firstPageWouldNotBePartOfASpread = numberOfFrontMatterPagesInside%2 != 0;

			if(firstPageWouldNotBePartOfASpread)
			{
				var lastFrontMatterPage = _bookDom.SelectSingleNode("//div[contains(@class,'bloom-frontMatter')][last()]");

				var firstContentPageAndAlsoStartsSpread = _bookDom.SelectSingleNode(
					"//div[contains(@class,'bloom-frontMatter')][last()]" // last frontmatter page
					+ "/following-sibling::div[contains(@data-page, 'spread-start')]");
					// page after that says it needs to be facing the next page
				if(firstContentPageAndAlsoStartsSpread != null)
				{
					var flyDom = new XmlDocument();
					flyDom.LoadXml(@"
						<div class='bloom-flyleaf bloom-frontMatter bloom-page' data-page='required singleton'>
							<div class='pageLabel'>Flyleaf</div>
							<div style='height: 100px; width:100%'
								data-hint='This page was automatically inserted because the following page is marked as part of a two page spread.'>
							</div>
						</div>");
					var flyleaf = _bookDom.RawDom.ImportNode(flyDom.FirstChild, true) as XmlElement;
					flyleaf.SetAttribute("id", Guid.NewGuid().ToString());
					lastFrontMatterPage.ParentNode.InsertAfter(flyleaf, lastFrontMatterPage);
					SizeAndOrientation.UpdatePageSizeAndOrientationClasses(flyleaf, layout);
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

		/// <summary>
		/// This will return a different name if we recognize the submitted name and we know that we have changed it or retired it.
		/// </summary>
		public static string MigrateXMatterName(string nameOfXMatterPack)
		{
			// Bloom 3.7 retired the BigBook xmatter pack.
			// If we ever create another xmatter pack called BigBook (or rename the Factory pack) we'll need to redo this.
			string[] retiredPacks = { "BigBook" };
			if (retiredPacks.Contains(nameOfXMatterPack))
				return CollectionSettings.kDefaultXmatterName;
			return nameOfXMatterPack;;
		}
	}
}
