import { kBackgroundConversionDelayId } from "../toolbox/canvas/canvasElementConstants";

// How long the off-screen page capture (captureContentForExternalProcessing in bloomEditing.ts,
// driven by C# BookProcessor for "Update Book" and BloomBridge's process-book) waits for a page's
// in-flight async fix-ups to settle before it acts. This is a background job with nobody waiting at
// the keyboard, so it can afford far more patience than the live editor's 4 s. It must stay well
// below BookProcessor.kReadyTimeoutMs, the C# side's cap on polling for our answer.
export const kExternalCaptureMaxWaitMs = 20000;

// Decide what the off-screen capture does when it has waited kExternalCaptureMaxWaitMs and some
// requestPageContent delays are still active. Most delays guard work whose partial state is merely
// stale (a tooltip fetch, a size adjustment); capturing anyway is the right call for those. The
// conversion of an old-style background image is different: a page captured while it is pending
// carries both the old picture and a hidden replacement, and that saved page's picture can neither
// be cropped nor deleted (BL-16870). So for that one delay we refuse: return the ERROR text the C#
// caller recognises (BookProcessor.ProcessOnePage throws on an "ERROR:" prefix), which fails the
// page, and with it the whole all-or-nothing run, instead of saving a broken page. Returns undefined
// when capturing anyway is acceptable.
export function externalCaptureErrorForPendingWork(
    activeDelays: readonly string[],
): string | undefined {
    if (!activeDelays.includes(kBackgroundConversionDelayId)) {
        return undefined;
    }
    return (
        `ERROR: the page's background image was still being converted to a canvas element after ` +
        `${kExternalCaptureMaxWaitMs}ms (its image had not finished loading), so the page was not ` +
        `captured: saving it now would leave a picture that can neither be cropped nor deleted. ` +
        `Active delays: [${activeDelays.join(", ")}]`
    );
}
