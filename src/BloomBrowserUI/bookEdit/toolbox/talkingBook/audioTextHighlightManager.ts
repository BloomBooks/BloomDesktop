const kSegmentClass = "bloom-highlightSegment";
const kEnableHighlightClass = "ui-enableHighlight";
const kDisableHighlightClass = "ui-disableHighlight";
const kPostAudioSplitClass = "bloom-postAudioSplit";
const kTextBoxRecordingMode = "textbox";

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

        const registry = getHighlightRegistry(contextNode);
        const Highlight = getHighlightConstructor(contextNode);
        if (!registry || !Highlight) {
            return;
        }

        if (suppressCurrentHighlight) {
            allManagedHighlightNames.forEach((name) => registry.delete(name));
            return;
        }

        // Split highlights (blue segments after a textbox recording) and the current highlight
        // (yellow sentence) are mutually exclusive: split state replaces the yellow highlight.
        if (this.shouldShowSplitHighlights(currentHighlight, currentTextBox)) {
            registry.delete(currentHighlightName);
            this.refreshSplitHighlights(currentTextBox, registry, Highlight);
        } else {
            splitHighlightNames.forEach((name) => registry.delete(name));
            this.refreshCurrentHighlight(
                currentHighlight,
                currentTextBox,
                registry,
                Highlight,
            );
        }
    }

    private refreshCurrentHighlight(
        currentHighlight: Element | null,
        currentTextBox: HTMLElement | null,
        registry: HighlightRegistry,
        Highlight: HighlightConstructor,
    ): void {
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
        currentTextBox: HTMLElement,
        registry: HighlightRegistry,
        Highlight: HighlightConstructor,
    ): void {
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

    // Set the CSS variables that control the ::highlight colors to match the user's chosen
    // highlight color for the current text style, falling back to the default yellow.
    private updateCurrentHighlightColors(styleSource: Element): void {
        const documentElement = getDocumentElement(styleSource);
        if (!documentElement) {
            console.error(
                "AudioTextHighlightManager.updateCurrentHighlightColors() could not find documentElement for the style source.",
            );
            return;
        }

        const bloomEditable = styleSource.closest(".bloom-editable");
        const styleName = bloomEditable
            ? Array.from(bloomEditable.classList).find((c) =>
                  c.endsWith("-style"),
              )
            : undefined;

        const userColors = styleName
            ? this.getHighlightColorsFromUserStyles(
                  styleSource.ownerDocument,
                  styleName,
              )
            : undefined;

        documentElement.style.setProperty(
            kCurrentHighlightBackgroundCssVar,
            userColors?.backgroundColor ?? "#febf00",
        );
        documentElement.style.setProperty(
            kCurrentHighlightColorCssVar,
            userColors?.color ?? "black",
        );
    }

    // Look in the book's userModifiedStyles sheet for an audio highlight rule for the
    // given style name, and return its background-color and color if found.
    private getHighlightColorsFromUserStyles(
        doc: Document,
        styleName: string,
    ): { backgroundColor: string; color: string } | undefined {
        const userStyles = Array.from(doc.styleSheets).find(
            (s) =>
                (s.ownerNode as Element)?.getAttribute("title") ===
                "userModifiedStyles",
        );
        if (!userStyles) return undefined;

        try {
            for (const cssRule of Array.from(userStyles.cssRules)) {
                const rule = cssRule as CSSStyleRule;
                if (
                    rule.selectorText?.includes(styleName) &&
                    rule.selectorText?.includes("ui-audioCurrent") &&
                    rule.style.backgroundColor
                ) {
                    return {
                        backgroundColor: rule.style.backgroundColor,
                        color: rule.style.color || "black",
                    };
                }
            }
        } catch {
            // Stylesheets can throw on cross-origin access; shouldn't happen here
        }
        return undefined;
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
