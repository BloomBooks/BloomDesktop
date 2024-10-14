/// <reference path="./ReadersSynphonyWrapper.ts" />
/// <reference path="./libSynphony/jquery.text-markup.d.ts" />
/// <reference path="./jquery.div-columns.ts" />
/// <reference path="../../../typings/jqueryui/jqueryui.d.ts" />
/// <reference path="../../js/editableDivUtils.ts" />
/// <reference path="./directoryWatcher.ts" />
/// <reference path="../../../lib/localizationManager/localizationManager.ts" />
/// <reference path="readerTools.ts" />
/// <reference path="./libSynphony/synphony_lib.d.ts" />
import { DirectoryWatcher } from "./directoryWatcher";
import { resizeWordList } from "./readerTools";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import "./libSynphony/jquery.text-markup.ts";
import { removeAllHtmlMarkupFromString } from "./libSynphony/jquery.text-markup";
import "./jquery.div-columns.ts";
import { ReaderStage, ReaderLevel } from "./ReaderSettings";
import * as _ from "underscore";
import {
    theOneLanguageDataInstance,
    theOneLibSynphony
} from "./libSynphony/synphony_lib";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { DataWord, TextFragment } from "./libSynphony/bloomSynphonyExtensions";
import axios from "axios";
import {
    get,
    getWithConfig,
    post,
    postData,
    postString
} from "../../../utils/bloomApi";
import { EditableDivUtils } from "../../js/editableDivUtils";
import {
    allPromiseSettled,
    setTimeoutPromise
} from "../../../utils/asyncUtils";

const SortType = {
    alphabetic: "alphabetic",
    byLength: "byLength",
    byFrequency: "byFrequency"
};

export const MarkupType = {
    None: 0,
    Leveled: 1,
    Decodable: 2
};

const sortIconSelectedClass = "sortIconSelected"; // The class we apply to the selected sort icon
const disabledIconClass = "disabledIcon"; // The class we apply to icons that are disabled.
const disabledLimitClass = "disabledLimit"; // The class we apply to max values that are disabled (0).

export class DRTState {
    public stage: number = 1;
    public level: number = 1;
    public markupType: number = MarkupType.Decodable;
}

export class ReaderToolsModel {
    public previousHeight: number = 0;
    public previousWidth: number = 0;

    public stageNumber: number = 1;
    public levelNumber: number = 1;
    public synphony: ReadersSynphonyWrapper | undefined; // to ensure detection of async issues, don't init until we load its settings
    public sort: string = SortType.alphabetic;
    public currentMarkupType: number = MarkupType.None;
    public allWords = {};
    public texts: string[] = [];
    public setupType: string = "";
    public fontName: string = "";
    public readableFileExtensions: string[] = [];
    public directoryWatcher: DirectoryWatcher | undefined;
    public maxAllowedWords: number = 10000;

    // remember words so we can update the counts real-time
    public pageIDToText: any[] = [];

    // BL-599: Speed up the decodable reader tool
    public stageGraphemes: string[] = [];

    public activeElement: HTMLElement | undefined;
    public undoStack: any[];
    public redoStack: any[];

    public wordListChangedListeners: any = {};

    // some things need to wait until the word list has finished loading
    public wordListLoaded: boolean = false;
    public ckEditorLoaded: boolean = false;
    public allowedWordFilesRemaining: number = 0;

    /**
     * @summary Performs synchronous initialization. Call initAsync() after constructor to do the asynchronous initialization
     */
    public constructor() {
        // Nothing needed here anymore, everything happens in initAsync now.
    }

    // Should be called after the constructor.
    // Performs any initialization that should happen in the constructor, but is asynchronous.
    // (The constructor must return synchronously)
    public async initAsync(): Promise<void> {
        const keys = {
            "EditTab.Toolbox.DecodableReaderTool.StageNofM": "Stage {0} of {1}",
            "EditTab.Toolbox.LeveledReaderTool.LevelNofM": "Level {0} of {1}"
        };

        // Asynchronously pre-load the localizations (in the UI language) of the specified keys
        // Note: My observation, as of 5.1, is that the strings are loaded prior to when readerToolModels needs them,
        // so that is a good outcome for us. But, strictly speaking, we can't guarantee that.
        return theOneLocalizationManager.loadStringsPromise(keys, null);
    }

    public clearForTest() {
        this.stageNumber = 1;
        this.levelNumber = 1;
        this.synphony = undefined;
        this.sort = SortType.alphabetic;
        this.currentMarkupType = MarkupType.None;
        this.allWords = {};
        this.texts = [];
        this.setupType = "";
        this.fontName = "";
        this.readableFileExtensions = [];
        this.directoryWatcher = undefined;
        this.maxAllowedWords = 10000;
        this.pageIDToText = [];
        this.stageGraphemes = [];
        this.activeElement = undefined;
        this.undoStack = [];
        this.redoStack = [];
        this.wordListChangedListeners = {};
    }
    public getReadableFileExtensions() {
        return ["txt", "js", "json"];
    }

    public readyToDoMarkup(): boolean {
        return this.wordListLoaded && this.ckEditorLoaded;
    }
    public setCkEditorLoaded(): void {
        this.ckEditorLoaded = true;
    }

    public incrementStage(): void {
        this.setStageNumber(this.stageNumber + 1);
    }

    public decrementStage(): void {
        this.setStageNumber(this.stageNumber - 1);
    }

    public async setStageNumber(
        stage: number,
        skipSave?: boolean
    ): Promise<void> {
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        // this much needs to be done immediately; otherwise, the result of
        // different routines calling setStageNumber is unpredictable, depending on
        // when the different asyncGetText() calls complete.
        const stages = this.synphony.getStages();
        if (stage < 1 || stage > stages.length) {
            return;
        }

        this.stageNumber = stage;
        this.updateStageNOfMDisplay();
        this.updateStageButtonsAvailability();

        return theOneLocalizationManager
            .asyncGetText("Common.Loading", "Loading...", "")
            .then(loadingMessage => {
                // this may result in a need to resize the word list
                this.previousHeight = 0;
                $("#letterList").html(loadingMessage);
                $("#wordList").html(loadingMessage);

                // OK, now let that changed number and the "loading" messages
                // make it to the user's screen, then start doing the work.
                return setTimeoutPromise(async () => {
                    if (this.stageNumber === stage) {
                        this.stageGraphemes = this.getKnownGraphemes(stage);
                    }

                    // Both setting the letters and words require that to be done,
                    // but they can be done independently of each other.
                    // By separating them, we allow the letters to update while
                    // the words are still being generated.
                    const updateLetterListDonePromise = setTimeoutPromise(
                        () => {
                            // make sure this is still the stage they want
                            // (it won't be if they are rapidly clicking the next/previous stage buttons)
                            if (this.stageNumber === stage) {
                                this.updateLetterList();
                            }
                        },
                        0
                    );

                    const updateWordListDonePromise = setTimeoutPromise(() => {
                        // make sure this is still the stage they want
                        // (it won't be if they are rapidly clicking the next/previous stage buttons)
                        if (this.stageNumber === stage) {
                            this.updateWordList();
                        }
                    }, 0);

                    const defaultStagePostedPromise = new Promise<void>(
                        (resolve, reject) => {
                            if (!skipSave) {
                                this.saveState();
                                // When we're actually changing the stage number is the only time we want
                                // to update the default.
                                post(
                                    "readers/io/defaultStage?stage=" +
                                        this.stageNumber,
                                    () => {
                                        resolve();
                                    },
                                    r => {
                                        reject(r);
                                    }
                                );
                            } else {
                                resolve();
                            }
                        }
                    );

                    if (this.readyToDoMarkup()) {
                        this.doMarkup();
                    }

                    return allPromiseSettled([
                        updateLetterListDonePromise,
                        updateWordListDonePromise,
                        defaultStagePostedPromise
                    ]);

                    // The 1/2 second delay here gives us a chance to click quickly and change the stage before we start working
                    // If that happens, the check that is the first line of this setTimeout function will decide to bail out.
                }, 500);
            });
    }

    private updateStageNumberIfNeeded(): void {
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        const stages = this.synphony.getStages();
        if (stages.length <= 0) {
            // Note: an existing bug is that if you are on stage 2 of 2, then go to change settings, remove both stages, and accept,
            // the decrement stage button will still show up.
            // Nothing triggers updateStageButtonsAvailability to run again in that case, so it still retains the status back when it was on stage 2.
            return;
        }
        if (this.stageNumber > stages.length) {
            // You can reach this case if you had the last stage selected, then delete the last stage.
            this.stageNumber = stages.length;
        }
    }

    // Gets the string representing which stage we're on.
    // Precondition: the stage number must be properly updated first. Synphony should be loaded too, else will return undefined.
    private getStageLabel() {
        if (!this.synphony) {
            return undefined; // Synphony not loaded yet
        }

        const stages = this.synphony.getStages();
        if (stages.length <= 0) {
            return "0";
        }
        return stages[this.stageNumber - 1].getName();
    }

    public incrementLevel(): void {
        this.setLevelNumber(this.levelNumber + 1);
    }

    public decrementLevel(): void {
        this.setLevelNumber(this.levelNumber - 1);
    }

    public setLevelNumber(val: number, skipSave?: boolean): void {
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        const levels = this.synphony.getLevels();
        if (val < 1 || val > levels.length) {
            return;
        }
        this.levelNumber = val;
        this.updateLevelNOfMDisplay();
        this.enableLevelButtons();
        this.updateLevelLimits();
        if (!skipSave) {
            this.saveState();
            // When we're actually changing the level is the only time we want
            // to update the default.
            post("readers/io/defaultLevel?level=" + this.levelNumber);
        }
        this.doMarkup();
    }

    private updateLevelNumberIfNeeded(): void {
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        const levels = this.synphony.getLevels();
        if (levels.length <= 0) {
            // Note: an existing bug is that if you are on level 2 of 2, then go to change settings, remove both levels, and accept,
            // the decrement level button will still show up.
            // Nothing triggers enableLevelButtons to run again in that case, so it still retains the status back when it was on level 2.
            return;
        }

        if (levels.length < this.levelNumber) {
            // You can reach this case if you had the last level selected, then delete the last level.
            this.setLevelNumber(levels.length);
        }
    }

    // Gets the string representing which level we're on.
    // Precondition: the level number must be properly updated first. Synphony should be loaded too, else will return undefined.
    private getLevelLabel() {
        if (!this.synphony) {
            return undefined; // Synphony not loaded yet
        }

        const levels = this.synphony.getLevels();

        if (levels.length <= 0) {
            return "0";
        }
        return levels[this.levelNumber - 1].getName();
    }

    public sortByLength(): void {
        this.setSort(SortType.byLength);
    }

    public sortByFrequency(): void {
        this.setSort(SortType.byFrequency);
    }

    public sortAlphabetically(): void {
        this.setSort(SortType.alphabetic);
    }

    public setSort(sortType: string, skipSave?: boolean): void {
        this.sort = sortType;
        this.updateSortStatus();
        this.updateWordList();
        if (!skipSave) {
            this.saveState();
        }
    }

    public updateSortStatus(): void {
        this.updateSelectedStatus(
            "sortAlphabetic",
            this.sort === SortType.alphabetic
        );
        this.updateSelectedStatus(
            "sortLength",
            this.sort === SortType.byLength
        );
        this.updateSelectedStatus(
            "sortFrequency",
            this.sort === SortType.byFrequency
        );
    }

    public updateSelectedStatus(eltId: string, isSelected: boolean): void {
        this.setPresenceOfClass(eltId, isSelected, sortIconSelectedClass);
    }

    /**
     * Should be called when the browser has loaded the page, and when the user has changed configuration.
     * It updates various things in the UI to be consistent with the state of things in the model.
     */
    public updateControlContents(): void {
        this.updateLetterList();
        this.updateStageNOfMDisplay();
        this.updateLevelNOfMDisplay();
        this.updateStageButtonsAvailability();
        this.enableLevelButtons();
        this.updateLevelLimits();
        this.updateWordList();
    }

    public updateStageNOfMDisplay() {
        this.updateStageNumberIfNeeded(); // May change the stage number

        const stageParent = document.getElementById("stageNofM");
        if (!stageParent) {
            return;
        }

        this.updateStageNOfMInternal(stageParent);
    }

    public updateStageNOfMInternal(stageParent: HTMLElement) {
        const stageLabel = this.getStageLabel() ?? "0";
        const numStages = this.getNumberOfStages() ?? 0;

        // Note: getText is synchronous. If the string's translation has not been loaded yet, it will just return the English synchronously.
        // I want this function to be synchronous in order so that the callers and all their dependencies
        // don't have to worry about asynchonously waiting for this function to complete.
        // The constructor does pre-load the translations at construction time, and it does seem to complete with plenty of time to spare.
        // So, hopefully we will always have the string in the UI language already ready to go when we reach here.
        // If not, it's not the end of the world to display it in English at first. (This function gets called several times,
        // so one would expect that even if the localizations aren't ready the first time, it ought to be ready by the last time)
        const localizedFormattedText = theOneLocalizationManager.getText(
            "EditTab.Toolbox.DecodableReaderTool.StageNofM",
            "Stage {0} of {1}",
            stageLabel,
            numStages.toString()
        );

        stageParent.innerText = localizedFormattedText;
    }

    public updateLevelNOfMDisplay() {
        this.updateLevelNumberIfNeeded(); // May change the level number

        const levelParent = document.getElementById("levelNofM");
        if (!levelParent) {
            return;
        }

        this.updateLevelNOfMInternal(levelParent);
    }

    public updateLevelNOfMInternal(levelParent: HTMLElement) {
        const levelLabel = this.getLevelLabel() ?? "0";
        const numLevels = this.getNumberOfLevels() ?? 0;

        const localizedFormattedText = theOneLocalizationManager.getText(
            "EditTab.Toolbox.LeveledReaderTool.LevelNofM",
            "Level {0} of {1}",
            levelLabel,
            numLevels.toString()
        );

        levelParent.innerText = localizedFormattedText;
    }

    public getNumberOfStages() {
        return this.synphony?.getStages().length;
    }

    public getNumberOfLevels() {
        return this.synphony?.getLevels().length;
    }

    public updateStageButtonsAvailability(): void {
        this.updateDisabledStatus("decStage", this.stageNumber <= 1);
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        this.updateDisabledStatus(
            "incStage",
            this.stageNumber >= this.synphony.getStages().length
        );
    }

    public updateDisabledStatus(eltId: string, isDisabled: boolean): void {
        this.setPresenceOfClass(eltId, isDisabled, disabledIconClass);
    }

    /**
     * Find the element with the indicated ID, and make sure that it has the className in its class attribute
     * if isWanted is true, and not otherwise.
     * (Tests currently assume it will be added last, but this is not required.)
     * (class names used with this method should not occur as sub-strings within a longer class name)
     */
    public setPresenceOfClass(
        eltId: string,
        isWanted: boolean,
        className: string
    ): void {
        let old = this.getElementAttribute(eltId, "class");

        // this can happen during testing
        if (!old) old = "";

        if (isWanted && old.indexOf(className) < 0) {
            this.setElementAttribute(
                eltId,
                "class",
                old + (old.length ? " " : "") + className
            );
        } else if (!isWanted && old.indexOf(className) >= 0) {
            this.setElementAttribute(
                eltId,
                "class",
                old
                    .replace(className, "")
                    .replace("  ", " ")
                    .trim()
            );
        }
    }

    public enableLevelButtons(): void {
        this.updateDisabledStatus("decLevel", this.levelNumber <= 1);
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        this.updateDisabledStatus(
            "incLevel",
            this.levelNumber >= this.synphony.getLevels().length
        );
    }

    public updateLevelLimits(): void {
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        let level = this.synphony.getLevels()[this.levelNumber - 1];
        if (!level) level = new ReaderLevel("");

        this.updateLevelLimit("maxWordsPerPage", level.getMaxWordsPerPage());
        this.updateLevelLimit(
            "maxWordsPerPageBook",
            level.getMaxWordsPerPage()
        );
        this.updateLevelLimit(
            "maxWordsPerSentence",
            level.getMaxWordsPerSentence()
        );
        this.updateLevelLimit("maxWordsPerBook", level.getMaxWordsPerBook());
        this.updateLevelLimit(
            "maxUniqueWordsPerBook",
            level.getMaxUniqueWordsPerBook()
        );
        this.updateLevelLimit(
            "maxAverageWordsPerSentence",
            level.getMaxAverageWordsPerSentence()
        );
        this.updateLevelLimit(
            "maxAverageGlyphsPerWord",
            level.getMaxAverageGlyphsPerWord()
        );
        this.updateLevelLimit(
            "maxAverageSentencesPerPage",
            level.getMaxAverageSentencesPerPage()
        );
        this.updateLevelLimit(
            "maxAverageWordsPerPage",
            level.getMaxAverageWordsPerPage()
        );
        this.updateLevelLimit(
            "maxSentencesPerPage",
            level.getMaxSentencesPerPage()
        );
        this.updateLevelLimit("maxLettersPerWord", level.getMaxGlyphsPerWord());

        if (level.thingsToRemember.length) {
            const list = document.getElementById("thingsToRemember");
            if (list !== null) {
                list.innerHTML = "";

                for (let i = 0; i < level.thingsToRemember.length; i++) {
                    const li = document.createElement("li");
                    li.innerHTML = level.thingsToRemember[i];
                    list.appendChild(li);
                }
            }
        }
    }

    public updateLevelLimit(id: string, limit: number): void {
        if (limit !== 0) {
            this.updateElementContent(id, limit.toString());
        }
        this.updateDisabledLimit(id, limit === 0);
    }

    public updateDisabledLimit(eltId: string, isDisabled: boolean): void {
        this.setPresenceOfClass(eltId, isDisabled, disabledLimitClass);
    }

    /**
     * Displays the list of words for the current Stage.
     */
    public updateWordList(): void {
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        // show the correct headings
        //reviewSLog
        const useAllowedWords = this.synphony.source
            ? this.synphony.source.useAllowedWords === 1
            : false;

        // this happens during unit testing
        const mlwld = document.getElementById("make-letter-word-list-div");
        if (mlwld) {
            mlwld.style.display = useAllowedWords ? "none" : "";
            this.setDisplayForHTMLElementById(
                "letters-in-this-stage",
                useAllowedWords
            );
            const ltrList = document.getElementById("letterList");
            if (ltrList && ltrList.parentElement) {
                this.setDisplayForHTMLElement(
                    ltrList.parentElement,
                    useAllowedWords
                );
            }
            this.setDisplayForHTMLElementById(
                "sample-words-this-stage",
                useAllowedWords
            );
            this.setDisplayForHTMLElementById("sortFrequency", useAllowedWords);
            this.setDisplayForHTMLElementById(
                "allowed-words-this-stage",
                !useAllowedWords
            );
            this.setDisplayForHTMLElementById(
                "allowed-word-list-truncated",
                !useAllowedWords
            );
        }

        if (!this.readyToDoMarkup()) return;

        const wordList = document.getElementById("wordList");
        if (wordList) wordList.innerHTML = "";

        const stages = this.synphony.getStages();
        if (stages.length === 0) return;

        let words: DataWord[] = [];
        if (useAllowedWords)
            words = this.getAllowedWordsAsObjects(this.stageNumber);
        else {
            const stageWords = this.getStageWordsAndSightWords(
                this.stageNumber
            );
            if (stageWords) {
                words = stageWords;
            }
        }

        resizeWordList(false);

        // All cases use localeCompare for alphabetic sort. This is not ideal; it will use whatever
        // locale the browser thinks is current. When we implement ldml-dependent sorting we can improve this.
        switch (this.sort) {
            case SortType.alphabetic:
                words.sort((a: DataWord, b: DataWord) => {
                    return a.Name.localeCompare(b.Name);
                });
                break;
            case SortType.byLength:
                words.sort((a: DataWord, b: DataWord) => {
                    if (a.Name.length === b.Name.length) {
                        return a.Name.localeCompare(b.Name);
                    }
                    return a.Name.length - b.Name.length;
                });
                break;
            case SortType.byFrequency:
                words.sort((a: DataWord, b: DataWord) => {
                    const aFreq = a.Count;
                    const bFreq = b.Count;
                    if (aFreq === bFreq) {
                        return a.Name.localeCompare(b.Name);
                    }
                    return bFreq - aFreq; // MOST frequent first
                });
                break;
            default:
        }

        // add the words
        let result = "";
        let longestWord = "";
        for (let i = 0; i < words.length; i++) {
            const w: DataWord = words[i];
            // Sigh. LangID is always null
            // result += `<div lang='${
            //     theOneLanguageDataInstance.LangID
            // }' class="word ${w.isSightWord ? " sight-word" : ""}"> ${w.Name}-${
            //     theOneLanguageDataInstance.LangID
            // }</div>`;

            // NOTE: The generated HTML would look nicer without the space immediately after lang1InATool
            // NOTE 2: The HTML would also look nicer without the extra space between the end of the open tag and the contents
            result += `<div class="word lang1InATool ${
                w.isSightWord ? " sight-word" : ""
            }"> ${w.Name}</div>`;

            if (w.Name.length > longestWord.length) longestWord = w.Name;
        }
        const div = $("div.wordList");
        div.css("font-family", this.fontName);

        this.updateElementContent("wordList", result);

        $.divsToColumnsBasedOnLongestWord("word", longestWord);
    }

    private setDisplayForHTMLElement(
        element: HTMLElement,
        conditionForNone: boolean
    ): void {
        if (!element) return; // safety first!
        element.style.display = conditionForNone ? "none" : "";
    }

    private setDisplayForHTMLElementById(
        elementId: string,
        conditionForNone: boolean
    ): void {
        const elt = document.getElementById(elementId);
        if (!elt) return;
        this.setDisplayForHTMLElement(elt, conditionForNone);
    }

    /**
     * Displays the list of letters for the current Stage.
     */
    public updateLetterList(): void {
        if (!this.synphony) {
            return; // Synphony not loaded yet
        }
        const stages = this.synphony.getStages();
        if (stages.length === 0) {
            // In case the user deletes all stages, and something had been displayed before.
            this.updateElementContent("letterList", "");
            return;
        }

        if (this.stageNumber > 0) {
            this.stageGraphemes = this.getKnownGraphemes(this.stageNumber); //BL-838
        }

        // Letters up through current stage
        const letters = this.stageGraphemes;

        // All the letters in the order they were entered on the Letters tab in the set up dialog
        const allLetters = this.synphony.source.letters.split(" ");

        // Sort our letters based on the order they were entered
        letters.sort((a, b) => {
            return allLetters.indexOf(a) - allLetters.indexOf(b);
        });

        let result = "";
        for (let i = 0; i < letters.length; i++) {
            const letter = letters[i];
            result +=
                // Alas, LangID is always null in this monstrosity of a code base
                // `<div lang='${
                //     theOneLanguageDataInstance.LangID
                // }' class="letter">` +
                `<div lang='en' class="lang1InATool letter">` +
                letter +
                "</div>";
        }
        const div = $("div.letterList");
        div.css("font-family", this.fontName);

        this.updateElementContent("letterList", result);

        $.divsToColumns("letter");
    }

    /**
     * Get the sight words for the current stage and all previous stages.
     * Note: The list returned may contain sight words from previous stages that are now decodable.
     * @param stageNumber
     * @returns An array of strings
     */
    public getSightWords(stageNumber?: number): string[] {
        let sightWords: string[] = [];
        if (!this.synphony) {
            return sightWords; // Synphony not loaded yet
        }
        const stages: ReaderStage[] = this.synphony.getStages(stageNumber);
        if (stages.length > 0) {
            for (let i = 0; i < stages.length; i++) {
                if (stages[i].sightWords)
                    sightWords = _.union(
                        sightWords,
                        stages[i].sightWords.split(" ")
                    );
            }
        }

        return sightWords;
    }

    /**
     * Get the sight words for the current stage and all previous stages as an array of DataWord objects
     * Note: The list returned may contain sight words from previous stages that are now decodable.
     * @param stageNumber
     * @returns An array of DataWord objects
     */
    public getSightWordsAsObjects(stageNumber: number): DataWord[] {
        const words: string[] = this.getSightWords(stageNumber);
        const returnVal: DataWord[] = [];

        for (let i = 0; i < words.length; i++) {
            const dw = new DataWord(words[i]);
            // Ensure a proper count for sight words found in the sample text data.
            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-6264.
            const possibleCount = this.allWords[words[i]];
            if (possibleCount) {
                dw.Count = possibleCount;
            } else {
                dw.Count = 0; // Not found in sample text data.
            }
            dw.isSightWord = true;
            returnVal.push(dw);
        }

        return returnVal;
    }

    /**
     * Get the graphemes for the current stage and all previous stages
     * @param stageNumber
     * @returns An array of strings
     */
    public getKnownGraphemes(stageNumber: number): string[] {
        if (!this.synphony) {
            return []; // Synphony not loaded yet
        }
        const stages = this.synphony.getStages(stageNumber);

        // compact to remove empty items if no graphemes are selected
        return _.compact(
            _.pluck(stages, "letters")
                .join(" ")
                .split(" ")
        );
    }

    /**
     *
     * @returns An array of DataWord objects
     */
    public getStageWords(): DataWord[] {
        if (!this.stageGraphemes || this.stageGraphemes.length === 0) return [];
        return this.selectWordsFromSynphony(
            false,
            this.stageGraphemes,
            this.stageGraphemes,
            true,
            true
        );
    }

    public getStageWordsAndSightWords(stageNumber: number): DataWord[] | null {
        if (!this.readyToDoMarkup()) return null;

        // first get the sight words
        const sightWords = this.getSightWordsAsObjects(stageNumber);
        const stageWords = this.getStageWords();

        return _.uniq(stageWords.concat(sightWords), false, (w: DataWord) => {
            return w.Name;
        });
    }

    /**
     * Change the markup type when the user selects a different Tool.
     * @param {int} markupType
     * returns true if doMarkup called
     */
    public setMarkupType(markupType: number): boolean {
        let newMarkupType: number | undefined;
        switch (markupType) {
            case 1:
                if (this.currentMarkupType !== MarkupType.Decodable)
                    newMarkupType = MarkupType.Decodable;
                break;

            case 2:
                if (this.currentMarkupType !== MarkupType.Leveled)
                    newMarkupType = MarkupType.Leveled;
                break;

            default:
                if (this.currentMarkupType !== MarkupType.None)
                    newMarkupType = MarkupType.None;
                break;
        }

        // if no change, return now
        // Note that an enum value of 0 is not "truthy".
        // See https://silbloom.myjetbrains.com/youtrack/issue/BL-6485.
        if (!newMarkupType && newMarkupType !== MarkupType.None) return false;
        let didMarkup = false;

        if (newMarkupType !== this.currentMarkupType) {
            const contentWindow = this.safelyGetContentWindow();
            if (contentWindow)
                $(
                    ".bloom-editable",
                    contentWindow.document
                ).removeSynphonyMarkup();
            this.currentMarkupType = newMarkupType;
            this.doMarkup();
            didMarkup = true;
        }

        this.saveState();
        return didMarkup;
    }

    private safelyGetContentWindow(): Window | null {
        const page = parent.window.document.getElementById("page");
        if (!page) return null;
        return (<HTMLIFrameElement>page).contentWindow;
    }

    public getElementsToCheck(): JQuery {
        const contentWindow = this.safelyGetContentWindow();

        // this happens during unit testing
        if (!contentWindow) {
            return $(".bloom-page")
                .not(".bloom-frontMatter, .bloom-backMatter")
                .find(
                    ".bloom-translationGroup:not(.bloom-imageDescription) .bloom-content1"
                );
        }

        // if this is a cover page, return an empty set
        const cover = $("body", contentWindow.document).find("div.cover");
        if (cover["length"] > 0) return $();

        // not a cover page, return elements to check
        return (
            $(".bloom-page", contentWindow.document)
                .not(".bloom-frontMatter, .bloom-backMatter")
                // don't count image descriptions
                .find(
                    ".bloom-translationGroup:not(.bloom-imageDescription) .bloom-content1"
                )
        );
    }

    public noteFocus(element: HTMLElement): void {
        this.activeElement = element;
        this.undoStack = [];
        this.redoStack = [];
        this.undoStack.push({
            html: element.innerHTML,
            text: element.textContent,
            offset: EditableDivUtils.getElementSelectionIndex(
                this.activeElement
            )
        });
    }

    public shouldHandleUndo(): boolean {
        return this.currentMarkupType !== MarkupType.None;
    }

    public undo(): void {
        if (!this.activeElement) return;
        if (
            this.activeElement.textContent ==
                this.undoStack[this.undoStack.length - 1].text &&
            this.undoStack.length > 1
        ) {
            this.redoStack.push(this.undoStack.pop());
        }
        this.activeElement.innerHTML = this.undoStack[
            this.undoStack.length - 1
        ].html;
        const restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
        if (restoreOffset < 0) return;
        EditableDivUtils.makeSelectionIn(
            this.activeElement,
            restoreOffset,
            -1,
            true
        );
    }

    public canUndo(): boolean {
        if (!this.activeElement) return false;
        if (
            this.undoStack &&
            (this.undoStack.length > 1 ||
                this.activeElement.textContent !== this.undoStack[0].text)
        ) {
            return true;
        }
        return false;
    }

    public redo(): void {
        if (!this.activeElement) return;
        if (this.redoStack.length > 0) {
            this.undoStack.push(this.redoStack.pop());
        }
        this.activeElement.innerHTML = this.undoStack[
            this.undoStack.length - 1
        ].html;
        const restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
        if (restoreOffset < 0) return;
        EditableDivUtils.makeSelectionIn(
            this.activeElement,
            restoreOffset,
            -1,
            true
        );
    }

    public getTopPageWindow(): Window | null {
        const page = top?.document.getElementById("page");
        if (!page) return null;
        return (<HTMLIFrameElement>page).contentWindow;
    }

    /**
     * Displays the correct markup for the current page.
     */
    private doMarkup(createCkEditorBookMarks: boolean = true): void {
        if (!this.readyToDoMarkup()) return;
        if (this.currentMarkupType === MarkupType.None) return;

        let oldSelectionPosition = -1;
        if (this.activeElement)
            oldSelectionPosition = EditableDivUtils.getElementSelectionIndex(
                this.activeElement
            );

        const editableElements = this.getElementsToCheck();

        // console.log("start of doMarkup:");
        // EditableDivUtils.logElementsInnerHtml(editableElements.toArray());

        let bookmarksForEachEditable: object[] = [];
        if (editableElements.length > 0) {
            // qtips can be orphaned if the element they belong to is deleted
            // (and so the mouse can't move off their owning element, and they never go away).
            // BL-2758 But if we're in a source collection we may have valid source bubbles,
            // don't delete them!
            $(editableElements[0])
                .closest("body")
                .children('.qtip:not(".uibloomSourceTextsBubble")')
                .remove();

            if (
                this.currentMarkupType === MarkupType.Decodable ||
                this.currentMarkupType === MarkupType.Leveled
            )
                bookmarksForEachEditable = EditableDivUtils.doCkEditorCleanup(
                    editableElements
                        .filter(
                            (index, element) => !!(element as HTMLDivElement)
                        )
                        .toArray() as HTMLDivElement[],
                    createCkEditorBookMarks
                );
        }

        switch (this.currentMarkupType) {
            case MarkupType.Leveled:
                if (editableElements.length > 0) {
                    const options = {
                        maxWordsPerSentence: this.maxWordsPerSentenceOnThisPage(),
                        maxWordsPerPage: this.maxWordsPerPage(),
                        maxGlyphsPerWord: this.maxGlyphsPerWord(),
                        maxSentencesPerPage: this.maxSentencesPerPage()
                    };
                    editableElements.checkLeveledReader(options);

                    // update current page words, but only if page is already known
                    // Some pages are (and should be) ignored.  See BL-9876.
                    const topPageWindow = this.getTopPageWindow();
                    if (topPageWindow) {
                        const pageDiv = $("body", topPageWindow.document).find(
                            "div.bloom-page"
                        );
                        if (
                            pageDiv.length &&
                            pageDiv[0].id &&
                            this.pageIDToText[pageDiv[0].id] !== undefined
                        )
                            this.pageIDToText[pageDiv[0].id] =
                                editableElements["allWords"];
                    }
                }

                this.displayBookTotals();
                break;

            case MarkupType.Decodable: {
                if (!this.synphony || editableElements.length == 0) return;

                // get current stage and all previous stages
                const stages = this.synphony.getStages(this.stageNumber);
                if (stages.length === 0) return;

                // get word lists
                let cumulativeWords: DataWord[];
                let sightWords: string[];
                if (this.synphony.source.useAllowedWords === 1) {
                    cumulativeWords = [];
                    sightWords = this.selectWordsFromAllowedLists(
                        this.stageNumber
                    );
                } else {
                    cumulativeWords = this.getStageWords();
                    sightWords = this.getSightWords(this.stageNumber);
                }

                editableElements.checkDecodableReader({
                    focusWords: cumulativeWords,
                    previousWords: cumulativeWords,
                    // theOneLibSynphony lowercases the text, so we must do the same with sight words.  (BL-2550)
                    sightWords: sightWords
                        .join(" ")
                        .toLowerCase()
                        .split(/\s/),
                    knownGraphemes: this.stageGraphemes
                });

                break;
            }

            default:
        }

        // console.log("in doMarkup, before restoreSelection:");
        // EditableDivUtils.logElementsInnerHtml(editableElements.toArray());

        if (bookmarksForEachEditable.length)
            EditableDivUtils.restoreSelectionFromCkEditorBookmarks(
                editableElements
                    .filter((index, element) => !!(element as HTMLDivElement))
                    .toArray() as HTMLDivElement[],
                bookmarksForEachEditable
            );

        // console.log("end of doMarkup:");
        // EditableDivUtils.logElementsInnerHtml(editableElements.toArray());

        if (
            this.activeElement &&
            this.activeElement.textContent !=
                this.undoStack[this.undoStack.length - 1].text
        ) {
            this.undoStack.push({
                html: this.activeElement.innerHTML,
                text: this.activeElement.textContent,
                offset: oldSelectionPosition
            });
            this.redoStack = []; // ok because only referred to by this variable.
        }

        // the contentWindow is not available during unit testing
        const contentWindow = this.safelyGetContentWindow();
        if (contentWindow) {
            contentWindow.postMessage("Qtips", "*");
        }
    }

    private getValueFromCurrentLevel(
        getter: (level: ReaderLevel) => number
    ): number {
        if (!this.synphony) {
            return Infinity; // not loaded yet
        }
        const levels: ReaderLevel[] = this.synphony.getLevels();
        if (levels.length <= 0) {
            return Infinity;
        }
        return getter(levels[this.levelNumber - 1]);
    }

    public maxWordsPerSentenceOnThisPage(): number {
        return this.getValueFromCurrentLevel(l => l.getMaxWordsPerSentence());
    }

    public maxWordsPerBook(): number {
        return this.getValueFromCurrentLevel(l => l.getMaxWordsPerBook());
    }

    public maxUniqueWordsPerBook(): number {
        return this.getValueFromCurrentLevel(l => l.getMaxUniqueWordsPerBook());
    }

    public maxSentencesPerBook(): number {
        return this.getValueFromCurrentLevel(l => l.getMaxSentencesPerBook());
    }

    public maxAverageWordsPerSentence(): number {
        return this.getValueFromCurrentLevel(l =>
            l.getMaxAverageWordsPerSentence()
        );
    }

    public maxAverageWordsPerPage(): number {
        return this.getValueFromCurrentLevel(l =>
            l.getMaxAverageWordsPerPage()
        );
    }

    public maxAverageGlyphsPerWord(): number {
        return this.getValueFromCurrentLevel(l =>
            l.getMaxAverageGlyphsPerWord()
        );
    }

    public maxAverageSentencesPerPage(): number {
        return this.getValueFromCurrentLevel(l =>
            l.getMaxAverageSentencesPerPage()
        );
    }

    public maxWordsPerPage(): number {
        return this.getValueFromCurrentLevel(l => l.getMaxWordsPerPage());
    }

    public maxSentencesPerPage(): number {
        return this.getValueFromCurrentLevel(l => l.getMaxSentencesPerPage());
    }

    public maxGlyphsPerWord(): number {
        return this.getValueFromCurrentLevel(l => l.getMaxGlyphsPerWord());
    }

    // Though I'm not using this now, it was hard-won, and instructive. So I'm leaving it here
    // as an example for now in case we need to do this transformResponse thing.
    //   getTextOfWholeBook(): void {
    //       //review: on the server, this is actually a json string
    //     axios.get<string>('/bloom/api/readers/textOfContentPages',
    //     {
    //       //when there are no content pages in the book, the server returns, properly, "{}"
    //       //However the default transformResponse of axios eagerly does a JSON.Parse on everything,
    //       //even if we say we only want text/plain and the server says it gave text/plain.  Sheesh.
    //       //So we specify our own identity transformResponse
    //         transformResponse:  (data: string) => <string>data }
    //     ).then(result => {
    //       //The return looks like {'12547c' : 'hello there', '898af87' : 'words of this page', etc.}
    //       this.pageIDToText = JSON.parse(result.data);
    //       this.doMarkup();
    //     });
    //   }

    public getTextOfWholeBook(): void {
        get("readers/io/textOfContentPages", result => {
            this.gettingTextOfWholeBook = false;
            //result.data looks like {'0bbf0bc5-4533-4c26-92d9-bea8fd064525:' : 'Jane saw spot', 'AAbf0bc5-4533-4c26-92d9-bea8fd064525:' : 'words of this page', etc.}
            this.pageIDToText = result.data as any[];
            this.doMarkup();
        });
    }

    public clearWholeBookCache() {
        this.pageIDToText = [];
    }

    private gettingTextOfWholeBook = false;
    private bookStatistics = {};

    public displayBookTotals(): void {
        if (this.gettingTextOfWholeBook) {
            return;
        }
        if (this.pageIDToText.length === 0) {
            this.gettingTextOfWholeBook = true;
            // The gettingTextOfWholeBook test is meant to stop repeated requests
            // getting the same data. In case we somehow don't get a result...
            // perhaps we deleted a page or something before it arrived?...
            // don't stop getting it forever.
            window.setTimeout(
                () => (this.gettingTextOfWholeBook = false),
                1000
            );
            this.getTextOfWholeBook();
            return;
        }

        this.bookStatistics = {};
        const pageStrings = _.values(this.pageIDToText).map(x =>
            removeAllHtmlMarkupFromString(x)
        );

        const pageElementsToCheck = this.getElementsToCheck();
        const pageText = pageElementsToCheck
            .toArray()
            .map(x => x.innerText) // this has newlines between paragraph content, text has none
            .join(" ");
        const sentences = theOneLibSynphony
            .stringToSentences(pageText)
            .filter(x => x.isSentence);
        const sentenceFragmentsByPage = this.getSentences(pageStrings);
        this.bookStatistics["levelNumber"] = this.levelNumber;
        this.bookStatistics["pageCount"] = sentenceFragmentsByPage.length;
        this.updateActualCount(
            ReaderToolsModel.countSentences(sentences),
            this.maxSentencesPerPage(),
            "actualSentencesPerPage"
        );

        this.updateActualCount(
            ReaderToolsModel.maxWordLength(pageText),
            this.maxGlyphsPerWord(),
            "actualLettersPerWord"
        );

        this.updateActualCount(
            pageElementsToCheck.getTotalWordCount(),
            this.maxWordsPerPage(),
            "actualWordsPerPage"
        );

        this.updateActualCount(
            pageElementsToCheck.getMaxSentenceLength(),
            this.maxWordsPerSentenceOnThisPage(),
            "actualWordsPerSentence"
        );

        this.updateActualCount(
            this.countWordsInBook(sentenceFragmentsByPage),
            this.maxWordsPerBook(),
            "actualWordCount"
        );
        this.updateActualCount(
            this.maxWordsPerPageInBook(sentenceFragmentsByPage),
            this.maxWordsPerPage(),
            "actualWordsPerPageBook"
        );
        this.updateActualCount(
            this.uniqueWordsInBook(sentenceFragmentsByPage),
            this.maxUniqueWordsPerBook(),
            "actualUniqueWords"
        );
        this.updateActualCount(
            ReaderToolsModel.totalSentencesInBook(sentenceFragmentsByPage),
            this.maxSentencesPerBook(),
            "actualSentenceCount"
        );
        this.updateActualCount(
            ReaderToolsModel.maxGlyphsInWord(sentenceFragmentsByPage),
            this.maxGlyphsPerWord(),
            "actualMaxGlyphsPerWord"
        );
        this.updateActualCount(
            ReaderToolsModel.maxSentenceLengthInBook(sentenceFragmentsByPage),
            this.maxWordsPerSentenceOnThisPage(),
            "actualMaxWordsPerSentence"
        );
        this.updateActualAverageCount(
            ReaderToolsModel.averageWordsInSentence(sentenceFragmentsByPage),
            this.maxAverageWordsPerSentence(),
            "actualAverageWordsPerSentence"
        );
        this.updateActualAverageCount(
            ReaderToolsModel.averageWordsInPage(sentenceFragmentsByPage),
            this.maxAverageWordsPerPage(),
            "actualAverageWordsPerPage"
        );
        this.updateActualAverageCount(
            ReaderToolsModel.averageGlyphsInWord(sentenceFragmentsByPage),
            this.maxAverageGlyphsPerWord(),
            "actualAverageGlyphsPerWord"
        );
        this.updateActualAverageCount(
            ReaderToolsModel.averageSentencesInPage(sentenceFragmentsByPage),
            this.maxAverageSentencesPerPage(),
            "actualAverageSentencesPerPage"
        );
    }

    public copyLeveledReaderStatsToClipboard(): void {
        postData("readers/ui/copyBookStatsToClipboard", this.bookStatistics);
    }

    private getSentences(pageStrings: any[]): TextFragment[][] {
        const result: TextFragment[][] = [];
        for (let i = 0; i < pageStrings.length; i++) {
            const page = pageStrings[i];
            let fragments: TextFragment[] = theOneLibSynphony.stringToSentences(
                page
            );

            // remove inter-sentence space
            fragments = fragments.filter(frag => {
                return frag.isSentence;
            });
            result.push(fragments);
        }
        return result;
    }

    public countWordsInBook(sentenceFragmentsByPage: TextFragment[][]): number {
        let total = 0;
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];

            for (let j = 0; j < fragments.length; j++) {
                total += fragments[j].wordCount();
            }
        }
        return total;
    }

    public uniqueWordsInBook(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        const wordMap = {};
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];

            for (let j = 0; j < fragments.length; j++) {
                const words = fragments[j].words;
                for (let k = 0; k < words.length; k++) {
                    wordMap[words[k]] = 1;
                }
            }
        }
        return Object.keys(wordMap).length;
    }

    public maxWordsPerPageInBook(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        let maxWords = 0;
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];

            let subMax = 0;
            for (let j = 0; j < fragments.length; j++) {
                subMax += fragments[j].wordCount();
            }

            if (subMax > maxWords) maxWords = subMax;
        }

        return maxWords;
    }

    public static averageWordsInSentence(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        let sentenceCount = 0;
        let wordCount = 0;
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];
            for (let j = 0; j < fragments.length; j++) {
                const wordsInSentence = fragments[j].words.length;
                if (wordsInSentence > 0) {
                    // if no words, not really a sentence...maybe an empty paragraph?
                    wordCount += wordsInSentence;
                    sentenceCount++;
                }
            }
        }
        if (sentenceCount == 0) {
            return 0;
        }
        return wordCount / sentenceCount;
        //return Math.round(10 * wordCount / sentenceCount) / 10; // for one decimal place (here and elsewhere)
    }

    public static averageWordsInPage(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        let wordCount = 0;
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];
            for (let j = 0; j < fragments.length; j++) {
                wordCount += fragments[j].words.length;
            }
        }
        if (sentenceFragmentsByPage.length == 0) {
            return 0;
        }
        return wordCount / sentenceFragmentsByPage.length;
    }

    public static averageGlyphsInWord(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        let wordCount = 0;
        let glyphCount = 0;
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];
            for (let j = 0; j < fragments.length; j++) {
                wordCount += fragments[j].words.length;
                for (const w of fragments[j].words) {
                    glyphCount += ReaderToolsModel.getWordLength(w);
                }
            }
        }
        if (wordCount == 0) {
            return 0;
        }
        return glyphCount / wordCount;
    }

    public static averageSentencesInPage(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        if (sentenceFragmentsByPage.length == 0) {
            return 0;
        }
        const sentenceCount = ReaderToolsModel.totalSentencesInBook(
            sentenceFragmentsByPage
        );
        return sentenceCount / sentenceFragmentsByPage.length;
    }

    public static maxGlyphsInWord(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        let maxGlyphCount = 0;
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];
            for (let j = 0; j < fragments.length; j++) {
                for (const w of fragments[j].words) {
                    maxGlyphCount = Math.max(
                        maxGlyphCount,
                        ReaderToolsModel.getWordLength(w)
                    );
                }
            }
        }
        return maxGlyphCount;
    }

    public static maxSentenceLengthInBook(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        let maxSentenceLength = 0;
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];
            for (let j = 0; j < fragments.length; j++) {
                maxSentenceLength = Math.max(
                    maxSentenceLength,
                    fragments[j].words.length
                );
            }
        }
        return maxSentenceLength;
    }

    public static totalSentencesInBook(
        sentenceFragmentsByPage: TextFragment[][]
    ): number {
        let sentenceCount = 0;
        if (sentenceFragmentsByPage.length == 0) {
            return 0;
        }
        for (let i = 0; i < sentenceFragmentsByPage.length; i++) {
            const fragments = sentenceFragmentsByPage[i];
            sentenceCount += ReaderToolsModel.countSentences(fragments);
        }
        return sentenceCount;
    }

    public updateActualCount(actual: number, max: number, id: string): void {
        $("#" + id).html(actual.toString());
        const acceptable = actual <= max || max === 0;
        // The two styles here must match ones defined in ReaderTools.htm or its stylesheet.
        // It's important NOT to use two names where one is a substring of the other (e.g., unacceptable
        // instead of tooLarge). That will mess things up going from the longer to the shorter.
        this.setPresenceOfClass(id, acceptable, "acceptable");
        this.setPresenceOfClass(id, !acceptable, "tooLarge");
        this.bookStatistics[id] = actual;
    }

    public updateActualAverageCount(
        average: number,
        max: number,
        id: string
    ): void {
        $("#" + id).html(average.toFixed(1));
        const acceptable = average <= max || max === 0;
        // The two styles here must match ones defined in ReaderTools.htm or its stylesheet.
        // It's important NOT to use two names where one is a substring of the other (e.g., unacceptable
        // instead of tooLarge). That will mess things up going from the longer to the shorter.
        this.setPresenceOfClass(id, acceptable, "acceptable");
        this.setPresenceOfClass(id, !acceptable, "tooLarge");
        this.bookStatistics[id] = average;
    }

    public static countSentences(sentences: TextFragment[]): number {
        return sentences.filter(f => f.words.length > 0).length;
    }

    public static maxWordLength(text: string): number {
        const words = theOneLibSynphony.getWordsFromHtmlString(text);
        let max = 0;
        for (const w of words) {
            max = Math.max(max, ReaderToolsModel.getWordLength(w));
        }
        return max;
    }

    // We'd like to use /\p{Mn}/gu to match diacritics, but FF60 apparently doesn't support it.
    // https://apps.timwhitlock.info/js/regex# provided this which is claimed to be equivalent.
    private static unicodeMn = /[\u0300-\u036f\u0483-\u0487\u0591-\u05bd\u05bf\u05c1-\u05c2\u05c4-\u05c5\u05c7\u0610-\u061a\u064b-\u065e\u0670\u06d6-\u06dc\u06df-\u06e4\u06e7-\u06e8\u06ea-\u06ed\u0711\u0730-\u074a\u07a6-\u07b0\u07eb-\u07f3\u0901-\u0902\u093c\u0941-\u0948\u094d\u0951-\u0954\u0962-\u0963\u0981\u09bc\u09c1-\u09c4\u09cd\u09e2-\u09e3\u0a01-\u0a02\u0a3c\u0a41-\u0a42\u0a47-\u0a48\u0a4b-\u0a4d\u0a51\u0a70-\u0a71\u0a75\u0a81-\u0a82\u0abc\u0ac1-\u0ac5\u0ac7-\u0ac8\u0acd\u0ae2-\u0ae3\u0b01\u0b3c\u0b3f\u0b41-\u0b44\u0b4d\u0b56\u0b62-\u0b63\u0b82\u0bc0\u0bcd\u0c3e-\u0c40\u0c46-\u0c48\u0c4a-\u0c4d\u0c55-\u0c56\u0c62-\u0c63\u0cbc\u0cbf\u0cc6\u0ccc-\u0ccd\u0ce2-\u0ce3\u0d41-\u0d44\u0d4d\u0d62-\u0d63\u0dca\u0dd2-\u0dd4\u0dd6\u0e31\u0e34-\u0e3a\u0e47-\u0e4e\u0eb1\u0eb4-\u0eb9\u0ebb-\u0ebc\u0ec8-\u0ecd\u0f18-\u0f19\u0f35\u0f37\u0f39\u0f71-\u0f7e\u0f80-\u0f84\u0f86-\u0f87\u0f90-\u0f97\u0f99-\u0fbc\u0fc6\u102d-\u1030\u1032-\u1037\u1039-\u103a\u103d-\u103e\u1058-\u1059\u105e-\u1060\u1071-\u1074\u1082\u1085-\u1086\u108d\u135f\u1712-\u1714\u1732-\u1734\u1752-\u1753\u1772-\u1773\u17b7-\u17bd\u17c6\u17c9-\u17d3\u17dd\u180b-\u180d\u18a9\u1920-\u1922\u1927-\u1928\u1932\u1939-\u193b\u1a17-\u1a18\u1b00-\u1b03\u1b34\u1b36-\u1b3a\u1b3c\u1b42\u1b6b-\u1b73\u1b80-\u1b81\u1ba2-\u1ba5\u1ba8-\u1ba9\u1c2c-\u1c33\u1c36-\u1c37\u1dc0-\u1de6\u1dfe-\u1dff\u20d0-\u20dc\u20e1\u20e5-\u20f0\u2de0-\u2dff\u302a-\u302f\u3099-\u309a\ua66f\ua67c-\ua67d\ua802\ua806\ua80b\ua825-\ua826\ua8c4\ua926-\ua92d\ua947-\ua951\uaa29-\uaa2e\uaa31-\uaa32\uaa35-\uaa36\uaa43\uaa4c\ufb1e\ufe00-\ufe0f\ufe20-\ufe26]|\ud800\uddfd|\ud802[\ude01-\ude03\ude05-\ude06\ude0c-\ude0f\ude38-\ude3a\ude3f]|\ud834[\udd67-\udd69\udd7b-\udd82\udd85-\udd8b\uddaa-\uddad\ude42-\ude44]|\udb40[\udd00-\uddef]/g;

    public static getWordLength(word: string): number {
        // I'm not completely sure that all and only class Mn characters
        // should be ignored. Many will have been excluded by the word-breaking
        // algorithm; there shouldn't be punctuation characters in the word.
        const knownGraphemes = theOneLanguageDataInstance.GPCS.map(
            x => x.GPC.replace(this.unicodeMn, "") // drop diacritics
        )
            .filter(x => x.length > 1)
            .sort((x, y) => y.length - x.length);
        let w = word.replace(this.unicodeMn, ""); // drop diacritics here too
        // convert known graphemes to a PUA character that should not be occurring.
        // This causes multi-letter graphemes to count just once,
        // and by doing longer replacements first and using an unlikely PUA
        // character as the replacement there should be no danger that the
        // replacement will match part of another grapheme.
        for (const g of knownGraphemes) {
            w = w.replace(new RegExp(g, "g"), "\uEABC");
        }
        return w.length;
    }

    /** Should be called early on, before other init. */
    public setSynphony(val: ReadersSynphonyWrapper): void {
        this.synphony = val;
    }

    //   getSynphony(): ReadersSynphonyWrapper {
    //     return this.synphony;
    //   }

    /**
     * This group of functions uses jquery (if loaded) to update the real model.
     * Unit testing should spy or otherwise replace these functions, since $ will not be usefully defined.
     */
    public updateElementContent(id: string, val: string): void {
        $("#" + id).html(val);
    }

    public getElementAttribute(id: string, attrName: string): string {
        return $("#" + id).attr(attrName);
    }

    public setElementAttribute(
        id: string,
        attrName: string,
        val: string
    ): void {
        $("#" + id).attr(attrName, val);
    }

    /**
     * Add words from a file to the list of all words. Does not produce duplicates.
     * @param fileContents
     */
    public addWordsFromFile(fileContents: string): void {
        //reviewslog: at the moment, thes first two clauses just do the same things

        // is this a Synphony data file?
        if (
            fileContents.substr(0, 12) === '{"LangName":' ||
            //TODO remove this is bizarre artifact of the original synphony, where the data file was actually some javascript. Still used in a unit test.
            fileContents.substr(0, 12) === "setLangData("
        ) {
            theOneLibSynphony.langDataFromString(fileContents);
            if (this.synphony) {
                this.synphony.loadFromLangData(theOneLanguageDataInstance);
            } else {
                console.warn(
                    "ReaderToolsModel.AddWordsFromFile() - this.synphony is null, fileContents starts: " +
                        fileContents.substr(0, 24)
                );
            }
        }
        // handle sample texts files that are just a set of space-delimited words
        else {
            const words = theOneLibSynphony.getWordsFromHtmlString(
                fileContents
            );
            // Limit the number of words processed from files.  The program hangs on very long lists.
            let lim = words.length;
            const wordNames = Object.keys(this.allWords);
            if (wordNames.length + words.length > this.maxAllowedWords) {
                lim = this.maxAllowedWords - wordNames.length;
            }
            for (let i = 0; i < lim; i++) {
                this.allWords[words[i]] = 1 + (this.allWords[words[i]] || 0);
            }
        }
    }

    public beginSetTextsList(textsArg: string[]): Promise<void> {
        // only save the file types we can read
        this.texts = textsArg.filter(t => {
            const ext = t.split(".").pop();
            if (!ext) return false;
            return this.getReadableFileExtensions().indexOf(ext) > -1;
        });
        return this.beginGetAllSampleFiles().then(() => {
            this.addWordsToSynphony();

            // The word list has been received. Now we are using setTimeout() to delay the remainder of the word
            // list processing so the UI doesn't appear frozen as long.
            setTimeout(() => {
                this.wordListLoaded = true;
                this.updateControlContents(); // needed if user deletes all of the stages.
                this.doMarkup();
                this.updateWordList();
                this.processWordListChangedListeners();

                //note, this endpoint is confusing because it appears that ultimately we only use the word list out of this file (see "sampleTextsList").
                //This ends up being written to a ReaderToolsWords-xyz.json (matching its use, if not it contents).
                postData(
                    "readers/io/synphonyLanguageData",
                    theOneLanguageDataInstance
                );
            }, 200);
        });
    }

    /**
     * Called to process sample data files.
     * When all of them are read and processed, the promise is resolved.
     */
    public beginGetAllSampleFiles(): Promise<void> {
        // The <any> works around a flaw in the declaration of axios.all in axios.d.ts.
        // Using axios directly because api calls for returning the promise.
        return (<any>axios).all(
            this.texts.map(fileName => {
                return axios
                    .get("/bloom/api/readers/io/sampleFileContents", {
                        params: { fileName: fileName }
                    })
                    .then(result => {
                        // this same code runs both for simple word list text files, and for synphony .json files
                        // downstream here, code actually changes the json string back into an object, sigh. But
                        // we're looking for a non-risky patch here as 3.7 is about to go out the door.
                        // We had two bugs related to getting this wrong: BL-3969, BL-3970
                        if (
                            typeof result.data === "string" ||
                            <any>result.data instanceof String
                        ) {
                            this.setSampleFileContents(result.data); // simple wordlist file
                        } else {
                            this.setSampleFileContents(
                                JSON.stringify(result.data)
                            ); //synphony json
                        }
                    });
            })
        );
    }

    /**
     * Called in response to a request for the contents of a sample text file
     * @param fileContents
     */
    public setSampleFileContents(fileContents: string): void {
        this.addWordsFromFile(fileContents);
    }

    /**
     * Notify anyone who wants to know that the word list changed
     */
    public processWordListChangedListeners(): void {
        const handlers = Object.keys(this.wordListChangedListeners);
        for (let j = 0; j < handlers.length; j++)
            this.wordListChangedListeners[handlers[j]]();
    }

    /**
     * Take the list of words collected from the sample files, add it to SynPhony, and update the Stages.
     */
    public addWordsToSynphony() {
        // add words to the word list
        ReadersSynphonyWrapper.addWords(this.allWords);
        theOneLibSynphony.processVocabularyGroups();
    }

    /**
     * Gets words from SynPhony that match the input criteria
     * @param justWordName Return just the word names, not DataWord objects
     * @param desiredGPCs An array of strings
     * @param knownGPCs An array of strings
     * @param restrictToKnownGPCs
     * @param [allowUpperCase]
     * @param [syllableLengths] An array of integers, uses 1-24 if empty
     * @param [selectedGroups] An array of strings, uses all groups if empty
     * @param [partsOfSpeech] An array of strings, uses all parts of speech if empty
     * @returns An array of strings or DataWord objects
     */
    public selectWordsFromSynphony(
        justWordName: boolean,
        desiredGPCs: string[],
        knownGPCs: string[],
        restrictToKnownGPCs: boolean,
        allowUpperCase?: boolean,
        syllableLengths?: number[],
        selectedGroups?: string[],
        partsOfSpeech?: string[]
    ): any[] {
        if (!selectedGroups) {
            selectedGroups = [];
            for (
                let i = 1;
                i <= theOneLanguageDataInstance.VocabularyGroups;
                i++
            )
                selectedGroups.push("group" + i);
        }

        if (!syllableLengths) {
            //using 24 as an arbitrary max number of syllables
            syllableLengths = [];
            for (let j = 1; j < 25; j++) syllableLengths.push(j);
        }

        if (!partsOfSpeech) partsOfSpeech = [];

        if (justWordName)
            return theOneLibSynphony.selectGPCWordNamesWithArrayCompare(
                desiredGPCs,
                knownGPCs,
                restrictToKnownGPCs,
                allowUpperCase,
                syllableLengths,
                selectedGroups,
                partsOfSpeech
            );
        else
            return theOneLibSynphony.selectGPCWordsFromCache(
                desiredGPCs,
                knownGPCs,
                restrictToKnownGPCs,
                allowUpperCase,
                syllableLengths,
                selectedGroups,
                partsOfSpeech
            );
    }

    public selectWordsFromAllowedLists(stageNumber: number): string[] {
        if (!this.synphony) {
            return []; // not loaded yet
        }
        const stages: ReaderStage[] = this.synphony.getStages(stageNumber);

        let words: string[] = [];
        for (let i = 0; i < stages.length; i++) {
            if (stages[i].allowedWords)
                words = words.concat(stages[i].allowedWords);
        }

        // we are limiting the number of words to maxAllowedWords for performance reasons
        if (words.length > this.maxAllowedWords) {
            words = words.slice(0, this.maxAllowedWords);
        }

        return words;
    }

    /**
     * Get the allowed words for the current stage and all previous stages as an array of DataWord objects
     * @param stageNumber
     * @returns An array of DataWord objects
     */
    public getAllowedWordsAsObjects(stageNumber: number): DataWord[] {
        const words: string[] = this.selectWordsFromAllowedLists(stageNumber);
        const returnVal: DataWord[] = [];

        const uiLang = theOneLocalizationManager.getCurrentUILocale();

        for (let i = 0; i < words.length; i++) {
            returnVal.push(new DataWord(words[i]));
        }

        // inform the user if the list was truncated
        const toolbox = $("#toolbox");
        const msgDiv: JQuery = $(toolbox).find("#allowed-word-list-truncated");

        // if the list was truncated, show the message
        if (words.length < this.maxAllowedWords) {
            msgDiv.html("");
        } else {
            msgDiv.html(
                theOneLocalizationManager.simpleFormat(
                    $(toolbox)
                        .find("#allowed_word_list_truncated_text")
                        .html(),
                    [this.maxAllowedWords.toLocaleString(uiLang)]
                )
            );
        }

        return returnVal;
    }

    public saveState(): void {
        // this is needed for unit testing
        const toolbox = $("#toolbox");
        if (typeof toolbox.accordion !== "function") return;

        // this is also needed for unit testing
        const active = toolbox.accordion("option", "active");
        if (isNaN(active)) return;

        postString(
            "editView/saveToolboxSetting",
            "state\tdecodableReader\t" +
                "stage:" +
                this.stageNumber +
                ";sort:" +
                this.sort
        );
        postString(
            "editView/saveToolboxSetting",
            "state\tleveledReader\t" + this.levelNumber
        );
    }

    public restoreState(): void {
        // this is needed for unit testing
        const toolbox = $("#toolbox");
        if (typeof toolbox.accordion !== "function") return;

        const state = new DRTState();

        if (!this.currentMarkupType) this.currentMarkupType = state.markupType;
        // when restoring state we do NOT want to save the results; things are presumably unchanged,
        // and saving the state of a new book from a template can override system defaults we have
        // not yet applied to the book.
        this.setStageNumber(state.stage, true);
        this.setLevelNumber(state.level, true);
    }

    public getAllowedWordsLists(): void {
        if (!this.synphony) return; // not loaded yet

        const stages = this.synphony.getStages();

        // remember how many we are loading so we know when we're finished
        this.allowedWordFilesRemaining = stages.length;

        stages.forEach(
            (stage, index) => {
                // BL-4184: Even if the decodable reader setup doesn't have a filename for each stage,
                // we need to ensure that execution still passes through the "if all loaded" section of
                // the setAllowedWordsListList() method.
                if (stage.allowedWordsFile) {
                    getWithConfig(
                        "readers/io/allowedWordsList",
                        { params: { fileName: stage.allowedWordsFile } },
                        result =>
                            this.setAllowedWordsListList(result.data, index)
                    );
                } else {
                    this.setAllowedWordsListList(undefined, index);
                }
                // During Linux testing of BL-3498, the this (_this in the TS converted to JS), in the axios.get callback was
                // undefined without this bind().  This .bind() is also the fix for BL-3496.
                // But apparently when we switched to TS v2.0, it's no longer needed
            } /*.bind(this)*/
        );
    }

    public setAllowedWordsListList(
        fileContents: string | undefined,
        stageIndex: number
    ): void {
        // remove this one from the count of files remaining
        this.allowedWordFilesRemaining--;

        if (fileContents && this.synphony) {
            this.synphony
                .getStages()
                [stageIndex].setAllowedWordsString(fileContents);
        }

        // if all loaded...
        if (this.allowedWordFilesRemaining < 1) {
            this.wordListLoaded = true;
            this.updateControlContents();
            this.doMarkup();
        }
    }
}

// In case this code is loaded into more than one iframe, we want them to share a single instance.
// So, we will put it in the top window, and let the first instance which executes this block create it.
if (!(<any>top).theOneReaderToolsModel) {
    (<any>top).theOneReaderToolsModel = new ReaderToolsModel();
    (<any>top).theOneReaderToolsModel.initAsync(); // Start the initialization asynchronously, but don't wait for it.
}

export function getTheOneReaderToolsModel() {
    return (<any>top).theOneReaderToolsModel;
}
