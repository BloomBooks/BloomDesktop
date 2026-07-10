using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Keyboarding;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using L10NSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.Code;
using SIL.IO;
using SIL.Progress;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Used by the settings dialog (currently just the Subscription Settings tab) and various places that need to know
    /// if a subscription is enabled or not.
    /// </summary>
    public class CollectionSettingsApi
    {
        public const string kApiUrlPart = "settings/";

        // While displaying the CollectionSettingsDialog, which is what this API mainly exists to serve
        // we keep a reference to it here so pending settings can be updated there.
        public static CollectionSettingsDialog DialogBeingEdited;

        private readonly CollectionSettings _collectionSettings;
        private readonly List<object> _numberingStyles = new List<object>();
        private readonly XMatterPackFinder _xmatterPackFinder;
        private readonly BookSelection _bookSelection;

        // Built lazily on first use of a keyboard-settings endpoint (see EnsureKeyboardServicesBuilt).
        private KeymanCloudClient _keymanCloudClient;
        private CollectionKeyboardCache _keyboardCache;
        private OsKeyboards _osKeyboards;

        public static event EventHandler<LanguageChangeEventArgs> LanguageChange;

        public CollectionSettingsApi(
            CollectionSettings collectionSettings,
            XMatterPackFinder xmatterPackFinder,
            BookSelection bookSelection
        )
        {
            _collectionSettings = collectionSettings;
            _xmatterPackFinder = xmatterPackFinder;
            this._bookSelection = bookSelection;
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
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "advancedProgramSettings",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithJson(
                            JsonConvert.SerializeObject(GetAdvancedSettingsData())
                        );
                    }
                    else
                    {
                        var dialog = DialogBeingEdited;
                        if (dialog != null)
                        {
                            StoreAdvancedSettingsData(request, dialog);
                        }
                        request.PostSucceeded();
                    }
                },
                true
            );
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "lockedToOneDownloadedBook",
                request => _collectionSettings.EditingABlorgBook,
                null,
                false
            );

            // Enhance: The get here has one signature {descriptor, defaultBookshelf} while the post has another (defaultBookshelfId:string).
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "bookShelfData",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        var subscriptionDescriptor = _collectionSettings.Subscription.Descriptor;
                        var defaultBookshelfUrlKey = _collectionSettings.DefaultBookshelf;
                        // Note that these variable names flow through as the object keys and must match the names expected by the client.
                        request.ReplyWithJson(
                            new { subscriptionDescriptor, defaultBookshelfUrlKey }
                        );
                    }
                    else
                    {
                        // post: doesn't include the descriptor, as this is not where we edit that.
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
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "changeLanguage",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                        return; // Should be a post
                    var data = DynamicJson.Parse(request.RequiredPostJson());
                    if (string.IsNullOrEmpty(data.LanguageTag))
                    {
                        // User clicked cancel. Clear all listeners for this dialog
                        UnsubscribeAllLanguageChangeListeners();
                    }
                    LanguageChange?.Invoke(
                        this,
                        new LanguageChangeEventArgs()
                        {
                            LanguageTag = data.LanguageTag,
                            DesiredName = data.DesiredName,
                            DefaultName = data.DefaultName,
                            Country = data.Country,
                        }
                    );
                    request.PostSucceeded();
                },
                true
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
            // Calls to handle communication with the KeyboardSection control on the Book Making tab
            // (plan item 6). Registered handleOnUiThread:false so the Keyman cloud search (network I/O,
            // up to a multi-second timeout when offline) doesn't freeze the settings dialog; the
            // UI-thread-only OS keyboard enumeration is marshalled back to the UI thread inside
            // GetKeyboardsForLanguage.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "keyboardsForLanguage",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                        return; // Should be a get
                    var languageNumber = int.Parse(request.Parameters["languageNumber"]);
                    request.ReplyWithJson(GetKeyboardsForLanguage(languageNumber));
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "setKeyboardForLanguage",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                        return; // Should be a post

                    // Should contain a 1-based language number and the raw keyboard setting string.
                    var data = DynamicJson.Parse(request.RequiredPostJson());
                    var languageNumber = (int)data.languageNumber;
                    var keyboard = (string)data.keyboard;
                    UpdatePendingKeyboard(keyboard, languageNumber);
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
                        request.ReplyWithJson(_collectionSettings.Subscription.Descriptor);
                    }
                    else
                    {
                        throw new NotImplementedException(
                            "We don't expect to be setting the branding key, ever. It flows from the subscription code."
                        );
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
            // // a "deprecated" subscription is one that used to be eternal but is now being phased out
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "deprecatedBrandingsExpiryDate",
                request =>
                {
                    request.ReplyWithText(
                        SubscriptionAndFeatures.Subscription.kExpiryDateForDeprecatedCodes
                    );
                },
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

        private object GetAdvancedSettingsData()
        {
            var dialog = DialogBeingEdited;
            var isAutoUpdateSupported =
                dialog?.ShowAutomaticallyUpdateOption
                ?? CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform;
            return new
            {
                values = new
                {
                    autoUpdate = dialog?.PendingAutomaticallyUpdate
                        ?? (isAutoUpdateSupported && Settings.Default.AutoUpdate),
                    showExperimentalBookSources = dialog?.PendingShowExperimentalBookSources
                        ?? ExperimentalFeatures.IsFeatureEnabled(
                            ExperimentalFeatures.kExperimentalSourceBooks
                        ),
                    allowTeamCollection = dialog?.PendingAllowTeamCollection
                        ?? ExperimentalFeatures.IsFeatureEnabled(
                            ExperimentalFeatures.kTeamCollections
                        ),
                    allowAppBuilder = dialog?.PendingAllowAppBuilder
                        ?? ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kAppBuilder),
                    showQrCode = dialog?.PendingShowQrCode
                        ?? _collectionSettings.ShowBlorgLanguageQrCode,
                    qrcodeCaption = dialog?.PendingBadgeQrCodeCaption
                        ?? _collectionSettings.BadgeQrCodeLabelLocalized,
                },
                showAutoUpdate = isAutoUpdateSupported,
                showExperimentalBookSourcesOption = dialog?.ShowExperimentalBookSourcesOption
                    ?? false,
                allowTeamCollectionEnabled = dialog?.AllowTeamCollectionOptionEnabled ?? true,
            };
        }

        private void StoreAdvancedSettingsData(ApiRequest request, CollectionSettingsDialog dialog)
        {
            var data = JObject.Parse(request.RequiredPostJson());

            var autoUpdateToken = data["autoUpdate"];
            if (autoUpdateToken != null)
                dialog.PendingAutomaticallyUpdate = autoUpdateToken.Value<bool>();

            var showExperimentalBookSourcesToken = data["showExperimentalBookSources"];
            if (showExperimentalBookSourcesToken != null)
                dialog.PendingShowExperimentalBookSources =
                    showExperimentalBookSourcesToken.Value<bool>();

            var allowTeamCollectionToken = data["allowTeamCollection"];
            if (allowTeamCollectionToken != null)
            {
                var allowTeamCollection = allowTeamCollectionToken.Value<bool>();
                var previousValue = dialog.PendingAllowTeamCollection;
                dialog.PendingAllowTeamCollection = allowTeamCollection;
                if (allowTeamCollection != previousValue)
                    dialog.ChangeThatRequiresRestart();
            }

            var allowAppBuilderToken = data["allowAppBuilder"];
            if (allowAppBuilderToken != null)
            {
                var allowAppBuilder = allowAppBuilderToken.Value<bool>();
                dialog.PendingAllowAppBuilder = allowAppBuilder;
            }

            var showQrCodeToken = data["showQrCode"];
            if (showQrCodeToken != null)
            {
                var showQrCode = showQrCodeToken.Value<bool>();
                var previousValue = dialog.PendingShowQrCode;
                dialog.PendingShowQrCode = showQrCode;
                // We don't really need a change as drastic as a restart, but I don't expect
                // this to change often and somehow the badge needs to get updated.
                if (showQrCode != previousValue)
                    dialog.ChangeThatRequiresRestart();
            }
            var qrcodeCaptionToken = data["qrcodeCaption"];
            if (qrcodeCaptionToken != null)
            {
                var qrcodeCaption = qrcodeCaptionToken.Value<string>();
                var previousValue = dialog.PendingBadgeQrCodeCaption;
                dialog.PendingBadgeQrCodeCaption = qrcodeCaption;
                // We don't really need a change as drastic as a restart, but I don't expect
                // this to change often and somehow the badge needs to get updated.
                if (qrcodeCaption != previousValue)
                    dialog.ChangeThatRequiresRestart();
            }
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

        // Used by BookSettingsDialog and others
        private void HandleGetLanguageNames(ApiRequest request)
        {
            var x = new ExpandoObject() as IDictionary<string, object>;
            // The values set here should correspond to the declaration of ILanguageNameValues
            // in BookSettingsDialog.tsx.
            x["language1Name"] = _bookSelection.CurrentSelection.CollectionSettings.Language1.Name;
            x["language1Tag"] = _bookSelection.CurrentSelection.CollectionSettings.Language1.Tag;
            x["language2Name"] = _bookSelection.CurrentSelection.CollectionSettings.Language2.Name;
            x["language2Tag"] = _bookSelection.CurrentSelection.CollectionSettings.Language2.Tag;
            if (
                !String.IsNullOrEmpty(
                    _bookSelection.CurrentSelection.CollectionSettings.Language3?.Name
                )
            )
            {
                x["language3Name"] = _bookSelection
                    .CurrentSelection
                    .CollectionSettings
                    .Language3
                    .Name;
                x["language3Tag"] = _bookSelection
                    .CurrentSelection
                    .CollectionSettings
                    .Language3
                    .Tag;
            }

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
                    description,
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
                numberingStyleData = _numberingStyles.ToArray(),
            };
        }

        private object GetLanguageData()
        {
            var langData = new object[3];
            for (var i = 0; i < 3; i++)
            {
                if (
                    _collectionSettings.AllLanguages[i] == null
                    || string.IsNullOrEmpty(_collectionSettings.AllLanguages[i].Name)
                )
                    continue;
                var name = _collectionSettings.AllLanguages[i].Name;
                var font = _collectionSettings.AllLanguages[i].FontName;
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
                && _collectionSettings.AllLanguages[zeroBasedLanguageNumber] == null
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
                && _collectionSettings.AllLanguages[zeroBasedLanguageNumber] == null
            )
                return;
            if (DialogBeingEdited != null)
                DialogBeingEdited.PendingFontSelections[zeroBasedLanguageNumber] = fontName;
            if (fontName != _collectionSettings.AllLanguages[zeroBasedLanguageNumber].FontName)
                DialogBeingEdited.ChangeThatRequiresRestart();
        }

        /// <summary>
        /// Wire up the Keyman cloud client, collection keyboard cache, and OS keyboard enumerator the
        /// first time a keyboard-settings endpoint is used. Mirrors the lazy setup in
        /// KeyboardingConfigApi, which serves the analogous edit-view endpoint.
        /// </summary>
        private void EnsureKeyboardServicesBuilt()
        {
            if (_osKeyboards != null)
                return;
            _keymanCloudClient = new KeymanCloudClient();
            _keyboardCache = new CollectionKeyboardCache(
                _collectionSettings.FolderPath,
                _keymanCloudClient
            );
            _osKeyboards = new OsKeyboards();
        }

        // languageNumber is 1-based
        private object GetKeyboardsForLanguage(int languageNumber)
        {
            Guard.Against(
                languageNumber == 0,
                "'languageNumber' should be 1-based index, but is 0"
            );
            var zeroBasedLanguageNumber = languageNumber - 1;
            var writingSystem = _collectionSettings.AllLanguages[zeroBasedLanguageNumber];

            EnsureKeyboardServicesBuilt();
            var tag = writingSystem.Tag;

            // Keyman cloud search does network I/O (up to a multi-second timeout when offline). This
            // endpoint runs off the UI thread (see the handler registration), so this call happens on a
            // server worker thread and never freezes the settings dialog. Offline/failure is treated as
            // "no results" internally, so no try/catch is needed here.
            var cloudResults = _keymanCloudClient.SearchKeyboardsForLanguage(tag);
            var cloud = cloudResults
                .Select(k => new
                {
                    id = k.Id,
                    name = k.Name,
                    downloads = k.Downloads,
                })
                .ToArray();

            // Reading the dialog's pending selection and enumerating OS keyboards are UI-thread-only
            // (libpalaso's KeyboardController and Win32 input state), so marshal just those to the UI
            // thread. automaticResolvesTo depends on the best OS keyboard, so it is computed here too.
            string current = null;
            object installed = null;
            object automaticResolvesTo = null;
            RunOnUiThread(() =>
            {
                current =
                    DialogBeingEdited?.PendingKeyboardSelections[zeroBasedLanguageNumber]
                    ?? writingSystem.Keyboard
                    ?? "";

                var installedList = _osKeyboards
                    .GetInstalledKeyboardsForLanguage(tag)
                    .Select(k => new
                    {
                        id = k.Id,
                        name = string.IsNullOrEmpty(k.LocalizedName) ? k.Name : k.LocalizedName,
                    })
                    .ToArray();
                installed = installedList;

                var bestOsKeyboard = _osKeyboards.FindBestForLanguage(tag);
                if (bestOsKeyboard != null)
                {
                    var name =
                        installedList.FirstOrDefault(k => k.id == bestOsKeyboard.Id)?.name
                        ?? bestOsKeyboard.Id;
                    automaticResolvesTo = new { kind = "system", displayName = name };
                }
                else if (!string.IsNullOrEmpty(writingSystem.CachedKmwFallbackKeyboard))
                {
                    var match = cloudResults.FirstOrDefault(k =>
                        k.Id == writingSystem.CachedKmwFallbackKeyboard
                    );
                    automaticResolvesTo = new
                    {
                        kind = "kmw",
                        displayName = match?.Name ?? writingSystem.CachedKmwFallbackKeyboard,
                    };
                }
                else
                {
                    automaticResolvesTo = new
                    {
                        kind = "none",
                        displayName = LocalizationManager.GetString(
                            "CollectionSettingsDialog.BookMakingTab.Keyboard.NoneResolvesTo",
                            "Default"
                        ),
                    };
                }
            });

            return new
            {
                current,
                languageTag = tag,
                automaticResolvesTo,
                installed,
                cloud,
            };
        }

        /// <summary>
        /// Run <paramref name="action"/> on the WinForms UI thread. The keyboardsForLanguage endpoint is
        /// served off the UI thread so its Keyman cloud search can't freeze the settings dialog, but OS
        /// keyboard enumeration is UI-thread-only, so we marshal just that part back via the settings
        /// dialog (which is the window this endpoint serves, so it is present here). Runs inline if we are
        /// already on the UI thread or the dialog is unexpectedly absent.
        /// </summary>
        private static void RunOnUiThread(Action action)
        {
            var dialog = DialogBeingEdited;
            if (dialog != null && dialog.InvokeRequired)
                dialog.Invoke(action);
            else
                action();
        }

        // languageNumber is 1-based. Like UpdatePendingFontName, this flags
        // ChangeThatRequiresRestart() as soon as the selection differs from the saved setting, so the
        // dialog's OK button switches to "Restart" immediately (the change genuinely needs a restart;
        // flagging it here means the user is not surprised by one after clicking OK). The authoritative
        // "did it actually change" check still runs at OK-commit time in
        // CollectionSettingsDialog.UpdateLanguageSettings.
        private void UpdatePendingKeyboard(string keyboard, int languageNumber)
        {
            Guard.Against(
                languageNumber == 0,
                "'languageNumber' should be 1-based index, but is 0"
            );

            var zeroBasedLanguageNumber = languageNumber - 1;
            if (
                zeroBasedLanguageNumber == 2
                && _collectionSettings.AllLanguages[zeroBasedLanguageNumber] == null
            )
                return;
            if (DialogBeingEdited != null)
            {
                DialogBeingEdited.PendingKeyboardSelections[zeroBasedLanguageNumber] = keyboard;
                // Normalize null to "" so switching between the two null/empty forms of Automatic is
                // not mistaken for a change.
                var currentKeyboard =
                    _collectionSettings.AllLanguages[zeroBasedLanguageNumber].Keyboard ?? "";
                if ((keyboard ?? "") != currentKeyboard)
                    DialogBeingEdited.ChangeThatRequiresRestart();
            }
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

        public void PrepareToShowDialog() { }

        public static void DialogClosed() { }

        public static void UnsubscribeAllLanguageChangeListeners()
        {
            if (LanguageChange == null)
                return;
            foreach (Delegate del in LanguageChange.GetInvocationList())
            {
                EventHandler<LanguageChangeEventArgs> handler =
                    del as EventHandler<LanguageChangeEventArgs>;
                if (handler != null)
                {
                    LanguageChange -= handler;
                }
            }
        }
    }
}
