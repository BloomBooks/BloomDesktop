/// <reference path="readerToolsModel.ts" />
/// <reference path="directoryWatcher.ts" />
/// <reference path="../../../typings/jqueryui/jqueryui.d.ts" />
import $ from "jquery";
import jQuery from "jquery";
import { DirectoryWatcher } from "./directoryWatcher";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import {
    theOneLanguageDataInstance,
    theOneLibSynphony,
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
    const retryOrFail = () => {
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
    };

    get(
        "readers/io/readerToolSettings",
        (settingsFileContent) => {
            const normalizedSettings = tryNormalizeReaderSettings(
                settingsFileContent.data,
            );
            // Act on the reply outside the request's promise chain. bloomApi.get() hangs
            // our error callback on a .catch() *after* this handler, so anything thrown in
            // here -- a genuine bug in onLoaded, not a failed request -- would otherwise be
            // caught, retried, and finally reported as a load failure, hiding it. Out here
            // it fails fast and gets reported, which is what this repo wants. (BL-16732)
            window.setTimeout(() => {
                if (normalizedSettings) {
                    onLoaded(normalizedSettings);
                    return;
                }

                retryOrFail();
            }, 0);
        },
        // A request that outright fails has to count as a failed attempt too. Without this
        // error callback, bloomApi.get() reports the error and never calls anyone back, so
        // neither onLoaded nor onFailed ever runs -- and every caller waiting on us waits
        // forever. Callers act on that by doing nothing at all, which is invisible.
        // (BL-16732)
        retryOrFail,
    );
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

/**
 * Loads the Synphony (reader) settings and updates the Decodable Reader tool's controls
 * to match. Returns a promise that resolves when that is done.
 */
export function beginInitializeDecodableReaderTool(): JQueryPromise<void> {
    // load synphony settings and then finish init
    return beginLoadSynphonySettings().then(() => {
        getTheOneReaderToolsModel().updateControlContents();
    });
}

/**
 * Loads the Synphony (reader) settings and updates the Leveled Reader tool's controls
 * to match. Returns a promise that resolves when that is done.
 */
export function beginInitializeLeveledReaderTool(): JQueryPromise<void> {
    // load synphony settings
    return beginLoadSynphonySettings().then(() => {
        getTheOneReaderToolsModel().updateControlContents();
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
                // readerToolsInitialized and lastReaderToolSettingsContent are module state
                // in whichever frame loaded this module, but the ReaderToolsModel they
                // describe lives in a holder on the top window which
                // getTheOneReaderToolsModel() deliberately *replaces* when the frame that
                // created it is reloaded. So our memory of having loaded the settings can
                // outlive the model that holds them, and then "the settings are unchanged"
                // is no reason to skip the work: the model in front of us has never seen
                // them. Refreshing is what gets them into it. Without this, synphony stays
                // undefined and every reader-tool feature needing it is silently dead
                // (e.g. Set Up Levels/Stages) until Bloom is restarted. (BL-16732)
                const shouldRefresh =
                    newSettingsContent !== lastReaderToolSettingsContent ||
                    !getTheOneReaderToolsModel().synphony;
                if (!shouldRefresh) {
                    result.resolve();
                    return;
                }
                beginRefreshEverything(normalizedSettings).then(
                    () => {
                        lastReaderToolSettingsContent = newSettingsContent;
                        result.resolve();
                    },
                    // Refreshing fetches the sample-texts list, and if that request fails we
                    // have no settings worth remembering -- but our callers still have to be
                    // released. A caller left waiting forever is precisely what the user
                    // experiences as a button that does nothing. (BL-16732)
                    () => result.resolve(),
                );
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
 * Re-creates the one instance of LanguageData and ReadersSynphonyWrapper, populates them from the supplied or current
 * settings and sample word files, and updates the UI to match. Because of the convoluted way we build
 * the indexes inside the LanguageData object, this is the only currently feasible way to get it in
 * a consistent state after changes to the sample words files or the panel in the settings dialog.
 * Returns a promise which is resolved when all the sample words files are loaded and the model is ready to use.
 */
function beginRefreshEverything(settings: ReaderSettings): JQueryPromise<void> {
    // reset the file and word list
    ResetLanguageDataInstance();
    getTheOneReaderToolsModel().allWords = {};
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
): Promise<void> {
    // Using axios directly because our api at this point calls for returning the promise.
    return <any>(
        axios
            .post("/bloom/api/readers/io/readerToolSettings", settings)
            .then(() => {
                // reviewslog: following previous logic that we need to reload files if useAllowedWords
                // is true. Seems we should at least need to do it ALSO if it was PREVIOUSLY true.
                // But that is a very obscure case...we don't expect users to switch back and forth
                // in the basic mechanism by which they define stages.
                if (
                    settings.moreWords !== previousMoreWords ||
                    settings.letters !== previousLetters ||
                    settings.useAllowedWords
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
