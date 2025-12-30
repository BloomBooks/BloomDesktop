using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.SafeXml;
using Bloom.Utils;
using L10NSharp;
using SIL.IO;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Api for handling requests regarding the edit tab view itself
    /// </summary>
    public class EditingViewApi
    {
        public EditingView View { get; set; }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("editView/setModalState", HandleSetModalState, true);
            apiHandler.RegisterEndpointHandler("editView/chooseWidget", HandleChooseWidget, true);
            apiHandler.RegisterEndpointHandler(
                "editView/getColorsUsedInBookCanvasElements",
                HandleGetColorsUsedInBookCanvasElements,
                true
            );
            apiHandler.RegisterEndpointHandler("editView/pageDomLoaded", HandlePageDomLoaded, true);
            apiHandler.RegisterEndpointHandler(
                "editView/saveToolboxSetting",
                HandleSaveToolboxSetting,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/pageContent",
                request =>
                {
                    var pageContentData = request.RequiredPostString(unescape: false);
                    View.Model.ReceivePageContent(pageContentData);
                    request.PostSucceeded();
                },
                true,
                true // review.
            );
            apiHandler.RegisterEndpointHandler("editView/setTopic", HandleSetTopic, true);
            apiHandler.RegisterEndpointHandler(
                "editView/isTextSelected",
                HandleIsTextSelected,
                false
            );
            apiHandler.RegisterEndpointHandler("editView/getBookLangs", HandleGetBookLangs, false);
            apiHandler.RegisterEndpointHandler(
                "editView/isClipboardBookHyperlink",
                HandleIsClipboardBookHyperlink,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "editView/requestTranslationGroupContent",
                RequestDefaultTranslationGroupContent,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/duplicatePageMany",
                HandleDuplicatePageMany,
                true
            );
            apiHandler.RegisterEndpointHandler("editView/topics", HandleTopics, false);
            apiHandler.RegisterEndpointHandler("editView/changeImage", HandleChangeImage, true);
            apiHandler.RegisterEndpointHandler("editView/copyImage", HandleCopyImage, true);
            apiHandler.RegisterEndpointHandler("editView/pasteImage", HandlePasteImage, true);
            apiHandler.RegisterEndpointHandler("editView/paste", HandlePaste, true);
            apiHandler.RegisterEndpointHandler(
                "editView/topBarButtonClick",
                HandleTopBarButtonClick,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/updateTopBarDropdownDisplay",
                HandleUpdateTopBarDropdownDisplay,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/topBarDropdownClicked",
                HandleTopBarDropdownClicked,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/sourceTextTab",
                HandleSourceTextTab,
                false,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "editView/prevPageSplit",
                HandlePrevPageSplit,
                false
            );
            apiHandler.RegisterEndpointHandler("editView/jumpToPage", HandleJumpToPage, true);
            apiHandler.RegisterEndpointHandler(
                "editView/addImageFromUrl",
                HandleAddImageFromUrl,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/currentBookId",
                HandleGetCurrentBookId,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "editView/showBookSettingsDialog",
                HandleShowBookSettingsDialog,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/showPageSettingsDialog",
                HandleShowPageSettingsDialog,
                true
            );
        }

        private void HandleJumpToPage(ApiRequest request)
        {
            var pageId = request.GetPostStringOrNull();
            request.PostSucceeded();
            View.Model.SaveThen(() => pageId, () => { });
        }

        private void HandleShowBookSettingsDialog(ApiRequest request)
        {
            request.PostSucceeded();
            View.SaveAndOpenBookSettingsDialog();
        }

        private void HandleShowPageSettingsDialog(ApiRequest request)
        {
            request.PostSucceeded();
            View.SaveAndOpenPageSettingsDialog();
        }

        /// <summary>
        /// This one is for the snapping function on dragging origami splitters.
        /// </summary>
        /// <param name="request"></param>
        private void HandlePrevPageSplit(ApiRequest request)
        {
            var id = request.RequiredParam("id");
            var orientation = request.RequiredParam("orientation");
            IPage prevPage = null;
            foreach (var page in request.CurrentBook.GetPages())
            {
                if (page.Id == id)
                {
                    if (prevPage != null)
                    {
                        // prevPage is the one we want.
                        var classPosition =
                            orientation == "horizontal" ? "position-top" : "position-left";
                        var topSplitPanes = prevPage
                            .GetDivNodeForThisPage()
                            .SafeSelectNodes(
                                $".//div[contains(@class, 'split-pane-component') and contains(@class, '{classPosition}')]"
                            )
                            .Cast<SafeXmlElement>()
                            .ToArray();
                        // Enhance: this could reasonably do something fancier like finding the top-level split
                        // and using it if horizontal, even if there are other horizontal splits.
                        if (topSplitPanes.Length == 1)
                        {
                            // The stylesheet sets the position at 50%,
                            // so if the element doesn't have it set explicitly as an override,
                            // it will be at 50%.
                            var split = "50";

                            var style = topSplitPanes[0].GetAttribute("style");
                            if (!string.IsNullOrEmpty(style))
                            {
                                var styleKeyword = orientation == "horizontal" ? "bottom" : "right";
                                var matches = new Regex($"{styleKeyword}: (.*)%").Match(style);
                                if (matches.Success)
                                    split = matches.Groups[1].Value;
                            }
                            request.ReplyWithText(split);
                            return;
                        }
                    }
                    request.ReplyWithText("none");
                    return;
                }

                prevPage = page;
            }
            request.ReplyWithText("none");
        }

        private void HandleSourceTextTab(ApiRequest request)
        {
            var langTag = request.RequiredPostString();
            // There's a puzzle here that I encountered when converting from GeckoFx.
            // The code in BloomHintBubbles that adds the special item for 'hint' sets the
            // sourceTextTab that old code in _browser1_OnBrowserClick was looking for,
            // as if expecting that 'hint' would make that tab the default for other bubbles
            // just as it works for a language. One reason it didn't work before was that
            // the 'target' in that tab was typically the img, not the li element that has
            // the sourceTextTab attribute. So I made the JS call this method when that tab
            // is clicked, and that works. But setting Settings.Default.LastSourceLanguageViewed
            // to "hint" still does not work. I'm not sure why, nor whether that behavior is
            // wanted. However, saving it to LastSourceLanguageViewed when it doesn't work
            // causes the previously remembered language to be forgotten, so for now,
            // I'm coding it here to ignore 'hint'.
            if (langTag != "hint")
            {
                // If this is a different language than the current one, shift the current to secondary
                if (langTag != Settings.Default.LastSourceLanguageViewed)
                {
                    Settings.Default.LastSourceLanguageViewed2 = Settings
                        .Default
                        .LastSourceLanguageViewed;
                    Settings.Default.LastSourceLanguageViewed = langTag;
                    Settings.Default.Save();
                }
            }
            request.PostSucceeded();
        }

        private void HandlePasteImage(ApiRequest request)
        {
            dynamic data = DynamicJson.Parse(request.RequiredPostJson());
            View.OnPasteImage(
                data.imageId,
                UrlPathString.CreateFromUrlEncodedString(data.imageSrc),
                data.imageIsGif
            );
            request.PostSucceeded();
        }

        // Ctrl-V seems to be only possible to intercept in Javascript.
        // This makes it do the same as the Paste button.
        private void HandlePaste(ApiRequest request)
        {
            View.OnPaste(this, EventArgs.Empty);
            request.PostSucceeded();
        }

        private void HandleTopBarButtonClick(ApiRequest request)
        {
            dynamic data = DynamicJson.Parse(request.RequiredPostJson());
            // If we don't force the focus to the main editing browser, our browser with the buttons will steal it and cut/copy, etc. won't work.
            View.Browser.Focus();

            // Paste is tricky. We need C# to tell us if there is an image on the clipboard or not.
            // We don't want to go to the browser from here or we'll just end up coming back to C# and
            // back to the browser. So shortcut it and call the C# stuff directly.
            if (data.command == "paste")
            {
                HandlePaste(request);
                return;
            }

            View.Browser.RunJavascriptAsync(
                $"editTabBundle?.getEditablePageBundleExports()?.topBarButtonClick({data})"
            );
            request.PostSucceeded();
        }

        private void HandleUpdateTopBarDropdownDisplay(ApiRequest request)
        {
            View.UpdateDropdownButtons();
            request.PostSucceeded();
        }

        private void HandleTopBarDropdownClicked(ApiRequest request)
        {
            dynamic data = DynamicJson.Parse(request.RequiredPostJson());
            View.Browser.Focus();
            switch (data.command)
            {
                case "contentLanguages":
                    View.ContentLanguagesDropdownClicked();
                    break;
                case "layoutChoices":
                    View.LayoutChoicesDropdownClicked();
                    break;
            }
            request.PostSucceeded();
        }

        private void HandleCopyImage(ApiRequest request)
        {
            dynamic data = DynamicJson.Parse(request.RequiredPostJson());
            View.OnCopyImage(
                UrlPathString.CreateFromUrlEncodedString(data.imageSrc),
                data.imageIsGif
            );
            request.PostSucceeded();
        }

        private void HandleChangeImage(ApiRequest request)
        {
            dynamic data = DynamicJson.Parse(request.RequiredPostJson());
            // We don't want to tie up server locks etc. while the dialog displays.
            MiscUtils.DoOnceOnIdle(() =>
            {
                View.OnChangeImage(
                    data.imageId,
                    UrlPathString.CreateFromUrlEncodedString(data.imageSrc),
                    data.imageIsGif
                );
            });
            request.PostSucceeded();
        }

        // Answer true if the current clipboard contents are something that makes sense to paste into the href
        // of a hyperlink in a Bloom Book. Currently we allow all http(s) and mailto links, plus internal links
        // (starting with #) provided they are to a non-xmatter page that is present in the book.
        private void HandleIsClipboardBookHyperlink(ApiRequest request)
        {
            string clipContent = ""; // initial value is not used, delegate will set it.
            Program.MainContext.Send(
                o =>
                {
                    try
                    {
                        clipContent = PortableClipboard.GetText();
                    }
                    catch (Exception e)
                    {
                        // Need to make sure to handle exceptions.
                        // If the worker thread dies with an unhandled exception,
                        // it causes the whole program to immediately crash without opportunity for error reporting
                        Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);

                        // Causes the final result to be false
                        // Don't just ReplyWithBoolean here, that could result in trying to send two replies, which will fail.
                        clipContent = "";
                    }
                },
                null
            );

            request.ReplyWithBoolean(IsBloomHyperlink(clipContent, request.CurrentBook));
        }

        static Regex _bloomHyperlinkRegex = new Regex(
            @"^(/book/([-A-Fa-f0-9]+))?(#(cover|[-A-Fa-f0-9]+))?",
            RegexOptions.Compiled
        );

        private bool IsBloomHyperlink(string text, Book.Book book)
        {
            if (string.IsNullOrEmpty(text) || book == null)
                return false;
            // This is simplistic but enough to prevent most nonsensical URLs being put in links.
            if (
                text.StartsWith("http://")
                || text.StartsWith("https://")
                || text.StartsWith("mailto:")
            )
                return true;
            var match = _bloomHyperlinkRegex.Match(text);
            if (match.Success)
            {
                var bookId = match.Groups[2].Value;
                var pageId = match.Groups[4].Value;
                // It is no good linking to xmatter pages by id because their IDs change.
                // We maintain a link to the outside front cover using the id "cover" to get
                // around this problem.
                if (bookId == book.ID)
                {
                    return pageId == "cover"
                        || book.GetPages().Any(page => page.Id == pageId && !page.IsXMatter);
                }
                else
                {
                    return Guid.TryParse(bookId, out var bookGuid)
                        && (pageId == "cover" || Guid.TryParse(pageId, out var pageGuid));
                }
            }
            return false;
        }

        private void RequestDefaultTranslationGroupContent(ApiRequest request)
        {
            View.Model.RequestDefaultTranslationGroupContent(request);
        }

        private void HandleGetBookLangs(ApiRequest request)
        {
            var bookData = request.CurrentBook.BookData;
            dynamic answer = new ExpandoObject();
            answer.V = bookData.Language1.Name;
            answer.N1 = bookData.MetadataLanguage1.Name;
            var n2Name = bookData.MetadataLanguage2?.Name;
            answer.N2 = string.IsNullOrEmpty(n2Name) ? "-----" : n2Name;
            request.ReplyWithJson(answer);
        }

        private void HandleIsTextSelected(ApiRequest request)
        {
            EditingModel.IsTextSelected = request.RequiredPostBooleanAsJson();
            request.PostSucceeded();
        }

        private void HandleDuplicatePageMany(ApiRequest request)
        {
            var model = View.Model;
            var requestData = DynamicJson.Parse(request.RequiredPostJson());
            request.PostSucceeded();
            model.DuplicatePageManyTimes((int)requestData.numberOfTimes);
        }

        public void HandleSetModalState(ApiRequest request)
        {
            lock (request)
            {
                View.SetModalState(request.RequiredPostBooleanAsJson());
                request.PostSucceeded();
            }
        }

        private void HandleChooseWidget(ApiRequest request)
        {
            using (
                var dlg = new MiscUI.BloomOpenFileDialog
                {
                    Filter = "Widget files|*.wdgt;*.html;*.htm",
                }
            )
            {
                var result = dlg.ShowDialog();
                if (result != DialogResult.OK)
                {
                    request.ReplyWithText("");
                    return;
                }

                var fullWidgetPath = dlg.FileName;
                var ext = Path.GetExtension(fullWidgetPath);
                if (ext.EndsWith("htm") || ext.EndsWith("html"))
                {
                    fullWidgetPath = WidgetHelper.CreateWidgetFromHtmlFolder(fullWidgetPath);
                    if (String.IsNullOrEmpty(fullWidgetPath))
                    {
                        // treat an error in creating the widget the same as canceling (BL-15421)
                        request.ReplyWithText("");
                        return;
                    }
                }
                UrlPathString activityRelativePathUrl = View.Model.AddWidgetFilesToBookFolder(
                    fullWidgetPath
                );
                request.ReplyWithText(activityRelativePathUrl.UrlEncodedForHttpPath);
                // clean up the temporary widget file we created.
                if (fullWidgetPath != dlg.FileName)
                    RobustFile.Delete(fullWidgetPath);
            }
        }

        private void HandleGetColorsUsedInBookCanvasElements(ApiRequest request)
        {
            var model = View.Model;
            if (!model.HaveCurrentEditableBook)
            {
                request.ReplyWithText("");
                return;
            }
            var currentBook = View.Model.CurrentBook;
            // Enhance: Two ideas. (1) Get colors from the current page first, in case the book has too many
            // colors to fit in our dialog's swatch array. (2) Order the list returned by frequency, so the most
            // frequently used colors are at the front of the resultant swatch array.
            var currentBookDom = currentBook.OurHtmlDom;
            var colors = currentBookDom.GetColorsUsedInBookCanvasElements();
            request.ReplyWithText("[" + String.Join(",", colors) + "]");
        }

        private void HandlePageDomLoaded(ApiRequest request)
        {
            // we collect and pass on the pageId for bookkeeping purposes
            var pageId = request.RequiredPostString();
            View.Model.HandlePageDomLoadedEvent(pageId);
            request.PostSucceeded();
        }

        private void HandleSaveToolboxSetting(ApiRequest request)
        {
            var settingString = request.RequiredPostString();
            View.Model.SaveToolboxSettings(settingString);
            request.PostSucceeded();
        }

        private void HandleSetTopic(ApiRequest request)
        {
            var topicString = request.RequiredPostString();
            // RequiredPostString cannot be empty, so we use a substitute value for empty.
            if (topicString == "<NONE>")
                topicString = "";
            View.Model.SetTopic(topicString);
            request.PostSucceeded();
        }

        public void HandleTopics(ApiRequest request)
        {
            var keyToLocalizedTopicDictionary = new Dictionary<string, string>();
            foreach (var topic in BookInfo.TopicsKeys)
            {
                var localized = LocalizationManager.GetDynamicString(
                    "Bloom",
                    "Topics." + topic,
                    topic,
                    @"shows in the topics chooser in the edit tab"
                );
                keyToLocalizedTopicDictionary.Add(topic, localized);
            }

            var localizedNoTopic = LocalizationManager.GetDynamicString(
                "Bloom",
                "Topics.NoTopic",
                "No Topic",
                @"shows in the topics chooser in the edit tab"
            );
            var arrayOfKeyValuePairs =
                from key in keyToLocalizedTopicDictionary.Keys
                orderby keyToLocalizedTopicDictionary[key]
                select $"{{\"englishKey\":\"{key}\",\"translated\":\"{keyToLocalizedTopicDictionary[key]}\"}}";
            var data = new List<string>
            {
                $"{{\"englishKey\":\"No Topic\",\"translated\":\"{localizedNoTopic}\"}}",
            };
            data.AddRange(arrayOfKeyValuePairs);
            dynamic answer = new ExpandoObject();
            answer.Topics = data.ToArray();
            if (View == null || View.Model == null || View.Model.CurrentBook == null)
            {
                // Happens in unit tests.
                answer.Current = "";
            }
            else
            {
                var currentBook = View.Model.CurrentBook;
                var currentTopicKey = currentBook
                    .BookData.GetVariableOrNull("topic", "en")
                    .Unencoded;
                if (string.IsNullOrEmpty(currentTopicKey))
                    currentTopicKey = "No Topic";
                answer.Current = currentTopicKey;
            }

            request.ReplyWithJson(answer);
        }

        private void HandleAddImageFromUrl(ApiRequest request)
        {
            var desiredFileNameWithoutExtension = request.RequiredPostString(
                "desiredFileNameWithoutExtension"
            );
            var url = request.RequiredPostString("url");

            try
            {
                // When this is done, it will send a websocket message of the form "makeThumbnailFile-" + imageId
                Task.Run(() => View.AddImageFromUrlAsync(desiredFileNameWithoutExtension, url));
                request.PostSucceeded();
            }
            catch (Exception ex)
            {
                request.Failed("Error adding image: " + ex.Message);
            }
        }

        private void HandleGetCurrentBookId(ApiRequest request)
        {
            if (request.CurrentBook == null)
            {
                // it's not obvious what to do here. but HandleGetColorsUsedInBookCanvasElements()
                // has this kind of logic.
                request.ReplyWithText("");
                return;
            }
            request.ReplyWithText(request.CurrentBook.ID);
        }
    }
}
