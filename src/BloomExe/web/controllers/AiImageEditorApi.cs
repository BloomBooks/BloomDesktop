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
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Handles the AI Image Editor integration: launching the editor as an iframe
    /// overlay within the existing Bloom WebView2 and serving per-book file I/O
    /// from the .ai-image-editor folder.
    /// </summary>
    public class AiImageEditorApi
    {
        private readonly BookSelection _bookSelection;

        /// <summary>Set by EditingView constructor — gives access to the main browser.</summary>
        public EditingView View { get; set; }

        // Minted at launch; invalidated when the editor sends `cancel`.
        private string _sessionToken;

        // The CoreWebView2Frame for the active editor iframe (set when editor sends `ready`).
        private CoreWebView2Frame _editorFrame;

        // Stored so we can unsubscribe when the session ends.
        private Action<object, (CoreWebView2Frame Frame, string Json)> _frameMessageHandler;

        private string _pendingOAuthCode;
        private string _pendingOAuthError;

        private static readonly Regex AllowedFileName = new Regex(
            @"^(state\.json|connection\.json|history/[a-zA-Z0-9_\-]+\.png)$",
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
#if DEBUG
            return "http://localhost:3000/";
#else
            return $"{BloomServer.ServerUrl}/bloom/aiImageEditor/index.html";
#endif
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

            var httpBase =
                $"{BloomServer.ServerUrlWithBloomPrefixEndingInSlash}api/aiImageEditor";

            // Subscribe to iframe messages so we can send `init` when the editor is ready.
            var mainBrowser = View?.MainBrowser as WebView2Browser;
            if (mainBrowser != null)
            {
                _frameMessageHandler = OnEditorFrameMessage;
                mainBrowser.FrameWebMessageReceived += _frameMessageHandler;
            }

            // Return the data the JS needs to create the iframe overlay. The editor
            // runs in iframe mode and gets its `init` from the overlay JS, which builds
            // it from this reply (it does NOT receive the WebView2 frame message that
            // SendInit posts). So the whole-book image list must travel here.
            request.ReplyWithJson(
                new
                {
                    editorUrl = GetEditorUrl(),
                    httpBase,
                    sessionToken = _sessionToken,
                    book = new { id = book.BookInfo.Id, title = book.BookInfo.Title },
                    bookImages = EnumerateBookImages(book),
                    references = Array.Empty<object>(),
                    apiKey = (string)null,
                }
            );
        }

        private void OnEditorFrameMessage(
            object sender,
            (CoreWebView2Frame Frame, string Json) args
        )
        {
            try
            {
                dynamic message = JsonConvert.DeserializeObject(args.Json);
                string type = (string)message?.type;

                switch (type)
                {
                    case "ready":
                        _editorFrame = args.Frame;
                        SendInit(args.Frame);
                        break;

                    case "cancel":
                        RemoveOverlay();
                        EndSession();
                        break;

                    case "log":
                        var level = (string)message?.payload?.level ?? "info";
                        var text = (string)message?.payload?.message ?? "";
                        Debug.WriteLine($"[AiImageEditor:{level}] {text}");
                        break;

                    case "open-external":
                        OpenExternalUrl((string)message?.payload?.url);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AiImageEditorApi frame message error: {ex.Message}");
            }
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

        private void SendInit(CoreWebView2Frame frame)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null || _sessionToken == null)
                return;

            var httpBase =
                $"{BloomServer.ServerUrlWithBloomPrefixEndingInSlash}api/aiImageEditor";
            var initPayload = new
            {
                book = new { id = book.BookInfo.Id, title = book.BookInfo.Title },
                httpBase,
                sessionToken = _sessionToken,
                // The iframe editor receives its real init (incl. bookImages) from the
                // overlay JS built off the launch reply; it does not consume this WebView2
                // frame message, so there's no need to enumerate the whole book again here.
                bookImages = Array.Empty<object>(),
                references = Array.Empty<object>(),
                apiKey = (string)null,
            };
            var json = JsonConvert.SerializeObject(new { type = "init", payload = initPayload });
            (View?.MainBrowser as WebView2Browser)?.PostWebMessageAsJsonToFrame(frame, json);
        }

        private void RemoveOverlay()
        {
            var mainBrowser = View?.MainBrowser as WebView2Browser;
            mainBrowser?.RunJavascriptFireAndForget(
                "document.getElementById('ai-editor-overlay')?.remove();"
            );
        }

        private void EndSession()
        {
            _sessionToken = null;
            _editorFrame = null;
            _pendingOAuthCode = null;
            _pendingOAuthError = null;

            var mainBrowser = View?.MainBrowser as WebView2Browser;
            if (mainBrowser != null && _frameMessageHandler != null)
            {
                mainBrowser.FrameWebMessageReceived -= _frameMessageHandler;
                _frameMessageHandler = null;
            }
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
                request.ReplyWithText($"OpenRouter sign-in failed: {error}. You can return to Bloom.");
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
                    if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
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
                sourceBytesPath = Path.Combine(editorFolder, "history", replacement.resultId + ".png");
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

            HtmlDom.SetImageElementUrl(element, UrlPathString.CreateFromUnencodedString(newFileName));

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
                if (!AllowedImageExtensions.Contains(Path.GetExtension(fullCandidate).ToLowerInvariant()))
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
