import { describe, it, expect, beforeEach } from "vitest";
import { ResetLanguageDataInstance } from "./libSynphony/synphony_lib";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";

describe("Bloom Edit Controls tests", () => {
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
            maxWordsPerSentence: 3,
            maxWordsPerPage: 6,
            maxWordsPerBook: 90,
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""],
        });
        settings.levels.push({
            maxWordsPerSentence: 5,
            maxWordsPerPage: 10,
            maxWordsPerBook: 100,
            maxUniqueWordsPerBook: "",
            thingsToRemember: [""],
        });
        settings.levels.push({
            maxWordsPerSentence: 7,
            maxWordsPerPage: 14,
            maxWordsPerBook: 110,
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

        beforeEachDonePromise = Promise.all([setStageDone]);
    });

    it("increments level up to the number of levels, then clamps", () => {
        const model = getTheOneReaderToolsModel();
        model.setLevelNumber(1);
        expect(model.levelNumber).toBe(1);
        model.incrementLevel();
        expect(model.levelNumber).toBe(2);
        model.incrementLevel();
        expect(model.levelNumber).toBe(3);
        // at the max (3 levels), increment should not go past
        model.incrementLevel();
        expect(model.levelNumber).toBe(3);
    });
    it("decrements level down to 1, then clamps", () => {
        const model = getTheOneReaderToolsModel();
        model.setLevelNumber(3);
        expect(model.levelNumber).toBe(3);
        model.decrementLevel();
        expect(model.levelNumber).toBe(2);
        model.decrementLevel();
        expect(model.levelNumber).toBe(1);
        // at the min, decrement should not go below 1
        model.decrementLevel();
        expect(model.levelNumber).toBe(1);
    });
    it("reports the correct number of levels", () => {
        expect(getTheOneReaderToolsModel().getNumberOfLevels()).toBe(3);
    });
    it("exposes max values that track the current level", () => {
        const model = getTheOneReaderToolsModel();
        // settings.levels[0]: maxWordsPerPage 6, maxWordsPerSentence 3, maxWordsPerBook 90
        model.setLevelNumber(1);
        expect(model.maxWordsPerPage()).toBe(6);
        expect(model.maxWordsPerSentenceOnThisPage()).toBe(3);
        expect(model.maxWordsPerBook()).toBe(90);
        // settings.levels[1]: 10, 5, 100
        model.setLevelNumber(2);
        expect(model.maxWordsPerPage()).toBe(10);
        expect(model.maxWordsPerSentenceOnThisPage()).toBe(5);
        expect(model.maxWordsPerBook()).toBe(100);
    });
});
