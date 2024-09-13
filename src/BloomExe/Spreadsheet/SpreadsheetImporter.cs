using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Forms;
using System.Xml;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Xml;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.web;
using Bloom.Collection;
using Bloom.History;
using Bloom.ImageProcessing;
using Bloom.web.controllers;
using Bloom.Utils;
using Bloom.SafeXml;
using Bloom.TeamCollection;

namespace Bloom.Spreadsheet
{
    /// <summary>
    /// Imports data from an internal spreadsheet into a bloom book.
    /// </summary>
    public class SpreadsheetImporter
    {
        private HtmlDom _destinationDom;
        private readonly Book.Book _book;
        private InternalSpreadsheet _sheet;
        private int _currentRowIndex;
        private int _currentPageIndex;
        private SafeXmlElement _currentPage;
        private List<SafeXmlElement> _pages;

        // text, image, video, widget
        const int blockTypeCount = 4;

        // lists of translation groups (other than image descriptions),
        // image containers, video containers, and widget containers.
        List<SafeXmlElement>[] _blocksOnPage = new List<SafeXmlElement>[blockTypeCount];
        public const int translationGroupIndex = 0;
        public const int imageContainerIndex = 1;
        public const int videoContainerIndex = 2;
        public const int widgetContainerIndex = 3;

        // for each kind of block, this gives the index in the corresponding list in blocksOnPage
        // of the next block we should import into. May be -1 or too large if there are no more
        // blocks of that type we can import into on this page.
        int[] _blockOnPageIndexes = new int[blockTypeCount];
        private List<string> _warnings;
        private List<ContentRow> _inputRows;
        private readonly SafeXmlElement _dataDivElement;
        private readonly string _pathToSpreadsheetFolder;
        private readonly string _pathToBookFolder;
        private readonly IBloomWebSocketServer _webSocketServer;
        private IWebSocketProgress _progress;
        private int _frontMatterPagesSeen;
        private bool _bookIsLandscape;
        private Layout _destLayout;
        private readonly CollectionSettings _collectionSettings;
        private readonly TeamCollectionManager _tcManager;

        public delegate SpreadsheetImporter Factory();

        /// <summary>
        /// Create an instance. The webSocketServer may be null unless using ImportWithProgressAsync.
        /// </summary>
        /// <remarks>The web socket server is a constructor argument as a step in the direction
        /// of allowing this class to be instantiated and supplied with the socket server by
        /// AutoFac. However, for that to work, we'd need to move the other constructor arguments,
        /// which AutoFac can't know, to the ImportAsync method. And for now, all callers which need
        /// to pass a socket server already have one.</remarks>
        public SpreadsheetImporter(
            IBloomWebSocketServer webSocketServer,
            HtmlDom destinationDom,
            string pathToSpreadsheetFolder = null,
            string pathToBookFolder = null,
            CollectionSettings collectionSettings = null
        )
        {
            _destinationDom = destinationDom;
            _dataDivElement = _destinationDom
                .SafeSelectNodes("//div[@id='bloomDataDiv']")
                .Cast<SafeXmlElement>()
                .First();
            _pathToBookFolder = pathToBookFolder;
            // Tests and CLI may not set one or more of these
            _pathToSpreadsheetFolder = pathToSpreadsheetFolder;
            _webSocketServer = webSocketServer;
            _collectionSettings = collectionSettings;
        }

        /// <summary>
        /// Used by the main SpreadsheetApi call. Tests and CLI (which don't usually have access to the book)
        /// use the other ctor.
        /// </summary>
        public SpreadsheetImporter(
            IBloomWebSocketServer webSocketServer,
            Book.Book book,
            string pathToSpreadsheetFolder,
            TeamCollectionManager teamCollectionManager = null
        )
            : this(
                webSocketServer,
                book.OurHtmlDom,
                pathToSpreadsheetFolder,
                book.FolderPath,
                book.CollectionSettings
            )
        {
            _book = book;
            _tcManager = teamCollectionManager;
        }

        // If the import is not done on the main UI thread, a control should be passed that can be used to
        // invoke things (currently browser operations) that must be done on that thread.
        public Control ControlForInvoke { get; set; }

        /// <summary>
        /// If true, bloom-editable elements in matched translation groups which do
        /// not have a corresponding column in the input will be deleted.
        /// </summary>
        public bool RemoveOtherLanguages => Params.RemoveOtherLanguages;

        public SpreadsheetImportParams Params = new SpreadsheetImportParams();

        public async Task ImportWithProgressAsync(string inputFilepath, Action doWhenProgressCloses)
        {
            Debug.Assert(
                _pathToBookFolder != null,
                "Somehow we made it into ImportWithProgressAsync() without a path to the book folder"
            );
            await BrowserProgressDialog.DoWorkWithProgressDialogAsync(
                _webSocketServer,
                async (progress, worker) =>
                {
                    var sheet = InternalSpreadsheet.ReadFromFile(inputFilepath, progress);
                    if (sheet == null)
                        return true;
                    if (!Validate(sheet, progress))
                        return true; // errors already reported to progress
                    progress.MessageWithoutLocalizing($"Making a backup of the original book...");
                    var backupPath = BookStorage.SaveCopyBeforeImportOverwrite(_pathToBookFolder);
                    progress.MessageWithoutLocalizing($"Backup completed (at {backupPath})");
                    var audioFolder = Path.Combine(_pathToBookFolder, "audio");
                    if (Directory.Exists(audioFolder))
                        SIL.IO.RobustIO.DeleteDirectoryAndContents(audioFolder);
                    await ImportAsync(sheet, progress);
                    BookHistory.AddEvent(
                        _book,
                        BookHistoryEventType.ImportSpreadsheet,
                        "Spreadsheet imported from " + inputFilepath
                    );

                    return true; // always leave the dialog up until the user chooses 'close'
                },
                "collectionTab",
                "Importing Spreadsheet",
                showCancelButton: false,
                doWhenDialogCloses: doWhenProgressCloses
            );
        }

        private Browser _browser;

        delegate Task<Browser> GetBrowserDelegate();

        /// <summary>
        /// Get a brower with certain functions that spreadsheet import needs made available
        /// </summary>
        /// <remarks>
        /// The browser is pre-loaded with the code imported by spreadsheetBundleRoot.ts,
        /// which can be accessed using code like RunJavascript("spreadsheetBundle.split('my sentence')")
        /// to call any function which that file exports. Add more to it as needed.
        /// It's potentially tricky to debug any problems on the JS side, since the browser isn't
        /// open on the screen anywhere to debug. However, it doesn't have any links that the
        /// browser complains about as being cross-domain, so I've chosen to implement it using file urls.
        /// This means that you can simply open the root file (output/browser/spreadsheet/spreadsheetFunctions.html)
        /// and try your RunJavascript inputs in the console.
        /// Remember to escape any single quotes in data strings passed to RunJavascript!
        /// </remarks>
        protected virtual async Task<Browser> GetBrowserAsync()
        {
            if (_browser == null)
            {
                if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
                {
                    return (Browser)
                        ControlForInvoke.Invoke(new GetBrowserDelegate(GetBrowserAsync));
                }
                // Todo Linux: I'm choosing not to do BrowserMaker.MakeBrowser here, because
                // the Gecko code to try to determine that the browser has fully loaded everything
                // is more complicated, and I'm not sure this functionality is needed on Linux,
                // and it seems there is little need to be testing this code in the browser we are
                // about to drop. So if we want this on Linux, we'll have to test carefully there,
                // and possibly make a new overload of NavigateAndWaitUntilDone if the wait code here
                // is not good enough.
#if __MonoCS__
                _browser = new GeckoFxBrowser();
#else
                _browser = new WebView2Browser();
#endif
                var signal = new SemaphoreSlim(0, 1);
                _browser.DocumentCompleted += (sender, args) =>
                {
                    signal.Release();
                };
                var rootPage = BloomFileLocator.GetBrowserFile(
                    false,
                    "spreadsheet",
                    "spreadsheetFunctions.html"
                );
                _browser.Navigate(rootPage, false);
                await signal.WaitAsync(); // Following code happens after browser has navigated
                // This extra check that spreadsheetBundle actually exists might not be necessary.
                while (await _browser.GetStringFromJavascriptAsync($"spreadsheetBundle") == null)
                    await Task.Delay(10);
            }
            return _browser;
        }

        public bool Validate(InternalSpreadsheet sheet, IWebSocketProgress progress)
        {
            // An export would have several others. But none of them is absolutely required except this one.
            // (We could do without it, too, by assuming the first column contains them. But it's helpful to be
            // able to recognize spreadsheets created without any knowledge at all of the expected content.)
            // Note: depending on row content, this problem may be detected earlier in SpreadsheetIO while
            // converting the file to an InternalSpreadsheet.
            var rowTypeColumn = sheet.GetColumnForTag(InternalSpreadsheet.RowTypeColumnLabel);
            if (rowTypeColumn < 0)
            {
                progress.MessageWithoutLocalizing(MissingHeaderMessage, ProgressKind.Error);
                return false;
            }
            var inputRows = sheet.ContentRows.ToList();
            if (!inputRows.Any(r => r.GetCell(rowTypeColumn).Content.StartsWith("[")))
            {
                progress.MessageWithoutLocalizing(
                    "This spreadsheet has no data that Bloom knows how to import. Did you follow the standard format for Bloom spreadsheets?",
                    ProgressKind.Warning
                );
                // Technically this isn't a fatal error. We could just let the main loop do nothing. But reporting it as one avoids creating a spurious backup.
                return false;
            }
            return true;
        }

        /// <summary>
        /// Import the spreadsheet into the dom
        /// </summary>
        /// <returns>a list of warnings</returns>
        public async Task<List<string>> ImportAsync(
            InternalSpreadsheet sheet,
            IWebSocketProgress progress = null
        )
        {
            _sheet = sheet;
            _progress = progress ?? new NullWebSocketProgress();
            Progress("Importing spreadsheet...");
            _warnings = new List<string>();
            _inputRows = _sheet.ContentRows.ToList();
            _pages = _destinationDom.GetPageElements().ToList();
            _bookIsLandscape =
                _pages.FirstOrDefault()?.GetAttribute("class")?.Contains("Landscape") ?? false;
            _currentRowIndex = 0;
            _currentPageIndex = -1;
            _blocksOnPage[translationGroupIndex] = new List<SafeXmlElement>();
            _blocksOnPage[imageContainerIndex] = new List<SafeXmlElement>();
            _destLayout = Layout.FromDom(_destinationDom, Layout.A5Portrait);
            var pageTypeIndex = sheet.GetColumnForTag(InternalSpreadsheet.PageTypeColumnLabel);
            while (_currentRowIndex < _inputRows.Count)
            {
                var currentRow = _inputRows[_currentRowIndex];
                string rowTypeLabel = currentRow.MetadataKey;
                bool extraRow = false;
                string pageType = null;
                if (pageTypeIndex >= 0)
                    pageType = currentRow.GetCell(pageTypeIndex).Content.Trim();

                if (rowTypeLabel == InternalSpreadsheet.PageContentRowLabel)
                {
                    bool rowHasImage = !string.IsNullOrWhiteSpace(
                        currentRow.GetCell(InternalSpreadsheet.ImageSourceColumnLabel).Text
                    );
                    bool rowHasText = RowHasText(currentRow);
                    bool rowHasVideo = !string.IsNullOrWhiteSpace(
                        currentRow.GetCell(InternalSpreadsheet.VideoSourceColumnLabel).Text
                    );
                    bool rowHasWidget = !string.IsNullOrWhiteSpace(
                        currentRow.GetCell(InternalSpreadsheet.WidgetSourceColumnLabel).Text
                    );
                    var typesInRow = MakeBlockTypes(
                        rowHasText,
                        rowHasImage,
                        rowHasVideo,
                        rowHasWidget
                    );
                    while (typesInRow != BlockTypes.None)
                    {
                        var typesToPut = AdvanceToNextSetOfBlocks(typesInRow, pageType);
                        // If we need another iteration because we could not find a page type that will hold
                        // all the data types we want on this row, we don't want to force the extra page to
                        // be the original type, which may well not have the slot we're missing (usually widget).
                        // Rather, we want a default page type that WILL hold the rest of the row data.
                        pageType = null;

                        if ((typesToPut & BlockTypes.Image) == BlockTypes.Image)
                        {
                            ContentRow descriptionRow = null;
                            if (_currentRowIndex < _inputRows.Count - 1)
                            {
                                var nextRow = _inputRows[_currentRowIndex + 1];
                                if (
                                    nextRow.MetadataKey
                                    == InternalSpreadsheet.ImageDescriptionRowLabel
                                )
                                {
                                    extraRow = true;
                                    descriptionRow = nextRow;
                                }
                            }

                            await PutRowInImageAsync(
                                currentRow,
                                descriptionRow,
                                _blocksOnPage[imageContainerIndex][
                                    _blockOnPageIndexes[imageContainerIndex]
                                ]
                            );
                        }

                        if ((typesToPut & BlockTypes.Text) == BlockTypes.Text)
                        {
                            await PutRowInGroupAsync(
                                currentRow,
                                _blocksOnPage[translationGroupIndex][
                                    _blockOnPageIndexes[translationGroupIndex]
                                ]
                            );
                        }

                        if ((typesToPut & BlockTypes.Video) == BlockTypes.Video)
                        {
                            PutRowInVideo(
                                currentRow,
                                _blocksOnPage[videoContainerIndex][
                                    _blockOnPageIndexes[videoContainerIndex]
                                ]
                            );
                        }

                        if ((typesToPut & BlockTypes.Widget) == BlockTypes.Widget)
                        {
                            PutRowInWidget(
                                currentRow,
                                _blocksOnPage[widgetContainerIndex][
                                    _blockOnPageIndexes[widgetContainerIndex]
                                ]
                            );
                        }

                        // Remove whatever we already imported. Usually this makes it 'none' and
                        // the loop stops. However, we don't have default pages that can hold both
                        // widgets and other types, so if we get such a combination the widget
                        // will be left over and added later.
                        // Also, the row may specify a particular page type, which might
                        // not have all the required slots.
                        typesInRow &= ~typesToPut;
                    }
                }
                else if (rowTypeLabel.StartsWith("[") && rowTypeLabel.EndsWith("]")) //This row is xmatter
                {
                    string dataBookLabel = rowTypeLabel.Substring(1, rowTypeLabel.Length - 2); //remove brackets
                    await UpdateDataDivFromRowAsync(currentRow, dataBookLabel);
                }
                _currentRowIndex++;
                if (extraRow)
                    _currentRowIndex++;
            }

            CleanupLeftOverPages();

            CleanupDataDiv();
            // This section is necessary to make sure changes to the dom are recorded.
            // If we run SS Importer from the CLI (without CollectionSettings), BringBookUpToDate()
            // will happen when we eventually open the book, but the user gets an updated thumbail and preview
            // if we do it here for the main production case where we DO have both the CollectionSettings
            // and the Book itself. Testing is the other situation (mostly) that doesn't use CollectionSettings.
            if (_collectionSettings != null && _book != null)
            {
                ImportLanguageNames();
                _book.BringBookUpToDate(new NullProgress());
            }

            if (_browser != null)
            {
                Action disposeBrowser = () =>
                {
                    _browser.Dispose();
                    _browser = null;
                };
                if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
                    ControlForInvoke.Invoke(disposeBrowser);
                else
                    disposeBrowser();
            }
            Progress("Done");
            return _warnings;
        }

        private void ImportLanguageNames()
        {
            var collectionSettingsAreDirty = false;
            var okToChangeSettings =
                (_tcManager == null)
                    ? TeamCollectionManager.CollectionSettingsCanBeEdited(_collectionSettings)
                    : _tcManager.OkToEditCollectionSettings;
            foreach (string lang in _sheet.Languages)
            {
                if (lang == "*")
                    continue;
                var langName = _sheet.Header.ColumnNameRow
                    .GetCell(_sheet.GetRequiredColumnForLang(lang))
                    .Content;
                // If this is the sign language, update the name only if the name is different.
                // (The sign language is recorded separately in the collection settings.)
                if (lang == _collectionSettings.SignLanguage.Tag)
                {
                    if (_collectionSettings.SignLanguage.Name != langName)
                    {
                        if (okToChangeSettings)
                            _collectionSettings.SignLanguage.SetName(langName, true);
                        else
                            Warn(
                                $"The name of the sign language ('${lang}') could not be changed to '{langName}' because you are not a team administrator."
                            );
                        collectionSettingsAreDirty = true;
                    }
                    continue;
                }
                // Set the name in the collection settings language list, creating the language if needed.
                var dirty = _collectionSettings.UpdateOrCreateCollectionLanguage(
                    lang,
                    langName,
                    okToChangeSettings
                );
                if (dirty)
                {
                    if (!okToChangeSettings)
                        Warn(
                            $"The name of the language for '{lang}' could not be changed to '{langName}' because you are not a team administrator."
                        );
                    collectionSettingsAreDirty = true;
                }
            }
            if (collectionSettingsAreDirty && okToChangeSettings)
            {
                _collectionSettings.Save();
            }
        }

        class PageRecord
        {
            public SafeXmlElement Page;
            public string SourceBookPath;
        }

        // This dictionary is progressively built from our collection of possible template books
        // as we need to process them in order to find a page with a requested label.
        // For each label we've encountered in one of the processed template books, it contains
        // the page SafeXmlElement and a note of which book it came from.
        Dictionary<string, PageRecord> _labelToPageRecord = new Dictionary<string, PageRecord>();

        // A list of books in which we will look for template pages referred to in page types
        // I thought it might be useful to set this in testing; otherwise, a default is used.
        // But performance of the test seems OK without using it.
        public List<string> BookTemplatePaths;
        private List<string> _bookTemplatePaths;

        // Books from which we've already used a page, and therefore, have imported any
        // special stylesheets it uses.
        HashSet<string> _importedBooks = new HashSet<string>();

        public static string GetLabelFromPage(SafeXmlElement page)
        {
            var labelElt = page?.SafeSelectNodes(".//div[@class='pageLabel' and @lang='en']")
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            if (labelElt != null)
            {
                // Note that while the file may show something like "Basic Text &amp; Picture",
                // the InnerText property already converts this to "Basic Text & Picture".
                return labelElt.InnerText.Trim();
            }

            return null;
        }

        /// <summary>
        /// Get a page with the specified label. We look at books that would currently
        /// show in the Add Page dialog for a page that has a div with class pageLabel
        /// and lang en whose content is the requested label (ignoring case).
        /// </summary>
        private SafeXmlElement GetPageForLabel(string label1)
        {
            var label = label1.ToLowerInvariant();
            if (_bookTemplatePaths == null)
            {
                if (BookTemplatePaths == null)
                {
                    var sourceCollectionPaths = new[]
                    {
                        Path.GetDirectoryName(_pathToBookFolder)
                    }.Concat(
                        SourceCollectionsList.GetCollectionFolders(
                            ProjectContext.SourceRootFolders()
                        )
                    );
                    var templateBookPaths = PageTemplatesApi.GetBooksInCollectionDirectories(
                        sourceCollectionPaths
                    );
                    _bookTemplatePaths = PageTemplatesApi.GetBookTemplatePaths(
                        null,
                        templateBookPaths
                    );
                }
                else
                    _bookTemplatePaths = new List<string>(BookTemplatePaths);
            }

            PageRecord result;
            while (
                !_labelToPageRecord.TryGetValue(label, out result) && _bookTemplatePaths.Count > 0
            )
            {
                // Process one more book. This is fairly time-consuming, so we only process as many
                // as we need to find the requested label. Many of them are in basic book, one of the first we try.
                var bookPath = _bookTemplatePaths[0];
                _bookTemplatePaths.RemoveAt(0);
                var dom = XmlHtmlConverter.GetXmlDomFromHtmlFile(bookPath, false);
                var pages = dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]");
                foreach (SafeXmlElement page in pages)
                {
                    var pageLabel = GetLabelFromPage(page).ToLowerInvariant();
                    // If we already found a page with this label, keep using the one we found first.
                    // This is so that if some random template contains a page with an unchanged
                    // label from one of our built-in templates, we will use the original on import.
                    if (pageLabel != null && !_labelToPageRecord.TryGetValue(pageLabel, out _))
                    {
                        _labelToPageRecord.Add(
                            pageLabel,
                            new PageRecord() { Page = page, SourceBookPath = bookPath }
                        );
                    }
                }
            }

            if (result == null)
                return null;
            ImportStylesheetsIfNeeded(result.SourceBookPath);
            ImportUserDefinedStylesForPage(result);

            return result.Page;
        }

        private void ImportUserDefinedStylesForPage(PageRecord result)
        {
            var head = result.Page.OwnerDocument
                .GetElementsByTagName("head")
                .Cast<SafeXmlElement>()
                .First();
            var userStylesOnPage = HtmlDom.GetUserModifiableStylesUsedOnPage(head, result.Page);
            var existingUserStyles = Book.Book.GetOrCreateUserModifiedStyleElementFromStorage(
                _destinationDom.Head
            );
            var newMergedUserStyleXml = HtmlDom.MergeUserStylesOnInsertion(
                existingUserStyles,
                userStylesOnPage,
                out bool _
            );
            existingUserStyles.InnerXml = newMergedUserStyleXml;
        }

        private void ImportStylesheetsIfNeeded(string sourceBook)
        {
            if (!_importedBooks.Contains(sourceBook))
            {
                // If we're pulling in pages from this book, we may need to pull in any unique stylesheets it uses, too.
                var sourceDom = new HtmlDom(
                    XmlHtmlConverter.GetXmlDomFromHtmlFile(sourceBook, false)
                );
                HtmlDom.AddStylesheetFromAnotherBook(sourceDom, _destinationDom);
                HtmlDom.CopyMissingStylesheetFiles(
                    sourceDom,
                    Path.GetDirectoryName(sourceBook),
                    _pathToBookFolder
                );
                _importedBooks.Add(sourceBook);
            }
        }

        private void PutRowInVideo(ContentRow currentRow, SafeXmlElement videoContainer)
        {
            var source = currentRow.GetCell(InternalSpreadsheet.VideoSourceColumnLabel).Text;
            Debug.Assert(
                !string.IsNullOrWhiteSpace(source),
                "Should not be trying to Put video when there is none"
            );
            var videoFile = Path.GetFileName(source);

            var videoElt = videoContainer.GetElementsByTagName("video").FirstOrDefault();
            if (videoElt == null)
            {
                // pathological
                videoElt = videoContainer.OwnerDocument.CreateElement("video");
                videoContainer.AppendChild(videoElt);
            }
            var srcElt = videoElt.GetElementsByTagName("source").FirstOrDefault();
            if (srcElt == null)
            {
                // pathological?
                srcElt = videoElt.OwnerDocument.CreateElement("source");
                videoElt.AppendChild(srcElt);
            }

            if (_pathToSpreadsheetFolder != null) // only null in some unit tests
            {
                var sourcePath = Path.Combine(_pathToSpreadsheetFolder, source);
                if (!RobustFile.Exists(sourcePath))
                {
                    Warn(
                        $"Video \"{sourcePath}\" on row {CurrentRowIndexForMessages} was not found."
                    );
                    return;
                }
                var videoDirectory = Path.Combine(_pathToBookFolder, "video");
                Directory.CreateDirectory(videoDirectory);

                var dest = Path.Combine(videoDirectory, videoFile);
                while (RobustFile.Exists(dest))
                {
                    videoFile = ImageUtils.GetUnusedFilename(
                        videoDirectory,
                        Path.GetFileNameWithoutExtension(videoFile),
                        Path.GetExtension(videoFile),
                        "video"
                    );
                    dest = Path.Combine(videoDirectory, videoFile);
                }
                // Enhance: we should probably do something to prevent multiple video elements
                // targeting the same file? But if importing to an existing book, we don't want
                // to duplicate them all.
                try
                {
                    RobustFile.Copy(sourcePath, dest, true);
                }
                catch (Exception e)
                    when (e is IOException
                        || e is SecurityException
                        || e is UnauthorizedAccessException
                    )
                {
                    Warn(
                        $"Bloom had trouble copying the file {sourcePath} to the video folder: "
                            + e.Message
                    );
                }
            }

            // Note that we always want a forward slash, even in Windows,
            // and we always copy directly to the video folder, wherever the file
            // is found.
            srcElt.SetAttribute(
                "src",
                UrlPathString.CreateFromUnencodedString("video/" + videoFile).UrlEncoded
            );
            videoContainer.RemoveClass("bloom-noVideoSelected");
        }

        private void PutRowInWidget(ContentRow currentRow, SafeXmlElement widgetContainer)
        {
            var source = currentRow.GetCell(InternalSpreadsheet.WidgetSourceColumnLabel).Text;
            Debug.Assert(
                !string.IsNullOrWhiteSpace(source),
                "Should not be trying to Put activity when there is none"
            );

            // Sourcepath typically is activities/activityFolder/activityFile.html
            // But it might conceivably not be in the spreadsheet activities folder.
            // Then how do we know how many levels up to copy?
            // It's possible index.html is nested more than one level in activityFolder!
            // For now, require the path to start with activities.
            if (!source.ToLowerInvariant().Replace("\\", "/").StartsWith("activities/"))
            {
                Warn(
                    $"Could not import the widget on row {CurrentRowIndexForMessages}. Widgets must be in the Spreadsheet folder's activities subfolder, but was '{source}'."
                );
                return;
            }

            var activityDirectory = source.Substring("activities/".Length); // typically activityFolder/activityFile.html
            var sourceDir = Path.GetDirectoryName(activityDirectory); // typically activityFolder
            while (!string.IsNullOrEmpty(Path.GetDirectoryName(sourceDir)))
                sourceDir = Path.GetDirectoryName(sourceDir);

            var iframeElt = widgetContainer.GetElementsByTagName("iframe").FirstOrDefault();
            if (iframeElt == null)
            {
                // pathological
                iframeElt = widgetContainer.OwnerDocument.CreateElement("iframe");
                widgetContainer.AppendChild(iframeElt);
            }

            // Note that we always want a forward slash, even in Windows,
            // and we always copy directly to the activities folder, wherever the file
            // is found. It's important that slashes NOT be encoded; our fileserver will
            // convert them and find the HTML file, but the iframe won't have the right
            // base URL which interferes with finding its assets using relative URLs.
            iframeElt.SetAttribute(
                "src",
                UrlPathString.CreateFromUnencodedString(source).UrlEncodedForHttpPath
            );
            widgetContainer.RemoveClass("bloom-noWidgetSelected");

            if (_pathToSpreadsheetFolder != null)
            {
                var sourcePath = Path.Combine(_pathToSpreadsheetFolder, source);
                if (!RobustFile.Exists(sourcePath))
                {
                    Warn(
                        $"Could not find '{sourcePath}' for the widget on row {CurrentRowIndexForMessages}."
                    );
                    return;
                }

                var activitiesDestDirectory = Path.Combine(_pathToBookFolder, "activities");
                Directory.CreateDirectory(activitiesDestDirectory);
                var sourceFolderPath = Path.Combine(
                    _pathToSpreadsheetFolder,
                    "activities",
                    sourceDir
                );
                try
                {
                    var destFolderPath = Path.Combine(_pathToBookFolder, "activities");
                    SIL.IO.RobustIO.DeleteDirectoryAndContents(
                        Path.Combine(destFolderPath, sourceDir)
                    );
                    DirectoryUtilities.CopyDirectory(sourceFolderPath, destFolderPath);
                }
                catch (Exception e)
                    when (e is IOException
                        || e is SecurityException
                        || e is UnauthorizedAccessException
                    )
                {
                    Warn(
                        $"Bloom had trouble copying the folder {sourceFolderPath} to the activities folder: "
                            + e.Message
                    );
                }
            }
        }

        private void CleanupLeftOverPages()
        {
            // the current page at _currentPageIndex must have some useful content,
            // because we would only advance _currentPageIndex to it in order to add some.
            // So we want to keep that one for sure.
            _currentPageIndex++;
            // If by any chance we arn't past the front matter (e.g., spreadsheet is empty) advance past it.
            while (
                _currentPageIndex < _pages.Count
                && XMatterHelper.IsFrontMatterPage(_pages[_currentPageIndex])
            )
                _currentPageIndex++;

            while (
                _currentPageIndex < _pages.Count
                && !XMatterHelper.IsBackMatterPage(_pages[_currentPageIndex])
            )
            {
                _currentPage = _pages[_currentPageIndex];
                _currentPage.ParentNode.RemoveChild(_currentPage);
                _currentPageIndex++;
            }
        }

        private async Task PutRowInImageAsync(
            ContentRow currentRow,
            ContentRow descriptionRow,
            SafeXmlElement currentImageContainer
        )
        {
            var spreadsheetImgPath = currentRow
                .GetCell(InternalSpreadsheet.ImageSourceColumnLabel)
                .Content;
            if (spreadsheetImgPath == InternalSpreadsheet.BlankContentIndicator)
            {
                spreadsheetImgPath = "placeHolder.png";
            }

            var destFileName = Path.GetFileName(spreadsheetImgPath);
            var fullSpreadsheetPath = spreadsheetImgPath;
            if (_pathToSpreadsheetFolder != null) //currently will only be null in tests
            {
                // To my surprise, if spreadsheetImgPath is rooted (a full path), this will just use it,
                // ignoring _pathToSpreadsheetFolder, which is what we want.
                fullSpreadsheetPath = Path.Combine(_pathToSpreadsheetFolder, spreadsheetImgPath);
            }
            // _pathToBookFolder can be null in tests
            if (!ImageUpdater.IsPlaceholderOrLicense(destFileName) && _pathToBookFolder != null)
            {
                var fullDestinationPath = Path.Combine(_pathToBookFolder, destFileName);
                while (RobustFile.Exists(fullDestinationPath))
                {
                    destFileName = ImageUtils.GetUnusedFilename(
                        _pathToBookFolder,
                        Path.GetFileNameWithoutExtension(destFileName),
                        Path.GetExtension(destFileName),
                        "image"
                    );
                    fullDestinationPath = Path.Combine(_pathToBookFolder, destFileName);
                }
            }

            var imgElement = GetImgFromContainer(currentImageContainer);
            // Enhance: warn if null?
            imgElement?.SetAttribute(
                "src",
                UrlPathString.CreateFromUnencodedString(destFileName).UrlEncoded
            );
            // Earlier versions of Bloom often had explicit height and width settings on images.
            // In case anything of the sort remains, it probably won't be correct for the new image,
            // so best to get rid of it.
            imgElement?.RemoveAttribute("height");
            imgElement?.RemoveAttribute("width");
            // image containers often have a generated title attribute that gives the file name and
            // notes about its resolution, etc. We think it will be regenerated as needed, but certainly
            // the one from a previous image is no use.
            currentImageContainer.RemoveAttribute("title");
            if (_pathToSpreadsheetFolder != null) //currently will only be null in tests
            {
                if (spreadsheetImgPath == "placeHolder.png")
                {
                    // Don't assume the source has it, let's get a copy from files shipped with Bloom
                    fullSpreadsheetPath = Path.Combine(
                        BloomFileLocator.FactoryCollectionsDirectory,
                        "template books",
                        "Basic Book",
                        "placeHolder.png"
                    );
                }

                CopyImageFileToDestination(destFileName, fullSpreadsheetPath, imgElement);
            }

            if (descriptionRow != null)
            {
                var group = currentImageContainer
                    .GetElementsByTagName("div")
                    .FirstOrDefault(
                        e => e.GetAttribute("class").Contains("bloom-imageDescription")
                    );
                if (group == null)
                {
                    group = currentImageContainer.OwnerDocument.CreateElement("div");
                    group.SetAttribute(
                        "class",
                        "bloom-translationGroup bloom-imageDescription bloom-trailingElement"
                    );
                    currentImageContainer.AppendChild(group);
                }
                await PutRowInGroupAsync(descriptionRow, group);
            }
        }

        private void CopyImageFileToDestination(
            string destFileName,
            string fullSpreadsheetPath,
            SafeXmlElement imgElement = null
        )
        {
            try
            {
                if (_pathToBookFolder != null && _pathToSpreadsheetFolder != null)
                {
                    var dest = Path.Combine(_pathToBookFolder, destFileName);
                    if (RobustFile.Exists(fullSpreadsheetPath))
                    {
                        RobustFile.Copy(fullSpreadsheetPath, dest, true);
                        if (imgElement != null)
                            ImageUpdater.UpdateImgMetadataAttributesToMatchImage(
                                _pathToBookFolder,
                                imgElement,
                                new NullProgress()
                            );
                    }
                    else
                    {
                        // Review: I doubt these messages are worth localizing? The sort of people who attempt
                        // spreadsheet import can likely cope with some English?
                        // +1 conversion from zero-based to 1-based counting, further adding header.RowCount
                        // makes it match up with the actual row label in the spreadsheet.
                        Warn(
                            $"Image \"{fullSpreadsheetPath}\" on row {CurrentRowIndexForMessages} was not found."
                        );
                    }
                }
            }
            catch (Exception e)
                when (e is IOException || e is SecurityException || e is UnauthorizedAccessException
                )
            {
                Warn(
                    $"Bloom had trouble copying the file {fullSpreadsheetPath} to the book folder or retrieving its metadata: "
                        + e.Message
                );
            }
        }

        void Warn(string message)
        {
            _warnings.Add(message);
            _progress?.MessageWithoutLocalizing(message, ProgressKind.Warning);
        }

        void Progress(string message)
        {
            // We don't think the importer messages are worth localizing at this point.
            // Users sophisticated enough to use this feature can probably cope with some English.
            _progress?.MessageWithoutLocalizing(message, ProgressKind.Progress);
        }

        private SafeXmlElement GetImgFromContainer(SafeXmlElement container)
        {
            return container.ChildNodes.FirstOrDefault(x => x.Name == "img") as SafeXmlElement;
        }

        private bool _foundCopyright;
        private bool _foundLicenseUrl;
        private bool _foundLicenseNotes;

        private async Task UpdateDataDivFromRowAsync(ContentRow currentRow, string dataBookLabel)
        {
            if (dataBookLabel.Contains("branding"))
                return; // branding data-div elements are complex and difficult and determined by current collection state
            // Only a few of these are worth reporting
            string whatsUpdated = null;
            bool rowAllowsAsteriskOnly = false;
            switch (dataBookLabel)
            {
                case "coverImage":
                    whatsUpdated = "the image on the cover";
                    break;
                case "bookTitle":
                    whatsUpdated = "the book title";
                    break;
                case "copyright":
                    whatsUpdated = "copyright information";
                    _foundCopyright = true;
                    rowAllowsAsteriskOnly = true;
                    break;
                case "licenseUrl":
                    _foundLicenseUrl = true;
                    break;
                case "licenseNotes":
                    _foundLicenseNotes = true;
                    break;
            }
            if (whatsUpdated != null)
                Progress($"Updating {whatsUpdated}.");

            var xPath = "div[@data-book=\"" + dataBookLabel + "\"]";
            var matchingNodes = _dataDivElement.SafeSelectNodes(xPath);
            SafeXmlElement templateNode;
            bool templateNodeIsNew = false;
            if (matchingNodes.Length > 0)
            {
                templateNode = (SafeXmlElement)matchingNodes[0];
            }
            else
            {
                templateNodeIsNew = true;
                templateNode = _destinationDom.RawDom.CreateElement("div");
                templateNode.SetAttribute("data-book", dataBookLabel);
            }

            var imageSrcCol = _sheet.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);
            var imageSrc = imageSrcCol >= 0 ? currentRow.GetCell(imageSrcCol).Content : null; // includes "images" folder
            var imageFileName = Path.GetFileName(imageSrc);
            bool specificLanguageContentFound = false;
            bool asteriskContentFound = false;

            //Whether or not a data-book div has a src attribute, we found that the innerText is used to set the
            //src of the image in the actual pages of the document, though we haven't found a case where they differ.
            //So during export we put the innerText into the image source column, and want to put it into
            //both src and innertext on import, unless the element is in the noSrcAttribute list
            if (imageFileName.Length > 0)
            {
                templateNode.SetAttribute("lang", "*");
                templateNode.InnerText = imageFileName;
                // Some old books do the cover image with a background-image setting in the style.
                // If present, this somehow takes precedence over the content of the node, so make sure
                // it isn't.
                templateNode.RemoveAttribute("style");

                if (!SpreadsheetExporter.DataDivImagesWithNoSrcAttributes.Contains(dataBookLabel))
                {
                    templateNode.SetAttribute("src", imageFileName);
                }
                if (templateNodeIsNew)
                    AddDataBookNode(templateNode);

                if (_pathToSpreadsheetFolder != null)
                {
                    // Make sure the image gets copied over too.
                    var fullSpreadsheetPath = Path.Combine(_pathToSpreadsheetFolder, imageSrc);
                    CopyImageFileToDestination(imageFileName, fullSpreadsheetPath);
                }
            }
            else //This is not an image node
            {
                if (dataBookLabel.Equals("coverImage"))
                {
                    Warn("No cover image found");
                }

                foreach (string lang in _sheet.Languages)
                {
                    var langVal = currentRow.GetCell(_sheet.GetRequiredColumnForLang(lang)).Content;
                    var langXPath =
                        "div[@data-book=\"" + dataBookLabel + "\" and @lang=\"" + lang + "\"]";
                    var langMatchingNodes = _dataDivElement
                        .SafeSelectNodes(langXPath)
                        .Cast<SafeXmlElement>();

                    if (!string.IsNullOrEmpty(langVal))
                    {
                        //Found content in spreadsheet for this language and row
                        if (lang.Equals("*"))
                        {
                            asteriskContentFound = true;
                        }
                        else
                        {
                            if (rowAllowsAsteriskOnly)
                            {
                                Warn(
                                    String.Format(
                                        "Ignored data for {0} in {1}. This field should only have data in the '*' column.",
                                        dataBookLabel,
                                        lang
                                    )
                                );
                                continue;
                            }
                            specificLanguageContentFound = true;
                        }

                        if (langMatchingNodes.Count() > 0) //Found matching node in dom. Update node.
                        {
                            var matchingNode = langMatchingNodes.First();
                            matchingNode.InnerXml = langVal;
                            if (langMatchingNodes.Count() > 1)
                            {
                                Warn(
                                    "Found more than one "
                                        + dataBookLabel
                                        + " element for language "
                                        + lang
                                        + " in the book dom. Only the first will be updated."
                                );
                            }
                            await AddAudioAsync(matchingNode, lang, currentRow);
                        }
                        else //No node for this language and data-book. Create one from template and add.
                        {
                            var newNode = (SafeXmlElement)templateNode.CloneNode(deep: true);
                            newNode.SetAttribute("lang", lang);
                            newNode.InnerXml = langVal;
                            AddDataBookNode(newNode);
                            await AddAudioAsync(newNode, lang, currentRow);
                        }
                    }
                    else //Spreadsheet cell for this row and language is empty. Remove the corresponding node if present.
                    {
                        foreach (SafeXmlNode n in langMatchingNodes.ToArray())
                        {
                            _dataDivElement.RemoveChild(n);
                        }
                    }
                }

                if (RemoveOtherLanguages)
                {
                    HtmlDom.RemoveOtherLanguages(
                        matchingNodes.Cast<SafeXmlElement>().ToList(),
                        _dataDivElement,
                        _sheet.Languages
                    );
                }

                if (
                    asteriskContentFound
                    && specificLanguageContentFound
                    && currentRow.MetadataKey != "[ISBN]"
                )
                {
                    Warn(
                        dataBookLabel
                            + " information found in both * language column and other language column(s)"
                    );
                }
            }
        }

        private void CleanupDataDiv()
        {
            if (_foundCopyright)
            {
                // If we didn't find a copyright, then probably the spreadsheet has no useful
                // copyright and license information, so we won't mess with what's in the target doc.
                // But if it has copyright, we expect it to have license information as well.
                // If it doesn't, that typically means the source book didn't either,
                // which has a specific significance (NullLicense or CustomLicense).
                // So make the destination book match, if we didn't find those.
                if (!_foundLicenseUrl)
                    RemoveDataDivField("licenseUrl");
                if (!_foundLicenseNotes)
                    RemoveDataDivField("licenseNotes");
            }
        }

        void RemoveDataDivField(string fieldName)
        {
            var nodes = _dataDivElement
                .SafeSelectNodes($"div[@data-book=\"{fieldName}\"]")
                .Cast<SafeXmlElement>()
                .ToArray();
            foreach (var node in nodes)
            {
                node.ParentNode.RemoveChild(node);
            }
        }

        private void AddDataBookNode(SafeXmlNode node)
        {
            _dataDivElement.AppendChild(node);
        }

        private int PageNumberToReport => _currentPageIndex + 1 - _frontMatterPagesSeen;

        private List<SafeXmlDocument> _sourcesForDefaultPages;
        private HashSet<SafeXmlDocument> _sourcesWithExtraStylesheets;
        private string _activityTemplatePath;

        /// <summary>
        /// Return a copy of a page from one of our factory templates that has the specified guid.
        /// This is used to get a default page that can contain a particular combination of block types.
        /// </summary>
        private void GenerateDefaultPage(string guid)
        {
            if (_sourcesForDefaultPages == null)
            {
                _sourcesForDefaultPages = new List<SafeXmlDocument>();
                _sourcesWithExtraStylesheets = new HashSet<SafeXmlDocument>();
                var path = Path.Combine(
                    BloomFileLocator.FactoryCollectionsDirectory,
                    "template books",
                    "Basic Book",
                    "Basic Book.html"
                );
                _sourcesForDefaultPages.Add(XmlHtmlConverter.GetXmlDomFromHtmlFile(path, false));
                path = Path.Combine(
                    BloomFileLocator.FactoryCollectionsDirectory,
                    "template books",
                    "Sign Language",
                    "Sign Language.html"
                );
                _sourcesForDefaultPages.Add(XmlHtmlConverter.GetXmlDomFromHtmlFile(path, false));
                _activityTemplatePath = Path.Combine(
                    BloomFileLocator.FactoryCollectionsDirectory,
                    "template books",
                    "Activity",
                    "Activity.html"
                );
                var activityDoc = XmlHtmlConverter.GetXmlDomFromHtmlFile(
                    _activityTemplatePath,
                    false
                );
                _sourcesForDefaultPages.Add(activityDoc);
                _sourcesWithExtraStylesheets.Add(activityDoc);
            }

            for (int i = 0; i < _sourcesForDefaultPages.Count; i++)
            {
                var templatePage =
                    _sourcesForDefaultPages[i].SelectSingleNode($"//div[@id='{guid}']")
                    as SafeXmlElement;
                if (templatePage != null)
                {
                    ImportPage(templatePage);
                    if (_sourcesWithExtraStylesheets.Contains(_sourcesForDefaultPages[i]))
                    {
                        // Activity folder is the only one that might require us to copy a stylesheet
                        ImportStylesheetsIfNeeded(_activityTemplatePath);
                    }
                    var pageLabel =
                        templatePage
                            .SafeSelectNodes(".//div[@class='pageLabel']")
                            .Cast<SafeXmlElement>()
                            .FirstOrDefault()
                            ?.InnerText ?? "";
                    Progress($"Adding page {PageNumberToReport} using a {pageLabel} layout");
                    return;
                }
            }

            throw new ApplicationException("Did not find expected page to insert");
        }

        // Insert a clone of templatePage into the document before _currentPage (or after _lastContentPage, if _currentPage is null),
        // and make _currentPage point to the new page.
        private void ImportPage(SafeXmlElement templatePage)
        {
            var newPage =
                _destinationDom.RawDom.DocumentElement.OwnerDocument.ImportNode(templatePage, true)
                as SafeXmlElement;
            BookStarter.SetupIdAndLineage(templatePage, newPage);
            _pages.Insert(_currentPageIndex, newPage);
            SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPage, _destLayout);
            // Correctly inserts at end if _currentPage is null, though this will hardly ever
            // be true because we normally have at least backmatter page to insert before.
            (_pages[0].ParentNode ?? _destinationDom.Body).InsertBefore(newPage, _currentPage);

            ClearPageContent(newPage);

            _currentPage = newPage;
        }

        private void ClearPageContent(SafeXmlElement page)
        {
            // clear everything: this is useful in case it has slots we won't use.
            // They might have content either from the original last page, or from the
            // modifications we already made to it.
            var editables = page.SafeSelectNodes(
                    ".//div[contains(@class, 'bloom-editable') and @lang != 'z']"
                )
                .Cast<SafeXmlElement>()
                .ToArray();
            foreach (var e in editables)
            {
                var allInGroup = e.ParentNode.ChildNodes
                    .Cast<SafeXmlNode>()
                    .Where(
                        x =>
                            x != e
                            && x is SafeXmlElement y
                            && y.GetAttribute("class").Contains("bloom-editable")
                    );
                if (allInGroup.Any())
                    e.ParentNode.RemoveChild(e);
                else
                {
                    // The only thing in the group is an element with a language other than 'z'.
                    // We want to keep this, but only as a template (e.g., to preserve the user style).
                    e.SetAttribute("lang", "z");
                    e.InnerText = "";
                }
            }

            var imageContainers = GetImageContainers(page);
            foreach (var c in imageContainers)
            {
                var img = GetImgFromContainer(c);
                img?.SetAttribute("src", "placeHolder.png");
                foreach (
                    var attr in new[] { "alt", "data-copyright", "data-creator", "data-license" }
                )
                    img?.RemoveAttribute(attr);
                c.RemoveAttribute("title");
            }

            // This is not tested yet, but we want to remove video content if any from whatever last page we're copying.
            foreach (
                var v in page.SafeSelectNodes(
                        ".//div[contains(@class, 'bloom-videoContainer')]/video"
                    )
                    .Cast<SafeXmlElement>()
                    .ToList()
            )
            {
                (v.ParentNode as SafeXmlElement).AddClass("bloom-noVideoSelected");
                v.ParentNode.RemoveChild(v);
            }

            // and widgets (also not tested)
            foreach (
                var w in page.SafeSelectNodes(
                        ".//div[contains(@class, 'bloom-widgetContainer')]/iframe"
                    )
                    .Cast<SafeXmlElement>()
                    .ToList()
            )
            {
                (w.ParentNode as SafeXmlElement).AddClass("bloom-noWidgetSelected");
                w.ParentNode.RemoveChild(w);
            }

            if (GetLabelFromPage(page).ToLowerInvariant() == "quiz page")
            {
                // No answer should be marked correct unless a row specifies it
                foreach (
                    SafeXmlElement tg in page.SafeSelectNodes(
                        ".//div[contains(@class, 'correct-answer')]"
                    )
                )
                    tg.RemoveClass("correct-answer");
                // All the answers are, for the moment, empty, so mark them accordingly.
                foreach (
                    SafeXmlElement tg in page.SafeSelectNodes(
                        ".//div[contains(@class, 'QuizAnswer-style')]"
                    )
                )
                    (tg.ParentNode.ParentNode as SafeXmlElement).AddClass("empty");
            }
        }

        private SafeXmlElement _lastContentPage; // Actually the last one we've seen so far, but used only when it really is the last
        public const string MissingHeaderMessage =
            "Bloom can only import spreadsheets that match a certain layout. In this spreadsheet, Bloom was unable to find the required \"[row type]\" column in row 1";

        private void AdvanceToNextNumberedPage(BlockTypes blocksNeeded, string pageType)
        {
            Debug.Assert(
                blocksNeeded != BlockTypes.None,
                "Shouldn't be advancing to another page unless we have something to put on it"
            );
            while (true)
            {
                _currentPageIndex++;

                if (_currentPageIndex >= _pages.Count)
                {
                    // We'll have to generate a new page. It will have what we need, so the loop
                    // will terminate there.
                    _currentPage = null; // this has an effect on where we insert the new page.
                    InsertCloneOfLastPageOrDefault(blocksNeeded, pageType);
                    return;
                }

                _currentPage = _pages[_currentPageIndex];
                // Is this where we want to stop, or should we skip this page and move on?
                // If it's a content page, we consider that a template content page we can
                // insert row content into...or if it doesn't hold the right sort of content,
                // we'll insert a page that does before it.
                // If it's a back matter page, we've come to the end...we'll have to insert
                // extra pages before it for whatever remaining content we have.
                if (!XMatterHelper.IsFrontMatterPage(_currentPage))
                {
                    break;
                }

                _frontMatterPagesSeen++;
            }

            var isBackMatter = XMatterHelper.IsBackMatterPage(_currentPage);
            if (isBackMatter)
            {
                InsertCloneOfLastPageOrDefault(blocksNeeded, pageType);
                return;
            }

            // OK, we've found a page in the template book which can hold imported content.
            // If it can hold the sort of content we have, we'll use it; otherwise,
            // we'll insert a page that can.
            CollectElementsFromCurrentPage();
            // Note that these are only updated when current page is set to an original usable page,
            // not when it is set to an inserted one. Thus, once we start adding pages at the end,
            // it and these variables are fixed at the values for the last content page.
            _lastContentPage = _currentPage;
            _blockTypesAvailableOnLastPage = BlockTypesAvailableOnPage;
            _pageTypeOfLastPage = GetLabelFromPage(_lastContentPage);

            if (
                InsertDefaultPageIfNeeded(
                    blocksNeeded,
                    _blockTypesAvailableOnLastPage,
                    pageType,
                    _pageTypeOfLastPage
                )
            )
            {
                // progress message already sent
                CollectElementsFromCurrentPage();
            }
            else
            {
                Progress($"Updating page {PageNumberToReport}");
            }

            if (GetLabelFromPage(_currentPage).ToLowerInvariant() == "quiz page")
            {
                // I'm not clear why we don't want to do this for all pages. But a number of existing
                // unit tests fail if we do, so for now I'm choosing not to change it. However, the
                // Quiz page definitely needs to be cleared, so that we won't keep more answers
                // than the current spreadsheet fills in.
                ClearPageContent(_currentPage);
            }

            for (int i = 0; i < blockTypeCount; i++)
                _blockOnPageIndexes[i] = -1;
        }

        private BlockTypes _blockTypesAvailableOnLastPage = BlockTypes.None;
        private string _pageTypeOfLastPage;

        BlockTypes BlockTypesAvailableOnPage => BlockTypesAvailable(_blocksOnPage);

        BlockTypes BlockTypesAvailable(List<SafeXmlElement>[] blocksOnPage)
        {
            var result = BlockTypes.None;
            var bitval = 1;
            for (var i = 0; i < blockTypeCount; i++)
            {
                var blocks = blocksOnPage[i];
                if (blocks?.Count > 0)
                    result |= (BlockTypes)(bitval);
                bitval *= 2;
            }

            return result;
        }

        private void InsertCloneOfLastPageOrDefault(BlockTypes blocksNeeded, string pageType)
        {
            // If we don't have a last page at all, the default state of the last page variables will
            // tell us to create one.
            if (
                !InsertDefaultPageIfNeeded(
                    blocksNeeded,
                    _blockTypesAvailableOnLastPage,
                    pageType,
                    _pageTypeOfLastPage
                )
            )
            {
                Progress($"Adding page {PageNumberToReport} by copying the last page");
                ImportPage(_lastContentPage);
            }

            CollectElementsFromCurrentPage();
            for (int i = 0; i < blockTypeCount; i++)
                _blockOnPageIndexes[i] = -1;
        }

        private Dictionary<BlockTypes, string> _pagesToInsert;

        Dictionary<BlockTypes, string> PageToInsert()
        {
            if (_pagesToInsert == null)
            {
                _pagesToInsert = new Dictionary<BlockTypes, string>();
                _pagesToInsert[BlockTypes.Image] = Book.Book.JustPictureGuid; // just an image
                _pagesToInsert[BlockTypes.Image | BlockTypes.Text] =
                    Book.Book.BasicTextAndImageGuid;
                _pagesToInsert[BlockTypes.Image | BlockTypes.Text | BlockTypes.Landscape] =
                    Book.Book.PictureOnLeftGuid;
                _pagesToInsert[BlockTypes.Text] = Book.Book.JustTextGuid;
                _pagesToInsert[BlockTypes.Video] = Book.Book.JustVideoGuid;
                _pagesToInsert[BlockTypes.Text | BlockTypes.Video] = Book.Book.VideoOverTextGuid;
                _pagesToInsert[BlockTypes.Image | BlockTypes.Video] = Book.Book.PictureAndVideoGuid;
                // not obvious which arrangement of text, video, and image would be best. None of our templates is designed for
                // portrait orientation. However, I think pictures and video are likely to shrink better than text, so it seems
                // best to default to the layout that leaves the most room for text. ("Big text" does not refer to point size
                // but to a large space for text).
                _pagesToInsert[BlockTypes.Text | BlockTypes.Image | BlockTypes.Video] =
                    Book.Book.BigTextDiglotGuid;
                _pagesToInsert[BlockTypes.Widget] = Book.Book.WidgetGuid;
            }
            return _pagesToInsert;
        }

        BlockTypes MakeBlockTypes(
            bool needTextGroup,
            bool needImageContainer,
            bool needVideoContainer,
            bool needWidgetContainer
        )
        {
            var result = BlockTypes.None;
            if (needTextGroup)
                result |= BlockTypes.Text;
            if (needImageContainer)
                result |= BlockTypes.Image;
            if (needVideoContainer)
                result |= BlockTypes.Video;
            if (needWidgetContainer)
                result |= BlockTypes.Widget;
            return result;
        }

        /// <summary>
        /// If necessary, insert a page. This could be because the next page in the template
        /// book won't hold all the block types we want to put on a single page, or because
        /// it doesn't match the page type that the spreadsheet says to use. Returns true
        /// if a page was inserted. (This is used in two contexts. In one, we already have
        /// a _contentPage of type pageWeHave in which blocksWeHave are available,and have
        /// already advanced to that, if a non-empty pageTypeNeeded required it. Returning
        /// false means we will use that page. In the other context, we have used up all the
        /// template pages. In that case, blocksWeHave and pageTypeWeHave reflect the last
        /// template page, which we are considering cloning. Returning false indicates that
        /// a clone of that page satisfies our constraints; the caller will then add the clone.)
        /// </summary>
        private bool InsertDefaultPageIfNeeded(
            BlockTypes blocksNeeded,
            BlockTypes blocksWeHave,
            string pageTypeNeeded,
            string pageTypeWeHave
        )
        {
            if (
                pageTypeWeHave == pageTypeNeeded
                && !string.IsNullOrEmpty(pageTypeNeeded)
                && (blocksNeeded & blocksWeHave) != BlockTypes.None
            )
            {
                // We have a page of the right type and it will hold something. So we will use it.
                // (Since we were given pageTypeNeeded, we will use a page of that type even if it
                // won't hold ALL the blocks we need.)
                // Return false to indicate we did not insert a page to use instead.
                return false;
            }

            if (_pathToBookFolder == null)
            {
                // In certain unit tests that predated page labels, we don't set things up enough for
                // finding pages by type, so ignore page type.
                pageTypeNeeded = null;
            }

            if (!string.IsNullOrEmpty(pageTypeNeeded))
            {
                // We need this particular type of page, and since we didn't return above,
                // we need to add it.
                var templatePage = GetPageForLabel(pageTypeNeeded);
                if (templatePage != null)
                {
                    var safeTemplatePage = templatePage;
                    var blocksOnPage = new List<SafeXmlElement>[blockTypeCount];
                    CollectElementsFromPage(safeTemplatePage, blocksOnPage);
                    var typesOnTemplatePage = BlockTypesAvailable(blocksOnPage);
                    if ((typesOnTemplatePage & blocksNeeded) == BlockTypes.None)
                    {
                        Warn(
                            $"Row {CurrentRowIndexForMessages} requested page type '{pageTypeNeeded}' but contains no data suitable for that page type."
                        );
                        // And we will continue to look for some page type that CAN hold the data.
                        // Review: alternatively, we could go ahead and insert/reuse the requested page, leave it empty, and carry on.
                        // Since we won't succeed in putting the current row data on it, the next iteration will be done without a pageTypeNeeded
                        // and should insert or reuse something more suitable.
                        blocksWeHave = BlockTypes.None; // Still want to force a new page, even if possibly what we need would fit on _currentPage.
                    }
                    else
                    {
                        ImportPage(safeTemplatePage);
                        return true;
                    }
                }
            }
            var blocksWeDontHave = BlockTypes.All & ~blocksWeHave;
            var needNewPage = (blocksNeeded & blocksWeDontHave) != BlockTypes.None;
            if (!needNewPage)
                return false;

            var key = blocksNeeded;
            if (_bookIsLandscape)
            {
                key |= BlockTypes.Landscape;
            }

            if (!PageToInsert().TryGetValue(key, out string guid) && _bookIsLandscape)
            {
                // Most of the blocktype combinations don't actually have a distinct entry
                // for landscape mode. So we'll try again without specifying that.
                PageToInsert().TryGetValue(blocksNeeded, out guid);
            }
            // If we still didn't get one, try removing widget flag. We don't have default pages
            // that can hold combinations of widgets and other things, so we'll get a page
            // that can hold everything else, and then make a separate page (in another iteration)
            // to hold the widget.
            if (
                string.IsNullOrEmpty(guid)
                && (blocksNeeded & BlockTypes.Widget) == BlockTypes.Widget
            )
            {
                key = blocksNeeded & ~BlockTypes.Widget;
                // This will conveniently re-apply the landscape logic etc as necessary
                // including returning false if we have all the other block types we need.
                return InsertDefaultPageIfNeeded(
                    key,
                    blocksWeHave,
                    pageTypeNeeded,
                    _pageTypeOfLastPage
                );
            }

            if (string.IsNullOrEmpty(guid))
            {
                throw new ApplicationException("Failed to find a default page type");
            }

            GenerateDefaultPage(guid);
            return true;
        }

        // A good index to show for the current row in messages. This should be the actual
        // row number Excel displays next to the row.
        private int CurrentRowIndexForMessages =>
            _sheet.GetIndexOfRow(_inputRows[_currentRowIndex]) + 1;

        private List<SafeXmlElement> GetImageContainers(SafeXmlElement ancestor)
        {
            return ancestor
                .SafeSelectNodes(".//div[contains(@class, 'bloom-imageContainer')]")
                .Cast<SafeXmlElement>()
                .ToList();
        }

        private List<SafeXmlElement> GetVideoContainers(SafeXmlElement ancestor)
        {
            return ancestor
                .SafeSelectNodes(".//div[contains(@class, 'bloom-videoContainer')]")
                .Cast<SafeXmlElement>()
                .ToList();
        }

        private List<SafeXmlElement> GetWidgetContainers(SafeXmlElement ancestor)
        {
            return ancestor
                .SafeSelectNodes(".//div[contains(@class, 'bloom-widgetContainer')]")
                .Cast<SafeXmlElement>()
                .ToList();
        }

        private void CollectElementsFromCurrentPage()
        {
            CollectElementsFromPage(_currentPage, _blocksOnPage);
        }

        private void CollectElementsFromPage(
            SafeXmlElement currentPage,
            List<SafeXmlElement>[] blocksOnPageCollector
        )
        {
            blocksOnPageCollector[imageContainerIndex] = GetImageContainers(currentPage);
            // We don't want image description slots as possible destinations for text.
            // They are handled by special extra rows inserted after the row that has the image.
            var allGroups = TranslationGroupManager.SortedGroupsOnPage(currentPage, true);
            blocksOnPageCollector[translationGroupIndex] = allGroups
                .Where(x => !x.GetAttribute("class").Contains("bloom-imageDescription"))
                .ToList();
            blocksOnPageCollector[videoContainerIndex] = GetVideoContainers(currentPage);
            blocksOnPageCollector[widgetContainerIndex] = GetWidgetContainers(currentPage);
        }

        // This helper method supports various tasks that have to be done for each block type
        // that is present in the enumeration. For each bit that is set in 'types',
        // the action is invoked, passing the index of the bit and the type.
        // For example, if types is Image | Video, the action will be invoked twice,
        // first with 1 (imageContainerIndex) and BlockTypes.Image, then with
        // 2 (videoContainerIndex) and BlockTypes.Video.
        void ForEachIndexInTypes(BlockTypes types, Action<int, BlockTypes> action)
        {
            var bit = 1;
            for (int i = 0; i < blockTypeCount; i++)
            {
                var currentType = (BlockTypes)bit;
                if ((types & currentType) != 0)
                    action(i, currentType);
                bit *= 2;
            }
        }

        // Set everything up so that we can insert as many as possible of the specified blocktypes into the current
        // page. If pageType is empty and the current page will hold all the types, it just advances
        // indexes in _blockOnPageIndexes so that they indicate the right places to insert. If pageType is
        // set, we will always advance to a new page of that type, as long as it will hold at least
        // one of the types of data needed.
        private BlockTypes AdvanceToNextSetOfBlocks(BlockTypes typesNeeded, string pageType)
        {
            var haveAllNeeded = true;
            var result = typesNeeded;
            ForEachIndexInTypes(
                typesNeeded,
                (i, _) =>
                {
                    _blockOnPageIndexes[i]++;
                    if (
                        _blocksOnPage[i] == null || _blockOnPageIndexes[i] >= _blocksOnPage[i].Count
                    )
                    {
                        haveAllNeeded = false;
                    }
                }
            );

            if (!haveAllNeeded || !string.IsNullOrEmpty(pageType))
            {
                AdvanceToNextNumberedPage(typesNeeded, pageType);
                ForEachIndexInTypes(
                    typesNeeded,
                    (i, blocktype) =>
                    {
                        if (_blocksOnPage[i].Count > 0)
                            // It's new page, we will use the first block of that type.
                            _blockOnPageIndexes[i] = 0;
                        else
                        {
                            // The new page doesn't have a block of this type, so clear the
                            // flag to let our caller know not to try to add it.
                            _blockOnPageIndexes[i] = -1;
                            result &= ~blocktype;
                        }
                    }
                );
            }

            return result;
        }

        private bool RowHasText(ContentRow row)
        {
            var sheetLanguages = _sheet.Languages;
            foreach (var lang in sheetLanguages)
            {
                var colIndex = _sheet.GetRequiredColumnForLang(lang);
                var content = row.GetCell(colIndex).Content;
                if (!string.IsNullOrEmpty(content))
                    return true;
            }
            return false;
        }

        public static bool IsEmptyCell(string content)
        {
            return string.IsNullOrEmpty(content)
                || content == InternalSpreadsheet.BlankContentIndicator
                // How the blank content indicator appears when read from spreadsheet by SpreadsheetIO
                || content == "<p>" + InternalSpreadsheet.BlankContentIndicator + "</p>";
        }

        /// <summary>
        /// This is where all the excitement happens. We update the specified group
        /// with the data from the spreadsheet row.
        /// </summary>
        /// <param name="row"></param>
        /// <param name="group"></param>
        private async Task PutRowInGroupAsync(ContentRow row, SafeXmlElement group)
        {
            if (group.GetAttribute("class").Contains("QuizAnswer-style"))
            {
                (group.ParentNode as SafeXmlElement).RemoveClass("empty");
            }
            var attributeData = row.GetCell(InternalSpreadsheet.AttributeColumnLabel)?.Content;
            if (!string.IsNullOrEmpty(attributeData))
            {
                var target = group;
                if (attributeData.StartsWith("../"))
                {
                    attributeData = attributeData.Substring(3);
                    target = (SafeXmlElement)group.ParentNode;
                }
                var parts = attributeData.Split('=');
                if (parts.Length == 2)
                {
                    if (parts[0] == "class")
                    {
                        // A special case we need for historical reasons, particularly Quiz Page.
                        // In future, we want to avoid using this approach, and design so that
                        // we simply set the value of an attribute starting with data-content.
                        target.AddClass(parts[1]);
                    }
                    else
                    {
                        target.SetAttribute(parts[0], parts[1]);
                    }
                }
            }
            var sheetLanguages = _sheet.Languages;
            foreach (var lang in sheetLanguages)
            {
                var colIndex = _sheet.GetRequiredColumnForLang(lang);
                var content = row.GetCell(colIndex).Content;
                var editable = HtmlDom.GetEditableChildInLang(group, lang);
                if (editable == null)
                {
                    if (IsEmptyCell(content))
                        continue; // Review: or make an empty one?

                    editable = TranslationGroupManager.MakeElementWithLanguageForOneGroup(
                        group,
                        lang
                    );
                }

                if (IsEmptyCell(content))
                {
                    editable.ParentNode.RemoveChild(editable);
                }
                else
                {
                    editable.InnerXml = content;
                }

                await AddAudioAsync(editable, lang, row);
                // If the inner XML contains a vertical bar at the end of a span, we assume that it is used
                // to split a phrase for audio and convert the vertical bar character to an invisible span
                // explicitly designating a bloom audio split marker.  Any other vertical bars are left
                // unchanged because the user might be inserting them for another purpose.  Opening the
                // talking book tool on a page effectively converts unadorned vertical bars to audio split
                // markers anyway, so the user can get that effect once the tool is opened to record audio.
                if (editable.InnerXml.Contains("|</span>"))
                {
                    var innerContent = editable.InnerXml;
                    editable.InnerXml = innerContent.Replace(
                        "|</span>",
                        "<span class='bloom-audio-split-marker'>\u200B</span></span>"
                    );
                }
            }

            if (RemoveOtherLanguages)
            {
                HtmlDom.RemoveOtherLanguages(@group, sheetLanguages);
            }
        }

        /// <summary>
        /// Add any audio for the given language from the row to the editable.
        /// Audio is present if the spreadsheet has a column [audio lg]
        /// (and possibly one [audio alignments lg]), and the row has data in the cell
        /// that corresponds to the [audio lg] column.
        /// If it has, we next determine whether the group is recorded in TextBox mode.
        /// This is true if the relevant [audio alignments lg] cell is non-empty.
        /// (Report an error if this is true and there is more than one recording file.)
        /// (Report an error if any audio file does not exist or has the wrong
        /// extension...possibly also if it isn't really an mp3? Not attempting that
        /// currently, though our code for getting the duration might throw.)
        /// We will set data-audiorecordingmode to TextBox or Sentence accordingly,
        /// and in TextBox mode, we will
        ///  - (report error if the alignments aren't numbers, or not ascending, or [warning] larger than
        ///    the audio file duration, or there is more than one but not as many as we have
        ///    sentences)
        ///  - copy all but the last number in alignments to data-audiorecordingendtimes.
        ///    (unless some are too large or won't parse...then we do a warning and convert back to unsplit).
        ///  - put the actual duration of the audio (measured from the one file) in a final entry
        ///    in data-audiorecordingendtimes and also a data-duration for the whole bloom-editable.
        ///  - if we have as many alignments as sentences, make appropriate
        ///    bloom-highlightSegment spans out of the sentences and add class bloom-postAudioSplit to the editable.
        ///  - add class audio-sentence to the bloom-editable
        ///  - copy the audio file into our audio folder. Use a name that is a valid
        ///    filename, a valid XML ID, does not conflict with any existing file, and
        ///    otherwise as similar to the given name as possible
        ///  - give the bloom-editable an id corresponding to the file
        ///  - make a recordingMD5 as usual.
        /// If instead we're in sentence mode (no alignment data),
        ///  - (report an error if we don't have one audio file per sentence, or if any are
        ///    missing that don't say they are)
        ///  - make a span for each sentence with
        ///    - class audio-sentence
        ///    - recordingmd5 computed as usual (assume the recording is current)
        ///    - id derived by copying the corresponding audio file, as above
        ///    - data-duration attribute computed from the file
        /// </summary>
        /// <param name="editable"></param>
        /// <param name="lang"></param>
        /// <param name="row"></param>
        private async Task AddAudioAsync(SafeXmlElement editable, string lang, ContentRow row)
        {
            // in case we're importing into an existing page, remove any existing audio-related stuff first.
            // We want to do this even if there isn't any new audio stuff.
            editable.RemoveAttribute("data-duration");
            editable.RemoveAttribute("data-audiorecordingendtimes");
            editable.RemoveAttribute("recordingmd5");
            editable.RemoveClass("bloom-postAudioSplit");
            editable.RemoveAttribute("data-audiorecordingmode");
            editable.RemoveClass("audio-sentence");
            foreach (
                var span in editable
                    .SafeSelectNodes("span[@class]")
                    .Cast<SafeXmlElement>()
                    .ToArray()
            )
            {
                var className = span.GetAttribute("class");
                if (className == "audio-sentence" || className == "bloom-highlightSegment")
                {
                    // remove these spans, keeping content
                    foreach (var node in span.ChildNodes)
                    {
                        span.ParentNode.InsertBefore(node, span);
                    }
                    span.ParentNode.RemoveChild(span);
                }
            }

            var audioColIndex = _sheet.GetOptionalColumnForLangAudio(lang);
            if (audioColIndex == -1)
                return; // no audio data for this language at all.
            if (_pathToSpreadsheetFolder == null)
                return; // happens during unit tests not focused on audio
            var audioFilesList = row.GetCell(audioColIndex).Content;
            // We need the 'where' because Split creates a single empty string if the input is empty.
            var audioFiles = audioFilesList
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x != "")
                .ToArray();
            var audioAlignColIndex = _sheet.GetOptionalColumnForAudioAlignment(lang);
            var alignmentData = "";
            if (audioAlignColIndex >= 0)
            {
                alignmentData = row.GetCell(audioAlignColIndex).Content;
            }

            if (audioFiles.Length == 0 && string.IsNullOrEmpty(alignmentData))
                return; // OK to have no audio at all, even if the columns are there.

            var alignments = alignmentData.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries
            );
            var paras = editable.SafeSelectNodes(".//p").Cast<SafeXmlElement>();
            // In this variable we build a list of paragraphs and their sentences, for later processing
            // after we make some sanity checks. The immediate goal is to count sentences that could
            // have recording or alignment information.
            var paraFragments = new List<Tuple<SafeXmlElement, string[]>>();
            int sentenceCount = 0;
            foreach (var para in paras)
            {
                var text = para.InnerXml.Replace("'", "\\'");
                // Sentences is a sequence of strings, each of which is the text of a JS TextFragment,
                // prepended with 's' if it's a sentence and ' ' if it's not.
                string[] fragments = new string[0];
                if (text.Length > 0) // if not, we get a spurious empty string instead of an empty array.
                {
                    Func<Task> runJs = async () =>
                        fragments = await GetSentenceFragmentsAsync(text);
                    if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
                        await (Task)ControlForInvoke.Invoke(runJs);
                    else
                        await runJs();
                }

                paraFragments.Add(Tuple.Create(para, fragments));
                sentenceCount += fragments.Count(x => x.StartsWith("s"));
            }

            if (alignments.Length > 0)
            {
                // The presence of alignment data indicates TextBox recording mode, so the editable should
                // only have one audio file.
                if (audioFiles.Length > 1)
                {
                    _progress.MessageWithParams(
                        "TooManyAudioFilesForAlignment",
                        "",
                        "Did not import audio on page {0} because there should be only one audio file when audio alignment is specified.",
                        ProgressKind.Error,
                        PageNumberToReport.ToString()
                    );
                    return;
                }
                var audioFile = audioFilesList;
                var durationStr = await AddAudioFileAsync(editable, audioFile, row);
                if (String.IsNullOrEmpty(durationStr))
                    return; // already reported, just don't add any audio information.
                var duration = double.Parse(durationStr, CultureInfo.InvariantCulture);
                editable.SetAttribute("data-audiorecordingmode", "TextBox");
                editable.AddClass("audio-sentence");
                var alignmentChecks = alignments.Select(x =>
                {
                    if (
                        double.TryParse(
                            x,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out double val
                        )
                    )
                    {
                        if (val > duration + 0.02)
                        {
                            return "toobig";
                        }

                        return "good";
                    }

                    return "bad";
                });
                // Yes, we might be asking for -1 alignmentChecks, that just returns none.
                // Decided not to check whether the last (often only) alignment is too big, since we will
                // automatically correct it to the actual duration anyway.
                if (alignmentChecks.Take(alignments.Length - 1).Any(x => x == "toobig"))
                {
                    _progress.MessageWithParams(
                        "AlignmentsTooBig",
                        "",
                        "Removed audio alignments on page {0} because some values in the list given ('{1}') are larger than the duration of the audio file ({2}).",
                        ProgressKind.Warning,
                        PageNumberToReport.ToString(),
                        alignmentData,
                        durationStr
                    );
                    return;
                }

                if (alignmentChecks.Any(x => x == "bad"))
                {
                    _progress.MessageWithParams(
                        "AlignmentsInvalid",
                        "",
                        "Removed audio alignments on page {0} because some values in '{1}' are not valid numbers.",
                        ProgressKind.Warning,
                        PageNumberToReport.ToString(),
                        alignmentData
                    );
                    return;
                }

                if (sentenceCount == alignments.Length)
                {
                    // treat as split TextBox (although just possibly it was originally a single sentence
                    // recorded in TextBox mode and never split)

                    // Break it up into spans with ids and the bloom-highlightSegment class
                    foreach (var paraGroup in paraFragments)
                    {
                        var para = paraGroup.Item1;
                        var fragments = paraGroup.Item2;

                        para.InnerText = "";
                        foreach (var taggedFragment in fragments)
                        {
                            var fragment = taggedFragment.Substring(1);
                            if (taggedFragment.StartsWith("s"))
                            {
                                var span = para.OwnerDocument.CreateElement("span");
                                HtmlDom.SetNewHtmlIdValue(span); // need it to have one, don't care what
                                span.SetAttribute("class", "bloom-highlightSegment");
                                span.InnerXml = fragment;
                                para.AppendChild(span);
                            }
                            else
                            {
                                // a white space fragment.
                                // There may be formatting tags (e.g. italics or colors) which we will ignore since this is just whitespace anyway
                                // And otherwise they will get escaped and end up in the text
                                var tempElement = para.OwnerDocument.CreateElement("span");
                                tempElement.InnerXml = fragment;
                                var node = para.OwnerDocument.CreateTextNode(tempElement.InnerText);
                                para.AppendChild(node);
                            }
                        }
                    }

                    // Set the end times, using the given data except for the last one, which should
                    // be the accurate duration.
                    var alignmentsVal = durationStr;
                    if (alignments.Length > 1)
                        alignmentsVal =
                            String.Join(" ", alignments.Take(alignments.Length - 1))
                            + " "
                            + durationStr;
                    editable.SetAttribute("data-audiorecordingendtimes", alignmentsVal);
                    editable.AddClass("bloom-postAudioSplit");
                }
                // If there is just one alignment, but not just one sentence, we're in TextBox mode, unsplit.
                // There's nothing more to do. But any other non-matching number is an error.
                else if (alignments.Length != 1)
                {
                    _progress.MessageWithParams(
                        "AlignmentMismatch",
                        "",
                        "Did not import audio alignments on page {0} because there are {1} audio alignments for {2} sentences; they should match up.",
                        ProgressKind.Error,
                        PageNumberToReport.ToString(),
                        alignments.Length.ToString(),
                        sentenceCount.ToString()
                    );
                    return;
                }
            }
            else
            {
                // Sentence mode
                if (audioFiles.Length != sentenceCount)
                {
                    _progress.MessageWithParams(
                        "AudioFileMismatch",
                        "",
                        "Did not import audio on page {0} because there are {1} audio files for {2} sentences; they should match up. Use 'missing' if necessary.",
                        ProgressKind.Error,
                        PageNumberToReport.ToString(),
                        audioFiles.Length.ToString(),
                        sentenceCount.ToString()
                    );
                    return;
                }
                editable.SetAttribute("data-audiorecordingmode", "Sentence");
                // Break it up into spans with ids, the audio-sentence class, etc.
                var audioFileIndex = 0;
                foreach (var paraGroup in paraFragments)
                {
                    var para = paraGroup.Item1;
                    var fragments = paraGroup.Item2;

                    para.InnerText = "";
                    foreach (var taggedFragment in fragments)
                    {
                        var fragment = taggedFragment.Substring(1);
                        if (taggedFragment.StartsWith("s"))
                        {
                            var span = para.OwnerDocument.CreateElement("span");
                            var audioFile = audioFiles[audioFileIndex++];
                            span.InnerXml = fragment;
                            await AddAudioFileAsync(span, audioFile, row);
                            span.AddClass("audio-sentence");
                            para.AppendChild(span);
                        }
                        else
                        {
                            var node = para.OwnerDocument.CreateTextNode(fragment);
                            para.AppendChild(node);
                        }
                    }
                }
            }
        }

        protected virtual async Task<string[]> GetSentenceFragmentsAsync(string text)
        {
            return (
                await (await GetBrowserAsync()).GetStringFromJavascriptAsync(
                    $"spreadsheetBundle.split('{text}')"
                )
            ).Split('\n');
        }

        /// <summary>
        /// Add an audio file to the book, linked by adding an id matching its name to the element.
        /// The audio file will be copied to the book's audio folder.
        /// The name may be changed in various circumstances.
        /// Also sets the data-duration and recordingmd5 attributes of the element, which means
        /// it is important to finalize the text of the element before calling this.
        /// </summary>
        /// <param name="elt"></param>
        /// <param name="audioFile"></param>
        /// <returns>a string representation of the duration of the audio file, in seconds, or an empty string,
        /// if we don't find a valid audio file.</returns>
        private async Task<string> AddAudioFileAsync(
            SafeXmlElement elt,
            string audioFile,
            ContentRow row
        )
        {
            if (audioFile == "missing")
            {
                HtmlDom.SetNewHtmlIdValue(elt);
                return "0";
            }
            var id = SanitizeXHtmlId(
                MiscUtils.TruncateFileBasename(
                    Path.GetFileNameWithoutExtension(audioFile),
                    Path.GetExtension(audioFile),
                    "sound"
                )
            );
            var destFile = id + Path.GetExtension(audioFile);
            // We may as well set this; elements with class audio-sentence are supposed to have
            // ids, even if there is no corresponding file.
            elt.SetAttribute("id", id);
            if (_pathToSpreadsheetFolder == null)
                return "0"; // unit tests, we can't try to copy file.
            string src = audioFile;
            if (audioFile.StartsWith("./"))
                src = Path.Combine(_pathToSpreadsheetFolder, audioFile.Substring(2));
            if (RobustFile.Exists(src))
            {
                var audioPath = Path.Combine(_pathToBookFolder, "audio");
                Directory.CreateDirectory(audioPath);
                var destPath = Path.Combine(audioPath, destFile);
                if (RobustFile.Exists(destPath))
                {
                    id = HtmlDom.SetNewHtmlIdValue(elt);
                    destPath = Path.Combine(audioPath, id + ".mp3");
                }

                double duration;
                try
                {
                    duration = GetDuration(src);
                }
                catch (InvalidDataException ex)
                {
                    _progress.MessageWithParams(
                        "InvalidMp3",
                        "",
                        "Did not import audio on page {0} because the audio file '{1}' is not a valid mp3 file.",
                        ProgressKind.Error,
                        PageNumberToReport.ToString(),
                        src.Replace("\\", "/")
                    );
                    return "0";
                }
                RobustFile.Copy(src, destPath);
                var durationStr = duration.ToString(CultureInfo.InvariantCulture);
                elt.SetAttribute("data-duration", durationStr);
                string md5 = await GetMd5Async(elt);
                elt.SetAttribute("recordingmd5", md5);
                return durationStr;
            }

            src = src.Replace("\\", "/"); // works on all platforms, simplifies testing
            var rowId = row.GetCell(0).Content;
            if (rowId == InternalSpreadsheet.PageContentRowLabel)
            {
                _progress.MessageWithParams(
                    "MissingAudioFile",
                    "",
                    "Did not import audio on page {0} because '{1}' was not found.",
                    ProgressKind.Error,
                    PageNumberToReport.ToString(),
                    src
                );
            }
            else
            {
                _progress.MessageWithParams(
                    "MissingAudioFile",
                    "",
                    "Did not import audio for {0} because '{1}' was not found.",
                    ProgressKind.Error,
                    rowId.Trim(new[] { '[', ']' }),
                    src
                );
            }

            return "";
        }

        delegate Task<string> ElementStringTask(SafeXmlElement elt);

        protected virtual async Task<string> GetMd5Async(SafeXmlElement elt)
        {
            if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
            {
                var result =
                    (Task<string>)ControlForInvoke.Invoke(new ElementStringTask(GetMd5Async), elt);
                return await (result);
            }
            return await (await GetBrowserAsync()).GetStringFromJavascriptAsync(
                $"spreadsheetBundle.getMd5('{elt.InnerText.Replace("'", "\\'")}')"
            );
        }

        internal static string SanitizeXHtmlId(string id)
        {
            if (!XmlConvert.IsStartNCNameChar(id[0]))
                id = 'i' + id;
            for (int i = 0; i < id.Length; )
            {
                if (!XmlConvert.IsNCNameChar(id[i]))
                    id = id.Replace(id[i].ToString(), "");
                else
                    i++;
            }
            if (id.Length > 1)
                return id;
            return "defaultId"; // arbitrary, and likely a duplicate, but at least valid.
        }

        private double GetDuration(string path)
        {
            return Utils.MiscUtils.GetMp3TimeSpan(path, true).TotalSeconds;
        }
    }

    /// <summary>
    /// Encapsulates combinations of types of block that a page might contain
    /// as sources or destinations for spreadsheet content.
    /// </summary>
    [Flags]
    enum BlockTypes
    {
        None = 0,

        // The bitshifts make sure these stay in sync
        Text = 1 << SpreadsheetImporter.translationGroupIndex,
        Image = 1 << SpreadsheetImporter.imageContainerIndex,
        Video = 1 << SpreadsheetImporter.videoContainerIndex,
        Widget = 1 << SpreadsheetImporter.widgetContainerIndex,
        All = 15, // deliberately not including landscape!

        // This is special. A combination of the above flags may be used as an index
        // to look up a page guid that should be inserted when we need that combination
        // of blocks on a page. Sometimes, we want a different page for Landscape.
        // If so, we can or this value in to make a suitable key.
        // It is not actually a type of block.
        Landscape = 1024
    }
}
