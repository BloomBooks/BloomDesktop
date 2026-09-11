// What Bloom knows about refits of the pages the browser is not editing that are waiting to be
// run, and the two things the user can say about them.
//
// Text never moves across pages on its own: a request to refit a chain from this page on is
// recorded as pending (FlowTextApi pendingWalks), and it runs when the user asks for it or when
// they change pages, if they have said that is what they want.
//
// These calls deliberately do NOT go through flowBoundaryClient's serialized queue. Nothing here
// reads or writes a text box, so none of them can be undone by, or undo, a move of text; and a
// status read that waited behind a page's worth of text moving would put the bubble up long after
// the state it describes. The one call that must be ordered against the moves is reflowNow, and
// its caller waits for the page to finish its own work before making it.

import { getAsync, postJsonAsync } from "../../utils/bloomApi";

/** The refits Bloom is holding, and whether it has been told to run them on a page change. */
export interface IPendingWalks {
    pending: boolean;
    /** The chains, by chain id, that have a refit waiting. */
    chainIds: string[];
    /**
     * Whether changing pages runs the waiting refits. Undefined when the reply does not carry
     * it, in which case the caller asks for it on its own.
     */
    reflowOnPageChange?: boolean;
    /**
     * Whether a refit may add and remove pages. Undefined when the reply does not carry it, in
     * which case the caller asks for it on its own.
     */
    autoPages?: boolean;
}

/**
 * The refits Bloom is holding. Undefined when Bloom could not be asked, which leaves what the
 * page is already showing alone rather than claiming there is nothing waiting.
 */
export async function getPendingWalks(): Promise<IPendingWalks | undefined> {
    try {
        const response = await getAsync("flowText/pendingWalks");
        const data = response?.data as IPendingWalks | undefined;
        if (!data) {
            return undefined;
        }

        return {
            pending: !!data.pending,
            chainIds: data.chainIds ?? [],
            reflowOnPageChange: data.reflowOnPageChange,
            autoPages: data.autoPages,
        };
    } catch {
        return undefined;
    }
}

/**
 * Run the refits Bloom is holding, now. Bloom puts its own progress dialog up for as long as
 * the work takes, so there is nothing for the browser to show.
 */
export async function postReflowNow(): Promise<void> {
    await postJsonAsync("flowText/reflowNow", {});
}

/** Whether changing pages runs the refits Bloom is holding. */
export async function getReflowOnPageChange(): Promise<boolean> {
    const response = await getAsync("flowText/reflowOnPageChange");
    return readBoolean(response?.data);
}

/** Say whether changing pages should run the refits Bloom is holding. */
export async function postReflowOnPageChange(value: boolean): Promise<void> {
    await postJsonAsync("flowText/reflowOnPageChange", { value });
}

/** Whether a refit may add the pages the run needs and remove the ones it leaves empty. */
export async function getAutoPages(): Promise<boolean> {
    const response = await getAsync("flowText/autoPages");
    return readBoolean(response?.data);
}

/** Say whether a refit may add and remove pages. */
export async function postAutoPages(value: boolean): Promise<void> {
    await postJsonAsync("flowText/autoPages", { value });
}

/**
 * The boolean in a reply that is either the boolean itself or an object wrapping it. Bloom's
 * boolean endpoints answer both ways, depending on which of its helpers wrote the endpoint.
 */
function readBoolean(data: unknown): boolean {
    if (typeof data === "boolean") {
        return data;
    }

    if (data && typeof data === "object" && "value" in data) {
        return !!(data as { value: unknown }).value;
    }

    return false;
}

/** One box of the page being edited whose content a refit changed. */
export interface IRefitBox {
    chainId: string;
    /** The language whose box this is; a group holds one box per language. */
    lang: string;
    /**
     * Where the box's group comes among the page's translation groups that are not inside a
     * bloom-canvas, counting from zero. C# names a group the same way (FlowTextChains.GetGroupAt).
     */
    indexInPage: number;
    html: string;
}

/**
 * The boxes of the page being edited that the refit rewrote. A refit runs off-screen and saves
 * the pages it changes, but it does not reload the editor, so the browser is what brings the page
 * on screen up to date.
 *
 * Bloom clears the result as it hands it over, so this is the only copy of it: the caller has to
 * put what it gets into the page.
 */
export async function getRefitResult(pageId: string): Promise<IRefitBox[]> {
    const response = await getAsync(
        `flowText/refitResult?pageId=${encodeURIComponent(pageId)}`,
    );
    const data = response?.data as { boxes?: IRefitBox[] } | undefined;
    return data?.boxes ?? [];
}
