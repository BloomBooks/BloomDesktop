describe("readerTools-libSynphony tests", function() {

    it("addWordsFromFile", function() {

        model = new ReaderToolsModel();
        var fileContents = 'The cat sat on the mat. The rat sat on the cat.';

        model.addWordsFromFile(fileContents);
        expect(model.allWords).toEqual(['the', 'cat', 'sat', 'on', 'mat', 'rat']);
    });

    it("addWordsToSynphony", function() {

        var settingsFileContents = '{\"letters\":\"a b c d e f g h i j k l m n o p q r s t u v w x y z\",\"letterCombinations\":\"ai oo sh ng th ing\",\"moreWords\":\"one\\ntwo\\nthree\",\"stages\":[{\"letters\":\"a c m r t\",\"sightWords\":\"canine feline\"},{\"letters\":\"d g o e s\",\"sightWords\":\"carnivore omnivore\"},{\"letters\":\"i l n th\",\"sightWords\":\"rodent\"}]}';
        var sampleFileContents = 'The cat sat on the mat. The rat sat on the cat.';

        model = new ReaderToolsModel();
        var synphony = model.getSynphony();
        synphony.loadSettings(settingsFileContents);

        model.addWordsFromFile(sampleFileContents);
        model.addWordsToSynphony();

        expect(synphony.stages.length).toBe(3);
        expect(synphony.stages[0].words).toEqual({cat: 1, mat: 1, rat: 1});
        expect(synphony.stages[1].words).toEqual({sat: 1});
        expect(synphony.stages[2].words).toEqual({on: 1, the: 1});
    });
});
