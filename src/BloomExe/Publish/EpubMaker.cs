using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using Bloom.Publish;
using BloomTemp;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// This class handles the process of creating an epub out of a bloom book.
	/// The process has two stages, corresponding to the way our UI displays a preview and
	/// then allows the user to save. We 'stage' the epub by generating all the files into a
	/// temporary folder. Then, if the user says to save, we actually zip them into an epub.
	/// </summary>
	public class EpubMaker : IDisposable
	{
		public Book Book
		{
			get
			{
				return _book;
			}
			internal set
			{
				_book = value;
			}
		}

		private Book _book;
		// This is a shorthand for _book.Storage. Since that is something of an implementation secret of Book,
		// it also provides a safe place for us to make changes if we ever need to get the Storage some other way.
		private IBookStorage Storage {get { return _book.Storage; } }
		// Keeps track of IDs that have been used in the manifest. These are generated to roughly match file
		// names, but the algorithm could pathologically produce duplicates, so we guard against this.
		private HashSet<string> _idsUsed = new HashSet<string>();
		// Keeps track of the ID actually generated for each resource. This is generally algorithmic,
		// but in case of duplicates the ID for an item might not be what the algorithm predicts.
		private Dictionary<string, string> _mapItemToId = new Dictionary<string, string>();
		// Keep track of the files we already copied to the epub, so we don't copy them again to a new name.
		private Dictionary<string, string>  _mapSrcPathToDestFileName = new Dictionary<string, string>();
		// Some file names are not allowed in epubs, in which case, we have to rename the file and change the
		// link in the HTML. This keeps track of files that needed to be renamed when copied into the epub.
		Dictionary<string, string> _mapChangedFileNames = new Dictionary<string, string>();
		// All the things (files) we need to list in the manifest
		private List<string> _manifestItems;
		// The things we need to list in the 'spine'...defines the normal reading order of the book
		private List<string> _spineItems;
		// We track the first page that is actually content and link to it in our rather trivial table of contents.
		private string _firstContentPageItem;
		private string _coverPage;
		private string _contentFolder;
		private string _navFileName;
		// This temporary folder holds the staging folder with the bloom content. It also (temporarily)
		// holds a copy of the Readium code, since I haven't been able to figure out how to get that
		// code to redirect to display a folder which isn't a child of the folder containing the
		// readium HTML.
		private TemporaryFolder _stagingFolder;
		public string StagingDirectory { get; private set; }
		private BookThumbNailer _thumbNailer;

		/// <summary>
		/// Set to true for unpaginated output. This is something of a misnomer...any better ideas?
		/// If it is true (which currently it always is), we remove the stylesheets for precise page layout
		/// and just output the text and pictures in order with a simple default stylesheet.
		/// Rather to my surprise, the result still has page breaks where expected, though the reader may
		/// add more if needed.
		/// </summary>
		public bool Unpaginated { get; set; }

		public EpubMaker(BookThumbNailer thumbNailer)
		{
			_thumbNailer = thumbNailer;
		}

		/// <summary>
		/// Generate all the files we will zip into the epub for the current book into the StagingFolder.
		/// It is required that the parent of the StagingFolder is a temporary folder into which we can
		/// copy the Readium stuff. This folder is deleted when the EpubMaker is disposed.
		/// </summary>
		public void StageEpub()
		{
			if (_stagingFolder != null)
				_stagingFolder.Dispose();
			_stagingFolder = new TemporaryFolder("Epub export");
			// The readium control remembers the current page for each book.
			// So it is useful to have a unique name for each one.
			// However, it needs to be something we can put in a URL without complications,
			// so a guid is better than say the book's own folder name.
			StagingDirectory = Path.Combine(_stagingFolder.FolderPath, _book.ID);
			if (Directory.Exists(StagingDirectory))
				Directory.Delete(StagingDirectory, true);
			// in case of previous versions // Enhance: delete when done? Generate new name if conflict?
			var contentFolderName = "content";
			_contentFolder = Path.Combine(StagingDirectory, contentFolderName);
			Directory.CreateDirectory(_contentFolder); // also creates parent staging directory
			var pageIndex = 0;
			_manifestItems = new List<string>();
			_spineItems = new List<string>();
			int firstContentPageIndex = Book.GetIndexLastFrontkMatterPage() + 2; // pageIndex starts at 1
			_firstContentPageItem = null;
			foreach (XmlElement pageElement in Book.GetPageElements())
			{
				++pageIndex;
				var pageDom = MakePageFile(pageElement, pageIndex, firstContentPageIndex);
				// for now, at least, all Bloom book pages currently have the same stylesheets, so we only neeed
				//to look at those stylesheets on the first page
				if (pageIndex == 1)
					CopyStyleSheets(pageDom);
			}

			const string coverPageImageFile = "thumbnail-256.png";
			// This thumbnail is otherwise only made when uploading, so it may be out of date.
			// Just remake it every time.
			_thumbNailer.MakeThumbnailOfCover(Book, 256, Form.ActiveForm);
			CopyFileToEpub(Path.Combine(Book.FolderPath, coverPageImageFile));

			EmbedFonts(); // must call after copying stylesheets
			MakeNavPage();

			//supporting files

			// Fixed requirement for all epubs
			File.WriteAllText(Path.Combine(StagingDirectory, "mimetype"), @"application/epub+zip");

			var metaInfFolder = Path.Combine(StagingDirectory, "META-INF");
			Directory.CreateDirectory(metaInfFolder);
			var containerXmlPath = Path.Combine(metaInfFolder, "container.xml");
			File.WriteAllText(containerXmlPath, @"<?xml version='1.0' encoding='utf-8'?>
					<container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>
					<rootfiles>
					<rootfile full-path='content/content.opf' media-type='application/oebps-package+xml'/>
					</rootfiles>
					</container>");

			MakeManifest(coverPageImageFile);
		}

		private void MakeManifest(string coverPageImageFile)
		{
			// content.opf: contains primarily the manifest, listing all the content files of the epub.
			var manifestPath = Path.Combine(_contentFolder, "content.opf");
			XNamespace opf = "http://www.idpf.org/2007/opf";
			var rootElt = new XElement(opf + "package",
				new XAttribute("version", "3.0"),
				new XAttribute("unique-identifier", "I" + Book.ID));
			// add metadata
			var dcNamespace = "http://purl.org/dc/elements/1.1/";
			XNamespace dc = dcNamespace;
			var metadataElt = new XElement(opf + "metadata",
				new XAttribute(XNamespace.Xmlns + "dc", dcNamespace),
				// attribute makes the namespace have a prefix, not be a default.
				new XElement(dc + "title", Book.Title),
				new XElement(dc + "language", Book.CollectionSettings.Language1Iso639Code),
				new XElement(dc + "identifier",
					new XAttribute("id", "I" + Book.ID), "bloomlibrary.org." + Book.ID),
				new XElement(opf + "meta",
					new XAttribute("property", "dcterms:modified"),
					new FileInfo(Storage.FolderPath).LastWriteTimeUtc.ToString("s") + "Z")); // like 2012-03-20T11:37:00Z
			rootElt.Add(metadataElt);

			var manifestElt = new XElement(opf + "manifest");
			rootElt.Add(manifestElt);
			foreach (var item in _manifestItems)
			{
				var itemElt = new XElement(opf + "item",
					new XAttribute("id", GetIdOfFile(item)),
					new XAttribute("href", item),
					new XAttribute("media-type", GetMediaType(item)));
				// This isn't very useful but satisfies a validator requirement until we think of
				// something better.
				if (item == _navFileName)
					itemElt.SetAttributeValue("properties", "nav");
				if (item == coverPageImageFile)
					itemElt.SetAttributeValue("properties", "cover-image");
				manifestElt.Add(itemElt);
			}
			MakeSpine(opf, rootElt, manifestPath);
		}

		private void MakeSpine(XNamespace opf, XElement rootElt, string manifestPath)
		{
			// Generate the spine, which indicates the top-level readable content in order.
			// These IDs must match the corresponding ones in the manifest, since the spine
			// doesn't indicate where to actually find the content.
			var spineElt = new XElement(opf + "spine");
			rootElt.Add(spineElt);
			foreach (var item in _spineItems)
			{
				var itemElt = new XElement(opf + "itemref",
					new XAttribute("idref", GetIdOfFile(item)));
				spineElt.Add(itemElt);
			}
			using (var writer = XmlWriter.Create(manifestPath))
				rootElt.WriteTo(writer);
		}

		private void CopyStyleSheets(HtmlDom pageDom)
		{
			foreach (XmlElement link in pageDom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var href = Path.Combine(Book.FolderPath, link.GetAttribute("href"));
				var name = Path.GetFileName(href);
				if (name == "fonts.css")
					continue; // generated file for this book, already copied to output.

				var fl = Storage.GetFileLocator();
				//var path = this.GetFileLocator().LocateFileWithThrow(name);
				var path = fl.LocateFileWithThrow(name);
				CopyFileToEpub(path);
			}
		}

		private HtmlDom MakePageFile(XmlElement pageElement, int pageIndex, int firstContentPageIndex)
		{
			var pageDom = GetEpubFriendlyHtmlDomForPage(pageElement);
			pageDom.RemoveModeStyleSheets();
			if (Unpaginated)
			{
				RemoveRegularStylesheets(pageDom);
				pageDom.AddStyleSheet(Storage.GetFileLocator().LocateFileWithThrow(@"baseEpub.css").ToLocalhost());
			}
			else
			{
				// Review: this branch is not currently used. Very likely we need SOME different stylesheets
				// from the printed book, possibly including baseEpub.css, if it's even possible to make
				// useful fixed-layout books out of Bloom books that will work with current readers.
				pageDom.AddStyleSheet(Storage.GetFileLocator().LocateFileWithThrow(@"basePage.css").ToLocalhost());
				pageDom.AddStyleSheet(Storage.GetFileLocator().LocateFileWithThrow(@"previewMode.css"));
				pageDom.AddStyleSheet(Storage.GetFileLocator().LocateFileWithThrow(@"origami.css"));
			}

			RemoveUnwantedContent(pageDom);

			pageDom.SortStyleSheetLinks();
			pageDom.AddPublishClassToBody();

			MakeCssLinksAppropriateForEpub(pageDom);
			RemoveBloomUiElements(pageDom);
			RemoveSpuriousLinks(pageDom);
			RemoveScripts(pageDom);
			FixIllegalIds(pageDom);
			FixPictureSizes(pageDom);
			// Since we only allow one htm file in a book folder, I don't think there is any
			// way this name can clash with anything else.
			var pageDocName = pageIndex + ".xhtml";
			if (pageIndex == 1)
				_coverPage = pageDocName;

			CopyImages(pageDom);

			_manifestItems.Add(pageDocName);
			_spineItems.Add(pageDocName);

			if (pageIndex == firstContentPageIndex)
				_firstContentPageItem = pageDocName;

			FixChangedFileNames(pageDom);
			pageDom.AddStyleSheet("fonts.css"); // enhance: could omit if we don't embed any

			// epub validator requires HTML to use namespace. Do this last to avoid (possibly?) messing up our xpaths.
			pageDom.RawDom.DocumentElement.SetAttribute("xmlns", "http://www.w3.org/1999/xhtml");
			File.WriteAllText(Path.Combine(_contentFolder, pageDocName), pageDom.RawDom.OuterXml);
			return pageDom;
		}

		private void CopyImages(HtmlDom pageDom)
		{
// Manifest has to include all referenced files
			foreach (XmlElement img in pageDom.SafeSelectNodes("//img").Cast<XmlElement>().ToList())
			{
				var srcAttr = img.Attributes["src"];
				if (srcAttr == null)
					continue; // very weird, but all we can do is ignore it.
				// src should be a url, so any special characters should be encoded.
				// I'm not sure the rest of the program is doing this entirely right yet,
				// but at least this handles %20, which definitely is used.
				var imgName = StripQuery(WebUtility.UrlDecode(srcAttr.Value));
				if (string.IsNullOrEmpty(imgName))
					continue;
				// Images are always directly in the folder
				var srcPath = Path.Combine(Book.FolderPath, imgName);
				if (File.Exists(srcPath))
					CopyFileToEpub(srcPath);
				else
					img.ParentNode.RemoveChild(img);
			}
		}

		private static string StripQuery(string url)
		{
			var index = url.IndexOf('?');
			if (index >= 0)
				url = url.Substring(0, index);
			return url;
		}

		// Combines staging and finishing (currently just used in tests).
		public void SaveEpub(string destinationEpubPath)
		{
			StageEpub();
			FinishEpub(destinationEpubPath);
		}

		/// <summary>
		/// Finish publishing an epub that has been staged, by zipping it into the desired final file.
		/// </summary>
		/// <param name="destinationEpubPath"></param>
		public void FinishEpub(string destinationEpubPath)
		{
			var zip = new BloomZipFile(destinationEpubPath);
			foreach (var file in Directory.GetFiles(StagingDirectory))
				zip.AddTopLevelFile(file);
			foreach (var dir in Directory.GetDirectories(StagingDirectory))
				zip.AddDirectory(dir);
			zip.Save();
		}

		/// <summary>
		/// Try to embed the fonts we need.
		/// </summary>
		private void EmbedFonts()
		{
			var fontsWanted = GetFontsUsed();
			var fontFileFinder = new FontFileFinder();
			var filesToEmbed = fontsWanted.SelectMany(fontFileFinder.GetFilesForFont).ToArray();
			foreach (var file in filesToEmbed)
			{
				CopyFileToEpub(file);
			}
			var sb = new StringBuilder();
			foreach (var font in fontsWanted)
			{
				var group = fontFileFinder.GetGroupForFont(font);
				if (group != null)
				{
					AddFontFace(sb, font, "normal", "normal", group.Normal);
					AddFontFace(sb, font, "bold", "normal", group.Bold);
					AddFontFace(sb, font, "normal", "italic", group.Italic);
					AddFontFace(sb, font, "bold", "italic", group.BoldItalic);
				}
			}
			File.WriteAllText(Path.Combine(_contentFolder, "fonts.css"), sb.ToString());
			_manifestItems.Add("fonts.css");
		}

		void AddFontFace(StringBuilder sb, string name, string weight, string style, string path)
		{
			if (path == null)
				return;
			sb.AppendLineFormat("@font-face {{font-family:'{0}'; font-weight:{1}; font-style:{2}; src:url({3}) format('{4}');}}",
				name, weight, style, Path.GetFileName(path),
				Path.GetExtension(path) == ".woff" ? "woff" : "opentype");
		}

		/// <summary>
		/// First step of embedding fonts: determine what are used in the document.
		/// Eventually we may load each page into a DOM and use JavaScript to ask each
		/// bit of text what actual font and face it is using.
		/// For now we examine the stylesheets and collect the font families they mention.
		/// </summary>
		/// <returns></returns>
		private IEnumerable<string> GetFontsUsed()
		{
			var result = new HashSet<string>();
			foreach (var ss in Directory.GetFiles(Book.FolderPath, "*.css"))
			{
				var root = File.ReadAllText(ss, Encoding.UTF8);
				HtmlDom.FindFontsUsedInCss(root, result);
			}
			return result;
		}

		const double mmPerInch = 25.4;

		/// <summary>
		/// Typically pictures are given an absolute size in px, which looks right given
		/// the current absolute size of the page it is on. For an epub, a percent size
		/// will work better. We calculate it based on the page sizes and margins in
		/// BasePage.less and commonMixins.less. The page size definitions are unlikely
		/// to change, but change might be needed here if there is a change to the main
		/// .marginBox rule in basePage.less.
		/// To partly accommodate origami pages, we adjust for parent divs with an explict
		/// style setting the percent width.
		/// </summary>
		/// <param name="pageDom"></param>
		private void FixPictureSizes(HtmlDom pageDom)
		{
			bool firstTime = true;
			double pageWidthMm = 210; // assume A5 Portrait if not specified
			foreach (XmlElement img in pageDom.RawDom.SafeSelectNodes("//img"))
			{
				var marginBox = img.ParentNode.ParentNode as XmlElement;
				var mulitplier = 1.0;
				// For now we only attempt to adjust pictures contained in the marginBox.
				// To do better than this we will probably need to actually load the HTML into
				// a browser; even then it will be complex.
				while (marginBox != null && !HasClass(marginBox, "marginBox"))
				{
					// 'marginBox' is not yet the margin box...it is some parent div.
					// If it has an explicit percent width style, adjust for this.
					var styleAttr = marginBox.Attributes["style"];
					if (styleAttr != null)
					{
						var style = styleAttr.Value;
						var match = new Regex("width:\\s*(\\d+(\\.\\d+)?)%").Match(style);
						if (match.Success)
						{
							double percent;
							if (Double.TryParse(match.Groups[1].Value, out percent))
							{
								mulitplier *= percent/100;
							}
						}
					}
					marginBox = marginBox.ParentNode as XmlElement;
				}
				if (marginBox == null)
					continue;
				var page = marginBox.ParentNode as XmlElement;
				if (!HasClass(page, "bloom-page"))
					continue; // or return? marginBox should be child of page!
				if (firstTime)
				{
					var pageClass = HtmlDom.GetAttributeValue(page, "class").Split().FirstOrDefault(c => c.Contains("Portrait") || c.Contains("Landscape"));
					// This calculation unfortunately duplicates information from basePage.less.
					const int A4Width = 210;
					const int A4Height = 297;
					const double letterPortraitHeight = 11.0 * mmPerInch;
					const double letterPortraitWidth = 8.5 * mmPerInch;
					const double legalPortraitHeight = 14.0 * mmPerInch;
					const double legalPortraitWidth = 8.5 * mmPerInch;
					switch (pageClass)
					{
						case "A5Portrait":
							pageWidthMm = A4Height/2.0;
							break;
						case "A4Portrait":
							pageWidthMm = A4Width;
							break;
						case "A5Landscape":
							pageWidthMm = A4Width / 2.0;
							break;
						case "A4Landscape":
							pageWidthMm = A4Height;
							break;
						case "A6Portrait":
							pageWidthMm = A4Width / 2.0;
							break;
						case "A6Landscape":
							pageWidthMm = A4Height / 2.0;
							break;
						case "B5Portrait":
							pageWidthMm = 176;
							break;
						case "QuarterLetterPortrait":
							pageWidthMm = letterPortraitWidth/2.0;
							break;
						case "QuarterLetterLandscape":
						case "HalfLetterPortrait":
							pageWidthMm = letterPortraitHeight / 2.0;
							break;
						case "HalfLetterLandscape":
						case "LetterPortrait":
							pageWidthMm = letterPortraitWidth;
							break;
						case "LetterLandscape":
							pageWidthMm = letterPortraitHeight;
							break;
						case "HalfLegalPortrait":
							pageWidthMm = legalPortraitHeight / 2.0;
							break;
						case "HalfLegalLandscape":
						case "LegalPortrait":
							pageWidthMm = legalPortraitWidth;
							break;
						case "LegalLandscape":
							pageWidthMm = legalPortraitHeight;
							break;
					}
					firstTime = false;
				}
				var imgStyle = HtmlDom.GetAttributeValue(img, "style");
				// We want to take something like 'width:334px; height:220px; margin-left: 34px; margin-top: 0px;'
				// and change it to something like 'width:75%; height:auto; margin-left: 10%; margin-top: 0px;'
				// This first pass deals with width.
				if (ConvertStyleFromPxToPercent("width", pageWidthMm, mulitplier, ref imgStyle)) continue;

				// Now change height to auto, to preserve aspect ratio
				imgStyle = new Regex("height:\\s*\\d+px").Replace(imgStyle, "height:auto");
				if (!imgStyle.Contains("height"))
					imgStyle = "height:auto; " + imgStyle;

				// Similarly fix indent
				ConvertStyleFromPxToPercent("margin-left", pageWidthMm, mulitplier, ref imgStyle);

				img.SetAttribute("style", imgStyle);
			}
		}

		// Returns true if we don't find the expected style
		private static bool ConvertStyleFromPxToPercent(string stylename, double pageWidthMm, double multiplier, ref string imgStyle)
		{
			var match = new Regex("(.*" + stylename + ":\\s*)(\\d+)px(.*)").Match(imgStyle);
			if (!match.Success)
				return true;
			var widthPx = int.Parse(match.Groups[2].Value);
			var widthInch = widthPx/96.0; // in print a CSS px is exactly 1/96 inch
			const int marginBoxMarginMm = 40; // see basePage.less SetMarginBox.
			var marginBoxWidthInch = (pageWidthMm - marginBoxMarginMm)/mmPerInch;
			var parentBoxWidthInch = marginBoxWidthInch*multiplier; // parent box is smaller by net effect of parents with %width styles
			// 1/10 percent is close enough and more readable/testable than arbitrary precision; make a string with one decimal
			var newWidth = (Math.Round(widthInch/parentBoxWidthInch*1000)/10).ToString("F1");
			imgStyle = match.Groups[1] + newWidth  + "%" + match.Groups[3];
			return false;
		}

		/// <summary>
		/// Remove stuff that we don't want displayed. Some e-readers don't obey display:none. Also, not shipping it saves space.
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveUnwantedContent(HtmlDom pageDom)
		{
			// Remove bloom-editable material not in one of the interesting languages
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes("//div").Cast<XmlElement>().ToArray())
			{
				if (!HasClass(elt, "bloom-editable"))
					continue;
				var langAttr = elt.Attributes["lang"];
				var lang = langAttr == null ? null : langAttr.Value;
				if (lang == Book.MultilingualContentLanguage2 || lang == Book.MultilingualContentLanguage3 ||
					lang == Book.CollectionSettings.Language1Iso639Code)
					continue; // keep these
				if (lang == Book.CollectionSettings.Language2Iso639Code && IsInXMatterPage(elt))
					continue;
				elt.ParentNode.RemoveChild(elt);
			}
			// Remove any left-over bubbles
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes("//label").Cast<XmlElement>().ToArray())
			{
				if (HasClass(elt, "bubble"))
					elt.ParentNode.RemoveChild(elt);
			}
			// Remove page labels
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes("//div").Cast<XmlElement>().ToArray())
			{
				if (HasClass(elt, "pageLabel"))
					elt.ParentNode.RemoveChild(elt);
			}
		}

		private bool IsInXMatterPage(XmlElement elt)
		{
			while (elt != null)
			{
				if (HasClass(elt, "bloom-page"))
					return HasClass(elt, "bloom-frontMatter") || HasClass(elt, "bloom-backMatter");
				elt = elt.ParentNode as XmlElement;
			}
			return false;
		}

		bool HasClass(XmlElement elt, string className)
		{
			if (elt == null)
				return false;
			var classAttr = elt.Attributes["class"];
			if (classAttr == null)
				return false;
			return ((" " + classAttr.Value + " ").Contains(" " + className + " "));
		}

		private void RemoveRegularStylesheets(HtmlDom pageDom)
		{
			foreach (XmlElement link in pageDom.RawDom.SafeSelectNodes("//head/link").Cast<XmlElement>().ToArray())
			{
				var href = link.Attributes["href"];
				if (href != null && Path.GetFileName(href.Value).StartsWith("custom"))
					continue;
				if (href != null && Path.GetFileName(href.Value) == "settingsCollectionStyles.css")
					continue;
				link.ParentNode.RemoveChild(link);
			}
		}

		private void FixChangedFileNames(HtmlDom pageDom)
		{
			foreach (var attr in new[] {"src", "href"})
			{
				foreach (var node in pageDom.RawDom.SafeSelectNodes("//*[@" + attr + "]"))
				{
					var elt = node as XmlElement;
					if (elt == null)
						continue;
					// These attribute SHOULD contain URLs, so it should be necessary to decode them
					// to get actual file names, if there are any special characters involved.
					var oldName = WebUtility.UrlDecode(elt.Attributes[attr].Value);

					var fileName = attr == "src" ? StripQuery(oldName) : oldName;

					string newName;
					if (!_mapChangedFileNames.TryGetValue(fileName, out newName))
					{
						newName = fileName;
					}
					if (oldName != newName)
						elt.SetAttribute(attr, HttpUtility.UrlPathEncode(newName)); // Supposedly deprecated, but we need space->%20, not +
				}
			}
		}

		// Copy a file to the appropriate place in the epub staging area, and note
		// that it is a necessary manifest item.
		private void CopyFileToEpub(string srcPath)
		{
			if (_mapSrcPathToDestFileName.ContainsKey(srcPath))
				return; // File already present, must be used more than once.
			// Validator warns against spaces in filenames.
			var originalFileName = Path.GetFileName(srcPath);
			string fileName = originalFileName.Replace(" ", "_");
			var dstPath = Path.Combine(_contentFolder, fileName);
			// We deleted the root directory at the start, so if the file is already
			// there it is a clash, either multiple sources for files with the same name,
			// or produced by replacing spaces, or something. Come up with a similar unique name.
			for (int fix = 1; File.Exists(dstPath); fix++)
			{
				fileName = fileName + fix;
				dstPath = Path.Combine(_contentFolder, fileName);
			}
			if (originalFileName != fileName)
				_mapChangedFileNames[originalFileName] = fileName;
			CopyFile(srcPath, dstPath);
			_manifestItems.Add(fileName);
			_mapSrcPathToDestFileName[srcPath] = fileName;
		}

		/// <summary>
		/// This supports testing without actually copying files.
		/// </summary>
		/// <param name="srcPath"></param>
		/// <param name="dstPath"></param>
		internal virtual void CopyFile(string srcPath, string dstPath)
		{
			File.Copy(srcPath, dstPath);
		}

		// The validator is (probably excessively) upset about IDs that start with numbers.
		// I don't think we actually use these IDs in the epub so maybe we should just remove them?
		private void FixIllegalIds(HtmlDom pageDom)
		{
			foreach (var node in pageDom.RawDom.SafeSelectNodes("//*[@id]"))
			{
				var elt = node as XmlElement;
				if (elt == null)
					continue;
				var id = elt.Attributes["id"].Value;
				var first = id[0];
				if (first >= '0' && first <= '9')
					elt.SetAttribute("id", "i" + id);
			}
		}

		private void MakeNavPage()
		{
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			// Todo: improve this or at least make a way "Cover" and "Content" can be put in the book's language.
			var content = XElement.Parse(@"
<html xmlns='http://www.w3.org/1999/xhtml' xmlns:epub='http://www.idpf.org/2007/ops'>
	<head>
		<meta charset='utf-8' />
	</head>
	<body>
		<nav epub:type='toc' id='toc'>
			<ol>
				<li><a>Cover</a></li>
				<li><a>Content</a></li>
			</ol>
		</nav>
	</body>
</html>");
			var ol = content.Element(xhtml + "body").Element(xhtml + "nav").Element(xhtml + "ol");
			var items = ol.Elements(xhtml + "li").ToArray();
			var coverItem = items[0];
			var contentItem = items[1];
			if (_firstContentPageItem == null)
				contentItem.Remove();
			else
				contentItem.Element(xhtml + "a").SetAttributeValue("href", _firstContentPageItem);
			if (_coverPage == _firstContentPageItem)
				coverItem.Remove();
			else
				coverItem.Element(xhtml + "a").SetAttributeValue("href", _coverPage);
			_navFileName = "nav.xhtml";
			var navPath = Path.Combine(_contentFolder, _navFileName);

			using (var writer = XmlWriter.Create(navPath))
				content.WriteTo(writer);
			_manifestItems.Add(_navFileName);
		}

		/// <summary>
		/// We don't need to make scriptable books, and if our html contains scripts
		/// (which probably won't work on most readers) we have to add various attributes.
		/// Also our scripts are external refs, which would have to be fixed.
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveScripts(HtmlDom pageDom)
		{
			foreach (var node in pageDom.RawDom.SafeSelectNodes("//script").Cast<XmlNode>().ToArray())
			{
				var elt = node as XmlElement;
				if (elt == null)
					continue;
				elt.ParentNode.RemoveChild(elt);
			}
		}

		/// <summary>
		/// Clean up any dangling pointers and similar spurious data.
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveSpuriousLinks(HtmlDom pageDom)
		{
			// The validator has complained about area-describedby where the id is not found.
			// I don't think we will do qtips at all in books so let's just remove these altogether for now.
			foreach (var node in pageDom.RawDom.SafeSelectNodes("//*[@aria-describedby]"))
			{
				var elt = node as XmlElement;
				if (elt == null)
					continue;
				elt.RemoveAttribute("aria-describedby");
			}

			// Validator doesn't like empty lang attributes, and they don't convey anything useful, so remove.
			foreach (var node in pageDom.RawDom.SafeSelectNodes("//*[@lang='']"))
			{
				var elt = node as XmlElement;
				if (elt == null)
					continue;
				elt.RemoveAttribute("lang");
			}
			// Validator doesn't like '*' as value of lang attributes, and they don't convey anything useful, so remove.
			foreach (var node in pageDom.RawDom.SafeSelectNodes("//*[@lang='*']"))
			{
				var elt = node as XmlElement;
				if (elt == null)
					continue;
				elt.RemoveAttribute("lang");
			}
		}

		/// <summary>
		/// Remove anything that has class bloom-ui
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveBloomUiElements(HtmlDom pageDom)
		{
			foreach (var node in pageDom.RawDom.SafeSelectNodes("//*[contains(@class,'bloom-ui')]").Cast<XmlNode>().ToList())
			{
				var elt = node as XmlElement;
				if (elt == null)
					continue;
				elt.ParentNode.RemoveChild(elt);
			}
		}

		/// <summary>
		/// Since file names often start with numbers, which epub validation won't allow for element IDs,
		/// stick an 'f' in front. Generally clean up file name to make a valid ID as similar as possible.
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		private string GetIdOfFile(string item)
		{
			string id;
			if (_mapItemToId.TryGetValue(item, out id))
				return id;
			// Attempt to use file name as ID for recognizability
			// Remove spaces which are illegal in XML IDs.
			// Add initial letter to avoid starting with digit
			id = "f" + Path.GetFileNameWithoutExtension(item).Replace(" ", "");
			var idOriginal = id;
			for (int i = 1; _idsUsed.Contains(id.ToLowerInvariant()); i++)
			{
				// Somehow we made a clash
				id = idOriginal + i;
			}
			_idsUsed.Add(id.ToLowerInvariant());
			_mapItemToId[item] = id;

			return id;
		}

		private object GetMediaType(string item)
		{
			switch (Path.GetExtension(item).Substring(1))
			{
				case "xml": // Review
				case "xhtml":
					return "application/xhtml+xml";
				case "jpg":
				case "jpeg":
					return "image/jpeg";
				case "png":
					return "image/png";
				case "css":
					return "text/css";
				case "woff":
					return "application/font-woff"; // http://stackoverflow.com/questions/2871655/proper-mime-type-for-fonts
				case "ttf":
				case "otf":
					return "application/font-sfnt"; // http://stackoverflow.com/questions/2871655/proper-mime-type-for-fonts

			}
			throw new ApplicationException("unexpected file type in file " + item);
		}

		private static void MakeCssLinksAppropriateForEpub(HtmlDom dom)
		{
			dom.RemoveModeStyleSheets();
			dom.SortStyleSheetLinks();
			dom.RemoveFileProtocolFromStyleSheetLinks();
			dom.RemoveDirectorySpecificationFromStyleSheetLinks();
		}

		private HtmlDom GetEpubFriendlyHtmlDomForPage(XmlElement page)
		{
			var headXml = Storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			var dom = new HtmlDom(@"<html>" + headXml + "<body></body></html>");
			dom = Storage.MakeDomRelocatable(dom);
			var body = dom.RawDom.SelectSingleNodeHonoringDefaultNS("//body");
			var pageDom = dom.RawDom.ImportNode(page, true);
			body.AppendChild(pageDom);
			return dom;
		}

		public void Dispose()
		{
			if (_stagingFolder != null)
				_stagingFolder.Dispose();
		}
	}
}