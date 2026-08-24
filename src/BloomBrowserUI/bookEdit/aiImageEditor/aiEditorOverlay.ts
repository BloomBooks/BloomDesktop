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
// ai-editor is a SEPARATE web app (the `bloom-ai-image-tools` package); we do not
// import it — we load it into an <iframe> overlay. The flow:
//   0. The menu command (aiEditorPageCommands.launchAiImageEditor, in the page frame)
//      POSTs aiImageEditor/saveThenLaunch. C# saves the page being edited — which the
//      whole-book image list below depends on, and which reloads the page frame — and then
//      calls openAiImageEditor() here. See HandleSaveThenLaunch.
//   1. POST aiImageEditor/launch -> C# mints a session, makes the per-book
//      .ai-image-editor folder, and returns the ai-editor URL + the whole-book image
//      list + enumerated history + httpBase/sessionToken.
//   2. Build a fixed overlay <div id="ai-editor-overlay"> holding an <iframe> at
//      that URL with ?mode=bloom-iframe.
//   3. Handshake over window.postMessage on channel "bloom-ai-image-tools": the
//      ai-editor posts `ready`; we post `init` (the launch reply + the right-clicked
//      image as selectedBookImageId). Image bytes never ride postMessage — they go
//      over HTTP via aiImageEditor/file; the ai-editor references results by id.
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

// The analytics events the ai-editor may ask us to send, mapping the name IT uses to the name we
// record. Bloom is what actually posts to Segment, and a name it does not recognize would create a
// new event type in our data rather than land in an existing one, so the vocabulary is pinned on
// this side.
//
// The rename is why this is a map and not just a list: our events say "AI Image Editor" because
// one day there may be an AI editor for text, or video, or games, and "AI Editor Generate" would
// then be ambiguous. Doing the translation here rather than in the ai-editor means Bloom's
// vocabulary is Bloom's business, and a package release is not needed to change it.
//
// Their PROPERTIES are not filtered. We control both ends of this channel, so an allow-list of
// property names would only be guarding against ourselves; if something specific ever must not be
// forwarded, the place to stop it is in the ai-editor, or by removing that one property here.
// Today it sends nothing but ids, enums, numbers and booleans -- no free-form text of any kind.
//
// A Map, not an object literal: with an object, `event in obj` is also true for inherited members,
// so an event named "toString" or "constructor" would be treated as permitted. Map.get() only sees
// real entries.
const kAnalyticsEventsTheAiEditorMaySend = new Map<string, string>([
    ["AI Editor Generate", "AI Image Editor Generate"],
]);

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
            // the `...launchData` spread into the ai-editor's init payload.
            history?: Array<{
                id: string;
                url: string;
                metadata?: Record<string, unknown> | null;
            }>;
            apiKey?: string | null;
            // Playground/demo context: the ai-editor must disable its
            // "set OpenRouter API key" UI. Rides through the `...launchData`
            // spread below into the ai-editor's init payload.
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
        // "{pageId}:{ordinal}" id the ai-editor echoes back on commit. The host
        // applies replacements book-wide in C#, so there is no per-image DOM
        // id wrangling here anymore.

        // Identify the image the user right-clicked so the ai-editor can open with it
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

        // ----- Analytics for this ai-editor session (BL-16716) -----
        // Generation happens inside the ai-editor app, which reports each attempt to us over the
        // bridge (the "analytics" message below); we count those so that a session the user
        // abandons can say how much AI work was thrown away. That is the clearest signal we
        // have that the output was not good enough -- much better than a count of generations,
        // which goes up whether people liked what they got or not.
        //
        // ONE event per session, "AI Image Editor Closed", sent when the session settles. It says
        // what the session achieved, and an appliedCount of zero IS the cancel -- which is why
        // there is no separate cancel event to keep in step with this one.
        //
        // "Settles" means the overlay has gone AND no commit is still outstanding, which is why
        // the counts below accumulate rather than being reported as each reply arrives. Reporting
        // per reply would be wrong in both directions: a session with two commits, one that fails
        // and one that succeeds, would be recorded by whichever answered first -- so a session
        // whose pictures did land could be filed as one that threw everything away. Waiting costs
        // us a session that is never closed at all (Bloom quit with the overlay still up), which
        // is the rarer and less misleading loss.
        let generationsThisSession = 0;
        // What every commit in this session added up to. failedCount is derived from the first two.
        let replacementsAttempted = 0;
        let picturesApplied = 0;
        let picturesGenerated = 0;
        let picturesReused = 0;
        let closedReported = false;
        // How many commits we have sent and not yet had an answer to. Reporting the session while
        // any is outstanding must not happen: the pictures may be moments from being saved. A count
        // rather than a flag, because the ai-editor is free to send a second commit before the
        // first is answered, and the first reply would then clear a flag while the second was still
        // in the air.
        let commitsInFlight = 0;
        // Set by cleanup. Asking the DOM whether the overlay is still there would not do: a
        // relaunch tears this session down and immediately puts up a new overlay with the same id.
        let sessionEnded = false;

        // Report how this session turned out. Safe -- and expected -- to call from anywhere that
        // might have settled the last thing we were waiting for: it does nothing until both
        // conditions hold, and nothing ever again once it has reported.
        //
        // Both conditions are tested HERE rather than at the call sites, because every caller
        // needs them and one of them is easy to get wrong: a reply arriving for commit A must not
        // report while commit B is still in the air. Each reply decrements the count before calling
        // us, so whichever settles last is the one that reports.
        const reportClosed = () => {
            if (closedReported || !sessionEnded || commitsInFlight > 0) return;
            closedReported = true;
            // Did anything actually reach the book? A non-zero failedCount is exactly the class of
            // bug BL-16702 was: a commit that silently did nothing. Generated versus reused says
            // whether people are paying for new pictures or re-using ones they already have. And
            // generatedThisSession against a zero appliedCount is how much AI work was thrown away
            // -- the clearest signal we have that the output was not good enough, and much better
            // than a count of generations, which goes up whether people liked what they got or not.
            trackEvent("AI Image Editor Closed", {
                replacementCount: replacementsAttempted,
                appliedCount: picturesApplied,
                failedCount: replacementsAttempted - picturesApplied,
                generatedCount: picturesGenerated,
                reusedCount: picturesReused,
                generatedThisSession: generationsThisSession,
                historyCount: (launchData.history ?? []).length,
            });
        };

        const cleanup = () => {
            // Every way of ending the session without committing lands here: the ai-editor's own
            // Cancel button, our close box, and a relaunch superseding this session.
            //
            // Except one: closing while a commit is still in flight. The overlay goes away
            // immediately, but the pictures may well be saved a moment later, so this is not yet
            // the moment to say what the session achieved. reportClosed declines while a commit is
            // outstanding; the last reply to arrive is what reports.
            //
            // Idempotent, and it has to be: a commit sent by THIS session can be answered after the
            // user has closed it and opened the ai-editor again, and its success path calls us. The
            // overlay we would then tear down -- looked up by id, and the cleanup hook on the
            // window -- belong to the new session, so a second run would make the ai-editor the user
            // is looking at vanish, unclosably. (Pre-existing; deferring the outcome to the commit
            // reply made it easier to reach.)
            if (sessionEnded) return;
            sessionEnded = true;
            reportClosed();
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
                          // Note that this whole block is a type assertion on the
                          // message that arrived -- it describes the wire, and builds
                          // nothing. The `commit` case below forwards `replacements` to
                          // C# by reference, unchanged, which has two consequences worth
                          // knowing before touching either place:
                          //
                          //  - a field the ai-editor sends arrives at C# intact whether
                          //    or not it is declared here, so this list being incomplete
                          //    would break nothing today;
                          //  - it is nevertheless kept complete on purpose, because the
                          //    obvious-looking refactor -- rebuild the array field by
                          //    field on the way to C# -- can only carry the fields
                          //    someone thought to name, and would silently drop any this
                          //    type had not caught up with.
                          //
                          // This side itself reads only resultId and sourceUrl, to say
                          // how many pictures were generated rather than reused (see
                          // noteCommitResult).
                          //
                          // "credits" below means the picture's ATTRIBUTION -- copyright
                          // notice, creator, license. It has nothing to do with the
                          // OpenRouter credits that costUSD and spentCredits report, in
                          // this same message type. Attribution being lost when a picture
                          // went through the ai-editor was BL-16603; that is why these
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
                          // For the "analytics" message: an event the ai-editor wants recorded.
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
                        // The ai-editor can be gone by the time we answer: the user is free to close
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
                    // ai-editor is by definition on the page they have open.
                    //
                    // offPageApplied comes from C#, which did those itself and knows;
                    // currentPageApplied is what the page frame says it managed.
                    // Both of these exist because postJson chains .then(success).catch(error): if
                    // anything escapes the success callback, the error callback runs for the SAME
                    // request, and everything either of them does would otherwise happen twice.
                    // Counting twice would put one commit's pictures into the session totals and
                    // into the picture-source breakdown twice over; decrementing twice would leave
                    // commitsInFlight at -1, after which "no commit outstanding" is never true
                    // again and the session could never be reported at all.
                    let commitCounted = false;
                    let commitSettled = false;
                    const noteCommitSettled = () => {
                        if (commitSettled) return;
                        commitSettled = true;
                        commitsInFlight--;
                    };
                    // Add what this commit achieved to the session totals. It does not send
                    // anything: reportClosed sends one event for the whole session (see there).
                    const noteCommitResult = (
                        offPageApplied: number,
                        currentPageApplied: number,
                    ) => {
                        if (commitCounted) return;
                        commitCounted = true;
                        const applied = offPageApplied + currentPageApplied;
                        replacementsAttempted += replacements.length;
                        picturesApplied += applied;
                        // Deliberately counted over every replacement the ai-editor sent, not only
                        // the ones that landed -- so generatedCount and reusedCount are NOT
                        // comparable with appliedCount, and do not sum to it when a swap fails.
                        // They answer a different question: what the user chose, and therefore what
                        // they paid OpenRouter for, which is true whether or not the picture then
                        // made it into the book. appliedCount and failedCount are the pair that
                        // says what landed.
                        picturesGenerated += replacements.filter(
                            (r) => !!r?.resultId,
                        ).length;
                        picturesReused += replacements.filter(
                            (r) => !r?.resultId && !!r?.sourceUrl,
                        ).length;
                        // Count each picture that reached the book the same way a pasted or
                        // chooser-chosen one is counted, so the source breakdown covers every route
                        // a picture can enter a book by. One per picture, as those routes do, and
                        // reported as it happens rather than at session end because these are
                        // per-picture facts and nothing about them is still pending.
                        for (let i = 0; i < applied; i++) {
                            trackChangePicture("ai-editor");
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
                            // actually landed. Combine both so the ai-editor's ack
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
                                noteCommitResult(
                                    (result?.results ?? []).filter(
                                        (r) => r?.ok && !r.isCurrentPage,
                                    ).length,
                                    currentPageApplied,
                                );
                                if (finalOk) {
                                    cleanup();
                                }
                                // Unconditionally, and after cleanup rather than instead of it.
                                // cleanup() reports when IT is what ends the session, but it
                                // short-circuits when the session has already ended -- which is
                                // exactly the case where this reply is the last thing anyone was
                                // waiting for, so leaving the report to cleanup would lose a
                                // session whose pictures did land. A partial failure, meanwhile,
                                // leaves the overlay up, and this correctly does nothing until the
                                // user closes it.
                                reportClosed();
                            }
                        },
                        () => {
                            noteCommitSettled();
                            // The request failed, so we know nothing landed as far as anyone can
                            // tell -- which is also what the ai-editor is about to tell the user.
                            // Counting the attempt matters more than the small chance that C#
                            // did the work and only the reply went missing: a commit that reaches
                            // nobody is the failure this event was added to make visible, and it
                            // shows up as replacementCount without appliedCount.
                            noteCommitResult(0, 0);
                            ackEditor(false, "Failed to apply replacements.");
                            reportClosed();
                        },
                    );
                    break;
                }
                case "analytics": {
                    // The ai-editor has no analytics service of its own; it hands events to
                    // whatever host it is running in. C# adds BookId; branding is already on every
                    // event as "BrandingProjectName" (see AnalyticsApi).
                    //
                    // Known event names only, so an unrecognized one cannot invent a new event type
                    // in our data, and each is recorded under Bloom's own name for it. Their
                    // properties are passed through as sent -- see the comment on
                    // kAnalyticsEventsTheAiEditorMaySend.
                    const event = data.payload?.event;
                    const ourNameForIt = event
                        ? kAnalyticsEventsTheAiEditorMaySend.get(event)
                        : undefined;
                    if (!ourNameForIt) {
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
                    trackEvent(ourNameForIt, data.payload?.properties);
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
                    // Bloom owns the OpenRouter API key. A key the user pastes into the
                    // ai-editor is handed up here so Bloom persists it per-user (and
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
