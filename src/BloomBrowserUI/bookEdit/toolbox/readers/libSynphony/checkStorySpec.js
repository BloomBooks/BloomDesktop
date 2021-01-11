import { theOneLibSynphony, setLangData } from "./synphony_lib";
import _ from "underscore";

describe("Check Story", function() {
    const lettersInLanguage = [
        "a",
        "b",
        "c",
        "d",
        "e",
        "f",
        "g",
        "h",
        "i",
        "j",
        "k",
        "l",
        "m",
        "n",
        "o",
        "p",
        "q",
        "r",
        "s",
        "t",
        "u",
        "v",
        "w",
        "x",
        "y",
        "z",
        "ch",
        "'",
        "b'",
        "-",
        "aa",
        "ng"
    ];

    function generateTestData() {
        //reviewslog: changed from bare theOneLanguageDataInstance to window.theOneLanguageDataInstance
        setLangData({
            LangName: "",
            LangID: "",
            LanguageSortOrder: lettersInLanguage,
            ProductivityGPCSequence: [],
            Numbers: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9],
            GPCS: [
                {
                    GPC: "a",
                    GPCuc: "A",
                    Grapheme: "a",
                    Phoneme: "a",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "b",
                    GPCuc: "B",
                    Grapheme: "b",
                    Phoneme: "b",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "c",
                    GPCuc: "C",
                    Grapheme: "c",
                    Phoneme: "c",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "d",
                    GPCuc: "D",
                    Grapheme: "d",
                    Phoneme: "d",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "e",
                    GPCuc: "E",
                    Grapheme: "e",
                    Phoneme: "e",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "f",
                    GPCuc: "F",
                    Grapheme: "f",
                    Phoneme: "f",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "g",
                    GPCuc: "G",
                    Grapheme: "g",
                    Phoneme: "g",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "h",
                    GPCuc: "H",
                    Grapheme: "h",
                    Phoneme: "h",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "i",
                    GPCuc: "I",
                    Grapheme: "i",
                    Phoneme: "i",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "j",
                    GPCuc: "J",
                    Grapheme: "j",
                    Phoneme: "j",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "k",
                    GPCuc: "K",
                    Grapheme: "k",
                    Phoneme: "k",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "l",
                    GPCuc: "L",
                    Grapheme: "l",
                    Phoneme: "l",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "m",
                    GPCuc: "M",
                    Grapheme: "m",
                    Phoneme: "m",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "n",
                    GPCuc: "N",
                    Grapheme: "n",
                    Phoneme: "n",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "o",
                    GPCuc: "O",
                    Grapheme: "o",
                    Phoneme: "o",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "p",
                    GPCuc: "P",
                    Grapheme: "p",
                    Phoneme: "p",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "q",
                    GPCuc: "Q",
                    Grapheme: "q",
                    Phoneme: "q",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "r",
                    GPCuc: "R",
                    Grapheme: "r",
                    Phoneme: "r",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "s",
                    GPCuc: "S",
                    Grapheme: "s",
                    Phoneme: "s",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "t",
                    GPCuc: "T",
                    Grapheme: "t",
                    Phoneme: "t",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "u",
                    GPCuc: "U",
                    Grapheme: "u",
                    Phoneme: "u",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "v",
                    GPCuc: "V",
                    Grapheme: "v",
                    Phoneme: "v",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "w",
                    GPCuc: "W",
                    Grapheme: "w",
                    Phoneme: "w",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "x",
                    GPCuc: "X",
                    Grapheme: "x",
                    Phoneme: "x",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "y",
                    GPCuc: "Y",
                    Grapheme: "y",
                    Phoneme: "y",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "z",
                    GPCuc: "Z",
                    Grapheme: "z",
                    Phoneme: "z",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "ch",
                    GPCuc: "CH",
                    Grapheme: "ch",
                    Phoneme: "ch",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "'",
                    GPCuc: "'",
                    Grapheme: "'",
                    Phoneme: "'",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "b'",
                    GPCuc: "B'",
                    Grapheme: "b'",
                    Phoneme: "b'",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "-",
                    GPCuc: "-",
                    Grapheme: "-",
                    Phoneme: "-",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "aa",
                    GPCuc: "AA",
                    Grapheme: "aa",
                    Phoneme: "aa",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                },
                {
                    GPC: "ng",
                    GPCuc: "NG",
                    Grapheme: "ng",
                    Phoneme: "ng",
                    Category: "other",
                    Combining: "false",
                    Frequency: 1,
                    TokenFreq: 1,
                    IPA: "",
                    Alt: []
                }
            ],
            VocabularyGroupsDescriptions: [],
            VocabularyGroups: 1,
            group1: [],
            UseFullGPCNotation: false
        });
    }

    beforeEach(function() {
        generateTestData();
    });

    afterEach(function() {
        //
    });

    it("Validate letter combination separately from letters", function() {
        var inputText = "a bad cad chad ch,ad had d,ach d,ac";
        var knownGPCs = ["a", "b", "ch", "d", "n"];
        var results = theOneLibSynphony.checkStory(
            [],
            [],
            knownGPCs,
            inputText,
            ""
        );
        expect(results.possible_words.length).toBe(5); // a bad chad ch,ad d,ach
        expect(results.remaining_words.length).toBe(3); // cad had d,ac
    });

    it("Check letter combination and letters", function() {
        var inputText = "a bad cad chad ch,ad had d,ach d,ac";
        var knownGPCs = ["a", "b", "ch", "h", "d", "n"];
        var results = theOneLibSynphony.checkStory(
            [],
            [],
            knownGPCs,
            inputText,
            ""
        );
        expect(results.possible_words.length).toBe(6); // a bad chad ch,ad d,ach had
        expect(results.remaining_words.length).toBe(2); // cad d,ac
    });

    it("Check usage of single quote as GPC", function() {
        var inputText = "o'o 'obo bodo' cob";
        var knownGPCs = ["'", "b", "o", "d"];
        var results = theOneLibSynphony.checkStory(
            [],
            [],
            knownGPCs,
            inputText,
            ""
        );
        expect(results.possible_words.length).toBe(3); // o'o 'obo bodo'
        expect(results.remaining_words.length).toBe(1); // cobc
    });

    it("Check single quote in digraph, but not as single character", function() {
        var inputText = "o'o b'ob bob' ob'o cob";
        var knownGPCs = ["b", "b'", "o", "d"];
        var results = theOneLibSynphony.checkStory(
            [],
            [],
            knownGPCs,
            inputText,
            ""
        );
        expect(results.possible_words.length).toBe(3); // b'ob bob' ob'o
        expect(results.remaining_words.length).toBe(2); // o'o cob
    });

    // Enhance: This test passes, but only because I don't check the actual values returned
    // in possible_words. The exterior hyphens are stripped off of '-obo' and 'bodo-'.
    // Not sure why or how much trouble it'd be to fix. Also not sure it's a problem, since
    // hyphens are usually word internal things (except in linguistics).
    it("Check usage of hyphen as GPC", function() {
        var inputText = "o-o -obo bodo- cob d,oc";
        var knownGPCs = ["b", "-", "o", "d"];
        var results = theOneLibSynphony.checkStory(
            [],
            [],
            knownGPCs,
            inputText,
            ""
        );
        expect(results.possible_words.length).toBe(3); // o-o -obo bodo-
        expect(results.remaining_words.length).toBe(2); // cob d,oc
    });

    it("Check double letter combinations", function() {
        var inputText = "a and nad dan aa dad aand naad daan";
        var knownGPCs = ["a", "d", "n"];
        var results = theOneLibSynphony.checkStory(
            [],
            [],
            knownGPCs,
            inputText,
            ""
        );
        expect(results.possible_words.length).toBe(5); // a and nad dan dad
        expect(results.remaining_words.length).toBe(4); // aa aand naad daan
    });

    // BL-4720
    it("The only unknown letter is part of a known digraph", function() {
        var inputText = "a an ang gang ga nga ngag";
        var knownGPCs = _.without(lettersInLanguage, "g"); // g is the only unknown letter
        expect(_.contains(knownGPCs, "ng")).toBe(true); // make sure our test is set up correctly. 'ng' is known
        var results = theOneLibSynphony.checkStory(
            [],
            [],
            knownGPCs,
            inputText,
            ""
        );
        expect(results.possible_words.length).toBe(4); // a an ang nga
        expect(results.remaining_words.length).toBe(3); // gang ga ngag
    });
});
