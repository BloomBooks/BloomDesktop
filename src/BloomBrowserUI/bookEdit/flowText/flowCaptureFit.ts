// Fitting one box of a chain in a browser that nobody is looking at.
//
// C# walks a chain page by page off-screen (FlowTextWalk) so that a run of text is refitted
// everywhere it goes without the user visiting each page. For each box it puts the whole of the
// run that is left into that box, loads the page into a throwaway browser, and calls the
// function here. We measure the real layout, divide the text where it stops fitting, and stash
// the two halves for C# to poll, exactly the way captureContentForExternalProcessing stashes a
// captured page.
//
// The division is the same one settleCrossPageBoundary makes for the page being edited
// (splitCombinedAcrossPages), so a box refitted off-screen holds what it would hold if the user
// had opened its page.

import { waitForRequestPageContentDelays } from "../js/bloomEditing";
import { snapToWordStart } from "./flowCrossPage";
import {
    getCombinedTextAcrossPages,
    normalizeChainedEditable,
    splitCombinedAcrossPages,
} from "./flowDomMove";
import { placeOverflowMarker } from "./flowOverflowMarker";
import { getPretextMeasurer } from "./flowPretextMeasurer";

// The class that says a box holds ordinary flowing text. C# looks for it on the box itself
// (FlowTextChains.GetFlowEditable), so the two sides must pick out the same box.
const kNormalStyleClass = "normal-style";

/** The two halves of a box's text: what fits in it, and what has to go on to the next box. */
export interface IFlowFitResult {
    /** The new content of this box. */
    head: string;
    /**
     * The content for the next box of the chain, its first paragraph carrying the continuation
     * markers when this box's last paragraph was divided. Empty when all of the text fits here.
     */
    tail: string;
    /**
     * The style attribute this page left on the box's translation group, which carries the font
     * size the editor works out from the box (SetupThingsSensitiveToStyleChanges). A page
     * thumbnail is drawn in the page list's own document, where that inline size is the only word
     * on how big the text is, so C# copies it back onto the page it saves.
     *
     * captureFlowFit fills this in; divideBoxAtFit, which answers only about the text, does not.
     */
    groupStyle?: string;
}

/**
 * Divide the text of one box of a chain at the character where it stops fitting, and stash the
 * result on window.__bloomFlowFit for the C# caller to poll: the JSON of an IFlowFitResult, or
 * a string beginning "ERROR:".
 *
 * groupIndex counts the page's translation groups that are not inside a bloom-canvas, in
 * document order, which is how C# names a group (FlowTextChains.GetFlowGroupsOfPage).
 *
 * The last box of a chain has nowhere to send text, so isLastBox keeps everything here and
 * leaves the mark that says where the text stopped fitting, which is what makes the page
 * complain and what a box added later offers to continue.
 */
export function captureFlowFit(
    groupIndex: number,
    lang: string,
    isLastBox: boolean,
): void {
    window.__bloomFlowFit = undefined;
    void runCapture(groupIndex, lang, isLastBox);
}

async function runCapture(
    groupIndex: number,
    lang: string,
    isLastBox: boolean,
): Promise<void> {
    try {
        // Which character falls on which line depends on the font that actually draws it, and
        // on anything still resizing the page, so both have to settle before we measure.
        await document.fonts?.ready?.catch?.(() => undefined);
        await waitForRequestPageContentDelays();

        const editable = findFlowEditable(groupIndex, lang);
        // The text arrives here as C# wrote it, which is not how the editor would leave it, and
        // where a character falls is measured against the paragraphs the box really has.
        normalizeChainedEditable(editable);
        const markerOffset = placeOverflowMarker(
            editable,
            getPretextMeasurer(),
        );
        if (isLastBox || markerOffset === undefined) {
            // Either nothing follows this box, or there is no character inside the text at
            // which its share ends: all of the text fits, or not even one word does. Each way
            // the box keeps everything, with whatever mark placeOverflowMarker just settled.
            stash({ head: editable.innerHTML, tail: "" }, editable);
            return;
        }

        stash(divideBoxAtFit(editable, markerOffset), editable);
    } catch (error) {
        const problem = error as Error;
        window.__bloomFlowFit =
            "ERROR: " + problem?.message + "\n" + problem?.stack;
    }
}

/**
 * Divide this box's content at the character where its text stops fitting, which is where the
 * mark sits. Text moves a whole word at a time, as it does on the page being edited, so the
 * division is at the start of the word the mark falls in.
 *
 * Throws when the two halves are not the box's text: they are about to be written to two
 * different pages, so a word dropped or repeated here is dropped or repeated in the book.
 */
export function divideBoxAtFit(
    editable: HTMLElement,
    markerOffset: number,
): IFlowFitResult {
    const text = getCombinedTextAcrossPages(editable, "");
    const split = splitCombinedAcrossPages(
        editable,
        "",
        snapToWordStart(text, markerOffset),
    );
    const before = comparableWords(text);
    const after =
        comparableWords(textOfHtml(split.currentHtml)) +
        " " +
        comparableWords(textOfHtml(split.nextHtml));
    if (collapse(after) !== collapse(before)) {
        throw new Error(
            "captureFlowFit: dividing the box changed its text. It held " +
                `${before.length} characters and the two halves hold ${after.length}.`,
        );
    }

    return { head: split.currentHtml, tail: split.nextHtml };
}

/** The words of a piece of text, with the characters a reader never sees left out. */
function comparableWords(text: string): string {
    return collapse(text.replace(/[​‌]/g, ""));
}

function collapse(text: string): string {
    return text.replace(/\s+/g, " ").trim();
}

/** The text of a fragment of HTML, its paragraphs separated the way linearized text has them. */
function textOfHtml(html: string): string {
    const holder = document.createElement("div");
    holder.innerHTML = html;
    const paragraphs = Array.from(holder.querySelectorAll("p"));
    if (!paragraphs.length) {
        return holder.textContent ?? "";
    }
    return paragraphs.map((paragraph) => paragraph.textContent ?? "").join(" ");
}

function stash(result: IFlowFitResult, editable: HTMLElement): void {
    const group = editable.closest<HTMLElement>(".bloom-translationGroup");
    window.__bloomFlowFit = JSON.stringify({
        ...result,
        groupStyle: group?.getAttribute("style") ?? "",
    });
}

/**
 * The box of this language in the group at this place in the page. Throws rather than reporting
 * nothing: C# asked for a box it read out of the same page, so a box that is not there means
 * the two sides disagree about the page, and that has to be visible.
 */
function findFlowEditable(groupIndex: number, lang: string): HTMLElement {
    const page = document.querySelector<HTMLElement>(".bloom-page");
    if (!page) {
        throw new Error("captureFlowFit: this document holds no page.");
    }

    const groups = Array.from(
        page.querySelectorAll<HTMLElement>(".bloom-translationGroup"),
    ).filter((group) => !group.closest(".bloom-canvas"));
    const group = groups[groupIndex];
    if (!group) {
        throw new Error(
            `captureFlowFit: the page has ${groups.length} translation groups, so there is ` +
                `none at ${groupIndex}.`,
        );
    }

    const editable = group.querySelector<HTMLElement>(
        `:scope > .bloom-editable.${kNormalStyleClass}[lang="${lang}"]`,
    );
    if (!editable) {
        throw new Error(
            `captureFlowFit: group ${groupIndex} has no ${kNormalStyleClass} box for "${lang}".`,
        );
    }

    return editable;
}
