using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Bloom.AiSourceBubbles;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
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
                            StoreAdvancedSettingsData(
                                JObject.Parse(request.RequiredPostJson()),
                                dialog
                            );
                        }
                        request.PostSucceeded();
                    }
                },
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "validateAiSourceBubbles",
                HandleValidateAiSourceBubblesAsync,
                false,
                true
            );
            apiHandler.RegisterAsyncEndpointHandler(
                kApiUrlPart + "aiSourceBubblesSupportedLanguages",
                HandleGetAiSourceBubblesSupportedLanguagesAsync,
                false,
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
            var aiSourceBubblesValidation = GetAiSourceBubblesValidationState(dialog);
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
                    allowAiSourceBubbles = dialog?.PendingAllowAiSourceBubbles
                        ?? ExperimentalFeatures.IsFeatureEnabled(
                            ExperimentalFeatures.kAiSourceBubbles
                        ),
                    aiSourceBubblesProvider = AiSourceBubblesService.NormalizeProviderId(
                        dialog?.PendingAiSourceBubblesProviderId
                            ?? _collectionSettings.AiSourceBubblesProviderId
                    ),
                    aiSourceBubblesTargetLanguageTag = dialog?.PendingAiSourceBubblesTargetLanguageTag
                        ?? _collectionSettings.AiSourceBubblesTargetLanguageTag,
                    aiSourceBubblesDeepLApiKey = dialog?.PendingAiSourceBubblesDeepLApiKey
                        ?? _collectionSettings.AiSourceBubblesDeepLApiKey,
                    aiSourceBubblesGoogleServiceAccountEmail = dialog?.PendingAiSourceBubblesGoogleServiceAccountEmail
                        ?? _collectionSettings.AiSourceBubblesGoogleServiceAccountEmail,
                    aiSourceBubblesGooglePrivateKey = dialog?.PendingAiSourceBubblesGooglePrivateKey
                        ?? _collectionSettings.AiSourceBubblesGooglePrivateKey,
                    showQrCode = dialog?.PendingShowQrCode
                        ?? _collectionSettings.ShowBlorgLanguageQrCode,
                    qrcodeCaption = dialog?.PendingBadgeQrCodeCaption
                        ?? _collectionSettings.BadgeQrCodeLabelLocalized,
                },
                showAutoUpdate = isAutoUpdateSupported,
                showExperimentalBookSourcesOption = dialog?.ShowExperimentalBookSourcesOption
                    ?? false,
                allowTeamCollectionEnabled = dialog?.AllowTeamCollectionOptionEnabled ?? true,
                aiSourceBubblesValidation,
            };
        }

        private object GetAiSourceBubblesValidationState(CollectionSettingsDialog dialog)
        {
            var currentSettings = GetAiSourceBubblesSettings(dialog);
            var currentFingerprint = AiSourceBubblesService.GetConfigurationFingerprint(
                currentSettings
            );
            var validatedFingerprint =
                dialog?.PendingAiSourceBubblesValidatedConfigurationFingerprint
                ?? _collectionSettings.AiSourceBubblesValidatedConfigurationFingerprint;
            var succeeded =
                dialog?.PendingAiSourceBubblesLastValidationSucceeded
                ?? _collectionSettings.AiSourceBubblesLastValidationSucceeded;
            var message =
                dialog?.PendingAiSourceBubblesLastValidationMessage
                ?? _collectionSettings.AiSourceBubblesLastValidationMessage;
            var isCurrent = String.Equals(
                currentFingerprint,
                validatedFingerprint,
                StringComparison.Ordinal
            );

            return new
            {
                currentFingerprint,
                validatedFingerprint = isCurrent ? validatedFingerprint : String.Empty,
                succeeded = isCurrent && succeeded,
                message = isCurrent ? message : String.Empty,
            };
        }

        private CollectionSettings GetAiSourceBubblesSettings(CollectionSettingsDialog dialog)
        {
            return new CollectionSettings
            {
                Subscription = _collectionSettings.Subscription,
                AiSourceBubblesProviderId =
                    dialog?.PendingAiSourceBubblesProviderId
                    ?? _collectionSettings.AiSourceBubblesProviderId,
                AiSourceBubblesTargetLanguageTag =
                    dialog?.PendingAiSourceBubblesTargetLanguageTag
                    ?? _collectionSettings.AiSourceBubblesTargetLanguageTag,
                AiSourceBubblesDeepLApiKey =
                    dialog?.PendingAiSourceBubblesDeepLApiKey
                    ?? _collectionSettings.AiSourceBubblesDeepLApiKey,
                AiSourceBubblesGoogleServiceAccountEmail =
                    dialog?.PendingAiSourceBubblesGoogleServiceAccountEmail
                    ?? _collectionSettings.AiSourceBubblesGoogleServiceAccountEmail,
                AiSourceBubblesGooglePrivateKey =
                    dialog?.PendingAiSourceBubblesGooglePrivateKey
                    ?? _collectionSettings.AiSourceBubblesGooglePrivateKey,
            };
        }

        private static void InvalidateAiSourceBubblesValidation(CollectionSettingsDialog dialog)
        {
            var pendingSettings = new CollectionSettings
            {
                AiSourceBubblesProviderId = dialog.PendingAiSourceBubblesProviderId,
                AiSourceBubblesTargetLanguageTag = dialog.PendingAiSourceBubblesTargetLanguageTag,
                AiSourceBubblesDeepLApiKey = dialog.PendingAiSourceBubblesDeepLApiKey,
                AiSourceBubblesGoogleServiceAccountEmail =
                    dialog.PendingAiSourceBubblesGoogleServiceAccountEmail,
                AiSourceBubblesGooglePrivateKey = dialog.PendingAiSourceBubblesGooglePrivateKey,
            };
            var currentFingerprint = AiSourceBubblesService.GetConfigurationFingerprint(
                pendingSettings
            );
            if (
                String.Equals(
                    dialog.PendingAiSourceBubblesValidatedConfigurationFingerprint,
                    currentFingerprint,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }

            dialog.PendingAiSourceBubblesValidatedConfigurationFingerprint = String.Empty;
            dialog.PendingAiSourceBubblesLastValidationSucceeded = false;
            dialog.PendingAiSourceBubblesLastValidationMessage = String.Empty;
        }

        /// <summary>
        /// Validates the AI Source Bubbles configuration currently being edited in Collection Settings.
        /// </summary>
        private async Task HandleValidateAiSourceBubblesAsync(ApiRequest request)
        {
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed(HttpStatusCode.MethodNotAllowed, "Only POST is supported.");
                return;
            }

            var dialog = DialogBeingEdited;
            var requestJson = request.RequiredPostJson();
            if (dialog != null && !String.IsNullOrWhiteSpace(requestJson))
            {
                StoreAdvancedSettingsData(JObject.Parse(requestJson), dialog);
            }

            var settings = GetAiSourceBubblesSettings(dialog);
            var validationResult = new AiSourceBubblesValidationResult
            {
                ConfigurationFingerprint = AiSourceBubblesService.GetConfigurationFingerprint(
                    settings
                ),
                Succeeded = false,
                Message = String.Empty,
            };

            try
            {
                validationResult = await new AiSourceBubblesService(
                    settings
                ).ValidateConfigurationAsync();
            }
            catch (ArgumentException e)
            {
                validationResult.Message = e.Message;
            }
            catch (InvalidOperationException e)
            {
                validationResult.Message = e.Message;
            }
            catch (HttpRequestException e)
            {
                validationResult.Message = e.Message;
            }
            catch (CryptographicException e)
            {
                validationResult.Message = e.Message;
            }
            catch (JsonException e)
            {
                validationResult.Message = e.Message;
            }

            if (dialog != null)
            {
                dialog.PendingAiSourceBubblesValidatedConfigurationFingerprint =
                    validationResult.ConfigurationFingerprint;
                dialog.PendingAiSourceBubblesLastValidationSucceeded = validationResult.Succeeded;
                dialog.PendingAiSourceBubblesLastValidationMessage = validationResult.Message;
            }

            request.ReplyWithJson(validationResult);
        }

        /// <summary>
        /// Gets the provider-backed list of target languages for the AI Source Bubbles settings currently being edited.
        /// </summary>
        private async Task HandleGetAiSourceBubblesSupportedLanguagesAsync(ApiRequest request)
        {
            if (request.HttpMethod != HttpMethods.Post)
            {
                request.Failed(HttpStatusCode.MethodNotAllowed, "Only POST is supported.");
                return;
            }

            var dialog = DialogBeingEdited;
            var requestJson = request.RequiredPostJson();
            if (dialog != null && !String.IsNullOrWhiteSpace(requestJson))
            {
                StoreAdvancedSettingsData(JObject.Parse(requestJson), dialog);
            }

            var settings = GetAiSourceBubblesSettings(dialog);
            try
            {
                var languages = await new AiSourceBubblesService(
                    settings
                ).GetSupportedTargetLanguagesAsync();
                request.ReplyWithJson(new { languages, message = String.Empty });
            }
            catch (ArgumentException e)
            {
                request.ReplyWithJson(
                    new { languages = Array.Empty<object>(), message = e.Message }
                );
            }
            catch (InvalidOperationException e)
            {
                request.ReplyWithJson(
                    new { languages = Array.Empty<object>(), message = e.Message }
                );
            }
            catch (HttpRequestException e)
            {
                request.ReplyWithJson(
                    new { languages = Array.Empty<object>(), message = e.Message }
                );
            }
            catch (CryptographicException e)
            {
                request.ReplyWithJson(
                    new { languages = Array.Empty<object>(), message = e.Message }
                );
            }
            catch (JsonException e)
            {
                request.ReplyWithJson(
                    new { languages = Array.Empty<object>(), message = e.Message }
                );
            }
        }

        private void StoreAdvancedSettingsData(JObject data, CollectionSettingsDialog dialog)
        {
            var aiSourceBubblesConfigurationChanged = false;

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

            var allowAiSourceBubblesToken = data["allowAiSourceBubbles"];
            if (allowAiSourceBubblesToken != null)
            {
                var allowAiSourceBubbles = allowAiSourceBubblesToken.Value<bool>();
                dialog.PendingAllowAiSourceBubbles = allowAiSourceBubbles;
            }

            var aiSourceBubblesProviderToken = data["aiSourceBubblesProvider"];
            if (aiSourceBubblesProviderToken != null)
            {
                var providerId = AiSourceBubblesService.NormalizeProviderId(
                    aiSourceBubblesProviderToken.Value<string>()
                );
                aiSourceBubblesConfigurationChanged |= !String.Equals(
                    dialog.PendingAiSourceBubblesProviderId,
                    providerId,
                    StringComparison.OrdinalIgnoreCase
                );
                dialog.PendingAiSourceBubblesProviderId = providerId;
            }

            var aiSourceBubblesTargetLanguageTagToken = data["aiSourceBubblesTargetLanguageTag"];
            if (aiSourceBubblesTargetLanguageTagToken != null)
            {
                var targetLanguageTag = aiSourceBubblesTargetLanguageTagToken.Value<string>();
                aiSourceBubblesConfigurationChanged |= !String.Equals(
                    dialog.PendingAiSourceBubblesTargetLanguageTag,
                    targetLanguageTag,
                    StringComparison.Ordinal
                );
                dialog.PendingAiSourceBubblesTargetLanguageTag = targetLanguageTag;
            }

            var aiSourceBubblesDeepLApiKeyToken = data["aiSourceBubblesDeepLApiKey"];
            if (aiSourceBubblesDeepLApiKeyToken != null)
            {
                var deepLApiKey = aiSourceBubblesDeepLApiKeyToken.Value<string>();
                aiSourceBubblesConfigurationChanged |= !String.Equals(
                    dialog.PendingAiSourceBubblesDeepLApiKey,
                    deepLApiKey,
                    StringComparison.Ordinal
                );
                dialog.PendingAiSourceBubblesDeepLApiKey = deepLApiKey;
            }
            var aiSourceBubblesGoogleServiceAccountEmailToken = data[
                "aiSourceBubblesGoogleServiceAccountEmail"
            ];
            if (aiSourceBubblesGoogleServiceAccountEmailToken != null)
            {
                var googleServiceAccountEmail =
                    aiSourceBubblesGoogleServiceAccountEmailToken.Value<string>();
                aiSourceBubblesConfigurationChanged |= !String.Equals(
                    dialog.PendingAiSourceBubblesGoogleServiceAccountEmail,
                    googleServiceAccountEmail,
                    StringComparison.Ordinal
                );
                dialog.PendingAiSourceBubblesGoogleServiceAccountEmail = googleServiceAccountEmail;
            }

            var aiSourceBubblesGooglePrivateKeyToken = data["aiSourceBubblesGooglePrivateKey"];
            if (aiSourceBubblesGooglePrivateKeyToken != null)
            {
                var googlePrivateKey = aiSourceBubblesGooglePrivateKeyToken.Value<string>();
                aiSourceBubblesConfigurationChanged |= !String.Equals(
                    dialog.PendingAiSourceBubblesGooglePrivateKey,
                    googlePrivateKey,
                    StringComparison.Ordinal
                );
                dialog.PendingAiSourceBubblesGooglePrivateKey = googlePrivateKey;
            }

            if (aiSourceBubblesConfigurationChanged)
            {
                InvalidateAiSourceBubblesValidation(dialog);
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
