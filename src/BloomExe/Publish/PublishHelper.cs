using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.FontProcessing;
using Bloom.Publish.Epub;
using Bloom.SafeXml;
using Bloom.web;
using Bloom.web.controllers;
using Bloom.Workspace;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Xml;

namespace Bloom.Publish
{
    public class PublishHelper : IDisposable
    {
        public const string kSimpleComprehensionQuizJs = "simpleComprehensionQuiz.js";
        public const string kVideoPlaceholderImageFile = "video-placeholder.svg";
        private static PublishHelper _latestInstance;

        public PublishHelper()
        {
            if (!InPublishTab && !Program.RunningUnitTests && !Program.RunningInConsoleMode)
            {
                throw new InvalidOperationException(
                    "Should not be creating bloom book while not in publish tab"
                );
            }
            _latestInstance = this;
        }

        public Control ControlForInvoke { get; set; }

        public static void Cancel()
        {
            _latestInstance = null;
        }

        public static bool InPublishTab { get; set; }

        private Browser _browser;
        public Browser BrowserForPageChecks
        {
            get
            {
                if (_browser == null)
                {
                    Debug.Assert(
                        ControlForInvoke != null
                            || Program.RunningUnitTests
                            || Program.RunningOnUiThread
                    );
                    if (ControlForInvoke != null && ControlForInvoke.InvokeRequired)
                    {
                        ControlForInvoke.Invoke(
                            (Action)(() => _browser = BrowserMaker.MakeBrowser())
                        );
                    }
                    else
                    {
                        _browser = BrowserMaker.MakeBrowser();
                    }
                }
                return _browser;
            }
        }

        // The only reason this isn't just ../* is performance. We could change it.  It comes from the need to actually
        // remove any elements that the style rules would hide, because epub readers ignore visibility settings.
        private const string kSelectThingsThatCanBeHidden = ".//div | .//img";

        /// <summary>
        /// Remove unwanted content from the XHTML of this book.  As a side-effect, store the fonts used in the remaining
        /// content of the book.
        /// </summary>
        public void RemoveUnwantedContent(
            HtmlDom dom,
            Book.Book book,
            bool removeInactiveLanguages,
            ISet<string> warningMessages,
            EpubMaker epubMaker = null,
            bool keepPageLabels = false
        )
        {
            FontsUsed.Clear();
            FontsAndLangsUsed.Clear();
            // Removing unwanted content involves a real browser really navigating. I'm not sure exactly why,
            // but things freeze up if we don't do it on the UI thread.
            if (ControlForInvoke != null)
            {
                ControlForInvoke.Invoke(
                    (Action)(
                        delegate
                        {
                            RemoveUnwantedContentInternal(
                                dom,
                                book,
                                removeInactiveLanguages,
                                epubMaker,
                                warningMessages,
                                keepPageLabels
                            );
                        }
                    )
                );
            }
            else
                RemoveUnwantedContentInternal(
                    dom,
                    book,
                    removeInactiveLanguages,
                    epubMaker,
                    warningMessages,
                    keepPageLabels
                );
        }

        /// <summary>
        /// This javascript function is run in the browser to get the display and font information
        /// for all the elements with an id.  The C# code must ensure that all elements that might
        /// possibly be hidden by CSS have an id attribute before loading the document in the browser.
        /// </summary>
        /// <remarks>
        /// Running this script and getting the results for the whole page is much faster than running
        /// two trivial scripts for each element in WebView2. See BL-12402.
        /// </remarks>
        public const string GetElementDisplayAndFontInfoJavascript =
            @"(() =>
{
	const elementsInfo = [];
	const elementsWithId = document.querySelectorAll(""[id]"");
	elementsWithId.forEach(elt => {
		const style = getComputedStyle(elt, null);
		if (style) {
			elementsInfo.push({
				id: elt.id,
				display: style.display,
				fontFamily: style.getPropertyValue(""font-family"")
			});
		}
	});
	return { results: elementsInfo };
})();";

        /// <summary>
        /// Store the display and font information for all the elements returned as JSON
        /// from executing the script in GetElementDisplayAndFontInfoJavascript.
        /// </summary>
        /// <remarks>
        /// This information will be loaded into two Dictionary objects, one mapping id to display
        /// and the other mapping id to font-family for faster lookup.
        /// </remarks>
        private class ElementInfoArray
        {
            public ElementInfo[] results;
        }

        /// <summary>
        /// Store the display and font information for a single element.
        /// </summary>
        private class ElementInfo
        {
            public string id;
            public string display;
            public string fontFamily;
        }

        Dictionary<string, string> _mapIdToDisplay = new Dictionary<string, string>();
        Dictionary<string, string> _mapIdToFontFamily = new Dictionary<string, string>();

        private void RemoveUnwantedContentInternal(
            HtmlDom dom,
            Book.Book book,
            bool removeInactiveLanguages,
            EpubMaker epubMaker,
            ISet<string> warningMessages,
            bool keepPageLabels = false
        )
        {
            var startRemoveTime = DateTime.Now;
            // The ControlForInvoke can be null for tests.  If it's not null, we better not need an Invoke!
            Debug.Assert(ControlForInvoke == null || !ControlForInvoke.InvokeRequired); // should be called on UI thread.
            Debug.Assert(dom != null && dom.Body != null);

            // Collect all the page divs.
            var pageElts = new List<SafeXmlElement>();
            if (epubMaker != null)
            {
                pageElts.Add((SafeXmlElement)dom.Body.FirstChild); // already have a single-page dom prepared for export
            }
            else
            {
                foreach (SafeXmlElement page in book.GetPageElements())
                    pageElts.Add(page);
            }

            RemoveEnterpriseFeaturesIfNeeded(book, pageElts, warningMessages);

            // Remove any left-over bubbles
            foreach (SafeXmlElement elt in dom.RawDom.SafeSelectNodes("//label"))
            {
                if (elt.HasClass("bubble"))
                    elt.ParentNode.RemoveChild(elt);
            }
            // Remove page labels and descriptions.  Also remove pages (or other div elements) that users have
            // marked invisible.  (The last mimics the effect of bookLayout/languageDisplay.less for editing
            // or PDF published books.)
            foreach (SafeXmlElement elt in dom.RawDom.SafeSelectNodes("//div"))
            {
                if (!book.IsTemplateBook)
                {
                    if (!keepPageLabels && elt.HasClass("pageLabel"))
                        elt.ParentNode.RemoveChild(elt);

                    if (elt.HasClass("pageDescription"))
                        elt.ParentNode.RemoveChild(elt);
                }
            }
            // Our recordingmd5 attribute is not allowed by epub
            foreach (
                SafeXmlElement elt in HtmlDom.SelectAudioSentenceElementsWithRecordingMd5(
                    dom.RawDom.DocumentElement
                )
            )
            {
                elt.RemoveAttribute("recordingmd5");
            }
            // Users should not be able to edit content of published books
            foreach (SafeXmlElement elt in dom.RawDom.SafeSelectNodes("//div[@contenteditable]"))
            {
                elt.RemoveAttribute("contenteditable");
            }

            foreach (
                var div in dom.Body.SafeSelectNodes("//div[@role='textbox']").Cast<SafeXmlElement>()
            )
            {
                div.RemoveAttribute("role"); // this isn't an editable textbox in an ebook
                div.RemoveAttribute("aria-label"); // don't want this without a role
                div.RemoveAttribute("spellcheck"); // too late for spell checking in an ebook
                div.RemoveAttribute("content-editable"); // too late for editing in an ebook
            }

            // Clean up img elements (BL-6035/BL-6036 and BL-7218)
            foreach (var img in dom.Body.SafeSelectNodes("//img").Cast<SafeXmlElement>())
            {
                // Ensuring a proper alt attribute is handled elsewhere
                var src = img.GetOptionalStringAttribute("src", null);
                if (String.IsNullOrEmpty(src) || src == "placeHolder.png")
                {
                    // If this is a template book, then the whole point of the book is to not have content. So then we want to preserve the placeholders so
                    // that people looking at the book on Bloom Library can see how the template pages are constructed.
                    if (!book.IsTemplateBook)
                    {
                        // If the image file doesn't exist, we want to find out about it.  But if there is no
                        // image file, epubcheck complains and it doesn't do any good anyway.
                        img.ParentNode.RemoveChild(img);
                    }
                }
                else
                {
                    var parent = img.ParentNode as SafeXmlElement;
                    parent.RemoveAttribute("title"); // We don't want this in published books.
                    img.RemoveAttribute("title"); // We don't want this in published books.  (probably doesn't exist)
                    img.RemoveAttribute("type"); // This is invalid, but has appeared for svg branding images.
                }
            }

            if (epubMaker != null)
            {
                // epub-check doesn't like these attributes (BL-6036).  I suppose BloomReader might find them useful.
                foreach (
                    var div in dom.Body
                        .SafeSelectNodes("//div[contains(@class, 'split-pane-component-inner')]")
                        .Cast<SafeXmlElement>()
                )
                {
                    div.RemoveAttribute("min-height");
                    div.RemoveAttribute("min-width");
                }
            }

            // These elements are inserted and supposedly removed by the ckeditor javascript code.
            // But at least one book created by our test team still has one output to an epub.  If it
            // exists, it probably has a style attribute (position:fixed) that epubcheck won't like.
            // (fixed position way off the screen to hide it)
            foreach (
                var div in dom.Body
                    .SafeSelectNodes("//*[@data-cke-hidden-sel]")
                    .Cast<SafeXmlElement>()
            )
            {
                div.ParentNode.RemoveChild(div);
            }

            // Finally we try to remove elements (except image descriptions) that aren't visible.
            // To accurately determine visibility, we point a real browser at the document.
            // We've had some problems with this, which we now think are fixed; if it doesn't work, for
            // BloomReader we just allow the document to be a little bigger than it needs to be.
            // BloomReader will obey rules like display:none.
            // For epubs, we don't; display:none is not reliably obeyed, so the reader could see
            // unexpected things.
            // We make this displayDom because, at least in the case of flowable epubs,
            // the very simplified stylesheet in use in the epub dom doesn't hide anything, so we
            // need to use the real DOM with its stylesheets to figure out what is hidden there
            // and should be removed in the epub.
            HtmlDom displayDom = null;
            foreach (SafeXmlElement page in pageElts)
            {
                EnsureAllThingsThatCanBeHiddenHaveIds(page);
                if (displayDom == null)
                {
                    displayDom = book.GetHtmlDomWithJustOnePage(page);
                    displayDom.BaseForRelativePaths = book.FolderPath;
                }
                else
                {
                    var pageNode = displayDom.RawDom.ImportNode(page, true);
                    displayDom.Body.AppendChild(pageNode);
                }
            }
            if (displayDom == null)
                return;
            if (epubMaker != null)
                epubMaker.AddEpubVisibilityStylesheetAndClass(displayDom);
            if (this != _latestInstance)
                return;
            if (
                !BrowserForPageChecks.NavigateAndWaitTillDone(
                    displayDom,
                    10000,
                    InMemoryHtmlFileSource.JustCheckingPage,
                    () => this != _latestInstance,
                    false
                )
            )
            {
                // We started having problems with timeouts here (BL-7892).
                // We may as well carry on. We only need the browser to have navigated so calls to IsDisplayed(elt)
                // below will give accurate answers. Even if the browser hasn't gotten that far yet (e.g., in
                // a long document), it may stay ahead of us. We'll report a failure (currently only for epubs, see above)
                // if we actually can't find the element we need in IsDisplayed().
                Debug.WriteLine("Failed to navigate fully to RemoveUnwantedContentInternal DOM");
                Logger.WriteEvent("Failed to navigate fully to RemoveUnwantedContentInternal DOM");
            }
            if (this != _latestInstance)
                return;

            // Get and store the display and font information for each element in the DOM.
            var elementsInfo = BrowserForPageChecks.RunJavascriptWithStringResult_Sync_Dangerous(
                GetElementDisplayAndFontInfoJavascript
            );
            var rawInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<ElementInfoArray>(
                elementsInfo
            );
            if (rawInfo != null)
            {
                foreach (var info in rawInfo.results)
                {
                    _mapIdToDisplay[info.id] = info.display;
                    _mapIdToFontFamily[info.id] = info.fontFamily;
                }
            }
            var toBeDeleted = new List<SafeXmlElement>();
            // Deleting the elements in place during the foreach messes up the list and some things that should be deleted aren't
            // (See BL-5234). So we gather up the elements to be deleted and delete them afterwards.
            foreach (SafeXmlElement page in pageElts)
            {
                // BL-9501 Don't remove pages from template books, which are often empty but we still want to show their components
                if (!book.IsTemplateBook)
                {
                    // As the constant's name here suggests, in theory, we could include divs
                    // that don't have .bloom-editable, and all their children.
                    // But I'm not smart enough to write that selector and for bloomds, all we're doing here is saving space,
                    // so those other divs we are missing doesn't seem to matter as far as I can think.
                    var kSelectThingsThatCanBeHiddenButAreNotText = ".//img";
                    var selector = removeInactiveLanguages
                        ? kSelectThingsThatCanBeHidden
                        : kSelectThingsThatCanBeHiddenButAreNotText;
                    foreach (SafeXmlElement elt in page.SafeSelectNodes(selector))
                    {
                        // Even when they are not displayed we want to keep image descriptions if they aren't empty.
                        // This is necessary for retaining any associated audio files to play.
                        // (If they are empty, they won't have any audio and may trigger embedding an unneeded font.)
                        // See https://issues.bloomlibrary.org/youtrack/issue/BL-7237.
                        // As noted above, if the displayDom is not sufficiently loaded for a definitive
                        // answer to IsDisplayed, we will throw when making epubs but not for bloom reader.
                        if (
                            !IsDisplayed(elt, epubMaker != null) && !IsNonEmptyImageDescription(elt)
                        )
                        {
                            toBeDeleted.Add(elt);
                        }
                    }

                    foreach (var elt in toBeDeleted)
                    {
                        elt.ParentNode.RemoveChild(elt);
                    }
                }
                // We need the font information for wanted text elements as well.  This is a side-effect but related to
                // unwanted elements in that we don't need fonts that are used only by unwanted elements.  Note that
                // elements don't need to be actually visible to provide computed style information such as font-family.
                foreach (SafeXmlElement elt in page.SafeSelectNodes(".//div"))
                {
                    StoreFontUsed(elt);
                }
                //Debug.WriteLine($"Removing {toBeDeleted.Count} elements from page");
                RemoveTempIds(page); // don't need temporary IDs any more.
                toBeDeleted.Clear();
            }
            var endRemoveTime = DateTime.Now;
            //Debug.WriteLine($"Fonts found: {String.Join(",", FontsUsed)}");
            Debug.WriteLine(
                $"RemoveUnwantedContentInternal took {(endRemoveTime - startRemoveTime).TotalMilliseconds} ms"
            );
        }

        public static void RemoveEnterpriseFeaturesIfNeeded(
            Book.Book book,
            List<SafeXmlElement> pageElts,
            ISet<string> warningMessages
        )
        {
            var omittedPages = RemoveEnterprisePagesIfNeeded(
                book.BookData,
                book.Storage.Dom,
                pageElts
            );
            if (omittedPages.Count > 0)
            {
                warningMessages.Add(
                    LocalizationManager.GetString(
                        "Publish.RemovingEnterprisePages",
                        "Removing one or more pages which require Bloom Enterprise to be enabled"
                    )
                );
                foreach (var label in omittedPages.Keys.OrderBy(x => x))
                    warningMessages.Add($"{omittedPages[label]} {label}");
            }
            if (!book.CollectionSettings.HaveEnterpriseFeatures)
                RemoveEnterpriseOnlyAssets(book);
        }

        /// <summary>
        /// Remove any Bloom Enterprise-only pages if Bloom Enterprise is not enabled.
        /// Also renumber the pages if any are removed.
        /// </summary>
        /// <returns>dictionary of types of pages removed and how many of each type (may be empty)</returns>
        public static Dictionary<string, int> RemoveEnterprisePagesIfNeeded(
            BookData bookData,
            HtmlDom dom,
            List<SafeXmlElement> pageElts
        )
        {
            var omittedPages = new Dictionary<string, int>();
            if (!bookData.CollectionSettings.HaveEnterpriseFeatures)
            {
                var pageRemoved = false;
                foreach (var page in pageElts.ToList())
                {
                    if (Book.Book.IsPageBloomEnterpriseOnly(page))
                    {
                        CollectPageLabel(page, omittedPages);
                        page.ParentNode.RemoveChild(page);
                        pageElts.Remove(page);
                        pageRemoved = true;
                    }
                }
                if (pageRemoved)
                {
                    dom.UpdatePageNumberAndSideClassOfPages(
                        bookData.CollectionSettings.CharactersForDigitsForPageNumbers,
                        bookData.Language1.IsRightToLeft
                    );
                }
            }
            return omittedPages;
        }

        private static void RemoveEnterpriseOnlyAssets(Book.Book book)
        {
            RobustFile.Delete(Path.Combine(book.FolderPath, kSimpleComprehensionQuizJs));
            RobustFile.Delete(Path.Combine(book.FolderPath, kVideoPlaceholderImageFile));
        }

        private bool IsDisplayed(SafeXmlElement elt, bool throwOnFailure)
        {
            var id = elt.GetAttribute("id");
            if (!_mapIdToDisplay.TryGetValue(id, out var display))
            {
                Debug.WriteLine("element not found in IsDisplayed()");
                if (throwOnFailure)
                {
                    throw new ApplicationException(
                        "Failure to completely load visibility document in RemoveUnwantedContent"
                    );
                }
            }
            return display != "none";
        }

        // store a set of font names encountered in displaying the book
        public HashSet<string> FontsUsed = new HashSet<string>();

        // map font names onto a set of language tags
        public Dictionary<string, HashSet<string>> FontsAndLangsUsed =
            new Dictionary<string, HashSet<string>>();

        /// <summary>
        /// Stores the font used.  Note that unwanted elements should have been removed already.
        /// </summary>
        /// <remarks>
        /// Elements that are made invisible by CSS still have their styles computed and can provide font information.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-11108 for a misunderstanding of this.
        /// </remarks>
        private void StoreFontUsed(SafeXmlElement elt)
        {
            var id = elt.GetAttribute("id");
            if (!_mapIdToFontFamily.TryGetValue(id, out var fontFamily))
                return; // Shouldn't happen, but ignore if it does.
            // we actually can get a comma-separated list with fallback font options: split into an array so we can use just the first one
            var fonts = fontFamily.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            // Fonts whose names contain spaces are quoted: remove the quotes.
            var font = fonts[0].Replace("\"", "");
            //Console.WriteLine("DEBUG PublishHelper.StoreFontUsed(): font=\"{0}\", fontFamily=\"{1}\"", font, fontFamily);
            FontsUsed.Add(font);
            // We need more information for font analytics.  This still suffers from the limitation on multiple languages being
            // uploaded or embedded in a BloomPub that are not actively displayed.  Nothing will be recorded for such languages.
            // But this is the best we can do without a lot of additional work.  (See BL-11512.)
            if (Program.RunningHarvesterMode)
            {
                var lang = elt.GetAttribute("lang");
                if (string.IsNullOrEmpty(lang) || lang == "z" || lang == "*")
                    return; // no language information
                if (!FontsAndLangsUsed.TryGetValue(font, out HashSet<string> langsForFont))
                {
                    langsForFont = new HashSet<string>();
                    FontsAndLangsUsed[font] = langsForFont;
                }
                langsForFont.Add(lang);
            }
        }

        private bool IsNonEmptyImageDescription(SafeXmlElement elt)
        {
            var classes = elt.GetAttribute("class");
            if (
                !String.IsNullOrEmpty(classes)
                && (
                    classes.Contains("ImageDescriptionEdit-style")
                    || classes.Contains("bloom-imageDescription")
                )
            )
            {
                return !String.IsNullOrWhiteSpace(elt.InnerText);
            }
            return false;
        }

        internal const string kTempIdMarker = "PublishTempIdXXYY";
        private static int s_count = 1;

        public static void EnsureAllThingsThatCanBeHiddenHaveIds(SafeXmlElement pageElt)
        {
            foreach (SafeXmlElement elt in pageElt.SafeSelectNodes(kSelectThingsThatCanBeHidden))
            {
                if (!string.IsNullOrEmpty(elt.GetAttribute("id")))
                    continue;
                elt.SetAttribute("id", kTempIdMarker + s_count++);
            }
        }

        public static void RemoveTempIds(SafeXmlElement pageElt)
        {
            foreach (SafeXmlElement elt in pageElt.SafeSelectNodes(kSelectThingsThatCanBeHidden))
            {
                var id = elt.GetAttribute("id");
                if (id != null && id.StartsWith(kTempIdMarker))
                    elt.RemoveAttribute("id");
            }
        }

        /// <summary>
        /// tempFolderPath is where to put the book. Note that a few files (e.g., customCollectionStyles.css)
        /// are copied into its parent in order to be in the expected location relative to the book,
        /// so that needs to be a folder we can write in.
        /// </summary>
        public static Book.Book MakeDeviceXmatterTempBook(
            string bookFolderPath,
            BookServer bookServer,
            string tempFolderPath,
            bool isTemplateBook,
            Dictionary<string, int> omittedPageLabels = null,
            bool includeVideoAndActivities = true,
            string[] narrationLanguages = null,
            bool wantMusic = false,
            bool wantFontFaceDeclarations = true
        )
        {
            var filter = new BookFileFilter(bookFolderPath)
            {
                IncludeFilesNeededForBloomPlayer = includeVideoAndActivities,
                WantVideo = includeVideoAndActivities,
                NarrationLanguages = narrationLanguages,
                WantMusic = true
            };
            filter.CopyBookFolderFiltered(tempFolderPath);
            var collectionStylesSource = Path.Combine(
                Path.GetDirectoryName(bookFolderPath),
                "customCollectionStyles.css"
            );
            var collectionStylesDest = Path.Combine(tempFolderPath, "customCollectionStyles.css");
            if (RobustFile.Exists(collectionStylesSource))
            {
                RobustFile.Copy(collectionStylesSource, collectionStylesDest, true);
            }
            else
            {
                RobustFile.Delete(collectionStylesDest);
            }
            // We can always save in a temp book
            var bookInfo = new BookInfo(tempFolderPath, true, new AlwaysEditSaveContext())
            {
                UseDeviceXMatter = !isTemplateBook
            };

            var modifiedBook = bookServer.GetBookFromBookInfo(bookInfo);
            // This book has to stand alone. If it needs a customCollectionStyles.css, it will have to use the one we just
            // copied into the actual book folder, not one in a parent folder.
            modifiedBook.Storage.LinkToLocalCollectionStyles = true;
            modifiedBook.WriteFontFaces = wantFontFaceDeclarations;
            modifiedBook.BringBookUpToDate(new NullProgress(), true);
            modifiedBook.RemoveNonPublishablePages(omittedPageLabels);
            var domForVideoProcessing = modifiedBook.OurHtmlDom;
            var videoContainerElements = HtmlDom
                .SelectChildVideoElements(domForVideoProcessing.RawDom.DocumentElement)
                .Cast<SafeXmlElement>();
            if (videoContainerElements.Any())
            {
                SignLanguageApi.ProcessVideos(videoContainerElements, modifiedBook.FolderPath);
            }
            modifiedBook.Save();
            modifiedBook.UpdateSupportFiles();
            return modifiedBook;
        }

        #region IDisposable Support
        // This code added to correctly implement the disposable pattern.
        private bool _isDisposed = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    if (_browser != null) // Don't use BrowserForPageChecks here...if we don't have one we don't want to make it now!
                    {
                        if (ControlForInvoke != null)
                        {
                            // Seems safest of all to invoke using the thing we use for all other invokes.
                            // Also, seems our WebView2Browser may not actually get a handle, yet its
                            // embedded WebView2 still needs to be disposed on the right thread.
                            ControlForInvoke.Invoke((Action)(() => _browser.Dispose()));
                        }
                        else if (_browser.IsHandleCreated)
                        {
                            _browser.Invoke((Action)(() => _browser.Dispose()));
                        }
                        else
                        {
                            // We can't invoke if it doesn't have a handle...and we certainly don't want
                            // to waste time getting it one...hopefully we can just dispose it on this
                            // thread.
                            _browser.Dispose();
                        }
                    }

                    _browser = null;
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion

        /// <summary>
        /// If the page element has a label, collect it into the page labels/count dictionary (if there is one;
        /// it might be null).
        /// </summary>
        public static void CollectPageLabel(
            SafeXmlElement pageElement,
            Dictionary<string, int> omittedPageLabels
        )
        {
            if (omittedPageLabels == null)
                return;
            var label = pageElement.SelectSingleNode(".//div[@class='pageLabel']")?.InnerText;
            if (!String.IsNullOrWhiteSpace(label))
            {
                if (omittedPageLabels.TryGetValue(label, out int count))
                    omittedPageLabels[label] = ++count;
                else
                    omittedPageLabels.Add(label, 1);
            }
            else
            {
                Console.WriteLine("DEBUG: no label found for page being omitted!");
            }
        }

        public static void SendBatchedWarningMessagesToProgress(
            ISet<string> warningMessages,
            IWebSocketProgress progress
        )
        {
            if (warningMessages.Any())
                progress.Message("Common.Warning", "Warning", ProgressKind.Warning, false);
            foreach (var warningMessage in warningMessages)
            {
                // Messages are already localized
                progress.MessageWithoutLocalizing(warningMessage, ProgressKind.Warning);
            }
        }

        // from bloomUI.less, @bloom-warning: #f3aa18;
        // WriteMessageWithColor doesn't work on Linux (the message is displayed in the normal black).
        static System.Drawing.Color _bloomWarning = System.Drawing.Color.FromArgb(
            0xFF,
            0xF3,
            0xAA,
            0x18
        );

        public static void SendBatchedWarningMessagesToProgress(
            ISet<string> warningMessages,
            IProgress progress
        )
        {
            if (warningMessages.Any())
            {
                var warning = L10NSharp.LocalizationManager.GetString("Common.Warning", "Warning");
                progress.WriteMessageWithColor(_bloomWarning.ToString(), "{0}", warning);
            }
            foreach (var warningMessage in warningMessages)
            {
                // Messages are already localized
                progress.WriteMessageWithColor(_bloomWarning.ToString(), "{0}", warningMessage);
            }
        }

        private static Dictionary<string, FontMetadata> _fontMetadataMap;

        internal static void ClearFontMetadataMapForTests()
        {
            _fontMetadataMap = null;
        }

        /// <summary>
        /// Checks the wanted fonts for being valid for  embedding, both for licensing and for the type of file
        /// (based on the filename extension).
        /// The list of rejected fonts is returned in badFonts and the list of files to copy for good fonts is
        /// returned in filesToEmbed.  Messages are written to the progress output as the processing goes along.
        /// </summary>
        /// <remarks>
        /// fontFileFinder must be either a new instance or a stub for testing.
        /// Setting fontFileFinder.NoteFontsWeCantInstall ensures that fontFileFinder.GetFilesForFont(font)
        /// will not return any files for fonts that we know cannot be embedded without reference to the
        /// license details.
        /// </remarks>
        public static void CheckFontsForEmbedding(
            IWebSocketProgress progress,
            HashSet<string> fontsWanted,
            IFontFinder fontFileFinder,
            out List<string> filesToEmbed,
            out HashSet<string> badFonts
        )
        {
            filesToEmbed = new List<string>();
            badFonts = new HashSet<string>();
            const string defaultFont = "Andika";

            fontFileFinder.NoteFontsWeCantInstall = true;
            if (_fontMetadataMap == null)
            {
                _fontMetadataMap = new Dictionary<string, FontMetadata>();
                foreach (var meta in FontsApi.AvailableFontMetadata)
                    _fontMetadataMap.Add(meta.name, meta);
            }
            foreach (var font in fontsWanted)
            {
                var fontFiles = fontFileFinder.GetFilesForFont(font);
                var filesFound = fontFiles.Any(); // unembeddable fonts determined don't have any files recorded
                var badLicense = false;
                var missingLicense = false;
                var badFileType = false;
                var fileExtension = "";
                if (_fontMetadataMap.TryGetValue(font, out var meta))
                {
                    fileExtension = meta.fileExtension;
                    switch (meta.determinedSuitability)
                    {
                        case FontMetadata.kUnsuitable:
                            badLicense = true;
                            missingLicense = false;
                            break;
                        case FontMetadata.kInvalid:
                            // We don't really know the values for badLicense and missingLicense, but they don't matter.
                            badFileType = true;
                            break;
                        case FontMetadata.kUnknown: // same as not finding the metadata for the font.
                            badLicense = false;
                            missingLicense = true;
                            break;
                        case FontMetadata.kOK:
                            badLicense = false;
                            missingLicense = false;
                            break;
                    }
                }
                else
                {
                    missingLicense = true;
                    // This is usually covered by the case kInvalid above, but needed if no metadata at all.
                    if (filesFound)
                    {
                        fileExtension = Path.GetExtension(fontFiles.First()).ToLowerInvariant();
                        badFileType = !FontMetadata.fontFileTypesBloomKnows.Contains(fileExtension);
                    }
                }
                if (filesFound && !badFileType && !badLicense)
                {
                    filesToEmbed.AddRange(fontFiles);
                    if (missingLicense)
                        progress.MessageWithParams(
                            "PublishTab.Android.File.Progress.UnknownLicense",
                            "{0} is a font name",
                            "Checking {0} font: Unknown license",
                            ProgressKind.Progress,
                            font
                        );
                    else
                        progress.MessageWithParams(
                            "PublishTab.Android.File.Progress.CheckFontOK",
                            "{0} is a font name",
                            "Checking {0} font: License OK for embedding.",
                            ProgressKind.Progress,
                            font
                        );
                    // Assumes only one font file per font; if we embed multiple font files, will need to enhance this.
                    var size = new FileInfo(fontFiles.First()).Length;
                    var sizeToReport = (size / 1000000.0).ToString("F2"); // purposely locale-specific; might be e.g. 1,2
                    progress.MessageWithParams(
                        "PublishTab.Android.File.Progress.Embedding",
                        "{1} is a number with two decimal places, the number of megabytes the font file takes up",
                        "Embedding font {0} at a cost of {1} megs",
                        ProgressKind.Note,
                        font,
                        sizeToReport
                    );
                    continue;
                }
                // If the missing font is Andika New Basic, don't complain because Andika subsumes Andika New Basic,
                // and will be automatically substituted for it.
                var dontComplain = font == "Andika New Basic";
                if (badFileType)
                {
                    progress.MessageWithParams(
                        "PublishTab.Android.File.Progress.IncompatibleFontFileFormat",
                        "{0} is a font name, {1} is a file extension (for example: .ttc)",
                        "This book has text in a font named \"{0}\". Bloom cannot publish this font's format ({1}).",
                        ProgressKind.Error,
                        font,
                        fileExtension
                    );
                    progress.Message(
                            "PublishTab.FontProblem.CheckInBookSettingsDialog",
                            "Check the Fonts section of the Book Settings dialog to locate this font.",
                            ProgressKind.Error);
                }
                else if (fontFileFinder.FontsWeCantInstall.Contains(font) || badLicense)
                {
                    progress.MessageWithParams(
                        "PublishTab.Android.File.Progress.LicenseForbids",
                        "{0} is a font name",
                        "This book has text in a font named \"{0}\". The license for \"{0}\" does not permit Bloom to embed the font in the book.",
                        ProgressKind.Error,
                        font
                    );
                    progress.Message(
                            "PublishTab.FontProblem.CheckInBookSettingsDialog",
                            "Check the Fonts section of the Book Settings dialog to locate this font.",
                            ProgressKind.Error);
                }
                else if (!dontComplain)
                {
                    progress.MessageWithParams(
                        "PublishTab.Android.File.Progress.NoFontFound",
                        "{0} is a font name",
                        "This book has text in a font named \"{0}\", but Bloom could not find that font on this computer.",
                        ProgressKind.Error,
                        font
                    );
                    progress.Message(
                            "PublishTab.FontProblem.CheckInBookSettingsDialog",
                            "Check the Fonts section of the Book Settings dialog to locate this font.",
                            ProgressKind.Error);
                }
                if (!dontComplain)
                    progress.MessageWithParams(
                        "PublishTab.Android.File.Progress.SubstitutingAndika",
                        "{0} is a font name",
                        "Bloom will substitute \"{0}\" instead.",
                        ProgressKind.Error,
                        defaultFont,
                        font
                    );
                badFonts.Add(font); // need to prevent the bad/missing font from showing up in fonts.css and elsewhere
            }
        }

        /// <summary>
        /// Fix the standard CSS files to replace any fonts listed in badFonts with the defaultFont value.
        /// </summary>
        public static void FixCssReferencesForBadFonts(
            string cssFolderPath,
            string defaultFont,
            HashSet<string> badFonts
        )
        {
            // Note that the font may be referred to in defaultLangStyles.css, in customCollectionStyles.css, or in a style defined in the HTML.
            // This method handles the .css files.
            var defaultLangStyles = Path.Combine(cssFolderPath, "defaultLangStyles.css");
            if (RobustFile.Exists(defaultLangStyles))
            {
                var cssTextOrig = RobustFile.ReadAllText(defaultLangStyles);
                var cssText = cssTextOrig;
                foreach (var font in badFonts)
                {
                    var cssRegex = new System.Text.RegularExpressions.Regex(
                        $"font-family:\\s*'?{font}'?;"
                    );
                    cssText = cssRegex.Replace(cssText, $"font-family: '{defaultFont}';");
                }
                if (cssText != cssTextOrig)
                    RobustFile.WriteAllText(defaultLangStyles, cssText);
            }
            var customCollectionStyles = Path.Combine(cssFolderPath, "customCollectionStyles.css");
            if (RobustFile.Exists(customCollectionStyles))
            {
                var cssTextOrig = RobustFile.ReadAllText(customCollectionStyles);
                var cssText = cssTextOrig;
                foreach (var font in badFonts)
                {
                    var cssRegex = new System.Text.RegularExpressions.Regex(
                        $"font-family:\\s*'?{font}'?;"
                    );
                    cssText = cssRegex.Replace(cssText, $"font-family: '{defaultFont}';");
                }
                if (cssText != cssTextOrig)
                    RobustFile.WriteAllText(customCollectionStyles, cssText);
            }
        }

        /// <summary>
        /// Fix the userModifiedStyles in the HTML DOM to replace any fonts listed in badFonts with the defaultFont
        /// value.  Note that ePUB uses namespaces in its XHTML files while BloomPub does not use namespaces.
        /// </summary>
        /// <returns><c>true</c> if any references for bad fonts were fixed, <c>false</c> otherwise.</returns>
        public static bool FixXmlDomReferencesForBadFonts(
            SafeXmlDocument bookDoc,
            string defaultFont,
            HashSet<string> badFonts,
            XmlNamespaceManager nsmgr = null,
            string nsPrefix = ""
        ) // these two arguments needed for processing ePUB files.
        {
            // Now for styles defined in the dom...
            var xpath =
                $"//{nsPrefix}head/{nsPrefix}style[@type='text/css' and @title='userModifiedStyles']";
            var userStylesNode =
                nsmgr == null
                    ? bookDoc.FirstChild.SelectSingleNode(xpath)
                    : bookDoc.FirstChild.SelectSingleNode(xpath, nsmgr);
            if (
                userStylesNode != null
                && !String.IsNullOrEmpty(userStylesNode.InnerXml)
                && userStylesNode.InnerXml.Contains("font-family:")
            )
            {
                var cssTextOrig = userStylesNode.InnerXml; // InnerXml needed to preserve CDATA markup
                var cssText = cssTextOrig;
                foreach (var font in badFonts)
                {
                    var cssRegex = new System.Text.RegularExpressions.Regex(
                        $"font-family:\\s*{font}\\s*!\\s*important;"
                    );
                    cssText = cssRegex.Replace(cssText, $"font-family: {defaultFont} !important;");
                }
                if (cssText != cssTextOrig)
                {
                    userStylesNode.InnerXml = cssText;
                    return true;
                }
            }
            return false;
        }

        public static void ReportInvalidFonts(string destDirName, IProgress progress)
        {
            // For ePUB and BloomPub, we display the book to determine exactly which fonts are
            // actually used.  We don't have a browser available to do that for uploads, so we scan
            // css files and the styles set in the html file to see what font-family values are present.
            // There's also the question of multilanguage books having data that isn't actively
            // displayed but could potentially be displayed.
            HashSet<string> fontsFound = new HashSet<string>();
            foreach (var filepath in Directory.EnumerateFiles(destDirName, "*.css"))
            {
                var cssContent = RobustFile.ReadAllText(filepath);
                HtmlDom.FindFontsUsedInCss(cssContent, fontsFound, includeFallbackFonts: true);
            }
            // There should be only one html file with the same name as the directory it's in, but let's
            // not make any assumptions here.
            foreach (var filepath in Directory.EnumerateFiles(destDirName, "*.htm"))
            {
                var cssContent = RobustFile.ReadAllText(filepath);
                HtmlDom.FindFontsUsedInCss(cssContent, fontsFound, includeFallbackFonts: true); // works on HTML files as well
            }
            if (_fontMetadataMap == null)
            {
                _fontMetadataMap = new Dictionary<string, FontMetadata>();
                foreach (var meta in FontsApi.AvailableFontMetadata)
                    _fontMetadataMap.Add(meta.name, meta);
            }
            var cssGenericFonts = new HashSet<string>
            {
                "serif",
                "sans-serif",
                "cursive",
                "fantasy",
                "monospace"
            };
            foreach (var font in fontsFound)
            {
                if (cssGenericFonts.Contains(font.ToLowerInvariant()))
                    continue;
                if (_fontMetadataMap.TryGetValue(font, out var meta))
                {
                    string msg2 = null;
                    switch (meta.determinedSuitability)
                    {
                        case FontMetadata.kOK:
                            break;
                        case FontMetadata.kUnknown:
                            //progress.WriteWarning("This book has a font, \"{0}\", which has an unknown license.", font);
                            break;
                        case FontMetadata.kUnsuitable:
                            msg2 = LocalizationManager.GetString(
                                "PublishTab.FontProblem.License",
                                "The metadata inside this font tells us that it may not be embedded for free in ebooks and the web."
                            );
                            break;
                        case FontMetadata.kInvalid:
                            if (meta.determinedSuitabilityNotes.Contains("exception"))
                                msg2 = LocalizationManager.GetString(
                                    "PublishTab.FontProblem.Exception",
                                    "The font's file cannot be processed by Bloom and may be corrupted or not a font file."
                                );
                            else
                                msg2 = String.Format(
                                    LocalizationManager.GetString(
                                        "PublishTab.FontProblem.Format",
                                        "Bloom cannot publish ePUBs and BloomPubs with this font's format ({0})."
                                    ),
                                    meta.fileExtension
                                );
                            break;
                    }
                    if (msg2 != null)
                    {
                        var msgFmt1 = LocalizationManager.GetString(
                            "PublishTab.FontProblem",
                            "This book has a font, \"{0}\", which has the following problem:"
                        );
                        var msg3 = LocalizationManager.GetString(
                            "PublishTab.FontProblem.Result",
                            "BloomLibrary.org will display the PDF and allow downloads for translation, but cannot offer the �READ� button or downloads for BloomPUB or ePUB."
                        );
                        var msg4 = LocalizationManager.GetString(
                            "PublishTab.FontProblem.CheckInBookSettingsDialog",
                            "Check the Fonts section of the Book Settings dialog to locate this font."
                        );
                        // progress.WriteError() uses Color.Red, but also exposes a link to "report error" which we don't want here.
                        progress.WriteMessageWithColor("Red", msgFmt1, font);
                        progress.WriteMessageWithColor("Red", " \u2022 {0}", msg2);
                        progress.WriteMessageWithColor("Red", " \u2022 {0}", msg3);
                        progress.WriteMessageWithColor("Red", " \u2022 {0}", msg4);
                    }
                }
                else
                {
                    //progress.WriteWarning("This book has a font, \"{0}\", which is not on this computer and whose license is unknown.", font);
                }
            }
        }
    }
}
