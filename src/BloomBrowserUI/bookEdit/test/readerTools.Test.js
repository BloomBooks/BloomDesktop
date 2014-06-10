describe("Bloom Edit Controls tests", function() {
    var api = new SynphonyApi();
    api.addStageWithWords("1A", "cat sat rat", "feline rodent");
    api.addStageWithWords("A", "bob fob", "one two");
    api.addStageWithWords("3", "big wig fig rig", "fruit nut");

    var levelD = new Level("D");
    levelD.maxWordsPerBook = 17;
    levelD.maxWordsPerSentence = 3;
    levelD.maxUniqueWordsPerBook = 10;
    levelD.maxWordsPerPage = 4;
    api.addLevel(levelD);
    var levelE = new Level("E");
    // maxWordsPerBook is deliberately left at default, 0.
    levelE.maxWordsPerSentence = 5;
    levelE.maxUniqueWordsPerBook = 12;
    levelE.maxWordsPerPage = 6;
    api.addLevel(levelE);
    api.addLevel(new Level("F"));

    var model;
    var classValues;

    beforeEach(function() {
        model = new ReaderToolsModel();
        model.setSynphony(api);
        spyOn(model, 'updateElementContent');
        // simulated values of class attribute. Currently we ignore the attrName argument, since we only modify class.
        classValues = {decStage:"something", incStage: "something", decLevel: "something", incLevel: "something"};
        model.setElementAttribute = function(elementId, attrName, val) {
            classValues[elementId] = val;
        };
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
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", "A");

        model.updateElementContent.reset();
        model.incrementStage();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", "3");

        model.updateElementContent.reset();
        model.incrementStage();
        expect(model.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements stage to 1 on stage left button", function() {
        model.setStageNumber(3);
        model.updateElementContent.reset();
        model.decrementStage();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", "A");

        model.updateElementContent.reset();
        model.decrementStage();
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", "1A");

        model.updateElementContent.reset();
        model.decrementStage();
        expect(model.updateElementContent).not.toHaveBeenCalled();
    });

    it("increments level to limit on level right button", function() {
        model.incrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", "E");

        model.updateElementContent.reset();
        model.incrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", "F");

        model.updateElementContent.reset();
        model.incrementLevel();
        expect(model.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements level to 1 on level left button", function() {
        model.setLevelNumber(3);
        model.updateElementContent.reset();
        model.decrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", "E");

        model.updateElementContent.reset();
        model.decrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", "D");

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
        expect(model.updateElementContent).toHaveBeenCalledWith("levelNumber", "F");
    });

    it("sorts word list correctly when sort buttons clicked", function() {
        var api2 = new SynphonyApi(); // use own api for this test, don't modify the shared variable
        api2.addStageWithWords("1", "catty catty catty sat rate rate rate rate rate");
        api2.addStageWithWords("2", "bob fob cob job hope hope hope");
        model.setSynphony(api2);
        model.setStageNumber(2);
        model.updateElementContent.reset();

        // Default is currently alphabetic
        model.setStageNumber(1);
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word">rate</div><div class="word">sat</div>');

        model.updateElementContent.reset();
        model.sortByLength();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">rate</div><div class="word">catty</div>');

        model.updateElementContent.reset();
        model.sortByFrequency();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">rate</div><div class="word">catty</div><div class="word">sat</div>');

        model.updateElementContent.reset();
        model.sortAlphabetically();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word">rate</div><div class="word">sat</div>');

        model.updateElementContent.reset();
        model.setStageNumber(2);
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">cob</div><div class="word">fob</div><div class="word">hope</div><div class="word">job</div>');

        model.updateElementContent.reset();
        model.sortByLength(); // same-length ones should be alphabetic
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">cob</div><div class="word">fob</div><div class="word">job</div><div class="word">hope</div>');

        model.updateElementContent.reset();
        model.sortByFrequency();
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">hope</div><div class="word">bob</div><div class="word">cob</div><div class="word">fob</div><div class="word">job</div>');
    });

    it("updates word list when stage changes", function() {
        var api2 = new SynphonyApi(); // use own api for this test, don't modify the shared variable
        // We want a specific set of words to test rows of one, two, and three words.
        api2.addStageWithWords("1", "cat sat rat"); // exactly fills row
        api2.addStageWithWords("2", "bob fob"); // less than one row
        api2.addStageWithWords("3", "big wig fig rig"); // second partial row (with just one word)
        model.setSynphony(api2);

        model.setStageNumber(2);
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">fob</div>');

        model.updateElementContent.reset();
        model.setStageNumber(1);
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">cat</div><div class="word">rat</div><div class="word">sat</div>');

        model.updateElementContent.reset();
        model.setStageNumber(3);
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">big</div><div class="word">fig</div><div class="word">rig</div><div class="word">wig</div>');
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
        expect(model.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">cat</div><div class="word">rat</div><div class="word">sat</div><div class=\"word sight-word\">feline</div><div class=\"word sight-word\">rodent</div>');
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
        expect(model.updateElementContent).toHaveBeenCalledWith("stageNumber", "1A");
    });

    it("sets level max values on init", function() {
        model.updateControlContents();
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "4");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "4");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerBook", "17");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "3");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "10");
        expect(model.getElementAttribute("maxWordsPerBook", "class")).toBe("");
    });

    it("updates max values when level changes", function() {
        model.incrementLevel();
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "6");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "6");
        expect(model.updateElementContent).not.toHaveBeenCalledWith("maxWordsPerBook", "0");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "5");
        expect(model.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "12");
        expect(model.getElementAttribute("maxWordsPerBook", "class")).toBe("disabledLimit");
    });
});