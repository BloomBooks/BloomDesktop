import { describe, it, expect, beforeEach, afterEach, beforeAll } from "vitest";
import { theOneLibSynphony } from "./synphony_lib";
import { LibSynphony } from "./synphony_lib.js";
import "./bloomSynphonyExtensions";

describe("Unicode Standards", function () {
    beforeEach(function () {
        //
    });

    afterEach(function () {
        //
    });

    /**
     * UAX29 tests are based on the following document:
     * http://www.unicode.org/reports/tr29/#Sentence_Boundaries
     */
    it("Test UAX29, Cases SB1 and SB2", function () {
        // Break at the start and end of text.
        var inputText = "this is a block of text to test";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);
    });

    it("Test UAX29, Case SB3", function () {
        // Do not break within CRLF.
        var inputText = "This is\r\na block of\r\ntext to test.";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(3);
    });

    it("Test UAX29, Case SB4", function () {
        // Break after paragraph separators.
        var inputText =
            "Sentence 1\rSentence 2\nSentence 3\u0085Sentence 4\u2028Sentence 5\u2029Sentence 6\r\nSentence 7<br>Sentence 8";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(8);
    });

    it("Test UAX29, Case SB5", function () {
        // Ignore Format and Extend characters, except when they appear at the beginning of a region of text.
        var inputText =
            "Thi\u200cs is\r\na blo\u2063ck of\r\nte\u202axt to t\u200dest.";
        var fragments = theOneLibSynphony.stringToSentences(inputText);

        //for (var i = 0; i < fragments.length; i++)
        //	jstestdriver.console.debug(i, fragments[i].text);

        expect(fragments.length).toBe(3);
    });

    it("Test UAX29, Case SB6", function () {
        // Do not break after ambiguous terminators like period if they are immediately followed by a number or lowercase letter.
        var inputText = "This is test sentence .0 .a";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);
    });

    it("Test UAX29, Case SB7", function () {
        // Do not break after ambiguous terminators like period if they are between uppercase letters.
        var inputText = "This is test sentence U.S.A.";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);
    });

    it("Test UAX29, Case SB8", function () {
        // Do not break after ambiguous terminators like period if the first following letter (optionally after certain punctuation) is lowercase.
        var inputText =
            "This is test sentence. this is a continuation of the sentence.";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);

        var inputText =
            "This is test (sentence.)' this is a continuation of the sentence.";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);

        inputText =
            "This is test (sentence.)' '(this is a continuation) of the sentence.";
        fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);
    });

    it("Test UAX29, Case SB8a", function () {
        // Do not break after ambiguous terminators like period if they are followed by “continuation” punctuation such as comma, colon, or semicolon.
        var inputText =
            "This is test sentence., This is a continuation of the sentence.";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);

        inputText =
            "This is test sentence. : This is a continuation of the sentence.";
        fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(1);
    });

    it("Test UAX29, Case SB9", function () {
        // Break after sentence terminators, but include closing punctuation
        var inputText = "This is a test sentence.)'\"";
        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments[0].text).toBe(inputText);
    });

    it("Test UAX29, Case SB10 and SB11", function () {
        // Do not break within sentence-ending sequences
        var inputText =
            'This is (sentence 1.)\'"\u201d "This is sentence 2."\r\n<br /> This is sentence 3.\r\n';
        var result1 = "This is (sentence 1.)'\"\u201d";
        var result2 = " ";
        var result3 = '"This is sentence 2."';
        var result4 = "\r\n<br />";
        var result5 = " This is sentence 3.";
        var result6 = "\r\n";

        var fragments = theOneLibSynphony.stringToSentences(inputText);
        expect(fragments.length).toBe(6);
        expect(fragments[0].text).toBe(result1);
        expect(fragments[1].text).toBe(result2);
        expect(fragments[2].text).toBe(result3);
        expect(fragments[3].text).toBe(result4);
        expect(fragments[4].text).toBe(result5);
        expect(fragments[5].text).toBe(result6);
    });
});
