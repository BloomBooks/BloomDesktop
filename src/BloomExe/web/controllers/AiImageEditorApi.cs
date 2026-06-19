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
using Bloom.ToPalaso;
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
    ///   DEBUG  : http://localhost:3000/  — the editor repo's Vite dev server (HMR).
    ///   RELEASE: {ServerUrl}/bloom/aiImageEditor/index.html — the editor's built app
    ///            ("dist-app"), served same-origin by BloomServer so there's no CORS.
    ///   NOTE: the RELEASE path is not fully wired yet. A build step must copy the
    ///   editor package's `dist-app/` into output/browser/aiImageEditor/ (mirroring the
    ///   existing `bp-to-output` copy for bloom-player), and Bloom must take a dependency
    ///   on the published `bloom-ai-image-tools` package as the source.
    ///
    /// TWO COMMUNICATION PLANES
    ///   1. HTTP, editor/front-end JS -> this controller, over Bloom's own local server:
    ///        aiImageEditor/launch        mint session, make folders, enumerate book
    ///                                    images + history, return the launch payload.
    ///        aiImageEditor/file          GET/POST/DELETE files under .ai-image-editor/.
    ///        aiImageEditor/commit        apply the chosen replacements to the book.
    ///        aiImageEditor/openExternal  open an OpenRouter OAuth URL in the real browser.
    ///        aiImageEditor/oauth-*       OAuth callback + result polling.
    ///   2. window.postMessage on channel "bloom-ai-image-tools", between the overlay JS
    ///      (CanvasElementContextControls.tsx) and the editor iframe: ready / init /
    ///      commit / cancel / log / open-external / ack. The overlay JS — NOT this class —
    ///      sends `init` (built from the launch reply) and tears the overlay down. Image
    ///      BYTES never cross postMessage; they move only as files via aiImageEditor/file.
    ///
    /// DATA ON DISK
    ///   Per-book folder `<book>/.ai-image-editor/` with `history/<id>.png` images and
    ///   `history/<id>.json` sidecars. The history folder is the source of truth.
    ///
    /// SECURITY
    ///   A per-launch session token (query param) gates /file, /commit, /oauth-result.
    ///   File names are allow-listed; page/result ids are charset-restricted; reused
    ///   source URLs must resolve inside the book folder (no path traversal); openExternal
    ///   is restricted to https openrouter.ai.
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

        private string _pendingOAuthCode;
        private string _pendingOAuthError;

        // Files the /file endpoint may read/write/delete: the two top-level json files,
        // history image bytes (any supported raster extension), and the per-image
        // history sidecars (history/<id>.json) that travel with each image.
        private static readonly Regex AllowedFileName = new Regex(
            @"^(state\.json|connection\.json|history/[a-zA-Z0-9_\-]+\.(png|jpe?g|gif|webp|bmp|tiff?|svg|json))$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public AiImageEditorApi(BookSelection bookSelection)
        {
            _bookSelection = bookSelection;
        }

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
                "aiImageEditor/openExternal",
                HandleOpenExternal,
                handleOnUiThread: true,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/saveCredentials",
                HandleSaveCredentials,
                handleOnUiThread: false,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/oauth-callback",
                HandleOAuthCallback,
                handleOnUiThread: false,
                requiresSync: false
            );
            apiHandler.RegisterEndpointHandler(
                "aiImageEditor/oauth-result",
                HandleOAuthResult,
                handleOnUiThread: false,
                requiresSync: false
            );
        }

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

        private void HandleLaunch(ApiRequest request)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null)
            {
                request.Failed("No book selected");
                return;
            }

            // Tear down any previous session.
            EndSession();

            _sessionToken = Guid.NewGuid().ToString("N");
            _pendingOAuthCode = null;
            _pendingOAuthError = null;

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
                    // Bloom owns the OpenRouter key: supply the per-user stored key (and
                    // user name) so the editor doesn't have to ask for it again. The editor
                    // hands any newly obtained key back via aiImageEditor/saveCredentials.
                    apiKey = OpenRouterCredentialStore.GetApiKey(),
                    openRouterUser = OpenRouterCredentialStore.GetOpenRouterUser(),
                }
            );
        }

        private class SaveCredentialsRequest
        {
            public string apiKey { get; set; }
            public string authMethod { get; set; }
            public string openRouterUser { get; set; }
        }

        /// <summary>
        /// Receives the user's OpenRouter credentials from the editor (after OAuth sign-in or
        /// manual key entry) and persists them per-user via <see cref="OpenRouterCredentialStore"/>.
        /// A null/empty apiKey clears the stored credentials (sign-out). Session-gated so a
        /// stray frame can't overwrite the user's stored key.
        /// </summary>
        private void HandleSaveCredentials(ApiRequest request)
        {
            if (!HasValidSession(request))
                return;

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

            OpenRouterCredentialStore.Save(
                payload.apiKey,
                payload.authMethod,
                payload.openRouterUser
            );
            request.PostSucceeded();
        }

        private void HandleOpenExternal(ApiRequest request)
        {
            if (!HasValidSession(request))
                return;

            // unescape: false — the body is an already-encoded auth URL whose
            // callback_url param must not be double-decoded.
            OpenExternalUrl(request.RequiredPostString(unescape: false));
            request.PostSucceeded();
        }

        /// <summary>
        /// Opens an OAuth URL in the user's default browser (with their normal
        /// OpenRouter identity), rather than navigating the WebView. Restricted to
        /// HTTPS openrouter.ai so a compromised editor frame can't ask Bloom to
        /// launch arbitrary URLs or protocols.
        /// </summary>
        private static void OpenExternalUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return;
            if (uri.Scheme != Uri.UriSchemeHttps)
                return;
            if (!uri.Host.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase))
                return;

            ProcessExtra.SafeStartInFront(uri.AbsoluteUri);
        }

        // Invalidates the current session. Called at the start of each launch to tear down
        // any prior session; the overlay itself is created and removed by the overlay JS.
        private void EndSession()
        {
            _sessionToken = null;
            _pendingOAuthCode = null;
            _pendingOAuthError = null;
        }

        private bool HasValidSession(ApiRequest request)
        {
            var session = request.GetParamOrNull("session");
            if (_sessionToken == null || session != _sessionToken)
            {
                request.Failed(HttpStatusCode.Unauthorized, "Invalid or expired session");
                return false;
            }

            return true;
        }

        private void HandleOAuthCallback(ApiRequest request)
        {
            // No session check here: this endpoint is hit by the user's external
            // browser (redirected from OpenRouter), which has no session token, and
            // the callback URL is deliberately stable so OpenRouter reuses one app
            // record. The code is useless without the PKCE verifier held by the
            // editor, and the /oauth-result poll that hands it back is session-gated.
            var error = request.GetParamOrNull("error");
            var code = request.GetParamOrNull("code");

            _pendingOAuthError = error;
            _pendingOAuthCode = code;

            if (!string.IsNullOrEmpty(error))
            {
                request.ReplyWithText(
                    $"OpenRouter sign-in failed: {error}. You can return to Bloom."
                );
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                request.Failed(HttpStatusCode.BadRequest, "Missing OAuth code");
                return;
            }

            request.ReplyWithText("OpenRouter sign-in completed. You can return to Bloom.");
        }

        private void HandleOAuthResult(ApiRequest request)
        {
            if (!HasValidSession(request))
                return;

            var answer = new { code = _pendingOAuthCode, error = _pendingOAuthError };
            _pendingOAuthCode = null;
            _pendingOAuthError = null;
            request.ReplyWithJson(answer);
        }

        private void HandleFile(ApiRequest request)
        {
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
                    var bytes = request.RawPostData;
                    if (bytes != null && bytes.Length > 0)
                        RobustFile.WriteAllBytes(fullPath, bytes);
                    request.PostSucceeded();
                    break;

                case HttpMethods.Delete:
                    if (RobustFile.Exists(fullPath))
                        RobustFile.Delete(fullPath);
                    request.PostSucceeded();
                    break;

                case HttpMethods.Options:
                    // Handle CORS preflight so that the dev Vite server (localhost:3000)
                    // can fetch this endpoint cross-origin from the main Bloom server.
                    request.ReplyWithText("");
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

        private static readonly HashSet<string> AllowedImageExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".webp",
            ".svg",
            ".bmp",
            ".tif",
            ".tiff",
        };

        private static bool IsImageFileName(string name) =>
            AllowedImageExtensions.Contains(Path.GetExtension(name));

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
                    if (
                        element.HasClass("branding")
                        || element.HasClass("licenseImage")
                        || element.HasClass("bloom-qrcode")
                    )
                        continue;

                    var relativePath = HtmlDom.GetImageElementUrl(element).PathOnly.NotEncoded;
                    if (string.IsNullOrEmpty(relativePath))
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
            var appliedCount = 0;
            var savedAnyOffPage = false;

            foreach (var replacement in replacements)
            {
                var applied = TryApplyReplacement(
                    book,
                    editorFolder,
                    replacement,
                    currentPageId,
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

            request.ReplyWithJson(
                new
                {
                    ok = appliedCount == replacements.Count,
                    appliedCount,
                    results,
                }
            );
        }

        private bool TryApplyReplacement(
            Bloom.Book.Book book,
            string editorFolder,
            CommitReplacement replacement,
            string currentPageId,
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

            var separator = replacement.incomingId.LastIndexOf(':');
            if (separator <= 0)
            {
                error = "Malformed incomingId";
                return false;
            }
            var pageId = replacement.incomingId.Substring(0, separator);
            if (
                !SafeId.IsMatch(pageId)
                || !int.TryParse(replacement.incomingId.Substring(separator + 1), out var ordinal)
            )
            {
                error = "Malformed incomingId";
                return false;
            }

            if (
                !(
                    book.OurHtmlDom.RawDom.SelectSingleNode("//div[@id='" + pageId + "']")
                    is SafeXmlElement page
                )
            )
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
            if (
                element.HasClass("branding")
                || element.HasClass("licenseImage")
                || element.HasClass("bloom-qrcode")
            )
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
                sourceBytesPath = Path.Combine(
                    editorFolder,
                    "history",
                    replacement.resultId + ".png"
                );
                if (!RobustFile.Exists(sourceBytesPath))
                {
                    error = "Result image not found";
                    return false;
                }
            }
            else if (
                !string.IsNullOrEmpty(replacement.sourceUrl)
                && TryResolveServedUrlToBookFile(book, replacement.sourceUrl, out sourceBytesPath)
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
            var extension = Path.GetExtension(sourceBytesPath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";
            var newFileName = "ai-" + Guid.NewGuid().ToString("N") + extension;
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
        /// back to a file path, requiring that it lands on an existing file inside the current
        /// book folder. Guards against path traversal so the editor can't have us read or copy
        /// arbitrary files.
        /// </summary>
        private bool TryResolveServedUrlToBookFile(
            Bloom.Book.Book book,
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
                var bookFolder = Path.GetFullPath(book.FolderPath);
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

        private static void AppendEditedWithAiCredit(SafeXmlElement element)
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
