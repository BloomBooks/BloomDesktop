// The AI Image Editor overlay and session — the TOP-WINDOW half of the feature.
//
// This runs in the workspace root, not the page iframe, for the same reason the image
// gallery and the copyright/license dialog do (see the comments on those commands in
// canvasControlRegistry.ts): the page frame gets reloaded whenever the page is saved, and
// an overlay whose own controls belonged to that frame would be left on screen with
// nothing able to close it. Everything that needs the live page is asked of the page
// frame through getEditablePageBundleExports() (see aiImageEditorPageCommands.ts).
//
// The C# half is AiImageEditorApi.cs (read that file's header for the full picture). The
// AI Image Editor is a SEPARATE web app (the `bloom-ai-image-tools` package); we do not
// import it — we load it into an <iframe> overlay. The flow:
//   0. The menu command (aiImageEditorPageCommands.launchAiImageEditor, in the page frame)
//      POSTs aiImageEditor/saveThenLaunch. C# saves the page being edited — which the
//      whole-book image list below depends on, and which reloads the page frame — and then
//      calls openAiImageEditor() here. See HandleSaveThenLaunch.
//   1. POST aiImageEditor/launch -> C# mints a session, makes the per-book
//      .ai-image-editor folder, and returns the AI Image Editor URL + the whole-book image
//      list + enumerated history + httpBase/sessionToken.
//   2. Build a fixed overlay <div id="ai-image-editor-overlay"> holding an <iframe> at
//      that URL with ?mode=bloom-iframe.
//   3. Handshake over window.postMessage on channel "bloom-ai-image-tools": the
//      AI Image Editor posts `ready`; we post `init` (the launch reply + the right-clicked
//      image as selectedBookImageId). Image bytes never ride postMessage — they go
//      over HTTP via aiImageEditor/file; the AI Image Editor references results by id.
//   4. On `commit` we POST aiImageEditor/commit; C# applies replacements to all
//      non-current pages and returns {oldSrc,newSrc,copyright,creator,license} for any on
//      the live page, which the page frame applies for us. `cancel`/close just tear the
//      overlay down. (There is intentionally no C#->iframe message channel; init flows from
//      here, because only the browser can postMessage to the iframe.)

import {
    post,
    postJson,
    postString,
    trackChangePicture,
    trackEvent,
} from "../../utils/bloomApi";
import { getEditablePageBundleExports } from "../js/workspaceFrames";
import {
    getSuggestedImageTargetForFraction,
    IDigitalScreen,
} from "../js/imageTargetResolution";
import {
    IAiImageEditorApplyOutcome,
    IAiImageEditorCommitResult,
    IAiImageEditorTarget,
    isCurrentPageSwap,
} from "./aiImageEditorShared";

// How long the close box waits for the AI Image Editor to answer "request-close" before closing
// the overlay without it.
const kCloseFallbackMs = 2000;

// Hand the commit's current-page swaps to the page frame, which owns the live page. Only call
// this when there is such a swap (see isCurrentPageSwap): the frame is briefly unreachable while
// it reloads, and a commit with nothing to do on that page must not be failed for that.
function applyOnThePageBeingEdited(
    results?: IAiImageEditorCommitResult[],
): IAiImageEditorApplyOutcome {
    const pageFrame = getEditablePageBundleExports();
    if (!pageFrame) {
        // Say what DID happen as well as what didn't: by now C# has applied and saved every
        // off-page replacement, so "the commit failed" on its own would invite a blind retry
        // that redoes those and orphans their files.
        throw new Error(
            "the page being edited is not available, so only the replacements on other " +
                "pages were made (those are saved)",
        );
    }
    return pageFrame.applyAiImageEditorReplacements(results);
}

// Tells the AI Image Editor how big each slot in the book wants its image to be. The editor
// offers that as the Upscale tool's "Auto" size, and shows the memo under the selector; a slot
// with no suggestedTarget simply gets no "Auto" option.
//
// It can answer for EVERY page, not just the open one, because Bloom records each slot's share
// of its page in the HTML whenever the page is saved (recordFractionOfPageOnImageSlots), and
// C# hands that back with each book image. By the time this editor opens, every page carries the
// value: launching it on a book that has not been through the off-screen per-page pass runs that
// pass first, re-saving every page, and only then opens the editor (HandleSaveThenLaunch, via
// EditingModel.UpdatePageLayoutIfNeededThen; BL-16852). A slot with no value is therefore
// not the ordinary state of an unvisited page but a sign that the pass did not run or failed; the
// editor then offers that slot no "Auto" option rather than a guess. Every editor launch also
// saves the page being edited first, so that page at least always has it.
//
// The one thing only the live page can say is how big the page is, which is the same for every
// page in the book, so we ask the page frame once. Nothing here may stop the editor opening:
// the page frame is a separate bundle that may not be attached yet, so a miss is reported to
// the console and otherwise ignored.
//
// `digitalScreen` is the book's BloomPUB image limit, which C# sends with the launch reply and
// which decides how many pixels a slot on a screen-sized page is worth.
function addSuggestedTargets(
    bookImages: Array<{
        fractionOfPage?: { width: number; height: number } | null;
        suggestedTarget?: { width: number; height: number; memo: string };
    }>,
    digitalScreen: IDigitalScreen,
): void {
    try {
        const page =
            getEditablePageBundleExports()?.getAiImageEditorPageMetrics();
        if (!page) return;
        bookImages.forEach((bookImage) => {
            if (!bookImage.fractionOfPage) return;
            const suggestion = getSuggestedImageTargetForFraction(
                bookImage.fractionOfPage,
                page,
                digitalScreen,
            );
            if (!suggestion) return;
            bookImage.suggestedTarget = {
                width: suggestion.width,
                height: suggestion.height,
                memo: suggestion.memo,
            };
        });
    } catch (e) {
        console.warn(
            "AI Image Editor: could not work out what size the images should be, so it will offer no automatic size: " +
                (e instanceof Error ? e.message : String(e)),
        );
    }
}

// Opens the AI Image Editor overlay, with the image named by `target` (the one the user
// right-clicked, before the save reloaded the page frame) in its "Image to Edit" slot.
// Called from C# — via workspaceBundle.openAiImageEditor — once the page has been saved.
export function openAiImageEditor(target: IAiImageEditorTarget): void {
    post("aiImageEditor/launch", (r) => {
        const launchData = r.data as {
            editorUrl: string;
            httpBase: string;
            sessionToken: string;
            // Which language Bloom's UI is in ("en", "fr", "es-419"). Rides through the
            // `...launchData` spread below into the editor's init payload, so the name must
            // match what the editor reads.
            uiLanguageId: string;
            book: { id: string; title: string };
            bookImages?: Array<{
                id: string;
                src: string;
                pageLabel?: string;
                width?: number;
                height?: number;
                isPlaceholder?: boolean;
                // How much of its page this slot covers, as C# read it out of the book's
                // HTML. Normally always present, because launching this editor first puts the
                // book through the per-page pass when it needs it (BL-16852); null only if that
                // pass did not run or failed, and the slot then gets no "Auto" size.
                fractionOfPage?: { width: number; height: number } | null;
                // What size this slot would like its image to be, worked out below from
                // fractionOfPage, how big the pages of this book are, and the book's
                // digitalScreen limit. C# does no arithmetic here, because only a laid-out
                // browser page knows the page size. (fractionOfPage itself, like
                // digitalScreen, rides along to the editor in the ...launchData spread below;
                // the editor ignores fields it does not know.)
                suggestedTarget?: {
                    width: number;
                    height: number;
                    memo: string;
                };
            }>;
            // The screen a digital copy of this book is made for: the BloomPUB image limit
            // from Book Settings, which is what the publish step shrinks images to. Used for
            // the suggested targets above.
            digitalScreen: IDigitalScreen;
            references?: Array<{
                id: string;
                src: string;
                name?: string;
            }>;
            // Enumerated by C# from the per-book history folder; rides through
            // the `...launchData` spread into the AI Image Editor's init payload.
            history?: Array<{
                id: string;
                url: string;
                metadata?: Record<string, unknown> | null;
            }>;
            apiKey?: string | null;
            // Set for a Playground book: the AI Image Editor goes into "look-around"
            // mode -- every tool that would call OpenRouter is disabled, as is the
            // "set OpenRouter API key" UI. Rides through the `...launchData` spread
            // below into the AI Image Editor's init payload, so the name must match
            // what the editor reads.
            playgroundMode?: boolean;
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
        // "{pageId}:{ordinal}" id the AI Image Editor echoes back on commit. The host
        // applies replacements book-wide in C#, so there is no per-image DOM
        // id wrangling here anymore.

        // Identify the image the user right-clicked so the AI Image Editor can open with it
        // already in the "Image to Edit" slot. The page frame numbered the slot it was
        // clicked on, and C# builds each book image's id from the same numbering, so naming
        // the clicked one is just building that id.
        //
        // An empty placeholder slot is named like any other (BL-16744). It used to be
        // withheld, on the grounds that an empty slot has nothing to edit — but the AI
        // image editor answers a missing selectedBookImageId by targeting the FIRST image
        // of the book, which is normally the front cover. So withholding it aimed the user
        // at the cover when they had asked for an empty slot on some other page. The editor
        // reads isPlaceholder on the named slot and, for an empty one, puts nothing in its
        // "Image to Edit" panel and opens its "Create an Image" tool instead; it keeps the
        // slot so the created image can be committed straight into it. That behavior
        // arrived in bloom-ai-image-tools 0.1.6.
        const clickedId = target.pageId + ":" + target.slotIndex;
        const selectedBookImageId = (launchData.bookImages ?? []).some(
            (bi) => bi.id === clickedId,
        )
            ? clickedId
            : undefined;

        addSuggestedTargets(
            launchData.bookImages ?? [],
            launchData.digitalScreen,
        );

        const initPayload = {
            ...launchData,
            bookImages: launchData.bookImages ?? [],
            references: launchData.references ?? [],
            apiKey: launchData.apiKey ?? null,
            selectedBookImageId,
        };

        hostWindow.__bloomAiImageEditorCleanup?.();

        // Set by cleanup. Asking the DOM whether the overlay is still there would not do: a
        // relaunch tears this session down and immediately puts up a new overlay with the same id.
        let sessionEnded = false;

        const cleanup = () => {
            // Every way of ending the session lands here: a successful commit, the AI Image
            // Editor's own Cancel button, our close box, and a relaunch superseding this session.
            //
            // Idempotent, and it has to be: a commit sent by THIS session can be answered after the
            // user has closed it and opened the AI Image Editor again, and its success path calls
            // us. The overlay we would then tear down -- looked up by id, and the cleanup hook on
            // the window -- belong to the new session, so a second run would make the AI Image
            // Editor the user is looking at vanish, unclosably. (Pre-existing; deferring the
            // outcome to the commit
            // reply made it easier to reach.)
            if (sessionEnded) return;
            sessionEnded = true;
            hostWindow.removeEventListener("message", handleMessage);
            hostDocument.getElementById("ai-image-editor-overlay")?.remove();
            delete hostWindow.__bloomAiImageEditorCleanup;
        };

        const overlay = hostDocument.createElement("div");
        overlay.id = "ai-image-editor-overlay";
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
        // The close box asks the AI Image Editor to close rather than removing it: it reports the
        // end of the session to analytics and then sends "cancel", which is what closes the
        // overlay. Removing the iframe at once would lose that report. If no "cancel" arrives
        // (the AI Image Editor is hung, or never loaded), close anyway after a short wait.
        closeBtn.onclick = () => {
            try {
                iframe.contentWindow?.postMessage(
                    { channel: "bloom-ai-image-tools", type: "request-close" },
                    iframeUrl.origin,
                );
            } catch (e) {
                console.warn(`[AI Image Editor] could not request close: ${e}`);
            }
            hostWindow.setTimeout(cleanup, kCloseFallbackMs);
        };
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
                          // Note that this whole block is a type assertion on the
                          // message that arrived -- it describes the wire, and builds
                          // nothing. The `commit` case below forwards `replacements` to
                          // C# by reference, unchanged, which has two consequences worth
                          // knowing before touching either place:
                          //
                          //  - a field the AI Image Editor sends arrives at C# intact whether
                          //    or not it is declared here, so this list being incomplete
                          //    would break nothing today;
                          //  - it is nevertheless kept complete on purpose, because the
                          //    obvious-looking refactor -- rebuild the array field by
                          //    field on the way to C# -- can only carry the fields
                          //    someone thought to name, and would silently drop any this
                          //    type had not caught up with.
                          //
                          // "credits" below means the picture's ATTRIBUTION -- copyright
                          // notice, creator, license. It has nothing to do with the
                          // OpenRouter credits that costUSD and spentCredits report, in
                          // this same message type. Attribution being lost when a picture
                          // went through the AI Image Editor was BL-16603; that is why these
                          // fields exist and why they have to survive the trip.
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
                          // For the "analytics" message: an event the AI Image Editor wants
                          // recorded. Its own code guarantees no prompt text or other user
                          // content is in
                          // here -- see IBloomHostControl.trackEvent in bloom-ai-image-tools.
                          event?: string;
                          properties?: Record<
                              string,
                              string | number | boolean
                          >;
                          // For the "modal-open" message: whether the AI Image Editor
                          // now has a dialog of its own open.
                          open?: boolean;
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
                        // The AI Image Editor can be gone by the time we answer: the user is free
                        // to close the overlay while a commit is in flight, which detaches this
                        // iframe.
                        // Telling a window that no longer exists must not throw, because the work
                        // that follows this call still has to happen -- counting the pictures that
                        // landed, and closing the overlay.
                        // (Browsers null contentWindow on a detached frame, so the optional chain
                        // usually covers it; jsdom leaves it non-null and throws from inside
                        // postMessage, and that is a difference we should not be relying on.)
                        try {
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
                        } catch (e) {
                            console.warn(
                                `[AI Image Editor] could not acknowledge commit ${requestId}: ${e}`,
                            );
                        }
                    };

                    const replacements = data.payload?.replacements ?? [];
                    if (replacements.length === 0) {
                        ackEditor(false, "No replacements to apply.");
                        break;
                    }

                    // "Change Picture" is reported from here rather than from C# (AiImageEditorApi.HandleCommit, which
                    // has the matching comment) because this is the only side that ever learns
                    // whether the pictures on the page being edited really got swapped in. C# can
                    // only stage those and hand them back, so counting a staged one as applied
                    // overstated success in exactly the case the event exists to catch -- and in the
                    // ordinary case at that, since the picture the user right-clicked to open the
                    // AI Image Editor is by definition on the page they have open.
                    //
                    // offPageApplied comes from C#, which did those itself and knows;
                    // currentPageApplied is what the page frame says it managed.
                    //
                    // commitCounted exists because postJson chains .then(success).catch(error): if
                    // anything escapes the success callback, the error callback runs for the SAME
                    // request, and whatever either of them does would otherwise happen twice.
                    let commitCounted = false;
                    // Count each picture that reached the book the same way a pasted or
                    // chooser-chosen one is counted, so the source breakdown covers every route
                    // a picture can enter a book by. One per picture, as those routes do.
                    const noteCommitResult = (
                        offPageApplied: number,
                        currentPageApplied: number,
                    ) => {
                        if (commitCounted) return;
                        commitCounted = true;
                        const applied = offPageApplied + currentPageApplied;
                        for (let i = 0; i < applied; i++) {
                            trackChangePicture("ai-editor");
                        }
                    };

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
                            // actually landed. Combine both so the AI Image Editor's ack
                            // reflects the true outcome, and always ack (even when the
                            // apply fails) so its overlay can't hang.
                            let finalOk = false;
                            let message: string | undefined;
                            // Outside the try because the finally block reports it.
                            let currentPageApplied = 0;
                            try {
                                // Only involve the page frame when this commit actually has a
                                // swap for the page being edited. Asking for it unconditionally
                                // failed a wholly successful off-page commit whenever the frame
                                // happened to be mid-reload — reporting an error for images that
                                // had in fact been replaced and saved, and inviting a retry that
                                // would redo them and orphan the files.
                                const cp = (result?.results ?? []).some(
                                    isCurrentPageSwap,
                                )
                                    ? applyOnThePageBeingEdited(result?.results)
                                    : { applied: 0, expected: 0 };
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
                                // Deliberately NO save here. A current-page swap lives in
                                // the live page DOM only, like an image pasted or chosen
                                // from the gallery, and is saved the same way: by the
                                // normal page save when the user moves on. Saving now
                                // would reload the page frame, and the reload would
                                // discard the image undo the swap just registered — the
                                // whole reason ordinary image changes don't save either
                                // (BL-16330). Later sessions still read a fresh book DOM,
                                // because every launch saves first (HandleSaveThenLaunch);
                                // a retry from THIS still-open overlay reads stale oldSrc
                                // for the slots that landed, which the page frame handles
                                // by remembering the elements it already swapped (see
                                // applyAiImageEditorReplacements).
                                // Now, and only now, is the applied count a fact. Counted from
                                // C#'s own results for the other pages, plus what the page frame
                                // reported for this one (0 if we never got that far).
                                noteCommitResult(
                                    (result?.results ?? []).filter(
                                        (r) => r?.ok && !r.isCurrentPage,
                                    ).length,
                                    currentPageApplied,
                                );
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
                case "analytics": {
                    // The AI Image Editor has no analytics service of its own; it hands events to
                    // whatever host it is running in. C# adds BookId; branding is already on every
                    // event as "BrandingProjectName" (see AnalyticsApi).
                    //
                    // Forwarded exactly as sent; see this folder's AGENTS.md.
                    const event = data.payload?.event;
                    if (!event) break;
                    trackEvent(event, data.payload?.properties);
                    break;
                }
                case "modal-open":
                    // Our close button is drawn over the iframe, so the editor cannot
                    // cover it with a dialog of its own; two close buttons a few pixels
                    // apart invite shutting the whole editor when the user meant to
                    // shut the dialog. Hidden rather than dimmed, and pointer events go
                    // with it so it cannot be clicked while it is invisible.
                    closeBtn.style.visibility = data.payload?.open
                        ? "hidden"
                        : "visible";
                    closeBtn.style.pointerEvents = data.payload?.open
                        ? "none"
                        : "auto";
                    break;
                case "log":
                    console.log(
                        "[AI Image Editor:" +
                            (data.payload?.level ?? "info") +
                            "] " +
                            (data.payload?.message ?? ""),
                    );
                    break;
                case "saveCredentials":
                    // Bloom owns the OpenRouter API key. A key the user pastes into the
                    // AI Image Editor is handed up here so Bloom persists it per-user (and
                    // supplies it on the next launch). A null apiKey clears the stored key.
                    // The name must match ServiceKeyStore.kOpenRouterName.
                    postString(
                        "serviceKeys/key?name=OR",
                        data.payload?.apiKey ?? "",
                    );
                    break;
            }
        };

        hostWindow.addEventListener("message", handleMessage);
        hostWindow.__bloomAiImageEditorCleanup = cleanup;
        hostDocument.body.appendChild(overlay);
    });
}
