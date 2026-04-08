const kSegmentClass = "bloom-highlightSegment";
const kEnableHighlightClass = "ui-enableHighlight";
const kSuppressHighlightClass = "ui-suppressHighlight";
const kDisableHighlightClass = "ui-disableHighlight";
const kPostAudioSplitClass = "bloom-postAudioSplit";
const kTextBoxRecordingMode = "textbox";
const kPseudoHighlightSupportClass = "bloom-audio-pseudoHighlights";

const kCurrentHighlightBackgroundCssVar =
    "--bloom-audio-current-highlight-background";
const kCurrentHighlightColorCssVar = "--bloom-audio-current-highlight-color";

// This manager translates Bloom's audio-highlight classes into the CSS highlight registry and
// ::highlight pseudo-elements.
// In rare cases, the browser can automatically move computed css into an inline style within a contenteditable, which
// we suspect is causing BL-15300 where TBT highlighting gets stuck in the book. This method of highlighting without
// modifying the dom should prevent that, and is also the direction we want to move in for highlighting.
// The DOM still decides which pieces of text are eligible and marks them with the appropriate classes, but in the Edit
// Tab the visible paint comes from ::highlight pseudo-elements instead of the element
// background colors, which we continue to use in Bloom Player etc. - we will need to keep the original class and css
// rules for a while so that old versions of Bloom player display the highlights, but a next step would be to make
// newer versions of Bloom Player switch to using pseudo-elements to display highlights like we do here

export const currentHighlightName = "bloom-audio-current";

export const splitHighlightNames = [
    "bloom-audio-split-1",
    "bloom-audio-split-2",
    "bloom-audio-split-3",
] as const;

const allManagedHighlightNames = [currentHighlightName, ...splitHighlightNames];

type HighlightRegistry = Map<string, unknown>;
type HighlightConstructor = new (...ranges: Range[]) => unknown;

function getDocumentWindow(contextNode: Node): Window | undefined {
    return contextNode.ownerDocument?.defaultView ?? undefined;
}

function getDocumentElement(contextNode: Node): HTMLElement | undefined {
    return contextNode.ownerDocument?.documentElement ?? undefined;
}

function getDocumentBody(contextNode: Node): HTMLElement | undefined {
    return contextNode.ownerDocument?.body ?? undefined;
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

export class AudioTextHighlightManager {
    public clearAllManagedHighlights(contextNode?: Node): void {
        if (!contextNode) {
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        if (!registry) {
            return;
        }

        allManagedHighlightNames.forEach((name) => registry.delete(name));
    }

    public clearSplitHighlights(contextNode?: Node): void {
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

        if (
            !getHighlightRegistry(contextNode) ||
            !getHighlightConstructor(contextNode)
        ) {
            return;
        }

        // Once pseudo-element highlights are active, suppress the old element background rules so we don't double-highlight
        getDocumentBody(contextNode)?.classList.add(
            kPseudoHighlightSupportClass,
        );

        this.refreshCurrentHighlight(currentHighlight, currentTextBox);
        this.refreshSplitHighlights(currentHighlight, currentTextBox);
    }

    private refreshCurrentHighlight(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
    ): void {
        const contextNode = currentHighlight ?? currentTextBox;
        if (!contextNode) {
            console.error(
                "AudioTextHighlightManager.refreshCurrentHighlight() was called without a context node.",
            );
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        const Highlight = getHighlightConstructor(contextNode);
        // copilot wants belt-and-suspenders, fine
        if (!registry || !Highlight) {
            console.error(
                "AudioTextHighlightManager.refreshCurrentHighlight() lost pseudo-highlight support after refreshHighlights() verified it.",
            );
            return;
        }

        // The split-complete blue state replaces the yellow current highlight while it is visible,
        // so clear the yellow registry entry instead of letting the two overlap.
        if (this.shouldShowSplitHighlights(currentHighlight, currentTextBox)) {
            registry.delete(currentHighlightName);
            return;
        }

        const highlightInfo = this.getCurrentHighlightInfo(
            currentHighlight,
            currentTextBox,
        );
        if (!highlightInfo || highlightInfo.ranges.length === 0) {
            registry.delete(currentHighlightName);
            return;
        }

        // enhance: don't check for highlight color settings changes so often
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
            console.error(
                "AudioTextHighlightManager.refreshSplitHighlights() was called without a context node.",
            );
            return;
        }

        if (!this.shouldShowSplitHighlights(currentHighlight, currentTextBox)) {
            this.clearSplitHighlights(contextNode);
            return;
        }

        const registry = getHighlightRegistry(contextNode);
        const Highlight = getHighlightConstructor(contextNode);
        if (!registry || !Highlight) {
            console.error(
                "AudioTextHighlightManager.refreshSplitHighlights() lost pseudo-highlight support after refreshHighlights() verified it.",
            );
            return;
        }

        // We cycle through 3 colors for split highlights
        const rangesByName = new Map<string, Range[]>();
        splitHighlightNames.forEach((name) => rangesByName.set(name, []));

        const segmentGroups = new Map<Element, Element[]>();
        Array.from(
            currentTextBox.querySelectorAll(`span.${kSegmentClass}`),
        ).forEach((segment) => {
            const parent = segment.parentElement;
            if (!parent) {
                // won't happen
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
                ranges?.push(...this.getRangesForSegment(segment));
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

        // copilot says: fixHighlighting() can carve the visible pieces into nested ui-enableHighlight
        // spans so punctuation or outer whitespace stays unpainted. Prefer those exact
        // spans whenever they exist so the pseudo-highlight matches the background-color highlight behavior
        const enabledDescendants = Array.from(
            currentHighlight.querySelectorAll(`span.${kEnableHighlightClass}`),
        );
        const enabledRanges = enabledDescendants
            .map((enabledSpan) => this.makeRange(enabledSpan))
            .filter((range): range is Range => !!range);
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
            console.error(
                "AudioTextHighlightManager.updateCurrentHighlightColors() could not find documentElement for the style source.",
            );
            return;
        }

        // The actual colors still come from CSS so user-modified highlight colors keep working.
        // We copy them into document-level variables that the ::highlight rule can read.
        const documentBody = getDocumentBody(styleSource);
        const hadPseudoHighlightOverride = documentBody?.classList.contains(
            kPseudoHighlightSupportClass,
        );

        // We have rules in audioRecording.less to override the normal highlighting background-color
        // (make it transparent), so it isn't underneath
        // the pseudo-element highlights which we use instead in the edit tab. Remove that rule, detect the otherwise
        // present computed highlight color, and then put the rule back.
        if (hadPseudoHighlightOverride) {
            documentBody?.classList.remove(kPseudoHighlightSupportClass);
        }
        const computedStyle =
            getDocumentWindow(styleSource)?.getComputedStyle(styleSource);
        const backgroundColor = computedStyle?.backgroundColor;
        const color = computedStyle?.color;
        if (hadPseudoHighlightOverride) {
            documentBody?.classList.add(kPseudoHighlightSupportClass);
        }

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
            console.error(
                "AudioTextHighlightManager.makeRange() could not find ownerDocument for a highlighted node.",
            );
            return undefined;
        }

        const range = ownerDocument.createRange();
        range.selectNodeContents(node);
        return range;
    }
}
