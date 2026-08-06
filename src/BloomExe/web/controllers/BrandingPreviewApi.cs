using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Progress;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Lets an external tool see how a book renders under any branding, page layout and xmatter
    /// pack, so that branding work can be reviewed across the whole matrix instead of one
    /// collection at a time. Written for https://github.com/BloomBooks/branding-viewer, which
    /// screenshots each combination and presents them side by side.
    ///
    /// These live under the "external/" url space alongside ExternalApi, which is where Bloom
    /// offers services to tools outside itself, and like those they are registered
    /// unconditionally. There is deliberately no flag to turn them on: the people who need this
    /// are testers running an installed build, and a launch flag is a thing they would have to
    /// remember.
    ///
    /// Note what that means. Branding normally flows from a checksum-validated subscription code
    /// and cannot be set directly; select-branding bypasses that, and because bringing a book up
    /// to date stamps the branding's files into the book folder and saves, a book can be left
    /// wearing a branding its author has no subscription for. That is accepted: every branding
    /// pack already ships inside every Bloom, and anyone determined can already edit a book's
    /// branding.css by hand. This is a convenience, not a lock, and it is not pretending to be one.
    /// </summary>
    public class BrandingPreviewApi
    {
        private const string kUrlPrefix = "external/";

        private readonly CollectionSettings _collectionSettings;
        private readonly BookSelection _bookSelection;
        private readonly XMatterPackFinder _xmatterPackFinder;

        // Called by autofac, which creates the one instance and registers it with the server.
        public BrandingPreviewApi(
            CollectionSettings collectionSettings,
            BookSelection bookSelection,
            XMatterPackFinder xmatterPackFinder
        )
        {
            _collectionSettings = collectionSettings;
            _bookSelection = bookSelection;
            _xmatterPackFinder = xmatterPackFinder;
        }

        /// <summary>
        /// Register the branding-preview endpoints. Called unconditionally from ProjectContext.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // GET: what this build can render. A caller needs no Bloom source tree to build its
            // axes, which is what lets the viewer be a separate tool shipped as one exe.
            // Read-only, so it does not need the UI thread.
            apiHandler.RegisterEndpointHandler(
                kUrlPrefix + "branding-options",
                HandleBrandingOptions,
                false
            );

            // POST JSON {"branding":..,"layout":..,"xmatter":..}; any field omitted or null leaves
            // that axis alone. All three in one call, so walking the matrix costs one book update
            // per combination rather than three. Must run on the UI thread because bringing the
            // book up to date can show a dialog.
            apiHandler.RegisterEndpointHandler(
                kUrlPrefix + "select-branding",
                HandleSelectBranding,
                true
            );
        }

        /// <summary>
        /// Report the brandings, page layouts and xmatter packs available in THIS build, along with
        /// the current state and selected book.
        /// </summary>
        private void HandleBrandingOptions(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;

            // Layout choices come from the selected book's own stylesheets, so they are the layouts
            // that book really supports rather than a fixed list. With no book selected there is
            // nothing to ask, so this is empty until one is; a caller should ask again after
            // selecting.
            var layouts =
                book == null
                    ? new string[0]
                    : book.GetSizeAndOrientationChoices()
                        .Select(l => l.SizeAndOrientation.ToString())
                        .OrderBy(s => s)
                        .ToArray();

            var xmatterKeyForcedByBranding =
                _collectionSettings.GetXMatterPackNameSpecifiedByBrandingOrNull();

            request.ReplyWithJson(
                new
                {
                    // A caller uses this to tell a user their Bloom is too old, rather than
                    // leaving them with an unexplained 404.
                    bloomVersion = Application.ProductVersion,
                    brandings = GetAvailableBrandingKeys(),
                    layouts,
                    xmatterOfferings = _xmatterPackFinder
                        .GetXMattersToOfferInSettings(xmatterKeyForcedByBranding)
                        .Select(p => p.Key)
                        .ToArray(),
                    current = new
                    {
                        branding = _collectionSettings.Subscription.Descriptor,
                        layout = book?.GetLayout().SizeAndOrientation.ToString(),
                        xmatter = _collectionSettings.XMatterPackName,
                    },
                    book = book == null
                        ? null
                        : new { path = book.FolderPath, title = book.TitleBestForUserDisplay },
                }
            );
        }

        /// <summary>
        /// Set any combination of branding, layout and xmatter, then bring the book up to date once
        /// so the render reflects all of them together.
        ///
        /// This catches its own exceptions and reports them through request.Failed(), which most
        /// handlers should not do. A survey walks hundreds of combinations and some are genuinely
        /// invalid: a branding whose xmatter this collection does not offer, or a layout the book's
        /// stylesheets do not support. One bad combination must not take Bloom down or leave the
        /// caller's run wedged, so we report it and let the caller move on to the next.
        /// NOTE: request.Failed() puts the text in the HTTP status reason phrase, not the body.
        /// </summary>
        private void HandleSelectBranding(ApiRequest request)
        {
            string branding = null,
                layout = null,
                xmatter = null;
            try
            {
                // Parse inside the try: a malformed body is exactly the sort of single-combination
                // failure this method exists to absorb and report.
                var o = JObject.Parse(request.RequiredPostString());
                branding = (string)o["branding"];
                layout = (string)o["layout"];
                xmatter = (string)o["xmatter"];

                if (!string.IsNullOrEmpty(branding))
                    _collectionSettings.Subscription = MakeSubscriptionForBranding(branding);
                if (!string.IsNullOrEmpty(xmatter))
                    _collectionSettings.XMatterPackName = xmatter;

                // Update the selected book in place rather than going through
                // CollectionModel.BringBookUpToDate(), which deselects and reselects it: during
                // that window CurrentSelection is null, and an in-flight book-preview image request
                // would throw. The whole-book preview renders CurrentSelection directly, so
                // re-selecting the same path would not refresh it anyway.
                var book = _bookSelection.CurrentSelection;
                if (book != null)
                {
                    if (!string.IsNullOrEmpty(layout))
                        book.SetLayout(
                            new Layout
                            {
                                SizeAndOrientation = SizeAndOrientation.FromString(layout),
                            }
                        );
                    book.BringBookUpToDate(new NullProgress());
                }

                request.PostSucceeded();
            }
            catch (Exception ex)
            {
                request.Failed(
                    $"select-branding (branding='{branding}', layout='{layout}', xmatter='{xmatter}') failed: {ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// The branding keys this build ships, read from the distribution's branding folder. A
        /// branding is a folder containing branding.json; "source" subfolders hold artwork sources
        /// and are not brandings.
        /// </summary>
        private static string[] GetAvailableBrandingKeys()
        {
            var brandingRoot = BloomFileLocator.GetOptionalBrowserDirectory("branding");
            if (string.IsNullOrEmpty(brandingRoot) || !Directory.Exists(brandingRoot))
                return new string[0];
            return Directory
                .GetDirectories(brandingRoot)
                .Where(d => RobustFile.Exists(Path.Combine(d, "branding.json")))
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Build a Subscription that yields the requested branding, without needing a real
        /// (checksum-validated, unexpired) subscription code. The branding name is used as the
        /// subscription descriptor, from which Bloom derives the branding key; the tier is inferred
        /// from the descriptor the same way Subscription.CalculateTier does.
        /// </summary>
        private static Subscription MakeSubscriptionForBranding(string branding)
        {
            // The empty/"Default" branding is exactly what you get with no subscription at all.
            if (string.IsNullOrWhiteSpace(branding) || branding == "Default")
                return new Subscription("");

            // The Local-Community branding's template contains a "{personalization}" token (the
            // local community's name), which Bloom fills from the part of the descriptor before
            // "-LC" (see Subscription.Personalization). A bare "Local-Community" descriptor has no
            // such part, so it would make BookData.MergeInPersonalization throw. Give the friendly
            // name a descriptor carrying a stable placeholder personalization; a caller wanting a
            // specific one passes a descriptor like "Acme-LC" instead.
            if (branding == "Local-Community" || branding == "Local Community")
                branding = "Sample-LC";

            var lower = branding.ToLowerInvariant();
            SubscriptionTier tier;
            if (lower.EndsWith("-lc"))
                tier = SubscriptionTier.LocalCommunity;
            else if (lower.EndsWith("-pro"))
                tier = SubscriptionTier.Pro;
            else
                tier = SubscriptionTier.Enterprise;

            return Subscription.ForUnitTestWithOverrideTierOrDescriptor(tier, branding);
        }
    }
}
