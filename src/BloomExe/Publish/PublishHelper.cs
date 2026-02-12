using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.FontProcessing;
using Bloom.ImageProcessing;
using Bloom.Publish.Epub;
using Bloom.SafeXml;
using Bloom.SubscriptionAndFeatures;
using Bloom.web;
using Bloom.web.controllers;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.Publish
{
    public class PublishHelper : IDisposable
    {
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
            Dictionary<string, int> omittedPages,
            ISet<string> modifiedPageMessages,
            PublishingMediums medium, // should normally only be one of them
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
                                omittedPages,
                                modifiedPageMessages,
                                medium,
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
                    omittedPages,
                    modifiedPageMessages,
                    medium,
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
        /// If the element's text is empty, then the font information should be ignored.
        /// </remarks>
        public const string GetElementDisplayAndFontInfoJavascript =
            @"(() =>
{
    const elementsInfo = [];
    const elementsWithId = document.querySelectorAll(""[id], em, strong"");
    elementsWithId.forEach(elt => {
        const hasText = !!elt.innerText;
        const style = getComputedStyle(elt, null);
        if (style) {
            if (hasText) {
                elementsInfo.push({
                    id: elt.id,
                    display: style.display,
                    fontFamily: style.getPropertyValue(""font-family""),
                    fontStyle: style.getPropertyValue(""font-style""),
                    fontWeight: style.getPropertyValue(""font-weight"")
                });
            }
            else {
                elementsInfo.push({
                    id: elt.id,
                    display: style.display,
                    fontFamily: """",
                    fontStyle: """",
                    fontWeight: """"
                });
            }
        }
    });
    return { results: elementsInfo };
})();";

        // A bit of JavaScript we can run, after hacking the DOM to make all languages visible
        // in bloom-editables, to get all the font families that are needed to render it in
        // any of the included languages.
        // Review: what elements should we retrieve font data for? I don't want to just use *,
        // because there are many elements that don't hold text (or only children with different
        // font settings hold text), and we don't care what font they may be set to use.
        // OTOH, it's just possible that there are elements that are not our expected Bloom
        // multilingual fields that do contain text that uses a problem font. We ought to be
        // able to assume that Branding data has suitable fonts. Is there anything else we
        // might want to include?
        // Note: it would be something of an optimization to make this code return a set
        // of the first font in each family, since that's all we really want. However, this
        // bit of Javascript is completely outside our build process; there's no compile time
        // checking, no access to libraries, no help at all getting it right. I want to keep
        // it as simple as will possibly do the job. And I don't think the performance cost
        // of passing more results back will be significant. (Moreover, we already had the
        // quite tricky code to extract the first font family in C#.)
        public const string GetElementFontFamilyInfoJavascript =
            @"(() =>
{
	const elementsInfo = [];
	document.querySelectorAll(""textarea, .bloom-editable *"").forEach(elt => {
		const style = getComputedStyle(elt, null);
		if (style) {
			elementsInfo.push(style.getPropertyValue(""font-family""));
		}
	});
	return elementsInfo;
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
            public string fontStyle;
            public string fontWeight;
        }

        public class FontInfo
        {
            public string fontFamily;
            public string fontStyle;
            public string fontWeight;

            public override string ToString()
            {
                if (string.IsNullOrEmpty(fontStyle) && string.IsNullOrEmpty(fontWeight))
                    return string.IsNullOrEmpty(fontFamily)
                        ? "<uninitialized FontInfo>"
                        : fontFamily;
                if (fontStyle == "normal" && fontWeight == "400")
                    return string.IsNullOrEmpty(fontFamily)
                        ? "<uninitialized FontInfo>"
                        : fontFamily;
                if (fontStyle == "normal" && fontWeight == "700")
                    return $"{fontFamily} Bold";
                if (fontStyle == "italic" && fontWeight == "400")
                    return $"{fontFamily} Italic";
                if (fontStyle == "italic" && fontWeight == "700")
                    return $"{fontFamily} Bold Italic";
                return $"{fontFamily} weight=\"{fontWeight}\" style=\"{fontStyle}\"";
            }

            public override bool Equals(object obj)
            {
                var that = obj as FontInfo;
                if (that == null)
                    return false;
                return this.fontFamily == that.fontFamily
                    && this.fontStyle == that.fontStyle
                    && this.fontWeight == that.fontWeight;
            }

            public override int GetHashCode()
            {
                return fontFamily.GetHashCode()
                    ^ fontStyle.GetHashCode()
                    ^ fontWeight.GetHashCode();
            }

            public static bool operator ==(FontInfo info1, FontInfo info2)
            {
                if (ReferenceEquals(info1, info2))
                    return true;
                if (ReferenceEquals(info1, null) || ReferenceEquals(info2, null))
                    return false;
                return info1.Equals(info2);
            }

            public static bool operator !=(FontInfo info1, FontInfo info2)
            {
                return !(info1 == info2);
            }
        }

        protected Dictionary<string, string> _mapIdToDisplay = new Dictionary<string, string>();
        protected Dictionary<string, FontInfo> _mapIdToFontInfo =
            new Dictionary<string, FontInfo>();

        private void RemoveUnwantedContentInternal(
            HtmlDom dom,
            Book.Book book,
            bool removeInactiveLanguages,
            EpubMaker epubMaker,
            Dictionary<string, int> omittedPages,
            ISet<string> modifiedPageMessages,
            PublishingMediums medium, // should normally only be one of them
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
                // The epub maker calls this function separately for each page,
                // and each call passes a dom where a single page is the direct child of the body.
                pageElts.Add((SafeXmlElement)dom.Body.FirstChild);
            }
            else
            {
                foreach (SafeXmlElement page in dom.GetPageElements())
                    pageElts.Add(page);
            }

            RemovePagesByFeatureSystem(book, dom, pageElts, medium, omittedPages);
            RemoveAssetsWhichRequireSubscription(book);
            RemoveClassesAndAttrsToDisableFeatures(
                pageElts,
                book.CollectionSettings.Subscription,
                book.BookData.BookIsDerivative(),
                modifiedPageMessages,
                book
            );
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
                if (String.IsNullOrEmpty(src) || ImageUtils.IsPlaceholderImageFilename(src))
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
                    var div in dom
                        .Body.SafeSelectNodes(
                            "//div[contains(@class, 'split-pane-component-inner')]"
                        )
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
                var div in dom
                    .Body.SafeSelectNodes("//*[@data-cke-hidden-sel]")
                    .Cast<SafeXmlElement>()
            )
            {
                div.ParentNode.RemoveChild(div);
            }
            // Remove any AI generated data that snuck in to the data div or book info. (BL-14339)
            RemoveUnpublishableBookData(dom.RawDom);
            if (epubMaker == null) // not needed/wanted for publishing epubs
                RemoveUnpublishableBookInfo(book.BookInfo);

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
                    if (string.IsNullOrEmpty(info.id))
                    {
                        // Presumably in a <strong> or <em> tag.
                        if (info.display != "none" && !string.IsNullOrEmpty(info.fontFamily))
                        {
                            var font = ExtractFontNameFromFontFamily(info.fontFamily);
                            FontsUsed.Add(
                                new FontInfo
                                {
                                    fontFamily = font,
                                    fontStyle = info.fontStyle,
                                    fontWeight = info.fontWeight,
                                }
                            );
                        }
                        continue;
                    }
                    _mapIdToDisplay[info.id] = info.display;
                    if (string.IsNullOrEmpty(info.fontFamily))
                        continue;
                    var fontInfo = new FontInfo
                    {
                        fontFamily = info.fontFamily,
                        fontStyle = info.fontStyle,
                        fontWeight = info.fontWeight,
                    };
                    _mapIdToFontInfo[info.id] = fontInfo;
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
                    toBeDeleted.Clear();
                }
                // Remove any content that was generated by AI. (BL-14339)
                RemoveUnpublishableContent(page);
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

        /// <summary>
        /// Typically called once after one or a sequence of calls to RemovePagesbyFeatureSystem,
        /// generates any appropriate messages.
        /// </summary>
        /// <param name="omittedPages"></param>
        public static List<string> MakePagesRemovedWarnings(Dictionary<string, int> omittedPages)
        {
            var warningMessages = new List<string>();
            if (omittedPages.Count > 0)
            {
                warningMessages.Add(
                    LocalizationManager.GetString(
                        "Publish.RemovingDisallowedPages",
                        "Removing one or more pages which require a higher subscription tier"
                    )
                );
                foreach (var label in omittedPages.Keys.OrderBy(x => x))
                    warningMessages.Add($"{omittedPages[label]} {label}");
            }

            return warningMessages;
        }

        /// <summary>
        /// Remove any pages that violate the current subscription for this type of book (derivative or original),
        /// or which are inappropriate for the intended publishing medium.
        /// Also renumber the pages if any are removed.
        /// Accumulates into omittedPages information about how many pages of each label were removed.
        /// </summary>
        public static void RemovePagesByFeatureSystem(
            Book.Book book,
            // Usually book.OurHtmlDom, but Book.GetDomForPrinting makes a new dom without making a
            // new book, and it's that dom we want to modify.
            HtmlDom dom,
            // usually dom.GetPageElements(), but EpubMaker calls this repeatedly with a DOM for each
            // individual page
            List<SafeXmlElement> pageElts,
            PublishingMediums medium, // should normally only be one of them
            Dictionary<string, int> omittedPages
        )
        {
            var bookData = book.BookData;
            var charactersForDigits = bookData.CollectionSettings.CharactersForDigitsForPageNumbers;
            var language1IsRightToLeft = bookData.Language1.IsRightToLeft;
            var isDerivative = bookData.BookIsDerivative();
            var subscription = book.CollectionSettings.Subscription;
            if (book.IsPlayground)
                subscription = Subscription.CreateTempSubscriptionForTier(
                    SubscriptionTier.Enterprise
                );
            RemovePagesByFeatureSystem(
                pageElts,
                medium,
                omittedPages,
                subscription,
                isDerivative,
                charactersForDigits,
                language1IsRightToLeft
            );
        }

        // This version is easier to test
        internal static void RemovePagesByFeatureSystem(
            List<SafeXmlElement> pageElts,
            PublishingMediums medium,
            // The string is a page label, and the int is a count of how many pages with that label have
            // been removed. This can be accumulated over multiple calls to this method.
            Dictionary<string, int> omittedPages,
            Subscription subscription,
            bool isDerivative,
            string charactersForDigits,
            bool language1IsRightToLeft
        )
        {
            var pageRemoved = false;
            foreach (var page in pageElts.ToList()) // make a copy because we will modify the original
            {
                var reasonForRemoval = FeatureStatus.ShouldPageBeRemovedForPublishing(
                    page,
                    subscription,
                    isDerivative,
                    medium
                );
                if (reasonForRemoval != FeatureStatus.ReasonForRemoval.None)
                {
                    if (reasonForRemoval == FeatureStatus.ReasonForRemoval.InsufficientSubscription)
                        CollectPageLabel(page, omittedPages);
                    // Enhance: if we want to report removal because of unsupported medium,
                    // we could add another dictionary to track those.
                    page.ParentNode.RemoveChild(page);
                    pageElts.Remove(page);
                    pageRemoved = true;
                }
            }
            if (pageRemoved)
            {
                // This won't help with Epubs, because the dom we get only has the one page.
                HtmlDom.UpdatePageNumberAndSideClassOfPages(
                    charactersForDigits,
                    language1IsRightToLeft,
                    pageElts
                );
            }
        }

        internal static void RemoveClassesAndAttrsToDisableFeatures(
            List<SafeXmlElement> pageElts,
            Subscription subscription,
            bool forDerivative,
            // Each item is a message like "The feature 'Background music' was removed from this book because it requires a higher subscription tier".
            // We use a set because order is not very important but we don't want to repeat for multiple pages with the same feature,
            // even across multiple calls to this method.
            ISet<string> messages,
            Book.Book book = null
        )
        {
            var disabledRemoveByModifyingFeatures = FeatureStatus.GetFeaturesToDisableUsingMethod(
                subscription,
                forDerivative,
                PreventionMethod.DisabledByModifyingDom,
                book
            );
            foreach (var page in pageElts)
            {
                foreach (var feature in disabledRemoveByModifyingFeatures)
                {
                    if (page.SafeSelectNodes(feature.ExistsInPageXPath).Length > 0)
                    {
                        if (feature.ClassesToRemoveToDisable != null)
                        {
                            // Remove the classes that would make this feature work.
                            foreach (var className in feature.ClassesToRemoveToDisable.Split())
                                page.RemoveClass(className);
                        }

                        if (feature.AttributesToRemoveToDisable != null)
                        {
                            // Remove the attributes that would make this feature work.
                            foreach (var attr in feature.AttributesToRemoveToDisable.Split())
                                page.RemoveAttribute(attr);
                        }
                        var messageFrame = LocalizationManager.GetString(
                            "PublishTab.FeatureRequiresHigherTier",
                            "The feature \"{0}\" was removed from this book because it requires a higher subscription tier",
                            ""
                        );
                        var featureDescription = I18NApi.GetTranslation(feature.L10NId);
                        var message = string.Format(messageFrame, featureDescription);
                        messages.Add(message);
                    }
                }
            }
        }

        // Not really sure what we need here; embedding the simple comprehension quiz JS has been
        // obsolete in all situations, but there may be some old version of Bloom Player
        // that needs it. But for sure we don't need it if the game feature is disabled and thus
        // there are no games in the publication.
        // The old code did not distinguish derivatives, but we're moving more in the
        // direction of allowing most things to be published in derivatives, so I decided
        // to err on the side of safety by NOT deleting these assets when publishing a derivative.
        private static void RemoveAssetsWhichRequireSubscription(Book.Book book)
        {
            if (!book.BookData.BookIsDerivative())
            {
                if (
                    !FeatureStatus
                        .GetFeatureStatus(book.CollectionSettings.Subscription, FeatureName.Game)
                        .Enabled
                )
                    RobustFile.Delete(Path.Combine(book.FolderPath, "simpleComprehensionQuiz.js"));
            }
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

        // store a set of font names, styles, and weights encountered in displaying the book
        public HashSet<FontInfo> FontsUsed = new HashSet<FontInfo>();

        // map font names onto a set of language tags
        public Dictionary<string, HashSet<string>> FontsAndLangsUsed =
            new Dictionary<string, HashSet<string>>();

        private static string ExtractFontNameFromFontFamily(string fontFamily)
        {
            // we actually can get a comma-separated list with fallback font options: split into an array so we can
            // use just the first one.  We don't need to worry about the fallback fonts, just the primary one.  It
            // would be very unusual for the user not to have the primary font installed, but to have a fallback font
            // installed instead that is always used.
            var fonts = fontFamily.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            // Fonts whose names contain spaces are quoted: remove the quotes.
            return fonts[0].Replace("\"", "");
        }

        /// <summary>
        /// Stores the font used.  Note that unwanted elements should have been removed already.
        /// </summary>
        /// <remarks>
        /// Elements that are made invisible by CSS still have their styles computed and can provide font information.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-11108 for a misunderstanding of this.
        /// </remarks>
        protected void StoreFontUsed(SafeXmlElement elt)
        {
            var id = elt.GetAttribute("id");
            // If it's not displayed, and there's nothing to display, ignore it.
            // This may not be perfect, but it's a reasonable heuristic.  It may let fonts
            // through that aren't actually used, but it's better than not recording fonts
            // that are used in multilingual books where some languages are not currently
            // displayed. (BL-14267)
            if (
                _mapIdToDisplay.TryGetValue(id, out var display)
                && display == "none"
                && elt.InnerText.Trim() == ""
            )
            {
                return;
            }
            // Now that we know the element is displayed, or at least has text that can be
            // displayed, get the font information for the element.
            if (!_mapIdToFontInfo.TryGetValue(id, out var fontInfo))
                return; // Shouldn't happen, but ignore if it does.

            // If the fontInfo.fontFamily contains a list of fallback fonts, we want only the
            // primary font.  Matters are already complex enough without worrying about whether
            // a fallback font is actually used.
            var font = ExtractFontNameFromFontFamily(fontInfo.fontFamily);
            // The default font-family established in basePage-sharedRules.less starts off
            // with our default font ("Andika") and then adds a bunch of fallback fonts.  We don't
            // need to record that "Andika" is used in this case, at least not when it isn't used
            // for actual text.  This helps to avoid embedding the default font in epubs where it
            // isn't actually used. (BL-14267)
            if (
                font == DefaultFont
                && font != fontInfo.fontFamily // we have fall-back fonts, likely the default set
                && !IsRealTextDisplayedInDesiredFont(
                    elt,
                    new FontInfo // we don't care about the fall-back fonts, just the primary one
                    {
                        fontFamily = font,
                        fontStyle = fontInfo.fontStyle,
                        fontWeight = fontInfo.fontWeight,
                    }
                )
            )
            {
                return;
            }

            FontsUsed.Add(
                new FontInfo
                {
                    fontFamily = font,
                    fontStyle = fontInfo.fontStyle,
                    fontWeight = fontInfo.fontWeight,
                }
            );
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

        /// <summary>
        /// Check whether actual text inside this element has been recorded as being in the desired font.
        /// If the current font is not specified, it is assumed to be the desired font.
        /// </summary>
        protected bool IsRealTextDisplayedInDesiredFont(
            SafeXmlElement element,
            FontInfo desiredFont,
            FontInfo currentFont = null
        )
        {
            // If there's no non-whitespace text in the element, nothing is displayed in the font.
            if (element.InnerText.Trim() == "")
                return false;
            // If the current font is not specified, assume that it's the desired font.
            if (currentFont == null)
                currentFont = desiredFont;
            foreach (var node in element.ChildNodes)
            {
                var trimmedInnerText = node.InnerText.Trim();
                if (trimmedInnerText == "")
                    continue; // If there's no text to display, we don't need to check further.
                if (node.NodeType == XmlNodeType.Text)
                {
                    // We have a text node that's not empty after trimming for whitespace.  Therefore
                    // we have text displayed in the current font, which might be the desired font.
                    if (currentFont == desiredFont)
                        return true;
                }
                else if (node.NodeType == XmlNodeType.Element)
                {
                    var id = ((SafeXmlElement)node).GetAttribute("id");
                    if (
                        String.IsNullOrEmpty(id) // no id, assume same font
                        || !_mapIdToFontInfo.TryGetValue(id, out var newCurrentFont)
                    )
                    {
                        // If there's no new font information, assume the current font is still
                        // in use and check the element recursively.
                        if (
                            IsRealTextDisplayedInDesiredFont(
                                (SafeXmlElement)node,
                                desiredFont,
                                currentFont
                            )
                        )
                            return true;
                    }
                    else
                    {
                        // Check the node recursively with the new font information.
                        var font1 = ExtractFontNameFromFontFamily(newCurrentFont.fontFamily);
                        // restrict to using only the first font in a list of fallback fonts
                        if (font1 != newCurrentFont.fontFamily)
                            newCurrentFont = new FontInfo
                            {
                                fontFamily = font1,
                                fontStyle = newCurrentFont.fontStyle,
                                fontWeight = newCurrentFont.fontWeight,
                            };
                        if (
                            IsRealTextDisplayedInDesiredFont(
                                (SafeXmlElement)node,
                                desiredFont,
                                newCurrentFont
                            )
                        )
                            return true;
                    }
                }
            }
            return false;
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
            bool wantFontFaceDeclarations = true,
            bool processVideos = true
        )
        {
            var filter = new BookFileFilter(bookFolderPath)
            {
                IncludeFilesNeededForBloomPlayer = includeVideoAndActivities,
                WantVideo = includeVideoAndActivities,
                NarrationLanguages = narrationLanguages,
                WantMusic = true,
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
            // The file customCollectionStyles.css has already been copied to the book folder if we
            // are running in harvester mode, and we don't want to delete it in that case.
            else if (!Program.RunningHarvesterMode)
            {
                RobustFile.Delete(collectionStylesDest);
            }
            // We can always save in a temp book
            var bookInfo = new BookInfo(tempFolderPath, true, new AlwaysEditSaveContext())
            {
                UseDeviceXMatter = !isTemplateBook,
            };

            var modifiedBook = bookServer.GetBookFromBookInfo(bookInfo);
            // This book has to stand alone. If it needs a customCollectionStyles.css, it will have to use the one we just
            // copied into the actual book folder, not one in a parent folder.
            modifiedBook.Storage.LinkToLocalCollectionStyles = true;
            modifiedBook.WriteFontFaces = wantFontFaceDeclarations;
            modifiedBook.BringBookUpToDate(new NullProgress(), true);
            modifiedBook.RemoveNonPublishablePages(omittedPageLabels);
            if (processVideos)
            {
                var domForVideoProcessing = modifiedBook.OurHtmlDom;
                var videoContainerElements = HtmlDom
                    .SelectChildVideoElements(domForVideoProcessing.RawDom.DocumentElement)
                    .Cast<SafeXmlElement>();
                if (videoContainerElements.Any())
                {
                    SignLanguageApi.ProcessVideos(videoContainerElements, modifiedBook.FolderPath);
                }
            }
            ImageUtils.ReallyCropImages(
                modifiedBook.RawDom,
                modifiedBook.FolderPath,
                modifiedBook.FolderPath
            );
            // Must come after ReallyCropImages, because any cropping for background images is
            // destroyed by SimplifyBackgroundImages.
            SimplifyBackgroundImages(modifiedBook.RawDom);
            // We don't need a data-div in a publication; save some space.
            var dataDiv = modifiedBook.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']");
            if (dataDiv != null)
            {
                dataDiv.ParentElement.RemoveChild(dataDiv);
            }
            modifiedBook.Save(true);
            modifiedBook.UpdateSupportFiles();
            return modifiedBook;
        }

        /// <summary>
        /// Once we have really cropped any images that need it, we no longer need the "canvas element-like"
        /// HTML structure that supports cropping background images. So we get rid of the extra structure
        /// and leave the export with just an img that properly fills the bloom-canvas (using CSS
        /// content-fit). The great advantage of this is that we need code to adjust the size of the
        /// canvas element, but the img size is automatic with content-fit. While editing, we adjust the
        /// size and position of things in a bloom-canvas, including the background canvas element, any time
        /// we open a page and notice that the bloom-canvas's size has changed (or when it changes while
        /// the page is open). But various things can change the bloom-canvas size for all images
        /// in a document, and the user shouldn't have to cycle through all the pages to get the
        /// images adjusted. (We decided to accept that they must do so for ordinary canvas elements). If we didn't do
        /// this trick, we'd have to somehow adjust any background image canvas element (basically any image
        /// that has migrated to the new system) to the correct size for the current size of its
        /// container, and enhance Bloom Player to do the same when it changes the page size, and not
        /// to give books with only background canvas elements the special treatment that it gives books with
        /// ordinary canvas elements, and to handle motion differently. And it would be difficult to make the
        /// correct adjustment in C#; the resize code is all in JS and makes use of the browser's
        /// rendering of the page. One day, we'd like to do all the publishing transformations in JS
        /// (https://www.notion.so/hattonjohn/Infrastructure-publishing-transformations-1514bb19df128007a15ffd4cafff28de?pvs=4)
        /// but for now this saves a lot of work. It essentially converts the background image
        /// represented as a canvas element to our pre-6.2 approach where it was a direct child of the bloom canvas.
        /// The canvas element div is removed, and the img that was in it is moved to an appropriate position
        /// as a direct child of the bloom-canvas.
        /// We also do this when publishing to BloomLibrary; this saves the harvester needing to worry about
        /// the background image canvas elements, and also saves our future code having to worry about blorg
        /// books that have a mixture of old and new background images.
        /// </summary>
        public static void SimplifyBackgroundImages(SafeXmlDocument dom)
        {
            var backgroundImageCanvasElements = dom.SafeSelectNodes(
                    "//div[contains(@class, 'bloom-backgroundImage')]"
                )
                .Cast<SafeXmlElement>()
                .ToArray();
            foreach (var canvasElement in backgroundImageCanvasElements)
            {
                // Most of these early continues are paranoia. If we find a div with class bloom-backgroundImage,
                // on a page, it WILL contain an image container that contains an img, and WILL be a child of
                // a bloom-canvas. So the xpath above targets exactly
                // the elements that need this transformation. The backgroundImage elements ("background
                // canvas elements") are removed, and the img is moved to the bloom-canvas.
                // One exception that has occurred is in an old, migrated book where an element in the data-div
                // had this class, but only the image file name as content.
                var imgContainer =
                    canvasElement.ChildNodes.FirstOrDefault(c =>
                        c is SafeXmlElement ce && ce.LocalName == "div"
                    ) as SafeXmlElement;
                if (imgContainer == null || !imgContainer.HasClass("bloom-imageContainer"))
                    continue;
                var img =
                    imgContainer.ChildNodes.FirstOrDefault(c =>
                        c is SafeXmlElement ce && ce.LocalName == "img"
                    ) as SafeXmlElement;
                if (img == null)
                    continue;
                var src = img.GetAttribute("src");
                if (string.IsNullOrEmpty(src))
                    continue;
                var bloomCanvas = canvasElement.ParentNode as SafeXmlElement;
                if (bloomCanvas == null || !bloomCanvas.HasClass(HtmlDom.kBloomCanvasClass))
                    continue;
                var bcImg =
                    bloomCanvas.ChildNodes.FirstOrDefault(c =>
                        c is SafeXmlElement ce && ce.LocalName == "img"
                    ) as SafeXmlElement;
                if (
                    bcImg != null
                    && !ImageUtils.IsPlaceholderImageFilename(bcImg.GetAttribute("src"))
                )
                    continue; // paranoia
                if (bcImg != null)
                {
                    bloomCanvas.RemoveChild(bcImg);
                }
                var svg =
                    bloomCanvas.ChildNodes.FirstOrDefault(c =>
                        c is SafeXmlElement ce && ce.LocalName == "svg"
                    ) as SafeXmlElement;
                var insertBefore = svg != null ? svg.NextSibling : bloomCanvas.FirstChild;
                bloomCanvas.InsertBefore(img, insertBefore);
                // Preserve links if they exist (BL-14318)
                if (imgContainer.AttributeNames.Contains("data-href"))
                    bloomCanvas.SetAttribute("data-href", imgContainer.GetAttribute("data-href"));
                if (canvasElement.AttributeNames.Contains("data-sound"))
                    bloomCanvas.SetAttribute(
                        "data-sound",
                        canvasElement.GetAttribute("data-sound")
                    );
                canvasElement.ParentNode.RemoveChild(canvasElement);
                if (
                    !bloomCanvas.ChildNodes.Any(c =>
                        c is SafeXmlElement ce && ce.HasClass("bloom-canvas-element")
                    )
                )
                {
                    bloomCanvas.RemoveClass("bloom-has-canvas-element");
                }
            }
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
                        if (
                            ControlForInvoke != null
                            && ControlForInvoke.IsHandleCreated
                            && !ControlForInvoke.IsDisposed
                        )
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
            List<string> warningMessages,
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
            List<string> warningMessages,
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

        public const string DefaultFont = "Andika";

        /// <summary>
        /// Checks the wanted fonts for being valid for  embedding, both for licensing and for the type of file
        /// (based on the filename extension).
        /// The list of rejected fonts is returned in badFonts and the list of files to copy for good fonts is
        /// returned in filesToEmbed.  Messages are written to the progress output as the processing goes along.
        /// </summary>
        /// <remarks>
        /// fontFileFinder must be either a new instance or a stub for testing.
        /// Setting fontFileFinder.NoteFontsWeCantInstall ensures that fontFileFinder. GetFilesForFont(font)
        /// will not return any files for fonts that we know cannot be embedded without reference to the
        /// license details.
        /// </remarks>
        public static void CheckFontsForEmbedding(
            IWebSocketProgress progress,
            HashSet<FontInfo> fontsWanted,
            IFontFinder fontFileFinder,
            out List<string> filesToEmbed,
            out HashSet<string> badFonts
        )
        {
            filesToEmbed = new List<string>();
            badFonts = new HashSet<string>();

            var invalidFonts = new HashSet<string>();
            var illegalFonts = new HashSet<string>();
            var missingFonts = new HashSet<string>();

            fontFileFinder.NoteFontsWeCantInstall = true;
            if (_fontMetadataMap == null)
            {
                EnsureMetadataForHarvesterMode(fontsWanted);
                _fontMetadataMap = new Dictionary<string, FontMetadata>();
                foreach (var meta in FontsApi.AvailableFontMetadata)
                    _fontMetadataMap.Add(meta.name, meta);
            }
            var filesAlreadyAdded = new HashSet<string>();
            foreach (var font in fontsWanted.OrderBy(x => x.ToString()))
            {
                var fontFile = fontFileFinder.GetFileForFont(
                    font.fontFamily,
                    font.fontStyle,
                    font.fontWeight
                );
                var filesFound = fontFile != null; // unembeddable fonts determined don't have any files recorded
                var badLicense = false;
                var missingLicense = false;
                var badFileType = false;
                var fileExtension = "";
                if (_fontMetadataMap.TryGetValue(font.fontFamily, out var meta))
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
                        fileExtension = Path.GetExtension(fontFile).ToLowerInvariant();
                        badFileType = !FontMetadata.fontFileTypesBloomKnows.Contains(fileExtension);
                    }
                }
                if (filesFound && !badFileType && !badLicense)
                {
                    var fileToEmbed = fontFile;
                    if (!filesAlreadyAdded.Contains(fileToEmbed))
                    {
                        filesToEmbed.Add(fileToEmbed);
                        filesAlreadyAdded.Add(fileToEmbed);
                    }
                    else
                    {
                        continue;
                    }
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
                    var size = new FileInfo(fontFile).Length;
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
                var dontComplain = font.fontFamily == "Andika New Basic";
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
                        ProgressKind.Error
                    );
                    invalidFonts.Add(font.fontFamily);
                }
                else if (fontFileFinder.FontsWeCantInstall.Contains(font.fontFamily) || badLicense)
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
                        ProgressKind.Error
                    );
                    illegalFonts.Add(font.fontFamily);
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
                        ProgressKind.Error
                    );
                    missingFonts.Add(font.fontFamily);
                }
                if (!dontComplain)
                    progress.MessageWithParams(
                        "PublishTab.Android.File.Progress.SubstitutingAndika",
                        "{0} is a font name",
                        "Bloom will substitute \"{0}\" instead.",
                        ProgressKind.Error,
                        DefaultFont,
                        font
                    );
                badFonts.Add(font.fontFamily); // need to prevent the bad/missing font from showing up in fonts.css and elsewhere
            }
            ReportProblemFontsForHarvesterMode(invalidFonts, illegalFonts, missingFonts);
        }

        public static string ProblemFontsPath;

        private static void ReportProblemFontsForHarvesterMode(
            HashSet<string> invalidFonts,
            HashSet<string> illegalFonts,
            HashSet<string> missingFonts
        )
        {
            // If we are running in harvester mode, we want to report the fonts that are not suitable for publication.
            // This is so that the harvester can report them to the user, and won't upload the artifacts.
            if (
                Program.RunningHarvesterMode
                && (invalidFonts.Count > 0 || illegalFonts.Count > 0 || missingFonts.Count > 0)
            )
            {
                var bldr = new StringBuilder();
                foreach (var fontName in invalidFonts)
                {
                    bldr.AppendLine($"invalid font - {fontName}");
                }
                foreach (var fontName in illegalFonts)
                {
                    bldr.AppendLine($"illegal font - {fontName}");
                }
                foreach (var fontName in missingFonts)
                {
                    bldr.AppendLine($"missing font - {fontName}");
                }
                if (!string.IsNullOrEmpty(ProblemFontsPath))
                    RobustFile.WriteAllText(ProblemFontsPath, bldr.ToString());
            }
        }

        /// <summary>
        /// If running in harvester mode, load the metadata for the default font and just the fonts
        /// that are actually wanted for publication.
        /// </summary>
        private static void EnsureMetadataForHarvesterMode(HashSet<FontInfo> fontsWanted)
        {
            if (Program.RunningHarvesterMode && !FontsApi.AvailableFontMetadata.Any())
            {
                var fontNames = new List<string>
                {
                    DefaultFont, // Ensure that Andika's metadata is always available.
                    "ABeeZee", // Another font distributed with Bloom
                };
                foreach (var font in fontsWanted)
                {
                    if (!fontNames.Contains(font.fontFamily))
                        fontNames.Add(font.fontFamily);
                }
                FontsApi.GetAllFontMetadata(fontNames);
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

        public static async Task ReportInvalidFontsAsync(
            string destDirName,
            IProgress progress,
            Control controlToInvokeOn
        )
        {
            // Make a browser so we can accurately determine what fonts are actually requested by
            // the stylesheets in the book, or might be if other languages being uploaded are activated.
            HashSet<string> fontsFound = new HashSet<string>();
            var bookPath = BookStorage.FindBookHtmlInFolder(destDirName);
            var dom = HtmlDom.CreateFromHtmlFile(bookPath);
            dom.BaseForRelativePaths = destDirName;
            foreach (
                var editable in dom.RawDom.SafeSelectElements(
                    "//div[contains(@class, 'bloom-editable')]"
                )
            )
            {
                // stuff has to be visible for us to compute the font family used for it
                // We can make all the bloom-editable elements visible, since we already removed
                // the ones that are not going to be uploaded, and we are not going to use this
                // DOM for anything else.
                if (editable.GetAttribute("lang") == "z")
                    continue; // will never be visible.
                editable.AddClass("bloom-visibility-code-on");
            }

            // This function, which is what we want to do next, may be either invoked
            // or simply run, depending on whether we need to force running it on the UI thread.
            // We can only manipulate the browser on the UI thread (except in tests).
            var getFontsAction = async () =>
            {
                // This will usually be the main UI thread, but in tests it could be anything.
                // In production, we must make sure we're on the UI thread to create and manipulate a browser,
                // but we may no longer be after we await RunJavaScriptAsync. We have to be on the same
                // thread to dispose it, so keep track of which thread it is.
                var threadWhereWeMadeBrowser = Thread.CurrentThread;
                // Even trying controlToInvokeOn.Invoke everywhere the browser is referenced, I couldn't get
                // "await WebView2Browser.CreateAsync()" to produce a browser that would successfully navigate
                // to the page for checking fonts.  The navigation would always time out and report failure,
                // unless it crashed before the timeout and stopped the program with exit code 0x80000003.
                // If it didn't crash, often the scan for fonts would return an empty array which looks
                // innocuous (but possibly misleading) to the user.  When it didn't return an empty array,
                // it returned an array full of nothing but "Times New Roman", the default browser font on
                // Windows which is illegal to embed or distribute, and complained vociferously to the user.
                // See BL-15292 for more details and discussion.
                var browser = new WebView2Browser(); // NOT await WebView2Browser.CreateAsync();
                try
                {
                    // Logically, if any await can result in a thread switch, we might need to invoke again here.
                    // (But invoking again here doesn't work!?)
                    if (
                        !browser.NavigateAndWaitTillDone(
                            dom,
                            10000,
                            InMemoryHtmlFileSource.JustCheckingPage,
                            () => false,
                            false
                        )
                    )
                    {
                        // We had problems with timeouts here in similar code (BL-7892).
                        // We may as well carry on and detect as many problem fonts as we can.
                        Debug.WriteLine("Failed to navigate fully to ReportInvalidFontsAsync DOM");
                        Logger.WriteEvent(
                            "Failed to navigate fully to ReportInvalidFontsAsync DOM"
                        );
                    }

                    // Get and store the display and font information for each element in the DOM.
                    var rawInfo = await browser.GetObjectFromJavascriptAsync(
                        GetElementFontFamilyInfoJavascript
                    );
                    //Debug.WriteLine($"DEBUG ReportInvalidFontsAsync: rawInfo={rawInfo}");
                    var fontFamilyInfo = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(
                        rawInfo
                    );

                    if (fontFamilyInfo != null)
                    {
                        foreach (var family in fontFamilyInfo)
                        {
                            var font = ExtractFontNameFromFontFamily(family);
                            if (!string.IsNullOrEmpty(font))
                            {
                                fontsFound.Add(font);
                            }
                        }
                    }
                }
                finally
                {
                    if (threadWhereWeMadeBrowser == Thread.CurrentThread)
                    {
                        // This seems to happen in tests. In live code, given the bizarre way
                        // Windows.Forms implements await and resumes on a different thread,
                        // we don't take this branch, but we normally have a controlToInvokeOn,
                        // which is usually null in tests.
                        browser.Dispose();
                    }
                    else if (controlToInvokeOn != null)
                    {
                        // If we made the browser on this control's thread, we can dispose of it properly by invoking
                        // to that thread. This is the usual path in production.
                        controlToInvokeOn.Invoke(() => browser.Dispose());
                    }
                    // Otherwise, we just can't dispose of it properly. Probably we're running tests
                    // and it doesn't matter much.
                }
            };
            if (controlToInvokeOn == null)
            {
                await getFontsAction();
            }
            else
            {
                await (Task)controlToInvokeOn.Invoke(getFontsAction);
            }

            // The old approach. Enhance: this is probably much faster to run. We think its only
            // problem is false positives. Possibly if it finds no problems, we don't need to run
            /// the more expensive browser-based approach.
            //foreach (var filepath in Directory.EnumerateFiles(destDirName, "*.css"))
            //{
            //    var cssContent = RobustFile.ReadAllText(filepath);
            //    HtmlDom.FindFontsUsedInCss(cssContent, fontsFound, includeFallbackFonts: true);
            //}
            //// There should be only one html file with the same name as the directory it's in, but let's
            //// not make any assumptions here.
            //foreach (var filepath in Directory.EnumerateFiles(destDirName, "*.htm"))
            //{
            //    var cssContent = RobustFile.ReadAllText(filepath);
            //    HtmlDom.FindFontsUsedInCss(cssContent, fontsFound, includeFallbackFonts: true); // works on HTML files as well
            //}

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
                "monospace",
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
                            "BloomLibrary.org will display the PDF and allow downloads for translation, but cannot offer the \"READ\" button or downloads for BloomPUB or ePUB."
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

        private const string AILangTagFragment = "-x-ai";
        private const string BasicAIDataSelector =
            $"//div[@lang and contains(@lang,'{AILangTagFragment}')]";

        /// <summary>
        /// Check if the language tag indicates that the content is unpublishable.
        /// Currently, this is used only for AI-generated content.
        /// </summary>
        public static bool IsUnpublishableLanguage(string lang)
        {
            return lang.Contains(AILangTagFragment);
        }

        /// <summary>
        /// Check if the book contains any data that is in an unpublishable language.
        /// Currently, this means a translation generated by AI (BL-14339).
        /// </summary>
        public static bool BookHasUnpublishableData(Book.Book book)
        {
            return book.RawDom.SelectSingleNode($".{BasicAIDataSelector}") != null;
        }

        /// <summary>
        /// Remove any elements on a Bloom page with content that is unpublishable.
        /// Currently, that means a translation generated by AI (BL-14339).
        /// </summary>
        public static void RemoveUnpublishableContent(SafeXmlElement page)
        {
            var toBeDeleted = new List<SafeXmlElement>();
            foreach (SafeXmlElement elt in page.SafeSelectNodes($".{BasicAIDataSelector}"))
            {
                toBeDeleted.Add(elt);
            }
            foreach (var elt in toBeDeleted)
            {
                if (elt.ParentNode != null)
                    elt.ParentNode.RemoveChild(elt);
            }
        }

        /// <summary>
        /// Remove any data stored in the #bloomDataDiv that is unpublishable.
        /// Currently, this means a translation generated by AI (BL-14339).
        /// One would hope that AI generated content is never used for L1, L2, or L3
        /// (especially L1!), but this can't be prevented.  So we need to clean out
        /// any AI generated content from the data div to prevent publishing it.
        /// </summary>
        public static void RemoveUnpublishableBookData(SafeXmlDocument rawDom)
        {
            var toBeDeleted = new List<SafeXmlElement>();
            foreach (
                SafeXmlElement elt in rawDom.SafeSelectNodes(
                    $".//div[@id='bloomDataDiv']{BasicAIDataSelector}"
                )
            )
            {
                toBeDeleted.Add(elt);
            }
            foreach (var elt in toBeDeleted)
            {
                if (elt.ParentNode != null)
                    elt.ParentNode.RemoveChild(elt);
            }
        }

        /// <summary>
        /// Remove any data stored in the BookInfo that is unpublishable.
        /// Currently, this means a translation generated by AI or a reference to
        /// a language tag marking such a translation (BL-14339).
        /// </summary>
        public static void RemoveUnpublishableBookInfo(BookInfo bookInfo)
        {
            var allTitlesRaw = bookInfo.AllTitles;
            if (allTitlesRaw != null && allTitlesRaw.Contains(AILangTagFragment))
            {
                var titleDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                    allTitlesRaw
                );
                var badKeys = new List<string>();
                foreach (var key in titleDict.Keys)
                {
                    if (IsUnpublishableLanguage(key))
                        badKeys.Add(key);
                }
                foreach (var key in badKeys)
                    titleDict.Remove(key);
                bookInfo.AllTitles = JsonConvert.SerializeObject(titleDict);
            }
            var displayNames = bookInfo.MetaData.DisplayNames;
            if (displayNames.Any(kvp => IsUnpublishableLanguage(kvp.Key)))
            {
                var badKeys = new List<string>();
                foreach (var key in displayNames.Keys)
                {
                    if (IsUnpublishableLanguage(key))
                        badKeys.Add(key);
                }
                foreach (var key in badKeys)
                    displayNames.Remove(key);
            }
        }
    }
}
