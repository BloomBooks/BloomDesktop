describe("readerTools-libSynphony tests", function() {

    it("addWordsFromFile", function() {

        var model = new ReaderToolsModel();
        var fileContents = 'The cat sat on the mat. The rat sat on the cat.';

        model.addWordsFromFile(fileContents);
        expect(model.allWords).toEqual({the: 4, cat: 2, sat: 2, on: 2, mat: 1, rat: 1});
    });

    it("addWordsToSynphony", function() {

        lang_data = null;
        var model = new ReaderToolsModel();

        var settings = new Object();
        settings.letters = 'a b c d e f g h i j k l m n o p q r s t u v w x y z';
        settings.letterCombinations = 'ai oo sh ng th ing';
        settings.moreWords = 'one two three';
        settings.stages = [];

        settings.stages.push({"letters":"a c m r t","sightWords":"canine feline"});
        settings.stages.push({"letters":"d g o e s","sightWords":"carnivore omnivore"});
        settings.stages.push({"letters":"i l n th","sightWords":"rodent"});

        var sampleFileContents = 'The cat sat on the mat. The rat sat on the cat.';

        var synphony = model.getSynphony();
        synphony.loadSettings(JSON.stringify(settings));

        model.addWordsFromFile(sampleFileContents);
        model.addWordsToSynphony();
        model.updateWordList();

        expect(synphony.stages.length).toBe(3);
        expect(_.pluck(model.getStageWords(1), 'Name')).toEqual(['cat', 'mat', 'rat']);
        expect(_.pluck(model.getStageWords(2), 'Name')).toEqual(['cat', 'sat', 'mat', 'rat']);
        expect(_.pluck(model.getStageWords(3), 'Name')).toEqual(['cat', 'sat', 'mat', 'rat', 'one', 'on', 'the']);

        expect(synphony.stages[0].sightWords).toEqual('canine feline');
        expect(synphony.stages[1].sightWords).toEqual('carnivore omnivore');
        expect(synphony.stages[2].sightWords).toEqual('rodent');
    });
});
