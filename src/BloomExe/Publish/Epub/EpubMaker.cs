using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Bloom.Api;
using Bloom.Book;
using BloomTemp;
#if !__MonoCS__
using NAudio.Wave;
#endif
using SIL.IO;
using SIL.Xml;

namespace Bloom.Publish.Epub
{
	/// <summary>
	/// This class handles the process of creating an ePUB out of a bloom book.
	/// The process has two stages, corresponding to the way our UI displays a preview and
	/// then allows the user to save. We 'stage' the ePUB by generating all the files into a
	/// temporary folder. Then, if the user says to save, we actually zip them into an ePUB.
	///
	/// Currently, we don't attempt to support sophisticated page layouts in epubs.
	/// For one thing, we have not identified any epub readers on phones that support enough
	/// EPUB3/HTML features to actually make an EPUB3 fixed-layout page that looks exactly
	/// like what Bloom puts on paper. For another, the device screen might be much smaller
	/// than the page was designed for, and the user might be annoyed if he has to zoom
	/// and scroll horizontally, or that the page does not obey the reader's font controls.
	///
	/// So, instead, we currently only attempt to support pages with a basically vertical layout.
	/// Bloom will do something with more complex ones, but it won't look much like the
	/// original. Basically, we will arrange the page elements one below the other, make the
	/// pictures about the original fraction of page wide, and hope for the best.
	/// Thus, any sort of side-by-side layout will be lost in epubs.
	///
	/// There is also no guarantee that what started out as a single page will fit on one
	/// screen in the reader. All readers we have tested will split a source page (which
	/// they treat more like a chapter) into as many pages as needed to show all the content.
	/// But a picture is not guaranteed to be on the same screen as text that was supposed
	/// to be on the same page.
	///
	/// We do output styles that should control text font and size, but readers vary widely
	/// in whether they obey these at all and, if so, how they interpret a particular font
	/// size.
	///
	/// Epubs deliberately omit blank pages.
	///
	/// Epubs currently have a very simplistic table of contents, with entries for the
	/// start of the book and the first content page.
	///
	/// We also simplified in various other ways:
	/// - although we generally embed fonts used in the document, we don't embed any that
	/// indicate they do not permit embedding. Currently we don't give any warning about this,
	/// nor any way for the user to override it if he actually has the right to embed.
	/// - to save space, we don't embed bold or italic variants of embedded fonts,
	/// even if bold or italic text in those fonts occurs.
	/// - we also don't support vertical alignment control (in what height would we vertically
	/// align something?); each element just takes up as much vertical space as it needs.
	/// - Currently epubs can only contain audio for one language--the primary vernacular one.
	/// </summary>
	public class EpubMaker : IDisposable
	{
		public const string kEPUBExportFolder = "ePUB export";

		public Book.Book Book
		{
			get { return _book; }
			internal set { _book = value; }
		}

		private Book.Book _book;

		private Book.Book _originalBook;
		// This is a shorthand for _book.Storage. Since that is something of an implementation secret of Book,
		// it also provides a safe place for us to make changes if we ever need to get the Storage some other way.
		private IBookStorage Storage
		{
			get { return _book.Storage; }
		}

		// Keeps track of IDs that have been used in the manifest. These are generated to roughly match file
		// names, but the algorithm could pathologically produce duplicates, so we guard against this.
		private HashSet<string> _idsUsed = new HashSet<string>();
		// Keeps track of the ID actually generated for each resource. This is generally algorithmic,
		// but in case of duplicates the ID for an item might not be what the algorithm predicts.
		private Dictionary<string, string> _mapItemToId = new Dictionary<string, string>();
		// Keep track of the files we already copied to the ePUB, so we don't copy them again to a new name.
		private Dictionary<string, string> _mapSrcPathToDestFileName = new Dictionary<string, string>();
		// Some file names are not allowed in epubs, in which case, we have to rename the file and change the
		// link in the HTML. This keeps track of files that needed to be renamed when copied into the ePUB.
		Dictionary<string, string> _mapChangedFileNames = new Dictionary<string, string>();
		// All the things (files) we need to list in the manifest
		private List<string> _manifestItems;
		// Duration of each item of type application/smil+xml (key is ID of item)
		Dictionary<string, TimeSpan> _pageDurations = new Dictionary<string, TimeSpan>();
		// The things we need to list in the 'spine'...defines the normal reading order of the book
		private List<string> _spineItems;
		// Counter for creating output page files.
		int _pageIndex;
		// We track the first page that is actually content and link to it in our rather trivial table of contents.
		private string _firstContentPageItem;
		private string _coverPage;
		private string _contentFolder;
		private string _navFileName;
		// This temporary folder holds the staging folder with the bloom content. It also (temporarily)
		// holds a copy of the Readium code, since I haven't been able to figure out how to get that
		// code to redirect to display a folder which isn't a child of the folder containing the
		// readium HTML.
		private TemporaryFolder _outerStagingFolder;
		public string BookInStagingFolder { get; private set; }
		private BookThumbNailer _thumbNailer;
		public bool PublishWithoutAudio { get; set; }
		Browser _browser = new Browser();
		private BookServer _bookServer;

		/// <summary>
		/// Set to true for unpaginated output. This is something of a misnomer...any better ideas?
		/// If it is true (which currently it always is), we remove the stylesheets for precise page layout
		/// and just output the text and pictures in order with a simple default stylesheet.
		/// Rather to my surprise, the result still has page breaks where expected, though the reader may
		/// add more if needed.
		/// </summary>
		public bool Unpaginated { get; set; }

		public EpubMaker(BookThumbNailer thumbNailer, NavigationIsolator _isolator, BookServer bookServer)
		{
			_thumbNailer = thumbNailer;
			_browser.Isolator = _isolator;
			_bookServer = bookServer;
		}

		/// <summary>
		/// Generate all the files we will zip into the ePUB for the current book into the StagingFolder.
		/// It is required that the parent of the StagingFolder is a temporary folder into which we can
		/// copy the Readium stuff. This folder is deleted when the EpubMaker is disposed.
		/// </summary>
		public void StageEpub(bool publishWithoutAudio = false)
		{
			PublishWithoutAudio = publishWithoutAudio;
			if(!string.IsNullOrEmpty(BookInStagingFolder))
				return; //already staged

			//I (JH) kept having trouble making epubs because this kept getting locked.
			SIL.IO.DirectoryUtilities.DeleteDirectoryRobust(Path.Combine(Path.GetTempPath(), kEPUBExportFolder));

			_outerStagingFolder = new TemporaryFolder(kEPUBExportFolder);
			var tempBookPath = Path.Combine(_outerStagingFolder.FolderPath, Path.GetFileName(Book.FolderPath));
			_originalBook = _book;
			if (_bookServer != null)
			{
				// It should only be null while running unit tests.
				// Eventually, we want a unit test that checks this device xmatter behavior.
				// But don't have time for now.
				_book = BookCompressor.MakeDeviceXmatterTempBook(_book, _bookServer, tempBookPath);
			}

			// The readium control remembers the current page for each book.
			// So it is useful to have a unique name for each one.
			// However, it needs to be something we can put in a URL without complications,
			// so a guid is better than say the book's own folder name.
			BookInStagingFolder = Path.Combine(_outerStagingFolder.FolderPath, _book.ID);
			// in case of previous versions // Enhance: delete when done? Generate new name if conflict?
			var contentFolderName = "content";
			_contentFolder = Path.Combine(BookInStagingFolder, contentFolderName);
			Directory.CreateDirectory(_contentFolder); // also creates parent staging directory
			_pageIndex = 0;
			_manifestItems = new List<string>();
			_spineItems = new List<string>();
			_firstContentPageItem = null;
			foreach(XmlElement pageElement in Book.GetPageElements())
			{
				var pageDom = MakePageFile(pageElement);
				if (pageDom == null)
					continue;	// page was blank, so we're not adding it to the ePUB.
				// for now, at least, all Bloom book pages currently have the same stylesheets, so we only neeed
				//to look at those stylesheets on the first page
				if (_pageIndex == 1)
					CopyStyleSheets(pageDom);
			}

			string coverPageImageFile = "thumbnail-256.png";
			// This thumbnail is otherwise only made when uploading, so it may be out of date.
			// Just remake it every time.
			ApplicationException thumbNailException = null;
			try
			{
				_thumbNailer.MakeThumbnailOfCover(Book, 256);
			}
			catch(ApplicationException e)
			{
				thumbNailException = e;
			}
			var coverPageImagePath = Path.Combine(Book.FolderPath, coverPageImageFile);
			if(thumbNailException != null || !RobustFile.Exists(coverPageImagePath))
			{
				NonFatalProblem.Report(ModalIf.All, PassiveIf.All,
					"Bloom failed to make a high-quality cover page for your book (BL-3209)",
					"We will try to make the book anyway, but you may want to try again.",
					thumbNailException);

				coverPageImageFile = "thumbnail.png"; // Try a low-res image, which should always exist
				coverPageImagePath = Path.Combine(Book.FolderPath, coverPageImageFile);
				if(!RobustFile.Exists(coverPageImagePath))
				{
					// I don't think we can make an epub without a cover page so at this point we've had it.
					// I suppose we could recover without actually crashing but it doesn't seem worth it unless this
					// actually happens to real users.
					throw new FileNotFoundException("Could not find or create thumbnail for cover page (BL-3209)", coverPageImageFile);
				}
			}
			CopyFileToEpub(coverPageImagePath);

			EmbedFonts(); // must call after copying stylesheets
			MakeNavPage();

			//supporting files

			// Fixed requirement for all epubs
			RobustFile.WriteAllText(Path.Combine(BookInStagingFolder, "mimetype"), @"application/epub+zip");

			var metaInfFolder = Path.Combine(BookInStagingFolder, "META-INF");
			Directory.CreateDirectory(metaInfFolder);
			var containerXmlPath = Path.Combine(metaInfFolder, "container.xml");
			RobustFile.WriteAllText(containerXmlPath, @"<?xml version='1.0' encoding='utf-8'?>
					<container version='1.0' xmlns='urn:oasis:names:tc:opendocument:xmlns:container'>
					<rootfiles>
					<rootfile full-path='content/content.opf' media-type='application/oebps-package+xml'/>
					</rootfiles>
					</container>");

			MakeManifest(coverPageImageFile);
		}

		private void MakeManifest(string coverPageImageFile)
		{
			// content.opf: contains primarily the manifest, listing all the content files of the ePUB.
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
			TimeSpan bookDuration = new TimeSpan();
			foreach(var item in _manifestItems)
			{
				var mediaType = GetMediaType(item);
				var idOfFile = GetIdOfFile(item);
				var itemElt = new XElement(opf + "item",
					new XAttribute("id", idOfFile),
					new XAttribute("href", item),
					new XAttribute("media-type", mediaType));
				// This isn't very useful but satisfies a validator requirement until we think of
				// something better.
				if(item == _navFileName)
					itemElt.SetAttributeValue("properties", "nav");
				if(item == coverPageImageFile)
					itemElt.SetAttributeValue("properties", "cover-image");
				if(Path.GetExtension(item).ToLowerInvariant() == ".xhtml")
				{
					var overlay = GetOverlayName(item);
					if(_manifestItems.Contains(overlay))
						itemElt.SetAttributeValue("media-overlay", GetIdOfFile(overlay));
				}
				manifestElt.Add(itemElt);
				if(mediaType == "application/smil+xml")
				{
					// need a metadata item giving duration (possibly only to satisfy Pagina validation,
					// but that IS an objective).
					TimeSpan itemDuration = _pageDurations[idOfFile];
					bookDuration += itemDuration;
					metadataElt.Add(new XElement(opf + "meta",
						new XAttribute("property", "media:duration"),
						new XAttribute("refines", "#" + idOfFile),
						new XText(itemDuration.ToString())));
				}
			}
			if(bookDuration.TotalMilliseconds > 0)
			{
				metadataElt.Add(new XElement(opf + "meta",
					new XAttribute("property", "media:duration"),
					new XText(bookDuration.ToString())));
			}
			MakeSpine(opf, rootElt, manifestPath);
		}


		/// <summary>
		/// Make a compressed audio file for the specified .wav file.
		/// (Or return null if it can't be done becaus we don't have a LAME package installed.)
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		// internal and virtual for testing.
		public bool GetBookHasAudio()
		{
			return
				Book.RawDom.SafeSelectNodes("//span[@id]")
					.Cast<XmlElement>()
					.Any(
						span => AudioProcessor.GetWavOrMp3Exists(Storage.FolderPath, span.Attributes["id"].Value) != null);
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
			var spansWithIds = pageDom.RawDom.SafeSelectNodes(".//span[@id]").Cast<XmlElement>();
			var spansWithAudio =
				spansWithIds.Where(
					x => AudioProcessor.GetOrCreateCompressedAudioIfWavExists(Storage.FolderPath, x.Attributes["id"].Value) != null);
			if(!spansWithAudio.Any())
				return;
			var overlayName = GetOverlayName(pageDocName);
			_manifestItems.Add(overlayName);
			string smilNamespace = "http://www.w3.org/ns/SMIL";
			XNamespace smil = smilNamespace;
			string epubNamespace = "http://www.idpf.org/2007/ops";
			XNamespace epub = epubNamespace;
			var seq = new XElement(smil + "seq",
				new XAttribute("id", "id1"), // all <seq> I've seen have this, not sure whether necessary
				new XAttribute(epub + "textref", pageDocName),
				new XAttribute(epub + "type", "bodymatter chapter") // only type I've encountered
			);
			var root = new XElement(smil + "smil",
				new XAttribute("xmlns", smilNamespace),
				new XAttribute(XNamespace.Xmlns + "epub", epubNamespace),
				new XAttribute("version", "3.0"),
				new XElement(smil + "body",
					seq));
			int index = 1;
			TimeSpan pageDuration = new TimeSpan();
			foreach(var span in spansWithAudio)
			{
				var spanId = span.Attributes["id"].Value;
				var path = AudioProcessor.GetOrCreateCompressedAudioIfWavExists(Storage.FolderPath, spanId);
				var dataDurationAttr = span.Attributes["data-duration"];
				if(dataDurationAttr != null)
				{
					// Make sure we parse "3.14159" properly since that's the form we'll see regardless of current locale.
					// (See http://issues.bloomlibrary.org/youtrack/issue/BL-4374.)
					pageDuration += TimeSpan.FromSeconds(Double.Parse(dataDurationAttr.Value, System.Globalization.CultureInfo.InvariantCulture));
				}
				else
				{
					//var durationSeconds = TagLib.File.Create(path).Properties.Duration.TotalSeconds;
					//duration += new TimeSpan((long)(durationSeconds * 1.0e7)); // argument is in ticks (100ns)
					// Haven't found a good way to get duration from MP3 without adding more windows-specific
					// libraries. So for now we'll figure it from the wav if we have it. If not we do a very
					// crude estimate from file size. Hopefully good enough for BSV animation.
					var wavPath = Path.ChangeExtension(path, "wav");
					if(RobustFile.Exists(wavPath))
					{
#if __MonoCS__
						pageDuration += new TimeSpan(new FileInfo(path).Length);	// TODO: this needs to be fixed for Linux/Mono
#else
						using(WaveFileReader wf = RobustIO.CreateWaveFileReader(wavPath))
							pageDuration += wf.TotalTime;
#endif
					}
					else
					{
						NonFatalProblem.Report(ModalIf.All, PassiveIf.All,
							"Bloom could not find one of the expected audio files for this book, nor a precomputed duration. Bloom can only make a very rough estimate of the length of the mp3 file.");
						// Crude estimate. In one sample, a 61K mp3 is 7s long.
						// So, multiply by 7 and divide by 61K to get seconds.
						// Then, to make a TimeSpan we need ticks, which are 0.1 microseconds,
						// hence the 10000000.
						pageDuration += new TimeSpan(new FileInfo(path).Length*7*10000000/61000);
					}
				}
				var epubPath = CopyFileToEpub(path);
				seq.Add(new XElement(smil + "par",
					new XAttribute("id", "s" + index++),
					new XElement(smil + "text",
						new XAttribute("src", pageDocName + "#" + spanId)),
					new XElement(smil + "audio",
						// Note that we don't need to preserve any audio/ in the path.
						// We now mangle file names so as to replace any / (with _2f) so all files
						// are at the top level in the ePUB. Makes one less complication for readers.
						new XAttribute("src", Path.GetFileName(epubPath)))));
			}
			_pageDurations[GetIdOfFile(overlayName)] = pageDuration;
			var overlayPath = Path.Combine(_contentFolder, overlayName);
			using(var writer = XmlWriter.Create(overlayPath))
				root.WriteTo(writer);

		}

		private static string GetOverlayName(string pageDocName)
		{
			return Path.ChangeExtension(Path.GetFileNameWithoutExtension(pageDocName) + "_overlay", "smil");
		}

		private void MakeSpine(XNamespace opf, XElement rootElt, string manifestPath)
		{
			// Generate the spine, which indicates the top-level readable content in order.
			// These IDs must match the corresponding ones in the manifest, since the spine
			// doesn't indicate where to actually find the content.
			var spineElt = new XElement(opf + "spine");
			if(this.Book.CollectionSettings.IsLanguage1Rtl)
			{
				spineElt.SetAttributeValue("page-progression-direction","rtl");
			}
			rootElt.Add(spineElt);
			foreach(var item in _spineItems)
			{
				var itemElt = new XElement(opf + "itemref",
					new XAttribute("idref", GetIdOfFile(item)));
				spineElt.Add(itemElt);
			}
			using(var writer = XmlWriter.Create(manifestPath))
				rootElt.WriteTo(writer);
		}

		private void CopyStyleSheets(HtmlDom pageDom)
		{
			foreach(XmlElement link in pageDom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var href = Path.Combine(Book.FolderPath, link.GetAttribute("href"));
				var name = Path.GetFileName(href);
				if(name == "fonts.css")
					continue; // generated file for this book, already copied to output.
				string path;
				if (name == "customCollectionStyles.css" || name == "settingsCollectionStyles.css")
				{
					// These two files should be in the original book's parent folder, not in some arbitrary place
					// in our search path.
					path = Path.Combine(Path.GetDirectoryName(_originalBook.FolderPath), name);
					// It's OK not to find these.
					if (!File.Exists(path))
						path = null;
				}
				else
				{
					var fl = Storage.GetFileLocator();
					path = fl.LocateFileWithThrow(name);
				}
				if (path != null)
					CopyFileToEpub(path);
			}
		}

		/// <summary>
		/// If the page is not blank, make the page file and return the HtmlDom of the page.
		/// If the page is blank, return null without writing anything to disk.
		/// </summary>
		/// <returns>the HtmlDom of the page if not blank, null if it was blank</returns>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4288 for discussion of blank pages.
		/// </remarks>
		private HtmlDom MakePageFile(XmlElement pageElement)
		{
			// nonprinting pages (e.g., comprehension questions) are omitted for now
			if (pageElement.Attributes["class"]?.Value?.Contains("nonprinting") ?? false)
			{
				return null;
			}
			var pageDom = GetEpubFriendlyHtmlDomForPage(pageElement);

			// Note, the following stylsheet stuff can be quite bewildering...
			// Testing shows that these stylesheets are not actually used
			// in RemoveUnwantedContent(), which falls back to the stylsheets in place for the book, which in turn,
			// in unit tests, is backed by a simple mocked BookStorage which doesn't have the stylesheet smarts. Sigh.

			pageDom.RemoveModeStyleSheets();
			if(Unpaginated)
			{
				RemoveRegularStylesheets(pageDom);
				pageDom.AddStyleSheet(Storage.GetFileLocator().LocateFileWithThrow(@"baseEPUB.css").ToLocalhost());
				pageDom.AddStyleSheet(Storage.GetFileLocator().LocateFileWithThrow(@"Device-XMatter.css").ToLocalhost());
			}
			else
			{
				// Review: this branch is not currently used. Very likely we need SOME different stylesheets
				// from the printed book, possibly including baseEPUB.css, if it's even possible to make
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

			// Check for a blank page before storing any data from this page or copying any files on disk.
			if (IsBlankPage(pageDom.RawDom.DocumentElement))
				return null;

			// Since we only allow one htm file in a book folder, I don't think there is any
			// way this name can clash with anything else.
			++_pageIndex;
			var pageDocName = _pageIndex + ".xhtml";
			if (_pageIndex == 1)
				_coverPage = pageDocName;

			CopyImages(pageDom);

			_manifestItems.Add(pageDocName);
			_spineItems.Add(pageDocName);
			if(!PublishWithoutAudio)
				AddAudioOverlay(pageDom, pageDocName);

			// Record the first non-blank page that isn't front-matter as the first content page.
			// Note that pageElement is a <div> with a class attribute that contains page level
			// formatting information.
			if (_firstContentPageItem == null && !pageElement.GetAttribute("class").Contains("bloom-frontMatter"))
				_firstContentPageItem = pageDocName;

			FixChangedFileNames(pageDom);
			pageDom.AddStyleSheet("fonts.css"); // enhance: could omit if we don't embed any

			// ePUB validator requires HTML to use namespace. Do this last to avoid (possibly?) messing up our xpaths.
			pageDom.RawDom.DocumentElement.SetAttribute("xmlns", "http://www.w3.org/1999/xhtml");
			RobustFile.WriteAllText(Path.Combine(_contentFolder, pageDocName), pageDom.RawDom.OuterXml);
			return pageDom;
		}

		/// <summary>
		/// Check whether this page will actually display anything.  Although paper books allow blank pages
		/// without confusing readers, ePUB books should not have blank pages.
		/// </summary>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4288.
		/// Note that this method is called after RemoveUnwantedContent(), RemoveBloomUiElements(),
		/// RemoveSpuriousLinks(), and RemoveScripts() have all been called.
		/// </remarks>
		private bool IsBlankPage(XmlElement pageElement)
		{
			foreach (XmlElement body in pageElement.GetElementsByTagName("body"))
			{
				// This may not be fool proof, but it works okay on an empty basic book.  It also works on a test
				// book with an empty page in the middle of the book, and on the two sample shell books shipped
				// with Bloom.
				if (!String.IsNullOrWhiteSpace(body.InnerText))
					return false;
				break;	// There should be only one body element.
			}
			// Any real image will be displayed.  Image only pages are allowed in Bloom.
			// (This includes background images.)
			foreach(XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(pageElement))
			{
				bool isBrandingFile;	// not used here, but part of method signature
				var path = FindRealImageFileIfPossible(img, out isBrandingFile);
				if (!String.IsNullOrEmpty(path) && Path.GetFileName(path) != "placeHolder.png")	// consider blank if only placeholder image
					return false;
			}
			return true;
		}

		private void CopyImages(HtmlDom pageDom)
		{
			// Manifest has to include all referenced files
			foreach(XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(pageDom.RawDom.DocumentElement))
			{
				bool isBrandingFile;
				var srcPath = FindRealImageFileIfPossible(img, out isBrandingFile);
				if (srcPath == null)
					continue;	// REVIEW: should we remove the element since the image source is empty?
				if (srcPath == String.Empty)
				{
					img.ParentNode.RemoveChild(img);	// the image source file can't be found.
				}
				else
				{
					CopyFileToEpub(srcPath);
					if (isBrandingFile)
						img.SetAttribute("src", Path.GetFileName(srcPath));
				}
			}
		}

		/// <summary>
		/// Find the image file in the file system if possible, returning its full path if it exists.
		/// Return null if the source url is empty.
		/// Return String.Empty if the image file does not exist in the file system.
		/// Also return a flag to indicate whether the image file is a branding api image file.
		/// </summary>
		private string FindRealImageFileIfPossible(XmlElement img, out bool isBrandingFile)
		{
			isBrandingFile = false;
			var url = HtmlDom.GetImageElementUrl(img);
			if (url == null || url.PathOnly == null || String.IsNullOrEmpty(url.NotEncoded))
				return null; // very weird, but all we can do is ignore it.
			// Notice that we use only the path part of the url. For some unknown reason, some bloom books
			// (e.g., El Nino in the library) have a query in some image sources, and at least some ePUB readers
			// can't cope with it.
			var filename = url.PathOnly.NotEncoded;
			if (String.IsNullOrEmpty(filename))
				return null;
			// Images are always directly in the folder
			var srcPath = Path.Combine(Book.FolderPath, filename);
			if (srcPath == BrandingApi.kApiBrandingImage)
			{
				isBrandingFile = true;
				return FindBrandingImageIfPossible(url.NotEncoded);
			}
			if (RobustFile.Exists(srcPath))
				return srcPath;
			return String.Empty;
		}

		/// <summary>
		/// Check whether the desired branding image file exists.  If it does, return its full path.
		/// Otherwise, return String.Empty;
		/// </summary>
		private string FindBrandingImageIfPossible(string urlPath)
		{
			var idx = urlPath.IndexOf('?');
			if (idx > 0)
			{
				var query = urlPath.Substring(idx + 1);
				var parsedQuery = HttpUtility.ParseQueryString(query);
				var file = parsedQuery["id"];
				if (!String.IsNullOrEmpty(file))
				{
					var path = Bloom.Api.BrandingApi.FindBrandingImageFileIfPossible(Book.CollectionSettings.BrandingProjectKey, file);
					if (!String.IsNullOrEmpty(path) && RobustFile.Exists(path))
						return path;
				}
			}
			return String.Empty;
		}

		// Combines staging and finishing (currently just used in tests).
		public void SaveEpub(string destinationEpubPath)
		{
			if(string.IsNullOrEmpty(BookInStagingFolder))
			{
				StageEpub();
			}
			FinishEpub(destinationEpubPath);
		}

		/// <summary>
		/// Finish publishing an ePUB that has been staged, by zipping it into the desired final file.
		/// </summary>
		/// <param name="destinationEpubPath"></param>
		public void FinishEpub(string destinationEpubPath)
		{
			var zip = new BloomZipFile(destinationEpubPath);
			foreach(var file in Directory.GetFiles(BookInStagingFolder))
				zip.AddTopLevelFile(file);
			foreach(var dir in Directory.GetDirectories(BookInStagingFolder))
				zip.AddDirectory(dir);
			zip.Save();
		}

		/// <summary>
		/// Try to embed the fonts we need.
		/// </summary>
		private void EmbedFonts()
		{
			// The 'false' here says to ignore all but the first font face in CSS's ordered lists of desired font faces.
			// If someone is publishing an Epub, they should have that font showing. For one thing, this makes it easier
			// for us to not embed fonts we don't want/ need.For another, it makes it less likely that an epub will look
			// different or have glyph errors when shown on a machine that does have that primary font.
			var fontsWanted = GetFontsUsed(Book.FolderPath, false);
			var fontFileFinder = new FontFileFinder();
			var filesToEmbed = fontsWanted.SelectMany(fontFileFinder.GetFilesForFont).ToArray();
			foreach(var file in filesToEmbed)
			{
				CopyFileToEpub(file);
			}
			var sb = new StringBuilder();
			foreach(var font in fontsWanted)
			{
				var group = fontFileFinder.GetGroupForFont(font);
				if(group != null)
				{
					AddFontFace(sb, font, "normal", "normal", group.Normal);
					// We are currently not including the other faces (nor their files...see FontFileFinder.GetFilesForFont().
					// BL-4202 contains a discussion of this. Basically,
					// - embedding them takes a good deal of extra space
					// - usually they do no good at all; it's nontrivial to figure out whether the book actually has any bold or italic
					// - even if the book has bold or italic, nearly all readers that display it at all do a reasonable
					//   job of synthesizing it from the normal face.
					//AddFontFace(sb, font, "bold", "normal", group.Bold);
					//AddFontFace(sb, font, "normal", "italic", group.Italic);
					//AddFontFace(sb, font, "bold", "italic", group.BoldItalic);
				}
			}
			RobustFile.WriteAllText(Path.Combine(_contentFolder, "fonts.css"), sb.ToString());
			_manifestItems.Add("fonts.css");
		}

		internal static void AddFontFace(StringBuilder sb, string name, string weight, string style, string path)
		{
			if(path == null)
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
		/// <param name="bookPath"></param>
		/// <param name="includeFallbackFonts"></param>
		/// <returns></returns>
		public static IEnumerable<string> GetFontsUsed(string bookPath, bool includeFallbackFonts)
		{
			var result = new HashSet<string>();
			// Css for styles are contained in the actual html
			foreach (var ss in Directory.EnumerateFiles(bookPath, "*.*").Where(f => f.EndsWith(".css") || f.EndsWith(".htm") || f.EndsWith(".html")))
			{
				var root = RobustFile.ReadAllText(ss, Encoding.UTF8);
				HtmlDom.FindFontsUsedInCss(root, result, includeFallbackFonts);
			}
			return result;
		}

		const double mmPerInch = 25.4;

		/// <summary>
		/// Typically pictures are given an absolute size in px, which looks right given
		/// the current absolute size of the page it is on. For an ePUB, a percent size
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
			foreach(XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(pageDom.RawDom.DocumentElement))
			{
				var parent = img.ParentNode.ParentNode as XmlElement;
				var mulitplier = 1.0;
				// For now we only attempt to adjust pictures contained in the marginBox.
				// To do better than this we will probably need to actually load the HTML into
				// a browser; even then it will be complex.
				while(parent != null && !HasClass(parent, "marginBox"))
				{
					// 'marginBox' is not yet the margin box...it is some parent div.
					// If it has an explicit percent width style, adjust for this.
					var styleAttr = parent.Attributes["style"];
					if(styleAttr != null)
					{
						var style = styleAttr.Value;
						var match = new Regex("width:\\s*(\\d+(\\.\\d+)?)%").Match(style);
						if(match.Success)
						{
							double percent;
							if(Double.TryParse(match.Groups[1].Value, out percent))
							{
								mulitplier *= percent/100;
							}
						}
					}
					parent = parent.ParentNode as XmlElement;
				}
				if(parent == null)
					continue;
				var page = parent.ParentNode as XmlElement;
				if(!HasClass(page, "bloom-page"))
					continue; // or return? marginBox should be child of page!
				if(firstTime)
				{
					var pageClass =
						HtmlDom.GetAttributeValue(page, "class")
							.Split()
							.FirstOrDefault(c => c.Contains("Portrait") || c.Contains("Landscape"));
					// This calculation unfortunately duplicates information from basePage.less.
					const int A4Width = 210;
					const int A4Height = 297;
					const double letterPortraitHeight = 11.0*mmPerInch;
					const double letterPortraitWidth = 8.5*mmPerInch;
					const double legalPortraitHeight = 14.0*mmPerInch;
					const double legalPortraitWidth = 8.5*mmPerInch;
					switch(pageClass)
					{
						case "A3Landscape":
							pageWidthMm = A4Width*2.0;
							break;
						case "A5Portrait":
							pageWidthMm = A4Height/2.0;
							break;
						case "A4Portrait":
							pageWidthMm = A4Width;
							break;
						case "A5Landscape":
							pageWidthMm = A4Width/2.0;
							break;
						case "A3Portrait":
						case "A4Landscape":
							pageWidthMm = A4Height;
							break;
						case "A6Portrait":
							pageWidthMm = A4Width/2.0;
							break;
						case "A6Landscape":
							pageWidthMm = A4Height/2.0;
							break;
						case "B5Portrait":
							pageWidthMm = 176;
							break;
						case "QuarterLetterPortrait":
							pageWidthMm = letterPortraitWidth/2.0;
							break;
						case "QuarterLetterLandscape":
						case "HalfLetterPortrait":
							pageWidthMm = letterPortraitHeight/2.0;
							break;
						case "HalfLetterLandscape":
						case "LetterPortrait":
							pageWidthMm = letterPortraitWidth;
							break;
						case "LetterLandscape":
							pageWidthMm = letterPortraitHeight;
							break;
						case "HalfLegalPortrait":
							pageWidthMm = legalPortraitHeight/2.0;
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
				if(ConvertStyleFromPxToPercent("width", pageWidthMm, mulitplier, ref imgStyle)) continue;

				// Now change height to auto, to preserve aspect ratio
				imgStyle = new Regex("height:\\s*\\d+px").Replace(imgStyle, "height:auto");
				if(!imgStyle.Contains("height"))
					imgStyle = "height:auto; " + imgStyle;

				// Similarly fix indent
				ConvertStyleFromPxToPercent("margin-left", pageWidthMm, mulitplier, ref imgStyle);

				img.SetAttribute("style", imgStyle);
			}
		}

		// Returns true if we don't find the expected style
		private static bool ConvertStyleFromPxToPercent(string stylename, double pageWidthMm, double multiplier,
			ref string imgStyle)
		{
			var match = new Regex("(.*" + stylename + ":\\s*)(\\d+)px(.*)").Match(imgStyle);
			if(!match.Success)
				return true;
			var widthPx = int.Parse(match.Groups[2].Value);
			var widthInch = widthPx/96.0; // in print a CSS px is exactly 1/96 inch
			const int marginBoxMarginMm = 40; // see basePage.less SetMarginBox.
			var marginBoxWidthInch = (pageWidthMm - marginBoxMarginMm)/mmPerInch;
			var parentBoxWidthInch = marginBoxWidthInch*multiplier;
				// parent box is smaller by net effect of parents with %width styles
			// 1/10 percent is close enough and more readable/testable than arbitrary precision; make a string with one decimal
			var newWidth = (Math.Round(widthInch/parentBoxWidthInch*1000)/10).ToString("F1");
			imgStyle = match.Groups[1] + newWidth + "%" + match.Groups[3];
			return false;
		}

		/// <summary>
		/// Remove stuff that we don't want displayed. Some e-readers don't obey display:none. Also, not shipping it saves space.
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveUnwantedContent(HtmlDom pageDom)
		{
			var pageElt = (XmlElement) pageDom.Body.FirstChild;

			// We need a real dom, with standard stylesheets, loaded into a browser, in order to let the
			// browser figure out what is visible. So we can easily match elements in the browser DOM
			// with the one we are manipulating, make sure they ALL have IDs.
			EnsureAllDivsHaveIds(pageElt);
			var normalDom = Book.GetHtmlDomWithJustOnePage(pageElt);
			AddEpubVisibilityStylesheetAndClass(normalDom);

			bool done = false;
			var dummy = _browser.Handle; // gets WebBrowser created along with handle
			_browser.WebBrowser.DocumentCompleted += (sender, args) => done = true;
			// just in case something goes wrong, keep program from deadlocking a few lines below.
			_browser.WebBrowser.NavigationError += (object sender, Gecko.Events.GeckoNavigationErrorEventArgs e) => done = true;
			_browser.Navigate(normalDom, source:"epub");
			while (!done)
			{
				Application.DoEvents();
				Application.RaiseIdle(new EventArgs()); // needed on Linux to avoid deadlock starving browser navigation
			}

			var toBeDeleted = new List<XmlElement>();
			// Deleting the elements in place during the foreach messes up the list and some things that should be deleted aren't
			// (See BL-5234). So we gather up the elements to be deleted and delete them afterwards.
			foreach (XmlElement elt in pageElt.SafeSelectNodes(".//div"))
			{
				if (!IsDisplayed(elt))
					toBeDeleted.Add(elt);
			}
			foreach (var elt in toBeDeleted)
			{
				elt.ParentNode.RemoveChild(elt);
			}

			// Remove any left-over bubbles
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes("//label"))
			{
				if(HasClass(elt, "bubble"))
					elt.ParentNode.RemoveChild(elt);
			}
			// Remove page labels and descriptions.  Also remove pages (or other div elements) that users have
			// marked invisible.  (The last mimics the effect of bookLayout/languageDisplay.less for editing
			// or PDF published books.)
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes("//div"))
						{
				if(HasClass(elt, "pageLabel"))
					elt.ParentNode.RemoveChild(elt);
				if(HasClass(elt, "pageDescription"))
					elt.ParentNode.RemoveChild(elt);
				// REVIEW: is this needed now with the new strategy?
				if (HasClass(elt, "bloom-editable") && HasClass(elt, "bloom-visibility-user-off"))
					elt.ParentNode.RemoveChild(elt);
			}
			// Our recordingmd5 attribute is not allowed
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes("//span[@recordingmd5]"))
			{
				elt.RemoveAttribute("recordingmd5");
			}
			// Users should not be able to edit content of published books
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes("//div[@contenteditable]"))
			{
				elt.RemoveAttribute("contenteditable");
			}
			RemoveTempIds(pageElt); // don't need temporary IDs any more.
		}

		/// <summary>
		/// The epub-visiblity class and the ebubVisibility.css stylesheet
		/// are only used to determine the visibility of items.
		/// They allow us to use the browser to determine visibility rules
		/// and then remove unwanted content from the dom completely since
		/// many eReaders do not properly handle display:none.
		/// </summary>
		/// <param name="dom"></param>
		private void AddEpubVisibilityStylesheetAndClass(HtmlDom dom)
		{
			var headNode = dom.SelectSingleNodeHonoringDefaultNS("/html/head");
			var epubVisibilityStylesheet = dom.RawDom.CreateElement("link");
			epubVisibilityStylesheet.SetAttribute("rel", "stylesheet");
			epubVisibilityStylesheet.SetAttribute("href", "epubVisibility.css");
			epubVisibilityStylesheet.SetAttribute("type", "text/css");
			headNode.AppendChild(epubVisibilityStylesheet);

			var bodyNode = dom.SelectSingleNodeHonoringDefaultNS("/html/body");
			var classAttribute = bodyNode.Attributes["class"];
			if (classAttribute != null)
				bodyNode.SetAttribute("class", classAttribute.Value + " epub-visibility");
			else
				bodyNode.SetAttribute("class", "epub-visibility");
		}

		private bool IsDisplayed(XmlElement elt)
		{
			var id = elt.Attributes["id"].Value;
			var display = _browser.RunJavaScript("getComputedStyle(document.getElementById('" + id + "'), null).display");
			return display != "none";
		}

		internal const string kTempIdMarker = "EpubTempIdXXYY";
		private void EnsureAllDivsHaveIds(XmlElement pageElt)
		{
			int count = 1;
			foreach(XmlElement elt in pageElt.SafeSelectNodes(".//div"))
			{
				if(elt.Attributes["id"] != null)
					continue;
				elt.SetAttribute("id", kTempIdMarker + count++);
			}
		}

		void RemoveTempIds(XmlElement pageElt)
		{
			foreach(XmlElement elt in pageElt.SafeSelectNodes(".//div"))
			{
				if (!elt.Attributes["id"].Value.StartsWith(kTempIdMarker))
					continue;
				elt.RemoveAttribute("id");
			}
		}

		bool HasClass(XmlElement elt, string className)
		{
			if(elt == null)
				return false;
			var classAttr = elt.Attributes["class"];
			if(classAttr == null)
				return false;
			return ((" " + classAttr.Value + " ").Contains(" " + className + " "));
		}

		private void RemoveRegularStylesheets(HtmlDom pageDom)
		{
			foreach(XmlElement link in pageDom.RawDom.SafeSelectNodes("//head/link").Cast<XmlElement>().ToArray())
			{
				var href = link.Attributes["href"];
				if(href != null && Path.GetFileName(href.Value).StartsWith("custom"))
					continue;
				if(href != null && Path.GetFileName(href.Value) == "settingsCollectionStyles.css")
					continue;
				link.ParentNode.RemoveChild(link);
			}
		}

		private void FixChangedFileNames(HtmlDom pageDom)
		{
			//NB: the original version of this was also concerned with hrefs. Since Bloom doesn't support making
			//links and there were no unit tests covering it, I decided to drop that support for now.

			foreach(XmlElement element in HtmlDom.SelectChildImgAndBackgroundImageElements(pageDom.RawDom.DocumentElement))
			{
				// Notice that we use only the path part of the url. For some unknown reason, some bloom books
				// (e.g., El Nino in the library) have a query in some image sources, and at least some ePUB readers
				// can't cope with it.
				var path = HtmlDom.GetImageElementUrl(element).PathOnly.NotEncoded;

				string modifiedPath;
				if(_mapChangedFileNames.TryGetValue(path, out modifiedPath))
				{
					path = modifiedPath;
				}
				// here we're either setting the same path, the same but stripped of a query, or a modified one.
				// In call cases, it really, truly is unencoded, so make sure the path doesn't do any more unencoding.
				HtmlDom.SetImageElementUrl(new ElementProxy(element), UrlPathString.CreateFromUnencodedString(path, true));
			}
		}

		// Copy a file to the appropriate place in the ePUB staging area, and note
		// that it is a necessary manifest item. Return the path of the copied file
		// (which may be different in various ways from the original; we suppress various dubious
		// characters and return something that doesn't depend on url decoding.
		private string CopyFileToEpub(string srcPath)
		{
			string existingFile;
			if(_mapSrcPathToDestFileName.TryGetValue(srcPath, out existingFile))
				return existingFile; // File already present, must be used more than once.
			string originalFileName;
			if(srcPath.StartsWith(Storage.FolderPath))
				originalFileName = srcPath.Substring(Storage.FolderPath.Length + 1).Replace("\\", "/");
					// allows keeping folder structure
			else
				originalFileName = Path.GetFileName(srcPath); // probably can't happen, but in case, put at root.
			// Validator warns against spaces in filenames. + and % and &<> are problematic because to get the real
			// file name it is necessary to use just the right decoding process. Some clients may do this
			// right but if we substitute them we can be sure things are fine.
			// I'm deliberately not using UrlPathString here because it doesn't correctly encode a lot of Ascii characters like =$&<>
			// which are technically not valid in hrefs
			var encoded =
				HttpUtility.UrlEncode(
					originalFileName.Replace("+", "_").Replace(" ", "_").Replace("&", "_").Replace("<", "_").Replace(">", "_"));
			var fileName = encoded.Replace("%", "_");
			var dstPath = Path.Combine(_contentFolder, fileName);
			// We deleted the root directory at the start, so if the file is already
			// there it is a clash, either multiple sources for files with the same name,
			// or produced by replacing spaces, or something. Come up with a similar unique name.
			for(int fix = 1; RobustFile.Exists(dstPath); fix++)
			{
				var fileNameWithoutExtension = Path.Combine(Path.GetDirectoryName(fileName),
					Path.GetFileNameWithoutExtension(fileName));
				fileName = Path.ChangeExtension(fileNameWithoutExtension + fix, Path.GetExtension(fileName));
				dstPath = Path.Combine(_contentFolder, fileName);
			}
			if(originalFileName != fileName)
				_mapChangedFileNames[originalFileName] = fileName;
			Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
			CopyFile(srcPath, dstPath);
			_manifestItems.Add(fileName);
			_mapSrcPathToDestFileName[srcPath] = fileName;
			return dstPath;
		}

		/// <summary>
		/// This supports testing without actually copying files.
		/// </summary>
		/// <param name="srcPath"></param>
		/// <param name="dstPath"></param>
		internal virtual void CopyFile(string srcPath, string dstPath)
		{
			RobustFile.Copy(srcPath, dstPath);
		}

		// The validator is (probably excessively) upset about IDs that start with numbers.
		// I don't think we actually use these IDs in the ePUB so maybe we should just remove them?
		private void FixIllegalIds(HtmlDom pageDom)
		{
			// Xpath results are things that have an id attribute, so MUST be XmlElements (though the signature
			// of SafeSelectNodes allows other XmlNode types).
			foreach(XmlElement elt in pageDom.RawDom.SafeSelectNodes("//*[@id]"))
			{
				var id = elt.Attributes["id"].Value;
				var first = id[0];
				if(first >= '0' && first <= '9')
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
			if(_firstContentPageItem == null)
				contentItem.Remove();
			else
				contentItem.Element(xhtml + "a").SetAttributeValue("href", _firstContentPageItem);
			if(_coverPage == _firstContentPageItem)
				coverItem.Remove();
			else
				coverItem.Element(xhtml + "a").SetAttributeValue("href", _coverPage);
			_navFileName = "nav.xhtml";
			var navPath = Path.Combine(_contentFolder, _navFileName);

			using(var writer = XmlWriter.Create(navPath))
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
			foreach(var elt in pageDom.RawDom.SafeSelectNodes("//script").Cast<XmlElement>().ToArray())
			{
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
			foreach(XmlElement elt in pageDom.RawDom.SafeSelectNodes("//*[@aria-describedby]"))
			{
				elt.RemoveAttribute("aria-describedby");
			}

			// Validator doesn't like empty lang attributes, and they don't convey anything useful, so remove.
			foreach(XmlElement elt in pageDom.RawDom.SafeSelectNodes("//*[@lang='']"))
			{
				elt.RemoveAttribute("lang");
			}
			// Validator doesn't like '*' as value of lang attributes, and they don't convey anything useful, so remove.
			foreach(XmlElement elt in pageDom.RawDom.SafeSelectNodes("//*[@lang='*']"))
			{
				elt.RemoveAttribute("lang");
			}
		}

		/// <summary>
		/// Remove anything that has class bloom-ui
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveBloomUiElements(HtmlDom pageDom)
		{
			foreach(var elt in pageDom.RawDom.SafeSelectNodes("//*[contains(@class,'bloom-ui')]").Cast<XmlElement>().ToList())
			{
				elt.ParentNode.RemoveChild(elt);
			}
		}

		/// <summary>
		/// Since file names often start with numbers, which ePUB validation won't allow for element IDs,
		/// stick an 'f' in front. Generally clean up file name to make a valid ID as similar as possible.
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		private string GetIdOfFile(string item)
		{
			string id;
			if(_mapItemToId.TryGetValue(item, out id))
				return id;
			id = ToValidXmlId(Path.GetFileNameWithoutExtension(item));
			var idOriginal = id;
			for(int i = 1; _idsUsed.Contains(id.ToLowerInvariant()); i++)
			{
				// Somehow we made a clash
				id = idOriginal + i;
			}
			_idsUsed.Add(id.ToLowerInvariant());
			_mapItemToId[item] = id;

			return id;
		}

		/// <summary>
		/// Given a filename, attempt to make a valid XML ID that is as similar as possible.
		/// - if it's OK don't change it
		/// - if it contains spaces remove them
		/// - if it starts with an invalid character add an initial 'f'
		/// - change other invalid characters to underlines
		/// We do this because ePUB technically uses XHTML and therefore follows XML rules.
		/// I doubt most readers care but validators do and we would like our ebooks to validate.
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		internal static string ToValidXmlId(string item)
		{
			string output = item.Replace(" ", "");
			// This conforms to http://www.w3.org/TR/REC-xml/#NT-Name except that we don't handle valid characters above FFFF.
			string validStartRanges =
				":A-Z_a-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u02FF\u0370-\u037D\u037F-\u1FFF\u200C-\u200D\u2070-\u218F\u2C00-\u2FEF\u3001-\uD7FF\uF900-\uFDCF\uFDF0-\uFFFD";
			string validChars = validStartRanges + "\\-.0-9\u00b7\u0300-\u036F\u203F-\u2040";
			output = Regex.Replace(output, "[^" + validChars + "]", "_");
			if(!new Regex("^[" + validStartRanges + "]").IsMatch(output))
				return "f" + output;
			return output;
		}

		private string GetMediaType(string item)
		{
			switch(Path.GetExtension(item).Substring(1))
			{
				case "xml": // Review
				case "xhtml":
					return "application/xhtml+xml";
				case "jpg":
				case "jpeg":
					return "image/jpeg";
				case "png":
					return "image/png";
				case "svg":
					return "image/svg+xml";		// https://www.w3.org/TR/SVG/intro.html
				case "css":
					return "text/css";
				case "woff":
					return "application/font-woff"; // http://stackoverflow.com/questions/2871655/proper-mime-type-for-fonts
				case "ttf":
				case "otf":
					// According to http://stackoverflow.com/questions/2871655/proper-mime-type-for-fonts, the proper
					// mime type for ttf fonts is now application/font-sfnt. However, this fails the Pagina Epubcheck
					// for epub 3.0.1, since the proper mime type for ttf was not put into the epub standard until 3.1.
					// See https://github.com/idpf/epub-revision/issues/443 and http://www.idpf.org/epub/31/spec/epub-changes.html#sec-epub31-cmt.
					// Since there are no plans to deprecate application/vnd.ms-opentype and it's unlikely to break
					// any reader (unlikely the reader even uses the type field), we're just sticking with that.
					return "application/vnd.ms-opentype"; // http://stackoverflow.com/questions/2871655/proper-mime-type-for-fonts
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
			if(_outerStagingFolder != null)
				_outerStagingFolder.Dispose();
		}

		public bool ReadyToSave()
		{
			// The same files have already been copied over to the staging area, but if we compare timestamps on the copied files
			// the comparison is unreliable (.wav files are larger and take longer to copy than the corresponding .mp3 files).
			// So we compare the original book's audio files to determine if any are missing -- BL-5437
			return PublishWithoutAudio || !AudioProcessor.IsAnyCompressedAudioMissing(_originalBook.FolderPath, Book.RawDom);
		}
	}
}
