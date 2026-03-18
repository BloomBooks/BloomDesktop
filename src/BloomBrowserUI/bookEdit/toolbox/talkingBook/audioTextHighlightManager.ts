const kSegmentClass = "bloom-highlightSegment";
const kEnableHighlightClass = "ui-enableHighlight";
const kSuppressHighlightClass = "ui-suppressHighlight";
const kDisableHighlightClass = "ui-disableHighlight";
const kPostAudioSplitClass = "bloom-postAudioSplit";
const kTextBoxRecordingMode = "textbox";
const kCustomHighlightSupportClass = "bloom-audio-customHighlights";
const kCurrentHighlightBackgroundSourceCssVar =
    "--bloom-audio-highlight-background";
const kCurrentHighlightColorSourceCssVar = "--bloom-audio-highlight-text-color";
const kCurrentHighlightBackgroundCssVar =
    "--bloom-audio-current-highlight-background";
const kCurrentHighlightColorCssVar = "--bloom-audio-current-highlight-color";

// Keep the live recording highlight in the same registry as split highlights so
// AudioRecording only has one place to refresh whenever the current element changes.
export const currentHighlightName = "bloom-audio-current";

export const splitHighlightNames = [
    "bloom-audio-split-1",
    "bloom-audio-split-2",
    "bloom-audio-split-3",
] as const;

const managedHighlightNames = [currentHighlightName, ...splitHighlightNames];

type HighlightRegistry = Map<string, unknown>;
type HighlightConstructor = new (...ranges: Range[]) => unknown;

const getDocumentWindow = (contextNode: Node): Window | undefined => {
    return contextNode.ownerDocument?.defaultView ?? undefined;
};

const getDocumentElement = (contextNode: Node): HTMLElement | undefined => {
    return contextNode.ownerDocument?.documentElement ?? undefined;
};

const getDocumentBody = (contextNode: Node): HTMLElement | undefined => {
    return contextNode.ownerDocument?.body ?? undefined;
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

        managedHighlightNames.forEach((name) => registry.delete(name));
        this.clearCurrentHighlightColors(contextNode);
    }

    private clearSplitHighlights(contextNode?: Node): void {
        if (!contextNode) {
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        if (!registry) {
            return;
        }

        splitHighlightNames.forEach((name) => registry.delete(name));
    }

    public refreshHighlights(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ): void {
        const contextNode = currentHighlight ?? currentTextBox;
        if (!contextNode) {
            return;
        }

        if (!this.supportsCustomHighlights(contextNode)) {
            return;
        }

        // Once custom highlights are active, suppress the old element background rules
        // so Chromium paints the pseudo highlight instead of double-highlighting the text.
        getDocumentBody(contextNode)?.classList.add(
            kCustomHighlightSupportClass,
        );

        this.refreshCurrentHighlight(currentHighlight, currentTextBox);
        this.refreshSplitHighlights(currentHighlight, currentTextBox);
    }

    private supportsCustomHighlights(contextNode: Node): boolean {
        return (
            !!getHighlightRegistry(contextNode) &&
            !!getHighlightConstructor(contextNode)
        );
    }

    private refreshCurrentHighlight(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ): void {
        const contextNode = currentHighlight ?? currentTextBox;
        if (!contextNode) {
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        const Highlight = getHighlightConstructor(contextNode);
        if (!registry || !Highlight) {
            return;
        }

        // The split-complete blue state replaces the yellow current highlight while it is visible,
        // so clear the yellow registry entry instead of letting the two overlap.
        if (this.shouldShowSplitHighlights(currentHighlight, currentTextBox)) {
            registry.delete(currentHighlightName);
            this.clearCurrentHighlightColors(contextNode);
            return;
        }

        const highlightInfo = this.getCurrentHighlightInfo(
            currentHighlight,
            currentTextBox,
        );
        if (!highlightInfo || highlightInfo.ranges.length === 0) {
            registry.delete(currentHighlightName);
            this.clearCurrentHighlightColors(contextNode);
            return;
        }

        this.updateCurrentHighlightColors(highlightInfo.styleSource);
        registry.set(
            currentHighlightName,
            new Highlight(...highlightInfo.ranges),
        );
    }

    private refreshSplitHighlights(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ): void {
        const contextNode = currentHighlight ?? currentTextBox;
        if (!contextNode) {
            return;
        }

        if (!this.shouldShowSplitHighlights(currentHighlight, currentTextBox)) {
            this.clearSplitHighlights(contextNode);
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

        if (currentHighlight.classList.contains(kSuppressHighlightClass)) {
            return undefined;
        }

        const enabledDescendants = Array.from(
            currentHighlight.querySelectorAll(`span.${kEnableHighlightClass}`),
        );
        const enabledRanges = enabledDescendants
            .map((enabledSpan) => this.makeRange(enabledSpan))
            .filter((range): range is Range => !!range);

        // fixHighlighting() can carve the visible pieces into ui-enableHighlight spans.
        // Prefer those pieces so custom highlights preserve the same whitespace behavior.
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
            const paragraphRanges = paragraphs
                .map((paragraph) => this.makeRange(paragraph))
                .filter((range): range is Range => !!range);
            if (paragraphRanges.length > 0) {
                return {
                    ranges: paragraphRanges,
                    styleSource: paragraphs[0],
                };
            }
        }

        const wholeElementRange = this.makeRange(currentHighlight);
        if (!wholeElementRange) {
            return undefined;
        }

        return {
            ranges: [wholeElementRange],
            styleSource: currentHighlight,
        };
    }

    private updateCurrentHighlightColors(styleSource: Element): void {
        const documentElement = getDocumentElement(styleSource);
        if (!documentElement) {
            return;
        }

        // The actual colors still come from CSS so user-modified highlight colors keep working.
        // We copy them into document-level variables that the ::highlight rule can read.
        const computedStyle =
            getDocumentWindow(styleSource)?.getComputedStyle(styleSource);
        const backgroundColor = computedStyle
            ?.getPropertyValue(kCurrentHighlightBackgroundSourceCssVar)
            .trim();
        const color = computedStyle
            ?.getPropertyValue(kCurrentHighlightColorSourceCssVar)
            .trim();

        if (backgroundColor) {
            documentElement.style.setProperty(
                kCurrentHighlightBackgroundCssVar,
                backgroundColor,
            );
        } else {
            documentElement.style.removeProperty(
                kCurrentHighlightBackgroundCssVar,
            );
        }

        if (color) {
            documentElement.style.setProperty(
                kCurrentHighlightColorCssVar,
                color,
            );
        } else {
            documentElement.style.removeProperty(kCurrentHighlightColorCssVar);
        }
    }

    private clearCurrentHighlightColors(contextNode: Node): void {
        const documentElement = getDocumentElement(contextNode);
        if (!documentElement) {
            return;
        }

        documentElement.style.removeProperty(kCurrentHighlightBackgroundCssVar);
        documentElement.style.removeProperty(kCurrentHighlightColorCssVar);
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
