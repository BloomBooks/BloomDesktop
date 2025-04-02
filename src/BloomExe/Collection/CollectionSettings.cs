using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.Publish.BloomLibrary;
using Bloom.Publish.BloomPub;
using Bloom.Utils;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json.Linq;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.WritingSystems;
using Bloom.SubscriptionAndFeatures;

namespace Bloom.Collection
{
    public class NewCollectionSettings : CollectionSettings
    {
        public string PathToSettingsFile;
    }

    /// <summary>
    /// A Collection corresponds to a single folder (with subfolders) on the disk.
    /// In that folder is a file which persists the properties of this class, then a folder for each book
    /// </summary>
    public class CollectionSettings
    {
        private const int kCurrentOneTimeCheckVersionNumber = 1; // bumping this will trigger a new one time check
        public const string kDefaultXmatterName = "Traditional";
        public List<WritingSystem> AllLanguages = new List<WritingSystem>();
        public WritingSystem Language1 => AllLanguages[0];
        public WritingSystem Language2 => AllLanguages[1];
        public WritingSystem Language3 => AllLanguages[2];

        // Email addresses of users authorized to change collection settings if this is a TeamCollection.
        public string[] Administrators;

        public string AdministratorsDisplayString =>
            Administrators == null ? "" : string.Join(", ", Administrators);

        public WritingSystem SignLanguage;

        private const int kDefaultAudioRecordingTrimEndMilliseconds = 40;
        private const int kDefaultBooksOnWebGoal = 200;

        /// <summary>
        /// The branding the user wanted, but not confirmed by current SubscriptionCode, if any.
        /// </summary>
        public string InvalidBranding { get; private set; }

        public string DefaultBookshelf = "";

        public static readonly Dictionary<string, string> CssNumberStylesToCultureOrDigits =
            new Dictionary<string, string>()
            {
                // Initially, Bloom used CSS for page numbering and css counter styles for
                // controlling the script the page numbers are drawn in. For various reasons
                // we then switched to having code keep the page number in data-page-number,
                // so we can't make use of that CSS feature anymore but want to keep the same
                // list and keep working for users of previous versions.
                // In this dictionary, we're pairing css counting styles (the key) with
                // the 10 digits used by the script. As a side benefit, this will allow us to support
                // other number systems, if people request them (so long as they can be represented by just
                // replacing digits).
                // In many cases, as commented, the ten digits were obtained from Microsoft cultures using this expression:
                // new CultureInfo(cultureTag).NumberFormat.NativeDigits
                // Some of the results are empty strings when it doesn't seem they should be (Hebrew, Armenian, Georgian)
                // This reflects that these systems can't be done with simple digit substitution, so we fall
                // back to not converting
                { "Arabic-Indic", "٠١٢٣٤٥٦٧٨٩" }, // from ar-SA, not certain that this is correct one
                //{ "Armenian", ""}, // hy-AM yields 0123456789; not true Armenian, an ancient letter-value system so we can't do it
                //{ "Upper-Armenian", ""}, // hy-AM, probably a variation on Armenian also not permitting digit-substitution
                //{ "Lower-Armenian", ""},//haven't found the culture or list of number for this
                { "Bengali", "০১২৩৪৫৬৭৮৯" }, // from bn-BD
                { "Cambodian", "០១២៣៤៥៦៧៨៩" }, // from km-KH
                { "Khmer", "០១២៣៤៥៦៧៨៩" }, // from km-KH"
                { "Chakma", "𑄶𑄷𑄸𑄹𑄺𑄻𑄼𑄽𑄾𑄿" }, // see https://codepoints.net/search?sc=Cakm
                { "Cjk-Decimal", "〇一二三四五六七八九" }, // haven't found a culture for this
                { "Decimal", "" },
                { "Devanagari", "०१२३४५६७८९" }, // from hi-IN
                //{ "Georgian", ""}, //  ka-GE yields 0123456789; https://en.wikipedia.org/wiki/Georgian_numerals says Georgian is not a simple positional system so we can't do it
                { "Gujarati", "૦૧૨૩૪૫૬૭૮૯" }, // from gu-IN
                { "Gurmukhi", "੦੧੨੩੪੫੬੭੮੯" }, // from pa-IN
                // { "Hebrew", ""}, // he-IL yields 0123456789; not true Hebrew, which uses a non-positional letter-value system, so we can't do it.
                { "Kannada", "೦೧೨೩೪೫೬೭೮೯" }, // from kn-IN
                { "Kayah", "꤀꤁꤂꤃꤄꤅꤆꤇꤈꤉" },
                { "Lao", "໐໑໒໓໔໕໖໗໘໙" }, // from lo-LA
                { "Malayalam", "൦൧൨൩൪൫൬൭൮൯" }, // ml-IN
                { "Mongolian", "᠐᠑᠒᠓᠔᠕᠖᠗᠘᠙" }, // from https://en.wikipedia.org/wiki/Mongolian_numerals; was mn-Mong-MN, which would wrongly be used as a digit string.
                { "Myanmar", "၀၁၂၃၄၅၆၇၈၉" }, // from my-MM
                { "Oriya", "୦୧୨୩୪୫୬୭୮୯" }, // haven't found a culture for this
                { "Persian", "۰۱۲۳۴۵۶۷۸۹" }, // from fa-IR
                { "Shan", "႐႑႒႓႔႕႖႗႘႙" },
                { "Tamil", "௦௧௨௩௪௫௬௭௮௯" }, // from ta-IN"
                { "Telugu", "౦౧౨౩౪౫౬౭౮౯" }, // from te-IN
                { "Thai", "๐๑๒๓๔๕๖๗๘๙" }, // from th-TH
                { "Tibetan", "༠༡༢༣༤༥༦༧༨༩" }, // from bo-CN
            };

        public CollectionSettings()
        {
            Subscription = new Subscription(null);
            //Note: I'm not convinced we actually ever rely on dynamic name lookups anymore?
            //See: https://issues.bloomlibrary.org/youtrack/issue/BL-7832
            Func<string> getTagOfDefaultLanguageForNaming = () => Language2.Tag;
            AllLanguages.Add(new WritingSystem(getTagOfDefaultLanguageForNaming));
            AllLanguages.Add(new WritingSystem(getTagOfDefaultLanguageForNaming));
            AllLanguages.Add(new WritingSystem(getTagOfDefaultLanguageForNaming));

            SignLanguage = new WritingSystem(getTagOfDefaultLanguageForNaming);
            PageNumberStyle = "Decimal";
            XMatterPackName = kDefaultXmatterName;
            Language2Tag = "en";
            AllowNewBooks = true;
            CollectionName = "dummy collection";
            AudioRecordingMode = TalkingBookApi.AudioRecordingMode.Sentence;
            AudioRecordingTrimEndMilliseconds = kDefaultAudioRecordingTrimEndMilliseconds;
            BooksOnWebGoal = kDefaultBooksOnWebGoal;
        }

        public static void CreateNewCollection(NewCollectionSettings collectionInfo)
        {
            // For some reason this constructor is used to create new collections. But I think a static method is much clearer.
            new CollectionSettings(collectionInfo);
        }

        public CollectionSettings(NewCollectionSettings collectionInfo)
            : this(collectionInfo.PathToSettingsFile)
        {
            AllowNewBooks = collectionInfo.AllowNewBooks;

            AllLanguages = new List<WritingSystem>(collectionInfo.AllLanguages);
            Language2.FontName = Language3.FontName = WritingSystem.GetDefaultFontName();
            SignLanguage = collectionInfo.SignLanguage;

            Country = collectionInfo.Country;
            Province = collectionInfo.Province;
            District = collectionInfo.District;
            IsSourceCollection = collectionInfo.IsSourceCollection;
            XMatterPackName = collectionInfo.XMatterPackName;
            PageNumberStyle = collectionInfo.PageNumberStyle;
            Subscription = collectionInfo.Subscription;
            AudioRecordingMode = collectionInfo.AudioRecordingMode;
            AudioRecordingTrimEndMilliseconds = collectionInfo.AudioRecordingTrimEndMilliseconds;
            BooksOnWebGoal = collectionInfo.BooksOnWebGoal;

            Save();
        }

        /// <summary>
        /// can be used whether the Collection exists already, or not
        /// </summary>
        public CollectionSettings(
            string desiredOrExistingSettingsFilePath,
            bool editingABlorgBook = false
        )
            : this()
        {
            EditingABlorgBook = editingABlorgBook;
            SettingsFilePath = desiredOrExistingSettingsFilePath;
            CollectionName = Path.GetFileNameWithoutExtension(desiredOrExistingSettingsFilePath);
            var collectionDirectory = Path.GetDirectoryName(desiredOrExistingSettingsFilePath);
            var parentDirectoryPath = Path.GetDirectoryName(collectionDirectory);

            if (RobustFile.Exists(desiredOrExistingSettingsFilePath))
            {
                DoDefenderFolderProtectionCheck();
                Load();
            }
            else
            {
                if (!Directory.Exists(parentDirectoryPath))
                    Directory.CreateDirectory(parentDirectoryPath);

                if (!Directory.Exists(collectionDirectory))
                    Directory.CreateDirectory(collectionDirectory);

                DoDefenderFolderProtectionCheck();
                Save();
            }
        }

        private void DoDefenderFolderProtectionCheck()
        {
            // We check for a Windows Defender "Controlled Access" problem when we start Bloom,
            // but the user may have moved their startup collection to a "safe" place and now be opening a different
            // collection in a "controlled" place. Test again with this settings file path.
            // 'FolderPath' is the directory part of 'SettingsFilePath'.
            if (!DefenderFolderProtectionCheck.CanWriteToDirectory(FolderPath))
            {
                Environment.Exit(-1);
            }
        }

        // The initializer provides a default for collections (like in unit tests)
        // that are not loaded from  file, but normally it is saved and restored
        // in the settings file.
        public string CollectionId = Guid.NewGuid().ToString();

        private string DefaultLanguageForNamingLanguages()
        {
            return Language2.Tag ?? "en";
        }

        #region Persisted properties

        //these are virtual for the sake of the unit test mock framework
        public virtual string Language1Tag
        {
            get { return Language1.Tag; }
            set { Language1.ChangeTag(value); }
        }
        public virtual string Language2Tag
        {
            get { return Language2.Tag; }
            set { Language2.ChangeTag(value); }
        }
        public virtual string Language3Tag
        {
            get { return Language3.Tag; }
            set { Language3.ChangeTag(value); }
        }
        public virtual string SignLanguageTag
        {
            get { return SignLanguage.Tag; }
            set { SignLanguage.ChangeTag(value); }
        }

        /// <summary>
        /// Intended for making shell books and templates, not vernacular
        /// </summary>
        [Obsolete("We never distinguish source collections anymore, so this is obsolete.")]
        public virtual bool IsSourceCollection { get; set; }

        /// <summary>
        /// Get the name of the language whose tag is the first argument, if possible in the language specified by the second.
        /// If the language tag is unknown, return it unchanged.
        /// </summary>
        public string GetLanguageName(string tag, string inLanguage)
        {
            // Use the WritingSystem name based on Ethnologue (or customized) in preference to the
            // IeftLanguageTag name based on older ISO 639 data.  See BL-12992.
            var language = AllLanguages.Find(x => x.Tag == tag);
            if (language != null)
                return language.Name;
            if (tag == SignLanguageTag)
                return SignLanguage.Name;
            // Note: the inLanguage parameter is often ignored by IetfLanguageTag.GetLocalizedLanguageName().
            return IetfLanguageTag.GetLocalizedLanguageName(tag, inLanguage);
        }
        #endregion

        /// ------------------------------------------------------------------------------------
        public void Save()
        {
            Logger.WriteEvent("Saving Collection Settings");

            XElement xml = new XElement("Collection");
            xml.Add(new XAttribute("version", "0.2"));
            xml.Add(new XElement("CollectionId", CollectionId));
            Language1.SaveToXElementLegacy(xml, 1);
            Language2.SaveToXElementLegacy(xml, 2);
            Language3.SaveToXElementLegacy(xml, 3);
            var languagesElement = new XElement("Languages");
            int wsNum = 0;
            foreach (var langWs in AllLanguages)
            {
                var language = new XElement("Language");
                switch (++wsNum)
                {
                    case 1:
                        language.Add(new XAttribute("L1", "true"));
                        break;
                    case 2:
                        // L2 may essentially duplicate L1, but that's okay: we've always allowed this.
                        language.Add(new XAttribute("L2", "true"));
                        break;
                    case 3:
                        if (String.IsNullOrEmpty(Language3Tag))
                            continue; // Don't bother writing empty content.
                        // L3 may essentially duplicate L1 or L2, but that's okay: we've always allowed this.
                        language.Add(new XAttribute("L3", "true"));
                        break;
                }
                langWs.SaveToXElement(language);
                languagesElement.Add(language);
            }
            xml.Add(languagesElement);
            SignLanguage.SaveToXElement(xml, isSignLanguage: true);
            xml.Add(new XElement("OneTimeCheckVersionNumber", OneTimeCheckVersionNumber));
            xml.Add(new XElement("IsSourceCollection", IsSourceCollection.ToString()));
            xml.Add(new XElement("XMatterPack", XMatterPackName));
            xml.Add(new XElement("PageNumberStyle", PageNumberStyle));

            // Versions before Bloom 6.1 read this in. Starting with Bloom 6.1, we ignore this and just parse the SubscriptionCode.
            // For now we are still saving this for backwards compatibility.
            xml.Add(new XElement("BrandingProjectName", Subscription.BrandingKey));
            xml.Add(new XElement("SubscriptionCode", Subscription.Code));
            xml.Add(new XElement("Country", Country));
            xml.Add(new XElement("Province", Province));
            xml.Add(new XElement("District", District));
            xml.Add(new XElement("AllowNewBooks", AllowNewBooks.ToString()));
            xml.Add(new XElement("AudioRecordingMode", AudioRecordingMode.ToString()));
            xml.Add(
                new XElement("AudioRecordingTrimEndMilliseconds", AudioRecordingTrimEndMilliseconds)
            );
            xml.Add(new XElement("BooksOnWebGoal", BooksOnWebGoal));
            if (Administrators != null && Administrators.Length > 0)
                xml.Add(new XElement("Administrators", string.Join(",", Administrators)));
            if (!string.IsNullOrEmpty(DefaultBookshelf))
            {
                xml.Add(new XElement("DefaultBookTags", "bookshelf:" + DefaultBookshelf));
            }
            xml.Add(BulkPublishBloomPubSettings.ToXElement());
            RobustIO.SaveXElement(xml, SettingsFilePath);

            // Color palette settings are stored in a separate Json file
            SaveColorPalettesToJsonFile();
        }

        internal void SaveColorPalettesToJsonFile()
        {
            // If we're in a Team Collection, the file checker will try to sync up with any changes
            // that have occurred remotely.
            var jsonFilePath = Path.Combine(FolderPath, "colorPalettes.json");
            SaveColorPalettesToJsonFile(ColorPalettes, jsonFilePath);
        }

        internal static void SaveColorPalettesToJsonFile(
            Dictionary<string, string> colorPalettes,
            string jsonFilePath
        )
        {
            var jsonString = new StringBuilder();
            jsonString.AppendLine("{");
            foreach (var key in colorPalettes.Keys)
                jsonString.AppendLine($"\"{key}\":\"{colorPalettes[key]}\",");
            jsonString.AppendLine("}");
            RobustFile.WriteAllText(jsonFilePath, jsonString.ToString());
        }

        public string GetCollectionStylesCss(bool omitDirection)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                "/* *** DO NOT EDIT! ***   These styles are controlled by the Settings dialog box in Bloom. */"
            );
            sb.AppendLine(
                "/* They may be over-ridden by rules in customCollectionStyles.css, appearance.css or customBookStyles[2].css */"
            );
            // Note: css pseudo elements  cannot have a @lang attribute. So this is needed to show page numbers in scripts not
            // covered by Andika.  The important information needed is the name of the font to use for the page numbers, the
            // directionality of the script, and possibly how to break lines in the script.  The line-height for page numbers
            // is set in basePage.css using an appearance system variable, so we don't need to set it here.  Indeed, setting
            // it here breaks the page numbers in some cases since defaultLangStyles.css is loaded after basePage.css.  See
            // BL-13699.  (line-height really matters only for multi-line page numbers, which are rare to say the least.)
            WritingSystem.AddSelectorCssRule(
                sb,
                ".numberedPage::after",
                Language1.FontName,
                Language1.IsRightToLeft,
                -1, // Prevents a line-height rule from being created.
                Language1.BreaksLinesOnlyAtSpaces,
                omitDirection
            );
            Language1.AddSelectorCssRule(sb, omitDirection);
            if (Language2Tag != Language1Tag)
                Language2.AddSelectorCssRule(sb, omitDirection);
            if (
                !string.IsNullOrEmpty(Language3Tag)
                && Language3Tag != Language1Tag
                && Language3Tag != Language2Tag
            )
            {
                Language3.AddSelectorCssRule(sb, omitDirection);
            }
            return sb.ToString();
        }

        public static string CollectionIdFromCollectionFolder(string collectionFolder)
        {
            try
            {
                var settingsFilePath = Path.Combine(
                    collectionFolder,
                    Path.ChangeExtension(Path.GetFileName(collectionFolder), "bloomCollection")
                );
                if (!RobustFile.Exists(settingsFilePath))
                {
                    // When we're joining a TC, we extract settings in to a temp folder whose name does not
                    // match the settings file.
                    var collections = Directory
                        .EnumerateFiles(collectionFolder, "*.bloomCollection")
                        .ToList();
                    if (collections.Count >= 1)
                    {
                        // Hopefully this repairs things.
                        settingsFilePath = collections[0];
                    }
                    else
                    {
                        return "";
                    }
                }

                var settingsContent = RobustFile.ReadAllText(settingsFilePath, Encoding.UTF8);
                var xml = XElement.Parse(settingsContent);
                return ReadString(xml, "CollectionId", "");
            }
            catch (Exception ex)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);
                return "";
            }
        }

        /// ------------------------------------------------------------------------------------
        public void Load()
        {
            try
            {
                // Previously was SIL.IO.RobustIO.LoadXElement(SettingsFilePath). However, we had problems with this
                // using some non-roman collection names...specifically, one involving the Northern Pashto
                // localization of 'books' (کتابونه)...see BL-5416. It seems that somewhere in the
                // implementation of Linq.XElement.Load() the path is converted to a URL and then back
                // to a path and something changes in that process so that a valid path passed to Load()
                // raises an invalid path exception. Reading the file directly and then parsing the string
                // works around this problem.
                var settingsContent = RobustFile.ReadAllText(SettingsFilePath, Encoding.UTF8);
                var nameMigrations = new[]
                {
                    new[] { "LanguageName", "Language1Name" }, // but NOT SignLanguageName -> SignLanguage1Name !!
                    new[] { "SignLanguage1Name", "SignLanguageName" }, // un-migrate SignLanguageName
                    new[] { "IsShellLibrary", "IsSourceCollection" },
                    new[] { "National1Iso639Code", "Language2Iso639Code" },
                    new[] { "National2Iso639Code", "Language3Iso639Code" },
                    new[] { "IsShellMakingProject", "IsSourceCollection" },
                    new[] { "Local Community", "Local-Community" } // migrate for 4.4
                };

                foreach (var fromTo in nameMigrations)
                {
                    settingsContent = settingsContent.Replace(fromTo[0], fromTo[1]);
                }

                var xml = XElement.Parse(settingsContent);
                // The default if we don't find one is the arbitrary ID generated when we initialized
                // the variable (at its declaration).
                CollectionId = ReadString(xml, "CollectionId", CollectionId);

                var languageList = xml.Descendants("Languages").FirstOrDefault();
                if (languageList != null)
                {
                    LoadLanguagesList(languageList);
                }
                else
                {
                    Language1.ReadFromXmlLegacy(xml, "en", 1);
                    Language2.ReadFromXmlLegacy(xml, "self", 2);
                    Language3.ReadFromXmlLegacy(xml, Language2.Tag, 3);
                }
                SignLanguage.ReadFromXml(xml, Language2.Tag, isSignLanguage: true);

                XMatterPackName = ReadString(xml, "XMatterPack", "Factory");

                var style = ReadString(xml, "PageNumberStyle", "Decimal");

                //for historical (and maybe future?) reasons, we collect the page number style as one of the
                //CSS counter number styles
                PageNumberStyle = CssNumberStylesToCultureOrDigits.Keys.Contains(style)
                    ? style
                    : "Decimal";
                OneTimeCheckVersionNumber = ReadInteger(xml, "OneTimeCheckVersionNumber", 0);

                var pathToFileAboutABlorgBookWeHaveDownloadedForEditing = Path.Combine(
                    FolderPath,
                    BloomLibraryPublishModel.kNameOfFileAboutABlorgBookWeHaveDownloadedForEditing
                );
                var bloomProblemBookJsonPath = Path.Combine(
                    FolderPath,
                    ProblemReportApi.kProblemBookJsonName
                );

                // This may be set during construction if we're actually doing the download.
                // In later runs, we know because the first run leaves a special json file.
                EditingABlorgBook |=
                    RobustFile.Exists(pathToFileAboutABlorgBookWeHaveDownloadedForEditing)
                    // We also treat harvester runs as if we are editing a blorg book.
                    // The main reason for this is to ensure that redacted subscriptions are handled correctly.
                    // In other words, when running harvester, subscription code will be something like SIL-LEAD-***-***,
                    // but we still want to process the book using the original branding.
                    || Program.RunningHarvesterMode;

                // There are cases where we want to keep the branding, even if it's expired.
                // 1) they got this book using blorg's "download for editing" feature which is restricted to
                // user logins that are marked as editors of the collection. We want to allow them to re-upload it with fixes
                // even if the subscription has expired.
                // 2) this is a developer looking into a Bloom Problem Report.
                var downloadInfoPath = RobustFile.Exists(
                    pathToFileAboutABlorgBookWeHaveDownloadedForEditing
                )
                    ? pathToFileAboutABlorgBookWeHaveDownloadedForEditing
                    : RobustFile.Exists(bloomProblemBookJsonPath)
                        ? bloomProblemBookJsonPath
                        : null;
                if (downloadInfoPath != null)
                {
                    IgnoreExpiration = true;
                    var editSettings = JObject.Parse(RobustFile.ReadAllText(downloadInfoPath));

                    // BloomProblemReport.json's have an issueID that will be better for us to use as a collection name
                    if (editSettings.TryGetValue("issueID", out JToken issueID))
                    {
                        CollectionName = issueID.Value<string>();
                    }
                }

                Subscription = Subscription.FromCollectionSettingsInfo(
                    ReadString(xml, "SubscriptionCode", null),
                    ReadString(xml, "BrandingProjectName", "Default"),
                    EditingABlorgBook
                );

                Country = ReadString(xml, "Country", "");
                Province = ReadString(xml, "Province", "");
                District = ReadString(xml, "District", "");
                AllowNewBooks = ReadBoolean(xml, "AllowNewBooks", true);
                IsSourceCollection = ReadBoolean(xml, "IsSourceCollection", false);

                string audioRecordingModeStr = ReadString(xml, "AudioRecordingMode", "Unknown");
                TalkingBookApi.AudioRecordingMode parsedAudioRecordingMode;
                if (!Enum.TryParse(audioRecordingModeStr, out parsedAudioRecordingMode))
                {
                    parsedAudioRecordingMode = TalkingBookApi.AudioRecordingMode.Unknown;
                }
                AudioRecordingMode = parsedAudioRecordingMode;
                AudioRecordingTrimEndMilliseconds = ReadInteger(
                    xml,
                    "AudioRecordingTrimEndMilliseconds",
                    kDefaultAudioRecordingTrimEndMilliseconds
                );
                BooksOnWebGoal = ReadInteger(xml, "BooksOnWebGoal", kDefaultBooksOnWebGoal);
                Administrators = ReadString(xml, "Administrators", "")
                    .Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                var defaultTags = ReadString(xml, "DefaultBookTags", "").Split(',');
                var defaultBookshelfTag = defaultTags
                    .Where(t => t.StartsWith("bookshelf:"))
                    .FirstOrDefault();
                DefaultBookshelf =
                    (
                        defaultBookshelfTag != null
                        && Subscription.Tier == SubscriptionTier.Enterprise
                    )
                        ? defaultBookshelfTag.Substring("bookshelf:".Length)
                        : "";

                var bulkPublishSettingsFromXml = BulkBloomPubPublishSettings.LoadFromXElement(xml);
                if (bulkPublishSettingsFromXml != null)
                {
                    BulkPublishBloomPubSettings = bulkPublishSettingsFromXml;
                }

                LoadDictionary(xml, "Palette", ColorPalettes);
            }
            catch (Exception originalError)
            {
                string settingsContents;
                try
                {
                    settingsContents = RobustFile.ReadAllText(SettingsFilePath);
                }
                catch (Exception error)
                {
                    settingsContents = error.Message;
                }
                Logger.WriteEvent("Contents of " + SettingsFilePath + ": /r/n" + settingsContents);

                // We used to notify the user of a problem here.
                // But now we decided it is better to catch at a higher level, at OpenProjectWindow(), else we have two different
                // error UI dialogs for the same problem. See BL-9916.

                throw new FileException(SettingsFilePath, originalError);
            }

            try
            {
                string oldcustomCollectionStylesPath = FolderPath.CombineForPath("collection.css");
                if (RobustFile.Exists(oldcustomCollectionStylesPath))
                {
                    string newcustomCollectionStylesPath = FolderPath.CombineForPath(
                        "customCollectionStyles.css"
                    );

                    RobustFile.Move(oldcustomCollectionStylesPath, newcustomCollectionStylesPath);
                }
            }
            catch (Exception)
            {
                //ah well, we tried, no big deal, only a couple of beta testers used this old name
            }

            // Check if we need to do a one time check (perhaps migrate to a new Settings value)
            if (OneTimeCheckVersionNumber < kCurrentOneTimeCheckVersionNumber)
            {
                DoOneTimeCheck();
            }

            SetAnalyticsProperties();
        }

        private void LoadLanguagesList(XElement languageList)
        {
            int wsNum = 0;
            foreach (var language in languageList.Descendants("Language"))
            {
                if (wsNum == 0)
                    Language1.ReadFromXml(language, "en");
                else if (wsNum == 1)
                    Language2.ReadFromXml(language, "self");
                else if (wsNum == 2 && language.Attribute("L3")?.Value == "true")
                    Language3.ReadFromXml(language, Language2.Tag);
                else
                {
                    var writingSystem = new WritingSystem(DefaultLanguageForNamingLanguages);
                    writingSystem.ReadFromXml(language, Language2.Tag);
                    AllLanguages.Add(writingSystem);
                }
                ++wsNum;
            }
            if (Language3.Name == null)
            {
                // If Language3 is not defined, set some default values that would otherwise be null.
                Language3.SetName("", false);
                Language3.Tag = "";
                Language3.FontName = WritingSystem.GetDefaultFontName();
            }
        }

        /// <summary>
        /// Update the collection settings if necessary with the language tag and name.
        /// But if okToMakeChange is false, don't make the change, just return whether it would be necessary.
        /// </summary>
        /// <returns>true if the settings change, false if the tag and name are already set that way</returns>
        public bool UpdateOrCreateCollectionLanguage(
            string tag,
            string name,
            bool okToMakeChange = true
        )
        {
            var language = AllLanguages.Find(x => x.Tag == tag);
            if (language != null)
            {
                if (language.Name == name)
                    return false;
                if (okToMakeChange)
                    language.SetName(name, true);
            }
            else if (okToMakeChange)
            {
                var writingSystem = new WritingSystem(DefaultLanguageForNamingLanguages);
                writingSystem.Tag = tag;
                writingSystem.SetName(name, true);
                writingSystem.FontName = WritingSystem.GetDefaultFontName();
                AllLanguages.Add(writingSystem);
            }
            return true;
        }

        // when you click "Download into Bloom for Editing" on blorg, we download the book and go into
        // a mode that restricts you to this one book in your collection but also allows you to retain
        // whatever you branding was at the time, even it has expired.
        public bool EditingABlorgBook;
        public bool IgnoreExpiration;

        private void DoOneTimeCheck()
        {
            // We had a migration from Andika to Andika New Basic for a long time, but it's no longer useful.
            // (See https://issues.bloomlibrary.org/youtrack/issue/BL-7868.)
            // If we ever have to do another one of these, we should call a method based on OneTimeCheckVersionNumber.
            OneTimeCheckVersionNumber = kCurrentOneTimeCheckVersionNumber;
            Save(); // save updated settings
        }

        internal static bool ReadBoolean(XElement xml, string id, bool defaultValue)
        {
            string s = ReadString(xml, id, defaultValue.ToString());
            bool b;
            bool.TryParse(s, out b);
            return b;
        }

        private int ReadInteger(XElement xml, string id, int defaultValue)
        {
            var s = ReadString(xml, id, defaultValue.ToString(CultureInfo.InvariantCulture));
            int i;
            int.TryParse(s, out i);
            return i;
        }

        public static string ReadString(XElement document, string id, string defaultValue)
        {
            var nodes = document.Descendants(id);
            if (nodes != null && nodes.Count() > 0)
                return nodes.First().Value;
            else
            {
                return defaultValue;
            }
        }

        internal void LoadDictionary(XElement document, string tag, Dictionary<string, string> dict)
        {
            dict.Clear();

            // The color palettes are now stored in a separate JSON file, so we try to load them
            // from the JSON file before looking at the XML data.  (The XML data is not removed
            // until the whole collection settings are saved.)
            if (tag == "Palette")
            {
                var path = Path.Combine(FolderPath, "colorPalettes.json");
                if (LoadColorPalettesFromJsonFile(dict, path))
                    return;
            }
            var elements = document.Descendants(tag);
            if (elements != null)
            {
                foreach (XElement element in elements)
                    dict[element.Attribute("id").Value] = element.Value;
            }
        }

        internal static bool LoadColorPalettesFromJsonFile(
            Dictionary<string, string> dict,
            string path
        )
        {
            if (RobustFile.Exists(path))
            {
                var jsonString = RobustFile.ReadAllText(path);
                try
                {
                    var json = JObject.Parse(jsonString);
                    if (json != null)
                    {
                        foreach (var property in json.Properties())
                            dict[property.Name] = property.Value.ToString();
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.WriteEvent($"Error loading color palettes from {path}: " + ex.Message);
                }
            }
            return false;
        }

        public virtual string CollectionName { get; protected set; }

        [XmlIgnore]
        public string FolderPath
        {
            get { return Path.GetDirectoryName(SettingsFilePath); }
        }

        [XmlIgnore]
        public string SettingsFilePath { get; set; }

        private string _xmatterNameInCollectionSettingsFile;

        /// <summary>
        /// for the "Factory-XMatter.htm", this would be named "Factory"
        /// </summary>
        public virtual string XMatterPackName
        {
            // xmatter specified by the branding always wins
            // enhance: maybe we should store this.. but I don't think this is called often
            get =>
                GetXMatterPackNameSpecifiedByBrandingOrNull()
                ?? this._xmatterNameInCollectionSettingsFile;
            set => this._xmatterNameInCollectionSettingsFile = value;
        }

        public string GetXMatterPackNameSpecifiedByBrandingOrNull()
        {
            if (!string.IsNullOrEmpty(Subscription.BrandingKey))
            {
                var xmatterToUse = BrandingSettings
                    .GetSettingsOrNull(Subscription.BrandingKey)
                    ?.GetXmatterToUse();
                if (xmatterToUse != null)
                {
                    return xmatterToUse;
                }
            }
            return null;
        }

        public virtual string Country { get; set; }
        public virtual string Province { get; set; }
        public virtual string District { get; set; }

        public string VernacularCollectionNamePhrase
        {
            get
            {
                //review: in June 2013, I made it just use the collectionName regardless of the type. I wish I'd make a comment with the previous approach
                //explaining *why* we would want to just say, for example, "Foobar Books". Probably for some good reason.
                //But it left us with the weird situation of being able to change the collection name in the settings, and have that only affect the  title
                //bar of the window (and the on-disk name). People wanted to change to a language name they want to see. (We'll probably have to do something
                //to enable that anyhow because it shows up elsewhere, but this is a step).
                return CollectionName;
                //var fmt = L10NSharp.LocalizationManager.GetString("CollectionTab.Vernacular Collection Heading", "{0} Books", "The {0} is where we fill in the name of the Vernacular");
                //return string.Format(fmt, Language1Name);
            }
        }

        public string PageNumberStyle { get; set; }

        internal IEnumerable<string> GetAllLanguageTags()
        {
            var langTags = new List<string>();
            langTags.Add(Language1.Tag);
            if (Language2.Tag != Language1.Tag)
                langTags.Add(Language2.Tag);
            if (!String.IsNullOrEmpty(Language3.Tag) && !langTags.Any(tag => tag == Language3.Tag))
                langTags.Add(Language3.Tag);
            return langTags;
        }

        public string GetBrandingFlavor()
        {
            BrandingSettings.ParseSubscriptionDescriptor(
                Subscription.Descriptor,
                out var baseKey,
                out var flavor,
                out var subUnitName
            );
            return flavor;
        }

        public string GetBrandingFolderName()
        {
            BrandingSettings.ParseSubscriptionDescriptor(
                Subscription.Descriptor,
                out var folderName,
                out var flavor,
                out var subUnitName
            );
            return folderName;
        }

        public Subscription Subscription;

        public int OneTimeCheckVersionNumber { get; set; }

        public bool AllowNewBooks { get; set; }

        public TalkingBookApi.AudioRecordingMode AudioRecordingMode { get; set; }

        public int AudioRecordingTrimEndMilliseconds { get; set; }

        public int BooksOnWebGoal { get; set; }

        public BulkBloomPubPublishSettings BulkPublishBloomPubSettings =
            new BulkBloomPubPublishSettings
            {
                makeBookshelfFile = true,
                bookshelfColor = Palette.kBloomLightBlueHex,
                makeBloomBundle = true,
                distributionTag = ""
            };

        public bool AllowDeleteBooks
        {
            get { return AllowNewBooks; } //at the moment, we're combining these two concepts; we can split them if a good reason to comes along
        }

        public static string GetPathForNewSettings(
            string parentFolderPath,
            string newCollectionName
        )
        {
            return parentFolderPath.CombineForPath(
                newCollectionName,
                newCollectionName + ".bloomCollection"
            );
        }

        public static string RenameCollection(string fromDirectory, string toDirectory)
        {
            if (!Directory.Exists(fromDirectory))
            {
                throw new ApplicationException(
                    "Bloom could not complete the renaming of the collection, because there isn't a directory with the source name anymore: "
                        + fromDirectory
                );
            }

            if (Directory.Exists(toDirectory)) //there's already a folder taking this name
            {
                throw new ApplicationException(
                    "Bloom could not complete the renaming of the collection, because there is already a directory with the new name: "
                        + toDirectory
                );
            }

            //this is just a sanity check, it will throw if the existing directory doesn't have a collection
            FindSettingsFileInFolder(fromDirectory);

            //first rename the directory, as that is the part more likely to fail (because *any* locked file in there will cause a failure)
            SIL.IO.RobustIO.MoveDirectory(fromDirectory, toDirectory);
            string collectionSettingsPath;
            try
            {
                collectionSettingsPath = FindSettingsFileInFolder(toDirectory);
            }
            catch (Exception)
            {
                throw;
            }

            try
            {
                //we now make a default name based on the name of the directory
                string destinationPath = Path.Combine(
                    toDirectory,
                    Path.GetFileName(toDirectory) + ".bloomCollection"
                );
                if (!RobustFile.Exists(destinationPath))
                    RobustFile.Move(collectionSettingsPath, destinationPath);

                return destinationPath;
            }
            catch (Exception error)
            {
                //change the directory name back, so the rename isn't half-done.
                SIL.IO.RobustIO.MoveDirectory(toDirectory, fromDirectory);
                throw new ApplicationException(
                    string.Format(
                        "Could change the folder name, but not the collection file name",
                        fromDirectory,
                        toDirectory
                    ),
                    error
                );
            }
        }

        public static string FindSettingsFileInFolder(string folderPath)
        {
            try
            {
                return Directory.GetFiles(folderPath, "*.bloomCollection").First();
            }
            catch (Exception)
            {
                throw new ApplicationException(
                    string.Format(
                        "Bloom expected to find a .bloomCollectionFile in {0}, but there isn't one.",
                        folderPath
                    )
                );
            }
        }

        /// <summary>
        /// The user settings can define a number system. This gives the digits, 0..9 of the selected system.
        /// </summary>
        public string CharactersForDigitsForPageNumbers
        {
            get
            {
                string info;
                if (CssNumberStylesToCultureOrDigits.TryGetValue(PageNumberStyle, out info))
                {
                    // normal info.length gives 20 for chakma's 10 characters... I gather because it is converted to utf 16  and then
                    // those bytes are counted? Here's all the info:
                    // "In short, the length of a string is actually a ridiculously complex question and calculating it can take a lot of CPU time as well as data tables."
                    // https://stackoverflow.com/questions/26975736/why-is-the-length-of-this-string-longer-than-the-number-of-characters-in-it
                    var infoOnDigitsCharacters = new StringInfo(info);
                    if (infoOnDigitsCharacters.LengthInTextElements == 10) // string of digits
                        return info; //we've just listed the digits out, no need to look up a culture

                    if (infoOnDigitsCharacters.LengthInTextElements == 5) // Microsoft culture code
                    {
                        try
                        {
                            var digits = new CultureInfo(info).NumberFormat.NativeDigits;
                            Debug.Assert(digits.Length == 10);
                            var joined = string.Join("", digits);
                            Debug.Assert(joined.Length == 10);
                            return joined;
                        }
                        catch (CultureNotFoundException)
                        {
                            // fall through to default return value
                        }
                        catch (Exception)
                        {
                            //there's no scenario
                            //where this is worth stopping people in their tracks. I just want a
                            //problem report saying "Hey page numbers don't look right on this machine".
                        }
                    }
                }
                //Missing or malformed value for this identifier.
                return "0123456789";
            }
        }

        internal readonly Dictionary<string, string> ColorPalettes =
            new Dictionary<string, string>();

        public string GetColorPaletteAsJson(string paletteTag)
        {
            var colorElementList = new List<string>();
            if (
                ColorPalettes.TryGetValue(paletteTag, out string savedPalette)
                && !String.IsNullOrWhiteSpace(savedPalette)
            )
            {
                var paletteColors = savedPalette.Split(' ');
                foreach (var savedColor in paletteColors)
                {
                    double opacity = 1;
                    var pieces = savedColor.Split('/');
                    if (pieces.Length > 1)
                        opacity = double.Parse(pieces[1], NumberFormatInfo.InvariantInfo);
                    if (opacity > 1)
                        opacity = opacity / 100; // some old values stored range of 0-100 instead of 0-1
                    var colors = pieces[0].Split('-');
                    // Opacity needs to be formatted in an invariant manner to keep the file format stable.
                    var colorElement =
                        $"{{\"colors\":[\"{string.Join("\",\"", colors)}\"],\"opacity\":{opacity.ToString("0.###", CultureInfo.InvariantCulture)}}}";
                    colorElementList.Add(colorElement);
                }
            }
            var jsonString = "[" + string.Join(",", colorElementList) + "]";
            return jsonString;
        }

        public void AddColorToPalette(string paletteTag, string colorString)
        {
            dynamic colorObject;
            try
            {
                colorObject = DynamicJson.Parse(colorString);
            }
            catch (Exception)
            {
                Logger.WriteEvent(
                    $"CollectionSettings.AddColorToPalette(\"{paletteTag}\", \"{colorString}\") failed to parse the colorString"
                );
                colorObject = null;
            }
            if (colorObject != null)
            {
                try
                {
                    var colors = colorObject.colors;
                    var opacity = (double)colorObject.opacity;
                    var list = new List<string>(colors.Count);
                    for (int i = 0; i < colors.Count; ++i)
                        list.Add(colors[i]);
                    var colorToSave = string.Join("-", list);
                    if (opacity != 1)
                        // ToString() here needs to be invariant, since this is a saved file format.
                        colorToSave =
                            $"{colorToSave}/{opacity.ToString("0.###", CultureInfo.InvariantCulture)}";
                    if (!ColorPalettes.TryGetValue(paletteTag, out string savedPalette))
                        savedPalette = "";
                    var paletteColors = savedPalette.Split(' ');
                    if (paletteColors.Contains(colorToSave))
                        return;
                    savedPalette = savedPalette + " " + colorToSave;
                    ColorPalettes[paletteTag] = savedPalette.Trim();
                    SaveColorPalettesToJsonFile();
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // what happens if content isn't set as expected
                }
            }
        }

        /// <summary>
        /// The collection settings point to object which might not exist. For example, the xmatter pack might not exist.
        /// So this should be called as soon as it is OK to show some UI. It will find any dependencies it can't meet,
        /// revert them to defaults, and notify the user.
        /// </summary>
        public void CheckAndFixDependencies(BloomFileLocator bloomFileLocator)
        {
            var errorTemplate = LocalizationManager.GetString(
                "Errors.XMatterNotFound",
                "This Collection called for Front/Back Matter pack named '{0}', but this version of Bloom does not have it, and Bloom could not find it on this computer. The collection has been changed to use the default Front/Back Matter pages."
            );
            var errorMessage = String.Format(errorTemplate, XMatterPackName);
            XMatterPackName = XMatterHelper.MigrateXMatterName(XMatterPackName);
            if (
                string.IsNullOrEmpty(
                    XMatterHelper.GetXMatterDirectory(
                        XMatterPackName,
                        bloomFileLocator,
                        errorMessage,
                        false
                    )
                )
            )
            {
                this.XMatterPackName = kDefaultXmatterName;
                Save();
            }
        }

        /// <summary>
        /// Set some properties related to this collection, which will go out with every subsequent event
        /// </summary>
        public void SetAnalyticsProperties()
        {
            if (!Analytics.AllowTracking)
            {
                return; //e.g. in unit tests
            }
            // this is ambiguous with what country we are *in*. I'm preserving it for now so we don't have a discontinuity in the analytics database,
            // but then adding an unambiguous duplicate with CollectionCountry
            Analytics.SetApplicationProperty("Country", Country);
            Analytics.SetApplicationProperty("CollectionCountry", Country);
            Analytics.SetApplicationProperty("Language1Iso639Code", Language1Tag);
            Analytics.SetApplicationProperty("Language2Iso639Code", Language2Tag);
            Analytics.SetApplicationProperty("Language3Iso639Code", Language3Tag ?? "---");
            Analytics.SetApplicationProperty("SignLanguageIso639Code", SignLanguageTag ?? "---");
            Analytics.SetApplicationProperty("Language1Iso639Name", Language1.Name);
            Analytics.SetApplicationProperty("BrandingProjectName", Subscription.Descriptor);
        }

        public string GetWritingSystemDisplayForUICss()
        {
            /*
             // I wanted to limit this with the language tag, but after 2 hours I gave up simply getting the current language tag
            // to the decodable reader code. What a mess that code is. So now I'm taking advantage of the fact that there is only
            // one language used in our current tools
            // return $"[lang='{Tag}']{{font-size: {(BaseUIFontSizeInPoints == 0 ? 10 : BaseUIFontSizeInPoints)}pt;}}";
            var css = "";
            foreach (var writingSystem in LanguagesZeroBased)
            {
                css += writingSystem.GetWritingSystemDisplayForUICss();
            }

            return css;
            */
            return $".lang1InATool{{font-size: {(Language1.BaseUIFontSizeInPoints == 0 ? 10 : Language1.BaseUIFontSizeInPoints)}pt;}}";
        }

        /// <summary>
        /// Give the string the user expects to see as the name of a specified language.
        /// This routine uses the user-specified name for the main project language.
        /// For the other two project languages, it explicitly uses the appropriate collection settings
        /// name for that language, which the user also set.
        /// If the user hasn't set a name for the given language, this will find a fairly readable name
        /// for the languages Palaso knows about (probably the autonym) and fall back to the tag itself
        /// if it can't find a name.
        /// BL-8174 But in case the tag includes Script/Region/Variant codes, we should show them somewhere too.
        /// </summary>
        // TODO (default name BL-13703) make this consistent with the new Language Chooser default display name instead of using LibPalasso?
        public string GetDisplayNameForLanguage(string langTag, string metadataLanguageTag = null)
        {
            if (metadataLanguageTag == null)
                metadataLanguageTag = this.Language2Tag;

            if (langTag == this.Language1Tag && !string.IsNullOrWhiteSpace(this.Language1.Name))
                return GetLanguageNameWithScriptVariants(
                    langTag,
                    this.Language1.Name,
                    this.Language1.IsCustomName,
                    metadataLanguageTag
                );
            if (langTag == this.Language2Tag)
                return GetLanguageNameWithScriptVariants(
                    langTag,
                    this.Language2.Name,
                    this.Language2.IsCustomName,
                    metadataLanguageTag
                );
            if (langTag == this.Language3Tag)
                return GetLanguageNameWithScriptVariants(
                    langTag,
                    this.Language3.Name,
                    this.Language3.IsCustomName,
                    metadataLanguageTag
                );
            if (langTag == this.SignLanguageTag)
                return GetLanguageNameWithScriptVariants(
                    langTag,
                    this.SignLanguage.Name,
                    this.SignLanguage.IsCustomName,
                    metadataLanguageTag
                );
            return this.GetLanguageName(langTag, metadataLanguageTag);
        }

        // We always want to use a name the user deliberately gave (hence the use of 'nameIsCustom').
        // We also want to include Script/Region/Variant codes if those will be helpful.
        // OTOH, the custom name, if present may well include the sense of any srv codes, so (e.g.) if we
        // have a custom name 'Naskapi Roman', it seems like overkill to also include 'Naskapi-Latn'.
        private string GetLanguageNameWithScriptVariants(
            string completeLangTag,
            string collectionSettingsLanguageName,
            bool nameIsCustom,
            string metadataLanguageTag
        )
        {
            Guard.AgainstNull(metadataLanguageTag, "metadataLanguageTag is null.");
            var hyphenIndex = completeLangTag.IndexOf('-');
            var srvCodes =
                hyphenIndex > -1 && completeLangTag.Length > hyphenIndex + 1
                    ? completeLangTag.Substring(hyphenIndex + 1)
                    : string.Empty;
            // Special case for 'zh-CN': this one needs to be treated as if it had no srv codes
            if (completeLangTag == "zh-CN")
                srvCodes = string.Empty;
            if (string.IsNullOrEmpty(srvCodes))
                return collectionSettingsLanguageName;
            var baseIsoCode = completeLangTag.Substring(0, hyphenIndex);
            return nameIsCustom
                ? collectionSettingsLanguageName
                    + " ("
                    + GetLanguageName(baseIsoCode, metadataLanguageTag)
                    + ")"
                : collectionSettingsLanguageName
                    + "-"
                    + srvCodes
                    + " ("
                    + collectionSettingsLanguageName
                    + ")";
        }

        public static string[] ParseAdministratorString(string newAdminString)
        {
            return newAdminString.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static bool ValidateAdministrators(string newAdminString)
        {
            string[] administratorList = ParseAdministratorString(newAdminString);
            return administratorList.Length > 0 && administratorList.All(MiscUtils.IsValidEmail);
        }

        public void ModifyAdministrators(string newAdminString)
        {
            Administrators = ParseAdministratorString(newAdminString);
            Save();
        }
    }
}
