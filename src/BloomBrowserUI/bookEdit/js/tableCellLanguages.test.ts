import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
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
import { theOneUndoStack } from "../undo/UndoStack";

// Setting up a box needs the whole editing page (CKEditor, the toolbox); these tests are about
// which boxes are shown.
vi.mock("./translationGroupLanguages", async (importOriginal) => ({
    ...(await importOriginal<typeof import("./translationGroupLanguages")>()),
    setUpBoxesBuiltAfterLoad: vi.fn(),
}));

const kLanguages: ICellLanguage[] = cellLanguagesOfCollection({
    language1Name: "Tok Pisin",
    language1Tag: "tpi",
    language2Name: "English",
    language2Tag: "en",
    language3Name: "Spanish",
    language3Tag: "es",
});

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
    theOneUndoStack.clear();
});
afterEach(() => theOneUndoStack.clear());

describe("cellLanguagesOfCollection", () => {
    it("lists L1, L2 and L3 in role order", () => {
        expect(
            kLanguages.map(
                (language) =>
                    `${language.role} ${language.shortName} - ${language.name}`,
            ),
        ).toEqual(["V L1 - Tok Pisin", "N1 L2 - English", "N2 L3 - Spanish"]);
    });

    it("keeps a role whose language repeats L1 but leaves out a missing L3, and the menu lists the language once", () => {
        const languages = cellLanguagesOfCollection({
            language1Name: "English",
            language1Tag: "en",
            language2Name: "English",
            language2Tag: "en",
        });
        expect(languages.map((language) => language.role)).toEqual(["V", "N1"]);
        expect(
            menuLanguages(languages).map((language) => language.role),
        ).toEqual(["V"]);
    });

    it("still shows a cell whose stored role's language repeats L1, after L1 is ticked and unticked", () => {
        // The cell was set to L2 in a collection where L2 was another language.
        const languages = cellLanguagesOfCollection({
            language1Name: "English",
            language1Tag: "en",
            language2Name: "English",
            language2Tag: "en",
        });
        const group = makeGroup("N1", ["en"]);

        toggleRole([group], "V", languages);
        toggleRole([group], "V", languages);

        expect(group.getAttribute("data-default-languages")).toBe("N1");
        expect(visibleTagsOf(group)).toEqual(["en"]);
    });
});

describe("rolesFromSetting", () => {
    it("reads no setting, an empty one and auto as L1 only", () => {
        expect(rolesFromSetting(null)).toEqual(["V"]);
        expect(rolesFromSetting("")).toEqual(["V"]);
        expect(rolesFromSetting("auto")).toEqual(["V"]);
    });

    it("reads a list in role order whatever order it was written in", () => {
        expect(rolesFromSetting("N2,V")).toEqual(["V", "N2"]);
        expect(rolesFromSetting("N1")).toEqual(["N1"]);
        expect(rolesFromSetting("L1, N1")).toEqual(["V", "N1"]);
    });
});

describe("showSettingInGroup", () => {
    it("stores the setting and shows exactly their languages", () => {
        const group = makeGroup("V", ["tpi"]);
        expect(visibleTagsOf(group)).toEqual(["tpi"]);

        const { added } = showSettingInGroup(group, "V,N2", kLanguages);

        expect(added).toEqual([]);
        expect(group.getAttribute("data-default-languages")).toBe("V,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi", "es"]);
    });

    it("adds an editable for a language a new cell does not have yet, in role order, and returns it to be set up", () => {
        // A cell made since the page loaded has only its first language's editable.
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        group.innerHTML =
            "<div class='bloom-editable normal-style bloom-visibility-code-on' lang='tpi' contenteditable='true'><p>hi</p></div>";
        document.body.appendChild(group);

        const { added: wired } = showSettingInGroup(
            group,
            "V,N2,N1",
            kLanguages,
        );

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
});

describe("toggleRole", () => {
    it("ticks a language in every cell of a column, then unticks it, keeping the text", () => {
        const groups = [
            makeGroup("V", ["tpi"]),
            makeGroup("V,N1", ["tpi", "en"]),
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
            makeGroup("V", ["tpi"]),
            makeGroup("V,N1", ["tpi", "en"]),
        ];
        expect(roleState(groups, "V", kLanguages)).toBe("all");
        expect(someGroupHasOneLanguage(groups, kLanguages)).toBe(true);

        toggleRole(groups, "V", kLanguages);

        // The second cell could lose L1, but the first could not; the box is locked, and a
        // toggle that gets through anyway leaves the first cell alone.
        expect(rolesOfGroup(groups[0])).toEqual(["V"]);
    });

    it("is one undo step, which puts back the setting exactly as it was, and can be redone", () => {
        const groups = [makeGroup(null, ["tpi"]), makeGroup("V", ["tpi"])];

        toggleRole(groups, "N2", kLanguages);
        expect(
            groups.map((group) => group.getAttribute("data-default-languages")),
        ).toEqual(["V,N2", "V,N2"]);
        expect(theOneUndoStack.getEntryCount()).toBe(1);

        theOneUndoStack.undo();

        // The first cell had no setting at all, and gets none back.
        expect(groups[0].hasAttribute("data-default-languages")).toBe(false);
        expect(groups[1].getAttribute("data-default-languages")).toBe("V");
        expect(groups.map(visibleTagsOf)).toEqual([["tpi"], ["tpi"]]);

        theOneUndoStack.redo();

        expect(groups.map(visibleTagsOf)).toEqual([
            ["tpi", "es"],
            ["tpi", "es"],
        ]);
    });

    it("undoes and redoes after the cells were rebuilt, as a table Undo rebuilds them", () => {
        makeGroup(null, ["tpi"]);
        toggleRole([makeGroup("V", ["tpi"])], "N1", kLanguages);

        // Rebuilding from the markup, as the table library does when it undoes an operation,
        // leaves equal groups that are new elements.
        const before = document.body.innerHTML;
        document.body.innerHTML = before;
        const rebuilt = Array.from(
            document.querySelectorAll<HTMLElement>(".bloom-translationGroup"),
        );
        expect(rebuilt[1].getAttribute("data-default-languages")).toBe("V,N1");

        theOneUndoStack.undo();

        expect(rebuilt[1].getAttribute("data-default-languages")).toBe("V");
        expect(visibleTagsOf(rebuilt[1])).toEqual(["tpi"]);
        // The other group, before it on the page, is left alone.
        expect(rebuilt[0].hasAttribute("data-default-languages")).toBe(false);

        theOneUndoStack.redo();

        expect(rebuilt[1].getAttribute("data-default-languages")).toBe("V,N1");
        expect(visibleTagsOf(rebuilt[1])).toEqual(["tpi", "en"]);
    });
});

describe("showSettingInGroup", () => {
    it("leaves a group given no setting with none, showing L1 only", () => {
        const group = makeGroup("N2", ["es"]);

        showSettingInGroup(group, null, kLanguages);

        expect(group.hasAttribute("data-default-languages")).toBe(false);
        expect(visibleTagsOf(group)).toEqual(["tpi"]);
    });

    it("stores a setting exactly as given", () => {
        const group = makeGroup(null, ["tpi"]);

        showSettingInGroup(group, "N1,V", kLanguages);

        expect(group.getAttribute("data-default-languages")).toBe("N1,V");
        expect(visibleTagsOf(group)).toEqual(["tpi", "en"]);
    });
});

describe("a cell that stores a role the collection no longer gives a language", () => {
    // The cell was set to L1 and L3; the collection has since dropped its third language.
    const languages = cellLanguagesOfCollection({
        language1Name: "Tok Pisin",
        language1Tag: "tpi",
        language2Name: "English",
        language2Tag: "en",
    });

    it("draws the menu's boxes from the languages the cell still shows", () => {
        expect(languages.map((language) => language.role)).toEqual(["V", "N1"]);
        const group = makeGroup("V,N2", ["tpi"]);

        expect(roleState([group], "V", languages)).toBe("all");
        expect(roleState([group], "N1", languages)).toBe("none");
        expect(someGroupHasOneLanguage([group], languages)).toBe(true);
    });

    it("lets the user tick a replacement language, keeping the stored role", () => {
        const group = makeGroup("V,N2", ["tpi"]);

        toggleRole([group], "N1", languages);

        expect(group.getAttribute("data-default-languages")).toBe("V,N1,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi", "en"]);
    });

    it("shows L1 in a cell whose only role has none, and keeps it when the user ticks another", () => {
        const group = makeGroup("N2", []);
        expect(roleState([group], "V", languages)).toBe("all");
        expect(someGroupHasOneLanguage([group], languages)).toBe(true);

        toggleRole([group], "N1", languages);

        expect(group.getAttribute("data-default-languages")).toBe("V,N1,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi", "en"]);
    });
});
