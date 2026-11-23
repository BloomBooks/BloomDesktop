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
    languagesShownBySetting,
    showLanguagesInTranslationGroup,
} from "../../js/translationGroupLanguages";

Object.assign(pageExports, {
    languagesShownBySetting,
    showLanguagesInTranslationGroup,
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

// A canvas text box showing Tok Pisin, and the Language submenu's entries for it; on a
// custom-layout page (a custom cover) when `customLayout`, where its box is marked as such a page
// marks the first language.
function makeMenu(customLayout = false): {
    group: HTMLElement;
    entries: IControlMenuCommandRow[];
} {
    const page = document.createElement("div");
    page.className = customLayout
        ? "bloom-page bloom-customLayout"
        : "bloom-page";
    const canvasElement = document.createElement("div");
    canvasElement.className = "bloom-canvas-element";
    canvasElement.innerHTML =
        "<div class='bloom-translationGroup' data-default-languages='V'>" +
        `<div class='bloom-editable bloom-content1 ${customLayout ? "bloom-contentFirst " : ""}bloom-visibility-code-on' lang='tpi'><p>tpi</p></div>` +
        "</div>";
    page.appendChild(canvasElement);
    document.body.appendChild(page);
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

    it("on a custom cover, marks every box of the text as page load would", () => {
        useToolbarFirst("tpi");
        const { group, entries } = makeMenu(true);
        const contentClassesOf = (lang: string) =>
            Array.from(
                group.querySelector(`.bloom-editable[lang='${lang}']`)!
                    .classList,
            ).filter((name) => name.startsWith("bloom-content"));
        // sanity check: the Tok Pisin box starts as the cover's first language
        expect(contentClassesOf("tpi")).toContain("bloom-contentFirst");

        choose(entries, "English");

        // English, now shown and set to N1, is the cover's second language...
        expect(contentClassesOf("en").sort()).toEqual([
            "bloom-contentNational1",
            "bloom-contentSecond",
        ]);
        // ...and the hidden Tok Pisin box is no longer marked as its first.
        expect(contentClassesOf("tpi").sort()).toEqual([
            "bloom-content1",
            "bloom-contentCL1",
        ]);
    });
});
