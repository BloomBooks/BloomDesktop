using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.SubscriptionAndFeatures;
using SIL.IO;
using SIL.Progress;

namespace Bloom.web.controllers
{
    /// <summary>
    /// A collection of endpoints that exist ONLY to support end-to-end / visual-regression
    /// testing (see src/BloomVisualRegressionTests). They deliberately let a test do things that
    /// no real user should be able to do, so they are registered ONLY when Bloom is launched in
    /// e2e test mode (the --e2e flag; see Program.RunningE2eTests and ProjectContext). A normal run
    /// never exposes them, in any build configuration. (These used to be compiled into DEBUG builds
    /// only, but CI runs the e2e suite against Release builds, so the guard is now at runtime.)
    /// </summary>
    public class E2eTestingApi
    {
        public const string kApiUrlPart = "e2e/";

        private readonly CollectionSettings _collectionSettings;
        private readonly BookSelection _bookSelection;
        private readonly PublishApi _publishApi;
        private readonly CollectionModel _collectionModel;
        private readonly PageTemplatesApi _pageTemplatesApi;
        private readonly SourceCollectionsList _sourceCollectionsList;
        private readonly EditingModel _editingModel;

        public E2eTestingApi(
            CollectionSettings collectionSettings,
            BookSelection bookSelection,
            PublishApi publishApi,
            CollectionModel collectionModel,
            PageTemplatesApi pageTemplatesApi,
            SourceCollectionsList sourceCollectionsList,
            EditingModel editingModel
        )
        {
            _collectionSettings = collectionSettings;
            _bookSelection = bookSelection;
            _publishApi = publishApi;
            _collectionModel = collectionModel;
            _pageTemplatesApi = pageTemplatesApi;
            _sourceCollectionsList = sourceCollectionsList;
            _editingModel = editingModel;
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

            // GET returns the Edit tab's state as JSON: {"state": "Editing"} once the page being
            // edited has loaded; "Navigating" while a page loads, "SavePending" while a save is in
            // flight, "NoPage" with nothing loaded. An action that saves first (add a page, jump
            // to a page, change layout) is silently dropped unless the state is Editing, and the
            // page iframe showing a .bloom-page does not mean the state has got there yet, so a
            // test that drives the Edit tab waits for this rather than for the DOM.
            // Off the UI thread deliberately: a test polls this while the UI thread is busy
            // loading the page, and reading the state is a single enum field.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "editingState",
                request =>
                    request.ReplyWithJson(new { state = _editingModel.EditingState.ToString() }),
                false // does not need the UI thread
            );

            // GET returns the selected book's pages as JSON: id, caption, and whether the page is
            // front or back matter. A test needs page ids to navigate (editView/jumpToPage takes
            // one), and the page-list thumbnails do not expose which pages are xmatter, so without
            // this a test has to guess from thumbnail markup.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "pages",
                HandleGetPages,
                false // does not need the UI thread
            );

            // GET returns the pages the Add Page dialog would offer for the selected book: the
            // path of its template book, and the id and label of each template page. A test needs
            // these to call the production "addPage" endpoint, and the dialog itself reads them
            // out of the template book's HTML, so there is no other way to ask for them.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "templatePages",
                HandleGetTemplatePages,
                false // does not need the UI thread
            );
        }

        /// <summary>
        /// True if the editable collection is loaded and its book list is available. Mirrors what
        /// selecting a book needs (see CollectionApi.GetCollectionOfRequest), so a test can wait
        /// for this before selecting. Any exception means "not ready yet", so we swallow it.
        /// An EMPTY collection is ready: a test that creates its own collection starts with no
        /// books and then makes one, so "has at least one book" would never come true for it.
        /// </summary>
        private bool IsCollectionReady()
        {
            try
            {
                var editable = _collectionModel.TheOneEditableCollection;
                if (editable == null)
                    return false;
                // Enumerate the list rather than merely asking for it: that is the part that
                // throws while the collection is still loading, which is what we are waiting out.
                editable.GetBookInfos().ToList();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reply with the pages of the selected book, in order. `isContentPage` is false for the
        /// front and back matter, which a test normally wants to skip over.
        /// </summary>
        private void HandleGetPages(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null)
            {
                request.ReplyWithJson(new object[0]);
                return;
            }
            var pages = book.GetPages()
                .Select(page => new
                {
                    id = page.Id,
                    caption = page.Caption,
                    isContentPage = !page.IsXMatter,
                })
                .ToArray();
            request.ReplyWithJson(pages);
        }

        /// <summary>
        /// Reply with the template pages the Add Page dialog would offer the selected book, in the
        /// dialog's order: the book's own template first, then every other template book that has a
        /// "template" folder (Basic Book and the rest). Each page carries the path and title of the
        /// template book that holds it. A book made from a template starts with no content page,
        /// because every page of a template is a template page, so a test that needs one adds it.
        /// A template that is not on this machine is simply absent from the list.
        /// </summary>
        private void HandleGetTemplatePages(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null)
            {
                request.ReplyWithJson(new object[0]);
                return;
            }
            var pages = _pageTemplatesApi
                .GetTemplateBookPathsForAddPage()
                .Where(RobustFile.Exists)
                .Select(path => _sourceCollectionsList.FindAndCreateTemplateBookByFullPath(path))
                .Where(templateBook => templateBook != null)
                .SelectMany(templateBook =>
                {
                    var templateBookPath = templateBook.GetPathHtmlFile().Replace('\\', '/');
                    var templateBookTitle = templateBook.Title;
                    return templateBook
                        .GetTemplatePagesIdDictionary()
                        .Select(pair => new
                        {
                            id = pair.Key,
                            label = pair.Value.Caption,
                            templateBookPath,
                            templateBookTitle,
                        });
                })
                .ToArray();
            request.ReplyWithJson(pages);
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
