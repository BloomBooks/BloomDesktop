// The AI Image Editor overlay and session — the TOP-WINDOW half of the feature.
//
// This runs in the workspace root, not the page iframe, for the same reason the image
// gallery and the copyright/license dialog do (see the comments on those commands in
// canvasControlRegistry.ts): the page frame gets reloaded whenever the page is saved, and
// an overlay whose own controls belonged to that frame would be left on screen with
// nothing able to close it. Everything that needs the live page is asked of the page
// frame through getEditablePageBundleExports() (see aiEditorPageCommands.ts).
//
// The C# half is AiImageEditorApi.cs (read that file's header for the full picture). The
// AI image editor is a SEPARATE web app (the `bloom-ai-image-tools` package); we do not
// import it — we load it into an <iframe> overlay. The flow:
//   0. The menu command (aiEditorPageCommands.launchAiImageEditor, in the page frame)
//      POSTs aiImageEditor/saveThenLaunch. C# saves the page being edited — which the
//      whole-book image list below depends on, and which reloads the page frame — and then
//      calls openAiImageEditor() here. See HandleSaveThenLaunch.
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
//      the live page, which the page frame applies for us. `cancel`/close just tear the
//      overlay down. (There is intentionally no C#->iframe message channel; init flows from
//      here, because only the browser can postMessage to the iframe.)

import { post, postJson, postThatMightNavigate } from "../../../utils/bloomApi";
import { getEditablePageBundleExports } from "../../js/workspaceFrames";
import {
    fileNameOf,
    IAiImageEditorCommitResult,
    IAiImageEditorTarget,
} from "./aiEditorShared";

// Opens the AI Image Editor overlay, with the image named by `target` (the one the user
// right-clicked, before the save reloaded the page frame) in its "Image to Edit" slot.
// Called from C# — via workspaceBundle.openAiImageEditor — once the page has been saved.
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
        const hostWindow = window as Window & {
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

        // Identify the image the user right-clicked so the AI image editor can open with it
        // already in the "Image to Edit" slot. We match by page + filename rather than DOM
        // ordinal, because the live page has extra injected UI images that would throw
        // positional indices off.
        const clickedMatch =
            target.pageId && target.imageFileName
                ? (launchData.bookImages ?? []).find(
                      (bi) =>
                          bi.id.startsWith(target.pageId + ":") &&
                          fileNameOf(bi.src) === target.imageFileName,
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
                    // {isCurrentPage, oldSrc, newSrc} and the page frame applies them
                    // via Bloom's own changeImageByElement().
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
                                      results?: IAiImageEditorCommitResult[];
                                  }
                                | undefined;
                            // The server reports whether it staged every replacement; for
                            // current-page slots only the live DOM knows whether the edit
                            // actually landed. Combine both so the AI image editor's ack
                            // reflects the true outcome, and always ack (even when the
                            // apply fails) so its overlay can't hang.
                            let finalOk = false;
                            let message: string | undefined;
                            let currentPageApplied = 0;
                            try {
                                const pageFrame =
                                    getEditablePageBundleExports();
                                if (!pageFrame) {
                                    throw new Error(
                                        "the page being edited is not available",
                                    );
                                }
                                const cp =
                                    pageFrame.applyAiImageEditorReplacements(
                                        result?.results,
                                    );
                                currentPageApplied = cp.applied;
                                const serverOk = result?.ok !== false;
                                finalOk =
                                    serverOk &&
                                    cp.applied === cp.expected &&
                                    !cp.error;
                                if (!finalOk) {
                                    message = serverOk
                                        ? `Only ${cp.applied} of ${cp.expected} image(s) on the current page could be updated.`
                                        : "Some images could not be replaced.";
                                    // The page frame reports a failed swap as a return value
                                    // rather than an exception (it has to, so we still learn
                                    // how many landed and therefore need saving). Append its
                                    // reason, or the count would be all the user ever saw.
                                    if (cp.error) {
                                        message += " " + cp.error;
                                    }
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
                                // unlike the off-page slots (which C# saved), a
                                // current-page swap is not otherwise persisted. Save +
                                // rethink the page so the saved DOM matches the live one:
                                // otherwise a second commit in this same session would
                                // read its oldSrc from a saved page still showing the
                                // pre-edit image and match nothing ("0 of N could be
                                // updated"). Mirrors doVideoCommand's save after
                                // updateVideoInContainer.
                                //
                                // We can save right now, even with the overlay still up,
                                // precisely because this overlay lives in the top window:
                                // the page reload underneath it leaves its controls alone.
                                // (currentPageApplied is what the page frame says landed,
                                // so a failure part way through still saves the rest.)
                                if (currentPageApplied > 0) {
                                    postThatMightNavigate(
                                        "common/saveChangesAndRethinkPageEvent",
                                    );
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
