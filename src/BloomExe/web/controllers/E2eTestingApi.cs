using System;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.SubscriptionAndFeatures;
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
        private readonly EditingModel _editingModel;

        public E2eTestingApi(
            CollectionSettings collectionSettings,
            BookSelection bookSelection,
            PublishApi publishApi,
            CollectionModel collectionModel,
            EditingModel editingModel
        )
        {
            _collectionSettings = collectionSettings;
            _bookSelection = bookSelection;
            _publishApi = publishApi;
            _collectionModel = collectionModel;
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

            // GET returns true once the Edit tab has finished loading the page it shows and is
            // editing it. While the Edit tab is still navigating to a page, it silently ignores any
            // command that begins with saving the page (duplicate, delete, jump to another page),
            // and a test that sends one in that window sees nothing happen. There is no signal in
            // the UI for this, so a test polls this before such a command. Read-only.
            apiHandler.RegisterBooleanEndpointHandler(
                kApiUrlPart + "isEditingPage",
                request => _editingModel.StateMachine.Editing,
                null, // read only
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

            // POST body is a JSON array of one to three language tags, e.g. ["en","fr","es"], for
            // Language1, Language2 and Language3. Sets the collection's languages and writes the
            // .bloomCollection file, which a test otherwise has to compose by hand: the Collection
            // Settings dialog is a WinForms surface CDP cannot reach, and its own
            // collectionSettings/changeLanguage endpoint only answers while that dialog is open.
            // Changing a collection's languages still needs the collection to be reopened, exactly
            // as it does for a user who clicks OK in that dialog, so the caller must restart Bloom
            // (src/BloomE2E/helpers/collection.ts setCollectionLanguages does both).
            // Must run on the UI thread because it changes the settings the UI is showing.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "setCollectionLanguages",
                HandleSetCollectionLanguages,
                true
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

            // GET returns the URL of the workspace root document Bloom drives, or an empty string
            // before that browser exists. A run has more than one document carrying the workspace
            // root's markup, so the top bar's test id alone does not identify the right one, and a
            // test that attaches to the wrong one is silently broken: its own typing and clicking
            // work, while every page Bloom loads goes somewhere it cannot see. Compare on the file
            // name, which is unique per document; the rest of the URL is escaped differently by
            // Bloom and by the debugging protocol. Needs the UI thread to read the browser.
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "shellUrl", HandleGetShellUrl, true);

            // GET returns what the Edit tab is doing, as
            // {state, pageId, visible, pageLoadAnnouncements}. A test must not ask that tab to
            // change pages while it is still loading one: the request is queued until the page
            // loads, and a page announces itself more than once, so a request released by the
            // first announcement starts a save that the second one leaves unanswered. Nothing
            // happens after that (see src/BloomE2E/AUTOMATION-DEBT.md). The DOM cannot tell a test
            // any of this, because the frame still holds the page from before the tab switch, so
            // the count of announcements is how a test knows the last one has been and gone. Off
            // the UI thread on purpose: this reads three fields, and a test asks for them exactly
            // when the UI thread is busy.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "editState",
                HandleGetEditState,
                false // does not need the UI thread
            );
        }

        /// <summary>
        /// Reply with what the Edit tab is doing (see the registration above). Before the first
        /// book is selected there is no EditingModel, which reads as NoPage.
        /// </summary>
        private void HandleGetEditState(ApiRequest request)
        {
            var model = Bloom.Edit.EditingModel.ModelForE2eTests;
            // The UI thread can change these fields between one read and the next, so keep
            // pageLoadAnnouncements last. A count read after the state can only be the same or
            // higher, which makes a test see the count still rising and wait again. Read first, it
            // could pair a stale count with a settled state, and a test would stop waiting too
            // soon.
            request.ReplyWithJson(
                new
                {
                    state = (model?.EditTabState ?? State.NoPage).ToString(),
                    pageId = model?.EditTabStatePageId ?? "",
                    visible = model?.Visible ?? false,
                    pageLoadAnnouncements = model?.PageLoadAnnouncements ?? 0,
                }
            );
        }

        /// <summary>
        /// Reply with the URL of the workspace root document Bloom drives (see the registration
        /// above), or an empty string if the main browser is not up yet.
        /// </summary>
        private void HandleGetShellUrl(ApiRequest request)
        {
            request.ReplyWithText(Workspace.WorkspaceView.MainBrowserForE2eTests?.Url ?? "");
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
        /// Reply with the template pages available to the selected book, each with the path of the
        /// template book that holds it. A book made from a template starts with no content page,
        /// because every page of a template is a template page, so a test that needs one adds it.
        /// </summary>
        private void HandleGetTemplatePages(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            var templateBook = book?.FindTemplateBook();
            if (templateBook == null)
            {
                request.ReplyWithJson(new object[0]);
                return;
            }
            var templateBookPath = templateBook.GetPathHtmlFile().Replace('\\', '/');
            var pages = templateBook
                .GetTemplatePagesIdDictionary()
                .Select(pair => new
                {
                    id = pair.Key,
                    label = pair.Value.Caption,
                    templateBookPath,
                })
                .ToArray();
            request.ReplyWithJson(pages);
        }

        /// <summary>
        /// Set the collection's Language1, Language2 and Language3 to the tags in the POST body (a
        /// JSON array of one to three tags), and save the .bloomCollection file. Fewer than three
        /// tags leaves the languages that were not named empty, except that a collection naming
        /// only one language repeats it as Language2, which is what Bloom's own new-collection code
        /// writes.
        ///
        /// This does the same work as clicking OK in the Collection Settings dialog, including
        /// keeping a language that is no longer one of the first three in the collection's list of
        /// languages. Like that dialog, it needs the collection reopened before the change is
        /// everywhere it should be; the caller restarts Bloom.
        /// </summary>
        private void HandleSetCollectionLanguages(ApiRequest request)
        {
            var tags = request.RequiredPostObject<string[]>();
            if (tags == null || tags.Length < 1 || tags.Length > 3)
                throw new ArgumentException(
                    "e2e/setCollectionLanguages takes a JSON array of one to three language tags."
                );
            if (string.IsNullOrWhiteSpace(tags[0]))
                throw new ArgumentException(
                    "e2e/setCollectionLanguages needs a tag for Language1."
                );

            // Start from the collection's own writing systems, so that everything about each
            // language except its tag (the font, the line height, the writing direction) keeps the
            // value it had, then put the requested tag on each one.
            //
            // Each language is named in English ("French", not "français"), which is what a person
            // gets by keeping the English name Bloom's language chooser offers. Bloom calls a name
            // that is not the language's own name for itself a custom name, and a custom name is
            // the one thing it shows verbatim everywhere; leaving the name uncustomized would make
            // each screen name the language in whatever language it liked.
            var pending = new WritingSystem[3];
            for (var i = 0; i < 3; i++)
            {
                pending[i] = _collectionSettings.AllLanguages[i].Clone();
                var tag = i < tags.Length ? tags[i].Trim() : string.Empty;
                pending[i].ChangeTag(tag);
                if (!string.IsNullOrEmpty(tag))
                {
                    // ChangeTag has already replaced the name with the one the language uses for
                    // itself, but it leaves IsCustomName alone. So clear that flag before asking
                    // for the English name: a language that arrived here with a custom name would
                    // otherwise be told "you have a name a person chose", and hand back the name
                    // ChangeTag just computed instead of the English one.
                    pending[i].SetName(pending[i].Name, false);
                    pending[i].SetName(pending[i].GetNameInLanguage("en"), true);
                }
            }
            if (string.IsNullOrEmpty(pending[1].Tag))
            {
                pending[1].ChangeTag(pending[0].Tag);
                // Same reason as the loop above: ChangeTag leaves IsCustomName alone, so a
                // Language2 that arrived here with a custom name would keep the name of the
                // language it just replaced.
                pending[1].SetName(pending[1].Name, false);
                pending[1].SetName(pending[1].GetNameInLanguage("en"), true);
            }

            CollectionSettingsDialog.UpdateLanguageSettings(
                _collectionSettings.AllLanguages,
                pending,
                pending.Select(language => language.FontName).ToArray()
            );
            _collectionSettings.Save();

            request.PostSucceeded();
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
