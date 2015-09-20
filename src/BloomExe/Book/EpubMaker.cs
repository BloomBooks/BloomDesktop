using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using BloomTemp;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom.Book
{
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
				_storage = _book.Storage;
			}
		}

		private Book _book;
		private IBookStorage _storage;
		private HashSet<string> _idsUsed = new HashSet<string>();
		private Dictionary<string, string> _mapItemToId = new Dictionary<string, string>();
		private Dictionary<string, string>  _mapSrcPathToDestFileName = new Dictionary<string, string>();
		Dictionary<string, string> _mapChangedFileNames = new Dictionary<string, string>();
		private List<string> _manifestItems;
		private List<string> _spineItems;
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

		/// <summary>
		/// Set to true for unpaginated output.
		/// </summary>
		public bool Unpaginated { get; set; }

		public EpubMaker(Book book) : this()
		{
			Book = book;
			_storage = Book.Storage;
		}

		public EpubMaker()
		{ }

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
				//var id = pageElement.GetAttribute("id");

				++pageIndex;
				var pageDom = GetEpubFriendlyHtmlDomForPage(pageElement);
				pageDom.RemoveModeStyleSheets();
				if (Unpaginated)
				{
					RemoveRegularStylesheets(pageDom);
					pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFileWithThrow(@"epubUnpaginated.css").ToLocalhost());
				}
				else
				{
					pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFileWithThrow(@"basePage.css").ToLocalhost());
					pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFileWithThrow(@"previewMode.css"));
					pageDom.AddStyleSheet(_storage.GetFileLocator().LocateFileWithThrow(@"origami.css"));
				}

				RemoveUnwantedContent(pageDom);

				pageDom.SortStyleSheetLinks();
				pageDom.AddPublishClassToBody();

				MakeCssLinksAppropriateForEpub(pageDom);
				RemoveSpuriousLinks(pageDom);
				RemoveScripts(pageDom);
				FixIllegalIds(pageDom);
				FixPictureSizes(pageDom);
				// Since we only allow one htm file in a book folder, I don't think there is any
				// way this name can clash with anything else.
				var pageDocName = pageIndex + ".xhtml";

				// Manifest has to include all referenced files
				foreach (XmlElement img in pageDom.SafeSelectNodes("//img"))
				{
					var srcAttr = img.Attributes["src"];
					if (srcAttr == null)
						continue; // hug?
					var imgName = srcAttr.Value;
					if (string.IsNullOrEmpty(imgName))
						continue;
					// Images are always directly in the folder
					var srcPath = Path.Combine(Book.FolderPath, imgName);
					CopyFileToEpub(srcPath);
				}

				_manifestItems.Add(pageDocName);
				_spineItems.Add(pageDocName);
				AddAudioOverlay(pageDom, pageDocName);

				// for now, at least, all Bloom book pages currently have the same stylesheets, so we only neeed
				//to look at those stylesheets on the first page
				if (pageIndex == 1)
				{
					_coverPage = pageDocName;
					//css
					foreach (XmlElement link in pageDom.SafeSelectNodes("//link[@rel='stylesheet']"))
					{
						var href = Path.Combine(Book.FolderPath, link.GetAttribute("href"));
						var name = Path.GetFileName(href) ?? href;

						var fl = Book.Storage.GetFileLocator();
						//var path = this.GetFileLocator().LocateFileWithThrow(name);
						var path = fl.LocateFileWithThrow(name);
						CopyFileToEpub(path);
					}
				}
				if (pageIndex == firstContentPageIndex)
					_firstContentPageItem = pageDocName;

				CopyFileToEpub(Path.Combine(Book.FolderPath, "thumbnail.png"));
				RearrangeImageOnTop(pageDom);

				FixChangedFileNames(pageDom);
				// Do this AFTER we copy the CSS files, this file is generated in place just for the epub.
				pageDom.AddStyleSheet("fonts.css"); // enhance: could omit if we don't embed any

				// epub validator requires HTML to use namespace. Do this last to avoid (possibly?) messing up our xpaths.
				pageDom.RawDom.DocumentElement.SetAttribute("xmlns", "http://www.w3.org/1999/xhtml");
				File.WriteAllText(Path.Combine(_contentFolder, pageDocName), pageDom.RawDom.OuterXml);

			}

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

			// content.opf
			var contentOpfPath = Path.Combine(_contentFolder, "content.opf");
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
					new FileInfo(_storage.FolderPath).LastWriteTimeUtc.ToString("s") + "Z")); // like 2012-03-20T11:37:00Z
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
				if (item == "thumbnail.png")
					itemElt.SetAttributeValue("properties", "cover-image");
				if (Path.GetExtension(item).ToLowerInvariant() == ".xhtml")
				{
					var overlay = GetOverlayName(item);
					if (_manifestItems.Contains(overlay))
						itemElt.SetAttributeValue("media-overlay", GetIdOfFile(overlay));
				}
				manifestElt.Add(itemElt);
			}
			var spineElt = new XElement(opf + "spine");
			rootElt.Add(spineElt);
			foreach (var item in _spineItems)
			{
				var itemElt = new XElement(opf + "itemref",
					new XAttribute("idref", GetIdOfFile(item)));
				spineElt.Add(itemElt);
			}
			using (var writer = XmlWriter.Create(contentOpfPath))
				rootElt.WriteTo(writer);
		}

		/// <summary>
		/// Create an audio overlay for the page if appropriate.
		/// We are looking for the page to contain spans with IDs. For each such ID X,
		/// we look for a file _storage.FolderPath/audio/X.mp{3,4}.
		/// If we find at least one such file, we create pageDocName_overlay.smil
		/// with appropriate contents to tell the reader how to read all such spans
		/// aloud.
		/// </summary>
		/// <param name="pageDom"></param>
		/// <param name="pageDocName"></param>
		private void AddAudioOverlay(HtmlDom pageDom, string pageDocName)
		{
			var spansWithIds = pageDom.RawDom.SafeSelectNodes("//span[@id]").Cast<XmlElement>();
			// todo: test case where an mp4 is missing.
			var spansWithAudio =
				spansWithIds.Where(
					x => File.Exists(Path.Combine(_storage.FolderPath, "audio", Path.ChangeExtension(x.Attributes["id"].Value, "mp3")))
					|| File.Exists(Path.Combine(_storage.FolderPath, "audio", Path.ChangeExtension(x.Attributes["id"].Value, "mp4"))));
			if (!spansWithAudio.Any())
				return; // todo: test this case
			var overlayName = GetOverlayName(pageDocName);
			_manifestItems.Add(overlayName);
			string smilNamespace = "http://www.w3.org/ns/SMIL";
			XNamespace smil = smilNamespace;
			string epubNamespace = "http://www.idpf.org/2007/ops";
			XNamespace epub = epubNamespace;
			var seq = new XElement(smil+"seq",
				new XAttribute("id", "id1"), // all <seq> I've seen have this, not sure whether necessary
				new XAttribute(epub + "textref", pageDocName),
				new XAttribute(epub + "type", "bodymatter chapter") // only type I've encountered
				);
			var root = new XElement(smil + "smil",
				new XAttribute( "xmlns", smilNamespace),
				new XAttribute(XNamespace.Xmlns + "epub", epubNamespace),
				new XAttribute("version", "3.0"),
				new XElement(smil + "body",
					seq));
			int index = 1;
			foreach (var span in spansWithAudio)
			{
				var extension = "mp3";
				if (!File.Exists(Path.Combine(_storage.FolderPath, "audio", Path.ChangeExtension(span.Attributes["id"].Value, "mp3"))))
					extension = "mp4";
				var spanId = span.Attributes["id"].Value;
				seq.Add(new XElement(smil+"par",
					new XAttribute("id", "s" + index++),
					new XElement(smil + "text",
						new XAttribute("src", pageDocName + "#" + spanId)),
						new XElement(smil + "audio",
							new XAttribute("src", "audio/" + spanId + "." + extension))));
				CopyFileToEpub(Path.Combine(_storage.FolderPath, "audio", Path.ChangeExtension(spanId, extension)));
			}
			var overlayPath = Path.Combine(_contentFolder, overlayName);
			using (var writer = XmlWriter.Create(overlayPath))
				root.WriteTo(writer);

		}

		private static string GetOverlayName(string pageDocName)
		{
			return Path.ChangeExtension(Path.GetFileNameWithoutExtension(pageDocName) + "_overlay", "smil");
		}

		public void SaveEpub(string destinationEpubPath)
		{
			StageEpub();
			FinishEpub(destinationEpubPath);
		}

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
		/// Bloom uses tricky styles to re-arrange the elements on a basic book's Basic Text and Picture page.
		/// We want things to come out in the right order with a simple stylesheet.
		/// </summary>
		/// <param name="pageDom"></param>
		private void RearrangeImageOnTop(HtmlDom pageDom)
		{
			var marginBox = pageDom.SafeSelectNodes("//div[@class='marginBox']").Cast<XmlElement>().FirstOrDefault();
			if (marginBox == null)
				return;
			var page = (XmlElement) marginBox.ParentNode;
			var pageClass = " " + AttrVal(page, "class") + " ";
			if (!pageClass.Contains(" bloom-page ") || !pageClass.Contains(" imageOnTop ")
				|| (!pageClass.Contains(" bloom-bilingual ") && !pageClass.Contains(" .bloom-trilingual ")))
				return; // not the kind of page we want to fix, or not bilingual mode
			var imageContainer = FindChildWithClass(marginBox, "bloom-imageContainer");
			var transGroup = FindChildWithClass(marginBox, "bloom-translationGroup");
			if (imageContainer == null || transGroup == null)
				return;
			var content1 = FindChildWithClass(transGroup, "bloom-content1");
			var content2 = FindChildWithClass(transGroup, "bloom-content2");
			if (content1 == null || content2 == null)
				return;
			var dup = (XmlElement)transGroup.CloneNode(false);
			marginBox.InsertBefore(dup, imageContainer);
			transGroup.RemoveChild(content1);
			dup.AppendChild(content1);
			if (dup.Attributes["id"] != null)
				dup.RemoveAttribute("id");
		}

		XmlElement FindChildWithClass(XmlElement parent, string classVal)
		{
			foreach (var node in parent.ChildNodes)
			{
				var elt = node as XmlElement;
				if (elt == null)
					continue;
				var eltClass = " " + AttrVal(elt, "class") + " ";
				if (eltClass.Contains(" " + classVal + " "))
					return elt;
			}
			return null;
		}

		/// <summary>
		/// Try to embed the fonts we need.
		/// </summary>
		private void EmbedFonts()
		{
			var fontsWanted = GetFontsUsed();
			var filesToEmbed = fontsWanted.SelectMany(GetFilesForFont).ToArray();
			foreach (var file in filesToEmbed)
			{
				CopyFileToEpub(file);
			}
			var sb = new StringBuilder();
			foreach (var font in fontsWanted)
			{
				FontGroup group;
				if (_fontNameToFiles.TryGetValue(font, out group))
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
			var findFF = new Regex("font-family:\\s*(['\"])([^'\"]*)\\1");
			foreach (var ss in Directory.GetFiles(Book.FolderPath, "*.css"))
			{
				var root = File.ReadAllText(ss, Encoding.UTF8);
				foreach (Match match in findFF.Matches(root))
				{
					result.Add(match.Groups[2].Value);
				}
			}
			return result;
		}

		/// <summary>
		/// Set of up to four files useful for a given font name
		/// </summary>
		class FontGroup : IEnumerable<string>
		{
			public string Normal;
			public string Bold;
			public string Italic;
			public string BoldItalic;

			public void Add(GlyphTypeface gtf, string path)
			{
				if (Normal == null)
					Normal = path;
				if (gtf.Style == System.Windows.FontStyles.Italic)
				{
					if (isBoldFont(gtf))
						BoldItalic = path;
					else
						Italic = path;
				}
				else
				{
					if (isBoldFont(gtf))
						Bold = path;
					else
						Normal = path;
				}
			}

			private static bool isBoldFont(GlyphTypeface gtf)
			{
				return gtf.Weight.ToOpenTypeWeight() > 600;
			}

			public IEnumerator<string> GetEnumerator()
			{
				if (Normal != null)
					yield return Normal;
				if (Bold != null)
					yield return Bold;
				if (Italic != null)
					yield return Italic;
				if (BoldItalic != null)
					yield return BoldItalic;
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}

		Dictionary<string, FontGroup> _fontNameToFiles;

		/// <summary>
		/// This is really hard. We somehow need to figure out what font file(s) are used for a particular font.
		/// http://stackoverflow.com/questions/16769758/get-a-font-filename-based-on-the-font-handle-hfont
		/// has some ideas; the result would be Windows-specific. And at some point we should ideally consider
		/// what faces are needed.
		/// For now we use brute force.
		/// 'Andika New Basic' -> AndikaNewBasic-{R,B,I,BI}.ttf
		/// Arial -> arial.ttf/ariali.ttf/arialbd.ttf/arialbi.ttf
		/// 'Charis SIL' -> CharisSIL{R,B,I,BI}.ttf (note: no hyphen)
		/// Doulos SIL -> DoulosSILR
		/// </summary>
		/// <param name="fontName"></param>
		/// <returns>enumeration of file paths (possibly none) that contain data for the specified font name</returns>
		private IEnumerable<string> GetFilesForFont(string fontName)
		{
			// Review Linux: very likely something here is not portable.
			if (_fontNameToFiles == null)
			{
				_fontNameToFiles = new Dictionary<string, FontGroup>();
				foreach (var fontFile in Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)))
				{
					// Epub only understands these types, so skip anything else.
					switch (Path.GetExtension(fontFile))
					{
						case ".ttf":
						case ".otf":
						case ".woff":
							break;
						default:
							continue;
					}
					GlyphTypeface gtf;
					try
					{
						gtf = new GlyphTypeface(new Uri("file:///" + fontFile));
					}
					catch (Exception)
					{
						continue; // file is somehow corrupt or not really a font file? Just ignore it.
					}
					switch (gtf.EmbeddingRights)
					{
						case FontEmbeddingRight.Editable:
						case FontEmbeddingRight.EditableButNoSubsetting:
						case FontEmbeddingRight.Installable:
						case FontEmbeddingRight.InstallableButNoSubsetting:
						case FontEmbeddingRight.PreviewAndPrint:
						case FontEmbeddingRight.PreviewAndPrintButNoSubsetting:
							break;
						default:
							continue; // not allowed to embed (enhance: warn user?)
					}
					var fc = new PrivateFontCollection();
					try
					{
						fc.AddFontFile(fontFile);
					}
					catch (FileNotFoundException)
					{
						continue; // not sure how this can happen but I've seen it.
					}
					var name = fc.Families[0].Name;
					// If you care about bold, italic, etc, you can filter here.
					FontGroup files;
					if (!_fontNameToFiles.TryGetValue(name, out files))
					{
						files = new FontGroup();
						_fontNameToFiles[name] = files;
					}
					files.Add(gtf, fontFile);
				}
			}
			FontGroup result;
			if (!_fontNameToFiles.TryGetValue(fontName, out result))
				return new string[0];
			return result;
		}

		/// <summary>
		/// Typically pictures are given an absolute size in px, which looks right given
		/// the current absolute size of the page it is on. For an epub, a percent size
		/// will work better. We calculate it based on the page sizes and margins in
		/// BasePage.less and commonMixins.less. The page size definitions are unlikely
		/// to change, but change might be needed here if there is a change to the main
		/// .marginBox rule in basePage.less
		/// </summary>
		/// <param name="pageDom"></param>
		private void FixPictureSizes(HtmlDom pageDom)
		{
			bool firstTime = true;
			double pageWidthMm = 210; // assume A5 Portrait if not specified
			foreach (XmlElement img in pageDom.RawDom.SafeSelectNodes("//img"))
			{
				var marginBox = img.ParentNode.ParentNode as XmlElement;
				// For now we only attempt to adjust pictures contained in the marginBox.
				// To do better than this we will probably need to actually load the HTML into
				// a browser; even then it will be complex.
				while (marginBox != null && !HasClass(marginBox, "marginBox"))
					marginBox = marginBox.ParentNode as XmlElement;
				if (marginBox == null)
					continue;
				var page = marginBox.ParentNode as XmlElement;
				if (!HasClass(page, "bloom-page"))
					continue; // or return? marginBox should be child of page!
				if (firstTime)
				{
					var pageClass = AttrVal(page, "class").Split().FirstOrDefault(c => c.Contains("Portrait") || c.Contains("Landscape"));
					const int A4Width = 210;
					const int A4Height = 297;
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
					}
					firstTime = false;
				}
				var imgStyle = AttrVal(img, "style");
				// We want to take something like 'width:334px; height:220px; margin-left: 34px; margin-top: 0px;'
				// and change it to something like 'width:75%; height:auto; margin-left: 10%; margin-top: 0px;'
				// This first pass deals with width.
				if (ConvertWidth("width", pageWidthMm, ref imgStyle)) continue;

				// Now change height to auto, to preserve aspect ratio
				imgStyle = new Regex("height:\\s*\\d+px").Replace(imgStyle, "height:auto");
				if (!imgStyle.Contains("height"))
					imgStyle = "height:auto; " + imgStyle;

				// Similarly fix indent
				ConvertWidth("margin-left", pageWidthMm, ref imgStyle);

				img.SetAttribute("style", imgStyle);
			}
		}

		// Returns true if we don't find the expected style
		private static bool ConvertWidth(string width, double pageWidthMm, ref string imgStyle)
		{
			var match = new Regex("(.*" + width + ":\\s*)(\\d+)px(.*)").Match(imgStyle);
			if (!match.Success)
				return true;
			var widthPx = int.Parse(match.Groups[2].Value);
			var widthInch = widthPx/96.0; // in print a CSS px is exactly 1/96 inch
			const int marginBoxMarginMm = 40; // see basePage.less SetMarginBox.
			const double mmPerInch = 25.4;
			var marginBoxWidthInch = (pageWidthMm - marginBoxMarginMm)/mmPerInch;
			// 1/10 percent is close enough and more readable/testable than arbitrary precision; make a string with one decimal
			var newWidth = (Math.Round(widthInch/marginBoxWidthInch*1000)/10).ToString("F1");
			imgStyle = match.Groups[1] + newWidth  + "%" + match.Groups[3];
			return false;
		}

		string AttrVal(XmlElement elt, string name)
		{
			var attr = elt.Attributes[name];
			if (attr == null)
				return "";
			return attr.Value;
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
			// Remove and left-over bubbles
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
					var oldName = elt.Attributes[attr].Value;
					string newName;
					if (_mapChangedFileNames.TryGetValue(oldName, out newName))
						elt.SetAttribute(attr, newName);
				}
			}
		}

		private void CopyFileToEpub(string srcPath)
		{
			if (_mapSrcPathToDestFileName.ContainsKey(srcPath))
				return; // File already present, must be used more than once.
			// Validator warns against spaces in filenames.
			string originalFileName;
			if (srcPath.StartsWith(_storage.FolderPath))
				originalFileName = srcPath.Substring(_storage.FolderPath.Length + 1).Replace("\\", "/"); // allows keeping folder structure
			else
				originalFileName = Path.GetFileName(srcPath); // probably can't happen, but in case, put at root.
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
			fileName = fileName.Replace("/", "_");
			if (originalFileName != fileName)
				_mapChangedFileNames[originalFileName] = fileName;
			Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
			CopyFile(srcPath, dstPath);
			_manifestItems.Add(fileName);
			_mapSrcPathToDestFileName[srcPath] = fileName;
		}

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
		/// Since file names often start with numbers, which epub validation won't allow for element IDs,
		/// stick an 'f' in front.
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
				case "smil":
					return "application/smil+xml";
				case "mp4":
					return "audio/mp4";
				case "mp3":
					return "audio/mpeg";
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
			var headXml = _storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
			var dom = new HtmlDom(@"<html>" + headXml + "<body></body></html>");
			dom = _storage.MakeDomRelocatable(dom);
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