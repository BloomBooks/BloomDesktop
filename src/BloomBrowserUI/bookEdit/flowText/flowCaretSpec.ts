import { beforeEach, describe, expect, it } from "vitest";
import {
    getCollapsedSelectionOffsetInEditable,
    restoreCollapsedSelectionInChain,
    restoreCollapsedSelectionInEditable,
} from "./flowCaret";

const putCaretAt = (container: Node, offset: number): void => {
    const range = document.createRange();
    range.setStart(container, offset);
    range.collapse(true);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
};

const getEditable = (): HTMLElement =>
    document.querySelector<HTMLElement>(".bloom-editable")!;

const getParagraphs = (): HTMLParagraphElement[] =>
    Array.from(getEditable().querySelectorAll("p"));

/** The paragraph the caret is in, as an index, or -1 when it is not in one. */
const getCaretParagraphIndex = (): number => {
    const selection = window.getSelection()!;
    return getParagraphs().findIndex(
        (paragraph) =>
            paragraph === selection.anchorNode ||
            paragraph.contains(selection.anchorNode!),
    );
};

describe("keeping the caret through a rewrite of the box", () => {
    beforeEach(() => {
        document.body.innerHTML =
            '<div class="bloom-editable" contenteditable="true">' +
            "<p>aaa bbb</p><p>ccc ddd</p><p><br></p><p>eee fff</p></div>";
    });

    it("puts the caret back inside the empty paragraph that Enter has just made", () => {
        const paragraphs = getParagraphs();
        putCaretAt(paragraphs[2], 0);
        expect(getCaretParagraphIndex()).toBe(2);

        const offset = getCollapsedSelectionOffsetInEditable(getEditable());
        expect(offset).toBe("aaa bbb\nccc ddd\n".length);

        restoreCollapsedSelectionInEditable(getEditable(), offset!);
        expect(getCaretParagraphIndex()).toBe(2);
    });

    it("puts the caret back at the end of a paragraph's text, inside that paragraph", () => {
        const paragraphs = getParagraphs();
        const textOfSecond = paragraphs[1].firstChild as Text;
        putCaretAt(textOfSecond, textOfSecond.data.length);

        const offset = getCollapsedSelectionOffsetInEditable(getEditable());
        expect(offset).toBe("aaa bbb\nccc ddd".length);

        restoreCollapsedSelectionInEditable(getEditable(), offset!);
        expect(getCaretParagraphIndex()).toBe(1);
        const selection = window.getSelection()!;
        expect(selection.anchorNode).toBe(textOfSecond);
        expect(selection.anchorOffset).toBe(textOfSecond.data.length);
    });

    it("puts the caret back at the start of a paragraph's text", () => {
        const paragraphs = getParagraphs();
        const textOfLast = paragraphs[3].firstChild as Text;
        putCaretAt(textOfLast, 0);

        const offset = getCollapsedSelectionOffsetInEditable(getEditable());
        expect(offset).toBe("aaa bbb\nccc ddd\n\n".length);

        restoreCollapsedSelectionInEditable(getEditable(), offset!);
        expect(getCaretParagraphIndex()).toBe(3);
        expect(window.getSelection()!.anchorOffset).toBe(0);
    });
});

describe("keeping the caret through a move between the boxes of a chain", () => {
    const makeChain = (): HTMLElement[] => {
        document.body.innerHTML =
            '<div class="bloom-editable" contenteditable="true"><p>aaa bbb</p></div>' +
            '<div class="bloom-editable" contenteditable="true">' +
            '<p data-flow-continuation="true" data-flow-seam-space="true">ccc ddd</p></div>';
        return Array.from(
            document.querySelectorAll<HTMLElement>(".bloom-editable"),
        );
    };

    it("puts the caret at the end of the last box when the offset runs past the chain's text", () => {
        const chain = makeChain();
        // The chain holds "aaa bbb" and "ccc ddd", 14 characters. The caret was after 15, with
        // the space at the seam still a character of the text.
        restoreCollapsedSelectionInChain(chain, { offset: 15 });

        const selection = window.getSelection()!;
        const textOfLast = chain[1].querySelector("p")!.firstChild as Text;
        expect(chain[1].contains(selection.anchorNode)).toBe(true);
        expect(selection.anchorNode).toBe(textOfLast);
        expect(selection.anchorOffset).toBe(textOfLast.data.length);
    });

    it("counts paragraph breaks when it puts the caret into a later box", () => {
        const chain = makeChain();
        chain[1].innerHTML =
            '<p data-flow-continuation="true">ccc</p><p><br></p><p>ddd</p>';
        // 7 for the first box, then "ccc" and its break: the start of the empty paragraph.
        restoreCollapsedSelectionInChain(chain, { offset: 7 + 4 });

        const selection = window.getSelection()!;
        expect(selection.anchorNode).toBe(chain[1].querySelectorAll("p")[1]);
        expect(selection.anchorOffset).toBe(0);
    });
});
