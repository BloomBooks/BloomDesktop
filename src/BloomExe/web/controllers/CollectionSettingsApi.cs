using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;
using Bloom.TeamCollection;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using L10NSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
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

        /// <summary>
        /// The edits being made in whichever Collection Settings dialog is open, or null when
        /// none is. Every endpoint that records a pending setting writes here, and ignores a post
        /// that arrives after the session has ended (a control losing focus as the dialog closes).
        /// </summary>
        public static PendingCollectionSettings PendingSettings { get; private set; }

        /// <summary>
        /// Raised when an editing session ends without its edits being applied, so that anything
        /// holding a pending value of its own can go back to the saved one.
        /// </summary>
        public static event EventHandler EditingCancelled;

        /// <summary>
        /// Set by the WinForms Collection Settings dialog while it is open, so that its Book Making
        /// tab can still open the WinForms ScriptSettingsDialog for a (zero-based) language number.
        /// The React dialog never opens that dialog.
        /// </summary>
        public static Action<int> ShowScriptSettingsDialog;

        /// <summary>
        /// The TypeScript side expects the names in the collection/settings contract in camelCase,
        /// while the C# classes spell them the way C# does.
        /// </summary>
        internal static readonly JsonSerializerSettings kCamelCaseSettings =
            new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy(),
                },
            };

        private readonly CollectionSettings _collectionSettings;
        private readonly List<NumberingStyleOffering> _numberingStyles =
            new List<NumberingStyleOffering>();
        private readonly XMatterPackFinder _xmatterPackFinder;
        private readonly BookSelection _bookSelection;
        private readonly TeamCollectionManager _tcManager;
        private readonly QueueRenameOfCollection _queueRenameOfCollection;

        public static event EventHandler<LanguageChangeEventArgs> LanguageChange;

        public CollectionSettingsApi(
            CollectionSettings collectionSettings,
            XMatterPackFinder xmatterPackFinder,
            BookSelection bookSelection,
            TeamCollectionManager tcManager,
            QueueRenameOfCollection queueRenameOfCollection
        )
        {
            _collectionSettings = collectionSettings;
            _xmatterPackFinder = xmatterPackFinder;
            this._bookSelection = bookSelection;
            _tcManager = tcManager;
            _queueRenameOfCollection = queueRenameOfCollection;
        }

        /// <summary>
        /// Starts a session in which a dialog gathers edits to the collection settings, starting
        /// from the values the collection has now.
        /// </summary>
        public static PendingCollectionSettings BeginEditing(CollectionSettings settings)
        {
            PendingSettings = new PendingCollectionSettings(settings);
            return PendingSettings;
        }

        /// <summary>
        /// Ends the session started by BeginEditing, whether or not its edits were applied.
        /// </summary>
        public static void EndEditing()
        {
            PendingSettings = null;
        }

        /// <summary>
        /// Ends the session and tells subscribers that its edits were thrown away.
        /// </summary>
        public static void CancelEditing()
        {
            EndEditing();
            EditingCancelled?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Whether the collection we have open is a Team Collection, even if we cannot reach the
        /// repository just now.
        /// </summary>
        private bool CurrentCollectionIsTeamCollection =>
            _tcManager.CurrentCollectionEvenIfDisconnected != null;

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "collection/settings",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                        HandleGetCollectionSettings(request);
                    else
                        HandleSaveCollectionSettings(request);
                },
                true
            );
            apiHandler.RegisterEndpointHandler(
                "collection/settings/cancel",
                request =>
                {
                    CancelEditing();
                    request.PostSucceeded();
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
                        if (PendingSettings != null)
                            StoreAdvancedSettingsData(request, PendingSettings);
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
                        UpdatePendingDefaultBookshelf(newShelf);
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
                            IsRtl = data.IsRtl,
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
                        request.ReplyWithJson(
                            JsonConvert.SerializeObject(GetNumberingStyleData(), kCamelCaseSettings)
                        );
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
#if DEBUG
                        // DEV-ONLY (Debug builds): force the collection's branding to an arbitrary
                        // key at runtime so tooling can survey every branding's rendered pages
                        // without restarting Bloom or minting real subscription codes. This reuses
                        // the already-registered handler so it takes effect via hot-reload with no
                        // restart. NOT shipped behavior (Release still throws). See BL-16370.
                        // POST body: either a bare branding descriptor (e.g. "Default"), or JSON
                        // {"branding":..,"layout":..,"xmatter":..} where any field may be omitted
                        // (null/absent = leave that axis unchanged). Used by the branding-report
                        // survey tool to walk branding × layout × xmatter for one book.
                        // This handler is registered handleOnUiThread:true, so the book work runs
                        // on the UI thread (safe). We update the in-memory selected book in place
                        // because the whole-book preview renders CurrentSelection directly.
                        string branding = null,
                            layout = null,
                            xmatter = null;
                        try
                        {
                            // Read and parse inside the try as well: a malformed body is exactly
                            // the sort of one-cell failure the catch below is meant to absorb.
                            var body = request.RequiredPostString();
                            if (body.TrimStart().StartsWith("{"))
                            {
                                var o = Newtonsoft.Json.Linq.JObject.Parse(body);
                                branding = (string)o["branding"];
                                layout = (string)o["layout"];
                                xmatter = (string)o["xmatter"];
                            }
                            else
                            {
                                branding = body;
                            }
                            if (!string.IsNullOrEmpty(branding))
                                _collectionSettings.Subscription =
                                    SubscriptionAndFeatures.Subscription.ForUnitTestWithOverrideTierOrDescriptor(
                                        SubscriptionAndFeatures.SubscriptionTier.Enterprise,
                                        branding
                                    );
                            if (!string.IsNullOrEmpty(xmatter))
                                _collectionSettings.XMatterPackName = xmatter;
                            var book = _bookSelection?.CurrentSelection;
                            if (book != null)
                            {
                                if (!string.IsNullOrEmpty(layout))
                                    book.SetLayout(
                                        new Layout
                                        {
                                            SizeAndOrientation = SizeAndOrientation.FromString(
                                                layout
                                            ),
                                        }
                                    );
                                book.BringBookUpToDate(new SIL.Progress.NullProgress());
                            }
                            request.PostSucceeded();
                        }
                        catch (Exception ex)
                        {
                            // Don't let one cell's failure take down Bloom or wedge the run;
                            // report it (type + message) so the survey/control tool can log and
                            // move on. NOTE: request.Failed() puts this text in the HTTP status
                            // reason phrase (response.statusText), NOT the body.
                            request.Failed(
                                "set-state (branding='"
                                    + branding
                                    + "', layout='"
                                    + layout
                                    + "', xmatter='"
                                    + xmatter
                                    + "') failed: "
                                    + ex.GetType().Name
                                    + ": "
                                    + ex.Message
                            );
                        }
#else
                        throw new NotImplementedException(
                            "We don't expect to be setting the branding key, ever. It flows from the subscription code."
                        );
#endif
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
                        request.ReplyWithJson(
                            JsonConvert.SerializeObject(SetupXMatterList(), kCamelCaseSettings)
                        );
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
            var pending = PendingSettings;
            var isAutoUpdateSupported = CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform;
            return new
            {
                values = new
                {
                    autoUpdate = pending?.AutomaticallyUpdate
                        ?? (isAutoUpdateSupported && Settings.Default.AutoUpdate),
                    showExperimentalBookSources = pending?.ShowExperimentalBookSources
                        ?? ExperimentalFeatures.IsFeatureEnabled(
                            ExperimentalFeatures.kExperimentalSourceBooks
                        ),
                    allowTeamCollection = pending?.AllowTeamCollection
                        ?? ExperimentalFeatures.IsFeatureEnabled(
                            ExperimentalFeatures.kTeamCollections
                        ),
                    showQrCode = pending?.ShowQrCode ?? _collectionSettings.ShowBlorgLanguageQrCode,
                    qrcodeCaption = pending?.BadgeQrCodeCaption
                        ?? _collectionSettings.BadgeQrCodeLabelLocalized,
                },
                showAutoUpdate = isAutoUpdateSupported,
                // The Experimental Book Sources toggle is not offered to anyone at the moment.
                showExperimentalBookSourcesOption = false,
                // Don't allow the user to disable the Team Collection feature if we're currently in a Team Collection.
                allowTeamCollectionEnabled = !(
                    (pending?.AllowTeamCollection ?? false) && CurrentCollectionIsTeamCollection
                ),
            };
        }

        private void StoreAdvancedSettingsData(
            ApiRequest request,
            PendingCollectionSettings pending
        )
        {
            var data = JObject.Parse(request.RequiredPostJson());

            var autoUpdateToken = data["autoUpdate"];
            if (autoUpdateToken != null)
                pending.AutomaticallyUpdate = autoUpdateToken.Value<bool>();

            var showExperimentalBookSourcesToken = data["showExperimentalBookSources"];
            if (showExperimentalBookSourcesToken != null)
                pending.ShowExperimentalBookSources =
                    showExperimentalBookSourcesToken.Value<bool>();

            var allowTeamCollectionToken = data["allowTeamCollection"];
            if (allowTeamCollectionToken != null)
            {
                var allowTeamCollection = allowTeamCollectionToken.Value<bool>();
                var previousValue = pending.AllowTeamCollection;
                pending.AllowTeamCollection = allowTeamCollection;
                if (allowTeamCollection != previousValue)
                    pending.ChangeThatRequiresRestart();
            }

            var showQrCodeToken = data["showQrCode"];
            if (showQrCodeToken != null)
            {
                var showQrCode = showQrCodeToken.Value<bool>();
                var previousValue = pending.ShowQrCode;
                pending.ShowQrCode = showQrCode;
                // We don't really need a change as drastic as a restart, but I don't expect
                // this to change often and somehow the badge needs to get updated.
                if (showQrCode != previousValue)
                    pending.ChangeThatRequiresRestart();
            }
            var qrcodeCaptionToken = data["qrcodeCaption"];
            if (qrcodeCaptionToken != null)
            {
                var qrcodeCaption = qrcodeCaptionToken.Value<string>();
                var previousValue = pending.BadgeQrCodeCaption;
                pending.BadgeQrCodeCaption = qrcodeCaption;
                // We don't really need a change as drastic as a restart, but I don't expect
                // this to change often and somehow the badge needs to get updated.
                if (qrcodeCaption != previousValue)
                    pending.ChangeThatRequiresRestart();
            }
        }

        /// <summary>
        /// Replies to GET collection/settings, starting a fresh editing session so the values the
        /// dialog shows are the ones the collection has now. A Team Collection member who is not
        /// an administrator gets only the reason they may not edit, and no session.
        /// </summary>
        private void HandleGetCollectionSettings(ApiRequest request)
        {
            if (!_tcManager.OkToEditCollectionSettings)
            {
                request.ReplyWithJson(
                    JsonConvert.SerializeObject(
                        new CollectionSettingsResponse
                        {
                            NotAllowedMessage = WorkspaceView.MustBeAdminMessage(
                                _collectionSettings,
                                "\n"
                            ),
                        },
                        kCamelCaseSettings
                    )
                );
                return;
            }
            BeginEditing(_collectionSettings);
            var response = new CollectionSettingsResponse
            {
                Values = GetCurrentValues(),
                Context = GetContext(),
                RestartPaths = CollectionSettingsValues.GetRestartPaths(),
            };
            request.ReplyWithJson(JsonConvert.SerializeObject(response, kCamelCaseSettings));
        }

        /// <summary>
        /// Handles POST collection/settings, whose body is the "values" object of the GET reply.
        /// On a validation failure nothing is saved and the session stays open so the user can
        /// fix what is wrong.
        /// </summary>
        private void HandleSaveCollectionSettings(ApiRequest request)
        {
            var pending = PendingSettings;
            var postedValues = JsonConvert.DeserializeObject<CollectionSettingsValues>(
                request.RequiredPostJson()
            );
            MergeIntoPendingSettings(postedValues, pending);
            if (CollectionSettingsValues.AnyRestartPathChanged(GetCurrentValues(), postedValues))
                pending.ChangeThatRequiresRestart();

            var errorMessage = CollectionSettingsUpdater.Validate(
                pending,
                CurrentCollectionIsTeamCollection
            );
            if (errorMessage != null)
            {
                request.ReplyWithJson(
                    JsonConvert.SerializeObject(
                        new CollectionSettingsSaveResult
                        {
                            RestartRequired = false,
                            ErrorMessage = errorMessage,
                        },
                        kCamelCaseSettings
                    )
                );
                return;
            }

            var restartRequired = CollectionSettingsUpdater.Apply(
                pending,
                _collectionSettings,
                CurrentCollectionIsTeamCollection,
                _xmatterPackFinder,
                newName => _queueRenameOfCollection.Raise(newName)
            );
            EndEditing();
            request.ReplyWithJson(
                JsonConvert.SerializeObject(
                    new CollectionSettingsSaveResult
                    {
                        RestartRequired = restartRequired,
                        ErrorMessage = null,
                    },
                    kCamelCaseSettings
                )
            );
            if (restartRequired)
                WorkspaceApi.ReopenCollectionWhenIdle();
        }

        /// <summary>
        /// The editable settings as the collection has them now.
        /// </summary>
        private CollectionSettingsValues GetCurrentValues()
        {
            var thirdLanguage = _collectionSettings.AllLanguages[2];
            return new CollectionSettingsValues
            {
                Languages = new LanguagesValues
                {
                    Language1 = MakeLanguageValues(_collectionSettings.AllLanguages[0]),
                    Language2 = MakeLanguageValues(_collectionSettings.AllLanguages[1]),
                    Language3 = string.IsNullOrEmpty(thirdLanguage?.Tag)
                        ? null
                        : MakeLanguageValues(thirdLanguage),
                    SignLanguage = new SignLanguageValues
                    {
                        Tag = _collectionSettings.SignLanguage.Tag,
                        Name = _collectionSettings.SignLanguage.Name,
                        IsCustomName = _collectionSettings.SignLanguage.IsCustomName,
                    },
                },
                FrontBackMatter = new FrontBackMatterValues
                {
                    Xmatter = _collectionSettings.XMatterPackName,
                    PageNumberStyle = _collectionSettings.PageNumberStyle,
                    ShowQrCode = _collectionSettings.ShowBlorgLanguageQrCode,
                    QrcodeCaption = _collectionSettings.BadgeQrCodeLabelLocalized,
                    // A collection whose settings file never carried these leaves them null.
                    Country = _collectionSettings.Country ?? "",
                    Province = _collectionSettings.Province ?? "",
                    District = _collectionSettings.District ?? "",
                },
                Advanced = new AdvancedValues
                {
                    AutoUpdate =
                        CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform
                        && Settings.Default.AutoUpdate,
                    CollectionName = _collectionSettings.CollectionName,
                },
                Experimental = new Dictionary<string, bool>
                {
                    {
                        ExperimentalFeatures.kTeamCollections,
                        ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections)
                    },
                },
            };
        }

        private static LanguageValues MakeLanguageValues(WritingSystem language)
        {
            return new LanguageValues
            {
                Tag = language.Tag,
                Name = language.Name,
                IsCustomName = language.IsCustomName,
                FontName = language.FontName,
                IsRightToLeft = language.IsRightToLeft,
                LineHeight = language.LineHeight,
                BreaksLinesOnlyAtSpaces = language.BreaksLinesOnlyAtSpaces,
                BaseUIFontSizeInPoints = language.BaseUIFontSizeInPoints,
            };
        }

        /// <summary>
        /// What the dialog needs to render but cannot change.
        /// </summary>
        private CollectionSettingsContext GetContext()
        {
            var brandingForcedXmatter =
                _collectionSettings.GetXMatterPackNameSpecifiedByBrandingOrNull();
            return new CollectionSettingsContext
            {
                IsTeamCollection = CurrentCollectionIsTeamCollection,
                EditingBlorgBook = _collectionSettings.EditingABlorgBook,
                ShowAutoUpdate = CollectionSettingsDialog.AutoUpdateSupportedOnThisPlatform,
                TeamCollectionsAllowed = FeatureStatus
                    .GetFeatureStatus(_collectionSettings.Subscription, FeatureName.TeamCollection)
                    .Enabled,
                XmatterOfferings = GetXmatterOfferings(brandingForcedXmatter),
                BrandingForcedXmatter = brandingForcedXmatter,
                NumberingStyles = GetNumberingStyleOfferings(),
            };
        }

        /// <summary>
        /// Copies the values the React dialog sends into the editing session. The subscription,
        /// the administrators and the bookshelf arrive through endpoints of their own, so they
        /// are not here; neither is anything the caller left out.
        /// </summary>
        private static void MergeIntoPendingSettings(
            CollectionSettingsValues values,
            PendingCollectionSettings pending
        )
        {
            if (values.Languages != null)
            {
                MergeLanguage(values.Languages.Language1, pending, 0);
                MergeLanguage(values.Languages.Language2, pending, 1);
                MergeLanguage(values.Languages.Language3, pending, 2);
                var signLanguage = values.Languages.SignLanguage;
                if (signLanguage != null)
                {
                    pending.SignLanguage.ChangeTag(signLanguage.Tag);
                    pending.SignLanguage.SetName(signLanguage.Name, signLanguage.IsCustomName);
                }
            }
            if (values.FrontBackMatter != null)
            {
                pending.Xmatter = values.FrontBackMatter.Xmatter;
                pending.NumberingStyle = values.FrontBackMatter.PageNumberStyle;
                pending.ShowQrCode = values.FrontBackMatter.ShowQrCode;
                pending.BadgeQrCodeCaption = values.FrontBackMatter.QrcodeCaption;
                pending.Country = values.FrontBackMatter.Country;
                pending.Province = values.FrontBackMatter.Province;
                pending.District = values.FrontBackMatter.District;
            }
            if (values.Advanced != null)
            {
                pending.AutomaticallyUpdate = values.Advanced.AutoUpdate;
                pending.CollectionName = values.Advanced.CollectionName;
            }
            if (values.Experimental == null)
                return;
            foreach (var feature in values.Experimental)
            {
                switch (feature.Key)
                {
                    case ExperimentalFeatures.kTeamCollections:
                        pending.AllowTeamCollection = feature.Value;
                        break;
                    case ExperimentalFeatures.kExperimentalSourceBooks:
                        pending.ShowExperimentalBookSources = feature.Value;
                        break;
                    default:
                        throw new ArgumentException(
                            $"Unknown experimental feature '{feature.Key}'"
                        );
                }
            }
        }

        private static void MergeLanguage(
            LanguageValues language,
            PendingCollectionSettings pending,
            int zeroBasedLanguageNumber
        )
        {
            if (language == null)
                return;
            var pendingLanguage = pending.Languages[zeroBasedLanguageNumber];
            // Setting the tag also sets a default name, so set the name we were given afterwards.
            pendingLanguage.ChangeTag(language.Tag);
            pendingLanguage.SetName(language.Name, language.IsCustomName);
            pendingLanguage.IsRightToLeft = language.IsRightToLeft;
            pendingLanguage.LineHeight = language.LineHeight;
            pendingLanguage.BreaksLinesOnlyAtSpaces = language.BreaksLinesOnlyAtSpaces;
            pendingLanguage.BaseUIFontSizeInPoints = language.BaseUIFontSizeInPoints;
            pending.FontSelections[zeroBasedLanguageNumber] = language.FontName;
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
            request.ReplyWithJson(MakeLanguageDataJson(languageName, langTag));
        }

        /// <summary>
        /// Builds the JSON that the languageData endpoint returns. A display name is arbitrary
        /// user text: it can perfectly well contain a double quote or a backslash (BL-16209), so
        /// it has to be serialized rather than pasted into a hand-built JSON string. Getting that
        /// wrong produced invalid JSON, which made the whole collection tab fail to render.
        /// The callers of this endpoint treat languageName as a string (the books-on-Blorg
        /// progress bar compares it with ""), so keep coercing a null name to empty the way the old hand-built string
        /// did rather than sending a JSON null.
        /// </summary>
        internal static string MakeLanguageDataJson(string languageName, string languageTag)
        {
            return JsonConvert.SerializeObject(
                new { languageName = languageName ?? "", languageCode = languageTag ?? "" }
            );
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
            string xmatterKeyForcedByBranding =
                _collectionSettings.GetXMatterPackNameSpecifiedByBrandingOrNull();

            // This will switch to the default factory xmatter if the current one is not valid.
            var currentXmatter = _xmatterPackFinder.GetValidXmatter(
                xmatterKeyForcedByBranding,
                _collectionSettings.XMatterPackName
            );

            return new
            {
                currentXmatter,
                xmatterOfferings = GetXmatterOfferings(xmatterKeyForcedByBranding),
            };
        }

        /// <summary>
        /// The front/back matter packs the user may choose from, with their localized labels.
        /// </summary>
        private XmatterOffering[] GetXmatterOfferings(string xmatterKeyForcedByBranding)
        {
            var xmatterOfferings = new List<XmatterOffering>();
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
                xmatterOfferings.Add(
                    new XmatterOffering
                    {
                        DisplayName = labelToShow,
                        InternalName = pack.Key,
                        Description = pack.GetDescription(), // already localized, if available
                    }
                );
            }
            return xmatterOfferings.ToArray();
        }

        private object GetNumberingStyleData()
        {
            return new
            {
                currentPageNumberStyle = _collectionSettings.PageNumberStyle,
                numberingStyleData = GetNumberingStyleOfferings(),
            };
        }

        /// <summary>
        /// The page numbering styles the user may choose from, with their localized labels.
        /// </summary>
        private NumberingStyleOffering[] GetNumberingStyleOfferings()
        {
            if (_numberingStyles.Count == 0)
            {
                foreach (var styleKey in CollectionSettings.CssNumberStylesToCultureOrDigits.Keys)
                {
                    var localizedStyle = LocalizationManager.GetString(
                        "CollectionSettingsDialog.BookMakingTab.PageNumberingStyle." + styleKey,
                        styleKey
                    );
                    _numberingStyles.Add(
                        new NumberingStyleOffering
                        {
                            LocalizedStyle = localizedStyle,
                            StyleKey = styleKey,
                        }
                    );
                }
            }
            return _numberingStyles.ToArray();
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
            ShowScriptSettingsDialog?.Invoke(zeroBasedLanguageNumber);
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
            if (PendingSettings == null)
                return;
            PendingSettings.FontSelections[zeroBasedLanguageNumber] = fontName;
            if (fontName != _collectionSettings.AllLanguages[zeroBasedLanguageNumber].FontName)
                PendingSettings.ChangeThatRequiresRestart();
        }

        private void UpdatePendingNumberingStyle(string numberingStyle)
        {
            if (PendingSettings == null)
                return;
            PendingSettings.NumberingStyle = numberingStyle;
            if (numberingStyle != _collectionSettings.PageNumberStyle)
                PendingSettings.ChangeThatRequiresRestart();
        }

        private void UpdatePendingXmatter(string xMatterChoice)
        {
            if (PendingSettings == null)
                return;
            PendingSettings.Xmatter = xMatterChoice;
            if (xMatterChoice != _collectionSettings.XMatterPackName)
                PendingSettings.ChangeThatRequiresRestart();
        }

        private void UpdatePendingDefaultBookshelf(string bookshelf)
        {
            if (PendingSettings == null)
                return;
            PendingSettings.DefaultBookshelf = bookshelf;
            if (bookshelf != _collectionSettings.DefaultBookshelf)
                PendingSettings.ChangeThatRequiresRestart();
        }

        private void UpdatePendingAdministratorEmails(string emails)
        {
            if (PendingSettings == null)
                return;
            PendingSettings.Administrators = emails;
            if (emails != _collectionSettings.AdministratorsDisplayString)
                PendingSettings.ChangeThatRequiresRestart();
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
