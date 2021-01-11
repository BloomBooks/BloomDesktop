/**
 * numbers.test.js
 *
 * DESCRIPTION
 *
 * Created Jul 16, 2014 by Hopper
 *
 */

import { StoryCheckResults } from "./synphony_lib";
import _ from "underscore";

describe("NumberTests", function() {
    beforeEach(function() {
        //
    });

    afterEach(function() {
        //
    });

    it("latinNumerals", function() {
        var remainingWords = ["qwerty", "a1sdfg", "123", "12zxcvb", "456.789"];
        var storyResults = new StoryCheckResults(
            [],
            [],
            [],
            [],
            remainingWords,
            0,
            0
        );

        var numbers = storyResults.getNumbers();

        // check numbers
        expect(numbers.length).toBe(2);
        expect(numbers[0]).toBe("123");
        expect(numbers[1]).toBe("456.789");
    });

    it("arabicNumerals", function() {
        var remainingWords = ["qwerty", "٠asdfg", "٠١٢", "zxc٤vb", "٣٤٥٦٧٨٩"];
        var storyResults = new StoryCheckResults(
            [],
            [],
            [],
            [],
            remainingWords,
            0,
            0
        );

        var numbers = storyResults.getNumbers();

        // check numbers
        expect(numbers.length).toBe(2);
        expect(numbers[0]).toBe("٠١٢");
        expect(numbers[1]).toBe("٣٤٥٦٧٨٩");
    });

    it("devanagariNumerals", function() {
        var remainingWords = ["qwerty", "९asdfg९", "०१२", "zxcvb", "३४५६७८९"];
        var storyResults = new StoryCheckResults(
            [],
            [],
            [],
            [],
            remainingWords,
            0,
            0
        );

        var numbers = storyResults.getNumbers();

        // check numbers
        expect(numbers.length).toBe(2);
        expect(numbers[0]).toBe("०१२");
        expect(numbers[1]).toBe("३४५६७८९");
    });

    it("testRemainingWords", function() {
        var remainingWords = [
            "qwerty",
            "a1sdfg",
            "on",
            "123",
            "12zxcvb",
            "456.789"
        ];
        var storyResults = new StoryCheckResults(
            [],
            [],
            [],
            [],
            remainingWords,
            0,
            0
        );

        var numbers = storyResults.getNumbers();
        var badWords = _.difference(remainingWords, numbers);
        // check badWords
        expect(badWords.length).toBe(4);
        expect(badWords[0]).toBe("qwerty");
        expect(badWords[1]).toBe("a1sdfg");
        expect(badWords[2]).toBe("on");
        expect(badWords[3]).toBe("12zxcvb");
    });
});
