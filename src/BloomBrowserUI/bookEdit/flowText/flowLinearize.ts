// Turns the markup of a bloom-editable into one string, plus a table that maps every
// offset in that string back to a DOM position. Everything here is pure: it reads the DOM
// and returns data, it never changes anything.

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
        );
    });

    points[text.length] = getEndBoundary(editable);
    return { text, points };
}

export function linearizeParagraph(
    paragraph: HTMLParagraphElement,
): LinearizedContent {
    const points: BoundaryPoint[] = [];
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
        );
    });

    points[text.length] = getEndBoundary(paragraph);
    return { text, points };
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

    if (node.tagName === "BR" || node.classList.contains("bloom-linebreak")) {
        setText(getText() + "\n");
        points[getText().length] = getBoundaryAfterNode(node);
        return;
    }

    Array.from(node.childNodes).forEach((child) => {
        walkNode(child, points, getText, setText);
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
    points[getText().length] = boundary;
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
    if (node.nodeType === Node.COMMENT_NODE) {
        return true;
    }

    if (node.nodeType !== Node.TEXT_NODE) {
        return false;
    }

    return !(node.textContent ?? "").trim();
}
