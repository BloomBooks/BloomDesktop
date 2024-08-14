using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Edit;
using Bloom.web.controllers;
using Newtonsoft.Json;
using SIL.IO;
using System.Text.RegularExpressions;
using SIL.Extensions;
using Bloom.SafeXml;
using System.Windows;

namespace Bloom.Api
{
    /// <summary>
    /// Exposes some settings of the current Book via API
    /// </summary>
    public class BookSettingsApi
    {
        private readonly BookSelection _bookSelection;
        private readonly PageRefreshEvent _pageRefreshEvent;
        private readonly BookRefreshEvent _bookRefreshEvent;
        private EditingView _editingView;

        public BookSettingsApi(
            BookSelection bookSelection,
            PageRefreshEvent pageRefreshEvent,
            BookRefreshEvent bookRefreshEvent,
            EditingView editingView
        )
        {
            _bookSelection = bookSelection;
            _pageRefreshEvent = pageRefreshEvent;
            _bookRefreshEvent = bookRefreshEvent;
            _editingView = editingView;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Not sure this needs UI thread, but it can result in saving the page, which seems
            // safest to do that way.
            apiHandler.RegisterEndpointHandler(
                "book/settings",
                HandleBookSettings,
                true /* review */
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/appearanceUIOptions",
                HandleGetAvailableAppearanceUIOptions,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/overrides",
                HandleGetOverrides,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/deleteCustomBookStyles",
                HandleDeleteCustomBookStyles,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/stylesAndFonts",
                HandleStylesAndFonts,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "book/settings/jumpToPage",
                HandleJumpToPage,
                true);
        }

        private void HandleJumpToPage(ApiRequest request)
        {
            var pageId = request.GetPostStringOrNull();
            request.PostSucceeded();
            _editingView.Model.SaveThen(() => pageId, () => { });
        }

        private void HandleGetOverrides(ApiRequest request)
        {
            var x = new ExpandoObject() as IDictionary<string, object>;
            // The values set here should correspond to the declaration of IOverrideValues
            // in BookSettingsDialog.tsx.
            x["branding"] = _bookSelection.CurrentSelection.Storage.BrandingAppearanceSettings;
            x["xmatter"] = _bookSelection.CurrentSelection.Storage.XmatterAppearanceSettings;
            x["brandingName"] = _bookSelection
                .CurrentSelection
                .CollectionSettings
                .BrandingProjectKey;
            x["xmatterName"] = _bookSelection.CurrentSelection.CollectionSettings.XMatterPackName;
            request.ReplyWithJson(JsonConvert.SerializeObject(x));
        }

        private void HandleDeleteCustomBookStyles(ApiRequest request)
        {
            // filename is usually "customBookStyles.css" but could possibly be "customCollectionStyles.css"
            var filename = request.GetParamOrNull("file");
            if (filename == null)
            {
                request.Failed("No file specified");
                return;
            }
            RobustFile.Delete(Path.Combine(_bookSelection.CurrentSelection.FolderPath, filename));
            _bookSelection.CurrentSelection.SettingsUpdated();
            // We should only delete it when it's not in use, so we should not need to refresh the page.
            IndicatorInfoApi.NotifyIndicatorInfoChanged();
            request.PostSucceeded();
        }

        private void HandleGetAvailableAppearanceUIOptions(ApiRequest request)
        {
            request.ReplyWithJson(
                _bookSelection.CurrentSelection.BookInfo.AppearanceSettings.AppearanceUIOptions(
                    _bookSelection.CurrentSelection.Storage.LegacyThemeCanBeUsed
                )
            );
        }

        /// <summary>
        /// Get a json of the book's settings.
        /// </summary>
        private void HandleBookSettings(ApiRequest request)
        {
            switch (request.HttpMethod)
            {
                case HttpMethods.Get:
                    var settings = new
                    {
                        currentToolBoxTool = _bookSelection.CurrentSelection.BookInfo.CurrentTool,
                        //bloomPUB = new { imageSettings = new { maxWidth= _bookSelection.CurrentSelection.BookInfo.PublishSettings.BloomPub.ImageSettings.MaxWidth, maxHeight= _bookSelection.CurrentSelection.BookInfo.PublishSettings.BloomPub.ImageSettings.MaxHeight} }
                        publish = _bookSelection.CurrentSelection.BookInfo.PublishSettings,
                        appearance = _bookSelection
                            .CurrentSelection
                            .BookInfo
                            .AppearanceSettings
                            .GetCopyOfProperties
                    };
                    // The book settings dialog wants to edit the content language visibility as if it was just another
                    // appearance setting. But we have another control that manipulates it, and a long-standing place to
                    // store it that is NOT in appearance.json. So for this purpose we pretend it is a set of three
                    // appearance settings that follow the pattern for controlling which languages are shown for a field.
                    var appearance = (settings.appearance as IDictionary<string, object>);
                    //var collectionLangs = _bookSelection.CurrentSelection.CollectionSettings.LanguagesZeroBased;
                    var bookLangs = new HashSet<string>();
                    if (_bookSelection.CurrentSelection.Language1Tag != null)
                    {
                        bookLangs.Add(_bookSelection.CurrentSelection.Language1Tag);
                    }

                    if (_bookSelection.CurrentSelection.Language2Tag != null)
                    {
                        bookLangs.Add(_bookSelection.CurrentSelection.Language2Tag);
                    }
                    if (_bookSelection.CurrentSelection.Language3Tag != null)
                    {
                        bookLangs.Add(_bookSelection.CurrentSelection.Language3Tag);
                    }

                    appearance["autoTextBox-L1-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language1Tag
                    );
                    appearance["autoTextBox-L2-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language2Tag
                    );
                    appearance["autoTextBox-L3-show"] = bookLangs.Contains(
                        _bookSelection.CurrentSelection.CollectionSettings.Language3Tag
                    );
                    var jsonData = JsonConvert.SerializeObject(settings);

                    request.ReplyWithJson(jsonData);
                    break;
                case HttpMethods.Post:
                    var json = request.RequiredPostJson();
                    dynamic newSettings = Newtonsoft.Json.Linq.JObject.Parse(json);
                    //var c = newSettings.appearance.cover.coverColor;
                    //_bookSelection.CurrentSelection.SetCoverColor(c.ToString());
                    // review: crazy bit here, that above I'm taking json, parsing it into and object, and grabbing part of it. But then
                    // here we take it back to json and pass it to this thing that is going to parse it again. In this case, speed
                    // is irrelevant. The nice thing is, it retains the identity of PublishSettings in case someone is holding onto it.
                    var jsonOfJustPublishSettings = JsonConvert.SerializeObject(
                        newSettings.publish
                    );
                    _bookSelection.CurrentSelection.BookInfo.PublishSettings.LoadNewJson(
                        jsonOfJustPublishSettings
                    );
                    // Now we need to extract the content language visibility settings and remove them from what gets saved
                    // as the appearance settings.
                    var newAppearance = newSettings.appearance;
                    var showL1 = newAppearance["autoTextBox-L1-show"].Value;
                    newAppearance.Remove("autoTextBox-L1-show");
                    var showL2 = newAppearance["autoTextBox-L2-show"].Value;
                    newAppearance.Remove("autoTextBox-L2-show");
                    var showL3 = newAppearance["autoTextBox-L3-show"].Value;
                    newAppearance.Remove("autoTextBox-L3-show");
                    // Things get a little complex here. The three values we just computed indicate the desired visibility
                    // of the three collection languages. But L2 may well be the same as L1, and conceivably L3 might be the
                    // the same as L1 or L2 or both. If so, the controls that would be for duplicate languages are not shown,
                    // and their values are not updated. Worse, we are about to call SetActiveLanguages, and its arguments
                    // control the visibility of items in a de-duplicated list of languages.
                    // This seems as though it would have a more complicated effect than it actually does. We always show
                    // the control for L1, so showL1 is always valid. The third argument is only relevant if there are
                    // three distinct languages, so we can always pass showL3. The second argument is the tricky one:
                    // if L2 is the same as L1, then arg2 controls the visibility of L3, so we must pass showL3 (and ignore
                    // showL2, which is meaningless). If they are different, then showL2 controls the visibility of L2.
                    // (If all three are the same, the second and third arguments are irrelevant.
                    // If L3 is the same as L1 or L2, but L1 and L2 are different, then showL3 is meaningless, but also ignored,
                    // since there are only two languages and showL1 and showL2 are the only relevant arguments.)
                    var tag1 = _bookSelection.CurrentSelection.CollectionSettings.Language1Tag;
                    var tag2 = _bookSelection.CurrentSelection.CollectionSettings.Language2Tag;
                    _editingView.SetActiveLanguages(showL1, tag1 == tag2 ? showL3 : showL2, showL3);
                    // Todo: save the content languages
                    _bookSelection.CurrentSelection.BookInfo.AppearanceSettings.UpdateFromDynamic(
                        newAppearance
                    );

                    _bookSelection.CurrentSelection.SettingsUpdated();

                    // we want a "full" save, which means that the <links> in the <head> can be regenerated, i.e. in response
                    // to a change in the CssTheme from/to legacy that requires changing between "basePage.css" and "basePage-legacy-5-6.css"
                    _pageRefreshEvent.Raise(
                        PageRefreshEvent.SaveBehavior.SaveBeforeRefreshFullSave
                    );
                    IndicatorInfoApi.NotifyIndicatorInfoChanged();

                    request.PostSucceeded();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private bool GetIsBookATemplate()
        {
            return _bookSelection.CurrentSelection.IsSuitableForMakingShells;
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

        private void HandleStylesAndFonts(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            // Get all the styles used in the book.
            var stylesInBook = GetStylesUsedInBook(book.OurHtmlDom, book.FolderPath);
            // Get all the languages used in the book and their default fonts.
            var fontToLangs = new Dictionary<string, List<string>>();
            var langToFont = GetLanguagesAndDefaultFonts(fontToLangs, book, book.OurHtmlDom);
            // Get all the user-modified styles that set a font for the style.
            var stylesAndFonts = GetUserModifiedStylesAndFonts(book.OurHtmlDom);
            // Add the styles that are in the book but not covered by the user-modified styles.
            foreach (var style in stylesInBook.Keys)
            {
                var existing = stylesAndFonts.FindAll(s => s.style == style).ToArray();
                if (existing.Length == 0)
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
                    stylesAndFonts.Add(styleAndFont);
                    styleAndFont.pageId = stylesInBook[style].id;
                    styleAndFont.pageDescription = stylesInBook[style].description;
                }
                else
                {
                    foreach (var tag in langToFont.Keys)
                    {
                        if (existing.Any(s => s.languageTag == tag))
                            continue;
                        var styleAndFont = new StyleAndFont();
                        styleAndFont.style = style;
                        styleAndFont.languageTag = tag;
                        styleAndFont.fontName = langToFont[tag];
                        styleAndFont.pageId = stylesInBook[style].id;
                        styleAndFont.pageDescription = stylesInBook[style].description;
                        stylesAndFonts.Add(styleAndFont);
                    }
                }
            }
            foreach (var styleAndFont in stylesAndFonts.ToArray())
            {
                if (styleAndFont.languageTag == "*")
                    styleAndFont.languageName = "*";
                else
                    styleAndFont.languageName = GetLanguageName(styleAndFont.languageTag);
                // If the style is not in the default styles, don't worry about not getting a localized name.
                styleAndFont.styleName = L10NSharp.LocalizationManager.GetString(
                    $"EditTab.FormatDialog.DefaultStyles.{styleAndFont.style}-style", GetEnglishStyleName(styleAndFont.style));
            }
            stylesAndFonts.Sort((a, b) => string.Compare(a.styleName, b.styleName, StringComparison.InvariantCultureIgnoreCase));
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

        private static Dictionary<string, string> GetLanguagesAndDefaultFonts(Dictionary<string, List<string>> fontToLangs, Book.Book book, HtmlDom dom)
        {
            var langToFont = new Dictionary<string, string>();
            var defaultLangStylesPath = Path.Combine(book.FolderPath, "defaultLangStyles.css");
            var languagesWithContent = dom.GetLanguagesWithContent().ToArray();
            if (RobustFile.Exists(defaultLangStylesPath))
            {
                var defaultLangStylesContent = RobustFile.ReadAllText(defaultLangStylesPath, System.Text.Encoding.UTF8);
                /* Pattern to match in the defaultLangStylesContent (whole text):
                [lang='en']
                {
                    font-family: 'Andika New Basic';
                    direction: ltr;
                }
                */
                Regex languageCssRegex = new Regex(
                    @"\[\s*lang\s*=\s*['""](.*?)['""]\s*\]\s*{[^}]*font-family:([^;]*);[^}]*}",
                    RegexOptions.Singleline | RegexOptions.Compiled
                );
                foreach (Match match in languageCssRegex.Matches(defaultLangStylesContent))
                {
                    var lang = match.Groups[1].Value;
                    var font = match.Groups[2].Value.Trim(new[] { ' ', '\t', '"', '\'' });
                    if (languagesWithContent.Contains(lang))
                        AddLangForFont(lang, font, langToFont, fontToLangs);
                }
            }
            else
            {
                // This branch is probably never taken, but it's here just in case.
                var settings = book.CollectionSettings;
                if (!string.IsNullOrEmpty(settings.Language1Tag) && !string.IsNullOrEmpty(settings.Language1.FontName))
                    AddLangForFont(settings.Language1Tag, settings.Language1.FontName, langToFont, fontToLangs);
                if (!string.IsNullOrEmpty(settings.Language2Tag) && !string.IsNullOrEmpty(settings.Language2.FontName))
                    AddLangForFont(settings.Language2Tag, settings.Language2.FontName, langToFont, fontToLangs);
                if (!string.IsNullOrEmpty(settings.Language3Tag) && !string.IsNullOrEmpty(settings.Language3.FontName))
                    AddLangForFont(settings.Language3Tag, settings.Language3.FontName, langToFont, fontToLangs);
            }
            return langToFont;
        }

        private static Dictionary<string,PageInfo> GetStylesUsedInBook(HtmlDom dom, string bookFolderPath)
        {
            var stylesInBook = new Dictionary<string, PageInfo>();
            foreach (var div in dom.SafeSelectNodes("//div[contains(@class,'bloom-page')]//div[contains(@class, '-style')]"))
            {
                var classList = div.GetAttribute("class");
                var classes = classList.Split(' ');
                var style = classes.First(c => c.EndsWith("-style")).Replace("-style", "");
                // These are not offered by the Styles Dialog, so I guess users aren't supposed to be aware of them.
                // Presumably xmatter developers want complete control over these.
                if (style == "Inside-Front-Cover" || style == "Inside-Back-Cover" || style == "Outside-Back-Cover")
                    continue;
                var pageDiv = div.SelectSingleNode("ancestor::div[contains(@class,'bloom-page')]");
                if (!stylesInBook.Keys.Contains(style))
                {
                    // ENHANCE: distinguish first pages for each language.
                    stylesInBook.Add(style, new PageInfo { id=pageDiv.GetAttribute("id"), description=GetPageDescription(pageDiv)});
                }
            }
            return stylesInBook;
        }

        private static string GetPageDescription(SafeXmlNode pageDiv)
        {
            var xmatter = pageDiv.GetAttribute("data-xmatter-page");
            if (!String.IsNullOrEmpty(xmatter))
            {
                return L10NSharp.LocalizationManager.GetDynamicString("BloomMediumPriority",
                    $"BookSettings.Fonts.{xmatter}", GetEnglishForXmatterPages(xmatter));
            }
            var pageNumber = pageDiv.GetAttribute("data-page-number");
            if (!String.IsNullOrEmpty(pageNumber))
            {
                var fmt = L10NSharp.LocalizationManager.GetDynamicString("BloomMediumPriority",
                    "BookSettings.Fonts.PageNumber", "Page {0}");
                return string.Format(fmt, pageNumber);
            }
            return L10NSharp.LocalizationManager.GetDynamicString("BloomMediumPriority",
                "BookSettings.Fonts.UnnumberedPage", "Unnumbered Page");
        }

        // These should be kept in sync with the entries in BloomMediumPriority.xlf.
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
                    return "Back Cover";
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
                /* Patterns to match in the userModifiedStyles (line by line):
                .BigWords-style { font-size: 45pt !important; text-align: center !important; }
                .BigWords-style[lang="en"] { font-family: Cambria !important; }
                .normal-style[lang="en"] { font-size: 13pt !important; font-family: "Charis SIL Literacy AmArea" !important; }
                .normal-style { font-size: 13pt !important; }
                .TextToMatch-style[lang="es"] { font-size: 14pt !important; font-family: ABeeZee !important; }
                */
                var regex = new Regex(@"\.([^\s]+)-style(\[lang=""([A-Za-z-]*)""\])?\s*{[^}]*font-family:([^};]*)[^}]*}");
                foreach (Match match in regex.Matches(userModifiedStyles.InnerText))
                {
                    var styleAndFont = new StyleAndFont();
                    styleAndFont.style = match.Groups[1].Value;
                    styleAndFont.languageTag = match.Groups[3]?.Value ?? "*";
                    styleAndFont.fontName = match.Groups[4].Value.Replace("!important", "").Trim(new[] { ' ', '\t', '"', '\'' });
                    stylesAndFonts.Add(styleAndFont);
                }
            }
            return stylesAndFonts;
        }

        private static void AddLangForFont(string langTag, string font,
            Dictionary<string, string> langToFont, Dictionary<string, List<string>> fontToLangs)
        {
            if (!string.IsNullOrEmpty(langTag) && !string.IsNullOrEmpty(font) && !langToFont.ContainsKey(langTag))
            {
                langToFont.Add(langTag, font);
                if (!fontToLangs.ContainsKey(font))
                    fontToLangs.Add(font, new List<string>());
                fontToLangs[font].Add(langTag);
            }
        }

        private string GetLanguageName(string langTag)
        {
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
