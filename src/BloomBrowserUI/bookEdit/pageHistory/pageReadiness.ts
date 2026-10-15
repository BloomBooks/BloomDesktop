// When the page that has just loaded is ready for its undo history (docs/undo/MINIMUM.md item 6).
// Until then Bloom is still setting the page up, and everything it changes is part of the page as
// the user found it, not a step the user can undo: CKEditor writes `<p><br></p>` into an empty box
// once its editor is ready, image sizing and other load-time work runs inside page content delays,
// and the toolbox's tools run newPageReady twice, the second time 600 ms after the first (the
// background picture conversion of BL-16870, the Games tool's shuffle).

/** The waits that pageReadiness cannot do on its own, because they live in other modules. */
export interface PageReadinessWaits {
    /** Resolves when no page content delay (wrapWithRequestPageContentDelay) is pending. */
    noPageContentDelays(): Promise<void>;
    /** Resolves when the toolbox has run its newPageReady passes for this page, if it runs any. */
    toolboxPassesHaveRun(): Promise<void>;
}

/** The part of a CKEditor 4 editor that this module uses. */
interface CkEditorInstance {
    status?: string;
    instanceReady?: boolean;
    once(event: string, callback: () => void): void;
}

/**
 * Resolves when the page in pageWindow is ready: the window has loaded, every CKEditor instance
 * has fired instanceReady, no page content delay is pending, and the toolbox has run its
 * newPageReady passes. CKEditor and the delays are checked again after the toolbox, whose passes
 * can start both.
 */
export async function whenPageIsReady(
    pageWindow: Window,
    waits: PageReadinessWaits,
): Promise<void> {
    await whenWindowHasLoaded(pageWindow);
    await whenEveryCkEditorIsReady(pageWindow);
    await waits.noPageContentDelays();
    await waits.toolboxPassesHaveRun();
    await whenEveryCkEditorIsReady(pageWindow);
    await waits.noPageContentDelays();
}

function whenWindowHasLoaded(pageWindow: Window): Promise<void> {
    if (pageWindow.document.readyState === "complete") return Promise.resolve();
    return new Promise((resolve) =>
        pageWindow.addEventListener("load", () => resolve(), { once: true }),
    );
}

/**
 * Resolves when every CKEditor instance in pageWindow is ready, including any that are made while
 * this waits. With no CKEditor on the page (a page with no text boxes, or a unit test), at once.
 */
export async function whenEveryCkEditorIsReady(
    pageWindow: Window,
): Promise<void> {
    const ckeditor = (
        pageWindow as Window & {
            CKEDITOR?: { instances: Record<string, CkEditorInstance> };
        }
    ).CKEDITOR;
    if (!ckeditor) return;
    for (;;) {
        const notReady = Object.values(ckeditor.instances).find(
            (instance) => !isReady(instance),
        );
        if (!notReady) return;
        // An editor destroyed before it was ready never fires instanceReady.
        await new Promise<void>((resolve) => {
            notReady.once("instanceReady", () => resolve());
            notReady.once("destroy", () => resolve());
        });
    }
}

/**
 * CKEditor 4 sets status to "ready" just before it fires instanceReady; Bloom's own waits
 * (toolbox.ts, StyleEditor.ts) read instanceReady instead, so either counts. A destroyed editor
 * will never be ready, and has nothing more to write.
 */
function isReady(instance: CkEditorInstance): boolean {
    return (
        instance.status === "ready" ||
        instance.status === "destroyed" ||
        !!instance.instanceReady
    );
}
