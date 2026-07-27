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
