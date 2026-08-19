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

import {
    post,
    postJson,
    postThatMightNavigate,
    trackChangePicture,
    trackEvent,
} from "../../utils/bloomApi";
import { getEditablePageBundleExports } from "../js/workspaceFrames";
import {
    fileNameOf,
    IAiImageEditorApplyOutcome,
    IAiImageEditorCommitResult,
    IAiImageEditorTarget,
    isCurrentPageSwap,
} from "./aiEditorShared";

// The analytics the editor iframe is allowed to send us: event names, and for each the exact
// property names that may ride along. Both halves are pinned on Bloom's side of the bridge,
// because Bloom is what actually posts to Segment -- see the "analytics" case below. Adding
// anything here is a deliberate act, which is the point: the editor's promise not to include
// prompt text is a promise made in another repository, and a property allow-list is what makes
// it true here regardless.
// A Map, not an object literal: with an object, `event in obj` is also true for inherited
// members, so an event named "toString" or "constructor" would be treated as permitted and then
// blow up while its properties were filtered -- taking down the handling of that message, which
// is the same handler that processes commit and cancel. Map.has() only sees real entries.
const kAnalyticsTheEditorMaySend = new Map<string, string[]>([
    [
        "AI Editor Generate",
        [
            "tool",
            "model",
            "sourceKind",
            "referenceCount",
            "batch",
            "runsLocally",
            "attemptNumber",
            "result",
            "durationSeconds",
            "costUSD",
            "spentCredits",
        ],
    ],
]);

// Keep only the properties that event is allowed to carry, dropping anything unrecognized.
function allowedProperties(
    event: string,
    properties?: Record<string, string | number | boolean>,
): Record<string, string | number | boolean> {
    const allowed = kAnalyticsTheEditorMaySend.get(event) ?? [];
    const result: Record<string, string | number | boolean> = {};
    for (const name of allowed) {
        if (properties && name in properties) {
            result[name] = properties[name];
        }
    }
    return result;
}

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

// Opens the AI Image Editor overlay, with the image named by `target` (the one the user
// right-clicked, before the save reloaded the page frame) in its "Image to Edit" slot.
// Called from C# — via workspaceBundle.openAiImageEditor — once the page has been saved.
export function openAiImageEditor(target: IAiImageEditorTarget): void {
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

        // ----- Analytics for this editor session (BL-16716) -----
        // Generation happens inside the editor app, which reports each attempt to us over the
        // bridge (the "analytics" message below); we count those so that a session the user
        // abandons can say how much AI work was thrown away. That is the clearest signal we
        // have that the output was not good enough -- much better than a count of generations,
        // which goes up whether people liked what they got or not.
        // The matching "AI Editor Open"/"Commit" events come from C# (AiImageEditorApi), which
        // has the book, the key and the history to hand.
        let generationsThisSession = 0;
        let commitSucceeded = false;
        // How many commits we have sent and not yet had an answer to. Closing the overlay while any
        // is outstanding must not report the session as thrown away: the images may be moments from
        // being saved. A count rather than a flag, because the editor is free to send a second
        // commit before the first is answered, and the first reply would then clear a flag while the
        // second was still in the air.
        let commitsInFlight = 0;
        let cancelReported = false;
        // Set by cleanup. Asking the DOM whether the overlay is still there would not do: a
        // relaunch tears this session down and immediately puts up a new overlay with the same id.
        let sessionEnded = false;
        // Whether any picture from this session reached the book. Not the same as
        // commitSucceeded, which means every replacement in a commit worked: a commit can put one
        // picture in the book and fail on another, and that session is not one whose AI work was
        // thrown away, however it ends afterwards.
        let anythingReachedTheBook = false;

        // The session ended with nothing kept. Called from cleanup, and again from each commit
        // reply, since a commit outstanding at the moment the user closed is what decides.
        //
        // The outstanding-commit test lives HERE rather than at the call sites, because every
        // caller needs it and one of them is easy to get wrong: a reply arriving for commit A must
        // not report a cancel while commit B is still in the air, or a session whose pictures do
        // land ends up in both the cancel and the commit figures. Each reply decrements the count
        // before calling us, so whichever one settles last is the one that reports.
        const reportCancel = () => {
            if (
                cancelReported ||
                commitSucceeded ||
                anythingReachedTheBook ||
                commitsInFlight > 0
            )
                return;
            cancelReported = true;
            trackEvent("AI Editor Cancel", {
                generatedThisSession: generationsThisSession,
                historyCount: (launchData.history ?? []).length,
            });
        };

        const cleanup = () => {
            // Every way of ending the session without committing lands here: the editor's own
            // Cancel button, our close box, and a relaunch superseding this session.
            //
            // Except one: closing while a commit is still in flight. The overlay goes away
            // immediately, but the pictures may well be saved a moment later -- and reporting a
            // cancel now would count that session as thrown-away work AND as a commit, inflating
            // the very number this event exists to provide. reportCancel declines in that case;
            // the last commit reply to arrive is what reports.
            //
            // Idempotent, and it has to be: a commit sent by THIS session can be answered after the
            // user has closed it and opened the editor again, and its success path calls us. The
            // overlay we would then tear down -- looked up by id, and the cleanup hook on the
            // window -- belong to the new session, so a second run would make the editor the user
            // is looking at vanish, unclosably. (Pre-existing; deferring the cancel decision to the
            // commit reply made it easier to reach.)
            if (sessionEnded) return;
            sessionEnded = true;
            reportCancel();
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
                          // image editor sends is declared here even though this side
                          // reads only resultId and sourceUrl (to say how many pictures
                          // were generated rather than reused — see reportCommit).
                          // Rebuilding the array field by field instead of passing it
                          // through would silently drop `credits`, and the result would
                          // lose its credits — the bug this whole feature exists to
                          // prevent.
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
                          // For the "analytics" message: an event the editor wants recorded.
                          // Its own code guarantees no prompt text or other user content is in
                          // here -- see IBloomHostControl.trackEvent in bloom-ai-image-tools.
                          event?: string;
                          properties?: Record<
                              string,
                              string | number | boolean
                          >;
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
                        // The editor can be gone by the time we answer: the user is free to close
                        // the overlay while a commit is in flight, which detaches this iframe.
                        // Telling a window that no longer exists must not throw, because the work
                        // that follows this call still has to happen -- saving the page the swaps
                        // landed on, and deciding whether the session ended with nothing kept.
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

                    // Reported from here rather than from C# (AiImageEditorApi.HandleCommit, which
                    // has the matching comment) because this is the only side that ever learns
                    // whether the pictures on the page being edited really got swapped in. C# can
                    // only stage those and hand them back, so counting a staged one as applied
                    // overstated success in exactly the case the event exists to catch -- and in the
                    // ordinary case at that, since the picture the user right-clicked to open the
                    // editor is by definition on the page they have open.
                    //
                    // offPageApplied comes from C#, which did those itself and knows;
                    // currentPageApplied is what the page frame says it managed.
                    // Both of these exist because postJson chains .then(success).catch(error): if
                    // anything escapes the success callback, the error callback runs for the SAME
                    // request, and everything either of them does would otherwise happen twice.
                    // Reporting twice would count one commit as two, and its pictures twice in the
                    // picture-source breakdown; decrementing twice would leave commitsInFlight at -1,
                    // after which "no commit outstanding" is never true again and the session could
                    // never be reported as abandoned.
                    let commitReported = false;
                    let commitSettled = false;
                    const noteCommitSettled = () => {
                        if (commitSettled) return;
                        commitSettled = true;
                        commitsInFlight--;
                    };
                    const reportCommit = (
                        offPageApplied: number,
                        currentPageApplied: number,
                    ) => {
                        if (commitReported) return;
                        commitReported = true;
                        const applied = offPageApplied + currentPageApplied;
                        if (applied > 0) anythingReachedTheBook = true;
                        // Does anything actually reach the book? A non-zero failed rate is exactly
                        // the class of bug BL-16702 was: a commit that silently did nothing.
                        // Generated vs reused says whether people are paying for new images or
                        // re-using ones they already have.
                        trackEvent("AI Editor Commit", {
                            replacementCount: replacements.length,
                            appliedCount: applied,
                            failedCount: replacements.length - applied,
                            generatedCount: replacements.filter(
                                (r) => !!r?.resultId,
                            ).length,
                            reusedCount: replacements.filter(
                                (r) => !r?.resultId && !!r?.sourceUrl,
                            ).length,
                        });
                        // Also count each picture that reached the book the same way a pasted or
                        // chooser-chosen one is counted, so the source breakdown covers every route
                        // a picture can enter a book by. One per picture, as those routes do.
                        for (let i = 0; i < applied; i++) {
                            trackChangePicture("AI editor", "ai-editor");
                        }
                    };

                    commitsInFlight++;
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
                                noteCommitSettled();
                                // Now, and only now, is the applied count a fact. Counted from
                                // C#'s own results for the other pages, plus what the page frame
                                // reported for this one (0 if we never got that far).
                                reportCommit(
                                    (result?.results ?? []).filter(
                                        (r) => r?.ok && !r.isCurrentPage,
                                    ).length,
                                    currentPageApplied,
                                );
                                if (finalOk) {
                                    commitSucceeded = true;
                                    cleanup();
                                } else if (sessionEnded) {
                                    // The session was closed while this commit was in flight, so
                                    // cleanup deferred the decision to us -- and the commit did not
                                    // work out. It really did end with nothing kept.
                                    reportCancel();
                                }
                            }
                        },
                        () => {
                            noteCommitSettled();
                            // The request failed, so we know nothing landed as far as anyone can
                            // tell -- which is also what the editor is about to tell the user.
                            // Reporting the attempt matters more than the small chance that C#
                            // did the work and only the reply went missing: a commit that reaches
                            // nobody is the failure this event was added to make visible.
                            reportCommit(0, 0);
                            ackEditor(false, "Failed to apply replacements.");
                            if (sessionEnded) {
                                // As above: closed while in flight, and the request itself failed.
                                reportCancel();
                            }
                        },
                    );
                    break;
                }
                case "analytics": {
                    // The editor has no analytics service of its own; it hands events to
                    // whatever host it is running in. C# adds BookId and branding.
                    //
                    // Only known event names, carrying only known properties, are forwarded.
                    // Bloom is the party that actually sends to Segment, so Bloom enforces its
                    // own privacy line rather than trusting a sibling repository not to regress:
                    // without this, a change over there could push an arbitrary event name, or a
                    // new property holding prompt text, straight out of here.
                    const event = data.payload?.event;
                    if (!event || !kAnalyticsTheEditorMaySend.has(event)) {
                        if (event) {
                            console.warn(
                                `[AI Image Editor] not forwarding unrecognized analytics event "${event}"`,
                            );
                        }
                        break;
                    }
                    if (event === "AI Editor Generate") {
                        generationsThisSession++;
                    }
                    trackEvent(
                        event,
                        allowedProperties(event, data.payload?.properties),
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
}
