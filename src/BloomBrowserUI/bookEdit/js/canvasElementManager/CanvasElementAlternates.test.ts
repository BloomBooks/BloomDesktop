import { describe, it, expect, vi, beforeEach } from "vitest";

// Every test passes the language explicitly, so the collection settings this module would
// otherwise consult are never reached.
import { saveStateOfCanvasElementAsCurrentLangAlternate } from "./CanvasElementAlternates";

const kLang = "xyz";

function makeCanvasElement(dataBubble: string | null): HTMLElement {
    const el = document.createElement("div");
    el.className = "bloom-canvas-element";
    el.setAttribute("style", "left: 10px; top: 20px;");
    if (dataBubble !== null) el.setAttribute("data-bubble", dataBubble);
    const editable = document.createElement("div");
    editable.className = "bloom-editable";
    editable.setAttribute("lang", kLang);
    el.appendChild(editable);
    return el;
}

function alternateOn(canvasElement: HTMLElement): string | null {
    return canvasElement
        .getElementsByClassName("bloom-editable")[0]
        .getAttribute("data-bubble-alternate");
}

describe("saveStateOfCanvasElementAsCurrentLangAlternate", () => {
    beforeEach(() => {
        vi.spyOn(console, "warn").mockImplementation(() => {});
    });

    it("records the alternate when the bubble data is readable", () => {
        // Bloom stores this JSON with backticks standing in for the quotes.
        const el = makeCanvasElement("{`version`:`1.0`,`tails`:[{`tipX`:1}]}");

        saveStateOfCanvasElementAsCurrentLangAlternate(el, kLang);

        const written = alternateOn(el);
        expect(written).not.toBeNull();
        expect(written).toContain("`lang`:`" + kLang + "`");
        expect(written).toContain("tipX");
    });

    it("does not lose the whole page when a canvas element has no bubble data", () => {
        // The real hazard: this runs inside the clone gather, so throwing here does not merely
        // skip one alternate, it aborts gathering the page -- and then the page cannot be saved at
        // all. A missing attribute used to throw, because JSON.parse("") is an error.
        const el = makeCanvasElement(null);

        expect(() =>
            saveStateOfCanvasElementAsCurrentLangAlternate(el, kLang),
        ).not.toThrow();
        expect(alternateOn(el)).toBeNull();
    });

    it("does not lose the whole page when the bubble data is malformed", () => {
        const el = makeCanvasElement("{this is not json");

        expect(() =>
            saveStateOfCanvasElementAsCurrentLangAlternate(el, kLang),
        ).not.toThrow();
        expect(alternateOn(el)).toBeNull();
    });

    it("leaves an existing alternate alone rather than replacing it with an empty one", () => {
        // Recording an alternate with no tails would claim this language's copy has none, and the
        // user would lose them on switching to it. Skipping is the conservative choice.
        const el = makeCanvasElement("{broken");
        const editable = el.getElementsByClassName("bloom-editable")[0];
        editable.setAttribute("data-bubble-alternate", "{`lang`:`xyz`}");

        saveStateOfCanvasElementAsCurrentLangAlternate(el, kLang);

        expect(alternateOn(el)).toBe("{`lang`:`xyz`}");
    });
});
