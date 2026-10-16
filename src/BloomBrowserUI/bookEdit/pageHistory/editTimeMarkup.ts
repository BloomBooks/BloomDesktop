// What editing adds inside a .bloom-page that a save does not keep, as declarative rules, and the
// page history's filter built from them (docs/undo/APPROACHES.md section 3.7, docs/undo/MINIMUM.md
// item 2). The history must neither record nor put back such markup, and it must record
// everything that a save keeps, so each rule here mirrors a step of the save path:
// extractAndStripPageContentForSave in bookEdit/js/bloomEditing.ts, the functions it calls, and
// HtmlDom.ProcessPageAfterEditing in C#. Each rule names the step it mirrors. A rule that drops
// something the save keeps would lose content on undo, so a change to the save path must be
// matched here.
//
// The save path does not use this module yet: on master it strips the live page and the page
// reloads, so the history never meets what it strips. These rules are written so that the save
// can share them when it stops reloading.

import { kSelectorForPotentialNiceScrollElements } from "bloom-player";
import type { PageHistoryFilter } from "./pageHistoryTypes";

/** Elements that a save removes with everything inside them. */
const kEditTimeElements = [
    // C#: ProcessPageAfterEditing removes both.
    ".bloom-ui",
    ".ui-resizable-handle",
    // bloom-table's stripEditTimeMarkup, from removeTableEditingArtifacts.
    "[data-table-overlay]",
    // CanvasElementManager.turnOffCanvasElementEditing: Comical.stopEditing and
    // removeOrphanedDrawingSurfaces take out Comical's drawing surfaces.
    "canvas.comical-generated",
    // bloom-player's cleanupNiceScroll removes niceScroll's rails and cursors.
    ".nicescroll-rails",
    ".nicescroll-cursors",
    // The Impairment Visualizer's detachFromPage, run by removeToolboxMarkup.
    ".ui-cbOverlay",
    // removeEditingDebris; these labels are also .bloom-ui.
    ".marginBox .textBox-identifier",
    // CKEditor's bookmarks, paste bin and hidden selection container, which getData() leaves out
    // (doCkEditorCleanup).
    "[data-cke-bookmark]",
    "#cke_pastebin",
    "[data-cke-hidden-sel]",
].join(", ");

/**
 * Elements that a save keeps but that undo leaves alone. The save draws Comical's SVG again from
 * paper.js, which onAfterUndoOrRedo rebuilds from data-bubble.
 */
const kElementsIgnoredByUndoOnly = "svg.comical-generated";

/** Class tokens that a save removes wherever they are. */
const kEditTimeClasses = new Set([
    // bloom-table's stripEditTimeMarkup, on .bloom-cell and .bloom-table there.
    "cell--selected",
    "bloom-pulse-fill",
    "bloom-pulse-border",
    "table--selected",
    "bloom-pointer-near",
    "bloom-current-table",
    // removeEditingDebris, on .marginBox there.
    "origami-layout-mode",
    // turnOffCanvasElementEditing: removeFocusClass and EnableAllImageEditing. The Motion tool's
    // detachFromPage also removes bloom-hideImageButtons.
    "bloom-focusedCanvasElement",
    "bloom-hideImageButtons",
    // cleanupNiceScroll, on niceScroll's elements there.
    "scrolling-bubble",
    // The reader tools' detachFromPage (setMarkupType(0)), run by removeToolboxMarkup.
    "page-too-many-words-or-sentences",
]);

/**
 * Class tokens that a save keeps but that undo leaves alone, because Bloom works them out again
 * from what undo puts back. OverflowChecker works out the first four from the content each time
 * the content changes, on the boxes, the elements that contain an overflowing box, and the
 * .bloom-page (pageOverflows). Layout-Problem-Detected is one that OverflowChecker only removes,
 * left by an earlier version of Bloom.
 */
const kClassesIgnoredByUndoOnly = new Set([
    "overflow",
    "thisOverflowingParent",
    "childOverflowingThis",
    "pageOverflows",
    "Layout-Problem-Detected",
    // On an img whose src failed to load: the img's error handler adds it and its load handler
    // takes it off (HandleImageError in bloomImages.ts, or the onerror attribute that C# gives a
    // cover image), whenever the browser finishes loading a src, which is after the change
    // that set the src, outside any transaction. Undo putting back a src makes the browser load
    // it, which adds or removes the class again. C# does not copy it back from the data-div.
    "bloom-imageLoadError",
]);

/** C#: RemoveCkEditorMarkup removes every class token that starts with this. */
const kCkEditorClassPrefix = "cke_";

/**
 * niceScroll takes an alignment class off a translation group while it scrolls the group's text,
 * and leaves a token naming it; cleanupNiceScroll puts the alignment class back. A save keeps the
 * alignment class, so the history treats each such token as the class it names.
 */
const kAlignmentClassesRenamedByNiceScroll = new Map([
    ["bloom-vertical-align-center-removed", "bloom-vertical-align-center"],
    ["bloom-vertical-align-bottom-removed", "bloom-vertical-align-bottom"],
]);

/** bloom-table's stripEditTimeMarkup removes these inline style properties wherever they are. */
const kEditTimeStyleProperties = new Set([
    "--hint-top-color",
    "--hint-right-color",
    "--hint-bottom-color",
    "--hint-left-color",
    "anchor-name",
]);

/**
 * cleanupNiceScroll clears overflow, outline and width from every element that niceScroll may
 * scroll (kSelectorForPotentialNiceScrollElements), whether or not niceScroll is on it. Clearing
 * the overflow and outline shorthands clears their longhands too.
 */
const kNiceScrollStyleProperties = new Set([
    "overflow",
    "overflow-x",
    "overflow-y",
    "outline",
    "outline-color",
    "outline-style",
    "outline-width",
    "width",
]);

/** The query parameter that bloomVideo.ts adds to a video's src so that the browser reloads it. */
const kVideoTimestampParam = "bloomVideoTransientTimestamp";

/** CKEditor's filling character, U+200B ZERO WIDTH SPACE. */
const kFillingChar = "\u200B";

/** The custom data key under which CKEditor keeps the text node holding its filling character. */
const kFillingCharKey = "cke-fillingChar";

/** True for an element that a save removes, or that undo leaves alone, with what is inside it. */
export function isEditTimeElement(node: Node): boolean {
    if (node.nodeType !== Node.ELEMENT_NODE) return false;
    const element = node as Element;
    return (
        element.matches(kEditTimeElements) ||
        element.matches(kElementsIgnoredByUndoOnly)
    );
}

/**
 * Blocks that CKEditor's data processor fills when they are empty: those of CKEDITOR.dtd.$block
 * and $blockLimit that can hold text, less the sectioning ones that a box does not use.
 */
const kBlocksThatCkEditorFills = new Set([
    "ADDRESS",
    "BLOCKQUOTE",
    "CAPTION",
    "DD",
    "DIV",
    "DT",
    "FIGCAPTION",
    "H1",
    "H2",
    "H3",
    "H4",
    "H5",
    "H6",
    "LI",
    "P",
    "PRE",
    "TD",
    "TH",
]);

/**
 * True for a br that is CKEditor's filler in an otherwise empty block of a box, when it is the
 * only child of parent: a br with no data-cke-eol, in a block inside a .bloom-editable. CKEditor
 * puts one into an empty block in the browser and takes it out again as the caret comes and goes
 * (a focus lost, a box written again after it left the page). The save does not keep it as such:
 * getData() drops the last br of a block and fills an empty block with &nbsp;
 * (lib/ckeditor/ckeditor.js, config.fillEmptyBlocks left at its default), and doCkEditorCleanup
 * turns <p>&nbsp;</p> into <p><br></p> (fixUpEmptyishParagraphs), so <p></p> and <p><br></p> save
 * alike. A br after text (<p>a<br></p>) is dropped by the save too, but it is left to count, as
 * is every other br: recording it is the safe direction.
 */
export function isCkEditorFillerBr(child: Node, parent: Node): boolean {
    if (child.nodeType !== Node.ELEMENT_NODE) return false;
    const br = child as Element;
    if (br.tagName !== "BR" || br.hasAttribute("data-cke-eol")) return false;
    if (parent.nodeType !== Node.ELEMENT_NODE) return false;
    const block = parent as Element;
    return (
        kBlocksThatCkEditorFills.has(block.tagName) &&
        !!block.closest(".bloom-editable")
    );
}

/** True for a node that is, or is inside, an element that isEditTimeElement is true for. */
export function isInsideEditTimeElement(node: Node): boolean {
    const element =
        node.nodeType === Node.ELEMENT_NODE
            ? (node as Element)
            : node.parentElement;
    return (
        !!element?.closest(kEditTimeElements) ||
        !!element?.closest(kElementsIgnoredByUndoOnly)
    );
}

/**
 * The part of an attribute's value that a save keeps, or undefined when it keeps none: class token
 * by token, style property by property, a video's src without its timestamp, and the attributes
 * that a save drops (or that undo leaves alone) not at all.
 */
export function keptAttributeValue(
    element: Element,
    name: string,
    value: string | undefined,
): string | undefined {
    if (value === undefined) return undefined;
    if (name === "class") return keptClasses(value);
    if (name === "style") return keptStyle(element, value);
    if (isDroppedAttribute(name, value)) return undefined;
    if (
        kCkEditorAttributesOfABox.has(name) &&
        element.classList.contains("bloom-editable")
    )
        return undefined;
    if (name === "src" && element.matches("video, video source"))
        return withoutVideoTimestamp(value);
    return value;
}

/**
 * The value to give an attribute when putting back the kept part `kept` while its value now is
 * `current`: kept, with the part of current that a save drops, so that undoing a class change
 * neither brings back an old cke_focus nor removes the one there now. Undefined removes the
 * attribute.
 */
export function attributeValueToPutBack(
    element: Element,
    name: string,
    kept: string | undefined,
    current: string | undefined,
): string | undefined {
    if (keptAttributeValue(element, name, current) === kept) return current;
    if (name === "class") return classesToPutBack(kept, current);
    if (name === "style") return styleToPutBack(element, kept, current);
    return kept;
}

/**
 * Attributes of a .bloom-editable that a save keeps but that undo leaves alone: CKEditor 4.5.1's
 * inline editor sets them when it attaches to the box (CKEDITOR.editable's setup and the
 * instanceCreated "mode" handler in lib/ckeditor/ckeditor.js), and nothing in Bloom sets them on
 * a box. With lib/ckeditor/config.js they are tabindex="0", spellcheck="false" (because of
 * disableNativeSpellChecker), role="textbox" and aria-label="false" (because title is false).
 * CKEditor may finish attaching after the box has left the page, so they can differ between a
 * box that an undo step took out and the same box when the step puts it back. C# drops them only
 * when it copies a box to the data-div (BookData._attributesNotToCopy). A translation group's
 * tabindex is Bloom's own (the Talking Book playback order), so this applies only to boxes.
 */
const kCkEditorAttributesOfABox = new Set([
    "tabindex",
    "spellcheck",
    "role",
    "aria-label",
]);

/**
 * True for an attribute that a save drops whatever its value, or that undo leaves alone wherever
 * it is, or, for qtip's describedby attributes, that a save drops with this value.
 */
function isDroppedAttribute(name: string, value: string): boolean {
    switch (name) {
        // qtip's markers on the elements it annotates. The save on master keeps them, but every
        // page load resets them (Cleanup, bloomQtipUtils.cleanupBubbles), so they are qtip's
        // state, not content, and section 3.7 lists them.
        case "data-hasqtip":
            return true;
        case "aria-describedby":
        case "ariasecondary-describedby":
            return value.startsWith("qtip-");
        // TeardownTableEditing removes data-table-attached; bloom-table's stripEditTimeMarkup
        // removes data-btable-anchor-name.
        case "data-table-attached":
        case "data-btable-anchor-name":
            return true;
        // Kept by the save, left alone by undo: which canvas element is active is view state.
        case "data-bloom-active":
            return true;
    }
    // CKEditor's own attributes, which getData() leaves out (doCkEditorCleanup); C# also removes
    // data-cke-saved-href.
    return name.startsWith("data-cke-");
}

/**
 * True for a class token that a save removes, or that undo leaves alone. The history neither
 * records nor compares it, and putting back a class value keeps the ones there now.
 */
function isEditTimeClass(token: string): boolean {
    return (
        kEditTimeClasses.has(token) ||
        kClassesIgnoredByUndoOnly.has(token) ||
        token.startsWith(kCkEditorClassPrefix)
    );
}

/** The class that a token stands for in a save: niceScroll's renamed tokens become the original. */
function savedFormOfClass(token: string): string {
    return kAlignmentClassesRenamedByNiceScroll.get(token) ?? token;
}

/** The tokens of a class value, without empty ones. */
function classTokens(value: string | undefined): string[] {
    return value === undefined ? [] : value.split(/\s+/).filter((t) => t);
}

/**
 * The classes of a class value that the history records (those that a save keeps, less those
 * that undo leaves alone), in their saved form, sorted and without
 * repeats, or undefined when there are none. The order of tokens means nothing, and niceScroll
 * changes it when it takes an alignment class off and puts it back, so it is not compared.
 */
export function keptClasses(value: string): string | undefined {
    const kept = new Set(
        classTokens(value)
            .filter((token) => !isEditTimeClass(token))
            .map(savedFormOfClass),
    );
    return kept.size === 0 ? undefined : Array.from(kept).sort().join(" ");
}

/**
 * The class value with the kept classes `kept` and the edit-time classes of `current`, in
 * current's order where it has them. A kept class that current holds in niceScroll's renamed
 * form stays in that form, since niceScroll still holds it.
 */
function classesToPutBack(
    kept: string | undefined,
    current: string | undefined,
): string | undefined {
    const keptTokens = classTokens(kept);
    const stillWanted = new Set(keptTokens);
    const result: string[] = [];
    for (const token of classTokens(current)) {
        if (isEditTimeClass(token)) {
            if (!result.includes(token)) result.push(token);
            continue;
        }
        const savedForm = savedFormOfClass(token);
        if (stillWanted.has(savedForm)) {
            result.push(token);
            stillWanted.delete(savedForm);
        }
    }
    for (const token of keptTokens) {
        if (stillWanted.has(token)) result.push(token);
    }
    return result.length === 0 ? undefined : result.join(" ");
}

/** One declaration of an inline style. */
interface StyleDeclaration {
    // Lower case, except for a custom property, whose name is case sensitive.
    name: string;
    value: string;
}

/**
 * The declarations of a style attribute's value, in order. Splits at semicolons outside quotes
 * and brackets, so that a url() or a quoted string may hold one. A piece with no colon is not a
 * declaration, and the browser ignores it, so it is left out.
 */
function parseStyle(text: string): StyleDeclaration[] {
    const pieces: string[] = [];
    let start = 0;
    let depth = 0;
    let quote: string | undefined;
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quote) {
            if (c === "\\") i++;
            else if (c === quote) quote = undefined;
        } else if (c === '"' || c === "'") quote = c;
        else if (c === "(") depth++;
        else if (c === ")") depth = Math.max(0, depth - 1);
        else if (c === ";" && depth === 0) {
            pieces.push(text.substring(start, i));
            start = i + 1;
        }
    }
    pieces.push(text.substring(start));
    const declarations: StyleDeclaration[] = [];
    for (const piece of pieces) {
        const colon = piece.indexOf(":");
        if (colon < 0) continue;
        const rawName = piece.substring(0, colon).trim();
        const value = piece.substring(colon + 1).trim();
        if (!rawName) continue;
        const name = rawName.startsWith("--") ? rawName : rawName.toLowerCase();
        declarations.push({ name, value });
    }
    return declarations;
}

/**
 * Style text for declarations, in the form that the browser writes when code sets a property.
 * Setting one property rewrites the whole attribute in that form, so a value that the browser has
 * only rewritten is no change.
 */
function serializeStyle(
    doc: Document,
    declarations: StyleDeclaration[],
): string | undefined {
    if (declarations.length === 0) return undefined;
    return declarations
        .map((d) => `${d.name}: ${normalizedStyleValue(doc, d)};`)
        .join(" ");
}

const scratchStyles = new WeakMap<Document, CSSStyleDeclaration>();

/**
 * A declaration's value as the browser writes it (for instance a color in rgb() form), found by
 * setting it on an element that is not in the page. A value that the browser does not accept is
 * left as it is.
 */
function normalizedStyleValue(
    doc: Document,
    declaration: StyleDeclaration,
): string {
    let scratch = scratchStyles.get(doc);
    if (!scratch) {
        scratch = doc.createElement("div").style;
        scratchStyles.set(doc, scratch);
    }
    const important = /\s*!\s*important$/i.exec(declaration.value);
    const value = important
        ? declaration.value.substring(0, important.index)
        : declaration.value;
    scratch.setProperty(declaration.name, value, important ? "important" : "");
    const normalized = scratch.getPropertyValue(declaration.name);
    scratch.removeProperty(declaration.name);
    if (!normalized) return declaration.value;
    return important ? `${normalized} !important` : normalized;
}

/** True for an inline style property of element that a save removes. */
function isEditTimeStyleProperty(element: Element, name: string): boolean {
    if (kEditTimeStyleProperties.has(name)) return true;
    return (
        kNiceScrollStyleProperties.has(name) &&
        element.matches(kSelectorForPotentialNiceScrollElements)
    );
}

/**
 * The declarations of a style value that a save keeps, in order, as style text, or undefined when
 * there are none.
 */
export function keptStyle(element: Element, value: string): string | undefined {
    return serializeStyle(
        element.ownerDocument,
        parseStyle(value).filter(
            (d) => !isEditTimeStyleProperty(element, d.name),
        ),
    );
}

/** Style text with the kept declarations `kept`, then the edit-time declarations of `current`. */
function styleToPutBack(
    element: Element,
    kept: string | undefined,
    current: string | undefined,
): string | undefined {
    const editTime = parseStyle(current ?? "").filter((d) =>
        isEditTimeStyleProperty(element, d.name),
    );
    return serializeStyle(element.ownerDocument, [
        ...parseStyle(kept ?? ""),
        ...editTime,
    ]);
}

/**
 * A video URL without the timestamp parameter. This is the rule of stripTransientVideoTimestampParam
 * in bloomVideo.ts, which removeEditingDebris applies; editTimeMarkupSpec.ts checks that the two
 * agree. It is not imported because bloomVideo.ts brings in the sign language tool and much else.
 */
export function withoutVideoTimestamp(url: string): string {
    const hashIndex = url.indexOf("#");
    const hash = hashIndex >= 0 ? url.substring(hashIndex) : "";
    const beforeHash = hashIndex >= 0 ? url.substring(0, hashIndex) : url;
    const queryIndex = beforeHash.indexOf("?");
    if (queryIndex < 0) return url;
    const searchParams = new URLSearchParams(
        beforeHash.substring(queryIndex + 1),
    );
    searchParams.delete(kVideoTimestampParam);
    const search = searchParams.toString();
    return `${beforeHash.substring(0, queryIndex)}${search ? `?${search}` : ""}${hash}`;
}

/**
 * The text without CKEditor's filling character: its first U+200B goes, and a plain space right
 * after it becomes a no-break space, so that the space is not lost to whitespace collapsing. This
 * is what EditableDivUtils.removeTrackedCkEditorFillingChar does to the node. Later U+200B are
 * left, because Thai, Khmer and Myanmar text uses U+200B as a word break (BL-16843).
 */
export function withoutFillingChar(text: string): string {
    const i = text.indexOf(kFillingChar);
    if (i < 0) return text;
    const rest = text.substring(i + 1);
    return (
        text.substring(0, i) +
        (rest.startsWith(" ") ? "\u00A0" + rest.substring(1) : rest)
    );
}

/** The part of a CKEditor editable that holds the filling character's node. */
interface CkEditorEditable {
    getCustomData(key: string): { $?: Node } | null | undefined;
    removeCustomData(key: string): unknown;
}

/** An element that BloomField has attached a CKEditor instance to. */
type ElementWithCkEditor = Element & {
    bloomCkEditor?: { editable(): CkEditorEditable | null | undefined };
};

/**
 * The editable of the CKEditor instance on the nearest element at or above node that has one.
 * BloomField keeps the instance on the element as bloomCkEditor, as EditableDivUtils reads it.
 */
function ckEditorEditableOf(node: Node): CkEditorEditable | undefined {
    for (
        let element: Element | null =
            node.nodeType === Node.ELEMENT_NODE
                ? (node as Element)
                : node.parentElement;
        element;
        element = element.parentElement
    ) {
        const editor = (element as ElementWithCkEditor).bloomCkEditor;
        if (editor) return editor.editable() ?? undefined;
    }
    return undefined;
}

/**
 * True when node is the text node in which the CKEditor instance of its box keeps its filling
 * character now. CKEditor inserts the character at a collapsed selection next to an inline
 * element, remembers the node, and takes the character out again later; getData() leaves it out.
 */
export function isTrackedFillingCharNode(node: Node): boolean {
    if (node.nodeType !== Node.TEXT_NODE) return false;
    return ckEditorEditableOf(node)?.getCustomData(kFillingCharKey)?.$ === node;
}

/** Every text node inside root that a CKEditor instance keeps its filling character in now. */
function trackedFillingCharNodes(root: Element): Set<Node> {
    const nodes = new Set<Node>();
    root.querySelectorAll("[contenteditable]").forEach((element) => {
        const node = (element as ElementWithCkEditor).bloomCkEditor
            ?.editable()
            ?.getCustomData(kFillingCharKey)?.$;
        if (node && root.contains(node)) nodes.add(node);
    });
    return nodes;
}

/**
 * The page history's filter for the rules above, with the one rule that a save has no form of:
 * CKEditor's filling character.
 *
 * A text value that holds the filling character must be recorded without it. Put back after
 * CKEditor has forgotten the node, the character stays for good and is saved into the book
 * (BL-16490). Which U+200B is the filling character depends on CKEditor's state when the value
 * was current, not on the text, since a U+200B may be a real word break. A node holds the
 * filling character from when CKEditor creates it until CKEditor forgets it, and CKEditor never
 * tracks a node again after forgetting it. So a value is taken to hold the character when its
 * node is tracked now, or when it is the value at the start of a batch and its node was tracked
 * when the previous batch was noted. Each forgetting changes the node's text, so it shows in the
 * batch in which it happened.
 */
class EditTimeMarkupFilter implements PageHistoryFilter {
    // The nodes that held the filling character when noteRecords last looked.
    private trackedAtLastLook = new Set<Node>();
    // Every node that held the filling character at any look.
    private readonly everTracked = new WeakSet<Node>();
    // The nodes that the latest look found holding it for the first time.
    private trackedFirstAtLatestLook = new Set<Node>();
    // The nodes that held it at the start of the latest batch, which is when the previous look
    // happened: tracking starts and ends only with a change to the page.
    private trackedAtBatchStart = new Set<Node>();

    /** See PageHistoryFilter.ignoreNode. */
    public ignoreNode(node: Node): boolean {
        return isEditTimeElement(node);
    }

    /** See PageHistoryFilter.isLeftOutWhenOnlyChild. */
    public isLeftOutWhenOnlyChild(child: Node, parent: Node): boolean {
        return isCkEditorFillerBr(child, parent);
    }

    /** See PageHistoryFilter.keptAttributeValue. */
    public keptAttributeValue(
        element: Element,
        name: string,
        value: string | undefined,
    ): string | undefined {
        return keptAttributeValue(element, name, value);
    }

    /** See PageHistoryFilter.valueToPutBack. */
    public valueToPutBack(
        element: Element,
        name: string,
        kept: string | undefined,
        current: string | undefined,
    ): string | undefined {
        return attributeValueToPutBack(element, name, kept, current);
    }

    /** See PageHistoryFilter.keptText. */
    public keptText(
        node: CharacterData,
        value: string,
        when: "now" | "earlier",
    ): string {
        const holdsFillingChar =
            isTrackedFillingCharNode(node) ||
            (when === "earlier" && this.trackedAtBatchStart.has(node));
        return holdsFillingChar ? withoutFillingChar(value) : value;
    }

    /**
     * See PageHistoryFilter.textToPutBack. The text put back has no filling character, so if
     * CKEditor tracks the node, it is made to forget it, as removeTrackedCkEditorFillingChar
     * does; otherwise CKEditor would later take a real U+200B out of it, and this filter would
     * take one out of every value it reads from the node.
     */
    public textToPutBack(node: CharacterData, kept: string): string {
        if (isTrackedFillingCharNode(node)) {
            // Still a node that once held the character, for isIgnoredText.
            this.everTracked.add(node);
            ckEditorEditableOf(node)!.removeCustomData(kFillingCharKey);
        }
        this.trackedAtLastLook.delete(node);
        this.trackedAtBatchStart.delete(node);
        return kept;
    }

    /**
     * See PageHistoryFilter.isIgnoredText. A text node that holds only the filling character, or
     * that once held it and is empty now that CKEditor has taken it out, is left out of child
     * lists: CKEditor inserts one wherever the caret lands next to an inline element or in an
     * empty paragraph, and a save keeps nothing of it. Text typed into it brings it into the
     * child lists. "earlier" excludes a node that the latest look found for the first time,
     * since it was not on the page at the start of that batch.
     */
    public isIgnoredText(
        node: CharacterData,
        value: string,
        when: "now" | "earlier",
    ): boolean {
        if (when === "earlier") {
            if (
                !this.everTracked.has(node) ||
                this.trackedFirstAtLatestLook.has(node)
            )
                return false;
        } else if (
            !this.everTracked.has(node) &&
            !isTrackedFillingCharNode(node)
        )
            return false;
        return this.keptText(node, value, when) === "";
    }

    /** See PageHistoryFilter.noteRecords. */
    public noteRecords(root: Element, _records: MutationRecord[]): void {
        this.trackedAtBatchStart = this.trackedAtLastLook;
        this.trackedAtLastLook = trackedFillingCharNodes(root);
        this.trackedFirstAtLatestLook = new Set(
            Array.from(this.trackedAtLastLook).filter(
                (node) => !this.everTracked.has(node),
            ),
        );
        this.trackedAtLastLook.forEach((node) => this.everTracked.add(node));
    }
}

/** A new filter for one page history: it keeps state about CKEditor's filling character. */
export function createEditTimeMarkupFilter(): PageHistoryFilter {
    return new EditTimeMarkupFilter();
}
