// Turns the markup of a bloom-editable into one string, plus a table that maps every
// offset in that string back to a DOM position. Everything here is pure: it reads the DOM
// and returns data, it never changes anything.

import { kOverflowStartClass } from "./flowConstants";

/** A place in the DOM that a Range can be set to: the same pair setStart() takes. */
export type BoundaryPoint = {
    container: Node;
    offset: number;
};

export type LinearizedContent = {
    text: string;
    /**
     * points[n] is where offset n of text sits in the DOM. The array is sparse: an offset
     * inside a multi-character grapheme has no entry.
     */
    points: BoundaryPoint[];
    /**
     * The offsets at which an overflow marker sits, in document order. The markers hold no
     * text, so nothing in points or text records them; this is the only place they show up.
     */
    markerOffsets: number[];
};

/** One top-level <p> of an editable, with its own offsets inside the editable's text. */
export type ParagraphSegment = {
    paragraph: HTMLParagraphElement;
    points: BoundaryPoint[];
    textLength: number;
    startOffset: number;
    endOffset: number;
    /** The offset of the first character of the next paragraph. */
    afterBreakOffset: number;
};

export type ParagraphBoundary = {
    segmentIndex: number;
    localOffset: number;
};

export function getTopLevelParagraphs(
    container: ParentNode,
): HTMLParagraphElement[] {
    return Array.from(container.childNodes).filter(
        (child) => child instanceof HTMLParagraphElement,
    ) as HTMLParagraphElement[];
}

export function getLastTopLevelParagraph(
    container: ParentNode,
): HTMLParagraphElement | undefined {
    const paragraphs = getTopLevelParagraphs(container);
    return paragraphs[paragraphs.length - 1];
}

export function linearizeEditable(editable: HTMLElement): LinearizedContent {
    const points: BoundaryPoint[] = [];
    const markerOffsets: number[] = [];
    let text = "";

    points[0] = { container: editable, offset: 0 };
    Array.from(editable.childNodes).forEach((child) => {
        walkNode(
            child,
            points,
            () => text,
            (value) => {
                text = value;
            },
            markerOffsets,
        );
    });

    points[text.length] = getEndBoundary(editable);
    return { text, points, markerOffsets };
}

export function linearizeParagraph(
    paragraph: HTMLParagraphElement,
): LinearizedContent {
    const points: BoundaryPoint[] = [];
    const markerOffsets: number[] = [];
    let text = "";

    points[0] = { container: paragraph, offset: 0 };
    Array.from(paragraph.childNodes).forEach((child) => {
        walkNode(
            child,
            points,
            () => text,
            (value) => {
                text = value;
            },
            markerOffsets,
        );
    });

    points[text.length] = getEndBoundary(paragraph);
    return { text, points, markerOffsets };
}

/**
 * Add the text of one node, and the DOM position of every offset in it, to the running
 * result. A <br> and a bloom-linebreak span each count as one newline; the end of a
 * paragraph counts as one newline unless the text already ends with one.
 */
export function walkNode(
    node: Node,
    points: BoundaryPoint[],
    getText: () => string,
    setText: (value: string) => void,
    markerOffsets?: number[],
): void {
    if (node.nodeType === Node.TEXT_NODE) {
        const textNode = node as Text;
        points[getText().length] = { container: textNode, offset: 0 };
        for (let index = 0; index < textNode.data.length; index++) {
            setText(getText() + textNode.data[index]);
            points[getText().length] = {
                container: textNode,
                offset: index + 1,
            };
        }
        return;
    }

    if (!(node instanceof HTMLElement)) {
        return;
    }

    if (isOverflowMarkerNode(node)) {
        // The marker is not text, so it adds no characters. Its offset goes in the side
        // list, because a later sibling text node overwrites the boundary point here.
        markerOffsets?.push(getText().length);
        points[getText().length] = getBoundaryAfterNode(node);
        return;
    }

    if (node.tagName === "BR" || node.classList.contains("bloom-linebreak")) {
        setText(getText() + "\n");
        points[getText().length] = getBoundaryAfterNode(node);
        return;
    }

    if (node.tagName === "P") {
        // The start of a paragraph is a place inside it. A paragraph with text overwrites
        // this with the start of that text; an empty one, such as the one Enter has just
        // made, keeps it, so that a caret put back here goes into the paragraph and what is
        // typed next goes in with it.
        points[getText().length] = { container: node, offset: 0 };
    }

    Array.from(node.childNodes).forEach((child) => {
        walkNode(child, points, getText, setText, markerOffsets);
    });

    if (node.tagName === "P") {
        appendLogicalBreak(
            points,
            getText,
            setText,
            getBoundaryAfterNode(node),
        );
    }
}

export function getEndBoundary(node: Node): BoundaryPoint {
    return { container: node, offset: node.childNodes.length };
}

export function getBoundaryAfterNode(node: Node): BoundaryPoint {
    const parent = node.parentNode;
    if (!parent) {
        return { container: node, offset: node.childNodes.length };
    }

    return {
        container: parent,
        offset: Array.from(parent.childNodes).indexOf(node) + 1,
    };
}

function appendLogicalBreak(
    points: BoundaryPoint[],
    getText: () => string,
    setText: (value: string) => void,
    boundary: BoundaryPoint,
): void {
    // The offset before the break keeps the point the paragraph's own content gave it, which
    // is inside the paragraph: a caret put back at the end of a paragraph's text belongs in
    // that paragraph, not between it and the next.
    if (!getText().endsWith("\n")) {
        setText(getText() + "\n");
    }
    points[getText().length] = boundary;
}

/**
 * The top-level paragraphs of the editable, each with the offset range it occupies in the
 * editable's linearized text. The paragraphs are the live elements, so a caller can extract
 * from them.
 */
export function getParagraphSegments(
    editable: HTMLElement,
): ParagraphSegment[] {
    let offset = 0;
    return getTopLevelParagraphs(editable).map((paragraph) => {
        const linearized = linearizeParagraph(paragraph);
        const startOffset = offset;
        const endOffset = startOffset + linearized.text.length;
        offset = endOffset + 1;
        return {
            paragraph,
            points: linearized.points,
            textLength: linearized.text.length,
            startOffset,
            endOffset,
            afterBreakOffset: offset,
        };
    });
}

export function resolveStartBoundary(
    segments: ParagraphSegment[],
    offset: number,
): ParagraphBoundary | undefined {
    for (let index = 0; index < segments.length; index++) {
        const segment = segments[index];
        if (offset < segment.endOffset) {
            return {
                segmentIndex: index,
                localOffset: offset - segment.startOffset,
            };
        }

        if (offset <= segment.afterBreakOffset) {
            // The offset is the paragraph break itself, so the extraction starts at the
            // beginning of the following paragraph.
            if (index + 1 < segments.length) {
                return { segmentIndex: index + 1, localOffset: 0 };
            }

            return undefined;
        }
    }

    return undefined;
}

export function resolveEndBoundary(
    segments: ParagraphSegment[],
    offset: number,
): ParagraphBoundary | undefined {
    for (let index = 0; index < segments.length; index++) {
        const segment = segments[index];
        if (offset < segment.endOffset) {
            return {
                segmentIndex: index,
                localOffset: offset - segment.startOffset,
            };
        }

        if (offset <= segment.afterBreakOffset) {
            return { segmentIndex: index, localOffset: segment.textLength };
        }
    }

    const lastSegment = segments[segments.length - 1];
    return lastSegment
        ? {
              segmentIndex: segments.length - 1,
              localOffset: lastSegment.textLength,
          }
        : undefined;
}

/**
 * The length to compare caret offsets against. The linearized text of an editable ends with
 * the break of its last paragraph, but the caret cannot go after that break.
 */
export function getComparableLinearizedLength(text: string): number {
    return text.endsWith("\n") ? text.length - 1 : text.length;
}

export function getComparableEditableLength(editable: HTMLElement): number {
    return getComparableLinearizedLength(linearizeEditable(editable).text);
}

export function isIgnorableNode(node: Node): boolean {
    if (node.nodeType === Node.COMMENT_NODE || isOverflowMarkerNode(node)) {
        return true;
    }

    if (node.nodeType !== Node.TEXT_NODE) {
        return false;
    }

    return !(node.textContent ?? "").trim();
}

/** The span OverflowChecker puts at the character where the box stops fitting. */
export function isOverflowMarkerNode(node: Node): boolean {
    return (
        node instanceof HTMLElement &&
        node.tagName === "SPAN" &&
        node.classList.contains(kOverflowStartClass)
    );
}

/**
 * The offset at which the last word of text begins, counting the whitespace that precedes
 * the word as part of what comes before it. With no whitespace at all the text is one word,
 * so the last grapheme is what the offset separates.
 */
export function findLastWordStart(text: string): number | undefined {
    const withoutTrailingSpace = text.replace(/\s+$/, "");
    if (!withoutTrailingSpace.length) {
        return undefined;
    }

    for (let index = withoutTrailingSpace.length - 1; index >= 0; index--) {
        if (/\s/.test(withoutTrailingSpace[index])) {
            return index + 1;
        }
    }

    return findLastGraphemeStart(withoutTrailingSpace);
}

/**
 * The offset at which the word after the one containing startOffset begins, counting the
 * whitespace between them as part of the earlier word. Undefined when no word follows.
 */
export function findNextWordStart(
    text: string,
    startOffset: number,
): number | undefined {
    let index = startOffset;
    while (index < text.length && !/\s/.test(text[index])) {
        index++;
    }

    while (index < text.length && /\s/.test(text[index])) {
        index++;
    }

    return index > startOffset && index < text.length ? index : undefined;
}

function findLastGraphemeStart(text: string): number | undefined {
    let lastStart: number | undefined = undefined;
    for (const part of new Intl.Segmenter(undefined, {
        granularity: "grapheme",
    }).segment(text)) {
        lastStart = part.index;
    }

    return lastStart;
}
