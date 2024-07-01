using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Edit;
using Bloom.Utils;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Text;
using SIL.Windows.Forms.ClearShare;

namespace Bloom.Book
{
    /// <summary>
    /// A BookInfo has everything needed to display a title and thumbnail, and (eventually) do common searching filtering operations, without accessing the actual contents of the book.
    /// A related responsibility is to wrap the meta.json file which stores this search/display data in a form suitable for uploading to our web server.
    /// </summary>
    public class BookInfo
    {
        private const string kTopicPrefix = "topic:";
        private const string kBookshelfPrefix = "bookshelf:";

        // The BloomBookOrder files are no longer used. But we still use this in a couple places to delete them.
        public const string BookOrderExtension = ".BloomBookOrder";

        private ISaveContext _saveContext;

        private BookMetaData _metadata;

        public BookMetaData MetaData
        {
            get { return _metadata ?? (_metadata = new BookMetaData()); }
        }

        //for use by ErrorBookInfo
        protected BookInfo()
        {
            PublishSettings = new PublishSettings();
            AppearanceSettings = new AppearanceSettings();
        }

        internal BookInfo(string folderPath, bool isInEditableCollection)
            // By default (especially for many test cases), BookInfo's should assume savability
            // unless not editable
            : this(
                folderPath,
                isInEditableCollection,
                isInEditableCollection
                    ? new AlwaysEditSaveContext()
                    : new NoEditSaveContext() as ISaveContext
            )
        {
            // For real code, please explicitly provide a correct saveContext unless isInEditableCollection is false
            if (isInEditableCollection)
                Guard.Against(
                    !Program.RunningUnitTests,
                    "Only use this ctor for tests and non-editable collections"
                );
        }

        public BookInfo(string folderPath, bool isInEditableCollection, ISaveContext saveContext)
            : this()
        {
            Guard.AgainstNull(saveContext, "Please supply an actual saveContext");

            _saveContext =
                saveContext
                ?? (
                    isInEditableCollection
                        ? new AlwaysEditSaveContext()
                        : new NoEditSaveContext() as ISaveContext
                );

            FolderPath = folderPath;
            //NB: This was coded in an unfortunate way such that touching almost any property causes a new metadata to be quietly created.
            //So It's vital that we not touch properties that could create a blank metadata, before attempting to load the existing one.

            UpdateFromDisk();

            IsInEditableCollection = isInEditableCollection;

            FixDefaultsIfAppropriate();
        }

        public void UpdateFromDisk()
        {
            _metadata = BookMetaData.FromFolder(FolderPath);
            if (_metadata == null)
            {
                // Look for old tags files not yet migrated
                var oldTagsPath = Path.Combine(FolderPath, "tags.txt");
                if (RobustFile.Exists(oldTagsPath))
                {
                    Book.ConvertTagsToMetaData(oldTagsPath, this);
                }
                // otherwise leave it null, first attempt to use will create a default one
            }

            PublishSettings = PublishSettings.FromFolder(FolderPath);
            AppearanceSettings.UpdateFromFolder(FolderPath);
        }

        public enum HowToPublishImageDescriptions
        {
            None,
            OnPage //Removed Links in Bloom 4.6 on June 28, 2019.
        }

        public virtual string Id
        {
            get { return MetaData.Id; }
            set { MetaData.Id = value; }
        }

        public string FolderPath { get; set; }

        public bool AllowUploading
        {
            get { return MetaData.AllowUploadingToBloomLibrary; }
            set { MetaData.AllowUploadingToBloomLibrary = value; }
        }

        // The BloomBookOrder files are no longer used. But this method is
        // still used in a couple places to delete them.
        public static string BookOrderPath(string bookFolder)
        {
            return Path.Combine(bookFolder, Path.GetFileName(bookFolder) + BookOrderExtension);
        }

        public void MovePublisherToOriginalPublisher()
        {
            if (string.IsNullOrEmpty(MetaData.OriginalPublisher))
            {
                MetaData.OriginalPublisher = string.IsNullOrEmpty(MetaData.Publisher)
                    ? string.Empty
                    : MetaData.Publisher;
            }
            MetaData.Publisher = string.Empty;
        }

        //there was a beta version that would introduce the .json files with the incorrect defaults
        //we don't have a good way of differentiating when these defaults were set automatically
        //vs. when someone actually set them to false. So this method is only used if a certain
        //environment variable is set, so that our librarian (who ran into this) can fix her
        //affected collections.
        public void FixDefaultsIfAppropriate()
        {
            if (Environment.GetEnvironmentVariable("FixBloomMetaInfo") != "true")
                return;
            MetaData.AllowUploadingToBloomLibrary = true;
            MetaData.BookletMakingIsAppropriate = true;
        }

        public bool BookletMakingIsAppropriate
        {
            get { return MetaData.BookletMakingIsAppropriate; }
        }

        public bool IsSuitableForMakingShells
        {
            get { return MetaData.IsSuitableForMakingShells; }
            set { MetaData.IsSuitableForMakingShells = value; }
        }

        /// <summary>
        /// True if the user explicitly set a name (name is not automatically derived
        /// from title).
        /// </summary>
        public bool FileNameLocked
        {
            get { return MetaData.NameLocked; }
            set { MetaData.NameLocked = value; }
        }

        public bool IsSuitableForMakingTemplates
        {
            get { return MetaData.IsSuitableForMakingTemplates; }
            set { MetaData.IsSuitableForMakingTemplates = value; }
        }

        public bool IsSuitableForVernacularLibrary
        {
            get { return MetaData.IsSuitableForVernacularLibrary; }
            set { MetaData.IsSuitableForVernacularLibrary = value; }
        }

        //SeeAlso: commented IsExperimental on Book
        public bool IsExperimental
        {
            get { return MetaData.IsExperimental; }
            set { MetaData.IsExperimental = value; }
        }

        /// <summary>
        /// A "Folio" document is one that acts as a wrapper for a number of other books
        /// </summary>
        public bool IsFolio
        {
            get { return MetaData.IsFolio; }
            set { MetaData.IsFolio = value; }
        }

        public bool IsRtl
        {
            get { return MetaData.IsRtl; }
            set { MetaData.IsRtl = value; }
        }

        // Todo: multilingual
        public string Title
        {
            get { return MetaData.Title; }
            set
            {
                var titleStr = Book.RemoveHtmlMarkup(
                    value,
                    Book.LineBreakSpanConversionMode.ToNewline
                );
                MetaData.Title = titleStr;
            }
        }

        public string OriginalTitle
        {
            get { return MetaData.OriginalTitle; }
            set { MetaData.OriginalTitle = value; }
        }

        /// <summary>
        /// A possibly-temporary expedient to get multilingual title data into the json, and thus into parse.com
        /// This stores a Json string representing lang:title pairs, e.g.,
        /// {"en":"my nice title","de":"Mein schönen Titel","es":"мy buen título"}.
        /// Also used to quickly get a title in the most helpful language available to identify
        /// the book in lists, dialogs, and so forth.
        /// </summary>
        public virtual string AllTitles
        {
            get { return MetaData.AllTitles; }
            set { MetaData.AllTitles = value; }
        }

        public string Isbn
        {
            get { return MetaData.Isbn; }
            set { MetaData.Isbn = value; }
        }

        public string BookLineage
        {
            get { return MetaData.BookLineage; }
            set { MetaData.BookLineage = value; }
        }

        // This indicates the kind of license in use. For Creative Commons licenses, it is the Abbreviation of the CreativeCommonsLicense
        // object, the second-last (before version number) element of the licenseUrl. Other known values are 'ask' (no license granted,
        // ask the copyright holder for permission to use) 'custom' (rights presumably specified in licenseNotes)
        // Review: would it help with filtering if this field contained some indication of whether licenseNotes contains anything
        // (e.g., so we can search for CC licenses with no non-standard encumbrance)?
        public string License
        {
            get { return MetaData.License; }
            set { MetaData.License = value; }
        }

        public string FormatVersion
        {
            get { return MetaData.FormatVersion; }
            set { MetaData.FormatVersion = value; }
        }

        public string BrandingProjectKey
        {
            get { return MetaData.BrandingProjectName; }
            set { MetaData.BrandingProjectName = value; }
        }

        // When license is 'custom' this contains the license information. For other types in may contain additional permissions
        // (or possibly restrictions).
        // Review: do we need this, or just a field indicating whether there ARE additional notes, or just some modifier in license indicating that?
        public string LicenseNotes
        {
            get { return MetaData.LicenseNotes; }
            set { MetaData.LicenseNotes = value; }
        }

        public string Copyright
        {
            get { return MetaData.Copyright; }
            set { MetaData.Copyright = value; }
        }

        /// <summary>
        /// Determined at construction time, tells whether the book is part of the editable collection.
        /// This does not necessarily mean we can edit it, for example, it may not be checked out.
        /// Often, IsSaveable is a better test.
        /// </summary>
        public bool IsInEditableCollection { get; private set; }

        /// <summary>
        /// Determined by the SaveContext, which if relevant will consider team collection status.
        /// This determines whether changes to this book can currently be saved.
        /// </summary>
        public bool IsSaveable => IsInEditableCollection && _saveContext.CanSaveChanges(this);

        /// <summary>
        /// Are we currently allowed to change the book ID?
        /// Elsewhere in this class, this is referred to simply as "Id" but this is used in contexts
        /// where it is important to distinguish it from the book's databaseId on our server.
        /// </summary>
        public bool CanChangeBookInstanceId =>
            IsInEditableCollection && _saveContext.CanChangeBookInstanceId(this);

        /// <summary>
        /// If true, use a device-specific xmatter pack.
        /// This will either be a pack associated with the collection's pack by adding "-Device",
        /// e.g. ABC-Device for ABC,
        /// or "Device" if no such pack exists.
        /// </summary>
        public bool UseDeviceXMatter { get; set; }

        /// <summary>
        /// The name of the folder that contains the book. (not the full path, just the folder name)
        /// </summary>
        public string FolderName => Path.GetFileName(FolderPath);

        /// <summary>
        /// This one knows nothing of what language the user speaks... currently using that requires actually reading in the html, which is beyond what this class can do
        /// </summary>
        public string QuickTitleUserDisplay => FolderName;

        public bool TryGetPremadeThumbnail(out Image image)
        {
            string path = Path.Combine(FolderPath, "thumbnail.png");
            if (RobustFile.Exists(path))
            {
                try
                {
                    image = RobustImageIO.GetImageFromFile(path);
                    return true;
                }
                catch (Exception e) // If that file became corrupted, we would not want to lock user out of their book.
                {
                    Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
                    // Per BL-5241, and since some books in BL.org have empty thumbnail.png files and can easily get into
                    // books from bloom library, and we don't fix them there, we don't want a yellow screen even for alpha.
                    NonFatalProblem.Report(
                        ModalIf.None,
                        PassiveIf.All,
                        "Could not read thumbnail.png",
                        "Could not read thumbnail.png at " + FolderPath
                    );
                    //The file will be re-generate now.
                }
            }
            image = null;
            return false;
        }

        public void SavePublishSettings()
        {
            PublishSettings.WriteToFolder(FolderPath);
        }

        public void SaveAppearanceSettings()
        {
            AppearanceSettings.WriteToFolder(FolderPath);
        }

        public void Save()
        {
            // https://jira.sil.org/browse/BL-354 "The requested operation cannot be performed on a file with a user-mapped section open"
            var count = 0;

            do
            {
                try
                {
                    MetaData.WriteToFolder(FolderPath);
                    SavePublishSettings();
                    SaveAppearanceSettings();
                    return;
                }
                catch (IOException e)
                {
                    Thread.Sleep(500);
                    count++;

                    // stop trying after 5 attempts to save the file.
                    if (count > 4)
                    {
                        Debug.Fail("Reproduction of BL-354 that we have taken steps to avoid");

                        var msg = LocalizationManager.GetDynamicString(
                            "Bloom",
                            "BookEditor.ErrorSavingPage",
                            "Bloom wasn't able to save the changes to the page."
                        );
                        ErrorReport.NotifyUserOfProblem(e, msg);
                    }
                }
            } while (count < 5);
        }

        /// <summary>
        /// Settings for the publish tab (and Harvester).
        /// </summary>
        public PublishSettings PublishSettings { get; private set; }
        public AppearanceSettings AppearanceSettings { get; private set; }

        public string CountryName
        {
            get { return MetaData.CountryName; }
            set { MetaData.CountryName = value; }
        }

        public string ProvinceName
        {
            get { return MetaData.ProvinceName; }
            set { MetaData.ProvinceName = value; }
        }

        public string DistrictName
        {
            get { return MetaData.DistrictName; }
            set { MetaData.DistrictName = value; }
        }

        public string PHashOfFirstContentImage
        {
            get { return MetaData.PHashOfFirstContentImage; }
            set { MetaData.PHashOfFirstContentImage = value; }
        }

        internal string MetaDataPath
        {
            get { return BookMetaData.MetaDataPath(FolderPath); }
        }

        public const string MetaDataFileName = "meta.json";
        public const string PublishSettingsFileName = "publish-settings.json";

        public string Credits
        {
            get { return MetaData.Credits; }
            set { MetaData.Credits = value; }
        }

        public string Summary
        {
            get { return MetaData.Summary; }
            set { MetaData.Summary = value; }
        }

        string[] SplitList(string list)
        {
            if (list == null)
            {
                return new string[0];
            }
            return list.Split(',')
                .Select(item => item.Trim())
                .Where(item => !String.IsNullOrEmpty(item))
                .ToArray();
        }

        /// <summary>
        /// Get a comma delimited list of Topics, or set the Topic tags using a comma delimited list.
        /// </summary>
        public string TopicsList
        {
            get
            {
                return MetaData.Tags == null
                    ? ""
                    : String.Join(
                        ", ",
                        MetaData.Tags.Where(TagIsTopic).Select(GetTopicNameFromTag)
                    );
            }
            set { UpdateOneTypeOfMetaDataTags(TagIsTopic, kTopicPrefix, SplitList(value)); }
        }

        /// <summary>
        /// Allow mass uploader to set a bookshelf tag before uploading. We assume we are only setting
        /// a single bookshelf tag so we can avoid using SplitList() which depends on commas (BL-7511 had a bookshelf
        /// with a comma in the name).
        /// </summary>
        /// <remarks>This is only used by the bulk upload process.</remarks>
        public string Bookshelf
        {
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    ClearBookshelf();
                else
                    UpdateOneTypeOfMetaDataTags(TagIsBookshelf, kBookshelfPrefix, new[] { value });
                Save();
            }
        }

        internal void ClearBookshelf()
        {
            UpdateOneTypeOfMetaDataTags(TagIsBookshelf, kBookshelfPrefix, new string[] { });
        }

        private void UpdateOneTypeOfMetaDataTags(
            Func<string, bool> tagTest,
            string prefix,
            string[] valuesToSet
        )
        {
            EnsureStringsHaveCorrectPrefixes(prefix, valuesToSet);
            // Leave all the other types of tags intact. Replace all existing tags matching the prefix type.
            MetaData.Tags =
                MetaData.Tags?.Where(t => !tagTest(t)).Union(valuesToSet).ToArray() ?? valuesToSet;
        }

        /// <summary>
        /// This starts by removing any existing tags matching the given prefix.
        /// Then, if the given value has contents, it will trim it and add a tag with the given prefix and value.
        /// </summary>
        /// <param name="tagPrefixWithoutColon">The "key" of the tag. That is, the part before the colon. The colon should not be included.</param>
        /// <param name="newValueOrNull">The "value" of the tag. That is, the part after the colon. If null, empty, or all whitespace is passed in, the tag will be removed.</param>
        /// <remarks>Precondition: MetaData must be initialized already</remarks>
        public void UpdateOneSingletonTag(string tagPrefixWithoutColon, string newValueOrNull)
        {
            var list = MetaData.Tags?.ToList() ?? new List<string>();
            list.RemoveAll((t) => t.StartsWith(tagPrefixWithoutColon + ":"));
            var value = newValueOrNull;
            value = value?.Trim(); // I'm feeling defensive
            if (!string.IsNullOrEmpty(value))
            {
                list.Add(tagPrefixWithoutColon + ":" + value);
            }
            MetaData.Tags = list.ToArray();
        }

        public int PageCount
        {
            get { return MetaData.PageCount; }
            set { MetaData.PageCount = value; }
        }

        /// <summary>
        /// So far, this is just a way of getting at the metadata field. It is only set during book upload.
        /// </summary>
        public LanguageDescriptor[] LanguageDescriptors
        {
            get { return MetaData.LanguageDescriptors; }
            set { MetaData.LanguageDescriptors = value; }
        }

        public List<ToolboxToolState> Tools
        {
            get
            {
                if (MetaData.ToolStates == null)
                    MetaData.ToolStates = new List<ToolboxToolState>();
                return MetaData.ToolStates;
            }
            set { MetaData.ToolStates = value; }
        }

        public string CurrentTool
        {
            get { return MetaData.CurrentTool; }
            set { MetaData.CurrentTool = value; }
        }

        // Whether we should allow the reader tools initially. (Was, whether to show at all. As of BL-2907, they are always an option).
        public bool ToolboxIsOpen
        {
            get { return MetaData.ToolboxIsOpen; }
            set { MetaData.ToolboxIsOpen = value; }
        }

        public static IEnumerable<string> TopicsKeys
        {
            get
            {
                //If you modify any of these, consider modifying/updating the localization files; the localization ids for these are just the current English (which is fragile)
                //If you make changes/additions here, also synchronize with blorg's ClosedVocabularies.ts

                return new[]
                {
                    "Agriculture",
                    "Animal Stories",
                    "Bible",
                    "Business",
                    "Culture",
                    "Community Living",
                    "Dictionary",
                    "Environment",
                    "Fiction",
                    "Health",
                    "How To",
                    "Math",
                    "Non Fiction",
                    "Personal Development",
                    "Primer",
                    "Science",
                    "Story Book",
                    "Traditional Story"
                };
            }
        }

        public void SetLicenseAndCopyrightMetadata(Metadata metadata)
        {
            License = metadata.License.Token;
            Copyright = metadata.CopyrightNotice;
            // obfuscate any emails in the license notes.
            var notes = metadata.License.RightsStatement;
            if (notes != null)
            {
                // recommended at http://www.regular-expressions.info/email.html.
                // This purposely does not handle non-ascii emails, or ones with special characters, which he says few servers will handle anyway.
                // It is also not picky about exactly valid top-level domains (or country codes), and will exclude the rare 'museum' top-level domain.
                // There are several more complex options we could use there. Just be sure to add () around the bit up to (and including) the @,
                // and another pair around the rest.
                var regex = new Regex(
                    "\\b([A-Z0-9._%+-]+@)([A-Z0-9.-]+.[A-Z]{2,4})\\b",
                    RegexOptions.IgnoreCase
                );
                // We keep the existing email up to 2 characters after the @, and replace the rest with a message.
                // Not making the message localizable as yet, since the web site isn't, and I'm not sure what we would need
                // to put to make it so. A fixed string seems more likely to be something we can replace with a localized version,
                // in the language of the web site user rather than the language of the uploader.
                notes = regex.Replace(
                    notes,
                    new MatchEvaluator(
                        m =>
                            m.Groups[1].Value
                            + m.Groups[2].Value.Substring(0, 2)
                            + "(download book to read full email address)"
                    )
                );
                LicenseNotes = notes;
            }
        }

        /// <summary>
        /// Check whether this book (or its pages) should be shown as a source.  If it is not experimental, the answer
        /// is always "yes".  If it is experimental, then show it only if the user wants experimental sources.
        /// </summary>
        internal bool ShowThisBookAsSource()
        {
            return MetaData.ShowThisBookAsSource();
        }

        private static bool TagIsTopic(string tag)
        {
            return TagIsCorrectType(kTopicPrefix, tag);
        }

        private static bool TagIsBookshelf(string tag)
        {
            return TagIsCorrectType(kBookshelfPrefix, tag);
        }

        private static bool TagIsCorrectType(string prefix, string tag)
        {
            return tag.StartsWith(prefix) || !tag.Contains(":");
        }

        private static string GetTopicNameFromTag(string tag)
        {
            return tag.StartsWith(kTopicPrefix) ? tag.Substring(kTopicPrefix.Length) : tag;
        }

        private static void EnsureStringsHaveCorrectPrefixes(string prefix, string[] tagStrings)
        {
            for (int i = 0; i < tagStrings.Length; i++)
            {
                if (!tagStrings[i].StartsWith(prefix))
                    tagStrings[i] = prefix + tagStrings[i];
            }
        }

        /// <summary>
        /// Replace all occurrences of bookInstanceId with a fresh Guid, since this book is a manual duplicate.
        /// - Update meta.json
        /// - Delete bookName.bloombookorder (if present) - note, these are obsolete and no longer created
        /// - Delete meta.bak (if present)
        /// </summary>
        public static string InstallFreshInstanceGuid(string bookFolder)
        {
            var metaData = BookMetaData.FromFolder(bookFolder) ?? new BookMetaData();
            var freshGuidString = Guid.NewGuid().ToString();
            metaData.Id = freshGuidString;
            var count = 0;

            do
            {
                try
                {
                    metaData.WriteToFolder(bookFolder);
                    RobustFile.Delete(BookOrderPath(bookFolder));
                    RobustFile.Delete(BookMetaData.BackupFilePath(bookFolder));
                    break;
                }
                catch (UnauthorizedAccessException e)
                {
                    // Don't display modal, always toast
                    NonFatalProblem.Report(
                        ModalIf.None,
                        PassiveIf.All,
                        "Failed to repair duplicate id",
                        "BookInfo.InstallFreshInstanceGuid() failed to repair duplicate id in locked meta.json file at "
                            + bookFolder,
                        e
                    );
                    break; // retry is unlikely to help here, and we don't want repeated warning messages.
                }
                catch (IOException e)
                {
                    Thread.Sleep(500);
                    count++;

                    // stop trying after 5 attempts to save the file.
                    if (count > 4)
                    {
                        Debug.Fail("Reproduction of BL-354 that we have taken steps to avoid");

                        var msg = LocalizationManager.GetDynamicString(
                            "Bloom",
                            "BookEditor.ErrorSavingPage",
                            "Bloom wasn't able to save the changes to the page."
                        );
                        ErrorReport.NotifyUserOfProblem(e, msg);
                    }
                }
            } while (count < 5);

            return freshGuidString;
        }

        /// <summary>
        /// In the past we've had problems with users copying folders manually and creating derivative books with
        /// the same bookInstanceId guids. If they then try to upload both books with duplicate ids, the
        /// duplicates overwrite whichever book got uploaded first.
        /// This method recurses through the folders under 'pathToDirectory' and keeps track of all the unique bookInstanceId
        /// guids. When a duplicate is found, we will call InstallFreshInstanceGuid().
        /// </summary>
        public static void CheckForDuplicateInstanceIdsAndRepair(
            string pathToDirectory,
            Func<string, bool> okToChangeId
        )
        {
            // Key is instanceId guid, Value is a SortedList of entries where the key is the LastEdited datetime of the
            // meta.json file and the value is the filepath.
            var idToSortedFilepathsMap = new Dictionary<string, SortedList<DateTime, string>>();
            var currentFolder = pathToDirectory;
            GatherInstanceIdsRecursively(currentFolder, idToSortedFilepathsMap);
            // All the data is gathered, now to fix any problems. We assume that the first entry in the SortedList
            // is the original and we change the guid Id in all the copies.
            foreach (var kvp in idToSortedFilepathsMap)
            {
                var id = kvp.Key;
                var sortedFilepaths = kvp.Value;
                if (sortedFilepaths.Count < 2) // no problem here!
                    continue;

                // okToChange may be quite expensive, possibly involving spinning up a ProjectContext.
                // We only want to test it for books actually in a conflict set.
                // Make sure we do this evaluation only once.
                var filePathsToChange = sortedFilepaths.Values.Where(p => okToChangeId(p)).ToList();
                if (filePathsToChange.Count() == sortedFilepaths.Count)
                {
                    // just skip the first and change the others
                    filePathsToChange = sortedFilepaths.Values.Skip(1).ToList();
                }

                if (filePathsToChange.Count() < sortedFilepaths.Count - 1)
                {
                    // Review: strong alternative candidates:
                    // - do nothing; we'll hope duplicate IDs don't cause catastrophic problems.
                    // - change all the ones we can, hope the remaining duplicates don't cause catastrophic problems
                    // - fix duplicates anyway, except for one of the TC ones; hope changed TC ID doesn't cause catastrophic problems
                    // The last is most similar to the way things were before adding this code to prevent changing
                    // IDs of books checked in to TC.
                    // My inclination is to let it crash for now. I expect duplicate IDs are fairly rare, and for more
                    // than one member of a duplicate set to be in a TC is hopefully vanishingly rare. If it happens,
                    // I think it's worth hearing about.
                    var problemPaths = string.Join(
                        ", ",
                        sortedFilepaths.Values.Except(filePathsToChange).Select(Path.GetFileName)
                    );
                    throw new FatalException(
                        "Bloom found two or more books that are already shared in your Team Collection and have the same ID ("
                            + problemPaths
                            + "). You will need help from the Bloom Team to sort this out."
                    );
                }

                Logger.WriteEvent($"***Fixing {filePathsToChange.Count()} duplicate ids for: {id}");
                foreach (var filepath in filePathsToChange)
                {
                    InstallFreshInstanceGuid(filepath);
                }
            }
        }

        private static void GatherInstanceIdsRecursively(
            string currentFolder,
            IDictionary<string, SortedList<DateTime, string>> idToSortedFilepathsMap
        )
        {
            const string metaJsonFileName = "meta.json";

            var metaJsonPath = Path.Combine(currentFolder, metaJsonFileName);
            try
            {
                if (!RobustFile.Exists(metaJsonPath))
                {
                    var subDirectories = Directory.GetDirectories(currentFolder);
                    foreach (var subDirectory in subDirectories)
                    {
                        GatherInstanceIdsRecursively(subDirectory, idToSortedFilepathsMap);
                    }
                    return;
                }
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.WriteLine(
                    $"GatherInstanceIdsRecursively({currentFolder}: UnauthorizedAccessException: {e.Message}"
                );
                return; // we don't have permission to read this folder, so we can't do anything with it.
            }
            // Leaf node; we're in a book folder
            var metaFileLastWriteTime = RobustFile.GetLastWriteTimeUtc(metaJsonPath);
            var id = (BookMetaData.FromFolder(currentFolder) ?? new BookMetaData()).Id;
            SafelyAddToIdSet(id, metaFileLastWriteTime, currentFolder, idToSortedFilepathsMap);
        }

        internal string GetBestTitleForUserDisplay(List<string> langCodes)
        {
            if (FileNameLocked)
                return FolderName;
            try
            {
                // JSON parsing requires newlines to be double quoted with backslashes inside string values.
                var jsonString =
                    AllTitles == null ? "{}" : AllTitles.Replace("\r", "\\r").Replace("\n", "\\n");
                dynamic titles = DynamicJson.Parse(jsonString);
                IEnumerable<string> langs = titles.GetDynamicMemberNames();
                var multiText = new MultiTextBase();
                // I have no idea why "item" gets included...it's never a language id, so never in the json...
                // but sometimes it does, and then titles["item"] throws, and this method does not
                // behave as expected.
                foreach (var lang in langs.Where((l) => l != "item"))
                    multiText[lang] = titles[lang].Trim();
                return Book.GetBestTitleForDisplay(multiText, langCodes, IsInEditableCollection);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return Title;
        }

        private static void SafelyAddToIdSet(
            string bookId,
            DateTime lastWriteTime,
            string bookFolder,
            IDictionary<string, SortedList<DateTime, string>> idToSortedFilepathsMap
        )
        {
            SortedList<DateTime, string> sortedFilepaths;

            if (!idToSortedFilepathsMap.TryGetValue(bookId, out sortedFilepaths))
            {
                var list = new SortedList<DateTime, string> { { lastWriteTime, bookFolder } };
                idToSortedFilepathsMap.Add(bookId, list);
            }
            else
            {
                // if lastWriteTime was totally reliable, I'd assume that 2 files couldn't have been written
                // at the same time, but evidence in several places on the internet suggests otherwise, so
                // we'll use what we have and if we find duplicates we have been defeated in our intent. We will
                // then just add a few milliseconds to the lastWriteTime and try again.
                var oneTick = new TimeSpan(1);
                while (sortedFilepaths.ContainsKey(lastWriteTime))
                {
                    // not good
                    lastWriteTime += oneTick;
                }
                sortedFilepaths.Add(lastWriteTime, bookFolder);
            }
        }

        /// <summary>
        /// Store the book's status in the Bloom Library.  This is not saved in either the meta.json file
        /// or in the publish-settings.json file.  It is always retrieved from the Bloom Library when the
        /// collection is loaded or when books are uploaded.
        /// </summary>
        public BloomLibraryStatus BloomLibraryStatus;
    }

    public class ErrorBookInfo : BookInfo
    {
        public ErrorBookInfo(string folderPath, Exception exception) //No: our known-bad contents could crash that: base(folderPath,false)
        {
            FolderPath = folderPath;
            Exception = exception;
        }

        public Exception Exception { get; set; }
    }

    /// <summary>
    /// This just wraps the stuff we put in the json file.
    /// It is tempting to just serialize/deserialize the BookInfo itself.
    /// However, that would require us to refactor all the code that creates BookInfos, since
    /// it expects to use a constructor taking a pathname, while the Json code expects to
    /// create the object for us out of the pathname.
    /// Also, separating them like this means we don't have to be careful to mark things we don't want in the json.
    /// </summary>
    public class BookMetaData
    {
        internal const string BackupExtension = "bak";

        public BookMetaData()
        {
            IsExperimental = false;
            AllowUploadingToBloomLibrary = true;
            Draft = false;
            BookletMakingIsAppropriate = true;
            IsSuitableForVernacularLibrary = true;
            Id = Guid.NewGuid().ToString();
            BookLineage = "";
        }

        public static BookMetaData FromString(string input)
        {
            var result = FromStringUnchecked(input);
            if (result == null)
            {
                throw new ApplicationException("meta.json of this book may be corrupt");
            }
            return result;
        }

        public static BookMetaData FromFile(string path)
        {
            // If it's a normal meta.json file, use FromFolder, which can recover from problems
            // if necessary by restoring a backup.
            BookMetaData result;
            if (Path.GetFileName(path) == "meta.json")
                result = FromFolder(Path.GetDirectoryName(path));
            else if (TryReadMetaData(path, out result))
                return result;
            if (result == null)
                result = GetRepairedMetaDataWithIdOnly(path);
            if (result == null)
                throw new ApplicationException("Failed to get any usable metadata from " + path);
            return result;
        }

        public static BookMetaData FromStringUnchecked(string input)
        {
            return JsonConvert.DeserializeObject<BookMetaData>(input);
        }

        /// <summary>
        /// Make a metadata, usually by just reading the meta.json file in the book folder.
        /// If some exception is thrown while trying to do that, or if it doesn't exist,
        /// Try reading a backup (and restore it if successful).
        /// If that also fails, try to at least extract an ID from the meta.json, using a regex,
        /// and if successful return a metadata in its default state except for that ID.
        /// If that also fails, return null.
        /// </summary>
        public static BookMetaData FromFolder(string bookFolderPath)
        {
            var metaDataPath = MetaDataPath(bookFolderPath);
            BookMetaData result;
            if (TryReadMetaData(metaDataPath, out result))
                return result;

            var backupPath = BackupFilePath(bookFolderPath);
            if (RobustFile.Exists(backupPath) && TryReadMetaData(backupPath, out result))
            {
                RobustFile.Delete(metaDataPath); // Don't think it's worth saving the corrupt one
                RobustFile.Move(backupPath, metaDataPath);
                return result;
            }
            // Things are messed up, but maybe we can still get an ID from the file?
            // That's the single most important thing in it; the rest can be restored from
            // the book or just holds stuff like tool state.
            // For example, in BL-11821 we encountered meta.json files where various integers
            // were represented with decimals (0.0 instead of 0) which produced a JsonReaderException.
            return GetRepairedMetaDataWithIdOnly(metaDataPath);
        }

        /// <summary>
        /// This is generally used when we fail to parse a metadata file properly.
        /// It attempts to get one that at least has the right ID.
        /// </summary>
        /// <param name="MetaDataPath"></param>
        /// <returns></returns>
        public static BookMetaData GetRepairedMetaDataWithIdOnly(string metaDataPath)
        {
            if (RobustFile.Exists(metaDataPath))
            {
                string id;
                try
                {
                    id = GetIdFromDamagedMetaDataString(RobustFile.ReadAllText(metaDataPath));
                }
                catch (IOException error)
                {
                    throw new FileException(metaDataPath, error);
                }
                if (id != null)
                    return new BookMetaData() { Id = id };
            }

            return null;
        }

        /// <summary>
        /// Try to get a book ID from a (typically damaged, unreadable) metaDataString.
        /// </summary>
        public static string GetIdFromDamagedMetaDataString(string metaDataString)
        {
            var re = new Regex(@"""bookInstanceId"":""(.*?)""");
            var match = re.Match(metaDataString);
            if (match.Success)
                return match.Groups[1].Value;
            return null;
        }

        private static bool TryReadMetaData(string path, out BookMetaData result)
        {
            result = null;
            if (!RobustFile.Exists(path))
                return false;
            string metadataString = "read failed"; // for error reporting
            try
            {
                metadataString = RobustFile.ReadAllText(path);
                result = FromStringUnchecked(metadataString);
                if (result == null)
                {
                    Logger.WriteEvent(
                        "Failed to read metadata from "
                            + path
                            + " with contents '"
                            + metadataString
                            + "'"
                    );
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Logger.WriteError(
                    "Got error reading meta.json from "
                        + path
                        + " with contents '"
                        + metadataString
                        + "'",
                    e
                );
                return false;
            }
        }

        public static string MetaDataPath(string bookFolderPath)
        {
            return bookFolderPath.CombineForPath(BookInfo.MetaDataFileName);
        }

        public static string BackupFilePath(string bookFolderPath)
        {
            return Path.ChangeExtension(MetaDataPath(bookFolderPath), BackupExtension);
        }

        public void WriteToFolder(string bookFolderPath, bool makeBackup = true)
        {
            string tempFilePath;
            var metaDataPath = MetaDataPath(bookFolderPath);
            try
            {
                using (var temp = TempFile.InFolderOf(metaDataPath))
                {
                    tempFilePath = temp.Path;
                }
            }
            catch (UnauthorizedAccessException e)
            {
                throw new BloomUnauthorizedAccessException(metaDataPath, e);
            }

            RobustFile.WriteAllText(tempFilePath, Json);
            if (RobustFile.Exists(metaDataPath))
                RobustFile.Replace(
                    tempFilePath,
                    metaDataPath,
                    makeBackup ? BackupFilePath(bookFolderPath) : null
                );
            else
                RobustFile.Move(tempFilePath, metaDataPath);
        }

        [JsonIgnore]
        public string Json
        {
            get { return JsonConvert.SerializeObject(this); }
        }

        /// <summary>
        /// Get the reduced Json string that we upload to set the database entry for the book on our website.
        /// This leaves out some of the metadata that we use while working on the book.
        /// Note that the full metadata is currently uploaded to S3 as part of the book content;
        /// this reduced subset is just for the website itself.
        /// Note that if you add a property to the upload set here, you must manually add a corresponding field to
        /// the books table in the database. This is very important. Currently, the field will auto-add to
        /// the databases used for unit testing and even (I think) the one for sandbox testing,
        /// but not to the live site; so if you forget to do this uploading will suddenly break.
        /// It is for this reason that we deliberately don't automatically add new fields to the upload set.
        /// Note that it is desirable that the name you give each property in the anonymous object which
        /// get jsonified here matches the JsonProperty name used to deserialize it.
        /// That allows the WebDataJson to be a valid Json representation of this class with just
        /// some fields left out. At least one unit test will fail if the names don't match.
        /// (Though, I don't think anything besides that test currently attempts to create
        /// a BookMetaData object from a WebDataJson string.)
        /// It is of course vital that the names in the anonymous object match the fields in the database.
        /// </summary>
        [JsonIgnore]
        public string WebDataJson =>
            JsonConvert.SerializeObject(
                new
                {
                    bookInstanceId = Id,
                    suitableForMakingShells = IsSuitableForMakingShells, // not yet used by BL, potentially useful filter
                    suitableForVernacularLibrary = IsSuitableForVernacularLibrary, // not yet used by BL, potentially useful filter
                    experimental = IsExperimental, // not yet used by BL (I think), potentially useful filter
                    title = Title,
                    allTitles = AllTitles, // created for BL to search, though it doesn't yet.
                    originalTitle = OriginalTitle,
                    baseUrl = BaseUrl, // how web site finds image and download
                    isbn = Isbn,
                    bookLineage = BookLineage,
                    license = License,
                    formatVersion = FormatVersion,
                    licenseNotes = LicenseNotes,
                    copyright = Copyright,
                    credits = Credits,
                    tags = Tags,
                    summary = Summary,
                    pageCount = PageCount,
                    languageDescriptors = LanguageDescriptors, // the upload azure function converts this to language object pointers before saving to the database
                    leveledReaderLevel = LeveledReaderLevel,
                    country = CountryName,
                    province = ProvinceName,
                    district = DistrictName,
                    features = Features,
                    internetLimits = InternetLimits,
                    importedBookSourceUrl = ImportedBookSourceUrl,
                    phashOfFirstContentImage = PHashOfFirstContentImage,
                    updateSource = GetUpdateSource(),
                    publisher = Publisher,
                    originalPublisher = OriginalPublisher,
                    draft = Draft,
                    // Other fields are not needed by the web site and we don't expect they will be.
                }
            );

        [JsonProperty("bookInstanceId")]
        public string Id { get; set; }

        [JsonProperty("suitableForMakingShells")]
        public bool IsSuitableForMakingShells { get; set; }

        // Special property for Template Starter template.
        [JsonProperty("suitableForMakingTemplates")]
        public bool IsSuitableForMakingTemplates { get; set; }

        [JsonProperty("suitableForVernacularLibrary")]
        public bool IsSuitableForVernacularLibrary { get; set; }

        /// <summary>
        /// This version number is set when making a meta.json to embed in a bloompub file.
        /// We increment it whenever something changes that bloom-player or some other
        /// client might need to know about. It is NOT intended that the player would
        /// refuse to open a book with a higher number than it knows about; we may one day
        /// implement another mechanism for that. Rather, this is intended to allow a
        /// newer player which accommodates older books to know which of those accommodations
        /// are needed.
        /// See the one place where it is set for a history of the versions and what each
        /// indicates about the bloompub content.
        /// </summary>
        [JsonProperty("bloomdVersion")]
        public int BloomdVersion { get; set; }

        //SeeAlso: commented IsExperimental on Book
        [JsonProperty("experimental")]
        public bool IsExperimental { get; set; }

        /// <summary>
        /// Check whether this book (or its pages) should be shown as a source.  If it is not experimental, the answer
        /// is always "yes".  If it is experimental, then show it only if the user wants experimental sources.
        /// </summary>
        internal bool ShowThisBookAsSource()
        {
            return !IsExperimental
                || ExperimentalFeatures.IsFeatureEnabled(
                    ExperimentalFeatures.kExperimentalSourceBooks
                );
        }

        [JsonProperty("brandingProjectName")]
        public string BrandingProjectName { get; set; }

        /// <summary>
        /// True if the user explicitly set a name (name is not automatically derived
        /// from title).
        /// </summary>
        [JsonProperty("nameLocked")]
        public bool NameLocked { get; set; }

        /// <summary>
        /// A "Folio" document is one that acts as a wrapper for a number of other books
        /// </summary>
        [JsonProperty("folio")]
        public bool IsFolio { get; set; }

        // A book is considerted RTL if its first content language is.
        [JsonProperty("isRtl")]
        public bool IsRtl { get; set; }

        // Enhance: multilingual?
        // BL-3774 was caused by a book with a meta.json value for Title of null.
        // So here let's just ensure we have store strings in that situation.
        private string _title = string.Empty;

        [JsonProperty("title")]
        public string Title
        {
            get { return _title; }
            set { _title = value == null ? string.Empty : value; }
        }

        [JsonProperty("allTitles")]
        public string AllTitles { get; set; }

        [JsonProperty("originalTitle")]
        public string OriginalTitle { get; set; }

        // This is filled in when we upload the book. It is not used locally, but becomes a field in the book record database
        // containing the actual url where we can grab the thumbnails, pdfs, etc.
        public string BaseUrl { get; set; }

        [JsonProperty("isbn")]
        public string Isbn { get; set; }

        [JsonProperty("bookLineage")]
        public string BookLineage { get; set; }

        // This indicates the kind of license in use. For Creative Commons licenses, it is the Abbreviation of the CreativeCommonsLicense
        // object, the second-last (before version number) element of the licenseUrl. Other known values are 'ask' (no license granted,
        // ask the copyright holder for permission to use) 'custom' (rights presumably specified in licenseNotes)
        // Review: would it help with filtering if this field contained some indication of whether licenseNotes contains anything
        // (e.g., so we can search for CC licenses with no non-standard encumbrance)?
        [JsonProperty("license")]
        public string License { get; set; }

        [JsonProperty("formatVersion")]
        public string FormatVersion { get; set; }

        // When license is 'custom' this contains the license information. For other types in may contain additional permissions
        // (or possibly restrictions).
        // Review: do we need this, or just a field indicating whether there ARE additional notes, or just some modifier in license indicating that?
        [JsonProperty("licenseNotes")]
        public string LicenseNotes { get; set; }

        [JsonProperty("copyright")]
        public string Copyright { get; set; }

        [JsonProperty("credits")]
        public string Credits { get; set; }

        /// <summary>
        /// This is intended to be a list of strings, possibly from a restricted domain, indicating kinds of content
        /// the book contains. Currently it contains one member of the Topics list and possibly a bookshelf for the website.
        /// </summary>
        [JsonProperty("tags")]
        public string[] Tags { get; set; }

        [JsonProperty("pageCount")]
        public int PageCount { get; set; }

        // This is obsolete but loading old Json files fails if we don't have a setter for it.
        [JsonProperty("languages")]
        public string[] Languages
        {
            get { return new string[0]; }
            set { }
        }

        [JsonProperty("languageDescriptors")]
        public LanguageDescriptor[] LanguageDescriptors { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }

        // This is set to true in situations where the materials that are not permissively licensed and the creator doesn't want derivative works being uploaded.
        // Currently we don't need this property in the book record database, so we don't upload it.
        [JsonProperty(
            "allowUploadingToBloomLibrary",
            DefaultValueHandling = DefaultValueHandling.Populate
        )]
        [DefaultValue(true)]
        public bool AllowUploadingToBloomLibrary { get; set; }

        // Make book visible only to reviewers with URL
        [JsonProperty("draft")]
        [DefaultValue(false)]
        public bool Draft { get; set; }

        [JsonProperty(
            "bookletMakingIsAppropriate",
            DefaultValueHandling = DefaultValueHandling.Populate
        )]
        [DefaultValue(true)]
        public bool BookletMakingIsAppropriate { get; set; }

        /// <summary>
        /// This is an item the user checks-off as part of claiming that the book is fully accessible.
        /// </summary>
        [JsonProperty("a11y_NoEssentialInfoByColor")]
        public bool A11y_NoEssentialInfoByColor;

        /// <summary>
        /// This is an item the user checks-off as part of claiming that the book is fully accessible.
        /// </summary>
        [JsonProperty("a11y_NoTextIncludedInAnyImages")]
        public bool A11y_NoTextIncludedInAnyImages;

        // About this ignore: this actually would be perfectly fine as a top-level bit of true metadata.
        // However, it is currently also in the toolstate, so we're leaving it out of the meta.json so as not to
        // duplicate that. We could reasonably decide in the future to instead make *this* the source of truth and
        // have the tool use this value.  Note too that there is code that takes the toolbox state for level and
        // sends that to the book record database, so that is treating it as a top level property.
        [JsonIgnore]
        public int LeveledReaderLevel
        {
            get
            {
                // There is also some code that looks at the ancestry to decide if something is a leveled reader.
                // There is *also* (bear with me) another flag, currently accessed via menu on the book thumbnail,
                // for saying this is a leveled reader. That value is just a class, "leveled-reader", that is set in
                // the class attribute of the body of the document.

                // JT & JH talking on 8/31/2021: we agree that data-leveledReaderLevel should become the source of truth
                // for the book, because these are really statements about the book. *real* metadata (not just tool state).
                // Then the tool state, the property in meta.json, and the "leveled-reader" class on body
                // should just be reflections of that.
                // We would need a migration in order to get the level the old way and put it onto these body attributes.
                // It's not clear to JH at the moment what the best way would be for the user to say "this is a leveled reader";
                // currently we kind of have 3 competing ways: the thumbnail menu item, if the tool itself has a check mark, and
                // the ancestry of the book.

                var leveledReaderTool = ToolStates?.SingleOrDefault(
                    t => t.ToolId == "leveledReader"
                );
                if (
                    leveledReaderTool == null
                    || leveledReaderTool.State == null
                    || !leveledReaderTool.Enabled
                )
                    return 0;

                if (int.TryParse(leveledReaderTool.State, out int level))
                {
                    return level;
                }
                return 0;
            }
        }

        // About this ignore: See note on LeveledReaderLevel
        [JsonIgnore]
        public int DecodableReaderStage
        {
            get
            {
                var decodableReaderTool = ToolStates?.SingleOrDefault(
                    t => t.ToolId == "decodableReader"
                );
                if (decodableReaderTool == null)
                    return 0;

                /* sadly, these tool states are not actual json. They don't have quotes around string values.
                 So this won't work. Maybe someday?
                    var definition = new { stage = 0 };
                    var x = JsonConvert.DeserializeAnonymousType("{" + decodableReaderTool.State + "}", definition);
                    return x.stage;
                */

                if (
                    ParsePseudoJsonToolboxState(decodableReaderTool.State)
                        .TryGetValue("stage", out string stage)
                )
                {
                    if (int.TryParse(stage, out var stageNumber))
                    {
                        return stageNumber;
                    }
                }
                return 0;
            }
        }

        // Sadly, these tool states are not actual json objects.They don't have {, } nor quotes around string values.
        // I don't know which tools use this format for states, but decodable reader does (at least through 5.1).
        private Dictionary<string, string> ParsePseudoJsonToolboxState(string stateString)
        {
            var x = new Dictionary<string, string>();
            if (stateString == null)
                return x;
            var pairs = stateString.Split(new[] { ';' });
            foreach (var pair in pairs)
            {
                var parts = pair.Split(new[] { ':' });
                x.Add(parts[0], parts[1]);
            }
            return x;
        }

        [JsonProperty("country")]
        public string CountryName { get; set; }

        [JsonProperty("province")]
        public string ProvinceName { get; set; }

        [JsonProperty("district")]
        public string DistrictName { get; set; }

        /// <summary>These panels are being displayed in the toolbox for this book</summary>
        /// <example>["decodableReader", "leveledReader", "pageElements"]</example>
        [JsonProperty("tools", ItemConverterType = typeof(ToolboxToolConverter))]
        public List<ToolboxToolState> ToolStates { get; set; }

        [JsonProperty("currentTool", NullValueHandling = NullValueHandling.Ignore)]
        public string CurrentTool { get; set; }

        [JsonProperty("toolboxIsOpen")]
        [DefaultValue(false)]
        public bool ToolboxIsOpen { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        /// <summary>
        /// The publisher of the book.  For many books, this may be unset because the book is "self-published".
        /// </summary>
        [JsonProperty("publisher")]
        public string Publisher { get; set; }

        [JsonProperty("originalPublisher")]
        public string OriginalPublisher { get; set; }

        // tags from Thema (https://www.editeur.org/151/Thema/)
        [JsonProperty("subjects")]
        public SubjectObject[] Subjects { get; set; }

        //https://www.w3.org/wiki/WebSchemas/Accessibility#Features_for_augmentation
        [JsonProperty("hazards")]
        public string Hazards { get; set; }

        //https://www.w3.org/wiki/WebSchemas/Accessibility#Features_for_augmentation
        [JsonProperty("a11yFeatures")]
        public string A11yFeatures { get; set; }

        //http://www.idpf.org/epub/a11y/accessibility.html#sec-conf-reporting
        [JsonProperty("a11yLevel")]
        public string A11yLevel { get; set; }

        //http://www.idpf.org/epub/a11y/accessibility.html#sec-conf-reporting
        [JsonProperty("a11yCertifier")]
        public string A11yCertifier { get; set; }

        // Global Digital Library: Indicates reading level
        // NB: this is just "level" in the Global Digital Library
        // e.g. "Pratham Level 1"
        [JsonProperty("readingLevelDescription")]
        public string ReadingLevelDescription { get; set; }

        // Global Digital Library: The typical range of ages the content’s intended end user.
        [JsonProperty("typicalAgeRange")]
        public string TypicalAgeRange { get; set; }

        [JsonProperty("features")]
        public string[] Features
        {
            get
            {
                var features = new List<string>(5);

                AddFeaturesToList(features, "blind", Feature_Blind_LangCodes);
                AddFeaturesToList(features, "talkingBook", Feature_TalkingBook_LangCodes);
                AddFeaturesToList(features, "signLanguage", Feature_SignLanguage_LangCodes);

                if (Feature_Video)
                    features.Add("video");
                if (Feature_Motion)
                    features.Add("motion");
                // Feature code is unchanged despite the toolbox tool rename to Overlay Tool.
                if (Feature_Comic)
                    features.Add("comic");
                if (Feature_Activity)
                    features.Add("activity");
                if (Feature_Quiz)
                    features.Add("quiz");
                if (Feature_Widget)
                    features.Add("widget");
                if (Feature_SimpleDomChoice)
                    features.Add("simple-dom-choice");

                return features.ToArray();
            }
            set
            {
                Feature_Motion = value.Contains("motion");
                Feature_Comic = value.Contains("comic");
                Feature_Video = value.Contains("video");
                // no need to set Feature_Activity, it's automatically derived
                Feature_Quiz = value.Contains("quiz");
                Feature_Widget = value.Contains("widget");
                Feature_SimpleDomChoice = value.Contains("simple-dom-choice");

                Feature_Blind_LangCodes = new HashSet<string>();
                Feature_TalkingBook_LangCodes = new HashSet<string>();
                Feature_SignLanguage_LangCodes = new HashSet<string>();

                foreach (var featureString in value)
                {
                    var fields = featureString.Split(':');
                    if (fields.Length < 2)
                    {
                        continue;
                    }

                    string featureName = fields[0];
                    string langCode = fields[1];

                    switch (featureName)
                    {
                        case "blind":
                            ((HashSet<string>)Feature_Blind_LangCodes).Add(langCode);
                            break;
                        case "talkingBook":
                            ((HashSet<string>)Feature_TalkingBook_LangCodes).Add(langCode);
                            break;
                        case "signLanguage":
                            ((HashSet<string>)Feature_SignLanguage_LangCodes).Add(langCode);
                            break;
                    }
                }

                // Handle special case for sign language if it had a sign language video but the sign language code was not set in collection settings
                if (value.Contains("signLanguage") && !Feature_SignLanguage_LangCodes.Any())
                {
                    ((HashSet<string>)Feature_SignLanguage_LangCodes).Add("");
                }

                // Backwards compatability for reading old JSONs that don't contain language-specific features
                if (value.Contains("blind") && !Feature_Blind_LangCodes.Any())
                {
                    ((HashSet<string>)Feature_Blind_LangCodes).Add("");
                }
                if (value.Contains("talkingBook") && !Feature_TalkingBook_LangCodes.Any())
                {
                    ((HashSet<string>)Feature_TalkingBook_LangCodes).Add("");
                }
            }
        }

        /// <summary>
        /// Modifies featureList with the new features to be added
        /// Includes both the overall feature (e.g. "talkingBook" as well as the language-specific features ("talkingBook:en")
        /// </summary>
        /// <param name="featureList">The list that will be modified. This method may add discovered features here</param>
        /// <param name="featureName">The name (prefix) of the feature. e.g. "talkingBook" in "talkingBook:en"</param>
        /// <param name="languagesWithFeature">An IEnumerable of language codes that contain the specified feature</param>
        private static void AddFeaturesToList(
            IList<string> featureList,
            string featureName,
            IEnumerable<string> languagesWithFeature
        )
        {
            if (languagesWithFeature?.Any() == true)
            {
                featureList.Add(featureName);
                var featureValues = languagesWithFeature
                    .Where(HtmlDom.IsLanguageValid)
                    .Select(langCode => $"{featureName}:{langCode}");
                featureList.AddRange(featureValues);
            }
        }

        [JsonIgnore]
        public IEnumerable<string> Feature_Blind_LangCodes { get; set; }

        [JsonIgnore]
        public IEnumerable<string> Feature_TalkingBook_LangCodes { get; set; }

        // SL only expected to have 0 or 1 elements. Kind of like a bool, but the difference is
        // we do actually need to know what the language code is for it too.
        // The element might be "", which means that the SL language code was not set in the collection settings.
        [JsonIgnore]
        public IEnumerable<string> Feature_SignLanguage_LangCodes { get; set; }

        // Note:
        //   Originally, Feature_Activity was true if and only if the book was a widget.
        //   Then, Bloom Library decided that quizzes should be renamed to activity instead.
        //   So now, Bloom Desktop will make it so that quizzes, widgets, and simple-choice pages
        //   count as the more general "activity", and each of these will also get a more
        //   specific tag applied too.
        [JsonIgnore]
        public bool Feature_Activity => Feature_Quiz || Feature_Widget || Feature_SimpleDomChoice;

        [JsonIgnore]
        public bool Feature_TalkingBook
        {
            get { return Feature_TalkingBook_LangCodes?.Any() == true; }
        }

        [JsonIgnore]
        public bool Feature_SignLanguage
        {
            get { return Feature_SignLanguage_LangCodes?.Any() == true; }
        }

        [JsonIgnore]
        public bool Feature_Video { get; set; }

        [JsonIgnore]
        public bool Feature_Motion { get; set; }

        [JsonIgnore]
        public bool Feature_Quiz { get; set; }

        // Feature name is unchanged despite the toolbox tool rename to Overlay Tool, because they are stored
        // in the book record database and used from there by Bloom Library, and because older versions of Bloom would not
        // recognize a 'Feature_Overlay'.
        [JsonIgnore]
        public bool Feature_Comic { get; set; }

        [JsonIgnore]
        public bool Feature_Widget { get; set; }

        [JsonIgnore]
        public bool Feature_SimpleDomChoice { get; set; }

        [JsonProperty("page-number-style")]
        public string PageNumberStyle { get; set; }

        [JsonProperty("language-display-names")]
        public Dictionary<string, string> DisplayNames { get; set; }

        // A json string used to limit what the user has access to (such as based on their location)
        // example:
        // {"downloadShell":{"countryCode":"PG"}}
        // which would mean only users in Papua New Guinea can download this book for use as a shell.
        // Currently, there is no UI for this. So, whatever the user enters in manually in meta.json gets passed to the database.
        [JsonProperty("internetLimits")]
        public dynamic InternetLimits { get; set; }

        /// <summary>
        /// Flag whether the user has used the original copyright and license for a derived/translated book.
        /// </summary>
        [JsonProperty("use-original-copyright")]
        public bool UseOriginalCopyright { get; set; }

        /// <summary>
        /// The URL the source of this book was downloaded from before conversion to Bloom source format.
        /// This is set by RoseGarden, but expected to be empty for books that originate in Bloom.
        /// </summary>
        [JsonProperty("imported-book-source-url")]
        public string ImportedBookSourceUrl { get; set; }

        /// <summary>
        /// This is a "perceptual hash" (http://phash.org/) of the image in the first bloom-imageContainer
        /// we find on the first page after any xmatter pages. We use this to suggest which books are
        /// probably related to each other. This allows us to link, for example, books that are translations
        /// of each other.  (bloom-harvester uses https://www.nuget.org/packages/CoenM.ImageSharp.ImageHash
        /// to calculate the phash.)
        /// </summary>
        [JsonProperty("phashOfFirstContentImage")]
        public string PHashOfFirstContentImage { get; set; }

        /// <summary>
        /// UpdateSource provides information to the parse server cloud code so it knows if it is dealing with
        /// a new upload, a re-upload, or something else when the books record changes.
        /// We only started setting this in BloomDesktop in version 4.7.
        /// Prior to that, it was assumed that if the updateSource was not set, the change was coming from BloomDesktop.
        /// </summary>
        private string GetUpdateSource() => $"BloomDesktop {Application.ProductVersion}";
    }

    /// <summary>
    /// Holds Code-Description pairs for Thema subjects.
    /// https://www.editeur.org/files/Thema/1.3/Thema_v1.3.0_en.json
    /// </summary>
    public class SubjectObject
    {
        [JsonProperty("value")]
        public string value { get; set; }

        [JsonProperty("label")]
        public string label { get; set; }
    }

    /// <summary>
    /// This class represents the database Language class (for purposes of generating json)
    /// </summary>
    public class LanguageDescriptor
    {
        [JsonProperty("isoCode")]
        public string LangTag { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("ethnologueCode")]
        public string EthnologueCode { get; set; }
    }

    // In the future, we may add other slots including {TitleLanguage1, TitleLanguage2, CreditsLanguage}
    // We would then rename these to be more specific, e.g. Language1-->InteriorLanguage1
    // See https://docs.google.com/document/d/1uIiog56oYMAa4tTyNag1SgyXPEKgk0EKMxMP1LzPeCc
    public enum LanguageSlot
    {
        Language1,
        Language2,
        Language3,
        MetadataLanguage1,
        MetadataLanguage2,
        SignLanguage
    }
}
