import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
    theOneLanguageDataInstance,
    ResetLanguageDataInstance,
} from "./libSynphony/synphony_lib";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";

describe("Bloom Edit Controls tests", () => {
    let classValues;

    let stageNOfMElement = document.createElement("div");
    let levelNOfMElement = document.createElement("div");

    let beforeEachDonePromise: Promise<any[]> | undefined = undefined;

    beforeEach(() => {
        beforeEachDonePromise = undefined;

        //noinspection JSUndeclaredVariable
        //reviewslog: this is not allowed: theOneLanguageDataInstance = null;
        ResetLanguageDataInstance();

        getTheOneReaderToolsModel().clearForTest(); /// brute force way to reset global state

        const settings: any = {};
        settings.letters =
            "a b c d e f g h i j k l m n o p q r s t u v w x y z";
        settings.moreWords = "catty sat rate bob fob big wig fig rig";
        settings.stages = [];
        settings.levels = [];

        settings.stages.push({
            letters: "a c e r s t y",
            sightWords: "feline rodent",
        });
        settings.stages.push({ letters: "b f o", sightWords: "one two" });
        settings.stages.push({ letters: "g i w", sightWords: "fruit nut" });

        settings.levels.push({
            maxWordsPerSentence: "3",
            maxWordsPerPage: "6",
            maxWordsPerBook: "90",
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""],
        });
        settings.levels.push({
            maxWordsPerSentence: "5",
            maxWordsPerPage: "10",
            maxWordsPerBook: "100",
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""],
        });
        settings.levels.push({
            maxWordsPerSentence: "7",
            maxWordsPerPage: "14",
            maxWordsPerBook: "110",
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""],
        });

        const api = new ReadersSynphonyWrapper();
        getTheOneReaderToolsModel().synphony = api;
        api.loadSettings(settings);

        const sampleFileContents =
            "catty catty, sat sat sat sat sat sat sat sat, bob bob bob, fob fob, wig, fig fig fig fig fig fig, rig, catty, sat bob fob fig, sat fig, sat";
        getTheOneReaderToolsModel().addWordsFromFile(sampleFileContents);

        getTheOneReaderToolsModel().addWordsToSynphony();

        const setStageDone = getTheOneReaderToolsModel().setStageNumber(1);
        getTheOneReaderToolsModel().wordListLoaded = true;

        vi.spyOn(getTheOneReaderToolsModel(), "updateElementContent");
        // simulated values of class attribute. Currently we ignore the attrName argument, since we only modify class.
        classValues = {
            decStage: "something",
            incStage: "something",
            decLevel: "something",
            incLevel: "something",
        };
        getTheOneReaderToolsModel().setElementAttribute = (
            elementId,
            attrName,
            val,
        ) => {
            classValues[elementId] = val;
        };

        //noinspection JSUnusedLocalSymbols
        getTheOneReaderToolsModel().getElementAttribute = (
            elementId,
            attrName,
        ) => {
            const result = classValues[elementId];
            if (result) {
                return result;
            }
            return "";
        };

        stageNOfMElement = document.createElement("div");
        stageNOfMElement.id = "stageNofM";
        stageNOfMElement.innerText = "Stage 0 of 0";
        document.body.appendChild(stageNOfMElement);

        levelNOfMElement = document.createElement("div");
        levelNOfMElement.id = "levelNofM";
        levelNOfMElement.innerText = "Level 0 of 0";
        document.body.appendChild(levelNOfMElement);

        beforeEachDonePromise = Promise.all([setStageDone]);
    });

    afterEach(() => {
        document.body.removeChild(stageNOfMElement);
        document.body.removeChild(levelNOfMElement);
    });

    it("increments level to limit on level right button", () => {
        getTheOneReaderToolsModel().incrementLevel();
        expect(levelNOfMElement.innerText).toBe("Level 2 of 3");

        getTheOneReaderToolsModel().incrementLevel();
        expect(levelNOfMElement.innerText).toBe("Level 3 of 3");

        // Expect that it doesn't change if you try to increment while at the highest value already
        getTheOneReaderToolsModel().incrementLevel();
        expect(levelNOfMElement.innerText).toBe("Level 3 of 3");
    });

    it("decrements level to 1 on level left button", () => {
        getTheOneReaderToolsModel().setLevelNumber(3);
        getTheOneReaderToolsModel().decrementLevel();
        expect(levelNOfMElement.innerText).toBe("Level 2 of 3");

        getTheOneReaderToolsModel().decrementLevel();
        expect(levelNOfMElement.innerText).toBe("Level 1 of 3");

        // Expect that it doesn't change if you try to decrement while at the lowest value already
        getTheOneReaderToolsModel().decrementLevel();
        expect(levelNOfMElement.innerText).toBe("Level 1 of 3");
    });

    it("updates level button visibility when setting level", () => {
        getTheOneReaderToolsModel().setLevelNumber(3);
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "decLevel",
                "class",
            ),
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "incLevel",
                "class",
            ),
        ).toBe("something disabledIcon");

        getTheOneReaderToolsModel().decrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "incLevel",
                "class",
            ),
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "decLevel",
                "class",
            ),
        ).toBe("something");

        getTheOneReaderToolsModel().decrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "incLevel",
                "class",
            ),
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "decLevel",
                "class",
            ),
        ).toBe("something disabledIcon");

        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "incLevel",
                "class",
            ),
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "decLevel",
                "class",
            ),
        ).toBe("something");

        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "decLevel",
                "class",
            ),
        ).toBe("something");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "incLevel",
                "class",
            ),
        ).toBe("something disabledIcon");
    });

    it("updates content of level element when setting level", () => {
        getTheOneReaderToolsModel().setLevelNumber(3);
        expect(levelNOfMElement.innerText).toBe("Level 3 of 3");
    });

    it("updates level buttons on init", () => {
        getTheOneReaderToolsModel().updateControlContents();
        expect(levelNOfMElement.innerText).toBe("Level 1 of 3");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "decLevel",
                "class",
            ),
        ).toBe("something disabledIcon");
    });

    it("sets level max values on init", () => {
        getTheOneReaderToolsModel().updateControlContents();
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).toHaveBeenCalledWith("maxWordsPerPage", "6");
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).toHaveBeenCalledWith("maxWordsPerPageBook", "6");
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).toHaveBeenCalledWith("maxWordsPerBook", "90");
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).toHaveBeenCalledWith("maxWordsPerSentence", "3");
        //expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "0");
        expect(
            getTheOneReaderToolsModel().getElementAttribute(
                "maxWordsPerBook",
                "class",
            ),
        ).toBe("");
    });

    it("updates max values when level changes", () => {
        getTheOneReaderToolsModel().incrementLevel();
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).toHaveBeenCalledWith("maxWordsPerPage", "10");
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).toHaveBeenCalledWith("maxWordsPerPageBook", "10");
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).not.toHaveBeenCalledWith("maxWordsPerBook", "0");
        expect(
            getTheOneReaderToolsModel().updateElementContent,
        ).toHaveBeenCalledWith("maxWordsPerSentence", "5");
        //expect(getTheOneReaderToolsModel().updateElementContent).toHaveBeenCalledWith("maxUniqueWordsPerBook", "12");
        //expect(getTheOneReaderToolsModel().getElementAttribute("maxWordsPerBook", "class")).toBe("disabledLimit");
    });
});
