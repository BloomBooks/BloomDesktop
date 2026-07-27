// Test-only support for the code that paints text highlights through the CSS custom highlight
// registry (see bookEdit/js/textHighlightManager.ts). jsdom implements neither CSS.highlights nor
// Highlight, so specs install these stand-ins in the window under test and then read back what
// the production code registered.

export class FakeHighlight {
    public ranges: Range[];
    public priority?: number;

    public constructor(...ranges: Range[]) {
        this.ranges = ranges;
    }
}

export type FakeHighlightRegistry = Map<string, FakeHighlight>;

type WindowWithHighlightApis = Window & {
    CSS?: { highlights?: FakeHighlightRegistry };
    Highlight?: typeof FakeHighlight;
};

// Give targetWindow an empty highlight registry and a Highlight constructor, and return the
// registry so the test can inspect it.
export function installHighlightPolyfill(
    targetWindow: Window,
): FakeHighlightRegistry {
    const windowWithHighlightApis = targetWindow as WindowWithHighlightApis;
    if (!windowWithHighlightApis.CSS) {
        windowWithHighlightApis.CSS = {};
    }
    const registry = new Map<string, FakeHighlight>();
    windowWithHighlightApis.CSS.highlights = registry;
    windowWithHighlightApis.Highlight = FakeHighlight;
    return registry;
}

// The registry installed in targetWindow. Fails loudly rather than silently reporting "no
// highlights" if the test forgot to install the polyfill.
export function getHighlightRegistry(
    targetWindow: Window,
): FakeHighlightRegistry {
    const registry = (targetWindow as WindowWithHighlightApis).CSS?.highlights;
    if (!registry) {
        throw new Error(
            "Expected CSS.highlights test polyfill to be installed",
        );
    }
    return registry;
}

// The text each Range of the named highlight covers, in the order the production code registered
// them. Empty if nothing is painted under that name.
export function getHighlightTexts(
    targetWindow: Window,
    highlightName: string,
): string[] {
    const highlight = getHighlightRegistry(targetWindow).get(highlightName);
    return highlight ? highlight.ranges.map((range) => range.toString()) : [];
}
