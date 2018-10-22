// This class supports creating audio recordings for talking books.
// It is also used by the motion tool when previewing.
// Things currently get started when the user selects the "Talking Book Tool" item in
// the toolbox while editing. This invokes the function audioRecorder.setupForRecording()
// in this file. That code breaks the
// page's text into sentence-length spans (if not already done), makes sure each
// has an id (preserving existing ones, and using guids for new ones). Then it
// displays  a popup 'bubble' with controls for moving between sentences,
// recording the current sentence, and playing it back. The audio files
// are placed in a folder called 'audio' in the main book folder. Currently we
// save both uncompressed .wav files and compressed .mp3 files for each segment.
// One reason to keep the .wav files is that I don't think FF can play mp3s.
// Currently the actual recording is done in C#, since I can't get audio
// recording to work reliably in HTML using Gecko29. In JohnT's fork of Bloom,
// there is a branch RecordAudioInBrowserSpike in which I attempted to do this.
// It works sometimes, but often part or all of the recording is silence.
// Things that still need doing:
// - Modify TeamCity build (make a new channel if we need an installer
//   with this feature and are not merging yet) to add the naudio.dll
//   dependency
// - Do something about the Lame dependency...either bundle it somehow, or
//   somewhere provide a link for downloading it. We have to pay a fee
//   (see  http://www.mp3-tech.org/ in the games tab) if we distribute more
//   5000 copies of a product that does MP3 encoding. Are we doing so if we
//   just tell people to get the thing that LAME is distributing and use it?
//   It may be necessary for us to track the number of installs that are
//   mp3-encoding-capable, and pay the fee if we reach the limit. May also
//   need legal advice on whether we meet the definition of interactive
//   educational software. Do we have to get a license even if we aren't
//   paying because we haven't distributed enough copies?
// - Hide recording controls until LAME installed, or warn that it is not.
// Other possible improvements:
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

import * as JQuery from "jquery";
import * as $ from "jquery";
import { theOneLibSynphony } from "../readers/libSynphony/synphony_lib";
import theOneLocalizationManager from "../../../lib/localizationManager/localizationManager";
import { TextFragment } from "../readers/libSynphony/bloomSynphonyExtensions";
import axios from "axios";
import { BloomApi } from "../../../utils/bloomApi";
import * as toastr from "toastr";
import WebSocketManager from "../../../utils/WebSocketManager";

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
const kAudioSentenceClassSelector = "." + kAudioSentence;
const kBloomEditableTextBoxSelector = "div.bloom-editable";
const kRecordingModeControl: string = "audio-recordingModeControl";
const kRecordingModeClickHandler: string =
    "audio-recordingModeControl-clickHandler";

// TODO: We would actually like this to have (conceptually) different state for each text box, not a single one per page.
// This would allow us to set a separate audio-recording mode for each state.
// However, currently this code has a lot of reliance on GetPage(), which just shows that the structure is not conceptually set up to handle per-text-box. So we will leave this for later.
export default class AudioRecording {
    private recording: boolean;
    private levelCanvas: HTMLCanvasElement;
    private levelCanvasWidth: number = 15;
    private levelCanvasHeight: number = 80;
    private hiddenSourceBubbles: JQuery;
    private playingAll: boolean; // true during listen.
    private idOfCurrentSentence: string;
    private awaitingNewRecording: boolean;
    private audioRecordingMode: AudioRecordingMode;
    private recordingModeInput: HTMLInputElement; // Currently a checkbox, could change to a radio button in the future

    private listenerFunction: (MessageEvent) => void;

    constructor() {
        // Initialize to Unknown (as opposed to setting to the default Sentence) so we can identify when we need to fetch from Collection Settings vs. when it's already set.
        this.audioRecordingMode = AudioRecordingMode.Unknown;
        this.recordingModeInput = <HTMLInputElement>(
            document.getElementById(kRecordingModeControl)
        );
        if (this.recordingModeInput != null) {
            // Only expected to be null in the unit tests.
            this.recordingModeInput.disabled = true; // Initial state should be disabled so that enableRecordingMode will recognize it needs to initialize things on startup
        }
    }

    // Class method called by exported function of the same name.
    // Only called the first time the Toolbox is opened for this book during this Editing session.
    public initializeTalkingBookTool() {
        // I've sometimes observed events like click being handled repeatedly for a single click.
        // Adding thse .off calls seems to help...it's as if something causes this show event to happen
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
        $("#audio-listen")
            .off()
            .click(e => this.listen());
        $("#audio-clear")
            .off()
            .click(e => this.clearRecording());

        $("#player").off();
        $("#player").attr("preload", "auto"); // speeds playback, ensures we get the durationchange event
        $("#player").bind("error", e => {
            if (this.playingAll) {
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

    // Async. Sets up member variables (e.g. audioRecordingMode) that updateMarkup...() depends on.
    //    May execute some code asynchronously if it needs to retrieve some values from the collection settings.
    //
    // Precondition: You may assume that all initialization will be fully completed before callback() is called.
    //
    // callback: A function to call after initialization completes (especially if the initialization happens asynchronously). Set to null if not needed.
    //           e.g., this should include anything that has a dependency on this.audioRecordingMode
    public initializeForMarkup(callback: () => void = null) {
        const doWhenRecordingModeIsKnown = (audioRecordingModeStr: string) => {
            if (audioRecordingModeStr in AudioRecordingMode) {
                this.audioRecordingMode = <AudioRecordingMode>(
                    audioRecordingModeStr
                );
            } else {
                this.audioRecordingMode = AudioRecordingMode.Unknown;
            }

            if (this.audioRecordingMode == AudioRecordingMode.Unknown) {
                this.audioRecordingMode = AudioRecordingMode.Sentence;
            }

            // Make the checkbox reflect the state
            if (this.audioRecordingMode == AudioRecordingMode.Sentence) {
                this.recordingModeInput.checked = true;
            } else if (this.audioRecordingMode == AudioRecordingMode.TextBox) {
                this.recordingModeInput.checked = false;
            }

            // Execute anything that depends on the initialization
            if (callback) {
                callback();
            }
        };

        if (this.getPage().find("[data-audioRecordingMode]").length > 0) {
            // We are able to identify and load the mode directly from the HTML
            const audioRecordingModeStr: string = this.getPage()
                .find("[data-audioRecordingMode]")
                .first()
                .attr("data-audioRecordingMode");
            doWhenRecordingModeIsKnown(audioRecordingModeStr);
        } else {
            // We are not sure what it should be.
            // So, check what the collection default has to say

            BloomApi.get("talkingBook/defaultAudioRecordingMode", result => {
                doWhenRecordingModeIsKnown(result.data);
            });

            // Note: Any code after here will not necessarily (in fact, probably not) run sequentially after the code in your Get() callback
        }
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

        this.hiddenSourceBubbles = this.getPage().find(
            ".uibloomSourceTextsBubble"
        );
        this.hiddenSourceBubbles.hide();
        var editable = this.getRecordableDivs();
        if (editable.length === 0) {
            // no editable text on this page.
            this.changeStateAndSetExpected("");
            return;
        }
        this.updateMarkupAndControlsToCurrentText();

        this.changeStateAndSetExpected("record");

        this.addAudioLevelListener();
    }

    // Called when a new page is loaded and (above) when the Talking Book Tool is chosen.
    public addAudioLevelListener(): void {
        WebSocketManager.addListener(kWebsocketContext, e => {
            if (e.id == "peakAudioLevel") this.setstaticPeakLevel(e.message);
        });
    }

    // Called by TalkingBookModel.detachFromPage(), which is called when changing tools, hiding the toolbox,
    // or saving (leaving) pages.
    public removeRecordingSetup() {
        this.hiddenSourceBubbles.show();
        var page = this.getPage();
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
    private getRecordableDivs(): JQuery {
        var $this = this;
        var divs = this.getPage().find(
            ":not(.bloom-noAudio) > " + kBloomEditableTextBoxSelector
        );
        return divs.filter(":visible").filter(function(idx, elt) {
            return theOneLibSynphony
                .stringToSentences(elt.innerHTML)
                .some(frag => {
                    return $this.isRecordable(frag);
                });
        });
    }

    private getAudioElements(): JQuery {
        return this.getRecordableDivs()
            .find(kAudioSentenceClassSelector) // Looks only in the descendants, but won't check any of the elements themselves in getRecordableDivs()
            .addBack(kAudioSentenceClassSelector); // Also applies the selector to the result of getRecordableDivs()
    }

    private moveToNextAudioElement(): void {
        toastr.clear();

        var next = this.getNextAudioElement();
        if (!next) return;
        var current: JQuery = this.getPage().find(".ui-audioCurrent");
        this.setCurrentAudioElement(current, $(next));
        this.changeStateAndSetExpected("record");
    }

    private getNextAudioElement(): HTMLElement {
        var current: JQuery = this.getPage().find(".ui-audioCurrent");
        var audioElts = this.getAudioElements();
        var next: JQuery = audioElts.eq(audioElts.index(current) + 1);
        return next.length === 0 ? null : next[0];
    }

    private getPreviousAudioElement(): HTMLElement {
        var current: JQuery = this.getPage().find(".ui-audioCurrent");
        var audioElts = this.getAudioElements();
        var currentIndex = audioElts.index(current);
        if (currentIndex === 0) return null;
        var prev: JQuery = audioElts.eq(currentIndex - 1);
        return prev.length === 0 ? null : prev[0];
    }

    // Generally we set the current span, we want to highlight it. But during
    // listening to the whole page, especially in a Motion preview, we
    // prefer not to highlight the current span unless it actually has audio.
    // This is achieved by passing checking true.
    private setCurrentAudioElement(
        current: JQuery,
        changeTo: JQuery,
        checking?: boolean
    ): void {
        if (current) {
            current
                .removeClass("ui-audioCurrent")
                .removeClass("disableHighlight");
        }
        // We might be changing to nothing (changeTo[0] is null) when doing a whole-page
        // preview (possibly from Motion) and there is no text or it has never
        // been marked up by the talking book tool.
        if (checking && changeTo[0]) {
            changeTo.addClass("disableHighlight"); // prevents highlight showing at once
            axios
                .get("/bloom/api/audio/checkForSegment?id=" + changeTo[0].id)
                .then(response => {
                    if (response.data === "exists") {
                        changeTo.removeClass("disableHighlight");
                    }
                })
                .catch(error => {
                    toastr.error(
                        "Error checking on audio file " + error.statusText
                    );
                    //server couldn't find it, so just leave it unhighlighted
                });
        }
        changeTo.addClass("ui-audioCurrent");
        this.idOfCurrentSentence = changeTo.attr("id");
        this.updatePlayerStatus();
        this.changeStateAndSetExpected("record");
    }

    private currentAudioUrl(id: string): string {
        return this.urlPrefix() + id + ".wav";
    }

    private urlPrefix(): string {
        var bookSrc = this.getPageFrame().src;
        var index = bookSrc.lastIndexOf("/");
        var bookFolderUrl = bookSrc.substring(0, index + 1);
        return bookFolderUrl + "audio/";
    }

    private moveToPrevAudioElement(): void {
        toastr.clear();
        var current: JQuery = this.getPage().find(".ui-audioCurrent");
        var audioElts = this.getAudioElements();
        var currentIndex = audioElts.index(current);
        if (currentIndex === 0) return;
        var prev = this.getPreviousAudioElement();
        if (prev == null) return;
        this.setCurrentAudioElement(current, $(prev));
    }

    // Gecko has no way of knowing that we've created or modified the audio file,
    // so it will cache the previous content of the file or
    // remember if no such file previously existed. So we add a bogus query string
    // based on the current time so that it asks the server for the file again.
    // Fixes BL-3161
    private updatePlayerStatus() {
        var player = $("#player");
        player.attr(
            "src",
            this.currentAudioUrl(this.idOfCurrentSentence) +
                "?nocache=" +
                new Date().getTime()
        );
    }

    private startRecordCurrent(): void {
        if (!this.isEnabledOrExpected("record")) {
            return;
        }

        toastr.clear();
        this.recording = true;
        var current: JQuery = this.getPage().find(".ui-audioCurrent");
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
        if (!this.recording) return; // will trigger if the button wasn't enabled, so the recording never started

        this.recording = false;
        this.awaitingNewRecording = true;

        //this.updatePlayerStatus();

        axios
            .post("/bloom/api/audio/endRecord")
            .then(response => {
                this.updatePlayerStatus();
                this.setStatus("record", Status.Disabled);
                //at the moment, the bakcend is returning when it asks the recorder to stop.
                //But the actual file isn't available for a few moments after that.
                //So we delay looking for it.
                window.setTimeout(() => {
                    this.changeStateAndSetExpected("play");
                }, 1000); // Enhance: Maybe it makes sense to disable any buttons that make notable changes to the state (especially the Recording Mode Control) until this returns.
            })
            .catch(error => {
                this.changeStateAndSetExpected("record"); //record failed, so we expect them to try again
                toastr.error(error.response.statusText);
                console.log(error.response.statusText);
                this.updatePlayerStatus();
            });
    }

    // Called when we get a duration for a current audio element. Mainly we want it after recording a new one.
    // However, for older documents that don't have this, just playing them all will add the new info...
    // or even just stepping through with Next.
    private durationChanged(): void {
        this.awaitingNewRecording = false;
        var current = this.getPage().find(".ui-audioCurrent");
        current.attr(
            "data-duration",
            (<HTMLAudioElement>$("#player").get(0)).duration
        );
    }

    private playCurrent(): void {
        toastr.clear();

        if (!this.isEnabledOrExpected("play")) {
            return;
        }
        this.playingAll = false; // in case it gets clicked after an incomplete play all.
        this.setStatus("play", Status.Active);
        this.playCurrentInternal();
    }

    private playCurrentInternal() {
        (<HTMLMediaElement>document.getElementById("player")).play();
    }

    // 'Listen' is shorthand for playing all the sentences on the page in sequence.
    public listen(): void {
        var original: JQuery = this.getPage().find(".ui-audioCurrent");
        var audioElts = this.getAudioElements();
        var first = audioElts.eq(0);
        this.setCurrentAudioElement(original, first, true);
        this.playingAll = true;
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
        if (this.playingAll) {
            var current: JQuery = this.getPage().find(".ui-audioCurrent");
            var audioElts = this.getAudioElements();
            var next: JQuery = audioElts.eq(audioElts.index(current) + 1);
            if (next.length !== 0) {
                this.setCurrentAudioElement(current, next, true);
                this.setStatus("listen", Status.Active); // gets returned to enabled by setCurrentSpan
                this.playCurrentInternal();
                return;
            }
            this.playingAll = false;
            this.changeStateAndSetExpected("listen");
            return;
        }
        this.changeStateAndSetExpected("next");
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
        //var currentFile = $('#player').attr('src');
        // this.fireCSharpEvent('deleteFile', currentFile);
        axios
            .post(
                "/bloom/api/audio/deleteSegment?id=" + this.idOfCurrentSentence
            )
            .then(result => {
                // data-duration needs to be deleted when the file is deleted.
                // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3671.
                // Note: this is not foolproof because the durationchange handler is
                // being called asynchronously with stale data and sometimes restoring
                // the deleted attribute.
                var current = this.getPage().find(
                    "#" + this.idOfCurrentSentence
                );
                if (current.length !== 0) {
                    current.first().removeAttr("data-duration");
                }
            })
            .catch(error => {
                toastr.error(error.statusText);
            });
        this.updatePlayerStatus();
        this.changeStateAndSetExpected("record");
    }

    // For now, we know this is a checkbox, so we just need to toggle the value.
    // In the future, there may be more than two values and we will need to pass in a parameter to let us know which mode to switch to
    private updateRecordingMode() {
        // Check if there are any audio recordings present.
        //   If so, these would become invalidated (and deleted down the road when the book's unnecessary files gets cleaned up)
        //   Warn the user if this deletion could happen
        //   We detect this state by relying on the same logic that turns on the Listen button when an audio recording is present
        if (this.isEnabledOrExpected("listen")) {
            this.notifyRecordingModeControlDisabled();
            return;
        }

        const checkbox: HTMLInputElement = this.recordingModeInput;
        if (
            this.audioRecordingMode == AudioRecordingMode.Sentence ||
            this.audioRecordingMode == undefined
        ) {
            this.audioRecordingMode = AudioRecordingMode.TextBox;
            checkbox.checked = false;
        } else {
            this.audioRecordingMode = AudioRecordingMode.Sentence;
            checkbox.checked = true;
        }

        // Update the collection's default recording span mode to the new value
        BloomApi.postJson(
            "talkingBook/defaultAudioRecordingMode",
            this.audioRecordingMode
        );

        // Update the UI
        this.updateMarkupAndControlsToCurrentText();
    }

    private enableRecordingModeControl() {
        if (this.recordingModeInput.disabled) {
            this.recordingModeInput.disabled = false;

            // The click handler (an invisible div over the checkbox) is used to handle events on the checkbox even while the checkbox is disabled (which would suppress events on the checkbox itself)
            // Note: In the future, if the click handler is no longer used, just assign the same onClick function() to the checkbox itself.
            $("#" + kRecordingModeClickHandler)
                .off()
                .click(e => this.updateRecordingMode());
        }
    }

    private disableRecordingModeControl() {
        // Note: Possibly could be neat to check if all the audio is re-usable before disabling.
        //       (But then what happens if they modify the text box?  Well, it's kinda awkward, but it's already awkward if they modify the text in by-sentence mode)
        if (!this.recordingModeInput.disabled) {
            this.recordingModeInput.disabled = true;
            // Note: In the future, if the click handler is no longer used, just assign the same onClick function() to the checkbox itself.
            $("#" + kRecordingModeClickHandler)
                .off()
                .click(e => this.notifyRecordingModeControlDisabled());
        }
    }

    private notifyRecordingModeControlDisabled() {
        // Enhance: The string needs to be updated if we develop a concept of per-text-box (ideal) Talking Book toolbox instead of per-page (current)
        // Change "on this page" to "in this text box"
        if (this.recordingModeInput.disabled) {
            theOneLocalizationManager
                .asyncGetText(
                    "EditTab.Toolbox.TalkingBookTool.RecordingModeClearExistingRecordings",
                    "Please clear all existing recordings on this page before changing modes.",
                    ""
                )
                .done(localizedNotification => {
                    toastr.warning(localizedNotification);
                });
        }
    }

    public getPageFrame(): HTMLIFrameElement {
        return <HTMLIFrameElement>parent.window.document.getElementById("page");
    }

    // The body of the editable page, a root for searching for document content.
    public getPage(): JQuery {
        var page = this.getPageFrame();
        if (!page) return null;
        return $(page.contentWindow.document.body);
    }

    public newPageReady() {
        // FYI, it is possible for newPageReady to be called without updateMarkup() being called. (e.g. when opening the toolbox with an empty text box)
        this.initializeForMarkup();
    }

    // Called on initial setup and on toolbox updateMarkup(), including when a new page is created with Talking Book tab open
    public updateMarkupAndControlsToCurrentText() {
        var editable = this.getRecordableDivs();
        if (editable.length === 0) {
            // no editable text on this page.
            this.changeStateAndSetExpected("");
            return;
        }

        const isFullyInitialized: boolean =
            this.audioRecordingMode != AudioRecordingMode.Unknown;
        if (!isFullyInitialized) {
            this.initializeForMarkup(() => {
                this.updateMarkupAndControlsToCurrentText();
            });
            return;
        }

        this.makeAudioSentenceElements(editable);
        // For displaying the qtip, restrict the editable divs to the ones that have
        // audio sentences.
        editable = editable.has(kAudioSentenceClassSelector);
        var thisClass = this;

        //thisClass.setStatus('record', Status.Expected);
        thisClass.levelCanvas = $("#audio-meter").get()[0];

        const firstSentence = this.getPage()
            .find(kAudioSentenceClassSelector)
            .first();
        if (firstSentence.length === 0) {
            // no recordable sentence found.
            return;
        }
        thisClass.setCurrentAudioElement(
            this.getPage().find(".ui-audioCurrent"),
            firstSentence
        ); // typically first arg matches nothing.
    }

    // This gets invoked via websocket message. It draws a series of bars
    // (reminiscent of leds in a hardware level meter) within the canvas in the
    //  top right of the bubble to indicate the current peak level.
    public setstaticPeakLevel(level: string): void {
        if (!this.levelCanvas) return; // just in case C# calls this unexpectedly
        var ctx = this.levelCanvas.getContext("2d");
        // Erase the whole canvas
        var height = 15;
        var width = 80;
        var toolboxBackgroundColor = "#404040"; // should match value in audioRecording.less
        ctx.fillStyle = toolboxBackgroundColor;
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
        var s = [];
        var hexDigits = "0123456789abcdef";
        for (var i = 0; i < 36; i++) {
            s[i] = hexDigits.substr(Math.floor(Math.random() * 0x10), 1);
        }
        s[14] = "4"; // bits 12-15 of the time_hi_and_version field to 0010
        s[19] = hexDigits.substr((s[19] & 0x3) | 0x8, 1); // bits 6-7 of the clock_seq_hi_and_reserved to 01
        s[8] = s[13] = s[18] = s[23] = "-";

        var uuid = s.join("");
        return uuid;
    }

    private md5(message): string {
        var HEX_CHARS = "0123456789abcdef".split("");
        var EXTRA = [128, 32768, 8388608, -2147483648];
        var blocks = [];

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
    public makeAudioSentenceElements(rootElementList: JQuery): void {
        // Preconditions:
        //   bloom-editable ids are not currently used / will be robustly handled in the future / are agnostic to the specific value and format
        //   The first node(s) underneath a bloom-editable should always be <p> elements

        rootElementList.each((index: number, root: Element) => {
            if (this.audioRecordingMode == AudioRecordingMode.Sentence) {
                if (this.isRootRecordableDiv(root)) {
                    // Save which setting was used, so we can load it properly later
                    root.setAttribute("data-audioRecordingMode", "Sentence");

                    // Cleanup markup from AudioRecordingMode=TextBox
                    if (root.classList.contains(kAudioSentence)) {
                        root.classList.remove(kAudioSentence);
                    }
                }
            } else if (this.audioRecordingMode == AudioRecordingMode.TextBox) {
                if (this.isRootRecordableDiv(root)) {
                    // Save which setting was used, so we can load it properly later
                    root.setAttribute("data-audioRecordingMode", "TextBox");

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
                    this.makeAudioSentenceElements(rootElementList); // start over.
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
                    name != "sup" && // better add this one too
                    name != "u" &&
                    $(child).attr("id") !== "formatButton"
                ) {
                    processedChild = true;
                    this.makeAudioSentenceElements($(child));
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
    private makeAudioSentenceElementsLeaf(elt: JQuery): void {
        // When all text is deleted, we get in a temporary state with no paragraph elements, so the root editable div
        // may be processed...and if this happens during editing the format button may be present. The body of this function
        // will do weird things with it (wrap it in a sentence span, for example) so the easiest thing is to remove
        // it at the start and reinstate it at the end. Fortunately its position is predictable. But I wish this
        // otherwise fairly generic code didn't have to know about it.
        var formatButton = elt.find("#formatButton");
        formatButton.remove(); // nothing happens if not found

        var markedSentences = elt.find(kAudioSentenceClassSelector);
        var reuse = []; // an array of id/md5 pairs for any existing sentences marked up for audio in the element.
        markedSentences.each(function(index) {
            reuse.push({
                id: $(this).attr("id"),
                md5: $(this).attr("recordingmd5")
            });
            $(this).replaceWith($(this).html()); // strip out the audio-sentence wrapper so we can re-partition.
        });

        const fragments: TextFragment[] = theOneLibSynphony.stringToSentences(
            elt.html()
        );

        // If any new sentence has an md5 that matches a saved one, attach that id/md5 pair to that fragment.
        for (var i = 0; i < fragments.length; i++) {
            var fragment = fragments[i];
            if (this.isRecordable(fragment)) {
                var currentMd5 = this.md5(fragment.text);
                for (var j = 0; j < reuse.length; j++) {
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
        var newHtml = "";
        for (var i = 0; i < fragments.length; i++) {
            var fragment = fragments[i];

            if (!this.isRecordable(fragment)) {
                // this is inter-sentence space (or white space before first sentence).
                newHtml += fragment.text;
            } else {
                var newId: string = null;
                var newMd5: string = "";
                var reuseThis = (<any>fragment).matchingAudioSpan;
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
                    newId = this.createValidXhtmlUniqueId();
                }
                newHtml +=
                    '<span id= "' +
                    newId +
                    '" class="' +
                    kAudioSentence +
                    '"' +
                    newMd5 +
                    ">" +
                    fragment.text +
                    "</span>";
            }
        }

        // set the html
        elt.html(newHtml);
        elt.append(formatButton);
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
            return !this.isWhiteSpace(content);
        }
        return false;
    }

    private isWhiteSpace(test: string): boolean {
        if (test.match(/^\s*$/)) return true;
        return false;
    }

    private fireCSharpEvent(eventName, eventData): void {
        // Note: other implementations of fireCSharpEvent have 'view':'window', but the TS compiler does
        // not like this. It seems to work fine without it, and I don't know why we had it, so I am just
        // leaving it out.
        var event = new MessageEvent(eventName, {
            bubbles: true,
            cancelable: true,
            data: eventData
        });
        top.document.dispatchEvent(event);
    }

    // ------------ State Machine ----------------

    private changeStateAndSetExpected(expectedVerb: string) {
        console.log("changeState(" + expectedVerb + ")");
        this.setStatus("record", Status.Disabled);
        this.setStatus("play", Status.Disabled);
        this.setStatus("next", Status.Disabled);
        this.setStatus("prev", Status.Disabled);
        this.setStatus("clear", Status.Disabled);
        this.setStatus("listen", Status.Disabled);

        this.enableRecordingModeControl(); // as with the disabling above, we will set the state we really want below

        if (this.getPage().find(".ui-audioCurrent").length === 0) return;

        this.setEnabledOrExpecting("record", expectedVerb);

        //set play and clear buttons based on whether we have an audio file for this
        axios
            .get(
                "/bloom/api/audio/checkForSegment?id=" +
                    this.idOfCurrentSentence
            )
            .then(response => {
                if (response.data === "exists") {
                    this.setStatus("clear", Status.Enabled);
                    this.setEnabledOrExpecting("play", expectedVerb);
                }
            })
            .catch(error => {
                toastr.error(
                    "Error checking on audio file " + error.statusText
                );
                //server couldn't find it, so just leave these buttons disabled
            });

        if (this.getNextAudioElement()) {
            this.setEnabledOrExpecting("next", expectedVerb);
        }
        if (this.getPreviousAudioElement()) {
            this.setStatus("prev", Status.Enabled);
        }

        //set listen button based on whether we have an audio at all for this page
        var ids = [];
        this.getAudioElements().each(function() {
            ids.push(this.id);
        });
        axios
            .get("/bloom/api/audio/enableListenButton?ids=" + ids)
            .then(response => {
                if (response.statusText == "OK") {
                    this.setStatus("listen", Status.Enabled);
                    this.disableRecordingModeControl();
                }
            })
            .catch(response => {
                // This handles the case where AudioRecording.HandleEnableListenButton() (in C#)
                // sends back a request.Failed("no audio") and thereby avoids an uncaught js exception.
                this.setStatus("listen", Status.Disabled);
            });
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
        $("#audio-" + which)
            .removeClass("expected")
            .removeClass("disabled")
            .removeClass("enabled")
            .removeClass("active")
            .addClass(Status[to].toLowerCase());
        if (to === Status.Expected) {
            $("#audio-" + which + "-label").addClass("expected");
        } else {
            $("#audio-" + which + "-label").removeClass("expected");
        }
    }
}

export var theOneAudioRecorder: AudioRecording;

export function initializeTalkingBookTool() {
    if (theOneAudioRecorder) return;
    theOneAudioRecorder = new AudioRecording();
    //reviewslog: not allowed    theOneLibSynphony = new LibSynphony();
    theOneAudioRecorder.initializeTalkingBookTool();
}
