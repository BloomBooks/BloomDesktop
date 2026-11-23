import { beforeEach, describe, expect, it, vi } from "vitest";

// The canvas text box's Language submenu stores V, N1 or N2 and names each entry for the language
// that setting shows, as page load will show it. These tests use the page's own rules for that
// (translationGroupLanguages.ts), with the page's settings made up here.

// The menu runs in the toolbox frame and reaches the page's code through its exports. The fake
// exports are filled in below, once the page's module has been imported normally: importing it
// from inside this factory would wait on the very module being mocked.
const pageExports = vi.hoisted(() => ({}) as Record<string, unknown>);
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...((await importOriginal()) as object),
    getEditablePageBundleExports: () => pageExports,
}));
vi.mock("./canvasElementPageBridge", () => ({
    getCanvasElementManager: () => undefined,
}));

import { makeLanguageMenuItem } from "./canvasControlTextMenuItems";
import { IControlContext, IControlMenuCommandRow } from "./canvasControlTypes";
import {
    addLanguageClassesToBox,
    languagesShownBySetting,
    showLanguagesInTranslationGroup,
} from "../../js/translationGroupLanguages";

Object.assign(pageExports, {
    languagesShownBySetting,
    showLanguagesInTranslationGroup,
    addLanguageClassesToBox,
    setUpBoxesBuiltAfterLoad: () => {},
});

const kNames = {
    language1Name: "Tok Pisin",
    language1Tag: "tpi",
    language2Name: "English",
    language2Tag: "en",
    language3Name: "Spanish",
    language3Tag: "es",
};

// Make the page's settings (GetSettings, which C# injects) describe a collection of Tok Pisin,
// English and Spanish, with `toolbarFirst` the first language ticked in the toolbar.
function useToolbarFirst(toolbarFirst: string): void {
    (globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
        languageForNewTextBoxes: toolbarFirst,
        collectionLanguage1: "tpi",
        metadataLanguage1: "en",
        metadataLanguage2: "es",
    });
}

// A canvas text box showing Tok Pisin, and the Language submenu's entries for it.
function makeMenu(): {
    group: HTMLElement;
    entries: IControlMenuCommandRow[];
} {
    const canvasElement = document.createElement("div");
    canvasElement.className = "bloom-canvas-element";
    canvasElement.innerHTML =
        "<div class='bloom-translationGroup' data-default-languages='V'>" +
        "<div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='tpi'><p>tpi</p></div>" +
        "</div>";
    document.body.appendChild(canvasElement);
    const ctx = {
        canvasElement,
        languageNameValues: kNames,
    } as unknown as IControlContext;
    return {
        group: canvasElement.querySelector<HTMLElement>(
            ".bloom-translationGroup",
        )!,
        entries: makeLanguageMenuItem(ctx)
            .subMenuItems as IControlMenuCommandRow[],
    };
}

function choose(entries: IControlMenuCommandRow[], label: string): void {
    const entry = entries.find((other) => other.englishLabel === label);
    if (!entry) {
        throw new Error(
            `No entry "${label}"; there are ${entries.map((other) => other.englishLabel).join(", ")}`,
        );
    }
    entry.onSelect(
        { canvasElement: document.body } as never,
        undefined as never,
    );
}

beforeEach(() => {
    document.body.innerHTML = "";
});

describe("the canvas text box's Language submenu", () => {
    it("lists the collection's languages, storing V, N1 and N2, when the toolbar's first language is the collection's", () => {
        useToolbarFirst("tpi");
        const { group, entries } = makeMenu();

        expect(entries.map((entry) => entry.englishLabel)).toEqual([
            "Tok Pisin",
            "English",
            "Spanish",
        ]);
        choose(entries, "English");
        expect(group.getAttribute("data-default-languages")).toBe("N1");
        choose(entries, "Tok Pisin");
        expect(group.getAttribute("data-default-languages")).toBe("V");
    });

    it("lists the toolbar's first language once, under the role that keeps it whatever the toolbar does", () => {
        // The toolbar puts English, the collection's second language, first: V and N1 both show it.
        useToolbarFirst("en");
        const { group, entries } = makeMenu();

        expect(entries.map((entry) => entry.englishLabel)).toEqual([
            "English",
            "Spanish",
        ]);
        choose(entries, "English");
        expect(group.getAttribute("data-default-languages")).toBe("N1");
        // It shows English at once, as page load will for N1.
        expect(
            Array.from(
                group.querySelectorAll(
                    ".bloom-editable.bloom-visibility-code-on",
                ),
            ).map((box) => box.getAttribute("lang")),
        ).toEqual(["en"]);
    });
});
