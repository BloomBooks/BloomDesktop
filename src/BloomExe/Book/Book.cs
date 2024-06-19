using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.ErrorReporter;
using Bloom.FontProcessing;
using Bloom.History;
using Bloom.Publish;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Linq;
using SIL.Progress;
using SIL.Reporting;
using SIL.Text;
using SIL.Windows.Forms.ClearShare;
using SIL.Xml;
using Enumerable = System.Linq.Enumerable;

namespace Bloom.Book
{
    public class Book
    {
        public delegate Book Factory(BookInfo info, IBookStorage storage); //autofac uses this
        public static Color[] CoverColors =
        {
            Color.FromArgb(228, 140, 132),
            Color.FromArgb(176, 222, 228),
            Color.FromArgb(152, 208, 185),
            Color.FromArgb(194, 166, 191)
        };

        //We only randomize the initial value for each run. Without it, we were making a lot
        // more red books than any other color, because the
        //first book for a given run would always be red, and people are unlikely to make more
        //than one book per session.
        private static int s_coverColorIndex = new Random().Next(CoverColors.Length - 1);

        private readonly ITemplateFinder _templateFinder;
        private readonly PageListChangedEvent _pageListChangedEvent;
        private readonly BookRefreshEvent _bookRefreshEvent;
        private readonly BookSavedEvent _bookSavedEvent;
        private List<IPage> _pagesCache;
        internal const string kIdOfBasicBook = "056B6F11-4A6C-4942-B2BC-8861E62B03B3";

        public event EventHandler ContentsChanged;
        private BookData _bookData;
        public const string ReadMeImagesFolderName = "ReadMeImages";

        int _audioFilesCopiedForDuplication;

        public const string BasicTextAndImageGuid = "adcd48df-e9ab-4a07-afd4-6a24d0398382";
        public const string JustPictureGuid = "adcd48df-e9ab-4a07-afd4-6a24d0398385";
        public const string PictureOnLeftGuid = "7b192144-527c-417c-a2cb-1fb5e78bf38a";
        public const string JustTextGuid = "a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb";
        public const string JustVideoGuid = "8bedcdf8-3ad6-4967-b027-6c186436572f";
        public const string VideoOverTextGuid = "299644f5-addb-476f-a4a5-e3978139b188";
        public const string PictureAndVideoGuid = "24c90e90-2711-465d-8f20-980d9ffae299";
        public const string BigTextDiglotGuid = "08422e7b-9406-4d11-8c71-02005b1b8095";
        public const string WidgetGuid = "3a705ac1-c1f2-45cd-8a7d-011c009cf406"; // default page type for a single widget

        public bool UpToDate = false;

        /// <summary>
        /// Flag whether we want to write out the @font-face lines for served fonts to defaultLangStyles.css.
        /// (ePUB and BloomPub publishing are when we don't want to do this, either because those fonts will
        /// be embedded or because bloom-player already has compatible @font-face declarations for Andika.)
        /// </summary>
        public bool WriteFontFaces = true;

        //for moq'ing only; parameterless ctor required by Moq
        public Book()
        {
            Guard.Against(!Program.RunningUnitTests, "Only use this ctor for tests!");
        }

        public Book(BookInfo info = null, IBookStorage storage = null)
            : this()
        {
            BookInfo = info;
            Storage = storage;
            CollectionSettings = storage?.CollectionSettings;
        }

        public Book(
            BookInfo info,
            IBookStorage storage,
            ITemplateFinder templateFinder,
            CollectionSettings collectionSettings,
            PageListChangedEvent pageListChangedEvent,
            BookRefreshEvent bookRefreshEvent,
            BookSavedEvent bookSavedEvent = null,
            ISaveContext context = null
        )
        {
            BookInfo = info;
            if (bookSavedEvent == null) // unit testing
            {
                bookSavedEvent = new BookSavedEvent();
            }
            UserPrefs = UserPrefs.LoadOrMakeNew(Path.Combine(info.FolderPath, "book.userPrefs"));

            Guard.AgainstNull(storage, "storage");

            // This allows the _storage to
            storage.BookInfo = info;

            // We always validate the book during the process of loading the storage,
            // so we don't need to do it again until something changes...just note the result.
            if (!string.IsNullOrEmpty(storage.ErrorMessagesHtml))
            {
                HasFatalError = true;
                FatalErrorDescription = storage.ErrorMessagesHtml;
            }
            else if (!string.IsNullOrEmpty(storage.InitialLoadErrors))
            {
                HasFatalError = true;
                FatalErrorDescription = storage.InitialLoadErrors;
            }

            Storage = storage;

            //this is a hack to keep these two in sync (in one direction)
            Storage.FolderPathChanged += _storage_FolderPathChanged;

            _templateFinder = templateFinder;

            CollectionSettings = collectionSettings ?? storage.CollectionSettings;

            _pageListChangedEvent = pageListChangedEvent;
            _bookRefreshEvent = bookRefreshEvent;
            _bookSavedEvent = bookSavedEvent;

            _bookData = new BookData(OurHtmlDom, CollectionSettings, UpdateImageMetadataAttributes);

            InjectStringListingActiveLanguagesOfBook();

            if (IsInEditableCollection)
            {
                // This is usually unnecessary since it happens as part of ensure-up-to-date,
                // which is called before we do anything much with a book. But if we are starting
                // with a book that has no data-div (mainly testing), this copies data from
                // xmatter pages to the data-div that would otherwise be lost when we update
                // the xmatter pages. That's important in some unit tests, at least. And a few
                // other unit tests fail if we don't do this.
                _bookData.SynchronizeDataItemsThroughoutDOM();
            }

            // If it doesn't already have a userModifiedStyles element, give it one.
            // BL-4266 Somehow it is important for there to be a userModifiedStyles element BEFORE (in order!)
            // the coverColor style element in the Head of the DOM in order for the new Css Rules
            // to get inserted properly. So we make sure there is one.
            GetOrCreateUserModifiedStyleElementFromStorage();

            // If we're showing the user one of our built-in templates (like Basic Book), pick a color for it.
            // If it is editable or from bloomlibrary or a BloomPack, then we don't want to change to the next color,
            // we want to use the color that we used for the sample shell/template we showed them previously.
            // (BL-11490 Even shells or downloaded books should preserve the original cover color.)
            if (
                Path.GetDirectoryName(info.FolderPath)
                == BloomFileLocator.FactoryTemplateBookDirectory
            )
            {
                SelectNextCoverColor(); // we only increment when showing a built-in template
                InitCoverColor();
            }

            // If it doesn't already have a cover color, give it one.
            if (HtmlDom.GetCoverColorStyleElement(OurHtmlDom.Head) == null)
            {
                InitCoverColor(); // should use the same color as what they saw in the preview of the template/shell
            }
            FixBookIdAndLineageIfNeeded();
            FixUrlEncodedCoverImageIfNeeded();
            Storage.Dom.RemoveExtraBookTitles();
            Storage.Dom.RemoveExtraContentTypesMetas();
            Guard.Against(
                OurHtmlDom.RawDom.InnerXml == "",
                "Bloom could not parse the xhtml of this document"
            );

            // We introduced "template starter" in 3.9, but books you made with it could be used in 3.8 etc.
            // If those books came back to 3.9 or greater (which would happen eventually),
            // they would still have this tag that they didn't really understand, and which should have been removed.
            // At the moment, only templates are suitable for making shells, so use that to detect that someone has
            // edited a user defined template book in a version that doesn't know about user defined templates.
            if (Storage.Dom.GetGeneratorVersion() < new System.Version(3, 9))
            {
                if (IsSuitableForMakingShells)
                    Storage.Dom.FixAnyAddedCustomPages();
                else
                    Storage.Dom.RemoveMetaElement("xmatter");
            }
            if (Storage is BookStorage)
                (Storage as BookStorage).BookTitleChanged += Book_BookTitleChanged;
        }

        /// <summary>
        /// This gets set when a new book is created by copying a source book.
        /// It is the folder name (without path) of the source book.
        /// Setting this allows the code which renames the new book to make a more helpful history report.
        /// </summary>
        public static string SourceToReportForNextRename;

        public void UpdateBookInfoFromDisk()
        {
            BookInfo.UpdateFromDisk();
            //History.BookHistory.AddEvent(this, BookHistoryEventType, "Updated book info from disk");
        }

        private void Book_BookTitleChanged(object sender, EventArgs e)
        {
            if (BookInfo.FileNameLocked)
            {
                // Forced rename
                BookHistory.AddEvent(
                    this,
                    BookHistoryEventType.Renamed,
                    $"Book renamed to \"{Path.GetFileName(FolderPath)}\""
                );
            }
            else
            {
                var folderName = Path.GetFileName(FolderPath);
                if (SourceToReportForNextRename == null)
                {
                    BookHistory.AddEvent(
                        this,
                        BookHistoryEventType.Renamed,
                        $"Book title changed to \"{Storage.Dom.Title}\""
                    );
                }
                else
                {
                    // On this path we don't think the folder name will be significantly different from the title
                    // (added number to disambiguate, illegal characters removed). So don't need two versions.
                    BookHistory.AddEvent(
                        this,
                        BookHistoryEventType.Created,
                        $"Created a new book \"{Storage.Dom.Title}\" from a source book \"{SourceToReportForNextRename}\""
                    );
                    SourceToReportForNextRename = null;
                }
            }
            // Ensure proper path for any new book title and embedded CSS that is now invalid.
            SettingsUpdated();
        }

        void _storage_FolderPathChanged(object sender, EventArgs e)
        {
            BookInfo.FolderPath = Storage.FolderPath;
            UserPrefs.UpdateFileLocation(Storage.FolderPath);
            ResetPreviewDom();
        }

        /// <summary>
        /// This just increments the color index so that the next book to be constructed that doesn't already have a color will use it
        /// </summary>
        public static void SelectNextCoverColor()
        {
            s_coverColorIndex = s_coverColorIndex + 1;
            if (s_coverColorIndex >= CoverColors.Length)
                s_coverColorIndex = 0;
        }

        public CollectionSettings CollectionSettings { get; }

        public void InvokeContentsChanged(EventArgs e)
        {
            ContentsChanged?.Invoke(this, e);
        }

        /// <summary>
        /// If we have to just show title in one language, which should it be?
        /// Note, this isn't going to be the best for choosing a filename, which we are more likely to want in a national language
        /// </summary>
        public virtual string TitleBestForUserDisplay
        {
            get
            {
                return GetBestTitleForDisplay(
                    _bookData.GetMultiTextVariableOrEmpty("bookTitle"),
                    _bookData.GetBasicBookLanguageCodes().ToList(),
                    IsInEditableCollection
                );
            }
        }

        /// <summary>
        /// If the user has renamed the book, returns that. Otherwise, returns the title
        /// </summary>
        /// <remarks>
        /// When should you use this vs TitleBestForUserDisplay?
        /// NameBestForUserDisplay should definitely be used on the button caption and when deriving a related file name (for a book artifact).
        /// In general, messages about the book as well as logs, and in general most things should probably use this (NameBestForUserDisplay),
        /// because the user has explicitly told us that this is how they want to refer to the book.
        /// The only exceptions where TitleBestForUserDisplay is more appropriate that come to mind are maybe on Publishing related things where
        /// the actual, literal book title that readers see is important.
        /// Also, when comparing with any title-related variables like {originalTitle}.
        /// </remarks>
        public virtual string NameBestForUserDisplay
        {
            get
            {
                if (BookInfo.FileNameLocked)
                {
                    // The user has explicitly chosen a name to use for the book, distinct from its titles.
                    return Path.GetFileName(FolderPath);
                }
                return TitleBestForUserDisplay;
            }
        }

        /// <summary>
        /// Get the best title to display for the given multilingual title and list of languages.
        /// Do NOT use this method for a book if its BookInfo is NameLocked!
        /// </summary>
        public static string GetBestTitleForDisplay(
            MultiTextBase title,
            List<string> langCodes,
            bool isInEditableCollection
        )
        {
            var display = title.GetExactAlternative(langCodes[0]);
            if (string.IsNullOrEmpty(display))
            {
                //the SIL-LEAD project, SHRP (around 2012-2016) had books that just had an English name, before we changed Bloom
                //to not show English names. But the order was also critical. So we want those old books to go ahead and use their
                //English names.
                var englishTitle = title.GetExactAlternative("en").ToLowerInvariant();
                var SHRPMatches = new[] { "p1", "p2", "p3", "p4", "SHRP" };
                var couldBeOldStyleUgandaSHRPBook = SHRPMatches.Any(
                    m => englishTitle.Contains(m.ToLowerInvariant())
                );

                //if this book is one of the ones we're editing in our collection, it really
                //needs a title in our main language, it would be confusing to show a title from some other langauge
                if (!couldBeOldStyleUgandaSHRPBook && (isInEditableCollection || title.Empty))
                {
                    display = LocalizationManager.GetString(
                        "CollectionTab.TitleMissing",
                        "Title Missing",
                        "Shown as the thumbnail caption when the book doesn't have a title."
                    );
                }
                //but if this book is just in our list of sources, well then let's look through the names
                //and try to get one that is likely to be helpful
                else
                {
                    var orderedPreferences = new List<string>();
                    orderedPreferences.Add(LocalizationManager.UILanguageId);

                    // we already know that langCodes[0] does not have a title
                    for (int i = 1; i < langCodes.Count; ++i)
                        orderedPreferences.Add(langCodes[i]);
                    orderedPreferences.Add("en");
                    orderedPreferences.Add("fr");
                    orderedPreferences.Add("es");
                    display = title.GetBestAlternativeString(orderedPreferences);
                    if (string.IsNullOrWhiteSpace(display))
                    {
                        display = title.GetFirstAlternative();
                        Debug.Assert(
                            !string.IsNullOrEmpty(display),
                            "by our logic, this shouldn't possible"
                        );
                    }
                }
            }
            // Handle both Windows and Linux line endings in case a file copied between the two
            // ends up with the wrong one.
            display = display
                .Replace("<br />", " ")
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("  ", " ");
            display = RemoveHtmlMarkup(display, LineBreakSpanConversionMode.ToSpace).Trim();
            return display;
        }

        public enum LineBreakSpanConversionMode
        {
            ToNewline, // Environment.Newline
            ToSpace, // " "
            ToSimpleNewline // "\n", used for export to meta.json
        }

        // what can be in here is *XHTML*, e.g. bold, italics, etc.
        public static string RemoveHtmlMarkup(
            string input,
            LineBreakSpanConversionMode lineBreakSpanConversionOption
        )
        {
            if (input == null)
                return null;
            try
            {
                var doc = new XmlDocument();
                doc.PreserveWhitespace = true;
                doc.LoadXml("<div>" + input + "</div>");

                const char kBOM = '\uFEFF'; // Unicode Byte Order Mark character.

                // Handle Shift+Enter, which gets translated to <span class="bloom-linebreak" />
                // This is being handled using at the XML level instead of string level, so that it'll work regardless of
                // whether it uses the <span /> form or <span></span> form. (I do see places in the debugger where the data is in <span></span> form.)
                var lineBreaks = doc.SafeSelectNodes(
                    "//span[contains(concat(' ', normalize-space(@class), ' '), ' bloom-linebreak ')]"
                );
                var lineBreakElements = lineBreaks.Cast<XmlElement>().ToArray();
                foreach (var lineBreakSpan in lineBreakElements)
                {
                    // But before we mess with lineBreakSpan, first check if it's immediately followed by a BOM
                    // character (which is also inserted by Shift-Enter), and if so delete that out.
                    var nextSibling = lineBreakSpan.NextSibling;

                    // String.StartsWith() seems to always ignore the BOM character, so use a character
                    // comparison.  See https://issues.bloomlibrary.org/youtrack/issue/BL-11717.
                    if (
                        nextSibling?.NodeType == XmlNodeType.Text
                        && !String.IsNullOrEmpty(nextSibling.Value)
                        && nextSibling.Value[0] == kBOM
                    )
                    {
                        nextSibling.Value = nextSibling.Value.Substring(1);
                    }

                    // Now delete lineBreakSpan and replace it
                    var replacementForLinebreakSpan = "";
                    switch (lineBreakSpanConversionOption)
                    {
                        case LineBreakSpanConversionMode.ToNewline:
                            replacementForLinebreakSpan = Environment.NewLine;
                            break;
                        case LineBreakSpanConversionMode.ToSpace:
                            replacementForLinebreakSpan = " ";
                            break;
                        case LineBreakSpanConversionMode.ToSimpleNewline:
                            replacementForLinebreakSpan = "\n";
                            break;
                    }
                    var newlineNode = doc.CreateTextNode(replacementForLinebreakSpan);
                    lineBreakSpan.ParentNode.ReplaceChild(newlineNode, lineBreakSpan);
                }

                return doc.DocumentElement.InnerText;
            }
            catch (XmlException)
            {
                return input; // If we can't parse for some reason, return the original string
            }
        }

        /// <summary>
        /// we could get the title from the <title/> element, the name of the html, or the name of the folder...
        /// </summary>
        public virtual string Title
        {
            get
            {
                Debug.Assert(BookInfo.FolderPath == Storage.FolderPath);

                if (IsInEditableCollection)
                {
                    //REVIEW: evaluate and explain when we would choose the value in the html over the name of the folder.
                    //1 advantage of the folder is that if you have multiple copies, the folder tells you which one you are looking at
                    var s = OurHtmlDom.Title;
                    if (string.IsNullOrEmpty(s))
                        return Path.GetFileName(Storage.FolderPath);
                    return s;
                }
                else //for templates and such, we can already just use the folder name
                {
                    return Path.GetFileName(Storage.FolderPath);
                }
            }
        }

        public string PrettyPrintLanguage(string code)
        {
            return _bookData.GetDisplayNameForLanguage(code);
        }

        public virtual HtmlDom GetEditableHtmlDomForPage(IPage page)
        {
            if (HasFatalError)
            {
                return GetErrorDom();
            }

            var pageDom = GetHtmlDomWithJustOnePage(page);
            pageDom.RemoveModeStyleSheets();
            pageDom.EnsureStylesheetLinks(
                this.BookInfo.AppearanceSettings.BasePageCssName,
                "editMode.css",
                "editPaneGlobal.css"
            );
            AddJavaScriptForEditing(pageDom);
            RuntimeInformationInjector.AddUIDictionaryToDom(pageDom, _bookData, BookInfo);
            RuntimeInformationInjector.AddUISettingsToDom(
                pageDom,
                _bookData,
                Storage.GetFileLocator()
            );
            UpdateMultilingualSettings(pageDom);

            if (IsSuitableForMakingShells && !page.IsXMatter)
            {
                // We're editing a template page in a template book.
                // Make the label editable. Note: HtmlDom.ProcessPageAfterEditing knows about removing this.
                // I don't like the knowledge being in two places, but the place to remove the attribute is in the
                // middle of a method in HtmlDom and it's this class that knows about the book being a template
                // and whether it should be added.
                // (Note: we don't want this for xmatter pages because they don't function as actual template pages.)
                HtmlDom.MakeEditableDomShowAsTemplate(pageDom);
            }
            return pageDom;
        }

        private void AddJavaScriptForEditing(HtmlDom dom)
        {
            // BL-117, PH: With the newer xulrunner, javascript code with parenthesis in the URL is not working correctly.

            //dom.AddJavascriptFile("lib/ckeditor/ckeditor.js".ToLocalhost());

            //reviewslog: added this to get the "WebFXTabPane()" working in StyleEditor. Previously tried adding "export" to the function and then importing it
            dom.AddJavascriptFile("lib/tabpane.js".ToLocalhost());

            //reviewslog: four lines are prompted by the qtip "too much recursion" error, which I got on certain pages. The qtip
            //code in question says it is for when jquery-ui is not found. I "solved" this by loading jquery, jquery-ui,
            //and finally qtip into the global space here
            dom.AddJavascriptFile("jquery.min.js".ToLocalhost());
            dom.AddJavascriptFile(
                "modified_libraries/jquery-ui/jquery-ui-1.10.3.custom.min.js".ToLocalhost()
            );
            //			dom.AddJavascriptFile("lib/jquery.qtip.js".ToLocalhost());
            //			dom.AddJavascriptFile("lib/jquery.qtipSecondary.js".ToLocalhost());

            // first tried this as import 'jquery.hotkeys' in bloomEditing, but that didn't work
            //dom.AddJavascriptFile("jquery.hotkeys.js".ToLocalhost());

            dom.AddJavascriptFile("commonBundle.js".ToLocalhost());
            dom.AddJavascriptFile("editablePageBundle.js".ToLocalhost());
            // Add this last because currently its document ready function has to execute AFTER the bootstrap call in bloomEditing.ts,
            // which is compiled into editablePageIFrame.js. The bootstrap function sets CKEDITOR.disableAutoInline = true,
            // which suppresses a document ready function in CKEditor iself from calling inline() on all content editable
            // elements, which we don't want (a) because some content editable elements shouldn't have CKEditor functions, and
            // (b) because it causes crashes when we intentionally do our own inline() calls on the elements where we DO
            // want CKEditor.
            // ReviewSlog: It would be much more robust not to depend on the order in which document ready functions
            // execute, especially if the only control over that is the order of loading files. But I don't know
            // where we can put the CKEDITOR.disableAutoInline = true so that it will reliably execute AFTER CKEDITOR is
            // defined and BEFORE its document ready function.
            dom.AddJavascriptFile("lib/ckeditor/ckeditor.js".ToLocalhost());
        }

        private void UpdateMultilingualSettings(HtmlDom dom)
        {
            TranslationGroupManager.UpdateContentLanguageClasses(
                dom.RawDom,
                _bookData,
                BookInfo.AppearanceSettings,
                Language1Tag,
                Language2Tag,
                Language3Tag
            );
            BookInfo.IsRtl = IsPrimaryLanguageRtl;

            BookStarter.SetLanguageForElementsWithMetaLanguage(dom.RawDom, _bookData);
        }

        private HtmlDom GetHtmlDomWithJustOnePage(IPage page)
        {
            var divNodeForThisPage = page.GetDivNodeForThisPage();
            if (divNodeForThisPage == null)
            {
                throw new ApplicationException(
                    $"The requested page {page.Id} from book {page.Book.FolderPath} isn't in this book {FolderPath}."
                );
            }

            return GetHtmlDomWithJustOnePage(divNodeForThisPage);
        }

        public HtmlDom GetHtmlDomWithJustOnePage(XmlElement divNodeForThisPage)
        {
            var headXml = Storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head").OuterXml;
            var originalBody = Storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/body");

            var enterpriseStatusClass = this.CollectionSettings.HaveEnterpriseFeatures
                ? "enterprise-on"
                : "enterprise-off";
            var dom = new HtmlDom(
                @"<html>" + headXml + $"<body class='{enterpriseStatusClass}'></body></html>"
            );
            dom = Storage.MakeDomRelocatable(dom);
            // Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
            dom.RawDom.PreserveWhitespace = true;
            var newBody = dom.RawDom.SelectSingleNodeHonoringDefaultNS("/html/body") as XmlElement;

            // copy over any attributes on body (we store a form of the book feature flags there so that css can get at them)
            foreach (XmlAttribute attr in originalBody.Attributes)
            {
                newBody.SetAttribute(attr.Name, attr.Value);
            }

            var pageDom = dom.RawDom.ImportNode(divNodeForThisPage, true);
            newBody.AppendChild(pageDom);

            //                BookStorage.HideAllTextAreasThatShouldNotShow(dom, langTagToLeaveVisible, Page.GetPageSelectorXPath(dom));

            return dom;
        }

        public HtmlDom GetHtmlDomForPageList(HtmlDom inputDom)
        {
            var headNode = Storage.Dom.SelectSingleNodeHonoringDefaultNS("/html/head");
            var inputHead = inputDom.SelectSingleNodeHonoringDefaultNS("/html/head");
            var insertBefore = inputHead.FirstChild; // Enhance: handle case where there is no existing child
            foreach (XmlNode child in headNode.ChildNodes)
            {
                inputHead.InsertBefore(inputDom.RawDom.ImportNode(child, true), insertBefore);
            }

            // This version somehow leaves the head in the wrong (empty) namespace and nothing works.
            //var importNode = inputDom.RawDom.ImportNode(headNode, true);
            //foreach (XmlNode child in inputHead.ChildNodes)
            //	importNode.AppendChild(child);
            //inputHead.ParentNode.ReplaceChild(importNode, inputHead);
            var result = Storage.MakeDomRelocatable(inputDom);
            result.EnsureStylesheetLinks("previewMode.css");
            return result;
        }

        public HtmlDom GetPreviewXmlDocumentForPage(IPage page)
        {
            if (HasFatalError)
            {
                return GetErrorDom();
            }
            var pageDom = GetHtmlDomWithJustOnePage(page);
            pageDom.RemoveModeStyleSheets();
            pageDom.EnsureStylesheetLinks(this.Storage.GetCssFilesToLinkForPreview());
            // Note: it would be a fine enhancement here to first check for "branding-{flavor}.css",
            // but we'll leave that until we need it.
            AddPreviewJavascript(pageDom); //review: this is just for thumbnails... should we be having the javascript run?
            return pageDom;
        }

        // Differs from GetPreviewXmlDocumentForPage() by not adding the three stylesheets
        // adding them will full paths seems to be diastrous. I think cross-domain rules
        // prevent them from being loaded, and so we lose the page size information, and the
        // thumbs come out random sizes. Not sure why this isn't a problem in GetPreviewXmlDocumentForPage.
        // Also, since this is used for thumbnails of template pages, we insert some arbitrary text
        // into empty editable divs to give a better idea of what a typical page will look like.
        internal HtmlDom GetThumbnailXmlDocumentForPage(IPage page)
        {
            if (HasFatalError)
            {
                return GetErrorDom();
            }
            var pageDom = GetHtmlDomWithJustOnePage(page);
            AddPreviewJavascript(pageDom);
            HtmlDom.AddClass(pageDom.Body, "bloom-templateThumbnail");
            return pageDom;
        }

        public HtmlDom GetPreviewXmlDocumentForFirstPage()
        {
            if (HasFatalError)
            {
                return null;
            }

            var bookDom = GetBookDomWithStyleSheets("previewMode.css", "thumbnail.css");

            HideEverythingButFirstPageAndRemoveScripts(bookDom.RawDom);
            return bookDom;
        }

        private static void HideEverythingButFirstPageAndRemoveScripts(XmlDocument bookDom)
        {
            bool onFirst = true;
            foreach (
                XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
            )
            {
                if (!onFirst)
                {
                    node.SetAttribute("style", "", "display:none");
                }
                onFirst = false;
            }
            //Without casting to array, Mono considers this manipulating the enumerable list
            foreach (var node in bookDom.SafeSelectNodes("//script").Cast<XmlNode>().ToArray())
            {
                //TODO: this removes image scaling, which is ok so long as it's already scaled with width/height attributes
                node.ParentNode.RemoveChild(node);
            }
        }

        private static void DeletePages(
            XmlDocument bookDom,
            Func<XmlElement, bool> pageSelectingPredicate
        )
        {
            // Seems safest to make a list so we're not modifying the document while iterating through it.
            var pagesToDelete = new List<XmlElement>();
            foreach (
                XmlElement node in bookDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
            )
            {
                if (pageSelectingPredicate(node))
                {
                    pagesToDelete.Add(node);
                }
            }
            foreach (var node in pagesToDelete)
            {
                // An earlier version of this method just set the visibility of the pages we don't want
                // in this printout to display:none, like this:
                //node.SetAttribute("style", "", "display:none");
                // However, this runs up against a defect in Gecko PDF generation: apparently when
                // all the content after the last page in a paginated document is display:none, Gecko
                // puts in an extra blank page. We suspect something like code that detects that
                // the current page is finished and the document is not finished and starts a new page,
                // which turns out not to be needed. The extra blank page can mess up booklet generation
                // and cause an extra sheet of paper to be used (leaving a wasted four blank pages at
                // the end). See BL-705.
                node.ParentNode.RemoveChild(node);
            }
        }

        internal IFileLocator GetFileLocator()
        {
            return Storage.GetFileLocator();
        }

        private HtmlDom GetBookDomWithStyleSheets(params string[] cssFileNames)
        {
            var dom = Storage.GetRelocatableCopyOfDom();
            dom.RemoveModeStyleSheets();
            dom.EnsureStylesheetLinks(cssFileNames);

            return dom;
        }

        public virtual string StoragePageFolder => Storage.FolderPath;

        private HtmlDom GetErrorDom(string extraMessages = "")
        {
            var builder = new StringBuilder();
            builder.Append(
                "<html><head><meta charset=\"UTF-8\" /></head><body style='font-family:arial,sans; background-color:white'>"
            );

            builder.AppendLine(
                Storage != null
                    ? Storage.GetBrokenBookRecommendationHtml()
                    : BookStorage.GenericBookProblemNotice
            );

            // Often GetBrokenBookRecommendation and FatalErrorDescription both come from _storage.ErrorMessagesHtml.
            // Try not to say the same thing twice.
            if (
                FatalErrorDescription != null && !builder.ToString().Contains(FatalErrorDescription)
            )
                builder.Append(FatalErrorDescription);

            builder.Append("<p>" + WebUtility.HtmlEncode(extraMessages) + "</p>");

            if (Storage.ErrorAllowsReporting)
            {
                var message = LocalizationManager.GetString(
                    "Errors.ReportThisProblemButton",
                    "Report this problem to Bloom Support"
                );
                // The easiest way to get something in this generated DOM to actually do something back in C#
                // is to embed an inline click action which posts to our fileserver directly.
                var url =
                    BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                    + "api/problemReport/unreadableBook";
                // No single quotes allowed in this string!
                var onClick =
                    "const Http = new XMLHttpRequest(); const url=\""
                    + url
                    + "\"; Http.open(\"POST\", url); Http.send();";
                builder.Append("<button onClick='" + onClick + "'>" + message + "</button>");
            }

            builder.Append("</body></html>");

            return new HtmlDom(builder.ToString());
        }

        private bool IsDownloaded => FolderPath.StartsWith(BookDownload.DownloadFolder);

        // BL-2678: we want the user to be able to delete troublesome/no longer needed books
        // downloaded from BloomLibrary.org
        public virtual bool CanDelete => IsSaveable || IsDownloaded;

        /// <summary>
        /// In the Bloom app, only one collection at a time is editable; that's the library they opened.
        /// All the other collections of templates, shells, etc., are not editable.
        /// Everything special we want to do about books in this collection we do NOT want to do if
        /// the book is in some error state, so we only return true if that is false.
        /// </summary>
        public virtual bool IsInEditableCollection
        {
            get
            {
                if (BookInfo == null || !BookInfo.IsInEditableCollection)
                    return false;
                return !HasFatalError;
            }
        }

        /// <summary>
        /// True if changes to the book may currently be saved. This includes the book being checked out
        /// if it is in a team collection, and by intent should include any future requirements.
        /// </summary>
        /// <remarks>Making this virtual allows tests to mock it.</remarks>
        public virtual bool IsSaveable => IsInEditableCollection && BookInfo.IsSaveable;

        /// <summary>
        /// First page in the book (or null if there are none)
        /// </summary>
        public IPage FirstPage => GetPages().FirstOrDefault();

        public IPage GetPageByIndex(int pageIndex)
        {
            // index must be >= 0
            if (pageIndex < 0)
                return null;

            // index must be less than the number of pages
            var pages = GetPages().ToList();
            if (pages.Count <= pageIndex)
                return null;

            return pages[pageIndex];
        }

        string _cachedTemplateKey;
        Book _cachedTemplateBook;

        public Book FindTemplateBook()
        {
            Guard.AgainstNull(_templateFinder, "_templateFinder");
            if (!IsInEditableCollection)
                return null; // won't be adding pages, don't need source of templates
            string templateKey = PageTemplateSource;

            Book book = null;
            if (!String.IsNullOrEmpty(templateKey))
            {
                if (templateKey.ToLowerInvariant() == "basicbook") //catch this pre-beta spelling with no space
                    templateKey = "Basic Book";
                // Template was renamed for 3.8 (and needs to end in Template, see PageTemplatesApi.GetBookTemplatePaths)
                if (templateKey.ToLowerInvariant() == "arithmetic")
                    templateKey = "Arithmetic Template";
                // We can assume that a book's "TemplateBook" does not change over time.  To be even safer,
                // we'll add a check for the same "TemplateKey" to allow reusing a cached "TemplateBook".
                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3782.
                if (templateKey == _cachedTemplateKey && _cachedTemplateBook != null)
                    return _cachedTemplateBook;
                // Find the template for this book.
                book = _templateFinder.FindAndCreateTemplateBookFromDerivative(this);
                _cachedTemplateBook = book;
                _cachedTemplateKey = templateKey;
            }
            return book;
        }

        //This is the set of pages that we show first in the Add Page dialog.
        public string PageTemplateSource
        {
            get { return OurHtmlDom.GetMetaValue("pageTemplateSource", ""); }
            set { OurHtmlDom.UpdateMetaElement("pageTemplateSource", value); }
        }

        public virtual HtmlDom OurHtmlDom => Storage.Dom;

        public virtual XmlDocument RawDom => OurHtmlDom.RawDom;

        // Tests can run without ever setting Storage.  This check is currently enough for them to work.
        public virtual string FolderPath => Storage?.FolderPath;

        private HtmlDom _previewDom;

        public virtual HtmlDom GetPreviewHtmlFileForWholeBook()
        {
            //we may already know we have an error (we might not discover until later)
            if (HasFatalError)
            {
                return GetErrorDom();
            }
            if (!Storage.GetLooksOk())
            {
                return GetErrorDom(Storage.GetValidateErrors());
            }
            if (_previewDom != null)
            {
                //Console.WriteLine("DEBUG GetPreviewHtmlFileForWholeBook(): using cached _previewDOM");
                return _previewDom;
            }

            // We want to use exactly the stylesheets that are already there, especially if we
            // are previewing a book we can't EnsureUpToDate().
            var previewDom = Storage.GetRelocatableCopyOfDom(false);
            // Earlier code also added origami.css, but I don't know any reason to add it now
            // in the unlikely event it isn't already loaded. We want the preview to look just
            // like the HTML would look if we opened the file.
            previewDom.EnsureStylesheetLinks("previewMode.css");

            // Older code (pre-6.0) called UpdateContentLanguageClasses() here with code that chose the
            // collection L1 language if the book has it, otherwise, the book's own L1. That now results
            // in a preview that may lack a title. Not really sure why, it may
            // be something to do with our previously bringing the selected book partly up to date.
            // For now, we'll just leave the preview showing whatever language the HTML says to.

            AddPreviewJavascript(previewDom);
            previewDom.AddPublishClassToBody("preview");

            SandboxActivityIframesToPreventFocus(previewDom);

            // Not needed for preview mode, so just remove them to reduce memory usage.
            PreventVideoAutoLoad(previewDom);
            RemoveImageResolutionMessageAndAddMissingImageMessage(previewDom);

            // Preview may need fullBleed markup to show pages correctly.
            InsertFullBleedMarkup(previewDom.Body);

            _previewDom = previewDom;
            return previewDom;
        }

        /// <summary>
        /// Activity pages need to run scripts even in preview to show their start page, but we don't
        /// want them to be able to set the focus.  Active Presenter is one example of an activity
        /// builder that has code on a timer loop that continually sets the focus to one of its elements.
        /// This effectively prevents users from being able to rename the book via the Book Rename menu
        /// item.
        /// Other activity builders may have the same problem, but we haven't encountered them yet.
        /// But some activity builders use WebSQL which requires the sandbox to opened up enough to
        /// allow the focus calls to leak through.  Otherwise, the preview displays a disconcerting
        /// popup message about the data storage not being available.  So for now, we sandbox only
        /// Active Presenter generated activity iframes to prevent focus.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-12598.
        /// A proposed feature policy would apparently do exactly what we want here, but it's still
        /// waiting four years after being proposed: https://chromestatus.com/feature/5179186249465856.
        /// </remarks>
        private void SandboxActivityIframesToPreventFocus(HtmlDom previewDom)
        {
            foreach (
                var iframe in previewDom
                    .SafeSelectNodes(
                        "//div[@data-activity='iframe']//iframe[starts-with(@src,'activities/')]"
                    )
                    .Cast<XmlElement>()
            )
            {
                var src = iframe.GetAttribute("src");
                // The fileName might be URL encoded.
                var path = UrlPathString.GetFullyDecodedPath(FolderPath, ref src);
                if (RobustFile.Exists(path))
                {
                    var html = RobustFile.ReadAllText(path);
                    if (html.Contains("ActivePresenter"))
                        iframe.SetAttribute("sandbox", "allow-scripts");
                }
            }
        }

        /// <summary>
        /// Force a full run of the book up to date process, even if we've already done it.
        /// This is not very wasteful if we know we're working on a new book or a newly made copy.
        /// Otherwise, it should only be used if we've changed something radical, like branding
        /// or xmatter, and want to make sure everything is consistent with that. (A lot of what this
        /// does is really migration from earlier versions of Bloom, and does not need to be
        /// done every time. A useful optimization would be to move such things into
        /// methods that use the maintenanceLevel to know whether they need doing.)
        /// </summary>
        public void BringBookUpToDate(IProgress progress, bool forCopyOfUpToDateBook = false)
        {
            UpToDate = false; // force a full update
            EnsureUpToDate(progress, forCopyOfUpToDateBook);
        }

        /// <summary>
        /// Make any needed changes to make a book which might have come from an old version of Bloom
        /// consistent with the current data model. Also makes sure it has the current XMatter
        /// and a folder name consistent with its title (unless folder name has been overridden).
        /// </summary>
        public void EnsureUpToDate(IProgress progress = null, bool forCopyOfUpToDateBook = false)
        {
            if (progress == null)
                progress = new NullProgress();
            if (UpToDate)
                return;
            if (!IsSaveable)
            {
                NonFatalProblem.Report(
                    ModalIf.Alpha,
                    PassiveIf.All,
                    "Bloom attempted to update a book which cannot currently be saved: "
                        + FolderPath
                );
                return;
            }
            _pagesCache = null;
            string oldMetaData = string.Empty;

            // This does its own check for whether it needs to be done, and updates the state
            // after doing anything it needs to.
            EnsureUpToDateMemory(progress);

            Storage.MigrateToMediaLevel1ShrinkLargeImages();

            Storage.CleanupUnusedSupportFiles(forCopyOfUpToDateBook);

            Save();
            UpToDate = true;

            _bookRefreshEvent?.Raise(this);
        }

        private void VerifyLayout(HtmlDom dom)
        {
            SetLayout(Layout.FromDom(dom, Layout.A5Portrait)); // if layout is not recognized, default to A5Portrait
        }

        private void BringBookInfoUpToDate(string oldMetaData)
        {
            if (oldMetaData.Contains("readerToolsAvailable"))
            {
                var newMetaString = oldMetaData.Replace("readerToolsAvailable", "toolboxIsOpen");
                var newMetaData = BookMetaData.FromString(newMetaString);
                BookInfo.ToolboxIsOpen = newMetaData.ToolboxIsOpen;
            }
            BookInfo.CountryName = CollectionSettings.Country;
            BookInfo.ProvinceName = CollectionSettings.Province;
            BookInfo.DistrictName = CollectionSettings.District;

            // Propagate the IsFolio value determined by the Book class (which determines it from the HTML file)
            // into BookInfo (which represents the meta.json file).
            // The version in Book.cs should be the ultimate source of truth, but it's handy to have BookInfo have a copy
            // of it too because BookInfo's meta.json is faster to parse than the book's HTML file.
            // This helps out the AddChildBookContentsToFolio() method.
            BookInfo.IsFolio = IsFolio;
        }

        /// <summary>
        /// Fix errors that users have encountered.
        /// 1) duplication of language div elements inside of translationGroup divs.
        /// 2) duplication of audio id values in the book.
        /// 3) improper license change
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-6923.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-9503 and several other issues.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-11903.
        /// </remarks>
        private void FixErrorsEncounteredByUsers(HtmlDom bookDOM)
        {
            // Fix bug reported in BL-6923: duplicate language div elements inside translationGroup divs.
            foreach (
                XmlElement groupElement in bookDOM.Body.SafeSelectNodes(
                    "descendant::*[contains(@class,'bloom-translationGroup')]"
                )
            )
            {
                // Even though the user only had duplicate vernacular divs, let's check all the book
                // languages just to be safe.
                foreach (var lang in _bookData.GetAllBookLanguageCodes())
                    TranslationGroupManager.FixDuplicateLanguageDivs(groupElement, lang);
            }
            // Fix bug reported in BL-9503 and several other issues: duplicate audio ids.
            // This does not need to be fixed for preview, and in fact can cause a warning
            // dialog to pop up behind the splash screen if it is fixed there.  (and will
            // fix things again with a second warning dialog when the book is edited or
            // published.)
            if (bookDOM != OurHtmlDom)
                return;
            var idSet = new HashSet<string>();
            _audioFilesCopiedForDuplication = 0;
            FixDuplicateAudioIdsInDataDiv(bookDOM, idSet);
            var audioFilesCopiedForDataDiv = _audioFilesCopiedForDuplication;
            FixDuplicateAudioIdInBookPages(bookDOM, idSet);
            if (_audioFilesCopiedForDuplication > 0)
            {
                // Inform user of need to rerecord audio.  (If no audio files got copied, then there's nothing to rerecord, just lots to record!)
                // If the only files copied were found in the data-div, then restrict the message for where to rerecord.
                var shortMsg =
                    "There was a problem with recordings in this Talking Book, which was caused by a bug in an older version of Bloom."
                    + (
                        (audioFilesCopiedForDataDiv == _audioFilesCopiedForDuplication)
                            ? " We have fixed the book, but now you will need to review the Cover, Title, and Credits pages to see if you need to record some text again."
                            : " We have fixed the book, but now you will need to review all of your recordings in this book to see if you need to record some text again."
                    )
                    + " We are very sorry for our mistake and the inconvenience this might cause you.";
                NonFatalProblem.Report(
                    ModalIf.All,
                    PassiveIf.None,
                    shortMsg,
                    null,
                    null,
                    false,
                    true
                );
            }
            // Fix bug reported in BL-8599 (and possibly other issues): missing audio ids.
            FixMissingAudioIdsInDataDiv(bookDOM);
            FixMissingAudioIdsInBookPages(bookDOM);
            // Fix bug reported in BL-10786: coverImage multiply HTML-encoded.
            FixExcessiveHTMLEncodingOfCoverImage(bookDOM);
            // Fix bug reported in BL-11093: improper change of license.
            FixImproperLicenseChange(bookDOM, _bookData);
            // Fix temporarily bad userModifiedStyles rules in Uzbek xmatter (BL-12047)
            FixBadFontFamilyFromXMatter(bookDOM);
            // Fix bug reported in BL-12287: improper audio highlighting of padded sentences.
            FixImproperAudioHighlightingOfPaddedSentences(bookDOM);
        }

        /// <summary>
        /// Our initial Uzbek xmatter had a bad font-family rule in the userModifiedStyles. (BL-12047)
        /// A couple of books got uploaded and the Harvester couldn't figure out what to do about fonts.
        /// The xmatter was repaired, so new books will be okay, but we need to fix the old ones.
        /// </summary>
        /// <param name="bookDOM"></param>
        private void FixBadFontFamilyFromXMatter(HtmlDom bookDOM)
        {
            var userStyles = bookDOM.SelectSingleNode("//head/style[@title='userModifiedStyles']");
            if (userStyles == null)
                return; // shouldn't happen, but paranoia sometimes pays off, especially in running tests.
            var styleContents = userStyles.InnerXml;
            userStyles.InnerXml = styleContents.Replace(
                "font-family: \"Aileron; Arial\"",
                "font-family: Aileron, Arial"
            );
        }

        private void FixImproperAudioHighlightingOfPaddedSentences(HtmlDom bookDOM)
        {
            var userStylesNode = GetOrCreateUserModifiedStyleElementFromStorage(bookDOM.Head);
            if (userStylesNode == null)
                return; // shouldn't happen, but paranoia sometimes pays off, especially in running tests.
            HtmlDom.AddMissingAudioHighlightRules(userStylesNode);
        }

        /// <summary>
        /// Go through the #bloomDataDiv looking for duplicate audio ids.  If any are found, fix them
        /// and at the end update the DOM to reflect the fixes.
        /// </summary>
        private void FixDuplicateAudioIdsInDataDiv(HtmlDom bookDOM, HashSet<string> idSet)
        {
            var dataDiv = bookDOM.SelectSingleNode("//div[@id='bloomDataDiv']");
            if (dataDiv == null)
                return; // shouldn't happen, but paranoia sometimes pays off, especially in running tests.
            var nodes = dataDiv
                .SafeSelectNodes("(.//div|.//span)[@id and contains(@class,'audio-sentence')]")
                .Cast<XmlNode>()
                .ToList();
            var duplicateAudioIdsFixed = 0;
            foreach (var audioElement in nodes)
            {
                var id = (audioElement as XmlElement).GetOptionalStringAttribute("id", null);
                var isNewlyAdded = idSet.Add(id);
                if (!isNewlyAdded)
                {
                    var newId = FixDuplicateAudioId(audioElement, id);
                    idSet.Add(newId);
                }
            }
            // OK, now fix all the places any duplicates were used in the book's pages.
            if (duplicateAudioIdsFixed > 0)
                _bookData.SynchronizeDataItemsThroughoutDOM();
        }

        private void FixDuplicateAudioIdInBookPages(HtmlDom bookDOM, HashSet<string> idSet)
        {
            foreach (var page in bookDOM.GetPageElements())
            {
                var nodeList = HtmlDom.SelectChildNarrationAudioElements(page, true);
                for (int i = 0; i < nodeList.Count; ++i)
                {
                    var node = nodeList.Item(i);
                    if (node.Attributes == null)
                        continue; // No id exists if no attributes exist.
                    var id = node.GetOptionalStringAttribute("id", null);
                    if (id == null)
                        continue;
                    if (HtmlDom.IsNodePartOfDataBookOrDataCollection(node))
                        continue;
                    var isNewlyAdded = idSet.Add(id);
                    if (!isNewlyAdded)
                    {
                        // Uh-oh. That means an element like this already exists!?
                        FixDuplicateAudioId(node, id);
                    }
                }
            }
        }

        private string FixDuplicateAudioId(XmlNode node, string id)
        {
            // Create a new id value, and copy the audio file if it exists.
            var newId = HtmlDom.SetNewHtmlIdValue(node as XmlElement);
            if (!String.IsNullOrEmpty(FolderPath))
            {
                var oldAudioPath = Path.Combine(FolderPath, "audio", id + ".mp3");
                if (RobustFile.Exists(oldAudioPath))
                {
                    var newAudioPath = Path.Combine(FolderPath, "audio", newId + ".mp3");
                    RobustFile.Copy(oldAudioPath, newAudioPath);
                    ++_audioFilesCopiedForDuplication;
                }
            }
            var msg =
                $"Duplicate GUID {id} on recordable with text \"{node.InnerText.Trim()}\" changed to {newId}.";
            Logger.WriteEvent(msg);
            return newId;
        }

        private void FixMissingAudioIdsInDataDiv(HtmlDom bookDOM)
        {
            var dataDiv = bookDOM.SelectSingleNode("//div[@id='bloomDataDiv']");
            if (dataDiv == null)
                return; // shouldn't happen, but paranoia sometimes pays off, especially in running tests.
            var nodeList = dataDiv
                .SafeSelectNodes("(.//div|.//span)[not(@id) and contains(@class,'audio-sentence')]")
                .Cast<XmlElement>()
                .ToList();
            foreach (var node in nodeList)
                HtmlDom.SetNewHtmlIdValue(node);
            // Fix all the places where the data-div original is missing an id value.
            if (nodeList.Count > 0)
            {
                _bookData.SynchronizeDataItemsThroughoutDOM();
                var msg = $"Fixed {nodeList.Count} missing audio ids in the book's data div.";
                Logger.WriteEvent(msg);
            }
        }

        private void FixMissingAudioIdsInBookPages(HtmlDom bookDOM)
        {
            var idsAdded = 0;
            foreach (var page in bookDOM.GetPageElements())
            {
                var nodeList = page.SafeSelectNodes(
                        "(.//div|.//span)[not(@id) and contains(@class,'audio-sentence')]"
                    )
                    .Cast<XmlElement>()
                    .ToList();
                foreach (var node in nodeList)
                    HtmlDom.SetNewHtmlIdValue(node);
                idsAdded += nodeList.Count;
            }
            if (idsAdded > 0)
            {
                var msg = $"Fixed {idsAdded} missing audio ids in the book's pages.";
                Logger.WriteEvent(msg);
            }
        }

        private void FixExcessiveHTMLEncodingOfCoverImage(HtmlDom bookDOM)
        {
            var coverImgElt = bookDOM
                .SafeSelectNodes("//div[@id='bloomDataDiv']/div[@data-book='coverImage']")
                .Cast<XmlElement>()
                .FirstOrDefault();
            if (coverImgElt == null)
                return;
            var coverImageFileName = coverImgElt.InnerText;
            if (string.IsNullOrEmpty(coverImageFileName))
                return;
            coverImageFileName = coverImageFileName.Trim();
            // The fileName might be URL encoded.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3901.
            var coverImagePath = UrlPathString.GetFullyDecodedPath(
                StoragePageFolder,
                ref coverImageFileName
            );
            while (coverImagePath.Contains("&amp;") && !RobustFile.Exists(coverImagePath))
                coverImagePath = HttpUtility.HtmlDecode(coverImagePath);
            var filename = Path.GetFileName(coverImagePath);
            if (filename != coverImageFileName)
            {
                // Note that setting InnerText or calling SetAttribute automatically XML-encodes any necessary
                // characters.  Getting InnerText or calling GetAttribute reverses the changes, if any.
                coverImgElt.InnerText = filename;
                coverImgElt.SetAttribute("src", filename);
                var localizedFormatString = LocalizationManager.GetString(
                    "EditTab.Image.AltMsg",
                    "This picture, {0}, is missing or was loading too slowly."
                );
                var altValue = String.Format(localizedFormatString, filename);
                coverImgElt.SetAttribute("alt", altValue);
            }
        }

        private void FixImproperLicenseChange(HtmlDom bookDom, BookData bookData)
        {
            var originalMetadata = BookCopyrightAndLicense.GetOriginalMetadata(bookDom, bookData);
            if (!BookCopyrightAndLicense.IsDerivative(originalMetadata))
                return;
            var originalLicense = originalMetadata.License;
            if (originalLicense == null)
                return; // just to be safe
            var keepOriginal =
                originalLicense is NullLicense
                || // must preserve "contact copyright holder"
                originalLicense is CustomLicense
                || // must preserve custom licenses
                (
                    originalLicense is CreativeCommonsLicense
                    && (originalLicense as CreativeCommonsLicense).DerivativeRule
                        == CreativeCommonsLicense.DerivativeRules.NoDerivatives
                );
            if (!keepOriginal)
                return;
            var metadata = BookCopyrightAndLicense.GetMetadata(bookDom, bookData);
            var license = metadata.License;
            if (AreLicensesDifferent(license, originalLicense))
            {
                metadata.License = originalLicense;
                BookCopyrightAndLicense.SetMetadata(
                    metadata,
                    bookDom,
                    this.FolderPath,
                    bookData,
                    false
                );
            }
        }

        private bool AreLicensesDifferent(LicenseInfo license, LicenseInfo originalLicense)
        {
            return license.Token != originalLicense.Token
                || license.RightsStatement != originalLicense.RightsStatement
                || license.Url != originalLicense.Url;
        }

        class GuidAndPath
        {
            public string Guid; // replacement guid
            public string Path; // where to find file, relative to root templates directory
        }

        private static Dictionary<string, GuidAndPath> _pageMigrations;

        /// <summary>
        /// Get (after initializing, if necessary) the dictionary mapping page IDs we know how to migrate
        /// onto the ID and file location of the page we want to update it to.
        /// Paths are relative to root templates directory
        /// </summary>
        private static Dictionary<string, GuidAndPath> PageMigrations
        {
            get
            {
                if (_pageMigrations == null)
                {
                    _pageMigrations = new Dictionary<string, GuidAndPath>();
                    // Basic Book
                    _pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398382"] = new GuidAndPath()
                    {
                        Guid = BasicTextAndImageGuid,
                        Path = "Basic Book/Basic Book.html"
                    };
                    _pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398383"] = new GuidAndPath()
                    {
                        Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398383",
                        Path = "Basic Book/Basic Book.html"
                    }; // Picture in Middle
                    _pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398384"] = new GuidAndPath()
                    {
                        Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398384",
                        Path = "Basic Book/Basic Book.html"
                    }; // Picture on Bottom
                    _pageMigrations["5dcd48df-e9ab-4a07-afd4-6a24d0398385"] = new GuidAndPath()
                    {
                        Guid = JustPictureGuid,
                        Path = "Basic Book/Basic Book.html"
                    };
                    _pageMigrations["d31c38d8-c1cb-4eb9-951b-d2840f6a8bdb"] = new GuidAndPath()
                    {
                        Guid = JustTextGuid,
                        Path = "Basic Book/Basic Book.html"
                    };
                    _pageMigrations["FD115DFF-0415-4444-8E76-3D2A18DBBD27"] = new GuidAndPath()
                    {
                        Guid = "aD115DFF-0415-4444-8E76-3D2A18DBBD27",
                        Path = "Basic Book/Basic Book.html"
                    }; // Picture & Word
                    // Big book [see commit 7bfefd0dbc9faf8930c4926b0156e44d3447e11b]
                    _pageMigrations["AF708725-E961-44AA-9149-ADF66084A04F"] = new GuidAndPath()
                    {
                        Guid = JustPictureGuid,
                        Path = "Big Book/Big Book.html"
                    };
                    _pageMigrations["D9A55EB6-43A8-4C6A-8891-2C1CDD95772C"] = new GuidAndPath()
                    {
                        Guid = JustTextGuid,
                        Path = "Big Book/Big Book.html"
                    };
                    // Decodable reader [see commit 7bfefd0dbc9faf8930c4926b0156e44d3447e11b]
                    _pageMigrations["f95c0314-ce47-4b47-a638-06325ad1a963"] = new GuidAndPath()
                    {
                        Guid = BasicTextAndImageGuid,
                        Path = "Decodable Reader/Decodable Reader.html"
                    };
                    _pageMigrations["c0847f89-b58a-488a-bbee-760ce4a13567"] = new GuidAndPath()
                    {
                        Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398383",
                        Path = "Decodable Reader/Decodable Reader.html"
                    }; // Picture in Middle
                    _pageMigrations["f99b252a-26b1-40c8-b543-dbe0b05f08a5"] = new GuidAndPath()
                    {
                        Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398384",
                        Path = "Decodable Reader/Decodable Reader.html"
                    }; // Picture on Bottom
                    _pageMigrations["c506f278-cb9f-4053-9e29-f7a9bdf64445"] = new GuidAndPath()
                    {
                        Guid = JustPictureGuid,
                        Path = "Decodable Reader/Decodable Reader.html"
                    };
                    _pageMigrations["e4ff6195-b0b6-4909-8025-4424ee9188ea"] = new GuidAndPath()
                    {
                        Guid = JustTextGuid,
                        Path = "Decodable Reader/Decodable Reader.html"
                    };
                    _pageMigrations["bd85f898-0a45-45b3-8e34-faaac8945a0c"] = new GuidAndPath()
                    {
                        Guid = "aD115DFF-0415-4444-8E76-3D2A18DBBD27",
                        Path = "Decodable Reader/Decodable Reader.html"
                    }; // Picture & Word
                    // Leveled reader [see commit 7bfefd0dbc9faf8930c4926b0156e44d3447e11b]
                    _pageMigrations["e9f2142b-f135-4bcd-9123-5a2623f5302f"] = new GuidAndPath()
                    {
                        Guid = BasicTextAndImageGuid,
                        Path = "Leveled Reader/Leveled Reader.html"
                    };
                    _pageMigrations["c5aae471-f801-4c5d-87b7-1614d56b0c53"] = new GuidAndPath()
                    {
                        Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398383",
                        Path = "Leveled Reader/Leveled Reader.html"
                    }; // Picture in Middle
                    _pageMigrations["a1f437fe-c002-4548-af02-fe84d048b8fc"] = new GuidAndPath()
                    {
                        Guid = "adcd48df-e9ab-4a07-afd4-6a24d0398384",
                        Path = "Leveled Reader/Leveled Reader.html"
                    }; // Picture on Bottom
                    _pageMigrations["d7599aa7-f35c-4029-8aa2-9afda870bcfa"] = new GuidAndPath()
                    {
                        Guid = JustPictureGuid,
                        Path = "Leveled Reader/Leveled Reader.html"
                    };
                    _pageMigrations["d93a28c6-9ff8-4f61-a820-49093e3e275b"] = new GuidAndPath()
                    {
                        Guid = JustTextGuid,
                        Path = "Leveled Reader/Leveled Reader.html"
                    };
                    _pageMigrations["a903467a-dad2-4767-8be9-54336cae7731"] = new GuidAndPath()
                    {
                        Guid = "aD115DFF-0415-4444-8E76-3D2A18DBBD27",
                        Path = "Leveled Reader/Leveled Reader.html"
                    }; // Picture & Word
                }
                return _pageMigrations;
            }
        }

        /// <summary>
        /// Bring the page up to date. Currently this is used to switch various old page types to new versions
        /// based on Custom Page (so they can actually be customized).
        /// </summary>
        /// <param name="page"></param>
        public void BringPageUpToDate(XmlElement page)
        {
            var lineageAttr = page.Attributes["data-pagelineage"];
            if (lineageAttr == null)
                return;
            var lineage = lineageAttr.Value;
            var originalTemplateGuid = lineage;
            int index = lineage.IndexOf(";", StringComparison.InvariantCulture);
            if (index >= 0)
                originalTemplateGuid = lineage.Substring(0, index);
            GuidAndPath updateTo;
            if (!PageMigrations.TryGetValue(originalTemplateGuid, out updateTo))
                return; // Not one we want to migrate. Possibly already done, or one we don't want to convert, or created in the field...
            var layoutOfThisBook = GetLayout();
            var bookPath = BloomFileLocator.GetFactoryBookTemplateDirectory(updateTo.Path);
            var templateDoc = XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false);
            var newPage = (XmlElement)
                templateDoc.SafeSelectNodes("//div[@id='" + updateTo.Guid + "']")[0];
            var classesToDrop = new[]
            {
                "imageWholePage",
                "imageOnTop",
                "imageInMiddle",
                "imageOnBottom",
                "textWholePage",
                "pictureAndWordPage"
            };
            HtmlDom.MergeClassesIntoNewPage(page, newPage, classesToDrop);
            SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPage, layoutOfThisBook);
            foreach (XmlAttribute attr in page.Attributes)
            {
                if (newPage.HasAttribute(attr.Name))
                {
                    // don't overwrite things specified in template, typically class, id, data-page
                    // Otherwise copy everything, even things we don't know about at the time of writing this
                    // code; if we add a new attribute that gets set before this code runs, we'd like to transfer
                    // it to the new element without having to remember to update this code.
                    continue;
                }

                newPage.SetAttribute(attr.Name, attr.Value);
            }
            bool dummy;
            OurHtmlDom.MigrateEditableData(
                page,
                newPage,
                lineage.Replace(originalTemplateGuid, updateTo.Guid),
                true,
                out dummy
            );
        }

        private object _updateLock = new object();
        private bool _doingBookUpdate = false;
        private HtmlDom _domBeingUpdated = null;
        private string _updateStackTrace = null;

        /// <summary>
        /// As the bloom format evolves, including structure and classes and other attributes, this
        /// makes changes to old books. We'd like it to be very fast, because currently we don't have
        /// a real way to detect the need for migration. So we do it all the time.
        ///
        /// Yes, we have format version number, but, for example, one overhaul of the common xmatter
        /// html introduced a new class, "frontCover". Hardly enough to justify bumping the version number
        /// and making older Blooms unable to read new books. But because this is run, the xmatter will be
        /// migrated to the new template.
        ///
        /// We also have maintenanceLevel in the book metadata. This can be used (in methods like
        /// MigrateToMediaLevel1ShrinkLargeImages, called below) to do longer-running things that
        /// definitely only need doing once. Probably it would be good for more of the work done here
        /// to be moved into such methods.
        ///
        /// The code here was historically used, roughly, to bring a book as far up to date as possible
        /// without changing files in the book folder. In the course of switching to the Appearance/Theme
        /// system, we realized that isn't really feasible. So now we either bring a book up to date or
        /// don't, and we only bring it up to date if we can save it. This method should only be called
        /// by the main Update method. We may inline it at some point, but for now, that would make
        /// seeing changes more difficult.
        ///
        /// If we need to reinstate the ability to bring a book up to date without saving it, JohnT's fork
        /// has two branches, AppearanceChanges3 and AppearanceChanges4. The changes made going from 3
        /// to 4 basically remove the cache, so making the opposite change should be about right for
        /// restoring it. Be careful, though...the AppearanceChanges3 version would wrongly save
        /// editMode.css into the book folder. The fix getting rid of DeterminePublishability() is worth
        /// keeping (though we'll no longer need CannotPublishWithoutCheckout).
        /// SaveAsBloomSourceFile should draw from the cache if we allow it to save a book that isn't
        /// all the way up to date.
        /// </summary>
        private void EnsureUpToDateMemory(IProgress progress)
        {
            Guard.Against(
                !IsSaveable,
                "EnsureUpToDateMemory should only now be called for Saveable books as part of the main book updating"
            );
            string oldMetaData = "";
            if (RobustFile.Exists(BookInfo.MetaDataPath))
            {
                oldMetaData = RobustFile.ReadAllText(BookInfo.MetaDataPath); // Have to read this before other migration overwrites it.
            }

            RemoveImgTagInDataDiv(OurHtmlDom);
            RemoveCkeEditorResidue(OurHtmlDom);
            if (Title.Contains("allowSharedUpdate"))
            {
                // Original version of this code that suffers BL_3166
                EnsureUpToDateMemoryUnprotected(progress);
            }
            else
            {
                // New version that we hope prevents BL_3166
                if (_doingBookUpdate)
                {
                    // Nag user if we appear to be updating the same DOM (OurHtmlDom or the previewDom which is always new)
#if DEBUG
                    if (SIL.PlatformUtilities.Platform.IsWindows) // hangs on Linux
                        MessageBox.Show(
                            "Caught Bloom doing two updates at once! Possible BL-3166 is being prevented"
                        );
#endif
                    Console.WriteLine(
                        "WARNING: Bloom appears to be updating the same DOM twice at the same time! (BL-3166??)"
                    );
                    Console.WriteLine("Current StackTrace: {0}", Environment.StackTrace);
                    Console.WriteLine("Prior StackTrace: {0}", _updateStackTrace);
                }
                lock (_updateLock)
                {
                    _domBeingUpdated = OurHtmlDom;
                    _updateStackTrace = Environment.StackTrace;
                    _doingBookUpdate = true;
                    EnsureUpToDateMemoryUnprotected(progress);
                    _doingBookUpdate = false;
                    _domBeingUpdated = null;
                    _updateStackTrace = null;
                }
            }
            RemoveObsoleteSoundAttributes(OurHtmlDom);
            Storage.RepairEmptyPages();
            RemoveObsoleteImageAttributes(OurHtmlDom);
            BringBookInfoUpToDate(oldMetaData);
            FixErrorsEncounteredByUsers(OurHtmlDom);
            AddReaderBodyAttributes(OurHtmlDom);
            AddLanguageAttributesToBody(OurHtmlDom);
            OurHtmlDom.Body.SetAttribute(
                "data-bookshelfurlkey",
                this.CollectionSettings.DefaultBookshelf
            );
            //BookInfo.SettingsUpdated();// REVIEW: Where should this go? Stuck in here just so it gets written at least once
            if (IsTemplateBook)
            {
                // this will turn on rules in previewMode.css that show the structure of the template and names of pages
                HtmlDom.AddClassToBody(OurHtmlDom.RawDom, "template");
            }
            else
            {
                // It might be there when not appropriate, because this is a new book created from a template,
                // or one that was created from a template in an earlier version of Bloom without this fix!
                // Make sure it's not.
                HtmlDom.RemoveClassFromBody(OurHtmlDom.RawDom, "template");
            }

            // Following code (roughly) was in the main BBUD but we were doing it when the book gets selected.
            // We've now simplified to only three states, so it makes most sense to do everything we can
            // that doesn't involve writing files here.
            progress.WriteStatus("Updating pages...");
            foreach (
                XmlElement pageDiv in OurHtmlDom.SafeSelectNodes(
                    "//body/div[contains(@class, 'bloom-page')]"
                )
            )
            {
                BringPageUpToDate(pageDiv);
            }

            // Most of this probably only needs doing if we're actually about to edit it.
            // But earlier versions did it whenever we did BBUD on a book in the editable collection,
            // and I'm not game to change it.
            try
            {
                ImageUpdater.UpdateAllHtmlDataAttributesForAllImgElements(
                    FolderPath,
                    OurHtmlDom,
                    progress
                );
            }
            catch (UnauthorizedAccessException e)
            {
                BookStorage.ShowAccessDeniedErrorReport(e);
            }

            VerifyLayout(OurHtmlDom); // make sure we have something recognizable for layout

            // These (and maybe other stuff) could be avoided when updating a copy of an up-to-date book,
            // but it takes almost no time when the book IS already up-to-date.
            // These methods work with the same book metadata to determine what migration has
            // already been done, so they must be called in exactly this order.
            Storage.MigrateMaintenanceLevels();
            Storage.MigrateToMediaLevel1ShrinkLargeImages();
            Storage.MigrateToLevel2RemoveTransparentComicalSvgs();
            Storage.MigrateToLevel3PutImgFirst();
            Storage.MigrateToLevel4UseAppearanceSystem();

            // Make sure the appearance settings have checked the current state of the css files.
            // This should be done before UpdateSupportFiles, because this can affect what files
            // are copied to the book folder.
            var cssFiles = this.Storage.GetCssFilesToCheckForAppearanceCompatibility();
            BookInfo.AppearanceSettings.CheckCssFilesForCompatibility(
                cssFiles,
                Storage.LegacyThemeCanBeUsed
            );

            // Ensure that the appearance settings are up to date with the current branding (if any).
            BookInfo.AppearanceSettings.UpdateFromOverrides(Storage.BrandingAppearanceSettings);
            BookInfo.AppearanceSettings.UpdateFromOverrides(Storage.XmatterAppearanceSettings);

            // Restore possibly messed up multilingual settings.  Also apply appearance settings
            // related to visibility of the multilingual variants.
            UpdateMultilingualSettings(OurHtmlDom);
            // Note that OurHtmlDom.FixDivOrdering must be done after UpdateMultilingualSettings,
            // because the latter method adds classes to the editable divs that are used for sorting.

            // Enhance: there's probably some case where this will get unnecessarily repeated,
            // but I think it would only be in the very rare case of repeatedly needing to update
            // a pre-4.9 book when we can't Save().
            OurHtmlDom.FixDivOrdering();

            UpdateSupportFiles();
        }

        private void AddLanguageAttributesToBody(HtmlDom bookDom)
        {
            // TODO: figure out what to do when we expand beyond three languages, if there's any reason
            // to have this at all. Searching all cs, ts, tsx, and less files, it appears we don't do
            // anything with these attributes anywhere. Nor in BloomPlayer or BloomLibrary.
            // If we do discover a purpose, we might want to add a data-M1 for the first metadata
            // language.
            // (The commit label when these were added says "so that a single branding can vary things by lang."
            // We don't appear to actually do that but it still seems like it might be useful.)
            bookDom.Body.SetAttribute("data-L1", this._bookData.Language1Tag);
            bookDom.Body.SetAttribute("data-L2", this._bookData.Language2Tag);
            bookDom.Body.SetAttribute("data-L3", this._bookData.Language3Tag);
        }

        private void AddReaderBodyAttributes(HtmlDom bookDom)
        {
            // Bloom prior to late 4.7beta had decodable and leveled reader templates without body classes, which
            // became necessary for an SIL LEAD "ABC+" xmatter. Here we add that if we can tell the book descended
            // from those templates, then add the required classes if they are missing.
            const string kDecodableParentGuid = "f0434a0b-791f-408e-b6e6-ee92f0f02f2d";
            const string kLeveledParentGuid = "ea43ce61-a752-429d-ad1a-ec282db33328";
            if (
                !bookDom.HasClassOnBody("decodable-reader-off")
                && BookInfo.BookLineage != null
                && BookInfo.BookLineage.Contains(kDecodableParentGuid)
            )
            {
                HtmlDom.AddClass(bookDom.Body, "decodable-reader");
            }
            else if (
                !bookDom.HasClassOnBody("leveled-reader-off")
                && BookInfo.BookLineage != null
                && BookInfo.BookLineage.Contains(kLeveledParentGuid)
            )
            {
                HtmlDom.AddClass(bookDom.Body, "leveled-reader");
            }

            // (Semi-Implemented)
            // Note, this doesn't get set very often. Indeed you probably have to go to a different book and come
            // back. So if we were using this to, say, set the cover color, we'd wish that the change was immediately
            // visible. At the moment, we aren't actually using these (they are for the future so that we don't have
            // a big delay if a client ever needs them). But when we need them, we're probably going to want a way
            // to make the changes as soon as the user changes the level.
            // Also, when saving, note that EditingModel.NeedToDoFullSave might want to know about
            // these values.
            bookDom.Body.SetAttribute(
                "data-decodablestage",
                this.BookInfo.MetaData.DecodableReaderStage.ToString()
            );
            bookDom.Body.SetAttribute(
                "data-leveledreaderlevel",
                this.BookInfo.MetaData.LeveledReaderLevel.ToString()
            );
        }

        // Some books got corrupted with CKE temp data, possibly before we prevented this happening when
        // pasting HTML (e.g., from Word). A typical example (BL-6050/BL-6058) is
        //  <div data-cke-hidden-sel="1" data-cke-temp="1" style="position:fixed;top:0;left:-1000px" class="bloom-contentNational2">
        //		<br>
        //	</div>
        // We want to remove this. In the example problem book, every example has both data-cke-temp and data-cke-hidden-sel
        // (both set to 1), but data-cke-temp feels like a more generic thing to look for, increasing our chances
        // of catching more junk.
        private void RemoveCkeEditorResidue(HtmlDom bookDom)
        {
            foreach (
                var problemDiv in bookDom
                    .SafeSelectNodes(".//div[@data-cke-temp]")
                    .Cast<XmlElement>()
                    .ToArray()
            )
            {
                problemDiv.ParentNode.RemoveChild(problemDiv);
            }
        }

        private void EnsureUpToDateMemoryUnprotected(IProgress progress)
        {
            // With one exception, handled below, nothing in the update process should change the license info, so save what is current before we mess with
            // anything (may fix BL-3166).
            var licenseMetadata = GetLicenseMetadata();

            progress.WriteStatus("Updating collection settings...");
            try
            {
                UpdateCollectionRelatedStylesAndSettings(OurHtmlDom);
            }
            catch (UnauthorizedAccessException e)
            {
                BookStorage.ShowAccessDeniedErrorReport(e);
                return;
            }

            progress.WriteStatus("Updating Front/Back Matter...");
            BringXmatterHtmlUpToDate(OurHtmlDom);
            RepairBrokenSmallCoverCredits(OurHtmlDom);
            RepairCoverImageDescriptions(OurHtmlDom);
            DetectAndMarkDarkCoverColor(OurHtmlDom);

            progress.WriteStatus("Repair page label localization");
            RepairPageLabelLocalization(OurHtmlDom);

            progress.WriteStatus("Repair possible messed up Questions pages and migrate classes");
            RepairQuestionsPages(OurHtmlDom);
            MigrateNonstandardClassNames(OurHtmlDom);

            // migrate our cover color to the new system
            // ToDo: when we implement this setting.
            // if (string.IsNullOrEmpty(BookInfo.AppearanceSettings.CoverColor))
            // {
            // 	BookInfo.AppearanceSettings.CoverColor = GetCoverColor();
            // }

            progress.WriteStatus("Gathering Data...");
            TranslationGroupManager.PrepareElementsInPageOrDocument(OurHtmlDom.RawDom, _bookData);
            progress.WriteStatus("Updating Data...");

            InjectStringListingActiveLanguagesOfBook();

            if (_bookData.BookIsDerivative())
                Storage.EnsureOriginalTitle();

            // The one step that can legitimately change the metadata...though current branding packs
            // will only do so if it is originally empty. So set the saved one before it and then get a new one.
            BookCopyrightAndLicense.SetMetadata(
                licenseMetadata,
                OurHtmlDom,
                FolderPath,
                _bookData,
                BookInfo.MetaData.UseOriginalCopyright
            );
            _bookData.MergeBrandingSettings(CollectionSettings.BrandingProjectKey);
            _bookData.SynchronizeDataItemsThroughoutDOM();
            licenseMetadata = GetLicenseMetadata();
            // I think we should only mess with tags if we are updating the book for real.
            var oldTagsPath = Path.Combine(Storage.FolderPath, "tags.txt");
            if (RobustFile.Exists(oldTagsPath))
            {
                ConvertTagsToMetaData(oldTagsPath, BookInfo);
                RobustFile.Delete(oldTagsPath);
            }
            BookInfo.BrandingProjectKey = CollectionSettings.BrandingProjectKey;

            // get any license info into the json and restored in the replaced front matter.
            BookCopyrightAndLicense.SetMetadata(
                licenseMetadata,
                OurHtmlDom,
                FolderPath,
                _bookData,
                BookInfo.MetaData.UseOriginalCopyright
            );

            OurHtmlDom.RemoveMetaElement(
                "bloomBookLineage",
                () => BookInfo.BookLineage,
                val => BookInfo.BookLineage = val
            );
            OurHtmlDom.RemoveMetaElement(
                "bookLineage",
                () => BookInfo.BookLineage,
                val => BookInfo.BookLineage = val
            );
            // BookInfo will always have an ID, the constructor makes one even if there is no json file.
            // To allow migration, pretend it has no ID if there is not yet a meta.json.
            OurHtmlDom.RemoveMetaElement(
                "bloomBookId",
                () => (RobustFile.Exists(BookInfo.MetaDataPath) ? BookInfo.Id : null),
                val => BookInfo.Id = val
            );

            // Title should be replicated in json
            //if (!string.IsNullOrWhiteSpace(Title)) // check just in case we somehow have more useful info in json.
            //    bookDOM.Title = Title;
            // Bit of a kludge, but there's no way to tell whether a boolean is already set in the JSON, so we fake that it is not,
            // thus ensuring that if something is in the metadata we use it.
            // If there is nothing there the default of true will survive.
            OurHtmlDom.RemoveMetaElement(
                "SuitableForMakingVernacularBooks",
                () => null,
                val => BookInfo.IsSuitableForVernacularLibrary = val == "yes" || val == "definitely"
            );

            OurHtmlDom.UpdatePageNumberAndSideClassOfPages(
                CollectionSettings.CharactersForDigitsForPageNumbers,
                IsPrimaryLanguageRtl
            );

            UpdateTextsNewlyChangedToRequiresParagraph(OurHtmlDom);

            UpdateCharacterStyleMarkup(OurHtmlDom);

            OurHtmlDom.SetImageAltAttrsFromDescriptions(_bookData.Language1.Tag);

            //we've removed and possible added pages, so our page cache is invalid
            _pagesCache = null;
        }

        const string kCustomStyles = "customCollectionStyles.css";
        public const string kOldCollectionStyles = "settingsCollectionStyles.css";

        /// <summary>
        /// Adjust several external stylesheet links and associated files.  Also adjust some book level
        /// settings (in json or html) to match current collection settings.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-7343.
        /// </remarks>
        private void UpdateCollectionRelatedStylesAndSettings(HtmlDom bookDom)
        {
            foreach (XmlElement link in bookDom.SafeSelectNodes("//link[@rel='stylesheet']"))
            {
                var fileName = link.GetStringAttribute("href");
                if (
                    fileName == kOldCollectionStyles
                    || fileName == "../" + kOldCollectionStyles
                    || fileName == "..\\" + kOldCollectionStyles
                )
                    link.SetAttribute("href", "defaultLangStyles.css");
                else if (fileName == "../" + kCustomStyles || fileName == "..\\" + kCustomStyles)
                    link.SetAttribute("href", kCustomStyles);
            }
            // Rename/remove/create files that have changed names or locations to match link href changes above.
            // Don't do this in distributed folders.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7550.
            if (!FolderPath.StartsWith(BloomFileLocator.FactoryCollectionsDirectory))
            {
                BookStorage.CssFilesThatAreObsolete.ForEach(filename =>
                {
                    var path = Path.Combine(FolderPath, filename);
                    if (RobustFile.Exists(path))
                        RobustFile.Delete(path);
                });
                if (
                    RobustFile.Exists(
                        Path.Combine(Path.GetDirectoryName(FolderPath), kOldCollectionStyles)
                    )
                )
                    RobustFile.Delete(
                        Path.Combine(Path.GetDirectoryName(FolderPath), kOldCollectionStyles)
                    );
                CreateOrUpdateDefaultLangStyles();
                // Copy files from collection to book folders to match link href changes above.
                if (
                    RobustFile.Exists(
                        Path.Combine(Path.GetDirectoryName(FolderPath), kCustomStyles)
                    )
                )
                    RobustFile.Copy(
                        Path.Combine(Path.GetDirectoryName(FolderPath), kCustomStyles),
                        Path.Combine(FolderPath, kCustomStyles),
                        true
                    );
            }
            // Update book settings from collection settings
            UpdateCollectionSettingsInBookMetaData();
        }

        /// <summary>
        /// Write the appropriate styles to the defaultLangStyles.css file.
        /// You would normally expect that we just write this afresh, but actually
        /// we want to preserve font information from languages that are no longer
        /// part of L1,L2,or L3 but are still in the book and thus may still be
        /// rendered by Bloom Player.
        /// </summary>
        private void CreateOrUpdateDefaultLangStyles()
        {
            var path = Path.Combine(FolderPath, "defaultLangStyles.css");
            bool doesAlreadyExist = RobustFile.Exists(path);
            if (Program.RunningHarvesterMode && doesAlreadyExist)
            {
                // Would overwrite, but overwrite not allowed in Harvester mode because we need to preserve the language information.
                // However, we do want to remove any font-face declarations as bloom-player provides its own, and these cause problems with
                // synthesizing font variants. See BL-12594.
                if (!WriteFontFaces)
                    RemoveFontFaceDeclarations(path);
                return;
            }

            var collectionStylesCss = CollectionSettings.GetCollectionStylesCss(false);
            var cssBuilder = new StringBuilder(collectionStylesCss);
            string existingContent = null;
            if (doesAlreadyExist)
            {
                // We want to use the current CSS from our collection, which we already added to the
                // string builder, for all the languages it contains, but keep the rules already in
                // defaultLangStyles.css for any other languages that are there.  We start by setting
                // languagesWeAlreadyHave to the languages we do NOT want to copy from defaultLangStyles.css
                // (because we have more current data about them already).
                var languagesWeAlreadyHave = new HashSet<string>
                {
                    CollectionSettings.Language1Tag,
                    CollectionSettings.Language2Tag
                };
                if (!String.IsNullOrEmpty(CollectionSettings.Language3Tag))
                    languagesWeAlreadyHave.Add(CollectionSettings.Language3Tag);

                var cssLines = RobustFile.ReadAllLines(path);
                existingContent = string.Join(Environment.NewLine, cssLines);
                const string kLangTag = "[lang='";
                var copyCurrentRule = false;
                for (var index = 0; index < cssLines.Length; ++index)
                {
                    var line = cssLines[index].Trim();
                    if (line.StartsWith(kLangTag))
                    {
                        var idxQuote = line.IndexOf("'", kLangTag.Length);
                        if (idxQuote > 0)
                        {
                            var lang = line.Substring(kLangTag.Length, idxQuote - kLangTag.Length);
                            copyCurrentRule = !languagesWeAlreadyHave.Contains(lang);
                            languagesWeAlreadyHave.Add(lang); // don't copy if another css block has crept in.
                        }
                    }
                    if (copyCurrentRule)
                        cssBuilder.AppendLine(cssLines[index]);
                    if (line == "}")
                        copyCurrentRule = false;
                }
            }
            if (WriteFontFaces)
            {
                var serve = FontServe.GetInstance();
                cssBuilder.Insert(0, serve.GetAllFontFaceDeclarations());
            }
            try
            {
                var newContent = cssBuilder.ToString();
                if (newContent.Trim() != existingContent?.Trim())
                    RobustFile.WriteAllText(path, newContent);
            }
            catch (UnauthorizedAccessException e)
            {
                // Re-throw with additional debugging info.
                throw new BloomUnauthorizedAccessException(path, e);
            }
        }

        private void RemoveFontFaceDeclarations(string path)
        {
            var contents = RobustFile.ReadAllText(path);
            contents = Regex.Replace(contents, "^@font-face.*$\n", "", RegexOptions.Multiline);
            RobustFile.WriteAllText(path, contents);
        }

        private void UpdateCollectionSettingsInBookMetaData()
        {
            Debug.WriteLine(
                $"updating page number style and language display names in {FolderPath}/meta.json"
            );
            BookInfo.MetaData.PageNumberStyle = CollectionSettings.PageNumberStyle;
            if (BookInfo.MetaData.DisplayNames == null)
                BookInfo.MetaData.DisplayNames = new Dictionary<string, string>();
            foreach (var lang in _bookData.GetAllBookLanguages(true, true))
                BookInfo.MetaData.DisplayNames[lang.Tag] = lang.Name;
            // These settings will be saved to the meta.json file the next time the book itself is saved.
        }

        /// <summary>
        /// For awhile in v4.1, Question pages used a "nonprinting" class,
        /// but later we made the class fit our usual "bloom-x" class naming scheme for classes
        /// which are known to the C# code.
        /// </summary>
        /// <param name="bookDOM"></param>
        private static void MigrateNonstandardClassNames(HtmlDom bookDOM)
        {
            if (bookDOM?.Body == null)
                return; // must be a test running...

            var nonPrintingPages = bookDOM.Body.SelectNodes(
                "//div[contains(@class,'nonprinting')]"
            );
            foreach (XmlElement nonPrintingPageElement in nonPrintingPages)
            {
                nonPrintingPageElement.Attributes["class"].InnerText = HtmlDom.RemoveClass(
                    "nonprinting",
                    nonPrintingPageElement.Attributes["class"].InnerText
                );
                HtmlDom.AddClass(nonPrintingPageElement, "bloom-nonprinting");
            }
        }

        /// <summary>
        /// At one point in v4.1, Question pages were able to be recorded with the Talking Book tool, but opening the
        /// tool on these pages embedded tons of span elements which messed up BR display. New question pages have classes
        /// that keep them from being recorded. Here we fix up any existing question pages from the old code.
        /// </summary>
        /// <param name="bookDOM"></param>
        private static void RepairQuestionsPages(HtmlDom bookDOM)
        {
            if (bookDOM?.Body == null)
                return; // must be a test running...

            // classes to add
            const string classNoStyleMods = "bloom-userCannotModifyStyles";
            const string classNoAudio = "bloom-noAudio";

            var questionNodes = bookDOM.Body.SelectNodes("//div[contains(@class,'quizContents')]");
            foreach (XmlElement quizContentsElement in questionNodes)
            {
                if (!HtmlDom.HasClass(quizContentsElement, "bloom-noAudio")) // Needs migration
                {
                    HtmlDom.AddClass(quizContentsElement, classNoStyleMods);
                    HtmlDom.AddClass(quizContentsElement, classNoAudio);
                    HtmlDom.StripUnwantedTagsPreservingText(
                        bookDOM.RawDom,
                        quizContentsElement,
                        new[] { "div", "p", "br" }
                    );
                }
            }
        }

        /// <summary>
        /// Prior to Bloom 4.0 there was an attempt at localizing Page labels and the translation
        /// for several languages was done, but was not showing up. It turns out this was due to an
        /// incomplete implementation. Hopefully the implementation is complete now, but for older
        /// books that were not built with the proper attribute on the page label DIV we now insert it
        /// so that page labels can be localized properly.
        /// </summary>
        /// <remarks>
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5313
        /// </remarks>
        internal static void RepairPageLabelLocalization(HtmlDom bookDOM)
        {
            if (bookDOM?.Body == null)
                return; // must be a test running...

            const string i18nPrefix = "TemplateBooks.PageLabel.";
            const string i18nAttr = "data-i18n";
            var prefixLength = i18nPrefix.Length;

            var pageLabelNodes = bookDOM.Body.SelectNodes("//div[@id]/div[@class='pageLabel']");
            foreach (XmlElement pageLabelElt in pageLabelNodes)
            {
                // If we already have a data-i18n attribute with the right contents, skip this one.
                var i18nValue = pageLabelElt.GetOptionalStringAttribute(i18nAttr, i18nPrefix);
                if (i18nValue.StartsWith(i18nPrefix) && i18nValue.Length > prefixLength)
                {
                    // As best we can tell, this already has the right localization attribute contents.
                    continue;
                }
                // Need to fix this one up with the right attribute and contents
                var englishContents = pageLabelElt.InnerText.Trim();
                i18nValue = i18nPrefix + englishContents;
                pageLabelElt.SetAttribute(i18nAttr, i18nValue);
            }
        }

        /// <summary>
        /// A bug in the initial release of Bloom 3.8 resulted in the nested editable divs being stored
        /// for the smallCoverCredits data under the general language tag "*".  (The bug was actually in
        /// the pug mixins for xmatter.)  The manifestation experienced by users was having the front page
        /// credits disappear from older books.  New books appeared to work properly.  Fixing the xmatter
        /// without fixing the book's html would have much the same effect, but for any books that had been
        /// created or edited with Bloom 3.8 (or newer).  This method restores sanity to the bloomDataDiv
        /// for the smallCoverCredits content.
        /// </summary>
        /// <remarks>
        /// See http://issues.bloomlibrary.org/youtrack/issue/BL-4591.
        /// </remarks>
        internal static void RepairBrokenSmallCoverCredits(HtmlDom bookDOM)
        {
            var dataDiv = bookDOM?.Body?.SelectSingleNode("div[@id='bloomDataDiv']");
            var badSmallCoverDiv = dataDiv?.SelectSingleNode(
                "div[@data-book='smallCoverCredits' and @lang='*']"
            );
            if (badSmallCoverDiv != null)
            {
                var divs = badSmallCoverDiv.SelectNodes("div[@lang!='']");
                foreach (XmlNode div in divs)
                {
                    var lang = div.GetStringAttribute("lang");
                    Debug.Assert(lang != "*");
                    var existingDiv = dataDiv.SelectSingleNode(
                        "div[@data-book='smallCoverCredits' and @lang='" + lang + "']"
                    );
                    if (existingDiv != null)
                        continue; // I don't think this should ever happen, but just in case...
                    var innerText = div.InnerText;
                    if (String.IsNullOrWhiteSpace(innerText))
                        continue; // ignore empty content regardless of XML markup
                    var newDiv = dataDiv.OwnerDocument.CreateElement("div");
                    newDiv.SetAttribute("data-book", "smallCoverCredits");
                    newDiv.SetAttribute("lang", lang);
                    newDiv.InnerXml = div.InnerXml.Trim(); // ignore surrounding newlines (or other whitespace)
                    dataDiv.AppendChild(newDiv);
                }
                dataDiv.RemoveChild(badSmallCoverDiv);
            }
        }

        internal static void RemoveImgTagInDataDiv(HtmlDom bookDom)
        {
            // BL-4586 Some old books ended up with background-image urls containing XML img tags
            // in the HTML-encoded string. This happened because the coverImage data-book element
            // contained an img tag instead of a bare filename.
            // If such a thing exists in this book we will strip it out and replace it with the
            // filename in the img src attribute.
            const string dataDivImgXpath = "//div[@id='bloomDataDiv']/div[@data-book='coverImage']";
            var elementsToCheck = bookDom.SafeSelectNodes(dataDivImgXpath);
            foreach (XmlNode coverImageElement in elementsToCheck)
            {
                var imgNodes = coverImageElement.SafeSelectNodes("img");
                if (imgNodes.Count == 0)
                {
                    continue;
                }
                var imgNode = imgNodes[0];
                coverImageElement.InnerText =
                    (imgNode.Attributes == null || imgNode.Attributes["src"] == null)
                        ? string.Empty
                        : HttpUtility.UrlDecode(imgNode.Attributes["src"].Value);
            }
        }

        /// <summary>
        /// Repair the cover image descriptions to be language specific, storing only the internal paragraph(s) for
        /// each language, instead of the entire translationGroup div as a single piece.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-7039.
        /// This replaces the need to update the bloomDataDiv item to use ImageDescriptionEdit-style
        /// instead of normal-style as done for https://issues.bloomlibrary.org/youtrack/issue/BL-7804.
        /// </remarks>
        internal static void RepairCoverImageDescriptions(HtmlDom bookDOM)
        {
            var dataDiv = bookDOM?.Body?.SelectSingleNode("div[@id='bloomDataDiv']");
            var coverImageDiv = dataDiv?.SelectSingleNode(
                "div[@data-book='coverImageDescription' and @lang='*']"
            );
            if (coverImageDiv == null)
                return; // nothing to fix
            foreach (
                XmlElement coverImageDescription in dataDiv
                    .SafeSelectNodes("div[@data-book='coverImageDescription']")
                    .Cast<XmlElement>()
                    .ToList()
            )
            {
                var lang = coverImageDescription.GetAttribute("lang");
                if (lang == "*")
                {
                    foreach (
                        XmlNode descriptionDiv in coverImageDescription.SafeSelectNodes(
                            "div[contains(@class,'bloom-editable')]"
                        )
                    )
                    {
                        var innerLang = descriptionDiv.Attributes["lang"]?.Value;
                        if (String.IsNullOrEmpty(innerLang) || innerLang == "z")
                            continue;
                        var newDescriptionDiv = dataDiv.OwnerDocument.CreateElement("div");
                        newDescriptionDiv.SetAttribute("data-book", "coverImageDescription");
                        newDescriptionDiv.SetAttribute("lang", innerLang);
                        newDescriptionDiv.InnerXml = descriptionDiv.InnerXml;
                        dataDiv.AppendChild(newDescriptionDiv);
                    }
                }
                // Get rid of the obsolete data-book element.  If the user went from 4.7 with separate language specific entries
                // back to 4.6 with the single lang="*" entry, then all of the entries got filled in with the same obsolete data,
                // so all of them have to be removed.
                dataDiv.RemoveChild(coverImageDescription);
            }
        }

        internal static void DetectAndMarkDarkCoverColor(HtmlDom bookDOM)
        {
            var coverColor = GetCoverColorFromDom(bookDOM.RawDom);
            if (coverColor != null)
            {
                var coverColorIsDark = ColorUtils.IsDark(coverColor);
                foreach (
                    var page in bookDOM
                        .SafeSelectNodes("//div[contains(@class,'coverColor')]")
                        .Cast<XmlElement>()
                )
                {
                    if (coverColorIsDark)
                        HtmlDom.AddClass(page, "darkCoverColor");
                    else
                        HtmlDom.RemoveClass(page, "darkCoverColor");
                }
            }
        }

        public void BringXmatterHtmlUpToDate(HtmlDom bookDOM)
        {
            var fileLocator = Storage.GetFileLocator();
            var helper = new XMatterHelper(
                bookDOM,
                CollectionSettings.XMatterPackName,
                fileLocator,
                BookInfo.UseDeviceXMatter
            );

            // Before applying the xmatter, check to see if the previous was Kyrgystan2020, which through to this version, 5.2,
            // unfortunately has a branding that pollutes the data-div with this fullBleed, which isn't wanted if the book
            // is re-used in another project. See https://issues.bloomlibrary.org/youtrack/issue/BL-11290.
            // The one scenario we know this would break would be a book from the Kyr project was re-purposed in another project,
            // and that book used the "Paper Comic" which does have to have full bleed.
            if (
                !helper.GetStyleSheetFileName().Contains("Kyrgyzstan2020")
                && // we're not going (or more likely staying) in Kyrgyzstan
                _bookData.GetVariableOrNull("xmatter", "*").Xml == "Kyrgyzstan2020"
            ) // but we are coming from it
            {
                Logger.WriteEvent(
                    "Removing fullBleed because the previous xmatter was Kyrgystan2020."
                );
                _bookData.RemoveAllForms("fullBleed"); // this is fine if it doesn't find it.
            }

            // If it's not the real book DOM we won't copy branding images into the real book folder, for fear
            // of messing up the real book, if the temporary one is in a different orientation.
            if (bookDOM != OurHtmlDom)
                helper.TemporaryDom = true;

            //note, we determine this before removing xmatter to fix the situation where there is *only* xmatter, no content, so if
            //we wait until we've removed the xmatter, we no how no way of knowing what size/orientation they had before the update.
            // Per BL-3571, if it's using a layout we don't know (e.g., from a newer Bloom) we switch to A5Portrait.
            // Various things, especially publication, don't work with unknown page sizes.
            Layout layout = Layout.FromDomAndChoices(bookDOM, Layout.A5Portrait, fileLocator);
            var oldIds = new List<string>();
            XMatterHelper.RemoveExistingXMatter(bookDOM, oldIds);
            // this says, if you can't figure out the page size, use the one we got before we removed the xmatter...
            // still requiring it to be a valid layout.
            layout = Layout.FromDomAndChoices(bookDOM, layout, fileLocator);
            helper.InjectXMatter(
                _bookData.WritingSystemAliases,
                layout,
                BookInfo.UseDeviceXMatter,
                _bookData.MetadataLanguage1Tag,
                oldIds
            );

            var dataBookLangs = bookDOM.GatherDataBookLanguages();
            TranslationGroupManager.PrepareDataBookTranslationGroups(bookDOM.RawDom, dataBookLangs);

            helper.InjectDefaultUserStylesFromXMatter();
        }

        // Around May 2014 we added a class, .bloom-requireParagraphs, backed by javascript that makes geckofx
        // emit <p>s instead of <br>s (which you can't style and don't leave a space in wkhtmltopdf).
        // If there is existing text after we added this, it needs code to do the conversion. There
        // is already javascript for this, but by having it here allows us to update an entire collection in one commmand.
        // Note, this doesn't yet do as much as the javascript version, which also can be triggered by a border-top-style
        // of "dashed", so that books shipped without this class can still be converted over.
        public void UpdateTextsNewlyChangedToRequiresParagraph(HtmlDom bookDom)
        {
            var texts = OurHtmlDom.SafeSelectNodes(
                "//*[contains(@class,'bloom-requiresParagraphs')]/div[contains(@class,'bloom-editable') and br]"
            );
            foreach (XmlElement text in texts)
            {
                string s = "";
                foreach (
                    var chunk in text.InnerXml.Split(
                        new string[] { "<br />", "<br/>" },
                        StringSplitOptions.None
                    )
                )
                {
                    if (chunk.Trim().Length > 0)
                        s += "<p>" + chunk + "</p>";
                }
                text.InnerXml = s;
            }
        }

        /// <summary>
        /// Convert old &lt;b&gt; and &lt;i&gt; to &lt;strong&gt; and &lt;em&gt; respectively.
        /// Also remove instances like &lt;/b&gt;&lt;b&gt; altogether since such markup is redundant.
        /// </summary>
        public void UpdateCharacterStyleMarkup(HtmlDom bookDOM)
        {
            var preserve = bookDOM.RawDom.PreserveWhitespace;
            bookDOM.RawDom.PreserveWhitespace = true;
            var paragraphs = bookDOM.SafeSelectNodes("//div[contains(@class,'bloom-editable')]/p");
            foreach (XmlElement para in paragraphs)
            {
                string inner = para.InnerXml;
                if (String.IsNullOrEmpty(inner) || !inner.Contains("<"))
                    continue;
                inner = Regex.Replace(
                    inner,
                    @"</b>(\p{Z}*)<b>",
                    "$1",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                );
                inner = Regex.Replace(
                    inner,
                    @"</i>(\p{Z}*)<i>",
                    "$1", // we've handled "</i></b> <b><i>"
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                );
                inner = Regex.Replace(
                    inner,
                    @"</b>(\p{Z}*)<b>",
                    "$1", // repeat in case "</b></i> <i><b>" happens
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                );
                // Note that .*? is the non-greedy match for any number of any characters.
                inner = Regex.Replace(
                    inner,
                    @"<b>(.*?)</b>",
                    "<strong>$1</strong>",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                );
                inner = Regex.Replace(
                    inner,
                    @"<i>(.*?)</i>",
                    "<em>$1</em>",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                );
                if (inner != para.InnerXml)
                    para.InnerXml = inner;
            }
        }

        internal static void ConvertTagsToMetaData(string oldTagsPath, BookInfo bookMetaData)
        {
            var oldTags = RobustFile.ReadAllText(oldTagsPath);
            bookMetaData.IsFolio = oldTags.Contains("folio");
            bookMetaData.IsExperimental = oldTags.Contains("experimental");
        }

        private void FixBookIdAndLineageIfNeeded()
        {
            HtmlDom bookDOM = Storage.Dom;
            //at version 0.9.71, we introduced this book lineage for real. At that point almost all books were from Basic book,
            //so let's get further evidence by looking at the page source and then fix the lineage
            // However, if we have json lineage, it is normal not to have it in HTML metadata.
            if (
                string.IsNullOrEmpty(BookInfo.BookLineage)
                && bookDOM.GetMetaValue("bloomBookLineage", "") == ""
            )
                if (PageTemplateSource == "Basic Book")
                {
                    bookDOM.UpdateMetaElement("bloomBookLineage", kIdOfBasicBook);
                }

            //there were a number of books in version 0.9 that just copied the id of the basic book from which they were created
            if (bookDOM.GetMetaValue("bloomBookId", "") == kIdOfBasicBook)
            {
                if (bookDOM.GetMetaValue("title", "") != "Basic Book")
                {
                    bookDOM.UpdateMetaElement("bloomBookId", Guid.NewGuid().ToString());
                }
            }
        }

        /// <summary>
        /// Repair any cover image filenames that were left URL-encoded by earlier versions of
        /// Bloom.  Although rare, this has surfaced recently.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-11145
        /// and https://issues.bloomlibrary.org/youtrack/issue/BH-6143.
        /// </remarks>
        private void FixUrlEncodedCoverImageIfNeeded()
        {
            var node = Storage.Dom.SelectSingleNode(
                "//body/div[@id='bloomDataDiv']/div[@data-book='coverImage']"
            );
            if (node == null)
                return; // shouldn't happen, but nothing to fix if it does.
            var text = node.InnerText;
            if (
                !String.IsNullOrWhiteSpace(text)
                && RobustFile.Exists(Path.Combine(FolderPath, text))
            )
                return; // file exists, no need to tweak reference
            // GetFullyDecodedPath decodes until either the file exists or no further URL decoding is possible.
            // If the file isn't found, then text is the original value.
            var filepath = UrlPathString.GetFullyDecodedPath(FolderPath, ref text);
            if (text != node.InnerText)
            {
                node.InnerText = text;
                node.SetAttribute("src", text);
                var localizedFormatString = LocalizationManager.GetString(
                    "EditTab.Image.AltMsg",
                    "This picture, {0}, is missing or was loading too slowly."
                );
                var altValue = String.Format(localizedFormatString, text);
                node.SetAttribute("alt", altValue);
            }
        }

        /// <summary>
        /// The bloomBookId meta value
        /// </summary>
        public string ID => Storage.BookInfo.Id;

        private void UpdateImageMetadataAttributes(XmlElement imgNode)
        {
            try
            {
                ImageUpdater.UpdateImgMetadataAttributesToMatchImage(
                    FolderPath,
                    imgNode,
                    new NullProgress()
                );
            }
            catch (UnauthorizedAccessException e)
            {
                BookStorage.ShowAccessDeniedErrorReport(e);
            }
        }

        // Returns true if it updated something.
        public bool UpdatePageToTemplate(
            HtmlDom pageDom,
            IPage templatePage,
            string pageId,
            bool allowDataLoss = true
        )
        {
            if (
                !OurHtmlDom.UpdatePageToTemplate(
                    pageDom,
                    templatePage.GetDivNodeForThisPage(),
                    pageId,
                    allowDataLoss
                )
            )
                return false;
            AddMissingStylesFromTemplatePage(templatePage);
            CopyMissingStylesheetFiles(templatePage);
            UpdateEditableAreasOfElement(pageDom);
            return true;
        }

        // returns true if it updated something
        public bool UpdatePageToTemplateAndUpdateLineage(
            IPage pageToChange,
            IPage templatePage,
            bool allowDataLoss = true
        )
        {
            if (!UpdatePageToTemplate(OurHtmlDom, templatePage, pageToChange.Id, allowDataLoss))
                return false;
            // The Page objects are cached in the page list and may be used if we issue another
            // change layout command. We must update their lineage so the right "current layout"
            // will be shown if the user changes the layout of the same page again.
            var pageChanged = pageToChange as Page;
            pageChanged?.UpdateLineage(new[] { templatePage.Id });
            return true;
        }

        public void ChangeLayoutForAllContentPages(IPage templatePage)
        {
            foreach (var page in GetPages())
                if (!page.IsXMatter)
                    UpdatePageToTemplateAndUpdateLineage(page, templatePage, allowDataLoss: false);
            Save();
        }

        private static void UpdateDivInsidePage(
            int zeroBasedCount,
            XmlElement templateElement,
            XmlElement targetPage,
            IProgress progress
        )
        {
            XmlElement targetElement =
                targetPage.SelectSingleNode(
                    "div/div[" + (zeroBasedCount + 1).ToString(CultureInfo.InvariantCulture) + "]"
                ) as XmlElement;
            if (targetElement == null)
            {
                progress.WriteError(
                    "Book had less than the expected number of divs on page "
                        + targetPage.GetAttribute("id")
                        + ", so it cannot be completely updated."
                );
                return;
            }
            targetElement.SetAttribute("class", templateElement.GetAttribute("class"));
        }

        public bool HasOriginalCopyrightInfo
        {
            get
            {
                var x = OurHtmlDom.SafeSelectNodes(
                    "//div[contains(@id, 'bloomDataDiv')]/div[contains(@data-book, 'originalCopyright') and string-length(translate(normalize-space(text()), ' ', '')) > 0]"
                );
                return x.Count > 0;
            }
        }

        public string CategoryForUsageReporting
        {
            get
            {
                if (IsSuitableForMakingShells)
                {
                    // We might also be making something intended to be a new shell, but we can't tell the difference
                    // between that and a custom vernacular book any more.
                    return "CustomVernacularBook";
                }
                else
                {
                    // We started from a shell, so presumably we're translating it...though we could
                    // be adapting it to a new shell (e.g., abridging it or otherwise enhancing it).
                    return "ShellTranslating";
                }
            }
        }

        // Anything that sets HasFatalError true should appropriately set FatalErrorDescription.
        public virtual bool HasFatalError { get; private set; }
        private string FatalErrorDescription { get; set; }

        public string ThumbnailPath => Path.Combine(FolderPath, "thumbnail.png");

        public string NonPaddedThumbnailPath => Path.Combine(FolderPath, "nonPaddedThumbnail.png");

        /// <summary>
        /// In a vernacular library, we want to hide books that are meant only for people making shells
        /// </summary>
        public bool IsSuitableForVernacularLibrary => BookInfo.IsSuitableForVernacularLibrary;

        //discontinuing this for now becuase we need to know whether to show the book when all we have is a bookinfo, not access to the
        //dom like this requires. We'll just hard code the names of the experimental things.
        //        public bool IsExperimental
        //        {
        //            get
        //            {
        //                string metaValue = OurHtmlDom.GetMetaValue("experimental", "false");
        //                return metaValue == "true" || metaValue == "yes";
        //            }
        //        }

        /// <summary>
        /// In a shell-making library, we want to hide books that are just shells, so rarely make sense as a starting point for more shells.
        /// Note: the setter on this property just sets the flag to the appropriate state for testing.
        /// </summary>
        public bool IsSuitableForMakingShells
        {
            get { return BookInfo.IsSuitableForMakingShells; }
            set { BookInfo.IsSuitableForMakingShells = value; }
        }

        /// <summary>
        /// The name "IsSuitableForMakingShells" must have been the best at one point, but now that Template books are a built-in part of Bloom,
        /// it makes code hard to read, when we just think in terms of "is this a template book"? If it turns out the semantics do not actually line up
        /// well, then we will have to modify this and that's good.
        /// </summary>
        public bool IsTemplateBook => IsSuitableForMakingShells;

        /// <summary>
        /// A "Folio" document is one that acts as a wrapper for a number of other books
        /// </summary>
        public bool IsFolio
        {
            get
            {
                string metaValue = OurHtmlDom.GetMetaValue(
                    "folio",
                    OurHtmlDom.GetMetaValue("Folio", "no")
                );
                return metaValue == "yes" || metaValue == "true";
            }
        }

        /// <summary>
        /// The first language to show, typically the vernacular
        /// </summary>
        public string Language1Tag => _bookData.Language1Tag;

        /// <summary>
        /// For bilingual or trilingual books, this is the second language to show in Auto blocks.
        /// </summary>
        public string Language2Tag => _bookData.Language2Tag;

        //ENHANCE: Make MCL2 and MCL3 return XmlString isntead of string

        /// <summary>
        /// For trilingual books, this is the third language to show in Auto blocks.
        /// </summary>
        public string Language3Tag => _bookData.Language3Tag;

        public IEnumerable<string> ActiveLanguages
        {
            get
            {
                var result = new HashSet<string>(new[] { Language1Tag });
                if (Language2Tag != null)
                    result.Add(Language2Tag);
                if (Language3Tag != null)
                    result.Add(Language3Tag);
                return result;
            }
        }

        public virtual BookInfo BookInfo { get; protected set; }

        public UserPrefs UserPrefs { get; private set; }

        public void SetMultilingualContentLanguages(params string[] contentLanguages)
        {
            _bookData.SetMultilingualContentLanguages(contentLanguages);
            InjectStringListingActiveLanguagesOfBook();
            _bookData.UpdateDomFromDataset();
            _bookData.SetupDisplayOfLanguagesOfBook();
        }

        /// <summary>
        /// Bloom books can have up to 3 languages active at any time. This method pushes in a string
        /// listing then, separated by commas. It is then usable on the front page, title page, etc.
        /// </summary>
        /// If we have a sign language defined, we start the list with the name of the sign language that
        /// is stored in the CollectionSettings.
        /// <remarks>
        /// We use the name of the language assigned by the user when the language was chosen rather
        /// than attempting to use the name in the national language (most likely getting the autonyms
        /// from a buggy list).  This keeps things simpler and follows the principle of least surprise.
        /// </remarks>
        private void InjectStringListingActiveLanguagesOfBook()
        {
            // Put the sign language (if any) first in the list.  See BL-11414.
            // Getting the sign language will have to change if we ever assign it on a per-book basis.
            var languagesOfBook =
                (
                    string.IsNullOrEmpty(_bookData.SignLanguageTag)
                        ? ""
                        : _bookData.SignLanguage.Name + ", "
                ) + _bookData.Language1.Name;

            if (Language2Tag != null)
            {
                languagesOfBook += ", " + _bookData.Language2.Name;
            }

            if (Language3Tag != null)
            {
                languagesOfBook += ", " + _bookData.Language3.Name;
            }

            _bookData.Set("languagesOfBook", XmlString.FromUnencoded(languagesOfBook), false);
        }

        /// <summary>
        /// All the languages in a book is a difficult concept to define.
        /// So much so that the result isn't simply a list, but a dictionary: the keys indicate the languages
        /// that are present, and the (boolean) values are true if the book is considered to be "complete"
        /// in that language, which in some contexts (such as publishing to bloom library) governs whether
        /// the language is published by default.
        /// </summary>
        /// <remarks>The logic here is used to determine how to present (show and/or enable) language checkboxes on the publish screens.
        /// Nearly identical logic is used in bloom-player to determine which languages to show on the Language Menu,
        /// so changes here may need to be reflected there and vice versa.</remarks>
        public Dictionary<string, bool> AllPublishableLanguages(
            bool includeLangsOccurringOnlyInXmatter
        )
        {
            var result = new Dictionary<string, bool>();
            var parents = new HashSet<XmlElement>(); // of interesting non-empty children
            var langDivs = OurHtmlDom.GetLanguageDivs(includeLangsOccurringOnlyInXmatter).ToArray();

            // First pass: fill in the dictionary with languages which have non-empty content in relevant divs
            foreach (var div in langDivs)
            {
                var lang = div.Attributes["lang"].Value;
                if (HtmlDom.DivHasContent(div))
                {
                    result[lang] = true; // may be set repeatedly, but no harm.
                    // Add each parent only once, but add every parent for divs with any text.
                    var parent = (XmlElement)div.ParentNode;
                    if (!parents.Contains(parent))
                        parents.Add(parent);
                }
            }

            var xmatterOnlyLangs = new HashSet<string>();
            // Second pass: for each parent, if it lacks a non-empty child for one of the languages, set value for that lang to false.
            // OTOH, if the parent is in XMatter, don't set the value.
            foreach (var lang in result.Keys.ToList()) // ToList so we can modify original collection as we go
            {
                bool isXmatterOnly = true;
                foreach (var parent in parents)
                {
                    if (ElementIsInXMatter(parent))
                        continue;
                    isXmatterOnly = false;
                    if (IsLanguageWantedInContent(parent, lang) && !HasContentInLang(parent, lang))
                    {
                        result[lang] = false; // not complete
                        break; // no need to check other parents.
                    }
                }
                if (isXmatterOnly)
                    xmatterOnlyLangs.Add(lang);
            }

            // Remove any languages that are only in xmatter, unless they are set for this book.
            var languagesToIncludeEvenIfXmatterOnly = new string[]
            {
                Language1Tag,
                BookData.MetadataLanguage1Tag,
                BookData.MetadataLanguage2Tag
            };
            foreach (var lang in xmatterOnlyLangs)
            {
                if (!languagesToIncludeEvenIfXmatterOnly.Contains(lang))
                    result.Remove(lang);
            }

            return result;
        }

        internal static bool ElementIsInXMatter(XmlElement element)
        {
            if (element == null)
                return false;
            while (element.ParentNode != null && element.ParentNode.Name != "body")
            {
                if (
                    element.ParentWithClass("bloom-frontMatter") != null
                    || element.ParentWithClass("bloom-backMatter") != null
                )
                    return true;
                element = element.ParentNode as XmlElement;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the given language bloom-editable is wanted (should be visible, and therefore typically
        /// published) in the given parent.
        /// Currently this is reliable only for elements where visibility is controlled by the old data-default-languages
        /// attribute rather than the new appearance system. It will need enhancement if we ever need to use it for xmatter,
        /// or for that matter if the appearance system is used to control visibility of something on content pages.
        /// </summary>
        private bool IsLanguageWantedInContent(XmlElement parent, string lang)
        {
            var defaultLangs = parent.GetAttribute("data-default-languages");
            if (String.IsNullOrEmpty(defaultLangs) || defaultLangs == "auto")
                return true; // assume we want everything
            var dataDefaultLanguages = defaultLangs.Split(
                new char[] { ',', ' ' },
                StringSplitOptions.RemoveEmptyEntries
            );
            foreach (var defaultLang in dataDefaultLanguages)
            {
                switch (defaultLang)
                {
                    case "V":
                    case "L1":
                        if (lang == Language1Tag)
                            return true;
                        break;
                    case "N1":
                        if (lang == _bookData.MetadataLanguage1Tag)
                            return true;
                        break;
                    case "L2":
                        if (lang == _bookData.Language2Tag)
                            return true;
                        break;
                    case "N2":
                        if (lang == _bookData.MetadataLanguage2Tag)
                            return true;
                        break;
                    case "L3":
                        if (lang == _bookData.Language3Tag)
                            return true;
                        break;
                }
            }
            return false;
        }

        private static bool HasContentInLang(XmlElement parent, string lang)
        {
            foreach (var node in parent.ChildNodes)
            {
                var div = node as XmlElement;
                if (
                    div?.Attributes["lang"] == null
                    || div.Attributes["lang"].Value != lang
                    || div.Name == "label"
                )
                    continue;
                return !string.IsNullOrWhiteSpace(div.InnerText); // this one settles it: success if non-empty
            }
            return false; // not found
        }

        public void RemoveObsoleteAudioMarkup()
        {
            RemoveObsoleteAudioMarkup(AudioProcessor.DoesAudioExistForSegment);
        }

        /// <summary>
        /// Remove any obsolete audio markup, using the supplied function to detect whether the markup is actually obsolete.
        /// (based on whether the relevant audio file exists or not.)
        /// </summary>
        /// <remarks>
        /// The extra complication of passing in a function is used to enable testing that non-obsolete markup is not removed.
        /// </remarks>
        public void RemoveObsoleteAudioMarkup(Func<string, string, bool> doesFileExist)
        {
            foreach (
                var spanOrDiv in HtmlDom
                    .SelectAudioSentenceElements(RawDom.DocumentElement)
                    .Cast<XmlElement>()
                    .ToList()
            )
            {
                if (!doesFileExist(Storage.FolderPath, spanOrDiv.Attributes["id"]?.Value))
                {
                    if (spanOrDiv.Name == "span")
                    {
                        HtmlDom.RemoveElementLayer(spanOrDiv);
                    }
                    else
                    {
                        // TextBox mode recording: clean up multiple attributes on the div, and remove unneeded spans while
                        // preserving the span content.
                        spanOrDiv.RemoveAttribute("data-audiorecordingmode");
                        spanOrDiv.RemoveAttribute("data-audiorecordingendtimes");
                        spanOrDiv.RemoveAttribute("data-duration");
                        HtmlDom.RemoveClass(spanOrDiv, "audio-sentence");
                        HtmlDom.RemoveClass(spanOrDiv, "bloom-postAudioSplit");
                        foreach (
                            var span in spanOrDiv
                                .SafeSelectNodes(".//span[@class='bloom-highlightSegment']")
                                .Cast<XmlElement>()
                                .ToList()
                        )
                        {
                            HtmlDom.RemoveElementLayer(span);
                        }
                    }
                }
            }
            foreach (
                var div in RawDom.DocumentElement
                    .SafeSelectNodes("//div[@data-audiorecordingmode]")
                    .Cast<XmlElement>()
                    .ToList()
            )
            {
                var nodes = div.SafeSelectNodes(
                    "descendant-or-self::node()[contains(@class,'audio-sentence')]"
                );
                if (nodes == null || nodes.Count == 0)
                    div.RemoveAttribute("data-audiorecordingmode");
            }
        }

        /// <summary>
        /// Determines if the book references an existing audio file.
        /// </summary>
        /// <returns></returns>
        public bool HasAudio()
        {
            return GetRecordedAudioSentences().Any();
        }

        /// <summary>
        /// Returns the elements that reference an audio file that exist
        /// </summary>
        /// <returns></returns>
        public IEnumerable<XmlElement> GetRecordedAudioSentences()
        {
            return OurHtmlDom.GetRecordedAudioSentences(Storage.FolderPath);
        }

        /// <summary>
        /// Check whether all text is covered by audio recording.
        /// </summary>
        /// <remarks>
        /// Only editable text in numbered pages is checked at the moment.
        /// </remarks>
        public bool HasFullAudioCoverage()
        {
            // REVIEW: should any of the xmatter pages be checked (front cover, title, credits?)
            foreach (var divWantPage in RawDom.SafeSelectNodes("//div[@class]").Cast<XmlElement>())
            {
                if (!HtmlDom.HasClass(divWantPage, "numberedPage"))
                    continue;
                foreach (
                    var div in divWantPage.SafeSelectNodes(".//div[@class]").Cast<XmlElement>()
                )
                {
                    if (!HtmlDom.HasClass(div, "bloom-editable"))
                        continue;
                    var lang = div.GetStringAttribute("lang");
                    if (lang != Language1Tag)
                        continue; // this won't go into the book -- it's a different language.
                    // TODO: Ensure handles image descriptions once those get implemented.
                    var textOfDiv = div.InnerText.Trim();

                    // Note: Prior to version 4.4, audio-sentences were only in spans, but in version 4.4 they can be divs
                    // (because the boundaries may need to cross multiple paragraphs, and <span> cannot contain <p> (because span is inline, and p is not))
                    foreach (
                        var audioSentenceChildNode in HtmlDom
                            .SelectAudioSentenceElements(div)
                            .Cast<XmlElement>()
                    )
                    {
                        var id = audioSentenceChildNode.GetOptionalStringAttribute("id", "");
                        if (
                            string.IsNullOrEmpty(id)
                            || !AudioProcessor.DoesAudioExistForSegment(
                                Storage.FolderPath,
                                audioSentenceChildNode.Attributes["id"].Value
                            )
                        )
                            return false; // missing audio file
                        if (!textOfDiv.StartsWith(audioSentenceChildNode.InnerText))
                            return false; // missing audio span?
                        textOfDiv = textOfDiv.Substring(audioSentenceChildNode.InnerText.Length);
                        textOfDiv = textOfDiv.TrimStart();
                    }

                    if (!string.IsNullOrEmpty(textOfDiv))
                        return false; // non-whitespace not covered by functional audio spans
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether the book references an existing image file other than
        /// branding, placeholder, or license images.
        /// </summary>
        /// <returns></returns>
        public bool HasImages()
        {
            return RawDom
                    .SafeSelectNodes("//img[@src]")
                    .Cast<XmlElement>()
                    .Any(NonTrivialImageFileExists)
                || RawDom
                    .SafeSelectNodes("//div[@style]")
                    .Cast<XmlElement>()
                    .Any(NonTrivialImageFileExists);
        }

        private bool NonTrivialImageFileExists(XmlElement image)
        {
            if (image.Name == "img")
            {
                if (HtmlDom.HasClass(image, "branding") || HtmlDom.HasClass(image, "licenseImage"))
                    return false;
            }
            var imageUrl = HtmlDom.GetImageElementUrl(image);
            var file = imageUrl.PathOnly.NotEncoded;
            if (string.IsNullOrEmpty(file))
                return false;
            if (file == "placeHolder.png" && image.Attributes["data-license"] == null)
                return false;
            return RobustFile.Exists(Path.Combine(Storage.FolderPath, file));
        }

        /// <summary>
        /// Determines whether the book references any existing video files other than branding.
        /// </summary>
        /// <returns></returns>
        public bool HasVideos()
        {
            return OurHtmlDom
                .SelectVideoSources()
                .Cast<XmlElement>()
                .Any(NonTrivialVideoFileExists);
        }

        /// <summary>
        /// Returns whether the book references any existing sign language video files.
        /// </summary>
        public bool HasSignLanguageVideos()
        {
            // Currently no difference between videos and sign language videos
            return HasVideos();
        }

        private bool NonTrivialVideoFileExists(XmlElement vidSource)
        {
            Debug.Assert(vidSource.Name == "source");
            // In case future books have video branding...
            if (
                HtmlDom.HasClass(vidSource, "branding")
                || HtmlDom.HasClass(vidSource.ParentNode as XmlElement, "branding")
            )
                return false;
            // video reference HTML structure is:
            //   <div class='bloom-videoContainer'>
            //     <video>
            //       <source src="video/guid.mp4#t=time1,time2" />   (# and after is optional)
            //     </video>
            //   </div>
            var vidNode = vidSource.ParentNode;
            if (vidNode == null)
                return false;
            // HtmlDom.GetVideoElementUrl() takes the .bloom-videoContainer node as a parameter.
            var videoUrl = HtmlDom.GetVideoElementUrl(vidNode.ParentNode as XmlElement);
            var file = videoUrl.PathOnly.NotEncoded;
            return !string.IsNullOrEmpty(file)
                && RobustFile.Exists(Path.Combine(Storage.FolderPath, file));
        }

        // Book Information should show only for templates, not for created books.
        // See https://issues.bloomlibrary.org/youtrack/issue/BL-5190.
        public bool HasAboutBookInformationToShow
        {
            get
            {
                if (Storage.FolderPath == null)
                    return false;
                if (Storage.FolderPath.Replace("\\", "/").Contains("/browser/templates/"))
                    return RobustFile.Exists(AboutBookHtmlPath); // built-in template shipped with Bloom
                if (BookInfo.IsSuitableForMakingShells || BookInfo.IsSuitableForMakingTemplates)
                    return RobustFile.Exists(AboutBookMdPath);
                return false;
            }
        }

        public string AboutBookHtmlPath =>
            BloomFileLocator.GetBestLocalizedFile(
                Storage.FolderPath.CombineForPath("ReadMe-en.htm")
            );

        public string AboutBookMdPath =>
            BloomFileLocator.GetBestLocalizedFile(
                Storage.FolderPath.CombineForPath("ReadMe-en.md")
            );

        public void InitCoverColor()
        {
            // for digital comic template, we want a black cover.
            // NOTE as this writing, at least, xmatter cannot set <meta> values, so this isn't a complete solution. It's only
            // useful for starting off a book from a template book.
            var preserve = this.OurHtmlDom.GetMetaValue("preserveCoverColor", "false");
            if (preserve == "false")
            {
                AddCoverColor(this.OurHtmlDom, CoverColors[s_coverColorIndex]);
            }
        }

        private void AddCoverColor(HtmlDom dom, Color coverColor)
        {
            var colorValue = ColorTranslator.ToHtml(coverColor);
            //            var colorValue = String.Format("#{0:X2}{1:X2}{2:X2}", coverColor.R, coverColor.G, coverColor.B);
            XmlElement colorStyle = dom.RawDom.CreateElement("style");
            colorStyle.SetAttribute("type", "text/css");
            colorStyle.InnerXml = @"
				DIV.bloom-page.coverColor	{		background-color: colorValue !important;	}
				".Replace("colorValue", colorValue); //string.format has a hard time with all those {'s

            dom.Head.AppendChild(colorStyle);
        }

        public String GetCoverColor()
        {
            return GetCoverColorFromDom(RawDom);
        }

        public static String GetCoverColorFromDom(XmlDocument dom)
        {
            foreach (XmlElement stylesheet in dom.SafeSelectNodes("//style"))
            {
                var content = stylesheet.InnerText;
                // Our XML representation of an HTML DOM doesn't seem to have any object structure we can
                // work with. The Stylesheet content is just raw CDATA text.
                // Regex updated to handle comments and lowercase 'div' in the cover color rule.
                var match = new Regex(
                    @"(DIV|div).bloom-page.coverColor\s*{.*?background-color:\s*(#[0-9a-fA-F]*|[a-z]*)",
                    RegexOptions.Singleline
                ).Match(content);
                if (match.Success)
                {
                    return match.Groups[2].Value;
                }
            }
            return "#FFFFFF";
        }

        /// <summary>
        /// Set the cover color. Not used initially; assumes there is already an (unfortunately unmarked)
        /// stylesheet created as in AddCoverColor.
        /// </summary>
        /// <param name="color"></param>
        public void SetCoverColor(string color)
        {
            if (SetCoverColorInternal(color))
            {
                Save();
                ContentsChanged?.Invoke(this, new EventArgs());
            }
        }

        /// <summary>
        /// Internal method is testable
        /// </summary>
        /// <param name="color"></param>
        /// <returns>true if a change was made</returns>
        internal bool SetCoverColorInternal(string color)
        {
            foreach (XmlElement stylesheet in RawDom.SafeSelectNodes("//style"))
            {
                string content = stylesheet.InnerXml;
                var regex = new Regex(
                    @"(DIV.(coverColor\s*TEXTAREA|bloom-page.coverColor)\s*{\s*background-color:\s*)(#[0-9a-fA-F]*)",
                    RegexOptions.IgnoreCase
                );
                if (!regex.IsMatch(content))
                    continue;
                var newContent = regex.Replace(content, "$1" + color);
                stylesheet.InnerXml = newContent;
                DetectAndMarkDarkCoverColor(OurHtmlDom);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Make stuff readonly, which isn't doable via css, surprisingly. And even more
        /// surprisingly still necessary after the switch to a React Collection tab preview!
        /// </summary>
        /// <param name="dom"></param>
        internal void AddPreviewJavascript(HtmlDom dom)
        {
            dom.AddJavascriptFile("commonBundle.js".ToLocalhost());
            dom.AddJavascriptFile("bookPreviewBundle.js".ToLocalhost());
        }

        /// <summary>
        /// Tells videos not to preload. Useful for preview mode, where the videos will never actually play.
        /// The goal is to minimize a memory leak where videos once loaded continue to use memory for
        /// a long time, especially if the system has a lot (BL-9845).
        /// Doc says preload="none" is unreliable, but it does work in Gecko60 to prevent these leaks.
        /// Currently, code in bookPreview.ts uses an IntersectionObserver to make videos load when they
        /// become visible. At that point, there will be leaks, but at least one user finds it useful
        /// to see the first frame of the video in preview mode (BL-9875). At least, we don't leak from
        /// just clicking on a book, only if we really look through them.
        /// </summary>
        /// <param name="dom">The dom containing the video elements.</param>
        private static void PreventVideoAutoLoad(HtmlDom dom)
        {
            var videos = dom.SelectVideoSources();

            for (int i = 0; i < (videos?.Count ?? 0); ++i)
            {
                var videoSrc = videos[i];
                var videoTag = videoSrc.ParentNode as XmlElement;
                videoTag?.SetAttribute("preload", "none");
            }
        }

        /// <summary>
        /// It's annoying to put your mouse in the preview pane in Collections tab and get a long image resolution
        /// message popping up. So here we delete the title attribute from any image-container divs. (BL-10341)
        /// This is only used in the preview.
        /// Review: This works, but I'm not really "up" on the preview process. Should this go in bookPreview.ts?
        /// </summary>
        private static void RemoveImageResolutionMessageAndAddMissingImageMessage(HtmlDom dom)
        {
            var imageContainerList = dom.Body.SafeSelectNodes(
                "//div[contains(@class,'bloom-imageContainer')]"
            );
            foreach (XmlElement imageContainer in imageContainerList)
            {
                imageContainer.RemoveAttribute("title");
                foreach (XmlElement img in imageContainer.SafeSelectNodes("img"))
                {
                    var src = img.GetAttribute("src");
                    var alt = img.GetAttribute("alt");
                    if (!String.IsNullOrEmpty(src) && String.IsNullOrEmpty(alt))
                    {
                        var localizedFormatString = LocalizationManager.GetString(
                            "EditTab.Image.AltMsg",
                            "This picture, {0}, is missing or was loading too slowly."
                        );
                        var altValue = String.Format(localizedFormatString, src);
                        img.SetAttribute("alt", altValue);
                    }
                }
            }
        }

        public IEnumerable<IPage> GetPages()
        {
            if (HasFatalError)
                yield break;

            if (_pagesCache == null)
            {
                BuildPageCache();
            }

            foreach (var page in _pagesCache)
            {
                yield return page;
            }
        }

        public IPage GetPage(string id)
        {
            return GetPages().FirstOrDefault(p => p.Id == id);
        }

        private void BuildPageCache()
        {
            _pagesCache = new List<IPage>();

            foreach (
                XmlElement pageNode in OurHtmlDom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-page')]"
                )
            )
            {
                //review: we want to show titles for template books, numbers for other books.
                //this here requires that titles be removed when the page is inserted, kind of a hack.
                string captionI18nId;
                var caption = GetPageLabelFromDiv(pageNode, out captionI18nId);
                if (String.IsNullOrEmpty(caption))
                {
                    caption = "";
                    //we aren't keeping these up to date yet as thing move around, so.... (pageNumber + 1).ToString();
                }
                _pagesCache.Add(CreatePageDecriptor(pageNode, caption, captionI18nId));
            }
        }

        public Dictionary<string, IPage> GetTemplatePagesIdDictionary()
        {
            if (HasFatalError)
                return null;

            var result = new Dictionary<string, IPage>();

            foreach (
                XmlElement pageNode in OurHtmlDom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-page') and not(contains(@data-page, 'singleton'))]"
                )
            )
            {
                string captionI18nId;
                var caption = GetPageLabelFromDiv(pageNode, out captionI18nId);
                result.Add(
                    GetPageIdFromDiv(pageNode),
                    CreatePageDecriptor(pageNode, caption, captionI18nId)
                );
            }
            return result;
        }

        private static string GetPageIdFromDiv(XmlElement pageNode)
        {
            return pageNode.GetAttribute("id");
        }

        private static string GetPageLabelFromDiv(XmlElement pageNode, out string captionI18nId)
        {
            var englishDiv = pageNode.SelectSingleNode(
                "div[contains(@class,'pageLabel') and @lang='en']"
            );
            var caption = (englishDiv == null) ? String.Empty : englishDiv.InnerText;
            captionI18nId = null;
            if (englishDiv != null && englishDiv.Attributes["data-i18n"] != null)
                captionI18nId = englishDiv.Attributes["data-i18n"].Value;
            return caption;
        }

        private IPage CreatePageDecriptor(XmlElement pageNode, string caption, string captionI18nId) //, Action<Image> thumbNailReadyCallback)
        {
            return new Page(this, pageNode, caption, captionI18nId, (page => FindPageDiv(page)));
        }

        private XmlElement FindPageDiv(IPage page)
        {
            //review: could move to page
            var pageElement = OurHtmlDom.RawDom.SelectSingleNodeHonoringDefaultNS(page.XPathToDiv);
            Require.That(pageElement != null, "Page could not be found: " + page.XPathToDiv);

            return pageElement as XmlElement;
        }

        /// <summary>
        /// Insert a copy of templatePage after pageBefore. If numberToAdd is greater than 1, multiple copies are inserted.
        /// </summary>
        /// <returns>the id of the first page added.</returns>
        public string InsertPageAfter(IPage pageBefore, IPage templatePage, int numberToAdd = 1)
        {
            Guard.Against(HasFatalError, "Insert page failed: " + FatalErrorDescription);
            Guard.Against(!IsSaveable, "Tried to edit a non-editable book.");

            ClearCachedDataFromDom();
            bool stylesChanged = false;

            if (templatePage.Book != null) // will be null in some unit tests that are unconcerned with stylesheets
                stylesChanged = HtmlDom.AddStylesheetFromAnotherBook(
                    templatePage.Book.OurHtmlDom,
                    OurHtmlDom
                );

            // And, if it comes from a different book, we may need to copy over some of the user-defined
            // styles from that book. Do this before we set up the new page, which will get a copy of this
            // book's (possibly updated) stylesheet.
            stylesChanged |= AddMissingStylesFromTemplatePage(templatePage);

            XmlDocument dom = OurHtmlDom.RawDom;
            var templatePageDiv = templatePage.GetDivNodeForThisPage();
            var newPageDiv = dom.ImportNode(templatePageDiv, true) as XmlElement;
            BookStarter.SetupPage(newPageDiv, _bookData); //, LockedExceptForTranslation);
            if (!IsSuitableForMakingShells)
            {
                // We need to add these early on for leveled reader statistics not to get messed up
                // when adding a new (empty) page.  See https://issues.bloomlibrary.org/youtrack/issue/BL-9876.
                TranslationGroupManager.UpdateContentLanguageClasses(
                    newPageDiv,
                    _bookData,
                    BookInfo.AppearanceSettings,
                    Language1Tag,
                    Language2Tag,
                    Language3Tag
                );
            }

            SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPageDiv, GetLayout());
            newPageDiv.RemoveAttribute("title"); //titles are just for templates [Review: that's not true for front matter pages, but at the moment you can't insert those, so this is ok]C:\dev\Bloom\src\BloomExe\StyleSheetService.cs
            // If we're a template, make the new page a template one.
            HtmlDom.MakePageWithTemplateStatus(IsSuitableForMakingShells, newPageDiv);
            var elementOfPageBefore = FindPageDiv(pageBefore);

            // This is the only part that needs repeating if we're adding multiple pages at a time.
            var firstPageAdded = newPageDiv; // temporarily
            for (var i = 0; i < numberToAdd; i++)
            {
                var clonedDiv = (XmlElement)newPageDiv.CloneNode(true);
                if (i == 0)
                    firstPageAdded = clonedDiv;
                BookStarter.SetupIdAndLineage(templatePageDiv, clonedDiv);
                BookStarter.UniqueifyIds(clonedDiv);
                elementOfPageBefore.ParentNode.InsertAfter(clonedDiv, elementOfPageBefore);

                CopyAndRenameAudioFiles(clonedDiv, templatePage.Book.FolderPath);
                CopyAndRenameVideoFiles(clonedDiv, templatePage.Book.FolderPath);
                CopyWidgetFilesIfNeeded(clonedDiv, templatePage.Book.FolderPath);
                // Copying of image files is handled below.
            }

            OrderOrNumberOfPagesChanged();
            BuildPageCache();
            var newPage = GetPages().First(p => p.GetDivNodeForThisPage() == firstPageAdded);
            Guard.AgainstNull(newPage, "could not find the page we just added");

            //_pageSelection.SelectPage(CreatePageDecriptor(newPageDiv, "should not show", _collectionSettings.Language1Tag));

            // If copied page references images, copy them.
            foreach (var pathFromBook in BookStorage.GetImagePathsRelativeToBook(newPageDiv))
            {
                var path = Path.Combine(FolderPath, pathFromBook);
                if (!RobustFile.Exists(path))
                {
                    var fileName = Path.GetFileName(path);
                    var sourcePath = Path.Combine(templatePage.Book.FolderPath, fileName);
                    if (RobustFile.Exists(sourcePath))
                        RobustFile.Copy(sourcePath, path);
                }
            }

            //similarly, if the page has stylesheet files we don't have, copy them
            CopyMissingStylesheetFiles(templatePage);

            // and again for scripts (but we currently only worry about ones in the page itself)
            foreach (XmlElement scriptElt in newPageDiv.SafeSelectNodes(".//script[@src]"))
            {
                var fileName = scriptElt.Attributes["src"]?.Value;
                // Bloom Desktop accesses simpleComprehensionQuiz.js from the output/browser folder.
                // Bloom Reader uses the copy of that file which comes with bloom-player.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-8480.
                if (
                    string.IsNullOrEmpty(fileName)
                    || fileName == PublishHelper.kSimpleComprehensionQuizJs
                )
                    continue;
                var destinationPath = Path.Combine(FolderPath, fileName);
                // In other similar operations above we don't overwrite an existing file (e.g., images, css).
                // But our general policy for JS is to go with the latest from our templates.
                // So we copy the smart page's current code; the page content may
                // be specific to it. Of course, if the book contains existing pages that expect
                // an old version of this code, anything could happen. Maintainers of released
                // smart pages will need to consider this. But this will sure help while developing
                // new smart pages.
                // Note that if it's important for old pages to keep using old JS, the maintainer
                // of the quiz page can simply rename the JS file; this code will copy the new JS
                // and any copies of the old page will keep using the old JS which will still be around.
                // But only this strategy allows the code to be updated (e.g., to make the old and new
                // versions of the page work properly together).
                var sourcePath = Path.Combine(templatePage.Book.FolderPath, fileName);
                // Don't try to copy a file over itself.  (See https://issues.bloomlibrary.org/youtrack/issue/BL-7349.)
                if (sourcePath == destinationPath)
                    continue;
                if (RobustFile.Exists(sourcePath))
                    RobustFile.Copy(sourcePath, destinationPath, true);
            }

            if (IsSuitableForMakingShells)
            {
                // If we just added the first template page to a template, it's now usable for adding
                // pages to other books. But the thumbnail for that template, and the template folder
                // it lives in, won't get created unless the user chooses Add Page again.
                // Even if he doesn't (maybe it's a one-page template), we want it to have the folder
                // that identifies it as a template book for the add pages dialog.
                // (We don't want to do so when the book is first created, because it's no good in
                // Add Pages until it has at least one addable page.)
                var templateFolderPath = Path.Combine(
                    FolderPath,
                    PageTemplatesApi.TemplateFolderName
                );
                Directory.CreateDirectory(templateFolderPath); // harmless if it exists already
            }

            MiscUtils.DoOnceOnIdle(() =>
            {
                // This UI updating code generates a lot of API calls, especially when we pass
                // true to pageListChangedEvent. And it is typically called within an API call
                // (implementing Add Page). We want to postpone triggering more API calls
                // until the current one is over, to prevent deadlocks in the BloomServer.
                _pageListChangedEvent?.Raise(stylesChanged);

                InvokeContentsChanged(null);
            });
            return newPage.Id;
        }

        private void CopyWidgetFilesIfNeeded(XmlElement newPageDiv, string sourceBookFolder)
        {
            if (sourceBookFolder == FolderPath)
            {
                // Copying within same book. Reuse the same widget.
                return;
            }

            foreach (var widgetIframe in HtmlDom.GetWidgetIframes(newPageDiv))
            {
                var widgetSource = UrlPathString.CreateFromUrlEncodedString(
                    widgetIframe.GetAttribute("src")
                );
                var sourcePath = Path.Combine(sourceBookFolder, widgetSource.NotEncoded);
                if (RobustFile.Exists(sourcePath))
                {
                    // This combo (create/add) unfortunately needlessly zips and unzips
                    // the widget contents, but it does other things we need like guaranteeing
                    // a unique name and not duplicating activities.
                    var wdgtFilePath = WidgetHelper.CreateWidgetFromHtmlFolder(
                        sourcePath,
                        ensureIndexHtmlFileName: false
                    );
                    var newRelativePath = WidgetHelper
                        .AddWidgetFilesToBookFolder(FolderPath, wdgtFilePath)
                        .UrlEncodedForHttpPath;
                    if (widgetSource.UrlEncodedForHttpPath != newRelativePath)
                    {
                        // This means the existing book had an activity with the same name
                        // but different content. We got a new, unique name and need to update the dom.
                        widgetIframe.SetAttribute("src", newRelativePath);
                    }
                }
            }
        }

        /// <summary>
        /// Copy stylesheet files referenced by the template page that this book doesn't yet have.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-7170.
        /// </remarks>
        private void CopyMissingStylesheetFiles(IPage templatePage)
        {
            var sourceDom = templatePage.Book.OurHtmlDom;
            var sourceFolder = templatePage.Book.FolderPath;
            var destFolder = FolderPath;
            HtmlDom.CopyMissingStylesheetFiles(sourceDom, sourceFolder, destFolder);
        }

        /// <summary>
        /// If we are inserting a page from a different book, or updating the layout of our page to one from a
        /// different book, we may need to copy user-defined styles from that book to our own.
        /// </summary>
        /// <returns>true if anything added</returns>
        /// <param name="templatePage"></param>
        private bool AddMissingStylesFromTemplatePage(IPage templatePage)
        {
            if (templatePage.Book.FolderPath != FolderPath)
            {
                var domForPage = templatePage.Book.GetEditableHtmlDomForPage(templatePage);
                if (domForPage != null) // possibly null only in unit tests?
                {
                    var userStylesOnPage = HtmlDom.GetUserModifiableStylesUsedOnPage(domForPage); // could be empty
                    var existingUserStyles = GetOrCreateUserModifiedStyleElementFromStorage();
                    var newMergedUserStyleXml = HtmlDom.MergeUserStylesOnInsertion(
                        existingUserStyles,
                        userStylesOnPage,
                        out bool didAdd
                    );
                    existingUserStyles.InnerXml = newMergedUserStyleXml;
                    return didAdd;
                }
            }

            return false;
        }

        public static string CollectionKind(Book book)
        {
            var collectionKind = "other";

            if (book == null || book.HasFatalError)
            {
                // not exactly a kind of collection, but a convenient way to indicate these states,
                // in which edit/make button should not show at all.
                collectionKind = "error";
            }
            else if (book != null && book.IsInEditableCollection)
            {
                collectionKind = "main";
            }
            // Review: we're tentatively thinking that "delete book" and "open folder on disk"
            // will both be enabled for all but factory collections. Currently, Bloom is more
            // restrictive on delete: only books in main or "Books from BloomLibrary.org" can be deleted.
            // But there doesn't seem to be any reason to prevent deleting books from e.g. a bloompack.
            else if (
                book != null
                && BloomFileLocator.IsInstalledFileOrDirectory(book.CollectionSettings.FolderPath)
            )
            {
                collectionKind = "factory";
            }

            return collectionKind;
        }

        /// <summary>
        /// Duplicate the page and return the ID of the new one.
        /// </summary>
        public string DuplicatePage(IPage page, int numberToAdd = 1)
        {
            // Can be achieved by just using the current page as both the place to insert after
            // and the template to copy.
            // Note that Pasting a page uses the same routine; unit tests for duplicate and copy/paste
            // take advantage of our knowledge that the code is shared so that between them they cover
            // the important code paths. If the code stops being shared, we should extend test
            // coverage appropriately.
            return InsertPageAfter(page, page, numberToAdd);
        }

        private void CopyAndRenameAudioFiles(XmlElement newpageDiv, string sourceBookFolder)
        {
            foreach (
                var audioElement in HtmlDom
                    .SelectRecordableDivOrSpans(newpageDiv)
                    .Cast<XmlElement>()
                    .ToList()
            )
            {
                var oldId = audioElement.GetStringAttribute("id");
                var id = HtmlDom.SetNewHtmlIdValue(audioElement);
                if (string.IsNullOrEmpty(oldId))
                    continue;
                var sourceAudioFilePath = Path.Combine(
                    Path.Combine(sourceBookFolder, "audio"),
                    oldId + ".wav"
                );
                var newAudioFolderPath = Path.Combine(FolderPath, "audio");
                var newAudioFilePath = Path.Combine(newAudioFolderPath, id + ".wav");
                Directory.CreateDirectory(newAudioFolderPath);
                if (RobustFile.Exists(sourceAudioFilePath))
                {
                    RobustFile.Copy(sourceAudioFilePath, newAudioFilePath);
                }

                var mp3Path = Path.ChangeExtension(sourceAudioFilePath, "mp3");
                var newMp3Path = Path.ChangeExtension(newAudioFilePath, "mp3");
                if (RobustFile.Exists(mp3Path))
                {
                    RobustFile.Copy(mp3Path, newMp3Path);
                }
            }
        }

        private void CopyAndRenameVideoFiles(XmlElement newpageDiv, string sourceBookFolder)
        {
            foreach (
                var source in newpageDiv
                    .SafeSelectNodes(".//video/source")
                    .Cast<XmlElement>()
                    .ToList()
            )
            {
                var src = source.GetAttribute("src");
                // old source may have a param, too, but we don't currently need to keep it.
                string timings;
                src = SignLanguageApi.StripTimingFromVideoUrl(src, out timings);
                if (String.IsNullOrWhiteSpace(src))
                    continue;
                var oldVideoPath = Path.Combine(sourceBookFolder, src);
                // If the video file doesn't exist, don't bother adjusting anything.
                // If it does exist, copy it with a new name based on the current one, similarly
                // to how we've been renaming image files that already exist in the book's folder.
                if (RobustFile.Exists(oldVideoPath))
                {
                    var extension = Path.GetExtension(src);
                    var oldFileName = Path.GetFileNameWithoutExtension(src);
                    int count = 0;
                    string newVideoPath;
                    do
                    {
                        ++count;
                        var newFileName =
                            oldFileName + "-" + count.ToString(CultureInfo.InvariantCulture);
                        newVideoPath = Path.Combine(FolderPath, "video", newFileName + extension);
                    } while (RobustFile.Exists(newVideoPath));

                    Directory.CreateDirectory(Path.GetDirectoryName(newVideoPath));
                    RobustFile.Copy(oldVideoPath, newVideoPath, false);

                    source.SetAttribute(
                        "src",
                        "video/"
                            + UrlPathString
                                .CreateFromUnencodedString(Path.GetFileName(newVideoPath))
                                .UrlEncoded
                            + (string.IsNullOrEmpty(timings) ? "" : "#t=" + timings)
                    );
                }
            }
        }

        public void DeletePage(IPage page)
        {
            Guard.Against(HasFatalError, "Delete page failed: " + FatalErrorDescription);
            Guard.Against(!IsSaveable, "Tried to edit a non-editable book.");

            if (GetPages().Count() < 2)
                return;

            ClearCachedDataFromDom();
            //_pagesCache.Remove(page);
            OrderOrNumberOfPagesChanged();

            var pageNode = FindPageDiv(page);
            pageNode.ParentNode.RemoveChild(pageNode);
            Storage.Dom.UpdatePageNumberAndSideClassOfPages(
                CollectionSettings.CharactersForDigitsForPageNumbers,
                IsPrimaryLanguageRtl
            );

            _pageListChangedEvent?.Raise(false);

            InvokeContentsChanged(null);
        }

        public bool IsPrimaryLanguageRtl => _bookData.Language1.IsRightToLeft;

        private void OrderOrNumberOfPagesChanged()
        {
            OurHtmlDom.UpdatePageNumberAndSideClassOfPages(
                CollectionSettings.CharactersForDigitsForPageNumbers,
                IsPrimaryLanguageRtl
            );
        }

        private void ClearCachedDataFromDom()
        {
            _pagesCache = null;
            ResetPreviewDom();
        }

        internal void ResetPreviewDom()
        {
            _previewDom = null;
        }

        /// <summary>
        /// Internal for use by BookMetadataApi, which wants to know the number of the highest page number in the book.
        /// </summary>
        /// <returns></returns>
        internal int GetLastNumberedPageNumber()
        {
            var lastPageNumber = 0;
            foreach (var page in GetPages())
            {
                if (page.IsBackMatter)
                    return lastPageNumber;

                string dummy;
                page.GetCaptionOrPageNumber(ref lastPageNumber, out dummy);
            }
            return lastPageNumber;
        }

        public BookData BookData => _bookData;

        public void InsertFullBleedMarkup(XmlElement body)
        {
            if (FullBleed)
            {
                HtmlDom.InsertFullBleedMarkup(body);
            }
        }

        public bool FullBleed =>
            BookData.GetVariableOrNull("fullBleed", "*").Xml == "true"
            && CollectionSettings.HaveEnterpriseFeatures;

        /// <summary>
        /// Save the page content to the DOM.
        /// Return true if needToDoFullSave is true, or if this method discovers another reason we need to do a full save.
        /// Returns as an out param the page element from the book's dom that got modified.
        /// </summary>
        public bool UpdateDomFromEditedPage(
            HtmlDom editedPageDom,
            out XmlElement pageToSaveToDisk,
            bool needToDoFullSave = true
        )
        {
            // This is needed if the user did some ChangeLayout (origami) manipulation. This will populate new
            // translationGroups with .bloom-editables and set the proper classes on those editables to match the current multilingual settings.
            UpdateEditableAreasOfElement(editedPageDom);

            //replace the corresponding page contents in our DOM with what is in this PageDom
            XmlElement pageFromEditedDom = editedPageDom.SelectSingleNodeHonoringDefaultNS(
                "//div[contains(@class, 'bloom-page')]"
            );
            string pageId = pageFromEditedDom.GetAttribute("id");
            pageToSaveToDisk = GetPageFromStorage(pageId);

            HtmlDom.ProcessPageAfterEditing(pageToSaveToDisk, pageFromEditedDom);
            HtmlDom.SetImageAltAttrsFromDescriptions(pageToSaveToDisk, Language1Tag);

            // The main condition for being able to just write the page is that no shareable data on the
            // page changed during editing. If that's so we can skip this step.
            if (needToDoFullSave)
                _bookData.SuckInDataFromEditedDom(editedPageDom, BookInfo); //this will do an updatetitle

            // When the user edits the styles on a page, the new or modified rules show up in a <style/> element with title "userModifiedStyles".
            // Here we copy that over to the book DOM.
            var userModifiedStyles = HtmlDom.GetUserModifiedStyleElement(editedPageDom.Head);
            var stylesChanged = false;
            if (userModifiedStyles != null)
            {
                var userModifiedStyleElementFromStorage =
                    GetOrCreateUserModifiedStyleElementFromStorage();
                if (userModifiedStyleElementFromStorage.InnerXml != userModifiedStyles.InnerXml)
                {
                    userModifiedStyleElementFromStorage.InnerXml = userModifiedStyles.InnerXml;
                    stylesChanged = true; // note, this is not shared data in the sense that needs SuckInDataFromEditedDom
                }

                //Debug.WriteLine("Incoming User Modified Styles:   " + userModifiedStyles.OuterXml);
            }
            return needToDoFullSave || stylesChanged;
        }

        /// <summary>
        /// This combines saving the page content to the DOM and saving the DOM to disk.
        /// </summary>
        public void SavePage(HtmlDom editedPageDom, bool needToDoFullSave = true)
        {
            Debug.Assert(IsSaveable);
            try
            {
                var reallyNeedFullSave = UpdateDomFromEditedPage(
                    editedPageDom,
                    out XmlElement pageToSaveToDisk,
                    needToDoFullSave
                );

                SavePageToDisk(pageToSaveToDisk, reallyNeedFullSave);
            }
            catch (Exception error)
            {
                var msg = LocalizationManager.GetString(
                    "Errors.CouldNotSavePage",
                    "Bloom had trouble saving a page. Please report the problem to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!"
                );
                ErrorReport.NotifyUserOfProblem(error, msg);
            }
        }

        /// <summary>
        /// Finish a delayed save. pageToSaveToDisk should be the value from the out param of UpdateDomFromEditedPage().
        /// It is the one page that needs saving, if reallyNeedFullSave is false; if that is true, it is not used.
        /// </summary>
        public void SavePageToDisk(XmlElement pageToSaveToDisk, bool reallyNeedFullSave)
        {
            try
            {
                try
                {
                    if (pageToSaveToDisk != null && !reallyNeedFullSave)
                    {
                        string pageId = pageToSaveToDisk.GetAttribute("id");
                        // nothing changed outside this page. We can do a much more efficient write operation.
                        // (On a 200+ page book, like the one in BL-7253, this version of updating the page
                        // runs in about a half second instead of two and a half. Moreover, on such a book,
                        // running the full Save rather quickly fragments the heap...allocating about 16 7-megabyte
                        // memory chunks in each Save...to the point where Bloom runs out of memory.)

                        SaveForPageChanged(pageId, pageToSaveToDisk);
                    }
                    else
                    {
                        // We've seen pages get emptied out, and we don't know why. This is a safety check.
                        // See BL-13078, BL-13120, BL-13123, and BL-13143 for examples.
                        if (
                            pageToSaveToDisk != null
                            && BookStorage.CheckForEmptyMarginBoxOnPage(pageToSaveToDisk)
                        )
                        {
                            // This has been logged and reported to the user. We don't want to save the empty page.
                            return;
                        }
                        Save();
                    }
                }
                catch (UnauthorizedAccessException e)
                {
                    BookStorage.ShowAccessDeniedErrorReport(e);
                    return;
                }

                if (!BookInfo.FileNameLocked)
                    Storage.UpdateBookFileAndFolderName(CollectionSettings);
                //review used to have   UpdateBookFolderAndFileNames(data);
            }
            catch (Exception error)
            {
                var msg = LocalizationManager.GetString(
                    "Errors.CouldNotSavePage",
                    "Bloom had trouble saving a page. Please report the problem to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!"
                );
                ErrorReport.NotifyUserOfProblem(error, msg);
            }
        }

        //        /// <summary>
        //        /// Gets the first element with the given tag & id, within the page-div with the given id.
        //        /// </summary>
        //        private XmlElement GetStorageNode(string pageDivId, string tag, string elementId)
        //        {
        //            var query = String.Format("//div[@id='{0}']//{1}[@id='{2}']", pageDivId, tag, elementId);
        //            var matches = OurHtmlDom.SafeSelectNodes(query);
        //            if (matches.Count != 1)
        //            {
        //                throw new ApplicationException("Expected one match for this query, but got " + matches.Count + ": " + query);
        //            }
        //            return (XmlElement)matches[0];
        //        }

        /// <summary>
        /// The <style title='userModifiedStyles'/> element is where we keep our user-modifiable style information
        /// </summary>
        internal XmlElement GetOrCreateUserModifiedStyleElementFromStorage()
        {
            return GetOrCreateUserModifiedStyleElementFromStorage(OurHtmlDom.Head);
        }

        public static XmlElement GetOrCreateUserModifiedStyleElementFromStorage(
            XmlElement headElement
        )
        {
            var userStyleElement = HtmlDom.GetUserModifiedStyleElement(headElement);
            if (userStyleElement == null)
                return HtmlDom.AddEmptyUserModifiedStylesNode(headElement);

            var coverColorElement = HtmlDom.GetCoverColorStyleElement(headElement);
            // If the user defines the cover color, the two elements could end up being the same.
            if (coverColorElement == null || coverColorElement == userStyleElement)
                return userStyleElement;

            // We have both style elements. Make sure they're in the right order.
            // BL -4266 was a problem if the 'coverColor' was listed first.
            headElement.RemoveChild(coverColorElement);
            headElement.InsertAfter(coverColorElement, userStyleElement);
            return userStyleElement;
        }

        /// <summary>
        /// Gets the first element with the given tag & id, within the page-div with the given id.
        /// </summary>
        private XmlElement GetPageFromStorage(string pageDivId)
        {
            var query = $"//div[@id='{pageDivId}']";
            var matches = OurHtmlDom.SafeSelectNodes(query);
            if (matches.Count != 1)
            {
                throw new ApplicationException(
                    "Expected one match for this query, but got " + matches.Count + ": " + query
                );
            }
            return (XmlElement)matches[0];
        }

        /// <summary>
        /// Move a page to somewhere else in the book
        /// </summary>
        public bool RelocatePage(IPage page, int indexOfItemAfterRelocation)
        {
            Guard.Against(HasFatalError, "Move page failed: " + FatalErrorDescription);
            Guard.Against(!IsSaveable, "Tried to edit a non-editable book.");

            if (!CanRelocatePageAsRequested(indexOfItemAfterRelocation))
            {
                return false;
            }

            ClearCachedDataFromDom();

            var pages = GetPageElements();
            var pageDiv = FindPageDiv(page);
            var body = pageDiv.ParentNode;
            body.RemoveChild(pageDiv);
            if (indexOfItemAfterRelocation == 0)
            {
                body.InsertBefore(pageDiv, body.FirstChild);
            }
            else
            {
                body.InsertAfter(pageDiv, pages[indexOfItemAfterRelocation - 1]);
            }
            OrderOrNumberOfPagesChanged();
            BuildPageCache();
            Save();
            InvokeContentsChanged(null);
            return true;
        }

        public void ReloadFromDisk(string renamedTo)
        {
            if (renamedTo != null)
                BookInfo.FolderPath = renamedTo;
            ClearCachedDataFromDom(); // before updating storage, which sends some events that could use the obsolete one
            Storage.ReloadFromDisk(
                renamedTo,
                () =>
                {
                    // This needs to happen after we've created the new DOM, but before
                    // we start broadcasting rename events that may assume the book is
                    // in a consistent state.
                    _bookData = new BookData(
                        OurHtmlDom,
                        CollectionSettings,
                        UpdateImageMetadataAttributes
                    );
                }
            );
            InvokeContentsChanged(null);
        }

        internal XmlNodeList GetPageElements()
        {
            return OurHtmlDom.SafeSelectNodes("/html/body//div[contains(@class,'bloom-page')]");
        }

        private bool CanRelocatePageAsRequested(int indexOfItemAfterRelocation)
        {
            int upperBounds = GetIndexOfFirstBackMatterPage();
            if (upperBounds < 0)
                upperBounds = 10000;

            return indexOfItemAfterRelocation > GetIndexOfLastFrontMatterPage()
                && indexOfItemAfterRelocation < upperBounds;
        }

        private int GetIndexOfLastFrontMatterPage()
        {
            XmlElement lastFrontMatterPage =
                OurHtmlDom.RawDom.SelectSingleNode(
                    "(/html/body/div[contains(@class,'bloom-frontMatter')])[last()]"
                ) as XmlElement;
            if (lastFrontMatterPage == null)
                return -1;
            return GetIndexOfPage(lastFrontMatterPage);
        }

        private int GetIndexOfFirstBackMatterPage()
        {
            XmlElement firstBackMatterPage =
                OurHtmlDom.RawDom.SelectSingleNode(
                    "(/html/body/div[contains(@class,'bloom-backMatter')])[position()=1]"
                ) as XmlElement;
            if (firstBackMatterPage == null)
                return -1;
            return GetIndexOfPage(firstBackMatterPage);
        }

        private int GetIndexOfPage(XmlElement pageElement)
        {
            var elements = GetPageElements();
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i] == pageElement)
                    return i;
            }
            return -1;
        }

        public HtmlDom GetDomForPrinting(
            PublishModel.BookletPortions bookletPortion,
            BookCollection currentBookCollection,
            BookServer bookServer,
            Layout pageLayout
        )
        {
            var printingDom = GetBookDomWithStyleSheets("previewMode.css", "origami.css");

            if (IsFolio)
            {
                AddChildBookContentsToFolio(printingDom, currentBookCollection, bookServer);
            }

            //we do this now becuase the publish ui allows the user to select a different layout for the pdf than what is in the book file
            SizeAndOrientation.UpdatePageSizeAndOrientationClasses(printingDom.RawDom, pageLayout);

            //whereas the base is to our embedded server during editing, it's to the file folder
            //when we make a PDF, because we wan the PDF to use the original hi-res versions

            BookStorage.SetBaseForRelativePaths(printingDom, FolderPath);

            DeletePages(
                printingDom.RawDom,
                p => p.GetAttribute("class").ToLowerInvariant().Contains("bloom-nonprinting")
            );
            PublishHelper.RemoveEnterprisePagesIfNeeded(
                _bookData,
                printingDom,
                printingDom.GetPageElements().ToList()
            );

            switch (bookletPortion)
            {
                case PublishModel.BookletPortions.AllPagesNoBooklet:
                    break;
                case PublishModel.BookletPortions.BookletCover:
                    DeletePages(
                        printingDom.RawDom,
                        p => !p.GetAttribute("class").ToLowerInvariant().Contains("cover")
                    );
                    break;
                case PublishModel.BookletPortions.BookletPages:
                    DeletePages(
                        printingDom.RawDom,
                        p => p.GetAttribute("class").ToLowerInvariant().Contains("cover")
                    );
                    break;
                default:
                    throw new ArgumentOutOfRangeException("bookletPortion");
            }
            // Do this after we remove unwanted pages; otherwise, the page removal code must also remove the media boxes.
            if (FullBleed)
            {
                InsertFullBleedMarkup(printingDom.Body);
            }
            if (!FullBleed)
                AddCoverColor(printingDom, Color.White);
            AddPreviewJavascript(printingDom);
            return printingDom;
        }

        /// <summary>
        /// used when this book is a "master"/"folio" book that is used to bring together a number of other books in the collection
        /// </summary>
        /// <param name="printingDom"></param>
        /// <param name="currentBookCollection"></param>
        /// <param name="bookServer"></param>
        private void AddChildBookContentsToFolio(
            HtmlDom printingDom,
            BookCollection currentBookCollection,
            BookServer bookServer
        )
        {
            XmlNode currentLastContentPage = GetLastPageForInsertingNewContent(printingDom);

            int cumulativePageNum = 1;
            string lastPageNumStr = currentLastContentPage.Attributes
                ?.GetNamedItem("data-page-number")
                ?.InnerText;
            if (int.TryParse(lastPageNumStr, out int lastPageNum))
            {
                cumulativePageNum = lastPageNum;
            }

            //currently we have no way of filtering them, we just take them all
            foreach (var bookInfo in currentBookCollection.GetBookInfos())
            {
                if (bookInfo.IsFolio)
                    continue;
                var childBook = bookServer.GetBookFromBookInfo(bookInfo);

                //this will set the class bloom-content1 on the correct language
                //this happens anyhow if the page was ever looked at in the Edti Tab
                //But if we are testing a collection's folio pdf'ing ability on a newly-generated
                //SHRP collection, and we don't do this, we see lots of sample text because every
                //bloom-editable has "bloom-content1", even the "Z" language ones.
                childBook.UpdateEditableAreasOfElement(childBook.OurHtmlDom);

                //add links to the template css needed by the children.

                HtmlDom.AddStylesheetFromAnotherBook(childBook.OurHtmlDom, printingDom);

                foreach (
                    XmlElement pageDiv in childBook.OurHtmlDom.RawDom.SafeSelectNodes(
                        "/html/body//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))]"
                    )
                )
                {
                    XmlElement importedPage = (XmlElement)
                        printingDom.RawDom.ImportNode(pageDiv, true);

                    if (!String.IsNullOrWhiteSpace(importedPage.GetAttribute("data-page-number")))
                    {
                        ++cumulativePageNum;
                        importedPage.SetAttribute("data-page-number", cumulativePageNum.ToString());
                    }

                    currentLastContentPage.ParentNode.InsertAfter(
                        importedPage,
                        currentLastContentPage
                    );
                    currentLastContentPage = importedPage;

                    foreach (
                        XmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(
                            importedPage
                        )
                    )
                    {
                        var bookFolderName = Path.GetFileName(bookInfo.FolderPath);
                        var path = HtmlDom.GetImageElementUrl(img);
                        var pathRelativeToFolioFolder =
                            "../" + bookFolderName + "/" + path.NotEncoded; // want query as well as filepath
                        //NB: URLEncode would replace spaces with '+', which is ok in the parameter section, but not the URL
                        //So we are using UrlPathEncode

                        HtmlDom.SetImageElementUrl(
                            img,
                            UrlPathString.CreateFromUnencodedString(pathRelativeToFolioFolder)
                        );
                    }
                }
            }
        }

        private XmlElement GetLastPageForInsertingNewContent(HtmlDom printingDom)
        {
            var lastPage =
                printingDom.RawDom.SelectSingleNode(
                    "/html/body//div[contains(@class, 'bloom-page') and not(contains(@class,'bloom-frontMatter')) and not(contains(@class,'bloom-backMatter'))][last()]"
                ) as XmlElement;
            if (lastPage == null)
            {
                //currently nothing but front and back matter
                var lastFrontMatter =
                    printingDom.RawDom.SelectSingleNode(
                        "/html/body//div[contains(@class,'bloom-frontMatter')][last()]"
                    ) as XmlElement;
                if (lastFrontMatter == null)
                    throw new ApplicationException(
                        "GetLastPageForInsertingNewContent() found no content pages nor frontmatter"
                    );
                return lastFrontMatter;
            }
            else
            {
                return lastPage;
            }
        }

        /// <summary>
        /// this is used for configuration, where we do want to offer up the original file.
        /// </summary>
        /// <returns></returns>
        public string GetPathHtmlFile()
        {
            return Storage.PathToExistingHtml;
        }

        public PublishModel.BookletLayoutMethod GetDefaultBookletLayoutMethod()
        {
            return GetBookletLayoutMethod(GetLayout());
        }

        public PublishModel.BookletLayoutMethod GetBookletLayoutMethod(Layout layout)
        {
            //NB: all we support at the moment is specifying "Calendar"
            if (
                OurHtmlDom
                    .SafeSelectNodes(
                        String.Format(
                            "//meta[@name='defaultBookletLayout' and @content='Calendar']"
                        )
                    )
                    .Count > 0
            )
                return PublishModel.BookletLayoutMethod.Calendar;
            else
            {
                if (
                    layout.SizeAndOrientation.IsLandScape
                    && layout.SizeAndOrientation.PageSizeName == "A5"
                )
                    return PublishModel.BookletLayoutMethod.CutAndStack;
                return PublishModel.BookletLayoutMethod.SideFold;
            }
        }

        /// <summary>
        ///Under normal conditions, this isn't needed, because it is done when a book is first created. But thing might have changed:
        /// *changing xmatter pack, and update to it, changing the languages, etc.
        /// *the book was dragged from another project
        /// *the editing language was changed.
        /// Under those conditions, if we didn't, for example, do a PrepareElementsInPageOrDocument, we would end up with no
        /// editable items, because there are no elements in our language.
        /// </summary>
        public void PrepareForEditing()
        {
            // If the book was in a TC and not checked out when we selected it, some migration
            // that might be helpful or even necessary when editing it was skipped. Do it now.
            // If it was already checked out or not in a TC, this will be a no-op, because
            // we did it all when we selected it.
            EnsureUpToDate(new NullProgress());

            //We could re-enable RebuildXMatter() here later, so that we get this nice refresh each time.
            //But currently this does some really slow image compression:	RebuildXMatter(RawDom);
            UpdateEditableAreasOfElement(OurHtmlDom);
        }

        /// <summary>
        /// This is called both for the whole book, and for individual pages when the user uses Origami to make changes to the layout of the page.
        /// It would be nicer in the HtmlDom, but it uses knowledge about the collection and book languages that the DOM doesn't have.
        /// </summary>
        /// <param name="elementToUpdate"></param>
        public void UpdateEditableAreasOfElement(HtmlDom dom)
        {
            var language1Tag = Language1Tag;
            var language2Tag = Language2Tag;
            var language3Tag = Language3Tag;
            foreach (XmlElement div in dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]"))
            {
                TranslationGroupManager.PrepareElementsInPageOrDocument(div, _bookData);
                TranslationGroupManager.UpdateContentLanguageClasses(
                    div,
                    _bookData,
                    BookInfo.AppearanceSettings,
                    language1Tag,
                    language2Tag,
                    language3Tag
                );
            }
        }

        public string CheckForErrors()
        {
            var errors = Storage.GetValidateErrors();
            if (!String.IsNullOrEmpty(errors))
            {
                HasFatalError = true;
                FatalErrorDescription = errors;
            }
            return errors ?? "";
        }

        public void CheckBook(IProgress progress, string pathToFolderOfReplacementImages = null)
        {
            Storage.CheckBook(progress, pathToFolderOfReplacementImages);
        }

        public virtual Layout GetLayout()
        {
            return Layout.FromDom(OurHtmlDom, Layout.A5Portrait);
        }

        public IEnumerable<Layout> GetSizeAndOrientationChoices()
        {
            try
            {
                return SizeAndOrientation.GetSizeAndOrientationChoices(
                    OurHtmlDom,
                    Storage.GetFileLocator()
                );
            }
            catch (Exception error)
            {
                HasFatalError = true;
                FatalErrorDescription = error.Message;
                throw error;
            }
        }

        public void SetLayout(Layout layout)
        {
            SizeAndOrientation.AddClassesForLayout(OurHtmlDom, layout);
        }

        /// <summary>
        /// This is used when the user elects to apply the same image metadata to all images.
        /// </summary>
        public void CopyImageMetadataToWholeBookAndSave(Metadata metadata, IProgress progress)
        {
            try
            {
                ImageUpdater.CopyImageMetadataToWholeBook(
                    Storage.FolderPath,
                    OurHtmlDom,
                    metadata,
                    progress
                );
            }
            catch (UnauthorizedAccessException e)
            {
                BookStorage.ShowAccessDeniedErrorReport(e);
                return; // Probably not much point to saving if copying the image metadata didn't fully complete successfully
            }
            Save();
        }

        public Metadata GetLicenseMetadata()
        {
            //BookCopyrightAndLicense.LogMetdata(OurHtmlDom);
            var result = BookCopyrightAndLicense.GetMetadata(OurHtmlDom, _bookData);

            //Logger.WriteEvent("After");
            //BookCopyrightAndLicense.LogMetdata(OurHtmlDom);
            return result;
        }

        public void SetMetadata(Metadata metadata)
        {
            BookCopyrightAndLicense.SetMetadata(
                metadata,
                OurHtmlDom,
                FolderPath,
                _bookData,
                BookInfo.MetaData.UseOriginalCopyright
            );
            BookInfo.SetLicenseAndCopyrightMetadata(metadata);
        }

        public void SetTitle(string name)
        {
            OurHtmlDom.Title = name;
        }

        public void ExportXHtml(string path)
        {
            XmlHtmlConverter.GetXmlDomFromHtmlFile(Storage.PathToExistingHtml, true).Save(path);
        }

        bool OkToChangeFileAndFolderName
        {
            get
            {
                if (LockDownTheFileAndFolderName || BookInfo.FileNameLocked)
                    return false;
                return IsSaveable;
            }
        }

        public void Save()
        {
            // If you add something here, consider whether it is needed in SaveForPageChanged().
            // I believe all the things currently here before the actual Save are not needed
            // in the cases where we use SaveForPageChanged(). We switch to Save if any
            // book data changed, which will be true if we're changing the title and thus
            // the book's location. We also do a full Save after bringing the book up to date;
            // after that, there shouldn't be any obsolete sound attributes.
            // (In fact, since we bring a book up to date before editing, and that code
            // does the removal, I don't see why it's needed here either.)
            Guard.Against(HasFatalError, "Save failed: " + FatalErrorDescription);
            Guard.Against(!IsSaveable, "Tried to save a non-editable book.");
            if (!IsSaveable)
            {
                NonFatalProblem.Report(
                    ModalIf.Alpha,
                    PassiveIf.All,
                    "Bloom attempted to Save a book which cannot currently be saved: " + FolderPath
                );
                return;
            }

            RemoveObsoleteSoundAttributes(OurHtmlDom);
            _bookData.UpdateVariablesAndDataDivThroughDOM(BookInfo); //will update the title if needed
            if (OkToChangeFileAndFolderName)
            {
                Storage.UpdateBookFileAndFolderName(CollectionSettings); //which will update the file name if needed
            }
            if (IsSuitableForMakingShells)
            {
                // A template book is considered to be its own source, so update the source to match the
                // current book location.
                PageTemplateSource = Path.GetFileName(FolderPath);
            }

            try
            {
                Storage.Save();
            }
            catch (UnauthorizedAccessException e)
            {
                BookStorage.ShowAccessDeniedErrorReport(e);
                return;
            }

            DoPostSaveTasks();
        }

        public void SaveForPageChanged(string pageId, XmlElement modifiedPage)
        {
            Guard.Against(HasFatalError, "Save failed: " + FatalErrorDescription);
            Guard.Against(!IsSaveable, "Tried to save a non-editable book.");
            Storage.SaveForPageChanged(pageId, modifiedPage);
            DoPostSaveTasks();
        }

        private void DoPostSaveTasks()
        {
            ResetPreviewDom();
            // Tell the accessibility checker window (and any future subscriber) to re-compute.
            // This Task.Delay() helps even with a delay of 0, becuase it means we get to finish with this command.
            // I'm chooing 1 second at the moment as that feels about the longest I would want to
            // wait to see if what I did made and accesibility check change. Of course we're *probably*
            // ready to run this much, much sooner.
            Task.Delay(1000).ContinueWith((task) => _bookSavedEvent.Raise(this));

            InvokeContentsChanged(null); //enhance: could we detect if anything actually changed?
        }

        /// <summary>
        /// Remove any obsolete data-duration attributes that the typescript code failed to remove.
        /// </summary>
        /// <remarks>
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3671.
        /// </remarks>
        private void RemoveObsoleteSoundAttributes(HtmlDom htmlDom)
        {
            foreach (
                var span in htmlDom.RawDom
                    .SafeSelectNodes("//span[@data-duration and @id]")
                    .Cast<XmlElement>()
            )
            {
                if (
                    !AudioProcessor.DoesAudioExistForSegment(
                        Storage.FolderPath,
                        span.GetStringAttribute("id")
                    )
                )
                    span.RemoveAttribute("data-duration"); // file no longer exists, shouldn't have any duration setting
            }
        }

        /// <summary>
        /// Remove any obsolete explicit image size and position left over from earlier versions of Bloom, before we had object-fit:contain.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-9401.  A similar fix exists in bookEdit/js/bloomImages.ts, but that applies
        /// only when a page is opened in the edit tab.
        /// </remarks>
        private void RemoveObsoleteImageAttributes(HtmlDom htmlDom)
        {
            foreach (
                var img in htmlDom.RawDom
                    .SafeSelectNodes(
                        "//div[contains(@class,'bloom-imageContainer')]/img[@style|@width|@height]"
                    )
                    .Cast<XmlElement>()
            )
            {
                if (
                    img.ParentNode
                        .GetOptionalStringAttribute("class", "")
                        .Contains("bloom-scale-with-code")
                )
                    continue;
                var style = img.GetOptionalStringAttribute("style", "");
                var fixedStyle = RemoveSizeStyling(style);
                if (String.IsNullOrEmpty(fixedStyle))
                    img.RemoveAttribute("style");
                else if (fixedStyle != style)
                    img.SetAttribute("style", fixedStyle);
                img.RemoveAttribute("width");
                img.RemoveAttribute("height");
            }
        }

        private string RemoveSizeStyling(string style)
        {
            // For example, style="width: 404px; height: 334px; margin-left: 1px; margin-top: 0px;"
            // should reduce to "".
            var style1 = Regex.Replace(style, "width: *[0-9a-z]+;", "");
            var style2 = Regex.Replace(style1, "height: *[0-9a-z]+;", "");
            var style3 = Regex.Replace(style2, "margin-left: *[0-9a-z]+;", "");
            var style4 = Regex.Replace(style3, "margin-top: *[0-9a-z]+;", "");
            return style4.Trim();
        }

        //used by the command-line "hydrate" command
        public bool LockDownTheFileAndFolderName { get; set; }

        //TODO: remove this in favor of meta data (the later currently doesn't appear to have access to lineage, I need to ask JT about that)
        public string GetBookLineage()
        {
            return OurHtmlDom.GetMetaValue("bloomBookLineage", "");
        }

        public bool IsCalendar
        {
            get
            {
                if (OurHtmlDom == null)
                    return false;

                return OurHtmlDom.GetMetaValue("defaultBookletLayout", "") == "Calendar";
            }
        }

        public MultiTextBase GetDataItem(string name)
        {
            return _bookData.GetMultiTextVariableOrEmpty(name);
        }

        public virtual IBookStorage Storage { get; }

        /// <summary>
        /// This gets called as a result of a UI action. It sets the new topic in our data,
        /// but doesn't do anything related to how it is displayed on the page.
        /// The way to think about this is that we're aiming for a more react™-style flow.
        /// </summary>
        public void SetTopic(string englishTopicAsKey)
        {
            _bookData.Set("topic", XmlString.FromUnencoded(englishTopicAsKey), "en");
        }

        /// <summary>
        /// Compute a hash for all of the book related files that will detect any significant
        /// changes to a Bloom book.  All of the significant files in the folder and subfolders
        /// and basic collection files in the parent folder are included in the hash.
        /// </summary>
        /// <param name="bookFilePath">path to the book's HTML file inside its folder</param>
        public static string ComputeHashForAllBookRelatedFiles(string bookFilePath)
        {
            return MakeVersionCode(
                RobustFile.ReadAllText(bookFilePath, Encoding.UTF8),
                bookFilePath
            );
        }

        /// <summary>
        /// Make a version code which will detect any significant changes to the content of a bloom book.
        /// fileContent is typically the content of the file at filePath which is the book's main HTM file;
        /// however (for testing) filePath may be omitted.
        /// The method computes a SHA of the file content and, if a path is passed, all other files
        /// in the same folder and its subfolders (plus collection level files in the parent folder.)
        /// The file is transformed somewhat so that (some) changes that are not significant are ignored.
        /// Notes:
        /// - renaming a file may or may not produce a different code (depends on whether it changes
        /// the alphabetical order of the files).
        /// - pdf files are currently omitted
        /// - audio files could be omitted until we start supporting audio, but as that is planned I
        /// have not chosen to omit them
        /// - I am not sure that this will reliably give the same result when run on Linux and Windows.
        /// For one thing, depending on the exact file transfer process, one or more files might have
        /// different line endings, which is enough to produce a different SHA.
        /// </summary>
        /// <remarks>
        /// This method is used by TeamCollection, bulk upload, and a few other places to detect
        /// changes to books.
        /// </remarks>
        public static string MakeVersionCode(string fileContent, string filePath = null)
        {
            return RetryUtility.Retry(() => MakeVersionCodeInternal(fileContent, filePath));
        }

        private static string MakeVersionCodeInternal(string fileContent, string filePath = null)
        {
            //var debugBldr = new StringBuilder();
            //string debugPath = null;
            var simplified = fileContent;
            // In general, whitespace sequences are equivalent to a single space.
            // If the user types multiple spaces all but one will be removed.
            simplified = new Regex(@"\s+").Replace(simplified, " ");
            // Between the end of one tag and the start of the next white space doesn't count at all
            simplified = new Regex(@">\s+<").Replace(simplified, "><");
            // A space before (or inside) a <br/> element doesn't matter.
            simplified = new Regex(@"\s+<br\s*/>").Replace(simplified, "<br/>");
            simplified = new Regex(@"<br\s+/>").Replace(simplified, "<br/>");
            // Ignore the generator metadata: precise version of Bloom doesn't matter
            simplified = new Regex(@"<meta name=""Generator""[^>]*></meta>").Replace(
                simplified,
                ""
            );
            // The order of divs inside the bloomDataDiv is neither important nor deterministic, so we sort it.
            simplified = SortDataDivElements(simplified);
            // Page IDs (actually any element ids) are ignored
            // (the bit before the 'id' matches an opening wedge followed by anything but a closing one,
            // and is transferred to the output by $1. Then we look for an id='whatever', with optional
            // whitespace, where (['\"]) matches either kind of opening quote while \2 matches the same one at the end.
            // The question mark makes sure we end with the first possible closing quote.
            // Then we grab everything up to the closing wedge and transfer that to the output as $3.)
            simplified = new Regex("(<[^>]*)\\s*id\\s*=\\s*(['\"]).*?\\2\\s*([^>]*>)").Replace(
                simplified,
                "$1$3"
            );
            var bytes = Encoding.UTF8.GetBytes(simplified);
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                if (filePath != null)
                {
                    //debugBldr.AppendLineFormat("Hashing {0}", filePath);
                    //using (var sha2 = SHA256.Create())
                    //{
                    //	sha2.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                    //	sha2.TransformFinalBlock(new byte[0], 0, 0);
                    //	debugBldr.AppendLineFormat("hashing simplified HTML [{0} bytes] => {1}", bytes.Length, Convert.ToBase64String(sha2.Hash));
                    //}
                    var folder = Path.GetDirectoryName(filePath);
                    var filter = new BookFileFilter(folder)
                    {
                        IncludeFilesForContinuedEditing = true,
                        NarrationLanguages = null, // include every narration language
                        WantVideo = true,
                        WantMusic = true
                    };
                    // Ignore 'meta.json' since this stores currentTool and toolboxIsOpen, which are irrelevant to whether
                    // the book has changed. OTOH, we need to do something about Draft status and Features which are not irrelevant.
                    filter.AlwaysReject("meta.json");
                    // Ignore video placeholder since this file is provided as needed.
                    filter.AlwaysReject("video-placeholder.svg");
                    // Ignore 'thumbnail.png' since it seems to vary gratuitously (still check other thumbnail images).
                    filter.AlwaysReject("thumbnail.png");
                    // Order must be predictable but does not otherwise matter.
                    foreach (
                        var path in Directory
                            .GetFiles(folder, "*", SearchOption.AllDirectories)
                            .OrderBy(x => x)
                    )
                    {
                        if (!filter.Filter(path))
                            continue;
                        if (path == filePath)
                            continue; // we already included a simplified version of the main HTML file
                        //AppendDebugInfo(debugBldr, path);
                        using (
                            var input = RobustIO.GetFileStream(path, FileMode.Open, FileAccess.Read)
                        )
                        {
                            byte[] buffer = new byte[4096];
                            int count;
                            while ((count = input.Read(buffer, 0, 4096)) > 0)
                            {
                                sha.TransformBlock(buffer, 0, count, buffer, 0);
                            }
                        }
                    }
                    foreach (
                        var path in Directory
                            .GetFiles(
                                Path.GetDirectoryName(folder),
                                "*.*",
                                SearchOption.TopDirectoryOnly
                            )
                            .OrderBy(x => x)
                    )
                    {
                        var name = Path.GetFileName(path);
                        if (
                            name == "customCollectionStyles.css"
                            || name.EndsWith(".bloomCollection", StringComparison.Ordinal)
                        )
                        {
                            //AppendDebugInfo(debugBldr, path);
                            byte[] buffer = RobustFile.ReadAllBytes(path);
                            sha.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
                        }
                    }
                    //var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    //debugPath = Path.Combine(folder, "DebugHashing-" + timestamp + ".bak");	// .bak gets ignored
                    //RobustFile.WriteAllText(Path.Combine(folder, "SimplifiedHtml-" + timestamp + ".bak"), simplified);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                //if (debugPath != null)
                //{
                //	debugBldr.AppendLineFormat("final hash = {0}", Convert.ToBase64String(sha.Hash));
                //	RobustFile.WriteAllText(debugPath, debugBldr.ToString());
                //}
                return Convert.ToBase64String(sha.Hash);
            }
        }

        //private static void AppendDebugInfo(StringBuilder debugBldr, string path)
        //{
        //	using (var sha2 = SHA256.Create())
        //	{
        //		byte[] buffer = RobustFile.ReadAllBytes(path);
        //		sha2.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
        //		sha2.TransformFinalBlock(new byte[0], 0, 0);
        //		debugBldr.AppendLineFormat("hashing {0} [{1} bytes] => {2}", Path.GetFileName(path), buffer.Length, Convert.ToBase64String(sha2.Hash));
        //	}
        //}

        private static string SortDataDivElements(string htmlText)
        {
            // Extract the text block that contains the outer #bloomDataDiv
            var begin = htmlText.IndexOf("<div id=\"bloomDataDiv\">", StringComparison.Ordinal);
            var end = htmlText.IndexOf("<div class=\"bloom-page", StringComparison.Ordinal);
            if (begin < 0 || end <= begin)
                return htmlText;
            var dataDivBlock = htmlText.Substring(begin, end - begin);
            // Extract the text block that contains the inner #bloomDataDiv, split it into lines, and sort the lines.
            var beginDivs = dataDivBlock.IndexOf("<div data-book=\"", StringComparison.Ordinal);
            var endDivs = dataDivBlock.LastIndexOf("</div>", StringComparison.Ordinal);
            if (beginDivs < 0 || endDivs <= beginDivs)
                return htmlText;
            var innerDataDiv = dataDivBlock.Substring(beginDivs, endDivs - beginDivs);
            innerDataDiv = innerDataDiv.Replace("</div><div data-book=", "</div>\n<div data-book=");
            var dataDivs = innerDataDiv.Split(new[] { '\n' });
            dataDivs.Sort((x, y) => string.Compare(x, y, StringComparison.Ordinal));
            // Replace the original outer #bloomDataDiv text with one containing the sorted inner #bloomDataDiv.
            var sortedInnerDataDiv = string.Join("", dataDivs);
            var newHtml = htmlText.Replace(
                dataDivBlock,
                "<div id=\"bloomDataDiv\">" + sortedInnerDataDiv + "</div>"
            );
            return newHtml;
        }

        /// <summary>
        /// Gets the complete path to the book's cover image, or null if there isn't one.
        /// </summary>
        /// <returns></returns>
        public string GetCoverImagePath()
        {
            if (Storage == null)
                return null; // can happen in tests
            // This first branch covers the currently obsolete approach to images using background-image.
            // In that approach the data-book attribute is on the imageContainer.
            // Note that we want the coverImage from a page, instead of the dataDiv because the former
            // "doesn't have the data in the form that GetImageElementUrl can handle."
            var coverImgElt = Storage.Dom
                .SafeSelectNodes("//div[not(@id='bloomDataDiv')]/div[@data-book='coverImage']")
                .Cast<XmlElement>()
                .FirstOrDefault();
            // If that fails, we look for an img with the relevant attribute. Happily this doesn't conflict with the data-div.
            if (coverImgElt == null)
            {
                coverImgElt = Storage.Dom
                    .SafeSelectNodes("//img[@data-book='coverImage']")
                    .Cast<XmlElement>()
                    .FirstOrDefault();
            }
            if (coverImgElt == null)
                return null;
            var coverImageUrl = HtmlDom.GetImageElementUrl(coverImgElt);
            var coverImageFileName = coverImageUrl.PathOnly.NotEncoded;
            if (string.IsNullOrEmpty(coverImageFileName))
                return null;
            // The fileName might be URL encoded.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3901.
            var coverImagePath = UrlPathString.GetFullyDecodedPath(
                StoragePageFolder,
                ref coverImageFileName
            );
            if (!RobustFile.Exists(coverImagePath))
            {
                // And the filename might be multiply-HTML encoded.
                while (coverImagePath.Contains("&amp;"))
                {
                    coverImagePath = HttpUtility.HtmlDecode(coverImagePath);
                    if (RobustFile.Exists(coverImagePath))
                        return coverImagePath;
                }
                return null;
            }
            return coverImagePath;
        }

        /// <summary>
        /// Check whether the given image file is for the book's cover.  If so, we may want to make
        /// it transparent in further processing.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-4816 for why we want to limit
        /// which image files are given a transparent background.
        /// </remarks>
        public bool ImageFileIsForBookCover(string imagePath)
        {
            // At the moment, only the cover image needs a transparent background, and then only if it's
            // a black and white drawing.
            // Note that if an image file is used more than once in a book, it gets a different
            // name each time.
            // For publishing, the imagePath will be in a temporary folder location instead of
            // the Bloom collection folder, so comparing the full paths does not work.  In publishing,
            // we do get a few images requested from the PDF framework whose names may inadvertently
            // match files we use, so we double-check by also comparing file sizes.
            var coverImagePath = GetCoverImagePath();
            if (Path.GetFileName(imagePath) == Path.GetFileName(coverImagePath))
                return (new FileInfo(imagePath).Length == new FileInfo(coverImagePath).Length);
            return false;
            ;
        }

        /// <summary>
        /// The primary focus of this method is removing pages we don't want in bloompub files,
        /// particularly xmatter pages that often don't have content but just might.
        /// It will detect pages with img elements or bloom-imageContainer elements with
        /// background images, and as long as the image isn't our placeholder, such pages
        /// are non-blank. For text, it is looking for divs that have the bloom-visibility-code-on
        /// class (and some non-white content). This means that it's looking for content that is
        /// visible given the current collection settings. Blank pages might have content in other
        /// languages. It's even possible (see comments on the code that inserts the
        /// bloom-visibility-code-on class) that the user might override it somehow.
        /// It's conceivable that pages contain text that's not in our editable divs.
        /// Thus, this mechanism is not as reliable as the process used in epub publishing to delete
        /// invisible text, which involves actually building a display of the page in the browser,
        /// but it is much faster and simpler and seems adequate to the current purpose.
        /// Also, (BL-7586) we don't want to delete activity pages, that may not have text, but still
        /// could be fully functioning activities.
        /// Currently the intention is to apply this to a copy of the book, not the original.
        /// If the optional argument is provided, having 'visible' text is defined as text
        /// in one of the specified languages.
        /// </summary>
        public void RemoveBlankPages(HashSet<string> languagesToInclude = null)
        {
            foreach (
                var page in RawDom
                    .SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
                    .Cast<XmlElement>()
                    .ToArray()
            )
            {
                if (HtmlDom.IsActivityPage(page))
                    continue;
                if (PageHasImages(page))
                    continue;
                if (languagesToInclude == null && PageHasVisibleText(page))
                    continue;
                if (languagesToInclude != null && PageHasTextInLanguage(page, languagesToInclude))
                    continue;
                if (PageHasVideo(page))
                    continue;
                if (IsPageProtectedFromRemoval(page))
                    continue;
                page.ParentNode.RemoveChild(page);
            }
            OrderOrNumberOfPagesChanged();
        }

        public void UpdateSupportFiles()
        {
            Storage.UpdateSupportFiles();
        }

        private bool IsPageProtectedFromRemoval(XmlElement pageElement)
        {
            // One final check to see if we have explicitly disallowed this page (or one of its children) from being removed.
            // As of May 2020, this is used to protect the Afghan xmatters from having these pages removed:
            // 1) The national anthem page
            //      The only thing on this page is an image which is added by css, so PageHasImages() doesn't see it.
            // 2) The message page
            //      The only thing on this page is language-neutral text.
            //      It has to be language-neutral to make sure we don't strip it out if the language isn't part of the book.
            //      However, then PageHasTextInLanguage() won't return true.
            // It is also used on divs that have data-book="outside-back-cover-branding-bottom-html"
            // since if the current Branding is incomplete, the message we show is added by css (like #1 above)
            return HtmlDom.HasClass(pageElement, "bloom-force-publish")
                || pageElement
                    .SafeSelectNodes(".//div[contains(@class, 'bloom-force-publish')]")
                    .Count > 0;
        }

        private bool PageHasVisibleText(XmlElement page)
        {
            foreach (
                XmlElement div in page.SafeSelectNodes(
                    ".//div[contains(@class, 'bloom-visibility-code-on')]"
                )
            )
            {
                if (!string.IsNullOrWhiteSpace(div.InnerText))
                    return true;
            }
            return false;
        }

        // Return true if the element contains text in (a div) whose lang is one of the specified set.
        // Note that we're NOT checking visibility here...this is used in publishing modes where we may
        // want to publish a page if it has content in languages the user has said to publish, even if it has
        // none in the visible languages of this collection.
        private static bool PageHasTextInLanguage(
            XmlElement page,
            HashSet<string> languagesToLookFor
        )
        {
            foreach (XmlElement div in page.SafeSelectNodes(".//div[@lang]"))
            {
                if (
                    languagesToLookFor.Contains(div.GetStringAttribute("lang"))
                    && !string.IsNullOrWhiteSpace(div.InnerText)
                    // page labels are deleted in most scenarios; even when kept, they are not a reason
                    // to keep otherwise blank pages.
                    && div.Attributes["class"]?.Value != "pageLabel"
                )
                    return true;
            }
            return false;
        }

        private bool PageHasImages(XmlElement page)
        {
            foreach (XmlElement img in page.SafeSelectNodes(".//img"))
            {
                if (img.Attributes["src"]?.Value != "placeHolder.png")
                    return true;
            }
            foreach (
                XmlElement div in page.SafeSelectNodes(
                    ".//div[contains(@class, 'bloom-imageContainer')]"
                )
            )
            {
                var imgUrl = HtmlDom.GetImageElementUrl(div).PathOnly.NotEncoded;
                // Actually getting a background img url is a good indication that it's one we want.
                if (!string.IsNullOrEmpty(imgUrl) && imgUrl != "placeHolder.png")
                    return true;
            }
            return false;
        }

        private bool PageHasVideo(XmlElement page)
        {
            foreach (XmlElement videoSource in page.SafeSelectNodes(".//video/source"))
            {
                var src = videoSource.GetAttribute("src");
                if (!string.IsNullOrEmpty(src))
                    return true;
            }
            return false;
        }

        public void SetAnimationDurationsFromAudioDurations()
        {
            foreach (
                XmlElement page in RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
            )
            {
                // For now we only apply this to the first image container.
                var imgContainer =
                    page.SelectSingleNode(
                        ".//div[contains(@class, 'bloom-imageContainer') and @data-initialrect]"
                    ) as XmlElement;
                if (imgContainer == null)
                    continue;
                double duration = 0.0;
                foreach (
                    XmlElement editable in page.SafeSelectNodes(
                        ".//div[contains(@class,'bloom-editable') and contains(@class, 'bloom-content1')]"
                    )
                )
                {
                    foreach (
                        XmlElement span in HtmlDom.SelectAudioSentenceElementsWithDataDuration(
                            editable
                        )
                    )
                    {
                        double.TryParse(
                            span.Attributes["data-duration"].Value,
                            NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture,
                            out var time
                        );
                        duration += time;
                    }
                }
                if (duration == 0.0)
                    duration = 4.0; // per BL-5393, if we don't have voice durations use 4 seconds.
                imgContainer.SetAttribute(
                    "data-duration",
                    duration.ToString(CultureInfo.InvariantCulture)
                );
            }
        }

        public bool HasMotionPages => OurHtmlDom.HasMotionPages();

        public bool HasQuizPages => OurHtmlDom.HasQuizPages();
        public bool HasActivities => OurHtmlDom.HasActivityPages();

        public bool HasOverlayPages => OurHtmlDom.HasOverlayPages();

        public bool HasOnlyPictureOnlyPages()
        {
            foreach (var page in GetPages())
            {
                if (page.IsXMatter)
                    continue;
                var pageDiv = page.GetDivNodeForThisPage();
                foreach (
                    var groupDiv in pageDiv
                        .SafeSelectNodes(".//div[contains(@class, 'bloom-translationGroup')]")
                        .Cast<XmlElement>()
                )
                {
                    var classes = groupDiv.GetAttribute("class");
                    if (!classes.Contains("bloom-imageDescription"))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Re-compute and update all of the metadata features for the book
        /// </summary>
        /// <param name="allowedLanguages">If non-null, limits the calculation to only considering the languages specified. Applies only to language-specific features
        /// (e.g. talkingBook. Does not apply to Sign Language or language-independent features</param>
        internal void UpdateMetadataFeatures(
            bool isTalkingBookEnabled,
            bool isSignLanguageEnabled,
            IEnumerable<string> allowedLanguages
        )
        {
            // Language-specific features
            UpdateTalkingBookFeature(isTalkingBookEnabled, allowedLanguages);

            // Sign Language is a special case - the SL videos are not marked up with lang attributes
            UpdateSignLanguageFeature(isSignLanguageEnabled);

            UpdateVideoFeature();

            // Language-independent features
            UpdateQuizFeature();
            UpdateSimpleDomChoiceFeature();
            UpdateMotionFeature();
            UpdateComicFeature();
            UpdateWidgetFeature();

            BookInfo.Save();
        }

        /// <summary>
        /// Updates the feature in bookInfo.metadata to indicate whether the book is accessible to the blind/visually impaired
        /// </summary>
        /// <param name="isEnabled">True to indicate the feature is enabled, or false for disabled (will clear the feature in the metadata)</param>
        public void UpdateBlindFeature(bool isEnabled)
        {
            if (!isEnabled)
            {
                // Only clear the current L1. If we've earlier somehow claimed it for some other
                // language, presumably it's still true.
                BookInfo.MetaData.Feature_Blind_LangCodes =
                    BookInfo.MetaData.Feature_Blind_LangCodes.Except(new[] { Language1Tag });
                return;
            }

            // Set the metadata feature that indicates a book is accessible to the blind. Our current default
            // definition of this is the presence of image descriptions in the non-xmatter part of the book.
            // This is very imperfect. Minimally, to be accessible to the blind, it should also be a talking book
            // and everything, including image descriptions, should have audio; but talkingBook is a separate feature.
            // Also we aren't checking that EVERY image has a description. What we have is therefore too weak,
            // but EVERY image might be too strong...some may just be decorative. Then there are considerations
            // like contrast and no essential information conveyed by color and other stuff that the DAISY code
            // checks. Thus, we only actually claim this if the author has checked the "accessible to the blind"
            // feature. Currently this can only be set for L1; the user could fudge by changing L1.
            BookInfo.MetaData.Feature_Blind_LangCodes =
                BookInfo.MetaData.Feature_Blind_LangCodes.Union(new[] { Language1Tag });
        }

        /// <summary>
        /// Updates the feature in bookInfo.metadata to indicate whether the book contains meaningful narration audio
        /// Narration audio in XMatter DOES count (for now?)
        /// </summary>
        /// <param name="isEnabled">True to indicate the feature is enabled, or false for disabled (will clear the feature in the metadata)</param>
        /// <param name="allowedLanguages">If non-null, limits the calculation to only considering the languages specified</param>
        private void UpdateTalkingBookFeature(bool isEnabled, IEnumerable<string> allowedLanguages)
        {
            if (!isEnabled)
            {
                BookInfo.MetaData.Feature_TalkingBook_LangCodes =
                    System.Linq.Enumerable.Empty<string>();
                return;
            }

            var langCodes = GetRecordedAudioSentences()
                .Select(HtmlDom.GetClosestLangCode)
                .Where(HtmlDom.IsLanguageValid)
                .Distinct();

            if (allowedLanguages != null)
                langCodes = langCodes.Intersect(allowedLanguages);

            BookInfo.MetaData.Feature_TalkingBook_LangCodes = langCodes.ToList();
        }

        /// <summary>
        /// Updates the feature in bookInfo.metadata to indicate whether the book contains sign language video
        /// </summary>
        /// <param name="isEnabled">True to indicate the feature is enabled, or false for disabled (will clear the feature in the metadata)</param>
        private void UpdateSignLanguageFeature(bool isEnabled)
        {
            if (isEnabled && HasSignLanguageVideos())
                // FYI: this might be "", but that's OK. Pass it through anyway
                BookInfo.MetaData.Feature_SignLanguage_LangCodes = new string[]
                {
                    this.CollectionSettings.SignLanguageTag
                };
            else
                BookInfo.MetaData.Feature_SignLanguage_LangCodes =
                    System.Linq.Enumerable.Empty<string>();
        }

        private void UpdateVideoFeature()
        {
            BookInfo.MetaData.Feature_Video = HasVideos();
        }

        /// <summary>
        /// Updates the feature in bookInfo.metadata to indicate whether the book contains quizzes
        /// </summary>
        private void UpdateQuizFeature()
        {
            // For now, our model is that quizzes are a language-independent feature.
            // If the book has a quiz and the user uploads it with an incomplete translation (quizzes not translated), so be it.
            // If we wanted to, it is also possible to compute it as a language-specific feature.
            // (That is, check if the languages in the book have non-empty text for part of the quiz section)
            BookInfo.MetaData.Feature_Quiz =
                CollectionSettings.HaveEnterpriseFeatures && HasQuizPages;
        }

        private void UpdateSimpleDomChoiceFeature()
        {
            BookInfo.MetaData.Feature_SimpleDomChoice =
                CollectionSettings.HaveEnterpriseFeatures && OurHtmlDom.HasSimpleDomChoicePages();
        }

        /// <summary>
        /// Updates the feature in bookInfo.metadata to indicate whether the book contains widget activities
        /// </summary>
        private void UpdateWidgetFeature()
        {
            BookInfo.MetaData.Feature_Widget = Storage
                .GetActivityFolderNamesReferencedInBook()
                .Any();
        }

        /// <summary>
        /// Updates the feature in bookInfo.metadata to indicate whether the book contains comic pages.
        /// These are now created with the Overlay Tool, but the feature retains the old name.
        /// (But, we will only report it as a Comic book if the user didn't turn it off in the publish settings.)
        /// </summary>
        private void UpdateComicFeature()
        {
            BookInfo.MetaData.Feature_Comic =
                HasOverlayPages && BookInfo.PublishSettings.BloomLibrary.Comic;
        }

        /// <summary>
        /// Updates the feature in bookInfo.metadata to indicate whether the book is a motion book
        /// (The publish setting is true by default, but only relevant...and the feature will only
        /// be true...if the book actually has some motion settings.)
        /// </summary>
        private void UpdateMotionFeature()
        {
            BookInfo.MetaData.Feature_Motion =
                HasMotionPages && BookInfo.PublishSettings.BloomPub.PublishAsMotionBookIfApplicable;
        }

        /// <summary>
        /// BloomServer needs a static version of this method to ensure that the collection tab preview pane
        /// has the necessary placeholder file.
        /// </summary>
        public static void EnsureVideoPlaceholderFile(Book book)
        {
            book.Storage.Update("video-placeholder.svg");
        }

        public static void EnsureWidgetPlaceholderFile(Book book)
        {
            book.Storage.Update("widget-placeholder.svg");
        }

        /// <summary>
        /// Motion mode is currently implemented in the player in response to a set of six features
        /// which we place on the body. The original idea was that we might want to control these
        /// behaviors independently, and even possibly that any of them might depend on whether the
        /// device is in landscape mode and what media is being used; we don't currently use any
        /// of those capabilities.
        /// </summary>
        /// <param name="motion"></param>
        public void SetMotionAttributesOnBody(bool motion)
        {
            Action<string, string, string> addOrRemove;
            if (motion)
                addOrRemove = (
                    string featureName,
                    string orientationConstraint,
                    string mediaConstraint
                ) => OurHtmlDom.SetBookFeature(featureName, orientationConstraint, mediaConstraint);
            else
                addOrRemove = (
                    string featureName,
                    string orientationConstraint,
                    string mediaConstraint
                ) =>
                    OurHtmlDom.ClearBookFeature(
                        featureName,
                        orientationConstraint,
                        mediaConstraint
                    );
            // Enhance: we can probably put all this in HtmlDom and have it not know about the particular features, just copy them
            // from the datadiv. That means it will need to be possible to identify them by some attribute, e.g. data-isBookFeature="true"
            // these are read by Bloom Reader (and eventually Reading App Builder?)
            addOrRemove("autoadvance", "landscape", "bloomReader");
            addOrRemove("canrotate", "allOrientations", "bloomReader");
            addOrRemove("playanimations", "landscape", "bloomReader"); // could be ignoreAnimations
            addOrRemove("playmusic", "landscape", "bloomReader");
            addOrRemove("playnarration", "landscape", "bloomReader");

            // these are read by css
            //modifiedBook.OurHtmlDom.SetBookFeature("hideMargin", "landscape", "bloomReader");
            //modifiedBook.OurHtmlDom.SetBookFeature("hidePageNumbers", "landscape", "bloomReader");
            addOrRemove("fullscreenpicture", "landscape", "bloomReader");

            // Make sure we publish this feature consistent with the publication setting.
            BookInfo.MetaData.Feature_Motion = motion;

            Save();
        }

        /// <summary>
        /// BL-5886 Translation Instructions page should not end up in BR (or Epub or Pdf, but other classes ensure that).
        /// N.B. This is only intended for use on temporary files.
        /// </summary>
        public void RemoveNonPublishablePages(Dictionary<string, int> removedLabels = null)
        {
            const string xpath = "//div[contains(@class,'bloom-noreader')]";

            var dom = OurHtmlDom.RawDom;
            var nonpublishablePages = dom.SafeSelectNodes(xpath);
            foreach (XmlNode doomedPage in nonpublishablePages)
            {
                PublishHelper.CollectPageLabel((XmlElement)doomedPage, removedLabels);
                doomedPage.ParentNode.RemoveChild(doomedPage);
            }
        }

        public static bool IsPageBloomEnterpriseOnly(XmlElement page)
        {
            var classAttrib = page.GetAttribute("class");
            return classAttrib.Contains("enterprise-only")
                ||
                // legacy quiz pages don't have 'enterprise-only'
                classAttrib.Contains("questions")
                || page.SafeSelectNodes(".//div[contains(@class,'bloom-widgetContainer')]").Count
                    > 0;
        }

        /// <summary>
        /// Used by the publish tab to tell the user they can't publish a book with Overlay elements w/o Enterprise.
        /// </summary>
        /// <returns></returns>
        public string GetNumberOfFirstPageWithOverlay()
        {
            var pageNodes = RawDom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]");
            if (pageNodes.Count == 0) // Unexpected!
                return "";
            foreach (XmlNode pageNode in pageNodes)
            {
                var resultNode = pageNode.SelectSingleNode(
                    ".//div[contains(@class,'bloom-textOverPicture')]"
                );
                if (resultNode == null)
                    continue;
                var pageNumberAttribute = pageNode.Attributes?["data-page-number"];
                if (pageNumberAttribute != null)
                {
                    return pageNumberAttribute.Value;
                }
                // If at some point we allow overlay elements on xmatter,
                // we will need to find and return the 'data-xmatter-page' attribute.
            }
            return ""; // Also unexpected!
        }

        /// <summary>
        /// Given a choice, what language should we use to display text on the page (not in the UI, which is controlled by the UI Language)
        /// </summary>
        /// <returns>A prioritized enumerable of language codes</returns>
        public IEnumerable<string> GetLanguagePrioritiesForLocalizedTextOnPage(
            bool includeLang1 = true
        )
        {
            return _bookData.GetLanguagePrioritiesForLocalizedTextOnPage(includeLang1);
        }

        /// <summary>
        /// Get a name for Language1 that is safe for using as part of a file name.
        /// (Currently used for suggesting a pdf filename when publishing.)
        /// </summary>
        /// <param name="inLanguage"></param>
        /// <returns></returns>
        public string GetFilesafeLanguage1Name(string inLanguage)
        {
            var languageName = _bookData.CollectionSettings.GetLanguageName(
                _bookData.Language1.Tag,
                inLanguage
            );
            return Path.GetInvalidFileNameChars()
                .Aggregate(languageName, (current, character) => current.Replace(character, ' '));
        }

        /// <summary>
        /// Returns which languages in the book have at least one recorded audio
        /// </summary>
        public HashSet<string> GetLanguagesWithAudio()
        {
            var languagesWithAudio = new HashSet<string>();
            var narrationNodeList = HtmlDom.SelectChildNarrationAudioElements(
                OurHtmlDom.Body,
                true
            );

            for (int i = 0; i < narrationNodeList.Count; ++i)
            {
                var node = narrationNodeList[i];

                var id = node.GetOptionalStringAttribute("id", null);
                if (String.IsNullOrEmpty(id))
                    continue;

                var fileNames = BookStorage.GetNarrationAudioFileNames(id, true);

                bool doesAnyAudioFileExist = false;
                foreach (var audioFileName in fileNames)
                {
                    var fullPath = Path.Combine(FolderPath, "audio", audioFileName);
                    if (RobustFile.Exists(fullPath))
                    {
                        doesAnyAudioFileExist = true;
                        break;
                    }
                }

                if (!doesAnyAudioFileExist)
                    continue;

                // At this point, we know that node contains an audio file associated with it.
                var nodeWithLangAttr = HtmlDom.FindSelfOrAncestorMatchingCondition(
                    node,
                    n =>
                    {
                        var tempLang = n.GetOptionalStringAttribute("lang", "");
                        return !String.IsNullOrEmpty(tempLang);
                    }
                );

                var lang = nodeWithLangAttr.GetOptionalStringAttribute("lang", "");
                languagesWithAudio.Add(lang);
            }

            return languagesWithAudio;
        }

        /// <summary>
        /// The book has been given the indicated name by the user, or some other process
        /// like making a backup that means we permanently want a name not matching the title.
        /// This is distinct from giving it an automatic name based on editing the Title.
        /// Typically, the user has explicitly used the Rename command to specify that this
        /// should be the name. If it is empty, we will go back to using an automatic
        /// name. Otherwise, this will be its name, and it will not be subject to rename
        /// by title editing.
        /// </summary>
        /// <param name="newName"></param>
        public void SetAndLockBookName(string newName)
        {
            if (!string.IsNullOrWhiteSpace(newName))
            {
                BookInfo.FileNameLocked = true;
                Storage.SetBookName(newName);
                BookInfo.Save();
            }
            else
            {
                // Back to automatic name
                Storage.UpdateBookFileAndFolderName(CollectionSettings);
                BookInfo.FileNameLocked = false;
                BookInfo.Save();
            }
        }

        // see BL-11510
        public void ReportSimplisticFontAnalytics(
            FontAnalytics.FontEventType fontEventType,
            string eventDetails = null
        )
        {
            var testOnly = BookUpload.UseSandboxByDefault;
            FontAnalytics.Report(
                this.ID,
                fontEventType,
                this.CollectionSettings.Language1.Tag,
                testOnly,
                this.CollectionSettings.Language1.FontName,
                eventDetails
            );
        }

        public void AddHistoryRecordForLibraryUpload(string url)
        {
            BookHistory.AddEvent(
                this,
                BookHistoryEventType.Uploaded,
                "Book uploaded to Bloom Library" + (string.IsNullOrEmpty(url) ? "" : $" ({url})")
            );
        }

        public bool IsRequiredLanguage(string langCode)
        {
            // Languages which have been selected for display in this book need to be selected
            return GetRequiredLanguages().Contains(langCode);
        }

        public bool IsCollectionLanguage(string langCode)
        {
            return CollectionSettings.GetAllLanguageTags().Contains(langCode);
        }

        private IEnumerable<string> GetRequiredLanguages()
        {
            return new[] { BookData.Language1.Tag, Language2Tag, Language3Tag }.Where(
                l => !string.IsNullOrEmpty(l)
            );
        }

        /// <summary>
        /// Given a superset of languages, return the subset that we should advertise on BloomLibrary.org.
        /// Specifically, return the languages in the superset which also have text in the content pages of the book.
        /// </summary>
        public IEnumerable<string> GetTextLanguagesToAdvertiseOnBloomLibrary(
            IEnumerable<string> languagesSuperset
        )
        {
            return languagesSuperset.Intersect(
                AllPublishableLanguages(includeLangsOccurringOnlyInXmatter: false).Keys
            );
        }

        public void SetIsDecodable(bool isDecodable)
        {
            OurHtmlDom.SetClassOnBody(isDecodable, "decodable-reader");
            OurHtmlDom.SetClassOnBody(!isDecodable, "decodable-reader-off");
        }

        public void SetIsLeveled(bool isLeveled)
        {
            OurHtmlDom.SetClassOnBody(isLeveled, "leveled-reader");
            OurHtmlDom.SetClassOnBody(!isLeveled, "leveled-reader-off");
        }

        public bool HasL1Title()
        {
            var titleDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                BookInfo.AllTitles ?? "{}"
            );
            if (!titleDict.TryGetValue(Language1Tag, out string l1Title))
                return false;
            return !string.IsNullOrEmpty(l1Title);
        }

        internal void SettingsUpdated()
        {
            // Something has changed in relation to settings. For example, we may have changed theme,
            // or deleted customBookStyles.css. We need to update things to reflect the new state of things.
            var cssFiles = this.Storage.GetCssFilesToCheckForAppearanceCompatibility();
            // This might produce different results if customBookStyles.css has been deleted.
            BookInfo.AppearanceSettings.CheckCssFilesForCompatibility(
                cssFiles,
                Storage.LegacyThemeCanBeUsed
            );
            // At one point the line commented out here was all this function did.
            // It needs to be done at some point at least if the theme has changed, to generate the updated
            // Appearance.css. But usually the caller does a full Save() after calling this, so
            // doing it here is redundant. In the case of deleting customBookStyles.css,
            // Appearance.css should not be affected, so I don't think it would matter not to do it.
            // I'm leaving it commented out for now in case something in our Appearance/Theme
            // testing indicates it is needed, since it may help whoever is looking for any problem
            // that removing it causes. If you find a reason we need it, please document thoroughly.
            //BookInfo.Save();
            // Should not be needed when deleting customBookStyles.css, but definitely when we change theme.
            Storage.UpdateSupportFiles();
            // temporary while we're in transition between storing cover color in the HTML and in the bookInfo
            //SetCoverColor(BookInfo.AppearanceSettings.CoverColor);
            _pageListChangedEvent.Raise(true);
        }
    }
}
