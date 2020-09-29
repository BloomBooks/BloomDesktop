import AudioRecording, { AudioRecordingMode } from "./audioRecording";
import axios, { AxiosResponse } from "axios";

const kAudioSentence = "audio-sentence";

enum RecordingStatus {
    None,
    Partial,
    Full
}
// It represents a recordable text box that has elements we can make audio recordings of.
// This class is a work in progress. We can migrate functions from audioRecording.ts into here
// that solely operate on a text box without caring about the talking book tool.
export default class Recordable {
    private textBox: HTMLDivElement;

    public constructor(textBox: HTMLElement) {
        console.assert(
            textBox.classList.contains("bloom-editable"),
            "Only bloom-editables are expected to be made Recordables"
        );
        console.assert(
            textBox.tagName.toLowerCase() === "div",
            "A non-div element was unexpectedly passed into Recordable's constructor."
        );
        this.textBox = textBox as HTMLDivElement;
    }

    // BL-8425 Some cases were found where 'data-audioRecordingMode' was present, but 'audio-sentence' class
    // didn't occur at all in that div or its children. So here we want to make sure that things get processed
    // if they might be in a bad state.
    public isRecordableDivFullyInitialized(): boolean {
        // ENHANCE: As of version 4.9, this function (nor its predecesor in AudioRecording.ts) is no longer called.
        // Consider removing in version 4.10 or sooner if it is indeed no longer needed

        const modeAttribute = this.textBox.getAttribute(
            "data-audioRecordingMode"
        );
        if (!modeAttribute) {
            return false;
        }
        if (
            modeAttribute == AudioRecordingMode.TextBox &&
            !this.textBox.classList.contains(kAudioSentence) && // Missing the normal state of text boxes (with audio-sentence on the text box itself)
            this.textBox.getElementsByClassName(kAudioSentence).length === 0 // Also missing the legacy Hard Split (v4.5) setup
        ) {
            return false;
        }
        if (
            modeAttribute == AudioRecordingMode.Sentence &&
            this.textBox.classList.contains(kAudioSentence)
        ) {
            // This looks so strange. Why does the text box itself have audioSentence? Only the children should have it.
            // Let's re-initialize this.
            return false;
        }
        if (
            modeAttribute == AudioRecordingMode.Sentence &&
            this.textBox.getElementsByClassName(kAudioSentence).length === 0
        ) {
            // Note: It might also be empty, but... oh well, we'll re-initialize it anyway.
            return false;
        }
        return true;
    }

    // Gets all the elements that have audio-sentence class on them (possibly the text box itself)
    public getAudioSentences(): HTMLElement[] {
        // Note: Not expected for both the text box and its children to contain audio-sentence.
        // We only expect one or the other.
        if (this.textBox.classList.contains("audio-sentence")) {
            return [this.textBox];
        } else {
            // This only matches strict descendants, not itself
            const matchingDescendants = this.textBox.querySelectorAll(
                ".audio-sentence"
            );

            return Array.from(matchingDescendants) as HTMLElement[];
        }
    }

    public getAudioSentenceIds(): string[] {
        return this.getAudioSentences().map((element: Element) => {
            console.assert(
                element.id,
                "Element unexpectedly had falsy ID: " + element.innerHTML
            );

            return element.id;
        });
    }

    // Returns true (asynchronously) if this text box contains any recordings.
    public async areAnyRecordingsPresentAsync(): Promise<boolean> {
        const idsToCheck: string[] = this.getAudioSentenceIds();
        if (idsToCheck.length === 0) {
            return false;
        }

        try {
            await axios.get(
                `/bloom/api/audio/checkForAnyRecording?ids=${idsToCheck}`
            );
            return true;
        } catch {
            // If the recording is not there, it will return a 404 error aka it goes into the catch.
            return false;
        }
    }

    // Returns true (asynchronously) if every audio-sentence within this text box is recorded.
    public async isFullyRecordedAsync(): Promise<boolean> {
        const idsToCheck: string[] = this.getAudioSentenceIds();
        if (idsToCheck.length === 0) {
            return false;
        }

        let result: AxiosResponse<any>;
        try {
            const url = `/bloom/api/audio/checkForAllRecording?ids=${idsToCheck}`;
            result = await axios.get(url);
        } catch {
            // Just swallow 404 errors and return false instead
            // (Unit tests may return 404's if not configured to return fake values for these calls)
            return false;
        }
        const isAllPresent = result.data as boolean;
        return isAllPresent;
    }

    public static async isSentenceRecordedAsync(
        sentence: Element
    ): Promise<boolean> {
        if (!sentence.id) {
            return Promise.reject(
                new Error("id was falsy on sentence: " + sentence.outerHTML)
            );
        }

        try {
            await axios.get(
                `/bloom/api/audio/checkForAnyRecording?ids=${sentence.id}`
            );
        } catch {
            // Well, currently missing is coded up as returning a 404 error.
            return Promise.resolve(false);
        }

        return Promise.resolve(true);
    }

    // Sets the md5 of the text box to that of its current contents
    public setChecksum(): void {
        const sentences = this.getAudioSentences();
        sentences.forEach(sentence => {
            const md5 = AudioRecording.getChecksum(sentence.innerText);
            sentence.setAttribute("recordingmd5", md5);
        });
    }

    public unsetChecksum(): void {
        const sentences = this.getAudioSentences();
        sentences.forEach(sentence => {
            sentence.removeAttribute("recordingmd5");
        });
    }

    public async setMd5IfMissingAsync(): Promise<void> {
        const sentences = this.getAudioSentences();
        const asyncUpdates = sentences.map(elem => {
            return this.setMd5OnSentenceIfMissingAsync(elem);
        });
        await Promise.all(asyncUpdates);
    }

    private async setMd5OnSentenceIfMissingAsync(
        sentence: HTMLElement
    ): Promise<void> {
        if (this.isMissingRecordingChecksum(sentence)) {
            // We only want to update ones that have a recording associated with them.
            if (await Recordable.isSentenceRecordedAsync(sentence)) {
                const md5 = AudioRecording.getChecksum(sentence.innerText);
                sentence.setAttribute("recordingmd5", md5);
            }
        }
    }

    private isMissingRecordingChecksum(element: HTMLElement) {
        const checksum = element.getAttribute("recordingmd5");
        return !checksum || checksum === "undefined";
    }

    public async shouldUpdateMarkupAsync(): Promise<boolean> {
        if (this.areAnyRecordingsOutOfDate()) {
            // Should be updated
            return true;
        } else {
            const recordingStatus = await this.getRecordingStatusAsync();
            if (recordingStatus === RecordingStatus.Full) {
                // Should not be updated. Don't mess up their fully recorded book
                return false;
            } else if (recordingStatus === RecordingStatus.Partial) {
                // Not fully recorded. We're expecting them to make another recording.
                // We'll go ahead and update the markup so that if/when they do record it, it'll be in the right state
                // Also eliminates some tricky cases like if it's partially recorded and user edits the unrecorded span.
                return true;
            } else {
                // No recordings: Safe to update if we want (no risk of audio loss). Might as well (make sure parsed with latest algo/settings).
                return true;
            }
        }
    }

    // Determines whether the text box is fully, partially, or not recorded all in one go,
    // without multiple sequential awaits.
    private async getRecordingStatusAsync(): Promise<RecordingStatus> {
        const asyncTasks = [
            this.isFullyRecordedAsync(),
            this.areAnyRecordingsPresentAsync()
        ];

        const [isFullyRecorded, isPartiallyRecorded] = await Promise.all(
            asyncTasks
        );
        if (isFullyRecorded) {
            return RecordingStatus.Full;
        } else if (isPartiallyRecorded) {
            return RecordingStatus.Partial;
        } else {
            return RecordingStatus.None;
        }
    }

    private areAnyRecordingsOutOfDate(): boolean {
        return this.getOutOfDateRecordings().length > 0;
    }

    // Returns true if any of the checksums for the audio-sentences within this recordable don't match (or don't eixist)
    private getOutOfDateRecordings(): HTMLElement[] {
        // These are the elements that physically correspond to the audio recording
        const sentences = this.getAudioSentences();

        const outOfDateSentences = sentences.filter(sentence =>
            this.isSentenceOutOfDate(sentence)
        );

        return outOfDateSentences;
    }

    // Returns true is the sentence does not match its checksum. (A missing checksum does not count as out of date).
    private isSentenceOutOfDate(sentence: HTMLElement): boolean {
        if (this.isMissingRecordingChecksum(sentence)) {
            return false;
        }

        const previousChecksum = sentence.getAttribute("recordingmd5");
        const currentChecksum = AudioRecording.getChecksum(sentence.innerText);
        return previousChecksum !== currentChecksum;
    }
}
