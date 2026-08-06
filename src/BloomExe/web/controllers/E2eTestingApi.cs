using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.SubscriptionAndFeatures;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Progress;

namespace Bloom.web.controllers
{
    /// <summary>
    /// A collection of endpoints that exist ONLY to support end-to-end / visual-regression
    /// testing (see src/BloomVisualRegressionTests) and the external branding survey tool
    /// (https://github.com/BloomBooks/branding-viewer). They deliberately let a caller do things
    /// that no real user should be able to do, so they are registered ONLY when Bloom is launched in
    /// e2e test mode (the --e2e flag; see Program.RunningE2eTests and ProjectContext). A normal run
    /// never exposes them, in any build configuration. (These used to be compiled into DEBUG builds
    /// only, but CI runs the e2e suite against Release builds, so the guard is now at runtime. The
    /// branding viewer depends on that too: it drives whatever Bloom a tester has installed, which
    /// is a Release build from CI.)
    /// </summary>
    public class E2eTestingApi
    {
        public const string kApiUrlPart = "e2e/";

        private readonly CollectionSettings _collectionSettings;
        private readonly BookSelection _bookSelection;
        private readonly PublishApi _publishApi;
        private readonly CollectionModel _collectionModel;
        private readonly XMatterPackFinder _xmatterPackFinder;

        public E2eTestingApi(
            CollectionSettings collectionSettings,
            BookSelection bookSelection,
            PublishApi publishApi,
            CollectionModel collectionModel,
            XMatterPackFinder xmatterPackFinder
        )
        {
            _collectionSettings = collectionSettings;
            _bookSelection = bookSelection;
            _publishApi = publishApi;
            _collectionModel = collectionModel;
            _xmatterPackFinder = xmatterPackFinder;
        }

        /// <summary>
        /// Register the test-only endpoints. Called from ProjectContext only in e2e test mode.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // POST body is a branding name/descriptor, e.g. "Default", "Local-Community",
            // or an enterprise descriptor like "UEEP[Uzbek]". In production, branding flows
            // from the (checksum-validated) subscription code and cannot be set directly; this
            // endpoint lets tests force a branding so they can screenshot each one.
            // Must run on the UI thread because bringing the book up to date shows a dialog.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "setBranding",
                HandleSetBranding,
                true
            );

            // POST body is an appearance theme name, e.g. "default", "zero-margin-ebook" (see the
            // files in src/content/appearanceThemes). Lets tests screenshot each theme. Must run
            // on the UI thread because bringing the book up to date shows a dialog.
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "setTheme", HandleSetTheme, true);

            // POST (no body needed). Stages the currently selected book as a BloomPUB exactly as
            // the Publish:BloomPub preview does, and replies with the localhost URL of the staged
            // book's .htm file. A test then loads that URL in bloom-player to screenshot how the
            // book looks in the player (which can differ from the edit/preview rendering even when
            // the source book is identical, because it renders the staged output). The test must
            // first put Bloom into the publish tab (POST workspace/selectTab {tab:"publish"}).
            // handleOnUiThread is false to match the production publish/bloompub/updatePreview
            // endpoint: staging must NOT run on the UI thread because its page checks drive an
            // off-thread OffScreenBrowser and would otherwise risk a UI-thread deadlock.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "makeBloomPubPreview",
                HandleMakeBloomPubPreview,
                false
            );

            // GET returns true once the editable collection is loaded and its books are
            // enumerable. Switching to the collection tab reloads its webview, and during that
            // window the collection list is momentarily unavailable; selecting a book then throws
            // (NullReferenceException -> 503) and pops an error box. A test that switches back to
            // the collection tab (e.g. to select the next book) polls this first so it selects
            // only once the collection is ready. Read-only; safe to call off the UI thread.
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "isCollectionReady",
                request => IsCollectionReady(),
                null, // read only
                false // does not need the UI thread
            );

            // POST JSON {"branding":..,"layout":..,"xmatter":..}; any field omitted or null leaves
            // that axis alone. Sets all three in ONE call so a survey walks the
            // branding x layout x xmatter matrix with a single book-update per cell instead of
            // three. Must run on the UI thread because bringing the book up to date shows a dialog.
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "setState", HandleSetState, true);

            // GET returns everything a survey tool needs to build its axes, so it does not need a
            // BloomDesktop checkout to know what exists. This is the whole reason the branding
            // viewer can live in its own repo and ship as a standalone exe: the running Bloom is
            // the source of truth for which brandings/layouts/xmatter packs THIS build has.
            // Read-only; safe off the UI thread.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "surveyOptions",
                HandleSurveyOptions,
                false
            );
        }

        /// <summary>
        /// True if the editable collection is loaded and its book list is available. Mirrors what
        /// selecting a book needs (see CollectionApi.GetCollectionOfRequest), so a test can wait
        /// for this before selecting. Any exception means "not ready yet", so we swallow it.
        /// </summary>
        private bool IsCollectionReady()
        {
            try
            {
                var editable = _collectionModel.TheOneEditableCollection;
                return editable != null && editable.GetBookInfos().Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stage the currently selected book as a BloomPUB and reply with the localhost URL of the
        /// staged .htm file, which a test can load in bloom-player.
        /// </summary>
        private void HandleMakeBloomPubPreview(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            // Fail Fast: a test should only call this once a book is selected. If none is, letting
            // it throw tells us the test is out of order rather than silently returning nothing.
            var stagedUrl = _publishApi.StageBookForBloomPubPreviewForTest(book);
            request.ReplyWithText(stagedUrl);
        }

        /// <summary>
        /// Set the currently selected book's appearance theme to the one named in the POST body,
        /// then make it up to date so its appearance.css is regenerated for that theme.
        /// </summary>
        private void HandleSetTheme(ApiRequest request)
        {
            var theme = request.RequiredPostString();
            var book = _bookSelection.CurrentSelection;
            if (book != null)
            {
                // Mirror what the book settings dialog does when the user picks a theme: set the
                // theme, let the book react (SettingsUpdated regenerates appearance.css from the
                // selected theme), then bring it fully up to date so the change is saved and the
                // preview refreshes.
                book.BookInfo.AppearanceSettings.CssThemeName = theme;
                book.SettingsUpdated();
                book.BringBookUpToDate(new NullProgress());
            }

            request.PostSucceeded();
        }

        /// <summary>
        /// Force the collection to the branding named in the POST body, then make the currently
        /// selected book (if any) up to date so it picks up that branding's files and appearance.
        /// </summary>
        private void HandleSetBranding(ApiRequest request)
        {
            var branding = request.RequiredPostString();
            _collectionSettings.Subscription = MakeSubscriptionForBranding(branding);

            // Bringing the book up to date is what actually copies the branding files into the
            // book folder (BookStorage.LoadCurrentBrandingFilesIntoBookFolder), updates its DOM,
            // saves it, and raises a refresh event for the preview. We update the selected book
            // in place rather than going through CollectionModel.BringBookUpToDate(), which
            // deselects and reselects the book: during that window CurrentBook is null, and an
            // in-flight book-preview image request would throw in
            // BloomServer.ProcessImageFileRequest. There is nothing to update if no book is selected.
            var book = _bookSelection.CurrentSelection;
            if (book != null)
                book.BringBookUpToDate(new NullProgress());

            request.PostSucceeded();
        }

        /// <summary>
        /// Set any combination of branding, layout and xmatter on the current collection/book, then
        /// bring the book up to date once so the render reflects all of them together.
        ///
        /// Unlike the other handlers here, this one catches its own exceptions and reports them
        /// through request.Failed(). A survey walks hundreds of cells and some combinations are
        /// genuinely invalid (a branding whose xmatter the collection does not offer, a layout the
        /// book's stylesheets do not support). One bad cell must not take Bloom down or wedge the
        /// run: the caller logs the message and moves to the next cell. NOTE: request.Failed() puts
        /// the text in the HTTP status reason phrase (response.statusText), not the body.
        /// </summary>
        private void HandleSetState(ApiRequest request)
        {
            string branding = null,
                layout = null,
                xmatter = null;
            try
            {
                // Parse inside the try: a malformed body is exactly the sort of single-cell
                // failure this method is meant to absorb and report.
                var body = request.RequiredPostString();
                var o = JObject.Parse(body);
                branding = (string)o["branding"];
                layout = (string)o["layout"];
                xmatter = (string)o["xmatter"];

                if (!string.IsNullOrEmpty(branding))
                    _collectionSettings.Subscription = MakeSubscriptionForBranding(branding);
                if (!string.IsNullOrEmpty(xmatter))
                    _collectionSettings.XMatterPackName = xmatter;

                // As in HandleSetBranding, we update the selected book in place rather than going
                // through CollectionModel.BringBookUpToDate(), which deselects and reselects and so
                // leaves CurrentBook null for a window in which an in-flight book-preview image
                // request would throw. There is nothing to update if no book is selected.
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
                    $"setState (branding='{branding}', layout='{layout}', xmatter='{xmatter}') failed: {ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// Report the axes a survey can vary on THIS build, plus the current state and the selected
        /// book, so an external tool needs no BloomDesktop source tree.
        /// </summary>
        private void HandleSurveyOptions(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;

            // Layout choices come from the selected book's own stylesheets, so they are the layouts
            // that book actually supports rather than a hard-coded list.
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
                    // The tool keys its capability check off this: a Bloom built before these
                    // endpoints existed 404s, and one that answers is new enough to survey.
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
        /// subscription descriptor, from which Bloom derives the branding key; the tier is
        /// inferred from the descriptor the same way Subscription.CalculateTier does.
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
            // name a descriptor that carries a stable placeholder personalization. Callers that
            // want a specific personalization can instead pass a descriptor like "Acme-LC".
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
