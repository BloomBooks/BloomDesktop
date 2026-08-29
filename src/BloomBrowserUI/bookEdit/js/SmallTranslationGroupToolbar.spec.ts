import { describe, it, expect, beforeEach, vi } from "vitest";

// bloomEditing, which the toolbar asks for the style editor that opens the format dialog,
// wants these when it is loaded. Nothing here runs any of the code that uses them.
(globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
    languageForNewTextBoxes: "xyz",
});
class NoopResizeObserver {
    public observe(): void {}
    public unobserve(): void {}
    public disconnect(): void {}
}
(globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver =
    NoopResizeObserver;

import { groupWantingToolbar } from "./SmallTranslationGroupToolbar";
import { getLanguageNameToShow } from "./bloomEditing";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { kTooSmallForInBoxAffordancesClass } from "./translationGroupSizeMarking";

/**
 * A translation group holding one editable, marked too small or not. Returns the editable,
 * which is what gets the focus and so what the toolbar decision is made from.
 */
function makeEditableInGroup(markedTooSmall: boolean): HTMLElement {
    const group = document.createElement("div");
    group.className = "bloom-translationGroup";
    if (markedTooSmall) group.classList.add(kTooSmallForInBoxAffordancesClass);
    const editable = document.createElement("div");
    editable.className = "bloom-editable";
    editable.setAttribute("contenteditable", "true");
    group.appendChild(editable);
    document.body.appendChild(group);
    return editable;
}

describe("groupWantingToolbar", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
    });

    it("gives the group of an editable in a group marked too small", () => {
        const editable = makeEditableInGroup(true);
        expect(groupWantingToolbar(editable)).toBe(editable.parentElement);
    });

    it("gives nothing for an editable in a group that is not marked", () => {
        const editable = makeEditableInGroup(false);
        expect(groupWantingToolbar(editable)).toBeUndefined();
    });

    it("finds the group from a paragraph inside the editable", () => {
        const editable = makeEditableInGroup(true);
        const paragraph = document.createElement("p");
        editable.appendChild(paragraph);
        expect(groupWantingToolbar(paragraph)).toBe(editable.parentElement);
    });

    it("gives nothing for something that is not in an editable at all", () => {
        makeEditableInGroup(true);
        const stray = document.createElement("div");
        document.body.appendChild(stray);
        expect(groupWantingToolbar(stray)).toBeUndefined();
    });

    it("gives nothing for a null target", () => {
        expect(groupWantingToolbar(null)).toBeUndefined();
    });
});

// The name the toolbar puts in its bar. It is worked out rather than read off
// data-languageTipContent, because AddLanguageTags never puts that attribute on a box
// narrower than 100px, which is most of what the toolbar serves: a calendar month grid's
// cells had no attribute to read, and the bar came up with no name in it.
describe("getLanguageNameToShow", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        vi.restoreAllMocks();
    });

    /** An editable of the given language, with no data-languageTipContent on it. */
    function makeEditable(lang: string | undefined): HTMLElement {
        const editable = document.createElement("div");
        editable.className = "bloom-editable";
        if (lang !== undefined) editable.setAttribute("lang", lang);
        document.body.appendChild(editable);
        expect(editable.hasAttribute("data-languageTipContent")).toBe(false);
        return editable;
    }

    it("gives the name the localization manager knows for the language", () => {
        vi.spyOn(theOneLocalizationManager, "getText").mockImplementation(
            (key: string) => (key === "tmh" ? "Tamajaq" : key),
        );
        expect(getLanguageNameToShow(makeEditable("tmh"))).toBe("Tamajaq");
    });

    it("falls back to the language code when no name is known", () => {
        expect(getLanguageNameToShow(makeEditable("xyz"))).toBe("xyz");
    });

    it("gives nothing for the prototype block language z", () => {
        expect(getLanguageNameToShow(makeEditable("z"))).toBeUndefined();
    });

    it("gives nothing for the placeholder language *", () => {
        expect(getLanguageNameToShow(makeEditable("*"))).toBeUndefined();
    });

    it("gives nothing for an editable with no lang at all", () => {
        expect(getLanguageNameToShow(makeEditable(undefined))).toBeUndefined();
    });

    it("gives nothing inside something that hides language names", () => {
        const editable = makeEditable("tmh");
        const page = document.createElement("div");
        page.className = "bloom-hideLanguageNameDisplay";
        document.body.appendChild(page);
        page.appendChild(editable);
        expect(getLanguageNameToShow(editable)).toBeUndefined();
    });
});
