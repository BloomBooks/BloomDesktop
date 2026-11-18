import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
    cellLanguagesOfCollection,
    ICellLanguage,
    menuLanguages,
    roleState,
    rolesFromSetting,
    rolesOfGroup,
    showRolesInGroup,
    showSettingInGroup,
    someGroupHasOneLanguage,
    toggleRole,
} from "./tableCellLanguages";
import { theOneUndoStack } from "../undo/UndoStack";

const kLanguages: ICellLanguage[] = cellLanguagesOfCollection(
    {
        language1Name: "Tok Pisin",
        language1Tag: "tpi",
        language2Name: "English",
        language2Tag: "en",
        language3Name: "Spanish",
        language3Tag: "es",
    },
    ["tpi"],
);

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

const noWiring = () => {};

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

    it("gives L1 the toolbar's first language, which need not be the collection's first", () => {
        const languages = cellLanguagesOfCollection(
            {
                language1Name: "Tok Pisin",
                language1Tag: "tpi",
                language2Name: "English",
                language2Tag: "en",
            },
            ["en"],
        );
        expect(
            languages.map(
                (language) =>
                    `${language.role} ${language.shortName} - ${language.name} (${language.tooltipEnglish})`,
            ),
        ).toEqual([
            "V L1 - English (Language 2 (e.g. National Language))",
            "N1 L2 - English (Language 2 (e.g. National Language))",
        ]);
        expect(
            menuLanguages(languages).map((language) => language.role),
        ).toEqual(["V"]);
    });

    it("keeps a role whose language repeats L1 but leaves out a missing L3, and the menu lists the language once", () => {
        const languages = cellLanguagesOfCollection(
            {
                language1Name: "English",
                language1Tag: "en",
                language2Name: "English",
                language2Tag: "en",
            },
            ["en"],
        );
        expect(languages.map((language) => language.role)).toEqual(["V", "N1"]);
        expect(
            menuLanguages(languages).map((language) => language.role),
        ).toEqual(["V"]);
    });

    it("still shows a cell whose stored role's language repeats L1, after L1 is ticked and unticked", () => {
        // The cell was set to L2 in a collection where L2 was another language.
        const languages = cellLanguagesOfCollection(
            {
                language1Name: "English",
                language1Tag: "en",
                language2Name: "English",
                language2Tag: "en",
            },
            ["en"],
        );
        const group = makeGroup("N1", ["en"]);

        toggleRole([group], "V", languages, noWiring);
        toggleRole([group], "V", languages, noWiring);

        expect(group.getAttribute("data-default-languages")).toBe("N1");
        expect(visibleTagsOf(group)).toEqual(["en"]);
    });
});

describe("rolesFromSetting", () => {
    it("reads no setting, an empty one and auto as the languages ticked in the toolbar", () => {
        // A bilingual book: the toolbar ticks Tok Pisin, then Spanish.
        const languages = cellLanguagesOfCollection(
            {
                language1Name: "Tok Pisin",
                language1Tag: "tpi",
                language2Name: "English",
                language2Tag: "en",
                language3Name: "Spanish",
                language3Tag: "es",
            },
            ["tpi", "es"],
        );
        expect(rolesFromSetting(null, languages)).toEqual(["V", "N2"]);
        expect(rolesFromSetting("", languages)).toEqual(["V", "N2"]);
        expect(rolesFromSetting("auto", languages)).toEqual(["V", "N2"]);
    });

    it("reads a list in role order whatever order it was written in", () => {
        expect(rolesFromSetting("N2,V", kLanguages)).toEqual(["V", "N2"]);
        expect(rolesFromSetting("N1", kLanguages)).toEqual(["N1"]);
        expect(rolesFromSetting("L1, N1", kLanguages)).toEqual(["V", "N1"]);
    });
});

describe("showRolesInGroup", () => {
    it("stores the roles and shows exactly their languages", () => {
        const group = makeGroup("V", ["tpi"]);
        expect(visibleTagsOf(group)).toEqual(["tpi"]);

        const added = showRolesInGroup(group, ["V", "N2"], kLanguages);

        expect(added).toEqual([]);
        expect(group.getAttribute("data-default-languages")).toBe("V,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi", "es"]);
    });

    it("adds an editable for a language a new cell does not have yet, in role order, and returns it for wiring", () => {
        // A cell made since the page loaded has only its first language's editable.
        const group = document.createElement("div");
        group.className = "bloom-translationGroup";
        group.innerHTML =
            "<div class='bloom-editable normal-style bloom-visibility-code-on' lang='tpi' contenteditable='true'><p>hi</p></div>";
        document.body.appendChild(group);

        const wired = showRolesInGroup(group, ["V", "N2", "N1"], kLanguages);

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
        // The new ones are not yet typable; the wiring does that.
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

        toggleRole(groups, "N1", kLanguages, noWiring);

        expect(roleState(groups, "N1", kLanguages)).toBe("all");
        expect(groups.map(visibleTagsOf)).toEqual([
            ["tpi", "en"],
            ["tpi", "en"],
        ]);

        toggleRole(groups, "N1", kLanguages, noWiring);

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

        toggleRole(groups, "V", kLanguages, noWiring);

        // The second cell could lose L1, but the first could not; the box is locked, and a
        // toggle that gets through anyway leaves the first cell alone.
        expect(rolesOfGroup(groups[0], kLanguages)).toEqual(["V"]);
    });

    it("is one undo step, which puts back the setting exactly as it was, and can be redone", () => {
        const groups = [makeGroup(null, ["tpi"]), makeGroup("V", ["tpi"])];

        toggleRole(groups, "N2", kLanguages, noWiring);
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
        toggleRole([makeGroup("V", ["tpi"])], "N1", kLanguages, noWiring);

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

describe("when the toolbar's first language is the collection's L2", () => {
    // V and N1 then name the same language, English, and the menu lists it once, as L1.
    const languages = cellLanguagesOfCollection(
        {
            language1Name: "Tok Pisin",
            language1Tag: "tpi",
            language2Name: "English",
            language2Tag: "en",
            language3Name: "Spanish",
            language3Tag: "es",
        },
        ["en"],
    );

    it("ticks L1 for a cell stored as N1, since it shows that language", () => {
        expect(
            menuLanguages(languages).map((language) => language.role),
        ).toEqual(["V", "N2"]);
        const group = makeGroup("N1", ["en"]);

        expect(roleState([group], "V", languages)).toBe("all");
        expect(someGroupHasOneLanguage([group], languages)).toBe(true);
    });

    it("unticking L1 hides the language even where N1 also names it", () => {
        const group = makeGroup("V,N1,N2", ["en", "es"]);
        expect(visibleTagsOf(group)).toEqual(["en", "es"]);

        toggleRole([group], "V", languages, noWiring);

        expect(group.getAttribute("data-default-languages")).toBe("N2");
        expect(visibleTagsOf(group)).toEqual(["es"]);
        expect(roleState([group], "V", languages)).toBe("none");
    });
});

describe("showSettingInGroup", () => {
    it("leaves a group given no setting with none, showing the toolbar's languages", () => {
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
    const languages = cellLanguagesOfCollection(
        {
            language1Name: "Tok Pisin",
            language1Tag: "tpi",
            language2Name: "English",
            language2Tag: "en",
        },
        ["tpi"],
    );

    it("draws the menu's boxes from the languages the cell still shows", () => {
        expect(languages.map((language) => language.role)).toEqual(["V", "N1"]);
        const group = makeGroup("V,N2", ["tpi"]);

        expect(roleState([group], "V", languages)).toBe("all");
        expect(roleState([group], "N1", languages)).toBe("none");
        expect(someGroupHasOneLanguage([group], languages)).toBe(true);
    });

    it("lets the user tick a replacement language, keeping the stored role", () => {
        const group = makeGroup("V,N2", ["tpi"]);

        toggleRole([group], "N1", languages, noWiring);

        expect(group.getAttribute("data-default-languages")).toBe("V,N1,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi", "en"]);
    });

    it("lets the user tick a language in a cell whose only role has none", () => {
        const group = makeGroup("N2", []);
        expect(roleState([group], "V", languages)).toBe("none");

        toggleRole([group], "V", languages, noWiring);

        expect(group.getAttribute("data-default-languages")).toBe("V,N2");
        expect(visibleTagsOf(group)).toEqual(["tpi"]);
    });
});
