using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.FontProcessing;
using Bloom.Publish.Epub;
using Bloom.web;
using Bloom.web.controllers;
using BloomTemp;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Publish.BloomPub
{
    /// <summary>
    /// This class is the beginnings of a separate place to put code for creating .bloompub files.
    /// Much of the logic is still in BookCompressor. Eventually we might move more of it here,
    /// so that making a bloompub actually starts here and calls BookCompressor.
    /// </summary>
    public static class BloomPubMaker
    {
        public const string BloomPubExtensionWithDot = ".bloompub";
        public const string kQuestionFileName = "questions.json";
        public const string BRExportFolder = "BloomReaderExport";
        internal const string kDistributionFileName = ".distribution";
        internal const string kDistributionBloomDirect = "bloom-direct";
        internal const string kDistributionBloomWeb = "bloom-web";
        internal const string kCreatorBloom = "bloom";
        internal const string kCreatorHarvester = "harvester";
        public static string HashOfMostRecentlyCreatedBook { get; private set; }
        public static Control ControlForInvoke { get; set; }

        public static void CreateBloomPub(
            BloomPubPublishSettings settings,
            BookInfo bookInfo,
            string outputFolder,
            BookServer bookServer,
            IWebSocketProgress progress
        )
        {
            var outputPath = Path.Combine(
                outputFolder,
                Path.GetFileName(bookInfo.FolderPath) + BloomPubExtensionWithDot
            );
            BloomPubMaker.CreateBloomPub(
                settings: settings,
                outputPath: outputPath,
                bookFolderPath: bookInfo.FolderPath,
                bookServer: bookServer,
                progress: progress,
                isTemplateBook: bookInfo.IsSuitableForMakingShells
            );
        }

        public static void CreateBloomPub(
            BloomPubPublishSettings settings,
            string outputPath,
            Book.Book book,
            BookServer bookServer,
            IWebSocketProgress progress
        )
        {
            CreateBloomPub(
                settings,
                outputPath,
                bookFolderPath: book.FolderPath,
                bookServer,
                progress: progress,
                isTemplateBook: book.IsTemplateBook
            );
        }

        // Create a BloomReader book while also creating the temporary folder for it (according to the specified parameter) and disposing of it
        public static void CreateBloomPub(
            BloomPubPublishSettings settings,
            string outputPath,
            string bookFolderPath,
            BookServer bookServer,
            IWebSocketProgress progress,
            bool isTemplateBook,
            string tempFolderName = BRExportFolder,
            string creator = kCreatorBloom
        )
        {
            using (var temp = new TemporaryFolder(tempFolderName))
            {
                CreateBloomPub(
                    settings,
                    outputPath,
                    bookFolderPath,
                    bookServer,
                    progress,
                    temp,
                    creator,
                    isTemplateBook
                );
            }
        }

        /// <summary>
        /// Create a Bloom Digital book (the zipped .bloompub file) as used by BloomReader (and Bloom Library etc)
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="outputPath">The path to create the zipped .bloompub output file at</param>
        /// <param name="bookFolderPath">The path to the input book</param>
        /// <param name="bookServer"></param>
        /// <param name="progress"></param>
        /// <param name="tempFolder">A temporary folder. This function will not dispose of it when done</param>
        /// <param name="creator">value for &lt;meta name="creator" content="..."/&gt; (defaults to "bloom")</param>
        /// <param name="isTemplateBook"></param>
        /// <returns>Path to the unzipped .bloompub</returns>
        public static string CreateBloomPub(
            BloomPubPublishSettings settings,
            string outputPath,
            string bookFolderPath,
            BookServer bookServer,
            IWebSocketProgress progress,
            TemporaryFolder tempFolder,
            string creator = kCreatorBloom,
            bool isTemplateBook = false
        )
        {
            var modifiedBook = PrepareBookForBloomReader(
                settings,
                bookFolderPath,
                bookServer,
                tempFolder,
                progress,
                isTemplateBook,
                creator
            );
            // We want at least 256 for Bloom Reader, because the screens have a high pixel density. And (at the moment) we are asking for
            // 64dp in Bloom Reader.

            BookCompressor.MakeSizedThumbnail(modifiedBook, modifiedBook.FolderPath, 256);

            MakeSha(BookStorage.FindBookHtmlInFolder(bookFolderPath), modifiedBook.FolderPath);
            CompressImages(
                modifiedBook.FolderPath,
                settings.ImagePublishSettings,
                modifiedBook.RawDom
            );
            SignLanguageApi.ProcessVideos(
                HtmlDom
                    .SelectChildVideoElements(modifiedBook.RawDom.DocumentElement)
                    .Cast<XmlElement>(),
                modifiedBook.FolderPath
            );
            var newContent = XmlHtmlConverter.ConvertDomToHtml5(modifiedBook.RawDom);
            RobustFile.WriteAllText(
                BookStorage.FindBookHtmlInFolder(modifiedBook.FolderPath),
                newContent,
                Encoding.UTF8
            );

            BookCompressor.CompressBookDirectory(
                outputPath,
                modifiedBook.FolderPath,
                MakeFilter(modifiedBook.FolderPath),
                "",
                wrapWithFolder: false
            );

            return modifiedBook.FolderPath;
        }

        /// <summary>
        /// Make a filter suitable for passing the files a BloomPub needs.
        /// </summary>
        public static IFilter MakeFilter(string folderPath)
        {
            var filter = new BookFileFilter(folderPath)
            {
                IncludeFilesNeededForBloomPlayer = true,
                WantMusic = true,
                WantVideo = true,
                NarrationLanguages = null
            };
            // these are artifacts of uploading book to BloomLibrary.org and not useful in BloomPubs
            filter.AlwaysReject("thumbnail-256.png");
            filter.AlwaysReject("thumbnail-70.png");
            return filter;
        }

        public static void CompressImages(
            string modifiedBookFolderPath,
            ImagePublishSettings imagePublishSettings,
            XmlDocument dom
        )
        {
            List<string> imagesToPreserveResolution;
            List<string> coverImages;

            var fullScreenAttr = dom.GetElementsByTagName("body")
                .Cast<XmlElement>()
                .First()
                .Attributes["data-bffullscreenpicture"]?.Value;
            if (
                fullScreenAttr != null
                && fullScreenAttr.IndexOf("bloomReader", StringComparison.InvariantCulture) >= 0
            )
            {
                // This feature (currently used for motion books in landscape mode) triggers an all-black background,
                // due to a rule in bookFeatures.less.
                // Making white pixels transparent on an all-black background makes line-art disappear,
                // which is bad (BL-6564), so just make an empty list in this case.
                coverImages = new List<string>();
            }
            else
            {
                coverImages = FindCoverImages(dom);
            }
            imagesToPreserveResolution = FindImagesToPreserveResolution(dom);

            foreach (var filePath in Directory.GetFiles(modifiedBookFolderPath))
            {
                if (
                    BookCompressor.CompressableImageFileExtensions.Contains(
                        Path.GetExtension(filePath).ToLowerInvariant()
                    )
                )
                {
                    var fileName = Path.GetFileName(filePath);
                    if (imagesToPreserveResolution.Contains(fileName))
                        continue; // don't compress these
                    // Cover images should be transparent if possible.  Others don't need to be.
                    var forUseOnColoredBackground = coverImages.Contains(fileName);
                    GetNewImageIfNeeded(filePath, imagePublishSettings, forUseOnColoredBackground);
                }
            }
        }

        private static void GetNewImageIfNeeded(
            string filePath,
            ImagePublishSettings imagePublishSettings,
            bool forUseOnColoredBackground
        )
        {
            using (var tagFile = RobustFileIO.CreateTaglibFile(filePath))
            {
                var currentWidth = tagFile.Properties.PhotoWidth;
                var currentHeight = tagFile.Properties.PhotoHeight;
                // We want to make sure that the image is not larger than the maximum width or height.
                // We don't know whether the image is portrait or landscape, so we have to check both
                // orientations.  The publish settings are known to be in landscape orientation, and
                // the image has to fit into those bounds, but we don't care whether the actual display
                // is portrait or landscape.
                if (
                    imagePublishSettings.MaxWidth >= currentWidth
                        && imagePublishSettings.MaxHeight >= currentHeight
                    || imagePublishSettings.MaxWidth >= currentHeight
                        && imagePublishSettings.MaxHeight >= currentWidth
                )
                {
                    if (!forUseOnColoredBackground)
                        return; // current file is okay as is: small enough and no need to make transparent.
                }
            }
            BookCompressor.CopyResizedImageFile(
                filePath,
                filePath,
                imagePublishSettings,
                forUseOnColoredBackground
            );
        }

        private const string kBackgroundImage = "background-image:url('"; // must match format string in HtmlDom.SetImageElementUrl()

        private static List<string> FindCoverImages(XmlDocument xmlDom)
        {
            var transparentImageFiles = new List<string>();
            foreach (
                var div in xmlDom
                    .SafeSelectNodes(
                        "//div[contains(concat(' ',@class,' '),' coverColor ')]//div[contains(@class,'bloom-imageContainer')]"
                    )
                    .Cast<XmlElement>()
            )
            {
                var style = div.GetAttribute("style");
                if (!String.IsNullOrEmpty(style) && style.Contains(kBackgroundImage))
                {
                    System.Diagnostics.Debug.Assert(
                        div.GetStringAttribute("class").Contains("bloom-backgroundImage")
                    );
                    // extract filename from the background-image style
                    transparentImageFiles.Add(ExtractFilenameFromBackgroundImageStyleUrl(style));
                }
                else
                {
                    // extract filename from child img element
                    var img = div.SelectSingleNode("//img[@src]");
                    if (img != null)
                        transparentImageFiles.Add(
                            System.Web.HttpUtility.UrlDecode(img.GetStringAttribute("src"))
                        );
                }
            }
            return transparentImageFiles;
        }

        private static List<string> FindImagesToPreserveResolution(XmlDocument dom)
        {
            var preservedImages = new List<string>();
            foreach (
                var div in dom.SafeSelectNodes(
                        "//div[contains(@class,'marginBox')]//div[contains(@class,'bloom-preserveResolution')]"
                    )
                    .Cast<XmlElement>()
            )
            {
                var style = div.GetAttribute("style");
                if (!string.IsNullOrEmpty(style) && style.Contains(kBackgroundImage))
                {
                    System.Diagnostics.Debug.Assert(
                        div.GetStringAttribute("class").Contains("bloom-backgroundImage")
                    );
                    preservedImages.Add(ExtractFilenameFromBackgroundImageStyleUrl(style));
                }
            }
            foreach (
                var img in dom.SafeSelectNodes(
                        "//div[contains(@class,'marginBox')]//img[contains(@class,'bloom-preserveResolution')]"
                    )
                    .Cast<XmlElement>()
            )
            {
                preservedImages.Add(
                    System.Web.HttpUtility.UrlDecode(img.GetStringAttribute("src"))
                );
            }
            return preservedImages;
        }

        private static string ExtractFilenameFromBackgroundImageStyleUrl(string style)
        {
            var filename = style.Substring(
                style.IndexOf(kBackgroundImage) + kBackgroundImage.Length
            );
            filename = filename.Substring(0, filename.IndexOf("'"));
            return System.Web.HttpUtility.UrlDecode(filename);
        }

        private static void MakeSha(string pathToFileForSha, string folderForSha)
        {
            var sha = Book.Book.ComputeHashForAllBookRelatedFiles(pathToFileForSha);
            var name = "version.txt"; // must match what BloomReader is looking for in NewBookListenerService.IsBookUpToDate()
            // We send the straight string without a BOM in our advertisement, so that needs to be what we write
            // in the file, otherwise, BR never recognizes that it already has the current version.
            RobustFile.WriteAllText(
                Path.Combine(folderForSha, name),
                sha,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            HashOfMostRecentlyCreatedBook = sha;
        }

        public static Dictionary<string, HashSet<string>> BloomPubFontsAndLangsUsed = null;

        public static Book.Book PrepareBookForBloomReader(
            BloomPubPublishSettings settings,
            string bookFolderPath,
            BookServer bookServer,
            TemporaryFolder temp,
            IWebSocketProgress progress,
            bool isTemplateBook,
            string creator = kCreatorBloom
        )
        {
            // MakeDeviceXmatterTempBook needs to be able to copy customCollectionStyles.css etc into parent of bookFolderPath
            // And bloom-player expects folder name to match html file name.
            var htmPath = BookStorage.FindBookHtmlInFolder(bookFolderPath);
            var tentativeBookFolderPath = Path.Combine(
                temp.FolderPath,
                // Windows directory names cannot have trailing periods, but FileNameWithoutExtension can have these.  (BH-6097)
                BookStorage.SanitizeNameForFileSystem(Path.GetFileNameWithoutExtension(htmPath))
            );
            Directory.CreateDirectory(tentativeBookFolderPath);
            var modifiedBook = PublishHelper.MakeDeviceXmatterTempBook(
                bookFolderPath,
                bookServer,
                tentativeBookFolderPath,
                isTemplateBook,
                narrationLanguages: settings?.AudioLanguagesToInclude,
                wantMusic: true,
                // bloom-player has its own @font-face declarations built in for Andika which are compatible with ours.
                // Other fonts that BloomDesktop may serve need to be embedded in the .bloompub file.
                wantFontFaceDeclarations: false
            );

            modifiedBook.SetMotionAttributesOnBody(
                settings?.PublishAsMotionBookIfApplicable == true && modifiedBook.HasMotionPages
            );

            // Although usually tentativeBookFolderPath and modifiedBook.FolderPath are the same, there are some exceptions
            // In the process of bringing a book up-to-date (called by MakeDeviceXmatterTempBook), the folder path may change.
            // For example, it could change if the original folder path contains punctuation marks now deemed dangerous.
            //    The book will be moved to the sanitized version of the file name instead.
            // It can also happen if we end up picking a different version of the title (i.e. in a different language)
            //    than the one written to the .htm file.
            string modifiedBookFolderPath = modifiedBook.FolderPath;

            if (modifiedBook.CollectionSettings.HaveEnterpriseFeatures)
                ProcessQuizzes(modifiedBookFolderPath, modifiedBook.RawDom);

            // Right here, let's maintain the history of what the BloomdVersion signifies to a reader.
            // Version 1 (as opposed to no BloomdVersion field): the bookFeatures property may be
            // used to report features analytics (with earlier bloompub's, the reader must use its own logic)
            modifiedBook.Storage.BookInfo.MetaData.BloomdVersion = 1;

            modifiedBook.Storage.BookInfo.UpdateOneSingletonTag(
                "distribution",
                settings?.DistributionTag
            );
            if (!string.IsNullOrEmpty(settings?.BookshelfTag))
            {
                modifiedBook.Storage.BookInfo.UpdateOneSingletonTag(
                    "bookshelf",
                    settings.BookshelfTag
                );
            }

            if (settings?.RemoveInteractivePages ?? false)
            {
                var activities = modifiedBook
                    .GetPageElements()
                    .Cast<XmlNode>()
                    .Where(x => x is XmlElement elt && HtmlDom.IsActivityPage(elt))
                    .ToArray();
                foreach (var page in activities)
                    page.ParentNode.RemoveChild(page);
            }

            var xmatterLangsToKeep = new HashSet<string>(
                modifiedBook.BookData.GetAllBookLanguageCodes(true, true)
            );
            if (settings?.LanguagesToInclude != null)
            {
                PublishModel.RemoveUnwantedLanguageData(
                    modifiedBook.OurHtmlDom,
                    settings.LanguagesToInclude,
                    shouldPruneXmatter: true,
                    xmatterLangsToKeep
                );
                // For 5.3, we wholesale keep all L2/L3 rules even though this might result in incorrect error messages about fonts. (BL-11357)
                // In 5.4, we hope to clean up all this font determination stuff by using a real browser to determine what is used.
                var cssLangsToKeep = new HashSet<string>(settings.LanguagesToInclude);
                cssLangsToKeep.UnionWith(xmatterLangsToKeep);
                PublishModel.RemoveUnwantedLanguageRulesFromCssFiles(
                    modifiedBook.FolderPath,
                    cssLangsToKeep
                );
            }
            else if (
                Program.RunningHarvesterMode
                && modifiedBook.OurHtmlDom.SelectSingleNode(BookStorage.ComicalXpath) != null
            )
            {
                // This indicates that we are harvesting a book with comic speech bubbles or other overlays (Overlay Tool).
                // For books with overlays, we only publish a single language. It's not currently feasible to
                // allow the reader to switch language in a book with overlays, because typically that requires
                // adjusting the positions of the overlays, and we don't yet support having more than one
                // set of overlay locations in a single book. See BL-7912 for some ideas on how we might
                // eventually improve this. In the meantime, switching language would have bad effects,
                // and if you can't switch language, there's no point in the book containing more than one.
                var languagesToInclude = new string[1] { modifiedBook.BookData.Language1.Tag };
                PublishModel.RemoveUnwantedLanguageData(
                    modifiedBook.OurHtmlDom,
                    languagesToInclude,
                    shouldPruneXmatter: true,
                    xmatterLangsToKeep
                );
            }

            // Do this after processing interactive pages, as they can satisfy the criteria for being 'blank'
            HashSet<string> fontsUsed = null;
            using (var helper = new PublishHelper())
            {
                helper.ControlForInvoke = ControlForInvoke;
                ISet<string> warningMessages = new HashSet<string>();
                helper.RemoveUnwantedContent(
                    modifiedBook.OurHtmlDom,
                    modifiedBook,
                    false,
                    warningMessages,
                    keepPageLabels: settings?.WantPageLabels ?? false
                );
                PublishHelper.SendBatchedWarningMessagesToProgress(warningMessages, progress);
                fontsUsed = helper.FontsUsed;
                BloomPubFontsAndLangsUsed = helper.FontsAndLangsUsed;
            }
            if (!modifiedBook.IsTemplateBook)
            {
                if (settings?.LanguagesToInclude == null)
                    modifiedBook.RemoveBlankPages(null);
                else
                {
                    var languagesToInclude = new HashSet<string>(settings.LanguagesToInclude);
                    if (xmatterLangsToKeep != null)
                        languagesToInclude.UnionWith(xmatterLangsToKeep);
                    modifiedBook.RemoveBlankPages(languagesToInclude);
                }
            }

            // See https://issues.bloomlibrary.org/youtrack/issue/BL-6835.
            RemoveInvisibleImageElements(modifiedBook);
            modifiedBook.Storage.CleanupUnusedSupportFiles( /*isForPublish:*/
                true,
                settings?.AudioLanguagesToExclude
            );
            if (
                !modifiedBook.IsTemplateBook
                && RobustFile.Exists(Path.Combine(modifiedBookFolderPath, "placeHolder.png"))
            )
                RobustFile.Delete(Path.Combine(modifiedBookFolderPath, "placeHolder.png"));
            modifiedBook.RemoveObsoleteAudioMarkup();

            // We want these to run after RemoveUnwantedContent() so that the metadata will more accurately reflect
            // the subset of contents that are included in the .bloompub
            // Note that we generally want to disable features here, but not enable them, especially while
            // running harvester!  See https://issues.bloomlibrary.org/youtrack/issue/BL-8995.
            // BloomReader and BloomPlayer are not using the SignLanguage feature, and it's misleading to
            // assume the existence of videos implies sign language.  There is a separate "Video" feature
            // now that gets set automatically.  (Automated setting of the Blind feature is imperfect, but
            // more meaningful than trying to automate sign language just based on one video existing.)
            var enableSignLanguage = modifiedBook.BookInfo.MetaData.Feature_SignLanguage;
            modifiedBook.UpdateMetadataFeatures(
                isSignLanguageEnabled: enableSignLanguage,
                isTalkingBookEnabled: true, // talkingBook is only ever set automatically as far as I can tell.
                allowedLanguages: null // allow all because we've already filtered out the unwanted ones from the dom above.
            );

            modifiedBook.SetAnimationDurationsFromAudioDurations();

            modifiedBook.OurHtmlDom.SetMedia("bloomReader");
            modifiedBook.OurHtmlDom.AddOrReplaceMetaElement("bloom-digital-creator", creator);
            EmbedFonts(
                modifiedBook,
                progress,
                fontsUsed,
                FontFileFinder.GetInstance(Program.RunningUnitTests)
            );

            var bookFile = BookStorage.FindBookHtmlInFolder(modifiedBook.FolderPath);
            StripImgIfWeCannotFindFile(modifiedBook.RawDom, bookFile);
            StripContentEditableAndTabIndex(modifiedBook.RawDom);
            InsertReaderStylesheet(modifiedBook.RawDom);
            RobustFile.Copy(
                FileLocationUtilities.GetFileDistributedWithApplication(
                    BloomFileLocator.BrowserRoot,
                    "publish",
                    "ReaderPublish",
                    "readerStyles.css"
                ),
                Path.Combine(modifiedBookFolderPath, "readerStyles.css"),
                overwrite: true
            );
            ConvertImagesToBackground(modifiedBook.RawDom);

            AddDistributionFile(modifiedBookFolderPath, creator, settings);

            modifiedBook.Save();

            return modifiedBook;
        }

        /// <summary>
        /// Add a `.distribution` file to the zip which will be reported on for analytics from Bloom Reader.
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-8875.
        /// </summary>
        private static void AddDistributionFile(
            string bookFolder,
            string creator,
            BloomPubPublishSettings settings
        )
        {
            string distributionValue;
            switch (creator)
            {
                case kCreatorHarvester:
                    distributionValue = kDistributionBloomWeb;
                    break;
                case kCreatorBloom:
                    distributionValue = kDistributionBloomDirect;
                    if (settings != null && !string.IsNullOrEmpty(settings.DistributionTag))
                        distributionValue = settings.DistributionTag;
                    break;
                default:
                    throw new ArgumentException("Unknown creator", creator);
            }
            RobustFile.WriteAllText(
                Path.Combine(bookFolder, kDistributionFileName),
                distributionValue
            );
        }

        private static void ProcessQuizzes(string bookFolderPath, XmlDocument bookDom)
        {
            var jsonPath = Path.Combine(bookFolderPath, kQuestionFileName);
            var questionPages = bookDom.SafeSelectNodes(
                "//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'questions')]"
            );
            var questions = new List<QuestionGroup>();
            foreach (var page in questionPages.Cast<XmlElement>().ToArray())
            {
                ExtractQuestionGroups(page, questions);
                page.ParentNode.RemoveChild(page);
            }

            var quizPages = bookDom.SafeSelectNodes(
                "//html/body/div[contains(@class, 'bloom-page') and contains(@class, 'simple-comprehension-quiz')]"
            );
            foreach (var page in quizPages.Cast<XmlElement>().ToArray())
                AddQuizQuestionGroup(page, questions);
            var builder = new StringBuilder("[");
            foreach (var question in questions)
            {
                if (builder.Length > 1)
                    builder.Append(",\n");
                builder.Append(question.GetJson());
            }

            builder.Append("]");
            RobustFile.WriteAllText(jsonPath, builder.ToString());
        }

        // Given a page built using the new simple-comprehension-quiz template, generate JSON to produce the same
        // effect (more-or-less) in BloomReader 1.x. These pages are NOT deleted like the old question pages,
        // so we need to mark the JSON onlyForBloomReader1 to prevent BR2 from duplicating them.
        private static void AddQuizQuestionGroup(XmlElement page, List<QuestionGroup> questions)
        {
            var questionElts = page.SafeSelectNodes(
                ".//div[contains(@class, 'bloom-editable') and contains(@class, 'bloom-content1') and contains(@class, 'QuizQuestion-style')]"
            );
            var answerElts = page.SafeSelectNodes(
                    ".//div[contains(@class, 'bloom-editable') and contains(@class, 'bloom-content1') and contains(@class, 'QuizAnswer-style')]"
                )
                .Cast<XmlElement>()
                .Where(a => !string.IsNullOrWhiteSpace(a.InnerText));
            if (questionElts.Count == 0 || !answerElts.Any())
            {
                return;
            }

            var questionElt = questionElts[0];
            if (string.IsNullOrWhiteSpace(questionElt.InnerText))
                return;
            var lang = questionElt.Attributes["lang"]?.Value ?? "";
            if (string.IsNullOrEmpty(lang))
                return; // paranoia, bloom-editable without lang should not be content1

            var group = new QuestionGroup() { lang = lang, onlyForBloomReader1 = true };
            var question = new Question()
            {
                question = questionElt.InnerText.Trim(),
                answers = answerElts
                    .Select(
                        a =>
                            new Answer()
                            {
                                text = a.InnerText.Trim(),
                                correct = (
                                    (a.ParentNode?.ParentNode as XmlElement)
                                        ?.Attributes["class"]
                                        ?.Value ?? ""
                                ).Contains("correct-answer")
                            }
                    )
                    .ToArray()
            };
            group.questions = new[] { question };

            questions.Add(group);
        }

        private static void StripImgIfWeCannotFindFile(XmlDocument dom, string bookFile)
        {
            var folderPath = Path.GetDirectoryName(bookFile);
            foreach (var imgElt in dom.SafeSelectNodes("//img[@src]").Cast<XmlElement>().ToArray())
            {
                var file = UrlPathString
                    .CreateFromUrlEncodedString(imgElt.Attributes["src"].Value)
                    .PathOnly.NotEncoded;
                if (!RobustFile.Exists(Path.Combine(folderPath, file)))
                {
                    imgElt.ParentNode.RemoveChild(imgElt);
                }
            }
        }

        private static void StripContentEditableAndTabIndex(XmlDocument dom)
        {
            foreach (
                var editableElt in dom.SafeSelectNodes("//div[@contenteditable]").Cast<XmlElement>()
            )
                editableElt.RemoveAttribute("contenteditable");

            // For some reason Bloom adds tabindex="0" to bloom-editables and for some reason we remove them
            // when we go to publish (neither reason are clear to me; "saving space" for the latter?). In any case,
            // we need to keep the tabindex on translationGroups so we preserve audio playback order.
            const string tabindexXpath =
                "//div[@tabindex and not(contains(concat(' ', @class, ' '), ' bloom-translationGroup '))]";
            foreach (var tabIndexDiv in dom.SafeSelectNodes(tabindexXpath).Cast<XmlElement>())
                tabIndexDiv.RemoveAttribute("tabindex");
        }

        /// <summary>
        /// Find every place in the html file where an img element is nested inside a div with class bloom-imageContainer.
        /// Convert the img into a background image of the image container div.
        /// Specifically, make the following changes:
        /// - Copy any data-x attributes from the img element to the div
        /// - Convert the src attribute of the img to style="background-image:url('...')" (with the same source) on the div
        ///    (any pre-existing style attribute on the div is lost)
        /// - Add the class bloom-backgroundImage to the div
        /// - delete the img element
        /// (See oldImg and newImg in unit test CompressBookForDevice_ImgInImgContainer_ConvertedToBackground for an example).
        /// </summary>
        /// <param name="wholeBookHtml"></param>
        /// <returns></returns>
        private static void ConvertImagesToBackground(XmlDocument dom)
        {
            foreach (
                var imgContainer in dom.SafeSelectNodes(
                        "//div[contains(@class, 'bloom-imageContainer')]"
                    )
                    .Cast<XmlElement>()
                    .ToArray()
            )
            {
                var img = imgContainer.ChildNodes
                    .Cast<XmlNode>()
                    .FirstOrDefault(n => n is XmlElement && n.Name == "img");
                if (img == null || img.Attributes["src"] == null)
                    continue;
                // The filename should be already urlencoded since src is a url.
                var src = img.Attributes["src"].Value;
                HtmlDom.SetImageElementUrl(
                    imgContainer,
                    UrlPathString.CreateFromUrlEncodedString(src)
                );
                foreach (XmlAttribute attr in img.Attributes)
                {
                    if (attr.Name.StartsWith("data-"))
                        imgContainer.SetAttribute(attr.Name, attr.Value);
                }

                var classesToAdd = " bloom-backgroundImage";
                // This is a nasty special case; see BL-11712. This class causes images to grow to
                // cover the container, so when we convert to a background image, somehow we need to
                // do the same thing. If we have other similar classes we will have to do it again,
                // and again have two equivalent rules. Maybe we can eventually get rid of converting
                // to background image? Why did we want to, anyway?? Maybe we can copy all classes from
                // the img? But we'd still need duplicate rules.
                if ((img.Attributes["class"]?.Value ?? "").Contains("bloom-imageObjectFit-cover"))
                    classesToAdd += " bloom-imageObjectFit-cover";

                imgContainer.SetAttribute(
                    "class",
                    imgContainer.Attributes["class"].Value + classesToAdd
                );
                imgContainer.RemoveChild(img);
            }
        }

        private static void InsertReaderStylesheet(XmlDocument dom)
        {
            var link = dom.CreateElement("link");
            XmlUtils.GetOrCreateElement(dom, "html", "head").AppendChild(link);
            link.SetAttribute("rel", "stylesheet");
            link.SetAttribute("href", "readerStyles.css");
            link.SetAttribute("type", "text/css");
        }

        /// <summary>
        /// Remove image elements that are invisible due to the book's layout orientation.
        /// </summary>
        /// <remarks>
        /// This code is temporary for Version4.5.  Version4.6 extensively refactors the
        /// electronic publishing code to combine ePUB and BloomReader preparation as much
        /// as possible.
        /// </remarks>
        private static void RemoveInvisibleImageElements(Book.Book book)
        {
            var isLandscape = book.GetLayout().SizeAndOrientation.IsLandScape;
            foreach (var img in book.RawDom.SafeSelectNodes("//img").Cast<XmlElement>().ToArray())
            {
                var src = img.Attributes["src"]?.Value;
                if (string.IsNullOrEmpty(src))
                    continue;
                var classes = img.Attributes["class"]?.Value;
                if (string.IsNullOrEmpty(classes))
                    continue;
                if (
                    isLandscape && classes.Contains("portraitOnly")
                    || !isLandscape && classes.Contains("landscapeOnly")
                )
                {
                    // Remove this img element since it shouldn't be displayed.
                    img.ParentNode.RemoveChild(img);
                }
            }
        }

        /// <summary>
        /// Given a book, typically one in a temporary folder made just for exporting (or testing),
        /// and given the set of fonts found while creating that book and removing hidden elements,
        /// find the files needed for those fonts.
        /// Copy the font file for the normal style of that font family from the system font folder,
        /// if permitted; or post a warning in progress if we can't embed it.
        /// Create an extra css file (fonts.css) which tells the book to find the font files for those font families
        /// in the local folder, and insert a link to it into the book.
        /// </summary>
        /// <param name="fontFileFinder">use new FontFinder() for real, or a stub in testing</param>
        public static void EmbedFonts(
            Book.Book book,
            IWebSocketProgress progress,
            HashSet<string> fontsWanted,
            IFontFinder fontFileFinder
        )
        {
            const string defaultFont = "Andika"; // "Andika" already in BR, don't need to embed or make rule.
            fontsWanted.Remove(defaultFont);
            fontsWanted.Remove("Andika New Basic"); // This will be handled by the Andika font which is available in BR.
            PublishHelper.CheckFontsForEmbedding(
                progress,
                fontsWanted,
                fontFileFinder,
                out List<string> filesToEmbed,
                out HashSet<string> badFonts
            );
            foreach (var file in filesToEmbed)
            {
                // Enhance: do we need to worry about problem characters in font file names?
                var dest = Path.Combine(book.FolderPath, Path.GetFileName(file));
                RobustFile.Copy(file, dest);
            }
            // Create the fonts.css file, which tells the browser where to find the fonts for those families.
            var sb = new StringBuilder();
            foreach (var font in fontsWanted)
            {
                if (badFonts.Contains(font))
                    continue;
                var group = fontFileFinder.GetGroupForFont(font);
                if (group != null)
                {
                    EpubMaker.AddFontFace(sb, font, "normal", "normal", group.Normal);
                }
                // We don't need (or want) a rule to use Andika instead.
                // The reader typically WILL use Andika, because we have a rule making it the default font
                // for the whole body of the document, and BloomReader always has it available.
                // However, it's possible that although we aren't allowed to embed the desired font,
                // the device actually has it installed. In that case, we want to use it.
            }
            RobustFile.WriteAllText(Path.Combine(book.FolderPath, "fonts.css"), sb.ToString());
            // Tell the document to use the new stylesheet.
            book.OurHtmlDom.EnsureStylesheetLinks("fonts.css");
            // Repair defaultLangStyles.css and other places in the output book if needed.
            if (badFonts.Any())
            {
                PublishHelper.FixCssReferencesForBadFonts(book.FolderPath, defaultFont, badFonts);
                PublishHelper.FixXmlDomReferencesForBadFonts(
                    book.OurHtmlDom.RawDom,
                    defaultFont,
                    badFonts
                );
            }
        }

        /// <summary>
        /// Start with a page, which should appear to the user to contain blocks like this,
        /// separated by blank lines:
        /// Question A
        /// answer1
        /// *correct answer2
        /// answer3
        ///
        /// Question B
        /// *correct answer1
        /// answer2
        /// answer3
        ///
        /// The actual html encoding will vary. Each line may be wrapped as a paragraph, or there might be br-type line breaks.
        /// We want to make json like this for each question:
        /// {"question":"Question", "answers": [{"text":"answer1"}, {"text":"correct answer", "correct":true}, {"text":"answer2"}]},
        /// </summary>
        public static void ExtractQuestionGroups(
            XmlElement page,
            List<QuestionGroup> questionGroups
        )
        {
            foreach (
                XmlElement source in page.SafeSelectNodes(
                    ".//div[contains(@class, 'bloom-editable')]"
                )
            )
            {
                var lang = source.Attributes["lang"]?.Value ?? "";
                if (String.IsNullOrEmpty(lang) || lang == "z")
                    continue;
                var group = new QuestionGroup() { lang = lang };
                // this looks weird, but it's just driven by the test cases which are in turn collected
                // from various ways of getting the questions on the page (typing, pasting).
                // See BookReaderFileMakerTests.ExtractQuestionGroups_ParsesCorrectly()
                var separators = new[]
                {
                    "<br />",
                    "</p>",
                    // now add those may not actually show up in firefox, but are in the pre-existing
                    // unit tests, presumably with written-by-hand html?
                    "</br>",
                    "<p />"
                };
                var lines = source.InnerXml.Split(separators, StringSplitOptions.None);
                var questions = new List<Question>();
                Question question = null;
                var answers = new List<Answer>();
                foreach (var line in lines)
                {
                    var cleanLine = line.Replace("<p>", ""); // our split above just looks at the ends of paragraphs, ignores the starts.
                    // Similarly, our split above just looks at the ends of brs, ignores the starts
                    //(separate start vs. end br elements might not occur in real FF tests, see note above).
                    cleanLine = cleanLine.Replace("<br>", "");
                    cleanLine = cleanLine.Replace("\u200c", "");
                    if (String.IsNullOrWhiteSpace(cleanLine))
                    {
                        // If we've accumulated an actual question and answers, put it in the output.
                        // otherwise, we're probably just dealing with leading white space before the first question.
                        if (answers.Any())
                        {
                            question.answers = answers.ToArray();
                            answers.Clear();
                            questions.Add(question);
                            question = null;
                        }
                    }
                    else
                    {
                        var trimLine = cleanLine.Trim();
                        if (question == null)
                        {
                            // If we don't already have a question being built, this first line is the question.
                            question = new Question() { question = trimLine };
                        }
                        else
                        {
                            // We already got the question, and haven't seen a blank line since,
                            // so this is one of its answers.
                            var correct = trimLine.StartsWith("*");
                            if (correct)
                            {
                                trimLine = trimLine.Substring(1).Trim();
                            }
                            answers.Add(new Answer() { text = trimLine, correct = correct });
                        }
                    }
                }
                if (answers.Any())
                {
                    // Save the final question.
                    question.answers = answers.ToArray();
                    questions.Add(question);
                }
                if (questions.Any())
                {
                    // There may well be editable divs, especially automatically generated for active langauges,
                    // which don't have any questions. Skip them. But if we got at least one, save it.
                    group.questions = questions.ToArray();
                    questionGroups.Add(group);
                }
            }
        }
    }
}
