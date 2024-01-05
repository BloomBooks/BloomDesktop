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
using Bloom.Publish.BloomPub;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using SIL.Code;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.WritingSystems;
using Directory = System.IO.Directory;

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
        public WritingSystem Language1;
        public WritingSystem Language2;
        public WritingSystem Language3;
        public WritingSystem[] LanguagesZeroBased;

        // Email addresses of users authorized to change collection settings if this is a TeamCollection.
        public string[] Administrators;

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
            //Note: I'm not convinced we actually ever rely on dynamic name lookups anymore?
            //See: https://issues.bloomlibrary.org/youtrack/issue/BL-7832
            Func<string> getTagOfDefaultLanguageForNaming = () => Language2.Tag;
            Language1 = new WritingSystem(1, getTagOfDefaultLanguageForNaming);
            Language2 = new WritingSystem(2, getTagOfDefaultLanguageForNaming);
            Language3 = new WritingSystem(3, getTagOfDefaultLanguageForNaming);
            LanguagesZeroBased = new WritingSystem[3];
            this.LanguagesZeroBased[0] = Language1;
            this.LanguagesZeroBased[1] = Language2;
            this.LanguagesZeroBased[2] = Language3;

            SignLanguage = new WritingSystem(-1, getTagOfDefaultLanguageForNaming);

            BrandingProjectKey = "Default";
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
            Language1.FontName = collectionInfo.Language1.FontName;
            Language1 = collectionInfo.Language1;
            Language2 = collectionInfo.Language2;
            Language3 = collectionInfo.Language3;

            Language2.FontName = Language3.FontName = WritingSystem.GetDefaultFontName();
            SignLanguage = collectionInfo.SignLanguage;

            Country = collectionInfo.Country;
            Province = collectionInfo.Province;
            District = collectionInfo.District;
            IsSourceCollection = collectionInfo.IsSourceCollection;
            XMatterPackName = collectionInfo.XMatterPackName;
            PageNumberStyle = collectionInfo.PageNumberStyle;
            BrandingProjectKey = collectionInfo.BrandingProjectKey;
            SubscriptionCode = collectionInfo.SubscriptionCode;
            if (BrandingProjectKey == "Local Community")
            {
                // migrate for 4.4
                BrandingProjectKey = "Local-Community";
            }

            AudioRecordingMode = collectionInfo.AudioRecordingMode;
            AudioRecordingTrimEndMilliseconds = collectionInfo.AudioRecordingTrimEndMilliseconds;
            BooksOnWebGoal = collectionInfo.BooksOnWebGoal;

            Save();
        }

        /// <summary>
        /// can be used whether the Collection exists already, or not
        /// </summary>
        public CollectionSettings(string desiredOrExistingSettingsFilePath)
            : this()
        {
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
            Language1.SaveToXElement(xml);
            Language2.SaveToXElement(xml);
            Language3.SaveToXElement(xml);
            SignLanguage.SaveToXElement(xml);
            xml.Add(new XElement("OneTimeCheckVersionNumber", OneTimeCheckVersionNumber));
            xml.Add(new XElement("IsSourceCollection", IsSourceCollection.ToString()));
            xml.Add(new XElement("XMatterPack", XMatterPackName));
            xml.Add(new XElement("PageNumberStyle", PageNumberStyle));
            xml.Add(new XElement("BrandingProjectName", BrandingProjectKey));
            xml.Add(new XElement("SubscriptionCode", SubscriptionCode));
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
            foreach (var key in ColorPalettes.Keys)
            {
                var palette = new XElement("Palette", ColorPalettes[key]);
                palette.Add(new XAttribute("id", key));
                xml.Add(palette);
            }
            SIL.IO.RobustIO.SaveXElement(xml, SettingsFilePath);
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
            // note: css pseudo elements  cannot have a @lang attribute. So this is needed to show page numbers in scripts not covered by Andika New Basic.
            WritingSystem.AddSelectorCssRule(
                sb,
                ".numberedPage::after",
                Language1.FontName,
                Language1.IsRightToLeft,
                Language1.LineHeight,
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

                Language1.ReadFromXml(xml, true, "en");
                Language2.ReadFromXml(xml, true, "self");
                Language3.ReadFromXml(xml, true, Language2.Tag);
                SignLanguage.ReadFromXml(xml, true, Language2.Tag);

                XMatterPackName = ReadString(xml, "XMatterPack", "Factory");

                var style = ReadString(xml, "PageNumberStyle", "Decimal");

                //for historical (and maybe future?) reasons, we collect the page number style as one of the
                //CSS counter number styles
                PageNumberStyle = CssNumberStylesToCultureOrDigits.Keys.Contains(style)
                    ? style
                    : "Decimal";
                OneTimeCheckVersionNumber = ReadInteger(xml, "OneTimeCheckVersionNumber", 0);
                BrandingProjectKey = ReadString(xml, "BrandingProjectName", "Default");
                SubscriptionCode = ReadString(xml, "SubscriptionCode", null);
                if (
                    BrandingProjectKey != "Default"
                    && BrandingProjectKey != "Local-Community"
                    && !Program.RunningHarvesterMode
                )
                {
                    // Validate branding, so things can't be circumvented by just typing something random into settings
                    var expirationDate = CollectionSettingsApi.GetExpirationDate(SubscriptionCode);
                    if (expirationDate < DateTime.Now) // no longer require branding files to exist yet
                    {
                        InvalidBranding = BrandingProjectKey;
                        BrandingProjectKey = "Default"; // keep the code, but don't use it as active branding.
                    }
                }
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
                        defaultBookshelfTag == null
                        || BrandingProjectKey == "Default"
                        || BrandingProjectKey == "Local-Community"
                    )
                        ? ""
                        : defaultBookshelfTag.Substring("bookshelf:".Length);

                var bulkPublishSettingsFromXml = BulkBloomPubPublishSettings.LoadFromXElement(xml);
                if (bulkPublishSettingsFromXml != null)
                {
                    BulkPublishBloomPubSettings = bulkPublishSettingsFromXml;
                }

                LoadDictionary(xml, "Palette", ColorPalettes);
            }
            catch (Exception)
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
                throw;
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

        internal static string ReadString(XElement document, string id, string defaultValue)
        {
            var nodes = document.Descendants(id);
            if (nodes != null && nodes.Count() > 0)
                return nodes.First().Value;
            else
            {
                return defaultValue;
            }
        }

        internal static void LoadDictionary(
            XElement document,
            string tag,
            Dictionary<string, string> dict
        )
        {
            dict.Clear();
            var elements = document.Descendants(tag);
            if (elements != null)
            {
                foreach (XElement element in elements)
                    dict[element.Attribute("id").Value] = element.Value;
            }
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
            if (!string.IsNullOrEmpty(BrandingProjectKey))
            {
                var xmatterToUse = BrandingSettings
                    .GetSettingsOrNull(this.BrandingProjectKey)
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

        // e.g. "ABC2020" or "Kyrgyzstan2020[English]"
        public string BrandingProjectKey { get; set; }

        public string GetBrandingFlavor()
        {
            BrandingSettings.ParseBrandingKey(BrandingProjectKey, out var baseKey, out var flavor);
            return flavor;
        }

        public string GetBrandingFolderName()
        {
            BrandingSettings.ParseBrandingKey(
                BrandingProjectKey,
                out var folderName,
                out var flavor
            );
            return folderName;
        }

        public string SubscriptionCode { get; set; }

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

        public bool HaveEnterpriseFeatures =>
            !String.IsNullOrEmpty(BrandingProjectKey) && BrandingProjectKey != "Default";
        public bool HaveEnterpriseSubscription =>
            HaveEnterpriseFeatures && BrandingProjectKey != "Local-Community";

        private readonly Dictionary<string, string> ColorPalettes =
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
                    Save();
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
            Analytics.SetApplicationProperty("BrandingProjectName", BrandingProjectKey);
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

        /// <summary>
        /// Return true if the part of the subscription code that identifies the branding is one we know about.
        /// Either the branding files must exist or the expiration date must be set, even if expired.
        /// This allows new, not yet implemented, subscriptions/brandings to be recognized as valid, and expired
        /// subscriptions to be flagged as such but not treated as totally invalid.
        /// </summary>
        public bool IsSubscriptionCodeKnown()
        {
            return BrandingProject.HaveFilesForBranding(BrandingProjectKey)
                || CollectionSettingsApi.GetExpirationDate(SubscriptionCode) != DateTime.MinValue;
        }

        public CollectionSettingsApi.EnterpriseStatus GetEnterpriseStatus()
        {
            if (CollectionSettingsApi.FixEnterpriseSubscriptionCodeMode)
            {
                // We're displaying the dialog to fix a branding code...select that option
                return CollectionSettingsApi.EnterpriseStatus.Subscription;
            }
            if (BrandingProjectKey == "Default")
                return CollectionSettingsApi.EnterpriseStatus.None;
            else if (BrandingProjectKey == "Local-Community")
                return CollectionSettingsApi.EnterpriseStatus.Community;
            return CollectionSettingsApi.EnterpriseStatus.Subscription;
        }
    }
}
