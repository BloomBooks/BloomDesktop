using System;
using System.Globalization;
using System.Text;
using Bloom.Api;
using Bloom.Collection;
using SIL.IO;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Used by the settings dialog (currently just the EnterpriseSettings tab) and various places that need to know
    /// if Enterprise is enabled or not.
    /// </summary>
    public class SubscriptionSettingsEditorApi
    {
        public const string kApiUrlPart = "settings/";

        private readonly CollectionSettings _collectionSettings;
        private Subscription _subscription;

        public SubscriptionSettingsEditorApi(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
            _subscription = collectionSettings.Subscription;
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscriptionCode",
                request =>
                {
                    if (request.HttpMethod == HttpMethods.Get)
                    {
                        request.ReplyWithText(_subscription?.Code ?? "");
                    }
                    else // post
                    {
                        var codeString = request.RequiredPostString();
                        _subscription = new Subscription(codeString);

                        NotifyPendingSubscriptionChange?.Invoke(codeString);

                        // var newBranding = Subscription.GetBranding(_displayedCode);
                        // var oldBranding = !string.IsNullOrEmpty(_collectionSettings.InvalidBranding)
                        //     ? _collectionSettings.InvalidBranding
                        //     : "";



                        // If the user has entered a different subscription code then what was previously saved, we
                        // generally want to clear out the Bookshelf. But if the BrandingKey is the same as the old one,
                        // we'll leave it alone, since they probably renewed for another year or so and want to use the
                        // same bookshelf.
                        // if (
                        //     _displayedCode != _collectionSettings.SubscriptionCode
                        //     && newBranding != oldBranding
                        // )
                        //     ResetBookshelf();
                        // if (Subscription.Expired(_displayedCode)) // expired or invalid
                        // {
                        //     BrandingChangeHandler("Default", null); // TODO: this is the traditional behavior, but it doesn't seem right?
                        // }
                        // else
                        // {
                        //     _knownBrandingIn_displayedCode = BrandingChangeHandler(
                        //         Subscription.GetBranding(_displayedCode),
                        //         _displayedCode
                        //     );
                        //     if (!_knownBrandingIn_displayedCode)
                        //     {
                        //         BrandingChangeHandler("Default", null); // Review: or just leave unchanged?
                        //     }
                        // }
                        request.PostSucceeded();
                    }
                },
                false
            );
            apiHandler.RegisterEnumEndpointHandler(
                kApiUrlPart + "subscriptionTier",
                request => _subscription.Tier,
                null, // this is read-only
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscriptionSummary",
                request =>
                {
                    string branding = _subscription.BrandingKey ?? "";
                    if (string.IsNullOrEmpty(branding))
                    {
                        request.ReplyWithText("");
                        return;
                    }
                    var html = GetSummaryHtml(branding);
                    request.ReplyWithText(html);
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscriptionExpiration",
                request =>
                {
                    if (_subscription != null && _subscription.GetIntegrityLabel() == "ok")
                        request.ReplyWithText(
                            _subscription
                                .GetExpirationDate()
                                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        );
                    else
                        request.ReplyWithText("");
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "subscriptionCodeIntegrity",
                request =>
                {
                    request.ReplyWithText(_subscription?.GetIntegrityLabel() ?? "none");
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "brandingProjectKey",
                request =>
                {
                    request.ReplyWithText(_subscription.BrandingKey);
                },
                false
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "hasSubscriptionFiles",
                request =>
                {
                    if (!string.IsNullOrEmpty(_subscription.BrandingKey))
                    {
                        request.ReplyWithText("false");
                        return;
                    }
                    var haveFiles = BrandingProject.HaveFilesForBranding(
                        _subscription.BrandingKey ?? ""
                    );

                    request.ReplyWithText(haveFiles ? "true" : "false");
                },
                false
            );
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

        public static Action<string> NotifyPendingSubscriptionChange;
    }
}
