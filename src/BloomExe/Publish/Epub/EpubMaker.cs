using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Bloom.Book;
using Bloom.ToPalaso;
using Bloom.web;
using Bloom.web.controllers;
using BloomTemp;
using L10NSharp;
#if __MonoCS__
using SIL.CommandLineProcessing;
#else
using NAudio.Wave;
#endif
using SIL.IO;
using SIL.Reporting;
using SIL.Text;
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
		public delegate EpubMaker Factory();// autofac uses this

		public const string kEPUBExportFolder = "ePUB_export";
		protected const string kEpubNamespace = "http://www.idpf.org/2007/ops";

		public const string kAudioFolder = "audio";
		public const string kImagesFolder = "images";
		public const string kCssFolder = "css";
		public const string kFontsFolder = "fonts";
		public const string kVideoFolder = "video";
		private Guid _thumbnailRequestId;
		private HashSet<string> _omittedPageLabels = new HashSet<string>();

		public static readonly string EpubExportRootFolder = Path.Combine(Path.GetTempPath(), kEPUBExportFolder);

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

		// The only reason this isn't just ../* is performance. We could change it.  It comes from the need to actually
		// remove any elements that the style rules would hide, becuase epub readers ignore visibility settings.
		private string kSelectThingsThatCanBeHidden = ".//div | .//img";

		// Keeps track of IDs that have been used in the manifest. These are generated to roughly match file
		// names, but the algorithm could pathologically produce duplicates, so we guard against this.
		private HashSet<string> _idsUsed = new HashSet<string>();
		// Keeps track of the ID actually generated for each resource. This is generally algorithmic,
		// but in case of duplicates the ID for an item might not be what the algorithm predicts.
		private Dictionary<string, string> _mapItemToId = new Dictionary<string, string>();
		// Keep track of the files we already copied to the ePUB, so we don't copy them again to a new name.
		private Dictionary<string, string> _mapSrcPathToDestFileName = new Dictionary<string, string>();
		// All the things (files) we need to list in the manifest
		private List<string> _manifestItems;
		// xhtml files with <script> element or onxxx= attributes
		private List<string> _scriptedItems;
		// xhtml files that refer to an svg image
		private List<string> _svgItems;
		// Duration of each item of type application/smil+xml (key is ID of item)
		Dictionary<string, TimeSpan> _pageDurations = new Dictionary<string, TimeSpan>();
		// The things we need to list in the 'spine'...defines the normal reading order of the book
		private List<string> _spineItems;
		// files that should be marked linear="no" in the spine.
		private HashSet<string> _nonLinearSpineItems;
		// Maps bloom page to a back link that needs to point to it once we know its name.
		// The back link must be later in the document, otherwise, it will get output incomplete.
		private Dictionary<XmlElement, List<Tuple<XmlElement, string>>> _pendingBackLinks;
		// Maps bloom-page elements to the file name we would like to use for that page.
		// Used for image description pages which we want out of the usual naming sequence.
		// One reason is that we want to know the file name in advance when we create the
		// link to the image description, and at that point we don't know what page number
		// it will have because we haven't finished evaluating which pages to omit as blank.
		private Dictionary<XmlElement, string> _desiredNameMap;
		// Counter for creating output page files.
		int _pageIndex;
		// Counter for referencing unrecognized "required singleton" (front/back matter?) pages.
		int _frontBackPage;
		// list of language ids to use for trying to localize names of front/back matter pages.
		string[] _langsForLocalization;
		// We track the first page that is actually content and link to it in our rather trivial table of contents.
		private string _firstContentPageItem;
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
		private PublishHelper _publishHelper = new PublishHelper();
		private BookServer _bookServer;
		// Ordered list of Table of Content entries.
		List<string> _tocList = new List<string>();
		// Ordered list of page navigation list item elements.
		private List<string> _pageList = new List<string>();
		// flag whether we've seen the first page with class numberedPage
		private bool _firstNumberedPageSeen;
		// image counter for creating id values
		private int _imgCount;

		/// <summary>
		/// Preparing a book for publication involves displaying it in a browser in order
		/// to accurately determine which elements are invisible and can be pruned from the
		/// published book.  This requires being on the UI thread, which may require having
		/// a Control available for calling Invoke() which will move execution to the UI
		/// thread.
		/// </summary>
		public Control ControlForInvoke
		{
			get { return _publishHelper.ControlForInvoke; }
			set { _publishHelper.ControlForInvoke = value; }
		}

		public bool AbortRequested
		{
			get { return _abortRequested; }
			set
			{
				_abortRequested = value;
				if (_abortRequested && _thumbNailer != null && _thumbnailRequestId != Guid.Empty)
				{
					_thumbNailer.CancelOrder(_thumbnailRequestId);
				}
			}
		}

		// Only make one audio file per page. This means if there are multiple recorded sentences on a page,
		// Epubmaker will squash them into one, compress it, and make smil entries with appropriate offsets.
		// This is closer to how the standard Moby Dick example is done, and at least one epub reader
		// (Simply Reading by Daisy) skips a lot of audio segments if we do one per sentence, but works
		// better with this approach. Nothing that we know of does less well, so for now, this is always
		// set true in real epub creation. Most of our unit tests predate it, however, and rather than
		// try to update all that would be affected I am leaving the default false.
		public bool OneAudioPerPage { get; set; }

		/// <summary>
		/// Set to true for unpaginated output. This is something of a misnomer...any better ideas?
		/// If it is true (which currently it always is), we remove the stylesheets for precise page layout
		/// and just output the text and pictures in order with a simple default stylesheet.
		/// Rather to my surprise, the result still has page breaks where expected, though the reader may
		/// add more if needed.
		/// </summary>
		public bool Unpaginated { get; set; }

		public BookInfo.HowToPublishImageDescriptions PublishImageDescriptions { get; set; }
		public bool RemoveFontSizes { get; set; }

		public EpubMaker(BookThumbNailer thumbNailer, BookServer bookServer)
		{
			_thumbNailer = thumbNailer;
			_bookServer = bookServer;
		}

		/// <summary>
		/// Generate all the files we will zip into the ePUB for the current book into the StagingFolder.
		/// It is required that the parent of the StagingFolder is a temporary folder into which we can
		/// copy the Readium stuff. This folder is deleted when the EpubMaker is disposed.
		/// </summary>
		public void StageEpub(WebSocketProgress progress, bool publishWithoutAudio = false)
		{
			PublishWithoutAudio = publishWithoutAudio;
			if (!string.IsNullOrEmpty(BookInStagingFolder))
				return; //already staged

			progress.Message("BuildingEPub", comment: "Shown in a progress box when Bloom is starting to create an ePUB",
				message: "Building ePUB");
			if (String.IsNullOrEmpty(Book.CollectionSettings.Language3Iso639Code))
				_langsForLocalization = new string[]
					{Book.CollectionSettings.Language1Iso639Code, Book.CollectionSettings.Language2Iso639Code};
			else
				_langsForLocalization = new string[]
				{
					Book.CollectionSettings.Language1Iso639Code, Book.CollectionSettings.Language2Iso639Code,
					Book.CollectionSettings.Language3Iso639Code
				};

			// robustly come up with a directory we can use, even if previously used directories are locked somehow
			Directory.CreateDirectory(EpubExportRootFolder); // this is ok if it already exists
			for (var i = 0; i < 20; i++)
			{
				var dir = Path.Combine(EpubExportRootFolder, i.ToString());

				if (Directory.Exists(dir))
				{
					// see if we can delete this old directory first
					if (!SIL.IO.RobustIO.DeleteDirectoryAndContents(dir))
					{
						progress.MessageWithoutLocalizing("could not remove " + dir);
						continue; // if not, let's change the target directory name and try again
					}
				}

				Directory.CreateDirectory(dir);
				_outerStagingFolder = TemporaryFolder.TrackExisting(dir);
				break;
			}

			var tempBookPath = Path.Combine(_outerStagingFolder.FolderPath, Path.GetFileName(Book.FolderPath));
			_originalBook = _book;
			if (_bookServer != null)
			{
				// It should only be null while running unit tests which don't create a physical file.
				_book = PublishHelper.MakeDeviceXmatterTempBook(_book.FolderPath, _bookServer, tempBookPath, _omittedPageLabels);
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
			_nonLinearSpineItems = new HashSet<string>();
			_pendingBackLinks = new Dictionary<XmlElement, List<Tuple<XmlElement, string>>>();
			_desiredNameMap = new Dictionary<XmlElement, string>();
			_scriptedItems = new List<string>();
			_svgItems = new List<string>();
			_firstContentPageItem = null;
			ISet<string> warningMessages = new HashSet<string>();

			HandleImageDescriptions(Book.OurHtmlDom);
			if (string.IsNullOrEmpty(SignLanguageApi.FfmpegProgram))
			{
				Logger.WriteEvent("Cannot find ffmpeg program while preparing videos for publishing.");
			}

			var pageLabelProgress = progress.WithL10NPrefix("TemplateBooks.PageLabel.");
			foreach (XmlElement pageElement in Book.GetPageElements())
			{
				// We could check for this in a few more places, but once per page seems enough in practice.
				if (AbortRequested)
					break;
				if (MakePageFile(pageElement, warningMessages))
				{
					var pageLabelEnglish = HtmlDom.GetNumberOrLabelOfPageWhereElementLives(pageElement);
					pageLabelProgress.Message(pageLabelEnglish, pageLabelEnglish);
				};
			}
			
			PublishHelper.SendBatchedWarningMessagesToProgress(warningMessages, progress);

			if (_omittedPageLabels.Any())
			{
				progress.Message("OmittedPages",
					"The following pages were removed because they are not supported in ePUBs:",
					MessageKind.Warning);
				foreach (var label in _omittedPageLabels.OrderBy(x => x))
				{
					progress.MessageWithoutLocalizing(label, MessageKind.Warning);
				}
			}

			string coverPageImageFile = "thumbnail-256.png";
			// This thumbnail is otherwise only made when uploading, so it may be out of date.
			// Just remake it every time.
			ApplicationException thumbNailException = null;
			try
			{
				_thumbnailRequestId = Guid.NewGuid();
				_thumbNailer.MakeThumbnailOfCover(Book, 256,_thumbnailRequestId);
				_thumbnailRequestId = Guid.Empty;
			}
			catch (ApplicationException e)
			{
				thumbNailException = e;
			}

			if (AbortRequested)
				return; // especially to avoid reporting problems making thumbnail, e.g., because aborted.

			var coverPageImagePath = Path.Combine(Book.FolderPath, coverPageImageFile);
			if (thumbNailException != null || !RobustFile.Exists(coverPageImagePath))
			{
				NonFatalProblem.Report(ModalIf.All, PassiveIf.All,
					"Bloom failed to make a high-quality cover page for your book (BL-3209)",
					"We will try to make the book anyway, but you may want to try again.",
					thumbNailException);

				coverPageImageFile = "thumbnail.png"; // Try a low-res image, which should always exist
				coverPageImagePath = Path.Combine(Book.FolderPath, coverPageImageFile);
				if (!RobustFile.Exists(coverPageImagePath))
				{
					// I don't think we can make an epub without a cover page so at this point we've had it.
					// I suppose we could recover without actually crashing but it doesn't seem worth it unless this
					// actually happens to real users.
					throw new FileNotFoundException("Could not find or create thumbnail for cover page (BL-3209)",
						coverPageImageFile);
				}
			}

			CopyFileToEpub(coverPageImagePath, true, true, kImagesFolder);

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

			MakeManifest(kImagesFolder + "/" + coverPageImageFile);

			foreach (var filename in Directory.EnumerateFiles(Path.Combine(_contentFolder, kImagesFolder), "*.*"))
			{
				if (Path.GetExtension(filename).ToLowerInvariant() == ".svg")
					PruneSvgFileOfCruft(filename);
			}
		}

		private void MakeManifest(string coverPageImageFile)
		{
			// content.opf: contains primarily the manifest, listing all the content files of the ePUB.
			var manifestPath = Path.Combine(_contentFolder, "content.opf");
			XNamespace opf = "http://www.idpf.org/2007/opf";
			var rootElt = new XElement(opf + "package",
				new XAttribute("version", "3.0"),
				new XAttribute("unique-identifier", "I" + Book.ID),
				new XAttribute("prefix", "a11y: http://www.idpf.org/epub/vocab/package/a11y/# epub32: https://w3c.github.io/publ-epub-revision/epub32/spec/epub-packages.html#"));
			// add metadata
			var dcNamespace = "http://purl.org/dc/elements/1.1/";
			var source = GetBookSource();
			XNamespace dc = dcNamespace;
			var bookMetaData = Book.BookInfo.MetaData;
			var licenseUrl = Book.GetLicenseMetadata().License.Url;
			if (string.IsNullOrEmpty(licenseUrl))
				licenseUrl = null; // allows us to use ?? below.
			var metadataElt = new XElement(opf + "metadata",
				new XAttribute(XNamespace.Xmlns + "dc", dcNamespace),
				new XAttribute(XNamespace.Xmlns + "opf", opf.NamespaceName),
				// attribute makes the namespace have a prefix, not be a default.
				new XElement(dc + "title", Book.Title),
				new XElement(dc + "language", Book.CollectionSettings.Language1Iso639Code),
				new XElement(dc + "identifier",
					new XAttribute("id", "I" + Book.ID), "bloomlibrary.org." + Book.ID),
					new XElement(dc + "source", source));
			if (!string.IsNullOrEmpty(bookMetaData.Author))
				metadataElt.Add(new XElement(dc + "creator", new XAttribute("id", "author"), bookMetaData.Author));
			// Per BL-6438 and a reply from the GDL (Global Digital Library), it is better to put
			// this in the field than to leave it blank when there is no URL available that specifies
			// the license. Unfortunately there is nowhere to put the RightsStatement that might
			// indicate more generous permissions (or attempt to restrict what the CC URL indicates).
			metadataElt.Add(new XElement(dc + "rights", licenseUrl ?? "All Rights Reserved"));
			AddSubjects(metadataElt, dc, opf);
			// Last modified datetime like 2012-03-20T11:37:00Z
			metadataElt.Add(new XElement(opf + "meta",
				new XAttribute("property", "dcterms:modified"), new FileInfo(Storage.FolderPath).LastWriteTimeUtc.ToString("s") + "Z"));
			if (!string.IsNullOrEmpty(bookMetaData.TypicalAgeRange))
				metadataElt.Add(new XElement(opf + "meta",
					new XAttribute("property", "schema:typicalAgeRange"), bookMetaData.TypicalAgeRange));
			metadataElt.Add(new XElement(opf + "meta",
				new XAttribute("property", "schema:numberOfPages"), Book.GetLastNumberedPageNumber().ToString(CultureInfo.InvariantCulture)));
			// dcterms:educationLevel is the closest authorized value for property that I've found for ReadingLevelDescription
			if (!string.IsNullOrEmpty(bookMetaData.ReadingLevelDescription))
				metadataElt.Add(new XElement(opf + "meta",
					new XAttribute("property", "dcterms:educationLevel"), bookMetaData.ReadingLevelDescription));
			AddAccessibilityMetadata(metadataElt, opf, bookMetaData);
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
				var properties = new StringBuilder();
				if (_scriptedItems.Contains(item))
					properties.Append("scripted");
				if (_svgItems.Contains(item))
					properties.Append(" svg");
				// This isn't very useful but satisfies a validator requirement until we think of
				// something better.
				if (item == _navFileName)
					properties.Append(" nav");
				if (item == coverPageImageFile)
					properties.Append(" cover-image");
				var propertiesValue = properties.ToString().Trim();
				if (propertiesValue.Length > 0)
					itemElt.SetAttributeValue("properties", propertiesValue);
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
				metadataElt.Add(new XElement(opf + "meta",
					new XAttribute("property", "media:active-class"),
					new XText("-epub-media-overlay-active")));
			}

			MakeSpine(opf, rootElt, manifestPath);
		}

		/// <summary>
		/// Add the (possibly several) Thema subject code(s) to the metadata.
		/// </summary>
		/// <param name="metadataElt"></param>
		/// <param name="dc"></param>
		/// <param name="opf"></param>
		private void AddSubjects(XElement metadataElt, XNamespace dc, XNamespace opf)
		{
			var subjects = Book.BookInfo.MetaData.Subjects;
			if (subjects == null)
				return;
			var i = 1;
			foreach (var subjectObj in subjects)
			{
				var id = string.Format("subject{0:00}", i);
				var code = subjectObj.value;
				var description = subjectObj.label;
				Debug.Assert(!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(description),
					"There has been a failure in the SubjectChooser code.");
				metadataElt.Add(
					new XElement(dc + "subject", new XAttribute("id", id), description),
					new XElement(opf + "meta", new XAttribute("refines", "#"+id), new XAttribute("property", "epub32:authority"), "https://ns.editeur.org/thema/"),
					new XElement(opf + "meta", new XAttribute("refines", "#"+id), new XAttribute("property", "epub32:term"), code)
				);
				++i;
			}
		}

		/// <summary>
		/// If the book has an ISBN we should use use that.  Otherwise the source should contain
		/// "as much information as possible about the source publication (e.g., the publisher,
		/// date, edition, and binding)". Since it probably doesn't have a publisher if it lacks
		/// an ISBN, let alone an edition, maybe all we can do is a date, something like
		/// "created from Bloom book on *date*".  Possibly noting the currently configured
		/// page size is relevant. Possibly at some point we will have a metadata input screen
		/// and we might put a version field in that and use it here.
		/// </summary>
		private string GetBookSource()
		{
			var isbnMulti = Book.GetDataItem("ISBN");
			if (!MultiTextBase.IsEmpty(isbnMulti))
			{
				var isbn = isbnMulti.GetBestAlternative("*");
				if (!String.IsNullOrEmpty(isbn))
					return "urn:isbn:" + isbn;
			}
			var layout = Book.GetLayout();
			var pageSize = layout.SizeAndOrientation.PageSizeName + (layout.SizeAndOrientation.IsLandScape ? " Landscape" : " Portrait");
			var date = DateTime.Now.ToString("yyyy-MM-dd");
			return String.Format("created from Bloom book on {0} with page size {1}", date, pageSize);
		}

		/// <summary>
		/// Add accessibility related metadata elements.
		/// </summary>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5895 and http://www.idpf.org/epub/a11y/techniques/#meta-003.
		/// https://www.w3.org/wiki/WebSchemas/Accessibility is also helpful.
		/// https://github.com/daisy/epub-revision-a11y/wiki/ePub-3.1-Accessibility--Proposal-To-Schema.org is also helpful, but
		/// I think the final version went for multiple &lt;meta accessMode="..."&gt; elements instead of lumping the attribute
		/// values together in one element.
		/// </remarks>
		private void AddAccessibilityMetadata(XElement metadataElt, XNamespace opf, BookMetaData metadata)
		{
			var hasImages = Book.HasImages();
			var hasVideo = Book.HasVideos();
			var hasFullAudio = Book.HasFullAudioCoverage();
			var hasAudio = Book.HasAudio(); // Check whether the book references any audio files that actually exist.

			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessMode"), "textual"));
			if (hasImages)
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessMode"), "visual"));
			if (hasAudio)
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessMode"), "auditory"));

			// Including everything like this is probably the best we can do programmatically without author input.
			// This assumes that images are neither essential nor sufficient.
			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessModeSufficient"), "textual"));
			if (hasImages || hasVideo)
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessModeSufficient"), "textual,visual"));
			if (hasAudio)
			{
				if (hasFullAudio)
					metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessModeSufficient"), "auditory"));
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessModeSufficient"), "textual,auditory"));
			}
			if ((hasImages || hasVideo) && hasAudio)
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessModeSufficient"), "textual,visual,auditory"));

			if (hasAudio)	// REVIEW: should this be hasFullAudio?
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "synchronizedAudioText"));
			// Note: largePrint description says "The property is not set if the font size can be increased. See displayTransformability."
			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "displayTransformability/resizeText"));
			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "printPageNumbers"));
			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "unlocked"));
			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "readingOrder"));
			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "tableOfContents"));

			if (!string.IsNullOrEmpty(metadata.A11yFeatures))
			{
				if(metadata.A11yFeatures.Contains("signLanguage"))
					metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "signLanguage"));
				if (metadata.A11yFeatures.Contains("alternativeText"))
					metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityFeature"), "alternativeText"));
			}

			// See http://www.idpf.org/epub/a11y/accessibility.html#sec-conf-reporting for the next two elements.
			if (!string.IsNullOrEmpty(metadata.A11yLevel))
			{
				metadataElt.Add(new XElement(opf + "link", new XAttribute("rel", "dcterms:conformsTo"),
					new XAttribute("href", "http://www.idpf.org/epub/a11y/accessibility-20170105.html#" + metadata.A11yLevel)));
			}
			if (!string.IsNullOrEmpty(metadata.A11yCertifier))
			{
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "a11y:certifiedBy"), metadata.A11yCertifier));
			}

			// Hazards section -- entirely 'manual' based on user entry (or lack thereof) in the dialog
			if (!string.IsNullOrEmpty(metadata.Hazards))
			{
				var hazards = metadata.Hazards.Split(',');
				// "none" is recommended instead of listing all 3 noXXXHazard values separately.
				// But since we don't know anything about sound, we can't use it.  (BL-6947)
				foreach (var hazard in hazards)
				{
					metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityHazard"), hazard));
				}
			}
			else
			{
				// report that we don't know anything.
				metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilityHazard"), "unknown"));
			}

			metadataElt.Add(new XElement(opf + "meta", new XAttribute("property", "schema:accessibilitySummary"),
				"How well the accessibility features work is up to the individual author."));	// What else to say?  not sure localization is possible.
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
			// These elements are marked as audio-sentence but we're not sure yet if the user actually recorded them yet
			var audioSentenceElements = HtmlDom.SelectAudioSentenceElements(pageDom.RawDom.DocumentElement).Cast<XmlElement>();

			// Now check if the audio recordings actually exist for them
			var audioSentenceElementsWithRecordedAudio =
				audioSentenceElements.Where(
					x => AudioProcessor.GetOrCreateCompressedAudio(Storage.FolderPath, x.Attributes["id"].Value) != null);
			if(!audioSentenceElementsWithRecordedAudio.Any())
				return;
			var overlayName = GetOverlayName(pageDocName);
			_manifestItems.Add(overlayName);
			string smilNamespace = "http://www.w3.org/ns/SMIL";
			XNamespace smil = smilNamespace;
			XNamespace epub = kEpubNamespace;
			var seq = new XElement(smil + "seq",
				new XAttribute("id", "id1"), // all <seq> I've seen have this, not sure whether necessary
				new XAttribute(epub + "textref", pageDocName),
				new XAttribute(epub + "type", "bodymatter chapter") // only type I've encountered
			);
			var root = new XElement(smil + "smil",
				new XAttribute("xmlns", smilNamespace),
				new XAttribute(XNamespace.Xmlns + "epub", kEpubNamespace),
				new XAttribute("version", "3.0"),
				new XElement(smil + "body",
					seq));
			int index = 1;
			TimeSpan pageDuration = new TimeSpan();
			string mergedAudioPath = null;
			if (OneAudioPerPage && audioSentenceElementsWithRecordedAudio.Count() > 1)
				mergedAudioPath = MergeAudioElements(audioSentenceElementsWithRecordedAudio);
			foreach(var audioSentenceElement in audioSentenceElementsWithRecordedAudio)
			{
				// These are going to be the same regardless of whether this audio sentence has sub-elements to highlight.
				var audioId = audioSentenceElement.Attributes["id"].Value;
				var path = AudioProcessor.GetOrCreateCompressedAudio(Storage.FolderPath, audioId);
				string epubPath = mergedAudioPath ?? CopyFileToEpub(path, subfolder: kAudioFolder);
				var newSrc = epubPath.Substring(_contentFolder.Length + 1).Replace('\\', '/');

				var highlightSegments = audioSentenceElement.SelectNodes(".//*[contains(concat(' ', normalize-space(@class), ' '),' bloom-highlightSegment ')]");
				if (highlightSegments.Count == 0)
				{
					// Traditional approach, no sub-elements.
					var dataDurationAttr = audioSentenceElement.Attributes["data-duration"];
					TimeSpan clipTimeSpan;
					if(dataDurationAttr != null)
					{
						// Make sure we parse "3.14159" properly since that's the form we'll see regardless of current locale.
						// (See http://issues.bloomlibrary.org/youtrack/issue/BL-4374.)
						clipTimeSpan = TimeSpan.FromSeconds(Double.Parse(dataDurationAttr.Value, System.Globalization.CultureInfo.InvariantCulture));
					}
					else
					{
						try
						{
#if __MonoCS__
							// ffmpeg can provide the length of the audio, but you have to strip it out of the command line output
							// See https://stackoverflow.com/a/33115316/7442826 or https://stackoverflow.com/a/53648234/7442826
							// The output (which is sent to stderr, not stdout) looks something like this:
							// "size=N/A time=00:03:36.13 bitrate=N/A speed= 432x    \rsize=N/A time=00:07:13.16 bitrate=N/A speed= 433x    \rsize=N/A time=00:08:42.97 bitrate=N/A speed= 434x"
							// When seen on the console screen interactively, it looks like a single line that is updated frequently.
							// A short file may have only one carriage-return separated section of output, while a very long file may
							// have more sections than this.
							var args = String.Format("-v quiet -stats -i \"{0}\" -f null -", path);
							var result = CommandLineRunner.Run("/usr/bin/ffmpeg", args, "", 20 * 10, new SIL.Progress.NullProgress());
							var output = result.ExitCode == 0 ? result.StandardError : null;
							string timeString = null;
							if (!string.IsNullOrEmpty(output))
							{
								var idxTime = output.LastIndexOf("time=");
								if (idxTime > 0)
									timeString = output.Substring(idxTime + 5, 11);
							}
							clipTimeSpan = TimeSpan.Parse(timeString, CultureInfo.InvariantCulture);
#else
							using (var reader = new Mp3FileReader(path))
								clipTimeSpan = reader.TotalTime;
#endif
						}
						catch
						{
							NonFatalProblem.Report(ModalIf.All, PassiveIf.All,
								"Bloom could not accurately determine the length of the audio file and will only make a very rough estimate.");
							// Crude estimate. In one sample, a 61K mp3 is 7s long.
							// So, multiply by 7 and divide by 61K to get seconds.
							// Then, to make a TimeSpan we need ticks, which are 0.1 microseconds,
							// hence the 10000000.
							clipTimeSpan = new TimeSpan(new FileInfo(path).Length * 7 * 10000000 / 61000);
						}
					}

					// Determine start time based on whether we have oneAudioPerPage (implies that we need to merge all the aduio files into one big file) or not
					TimeSpan clipStart = mergedAudioPath != null ? pageDuration : new TimeSpan(0);
					TimeSpan clipEnd = clipStart + clipTimeSpan;
					pageDuration += clipTimeSpan;

					AddEpubAudioParagraph(seq, smil, ref index, pageDocName, audioId, newSrc, clipStart, clipEnd);
				}
				else
				{
					// We have some subelements to worry about.

					string timingsStr = audioSentenceElement.GetAttribute("data-audiorecordingendtimes");    // These should be in seconds

					string[] timingFields = timingsStr.Split(' ');
					var segmentEndTimesSecs = new List<float>(timingFields.Length);
					for (int i = 0; i < timingFields.Length; ++i)
					{
						float time;
						if (!float.TryParse(timingFields[i], out time))
						{
							time = float.NaN;
						}
						segmentEndTimesSecs.Add(time);
					}

					// Keeps track of the duration of the current element including all sub-elements, but not any earlier elements on the page)
					// (In contrast with the page duration, which does include all earlier elements)
					TimeSpan currentElementDuration = new TimeSpan();
					double previousEndTimeSecs = 0;

					for (int i = 0; i < highlightSegments.Count && i < segmentEndTimesSecs.Count; ++i)
					{
						float clipEndTimeSecs = segmentEndTimesSecs[i];
						if (float.IsNaN(clipEndTimeSecs))
						{
							// Don't know how long this clip is -> don't know how long to highlight for -> just skip this segment and go to the next one.
							continue;
						}
						double clipDurationSecs = clipEndTimeSecs - previousEndTimeSecs;
						previousEndTimeSecs = clipEndTimeSecs;

						TimeSpan clipTimeSpan = TimeSpan.FromSeconds(clipDurationSecs);

						var segment = highlightSegments[i];
						string segmentId = segment.GetStringAttribute("id");

						// Determine start time based on whether we have oneAudioPerPage (implies that we need to merge all the aduio files into one big file) or not
						TimeSpan clipStart = (mergedAudioPath != null) ?  pageDuration + currentElementDuration : currentElementDuration;
						TimeSpan clipEnd = clipStart + clipTimeSpan;
						currentElementDuration += clipTimeSpan;

						AddEpubAudioParagraph(seq, smil, ref index, pageDocName, segmentId, newSrc, clipStart, clipEnd);
					}

					pageDuration += currentElementDuration;
				}
			}
			_pageDurations[GetIdOfFile(overlayName)] = pageDuration;
			var overlayPath = Path.Combine(_contentFolder, overlayName);
			using(var writer = XmlWriter.Create(overlayPath))
				root.WriteTo(writer);

		}

		/// <summary>
		/// Adds a narrated piece of text into an overlay sequence
		/// </summary>
		/// <param name="seq">The element to add to</param>
		/// <param name="smil">The namespace</param>
		/// <param name="index">Pass by Reference. The 1-based index of the element. This function will increment it after adding the new element</param>
		/// <param name="pageDocName">The name of the page, e.g. 1.xhtml, which will be used as the filename of a URL</param>
		/// <param name="segmentId">The ID (as in the ID used in the Named Anchor fragment of a URL) of the TEXT to be highlighted (as opposed to the ID of the audio to be played)</param>
		/// <param name="newSrc">The source of the AUDIO file</param>
		/// <param name="clipStartSecs">The start time (in seconds) of this segment within the audio file</param>
		/// <param name="clipEndSecs">The end time (in seconds) of this segment within the audio file</param>
		private void AddEpubAudioParagraph(XElement seq, XNamespace smil, ref int index, string pageDocName, string segmentId, string newSrc, TimeSpan clipStartSecs, TimeSpan clipEndSecs)
		{
			seq.Add(new XElement(smil + "par",
				new XAttribute("id", "s" + index++),
				new XElement(smil + "text",
					new XAttribute("src", pageDocName + "#" + segmentId)),
				new XElement(smil + "audio",
					new XAttribute("src", newSrc),
					new XAttribute("clipBegin", clipStartSecs.ToString(@"h\:mm\:ss\.fff")),
					new XAttribute("clipEnd", clipEndSecs.ToString(@"h\:mm\:ss\.fff")))));
		}

		/// <summary>
		/// Merge the audio files corresponding to the specified elements. Returns the path to the merged MP3 if all is well, null if
		/// we somehow failed to merge.
		/// </summary>
		private string MergeAudioElements(IEnumerable<XmlElement> elementsWithAudio)
		{
			var mergeFiles =
				elementsWithAudio
					.Select(s => AudioProcessor.GetOrCreateCompressedAudio(Storage.FolderPath, s.Attributes["id"]?.Value))
					.Where(s => !string.IsNullOrEmpty(s));
			Directory.CreateDirectory(Path.Combine(_contentFolder, kAudioFolder));
			var combinedAudioPath = Path.Combine(_contentFolder, kAudioFolder, "page" + _pageIndex + ".mp3");
			var errorMessage = AudioProcessor.MergeAudioFiles(mergeFiles, combinedAudioPath);
			if (errorMessage == null)
			{
				_manifestItems.Add(kAudioFolder + "/" + Path.GetFileName(combinedAudioPath));
				return combinedAudioPath;
			}
			Logger.WriteEvent("Failed to merge audio files for page " + _pageIndex + " " + errorMessage);
			// and we will do it the old way. Works for some readers.
			return null;
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
			if(this.Book.CollectionSettings.Language1.IsRightToLeft)
			{
				spineElt.SetAttributeValue("page-progression-direction","rtl");
			}
			rootElt.Add(spineElt);
			foreach(var item in _spineItems)
			{
				var itemElt = new XElement(opf + "itemref",
					new XAttribute("idref", GetIdOfFile(item)));
				spineElt.Add(itemElt);
				if (_nonLinearSpineItems.Contains(item))
					itemElt.SetAttributeValue("linear", "no");

			}
			using(var writer = XmlWriter.Create(manifestPath))
				rootElt.WriteTo(writer);
		}

		Dictionary<string, string> _directionSettings = new Dictionary<string, string>();
		private bool _abortRequested;

		private void CopyStyleSheets(HtmlDom pageDom)
		{
			foreach(XmlElement link in pageDom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var href = Path.Combine(Book.FolderPath, link.GetAttribute("href"));
				var name = Path.GetFileName(href);
				if(name == "fonts.css")
					continue; // generated file for this book, already copied to output.
				string path;
				if (name == "customCollectionStyles.css" || name == "defaultLangStyles.css")
				{
					// These files should be in the book's folder, not in some arbitrary place in our search path.
					path = Path.Combine(_originalBook.FolderPath, name);
					// It's OK not to find these.
					if (!File.Exists(path))
					{
						continue;
					}
					else if (name == "defaultLangStyles.css")
					{
						ProcessSettingsForTextDirectionality(path);
					}
				}
				else
				{
					var fl = Storage.GetFileLocator();
					path = fl.LocateFileWithThrow(name);
				}
				CopyFileToEpub(path, subfolder:kCssFolder);
			}
		}

		private void ProcessSettingsForTextDirectionality(string path)
		{
			// We have to deal with the direction: settings since EPUB doesn't like them.
			// See https://issues.bloomlibrary.org/youtrack/issue/BL-6705.
			_directionSettings.Clear();
			// REVIEW: is BODY always ltr, or should it be the same as Language1?  Having BODY be ltr for a book in Arabic or Hebrew
			// seems counterintuitive even if all the div elements are marked correctly.
			_directionSettings.Add("body", "ltr");
			_directionSettings.Add(this.Book.CollectionSettings.Language1Iso639Code, this.Book.CollectionSettings.Language1.IsRightToLeft ? "rtl" : "ltr");
			if (!_directionSettings.ContainsKey(this.Book.CollectionSettings.Language2Iso639Code))
				_directionSettings.Add(this.Book.CollectionSettings.Language2Iso639Code, this.Book.CollectionSettings.Language2.IsRightToLeft ? "rtl" : "ltr");
			if (!String.IsNullOrEmpty(this.Book.CollectionSettings.Language3Iso639Code) && !_directionSettings.ContainsKey(this.Book.CollectionSettings.Language3Iso639Code))
				_directionSettings.Add(this.Book.CollectionSettings.Language3Iso639Code, this.Book.CollectionSettings.Language3.IsRightToLeft ? "rtl" : "ltr");
		}

		private void SetDirAttributes(HtmlDom pageDom)
		{
			string bodyDir;
			if (!_directionSettings.TryGetValue("body", out bodyDir))
				return;
			bodyDir = bodyDir.ToLowerInvariant();
			foreach (XmlElement body in pageDom.SafeSelectNodes("//body"))
			{
				body.SetAttribute ("dir", bodyDir);
				break;	// only one body element anyway
			}
			var allSame = true;
			foreach (var dir in _directionSettings.Values)
			{
				if (dir != bodyDir)
				{
					allSame = false;
					break;
				}
			}
			if (allSame)
				return;
			foreach (var key in _directionSettings.Keys)
			{
				if (key == "body")
					continue;
				var dir = _directionSettings[key];
				foreach (XmlElement div in pageDom.SafeSelectNodes("//div[@lang='"+key+"']"))
					div.SetAttribute("dir", dir);
			}
		}

		/// <summary>
		/// If the page is not blank, make the page file.
		/// If the page is blank, return without writing anything to disk.
		/// </summary>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4288 for discussion of blank pages.
		/// </remarks>
		private bool MakePageFile(XmlElement pageElement, ISet<string> warningMessages)
		{
			// nonprinting pages (e.g., old-style comprehension questions) are omitted for now
			// interactive pages (e.g., new-style quiz pages) are also omitted. We're drastically
			// simplifying the layout of epub pages, and omitting most style sheets, and not including
			// javascript, so even if the player supports all those things perfectly, they're not likely
			// to work properly.
			if ((pageElement.Attributes["class"]?.Value?.Contains("bloom-nonprinting") ?? false)
			    || (pageElement.Attributes["class"]?.Value?.Contains("bloom-interactive-page") ?? false))
			{
				PublishHelper.CollectPageLabel(pageElement, _omittedPageLabels);
				return false;
			}

			var pageDom = GetEpubFriendlyHtmlDomForPage(pageElement);

			// Note, the following stylsheet stuff can be quite bewildering...
			// Testing shows that these stylesheets are not actually used
			// in PublishHelper.RemoveUnwantedContent(), which falls back to the stylesheets in place for the book, which in turn,
			// in unit tests, is backed by a simple mocked BookStorage which doesn't have the stylesheet smarts. Sigh.

			pageDom.RemoveModeStyleSheets();
			if (Unpaginated)
			{
				// Do not add any stylesheets that are not originally written specifically for ePUB use.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-5495.
				RemoveRegularStylesheets(pageDom);
				pageDom.AddStyleSheet(Storage.GetFileLocator().LocateFileWithThrow(@"baseEPUB.css").ToLocalhost());
				var brandingPath = Storage.GetFileLocator().LocateOptionalFile(@"branding.css");
				if (!string.IsNullOrEmpty(brandingPath))
				{
					pageDom.AddStyleSheet(brandingPath.ToLocalhost());
				}
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

			// Remove stuff that we don't want displayed. Some e-readers don't obey display:none. Also, not shipping it saves space.
			_publishHelper.RemoveUnwantedContent(pageDom, this.Book, true, warningMessages, this);

			pageDom.SortStyleSheetLinks();
			pageDom.AddPublishClassToBody();
			if (RemoveFontSizes)
			{
				DoRemoveFontSizes(pageDom);
			}

			MakeCssLinksAppropriateForEpub(pageDom);
			RemoveBloomUiElements(pageDom);
			RemoveSpuriousLinks(pageDom);
			RemoveScripts(pageDom);
			FixIllegalIds(pageDom);
			FixPictureSizes(pageDom);

			// Check for a blank page before storing any data from this page or copying any files on disk.
			if (IsBlankPage(pageDom.RawDom.DocumentElement))
				return false;

			// Do this as the last cleanup step, since other things may be looking for these elements
			// expecting them to be divs.
			ConvertHeadingStylesToHeadingElements(pageDom);

			// Since we only allow one htm file in a book folder, I don't think there is any
			// way this name can clash with anything else.
			++_pageIndex;
			var pageDocName = _pageIndex + ".xhtml";
			string preferedPageName;
			if (_desiredNameMap.TryGetValue(pageElement, out preferedPageName))
				pageDocName = preferedPageName;

			CopyImages(pageDom);
			CopyVideos(pageDom);

			AddEpubNamespace(pageDom);
			AddPageBreakSpan(pageDom, pageDocName);
			AddEpubTypeAttributes(pageDom);
			AddAriaAccessibilityMarkup(pageDom);
			CheckForEpubProperties(pageDom, pageDocName);

			_manifestItems.Add(pageDocName);
			_spineItems.Add(pageDocName);
			if(!PublishWithoutAudio)
				AddAudioOverlay(pageDom, pageDocName);

			StoreTableOfContentInfo(pageElement, pageDocName);

			// for now, at least, all Bloom book pages currently have the same stylesheets, so we only neeed
			//to copy those stylesheets on the first page
			if (_pageIndex == 1)
				CopyStyleSheets(pageDom);
			// But we always need to adjust the stylesheets to be in the css folder
			foreach(XmlElement link in pageDom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var name = Path.GetFileName(link.GetAttribute("href"));
				link.SetAttribute("href", kCssFolder+"/" + name);
			}
			pageDom.AddStyleSheet(kCssFolder+"/" + "fonts.css"); // enhance: could omit if we don't embed any

			// EPUB doesn't like direction: settings in CSS, so we need to explicitly set dir= attributes.
			if (_directionSettings.Count > 0)
				SetDirAttributes(pageDom);

			// ePUB validator requires HTML to use namespace. Do this last to avoid (possibly?) messing up our xpaths.
			pageDom.RawDom.DocumentElement.SetAttribute("xmlns", "http://www.w3.org/1999/xhtml");
			RobustFile.WriteAllText(Path.Combine(_contentFolder, pageDocName), pageDom.RawDom.OuterXml);
			List<Tuple<XmlElement, string>> pendingBackLinks;
			if (_pendingBackLinks.TryGetValue(pageElement, out pendingBackLinks))
			{
				foreach (var pendingBackLink in pendingBackLinks)
					pendingBackLink.Item1.SetAttribute("href", pageDocName + "#" + pendingBackLink.Item2);
			}

			return true;
		}

		/// <summary>
		/// Store the table of content information (if any) for this page.
		/// </summary>
		/// <param name="pageElement">a &lt;div&gt; with a class attribute that contains page level formatting information</param>
		/// <param name="pageDocName">filename of the page's html file</param>
		private void StoreTableOfContentInfo(XmlElement pageElement, string pageDocName)
		{
			var pageClasses = pageElement.GetAttribute("class").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string pageLabel = null;
			if (pageClasses.Contains("bloom-frontMatter") || pageClasses.Contains ("bloom-backMatter"))
			{
				pageLabel = GetXMatterPageName(pageClasses);
			}
			else if (_firstContentPageItem == null)
			{
				// Record the first non-blank page that isn't front-matter as the first content page.
				_firstContentPageItem = pageDocName;
				string languageIdUsed;
				pageLabel = LocalizationManager.GetString("PublishTab.Epub.PageLabel.Content", "Content", "label for the book content in the ePUB's Table of Contents",
					_langsForLocalization, out languageIdUsed);
			}
			if (!String.IsNullOrEmpty(pageLabel))
				_tocList.Add(String.Format("<li><a href=\"{0}\">{1}</a></li>", pageDocName, pageLabel));
		}

		private void ConvertHeadingStylesToHeadingElements(HtmlDom pageDom)
		{
			foreach (var div in pageDom.SafeSelectNodes(".//div[contains(@class, 'Heading')]").Cast<XmlElement>().ToArray())
			{
				var classes = div.Attributes["class"].Value;
				var regex = new Regex(@"\bHeading([0-9])\b");
				var match = regex.Match(classes);
				if (!match.Success)
					continue; // not a precisely matching class
				var level = match.Groups[1]; // the number
				var tag = "h" + level;
				var replacement = div.OwnerDocument.CreateElement(tag);
				foreach (var attr in div.Attributes.Cast<XmlAttribute>().ToArray())
				{
					replacement.Attributes.Append(attr);
				}

				var paragraphSeen = false;
				foreach (var child in div.ChildNodes.Cast<XmlNode>().ToArray())
				{
					// paragraph elements are not valid inside heading elements, so we need to
					// remove any paragraph markup by copying only the content, not the markup
					if (child.Name == "p")
					{
						// If there are multiple paragraphs, the paragraph breaks disappear with
						// the paragraph markup, so insert line breaks instead.
						if (paragraphSeen)
							replacement.AppendChild(div.OwnerDocument.CreateElement("br"));
						foreach (var grandchild in child.ChildNodes.Cast<XmlNode>().ToArray())
							replacement.AppendChild(grandchild);
						paragraphSeen = true;
					}
					else
					{
						replacement.AppendChild(child);
					}
				}

				div.ParentNode.ReplaceChild(replacement, div);
			}
		}

		private void DoRemoveFontSizes(HtmlDom pageDom)
		{
			// Find the special styles element which contains the user-defined styles.
			// These are the only elements I can find that set explicit font sizes.
			// A few of our css rules apply percentage sizes, but that should be OK.
			var userStyles = pageDom.Head.ChildNodes.Cast<XmlNode>()
				.Where(x => x is XmlElement && x.Attributes["title"]?.Value == "userModifiedStyles").FirstOrDefault();
			if (userStyles != null)
			{
				var userStylesCData = userStyles.ChildNodes.Cast<XmlNode>().Where(x => x is XmlCDataSection).FirstOrDefault();
				if (userStylesCData != null)
				{
					var regex = new Regex(@"font-size\s*:[^;}]*;?");
					userStylesCData.InnerText = regex.Replace(userStylesCData.InnerText, "").Replace("  ", " ");
				}
			}
		}

		public static bool IsBranding(XmlElement element)
		{
			if (element == null)
			{
				return false;
			}

			if (PublishHelper.HasClass(element, "branding"))
			{
				return true;
			}

			// For example: <div data-book="credits-page-branding-bottom-html" lang="*"></div>
			while (element != null)
			{
				string value = element.GetAttribute("data-book");
				if (value.Contains("branding"))
				{
					return true;
				}
				else
				{
					XmlNode parentNode = element.ParentNode;	// Might be an XmlDocument up the chain
					if (parentNode is XmlElement)
					{
						element = (XmlElement)parentNode;
					}
					else
					{
						break;
					}
				}
			}

			return false;
		}

		// This method is called by reflection in some tests
		private void HandleImageDescriptions(HtmlDom bookDom)
		{
			// Set img alt attributes to the description, or erase them if no description (BL-6035)
			foreach (var img in bookDom.Body.SelectNodes("//img[@src]").Cast<XmlElement> ())
			{
				bool isLicense = PublishHelper.HasClass(img, "licenseImage");
				bool isBranding = IsBranding(img);
				if (isLicense || isBranding)
				{
					string newAltText = "";

					if (isLicense)
					{
						newAltText = "Image representing the license of this book";
					}
					else if (isBranding)
					{
						// Check if it's using the placeholder alt text... which isn't actually meaningful and we don't want in the ePub version for accessibility.
						string currentAltText = img.GetAttribute("alt");
						if (!HtmlDom.IsPlaceholderImageAltText(img))
						{
							// It is using a custom-specified one. Go ahead and keep it.
							newAltText = currentAltText;
						}
						else
						{
							// Placeholder or missing alt text.  Replace it with the ePub version of the placeholder alt text
							newAltText = "Logo of the book sponsors"; // Alternatively, it's OK to also put in "" to signal no accessibility need
						}
					}
					img.SetAttribute("alt", newAltText);
					img.SetAttribute("role", "presentation"); // tells accessibility tools to ignore it and makes DAISY checker happy
					continue;
				}
				if ((img.ParentNode as XmlElement).GetAttribute("aria-hidden") == "true")
				{
					img.SetAttribute("role", "presentation"); // may not be needed, but doesn't hurt anything.
					continue;
				}
				var desc = img.SelectSingleNode("following-sibling::div[contains(@class, 'bloom-imageDescription')]/div[contains(@class, 'bloom-content1')]") as XmlElement;
				var text = desc?.InnerText.Trim();
				if (!string.IsNullOrEmpty(text))
				{
					img.SetAttribute("alt", text);
					continue;
				}
				img.RemoveAttribute("alt");    // signal missing accessibility information
			}
			// Put the image descriptions on the page following the images.
			if (PublishImageDescriptions == BookInfo.HowToPublishImageDescriptions.OnPage)
			{
				var imageDescriptions = bookDom.SafeSelectNodes("//div[contains(@class, 'bloom-imageDescription')]");
				foreach (XmlElement description in imageDescriptions)
				{
					var activeDescriptions = description.SafeSelectNodes("div[contains(@class, 'bloom-visibility-code-on')]");
					if (activeDescriptions.Count == 0)
						continue;
					// Now that we need multiple asides (BL-6314), I'm putting them in a separate div.
					// Insert the div after the image container, thus not interfering with the
					// reader's placement of the image itself.
					var asideContainer = description.OwnerDocument.CreateElement("div");
					asideContainer.SetAttribute("class", "asideContainer");
					description.ParentNode.ParentNode.InsertAfter(asideContainer, description.ParentNode);
					foreach (XmlNode activeDescription in activeDescriptions)
					{
						// If the inner xml is only an audioSentence recording, it will still create the aside.
						// But if there really isn't anything here, skip it.
						if (string.IsNullOrWhiteSpace(activeDescription?.InnerXml))
							continue;
						var aside = description.OwnerDocument.CreateElement("aside");
						// We want to preserve all the inner markup, especially the audio spans.
						aside.InnerXml = activeDescription.InnerXml;
						// We also need the language attribute to get the style to work right.
						var langAttr = activeDescription.Attributes["lang"];
						if (langAttr != null)
						{
							aside.SetAttribute("lang", langAttr.Value);
						}

						// As well as potentially being used by stylesheets, 'imageDescription' is used by the
						// AddAriaAccessibilityMarkup to identify the aside as an image description
						// (and tie the image to it). 'ImageDescriptionEdit-style' works with the 'lang' attribute
						// to style the aside in the ePUB.
						aside.SetAttribute("class", "imageDescription ImageDescriptionEdit-style");
						asideContainer.AppendChild(aside);
					}
					// Delete the original image description since its content has been copied into the aside we
					// just made, and even if we keep it hidden, the player may play the audio for both. (BL-7308)
					description.ParentNode.RemoveChild(description);
				}
			}
			// code to handle HowToPublishImageDescriptions.Links was removed from Bloom 4.6 on June 28, 2019.
			// If HowToPublishImageDescriptions.None, leave alone, and they will be invisible, but not deleted.
			// This allows the image description audio to play even when the description isn't displayed in
			// written form (BL-7237).  (For broken readers, the text might still be visible, but then it's
			// likely the audio wouldn't play anyway.)
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
			foreach (XmlElement vid in HtmlDom.SelectChildVideoElements(pageElement).Cast<XmlElement>())
			{
				var src = FindVideoFileIfPossible(vid);
				if (!String.IsNullOrEmpty(src))
				{
					var srcPath = Path.Combine(Book.FolderPath, src);
					if (RobustFile.Exists(srcPath))
						return false;
				}
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
					var isCoverImage = img.SafeSelectNodes("parent::div[contains(@class, 'bloom-imageContainer')]/ancestor::div[contains(concat(' ',@class,' '),' coverColor ')]").Cast<XmlElement>().Count() != 0;
					var dstPath = CopyFileToEpub(srcPath, limitImageDimensions: true, needTransparentBackground: isCoverImage, subfolder: kImagesFolder);
					var newSrc = dstPath.Substring(_contentFolder.Length+1).Replace('\\','/');
					HtmlDom.SetImageElementUrl(new ElementProxy(img), UrlPathString.CreateFromUnencodedString(newSrc, true), false);
				}
			}
		}

		private void CopyVideos(HtmlDom pageDom)
		{
			foreach (XmlElement videoContainerElement in HtmlDom.SelectChildVideoElements(pageDom.RawDom.DocumentElement).Cast<XmlElement>())
			{
				var trimmedFilePath = SignLanguageApi.PrepareVideoForPublishing(videoContainerElement, Book.FolderPath, videoControls: true);
				if (string.IsNullOrEmpty(trimmedFilePath))
					continue;
				var dstPath = CopyFileToEpub(trimmedFilePath, subfolder:kVideoFolder);
				var newSrc = dstPath.Substring(_contentFolder.Length+1).Replace('\\','/');
				HtmlDom.SetVideoElementUrl(new ElementProxy(videoContainerElement), UrlPathString.CreateFromUnencodedString(newSrc, true), false);
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
			if (url == null || String.IsNullOrEmpty(url.PathOnly.NotEncoded))
				return null; // very weird, but all we can do is ignore it.
			// Notice that we use only the path part of the url. For some unknown reason, some bloom books
			// (e.g., El Nino in the library) have a query in some image sources, and at least some ePUB readers
			// can't cope with it.
			var filename = url.PathOnly.NotEncoded;
			if (String.IsNullOrEmpty(filename))
				return null;
			// Images are always directly in the folder
			var srcPath = Path.Combine(Book.FolderPath, filename);
			if (RobustFile.Exists(srcPath))
				return srcPath;
			return String.Empty;
		}

		private string FindVideoFileIfPossible(XmlElement vid)
		{
			var url = HtmlDom.GetVideoElementUrl(vid);
			if (url == null || String.IsNullOrEmpty(url.PathOnly.NotEncoded))
				return null;
			return url.PathOnly.NotEncoded;
		}

		/// <summary>
		/// Check whether the desired branding image file exists.  If it does, return its full path.
		/// Otherwise, return String.Empty;
		/// </summary>
//		private string FindBrandingImageIfPossible(string urlPath)
//		{
//			var idx = urlPath.IndexOf('?');
//			if (idx > 0)
//			{
//				var query = urlPath.Substring(idx + 1);
//				var parsedQuery = HttpUtility.ParseQueryString(query);
//				var file = parsedQuery["id"];
//				if (!String.IsNullOrEmpty(file))
//				{
//					var path = Bloom.Api.BrandingApi.FindBrandingImageFileIfPossible(Book.CollectionSettings.BrandingProjectKey, file, Book.GetLayout());
//					if (!String.IsNullOrEmpty(path) && RobustFile.Exists(path))
//						return path;
//				}
//			}
//			return String.Empty;
//		}

		private void AddEpubNamespace(HtmlDom pageDom)
		{
			pageDom.RawDom.DocumentElement.SetAttribute("xmlns:epub", kEpubNamespace);
		}

		// The Item2 values are localized for displaying to the user.  Unfortunately, localizing to
		// the national language is probably all we can achieve at the moment, and that's rather
		// iffy at best.  (Since our localization is for the UI language, not the book language.)
		// The Item3 values are not localized.
		static List<Tuple<string,string,string>> classLabelIdList = new List<Tuple<string,string,string>>
		{
			// Item1 = HTML class, Item2 = Label displayed to user, Item3 = Internal HTML id
			Tuple.Create("credits",          "Credits Page",       "pgCreditsPage"),
			Tuple.Create("frontCover",       "Front Cover",        "pgFrontCover"),
			Tuple.Create("insideBackCover",  "Inside Back Cover",  "pgInsideBackCover"),
			Tuple.Create("insideFrontCover", "Inside Front Cover", "pgInsideFrontCover"),
			Tuple.Create("outsideBackCover", "Outside Back Cover", "pgOutsideBackCover"),
			Tuple.Create("theEndPage",       "The End",            "pgTheEnd"),
			Tuple.Create("titlePage",        "Title Page",         "pgTitlePage")
		};
		private void AddPageBreakSpan(HtmlDom pageDom, string pageDocName)
		{
			var body = pageDom.Body;
			var div = body.FirstChild;
			var divClass = div.GetStringAttribute("class");
			var classes = divClass.Split(' ');
			System.Diagnostics.Debug.Assert(classes.Contains("bloom-page"));
			var page = String.Empty;
			var id = String.Empty;
			if (classes.Contains("numberedPage"))
			{
				// This page number value is not localized.
				page = div.GetStringAttribute("data-page-number");
			}
			else if (div.GetOptionalStringAttribute("data-page", "") == "required singleton")
			{
				page = GetXMatterPageName(classes);
				var found = classLabelIdList.Find(x => classes.Contains(x.Item1));
				// Note that GetXMatterPageName uses the same data, so the id will correspond to
				// the right page name regardless of localization.  If nothing is found, then the
				// value returned by GetXMatterPageName will not be localized (x1, x2, ...)
				if (found != null)
					id = found.Item3;
			}
			if (!String.IsNullOrEmpty(page))
			{
				if (String.IsNullOrEmpty(id))
				{
					// In this situation, 'page' will not be localized (and probably won't have spaces either).
					id = "pg" + page.Replace(" ", "");
				}
				var newChild = pageDom.RawDom.CreateElement("span");
				newChild.SetAttribute("type", kEpubNamespace, "pagebreak");
				newChild.SetAttribute("role", "doc-pagebreak");
				newChild.SetAttribute("id", id);
				newChild.SetAttribute("aria-label", page);
				newChild.InnerXml = page;
				div.InsertBefore(newChild, div.FirstChild);
				// We don't generally want to display the numbers for the page breaks.
				// REVIEW: should this be a user-settable option, defaulting to "display: none"?
				// Note that some e-readers ignore "display: none".  However, the recommended
				// Gitden reader appears to handle it okay.  At least, the page number values
				// from the inserted page break span are not displayed.
				var head = pageDom.Head;
				var newStyle = pageDom.RawDom.CreateElement("style");
				newStyle.SetAttribute("type", "text/css");
				newStyle.InnerXml = "span[role='doc-pagebreak'] { display: none }";
				head.AppendChild(newStyle);
				_pageList.Add( String.Format("<li><a href=\"{0}#{1}\">{2}</a></li>", pageDocName, id, page) );
			}
		}

		private string GetXMatterPageName(string[] classes)
		{
			string languageIdUsed;
			var found = classLabelIdList.Find(x => classes.Contains(x.Item1));
			if (found != null)
				return LocalizationManager.GetString("TemplateBooks.PageLabel."+found.Item2, found.Item2, "",
						_langsForLocalization, out languageIdUsed);
			// The 7 classes above match against what the device xmatter currently offers.  This handles any
			// "required singleton" that doesn't have a matching class.  Perhaps not satisfactory, and perhaps
			// not needed.
			++_frontBackPage;
			return "x" + _frontBackPage.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Add epub:type attributes as appropriate.
		/// </summary>
		private void AddEpubTypeAttributes(HtmlDom pageDom)
		{
			// See http://kb.daisy.org/publishing/docs/html/epub-type.html and https://idpf.github.io/epub-vocabs/structure/.
			// Note: all the "title" related types go on a heading content element (h1, h2, h3, ...).  Bloom doesn't use those.
			// "toc" is used in the nav.html file.
			// Various epub:type values such as frontmatter, bodymatter, cover, credits, etc. are true of certain pages
			// in Bloom books, but we can't currently use them. The standard calls for them to be applied to HTML
			// section elements (or body, but that is strongly deprecated).  Other guidelines say sections must be
			// used for things that belong in the table of contents, but we don't want a TOC entry for every xmatter
			// page.
			// NB: very few epub:type values seem to be valid in conjunction with the aria role attribute.  Conformance
			// checking seems to imply that every epub:type attribute must be matched with a role attribute.
			var body = pageDom.Body;
			var div = body.SelectSingleNode("div[@class]") as XmlElement;
			if (div.GetOptionalStringAttribute("data-page", "") == "required singleton")
			{
				if (PublishHelper.HasClass(div, "titlePage"))
				{
					div.SetAttribute ("type", kEpubNamespace, "titlepage");
				}
				// Possibly on title page
				var divOrigContrib = div.SelectSingleNode (".//div[@id='originalContributions']") as XmlElement;
				if (divOrigContrib != null && !String.IsNullOrWhiteSpace (divOrigContrib.InnerText))
					divOrigContrib.SetAttribute ("type", kEpubNamespace, "contributors");
				var divFunding = div.SelectSingleNode ("../div[@id='funding']") as XmlElement;
				if (divFunding != null && !String.IsNullOrWhiteSpace (divFunding.InnerText))
					divFunding.SetAttribute ("type", kEpubNamespace, "acknowledgements");
				// Possibly on general credits page
				var divCopyright = div.SelectSingleNode (".//div[@data-derived='copyright']") as XmlElement;
				if (divCopyright != null && !String.IsNullOrWhiteSpace (divCopyright.InnerText))
					divCopyright.SetAttribute ("type", kEpubNamespace, "copyright-page");
				var divAck = div.SelectSingleNode (".//div[@data-derived='versionAcknowledgments']") as XmlElement;
				if (divAck != null && !String.IsNullOrWhiteSpace (divAck.InnerText))
					divAck.SetAttribute ("type", kEpubNamespace, "acknowledgements");
				var divOrigCopyright = div.SelectSingleNode (".//div[@data-derived='originalCopyrightAndLicense']") as XmlElement;
				if (divOrigCopyright != null && !String.IsNullOrWhiteSpace (divOrigCopyright.InnerText))
					divOrigCopyright.SetAttribute ("type", kEpubNamespace, "other-credits");
				var divOrigAck = div.SelectSingleNode (".//div[@data-derived='originalAcknowledgments']") as XmlElement;
				if (divOrigAck != null && !String.IsNullOrWhiteSpace (divOrigAck.InnerText))
					divOrigAck.SetAttribute ("type", kEpubNamespace, "contributors");
			}
		}

		/// <summary>
		/// Add ARIA attributes and structure as appropriate.
		/// </summary>
		/// <remarks>
		/// See https://www.w3.org/TR/html-aria/ and http://kb.daisy.org/publishing/.
		///
		/// "Although the W3C’s Web Content Accessibility Guidelines 2.0 are a huge step forward in improving web accessibility,
		/// they do have their issues. Primarily, they are almost impossible to understand."
		/// https://www.wuhcag.com/web-content-accessibility-guidelines/
		/// </remarks>
		private void AddAriaAccessibilityMarkup(HtmlDom pageDom)
		{
			var div = pageDom.Body.SelectSingleNode("//div[@data-page='required singleton']") as XmlElement;
			if (div != null)
			{
				SetRoleAndLabelForClass(div, "frontCover", "TemplateBooks.PageLabel.Front Cover", "Front Cover");
				SetRoleAndLabelForClass(div, "titlePage", "TemplateBooks.PageLabel.Title Page", "Title Page");
				SetRoleAndLabelForClass(div, "credits", "TemplateBooks.PageLabel.Credits Page", "Credits Page");
				// Possibly on title page
				SetRoleAndLabelForMatchingDiv(div, "@id='originalContributions'", "PublishTab.AccessibleEpub.Original Contributions", "Original Contributions");
				SetRoleAndLabelForMatchingDiv(div, "@id='funding'", "PublishTab.AccessibleEpub.Funding", "Funding");
				// Possibly on general credits page
				SetRoleAndLabelForMatchingDiv(div, "@data-derived='copyright'", "PublishTab.AccessibleEpub.Copyright", "Copyright");
				SetRoleAndLabelForMatchingDiv(div, "contains(concat(' ',@class,' '), ' versionAcknowledgments ')", "PublishTab.AccessibleEpub.Version Acknowledgments", "Version Acknowledgments");
				SetRoleAndLabelForMatchingDiv(div, "@data-derived='originalCopyrightAndLicense'", "PublishTab.AccessibleEpub.Original Copyright", "Original Copyright");
				SetRoleAndLabelForMatchingDiv(div, "contains(concat(' ',@class,' '), ' originalAcknowledgments ')", "PublishTab.AccessibleEpub.Original Acknowledgments", "Original Acknowledgments");
			}
			else
			{
				// tests at least don't always start content on page 1
				div = pageDom.Body.SelectSingleNode("//div[@data-page-number]") as XmlElement;
				if (div != null && PublishHelper.HasClass(div, "numberedPage") && !_firstNumberedPageSeen)
				{
					div.SetAttribute ("role", "main");
					string languageIdUsed;
					var label = L10NSharp.LocalizationManager.GetString("PublishTab.Epub.Accessible.MainContent", "Main Content", "",
						_langsForLocalization, out languageIdUsed);
					div.SetAttribute("aria-label", label);
					_firstNumberedPageSeen = true;
				}
			}
			// Note that the alt attribute is handled in HandleImageDescriptions().
			foreach (var img in pageDom.Body.SelectNodes("//img[@src]").Cast<XmlElement> ())
			{
				if (PublishHelper.HasClass(img, "licenseImage") || PublishHelper.HasClass(img, "branding"))
					continue;
				div = img.SelectSingleNode("parent::div[contains(concat(' ',@class,' '),' bloom-imageContainer ')]") as XmlElement;
				// Typically by this point we've converted the image descriptions into asides whose container is the next
				// sibling of the image container. Set Aria Accessibility stuff for them.
				var asideContainer = div?.NextSibling as XmlElement;
				if (asideContainer != null && asideContainer.Attributes["class"]?.Value == "asideContainer")
				{
					++_imgCount;
					var descCount = 0;
					var bookFigId = "bookfig" + _imgCount.ToString(CultureInfo.InvariantCulture);
					img.SetAttribute("id", bookFigId);
					const string period = ".";
					foreach (XmlElement asideNode in asideContainer.ChildNodes)
					{
						var figDescId = "figdesc" + _imgCount.ToString(CultureInfo.InvariantCulture) + period + descCount.ToString(CultureInfo.InvariantCulture);
						++descCount;
						asideNode.SetAttribute("id", figDescId);
						var ariaAttr = img.GetAttribute("aria-describedby");
						// Ace by DAISY cannot handle multiple ID values in the aria-describedby attribute even
						// though the ARIA specification clearly allows this.  So for now, use only the first one.
						// I'd prefer to use specifically the vernacular language aside if we have to choose only
						// one, but the aside elements don't have a lang attribute (yet?).  Perhaps the aside
						// elements are ordered such that the first one is always the vernacular.
						// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6426.
						if (String.IsNullOrEmpty(ariaAttr))
							img.SetAttribute("aria-describedby", figDescId);
					}
				}
			}
			// Provide the general language of this document.
			// (Required for intermediate (AA) conformance with WCAG 2.0.)
			div = pageDom.RawDom.SelectSingleNode("/html") as XmlElement;
			div.SetAttribute("lang", Book.CollectionSettings.Language1Iso639Code);
			div.SetAttribute("xml:lang", Book.CollectionSettings.Language1Iso639Code);
		}

		private bool SetRoleAndLabelForMatchingDiv(XmlElement div, string attributeValue, string labelId, string labelEnglish)
		{
			var divInternal = div.SelectSingleNode (".//div[" + attributeValue + "]") as XmlElement;
			if (divInternal != null && !String.IsNullOrWhiteSpace(divInternal.InnerText))
			{
				string languageIdUsed;
				divInternal.SetAttribute("role", "contentinfo");
				var label = L10NSharp.LocalizationManager.GetString(labelId, labelEnglish, "", _langsForLocalization, out languageIdUsed);
				divInternal.SetAttribute("aria-label", label);
				return true;
			}
			return false;
		}

		private bool SetRoleAndLabelForClass(XmlElement div, string desiredClass, string labelId, string labelEnglish)
		{
			if (PublishHelper.HasClass(div, desiredClass))
			{
				string languageIdUsed;
				div.SetAttribute("role", "contentinfo");
				var label = L10NSharp.LocalizationManager.GetString (labelId, labelEnglish, "", _langsForLocalization, out languageIdUsed);
				div.SetAttribute("aria-label", label);
				return true;
			}
			return false;
		}


		private void CheckForEpubProperties (HtmlDom pageDom, string filename)
		{
			// check for any script elements in the DOM
			var scripts = pageDom.SafeSelectNodes("//script");
			if (scripts.Count > 0)
			{
				_scriptedItems.Add(filename);
			}
			else
			{
				// Check for any of the HTML event attributes in the DOM.  They would each contain a script.
				bool foundEventAttr = false;
				foreach (var attr in pageDom.SafeSelectNodes("//*/@*").Cast<XmlAttribute>())
				{
					switch (attr.Name)
					{
					case "onafterprint":
					case "onbeforeprint":
					case "onbeforeunload":
					case "onerror":
					case "onhashchange":
					case "onload":
					case "onmessage":
					case "onoffline":
					case "ononline":
					case "onpagehide":
					case "onpageshow":
					case "onpopstate":
					case "onresize":
					case "onstorage":
					case "onunload":
						_scriptedItems.Add(filename);
						foundEventAttr = true;
						break;
					default:
						break;
					}
					if (foundEventAttr)
						break;
				}
				// Check for any embedded SVG images.  (Bloom doesn't have any yet, but who knows? someday?)
				var svgs = pageDom.SafeSelectNodes("//svg");
				if (svgs.Count > 0)
				{
					_svgItems.Add(filename);
				}
				else
				{
					// check for any references to SVG image files.  (If we miss one, it's not critical: files with
					// only references to SVG images are optionally marked in the opf file.)
					foreach (var imgsrc in pageDom.SafeSelectNodes("//img/@src").Cast<XmlAttribute>())
					{
						if (imgsrc.Value.ToLowerInvariant().EndsWith(".svg", StringComparison.InvariantCulture))
						{
							_svgItems.Add(filename);
							break;
						}
					}
				}
			}
		}

		// Combines staging and finishing
		public void SaveEpub(string destinationEpubPath, WebSocketProgress progress)
		{
			if(string.IsNullOrEmpty (BookInStagingFolder)) {
				StageEpub(progress);
			}
			ZipAndSaveEpub (destinationEpubPath, progress);
		}

		/// <summary>
		/// Finish publishing an ePUB that has been staged, by zipping it into the desired final file.
		/// </summary>
		/// <param name="destinationEpubPath"></param>
		public void ZipAndSaveEpub (string destinationEpubPath, WebSocketProgress progress)
		{
			progress.Message("Saving", comment:"Shown in a progress box when Bloom is saving an epub", message:"Saving");
			var zip = new BloomZipFile (destinationEpubPath);
			foreach (var file in Directory.GetFiles (BookInStagingFolder))
				zip.AddTopLevelFile (file);
			foreach (var dir in Directory.GetDirectories (BookInStagingFolder))
				zip.AddDirectory (dir);
			zip.Save ();
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
			var fontsWanted = GetFontsUsed (Book.FolderPath, false);
			var fontFileFinder = new FontFileFinder ();
			var filesToEmbed = fontsWanted.SelectMany (fontFileFinder.GetFilesForFont).ToArray ();
			foreach (var file in filesToEmbed) {
				CopyFileToEpub(file, subfolder:kFontsFolder);
			}
			var sb = new StringBuilder ();
			foreach (var font in fontsWanted) {
				var group = fontFileFinder.GetGroupForFont (font);
				if (group != null) {
					// The fonts.css file is stored in a subfolder as are the font files.  They are in different
					// subfolders, and the reference to the font file has to take the relative path to fonts.css
					// into account.
					AddFontFace (sb, font, "normal", "normal", group.Normal, "../"+kFontsFolder+"/", true);
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
			Directory.CreateDirectory(Path.Combine(_contentFolder, kCssFolder));
			RobustFile.WriteAllText(Path.Combine(_contentFolder, kCssFolder, "fonts.css"), sb.ToString());
			_manifestItems.Add(kCssFolder+"/" + "fonts.css");
		}

		internal static void AddFontFace (StringBuilder sb, string name, string weight, string style, string path, string relativePathFromCss="", bool sanitizeFileName = false)
		{
			if (path == null)
				return;

			var fontFileName = Path.GetFileName(path);
			if (sanitizeFileName)
				fontFileName = GetAdjustedFilename(fontFileName, "");
			var fullRelativePath = relativePathFromCss + fontFileName;
			var format = Path.GetExtension(path) == ".woff" ? "woff" : "opentype";

			sb.AppendLine(
				$"@font-face {{font-family:'{name}'; font-weight:{weight}; font-style:{style}; src:url('{fullRelativePath}') format('{format}');}}");
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
		public static IEnumerable<string> GetFontsUsed (string bookPath, bool includeFallbackFonts)
		{
			var result = new HashSet<string> ();
			// Css for styles are contained in the actual html
			foreach (var ss in Directory.EnumerateFiles (bookPath, "*.*").Where (f => f.EndsWith (".css") || f.EndsWith (".htm") || f.EndsWith (".html"))) {
				var root = RobustFile.ReadAllText (ss, Encoding.UTF8);
				HtmlDom.FindFontsUsedInCss (root, result, includeFallbackFonts);
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
		private void FixPictureSizes (HtmlDom pageDom)
		{
			bool firstTime = true;
			double pageWidthMm = 210; // assume A5 Portrait if not specified
			foreach (XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements (pageDom.RawDom.DocumentElement)) {
				var parent = img.ParentNode.ParentNode as XmlElement;
				var mulitplier = 1.0;
				// For now we only attempt to adjust pictures contained in the marginBox.
				// To do better than this we will probably need to actually load the HTML into
				// a browser; even then it will be complex.
				while (parent != null && !PublishHelper.HasClass (parent, "marginBox")) {
					// 'marginBox' is not yet the margin box...it is some parent div.
					// If it has an explicit percent width style, adjust for this.
					var styleAttr = parent.Attributes ["style"];
					if (styleAttr != null) {
						var style = styleAttr.Value;
						var match = new Regex ("width:\\s*(\\d+(\\.\\d+)?)%").Match (style);
						if (match.Success) {
							double percent;
							if (Double.TryParse (match.Groups [1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out percent)) {
								mulitplier *= percent / 100;
							}
						}
					}
					parent = parent.ParentNode as XmlElement;
				}
				if (parent == null)
					continue;
				var page = parent.ParentNode as XmlElement;
				if (!PublishHelper.HasClass (page, "bloom-page"))
					continue; // or return? marginBox should be child of page!
				if (firstTime) {
					var pageClass =
						HtmlDom.GetAttributeValue (page, "class")
							.Split ()
							.FirstOrDefault (c => c.Contains ("Portrait") || c.Contains ("Landscape"));
					// This calculation unfortunately duplicates information from basePage.less.
					const int A4Width = 210;
					const int A4Height = 297;
					const double letterPortraitHeight = 11.0 * mmPerInch;
					const double letterPortraitWidth = 8.5 * mmPerInch;
					const double legalPortraitHeight = 14.0 * mmPerInch;
					const double legalPortraitWidth = 8.5 * mmPerInch;
					switch (pageClass) {
					case "A3Landscape":
						pageWidthMm = A4Width * 2.0;
						break;
					case "A5Portrait":
						pageWidthMm = A4Height / 2.0;
						break;
					case "A4Portrait":
						pageWidthMm = A4Width;
						break;
					case "A5Landscape":
						pageWidthMm = A4Width / 2.0;
						break;
					case "A3Portrait":
						pageWidthMm = A4Width * 2.0;
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
						pageWidthMm = letterPortraitWidth / 2.0;
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
				var imgStyle = HtmlDom.GetAttributeValue (img, "style");
				// We want to take something like 'width:334px; height:220px; margin-left: 34px; margin-top: 0px;'
				// and change it to something like 'width:75%; height:auto; margin-left: 10%; margin-top: 0px;'
				// This first pass deals with width.
				if (ConvertStyleFromPxToPercent ("width", pageWidthMm, mulitplier, ref imgStyle)) continue;

				// Now change height to auto, to preserve aspect ratio
				imgStyle = new Regex ("height:\\s*\\d+px").Replace (imgStyle, "height:auto");
				if (!imgStyle.Contains ("height"))
					imgStyle = "height:auto; " + imgStyle;

				// Similarly fix indent
				ConvertStyleFromPxToPercent ("margin-left", pageWidthMm, mulitplier, ref imgStyle);

				img.SetAttribute ("style", imgStyle);
			}
		}

		// Returns true if we don't find the expected style
		private static bool ConvertStyleFromPxToPercent (string stylename, double pageWidthMm, double multiplier,
			ref string imgStyle)
		{
			var match = new Regex ("(.*" + stylename + ":\\s*)(\\d+)px(.*)").Match (imgStyle);
			if (!match.Success)
				return true;
			var widthPx = int.Parse (match.Groups [2].Value);
			var widthInch = widthPx / 96.0; // in print a CSS px is exactly 1/96 inch
			const int marginBoxMarginMm = 40; // see basePage.less SetMarginBox.
			var marginBoxWidthInch = (pageWidthMm - marginBoxMarginMm) / mmPerInch;
			var parentBoxWidthInch = marginBoxWidthInch * multiplier;
			// parent box is smaller by net effect of parents with %width styles
			// 1/10 percent is close enough and more readable/testable than arbitrary precision; make a string with one decimal
			var newWidth = (Math.Round (widthInch / parentBoxWidthInch * 1000) / 10).ToString ("F1");
			imgStyle = match.Groups [1] + newWidth + "%" + match.Groups [3];
			return false;
		}

		/// <summary>
		/// Inkscape adds a lot of custom attributes and elements that the epubcheck program
		/// objects to.  These may make life easier for editing with inkscape, but aren't needed
		/// to display the image.  So we remove those elements and attributes from the .svg
		/// files when exporting to an ePUB.
		/// </summary>
		/// <remarks>
		/// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6046.
		/// </remarks>
		private void PruneSvgFileOfCruft(string filename)
		{
			var xdoc = new XmlDocument();
			xdoc.Load(filename);
			var unwantedElements = new List<XmlElement>();
			var unwantedAttrsCount = 0;
			foreach (var xel in xdoc.SelectNodes("//*").Cast<XmlElement>())
			{
				if (xel.Name.StartsWith("inkscape:") ||
					xel.Name.StartsWith("sodipodi:") ||
					xel.Name.StartsWith("rdf:") ||
					xel.Name == "flowRoot")	// epubcheck objects to this: must be from a newer version of SVG?
				{
					// Some of the unwanted elements may be children of this element, and
					// deleting this element at this point could disrupt the enumerator and
					// terminate the loop.  So we postpone deleting the element for now.
					unwantedElements.Add(xel);
				}
				else
				{
					// Removing the attribute here requires working from the end of the list of attributes.
					for (int i = xel.Attributes.Count - 1; i >= 0; --i)
					{
						var attr = xel.Attributes[i];
						if (attr.Name.StartsWith("inkscape:") ||
							attr.Name.StartsWith("sodipodi:") ||
							attr.Name.StartsWith("rdf:"))
						{
							xel.RemoveAttributeAt(i);
							++unwantedAttrsCount;
						}
					}
				}
			}
			foreach (var xel in unwantedElements)
			{
				var parent = (XmlElement)xel.ParentNode;
				parent.RemoveChild(xel);
			}
			//System.Diagnostics.Debug.WriteLine($"PruneSvgFileOfCruft(\"{filename}\"): removed {unwantedElements.Count} elements and {unwantedAttrsCount} attributes");
			using (var writer = new XmlTextWriter(filename, new UTF8Encoding(false)))
			{
				xdoc.Save(writer);
			}
		}

		/// <summary>
		/// The epub-visiblity class and the epubVisibility.css stylesheet
		/// are only used to determine the visibility of items.
		/// They allow us to use the browser to determine visibility rules
		/// and then remove unwanted content from the dom completely since
		/// many eReaders do not properly handle display:none.
		/// </summary>
		/// <param name="dom"></param>
		internal void AddEpubVisibilityStylesheetAndClass(HtmlDom dom)
		{
			var headNode = dom.SelectSingleNodeHonoringDefaultNS ("/html/head");
			var epubVisibilityStylesheet = dom.RawDom.CreateElement ("link");
			epubVisibilityStylesheet.SetAttribute ("rel", "stylesheet");
			epubVisibilityStylesheet.SetAttribute ("href", "epubVisibility.css");
			epubVisibilityStylesheet.SetAttribute ("type", "text/css");
			headNode.AppendChild (epubVisibilityStylesheet);

			var bodyNode = dom.SelectSingleNodeHonoringDefaultNS ("/html/body");
			var classAttribute = bodyNode.Attributes ["class"];
			if (classAttribute != null)
				bodyNode.SetAttribute ("class", classAttribute.Value + " epub-visibility");
			else
				bodyNode.SetAttribute ("class", "epub-visibility");
		}

		private void RemoveRegularStylesheets (HtmlDom pageDom)
		{
			foreach (XmlElement link in pageDom.RawDom.SafeSelectNodes ("//head/link").Cast<XmlElement> ().ToArray ()) {
				var href = link.Attributes ["href"];
				if (href != null && Path.GetFileName (href.Value).StartsWith ("custom"))
					continue;
				if (href != null && Path.GetFileName (href.Value) == "defaultLangStyles.css")
					continue;
				link.ParentNode.RemoveChild (link);
			}
		}

		// Copy a file to the appropriate place in the ePUB staging area, and note
		// that it is a necessary manifest item. Return the path of the copied file
		// (which may be different in various ways from the original; we suppress various dubious
		// characters and return something that doesn't depend on url decoding.
		private string CopyFileToEpub (string srcPath, bool limitImageDimensions=false, bool needTransparentBackground=false, string subfolder = "")
		{
			string existingFile;
			if (_mapSrcPathToDestFileName.TryGetValue (srcPath, out existingFile))
				return existingFile; // File already present, must be used more than once.
			var fileName = GetAdjustedFilename(srcPath, Storage.FolderPath);
			// If the fileName starts with a folder inside the Bloom book that maps onto
			// a folder in the epub, remove that folder from the fileName since the proper
			// (quite possibly the same) folder name will be added below as needed.  This
			// simplifies the processing for files being moved into a subfolder for the
			// first time, or into a folder of a different name.
			if (fileName.StartsWith("audio/") || fileName.StartsWith("video/"))
				fileName = fileName.Substring(6);
			string dstPath = SubfolderAdjustedContentPath(subfolder, fileName);
			// We deleted the root directory at the start, so if the file is already
			// there it is a clash, either multiple sources for files with the same name,
			// or produced by replacing spaces, or something. Come up with a similar unique name.
			for (int fix = 1; RobustFile.Exists (dstPath); fix++) {
				var fileNameWithoutExtension = Path.Combine (Path.GetDirectoryName (fileName),
					Path.GetFileNameWithoutExtension (fileName));
				fileName = Path.ChangeExtension (fileNameWithoutExtension + fix, Path.GetExtension (fileName));
				dstPath = SubfolderAdjustedContentPath(subfolder, fileName);
			}
			Directory.CreateDirectory (Path.GetDirectoryName (dstPath));
			CopyFile (srcPath, dstPath, limitImageDimensions, needTransparentBackground);
			_manifestItems.Add(SubfolderAdjustedName(subfolder, fileName));
			_mapSrcPathToDestFileName [srcPath] = dstPath;
			return dstPath;
		}

		public static string GetAdjustedFilename(string srcPath, string folderPath)
		{
			string originalFileName;
			// keep subfolder structure if possible
			if (!string.IsNullOrEmpty(folderPath) && srcPath.StartsWith(folderPath))
				originalFileName = srcPath.Substring(folderPath.Length + 1).Replace('\\', '/');
			else
				originalFileName = Path.GetFileName(srcPath);
			// Validator warns against spaces in filenames. + and % and &<> are problematic because to get the real
			// file name it is necessary to use just the right decoding process. Some clients may do this
			// right but if we substitute them we can be sure things are fine.
			// I'm deliberately not using UrlPathString here because it doesn't correctly encode a lot of Ascii characters like =$&<>
			// which are technically not valid in hrefs
			var revisedFileName = Regex.Replace(originalFileName, "[ +%&<>]", "_");
			var encodedFileName = HttpUtility.UrlEncode(revisedFileName);
			encodedFileName = encodedFileName.Replace("%2f","/");	// we don't want to encode directory separators!
			// If a filename is encoded, epub readers don't seem to decode it very well in requesting
			// the file.  Since we've protected ourselves against problematic characters, now we can
			// protect against decoding issues by fixing encoded characters to effectively stay that
			// way.  We could just change every problematic (nonalphanumeric) character to _, but
			// doing things this way minimizes filename conflicts.  Note that the filename created
			// here is stored verbatim in the ePUB's XHTML file and used verbatim in the filename
			// stored in the ePUB archive.
			return encodedFileName.Replace("%", "_");
		}

		private string SubfolderAdjustedName(string subfolder, string name)
		{
			if (String.IsNullOrEmpty(subfolder))
				return name;
			else
				return subfolder+"/"+name;
		}

		private string SubfolderAdjustedContentPath(string subfolder, string fileName)
		{
			if (String.IsNullOrEmpty(subfolder))
				return Path.Combine(_contentFolder, fileName);
			else
				return Path.Combine(_contentFolder, subfolder, fileName);
		}

		/// <summary>
		/// This supports testing without actually copying files.
		/// </summary>
		internal virtual void CopyFile(string srcPath, string dstPath, bool limitImageDimensions=false, bool needTransparentBackground=false)
		{
			if (limitImageDimensions && BookCompressor.ImageFileExtensions.Contains(Path.GetExtension(srcPath.ToLowerInvariant())))
			{
				var imageBytes = BookCompressor.GetImageBytesForElectronicPub(srcPath, needTransparentBackground);
				RobustFile.WriteAllBytes(dstPath, imageBytes);
				return;
			}
			RobustFile.Copy(srcPath, dstPath);
		}

		// The validator is (probably excessively) upset about IDs that start with numbers.
		// I don't think we actually use these IDs in the ePUB so maybe we should just remove them?
		private void FixIllegalIds (HtmlDom pageDom)
		{
			// Xpath results are things that have an id attribute, so MUST be XmlElements (though the signature
			// of SafeSelectNodes allows other XmlNode types).
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes ("//*[@id]")) {
				var id = elt.Attributes ["id"].Value;
				var first = id [0];
				if (first >= '0' && first <= '9')
					elt.SetAttribute ("id", "i" + id);
			}
		}

		private void MakeNavPage ()
		{
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			var sb = new StringBuilder ();
			sb.Append (@"
<html xmlns='http://www.w3.org/1999/xhtml' xmlns:epub='http://www.idpf.org/2007/ops'>
	<head>
		<meta charset='utf-8' />
	</head>
	<body>
		<nav epub:type='toc' id='toc'>
			<ol>");
			foreach (var item in _tocList)
			{
				sb.AppendLine();
				sb.AppendFormat("\t\t\t\t{0}", item);
			}
			sb.Append(@"
			</ol>
		</nav>
		<nav epub:type='page-list'>
			<ol>");
			foreach (var item in _pageList)
			{
				sb.AppendLine();
				sb.AppendFormat("\t\t\t\t{0}", item);
			}
			sb.Append (@"
			</ol>
		</nav>
	</body>
</html>");
			var content = XElement.Parse (sb.ToString ());
			_navFileName = "nav.xhtml";
			var navPath = Path.Combine (_contentFolder, _navFileName);

			using (var writer = XmlWriter.Create (navPath))
				content.WriteTo (writer);
			_manifestItems.Add (_navFileName);
		}

		/// <summary>
		/// We don't need to make scriptable books, and if our html contains scripts
		/// (which probably won't work on most readers) we have to add various attributes.
		/// Also our scripts are external refs, which would have to be fixed.
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveScripts (HtmlDom pageDom)
		{
			foreach (var elt in pageDom.RawDom.SafeSelectNodes ("//script").Cast<XmlElement> ().ToArray ()) {
				elt.ParentNode.RemoveChild (elt);
			}
		}

		/// <summary>
		/// Clean up any dangling pointers and similar spurious data.
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveSpuriousLinks (HtmlDom pageDom)
		{
			// The validator has complained about area-describedby where the id is not found.
			// I don't think we will do qtips at all in books so let's just remove these altogether for now.
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes ("//*[@aria-describedby]")) {
				elt.RemoveAttribute ("aria-describedby");
			}

			// Validator doesn't like empty lang attributes, and they don't convey anything useful, so remove.
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes ("//*[@lang='']")) {
				elt.RemoveAttribute ("lang");
			}
			// Validator doesn't like '*' as value of lang attributes, and they don't convey anything useful, so remove.
			foreach (XmlElement elt in pageDom.RawDom.SafeSelectNodes ("//*[@lang='*']")) {
				elt.RemoveAttribute ("lang");
			}
		}

		/// <summary>
		/// Remove anything that has class bloom-ui
		/// </summary>
		/// <param name="pageDom"></param>
		private void RemoveBloomUiElements (HtmlDom pageDom)
		{
			foreach (var elt in pageDom.RawDom.SafeSelectNodes ("//*[contains(concat(' ',@class,' '),' bloom-ui ')]").Cast<XmlElement> ().ToList ()) {
				elt.ParentNode.RemoveChild (elt);
			}
		}

		/// <summary>
		/// Since file names often start with numbers, which ePUB validation won't allow for element IDs,
		/// stick an 'f' in front. Generally clean up file name to make a valid ID as similar as possible.
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		private string GetIdOfFile (string item)
		{
			string id;
			if (_mapItemToId.TryGetValue (item, out id))
				return id;
			id = ToValidXmlId (Path.GetFileNameWithoutExtension (item));
			var idOriginal = id;
			for (int i = 1; _idsUsed.Contains (id.ToLowerInvariant ()); i++) {
				// Somehow we made a clash
				id = idOriginal + i;
			}
			_idsUsed.Add (id.ToLowerInvariant ());
			_mapItemToId [item] = id;

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
		internal static string ToValidXmlId (string item)
		{
			string output = item.Replace (" ", "");
			// This conforms to http://www.w3.org/TR/REC-xml/#NT-Name except that we don't handle valid characters above FFFF.
			string validStartRanges =
				":A-Z_a-z\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u02FF\u0370-\u037D\u037F-\u1FFF\u200C-\u200D\u2070-\u218F\u2C00-\u2FEF\u3001-\uD7FF\uF900-\uFDCF\uFDF0-\uFFFD";
			string validChars = validStartRanges + "\\-.0-9\u00b7\u0300-\u036F\u203F-\u2040";
			output = Regex.Replace (output, "[^" + validChars + "]", "_");
			if (!new Regex ("^[" + validStartRanges + "]").IsMatch (output))
				return "f" + output;
			return output;
		}

		private string GetMediaType(string item)
		{
			var extension = String.Empty;
			if (!String.IsNullOrEmpty(item))
				extension = Path.GetExtension(item);
			if (!String.IsNullOrEmpty(extension))
				extension = extension.Substring(1);	// ignore the .
			switch (extension.ToLowerInvariant())
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
				return "image/svg+xml";     // https://www.w3.org/TR/SVG/intro.html
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
			// REVIEW: Our mp4 containers are for video; but epub 3 doesn't have media types for video.
			// I started to remove this when we removed the vestiges of old audio mp4 code, but video unit tests fail without it.
			case "mp4":
				return "audio/mp4";
			case "mp3":
				return "audio/mpeg";
			}
			throw new ApplicationException ("unexpected/nonexistent file type in file " + item);
		}

		private static void MakeCssLinksAppropriateForEpub(HtmlDom dom)
		{
			dom.RemoveModeStyleSheets ();
			dom.SortStyleSheetLinks ();
			dom.RemoveFileProtocolFromStyleSheetLinks ();
			dom.RemoveDirectorySpecificationFromStyleSheetLinks ();
		}

		private HtmlDom GetEpubFriendlyHtmlDomForPage(XmlElement page)
		{
			var headXml = Storage.Dom.SelectSingleNodeHonoringDefaultNS ("/html/head").OuterXml;
			var dom = new HtmlDom (@"<html>" + headXml + "<body></body></html>");
			dom = Storage.MakeDomRelocatable (dom);
			var body = dom.RawDom.SelectSingleNodeHonoringDefaultNS ("//body");
			var pageDom = dom.RawDom.ImportNode (page, true);
			body.AppendChild (pageDom);
			return dom;
		}

		public void Dispose()
		{
			if (_outerStagingFolder != null)
				_outerStagingFolder.Dispose();
			_outerStagingFolder = null;
			if (_publishHelper != null)
				_publishHelper.Dispose();
			_publishHelper = null;
		}
	}
}
