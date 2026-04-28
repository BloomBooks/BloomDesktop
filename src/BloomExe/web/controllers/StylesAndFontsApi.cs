using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bloom.Api;
using Bloom.Book;
using Bloom.SafeXml;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.web.controllers
{
    public class StylesAndFontsApi
    {
        private readonly BookSelection _bookSelection;

        public StylesAndFontsApi(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "stylesAndFonts/getDataRows",
                HandleGetDataRows,
                false
            );
            apiHandler.RegisterBooleanEndpointHandler(
                "stylesAndFonts/unusedLanguageDataExists",
                request => DoesUnusedLanguageDataExist(),
                null,
                false
            );
        }

        /// <summary>
        /// Return true iff there are languages in the book data (including defaultLanguages.css) that
        /// do not appear in the current collection settings, but that have been assigned a font.
        /// </summary>
        private bool DoesUnusedLanguageDataExist()
        {
            // Get all the languages used in the book and their default fonts.
            InitializeFontAndLanguageData(
                out var langToDefaultFont,
                out _,
                out var unusedLanguages
            );
            // Get all the user-modified styles that set a font for the style.
            var modifiedStylesAndFonts = GetUserModifiedStylesAndFonts(
                _bookSelection.CurrentSelection.OurHtmlDom
            );
            foreach (var unusedLang in unusedLanguages)
            {
                if (langToDefaultFont.ContainsKey(unusedLang))
                    return true;
                foreach (var modifiedStyle in modifiedStylesAndFonts)
                {
                    if (modifiedStyle.languageTag == unusedLang || modifiedStyle.languageTag == "*")
                    {
                        return true;
                    }
                }
            }
            // Data may exist in unused languages, but if so they've never been assigned a font,
            // so there's no data to show for them in the Fonts tab of the Book Settings dialog.
            return false;
        }

        private void InitializeFontAndLanguageData(
            out Dictionary<string, string> langToDefaultFont,
            out Dictionary<string, List<string>> fontToLangs,
            out HashSet<string> unusedLanguages
        )
        {
            var book = _bookSelection.CurrentSelection;
            fontToLangs = new Dictionary<string, List<string>>();
            langToDefaultFont = GetLanguagesAndDefaultFonts(fontToLangs, book, book.OurHtmlDom);

            var collectionLanguages = GetCurrentLanguageSet();
            unusedLanguages = new HashSet<string>();
            foreach (var key in langToDefaultFont.Keys)
            {
                if (!collectionLanguages.Contains(key))
                    unusedLanguages.Add(key);
            }
            // There may be languages that have data in the book, but don't have default fonts assigned
            // in defaultLangauges.css.  They may still have fonts assigned in style overrides.  So we
            // also check for any languages that have data in the book at all, even if they don't have
            // a default font.
            var allLanguages = book.AllPublishableLanguages(
                includeLangsOccurringOnlyInXmatter: false
            );
            foreach (var key in allLanguages.Keys)
            {
                if (!collectionLanguages.Contains(key))
                    unusedLanguages.Add(key);
            }
        }

        // This class must be kept in sync with the IStyleAndFont interface in StyleAndFontTable.tsx.
        public class StyleAndFont
        {
            public string style;
            public string styleName;
            public string languageName;
            public string languageTag;
            public string fontName;
            public string pageId;
            public string pageDescription;

            // just helpful for debugging at this point
            public override string ToString()
            {
                return $"{style}, {fontName}";
            }
        }

        public struct PageInfo
        {
            public string id;
            public string description;
        }

        HashSet<string> _currentLanguageSet = null;

        private HashSet<string> GetCurrentLanguageSet()
        {
            if (_currentLanguageSet != null)
                return _currentLanguageSet;

            var book = _bookSelection.CurrentSelection;
            _currentLanguageSet = new HashSet<string>
            {
                book.CollectionSettings.Language1Tag,
                book.CollectionSettings.Language2Tag,
            };
            if (!string.IsNullOrEmpty(book.CollectionSettings.Language3Tag))
                _currentLanguageSet.Add(book.CollectionSettings.Language3Tag);
            if (!string.IsNullOrEmpty(book.CollectionSettings.SignLanguageTag))
                _currentLanguageSet.Add(book.CollectionSettings.SignLanguageTag);
            return _currentLanguageSet;
        }

        private void HandleGetDataRows(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            var languagesSelection = request.GetParamOrNull("languages");
            // Get all the styles used in the book.
            var stylesInBook = GetStylesUsedInBook(book.OurHtmlDom, book.FolderPath);
            // Get all the languages used in the book and their default fonts.
            InitializeFontAndLanguageData(
                out var langToDefaultFont,
                out var fontToLangs,
                out var unusedLanguages
            );
            // Get all the user-modified styles that set a font for the style.
            var modifiedStylesAndFonts = GetUserModifiedStylesAndFonts(book.OurHtmlDom);
            // Add the styles that are in the book but not covered by the user-modified styles.
            var stylesAndFonts = new List<StyleAndFont>();
            var collectionLanguages = GetCurrentLanguageSet();
            var allLanguagesInBook = new HashSet<string>(langToDefaultFont.Keys);
            allLanguagesInBook.UnionWith(unusedLanguages);
            foreach (var style in stylesInBook.Keys)
            {
                var modified = modifiedStylesAndFonts.FindAll(s => s.style == style).ToArray();
                if (modified.Length == 0)
                {
                    foreach (var kvp in fontToLangs)
                    {
                        string languageTag = null;
                        if (languagesSelection == "current")
                        {
                            var currentlyUsed = kvp
                                .Value.Where(lang => collectionLanguages.Contains(lang))
                                .ToList();
                            if (currentlyUsed.Count == 0)
                                continue; // This font is not used by the book's current languages, so skip it.
                            if (currentlyUsed.Count == 1)
                                languageTag = currentlyUsed.First();
                            else if (currentlyUsed.Count == collectionLanguages.Count)
                                languageTag = "*";
                            else
                                languageTag = string.Join(",", currentlyUsed);
                        }
                        else
                        {
                            var currentlyUnused = kvp
                                .Value.Where(lang => !collectionLanguages.Contains(lang))
                                .ToList();
                            if (currentlyUnused.Count == 0)
                                continue; // This font is used only by the book's current languages, so skip it.
                            if (currentlyUnused.Count == 1)
                                languageTag = currentlyUnused.First();
                            else
                                languageTag = string.Join(",", currentlyUnused);
                        }
                        var styleAndFont = new StyleAndFont();
                        styleAndFont.style = style;
                        styleAndFont.languageTag = languageTag;
                        styleAndFont.fontName = kvp.Key;
                        styleAndFont.pageId = stylesInBook[style].id;
                        styleAndFont.pageDescription = stylesInBook[style].description;
                        stylesAndFonts.Add(styleAndFont);
                    }
                }
                else
                {
                    foreach (var tag in allLanguagesInBook)
                    {
                        if (languagesSelection == "current" && !collectionLanguages.Contains(tag))
                            continue; // This language is not actually used in the book, so skip it.
                        else if (languagesSelection == "other" && collectionLanguages.Contains(tag))
                            continue; // This language is used in the book, so skip it.
                        string fontName = null;
                        var modifiedForLang = modified.FirstOrDefault(s =>
                            s.languageTag == tag && !string.IsNullOrEmpty(s.fontName)
                        );
                        if (modifiedForLang != null)
                            fontName = modifiedForLang.fontName;
                        else
                        {
                            // I don't think we normally have user-modified styles that apply to all languages,
                            // but if we do, it will apply to any language that doesn't have its own user-modified style.
                            // This replaces earlier code that checked for only having one entry in modified,
                            // but that could wrongly suggest that a font was in use when in fact we just had
                            // one language-specific override, possibly for a language not even used in the book.
                            var modifiedForAll = modified.FirstOrDefault(s =>
                                s.languageTag == "*" && !string.IsNullOrEmpty(s.fontName)
                            );
                            if (modifiedForAll != null)
                                fontName = modifiedForAll.fontName;
                            else if (langToDefaultFont.ContainsKey(tag))
                                fontName = langToDefaultFont[tag];
                            else
                                continue; // No font is specified for this language, so skip it.
                        }
                        var styleAndFont = new StyleAndFont();
                        styleAndFont.style = style;
                        styleAndFont.languageTag = tag;
                        styleAndFont.fontName = fontName;
                        styleAndFont.pageId = stylesInBook[style].id;
                        styleAndFont.pageDescription = stylesInBook[style].description;
                        stylesAndFonts.Add(styleAndFont);
                    }
                }
            }
            // Add the user-modified styles that are not actually used in the book.
            foreach (var styleAndFont in modifiedStylesAndFonts)
            {
                if (
                    !stylesAndFonts.Exists(s =>
                        s.style == styleAndFont.style
                        && (
                            s.languageTag == styleAndFont.languageTag
                            || (
                                s.languageTag == "*"
                                && collectionLanguages.Contains(styleAndFont.languageTag)
                            )
                            || s.languageTag.Split(",").Contains(styleAndFont.languageTag)
                        )
                    )
                )
                {
                    // I'm not sure if languageTag can ever == "*" here, but Devin was worried about it,
                    // and if it does happen, we may as well always include it.  All of these entries are
                    // likely to be skipped by TS code anyway because of the empty pageId.
                    if (styleAndFont.languageTag != "*")
                    {
                        if (
                            languagesSelection == "current"
                            && !collectionLanguages.Contains(styleAndFont.languageTag)
                        )
                            continue; // This language is not actually used in the book, so skip it.
                        else if (
                            languagesSelection == "other"
                            && collectionLanguages.Contains(styleAndFont.languageTag)
                        )
                            continue; // This language is used in the book, so skip it.
                    }
                    styleAndFont.pageId = "";
                    styleAndFont.pageDescription = "";
                    stylesAndFonts.Add(styleAndFont);
                }
            }
            foreach (var styleAndFont in stylesAndFonts.ToArray())
            {
                styleAndFont.languageName = GetLanguageName(styleAndFont.languageTag);
                // If the style is not in the default styles, don't worry about not getting a localized name.
                styleAndFont.styleName = L10NSharp.LocalizationManager.GetString(
                    $"EditTab.FormatDialog.DefaultStyles.{styleAndFont.style}-style",
                    GetEnglishStyleName(styleAndFont.style)
                );
            }
            stylesAndFonts.Sort(
                (a, b) =>
                {
                    var namesCompared = string.Compare(
                        a.styleName,
                        b.styleName,
                        StringComparison.InvariantCultureIgnoreCase
                    );
                    if (namesCompared == 0)
                        return string.Compare(
                            a.languageName,
                            b.languageName,
                            StringComparison.InvariantCultureIgnoreCase
                        );
                    else
                        return namesCompared;
                }
            );

            // Ensure every collection-level default font is included in the list
            // (only relevant when showing "current" languages, not "other" languages)
            if (languagesSelection == "current")
            {
                var collectionLevelFonts = new HashSet<(string fontName, string languageTag)>
                {
                    (
                        book.CollectionSettings.Language1.FontName,
                        book.CollectionSettings.Language1Tag
                    ),
                    (
                        book.CollectionSettings.Language2.FontName,
                        book.CollectionSettings.Language2Tag
                    ),
                };
                if (!String.IsNullOrEmpty(book.CollectionSettings.Language3Tag))
                {
                    collectionLevelFonts.Add(
                        (
                            book.CollectionSettings.Language3.FontName,
                            book.CollectionSettings.Language3Tag
                        )
                    );
                }
                foreach (var font in collectionLevelFonts.Reverse()) // reverse because we're inserting at the beginning each time
                {
                    if (!stylesAndFonts.Exists(s => s.fontName == font.fontName))
                    {
                        var languageName = GetLanguageName(font.languageTag);
                        stylesAndFonts.Insert(
                            0,
                            new StyleAndFont
                            {
                                styleName = string.Format(
                                    LocalizationManager.GetString(
                                        "CollectionSettingsDialog.BookMakingTab.DefaultFontFor",
                                        "Default Font for {0}",
                                        "{0} is a language name."
                                    ),
                                    languageName
                                ),
                                languageName = languageName,
                                fontName = font.fontName,
                            }
                        );
                    }
                }
            }
            var jsonData = JsonConvert.SerializeObject(stylesAndFonts.ToArray());
            request.ReplyWithJson(jsonData);
        }

        // Gets the English Display name for the default styles that are used in Bloom code
        // Changes here should be reflected in StyleEditor.ts: StyleEditor.getDisplayName().
        // Changes here should be reflected in the Bloom.xlf file too.
        private string GetEnglishStyleName(string ruleId)
        {
            switch (ruleId)
            {
                case "BigWords":
                    return "Big Words";
                case "Cover-Default":
                    return "Cover Default";
                case "Credits-Page":
                    return "Credits Page";
                case "Heading1":
                    return "Heading 1";
                case "Heading2":
                    return "Heading 2";
                case "normal":
                    return "Normal";
                case "Title-On-Cover":
                    return "Title On Cover";
                case "Title-On-Title-Page":
                    return "Title On Title Page";
                case "ImageDescriptionEdit":
                    return "Image Description Edit";
                case "QuizHeader":
                    return "Quiz Header";
                case "QuizQuestion":
                    return "Quiz Question";
                case "QuizAnswer":
                    return "Quiz Answer";
                case "Equation": // If the id is the same as the English, just fall through to default.
                default:
                    return ruleId;
            }
        }

        private static Dictionary<string, string> GetLanguagesAndDefaultFonts(
            Dictionary<string, List<string>> fontToLangs,
            Book.Book book,
            HtmlDom dom
        )
        {
            var defaultLangStylesPath = Path.Combine(book.FolderPath, "defaultLangStyles.css");
            var langToFont = dom.GetDefaultFontsForLanguages(
                Path.GetDirectoryName(defaultLangStylesPath)
            );
            if (langToFont == null)
            {
                // This branch is probably never taken, but it's here just in case.
                var settings = book.CollectionSettings;
                langToFont = new Dictionary<string, string>();
                if (
                    !string.IsNullOrEmpty(settings.Language1Tag)
                    && !string.IsNullOrEmpty(settings.Language1.FontName)
                )
                    langToFont.Add(settings.Language1Tag, settings.Language1.FontName);
                if (
                    !string.IsNullOrEmpty(settings.Language2Tag)
                    && !string.IsNullOrEmpty(settings.Language2.FontName)
                )
                    langToFont[settings.Language2Tag] = settings.Language2.FontName;
                if (
                    !string.IsNullOrEmpty(settings.Language3Tag)
                    && !string.IsNullOrEmpty(settings.Language3.FontName)
                )
                    langToFont[settings.Language3Tag] = settings.Language3.FontName;
            }
            foreach (var pair in langToFont)
            {
                if (!fontToLangs.ContainsKey(pair.Value))
                    fontToLangs.Add(pair.Value, new List<string>());
                fontToLangs[pair.Value].Add(pair.Key);
            }
            return langToFont;
        }

        private static Dictionary<string, PageInfo> GetStylesUsedInBook(
            HtmlDom dom,
            string bookFolderPath
        )
        {
            var stylesInBook = new Dictionary<string, PageInfo>();
            foreach (
                var div in dom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-page')]//div[contains(@class, '-style')]"
                )
            )
            {
                var classList = div.GetAttribute("class");
                var classes = classList.Split(' ');
                var style = classes.First(c => c.EndsWith("-style")).Replace("-style", "");
                // These are not offered by the Styles Dialog, so I guess users aren't supposed to be aware of them.
                // Presumably xmatter developers want complete control over these.
                if (
                    style == "Inside-Front-Cover"
                    || style == "Inside-Back-Cover"
                    || style == "Outside-Back-Cover"
                )
                    continue;
                var pageDiv = div.SelectSingleNode("ancestor::div[contains(@class,'bloom-page')]");
                if (!stylesInBook.Keys.Contains(style))
                {
                    // ENHANCE: distinguish first pages for each language.
                    stylesInBook.Add(
                        style,
                        new PageInfo
                        {
                            id = pageDiv.GetAttribute("id"),
                            description = GetPageDescription(pageDiv),
                        }
                    );
                }
            }
            return stylesInBook;
        }

        private static string GetPageDescription(SafeXmlNode pageDiv)
        {
            var xmatter = pageDiv.GetAttribute("data-xmatter-page");
            if (!String.IsNullOrEmpty(xmatter))
            {
                var englishLabel = GetEnglishForXmatterPages(xmatter);
                return L10NSharp.LocalizationManager.GetDynamicString(
                    "Bloom",
                    $"TemplateBooks.PageLabel.{englishLabel}",
                    englishLabel
                );
            }
            var pageNumber = pageDiv.GetAttribute("data-page-number");
            if (!String.IsNullOrEmpty(pageNumber))
            {
                var fmt = L10NSharp.LocalizationManager.GetDynamicString(
                    "BloomMediumPriority",
                    "BookSettings.Fonts.PageNumber",
                    "Page {0}"
                );
                return string.Format(fmt, pageNumber);
            }
            return L10NSharp.LocalizationManager.GetDynamicString(
                "BloomMediumPriority",
                "BookSettings.Fonts.UnnumberedPage",
                "Unnumbered Page"
            );
        }

        // These should be kept in sync with the entries for TemplateBooks.PageLabel.* in Bloom.xlf.
        private static string GetEnglishForXmatterPages(string xmatter)
        {
            switch (xmatter)
            {
                case "credits":
                    return "Credits Page";
                case "frontCover":
                    return "Front Cover";
                case "insideBackCover":
                    return "Inside Back Cover";
                case "insideFrontCover":
                    return "Inside Front Cover";
                case "outsideBackCover":
                    return "Outside Back Cover";
                case "titlePage":
                    return "Title Page";
                default:
                    return xmatter;
            }
        }

        private static List<StyleAndFont> GetUserModifiedStylesAndFonts(HtmlDom dom)
        {
            var stylesAndFonts = new List<StyleAndFont>();
            var userModifiedStyles = HtmlDom.GetUserModifiedStyleElement(dom.Head);
            if (userModifiedStyles != null)
            {
                foreach (
                    Match match in HtmlDom.UserModifiedStyleFontRegex.Matches(
                        userModifiedStyles.InnerText
                    )
                )
                {
                    if (string.IsNullOrEmpty(match.Groups[6].Value))
                        continue; // no font-family specified in this rule
                    var styleAndFont = new StyleAndFont();
                    styleAndFont.style = match.Groups[1].Value;
                    // Note: an earlier version of this code apparently attempted to set this to
                    // asterisk if there was no match for Groups[4], but wrongly expected it to be
                    // null when there was no match. In fact, it is an empty string when there is
                    // no match, so I (JT) fixed it. However, it's remotely possible that something
                    // somewhere depended on the previous mistake.
                    var langTag = match.Groups[4].Value;
                    styleAndFont.languageTag = string.IsNullOrEmpty(langTag) ? "*" : langTag;
                    styleAndFont.fontName = match
                        .Groups[7]
                        .Value.Replace("!important", "")
                        .Trim(new[] { ' ', '\t', '"', '\'' });
                    stylesAndFonts.Add(styleAndFont);
                }
            }
            return stylesAndFonts;
        }

        private string GetLanguageName(string langTag)
        {
            if (langTag == "*")
                return L10NSharp.LocalizationManager.GetString("BookSettings.Fonts.All", "(all)");
            var settings = _bookSelection.CurrentSelection.CollectionSettings;
            var language = settings.AllLanguages.Find(x => x.Tag == langTag);
            if (language != null)
                return language.Name;
            if (langTag == settings.SignLanguageTag)
                return settings.SignLanguage.Name;
            if (langTag.Contains(","))
            {
                var tags = langTag.Split(",");
                var names = tags.Select(t => GetLanguageName(t)).ToList();
                return string.Join(", ", names);
            }
            var ws = new Collection.WritingSystem(() => settings.Language2Tag);
            ws.Tag = langTag;
            return ws.GetNameInLanguage(settings.Language2Tag);
        }
    }
}
