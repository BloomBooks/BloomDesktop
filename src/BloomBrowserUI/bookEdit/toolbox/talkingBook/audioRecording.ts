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
import { BloomApi } from "../../../utils/bloomApi";
import * as toastr from "toastr";
import WebSocketManager from "../../../utils/WebSocketManager";
import { ToolBox } from "../toolbox";
import { element } from "prop-types";

enum Status {
    Disabled, // Can't use button now (e.g., Play when there is no recording)
    Enabled, // Can use now, not the most likely thing to do next
    Expected, // The most likely/appropriate button to use next (e.g., Play right after recording)
    Active // Button now active (Play while playing; Record while held down)
}

// Should correspond to the version in "\src\BloomExe\web\controllers\TalkingBookApi.cs"
export enum AudioRecordingMode {
    Unknown = "Unknown",
    Sentence = "Sentence",
    TextBox = "TextBox"
}

const kWebsocketContext = "audio-recording";
const kAudioSentence = "audio-sentence"; // Even though these can now encompass more than strict sentences, we continue to use this class name for backwards compatability reasons
const kAudioCurrent = "ui-audioCurrent";
const kAudioSentenceClassSelector = "." + kAudioSentence;
const kBloomEditableTextBoxClass = "bloom-editable";
const kBloomEditableTextBoxSelector = "div.bloom-editable";

const kAudioSplitId = "audio-split";

const kRecordingModeControl: string = "audio-recordingModeControl";
const kRecordingModeClickHandler: string =
    "audio-recordingModeControl-clickHandler";

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
export default class AudioRecording {
    private recording: boolean;
    private levelCanvas: HTMLCanvasElement;
    private levelCanvasWidth: number = 15;
    private levelCanvasHeight: number = 80;
    private hiddenSourceBubbles: JQuery;
    private elementsToPlayConsecutivelyStack: Element[] = [];
    private nextElementIdToPlay: string;
    private awaitingNewRecording: boolean;

    private audioSplitButton: HTMLButtonElement;
    public recordingModeInput: HTMLInputElement; // Currently a checkbox, could change to a radio button in the future

    public audioRecordingMode: AudioRecordingMode;

    // Corresponds to the collection default recording mode which we would theoretically get via async call to C# API
    // This is rather annoying because then we have async calls being introduced all over the place.
    // This seems rather unnecessary considering that:
    //   1) It's not even that important to solve the consistency problem and get the accurate value
    //   2) this is probably the only writer, which would greatly simplify the consistency problem of maintaining an accurate 2nd cached copy which can be accessed synchronously
    // Therefore, decided to use a cached copy instead.
    private cachedCollectionDefaultRecordingMode: AudioRecordingMode;

    private isShowing: boolean;

    // map<string, string[]> from a sentence to the desired IDs for that span (instead of using a new, dynamically generated one)
    // We have a string[] representing the IdList instead of just a string ID because a text box could potentially contain the same sentence multiple times.
    private sentenceToIdListMap: object = {};
    public __testonly__sentenceToIdListMap = this.sentenceToIdListMap; // Exposing it for unit tests. Not meant for public use.

    private stringToSentencesCache: object = {};

    private listenerFunction: (MessageEvent) => void;

    constructor() {
        this.audioSplitButton = <HTMLButtonElement>(
            document.getElementById(kAudioSplitId)!
        );

        // Initialize to Unknown (as opposed to setting to the default Sentence) so we can identify
        // when we need to fetch from Collection Settings vs. when it's already set.
        this.audioRecordingMode = AudioRecordingMode.Unknown;

        this.recordingModeInput = <HTMLInputElement>(
            document.getElementById(kRecordingModeControl)
        );
        if (this.recordingModeInput != null) {
            // Only expected to be null in the unit tests.
            // Initial state should be disabled so that enableRecordingMode will recognize it needs
            // to initialize things on startup.
            this.recordingModeInput.disabled = true;
        }
    }

    // Class method called by exported function of the same name.
    // Only called the first time the Toolbox is opened for this book during this Editing session.
    public initializeTalkingBookTool() {
        this.pullDefaultRecordingModeAsync();

        // I've sometimes observed events like click being handled repeatedly for a single click.
        // Adding these .off calls seems to help...it's as if something causes this show event to happen
        // more than once so the event handlers were being added repeatedly, but I haven't caught
        // that actually happening. However, the off() calls seem to prevent it.
        $("#audio-next")
            .off()
            .click(e => this.moveToNextAudioElement());
        $("#audio-prev")
            .off()
            .click(e => this.moveToPrevAudioElement());
        $("#audio-record")
            .off()
            .mousedown(e => this.startRecordCurrent())
            .mouseup(e => this.endRecordCurrent());
        $("#audio-play")
            .off()
            .click(e => this.playCurrent());
        $("#audio-split")
            .off()
            .click(e => this.split());
        $("#audio-listen")
            .off()
            .click(e => this.listen());
        $("#audio-clear")
            .off()
            .click(e => this.clearRecording());

        $("#player").off();
        // The following speeds playback, ensures we get the durationchange event.
        $("#player").attr("preload", "auto");
        $("#player").bind("error", e => {
            if (this.playingAudio()) {
                // during a "listen", we walk through each segment, but some (or all) may not have audio
                this.playEnded(); //move to the next one
            } else if (this.awaitingNewRecording) {
                // file may not have been created yet. Try again.
                this.updatePlayerStatus();
            }
            // A previous version did a toast here. However, the auto-preload which we set up to help
            // us update durations causes an error to be raised for all nonexistent audio files; it
            // may just be because we haven't recorded it yet. A toast for that is excessive.
            // We could possibly arrange for a toast if we get an error while actually playing,
            // but it seems very unlikely.
        });

        $("#player").bind("ended", e => this.playEnded());
        $("#player").bind("durationchange", e => this.durationChanged());
        $("#audio-input-dev")
            .off()
            .click(e => this.selectInputDevice());

        toastr.options.positionClass = "toast-toolbox-bottom";
        toastr.options.timeOut = 10000;
        toastr.options.preventDuplicates = true;
    }

    // Updates our cached version of the default recording mode with the version from the Bloom API Server.
    // If specified, the doneCallback parameter will be called after the result is returned from the server.
    public pullDefaultRecordingModeAsync(doneCallback: () => void = () => {}) {
        BloomApi.get("talkingBook/defaultAudioRecordingMode", result => {
            this.cachedCollectionDefaultRecordingMode = AudioRecording.getAudioRecordingModeWithDefaultFromString(
                result.data
            );

            doneCallback();
        });
    }

    public playingAudio(): boolean {
        return (
            this.elementsToPlayConsecutivelyStack &&
            this.elementsToPlayConsecutivelyStack.length > 0
        );
    }

    // Sets up member variables (e.g. audioRecordingMode) that updateMarkup...() depends on.
    //
    // Precondition: Assumes that all initialization of collection-depenedent settings has already been done.
    public initializeForMarkup() {
        const currentTextBox = this.getCurrentTextBox();

        this.audioRecordingMode = this.getRecordingModeOfTextBox(
            currentTextBox
        );
        if (this.audioRecordingMode == AudioRecordingMode.Unknown) {
            this.audioRecordingMode = AudioRecordingMode.Sentence;
        }

        this.setRecordingModeInput();
    }

    // Given a text box, determines its recording mode.
    // If explicitly persisted, that value will be used. But if not, will perform a series of fallback checks.
    private getRecordingModeOfTextBox(
        textBoxDiv: Element | null
    ): AudioRecordingMode {
        if (textBoxDiv) {
            // First, attempt to assign it from the text box's explicitly specified value if possible.
            const audioRecordingModeStr = textBoxDiv.getAttribute(
                "data-audioRecordingMode"
            );

            const recordingMode = AudioRecording.getAudioRecordingModeFromString(
                audioRecordingModeStr
            );
            if (recordingMode != AudioRecordingMode.Unknown) {
                return recordingMode;
            }
        }

        if (
            this.getPageDocBodyJQuery().find("[data-audioRecordingMode]")
                .length > 0
        ) {
            // For a text box that doesn't already have mode specified, first fallback is to make it the same as another text box on the page that does have it
            const audioRecordingModeStr: string = this.getPageDocBodyJQuery()
                .find("[data-audioRecordingMode]")
                .first()
                .attr("data-audioRecordingMode");

            const recordingMode = AudioRecording.getAudioRecordingModeFromString(
                audioRecordingModeStr
            );
            if (recordingMode != AudioRecordingMode.Unknown) {
                return recordingMode;
            }
        }

        if (
            this.getPageDocBodyJQuery().find("span.audio-sentence").length > 0
        ) {
            // This may happen when loading books from 4.3 or earlier that already have text recorded,
            // and is especially important if the collection default is set to anything other than Sentence.
            return AudioRecordingMode.Sentence;
        }

        // We are not sure what it should be.
        // So, check what the collection default has to say
        // Precondition: Assumes this class is the only writer, and doesn't bother attempting to retrieve from API or pull in the changes for next time
        return this.cachedCollectionDefaultRecordingMode;
    }

    // Typecast from string to AudioRecordingMode.
    public static getAudioRecordingModeFromString(
        audioRecordingModeStr: string | null
    ): AudioRecordingMode {
        if (
            audioRecordingModeStr &&
            audioRecordingModeStr in AudioRecordingMode
        ) {
            return <AudioRecordingMode>audioRecordingModeStr;
        } else {
            return AudioRecordingMode.Unknown;
        }
    }

    // Typecast from string to AudioRecordingMode, but if it would return Unknown, return the default value instead.
    public static getAudioRecordingModeWithDefaultFromString(
        audioRecordingModeStr: string | null,
        defaultRecordingMode: AudioRecordingMode = AudioRecordingMode.Sentence
    ): AudioRecordingMode {
        let recordingMode = AudioRecording.getAudioRecordingModeFromString(
            audioRecordingModeStr
        );

        if (recordingMode == AudioRecordingMode.Unknown) {
            recordingMode = defaultRecordingMode;
        }

        return recordingMode;
    }

    private setRecordingModeInput() {
        if (!this.recordingModeInput) {
            // Button is null for some reason, no need to update its checked status. And don't bother doing anything else either.
            return;
        }

        // We initialize the checkbox based on our state and whether this is an xMatter
        // page or not. We ran into a problem (BL-6737) where audio was lost if it was
        // recorded in TextBox mode, because the xMatter replacement code (using DataDiv)
        // didn't handle that case. We decided it was best to only allow Sentence mode on
        // xMatter pages.
        if (ToolBox.isXmatterPage()) {
            this.recordingModeInput.checked = false;
            this.disableRecordingModeControl(false);
            // We want this setting change to be a temporary thing, in the sense that
            // when we move to a non-xMatter (normal content) page, the UI will still be
            // in whatever mode the user had it in. Since the mode input is disabled, it
            // won't get set to anything else until we select a non-xMatter page.
            this.audioRecordingMode = AudioRecordingMode.Sentence;
        } else {
            if (this.audioRecordingMode == AudioRecordingMode.Sentence) {
                this.recordingModeInput.checked = false;
                this.hideSplitButton();
            } else if (this.audioRecordingMode == AudioRecordingMode.TextBox) {
                this.recordingModeInput.checked = true;
                this.displaySplitButton();
            }
        }
    }

    // At this point we are handling all missing dependencies the same.
    private handleMissingDependency(): void {
        const aeneasName = "Aeneas";
        const blAeneasUrl = "https://bloomlibrary.org/aeneas";
        // For now at least, we only report Aeneas as missing and point the user to pages
        // where installing Aeneas will also install all of its dependencies.
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Toolbox.TalkingBook.MissingDependency",
                "To use Auto Segment, first install this {0} system.",
                "The placeholder {0} will be replaced with the dependency that needs to be installed."
            )
            .done(localizedMessage => {
                let url: string = "";
                if (window.navigator.platform.startsWith("Win")) {
                    url = blAeneasUrl;
                } else {
                    url = blAeneasUrl + "/linux";
                }
                const anchor = '<a href="' + url + '">' + aeneasName + "</a>";
                const missingDependencyHoverTip = theOneLocalizationManager.simpleDotNetFormat(
                    localizedMessage,
                    [aeneasName]
                );
                const missingDependencyMsgWithLink = theOneLocalizationManager.simpleDotNetFormat(
                    localizedMessage,
                    [anchor]
                );
                toastr.error(missingDependencyMsgWithLink);
                this.audioSplitButton.setAttribute(
                    "title",
                    missingDependencyHoverTip
                );
            });
    }

    public setupForListen() {
        $("#player").bind("ended", e => this.playEnded());
        $("#player").bind("error", e => {
            // during a "listen", we walk through each segment, but some (or all) may not have audio
            this.playEnded(); //move to the next one
        });
    }

    // Called by TalkingBookModel.showTool() when a different tool is added/chosen or when the toolbox is re-opened, but not when a new page is added
    public setupForRecording(): void {
        this.updateInputDeviceDisplay();

        this.hiddenSourceBubbles = this.getPageDocBodyJQuery().find(
            ".uibloomSourceTextsBubble"
        );
        this.hiddenSourceBubbles.hide();
        const editable = this.getRecordableDivs();
        if (editable.length === 0) {
            // no editable text on this page.
            this.changeStateAndSetExpected("");
            return;
        }

        this.updateMarkupForCurrentText(this.getCurrentPlaybackMode());

        this.changeStateAndSetExpected("record");

        this.addAudioLevelListener();
    }

    // Called when a new page is loaded and (above) when the Talking Book Tool is chosen.
    public addAudioLevelListener(): void {
        WebSocketManager.addListener(kWebsocketContext, e => {
            if (e.id == "peakAudioLevel")
                this.setStaticPeakLevel(e.message ? e.message : "");
        });
    }

    // Called by TalkingBookModel.detachFromPage(), which is called when changing tools, hiding the toolbox,
    // or saving (leaving) pages.
    public removeRecordingSetup() {
        this.hiddenSourceBubbles.show();
        const page = this.getPageDocBodyJQuery();
        page.find(".ui-audioCurrent")
            .removeClass("ui-audioCurrent")
            .removeClass("disableHighlight");
    }

    public stopListeningForLevels() {
        WebSocketManager.closeSocket(kWebsocketContext);
    }

    // We now do recording in all editable divs that are visible.
    // This should NOT restrict to ones that already contain audio-sentence spans.
    // BL-5575 But we don't (at this time) want to record comprehension questions.
    // And BL-5457: Check that we actually have recordable text in the divs we return.
    private getRecordableDivs(includeCheckForText: boolean = true): JQuery {
        const $this = this;
        const divs = this.getPageDocBodyJQuery().find(
            ":not(.bloom-noAudio) > " + kBloomEditableTextBoxSelector
        );
        return divs.filter(":visible").filter((idx, elt) => {
            if (!includeCheckForText) {
                return true;
            }
            return this.stringToSentences(elt.innerHTML).some(frag => {
                return $this.isRecordable(frag);
            });
        });
    }

    // Corresponds to getRecordableDivs() but only applies the check to the current element
    public isRecordableDiv(
        element: Element | null,
        includeCheckForText: boolean = true,
        includeCheckForVisibility: boolean = true
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

            if (includeCheckForVisibility && !$(element).is(":visible")) {
                // Enhance: Create a non-JQuery equivalent isVisible function
                return false;
            }

            if (!includeCheckForText) {
                return true;
            }
            return this.stringToSentences(element!.innerHTML).some(frag => {
                return this.isRecordable(frag);
            });
        } else {
            return false;
        }
    }

    private containsAnyAudioElements(): boolean {
        return this.getAudioElements().length > 0;
    }

    private getAudioElements(): JQuery {
        return this.getRecordableDivs()
            .find(kAudioSentenceClassSelector) // Looks only in the descendants, but won't check any of the elements themselves in getRecordableDivs()
            .addBack(kAudioSentenceClassSelector); // Also applies the selector to the result of getRecordableDivs()
    }

    private doesElementContainAnyAudioElements(element: Element): boolean {
        return (
            element.classList.contains(kAudioSentence) ||
            element.getElementsByClassName(kAudioSentence).length > 0
        );
    }

    private moveToNextAudioElement(): void {
        toastr.clear();

        var next = this.getNextAudioElement();
        if (!next) return;
        var current: JQuery = this.getPageDocBodyJQuery().find(
            ".ui-audioCurrent"
        );
        this.setCurrentAudioElementFromJQuery(current, $(next));
        this.changeStateAndSetExpected("record");
    }

    private moveToPrevAudioElement(): void {
        toastr.clear();
        var current: JQuery = this.getPageDocBodyJQuery().find(
            ".ui-audioCurrent"
        );
        var audioElts = this.getAudioElements();
        if (current.length === 0 || audioElts.length === 0) return;
        var currentIndex = audioElts.index(current);
        if (currentIndex === 0) return;
        var prev = this.getPreviousAudioElement();
        if (prev == null) return;
        this.setCurrentAudioElementFromJQuery(current, $(prev));
        this.changeStateAndSetExpected("record"); // Enhance: I think it'd actually be better to dynamically assign Expected based on what audio is available etc., instead of based on state transitions. Especially when doing Prev.
    }

    public getNextAudioElement(): Element | null {
        return this.incrementAudioElementIndex();
    }

    public getPreviousAudioElement(): Element | null {
        const traverseReverse = true;
        return this.incrementAudioElementIndex(traverseReverse);
    }

    // Advances (or rewinds) through the audio elements by 1.
    private incrementAudioElementIndex(
        isTraverseInReverseOn: boolean = false
    ): Element | null {
        const currentTextBox = this.getCurrentTextBox();
        if (!currentTextBox) return null;

        if (this.audioRecordingMode == AudioRecordingMode.TextBox) {
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

    private incrementAudioElementIndexForTextBoxMode(
        isTraverseInReverseOn: boolean,
        currentTextBox: Element
    ): Element | null {
        // This means we need to go to the next text box, and then determine the right Recording segment.

        const incrementAmount = isTraverseInReverseOn ? -1 : 1;

        // Careful! Even though the Recording Mode is text box, current can be a Sentence... use currentTextBox instead
        const allTextBoxes = this.getRecordableDivs(false);
        const currentIndex = allTextBoxes.index(currentTextBox);
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
        const current = this.getPageDocBodyJQuery().find(".ui-audioCurrent");
        if (!current || current.length === 0) {
            return null;
        }

        // Find the next segment to be PLAYED.
        const audioElts = this.getAudioElements();
        if (audioElts.length === 0) {
            return null;
        }
        const nextIndex = audioElts.index(current) + incrementAmount;
        if (nextIndex < 0 || nextIndex >= audioElts.length) {
            return null;
        }
        const nextElement = audioElts[nextIndex];

        // Now find the text box of the next segment to be played.
        const nextTextBox = this.getParentTextBoxOfElement(nextElement);
        if (!nextTextBox) return null;

        if (currentTextBox == nextTextBox) {
            // Same Text Box: This case is easy because the next mode is guaranteed to be the same as the current mode.
            return nextElement;
        } else {
            // Different text box. Do some logic to figure out exactly which element should be played based on the mode and direction.
            return this.getNextRecordingSegment(
                nextTextBox,
                isTraverseInReverseOn
            );
        }
    }

    // Get the next recording segment when moving to the specified different text box. Depending on that box's mode, it may be the box itself, or its first or last segment.
    private getNextRecordingSegment(
        nextTextBox: Element,
        isTraverseInReverseOn: boolean
    ): Element | null {
        const nextRecordingMode = this.getRecordingModeOfTextBox(nextTextBox);

        if (nextRecordingMode == AudioRecordingMode.TextBox) {
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

    private setCurrentAudioElement(
        elementToChangeTo: Element,
        checking?: boolean
    ): void {
        let firstExistingAudioCurrentElement: Element | null = null;
        const pageDocBody = this.getPageDocBody();
        if (pageDocBody) {
            const audioCurrentElements = pageDocBody.getElementsByClassName(
                "ui-audioCurrent"
            );
            if (audioCurrentElements.length > 0) {
                firstExistingAudioCurrentElement = audioCurrentElements.item(0);
            }
        }
        this.setCurrentAudioElementFrom(
            firstExistingAudioCurrentElement,
            elementToChangeTo,
            checking
        );
    }

    // Generally we set the current span, we want to highlight it. But during
    // listening to the whole page, especially in a Motion preview, we
    // prefer not to highlight the current span unless it actually has audio.
    // This is achieved by passing checking true.
    private setCurrentAudioElementFromJQuery(
        current: JQuery,
        changeTo: JQuery,
        checking?: boolean
    ): void {
        // TODO: Deprecate me
        let currentElement: HTMLElement | null = null;
        if (current && current.length > 0) {
            currentElement = current[0];
        }

        if (changeTo && changeTo.length > 0) {
            const elementToChangeTo = changeTo[0];
            this.setCurrentAudioElementFrom(
                currentElement,
                elementToChangeTo,
                checking
            );
        }
    }

    private setCurrentAudioElementFrom(
        currentElement: Element | null | undefined,
        elementToChangeTo: Element,
        checking?: boolean
    ): void {
        if (currentElement == elementToChangeTo) {
            // No need to do much, and better not to so we can avoid any temporary flashes as the highlight is removed and re-applied
            this.setNextElementIdToPlay(elementToChangeTo.id);
            return;
        }

        if (currentElement) {
            currentElement.classList.remove(
                "ui-audioCurrent",
                "disableHighlight"
            );
        }

        // We might be changing to nothing when doing a whole-page
        // preview (possibly from Motion) and there is no text or it has never
        // been marked up by the talking book tool.
        if (checking) {
            elementToChangeTo.classList.add("disableHighlight"); // prevents highlight showing at once
            axios
                .get(
                    "/bloom/api/audio/checkForSegment?id=" +
                        elementToChangeTo.id
                )
                .then(response => {
                    if (response.data === "exists") {
                        elementToChangeTo.classList.remove("disableHighlight");
                    }
                })
                .catch(error => {
                    toastr.error(
                        "Error checking on audio file " + error.statusText
                    );
                    //server couldn't find it, so just leave it unhighlighted
                });
        }

        elementToChangeTo.classList.add("ui-audioCurrent");

        // Adjust the audio file that will get played by the Play (Check) button.
        // Note: The next element to be PLAYED may not be the same as the new element with the current RECORDING highlight.
        //       The element to be played might be a strict child of the element to be recorded.
        const firstAudioSentence = this.getFirstAudioSentenceWithinElement(
            elementToChangeTo
        );
        if (firstAudioSentence) {
            this.setNextElementIdToPlay(firstAudioSentence.id);
        } else {
            console.assert(
                "Unexpected state: The element is expected to have at least one audio sentence"
            );
            this.setNextElementIdToPlay(elementToChangeTo.id);
        }

        // Before updating the controls, we need to update the audioRecordingMode. It might've changed.
        this.initializeForMarkup();
    }

    // If we have an mp3 file but not a wav file, the file server will return that instead.
    private currentAudioUrl(id: string): string {
        return this.urlPrefix() + id + ".wav";
    }

    private urlPrefix(): string {
        const bookSrc = this.getPageFrame().src;
        const index = bookSrc.lastIndexOf("/");
        const bookFolderUrl = bookSrc.substring(0, index + 1);
        return "/bloom/api/audio/wavFile?id=" + bookFolderUrl + "audio/";
    }

    // Setter for idOfNextElementToPlay
    public setNextElementIdToPlay(id: string) {
        if (!this.nextElementIdToPlay || this.nextElementIdToPlay != id) {
            this.nextElementIdToPlay = id;
            this.updatePlayerStatus(); // May be redundant sometimes, but safer to trigger player update whenever the next element changes.
        }
    }

    // Gecko has no way of knowing that we've created or modified the audio file,
    // so it will cache the previous content of the file or
    // remember if no such file previously existed. So we add a bogus query string
    // based on the current time so that it asks the server for the file again.
    // Fixes BL-3161
    private updatePlayerStatus() {
        console.assert(this.nextElementIdToPlay != null);

        const player = $("#player");
        player.attr(
            "src",
            this.currentAudioUrl(this.nextElementIdToPlay) +
                "&nocache=" +
                new Date().getTime()
        );
    }

    private startRecordCurrent(): void {
        if (!this.isEnabledOrExpected("record")) {
            return;
        }

        toastr.clear();

        this.recording = true;
        var current: JQuery = this.getPageDocBodyJQuery().find(
            ".ui-audioCurrent"
        );
        var id = current.attr("id");
        axios
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

    private endRecordCurrent(): void {
        if (!this.recording) {
            // will trigger if the button wasn't enabled, so the recording never started
            return;
        }

        this.recording = false;
        this.awaitingNewRecording = true;

        axios
            .post("/bloom/api/audio/endRecord")
            .then(response => {
                if (this.audioRecordingMode == AudioRecordingMode.TextBox) {
                    // When ending a recording for a whole text box, we enter the state for Recording=TextBox,Playback=TextBox.
                    const allowUpdateOfCurrent = false;
                    this.updateMarkupForCurrentText(AudioRecordingMode.TextBox);
                }
                this.updatePlayerStatus();
                this.changeStateAndSetExpected("play");
            })
            .catch(error => {
                this.changeStateAndSetExpected("record"); //record failed, so we expect them to try again
                toastr.error(error.response.statusText);
                console.log(error.response.statusText);
                this.updatePlayerStatus();
            });
    }

    private updatePlaybackMarkup() {
        // We can't allowUpdateOfCurrent = true at this point (while updating the playback mode) because moving the highlight while certain other operations are ongoing leads to really unintuitive and confusing end results
        // So just force the operation to apply to whatever the Current Highlight points at right now.
        const allowUpdateOfCurrent = false;
        this.updateMarkupForCurrentText(allowUpdateOfCurrent);
    }

    // Called when we get a duration for a current audio element. Mainly we want it after recording a new one.
    // However, for older documents that don't have this, just playing them all will add the new info...
    // or even just stepping through with Next.
    private durationChanged(): void {
        this.awaitingNewRecording = false;
        var current = this.getPageDocBodyJQuery().find(".ui-audioCurrent");
        current.attr(
            "data-duration",
            (<HTMLAudioElement>$("#player").get(0)).duration
        );
    }

    public getCurrentPlaybackMode(): AudioRecordingMode {
        let playbackMode = AudioRecordingMode.Sentence; // For any strange/unexpected things that happen, default back to the legacy mode (Sentence)

        const currentTextBox = this.getCurrentTextBox();
        if (!currentTextBox) {
            return playbackMode;
        }

        let divAudioSentenceCount = 0;
        let spanAudioSentenceCount = 0;

        if (currentTextBox.classList.contains(kAudioSentence)) {
            ++divAudioSentenceCount;
        }

        const audioSentenceCollection = currentTextBox.getElementsByClassName(
            kAudioSentence
        );

        for (let i = 0; i < audioSentenceCollection.length; ++i) {
            const audioSentenceElement = audioSentenceCollection.item(i);
            if (audioSentenceElement) {
                if (audioSentenceElement.nodeName == "DIV") {
                    ++divAudioSentenceCount;
                } else if (audioSentenceElement.nodeName == "SPAN") {
                    ++spanAudioSentenceCount;
                }
            }
        }

        // Enhance: You could break early out of the loop and refactor if you are confident this assert won't fail.
        console.assert(
            !(divAudioSentenceCount > 0 && spanAudioSentenceCount > 0)
        );
        if (divAudioSentenceCount > spanAudioSentenceCount) {
            playbackMode = AudioRecordingMode.TextBox;
        }

        return playbackMode;
    }
    private playCurrent(): void {
        toastr.clear();

        if (!this.isEnabledOrExpected("play")) {
            return;
        }

        // We want to play everything (highlighted according to the unit of playback) within the unit of RECORDING.
        if (this.audioRecordingMode == AudioRecordingMode.TextBox) {
            this.elementsToPlayConsecutivelyStack = this.getAudioSegmentsInCurrentTextBox().reverse();
        } else {
            this.elementsToPlayConsecutivelyStack = [];
            const currentHighlight = this.getCurrentHighlight();
            if (currentHighlight) {
                this.elementsToPlayConsecutivelyStack.push(currentHighlight);
            }
        }

        this.setCurrentAudioElement(
            this.elementsToPlayConsecutivelyStack[
                this.elementsToPlayConsecutivelyStack.length - 1
            ],
            true
        );
        this.removeExpectedStatusFromAll();
        this.setStatus("play", Status.Active);
        this.playCurrentInternal();
    }

    private playCurrentInternal() {
        const mediaPlayer = <HTMLMediaElement>document.getElementById("player");
        if (mediaPlayer.error) {
            // We can no longer rely on the error event occurring after play() is called.
            // If we pre-load audio, the error event occurs on load (which will be before play).
            // So, we check the .error property to see if an error already occurred and if so, skip past the play straight to the playEnded() which is supposed to be called on error.
            this.playEnded(); // Will also start playing the next audio to play.
        } else {
            mediaPlayer.play();
        }
    }

    // 'Listen' is shorthand for playing all the sentences on the page in sequence.
    public listen(): void {
        const original: JQuery = this.getPageDocBodyJQuery().find(
            ".ui-audioCurrent"
        );

        this.elementsToPlayConsecutivelyStack = jQuery
            .makeArray(this.getAudioElements())
            .reverse();

        const stackSize = this.elementsToPlayConsecutivelyStack.length;
        if (stackSize === 0) return;
        const firstElementToPlay = this.elementsToPlayConsecutivelyStack[
            stackSize - 1
        ]; // Remember to pop it when you're done playing it. (i.e., in playEnded)
        this.setCurrentAudioElementFromJQuery(
            original,
            $(firstElementToPlay),
            true
        );
        this.setStatus("listen", Status.Active);
        this.playCurrentInternal();
    }

    // This is currently used in Motion, which removes all the current
    // audio markup afterwards. If we use it in this tool, we need to do more,
    // such as setting the current state of controls.
    public stopListen(): void {
        (<HTMLMediaElement>document.getElementById("player")).pause();
    }

    private playEnded(): void {
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
                this.setCurrentAudioElementFrom(
                    currentElement,
                    nextElement,
                    true
                );
                this.playCurrentInternal();
                return;
            } else {
                // Nothing left to play
                this.elementsToPlayConsecutivelyStack = [];

                if (this.audioRecordingMode == AudioRecordingMode.TextBox) {
                    // The playback mode could've been playing in Sentence mode (and highlighted the Playback Segment: a sentence)
                    // But now we need to switch the highlight back to show the Recording segment.
                    const currentTextBox = this.getCurrentTextBox();
                    console.assert(
                        currentTextBox,
                        "CurrentTextBox not expected to be null"
                    );
                    if (currentTextBox) {
                        this.setCurrentAudioElementBasedOnRecordingMode(
                            currentTextBox,
                            false
                        );
                    }
                }
                // For Play (Check) in sentence mode, no need to adjust the current highlight. Just leave it on whatever it was on before.
                //  (Assumption: Record by Sentence, Play by Text Box mode combination is not allowed)

                // Enhance: Or maybe for Listen To Whole Page, it should remember what the highlight was on before and move it back to there?
            }
        }

        // Change state to "Split" if possible but fallback to Next if not.
        // TODO: Maybe we should fallback to Listen to Whole Page if Next is not available (A.k.a. you just checked the last thing)
        //  What if you reached here by listening to the whole page? Does it matter that we'll push them toward listening to it again?
        this.changeStateAndSetExpected("split");
    }

    private selectInputDevice(): void {
        var thisClass = this;
        BloomApi.get("audio/devices", result => {
            var data = result.data; // Axios apparently recognizes the JSON and parses it automatically.
            // Retrieves JSON generated by AudioRecording.AudioDevicesJson
            // Something like {"devices":["microphone", "Logitech Headset"], "productName":"Logitech Headset", "genericName":"Headset" },
            // except that in practice currrently the generic and product names are the same and not as helpful as the above.
            if (data.devices.length <= 1) return; // no change is possible.
            if (data.devices.length == 2) {
                // Just toggle between them
                var device =
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
            var devList = $("#audio-devlist");
            devList.empty();
            for (var i = 0; i < data.devices.length; i++) {
                // convert "Microphone (xxxx)" --> xxxx, where the final ')' is often missing (cut off somewhere upstream)
                var label = data.devices[i].replace(
                    /Microphone \(([^\)]*)\)?/,
                    "$1"
                );
                //make what's left safe for html
                label = $("<div>")
                    .text(label)
                    .html();
                // preserve the product name, which is the id we will send back if they choose it
                var menuItem = devList.append(
                    '<li data-choice="' + i + '">' + label + "</li>"
                );
            }
            (<any>devList)
                .one(
                    "click",
                    function(event) {
                        devList.hide();
                        var choice = $(event.target).data("choice");
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
        BloomApi.get("audio/devices", result => {
            var data = result.data;
            // See selectInputDevice for what is retrieved.
            var genericName = data.genericName || "";
            var productName = data.productName || "";
            var nameToImage = [
                ["internal", "computer.svg"],
                // checking for "Array" is motivated by JohnT's dell laptop, where the internal microphone
                // comes up as "Microphone Array (2 RealTek Hi".
                ["array", "computer.svg"],
                ["webcam", "webcam.svg"],
                // checking for "Headse" is motivated by JohnT's Logitech Headset, which comes up as "Microphone (Logitech USB Headse".
                ["headse", "headset.svg"],
                ["usb audio", "headset.svg"], // we don't really know... should we just show a USB icon?
                ["plantronics", "headset.svg"],
                ["andrea", "headset.svg"], //usb-to-line
                ["vxi", "headset.svg"], // headsets and usb-to-line
                ["line", "lineaudio.svg"],
                ["high def", "lineaudio.svg"],
                ["zoom", "recorder.svg"]
            ];
            var imageName = "microphone.svg"; // Default if we don't recognize anything significant in the name of the current device.
            for (var i = 0; i < nameToImage.length; i++) {
                var match = nameToImage[i][0];
                if (
                    genericName.toLowerCase().indexOf(match) > -1 ||
                    productName.toLowerCase().indexOf(match) > -1
                ) {
                    imageName = nameToImage[i][1];
                    break;
                }
            }
            var devButton = $("#audio-input-dev");
            var src = devButton.attr("src");
            var lastSlash = src.lastIndexOf("/");
            var newSrc = src.substring(0, lastSlash + 1) + imageName;
            devButton.attr("src", newSrc);
            devButton.attr("title", productName);
        });
    }

    // Clear the recording for this sentence
    private clearRecording(): void {
        toastr.clear();

        if (!this.isEnabledOrExpected("clear")) {
            return;
        }

        // First determine which IDs we need to delete.
        const elementIdsToDelete: string[] = [];
        if (this.audioRecordingMode == AudioRecordingMode.Sentence) {
            elementIdsToDelete.push(this.nextElementIdToPlay);
        } else {
            // i.e., AudioRecordingMode = TextBox
            if (this.getCurrentPlaybackMode() == AudioRecordingMode.TextBox) {
                // Easy enough, we just delete the single file corresponding to the text box... which is supposed to be nextElementIdToPlay
                elementIdsToDelete.push(this.nextElementIdToPlay);
            } else {
                // i.e., AudioRecordingMode = TextBox but PlaybackMode = Sentence
                // Slightly more complicated, need to delete all the sentences in this text box.
                const sentences = this.getAudioSegmentsInCurrentTextBox();
                for (let i = 0; i < sentences.length; ++i) {
                    elementIdsToDelete.push(sentences[i].id);
                }
            }
        }

        // Now go about sending out the API calls to actually delete them.
        for (let i = 0; i < elementIdsToDelete.length; ++i) {
            const idToDelete = elementIdsToDelete[i];

            axios
                .post("/bloom/api/audio/deleteSegment?id=" + idToDelete)
                .then(result => {
                    // data-duration needs to be deleted when the file is deleted.
                    // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3671.
                    // Note: this is not foolproof because the durationchange handler is
                    // being called asynchronously with stale data and sometimes restoring
                    // the deleted attribute.
                    var current = this.getPageDocBodyJQuery().find(
                        "#" + idToDelete
                    );
                    if (current.length !== 0) {
                        current.first().removeAttr("data-duration");
                    }
                })
                .catch(error => {
                    toastr.error(error.statusText);
                });
        }
        this.updatePlayerStatus();
        this.changeStateAndSetExpected("record");
    }

    // Update the input element (e.g. checkbox) which visually represents the recording mode and updates the textbox markup to reflect the new mode.
    public updateRecordingMode(forceOverwrite: boolean = false) {
        // These two checks are here for paranoia. Normally if the function is disabled
        // we don't install this click handler at all.
        if (ToolBox.isXmatterPage()) {
            this.notifyRecordingModeControlDisabledXMatter();
            return;
        }

        // Check if there are any audio recordings present.
        //   If so, these would become invalidated (and deleted down the road when the book's unnecessary files gets cleaned up)
        //   Warn the user if this deletion could happen
        //   We detect this state by relying on the same logic that turns on the Listen button when an audio recording is present
        if (
            !forceOverwrite &&
            document.getElementById("audio-play")!.classList.contains("enabled")
        ) {
            if (
                this.audioRecordingMode == AudioRecordingMode.TextBox &&
                this.getCurrentPlaybackMode() == AudioRecordingMode.TextBox
            ) {
                this.notifyRecordingModeControlDisabled();
                return;
            }
        }

        const originalRecordingMode = this.audioRecordingMode;

        const checkbox: HTMLInputElement = this.recordingModeInput;
        if (
            this.audioRecordingMode == AudioRecordingMode.Sentence ||
            this.audioRecordingMode == undefined
        ) {
            this.audioRecordingMode = AudioRecordingMode.TextBox;
            checkbox.checked = true;
            this.displaySplitButton();
        } else {
            this.audioRecordingMode = AudioRecordingMode.Sentence;
            checkbox.checked = false;
            this.hideSplitButton();
        }

        // Update the collection's default recording span mode to the new value
        BloomApi.postJson(
            "talkingBook/defaultAudioRecordingMode",
            this.audioRecordingMode
        );
        this.cachedCollectionDefaultRecordingMode = this.audioRecordingMode;

        // Update the UI after clicking the checkbox
        if (originalRecordingMode == AudioRecordingMode.TextBox) {
            // This also implies converting the playback mode to Sentence, because we disallow Recording=Sentence,Playback=TextBox.
            // Enhance: Maybe don't bother if the current playback mode is already sentence?
            // Enhance: Maybe it means that you should be less aggressively trying to convert Markup into Playback=TextBox. Or that Clear should be more aggressively attempting ton convert into Playback=Sentence.

            // We can't allowUpdateOfCurrent = true at this point (while updating the playback mode) because moving the highlight while certain other operations are ongoing leads to really unintuitive and confusing end results
            // So just force the operation to apply to whatever the Current Highlight points at right now.
            const allowUpdateOfCurrent = false;
            this.updateMarkupForCurrentText(
                AudioRecordingMode.Sentence,
                allowUpdateOfCurrent
            );
        } else {
            // From Sentence -> TextBox, we don't convert the playback mode.

            const currentTextBox = this.getCurrentTextBox();
            if (currentTextBox) {
                this.persistRecordingMode(currentTextBox);
                this.setCurrentAudioElementBasedOnRecordingMode(currentTextBox);
                this.changeStateAndSetExpected("record");
            }
        }
    }

    public displaySplitButton(): void {
        const element = document.getElementById("audio-split-wrapper");
        if (element) {
            element.classList.remove("hide-countable");
            element.classList.add("talking-book-counter");
            element.classList.remove("initial-state"); // Note that by default it's already displayed correctly, so we can remove it immediately.
        }
    }

    public hideSplitButton(): void {
        const element = document.getElementById("audio-split-wrapper");
        if (element) {
            element.classList.add("hide-countable");
            element.classList.remove("talking-book-counter");

            // Need to special case the initial load, which does not need animation.
            // In our CSS, we detect initialState and change it accordingly.
            // But since the raw HTML does not have this button hidden, we wait a little bit to make sure any animation (transition) has definitely finished
            //   before removing the class that
            if (element.classList.contains("initial-state")) {
                setTimeout(() => {
                    element.classList.remove("initial-state");
                }, 200);
            }
        }
    }

    public persistRecordingMode(element: Element) {
        element.setAttribute(
            "data-audioRecordingMode",
            this.audioRecordingMode
        );

        // This only set the RECORDING mode. Don't touch the audio-sentence markup, which represents the PLAYBACK mode.
    }

    private enableRecordingModeControl() {
        toastr.clear();
        if (this.recordingModeInput.disabled) {
            this.recordingModeInput.disabled = false;

            // The click handler (an invisible div over the checkbox) is used to handle events on the checkbox even while the checkbox is disabled (which would suppress events on the checkbox itself)
            // Note: In the future, if the click handler is no longer used, just assign the same onClick function() to the checkbox itself.
            $("#" + kRecordingModeClickHandler)
                .off()
                .click(e => this.updateRecordingMode());
        }
    }

    private disableRecordingModeControl(
        useClearRecordingsNotification: boolean = true
    ) {
        // Note: Possibly could be neat to check if all the audio is re-usable before disabling.
        //       (But then what happens if they modify the text box?  Well, it's kinda awkward, but it's already awkward if they modify the text in by-sentence mode)
        this.recordingModeInput.disabled = true;
        const handlerJquery = $("#" + kRecordingModeClickHandler);
        handlerJquery.off();
        toastr.clear();
        if (useClearRecordingsNotification) {
            // Note: In the future, if the click handler is no longer used, just assign the same onClick function() to the checkbox itself.
            handlerJquery.click(e => this.notifyRecordingModeControlDisabled());
        } else {
            handlerJquery.click(e => {
                if (ToolBox.isXmatterPage)
                    this.notifyRecordingModeControlDisabledXMatter();
            });
        }
    }

    private notifyRecordingModeControlDisabled() {
        console.assert(
            this.recordingModeInput.disabled,
            "notifyRecordingModeControlDisabled(): Caller seems to imply that it believes recordingModeInput should be disabled, but the actual state is marked as enabled."
        );

        if (this.recordingModeInput.disabled) {
            theOneLocalizationManager
                .asyncGetText(
                    "EditTab.Toolbox.TalkingBookTool.RecordingModeSplitOrClearExistingRecordingTextBox",
                    "Please split or clear the existing recording in this text box before changing modes.",
                    ""
                )
                .done(localizedNotification => {
                    toastr.warning(localizedNotification);
                });
        }
    }

    private notifyRecordingModeControlDisabledXMatter() {
        theOneLocalizationManager
            .asyncGetText(
                "EditTab.Toolbox.TalkingBookTool.RecordingModeXMatter",
                "Sorry, front and back-matter pages must be recorded by sentences.",
                ""
            )
            .done(localizedNotification => {
                toastr.warning(localizedNotification);
            });
    }

    public getPageFrame(): HTMLIFrameElement {
        return <HTMLIFrameElement>parent.window.document.getElementById("page");
    }

    // The body of the editable page, a root for searching for document content.
    public getPageDocBody(): HTMLElement | null {
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

        if (page.length <= 0) {
            // The first one is probably the right one when this case is triggered, but even if not, it's better than nothing.
            this.setCurrentAudioElementToFirstAudioElement();
            page = this.getPageDocBodyJQuery();
        }

        const current = page.find(".ui-audioCurrent");
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
        if (!audioSentences || audioSentences.length == 0) {
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
                    const element = collection.item(i);
                    if (element) {
                        audioSegments.push(element);
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
    public getCurrentTextBox(): HTMLElement | null {
        const pageBody = this.getPageDocBody();
        if (!pageBody) return pageBody;

        let audioCurrentElements = pageBody.getElementsByClassName(
            kAudioCurrent
        );

        if (audioCurrentElements.length == 0) {
            // Oops, ui-audioCurrent not set on anything. Just going to have to stick it onto the first element.
            this.setCurrentAudioElementToFirstAudioElement();
            audioCurrentElements = pageBody.getElementsByClassName(
                kAudioCurrent
            );

            if (audioCurrentElements.length <= 0) {
                return null;
            }
        }

        // TODO: Maybe this should just never return null?  It's not expected to be valid. Maybe we should just fail fast with exception.
        //       It would make the code in the callers so much cleaner, less repetitive, and easier to work with.
        const currentTextBox = audioCurrentElements.item(0);
        console.assert(currentTextBox, "CurrentTextBox should not be null");
        return <HTMLElement | null>(
            this.getParentTextBoxOfElement(audioCurrentElements.item(0))
        );
    }

    private getParentTextBoxOfElement(element: Element | null): Element | null {
        let currToExamine: Element | null = element;

        while (currToExamine && !this.isRecordableDiv(currToExamine)) {
            // Recursively go up the tree to find the enclosing div, if necessary
            currToExamine = currToExamine.parentElement; // Will return null if no parent
        }

        return currToExamine;
    }

    // Determines which element should receive the Current Highlight
    //   (Notably, checks to see if we should move from the existing Current Highlight (determined via CSS classes) to the actively focused element instead.)
    private getWhichTextBoxShouldReceiveHighlight() {
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
            return pageFrame.contentDocument.activeElement;
        } else {
            return this.getCurrentTextBox();
        }
    }

    // Moves the currently highlighted element to specified text box
    // Note: as name promises, this moves it to a text box (a div) not an element (either an audio-sentence div or audio-sentence span).
    // This happens even if audioRecordingMode=Sentence. It is caller's responsibility to either ensure that this operation is valid,
    // or to be able to handle the resulting state (possibly in Sentence mode but a div is selected), for example by calling updateMarkupForCurrentText() to re-update the state.
    private moveCurrentHighlightToTextBox(newSelectedTextBox): void {
        const pageBody = this.getPageDocBody();
        if (!pageBody) return; // Just give up, not much we can do from here.

        const audioCurrentList = pageBody.getElementsByClassName(kAudioCurrent);
        this.setCurrentAudioElementFromJQuery(
            $(audioCurrentList),
            $(newSelectedTextBox)
        );
    }

    public newPageReady() {
        // FYI, it is possible for newPageReady to be called without updateMarkup() being called
        // (e.g. when opening the toolbox with an empty text box).
        this.initializeForMarkup();
        this.changeStateAndSetExpected("");
    }

    // Should be called when whatever tool uses this is about to be hidden (e.g., changing tools or closing toolbox)
    public hideTool() {
        this.isShowing = false;
        this.stopListeningForLevels();

        // Need to clear out any state. The next time this tool gets reopened, there is no guarantee that it will be reopened in the same context.
        this.audioRecordingMode = AudioRecordingMode.Unknown;
    }

    // Called on initial setup and on toolbox updateMarkup(), including when a new page is created with Talking Book tab open
    public updateMarkupForCurrentText(
        audioPlaybackMode,
        allowUpdateOfCurrent: boolean = true
    ): void {
        // Basic outline:
        // * This function gets called when the user types something, and upon initialization of the talking book tool too if there is a non-empty recordable text box
        // * First, see if we should update the Current Highlight to the element with the active focus instead.
        // * Then, ensure all the state is initialized.
        // * Now that we're finally ready, change the HTML markup with the audio-sentence classes, ids, etc.
        // * Adjust the Current Highlight appropriately.
        // * (keep adjusting the current highlight to) fight with timing issues

        const currentTextBox = this.getCurrentTextBox();

        // Enhance: it would be nice/significantly more intuitive if this (or a stripped-down version that just moves the highlight/audio recording mode) could run when the mouse focus changes.
        if (allowUpdateOfCurrent) {
            const selectedTextBox = this.getWhichTextBoxShouldReceiveHighlight();

            if (currentTextBox != selectedTextBox) {
                // Note: This may temporarily put things into a funny state. We ask to move the highlight to the whole div regardless of what the recording mode is.
                // We have a bit of a chicken and egg problem here. The new recording mode still needs to be determined, and the audio-sentence markup is not applied yet either,
                // but it's easier to determine the recording mode and apply the audio-sentence markup if we move the current highlight first than vice-versa.
                // Calling InitializeForMarkup (called by updateMarkupForCurrentText) and updateMarkupForCurrentText should get us back into a 100% valid state.
                this.moveCurrentHighlightToTextBox(selectedTextBox);
                this.audioRecordingMode = AudioRecordingMode.Unknown; // Clear the mode to signal that re-doing initialization is necessary.

                this.updateMarkupForCurrentText(
                    AudioRecordingMode.Unknown,
                    false
                );
                return;
            }
        }

        if (!currentTextBox) {
            return;
        }

        this.isShowing = true;

        if (audioPlaybackMode == AudioRecordingMode.Unknown) {
            this.initializeForMarkup();
            // The reason we force the new playback mode to be sentence is that
            // The only way it is allowed to reach PlaybackMode = TextBox is immediately after doing a recording by TextBox mode.
            // So here, (e.g. when you type into a text box for the first time), we want you to be in Recording=*,Playback=Sentence mode.
            this.updateMarkupForCurrentText(AudioRecordingMode.Sentence, false);
            return;
        }

        // In addition to us processing currentTextBox, also add any unprocessed divs
        const recordableDivs = this.getRecordableDivs();
        const unprocessedRecordables = recordableDivs.filter(
            ":not([data-audioRecordingMode])"
        );
        let unionedElementsToProcess = $(currentTextBox).add(
            unprocessedRecordables
        );

        if (unionedElementsToProcess.length === 0) {
            // no editable text on this page.
            this.changeStateAndSetExpected("");
            return;
        }

        this.makeAudioSentenceElements(
            unionedElementsToProcess,
            audioPlaybackMode
        );

        const thisClass = this;

        //thisClass.setStatus('record', Status.Expected);
        thisClass.levelCanvas = $("#audio-meter").get()[0];

        // This synchronous call probably makes the flashing problem even more likely compared to delaying it but I think it is helpful if the state is being rapidly modified.
        this.setCurrentAudioElementBasedOnRecordingMode(currentTextBox); // TODO: Probably not actually necessary to re-apply every time. Refactor it elsewhere.
        this.changeStateAndSetExpected("record");

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

        let delayInMilliseconds = 20;
        while (delayInMilliseconds < 1000) {
            // Keep setting the current highlight for an additional roughly 1 second
            setTimeout(() => {
                this.setCurrentAudioElementBasedOnRecordingMode(currentTextBox);
            }, delayInMilliseconds);

            delayInMilliseconds *= 2;
        }
    }

    private isFullyInitialized(): boolean {
        return this.audioRecordingMode != AudioRecordingMode.Unknown;
    }

    public setCurrentAudioElementBasedOnRecordingMode(
        element: Element,
        isEarlyAbortEnabled: boolean = false
    ) {
        if (isEarlyAbortEnabled && !this.isShowing) {
            // e.g., the tool was closed during the timeout interval. We must not apply any markup
            return;
        }

        if (this.audioRecordingMode == AudioRecordingMode.Sentence) {
            this.setCurrentAudioElementToFirstAudioSentenceWithinElement(
                element,
                isEarlyAbortEnabled
            );
            return;
        }
        console.assert(this.audioRecordingMode == AudioRecordingMode.TextBox);

        const audioCurrentList = this.getPageDocBodyJQuery().find(
            ".ui-audioCurrent"
        );

        if (isEarlyAbortEnabled && audioCurrentList.length >= 1) {
            // audioCurrent highlight is already working, so don't bother trying to fix anything up.
            // I think this probably can also help if you rapidly check and uncheck the checkbox, then click Next.
            // We wouldn't want multiple things highlighted, or end up pointing to the wrong thing, etc.
            return;
        }
        let audioCurrent: HTMLElement | null = null;
        if (audioCurrentList.length >= 1) {
            audioCurrent = audioCurrentList[0];
        }
        const changeTo = this.getParentTextBoxOfElement(element);
        if (changeTo) {
            this.setCurrentAudioElementFrom(audioCurrent, changeTo);
        }
    }

    public setCurrentAudioElementToFirstAudioSentenceWithinElement(
        element: Element,
        isEarlyAbortEnabled: boolean = false
    ) {
        if (isEarlyAbortEnabled && !this.isShowing) {
            // e.g., the tool was closed during the timeout interval. We must not apply any markup
            return;
        }

        const audioCurrentList = this.getPageDocBodyJQuery().find(
            ".ui-audioCurrent"
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

        this.setCurrentAudioElementFromJQuery(
            audioCurrentList,
            $(<any>changeTo)
        );
    }

    public setCurrentAudioElementToFirstAudioElement() {
        const audioCurrentList = this.getPageDocBodyJQuery().find(
            ".ui-audioCurrent"
        );

        const firstSentence = this.getPageDocBodyJQuery()
            .find(kAudioSentenceClassSelector)
            .first();
        if (firstSentence.length === 0) {
            // no recordable sentence found.
            return;
        }

        this.setCurrentAudioElementFromJQuery(audioCurrentList, firstSentence); // typically first arg matches nothing.

        // In Sentence/Sentence mode: OK to move.
        // Text/Sentence mode: Ok to swap.
        // In Text/Text mode: Not OK to swap because we don't support Sentence/Text mode.
        // This possibly moved the highlight to a different text box, so we need to re-compute settings.
        if (
            this.audioRecordingMode == AudioRecordingMode.TextBox &&
            this.getCurrentPlaybackMode() == AudioRecordingMode.TextBox &&
            this.getAudioFilePresent()
        ) {
            this.disableRecordingModeControl();
        } else {
            this.enableRecordingModeControl();
        }
    }

    // This gets invoked via websocket message. It draws a series of bars
    // (reminiscent of leds in a hardware level meter) within the canvas in the
    //  top right of the bubble to indicate the current peak level.
    public setStaticPeakLevel(level: string): void {
        if (!this.levelCanvas) return; // just in case C# calls this unexpectedly
        var ctx = this.levelCanvas.getContext("2d");
        if (!ctx) return;
        // Erase the whole canvas
        var height = 15;
        var width = 80;

        ctx.fillStyle = window.getComputedStyle(
            this.levelCanvas.parentElement!
        ).backgroundColor!;

        ctx.fillRect(0, 0, width, height);

        // Draw the appropriate number and color of bars
        var gap = 2;
        var barWidth = 4;
        var interval = gap + barWidth;
        var bars = Math.floor(width / interval);
        var loudBars = 2;
        var quietBars = 2;
        var mediumBars = Math.max(bars - (loudBars + quietBars), 1);
        var showBars = Math.floor(bars * parseFloat(level)); // + 1;
        ctx.fillStyle = "#D2D2D2"; // should match text color or "#00FF00";
        for (var i = 0; i < showBars; i++) {
            var left = interval * i;
            if (i >= quietBars) ctx.fillStyle = "#0C8597";
            if (i >= quietBars + mediumBars) ctx.fillStyle = "#FF0000"; //red
            ctx.fillRect(left, 0, barWidth, height);
        }
    }

    // from http://stackoverflow.com/questions/105034/create-guid-uuid-in-javascript
    private createUuid(): string {
        // http://www.ietf.org/rfc/rfc4122.txt
        var s: string[] = [];
        var hexDigits = "0123456789abcdef";
        for (var i = 0; i < 36; i++) {
            s[i] = hexDigits.substr(Math.floor(Math.random() * 0x10), 1);
        }
        s[14] = "4"; // bits 12-15 of the time_hi_and_version field to 0010
        s[19] = hexDigits.substr((s[19].charCodeAt(0) & 0x3) | 0x8, 1); // bits 6-7 of the clock_seq_hi_and_reserved to 01
        s[8] = s[13] = s[18] = s[23] = "-";

        var uuid = s.join("");
        return uuid;
    }

    private md5(message): string {
        var HEX_CHARS = "0123456789abcdef".split("");
        var EXTRA = [128, 32768, 8388608, -2147483648];
        var blocks: number[] = [];

        var h0,
            h1,
            h2,
            h3,
            a,
            b,
            c,
            d,
            bc,
            da,
            code,
            first = true,
            end = false,
            index = 0,
            i,
            start = 0,
            bytes = 0,
            length = message.length;
        blocks[16] = 0;
        var SHIFT = [0, 8, 16, 24];

        do {
            blocks[0] = blocks[16];
            blocks[16] = blocks[1] = blocks[2] = blocks[3] = blocks[4] = blocks[5] = blocks[6] = blocks[7] = blocks[8] = blocks[9] = blocks[10] = blocks[11] = blocks[12] = blocks[13] = blocks[14] = blocks[15] = 0;
            for (i = start; index < length && i < 64; ++index) {
                code = message.charCodeAt(index);
                if (code < 0x80) {
                    blocks[i >> 2] |= code << SHIFT[i++ & 3];
                } else if (code < 0x800) {
                    blocks[i >> 2] |= (0xc0 | (code >> 6)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | (code & 0x3f)) << SHIFT[i++ & 3];
                } else if (code < 0xd800 || code >= 0xe000) {
                    blocks[i >> 2] |= (0xe0 | (code >> 12)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |=
                        (0x80 | ((code >> 6) & 0x3f)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | (code & 0x3f)) << SHIFT[i++ & 3];
                } else {
                    code =
                        0x10000 +
                        (((code & 0x3ff) << 10) |
                            (message.charCodeAt(++index) & 0x3ff));
                    blocks[i >> 2] |= (0xf0 | (code >> 18)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |=
                        (0x80 | ((code >> 12) & 0x3f)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |=
                        (0x80 | ((code >> 6) & 0x3f)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | (code & 0x3f)) << SHIFT[i++ & 3];
                }
            }
            bytes += i - start;
            start = i - 64;
            if (index == length) {
                blocks[i >> 2] |= EXTRA[i & 3];
                ++index;
            }
            if (index > length && i < 56) {
                blocks[14] = bytes << 3;
                end = true;
            }

            if (first) {
                a = blocks[0] - 680876937;
                a = (((a << 7) | (a >>> 25)) - 271733879) << 0;
                d = (-1732584194 ^ (a & 2004318071)) + blocks[1] - 117830708;
                d = (((d << 12) | (d >>> 20)) + a) << 0;
                c =
                    (-271733879 ^ (d & (a ^ -271733879))) +
                    blocks[2] -
                    1126478375;
                c = (((c << 17) | (c >>> 15)) + d) << 0;
                b = (a ^ (c & (d ^ a))) + blocks[3] - 1316259209;
                b = (((b << 22) | (b >>> 10)) + c) << 0;
            } else {
                a = h0;
                b = h1;
                c = h2;
                d = h3;
                a += (d ^ (b & (c ^ d))) + blocks[0] - 680876936;
                a = (((a << 7) | (a >>> 25)) + b) << 0;
                d += (c ^ (a & (b ^ c))) + blocks[1] - 389564586;
                d = (((d << 12) | (d >>> 20)) + a) << 0;
                c += (b ^ (d & (a ^ b))) + blocks[2] + 606105819;
                c = (((c << 17) | (c >>> 15)) + d) << 0;
                b += (a ^ (c & (d ^ a))) + blocks[3] - 1044525330;
                b = (((b << 22) | (b >>> 10)) + c) << 0;
            }

            a += (d ^ (b & (c ^ d))) + blocks[4] - 176418897;
            a = (((a << 7) | (a >>> 25)) + b) << 0;
            d += (c ^ (a & (b ^ c))) + blocks[5] + 1200080426;
            d = (((d << 12) | (d >>> 20)) + a) << 0;
            c += (b ^ (d & (a ^ b))) + blocks[6] - 1473231341;
            c = (((c << 17) | (c >>> 15)) + d) << 0;
            b += (a ^ (c & (d ^ a))) + blocks[7] - 45705983;
            b = (((b << 22) | (b >>> 10)) + c) << 0;
            a += (d ^ (b & (c ^ d))) + blocks[8] + 1770035416;
            a = (((a << 7) | (a >>> 25)) + b) << 0;
            d += (c ^ (a & (b ^ c))) + blocks[9] - 1958414417;
            d = (((d << 12) | (d >>> 20)) + a) << 0;
            c += (b ^ (d & (a ^ b))) + blocks[10] - 42063;
            c = (((c << 17) | (c >>> 15)) + d) << 0;
            b += (a ^ (c & (d ^ a))) + blocks[11] - 1990404162;
            b = (((b << 22) | (b >>> 10)) + c) << 0;
            a += (d ^ (b & (c ^ d))) + blocks[12] + 1804603682;
            a = (((a << 7) | (a >>> 25)) + b) << 0;
            d += (c ^ (a & (b ^ c))) + blocks[13] - 40341101;
            d = (((d << 12) | (d >>> 20)) + a) << 0;
            c += (b ^ (d & (a ^ b))) + blocks[14] - 1502002290;
            c = (((c << 17) | (c >>> 15)) + d) << 0;
            b += (a ^ (c & (d ^ a))) + blocks[15] + 1236535329;
            b = (((b << 22) | (b >>> 10)) + c) << 0;
            a += (c ^ (d & (b ^ c))) + blocks[1] - 165796510;
            a = (((a << 5) | (a >>> 27)) + b) << 0;
            d += (b ^ (c & (a ^ b))) + blocks[6] - 1069501632;
            d = (((d << 9) | (d >>> 23)) + a) << 0;
            c += (a ^ (b & (d ^ a))) + blocks[11] + 643717713;
            c = (((c << 14) | (c >>> 18)) + d) << 0;
            b += (d ^ (a & (c ^ d))) + blocks[0] - 373897302;
            b = (((b << 20) | (b >>> 12)) + c) << 0;
            a += (c ^ (d & (b ^ c))) + blocks[5] - 701558691;
            a = (((a << 5) | (a >>> 27)) + b) << 0;
            d += (b ^ (c & (a ^ b))) + blocks[10] + 38016083;
            d = (((d << 9) | (d >>> 23)) + a) << 0;
            c += (a ^ (b & (d ^ a))) + blocks[15] - 660478335;
            c = (((c << 14) | (c >>> 18)) + d) << 0;
            b += (d ^ (a & (c ^ d))) + blocks[4] - 405537848;
            b = (((b << 20) | (b >>> 12)) + c) << 0;
            a += (c ^ (d & (b ^ c))) + blocks[9] + 568446438;
            a = (((a << 5) | (a >>> 27)) + b) << 0;
            d += (b ^ (c & (a ^ b))) + blocks[14] - 1019803690;
            d = (((d << 9) | (d >>> 23)) + a) << 0;
            c += (a ^ (b & (d ^ a))) + blocks[3] - 187363961;
            c = (((c << 14) | (c >>> 18)) + d) << 0;
            b += (d ^ (a & (c ^ d))) + blocks[8] + 1163531501;
            b = (((b << 20) | (b >>> 12)) + c) << 0;
            a += (c ^ (d & (b ^ c))) + blocks[13] - 1444681467;
            a = (((a << 5) | (a >>> 27)) + b) << 0;
            d += (b ^ (c & (a ^ b))) + blocks[2] - 51403784;
            d = (((d << 9) | (d >>> 23)) + a) << 0;
            c += (a ^ (b & (d ^ a))) + blocks[7] + 1735328473;
            c = (((c << 14) | (c >>> 18)) + d) << 0;
            b += (d ^ (a & (c ^ d))) + blocks[12] - 1926607734;
            b = (((b << 20) | (b >>> 12)) + c) << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[5] - 378558;
            a = (((a << 4) | (a >>> 28)) + b) << 0;
            d += (bc ^ a) + blocks[8] - 2022574463;
            d = (((d << 11) | (d >>> 21)) + a) << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[11] + 1839030562;
            c = (((c << 16) | (c >>> 16)) + d) << 0;
            b += (da ^ c) + blocks[14] - 35309556;
            b = (((b << 23) | (b >>> 9)) + c) << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[1] - 1530992060;
            a = (((a << 4) | (a >>> 28)) + b) << 0;
            d += (bc ^ a) + blocks[4] + 1272893353;
            d = (((d << 11) | (d >>> 21)) + a) << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[7] - 155497632;
            c = (((c << 16) | (c >>> 16)) + d) << 0;
            b += (da ^ c) + blocks[10] - 1094730640;
            b = (((b << 23) | (b >>> 9)) + c) << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[13] + 681279174;
            a = (((a << 4) | (a >>> 28)) + b) << 0;
            d += (bc ^ a) + blocks[0] - 358537222;
            d = (((d << 11) | (d >>> 21)) + a) << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[3] - 722521979;
            c = (((c << 16) | (c >>> 16)) + d) << 0;
            b += (da ^ c) + blocks[6] + 76029189;
            b = (((b << 23) | (b >>> 9)) + c) << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[9] - 640364487;
            a = (((a << 4) | (a >>> 28)) + b) << 0;
            d += (bc ^ a) + blocks[12] - 421815835;
            d = (((d << 11) | (d >>> 21)) + a) << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[15] + 530742520;
            c = (((c << 16) | (c >>> 16)) + d) << 0;
            b += (da ^ c) + blocks[2] - 995338651;
            b = (((b << 23) | (b >>> 9)) + c) << 0;
            a += (c ^ (b | ~d)) + blocks[0] - 198630844;
            a = (((a << 6) | (a >>> 26)) + b) << 0;
            d += (b ^ (a | ~c)) + blocks[7] + 1126891415;
            d = (((d << 10) | (d >>> 22)) + a) << 0;
            c += (a ^ (d | ~b)) + blocks[14] - 1416354905;
            c = (((c << 15) | (c >>> 17)) + d) << 0;
            b += (d ^ (c | ~a)) + blocks[5] - 57434055;
            b = (((b << 21) | (b >>> 11)) + c) << 0;
            a += (c ^ (b | ~d)) + blocks[12] + 1700485571;
            a = (((a << 6) | (a >>> 26)) + b) << 0;
            d += (b ^ (a | ~c)) + blocks[3] - 1894986606;
            d = (((d << 10) | (d >>> 22)) + a) << 0;
            c += (a ^ (d | ~b)) + blocks[10] - 1051523;
            c = (((c << 15) | (c >>> 17)) + d) << 0;
            b += (d ^ (c | ~a)) + blocks[1] - 2054922799;
            b = (((b << 21) | (b >>> 11)) + c) << 0;
            a += (c ^ (b | ~d)) + blocks[8] + 1873313359;
            a = (((a << 6) | (a >>> 26)) + b) << 0;
            d += (b ^ (a | ~c)) + blocks[15] - 30611744;
            d = (((d << 10) | (d >>> 22)) + a) << 0;
            c += (a ^ (d | ~b)) + blocks[6] - 1560198380;
            c = (((c << 15) | (c >>> 17)) + d) << 0;
            b += (d ^ (c | ~a)) + blocks[13] + 1309151649;
            b = (((b << 21) | (b >>> 11)) + c) << 0;
            a += (c ^ (b | ~d)) + blocks[4] - 145523070;
            a = (((a << 6) | (a >>> 26)) + b) << 0;
            d += (b ^ (a | ~c)) + blocks[11] - 1120210379;
            d = (((d << 10) | (d >>> 22)) + a) << 0;
            c += (a ^ (d | ~b)) + blocks[2] + 718787259;
            c = (((c << 15) | (c >>> 17)) + d) << 0;
            b += (d ^ (c | ~a)) + blocks[9] - 343485551;
            b = (((b << 21) | (b >>> 11)) + c) << 0;

            if (first) {
                h0 = (a + 1732584193) << 0;
                h1 = (b - 271733879) << 0;
                h2 = (c - 1732584194) << 0;
                h3 = (d + 271733878) << 0;
                first = false;
            } else {
                h0 = (h0 + a) << 0;
                h1 = (h1 + b) << 0;
                h2 = (h2 + c) << 0;
                h3 = (h3 + d) << 0;
            }
        } while (!end);

        var hex = HEX_CHARS[(h0 >> 4) & 0x0f] + HEX_CHARS[h0 & 0x0f];
        hex += HEX_CHARS[(h0 >> 12) & 0x0f] + HEX_CHARS[(h0 >> 8) & 0x0f];
        hex += HEX_CHARS[(h0 >> 20) & 0x0f] + HEX_CHARS[(h0 >> 16) & 0x0f];
        hex += HEX_CHARS[(h0 >> 28) & 0x0f] + HEX_CHARS[(h0 >> 24) & 0x0f];
        hex += HEX_CHARS[(h1 >> 4) & 0x0f] + HEX_CHARS[h1 & 0x0f];
        hex += HEX_CHARS[(h1 >> 12) & 0x0f] + HEX_CHARS[(h1 >> 8) & 0x0f];
        hex += HEX_CHARS[(h1 >> 20) & 0x0f] + HEX_CHARS[(h1 >> 16) & 0x0f];
        hex += HEX_CHARS[(h1 >> 28) & 0x0f] + HEX_CHARS[(h1 >> 24) & 0x0f];
        hex += HEX_CHARS[(h2 >> 4) & 0x0f] + HEX_CHARS[h2 & 0x0f];
        hex += HEX_CHARS[(h2 >> 12) & 0x0f] + HEX_CHARS[(h2 >> 8) & 0x0f];
        hex += HEX_CHARS[(h2 >> 20) & 0x0f] + HEX_CHARS[(h2 >> 16) & 0x0f];
        hex += HEX_CHARS[(h2 >> 28) & 0x0f] + HEX_CHARS[(h2 >> 24) & 0x0f];
        hex += HEX_CHARS[(h3 >> 4) & 0x0f] + HEX_CHARS[h3 & 0x0f];
        hex += HEX_CHARS[(h3 >> 12) & 0x0f] + HEX_CHARS[(h3 >> 8) & 0x0f];
        hex += HEX_CHARS[(h3 >> 20) & 0x0f] + HEX_CHARS[(h3 >> 16) & 0x0f];
        hex += HEX_CHARS[(h3 >> 28) & 0x0f] + HEX_CHARS[(h3 >> 24) & 0x0f];
        return hex;
    }

    // AudioRecordingMode=Sentence: We want to make out of each sentence in root a span which has a unique ID.
    // AudioRecordingMode=TextBox: We want to turn the bloom-editable text box into the unit which will be recorded. (It needs a unique ID too). No spans will be created though
    // If the text is already so marked up, we want to keep the existing ids
    // AND the recordingID checksum attribute (if any) that indicates what
    // version of the text was last recorded.
    // makeAudioSentenceElementsLeaf does this for roots which don't have children (except a few
    // special cases); this root method scans down and does it for each such child
    // in a root (possibly the root itself, if it has no children).
    public makeAudioSentenceElements(
        rootElementList: JQuery,
        audioPlaybackMode: AudioRecordingMode = this.audioRecordingMode
    ): void {
        // Preconditions:
        //   bloom-editable ids are not currently used / will be robustly handled in the future / are agnostic to the specific value and format
        //   The first node(s) underneath a bloom-editable should always be <p> elements

        rootElementList.each((index: number, root: Element) => {
            if (audioPlaybackMode == AudioRecordingMode.Sentence) {
                if (this.isRootRecordableDiv(root)) {
                    this.persistRecordingMode(root);

                    // Cleanup markup from AudioRecordingMode=TextBox
                    root.classList.remove(kAudioSentence);
                }
            } else if (audioPlaybackMode == AudioRecordingMode.TextBox) {
                if (this.isRootRecordableDiv(root)) {
                    // Save the RECORDING (not the playback) setting  used, so we can load it properly later
                    this.persistRecordingMode(root);

                    // Cleanup markup from AudioRecordingMode=Sentence
                    $(root)
                        .find(kAudioSentenceClassSelector)
                        .each((index, element) => {
                            if (!element.classList.contains("bloom-editable")) {
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
                        root.id = this.createValidXhtmlUniqueId();
                    }

                    // All done, no need to process any of the remaining children
                    return;
                } else if (
                    $(root).find(kBloomEditableTextBoxSelector).length <= 0
                ) {
                    // Trust that it got setup correctly, which if so means there is nothing to do for anything else
                    return;
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
                    $(child).replaceWith($(child).html()); // clean up.
                    this.makeAudioSentenceElements(
                        rootElementList,
                        audioPlaybackMode
                    ); // start over.
                    return;
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
                    this.makeAudioSentenceElements($(child), audioPlaybackMode);
                }
            }

            if (!processedChild) {
                // root is a leaf; process its actual content
                this.makeAudioSentenceElementsLeaf($(root));
            }
        });
        // Review: is there a need to handle elements that contain both sentence text AND child elements with their own text?
    }

    // The goal for existing markup is that if any existing audio-sentence span has an md5 that matches the content of a
    // current sentence, we want to preserve the association between that content and ID (and possibly recording).
    // Where there aren't exact matches, but there are existing audio-sentence spans, we keep the ids as far as possible,
    // just using the original order, since it is possible we have a match and only spelling or punctuation changed.
    // We also attempt to use any sentence IDs specified by this.sentenceToIdListMap
    private makeAudioSentenceElementsLeaf(elt: JQuery): void {
        // When all text is deleted, we get in a temporary state with no paragraph elements, so the root editable div
        // may be processed...and if this happens during editing the format button may be present. The body of this function
        // will do weird things with it (wrap it in a sentence span, for example) so the easiest thing is to remove
        // it at the start and reinstate it at the end. Fortunately its position is predictable. But I wish this
        // otherwise fairly generic code didn't have to know about it.
        const formatButton = elt.find("#formatButton");
        formatButton.remove(); // nothing happens if not found

        const markedSentences = elt.find(kAudioSentenceClassSelector);
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
            elt.html()
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
            if (this.isRecordable(fragment)) {
                const currentMd5 = this.md5(fragment.text);
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
                        newId = this.createValidXhtmlUniqueId();
                    }
                }
                newHtml +=
                    '<span id= "' +
                    newId +
                    '" class="' +
                    kAudioSentence +
                    '"' +
                    newMd5 +
                    ">" +
                    htmlFragment.text +
                    "</span>";
            }
        }

        // set the html
        elt.html(newHtml);
        elt.append(formatButton);
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
        text = text.replace(/  /g, " "); // Handle consecutive spaces

        return text;
    }

    private createValidXhtmlUniqueId(): string {
        let newId = this.createUuid();
        if (/^\d/.test(newId)) newId = "i" + newId; // valid ID in XHTML can't start with digit

        return newId;
    }

    private deleteElementAndPushChildNodesIntoParent(element) {
        if (element == null) {
            return;
        }

        const parent = element.parentElement;

        const childNodesCopy = Array.prototype.slice.call(element.childNodes); // Create a copy because e.childNodes is getting modified as we go
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
        var test = fragment.text.replace(/<br *[^>]*\/?>/g, " ");
        // and some may contain only nbsp
        test = test.replace("&nbsp;", " ");
        if (this.isWhiteSpace(test)) return false;
        return this.isTextOrHtmlWithText(test);
    }

    private isTextOrHtmlWithText(textOrHtml: string): boolean {
        var parser = new DOMParser();
        var doc = parser.parseFromString(textOrHtml, "text/html");
        if (doc && doc.documentElement) {
            //paranoia
            // on error, parseFromString returns a document with a parseerror element
            // rather than throwing an exception, so check for that
            if (doc.getElementsByTagName("parsererror").length > 0) {
                return false;
            }
            // textContent is the aggregation of the text nodes of all children
            var content = doc.documentElement.textContent;
            return !this.isWhiteSpace(content ? content : "");
        }
        return false;
    }

    private isWhiteSpace(test: string): boolean {
        if (test.match(/^\s*$/)) return true;
        return false;
    }

    // ------------ State Machine ----------------

    private changeStateAndSetExpected(
        expectedVerb: string,
        numRetriesRemaining: number = 1
    ) {
        console.log("changeState(" + expectedVerb + ")");

        // Call with "" verb if there's nothing specific to highlight, just need to check if these controls should be disabled.
        // (e.g. when we have found that the current page has no divs with recording content, and we may possible want to disable
        // the audio recording controls.)
        if (expectedVerb == "") {
            this.disableInteraction();
            // Whether we disable the Recording Mode control depends on whether we COULD have text
            // on this page (i.e. is there a textbox) and on whether this is an xMatter page.  (We
            // require sentence-by-sentence recording on xMatter pages due to the nature of their
            // content.)
            this.enableRecordingModeIfAppropriate();
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
            this.getPageDocBodyJQuery().find(".ui-audioCurrent").length === 0 &&
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
                this.setCurrentAudioElementToFirstAudioElement();
                this.changeStateAndSetExpected(
                    expectedVerb,
                    numRetriesRemaining - 1
                );
                return;
            } else {
                // We have reached an error state and attempts to self-correct it haven't
                // succeeded either. :(
                this.disableInteraction();
                return;
            }
        }

        this.setEnabledOrExpecting("record", expectedVerb);

        const isSplitButtonValid =
            this.audioRecordingMode == AudioRecordingMode.TextBox;
        const currentPlaybackMode = this.getCurrentPlaybackMode();

        //set play, clear, and split buttons based on whether we have an audio file for this element (or in the case of a text box, anything within this element)
        const currentElementIds: string[] = [];
        const audioSentenceCollectionCurrElem = this.getAudioSegmentsWithinElement(
            this.getCurrentHighlight()!
        );
        for (let i = 0; i < audioSentenceCollectionCurrElem.length; ++i) {
            const audioSentenceElement = audioSentenceCollectionCurrElem[i];
            if (audioSentenceElement) {
                const id = audioSentenceElement.getAttribute("id");
                if (id) {
                    currentElementIds.push(id);
                }
            }
        }

        axios
            .get(
                "/bloom/api/audio/checkForAnyRecording?ids=" +
                    currentElementIds.toString()
            )
            .then(response => {
                if (response.statusText == "OK") {
                    // Set clear
                    this.setStatus("clear", Status.Enabled);

                    // Set play
                    this.setEnabledOrExpecting("play", expectedVerb);

                    // Set split
                    if (isSplitButtonValid) {
                        if (currentPlaybackMode == AudioRecordingMode.TextBox) {
                            this.setEnabledOrExpecting("split", expectedVerb);
                        } else {
                            // RecordingMode=TextBox, PlaybackMode=Sentence.
                            // Two cases can lead here:
                            //   1) No audio was ever recorded in TextBox mode. In which case, we definitely want this button disabled.
                            //   2) The text was recorded in TextBox mode, and now has already been split, not much point pushing them to split again until the modify it meaningfully. Push to Next instead.
                            this.setStatus("split", Status.Disabled);
                        }
                    } else {
                        this.setStatus("split", Status.Disabled);
                    }
                } else {
                    this.setStatus("clear", Status.Disabled);
                    this.setStatus("play", Status.Disabled);
                    this.setStatus("split", Status.Disabled);
                }
            })
            .catch(response => {
                // Note: If there is no audio, it returns Request.Failed AKA it actually goes into the catch!!!
                this.setStatus("clear", Status.Disabled);
                this.setStatus("play", Status.Disabled);
                this.setStatus("split", Status.Disabled);
            });

        // Set Next and Prev buttons
        if (this.getNextAudioElement()) {
            let shouldNextButtonOverrideSplit: boolean = false;

            if (expectedVerb == "split") {
                if (
                    !isSplitButtonValid ||
                    currentPlaybackMode != AudioRecordingMode.TextBox
                ) {
                    shouldNextButtonOverrideSplit = true;
                }
            }

            if (!shouldNextButtonOverrideSplit) {
                // Normally we just want to do this
                this.setEnabledOrExpecting("next", expectedVerb);
            } else {
                // Alternatively, if expectedVerb was split and we want Next to override Split, then it should be Expected instead of merely Enabled.
                this.setStatus("next", Status.Expected);
            }
        } else {
            this.setStatus("next", Status.Disabled);
        }

        if (this.getPreviousAudioElement()) {
            this.setStatus("prev", Status.Enabled);
        } else {
            this.setStatus("prev", Status.Disabled);
        }

        // Set listen button based on whether we have an audio at all for this page
        const ids: any[] = [];
        this.getAudioElements().each(function() {
            ids.push(this.id);
        });
        axios
            .get("/bloom/api/audio/checkForAnyRecording?ids=" + ids)
            .then(response => {
                if (response.statusText == "OK") {
                    this.setStatus("listen", Status.Enabled);
                } else {
                    this.setStatus("listen", Status.Disabled);
                }
            })
            .catch(response => {
                // This handles the case where AudioRecording.HandleCheckForAnyRecording() (in C#)
                // sends back a request.Failed("no audio") and thereby avoids an uncaught js exception.
                this.setStatus("listen", Status.Disabled);
            });

        // Determine whether the recording mode checkbox should be enabled or not, based on whether any audio files are present
        // for anything in the current text box
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

        axios
            .get(
                "/bloom/api/audio/checkForAnyRecording?ids=" +
                    currentTextBoxIds.toString()
            )
            .then(response => {
                if (
                    response.statusText == "OK" &&
                    this.audioRecordingMode == AudioRecordingMode.TextBox &&
                    this.getCurrentPlaybackMode() == AudioRecordingMode.TextBox
                ) {
                    // There is some audio set to play back in Text Box mode. If we switch the Record Mode to sentence mode, it also implies switching the Playback mode to Sentence mode.
                    // Disable the control so that the user can't accidentally lose data during this switch.
                    this.disableRecordingModeControl(!ToolBox.isXmatterPage());
                } else {
                    this.enableRecordingModeControl();
                }
            })
            .catch(response => {
                // Note: If there is no audio, it returns Request.Failed AKA it actually goes into the catch!!!

                // We don't want to enable the checkbox if we are on an xMatter page (BL-6737).
                // It is probably already disabled at this point, but might as well play it safe.
                if (ToolBox.isXmatterPage()) {
                    this.disableRecordingModeControl(false);
                } else {
                    this.enableRecordingModeControl();
                }
            });
    }

    // Enable the recording mode checkbox only for non-xMatter pages that have visible text divs.
    // It doesn't matter whether any of the text divs actually contain text.
    private enableRecordingModeIfAppropriate() {
        // The 'false' parameter here checks for visible text divs, but doesn't check
        // for actual text in them. We already know there aren't any WITH text.
        if (
            !ToolBox.isXmatterPage() &&
            this.getRecordableDivs(false).length > 0
        ) {
            // Enable the control, although we don't currently have any text on this page, because
            // we could add text at some point.
            this.enableRecordingModeControl();
        } else {
            // Disable the control and its notification, since we can't have text on this page.
            this.disableRecordingModeControl(false);
        }
    }

    private setEnabledOrExpecting(verb: string, expectedVerb: string) {
        if (expectedVerb == verb) this.setStatus(verb, Status.Expected);
        else this.setStatus(verb, Status.Enabled);
    }

    private isEnabledOrExpected(verb: string): Boolean {
        return (
            $("#audio-" + verb).hasClass("enabled") ||
            $("#audio-" + verb).hasClass("expected")
        );
    }

    private setStatus(which: string, to: Status): void {
        const buttonElement = document.getElementById(`audio-${which}`);
        if (buttonElement) {
            buttonElement.classList.remove("expected");
            buttonElement.classList.remove("disabled");
            buttonElement.classList.remove("enabled");
            buttonElement.classList.remove("active");

            buttonElement.classList.add(Status[to].toLowerCase());
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
        }
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

    public getAudioFilePresent(): boolean {
        const playElement = document.getElementById("#audio-play");
        if (playElement && playElement.classList.contains("enabled")) {
            return true;
        }
        return false;
    }

    private split(): void {
        BloomApi.get(
            "audioSegmentation/checkAutoSegmentDependencies",
            result => {
                if (result.data === "FALSE") {
                    // The specific missing dependency is only reported in the error log on the C# side.
                    this.handleMissingDependency();
                } else {
                    this.autoSegment();
                }
            }
        );
    }
    // Callback for when the user clicks on the "Auto Segment" button.
    // This will automatically segment the audio to synchronize with the text (a.k.a. forced alignment)
    // The basic steps are:
    // * Split the text into fragments (sentences)
    // * Call API server to split the whole audio file into pieces, one piece per sentence.
    // *   (Black Box internals:  by using Aeneas to find the timing of each sentence start, then FFMPEG to split)
    // * Update the state of the UI to utilize the new files created by API server
    private autoSegment(): void {
        this.audioSplitButton.setAttribute("title", ""); // remove any error tooltips

        // First, check if there's even an audio recorded yet.
        const playButtonElement = document.getElementById("audio-play");
        if (
            playButtonElement &&
            playButtonElement.classList.contains("disabled")
        ) {
            // TODO: Localize after UI finalized
            toastr.warning(
                "Please record audio first before running Auto Segment"
            );

            this.setStatus("split", Status.Disabled); // Remove active/expected highlights
            return;
        }

        const currentTextBox = this.getCurrentHighlight();
        if (!currentTextBox) {
            // At this point, not going to be able to get the ID of the div so we can't figure out how to get the filename...
            // So just give up.
            toastr.error("AutoSegment did not succeed.");
            this.setStatus("split", Status.Enabled); // Remove active/expected highlights
            return;
        }

        const fragmentIdTuples = this.extractFragmentsAndSetSpanIdsForAudioSegmentation();

        if (fragmentIdTuples.length > 0) {
            const inputParameters = {
                audioFilenameBase: currentTextBox.id,
                audioTextFragments: fragmentIdTuples,
                lang: this.getAutoSegmentLanguageCode()
            };

            this.disableInteraction();
            // this.setStatus("split", Status.Active);  // Now we decide to just keep it disabled instaed.
            this.showBusy();

            BloomApi.postJson(
                "audioSegmentation/autoSegmentAudio",
                JSON.stringify(inputParameters),
                result => {
                    this.setStatus("split", Status.Disabled);
                    this.processAutoSegmentResponse(result);
                }
            );
        }
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
                const newId = this.createValidXhtmlUniqueId();

                // Sometimes extraneous newlines can be injected (by CKEditor?). They may get removed later (maybe after the CKEditor reloads when the text box's underlying HTML is modified???)
                // However, some processing needs the text immediately, and others are after the text is cleaned.
                // In order to reconcile the two, just normalize the text immediately.
                fragment.text = AudioRecording.normalizeText(fragment.text);

                fragmentObjects.push(
                    new AudioTextFragment(fragment.text, newId)
                );

                let idList: string[] = [];
                if (fragment.text in this.sentenceToIdListMap) {
                    idList = this.sentenceToIdListMap[fragment.text];
                }
                idList.push(newId); // This is saved so MakeSentenceAudioElementsLeaf can recover it
                this.sentenceToIdListMap[fragment.text] = idList;
            }
        }

        return fragmentObjects;
    }

    // I add a cached version so that it is more verifiable that two calls with the same inputs will definitely return the same outputs.
    public stringToSentences(text: string): TextFragment[] {
        if (text in this.stringToSentencesCache) {
            return this.stringToSentencesCache[text];
        } else {
            const retVal = theOneLibSynphony.stringToSentences(text);
            this.stringToSentencesCache[text] = retVal;
            return retVal;
        }
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
        this.disableRecordingModeControl();
    }

    public processAutoSegmentResponse(
        result: AxiosResponse<any>,
        doneCallback = () => {}
    ): void {
        const isSuccess = result && result.data == true;

        if (isSuccess) {
            // Now that we know the Auto Segmentation succeeded, finally convert into by-sentence mode.

            // Note that this will want to use the sentenceToIdListMap member variable to inform it to re-use the IDs used to create the split audio files
            const allowUpdateOfCurrent = false;
            this.updateMarkupForCurrentText(
                AudioRecordingMode.Sentence,
                allowUpdateOfCurrent
            );

            // Now that we're all done with use sentenceToIdListMap, clear it out so that there's no potential for accidental re-use
            this.sentenceToIdListMap = {};
            this.changeStateAndSetExpected("next");
            this.setStatus("split", Status.Disabled); // No need to run it again if it was successful. (Until the settings are changed).
            this.endBusy();
        } else {
            this.changeStateAndSetExpected("record");
            doneCallback();

            // TODO: Localize
            // If there is a more detailed error from C#, it should be reported via ErrorReport.ReportNonFatal[...]
            toastr.error("AutoSegment did not succeed.");
        }
    }

    private showBusy(): void {
        // Note: if there are any enabled buttons, you need to overwrite theirs too. But disabled buttons will work for free.
        const elementsToUpdate = this.getElementsToUpdateForCursor();

        for (let i = 0; i < elementsToUpdate.length; ++i) {
            const element = elementsToUpdate[i];
            if (element) {
                element.classList.add("cursor-progress");
            }
        }
    }

    private endBusy(): void {
        // Note: if there are any enabled buttons, you need to overwrite theirs too. But disabled buttons will work for free.
        const elementsToUpdate = this.getElementsToUpdateForCursor();

        for (let i = 0; i < elementsToUpdate.length; ++i) {
            const element = elementsToUpdate[i];
            if (element) {
                element.classList.remove("cursor-progress");
            }
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
}

export class AudioTextFragment {
    public fragmentText: string;
    public id: string;

    public constructor(fragmentText: string, id: string) {
        this.fragmentText = fragmentText;
        this.id = id;
    }
}

export let theOneAudioRecorder: AudioRecording;

export function initializeTalkingBookTool() {
    if (theOneAudioRecorder) return;
    theOneAudioRecorder = new AudioRecording();
    theOneAudioRecorder.initializeTalkingBookTool();
}
