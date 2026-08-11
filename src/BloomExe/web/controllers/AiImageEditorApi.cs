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
    /// WHERE THE AI IMAGE EDITOR COMES FROM  (see GetAiImageEditorUrl)
    ///   DEFAULT: {ServerUrl}/bloom/aiImageEditor/index.html — its built app
    ///            ("dist-app"), served same-origin by BloomServer so there's no CORS.
    ///            The build copies dist-app/ from the installed `bloom-ai-image-tools`
    ///            package into output/browser/aiImageEditor/ (a viteStaticCopy target,
    ///            mirroring the bloom-player copy); `./go.sh` stages the same at dev time
    ///            (scripts/aiEditorBuild.mjs), falling back to building a local checkout
    ///            until the package is published and added as a dependency.
    ///   LINKED : set BLOOM_AI_EDITOR_URL to the AI image editor's own Vite dev server (HMR).
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
    ///      (aiEditorOverlay.ts, in the TOP window) and the AI image editor's iframe: ready /
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
    ///   changeImageByElement() (aiEditorPageCommands.ts).
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
        // front-end's editable-format list (BloomBrowserUI/.../aiEditorImageFormats.ts).
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
            // The go.mjs launcher (scripts/aiEditorBuild.mjs) builds it from the local
            // bloom-ai-image-tools checkout and stages it there, so `./go.sh` "just works"
            // with no separate dev server, in both Debug and Release.
            //
            // Someone working on the AI image editor itself, who wants hot-module reload, can
            // instead point Bloom at its own Vite dev server by setting BLOOM_AI_EDITOR_URL,
            // e.g. BLOOM_AI_EDITOR_URL=http://localhost:3000/ and running `pnpm dev` in the
            // bloom-ai-image-tools checkout.
            var overrideUrl = Environment.GetEnvironmentVariable("BLOOM_AI_EDITOR_URL");
            if (!string.IsNullOrWhiteSpace(overrideUrl))
                return overrideUrl;
            return $"{BloomServer.ServerUrl}/bloom/aiImageEditor/index.html";
        }

        /// <summary>The image the user right-clicked, as the page frame sends it to
        /// <see cref="HandleSaveThenLaunch"/> and as we hand it back to the browser once the page
        /// has been saved. See IAiImageEditorTarget in aiEditorShared.ts. The page frame sends only
        /// the file name; we fill in the page id, since we are the ones who know which page we
        /// saved, and the overlay (in the top window) has no page DOM of its own to read it from.
        /// </summary>
        private class SaveThenLaunchRequest
        {
            public string imageFileName { get; set; }
            public string pageId { get; set; }
        }

        /// <summary>
        /// Saves the page the user is editing, then opens the AI image editor on it.
        ///
        /// The save is the point of this endpoint. Everything we tell the AI image editor about
        /// the book — the image list from <see cref="EnumerateBookImages"/>, and on commit each
        /// slot's current src — is read from the SAVED book DOM, but an image the user has just
        /// added exists only in the live page (Bloom deliberately doesn't save on an image change;
        /// see EditingModel.UpdateImageInBrowser and BL-16330). Launching against the unsaved DOM
        /// opened the editor with an empty "Image to Edit" slot (BL-16682); it would also have
        /// made the current page's commit results describe an image the live page no longer shows,
        /// and left <see cref="DeleteSupersededAiImageFiles"/> blind to a file the live page uses.
        ///
        /// Two separate things follow from the fact that saving always ends in a navigation.
        ///
        /// WHERE the overlay lives: not in the page iframe, which that navigation replaces every
        /// time — hence the top window, like the image-gallery and copyright/license commands (see
        /// aiEditorOverlay.ts and the comments on those commands in canvasControlRegistry.ts).
        ///
        /// WHEN we open it: only once the browser has a page again, via
        /// EditingModel.RunAfterNextPageLoad. It is tempting to use SaveThen's doAfterSaveToDisk
        /// instead, since the top window is alive at that moment — but that moment is immediately
        /// before the navigation, and the navigation is not always confined to the page iframe.
        /// EditingView.StartNavigationToEditPage reloads the whole workspace root when
        /// MemoryUtils.SystemIsShortOfMemory(), which is Bloom's own private bytes past ~2GB —
        /// the ordinary state of a long editing session on a big book. Opening from
        /// doAfterSaveToDisk there meant the page saved correctly and the editor never appeared,
        /// with no message: openAiImageEditor doesn't even build the overlay synchronously; it
        /// POSTs launch first and builds it in the reply, a whole round trip after the reload
        /// began. Waiting for the page load costs nothing and is immune to all three routes.
        ///
        /// To see that failure for yourself, temporarily make ShouldDoFullReload() return true —
        /// its own comment invites exactly this — rather than trying to grow Bloom past 2GB.
        /// </summary>
        private void HandleSaveThenLaunch(ApiRequest request)
        {
            // Must be read before SaveThen: by the time our callbacks run the request is complete.
            SaveThenLaunchRequest payload;
            try
            {
                payload = request.RequiredPostObject<SaveThenLaunchRequest>();
            }
            catch (Exception)
            {
                request.Failed(HttpStatusCode.BadRequest, "Invalid saveThenLaunch payload");
                return;
            }

            var model = View?.Model;
            var pageId = model?.CurrentPage?.Id;
            if (model == null || string.IsNullOrEmpty(pageId))
            {
                request.Failed("No page is open for editing");
                return;
            }
            payload.pageId = pageId;

            // Queue BEFORE the save: with no page loaded, SaveThen completes synchronously.
            model.RunAfterNextPageLoad(loadedPageId =>
            {
                // A different page means the user navigated, or the save failed and we went
                // elsewhere; the image we were asked to edit isn't there to edit.
                if (loadedPageId == pageId)
                    OpenEditorInBrowser(payload);
            });
            model.SaveThen(
                () => pageId,
                doIfNotInRightStateToSave: () => {
                    // Leave the queued launch alone. Every state that refuses a save — one already
                    // in flight, mid-navigation, saved-and-stripped — is on its way to a page load,
                    // and that load will open the editor against a book DOM the other save has just
                    // brought up to date, which is all we wanted.
                }
            );
            // No failureAction is needed, and adding one would open the editor twice: every way a
            // save can fail still ends in EditingStateMachine navigating back to the page, so the
            // queued launch fires anyway. That the hook covers the failure paths for free is one of
            // the reasons for preferring it over doAfterSaveToDisk.

            request.PostSucceeded();
        }

        /// <summary>
        /// Tells the browser to open the AI image editor overlay on <paramref name="target"/>. The
        /// browser owns the overlay (only it can postMessage to the editor's iframe), so all this
        /// side does is call its entry point; see openAiImageEditor in aiEditorOverlay.ts.
        /// Fire-and-forget, like EditingModel.UpdateImageInBrowser's call to changeImage: there is
        /// nothing here to wait for.
        /// </summary>
        private void OpenEditorInBrowser(SaveThenLaunchRequest target)
        {
            var arg = JsonConvert.SerializeObject(target);
            View?.Browser?.RunJavascriptFireAndForget($"workspaceBundle.openAiImageEditor({arg})");
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
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BLOOM_AI_EDITOR_URL"))
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
                    // "Local Dummy (No AI)" model, for cost-free testing) on developer AND
                    // alpha/unstable builds, so human alpha testers can exercise it without
                    // spending real AI credits. The AI image editor hides those tools unless
                    // the host opts in, so release/beta builds (IsDevOrAlpha false) never
                    // expose them to end users.
                    showDeveloperTools = ApplicationUpdateSupport.IsDevOrAlpha,
                }
            );
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
                    "The selected book changed since the editor was launched"
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
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    // Stream the body straight to disk: history images are multi-MB, so don't
                    // buffer them whole in memory (RawPostData would). Land the bytes in a
                    // temp file and swap it in, so an upload that dies partway can't leave a
                    // truncated file where a previous good one was. An empty body is a valid
                    // save of an empty file, not a no-op: the AI image editor must never be
                    // told "saved" while stale content survives on disk.
                    var tempPath = fullPath + ".tmp";
                    using (var input = request.RawPostStream)
                    using (var output = RobustFile.Create(tempPath))
                    {
                        // RawPostStream is null for an empty body (no entity body); that
                        // still means "save an empty file" here, so just leave the freshly
                        // created temp file empty rather than copying.
                        input?.CopyTo(output);
                    }
                    RobustFile.Move(tempPath, fullPath, true); // true: overwrite
                    request.PostSucceeded();
                    break;

                case HttpMethods.Delete:
                    if (RobustFile.Exists(fullPath))
                        RobustFile.Delete(fullPath);
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
        /// True if an image-bearing element is one the user is allowed to replace. Branding,
        /// license, and QR-code images are never user-changeable, so they are excluded both
        /// from the list offered to the AI image editor and from being overwritten at commit.
        /// Internal for testing.
        /// </summary>
        internal static bool IsUserChangeableImageElement(SafeXmlElement element) =>
            !element.HasClass("branding")
            && !element.HasClass("licenseImage")
            && !element.HasClass("bloom-qrcode");

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
                var pageLabel = page.GetAttribute("data-page-number");

                var holders = HtmlDom.SelectChildImgAndBackgroundImageElements(page);
                // Ordinal is the index within the full holder list so that commit can
                // re-find the element deterministically; the branding/license skip below
                // only affects which slots we offer, not the indexing.
                for (var ordinal = 0; ordinal < holders.Length; ordinal++)
                {
                    if (!(holders[ordinal] is SafeXmlElement element))
                        continue;
                    if (!IsUserChangeableImageElement(element))
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

                    images.Add(
                        new
                        {
                            id = pageId + ":" + ordinal,
                            src = (folderAsUrlPrefix + "/" + relativePath).ToLocalhost(),
                            pageLabel = string.IsNullOrEmpty(pageLabel) ? null : pageLabel,
                            // The AI image editor shows its own placeholder graphic for empty
                            // slots rather than trying to load the (book-less)
                            // placeHolder.png.
                            isPlaceholder = ImageUtils.IsPlaceholderImageFilename(relativePath),
                            // The image's current credits, so a result derived from it can
                            // carry (or amend) them. The AI image editor owns the credit
                            // *decision* and hands back whatever it chose on commit; Bloom
                            // only embeds that into the file. Null when the image has no
                            // usable metadata.
                            credits = GetCreditsForImageFile(book.FolderPath, relativePath),
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

            var holders = HtmlDom.SelectChildImgAndBackgroundImageElements(page);
            if (ordinal < 0 || ordinal >= holders.Length)
            {
                error = "Image index out of range";
                return false;
            }
            if (!(holders[ordinal] is SafeXmlElement element))
            {
                error = "Image element not found";
                return false;
            }
            if (!IsUserChangeableImageElement(element))
            {
                error = "Image is not user-changeable";
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
                // Only a freshly generated/uploaded result needs resizing. A REUSED book image
                // was already import-processed on its own way in, so there is nothing to gain
                // by shrinking it again — and resizing rewrites the file through
                // GraphicsMagick, which can drop the credits embedded in it. This path
                // deliberately doesn't re-write credits (see EmbedCreditsInNewImageFile below),
                // so there would be nothing to put them back.
                resizeIfNeeded: !string.IsNullOrEmpty(replacement.resultId)
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
        /// False for a reused book image, which was already import-processed on its own way in.
        /// Resizing it again would gain nothing and would rewrite the file through
        /// GraphicsMagick, which can drop the credits embedded in it — and the reuse path
        /// deliberately doesn't re-write credits, so they would simply be lost.
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
