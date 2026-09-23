// This manager paints text highlights using the CSS custom highlight registry
// (CSS.highlights) and ::highlight() pseudo-elements, instead of by wrapping the text in
// styled spans.
//
// In rare cases, the browser can automatically move computed css into an inline style within a
// contenteditable, which we suspect is causing BL-15300 (Talking Book highlighting gets stuck in
// the book) and BL-16558 (replacing a decodable-reader-marked word leaves a permanent
// background-color span). Highlighting without modifying the dom prevents that, and is the
// direction we want to move in for highlighting generally.
//
// The DOM still decides which pieces of text are eligible; only the visible paint comes from
// ::highlight pseudo-elements. Where the highlight classes are also meaningful outside the Edit
// Tab (as the Talking Book ones are, for Bloom Player), we continue to write those classes and
// their background-color rules so older consumers still work.
//
// Each client (the Talking Book tool, the decodable/leveled reader tools) owns an instance
// constructed with the set of highlight names it manages. Names are global to the document's
// registry, so they must be unique across clients, and each needs a matching ::highlight(name)
// rule in a stylesheet loaded into the document being highlighted.

type HighlightRegistry = Map<string, unknown>;
// A real Highlight is setlike over its Ranges and has a settable numeric priority.
type HighlightObject = Iterable<Range> & { priority?: number };
type HighlightConstructor = new (...ranges: Range[]) => HighlightObject;

function getDocumentWindow(contextNode: Node): Window | undefined {
    return contextNode.ownerDocument?.defaultView ?? undefined;
}

function getHighlightRegistry(
    contextNode: Node,
): HighlightRegistry | undefined {
    const docWindow = getDocumentWindow(contextNode) as
        | (Window & typeof globalThis)
        | undefined;
    const cssWithHighlights = docWindow?.CSS as
        | (typeof globalThis.CSS & {
              highlights?: HighlightRegistry;
          })
        | undefined;
    return cssWithHighlights?.highlights;
}

function getHighlightConstructor(
    contextNode: Node,
): HighlightConstructor | undefined {
    const docWindow = getDocumentWindow(contextNode) as
        | (Window & {
              Highlight?: HighlightConstructor;
          })
        | undefined;
    return docWindow?.Highlight;
}

// Make a Range covering all of node's content, or undefined if there is nothing to highlight.
export function makeRangeForNodeContents(node: Node): Range | undefined {
    if (node.textContent === null || node.textContent.length === 0) {
        return undefined;
    }

    const ownerDocument = node.ownerDocument;
    if (!ownerDocument) {
        console.error(
            "textHighlightManager.makeRangeForNodeContents() could not find ownerDocument for a highlighted node.",
        );
        return undefined;
    }

    const range = ownerDocument.createRange();
    range.selectNodeContents(node);
    return range;
}

// One run of characters within a TextOffsetMap's text. Most come from a real Text node; the
// rest are synthetic separators standing in for a visual break (a <br>, or the boundary of a
// block element) which contributes no characters of its own but does separate words.
interface TextPiece {
    start: number; // offset of this piece within the map's text
    length: number;
    node?: Text; // undefined for a synthetic separator
}

// A snapshot of the visible text of an element, together with what is needed to turn a pair of
// character offsets within that text back into a DOM Range. This is how a client can analyze
// text as a plain string - which is far easier to get right than analyzing HTML - and still
// highlight the result without touching the DOM.
export interface TextOffsetMap {
    text: string;
    pieces: TextPiece[];
}

// The character a visual break contributes to the mapped text. A newline, not a space, because
// a <br> ends a line and consumers must be able to tell that from an ordinary word gap: Bloom's
// sentence splitter counts a newline as paragraph-ending punctuation, so each line of a
// <br>-separated stanza is analyzed as its own sentence, exactly as it was when the reader tools
// fed raw HTML (where <br> became a paragraph-ending placeholder) to the splitter.
const kBreakSeparator = "\n";

// Tags whose boundaries separate words even though they contribute no characters. Compare
// removeAllHtmlMarkupFromString(), which substitutes a space for these in the HTML-string world.
const kSeparatingTags = new Set([
    "P",
    "DIV",
    "LI",
    "TR",
    "TD",
    "TH",
    "BLOCKQUOTE",
    "H1",
    "H2",
    "H3",
    "H4",
    "H5",
    "H6",
]);

// Walk root's descendants and build a TextOffsetMap of the text a reader actually sees.
// shouldSkipElement lets the caller exclude a subtree entirely (transient UI, editor bookmarks,
// invisible markers); its text is left out of both the string and the offset map.
export function mapVisibleText(
    root: Node,
    shouldSkipElement?: (element: Element) => boolean,
): TextOffsetMap {
    const pieces: TextPiece[] = [];
    let text = "";

    const addPiece = (content: string, node?: Text): void => {
        if (content.length === 0) {
            return;
        }
        pieces.push({ start: text.length, length: content.length, node });
        text += content;
    };

    const addSeparator = (): void => {
        // No point starting with a separator, or doubling one up. Note that we test for the
        // separator itself, not for whitespace: text that already ends in a space still needs
        // the break character, or a <br> after a space would stop ending the line.
        if (text.length > 0 && !text.endsWith(kBreakSeparator)) {
            addPiece(kBreakSeparator);
        }
    };

    const visitChildren = (parent: Node): void => {
        for (let child = parent.firstChild; child; child = child.nextSibling) {
            if (child.nodeType === Node.TEXT_NODE) {
                const textNode = child as Text;
                addPiece(textNode.data, textNode);
                continue;
            }
            if (child.nodeType !== Node.ELEMENT_NODE) {
                continue;
            }
            const element = child as Element;
            if (shouldSkipElement?.(element)) {
                continue;
            }
            if (element.tagName === "BR") {
                addSeparator();
                continue;
            }
            const isSeparating = kSeparatingTags.has(element.tagName);
            if (isSeparating) {
                addSeparator();
            }
            visitChildren(element);
            if (isSeparating) {
                addSeparator();
            }
        }
    };

    visitChildren(root);
    return { text, pieces };
}

// Find the DOM position for an offset in the map's text. Offsets that land on a synthetic
// separator have no DOM position of their own, so we snap to the nearest real text: forward for
// the start of a range, backward for its end. That way a range never begins or ends on a
// character that does not exist in the DOM.
function positionForOffset(
    map: TextOffsetMap,
    offset: number,
    isRangeStart: boolean,
): { node: Text; offset: number } | undefined {
    if (isRangeStart) {
        for (const piece of map.pieces) {
            if (piece.node && offset < piece.start + piece.length) {
                return {
                    node: piece.node,
                    offset: Math.max(0, offset - piece.start),
                };
            }
        }
        return undefined;
    }

    for (let i = map.pieces.length - 1; i >= 0; i--) {
        const piece = map.pieces[i];
        if (piece.node && offset > piece.start) {
            return {
                node: piece.node,
                offset: Math.min(piece.length, offset - piece.start),
            };
        }
    }
    return undefined;
}

// Make a Range covering [start, end) of the map's text, or undefined if that span contains no
// actual DOM text.
export function makeRangeFromTextOffsets(
    map: TextOffsetMap,
    start: number,
    end: number,
): Range | undefined {
    if (end <= start) {
        return undefined;
    }
    const startPosition = positionForOffset(map, start, true);
    const endPosition = positionForOffset(map, end, false);
    if (!startPosition || !endPosition) {
        return undefined;
    }

    const ownerDocument = startPosition.node.ownerDocument;
    if (!ownerDocument) {
        console.error(
            "textHighlightManager.makeRangeFromTextOffsets() could not find ownerDocument for a highlighted node.",
        );
        return undefined;
    }

    const range = ownerDocument.createRange();
    range.setStart(startPosition.node, startPosition.offset);
    range.setEnd(endPosition.node, endPosition.offset);
    // Snapping can put the end before the start if the whole span was synthetic separators.
    return range.collapsed ? undefined : range;
}

export class TextHighlightManager {
    // The highlight names this instance owns. clearAllManagedHighlights() removes exactly these.
    private readonly managedHighlightNames: readonly string[];

    public constructor(managedHighlightNames: readonly string[]) {
        this.managedHighlightNames = managedHighlightNames;
    }

    // Returns true if the document containing contextNode supports the custom highlight API,
    // so it is worth computing ranges at all. Test environments may not provide it.
    public canHighlight(contextNode: Node): boolean {
        return (
            !!getHighlightRegistry(contextNode) &&
            !!getHighlightConstructor(contextNode)
        );
    }

    // Paint the given ranges under the given highlight name, replacing whatever that name was
    // painting before. An empty list of ranges removes the highlight altogether.
    // Where highlights can overlap, a higher priority wins; the default is 0.
    public setHighlight(
        name: string,
        ranges: Range[],
        contextNode: Node,
        priority?: number,
    ): void {
        const registry = getHighlightRegistry(contextNode);
        const Highlight = getHighlightConstructor(contextNode);
        if (!registry || !Highlight) {
            return;
        }

        if (ranges.length === 0) {
            registry.delete(name);
            return;
        }

        const highlight = new Highlight(...ranges);
        if (priority !== undefined) {
            highlight.priority = priority;
        }
        registry.set(name, highlight);
    }

    // Remove the named highlights from the registry for the document containing contextNode.
    public clearHighlights(names: readonly string[], contextNode?: Node): void {
        if (!contextNode) {
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        if (!registry) {
            return;
        }

        names.forEach((name) => registry.delete(name));
    }

    // Remove all of this instance's highlights from the registry for the document containing
    // contextNode.
    public clearAllManagedHighlights(contextNode?: Node): void {
        this.clearHighlights(this.managedHighlightNames, contextNode);
    }

    // Returns true if the named entry in the registry for contextNode's document exists but any
    // of its Ranges no longer cover live content, so the highlight is still registered but paints
    // nothing (BL-15300). That happens two ways when page-setup code rewrites the DOM under the
    // highlight:
    // - the range's node itself was replaced (e.g. CKEditor's initialization replaces the
    //   paragraph): the range points at a detached node;
    // - an ANCESTOR of the range's node was removed (e.g. re-appending the bloom-editables to
    //   reorder them): per the DOM spec the live Range is then COLLAPSED onto the
    //   still-connected former parent, so a connectedness check alone would miss it.
    public hasDeadRanges(name: string, contextNode?: Node): boolean {
        if (!contextNode) {
            return false;
        }
        const registry = getHighlightRegistry(contextNode);
        if (!registry) {
            return false;
        }
        // A real (Chromium) Highlight is setlike over its Ranges, so we can iterate it. Test
        // environments may register a non-iterable stand-in; treat those as healthy.
        const highlight = registry.get(name) as HighlightObject | undefined;
        if (!highlight || typeof highlight[Symbol.iterator] !== "function") {
            return false;
        }
        for (const range of highlight) {
            if (
                range.collapsed ||
                !range.startContainer.isConnected ||
                range.startContainer.ownerDocument !== contextNode.ownerDocument
            ) {
                return true;
            }
        }
        return false;
    }
}
