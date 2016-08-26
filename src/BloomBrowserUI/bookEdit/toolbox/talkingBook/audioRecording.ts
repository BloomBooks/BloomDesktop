// This class supports creating audio recordings for talking books.
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
///<reference path="../../../typings/axios/axios.d.ts"/>
///<reference path="../../../typings/toastr/toastr.d.ts"/>

import * as JQuery from 'jquery';
import * as $ from 'jquery';
import {theOneLibSynphony}  from '../readers/libSynphony/synphony_lib';
import {TextFragment} from '../readers/libSynphony/bloomSynphonyExtensions';
import axios = require('axios');
import * as toastr from 'toastr';

enum Status {
    Disabled, // Can't use button now (e.g., Play when there is no recording)
    Enabled, // Can use now, not the most likely thing to do next
    Expected, // The most likely/appropriate button to use next (e.g., Play right after recording)
    Active // Button now active (Play while playing; Record while held down)
};


export default class AudioRecording {
    private recording: boolean;
    private levelCanvas: HTMLCanvasElement;
    private levelCanvasWidth: number = 15;
    private levelCanvasHeight: number = 80;
    private hiddenSourceBubbles: JQuery;
    private playingAll: boolean; // true during listen.
    private idOfCurrentSentence: string;
    private awaitingNewRecording: boolean;
    
    
    public initializeTalkingBookTool() {
        // I've sometimes observed events like click being handled repeatedly for a single click.
        // Adding thse .off calls seems to help...it's as if something causes this show event to happen
        // more than once so the event handlers were being added repeatedly, but I haven't caught
        // that actually happening. However, the off() calls seem to prevent it.
        $('#audio-next').off().click(e => this.moveToNextSpan());
        $('#audio-prev').off().click(e => this.moveToPrevSpan());
        $('#audio-record').off().mousedown(e => this.startRecordCurrent()).mouseup(e => this.endRecordCurrent());
        $('#audio-play').off().click(e => this.playCurrent());
        $('#audio-listen').off().click(e => this.listen());
        $('#audio-clear').off().click(e => this.clearRecording());
        $('#player').off();
        $('#player').attr('preload', 'auto'); // speeds playback, ensures we get the durationchange event
        $('#player').bind('error', e => {
            if (this.playingAll) {
                // during a "listen", we walk through each segment, but some (or all) may not have audio
                this.playEnded();//move to the next one
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

        $('#player').bind('ended', e => this.playEnded());
        $('#player').bind('durationchange', e=>this.durationChanged());
        $('#audio-input-dev').off().click(e => this.selectInputDevice());
        
        toastr.options.positionClass = 'toast-toolbox-bottom';
        toastr.options.timeOut =  10000;
        toastr.options.preventDuplicates= true;
    }
    public setupForRecording(): void {
        this.updateInputDeviceDisplay();
        this.hiddenSourceBubbles = this.getPage().find('.uibloomSourceTextsBubble');
        this.hiddenSourceBubbles.hide();
        var editable = this.getRecordableDivs();
        if (editable.length === 0) {
            // no editable text on this page.
            this.changeStateAndSetExpected('');
            return;
        }
        this.updateMarkupAndControlsToCurrentText();
        
        this.changeStateAndSetExpected('record');

        this.getWebSocket().onmessage = event => {
            var e = JSON.parse(event.data);
            if(e.id == "peakAudioLevel")
                    this.setstaticPeakLevel(e.payload);
        }
    }
    
    public removeRecordingSetup() {
        //alert('removeRecordingSetup');
        this.hiddenSourceBubbles.show();
        var page = this.getPage();
        page.find('.ui-audioCurrent').removeClass('ui-audioCurrent');
    }
    
    private getWebSocket() : WebSocket {
        if (typeof window.top["webSocket"] == "undefined") {
            //currently we use a different port for this websocket, and it's the main port + 1
            const websocketPort = parseInt(window.location.port) + 1;
            //NB: testing shows that our webSocketServer does receive a close notification when this window goes away
            window.top["webSocket"] = new WebSocket("ws://127.0.0.1:"+websocketPort.toString());
        }
        return window.top["webSocket"];
    } 
        
    // We only do recording in editable divs in the main content language.
    // This should NOT restrict to ones that already contain audio-sentence spans.
    private getRecordableDivs() : JQuery {
        return this.getPage().find('div.bloom-editable.bloom-content1');
    }

    private getAudioElements() : JQuery {
        return this.getRecordableDivs().find('.audio-sentence');
    }

    private moveToNextSpan(): void {
        toastr.clear();

        var next = this.getNextSpan();
        if (!next)
            return;
        var current: JQuery = this.getPage().find('.ui-audioCurrent');
        this.setCurrentSpan(current, $(next));
        this.changeStateAndSetExpected('record');
    }
    private getNextSpan(): HTMLElement{
        var current: JQuery = this.getPage().find('.ui-audioCurrent');
        var audioElts = this.getAudioElements();
        var next: JQuery = audioElts.eq(audioElts.index(current) + 1);
        return next.length === 0 ? null : next[0];
    }
    
     private getPreviousSpan(): HTMLElement{
        var current: JQuery = this.getPage().find('.ui-audioCurrent');
        var audioElts = this.getAudioElements();
        var currentIndex = audioElts.index(current);
        if (currentIndex === 0) return null;
        var prev: JQuery = audioElts.eq(currentIndex - 1);
        return (prev.length === 0)  ? null : prev[0];
    }

    private setCurrentSpan(current: JQuery, changeTo: JQuery): void {
        if (current)
            current.removeClass('ui-audioCurrent');
        changeTo.addClass('ui-audioCurrent');
        this.idOfCurrentSentence = changeTo.attr("id");
        this.updatePlayerStatus();
        this.changeStateAndSetExpected('record');
    }

    private currentAudioUrl(id: string): string{
        return this.urlPrefix() + id + '.wav';
    } 
    
    private urlPrefix():string{
        var bookSrc = this.getPageFrame().src;
        var index = bookSrc.lastIndexOf('/');
        var bookFolderUrl = bookSrc.substring(0, index + 1);
        return bookFolderUrl+'audio/';
    }

    private moveToPrevSpan(): void {
        toastr.clear();
        var current: JQuery = this.getPage().find('.ui-audioCurrent');
        var audioElts = this.getAudioElements();
        var currentIndex = audioElts.index(current);
        if (currentIndex === 0) return;
        var prev = this.getPreviousSpan();
        if (prev == null) return;
        this.setCurrentSpan(current, $(prev));
    }

    // Gecko has no way of knowing that we've created or modified the audio file,
    // so it will cache the previous content of the file or
    // remember if no such file previously existed. So we add a bogus query string
    // based on the current time so that it asks the server for the file again.
    // Fixes BL-3161
    private updatePlayerStatus() {
        var player  = $('#player');      
        player.attr('src', this.currentAudioUrl( this.idOfCurrentSentence)+"?nocache="+new Date().getTime());
    }

    private startRecordCurrent(): void {
        if(!this.isEnabledOrExpected('record')){
            return;
        }
        
        toastr.clear();
        this.recording = true;
        var current: JQuery = this.getPage().find('.ui-audioCurrent');
        var id = current.attr("id");
        axios.post("/bloom/api/audio/startRecord?id="+ id).then(result=>{
            this.setStatus('record', Status.Active);
        }).catch(error=> {
            toastr.error(error.statusText);
            console.log(error.statusText);
        });
    }


    private endRecordCurrent(): void {
        if (!this.recording) return; // will trigger if the button wasn't enabled, so the recording never started
        
        this.recording = false;
        this.awaitingNewRecording = true;
        
        //this.updatePlayerStatus();

        axios.post('/bloom/api/audio/endRecord').then( response =>{        
            this.updatePlayerStatus();
            this.setStatus('record', Status.Disabled);
            //at the moment, the bakcend is returning when it asks the recorder to stop.
            //But the actual file isn't available for a few moments after that.
            //So we delay looking for it.
            window.setTimeout( ()=>{
                this.changeStateAndSetExpected('play');
            }, 1000);
        }).catch( error =>{
             this.changeStateAndSetExpected('record');//record failed, so we expect them to try again
                toastr.error(error.statusText);
                console.log(error.statusText);
                this.updatePlayerStatus();
        });
    }

    // Called when we get a duration for a current audio element. Mainly we want it after recording a new one.
    // However, for older documents that don't have this, just playing them all will add the new info...
    // or even just stepping through with Next.
    private durationChanged(): void {
      this.awaitingNewRecording = false;
      var current = this.getPage().find('.ui-audioCurrent');
      current.attr('data-duration', (<HTMLAudioElement>$('#player').get(0)).duration);
    }

    private playCurrent(): void {
        toastr.clear();

        if(!this.isEnabledOrExpected('play')){
            return;
        }
        this.playingAll = false; // in case it gets clicked after an incomplete play all.
        this.setStatus('play', Status.Active);
        this.playCurrentInternal();
    }

    private playCurrentInternal() {
        (<HTMLMediaElement>document.getElementById('player')).play();
    }

    // 'Listen' is shorthand for playing all the sentences on the page in sequence.
    private listen(): void {
        var original: JQuery = this.getPage().find('.ui-audioCurrent');
        var audioElts = this.getAudioElements();
        var first = audioElts.eq(0);
        this.setCurrentSpan(original, first);
        this.playingAll = true;
        this.setStatus('listen', Status.Active);
        this.playCurrentInternal();
    }

    private playEnded(): void {
        if (this.playingAll) {
            var current: JQuery = this.getPage().find('.ui-audioCurrent');
            var audioElts = this.getAudioElements();
            var next: JQuery = audioElts.eq(audioElts.index(current) + 1);
            if (next.length !== 0) {
                this.setCurrentSpan(current, next);
                this.setStatus('listen', Status.Active); // gets returned to enabled by setCurrentSpan
                this.playCurrentInternal();
                return;
            }
            this.playingAll = false;
            this.changeStateAndSetExpected('listen');
            return;
        }
        this.changeStateAndSetExpected('next');
    }

    private selectInputDevice(): void {
        var thisClass = this;
        axios.get<any>("/bloom/api/audio/devices").then(result => {
            var data = result.data; // Axios apparently recognizes the JSON and parses it automatically.
            // Retrieves JSON generated by AudioRecording.AudioDevicesJson
            // Something like {"devices":["microphone", "Logitech Headset"], "productName":"Logitech Headset", "genericName":"Headset" },
            // except that in practice currrently the generic and product names are the same and not as helpful as the above.
            if (data.devices.length <= 1) return; // no change is possible.
            if (data.devices.length == 2) {
                // Just toggle between them
                var device = (data.devices[0] == data.productName) ? data.devices[1] : data.devices[0];
                axios.post("/bloom/api/audio/currentRecordingDevice", device)
                    .then(result=>{
                         this.updateInputDeviceDisplay();
                    })
                    .catch(error=>{
                        toastr.error(error.statusText);
                    });
                return;
            }
            var devList = $('#audio-devlist');
            devList.empty();
            for (var i = 0; i < data.devices.length; i++) {
                devList.append('<li>' + data.devices[i] + '</li>');
            }
            (<any>devList).one("click", function(event) {
                    devList.hide();
                    axios.post("/bloom/api/audio/currentRecordingDevice", $(event.target).text())
                     .then(result=>{
                         this.updateInputDeviceDisplay();
                    }).catch(error=>{
                        toastr.error(error.statusText);
                    });
                })
                .show().position({
                    my: "right bottom",
                    at: "right top",
                    of: $('#audio-input-dev')
                });
        });
    }

    private updateInputDeviceDisplay(): void {
        axios.get<any>("/bloom/api/audio/devices").then(result => {
            var data = result.data;
            // See selectInputDevice for what is retrieved.
            var genericName = data.genericName;
            var deviceName = data.productName;
            // The following logic is adapted from Palaso.Media.RecordingDeviceIndicator.UpdateDisplay()
            // Mods: checking for "Headse" is motivated by JohnT's Logitech Headset, which comes up as "Microphone (Logitech USB Headse".
            // checking for "Array" is motivated by JohnT's dell laptop, where the internal microphone comes up as "Microphone Array (2 RealTek Hi".

            // This seems a reasonable default to suggest what needs to be connected
            // if nothing is. It's also the default if we don't recognize anything significant in the name.
            var imageName = 'Microphone.svg';
            if (genericName !== null) {
                if (genericName.indexOf('Internal') >= 0 || genericName.indexOf('Array') >= 0) imageName = 'Computer.png';
                else if (genericName.indexOf('USB Audio Device') >= 0 || genericName.indexOf('Headse') >= 0) imageName = 'HeadSet.png';

                if (deviceName.indexOf('ZOOM') >= 0) imageName = 'Recorder.png';
                else if (deviceName.indexOf('Plantronics') >= 0 || deviceName.indexOf('Andrea') >= 0 || deviceName.indexOf('Microphone (VXi X200') >= 0) imageName = 'HeadSet.png';
                else if (deviceName.indexOf('Line') >= 0) imageName = 'ExternalAudioDevice.png';
            }
            var devButton = $('#audio-input-dev');
            var src = devButton.attr('src');
            var lastSlash = src.lastIndexOf('/');
            var newSrc = src.substring(0, lastSlash + 1) + imageName;
            devButton.attr('src', newSrc);
            devButton.attr('title', deviceName);
        });
    }

    // Clear the recording for this sentence
    clearRecording(): void {
        toastr.clear();

        if(!this.isEnabledOrExpected('clear')){
            return;
        }
        //var currentFile = $('#player').attr('src');
        // this.fireCSharpEvent('deleteFile', currentFile);
        axios.post('/bloom/api/audio/deleteSegment?id='+this.idOfCurrentSentence).catch(error=>{
            toastr.error(error.statusText);
        });
        this.updatePlayerStatus();
        this.changeStateAndSetExpected('record');
    }
    public getPageFrame(): HTMLIFrameElement {
        return <HTMLIFrameElement>parent.window.document.getElementById('page');
    }

    // The body of the editable page, a root for searching for document content.
    public getPage(): JQuery {
        var page = this.getPageFrame();
        if (!page) return null;
        return $(page.contentWindow.document.body);
    }

    public updateMarkupAndControlsToCurrentText() {
        var editable = this.getRecordableDivs();
        this.makeSentenceSpans(editable);
        // For displaying the qtip, restrict the editable divs to the ones that have
        // audio sentences.
        editable = editable.has('span.audio-sentence');
        var thisClass = this;

        //thisClass.setStatus('record', Status.Expected);
        thisClass.levelCanvas = $('#audio-meter').get()[0];
        var firstSentence = editable.find('span.audio-sentence').first();
        if (firstSentence.length === 0) {
            // no recordable sentence found.
            return;
        }
        thisClass.setCurrentSpan(this.getPage().find('.ui-audioCurrent'), firstSentence); // typically first arg matches nothing.
    }

    // This gets invoked via websocket message. It draws a series of bars
    // (reminiscent of leds in a hardware level meter) within the canvas in the
    //  top right of the bubble to indicate the current peak level.
    public setstaticPeakLevel(level: string): void {
        if (!this.levelCanvas)
            return; // just in case C# calls this unexpectedly
        var ctx = this.levelCanvas.getContext("2d");
        // Erase the whole canvas
        var height = 15;
        var width = 80;
        var toolboxBackgroundColor ='#404040';// should match value in audioRecording.less
        ctx.fillStyle = toolboxBackgroundColor;
        ctx.fillRect(0, 0, width, height);

        // Draw the appropriate number and color of bars
        var gap = 2;
        var barWidth = 4;
        var interval = gap + barWidth;
        var bars = Math.floor(width / interval);
        var loudBars = 2;
        var quietBars = 2;
        var mediumBars = Math.max(bars - (loudBars+quietBars), 1);
        var showBars = Math.floor(bars * parseFloat(level));// + 1;
        ctx.fillStyle =  '#D2D2D2'; // should match text color or "#00FF00";
        for (var i = 0; i < showBars; i++) {
            var left = interval * i;
            if (i >= quietBars) ctx.fillStyle = '#0C8597';
            if (i >= quietBars + mediumBars) ctx.fillStyle = "#FF0000";//red
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
        s[14] = "4";  // bits 12-15 of the time_hi_and_version field to 0010
        s[19] = hexDigits.substr((s[19] & 0x3) | 0x8, 1);  // bits 6-7 of the clock_seq_hi_and_reserved to 01
        s[8] = s[13] = s[18] = s[23] = "-";

        var uuid = s.join("");
        return uuid;
    }

    private md5(message): string {
        var HEX_CHARS = '0123456789abcdef'.split('');
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
            blocks[16] = blocks[1] = blocks[2] = blocks[3] =
                blocks[4] = blocks[5] = blocks[6] = blocks[7] =
                blocks[8] = blocks[9] = blocks[10] = blocks[11] =
                blocks[12] = blocks[13] = blocks[14] = blocks[15] = 0;
            for (i = start; index < length && i < 64; ++index) {
                code = message.charCodeAt(index);
                if (code < 0x80) {
                    blocks[i >> 2] |= code << SHIFT[i++ & 3];
                } else if (code < 0x800) {
                    blocks[i >> 2] |= (0xc0 | (code >> 6)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | (code & 0x3f)) << SHIFT[i++ & 3];
                } else if (code < 0xd800 || code >= 0xe000) {
                    blocks[i >> 2] |= (0xe0 | (code >> 12)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | ((code >> 6) & 0x3f)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | (code & 0x3f)) << SHIFT[i++ & 3];
                } else {
                    code = 0x10000 + (((code & 0x3ff) << 10) | (message.charCodeAt(++index) & 0x3ff));
                    blocks[i >> 2] |= (0xf0 | (code >> 18)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | ((code >> 12) & 0x3f)) << SHIFT[i++ & 3];
                    blocks[i >> 2] |= (0x80 | ((code >> 6) & 0x3f)) << SHIFT[i++ & 3];
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
                a = (a << 7 | a >>> 25) - 271733879 << 0;
                d = (-1732584194 ^ a & 2004318071) + blocks[1] - 117830708;
                d = (d << 12 | d >>> 20) + a << 0;
                c = (-271733879 ^ (d & (a ^ -271733879))) + blocks[2] - 1126478375;
                c = (c << 17 | c >>> 15) + d << 0;
                b = (a ^ (c & (d ^ a))) + blocks[3] - 1316259209;
                b = (b << 22 | b >>> 10) + c << 0;
            } else {
                a = h0;
                b = h1;
                c = h2;
                d = h3;
                a += (d ^ (b & (c ^ d))) + blocks[0] - 680876936;
                a = (a << 7 | a >>> 25) + b << 0;
                d += (c ^ (a & (b ^ c))) + blocks[1] - 389564586;
                d = (d << 12 | d >>> 20) + a << 0;
                c += (b ^ (d & (a ^ b))) + blocks[2] + 606105819;
                c = (c << 17 | c >>> 15) + d << 0;
                b += (a ^ (c & (d ^ a))) + blocks[3] - 1044525330;
                b = (b << 22 | b >>> 10) + c << 0;
            }

            a += (d ^ (b & (c ^ d))) + blocks[4] - 176418897;
            a = (a << 7 | a >>> 25) + b << 0;
            d += (c ^ (a & (b ^ c))) + blocks[5] + 1200080426;
            d = (d << 12 | d >>> 20) + a << 0;
            c += (b ^ (d & (a ^ b))) + blocks[6] - 1473231341;
            c = (c << 17 | c >>> 15) + d << 0;
            b += (a ^ (c & (d ^ a))) + blocks[7] - 45705983;
            b = (b << 22 | b >>> 10) + c << 0;
            a += (d ^ (b & (c ^ d))) + blocks[8] + 1770035416;
            a = (a << 7 | a >>> 25) + b << 0;
            d += (c ^ (a & (b ^ c))) + blocks[9] - 1958414417;
            d = (d << 12 | d >>> 20) + a << 0;
            c += (b ^ (d & (a ^ b))) + blocks[10] - 42063;
            c = (c << 17 | c >>> 15) + d << 0;
            b += (a ^ (c & (d ^ a))) + blocks[11] - 1990404162;
            b = (b << 22 | b >>> 10) + c << 0;
            a += (d ^ (b & (c ^ d))) + blocks[12] + 1804603682;
            a = (a << 7 | a >>> 25) + b << 0;
            d += (c ^ (a & (b ^ c))) + blocks[13] - 40341101;
            d = (d << 12 | d >>> 20) + a << 0;
            c += (b ^ (d & (a ^ b))) + blocks[14] - 1502002290;
            c = (c << 17 | c >>> 15) + d << 0;
            b += (a ^ (c & (d ^ a))) + blocks[15] + 1236535329;
            b = (b << 22 | b >>> 10) + c << 0;
            a += (c ^ (d & (b ^ c))) + blocks[1] - 165796510;
            a = (a << 5 | a >>> 27) + b << 0;
            d += (b ^ (c & (a ^ b))) + blocks[6] - 1069501632;
            d = (d << 9 | d >>> 23) + a << 0;
            c += (a ^ (b & (d ^ a))) + blocks[11] + 643717713;
            c = (c << 14 | c >>> 18) + d << 0;
            b += (d ^ (a & (c ^ d))) + blocks[0] - 373897302;
            b = (b << 20 | b >>> 12) + c << 0;
            a += (c ^ (d & (b ^ c))) + blocks[5] - 701558691;
            a = (a << 5 | a >>> 27) + b << 0;
            d += (b ^ (c & (a ^ b))) + blocks[10] + 38016083;
            d = (d << 9 | d >>> 23) + a << 0;
            c += (a ^ (b & (d ^ a))) + blocks[15] - 660478335;
            c = (c << 14 | c >>> 18) + d << 0;
            b += (d ^ (a & (c ^ d))) + blocks[4] - 405537848;
            b = (b << 20 | b >>> 12) + c << 0;
            a += (c ^ (d & (b ^ c))) + blocks[9] + 568446438;
            a = (a << 5 | a >>> 27) + b << 0;
            d += (b ^ (c & (a ^ b))) + blocks[14] - 1019803690;
            d = (d << 9 | d >>> 23) + a << 0;
            c += (a ^ (b & (d ^ a))) + blocks[3] - 187363961;
            c = (c << 14 | c >>> 18) + d << 0;
            b += (d ^ (a & (c ^ d))) + blocks[8] + 1163531501;
            b = (b << 20 | b >>> 12) + c << 0;
            a += (c ^ (d & (b ^ c))) + blocks[13] - 1444681467;
            a = (a << 5 | a >>> 27) + b << 0;
            d += (b ^ (c & (a ^ b))) + blocks[2] - 51403784;
            d = (d << 9 | d >>> 23) + a << 0;
            c += (a ^ (b & (d ^ a))) + blocks[7] + 1735328473;
            c = (c << 14 | c >>> 18) + d << 0;
            b += (d ^ (a & (c ^ d))) + blocks[12] - 1926607734;
            b = (b << 20 | b >>> 12) + c << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[5] - 378558;
            a = (a << 4 | a >>> 28) + b << 0;
            d += (bc ^ a) + blocks[8] - 2022574463;
            d = (d << 11 | d >>> 21) + a << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[11] + 1839030562;
            c = (c << 16 | c >>> 16) + d << 0;
            b += (da ^ c) + blocks[14] - 35309556;
            b = (b << 23 | b >>> 9) + c << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[1] - 1530992060;
            a = (a << 4 | a >>> 28) + b << 0;
            d += (bc ^ a) + blocks[4] + 1272893353;
            d = (d << 11 | d >>> 21) + a << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[7] - 155497632;
            c = (c << 16 | c >>> 16) + d << 0;
            b += (da ^ c) + blocks[10] - 1094730640;
            b = (b << 23 | b >>> 9) + c << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[13] + 681279174;
            a = (a << 4 | a >>> 28) + b << 0;
            d += (bc ^ a) + blocks[0] - 358537222;
            d = (d << 11 | d >>> 21) + a << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[3] - 722521979;
            c = (c << 16 | c >>> 16) + d << 0;
            b += (da ^ c) + blocks[6] + 76029189;
            b = (b << 23 | b >>> 9) + c << 0;
            bc = b ^ c;
            a += (bc ^ d) + blocks[9] - 640364487;
            a = (a << 4 | a >>> 28) + b << 0;
            d += (bc ^ a) + blocks[12] - 421815835;
            d = (d << 11 | d >>> 21) + a << 0;
            da = d ^ a;
            c += (da ^ b) + blocks[15] + 530742520;
            c = (c << 16 | c >>> 16) + d << 0;
            b += (da ^ c) + blocks[2] - 995338651;
            b = (b << 23 | b >>> 9) + c << 0;
            a += (c ^ (b | ~d)) + blocks[0] - 198630844;
            a = (a << 6 | a >>> 26) + b << 0;
            d += (b ^ (a | ~c)) + blocks[7] + 1126891415;
            d = (d << 10 | d >>> 22) + a << 0;
            c += (a ^ (d | ~b)) + blocks[14] - 1416354905;
            c = (c << 15 | c >>> 17) + d << 0;
            b += (d ^ (c | ~a)) + blocks[5] - 57434055;
            b = (b << 21 | b >>> 11) + c << 0;
            a += (c ^ (b | ~d)) + blocks[12] + 1700485571;
            a = (a << 6 | a >>> 26) + b << 0;
            d += (b ^ (a | ~c)) + blocks[3] - 1894986606;
            d = (d << 10 | d >>> 22) + a << 0;
            c += (a ^ (d | ~b)) + blocks[10] - 1051523;
            c = (c << 15 | c >>> 17) + d << 0;
            b += (d ^ (c | ~a)) + blocks[1] - 2054922799;
            b = (b << 21 | b >>> 11) + c << 0;
            a += (c ^ (b | ~d)) + blocks[8] + 1873313359;
            a = (a << 6 | a >>> 26) + b << 0;
            d += (b ^ (a | ~c)) + blocks[15] - 30611744;
            d = (d << 10 | d >>> 22) + a << 0;
            c += (a ^ (d | ~b)) + blocks[6] - 1560198380;
            c = (c << 15 | c >>> 17) + d << 0;
            b += (d ^ (c | ~a)) + blocks[13] + 1309151649;
            b = (b << 21 | b >>> 11) + c << 0;
            a += (c ^ (b | ~d)) + blocks[4] - 145523070;
            a = (a << 6 | a >>> 26) + b << 0;
            d += (b ^ (a | ~c)) + blocks[11] - 1120210379;
            d = (d << 10 | d >>> 22) + a << 0;
            c += (a ^ (d | ~b)) + blocks[2] + 718787259;
            c = (c << 15 | c >>> 17) + d << 0;
            b += (d ^ (c | ~a)) + blocks[9] - 343485551;
            b = (b << 21 | b >>> 11) + c << 0;

            if (first) {
                h0 = a + 1732584193 << 0;
                h1 = b - 271733879 << 0;
                h2 = c - 1732584194 << 0;
                h3 = d + 271733878 << 0;
                first = false;
            } else {
                h0 = h0 + a << 0;
                h1 = h1 + b << 0;
                h2 = h2 + c << 0;
                h3 = h3 + d << 0;
            }
        } while (!end);

        var hex = HEX_CHARS[(h0 >> 4) & 0x0F] + HEX_CHARS[h0 & 0x0F];
        hex += HEX_CHARS[(h0 >> 12) & 0x0F] + HEX_CHARS[(h0 >> 8) & 0x0F];
        hex += HEX_CHARS[(h0 >> 20) & 0x0F] + HEX_CHARS[(h0 >> 16) & 0x0F];
        hex += HEX_CHARS[(h0 >> 28) & 0x0F] + HEX_CHARS[(h0 >> 24) & 0x0F];
        hex += HEX_CHARS[(h1 >> 4) & 0x0F] + HEX_CHARS[h1 & 0x0F];
        hex += HEX_CHARS[(h1 >> 12) & 0x0F] + HEX_CHARS[(h1 >> 8) & 0x0F];
        hex += HEX_CHARS[(h1 >> 20) & 0x0F] + HEX_CHARS[(h1 >> 16) & 0x0F];
        hex += HEX_CHARS[(h1 >> 28) & 0x0F] + HEX_CHARS[(h1 >> 24) & 0x0F];
        hex += HEX_CHARS[(h2 >> 4) & 0x0F] + HEX_CHARS[h2 & 0x0F];
        hex += HEX_CHARS[(h2 >> 12) & 0x0F] + HEX_CHARS[(h2 >> 8) & 0x0F];
        hex += HEX_CHARS[(h2 >> 20) & 0x0F] + HEX_CHARS[(h2 >> 16) & 0x0F];
        hex += HEX_CHARS[(h2 >> 28) & 0x0F] + HEX_CHARS[(h2 >> 24) & 0x0F];
        hex += HEX_CHARS[(h3 >> 4) & 0x0F] + HEX_CHARS[h3 & 0x0F];
        hex += HEX_CHARS[(h3 >> 12) & 0x0F] + HEX_CHARS[(h3 >> 8) & 0x0F];
        hex += HEX_CHARS[(h3 >> 20) & 0x0F] + HEX_CHARS[(h3 >> 16) & 0x0F];
        hex += HEX_CHARS[(h3 >> 28) & 0x0F] + HEX_CHARS[(h3 >> 24) & 0x0F];
        return hex;

    }

    // We want to make out of each sentence in root a span which has a unique ID.
    // If the text is already so marked up, we want to keep the existing ids
    // AND the recordingID checksum attribute (if any) that indicates what
    // version of the text was last recorded.
    // makeSentenceLeaf does this for roots which don't have children (except a few
    // special cases); this root method scans down and does it for each such child
    // in a root (possibly the root itself, if it has no children).
    public makeSentenceSpans(root: JQuery): void {
        root.each((index: number, e: Element) => {
            var children = $(e).children();
            var processedChild: boolean = false; // Did we find a significant child?
            for (var i = 0; i < children.length; i++) {
                var child: HTMLElement = children[i];
                var name = child.nodeName.toLowerCase();
                // Review: is there a better way to pick out the elements that can occur within content elements?
                if (name != 'span' && name != 'br' && name != 'i' && name != 'b' && name != 'u' && $(child).attr('id') !== 'formatButton') {
                    processedChild = true;
                    this.makeSentenceSpans($(child));
                }
            }
            if (!processedChild) // root is a leaf; process its actual content
                this.makeSentenceSpansLeaf($(e));
        });
        // Review: is there a need to handle elements that contain both sentence text AND child elements with their own text?
    }

    // The goal for existing markup is that if any existing audio-sentence span has an md5 that matches the content of a
    // current sentence, we want to preserve the association between that content and ID (and possibly recording).
    // Where there aren't exact matches, but there are existing audio-sentence spans, we keep the ids as far as possible,
    // just using the original order, since it is possible we have a match and only spelling or punctuation changed.
    private makeSentenceSpansLeaf(elt: JQuery): void {
        // When all text is deleted, we get in a temporary state with no paragraph elements, so the root editable div
        // may be processed...and if this happens during editing the format button may be present. The body of this function
        // will do weird things with it (wrap it in a sentence span, for example) so the easiest thing is to remove
        // it at the start and reinstate it at the end. Fortunately its position is predictable. But I wish this
        // otherwise fairly generic code didn't have to know about it.
        var formatButton = elt.find('#formatButton');
        formatButton.remove(); // nothing happens if not found

        var markedSentences = elt.find("span.audio-sentence");
        var reuse = []; // an array of id/md5 pairs for any existing sentences marked up for audio in the element.
        markedSentences.each(function(index) {
            reuse.push({ id: $(this).attr('id'), md5: $(this).attr('recordingmd5') });
            $(this).replaceWith($(this).html()); // strip out the audio-sentence wrapper so we can re-partition.
        });

        var fragments: TextFragment[] = theOneLibSynphony.stringToSentences(elt.html());

        // If any new sentence has an md5 that matches a saved one, attatch that id/md5 pair to that fragment.
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
                var newMd5: string = '';
                var reuseThis = (<any>fragment).matchingAudioSpan;
                if (!reuseThis && reuse.length > 0) {
                    reuseThis = reuse[0]; // use first if none matches (preserves order at least)
                    reuse.splice(0, 1);
                }
                if (reuseThis) { // SOMETHING remains we can reuse
                    newId = reuseThis.id;
                    newMd5 = ' recordingmd5="' + reuseThis.md5 + '"';
                }
                if (!newId) {
                    newId = this.createUuid();
                    if (/^\d/.test(newId)) newId = 'i' + newId; // valid ID in XHTML can't start with digit
                }
                newHtml += '<span id= "' + newId + '" class="audio-sentence"' + newMd5 + '>' + fragment.text + '</span>';
            }
        }

        // set the html
        elt.html(newHtml);
        elt.append(formatButton);
    }

    private isRecordable(fragment: TextFragment): Boolean {
        if (fragment.isSpace) return false; // this seems to be reliable
        // initial white-space fragments may currently be marked sentence
        var test = fragment.text.replace(/<br *[^>]*\/?>/g, " ");
        // and some may contain only nbsp
        test = test.replace("&nbsp;", " ");
        return !test.match(/^\s*$/);
    }

    private fireCSharpEvent(eventName, eventData): void {
        // Note: other implementations of fireCSharpEvent have 'view':'window', but the TS compiler does
        // not like this. It seems to work fine without it, and I don't know why we had it, so I am just
        // leaving it out.
        var event = new MessageEvent(eventName, {'bubbles': true, 'cancelable': true, 'data': eventData });
        top.document.dispatchEvent(event);
    }
    
    
    // ------------ State Machine ----------------
    
    private changeStateAndSetExpected(expectedVerb: string){
        console.log("changeState("+expectedVerb+")");
        this.setStatus('record', Status.Disabled);
        this.setStatus('play', Status.Disabled);
        this.setStatus('next', Status.Disabled);
        this.setStatus('prev', Status.Disabled);
        this.setStatus('clear', Status.Disabled);
        this.setStatus('listen', Status.Disabled);
        
        if(this.getPage().find('.ui-audioCurrent').length===0)
            return;
                    
        this.setEnabledOrExpecting('record', expectedVerb);
            
        //set play and clear buttons based on whether we have an audio file for this
        axios.get("/bloom/api/audio/checkForSegment?id="+this.idOfCurrentSentence).then( response => {
            if(response.data === "exists")   {
                this.setStatus('clear', Status.Enabled);
                this.setEnabledOrExpecting('play',  expectedVerb);
            }
        }).catch(error =>{
            toastr.error("Error checking on audio file "+error.statusText);
            //server couldn't find it, so just leave these buttons disabled
        });
    
        if(this.getNextSpan())
        {
            this.setEnabledOrExpecting('next',  expectedVerb);
        }
        if(this.getPreviousSpan())
        {
            this.setStatus('prev',  Status.Enabled);
        }
                
        //set listen button based on whether we have an audio at all for this page
        var ids = [];
        this.getAudioElements().each(function() { ids.push(this.id); });
        axios.get("/bloom/api/audio/enableListenButton?ids=" + ids).then(response=>{
            if(response.statusText == "OK")
                this.setStatus('listen', Status.Enabled);
        });
    }
    
    private setEnabledOrExpecting(verb:  string, expectedVerb: string){
        if(expectedVerb == verb)
            this.setStatus(verb,Status.Expected);
        else
           this.setStatus(verb,Status.Enabled);
    }
    
    private isEnabledOrExpected(verb: string):Boolean{
        return $('#audio-' + verb).hasClass('enabled') || $('#audio-' + verb).hasClass('expected') ;    
    }
    
    private setStatus(which: string, to: Status): void {
        $('#audio-' + which).removeClass('expected').removeClass('disabled').removeClass('enabled').removeClass('active').addClass(Status[to].toLowerCase());
        if (to === Status.Expected) {
            $('#audio-' + which + '-label').addClass('expected');
        } else {
            $('#audio-' + which + '-label').removeClass('expected');
        }
    }
}

export var theOneAudioRecorder: AudioRecording;

export function initializeTalkingBookTool() {
    if (theOneAudioRecorder)
        return;
    theOneAudioRecorder = new AudioRecording();
    //reviewslog: not allowed    theOneLibSynphony = new LibSynphony();
    theOneAudioRecorder.initializeTalkingBookTool();
}



