import { beforeEach, describe, expect, it, vi } from "vitest";
import {
    cellLanguagesOfCollection,
    ICellLanguage,
    menuLanguages,
    roleState,
    rolesFromSetting,
    rolesOfGroup,
    showSettingInGroup,
    someGroupHasOneLanguage,
    toggleRole,
} from "./tableCellLanguages";
import { tableHistoryManager } from "bloom-table";
import { ILanguageNameValues } from "../toolbox/canvas/canvasControlTypes";

// Setting up a box needs the whole editing page (CKEditor, the toolbox); these tests are about
// which boxes are shown.
vi.mock("./translationGroupLanguages", async (importOriginal) => ({
    ...(await importOriginal<typeof import("./translationGroupLanguages")>()),
    setUpBoxesBuiltAfterLoad: vi.fn(),
}));
// A toggle is one of the table library's operations. The library's history is not what these tests
// are about, so its recording of an operation just performs it.
vi.mock("bloom-table", () => ({
    tableHistoryManager: {
        addHistoryEntry: vi.fn(
            (_table: HTMLElement, _label: string, perform: () => void) => {
                perform();
                return true;
            },
        ),
    },
}));

const kNames: ILanguageNameValues = {
    language1Name: "Tok Pisin",
    language1Tag: "tpi",
    language2Name: "English",
    language2Tag: "en",
    language3Name: "Spanish",
    language3Tag: "es",
};
const kLanguages: ICellLanguage[] = cellLanguagesOfCollection(kNames);

// Make the page's settings (GetSettings, which C# injects) describe the collection `names`, with
// `toolbarTags` ticked in the toolbar (by default its first language only), and return its
// languages as the menu lists them. Which boxes a cell shows comes from these settings, as at page
// load, so they must describe the same collection as the menu.
function useCollection(
    names: ILanguageNameValues,
    toolbarTags: string[] = [names.language1Tag],
): ICellLanguage[] {
    (globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
        languageForNewTextBoxes: toolbarTags[0],
        currentCollectionLanguage2: toolbarTags[1],
        currentCollectionLanguage3: toolbarTags[2],
        collectionLanguage1: names.language1Tag,
        metadataLanguage1: names.language2Tag,
        metadataLanguage2: names.language3Tag,
    });
    return cellLanguagesOfCollection(names);
}

// A text cell's group the way Bloom saves it: an editable for each of the book's languages,
// with only the ones the cell shows marked visible.
function makeGroup(setting: string | null, visibleTags: string[]): HTMLElement {
    const group = document.createElement("div");
    group.className = "bloom-translationGroup normal-style";
    if (setting !== null) group.setAttribute("data-default-languages", setting);
    for (const tag of ["tpi", "en", "es"]) {
        const editable = document.createElement("div");
        editable.className = "bloom-editable normal-style";
        if (visibleTags.includes(tag))
            editable.classList.add("bloom-visibility-code-on");
        editable.setAttribute("lang", tag);
        editable.innerHTML = `<p>${tag} text</p>`;
        group.appendChild(editable);
    }
    document.body.appendChild(group);
    return group;
}

function visibleTagsOf(group: HTMLElement): string[] {
    return Array.from(
        group.querySelectorAll<HTMLElement>(
            ":scope > .bloom-editable.bloom-visibility-code-on",
        ),
    ).map((editable) => editable.getAttribute("lang")!);
}

beforeEach(() => {
    document.body.innerHTML = "";
    useCollection(kNames);
});

describe("cellLanguagesOfCollection", () => {
    it("lists the collection's L1, L2 and L3 in role order, with their Collection Settings labels", () => {
        expect(
            kLanguages.map(
                (language) =>
                    `${language.role} ${language.shortName} - ${language.name} (${language.tooltipEnglish})`,
            ),
        ).toEqual([
            "CL1 L1 - Tok Pisin (Local Language)",
            "N1 L2 - English (Language 2 (e.g. National Language))",
            "N2 L3 - Spanish (Language 3 (e.g. Regional Language)   (Optional))",
        ]);
    });

    it("keeps a role whose language repeats L1 but leaves out a missing L3, and the menu lists the language once", () => {
        const languages = cellLanguagesOfCollection({
            language1Name: "English",
            language1Tag: "en",
            language2Name: "English",
            language2Tag: "en",
        });
        expect(languages.map((language) => language.role)).toEqual([
            "CL1",
            "N1",
        ]);
        expect(
            menuLanguages(languages).map((language) => language.role),
        ).toEqual(["CL1"]);
    });

    it("still shows a cell whose stored role's language repeats L1, after L1 is ticked and unticked", () => {
        // The cell was set to L2 in a collection where L2 was another language.
        const languages = useCollection({
            language1Name: "English",
            language1Tag: "en",
            language2Name: "English",
            language2Tag: "en",
        });
        const group = makeGroup("N1", ["en"]);

        toggleRole([group], "CL1", languages);
        toggleRole([group], "CL1", languages);

        expect(group.getAttribute("data-default-languages")).toBe("N1");
        expect(visibleTagsOf(group)).toEqual(["en"]);
    });
});

describe("rolesFromSetting", () => {
    it("reads a list in role order whatever order it was written in", () => {
        expect(rolesFromSetting("N2,CL1")).toEqual(["CL1", "N2"]);
        expect(rolesFromSetting("N1")).toEqual(["N1"]);
        expect(rolesFromSetting("CL1, N1")).toEqual(["CL1", "N1"]);
    });

    it("reads CL2 and CL3 as N1 and N2", () => {
        expect(rolesFromSetting("CL3 CL2")).toEqual(["N1", "N2"]);
    });

    it("reads no setting, auto and the toolbar's roles as none of the menu's roles", () => {
        expect(rolesFromSetting(null)).toEqual([]);
        expect(rolesFromSetting("")).toEqual([]);
        expect(rolesFromSetting("auto")).toEqual([]);
        expect(rolesFromSetting("V")).toEqual([]);
        expect(rolesFromSetting("V,L2,N1")).toEqual(["N1"]);
    });
});

describe("showSettingInGroup", () => {
    it("stores the setting and shows exactly their languages", () => {
        const group = makeGroup("CL1", ["tpi"]);
        expect(visibleTagsOf(group)).toEqual(["tpi"]);

        const { added } = showSettingInGroup(group, "CL1,N2");

        expect(added).toEqual([]);
        expect(group.getAttribute("data-default-languages")).toBe("CL1,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi", "es"]);
    });

    it("adds an editable for a language a new cell does not have yet, in role order, and returns it to be set up", () => {
        // A cell made since the page loaded has only its first language's editable.
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        group.innerHTML =
            "<div class='bloom-editable normal-style bloom-visibility-code-on' lang='tpi' contenteditable='true'><p>hi</p></div>";
        document.body.appendChild(group);

        const { added: wired } = showSettingInGroup(group, "CL1,N2,N1");

        const editables = Array.from(
            group.querySelectorAll<HTMLElement>(":scope > .bloom-editable"),
        );
        expect(
            editables.map((editable) => editable.getAttribute("lang")),
        ).toEqual(["tpi", "en", "es"]);
        expect(wired.map((editable) => editable.getAttribute("lang"))).toEqual([
            "en",
            "es",
        ]);
        // The new ones are not yet typable; setUpBoxesBuiltAfterLoad does that.
        expect(wired[0].hasAttribute("contenteditable")).toBe(false);
        expect(wired[0].classList.contains("normal-style")).toBe(true);
        expect(visibleTagsOf(group)).toEqual(["tpi", "en", "es"]);
    });

    it("stores a setting exactly as given", () => {
        const group = makeGroup(null, ["tpi"]);

        showSettingInGroup(group, "N1,CL1");

        expect(group.getAttribute("data-default-languages")).toBe("N1,CL1");
        expect(visibleTagsOf(group)).toEqual(["tpi", "en"]);
    });

    it("shows the languages of CL2 and CL3, which are N1's and N2's", () => {
        const group = makeGroup("CL1", ["tpi"]);

        showSettingInGroup(group, "CL2,CL3");

        expect(visibleTagsOf(group)).toEqual(["en", "es"]);
    });

    // Such settings reach a cell when they are copied from a cell of an older table (Paste
    // properties, Paint format, a new row beside it). The cell shows what page load will show.
    it("shows a setting the menu does not read as page load would: V the toolbar's first language, no setting the toolbar's languages", () => {
        // The toolbar ticks English, then Tok Pisin.
        useCollection(kNames, ["en", "tpi"]);
        const fromV = makeGroup("CL1", ["tpi"]);
        const fromNoSetting = makeGroup("CL1", ["tpi"]);
        const fromVAndN2 = makeGroup("CL1", ["tpi"]);

        showSettingInGroup(fromV, "V");
        showSettingInGroup(fromNoSetting, null);
        showSettingInGroup(fromVAndN2, "V,N2");

        expect(visibleTagsOf(fromV)).toEqual(["en"]);
        expect(visibleTagsOf(fromNoSetting)).toEqual(["tpi", "en"]);
        expect(visibleTagsOf(fromVAndN2)).toEqual(["en", "es"]);
    });
});

describe("toggleRole", () => {
    it("ticks a language in every cell of a column, then unticks it, keeping the text", () => {
        const groups = [
            makeGroup("CL1", ["tpi"]),
            makeGroup("CL1,N1", ["tpi", "en"]),
        ];
        expect(roleState(groups, "N1", kLanguages)).toBe("some");

        toggleRole(groups, "N1", kLanguages);

        expect(roleState(groups, "N1", kLanguages)).toBe("all");
        expect(groups.map(visibleTagsOf)).toEqual([
            ["tpi", "en"],
            ["tpi", "en"],
        ]);

        toggleRole(groups, "N1", kLanguages);

        expect(roleState(groups, "N1", kLanguages)).toBe("none");
        expect(groups.map(visibleTagsOf)).toEqual([["tpi"], ["tpi"]]);
        // Hiding a language does not throw its text away.
        expect(groups[1].querySelector("[lang='en']")?.textContent).toBe(
            "en text",
        );
    });

    it("will not take the last language out of a cell", () => {
        const groups = [
            makeGroup("CL1", ["tpi"]),
            makeGroup("CL1,N1", ["tpi", "en"]),
        ];
        expect(roleState(groups, "CL1", kLanguages)).toBe("all");
        expect(someGroupHasOneLanguage(groups, kLanguages)).toBe(true);

        toggleRole(groups, "CL1", kLanguages);

        // The second cell could lose L1, but the first could not; the box is locked, and a
        // toggle that gets through anyway leaves the first cell alone.
        expect(rolesOfGroup(groups[0])).toEqual(["CL1"]);
    });

    it("ticks L2 in a cell stored as CL2, and writes N1 when unticking L3 there", () => {
        const group = makeGroup("CL2,CL3", ["en", "es"]);
        expect(roleState([group], "N1", kLanguages)).toBe("all");

        toggleRole([group], "N2", kLanguages);

        expect(group.getAttribute("data-default-languages")).toBe("N1");
        expect(visibleTagsOf(group)).toEqual(["en"]);
    });

    // Tables made before CL1 existed may store no setting or V. The menu ticks nothing for such a
    // cell, and its first change gives the cell a setting the menu reads.
    it("ticks nothing for a cell with no setting or V, and its first tick replaces that setting", () => {
        const groups = [makeGroup(null, ["tpi"]), makeGroup("V", ["tpi"])];
        expect(roleState(groups, "CL1", kLanguages)).toBe("none");

        toggleRole(groups, "CL1", kLanguages);

        expect(
            groups.map((group) => group.getAttribute("data-default-languages")),
        ).toEqual(["CL1", "CL1"]);
        expect(groups.map(visibleTagsOf)).toEqual([["tpi"], ["tpi"]]);
    });

    // Undo then puts the whole table back as it was, as for every table operation, so it finds the
    // right cells whatever has been added to the page since.
    it("is one operation of the table library, on the cells' table", () => {
        const table = document.createElement("div");
        table.className = "bloom-table";
        document.body.appendChild(table);
        const groups = [makeGroup("CL1", ["tpi"]), makeGroup("CL1", ["tpi"])];
        groups.forEach((group) => table.appendChild(group));
        const record = vi.mocked(tableHistoryManager.addHistoryEntry);
        record.mockClear();

        toggleRole(groups, "N2", kLanguages);

        expect(record).toHaveBeenCalledTimes(1);
        expect(record.mock.calls[0][0]).toBe(table);
        expect(
            groups.map((group) => group.getAttribute("data-default-languages")),
        ).toEqual(["CL1,N2", "CL1,N2"]);
    });

    it("records nothing when no cell would change", () => {
        const record = vi.mocked(tableHistoryManager.addHistoryEntry);
        record.mockClear();

        // L1 is the only language of this cell, so it cannot be unticked.
        toggleRole([makeGroup("CL1", ["tpi"])], "CL1", kLanguages);

        expect(record).not.toHaveBeenCalled();
    });
});

describe("a cell that stores a role the collection no longer gives a language", () => {
    // The cell was set to L1 and L3; the collection has since dropped its third language.
    let languages: ICellLanguage[];
    beforeEach(() => {
        languages = useCollection({
            language1Name: "Tok Pisin",
            language1Tag: "tpi",
            language2Name: "English",
            language2Tag: "en",
        });
    });

    it("draws the menu's boxes from the languages the cell still shows", () => {
        expect(languages.map((language) => language.role)).toEqual([
            "CL1",
            "N1",
        ]);
        const group = makeGroup("CL1,N2", ["tpi"]);

        expect(roleState([group], "CL1", languages)).toBe("all");
        expect(roleState([group], "N1", languages)).toBe("none");
        expect(someGroupHasOneLanguage([group], languages)).toBe(true);
    });

    it("lets the user tick a replacement language, keeping the stored role", () => {
        const group = makeGroup("CL1,N2", ["tpi"]);

        toggleRole([group], "N1", languages);

        expect(group.getAttribute("data-default-languages")).toBe("CL1,N1,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi", "en"]);
    });

    it("lets the user tick a language in a cell whose only role has none", () => {
        const group = makeGroup("N2", []);
        expect(roleState([group], "CL1", languages)).toBe("none");

        toggleRole([group], "CL1", languages);

        expect(group.getAttribute("data-default-languages")).toBe("CL1,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi"]);
    });
});
