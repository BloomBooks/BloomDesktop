using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.SafeXml;
using Bloom.web;
using L10NSharp;
using SIL.IO;
using SIL.Xml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Forms;
using System.Xml;

namespace Bloom.Spreadsheet
{
    public class SpreadsheetExporter
    {
        InternalSpreadsheet _spreadsheet = new InternalSpreadsheet();
        private IWebSocketProgress _progress;
        private BloomWebSocketServer _webSocketServer;
        private string _outputFolder; // null if not exporting to folder (mainly some unit tests)
        private string _outputImageFolder; // null if not exporting to folder (mainly some unit tests)

        private ILanguageDisplayNameResolver LangDisplayNameResolver { get; set; }

        public delegate SpreadsheetExporter Factory();

        /// <summary>
        /// Constructs a new Spreadsheet Exporter
        /// </summary>
        /// <param name="webSocketServer">The webSockerServer of the instance</param>
        /// <param name="langDisplayNameResolver">The object that will be used to  retrieve the language display names</param>
        public SpreadsheetExporter(
            BloomWebSocketServer webSocketServer,
            ILanguageDisplayNameResolver langDisplayNameResolver
        )
        {
            _webSocketServer = webSocketServer;
            LangDisplayNameResolver = langDisplayNameResolver;
        }

        /// <summary>
        /// Constructs a new Spreadsheet Exporter
        /// </summary>
        /// <param name="webSocketServer">The webSockerServer of the instance</param>
        /// <param name="collectionSettings">The collectionSettings of the book that will be exported. This is used to retrieve the language display names</param>
        public SpreadsheetExporter(
            BloomWebSocketServer webSocketServer,
            CollectionSettings collectionSettings
        )
            : this(
                webSocketServer,
                new CollectionSettingsLanguageDisplayNameResolver(collectionSettings)
            ) { }

        public SpreadsheetExporter(ILanguageDisplayNameResolver langDisplayNameResolver)
        {
            Debug.Assert(
                Bloom.Program.RunningUnitTests,
                "SpreadsheetExporter should be passed a webSocketProgress unless running unit tests that don't need it"
            );

            LangDisplayNameResolver = langDisplayNameResolver;
        }

        //a list of values which, if they occur in the data-book attribute of an element in the bloomDataDiv,
        //indicate that the element content should be treated as an image, even though the element doesn't
        //have a src attribute nor actually contain an img element
        public static List<string> DataDivImagesWithNoSrcAttributes = new List<string>()
        {
            "licenseImage"
        };

        public async Task ExportToFolderWithProgressAsync(
            HtmlDom dom,
            string bookFolderPath,
            string outputFolder,
            Action<string> resultCallback
        )
        {
            await BrowserProgressDialog.DoWorkWithProgressDialogAsync(
                _webSocketServer,
                async (progress, worker) =>
                {
                    var spreadsheet = ExportToFolder(
                        dom,
                        bookFolderPath,
                        outputFolder,
                        out string outputFilePath,
                        progress
                    );
                    // ExportToFolder will return null if export fails, in which case we don't want to open/foreground the spreadsheet
                    if (spreadsheet != null)
                    {
                        resultCallback(outputFilePath);
                    }
                    return progress.HaveProblemsBeenReported;
                },
                "collectionTab",
                "Exporting Spreadsheet",
                showCancelButton: false
            );
        }

        public SpreadsheetExportParams Params = new SpreadsheetExportParams();

        public InternalSpreadsheet Export(
            HtmlDom dom,
            string bookFolderPath,
            IWebSocketProgress progress = null
        )
        {
            _progress = progress ?? new NullWebSocketProgress();
            _spreadsheet.Params = Params;
            var pages = dom.GetPageElements();

            //Get xmatter
            var dataDiv = GetDataDiv(dom);
            AddDataDivData(dataDiv, bookFolderPath);

            var iContentPage = 0;
            foreach (var page in pages)
            {
                // We ignore all xmatter pages, which were handled above by exporting data div data.
                if (XMatterHelper.IsXMatterPage(page))
                    continue;
                var pageNumber = page.GetAttribute("data-page-number") ?? "";
                //Each page alternates colors
                var colorForPage =
                    iContentPage++ % 2 == 0
                        ? InternalSpreadsheet.AlternatingRowsColor1
                        : InternalSpreadsheet.AlternatingRowsColor2;
                AddContentRows(page, pageNumber, bookFolderPath, colorForPage);
            }
            _spreadsheet.SortHiddenContentRowsToTheBottom();
            return _spreadsheet;
        }

        private bool _reportedImageDescription;

        private void AddContentRows(
            SafeXmlElement page,
            string pageNumber,
            string bookFolderPath,
            Color colorForPage
        )
        {
            var imageContainers = GetImageContainers(page);
            var allGroups = TranslationGroupManager.SortedGroupsOnPage(page, true);
            var groups = allGroups
                .Where(x => !x.GetAttribute("class").Contains("bloom-imageDescription"))
                .ToList();
            var videoContainers = page.SafeSelectNodes(
                    ".//*[contains(@class,'bloom-videoContainer')]"
                )
                .Cast<SafeXmlElement>()
                .ToList();
            var widgetContainers = page.SafeSelectNodes(
                    ".//*[contains(@class,'bloom-widgetContainer')]"
                )
                .Cast<SafeXmlElement>()
                .ToList();

            var pageType = SpreadsheetImporter.GetLabelFromPage(page);

            // Each of these will result in one row in the output.
            var rowContentSources = Extensions.MapUnevenLists(
                new[] { groups, imageContainers, videoContainers, widgetContainers }
            );
            foreach (var pageContent in rowContentSources)
            {
                var row = new ContentRow(_spreadsheet);
                if (!string.IsNullOrEmpty(pageType))
                {
                    var pageTypeIndex = _spreadsheet.AddColumnForTag(
                        InternalSpreadsheet.PageTypeColumnLabel,
                        "Page Type"
                    );
                    row.SetCell(pageTypeIndex, pageType);
                    // If we make more rows for this page, don't specify a type.
                    // This allows subsequent rows to go onto the same page if there is room.
                    pageType = null;
                }
                row.SetCell(
                    InternalSpreadsheet.RowTypeColumnLabel,
                    InternalSpreadsheet.PageContentRowLabel
                );
                row.SetCell(InternalSpreadsheet.PageNumberColumnLabel, pageNumber);
                var translationGroup = pageContent[0];
                var imageContainer = pageContent[1];
                var videoContainer = pageContent[2];
                var widgetContainer = pageContent[3];

                if (imageContainer != null)
                {
                    var image = (SafeXmlElement)imageContainer.SafeSelectNodes(".//img")[0];
                    var imagePath = ImagePath(
                        bookFolderPath,
                        image?.GetAttribute("src") ?? "placeHolder.png"
                    );
                    var fileName = Path.GetFileName(imagePath);
                    var outputPath = Path.Combine("images", fileName);
                    if (fileName == "placeHolder.png")
                        outputPath = InternalSpreadsheet.BlankContentIndicator;
                    row.SetCell(InternalSpreadsheet.ImageSourceColumnLabel, outputPath);
                    CopyImageFileToSpreadsheetFolder(imagePath);
                    var descriptions = imageContainer
                        .GetElementsByTagName("div")
                        .Where(e => e.GetAttribute("class").Contains("bloom-imageDescription"));
                    foreach (var description in descriptions) // typically at most one
                    {
                        var descriptionRow = new ContentRow(_spreadsheet);
                        descriptionRow.SetCell(
                            InternalSpreadsheet.RowTypeColumnLabel,
                            InternalSpreadsheet.ImageDescriptionRowLabel
                        );
                        descriptionRow.SetCell(
                            InternalSpreadsheet.PageNumberColumnLabel,
                            pageNumber
                        );
                        // Give this row the same color. It is conceptually part of the export of the same chunk of the document.
                        descriptionRow.BackgroundColor = colorForPage;
                        WriteTranslationGroup(description, descriptionRow, bookFolderPath);
                    }
                }

                if (translationGroup != null)
                {
                    WriteTranslationGroup(translationGroup, row, bookFolderPath);
                }

                if (videoContainer != null)
                {
                    WriteVideo(videoContainer, row, bookFolderPath);
                }

                if (widgetContainer != null)
                {
                    WriteWidget(widgetContainer, row, bookFolderPath);
                }

                row.BackgroundColor = colorForPage;
            }
        }

        private void WriteWidget(
            SafeXmlElement widgetContainer,
            ContentRow row,
            string bookFolderPath
        )
        {
            var source = UrlPathString
                .CreateFromUrlEncodedString(
                    widgetContainer
                        .GetElementsByTagName("iframe")
                        .FirstOrDefault()
                        ?.GetAttribute("src") ?? ""
                )
                .NotEncoded;
            var index = _spreadsheet.AddColumnForTag(
                InternalSpreadsheet.WidgetSourceColumnLabel,
                "Widgets"
            );
            row.SetCell(index, source);

            if (_outputFolder != null) // only null in some unit tests
            {
                // We expect source to be something like activities/widgetFolder/.../something.html
                // We want to copy widgetFolder (which might not be the direct parent of something.html)
                var sourceDir = Path.GetDirectoryName(source.Substring("activities/".Length)); // typically widgetFolder
                while (!string.IsNullOrEmpty(Path.GetDirectoryName(sourceDir)))
                    sourceDir = Path.GetDirectoryName(sourceDir);
                try
                {
                    var destFolderPath = Path.Combine(_outputFolder, "activities");
                    Directory.CreateDirectory(destFolderPath);
                    DirectoryUtilities.CopyDirectory(
                        Path.Combine(bookFolderPath, "activities", sourceDir),
                        destFolderPath
                    );
                }
                catch (Exception e)
                    when (e is IOException
                        || e is SecurityException
                        || e is UnauthorizedAccessException
                    )
                {
                    _progress.MessageWithParams(
                        "Spreadsheet.TroubleCopyingFolder",
                        "",
                        "Bloom had trouble copying the folder {0} to the activities folder: {1}",
                        ProgressKind.Warning,
                        sourceDir,
                        e.Message
                    );
                }
            }
        }

        private void WriteVideo(
            SafeXmlElement videoContainer,
            ContentRow row,
            string bookFolderPath
        )
        {
            var source = UrlPathString
                .CreateFromUrlEncodedString(
                    videoContainer
                        .GetElementsByTagName("source")
                        .Cast<SafeXmlElement>()
                        .FirstOrDefault()
                        ?.GetAttribute("src") ?? ""
                )
                .NotEncoded;
            var index = _spreadsheet.AddColumnForTag(
                InternalSpreadsheet.VideoSourceColumnLabel,
                "Video"
            );
            row.SetCell(index, source);
            if (_outputFolder != null)
            {
                var sourcePath = Path.Combine(bookFolderPath, source);
                var destPath = Path.Combine(_outputFolder, source);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    RobustFile.Copy(sourcePath, destPath);
                }
                catch (Exception e)
                    when (e is IOException
                        || e is SecurityException
                        || e is UnauthorizedAccessException
                    )
                {
                    _progress.MessageWithParams(
                        "SpreadsheetTroubleCopying",
                        "",
                        "Bloom had trouble copying the file {0} to the video folder: {1}",
                        ProgressKind.Warning,
                        sourcePath,
                        e.Message
                    );
                }
            }
        }

        private void WriteTranslationGroup(
            SafeXmlElement translationGroup,
            ContentRow row,
            string bookFolderPath
        )
        {
            foreach (
                var editable in translationGroup
                    .SafeSelectNodes("./*[contains(@class, 'bloom-editable')]")
                    .Cast<SafeXmlElement>()
            )
            {
                var langCode = editable.GetAttribute("lang") ?? "";
                if (langCode == "z" || langCode == "")
                    continue;
                var index = GetOrAddColumnForLang(langCode);
                var content = editable.InnerXml;
                content = content.Replace(
                    "<span class=\"bloom-audio-split-marker\">\u200B</span>",
                    "|"
                );
                // Don't just test content, it typically contains paragraph markup.
                if (String.IsNullOrWhiteSpace(editable.InnerText))
                {
                    content = InternalSpreadsheet.BlankContentIndicator;
                }
                row.SetCell(index, content);
                ExportAudio(editable, row, bookFolderPath);
            }
            // A special case just for Quiz pages.
            if (
                ((SafeXmlElement)translationGroup.ParentNode)
                    .GetAttribute("class")
                    ?.Contains("correct-answer") ?? false
            )
            {
                var indexAttr = _spreadsheet.AddColumnForTag(
                    InternalSpreadsheet.AttributeColumnLabel,
                    "Attribute data"
                );
                row.SetCell(indexAttr, "../class=correct-answer");
            }
            // Enhance: as necessary, we plan to enhance this to look for attributes whose names
            // start with data-content on the translation group, possibly its parent, maybe if
            // necessary the bloom-editables as well though that might need multiple columns,
            // and export it into the attributes column.
        }

        private void ExportAudio(SafeXmlElement editable, SpreadsheetRow row, string bookFolderPath)
        {
            // There are four possible states for a bloom-Editable in regard to audio.
            // 1. Audio recorded by block and not split.
            //   - The bloom-editable has class audio-sentence
            //   - The bloom-editable has data-audiorecordingmode="TextBox"
            //   - The bloom-editable has an id. The audio for the block lives in audio/id.mp3,
            //     which should exist.
            //   - The bloom-editable has data-duration, corresponding to the duration
            //     of the mp3.
            // 2. Audio recorded by block and split.
            //   - The bloom-editable has all of the above plus data-audiorecordingendtimes,
            //     a space-separated list of when the audio for each sentence ends.
            //   - the bloom-editable has the additional class bloom-postAudioSplit.
            //   - the p children of the bloom-editable have spans, each corresponding to
            //     a sentence.
            //   - each span has an id (though these do not correspond to files)
            //   - each span has class bloom-highlightSegment (but not class audio-sentence)
            // 3. Audio recorded by sentence.
            //   - The bloom-editable has data-audiorecordingmode="Sentence"
            //   - the p children of the bloom-editable have spans, each corresponding to
            //     a sentence.
            //   - each span has an id, at least one of which corresponds to an audio/[id].mp3 file
            //     (if none of them corresponds to a file, we don't have audio for this block;
            //     but it's not unusual that only some sentences have been recorded.)
            //   - each span has class audio-sentence
            //   - each span that has a recording has a data-duration attribute
            // In all three cases, the elements which have class audio-sentence also have
            // recordingmd5 attributes, if a recording has actually been made.
            // 4. No recorded audio.
            //   - might have none of these attributes, if we've never even opened
            //     the page using the talking book tool
            //   - might have much of the state of either of (1) or (3) above, except that
            //     any ids on audio-sentence elements do NOT correspond to existing audio
            //     files. Typically, they will also not have data-duration or recordingmd5.
            //     Typically data-audiorecordingendtimes and bloom-highlightSegment elements
            //     would also be absent, but there might be exceptions where audio has
            //     been recorded and deleted.
            // Another possible, but only legacy, state is data-audiorecordingmode="TextBox",
            // but the paragraphs have audio-sentence classes and mp3-file ids. This
            // corresponds to an early form of TextBox recording in which the audio was
            // split into individual sentence files. If we
            // find this, we will export it as sentence mode, and import will lose the
            // fact that it was once recorded as a text-block and the user
            // possibly wants to record it that way if re-doing.
            //
            // What we want to output:
            // 1. If there's any recorded audio, we need a column [audio lg] (user-friendly:
            // Language-name audio) which contains the relative path to the mp3 file(s),
            // comma-space separated.
            // In sentence mode, we'll output the literal "missing" for any span that
            // doesn't have a recording. (In TextBox mode, if the one expected audio file
            // is missing, we don't have audio at all for the block and don't export
            // anything for audio.)
            // 2. If there's data-audiorecordingendtimes, we want to list them in another
            // column [audio alignments lg] (user-friendly: Language-name alignments).
            // 3. If we're in text box mode with no data-audiorecordingendtimes, we want
            // that same column, but we'll just put the overall duration in it as a
            // single number.
            // 4. In sentence mode, we don't put anything in the alignments column
            // (or even create it, if nothing else wants it)
            // 5. All audio files noted should be copied to the spreadsheet folder.
            // Note that we don't output recordingmd5. It has no obvious use unless we
            // restore it on import, but then the user has the almost-impossible responsibility
            // of figuring out what it should be if the text is edited. It seems better
            // to assume that any recordings provided on import are current, and set the
            // recordingmd5 to the appropriate value for the imported text.
            var lang = editable.GetAttribute("lang");
            if (editable.HasClass("audio-sentence"))
            {
                var path = HandleAudioFile(editable, bookFolderPath);
                if (path == "missing")
                    return; // no real recording, so don't put anything in the file.
                var audioColIndex = GetOrAddColumnForLangAudio(lang);
                var alignmentIndex = GetOrAddColumnForAudioAlignment(lang);
                var endTimes = editable.GetAttribute("data-audiorecordingendtimes");
                if (string.IsNullOrEmpty(endTimes))
                {
                    var duration = editable.GetAttribute("data-duration");
                    row.SetCell(alignmentIndex, duration);
                }
                else
                {
                    row.SetCell(alignmentIndex, endTimes);
                }

                row.SetCell(audioColIndex, path);
            }
            else
            {
                // look for sentence-level
                var audioSentences = editable
                    .SafeSelectNodes(".//span[@class='audio-sentence']")
                    .Cast<SafeXmlElement>();
                var fileList = new StringBuilder();
                var gotRealRecording = false;
                foreach (var sentence in audioSentences)
                {
                    var path = HandleAudioFile(sentence, bookFolderPath);
                    if (fileList.Length > 0)
                        fileList.Append(", ");
                    fileList.Append(path);
                    if (path != "missing")
                        gotRealRecording = true;
                }

                if (gotRealRecording)
                {
                    var audioColIndex = GetOrAddColumnForLangAudio(lang);
                    row.SetCell(audioColIndex, fileList.ToString());
                }
            }
        }

        private string HandleAudioFile(SafeXmlElement elt, string bookFolderPath)
        {
            var id = elt.GetAttribute("id") ?? "";
            // Some of our early unit tests don't set an output folder, yet the data we happened to choose
            // has some audio annotations, so we need to just ignore audio output if there's nowhere to put it.
            if (_outputFolder != null)
            {
                var audioFolder = Path.Combine(_outputFolder, "audio");
                Directory.CreateDirectory(audioFolder);
                var dest = Path.Combine(audioFolder, id + ".mp3");
                var src = Path.Combine(bookFolderPath, "audio", id + ".mp3");
                if (RobustFile.Exists(src))
                {
                    // We're creating a new folder, so it's somewhat pathological to find a duplicate.
                    // Somehow two blocks in the document have the same IDs. Maybe we should do a warning?
                    // Anyway, it pretty much has to be the same file, so just leave it.
                    if (!RobustFile.Exists(dest))
                    {
                        RobustFile.Copy(src, dest);
                    }
                }
                else
                {
                    // In the case of audio it's entirely normal not to find a recording.
                    // Just means the page has been analyzed and set up with ids for it,
                    // but the recording hasn't been made yet.
                    return "missing";
                }
            }

            return $"./audio/{id}.mp3";
        }

        private SafeXmlElement GetDataDiv(HtmlDom elementOrDom)
        {
            return elementOrDom
                .SafeSelectNodes(".//div[@id='bloomDataDiv']")
                .Cast<SafeXmlElement>()
                .First();
        }

        private List<SafeXmlElement> GetImageContainers(SafeXmlElement elementOrDom)
        {
            return elementOrDom
                .SafeSelectNodes(".//*[contains(@class,'bloom-imageContainer')]")
                .Cast<SafeXmlElement>()
                .ToList();
        }

        private string ImagePath(string imagesFolderPath, string imageSrc)
        {
            return Path.Combine(
                imagesFolderPath,
                UrlPathString.CreateFromUrlEncodedString(imageSrc).NotEncoded
            );
        }

        /// <summary>
        /// Get the column for a language. If no column exists, one will be added
        /// </summary>
        /// <remarks>If the column does not exist it will be added.
        /// The friendly name used for the column will be the display name for that language according to {this.LangDisplayNameResolver}</remarks>
        /// If the column already exists, its index will be returned. The column, including the column friendly name, will not be modified
        /// <param name="langCode">The language code to look up, as specified in the header</param>
        /// <returns>The index of the column</returns>
        private int GetOrAddColumnForLang(string langCode)
        {
            // Check if a column already exists for this column
            var colIndex = _spreadsheet.GetOptionalColumnForLang(langCode);
            if (colIndex >= 0)
            {
                return colIndex;
            }

            // Doesn't exist yet. Let's add a column for it.
            var langFriendlyName = LangDisplayNameResolver.GetLanguageDisplayName(langCode);
            return _spreadsheet.AddColumnForLang(langCode, langFriendlyName);
        }

        private int GetOrAddColumnForLangAudio(string langCode)
        {
            var colIndex = _spreadsheet.GetOptionalColumnForLangAudio(langCode);
            if (colIndex >= 0)
            {
                return colIndex;
            }

            // Doesn't exist yet. Let's add a column for it.
            // We're not yet trying to handle localizing labels.
            var langFriendlyName =
                LangDisplayNameResolver.GetLanguageDisplayName(langCode) + " audio";
            return _spreadsheet.AddColumnForLangAudio(langCode, langFriendlyName);
        }

        private int GetOrAddColumnForAudioAlignment(string langCode)
        {
            var colIndex = _spreadsheet.GetOptionalColumnForAudioAlignment(langCode);
            if (colIndex >= 0)
            {
                return colIndex;
            }

            // Doesn't exist yet. Let's add a column for it.
            // We're not yet trying to handle localizing labels.
            var langFriendlyName =
                LangDisplayNameResolver.GetLanguageDisplayName(langCode) + " alignments";
            return _spreadsheet.AddColumnForAudioAlignment(langCode, langFriendlyName);
        }

        private void AddDataDivData(SafeXmlNode node, string bookFolderPath)
        {
            var dataBookNodeList = node.SafeSelectNodes("./div[@data-book]")
                .Cast<SafeXmlElement>()
                .ToList();
            //Bring the ones with the same data-book value together so we can easily make a single row for each data-book value
            dataBookNodeList.Sort(
                (a, b) => a.GetAttribute("data-book").CompareTo(b.GetAttribute("data-book"))
            );
            string prevDataBookLabel = null;
            SpreadsheetRow row = null;
            foreach (SafeXmlElement dataBookElement in dataBookNodeList)
            {
                var langCode = dataBookElement.GetAttribute("lang");
                if (langCode == "z")
                {
                    continue;
                }

                var dataBookLabel = dataBookElement.GetAttribute("data-book");
                // Don't export branding, these elements often contain complex content
                // beyond our current capabilities, but also, importing branding won't work
                // because these elements are determined by the current branding of the collection.
                // So there's no point in cluttering the export with them.
                if (dataBookLabel.Contains("branding"))
                    continue;
                // No need to export this, Bloom has them all and chooses the right one
                // based on the licenseUrl.
                if (dataBookLabel == "licenseImage")
                    continue;

                //The first time we see this tag:
                if (!dataBookLabel.Equals(prevDataBookLabel))
                {
                    row = new ContentRow(_spreadsheet);
                    var label = "[" + dataBookLabel.Trim() + "]";
                    if (
                        label != InternalSpreadsheet.BookTitleRowLabel
                        && label != InternalSpreadsheet.CoverImageRowLabel
                    )
                        row.Hidden = true;
                    row.SetCell(InternalSpreadsheet.RowTypeColumnLabel, label);

                    var imageSrcAttribute = dataBookElement.GetAttribute("src").Trim();

                    if (IsDataDivImageElement(dataBookElement, dataBookLabel))
                    {
                        if (imageSrcAttribute.Length > 0)
                            UrlPathString.GetFullyDecodedPath(
                                bookFolderPath,
                                ref imageSrcAttribute
                            );
                        if (
                            imageSrcAttribute.Length > 0
                            && dataBookElement.InnerText.Trim().Length > 0
                            && !imageSrcAttribute.Equals(dataBookElement.InnerText.Trim())
                        )
                        {
                            //Some data-book items redundantly store the src of the image which they capture in both their content and
                            //src attribute. We haven't yet found any case in which they are different, so are only storing one in the
                            //spreadsheet. This test is to make sure that we notice if we come across a case where it might be necessary
                            //to save both.
                            _progress.MessageWithParams(
                                "Spreadsheet.DataDivConflictWarning",
                                "",
                                "Export warning: Found differing 'src' attribute and element text for data-div element {0}. The 'src' attribute will be ignored.",
                                ProgressKind.Warning,
                                dataBookLabel
                            );
                        }

                        string imageSource;
                        string childSrc = ChildImgElementSrc(dataBookElement);
                        if (childSrc.Length > 0)
                        {
                            // We've lost track of what was 'incomplete' about our handling of data-book elements
                            // that have an image child and don't have branding in their key. But the message
                            // was a nuisance. Keeping the code in case it reminds us of a problem at some point.
                            //if (! dataBookElement.GetAttribute("data-book").Contains("branding"))
                            //{
                            //	var msg = LocalizationManager.GetString("Spreadsheet:DataDivNonBrandingImageElment",
                            //		"Export warning: Found a non-branding image in an <img> element for " + dataBookLabel
                            //		+ ". This is not fully handled yet.");
                            //	NonFatalProblem.Report(ModalIf.All, PassiveIf.None, msg, showSendReport: true);
                            //}
                            // Don't think we ever have data-book elements with more than one image. But if we encounter one,
                            // I think it's worth warning the user that we don't handle it.
                            if (
                                dataBookElement.ChildNodes
                                    .Cast<SafeXmlNode>()
                                    .Count(
                                        n =>
                                            n.Name == "img"
                                            && string.IsNullOrEmpty(
                                                ((SafeXmlElement)n).GetAttribute("src")
                                            )
                                    ) > 1
                            )
                            {
                                _progress.MessageWithParams(
                                    "Spreadsheet.MultipleImageChildren",
                                    "",
                                    "Export warning: Found multiple images in data-book element {0}. Only the first will be exported.",
                                    ProgressKind.Warning,
                                    dataBookLabel
                                );
                            }

                            imageSource = childSrc;
                        }
                        else
                        {
                            //We determined that whether or not a data-book div has a src attribute, it is the innerText
                            //of the item that is used to set the src of the image in the actual pages of the document.
                            //So that's what we want to capture in the spreadsheet.
                            imageSource = dataBookElement.InnerText.Trim();
                        }

                        row.SetCell(
                            InternalSpreadsheet.ImageSourceColumnLabel,
                            Path.Combine("images", imageSource)
                        );
                        CopyImageFileToSpreadsheetFolder(ImagePath(bookFolderPath, imageSource));
                        prevDataBookLabel = dataBookLabel;
                        continue;
                    }
                }

                if (IsDataDivImageElement(dataBookElement, dataBookLabel))
                {
                    _progress.MessageWithParams(
                        "Spreadsheet.DataDivImageMultiple",
                        "",
                        "Export warning: Found multiple elements for image element {0}. Only the first will be exported.",
                        ProgressKind.Warning,
                        dataBookLabel
                    );
                    continue;
                }

                var colIndex = GetOrAddColumnForLang(langCode);
                row.SetCell(colIndex, dataBookElement.InnerXml.Trim());
                ExportAudio(dataBookElement, row, bookFolderPath);
                prevDataBookLabel = dataBookLabel;
            }
        }

        private void CopyImageFileToSpreadsheetFolder(string imageSourcePath)
        {
            if (_outputImageFolder != null)
            {
                if (Path.GetFileName(imageSourcePath) == "placeHolder.png")
                    return; // don't need to copy this around.
                if (!RobustFile.Exists(imageSourcePath))
                {
                    _progress.MessageWithParams(
                        "Spreadsheet.MissingImage",
                        "",
                        "Export warning: did not find the image {0}. It will be missing from the export folder.",
                        ProgressKind.Warning,
                        imageSourcePath
                    );
                    return;
                }

                var destPath = Path.Combine(_outputImageFolder, Path.GetFileName(imageSourcePath));
                RobustFile.Copy(imageSourcePath, destPath, true);
            }
        }

        private bool IsDataDivImageElement(SafeXmlElement dataBookElement, string dataBookLabel)
        {
            var imageSrc = dataBookElement.GetAttribute("src").Trim();
            //Unfortunately, in the current state of Bloom, we have at least three ways of representing in the bloomDataDiv things that are
            //images in the main document.Some can be identified by having a src attribute on the data-book element itself. Some actually contain
            //an img element. And some don't have any identifying mark at all, so to recognize them we just have to hard-code a list.
            return imageSrc.Length > 0
                || ChildImgElementSrc(dataBookElement).Length > 0
                || DataDivImagesWithNoSrcAttributes.Contains(dataBookLabel);
        }

        private string ChildImgElementSrc(SafeXmlElement node)
        {
            foreach (SafeXmlNode childNode in node.ChildNodes)
            {
                if (childNode.Name.Equals("img") && ((SafeXmlElement)childNode).HasAttribute("src"))
                {
                    return ((SafeXmlElement)childNode).GetAttribute("src");
                }
            }

            return "";
        }

        /// <summary>
        /// Output the specified DOM to the specified outputFolder (after deleting any existing content, if
        /// permitted...depends on overwrite param and possibly user input).
        /// Returns the intermediate spreadsheet object created, and also outputs the path to the xlsx file created.
        /// Looks for images in the specified bookFolderPath (typically the book folder) and copies them to an
        /// images subdirectory of the outputFolder. Also expects to find an audio subfolder if relevant.
        /// Currently the xlsx file created will have the same name as the outputFolder, typically copied from
        /// the input book folder.
        /// <returns>the internal spreadsheet, or null if not permitted to overwrite.</returns>
        /// </summary>
        public InternalSpreadsheet ExportToFolder(
            HtmlDom dom,
            string bookFolderPath,
            string outputFolder,
            out string outputPath,
            IWebSocketProgress progress = null,
            OverwriteOptions overwrite = OverwriteOptions.Ask
        )
        {
            outputPath = Path.Combine(
                outputFolder,
                Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(outputFolder) + ".xlsx")
            );
            _outputFolder = outputFolder;
            _outputImageFolder = Path.Combine(_outputFolder, "images");
            try
            {
                if (Directory.Exists(outputFolder))
                {
                    if (overwrite == OverwriteOptions.Quit)
                    {
                        // I'm assuming someone working with a command-line can cope with English.
                        // Don't think it's worth cluttering the XLF with this.
                        Console.WriteLine(
                            $"Output folder ({_outputFolder}) exists. Use --overwrite to overwrite."
                        );
                        outputPath = null;
                        return null;
                    }

                    var appearsToBeBloomBookFolder = Directory
                        .EnumerateFiles(outputFolder, "*.htm")
                        .Any();
                    var msgTemplate = LocalizationManager.GetString(
                        "Spreadsheet.Overwrite",
                        "You are about to replace the existing folder named {0}"
                    );
                    var msg = string.Format(msgTemplate, outputFolder);
                    var messageBoxButtons = new[]
                    {
                        new MessageBoxButton() { Text = "Overwrite", Id = "overwrite" },
                        new MessageBoxButton()
                        {
                            Text = "Cancel",
                            Id = "cancel",
                            Default = true
                        }
                    };
                    if (appearsToBeBloomBookFolder)
                    {
                        if (overwrite == OverwriteOptions.Overwrite)
                        {
                            // Assume we can't UI in this mode. But we absolutely must not overwrite the book folder!
                            // So quit anyway.
                            Console.WriteLine(
                                $"Output folder ({_outputFolder}) exists and appears to be a Bloom book, not a previous export. If you really mean to export there, you'll have to delete the folder first."
                            );
                            outputPath = null;
                            return null;
                        }

                        msgTemplate = LocalizationManager.GetString(
                            "Spreadsheet.OverwriteBook",
                            "The folder named {0} already exists and looks like it might be a Bloom book folder!"
                        );
                        msg = string.Format(msgTemplate, outputFolder);
                        messageBoxButtons = new[] { messageBoxButtons[1] }; // only cancel
                    }

                    if (overwrite == OverwriteOptions.Ask)
                    {
                        var formToInvokeOn = Application.OpenForms
                            .Cast<Form>()
                            .FirstOrDefault(f => f is Shell);
                        string result = null;
                        formToInvokeOn.Invoke(
                            (Action)(
                                () =>
                                {
                                    result = BloomMessageBox.Show(
                                        formToInvokeOn,
                                        msg,
                                        messageBoxButtons,
                                        MessageBoxIcon.Warning
                                    );
                                }
                            )
                        );
                        if (result != "overwrite")
                        {
                            outputPath = null;
                            return null;
                        }
                    } // if it's not Ask, at this point it must be Overwrite, so go ahead.
                }

                // In case there's a previous export, get rid of it.
                SIL.IO.RobustIO.DeleteDirectoryAndContents(_outputFolder);
                Directory.CreateDirectory(_outputImageFolder); // also (re-)creates its parent, outputFolder
                var spreadsheet = Export(dom, bookFolderPath, progress);
                spreadsheet.WriteToFile(outputPath, progress);
                return spreadsheet;
            }
            catch (Exception e)
                when (e is IOException || e is SecurityException || e is UnauthorizedAccessException
                )
            {
                if (e is IOException && (e.HResult & 0x0000FFFF) == 32) //ERROR_SHARING_VIOLATION
                {
                    Console.WriteLine(
                        "Writing Spreadsheet failed. Do you have it open in another program?"
                    );
                    Console.WriteLine(e);
                    progress?.Message(
                        "Spreadsheet.SpreadsheetLocked",
                        "",
                        "Bloom could not write to the spreadsheet because another program has it locked. Do you have it open in another program?",
                        ProgressKind.Error
                    );
                }
                else
                {
                    Console.WriteLine(
                        String.Format(
                            "Bloom had problems writing files to that location ({0}). Check that you have permission to write there.",
                            _outputFolder
                        )
                    );
                    Console.WriteLine(e);
                    progress.MessageWithParams(
                        "Spreadsheet.WriteFailed",
                        "",
                        "Bloom had problems writing files to that location ({0}). Check that you have permission to write there.",
                        ProgressKind.Error,
                        _outputFolder
                    );
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Export failed: ");
                Console.WriteLine(e);
                progress.MessageWithParams(
                    "Spreadsheet.ExportFailed",
                    "{0} is a placeholder for the exception message",
                    "Export failed: {0}",
                    ProgressKind.Error,
                    e.Message
                );
            }

            outputPath = null;
            return null; // some error occurred and was caught
        }
    }

    public enum OverwriteOptions
    {
        Overwrite,
        Quit,
        Ask
    }

    /// <summary>
    /// An interface for SpreadsheetExporter to be able to convert language tags to their display names.
    /// This allows unit tests to use mocks to handle this functionality instead of figuring out how to construct a concrete resolver
    /// </summary>
    public interface ILanguageDisplayNameResolver
    {
        /// <summary>
        /// Given a language tag, returns the friendly name of that language (according to the dictionary passed into the constructor)
        /// </summary>
        /// <param name="langTag"></param>
        /// <returns>Returns the friendly name if available. If not, returns the language tag unchanged.</returns>
        string GetLanguageDisplayName(string langTag);
    }

    /// <summary>
    /// Resolves language codes to language display names based on the book's CollectionSettings
    /// </summary>
    class CollectionSettingsLanguageDisplayNameResolver : ILanguageDisplayNameResolver
    {
        private CollectionSettings CollectionSettings;

        public CollectionSettingsLanguageDisplayNameResolver(CollectionSettings collectionSettings)
        {
            this.CollectionSettings = collectionSettings;
        }

        public string GetLanguageDisplayName(string langCode)
        {
            return this.CollectionSettings.GetDisplayNameForLanguage(langCode);
        }
    }

    // Note: You can also resolve these from the book.BookInfo.MetaData.DisplayNames dictionary, but
    // that seems to have fewer entries than CollectionSetting's or BookData's GetDisplayNameForLanguage() function
}
