import { describe, it, expect } from "vitest";
import {
    cleanSpaceDelimitedList,
    cloneReaderSettings,
    hasOnlyKnownGraphemes,
    prepareSettingsForSave,
} from "./decodableStagesUtils";
import { ReaderSettings, ReaderStage, ReaderLevel } from "../ReaderSettings";
import { LanguageData } from "../libSynphony/synphony_lib";

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

    // The legacy dialog de-duplicated the typed words (_.uniq in readerSetup.io.ts).
    // Duplicates are not harmless: each copy is fed to LanguageData.addWord, which treats
    // repeats as a higher word frequency.
    it("removes duplicate sample words, as the legacy dialog did", () => {
        const settings = makeSettings({ moreWords: "cat\ndog\ncat\n dog " });

        const saved = prepareSettingsForSave(settings);

        expect(saved.moreWords).toBe("cat dog");
    });

    it("keeps distinct sample words in the order they were typed", () => {
        const settings = makeSettings({ moreWords: "dog cat bird" });

        expect(prepareSettingsForSave(settings).moreWords).toBe("dog cat bird");
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
    const noSymbols: string[] = [];

    it("accepts a word built only from taught letters", () => {
        expect(
            hasOnlyKnownGraphemes("dog", alphabet, ["d", "o", "g"], noSymbols),
        ).toBe(true);
    });

    it("rejects a word using a letter that has not been taught yet", () => {
        expect(
            hasOnlyKnownGraphemes("dog", alphabet, ["d", "o"], noSymbols),
        ).toBe(false);
    });

    it("prefers the longest grapheme, so 'ch' is one unit rather than 'c'+'h'", () => {
        // "chat" is decodable once ch/a/t are taught, even though "c" alone is not.
        expect(
            hasOnlyKnownGraphemes(
                "chat",
                alphabet,
                ["ch", "a", "t"],
                noSymbols,
            ),
        ).toBe(true);
        // Teaching only c and h (not the digraph) must not make "chat" decodable.
        expect(
            hasOnlyKnownGraphemes(
                "chat",
                alphabet,
                ["c", "h", "a", "t"],
                noSymbols,
            ),
        ).toBe(false);
    });

    it("ignores case in both the word and the grapheme lists", () => {
        expect(
            hasOnlyKnownGraphemes("DOG", alphabet, ["D", "o", "g"], noSymbols),
        ).toBe(true);
    });

    // These come from the toolbox frame via getSynphonyAlwaysMatchSymbols, because only that
    // frame's Synphony data defines them.
    it("allows a symbol the language marks as always matching", () => {
        expect(
            hasOnlyKnownGraphemes("do-g", alphabet, ["d", "o", "g"], ["-"]),
        ).toBe(true);
    });

    it("rejects that same symbol when the language does not define it", () => {
        expect(
            hasOnlyKnownGraphemes("do-g", alphabet, ["d", "o", "g"], noSymbols),
        ).toBe(false);
    });
});

// The dialog no longer carries its own copy of the word-splitting logic: hasOnlyKnownGraphemes
// segments through Synphony's LanguageData.getGpcForm, the same routine that segments the words
// Synphony loads from sample texts. These cases assert the resulting decodability decisions
// outright (rather than comparing the two, which would now be comparing one thing with itself),
// so they still fail if that wiring is broken or the sorting of multi-letter graphemes is lost.
describe("decodability decisions, via Synphony's segmentation", () => {
    const alphabet = ["a", "b", "c", "ch", "d", "e", "g", "h", "o", "s", "t"];
    const noSymbols: string[] = [];

    const cases: { word: string; known: string[]; expected: boolean }[] = [
        { word: "dog", known: ["d", "o", "g"], expected: true },
        { word: "dog", known: ["d", "o"], expected: false },
        // "ch" must be taken as one grapheme, so c+h alone is not enough...
        { word: "chat", known: ["ch", "a", "t"], expected: true },
        { word: "chat", known: ["c", "h", "a", "t"], expected: false },
        { word: "cheese", known: ["ch", "e", "s"], expected: true },
        // ...but "the" has no digraph in this alphabet, so t+h+e is enough.
        { word: "the", known: ["t", "h", "e"], expected: true },
        { word: "bogus", known: ["b", "o", "g"], expected: false },
    ];

    // Guard against a vacuous suite: a bug that made everything decodable (or nothing) would
    // otherwise be invisible if every case expected the same answer.
    it("covers both decodable and non-decodable words", () => {
        const expectations = cases.map((oneCase) => oneCase.expected);
        expect(expectations).toContain(true);
        expect(expectations).toContain(false);
    });

    for (const { word, known, expected } of cases) {
        it(`"${word}" with [${known.join(",")}] taught is ${expected ? "" : "not "}decodable`, () => {
            expect(
                hasOnlyKnownGraphemes(word, alphabet, known, noSymbols),
            ).toBe(expected);
        });
    }

    // Sanity check that we really are going through Synphony, by confirming its own segmentation
    // of one of the words above is what these expectations assume.
    it("relies on Synphony splitting 'chat' as ch + a + t", () => {
        const languageData = new LanguageData();
        const sortedGraphemes = alphabet
            .slice()
            .sort((first, second) => second.length - first.length);

        expect(languageData.getGpcForm("chat", sortedGraphemes)).toEqual([
            "ch",
            "a",
            "t",
        ]);
    });
});
