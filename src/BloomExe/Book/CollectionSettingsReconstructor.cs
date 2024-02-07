using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Bloom.Api;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Book
{
    /// <summary>
    /// Analyze a book and reconstruct as much as possible of the collection settings.
    /// (Adapted from Harvester's BookAnalyzer.cs.)
    /// </summary>
    class CollectionSettingsReconstructor
    {
        private readonly HtmlDom _dom;
        private readonly string _bookDirectory;
        private readonly string _bookshelf;
        private readonly Version _bloomVersion;

        public CollectionSettingsReconstructor(string html, string meta, string bookDirectory = "")
        {
            _bookDirectory = bookDirectory;
            _dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(html, false));
            var metaObj = DynamicJson.Parse(meta);
            if (!LoadFromUploadedSettings())
            {
                Language1Code = GetBestLangCode(1) ?? "";
                Language2Code = GetBestLangCode(2) ?? "en";
                Language3Code = GetBestLangCode(3) ?? "";
                SignLanguageCode = GetBestLangCode(-1) ?? "";
                // Try to get the language location information from the xmatter page. See BL-12583.
                SetLanguageLocationIfPossible();

                if (SignLanguageCode == "") // use the older method of looking for a sign language feature
                    SignLanguageCode = GetSignLanguageCode(metaObj);

                if (metaObj.IsDefined("brandingProjectName"))
                {
                    Branding = metaObj.brandingProjectName;
                }
                else
                {
                    // If we don't set this default value, then the epub will not build successfully. (The same is probably true for the
                    // bloompub file.)  We get a "Failure to completely load visibility document in RemoveUnwantedContent" exception thrown.
                    // See https://issues.bloomlibrary.org/youtrack/issue/BL-8485.
                    Branding = "Default";
                }

                _bookshelf = GetBookshelfIfPossible(_dom, metaObj);

                string pageNumberStyle = null;
                if (metaObj.IsDefined("page-number-style"))
                {
                    pageNumberStyle = metaObj["page-number-style"];
                }

                bool isRtl = false;
                if (metaObj.IsDefined("isRtl"))
                {
                    isRtl = metaObj["isRtl"];
                }

                var bloomCollectionElement = new XElement(
                    "Collection",
                    new XElement("Language1Iso639Code", new XText(Language1Code)),
                    new XElement("Language2Iso639Code", new XText(Language2Code)),
                    new XElement("Language3Iso639Code", new XText(Language3Code)),
                    new XElement("SignLanguageIso639Code", new XText(SignLanguageCode)),
                    new XElement(
                        "Language1Name",
                        new XText(GetLanguageDisplayNameOrEmpty(metaObj, Language1Code))
                    ),
                    new XElement(
                        "Language2Name",
                        new XText(GetLanguageDisplayNameOrEmpty(metaObj, Language2Code))
                    ),
                    new XElement(
                        "Language3Name",
                        new XText(GetLanguageDisplayNameOrEmpty(metaObj, Language3Code))
                    ),
                    new XElement(
                        "SignLanguageName",
                        new XText(GetLanguageDisplayNameOrEmpty(metaObj, SignLanguageCode))
                    ),
                    new XElement("XMatterPack", new XText(GetBestXMatter())),
                    new XElement("BrandingProjectName", new XText(Branding ?? "")),
                    new XElement("DefaultBookTags", new XText(_bookshelf)),
                    new XElement("PageNumberStyle", new XText(pageNumberStyle ?? "")),
                    new XElement("IsLanguage1Rtl", new XText(isRtl.ToString().ToLowerInvariant())),
                    new XElement("Country", new XText(Country ?? "")),
                    new XElement("Province", new XText(Province ?? "")),
                    new XElement("District", new XText(District ?? ""))
                );
                var sb = new StringBuilder();
                using (var writer = XmlWriter.Create(sb))
                    bloomCollectionElement.WriteTo(writer);
                BloomCollection = sb.ToString();
            }
        }

        /// <summary>
        /// BookAnalyzer overrides this to load from collectionFiles/book.uploadCollectionSettings
        /// if it exists. Bloom doesn't need this class at all if it finds that file.
        /// </summary>
        public virtual bool LoadFromUploadedSettings()
        {
            return false;
        }

        private string GetBookShelfFromTagsIfPossible(string defaultTagsString)
        {
            if (String.IsNullOrEmpty(defaultTagsString))
                return String.Empty;
            var defaultTags = defaultTagsString.Split(',');
            var defaultBookshelfTag = defaultTags
                .Where(t => t.StartsWith("bookshelf:"))
                .FirstOrDefault();
            return defaultBookshelfTag ?? String.Empty;
        }

        private string GetBookshelfIfPossible(HtmlDom dom, dynamic metaObj)
        {
            if (dom.Body.HasAttribute("data-bookshelfurlkey"))
            {
                var shelf = dom.Body.GetAttribute("data-bookshelfurlkey");
                if (!String.IsNullOrEmpty(shelf))
                    return "bookshelf:" + shelf;
            }
            if (metaObj.IsDefined("tags"))
            {
                string[] tags = metaObj["tags"];
                if (tags == null)
                    return String.Empty;
                foreach (var tag in tags)
                {
                    if (tag.StartsWith("bookshelf:"))
                        return tag;
                }
            }
            return String.Empty;
        }

        public string GetBookshelf()
        {
            return _bookshelf;
        }

        // [Obsolete: The DataDiv now contains (as of 5.5) the signlanguage code. We use this in case
        // a book was uploaded with an older Bloom.]
        // The only trace in the book that it belongs to a collection with a sign language is that
        // it is marked as having the sign language feature for that language. This is unfortunate but
        // the best we can do with the data we're currently uploading. We really need to know this,
        // because if sign language of the collection is not set, updating the book's features will
        // remove the language-specific sign language feature, and then we have no way to know it
        // should be there.
        // This is not very reliable; the collection might have a sign language but if the book
        // doesn't have video it will not be reflected in features. However, for now, we only care
        // about it in order to preserve the language-specific feature, so getting it from the
        // existing one is good enough.
        private string GetSignLanguageCode(dynamic metaObj)
        {
            if (!metaObj.IsDefined("features"))
                return "";
            var features = metaObj.features.Deserialize<string[]>();
            foreach (string feature in features)
            {
                const string marker = "signLanguage:";
                if (feature.StartsWith(marker))
                {
                    return feature.Substring(marker.Length);
                }
            }
            return "";
        }

        private string GetLanguageDisplayNameOrEmpty(dynamic metadata, string isoCode)
        {
            if (string.IsNullOrEmpty(isoCode))
                return "";

            if (
                metadata.IsDefined("language-display-names")
                && metadata["language-display-names"] != null
                && metadata["language-display-names"].IsDefined(isoCode)
            )
                return metadata["language-display-names"][isoCode];

            return "";
        }

        /// <summary>
        /// Gets the language code for the specified language number
        /// </summary>
        /// <param name="x">The language number</param>
        /// <returns>The language code for the specified language, as determined from the bloomDataDiv. Returns null if not found.</returns>
        private string GetBestLangCode(int x)
        {
            string xpathString = "//*[@id='bloomDataDiv']/*[@data-book='";
            xpathString += x < 1 ? "signLanguage']" : $"contentLanguage{x}']";
            var dataDivNodes = _dom.SafeSelectNodes(xpathString);
            // contentLanguage2 and contentLanguage3 are only present in bilingual or trilingual books,
            // so we fall back to getting lang 2 and 3 from the html if needed.
            // We should never be missing contentLanguage1 (but having the fallback here is basically free).
            // However, contentLanguage2 may match either contentNational1 or contentNational2,
            // and contentLanguage3 may match either contentNational1 or contentNational2. We need
            // to check for these possibilities to reconstruct the collection language settings.
            var htmlDiv = GetDivForLanguageUseFromHtml(x);
            var langFromHtml = htmlDiv?.Attributes["lang"]?.Value;
            if (dataDivNodes.Count == 0)
                return langFromHtml;
            string langFromDataDiv = dataDivNodes.Item(0).InnerText.Trim();
            if (String.IsNullOrEmpty(langFromHtml) || langFromDataDiv == langFromHtml)
                return langFromDataDiv;
            // We have a mismatch between the dataDivNodes[0] and the htmlDiv. htmlDiv wins because it has more
            // specific information in its class attribute.  (bloom-contentNational1 / bloom-contentNational2)
            return langFromHtml;
        }

        private XmlElement GetDivForLanguageUseFromHtml(int languageNumber)
        {
            // Sign language codes don't accompany videos in the Html.
            if (languageNumber < 0)
                return null;
            string classToLookFor = GetClassNameForLanguageNumber(languageNumber);
            // We assume that the bookTitle is always present and may have the relevant language
            var xpathString =
                $"//div[contains(@class, '{classToLookFor}') and @data-book='bookTitle' and @lang]";
            var titleDiv = _dom.SelectSingleNode(xpathString);
            var lang = titleDiv?.Attributes["lang"]?.Value;
            if (!String.IsNullOrEmpty(lang))
                return titleDiv;
            // Look for a visible div/p that has text in the designated national language.
            // (This fixes https://issues.bloomlibrary.org/youtrack/issue/BL-11050.)
            xpathString =
                $"//div[contains(@class,'bloom-visibility-code-on') and contains(@class,'{classToLookFor}') and @lang]/p[normalize-space(text())!='']";
            var para = _dom.SelectSingleNode(xpathString);
            if (para != null)
                return para.ParentNode as XmlElement;
            return null;
        }

        private static string GetClassNameForLanguageNumber(int languageNumber)
        {
            string classToLookFor;
            switch (languageNumber)
            {
                case 1:
                    classToLookFor = "bloom-content1";
                    break;
                case 2:
                    classToLookFor = "bloom-contentNational1";
                    break;
                case 3:
                    classToLookFor = "bloom-contentNational2";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(languageNumber),
                        "Must be 1, 2, or 3"
                    );
            }
            return classToLookFor;
        }

        private void SetLanguageLocationIfPossible()
        {
            string xpathString = "//*[@data-xmatter-page]//*[@data-library='languageLocation']";
            var matchingNodes = _dom.SafeSelectNodes(xpathString);
            if (matchingNodes.Count < 1)
                return;
            var matchedNode = matchingNodes.Item(0);
            var rawData = matchedNode.InnerText.Trim();
            if (String.IsNullOrEmpty(rawData))
                return;
            const string separator = ", ";
            var locationData = rawData.Split(new[] { separator }, StringSplitOptions.None);
            if (locationData.Length >= 3)
            {
                District = locationData[0];
                Province = locationData[1];
                Country = string.Join(separator, locationData.Skip(2));
            }
            else if (locationData.Length == 2)
            {
                Province = locationData[0];
                Country = locationData[1];
            }
            else if (locationData.Length == 1)
            {
                Country = locationData[0];
            }
        }

        /// <summary>
        /// Finds the XMatterName for this book. If it cannot be determined, falls back to "Device"
        /// </summary>
        private string GetBestXMatter()
        {
            string xmatterName = "Device"; // This is the default, in case anything goes wrong.
            if (String.IsNullOrEmpty(_bookDirectory))
            {
                return xmatterName;
            }

            DirectoryInfo dirInfo;
            try
            {
                dirInfo = new DirectoryInfo(_bookDirectory);
            }
            catch
            {
                return xmatterName;
            }

            string suffix = "-XMatter.css";
            var files = dirInfo.GetFiles();
            var matches = files.Where(x => x.Name.EndsWith(suffix));

            if (matches.Any())
            {
                string xmatterFilename = matches.First().Name;
                xmatterName = XMatterHelper.GetXMatterFromStyleSheetFileName(xmatterFilename);
            }

            return xmatterName ?? "Device";
        }

        public static CollectionSettingsReconstructor FromFolder(string bookFolder)
        {
            var bookPath = BookStorage.FindBookHtmlInFolder(bookFolder);
            if (!File.Exists(bookPath))
                throw new Exception("Incomplete upload: missing book's HTML file");
            var metaPath = Path.Combine(bookFolder, "meta.json");
            if (!File.Exists(metaPath))
                throw new Exception("Incomplete upload: missing book's meta.json file");
            return new CollectionSettingsReconstructor(
                File.ReadAllText(bookPath, Encoding.UTF8),
                File.ReadAllText(metaPath, Encoding.UTF8),
                bookFolder
            );
        }

        public string WriteBloomCollection(string bookFolder)
        {
            var collectionFolder = Path.GetDirectoryName(bookFolder);
            var result = Path.Combine(collectionFolder, "temp.bloomCollection");
            File.WriteAllText(result, BloomCollection, Encoding.UTF8);
            return result;
        }

        public string Language1Code { get; }
        public string Language2Code { get; }
        public string Language3Code { get; set; }
        public string SignLanguageCode { get; }
        public string Branding { get; }

        public string Country { get; private set; }
        public string Province { get; private set; }
        public string District { get; private set; }

        /// <summary>
        /// Either the contents of the uploaded Collection settings file or a generated
        /// skeleton BookCollection file for this book.
        /// </summary>
        public string BloomCollection { get; set; }

        /// <summary>
        /// Returns the number of words in a piece of text
        /// </summary>
        internal static int GetWordCount(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return 0;
            // FYI, GetWordsFromHtmlString() (which is a port from our JS code) returns an array containing the empty string
            // if the input to it is the empty string. So handle that...

            var words = GetWordsFromHtmlString(text);
            return words.Where(x => !String.IsNullOrEmpty(x)).Count();
        }

        private static readonly Regex kHtmlLinebreakRegex = new Regex(
            "/<br><\\/br>|<br>|<br \\/>|<br\\/>|\r?\n/",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Splits a piece of HTML text
        /// </summary>
        /// <param name="textHTML">The text to split</param>
        /// <param name="letters">Optional - Characters which Unicode defines as punctuation but which should be counted as letters instead</param>
        /// <returns>An array where each element represents a word</returns>
        private static string[] GetWordsFromHtmlString(string textHTML, string letters = null)
        {
            // This function is a port of the Javascript version in BloomDesktop's synphony_lib.js's getWordsFromHtmlString() function

            // Enhance: I guess it'd be ideal if we knew what the text's culture setting was, but I don't know how we can get that
            textHTML = textHTML.ToLower();

            // replace html break with space
            string s = kHtmlLinebreakRegex.Replace(textHTML, " ");

            var punct = "\\p{P}";

            if (!String.IsNullOrEmpty(letters))
            {
                // BL-1216 Use negative look-ahead to keep letters from being counted as punctuation
                // even if Unicode says something is a punctuation character when the user
                // has specified it as a letter (like single quote).
                punct = "(?![" + letters + "])" + punct;
            }
            /**************************************************************************
             * Replace punctuation in a sentence with a space.
             *
             * Preserves punctuation marks within a word (ex. hyphen, or an apostrophe
             * in a contraction)
             **************************************************************************/
            var regex = new Regex(
                "(^"
                    + punct
                    + "+)"
                    + // punctuation at the beginning of a string
                    "|("
                    + punct
                    + "+[\\s\\p{Z}\\p{C}]+"
                    + punct
                    + "+)"
                    + // punctuation within a sentence, between 2 words (word" "word)
                    "|([\\s\\p{Z}\\p{C}]+"
                    + punct
                    + "+)"
                    + // punctuation within a sentence, before a word
                    "|("
                    + punct
                    + "+[\\s\\p{Z}\\p{C}]+)"
                    + // punctuation within a sentence, after a word
                    "|("
                    + punct
                    + "+$)" // punctuation at the end of a string
            );
            s = regex.Replace(s, " ");

            // Split into words using Separator and SOME Control characters
            // Originally the code had p{C} (all Control characters), but this was too all-encompassing.
            const string whitespace = "\\p{Z}";
            const string controlChars = "\\p{Cc}"; // "real" Control characters
            // The following constants are Control(format) [p{Cf}] characters that should split words.
            // e.g. ZERO WIDTH SPACE is a Control(format) charactor
            // (See http://issues.bloomlibrary.org/youtrack/issue/BL-3933),
            // but so are ZERO WIDTH JOINER and NON JOINER (See https://issues.bloomlibrary.org/youtrack/issue/BL-7081).
            // See list at: https://www.compart.com/en/unicode/category/Cf
            const string zeroWidthSplitters = "\u200b"; // ZERO WIDTH SPACE
            const string ltrrtl = "\u200e\u200f"; // LEFT-TO-RIGHT MARK / RIGHT-TO-LEFT MARK
            const string directional = "\u202A-\u202E"; // more LTR/RTL/directional markers
            const string isolates = "\u2066-\u2069"; // directional "isolate" markers
            // split on whitespace, Control(control) and some Control(format) characters
            regex = new Regex(
                "["
                    + whitespace
                    + controlChars
                    + zeroWidthSplitters
                    + ltrrtl
                    + directional
                    + isolates
                    + "]+"
            );
            return regex.Split(s.Trim());
        }

        private IEnumerable<XmlElement> GetNumberedPages() =>
            _dom.SafeSelectNodes("//div[contains(concat(' ', @class, ' '),' numberedPage ')]")
                .Cast<XmlElement>();

        /// <remarks>This xpath assumes it is rooted at the level of the marginBox's parent (the page).</remarks>
        private static string GetTranslationGroupsXpath(bool includeImageDescriptions)
        {
            string imageDescFilter = includeImageDescriptions
                ? ""
                : " and not(contains(@class,'bloom-imageDescription'))";
            // We no longer (or ever did?) use box-header-off for anything, but some older books have it.
            // For our purposes (and really all purposes throughout the system), we don't want them to include them.
            string xPath =
                $"div[contains(@class,'marginBox')]//div[contains(@class,'bloom-translationGroup') and not(contains(@class, 'box-header-off')){imageDescFilter}]";
            return xPath;
        }

        /// <summary>
        /// Gets the translation groups for the current page that are not within the image container
        /// </summary>
        /// <param name="pageElement">The page containing the bloom-editables</param>
        private static XmlNodeList GetTranslationGroupsFromPage(
            XmlElement pageElement,
            bool includeImageDescriptions
        )
        {
            return pageElement.SafeSelectNodes(GetTranslationGroupsXpath(includeImageDescriptions));
        }

        /// <summary>
        /// Gets the bloom-editables for the current page that match the language and are not within the image container
        /// </summary>
        /// <param name="pageElement">The page containing the bloom-editables</param>
        /// <param name="lang">Only bloom-editables matching this ISO language code will be returned</param>
        private static IEnumerable<XmlElement> GetEditablesFromPage(
            XmlElement pageElement,
            string lang,
            bool includeImageDescriptions = true,
            bool includeTextOverPicture = true
        )
        {
            string translationGroupXPath = GetTranslationGroupsXpath(includeImageDescriptions);
            string langFilter = HtmlDom.IsLanguageValid(lang) ? $"[@lang='{lang}']" : "";

            string xPath =
                $"{translationGroupXPath}//div[contains(@class,'bloom-editable')]{langFilter}";
            var editables = pageElement.SafeSelectNodes(xPath).Cast<XmlElement>();

            foreach (var editable in editables)
            {
                bool isOk = true;
                if (!includeTextOverPicture)
                {
                    var textOverPictureMatch = GetClosestMatch(
                        editable,
                        (e) =>
                        {
                            return HtmlDom.HasClass(e, "bloom-textOverPicture");
                        }
                    );

                    isOk = textOverPictureMatch == null;
                }

                if (isOk)
                    yield return editable;
            }
        }

        internal delegate bool ElementMatcher(XmlElement element);

        /// <summary>
        /// Find the closest ancestor (or self) that matches the condition
        /// </summary>
        /// <param name="startElement"></param>
        /// <param name="matcher">A function that returns true if the element matches</param>
        /// <returns></returns>
        internal static XmlElement GetClosestMatch(XmlElement startElement, ElementMatcher matcher)
        {
            XmlElement currentElement = startElement;
            while (currentElement != null)
            {
                if (matcher(currentElement))
                {
                    return currentElement;
                }

                currentElement = currentElement.ParentNode as XmlElement;
            }

            return null;
        }
    }
}
