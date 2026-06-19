using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.SafeXml;
using Bloom.Utils;
using L10NSharp;
using SIL.Core.ClearShare;
using SIL.IO;
using SIL.Progress;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
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
                "editView/topBar/contentLanguageUsage",
                HandleGetContentLanguageUsage,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/topBar/contentLanguageUsageChange",
                HandleContentLanguageUsageChange,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/topBar/layoutChoiceData",
                HandleGetLayoutChoiceData,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/topBar/layoutChoiceChange",
                HandleLayoutChoiceChange,
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
                "editView/toggleCustomPageLayout",
                HandleToggleCustomCover,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/getDataBookValue",
                HandleGetDataBookValue,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/frameSources",
                HandleGetFrameSources,
                true
            );
            apiHandler.RegisterEndpointHandler(
                "editView/imageGalleryResult",
                HandleImageGalleryResult,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "editView/pickLocalImageFile",
                HandlePickLocalImageFile,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "editView/localFilePreview",
                HandleLocalFilePreview,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/artOfReading/local-collections/collections",
                HandleArtOfReadingCollections,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/artOfReading/local-collections/search",
                HandleArtOfReadingSearch,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/artOfReading/local-collections/collection-image",
                HandleArtOfReadingCollectionImage,
                false
            );
        }

        private void HandleGetFrameSources(ApiRequest request)
        {
            if (request.HttpMethod != HttpMethods.Get)
                throw new ArgumentException("editView/frameSources only supports GET");

            request.ReplyWithJson(View.GetEditFrameSources());
        }

        private void HandleGetDataBookValue(ApiRequest request)
        {
            var lang = request.RequiredParam("lang");
            var dataBook = request.RequiredParam("dataBook");
            var multiText = View.Model.CurrentBook.BookData.GetMultiTextVariableOrEmpty(dataBook);
            var value = multiText.GetExactAlternative(lang) ?? "";
            var matchingDataDivElement =
                View.Model.CurrentBook.RawDom.SelectSingleNode(
                    $"//div[@id='bloomDataDiv']/div[@data-book='{dataBook}' and @lang='{lang}']"
                ) as SafeXmlElement;

            request.ReplyWithJson(
                new
                {
                    content = value,
                    id = matchingDataDivElement?.GetAttribute("id"),
                    dataAudioRecordingMode = matchingDataDivElement?.GetAttribute(
                        "data-audiorecordingmode"
                    ),
                    dataDuration = matchingDataDivElement?.GetAttribute("data-duration"),
                    dataAudioRecordingEndTimes = matchingDataDivElement?.GetAttribute(
                        "data-audiorecordingendtimes"
                    ),
                    recordingMd5 = matchingDataDivElement?.GetAttribute("recordingmd5"),
                    hasAudioSentenceClass = matchingDataDivElement?.HasClass("audio-sentence")
                        ?? false,
                    hasBloomPostAudioSplitClass = matchingDataDivElement?.HasClass(
                        "bloom-postAudioSplit"
                    ) ?? false,
                }
            );
        }

        /// <summary>
        /// Save the current state of the page, then toggle the book cover custom flag, and finally reload the page.
        /// If we are in standard mode and do not have any saved state for custom mode, we return false, and
        /// the calling code will generate a new custom page state.
        /// </summary>
        private void HandleToggleCustomCover(ApiRequest request)
        {
            var requestJson = request.GetPostJsonOrNull();
            var pageId =
                requestJson == null
                    ? request.GetPostStringOrNull()
                    : request.RequiredPostString("pageId");
            var keepCustomLayoutDataWhenSwitchingToStandard =
                requestJson != null
                && request.RequiredPostString("keepCustomLayoutDataWhenSwitchingToStandard")
                    == "true";
            var book = View.Model.CurrentBook;
            var page = book.GetPage(pageId);
            var pageElt = page.GetDivNodeForThisPage();
            var switchingToCustom = !pageElt.HasClass("bloom-customLayout");
            var shouldRemoveCustomLayoutDataWhenSwitchingToStandard =
                !switchingToCustom && !keepCustomLayoutDataWhenSwitchingToStandard;
            var customLayoutId = pageElt.GetAttribute("data-custom-layout-id");
            if (switchingToCustom)
            {
                var customLayoutData = book.BookData.GetVariableOrNull(customLayoutId, "*");
                if (string.IsNullOrEmpty(customLayoutData.Xml))
                {
                    request.ReplyWithText("false");
                    return;
                }
            }
            else
            {
                pageElt.RemoveAttribute("data-tool-id");
            }

            request.ReplyWithText("true");
            View.Model.SaveThen(
                () =>
                {
                    if (pageElt.HasClass("bloom-customLayout"))
                        pageElt.RemoveClass("bloom-customLayout");
                    else
                        pageElt.AddClass("bloom-customLayout");
                    // We must capture these from the saved page before typically replacing that with a different
                    // page element.
                    var backgroundAudio = pageElt.GetAttribute(HtmlDom.musicAttrName);
                    var backgroundAudioVolume = pageElt.GetAttribute(HtmlDom.musicVolumeName);
                    // Bring everything up to date consistent with the new
                    // state. Might be enough just do the BookData update.
                    book.EnsureUpToDateMemory(new NullProgress());
                    // Toggling between custom and standard layout can replace the xMatter page HTML,
                    // so reapply branding QR-code HTML adjustments for the current book settings.
                    // This should not need to regenerate the QR code file.
                    book.UpdateQrCodeHtmlForCurrentSettings(updateQrCodeFileEvenIfItExists: false);

                    if (
                        shouldRemoveCustomLayoutDataWhenSwitchingToStandard
                        && !string.IsNullOrWhiteSpace(customLayoutId)
                    )
                    {
                        book.BookData.RemoveAllFormsAndDataDivChildrenForDataBook(customLayoutId);
                    }

                    var updatedPageElt = book.GetPage(pageId)?.GetDivNodeForThisPage();
                    if (updatedPageElt != null)
                    {
                        if (string.IsNullOrEmpty(backgroundAudio))
                            updatedPageElt.RemoveAttribute(HtmlDom.musicAttrName);
                        else
                            updatedPageElt.SetAttribute(HtmlDom.musicAttrName, backgroundAudio);

                        if (string.IsNullOrEmpty(backgroundAudioVolume))
                            updatedPageElt.RemoveAttribute(HtmlDom.musicVolumeName);
                        else
                            updatedPageElt.SetAttribute(
                                HtmlDom.musicVolumeName,
                                backgroundAudioVolume
                            );

                        // Keep the same invariant we enforce elsewhere.
                        if (string.IsNullOrEmpty(backgroundAudio))
                            updatedPageElt.RemoveAttribute(HtmlDom.musicVolumeName);
                    }

                    return pageId;
                },
                () => { }
            );
        }

        private void HandleJumpToPage(ApiRequest request)
        {
            var pageId = request.GetPostStringOrNull();
            request.PostSucceeded();
            View.Model.SaveThen(() => pageId, () => { });
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
            ((DynamicJson)data).TryGetValue("pageBackgroundColor", out string pageBackgroundColor);
            try
            {
                PasteImage(
                    data.imageId,
                    UrlPathString.CreateFromUrlEncodedString(data.imageSrc),
                    data.imageIsGif,
                    pageBackgroundColor
                );
            }
            catch (InvalidOperationException e)
            {
                request.Failed(System.Net.HttpStatusCode.BadRequest, e.Message);
                return;
            }
            request.PostSucceeded();
        }

        protected virtual void PasteImage(
            string imageId,
            UrlPathString priorImageSrc,
            bool imageIsGif,
            string pageBackgroundColor
        )
        {
            View.OnPasteImage(imageId, priorImageSrc, imageIsGif, pageBackgroundColor);
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
                $"workspaceBundle?.getEditablePageBundleExports()?.topBarButtonClick({data})"
            );
            request.PostSucceeded();
        }

        private void HandleUpdateTopBarDropdownDisplay(ApiRequest request)
        {
            View.UpdateDropdownButtons();
            request.PostSucceeded();
        }

        private void HandleGetContentLanguageUsage(ApiRequest request)
        {
            request.ReplyWithJson(View.GetContentLanguageUsage());
        }

        private void HandleContentLanguageUsageChange(ApiRequest request)
        {
            dynamic data = request.RequiredPostDynamic();
            var languageTag = (string)data.languageTag;
            var isUsedForContent = Convert.ToBoolean(data.isUsedForContent);

            View.Browser.Focus();
            View.HandleContentLanguageUsageChange(languageTag, isUsedForContent);
            request.PostSucceeded();
        }

        private void HandleGetLayoutChoiceData(ApiRequest request)
        {
            request.ReplyWithJson(View.GetLayoutChoicesMenu());
        }

        private void HandleLayoutChoiceChange(ApiRequest request)
        {
            dynamic data = request.RequiredPostDynamic();
            var layoutClassName = (string)data.layoutChoiceId;

            View.Browser.Focus();
            View.HandleLayoutChoicesMenuAction(layoutClassName);
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
                var ext = Path.GetExtension(fullWidgetPath).ToLowerInvariant();
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

        /// <summary>
        /// Saves an image chosen in the image gallery to the book folder and returns
        /// the resulting src and metadata as JSON for the JS caller to apply.
        /// Accepts either a local file path (localPath) or a remote URL (imageUrl).
        /// Gallery-provided license/credits/creator override the source EXIF, except for
        /// images from official collections (e.g. Art of Reading) whose EXIF is authoritative.
        /// </summary>
        private void HandleImageGalleryResult(ApiRequest request)
        {
            var data = (DynamicJson)DynamicJson.Parse(request.RequiredPostJson());
            data.TryGetValue("localPath", out string localPath);
            data.TryGetValue("imageUrl", out string imageUrl);
            data.TryGetValue("credits", out string credits);
            data.TryGetValue("license", out string license);
            data.TryGetValue("licenseUrl", out string licenseUrl);
            data.TryGetValue("creator", out string galleryCreator);
            string sourceFilePath;
            bool isTempFile = false;

            if (!string.IsNullOrEmpty(localPath))
            {
                sourceFilePath = localPath;
            }
            else if (!string.IsNullOrEmpty(imageUrl))
            {
                string extension;
                try
                {
                    extension = Path.GetExtension(new Uri(imageUrl).LocalPath);
                }
                catch
                {
                    extension = ".jpg";
                }
                if (string.IsNullOrEmpty(extension))
                    extension = ".jpg";

                sourceFilePath = Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString() + extension
                );
#pragma warning disable SYSLIB0014 // WebClient is fine for a simple synchronous download here
                using (var client = new System.Net.WebClient())
                    client.DownloadFile(imageUrl, sourceFilePath);
#pragma warning restore SYSLIB0014
                isTempFile = true;
            }
            else
            {
                request.Failed(
                    HttpStatusCode.BadRequest,
                    "imageGalleryResult requires localPath or imageUrl"
                );
                return;
            }

            try
            {
                // GIF files must be copied byte-for-byte to preserve animation.
                // PalasoImage / ProcessAndSaveImageIntoFolder will strip the animation frames.
                if (
                    Path.GetExtension(sourceFilePath)
                        .Equals(".gif", StringComparison.OrdinalIgnoreCase)
                )
                {
                    var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
                    var destName = ImageUtils.GetUnusedFilename(
                        View.Model.CurrentBook.FolderPath,
                        baseName,
                        ".gif",
                        "gif"
                    );
                    RobustFile.Copy(
                        sourceFilePath,
                        Path.Combine(View.Model.CurrentBook.FolderPath, destName)
                    );
                    request.ReplyWithJson(
                        new
                        {
                            src = UrlPathString.CreateFromUnencodedString(destName).UrlEncoded,
                            copyright = "",
                            license = "",
                            creator = "",
                        }
                    );
                    return;
                }

                using (var palasoImage = PalasoImage.FromFileRobustly(sourceFilePath))
                {
                    var info = PageEditingModel.ChangePicture(
                        View.Model.CurrentBook.FolderPath,
                        "",
                        UrlPathString.CreateFromUnencodedString(""),
                        palasoImage
                    );

                    // Metadata.Write (used inside ChangePicture) writes from the
                    // source-file-locked TagLib object, so existing EXIF tags like
                    // "Picassa" Artist can survive. Use SaveImageMetadataIfNeeded on a
                    // fresh load of the destination file so the replacement is complete.
                    var licenseInfo = BuildLicenseInfoFromGallery(license, licenseUrl);
                    bool hasGalleryMeta =
                        !string.IsNullOrEmpty(galleryCreator)
                        || !string.IsNullOrEmpty(credits)
                        || licenseInfo != null;

                    if (hasGalleryMeta)
                    {
                        var galleryMetadata = new Metadata();
                        galleryMetadata.Creator = galleryCreator ?? "";
                        galleryMetadata.CopyrightNotice = credits ?? "";
                        if (licenseInfo != null)
                            galleryMetadata.License = licenseInfo;

                        var destFileName = Uri.UnescapeDataString(info.src);
                        ImageUtils.SaveImageMetadataIfNeeded(
                            galleryMetadata,
                            View.Model.CurrentBook.FolderPath,
                            destFileName
                        );
                    }

                    request.ReplyWithJson(
                        new
                        {
                            src = info.src,
                            copyright = !string.IsNullOrEmpty(credits) ? credits : info.copyright,
                            creator = !string.IsNullOrEmpty(galleryCreator)
                                ? galleryCreator
                                : info.creator,
                            license = licenseInfo != null ? licenseInfo.ToString() : info.license,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                request.Failed(HttpStatusCode.InternalServerError, ex.Message);
            }
            finally
            {
                if (isTempFile && RobustFile.Exists(sourceFilePath))
                    RobustFile.Delete(sourceFilePath);
            }
        }

        /// <summary>
        /// Builds a libpalaso ILicenseInfo from the gallery-provided license string and/or URL.
        /// CC license URLs (creativecommons.org) are parsed into a proper CreativeCommonsLicense;
        /// everything else becomes a CustomLicense so the text is preserved.
        /// Returns null when no info is given.
        /// </summary>
        private static LicenseInfo BuildLicenseInfoFromGallery(string license, string licenseUrl)
        {
            // Only invoke the CC parser for actual creativecommons.org URLs.
            // FromLicenseUrl misparses unrelated URLs (e.g. pixabay.com/service/license/)
            // instead of throwing, so we guard before calling it.
            if (!string.IsNullOrEmpty(licenseUrl) && licenseUrl.Contains("creativecommons.org"))
            {
                try
                {
                    return CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
                }
                catch
                {
                    // Malformed CC URL — fall through to the named string.
                }
            }
            if (!string.IsNullOrEmpty(license))
                return new CustomLicense { RightsStatement = license };
            return null;
        }

        /// <summary>
        /// Opens a native file-picker dialog and returns the selected path as JSON.
        /// Pass gifOnly:true to restrict the filter to GIF files.
        /// </summary>
        private void HandlePickLocalImageFile(ApiRequest request)
        {
            dynamic data = DynamicJson.Parse(request.RequiredPostJson());
            ((DynamicJson)data).TryGetValue("gifOnly", out bool gifOnly);

            var filter = gifOnly
                ? "GIF images|*.gif"
                : "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.svg";

            string selectedPath = "";
            View.Invoke(
                (Action)(
                    () =>
                    {
                        using (
                            var dlg = new BloomOpenFileDialog
                            {
                                InitialDirectory = Environment.GetFolderPath(
                                    Environment.SpecialFolder.MyPictures
                                ),
                                Filter = filter,
                            }
                        )
                        {
                            View.SetModalState(true);
                            try
                            {
                                using (LegacyDpiDialogLauncher.EnterLegacyDpiScope())
                                {
                                    if (dlg.ShowDialog() == DialogResult.OK)
                                        selectedPath = dlg.FileName;
                                }
                            }
                            finally
                            {
                                View.SetModalState(false);
                            }
                        }
                    }
                )
            );

            _lastPickedLocalImagePath = selectedPath;
            var previewUrl = string.IsNullOrEmpty(selectedPath)
                ? ""
                : "/bloom/api/editView/localFilePreview?path=" + Uri.EscapeDataString(selectedPath);
            request.ReplyWithJson(new { filePath = selectedPath, previewUrl });
        }

        /// <summary>
        /// Serves a single local image file for preview purposes.
        /// The "path" query parameter is the absolute OS path to the file.
        /// Only the path most recently returned by HandlePickLocalImageFile is allowed.
        /// </summary>
        private void HandleLocalFilePreview(ApiRequest request)
        {
            var path = request.RequiredParam("path");
            var fullPath = Path.GetFullPath(path);

            if (fullPath != _lastPickedLocalImagePath)
            {
                request.Failed(HttpStatusCode.Forbidden, "File not authorized for preview");
                return;
            }

            if (!RobustFile.Exists(fullPath))
            {
                request.Failed(HttpStatusCode.NotFound, "File not found");
                return;
            }

            request.ReplyWithImage(fullPath);
        }

        /// <summary>
        /// The path most recently returned by HandlePickLocalImageFile. HandleLocalFilePreview
        /// only serves this exact file, preventing arbitrary local-file access via the endpoint.
        /// </summary>
        private string _lastPickedLocalImagePath;

        private static readonly string[] _defaultAorLanguages = new[] { "en", "es" };

        /// <summary>
        /// The root folder where SIL image collections (including Art of Reading) are installed.
        /// </summary>
        private static string ArtOfReadingBaseFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SIL",
                "ImageCollections"
            );

        /// <summary>
        /// Returns the list of Art of Reading image collections installed on this machine,
        /// together with the keyword-search languages they support.
        /// </summary>
        private void HandleArtOfReadingCollections(ApiRequest request)
        {
            var baseFolder = ArtOfReadingBaseFolder;
            if (!Directory.Exists(baseFolder))
            {
                request.ReplyWithJson(
                    new { collections = Array.Empty<string>(), languages = _defaultAorLanguages }
                );
                return;
            }

            var collections = Directory
                .GetDirectories(baseFolder)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray();

            var languages = GetArtOfReadingLanguages(baseFolder, collections);
            request.ReplyWithJson(new { collections, languages });
        }

        /// <summary>
        /// Reads the first available index.txt header to discover which keyword-language
        /// columns the collection provides (e.g. "en", "es").
        /// </summary>
        private static string[] GetArtOfReadingLanguages(string baseFolder, string[] collections)
        {
            foreach (var collection in collections)
            {
                var indexPath = Path.Combine(baseFolder, collection, "index.txt");
                if (!RobustFile.Exists(indexPath))
                    continue;
                var firstLine = RobustFile.ReadAllLines(indexPath).FirstOrDefault();
                if (firstLine == null)
                    continue;
                // Language-code columns are exactly 2 characters; skip "filename", "subfolder", "country"
                var langCodes = firstLine.Split('\t').Where(col => col.Length == 2).ToArray();
                if (langCodes.Length > 0)
                    return langCodes;
            }
            return _defaultAorLanguages;
        }

        /// <summary>
        /// Searches an Art of Reading collection's index.txt for images whose keyword list
        /// (in the requested language) contains the search term.
        /// Returns an array of {url, localPath} objects — url is a root-relative Bloom API URL
        /// for thumbnail display; localPath is the absolute OS path so the caller can copy the
        /// file directly without an extra HTTP round-trip.
        /// </summary>
        private void HandleArtOfReadingSearch(ApiRequest request)
        {
            var collection = request.RequiredParam("collection");
            var lang = request.RequiredParam("lang");
            var term = request.RequiredParam("term").Trim().ToLowerInvariant();

            var safeBase = Path.GetFullPath(ArtOfReadingBaseFolder);
            var indexPath = Path.GetFullPath(
                Path.Combine(ArtOfReadingBaseFolder, collection, "index.txt")
            );
            var imagesBaseForGuard = Path.GetFullPath(
                Path.Combine(ArtOfReadingBaseFolder, collection, "images")
            );
            if (
                !indexPath.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase)
                || !imagesBaseForGuard.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase)
            )
            {
                request.Failed(HttpStatusCode.Forbidden, "Invalid collection path");
                return;
            }

            if (!RobustFile.Exists(indexPath))
            {
                request.ReplyWithJson(Array.Empty<object>());
                return;
            }

            var lines = RobustFile.ReadAllLines(indexPath);
            if (lines.Length == 0)
            {
                request.ReplyWithJson(Array.Empty<object>());
                return;
            }

            var headers = lines[0].Split('\t');
            var filenameIdx = Array.IndexOf(headers, "filename");
            var subfolderIdx = Array.IndexOf(headers, "subfolder");
            if (subfolderIdx < 0)
                subfolderIdx = Array.IndexOf(headers, "country");
            var langIdx = Array.IndexOf(headers, lang);

            if (filenameIdx < 0 || langIdx < 0)
            {
                request.ReplyWithJson(Array.Empty<object>());
                return;
            }

            const string imageEndpoint =
                "/bloom/api/imageGallery/artOfReading/local-collections/collection-image";
            var imagesBase = imagesBaseForGuard;
            var results = new List<object>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var cols = line.Split('\t');
                if (cols.Length <= langIdx)
                    continue;

                var tags = cols[langIdx].Split(',').Select(t => t.Trim().ToLowerInvariant());
                if (!tags.Contains(term))
                    continue;

                var filename = filenameIdx < cols.Length ? cols[filenameIdx].Trim() : "";
                var subfolder =
                    subfolderIdx >= 0 && subfolderIdx < cols.Length
                        ? cols[subfolderIdx].Trim()
                        : "";

                // Resolve the actual file path, handling AOR's optional one-level
                // subsubfolder nesting (index subfolder may not be the direct parent).
                var imagePath = FindAorImagePath(imagesBase, subfolder, filename);
                if (imagePath == null)
                    continue;

                // Relative path from images/ to the file, forward-slash separated, for the URL
                var relPath = imagePath[imagesBase.Length..]
                    .TrimStart(Path.DirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/');

                results.Add(
                    new
                    {
                        url = $"{imageEndpoint}?collection={Uri.EscapeDataString(collection)}&file={Uri.EscapeDataString(relPath)}",
                        localPath = imagePath,
                    }
                );
            }

            request.ReplyWithJson(results.ToArray());
        }

        /// <summary>
        /// Resolves the actual path of an AOR image on disk.
        /// The index "subfolder" column may omit a further nesting level, so if the direct
        /// path does not exist we search one level of subdirectories (mirroring the Node.js
        /// storeInMapsIfFileExists logic).
        /// Returns null if the file cannot be found.
        /// </summary>
        private static string FindAorImagePath(string imagesBase, string subfolder, string filename)
        {
            // Try the direct path first
            var directPath = string.IsNullOrEmpty(subfolder)
                ? Path.Combine(imagesBase, filename)
                : Path.Combine(imagesBase, subfolder, filename);

            if (RobustFile.Exists(directPath))
                return directPath;

            if (string.IsNullOrEmpty(subfolder))
                return null;

            // Direct path failed; search subdirectories of the subfolder one level deep
            var subfolderDir = Path.Combine(imagesBase, subfolder);
            if (!Directory.Exists(subfolderDir))
                return null;

            foreach (var subdir in Directory.GetDirectories(subfolderDir))
            {
                var candidate = Path.Combine(subdir, filename);
                if (RobustFile.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Serves a single Art of Reading image from the local image collections folder.
        /// The "file" query parameter is a subfolder-relative path such as "Animals/dog.png".
        /// </summary>
        private void HandleArtOfReadingCollectionImage(ApiRequest request)
        {
            var collection = request.RequiredParam("collection");
            var file = request.RequiredParam("file");

            // Normalise separators and guard against directory traversal
            var normalizedFile = file.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var imagePath = Path.GetFullPath(
                Path.Combine(ArtOfReadingBaseFolder, collection, "images", normalizedFile)
            );
            var safeBase = Path.GetFullPath(ArtOfReadingBaseFolder);

            if (!imagePath.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
            {
                request.Failed(HttpStatusCode.Forbidden, "Invalid image path");
                return;
            }

            if (!RobustFile.Exists(imagePath))
            {
                request.Failed(HttpStatusCode.NotFound, "Image not found");
                return;
            }

            request.ReplyWithImage(imagePath);
        }
    }
}
