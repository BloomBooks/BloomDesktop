const kSegmentClass = "bloom-highlightSegment";
const kEnableHighlightClass = "ui-enableHighlight";
const kSuppressHighlightClass = "ui-suppressHighlight";
const kDisableHighlightClass = "ui-disableHighlight";
const kPostAudioSplitClass = "bloom-postAudioSplit";
const kTextBoxRecordingMode = "textbox";

export const splitHighlightNames = [
    "bloom-audio-split-1",
    "bloom-audio-split-2",
    "bloom-audio-split-3",
] as const;

type HighlightRegistry = Map<string, unknown>;
type HighlightConstructor = new (...ranges: Range[]) => unknown;

const getDocumentWindow = (contextNode: Node): Window | undefined => {
    return contextNode.ownerDocument?.defaultView ?? undefined;
};

const getHighlightRegistry = (
    contextNode: Node,
): HighlightRegistry | undefined => {
    const docWindow = getDocumentWindow(contextNode) as
        | (Window & typeof globalThis)
        | undefined;
    const cssWithHighlights = docWindow?.CSS as
        | (typeof globalThis.CSS & {
              highlights?: HighlightRegistry;
          })
        | undefined;
    return cssWithHighlights?.highlights;
};

const getHighlightConstructor = (
    contextNode: Node,
): HighlightConstructor | undefined => {
    const docWindow = getDocumentWindow(contextNode) as
        | (Window & {
              Highlight?: HighlightConstructor;
          })
        | undefined;
    return docWindow?.Highlight;
};

export class AudioTextHighlightManager {
    public clearManagedHighlights(contextNode?: Node): void {
        if (!contextNode) {
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        if (!registry) {
            return;
        }

        splitHighlightNames.forEach((name) => registry.delete(name));
    }

    public refreshSplitHighlights(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ): void {
        const contextNode = currentHighlight ?? currentTextBox;
        if (!contextNode) {
            return;
        }

        if (!this.shouldShowSplitHighlights(currentHighlight, currentTextBox)) {
            this.clearManagedHighlights(contextNode);
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        const Highlight = getHighlightConstructor(contextNode);
        if (!registry || !Highlight) {
            return;
        }

        const rangesByName = new Map<string, Range[]>();
        splitHighlightNames.forEach((name) => rangesByName.set(name, []));

        const segmentGroups = new Map<Element, Element[]>();
        Array.from(
            currentTextBox.querySelectorAll(`span.${kSegmentClass}`),
        ).forEach((segment) => {
            const parent = segment.parentElement;
            if (!parent) {
                return;
            }

            const segments = segmentGroups.get(parent) ?? [];
            segments.push(segment);
            segmentGroups.set(parent, segments);
        });

        segmentGroups.forEach((segments) => {
            segments.forEach((segment, index) => {
                const highlightName =
                    splitHighlightNames[index % splitHighlightNames.length];
                const ranges = rangesByName.get(highlightName);
                if (!ranges) {
                    return;
                }

                ranges.push(...this.getRangesForSegment(segment));
            });
        });

        splitHighlightNames.forEach((name) => {
            const ranges = rangesByName.get(name) ?? [];
            if (ranges.length > 0) {
                registry.set(name, new Highlight(...ranges));
            } else {
                registry.delete(name);
            }
        });
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

        if (
            currentTextBox.classList.contains(kSuppressHighlightClass) ||
            currentTextBox.classList.contains(kDisableHighlightClass)
        ) {
            return false;
        }

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
        )
            .map((enabledSpan) => this.makeRange(enabledSpan))
            .filter((range): range is Range => !!range);

        if (enabledRanges.length > 0) {
            return enabledRanges;
        }

        const wholeSegmentRange = this.makeRange(segment);
        return wholeSegmentRange ? [wholeSegmentRange] : [];
    }

    private makeRange(node: Node): Range | undefined {
        if (node.textContent === null || node.textContent.length === 0) {
            return undefined;
        }

        const ownerDocument = node.ownerDocument;
        if (!ownerDocument) {
            return undefined;
        }

        const range = ownerDocument.createRange();
        range.selectNodeContents(node);
        return range;
    }
}
