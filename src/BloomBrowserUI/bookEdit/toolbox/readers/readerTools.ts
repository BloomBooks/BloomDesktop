/// <reference path="readerToolsModel.ts" />
/// <reference path="directoryWatcher.ts" />
/// <reference path="../../../typings/jqueryui/jqueryui.d.ts" />
import $ from "jquery";
import jQuery from "jquery";
import { DirectoryWatcher } from "./directoryWatcher";
import {
    getFileExtension,
    getTheOneReaderToolsModel,
    isReadableSampleTextFile,
} from "./readerToolsModel";
import {
    theOneLanguageDataInstance,
    theOneLibSynphony,
    ResetLanguageDataGraphemes,
    ResetLanguageDataInstance,
} from "./libSynphony/synphony_lib";
import "./libSynphony/synphony_lib";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { ReaderSettings } from "./ReaderSettings";
import { clearWordCache } from "./libSynphony/bloomSynphonyExtensions";
import "../../../lib/jquery.onSafe";
import axios from "axios";
import { get } from "../../../utils/bloomApi";
import * as _ from "underscore";
import { renderRoot } from "../../../utils/reactRender";
import * as React from "react";
import { ReaderToolSwitch } from "./ReaderToolSwitch";

// listen for messages sent to this page
window.addEventListener("message", processDLRMessage, false);

let readerToolsInitialized: boolean = false;
let lastReaderToolSettingsContent: string | undefined;
const maxReaderSettingsLoadAttempts = 5;
let decodableToggleRenderVersion = 0;
let leveledToggleRenderVersion = 0;
let lastDecodableToggleBookKey: string | undefined;
let lastLeveledToggleBookKey: string | undefined;
function getReaderToggleRenderKey(
    isForLeveled: boolean,
    currentBookKey: string,
): string {
    if (isForLeveled) {
        if (currentBookKey !== lastLeveledToggleBookKey) {
            lastLeveledToggleBookKey = currentBookKey;
            leveledToggleRenderVersion++;
        }

        return `leveled-${leveledToggleRenderVersion}`;
    }

    if (currentBookKey !== lastDecodableToggleBookKey) {
        lastDecodableToggleBookKey = currentBookKey;
        decodableToggleRenderVersion++;
    }

    return `decodable-${decodableToggleRenderVersion}`;
}

// I'm not sure how copilot came to add this normalization. It claims that it is
// useful defensiveness against some uncertainty about whether Axios will return
// a string or an object.
function normalizeReaderSettings(rawSettings: unknown): ReaderSettings {
    if (typeof rawSettings === "string") {
        return JSON.parse(rawSettings) as ReaderSettings;
    }
    return rawSettings as ReaderSettings;
}

function tryNormalizeReaderSettings(
    rawSettings: unknown,
): ReaderSettings | undefined {
    try {
        const settings = normalizeReaderSettings(rawSettings);
        if (!settings) {
            return undefined;
        }
        return settings;
    } catch {
        return undefined;
    }
}

function loadReaderSettingsWithRetry(
    attemptsRemaining: number,
    onLoaded: (settings: ReaderSettings) => void,
    onFailed: () => void,
): void {
    get("readers/io/readerToolSettings", (settingsFileContent) => {
        const normalizedSettings = tryNormalizeReaderSettings(
            settingsFileContent.data,
        );
        if (normalizedSettings) {
            onLoaded(normalizedSettings);
            return;
        }

        if (attemptsRemaining > 1) {
            window.setTimeout(() => {
                loadReaderSettingsWithRetry(
                    attemptsRemaining - 1,
                    onLoaded,
                    onFailed,
                );
            }, 150);
            return;
        }

        onFailed();
    });
}

function getSetupDialogWindow(): Window | null {
    return (<HTMLIFrameElement>(
        parent.window.document.getElementById("settings_frame")
    )).contentWindow;
}

/**
 * Respond to messages
 * @param {Event} event
 */
function processDLRMessage(event: MessageEvent): void {
    const params = event.data.split("\n");
    const setupDialogWindow = getSetupDialogWindow();

    switch (params[0]) {
        case "Texts": // request from setup dialog for the list of sample texts
            if (getTheOneReaderToolsModel().texts) {
                if (setupDialogWindow) {
                    setupDialogWindow.postMessage(
                        "Files\n" +
                            getTheOneReaderToolsModel().texts.join("\r"),
                        "*",
                    );
                }
            }
            return;

        case "Words": {
            // request from setup dialog for a list of words for a stage
            let words: any;
            if (getTheOneReaderToolsModel().synphony.source.useAllowedWords) {
                //reviewslog
                // params[1] is the stage number
                words = getTheOneReaderToolsModel().selectWordsFromAllowedLists(
                    parseInt(params[1]),
                );
            } else {
                // params[1] is a list of known graphemes
                words = getTheOneReaderToolsModel().selectWordsFromSynphony(
                    false,
                    params[1].split(" "),
                    params[1].split(" "),
                    true,
                    true,
                );
            }

            if (setupDialogWindow) {
                // Post a message to the handler in readerSetup.ui.ts.
                // Best for this to post a message to a handler with a different name than this (Words) though.
                // Although at one point, only one set of handlers is attached to the window,
                // at a later point both handlers are attached due to jquery.text-markup.ts importing a file which imports this file (readerTools)
                // That causes an infinite recursion where this handler calls itself (with the wrong parameters) over and over.
                // It's more future proof to resolve this by making sure the handlers have unique names.
                setupDialogWindow.postMessage(
                    "UpdateWordsDisplay\n" + JSON.stringify(words),
                    "*",
                );
            }
            return;
        }

        case "SetupType":
            if (setupDialogWindow) {
                setupDialogWindow.postMessage(
                    "ConfigureActiveTab\n" +
                        getTheOneReaderToolsModel().setupType,
                    "*",
                );
            }
            return;

        case "SetMarkupType":
            getTheOneReaderToolsModel().setMarkupType(parseInt(params[1]));
            return;

        default:
    }
}

export function beginInitializeDecodableReaderTool(): JQueryPromise<void> {
    // load synphony settings and then finish init
    return beginLoadSynphonySettings().then(() => {
        getTheOneReaderToolsModel().updateControlContents();
        $("#toolbox").accordion("refresh");
    });
}

export function beginInitializeLeveledReaderTool(): JQueryPromise<void> {
    // load synphony settings
    return beginLoadSynphonySettings().then(() => {
        getTheOneReaderToolsModel().updateControlContents();
        $("#toolbox").accordion("refresh");
    });
}

export function beginLoadSynphonySettings(): JQueryPromise<void> {
    // make sure synphony is initialized
    const result = $.Deferred<void>();
    get("collection/defaultFont", (result) => setDefaultFont(result.data));
    if (readerToolsInitialized) {
        // If we already initialized the reader tools, we still need to read the current data,
        // since now that we're using a single browser window for the whole workspace,
        // we could change books without reloading the window, and there is some dependence
        // of the data on the current book. So we read it one more time, and do some cleanup
        // if it is different from what we had before.
        loadReaderSettingsWithRetry(
            maxReaderSettingsLoadAttempts,
            (normalizedSettings) => {
                const newSettingsContent = JSON.stringify(normalizedSettings);
                const shouldRefresh =
                    newSettingsContent !== lastReaderToolSettingsContent;
                if (!shouldRefresh) {
                    result.resolve();
                    return;
                }
                beginRefreshEverything(normalizedSettings).then(() => {
                    lastReaderToolSettingsContent = newSettingsContent;
                    result.resolve();
                });
            },
            () => {
                readerToolsInitialized = false;
                result.resolve();
            },
        );
        return result;
    }
    readerToolsInitialized = true;

    loadReaderSettingsWithRetry(
        maxReaderSettingsLoadAttempts,
        (normalizedSettings) => {
            lastReaderToolSettingsContent = JSON.stringify(normalizedSettings);
            initializeSynphony(normalizedSettings);
            //console.log("done synphony init");
            result.resolve();
        },
        () => {
            readerToolsInitialized = false;
            result.resolve();
        },
    );
    return result;
}

/**
 * The function that is called to hook everything up.
 * Note: settingsFileContent may be empty.
 *
 * @param settingsFileContent The content of the standard JSON) file that stores the Synphony settings for the collection.
 * @global {getTheOneReaderToolsModel()) ReaderToolsModel
 */
function initializeSynphony(
    settingsFileContent: ReaderSettings | string,
): void {
    const synphony = new ReadersSynphonyWrapper();
    synphony.loadSettings(settingsFileContent);
    getTheOneReaderToolsModel().setSynphony(synphony);
    getTheOneReaderToolsModel().restoreState();

    getTheOneReaderToolsModel().updateControlContents();

    // set up a DirectoryWatcher on the Sample Texts directory
    getTheOneReaderToolsModel().directoryWatcher = new DirectoryWatcher(
        "Sample Texts",
        10,
    );
    getTheOneReaderToolsModel().directoryWatcher.onChanged(
        "SampleFilesChanged.ReaderTools",
        readerSampleFilesChanged,
    );
    getTheOneReaderToolsModel().directoryWatcher.start();

    if (synphony.source.useAllowedWords) {
        // get the allowed words for each stage
        getTheOneReaderToolsModel().getAllowedWordsLists();
    } else {
        // get the list of sample texts
        get("readers/ui/sampleTextsList", (result) =>
            beginSetTextsList(result.data),
        );
    }
}

/**
 * Called in response to a request for the files in the sample texts directory
 * @param textsList List of file names delimited by \r
 */
function beginSetTextsList(textsList: string): Promise<void> {
    return getTheOneReaderToolsModel().beginSetTextsList(
        textsList.split(/\r/).filter((e) => {
            return e ? true : false;
        }),
    );
}

function setDefaultFont(fontName: string): void {
    getTheOneReaderToolsModel().fontName = fontName;
}

/**
 * This method is called whenever a change is detected in the Sample Files directory
 */
export function readerSampleFilesChanged(): void {
    // We have to basically start over; no other way to get things in a consistent state
    // between the changed sample files and the sample words in the dialog itself.
    // We can however keep the current version of the settings saved in the model.
    beginRefreshEverything(getTheOneReaderToolsModel().synphony.source);
}

function refreshSettingsExceptSampleWords(newSettings) {
    const synphony = getTheOneReaderToolsModel().synphony;
    synphony.loadSettings(newSettings);
    if (synphony.source.useAllowedWords) {
        getTheOneReaderToolsModel().getAllowedWordsLists();
    } else {
        getTheOneReaderToolsModel().updateControlContents();
        getTheOneReaderToolsModel().doMarkup();
    }
}

/**
 * Refreshes the reader setup and, for sample-word mode, rebuilds its language data from the sample files.
 * Returns a promise which is resolved when all the sample words files are loaded and the model is ready to use.
 */
function beginRefreshEverything(settings: ReaderSettings): JQueryPromise<void> {
    if (settings.useAllowedWords) {
        // Allowed-word-list mode does not use sample-word data, and we deliberately keep what
        // is already loaded so the setup dialog can still preview matching words if the user
        // switches back to stages mode. The graphemes must still be rebuilt though: loadSettings
        // only adds them, so without this a letter combination the user just deleted would keep
        // being counted as one letter (getWordLength) for the rest of the session.
        ResetLanguageDataGraphemes();
    } else {
        ResetLanguageDataInstance();
        getTheOneReaderToolsModel().allWords = {};
    }
    // This helps with updating the matching words panel in the setup dialog. If we switched to the
    // sample words tab, changed sample words, and switched back, or if the user just edited the sample
    // words files in the background, nothing will have changed that indicates the cache is invalid;
    // but in fact the words that should show for the current stage and state of things may need
    // updating.
    clearWordCache();

    const synphony = new ReadersSynphonyWrapper();
    synphony.loadSettings(settings);
    getTheOneReaderToolsModel().setSynphony(synphony);

    if (synphony.source.useAllowedWords) {
        // reload the allowed words for each stage
        getTheOneReaderToolsModel().getAllowedWordsLists();
    } else {
        // reload the sample texts
        // Using axios directly because our api at this point calls for returning the promise.
        return <any>(
            axios
                .get("/bloom/api/readers/io/sampleTextsList")
                .then((result) => beginSetTextsList(result.data))
        );
    }
    // Nothing to do, so return an already-resolved promise.
    return getAlreadyResolvedPromise();
}

function getAlreadyResolvedPromise(): JQueryDeferred<void> {
    const result = $.Deferred<void>();
    return result.resolve();
}

export function beginSaveChangedSettings(
    settings: ReaderSettings,
    previousMoreWords: string,
    previousLetters: string,
    previousUseAllowedWords?: number,
): Promise<void> {
    // Using axios directly because our api at this point calls for returning the promise.

    const refreshAllBasedOnAllowedWords: number | boolean =
        previousUseAllowedWords !== undefined
            ? settings.useAllowedWords !== previousUseAllowedWords
            : settings.useAllowedWords;

    return <any>(
        axios
            .post("/bloom/api/readers/io/readerToolSettings", settings)
            .then(() => {
                if (
                    settings.moreWords !== previousMoreWords ||
                    settings.letters !== previousLetters ||
                    refreshAllBasedOnAllowedWords
                ) {
                    return beginRefreshEverything(settings); // caller will resolve when everything is refreshed
                } else {
                    refreshSettingsExceptSampleWords(settings);
                    // Nothing to do, so return an already-resolved promise.
                    return getAlreadyResolvedPromise();
                }
            })
    );
}

/**
 * Adds a function to the list of functions to call when the word list changes
 */
export function addWordListChangedListener(
    listenerNameAndContext: string,
    callback: () => void,
) {
    getTheOneReaderToolsModel().wordListChangedListeners[
        listenerNameAndContext
    ] = callback;
}

/** Removes a listener previously added for word-list changes. */
export function removeWordListChangedListener(
    listenerNameAndContext: string,
): void {
    delete getTheOneReaderToolsModel().wordListChangedListeners[
        listenerNameAndContext
    ];
}

/**
 * Gets the symbols this language allows inside a word regardless of the reader's stage — a
 * syllable break, a stress mark and so on. They live only on the toolbox frame's copy of the
 * Synphony data (and only when the collection's imported language data defines them), so the
 * setup dialog, which runs in the workspace frame, has to ask for them across the bundle
 * boundary the same way it asks for matching words.
 */
export function getSynphonyAlwaysMatchSymbols(): string[] {
    // Match how selectWordsFromSynphony assembles its own copy of this list: it *concats*
    // AlwaysMatch, so that field may hold either one symbol or an array of them, and pushes
    // the other three, which are single symbols. Flattening with concat here covers both
    // shapes — a plain `typeof === "string"` test would silently drop an array-valued
    // AlwaysMatch, which is the sort of quiet omission this function exists to avoid.
    const symbols: unknown[] = ([] as unknown[]).concat(
        theOneLanguageDataInstance["AlwaysMatch"] ?? [],
        theOneLanguageDataInstance["SyllableBreak"] ?? [],
        theOneLanguageDataInstance["StressSymbol"] ?? [],
        theOneLanguageDataInstance["MorphemeBreak"] ?? [],
    );
    return symbols.filter(
        (symbol): symbol is string =>
            typeof symbol === "string" && symbol !== "",
    );
}

/**
 * Classifies the Sample Texts folder listing for the setup dialog, which runs in another frame
 * and so cannot reach the model directly. Answering from here — rather than letting the dialog
 * keep its own copy of the readable-extension list — is what keeps what the dialog shows in step
 * with what Bloom actually loads, including the case-insensitive comparison.
 */
export function classifySampleTextFiles(
    paths: string[],
): { path: string; readable: boolean; hasExtension: boolean }[] {
    return paths.map((path) => ({
        path,
        readable: isReadableSampleTextFile(path),
        hasExtension: getFileExtension(path) !== undefined,
    }));
}

/**
 * Gets the loaded sample words decodable with the given graphemes.
 *
 * The `true` first argument asks for word names rather than DataWord objects, which is all the
 * setup dialog wants. It is worth noting that this is a *different* entry point from the one the
 * toolbox's own getStageWords uses (which passes `false`), because that looks like a discrepancy
 * on a quick read and was raised as one during review. It is not: both end up in
 * libSynphony's selectGPCWordsWithArrayCompare with the same arguments, and the names variant
 * simply plucks Name off the results. The only real difference is that the `false` path memoizes
 * through theOneWordCache while this one does not, so this always reflects the current data.
 */
export function getDecodableStageMatchingWords(knownGpcs: string[]): string[] {
    return getTheOneReaderToolsModel().selectWordsFromSynphony(
        true,
        knownGpcs,
        knownGpcs,
        true,
        true,
    ) as string[];
}

/** Adds a listener that runs when the Sample Texts folder changes. */
export function addSampleTextFilesChangedListener(
    listenerNameAndContext: string,
    callback: () => void,
): void {
    getTheOneReaderToolsModel().directoryWatcher!.onChanged(
        listenerNameAndContext,
        callback,
    );
}

/** Removes a listener previously added for Sample Texts folder changes. */
export function removeSampleTextFilesChangedListener(
    listenerNameAndContext: string,
): void {
    getTheOneReaderToolsModel().directoryWatcher!.offChanged(
        listenerNameAndContext,
    );
}

export function makeLetterWordList(): void {
    // get a copy of the current settings
    const settings: ReaderSettings = <ReaderSettings>(
        jQuery.extend(true, {}, getTheOneReaderToolsModel().synphony.source)
    );

    // remove levels
    if (settings.levels.length !== 0) settings.levels = [];

    // get the words for each stage
    let knownGPCS: string[] = [];
    for (let i = 0; i < settings.stages.length; i++) {
        const stageGPCS: string[] = settings.stages[i].letters.split(" ");
        knownGPCS = _.union(knownGPCS, stageGPCS);
        const stageWords: string[] =
            getTheOneReaderToolsModel().selectWordsFromSynphony(
                true,
                stageGPCS,
                knownGPCS,
                true,
                true,
            );
        settings.stages[i].words = <string[]>_.toArray(stageWords);
    }

    // get list of all words
    let allGroups: string[] = [];
    for (let j = 1; j <= theOneLanguageDataInstance.VocabularyGroups; j++)
        allGroups.push("group" + j);
    allGroups = theOneLibSynphony.chooseVocabGroups(allGroups);

    let allWords: string[] = [];
    for (let g = 0; g < allGroups.length; g++) {
        allWords = allWords.concat(allGroups[g]);
    }
    allWords = _.compact(_.pluck(allWords, "Name"));

    // export the word list
    const ajaxSettings = {
        type: "POST",
        url: "/bloom/api/readers/ui/makeLetterAndWordList",
    };
    ajaxSettings["data"] = {
        settings: JSON.stringify(settings),
        allWords: allWords.join("\t"),
    };

    $.ajax(<JQueryAjaxSettings>ajaxSettings);
}
