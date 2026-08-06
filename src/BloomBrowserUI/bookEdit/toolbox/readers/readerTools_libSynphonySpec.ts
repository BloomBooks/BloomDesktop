import { getTheOneReaderToolsModel } from "./readerToolsModel";
import {
    theOneLanguageDataInstance,
    ResetLanguageDataInstance,
} from "./libSynphony/synphony_lib";
import * as _ from "underscore";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import $ from "jquery";
import {
    kSightWordHighlight,
    kWordNotDecodableHighlight,
} from "./readerHighlights";
import {
    getHighlightTexts,
    installHighlightPolyfill,
} from "../../test/highlightTestSupport";

describe("readerTools-libSynphony tests", () => {
    function generateTestData() {
        //reviewslog this wasn't allowed  theOneLanguageDataInstance = null;
        ResetLanguageDataInstance();

        //so we need another way to clear out this global, for testing purposes
        getTheOneReaderToolsModel().clearForTest();

        const settings: any = {};
        settings.letters =
            "a b c d e f g h i j k l m n o p q r s t u v w x y z th";
        settings.moreWords = "one two three";
        settings.stages = [];

        settings.stages.push({
            letters: "a c m r t",
            sightWords: "canine feline",
        });
        settings.stages.push({
            letters: "d g o e s",
            sightWords: "carnivore omnivore",
        });
        settings.stages.push({ letters: "i l n th", sightWords: "rodent" });

        const sampleFileContents =
            "The cat sat on the mat. The rat sat on the cat.";

        const synphony = new ReadersSynphonyWrapper();
        getTheOneReaderToolsModel().synphony = synphony;
        synphony.loadSettings(settings);

        getTheOneReaderToolsModel().addWordsFromFile(sampleFileContents);
        getTheOneReaderToolsModel().addWordsToSynphony();
    }

    function generateSightWordsOnlyTestData() {
        //reviewslog this wasn't allowed  theOneLanguageDataInstance = null;
        ResetLanguageDataInstance();

        //so we need another way to clear out this global, for testing purposes
        getTheOneReaderToolsModel().clearForTest();

        const settings: any = {};
        settings.stages = [];

        settings.stages.push({ letters: "", sightWords: "canine feline" });
        settings.stages.push({ letters: "", sightWords: "carnivore omnivore" });
        settings.stages.push({ letters: "", sightWords: "rodent" });

        const synphony = new ReadersSynphonyWrapper();
        getTheOneReaderToolsModel().synphony = synphony;
        synphony.loadSettings(settings);

        getTheOneReaderToolsModel().addWordsToSynphony();
    }

    function addDiv(id) {
        const div = document.createElement("div");
        div.id = id;
        document.body.appendChild(div);
        return div;
    }

    let divTextEntry1;
    let divTextEntry2;
    let divTextEntry3;

    // The reader tools paint violations with ::highlight() pseudo-elements over Ranges rather
    // than by inserting spans, so this is how a test sees what got marked.
    function highlighted(highlightName: string): string[] {
        return getHighlightTexts(window, highlightName);
    }

    beforeEach(() => {
        installHighlightPolyfill(window);
        divTextEntry1 = addDiv("text_entry1");
        divTextEntry2 = addDiv("text_entry2");
        divTextEntry3 = addDiv("text_entry3");
    });

    afterEach(() => {
        document.body.removeChild(divTextEntry1);
        document.body.removeChild(divTextEntry2);
        document.body.removeChild(divTextEntry3);
    });

    it("addWordsFromFile", () => {
        getTheOneReaderToolsModel().clearForTest();
        const fileContents = "The cat sat on the mat. The rat sat on the cat.";

        getTheOneReaderToolsModel().addWordsFromFile(fileContents);
        expect(getTheOneReaderToolsModel().allWords).toEqual({
            the: 4,
            cat: 2,
            sat: 2,
            on: 2,
            mat: 1,
            rat: 1,
        });
    });

    it("addWordsFromFile properly handles paragraphs", () => {
        getTheOneReaderToolsModel().clearForTest();
        const fileContents = "one\r\ntwo\nthree four five.\r\n six. seven";

        getTheOneReaderToolsModel().addWordsFromFile(fileContents);
        expect(getTheOneReaderToolsModel().allWords).toEqual({
            one: 1,
            two: 1,
            three: 1,
            four: 1,
            five: 1,
            six: 1,
            seven: 1,
        });
    });

    /* skipping See BL-3554
                it("addWordsToSynphony", () => {

                        generateTestData();
                        var synphony = getTheOneReaderToolsModel().synphony;

                        expect(synphony.stages.length).toBe(3);
                        getTheOneReaderToolsModel().setStageNumber(1);
                        expect(_.pluck(getTheOneReaderToolsModel().getStageWords(), 'Name').sort()).toEqual(['cat', 'mat', 'rat']);
                        getTheOneReaderToolsModel().setStageNumber(2);
                        expect(_.pluck(getTheOneReaderToolsModel().getStageWords(), 'Name').sort()).toEqual(['cat', 'mat', 'rat', 'sat']);
                        getTheOneReaderToolsModel().setStageNumber(3);
                        expect(_.pluck(getTheOneReaderToolsModel().getStageWords(), 'Name').sort()).toEqual(['cat', 'mat', 'on', 'one', 'rat', 'sat', 'the', 'three']);

                        expect(synphony.stages[0].sightWords).toEqual('canine feline');
                        expect(synphony.stages[1].sightWords).toEqual('carnivore omnivore');
                        expect(synphony.stages[2].sightWords).toEqual('rodent');
                });
        */

    /**
     * Test for BL-223, div displaying markup if there is no text
     */
    it("markupEndsWithBreakTag", () => {
        generateTestData();

        const knownGraphemes = [
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
        ];
        const text1 = $("#text_entry1");

        // Every word here is decodable with the given graphemes but was not collected from the
        // sample texts, so nothing should be marked. (We used to mark these "possible words",
        // but that feature has long been disabled: it had neither a color nor a tooltip.)
        // What the original bug was about is that the markup must not disturb the HTML - and
        // now it never does, whatever it finds.
        const inputs = ["<br>", "Cat dog.<br>", "Cat.<br>Dog.", "Cat<br>Dog."];
        inputs.forEach((input) => {
            text1
                .html(input)
                .checkDecodableReader({ knownGraphemes: knownGraphemes });
            expect(text1.html()).toEqual(input);
            expect(highlighted(kWordNotDecodableHighlight)).toEqual([]);
            expect(highlighted(kSightWordHighlight)).toEqual([]);
        });
    });

    it("sightWordOnlyStages", () => {
        generateSightWordsOnlyTestData();

        const knownGraphemes = [];
        const text1 = $("#text_entry1");

        // test empty div (just a <br>)
        text1
            .html("<br>")
            .checkDecodableReader({ sightWords: ["canine", "feline"] });
        expect(text1.html()).toEqual("<br>");
        expect(highlighted(kWordNotDecodableHighlight)).toEqual([]);

        // no sight words
        text1
            .html("Cat dog.")
            .checkDecodableReader({ sightWords: ["canine", "feline"] });
        expect(text1.html()).toEqual("Cat dog.");
        expect(highlighted(kWordNotDecodableHighlight)).toEqual(["Cat", "dog"]);
        expect(highlighted(kSightWordHighlight)).toEqual([]);

        // test one sight word
        text1
            .html("Canine Dog.")
            .checkDecodableReader({ sightWords: ["canine", "feline"] });
        expect(text1.html()).toEqual("Canine Dog.");
        expect(highlighted(kSightWordHighlight)).toEqual(["Canine"]);
        expect(highlighted(kWordNotDecodableHighlight)).toEqual(["Dog"]);

        text1
            .html("Canine feline")
            .checkDecodableReader({ sightWords: ["canine", "feline"] });
        expect(text1.html()).toEqual("Canine feline");
        expect(highlighted(kSightWordHighlight)).toEqual(["Canine", "feline"]);
        expect(highlighted(kWordNotDecodableHighlight)).toEqual([]);
    });
});
