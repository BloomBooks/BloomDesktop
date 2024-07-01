using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Properties;
using BookInstance = Bloom.Book.Book;
using Bloom.WebLibraryIntegration;
using SIL.Windows.Forms.ClearShare;
using BloomTemp;
using System.IO;
using System.Diagnostics;
using Bloom.Utils;
using Bloom.web;
using SIL.IO;
using SIL.Progress;
using Bloom.Book;
using System.Globalization;
using Bloom.ImageProcessing;
using System.Drawing;
using Bloom.Collection;
using Bloom.ToPalaso;
using Newtonsoft.Json.Linq;
using SIL.Reporting;

namespace Bloom.Publish.BloomLibrary
{
    /// <summary>
    /// Puts all of the business logic of whether a book's metadata is complete enough to publish and handling some of the login
    /// process in one place so that the regular single-book Publish tab upload and the command line bulk upload can use the
    /// same verification logic.
    /// </summary>
    public class BloomLibraryPublishModel
    {
        private LicenseInfo _license;
        private readonly BookUpload _uploader;
        private readonly PublishModel _publishModel;

        public static bool BookUploaded;
        public static string BookUploadedId;

        public BloomLibraryPublishModel(BookUpload uploader, BookInstance book, PublishModel model)
        {
            Book = book;
            InitializeLanguages(Book);

            _uploader = uploader;
            _publishModel = model;

            var licenseMetadata = Book.GetLicenseMetadata();
            // This is usually redundant, but might not be on old books where the license was set before the new
            // editing code was written.
            Book.SetMetadata(licenseMetadata);
            _license = licenseMetadata.License;

            EnsureBookId();
        }

        internal BookInstance Book { get; }

        internal string LicenseRights => _license.RightsStatement ?? string.Empty;

        // ReSharper disable once InconsistentNaming
        internal string CCLicenseUrl => (_license as CreativeCommonsLicense)?.Url;

        internal string LicenseToken => _license.Token.ToUpperInvariant();

        internal string Credits => Book.BookInfo.Credits;

        internal string Title => Book.BookInfo.Title;

        internal string Copyright => Book.BookInfo.Copyright;

        internal bool HasOriginalCopyrightInfo => Book.HasOriginalCopyrightInfo;

        internal bool IsTemplate => Book.BookInfo.IsSuitableForMakingShells;

        internal string Summary
        {
            get { return Book.BookInfo.Summary; }
            set
            {
                Book.BookInfo.Summary = value;
                Book.BookInfo.Save();
            }
        }

        /// <summary>
        /// Gets a user-friendly language name.
        /// </summary>
        internal string PrettyLanguageName(string code)
        {
            return Book.PrettyPrintLanguage(code);
        }

        // This is awkward. We really just want to always get the latest license info.
        // But Book.GetLicenseMetadata() is not a trivial operation.
        // The original code assumed that the book's license would not change during the lifetime of the model.
        // But now the user can open the CopyrightAndLicenseDialog from Publish tab.
        internal void EnsureUpToDateLicense()
        {
            _license = Book.GetLicenseMetadata().License;
        }

        /// <summary>
        /// Whether the most recent PDF generation succeeded.
        /// </summary>
        public bool PdfGenerationSucceeded
        {
            get { return _publishModel.PdfGenerationSucceeded; }
        }

        private void EnsureBookId()
        {
            if (string.IsNullOrEmpty(Book.BookInfo.Id))
            {
                Book.BookInfo.Id = Guid.NewGuid().ToString();
            }
        }

        internal bool IsBookPublicDomain =>
            _license?.Url != null
            && _license.Url.StartsWith("http://creativecommons.org/publicdomain/zero/");

        public const string kNameOfDownloadForEditFile = "downloadForEdit.json";

        internal dynamic GetConflictingBookInfoFromServer(int index)
        {
            // Include language information so we can get the names.
            var books = _uploader.GetBooksOnServer(Book.BookInfo.Id, includeLanguageInfo: true);
            books = SortConflictingBooksFromServer(
                books,
                Book.FolderPath,
                Settings.Default.WebUserId,
                Book.BookInfo.PHashOfFirstContentImage,
                bookId => _uploader.GetBookPermissions(bookId).reupload
            );
            if (index >= books.Length)
                return null;
            var result = books[index] as JObject;
            // set count of books as a field on result
            result.Add("count", books.Length);
            var databaseId = result["id"].ToString();
            var permissions = _uploader.GetBookPermissions(databaseId);
            result.Add("permissions", permissions);
            return result;
        }

        public static JObject GetDownloadForEditData(string pathToCollectionFolder)
        {
            var filePath = Path.Combine(pathToCollectionFolder, kNameOfDownloadForEditFile);
            if (RobustFile.Exists(filePath))
            {
                return JObject.Parse(RobustFile.ReadAllText(filePath));
            }
            return null;
        }

        /// <summary>
        /// If we have multiple conflicting books, we want to sort them in a way that makes sense to the user.
        /// If we're in a collection that was made for editing one particular book, and this is the book,
        /// put that one first.
        /// Then put books uploaded by the current user with the same phash as the book being uploaded next.
        /// Then any other books uploaded by the current user.
        /// Next, any other books this user has permission to reupload (again ones with the same phash first).
        /// Finally, any other books, again ones with the same phash first.
        /// This logic is pulled out as a static method to enable unit testing.
        /// The function canUpload can be replaced for testing.
        /// </summary>
        /// <remarks>It's potentially quite inefficient that we retrieve the permissions of all the books
        /// that don't have the right uploader (one at a time) to sort them, then retrieve the
        /// permissions of the current book again to include in the next-level-up result, and do it all again if the
        /// selected book index changes. We could cache the permission data somewhere and reuse it. Or we could change
        /// the API so the whole sorted collection is sorted once and then passed to the UI side.
        /// I was trying to avoid premature optimization and doing any more work than necessary
        /// to handle the multiple-collision scenario, since we hope it is very rare.</remarks>
        internal static dynamic[] SortConflictingBooksFromServer(
            dynamic[] books,
            string pathToBookFolder,
            string userEmail,
            string phash,
            Func<string, bool> canUpload
        )
        {
            if (books.Length < 2)
                return books;
            var remaining = books.ToList();
            var bookList = new List<dynamic>();
            var bookOfCollectionData = GetDownloadForEditData(
                Path.GetDirectoryName(pathToBookFolder)
            );
            if (bookOfCollectionData != null)
            {
                var databaseId = bookOfCollectionData["databaseId"];
                var instanceId = bookOfCollectionData["instanceId"];
                var bookFolder = bookOfCollectionData["bookFolder"]?.ToString();
                if (bookFolder == pathToBookFolder.Replace("\\", "/"))
                {
                    var matchingBook = books.FirstOrDefault(
                        b => b["id"] == databaseId && b["instanceId"] == instanceId
                    );
                    if (matchingBook != null)
                    {
                        bookList.Add(matchingBook);
                        remaining.Remove(matchingBook);
                    }
                }
            }

            var rightUploader = remaining.Where(b => b.uploader?.email == userEmail).ToList();
            var rightUploaderAndPHash = rightUploader
                .Where(b => b["phashOfFirstContentImage"]?.ToString() == phash)
                .ToArray();
            foreach (var book in rightUploaderAndPHash)
            {
                bookList.Add(book);
                remaining.Remove(book);
                rightUploader.Remove(book);
            }
            // Right uploader, but wrong phash
            foreach (var book in rightUploader)
            {
                bookList.Add(book);
                remaining.Remove(book);
            }
            // remaining are not the collection book and uploaded by someone else. Can we upload them?
            var uploadable = remaining.Where(b => canUpload(b["id"]?.ToString())).ToList();
            // Do any of those have the right hash?
            var uploadableWithRightHash = uploadable
                .Where(b => b["phashOfFirstContentImage"]?.ToString() == phash)
                .ToArray();
            foreach (var book in uploadableWithRightHash)
            {
                bookList.Add(book);
                remaining.Remove(book);
                uploadable.Remove(book);
            }
            // These are ones we can upload, but they have the wrong hash.
            foreach (var book in uploadable)
            {
                bookList.Add(book);
                remaining.Remove(book);
            }
            // The rest are not the collection book, uploaded by someone else, and we can't upload them.
            var remainingWithRightHash = remaining
                .Where(b => b["phashOfFirstContentImage"]?.ToString() == phash)
                .ToArray();
            foreach (var book in remainingWithRightHash)
            {
                bookList.Add(book);
                remaining.Remove(book);
            }
            // and finally the ones with nothing to make them attractive except the instanceId
            foreach (var book in remaining)
            {
                bookList.Add(book);
            }

            return bookList.ToArray();
        }

        internal bool IsTitleOKToPublish => Book.HasL1Title(); // Even picture books need a title (and templates have a title).

        /// <summary>
        /// The model alone cannot determine whether a book is OK to upload, because the language requirements
        /// are different for single book upload and bulk upload (which use this same model).
        /// For bulk upload, a book is okay to upload if this property is true AND it has ANY language
        /// with complete data (meaning all non-xmatter fields have something in the language).
        /// For single book upload, a book is okay to upload if this property is true AND it is EITHER
        /// OkToUploadWithNoLanguages OR the user has checked a language checkbox.
        /// This property just determines whether the book's metadata is complete enough to publish
        /// LoggedIn is not part of this, because the two users of the model check for login status in different
        /// parts of the process.
        /// </summary>
        internal bool MetadataIsReadyToPublish =>
            // Copyright info is not required if the book has been put in the public domain
            // Also, (BL-5563) if there is an original copyright and we're publishing from a source collection,
            // we don't need to have a copyright.
            (
                IsBookPublicDomain
                || !string.IsNullOrWhiteSpace(Copyright)
                || HasOriginalCopyrightInfo
            ) && IsTitleOKToPublish;

        internal bool LoggedIn => _uploader.LoggedIn;

        /// <summary>
        /// Stored Web user Id
        /// </summary>
        ///  Best not to store its own value, because the username/password can be changed if the user logs into a different account.
        internal string WebUserId
        {
            get { return Settings.Default.WebUserId; }
        }

        /// <summary>
        /// We would like users to be able to publish picture books that don't have any text.  Historically, we've required
        /// non-empty books with text unless the book is marked as being a template.  This restriction is too severe, so for
        /// now, we require either a template or a pure picture book.  (No text boxes apart from image description boxes on
        /// content pages.)  (See https://issues.bloomlibrary.org/youtrack/issue/BL-7514 for the initial user request, and
        /// https://issues.bloomlibrary.org/youtrack/issue/BL-7799 for why we made this property non-trivial.)
        /// </summary>
        internal bool OkToUploadWithNoLanguages =>
            Book.BookInfo.IsSuitableForMakingShells || Book.HasOnlyPictureOnlyPages();

        /// <returns>On success, returns the book objectId; on failure, returns empty string</returns>
        internal string UploadOneBook(
            BookInstance book,
            IProgress progress,
            PublishModel publishModel,
            bool excludeMusic,
            string existingBookObjectIdOrNull,
            bool changeUploader
        )
        {
            using (
                var tempFolder = new TemporaryFolder(
                    Path.Combine("BloomUpload", Path.GetFileName(book.FolderPath))
                )
            )
            {
                BookUploaded = true; // flag that an upload has occurred
                BookUploadedId = book.BookInfo.Id; // single book uploaded: only one needs to be updated
                BookUpload.PrepareBookForUpload(
                    ref book,
                    _publishModel.BookServer,
                    tempFolder.FolderPath,
                    progress
                );
                var bookParams = new BookUploadParameters
                {
                    ExcludeMusic = excludeMusic,
                    PreserveThumbnails = false,
                };
                return _uploader.FullUpload(
                    book,
                    progress,
                    publishModel,
                    bookParams,
                    existingBookObjectIdOrNull,
                    changeUploader
                );
            }
        }

        internal void LogIn()
        {
            BloomLibraryAuthentication.LogIn();
        }

        internal void LogOut()
        {
            _uploader.Logout();
        }

        internal LicenseState LicenseType
        {
            get
            {
                if (_license is CreativeCommonsLicense)
                {
                    return LicenseState.CreativeCommons;
                }
                if (_license is NullLicense)
                {
                    return LicenseState.Null;
                }
                return LicenseState.Custom;
            }
        }

        /// <summary>
        /// Used by bulk uploader to tell the user why we aren't uploading their book.
        /// </summary>
        /// <returns></returns>
        public string GetReasonForNotUploadingBook()
        {
            const string couldNotUpload = "Could not upload book. ";
            // It might be because we're missing required metadata.
            if (!MetadataIsReadyToPublish)
            {
                if (!IsTitleOKToPublish)
                {
                    return couldNotUpload + "Required book Title is empty.";
                }
                if (string.IsNullOrWhiteSpace(Copyright))
                {
                    return couldNotUpload + "Required book Copyright is empty.";
                }
            }
            // Or it might be because a non-template book doesn't have any 'complete' languages.
            // every non-x - matter field which contains text in any language contains text in this
            return couldNotUpload
                + "A non-template book needs at least one language where every non-xmatter field contains text in that language.";
        }

        public void UpdateBookMetadataFeatures(bool isTalkingBook, bool isSignLanguage)
        {
            var allowedLanguages = Book.BookInfo.PublishSettings.BloomLibrary.TextLangs
                .IncludedLanguages()
                .Union(Book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages());

            Book.UpdateMetadataFeatures(
                isTalkingBookEnabled: isTalkingBook,
                isSignLanguageEnabled: isSignLanguage,
                allowedLanguages
            );
        }

        public void SaveTextLanguageSelection(string langCode, bool include)
        {
            Book.BookInfo.PublishSettings.BloomLibrary.TextLangs[langCode] = include
                ? InclusionSetting.Include
                : InclusionSetting.Exclude;
            Book.BookInfo.Save(); // We updated the BookInfo, so need to persist the changes. (but only the bookInfo is necessary, not the whole book)
        }

        public void SaveAudioLanguageSelection(bool include)
        {
            // Currently, audio language selection is all or nothing for Bloom Library publish
            foreach (
                var langCode in Book.BookInfo.PublishSettings.BloomLibrary.AudioLangs.Keys.ToList()
            )
            {
                Book.BookInfo.PublishSettings.BloomLibrary.AudioLangs[langCode] = include
                    ? InclusionSetting.Include
                    : InclusionSetting.Exclude;
            }
            Book.BookInfo.Save(); // We updated the BookInfo, so need to persist the changes. (but only the bookInfo is necessary, not the whole book)
        }

        public static void InitializeLanguages(BookInstance book)
        {
            var allLanguages = book.AllPublishableLanguages(
                // True up to 5.6. Things are a bit tricky if xmatter contains L2 and possibly L3 data.
                // We always include that xmatter data if it is needed for the book to be complete.
                // But if nothing in the book content is in those languages, we don't list them as
                // book languages, either in Blorg or in Bloom Player. To be consistent, we don't
                // even want to have check boxes for them (they would not have any effect, since there
                // is nothing in the book in those languages that is optional to include).
                includeLangsOccurringOnlyInXmatter: false
            );

            var bookInfo = book.BookInfo;
            Debug.Assert(bookInfo?.MetaData != null, "Precondition: MetaData must not be null");

            InitializeTextLanguages(book, allLanguages);

            InitializeAudioLanguages(bookInfo, allLanguages.Select(x => x.Key));

            InitializeSignLanguage(bookInfo, book.CollectionSettings.SignLanguageTag);

            // The metadata may have been changed, so save it.
            bookInfo.Save();
        }

        private static void InitializeTextLanguages(
            BookInstance book,
            Dictionary<string, bool> allLanguages
        )
        {
            // reinitialize our list of which languages to publish, defaulting to the ones that are complete.
            foreach (var kvp in allLanguages)
            {
                var langCode = kvp.Key;
                var isRequiredLang = book.IsRequiredLanguage(langCode);
                var isCollectionLang = book.IsCollectionLanguage(langCode);

                // First, check if the user has already explicitly set the value. If so, we'll just use that value and be done.
                if (
                    book.BookInfo.PublishSettings.BloomLibrary.TextLangs.TryGetValue(
                        langCode,
                        out InclusionSetting checkboxValFromSettings
                    )
                )
                {
                    if (checkboxValFromSettings.IsSpecified())
                    {
                        // ...unless the check box will be disabled and checked, in which case we better make sure it isn't Exclude
                        if (isRequiredLang && checkboxValFromSettings == InclusionSetting.Exclude)
                            book.BookInfo.PublishSettings.BloomLibrary.TextLangs[langCode] =
                                InclusionSetting.IncludeByDefault;

                        continue;
                    }
                }

                // Nope, either no value exists or the value was some kind of default value.
                // Compute (or recompute) what the value should default to.
                bool shouldBeChecked = (kvp.Value && isCollectionLang) || isRequiredLang;

                var newInitialValue = shouldBeChecked
                    ? InclusionSetting.IncludeByDefault
                    : InclusionSetting.ExcludeByDefault;
                book.BookInfo.PublishSettings.BloomLibrary.TextLangs[langCode] = newInitialValue;
            }
            // Get rid of settings for languages that the book doesn't have, so we don't publish a claim
            // that it has that data. This is rarely needed, currently only if the book was previously
            // prepared for publication with data in a language and then all of it was deleted.
            foreach (
                var lang in book.BookInfo.PublishSettings.BloomLibrary.TextLangs.Keys.ToArray()
            )
            {
                if (!allLanguages.ContainsKey(lang))
                {
                    book.BookInfo.PublishSettings.BloomLibrary.TextLangs.Remove(lang);
                }
            }
        }

        private static void InitializeAudioLanguages(
            BookInfo bookInfo,
            IEnumerable<string> allLanguageTags
        )
        {
            // This is tricky, because an earlier version of our UI did not support different settings for different languages.
            // Thus, an older version of this code only did this init the first time, when AudioLangs was null.
            // However, at least one book (BL-11784) got into a state where AudioLangs is an empty set.
            // Then there is NO item in the set to be switched on or off, so effectively, it remains off.
            // (The old IncludeAudio was true if at least one is Include or IncludeByDefault).
            // It is therefore necessary, minimally, to make sure the set has at least one language
            // if allLangCodes has any. But properly, it is intended to have a value for every language in
            // allLangCodes...just in the old version they would all be the same.
            // It seems more future-proof to add any languages that are missing, while not changing the
            // setting for any we already have. But then, what setting should we use for any language
            // that is missing? To help preserve the old behavior, if all the old ones are exclude
            // we'll go with that, otherwise, include.
            var settingForNewLang = InclusionSetting.IncludeByDefault;
            if (
                bookInfo.PublishSettings.BloomLibrary.AudioLangs.Any()
                && bookInfo.PublishSettings.BloomLibrary.AudioLangs.All(
                    kvp => !kvp.Value.IsIncluded()
                )
            )
                settingForNewLang = InclusionSetting.ExcludeByDefault;

            foreach (var langCode in allLanguageTags)
            {
                if (!bookInfo.PublishSettings.BloomLibrary.AudioLangs.ContainsKey(langCode))
                    bookInfo.PublishSettings.BloomLibrary.AudioLangs[langCode] = settingForNewLang;
            }
        }

        private static void InitializeSignLanguage(
            BookInfo bookInfo,
            string collectionSignLanguageTag
        )
        {
            // User may have unset or modified the sign language for the collection in which case we need to exclude the old one it if it was previously included.
            foreach (
                var includedSignLangCode in bookInfo.PublishSettings.BloomLibrary.SignLangs
                    .IncludedLanguages()
                    .ToList()
            )
            {
                if (includedSignLangCode != collectionSignLanguageTag)
                {
                    bookInfo.PublishSettings.BloomLibrary.SignLangs[includedSignLangCode] =
                        InclusionSetting.ExcludeByDefault;
                }
            }
            // Include the collection sign language by default unless the user set it definitely.
            if (!string.IsNullOrEmpty(collectionSignLanguageTag))
            {
                if (
                    !bookInfo.PublishSettings.BloomLibrary.SignLangs.ContainsKey(
                        collectionSignLanguageTag
                    )
                    || bookInfo.PublishSettings.BloomLibrary.SignLangs[collectionSignLanguageTag]
                        == InclusionSetting.ExcludeByDefault
                )
                {
                    bookInfo.PublishSettings.BloomLibrary.SignLangs[collectionSignLanguageTag] =
                        InclusionSetting.IncludeByDefault;
                }
            }
        }

        public void ClearSignLanguageToPublish()
        {
            foreach (
                var includedSignLangCode in Book.BookInfo.PublishSettings.BloomLibrary.SignLangs
                    .IncludedLanguages()
                    .ToList()
            )
            {
                Book.BookInfo.PublishSettings.BloomLibrary.SignLangs[includedSignLangCode] =
                    InclusionSetting.Exclude;
            }
            Book.BookInfo.Save();
        }

        public void SetOnlySignLanguageToPublish(string langCode)
        {
            Book.BookInfo.PublishSettings.BloomLibrary.SignLangs = new Dictionary<
                string,
                InclusionSetting
            >
            {
                [langCode] = InclusionSetting.Include
            };
            Book.BookInfo.Save();
        }

        public bool IsPublishSignLanguage()
        {
            return Book.BookInfo.PublishSettings.BloomLibrary.SignLangs.IncludedLanguages().Any();
        }

        public bool L1SupportsVisuallyImpaired
        {
            get
            {
                return Book.BookInfo.MetaData.Feature_Blind_LangCodes.Contains(Book.Language1Tag);
            }
            set
            {
                Book.UpdateBlindFeature(value);
                Book.BookInfo.Save();
            }
        }

        public string CheckBookBeforeUpload()
        {
            return new LicenseChecker().CheckBook(Book, TextLanguagesToUpload.ToArray());
        }

        public IEnumerable<string> TextLanguagesToUpload =>
            Book.BookInfo.PublishSettings.BloomLibrary.TextLangs
                .Where(l => l.Value.IsIncluded())
                .Select(l => l.Key);

        public IEnumerable<string> TextLanguagesToAdvertiseOnBloomLibrary =>
            Book.GetTextLanguagesToAdvertiseOnBloomLibrary(TextLanguagesToUpload);

        public void AddHistoryRecordForLibraryUpload(string url)
        {
            Book.AddHistoryRecordForLibraryUpload(url);
        }

        public void BulkUpload(string rootFolderPath, IProgress progress)
        {
            var target = BookUpload.UseSandbox
                ? UploadDestination.Development
                : UploadDestination.Production;

            var bloomExePath = Program.BloomExePath;
            var command =
                $"\"{bloomExePath}\" upload \"{rootFolderPath}\" -u {WebUserId} -d {target}";
            if (SIL.PlatformUtilities.Platform.IsLinux)
                command = $"/opt/mono5-sil/bin/mono {command}";

            // The Bloom process run as a command line tool for bulk upload is safe for using the current culture.
            // We don't wait for this to finish, so we don't use the CommandLineRunner methods.
            ProcessStartInfo startInfo;
            if (SIL.PlatformUtilities.Platform.IsWindows)
            {
                startInfo = new ProcessStartInfo()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k {MiscUtils.EscapeForCmd(command)}",
                    WorkingDirectory = Path.GetDirectoryName(bloomExePath)
                };
            }
            else
            {
                string program = GetLinuxTerminalProgramAndAdjustCommand(ref command);
                if (String.IsNullOrEmpty(program))
                {
                    progress.WriteMessage(
                        "Cannot bulk upload because unable to find terminal window for output messages."
                    );
                    return;
                }
                startInfo = new ProcessStartInfo()
                {
                    FileName = program,
                    Arguments = command,
                    WorkingDirectory = Path.GetDirectoryName(bloomExePath)
                };
                // LD_PRELOAD is a Linux environment variable for a shared library that should be loaded before any other library is
                // loaded by a program that is starting up.  It is rarely needed, but the mozilla code used by Geckofx is one place
                // where this feature is used, specifically to load a xulrunner patch (libgeckofix.so) that must be in place before
                // xulrunner can be initialized for GeckoFx60 on Linux.  This must be in place in the environment before launching
                // any process (such as Bloom, here) that will initialize xulrunner, but may cause problems for other programs so
                // it is best not to have it in the environment unless we know it is needed.  In particular having LD_PRELOAD set to
                // load libgeckofix.so is known to cause problems when running some programs (possibly only BloomPdfMaker.exe) using
                // CommandLineRunner.  To guard against this Program.Main() removes it from the environment, but here we need to
                // temporarily restore it so it can be inherited by the instance of Bloom we are about to launch. Fortunately, it's
                // easy to reconstruct.
                var xulRunner = Environment.GetEnvironmentVariable("XULRUNNER");
                if (!String.IsNullOrEmpty("xulRunner"))
                    Environment.SetEnvironmentVariable("LD_PRELOAD", $"{xulRunner}/libgeckofix.so");
            }

            BookUploaded = true; // Flag that an upload has occurred
            BookUploadedId = null; // Multiple books have been uploaded, so a single id doesn't exist: all may need to be updated

            ProcessExtra.StartInFront(startInfo);
            progress.WriteMessage("Starting bulk upload in a terminal window...");
            progress.WriteMessage(
                "This process will skip books if it can tell that nothing has changed since the last bulk upload."
            );
            progress.WriteMessage(
                "When the upload is complete, there will be a file named 'BloomBulkUploadLog.txt' in your collection folder."
            );
            var url =
                $"{BloomLibraryUrls.BloomLibraryUrlPrefix}/{Book.CollectionSettings.DefaultBookshelf}";
            progress.WriteMessage("Your books will show up at {0}", url);
            if (SIL.PlatformUtilities.Platform.IsLinux) // LD_PRELOAD interferes with CommandLineRunner and GeckoFx60 on Linux
                Environment.SetEnvironmentVariable("LD_PRELOAD", null);
        }

        private string QuoteQuotes(string command)
        {
            return command.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private string GetLinuxTerminalProgramAndAdjustCommand(ref string command)
        {
            // See https://askubuntu.com/questions/484993/run-command-on-anothernew-terminal-window

            if (RobustFile.Exists("/usr/bin/gnome-terminal")) // standard for GNOME (Ubuntu/Wasta)
            {
                // /usr/bin/gnome-terminal -- /bin/bash -c "bloom upload \"folder\" -u user -d dest; read line"
                command = $"-- /bin/bash -c \"{QuoteQuotes(command)}; read line\"";
                return "/usr/bin/gnome-terminal";
            }
            if (RobustFile.Exists("/usr/bin/terminator")) // popular alternative
            {
                // /usr/bin/terminator -x /bin/bash -c "bloom upload \"folder\" -u user -d dest; read line"
                command = $"-x /bin/bash -c \"{QuoteQuotes(command)}; read line\"";
                return "/usr/bin/terminator";
            }
            if (RobustFile.Exists("/usr/bin/xfce4-terminal")) // standard for XFCE4 (XUbuntu)
            {
                // /usr/bin/xterm -hold -x /bin/bash -c "bloom upload \"folder\" -u user -d dest"
                command = $"-T \"Bloom upload\" --hold -x /bin/bash -c \"{QuoteQuotes(command)}\"";
                return "/usr/bin/xfce4-terminal";
            }
            if (RobustFile.Exists("/usr/bin/xterm")) // antique original (slightly better than nothing)
            {
                // /usr/bin/xterm -hold -x /bin/bash -c "bloom upload \"folder\" -u user -d dest"
                command = $"-T \"Bloom upload\" -hold -e /bin/bash -c \"{QuoteQuotes(command)}\"";
                return "/usr/bin/xterm";
            }
            // Neither konsole nor qterminal will launch with Bloom.  The ones above have been tested on Wasta 20.
            // symbol lookup error: /usr/lib/x86_64-linux-gnu/qt5/plugins/styles/libqgtk2style.so: undefined symbol: gtk_combo_box_entry_new
            // I suspect because they're still linking with GTK2 while Bloom has to use GTK3 with Geckofx60.

            // Give up.
            return null;
        }

        public dynamic GetUploadCollisionDialogProps(
            IEnumerable<string> languagesToAdvertise,
            bool signLanguageFeatureSelected,
            int index
        )
        {
            var existingBookInfo = GetConflictingBookInfoFromServer(index);

            if (existingBookInfo == null)
            {
                return new { shouldShow = false };
            }

            // Earlier code tried to find an existing thumbnail file, but it wasn't always up to date by the time it was needed.
            // This api builds it on the fly from the cover image so it is always current.
            var newThumbPath = "/bloom/api/publish/thumbnail";
            var newTitle = Book.TitleBestForUserDisplay;
            ConvertLanguageListsToNames(
                languagesToAdvertise,
                Book.BookData,
                existingBookInfo.languages,
                signLanguageFeatureSelected ? Book.CollectionSettings.SignLanguage : null,
                out string[] newLanguages,
                out string[] existingLanguages
            );

            var updatedDateTime = (DateTime)existingBookInfo.updatedAt;
            var createdDateTime = (DateTime)existingBookInfo.createdAt;
            // Find the best title available (BL-11027)
            // Users can click on this title to bring up the existing book's page.
            var existingTitle = existingBookInfo.titleFromUpload?.Value;
            if (String.IsNullOrEmpty(existingTitle))
            {
                // If title is undefined (which should not be the case), then use the first title from allTitles.
                var allTitles = existingBookInfo.titles ?? Array.Empty<object>(); //?.Value;
                if (allTitles.Length > 0)
                {
                    // Earlier code attempted to find a non-empty one. I don't think it's worth it.
                    // titleFromUpload should never be empty; Titles in allTitles should very rarely be empty;
                    // and if the first one is, we replace it with "Unknown" below.
                    existingTitle = allTitles[0].title.Value;
                }
            }
            // If neither title nor allTitles are defined, just give a placeholder value.
            if (String.IsNullOrEmpty(existingTitle))
                existingTitle = "Unknown";
            var existingId = existingBookInfo.id.ToString();
            var existingBookUrl = BloomLibraryUrls.BloomLibraryDetailPageUrlFromBookId(existingId);

            var createdDate = createdDateTime.ToString("d", CultureInfo.CurrentCulture);
            var updatedDate = updatedDateTime.ToString("d", CultureInfo.CurrentCulture);
            var existingThumbUrl = GetBloomLibraryThumbnailUrl(existingBookInfo);

            var oldBranding = existingBookInfo.brandingProjectName?.ToString();

            // Must match IUploadCollisionDlgProps in uploadCollisionDlg.tsx.
            return new
            {
                shouldShow = true,
                userEmail = LoggedIn ? WebUserId : "",
                newThumbUrl = newThumbPath,
                newTitle,
                newLanguages,
                existingBookObjectId = existingId,
                existingTitle,
                existingLanguages,
                existingCreatedDate = createdDate,
                existingUpdatedDate = updatedDate,
                existingBookUrl,
                existingThumbUrl,
                newBranding = Book.BookInfo.BrandingProjectKey,
                oldBranding,
                uploader = existingBookInfo.uploader.email,
                count = existingBookInfo.count,
                permissions = existingBookInfo.permissions
            };
        }

        private static readonly string kThumbnailFileName = "thumbnail-256.png";

        private static string GetBloomLibraryThumbnailUrl(dynamic existingBookInfo)
        {
            // Code basically copied from bloomlibrary2 Book.ts
            var baseUrl = existingBookInfo.baseUrl.ToString();
            var harvestState = existingBookInfo.harvestState.ToString();
            var updatedTime = existingBookInfo.updatedAt.ToString();
            var harvesterThumbnailUrl = GetHarvesterThumbnailUrl(
                baseUrl,
                harvestState,
                updatedTime
            );
            if (harvesterThumbnailUrl != null)
            {
                return harvesterThumbnailUrl;
            }
            // Try "legacy" version (the one uploaded with the book)
            return CreateUrlWithCacheBusting(
                baseUrl + BloomLibraryPublishModel.kThumbnailFileName,
                updatedTime
            );
        }

        // Code modified from bloomlibrary2 Book.ts.
        // Bloomlibrary2 code still (as of 11/11/2021) checks the harvest date to see if the thumbnail is useful,
        // But now there are no books in circulation on bloomlibrary that haven't been harvested since that date.
        // So now we can consider any harvester thumbnail as valid, as long as the harvestState is "Done".
        private static string GetHarvesterThumbnailUrl(
            string baseUrl,
            string harvestState,
            string lastUpdate
        )
        {
            if (harvestState != "Done")
                return null;

            const string slash = "%2f";
            var folderWithoutLastSlash = baseUrl;
            if (baseUrl.EndsWith(slash))
            {
                folderWithoutLastSlash = baseUrl.Substring(0, baseUrl.Length - 3);
            }

            var index = folderWithoutLastSlash.LastIndexOf(
                slash,
                StringComparison.InvariantCulture
            );
            var pathWithoutBookName = folderWithoutLastSlash.Substring(0, index);
            var pathToHarvestedBookFolder = pathWithoutBookName
                .Replace("BloomLibraryBooks-Sandbox", "bloomharvest-sandbox")
                .Replace("BloomLibraryBooks", "bloomharvest");

            // Harvested books are stored in a separate S3 bucket from the original uploads.
            // Harvester stores harvested thumbnails in a 'thumbnails' subfolder,
            // whereas the orignally-uploaded book doesn't have a separate subfolder for thumbnails.
            // BloomLibrary code depends on this location to find the thumbnails it displays.
            return CreateUrlWithCacheBusting(
                pathToHarvestedBookFolder
                    + "/thumbnails/"
                    + BloomLibraryPublishModel.kThumbnailFileName,
                lastUpdate
            );
        }

        private static string CreateUrlWithCacheBusting(string url, string lastUpdate)
        {
            return url + "?version=" + lastUpdate;
        }

        /// <summary>
        /// Generate two lists of language names, one based on the language codes in the book (including its sign
        /// language if passed in separately), and the other based on the language objects from the server.
        /// If we get different names for the same language from the two sources, we'll combine them with a slash.
        /// </summary>
        internal static void ConvertLanguageListsToNames(
            IEnumerable<string> langCodes,
            BookData bookData,
            IEnumerable<dynamic> databaseLangObjectsEnum,
            WritingSystem signLanguage,
            out string[] newLanguages,
            out string[] existingLanguages
        )
        {
            var mapCodeToName = new Dictionary<string, string>();
            var databaseLangObjects = databaseLangObjectsEnum.ToList();
            foreach (var langCode in langCodes)
            {
                mapCodeToName[langCode] = bookData.GetDisplayNameForLanguage(langCode);
            }

            if (signLanguage != null)
            {
                mapCodeToName[signLanguage.Tag] = signLanguage.Name;
            }

            foreach (var languageObject in databaseLangObjects)
            {
                var name = languageObject.name.ToString();
                var code = languageObject.tag.ToString();
                if (mapCodeToName.TryGetValue(code, out string altName) && altName != name)
                    mapCodeToName[code] = altName + "/" + name;
                else
                {
                    mapCodeToName[code] = name;
                }
            }
            var newLanguages1 = langCodes.Select(code => mapCodeToName[code]);
            if (signLanguage != null)
                newLanguages1 = newLanguages1.Append(mapCodeToName[signLanguage.Tag]);
            newLanguages = newLanguages1.ToArray();

            existingLanguages = databaseLangObjects
                .Select<dynamic, string>(
                    languageObject => mapCodeToName[languageObject.tag.ToString()]
                )
                .ToArray();
        }

        /// <summary>
        /// Are we currently allowed to change the book ID?
        /// In some places, this is referred to simply as "Id" but this is used in contexts
        /// where it is important to distinguish it from the book's databaseId on our server.
        /// </summary>
        internal bool ChangeBookInstanceId(IProgress progress)
        {
            if (!Book.BookInfo.CanChangeBookInstanceId)
            {
                ErrorReport.NotifyUserOfProblem(
                    "Bloom cannot fix the ID of this book because that would cause problems syncing your Team Collection. You can work around this by making a duplicate of the book and uploading that."
                );
                return false;
            }
            progress.WriteMessage("Setting new instance ID...");
            Book.BookInfo.Id = BookInfo.InstallFreshInstanceGuid(Book.FolderPath);
            progress.WriteMessage("ID is now " + Book.BookInfo.Id);
            return true;
        }

        internal void UpdateLangDataCache()
        {
            Book.BookData.UpdateCache();
        }

        private string CurrentSignLanguageName
        {
            get { return Book.CollectionSettings.SignLanguage.Name; }
        }
    }

    internal enum LicenseState
    {
        Null,
        CreativeCommons,
        Custom
    }
}
