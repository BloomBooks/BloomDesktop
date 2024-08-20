using Bloom.Api;
using Bloom.Book;
using Bloom.SafeXml;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
        }

        public struct PageInfo
        {
            public string id;
            public string description;
        }

        private void HandleGetDataRows(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            // Get all the styles used in the book.
            var stylesInBook = GetStylesUsedInBook(book.OurHtmlDom, book.FolderPath);
            // Get all the languages used in the book and their default fonts.
            var fontToLangs = new Dictionary<string, List<string>>();
            var langToFont = GetLanguagesAndDefaultFonts(fontToLangs, book, book.OurHtmlDom);
            // Get all the user-modified styles that set a font for the style.
            var modifiedStylesAndFonts = GetUserModifiedStylesAndFonts(book.OurHtmlDom);
            // Add the styles that are in the book but not covered by the user-modified styles.
            var stylesAndFonts = new List<StyleAndFont>();
            foreach (var style in stylesInBook.Keys)
            {
                var modified = modifiedStylesAndFonts.FindAll(s => s.style == style).ToArray();
                if (modified.Length == 0)
                {
                    var styleAndFont = new StyleAndFont();
                    styleAndFont.style = style;
                    if (fontToLangs.Count == 1)
                    {
                        if (fontToLangs.First().Value.Count == 1)
                            styleAndFont.languageTag = fontToLangs.First().Value.First();
                        else
                            styleAndFont.languageTag = "*";
                        styleAndFont.fontName = fontToLangs.First().Key;
                    }
                    styleAndFont.pageId = stylesInBook[style].id;
                    styleAndFont.pageDescription = stylesInBook[style].description;
                    stylesAndFonts.Add(styleAndFont);
                }
                else
                {
                    foreach (var tag in langToFont.Keys)
                    {
                        var styleAndFont = new StyleAndFont();
                        styleAndFont.style = style;
                        styleAndFont.languageTag = tag;
                        var modifiedForLang = modified.FirstOrDefault(
                            s => s.languageTag == tag && !string.IsNullOrEmpty(s.fontName)
                        );
                        if (modifiedForLang != null)
                            styleAndFont.fontName = modifiedForLang.fontName;
                        else if (
                            modified.Length == 1 && !string.IsNullOrEmpty(modified[0].fontName)
                        )
                            styleAndFont.fontName = modified[0].fontName;
                        else
                            styleAndFont.fontName = langToFont[tag];
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
                    !stylesAndFonts.Exists(
                        s =>
                            s.style == styleAndFont.style
                            && s.languageTag == styleAndFont.languageTag
                    )
                )
                {
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
                            description = GetPageDescription(pageDiv)
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
                    styleAndFont.languageTag = match.Groups[4]?.Value ?? "*";
                    styleAndFont.fontName = match.Groups[7].Value
                        .Replace("!important", "")
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
            if (langTag == settings.Language1Tag)
                return settings.Language1.Name;
            if (langTag == settings.Language2Tag)
                return settings.Language2.Name;
            if (langTag == settings.Language3Tag)
                return settings.Language3.Name;
            var ws = new Collection.WritingSystem(99, () => settings.Language2Tag);
            ws.Tag = langTag;
            return ws.Name;
        }
    }
}
