// This class supports creating audio recordings for talking books.
// It is also used by the motion tool when previewing.
// Things currently get started when the user selects the "Talking Book Tool" item in
// the toolbox while editing. This invokes the function audioRecorder.setupForRecording()
// in this file. That code breaks the
// page's text into sentence-length spans (if not already done), makes sure each
// has an id (preserving existing ones, and using guids for new ones). The audio files
// are placed in a folder called 'audio' in the main book folder. Currently we
// save both uncompressed .wav files and compressed .mp3 files for each segment.
// Currently the actual recording is done in C#, since I can't get audio
// recording to work reliably in HTML using Gecko29. In JohnT's fork of Bloom,
// there is a branch RecordAudioInBrowserSpike in which I attempted to do this.
// It works sometimes, but often part or all of the recording is silence.
// Possible improvements:
// - Notice when a new input device is connected and automatically select it
//   (cf Palaso.Media.NAudio.RecordingDeviceIndicator)
// - Update the input device display when the current device is unplugged and a new choice made.
// - Space key as alternative to record button
// - Keyboard shortcut for Play and Next?
// - Automatically move to next page when current one is done
// - Automatically put initial selection on first unrecorded sentence
//   (or maybe on the sentence they right-clicked?)
// - Some more obvious affordance for launching the Record feature
// - Extract content of bubble HTML into its own file?
///<reference path="../../../typings/jquery/jquery.d.ts"/>
///<reference path="../../../typings/toastr/toastr.d.ts"/>

import * as $ from "jquery";
import { theOneLibSynphony } from "../readers/libSynphony/synphony_lib";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import { TextFragment } from "../readers/libSynphony/bloomSynphonyExtensions";
import axios, { AxiosResponse } from "axios";
import {
    get,
    getAsync,
    getWithPromise,
    post,
    postData,
    postJson,
    postJsonAsync,
    postString
} from "../../../utils/bloomApi";
import * as toastr from "toastr";
import WebSocketManager from "../../../utils/WebSocketManager";
import { getActiveToolId, ToolBox } from "../toolbox";
import * as React from "react";
import * as ReactDOM from "react-dom";
import {
    IConfirmDialogProps,
    DialogResult
} from "../../../react_components/confirmDialog";
import {
    getEditTabBundleExports,
    getToolboxBundleExports
} from "../../js/bloomFrames";
import PlaybackOrderControls from "../../../react_components/playbackOrderControls";
import Recordable from "./recordable";
import { getMd5 } from "./md5Util";
import { setupImageDescriptions } from "../imageDescription/imageDescription";
import { TalkingBookAdvancedSection } from "./talkingBookAdvancedSection";
import { EditableDivUtils } from "../../js/editableDivUtils";
import {
    hideImageDescriptions,
    showImageDescriptions
} from "../imageDescription/imageDescriptionUtils";
import { IAudioRecorder } from "./IAudioRecorder";
import {
    getCanvasElementManager,
    kCanvasElementClass
} from "../overlay/canvasElementUtils";
import { RecordingMode } from "./recordingMode";

enum Status {
    Disabled, // Can't use button now (e.g., Play when there is no recording)
    DisabledUnlessHover, // Same as disabled, except it will become enabled if the user hovers over it.
    Enabled, // Can use now, not the most likely thing to do next
    Expected, // The most likely/appropriate button to use next (e.g., Play right after recording)
    Active // Button now active (Play while playing; Record while held down)
}

// ENHANCE: Replace AudioRecordingMode with this?
export enum AudioMode {
    PureSentence, // Record by Sentence, Play by Sentence
    PreTextBox, // Record by TextBox, Play by Sentence. An intermediate stage when transitioning from PureSentence -> PureTextBox
    PureTextBox, // Record by TextBox, Play by TextBox
    HardSplitTextBox, // Version 4.5 only. Record by TextBox, then split into sentences. (Each sentence has own audio file)
    SoftSplitTextBox // Version 4.6+. Record by TextBox, then split into sentences. (The entire text box only has 1 audio file. The timings for where each sentence starts is annotated).
}

export function getAllAudioModes(): AudioMode[] {
    return [
        AudioMode.PureSentence,
        AudioMode.PreTextBox,
        AudioMode.PureTextBox,
        AudioMode.HardSplitTextBox,
        AudioMode.SoftSplitTextBox
    ];
}

const kWebsocketContext = "audio-recording";
const kSegmentClass = "bloom-highlightSegment";
// Indicates that the element should be highlighted.
const kEnableHighlightClass = "ui-enableHighlight";
// Indicates that the element should NOT be highlighted.
// For example, some elements have highlighting prevented at this level
// because its content has been broken into child elements, only some of which show the highlight
const kDisableHighlightClass = "ui-disableHighlight";
// Indicates that highlighting is briefly/temporarily suppressed,
// but may become highlighted later.
// For example, audio highlighting is suppressed until the related audio starts playing (to avoid flashes)
const kSuppressHighlightClass = "ui-suppressHighlight";
const kAudioSentence = "audio-sentence"; // Even though these can now encompass more than strict sentences, we continue to use this class name for backwards compatability reasons
const kAudioSentenceClassSelector = "." + kAudioSentence;
export const kAudioCurrent = "ui-audioCurrent";
const kAudioCurrentClassSelector = "." + kAudioCurrent;
const kBloomEditableTextBoxClass = "bloom-editable";
const kBloomEditableTextBoxSelector = "div.bloom-editable";
const kBloomTranslationGroupClass = "bloom-translationGroup";
const kBloomVisibleClass = "bloom-visibility-code-on";

const kAudioSplitId = "audio-split";

const kTalkingBookToolId = "talkingBook";

export const kPlaybackOrderContainerClass: string =
    "bloom-playbackOrderControlsContainer";

const kEndTimeAttributeName: string = "data-audioRecordingEndTimes";

interface IPlaybackOrderInfo {
    containerDiv: HTMLDivElement;
    sourceTranslationGroup: HTMLDivElement;
    myPosition: number;
}

interface ISetHighlightParams {
    newElement: Element;
    shouldScrollToElement: boolean;
    suppressHighlightIfNoAudio?: boolean;
    oldElement?: Element | null | undefined; // Optional. Provides some minor optimization if set.
    forceRedisplay?: boolean; // optional. If true, reset higlight even if selected element unchanged.
}

// use this function to get the one and only audio recorder from the right iframe
export function getAudioRecorder(): IAudioRecorder {
    const toolboxBundleExports = getToolboxBundleExports();
    const result = toolboxBundleExports
        ? toolboxBundleExports.getTheOneAudioRecorderForExportOnly()
        : // there might be a startup situation where it still gets the wrong one,
          // but we are using getAudioRecorder to replace instances of theOneAudioRecorder in code
          // and decided this was safer than returning undefined
          theOneAudioRecorder;
    return result;
}

// Terminology //
// AudioSegment: the smallest unit of text to playback at a time.
// CurrentTextBox: The text box (div) which is either currently highlighted itself or contains the currently highlighted element. CurrentTextBox never points to a audio-sentence span.
// CurrentDiv: synonymous with CurrentTextBox
// CurrentHighlight: The element which is currently highlighted. It could be either a div or a sentence (or etc., in the future)
// CurrentElement: synonymous with CurrentHighlight
// Actively focused: Means that it has the UI focus e.g. the mouse has selected it
// Active: See "actively focused."
// Focused: See "actively focused."
// Selected: Means that the element is selected, e.g. it will be highlighted during the next stable state. However, it might not be currently highlighted YET.

// TODO: Maybe a lot of this code should move to TalkingBook.ts (regarding the tool) instead of AudioRecording.ts (regarding recording/playing the audio files)
export default class AudioRecording implements IAudioRecorder {
    private recording: boolean;
    private levelCanvas: HTMLCanvasElement;
    private currentAudioId: string;
    // When we are playing audio, this holds the segments we haven't yet finished playing, including the one currently playing.
    // Thus, when it's empty we are not playing audio at all
    // The elements are in reverse order (so the one playing currently or to play next is at the end and can be efficiently popped)
    private elementsToPlayConsecutivelyStack: Element[] = [];
    // When playing a whole-text-box recording, this is a list of the sentence elements to highlight and the time when each starts,
    // again in reverse order (current/next to play is last).
    private subElementsWithTimings: [Element, number][] = [];
    private currentAudioSessionNum: number = 0;
    private awaitingNewRecording: boolean;

    private audioSplitButton: HTMLButtonElement;

    private showingImageDescriptions: boolean;
    public recordingMode: RecordingMode;
    private previousRecordMode: RecordingMode;
    private haveAudio: boolean;
    private inShowPlaybackOrderMode: boolean = false;

    // Corresponds to the collection default recording mode which we would theoretically get via async call to C# API
    // This is rather annoying because then we have async calls being introduced all over the place.
    // This seems rather unnecessary considering that:
    //   1) It's not even that important to solve the consistency problem and get the accurate value
    //   2) this is probably the only writer, which would greatly simplify the consistency problem of maintaining an accurate 2nd cached copy which can be accessed synchronously
    // Therefore, decided to use a cached copy instead.
    private cachedCollectionDefaultRecordingMode: RecordingMode;

    private isShowing: boolean;

    // map<string, string[]> from a sentence to the desired IDs for that span (instead of using a new, dynamically generated one)
    // We have a string[] representing the IdList instead of just a string ID because a text box could potentially contain the same sentence multiple times.
    private sentenceToIdListMap: object = {};
    public __testonly__sentenceToIdListMap = this.sentenceToIdListMap; // Exposing it for unit tests. Not meant for public use.

    private playbackOrderCache: IPlaybackOrderInfo[] = [];
    private disablingOverlay: HTMLDivElement;

    constructor() {
        this.audioSplitButton = <HTMLButtonElement>(
            document.getElementById(kAudioSplitId)!
        );

        // Initialize to Unknown (as opposed to setting to the default Sentence) so we can identify
        // when we need to fetch from Collection Settings vs. when it's already set.
        this.recordingMode = RecordingMode.Unknown;

        this.levelCanvas = <HTMLCanvasElement>(
            document.getElementById("audio-meter")!
        );

        this.updateDisplay(); // review is the the best time?
    }

    // Class method called by exported function of the same name.
    // Only called the first time the Toolbox is opened for this book during this Editing session.
    public async initializeTalkingBookToolAsync(): Promise<void> {
        // I've sometimes observed events like click being handled repeatedly for a single click.
        // Adding these .off calls seems to help...it's as if something causes this show event to happen
        // more than once so the event handlers were being added repeatedly, but I haven't caught
        // that actually happening. However, the off() calls seem to prevent it.
        $("#audio-next")
            .off()
            .click(e => this.moveToNextAudioElement());
        $("#audio-prev")
            .off()
            .click(e => this.moveToPrevAudioElementAsync());
        $("#audio-record")
            .off()
            .mousedown(e => this.startRecordCurrentAsync())
            .mouseup(e => this.endRecordCurrentAsync());
        $("#audio-play")
            .off()
            .click(e => {
                if (!e.ctrlKey) {
                    // Normal case
                    this.togglePlayCurrentAsync();
                } else {
                    // Control + Click case: Special debug mode
                    this.playESpeakPreview();
                }
            });

        $("#audio-split")
            .off()
            .click(async e => {
                const mediaPlayer = this.getMediaPlayer();
                mediaPlayer.pause();
                getEditTabBundleExports().showAdjustTimingsDialogFromEditViewFrame(
                    this.split,
                    this.editTimingsFileAsync,
                    this.applyTimingsFileAsync,
                    canceled => {
                        if (!canceled) {
                            this.changeStateAndSetExpectedAsync("next");
                            this.updatePlayerStatus();
                        }
                    }
                );
            });

        $("#audio-listen")
            .off()
            .click(e => this.listenAsync());
        $("#audio-clear")
            .off()
            .click(e => this.clearRecordingAsync());

        $("#player").off();
        const player = this.getMediaPlayer();

        // The following speeds playback, ensures we get the durationchange event.
        player.setAttribute("preload", "auto");

        player.onerror = e => {
            if (this.playingAudio()) {
                // during a "listen", we walk through each segment, but some (or all) may not have audio
                this.playEndedAsync(); //move to the next one
            } else if (this.awaitingNewRecording) {
                // file may not have been created yet. Try again.
                // ENHANCE: Maybe it shouldn't try it an infinte number of times.
                this.updatePlayerStatus();
            }
            // A previous version did a toast here. However, the auto-preload which we set up to help
            // us update durations causes an error to be raised for all nonexistent audio files; it
            // may just be because we haven't recorded it yet. A toast for that is excessive.
            // We could possibly arrange for a toast if we get an error while actually playing,
            // but it seems very unlikely.
        };

        player.onended = () => this.playEndedAsync();
        player.ondurationchange = () => this.durationChanged();

        $("#audio-input-dev")
            .off()
            .click(e => this.selectInputDevice());

        toastr.options.positionClass = "toast-toolbox-bottom";
        toastr.options.timeOut = 10000;
        toastr.options.preventDuplicates = true;

        return this.pullDefaultRecordingModeAsync();
    }

    private getMediaPlayer(): HTMLMediaElement {
        const player = document.getElementById(
            "player"
        ) as HTMLMediaElement | null;

        if (!player) {
            throw new Error(`HTMLMediaElement #player was not found.`);
        }

        return player;
    }

    // Updates our cached version of the default recording mode with the version from the Bloom API Server.
    // Returns a promise, which you can attach callbacks to that will run after this function updates the cached version
    public async pullDefaultRecordingModeAsync(): Promise<void> {
        try {
            const result: void | AxiosResponse<any> = await getWithPromise(
                "talkingBook/defaultAudioRecordingMode"
            );

            if (!result) {
                // It returned void, which means an error
                this.cachedCollectionDefaultRecordingMode =
                    RecordingMode.Sentence;
            }

            const axiosResponse = result as AxiosResponse<any>;
            this.cachedCollectionDefaultRecordingMode = AudioRecording.getAudioRecordingModeWithDefaultFromString(
                axiosResponse.data
            );
        } catch {
            // The Bloom API call might not succeed... especially in the unit tests.
            // If so, just fallback to some reasonable default instead. Instead of propagating an error.
            this.cachedCollectionDefaultRecordingMode = RecordingMode.Sentence;
        }
    }

    public playingAudio(): boolean {
        return (
            this.elementsToPlayConsecutivelyStack &&
            this.elementsToPlayConsecutivelyStack.length > 0
        );
    }

    // Sets up member variables for audio recording mode
    //
    // Precondition: Assumes that all initialization of collection-dependent settings has already been done.
    public initializeAudioRecordingMode() {
        const currentTextBox = this.getCurrentTextBox();

        this.recordingMode = this.getRecordingModeOfTextBox(currentTextBox);
        if (this.recordingMode == RecordingMode.Unknown) {
            this.recordingMode = RecordingMode.Sentence;
        }

        this.updateDisplay();
    }

    // Given a text box, determines its recording mode.
    // If explicitly persisted, that value will be used. But if not, will perform a series of fallback checks.
    private getRecordingModeOfTextBox(
        textBoxDiv: Element | null
    ): RecordingMode {
        if (textBoxDiv) {
            // First, attempt to determine it from the text box's explicitly specified value if possible.
            const audioRecordingModeStr = textBoxDiv.getAttribute(
                "data-audiorecordingmode"
            );

            const recordingMode = AudioRecording.getAudioRecordingModeFromString(
                audioRecordingModeStr
            );
            if (recordingMode != RecordingMode.Unknown) {
                return recordingMode;
            }
        }

        const pageDocBody = this.getPageDocBody();
        if (pageDocBody) {
            const firstWithRecordingMode = pageDocBody.querySelector(
                "[data-audiorecordingmode]"
            );
            if (firstWithRecordingMode) {
                // For a text box that doesn't already have mode specified, first fallback is to make it the same as another text box on the page that does have it
                const audioRecordingModeStr = firstWithRecordingMode.getAttribute(
                    "data-audiorecordingmode"
                );

                const recordingMode = AudioRecording.getAudioRecordingModeFromString(
                    audioRecordingModeStr
                );
                if (recordingMode != RecordingMode.Unknown) {
                    return recordingMode;
                }
            }

            if (pageDocBody.querySelector("span." + kAudioSentence)) {
                // This may happen when loading books from 4.3 or earlier that already have text recorded,
                // and is especially important if the collection default is set to anything other than Sentence.
                return RecordingMode.Sentence;
            }
        }

        // We are not sure what it should be.
        // So, check what the collection default has to say
        // Precondition: Assumes this class is the only writer, and doesn't bother attempting to retrieve from API or pull in the changes for next time
        return this.cachedCollectionDefaultRecordingMode;
    }

    // Typecast from string to AudioRecordingMode.
    public static getAudioRecordingModeFromString(
        audioRecordingModeStr: string | null
    ): RecordingMode {
        if (audioRecordingModeStr && audioRecordingModeStr in RecordingMode) {
            return <RecordingMode>audioRecordingModeStr;
        } else {
            return RecordingMode.Unknown;
        }
    }

    // Typecast from string to AudioRecordingMode, but if it would return Unknown, return the default value instead.
    public static getAudioRecordingModeWithDefaultFromString(
        audioRecordingModeStr: string | null,
        defaultRecordingMode: RecordingMode = RecordingMode.Sentence
    ): RecordingMode {
        let recordingMode = AudioRecording.getAudioRecordingModeFromString(
            audioRecordingModeStr
        );

        if (recordingMode == RecordingMode.Unknown) {
            recordingMode = defaultRecordingMode;
        }

        return recordingMode;
    }

    public setupForListen() {
        $("#player").bind("ended", e => this.playEndedAsync());
        $("#player").bind("error", e => {
            // during a "listen", we walk through each segment, but some (or all) may not have audio
            this.playEndedAsync(); //move to the next one
        });
    }

    // Called by TalkingBookModel.showTool() when a different tool is added/chosen or when the toolbox is re-opened, but not when a new page is added
    // This function should contain only work that needs to be done when the tool is created
    // Initialization that happens for a new page should happen in newPageReady instead.
    public async setupForRecordingAsync(): Promise<void> {
        this.isShowing = true;

        this.updateInputDeviceDisplay();
        this.disablingOverlay = document.getElementById(
            "disablingOverlay"
        ) as HTMLDivElement;

        // Add these listeners even if there's currently no editables.
        // It's possible that your page starts with no editables, then you open the Talking Book Tool (and this method runs),
        // then the user changes the layout to add a text box, and then...
        // you'll want your listeners to have been set up already.
        this.addAudioLevelListener();
        this.addMicErrorListener();
    }

    // Called when the Talking Book Tool is chosen.
    public addAudioLevelListener(): void {
        WebSocketManager.addListener(kWebsocketContext, e => {
            if (e.id == "peakAudioLevel")
                this.setStaticPeakLevel(e.message ? e.message : "");
        });
    }

    public addMicErrorListener(): void {
        WebSocketManager.addListener(kWebsocketContext, e => {
            if (
                e.id === "recordingStartError" ||
                e.id === "monitoringStartError"
            ) {
                toastr.error(e.message ? e.message : "");
            }
            // Don't disable recording for a monitoring error, as right now when switching mics we may
            // kick off monitoring for the wrong mic
            if (e.id === "recordingStartError") {
                this.recording = false;
                this.setStatus("record", Status.Disabled);
            }
        });
    }

    // Called by TalkingBookModel.detachFromPage(), which is called when changing tools, hiding the toolbox,
    // or saving (leaving) pages.
    public removeRecordingSetup() {
        this.removeAudioCurrentFromPageDocBody();
        const page = this.getPageDocBodyJQuery();
        page.find(kAudioCurrentClassSelector)
            .removeClass(kAudioCurrent)
            .removeClass(kSuppressHighlightClass);
        if (this.inShowPlaybackOrderMode) {
            // We are removing the UI because we're changing tools or pages, but we want to leave
            // the checkbox checked for the next time this tool is active, so it will turn on the
            // playback order UI again. The 'true' param tells the method to leave the checkbox checked.
            this.removePlaybackOrderUi(<HTMLElement>page[0], true);
        }

        // In case of the Play -> Pause -> change page.
        this.revertFixHighlighting();
    }

    public stopListeningForLevels() {
        axios.post("/bloom/api/audio/stopMonitoring");
        WebSocketManager.closeSocket(kWebsocketContext);
    }

    private isPlaybackOrderSpecified(): boolean {
        const body = this.getPageDocBody();
        if (!body) {
            return false;
        }
        const visibleGroups = this.getVisibleTranslationGroups(body);
        for (let i = 0; i < visibleGroups.length; i++) {
            const div = visibleGroups[i];
            const tabindexAttr = div.getAttribute("tabindex");
            if (tabindexAttr && parseInt(tabindexAttr) > 0) {
                // We consider that ANY positive tabindex means playback order has been specified at some point.
                return true;
            }
        }
        return false;
    }

    // We now do recording in all editable divs that are visible.
    // This should NOT restrict to ones that already contain audio-sentence spans.
    // BL-5575 But we don't (at this time) want to record comprehension questions.
    // And BL-5457: Check that we actually have recordable text in the divs we return.
    // And now (BL-7883) we want to optionally order the divs by the tabindex attribute
    // on their parent translationGroup div.
    // And also (BL-8515) filter out image description text, if that tool is not active.
    private getRecordableDivs(
        includeCheckForText: boolean = true,
        includeCheckForPlaybackOrder: boolean = true,
        includeCheckForTempHidden: boolean = true
    ): HTMLElement[] {
        // REVIEW: this may in fact be unneeded but I'm just trying to get eslint set up and conceivably it is intentional
        // eslint-disable-next-line @typescript-eslint/no-this-alias
        const $this = this;
        const pageBody = this.getPageDocBody();
        if (!pageBody) {
            return []; // shouldn't happen
        }
        const editableDivs = Array.from(
            // requiring the visible class reduces the filtering we need to do, but also,
            // some elements (currently in Bloom Games) are visible as placeholders
            // when the main element is empty, and we don't want to record those.
            pageBody.getElementsByClassName(
                kBloomEditableTextBoxClass + " " + kBloomVisibleClass
            ),
            elem => <HTMLElement>elem
        );
        const recordableDivs = editableDivs.filter(elt => {
            if (elt.parentElement?.classList.contains("bloom-noAudio")) {
                return false;
            }
            // Copies in game targets are not recordable
            if (elt.closest("[data-target-of]")) {
                return false;
            }
            if (
                !elt.parentElement?.classList.contains(
                    kBloomTranslationGroupClass
                )
            ) {
                // We were getting copies from qtips
                return false;
            }
            if (!$this.isVisible(elt, includeCheckForTempHidden)) {
                return false;
            }
            if (!includeCheckForText) {
                return true;
            }
            return this.stringToSentences(elt.innerHTML).some(frag => {
                return $this.isRecordable(frag);
            });
        });
        if (!includeCheckForPlaybackOrder || !this.isPlaybackOrderSpecified()) {
            return recordableDivs;
        }
        return this.sortByTabindex(recordableDivs);
    }

    // Param 'recordableDivs' is an array of bloom-editable divs. This function serves
    // to sort that by the containing translationGroup's tabindex attribute (if it exists).
    // Divs inside of translationGroups that do NOT have a tabindex will be sorted to the bottom.
    private sortByTabindex(recordableDivs: HTMLElement[]): HTMLElement[] {
        return recordableDivs.sort((a, b) => {
            return this.getContainerTabindex(a) - this.getContainerTabindex(b);
        });
    }

    // Looks for an ancestor that is a translationGroup. If it doesn't find one, or if
    // the translationGroup doesn't have a tabindex attribute, it returns 0, otherwise
    // it returns the tabindex as a number.
    private getContainerTabindex(recordableDiv: HTMLElement): number {
        const translationGroup = recordableDiv.closest(
            "." + kBloomTranslationGroupClass
        );
        if (!translationGroup) {
            return 0; // something went wrong? xMatter bloom-editable?
        }
        const tabindexString = translationGroup.getAttribute("tabindex");
        if (!tabindexString) {
            return 999; // We want divs with no tabindex to sort to the bottom
        }
        return parseInt(tabindexString);
    }

    // Corresponds to getRecordableDivs() but only applies the check to the current element
    public isRecordableDiv(
        element: Element | null,
        includeCheckForTempHidden: boolean = true
    ): boolean {
        if (!element) {
            return false;
        }

        if (
            element.nodeName == "DIV" &&
            element.classList.contains(kBloomEditableTextBoxClass)
        ) {
            if (
                element.parentElement &&
                element.parentElement.classList.contains("bloom-noAudio")
            ) {
                return false;
            }

            if (!this.isVisible(element, includeCheckForTempHidden)) {
                return false;
            }

            return this.stringToSentences(element!.innerHTML).some(frag => {
                return this.isRecordable(frag);
            });
        } else {
            return false;
        }
    }

    private isVisible(
        elem: Element,
        includeCheckForTempHidden: boolean = true
    ) {
        if (EditableDivUtils.isInHiddenLanguageBlock(elem)) {
            return false;
        }
        if (!includeCheckForTempHidden) {
            return true;
        }
        const transgroup = elem.closest(".bloom-translationGroup");
        if (transgroup) {
            if (transgroup.classList.contains("box-header-off")) {
                return false;
            }
        }
        return true;
    }

    private containsAnyAudioElements(): boolean {
        return this.getAudioElements().length > 0;
    }

    private getAudioElements(
        includeCheckForTempHidden: boolean = true
    ): HTMLElement[] {
        // Starting with the recordable divs, get all of these or their descendants that have
        // the 'kAudioSentence' class. We now need to maintain the order that was given to us
        // by getRecordableDivs().
        const recordableDivs = this.getRecordableDivs(
            true,
            true,
            includeCheckForTempHidden
        );
        let result: HTMLElement[] = [];
        recordableDivs.forEach(div => {
            if (div.classList.contains(kAudioSentence)) {
                result.push(div);
            } else {
                const childElems = Array.from(
                    div.getElementsByClassName(kAudioSentence),
                    elem => <HTMLElement>elem
                );
                result = result.concat(childElems);
            }
        });
        return result;
    }

    private doesElementContainAnyAudioElements(element: Element): boolean {
        return (
            element.classList.contains(kAudioSentence) ||
            element.getElementsByClassName(kAudioSentence).length > 0
        );
    }

    // In case play is currently paused, end that state, typically because we are doing another command.
    private resetAudioIfPaused(): void {
        this.getMediaPlayer().currentTime = 0;
        this.elementsToPlayConsecutivelyStack = [];
        this.subElementsWithTimings = [];
        this.currentAudioSessionNum++;
    }

    private async moveToNextAudioElement(): Promise<void> {
        toastr.clear();

        const next = this.getNextAudioElement();
        if (!next) return;

        this.resetAudioIfPaused();

        await this.setSoundAndHighlightAsync({
            newElement: next,
            shouldScrollToElement: true
        });
        return this.changeStateAndSetExpectedAsync("record");
    }

    private async moveToPrevAudioElementAsync(): Promise<void> {
        toastr.clear();
        const prev = this.getPreviousAudioElement();
        if (prev == null) return;

        this.resetAudioIfPaused();

        await this.setSoundAndHighlightAsync({
            newElement: prev,
            shouldScrollToElement: true
        });
        return this.changeStateAndSetExpectedAsync("record"); // Enhance: I think it'd actually be better to dynamically assign Expected based on what audio is available etc., instead of based on state transitions. Especially when doing Prev.
    }

    // Gets the next audio element to be recorded
    public getNextAudioElement(): Element | null {
        return this.incrementAudioElementIndex();
    }

    // Gets the previous audio element to be recorded
    public getPreviousAudioElement(): Element | null {
        const traverseReverse = true;
        return this.incrementAudioElementIndex(traverseReverse);
    }

    // Advances (or rewinds) through the audio elements by 1.  (Doesn't modify anything)
    private incrementAudioElementIndex(
        isTraverseInReverseOn: boolean = false
    ): Element | null {
        const currentTextBox = this.getCurrentTextBox();
        if (!currentTextBox) return null;

        if (this.recordingMode === RecordingMode.TextBox) {
            return this.incrementAudioElementIndexForTextBoxMode(
                isTraverseInReverseOn,
                currentTextBox
            );
        } else {
            return this.incrementAudioElementIndexForSentenceMode(
                isTraverseInReverseOn,
                currentTextBox
            );
        }
    }

    // Returns the next audio element in the specified direction. (Doesn't modify anything)
    private incrementAudioElementIndexForTextBoxMode(
        isTraverseInReverseOn: boolean,
        currentTextBox: Element
    ): Element | null {
        // This means we need to go to the next text box, and then determine the right Recording segment.

        const incrementAmount = isTraverseInReverseOn ? -1 : 1;

        // Careful! Even though the Recording Mode is text box, current can be a Sentence... use currentTextBox instead
        const allTextBoxes = this.getRecordableDivs(false, true);
        const currentIndex = allTextBoxes.indexOf(<HTMLElement>currentTextBox);
        console.assert(currentIndex >= 0);
        if (currentIndex < 0) {
            return null;
        }

        // Loop in the appropriate direction until we can find a text box that actually contains an audio-sentence.
        // (This allows it to skip over empty text boxes, which would otherwise return null and end navigation using the arrow buttons).
        let nextTextBoxIndex: number = currentIndex;
        let nextTextBox: Element;

        do {
            nextTextBoxIndex += incrementAmount;

            if (
                nextTextBoxIndex < 0 ||
                nextTextBoxIndex >= allTextBoxes.length
            ) {
                return null;
            }
            nextTextBox = allTextBoxes[nextTextBoxIndex];
        } while (!this.doesElementContainAnyAudioElements(nextTextBox));

        return this.getNextRecordingSegment(nextTextBox, isTraverseInReverseOn);
    }

    private incrementAudioElementIndexForSentenceMode(
        isTraverseInReverseOn: boolean,
        currentTextBox: Element
    ): Element | null {
        // Enhance: Maybe this would be safer to advance/rewind to the next SPAN instead of next audio-sentence.

        const incrementAmount = isTraverseInReverseOn ? -1 : 1;
        const current = (<HTMLElement>(
            this.getPageDocBody()
        )).getElementsByClassName(kAudioCurrent);
        if (!current || current.length === 0) {
            return null;
        }

        // Find the next segment to be PLAYED.
        const audioElts = this.getAudioElements();
        if (audioElts.length === 0) {
            return null;
        }
        const nextIndex =
            audioElts.indexOf(<HTMLElement>current.item(0)) + incrementAmount;
        if (nextIndex < 0 || nextIndex >= audioElts.length) {
            return null;
        }
        const nextElement = audioElts[nextIndex];

        // Now find the text box of the next segment to be played.
        const nextTextBox = this.getTextBoxOfElement(nextElement);
        if (!nextTextBox) {
            console.assert(false, "nextTextBox not found.");
            return null;
        }

        if (currentTextBox === nextTextBox) {
            // Same Text Box: This case is easy because the next mode is guaranteed to be the same as the current mode.
            return nextElement;
        } else {
            // Different text box. Do some logic to figure out exactly which element should be played based on the mode and direction.
            return this.getRecordingModeOfTextBox(nextTextBox) ==
                RecordingMode.TextBox
                ? nextTextBox
                : nextElement;
        }
    }

    // Get the next recording segment when moving to the specified different text box. Depending on that box's mode, it may be the box itself, or its first or last segment.
    //
    // nextTextBox: The text box expected to contain the next recording segment.
    // isTraverseInReverseOn: false if going forwards, which implies next segment is the first segment within {nextTextBox}). True if going backwards (in reverse), which implies next segment is the last segment within {nextTextBox}
    //
    // Returns the next recording segment if it is inside {nextTextBox}, or null if {nextTextBox} does not contain any recording segments.
    private getNextRecordingSegment(
        nextTextBox: Element,
        isTraverseInReverseOn: boolean
    ): Element | null {
        const nextRecordingMode = this.getRecordingModeOfTextBox(nextTextBox);

        if (nextRecordingMode == RecordingMode.TextBox) {
            return nextTextBox;
        } else {
            // That is, NextRecordingMode = Sentence
            const nextDivAudioSegments = this.getAudioSegmentsWithinElement(
                nextTextBox
            );
            if (nextDivAudioSegments.length <= 0) {
                return null;
            }

            // Set index to the 0th sentence (going forward) or the last sentence (going reverse) accordingly.
            const sentenceIndex = isTraverseInReverseOn
                ? nextDivAudioSegments.length - 1
                : 0;
            const nextDivFirstSentence = nextDivAudioSegments[sentenceIndex];
            return nextDivFirstSentence;
        }
    }

    private removeAudioCurrentFromPageDocBody() {
        const pageDocBody = this.getPageDocBody();
        if (pageDocBody) {
            this.removeAudioCurrent(pageDocBody);
        }
    }

    private removeAudioCurrent(parentElement: Element) {
        // Note that HTMLCollectionOf's length can change if you change the number of elements matching the selector.
        const audioCurrentCollection: HTMLCollectionOf<Element> = parentElement.getElementsByClassName(
            kAudioCurrent
        );

        // Convert to an array whose length won't be changed
        const audioCurrentArray: Element[] = Array.from(audioCurrentCollection);

        for (let i = 0; i < audioCurrentArray.length; i++) {
            audioCurrentArray[i].classList.remove(
                kAudioCurrent,
                kSuppressHighlightClass
            );
        }

        const iconHolders = Array.from(
            parentElement.getElementsByClassName(
                "bloom-ui-current-audio-marker"
            )
        );
        for (let i = 0; i < iconHolders.length; i++) {
            iconHolders[i].remove();
        }
    }

    // I'm not sure why activeToolId falsy should count as "true" but that's how some old code
    // in setSoundAndHighlightAsync was written. It might be because when we're first showing
    // the toolbox, the default talking book tool starts being initialized before we actually
    // set what getActiveToolId() is looking for.
    private isTalkingBookToolActive(): boolean {
        const activeToolId = getActiveToolId();
        return activeToolId === "talkingBook" || !activeToolId;
    }

    public async setSoundAndHighlightAsync(
        setHighlightParams: ISetHighlightParams
    ): Promise<void> {
        // Check that the active tool is not something other than "talkingBook".  A page
        // can specify a tool it wants to be active initially when it first loads.
        // The multiple asynchronous calls during page loading can result in the wrong
        // tool getting the "newPageReady" method called when the new page specifies a
        // different tool to be activated.  This is the simplest fix that I've found.
        // See BL-14434.
        if (!this.isTalkingBookToolActive()) return;

        // Note: setHighlightToAsync() should be run first so that ui-audioCurrent points to the correct element when setSoundFrom() is run.
        await this.setHighlightToAsync(setHighlightParams);
        this.setSoundFrom(setHighlightParams.newElement);
    }

    // Changes the visually highlighted element (i.e, the element corresponding to .ui-audioCurrent) to the specified element.
    private async setHighlightToAsync({
        newElement,
        // If true, causes the element to be scrolled into view in addition to being highlighted.
        // Our general philosophy/recommendation about when this should be true is when it's clear the user is interacting with the Talking Book tool,
        // it's ok to scroll it into view.  If they're going to record it/etc., they'll probably want to read it too.
        // However, when the Talking Book tool opens automatically upon opening a book or upon opening the toolbox, it's not very clear whether
        // it's desirable to scroll the element into view. For now, the philosophy is that it should not be scrolled into view at those times.
        // Also, don't scroll while the user is typing: 1) something else already takes care of that well and
        // 2) in Record by Sentence mode, it's easily possible for the current highlight to be the 1st span while the user is typing into the last span.
        shouldScrollToElement,
        suppressHighlightIfNoAudio,
        oldElement, // Optional. Provides some minor optimization if set.
        forceRedisplay
    }: ISetHighlightParams): Promise<void> {
        if (!oldElement) {
            oldElement = this.getCurrentHighlight();
        }

        const visible = this.isVisible(newElement);

        // This should happen even if oldElement and newElement are the same.
        // e.g. the user could navigate (with arrows or mousewheel) such that oldElement is out of view, then press Play.
        // It would be worthwhile to scroll it back into view in that case.
        if (shouldScrollToElement && visible) {
            this.scrollElementIntoView(newElement);
        }

        if (oldElement === newElement && !forceRedisplay) {
            // No need to do much, and better not to so we can avoid any temporary flashes as the highlight is removed and re-applied
            return;
        }

        // Get rid of all the audio-currents just to be sure.
        this.removeAudioCurrentFromPageDocBody();

        if (!this.inShowPlaybackOrderMode) {
            // It's good for this to happen before awaiting the subsequent async behavior,
            // especially if the caller doesn't await this function.
            // This allows us to generally represent the correct current element immediately.
            newElement.classList.add(kAudioCurrent);
            getCanvasElementManager()?.setActiveElementToClosest(
                newElement as HTMLElement
            );
            if (visible) {
                // This is a workaround for a Chromium bug; see BL-11633. We'd like our style rules
                // to just put the icon on the element that has kAudioCurrent. But that element
                // has a background color, so (due to the bug) it cannot have position:relative,
                // or we lose the cursor. So insert an empty element (which by default will have
                // position: relative) to hold the icon.
                const iconHolder = newElement.ownerDocument.createElement(
                    "span"
                );
                iconHolder.classList.add("bloom-ui-current-audio-marker");
                iconHolder.classList.add("bloom-ui"); // makes sure it never becomes part of saved document.
                // If we're recording by text-box, we want the icon to be at the beginning of the text box,
                // but we also want it inside the text-box div.  Otherwise, the appearance system introduced
                // by Bloom 6.0 will cause a gap to appear between the invisible icon and the text, shifting
                // the text down while it is being recorded.  See BL-13128.
                // (The icon doesn't actually display for whole text box recording or for the first sentence
                // of sentence-by-sentence recording, but that's a separate issue that makes the text shift
                // even more mysterious.)
                if (newElement.tagName === "DIV") {
                    newElement.insertBefore(iconHolder, newElement.firstChild);
                } else {
                    newElement.parentElement?.insertBefore(
                        iconHolder,
                        newElement
                    );
                }
            }
        }

        if (suppressHighlightIfNoAudio && visible) {
            // prevents highlight showing at once
            // FYI: Because of how JS works, no rendering should happen between setting audioCurrent above and setting ui-suppressHighlight here.
            newElement.classList.add(kSuppressHighlightClass);
            try {
                const response: AxiosResponse<any> = await axios.get(
                    "/bloom/api/audio/checkForSegment?id=" + newElement.id
                );

                if (response.data === "exists") {
                    newElement.classList.remove(kSuppressHighlightClass);
                }
            } catch (error) {
                //server couldn't find it, so just leave it unhighlighted
                toastr.error(
                    "Error checking on audio file " + error.statusText
                );
            }
        }
    }

    // Scrolls an element into view.
    // Disclaimer: You probably don't want to call this while the user's typing. It'll be very annoying.
    private scrollElementIntoView(element: Element) {
        // In Bloom Player, scrollIntoView can interfere with page swipes,
        // so Bloom Player needs some smarts about when to call it...
        // But here, there shouldn't be any interference. So no smarts needed.
        if (element == null) {
            return;
        }

        element.scrollIntoView({
            // Animated instead of sudden
            behavior: "smooth",

            // "nearest" setting does lots of smarts for us (compared to us deciding when to use "start" or "end")
            // Seems to reduce unnecessary scrolling compared to start (aka true) or end (aka false).
            // Refer to https://drafts.csswg.org/cssom-view/#scroll-an-element-into-view,
            // which seems to imply that it won't do any scrolling if the two relevant edges are already inside.
            block: "nearest"

            // horizontal alignment is controlled by "inline". We'll leave it as its default ("nearest")
        });
    }

    // Given the specified element, updates the audio player's source and other necessary things in order to make the specified element's audio the next audio to play
    //
    // Precondition: element (the element to change the audio to) should also already be the currently highlighted element (even if we will later shrink the selection to one of its segments)
    public setSoundFrom(element: Element): void {
        // Adjust the audio file that will get played by the Play (Check) button.
        // Note: The next element to be PLAYED may not be the same as the new element with the current RECORDING highlight.
        //       The element to be played might be a strict child of the element to be recorded.
        const firstAudioSentence = this.getFirstAudioSentenceWithinElement(
            element
        );
        let id: string;
        if (firstAudioSentence) {
            id = firstAudioSentence.id;
        } else {
            console.assert(
                false,
                "setSoundFrom(): Element expected to contain an audio sentence"
            );
            id = element.id;
        }
        this.setCurrentAudioId(id);

        // Before updating the controls, we need to update the audioRecordingMode. It might've changed.
        this.initializeAudioRecordingMode(); // Not necessarily safe unless SetHighlightTo() was applied first()!
    }

    // If we have an mp3 file but not a wav file, the file server will return that instead.
    private currentAudioUrl(id: string): string {
        return this.urlPrefix() + id + ".wav";
    }

    private urlPrefix(): string {
        const pageFrame = this.getPageFrame()!; // Note: Just fail fast if it's null.
        const bookSrc = pageFrame.src;
        const index = bookSrc.lastIndexOf("/");
        const bookFolderUrl = bookSrc.substring(0, index + 1);
        return "/bloom/api/audio/wavFile?id=" + bookFolderUrl + "audio/";
    }

    // Setter for idOfNextElementToPlay
    public setCurrentAudioId(
        id: string // The next ID to set
    ) {
        if (!this.currentAudioId || this.currentAudioId != id) {
            this.currentAudioId = id;
            this.updatePlayerStatus();
        }
    }

    // Gecko has no way of knowing that we've created or modified the audio file,
    // so it will cache the previous content of the file or
    // remember if no such file previously existed. So we add a bogus query string
    // based on the current time so that it asks the server for the file again.
    // Fixes BL-3161
    private updatePlayerStatus() {
        console.assert(this.currentAudioId !== null);

        const player = this.getMediaPlayer();
        player.setAttribute(
            "src",
            this.currentAudioUrl(this.currentAudioId) +
                "&nocache=" +
                new Date().getTime()
        );
    }

    public async startRecordCurrentAsync(): Promise<void> {
        if (!this.isEnabledOrExpected("record")) {
            return;
        }

        this.resetAudioIfPaused();
        // If we were paused highlighting one sentence but are recording in text box mode,
        // things could get confusing. At least make sure the selection reflects what we
        // actually want to record.
        await this.setHighlightToAsync({
            newElement: this.getCurrentAudioSentence()!,
            shouldScrollToElement: true,
            forceRedisplay: true
        });

        toastr.clear();

        this.recording = true;

        const id = this.getCurrentAudioId();

        this.clearAudioSplit();

        return axios
            .post("/bloom/api/audio/startRecord?id=" + id)
            .then(result => {
                this.setStatus("record", Status.Active);
                // The active device MIGHT have changed, if the user unplugged since we
                // chose it.
                this.updateInputDeviceDisplay();
            })
            .catch(error => {
                toastr.error(error.statusText);
                console.log(error.statusText);
            });
    }

    private getCurrentAudioId(): string | undefined {
        let id: string | undefined = undefined;
        const pageDocBody = this.getPageDocBody();
        const audioCurrentElements = pageDocBody!.getElementsByClassName(
            kAudioCurrent
        );
        let currentElement: Element | null = null;
        if (audioCurrentElements.length > 0) {
            currentElement = audioCurrentElements.item(0);
        }
        if (currentElement) {
            if (currentElement.hasAttribute("id")) {
                id = currentElement.getAttribute("id")!;
            } else {
                id = AudioRecording.createValidXhtmlUniqueId();
                currentElement.setAttribute("id", id);
            }
        }
        return id;
    }

    public async endRecordCurrentAsync(): Promise<void> {
        if (!this.recording) {
            // will trigger if the button wasn't enabled, so the recording never started
            return;
        }

        this.recording = false;
        this.awaitingNewRecording = true;

        try {
            await axios
                .post("/bloom/api/audio/endRecord")
                .then(this.finishNewRecordingOrImportAsync.bind(this));
        } catch (error) {
            this.awaitingNewRecording = false;
            await this.changeStateAndSetExpectedAsync("record"); //record failed, so we expect them to try again
            if (error.response) {
                toastr.error(error.response.statusText);
                console.log(error.response.statusText);
            } else {
                toastr.error(error);
                console.log(error);
            }
            this.updatePlayerStatus();
        }
        this.updateDisplay();
    }

    private async finishNewRecordingOrImportAsync(): Promise<void> {
        const currentTextBox = this.getCurrentTextBoxSync();
        if (currentTextBox) {
            if (this.recordingMode == RecordingMode.TextBox) {
                // Reset the audioRecordingTimings.
                //
                // When ending a recording for a whole text box, we enter the state for Recording=TextBox,Playback=TextBox.
                // (Previously, it may have been in Record=TextBox,Play=Sentence if switching from Record by Sentence to Record By Text Box
                // Being in that intermediate state allows the user to easily switch between Sentence and TextBox without losing their existing sentence recordings.)
                this.updateMarkupForTextBox(
                    currentTextBox,
                    RecordingMode.TextBox,
                    RecordingMode.TextBox
                );
                await this.resetCurrentAudioElementAsync(currentTextBox);

                currentTextBox.removeAttribute(kEndTimeAttributeName);
            }

            const recordable = new Recordable(currentTextBox);
            recordable.setChecksum();
        }

        this.updatePlayerStatus();

        await this.changeStateAndSetExpectedAsync("play");
        this.updateDisplay();
    }

    // Called when we get a duration for a current audio element. Mainly we want it after recording a new one.
    // However, for older documents that don't have this, just playing them all will add the new info...
    // or even just stepping through with Next.
    private durationChanged(): void {
        this.awaitingNewRecording = false;

        const current = this.getCurrentAudioSentence();
        if (current) {
            const player = this.getMediaPlayer();
            current.setAttribute("data-duration", player.duration.toString());
        }
    }

    // Returns the audio-sentence corresponding to audio-current
    // In many cases the two are one and the same.
    // However, in Soft Split mode, the two are different. The audio-current is the visually-highlighted sub-element. Its parent though is the one with audio-sentence class (and has a physical audio file associated with it)
    public getCurrentAudioSentence(): HTMLElement | null {
        const currentHighlight = this.getCurrentHighlight();
        if (!currentHighlight) {
            return null;
        }

        let currToExamine: HTMLElement | null = currentHighlight;

        while (
            currToExamine &&
            !currToExamine.classList.contains(kAudioSentence) &&
            // Review: another option is to say that if we don't find an audio-sentence parent, return the
            // currentHighlight thing we started with.
            !currToExamine.classList.contains(kBloomEditableTextBoxClass)
        ) {
            // Recursively go up the tree to find the enclosing div, if necessary
            currToExamine = currToExamine.parentElement; // Will return null if no parent
        }

        // Returning currentHighlight is a special case. Usually we expect to find audio-sentence on
        // the element that has audio-current or on one of its parents. But when we have switched back
        // from sentence recording to text box recording, but not yet made a new recording,
        // the audio-sentence elements are still sentences, but the current highlight is the bloom-editable,
        // so that's where currToExamine starts out, and we never find an audio-sentence. In that case,
        // the audio-current element is the one we want.
        return currToExamine ?? currentHighlight;
    }

    public getCurrentPlaybackMode(maySetHighlight = true): RecordingMode {
        const currentTextBox = this.getCurrentTextBox(maySetHighlight);
        if (!currentTextBox) {
            return RecordingMode.Sentence;
        }

        return this.getPlaybackMode(currentTextBox);
    }

    private getPlaybackMode(textBox: Element): RecordingMode {
        let divAudioSentenceCount = 0;
        let spanAudioSentenceCount = 0;

        if (textBox.classList.contains(kAudioSentence)) {
            ++divAudioSentenceCount;
        }

        const audioSentenceCollection = textBox.getElementsByClassName(
            kAudioSentence
        );

        for (let i = 0; i < audioSentenceCollection.length; ++i) {
            const audioSentenceElement = audioSentenceCollection.item(i);
            if (audioSentenceElement) {
                if (audioSentenceElement.nodeName === "DIV") {
                    ++divAudioSentenceCount;
                } else if (audioSentenceElement.nodeName === "SPAN") {
                    ++spanAudioSentenceCount;
                }
            }
        }

        // Enhance: You could break early out of the loop and refactor if you are confident this assert won't fail.
        console.assert(
            !(divAudioSentenceCount > 0 && spanAudioSentenceCount > 0)
        );
        if (divAudioSentenceCount > spanAudioSentenceCount) {
            return RecordingMode.TextBox;
        } else if (divAudioSentenceCount < spanAudioSentenceCount) {
            return RecordingMode.Sentence;
        } else {
            // Yuck. They're equal (possibly both 0)
            // Just default to same as recording mode.
            return this.getRecordingModeOfTextBox(textBox);
        }
    }

    // The method called when the 'play' button is clicked. If we are already playing, it stops play.
    private async togglePlayCurrentAsync(): Promise<void> {
        toastr.clear();

        if (this.getStatus("play") === Status.Active) {
            const mediaPlayer = this.getMediaPlayer();
            mediaPlayer.pause();
            // We don't want to mess with the highlights like playEnded would do, let alone
            // move to the next segment, but we do want to switch status. "next" is automatically
            // substituted for "split" if we're in a mode where "split" does not apply.
            return this.changeStateAndSetExpectedAsync("split");
        }

        if (!this.isEnabledOrExpected("play")) {
            return;
        }

        const oldElementsToPlay = this.elementsToPlayConsecutivelyStack;
        const oldTimings = this.subElementsWithTimings;

        const audioElement = this.getCurrentAudioSentence();
        if (audioElement) {
            this.fixHighlighting(audioElement);
        }

        this.elementsToPlayConsecutivelyStack = [];

        // We want to play everything (highlighted according to the unit of playback) within the unit of RECORDING.
        if (this.recordingMode == RecordingMode.TextBox) {
            const currentTextBox = this.getCurrentTextBox();
            if (currentTextBox) {
                const audioSegments = this.getAudioSegmentsWithinElement(
                    currentTextBox
                );

                if (audioSegments && audioSegments.length > 0) {
                    // This text box is in 4.5 Format (Hard Split) where it contains audio-sentence elements within it
                    this.elementsToPlayConsecutivelyStack = jQuery
                        .makeArray(audioSegments)
                        .reverse();
                } else {
                    // Nope, no audio-sentence elements within it. Uses 4.6 Format (Soft Split)
                    this.elementsToPlayConsecutivelyStack = [currentTextBox];
                }
            }
        } else {
            // Pure Sentence mode.
            const currentHighlight = this.getCurrentHighlight();
            if (currentHighlight) {
                this.elementsToPlayConsecutivelyStack = [currentHighlight];
            }
        }

        // more complicated but similar to return this.playCurrentInternalAsync();
        const mediaPlayer = this.getMediaPlayer();
        if (mediaPlayer.error) {
            // We can no longer rely on the error event occurring after play() is called.
            // If we pre-load audio, the error event occurs on load (which will be before play).
            // So, we check the .error property to see if an error already occurred and if so, skip past the play straight to the playEnded() which is supposed to be called on error.
            return this.playEndedAsync(); // Will also start playing the next audio to play.
        } else {
            const currentTextBox = this.getCurrentTextBox();
            if (!currentTextBox) {
                return;
            }

            this.setupTimings(currentTextBox);

            // can we resume a paused playback? Only if playback is in progress and the things
            // still to be played are the tail end of what we would currently play. Because the
            // two arrays are stored in reverse order, that is true if they START with the same things.
            if (
                oldElementsToPlay &&
                oldElementsToPlay.length > 0 &&
                this.arrayStartsWith(
                    this.elementsToPlayConsecutivelyStack,
                    oldElementsToPlay
                ) &&
                this.arrayStartsWith(this.subElementsWithTimings, oldTimings)
            ) {
                // We can resume. The steps here are a subset of what we normally do,
                // leaving out initializing the media player and setting up highlights.
                // Note: the setTimeout loop that advances subElementTimings has never stopped,
                // so it will continue to advance things as the mediaPlayer resumes.
                this.elementsToPlayConsecutivelyStack = oldElementsToPlay;
                this.subElementsWithTimings = oldTimings;
                this.removeExpectedStatusFromAll();
                this.setStatus("play", Status.Active);
                this.haveAudio = true;
                mediaPlayer.play();
                return;
            }

            await this.setSoundAndHighlightAsync({
                newElement: this.elementsToPlayConsecutivelyStack[
                    this.elementsToPlayConsecutivelyStack.length - 1
                ],
                shouldScrollToElement: true,
                suppressHighlightIfNoAudio: true
            });
            this.removeExpectedStatusFromAll();
            this.setStatus("play", Status.Active);

            this.haveAudio = true;
            // Start playing the audio first.
            mediaPlayer.play();
            ++this.currentAudioSessionNum;

            // Now set in motion what is needed to advance the highlighting (if applicable)
            this.highlightNextSubElement(this.currentAudioSessionNum);
        }
    }

    private arrayStartsWith(long: Array<any>, short: Array<any>): boolean {
        if (long.length < short.length) {
            return false;
        }
        for (let i = 0; i < short.length; i++) {
            if (long[i] !== short[i] && !this.arrayEquals(long[i], short[i]))
                return false;
        }
        return true;
    }

    private arrayEquals(a, b) {
        return (
            Array.isArray(a) &&
            Array.isArray(b) &&
            a.length === b.length &&
            a.every((val, index) => val === b[index])
        );
    }

    private async playCurrentInternalAsync(): Promise<void> {
        const mediaPlayer = this.getMediaPlayer();
        if (mediaPlayer.error) {
            // We can no longer rely on the error event occurring after play() is called.
            // If we pre-load audio, the error event occurs on load (which will be before play).
            // So, we check the .error property to see if an error already occurred and if so, skip past the play straight to the playEnded() which is supposed to be called on error.
            return this.playEndedAsync(); // Will also start playing the next audio to play.
        } else {
            const currentTextBox = this.getCurrentTextBox();
            if (!currentTextBox) {
                return;
            }

            this.setupTimings(currentTextBox);

            // Start playing the audio first.
            mediaPlayer.play();
            ++this.currentAudioSessionNum;

            // Now set in motion what is needed to advance the highlighting (if applicable)
            this.highlightNextSubElement(this.currentAudioSessionNum);
        }
    }

    private setupTimings(currentTextBox: HTMLElement): void {
        // Regardless of whether we end up using timingsStr or not,
        // we should reset this now in case the previous page used it and was still playing
        // when the user flipped to the next page.
        this.subElementsWithTimings = [];

        const timingsStr = currentTextBox.getAttribute(kEndTimeAttributeName);
        if (timingsStr) {
            const childSpanElements = currentTextBox.querySelectorAll(
                `span.${kSegmentClass}`
            );
            const fields = timingsStr.split(" ");
            const subElementCount = Math.min(
                fields.length,
                childSpanElements.length
            );

            for (let i = subElementCount - 1; i >= 0; --i) {
                const durationSecs: number = Number(fields[i]);
                if (isNaN(durationSecs)) {
                    continue;
                }
                this.subElementsWithTimings.push([
                    childSpanElements.item(i),
                    durationSecs
                ]);
            }
        }
    }

    // Moves the highlight to the next sub-element
    // Note: May kick off some async work, but it's fairly inconsequential and no need to await it currently.
    // originalSessionNum: The value of this.currentAudioSessionNum at the time when the audio file started playing.
    //     This is used to check in the future if the timeouts we started are for the right session
    // startTimeInSecs is an optional fallback that will be used in case the currentTime cannot be determined from the audio player element.
    private highlightNextSubElement(
        originalSessionNum: number,
        startTimeInSecs: number = 0
    ) {
        // the item should not be popped off the stack until it's completely done with.
        const subElementCount = this.subElementsWithTimings.length;

        if (subElementCount <= 0) {
            return;
        }

        const topTuple = this.subElementsWithTimings[subElementCount - 1];
        const element = topTuple[0];
        const endTimeInSecs: number = topTuple[1];

        // Kicks off some async work of minimal consequence, no need to await it currently.
        this.setHighlightToAsync({
            newElement: element,
            shouldScrollToElement: true,
            suppressHighlightIfNoAudio: false // Should be false when playing sub-elements, because the highlighted sub-element doesn't have audio. The audio belongs to parent.
        });

        const mediaPlayer: HTMLMediaElement = this.getMediaPlayer();
        let currentTimeInSecs: number = mediaPlayer.currentTime;
        if (currentTimeInSecs <= 0) {
            currentTimeInSecs = startTimeInSecs;
        }

        // Handle cases where the currentTime has already exceeded the nextStartTime
        //   (might happen if you're unlucky in the thread queue... or if in debugger, etc.)
        // But instead of setting time to 0, set the minimum highlight time threshold to 0.1 (this threshold is arbitrary).
        const durationInSecs = Math.max(endTimeInSecs - currentTimeInSecs, 0.1);

        setTimeout(() => {
            this.onSubElementHighlightTimeEnded(originalSessionNum);
        }, durationInSecs * 1000);
    }

    // Handles a timeout indicating that the expected time for highlighting the current subElement has ended.
    // If we've really played to the end of that subElement, highlight the next one (if any).
    // originalSessionNum: The value of this.currentAudioSessionNum at the time when the audio file started playing.
    //     This is used to check in the future if the timeouts we started are for the right session
    private onSubElementHighlightTimeEnded(originalSessionNum: number) {
        // Check if the user has changed pages since the original audio for this started playing.
        if (originalSessionNum !== this.currentAudioSessionNum) {
            return;
        }

        const subElementCount = this.subElementsWithTimings.length;
        if (subElementCount <= 0) {
            console.assert(
                false,
                "Unexpected: subElementsWithTimings is empty but the function was expecting at least one element. If this happens deterministically, it is an error."
            );
            return;
        }

        const mediaPlayer: HTMLMediaElement = this.getMediaPlayer();
        if (mediaPlayer.ended || mediaPlayer.error) {
            return;
        }
        const playedDurationInSecs: number | undefined | null =
            mediaPlayer.currentTime;

        // Peek at the next sentence and see if we're ready to start that one. (We might not be ready to play the next audio if the current audio got paused).
        const subElementWithTiming = this.subElementsWithTimings[
            subElementCount - 1
        ];
        const nextStartTimeInSecs = subElementWithTiming[1];

        if (
            playedDurationInSecs &&
            playedDurationInSecs < nextStartTimeInSecs
        ) {
            // Still need to wait. Exit this function early and re-check later.
            const minRemainingDurationInSecs =
                nextStartTimeInSecs - playedDurationInSecs;
            setTimeout(() => {
                this.onSubElementHighlightTimeEnded(originalSessionNum);
            }, minRemainingDurationInSecs * 1000);

            return;
        }

        this.subElementsWithTimings.pop();

        this.highlightNextSubElement(originalSessionNum, nextStartTimeInSecs);
    }

    // 'Listen' is shorthand for playing all the sentences on the page in sequence.
    // Returns a promise that is fulfilled when the play has been STARTED (not completed).
    public async listenAsync(): Promise<void> {
        this.resetAudioIfPaused();

        this.fixHighlighting();

        this.elementsToPlayConsecutivelyStack = jQuery
            .makeArray(this.sortByTabindex(this.getAudioElements(false)))
            .reverse();

        const stackSize = this.elementsToPlayConsecutivelyStack.length;
        if (stackSize === 0) {
            return;
        }
        const firstElementToPlay = this.elementsToPlayConsecutivelyStack[
            stackSize - 1
        ]; // Remember to pop it when you're done playing it. (i.e., in playEnded)

        await this.setSoundAndHighlightAsync({
            newElement: firstElementToPlay,
            shouldScrollToElement: true,
            suppressHighlightIfNoAudio: true
        });
        this.setStatus("listen", Status.Active);
        return this.playCurrentInternalAsync();
    }

    // This is currently used in Motion, which removes all the current
    // audio markup afterwards. If we use it in this tool, we need to do more,
    // such as setting the current state of controls.
    public stopListen(): void {
        this.getMediaPlayer().pause();
    }

    private async playEndedAsync(): Promise<void> {
        if (
            this.elementsToPlayConsecutivelyStack &&
            this.elementsToPlayConsecutivelyStack.length > 0
        ) {
            const currentElement = this.elementsToPlayConsecutivelyStack.pop();
            const newStackCount = this.elementsToPlayConsecutivelyStack.length;
            if (newStackCount > 0) {
                // More items to play
                const nextElement = this.elementsToPlayConsecutivelyStack[
                    newStackCount - 1
                ];
                await this.setSoundAndHighlightAsync({
                    newElement: nextElement,
                    shouldScrollToElement: true,
                    suppressHighlightIfNoAudio: true,
                    oldElement: currentElement
                });
                return this.playCurrentInternalAsync();
            } else {
                // Nothing left to play
                this.elementsToPlayConsecutivelyStack = [];
                this.subElementsWithTimings = [];
                ++this.currentAudioSessionNum;

                if (this.recordingMode == RecordingMode.TextBox) {
                    // The playback mode could've been playing in Sentence mode (and highlighted the Playback Segment: a sentence)
                    // But now we need to switch the highlight back to show the Recording segment.
                    const currentTextBox = this.getCurrentTextBox();
                    console.assert(
                        !!currentTextBox,
                        "CurrentTextBox not expected to be null"
                    );
                    if (currentTextBox) {
                        await this.setCurrentAudioElementBasedOnRecordingModeAsync(
                            currentTextBox,
                            false
                        );
                    }
                }

                this.revertFixHighlighting();

                // For Play (Check) in sentence mode, no need to adjust the current highlight. Just leave it on whatever it was on before.
                //  (Assumption: Record by Sentence, Play by Text Box mode combination is not allowed)

                // Enhance: Or maybe for Listen To Whole Page, it should remember what the highlight was on before and move it back to there?
            }
        } else {
            this.revertFixHighlighting();
        }

        // Change state to "Split" if possible but fallback to Next if not.
        // TODO: Maybe we should fallback to Listen to Whole Page if Next is not available (A.k.a. you just checked the last thing)
        //  What if you reached here by listening to the whole page? Does it matter that we'll push them toward listening to it again?
        return this.changeStateAndSetExpectedAsync("split");
    }

    private selectInputDevice(): void {
        // REVIEW: this may in fact be unneeded but I'm just trying to get eslint set up and conceivably it is intentional
        // eslint-disable-next-line @typescript-eslint/no-this-alias
        const thisClass = this;
        get("audio/devices", result => {
            const data = result.data; // Axios apparently recognizes the JSON and parses it automatically.
            // Retrieves JSON generated by AudioRecording.AudioDevicesJson
            // Something like {"devices":["microphone", "Logitech Headset"], "productName":"Logitech Headset", "genericName":"Headset" },
            // except that in practice currrently the generic and product names are the same and not as helpful as the above.
            if (data.devices.length <= 1) return; // no change is possible.
            if (data.devices.length == 2) {
                // Just toggle between them
                const device =
                    data.devices[0] == data.productName
                        ? data.devices[1]
                        : data.devices[0];
                axios
                    .post("/bloom/api/audio/currentRecordingDevice", device, {
                        headers: { "Content-Type": "text/plain" }
                    })
                    .then(result => {
                        this.updateInputDeviceDisplay();
                    })
                    .catch(error => {
                        toastr.error(error.statusText);
                    });
                return;
            }
            const devList = $("#audio-devlist");
            devList.empty();
            for (let i = 0; i < data.devices.length; i++) {
                // convert "Microphone (xxxx)" --> xxxx, where the final ')' is often missing (cut off somewhere upstream)
                let label = data.devices[i].replace(
                    /Microphone \(([^\)]*)\)?/,
                    "$1"
                );
                //make what's left safe for html
                label = $("<div>")
                    .text(label)
                    .html();
                // preserve the product name, which is the id we will send back if they choose it
                const menuItem = devList.append(
                    '<li data-choice="' + i + '">' + label + "</li>"
                );
            }
            (<any>devList)
                .one(
                    "click",
                    function(event) {
                        devList.hide();
                        const choice = $(event.target).data("choice");
                        axios
                            .post(
                                "/bloom/api/audio/currentRecordingDevice",
                                data.devices[choice],
                                { headers: { "Content-Type": "text/plain" } }
                            )
                            .then(result => {
                                this.updateInputDeviceDisplay();
                            })
                            .catch(error => {
                                toastr.error(error.statusText);
                            });
                    }.bind(this)
                )
                .show()
                .position({
                    my: "left top",
                    at: "left bottom",
                    of: $("#audio-input-dev")
                });
        });
    }

    private updateInputDeviceDisplay(): void {
        get("audio/devices", result => {
            const data = result.data;
            // See selectInputDevice for what is retrieved.
            const genericName = data.genericName || "";
            const productName = data.productName || "";

            const defaultSrcPath = "/bloom/bookEdit/toolbox/talkingBook/";
            const nameToImageSrc = [
                ["internal", defaultSrcPath + "computer.svg"],
                // checking for "Array" is motivated by JohnT's dell laptop, where the internal microphone
                // comes up as "Microphone Array (2 RealTek Hi".
                ["array", defaultSrcPath + "computer.svg"],
                ["webcam", defaultSrcPath + "webcam.svg"],
                // checking for "Headse" is motivated by JohnT's Logitech Headset, which comes up as "Microphone (Logitech USB Headse".
                ["headse", defaultSrcPath + "headset.svg"],
                ["usb audio", defaultSrcPath + "headset.svg"], // we don't really know... should we just show a USB icon?
                ["plantronics", defaultSrcPath + "headset.svg"],
                ["andrea", defaultSrcPath + "headset.svg"], //usb-to-line
                ["vxi", defaultSrcPath + "headset.svg"], // headsets and usb-to-line
                ["line", defaultSrcPath + "lineaudio.svg"],
                ["high def", defaultSrcPath + "lineaudio.svg"],
                ["zoom", defaultSrcPath + "recorder.svg"]
            ];
            // Default if we don't recognize anything significant in the name of the current device.
            let imageSrc = defaultSrcPath + "microphone.svg";
            for (let i = 0; i < nameToImageSrc.length; i++) {
                const [pattern, imageSrcValue] = nameToImageSrc[i];
                if (
                    genericName.toLowerCase().indexOf(pattern) > -1 ||
                    productName.toLowerCase().indexOf(pattern) > -1
                ) {
                    imageSrc = imageSrcValue;
                    break;
                }
            }

            // Don't mislead user if we couldn't find any devices due to an error or lack of device.
            // (See https://issues.bloomlibrary.org/youtrack/issue/BL-7272.)
            if (!data.genericName && !data.productName)
                imageSrc = "/bloom/images/Attention.svg";

            const devButton = $("#audio-input-dev");
            devButton.attr("src", imageSrc);
            devButton.attr("title", productName);
        });
    }

    // Clear the recording for this sentence
    public async clearRecordingAsync(): Promise<void> {
        toastr.clear();

        this.resetAudioIfPaused();

        if (!this.isEnabledOrExpected("clear")) {
            return;
        }
        // First determine which IDs we need to delete.
        const elementIdsToDelete: string[] = [];
        if (this.recordingMode == RecordingMode.Sentence) {
            elementIdsToDelete.push(this.currentAudioId);
        } else {
            // i.e., AudioRecordingMode = TextBox
            // In particular, AudioRecordingMode = TextBox but PlaybackMode = Sentence is more complicated.
            // Need to delete all the segments (sentences) in this text box.
            // (TextBox/TextBox is easy and works either way, whether same as Sentence/Sentence or using the logic to find all segments within the text box used for TextBox/Sentence)
            const sentences = this.getAudioSegmentsInCurrentTextBox();
            for (let i = 0; i < sentences.length; ++i) {
                elementIdsToDelete.push(sentences[i].id);
            }
        }

        // Now go about sending out the API calls to actually delete them.
        const promisesToAwait: Promise<void>[] = [];
        for (let i = 0; i < elementIdsToDelete.length; ++i) {
            const idToDelete = elementIdsToDelete[i];

            const request = axios
                .post("/bloom/api/audio/deleteSegment?id=" + idToDelete)
                .then(result => {
                    // data-duration needs to be deleted when the file is deleted.
                    // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3671.
                    // Note: this is not foolproof because the durationchange handler is
                    // being called asynchronously with stale data and sometimes restoring
                    // the deleted attribute.
                    const current = this.getPageDocBodyJQuery().find(
                        "#" + idToDelete
                    );
                    if (current.length !== 0) {
                        current.first().removeAttr("data-duration");
                    }
                })
                .catch(error => {
                    toastr.error(error.statusText);
                });
            promisesToAwait.push(request);
        }

        // Remove the recording md5(s)
        const current = this.getAudioCurrentElement();
        if (current) {
            const recordable = new Recordable(current);
            recordable.unsetChecksum();

            this.clearAudioSplit();
        }

        this.updatePlayerStatus();

        await Promise.all(promisesToAwait);

        await this.changeStateAndSetExpectedAsync("record");
        this.updateDisplay();
    }

    private doesRecordingExistForCurrentSelection(): boolean {
        return document
            .getElementById("audio-play")!
            .classList.contains("enabled");
    }

    // Update the input element (checkbox) and turn on the playback order controls on the visible
    // translation-groups.
    public setShowPlaybackOrderMode(isOn: boolean) {
        this.inShowPlaybackOrderMode = isOn;
        const docBody = this.getPageDocBody();
        if (!docBody) {
            return;
        }
        if (this.inShowPlaybackOrderMode) {
            this.showPlaybackOrderUi(docBody);
        } else {
            this.removePlaybackOrderUi(docBody);
        }
        this.updateDisplay();
    }

    // It's a bit of a toss-up whether this function belongs here or in talkingBook.ts. It's primary
    // responsibility is to toggle the checkbox which is in toolbox land, so in that way it would
    // be more at home in talkingBooks.ts, which manages the toolbox. But many of the side effects
    // of switching it on (and especially off) involve functions here, and it affects the content
    // page as well as the toolbox. Another reason is that other checkboxes in the toolbox, like
    // Show playback order buttons, have their logic here, so it feels cleaner to keep their handling
    // together in one place.
    public async setShowingImageDescriptions(isOn: boolean) {
        this.showingImageDescriptions = isOn;
        const page = ToolBox.getPage();
        if (this.showingImageDescriptions) {
            if (page) {
                // we should always have a page, but testing makes lint happy
                showImageDescriptions(page);
            }
            // If we don't already have them, set them up
            if (page) {
                setupImageDescriptions(
                    page,
                    () => {},
                    () => {}
                );
                // Make sure audio recording is set up for the image descriptions, whether
                // they are new (and empty) or they already exist.  See BL-14436.
                await this.setupAndUpdateMarkupAsync();
                // The main reason for this is that we may need to change the enabled state
                // of buttons so the audio highlight can be moved into the image description.
                await this.changeStateAndSetExpectedAsync("record");
            }
        } else if (page) {
            // page should always be set, just making compiler happy
            // hides them
            hideImageDescriptions(page);
        }
        // If the highlight was on something not currently visible, move the selection
        const current = this.getCurrentHighlight();
        if (page && current && !this.isVisible(current)) {
            this.removeAudioCurrent(page);
            await this.setCurrentAudioElementToDefaultAsync();
        }
        // Whether or not we had to move the selection, some button states may need to change.
        // For example, perhaps there was previously nowhere for the 'next' button to take us,
        // but now we revealed a canvas element which is set to be after the current element.
        await this.updateButtonStateAsync("record");
        this.updateDisplay();
    }
    private showPlaybackOrderUi(docBody: HTMLElement) {
        this.removeAudioCurrent(docBody);
        this.playbackOrderCache = [];
        const translationGroups = this.getVisibleTranslationGroups(docBody);
        if (translationGroups.length < 1) {
            // no point in displaying playback order if there aren't any recordable divs
            return;
        }

        for (let i = 0; i < translationGroups.length; i++) {
            const currentTranslationGroup = translationGroups[i];
            const newDiv = `<div class="bloom-ui ${kPlaybackOrderContainerClass}"></div>`;
            currentTranslationGroup.insertAdjacentHTML("afterbegin", newDiv);
            const containerDivs = currentTranslationGroup.parentElement!.getElementsByClassName(
                kPlaybackOrderContainerClass
            );
            if (containerDivs.length !== 1) {
                continue; // paranoia, we just put it there!
            }
            const containerDiv = <HTMLDivElement>containerDivs[0];
            if (containerDiv === null) {
                continue; // paranoia, we just put it there!
            }
            const existingTabindex = currentTranslationGroup.getAttribute(
                "tabindex"
            );
            this.playbackOrderCache.push({
                containerDiv: containerDiv,
                sourceTranslationGroup: currentTranslationGroup,
                // Give any translationGroup that doesn't already have a tabindex, an index of 999.
                myPosition: existingTabindex ? Number(existingTabindex) : 999
            });
        }
        this.sortOutTabindexValues();
        this.renderPlaybackControls();
        this.setDisableEverythingMode(true);
        this.inShowPlaybackOrderMode = true;
    }

    private renderPlaybackControls() {
        const cacheLength = this.playbackOrderCache.length;
        for (let i = 0; i < cacheLength; i++) {
            // "birth" the React component inside the containerDiv
            const playbackOrderObj = this.playbackOrderCache[i];
            this.renderOnePlaybackOrderButtonSet(cacheLength, playbackOrderObj);
        }
    }

    private sortOutTabindexValues() {
        this.playbackOrderCache.sort((a, b) => {
            return a.myPosition - b.myPosition;
        });
        // Some of the translationGroups don't yet have a tabindex; reset all of them.
        for (let i = 0; i < this.playbackOrderCache.length; i++) {
            const playbackOrderInfo = this.playbackOrderCache[i];
            this.setTabindexInCacheAndHtml(playbackOrderInfo, i + 1); // NOT zero-based
        }
    }

    private setTabindexInCacheAndHtml(
        playbackOrderInfo: IPlaybackOrderInfo,
        index: number
    ) {
        playbackOrderInfo.sourceTranslationGroup.setAttribute(
            "tabindex",
            index.toString()
        );
        playbackOrderInfo.myPosition = index;
    }

    private renderOnePlaybackOrderButtonSet(
        listSize: number,
        playbackOrderInfo: IPlaybackOrderInfo
    ): void {
        ReactDOM.render(
            React.createElement(PlaybackOrderControls, {
                maxOrder: listSize,
                orderOneBased: playbackOrderInfo.myPosition,
                onIncrease: bumpUp,
                onDecrease: bumpDown
            }),
            playbackOrderInfo.containerDiv
        );
    }

    // 'bumpUp' means increase 'myOrderNum' (Add button), or move this translationGroup
    // closer to the end of the recording order.
    public bumpUp(whichPositionToBump: number) {
        if (
            whichPositionToBump >= this.playbackOrderCache.length ||
            whichPositionToBump < 1
        ) {
            return; // The React controls should ensure this anyway.
        }
        const srcInfo = this.playbackOrderCache[whichPositionToBump - 1];
        const destInfo = this.playbackOrderCache[whichPositionToBump];
        this.setTabindexInCacheAndHtml(srcInfo, whichPositionToBump + 1);
        this.setTabindexInCacheAndHtml(destInfo, whichPositionToBump);

        // re-sort
        this.sortOutTabindexValues();
        this.renderPlaybackControls();
    }

    // 'bumpDown' means decrease 'myOrderNum' (Remove button), or move this translationGroup
    // closer to the beginning of the recording order.
    public bumpDown(whichPositionToBump: number) {
        if (
            whichPositionToBump < 2 ||
            whichPositionToBump > this.playbackOrderCache.length
        ) {
            return; // The React controls should ensure this anyway.
        }
        const srcInfo = this.playbackOrderCache[whichPositionToBump - 1];
        const destInfo = this.playbackOrderCache[whichPositionToBump - 2];
        this.setTabindexInCacheAndHtml(srcInfo, whichPositionToBump - 1);
        this.setTabindexInCacheAndHtml(destInfo, whichPositionToBump);

        // re-sort
        this.sortOutTabindexValues();
        this.renderPlaybackControls();
    }

    // NOTE: This function kicks off some async work, but doesn't await them. (yet?)
    private removePlaybackOrderUi(
        docBody: HTMLElement,
        leaveChecked: boolean = false
    ) {
        const elementsToRemove = docBody.getElementsByClassName(
            kPlaybackOrderContainerClass
        );
        Array.from(elementsToRemove).forEach(element => {
            element.parentElement!.removeChild(element);
        });
        this.setDisableEverythingMode(false);
        this.setCurrentAudioElementToDefaultAsync();
    }

    private getVisibleTranslationGroups(
        docBody: HTMLElement
    ): HTMLDivElement[] {
        const result: HTMLDivElement[] = [];
        const transGroups = docBody.getElementsByClassName(
            kBloomTranslationGroupClass
        );
        for (let i = 0; i < transGroups.length; i++) {
            const currentTranslationGroup = <HTMLDivElement>transGroups.item(i);
            if (!this.isVisible(currentTranslationGroup)) {
                continue;
            }
            const visibleEditables = Array.from(
                currentTranslationGroup.getElementsByClassName("bloom-editable")
            ).filter(elem => this.isVisible(elem));
            if (visibleEditables.length > 0) {
                result.push(currentTranslationGroup);
            }
        }
        return result;
    }

    private setDisableEverythingMode(doDisable: boolean) {
        if (!this.disablingOverlay) {
            return; // should have been setup by now
        }
        const hiddenClass = "hiddenOverlay";
        if (doDisable) {
            this.disablingOverlay.classList.remove(hiddenClass);
        } else {
            this.disablingOverlay.classList.add(hiddenClass);
        }
    }

    // With css, the presence/absence of the checked class on the checkbox label determines its color.
    private setCheckboxLabelClass(
        addClass: boolean,
        clickHandlerElementId: string
    ) {
        const checkedClass = "checked";
        const checkboxLabel = (<HTMLLabelElement>(
            document.getElementById(clickHandlerElementId)
        )).nextElementSibling;
        if (checkboxLabel != null) {
            if (addClass) {
                checkboxLabel.classList.add(checkedClass);
            } else {
                checkboxLabel.classList.remove(checkedClass);
            }
        }
    }

    // Update the input element (e.g. checkbox) which visually represents the recording mode and updates the textbox markup to reflect the new mode.
    public async setRecordingModeAsync(
        recordingMode: RecordingMode,
        forceOverwrite: boolean = false
    ): Promise<void> {
        this.recordingMode = recordingMode;

        // Check if there are any audio recordings present.
        //   If so, these would become invalidated (and deleted down the road when the book's unnecessary files gets cleaned up)
        //   Warn the user if this deletion could happen
        //   We detect this state by relying on the same logic that turns on the Listen button when an audio recording is present
        if (!forceOverwrite && this.doesRecordingExistForCurrentSelection()) {
            if (
                this.recordingMode == RecordingMode.TextBox &&
                this.getCurrentPlaybackMode() == RecordingMode.TextBox
            ) {
                return;
            }
        }

        // Update the collection's default recording span mode to the new value
        // Note: We don't actually await the completion of this work.
        postJson("talkingBook/defaultAudioRecordingMode", this.recordingMode);
        this.cachedCollectionDefaultRecordingMode = this.recordingMode;

        let result;
        // Update the UI after clicking the checkbox
        if (this.previousRecordMode == RecordingMode.TextBox) {
            // This also implies converting the playback mode to Sentence, because we disallow Recording=Sentence,Playback=TextBox.
            // Enhance: Maybe don't bother if the current playback mode is already sentence?
            // Enhance: Maybe it means that you should be less aggressively trying to convert Markup into Playback=TextBox. Or that Clear should be more aggressively attempting ton convert into Playback=Sentence.

            const currentTextBox = this.getCurrentTextBox();
            if (currentTextBox) {
                this.updateMarkupForTextBox(
                    currentTextBox,
                    this.recordingMode,
                    RecordingMode.Sentence
                );
            }
            await this.resetCurrentAudioElementAsync();

            this.clearAudioSplit();

            // Enhance: Maybe could be Play if the current sentence already has text available?
            // Enhance: Maybe this function could have a Fallback optional parameter. And it would try to set Play, but switch to Record if not available.
            result = this.changeStateAndSetExpectedAsync("record");
        } else {
            // From Sentence -> TextBox, we don't convert the playback mode.
            // (since until/unless the user actually makes a new whole-text-box recording, we actually still have by-sentence recordings we can play)
            // Note: Calling updateMarkup to switch to TextBox and then calling it again to switch back to Sentence will generate different ID's
            // which means that existing audio will be purged soon and lost.
            // So, we delay actually changing the markup until the user actually records something
            // (Alternatively, I guess you can change here but you need to add UI to prevent the user from accidentally losing their recordings).

            const currentTextBox = this.getCurrentTextBox();
            if (currentTextBox) {
                this.persistRecordingMode(currentTextBox, this.recordingMode);
                await this.setCurrentAudioElementBasedOnRecordingModeAsync(
                    currentTextBox
                );
                result = this.changeStateAndSetExpectedAsync("record");
            }
        }
        this.previousRecordMode = this.recordingMode;
        return result;
    }

    public updateSplitButton(): void {
        const element = document.getElementById("audio-split-wrapper");
        if (element) {
            if (this.recordingMode == RecordingMode.TextBox) {
                element.classList.remove("hide-countable"); // When we make this button visible we have to adjust classes so that it starts to participate in the CSS that numbers the steps
                element.classList.add("talking-book-counter");
            } else {
                element.classList.add("hide-countable");
                element.classList.remove("talking-book-counter");
            }
        }
    }

    public persistRecordingMode(
        element: Element,
        recordingMode: RecordingMode = this.recordingMode
    ) {
        console.assert(
            recordingMode !== RecordingMode.Unknown,
            "AudioRecordingMode should not be set to Unknown"
        );
        element.setAttribute("data-audiorecordingmode", recordingMode);

        // This only set the RECORDING mode. Don't touch the audio-sentence markup, which represents the PLAYBACK mode.
    }

    // Gets the "page" iframe. May return null is the iframe doesn't exist.
    public getPageFrame(): HTMLIFrameElement | null {
        // Enhance: Maybe should just use the version in bloomFrames.ts instead?
        //   (we could add an async version there that would return a promise which is fulfilled when the frame becomes available AND loaded.)
        return <HTMLIFrameElement | null>(
            parent.window.document.getElementById("page")
        );
    }

    // The body of the editable page, a root for searching for document content.
    public getPageDocBody(): HTMLElement | null {
        // ENHANCE: What if the iFrame is there but hasn't finished loading yet? (Which will wipe contentWindow.document)
        const page = this.getPageFrame();
        if (!page || !page.contentWindow) return null;
        return page.contentWindow.document.body;
    }

    // The body of the editable page, a root for searching for document content.
    public getPageDocBodyJQuery(): JQuery {
        // Enhance: Delete all references one day
        const body = this.getPageDocBody();
        if (!body) return $();
        return $(body);
    }

    // Returns the element (could be either div, span, etc.) which is currently highlighted.
    public getCurrentHighlight(): HTMLElement | null {
        let page = this.getPageDocBodyJQuery();

        // ENHANCE: I don't think this really needs to be here?
        if (page.length <= 0) {
            // The first one is probably the right one when this case is triggered, but even if not, it's better than nothing.
            this.setCurrentAudioElementToDefaultAsync();
            page = this.getPageDocBodyJQuery();
        }

        const current = page.find(kAudioCurrentClassSelector);
        if (current && current.length > 0) {
            return current.get(0);
        }
        return null;
    }

    // Returns the text of the currently highlighted element
    public getCurrentText(): string {
        const currentHighlight = this.getCurrentHighlight();
        if (currentHighlight) {
            return currentHighlight.innerText;
        }
        return "";
    }

    public getFirstAudioSentenceWithinElement(
        element: Element | null
    ): Element | null {
        const audioSentences = this.getAudioSegmentsWithinElement(element);
        if (!audioSentences || audioSentences.length === 0) {
            return null;
        }

        return audioSentences[0];
    }

    public getAudioSegmentsWithinElement(element: Element | null): Element[] {
        const audioSegments: Element[] = [];

        if (element) {
            if (element.classList.contains(kAudioSentence)) {
                audioSegments.push(element);
            } else {
                const collection = element.getElementsByClassName(
                    kAudioSentence
                );
                for (let i = 0; i < collection.length; ++i) {
                    const audioSentenceElement = collection.item(i);
                    if (audioSentenceElement) {
                        audioSegments.push(audioSentenceElement);
                    }
                }
            }
        }

        return audioSegments;
    }

    // Returns a list of the audio segments (those with class "audio-sentence") in the text box (definitely a div) corresponding to the currently highlighted element (not necessariliy a div)
    public getAudioSegmentsInCurrentTextBox(): Element[] {
        const currentTextBox = this.getCurrentTextBox();
        const audioSegments = this.getAudioSegmentsWithinElement(
            currentTextBox
        );
        return audioSegments;
    }

    // Returns the Text Box div with the Current Highlight on it (that is, the one with .ui-audioCurrent class applied)
    // One difference between this function and getCurrentHighlight is that if the currently-highlighted element is not a div, then getCurrentTextBox() walks up the tree to find its most recent ancestor div. (whereas getCurrentHighlight can return non-div elements)
    // This function will also attempt to set it in case no such Current Highlight exists (which is often an erroneous state arrived at by race condition)
    public getCurrentTextBox(maySetHighlight = true): HTMLElement | null {
        const pageBody = this.getPageDocBody();
        if (
            !pageBody ||
            // Tests may not have a value for 'showPlaybackInput'.
            this.inShowPlaybackOrderMode
        ) {
            return null;
        }

        let audioCurrentElements = (Array.from(
            pageBody.getElementsByClassName(kAudioCurrent)
        ) as HTMLElement[]).filter(x => this.isVisible(x));

        if (audioCurrentElements.length === 0 && maySetHighlight) {
            // Oops, ui-audioCurrent not set on anything. Just going to have to stick it onto the first element.

            // ENHANCE: Theoretically, we should await this. (Or at least, the end of the function should await this promise
            // That means all the callers should be async'ify'd, which is like... everything. :(
            // But the only asynchronous work done is if you try to fallback by setting the current audio element
            // Luckily for us, setCurrentAudioElementToFirstAudioElementAsync() does enough work synchronously to allow
            // the rest of this function to complete without bothering to await the asynchronous work.
            //
            // So, I've made two versions of this function.
            // 1) This original version (that includes the asynchronous fallback)
            // 2) Also a synchronous (but no fallback) version of this function called getCurrentTextBoxSync()
            this.setCurrentAudioElementToDefaultAsync();
            audioCurrentElements = Array.from(
                pageBody.getElementsByClassName(kAudioCurrent)
            ) as HTMLElement[];

            if (audioCurrentElements.length <= 0) {
                return null;
            }
        }

        const currentTextBox = audioCurrentElements[0];
        console.assert(!!currentTextBox, "CurrentTextBox should not be null");
        return <HTMLElement | null>(
            this.getTextBoxOfElement(audioCurrentElements[0])
        );
    }

    public getAudioCurrentElement(): HTMLElement | null {
        const pageBody = this.getPageDocBody();
        if (!pageBody) {
            return null;
        }

        const audioCurrentElements = pageBody.getElementsByClassName(
            kAudioCurrent
        );

        if (audioCurrentElements.length === 0) {
            return null;
        }

        return audioCurrentElements.item(0) as HTMLElement;
    }

    // Gets the current text box. If none exists, immediately returns null.
    public getCurrentTextBoxSync(): HTMLElement | null {
        // TODO: Refactor the old getCurrentTextBox to something like: getCurrentTextBoxWithFallbackAsync
        // After that, you can rename this function to getCurrentTextBox

        const pageBody = this.getPageDocBody();
        if (
            !pageBody ||
            // Tests may not have a value for 'showPlaybackInput'.
            this.inShowPlaybackOrderMode
        ) {
            return null;
        }

        const audioCurrentElements = pageBody.getElementsByClassName(
            kAudioCurrent
        );

        if (audioCurrentElements.length === 0) {
            // Oops, ui-audioCurrent not set on anything. Just give up.
            return null;
        }

        const currentTextBox = this.getTextBoxOfElement(
            audioCurrentElements.item(0)
        );
        console.assert(!!currentTextBox, "CurrentTextBox should not be null");
        return <HTMLElement>currentTextBox;
    }

    // Returns the text box corresponding to this element.
    //   If element is a sentence span, it would return its most recent ancestor div which is a text box.
    //   If element is already a text box, it will return itself.
    //   Returns null if no ancestor which is a text box could be found.
    private getTextBoxOfElement(element: Element | null): Element | null {
        let currToExamine: Element | null = element;

        while (currToExamine && !this.isRecordableDiv(currToExamine, false)) {
            // Recursively go up the tree to find the enclosing div, if necessary
            currToExamine = currToExamine.parentElement; // Will return null if no parent
        }

        return currToExamine;
    }

    // Determines which element should receive the Current Highlight
    //   (Notably, checks to see if we should move from the existing Current Highlight (determined via CSS classes) to the actively focused element instead.)
    private async getWhichTextBoxShouldReceiveHighlightAsync(): Promise<HTMLElement | null> {
        const pageFrame = this.getPageFrame();

        // Determine which element should receive the current highlight
        if (
            pageFrame &&
            pageFrame.contentDocument &&
            pageFrame.contentDocument.activeElement &&
            this.isRecordableDiv(pageFrame.contentDocument.activeElement)
        ) {
            // FYI: The active element is the one that has "focus."  It may be a lot of other elements on the page, so definitely make sure to check that it is valid first (e.g. check IsRecordableDiv())
            // If the cursor is within a span within a div, it is the div that is the activeElement.  This is both a good thing (when we want to know what div the user is in) and a bad thing (in by sentence mode, we'd really prefer to know what span they're in but this is not trivial)
            return pageFrame.contentDocument.activeElement as HTMLElement;
        } else {
            const currentTextBox = this.getCurrentTextBox();

            if (currentTextBox) {
                return currentTextBox;
            } else {
                return await this.setCurrentAudioElementToDefaultAsync();
            }
        }
    }

    public async handleNewPageReady(
        deshroudPhraseDelimiters?: (page: HTMLElement | null) => void
    ): Promise<void> {
        // Changing the page causes the previous page's audio to stop playing (be "emptied").
        ++this.currentAudioSessionNum;

        // FYI, it is possible for handleNewPageReady to be called without updateMarkup() being called
        // (e.g. when opening the toolbox with an empty text box).
        this.initializeAudioRecordingMode();
        const docBody = this.getPageDocBody();

        // This check needs to be before the check for recordable divs below (which may return immediately), because sometimes
        // we may have empty textboxes that should nevertheless show the playback order UI.
        if (this.inShowPlaybackOrderMode) {
            if (docBody) {
                this.showPlaybackOrderUi(docBody);
            }
        }

        // The proper display setup must be in place before trying to update the audio markup.
        // See BL-14436.
        await this.setShowingImageDescriptions(this.showingImageDescriptions);

        this.watchElementsThatMightChangeAffectingVisibility(); // before we might return early if there are none!
        const editable = this.getRecordableDivs(true, false);
        docBody?.addEventListener(
            "mousedown",
            this.moveRecordingHighlightToClick,
            {
                capture: true
            }
        );
        if (editable.length === 0) {
            // no editable text on this page.
            this.haveAudio = false; // appropriately disables some advanced controls
            const result = this.changeStateAndSetExpectedAsync("");
            this.updateDisplay();
            return result;
        } else {
            if (deshroudPhraseDelimiters)
                deshroudPhraseDelimiters(this.getPageDocBody());
            await this.setupAndUpdateMarkupAsync();

            // See comment on this method.
            this.ensureHighlight(20);
        }

        this.updateDisplay();
    }

    // Declared in this unusual way so we can use it as an event handler without messing with bind
    // and still get the right 'this'.
    private moveRecordingHighlightToClick = async (event: MouseEvent) => {
        await this.moveRecordingHighlightToElement(event.target as HTMLElement);
    };

    // If we can somehow set audio recording to something associated with the argumennt, do so
    // and return true. (If it's already the current highlight, do nothing and return true).
    // If the element is something like a canvas element image that can't be recorded,
    // highlight nothing and return false.
    private async moveRecordingHighlightToElement(
        target: HTMLElement
    ): Promise<boolean> {
        // Probably redundant, but some nasty things can happen when we try to changeStateAndSetExpectedAsync()
        // when setSoundAndHighlightAsync() didn't actually set the highlight because we're in another tool.
        // This makes things a little safer.
        if (!this.isTalkingBookToolActive()) return false;
        let boxToSelect = target.closest(kAudioSentenceClassSelector);
        if (!boxToSelect) {
            // if it isn't one and isn't inside one, see if it contains one
            boxToSelect = target.getElementsByClassName(kAudioSentence)[0];
        }
        if (boxToSelect) {
            const textBox = this.getTextBoxOfElement(boxToSelect);
            if (!this.isRecordableDiv(textBox)) {
                // we just can't select anything here (probably it's empty)
                boxToSelect = null;
            }
            if (
                this.getRecordingModeOfTextBox(textBox) ===
                RecordingMode.TextBox
            ) {
                boxToSelect = textBox;
            }
        }
        const oldHighlight = this.getCurrentHighlight();
        if (!boxToSelect) {
            this.resetAudioIfPaused();
            this.removeAudioCurrent(this.getPageDocBody()!);
            this.changeStateAndSetExpectedAsync("");
            this.updateDisplay(false);
            return false;
        }
        if (boxToSelect !== oldHighlight) {
            this.resetAudioIfPaused();

            await this.setSoundAndHighlightAsync({
                newElement: boxToSelect,
                shouldScrollToElement: true
            });
            this.changeStateAndSetExpectedAsync("record");
        }
        return true;
    }

    private visibilityObserver: MutationObserver | null = null;

    private removeVisibilityObserver() {
        if (this.visibilityObserver) {
            this.visibilityObserver.disconnect();
            this.visibilityObserver = null;
        }
    }

    private watchElementsThatMightChangeAffectingVisibility() {
        this.removeVisibilityObserver();
        this.visibilityObserver = new MutationObserver(_ => {
            this.handleNewPageReady();
        });
        const divs = this.getDivsThatMightChangeAffectingVisibility();
        for (let i = 0; i < divs.length; i++) {
            this.visibilityObserver.observe(divs[i], {
                attributes: true,
                // Currently, the only elements that change causing visibility issues are
                // the keywords associated with slider items and the parent of the page,
                // changes to which can cause correct and wrong items to appear and disappear
                // in Bloom Games as we change modes or check answers (etc).
                // In all these cases, the only attribute that affects visibility
                // is currently class. If that changes, we'll need to add more attributes.
                attributeFilter: ["class"]
            });
        }
    }

    private getDivsThatMightChangeAffectingVisibility() {
        const pageBody = this.getPageDocBody();
        if (!pageBody) {
            return []; // shouldn't happen
        }
        const result = Array.from(
            // I don't much like that this function knows about this class, which belongs to a particular kind
            // of item in a particular kind of game. But I don't see how to encapsulate it better.
            // Slider: this line is only neded for the drag-word-slider game, which is mostly commented
            // out for now. But if we remove it here we have to further complicate things by providing
            // an alternative to convert to an array. I decided to just leave it in.
            pageBody.getElementsByClassName("bloom-wordChoice")
        );
        result.push(pageBody.parentElement!);
        return result;
    }

    private ensureHighlightToken;

    // This is a monumentally ugly workaround for BL-10471, a problem where a page has an image description
    // recorded in sentence mode that comes before a main body recorded in text mode. Somehow, things happen
    // out of order, such that although various things (including early calls to getCurrentTextBox)
    // put the highlight on the first sentence of the image description, later on that element is replaced
    // by another that does not have the highlight. I've confirmed this with debug code that sets an extra
    // attribute on the span when adding the highlight class: nothing else touches the new attribute, yet
    // it is missing from the page along with the highlight.
    // On the other hand, a mutation observer added at to the page at the same time as the highlight detects
    // no unexpected changes after setting the highlight.
    // The problem rarely happens in Firefox 92, perhaps once in 20 times, so I have had no luck using the
    // debugger to break when the object is modified.
    // The problem happens more often, though not always, when debugging in Firefox 60. However, Firefox 60
    // does not have the break-on-modification capability.
    // I added alerts everywhere that I could think of that might be modifying the DOM; parts of
    // makeAudioSentenceElements seemed most likely. None triggered when the problem happened.
    // After spending two days without useful progress, and considering that this only happens in a very rare
    // special case, I decided to just do this: at short intervals for a few seconds after initializing
    // the page, if we don't have a highlight we'll try again to make one.
    // Note that we don't stop this loop when we detect that there is a highlight, or after making one;
    // the problem we're trying to fix is that even though we have one it may unexpectedly go away.
    // We could go on doing this forever...there should always be something highlighted while this tool is
    // active, if there's any recordable text...but the problem seems to be transient so we may as well stop
    // and not go on using up cpu cycles forever. On my computer, 4 seconds is very generous...a half second
    // would do it...but other computers are slower.
    private ensureHighlight(repeats: number) {
        this.getCurrentTextBox();
        if (repeats > 0) {
            this.ensureHighlightToken = setTimeout(
                () => this.ensureHighlight(repeats - 1),
                200
            );
        }
    }

    // Should be called when whatever tool uses this is about to be hidden (e.g., changing tools or closing toolbox)
    public handleToolHiding() {
        this.isShowing = false;
        this.stopListeningForLevels();
        // In case this initialize loop is still going, stop it. Passing an invalid value won't hurt.
        clearTimeout(this.ensureHighlightToken);

        // Need to clear out any state. The next time this tool gets reopened, there is no guarantee that it will be reopened in the same context.
        this.recordingMode = RecordingMode.Unknown;
        // Don't want to leave this markup around to confuse other things.
        this.removeAudioCurrentFromPageDocBody();
        this.removeVisibilityObserver();
        this.getPageDocBody()?.removeEventListener(
            "mousedown",
            this.moveRecordingHighlightToClick,
            {
                capture: true
            }
        );
    }

    // Called upon handleNewPageReady(). Calls updateMarkup
    public async setupAndUpdateMarkupAsync(): Promise<void> {
        // For this purpose we want to include canvas elements even if they are hidden to show an image description,
        // since they may become visible when the show image description checkbox is deselected without
        // this code running again.
        const recordables = this.getRecordableDivs(true, true, false);

        const asyncTasks: Promise<() => void>[] = recordables.map(
            async elem => {
                await new Recordable(elem).setMd5IfMissingAsync();
                return this.tryGetUpdateMarkupForTextBoxActionAsync(elem);
            }
        );
        const updateFuncs = await Promise.all(asyncTasks);
        updateFuncs.forEach(x => x());

        // seting the current audio element right now is optional - we could also just wait around
        // for the first thing that calls getCurrentTextbox.
        // But let's just do it explicitly instead.
        await this.resetCurrentAudioElementAsync();

        return this.changeStateAndSetExpectedAsync("record");
    }

    // Entry point for TalkingBookTool's updateMarkupAsync() function. Does similar but less work than setupAndUpdateMarkupAsync.
    // Makes no changes itself, just figures out what they should be and returns a function that will quickly do the update.
    // (Typically, the function will be executed immediately unless the user has further edited the document in the meantime.)
    public async getUpdateMarkupAction(): Promise<() => void> {
        // Basic outline:
        // * This function gets called when the user types something
        // * First, see if we should update the Current Highlight to the element with the active focus instead.
        // * Then, change the HTML markup with the audio-sentence classes, ids, etc.
        // * Adjust the Current Highlight appropriately.
        const editables = this.getRecordableDivs(true, false);
        if (editables.length === 0) {
            // no editable text on this page.
            this.changeStateAndSetExpectedAsync("");
            return () => {
                // No updates needed because no editables
            };
        }

        // First, see if we should update the Current Highlight to the element with the active focus instead.
        await this.moveCurrentTextboxIfNeededAsync();
        const currentTextBox = this.getCurrentTextBox();
        if (!currentTextBox) {
            return () => {
                // This could be reached if you create a new page with the talking book tool open.
                // It's fine. Don't bother doing anything.
            };
        }

        // Now we can carry on with changing the HTML markup with the audio-sentence classes, ids, etc.
        // Because the user has edited the document, any existing recordings are suspect.
        // Although some might still be useful, and some may survive, we think at this point it is more important
        // that the markup is consistent with the current text in preparation for updating the recordings to match.
        const updateTheElement = await this.tryGetUpdateMarkupForTextBoxActionAsync(
            currentTextBox
        );

        return async () => {
            updateTheElement();
            // Adjust the current highlight appropriately
            // Regardless of whether it's present, we always need to set the current audio element
            // Obviously, when we update current markup, we normally set the current audio element afterward too.
            // The tricky case is when opening a hard split (version 4.5) book that has existing audio.
            // updateMarkupForCurrentText() does not run, but we still need it to move the current audio element.

            // I'd be happier if these functions didn't do anything async. But as far as I can tell, they
            // don't change anything that would interfere with typing if the user presses another key before
            // the async actions complete.
            await this.resetCurrentAudioElementAsync(currentTextBox);

            await this.changeStateAndSetExpectedAsync("record");
        };
    }

    // Note: This may temporarily put things into a funny state. We ask to move the highlight to the whole div regardless of what the recording mode is.
    // We have a bit of a chicken and egg problem here. The new recording mode still needs to be determined, and the audio-sentence markup is not applied yet either,
    // but it's easier to determine the recording mode and apply the audio-sentence markup if we move the current highlight first than vice-versa.
    // Calling resetCurrentAudioelement() should get us back into a 100% valid state.
    //
    // Returns true if the current text box was moved, false otherwise.
    private async moveCurrentTextboxIfNeededAsync(): Promise<boolean> {
        const currentTextBox = this.getCurrentTextBox();
        let selectedTextBox: HTMLElement | null;
        try {
            selectedTextBox = await this.getWhichTextBoxShouldReceiveHighlightAsync();
        } catch {
            // Don't bother moving it if there's any errors from awaiting it
            return false;
        }

        // Enhance: it would be nice/significantly more intuitive if this (or a stripped-down version that just moves the highlight/audio recording mode) could run when the mouse focus changes.
        if (selectedTextBox && currentTextBox != selectedTextBox) {
            this.setSoundAndHighlightAsync({
                newElement: selectedTextBox,
                // No need to scroll in this case... if it's changed becasue the user typed, something else (maybe CKEditor?) brings the text box into view.
                // And if it changed for some other reason, it's just some automatic setting of the current upon initialization.
                // We don't want to scroll in that case because may not be explicit user interaction
                shouldScrollToElement: false
            });

            // We need to redo initialization
            this.initializeAudioRecordingMode();
            return true;
        }

        return false;
    }

    // Asynchronously works out how to update the markup on the specified text box, but only if the operation is currently allowed.
    // Returns a function which will actually do the update (but should only be executed if no further edits happened in the meantime).
    private async tryGetUpdateMarkupForTextBoxActionAsync(
        textBox: HTMLElement
    ): Promise<() => void> {
        const recordable = new Recordable(textBox);

        const shouldUpdateMarkup = await recordable.shouldUpdateMarkupAsync();

        if (shouldUpdateMarkup) {
            const recordingMode = this.getRecordingModeOfTextBox(textBox);
            const playbackMode = this.getPlaybackMode(textBox);
            return this.getUpdateMarkupForTextBoxAction(
                textBox,
                recordingMode,
                playbackMode
            );
        } else {
            return () => {
                // Just keep the code from changing the splits.
                // No notification right now, which I think in many cases would be fine
                // But if you want to add some notification UI, it can go here.
            };
        }
    }

    // Called on initial setup and on toolbox updateMarkup(), including when a new page is created with Talking Book tab open
    // The 'playback' mode here is needed to distinguish how the markup elements are arranged
    // to store any current recording, as opposed to the recording mode which indicates how
    // we will make any new recording. The main case in which they are different is a textbox
    // recording that has been split in 4.5, which actually broke the recording up and
    // created audio-sentence elements with ids pointing to distinct recording files,
    // so that markup/playback were done as sentence mode, while a new recording would
    // be made at the textbox level.
    // Review: possibly 'audioMarkupMode' would be a better name than 'audioPlaybackMode'?
    private updateMarkupForTextBox(
        textBox: HTMLElement,
        audioRecordingMode: RecordingMode,
        audioPlaybackMode
    ): void {
        const updateAction = this.getUpdateMarkupForTextBoxAction(
            textBox,
            audioRecordingMode,
            audioPlaybackMode
        );
        updateAction();
    }
    private getUpdateMarkupForTextBoxAction(
        textBox: HTMLElement,
        audioRecordingMode: RecordingMode,
        audioPlaybackMode
    ): () => void {
        return this.getActionToMakeAudioSentenceElements(
            $(textBox),
            audioRecordingMode,
            audioPlaybackMode
        );
    }

    private async resetCurrentAudioElementAsync(
        currentTextBox?: HTMLElement | null
    ): Promise<void> {
        if (currentTextBox === undefined) {
            currentTextBox = this.getCurrentTextBox();
        }

        if (!currentTextBox) {
            currentTextBox = await this.setCurrentAudioElementToDefaultAsync();
        } else {
            await this.setCurrentAudioElementBasedOnRecordingModeAsync(
                currentTextBox
            );
        }

        // The below section of code is believed to be no longer necessary
        // * By the end of version 4.5 it doesn't seem necessary anymore but hard to say for sure because it only triggers non-deterministically
        // * Version 4.9 development: still wasn't observed to be necessary.
        // * Another version 4.9 test: 60 times in a row with no problem and no flash without this code.
        // TODO: Maybe we can try deleting the commented-out code in Version 4.10

        // Note: Marking up the Current Element needs to happen after CKEditor's onload() fully finishes.  (onload sets the HTML of the bloom-editable to its original value, so it can wipe out any changes made to the original value).
        //   There is a race condition as to which one finishes first.  We need to  finish AFTER Ckeditor's onload()
        //   Strange because... supposedly this gets called through:
        //   applyToolboxStateToUpdatedPage() -> doWhenPageReady() -> doWhenCkEditorReady() -> ... setupForRecording() -> updateMarkupAndControlsToCurrentText()
        //   That means that this is some code which EXPECTS the CkEditor to be fully loaded, but somehow onload() is still getting called afterward. needs more investigation.
        //     I suspect it might be a 2nd call to onload(). In some cases with high delays, you can observe that the toolbox is waiting for something (probaby CKEditor) to finish before it loads itself.
        //
        // Enhance (long-term): Why is onload() still called after doWhenCkEditorReady()?  Does updating the markup trigger an additional onload()?
        // In the short-term, to deal with that, we just call the function several times at various delays to try to get it right.
        //
        // Estimated failure rates:
        //   Synchronous (no timeout): 10/31 failure rate
        //   Nested timeouts (20, 20): I estimated 2/13 fail rate
        //   Nested timeouts (20, 100): 3/30 failure rate.  (Note: ideally we want at least 10 failures before we can semi-accurately estimate the probability)
        //   Parallel timeouts (20, 100, 500): 0/30 failure rate.  Sometimes (probably 30%) single on-off-on flash of the highlight.
        //   Parallel timeouts (20, exponential back-offs starting from 100): 0/30 failure rate. Flash still problematic.

        // We don't want this stuff to run in unit tests, because it adds async behavior onto this function
        // and messes up any asynchronous test code.
        // if (!(window as any).__karma__) {
        //     let delayInMilliseconds = 20;
        //     while (delayInMilliseconds < 1000) {
        //         // Keep setting the current highlight for an additional roughly 1 second
        //         setTimeout(() => {
        //             if (currentTextBox) {
        //                 this.setCurrentAudioElementBasedOnRecordingMode(
        //                     currentTextBox
        //                 );
        //             }
        //         }, delayInMilliseconds);

        //         delayInMilliseconds *= 2;
        //     }
        // }
    }

    public async setCurrentAudioElementBasedOnRecordingModeAsync(
        element: Element,
        isEarlyAbortEnabled: boolean = false
    ): Promise<void> {
        if (isEarlyAbortEnabled && !this.isShowing) {
            // e.g., the tool was closed during the timeout interval. We must not apply any markup
            return;
        }

        if (this.recordingMode == RecordingMode.Sentence) {
            return this.setCurrentAudioElementToFirstAudioSentenceWithinElementAsync(
                element,
                isEarlyAbortEnabled
            );
        }
        console.assert(this.recordingMode == RecordingMode.TextBox);

        const pageDocBody = this.getPageDocBody();
        if (!pageDocBody) {
            return;
        }
        const audioCurrentList = pageDocBody.getElementsByClassName(
            kAudioCurrent
        );

        if (isEarlyAbortEnabled && audioCurrentList.length >= 1) {
            // audioCurrent highlight is already working, so don't bother trying to fix anything up.
            // I think this probably can also help if you rapidly check and uncheck the checkbox, then click Next.
            // We wouldn't want multiple things highlighted, or end up pointing to the wrong thing, etc.
            return;
        }
        let audioCurrent: Element | null = null;
        if (audioCurrentList.length >= 1) {
            audioCurrent = audioCurrentList.item(0);
        }
        const changeTo = this.getTextBoxOfElement(element);
        if (changeTo) {
            return this.setSoundAndHighlightAsync({
                newElement: changeTo,
                // Don't automatically scroll because tool is possibly being initialized (we only want it to scroll on explicit user interaction like Next/Prev)
                shouldScrollToElement: false,
                oldElement: audioCurrent
            });
        }
    }

    public async setCurrentAudioElementToFirstAudioSentenceWithinElementAsync(
        element: Element,
        isEarlyAbortEnabled: boolean = false
    ) {
        if (isEarlyAbortEnabled && !this.isShowing) {
            // e.g., the tool was closed during the timeout interval. We must not apply any markup
            return;
        }

        const audioCurrentList = this.getPageDocBodyJQuery().find(
            kAudioCurrentClassSelector
        );

        if (isEarlyAbortEnabled && audioCurrentList.length >= 1) {
            // audioCurrent highlight is already working, so don't bother trying to fix anything up.
            // I think this probably can also help if you rapidly check and uncheck the checkbox, then click Next.
            // We wouldn't want multiple things highlighted, or end up pointing to the wrong thing, etc.
            return;
        }

        let changeTo: Element | null;
        if (element.classList.contains(kAudioSentence)) {
            // The element itself is already an audio-sentence. Easy, just use itself.
            changeTo = element;
        } else {
            const sentencesWithinCurrentElement = element.getElementsByClassName(
                kAudioSentence
            );
            if (sentencesWithinCurrentElement.length > 0) {
                changeTo = sentencesWithinCurrentElement.item(0);
            } else {
                // Confused, not supposed to be here, just try to set it to the first audio-sentence in any text box as a last resort
                const firstSentence = this.getPageDocBodyJQuery()
                    .find(kAudioSentenceClassSelector)
                    .first();
                if (firstSentence.length === 0) {
                    // no recordable sentence found.
                    return;
                }

                changeTo = firstSentence.get(0);
            }
        }

        if (changeTo) {
            await this.setSoundAndHighlightAsync({
                newElement: changeTo,
                // Don't automatically scroll because tool is possibly being initialized (we only want it to scroll on explicit user interaction like Next/Prev)
                shouldScrollToElement: false
            });
        }
    }

    public async setCurrentAudioElementToDefaultAsync(): Promise<HTMLElement | null> {
        // Seems very strange that we would be doing this when the talking book tool is not active.
        // Unfortunately there's a weird sequence of events that happens when we add a page that calls
        // for a particular tool (e.g., Game) and the toolbox isn't open. In the process of opening
        // the toolbox, we call newPageReady() for the (default) talking book tool. Various things
        // get initiated but not awaited. Then we finish switching to the desired tool. Eventually,
        // one of the async tasks completes and this gets called. On various paths it tries to
        // select something, but setSoundAndHighlightAsync doesn't do it because the tool isn't active.
        // Then there's a loop in changeStateAndSetExpectedAsync that keeps trying to get something
        // selected...we can get into a recursive await/call stack that freezes Bloom.
        // This is one of several places where we give up trying to select something if the tool
        // isn't active to prevent such problems.
        if (!this.isTalkingBookToolActive()) return null;
        const pageDocBody = this.getPageDocBody();
        if (!pageDocBody) {
            return null;
        }
        const activeCanvasElement = getCanvasElementManager()?.getActiveElement();
        if (activeCanvasElement) {
            await this.moveRecordingHighlightToElement(activeCanvasElement);
            // That may or may not make a highlight. In either case, given that there's an active canvas element,
            //  we don't want to highlight anything else.
            return null;
        }

        // Find the relevant audioSentences (already sorted)
        const firstSentenceArray = this.getAudioElements();
        if (firstSentenceArray.length === 0) {
            // no recordable sentence found.
            return null;
        }

        const firstSentence = firstSentenceArray[0];

        // If in Hard Split mode, should actually set it to the Text Box, not the Sentence.
        let nextHighlight: Element = firstSentence;
        const textBoxOfFirst = this.getTextBoxOfElement(firstSentence);
        if (textBoxOfFirst) {
            const textBoxRecordingMode = this.getRecordingModeOfTextBox(
                textBoxOfFirst
            );
            if (textBoxRecordingMode === RecordingMode.TextBox) {
                nextHighlight = textBoxOfFirst;
            }
        }
        await this.setSoundAndHighlightAsync({
            newElement: nextHighlight,
            // Don't automatically scroll because tool is possibly being initialized (we only want it to scroll on explicit user interaction like Next/Prev)
            shouldScrollToElement: false
        });

        // In Sentence/Sentence mode: OK to move.
        // Text/Sentence mode: Ok to swap.
        // In Text/Text mode: Not OK to swap because we don't support Sentence/Text mode.
        // This possibly moved the highlight to a different text box, so we need to re-compute settings.
        // if (
        //     this.recordingMode == RecordingMode.TextBox &&
        //     this.getCurrentPlaybackMode() == RecordingMode.TextBox &&
        //     AudioRecording.this.haveAudio
        // ) {
        //     this.disableRecordingModeControl();
        // } else {
        //     this.enableRecordingModeControl();
        // }

        return firstSentence;
    }

    // This gets invoked via websocket message. It draws a series of bars
    // (reminiscent of leds in a hardware level meter) within the canvas in the
    //  top right of the bubble to indicate the current peak level.
    public setStaticPeakLevel(level: string): void {
        if (!this.levelCanvas) return; // just in case C# calls this unexpectedly
        const ctx = this.levelCanvas.getContext("2d");
        if (!ctx) return;
        // Erase the whole canvas
        const height = 15;
        const width = 80;

        ctx.fillStyle = window.getComputedStyle(
            this.levelCanvas.parentElement!
        ).backgroundColor!;

        ctx.fillRect(0, 0, width, height);

        // Draw the appropriate number and color of bars
        const gap = 2;
        const barWidth = 4;
        const interval = gap + barWidth;
        const bars = Math.floor(width / interval);
        const loudBars = 2;
        const quietBars = 2;
        const mediumBars = Math.max(bars - (loudBars + quietBars), 1);
        const showBars = Math.floor(bars * parseFloat(level)); // + 1;
        ctx.fillStyle = "#D2D2D2"; // should match text color or "#00FF00";
        for (let i = 0; i < showBars; i++) {
            const left = interval * i;
            if (i >= quietBars) ctx.fillStyle = "#0C8597";
            if (i >= quietBars + mediumBars) ctx.fillStyle = "#FF0000"; //red
            ctx.fillRect(left, 0, barWidth, height);
        }
    }

    public static getChecksum(message: string): string {
        // Vertical line character ("|") acts as a phrase delimiter in Talking Books.
        // To perform phrase-level recording, the user can insert a temporary "|" character where he wants a phrase split to happen.
        // This is now recognized in the list of sentence delimiters, so it will be broken up as an audio-sentence.
        // Then the user records the audio.
        // Then the user deletes the vertical line characters.
        // Now the text should be the desired final state, and audio recordings are possible at a sub-sentence level.
        // However, we don't want the sentence markup to be updated because the checksums differ (since a character was deleted).
        //
        // Thus, our checksum function needs to ignore the vertical line character when computing the checksum.
        const adjustedMessage = message.replace("|", "");
        return getMd5(adjustedMessage);
    }

    // Currently only used in testing, this just calls getActionToMakeAudioSentenceElements
    // and then executes the action.
    public makeAudioSentenceElementsTest(
        rootElementList: JQuery,
        audioRecordingMode: RecordingMode,
        audioPlaybackMode: RecordingMode = this.recordingMode
    ): void {
        const doUpdate = this.getActionToMakeAudioSentenceElements(
            rootElementList,
            audioRecordingMode,
            audioPlaybackMode
        );
        doUpdate();
    }

    // AudioRecordingMode=Sentence: We want to make out of each sentence in root a span which has a unique ID.
    // AudioRecordingMode=TextBox: We want to turn the bloom-editable text box into the unit which will be
    // recorded. (It needs a unique ID too). No spans will be created though.
    // If the text is already so marked up, we want to keep the existing ids
    // AND the recordingID checksum attribute (if any) that indicates what
    // version of the text was last recorded.
    // makeAudioSentenceElementsLeaf does this for roots which don't have children (except a few
    // special cases); this root method scans down and does it for each such child
    // in a root (possibly the root itself, if it has no children).
    // To facilitate dynamic updates during typing, this doesn't usually change the elements itself.
    // Rather, it returns a function which, if no further keystrokes have been received or we
    // are not responding to typing, should be executed immediately to make the change.
    // A few kinds of change that don't happen during typing are made immediately.
    public getActionToMakeAudioSentenceElements(
        rootElementList: JQuery,
        audioRecordingMode: RecordingMode,
        audioPlaybackMode: RecordingMode = this.recordingMode
    ): () => void {
        // Preconditions:
        //   bloom-editable ids are not currently used / will be robustly handled in the future / are agnostic to the specific value and format
        //   The first node(s) underneath a bloom-editable should always be <p> elements
        console.assert(
            audioPlaybackMode != RecordingMode.Unknown,
            "updateMarkupForTextBox() should not be passed mode unknown"
        );

        // Each recursive call and each call to the "leaf" function returns a function that needs to be called...
        // if all the updates are not out of date. We accumulate them here; then our final result will be
        // a function that executes all of them.
        let updateFuncs: Array<() => void> = [];
        const result = () => updateFuncs.forEach(x => x());

        let needAnotherTry = true; // in pathological situations we need to start over. Usually only true for one iteration.
        while (needAnotherTry) {
            needAnotherTry = false;
            updateFuncs = [];

            rootElementList.each((index: number, root: Element) => {
                // These mode change operations are allowed to change the DOM, as they don't happen during typing.
                if (audioPlaybackMode == RecordingMode.Sentence) {
                    if (this.isRootRecordableDiv(root)) {
                        this.persistRecordingMode(root, audioRecordingMode);

                        // Cleanup markup from AudioRecordingMode=TextBox
                        root.classList.remove(kAudioSentence);
                    }
                } else if (audioPlaybackMode == RecordingMode.TextBox) {
                    // Note: Includes cases where you are in Soft Split mode. (You will return without running makeAudioSentenceElementsLeaf()
                    if (this.isRootRecordableDiv(root)) {
                        // Save the RECORDING (not the playback) setting  used, so we can load it properly later
                        this.persistRecordingMode(root, audioRecordingMode);

                        // Cleanup markup from AudioRecordingMode=Sentence
                        $(root)
                            .find(kAudioSentenceClassSelector)
                            .each((index, element) => {
                                if (
                                    !element.classList.contains(
                                        "bloom-editable"
                                    )
                                ) {
                                    this.deleteElementAndPushChildNodesIntoParent(
                                        element
                                    );
                                }
                            });

                        // Add the markup for AudioRecordingMode = TextBox
                        root.classList.add(kAudioSentence);
                        if (
                            root.id == undefined ||
                            root.id == null ||
                            root.id == ""
                        ) {
                            root.id = AudioRecording.createValidXhtmlUniqueId();
                        }

                        // All done, no need to process any of the remaining children
                        return true; //(but continue the each loop in case there are more roots)
                    } else if (
                        $(root).find(kBloomEditableTextBoxSelector).length <= 0
                    ) {
                        // Trust that it got setup correctly, which if so means there is nothing to do for any children
                        return true; // but continue the each loop
                    }
                    // Else: Need to continue recursively making our way down the tree until we find that textbox that we can see is somewhere down there
                }

                const children = $(root).children();
                let processedChild: boolean = false; // Did we find a significant child?

                for (let i = 0; i < children.length; i++) {
                    const child: HTMLElement = children[i];

                    if (
                        $(child).is(kAudioSentenceClassSelector) &&
                        $(child).find(kAudioSentenceClassSelector).length > 0
                    ) {
                        // child is spurious; an extra layer of wrapper around other audio spans.
                        // (Normally, this method is not allowed to change the DOM except in the action
                        // function it returns. However, this is fixing a pathological legacy situation.
                        // If it occurs at all, it should get fixed when the page is first loaded,
                        // and not happen during typing.)
                        $(child).replaceWith($(child).html()); // clean up.
                        needAnotherTry = true; // start the whole method over.
                        return false; // break the 'each' loop
                    }

                    // Recursively process non-leaf nodes
                    const name = child.nodeName.toLowerCase();
                    // Review: is there a better way to pick out the elements that can occur within content elements?
                    if (
                        name != "span" &&
                        name != "br" &&
                        name != "i" &&
                        name != "b" &&
                        name != "strong" && // ckeditor uses this for bold
                        name != "em" && // ckeditor italics
                        name != "u" && // ckeditor underline
                        name != "sup" && // ckeditor superscript
                        name != "a" && // Allow users to manually insert hyperlinks 4.5, and support 4.6 hyperlinks
                        $(child).attr("id") !== "formatButton"
                    ) {
                        processedChild = true;
                        updateFuncs.push(
                            this.getActionToMakeAudioSentenceElements(
                                $(child),
                                audioRecordingMode,
                                audioPlaybackMode
                            )
                        );
                    }
                }

                if (!processedChild) {
                    // root is a leaf; process its actual content
                    updateFuncs.push(
                        this.getActionToMakeAudioSentenceElementsLeaf($(root))
                    );
                }
                return true; // continue the 'each' loop
            });
        }
        // Review: is there a need to handle elements that contain both sentence text AND child elements with their own text?
        return result;
    }

    // Currently only used in testing.
    public makeAudioSentenceElementsLeafTest(elt: JQuery): void {
        const doUpdate = this.getActionToMakeAudioSentenceElementsLeaf(elt);
        doUpdate();
    }

    // The goal for existing markup is that if any existing audio-sentence span has an md5 that matches the content of a
    // current sentence, we want to preserve the association between that content and ID (and possibly recording).
    // Where there aren't exact matches, but there are existing audio-sentence spans, we keep the ids as far as possible,
    // just using the original order, since it is possible we have a match and only spelling or punctuation changed.
    // We also attempt to use any sentence IDs specified by this.sentenceToIdListMap.
    // N.B. If Bloom comes in here with spans that have no audio-sentence class, we may end up wrapping spans in spans.
    // public to allow unit testing.
    // Returns an action to actually make the change, if it is not obsolete by then.
    public getActionToMakeAudioSentenceElementsLeaf(elt: JQuery): () => void {
        const copy = elt.clone(); // don't modify elt except in the function we return
        // When all text is deleted, we get in a temporary state with no paragraph elements, so the root editable div
        // may be processed...and if this happens during editing the format button may be present. The body of this function
        // will do weird things with it (wrap it in a sentence span, for example) so the easiest thing is to remove
        // it at the start and reinstate it at the end. Fortunately its position is predictable. But I wish this
        // otherwise fairly generic code didn't have to know about it.
        const formatButton = copy.find("#formatButton");
        formatButton.remove(); // nothing happens if not found
        const currentMarker = copy.find(".bloom-ui-current-audio-marker");
        currentMarker.remove();

        this.cleanUpCkEditorHtml(elt.get(0), copy.get(0));

        const markedSentences = copy.find(
            `${kAudioSentenceClassSelector},.${kSegmentClass}`
        );
        //  TODO: Shouldn't re-use audio if the text box has a different lang associated. "Jesus" pronounced differently in differently langs.
        const reuse: any[] = []; // an array of id/md5 pairs for any existing sentences marked up for audio in the element.
        // If caller has manually specified a custom ID list, then let's say (for now) that we won't allow IDs to be re-used
        markedSentences.each(function(index) {
            reuse.push({
                id: $(this).attr("id"),
                md5: $(this).attr("recordingmd5")
            });
            $(this).replaceWith($(this).html()); // strip out the audio-sentence wrapper so we can re-partition.
        });

        const htmlFragments: TextFragment[] = this.stringToSentences(
            copy.html()
        );
        let textFragments: TextFragment[] | null = this.stringToSentences(
            elt.text()
        );

        // Try to use textFragments for hopefully more reliable comparison purposes compared to htmlFragments. See if we can easily align the two.
        if (htmlFragments.length != textFragments.length) {
            // Hmm, doesn't align perfectly :(

            if (
                htmlFragments.length == textFragments.length + 1 &&
                htmlFragments[htmlFragments.length - 1].text.indexOf("<br") >= 0
            ) {
                // Well, it's only off by a newline at the end. It's probably fine.
            } else {
                // The two can't be easily aligned, so mark textFragments as invalid.
                textFragments = null;
            }
        }

        // If any new sentence has an md5 that matches a saved one, attach that id/md5 pair to that fragment.
        for (let i = 0; i < htmlFragments.length; i++) {
            const fragment = htmlFragments[i];
            (<any>fragment).matchingAudioSpan = null; // remove obsolete audio info from possibly cached value (BL-9221)
            if (this.isRecordable(fragment)) {
                const currentMd5 = AudioRecording.getChecksum(fragment.text);
                for (let j = 0; j < reuse.length; j++) {
                    if (currentMd5 === reuse[j].md5) {
                        // It's convenient here (very locally) to add a field to fragment which is not part
                        // of its spec in theOneLibSynphony.
                        (<any>fragment).matchingAudioSpan = reuse[j];
                        reuse.splice(j, 1); // don't reuse again
                        break;
                    }
                }
            }
        }

        // Assemble the new HTML, reusing old IDs where possible and generating new ones where needed.
        let newHtml = "";
        for (let i = 0; i < htmlFragments.length; i++) {
            const htmlFragment = htmlFragments[i];

            if (!this.isRecordable(htmlFragment)) {
                // this is inter-sentence space (or white space before first sentence).
                newHtml += htmlFragment.text;
            } else {
                let newId: string | null = null;
                let newMd5: string = "";
                let reuseThis = (<any>htmlFragment).matchingAudioSpan;
                if (!reuseThis && reuse.length > 0) {
                    reuseThis = reuse[0]; // use first if none matches (preserves order at least)
                    reuse.splice(0, 1);
                }
                if (reuseThis) {
                    // SOMETHING remains we can reuse
                    newId = reuseThis.id;
                    newMd5 = ' recordingmd5="' + reuseThis.md5 + '"';
                }
                if (!newId) {
                    let text: string;
                    if (textFragments && i < textFragments.length) {
                        text = textFragments[i].text;
                    } else {
                        text = htmlFragments[i].text;
                    }

                    // Enhance: This ID mapping is so brittle, it really needs a more reliable mechanism than text-matching in the face of CKEditor modifications.
                    const normalizedText = AudioRecording.normalizeText(text);
                    if (normalizedText in this.sentenceToIdListMap) {
                        const idList = this.sentenceToIdListMap[normalizedText];

                        if (idList.length >= 1) {
                            newId = idList[0];

                            // We're done processing this id, so get rid of it.
                            // This allows us to use the next ID if there are multiple sentences with the same fragment text.
                            idList.shift();
                        }
                    }

                    if (!newId) {
                        newId = AudioRecording.createValidXhtmlUniqueId();
                    }
                }

                newHtml +=
                    `<span id="${newId}" class="${kAudioSentence}" ${newMd5}>` +
                    htmlFragment.text +
                    "</span>";
            }
        }

        return () => {
            // set the html (if this function gets called, that is, if there hasn't already been another keystroke)
            elt.html(newHtml);
            elt.append(formatButton);
        };
    }

    // When we switched to webview2, we started getting errant zero-width spaces in the text from ckeditor.
    // The "right" way to get text from ckeditor boxes is to call getData(). So that's what we're doing here.
    // It does clean up at least most of the zero-width spaces.
    // See BL-12391.
    // Note, there is similar logic in EditableDivUtils.doCkEditorCleanup().
    // But, unfortunately, the complication here of element vs copy means we can't easily share the code.
    private cleanUpCkEditorHtml(element: HTMLElement, copy: HTMLElement) {
        const editableDiv = element.closest(".bloom-editable") as HTMLElement;
        if (!editableDiv) return;

        const ckeditorOfThisBox = (<any>editableDiv).bloomCkEditor;
        if (!ckeditorOfThisBox) return;

        if (editableDiv.innerHTML !== ckeditorOfThisBox.getData()) {
            // Flag the element we are processing so we can find it in the version we make from ckeditor's getData().
            element.setAttribute("data-element-we-are-processing", "this-one");

            // Create a dummy element just so we can stash the result of getData() and then find our element in it.
            // getData() is for the whole text box, but we are processing one of the child elements.
            const newElement = document.createElement("div");
            // have to call getData() again so it contains data-element-we-are-processing
            EditableDivUtils.safelyReplaceContentWithCkEditorData(
                newElement,
                ckeditorOfThisBox.getData()
            );
            const newChild = newElement.querySelector(
                "[data-element-we-are-processing]"
            );
            copy.innerHTML = newChild!.innerHTML;

            // Make sure we remove the flag; we don't want to modify the original element.
            element.removeAttribute("data-element-we-are-processing");
        }
    }

    // Normalization rules for text which has already been processed by CKEditor
    // Note that the text seems to have 3 variations when running AutoSegment, which may represent extraneous whitespace differently:
    // 1) Raw Form - Directly after typing text, immediately upon clicking AutoSegment, before anything is modified
    // 2) Processing Form - After clicking AutoSegment and the response is being proc, during MakeAudioSentenceElementsLeaf
    // 3) Saved Form - After saving the page.
    // We want all 3 forms to be able to map to the same string after normalizing.
    public static normalizeText(text: string): string {
        if (!text) {
            return text;
        }

        text = text.replace(/\r/g, "").replace(/\n/g, ""); // Raw form may inject extraneous newlines upon inserting punctuation like '('
        text = text.replace(/<br \/>/g, ""); // Processing form will contain <br />.
        text = text.replace(/&nbsp;/g, String.fromCharCode(160)); // Saved form will store multiple spaces as Unicode decimal 160 = non-breaking space
        text = text.replace(/ {2}/g, " "); // Handle consecutive spaces

        return text;
    }

    public static createValidXhtmlUniqueId(): string {
        let newId = EditableDivUtils.createUuid();
        if (/^\d/.test(newId)) newId = "i" + newId; // valid ID in XHTML can't start with digit

        return newId;
    }

    private deleteElementAndPushChildNodesIntoParent(element) {
        if (element == null) {
            return;
        }

        const parent = element.parentElement;

        const childNodesCopy = Array.from(element.childNodes); // Create a copy because e.childNodes is getting modified as we go
        for (let i = 0; i < childNodesCopy.length; ++i) {
            parent.insertBefore(childNodesCopy[i], element);
        }
        element.remove();
    }

    private isRootRecordableDiv(element: Element): boolean {
        if (element == null) {
            return false;
        }

        return $(element).is(kBloomEditableTextBoxSelector);
    }

    private isRecordable(fragment: TextFragment): boolean {
        if (fragment.isSpace) return false; // this seems to be reliable
        // initial white-space fragments may currently be marked sentence
        let test = fragment.text.replace(/<br *[^>]*\/?>/g, " ");
        // and some may contain only nbsp
        test = test.replace("&nbsp;", " ");
        if (this.isWhiteSpace(test)) return false;
        return this.isTextOrHtmlWithText(test);
    }

    private isTextOrHtmlWithText(textOrHtml: string): boolean {
        const parser = new DOMParser();
        const doc = parser.parseFromString(textOrHtml, "text/html");
        if (doc && doc.documentElement) {
            //paranoia
            // on error, parseFromString returns a document with a parseerror element
            // rather than throwing an exception, so check for that
            if (doc.getElementsByTagName("parsererror").length > 0) {
                return false;
            }
            // textContent is the aggregation of the text nodes of all children
            const content = doc.documentElement.textContent;
            return !this.isWhiteSpace(content ? content : "");
        }
        return false;
    }

    private isWhiteSpace(test: string): boolean {
        if (test.match(/^\s*$/)) return true;
        return false;
    }

    // ------------ State Machine ----------------

    // Note: button states may not change immediately. If you call it rapidly in succession with different values, you may not see valid results.
    public async changeStateAndSetExpectedAsync(
        expectedVerb: string,
        numRetriesRemaining: number = 1
    ): Promise<void> {
        // console.log("changeState(" + expectedVerb + ")");

        // Call with "" verb if there's nothing specific to highlight, just need to check if these controls should be disabled.
        // (e.g. when we have found that the current page has no divs with recording content, and we may possible want to disable
        // the audio recording controls.)
        if (expectedVerb == "") {
            this.disableInteraction();
            return;
        }

        // Note: It's best not to modify the Enabled/Disabled state more than once if possible.
        //       It is subtle but it is possible to notice the flash of an element going from
        //       enabled -> disabled -> enabled. (And it is extremely noticeable if this function gets
        //       called several times in quick succession.)
        // Enhance: Consider whether it'd be a good idea to disable click events on these buttons,
        //       but still leave the buttons in their previous visual state.
        //       In theory, there can be a small delay between when we are supposed to change state
        //       (right now) and when we actually determine the correct state (after a callback).

        // Finding no audioCurrent is only unexpected if there are non-zero number of audio elements
        if (
            this.getPageDocBodyJQuery().find(kAudioCurrentClassSelector)
                .length === 0 &&
            this.containsAnyAudioElements()
        ) {
            // We have reached an unexpected state :(
            // (It can potentially happen if changes applied to the markup get wiped out and
            // overwritten e.g. by CkEditor Onload())
            if (numRetriesRemaining > 0) {
                // It's best not to leave everything disabled.
                // The user will be kinda stuck without any navigation.
                // Attempt to set the markup to the first element
                // Practically speaking, it's most likely to get into this erroneous state when
                // loading which will be on the first element. Even if the first element is
                // "wrong"... the alternative is it points to nothing and you are stuck.
                // IMO pointing to the first element is less wrong than disabling the whole toolbox.
                await this.setCurrentAudioElementToDefaultAsync();
                return this.changeStateAndSetExpectedAsync(
                    expectedVerb,
                    numRetriesRemaining - 1
                );
            } else {
                // We have reached an error state and attempts to self-correct it haven't
                // succeeded either. :(
                this.disableInteraction();
                return;
            }
        }

        return this.updateButtonStateAsync(expectedVerb);
    }

    private async updateButtonStateAsync(expectedVerb: string): Promise<void> {
        this.setEnabledOrExpecting("record", expectedVerb);

        const promisesToAwait: Promise<void>[] = [];
        promisesToAwait.push(this.updateListenButtonStateAsync());

        // Now work on all the things that depend or sometimes depend on availability of audio within the text box.
        const currentTextBoxIds: string[] = [];
        const audioSentenceCollection = this.getAudioSegmentsInCurrentTextBox();
        for (let i = 0; i < audioSentenceCollection.length; ++i) {
            const audioSentenceElement = audioSentenceCollection[i];
            if (audioSentenceElement) {
                const id = audioSentenceElement.getAttribute("id");
                if (id) {
                    currentTextBoxIds.push(id);
                }
            }
        }

        const doWhenTextBoxAudioAvailabilityKnownCallback = async (
            textBoxResponse: AxiosResponse<any>
        ) => {
            // Look up the subsequent async call that we (possibly) need to retrieve all the information we need to complete processing
            let idsToCheck: string;
            if (this.recordingMode == RecordingMode.Sentence) {
                const currentElementIds = this.getSegmentIdsWithinCurrent();
                idsToCheck = currentElementIds.toString();
            } else {
                console.assert(this.recordingMode == RecordingMode.TextBox);

                const currentTextBox = this.getCurrentTextBox();
                if (currentTextBox) {
                    idsToCheck = currentTextBox.id;
                } else {
                    idsToCheck = "";
                }
            }

            try {
                const elementResponse: AxiosResponse<any> = await axios.get(
                    `/bloom/api/audio/checkForAnyRecording?ids=${idsToCheck}`
                );

                this.updateButtonStateHelper(
                    expectedVerb,
                    textBoxResponse,
                    elementResponse
                );
            } catch (elementError) {
                // Note: If there is no audio, it returns Request.Failed AKA it actually goes into the catch!!!
                this.updateButtonStateHelper(
                    expectedVerb,
                    textBoxResponse,
                    elementError.response
                );
            }
        };

        try {
            const textBoxResponse: AxiosResponse<any> = await axios.get(
                `/bloom/api/audio/checkForAnyRecording?ids=${currentTextBoxIds.toString()}`
            );

            // Now find if any audio exists for the current recording element.
            promisesToAwait.push(
                doWhenTextBoxAudioAvailabilityKnownCallback(textBoxResponse)
            );
        } catch (textBoxError) {
            // Note: If there is no audio, it returns Request.Failed AKA it actually goes into the catch!!!
            promisesToAwait.push(
                doWhenTextBoxAudioAvailabilityKnownCallback(
                    textBoxError.response
                )
            );
        }

        await Promise.all(promisesToAwait);
    }

    public static async audioExistsForIdsAsync(
        ids: string[]
    ): Promise<boolean> {
        try {
            const response: AxiosResponse<any> = await axios.get(
                `/bloom/api/audio/checkForAnyRecording?ids=${ids}`
            );
            return this.DoesNarrationExist(response);
        } catch {
            return false;
        }
    }

    // Given a response (from "/bloom/api/audio/checkForAnyRecording?ids=..."), determines whether the response indicates that narration audio exists for any of the specified IDs
    private static DoesNarrationExist(response: AxiosResponse<any>): boolean {
        // Note regarding Non-OK status codes: If there is no audio, it returns Request.Failed AKA it actually has Non-OK Status code!
        //       This doesn't mean you need to log an error though, since it is "normal" for failed requests to return.
        //       Just mark them as not-exist instead.

        return (
            response && response.status === 200 && response.statusText === "OK"
        );
    }

    private updateButtonStateHelper(
        expectedVerb: string, // e.g. "record", "play", "check", etc.
        textBoxResponse: AxiosResponse<any>,
        elementResponse: AxiosResponse<any>
    ): void {
        // This var is true if the Text Box containing the Currently Highlighted Element contains audio for any of the elements within the text box.
        const doesTextBoxAudioExist: boolean = AudioRecording.DoesNarrationExist(
            textBoxResponse
        );

        // This var is true if the Currently Highlighted Element contains audio
        // (If RecordingMode=TextBox but PlaybackMode=Sentence, this means if any of the sentences of the currently highlighted element contain audio)
        const doesElementAudioExist: boolean = AudioRecording.DoesNarrationExist(
            elementResponse
        );

        // Clear and Play (Check) buttons
        if (doesElementAudioExist) {
            this.setStatus("clear", Status.Enabled);
            this.setEnabledOrExpecting("play", expectedVerb);
            this.haveAudio = true;
        } else {
            this.setStatus("clear", Status.Disabled);
            this.setStatus("play", Status.Disabled);
            this.haveAudio = false;
        }

        if (
            doesElementAudioExist &&
            this.recordingMode === RecordingMode.TextBox
        ) {
            this.setEnabledOrExpecting("split", expectedVerb);
        } else {
            this.setStatus("split", Status.Disabled);
        }

        // Next button
        if (this.getNextAudioElement()) {
            // Next exists. Set the Next button to at least Enabled, if not Expected.

            const shouldNextButtonOverrideSplit: boolean =
                expectedVerb === "split" &&
                this.getStatus("split") === Status.Disabled;
            if (!shouldNextButtonOverrideSplit) {
                // Normal case for Next
                this.setEnabledOrExpecting("next", expectedVerb);
            } else {
                // Alternatively, if expectedVerb was split but Split isn't enabled, then Next should become Expected in the place of Split
                this.setStatus("next", Status.Expected);
            }
        } else {
            this.setStatus("next", Status.Disabled);
        }

        // Prev (back) button
        if (this.getPreviousAudioElement()) {
            this.setStatus("prev", Status.Enabled);
        } else {
            this.setStatus("prev", Status.Disabled);
        }
    }

    private async updateListenButtonStateAsync(): Promise<void> {
        // Set listen button based on whether we have an audio at all for this page

        // First collect all the ids on this page.
        const ids: any[] = [];
        this.getAudioElements().forEach(element => {
            ids.push(element.id);
        });

        try {
            const response = await axios.get(
                "/bloom/api/audio/checkForAnyRecording?ids=" + ids
            );
            if (response.statusText === "OK") {
                this.setStatus("listen", Status.Enabled);
            } else {
                this.setStatus("listen", Status.Disabled);
            }
        } catch {
            // This handles the case where AudioRecording.HandleCheckForAnyRecording() (in C#)
            // sends back a request.Failed("no audio") and thereby avoids an uncaught js exception.
            this.setStatus("listen", Status.Disabled);
        }
    }

    // Returns the ids of each segment within current
    private getSegmentIdsWithinCurrent(): string[] {
        const currentHighlight = this.getCurrentHighlight();
        return this.getSegmentIdsWithinElement(currentHighlight);
    }

    // Returns the ids of each segment within the specified element
    private getSegmentIdsWithinElement(element: Element | null): string[] {
        const currentElementIds: string[] = [];

        if (element) {
            const segmentList = this.getAudioSegmentsWithinElement(element);
            for (let i = 0; i < segmentList.length; ++i) {
                const audioSentenceElement = segmentList[i];
                if (audioSentenceElement) {
                    const id = audioSentenceElement.getAttribute("id");
                    if (id) {
                        currentElementIds.push(id);
                    }
                }
            }
        }

        return currentElementIds;
    }

    public setEnabledOrExpecting(verb: string, expectedVerb: string) {
        if (expectedVerb === verb) this.setStatus(verb, Status.Expected);
        else this.setStatus(verb, Status.Enabled);
    }

    private isEnabledOrExpected(verb: string): boolean {
        return (
            $("#audio-" + verb).hasClass("enabled") ||
            $("#audio-" + verb).hasClass("expected")
        );
    }

    private getStatus(which: string): Status {
        const buttonElement = document.getElementById(`audio-${which}`);
        if (!buttonElement) {
            return Status.Disabled;
        }

        if (buttonElement.classList.contains("enabled")) {
            return Status.Enabled;
        } else if (buttonElement.classList.contains("expected")) {
            return Status.Expected;
        } else if (buttonElement.classList.contains("active")) {
            return Status.Active;
        } else {
            return Status.Disabled;
        }
    }

    private setStatus(which: string, to: Status): void {
        const buttonElement = document.getElementById(`audio-${which}`);
        if (buttonElement) {
            buttonElement.classList.remove("expected");
            buttonElement.classList.remove("disabled");
            buttonElement.classList.remove("disabledUnlessHover");
            buttonElement.classList.remove("enabled");
            buttonElement.classList.remove("active");

            // Convert names from PascalCase to camelCase.
            // The enum uses PascalCase, but the CSS uses camelCase
            const statusString: string = Status[to];
            const className: string = AudioRecording.ToCamelCaseFromPascalCase(
                statusString
            );
            buttonElement.classList.add(className);
        }

        const labelElement = document.getElementById(`audio-${which}-label`);
        if (labelElement) {
            if (to === Status.Expected) {
                labelElement.classList.add("expected");
            } else {
                labelElement.classList.remove("expected");
            }
        }

        // Also set expected on the list item, which provides the number e.g. "1)" or "2)" or "3)".
        // This provides the yellow highlight color on that part of the text too
        const listItemElement = document.getElementById(
            `audio-${which}-list-item`
        ); // Note: It is very much a normal case that this may return null for some inputs.
        if (listItemElement) {
            if (to === Status.Expected) {
                listItemElement.classList.add("expected");
            } else {
                listItemElement.classList.remove("expected");
            }
        }

        if (to === Status.Active) {
            // Doesn't make sense to expect something while something else is active.
            this.removeExpectedStatusFromAll();
            if (which === "play") {
                // We need a different label.
                var label = document.getElementById("audio-play-label")!;
                if (!this.originalPlayLabel) {
                    this.originalPlayLabel = label.innerText;
                }
                label.classList.add("hide-counter-still-count");
                theOneLocalizationManager
                    .asyncGetText("Common.Pause", "Pause", "")
                    .done(pause => {
                        label.innerText = pause;
                    });
            }
        } else {
            if (this.originalPlayLabel) {
                // we've been in the playing active state at some point, make sure we no longer are.
                // Note: we could clear originalPlayLabel here, which would save us executing this
                // block more than we really need to. However, there's a lot of async stuff
                // happening in this class. The very first time we hit play, we can be entirely
                // confident of capturing the original (localized) label. If we start clearing
                // the variable, I'm concerned that there may be some small chance that at some
                // point we will capture "Pause" and then we will be stuck there.
                var label = document.getElementById("audio-play-label")!;
                label.innerText = this.originalPlayLabel;
                label.classList.remove("hide-counter-still-count");
            }
        }
    }

    private originalPlayLabel: string;

    // Review: Where is the best place to put this function?
    public static ToCamelCaseFromPascalCase(text: string) {
        return text[0].toLowerCase() + text.slice(1);
    }

    public static showTalkingBookTool() {
        getToolboxBundleExports()
            ?.getTheOneToolbox()
            .activateToolFromId(kTalkingBookToolId);
    }

    private removeExpectedStatusFromAll(): void {
        const expectableButtonNames = ["record", "play", "split", "next"]; // only the buttons which have a possibility of being in Expected state.
        for (let i = 0; i < expectableButtonNames.length; ++i) {
            const buttonName = expectableButtonNames[i];
            const buttonElement = document.getElementById(
                `audio-${buttonName}`
            );
            if (buttonElement) {
                buttonElement.classList.remove("expected");
            }

            const labelElement = document.getElementById(
                `audio-${buttonName}-label`
            );
            if (labelElement) {
                labelElement.classList.remove("expected");
            }
        }
    }

    // Called when the user chooses "Use Aeneas to guess timings" (with no argument) or
    // "Apply Timings file" (with the file name).
    // This will return a new set of split timings (like we store in data-audioRecordingEndTimes),
    // or just possibly undefined if there was an error (already reported).
    // The basic steps are:
    // * Split the text into fragments (sentences)
    // * Call API server to split the whole audio file into pieces, one piece per sentence.
    //   (or, if we're passed a mnualTimingsPath, just get the times extracted from that)
    // *   (Black Box internals:  by using Aeneas to find the timing of each sentence start, then FFMPEG to split)
    // The data returned is used to update the Adjust Timings dialog, and makes its way back to the
    // document from there if OK is clicked.
    private split = async (
        manualTimingsPath?: string
    ): Promise<string | undefined> => {
        // First, check if there's even an audio recorded yet. (Not sure if we could ever get called in this
        // situation; I think the adjust timings dialog couldn't even be launched.)
        const playButtonElement = document.getElementById("audio-play");
        if (
            playButtonElement &&
            playButtonElement.classList.contains("disabled")
        ) {
            this.displaySplitError();
            this.setStatus("split", Status.Disabled); // Remove active/expected highlights
            return undefined;
        }
        this.showBusy();
        this.resetAudioIfPaused();

        const currentTextBox = this.getCurrentHighlight();
        if (!currentTextBox) {
            // At this point, not going to be able to get the ID of the div so we can't figure out how to get the filename...
            // So just give up.
            this.displaySplitError();
            this.setStatus("split", Status.Enabled); // Remove active/expected highlights
            this.endBusy();
            return undefined;
        }

        const fragmentIdTuples = this.extractFragmentsAndSetSpanIdsForAudioSegmentation();

        if (fragmentIdTuples.length > 0) {
            const inputParameters = {
                audioFilenameBase: currentTextBox.id,
                audioTextFragments: fragmentIdTuples,
                lang: this.getAutoSegmentLanguageCode(),
                manualTimingsPath
            };

            // this.setStatus("split", Status.Active);  // Now we decide to just keep it disabled instead.
            let result: void | AxiosResponse<any>;
            try {
                result = await postJsonAsync(
                    "audioSegmentation/getForcedAlignmentTimings", // Or can use "audioSegmentation/autoSegmentAudio" to create hard splits of the audio
                    JSON.stringify(inputParameters)
                );
            } catch (error) {
                // This always needs to happen regardless of what happens
                // Otherwise, classes like cursor-progress will not go away upon a C# exception.
                // It can even be persisted into the saved HTML file and re-loaded with the cursor-progress state still applied
                this.endBusy();
                this.updateDisplay();
                return undefined;
            }

            this.endBusy(); // This always needs to happen regardless of what path through processAutoSegmentResponse the code takes.
            if (result && result.data) {
                return this.processAutoSegmentResponse(result);
            }
        }
        return undefined;
    };

    public displaySplitError() {
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Toolbox.TalkingBookTool.SplitError",
                "Something went wrong while splitting the recording.",
                "This text appears if an error occurs while performing automatic splitting of audio (an audio file corresponding to an entire text box will be split into multiple sections, one section per sentence)."
            )
            .done(localizedMessage => {
                toastr.error(localizedMessage);
            });
    }

    // Finds the current text box, gets its text, split into sentences, then return each sentence with a UUID.
    public extractFragmentsAndSetSpanIdsForAudioSegmentation(): AudioTextFragment[] {
        const currentText = this.getCurrentText();

        const textFragments: TextFragment[] = this.stringToSentences(
            currentText
        );

        // Note: We will just create all new IDs for this. Which I think is reasonable.
        // If splitting the audio file, reusing audio recorded from by-sentence mode is probably less smooth.

        const fragmentObjects: AudioTextFragment[] = [];
        for (let i = 0; i < textFragments.length; ++i) {
            const fragment = textFragments[i];
            if (this.isRecordable(fragment)) {
                const newId = AudioRecording.createValidXhtmlUniqueId();

                // Sometimes extraneous newlines can be injected (by CKEditor?). They may get removed later (maybe after the CKEditor reloads when the text box's underlying HTML is modified???)
                // However, some processing needs the text immediately, and others are after the text is cleaned.
                // In order to reconcile the two, just normalize the text immediately.
                fragment.text = AudioRecording.normalizeText(fragment.text);

                let idList: string[] = [];
                if (fragment.text in this.sentenceToIdListMap) {
                    idList = this.sentenceToIdListMap[fragment.text];
                }
                idList.push(newId); // This is saved so MakeSentenceAudioElementsLeaf can recover it
                this.sentenceToIdListMap[fragment.text] = idList;

                // NOTE: sentenceToIdListMap above doesn't want the prettify-ing of this text.
                // It needs to match the normalizing algorithm that makeAudioSentenceElementsLeaf() uses.
                const textForAeneas = this.prepareTextForAeneas(fragment.text);

                fragmentObjects.push(
                    new AudioTextFragment(textForAeneas, newId)
                );
            }
        }

        return fragmentObjects;
    }

    // Beautify the fragments sent to Aeneas.
    // It was observed that punctuation can sometimes make a difference in the timing splits
    // Note: SOMETIMES, not always.
    // So, we remove the "|" delimiters (phrase delimiters)
    private prepareTextForAeneas(text: string): string {
        let textForAeneas = text;
        textForAeneas = textForAeneas.replace(/\|+$/, ""); // Delete trailing pipes (phrase delimiters)
        textForAeneas = textForAeneas.replace(/\s+$/, ""); // Delete trailing whitespace
        textForAeneas = textForAeneas.replace(/^\s+/, ""); // Delete leading whitespace

        return textForAeneas;
    }

    public stringToSentences(text: string): TextFragment[] {
        // Used to have a caching layer. Now we just always call it directly.
        // The cache was added to ensure that the same input returns the same ouput (at least for the same run),
        // but the underlying library has been pretty deterministic, so no real concern about different results there.
        //
        // The caching feature, if returned, should ideally only be live when you are running AutoSegment.
        return theOneLibSynphony.stringToSentences(text);
    }

    public getAutoSegmentLanguageCode(): string {
        const langCodeFromAutoSegmentSettings = ""; // TODO: IMPLEMENT ME after we convert to having the recording mode setting only apply to the current text box. It'll make this set of settings easier to figure out.
        let langCode = langCodeFromAutoSegmentSettings;
        if (!langCode) {
            const currentTextBox = this.getCurrentHighlight();
            if (currentTextBox) {
                const langAttributeValue = currentTextBox.getAttribute("lang");
                if (langAttributeValue) {
                    langCode = langAttributeValue;
                }
            }
        }

        // Remove the suffix for strings like "es-BRAI"  (Spanish - Brazil) or "zh-CN"
        // (This language code will be passed into eSpeak eventually, so it should be ones that eSpeak can work with)
        const countryCodeSeparatorIndex = langCode.indexOf("-");
        if (countryCodeSeparatorIndex >= 0) {
            langCode = langCode.substr(0, countryCodeSeparatorIndex);
        }

        return langCode;
    }

    private disableInteraction(): void {
        // We call this method in three cases (so far):
        // 1- When calling changeStateAndSetExpected() with expectedVerb = ""
        // 2- While doing auto segmenting
        // 3- An unrecoverable error has occurred
        this.setStatus("record", Status.Disabled);
        this.setStatus("play", Status.Disabled);
        this.setStatus("split", Status.Disabled);
        this.setStatus("next", Status.Disabled);
        this.setStatus("prev", Status.Disabled);
        this.setStatus("clear", Status.Disabled);
        this.setStatus("listen", Status.Disabled);
    }

    private showBusy(): void {
        const elementsToUpdate = this.getElementsToUpdateForCursor();

        for (let i = 0; i < elementsToUpdate.length; ++i) {
            const element = elementsToUpdate[i];
            if (element) {
                element.classList.add("cursor-progress");
            }
        }
        // One context where we want to show busy is the AdjustTimings dialog.
        // It's a dialog, so it's not in the normal flow of the page. Moreover, to give
        // it the greatest possible width, it's not even in this iframe. It doesn't even
        // have the stylesheet that knows what to do with .cursor-progress.
        // So I'm doing the same thing a different way. Maybe we should forget about
        // using CSS and modify them all directly like this?
        const rootDialogContainer = window.top?.document.getElementsByClassName(
            "MuiDialog-container"
        )[0] as HTMLElement;
        if (rootDialogContainer) {
            rootDialogContainer.style.cursor = "progress";
        }
    }

    private endBusy(): void {
        const elementsToUpdate = this.getElementsToUpdateForCursor();

        for (let i = 0; i < elementsToUpdate.length; ++i) {
            const element = elementsToUpdate[i];
            if (element) {
                element.classList.remove("cursor-progress");
            }
        }
        const rootDialogContainer = window.top?.document.getElementsByClassName(
            "MuiDialog-container"
        )[0] as HTMLElement;
        if (rootDialogContainer) {
            rootDialogContainer.style.cursor = "";
        }
    }

    // Break up the current text box into sentences marked with kSegmentClass.
    // This is the markup used when  recording in text box mode, but a recording has been
    // split somehow. However, we don't add the markup that causes it to be displayed
    // that way, because we don't yet have confirmed splits. Instead, we return a list
    // of proposed end times (which AdjustTimingsControl typically refines)
    public autoSegmentBasedOnTextLength(): number[] {
        const currentTextBox = this.getCurrentTextBox();
        if (!currentTextBox) return [];
        const segments = this.extractFragmentsAndSetSpanIdsForAudioSegmentation();
        // Generate crude estimate of audio lengths based on text lengths and total time,
        // which should be known if we have an audio recording to adjust at all.
        const durationText = currentTextBox.getAttribute("data-duration");
        const duration = durationText ? parseFloat(durationText) : 0;
        if (!duration) {
            // should never happen
            return [];
        }
        let textLength = 0;
        for (const segment of segments) {
            // ??1 here and below prevents any segment being completely empty.
            // (Though, I don't think extractFragmentsAndSetSpanIdsForAudioSegmentation
            // should actually make empty segments...belt and braces here.)
            textLength += segment.fragmentText?.length ?? 1;
        }
        const endTimes: number[] = [];
        let start = 0;
        for (const segment of segments) {
            const fragmentLength = segment.fragmentText?.length ?? 1;
            const end = start + (duration * fragmentLength) / textLength;
            endTimes.push(end);
            start = end;
        }

        // Don't do this until the user confirms the splits.
        // currentTextBox.setAttribute(
        //     kEndTimeAttributeName,
        //     endTimes.map(x => x.toString()).join(" ")
        // );

        // Temporarily switch to sentence, to get sentence elements created, using IDs generated
        // in extractFragmentsAndSetSpanIdsForAudioSegmentation
        this.updateMarkupForTextBox(
            currentTextBox,
            this.recordingMode,
            RecordingMode.Sentence
        );
        // And now back to text box mode, but we'll keep the sentences as auto-segmented fragments
        this.updatePlaybackModeToTextBox();
        // don't do this so it doesn't yet LOOK split.
        //this.markAudioSplit();
        return endTimes;
    }

    // If the result is success, we return the allEndTimesString, a list of end times,
    // such as we store in data-audioRecordingEndTimes
    public processAutoSegmentResponse(
        result: AxiosResponse<any>
    ): string | undefined {
        const isSuccess = result && result.data;

        if (isSuccess) {
            const autoSegmentResponse = result.data;
            return autoSegmentResponse.allEndTimesString;
        } else {
            this.changeStateAndSetExpectedAsync("record");

            // If there is a more detailed error from C#, it should be reported via ErrorReport.ReportNonFatal[...]

            this.displaySplitError();
            return undefined;
        }
    }

    private updatePlaybackModeToTextBox() {
        const currentTextBox = this.getCurrentTextBox();
        if (currentTextBox) {
            const audioSentencesInCurrentBox = currentTextBox.getElementsByClassName(
                kAudioSentence
            );
            // Careful! The HTMLCollection can be dynamically modified as you iterate it (and remove classes from it).
            let collectionIndex = 0;
            while (audioSentencesInCurrentBox.length > 0) {
                const audioSentenceElement = audioSentencesInCurrentBox.item(
                    collectionIndex
                );
                if (audioSentenceElement) {
                    audioSentenceElement.classList.remove(kAudioSentence);
                    audioSentenceElement.classList.add(kSegmentClass);
                } else {
                    ++collectionIndex;
                }
            }

            currentTextBox.classList.add(kAudioSentence);
            this.setCurrentAudioId(currentTextBox.id);
        }
    }

    public markAudioSplit() {
        const currentTextBox = this.getCurrentTextBox();
        if (currentTextBox) {
            currentTextBox.classList.add("bloom-postAudioSplit");
        }
    }

    private clearAudioSplit() {
        const currentTextBox = this.getCurrentTextBox();
        if (currentTextBox) {
            currentTextBox.classList.remove("bloom-postAudioSplit");
            currentTextBox.removeAttribute("data-audioRecordingEndTimes");
        }
    }

    private getElementsToUpdateForCursor(): (Element | null)[] {
        const elementsToUpdate: (Element | null)[] = [];
        elementsToUpdate.push(document.getElementById("toolbox"));

        const pageBody = this.getPageDocBody();
        if (pageBody) {
            elementsToUpdate.push(pageBody);
            const editables = pageBody.getElementsByClassName("bloom-editable");
            for (let i = 0; i < editables.length; ++i) {
                elementsToUpdate.push(editables[i]);
            }
        }

        return elementsToUpdate;
    }

    private playESpeakPreview(): void {
        const current = this.getCurrentHighlight();
        if (current) {
            const textToSpeak = current.innerText;

            const inputParameters = {
                text: textToSpeak,
                lang: this.getAutoSegmentLanguageCode()
            };

            postJson(
                "audioSegmentation/eSpeakPreview",
                JSON.stringify(inputParameters),
                result => {
                    if (result && result.data && result.data.filePath) {
                        const convertedText: string = result.data.text;
                        const languageUsed: string = result.data.lang;
                        const fileUsed: string = result.data.filePath;

                        if (convertedText) {
                            theOneLocalizationManager
                                .asyncGetText(
                                    "EditTab.Toolbox.TalkingBookTool.ESpeakPreview.ResultOfOrthographyConversion",
                                    "Result of orthography conversion:",
                                    "After this text, the program will display the text of the current text box after being converted into the script of another language using the settings in the conversion file."
                                )
                                .done(localizedMessage1 => {
                                    theOneLocalizationManager
                                        .asyncGetText(
                                            "EditTab.Toolbox.TalkingBookTool.ESpeakPreview.LanguageUsed",
                                            "eSpeak language:",
                                            "After this text, the program will display the language code of the language settings that eSpeak used. eSpeak is a piece of software that this program uses to do text-to-speech (have the computer read text out loud)."
                                        )
                                        .done(localizedMessage2 => {
                                            theOneLocalizationManager
                                                .asyncGetText(
                                                    "EditTab.Toolbox.TalkingBookTool.ESpeakPreview.ConversionFileUsed",
                                                    "Conversion file used:",
                                                    "After this text, the program will display the path to the conversion file (the location of the file on this computer). The conversion file specifies a mapping which is used to convert the script for one language into the script for another."
                                                )
                                                .done(localizedMessage3 => {
                                                    toastr.info(
                                                        `${localizedMessage1} \"${convertedText}\"<br /><br />` +
                                                            `${localizedMessage2} ${languageUsed}<br /><br />` +
                                                            `${localizedMessage3} ${fileUsed}`
                                                    );
                                                });
                                        });
                                });
                        }
                    }
                }
            );
        }
    }

    private updateDisplay(maySetHighlight = true): void {
        this.updateSplitButton();

        const container = document.getElementById(
            "advanced-talking-book-controls-react-container"
        );
        if (!container) {
            // Won't exist for unit tests
            return;
        }
        ReactDOM.render(
            React.createElement(TalkingBookAdvancedSection, {
                recordingMode: this.recordingMode,
                haveACurrentTextboxModeRecording:
                    this.haveAudio &&
                    this.getCurrentPlaybackMode(maySetHighlight) ===
                        RecordingMode.TextBox,
                setRecordingMode: async (recordingMode: RecordingMode) => {
                    this.setRecordingModeAsync(recordingMode);
                    this.updateDisplay();
                },
                //hasAudio: this.getStatus("clear") === Status.Enabled, // plausibly, we could instead require that we have *all* the audio
                hasAudio: this.haveAudio,
                hasRecordableDivs:
                    // It's a bit expensive to do the test for text present, but without it,
                    // Import Recording will be improperly enabled on an empty page.
                    this.getRecordableDivs(true, false).length > 0,
                handleImportRecordingClick: () =>
                    this.handleImportRecordingClick(),
                insertSegmentMarker: () => {
                    const selection = this.getPageFrame()!.contentWindow!.getSelection();
                    const range = selection!.getRangeAt(0);
                    const marker = document.createTextNode("|");
                    range.insertNode(marker);
                },
                inShowPlaybackOrderMode: this.inShowPlaybackOrderMode,
                setShowPlaybackOrder: (isOn: boolean) => {
                    this.setShowPlaybackOrderMode(isOn);
                },
                showingImageDescriptions: this.showingImageDescriptions,
                setShowingImageDescriptions: (isOn: boolean) => {
                    this.setShowingImageDescriptions(isOn);
                }
            }),
            container
        );
    }

    private editTimingsFileAsync = async (timingsFilePath: string) => {
        // we'll give this a real UI in the future. Also, not going to localize this yet.
        const realTimingsFile = timingsFilePath;
        alert(
            `Bloom will now open the timings file so that you can edit it directly. Alternatively, you can use Audacity by importing/exporting this file as a "labels" file. For information on how to edit this file using Audacity, search docs.bloomlibrary.org for "Audacity" Either way, you will need to use the "Apply Timings File..." button to apply your changes.\r\n\r\nCurrent timings file is ${realTimingsFile}\r\nCurrent audio file is ${this.currentAudioId} (.wav or .mp3)`
        );
        postJson("fileIO/openFileInDefaultEditor", {
            path: realTimingsFile
        });
    };
    private applyTimingsFileAsync = async (
        timingsFilePath: string
    ): Promise<string | undefined> => {
        const realTimingsFile = timingsFilePath;
        const result = await postJson("fileIO/chooseFile", {
            title: "Choose Timing File",
            fileTypes: [
                {
                    name: "Tab-separated Timing File",
                    extensions: ["txt", "tsv"]
                }
            ],
            defaultPath: realTimingsFile
        });
        if (!result || !result.data) {
            return;
        }

        return await this.split(result.data);
    };

    private confirmReplaceProps: IConfirmDialogProps = {
        title: "Replace with new audio?",
        titleL10nKey:
            "EditTab.Toolbox.TalkingBookTool.ImportRecording.ConfirmReplace",
        message:
            "If you import this recording, it will replace all the audio for this text box.",
        messageL10nKey:
            "EditTab.Toolbox.TalkingBookTool.ImportRecording.ConfirmReplaceMessage",
        confirmButtonLabel: "Replace",
        confirmButtonLabelL10nKey: "Common.Replace",
        onDialogClose: dialogResult => {
            if (dialogResult === DialogResult.Confirm) {
                this.importRecordingAsync();
            }
        }
    };
    private handleImportRecordingClick(): void {
        if (this.doesRecordingExistForCurrentSelection()) {
            getEditTabBundleExports().showConfirmDialog(
                this.confirmReplaceProps
            );
        } else {
            this.importRecordingAsync();
        }
    }

    public async importRecordingAsync(): Promise<void> {
        const result = await postJson("fileIO/chooseFile", {
            title: "Choose Audio File",
            fileTypes: [{ name: "MP3 File", extensions: ["mp3"] }]
        });
        if (!result) {
            return;
        }

        const importPath: string = result.data;
        if (!importPath) return;

        const resultAudioDir = await postJson(
            "fileIO/getSpecialLocation",
            "CurrentBookAudioDirectory"
        );

        if (!resultAudioDir) {
            return;
        }

        // If we ever import audio file types other than .mp3, we will need to update
        // BookCompressor.AudioFileExtensions.
        const targetPath =
            resultAudioDir.data + "/" + this.getCurrentAudioId() + ".mp3";
        await postData(
            "fileIO/copyFile",
            {
                from: encodeURIComponent(importPath),
                to: encodeURIComponent(targetPath)
            },
            this.finishNewRecordingOrImportAsync.bind(this)
        );
    }

    // Returns all elements that match CSS selector {expr} as an array.
    // Querying can optionally be restricted to {container}’s descendants
    // If includeSelf is true, it includes both itself as well as its descendants.
    // Otherwise, it only includes descendants.
    // Also filters out imageDescriptions if we aren't supposed to be reading them.
    private findAll(
        expr: string,
        container: HTMLElement | undefined = undefined,
        includeSelf: boolean = false
    ): HTMLElement[] {
        // querySelectorAll checks all the descendants
        const allMatches: HTMLElement[] = [].slice.call(
            (container || document).querySelectorAll(expr)
        );

        // Now check itself
        if (includeSelf && container && container.matches(expr)) {
            allMatches.push(container);
        }

        return allMatches;
    }

    // Match space or &nbsp; (\u00a0) or &ZeroWidthSpace; (\u200b). Must have three or more in a row to match.
    // Geckofx would typically give something like `&nbsp;&nbsp;&nbsp; ` but wv2 usually gives something like `&nbsp; &nbsp; `
    private multiSpaceRegex = /[ \u00a0\u200b]{3,}/;
    private multiSpaceRegexGlobal = new RegExp(this.multiSpaceRegex, "g");

    /**
     * Finds and fixes any elements on the page that should have their audio-highlighting disabled.
     */
    public fixHighlighting(currentAudioElement?: HTMLElement) {
        const audioElements = currentAudioElement
            ? [currentAudioElement]
            : this.getAudioElements();
        audioElements.forEach(audioElement => {
            // FYI, don't need to process the bloom-linebreak spans. Nothing bad happens, just unnecessary.
            const matches = this.findAll(
                "span[id]:not(.bloom-linebreak)",
                audioElement,
                true
            );
            matches.forEach(element => {
                // Simple check to help ensure that elements that don't need to be modified will remain untouched.
                // This doesn't consider whether text that shouldn't be highlighted is already in inside an
                // element with highlight disabled, but that's ok. The code down the stack checks that.
                const containsNonHighlightText = !!element.innerText.match(
                    this.multiSpaceRegex
                );

                if (containsNonHighlightText) {
                    if (!this.nodesToRestoreAfterPlayEnded.has(element.id)) {
                        // Note: The map could already have the id if you do Play -> Pause -> Play
                        // We want the modifications to exist during the Pause period,
                        // and we want the original innerHTML to win, so that's why we need to check
                        // if the ID exists already and avoid overwriting it.
                        this.nodesToRestoreAfterPlayEnded.set(
                            element.id,
                            element.innerHTML
                        );
                    }

                    this.fixHighlightingInNode(element, element);
                }
            });
        });
    }

    /**
     * Recursively fixes the audio-highlighting within a node (whether element node or text node)
     * @param node The node to recursively fix
     * @param startingSpan The starting span, AKA the one that will receive .ui-audioCurrent in the future.
     */
    private fixHighlightingInNode(node: Node, startingSpan: HTMLSpanElement) {
        if (
            node.nodeType === Node.ELEMENT_NODE &&
            (node as Element).classList.contains(kDisableHighlightClass)
        ) {
            // No need to process bloom-highlightDisabled elements (they've already been processed)
            return;
        } else if (node.nodeType === Node.TEXT_NODE) {
            // Leaf node. Fix the highlighting, then go back up the stack.
            this.fixHighlightingInTextNode(node, startingSpan);
            return;
        } else {
            // Recursive case
            const childNodesCopy = Array.from(node.childNodes); // Make a copy because node.childNodes is being mutated
            childNodesCopy.forEach(childNode => {
                this.fixHighlightingInNode(childNode, startingSpan);
            });
        }
    }

    /**
     * Analyzes a text node and fixes its highlighting.
     */
    private fixHighlightingInTextNode(
        textNode: Node,
        startingSpan: HTMLSpanElement
    ) {
        if (textNode.nodeType !== Node.TEXT_NODE) {
            throw new Error(
                "Invalid argument to fixMultiSpaceInTextNode: node must be a TextNode"
            );
        }

        if (!textNode.nodeValue) {
            return;
        }

        // string.matchAll would be cleaner, but not supported in all browsers (in particular, FF60)
        // Use RegExp.exec for greater compatibility.
        this.multiSpaceRegexGlobal.lastIndex = 0; // RegExp.exec is stateful! Need to reset the state.
        const matches: {
            text: string;
            startIndex: number;
            endIndex: number; // the index of the first character to exclude
        }[] = [];
        let regexResult: RegExpExecArray | null;
        while (
            (regexResult = this.multiSpaceRegexGlobal.exec(
                textNode.nodeValue
            )) != null
        ) {
            regexResult.forEach(matchingText => {
                matches.push({
                    text: matchingText,
                    startIndex:
                        this.multiSpaceRegexGlobal.lastIndex -
                        matchingText.length,
                    endIndex: this.multiSpaceRegexGlobal.lastIndex // the index of the first character to exclude
                });
            });
        }

        // First, generate the new DOM elements with the fixed highlighting.
        const newNodes: Node[] = [];
        if (matches.length === 0) {
            // No matches
            newNodes.push(this.makeHighlightedSpan(textNode.nodeValue));
        } else {
            let lastMatchEndIndex = 0; // the index of the first character to exclude of the last match
            for (let i = 0; i < matches.length; ++i) {
                const match = matches[i];

                const preMatchText = textNode.nodeValue.slice(
                    lastMatchEndIndex,
                    match.startIndex
                );
                lastMatchEndIndex = match.endIndex;
                if (preMatchText)
                    newNodes.push(this.makeHighlightedSpan(preMatchText));

                newNodes.push(document.createTextNode(match.text));

                if (i === matches.length - 1) {
                    const postMatchText = textNode.nodeValue.slice(
                        match.endIndex
                    );
                    if (postMatchText) {
                        newNodes.push(this.makeHighlightedSpan(postMatchText));
                    }
                }
            }
        }

        // Next, replace the old DOM element with the new DOM elements
        const oldNode = textNode;
        if (oldNode.parentNode && newNodes && newNodes.length > 0) {
            for (let i = 0; i < newNodes.length; ++i) {
                const nodeToInsert = newNodes[i];
                oldNode.parentNode.insertBefore(nodeToInsert, oldNode);
            }

            oldNode.parentNode.removeChild(oldNode);

            // We need to set ancestor's background back to transparent (instead of highlighted),
            // and let each of the newNodes's styles control whether to be highlighted or transparent.
            // If ancestor was highlighted but one of its new descendant nodes was transparent,
            // all that would happen is the descendant would allow the ancestor's highlight color to show through,
            // which doesn't achieve what we want :(
            startingSpan.classList.add(kDisableHighlightClass);
        }
    }

    private makeHighlightedSpan(textContent: string) {
        const newSpan = document.createElement("span");
        newSpan.classList.add(kEnableHighlightClass);
        newSpan.appendChild(document.createTextNode(textContent));
        return newSpan;
    }

    private nodesToRestoreAfterPlayEnded = new Map<string, string>();

    /**
     * This function will undo in BloomDesktop the modifications made by fixHighlighting()
     */
    public revertFixHighlighting() {
        this.nodesToRestoreAfterPlayEnded.forEach((htmlToRestore, id) => {
            const pageDocBody = this.getPageDocBody();
            const element = pageDocBody?.querySelector(`#${id}`);
            if (element) {
                element.innerHTML = htmlToRestore;
                element.classList.remove(kDisableHighlightClass);
            } else {
                console.warn("Can't find element " + id);
            }
        });
        this.nodesToRestoreAfterPlayEnded.clear();
    }
}

export class AudioTextFragment {
    public fragmentText: string;
    public id: string;

    public constructor(fragmentText: string, id: string) {
        this.fragmentText = fragmentText;
        this.id = id;
    }
}

// Generally, use getAudioRecorder() instead to make sure you get the one in the right iframe
export let theOneAudioRecorder: AudioRecording;

// Used by talkingBook when initially showing the tool.
export async function initializeTalkingBookToolAsync(): Promise<void> {
    if (!theOneAudioRecorder) {
        theOneAudioRecorder = new AudioRecording();
        await theOneAudioRecorder.initializeTalkingBookToolAsync();
    }
}

export function bumpUp(whichPositionToBump: number) {
    const audioRecorder = getAudioRecorder();
    if (!audioRecorder) {
        return; // paranoia
    }
    audioRecorder.bumpUp(whichPositionToBump);
}

export function bumpDown(whichPositionToBump: number) {
    const audioRecorder = getAudioRecorder();
    if (!audioRecorder) {
        return; // paranoia
    }
    audioRecorder.bumpDown(whichPositionToBump);
}
