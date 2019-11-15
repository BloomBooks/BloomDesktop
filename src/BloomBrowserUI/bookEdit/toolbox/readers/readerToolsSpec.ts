import {
    theOneLanguageDataInstance,
    ResetLanguageDataInstance
} from "./libSynphony/synphony_lib";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";

describe("Bloom Edit Controls tests", () => {
    var classValues;

    beforeEach(() => {
        //noinspection JSUndeclaredVariable
        //reviewslog: this is not allowed: theOneLanguageDataInstance = null;
        ResetLanguageDataInstance();

        getTheOneReaderToolsModel().clearForTest(); /// brute force way to reset global state

        var settings: any = {};
        settings.letters =
            "a b c d e f g h i j k l m n o p q r s t u v w x y z";
        settings.moreWords = "catty sat rate bob fob big wig fig rig";
        settings.stages = [];
        settings.levels = [];

        settings.stages.push({
            letters: "a c e r s t y",
            sightWords: "feline rodent"
        });
        settings.stages.push({ letters: "b f o", sightWords: "one two" });
        settings.stages.push({ letters: "g i w", sightWords: "fruit nut" });

        settings.levels.push({
            maxWordsPerSentence: "3",
            maxWordsPerPage: "6",
            maxWordsPerBook: "90",
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""]
        });
        settings.levels.push({
            maxWordsPerSentence: "5",
            maxWordsPerPage: "10",
            maxWordsPerBook: "100",
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""]
        });
        settings.levels.push({
            maxWordsPerSentence: "7",
            maxWordsPerPage: "14",
            maxWordsPerBook: "110",
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""]
        });

        var api = new ReadersSynphonyWrapper();
        getTheOneReaderToolsModel().synphony = api;
        api.loadSettings(settings);

        var sampleFileContents =
            "catty catty, sat sat sat sat sat sat sat sat, bob bob bob, fob fob, wig, fig fig fig fig fig fig, rig, catty, sat bob fob fig, sat fig, sat";
        getTheOneReaderToolsModel().addWordsFromFile(sampleFileContents);

        getTheOneReaderToolsModel().addWordsToSynphony();
        getTheOneReaderToolsModel().updateWordList();

        getTheOneReaderToolsModel().setStageNumber(1);
        getTheOneReaderToolsModel().wordListLoaded = true;

        spyOn(getTheOneReaderToolsModel(), "updateElementContent");
        // simulated values of class attribute. Currently we ignore the attrName argument, since we only modify class.
        classValues = {
            decStage: "something",
            incStage: "something",
            decLevel: "something",
            incLevel: "something"
        };
        getTheOneReaderToolsModel().setElementAttribute = (
            elementId,
            attrName,
            val
        ) => {
            classValues[elementId] = val;
        };

        //noinspection JSUnusedLocalSymbols
        getTheOneReaderToolsModel().getElementAttribute = (
            elementId,
            attrName
        ) => {
            var result = classValues[elementId];
            if (result) {
                return result;
            }
            return "";
        };
    });

    /* skipping until we figure out how to make work with localization See BL-3554
        it("increments stage to limit on stage right button", () => {
                getTheOneReaderToolsModel().incrementStage();

//        setTimeout(function(){
                        expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("stageNumber", "2");

                        (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                        getTheOneReaderToolsModel().incrementStage();
                        expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("stageNumber", "3");

                        (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                        getTheOneReaderToolsModel().incrementStage();
                        expect(getTheOneReaderToolsModel().updateElementContent).not.toHaveBeenCalled();
//            done(); // this done business is needed because incrementStage is async
//        }, 0);
        });
*/

    /* skipping until we figure out how to make work with localization See BL-3554
         it("decrements stage to 1 on stage left button", () => {
                getTheOneReaderToolsModel().setStageNumber(3);
                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().decrementStage();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("stageNumber", "2");

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().decrementStage();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("stageNumber", "1");

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().decrementStage();
                expect(getTheOneReaderToolsModel().updateElementContent).not.toHaveBeenCalled();
        });
*/
    it("increments level to limit on level right button", () => {
        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("levelNumber", "2");

        (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("levelNumber", "3");

        (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).not.toHaveBeenCalled();
    });

    /* skipping until we figure out how to make work with localization See BL-3554
        it("decrements level to 1 on level left button", () => {
                getTheOneReaderToolsModel().setLevelNumber(3);
                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().decrementLevel();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("levelNumber", "2");

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().decrementLevel();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("levelNumber", "1");

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().decrementLevel();
                expect(getTheOneReaderToolsModel().updateElementContent).not.toHaveBeenCalled();
        });
*/
    /* skipping until we figure out how to make work with localization See BL-3554
        it("setting stage updates stage button visibility", () => {
                getTheOneReaderToolsModel().setStageNumber(3);
                expect(getTheOneReaderToolsModel().getElementAttribute("decStage", "class")).toBe("something");
                expect(getTheOneReaderToolsModel().getElementAttribute("incStage", "class")).toBe("something disabledIcon");

                getTheOneReaderToolsModel().decrementStage();
                expect(getTheOneReaderToolsModel().getElementAttribute("incStage", "class")).toBe("something");
                expect(getTheOneReaderToolsModel().getElementAttribute("decStage", "class")).toBe("something");

                getTheOneReaderToolsModel().decrementStage();
                expect(getTheOneReaderToolsModel().getElementAttribute("incStage", "class")).toBe("something");
                expect(getTheOneReaderToolsModel().getElementAttribute("decStage", "class")).toBe("something disabledIcon");

                getTheOneReaderToolsModel().incrementStage();
                expect(getTheOneReaderToolsModel().getElementAttribute("incStage", "class")).toBe("something");
                expect(getTheOneReaderToolsModel().getElementAttribute("decStage", "class")).toBe("something");

                getTheOneReaderToolsModel().incrementStage();
                expect(getTheOneReaderToolsModel().getElementAttribute("decStage", "class")).toBe("something");
                expect(getTheOneReaderToolsModel().getElementAttribute("incStage", "class")).toBe("something disabledIcon");
        });
*/

    it("updates level button visibility when setting level", () => {
        getTheOneReaderToolsModel().setLevelNumber(3);
        expect(
            getTheOneReaderToolsModel().getElementAttribute("decLevel", "class")
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute("incLevel", "class")
        ).toBe("something disabledIcon");

        getTheOneReaderToolsModel().decrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute("incLevel", "class")
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute("decLevel", "class")
        ).toBe("something");

        getTheOneReaderToolsModel().decrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute("incLevel", "class")
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute("decLevel", "class")
        ).toBe("something disabledIcon");

        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute("incLevel", "class")
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute("decLevel", "class")
        ).toBe("something");

        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute("decLevel", "class")
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute("incLevel", "class")
        ).toBe("something disabledIcon");
    });

    it("updates content of level element when setting level", () => {
        getTheOneReaderToolsModel().setLevelNumber(3);
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("levelNumber", "3");
    });

    /* skipping due to mistery. See BL-3554
        it("sorts word list correctly when sort buttons clicked", () => {

                getTheOneReaderToolsModel().setStageNumber(2);
                getTheOneReaderToolsModel().ckEditorLoaded = true; // some things only happen once the editor is loaded; pretend it is.
                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();

                // Default is currently alphabetic
                getTheOneReaderToolsModel().setStageNumber(1);
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().sortByLength();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().sortByFrequency();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div>');

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().sortAlphabetically();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">catty</div><div class="word sight-word">feline</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div>');

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().setStageNumber(2);
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word">sat</div><div class="word sight-word">two</div>');

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().sortByLength(); // same-length ones should be alphabetic
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">bob</div><div class="word">fob</div><div class="word sight-word">one</div><div class="word">sat</div><div class="word sight-word">two</div><div class="word">rate</div><div class="word">catty</div><div class="word sight-word">feline</div><div class="word sight-word">rodent</div>');

                (<any>getTheOneReaderToolsModel().updateElementContent).calls.reset();
                getTheOneReaderToolsModel().sortByFrequency();
                expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("wordList", '<div class="word">sat</div><div class="word">bob</div><div class="word">catty</div><div class="word">fob</div><div class="word sight-word">feline</div><div class="word sight-word">one</div><div class="word">rate</div><div class="word sight-word">rodent</div><div class="word sight-word">two</div>');
        });
        */

    it("sets selected class when sort button clicked", () => {
        classValues.sortAlphabetic = "sortItem sortIconSelected";
        classValues.sortLength = "sortItem";
        classValues.sortFrequency = "sortItem";

        getTheOneReaderToolsModel().sortByLength();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortAlphabetic",
                "class"
            )
        ).toBe("sortItem");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortLength",
                "class"
            )
        ).toBe("sortItem sortIconSelected");

        getTheOneReaderToolsModel().sortByFrequency();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortLength",
                "class"
            )
        ).toBe("sortItem");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortFrequency",
                "class"
            )
        ).toBe("sortItem sortIconSelected");

        getTheOneReaderToolsModel().sortAlphabetically();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortFrequency",
                "class"
            )
        ).toBe("sortItem");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortAlphabetic",
                "class"
            )
        ).toBe("sortItem sortIconSelected");

        classValues.sortLength = "sortItem sortIconSelected"; // anomolous...length is also selected, though not properly current.
        classValues.sortAlphabetic = "sortItem"; // anomolous...doesn't have property, though it is current.

        getTheOneReaderToolsModel().sortByLength();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortAlphabetic",
                "class"
            )
        ).toBe("sortItem");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "sortLength",
                "class"
            )
        ).toBe("sortItem sortIconSelected");
    });

    it("updates word list on init", () => {
        getTheOneReaderToolsModel().ckEditorLoaded = true; // some things only happen once the editor is loaded; pretend it is.
        getTheOneReaderToolsModel().updateControlContents();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith(
            "wordList",
            '<div class="word lang1InATool "> catty</div><div class="word lang1InATool  sight-word"> feline</div><div class="word lang1InATool "> rate</div><div class="word lang1InATool  sight-word"> rodent</div><div class="word lang1InATool "> sat</div>'
        );
    });

    it("updates stage count and buttons on init", () => {
        getTheOneReaderToolsModel().updateControlContents();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("numberOfStages", "3");
        expect(
            getTheOneReaderToolsModel().getElementAttribute("decStage", "class")
        ).toBe("something disabledIcon");
    });

    it("updates level buttons on init", () => {
        getTheOneReaderToolsModel().updateControlContents();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("numberOfLevels", "3");
        expect(
            getTheOneReaderToolsModel().getElementAttribute("decLevel", "class")
        ).toBe("something disabledIcon");
    });

    it("updates stage label on init", () => {
        getTheOneReaderToolsModel().updateControlContents();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("stageNumber", "1");
    });

    it("sets level max values on init", () => {
        getTheOneReaderToolsModel().updateControlContents();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("maxWordsPerPage", "6");
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("maxWordsPerPageBook", "6");
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("maxWordsPerBook", "90");
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("maxWordsPerSentence", "3");
        //expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "0");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "maxWordsPerBook",
                "class"
            )
        ).toBe("");
    });

    it("updates max values when level changes", () => {
        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("maxWordsPerPage", "10");
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("maxWordsPerPageBook", "10");
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).not.toHaveBeenCalledWith("maxWordsPerBook", "0");
        expect(
            getTheOneReaderToolsModel().updateElementContent
        ).toHaveBeenCalledWith("maxWordsPerSentence", "5");
        //expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "12");
        //expect(getTheOneReaderToolsModel().getElementAttribute("maxWordsPerBook", "class")).toBe("disabledLimit");
    });
});
