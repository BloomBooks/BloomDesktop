describe("readerTools-libSynphony tests", function() {

    function generateTestData() {

        lang_data = null;
        var model = new ReaderToolsModel();

        var settings = {};
        settings.letters = 'a b c d e f g h i j k l m n o p q r s t u v w x y z';
        settings.letterCombinations = 'ai oo sh ng th ing';
        settings.moreWords = 'one two three';
        settings.stages = [];

        settings.stages.push({"letters":"a c m r t","sightWords":"canine feline"});
        settings.stages.push({"letters":"d g o e s","sightWords":"carnivore omnivore"});
        settings.stages.push({"letters":"i l n th","sightWords":"rodent"});

        var sampleFileContents = 'The cat sat on the mat. The rat sat on the cat.';

        var synphony = model.getSynphony();
        synphony.loadSettings(settings);

        model.addWordsFromFile(sampleFileContents);
        model.addWordsToSynphony();
        model.updateWordList();

        return model;
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

        var model = new ReaderToolsModel();
        var fileContents = 'The cat sat on the mat. The rat sat on the cat.';

        model.addWordsFromFile(fileContents);
        expect(model.allWords).toEqual({the: 4, cat: 2, sat: 2, on: 2, mat: 1, rat: 1});
    });

    it("addWordsToSynphony", function() {

        var model = generateTestData();
        var synphony = model.getSynphony();

        expect(synphony.stages.length).toBe(3);
        expect(_.pluck(model.getStageWords(1), 'Name')).toEqual(['cat', 'mat', 'rat']);
        expect(_.pluck(model.getStageWords(2), 'Name')).toEqual(['cat', 'sat', 'mat', 'rat']);
        expect(_.pluck(model.getStageWords(3), 'Name')).toEqual(['cat', 'sat', 'mat', 'rat', 'three', 'one', 'on', 'the']);

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
});
