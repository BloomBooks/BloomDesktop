import {theOneLanguageDataInstance, ResetLanguageDataInstance}  from './libSynphony/synphony_lib';
import {ReaderToolsModel} from './readerToolsModel';

describe("Bloom Edit Controls tests", function() {

    var model;
    var classValues;

    beforeEach(function() {
        //noinspection JSUndeclaredVariable
        //reviewslog: this is not allowed: theOneLanguageDataInstance = null;
        ResetLanguageDataInstance();
        
        model = new ReaderToolsModel();

        var settings = {};
        settings.letters = 'a b c d e f g h i j k l m n o p q r s t u v w x y z';
        settings.moreWords = 'catty sat rate bob fob big wig fig rig';
        settings.stages = [];
        settings.levels = [];

        settings.stages.push({"letters":"a c e r s t y","sightWords":"feline rodent"});
        settings.stages.push({"letters":"b f o","sightWords":"one two"});
        settings.stages.push({"letters":"g i w","sightWords":"fruit nut"});

        settings.levels.push({"maxWordsPerSentence":"3","maxWordsPerPage":"6","maxWordsPerBook":"90","maxUniqueWordsPerBook":"","thingsToRemember":[""]});
        settings.levels.push({"maxWordsPerSentence":"5","maxWordsPerPage":"10","maxWordsPerBook":"100","maxUniqueWordsPerBook":"","thingsToRemember":[""]});
        settings.levels.push({"maxWordsPerSentence":"7","maxWordsPerPage":"14","maxWordsPerBook":"110","maxUniqueWordsPerBook":"","thingsToRemember":[""]});

        var api = new SynphonyApi();
        model.synphony = api;
        api.loadSettings(settings);

        var sampleFileContents = 'catty catty, sat sat sat sat sat sat sat sat, bob bob bob, fob fob, wig, fig fig fig fig fig fig, rig, catty, sat bob fob fig, sat fig, sat';
        model.addWordsFromFile(sampleFileContents);

        model.addWordsToSynphony();
        model.updateWordList();

        model.setStageNumber(1);
        model.wordListLoaded = true;

        spyOn(ReaderToolsModel, 'updateElementContent');
        // simulated values of class attribute. Currently we ignore the attrName argument, since we only modify class.
        classValues = {decStage: "something", incStage: "something", decLevel: "something", incLevel: "something"};
        ReaderToolsModel.setElementAttribute = function(elementId, attrName, val) {
            classValues[elementId] = val;
        };

        //noinspection JSUnusedLocalSymbols
        ReaderToolsModel.getElementAttribute = function(elementId, attrName) {
            var result = classValues[elementId];
            if (result) {
                return result;
            }
            return "";
        };
    });

    it("increments stage to limit on stage right button", function() {
        expect(false)
        model.incrementStage();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "2");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.incrementStage();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "3");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.incrementStage();
        expect(ReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements stage to 1 on stage left button", function() {
        model.setStageNumber(3);
        ReaderToolsModel.updateElementContent.calls.reset();
        model.decrementStage();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "2");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.decrementStage();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "1");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.decrementStage();
        expect(ReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("increments level to limit on level right button", function() {
        model.incrementLevel();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "2");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.incrementLevel();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "3");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.incrementLevel();
        expect(ReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements level to 1 on level left button", function() {
        model.setLevelNumber(3);
        ReaderToolsModel.updateElementContent.calls.reset();
        model.decrementLevel();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "2");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.decrementLevel();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "1");

        ReaderToolsModel.updateElementContent.calls.reset();
        model.decrementLevel();
        expect(ReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("setting stage updates stage button visibility", function() {
        model.setStageNumber(3);
        expect(ReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something disabledIcon");

        model.decrementStage();
        expect(ReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");

        model.decrementStage();
        expect(ReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something disabledIcon");

        model.incrementStage();
        expect(ReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");

        model.incrementStage();
        expect(ReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something disabledIcon");
    });

    it("updates level button visibility when setting level", function() {
        model.setLevelNumber(3);
        expect(ReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something disabledIcon");

        model.decrementLevel();
        expect(ReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");

        model.decrementLevel();
        expect(ReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something disabledIcon");

        model.incrementLevel();
        expect(ReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");

        model.incrementLevel();
        expect(ReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");
        expect(ReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something disabledIcon");
    });

    it("updates content of level element when setting level", function() {
        model.setLevelNumber(3);
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "3");
    });

    it("sorts word list correctly when sort buttons clicked", function() {

        model.setStageNumber(2);
        model.ckEditorLoaded = true; // some things only happen once the editor is loaded; pretend it is.
        ReaderToolsModel.updateElementContent.calls.reset();

        // Default is currently alphabetic
        model.setStageNumber(1);
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

        ReaderToolsModel.updateElementContent.calls.reset();
        model.sortByLength();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

        ReaderToolsModel.updateElementContent.calls.reset();
        model.sortByFrequency();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div>');

        ReaderToolsModel.updateElementContent.calls.reset();
        model.sortAlphabetically();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

        ReaderToolsModel.updateElementContent.calls.reset();
        model.setStageNumber(2);
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div><div class="word sight-word">two</div>');

        ReaderToolsModel.updateElementContent.calls.reset();
        model.sortByLength(); // same-length ones should be alphabetic
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">sat</div><div class="word sight-word">two</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

        ReaderToolsModel.updateElementContent.calls.reset();
        model.sortByFrequency();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">bob</div><div class="word">catty</div><div class="word">fob</div><div class="word sight-word">feline</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word sight-word">two</div>');
    });

    it ("sets selected class when sort button clicked", function() {
        classValues.sortAlphabetic = "sortItem sortIconSelected";
        classValues.sortLength = "sortItem";
        classValues.sortFrequency = "sortItem";

        model.sortByLength();
        expect(ReaderToolsModel.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem");
        expect(ReaderToolsModel.getElementAttribute("sortLength", "class")).toBe("sortItem sortIconSelected");

        model.sortByFrequency();
        expect(ReaderToolsModel.getElementAttribute("sortLength", "class")).toBe("sortItem");
        expect(ReaderToolsModel.getElementAttribute("sortFrequency", "class")).toBe("sortItem sortIconSelected");

        model.sortAlphabetically();
        expect(ReaderToolsModel.getElementAttribute("sortFrequency", "class")).toBe("sortItem");
        expect(ReaderToolsModel.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem sortIconSelected");

        classValues.sortLength = "sortItem sortIconSelected"; // anomolous...length is also selected, though not properly current.
        classValues.sortAlphabetic = "sortItem"; // anomolous...doesn't have property, though it is current.

        model.sortByLength();
        expect(ReaderToolsModel.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem");
        expect(ReaderToolsModel.getElementAttribute("sortLength", "class")).toBe("sortItem sortIconSelected");
    });

    it ("updates word list on init", function() {
        model.ckEditorLoaded = true; // some things only happen once the editor is loaded; pretend it is.
        model.updateControlContents();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class=\"word sight-word\">feline</div><div class="word">rate</div><div class=\"word sight-word\">rodent</div><div class="word">sat</div>');
    });

    it ("updates stage count and buttons on init", function() {
        model.updateControlContents();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("numberOfStages", "3");
        expect(ReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something disabledIcon");
    });

    it ("updates level buttons on init", function() {
        model.updateControlContents();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("numberOfLevels", "3");
        expect(ReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something disabledIcon");
    });

    it ("updates stage label on init", function() {
        model.updateControlContents();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "1");
    });

    it("sets level max values on init", function() {
        model.updateControlContents();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "6");
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "6");
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerBook", "90");
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "3");
        //expect(model.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "0");
        expect(ReaderToolsModel.getElementAttribute("maxWordsPerBook", "class")).toBe("");
    });

    it("updates max values when level changes", function() {
        model.incrementLevel();
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "10");
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "10");
        expect(ReaderToolsModel.updateElementContent).not.toHaveBeenCalledWith("maxWordsPerBook", "0");
        expect(ReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "5");
        //expect(model.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "12");
        //expect(model.getElementAttribute("maxWordsPerBook", "class")).toBe("disabledLimit");
    });
});