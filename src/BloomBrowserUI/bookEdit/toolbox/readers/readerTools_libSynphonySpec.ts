import {ReaderToolsModel} from "./readerToolsModel";
import {theOneLanguageDataInstance,   ResetLanguageDataInstance}  from './libSynphony/synphony_lib';
import * as _ from 'underscore'
import ReadersSynphonyWrapper from './ReadersSynphonyWrapper';

describe("readerTools-libSynphony tests", function() {

    function generateTestData() {

        //reviewslog this wasn't allowed  theOneLanguageDataInstance = null;
        ResetLanguageDataInstance();

        //so we need another way to clear out this global, for testing purposes
        ReaderToolsModel.clearForTest();

        var settings: any = {};
        settings.letters = 'a b c d e f g h i j k l m n o p q r s t u v w x y z th';
        settings.moreWords = 'one two three';
        settings.stages = [];

        settings.stages.push({"letters":"a c m r t","sightWords":"canine feline"});
        settings.stages.push({"letters":"d g o e s","sightWords":"carnivore omnivore"});
        settings.stages.push({"letters":"i l n th","sightWords":"rodent"});

        var sampleFileContents = 'The cat sat on the mat. The rat sat on the cat.';

        var synphony = new ReadersSynphonyWrapper();
        ReaderToolsModel.synphony = synphony;
        synphony.loadSettings(settings);

        ReaderToolsModel.addWordsFromFile(sampleFileContents);
        ReaderToolsModel.addWordsToSynphony();
        ReaderToolsModel.updateWordList();
    }

    function generateSightWordsOnlyTestData() {

       //reviewslog this wasn't allowed  theOneLanguageDataInstance = null;
       ResetLanguageDataInstance();

        //so we need another way to clear out this global, for testing purposes
        ReaderToolsModel.clearForTest();

        var settings: any = {};
        settings.stages = [];

        settings.stages.push({"letters":"","sightWords":"canine feline"});
        settings.stages.push({"letters":"","sightWords":"carnivore omnivore"});
        settings.stages.push({"letters":"","sightWords":"rodent"});

        var synphony = new ReadersSynphonyWrapper();
        ReaderToolsModel.synphony = synphony;
        synphony.loadSettings(settings);

        ReaderToolsModel.addWordsToSynphony();
        ReaderToolsModel.updateWordList();
    }

    function addDiv(id) {
        var div = document.createElement('div');
        div.id = id;
        document.body.appendChild(div);
        return div;
    }

    var divTextEntry1;
    var divTextEntry2;
    var divTextEntry3;

    beforeEach(function() {
        divTextEntry1 = addDiv('text_entry1');
        divTextEntry2 = addDiv('text_entry2');
        divTextEntry3 = addDiv('text_entry3');
    });

    afterEach(function() {
        document.body.removeChild(divTextEntry1);
        document.body.removeChild(divTextEntry2);
        document.body.removeChild(divTextEntry3);
    });

    it("addWordsFromFile", function() {

        ReaderToolsModel.clearForTest();
        var fileContents = 'The cat sat on the mat. The rat sat on the cat.';

        ReaderToolsModel.addWordsFromFile(fileContents);
        expect(ReaderToolsModel.allWords).toEqual({the: 4, cat: 2, sat: 2, on: 2, mat: 1, rat: 1});
    });

    it("addWordsToSynphony", function() {

        generateTestData();
        var synphony = ReaderToolsModel.synphony;

        expect(synphony.stages.length).toBe(3);
        ReaderToolsModel.setStageNumber(1);
        expect(_.pluck(ReaderToolsModel.getStageWords(), 'Name').sort()).toEqual(['cat', 'mat', 'rat']);
        ReaderToolsModel.setStageNumber(2);
        expect(_.pluck(ReaderToolsModel.getStageWords(), 'Name').sort()).toEqual(['cat', 'mat', 'rat', 'sat']);
        ReaderToolsModel.setStageNumber(3);
        expect(_.pluck(ReaderToolsModel.getStageWords(), 'Name').sort()).toEqual(['cat', 'mat', 'on', 'one', 'rat', 'sat', 'the', 'three']);

        expect(synphony.stages[0].sightWords).toEqual('canine feline');
        expect(synphony.stages[1].sightWords).toEqual('carnivore omnivore');
        expect(synphony.stages[2].sightWords).toEqual('rodent');
    });

    /**
     * Test for BL-223, div displaying markup if there is no text
     */
    it("markupEndsWithBreakTag", function() {

        generateTestData();

        var knownGraphemes = ['a','b','c','d','e','f','g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v','w','x','y','z'];
        var text1 = $('#text_entry1');

        // test empty div (just a <br>)
        text1.html('<br>').checkDecodableReader({knownGraphemes: knownGraphemes});
        expect(text1.html()).toEqual('<br>');

        // test end of text followed by <br>
        text1.html('Cat dog.<br>').checkDecodableReader({knownGraphemes: knownGraphemes});
        expect(text1.html()).toEqual('<span class="possible-word" data-segment="word">Cat</span> <span class="possible-word" data-segment="word">dog</span>.<br>');

        // test <br> in middle of text
        text1.html('Cat.<br>Dog.').checkDecodableReader({knownGraphemes: knownGraphemes});
        expect(text1.html()).toEqual('<span class="possible-word" data-segment="word">Cat</span>.<br><span class="possible-word" data-segment="word">Dog</span>.');

        text1.html('Cat<br>Dog.').checkDecodableReader({knownGraphemes: knownGraphemes});
        expect(text1.html()).toEqual('<span class="possible-word" data-segment="word">Cat</span><br><span class="possible-word" data-segment="word">Dog</span>.');
    });

    it("sightWordOnlyStages", function() {

        generateSightWordsOnlyTestData();

        var knownGraphemes = [];
        var text1 = $('#text_entry1');

        // test empty div (just a <br>)
        text1.html('<br>').checkDecodableReader({sightWords:["canine", "feline"]});
        expect(text1.html()).toEqual('<br>');

        // no sight words
        text1.html('Cat dog.').checkDecodableReader({sightWords:["canine", "feline"]});
        expect(text1.html()).toEqual('<span class="word-not-found" data-segment="word">Cat</span> <span class="word-not-found" data-segment="word">dog</span>.');

        // test one sight word
        text1.html('Canine Dog.').checkDecodableReader({sightWords:["canine", "feline"]});
        expect(text1.html()).toEqual('<span class="sight-word" data-segment="word">Canine</span> <span class="word-not-found" data-segment="word">Dog</span>.');

        text1.html('Canine feline').checkDecodableReader({sightWords:["canine", "feline"]});
        expect(text1.html()).toEqual('<span class="sight-word" data-segment="word">Canine</span> <span class="sight-word" data-segment="word">feline</span>');
    });
});
