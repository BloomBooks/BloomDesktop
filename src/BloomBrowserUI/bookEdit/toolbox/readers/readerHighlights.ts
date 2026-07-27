import {
    makeRangeFromTextOffsets,
    mapVisibleText,
    TextHighlightManager,
    TextOffsetMap,
} from "../../js/textHighlightManager";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";

// The decodable and leveled reader tools mark up violations (a word that is not decodable at
// this stage, a sentence that is too long for this level, ...). They used to do that by wrapping
// the offending text in a styled span, which meant rewriting the innerHTML of a contenteditable
// on every keystroke. CKEditor responds to that by copying the span's computed background-color
// into a bare inline style of its own, which then gets saved into the book - permanently
// (BL-16558).
//
// So instead we paint the violations with ::highlight() pseudo-elements over live Ranges, the
// same technique the Talking Book tool uses (see textHighlightManager.ts). The DOM is never
// touched, so there is nothing for CKEditor to copy.

export const kSentenceTooLongHighlight = "bloom-reader-sentence-too-long";
export const kWordTooLongHighlight = "bloom-reader-word-too-long";
export const kWordNotDecodableHighlight = "bloom-reader-word-not-decodable";
export const kSightWordHighlight = "bloom-reader-sight-word";

// The class we put on our hover tip. bloom-ui means Bloom strips it when saving the page.
const kTipClass = "bloom-reader-highlight-tip";

interface ReaderHighlightLayer {
    name: string;
    // Where highlights overlap (a too-long word inside a too-long sentence) the higher priority
    // paints on top, which is how the old nested spans behaved.
    priority: number;
    // Text of the hover tip for this kind of violation, if it has one.
    tipL10nId?: string;
    tipEnglish?: string;
}

// Ordered so that the first layer whose range contains the mouse wins the tip; word-level
// violations are more specific than sentence-level ones, so they come first.
const readerHighlightLayers: ReaderHighlightLayer[] = [
    {
        name: kSightWordHighlight,
        priority: 3,
        tipL10nId: "EditTab.EditTab.Toolbox.DecodableReaderTool.SightWord",
        tipEnglish: "Sight Word",
    },
    {
        name: kWordNotDecodableHighlight,
        priority: 2,
        tipL10nId:
            "EditTab.EditTab.Toolbox.DecodableReaderTool.WordNotDecodable",
        tipEnglish: "This word is not decodable in this stage.",
    },
    {
        name: kWordTooLongHighlight,
        priority: 2,
    },
    {
        name: kSentenceTooLongHighlight,
        priority: 1,
        tipL10nId: "EditTab.EditTab.Toolbox.LeveledReaderTool.SentenceTooLong",
        tipEnglish: "This sentence is too long for this level.",
    },
];

const allReaderHighlightNames = readerHighlightLayers.map(
    (layer) => layer.name,
);

// True for elements inside a bloom-editable whose text the reader tools must not analyze:
// transient Bloom UI, CKEditor's hidden selection bookmarks, and the Talking Book tool's
// zero-width phrase markers. This replaces what removeAllHtmlMarkupFromString() used to strip
// out of the HTML string.
function shouldSkipElementForReaderText(element: Element): boolean {
    if (element.classList.contains("bloom-ui")) {
        return true;
    }
    if (element.id === "formatButton") {
        return true;
    }
    if (element.classList.contains("bloom-audio-split-marker")) {
        return true;
    }
    // CKEditor's bookmarks are hidden spans with ids like cke_bm_123S, holding a placeholder
    // character that would otherwise break a word in two.
    if (element.id.startsWith("cke_")) {
        return true;
    }
    const style = element.getAttribute("style");
    if (style && /display:\s*none/i.test(style)) {
        return true;
    }
    return false;
}

// Snapshot the text of an element as the reader sees it, ready for analysis and for turning the
// resulting character offsets back into Ranges.
export function mapReaderText(element: Node): TextOffsetMap {
    return mapVisibleText(element, shouldSkipElementForReaderText);
}

// A span of characters within a TextOffsetMap's text.
export interface TextSpan {
    start: number;
    end: number;
}

// Make the Ranges for a set of character spans, dropping any that turn out to cover no real text.
export function makeRangesForSpans(
    map: TextOffsetMap,
    spans: TextSpan[],
): Range[] {
    return spans
        .map((span) => makeRangeFromTextOffsets(map, span.start, span.end))
        .filter((range): range is Range => !!range);
}

// Narrow a span so it neither starts nor ends on whitespace. A sentence fragment can include the
// space that follows it, and we don't want the highlight to extend past the visible text.
export function trimSpan(text: string, span: TextSpan): TextSpan {
    let { start, end } = span;
    while (start < end && /\s/.test(text[start])) {
        start++;
    }
    while (end > start && /\s/.test(text[end - 1])) {
        end--;
    }
    return { start, end };
}

// Collects the Ranges found by one pass of the reader markup over the page's editable elements,
// registers them as ::highlight() layers, and answers hover tips for them.
class ReaderHighlightManager {
    private highlights = new TextHighlightManager(allReaderHighlightNames);

    // Ranges accumulated by the pass in progress, then the ones currently painted.
    private rangesInProgress = new Map<string, Range[]>();
    private paintedRanges = new Map<string, Range[]>();

    // The document we have attached the hover handler to, if any.
    private hoverDocument: Document | undefined;
    private tipElement: HTMLElement | undefined;

    // Start collecting the highlights for a new markup pass.
    public beginPass(): void {
        this.rangesInProgress = new Map<string, Range[]>();
    }

    // Add ranges found in one element to a layer of the pass in progress.
    public addRanges(highlightName: string, ranges: Range[]): void {
        if (ranges.length === 0) {
            return;
        }
        const existing = this.rangesInProgress.get(highlightName);
        if (existing) {
            existing.push(...ranges);
        } else {
            this.rangesInProgress.set(highlightName, [...ranges]);
        }
    }

    // Paint everything the pass found, replacing what was painted before. contextNode is any node
    // in the document being highlighted.
    public endPass(contextNode?: Node): void {
        if (!contextNode) {
            return;
        }

        this.paintedRanges = this.rangesInProgress;
        this.rangesInProgress = new Map<string, Range[]>();

        readerHighlightLayers.forEach((layer) => {
            this.highlights.setHighlight(
                layer.name,
                this.paintedRanges.get(layer.name) ?? [],
                contextNode,
                layer.priority,
            );
        });

        this.ensureHoverHandler(contextNode.ownerDocument ?? undefined);
    }

    // Remove all the reader highlights, e.g. when the user turns the tool off.
    public clearAll(contextNode?: Node): void {
        this.rangesInProgress = new Map<string, Range[]>();
        this.paintedRanges = new Map<string, Range[]>();
        this.highlights.clearAllManagedHighlights(contextNode);
        this.hideTip();
    }

    private ensureHoverHandler(pageDocument: Document | undefined): void {
        if (!pageDocument || this.hoverDocument === pageDocument) {
            return;
        }
        // A new page means a new document; the old one is being discarded along with its handler.
        this.hoverDocument = pageDocument;
        this.tipElement = undefined;
        pageDocument.addEventListener("mousemove", this.handleMouseMove);
        pageDocument.addEventListener("mouseleave", this.handleMouseLeave);
    }

    // Since there is no element to hang a tooltip on, we hit-test the mouse against the ranges we
    // painted and show our own tip. (This also fixes the tips for sight words and non-decodable
    // words, which have been throwing rather than appearing: the old code called qtip() on a raw
    // DOM element instead of on a jQuery wrapper.)
    private handleMouseMove = (event: MouseEvent): void => {
        const layer = this.layerAtPoint(event);
        if (!layer?.tipL10nId) {
            this.hideTip();
            return;
        }
        this.showTip(
            theOneLocalizationManager.getText(
                layer.tipL10nId,
                layer.tipEnglish ?? "",
            ),
            event,
        );
    };

    private handleMouseLeave = (): void => {
        this.hideTip();
    };

    private layerAtPoint(event: MouseEvent): ReaderHighlightLayer | undefined {
        const pageDocument = this.hoverDocument;
        if (!pageDocument) {
            return undefined;
        }
        const target = event.target as Element | null;
        if (!target?.closest?.(".bloom-editable")) {
            return undefined;
        }

        const caret = getCaretPosition(
            pageDocument,
            event.clientX,
            event.clientY,
        );
        if (!caret) {
            return undefined;
        }

        return readerHighlightLayers.find((layer) =>
            (this.paintedRanges.get(layer.name) ?? []).some((range) =>
                range.isPointInRange(caret.node, caret.offset),
            ),
        );
    }

    private showTip(text: string, event: MouseEvent): void {
        const pageDocument = this.hoverDocument;
        if (!pageDocument?.body || !text) {
            return;
        }
        if (!this.tipElement) {
            this.tipElement = pageDocument.createElement("div");
            // bloom-ui keeps it out of the saved page.
            this.tipElement.classList.add("bloom-ui", kTipClass);
            pageDocument.body.appendChild(this.tipElement);
        }
        this.tipElement.textContent = text;
        this.tipElement.style.left = `${event.pageX + 12}px`;
        this.tipElement.style.top = `${event.pageY + 16}px`;
        this.tipElement.style.display = "block";
    }

    private hideTip(): void {
        if (this.tipElement) {
            this.tipElement.style.display = "none";
        }
    }
}

// The document position under a point, using whichever of the two spellings the browser has.
function getCaretPosition(
    pageDocument: Document,
    x: number,
    y: number,
): { node: Node; offset: number } | undefined {
    const documentWithCaretApis = pageDocument as Document & {
        caretPositionFromPoint?: (
            x: number,
            y: number,
        ) => { offsetNode: Node; offset: number } | null;
        caretRangeFromPoint?: (x: number, y: number) => Range | null;
    };

    if (documentWithCaretApis.caretPositionFromPoint) {
        const position = documentWithCaretApis.caretPositionFromPoint(x, y);
        return position
            ? { node: position.offsetNode, offset: position.offset }
            : undefined;
    }
    if (documentWithCaretApis.caretRangeFromPoint) {
        const range = documentWithCaretApis.caretRangeFromPoint(x, y);
        return range
            ? { node: range.startContainer, offset: range.startOffset }
            : undefined;
    }
    return undefined;
}

export const theOneReaderHighlightManager = new ReaderHighlightManager();
