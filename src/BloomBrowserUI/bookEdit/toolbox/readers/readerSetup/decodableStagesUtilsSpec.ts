import { describe, it, expect, beforeEach } from "vitest";
import {
    cleanSpaceDelimitedList,
    cloneReaderSettings,
    hasOnlyKnownGraphemes,
    prepareSettingsForSave,
} from "./decodableStagesUtils";
import { ReaderSettings, ReaderStage, ReaderLevel } from "../ReaderSettings";
import {
    LanguageData,
    theOneLanguageDataInstance,
} from "../libSynphony/synphony_lib";

/** Builds settings with one stage per supplied letters string. */
function makeSettings(options: {
    letters?: string;
    moreWords?: string;
    stages?: { letters: string; sightWords: string }[];
}): ReaderSettings {
    const settings = new ReaderSettings();
    settings.letters = options.letters ?? "";
    settings.moreWords = options.moreWords ?? "";
    settings.stages = (options.stages ?? []).map((source, index) => {
        const stage = new ReaderStage((index + 1).toString());
        stage.letters = source.letters;
        stage.sightWords = source.sightWords;
        return stage;
    });
    return settings;
}

describe("cleanSpaceDelimitedList", () => {
    it("turns commas into spaces", () => {
        expect(cleanSpaceDelimitedList("a,b,c")).toBe("a b c");
    });

    it("turns newlines (and CRLF) into spaces", () => {
        expect(cleanSpaceDelimitedList("cat\ndog")).toBe("cat dog");
        expect(cleanSpaceDelimitedList("cat\r\ndog")).toBe("cat dog");
    });

    it("collapses runs of spaces and trims the ends", () => {
        expect(cleanSpaceDelimitedList("  cat    dog  ")).toBe("cat dog");
    });

    it("leaves an already-clean list untouched", () => {
        expect(cleanSpaceDelimitedList("a b c")).toBe("a b c");
    });

    it("returns an empty string for whitespace-only input", () => {
        expect(cleanSpaceDelimitedList("  \n , \n ")).toBe("");
    });
});

describe("cloneReaderSettings", () => {
    it("copies stage and level objects so edits do not touch the original", () => {
        const settings = makeSettings({
            stages: [{ letters: "a b", sightWords: "the" }],
        });
        settings.levels = [new ReaderLevel("1")];

        // Sanity check the starting state, so a passing test cannot be a false positive.
        expect(settings.stages[0].letters).toBe("a b");

        const copy = cloneReaderSettings(settings);
        copy.stages[0].letters = "changed";
        copy.levels[0].name = "changed";

        expect(settings.stages[0].letters).toBe("a b");
        expect(settings.levels[0].name).toBe("1");
        expect(copy.stages[0]).not.toBe(settings.stages[0]);
        expect(copy.levels[0]).not.toBe(settings.levels[0]);
    });

    it("copies the scalar fields", () => {
        const settings = makeSettings({ letters: "a b", moreWords: "cat" });
        settings.useAllowedWords = 1;

        const copy = cloneReaderSettings(settings);

        expect(copy.letters).toBe("a b");
        expect(copy.moreWords).toBe("cat");
        expect(copy.useAllowedWords).toBe(1);
    });
});

describe("prepareSettingsForSave", () => {
    // Regression test: moreWords used to be saved verbatim. Because
    // ReadersSynphonyWrapper.loadSettings splits it on plain spaces, a value containing
    // newlines became one run-together vocabulary entry and the words silently stopped
    // counting as decodable.
    it("collapses newlines in the typed sample words", () => {
        const settings = makeSettings({ moreWords: "zebra\nquokka\n" });

        expect(settings.moreWords).toContain("\n"); // sanity check

        const saved = prepareSettingsForSave(settings);

        expect(saved.moreWords).toBe("zebra quokka");
        expect(saved.moreWords.split(" ")).toEqual(["zebra", "quokka"]);
    });

    it("cleans the alphabet and every stage's letters and sight words", () => {
        const settings = makeSettings({
            letters: " a,b  c ",
            stages: [
                { letters: " a,b ", sightWords: "the\nand" },
                { letters: "c", sightWords: " a  cat " },
            ],
        });

        const saved = prepareSettingsForSave(settings);

        expect(saved.letters).toBe("a b c");
        expect(saved.stages[0].letters).toBe("a b");
        expect(saved.stages[0].sightWords).toBe("the and");
        expect(saved.stages[1].sightWords).toBe("a cat");
    });

    it("does not modify the settings it was given", () => {
        const settings = makeSettings({ moreWords: "zebra\nquokka" });

        prepareSettingsForSave(settings);

        expect(settings.moreWords).toBe("zebra\nquokka");
    });
});

describe("hasOnlyKnownGraphemes", () => {
    const alphabet = ["a", "b", "c", "ch", "d", "o", "g", "t", "h", "s"];

    beforeEach(() => {
        // These fields are not declared on LanguageData; clear any left by another test.
        for (const field of [
            "AlwaysMatch",
            "SyllableBreak",
            "StressSymbol",
            "MorphemeBreak",
        ]) {
            delete theOneLanguageDataInstance[field];
        }
    });

    it("accepts a word built only from taught letters", () => {
        expect(hasOnlyKnownGraphemes("dog", alphabet, ["d", "o", "g"])).toBe(
            true,
        );
    });

    it("rejects a word using a letter that has not been taught yet", () => {
        expect(hasOnlyKnownGraphemes("dog", alphabet, ["d", "o"])).toBe(false);
    });

    it("prefers the longest grapheme, so 'ch' is one unit rather than 'c'+'h'", () => {
        // "chat" is decodable once ch/a/t are taught, even though "c" alone is not.
        expect(hasOnlyKnownGraphemes("chat", alphabet, ["ch", "a", "t"])).toBe(
            true,
        );
        // Teaching only c and h (not the digraph) must not make "chat" decodable.
        expect(
            hasOnlyKnownGraphemes("chat", alphabet, ["c", "h", "a", "t"]),
        ).toBe(false);
    });

    it("ignores case in both the word and the grapheme lists", () => {
        expect(hasOnlyKnownGraphemes("DOG", alphabet, ["D", "o", "g"])).toBe(
            true,
        );
    });

    it("allows symbols listed in the language's always-match fields", () => {
        theOneLanguageDataInstance["SyllableBreak"] = "-";

        expect(hasOnlyKnownGraphemes("do-g", alphabet, ["d", "o", "g"])).toBe(
            true,
        );
    });

    it("rejects that same symbol when the language does not define it", () => {
        expect(hasOnlyKnownGraphemes("do-g", alphabet, ["d", "o", "g"])).toBe(
            false,
        );
    });
});

// The dialog splits typed sample words into graphemes itself, while sample-text words are
// split by Synphony (LanguageData.getGpcForm). If the two ever disagree, the same word
// would be judged decodable by different rules depending on where it came from. These
// tests pin the dialog's splitter to Synphony's.
describe("grapheme splitting agrees with Synphony's own", () => {
    const alphabet = ["a", "b", "c", "ch", "d", "e", "g", "h", "o", "s", "t"];

    /** True when Synphony considers every grapheme of the word to be a known one. */
    function synphonySaysDecodable(word: string, knownGpcs: string[]): boolean {
        const languageData = new LanguageData();
        for (const grapheme of alphabet) languageData.addGrapheme(grapheme);
        const sortedGraphemes = alphabet
            .slice()
            .sort((first, second) => second.length - first.length);
        const gpcForm: string[] = languageData.getGpcForm(
            word.toLowerCase(),
            sortedGraphemes,
        );
        const known = knownGpcs.map((gpc) => gpc.toLowerCase());
        return gpcForm.every((gpc) => known.includes(gpc));
    }

    const cases: { word: string; known: string[] }[] = [
        { word: "dog", known: ["d", "o", "g"] },
        { word: "dog", known: ["d", "o"] },
        { word: "chat", known: ["ch", "a", "t"] },
        { word: "chat", known: ["c", "h", "a", "t"] },
        { word: "cheese", known: ["ch", "e", "s"] },
        { word: "the", known: ["t", "h", "e"] },
        { word: "bogus", known: ["b", "o", "g"] },
    ];

    // Guard against a vacuous suite: if every case were decodable, the comparisons above
    // could pass while the two implementations actually disagreed about rejection.
    it("exercises both decodable and non-decodable words", () => {
        const outcomes = cases.map((c) =>
            synphonySaysDecodable(c.word, c.known),
        );
        expect(outcomes).toContain(true);
        expect(outcomes).toContain(false);
    });

    for (const { word, known } of cases) {
        it(`agrees on "${word}" with [${known.join(",")}] taught`, () => {
            expect(hasOnlyKnownGraphemes(word, alphabet, known)).toBe(
                synphonySaysDecodable(word, known),
            );
        });
    }
});
