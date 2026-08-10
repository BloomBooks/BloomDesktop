// "Edit with AI…" launcher — the front-end half of the AI Image Editor integration.
//
// The registry entry in canvasControlRegistry.ts is just the declarative menu command;
// all of the actual integration logic lives here because it is large and self-contained.
//
// This is the front-end half of a feature whose C# half is AiImageEditorApi.cs
// (read that file's header for the full picture). The AI image editor is a SEPARATE web app
// (the `bloom-ai-image-tools` package); we do not import it — we load it into an
// <iframe> overlay. The flow:
//   0. POST aiImageEditor/saveThenLaunch -> C# saves the current page (which RELOADS the
//      page frame) and then calls openAiImageEditor() below in the reloaded page. See
//      launchAiImageEditor for why the page has to be saved first, and why C# rather than
//      this frame is what remembers to carry on afterwards.
//   1. POST aiImageEditor/launch -> C# mints a session, makes the per-book
//      .ai-image-editor folder, and returns the AI image editor URL + the whole-book image
//      list + enumerated history + httpBase/sessionToken.
//   2. Build a fixed overlay <div id="ai-editor-overlay"> holding an <iframe> at
//      that URL with ?mode=bloom-iframe.
//   3. Handshake over window.postMessage on channel "bloom-ai-image-tools": the
//      AI image editor posts `ready`; we post `init` (the launch reply + the right-clicked
//      image as selectedBookImageId). Image bytes never ride postMessage — they go
//      over HTTP via aiImageEditor/file; the AI image editor references results by id.
//   4. On `commit` we POST aiImageEditor/commit; C# applies replacements to all
//      non-current pages and returns {oldSrc,newSrc,copyright,creator,license} for any on
//      the live page, which we apply here via Bloom's changeImageByElement(). The credits
//      come from the host because it read them off the new image file; see the comment at
//      the changeImageByElement call. `cancel`/close just tear the overlay
//      down. (There is intentionally no C#->iframe message channel; init flows from
//      here, the overlay JS, because only the browser can postMessage to the iframe.)

import { post, postJson, postThatMightNavigate } from "../../../utils/bloomApi";
import {
    getImageUrlFromImageContainer,
    GetRawImageUrl,
} from "../../js/bloomImages";
import { changeImageByElement } from "../../js/bloomEditing";
import { matchReplacementsToElements } from "./aiEditorSlotMatching";

// Pull the file name off an image url. `encoded` says whether the url is percent-encoded:
// live DOM srcs and host-served URLs are, but oldSrc in commit results arrives from C#
// already decoded (PathOnly.NotEncoded) — decoding it again corrupts (or throws on)
// filenames containing a literal '%'. On a failed decode fall back to the raw name rather
// than "", so an oddly-encoded src degrades to a possible mismatch instead of matching
// nothing ever.
const fileNameOf = (url?: string | null, encoded: boolean = true) => {
    const raw = (url ?? "").split("?")[0].split("/").pop() ?? "";
    if (!encoded) return raw;
    try {
        return decodeURIComponent(raw);
    } catch {
        return raw;
    }
};

// What openAiImageEditor needs to know about the image the user right-clicked. Just the
// file name: a page save reloads this frame, so nothing that survives the trip to C# and
// back can be a live element reference — and the page id is simply whatever page comes
// back, which we read off the reloaded document.
export interface IAiImageEditorTarget {
    imageFileName: string;
}

// Starts "Edit with AI…" for the given image. `img` is the right-clicked image and
// `imgContainer` its image container (if any).
//
// We do NOT open the editor here. C# enumerates the book's images (and, on commit, reads
// each slot's current src) from the SAVED book DOM, while an image the user just added
// lives only in this live page — changeImage/changeImageByElement deliberately do not save
// (BL-16330). Launching against that stale saved DOM opened the editor with an empty
// "Image to Edit" slot (BL-16682), and a later commit's oldSrc, read from the same stale
// DOM, would no longer match the live page. So the page must be saved first.
//
// Saving is what makes this a round trip through C#: Bloom's save strips the live page and
// therefore always ends by re-navigating to it, which tears down this frame — so the code
// that carries on afterwards cannot live here. C# does the save with EditingModel.SaveThen
// and calls openAiImageEditor() below once the reloaded page reports in. See
// AiImageEditorApi.HandleSaveThenLaunch.
export const launchAiImageEditor = (
    img: HTMLImageElement,
    imgContainer: HTMLElement | undefined,
): void => {
    const clickedUrl = imgContainer
        ? getImageUrlFromImageContainer(imgContainer)
        : img?.getAttribute("src");
    postJson("aiImageEditor/saveThenLaunch", {
        imageFileName: fileNameOf(clickedUrl),
    } as IAiImageEditorTarget);
};

// Opens the AI Image Editor overlay, with the image named by `target` (the one the user
// right-clicked, back before the save reloaded this page) in its "Image to Edit" slot.
// Called from C# — via workspaceBundle.getEditablePageBundleExports() — once the page has
// been saved and reloaded; see launchAiImageEditor above, which is what asks for that.
export const openAiImageEditor = (target: IAiImageEditorTarget): void => {
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
            // the `...launchData` spread into the AI image editor's init payload.
            history?: Array<{
                id: string;
                url: string;
                metadata?: Record<string, unknown> | null;
            }>;
            apiKey?: string | null;
            // Playground/demo context: the AI image editor must disable its
            // "set OpenRouter API key" UI. Rides through the `...launchData`
            // spread below into the AI image editor's init payload.
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
        // "{pageId}:{ordinal}" id the AI image editor echoes back on commit. The host
        // applies replacements book-wide in C#, so there is no per-image DOM
        // id wrangling here anymore.

        // Identify the image the user right-clicked so the AI image editor can
        // open with it already in the "Image to Edit" slot. We match by page +
        // filename rather than DOM ordinal, because the live page has extra
        // injected UI images that would throw positional indices off.
        const clickedPageId = document
            .querySelector(".bloom-page")
            ?.getAttribute("id");
        const clickedFile = target.imageFileName;
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

        // Set once a current-page swap has landed in the live DOM and so needs saving
        // (see the commit handler, which explains why). The save is DEFERRED to
        // cleanup() because saveChangesAndRethinkPageEvent reloads the page frame, and
        // everything that operates this overlay — the message listener, the ✕ button's
        // handler, this cleanup function itself — is code belonging to that frame, even
        // though the overlay div lives in the top window. Saving while the overlay is
        // still up therefore kills its controls and leaves a full-screen overlay the
        // user can't close without restarting Bloom. That matters because on a partial
        // failure we deliberately keep the overlay up for the user to read the error,
        // so the save has to wait until they close it.
        let livePageNeedsSaving = false;

        const cleanup = () => {
            hostWindow.removeEventListener("message", handleMessage);
            hostDocument.getElementById("ai-editor-overlay")?.remove();
            delete hostWindow.__bloomAiImageEditorCleanup;
            if (livePageNeedsSaving) {
                livePageNeedsSaving = false;
                postThatMightNavigate("common/saveChangesAndRethinkPageEvent");
            }
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
        // onApplied fires after each successful swap. The caller counts with it rather
        // than with the returned `applied`, because a throw part-way through this loop
        // never returns: the live DOM would then hold swaps that nothing had counted,
        // so nothing would save them (see the save call in the commit handler).
        const applyCurrentPageReplacements = (
            results?: Array<{
                incomingId?: string;
                ok?: boolean;
                isCurrentPage?: boolean;
                oldSrc?: string;
                newSrc?: string;
                // The data-copyright/data-creator/data-license the new file's embedded
                // metadata calls for, as the host read them back off that file.
                copyright?: string;
                creator?: string;
                license?: string;
            }>,
            onApplied?: () => void,
        ): { applied: number; expected: number } => {
            const toApply = (results ?? []).filter(
                (r) => r && r.ok && r.isCurrentPage && r.newSrc && r.oldSrc,
            );
            if (toApply.length === 0) return { applied: 0, expected: 0 };
            // This code always runs in the page frame (C# calls openAiImageEditor there),
            // so the live page is simply this document.
            const pageRoot =
                (document.querySelector(".bloom-page") as HTMLElement) ||
                document;
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
                    // Take the credits from the host, which read them off the new image
                    // file. Reading them off `target` instead would carry the REPLACED
                    // image's credits forward, so the page would go on showing credits
                    // (and no "missing information" indicator) for a new image that has
                    // none — until the next book-up-to-date pass re-derived them from the
                    // file and they silently vanished (BL-16603).
                    creator: r.creator ?? "",
                    copyright: r.copyright ?? "",
                    license: r.license ?? "",
                    // The AI commit applies replacements book-wide in C#
                    // (saved directly, not undoable), so don't register a
                    // separate per-image undo for the current-page piece.
                    undoable: "false",
                });
                onApplied?.();
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
                          // We relay this array to C# as-is, so every field the AI
                          // image editor sends is declared here even though nothing
                          // on this side reads them. Rebuilding the array field by
                          // field instead of passing it through would silently drop
                          // `credits`, and the result would lose its credits — the
                          // bug this whole feature exists to prevent.
                          replacements?: Array<{
                              incomingId?: string;
                              resultId?: string;
                              sourceUrl?: string;
                              credits?: {
                                  copyrightNotice?: string;
                                  creator?: string;
                                  // Two license fields, not one: a CC license has both a
                                  // URL and (possibly) free-text license notes, and
                                  // flattening them into one string lost the notes
                                  // (BL-16603). See AiImageEditorApi.ImageCredits.
                                  licenseUrl?: string;
                                  licenseRightsStatement?: string;
                                  attributionUrl?: string;
                                  collectionName?: string;
                                  collectionUri?: string;
                              } | null;
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
                                          copyright?: string;
                                          creator?: string;
                                          license?: string;
                                      }>;
                                  }
                                | undefined;
                            // The server reports whether it staged every replacement;
                            // for current-page slots only this live DOM knows if the
                            // edit actually landed. Combine both so the AI image editor's ack
                            // reflects the true outcome, and always ack (even on an
                            // apply exception) so the AI image editor overlay can't hang.
                            let finalOk = false;
                            let message: string | undefined;
                            let currentPageApplied = 0;
                            try {
                                const cp = applyCurrentPageReplacements(
                                    result?.results,
                                    () => currentPageApplied++,
                                );
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
                                // changeImageByElement only mutated the LIVE page DOM;
                                // unlike the off-page slots (which C# saved), a current-page
                                // swap is not otherwise persisted. Save + rethink the page so
                                // storage matches the live DOM — otherwise a relaunch would
                                // enumerate the stale storage (showing the pre-edit image) and
                                // a later commit's oldSrc, read from that stale storage, would
                                // no longer match the live page ("0 of N could be updated").
                                // Mirrors doVideoCommand's save after updateVideoInContainer.
                                // currentPageApplied is counted per swap as it happens, so a
                                // throw part-way through still saves the swaps that landed.
                                // cleanup() is what actually saves, so flag it BEFORE the
                                // cleanup() call below: on success the save then happens at
                                // once, and on a partial failure it waits for the user to
                                // close the overlay they are reading the error in.
                                if (currentPageApplied > 0) {
                                    livePageNeedsSaving = true;
                                }
                                if (finalOk) {
                                    cleanup();
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
                    // Bloom owns the OpenRouter API key. A key the user pastes into the AI
                    // image editor is handed up here so Bloom persists it per-user (and
                    // supplies it on the next launch). A null apiKey clears the stored key.
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
