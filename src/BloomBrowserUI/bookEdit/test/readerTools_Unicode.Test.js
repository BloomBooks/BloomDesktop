describe("Unicode character tests", function() {

    var model;
    var classValues;

    beforeEach(function() {

        //noinspection JSUndeclaredVariable
        lang_data = null;
        model = new ReaderToolsModel();

        var settings = {};
        settings.letters = 'a b c d e f g h i j k l m n o p q r s t u v w x y z';
        settings.letterCombinations = '';
        settings.moreWords = '';
        settings.stages = [];
        settings.levels = [];

        settings.stages.push({"letters":"a c e r s t y","sightWords":"feline rodent"});
        settings.stages.push({"letters":"b f o","sightWords":"one two"});
        settings.stages.push({"letters":"g i w","sightWords":"fruit nut"});

        settings.levels.push({"maxWordsPerSentence":"3","maxWordsPerPage":"6","maxWordsPerBook":"90","maxUniqueWordsPerBook":"","thingsToRemember":[""]});
        settings.levels.push({"maxWordsPerSentence":"5","maxWordsPerPage":"10","maxWordsPerBook":"100","maxUniqueWordsPerBook":"","thingsToRemember":[""]});
        settings.levels.push({"maxWordsPerSentence":"7","maxWordsPerPage":"14","maxWordsPerBook":"110","maxUniqueWordsPerBook":"","thingsToRemember":[""]});

        var api = model.getSynphony();
        api.loadSettings(JSON.stringify(settings));

        var sampleFileContents = 'catty catty, sat sat sat sat sat sat sat sat, bob bob bob, fob fob, wig, fig fig fig fig fig fig, rig, catty, sat bob fob fig, sat fig, sat';
        model.addWordsFromFile(sampleFileContents);

        model.addWordsToSynphony();
        model.updateWordList();

        //spyOn(model, 'updateElementContent');
    });

    it("normalize some words", function() {

        var uc = net.kornr.unicode;
        expect(uc.lowercase_nomark('Ça brûle')).toBe('ca brule');
        expect(uc.lowercase_nomark('minéraux')).toBe('mineraux');
        expect(uc.lowercase_nomark('l’eau')).toBe('l’eau');
        expect(uc.lowercase_nomark("l'eau")).toBe("l'eau");
        expect(uc.lowercase_nomark('espèces')).toBe('especes');
        expect(uc.lowercase_nomark('végétales')).toBe('vegetales');
        expect(uc.lowercase_nomark('nôtre')).toBe('notre');
        expect(uc.lowercase_nomark('connaît')).toBe('connait');
    });

    it("get gpc form", function() {

        var graphemes = _.sortBy(_.pluck(lang_data.GPCS, 'Grapheme'), 'length').reverse();

        expect(lang_data.getInsensitiveGpcForm('Ça', graphemes)).toEqual(['c', 'a']);
        expect(lang_data.getInsensitiveGpcForm('brûle', graphemes)).toEqual(['b', 'r', 'u', 'l', 'e']);
        expect(lang_data.getInsensitiveGpcForm('l\'eau', graphemes)).toEqual(['l', '\'', 'e', 'a', 'u']);
        expect(lang_data.getInsensitiveGpcForm('l’eau', graphemes)).toEqual(['l', '’', 'e', 'a', 'u']);
        expect(lang_data.getInsensitiveGpcForm('nôtre', graphemes)).toEqual(['n', 'o', 't', 'r', 'e']);
    });
});