using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.IO;
using SIL.Progress;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Used by the settings dialog (currently just the EnterpriseSettings tab) and various places that need to know
    /// if Enterprise is enabled or not.
    /// </summary>
    public class CollectionSettingsApi
    {
        public const string kApiUrlPart = "settings/";

        // These options must match the strings used in accessibileImage.tsx
        public enum EnterpriseStatus
        {
            None,
            Community,
            Subscription
        }

        // These are static so they can easily be set by the collection settings dialog using SetSubscriptionCode()
        private static string SubscriptionCode { get; set; }
        private static DateTime _enterpriseExpiry = DateTime.MinValue;

        // True if the part of the subscription code that identifies the branding is one this version of Bloom knows about
        private static bool _knownBrandingInSubscriptionCode = false;
        private static EnterpriseStatus _enterpriseStatus;

        // While displaying the CollectionSettingsDialog, which is what this API mainly exists to serve
        // we keep a reference to it here so pending settings can be updated there.
        public static CollectionSettingsDialog DialogBeingEdited;

        // When in FixEnterpriseSubscriptionCodeMode, and we think it is a legacy branding problem
        // (because the subscription code is missing or incomplete rather than wrong or expired or unknown),
        // this keeps track of the branding the collection file specified but which was not validated by a current code.
        public static string LegacyBrandingName { get; set; }

        private readonly CollectionSettings _collectionSettings;
        private readonly List<object> _numberingStyles = new List<object>();
        private readonly XMatterPackFinder _xmatterPackFinder;
        private readonly BookSelection _bookSelection;

        public CollectionSettingsApi(
            CollectionSettings collectionSettings,
            XMatterPackFinder xmatterPackFinder,
            BookSelection bookSelection
        )
        {
            _collectionSettings = collectionSettings;
            _xmatterPackFinder = xmatterPackFinder;
            this._bookSelection = bookSelection;
            SetSubscriptionCode(
                _collectionSettings.SubscriptionCode,
                _collectionSettings.IsSubscriptionCodeKnown(),
                _collectionSettings.GetEnterpriseStatus(false)
            );
        }

        private bool IsEnterpriseEnabled(bool failIfLockedToOneBook)
        {
            if (failIfLockedToOneBook && _collectionSettings.LockedToOneDownloadedBook)
                return false;
            return _collectionSettings.HaveEnterpriseFeatures;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "collection/settings",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        // Just a placeholder for the skeleton dialog for now.
                        request.ReplyWithJson("{}");
                    }
                    else if (request.HttpMethod == HttpMethods.Post)
                    {
                        request.PostSucceeded();
                    }
                },
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "lockedToOneDownloadedBook",
                request => _collectionSettings.LockedToOneDownloadedBook,
                null,
                false
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "enterpriseEnabled",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        lock (request)
                        {
                            // Some things (currently only creating a Team Collection) are not allowed if we're only
                            // in enterprise mode as a concession to allowing editing of a book that was downloaded
                            // for direct editing.
                            var failIfLockedToOneBook =
                                (request.GetParamOrNull("failIfLockedToOneBook") ?? "false")
                                == "true";
                            request.ReplyWithBoolean(IsEnterpriseEnabled(failIfLockedToOneBook));
                        }
                    }
                    else // post
                    {
                        System.Diagnostics.Debug.Fail(
                            "We shouldn't ever be using the 'post' version."
                        );
                        request.PostSucceeded();
                    }
                },
                true
            );
            apiHandler.RegisterEnumEndpointHandler(
                kApiUrlPart + "enterpriseStatus",
                request => _enterpriseStatus,
                (request, status) =>
                {
                    _enterpriseStatus = status;
                    if (_enterpriseStatus == EnterpriseStatus.None)
                    {
                        _knownBrandingInSubscriptionCode = true;
                        ResetBookshelf();
                        BrandingChangeHandler("Default", null);
                    }
                    else if (_enterpriseStatus == EnterpriseStatus.Community)
                    {
                        ResetBookshelf();
                        BrandingChangeHandler("Local-Community", null);
                    }
                    else
                    {
                        BrandingChangeHandler(
                            GetBrandingFromCode(SubscriptionCode),
                            SubscriptionCode
                        );
                    }
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "legacyBrandingName",
                request =>
                {
                    request.ReplyWithText(LegacyBrandingName ?? "");
                },
                false
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscriptionCode",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithText(SubscriptionCode ?? "");
                    }
                    else // post
                    {
                        var requestData = DynamicJson.Parse(request.RequiredPostJson());
                        SubscriptionCode = requestData.subscriptionCode;
                        _enterpriseExpiry = GetExpirationDate(SubscriptionCode);
                        var newBranding = GetBrandingFromCode(SubscriptionCode);
                        var oldBranding = !string.IsNullOrEmpty(_collectionSettings.InvalidBranding)
                            ? _collectionSettings.InvalidBranding
                            : "";
                        // If the user has entered a different subscription code then what was previously saved, we
                        // generally want to clear out the Bookshelf. But if the BrandingKey is the same as the old one,
                        // we'll leave it alone, since they probably renewed for another year or so and want to use the
                        // same bookshelf.
                        if (
                            SubscriptionCode != _collectionSettings.SubscriptionCode
                            && newBranding != oldBranding
                        )
                            ResetBookshelf();
                        if (_enterpriseExpiry < DateTime.Now) // expired or invalid
                        {
                            BrandingChangeHandler("Default", null);
                        }
                        else
                        {
                            _knownBrandingInSubscriptionCode = BrandingChangeHandler(
                                GetBrandingFromCode(SubscriptionCode),
                                SubscriptionCode
                            );
                            if (!_knownBrandingInSubscriptionCode)
                            {
                                BrandingChangeHandler("Default", null); // Review: or just leave unchanged?
                            }
                        }
                        request.PostSucceeded();
                    }
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "enterpriseSummary",
                request =>
                {
                    string branding = "";
                    if (_enterpriseStatus == EnterpriseStatus.Community)
                        branding = "Local-Community";
                    else if (_enterpriseStatus == EnterpriseStatus.Subscription)
                        branding =
                            _enterpriseExpiry == DateTime.MinValue
                                ? ""
                                : GetBrandingFromCode(SubscriptionCode);
                    var html = GetSummaryHtml(branding);
                    request.ReplyWithText(html);
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "enterpriseExpiry",
                request =>
                {
                    if (_enterpriseExpiry == DateTime.MinValue)
                    {
                        if (SubscriptionCodeLooksIncomplete(SubscriptionCode))
                            request.ReplyWithText("incomplete");
                        else
                            request.ReplyWithText("null");
                    }
                    else if (_knownBrandingInSubscriptionCode)
                    {
                        // O is ISO 8601, the only format I can find that C# ToString() can produce and JS is guaranteed to parse.
                        // See https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Date/parse
                        request.ReplyWithText(
                            _enterpriseExpiry.ToString("O", CultureInfo.InvariantCulture)
                        );
                    }
                    else
                    {
                        request.ReplyWithText("unknown");
                    }
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "hasSubscriptionFiles",
                request =>
                {
                    var haveFiles = BrandingProject.HaveFilesForBranding(
                        GetBrandingFromCode(SubscriptionCode)
                    );
                    if (haveFiles)
                        request.ReplyWithText("true");
                    else
                        request.ReplyWithText("false");
                },
                false
            );

            // Enhance: The get here has one signature {brandingProjectName, defaultBookshelf} while the post has another (defaultBookshelfId:string).
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "bookShelfData",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        var brandingProjectName = _collectionSettings.BrandingProjectKey;
                        var defaultBookshelfUrlKey = _collectionSettings.DefaultBookshelf;
                        request.ReplyWithJson(new { brandingProjectName, defaultBookshelfUrlKey });
                    }
                    else
                    {
                        // post: doesn't include the brandingProjectName, as this is not where we edit that.
                        var newShelf = request.RequiredPostString();
                        if (newShelf == "none")
                            newShelf = ""; // RequiredPostString won't allow us to just pass this
                        if (DialogBeingEdited != null)
                            DialogBeingEdited.PendingDefaultBookshelf = newShelf;
                        request.PostSucceeded();
                    }
                },
                false
            );
            // Calls to handle communication with new FontScriptControl on Book Making tab
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "specialScriptSettings",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                        return; // Should be a post
                    // Should have a (1-based) language number.
                    var data = DynamicJson.Parse(request.RequiredPostJson());
                    var languageNumber = (int)data.languageNumber;
                    HandlePendingFontSettings(languageNumber);
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "setFontForLanguage",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                        return; // Should be a post

                    // Should contain a 1-based language number and a font name
                    var data = DynamicJson.Parse(request.RequiredPostJson());
                    var languageNumber = (int)data.languageNumber;
                    var fontName = (string)data.fontName;
                    UpdatePendingFontName(fontName, languageNumber);
                    request.PostSucceeded();
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "currentFontData",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                        return; // Should be a get

                    // We want to return the data (languageName/fontName) for each active collection language
                    request.ReplyWithJson(GetLanguageData());
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "numberingStyle",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        // Should return all available numbering styles and the current style
                        request.ReplyWithJson(GetNumberingStyleData());
                    }
                    else
                    {
                        // We are receiving a pending numbering style change
                        var newNumberingStyle = request.RequiredPostString();
                        UpdatePendingNumberingStyle(newNumberingStyle);
                        request.PostSucceeded();
                    }
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "branding",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithJson(_collectionSettings.BrandingProjectKey);
                    }
                    else
                    {
                        // At least as of 5.6, this is only used by the visual regression tests
                        // Normally, we require a restart to change branding.
                        var key = request.RequiredPostString();
                        _collectionSettings.BrandingProjectKey = key;
                        _bookSelection.CurrentSelection?.BringBookUpToDate(new NullProgress()); // in case we changed the book's branding
                        request.PostSucceeded();
                    }
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "xmatter",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        // Should return all available xMatters and the current selected xMatter
                        request.ReplyWithJson(SetupXMatterList());
                    }
                    else
                    {
                        // We are receiving a pending xMatter change
                        var newXmatter = request.RequiredPostString();
                        UpdatePendingXmatter(newXmatter);
                        request.PostSucceeded();
                    }
                },
                true
            );

            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "getCustomPaletteColors",
                HandleGetCustomColorsRequest,
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "addCustomPaletteColor",
                HandleAddCustomColor,
                false
            );
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "webGoal", HandleWebGoalRequest, true);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "languageData",
                HandleLanguageDataRequest,
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "languageNames",
                HandleGetLanguageNames,
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "administrators",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithText(_collectionSettings.AdministratorsDisplayString);
                    }
                    else if (request.HttpMethod == HttpMethods.Post)
                    {
                        UpdatePendingAdministratorEmails(request.GetPostStringOrNull());
                        request.PostSucceeded();
                    }
                },
                true
            );
        }

        private void ResetBookshelf()
        {
            if (DialogBeingEdited != null)
                DialogBeingEdited.PendingDefaultBookshelf = "";
        }

        // Used by BooksOnBlorgProgressBar.
        private void HandleWebGoalRequest(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Post)
                return; // Should be a get

            var goal = _collectionSettings.BooksOnWebGoal;
            request.ReplyWithText(goal.ToString());
        }

        // Used by BooksOnBlorgProgressBar.
        private void HandleLanguageDataRequest(ApiRequest request)
        {
            if (request.HttpMethod == HttpMethods.Post)
                return; // Should be a get

            var languageName = _collectionSettings.GetLanguageName(
                _collectionSettings.Language1Tag,
                _collectionSettings.Language1Tag
            );
            var langTag = _collectionSettings.Language1Tag;
            // But if we have a Sign Language in the collection, use that for the Progress Bar.
            if (!string.IsNullOrEmpty(_collectionSettings.SignLanguageTag))
            {
                langTag = _collectionSettings.SignLanguageTag;
                languageName = _collectionSettings.GetLanguageName(
                    _collectionSettings.SignLanguageTag,
                    _collectionSettings.Language1Tag
                );
            }
            var jsonString =
                $"{{\"languageName\":\"{languageName}\",\"languageCode\":\"{langTag}\"}}";
            request.ReplyWithJson(jsonString);
        }

        // Used by BookSettingsDialog
        private void HandleGetLanguageNames(ApiRequest request)
        {
            var x = new ExpandoObject() as IDictionary<string, object>;
            // The values set here should correspond to the declaration of ILanguageNameValues
            // in BookSettingsDialog.tsx.
            x["language1Name"] = _bookSelection.CurrentSelection.CollectionSettings.Language1.Name;
            x["language2Name"] = _bookSelection.CurrentSelection.CollectionSettings.Language2.Name;
            if (
                !String.IsNullOrEmpty(
                    _bookSelection.CurrentSelection.CollectionSettings.Language3?.Name
                )
            )
                x["language3Name"] = _bookSelection
                    .CurrentSelection
                    .CollectionSettings
                    .Language3
                    .Name;
            request.ReplyWithJson(JsonConvert.SerializeObject(x));
        }

        private void HandleGetCustomColorsRequest(ApiRequest request)
        {
            var paletteKey = request.Parameters["palette"];
            var jsonString = _collectionSettings.GetColorPaletteAsJson(paletteKey);
            request.ReplyWithJson(jsonString);
        }

        private void HandleAddCustomColor(ApiRequest request)
        {
            var paletteTag = request.Parameters["palette"];
            var colorString = request.GetPostJson();
            _collectionSettings.AddColorToPalette(paletteTag, colorString);
            request.PostSucceeded();
        }

        private object SetupXMatterList()
        {
            var xmatterOfferings = new List<object>();

            string xmatterKeyForcedByBranding =
                _collectionSettings.GetXMatterPackNameSpecifiedByBrandingOrNull();

            var offerings = _xmatterPackFinder.GetXMattersToOfferInSettings(
                xmatterKeyForcedByBranding
            );

            foreach (var pack in offerings)
            {
                var labelToShow = LocalizationManager.GetDynamicString(
                    "Bloom",
                    "CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack."
                        + pack.EnglishLabel,
                    pack.EnglishLabel,
                    "Name of a Front/Back Matter Pack"
                );
                var description = pack.GetDescription(); // already localized, if available
                var item = new
                {
                    displayName = labelToShow,
                    internalName = pack.Key,
                    description
                };
                xmatterOfferings.Add(item);
            }

            // This will switch to the default factory xmatter if the current one is not valid.
            var currentXmatter = _xmatterPackFinder.GetValidXmatter(
                xmatterKeyForcedByBranding,
                _collectionSettings.XMatterPackName
            );

            return new { currentXmatter, xmatterOfferings = xmatterOfferings.ToArray() };
        }

        private object GetNumberingStyleData()
        {
            if (_numberingStyles.Count == 0)
            {
                foreach (var styleKey in CollectionSettings.CssNumberStylesToCultureOrDigits.Keys)
                {
                    var localizedStyle = LocalizationManager.GetString(
                        "CollectionSettingsDialog.BookMakingTab.PageNumberingStyle." + styleKey,
                        styleKey
                    );
                    _numberingStyles.Add(new { localizedStyle, styleKey });
                }
            }
            return new
            {
                currentPageNumberStyle = _collectionSettings.PageNumberStyle,
                numberingStyleData = _numberingStyles.ToArray()
            };
        }

        private object GetLanguageData()
        {
            var langData = new object[3];
            for (var i = 0; i < 3; i++)
            {
                if (
                    _collectionSettings.LanguagesZeroBased[i] == null
                    || string.IsNullOrEmpty(_collectionSettings.LanguagesZeroBased[i].Name)
                )
                    continue;
                var name = _collectionSettings.LanguagesZeroBased[i].Name;
                var font = _collectionSettings.LanguagesZeroBased[i].FontName;
                langData[i] = new { languageName = name, fontName = font };
            }
            return langData;
        }

        // languageNumber is 1-based
        private void HandlePendingFontSettings(int languageNumber)
        {
            Guard.Against(
                languageNumber == 0,
                "'languageNumber' should be 1-based index, but is 0"
            );
            var zeroBasedLanguageNumber = languageNumber - 1;
            if (
                zeroBasedLanguageNumber == 2
                && _collectionSettings.LanguagesZeroBased[zeroBasedLanguageNumber] == null
            )
                return;
            if (DialogBeingEdited != null)
            {
                var needRestart = DialogBeingEdited.FontSettingsLinkClicked(
                    zeroBasedLanguageNumber
                );
                if (needRestart)
                    DialogBeingEdited.ChangeThatRequiresRestart();
            }
        }

        // languageNumber is 1-based
        private void UpdatePendingFontName(string fontName, int languageNumber)
        {
            Guard.Against(
                languageNumber == 0,
                "'languageNumber' should be 1-based index, but is 0"
            );

            var zeroBasedLanguageNumber = languageNumber - 1;
            if (
                zeroBasedLanguageNumber == 2
                && _collectionSettings.LanguagesZeroBased[zeroBasedLanguageNumber] == null
            )
                return;
            if (DialogBeingEdited != null)
                DialogBeingEdited.PendingFontSelections[zeroBasedLanguageNumber] = fontName;
            if (
                fontName != _collectionSettings.LanguagesZeroBased[zeroBasedLanguageNumber].FontName
            )
                DialogBeingEdited.ChangeThatRequiresRestart();
        }

        private void UpdatePendingNumberingStyle(string numberingStyle)
        {
            if (DialogBeingEdited != null)
            {
                DialogBeingEdited.PendingNumberingStyle = numberingStyle;
                if (numberingStyle != _collectionSettings.PageNumberStyle)
                    DialogBeingEdited.ChangeThatRequiresRestart();
            }
        }

        private void UpdatePendingXmatter(string xMatterChoice)
        {
            if (DialogBeingEdited != null)
            {
                DialogBeingEdited.PendingXmatter = xMatterChoice;
                if (xMatterChoice != _collectionSettings.XMatterPackName)
                    DialogBeingEdited.ChangeThatRequiresRestart();
            }
        }

        private void UpdatePendingAdministratorEmails(string emails)
        {
            if (DialogBeingEdited != null)
            {
                DialogBeingEdited.PendingAdministrators = emails;
                if (emails != _collectionSettings.AdministratorsDisplayString)
                    DialogBeingEdited.ChangeThatRequiresRestart();
            }
        }

        public static string GetSummaryHtml(string branding)
        {
            BrandingSettings.ParseBrandingKey(
                branding,
                out var baseKey,
                out var flavor,
                out var subUnitName
            );
            var summaryFile = BloomFileLocator.GetOptionalBrandingFile(baseKey, "summary.htm");
            if (summaryFile == null)
                return "";

            var html = RobustFile.ReadAllText(summaryFile, Encoding.UTF8);
            return html.Replace("{flavor}", flavor).Replace("SUBUNIT", subUnitName);
        }

        public void PrepareToShowDialog()
        {
            if (SubscriptionCodeLooksIncomplete(_collectionSettings.SubscriptionCode))
                LegacyBrandingName = _collectionSettings.InvalidBranding; // otherwise we won't show the legacy branding message, just bring up the dialog and show whatever's wrong.
            else
                LegacyBrandingName = "";
        }

        public static void DialogClosed()
        {
            LegacyBrandingName = "";
        }

        // CollectionSettingsDialog sets this so we can call back with results from the tab.
        public static Func<string, string, bool> BrandingChangeHandler;

        public static bool SubscriptionCodeLooksIncomplete(string input)
        {
            if (input == null)
                return true;
            var parts = input.Split('-');
            if (parts.Length < 3)
                return true; // less than the required three components
            int last = parts.Length - 1;
            int dummy;
            if (!Int32.TryParse(parts[last - 1], out dummy))
                return true; // If they haven't started typing numbers, assume they're still in the name part, which could include a hyphen
            // If they've typed one number, we expect another. (Might not be true...ethnos-360-guatemala is incomplete...)
            // So, we already know the second-last part is a number, only short numbers or empty last part qualify as incomplete now.
            // Moreover, for the whole thing to be incomplete in this case, the completed number must be the right length; otherwise,
            // we consider it definitely wrong.
            if (
                parts[last - 1].Length == 6
                && parts[last].Length < 4
                && (parts[last].Length == 0 || Int32.TryParse(parts[last], out dummy))
            )
                return true;

            return false;
        }

        // Parse a string like PNG-RISE-361769-363798 or SIL-LEAD-361769-363644,
        // generated by a private google spreadsheet. The two last elements are numbers;
        // the first is an encoding of an expiry date, the second is a simple hash of
        // the project name (case-insensitive) and the expiry date, used to make it
        // a little less trivial to fake codes. We're not aiming for something that
        // would be difficult for someone willing to take the trouble to read this code.
        public static DateTime GetExpirationDate(string input)
        {
            if (input == null)
                return DateTime.MinValue;
            var parts = input.Split('-');
            if (parts.Length < 3)
                return DateTime.MinValue;
            int last = parts.Length - 1;
            if (parts[last].Length != 4 || parts[last - 1].Length != 6)
                return DateTime.MinValue;
            int datePart;
            if (!Int32.TryParse(parts[last - 1], out datePart))
                return DateTime.MinValue;
            int combinedChecksum;
            if (!Int32.TryParse(parts[last], out combinedChecksum))
                return DateTime.MinValue;

            int checkSum = CheckSum(GetBrandingFromCode(input));
            if ((Math.Floor(Math.Sqrt(datePart)) + checkSum) % 10000 != combinedChecksum)
                return DateTime.MinValue;
            int dateNum = datePart + 40000; // days since Dec 30 1899
            return new DateTime(1899, 12, 30) + TimeSpan.FromDays(dateNum);
        }

        // From the same sort of code extract the project name,
        // everything up to the second-last hyphen.
        public static string GetBrandingFromCode(string input)
        {
            if (input == null)
                return "";
            var parts = input.Split('-').ToList();
            if (parts.Count < 3)
                return "";
            parts.RemoveAt(parts.Count - 1);
            parts.RemoveAt(parts.Count - 1);
            return string.Join("-", parts.ToArray());
        }

        // Must match the function associated with the code generation google sheet
        private static int CheckSum(string input)
        {
            var sum = 0;
            input = input.ToUpperInvariant();
            for (var i = 0; i < input.Length; i++)
            {
                sum += input[i] * i;
            }
            return sum;
        }

        // Used to initialize things in the constructor.
        //
        // Also used by the settings dialog to ensure things are initialized properly there for a special "legacy" case.
        public static void SetSubscriptionCode(string code, bool knownCode, EnterpriseStatus status)
        {
            SubscriptionCode = code;
            _enterpriseExpiry = GetExpirationDate(code);
            _knownBrandingInSubscriptionCode = knownCode;
            _enterpriseStatus = status;
        }
    }
}
