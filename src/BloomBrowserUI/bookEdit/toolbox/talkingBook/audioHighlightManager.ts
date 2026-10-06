import StyleEditor from "../../StyleEditor/StyleEditor";
import {
    makeRangeFromTextOffsets,
    mapVisibleText,
    TextHighlightManager,
} from "../../js/textHighlightManager";

const kSegmentClass = "bloom-highlightSegment";
const kEnableHighlightClass = "ui-enableHighlight";
const kDisableHighlightClass = "ui-disableHighlight";
const kPostAudioSplitClass = "bloom-postAudioSplit";
const kTextBoxRecordingMode = "textbox";

// A run of three or more spaces (ordinary, non-breaking or zero-width) is layout -- authors use
// them to space out the words of a poem or a song -- so it is left unpainted, and the words on
// either side are highlighted separately. Two or fewer are just the gap between words.
const kLongGapRegex = /[ \u00a0\u200b]{3,}/g;
const kAllWhitespaceRegex = /^[\s\u00a0\u200b]*$/;

// Ranges covering the visible text under node, leaving out every long gap (see kLongGapRegex) and
// any piece that is only whitespace, so the highlight skips layout space without adding markup to
// the page. A gap that runs across inline markup (<em>, <strong>, a coloured span) counts as one
// gap, because we look at the text as the reader sees it, not node by node.
export function makeRangesSkippingLongGaps(node: Node): Range[] {
    const map = mapVisibleText(node);
    const ranges: Range[] = [];
    const addPiece = (start: number, end: number): void => {
        if (kAllWhitespaceRegex.test(map.text.slice(start, end))) {
            return;
        }
        const range = makeRangeFromTextOffsets(map, start, end);
        if (range) {
            ranges.push(range);
        }
    };
    kLongGapRegex.lastIndex = 0; // exec on a global regex is stateful
    let pieceStart = 0;
    let gap: RegExpExecArray | null;
    while ((gap = kLongGapRegex.exec(map.text)) !== null) {
        addPiece(pieceStart, gap.index);
        pieceStart = gap.index + gap[0].length;
    }
    addPiece(pieceStart, map.text.length);
    return ranges;
}

const kCurrentHighlightBackgroundCssVar =
    "--bloom-audio-current-highlight-background";
const kCurrentHighlightColorCssVar = "--bloom-audio-current-highlight-color";

// This translates Bloom's audio-highlight classes into the CSS highlight registry and
// ::highlight pseudo-elements (see textHighlightManager.ts for why and how).
// The DOM still decides which pieces of text are eligible and marks them with the appropriate
// classes, but in the Edit Tab the visible paint comes from ::highlight pseudo-elements instead
// of the element background colors, which we continue to use in Bloom Player etc. - we will need
// to keep the original class and css rules for a while so that old versions of Bloom player
// display the highlights, but a next step would be to make newer versions of Bloom Player switch
// to using pseudo-elements to display highlights like we do here.

export const currentHighlightName = "bloom-audio-current";

export const splitHighlightNames = [
    "bloom-audio-split-1",
    "bloom-audio-split-2",
    "bloom-audio-split-3",
] as const;

const allManagedHighlightNames = [currentHighlightName, ...splitHighlightNames];

// A StyleEditor instance used only to read the user's audio-highlight colors from a book's
// userModifiedStyles sheet. We share StyleEditor's rule lookup rather than duplicating the
// selector-matching logic, so the read and the write (StyleEditor.putAudioHiliteRulesInDom)
// can never drift apart. supportFilesRoot is irrelevant for this read-only use.
let styleEditorForColorLookup: StyleEditor | undefined;
function getStyleEditorForColorLookup(): StyleEditor {
    if (!styleEditorForColorLookup) {
        styleEditorForColorLookup = new StyleEditor("/bloom/bookEdit");
    }
    return styleEditorForColorLookup;
}

function getDocumentElement(contextNode: Node): HTMLElement | undefined {
    return contextNode.ownerDocument?.documentElement ?? undefined;
}

export class AudioHighlightManager {
    private highlights = new TextHighlightManager(allManagedHighlightNames);

    // Remove all current and split highlights from the registry for the document containing contextNode.
    public clearAllManagedHighlights(contextNode?: Node): void {
        this.highlights.clearAllManagedHighlights(contextNode);
    }

    // Remove only the split (blue segment) highlights from the registry, leaving the current highlight intact.
    public clearSplitHighlights(contextNode?: Node): void {
        this.highlights.clearHighlights(splitHighlightNames, contextNode);
    }

    // Returns true if the current highlight is still registered but no longer paints anything
    // because the DOM under it was rewritten. See TextHighlightManager.hasDeadRanges().
    public currentHighlightHasDeadRanges(contextNode?: Node): boolean {
        return this.highlights.hasDeadRanges(currentHighlightName, contextNode);
    }

    // currentHighlight is the element currently selected for recording etc.
    // It might be a span (sentence mode) or text box (text box mode).
    // currentTextBox is either the same as currentHighlight (text box mode)
    // or its TextBox ancestor (sentence mode).
    // Adjust pseudo-element highlights to what they should be for this state of things.
    public refreshHighlights(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
        suppressCurrentHighlight?: boolean,
    ): void {
        const contextNode = currentHighlight ?? currentTextBox;
        if (!contextNode) {
            return;
        }

        if (!this.highlights.canHighlight(contextNode)) {
            return;
        }

        if (suppressCurrentHighlight) {
            this.highlights.clearAllManagedHighlights(contextNode);
            return;
        }

        // Split highlights (blue segments after a textbox recording) and the current highlight
        // (yellow sentence) are mutually exclusive: split state replaces the yellow highlight.
        if (this.shouldShowSplitHighlights(currentHighlight, currentTextBox)) {
            this.highlights.clearHighlights(
                [currentHighlightName],
                contextNode,
            );
            this.refreshSplitHighlights(currentTextBox);
        } else {
            this.highlights.clearHighlights(splitHighlightNames, contextNode);
            this.refreshCurrentHighlight(currentHighlight, currentTextBox);
        }
    }

    private refreshCurrentHighlight(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ): void {
        const contextNode = currentHighlight ?? currentTextBox;
        if (!contextNode) {
            return;
        }

        const highlightInfo = this.getCurrentHighlightInfo(
            currentHighlight,
            currentTextBox,
        );
        if (!highlightInfo || highlightInfo.ranges.length === 0) {
            this.highlights.clearHighlights(
                [currentHighlightName],
                contextNode,
            );
            return;
        }

        // enhance: don't check for highlight color settings changes so often
        this.updateCurrentHighlightColors(highlightInfo.styleSource);
        this.highlights.setHighlight(
            currentHighlightName,
            highlightInfo.ranges,
            contextNode,
        );
    }

    private refreshSplitHighlights(currentTextBox: HTMLElement): void {
        // Cycle through 3 colors using a page-relative index so adjacent paragraphs
        // never share the same color at their boundary.
        const rangesByName = new Map<string, Range[]>();
        splitHighlightNames.forEach((name) => rangesByName.set(name, []));

        Array.from(
            currentTextBox.querySelectorAll(`span.${kSegmentClass}`),
        ).forEach((segment, index) => {
            const highlightName =
                splitHighlightNames[index % splitHighlightNames.length];
            const ranges = rangesByName.get(highlightName);
            ranges?.push(...this.getRangesForSegment(segment));
        });

        splitHighlightNames.forEach((name) => {
            this.highlights.setHighlight(
                name,
                rangesByName.get(name) ?? [],
                currentTextBox,
            );
        });
    }

    private getCurrentHighlightInfo(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ):
        | {
              ranges: Range[];
              styleSource: Element;
          }
        | undefined {
        if (!currentHighlight) {
            return undefined;
        }

        // A book can carry its own ui-enableHighlight spans inside a sentence whose own highlight
        // is disabled, marking the only parts that should be painted. Honour them when present.
        const enabledDescendants = Array.from(
            currentHighlight.querySelectorAll(`span.${kEnableHighlightClass}`),
        );
        const enabledRanges = enabledDescendants.flatMap((enabledSpan) =>
            makeRangesSkippingLongGaps(enabledSpan),
        );
        if (enabledRanges.length > 0) {
            return {
                ranges: enabledRanges,
                styleSource: enabledDescendants[0],
            };
        }

        if (currentHighlight.classList.contains(kDisableHighlightClass)) {
            return undefined;
        }

        if (currentHighlight === currentTextBox) {
            const paragraphs = Array.from(currentTextBox.querySelectorAll("p"));
            const paragraphRanges = paragraphs.flatMap((paragraph) =>
                makeRangesSkippingLongGaps(paragraph),
            );
            if (paragraphRanges.length > 0) {
                return {
                    ranges: paragraphRanges,
                    styleSource: paragraphs[0],
                };
            }
        }

        const wholeElementRanges = makeRangesSkippingLongGaps(currentHighlight);
        if (wholeElementRanges.length === 0) {
            return undefined;
        }

        return {
            ranges: wholeElementRanges,
            styleSource: currentHighlight,
        };
    }

    // Set the CSS variables that control the ::highlight colors to match the user's chosen
    // highlight color for the current text style, falling back to the default yellow.
    private updateCurrentHighlightColors(styleSource: Element): void {
        const documentElement = getDocumentElement(styleSource);
        if (!documentElement) {
            console.error(
                "AudioHighlightManager.updateCurrentHighlightColors() could not find documentElement for the style source.",
            );
            return;
        }

        const bloomEditable = styleSource.closest(".bloom-editable");
        const styleName = bloomEditable
            ? Array.from(bloomEditable.classList).find((c) =>
                  c.endsWith("-style"),
              )
            : undefined;

        // Read the user's chosen highlight colors for this style from the same
        // userModifiedStyles rules that StyleEditor.putAudioHiliteRulesInDom writes, by
        // delegating to StyleEditor's own lookup (looking in the page's document, not the
        // toolbox's). When there is no such style, fall back to the default yellow/black.
        const userColors = styleName
            ? getStyleEditorForColorLookup().getAudioHiliteProps(
                  styleName,
                  styleSource.ownerDocument,
              )
            : undefined;

        documentElement.style.setProperty(
            kCurrentHighlightBackgroundCssVar,
            userColors?.hiliteBgColor ?? "#febf00",
        );
        documentElement.style.setProperty(
            kCurrentHighlightColorCssVar,
            userColors?.hiliteTextColor ?? "black",
        );
    }

    private shouldShowSplitHighlights(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ): currentTextBox is HTMLElement {
        if (!currentHighlight || !currentTextBox) {
            return false;
        }

        if (currentHighlight !== currentTextBox) {
            return false;
        }

        if (currentTextBox.classList.contains(kDisableHighlightClass)) {
            return false;
        }

        // Split highlights are only for textbox recordings after AudioRecording has
        // split the textbox into segment spans and marked it as post-split.
        return (
            currentTextBox.classList.contains(kPostAudioSplitClass) &&
            currentTextBox
                .getAttribute("data-audiorecordingmode")
                ?.toLowerCase() === kTextBoxRecordingMode
        );
    }

    private getRangesForSegment(segment: Element): Range[] {
        const enabledRanges = Array.from(
            segment.querySelectorAll(`span.${kEnableHighlightClass}`),
        ).flatMap((enabledSpan) => makeRangesSkippingLongGaps(enabledSpan));

        if (enabledRanges.length > 0) {
            return enabledRanges;
        }

        return makeRangesSkippingLongGaps(segment);
    }
}
