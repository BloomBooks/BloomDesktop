using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Bloom.Collection;
using Bloom.SafeXml;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Book
{
    /// <summary>
    /// Creates the files for a new blank book from a template book
    /// </summary>
    public class BookStarter
    {
        private readonly IFileLocator _fileLocator;
        private readonly BookStorage.Factory _bookStorageFactory;
        private readonly CollectionSettings _collectionSettings;

        public delegate BookStarter Factory(); //autofac uses this

        public BookStarter(
            IChangeableFileLocator fileLocator,
            BookStorage.Factory bookStorageFactory,
            CollectionSettings collectionSettings
        )
        {
            _fileLocator = fileLocator;
            _bookStorageFactory = bookStorageFactory;
            _collectionSettings = collectionSettings;
        }

        public bool TestingSoSkipAddingXMatter { get; set; }

        /// <summary>
        /// Used in unit testing
        /// </summary>
        public bool OnNextRunSimulateFailureMakingBook;

        /// <summary>
        /// Given a template, make a new book
        /// </summary>
        /// <param name="sourceBookFolder"></param>
        /// <param name="parentCollectionPath"></param>
        /// <returns>path to the new book folder</returns>
        public string CreateBookOnDiskFromTemplate(
            string sourceBookFolder,
            string parentCollectionPath
        )
        {
            Logger.WriteEvent(
                "BookStarter.CreateBookOnDiskFromTemplate({0}, {1})",
                sourceBookFolder,
                parentCollectionPath
            );

            // We use the "initial name" to make the initial copy, and it gives us something
            //to name the folder and file until such time as the user enters a title in for the book.
            string initialBookName = GetInitialName(parentCollectionPath);
            var newBookFolder = Path.Combine(parentCollectionPath, initialBookName);
            CopyFolder(sourceBookFolder, newBookFolder);
            BookStorage.RemoveLocalOnlyFiles(newBookFolder);
            //if something bad happens from here on out, we need to delete that folder we just made
            try
            {
                var oldNamedFile = Path.Combine(
                    newBookFolder,
                    Path.GetFileName(GetPathToSingleHtmlFileOrReport(sourceBookFolder))
                );
                // We do now allow for the possibility that the source folder had other HTML files
                // besides a single one whose name matches the folder name. Delete those spurious files.
                // (We don't just use Directory.EnumerateFiles, because there are a few special cases we
                // allow to survive.)
                // (Do this before the rename, just in case the rename would cause a conflict.)
                var otherHtmlFiles = GetHtmFileCandidates(newBookFolder)
                    .Where(x => x != oldNamedFile);
                foreach (var otherFile in otherHtmlFiles)
                {
                    RobustFile.Delete(otherFile);
                }
                var newNamedFile = Path.Combine(newBookFolder, initialBookName + ".htm");
                RobustFile.Move(oldNamedFile, newNamedFile);

                //the destination may change here...
                newBookFolder = SetupNewDocumentContents(sourceBookFolder, newBookFolder);

                if (OnNextRunSimulateFailureMakingBook)
                    throw new ApplicationException("Simulated failure for unit test");
            }
            catch (Exception)
            {
                SIL.IO.RobustIO.DeleteDirectory(newBookFolder, true);
                throw;
            }
            return newBookFolder;
        }

        private string GetPathToSingleHtmlFileOrReport(string folder)
        {
            // As usual, if we find an HTML file at the expected location, we'll just use it, even
            // if there are others.
            var primaryCandidate = BookStorage.GetHtmCandidate(folder);
            if (RobustFile.Exists(primaryCandidate))
                return primaryCandidate;
            var candidates = GetHtmFileCandidates(folder);
            if (candidates.Count() == 1)
                return candidates.First();
            else
            {
                var msg = new StringBuilder();
                msg.AppendLineFormat(
                    "There should only be a single htm(l) file in each folder ({0}). [not counting configuration.html or ReadMe-*.htm]:",
                    folder
                );
                foreach (var f in candidates)
                    msg.AppendLineFormat("    {0}", f);
                ErrorReport.NotifyUserOfProblem(msg.ToString());
                throw new ApplicationException();
            }
        }

        private static IEnumerable<string> GetHtmFileCandidates(string folder)
        {
            // BL-4160 don't put an asterisk after the .htm. It is unnecessary as this search pattern
            // already returns both *.htm and *.html, but NOT *.htm.xyz [returns *.html only for Windows]
            // For both, "*.htm?" should work, but it doesn't return *.htm on Linux [Mono4 bug?].
            var candidates =
                from x in Directory.GetFiles(folder, "*.htm")
                where
                    !(
                        Path.GetFileName(x).ToLowerInvariant().StartsWith("configuration.htm")
                        || IsPathToReadMeHtm(x)
                    )
                select x;
            if (!candidates.Any())
                candidates =
                    from x in Directory.GetFiles(folder, "*.html")
                    where !(Path.GetFileName(x).ToLowerInvariant().StartsWith("configuration.html"))
                    select x;
            return candidates;
        }

        private static bool IsPathToReadMeHtm(string path)
        {
            return Regex.IsMatch(Path.GetFileName(path), "^ReadMe-[a-z]{2,3}(-[A-Z]{2})?\\.htm$");
        }

        private string GetMetaValue(SafeXmlDocument Dom, string name, string defaultValue)
        {
            var nameSuggestion = Dom.SafeSelectNodes("//head/meta[@name='" + name + "']");
            if (nameSuggestion.Length > 0)
            {
                return (nameSuggestion[0]).GetAttribute("content");
            }
            return defaultValue;
        }

        private string SetupNewDocumentContents(string sourceFolderPath, string initialPath)
        {
            // This bookInfo is temporary, just used to make the (also temporary) BookStorage we
            // use here in this method. I don't think it actually matters what its save context is.
            // If it were going to continue to be used, we'd have to get it a real one, because
            // it could later be checked in. But it's going to be writeable for as long as this one
            // is in use.
            var bookInfo = new BookInfo(initialPath, true, new AlwaysEditSaveContext());
            // We know we'll make another instance for this folder later, so we don't need a warning that
            // we already made one.
            bookInfo.AppearanceSettings.AllowLaterInstance(initialPath);
            var storage = _bookStorageFactory(bookInfo);

            bool usingTemplate = bookInfo.IsSuitableForMakingShells;
            bool makingTemplate = bookInfo.IsSuitableForMakingTemplates;
            // If we're not making it from a template or making a template, we're deriving a translation from an existing book
            var makingTranslation = !usingTemplate && !makingTemplate;

            var bookData = new BookData(storage.Dom, _collectionSettings, null);
            UpdateEditabilityMetadata(storage); //Path.GetFileName(initialPath).ToLower().Contains("template"));
            // BL-7614 We don't want a derivative of a book downloaded from a "bookshelf" to have the same bookshelf
            storage.BookInfo.ClearBookshelf();

            // NB: For a new book based on a page template, I think this should remove *everything*,
            // because the rest is in the xmatter.
            // For shells, we'll still have pages.

            // BL-6108: But if this is a template and we remove all the pages and xmatter,
            // there won't be anything left to tell us what the template's preferred layout was,
            // so we'll save that first.
            Layout templateLayout = null;
            if (usingTemplate)
                templateLayout = Layout.FromDom(storage.Dom, Layout.A5Portrait);

            // Remove from the new book any pages labeled as "extra".
            // Typically pages in a template are marked "extra" to indicate that they are options to insert with "Add Page"
            // but not pages (which a few templates have) that should be automatically inserted into every book
            // made from the template.
            // Originally we removed 'extra' pages in all cases, but we haven't actually used the capability
            // of deleting 'extra' pages from translations of shell books, and on the other hand, we did briefly release
            // a version of Bloom that incorrectly left shell book pages so marked. Something like 73 books in our library
            // may have this problem (BL-6392). Since we don't actually need the capability for making translations
            // of shell books, we decided to simply disable it: all pages in a shell book, even those marked
            // 'extra', will be kept in the translation.
            if (!makingTranslation)
            {
                for (
                    var initialPageDivs = storage.Dom.SafeSelectNodes(
                        "/html/body/div[contains(@data-page,'extra')]"
                    );
                    initialPageDivs.Length > 0;
                    initialPageDivs = storage.Dom.SafeSelectNodes(
                            "/html/body/div[contains(@data-page,'extra')]"
                        )
                )
                {
                    initialPageDivs[0].ParentNode.RemoveChild(initialPageDivs[0]);
                }
            }
            else
            {
                // When making a translation of an original move the 'publisher' (if there is one) to 'originalPublisher'.
                storage.BookInfo.MovePublisherToOriginalPublisher();

                StripFeatureAttributeFromComicPages(storage);
            }

            // Here we check for translation groups with the class 'bloom-clearWhenMakingDerivative'. We will clear out
            // any linked BookData material. The "RemoveAllForms" for ISBN below and the similar calls for "copyright"
            // and "versionAcknowledgments" in "TransformCreditPageData()" below are for standard fields.
            // This check is primarily for custom xmatter and brandings.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-11993 for why we decided to keep what amounts to
            // two systems for clearing out data that is unneeded in a derivative.
            // We need to run this method BEFORE removing the existing xmatter, because we look in the xmatter for the
            // class to figure out which BookData keys to remove.
            ClearUnneededOriginalContentFromDerivative(storage.Dom, bookData);

            // For a new book, we discard any old xmatter Ids, so the new book will have its own page IDs.
            // One way this is helpful is caching cover images by page ID, so each book has a different cover page ID.
            XMatterHelper.RemoveExistingXMatter(storage.Dom, new List<string>());

            // BL-4586 Some old books ended up with background-image urls containing XML img tags
            // in the HTML-encoded string. This happened because the coverImage data-book element
            // contained an img tag instead of a bare filename.
            // Normally such a thing would get fixed on loading the book, but if the "old book" in question
            // is a shell downloaded from BloomLibrary.org, Bloom is not allowed to modify the book,
            // so if such a thing exists in this copied book here we will strip it out and replace it with the
            // filename in the img src attribute.
            Book.RemoveImgTagInDataDiv(storage.Dom);

            bookData.RemoveAllForms("ISBN"); //ISBN number of the original doesn't apply to derivatives

            var sizeAndOrientation = Layout.FromDomAndChoices(
                storage.Dom,
                templateLayout ?? Layout.A5Portrait,
                _fileLocator
            );

            ProcessXMatterMetaTags(storage);
            // If we are making a shell (from a template, as opposed to making a translation of a shell),
            // it should not have a pre-determined license. A default will be filled in later.
            // (But, if we're MAKING a template, we want to keep the CC0 from Template Starter.)
            if (usingTemplate && !makingTemplate)
                BookCopyrightAndLicense.RemoveLicense(storage);

            InjectXMatter(initialPath, storage, sizeAndOrientation);

            SetLineageAndId(storage, sourceFolderPath);

            if (makingTranslation)
            {
                storage.EnsureOriginalTitle(); // Before SetBookTitle, so we definitely won't use this book's new empty title
            }

            SetBookTitle(storage, bookData, usingTemplate);

            // See note on "ClearUnneededOriginalContentFromDerivative()" above.
            TransformCreditPageData(
                storage.Dom,
                bookData,
                _collectionSettings,
                storage,
                makingTranslation
            );

            //Few sources will have this set at all. A template picture dictionary is one place where we might expect it to call for, say, bilingual
            int multilingualLevel = int.Parse(
                GetMetaValue(storage.Dom.RawDom, "defaultMultilingualLevel", "1")
            );
            TranslationGroupManager.SetInitialMultilingualSetting(bookData, multilingualLevel);

            var sourceDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(
                sourceFolderPath.CombineForPath(
                    Path.GetFileName(GetPathToSingleHtmlFileOrReport(sourceFolderPath))
                ),
                false
            );

            //If this is a shell book, make elements to hold the vernacular
            foreach (
                SafeXmlElement div in storage.Dom.RawDom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-page')]"
                )
            )
            {
                var sourceDiv =
                    sourceDom.SelectSingleNode("//div[@id='" + div.GetAttribute("id") + "']")
                    as SafeXmlElement;
                SetupIdAndLineage(sourceDiv, div);
                SetupPage(div, bookData);
            }

            ClearAwayDraftText(storage.Dom.RawDom);

            storage.UpdateSupportFiles();
            try
            {
                storage.Save();
            }
            catch (UnauthorizedAccessException e)
            {
                BookStorage.ShowAccessDeniedErrorReport(e);
                // Well, not sure what else to return here, so I guess just let it continue and return storage.FolderPath
            }

            //REVIEW this actually undoes the setting of the initial files name:
            //      storage.UpdateBookFileAndFolderName(_librarySettings);
            return storage.FolderPath;
        }

        /// <summary>
        /// 'internal' for testing only
        /// </summary>
        internal static void ClearUnneededOriginalContentFromDerivative(
            HtmlDom dom,
            BookData bookData
        )
        {
            // This was first needed by Mexico Branch to clear out Printing History (their custom xmatter field)
            // when making a derivative. Since this feature seems like something that might be needed elsewhere
            // eventually, we decided to add a class .bloom-clearWhenMakingDerivative to the .bloom-translationGroup and
            // check for it when deriving a new book.
            // N.B.: If at some point we put this class on a translationGroup that is NOT in xmatter, we'll need to
            // deal with the .bloom-editables inside of these translationGroups. As it stands now, we're about to blow
            // away the xmatter, so they don't matter.
            var groupsToEmpty = dom.RawDom.SafeSelectNodes(
                "//div[contains(@class,'bloom-clearWhenMakingDerivative')]"
            );
            foreach (SafeXmlElement translationGroup in groupsToEmpty)
            {
                var dataBookEl = translationGroup.SelectSingleNode(
                    "div[@data-book and contains(@class,'bloom-editable')]"
                );
                var dataBookKey = dataBookEl?.GetAttribute("data-book");
                if (dataBookKey != null)
                {
                    bookData.RemoveAllForms(dataBookKey);
                }
            }
        }

        // At publish time, we strip out pages with the bloom-enterprise class.
        // But we don't want to do that for comic pages in derivatives. See BL-10586.
        private void StripFeatureAttributeFromComicPages(BookStorage storage)
        {
            foreach (
                SafeXmlElement pageDiv in storage.Dom.RawDom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-page')]"
                )
            )
            {
                if (pageDiv.HasClass("comic"))
                    pageDiv.RemoveAttribute("data-feature");
            }
        }

        /// <summary>
        /// TemplateStarter intentionally makes its children (user's custom templates) have a special xmatter.
        /// But books creates with those custom templates should just use whatever xmatter normal books use,
        /// at least until we allow users to choose different ones, or allow template makers to specify which
        /// xmatter children should use.
        /// Templates should also be able to specify a fixed xmatter for books to use when based on that
        /// template.  (See https://issues.bloomlibrary.org/youtrack/issue/BL-7921.)
        /// </summary>
        private static void ProcessXMatterMetaTags(BookStorage storage)
        {
            // Don't copy the parent's xmatter if they specify it
            storage.Dom.RemoveMetaElement("xmatter");

            // If the parent specifies a required xmatter, use that.
            if (storage.Dom.HasMetaElement("requiredXMatter"))
            {
                storage.Dom.UpdateMetaElement(
                    "xmatter",
                    storage.Dom.GetMetaValue("requiredXMatter", "")
                );
            }

            // But if the parent says what children should use, then use that.
            if (storage.Dom.HasMetaElement("xmatter-for-children"))
            {
                storage.Dom.UpdateMetaElement(
                    "xmatter",
                    storage.Dom.GetMetaValue("xmatter-for-children", "")
                );
            }
            // Children, but not grand-children. So we remove this so the next generation doesn't see it.
            storage.Dom.RemoveMetaElement("xmatter-for-children");
        }

        private void SetLineageAndId(BookStorage storage, string sourceFolderPath)
        {
            string parentId = null;
            string lineage = null;
            if (RobustFile.Exists(Path.Combine(sourceFolderPath, BookInfo.MetaDataFileName)))
            {
                var sourceMetaData = BookMetaData.FromFolder(sourceFolderPath);
                parentId = sourceMetaData.Id;
                lineage = sourceMetaData.BookLineage;
            }
            else
            {
                // No parent meta.json, try for legacy embedded metadata in html
                parentId = GetMetaValue(storage.Dom.RawDom, "bloomBookId", "");
                lineage = GetMetaValue(storage.Dom.RawDom, "bloomBookLineage", "");
                if (string.IsNullOrEmpty(lineage))
                {
                    lineage = GetMetaValue(storage.Dom.RawDom, "bookLineage", ""); //try the old name for this value
                }
            }

            if (!string.IsNullOrEmpty(lineage))
                lineage += ",";
            if (!string.IsNullOrEmpty(parentId))
            {
                storage.BookInfo.BookLineage = lineage + parentId;
            }
            storage.BookInfo.Id = Guid.NewGuid().ToString();
            storage.Dom.RemoveMetaElement("bloomBookLineage"); //old metadata
            storage.Dom.RemoveMetaElement("bookLineage"); // even older name
        }

        /// <summary>
        /// When building on templates, we usually want to have some sample text, but don't let them bleed through to what the user sees
        /// </summary>
        /// <param name="element"></param>
        private static void ClearAwayDraftText(SafeXmlNode element)
        {
            //clear away everything done in language "x"
            var nodesInLangX = new List<SafeXmlNode>();
            nodesInLangX.AddRange(
                from SafeXmlNode x in element.SafeSelectNodes(String.Format("//*[@lang='x']"))
                select x
            );
            foreach (var node in nodesInLangX)
            {
                node.ParentNode.RemoveChild(node);
            }
        }

        /// <summary>
        /// This clears the description from a page as it comes in.
        /// </summary>
        /// <remarks>
        /// In a normal book,
        /// well there is no place to see the description once it is added. But if
        /// we are building a template, then that description will be shown when
        /// someone uses this template (in the Add Page dialog). The description is
        /// something like "A blank page that allows to create custom items"; once
        /// you modify that page, the description stops being accurate.
        /// Now, I can think of scenarios where you'd want to keep description.
        /// E.g. you have an alphabet chart, you add that to another template where hey,
        /// it's still an alphabet chart. This is a judgment call, which is worse.
        /// I'm judging that it's worse to have an out-of-date description than a missing one.
        /// </remarks>
        private static void ClearAwayPageDescription(SafeXmlNode pageDiv)
        {
            //clear away all pageDescription divs except the English one
            var nonEnglishDescriptions = new List<SafeXmlNode>();
            nonEnglishDescriptions.AddRange(
                from SafeXmlNode x in pageDiv.SafeSelectNodes(
                    "//div[contains(@class, 'pageDescription') and @lang != 'en']"
                )
                select x
            );
            foreach (var node in nonEnglishDescriptions)
            {
                node.ParentNode.RemoveChild(node);
            }
            // now leave the English Description as empty; serving as a placeholder if we are making a template
            // and want to go into the html and add a description
            var description = pageDiv.SelectSingleNode(
                "//div[contains(@class, 'pageDescription')]"
            );
            if (description != null)
            {
                description.InnerXml = "";
            }
        }

        private void SetBookTitle(BookStorage storage, BookData bookData, bool usingTemplate)
        {
            //This is what we were trying to do: there was a defaultNameForDerivedBooks meta tag in the html
            //which had no language code. It worked fine for English, e.g., naming new English books
            //"My Book" or "My Dictionary" or whatever.
            //But in other cases, it actually hurt because that English name would be hidden down in the new
            //book, where the author wouldn't see it. But some consumer, using English, would see it, and
            //"My Book" is a pretty dumb name for the carefully prepared book to be listed under.
            //
            //Now, if we are making this book from a shell book, we can keep whatever (title,language) pairs it has.
            //Those will be just fine, for example, if we have English as one of our national languages and so get
            // "vaccinations" for free without having to type that in again.
            //
            //But if we are making this from a *template*, then we *don't* want to keep the various ways to say the
            //name of the template. Seeing "Basic Book" as the name of a resulting shell is not helpful.

            //We just don't have a use for this at all anymore: nice idea, doesn't really work:
            storage.Dom.RemoveMetaElement("defaultNameForDerivedBooks");

            // Clear these out let other code set again when there is a real title.
            storage.BookInfo.Title = "";
            storage.Dom.Title = "";

            //If we're making a book from a template, remove all the titles in all languages
            if (usingTemplate)
            {
                bookData.RemoveAllForms("bookTitle");
            }
            // If we're making a Template, we really want its title to include Template
            // (in hopes the user will keep it at the end so the pages can be used in Add Page)
            if (storage.BookInfo.IsSuitableForMakingShells)
            {
                storage.BookInfo.Title = "My Template";
                storage.Dom.Title = "My Template";
                storage.Dom.SetBookSetting("bookTitle", "en", "My Template");
                // Yes, we want the English word Template in the vernacular Title. Ugly, but that's
                // what determines the file name, and that's what determines whether Add Page will
                // include it.
                storage.Dom.SetBookSetting(
                    "bookTitle",
                    _collectionSettings.Language1.Tag,
                    "My Template"
                );
            }
        }

        private void InjectXMatter(
            string initialPath,
            BookStorage storage,
            Layout sizeAndOrientation
        )
        {
            //now add in the xmatter from the currently selected xmatter pack
            if (!TestingSoSkipAddingXMatter)
            {
                var data = new Dictionary<string, string>();
                Debug.Assert(!string.IsNullOrEmpty(_collectionSettings.Language1.Tag));
                Debug.Assert(!string.IsNullOrEmpty(_collectionSettings.Language2.Tag));
                // Review: this sort of duplicates the knowledge in BookData.WritingSystemAliases
                // Is it worth creating a BookData here? Since we're just starting the new book, it can't
                // yet have any language settings different from the collection.
                data.Add("V", _collectionSettings.Language1.Tag);
                data.Add("N1", _collectionSettings.Language2.Tag);
                data.Add("N2", _collectionSettings.Language3.Tag);

                var helper = new XMatterHelper(
                    storage.Dom,
                    _collectionSettings.XMatterPackName,
                    _fileLocator
                );
                helper.FolderPathForCopyingXMatterFiles = storage.FolderPath;
                helper.InjectXMatter(
                    data,
                    sizeAndOrientation,
                    false,
                    _collectionSettings.Language2.Tag
                );
                //TranslationGroupManager.PrepareDataBookTranslationGroups(storage.Dom,languages);
            }
        }

        private void UpdateEditabilityMetadata(BookStorage storage)
        {
            storage.BookInfo.IsSuitableForMakingShells = storage
                .BookInfo
                .IsSuitableForMakingTemplates;
            // a newly created book is never suitable for making templates, even if its source was.
            storage.BookInfo.IsSuitableForMakingTemplates = false;
            // a newly created book never starts with its name locked. Some file name from the
            // template is unlikely to be useful. (It's arguable whether this belongs in this
            // method, but it's already manipulating the BookInfo, and this somewhat affects
            // editing titles.)
            storage.BookInfo.FileNameLocked = false;
        }

        public static void SetupPage(SafeXmlElement pageDiv, BookData bookData) //, bool inShellMode)
        {
            TranslationGroupManager.PrepareElementsInPageOrDocument(pageDiv, bookData);

            SetLanguageForElementsWithMetaLanguage(pageDiv, bookData);

            // a page might be "extra" as far as the template is concerned, but
            // once a page is inserted into book (which may become a shell), it's
            // just a normal page
            pageDiv.SetAttribute(
                "data-page",
                pageDiv.GetAttribute("data-page").Replace("extra", "").Trim()
            );
            ClearAwayDraftText(pageDiv);
            ClearAwayPageDescription(pageDiv);
        }

        /// <summary>
        /// In xmatter, text fields are normally labeled with a "meta" language code, like "N1" for first national language.
        /// This method detects those and then looks them up, returning the actual language code in use at the moment.
        /// </summary>
        /// <remarks>This is a little uncomfortable in this class, as this feature is not currently used in any
        /// bloom-translationGroup elements.
        /// </remarks>
        public static void SetLanguageForElementsWithMetaLanguage(
            SafeXmlNode elementOrDom,
            BookData bookData
        )
        {
            //			foreach (SafeXmlElement element in elementOrDom.SafeSelectNodes(".//*[@data-metalanguage]"))
            //			{
            //				string lang = "";
            //				string metaLanguage = element.GetAttribute("data-metalanguage").Trim();
            //				switch (metaLanguage)
            //				{
            //					case "V":
            //						lang = settings.Language1Tag;
            //						break;
            //					case "N1":
            //						lang = settings.Language2Tag;
            //						break;
            //					case "N2":
            //						lang = settings.Language3Tag;
            //						break;
            //					default:
            //						var msg = "Element called for meta language '" + metaLanguage + "', which is unrecognized.";
            //						Debug.Fail(msg);
            //						Logger.WriteEvent(msg);
            //						continue;
            //						break;
            //				}
            //				element.SetAttribute("lang", lang);
            //
            //				// As an aside: if the field also has a class "bloom-copyFromOtherLanguageIfNecessary", then elsewhere we will copy from the old
            //				// national language (or regional, or whatever) to this one if necessary, so as not to lose what they had before.
            //
            //			}
        }

        public static void SetupIdAndLineage(
            SafeXmlElement parentPageDiv,
            SafeXmlElement childPageDiv
        )
        {
            //NB: this works even if the parent and child are the same, which is the case when making a new book
            //but not when we're adding an individual template page. (Later: Huh?)

            childPageDiv.SetAttribute("id", Guid.NewGuid().ToString());

            if (parentPageDiv != null) //until we get the xmatter also coming in, xmatter pages will have no parentDiv available
            {
                string parentId = parentPageDiv.GetAttribute("id");
                string parentLineage = parentPageDiv.GetOptionalStringAttribute(
                    "data-pagelineage",
                    string.Empty
                );
                childPageDiv.SetAttribute(
                    "data-pagelineage",
                    (parentLineage + ";" + parentId).Trim(new char[] { ';' })
                );
            }
        }

        private string GetInitialName(string parentCollectionPath)
        {
            var name = BookStorage.SanitizeNameForFileSystem(UntitledBookName);
            return BookStorage.GetUniqueFolderName(parentCollectionPath, name);
        }

        public static string UntitledBookName
        {
            get
            {
                return LocalizationManager.GetString(
                    "EditTab.NewBookName",
                    "Book",
                    "Default file and folder name when you make a new book, but haven't give it a title yet."
                );
            }
        }

        private static void CopyFolder(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);
            foreach (var filePath in Directory.GetFiles(sourcePath))
            {
                //better to not just copy the old thumbnail, as the on in the library may well need to look different
                if (Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant() == "thumbnail")
                    continue;
                if (Path.GetFileNameWithoutExtension(filePath).StartsWith(".")) //.guidsForInstaller.xml
                    continue;
                // We don't want to include any history of the original in the new collection history.
                if (Path.GetFileName(filePath) == "history.db")
                    continue;
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                // We don't need to copy any backups, and we don't want userPrefs because they are likely
                // to include a page number and we want the new book to open at the cover.
                if (
                    new[] { ".jade", ".less", ".md", ".bak", ".userprefs", ".map" }.Any(
                        ex => ex == ext
                    )
                )
                    continue;
                // We don't want the ReadMe's that describe templates copied to the new books.
                if (IsPathToReadMeHtm(filePath))
                    continue;
                RobustFile.Copy(
                    filePath,
                    Path.Combine(destinationPath, Path.GetFileName(filePath))
                );
            }
            foreach (var dirPath in Directory.GetDirectories(sourcePath))
            {
                //any files found under "template" will not be copied. At the moment (Aug 2015), this is only
                //thumbnail svgs, but we could move readme's and such in there
                var directoriesToSkip = new[]
                {
                    "template",
                    Book.ReadMeImagesFolderName.ToLowerInvariant()
                };
                if (!directoriesToSkip.Contains(Path.GetFileName(dirPath).ToLowerInvariant()))
                {
                    CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
                }
            }
        }

        public static void TransformCreditPageData(
            HtmlDom dom,
            BookData bookData,
            CollectionSettings collectionSettings,
            BookStorage storage,
            bool makingTranslation
        )
        {
            // If we're deriving a translation from an existing book,
            // we should save the original copyright and license of that book.
            if (makingTranslation)
                SetOriginalCopyrightAndLicense(dom, bookData, collectionSettings);
            // a new book should never have the copyright holder set, whether it's a template, shell, or translation
            bookData.RemoveAllForms("copyright"); // RemoveAllForms does modify the dom
            // never assume this, even if true for the original, since we're going to clear the copyright info.
            storage.BookInfo.MetaData.UseOriginalCopyright = false;
            storage.BookInfo.Copyright = null; // this might be redundant but let's play safe
            // This is a place to put who it was translated by, usually in a national language.
            // Doesn't apply to templates or (usually) to shells; but a translation can serve again as a shell.
            // In that case, we expect it to be filled in with the new translator's information.
            // Keeping the previous translator's details there is confusing (BL-6271)
            bookData.RemoveAllForms("versionAcknowledgments");
        }

        /// <summary>
        /// Copy the copyright & license info to the originalCopyrightAndLicense,
        /// then remove the copyright so the translator can put in their own if they
        /// want. We retain the license, but the translator is allowed to change that.
        /// If the source is already a translation (already has original copyright or license)
        /// we keep them unchanged.
        /// </summary>
        public static void SetOriginalCopyrightAndLicense(
            HtmlDom dom,
            BookData bookData,
            CollectionSettings collectionSettings
        )
        {
            // If it already has some of this information, just keep it.
            if (bookData.BookIsDerivative())
            {
                return; //leave the original there.
            }
            var copyrightNotice = BookCopyrightAndLicense
                .GetMetadata(dom, bookData)
                .CopyrightNotice;
            bookData.Set(
                "originalLicenseUrl",
                XmlString.FromXml(BookCopyrightAndLicense.GetLicenseUrl(dom)),
                "*"
            );
            bookData.Set("originalCopyright", XmlString.FromUnencoded(copyrightNotice), "*");
            bookData.Set(
                "originalLicenseNotes",
                XmlString.FromXml(dom.GetBookSetting("licenseNotes").GetFirstAlternative()),
                "*"
            );
        }

        internal static void UniqueifyIds(SafeXmlElement pageDiv)
        {
            // find any img.id attributes and replace them with new ids, because we cannot have two elements with the same id in a book
            // we might want to do this for other elements as well, but audio file names may be tied to the id, and would have already
            // been uniquified by the time we get here.
            foreach (SafeXmlElement element in pageDiv.SafeSelectNodes(".//img[@id]"))
            {
                element.SetAttribute("id", Guid.NewGuid().ToString());
            }
        }
    }
}
