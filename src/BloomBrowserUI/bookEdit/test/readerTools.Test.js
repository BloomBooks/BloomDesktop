describe("Bloom Edit Controls tests", function() {

    var model;
    var classValues;

    beforeEach(function() {

        //noinspection JSUndeclaredVariable
        lang_data = null;
        model = new ReaderToolsModel();

        var settings = {};
        settings.letters = 'a b c d e f g h i j k l m n o p q r s t u v w x y z';
        settings.letterCombinations = 'th oo ing';
        settings.moreWords = 'catty sat rate bob fob big wig fig rig';
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

        spyOn(model, 'updateElementContent');
        // simulated values of class attribute. Currently we ignore the attrName argument, since we only modify class.
        classValues = {decStage: "something", incStage: "something", decLevel: "something", incLevel: "something"};
        model.setElementAttribute = function(elementId, attrName, val) {
            classValues[elementId] = val;
        };

        //noinspection JSUnusedLocalSymbols
        model.getElementAttribute = function(elementId, attrName) {
            var result = classValues[elementId];
            if (result) {
                return result;
            }
            return "";
        };
    });

    it("increments stage to limit on stage right button", function() {
        model.incrementStage();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", 2);

        model.updateElementContent.reset();
        model.incrementStage();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", 3);

        model.updateElementContent.reset();
        model.incrementStage();
        expect(model.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements stage to 1 on stage left button", function() {
        model.setStageNumber(3);
        model.updateElementContent.reset();
        model.decrementStage();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", 2);

        model.updateElementContent.reset();
        model.decrementStage();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", 1);

        model.updateElementContent.reset();
        model.decrementStage();
        expect(model.updateElementContent).not.toHaveBeenCalled();
    });

    it("increments level to limit on level right button", function() {
        model.incrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", 2);

        model.updateElementContent.reset();
        model.incrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", 3);

        model.updateElementContent.reset();
        model.incrementLevel();
        expect(model.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements level to 1 on level left button", function() {
        model.setLevelNumber(3);
        model.updateElementContent.reset();
        model.decrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", 2);

        model.updateElementContent.reset();
        model.decrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", 1);

        model.updateElementContent.reset();
        model.decrementLevel();
        expect(model.updateElementContent).not.toHaveBeenCalled();
    });

    it("setting stage updates stage button visibility", function() {
        model.setStageNumber(3);
        expect(model.getElementAttribute("decStage", "class")).toBe("something");
        expect(model.getElementAttribute("incStage", "class")).toBe("something disabledIcon");

        model.decrementStage();
        expect(model.getElementAttribute("incStage", "class")).toBe("something");
        expect(model.getElementAttribute("decStage", "class")).toBe("something");

        model.decrementStage();
        expect(model.getElementAttribute("incStage", "class")).toBe("something");
        expect(model.getElementAttribute("decStage", "class")).toBe("something disabledIcon");

        model.incrementStage();
        expect(model.getElementAttribute("incStage", "class")).toBe("something");
        expect(model.getElementAttribute("decStage", "class")).toBe("something");

        model.incrementStage();
        expect(model.getElementAttribute("decStage", "class")).toBe("something");
        expect(model.getElementAttribute("incStage", "class")).toBe("something disabledIcon");
    });

    it("updates level button visibility when setting level", function() {
        model.setLevelNumber(3);
        expect(model.getElementAttribute("decLevel", "class")).toBe("something");
        expect(model.getElementAttribute("incLevel", "class")).toBe("something disabledIcon");

        model.decrementLevel();
        expect(model.getElementAttribute("incLevel", "class")).toBe("something");
        expect(model.getElementAttribute("decLevel", "class")).toBe("something");

        model.decrementLevel();
        expect(model.getElementAttribute("incLevel", "class")).toBe("something");
        expect(model.getElementAttribute("decLevel", "class")).toBe("something disabledIcon");

        model.incrementLevel();
        expect(model.getElementAttribute("incLevel", "class")).toBe("something");
        expect(model.getElementAttribute("decLevel", "class")).toBe("something");

        model.incrementLevel();
        expect(model.getElementAttribute("decLevel", "class")).toBe("something");
        expect(model.getElementAttribute("incLevel", "class")).toBe("something disabledIcon");
    });

    it("updates content of level element when setting level", function() {
        model.setLevelNumber(3);
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", 3);
    });

    it("sorts word list correctly when sort buttons clicked", function() {

        model.setStageNumber(2);
        model.updateElementContent.reset();

        // Default is currently alphabetic
        model.setStageNumber(1);
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

        model.updateElementContent.reset();
        model.sortByLength();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

        model.updateElementContent.reset();
        model.sortByFrequency();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div>');

        model.updateElementContent.reset();
        model.sortAlphabetically();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

        model.updateElementContent.reset();
        model.setStageNumber(2);
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div><div class="word sight-word">two</div>');

        model.updateElementContent.reset();
        model.sortByLength(); // same-length ones should be alphabetic
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">sat</div><div class="word sight-word">two</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

        model.updateElementContent.reset();
        model.sortByFrequency();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">bob</div><div class="word">catty</div><div class="word">fob</div><div class="word sight-word">feline</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word sight-word">two</div>');
    });

    it ("sets selected class when sort button clicked", function() {
        classValues.sortAlphabetic = "sortItem sortIconSelected";
        classValues.sortLength = "sortItem";
        classValues.sortFrequency = "sortItem";

        model.sortByLength();
        expect(model.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem");
        expect(model.getElementAttribute("sortLength", "class")).toBe("sortItem sortIconSelected");

        model.sortByFrequency();
        expect(model.getElementAttribute("sortLength", "class")).toBe("sortItem");
        expect(model.getElementAttribute("sortFrequency", "class")).toBe("sortItem sortIconSelected");

        model.sortAlphabetically();
        expect(model.getElementAttribute("sortFrequency", "class")).toBe("sortItem");
        expect(model.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem sortIconSelected");

        classValues.sortLength = "sortItem sortIconSelected"; // anomolous...length is also selected, though not properly current.
        classValues.sortAlphabetic = "sortItem"; // anomolous...doesn't have property, though it is current.

        model.sortByLength();
        expect(model.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem");
        expect(model.getElementAttribute("sortLength", "class")).toBe("sortItem sortIconSelected");
    });

    it ("updates word list on init", function() {
        model.updateControlContents();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class=\"word sight-word\">feline</div><div class="word">rate</div><div class=\"word sight-word\">rodent</div><div class="word">sat</div>');
    });

    it ("updates stage count and buttons on init", function() {
        model.updateControlContents();
        expect(model.updateElementContent).toHaveBeenCalledWith("numberOfStages", "3");
        expect(model.getElementAttribute("decStage", "class")).toBe("something disabledIcon");
    });

    it ("updates level buttons on init", function() {
        model.updateControlContents();
        expect(model.updateElementContent).toHaveBeenCalledWith("numberOfLevels", "3");
        expect(model.getElementAttribute("decLevel", "class")).toBe("something disabledIcon");
    });

    it ("updates stage label on init", function() {
        model.updateControlContents();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", 1);
    });

    it("sets level max values on init", function() {
        model.updateControlContents();
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "6");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "6");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerBook", "90");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "3");
        //expect(model.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "0");
        expect(model.getElementAttribute("maxWordsPerBook", "class")).toBe("");
    });

    it("updates max values when level changes", function() {
        model.incrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "10");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "10");
        expect(model.updateElementContent).not.toHaveBeenCalledWith("maxWordsPerBook", "0");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "5");
        //expect(model.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "12");
        //expect(model.getElementAttribute("maxWordsPerBook", "class")).toBe("disabledLimit");
    });
});