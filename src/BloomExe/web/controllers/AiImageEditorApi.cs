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
using SIL.IO;

namespace Bloom.web.controllers
{
    /// <summary>
    /// AI Image Editor integration — host (Bloom) side.
    ///
    /// WHAT THIS IS
    ///   "Edit with AI…" (the image context menu) opens a separate web app — the
    ///   `bloom-ai-image-tools` editor — as an IFRAME OVERLAY inside Bloom's existing
    ///   edit-tab WebView2. It is NOT a separate window, and Bloom does NOT import the
    ///   editor's code: the editor is a self-contained web app loaded by URL. There is
    ///   no npm/bundler dependency between the two projects.
    ///
    /// WHERE THE EDITOR COMES FROM  (see GetEditorUrl)
    ///   DEFAULT: {ServerUrl}/bloom/aiImageEditor/index.html — the editor's built app
    ///            ("dist-app"), served same-origin by BloomServer so there's no CORS.
    ///            The build copies dist-app/ from the installed `bloom-ai-image-tools`
    ///            package into output/browser/aiImageEditor/ (a viteStaticCopy target,
    ///            mirroring the bloom-player copy); `./go.sh` stages the same at dev time
    ///            (scripts/aiEditorBuild.mjs), falling back to building a local checkout
    ///            until the package is published and added as a dependency.
    ///   LINKED : set BLOOM_AI_EDITOR_URL to the editor's own Vite dev server for HMR.
    ///            `./go.sh --with bloom-ai-image-tools` does this automatically (it starts
    ///            the dev server and points Bloom at it); GetEditorUrl honors the env var.
    ///
    /// TWO COMMUNICATION PLANES
    ///   1. HTTP, editor/front-end JS -> this controller, over Bloom's own local server:
    ///        aiImageEditor/launch        mint session, make folders, enumerate book
    ///                                    images + history, return the launch payload.
    ///        aiImageEditor/file          GET/POST/DELETE files under .ai-image-editor/.
    ///        aiImageEditor/commit        apply the chosen replacements to the book.
    ///        aiImageEditor/saveCredentials  persist the user's OpenRouter API key.
    ///   2. window.postMessage on channel "bloom-ai-image-tools", between the overlay JS
    ///      (CanvasElementContextControls.tsx) and the editor iframe: ready / init /
    ///      commit / cancel / log / ack. The overlay JS — NOT this class —
    ///      sends `init` (built from the launch reply) and tears the overlay down. Image
    ///      BYTES never cross postMessage; they move only as files via aiImageEditor/file.
    ///
    /// DATA ON DISK
    ///   Per-book folder `<book>/.ai-image-editor/` with `history/<id>.png` images and
    ///   `history/<id>.json` sidecars. The history folder is the source of truth.
    ///
    /// SECURITY
    ///   A per-launch session token (query param) gates /file, /commit, /saveCredentials.
    ///   File names are allow-listed; page/result ids are charset-restricted; reused
    ///   source URLs must resolve inside the book folder (no path traversal).
    ///
    /// COMMIT SPLIT
    ///   Off-page images are edited directly in the whole-book DOM here and saved. The
    ///   currently-open page is owned by the live browser, so those replacements are
    ///   returned as {oldSrc,newSrc} for the overlay JS to apply via Bloom's changeImage().
    ///
    /// EDITOR REPO: bloom-ai-image-tools — App.tsx (mode=bloom-iframe),
    ///   services/host/BloomHostBridge.ts (createIframeBloomHostBridge),
    ///   components/BloomEmbeddedShell.tsx.
    /// </summary>
    public class AiImageEditorApi
    {
        private readonly BookSelection _bookSelection;

        /// <summary>Set by the EditingView constructor — used at commit time to detect the
        /// currently-open page (which the live browser owns) so we don't edit it from here.</summary>
        public EditingView View { get; set; }

        // Minted at launch; torn down on the next launch (see EndSession). The editor's
        // `cancel`/`commit` are handled in the overlay JS, which removes the overlay.
        private string _sessionToken;

        // The folder of the book the session was launched for. Every session-gated request
        // re-resolves _bookSelection.CurrentSelection, so if the user somehow switches books
        // while an overlay is still up, requests from that overlay must fail rather than
        // read/write the newly selected book's files or commit against its DOM.
        private string _sessionBookFolderPath;

        // The image formats the AI editor can actually work with — the ones it can load as
        // input, and the raster formats it stores/serves/commits as results. Deliberately a
        // short list: formats the editor can't edit (e.g. svg, tif, bmp, gif) are excluded so
        // we never offer, serve, or commit an image the editor can't handle. Single source of
        // truth — AllowedFileName (below), IsImageFileName, the history-result probe, the
        // reused-source check, and the whole-book image list all derive from this set, so the
        // lists can't drift apart. Must stay in sync with the front-end's editable-format list
        // (BloomBrowserUI/.../aiEditorImageFormats.ts).
        private static readonly HashSet<string> AllowedImageExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
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
        private string GetEditorFolderPath()
        {
            var folderPath = _bookSelection.CurrentSelection?.FolderPath;
            return string.IsNullOrEmpty(folderPath)
                ? null
                : Path.Combine(folderPath, ".ai-image-editor");
        }

        private string GetEditorUrl()
        {
            // The editor is served by BloomServer from output/browser/aiImageEditor/. The
            // go.mjs launcher (scripts/aiEditorBuild.mjs) builds it from the local
            // bloom-ai-image-tools checkout and stages it there, so `./go.sh` "just works"
            // with no separate dev server, in both Debug and Release.
            //
            // Editor developers who want hot-module reload can instead point Bloom at the
            // editor's own Vite dev server by setting BLOOM_AI_EDITOR_URL, e.g.
            // BLOOM_AI_EDITOR_URL=http://localhost:3000/ and running `pnpm dev` in the
            // editor checkout.
            var overrideUrl = Environment.GetEnvironmentVariable("BLOOM_AI_EDITOR_URL");
            if (!string.IsNullOrWhiteSpace(overrideUrl))
                return overrideUrl;
            return $"{BloomServer.ServerUrl}/bloom/aiImageEditor/index.html";
        }

        /// <summary>
        /// Starts an editor session for the selected book: mints the session token, ensures
        /// the .ai-image-editor folders exist, and replies with everything the overlay JS
        /// needs to boot the editor iframe (editor URL, book image list, history, credentials).
        /// </summary>
        private void HandleLaunch(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null)
            {
                request.Failed("No book selected");
                return;
            }

            // The editor app is a separate package staged into browser/aiImageEditor at
            // build/dev time (see GetEditorUrl); until it is published and added as a real
            // dependency, a build can end up without it. Fail the launch with a clear
            // message rather than opening an overlay whose iframe would just 404.
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
            var editorFolder = GetEditorFolderPath();
            Directory.CreateDirectory(Path.Combine(editorFolder, "history"));

            var httpBase = $"{BloomServer.ServerUrlWithBloomPrefixEndingInSlash}api/aiImageEditor";

            // Return the data the JS needs to create the iframe overlay. The editor runs
            // in iframe mode and gets its `init` from the overlay JS (which builds it from
            // this reply and posts it to the iframe), so the whole-book image list must
            // travel here rather than over any C#->iframe channel.
            request.ReplyWithJson(
                new
                {
                    editorUrl = GetEditorUrl(),
                    httpBase,
                    sessionToken = _sessionToken,
                    book = new { id = book.BookInfo.Id, title = book.BookInfo.Title },
                    bookImages = EnumerateBookImages(book),
                    // The history folder is the source of truth; enumerate it so images
                    // (and their sidecars) appear even when state.json doesn't list them.
                    history = EnumerateHistoryImages(book),
                    references = Array.Empty<object>(),
                    // Bloom owns the OpenRouter key: supply the per-user stored key so the
                    // editor doesn't have to ask for it again. The editor hands any newly
                    // obtained key back via aiImageEditor/saveCredentials.
                    apiKey = OpenRouterCredentialStore.GetApiKey(),
                    // In a Playground template book all features are unlocked for
                    // "try it out", so the editor opens — but it's a shared demo
                    // context, so the editor must not let the user set/save an
                    // OpenRouter API key. The editor disables its credential UI when
                    // this is true; HandleSaveCredentials also refuses to persist.
                    demoOnly = book.IsPlayground,
                }
            );
        }

        private class SaveCredentialsRequest
        {
            public string apiKey { get; set; }
        }

        /// <summary>
        /// Receives the user's OpenRouter API key from the editor (manual key entry) and
        /// persists it per-user via <see cref="OpenRouterCredentialStore"/>. A null/empty
        /// apiKey clears the stored key (sign-out). Session-gated so a stray frame can't
        /// overwrite the user's stored key.
        /// </summary>
        private void HandleSaveCredentials(ApiRequest request)
        {
            if (!HasValidSession(request))
                return;

            // Defense in depth for the Playground "demo" case (see HandleLaunch): never
            // persist a key obtained during a Playground session, even if a stray frame
            // posts here despite the editor's disabled credential UI.
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
        /// The editor's persistence endpoint: GET/POST/DELETE of individual files under the
        /// book's .ai-image-editor folder. File names are restricted to the AllowedFileName
        /// allow-list, so the editor can only ever touch its own state/history files.
        /// </summary>
        private void HandleFile(ApiRequest request)
        {
            // Answer the CORS preflight before the session gate: the browser sends OPTIONS
            // automatically with none of our application query params (including "session"),
            // so if HasValidSession ran first it would 401 the preflight and block the real
            // cross-origin request. This only matters for the LINKED dev workflow, where the
            // editor's own Vite dev server (localhost:3000) fetches this endpoint from a
            // different origin; the shipped product serves the editor same-origin.
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

            var editorFolder = GetEditorFolderPath();
            if (editorFolder == null)
            {
                request.Failed("No book selected");
                return;
            }

            var relativePath = name.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(editorFolder, relativePath);

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
                    // Stream the body straight to disk: history images are multi-MB, so
                    // don't buffer them whole in memory (RawPostData would). Land the bytes
                    // in a temp file and swap it in, so an upload that dies partway can't
                    // leave a truncated file where a previous good one was. An empty body
                    // is a valid save of an empty file, not a no-op: the editor must never
                    // be told "saved" while stale content survives on disk.
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

        // Book page ids and editor result ids are echoed back to us on commit and
        // interpolated into XPath / file paths, so we restrict them to a safe charset.
        private static readonly Regex SafeId = new Regex(
            @"^[a-zA-Z0-9_\-]+$",
            RegexOptions.Compiled
        );

        private const string EditedWithAiCredit = "Edited with AI";

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
        /// license, and QR-code images are never user-changeable, so they are excluded both from
        /// the list offered to the editor and from being overwritten at commit. Internal for testing.
        /// </summary>
        internal static bool IsUserChangeableImageElement(SafeXmlElement element) =>
            !element.HasClass("branding")
            && !element.HasClass("licenseImage")
            && !element.HasClass("bloom-qrcode");

        /// <summary>
        /// Locates the bytes for a history result by id. The editor may store a result under
        /// any supported raster extension, so we probe history/&lt;resultId&gt;.&lt;ext&gt; across
        /// <see cref="AllowedImageExtensions"/> and return the first match. The caller must
        /// have already validated resultId against <see cref="SafeId"/>.
        /// </summary>
        private static bool TryFindHistoryResultFile(
            string editorFolder,
            string resultId,
            out string path
        )
        {
            var historyFolder = Path.Combine(editorFolder, "history");
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

            /// <summary>The editor result id; its bytes live at history/{resultId}.png.</summary>
            public string resultId { get; set; }

            /// <summary>For a reused existing image: its host-served URL, resolved to a book file.</summary>
            public string sourceUrl { get; set; }
        }

        /// <summary>
        /// Enumerates every image the user is allowed to change across the whole book —
        /// all pages including front cover and xmatter, including empty placeholder slots —
        /// excluding only branding and license images. Each entry is a reference
        /// (id + servable URL); image bytes are fetched lazily by the editor, never inlined.
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

                    // Only offer images the editor can actually edit. A book may legitimately
                    // contain formats the editor can't open (e.g. an svg illustration); those
                    // slots are simply omitted from the list rather than handed over to fail.
                    if (!IsImageFileName(relativePath))
                        continue;

                    images.Add(
                        new
                        {
                            id = pageId + ":" + ordinal,
                            src = (folderAsUrlPrefix + "/" + relativePath).ToLocalhost(),
                            pageLabel = string.IsNullOrEmpty(pageLabel) ? null : pageLabel,
                            // The editor shows its own placeholder graphic for empty slots
                            // rather than trying to load the (book-less) placeHolder.png.
                            isPlaceholder = ImageUtils.IsPlaceholderImageFilename(relativePath),
                        }
                    );
                }
            }

            return images;
        }

        /// <summary>
        /// Enumerates the per-book history folder so the editor can rebuild its history
        /// from the files on disk — the folder is the source of truth. Each image is
        /// returned as a reference (id + servable book-folder URL) plus the parsed
        /// contents of its sibling &lt;id&gt;.json sidecar, when present. Bytes are fetched
        /// lazily by URL, never inlined. Images with no sidecar (e.g. dropped in by hand)
        /// are still returned; the editor recovers them. Empty placeholder files and
        /// non-image files (including the sidecars themselves) are skipped.
        /// </summary>
        private List<object> EnumerateHistoryImages(Bloom.Book.Book book)
        {
            var result = new List<object>();
            var editorFolder = GetEditorFolderPath();
            if (editorFolder == null)
                return result;
            var historyFolder = Path.Combine(editorFolder, "history");
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
        /// Applies the editor's chosen replacements to the book. Each replacement names a
        /// slot (pageId:ordinal) anywhere in the book and an editor result whose bytes we
        /// read from the per-book history folder — so no image bytes cross the bridge.
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

            var editorFolder = GetEditorFolderPath();
            if (editorFolder == null)
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
                    editorFolder,
                    replacement,
                    currentPageId,
                    pageCache,
                    out var error,
                    out var isCurrentPage,
                    out var oldSrc,
                    out var newSrc,
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
            // slot — or by the current page (which the front-end has yet to repoint) — survives.
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
        /// copying the new image bytes (from history or a reused book file) into the book
        /// folder under a fresh name. An off-page slot is edited here in the storage DOM
        /// (caller saves); a current-page slot is left untouched — we return oldSrc/newSrc
        /// (via the out params) for the front-end to apply to the live DOM. Returns false
        /// with <paramref name="error"/> set when the replacement can't be applied.
        /// </summary>
        private bool TryApplyReplacement(
            Bloom.Book.Book book,
            string editorFolder,
            CommitReplacement replacement,
            string currentPageId,
            Dictionary<string, SafeXmlElement> pageCache,
            out string error,
            out bool isCurrentPage,
            out string oldSrc,
            out string newSrc,
            out SafeXmlElement pageForDataDivSync
        )
        {
            error = null;
            isCurrentPage = false;
            oldSrc = null;
            newSrc = null;
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
                // The editor may save a result in any supported raster format, so look it up
                // by id across the allowed extensions rather than assuming .png (which would
                // fail to commit a .jpg/.webp result that EnumerateHistoryImages happily lists).
                if (
                    !TryFindHistoryResultFile(
                        editorFolder,
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

            // Write the new image under a fresh name (preserving the source extension) so
            // we never clobber a file shared by another slot or serve stale cached bytes.
            // GetUnusedFilename guarantees fresh while keeping the name human-readable
            // (filenames surface in image metadata and problem reports).
            var extension = Path.GetExtension(sourceBytesPath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";
            var newFileName = ImageUtils.GetUnusedFilename(book.FolderPath, "ai-image", extension);
            RobustFile.Copy(sourceBytesPath, Path.Combine(book.FolderPath, newFileName), true);
            newSrc = newFileName;

            if (isCurrentPage)
            {
                // Leave the live (current) page to the front-end: it will call Bloom's
                // changeImage() with newSrc so the canvas + normal save flow handle it.
                return true;
            }

            HtmlDom.SetImageElementUrl(
                element,
                UrlPathString.CreateFromUnencodedString(newFileName)
            );

            // Keep existing copyright/license; mark the illustrator as AI-edited (once).
            AppendEditedWithAiCredit(element);

            if (element.HasAttribute("data-book"))
                pageForDataDivSync = page;

            return true;
        }

        /// <summary>
        /// Resolves a host-served image URL (as handed to the editor by EnumerateBookImages)
        /// back to a file path, requiring that it lands on an existing image file inside the
        /// given book folder. Guards against path traversal so the editor can't have us read or
        /// copy arbitrary files. Internal for testing.
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
        /// element anywhere in <paramref name="dom"/> still references. Without this, editing
        /// an already-AI-edited slot again would leave the previous ai-image file orphaned in
        /// the book folder, and they would accumulate. Only our own generated files are
        /// considered, and only when nothing references them, so a file shared by another
        /// slot (or a user's original image) is never removed. Internal for testing; callers
        /// pass the book's folder path and its (already-edited, already-saved) DOM.
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
        /// Adds the "Edited with AI" illustrator credit to the element's data-creator, once:
        /// an empty creator becomes just the credit; an existing creator gets ", Edited with AI"
        /// appended; and a creator that already mentions it (any case) is left unchanged.
        /// Internal for testing.
        /// </summary>
        internal static void AppendEditedWithAiCredit(SafeXmlElement element)
        {
            var creator = element.GetAttribute("data-creator") ?? "";
            if (creator.IndexOf(EditedWithAiCredit, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            element.SetAttribute(
                "data-creator",
                string.IsNullOrWhiteSpace(creator)
                    ? EditedWithAiCredit
                    : creator + ", " + EditedWithAiCredit
            );
        }
    }
}
