using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.UI.WebControls;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Collection;
using Bloom.ErrorReporter;
using Bloom.ImageProcessing;
using Bloom.Publish;
using Bloom.SafeXml;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Progress;
using SIL.Reporting;
using Image = System.Drawing.Image;

namespace Bloom.Book
{
    /* The role of this class is simply to isolate the actual storage mechanism (e.g. file system)
     * to a single place.  All the other classes can then just pass around DOMs.
     */
    public interface IBookStorage
    {
        //TODO Convert most of this section to something like IBookDescriptor, which has enough to display in a catalog, do some basic filtering, etc.
        string Key { get; }
        string FolderName { get; }
        string FolderPath { get; }
        string PathToExistingHtml { get; }
        bool TryGetPremadeThumbnail(string fileName, out Image image);

        //bool DeleteBook();
        bool RemoveBookThumbnail(string fileName);
        string ErrorMessagesHtml { get; }
        string GetBrokenBookRecommendationHtml();

        // REQUIRE INITIALIZATION (AVOID UNLESS USER IS WORKING WITH THIS BOOK SPECIFICALLY)
        bool GetLooksOk();
        HtmlDom Dom { get; }
        void Save();

        void SaveForPageChanged(string pageId, SafeXmlElement modifiedPage);
        HtmlDom GetRelocatableCopyOfDom(bool withUpdatedStylesheets = true);
        HtmlDom MakeDomRelocatable(HtmlDom dom);
        string SaveHtml(HtmlDom bookDom);
        void SetBookName(string name);
        void RestoreBookName(string restoredName);
        string GetHtmlMessageIfFeatureIncompatibility();
        string GetValidateErrors();
        void CheckBook(IProgress progress, string pathToFolderOfReplacementImages = null);
        void UpdateBookFileAndFolderName(CollectionSettings settings);
        string HandleRetiredXMatterPacks(HtmlDom dom, string nameOfXMatterPack);
        IFileLocator GetFileLocator();
        event EventHandler FolderPathChanged;

        void CleanupUnusedSupportFiles(
            bool isForPublish,
            HashSet<string> langsToExcludeAudioFor = null
        );
        void CleanupUnusedImageFiles(bool keepFilesForEditing = true);
        void CleanupUnusedAudioFiles(bool isForPublish, HashSet<string> langsToExcludeAudioFor);
        void CleanupUnusedVideoFiles();
        void CleanupUnusedActivities();
        void RepairEmptyPages();

        ExpandoObject XmatterAppearanceSettings { get; }
        ExpandoObject BrandingAppearanceSettings { get; }

        bool LegacyThemeCanBeUsed { get; }

        bool HarvesterMayConvertToDefaultTheme { get; }

        BookInfo BookInfo { get; set; }
        string NormalBaseForRelativepaths { get; }
        string InitialLoadErrors { get; }
        bool ErrorAllowsReporting { get; }
        void UpdateSupportFiles();
        void Update(string fileName, string factoryPath = "");
        string Duplicate();
        IEnumerable<string> GetNarrationAudioFileNamesReferencedInBook(bool includeWav);
        IEnumerable<string> GetBackgroundMusicFileNamesReferencedInBook();
        string GetSupportingFile(string relativePath, bool useInstalledBranding = false);
        void EnsureOriginalTitle();
        bool LinkToLocalCollectionStyles { get; set; }

        IEnumerable<string> GetActivityFolderNamesReferencedInBook();
        void MigrateMaintenanceLevels();
        void MigrateToMediaLevel1ShrinkLargeImages();
        void MigrateToLevel2RemoveTransparentComicalSvgs();
        void MigrateToLevel3PutImgFirst();

        void MigrateToLevel4UseAppearanceSystem();

        CollectionSettings CollectionSettings { get; }

        void ReloadFromDisk(string renamedTo, Action betweenReloadAndEvents);

        string[] GetCssFilesToLinkForPreview();

        Tuple<string, string>[] GetCssFilesToCheckForAppearanceCompatibility(
            bool justOldCustomFiles = false
        );

        string PathToXMatterStylesheet { get; }
    }

    public class BookStorage : IBookStorage
    {
        public delegate BookStorage Factory(BookInfo bookInfo); //autofac uses this

        /// <summary>
        /// History of these numbers:
        ///   Initially, the two numbers were only one number (kBloomFormatVersion):
        ///		0.4 had version 0.4
        ///		0.8, 0.9, 1.0 had version 0.8
        ///		1.1 had version 1.1
        ///     Bloom 1.0 went out with format version 0.8, but meanwhile compatible books were made (by hand) with "1.1."
        ///     We didn't notice because Bloom didn't actually check this number, it just produced it.
        ///     At that point, books with a newer format version (2.0) started becoming available, so we patched Bloom 1.0
        ///     to reject those. At that point, we also changed Bloom 1.0's kBloomFormatVersion to 1.1 so that it would
        ///     not reject those format version 1.1 books.
        ///     For the next version of Bloom (expected to be called version 2, but unknown at the moment) we went with
        ///     (coincidentally) kBloomFormatVersion = "2.0"
        ///   Starting with a hotfix to 4.4 (4.4.5), the one number became two numbers:
        ///    kBloomFormatVersionToWrite is what this version of Bloom writes when it creates a book.
        ///    kMaxBloomFormatVersionToRead is the highest format version this version of Bloom can read/open.
        ///   The reason for this is that 4.6 started creating books which 4.4 couldn't publish, but an easy change to 4.4.5 (BL-7431)
        ///    meant that it could. 4.5 already could. And we wanted 4.4 and 4.5 to continue to create backward-compatible books.
        ///    So 4.4 and 4.5 continued to create 2.0 format while (after 4.4.5) being able to read/open 2.1.
        ///    And 4.6 started creating 2.1.
        /// </summary>
        internal const string kBloomFormatVersionToWrite = "2.1";
        internal const string kMaxBloomFormatVersionToRead = "2.1";

        /// <summary>
        /// These constants are not currently actually used in code, but indicate the largest
        /// number currently used for some DOM metadata elements that keep track of how much
        /// a book has been migrated.
        /// History of kMaintenanceLevel:
        ///   Bloom 4.9: 1 = Ensure that all images are opaque and no larger than our desired maximum size.
        ///              2 = Remove any 'comical-generated' svgs that are transparent.
        ///				 3 = Ensure main img comes first in image container
        ///   (Bloom 6.0 added kMediaMaintenanceLevel so we could distinguish migrations that affect
        ///   other files (typically images or media) in the book folder from ones that only affect
        ///   the DOM and can safely be done in memory. (Later in 6.0 we stopped doing incomplete
        ///   book updates in memory, so this distinction may no longer be helpful.))
        ///   Bloom 6.0  4 = Switched to using a theme (or explicitly using legacy)
        /// History of kMediaMaintenanceLevel (introduced in 6.0)
        ///   missing: set it to 0 if maintenanceLevel is 0 or missing, otherwise 1
        ///              0 = No media maintenance has been done
        ///   Bloom 6.0: 1 = maintenanceLevel at least 1 (so images are opaque and not too big)
        /// </summary>
        public const int kMaintenanceLevel = 4;
        public const int kMediaMaintenanceLevel = 1;

        public const string PrefixForCorruptHtmFiles = "_broken_";
        private IChangeableFileLocator _fileLocator;
        private BookRenamedEvent _bookRenamedEvent;
        private readonly CollectionSettings _collectionSettings;
        private static bool _alreadyNotifiedAboutOneFailedCopy;
        private BookInfo _metaData;
        private bool _errorAlreadyContainsInstructions;
        public event EventHandler FolderPathChanged;
        public event EventHandler BookTitleChanged;

        // Returns any errors reported while loading the book (during 'expensive initialization').
        public string InitialLoadErrors { get; private set; }
        public bool ErrorAllowsReporting { get; private set; } // True if we want to display a Report to Bloom Support button

        public virtual BookInfo BookInfo
        {
            get
            {
                Debug.Assert(_metaData != null);
                return _metaData;
            }
            set { _metaData = value; }
        }

        public BookStorage()
        {
            if (!Program.RunningUnitTests)
                throw new ApplicationException(
                    "Parameterless BookStorage constructor is allowed only in unit tests!"
                );
        }

        /// <summary>
        /// Historically, we used this constructor, but every call resulted in initializing a new BookInfo
        /// during ExpensiveInitialization. Then, right after we finish constructing it, we set the BookInfo
        /// we really want. That became more of a problem in 6.0, when we started doing more expensive
        /// initialization of AppearanceSettings and could get in trouble working with one that was not
        /// fully initialized. So now we use the other constructor, which takes the BookInfo
        /// we really want to use. This one is retained only for convenience of old unit tests.
        /// </summary>
        internal BookStorage(
            string folderPath,
            IChangeableFileLocator baseFileLocator,
            BookRenamedEvent bookRenamedEvent,
            CollectionSettings collectionSettings
        )
            : this(
                new BookInfo(folderPath, false),
                baseFileLocator,
                bookRenamedEvent,
                collectionSettings
            )
        {
            if (!Program.RunningUnitTests)
                throw new ApplicationException(
                    "BookStorage constructor passing folder name instead of bookInfo is allowed only in unit tests!"
                );
        }

        public BookStorage(
            BookInfo bookInfo,
            IChangeableFileLocator baseFileLocator,
            BookRenamedEvent bookRenamedEvent,
            CollectionSettings collectionSettings
        )
        {
            FolderPath = bookInfo.FolderPath;
            _metaData = bookInfo;

            // We clone this because we'll be customizing it for use by just this book
            _fileLocator = (IChangeableFileLocator)
                baseFileLocator.CloneAndCustomize(new string[] { });
            _bookRenamedEvent = bookRenamedEvent;
            _collectionSettings = collectionSettings;

            ErrorAllowsReporting = true;

            ExpensiveInitialization();
        }

        private string _cachedFolderPath;
        private string _cachedPathToHtml;

        public string GetSupportingFile(string relativePath, bool useInstalledBranding = false)
        {
            if (useInstalledBranding && relativePath == "branding.css")
            {
                return BloomFileLocator.GetOptionalBrandingFile(
                    _collectionSettings.BrandingProjectKey,
                    "branding.css"
                );
            }
            if (BookStorage.CssFilesThatAreDynamicallyUpdated.Contains(relativePath))
            {
                var localPath = Path.Combine(FolderPath, relativePath);
                if (RobustFile.Exists(localPath))
                {
                    return localPath;
                }
                else
                {
                    return null;
                }
            }

            if (relativePath == Path.GetFileName(PathToXMatterStylesheet))
            {
                return PathToXMatterStylesheet;
            }

            return _fileLocator.LocateFile(relativePath);
        }

        public static void RemoveLocalOnlyFiles(string folderPath)
        {
            LocalOnlyFiles(folderPath).ForEach(f => RobustFile.Delete(f));
        }

        /// <summary>
        /// This is something of a work in progress. The idea is to identify stuff that we
        /// don't want when making a copy of a book folder. It's tricky because it tends
        /// to involve an intersection of responsibilities. On the one hand, whether we are
        /// publishing a book to S3 or making a local duplicate or creating an epub, we
        /// don't need the status file that helps implement Team Collection; and
        /// code for those tasks has no business knowing about that status file. OTOH,
        /// some of those functions don't need some of the audio files; but a generic
        /// cleanup function like this doesn't know which ones. For now, it just deals
        /// with TC cleanup and with any unused copies of the placeHolder.png image file.
        /// </summary>
        public static List<string> LocalOnlyFiles(string folderPath)
        {
            var accumulator = new List<string>();
            TeamCollection.TeamCollection.AddTCSpecificFiles(folderPath, accumulator);
            AddUnusedPlaceholderImages(folderPath, accumulator);
            return accumulator;
        }

        /// <summary>
        /// Add unused copies of placeHolder.png in bookFolderPath to accumulator.
        /// </summary>
        private static void AddUnusedPlaceholderImages(
            string bookFolderPath,
            List<string> accumulator
        )
        {
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-7616 and also
            // https://issues.bloomlibrary.org/youtrack/issue/BL-9479 for what has
            // happened in the past that still can use cleanup in new work.
            var placeholders = Directory.GetFiles(bookFolderPath, "placeHolder*.png");
            if (placeholders.Length == 0)
                return;
            var htmlPath = FindBookHtmlInFolder(bookFolderPath);
            if (String.IsNullOrEmpty(htmlPath))
                return; // shouldn't happen, but if it does we'll surely flag it elsewhere
            var htmlContent = RobustFile.ReadAllText(htmlPath);
            foreach (var filepath in placeholders)
            {
                var filename = Path.GetFileName(filepath);
                if (htmlContent.Contains($" src=\"{filename}\""))
                    continue; // file is used
                accumulator.Add(filepath);
            }
        }

        public string PathToExistingHtml
        {
            get
            {
                // We reference PathToExistingHtml about 3 times per book when doing ExpensiveInitialization.
                // Let's make it not quite so expensive.
                // But let's at least make sure that "existing html" actually does (the user could have manually renamed it)
                if (
                    !String.IsNullOrEmpty(_cachedFolderPath)
                    && FolderPath == _cachedFolderPath
                    && RobustFile.Exists(_cachedPathToHtml)
                )
                {
                    return _cachedPathToHtml;
                }

                _cachedFolderPath = FolderPath;
                _cachedPathToHtml = FindBookHtmlInFolder(_cachedFolderPath);
                return _cachedPathToHtml;
            }
        }

        /// <summary>
        /// Returns the Folder Name, meaning just the name of the folder without the full path.
        /// </summary>
        public string FolderName
        {
            get
            {
                // Path.GetFileName will return empty string if the FolderPath ends with "/"
                Debug.Assert(
                    !FolderPath.EndsWith(Path.DirectorySeparatorChar.ToString()),
                    "FolderPath is expected not to include a trailing slash, otherwise the folder's name will be determined incorrectly."
                );

                // You want GetFileName, not GetFileNameWithoutExtension, because folders don't have extensions
                // so GetFileNameWithoutExtension will return the wrong result if the folder name contains a period
                //
                // If the FolderPath does NOT end with "/", GetDirectoryName will get the full path to the book's PARENT directory, not the BOOK directory,
                // but GetFileName(GetDirectoryName()) would return the desired result if the FolderPath DOES end with "/"
                return Path.GetFileName(FolderPath);
            }
        }

        public string FolderPath { get; private set; }

        public string Key => FolderPath;

        /// <summary>
        ///
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>false if we shouldn't mess with the thumbnail</returns>
        public bool RemoveBookThumbnail(string fileName)
        {
            string path = Path.Combine(FolderPath, fileName);
            if (RobustFile.Exists(path) && (new FileInfo(path).IsReadOnly)) //readonly is good when you've put in a custom thumbnail
            {
                return false;
            }
            if (RobustFile.Exists(path))
            {
                RobustFile.Delete(path);
            }
            return true;
        }

        public string ErrorMessagesHtml { get; private set; }

        /// <summary>
        /// this is a method because it wasn't clear if we will eventually generate it on the fly (book paths do change as they are renamed)
        /// </summary>
        /// <returns></returns>
        public IFileLocator GetFileLocator()
        {
            return _fileLocator;
        }

        public bool TryGetPremadeThumbnail(string fileName, out Image image)
        {
            string path = Path.Combine(FolderPath, fileName);
            if (RobustFile.Exists(path))
            {
                image = RobustImageIO.GetImageFromFile(path);
                return true;
            }
            image = null;
            return false;
        }

        public HtmlDom Dom { get; private set; }

        public static string GetHtmlMessageIfVersionIsIncompatibleWithThisBloom(
            HtmlDom dom,
            string path
        )
        {
            var versionString = dom.GetMetaValue("BloomFormatVersion", "").Trim();
            if (String.IsNullOrEmpty(versionString))
                return ""; // "This file lacks the following required element: <meta name='BloomFormatVersion' content='x.y'>";

            float versionFloat = 0;
            if (
                !Single.TryParse(
                    versionString,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out versionFloat
                )
            )
                return "This file claims a version number that isn't really a number: "
                    + versionString;

            if (
                versionFloat
                > Single.Parse(kMaxBloomFormatVersionToRead, CultureInfo.InvariantCulture)
            )
            {
                var msg = LocalizationManager.GetString(
                    "Errors.NeedNewerVersion",
                    "{0} requires a newer version of Bloom. Download the latest version of Bloom from {1}",
                    "{0} will get the name of the book, {1} will give a link to open the Bloom Library Web page."
                );
                msg = String.Format(
                    msg,
                    path,
                    $"<a href='{UrlLookup.LookupUrl(UrlType.LibrarySite, null)}'>BloomLibrary.org</a>"
                );
                msg += $". (Format {versionString} vs. {kMaxBloomFormatVersionToRead})";
                return msg;
            }

            return null;
        }

        /// <summary>
        /// Returns HTML with error message for any features that this book contains which cannot be opened
        /// by this version of Bloom.
        /// </summary>
        /// <remarks>
        /// Note that although we don't allow the user to open the book (because if this version opens and
        /// saves the book, it will cause major problems for a later version of Bloom), there isn't actually
        /// any corruption or malformed data or anything particularly wrong with the book storage.
        ///
        /// So, we need to handle these kind of errors differently than validation errors.
        ///</remarks>
        /// <returns>HTML error message or empty string, if no error.</returns>
        public string GetHtmlMessageIfFeatureIncompatibility()
        {
            // Check if there are any features in this file format (which is readable), but which won't be supported (and have effects bad enough to warrant blocking opening) in this version.
            var featureVersionRequirementJson = Dom.GetMetaValue("FeatureRequirement", "");
            if (string.IsNullOrEmpty(featureVersionRequirementJson))
            {
                return "";
            }
            VersionRequirement[] featureVersionRequirementList = (VersionRequirement[])
                JsonConvert.DeserializeObject(
                    featureVersionRequirementJson,
                    typeof(VersionRequirement[])
                );

            if (featureVersionRequirementList != null && featureVersionRequirementList.Length >= 1)
            {
                var assemblyVersion = typeof(BookStorage).Assembly?.GetName()?.Version;

                Version currentBloomDesktopVersion;
                if (assemblyVersion == null)
                {
                    currentBloomDesktopVersion = new Version(1, 0);
                }
                else
                {
                    // Make it so that it only compares Major/Minor and doesn't care about different or missing Build or Revision numbers.
                    currentBloomDesktopVersion = new Version(
                        assemblyVersion.Major,
                        assemblyVersion.Minor
                    );
                }

                var breakingFeatureRequirements = featureVersionRequirementList.Where(
                    x => currentBloomDesktopVersion < new Version(x.BloomDesktopMinVersion)
                );

                // Note: even though versionRequirements is guaranteed non-empty by now, the ones that actually break our current version of Bloom DESKTOP could be empty.
                if (breakingFeatureRequirements.Any())
                {
                    string messageNewVersionNeededHeader = LocalizationManager.GetString(
                        "Errors.NewVersionNeededHeader",
                        "This book needs a new version of Bloom."
                    );
                    string messageCurrentRunningVersion = String.Format(
                        LocalizationManager.GetString(
                            "Errors.CurrentRunningVersion",
                            "You are running Bloom {0}"
                        ),
                        currentBloomDesktopVersion
                    );
                    string messageDownloadLatestVersion = LocalizationManager.GetString(
                        "Errors.DownloadLatestVersion",
                        "Upgrade to the latest Bloom (requires Internet connection)"
                    );

                    string messageFeatureRequiresNewerVersion;
                    if (breakingFeatureRequirements.Count() == 1)
                    {
                        var requirement = breakingFeatureRequirements.First();
                        messageFeatureRequiresNewerVersion =
                            String.Format(
                                LocalizationManager.GetString(
                                    "Errors.FeatureRequiresNewerVersionSingular",
                                    "This book requires Bloom {0} or greater because it uses the feature \"{1}\"."
                                ),
                                requirement.BloomDesktopMinVersion,
                                requirement.FeaturePhrase
                            ) + "<br/>";
                    }
                    else
                    {
                        var sortedRequirements = breakingFeatureRequirements.OrderByDescending(
                            x => new Version(x.BloomDesktopMinVersion)
                        );
                        var highestVersionRequired = sortedRequirements
                            .First()
                            .BloomDesktopMinVersion;

                        messageFeatureRequiresNewerVersion = String.Format(
                            LocalizationManager.GetString(
                                "Errors.FeatureRequiresNewerVersionPlural",
                                "This book requires Bloom {0} or greater because it uses the following features:"
                            ),
                            highestVersionRequired
                        );

                        string listItemsHtml = String.Join(
                            "",
                            sortedRequirements.Select(x => $"<li>{x.FeaturePhrase}</li>")
                        );
                        messageFeatureRequiresNewerVersion += $"<ul>{listItemsHtml}</ul>";
                    }

                    string message =
                        $"<strong>{messageNewVersionNeededHeader}</strong><br/><br/><br/>"
                        + $"{messageCurrentRunningVersion}. {messageFeatureRequiresNewerVersion}<br/><br/>"
                        +
                        // If we just embed the URL, since we show this document in a plain browser without any
                        // of our code loaded (in particular, our linkHandler code in typescript), the browser
                        // control inside bloom will navigate there, which isn't what we want. The easiest
                        // way around this is to have an api which does what we want.
                        $"<a href='/bloom/api/app/showDownloadsPage'>{messageDownloadLatestVersion}</a>";

                    return message;
                }
            }

            return "";
        }

        public bool GetLooksOk()
        {
            return RobustFile.Exists(PathToExistingHtml) && String.IsNullOrEmpty(ErrorMessagesHtml);
        }

        public void Save()
        {
            if (!String.IsNullOrEmpty(ErrorMessagesHtml))
            {
                return; //too dangerous to try and save
            }
            Logger.WriteEvent(
                "BookStorage.Saving... (eventual destination: {0})",
                PathToExistingHtml
            );

            Dom.UpdateMetaElement(
                "Generator",
                "Bloom " + ErrorReport.GetVersionForErrorReporting()
            );
            var formatVersion = GetBloomFormatVersionToWrite(BookInfo.FormatVersion);
            if (!Program.RunningUnitTests)
            {
                Dom.UpdateMetaElement("BloomFormatVersion", formatVersion);
            }
            BookInfo.FormatVersion = formatVersion;

            var requiredVersions = GetRequiredVersionsString(Dom);
            if (!string.IsNullOrEmpty(requiredVersions))
            {
                Dom.UpdateMetaElement("FeatureRequirement", requiredVersions);
            }
            else
            {
                // Might be necessary if you duplicated a book, or modified a book such that it no longer needs this
                Dom.RemoveMetaElement("FeatureRequirement");
            }

            string tempPath = SaveHtml(Dom);
            ValidateSave(tempPath);

            BookInfo.Save();
        }

        // Common final stage of Save() and SaveForPageChanged(). Validates the temp file, reports any problems,
        // and if all is well moves the current file to a backup and the new one to replace the original.
        private void ValidateSave(string tempPath)
        {
            string errors = ValidateBook(Dom, tempPath);

            if (!String.IsNullOrEmpty(errors))
            {
                Logger.WriteEvent("Errors saving book {0}: {1}", PathToExistingHtml, errors);
                var badFilePath = PathToExistingHtml + ".bad";
                RobustFile.Copy(tempPath, badFilePath, true);
                // delete the temporary file since we've made a copy of it.
                RobustFile.Delete(tempPath);
                //hack so we can package this for palaso reporting
                errors += String.Format(
                    "{0}{0}{0}Contents:{0}{0}{1}",
                    Environment.NewLine,
                    RobustFile.ReadAllText(badFilePath)
                );
                var ex = new XmlSyntaxException(errors);

                // ENHANCE: If it's going to kill the process right afterward, seems like we could call the FatalMessage version instead...
                ErrorReport.NotifyUserOfProblem(
                    ex,
                    "Before saving, Bloom did an integrity check of your book, and found something wrong. This doesn't mean your work is lost, but it does mean that there is a bug in the system or templates somewhere, and the developers need to find and fix the problem (and your book).  Please report the problem to us.  Bloom has saved the bad version of this book as "
                        + badFilePath
                        + ".  Bloom will now exit, and your book will probably not have this recent damage.  If you are willing, please try to do the same steps again, so that you can report exactly how to make it happen."
                );

                Process.GetCurrentProcess().Kill();
            }
            else
            {
                Logger.WriteMinorEvent(
                    "ReplaceFileWithUserInteractionIfNeeded({0},{1})",
                    tempPath,
                    PathToExistingHtml
                );
                if (!String.IsNullOrEmpty(tempPath))
                    FileUtils.ReplaceFileWithUserInteractionIfNeeded(
                        tempPath,
                        PathToExistingHtml,
                        GetBackupFilePath()
                    );
            }
        }

        /// <summary>
        /// A highly optimized Save for use when the only thing that needs to be written is the content
        /// of the one page the user has been editing. This is quite a bit of complexity to add for
        /// that case, but it's a common and important case: nearly all edits just affect a single page.
        /// And currently Bloom Saves even when just switching pages without changing anything.
        /// On a long book (e.g., BL-7253) using this makes page switching two seconds faster,
        /// as well as preventing heap fragmentation that eventually leads to running out of memory.
        /// </summary>
        public void SaveForPageChanged(string pageId, SafeXmlElement modifiedPage)
        {
            // We've seen pages get emptied out, and we don't know why. This is a safety check.
            // See BL-13078, BL-13120, BL-13123, and BL-13143 for examples.
            if (CheckForEmptyMarginBoxOnPage(modifiedPage))
            {
                // This has been logged and reported to the user. We don't want to save the empty page.
                return;
            }

            // Convert the one page to HTML
            string pageHtml = XmlHtmlConverter.ConvertElementToHtml5(modifiedPage);

            // Read the old file and copy it to the new one, except for replacing the one page.
            string tempPath = GetNameForATempFileInStorageFolder();
            RetryUtility.Retry(() =>
            {
                using (
                    var reader = new StreamReader(
                        RobustIO.GetFileStream(PathToExistingHtml, FileMode.Open),
                        Encoding.UTF8
                    )
                )
                {
                    using (
                        var writer = new StreamWriter(
                            RobustIO.GetFileStream(tempPath, FileMode.Create),
                            Encoding.UTF8
                        )
                    )
                    {
                        ReplacePage(pageId, reader, writer, pageHtml);
                    }
                }
            });
            ValidateSave(tempPath);
            BookInfo.Save();
        }

        private static string GetBloomFormatVersionToWrite(string existingVersion)
        {
            if (
                !float.TryParse(
                    existingVersion,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out var existingVersionFloat
                )
            )
                return kBloomFormatVersionToWrite;
            if (
                existingVersionFloat
                > float.Parse(kBloomFormatVersionToWrite, CultureInfo.InvariantCulture)
            )
                return existingVersion;
            return kBloomFormatVersionToWrite;
        }

        // The states for the ReplacePage state machine.
        enum SearchState
        {
            LookForOpenDiv, // First part of finding the page to replace is to find "<div"
            LookForId, // Once we found an open div, we look for an ID attribute with the right value
            LookForNextPageDiv, // Then looking for the next page...first its "<div"
            LookForNextPageClass, // Then for its class attribute
            LookForBloomPageClass, // and within that for "bloom-page"
            CopyingTailEnd // and once we replaced the page and found the next one we just copy the rest
        }

        // This method implements a state machine (using the SearchState states above) which reads a bloom HTML
        // file from reader and writes it to writer, replacing the page with the specified ID with the content of
        // pageHtml.
        // It implements some pretty complex RegEx-like logic to do this. There may be a simpler way, but note that
        // the goal is to avoid pulling the whole file into memory like XmlHtmlConverter does.
        // It assumes the file is written the way Bloom writes it. For example, it does not attempt to handle
        // attributes delimited with single quotes, attributes without spaces between them, attributes with white space
        // between name and quotes. It uses a naive strategy to identify pages, looking for a div whose class contains
        // bloom-page, with no attempt to NOT match longer class names like hidden-bloom-page. (VERY many other places
        // in our code, for better or worse, assume that a page can be identified by contains(@class,'bloom-page'),
        // and the code simplification from assuming it here is considerable.)
        internal static void ReplacePage(
            string pageId,
            StreamReader reader,
            StreamWriter writer,
            string pageHtml
        )
        {
            // the main state our state machine is in. Initially, we're looking for the page to replace.
            var state = SearchState.LookForOpenDiv;

            // In most of the main states, we have a nested state machine which is looking for a certain string
            // (and possibly a terminator). The string we want to match is in matchNext, and matchIndex indicates
            // which character in the string we need to match currently.
            string matchNext = "<div";
            int matchIndex = 0;
            // In several states, we need to save up input text which we may or may not output, depending on
            // whether we complete the match we are attempting.
            var pending = new StringBuilder();
            // These variables efficiently keep track of the last 100 characters read
            // to support copying whatever follows the last page if that's the one we replaced.
            // 100 characters is probably excessive; typically all that is after the last
            // page in a bloom file is </body></html> (possibly with some white space).
            // However, it's not much more expensive to have a 100 character buffer, and
            // it might be helpful one day to be able to handle a trailing <script> tag or
            // something similar.
            const int bufLen = 100;
            var buffer = new char[bufLen];
            int bufIndex = 0;
            var bufWrapped = false; // did we fill the buffer and wrap? Will nearly always end up true except in unit tests.

            while (!reader.EndOfStream)
            {
                var input = Convert.ToChar(reader.Read());
                buffer[bufIndex++] = input;
                if (bufIndex >= bufLen)
                {
                    bufIndex = 0;
                    bufWrapped = true;
                }

                var c = Char.ToLowerInvariant(input);

                // Note that after this switch, we copy the input character to the output.
                // That is therefore the result of any branch that ends with 'break'.
                // Other branches typically append input to 'pending' (for possible output later, depending
                // on whether match succeeds) and use 'continue'.
                switch (state)
                {
                    case SearchState.LookForOpenDiv:
                        if (c == matchNext[matchIndex])
                        {
                            pending.Append(input);
                            matchIndex++;
                            if (matchIndex >= matchNext.Length)
                            {
                                // found an opening div. Now we need the ID attribute to match,
                                // for it to be the page we want.
                                matchNext = " id=\"" + pageId + "\"";
                                matchIndex = 0;
                                state = SearchState.LookForId;
                            }
                            continue;
                        }
                        // current attempt to match <div has failed;
                        // output any incomplete match and start over.
                        writer.Write(pending);
                        pending.Clear();
                        matchIndex = 0;
                        break;
                    case SearchState.LookForId:
                        pending.Append(input);
                        if (c == matchNext[matchIndex])
                        {
                            matchIndex++;
                            if (matchIndex >= matchNext.Length)
                            {
                                // We found the page to replace. We do NOT output pending,
                                // because that's part of the page we're replacing. Instead,
                                // output the replacement page, and then start looking for the
                                // start of the next page.
                                writer.Write(pageHtml);
                                matchNext = "<div";
                                matchIndex = 0;
                                state = SearchState.LookForNextPageDiv;
                            }
                            continue;
                        }

                        if (c == '>')
                        {
                            // Got to the end of the <div tag without finding the ID.
                            // back to looking for an opening <div
                            // first, write out the saved content of the div tag.
                            state = SearchState.LookForOpenDiv;
                            writer.Write(pending);
                            pending.Clear();
                            matchNext = "<div";
                            matchIndex = 0;
                            continue; // the final > was already added to pending and then output
                        }
                        // otherwise, we're still in the div header, looking for ID, continuing to
                        // accumulate pending stuff we will output if we don't match,
                        // but have to start over looking for the id.
                        matchIndex = 0;
                        continue;
                    case SearchState.LookForNextPageDiv:
                        // Looking for "<div" as the first part of finding a following bloom-page.
                        if (c == matchNext[matchIndex])
                        {
                            pending.Append(input);
                            matchIndex++;
                            if (matchIndex >= matchNext.Length)
                            {
                                // Found the <div, now we have to look for the start of the class attribute.
                                state = SearchState.LookForNextPageClass;
                                matchNext = " class=\"";
                                matchIndex = 0;
                            }
                            continue;
                        }
                        // Back to skipping, looking for start of next bloom-page
                        matchIndex = 0;
                        // do NOT output it, it turned out to be part of the page we're replacing,
                        // not part of the following one we need to keep.
                        pending.Clear();
                        continue;
                    case SearchState.LookForNextPageClass:
                        // Looking for / class="/ (before closing >) as second step in finding following bloom-page
                        pending.Append(input);
                        if (c == matchNext[matchIndex])
                        {
                            matchIndex++;
                            if (matchIndex >= matchNext.Length)
                            {
                                // Found start of class attr, but to be the next page it must contain 'bloom-page'
                                state = SearchState.LookForBloomPageClass;
                                matchNext = "bloom-page";
                                matchIndex = 0;
                            }
                            continue;
                        }
                        if (c == '>')
                        {
                            // div has no class, go back to start of looking for following page
                            state = SearchState.LookForNextPageDiv;
                            pending.Clear(); // don't output, part of replaced page
                            matchNext = "<div";
                            matchIndex = 0;
                            continue;
                        }
                        // start again looking for class within <div tag
                        matchIndex = 0;
                        continue;
                    case SearchState.LookForBloomPageClass:
                        // we're inside the class attribute of a div, looking for "bloom-page"
                        // (before the following quote).
                        pending.Append(input);
                        if (c == matchNext[matchIndex])
                        {
                            matchIndex++;
                            if (matchIndex >= matchNext.Length)
                            {
                                // Yes! we've found the next page.
                                // All the stuff we accumulated since the <div is part of the next page
                                // and needs to be output.
                                // And from here on we can just copy the rest of the file.
                                writer.Write(pending);
                                state = SearchState.CopyingTailEnd;
                            }
                            continue;
                        }
                        if (c == '"')
                        {
                            // end of class attr, didn't find 'bloom-page', back to looking for <div
                            state = SearchState.LookForNextPageDiv;
                            pending.Clear(); // don't output, part of replaced page
                            matchNext = "<div";
                            matchIndex = 0;
                            continue;
                        }
                        // start again looking for bloom-page in class attr
                        matchIndex = 0;
                        continue;
                    case SearchState.CopyingTailEnd:
                        // Once we reach this state, just copy everything else.
                        break;
                }
                // default behavior if we're not in the middle of a match (or we are just copying tail end)
                // copies input to output.
                writer.Write(input);
            }

            if (state != SearchState.CopyingTailEnd && state != SearchState.LookForOpenDiv)
            {
                // We found the page to replace, but never found a following page div.
                // Presumably, then, we replaced the last page.
                // Look back a short distance and copy over anything after the last closing div
                // (typically </body></html>)
                // (There are pathological cases where we might be in some other state, but not with
                // valid files. Even LookForOpenDiv implies that the page we wanted to replace was
                // missing.)
                Debug.Assert(
                    state == SearchState.LookForNextPageDiv,
                    "Something went wrong in the Save Page process"
                );
                var bufString = new string(buffer);
                var tailOfFile = bufString.Substring(0, bufIndex);
                if (bufWrapped)
                    tailOfFile = bufString.Substring(bufIndex, bufLen - bufIndex) + tailOfFile;
                int lastDiv = tailOfFile.LastIndexOf("</div>", StringComparison.InvariantCulture);
                writer.Write(tailOfFile.Substring(lastDiv + "</div>".Length));
            }
        }

        // It would be nice if this could simply extend VersionRequirement, but structs don't have
        // inheritance, and I'm not sure why it was made a struct: it may make some difference
        // to how it is serialized to JSON. And I don't want to risk that because the JSON has
        // to be read by older versions of Bloom.
        class Feature
        {
            public string BloomDesktopMinVersion { get; set; }
            public string BloomReaderMinVersion { get; set; }
            public string FeatureId { get; set; }
            public string FeaturePhrase { get; set; }

            public string XPath { get; set; }
        }

        /// <summary>
        /// Xpath for things generated by comical.js. We have renamed the tool Overlay Tool, but at this point
        /// I'm not renaming Features and the comical package itself.
        /// </summary>
        public static string ComicalXpath = "//*[@class='comical-generated']";

        static Feature[] _features =
        {
            new Feature()
            {
                FeatureId = "wholeTextBoxAudio",
                FeaturePhrase = "Whole Text Box Audio",
                BloomDesktopMinVersion = "4.4",
                BloomReaderMinVersion = "1.0",
                XPath = "//*[@data-audiorecordingmode='TextBox']"
            },
            new Feature()
            {
                FeatureId = "wholeTextBoxAudioInXmatter",
                // technically could be in back matter, but typically back matter has no recordable text,
                // and xmatter seems too technical a term for end users to see.
                FeaturePhrase = "Whole Text Box Audio in Front/Back Matter",
                BloomDesktopMinVersion = "4.7",
                BloomReaderMinVersion = "1.0",
                XPath = "//div[@data-xmatter-page]//*[@data-audiorecordingmode='TextBox']"
            },
            new Feature()
            {
                FeatureId = "comical-1",
                FeaturePhrase = "Support for Comics",
                BloomDesktopMinVersion = "4.7",
                BloomReaderMinVersion = "1.0",
                // We've updated Bloom to only store SVGs in the file if they are non-transparent. So now a
                // Bloom book is considered 'comical' for Publishing, etc. if it has a comical-generated SVG.
                XPath = ComicalXpath
            },
            new Feature()
            {
                FeatureId = "comical-2",
                FeaturePhrase = "Support for Comic Captions with Straight Line Tails",
                BloomDesktopMinVersion = "5.0",
                BloomReaderMinVersion = "1.0",
                // Bloom now allows comical Captions to have straight line tails, but if we open such a book
                // in an older version of Bloom which nevertheless has comical, it will give it a normal bubble tail.
                // This xpath finds a bubble with a "caption" style and a non-empty tail spec.
                XPath =
                    "//div[contains(@class,'bloom-textOverPicture') and contains(@data-bubble, '`caption`') and contains(@data-bubble, '`tails`:[{`')]"
            },
            new Feature()
            {
                FeatureId = "hiddenAudioSplitMarkers",
                FeaturePhrase = "Hide audio split markers (|) outside the talking book tool",
                BloomDesktopMinVersion = "5.5",
                BloomReaderMinVersion = "1.0",
                XPath = "//span[contains(@class,'bloom-audio-split-marker')]"
            },
            new Feature()
            {
                FeatureId = "bloomGames6.1",
                FeaturePhrase = "Bloom Games added in 6.1",
                BloomDesktopMinVersion = "6.1",
                BloomReaderMinVersion = "3.3",
                XPath =
                    "//div[@data-activity='drag-letter-to-target' or @data-activity='drag-image-to-target' or @data-activity='drag-sort-sentence' ]"
            }
        };

        // We should keep somewhat aware of how long this takes to run.
        // It's part of every page save, even the 'current page only' ones.
        // Currently (Dec 2019, with two features), it's usually too fast to measure (reported as 0ms) in a 20-page book,
        // but around 50ms in a 200-page one (the picture dictionary from BL-7253).
        // That's on a fast machine, where a full save of the picture dictionary takes
        // about 2.5s, and a page-only save about half a second, so it's running 10% of
        // our page-change time.
        // An easy optimization would be to calculate this string on the current page DOM
        // when we start and stop editing it. Both should take less than a millisecond.
        // If it doesn't change, I _think_ it's safe to conclude that edits to this page
        // would not affect the result for the whole book. However, calculating for the whole
        // book and comparing with what's currently stored is simpler and safer if we
        // can live with a modest performance cost for very large books.
        public static string GetRequiredVersionsString(HtmlDom dom)
        {
            var watch = new Stopwatch();
            watch.Start();
            var result = "";
            var requiredVersions = GetRequiredVersions(dom).ToArray();
            if (requiredVersions != null && requiredVersions.Length >= 1)
            {
                result = JsonConvert.SerializeObject(requiredVersions);
            }
            watch.Stop();
            Debug.WriteLine("GetRequiredVersionsString took " + watch.ElapsedMilliseconds + "ms");
            return result;
        }

        /// <summary>
        /// Determines which features will have serious breaking effects if not opened in the proper version of any
        /// relevant Bloom products.
        /// </summary>
        /// <remarks>
        /// This should include not only BloomDesktop considerations, but needs to insert enough information
        /// for things like BloomReader to be able to figure it out too.
        /// </remarks>
        public static IOrderedEnumerable<VersionRequirement> GetRequiredVersions(HtmlDom dom)
        {
            var reqList = new List<VersionRequirement>();

            foreach (var feature in _features)
            {
                if (dom.SelectSingleNode(feature.XPath) != null)
                {
                    reqList.Add(
                        new VersionRequirement()
                        {
                            FeatureId = feature.FeatureId,
                            FeaturePhrase = feature.FeaturePhrase,
                            BloomDesktopMinVersion = feature.BloomDesktopMinVersion,
                            BloomReaderMinVersion = feature.BloomReaderMinVersion
                        }
                    );
                }
            }

            return reqList.OrderByDescending(x => x.BloomDesktopMinVersion);
        }

        // need to know this in BookCollection too.
        public const string BackupFilename = "bookhtml.bak";

        // We try to keep all filenames in the book folder less than this.
        // The basic idea is to avoid running into the 260 character max path length.
        // We don't know exactly what directory we might want to unzip a book into
        // (and even if this computer has been configured to allow longer paths, some other
        // computer using the book might not be) so it makes sense to keep them fairly short.
        public const int kMaxFilenameLength = 50;

        private string GetBackupFilePath()
        {
            try
            {
                return Path.Combine(FolderPath, BackupFilename);
            }
            catch (Exception ex)
            {
                // Following up BL-5636, which involves a "Path is not of a legal form" here, we'd like to see
                // the exact path that has the problem.
                throw new ArgumentException("Failed to get backup file path for " + FolderPath, ex);
            }
        }

        /// <summary>
        /// Determine whether the folder contains static content that might be read-only.
        /// For example, template books are read-only on Linux and Windows --allUsers.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-5061.
        /// </remarks>
        public static bool IsStaticContent(string folderPath)
        {
            // Checking just /browser/ or just /templates/ might accidently trigger on
            // a book named "browser" or "templates".
            // Convert \ to / for Window's sake.  It shouldn't exist in a Linux folder path.
            return folderPath.Replace('\\', '/').Contains("/browser/templates/");
        }

        #region Image Files

        private readonly List<string> _brandingImageNames = new List<string>();

        /// <summary>
        /// Compare the images we find in the top level of the book folder to those referenced
        /// in the dom, and remove any unreferenced ones.
        /// </summary>
        public void CleanupUnusedImageFiles(bool keepFilesForEditing = true)
        {
            if (IsStaticContent(FolderPath))
                return;
            //Collect up all the image files in our book's directory
            var imageFiles = new List<string>();
            var imageExtentions = new HashSet<string>(new[] { ".jpg", ".png", ".svg" });
            var ignoredFilenameStarts = new HashSet<string>(
                new[]
                {
                    "thumbnail",
                    "license",
                    "video-placeholder",
                    "coverImage200",
                    "widget-placeholder"
                }
            );
            foreach (
                var path in RobustIO
                    .EnumerateFilesInDirectory(FolderPath)
                    .Where(s => imageExtentions.Contains(Path.GetExtension(s).ToLowerInvariant()))
            )
            {
                var filename = Path.GetFileName(path);
                if (
                    ignoredFilenameStarts.Any(
                        s => filename.StartsWith(s, StringComparison.InvariantCultureIgnoreCase)
                    )
                )
                    continue;
                if (filename.ToLowerInvariant() == "placeholder.png")
                    continue; // delete unused copies, but keep base placeholder image file even if unused at the moment
                imageFiles.Add(Path.GetFileName(GetNormalizedPathForOS(path)));
            }
            //Remove from that list each image actually in use
            var element = Dom.RawDom.DocumentElement;
            var pathsToNotDelete = GetImagePathsRelativeToBook(element);
            pathsToNotDelete.Add("epub-thumbnail.png"); // this may have been carefully crafted...
            pathsToNotDelete.Add("custom-thumbnail-256.png"); // this may have been carefully crafted as well...

            if (keepFilesForEditing)
            {
                //also, remove from the doomed list anything referenced in the datadiv that looks like an image
                //This saves us from deleting, for example, cover page images if this is called before the front-matter
                //has been applied to the document.
                pathsToNotDelete.AddRange(
                    from SafeXmlElement dataDivImage in Dom.RawDom.SafeSelectNodes(
                        "//div[@id='bloomDataDiv']//div[contains(text(),'.png') or contains(text(),'.jpg') or contains(text(),'.svg')]"
                    )
                    select UrlPathString
                        .CreateFromUrlEncodedString(dataDivImage.InnerText.Trim())
                        .PathOnly.NotEncoded
                );
                pathsToNotDelete.AddRange(_brandingImageNames);
            }

            foreach (var path in pathsToNotDelete)
            {
                imageFiles.Remove(GetNormalizedPathForOS(path)); //Remove just returns false if it's not in there, which is fine
            }

            //Delete any files still in the list
            foreach (var fileName in imageFiles)
            {
                var path = Path.Combine(FolderPath, fileName);
                try
                {
                    Debug.WriteLine("Removed unused image: " + path);
                    Logger.WriteEvent("Removed unused image: " + path);
                    RobustFile.Delete(path);
                }
                catch (Exception)
                {
                    Debug.WriteLine("Could not remove unused image: " + path);
                    Logger.WriteEvent("Could not remove unused image: " + path);
                    //It's not worth bothering the user about, we'll get it someday.
                    //We're not even doing a Debug.Fail because that makes it harder to unit test this condition.
                }
            }
        }

        /// <summary>
        /// Return the paths, relative to the book folder, of all the images referred to in the element.
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        internal static List<string> GetImagePathsRelativeToBook(SafeXmlElement element)
        {
            return (
                from SafeXmlElement img in HtmlDom.SelectChildImgAndBackgroundImageElements(element)
                select HtmlDom.GetImageElementUrl(img).PathOnly.NotEncoded
            )
                .Distinct()
                .ToList();
        }

        #endregion Image Files

        #region Audio Files
        /// <summary>
        /// Compare the audio we find in the audio folder in the book folder to those referenced
        /// in the dom, and remove any unreferenced ones.
        /// </summary>
        /// <param name="langsToExcludeAudioFor">If non-null and isForPublish=true, specifies the languages for which narration audio will not be included.</param>
        public void CleanupUnusedAudioFiles(
            bool isForPublish,
            HashSet<string> langsToExcludeAudioFor = null
        )
        {
            if (IsStaticContent(FolderPath))
                return;

            //Collect up all the audio files in our book's audio directory
            var audioFolderPath = AudioProcessor.GetAudioFolderPath(FolderPath);
            HashSet<string> audioFilesToDeleteIfNotUsed; // Should be a HashSet or something we can delete a specific item from repeatedly

            if (Directory.Exists(audioFolderPath))
            {
                if (isForPublish)
                {
                    // Find files that end with _timings.tsv and delete them from the publish folder (which is supposed to be different than the source folder)
                    var timingFilePaths = Directory.EnumerateFiles(
                        audioFolderPath,
                        "*_timings.tsv"
                    );
                    foreach (var timingFilePath in timingFilePaths)
                    {
                        RobustFile.Delete(timingFilePath);
                    }
                }

                // Review: do we only want to delete files in this directory if they have certain file extensions?
                audioFilesToDeleteIfNotUsed = new HashSet<string>(
                    Directory
                        .EnumerateFiles(audioFolderPath)
                        .Where(AudioProcessor.HasAudioFileExtension)
                        .Select(path => Path.GetFileName(GetNormalizedPathForOS(path)))
                );
            }
            else
            {
                audioFilesToDeleteIfNotUsed = new HashSet<string>();
            }

            //Look up which files could actually be used by the book
            var usedAudioFileNames = new HashSet<string>();

            var backgroundMusicFileNames = GetBackgroundMusicFileNamesReferencedInBook();
            usedAudioFileNames.AddRange(backgroundMusicFileNames);

            var activityPages = Dom.SafeSelectNodes("//div[@data-activity]");
            foreach (SafeXmlElement dap in activityPages)
            {
                var correctSound = dap.GetAttribute("data-correct-sound");
                var wrongSound = dap.GetAttribute("data-wrong-sound");
                if (correctSound != null)
                    usedAudioFileNames.Add(correctSound);
                if (wrongSound != null)
                    usedAudioFileNames.Add(wrongSound);
            }
            // These can now occur anywhere, not just in activity pages.
            var dataSoundElts = Dom.SafeSelectNodes(".//div[@data-sound]");
            foreach (var ds in dataSoundElts)
            {
                usedAudioFileNames.Add(ds.GetAttribute("data-sound"));
            }

            // Don't get too trigger-happy with the delete button if you're not in publish mode
            if (!isForPublish)
            {
                langsToExcludeAudioFor = null;
            }
            // re BL-7617: If we decide we want to clean up .wav files from earlier versions of Bloom, we just need to flip
            // the first boolean parameter here and fix up one test in BookStorageTests.cs:
            //   CleanupUnusedAudioFiles_BookHadUnusedAudio_AudiosRemoved()
            // var narrationAudioFileNames = GetNarrationAudioFileNamesReferencedInBook(false, includeSplitTextBoxAudio: !isForPublish, langsToExclude: langsToExcludeAudioFor);
            var narrationAudioFileNames = GetNarrationAudioFileNamesReferencedInBook(
                true,
                includeSplitTextBoxAudio: !isForPublish,
                langsToExclude: langsToExcludeAudioFor
            );
            usedAudioFileNames.AddRange(narrationAudioFileNames);

            audioFilesToDeleteIfNotUsed.ExceptWith(usedAudioFileNames);

            //Delete any files still in the list
            foreach (var fileName in audioFilesToDeleteIfNotUsed)
            {
                var path = Path.Combine(audioFolderPath, fileName);
                try
                {
                    Debug.WriteLine("Removed unused audio file: " + path);
                    Logger.WriteEvent("Removed unused audio file: " + path);
                    RobustFile.Delete(path);
                }
                catch (Exception ex) when (ex is IOException || ex is SecurityException)
                {
                    // It's not worth bothering the user about, we'll get it someday.
                    // We're not even doing a Debug.Fail because that makes it harder to unit test this condition.
                    Debug.WriteLine("Could not remove unused audio file: " + path);
                    Logger.WriteEvent("Could not remove unused audio file: " + path);
                }
            }
        }

        public void CleanupUnusedActivities()
        {
            var activityFolderPath = GetActivityFolderPath(FolderPath);
            if (!Directory.Exists(activityFolderPath))
                return;
            var activityFolders = Directory.GetDirectories(activityFolderPath);
            var wantedFolders = new HashSet<string>(GetActivityFolderNamesReferencedInBook());

            foreach (var folder in activityFolders)
            {
                if (wantedFolders.Contains(Path.GetFileName(folder)))
                    continue;
                SIL.IO.RobustIO.DeleteDirectoryAndContents(folder);
            }
        }

        /// <summary>
        /// Returns all possible file names for audio narration which are referenced in the DOM.
        /// This should include items from the data div.
        /// </summary>
        /// <param name="includeWav">Optionally include/exclude .wav files</param>
        public IEnumerable<string> GetNarrationAudioFileNamesReferencedInBook(bool includeWav)
        {
            return GetNarrationAudioFileNamesReferencedInBook(
                includeWav,
                includeSplitTextBoxAudio: false,
                langsToExclude: null
            );
        }

        /// <summary>
        /// Returns all possible file names for audio narration which are referenced in the DOM.
        /// This should include items from the data div.
        /// </summary>
        /// <param name="includeWav">Optionally include/exclude .wav files</param>
        /// <param name="includeSplitTextBoxAudio">True if the function should also return the filenames for text boxes which are not audio sentences but contain sub-elements which are (e.g. after a hard split of whole-text-box audio)</param>
        /// <param name="langsToExclude">If non-null, specifies the languages for which narration audio will not be included.</param>
        public IEnumerable<string> GetNarrationAudioFileNamesReferencedInBook(
            bool includeWav,
            bool includeSplitTextBoxAudio,
            HashSet<string> langsToExclude
        )
        {
            var narrationElements = HtmlDom.SelectChildNarrationAudioElements(
                Dom.RawDom.DocumentElement,
                includeSplitTextBoxAudio,
                langsToExclude
            );
            var narrationIds = narrationElements
                .Select(node => node.GetOptionalStringAttribute("id", null))
                .Where(id => id != null);

            // The dom only includes the ID, so we return all possible file names
            foreach (var narrationId in narrationIds)
                foreach (var filename in GetNarrationAudioFileNames(narrationId, includeWav))
                    yield return filename;
        }

        /// <summary>
        /// Given a narration ID, yields the associated audio filenames (just the filename, not the full path) for that narration ID
        /// </summary>
        internal static IEnumerable<string> GetNarrationAudioFileNames(
            string narrationId,
            bool includeWav
        )
        {
            var extensionsToInclude = AudioProcessor.NarrationAudioExtensions.ToList();
            if (!includeWav)
                extensionsToInclude.Remove(".wav");

            foreach (var extension in extensionsToInclude)
                // Should be a simple append, but previous code had ChangeExtension, so being defensive
                yield return GetNormalizedPathForOS(Path.ChangeExtension(narrationId, extension));
        }

        public IEnumerable<string> GetActivityFolderNamesReferencedInBook()
        {
            var widgetElements = Dom.SafeSelectNodes(
                "//*[contains(@class, 'bloom-widgetContainer')]/iframe"
            );
            foreach (SafeXmlElement elt in widgetElements)
            {
                // Saved as a relative URL, which means it's supposed to be encoded.
                var encodedSrc = elt.GetAttribute("src");

                // Decode the percent-encodings.
                var src = UrlPathString.CreateFromUrlEncodedString(encodedSrc).NotEncoded;

                yield return Path.GetFileName(Path.GetDirectoryName(src));
            }
        }

        /// <summary>
        /// Returns all file names for background music which are referenced in the DOM.
        /// This should include items from the data div.
        /// </summary>
        public IEnumerable<string> GetBackgroundMusicFileNamesReferencedInBook()
        {
            return Dom.GetBackgroundMusicFileNamesReferencedInBook();
        }

        #endregion Audio Files

        #region Video Files

        /// <summary>
        /// Compare the video we find in the video folder in the book folder to those referenced
        /// in the dom, and remove any unreferenced ones.
        /// </summary>
        public void CleanupUnusedVideoFiles()
        {
            if (IsStaticContent(FolderPath))
                return;

            //Collect up all the video files in our book's video directory
            var videoFolderPath = GetVideoFolderPath(FolderPath);
            var videoFilesToDeleteIfNotUsed = new List<string>();
            var videoExtensions = new HashSet<string> { ".mp4", ".webm" }; // .mov, .avi...?

            if (Directory.Exists(videoFolderPath))
            {
                foreach (var videoExtension in videoExtensions)
                {
                    foreach (
                        var path in Directory.EnumerateFiles(videoFolderPath, "*" + videoExtension)
                    )
                    {
                        videoFilesToDeleteIfNotUsed.Add(
                            Path.GetFileName(GetNormalizedPathForOS(path))
                        );
                    }
                }
            }

            //Remove from that list each video file actually in use
            var element = Dom.RawDom.DocumentElement;
            var usedVideoPaths = GetVideoPathsRelativeToBook(element);

            foreach (var relativeFilePath in usedVideoPaths) // relativeFilePath includes "video/"
            {
                if (Path.GetExtension(relativeFilePath).Length > 0)
                {
                    if (
                        videoExtensions.Contains(
                            Path.GetExtension(relativeFilePath).ToLowerInvariant()
                        )
                    )
                    {
                        videoFilesToDeleteIfNotUsed.Remove(
                            Path.GetFileName(GetNormalizedPathForOS(relativeFilePath))
                        ); //This call just returns false if not found, which is fine.
                    }
                }

                // if there is a .orig version of the used file, keep it too (by removing it from this list).
                const string origExt = ".orig";
                var tempfileName = Path.ChangeExtension(relativeFilePath, origExt);
                videoFilesToDeleteIfNotUsed.Remove(
                    Path.GetFileName(GetNormalizedPathForOS(tempfileName))
                );
            }
            //Delete any files still in the list
            foreach (var fileName in videoFilesToDeleteIfNotUsed)
            {
                var path = Path.Combine(videoFolderPath, fileName);
                try
                {
                    Debug.WriteLine("Removed unused video file: " + path);
                    Logger.WriteEvent("Removed unused video file: " + path);
                    RobustFile.Delete(path);
                }
                catch (Exception ex) when (ex is IOException || ex is SecurityException)
                {
                    // It's not worth bothering the user about, we'll get it someday.
                    // We're not even doing a Debug.Fail because that makes it harder to unit test this condition.
                    Debug.WriteLine("Could not remove unused video file: " + path);
                    Logger.WriteEvent("Could not remove unused video file: " + path);
                }
            }
        }

        internal static string GetVideoFolderName => "video/";

        internal static string GetVideoFolderPath(string bookFolderPath)
        {
            return Path.Combine(bookFolderPath, GetVideoFolderName);
        }

        internal static string GetVideoDirectoryAndEnsureExistence(string bookFolder)
        {
            var videoFolder = GetVideoFolderPath(bookFolder);
            if (!Directory.Exists(videoFolder))
            {
                try
                {
                    Directory.CreateDirectory(videoFolder);
                }
                catch (IOException error)
                {
                    ErrorReport.NotifyUserOfProblem(error, error.Message);
                }
            }

            return videoFolder;
        }

        internal static List<string> GetVideoPathsRelativeToBook(SafeXmlElement element)
        {
            return (
                from SafeXmlElement videoContainerElements in HtmlDom.SelectChildVideoElements(
                    element
                )
                select HtmlDom.GetVideoElementUrl(videoContainerElements).PathOnly.NotEncoded
            )
                .Where(path => !String.IsNullOrEmpty(path))
                .Distinct()
                .ToList();
        }

        #endregion Video Files

        public static string GetNormalizedPathForOS(string path)
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT
                ? path.ToLowerInvariant()
                : path;
        }

        private void AssertIsAlreadyInitialized()
        {
            if (Dom == null)
                throw new ApplicationException(
                    "BookStorage was at a place that should have been initialized earlier, but wasn't."
                );
        }

        public string SaveHtml(HtmlDom dom)
        {
            AssertIsAlreadyInitialized();
            string tempPath = GetNameForATempFileInStorageFolder();
            MakeCssLinksAppropriateForStoredFile(dom);
            SetBaseForRelativePaths(dom, String.Empty); // remove any dependency on this computer, and where files are on it.
            //CopyXMatterStylesheetsIntoFolder
            return XmlHtmlConverter.SaveDOMAsHtml5(dom.RawDom, tempPath);
        }

        /// <summary>
        /// Get a temporary file pathname in the current book's folder.  This is needed to ensure proper permissions are granted
        /// to the resulting file later after FileUtils.ReplaceFileWithUserInteractionIfNeeded is called.  That method may call
        /// RobustFile.Replace which replaces both the file content and the file metadata (permissions).  The result of that if we use
        /// the user's temp directory is described in http://issues.bloomlibrary.org/youtrack/issue/BL-3954.
        /// </summary>
        private string GetNameForATempFileInStorageFolder()
        {
            using (var temp = TempFile.InFolderOf(PathToExistingHtml))
            {
                return temp.Path;
            }
        }

        /// <summary>
        /// creates a relocatable copy of our main HtmlDom
        /// </summary>
        /// <returns></returns>
        public HtmlDom GetRelocatableCopyOfDom(bool withUpdatedStylesheets = true)
        {
            HtmlDom relocatableDom = Dom.Clone();

            SetBaseForRelativePaths(relocatableDom, FolderPath);
            if (withUpdatedStylesheets)
                EnsureHasLinksToStylesheets(relocatableDom);
            else
                relocatableDom.PreserveExistingStylesheets = true;

            return relocatableDom;
        }

        /// <summary>
        /// this one works on the dom passed to it
        /// </summary>
        /// <param name="dom"></param>
        /// <returns></returns>
        public HtmlDom MakeDomRelocatable(HtmlDom dom)
        {
            var relocatableDom = dom.Clone();

            SetBaseForRelativePaths(relocatableDom, FolderPath);
            EnsureHasLinksToStylesheets(relocatableDom);

            return relocatableDom;
        }

        private static bool ShouldWeChangeFolderName(
            string sanitizedFilename,
            string currentFolderName,
            string idealFolderName
        )
        {
            // As of 16 Dec 2019 we changed our definition of "sanitized" to include some more characters that can
            // cause problems in an HTML/XML-based app that passes information around in JSON and URLs.
            // We would like to make sure that if the further sanitization of the name changes what already exists,
            // we go ahead and do the rename.
            // The various cases of sanitized names and folders and existing book names (e.g. Foo1) are tricky
            // to tease apart. We can't just let it go, though, since the new higher level of sanitization is
            // necessary to keep bloom-player from throwing an error.

            // 1. If the current folder name needs sanitizing, we definitely can't use it; keep going.
            if (currentFolderName != SanitizeNameForFileSystem(currentFolderName))
                return true;
            // 2. If the name we are already using is the name we want, keep using it (don't change anything)
            if (currentFolderName == sanitizedFilename)
                return false;
            // 3. If our most prefered sanitized filename works (doesn't exist), go ahead and change to it; keep going.
            if (!Directory.Exists(idealFolderName))
                return true;
            // 4. If our current folder name is one of the possible work-arounds (because preferred name is in use),
            //    then return early and don't change. (We don't need to generate another alternative.)
            // 5. Otherwise change to some other (sanitized) name, ensuring availability.
            return !currentFolderName.StartsWith(sanitizedFilename);
        }

        public void SetBookName(string name)
        {
            if (!Directory.Exists(FolderPath)) //bl-290 (user had 4 month-old version, so the bug may well be long gone)
            {
                var msg = LocalizationManager.GetString(
                    "BookStorage.FolderMoved",
                    "It appears that some part of the folder path to this book has been moved or renamed. As a result, Bloom cannot save your changes to this page, and will need to exit now. If you haven't been renaming or moving things, please report the problem to us."
                );
                ErrorReport.NotifyUserOfProblem(
                    new ApplicationException(
                        $"In SetBookName('{name}'), BookStorage thinks the existing folder is '{FolderPath}', but that does not exist. (ref bl-290)"
                    ),
                    msg
                );
                // Application.Exit() is not drastic enough to terminate all the call paths here and all the code
                // that tries to make sure we save on exit. Get lots of flashing windows during shutdown.
                Environment.Exit(-1);
            }

            name = SanitizeNameForFileSystem(name);

            var currentFilePath = PathToExistingHtml;
            var currentFolderName = Path.GetFileNameWithoutExtension(currentFilePath);
            var idealFolderName = Path.Combine(Directory.GetParent(FolderPath).FullName, name);
            if (!ShouldWeChangeFolderName(name, currentFolderName, idealFolderName))
                return;

            // Figure out what name we're really going to use (might need to add a number suffix).
            idealFolderName = GetUniqueFolderPath(idealFolderName);

            // Next, rename the file
            Guard.Against(
                FolderPath.StartsWith(
                    BloomFileLocator.FactoryTemplateBookDirectory,
                    StringComparison.Ordinal
                ),
                "Cannot rename template books!"
            );
            Logger.WriteEvent(
                "Renaming html from '{0}' to '{1}.htm'",
                currentFilePath,
                idealFolderName
            );
            var newFilePath = Path.Combine(FolderPath, Path.GetFileName(idealFolderName) + ".htm");
            if (RobustFile.Exists(newFilePath))
            {
                // The folder already contains two HTML files, one with the name we were going to change to.
                // Just get rid of it.
                // (This is a weird state of affairs that should never occur but did once (BL-10200).
                // Extra HTML files in the book folder are an anomaly. We could recycle it, report it,
                // etc...but this has only happened once. I don't think it's worth it. Just clean up
                // and so prevent a crash.)
                RobustFile.Delete(newFilePath);
            }

            RobustFile.Move(currentFilePath, newFilePath);

            var fromToPair = new KeyValuePair<string, string>(FolderPath, idealFolderName);
            try
            {
                Logger.WriteEvent(
                    "Renaming folder from '{0}' to '{1}'",
                    FolderPath,
                    idealFolderName
                );

                //This one can't handle network paths and isn't necessary, since we know these are on the same volume:
                //SIL.IO.DirectoryUtilities.MoveDirectorySafely(FolderPath, newFolderPath);
                SIL.IO.RobustIO.MoveDirectory(FolderPath, idealFolderName);

                _fileLocator.RemovePath(FolderPath);
                _fileLocator.AddPath(idealFolderName);

                FolderPath = idealFolderName;
            }
            catch (Exception e)
            {
                Logger.WriteEvent("Failed folder rename: " + e.Message);
                Debug.Fail("(debug mode only): could not rename the folder");
            }

            RaiseBookRenamedEvent(fromToPair);

            OnFolderPathChanged();
        }

        /// <summary>
        /// This does the minimum notifications when a book's name is being restored to an earlier one.
        /// The caller is responsible to ensure that the name is Sanitized and the folder and HTML file
        /// are restored; this just updates the BookStorage's notion of where it is, and sends the
        /// notifications for which bookstorage is responsible about the change.
        /// </summary>
        /// <param name="restoredName"></param>
        public void RestoreBookName(string restoredName)
        {
            string restoredPath = Path.Combine(Path.GetDirectoryName(FolderPath), restoredName);
            var fromToPair = new KeyValuePair<string, string>(FolderPath, restoredPath);
            FolderPath = restoredPath;
            RaiseBookRenamedEvent(fromToPair);

            OnFolderPathChanged();
        }

        private void RaiseBookRenamedEvent(KeyValuePair<string, string> fromToPair)
        {
            // It's possible for books to get renamed in temp folders, for example,
            // a book which in the collection has a duplicate suffix in its folder path
            // may get that removed when a temp copy for publishing is brought up to date.
            // We only want to raise the event when a book actually in the collection
            // is updated. (In particular we don't want to rename the real book for
            // remote users of a TeamCollection when we were just renaming the copy
            // we were publishing.)
            if (FolderPath.StartsWith(_collectionSettings.FolderPath))
            {
                _bookRenamedEvent.Raise(fromToPair);
                BookTitleChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected virtual void OnFolderPathChanged()
        {
            FolderPathChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetValidateErrors()
        {
            if (!String.IsNullOrEmpty(ErrorMessagesHtml))
                return "";
            if (!Directory.Exists(FolderPath))
            {
                return "The directory (" + FolderPath + ") could not be found.";
            }
            if (!RobustFile.Exists(PathToExistingHtml))
            {
                return "Could not find an html file to use.";
            }

            return ValidateBook(Dom, PathToExistingHtml);
        }

        /// <summary>
        ///
        /// </summary>
        /// <remarks>The image-replacement feature is perhaps a one-off for a project where an advisor replaced the folders
        /// with a version that lacked most of the images (perhaps because dropbox copies small files first and didn't complete the sync)</remarks>
        /// <param name="progress"></param>
        /// <param name="pathToFolderOfReplacementImages">We'll find any matches in the entire folder, regardless of sub-folder name</param>
        public void CheckBook(IProgress progress, string pathToFolderOfReplacementImages = null)
        {
            var error = GetValidateErrors();
            if (!String.IsNullOrEmpty(error))
                progress.WriteError(error);

            //check for missing images

            foreach (
                SafeXmlElement imgNode in HtmlDom.SelectChildImgAndBackgroundImageElements(Dom.Body)
            )
            {
                var imageFileName = HtmlDom.GetImageElementUrl(imgNode).PathOnly.NotEncoded;
                if (String.IsNullOrEmpty(imageFileName))
                {
                    var classNames = imgNode.GetAttribute("class");
                    if (classNames == null || !classNames.Contains("licenseImage")) //bit of hack... it's ok for licenseImages to be blank
                    {
                        progress.WriteWarning("image src is missing");
                        //review: this, we could fix with a new placeholder... maybe in the javascript edit stuff?
                    }
                    continue;
                }
                // Certain .svg files (cogGrey.svg, FontSizeLetter.svg) aren't really part of the book and are stored elsewhere.
                // Also, at present the user can't insert them into a book. Don't report them.
                // TODO: if we ever allow the user to add .svg files, we'll need to change this
                if (
                    Path.HasExtension(imageFileName)
                    && Path.GetExtension(imageFileName).ToLowerInvariant() == ".svg"
                )
                    continue;

                // Branding images are handled in a special way in BrandingApi.cs.
                // Without this, we get "Warning: Image /bloom/api/branding/image is missing from the folder xxx" (see BL-3975)
                if (imageFileName.EndsWith(Bloom.Api.BrandingSettings.kBrandingImageUrlPart))
                    continue;

                //trim off the end of "license.png?123243"
                var startOfDontCacheHack = imageFileName.IndexOf('?');
                if (startOfDontCacheHack > -1)
                    imageFileName = imageFileName.Substring(0, startOfDontCacheHack);

                while (Uri.UnescapeDataString(imageFileName) != imageFileName)
                    imageFileName = Uri.UnescapeDataString(imageFileName);

                if (!RobustFile.Exists(Path.Combine(FolderPath, imageFileName)))
                {
                    if (!String.IsNullOrEmpty(pathToFolderOfReplacementImages))
                    {
                        if (
                            !AttemptToReplaceMissingImage(
                                imageFileName,
                                pathToFolderOfReplacementImages,
                                progress
                            )
                        )
                        {
                            progress.WriteWarning(
                                $"Could not find replacement for image {imageFileName} in {FolderPath}"
                            );
                        }
                    }
                    else
                    {
                        progress.WriteWarning(
                            $"Image {imageFileName} is missing from the folder {FolderPath}"
                        );
                    }
                }
            }
        }

        private bool AttemptToReplaceMissingImage(
            string missingFile,
            string pathToFolderOfReplacementImages,
            IProgress progress
        )
        {
            try
            {
                foreach (
                    var imageFilePath in Directory.GetFiles(
                        pathToFolderOfReplacementImages,
                        missingFile
                    )
                )
                {
                    RobustFile.Copy(imageFilePath, Path.Combine(FolderPath, missingFile));
                    progress.WriteMessage(
                        $"Replaced image {missingFile} from a copy in {pathToFolderOfReplacementImages}"
                    );
                    return true;
                }
                foreach (var dir in Directory.GetDirectories(pathToFolderOfReplacementImages))
                {
                    //				    doesn't really matter
                    //					if (dir == _folderPath)
                    //				    {
                    //						progress.WriteMessage("Skipping the directory of this book");
                    //				    }
                    if (AttemptToReplaceMissingImage(missingFile, dir, progress))
                        return true;
                }
                return false;
            }
            catch (Exception error)
            {
                progress.WriteException(error);
                return false;
            }
        }

        public void UpdateBookFileAndFolderName(CollectionSettings collectionSettings)
        {
            var title = Dom.Title;
            if (title != null)
            {
                SetBookName(title);
            }
        }

        #region Static Helper Methods

        public static string GetHtmCandidate(string folderPath)
        {
            return Path.Combine(folderPath, Path.GetFileName(folderPath) + ".htm");
        }

        public static string FindBookHtmlInFolder(string folderPath)
        {
            string p = GetHtmCandidate(folderPath);
            if (RobustFile.Exists(p))
                return p;
            p = Path.Combine(folderPath, Path.GetFileName(folderPath) + ".html");
            if (RobustFile.Exists(p))
                return p;

            if (!Directory.Exists(folderPath)) //bl-291 (user had 4 month-old version, so the bug may well be long gone)
            {
                //in version 1.012 I got this because I had tried to delete the folder on disk that had a book
                //open in Bloom.
                ErrorReport.NotifyUserOfProblem(
                    "There's a problem; Bloom can't save this book. Did you perhaps delete or rename the folder that this book is (was) in?"
                );
                throw new ApplicationException(
                    $"In FindBookHtmlInFolder('{folderPath}'), the folder does not exist. (ref bl-291)"
                );
            }

            //ok, so maybe they changed the name of the folder and not the htm. Can we find a *single* html doc?
            // BL-3572 when the only file in the directory is "Big Book.html", it matches both filters in Windows (tho' not in Linux?)
            // so Union works better here. (And we'll change the name of the book too.)
            // BL-8893 Sometimes users can get into a state where a template directory Bloom thinks it should
            // look in is closed to Bloom by system permissions. In that case, skip that directory.
            // This is the location that threw an exception in the case of the original user's problem.
            var candidates = GetAllHtmCandidates(folderPath).ToList();
            if (candidates.Count == 0)
                return string.Empty;

            // Remove HTML files that start with a period.  These can be created by MacOS for some users
            // (although Bloom doesn't run natively on MacOS).  See BL-11415.
            // Note that periods are stripped from the beginning and end of titles when creating file/folder
            // names in SanitizeNameForFileSystem()/RemoveDangerousCharacters().
            candidates.RemoveAll((path) => Path.GetFileName(path).StartsWith("."));
            if (candidates.Count == 0)
                return string.Empty;

            var decoyMarkers = new[]
            {
                "configuration",
                PrefixForCorruptHtmFiles, // Used to rename corrupt htm files before restoring backup
                "_conflict", // owncloud
                "[conflict]", // Google Drive
                "conflicted copy" // Dropbox
            };
            candidates.RemoveAll(
                (name) => decoyMarkers.Any(d => name.ToLowerInvariant().Contains(d))
            );
            if (candidates.Count == 1)
                return candidates[0];

            //template
            p = Path.Combine(folderPath, "templatePages.htm");
            if (RobustFile.Exists(p))
                return p;
            p = Path.Combine(folderPath, "templatePages.html");
            if (RobustFile.Exists(p))
                return p;

            return String.Empty;
        }

        private static IEnumerable<string> GetAllHtmCandidates(string folderPath)
        {
            try
            {
                return Directory
                    .GetFiles(folderPath)
                    // Although GetFiles supports simple pattern matching, it doesn't support enforcing end-of-string matches...
                    // So let's do the filtering this way instead, to make sure we don't get any extensions that start with "htm" but aren't exact matches.
                    .Where(name => name.EndsWith(".htm") || name.EndsWith(".html"));
            }
            catch (UnauthorizedAccessException uaex)
            {
                Logger.WriteError("Bloom folder access problem: ", uaex);
                return new List<string>();
            }
        }

        /// <summary>
        /// This method finds any .htm or .html files in the book folder that should not be published.
        /// </summary>
        /// <param name="folderPath"></param>
        /// <remarks>Internal for testing.</remarks>
        internal static IEnumerable<string> FindDeletableHtmFiles(string folderPath)
        {
            var theRealOne = FindBookHtmlInFolder(folderPath);
            var okayFiles = new List<string>
            {
                theRealOne,
                Path.Combine(folderPath, "configuration.htm")
            };
            var allCandidates = GetAllHtmCandidates(folderPath).ToList();
            allCandidates.RemoveAll(f => okayFiles.Contains(f));
            return allCandidates.Where(
                f => !Path.GetFileName(f).ToLowerInvariant().StartsWith("readme-")
            );
        }

        /// <summary>
        /// PublishHelper (ePUB and BloomPUB) and BloomS3Client (Upload) call this after copying a book
        /// to a staging folder.
        /// </summary>
        internal static void EnsureSingleHtmFile(string folderPath)
        {
            var badHtmFilesToDelete = FindDeletableHtmFiles(folderPath);
            foreach (string badFilePath in badHtmFilesToDelete)
            {
                RobustFile.Delete(badFilePath);
            }
        }

        public static void SetBaseForRelativePaths(HtmlDom dom, string folderPath)
        {
            dom.BaseForRelativePaths = GetBaseForRelativePaths(folderPath);
        }

        /// <summary>
        /// Base for relative paths when editing the book (not generating PDF or anything special).
        /// </summary>
        public string NormalBaseForRelativepaths => GetBaseForRelativePaths(FolderPath);

        private static string GetBaseForRelativePaths(string folderPath)
        {
            string path = "";
            if (!String.IsNullOrEmpty(folderPath))
            {
                //this is only used by relative paths, and only img src's are left relative.
                //we are redirecting through our build-in httplistener in order to make white backgrounds transparent
                // and possibly shrink
                //big images before giving them to gecko which has trouble with really hi-res ones
                //Some clients don't want low-res images and can suppress this by setting HtmlDom.UseOriginalImages.
                var uri = folderPath + Path.DirectorySeparatorChar;
                path = uri.ToLocalhost();
            }
            return path;
        }

        public string ValidateBook(string path)
        {
            var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path, false)); //with throw if there are errors
            return ValidateBook(dom, path);
        }

        private string ValidateBook(HtmlDom dom, string path)
        {
            Debug.WriteLine($"ValidateBook({path})");
            var msg = GetHtmlMessageIfVersionIsIncompatibleWithThisBloom(dom, path);
            return !String.IsNullOrEmpty(msg)
                ? msg
                : dom.ValidateBook(path, !BookInfo.IsSuitableForMakingTemplates);
        }

        #endregion


        // It is remotely possible (see comment on ExpensiveInitialization) that this might be
        // called repeatedly on the same book. This could result in many open error message dialogs
        // at the same time reporting the same error.  So we keep track of what we've complained about
        // already to prevent this from happening.
        private static HashSet<string> _booksWithMultipleHtmlFiles = new HashSet<string>();

        /// <summary>
        /// An old comment says that ExpensiveInitialization is called repeatedly during idle time, but
        /// a quick test (Dec 2023) indicates this is not the case; it is called whenever we create a book
        /// object, but we generally only do that for the selected book. One exception is when the book folder
        /// does not contain a thumbnail; we create a book object in order to find the cover image.
        /// However, it should be very rare for this to be missing.
        /// Another is when we create a template book in order to display its pages in the Add Page dialog.
        /// Thus, the main reason to call this is when we are selecting a book and about to show its
        /// preview. This means it has basically the same purpose as Book.BringBookUpToDateMemory().
        /// We would like to fold its functionality into that method, so don't add new functionality
        /// here, at least without checking with JohnT, JohnH, or Andrew.
        /// Note that this routine is theoretically bound by the same constraint as Book.BringBookUpToDatePreview(),
        /// that it ought not to modify anything in the book folder, at least not if we're not allowed
        /// to save the book.  Ideally this routine should not modify anything in the book folder.
        /// For now, we're making an exception if things are so bad that we need to restore a backup.
        /// </summary>
        private void ExpensiveInitialization()
        {
            Dom = new HtmlDom();
            //the fileLocator we get doesn't know anything about this particular book.
            _fileLocator.AddPath(FolderPath);
            RobustIO.RequireThatDirectoryExists(FolderPath);
            string pathToExistingHtml;
            try
            {
                pathToExistingHtml = PathToExistingHtml;
            }
            catch (UnauthorizedAccessException error)
            {
                ShowAccessDeniedErrorHtml(error);
                return;
            }
            catch (Exception ex)
            {
                Logger.WriteError("Exception finding HTML file in " + FolderPath, ex);
                // So far, this has proved rare enough that I don't think it's worth localizing.
                var message = EncodeAndJoinStringsForHtml(
                    new[]
                    {
                        "Bloom had a problem finding the HTML file in "
                            + FolderPath
                            + ". You may need technical help in sorting this out.",
                        ex.Message
                    }
                );

                ErrorMessagesHtml = message;
                _errorAlreadyContainsInstructions = true;
                ErrorAllowsReporting = true;
                return;
            }
            var backupPath = GetBackupFilePath();

            if (Utils.LongPathAware.GetExceedsMaxPath(pathToExistingHtml))
            {
                Utils.LongPathAware.ReportLongPath(pathToExistingHtml);
                return;
            }

            // If we have a single html file, or an html file whose name matches the folder, then we can proceed.
            // ALternatively, if we want to fully update the book and we have a backup file, then we'll use that to proceed.
            // If neither of these cases apply, then we'll need to complain to the user and hope that he or she can
            // figure out how to recover since it's beyond what a program can handle.  (probably multiple html files
            // that don't match the folder name and no backup file in the folder)
            if (!RobustFile.Exists(pathToExistingHtml) && !RobustFile.Exists(backupPath))
            {
                ErrorAllowsReporting = false;
                // Error out
                var files = new List<string>(Directory.GetFiles(FolderPath));
                var htmlCount = 0;
                foreach (var f in files)
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".htm" || ext == ".html")
                        ++htmlCount;
                }
                if (htmlCount > 1)
                {
                    // Note: Corresponding error messages have not be localized, so this one isn't either.
                    ErrorMessagesHtml = "More than one html file in the book's folder";
                    if (_booksWithMultipleHtmlFiles.Contains(FolderPath))
                        return; // we already complained about this book.
                    _booksWithMultipleHtmlFiles.Add(FolderPath);
                    var b = new StringBuilder();
                    var msg1 = LocalizationManager.GetString(
                        "Errors.MultipleHtmlFiles",
                        "Bloom book folders must have only one file ending in .htm.  In the book \"{0}\", these are the files ending in .htm:",
                        "{0} will get the name of the book"
                    );
                    var msg2 = LocalizationManager.GetString(
                        "Errors.RemoveExcessHtml",
                        "Please remove all but one of the files ending in .htm.  Bloom will now open this folder for you.",
                        "This follows the Errors.MultipleHtmlFiles message and a list of HTML files."
                    );
                    b.AppendLineFormat(msg1, Path.GetFileName(FolderPath));
                    foreach (var f in files)
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".htm" || ext == ".html")
                            b.AppendLine("  * " + Path.GetFileName(f));
                    }
                    b.AppendLine(msg2);
                    ErrorReport.NotifyUserOfProblem(b.ToString());
                    // Open the file explorer on the book's folder to allow the user to delete (or move?) the unwanted file.
                    // The new process should use the current culture, so we don't need to worry about that.
                    // We don't wait for this to finish, so we don't use the CommandLineRunner methods.
                    ProcessExtra.SafeStartInFront(FolderPath);
                }
                else
                {
                    // This was  observed to happen at the end of downloading a very large book (BL-12034).
                    // It's also of course possible that the user has been messing with the book folder,
                    // or that we have a bug. It's rare enough that we don't feel a need to make it a super-nice
                    // message, but it should give some useful information and not crash.
                    // Generally this will not be seen, because Bloom does not create a book icon in a collection
                    // at all for a folder that does not contain at least one HTM{L} file.
                    // If it IS seen, currently it will appear as ErrorMessagesHtml, so it needs to be valid HTML,
                    // and to use <br> for newlines. If we manage to get NotifyUserOfProblem working in this
                    // situation, we probably need to switch back to newlines.
                    var b = new StringBuilder();
                    b.AppendLine("Could not find an html file in the folder to use.<br/>");
                    if (files.Count == 0)
                    {
                        b.AppendLine("***There are no files.<br>");
                    }
                    else
                    {
                        b.AppendLine("Files in this book are:");
                        foreach (var f in files)
                            b.AppendLine("<br/>" + f);
                    }
                    ErrorMessagesHtml = b.ToString();
                    // This freezes the app and does not show. I can't figure out why. We plan to come back to this in 5.6 (BL-12034)
                    //ErrorReport.NotifyUserOfProblem(ErrorMessagesHtml);

                    // It's tempting to throw here, but then the constructor fails,
                    // and we get a very confusing stack dump because usually creating
                    // a BookStorage happens deep in the depths of AutoFac (BL-12034).
                    // So I'm going for returning a BookStorage in an error state, though
                    // that has some problems too.
                    return;
                }
            }
            else
            {
                SafeXmlDocument xmlDomFromHtmlFile;
                try
                {
                    xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(
                        PathToExistingHtml,
                        false
                    );
                }
                catch (UnauthorizedAccessException error)
                {
                    ShowAccessDeniedErrorHtml(error);
                    return;
                }
                catch (Exception error)
                {
                    InitialLoadErrors = error.Message;
                    // If the user is actually trying to use this book and it's broken, we will try to restore a backup.
                    // The main reason not to do this otherwise is that we think we should notify the user that we
                    // are restoring a backup, and we don't want to bother him with such notifications about books
                    // he isn't looking at (or uploading) currently.
                    if (TryGetValidXmlDomFromHtmlFile(backupPath, out xmlDomFromHtmlFile))
                    {
                        RestoreBackup(pathToExistingHtml, error);
                    }
                    else
                    {
                        ErrorMessagesHtml = WebUtility.HtmlEncode(error.Message);
                        ErrorAllowsReporting = true;
                        Logger.WriteEvent("*** ERROR in " + PathToExistingHtml);
                        Logger.WriteEvent(
                            "*** ERROR: " + error.Message.Replace("{", "{{").Replace("}", "}}")
                        );
                        return;
                    }
                }

                Dom = new HtmlDom(xmlDomFromHtmlFile); //with throw if there are errors
                // Don't let spaces between <strong>, <em>, or <u> elements be removed. (BL-2484)
                Dom.RawDom.PreserveWhitespace = true;

                // An earlier comment warned that this was taking 1/3 of startup time. However, it was being done anyway
                // at some point where the Book constructor wanted to know whether the book was editable (which
                // triggers a check since books that don't validate aren't editable).
                // Hopefully this is OK since another old comment said,
                // "We did in fact change things so that storage isn't used until we've shown all the thumbnails we can
                // (then we go back and update in background)."
                InitialLoadErrors = ValidateBook(Dom, pathToExistingHtml);
                if (!string.IsNullOrEmpty(InitialLoadErrors))
                {
                    SafeXmlDocument possibleBackupDom;
                    if (TryGetValidXmlDomFromHtmlFile(backupPath, out possibleBackupDom))
                    {
                        RestoreBackup(
                            pathToExistingHtml,
                            new ApplicationException(
                                "main html file was not valid: " + InitialLoadErrors
                            )
                        );
                        xmlDomFromHtmlFile = possibleBackupDom;
                        Dom = new HtmlDom(xmlDomFromHtmlFile);
                    }
                }

                // For now, we really need to do this check, at least. This will get picked up by the Book later (feeling kludgy!)
                // I assume the following will never trigger (I also note that the dom isn't even loaded):

                if (!string.IsNullOrEmpty(ErrorMessagesHtml))
                {
                    Dom.RawDom.LoadXml(
                        "<html><body>There is a problem with the html structure of this book which will require expert help.</body></html>"
                    );
                    Logger.WriteEvent(
                        "{0}: There is a problem with the html structure of this book which will require expert help: {1}",
                        PathToExistingHtml,
                        ErrorMessagesHtml
                    );
                }
                // The following is a patch pushed out on the 25th build after 1.0 was released in order to give us
                // a bit of backwards version protection (I should have done this originally and in a less kludgy fashion
                // than I'm forced to do now).
                else
                {
                    var incompatibleVersionMessage =
                        GetHtmlMessageIfVersionIsIncompatibleWithThisBloom(Dom, PathToExistingHtml);
                    if (!string.IsNullOrWhiteSpace(incompatibleVersionMessage))
                    {
                        ErrorMessagesHtml = incompatibleVersionMessage;
                        Logger.WriteEvent("*** ERROR: " + incompatibleVersionMessage);
                        _errorAlreadyContainsInstructions = true;
                        ErrorAllowsReporting = true;
                        return;
                    }

                    var incompatibleFeatureMessage = GetHtmlMessageIfFeatureIncompatibility();
                    if (!string.IsNullOrWhiteSpace(incompatibleFeatureMessage))
                    {
                        ErrorMessagesHtml = incompatibleFeatureMessage;
                        Logger.WriteEvent("*** ERROR: " + incompatibleFeatureMessage);
                        _errorAlreadyContainsInstructions = true;
                        // This doesn't reflect any corruption or bugs in the code, so no reporting button needed.
                        ErrorAllowsReporting = false;
                        return;
                    }

                    Logger.WriteEvent("BookStorage Loading Dom from {0}", PathToExistingHtml);
                }

                // probably not needed at runtime if !fullyUpdateBookFiles, but one unit test relies on it having been done, and is very fast, so ok.
                Dom.UpdatePageDivs();
            }
        }

        public void ReloadFromDisk(string renamedTo, Action betweenReloadAndEvents)
        {
            var fromToPair = new KeyValuePair<string, string>(FolderPath, renamedTo);
            if (renamedTo != null)
                FolderPath = renamedTo;
            ExpensiveInitialization();

            betweenReloadAndEvents();

            if (renamedTo != null)
            {
                // The reload has renamed the book. We need to do the usual side effects.
                RaiseBookRenamedEvent(fromToPair);
                OnFolderPathChanged();
            }
        }

        public void CleanupUnusedSupportFiles(
            bool isForPublish,
            HashSet<string> langsToExcludeAudioFor = null
        )
        {
            CleanupUnusedImageFiles(!isForPublish);
            CleanupUnusedAudioFiles(isForPublish, langsToExcludeAudioFor);
            CleanupUnusedVideoFiles();
            CleanupUnusedActivities();
        }

        private void RestoreBackup(string pathToExistingHtml, Exception error)
        {
            var backupPath = GetBackupFilePath();
            string corruptFilePath = GetUniqueFileName(FolderPath, PrefixForCorruptHtmFiles, "htm");
            // BL-6099 it could be missing altogether if we had a bad crash or someone's anti-virus is acting up.
            if (String.IsNullOrEmpty(pathToExistingHtml))
            {
                RobustFile.Copy(backupPath, GetHtmCandidate(Path.GetDirectoryName(backupPath)));
            }
            else
            {
                RobustFile.Move(PathToExistingHtml, corruptFilePath);
                RobustFile.Move(backupPath, pathToExistingHtml);
            }
            var msg = LocalizationManager.GetString(
                "BookStorage.CorruptBook",
                "Bloom had a problem reading this book and recovered by restoring a recent backup. Please check recent changes to this book. If this happens for no obvious reason, please report it to us."
            );
            ErrorReport.NotifyUserOfProblem(error, msg);
            // We've restored a validated backup, so as far as the caller is concerned we have a good book.
            InitialLoadErrors = "";
        }

        private bool TryGetValidXmlDomFromHtmlFile(
            string path,
            out SafeXmlDocument xmlDomFromHtmlFile
        )
        {
            xmlDomFromHtmlFile = null;
            if (!RobustFile.Exists(path))
                return false;
            try
            {
                xmlDomFromHtmlFile = XmlHtmlConverter.GetXmlDomFromHtmlFile(path, false);
                return String.IsNullOrEmpty(ValidateBook(new HtmlDom(xmlDomFromHtmlFile), path));
            }
            catch (Exception error)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(error);
                return false;
            }
        }

        /// <summary>
        /// Reports an UnauthorizedAccessException to the user using the book storage instance's html
        /// </summary>
        internal void ShowAccessDeniedErrorHtml(UnauthorizedAccessException error)
        {
            var message = GetAccessDeniedHtml(error, writeLog: true);

            ErrorMessagesHtml = message;
            _errorAlreadyContainsInstructions = true;
            ErrorAllowsReporting = true;
        }

        /// <summary>
        /// Reports an UnauthorizedAccessException to the user using the Problem Report Dialog
        /// You can use this when you don't have easy access to the book storage's html displayed at load time.
        /// </summary>
        internal static void ShowAccessDeniedErrorReport(UnauthorizedAccessException noAccessError)
        {
            // WriteLog is false because Problem Report Dialog will log it anyway
            string summaryHtml = GetAccessDeniedHtml(noAccessError, writeLog: false);

            // Even though there's not really anything we can fix if we get a bug report,
            // we still set showSendReport=true because false uses a MessageBox for which the user
            // can't use the mouse to select text to copy/paste
            NonFatalProblem.Report(
                ModalIf.All,
                PassiveIf.None,
                summaryHtml,
                "",
                noAccessError,
                showSendReport: true,
                isShortMessagePreEncoded: true
            );
        }

        private static string GetAccessDeniedHtml(
            UnauthorizedAccessException noAccessError,
            bool writeLog
        )
        {
            // In the details shown to the user, don't need the extra diagnostic info in BloomUnauthorizedAccessException
            // (However, in the error report submitted to YouTrack, we do want that extra diagnostic info)
            Exception errorToUser = noAccessError;
            if (noAccessError is BloomUnauthorizedAccessException)
                errorToUser = noAccessError.InnerException;

            var deniedAccessMsg = LocalizationManager.GetString(
                "Errors.DeniedAccess",
                "Your computer denied Bloom access to the book. You may need technical help in setting the operating system permissions for this file."
            );

            var messagesForLog = new List<string>() { deniedAccessMsg, errorToUser.Message };

            if (writeLog)
            {
                string messageForLog = String.Join(Environment.NewLine, messagesForLog);
                Logger.WriteEvent("*** ERROR: " + messageForLog);
            }

            string encodedMessageForLog = EncodeAndJoinStringsForHtml(messagesForLog);

            string encodedSeeAlsoMsg = GetHelpLinkForFilePermissions();

            return $"{encodedMessageForLog}<br />{encodedSeeAlsoMsg}";
        }

        private static string GetHelpLinkForFilePermissions()
        {
            var helpUrl =
                @"http://community.bloomlibrary.org/t/how-to-fix-file-permissions-problems/78";
            return GetEncodedSeeWebPageString(helpUrl);
        }

        private static string EncodeAndJoinStringsForHtml(IEnumerable<string> unencodedStrings)
        {
            return String.Join("<br />", unencodedStrings.Select(WebUtility.HtmlEncode));
        }

        private static string GetEncodedSeeWebPageString(string webPageUrl)
        {
            var seeAlsoFormatStr = LocalizationManager.GetString("Common.SeeWebPage", "See {0}.");
            // FYI: The braces in the format string are not encoded by HtmlEncode.
            var encodedSeeAlsoFormatStr = WebUtility.HtmlEncode(seeAlsoFormatStr);

            // Theoretically, '&' characters in the URL could cause problems.
            string encodedWebPageUrl = WebUtility.HtmlEncode(webPageUrl);
            var helpUrlLink = $"<a href='{webPageUrl}'>{encodedWebPageUrl}</a>";

            var encodedSeeWebPageMsg = String.Format(encodedSeeAlsoFormatStr, helpUrlLink);
            return encodedSeeWebPageMsg;
        }

        /// <summary>
        /// Update our book folder to contain all the support files that should be copied there.
        /// </summary>
        public void UpdateSupportFiles()
        {
            if (IsStaticContent(FolderPath))
                return; // don't try to update our own templates, it's a waste and they might be locked.
            if (IsPathReadonly(FolderPath))
            {
                Logger.WriteEvent(
                    "Not updating files in folder {0} because the directory is read-only.",
                    FolderPath
                );
                return;
            }

            // We want current shipping versions of these copied into the destination folder always.
            var supportFilesToUpdate = new List<string>(
                new[]
                {
                    "placeHolder.png",
                    BookInfo.AppearanceSettings.BasePageCssName,
                    "previewMode.css",
                    "origami.css"
                }
            );

            // Now we want to look for any other .css files already in the book folder and update them.
            // There are a few we want to skip, however.
            // In BL-5824, we got bit by design decisions we made that allow stylesheets installed via
            // bloompack and by new Bloom versions to replace local ones. This was done so that we could
            // send out new Bloom implementation stylesheets via bloompack and in new Bloom versions
            // and have those used in all the books. This works well for most stylesheets.
            //
            // But customBookStyles.css, customBookStyles2.css,  and customCollectionStyles.css are exceptions;
            // their whole purpose is to let the local book or collection override Bloom's normal
            // behavior or anything in a bloompack.
            //
            // And defaultLangStyles.css is another file that should not be updated because it is always
            // generated from the local collection settings.
            //
            // Also, we don't want to update branding.css here because the default update process may pull it from
            // who knows where; it doesn't come from one of the directories we search early.
            // Instead, normally one is fetched from the right branding in LoadCurrentBrandingFilesIntoBookFolder,
            // or if the branding is under development we generate a placeholder, or if there is no branding
            // we generate an empty placeholder.
            var cssFilesToSkipInThisPhase = new HashSet<string>();
            cssFilesToSkipInThisPhase.AddRange(BookStorage.CssFilesThatAreDynamicallyUpdated);
            // We don't need to consider these now because they are already listed to be copied in.
            cssFilesToSkipInThisPhase.AddRange(supportFilesToUpdate);
            // In this phase of scanning the book directory, we will delete most xmatter stylesheets.
            // But not the current one! That we DO want to copy in.
            var xmatterCss = Path.GetFileName(XMatterHelper.PathToXMatterStylesheet);
            cssFilesToSkipInThisPhase.Add(xmatterCss); // prevent deleting it
            supportFilesToUpdate.Add(xmatterCss); // make sure it gets copied in

            foreach (var path in Directory.GetFiles(FolderPath, "*.css"))
            {
                var file = Path.GetFileName(path);
                if (cssFilesToSkipInThisPhase.Contains(file))
                    continue;
                // clean up any unwanted Xmatter CSS files. The one we want is already skipped.
                // Get rid of any versions of basePage.css that aren't in cssFilesToSkipInThisPhase
                if (file.EndsWith("XMatter.css") || file.StartsWith("basePage"))
                    RobustFile.Delete(path);
                else
                    supportFilesToUpdate.Add(file);
            }

            // This will pull them into the book folder.
            foreach (var file in supportFilesToUpdate)
            {
                var sourcePath = GetSupportingFile(file);
                var destPath = Path.Combine(FolderPath, file);
                if (sourcePath == destPath)
                    continue; // don't copy from ourselves
                if (!string.IsNullOrEmpty(sourcePath) && RobustFile.Exists(sourcePath))
                    RobustFile.Copy(sourcePath, destPath, true);
            }

            LoadCurrentBrandingFilesIntoBookFolder();
            BookInfo.AppearanceSettings.WriteCssToFolder(
                FolderPath,
                BrandingAppearanceSettings,
                XmatterAppearanceSettings
            );
        }

        public ExpandoObject XmatterAppearanceSettings =>
            XMatterSettings.GetSettingsOrNull(XMatterHelper.PathToXMatterSettings)?.Appearance;

        /// <summary>
        /// Is it OK to use legacy themes with this xmatter? Note that this defaults TRUE if
        /// there is no xmatter.json or it does not contain this field.
        /// </summary>
        public bool LegacyThemeCanBeUsed =>
            XMatterSettings
                .GetSettingsOrNull(XMatterHelper.PathToXMatterSettings)
                ?.LegacyThemeCanBeUsed ?? true;

        // Typically, harvester will not change the theme of a book so as not to potentially break things.
        // But for certain cases, such as books which cannot use legacy but are uploaded before the appearance
        // system, we have determined it is better to harvest them as default than refuse to harvest them at all.
        public bool HarvesterMayConvertToDefaultTheme =>
            XMatterSettings
                .GetSettingsOrNull(XMatterHelper.PathToXMatterSettings)
                ?.HarvesterMayConvertToDefaultTheme ?? false;

        public ExpandoObject BrandingAppearanceSettings =>
            Api.BrandingSettings
                .GetSettingsOrNull(CollectionSettings.BrandingProjectKey)
                ?.Appearance;

        // Brandings come with logos and such... we want them in the book folder itself so that they work
        // apart from Bloom and in web browsing, ePUB, and BloomPUB contexts.
        private void LoadCurrentBrandingFilesIntoBookFolder()
        {
            _brandingImageNames.Clear();
            try
            {
                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-6516 and https://issues.bloomlibrary.org/youtrack/issue/BL-6852.
                // On Linux installations, files can never be copied to the "FactoryCollectionsDirectory" or any of its subfolders
                // (like "FactoryTemplateBookDirectory" or "SampleShellsDirectory").  If Bloom is installed "for all users" on Windows,
                // it is also impossible to copy files there.  Copying files to those locations would allow Bloom to show branding for
                // a template preview or a sample shell preview, which seems rather unimportant.
                // Review: the above is no longer a problem, because we're keeping the branding files in memory.
                // Do we WANT our factory templates to show what they will look like in the current branding? If so we can just remove this.
                if (
                    FolderPath.StartsWith(
                        BloomFileLocator.FactoryCollectionsDirectory,
                        StringComparison.Ordinal
                    )
                )
                    return;
                var key = _collectionSettings.BrandingProjectKey;
                // I think this is redundant: BrandingProjectKey will be set to 'Default' if we don't have some definite one.
                // Keeping this for paranoia, in case there's some path I don't know about where that doesn't happen.
                if (String.IsNullOrEmpty(key))
                    key = "Default"; // The "default" Branding folder contains the branding-type stuff for non-enterprise books.
                var brandingFolder = BloomFileLocator.GetBrandingFolder(key);
                if (String.IsNullOrEmpty(brandingFolder))
                {
                    // This special "branding" contains a message about being patient until the branding ships.
                    // Its purpose is to allow us to release a new branding code even before we release a version
                    // of Bloom that properly supports it. (Note that it is, purposely, not localizable; it's only
                    // intended to be seen by a few administrators until the branding ships.)
                    brandingFolder = BloomFileLocator.GetBrandingFolder("Missing");
                }

                var filesToCopy = Directory
                    .EnumerateFiles(brandingFolder) //<--- .NET 4.5
                    // note this is how the branding.css gets into a book folder
                    .Where(
                        path =>
                            ".png,.svg,.jpg,.css"
                                .Split(',')
                                .Contains(Path.GetExtension(path).ToLowerInvariant())
                    );
                var gotBrandingCss = false;
                foreach (var sourcePath in filesToCopy)
                {
                    var fileName = Path.GetFileName(sourcePath);
                    var destPath = Path.Combine(FolderPath, fileName);
                    Utils.LongPathAware.ThrowIfExceedsMaxPath(destPath); //example: BL-8284
                    RobustFile.Copy(sourcePath, destPath, true);
                    if (fileName.EndsWith(".css"))
                    {
                        gotBrandingCss |= fileName == "branding.css";
                    }
                    else
                    {
                        _brandingImageNames.Add(fileName);
                    }
                }

                // Typically the above will copy a branding.css into the book folder.
                // Check that, and attempt to recover if it didn't happen.
                if (!gotBrandingCss)
                {
                    Debug.Fail("Brandings MUST provide a branding.css");
                    // An empty branding.css is better than having the file server search who-knows-where
                    // and coming up with some arbitrary branding.css. At least all Bloom installations,
                    // including the evil dev one that introduced a branding without the required file,
                    // will behave the same.
                    RobustFile.WriteAllText(Path.Combine(FolderPath, "branding.css"), "");
                }
            }
            catch (Exception err)
            {
                ProblemReportApi.ShowProblemDialog(
                    null,
                    err,
                    "There was a problem applying the branding: "
                        + _collectionSettings.BrandingProjectKey,
                    "nonfatal"
                );
            }
        }

        private XMatterHelper _xMatterHelper;
        private string _cachedXmatterPackName;
        private HtmlDom _cachedXmatterDom;
        private BookInfo _cachedXmatterBookInfo;

        private XMatterHelper XMatterHelper
        {
            get
            {
                var nameOfCollectionXMatterPack = _collectionSettings.XMatterPackName;
                // _fileLocator, BookInfo, and BookInfo.UseDeviceXMatter are never changed after being set in
                // constructors, so we don't need to consider that they might be different.
                // The other two things the helper depends on are also unlikely to change, but it may be
                // possible, so we'll play safe.
                if (
                    _cachedXmatterPackName != nameOfCollectionXMatterPack
                    || _cachedXmatterDom != Dom
                    || _cachedXmatterBookInfo != BookInfo
                )
                {
                    _cachedXmatterPackName = nameOfCollectionXMatterPack; // before mod, to match check above
                    nameOfCollectionXMatterPack = HandleRetiredXMatterPacks(
                        Dom,
                        nameOfCollectionXMatterPack
                    );
                    //Here the xmatter Helper may come back loaded with the xmatter from the collection settings, but if the book
                    //specifies a different one, it will come back with that (if it can be found).
                    _xMatterHelper = new XMatterHelper(
                        Dom,
                        nameOfCollectionXMatterPack,
                        _fileLocator,
                        BookInfo.UseDeviceXMatter
                    );
                    _cachedXmatterDom = Dom;
                    _cachedXmatterBookInfo = BookInfo;
                }
                return _xMatterHelper;
            }
        }

        public string PathToXMatterStylesheet => XMatterHelper.PathToXMatterStylesheet;

        private bool IsPathReadonly(string path)
        {
            return (RobustFile.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }

        public void Update(string sourceFileName, string sourcePathIncludingFileName = "")
        {
            var destinationName = sourceFileName; // preserve the destination name in case we redirect to another sourceFile to support legacy books
            if (!IsUserOrTempFolder)
            {
                if (
                    sourceFileName.ToLowerInvariant().Contains("xmatter")
                    && !sourceFileName.ToLower().StartsWith("factory-xmatter")
                )
                {
                    return; //we don't want to copy custom xmatters around to the program files directory, template directories, the Bloom src code folders, etc.
                }
            }

            // do not attempt to copy files to the installation directory
            var targetDirInfo = new DirectoryInfo(FolderPath);
            if (Platform.IsMono)
            {
                // do not attempt to copy files to the "/usr" directory
                if (targetDirInfo.FullName.StartsWith("/usr"))
                    return;
            }
            else
            {
                // do not attempt to copy files to the "Program Files" directory
                var programFolderPath = Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                );
                if (!String.IsNullOrEmpty(programFolderPath))
                {
                    var programsDirInfo = new DirectoryInfo(programFolderPath);
                    if (
                        String.Compare(
                            targetDirInfo.FullName,
                            programsDirInfo.FullName,
                            StringComparison.InvariantCultureIgnoreCase
                        ) == 0
                    )
                        return;
                }

                // do not attempt to copy files to the "Program Files (x86)" directory either
                if (Environment.Is64BitOperatingSystem)
                {
                    programFolderPath = Environment.GetFolderPath(
                        Environment.SpecialFolder.ProgramFilesX86
                    );
                    if (!String.IsNullOrEmpty(programFolderPath))
                    {
                        var programsDirInfo = new DirectoryInfo(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                        );
                        if (
                            String.Compare(
                                targetDirInfo.FullName,
                                programsDirInfo.FullName,
                                StringComparison.InvariantCultureIgnoreCase
                            ) == 0
                        )
                            return;
                    }
                }
            }

            string documentPath = "notSet";
            try
            {
                if (String.IsNullOrEmpty(sourcePathIncludingFileName))
                {
                    sourcePathIncludingFileName = _fileLocator.LocateFile(sourceFileName);
                }
                if (String.IsNullOrEmpty(sourcePathIncludingFileName)) //happens during unit testing
                    return;

                documentPath = Path.Combine(FolderPath, destinationName);
                if (!RobustFile.Exists(documentPath))
                {
                    Logger.WriteMinorEvent(
                        "BookStorage.Update() Copying missing file {0} to {1}",
                        sourcePathIncludingFileName,
                        documentPath
                    );

                    // get rid of previous xmatter stylesheets
                    if (destinationName.ToLowerInvariant().Contains("xmatter"))
                        RemoveExistingFilesBySuffix("XMatter.css");

                    RobustFile.Copy(sourcePathIncludingFileName, documentPath);
                    //if the source was locked, don't copy the lock over
                    RobustFile.SetAttributes(documentPath, FileAttributes.Normal);
                    return;
                }
                // due to BL-2166, we no longer compare times since downloaded books often have
                // more recent times than the DistFiles versions we want to use
                // var documentTime = RobustFile.GetLastWriteTimeUtc(documentPath);
                if (Platform.IsWindows) // See BL-13577.
                {
                    if (
                        sourcePathIncludingFileName.ToLowerInvariant()
                        == documentPath.ToLowerInvariant()
                    )
                        return; // no point in trying to update self!
                }
                else
                {
                    if (sourcePathIncludingFileName == documentPath)
                        return; // no point in trying to update self!
                }
                if (IsPathReadonly(documentPath))
                {
                    var msg =
                        $"Could not update one of the support files in this document ({documentPath}) because the destination was marked ReadOnly.";
                    Logger.WriteEvent(msg);
                    ErrorReport.NotifyUserOfProblem(msg);
                    return;
                }
                Logger.WriteMinorEvent(
                    "BookStorage.Update() Copying file {0} to {1}",
                    sourcePathIncludingFileName,
                    documentPath
                );

                RobustFile.Copy(sourcePathIncludingFileName, documentPath, true);
                //if the source was locked, don't copy the lock over
                RobustFile.SetAttributes(documentPath, FileAttributes.Normal);
            }
            catch (Exception e)
            {
                if (
                    documentPath.Contains(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                    ) || documentPath.ToLowerInvariant().Contains("program")
                ) //english only
                {
                    Logger.WriteEvent(
                        "Could not update file {0} because it was in the program directory.",
                        documentPath
                    );
                    return;
                }

                ReportCantUpdateSupportFile(sourcePathIncludingFileName, documentPath);
            }
        }

        private static void ReportCantUpdateSupportFile(string factoryPath, string documentPath)
        {
            if (_alreadyNotifiedAboutOneFailedCopy)
                return; //don't keep bugging them
            _alreadyNotifiedAboutOneFailedCopy = true;
            // We probably can't help the user if they create an issue, but we can log a bit more information to help local tech support.
            // (Only the shortMessage, which is what typical users actually see, is currently localized.)
            var msg = MiscUtils.GetExtendedFileCopyErrorInformation(
                documentPath,
                $"Could not update one of the support files in this document ({documentPath} from {factoryPath})."
            );
            Logger.WriteEvent(msg);
            var shortMsg = LocalizationManager.GetString(
                "Errors.CannotUpdateFile",
                "There was a problem updating a support file"
            );
            var howToTroubleshoot = LocalizationManager.GetString(
                "Errors.CannotUpdateTroubleshoot",
                "How to troubleshoot file updating errors"
            );

            var longerMsgTemplate = LocalizationManager.GetString(
                "Errors.CannotUpdateFileLonger",
                "Bloom was not able to update a support file named \"{0}\". This is usually not a problem. If you continue to see these messages, see \"{1}\"."
            );
            var longerMsg = string.Format(
                longerMsgTemplate,
                Path.GetFileName(documentPath),
                $"<a href='https://docs.bloomlibrary.org/troubleshooting-file-access' target='_blank'>{howToTroubleshoot}</a>"
            );

            // Something like this was called for in the comment on BL-11863 that led to this,
            // but we finally decided not to.
            // var avProgs = MiscUtils.InstalledAntivirusProgramNames();
            //if (!string.IsNullOrEmpty(avProgs))
            //{
            //	var avTemplate = LocalizationManager.GetString("Errors.TryPausingAV",
            //		"Try pausing \"{0}\" or telling \"{0}\" that you trust Bloom.");
            //	shortMsg += Environment.NewLine + string.Format(avTemplate, avProgs);
            //}

            BloomErrorReport.NotifyUserUnobtrusively(shortMsg, longerMsg);
        }

        private void RemoveExistingFilesBySuffix(string suffix)
        {
            foreach (var file in Directory.GetFiles(FolderPath, "*" + suffix))
            {
                try
                {
                    RobustFile.Delete(file);
                }
                catch (Exception e)
                {
                    // not worth bothering the user about, but we can log it in case it needs investigation
                    Debug.Fail(e.Message);
                    Logger.WriteError("Could not remove " + file, e);
                }
            }
        }

        /// <summary>
        /// user folder (or a temp folder, typically one where we've copied a user book to update for publishing)
        /// as opposed to our program installation folder or some template
        /// </summary>
        private bool IsUserOrTempFolder
        {
            get
            {
                if (FolderPath.Contains(Path.GetTempPath()))
                    return true;
                if (String.IsNullOrEmpty(_collectionSettings.FolderPath))
                {
                    //this happens when we are just hydrating the book via a command-line command
                    return true;
                }
                else
                    return FolderPath.Contains(_collectionSettings.FolderPath);
            }
        }

        // NB: this knows nothing of book-specific css's... even "basic book.css"
        internal void EnsureHasLinksToStylesheets(HtmlDom dom)
        {
            //clear out any old ones
            dom.RemoveNormalStyleSheetsLinks();
            //Stylesheets will all get sorted at the end by EnsureStylesheetLinks
            dom.EnsureStylesheetLinksWithoutSorting(Path.GetFileName(PathToXMatterStylesheet));
            dom.EnsureStylesheetLinksWithoutSorting(CssFilesThatAreAlwaysWanted);
            var appearanceRelatedCssFiles = BookInfo.AppearanceSettings.AppearanceRelatedCssFiles(
                LinkToLocalCollectionStyles
            );

            dom.EnsureStylesheetLinks(appearanceRelatedCssFiles.ToArray());
        }

        public string HandleRetiredXMatterPacks(HtmlDom dom, string nameOfXMatterPack)
        {
            var currentXmatterName = XMatterHelper.MigrateXMatterName(nameOfXMatterPack);

            if (currentXmatterName != nameOfXMatterPack)
            {
                const string xmatterSuffix = "-XMatter.css";
                EnsureDoesNotHaveLinkToStyleSheet(dom, nameOfXMatterPack + xmatterSuffix);
                dom.EnsureStylesheetLinks(nameOfXMatterPack + xmatterSuffix);
                // Since HtmlDom.GetMetaValue() is always called with the collection's xmatter pack as default,
                // we can just remove this wrong meta element.
                dom.RemoveMetaElement("xmatter");
            }
            return currentXmatterName;
        }

        private void EnsureDoesNotHaveLinkToStyleSheet(HtmlDom dom, string path)
        {
            foreach (SafeXmlElement link in dom.SafeSelectNodes("//link[@rel='stylesheet']"))
            {
                var fileName = link.GetAttribute("href");
                if (fileName == path)
                    dom.RawDom.RemoveStyleSheetIfFound(path);
            }
        }

        public string[] CssFilesThatAreAlwaysWanted
        {
            get { return new[] { "origami.css", "branding.css", "defaultLangStyles.css" }; }
        }

        // note: order is not significant here. We apply our standard stylesheet sorter later.
        // Enhance: it would be cleaner if most of these were in a common list, and this method just knew
        // what extra ones we need for a preview. Note also that (at least) MakeCssLinksAppropriateForStoredFile(),
        // below, independently knows about previewMode.css; if the idea is that the stored book should look
        // like the Preview when opened in a browser, it would be better to use the same code to
        // put the DOM in that state.
        // (Possibly cleaner still to have way fewer stylesheets, and turn rules on with classes.)
        public string[] GetCssFilesToLinkForPreview()
        {
            return new[] { "previewMode.css" }
                .Concat(CssFilesThatAreAlwaysWanted)
                .Concat(
                    this.BookInfo.AppearanceSettings.AppearanceRelatedCssFiles(
                        LinkToLocalCollectionStyles
                    )
                )
                .ToArray();
        }

        // While in Bloom, we could have an edit style sheet or (someday) other modes. But when stored,
        // we want to make sure it's ready to be opened in a browser.
        private void MakeCssLinksAppropriateForStoredFile(HtmlDom dom)
        {
            EnsureHasLinksToStylesheets(dom);
            dom.RemoveModeStyleSheets(); // nb must be before we add previewMode, which it removes
            dom.EnsureStylesheetLinks("previewMode.css");
            dom.RemoveFileProtocolFromStyleSheetLinks();
        }

        /// <summary>
        /// Sanitize a book's title for use as a file (or folder) name.
        /// The title is normalized to Unicode Normalization Form C.
        /// If the title contains invalid characters, they are replaced with spaces.
        /// If a title is too long, it is truncated.
        /// If the title is empty, "Book" (or the localized equivalent) is returned.
        /// </summary>
        public static string SanitizeNameForFileSystem(string name)
        {
            try
            {
                // Trim a single high surrogate character from the end of the string.
                // Probably this could only happen if someone pasted in a title that
                // was already corrupt with a dangling high surrogate at the end.
                if (name.Length > 0 && char.IsHighSurrogate(name[name.Length - 1]))
                    name = name.Substring(0, name.Length - 1);

                // We want NFC to prevent Dropbox complaining about encoding conflicts.
                // May as well do that first as it may result in less truncation.
                name = name.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException e)
            {
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-12587/Problems-with-unicode-surrogate-pair-in-title

                Logger.WriteError(
                    $"SanitizeNameForFileSystem() while trying to normalize {name}, got",
                    e
                );
                name = "Book";
            }
            // Then replace invalid characters with spaces and trim off characters
            // that shouldn't start or finish a directory name.
            name = RemoveDangerousCharacters(name);

            // Then make sure it's not too long.
            name = MiscUtils.TruncateSafely(name, kMaxFilenameLength);

            // Remove trailing whitespace, periods
            name = Regex.Replace(name, "[\\s.]+$", "", RegexOptions.Compiled);

            if (String.IsNullOrWhiteSpace(name))
            {
                // The localized default book name could itself have dangerous characters.
                name = RemoveDangerousCharacters(BookStarter.UntitledBookName);
                if (name.Length == 0)
                    name = "Book"; // This should absolutely never be needed, but let's be paranoid.
            }
            return name;
        }

        private static string RemoveDangerousCharacters(string name)
        {
            var dangerousCharacters = new List<char>();
            dangerousCharacters.AddRange(PathUtilities.GetInvalidOSIndependentFileNameChars());
            // NBSP also causes problems.  See https://issues.bloomlibrary.org/youtrack/issue/BL-5212.
            dangerousCharacters.Add('\u00a0');
            //dangerousCharacters.Add('.'); Moved this to a trim because SHRP uses names like "SHRP 2.3" (term 2, week 3)
            // Add characters to the list that will bother bloom-player (and JSON and URLs in general)
            dangerousCharacters.AddRange("&'{},;()$@");
            foreach (char c in dangerousCharacters)
            {
                name = name.Replace(c, ' ');
            }
            // Remove leading whitespace and periods.
            name = Regex.Replace(name, "^[\\s.]*", "", RegexOptions.Compiled);
            // Remove trailing whitespace and periods.
            // Windows does not allow directory names ending in period.
            // If we give it a chance, it will make a directory without the dots,
            // but all our code that thinks the folder name has the dots will break (e.g., BL-3402, BL-9040)
            name = Regex.Replace(name, "[\\s.]*$", "", RegexOptions.Compiled);
            return name;
        }

        /// <summary>
        /// if necessary, append a number to make the folder path unique
        /// </summary>
        private static string GetUniqueFolderPath(string folderPath)
        {
            var parent = Directory.GetParent(folderPath).FullName;
            var name = GetUniqueFolderName(parent, Path.GetFileName(folderPath));
            return Path.Combine(parent, name);
        }

        /// <summary>
        /// if necessary, insert a number according to template to make the folder path unique
        /// </summary>
        /// <param name="parentFolderPath">The parent directory which the new unique folder path will go in</param>
        /// <param name="unnumberedName">An unnumbered name to use first if possible, e.g. "Foldername (Copy)"</param>
        /// <param name="numberedNameTemplate">Template for inserting unique numbers into subsequent names, e.g."Foldername (Copy-{0})" </param>
        internal static string GetUniqueFolderPath(
            string parentFolderPath,
            string unnumberedName,
            string numberedNameTemplate
        )
        {
            int i = 1;
            var newName = unnumberedName;
            while (Directory.Exists(Path.Combine(parentFolderPath, newName)))
            {
                ++i;
                string previousName = newName;
                newName = String.Format(numberedNameTemplate, i);
                if (String.Equals(previousName, newName))
                {
                    throw new ArgumentException(
                        "numberedNameTemplate does not specify a place to insert a number",
                        numberedNameTemplate
                    );
                }
            }
            return Path.Combine(parentFolderPath, newName);
        }

        /// <summary>
        /// if necessary, append a number to make the subfolder name unique within the given folder
        /// </summary>
        internal static string GetUniqueFolderName(string parentPath, string name)
        {
            // Don't be tempted to give this parentheses. That isn't compatible with
            // SanitizeNameForFileSystem which removes parentheses. See BL-11663.

            int i = 1; // First non-blank suffix should be " 2"
            string suffix = "";
            while (Directory.Exists(Path.Combine(parentPath, name + suffix)))
            {
                ++i;
                suffix = " " + i.ToString(CultureInfo.InvariantCulture);
            }
            return name + suffix;
        }

        /// <summary>
        /// if necessary, append a number to make the file name unique within the given folder
        /// </summary>
        internal static string GetUniqueFileName(string parentPath, string name, string ext)
        {
            int i = 0;
            string suffix = "";
            string result;
            do
            {
                result = Path.ChangeExtension(Path.Combine(parentPath, name + suffix), ext);
                ++i;
                suffix = i.ToString(CultureInfo.InvariantCulture);
            } while (RobustFile.Exists(result));
            return result;
        }

        public string GetBrokenBookRecommendationHtml()
        {
            string s = "";
            if (!_errorAlreadyContainsInstructions)
            {
                s = GenericBookProblemNotice;
            }
            return s + "<p>" + ErrorMessagesHtml + "</p>";
        }

        public static string GenericBookProblemNotice =>
            "<p>"
            + LocalizationManager.GetString(
                "Errors.BookProblem",
                "Bloom had a problem showing this book. This doesn't mean your work is lost, but it does mean that something is out of date, is missing, or has gone wrong."
            )
            + "</p>";

        //enhance: move to SIL.IO.RobustIO
        public static void CopyDirectory(
            string sourceDir,
            string targetDir,
            string[] skipFileExtensionsLowerCase = null
        )
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                if (
                    skipFileExtensionsLowerCase != null
                    && skipFileExtensionsLowerCase.Contains(
                        Path.GetExtension(file.ToLowerInvariant())
                    )
                )
                    continue;
                RobustFile.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
                CopyDirectory(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
        }

        /// <summary>
        /// Makes a copy of the book on disk and gives the new copy a unique guid
        /// </summary>
        /// <returns>a path to the directory containing the duplicate</returns>
        public string Duplicate()
        {
            // get the book name and copy number of the current directory
            var baseName = Path.GetFileName(FolderPath);

            // see if this already has a name like "foo Copy 3"
            // If it does, we will use that number plus 1 as the starting point for looking for a new unique folder name
            var regexToGetCopyNumber = new Regex(@"^(.+)(\s-\sCopy)(\s[0-9]+)?$");
            var match = regexToGetCopyNumber.Match(baseName);
            var copyNum = 1;

            if (match.Success)
            {
                baseName = match.Groups[1].Value;
                if (match.Groups[3].Success)
                    copyNum = 1 + Int32.Parse(match.Groups[3].Value.Trim());
            }

            // directory for the new book
            var collectionDir = Path.GetDirectoryName(FolderPath);
            var newBookName = GetAvailableDirectory(collectionDir, baseName, copyNum);
            var newBookDir = Path.Combine(collectionDir, newBookName);
            Directory.CreateDirectory(newBookDir);

            // copy files
            // Note, .bloombookorder files are no longer created; they are obsolete.
            CopyDirectory(
                FolderPath,
                newBookDir,
                new[] { ".bak", ".bloombookorder", ".pdf", ".map" }
            );
            var metaPath = Path.Combine(newBookDir, "meta.json");

            ChangeInstanceId(metaPath);

            // rename the book htm file
            var oldName = Path.Combine(newBookDir, Path.GetFileName(PathToExistingHtml));
            var newName = Path.Combine(newBookDir, newBookName + ".htm");
            RobustFile.Move(oldName, newName);
            return newBookDir;
        }

        private void ChangeInstanceId(string metaDataPath)
        {
            // Update the InstanceId. This was not done prior to Bloom 4.2.104
            // If the meta.json file is missing, ok that's weird but that means we
            // don't have a duplicate bookInstanceId to worry about.
            if (RobustFile.Exists(metaDataPath))
            {
                var meta = DynamicJson.Parse(RobustFile.ReadAllText(metaDataPath));
                meta.bookInstanceId = Guid.NewGuid().ToString();
                RobustFile.WriteAllText(metaDataPath, meta.ToString());
            }
        }

        /// <summary>
        /// Get an available directory name for a new copy of a book
        /// </summary>
        /// <param name="collectionDir"></param>
        /// <param name="baseName"></param>
        /// <param name="copyNum"></param>
        /// <returns></returns>
        private static string GetAvailableDirectory(
            string collectionDir,
            string baseName,
            int copyNum
        )
        {
            string newName;
            if (copyNum == 1)
                newName = baseName + " - Copy";
            else
                newName = baseName + " - Copy " + copyNum;

            while (Directory.Exists(Path.Combine(collectionDir, newName)))
            {
                copyNum++;
                newName = baseName + " - Copy " + copyNum;
            }

            return newName;
        }

        public void EnsureOriginalTitle()
        {
            var dataDiv = Dom.RawDom
                .SafeSelectNodes("//div[@id='bloomDataDiv']")
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            if (dataDiv == null)
                return;
            var originalTitle = dataDiv.SafeSelectNodes(".//div[@data-book='originalTitle']");
            if (originalTitle.Length > 0)
                return;
            var titles = Dom.RawDom
                .SafeSelectNodes("//div[@data-book='bookTitle']")
                .Cast<SafeXmlElement>()
                .ToList();
            if (titles.Count == 0)
                return;
            // If we want to use some other, non-English title, we could consider such an option here,
            // e.g., any other language...except probably not the book's L1, especially when filling it in
            // for an older book.
            var useTitle = titles.FirstOrDefault(t => t.GetAttribute("lang") == "en");
            if (useTitle == null)
                return;
            var content = useTitle.InnerText;
            if (string.IsNullOrEmpty(content))
                return;
            var newElt = dataDiv.OwnerDocument.CreateElement("div");
            newElt.InnerText = content;
            newElt.SetAttribute("data-book", "originalTitle");
            newElt.SetAttribute("lang", "*");
            dataDiv.AppendChild(newElt);
            if (BookInfo != null) // should only be null in unit tests
            {
                BookInfo.OriginalTitle = content;
            }
        }

        internal static string GetActivityFolderPath(string bookFolderPath)
        {
            return Path.Combine(bookFolderPath, "activities");
        }

        /// <summary>
        /// Prior to Bloom 6.0, we had a single metadata element called "maintenanceLevel" that was used to
        /// keep track of whether a book had been updated to the latest version of Bloom in certain somewhat
        /// time-consuming ways. (BringBookUpToDate does other migrations, too, but we just run them every time.)
        /// We did not distinguish between changes that only affect the DOM and ones we could only make in
        /// folders where we can write, nor between ones that must be done before editing, ones that must be
        /// done before we do anything with the book, or ones that are optional.
        /// In 6.0 we introduced a new metadata element called "mediaMaintenanceLevel" that is used to keep
        /// track of migrations that affect files other than the main HTML one. This function,
        /// which must be called before any of the ones that might change the old maintenanceLevel metadata,
        /// initializes the new mediaMaintenanceLevel to the appropriate value based on the old maintenanceLevel
        /// if it does not already exist.
        /// </summary>
        public void MigrateMaintenanceLevels()
        {
            var mediaLevelString = Dom.GetMetaValue("mediaMaintenanceLevel", "bad");
            if (int.TryParse(mediaLevelString, out int mediaLevel))
                return; // already have mediaMaintenanceLevel

            // If mediaMaintenanceLevel is missing, it should be set to zero if the old maintenanceLevel
            // indicates we have not done MigrateToMediaLevel1ShrinkLargeImages, and to 1 if we have.
            Dom.UpdateMetaElement("mediaMaintenanceLevel", GetMaintenanceLevel() >= 1 ? "1" : "0");
            ;
        }

        /// <summary>
        /// In very old books (before 4.9) we did not shrink even very large images before adding them to
        /// books. When we encounter such a book, we go ahead and shrink them. This is probably less
        /// necessary than in Gecko days, when super-large images were prone to make Bloom run out of
        /// memory. However, it is still helpful for performance and reducing published file sizes.
        /// Does nothing if mediaMaintenanceLevel indicates it has already been done.
        /// </summary>
        public void MigrateToMediaLevel1ShrinkLargeImages()
        {
            var levelString = Dom.GetMetaValue("mediaMaintenanceLevel", "0");
            if (!int.TryParse(levelString, out int level))
                level = 0;
            if (level >= 1)
                return;
            if (ImageUtils.NeedToShrinkImages(FolderPath))
            {
                // If the book contains overlarge images, we want to fix those before editing because this can lead
                // to thumbnails not being created properly and other bad behavior.  This is a one-time fix that can
                // permanently change the images in the original book folder.  If any images must be shrunk, then a
                // progress dialog pops up because that can be a very slow process.  If nothing needs to be done,
                // nothing will appear on the screen, and it usually takes a small fraction of a second to determine
                // this.

                // Bloom 4.9 and later limit images used by Bloom books to be no larger than 3500x2550 in
                // order to avoid out of memory errors that can happen with really large images.
                // Bloom 5.6 changed this to 3840x2800 to accomodate Ultra HD (aka "4K"). Some older
                // books have images larger than this that can cause these out of memory problems.  This
                // method is used to fix these overlarge images before the user starts to edit or publish
                // the book.  The method also ensures that the images are all opaque since some old versions
                // of Bloom made all images transparent, which turned out to be a bad idea.
                // This update can be very slow, so encourage the user that something is happening.
                // NO images should have transparency removed.  See https://issues.bloomlibrary.org/youtrack/issue/BL-8846.

                if (Program.RunningUnitTests)
                {
                    // TeamCity enforces not showing modal dialogs during unit tests on Windows 10.
                    ImageUtils.FixSizeAndTransparencyOfImagesInFolder(
                        FolderPath,
                        new List<string>(),
                        new NullProgress()
                    );
                }
                else
                {
                    using (var dlg = new ProgressDialogBackground())
                    {
                        dlg.Text = "Updating Image Files";
                        dlg.ShowAndDoWork(
                            (progress, args) =>
                                ImageUtils.FixSizeAndTransparencyOfImagesInFolder(
                                    FolderPath,
                                    new List<string>(),
                                    progress
                                )
                        );
                    }
                }
            }

            Dom.UpdateMetaElement("mediaMaintenanceLevel", "1");
        }

        private int GetMaintenanceLevel()
        {
            var levelString = Dom.GetMetaValue("maintenanceLevel", "0");
            if (!int.TryParse(levelString, out int level))
                level = 0;
            return level;
        }

        /// <summary>
        /// Bloom 4.9 and later (a bit later than the above 4.9 and therefore a separate maintenance
        /// level) will only put comical-generated svgs in Bloom imageContainers if they are
        /// non-transparent. Since our test for whether a book is Comical for Publishing restrictions
        /// will now be a simple scan for these svgs, we here remove legacy svgs whose bubble style
        /// was "none", implying transparency.
        /// In Bloom 5.0, we renamed the Comic Tool -> Overlay Tool, but "comical" refers to the comical.js
        /// npm project which creates the svgs. It and the "Comic" feature have not been renamed for backward
        /// compatibility.
        /// This does nothing if maintenanceLevel indicates it has already been done.
        /// </summary>
        public void MigrateToLevel2RemoveTransparentComicalSvgs()
        {
            if (GetMaintenanceLevel() >= 2)
                return;

            var comicalSvgs = Dom.SafeSelectNodes(ComicalXpath).Cast<SafeXmlElement>();
            var elementsToSave = new HashSet<SafeXmlElement>();
            foreach (var svgElement in comicalSvgs)
            {
                var container = svgElement.ParentNode; // bloom-imageContainer div (not gonna be null)
                var textOverPictureDivs = container.SafeSelectNodes(
                    "div[contains(@class, 'bloom-textOverPicture')]"
                );
                if (textOverPictureDivs == null) // unlikely, but maybe possible
                    continue;

                foreach (var textOverPictureDiv in textOverPictureDivs)
                {
                    var bubbleData = textOverPictureDiv.GetAttribute("data-bubble");
                    if (string.IsNullOrEmpty(bubbleData))
                        continue;
                    var jsonObject = HtmlDom.GetJsonObjectFromDataBubble(bubbleData);
                    if (jsonObject == null)
                        continue; // only happens if it fails to parse the "json"
                    var style = HtmlDom.GetStyleFromDataBubbleJsonObj(jsonObject);
                    if (style == "none")
                        continue;
                    elementsToSave.Add(svgElement);
                    break;
                }
            }

            // Now delete the SVGs that only have bubbles of style 'none'.
            var dirty = false;
            foreach (var svgElement in comicalSvgs.ToArray())
            {
                if (!elementsToSave.Contains(svgElement))
                {
                    svgElement.ParentNode.RemoveChild(svgElement);
                    dirty = true;
                }
            }

            if (dirty)
            {
                try
                {
                    Save();
                }
                catch (UnauthorizedAccessException e)
                {
                    ShowAccessDeniedErrorReport(e);
                }
            }
            Dom.UpdateMetaElement("maintenanceLevel", "2");
        }

        public void MigrateToLevel3PutImgFirst()
        {
            if (GetMaintenanceLevel() >= 3)
                return;

            // Make sure that in every image container, the first element is the img.
            // This is important because, since 5.4, we don't use a z-index to put overlays above the base image
            // (so the overlay image container does not become a stacking context, so we can use
            // z-index on its children to put them above the comicaljs canvas),
            // which means we depend on the img being first to make sure the overlays are on top of it.
            // (I'm not sure we ever created situations where the img was not first, but now it's vital,
            // so I added this maintenance to make sure of it.)
            var imageContainers = Dom.SafeSelectNodes(
                    "//*[contains(@class, 'bloom-imageContainer')]"
                )
                .Cast<SafeXmlElement>();
            foreach (var ic in imageContainers)
            {
                var firstImage = ic.ChildNodes.FirstOrDefault(
                    x => x is SafeXmlElement && ((SafeXmlElement)x).Name == "img"
                );
                if (firstImage == null)
                    continue;
                var firstElement = ic.ChildNodes.FirstOrDefault(x => x is SafeXmlElement);
                if (firstElement != firstImage)
                {
                    ic.InsertBefore(firstImage, firstElement);
                }
            }

            // We only want to update the maintenance level if we finished the job.
            Dom.UpdateMetaElement("maintenanceLevel", "3");
        }

        public CollectionSettings CollectionSettings => _collectionSettings;

        /// <summary>
        /// Move the book in the specified folder to a name that is safe (especially for DropBox)
        /// </summary>
        /// <returns>path to book folder</returns>
        public static string MoveBookToSafeName(string oldBookFolder)
        {
            var fileName = Path.GetFileName(oldBookFolder);
            var goodName = SanitizeNameForFileSystem(fileName);
            if (goodName == fileName)
                return oldBookFolder; // no need to change.
            var goodPath = Path.Combine(Path.GetDirectoryName(oldBookFolder), goodName);
            return MoveBookToAvailableName(oldBookFolder, goodPath);
        }

        /// <summary>
        /// Move the book at the specified location to a similar location that is not in use.
        /// </summary>
        /// <returns>The path to the new book folder</returns>
        public static string MoveBookToAvailableName(
            string oldBookFolder,
            string desiredPath = null
        )
        {
            if (desiredPath == null)
                desiredPath = oldBookFolder;
            var newPathForExtraBook = BookStorage.GetUniqueFolderPath(desiredPath);
            SIL.IO.RobustIO.MoveDirectory(oldBookFolder, newPathForExtraBook);
            var extraBookPath = Path.Combine(
                newPathForExtraBook,
                Path.ChangeExtension(Path.GetFileName(oldBookFolder), "htm")
            );
            // This will usually succeed, since it is standard to name the book the same as the folder.
            // But if it doesn't, we can't move it, so it seems worth a check.
            // (And if we change our minds about keeping them in sync, this will be one less place to fix.)
            if (RobustFile.Exists(extraBookPath))
                RobustFile.Move(
                    extraBookPath,
                    Path.Combine(
                        newPathForExtraBook,
                        Path.ChangeExtension(Path.GetFileName(newPathForExtraBook), "htm")
                    )
                );
            return newPathForExtraBook;
        }

        /// <summary>
        /// Save a copy of the specified book in the a folder %temp%/bloom pre-import backups.
        /// Generate a unique name for the backup as necessary, and return its full path.
        /// </summary>
        public static string SaveCopyBeforeImportOverwrite(string bookFolder)
        {
            string origFileName = Path.GetFileName(bookFolder);
            var parentFolder = Path.Combine(Path.GetTempPath(), "bloom pre-import backups");
            var destPath = GetUniqueFileName(parentFolder, origFileName, ".bloomSource");
            Directory.CreateDirectory(parentFolder);
            var zipFile = new BloomZipFile(destPath);
            zipFile.AddDirectory(bookFolder, bookFolder.Length + 1, null, null);
            zipFile.Save();

            return destPath;
        }

        // These are files that should never be searched for outside of the book folder, should not be cached, etc.
        // One might think these could be the default, and we could instead specify other types, but
        // that isn't how this code base has evolved. So I'm just trying to gather in one place this list
        // that had become scattered around (and inconsistent).
        // JohnT: I don't like JohnH's name for this, but we couldn't agree on a better one. My read on the thing they
        // have in common is that they store something that is specific to the particular book (though customCollectionStyles.css
        // is shared across the collection, and branding with other books in that branding). So we do NOT want
        // to update them from the latest version of Bloom (except branding.css is updated to match the currently
        // selected branding from data that is part of Bloom).
        public readonly static string[] CssFilesThatAreDynamicallyUpdated =
        {
            "branding.css",
            "defaultLangStyles.css",
            "customCollectionStyles.css",
            "appearance.css",
            "customBookStyles.css",
            "customBookStyles2.css"
        };

        /// <summary>
        /// These are CSS files that are part of the Bloom installation, and are not expected to change at runtime.
        /// They should be part of every Bloom book file/DOM.
        /// Things like editMode.css, previewMode.css that are only in DOMs used for a particular purpose should NOT be here.
        /// Things in CssFilesThatAreDynamicallyUpdated, where information could be lost if we
        /// copy the installed version over the current version, should NOT be here.
        /// </summary>
        /// <returns></returns>
        public string[] getMinimalCssFilesFromInstallThatDoNotChangeAtRuntime()
        {
            return new string[] { this.BookInfo.AppearanceSettings.BasePageCssName, "origami.css" };
        }

        /// <summary>
        /// Should we look for customCollectionStyles.css in the book folder itself, or in the parent folder?
        /// Normally we look in the parent folder, but if we are making a bloomPub or similar copy,
        /// we copy it into the book folder itself, and that's where we should look.
        /// </summary>
        public bool LinkToLocalCollectionStyles { get; set; }

        public class CannotHarvestException : ApplicationException
        {
            public CannotHarvestException(string message)
                : base(message) { }
        }

        /// <summary>
        /// Migrate to the new appearance system if we haven't already tried to do so.
        /// </summary>
        public void MigrateToLevel4UseAppearanceSystem()
        {
            Guard.Against(
                !BookInfo.IsSaveable,
                "We should not even think about migrating a book that is not Saveable"
            );
            if (GetMaintenanceLevel() >= 4)
                return;

            if (
                Program.RunningHarvesterMode
                && !LegacyThemeCanBeUsed
                && !HarvesterMayConvertToDefaultTheme
            )
            {
                throw new CannotHarvestException(
                    "This book cannot currently be harvested, since it is not migrated to the appearance system and cannot use legacy theme."
                );
            }

            var cssFiles = GetCssFilesToCheckForAppearanceCompatibility(true);
            var substituteCssPath = BookInfo.AppearanceSettings.GetThemeAndSubstituteCss(cssFiles);
            if (substituteCssPath != null)
            {
                var destPath = Path.Combine(FolderPath, "customBookStyles2.css");
                // if we're doing an automatic substitution, we don't expect there to be a customBookStyles2.css already,
                // since substitution is used when the source book is NOT in the new appearance format, while
                // customBookStyles2.css is only supported in that format.
                RobustFile.Copy(substituteCssPath, destPath, false);
            }
            else
            {
                // if there wasn't a substitute, we may have chosen legacy theme.
                // That might be disabled by xmatter, but we'll handle that later in
                // EnsureUpToDate.
            }

            // This would happen as a side effect of saving the book at the end of updating it, but
            // we need it to happen before we re-initialize the settings and UpdateSupportFiles so
            // that the settings knows it is consistent with the state of things on disk, which allows
            // the right links to be made and the right files copied to the book folder.
            BookInfo.AppearanceSettings.WriteToFolder(FolderPath);

            Dom.UpdateMetaElement("maintenanceLevel", "4");
        }

        /// <summary>
        /// Files that might contain rules that conflict with the new appearance model (currently, that is if they affect
        /// the size and position of the marginBox).
        /// In the resulting list, the first item is the file name, and the second is content.
        /// Note that this routine must find and use the files that UpdateSupportingFiles will copy into the book.
        /// It has to be called before UpdateSupportingFiles, because it is used to initialize the
        /// appearanceSettings that determines which files UpdateSupportingFiles will copy.
        /// </summary>
        /// <returns></returns>
        public Tuple<string, string>[] GetCssFilesToCheckForAppearanceCompatibility(
            bool justOldCustomFiles = false
        )
        {
            var result = new List<Tuple<string, string>>();

            // Must come before customCollectionStyles.css (see AppearanceSettings.Initialize).
            result.Add(
                Tuple.Create(
                    GetSupportingFile("customBookStyles.css"),
                    GetSupportingFileString("customBookStyles.css")
                )
            );
            // this is sometimes copied into the book folder, but that's not reliable except in BloomPubs.
            // A flag in AppearanceSettings allows it to tell us which of them we should use for this book.
            var customCollectionStylesPath = FolderPath.CombineForPath(
                RelativePathToCollectionStyles(LinkToLocalCollectionStyles)
            );
            var customCollectionCss = RobustFile.Exists(customCollectionStylesPath)
                ? RobustFile.ReadAllText(customCollectionStylesPath)
                : null;
            result.Add(Tuple.Create(customCollectionStylesPath, customCollectionCss));

            if (!justOldCustomFiles)
            {
                // We want to check the branding.css file from the Bloom installation, not the one in the book folder.
                // The one in the book folder may still be from a previous version of Bloom.
                // After the compatibility check, we will copy the one from the Bloom installation into the book folder.
                // (xmatter below follows a similar pattern since GetSupportFile always returns the installed xmatter.)
                result.Add(
                    Tuple.Create(
                        GetSupportingFile("branding.css", useInstalledBranding: true),
                        GetSupportingFileString("branding.css", useInstalledBranding: true)
                    )
                );
                result.Add(
                    Tuple.Create(
                        GetSupportingFile("customBookStyles2.css"),
                        GetSupportingFileString("customBookStyles2.css")
                    )
                );
                result.Add(
                    Tuple.Create(
                        GetSupportingFile("appearance.css"),
                        GetSupportingFileString("appearance.css")
                    )
                );

                var xmatterFileName = Path.GetFileName(PathToXMatterStylesheet);
                result.Add(
                    Tuple.Create(
                        GetSupportingFile(xmatterFileName),
                        GetSupportingFileString(xmatterFileName)
                    )
                );
            }
            return result.ToArray();
        }

        private string GetSupportingFileString(string file, bool useInstalledBranding = false)
        {
            // Do the search for the file that UpdateSupportingFiles will copy into the book
            // folder, since this is called BEFORE we do that.
            var path = GetSupportingFile(file, useInstalledBranding);
            if (RobustFile.Exists(path))
                return RobustFile.ReadAllText(path);
            return null;
        }

        public static readonly string[] CssFilesThatAreObsolete =
        {
            "languageDisplay.css",
            "langVisibility.css",
            "editOriginalMode.css",
            "editTranslationMode.css"
        };

        // These go before "unknown" stylesheets in the sort order
        public static readonly string[] OrderedPrefixesOfCssFilesToSortBeforeUnknownStylesheets =
        {
            "basePage", // we leave off ".css" so that this can match version ones, like "basePage-legacy-5-6.css"
            "baseEPUB.css",
            "editMode.css",
            "previewMode.css",
            "origami.css",
        };

        // These go after "unknown" stylesheets in the sort order
        public static readonly string[] OrderedPrefixesOfCssFilesToSortAfterUnknownStylesheets =
        {
            "branding.css",
            "defaultLangStyles.css",
            "customCollectionStyles.css",
            "../customCollectionStyles.css",
            "appearance.css",
            // We don't usually have both of these, and I don't have a clear idea why one should come before
            // the other. But the order should be consistent, and if both are there, typically customBookStyles2.css
            // came from our system, while the other was added by the user. So allow the user one to win.
            "customBookStyles2.css",
            "customBookStyles.css"
        };

        // RemoveNormalStyleSheetsLinks uses this list to get rid of old css files before we add new ones.
        // This list must include all the CSS files that AppearanceRelatedCssFiles might ever return so we can
        // delete the ones we don't want
        public static readonly string[] AutomaticallyAddedCssFilePrefixes =
        // These are split up for the sake of the StyleSheetLinkSorter
        OrderedPrefixesOfCssFilesToSortBeforeUnknownStylesheets
            .Concat(OrderedPrefixesOfCssFilesToSortAfterUnknownStylesheets)
            .ToArray();

        /// <summary>
        /// Relative to the book folder, where should we find the customCollectionStyles.css file?
        /// Normally we look in the parent folder, but if we are making a bloomPub or similar copy,
        /// we copy it into the book folder itself, and that's where we should look.
        /// </summary>
        public static string RelativePathToCollectionStyles(bool useLocalCollectionStyles)
        {
            return (useLocalCollectionStyles ? "" : "../") + "customCollectionStyles.css";
        }

        /// <summary>
        /// Check for lack of content in the marginBox div on the page that would prevent saving the state.
        /// </summary>
        /// <returns>true if the marginBox content has disappeared, false otherwise</returns>
        /// <remarks>
        /// See BL-13078, BL-13120, BL-13123, and BL-13143 for reported instances of this occurring.
        /// </remarks>
        public static bool CheckForEmptyMarginBoxOnPage(SafeXmlElement pageElement)
        {
            if (Program.RunningUnitTests)
                return false; // unit tests might have incomplete data, so we don't want to report this as an error.

            // If the content of the marginBox has disappeared, we don't want to save that state.

            if (HasMessedUpMarginBox(pageElement))
            {
                ReportEmptyMarginBox(pageElement);
                return true;
            }

            return false;
        }

        private static void ReportEmptyMarginBox(SafeXmlElement pageDocument)
        {
            Debug.Fail("Margin box is messed up");

            Exception exception = null;
            try
            {
                // Need to throw to get a stack trace added to the exception.  (But the added stack points only to this method.)
                throw new ApplicationException(
                    $"Empty marginBox found on page (BL-13120, BL-13123):{Environment.NewLine}{pageDocument.OuterXml}"
                );
            }
            catch (Exception e)
            {
                exception = e;
            }
            Logger.WriteError(exception);
            Logger.WriteEvent(
                "Stack trace for missing marginBox content:{0}{1}",
                Environment.NewLine,
                new StackTrace(true)
            );
            // Write minor events to a second log to help diagnose the problem.  Nothing seems to enable logging
            // the minor events to the main log, so we have to do it manually to a separate file.
            var logpath = Path.Combine(Path.GetDirectoryName(Logger.LogPath), "MinorEventsLog.txt");
            RobustFile.WriteAllText(
                logpath,
                $"**** Detailed events leading up to missing marginBox content: ****{Environment.NewLine}"
            );
            RobustFile.AppendAllText(logpath, Logger.MinorEventsLog);
            RobustFile.AppendAllText(logpath, Environment.NewLine);
            exception.Data["ExtraFilePath"] = logpath;
            var msg = LocalizationManager.GetDynamicString(
                "BloomLowPriority",
                "ProblemReport.PageStructureProblem",
                "Bloom made a mistake on this page. Please report this to us, then check that nothing was lost."
            );
            ErrorReport.NotifyUserOfProblem(exception, msg);

            // Enhance: try to get stack dump from all threads?
            // See https://stackoverflow.com/questions/2057781/is-there-a-way-to-get-the-stacktraces-for-all-threads-in-c-like-java-lang-thre.
        }

        public const string kRepairedPageMessage =
            "We apologize, but it appears that a bug in an older version of Bloom removed the contents of this page. Please delete it. If you have a backup of this book, you may be able to copy the page from the good copy back into this book.";

        public void RepairEmptyPages()
        {
            var pages = Dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            var problems = pages.Where(page => HasMessedUpMarginBox(page)).ToList();
            if (problems.Count == 0)
                return;
            // Replace the messed up pages with modified clones of our "Just Text" page that contain the message above.
            var pathToBasicBookFolder = BloomFileLocator.GetFactoryBookTemplateDirectory(
                "Basic Book"
            );
            var pathToBasicBook = Path.Combine(pathToBasicBookFolder, "Basic Book.html");
            var dom = XmlHtmlConverter.GetXmlDomFromHtmlFile(pathToBasicBook);
            var justTextPage = dom.SelectSingleNode("//div[@id='" + Book.JustTextGuid + "']");
            // The standard Just Text page has a bloom-editable with language 'z' that is meant to be a template
            // for creating other language blocks. We'll just use it and modify the language.
            var editable = justTextPage.SelectSingleNode(".//div[@lang='z']") as SafeXmlElement;
            // I'm deliberately not localizing this because it's a very rare message that we don't want our
            // translators to waste time on. Moreover, it's purpose is just to let the user know something has
            // gone wrong, which will be fairly evident even with an incomprehensible message in it.
            editable.InnerText = kRepairedPageMessage;
            // It does however want to be visible, so it needs to be marked as the right language
            // (even though it probably isn't).
            // This is not perfect...the user MIGHT have turned off the collection L1.
            // In that case the message will show up in the source bubble. Not ideal, but
            // it's a corner case of hack to alleviate the results of a rare bug that we
            // think we've prevented from ever again doing this much damage. I think it's good enough.
            editable.SetAttribute("lang", _collectionSettings.Language1Tag);
            foreach (var page in problems)
            {
                page.ParentNode.ReplaceChild(
                    page.OwnerDocument.ImportNode(justTextPage, true),
                    page
                );
            }
        }

        // Tries to detect a state that some bug occasionally puts a page into, where it is more-or-less empty.
        // Another characteristic state produced by the bug is where the page labels that should be outside
        // the marginBox are inside it. BL-13120.
        static bool HasMessedUpMarginBox(SafeXmlElement page)
        {
            var marginBox = GetMarginBox(page);
            if (marginBox == null)
                return true; // marginBox should not be missing
            var internalNodes = marginBox.ChildNodes.Where(x => x is SafeXmlElement).ToList();
            if (internalNodes.Count == 0)
            {
                return true; // marginBox should not be empty
            }
            foreach (var elt in internalNodes)
            {
                var classes = elt.GetAttribute("class");
                if (string.IsNullOrEmpty(classes))
                    continue;
                // If the marginBox has a div.pageLabel, the real content has disappeared and we don't want to save that state.
                if (classes.Contains("pageLabel"))
                {
                    return true;
                }
            }

            return false;
        }

        // I think this is more efficient than an xpath, especially since marginBox is usually the last top-level child.
        static SafeXmlElement GetMarginBox(SafeXmlElement parent)
        {
            foreach (
                SafeXmlElement child in parent.ChildNodes.Where(x => x is SafeXmlElement).Reverse()
            )
            {
                if (child.GetAttribute("class").Contains("marginBox"))
                    return child;
                var mb = GetMarginBox(child);
                if (mb != null)
                    return mb;
            }

            return null;
        }
    }
}
