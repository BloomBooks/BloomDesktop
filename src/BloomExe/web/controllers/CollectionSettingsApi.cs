using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.SubscriptionAndFeatures;
using Bloom.TeamCollection;
using Bloom.Utils;
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
    /// Serves the Collection Settings dialog (collection/settings and its badge preview), and the
    /// various places that read a collection setting.
    /// </summary>
    public class CollectionSettingsApi
    {
        public const string kApiUrlPart = "settings/";

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
        private readonly XMatterPackFinder _xmatterPackFinder;
        private readonly BookSelection _bookSelection;
        private readonly TeamCollectionManager _tcManager;
        private readonly QueueRenameOfCollection _queueRenameOfCollection;

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
            apiHandler.RegisterEndpointHandler("collection/qrCode", HandleGetQrCode, false);
            apiHandler.RegisterEndpointHandler("collection/qrBadge", HandleGetQrBadge, false);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscription",
                HandleGetSubscription,
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "bookShelfData",
                request =>
                {
                    var subscriptionDescriptor = _collectionSettings.Subscription.Descriptor;
                    var defaultBookshelfUrlKey = _collectionSettings.DefaultBookshelf;
                    // Note that these variable names flow through as the object keys and must match the names expected by the client.
                    request.ReplyWithJson(new { subscriptionDescriptor, defaultBookshelfUrlKey });
                },
                false
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
                            if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
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
            // a "deprecated" subscription is one that used to be eternal but is now being phased out
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
        }

        /// <summary>
        /// Replies to GET collection/settings with the values the collection has now. A Team
        /// Collection member who is not an administrator gets only the reason they may not edit.
        /// </summary>
        private void HandleGetCollectionSettings(ApiRequest request)
        {
            if (!_tcManager.OkToEditCollectionSettings)
            {
                request.ReplyWithJson(
                    new
                    {
                        // MustBeAdminMessage is HTML for BloomMessageBox; the dialog shows
                        // plain text with its line breaks kept.
                        notAllowedMessage = WorkspaceView
                            .MustBeAdminMessage(_collectionSettings)
                            .Replace("<br>", "\n"),
                    }
                );
                return;
            }
            var xmatterKeyForcedByBranding =
                _collectionSettings.GetXMatterPackNameSpecifiedByBrandingOrNull();
            var response = new CollectionSettingsResponse
            {
                Values = new CollectionSettingsValues(_collectionSettings),
                RestartPaths = CollectionSettingsValues.GetRestartPaths(),
                IsTeamCollection = CurrentCollectionIsTeamCollection,
                EditingABlorgBook = _collectionSettings.EditingABlorgBook,
                DefaultTeamCollectionFolder = DropboxUtils.GetDropboxFolderPath() ?? "",
                DefaultFontName = WritingSystem.GetDefaultFontName(),
                PageNumberStyles = GetPageNumberStyles(),
                XmatterPacks = GetXmatterPacks(xmatterKeyForcedByBranding),
                XmatterChosenByBranding = xmatterKeyForcedByBranding != null,
            };
            request.ReplyWithJson(JsonConvert.SerializeObject(response, kCamelCaseSettings));
        }

        /// <summary>
        /// The page numbering styles, with their names as the user sees them.
        /// </summary>
        private static PageNumberStyleOption[] GetPageNumberStyles()
        {
            return CollectionSettings
                .CssNumberStylesToCultureOrDigits.Keys.Select(styleKey => new PageNumberStyleOption
                {
                    Value = styleKey,
                    Label = LocalizationManager.GetString(
                        "CollectionSettingsDialog.BookMakingTab.PageNumberingStyle." + styleKey,
                        styleKey
                    ),
                })
                .ToArray();
        }

        /// <summary>
        /// The front/back matter packs the collection may use, with their names and descriptions
        /// as the user sees them.
        /// </summary>
        private XmatterPack[] GetXmatterPacks(string xmatterKeyForcedByBranding)
        {
            return _xmatterPackFinder
                .GetXMattersToOfferInSettings(xmatterKeyForcedByBranding)
                .Select(pack => new XmatterPack
                {
                    Value = pack.Key,
                    Label = LocalizationManager.GetDynamicString(
                        "Bloom",
                        "CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack."
                            + pack.EnglishLabel,
                        pack.EnglishLabel,
                        "Name of a Front/Back Matter Pack"
                    ),
                    Description = pack.GetDescription(), // already localized, if available
                })
                .ToArray();
        }

        /// <summary>
        /// Handles POST collection/settings: the complete values as the user left them, and whether
        /// the dialog found a change that needs a restart (it already knows, to label OK).
        /// </summary>
        private void HandleSaveCollectionSettings(ApiRequest request)
        {
            var saveRequest = JsonConvert.DeserializeObject<CollectionSettingsSaveRequest>(
                request.RequiredPostJson()
            );
            CollectionSettingsUpdater.Apply(
                saveRequest.Values,
                _collectionSettings,
                CurrentCollectionIsTeamCollection,
                _xmatterPackFinder,
                newName => _queueRenameOfCollection.Raise(newName),
                // Making a Team Collection changes a restart path, so the reopen below follows.
                repoFolderParentPath =>
                    TeamCollectionApi.TheOneInstance.CreateTeamCollection(repoFolderParentPath)
            );
            request.PostSucceeded();
            if (saveRequest.RestartRequired)
                WorkspaceApi.ReopenCollectionWhenIdle();
        }

        /// <summary>
        /// Replies to GET settings/subscription with a description of the collection's
        /// subscription, or, given ?code=, of the one that code stands for (the Collection Settings
        /// dialog asks about the code being typed).
        /// </summary>
        private void HandleGetSubscription(ApiRequest request)
        {
            var code = request.GetParamOrNull("code");
            var subscription =
                code == null
                    ? _collectionSettings.Subscription
                    : CollectionSettingsUpdater.GetChangedSubscription(_collectionSettings, code)
                        ?? _collectionSettings.Subscription;
            request.ReplyWithJson(
                JsonConvert.SerializeObject(
                    new
                    {
                        Code = subscription.Code ?? "",
                        Tier = subscription.Tier.ToString(),
                        Summary = BrandingSettings.GetSummaryHtml(subscription.Descriptor),
                        Expiration = subscription.ExpirationDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture
                        ),
                        CodeIntegrity = subscription.GetIntegrityLabel(),
                        SubscriptionDescriptor = subscription.Descriptor,
                        MissingBrandingFiles = (
                            subscription.Tier == SubscriptionTier.Enterprise
                            && !BrandingProject.HaveFilesForBranding(subscription.BrandingKey)
                        ),
                        EditingBlorgBook = subscription.EditingBlorgBook,
                    }
                )
            );
        }

        /// <summary>
        /// Replies to GET collection/qrCode?tag=... with the PNG of the QR code that books in that
        /// language get on their badge. The badge preview (collection/qrBadge) shows it.
        /// </summary>
        private void HandleGetQrCode(ApiRequest request)
        {
            using (var bitmap = BookStorage.MakeLanguageQrCodeBitmap(request.RequiredParam("tag")))
            using (var png = new MemoryStream())
            {
                bitmap.Save(png, ImageFormat.Png);
                png.Position = 0;
                request.ReplyWithStreamContent(png, "image/png");
            }
        }

        /// <summary>
        /// Replies to GET collection/qrBadge?showQrCode=...&amp;tag=...&amp;languageName=...&amp;caption=...
        /// with {html}: the "Made with Bloom" badge as books get it, built by the code that puts it
        /// in books, for the Front &amp; Back Matter page's preview. It takes the values rather than
        /// using the saved ones so that the preview can follow what the user has not yet saved.
        /// </summary>
        private void HandleGetQrBadge(ApiRequest request)
        {
            var langTag = request.RequiredParam("tag");
            var html = BookStorage.MakeBadgePreviewHtml(
                request.RequiredParam("showQrCode") == "true",
                langTag,
                _collectionSettings.GetBadgeCaptionPattern(request.RequiredParam("caption")),
                request.RequiredParam("languageName"),
                // A few brandings have badge pictures of their own (dark, or white on red), but
                // most use the default branding's, so the preview shows those.
                "/bloom/branding/Default/",
                "/bloom/api/collection/qrCode?tag=" + Uri.EscapeDataString(langTag)
            );
            request.ReplyWithJson(new { html });
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
    }
}
