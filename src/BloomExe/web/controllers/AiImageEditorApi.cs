using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.SafeXml;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Core.ClearShare;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.web.controllers
{
    /// <summary>
    /// AI Image Editor integration — host (Bloom) side.
    ///
    /// TERMS
    ///   "AI image editor" always means the separate `bloom-ai-image-tools` web app, never
    ///   Bloom's own edit tab. Bloom is "the host".
    ///
    /// WHAT THIS IS
    ///   "Edit with AI…" (the image context menu) opens the AI image editor as an IFRAME
    ///   OVERLAY inside Bloom's existing edit-tab WebView2. It is NOT a separate window,
    ///   and Bloom does NOT import the AI image editor's code: it is a self-contained web
    ///   app loaded by URL. There is no npm/bundler dependency between the two projects.
    ///
    /// THE FRONT-END HALF
    ///   src/BloomBrowserUI/bookEdit/aiImageEditor/ — read its AGENTS.md first: those files are
    ///   split by which browser frame they run in (overlay in the top window, live-page work in
    ///   the page iframe), and that split is what the two-plane design below rests on.
    ///
    /// WHERE THE AI IMAGE EDITOR COMES FROM  (see GetAiImageEditorUrl)
    ///   DEFAULT: {ServerUrl}/bloom/aiImageEditor/index.html — its built app
    ///            ("dist-app"), served same-origin by BloomServer so there's no CORS.
    ///            The build copies dist-app/ from the installed `bloom-ai-image-tools`
    ///            package into output/browser/aiImageEditor/ (a viteStaticCopy target,
    ///            mirroring the bloom-player copy); `./go.sh` stages the same at dev time
    ///            (scripts/aiImageEditorBuild.mjs), falling back to building a local checkout
    ///            until the package is published and added as a dependency.
    ///   LINKED : set BLOOM_AI_IMAGE_EDITOR_URL to the AI image editor's own Vite dev server (HMR).
    ///            `./go.sh --with bloom-ai-image-tools` does this automatically (it starts
    ///            the dev server and points Bloom at it); GetAiImageEditorUrl honors it.
    ///
    /// TWO COMMUNICATION PLANES
    ///   1. HTTP, AI image editor / front-end JS -> this controller, over Bloom's server:
    ///        aiImageEditor/saveThenLaunch  what the menu command posts: save the page being
    ///                                    edited (needed before we can enumerate the book's
    ///                                    images), then call openAiImageEditor() in the top
    ///                                    window. The one C#->browser call here; see the method.
    ///        aiImageEditor/launch        mint session, make folders, enumerate book
    ///                                    images + history, return the launch payload.
    ///        aiImageEditor/file          GET/POST/DELETE files under .ai-image-editor/.
    ///        aiImageEditor/commit        apply the chosen replacements to the book.
    ///        aiImageEditor/saveCredentials  persist the user's OpenRouter API key.
    ///   2. window.postMessage on channel "bloom-ai-image-tools", between the overlay JS
    ///      (aiImageEditorOverlay.ts, in the TOP window) and the AI image editor's iframe: ready /
    ///      init / commit / cancel / log / ack. The overlay JS — NOT this class — sends
    ///      `init` (built from the launch reply) and tears the overlay down. Image BYTES
    ///      never cross postMessage; they move only as files via aiImageEditor/file.
    ///
    /// DATA ON DISK
    ///   Per-book folder `<book>/.ai-image-editor/` which contains a `history/` subfolder
    ///   of `<id>.png` images and `<id>.json` sidecars. The history subfolder is the
    ///   source of truth.
    ///
    /// SECURITY
    ///   A per-launch session token (query param) gates /file, /commit, /saveCredentials.
    ///   File names are allow-listed; page/result ids are charset-restricted; reused
    ///   source URLs must resolve inside the book folder (no path traversal).
    ///
    /// COMMIT SPLIT
    ///   Off-page images are edited directly in the whole-book DOM here and saved. The
    ///   currently-open page is owned by the live browser, so those replacements are
    ///   returned as {oldSrc,newSrc,copyright,creator,license}; the overlay hands them to the
    ///   page frame's applyAiImageEditorReplacements(), which applies them via Bloom's
    ///   changeImageByElement() (aiImageEditorPageCommands.ts).
    ///
    /// AI IMAGE EDITOR REPO: bloom-ai-image-tools — App.tsx (mode=bloom-iframe),
    ///   services/host/BloomHostBridge.ts (createIframeBloomHostBridge),
    ///   components/BloomEmbeddedShell.tsx.
    /// </summary>
    public class AiImageEditorApi
    {
        private readonly BookSelection _bookSelection;

        /// <summary>Set by the EditingView constructor — used at commit time to detect the
        /// currently-open page (which the live browser owns) so we don't edit it from here.</summary>
        public EditingView View { get; set; }

        // Minted at launch; torn down on the next launch (see EndSession). The AI image
        // editor's `cancel`/`commit` are handled in the overlay JS, which removes the overlay.
        private string _sessionToken;

        // The folder of the book the session was launched for. Every session-gated request
        // re-resolves _bookSelection.CurrentSelection, so if the user somehow switches books
        // while an overlay is still up, requests from that overlay must fail rather than
        // read/write the newly selected book's files or commit against its DOM.
        private string _sessionBookFolderPath;

        // The image formats the AI image editor can actually work with — the ones it can load
        // as input, and the raster formats it stores/serves/commits as results. Deliberately a
        // short list: formats the AI image editor can't edit (e.g. svg, tif, bmp, gif) are
        // excluded so we never offer, serve, or commit an image the AI image editor can't
        // handle. Single source of truth — AllowedFileName (below), IsImageFileName, the
        // history-result probe, the reused-source check, and the whole-book image list all
        // derive from this set, so the lists can't drift apart. Must stay in sync with the
        // front-end's editable-format list (BloomBrowserUI/.../aiImageEditorImageFormats.ts).
        private static readonly HashSet<string> AllowedImageExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
        };

        // The subset of AllowedImageExtensions that Bloom's import processing can actually
        // open. PalasoImage decodes through GDI+, which has no WebP codec, so handing it a
        // .webp throws every time — those are copied verbatim instead (see
        // ImportImageIntoBookFolder). Kept as its own list rather than "everything but webp"
        // so that adding a format above is a deliberate decision here too.
        private static readonly HashSet<string> ProcessableImageExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ".png",
            ".jpg",
            ".jpeg",
        };

        // Regex alternation of the allowed extensions without the leading dot
        // (e.g. "png|jpg|jpeg|..."), derived from AllowedImageExtensions.
        private static readonly string AllowedImageExtensionPattern = string.Join(
            "|",
            AllowedImageExtensions.Select(e => Regex.Escape(e.TrimStart('.')))
        );

        // Files the /file endpoint may read/write/delete: the two top-level json files,
        // history image bytes (any supported raster extension), and the per-image
        // history sidecars (history/<id>.json) that travel with each image.
        private static readonly Regex AllowedFileName = new Regex(
            @"^(state\.json|connection\.json|history/[a-zA-Z0-9_\-]+\.(?:"
                + AllowedImageExtensionPattern
                + @"|json))$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>
        /// Created per project (see ProjectContext); the selected book is resolved from
        /// <paramref name="bookSelection"/> at request time, not stored.
        /// </summary>
        public AiImageEditorApi(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

        /// <summary>
        /// Registers all of the AI Image Editor's API endpoints (launch, file persistence,
        /// commit, credentials) with Bloom's API handler.
        /// </summary>
        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/saveThenLaunch",
                HandleSaveThenLaunch,
                handleOnUiThread: true,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/launch",
                HandleLaunch,
                handleOnUiThread: true,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/file",
                HandleFile,
                handleOnUiThread: false,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/commit",
                HandleCommit,
                handleOnUiThread: true,
                requiresSync: true
            );
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/saveCredentials",
                HandleSaveCredentials,
                handleOnUiThread: false,
                requiresSync: false
            );
        }

        /// <summary>
        /// The selected book's ".ai-image-editor" working folder (state, history images,
        /// sidecars), or null when no book is selected.
        /// </summary>
        private string GetAiImageEditorFolderPath()
        {
            var folderPath = _bookSelection.CurrentSelection?.FolderPath;
            return string.IsNullOrEmpty(folderPath)
                ? null
                : Path.Combine(folderPath, ".ai-image-editor");
        }

        private string GetAiImageEditorUrl()
        {
            // The AI image editor is served by BloomServer from output/browser/aiImageEditor/.
            // The go.mjs launcher (scripts/aiImageEditorBuild.mjs) builds it from the local
            // bloom-ai-image-tools checkout and stages it there, so `./go.sh` "just works"
            // with no separate dev server, in both Debug and Release.
            //
            // Someone working on the AI image editor itself, who wants hot-module reload, can
            // instead point Bloom at its own Vite dev server by setting BLOOM_AI_IMAGE_EDITOR_URL,
            // e.g. BLOOM_AI_IMAGE_EDITOR_URL=http://localhost:3000/ and running `pnpm dev` in the
            // bloom-ai-image-tools checkout.
            var overrideUrl = GetLinkedEditorUrlOverride();
            if (!string.IsNullOrWhiteSpace(overrideUrl))
                return overrideUrl;
            return $"{BloomServer.ServerUrl}/bloom/aiImageEditor/index.html";
        }

        /// <summary>
        /// The env var a developer sets to point Bloom at the AI image editor's own Vite dev
        /// server instead of the staged build.
        /// </summary>
        internal const string kLinkedEditorUrlEnvironmentVariable = "BLOOM_AI_IMAGE_EDITOR_URL";

        /// <summary>
        /// The name this variable had before it was renamed to match the rest of the feature.
        /// Still honored so that a developer who has the old name in a shell profile or launch
        /// config keeps getting their linked dev server rather than silently falling back to
        /// the staged build. Transitional: delete once nobody is using it.
        /// </summary>
        internal const string kLinkedEditorUrlObsoleteEnvironmentVariable = "BLOOM_AI_EDITOR_URL";

        // So the deprecation notice appears once per run rather than once per read.
        private static bool _warnedAboutObsoleteEditorUrlVariable;

        /// <summary>
        /// The linked-dev-server URL the developer asked for, or null if they didn't ask for
        /// one. Prefers <see cref="kLinkedEditorUrlEnvironmentVariable"/> and falls back to
        /// <see cref="kLinkedEditorUrlObsoleteEnvironmentVariable"/>, logging once when the
        /// obsolete name is what supplied the value.
        /// </summary>
        internal static string GetLinkedEditorUrlOverride()
        {
            var url = Environment.GetEnvironmentVariable(kLinkedEditorUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(url))
                return url;

            url = Environment.GetEnvironmentVariable(kLinkedEditorUrlObsoleteEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (!_warnedAboutObsoleteEditorUrlVariable)
            {
                _warnedAboutObsoleteEditorUrlVariable = true;
                Logger.WriteEvent(
                    $"{kLinkedEditorUrlObsoleteEnvironmentVariable} is obsolete; please rename it"
                        + $" to {kLinkedEditorUrlEnvironmentVariable}. Using it for now."
                );
            }
            return url;
        }

        /// <summary>The image the user right-clicked, as the page frame sends it to
        /// <see cref="HandleSaveThenLaunch"/> and as we hand it back to the browser once the page
        /// has been saved. See IAiImageEditorTarget in aiImageEditorShared.ts. The page frame sends
        /// only the slot's index among the page's image containers; we fill in the page id, since
        /// we are the ones who know which page we saved, and the overlay (in the top window) has
        /// no page DOM of its own to read it from.
        /// </summary>
        private class SaveThenLaunchRequest
        {
            public string pageId { get; set; }

            /// <summary>Which image slot of that page the user clicked, as its index among the
            /// page's image containers in document order. The page frame counts them, because
            /// only it can see which slot the user clicked; we just carry it back to the
            /// overlay, where it names the book image "{pageId}:{slotIndex}".
            /// </summary>
            public int slotIndex { get; set; }
        }

        /// <summary>
        /// Saves the page the user is editing, then opens the AI image editor on it.
        ///
        /// The save is the point of this endpoint. Everything we tell the AI image editor about
        /// the book — the image list from <see cref="EnumerateBookImages"/>, and on commit each
        /// slot's current src — is read from the SAVED book DOM, but an image the user has just
        /// added exists only in the live page (Bloom deliberately doesn't save on an image change;
        /// see EditingModel.UpdateImageInBrowser and BL-16330). Launching against the unsaved DOM
        /// opened the AI Image Editor with an empty "Image to Edit" slot (BL-16682); it would also have
        /// made the current page's commit results describe an image the live page no longer shows,
        /// and left <see cref="DeleteSupersededAiImageFiles"/> blind to a file the live page uses.
        ///
        /// Two separate things follow from the fact that saving always ends in a navigation.
        ///
        /// WHERE the overlay lives: not in the page iframe, which that navigation replaces every
        /// time — hence the top window, like the image-gallery and copyright/license commands (see
        /// aiImageEditorOverlay.ts and the comments on those commands in canvasControlRegistry.ts).
        ///
        /// WHEN we open it: once the browser has a page again, via
        /// EditingModel.RunAfterNextPageLoad. Opening from SaveThen's doAfterSaveToDisk directly is
        /// tempting, since the top window is alive at that moment — but that moment is immediately
        /// before the navigation, and the navigation is not always confined to the page iframe.
        /// EditingView.StartNavigationToEditPage reloads the whole workspace root when
        /// MemoryUtils.SystemIsShortOfMemory(), which is Bloom's own private bytes past ~2GB —
        /// the ordinary state of a long editing session on a big book. Opening from
        /// doAfterSaveToDisk there meant the page saved correctly and the AI Image Editor never appeared,
        /// with no message: openAiImageEditor doesn't even build the overlay synchronously; it
        /// POSTs launch first and builds it in the reply, a whole round trip after the reload
        /// began. Waiting for the page load costs nothing and is immune to all three routes.
        /// doAfterSaveToDisk is still where we ASK for that, though — see the body — because it
        /// only runs when the save actually reached disk, which is how a failed save leaves the
        /// editor closed instead of opening it on a book DOM we know to be stale.
        ///
        /// To see that failure for yourself, temporarily make ShouldDoFullReload() return true —
        /// its own comment invites exactly this — rather than trying to grow Bloom past 2GB.
        /// </summary>
        private void HandleSaveThenLaunch(ApiRequest request)
        {
            // Must be read before SaveThen: by the time our callbacks run the request is complete.
            // Deliberately unguarded: the only caller is launchAiImageEditor in
            // aiImageEditorPageCommands.ts, which always sends {slotIndex}, so a parse failure means
            // we broke our own contract and we want to hear about it with the real exception rather
            // than a generic "invalid payload" that says nothing (see AGENTS.md, "Don't be overly
            // defensive about error handling").
            var payload = request.RequiredPostObject<SaveThenLaunchRequest>();

            var model = View?.Model;
            var pageId = model?.CurrentPage?.Id;
            if (model == null || string.IsNullOrEmpty(pageId))
            {
                request.Failed("No page is open for editing");
                return;
            }
            payload.pageId = pageId;

            // Ask NOW to be opened on the next page load, and record separately whether the book
            // DOM turned out to be sound. Both halves matter, for different reasons.
            //
            // Asking now, rather than from the callbacks below: OnTabAboutToChange discards the
            // queued request when the user leaves the Edit tab. If we only queued it later, from
            // doAfterSaveToDisk, that discard could run first — on a save still in flight — and we
            // would then re-arm behind it, so the AI Image Editor sprang open when the user came back to
            // that page. (Devin caught that; queueing up front puts the discard reliably after us.)
            var bookDomIsSound = false;
            model.RunAfterNextPageLoad(loadedPageId =>
            {
                // Not the page we saved: the user navigated meanwhile, so the image we were asked
                // to edit isn't there to edit.
                if (loadedPageId != pageId)
                    return;
                // The save was attempted and failed. Leave the AI Image Editor closed: the book DOM is
                // still stale, so we would be opening it on exactly the out-of-date data this
                // endpoint exists to prevent, and a commit from there would call book.Save() again
                // on top of whatever went wrong (disk full, a corrupt image, out of memory). The
                // user is not left wondering — EditingStateMachine has already shown them "Bloom
                // had trouble saving a page...". (JohnThomson raised this in review.)
                if (!bookDomIsSound)
                    return;
                OpenEditorInBrowser(payload);
            });

            // Saving is synchronous now (see PageSnapshot), so "did the book actually reach disk?"
            // is simply "did this return without throwing" -- which is what we need to know before
            // reading image sources back out of the file. A refusal to save (mid-navigation, say)
            // is not a problem: those states are on their way to a page load which brings the DOM
            // up to date anyway. Only an actual failure, such as the disk being full, is.
            try
            {
                model.SaveCurrentPageAndBook();
                bookDomIsSound = true;
            }
            catch (Exception)
            {
                bookDomIsSound = false;
                throw;
            }

            request.PostSucceeded();
        }

        /// <summary>
        /// Tells the browser to open the AI image editor overlay on <paramref name="target"/>. The
        /// browser owns the overlay (only it can postMessage to the AI Image Editor's iframe), so all this
        /// side does is call its entry point; see openAiImageEditor in aiImageEditorOverlay.ts.
        /// Fire-and-forget, like EditingModel.UpdateImageInBrowser's call to changeImage: there is
        /// nothing here to wait for.
        ///
        /// It does wait for workspaceBundle to exist, though. We are called the instant the PAGE
        /// iframe reports loaded, and on the whole-workspace-reload route (see
        /// HandleSaveThenLaunch) the root document and that iframe load in parallel, with
        /// window.workspaceBundle assigned at the very end of workspaceRoot.ts. If the iframe wins
        /// that race, calling straight in would throw inside a fire-and-forget script — silently
        /// doing nothing on exactly the route this design exists to survive. So poll for it, as
        /// workspaceFrames.doWhenWorkspaceBundleLoaded does, bounded so a genuinely absent bundle
        /// says so once instead of polling forever.
        /// </summary>
        private void OpenEditorInBrowser(SaveThenLaunchRequest target)
        {
            var arg = JsonConvert.SerializeObject(target);
            var script =
                $@"(function () {{
    var tries = 0;
    (function open() {{
        var bundle = window.workspaceBundle;
        if (bundle && bundle.openAiImageEditor) {{
            bundle.openAiImageEditor({arg});
            return;
        }}
        if (++tries < 100) {{
            window.setTimeout(open, 50);
        }} else {{
            console.error(
                'Bloom: workspaceBundle never appeared, so the AI image editor could not be opened.'
            );
        }}
    }})();
}})();";
            View?.Browser?.RunJavascriptFireAndForget(script);
        }

        /// <summary>
        /// Starts an AI image editor session for the selected book: mints the session token,
        /// ensures the .ai-image-editor folders exist, and replies with everything the overlay
        /// JS needs to boot the AI image editor's iframe (its URL, book image list, history,
        /// credentials).
        /// </summary>
        private void HandleLaunch(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null)
            {
                request.Failed("No book selected");
                return;
            }

            // The AI image editor app is a separate published package (bloom-ai-image-tools,
            // a dependency in package.json) staged into browser/aiImageEditor at build time
            // (see GetAiImageEditorUrl). We keep this "might be missing" guard on purpose: it
            // is handy during development, where a build can legitimately lack the staged app
            // (e.g. a local checkout that hasn't been built/staged yet). Fail the launch with
            // a clear message rather than opening an overlay whose iframe would just 404.
            if (
                string.IsNullOrWhiteSpace(GetLinkedEditorUrlOverride())
                && BloomFileLocator.GetBrowserFile(optional: true, "aiImageEditor", "index.html")
                    == null
            )
            {
                request.Failed("The AI Image Editor is not included in this build of Bloom.");
                return;
            }

            // Tear down any previous session.
            EndSession();

            _sessionToken = Guid.NewGuid().ToString("N");
            _sessionBookFolderPath = book.FolderPath;

            // H3: ensure .ai-image-editor and history subfolder exist.
            var aiImageEditorFolder = GetAiImageEditorFolderPath();
            Directory.CreateDirectory(Path.Combine(aiImageEditorFolder, "history"));

            var httpBase = $"{BloomServer.ServerUrlWithBloomPrefixEndingInSlash}api/aiImageEditor";

            // Return the data the JS needs to create the iframe overlay. The AI image editor
            // runs in iframe mode and gets its `init` from the overlay JS (which builds it
            // from this reply and posts it to the iframe), so the whole-book image list must
            // travel here rather than over any C#->iframe channel.
            request.ReplyWithJson(
                new
                {
                    editorUrl = GetAiImageEditorUrl(),
                    httpBase,
                    sessionToken = _sessionToken,
                    book = new { id = book.BookInfo.Id, title = book.BookInfo.Title },
                    bookImages = EnumerateBookImages(book),
                    // The history folder is the source of truth; enumerate it so images
                    // (and their sidecars) appear even when state.json doesn't list them.
                    history = EnumerateHistoryImages(book),
                    references = Array.Empty<object>(),
                    // Bloom owns the OpenRouter key: supply the per-user stored key so the AI
                    // image editor doesn't have to ask for it again. It hands any newly
                    // obtained key back via aiImageEditor/saveCredentials.
                    apiKey = OpenRouterCredentialStore.GetApiKey(),
                    // In a Playground template book all features are unlocked for
                    // "try it out", so the AI image editor opens — but it's a shared demo
                    // context, so it must not let the user set/save an OpenRouter API key.
                    // The AI image editor disables its credential UI when this is true;
                    // HandleSaveCredentials also refuses to persist.
                    demoOnly = book.IsPlayground,
                    // Let the AI image editor reveal its developer/tester tools (e.g. the
                    // "Local Dummy (No AI)" model, for cost-free testing). The AI image
                    // editor hides those tools unless the host opts in, so ordinary
                    // release/beta builds never expose them to end users.
                    showDeveloperTools = ShouldShowDeveloperTools(),
                }
            );
        }

        /// <summary>
        /// Environment variable a tester can set to get the AI image editor's tester tools
        /// (currently the "Local Dummy (No AI)" model) on a channel that would not normally
        /// offer them, e.g. an installed beta. See <see cref="kTesterToolsOnValues"/> for
        /// what counts as "on".
        /// </summary>
        internal const string kShowTesterToolsEnvironmentVariable =
            "BLOOM_AI_IMAGE_EDITOR_TESTER_TOOLS";

        /// <summary>
        /// The values of <see cref="kShowTesterToolsEnvironmentVariable"/> that mean "on",
        /// compared case-insensitively after trimming. We accept several spellings because
        /// the people setting this are testers typing into a Windows environment-variable
        /// box, not developers reading our source.
        /// </summary>
        internal static readonly string[] kTesterToolsOnValues = new[]
        {
            "true",
            "t",
            "y",
            "yes",
            "1",
        };

        /// <summary>
        /// Whether to tell the AI image editor to reveal its developer/tester tools. The
        /// "Local Dummy (No AI)" model is the one such tool today; it lets a tester exercise
        /// the editor without spending real AI credits. These are always on for developer and
        /// alpha/unstable builds. On any other channel (beta, release) a tester can opt in by
        /// setting <see cref="kShowTesterToolsEnvironmentVariable"/>, which is how we let a
        /// beta tester try the editor for free without shipping the tools to end users
        /// (BL-16770).
        /// </summary>
        internal static bool ShouldShowDeveloperTools()
        {
            if (ApplicationUpdateSupport.IsDevOrAlpha)
                return true;
            var optIn = Environment
                .GetEnvironmentVariable(kShowTesterToolsEnvironmentVariable)
                ?.Trim();
            return optIn != null
                && kTesterToolsOnValues.Contains(optIn, StringComparer.OrdinalIgnoreCase);
        }

        private class SaveCredentialsRequest
        {
            public string apiKey { get; set; }
        }

        /// <summary>
        /// Receives the user's OpenRouter API key from the AI image editor (manual key entry)
        /// and persists it per-user via <see cref="OpenRouterCredentialStore"/>. A null/empty
        /// apiKey clears the stored key (sign-out). Session-gated so a stray frame can't
        /// overwrite the user's stored key.
        /// </summary>
        private void HandleSaveCredentials(ApiRequest request)
        {
            if (!HasValidSession(request))
                return;

            // Defense in depth for the Playground "demo" case (see HandleLaunch): never
            // persist a key obtained during a Playground session, even if a stray frame
            // posts here despite the AI image editor's disabled credential UI.
            if (_bookSelection.CurrentSelection?.IsPlayground == true)
            {
                request.PostSucceeded();
                return;
            }

            SaveCredentialsRequest payload;
            try
            {
                payload = request.RequiredPostObject<SaveCredentialsRequest>();
            }
            catch (Exception)
            {
                request.Failed(HttpStatusCode.BadRequest, "Invalid credentials payload");
                return;
            }

            OpenRouterCredentialStore.Save(payload.apiKey);
            request.PostSucceeded();
        }

        // Invalidates the current session. Called at the start of each launch to tear down
        // any prior session; the overlay itself is created and removed by the overlay JS.
        private void EndSession()
        {
            _sessionToken = null;
            _sessionBookFolderPath = null;
        }

        /// <summary>
        /// True if the request carries the current session token AND the selected book is
        /// still the one the session was launched for; otherwise fails the request. The book
        /// check matters because the handlers resolve the book at request time, so a stale
        /// overlay must not operate on a different book than it was launched on.
        /// </summary>
        private bool HasValidSession(ApiRequest request)
        {
            var session = request.GetParamOrNull("session");
            if (_sessionToken == null || session != _sessionToken)
            {
                request.Failed(HttpStatusCode.Unauthorized, "Invalid or expired session");
                return false;
            }

            if (_bookSelection.CurrentSelection?.FolderPath != _sessionBookFolderPath)
            {
                request.Failed(
                    HttpStatusCode.Unauthorized,
                    "The selected book changed since the AI Image Editor was launched"
                );
                return false;
            }

            return true;
        }

        /// <summary>
        /// The AI image editor's persistence endpoint: GET/POST/DELETE of individual files
        /// under the book's .ai-image-editor folder. File names are restricted to the
        /// AllowedFileName allow-list, so the AI image editor can only ever touch its own
        /// state/history files.
        /// </summary>
        private void HandleFile(ApiRequest request)
        {
            // Answer the CORS preflight before the session gate: the browser sends OPTIONS
            // automatically with none of our application query params (including "session"),
            // so if HasValidSession ran first it would 401 the preflight and block the real
            // cross-origin request. This only matters for the LINKED dev workflow, where the
            // AI image editor's own Vite dev server (localhost:3000) fetches this endpoint
            // from a different origin; the shipped product serves it same-origin.
            if (request.HttpMethod == HttpMethods.Options)
            {
                request.ReplyWithText("");
                return;
            }

            if (!HasValidSession(request))
            {
                return;
            }

            var name = request.GetParamOrNull("name");
            if (string.IsNullOrEmpty(name) || !AllowedFileName.IsMatch(name))
            {
                request.Failed(System.Net.HttpStatusCode.BadRequest, "Invalid file name");
                return;
            }

            var aiImageEditorFolder = GetAiImageEditorFolderPath();
            if (aiImageEditorFolder == null)
            {
                request.Failed("No book selected");
                return;
            }

            var relativePath = name.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(aiImageEditorFolder, relativePath);

            switch (request.HttpMethod)
            {
                case HttpMethods.Get:
                    if (!RobustFile.Exists(fullPath))
                    {
                        request.Failed(System.Net.HttpStatusCode.NotFound, "Not found");
                        return;
                    }
                    if (IsImageFileName(name))
                        request.ReplyWithImage(fullPath);
                    else
                        request.ReplyWithFileContent(fullPath);
                    break;

                case HttpMethods.Post:
                    WriteRequestBodyToFile(fullPath, request.RawPostStream);
                    request.PostSucceeded();
                    break;

                case HttpMethods.Delete:
                    // Under the file's lock like the write, so a delete can't land in the
                    // middle of one (see WriteRequestBodyToFile).
                    lock (GetFileLock(fullPath))
                    {
                        if (RobustFile.Exists(fullPath))
                            RobustFile.Delete(fullPath);
                    }
                    request.PostSucceeded();
                    break;

                default:
                    request.Failed(
                        System.Net.HttpStatusCode.MethodNotAllowed,
                        "Method not allowed"
                    );
                    break;
            }
        }

        // One lock object per file this session has written or deleted under
        // .ai-image-editor. Tiny (a handful of history files per book) and never cleaned up,
        // deliberately: a lock we discarded while a request still held it would be no lock at
        // all. Case-insensitive because the keys are Windows paths.
        private static readonly Dictionary<string, object> _locksByFilePath = new Dictionary<
            string,
            object
        >(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The lock object that serializes CHANGES to one file of the AI image editor's folder
        /// (see <see cref="WriteRequestBodyToFile"/> for why they have to be serialized). A GET
        /// deliberately doesn't take it: a read can't corrupt anything, and making a response
        /// stream wait behind a multi-MB upload would cost more than it saves. What a read can
        /// hit is the moment of the swap — RobustFile.Move(overWrite) is a delete followed by a
        /// move, not an atomic replace, so a GET landing in that window can 404 — but that
        /// window is as old as this endpoint, and nothing reads a file while writing it: the AI
        /// image editor GETs history files when it starts, and writes them when it generates or
        /// commits.
        /// </summary>
        private static object GetFileLock(string fullPath)
        {
            lock (_locksByFilePath)
            {
                if (!_locksByFilePath.TryGetValue(fullPath, out var fileLock))
                {
                    fileLock = new object();
                    _locksByFilePath[fullPath] = fileLock;
                }
                return fileLock;
            }
        }

        /// <summary>
        /// Saves a POST to <see cref="HandleFile"/> at <paramref name="fullPath"/>, creating
        /// its folder if needed. Takes ownership of <paramref name="body"/> (the request's post
        /// stream) and disposes it.
        ///
        /// The body is streamed straight to disk, because history images are multi-MB and we
        /// don't want to buffer them whole in memory (RawPostData would). It lands in a temp
        /// file which is then swapped in, so an upload that dies partway can't leave a
        /// truncated file where a previous good one was. An empty body is a valid save of an
        /// empty file, not a no-op: the AI image editor must never be told "saved" while stale
        /// content survives on disk.
        ///
        /// Writes to a given file are SERIALIZED within this process (which is all Bloom needs:
        /// one Bloom has a given collection open), because two of them for the same file are
        /// ordinary traffic from the AI image editor, not a pathological case (BL-16702): one
        /// generated image assigned to two book-image slots makes its commit call putFile once
        /// per slot, concurrently (Promise.all), and both calls name the same
        /// history/&lt;resultId&gt;.png. Unserialized, the two collided on the shared temp path
        /// — RobustFile.Create opens with FileShare.None and, unlike most of RobustFile, does
        /// not retry — so the loser threw IOException, which the API layer turns into a 503
        /// "Cannot access ..." plus a yellow box. The AI image editor reports that as "Failed
        /// to write host file", and since it writes all the files before posting the commit,
        /// ONE such failure abandoned the whole commit: the user's Replace did nothing at all.
        ///
        /// Note that serializing those duplicate writes is all we do about them: the AI image
        /// editor still sends the same bytes once per slot, so one image in four slots posts the
        /// same few MB four times. We are deliberately not asking it to send each result only
        /// once (JohnThomson's call, BL-16702): using one picture in several places is expected
        /// to be rare, and when it happens the picture is likely to be a small decorative one,
        /// so the wasted bandwidth isn't worth optimizing — especially as it is bandwidth to
        /// localhost.
        ///
        /// Internal for testing.
        /// </summary>
        internal static void WriteRequestBodyToFile(string fullPath, Stream body)
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            lock (GetFileLock(fullPath))
            {
                var tempPath = fullPath + ".tmp";
                var reachedTheSwap = false;
                try
                {
                    using (body)
                    using (var output = RobustFile.Create(tempPath))
                    {
                        // The body stream is null for an empty body (no entity body); that
                        // still means "save an empty file" here, so just leave the freshly
                        // created temp file empty rather than copying.
                        body?.CopyTo(output);
                    }
                    reachedTheSwap = true;
                    RobustFile.Move(tempPath, fullPath, true); // true: overwrite
                }
                finally
                {
                    // An upload that died partway (the client went away, the disk filled up)
                    // must not leave its half-written temp behind. Nothing would ever serve or
                    // enumerate it — the name is neither allow-listed nor an image extension —
                    // but it can be multi-MB, and it would sit in the book's folder for the
                    // life of the book. The file it was going to replace is untouched, so the
                    // temp is all there is to clean up.
                    //
                    // A failure in the SWAP is the one case we leave alone, because there the
                    // temp may be the only copy of the bytes left: Move(overWrite) deletes the
                    // destination and then moves, so a failure between those two steps has
                    // already taken the old file with it. A stray temp is much the lesser evil
                    // — and the next write of this file overwrites it anyway. (Devin spotted
                    // that tidying up unconditionally could throw away both copies.)
                    if (!reachedTheSwap)
                        DeleteTempFileIgnoringErrors(tempPath);
                }
            }
        }

        /// <summary>
        /// Removes the temp file left by a failed write. Deliberately swallows anything that
        /// goes wrong: we are already on our way out with the real exception — the one the
        /// caller needs and the one that becomes the request's error — and failing to tidy up
        /// must not replace it with a less informative one.
        /// </summary>
        private static void DeleteTempFileIgnoringErrors(string tempPath)
        {
            try
            {
                if (RobustFile.Exists(tempPath))
                    RobustFile.Delete(tempPath);
            }
            catch (Exception e)
            {
                Logger.WriteError(
                    $"AiImageEditorApi: could not remove the temp file {tempPath} left behind by a failed write",
                    e
                );
            }
        }

        // ------------------------------------------------------------------
        // Whole-book image sharing & replacement
        // ------------------------------------------------------------------

        // Book page ids and AI image editor result ids are echoed back to us on commit and
        // interpolated into XPath / file paths, so we restrict them to a safe charset.
        private static readonly Regex SafeId = new Regex(
            @"^[a-zA-Z0-9_\-]+$",
            RegexOptions.Compiled
        );

        private static bool IsImageFileName(string name) =>
            AllowedImageExtensions.Contains(Path.GetExtension(name));

        /// <summary>
        /// Parses a commit slot id of the form "{pageId}:{ordinal}". Returns false (leaving
        /// pageId null and ordinal -1) when the id is empty, has no ':' separator, has a page
        /// id outside the safe charset (<see cref="SafeId"/>), or has a non-integer ordinal.
        /// A negative ordinal parses successfully here; the holder-index range check is done
        /// separately by the caller. Internal for testing.
        /// </summary>
        internal static bool TryParseIncomingId(
            string incomingId,
            out string pageId,
            out int ordinal
        )
        {
            pageId = null;
            ordinal = -1;
            if (string.IsNullOrEmpty(incomingId))
                return false;
            var separator = incomingId.LastIndexOf(':');
            if (separator <= 0)
                return false;
            var candidatePageId = incomingId.Substring(0, separator);
            if (
                !SafeId.IsMatch(candidatePageId)
                || !int.TryParse(incomingId.Substring(separator + 1), out var candidateOrdinal)
            )
                return false;
            pageId = candidatePageId;
            ordinal = candidateOrdinal;
            return true;
        }

        /// <summary>
        /// The image slots of one page, in document order: its image containers. A slot's index
        /// in this list is its whole identity, and it is what "{pageId}:{ordinal}" holds.
        /// Internal for testing.
        ///
        /// An image container is exactly what a user may replace, which is why nothing here
        /// filters. The branding, license and QR-code images live outside any container, so
        /// they are not slots and cannot be edited or overwritten.
        ///
        /// slotIndexOnPage in aiImageEditorPageCommands.ts numbers the same containers on the live
        /// page, so the index the page frame sends at launch means the same thing here. It has
        /// one exclusion this does not need: Bloom injects controls into the live page, and a
        /// save strips them, so they are never in the DOM we read.
        /// </summary>
        internal static SafeXmlElement[] SelectImageSlotsOnPage(SafeXmlElement page) =>
            page.SafeSelectNodes(
                    ".//*[contains(concat(' ', normalize-space(@class), ' '), ' "
                        + HtmlDom.kImageContainerClass
                        + " ')]"
                )
                .OfType<SafeXmlElement>()
                .ToArray();

        /// <summary>
        /// The element of a slot that carries the picture: the container's own img, or the
        /// container itself when it wears the picture as a background image. Null when the slot
        /// holds neither, which a slot with no picture at all can do.
        /// </summary>
        internal static SafeXmlElement GetImageElementOfSlot(SafeXmlElement slot)
        {
            var img = slot.SelectSingleNode("./img") as SafeXmlElement;
            if (img != null)
                return img;
            return (slot.GetAttribute("style") ?? "").Contains("background-image") ? slot : null;
        }

        /// <summary>
        /// Locates the bytes for a history result by id. The AI image editor may store a
        /// result under any supported raster extension, so we probe
        /// history/&lt;resultId&gt;.&lt;ext&gt; across <see cref="AllowedImageExtensions"/>
        /// and return the first match. The caller must have already validated resultId against
        /// <see cref="SafeId"/>.
        /// </summary>
        private static bool TryFindHistoryResultFile(
            string aiImageEditorFolder,
            string resultId,
            out string path
        )
        {
            var historyFolder = Path.Combine(aiImageEditorFolder, "history");
            foreach (var extension in AllowedImageExtensions)
            {
                var candidate = Path.Combine(historyFolder, resultId + extension);
                if (RobustFile.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
            path = null;
            return false;
        }

        private class CommitRequest
        {
            public List<CommitReplacement> replacements { get; set; }
        }

        private class CommitReplacement
        {
            /// <summary>The book image slot to replace, formatted "{pageId}:{ordinal}".</summary>
            public string incomingId { get; set; }

            /// <summary>The AI image editor's result id; its bytes live at
            /// history/{resultId}.png.</summary>
            public string resultId { get; set; }

            /// <summary>For a reused existing image: its host-served URL, resolved to a book file.</summary>
            public string sourceUrl { get; set; }

            /// <summary>
            /// The credits (intellectual-property metadata) the AI image editor determined for
            /// this result: carried over from whatever image the result was derived from, and
            /// possibly amended there. Null or all-empty means the AI image editor decided
            /// this result should have no credits — e.g. the user opened an existing
            /// illustration but then made an entirely new image — and Bloom must not invent
            /// any for it. Only meaningful for a generated/uploaded result
            /// (<see cref="resultId"/>).
            /// </summary>
            public ImageCredits credits { get; set; }
        }

        /// <summary>
        /// The intellectual-property metadata that travels between Bloom and the AI image
        /// editor for a single image, in both directions: outbound on each launch
        /// <c>bookImages</c> entry (the image's current credits) and inbound on each commit
        /// replacement (the credits the AI image editor chose for the result). These are the
        /// ClearShare fields Bloom reads and writes. Any field may be null/empty. The AI image
        /// editor treats the whole object as opaque — it carries it along an edit chain and
        /// hands it back verbatim, never reading or filling in a field — so these names are
        /// the wire (JSON) names and match the AI image editor's ImageCredits.
        ///
        /// The license takes TWO fields because that is what an image file can actually hold
        /// (dc:rights plus the cc:license URL), and either alone loses information: a CC
        /// license may carry license notes as well as its URL, and flattening the pair into
        /// one string dropped those notes (BL-16603).
        /// </summary>
        internal class ImageCredits
        {
            public string copyrightNotice { get; set; }
            public string creator { get; set; }

            /// <summary>The Creative Commons license URL, its canonical form. Empty for a
            /// custom license or none at all, neither of which has a URL.</summary>
            public string licenseUrl { get; set; }

            /// <summary>The free-text rights statement (dc:rights): for a custom license the
            /// license itself, and for a Creative Commons license the extra "license notes"
            /// restrictions the user added alongside it. Empty for none.</summary>
            public string licenseRightsStatement { get; set; }

            public string attributionUrl { get; set; }
            public string collectionName { get; set; }
            public string collectionUri { get; set; }
        }

        /// <summary>
        /// The three data-* attributes Bloom mirrors on an image element from the image file's
        /// embedded metadata. Not the same thing as <see cref="ImageCredits"/>: this is the
        /// small subset the edit-tab DOM carries, in the DOM's own spelling (a license here is
        /// its short ClearShare token, e.g. "cc-by", not the URL the AI image editor uses), and
        /// every field is a string, never null, because that is what the attributes hold.
        /// Sent to the front-end for a current-page replacement, which is the one slot this
        /// class can't write itself. Property names are the wire (JSON) names, matching
        /// PageEditingModel.ImageInfoForJavascript, which the front-end already consumes.
        /// </summary>
        internal class ImageCreditAttributes
        {
            public string copyright { get; set; }
            public string creator { get; set; }
            public string license { get; set; }
        }

        /// <summary>
        /// What to call a page when naming one of its image slots to the user: "Page 3" for a
        /// numbered page, or the page's own name (e.g. "Front Cover") for front or back matter.
        /// Null when the page says neither, in which case the slot goes unlabelled.
        /// In the user interface language.
        /// </summary>
        /// <remarks>
        /// Deliberately not HtmlDom.GetNumberOrLabelOfPageWhereElementLives: that returns the
        /// number bare, with no "Page", and answers "unknown" for a page whose
        /// data-page-number attribute is missing rather than empty, which HtmlDom itself can
        /// produce (see BL-12903). "unknown" would be worse than no label at all.
        /// </remarks>
        internal static string GetPageNameForImageSlotLabel(SafeXmlElement page)
        {
            // Back matter pages do carry page numbers, but they are clearer by name.
            var number = page.GetAttribute("data-page-number");
            if (!string.IsNullOrWhiteSpace(number) && !HtmlDom.IsBackMatterPage(page))
                // "Page" plus the number, rather than a "Page {0}" string of our own: the
                // word already exists in our strings, and a translator meeting a bare format
                // string has less to go on than the word itself.
                return LocalizationManager.GetString("ReaderSetup.PageHeader", "Page")
                    + " "
                    + number;

            var label = page.SelectSingleNode("./div[@class='pageLabel']")?.InnerText.Trim();
            if (string.IsNullOrEmpty(label))
                return null;
            // Page labels are localized under a dynamic id built from the English label, the
            // same way the page list in the Edit tab does it.
            return LocalizationManager.GetString("TemplateBooks.PageLabel." + label, label);
        }

        /// <summary>
        /// Names one image slot for the user. A page with a single image slot needs no more than
        /// the page name. A page with several says which slot this is: "Page 3 - Canvas
        /// Background" for the canvas background image, "Page 3 - Image 1" for the pictures on
        /// top of it. Every empty slot looks the same in the AI image editor, so this label is
        /// the only thing that tells two of them apart (BL-16744).
        /// </summary>
        /// <param name="pageName">from <see cref="GetPageNameForImageSlotLabel"/>, may be null</param>
        /// <param name="isCanvasBackground">true for the canvas background image</param>
        /// <param name="imageNumber">
        /// the 1-based position of this slot among the page's images, counting every slot that is
        /// not being named as the canvas background. A page with several canvases names none of
        /// them, so there every slot is counted. Ignored when isCanvasBackground is true.
        /// </param>
        /// <param name="slotCount">how many image slots this page offers in all</param>
        internal static string BuildImageSlotLabel(
            string pageName,
            bool isCanvasBackground,
            int imageNumber,
            int slotCount
        )
        {
            // The only picture on the page. There is nothing to tell it apart from.
            if (slotCount < 2)
                return pageName;

            var whichSlot = isCanvasBackground
                ? LocalizationManager.GetString(
                    "AiImageEditor.SlotLabel.CanvasBackground",
                    "Canvas Background"
                )
                : LocalizationManager.GetString("EditTab.CustomPage.Image", "Image")
                    + " "
                    + imageNumber;
            if (string.IsNullOrEmpty(pageName))
                return whichSlot;
            // The separator is not localizable. It carries no meaning to translate, and a
            // string of nothing but two placeholders and a dash gives a translator no context.
            return pageName + " - " + whichSlot;
        }

        /// <summary>
        /// Names every image slot of one page, in the order the page offers them. Kept in one
        /// place because a slot's name depends on what else the page holds, not just on itself.
        /// </summary>
        /// <param name="pageName">from <see cref="GetPageNameForImageSlotLabel"/>, may be null</param>
        /// <param name="isCanvasBackground">
        /// for each slot of the page, in order, whether it is the background image of a canvas
        /// </param>
        internal static List<string> BuildImageSlotLabelsForPage(
            string pageName,
            IReadOnlyList<bool> isCanvasBackground
        )
        {
            // "Canvas Background" names a slot only when the page has exactly one canvas. A
            // Picture Dictionary page has six, so six background images; calling them all
            // "Canvas Background" would be six identical labels, which is the very confusion
            // the label exists to remove. There, plain numbering tells them apart.
            var nameTheBackground = isCanvasBackground.Count(background => background) == 1;

            var labels = new List<string>();
            var imageNumber = 0;
            foreach (var background in isCanvasBackground)
            {
                // The background is named, not numbered, so the numbering of the pictures on
                // top of it starts at 1 whether or not the page has a background.
                var nameAsBackground = background && nameTheBackground;
                if (!nameAsBackground)
                    imageNumber++;
                labels.Add(
                    BuildImageSlotLabel(
                        pageName,
                        nameAsBackground,
                        imageNumber,
                        isCanvasBackground.Count
                    )
                );
            }
            return labels;
        }

        /// <summary>
        /// Enumerates every image the user is allowed to change across the whole book — all
        /// pages including front cover and xmatter, including empty placeholder slots —
        /// excluding only branding and license images. Each entry is a reference (id +
        /// servable URL); image bytes are fetched lazily by the AI image editor, never
        /// inlined.
        /// </summary>
        private List<object> EnumerateBookImages(Bloom.Book.Book book)
        {
            var images = new List<object>();
            var folderAsUrlPrefix = book.FolderPath.Replace("\\", "/");
            var pages = book
                .OurHtmlDom.RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .OfType<SafeXmlElement>();

            foreach (var page in pages)
            {
                var pageId = page.GetAttribute("id");
                if (string.IsNullOrEmpty(pageId) || !SafeId.IsMatch(pageId))
                    continue;
                var pageName = GetPageNameForImageSlotLabel(page);

                var slots = SelectImageSlotsOnPage(page);
                // The slots this page offers, gathered before any is added to the result,
                // because a slot's label depends on how many the page has: a page with one
                // image says just "Page 2", a page with more says "Page 2 - Image 1" and so on.
                var slotsOnThisPage =
                    new List<(
                        string id,
                        string src,
                        bool isPlaceholder,
                        bool isCanvasBackground,
                        ImageCredits credits
                    )>();
                // Ordinal is the index within the full slot list, so a slot we decline to offer
                // below still holds its place. That is what lets the page frame send an index it
                // worked out for itself, without knowing which slots we kept.
                for (var ordinal = 0; ordinal < slots.Length; ordinal++)
                {
                    var element = GetImageElementOfSlot(slots[ordinal]);
                    if (element == null)
                        continue;

                    var relativePath = HtmlDom.GetImageElementUrl(element).PathOnly.NotEncoded;
                    if (string.IsNullOrEmpty(relativePath))
                        continue;

                    // Only offer images the AI image editor can actually edit. A book may
                    // legitimately contain formats the AI image editor can't open (e.g. an svg
                    // illustration); those slots are simply omitted from the list rather than
                    // handed over to fail.
                    if (!IsImageFileName(relativePath))
                        continue;

                    slotsOnThisPage.Add(
                        (
                            id: pageId + ":" + ordinal,
                            src: (folderAsUrlPrefix + "/" + relativePath).ToLocalhost(),
                            // The AI image editor shows its own placeholder graphic for empty
                            // slots rather than trying to load the (book-less)
                            // placeHolder.png.
                            isPlaceholder: ImageUtils.IsPlaceholderImageFilename(relativePath),
                            // A bloom-canvas can hold one background image with pictures on
                            // top of it. The background is worth naming as such, because the
                            // user thinks of it as the page's picture, not as "image 1".
                            isCanvasBackground: HtmlDom.IsBackgroundImage(element),
                            // The image's current credits, so a result derived from it can
                            // carry (or amend) them. The AI image editor owns the credit
                            // *decision* and hands back whatever it chose on commit; Bloom
                            // only embeds that into the file. Null when the image has no
                            // usable metadata.
                            credits: GetCreditsForImageFile(book.FolderPath, relativePath)
                        )
                    );
                }

                // Now that the whole page is known, name its slots and add them.
                var labels = BuildImageSlotLabelsForPage(
                    pageName,
                    slotsOnThisPage.Select(slot => slot.isCanvasBackground).ToList()
                );
                for (var i = 0; i < slotsOnThisPage.Count; i++)
                {
                    var slot = slotsOnThisPage[i];
                    images.Add(
                        new
                        {
                            id = slot.id,
                            src = slot.src,
                            pageLabel = labels[i],
                            isPlaceholder = slot.isPlaceholder,
                            credits = slot.credits,
                        }
                    );
                }
            }

            return images;
        }

        /// <summary>
        /// Enumerates the per-book history folder so the AI image editor can rebuild its
        /// history from the files on disk — the folder is the source of truth. Each image is
        /// returned as a reference (id + servable book-folder URL) plus the parsed contents of
        /// its sibling &lt;id&gt;.json sidecar, when present. Bytes are fetched lazily by URL,
        /// never inlined. Images with no sidecar (e.g. dropped in by hand) are still returned;
        /// the AI image editor recovers them. Empty placeholder files and non-image files
        /// (including the sidecars themselves) are skipped.
        /// </summary>
        private List<object> EnumerateHistoryImages(Bloom.Book.Book book)
        {
            var result = new List<object>();
            var aiImageEditorFolder = GetAiImageEditorFolderPath();
            if (aiImageEditorFolder == null)
                return result;
            var historyFolder = Path.Combine(aiImageEditorFolder, "history");
            if (!Directory.Exists(historyFolder))
                return result;

            var folderAsUrlPrefix = book.FolderPath.Replace("\\", "/");

            foreach (var imagePath in Directory.EnumerateFiles(historyFolder))
            {
                var fileName = Path.GetFileName(imagePath);
                if (!IsImageFileName(fileName))
                    continue; // skip the .json sidecars and anything that isn't an image

                // The id is echoed back on commit and interpolated into paths, so keep it safe.
                var id = Path.GetFileNameWithoutExtension(fileName);
                if (string.IsNullOrEmpty(id) || !SafeId.IsMatch(id))
                    continue;

                // Skip empty placeholder files (e.g. book-image slots written with no bytes).
                try
                {
                    if (new FileInfo(imagePath).Length == 0)
                        continue;
                }
                catch (Exception)
                {
                    continue;
                }

                object metadata = null;
                var sidecarPath = Path.Combine(historyFolder, id + ".json");
                if (RobustFile.Exists(sidecarPath))
                {
                    try
                    {
                        metadata = JsonConvert.DeserializeObject(
                            RobustFile.ReadAllText(sidecarPath)
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"AiImageEditor: ignoring malformed sidecar {sidecarPath}: {ex.Message}"
                        );
                    }
                }

                var url = (
                    folderAsUrlPrefix + "/.ai-image-editor/history/" + fileName
                ).ToLocalhost();
                result.Add(
                    new
                    {
                        id,
                        url,
                        metadata,
                    }
                );
            }

            return result;
        }

        /// <summary>
        /// Applies the AI image editor's chosen replacements to the book. Each replacement
        /// names a slot (pageId:ordinal) anywhere in the book and an AI image editor result
        /// whose bytes we read from the per-book history folder — so no image bytes cross the
        /// bridge.
        /// </summary>
        private void HandleCommit(ApiRequest request)
        {
            if (!HasValidSession(request))
                return;

            var book = _bookSelection.CurrentSelection;
            if (book == null)
            {
                request.Failed("No book selected");
                return;
            }

            var aiImageEditorFolder = GetAiImageEditorFolderPath();
            if (aiImageEditorFolder == null)
            {
                request.Failed("No book selected");
                return;
            }

            CommitRequest payload;
            try
            {
                payload = request.RequiredPostObject<CommitRequest>();
            }
            catch (Exception)
            {
                request.Failed(HttpStatusCode.BadRequest, "Invalid commit payload");
                return;
            }

            // The currently displayed page is owned by the live browser + editing state
            // machine; we cannot reload it from C# without first saving the live DOM (which
            // would clobber a Storage-only image change), and navigating while editing throws.
            // So we apply changes to NON-current pages here (Storage DOM + Save), and hand the
            // current page's replacements back for the front-end to apply via Bloom's own
            // changeImage() against the live DOM.
            var currentPageId = View?.Model?.CurrentPage?.Id;

            var replacements = payload?.replacements ?? new List<CommitReplacement>();
            var results = new List<object>();
            var pagesToSyncToDataDiv = new HashSet<SafeXmlElement>();
            // The old image files displaced by off-page replacements. After the book is saved
            // we delete the ones we generated ("ai-image*") that nothing references any more,
            // so repeated AI edits of the same slot don't pile up orphaned files (BL-16523/G1).
            var supersededOffPageFiles = new List<string>();
            var appliedCount = 0;
            var savedAnyOffPage = false;
            // Reused across replacements so a page's whole-document lookup happens only once.
            var pageCache = new Dictionary<string, SafeXmlElement>();

            foreach (var replacement in replacements)
            {
                var applied = TryApplyReplacement(
                    book,
                    aiImageEditorFolder,
                    replacement,
                    currentPageId,
                    pageCache,
                    out var error,
                    out var isCurrentPage,
                    out var oldSrc,
                    out var newSrc,
                    out var creditAttributes,
                    out var pageNeedingDataDivSync
                );
                results.Add(
                    new
                    {
                        incomingId = replacement?.incomingId,
                        ok = applied,
                        error,
                        isCurrentPage,
                        oldSrc,
                        newSrc,
                        // Only set for a current-page slot, the one case where the front-end
                        // (not this code) writes the element's mirrored credit attributes.
                        copyright = creditAttributes?.copyright,
                        creator = creditAttributes?.creator,
                        license = creditAttributes?.license,
                    }
                );
                if (applied)
                {
                    appliedCount++;
                    if (!isCurrentPage)
                    {
                        savedAnyOffPage = true;
                        if (pageNeedingDataDivSync != null)
                            pagesToSyncToDataDiv.Add(pageNeedingDataDivSync);
                        // Remember the file this off-page slot used to point at; it may now be
                        // orphaned. (Current-page slots are repointed by the front-end via
                        // changeImage(), so their old files are not ours to delete here.)
                        supersededOffPageFiles.Add(oldSrc);
                    }
                }
            }

            if (savedAnyOffPage)
            {
                // Cover/xmatter images are bound through the data-div, which "wins" on a
                // full save (BookData.SynchronizeDataItemsFromContentsOfElement). Harvest
                // each such page into the data-div first so our edits aren't reverted.
                foreach (var page in pagesToSyncToDataDiv)
                    book.BookData.SuckInDataFromEditedDom(page);

                book.Save();
            }

            // The off-page slots now point at their new files (and the book is saved), so any
            // ai-image file we generated earlier that nothing references any more is safe to
            // remove. This runs against the current book DOM, so a file still used by another
            // slot — or by the current page (which the front-end has yet to repoint) —
            // survives.
            DeleteSupersededAiImageFiles(book.FolderPath, book.OurHtmlDom, supersededOffPageFiles);

            // The "AI Image Editor Closed" and "Change Picture" events are reported by
            // aiImageEditorOverlay.ts when it gets this reply, not here. For a slot on the page the user
            // has open we only STAGE the replacement and hand it back; whether it actually landed
            // is something only the browser learns, so counting a staged slot as applied here
            // would report success we do not have.
            request.ReplyWithJson(
                new
                {
                    ok = appliedCount == replacements.Count,
                    appliedCount,
                    results,
                }
            );
        }

        /// <summary>
        /// Applies one replacement to the slot named by incomingId ("{pageId}:{ordinal}"),
        /// bringing the new image bytes (from history or a reused book file) into the book
        /// folder under a fresh name via <see cref="ImportImageIntoBookFolder"/>, which also
        /// applies Bloom's normal import processing. An off-page slot is edited here in the storage DOM
        /// (caller saves); a current-page slot is left untouched — we return oldSrc/newSrc and
        /// the new file's <paramref name="creditAttributes"/> (via the out params) for the
        /// front-end to apply to the live DOM. Returns false with <paramref name="error"/> set
        /// when the replacement can't be applied.
        /// </summary>
        private bool TryApplyReplacement(
            Bloom.Book.Book book,
            string aiImageEditorFolder,
            CommitReplacement replacement,
            string currentPageId,
            Dictionary<string, SafeXmlElement> pageCache,
            out string error,
            out bool isCurrentPage,
            out string oldSrc,
            out string newSrc,
            out ImageCreditAttributes creditAttributes,
            out SafeXmlElement pageForDataDivSync
        )
        {
            error = null;
            isCurrentPage = false;
            oldSrc = null;
            newSrc = null;
            creditAttributes = null;
            pageForDataDivSync = null;

            if (replacement == null || string.IsNullOrEmpty(replacement.incomingId))
            {
                error = "Missing incomingId";
                return false;
            }

            if (!TryParseIncomingId(replacement.incomingId, out var pageId, out var ordinal))
            {
                error = "Malformed incomingId";
                return false;
            }

            // Resolve the page by id at most once per commit: the //div[@id=...] lookup is a
            // whole-document scan, and a commit often targets several images on the same page.
            if (!pageCache.TryGetValue(pageId, out var page))
            {
                page =
                    book.OurHtmlDom.RawDom.SelectSingleNode("//div[@id='" + pageId + "']")
                    as SafeXmlElement;
                pageCache[pageId] = page; // cache misses too, so a repeat bad id doesn't rescan
            }
            if (page == null)
            {
                error = "Page not found: " + pageId;
                return false;
            }

            var slots = SelectImageSlotsOnPage(page);
            if (ordinal < 0 || ordinal >= slots.Length)
            {
                error = "Image index out of range";
                return false;
            }
            var element = GetImageElementOfSlot(slots[ordinal]);
            if (element == null)
            {
                error = "Image element not found";
                return false;
            }

            isCurrentPage = pageId == currentPageId;
            oldSrc = HtmlDom.GetImageElementUrl(element).PathOnly.NotEncoded;

            // Locate the bytes for the new image. A generated/uploaded result lives in
            // the per-book history folder (referenced by resultId); a reused existing
            // image is referenced by its host-served URL (resolved to a book file).
            string sourceBytesPath;
            if (!string.IsNullOrEmpty(replacement.resultId))
            {
                if (!SafeId.IsMatch(replacement.resultId))
                {
                    error = "Invalid resultId";
                    return false;
                }
                // The AI image editor may save a result in any supported raster format, so
                // look it up by id across the allowed extensions rather than assuming .png
                // (which would fail to commit a .jpg/.webp result that EnumerateHistoryImages
                // happily lists).
                if (
                    !TryFindHistoryResultFile(
                        aiImageEditorFolder,
                        replacement.resultId,
                        out sourceBytesPath
                    )
                )
                {
                    error = "Result image not found";
                    return false;
                }
            }
            else if (
                !string.IsNullOrEmpty(replacement.sourceUrl)
                && TryResolveServedUrlToBookFile(
                    book.FolderPath,
                    replacement.sourceUrl,
                    out sourceBytesPath
                )
            )
            {
                // resolved to an existing file within the book folder
            }
            else
            {
                error = "Missing or invalid replacement source";
                return false;
            }

            // Bring the bytes into the book folder, import-processed and under a fresh
            // "ai-image*" name.
            var newFileName = ImportImageIntoBookFolder(
                sourceBytesPath,
                book.FolderPath,
                // Only a freshly generated/uploaded result needs resizing: a reused book image was
                // already import-processed on its own way in, so shrinking it again would gain
                // nothing and would cost it a generation of quality.
                // This used to cite credits as the reason too — resizing rewrites the file through
                // GraphicsMagick, which drops the XMP packet the licence lives in. That no longer
                // applies: ImportImageIntoBookFolder now re-attaches the metadata after processing,
                // whichever path it took. So scope is the whole justification for this argument.
                resizeIfNeeded: !string.IsNullOrEmpty(replacement.resultId)
            );
            // A generated result can arrive as a PNG for a slot the book held as a JPEG,
            // which can be several times the size; re-encode it as a JPEG when that wins
            // enough to be worth it (BL-16645).  A reused image is already in the book
            // folder in the appropriate format, so we don't need to process it again.
            if (!string.IsNullOrEmpty(replacement.resultId))
                newFileName = ConvertPngToJpegIfItBloatsTheJpegItReplaces(
                    book.FolderPath,
                    oldSrc,
                    newFileName
                );
            newSrc = newFileName;

            // A generated result arrives with no intellectual-property metadata of its own, so
            // whatever credits it should have, we have to write in. The AI image editor owns
            // that decision and sends them in replacement.credits; nothing means the result
            // gets no credits. Reused existing images (the sourceUrl branch) keep their own
            // file metadata, so we only do this for a freshly generated/uploaded result. See
            // EmbedCreditsInNewImageFile for why the credits have to be written into the FILE
            // and not just the DOM attributes.
            if (!string.IsNullOrEmpty(replacement.resultId))
                EmbedCreditsInNewImageFile(book.FolderPath, newFileName, replacement.credits);

            // Bloom mirrors an image file's IP metadata on the element as data-copyright/
            // data-creator/data-license, and the edit tab's credits indicator reads
            // data-copyright to decide whether to show "missing information". Those attributes
            // describe the OLD file, so they have to be re-derived from the new one; carrying
            // them forward would leave the page claiming credits the new file doesn't have
            // (BL-16603).
            if (isCurrentPage)
            {
                // Leave the live (current) page to the front-end: it will call Bloom's
                // changeImage() with newSrc and these attributes so the canvas + normal save
                // flow handle it.
                creditAttributes = ReadCreditAttributes(book.FolderPath, newFileName);
                return true;
            }

            HtmlDom.SetImageElementUrl(
                element,
                UrlPathString.CreateFromUnencodedString(newFileName)
            );
            // Now that the element points at the new file, Bloom's own updater can re-derive
            // the mirrored attributes for us.
            ImageUpdater.UpdateImgMetadataAttributesToMatchImage(
                book.FolderPath,
                element,
                new NullProgress()
            );

            if (element.HasAttribute("data-book"))
                pageForDataDivSync = page;

            return true;
        }

        /// <summary>
        /// Brings the bytes for a replacement into the book folder under a fresh "ai-image*"
        /// name, running the same import processing a user-added image gets — downscale
        /// anything oversized, convert a non-web format to PNG — instead of the verbatim copy
        /// this used to do (BL-16645). AI services can return very large PNGs, and an
        /// unprocessed one bloats the book folder for every sync, upload and backup.
        ///
        /// The NAME matters as much as the bytes: <see cref="DeleteSupersededAiImageFiles"/>
        /// reclaims our old files by their "ai-image" prefix, but ProcessAndSaveImageIntoFolder
        /// names its output after the SOURCE file, which for us is an opaque history id. So we
        /// let it write, then rename onto the name we reserved. Passing isSameFile:false
        /// guarantees it wrote a brand-new file (GetUnusedFilename picked that name), so the
        /// rename can never disturb a file another slot shares. Processing may change the
        /// extension (a non-web format becomes .png, .jpeg becomes .jpg), so the reserved name
        /// takes its extension from what was actually produced rather than from the source.
        ///
        /// Anything we can't process is copied verbatim rather than lost — unprocessed but
        /// intact, which is what this code did for every format before BL-16645. That covers
        /// .webp (see <see cref="ProcessableImageExtensions"/>), a placeholder (which
        /// ProcessAndSaveImageIntoFolder deliberately short-circuits on, writing nothing), and
        /// an image that fails to process at all. Internal for testing.
        /// </summary>
        /// <param name="resizeIfNeeded">
        /// False for a reused book image, which was already import-processed on its own way in, so
        /// resizing it again would gain nothing and would cost it a generation of quality. (It used
        /// to matter for credits as well; it no longer does, since this method re-attaches the
        /// metadata after processing whichever path it took.)
        /// </param>
        /// <returns>The name (no path) of the new file in the book folder.</returns>
        internal static string ImportImageIntoBookFolder(
            string sourceBytesPath,
            string bookFolderPath,
            bool resizeIfNeeded = true
        )
        {
            string processedName = null;
            if (
                ProcessableImageExtensions.Contains(Path.GetExtension(sourceBytesPath))
                && !ImageUtils.IsPlaceholderImageFilename(sourceBytesPath)
            )
            {
                try
                {
                    using (var imageInfo = PalasoImage.FromFileRobustly(sourceBytesPath))
                    {
                        processedName = ImageUtils.ProcessAndSaveImageIntoFolder(
                            imageInfo,
                            bookFolderPath,
                            isSameFile: false,
                            resizeFileIfNeeded: resizeIfNeeded
                        );
                        // Only re-attach metadata we actually read something from. Writing an empty
                        // model over the processed file could STRIP what
                        // ProcessAndSaveImageIntoFolder had preserved by copying the file verbatim —
                        // turning "we found no metadata" into "there is now no metadata" — and there
                        // is nothing to restore in that case anyway. Two ways to end up empty: the
                        // read failed (ImageUtils.CopyCoreMetadata guards on the same flag), or it
                        // succeeded and the source simply had none, which is the normal case for a
                        // freshly generated AI result. Both of Devin's reviews of #8188 on this line.
                        if (
                            processedName != null
                            && imageInfo.Metadata != null
                            && imageInfo.Metadata.ExceptionCaughtWhileLoading == null
                            && !imageInfo.Metadata.IsEmpty
                        )
                        {
                            // Put the credits back on whatever was just written, exactly as
                            // PageEditingModel.ChangePicture does after the same call — and for the
                            // same reason. Processing can rewrite the bytes (a GraphicsMagick
                            // resize, or a save through GDI+ for a non-web format), and libpalaso
                            // keeps the licence only in the XMP packet, which a GraphicsMagick
                            // rewrite drops. Creator and copyright happen to survive, because they
                            // are also written as PNG tEXt keys, so the symptom is narrow and easy
                            // to miss: an uploaded photo keeps its copyright line but arrives with
                            // no licence at all (BL-16645).
                            try
                            {
                                ImageUtils.SaveImageMetadata(
                                    imageInfo,
                                    Path.Combine(bookFolderPath, processedName)
                                );
                            }
                            catch (Exception metadataEx)
                            {
                                // Deliberately not rethrown: the outer catch would fall back to
                                // copying the source in verbatim, throwing away a perfectly good
                                // processed image over a metadata problem. Unprocessed bulk is a
                                // worse outcome than metadata we failed to re-attach.
                                Logger.WriteError(
                                    $"AiImageEditorApi: imported {sourceBytesPath} but could not re-attach its metadata",
                                    metadataEx
                                );
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Corrupt bytes, or an image too big for us to process at all. Falling
                    // through to the plain copy keeps the user's image, just unprocessed.
                    Logger.WriteError(
                        $"AiImageEditorApi: could not import-process {sourceBytesPath}; copying it unchanged",
                        ex
                    );
                }
            }

            var producedPath =
                processedName == null ? null : Path.Combine(bookFolderPath, processedName);
            var extension = Path.GetExtension(producedPath ?? sourceBytesPath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";
            var newFileName = ImageUtils.GetUnusedFilename(bookFolderPath, "ai-image", extension);
            var newPath = Path.Combine(bookFolderPath, newFileName);
            // producedPath may not exist if processing bailed out early; fall back to the copy.
            if (producedPath != null && RobustFile.Exists(producedPath))
                RobustFile.Move(producedPath, newPath, true); // true: overwrite
            else
                RobustFile.Copy(sourceBytesPath, newPath, true);
            return newFileName;
        }

        // How much bigger than the JPEG it supersedes a new PNG has to be before we spend a
        // GraphicsMagick run (and some lossy re-encoding) trying to shrink it. A PNG merely the
        // same size as the JPEG it replaces isn't the blow-up BL-16645 is about.
        private const double PngBloatRatioWorthReencoding = 1.5;

        /// <summary>
        /// The AI image editor can return a PNG for a slot the book held as a JPEG, and a
        /// photographic PNG can be several times the size of the JPEG it replaced — the very
        /// bloat <see cref="ImportImageIntoBookFolder"/> exists to avoid (BL-16645). So when the
        /// file we just imported is a PNG substantially bigger than the JPEG it supersedes,
        /// re-encode it as a JPEG and keep that instead, deleting the PNG. We only keep the JPEG
        /// if the saving is real: <see cref="ImageUtils.TryChangeFormatToJpegIfHelpful"/> insists
        /// on at least 50% smaller and cleans up after itself otherwise.
        ///
        /// A PNG we can see is transparent is left alone whatever its size, because a JPEG cannot
        /// carry an alpha channel and converting one would permanently flatten its see-through
        /// areas onto a solid background.  Note that checking the image for transparency does
        /// an exhaustive scan of its pixels, which can take about 10 msec for the largest images
        /// we store that are fully opaque (on a fast machine).  Any pixel that is less than fully
        /// opaque makes the image "transparent" for our purposes, so we don't have to check every
        /// pixel if we find one that is not fully opaque.  Checking for transparency is done only
        /// when needed, and is still much faster than re-encoding a large PNG as a JPEG.
        ///
        /// Returns the name (no path) to use for the new file — the JPEG's if we converted,
        /// otherwise <paramref name="newFileName"/> unchanged. Internal for testing.
        /// </summary>
        /// <remarks>
        /// Only called for a freshly generated or uploaded result; a REUSED book image is left alone.
        /// The problem this method fixes is about a generated result bloating a book, and a reused
        /// image is already in the book folder in the appropriate format.
        /// </remarks>
        internal static string ConvertPngToJpegIfItBloatsTheJpegItReplaces(
            string bookFolderPath,
            string oldSrc,
            string newFileName
        )
        {
            if (string.IsNullOrEmpty(oldSrc))
                return newFileName; // nothing was there to compare against
            var oldPath = Path.Combine(bookFolderPath, oldSrc);
            var newPath = Path.Combine(bookFolderPath, newFileName);
            // Judge by the bytes, not the extensions: a book folder can easily hold a file whose
            // extension misdescribes its content (which is why these helpers sniff the header).
            if (!ImageUtils.IsJpegFile(oldPath) || !ImageUtils.IsPngFile(newPath))
                return newFileName;
            if (
                new FileInfo(newPath).Length
                <= PngBloatRatioWorthReencoding * new FileInfo(oldPath).Length
            )
                return newFileName;

            // ImportImageIntoBookFolder only reserved the ".png" name, and GetUnusedFilename
            // checks just that one name — so the same base name with a ".jpg" extension may
            // well be another slot's live image. Reserve a genuinely unused .jpg name instead
            // of writing over it; TryChangeFormatToJpegIfHelpful also warns that a
            // pre-existing destination file can make GraphicsMagick fail outright.
            var jpegFileName = ImageUtils.GetUnusedFilename(bookFolderPath, "ai-image", ".jpg");
            var jpegPath = Path.Combine(bookFolderPath, jpegFileName);
            bool keepTheJpeg;
            try
            {
                // Dispose before touching the PNG again: PalasoImage owns a decoded copy of it.
                using (var image = PalasoImage.FromFileRobustly(newPath))
                {
                    // A JPEG has no alpha channel, so re-encoding a PNG that has any
                    // see-through areas would flatten them onto a solid background — and we
                    // delete the PNG below, so the transparency would be gone for good. That is
                    // why we scan every pixel here, while the same conversion in
                    // ImageUtils.AdjustImageForDisplay settles for a sample: there the original
                    // survives, so a missed patch costs a display copy rather than the picture.
                    if (ImageUtils.HasTransparency(image.Image, samplePixels: false))
                        return newFileName;
                    keepTheJpeg = ImageUtils.TryChangeFormatToJpegIfHelpful(image, jpegPath);
                }
            }
            catch (Exception ex)
            {
                // Anything we can't decode simply doesn't get optimized. Note that
                // ImportImageIntoBookFolder may have copied these bytes in verbatim without
                // ever decoding them (that is its fallback for a file it can't process), so an
                // undecodable file really can reach us — and letting that abort the whole
                // commit, losing every replacement in it, would be a poor trade for a missed
                // size saving.
                Logger.WriteError(
                    $"AiImageEditorApi: could not consider re-encoding {newPath} as a JPEG",
                    ex
                );
                // GraphicsMagick may already have written the JPEG before we got here — the throw
                // can come from disposing the PalasoImage, after the conversion itself succeeded —
                // and we are about to walk away from it, so clean it up like the declined case
                // below rather than leaving it unreferenced in the book folder.
                try
                {
                    if (RobustFile.Exists(jpegPath))
                        RobustFile.Delete(jpegPath);
                }
                catch (Exception cleanupEx)
                {
                    Logger.WriteError(
                        $"AiImageEditorApi: could not clean up the abandoned {jpegPath}",
                        cleanupEx
                    );
                }
                return newFileName;
            }
            if (!keepTheJpeg)
            {
                // TryChangeFormatToJpegIfHelpful removes a JPEG it merely decided against, but
                // not one left behind by a GraphicsMagick run that failed partway. Unlike its
                // other callers, our destination is the book folder itself, so a leftover would
                // sit unreferenced in the very place this method exists to keep small.
                // Guarded for the same reason as the delete below, and even more plainly: this is
                // the branch where we gained nothing at all, so a locked leftover file is the last
                // thing that should be allowed to throw away the user's whole commit.
                try
                {
                    if (RobustFile.Exists(jpegPath))
                        RobustFile.Delete(jpegPath);
                }
                catch (Exception ex)
                {
                    Logger.WriteError(
                        $"AiImageEditorApi: could not clean up the unused {jpegPath}",
                        ex
                    );
                }
                return newFileName;
            }
            // Carry the credits across before the PNG goes away. GraphicsMagick does not preserve
            // them when it rewrites a PNG as a JPEG — measured, not assumed: creator, copyright and
            // licence all come back empty. That matters because an UPLOADED result can arrive with
            // the user's own credits embedded in it, and nothing downstream would put them back:
            // EmbedCreditsInNewImageFile writes only the credits the AI Image Editor explicitly sent, and
            // only when it sent any. So without this, uploading a credited photo and having it
            // re-encoded would quietly strip its copyright (BL-16645, and John's review of #8188).
            ImageUtils.CopyCoreMetadata(newPath, jpegPath);
            // Nothing references the PNG now, and leaving it in the book folder would keep
            // exactly the bulk we just converted away from. But a momentarily locked file (a
            // virus scanner, the host still serving it) must not abort the commit: we already
            // have the JPEG, so a PNG we failed to delete is strictly better than discarding
            // every replacement in the request.
            try
            {
                RobustFile.Delete(newPath);
            }
            catch (Exception ex)
            {
                Logger.WriteError(
                    $"AiImageEditorApi: converted {newPath} to a JPEG but could not delete it",
                    ex
                );
            }
            return jpegFileName;
        }

        /// <summary>
        /// Resolves a host-served image URL (as handed to the AI image editor by
        /// EnumerateBookImages) back to a file path, requiring that it lands on an existing
        /// image file inside the given book folder. Guards against path traversal so the AI
        /// image editor can't have us read or copy arbitrary files. Internal for testing.
        /// </summary>
        internal static bool TryResolveServedUrlToBookFile(
            string bookFolderPath,
            string servedUrl,
            out string fsPath
        )
        {
            fsPath = null;
            try
            {
                // FromLocalhost strips the "<server>/bloom/" prefix and un-escapes; our book
                // image URLs are "<bookFolder>/<relativePath>", so this yields a full path.
                var candidate = servedUrl.FromLocalhost().Replace('/', Path.DirectorySeparatorChar);
                var fullCandidate = Path.GetFullPath(candidate);
                var bookFolder = Path.GetFullPath(bookFolderPath);
                if (
                    !fullCandidate.StartsWith(
                        bookFolder + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return false;
                if (!RobustFile.Exists(fullCandidate))
                    return false;
                if (
                    !AllowedImageExtensions.Contains(
                        Path.GetExtension(fullCandidate).ToLowerInvariant()
                    )
                )
                    return false;
                fsPath = fullCandidate;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Deletes the now-superseded image files we generated ("ai-image*") that no image
        /// element anywhere in <paramref name="dom"/> still references. Bloom's built-in
        /// garbage collector (BookStorage.CleanupUnusedImageFiles, run when the book is next
        /// brought up to date) would eventually remove unreferenced images anyway, so these
        /// don't accumulate forever; but it only handles .jpg/.png/.svg/.gif. This prunes our
        /// files promptly at commit time and also covers the other formats we can generate
        /// (.jpeg/.webp), so a re-edited slot's old ai-image file doesn't linger in the book
        /// folder between edits. Only our own generated files are considered, and only when
        /// nothing references them, so a file shared by another slot (or a user's original
        /// image) is never removed. Internal for testing; callers pass the book's folder path
        /// and its (already-edited, already-saved) DOM.
        /// </summary>
        internal static void DeleteSupersededAiImageFiles(
            string bookFolderPath,
            HtmlDom dom,
            IEnumerable<string> candidateFileNames
        )
        {
            var candidates = candidateFileNames
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(Path.GetFileName)
                .Where(name => name.StartsWith("ai-image", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0)
                return;

            var stillReferenced = CollectReferencedImageFileNames(dom);

            foreach (var fileName in candidates)
            {
                if (stillReferenced.Contains(fileName))
                    continue;
                var fullPath = Path.Combine(bookFolderPath, fileName);
                if (RobustFile.Exists(fullPath))
                    RobustFile.Delete(fullPath);
            }
        }

        /// <summary>
        /// The set of image file names (no path) referenced by any &lt;img&gt; or
        /// background-image element anywhere in the DOM, including the data-div. Used to
        /// decide whether a superseded file is safe to delete. Internal for testing.
        /// </summary>
        internal static HashSet<string> CollectReferencedImageFileNames(HtmlDom dom)
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var holders = HtmlDom.SelectChildImgAndBackgroundImageElements(
                dom.RawDom.DocumentElement
            );
            foreach (var holder in holders.OfType<SafeXmlElement>())
            {
                var url = HtmlDom.GetImageElementUrl(holder).PathOnly.NotEncoded;
                if (!string.IsNullOrEmpty(url))
                    referenced.Add(Path.GetFileName(url));
            }
            return referenced;
        }

        /// <summary>
        /// Reads one book image's intellectual-property metadata (creator, copyright, license,
        /// collection info) into the small <see cref="ImageCredits"/> shape shared with the AI
        /// image editor, or null when the file is missing or has no usable metadata. Used at
        /// launch to hand each book image its current credits, so a result the AI image editor
        /// derives from it can carry (and amend) them. Internal for testing.
        /// </summary>
        internal static ImageCredits GetCreditsForImageFile(
            string bookFolderPath,
            string relativePath
        )
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;
            // A book's image src can still be percent-encoded, so decode our way to the real
            // file the way the rest of Bloom does (BL-3901) instead of a plain Path.Combine.
            // Getting this wrong doesn't just miss a file: we'd report credits:null, the AI
            // image editor would believe the image has none, and a result derived from it would
            // legitimately get none — losing exactly the credits this code exists to carry.
            var path = UrlPathString.GetFullyDecodedPath(bookFolderPath, ref relativePath);
            if (!RobustFile.Exists(path))
                return null;

            Metadata meta;
            try
            {
                meta = RobustFileIO.MetadataFromFile(path);
            }
            catch (Exception)
            {
                // A corrupt/unreadable image must not fail the whole launch; it simply travels
                // to the AI image editor without credits.
                return null;
            }
            if (meta == null || meta.IsEmpty || meta.ExceptionCaughtWhileLoading != null)
                return null;

            return new ImageCredits
            {
                copyrightNotice = meta.CopyrightNotice,
                creator = meta.Creator,
                // Both halves of the license travel, because a CC license can have license
                // notes as well as a URL and sending only the URL loses them (BL-16603).
                licenseUrl = meta.License?.Url,
                licenseRightsStatement = meta.License?.RightsStatement,
                attributionUrl = meta.AttributionUrl,
                collectionName = meta.CollectionName,
                collectionUri = meta.CollectionUri,
            };
        }

        /// <summary>
        /// Reads an image file's embedded metadata into the three attribute values Bloom mirrors
        /// on the element (data-copyright/data-creator/data-license). Mirrors what
        /// ImageUpdater.UpdateImgMetadataAttributesToMatchImage would write, so a current-page
        /// element the front-end updates ends up saying exactly what the equivalent off-page
        /// element does — and what the next book-up-to-date pass would say. A missing or
        /// unreadable file yields empty strings; the updater removes the attributes outright in
        /// that case, which comes to the same thing for every reader (they all treat an absent
        /// attribute and an empty one alike). We can't actually get there anyway — the caller
        /// has just copied the file into place. Internal for testing.
        /// </summary>
        internal static ImageCreditAttributes ReadCreditAttributes(
            string bookFolderPath,
            string fileName
        )
        {
            var none = new ImageCreditAttributes
            {
                copyright = "",
                creator = "",
                license = "",
            };
            var path = Path.Combine(bookFolderPath, fileName);
            if (!RobustFile.Exists(path))
                return none;
            Metadata meta;
            try
            {
                meta = RobustFileIO.MetadataFromFile(path);
            }
            catch (Exception)
            {
                return none;
            }
            if (meta == null)
                return none;
            return new ImageCreditAttributes
            {
                copyright = meta.CopyrightNotice ?? "",
                creator = meta.Creator ?? "",
                // A NullLicense stringifies to "", so an image with no license gets "".
                license = meta.License?.ToString() ?? "",
            };
        }

        /// <summary>
        /// Embeds in a freshly written replacement file the <paramref name="credits"/>
        /// (intellectual-property metadata) the AI image editor chose for it, so the
        /// illustration's credits survive.
        ///
        /// The AI image editor is the only authority here: it knows what the result was derived
        /// from and whether the user amended the credits or made something entirely new. So
        /// when it sends no credits — null, or an object whose every field is empty — that is
        /// its answer, not a gap for Bloom to fill: we write nothing and return. In particular
        /// we must never reach for the replaced image's credits ourselves; a new image made
        /// while editing an old one is not the old illustration. Returning without touching the
        /// file also leaves a user-uploaded result's own embedded metadata alone, which is the
        /// only credit information anyone has for it.
        ///
        /// The credits have to be written into the FILE, not just the DOM's data-copyright/
        /// data-creator/data-license attributes: Bloom rebuilds those attributes FROM the
        /// file's embedded metadata (ImageUpdater.UpdateImgMetadataAttributesToMatchImage,
        /// run on book load and whenever the data div is synced). An AI-generated result has
        /// no metadata of its own, so without this the attributes would be silently cleared
        /// the next time Bloom syncs — the "lost credits" bug.
        ///
        /// Internal for testing.
        /// </summary>
        internal static void EmbedCreditsInNewImageFile(
            string bookFolderPath,
            string newFileName,
            ImageCredits credits
        )
        {
            if (credits == null || CreditsAreEmpty(credits))
                return;
            var source = MetadataFromCredits(credits);

            var newPath = Path.Combine(bookFolderPath, newFileName);
            try
            {
                // Clear whatever the generator left in the file first: stray tags can trip up
                // the TagLib library when we write (BL-16058). Then write just the IP fields.
                using (var tagFile = RobustFileIO.CreateTaglibFile(newPath))
                {
                    tagFile.RemoveTags(TagLib.TagTypes.AllTags);
                    RobustFileIO.SaveTaglibFile(tagFile);
                }
                source.WriteIntellectualPropertyOnly(newPath);
            }
            catch (Exception e)
            {
                Logger.WriteError(
                    $"AiImageEditorApi: could not copy image credits to {newPath}",
                    e
                );
            }
        }

        /// <summary>
        /// Builds an IP-only <see cref="Metadata"/> from the credits the AI image editor sent,
        /// reconstructing the license from its URL and/or rights statement.
        /// </summary>
        private static Metadata MetadataFromCredits(ImageCredits credits)
        {
            return new Metadata
            {
                Creator = credits.creator,
                CopyrightNotice = credits.copyrightNotice,
                AttributionUrl = credits.attributionUrl,
                CollectionName = credits.collectionName,
                CollectionUri = credits.collectionUri,
                License = BuildLicense(credits.licenseUrl, credits.licenseRightsStatement),
            };
        }

        /// <summary>
        /// Reconstructs a ClearShare license from the wire representation (a CC license URL
        /// and/or a free-text rights statement). This deliberately mirrors ClearShare's own
        /// LicenseUtils.FromXmp, which is how the license comes back OUT of an image file: a
        /// creativecommons.org URL becomes a CreativeCommonsLicense; failing that, a rights
        /// statement becomes a CustomLicense; nothing at all becomes a NullLicense. Matching
        /// FromXmp is what makes the round trip faithful — anything we can express here that it
        /// can't express is a license the file could not store and Bloom would not read back.
        ///
        /// The rights statement rides along on a CC license rather than replacing it, because
        /// ClearShare keeps both (a CC license plus the extra "license notes" restrictions the
        /// user added) and dropping the notes was BL-16603.
        ///
        /// Always returns non-null.
        /// </summary>
        private static LicenseInfo BuildLicense(string licenseUrl, string rightsStatement)
        {
            // Only hand actual creativecommons.org URLs to the CC parser: FromLicenseUrl
            // misparses unrelated URLs instead of throwing (see ImageGalleryApi).
            if (!string.IsNullOrEmpty(licenseUrl) && licenseUrl.Contains("creativecommons.org"))
            {
                try
                {
                    var ccLicense = CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
                    ccLicense.RightsStatement = rightsStatement;
                    return ccLicense;
                }
                catch
                {
                    // Malformed CC URL — fall through, so any rights statement still survives.
                }
            }
            if (!string.IsNullOrEmpty(rightsStatement))
                return new CustomLicense { RightsStatement = rightsStatement };
            return new NullLicense();
        }

        /// <summary>True when every field of the supplied credits is null/empty.</summary>
        private static bool CreditsAreEmpty(ImageCredits c) =>
            string.IsNullOrEmpty(c.creator)
            && string.IsNullOrEmpty(c.copyrightNotice)
            && string.IsNullOrEmpty(c.licenseUrl)
            && string.IsNullOrEmpty(c.licenseRightsStatement)
            && string.IsNullOrEmpty(c.attributionUrl)
            && string.IsNullOrEmpty(c.collectionName)
            && string.IsNullOrEmpty(c.collectionUri);
    }
}
