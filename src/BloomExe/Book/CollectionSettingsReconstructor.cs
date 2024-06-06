using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Bloom.Api;
using Bloom.SafeXml;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Book
{
    /// <summary>
    /// Analyze a book and reconstruct as much as possible of the collection settings.
    /// (Adapted from Harvester's BookAnalyzer.cs.)
    /// </summary>
    public class CollectionSettingsReconstructor
    {
        protected readonly HtmlDom _dom;
        protected readonly string _bookDirectory;
        protected string _bookshelf;

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
            var langFromHtml = htmlDiv?.GetAttribute("lang");
            if (dataDivNodes.Length == 0)
                return langFromHtml;
            string langFromDataDiv = dataDivNodes[0].InnerText.Trim();
            if (String.IsNullOrEmpty(langFromHtml) || langFromDataDiv == langFromHtml)
                return langFromDataDiv;
            // We have a mismatch between the dataDivNodes[0] and the htmlDiv. htmlDiv wins because it has more
            // specific information in its class attribute.  (bloom-contentNational1 / bloom-contentNational2)
            return langFromHtml;
        }

        private SafeXmlElement GetDivForLanguageUseFromHtml(int languageNumber)
        {
            // Sign language codes don't accompany videos in the Html.
            if (languageNumber < 0)
                return null;
            string classToLookFor = GetClassNameForLanguageNumber(languageNumber);
            // We assume that the bookTitle is always present and may have the relevant language
            var xpathString =
                $"//div[contains(@class, '{classToLookFor}') and @data-book='bookTitle' and @lang]";
            var titleDiv = _dom.SelectSingleNode(xpathString);
            var lang = titleDiv?.GetAttribute("lang");
            if (!String.IsNullOrEmpty(lang))
                return titleDiv;
            // Look for a visible div/p that has text in the designated national language.
            // (This fixes https://issues.bloomlibrary.org/youtrack/issue/BL-11050.)
            xpathString =
                $"//div[contains(@class,'bloom-visibility-code-on') and contains(@class,'{classToLookFor}') and @lang]/p[normalize-space(text())!='']";
            var para = _dom.SelectSingleNode(xpathString);
            if (para != null)
                return para.ParentNode as SafeXmlElement;
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
            if (matchingNodes.Length < 1)
                return;
            var matchedNode = matchingNodes[0];
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

        public string Language1Code { get; protected set; }
        public string Language2Code { get; protected set; }
        public string Language3Code { get; set; }
        public string SignLanguageCode { get; protected set; }
        public string Branding { get; protected set; }

        public string Country { get; protected set; }
        public string Province { get; protected set; }
        public string District { get; protected set; }

        /// <summary>
        /// Either the contents of the uploaded Collection settings file or a generated
        /// skeleton BookCollection file for this book.
        /// </summary>
        public string BloomCollection { get; set; }
    }
}
