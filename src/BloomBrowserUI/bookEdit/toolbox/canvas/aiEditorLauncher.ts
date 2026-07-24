// "Edit with AI…" launcher — the front-end half of the AI Image Editor integration.
//
// The registry entry in canvasControlRegistry.ts is just the declarative menu command;
// all of the actual integration logic lives here because it is large and self-contained.
//
// This is the front-end half of a feature whose C# half is AiImageEditorApi.cs
// (read that file's header for the full picture). The editor is a SEPARATE web app
// (the `bloom-ai-image-tools` package); we do not import it — we load it into an
// <iframe> overlay. The flow:
//   1. POST aiImageEditor/launch -> C# mints a session, makes the per-book
//      .ai-image-editor folder, and returns the editor URL + the whole-book image
//      list + enumerated history + httpBase/sessionToken.
//   2. Build a fixed overlay <div id="ai-editor-overlay"> holding an <iframe> at
//      that URL with ?mode=bloom-iframe.
//   3. Handshake over window.postMessage on channel "bloom-ai-image-tools": the
//      editor posts `ready`; we post `init` (the launch reply + the right-clicked
//      image as selectedBookImageId). Image bytes never ride postMessage — they go
//      over HTTP via aiImageEditor/file; the editor references results by id.
//   4. On `commit` we POST aiImageEditor/commit; C# applies replacements to all
//      non-current pages and returns {oldSrc,newSrc} for any on the live page, which
//      we apply here via Bloom's changeImageByElement(). `cancel`/close just tear the overlay
//      down. (There is intentionally no C#->iframe message channel; init flows from
//      here, the overlay JS, because only the browser can postMessage to the iframe.)

import { post, postJson, postThatMightNavigate } from "../../../utils/bloomApi";
import {
    getImageUrlFromImageContainer,
    GetRawImageUrl,
} from "../../js/bloomImages";
import { changeImageByElement } from "../../js/bloomEditing";
import { matchReplacementsToElements } from "./aiEditorSlotMatching";

// Opens the AI Image Editor overlay for the given image. `img` is the right-clicked
// image, `imgContainer` its image container (if any), and `canvasElement` the canvas
// element the command was invoked on.
export const launchAiImageEditor = (
    img: HTMLImageElement,
    imgContainer: HTMLElement | undefined,
    canvasElement: HTMLElement,
): void => {
    post("aiImageEditor/launch", (r) => {
        const launchData = r.data as {
            editorUrl: string;
            httpBase: string;
            sessionToken: string;
            book: { id: string; title: string };
            bookImages?: Array<{
                id: string;
                src: string;
                pageLabel?: string;
                width?: number;
                height?: number;
                isPlaceholder?: boolean;
            }>;
            references?: Array<{
                id: string;
                src: string;
                name?: string;
            }>;
            // Enumerated by C# from the per-book history folder; rides through
            // the `...launchData` spread into the editor's init payload.
            history?: Array<{
                id: string;
                url: string;
                metadata?: Record<string, unknown> | null;
            }>;
            apiKey?: string | null;
            // Playground/demo context: the editor must disable its
            // "set OpenRouter API key" UI. Rides through the `...launchData`
            // spread below into the editor's init payload.
            demoOnly?: boolean;
        };
        const hostWindow = (window.top ?? window) as Window & {
            __bloomAiImageEditorCleanup?: () => void;
        };
        const hostDocument = hostWindow.document;
        const iframeUrl = new URL(
            launchData.editorUrl,
            hostWindow.location.href,
        );
        iframeUrl.searchParams.set("mode", "bloom-iframe");
        // Bloom (C#) enumerates every user-changeable image in the whole book
        // and supplies them as `launchData.bookImages`, each with a stable
        // "{pageId}:{ordinal}" id the editor echoes back on commit. The host
        // applies replacements book-wide in C#, so there is no per-image DOM
        // id wrangling here anymore.

        // Identify the image the user actually right-clicked so the editor can
        // open with it already in the "Image to Edit" slot. We match by page +
        // filename rather than DOM ordinal, because the live page has extra
        // injected UI images that would throw positional indices off.
        // `encoded` says whether the url is percent-encoded: live DOM srcs and
        // host-served URLs are, but oldSrc in commit results arrives from C#
        // already decoded (PathOnly.NotEncoded) — decoding it again corrupts
        // (or throws on) filenames containing a literal '%'. On a failed decode
        // fall back to the raw name rather than "", so an oddly-encoded src
        // degrades to a possible mismatch instead of matching nothing ever.
        const fileNameOf = (url?: string | null, encoded: boolean = true) => {
            const raw = (url ?? "").split("?")[0].split("/").pop() ?? "";
            if (!encoded) return raw;
            try {
                return decodeURIComponent(raw);
            } catch {
                return raw;
            }
        };
        const clickedUrl = imgContainer
            ? getImageUrlFromImageContainer(imgContainer as HTMLElement)
            : (img as HTMLImageElement)?.getAttribute("src");
        const clickedPageId = canvasElement
            .closest(".bloom-page")
            ?.getAttribute("id");
        const clickedFile = fileNameOf(clickedUrl);
        const clickedMatch =
            clickedPageId && clickedFile
                ? (launchData.bookImages ?? []).find(
                      (bi) =>
                          bi.id.startsWith(clickedPageId + ":") &&
                          fileNameOf(bi.src) === clickedFile,
                  )
                : undefined;
        // Don't preload an empty placeholder slot into the edit target — there's
        // nothing to edit, and its placeholder graphic isn't a real raster image.
        const selectedBookImageId = clickedMatch?.isPlaceholder
            ? undefined
            : clickedMatch?.id;

        const initPayload = {
            ...launchData,
            bookImages: launchData.bookImages ?? [],
            references: launchData.references ?? [],
            apiKey: launchData.apiKey ?? null,
            selectedBookImageId,
        };

        hostWindow.__bloomAiImageEditorCleanup?.();

        const cleanup = () => {
            hostWindow.removeEventListener("message", handleMessage);
            hostDocument.getElementById("ai-editor-overlay")?.remove();
            delete hostWindow.__bloomAiImageEditorCleanup;
        };

        const overlay = hostDocument.createElement("div");
        overlay.id = "ai-editor-overlay";
        Object.assign(overlay.style, {
            position: "fixed",
            inset: "8px",
            zIndex: "10000",
            background: "#1a1a2e",
            borderRadius: "12px",
            overflow: "hidden",
            boxShadow: "0 18px 48px rgba(0, 0, 0, 0.45)",
        });

        const closeBtn = hostDocument.createElement("button");
        closeBtn.textContent = "✕";
        Object.assign(closeBtn.style, {
            position: "absolute",
            top: "8px",
            right: "12px",
            zIndex: "10001",
            background: "transparent",
            border: "none",
            color: "#fff",
            fontSize: "20px",
            cursor: "pointer",
            opacity: "0.6",
        });
        closeBtn.onclick = cleanup;
        overlay.appendChild(closeBtn);

        const iframe = hostDocument.createElement("iframe");
        iframe.src = iframeUrl.toString();
        iframe.setAttribute("allow", "clipboard-read; clipboard-write");
        Object.assign(iframe.style, {
            width: "100%",
            height: "100%",
            border: "none",
        });
        overlay.appendChild(iframe);

        // Apply replacements the host flagged as being on the currently-edited
        // page. The host can't change that page itself (the live browser owns
        // it), so it returns oldSrc/newSrc and we use Bloom's changeImageByElement()
        // on the live DOM. We match by oldSrc rather than index because the live
        // page has extra UI images that would throw off positional ordinals.
        const applyCurrentPageReplacements = (
            results?: Array<{
                incomingId?: string;
                ok?: boolean;
                isCurrentPage?: boolean;
                oldSrc?: string;
                newSrc?: string;
            }>,
        ): { applied: number; expected: number } => {
            const toApply = (results ?? []).filter(
                (r) => r && r.ok && r.isCurrentPage && r.newSrc && r.oldSrc,
            );
            if (toApply.length === 0) return { applied: 0, expected: 0 };
            const pageDoc = img.ownerDocument;
            const pageRoot =
                (pageDoc.querySelector(".bloom-page") as HTMLElement) ||
                pageDoc;
            // Look up the page's image-bearing elements once, not per replacement.
            const candidates = Array.from(
                pageRoot.querySelectorAll('img, [style*="background-image"]'),
            );
            // A page can have several slots sharing the same source (e.g. multiple
            // empty placeholders). matchReplacementsToElements consumes each matched
            // element once so distinct replacements land on distinct elements instead
            // of collapsing onto the first match, and applies in slot (ordinal) order.
            // We match by filename (as the clicked-image lookup does), not full src, so
            // a cache-busting query string or path prefix on the live element doesn't
            // cause a silent miss. oldSrc arrives from C# already decoded; the live srcs
            // are encoded, so fileNameOf normalizes both sides.
            const pairs = matchReplacementsToElements(
                toApply,
                (r) =>
                    parseInt((r.incomingId ?? "").split(":").pop() ?? "", 10) ||
                    0,
                (r) => fileNameOf(r.oldSrc, false),
                candidates as HTMLElement[],
                (el) => fileNameOf(GetRawImageUrl(el)),
            );
            pairs.forEach(({ replacement: r, element: target }) => {
                changeImageByElement(target, {
                    src: r.newSrc as string,
                    creator: target.getAttribute("data-creator") || "",
                    copyright: target.getAttribute("data-copyright") || "",
                    license: target.getAttribute("data-license") || "",
                    // The AI commit applies replacements book-wide in C#
                    // (saved directly, not undoable), so don't register a
                    // separate per-image undo for the current-page piece.
                    undoable: "false",
                });
            });
            return { applied: pairs.length, expected: toApply.length };
        };

        const handleMessage = (event: MessageEvent) => {
            if (event.source !== iframe.contentWindow) {
                return;
            }

            const data = event.data as
                | {
                      channel?: string;
                      type?: string;
                      requestId?: string;
                      payload?: {
                          level?: string;
                          message?: string;
                          replacements?: Array<{
                              incomingId?: string;
                              resultId?: string;
                          }>;
                          apiKey?: string | null;
                      };
                  }
                | undefined;

            if (data?.channel !== "bloom-ai-image-tools") {
                return;
            }

            switch (data.type) {
                case "ready":
                    iframe.contentWindow?.postMessage(
                        {
                            channel: "bloom-ai-image-tools",
                            type: "init",
                            payload: initPayload,
                        },
                        iframeUrl.origin,
                    );
                    break;
                case "cancel":
                    cleanup();
                    break;
                case "commit": {
                    // Replacements can target images on any page of the book.
                    // The host (AiImageEditorApi.HandleCommit) applies changes to
                    // NON-current pages directly against the whole-book DOM and
                    // saves. It cannot touch the page currently open for editing
                    // (the live browser owns it), so for those it returns
                    // {isCurrentPage, oldSrc, newSrc} and we apply them here via
                    // Bloom's own changeImage() against the live page DOM.
                    const requestId = data.requestId;
                    const ackEditor = (ok: boolean, error?: string) => {
                        iframe.contentWindow?.postMessage(
                            {
                                channel: "bloom-ai-image-tools",
                                type: "ack",
                                requestId,
                                ok,
                                error,
                            },
                            iframeUrl.origin,
                        );
                    };

                    const replacements = data.payload?.replacements ?? [];
                    if (replacements.length === 0) {
                        ackEditor(false, "No replacements to apply.");
                        break;
                    }

                    postJson(
                        "aiImageEditor/commit?session=" +
                            encodeURIComponent(launchData.sessionToken),
                        { replacements },
                        (response) => {
                            const result = response?.data as
                                | {
                                      ok?: boolean;
                                      appliedCount?: number;
                                      results?: Array<{
                                          incomingId?: string;
                                          ok?: boolean;
                                          isCurrentPage?: boolean;
                                          oldSrc?: string;
                                          newSrc?: string;
                                      }>;
                                  }
                                | undefined;
                            // The server reports whether it staged every replacement;
                            // for current-page slots only this live DOM knows if the
                            // edit actually landed. Combine both so the editor's ack
                            // reflects the true outcome, and always ack (even on an
                            // apply exception) so the editor overlay can't hang.
                            let finalOk = false;
                            let message: string | undefined;
                            let currentPageApplied = 0;
                            try {
                                const cp = applyCurrentPageReplacements(
                                    result?.results,
                                );
                                currentPageApplied = cp.applied;
                                const serverOk = result?.ok !== false;
                                finalOk =
                                    serverOk && cp.applied === cp.expected;
                                if (!finalOk) {
                                    message = serverOk
                                        ? `Only ${cp.applied} of ${cp.expected} image(s) on the current page could be updated.`
                                        : "Some images could not be replaced.";
                                }
                            } catch (e) {
                                finalOk = false;
                                message =
                                    "Failed to apply current-page replacements: " +
                                    (e instanceof Error
                                        ? e.message
                                        : String(e));
                            } finally {
                                ackEditor(finalOk, message);
                                if (finalOk) {
                                    cleanup();
                                }
                                // changeImageByElement only mutated the LIVE page DOM;
                                // unlike the off-page slots (which C# saved), a current-page
                                // swap is not otherwise persisted. Save + rethink the page so
                                // storage matches the live DOM — otherwise a relaunch would
                                // enumerate the stale storage (showing the pre-edit image) and
                                // a later commit's oldSrc, read from that stale storage, would
                                // no longer match the live page ("0 of N could be updated").
                                // Mirrors doVideoCommand's save after updateVideoInContainer.
                                if (currentPageApplied > 0) {
                                    postThatMightNavigate(
                                        "common/saveChangesAndRethinkPageEvent",
                                    );
                                }
                            }
                        },
                        () => {
                            ackEditor(false, "Failed to apply replacements.");
                        },
                    );
                    break;
                }
                case "log":
                    console.log(
                        "[AI Image Editor:" +
                            (data.payload?.level ?? "info") +
                            "] " +
                            (data.payload?.message ?? ""),
                    );
                    break;
                case "saveCredentials":
                    // Bloom owns the OpenRouter API key. When the user pastes a key
                    // in the editor, the editor hands it up here so Bloom persists it
                    // per-user (and supplies it on the next launch). A null apiKey
                    // clears the stored key.
                    postJson(
                        "aiImageEditor/saveCredentials?session=" +
                            encodeURIComponent(launchData.sessionToken),
                        {
                            apiKey: data.payload?.apiKey ?? null,
                        },
                    );
                    break;
            }
        };

        hostWindow.addEventListener("message", handleMessage);
        hostWindow.__bloomAiImageEditorCleanup = cleanup;
        hostDocument.body.appendChild(overlay);
    });
};
