import {theOneLanguageDataInstance, ResetLanguageDataInstance}  from './libSynphony/synphony_lib';
import {theOneReaderToolsModel} from './readerToolsModel';
import ReadersSynphonyWrapper from './ReadersSynphonyWrapper';

describe("Bloom Edit Controls tests", function() {
    var classValues;

    beforeEach(function() {
        //noinspection JSUndeclaredVariable
        //reviewslog: this is not allowed: theOneLanguageDataInstance = null;
        ResetLanguageDataInstance();

        theOneReaderToolsModel.clearForTest(); /// brute force way to reset global state

        var settings: any = {};
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

        var api = new ReadersSynphonyWrapper();
        theOneReaderToolsModel.synphony = api;
        api.loadSettings(settings);

        var sampleFileContents = 'catty catty, sat sat sat sat sat sat sat sat, bob bob bob, fob fob, wig, fig fig fig fig fig fig, rig, catty, sat bob fob fig, sat fig, sat';
        theOneReaderToolsModel.addWordsFromFile(sampleFileContents);

        theOneReaderToolsModel.addWordsToSynphony();
        theOneReaderToolsModel.updateWordList();

        theOneReaderToolsModel.setStageNumber(1);
        theOneReaderToolsModel.wordListLoaded = true;

        spyOn(theOneReaderToolsModel, 'updateElementContent');
        // simulated values of class attribute. Currently we ignore the attrName argument, since we only modify class.
        classValues = {decStage: "something", incStage: "something", decLevel: "something", incLevel: "something"};
        theOneReaderToolsModel.setElementAttribute = function(elementId, attrName, val) {
            classValues[elementId] = val;
        };

        //noinspection JSUnusedLocalSymbols
        theOneReaderToolsModel.getElementAttribute = function(elementId, attrName) {
            var result = classValues[elementId];
            if (result) {
                return result;
            }
            return "";
        };
    });

    it("increments stage to limit on stage right button", function() {
        expect(false)
        theOneReaderToolsModel.incrementStage();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "2");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.incrementStage();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "3");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.incrementStage();
        expect(theOneReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements stage to 1 on stage left button", function() {
        theOneReaderToolsModel.setStageNumber(3);
        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.decrementStage();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "2");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.decrementStage();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "1");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.decrementStage();
        expect(theOneReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("increments level to limit on level right button", function() {
        theOneReaderToolsModel.incrementLevel();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "2");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.incrementLevel();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "3");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.incrementLevel();
        expect(theOneReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("decrements level to 1 on level left button", function() {
        theOneReaderToolsModel.setLevelNumber(3);
        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.decrementLevel();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "2");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.decrementLevel();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "1");

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.decrementLevel();
        expect(theOneReaderToolsModel.updateElementContent).not.toHaveBeenCalled();
    });

    it("setting stage updates stage button visibility", function() {
        theOneReaderToolsModel.setStageNumber(3);
        expect(theOneReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something disabledIcon");

        theOneReaderToolsModel.decrementStage();
        expect(theOneReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");

        theOneReaderToolsModel.decrementStage();
        expect(theOneReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something disabledIcon");

        theOneReaderToolsModel.incrementStage();
        expect(theOneReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");

        theOneReaderToolsModel.incrementStage();
        expect(theOneReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("incStage", "class")).toBe("something disabledIcon");
    });

    it("updates level button visibility when setting level", function() {
        theOneReaderToolsModel.setLevelNumber(3);
        expect(theOneReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something disabledIcon");

        theOneReaderToolsModel.decrementLevel();
        expect(theOneReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");

        theOneReaderToolsModel.decrementLevel();
        expect(theOneReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something disabledIcon");

        theOneReaderToolsModel.incrementLevel();
        expect(theOneReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");

        theOneReaderToolsModel.incrementLevel();
        expect(theOneReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something");
        expect(theOneReaderToolsModel.getElementAttribute("incLevel", "class")).toBe("something disabledIcon");
    });

    it("updates content of level element when setting level", function() {
        theOneReaderToolsModel.setLevelNumber(3);
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("levelNumber", "3");
    });

    it("sorts word list correctly when sort buttons clicked", function() {

        theOneReaderToolsModel.setStageNumber(2);
        theOneReaderToolsModel.ckEditorLoaded = true; // some things only happen once the editor is loaded; pretend it is.
        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();

        // Default is currently alphabetic
        theOneReaderToolsModel.setStageNumber(1);
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.sortByLength();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.sortByFrequency();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div>');

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.sortAlphabetically();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.setStageNumber(2);
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div><div class="word sight-word">two</div>');

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.sortByLength(); // same-length ones should be alphabetic
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">sat</div><div class="word sight-word">two</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

        (<any>theOneReaderToolsModel.updateElementContent).calls.reset();
        theOneReaderToolsModel.sortByFrequency();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">bob</div><div class="word">catty</div><div class="word">fob</div><div class="word sight-word">feline</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word sight-word">two</div>');
    });

    it ("sets selected class when sort button clicked", function() {
        classValues.sortAlphabetic = "sortItem sortIconSelected";
        classValues.sortLength = "sortItem";
        classValues.sortFrequency = "sortItem";

        theOneReaderToolsModel.sortByLength();
        expect(theOneReaderToolsModel.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem");
        expect(theOneReaderToolsModel.getElementAttribute("sortLength", "class")).toBe("sortItem sortIconSelected");

        theOneReaderToolsModel.sortByFrequency();
        expect(theOneReaderToolsModel.getElementAttribute("sortLength", "class")).toBe("sortItem");
        expect(theOneReaderToolsModel.getElementAttribute("sortFrequency", "class")).toBe("sortItem sortIconSelected");

        theOneReaderToolsModel.sortAlphabetically();
        expect(theOneReaderToolsModel.getElementAttribute("sortFrequency", "class")).toBe("sortItem");
        expect(theOneReaderToolsModel.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem sortIconSelected");

        classValues.sortLength = "sortItem sortIconSelected"; // anomolous...length is also selected, though not properly current.
        classValues.sortAlphabetic = "sortItem"; // anomolous...doesn't have property, though it is current.

        theOneReaderToolsModel.sortByLength();
        expect(theOneReaderToolsModel.getElementAttribute("sortAlphabetic", "class")).toBe("sortItem");
        expect(theOneReaderToolsModel.getElementAttribute("sortLength", "class")).toBe("sortItem sortIconSelected");
    });

    it ("updates word list on init", function() {
        theOneReaderToolsModel.ckEditorLoaded = true; // some things only happen once the editor is loaded; pretend it is.
        theOneReaderToolsModel.updateControlContents();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class=\"word sight-word\">feline</div><div class="word">rate</div><div class=\"word sight-word\">rodent</div><div class="word">sat</div>');
    });

    it ("updates stage count and buttons on init", function() {
        theOneReaderToolsModel.updateControlContents();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("numberOfStages", "3");
        expect(theOneReaderToolsModel.getElementAttribute("decStage", "class")).toBe("something disabledIcon");
    });

    it ("updates level buttons on init", function() {
        theOneReaderToolsModel.updateControlContents();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("numberOfLevels", "3");
        expect(theOneReaderToolsModel.getElementAttribute("decLevel", "class")).toBe("something disabledIcon");
    });

    it ("updates stage label on init", function() {
        theOneReaderToolsModel.updateControlContents();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("stageNumber", "1");
    });

    it("sets level max values on init", function() {
        theOneReaderToolsModel.updateControlContents();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "6");
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "6");
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerBook", "90");
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "3");
        //expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "0");
        expect(theOneReaderToolsModel.getElementAttribute("maxWordsPerBook", "class")).toBe("");
    });

    it("updates max values when level changes", function() {
        theOneReaderToolsModel.incrementLevel();
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPage", "10");
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerPageBook", "10");
        expect(theOneReaderToolsModel.updateElementContent).not.toHaveBeenCalledWith("maxWordsPerBook", "0");
        expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxWordsPerSentence", "5");
        //expect(theOneReaderToolsModel.updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "12");
        //expect(theOneReaderToolsModel.getElementAttribute("maxWordsPerBook", "class")).toBe("disabledLimit");
    });
});