import { describe, it, expect } from "vitest";

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
    it("records the alternate when the bubble data is readable", () => {
        // Bloom stores this JSON with backticks standing in for the quotes.
        const el = makeCanvasElement("{`version`:`1.0`,`tails`:[{`tipX`:1}]}");

        saveStateOfCanvasElementAsCurrentLangAlternate(el, kLang);

        const written = alternateOn(el);
        expect(written).not.toBeNull();
        expect(written).toContain("`lang`:`" + kLang + "`");
        expect(written).toContain("tipX");
    });

    // A canvas element whose bubble data cannot be read is a bug we want to hear about, so this
    // throws rather than quietly saving the page without that element's alternate.
    it.each([null, "{this is not json"])(
        "throws when the bubble data is %s",
        (dataBubble) => {
            const el = makeCanvasElement(dataBubble);

            expect(() =>
                saveStateOfCanvasElementAsCurrentLangAlternate(el, kLang),
            ).toThrow();
            expect(alternateOn(el)).toBeNull();
        },
    );
});
