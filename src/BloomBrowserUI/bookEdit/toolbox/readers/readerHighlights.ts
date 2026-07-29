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

        // Only listen for hovers while there is actually something to explain.
        if (this.paintedRanges.size > 0) {
            this.ensureHoverHandler(contextNode.ownerDocument ?? undefined);
        } else {
            this.detachHoverHandler();
        }
    }

    // Remove all the reader highlights, e.g. when the user turns the tool off.
    public clearAll(contextNode?: Node): void {
        this.rangesInProgress = new Map<string, Range[]>();
        this.paintedRanges = new Map<string, Range[]>();
        this.highlights.clearAllManagedHighlights(contextNode);
        this.detachHoverHandler();
    }

    private ensureHoverHandler(pageDocument: Document | undefined): void {
        if (!pageDocument || this.hoverDocument === pageDocument) {
            return;
        }
        // A new page means a new document; the old one is being discarded along with its handler.
        this.detachHoverHandler();
        this.hoverDocument = pageDocument;
        pageDocument.addEventListener("mousemove", this.handleMouseMove);
        pageDocument.addEventListener("mouseleave", this.handleMouseLeave);
    }

    // Stop listening, and take our tip out of the page. There is nothing to hover over once the
    // highlights are gone, and leaving the handler attached would mean doing work on every mouse
    // move for the rest of the page's life. endPass() attaches it again when there is.
    private detachHoverHandler(): void {
        this.hideTip();
        this.tipElement?.remove();
        this.tipElement = undefined;
        if (!this.hoverDocument) {
            return;
        }
        this.hoverDocument.removeEventListener(
            "mousemove",
            this.handleMouseMove,
        );
        this.hoverDocument.removeEventListener(
            "mouseleave",
            this.handleMouseLeave,
        );
        this.hoverDocument = undefined;
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

    // The layer whose tip should show for this mouse position, if any. Only layers that actually
    // have a tip are considered: a too-long word sits inside a too-long sentence, and the word
    // layer has nothing to say, so letting it match first would hide the sentence's explanation.
    private layerAtPoint(event: MouseEvent): ReaderHighlightLayer | undefined {
        const pageDocument = this.hoverDocument;
        if (!pageDocument) {
            return undefined;
        }
        // Cheapest possible exit first. The listeners stay attached for the life of the page
        // document, so with the tool turned off (or on a page with no violations) this runs on
        // every mouse move and must not reach getCaretPosition(), which forces a layout.
        const layersWithTips = readerHighlightLayers.filter(
            (layer) =>
                layer.tipL10nId && this.paintedRanges.get(layer.name)?.length,
        );
        if (layersWithTips.length === 0) {
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

        return layersWithTips.find((layer) =>
            (this.paintedRanges.get(layer.name) ?? []).some(
                (range) =>
                    // Ranges whose content has been replaced since we painted are not under the
                    // mouse, and asking isPointInRange about one whose root differs from the
                    // caret's throws WrongDocumentError, so screen them out first.
                    range.startContainer.isConnected &&
                    range.startContainer.ownerDocument ===
                        caret.node.ownerDocument &&
                    // The caret test is the cheap one, and narrows us to a single candidate
                    // range; then we confirm the mouse really is over the text it covers.
                    range.isPointInRange(caret.node, caret.offset) &&
                    isPointOverRange(range, event.clientX, event.clientY),
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

// Is the point actually inside the area the range's text occupies?
// We have to ask, because caretPositionFromPoint() answers with the NEAREST text position even
// when the point is nowhere near any text. Hovering the white space below the last paragraph, for
// instance, gets you a position at the end of the last line, so a tip would appear or not
// depending on whether that line happened to be highlighted. Exported for testing: a Range's
// client rects need real layout, which the unit tests don't have.
export function isPointOverRange(range: Range, x: number, y: number): boolean {
    // A range spanning more than one line has one rect per line.
    return Array.from(range.getClientRects()).some(
        (rect) =>
            x >= rect.left &&
            x <= rect.right &&
            y >= rect.top &&
            y <= rect.bottom,
    );
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
