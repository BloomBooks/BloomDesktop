import { AudioRecordingMode } from "./audioRecording";
import axios from "axios";

const kAudioSentence = "audio-sentence";

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
    public async areRecordingsPresentAsync(): Promise<boolean> {
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
}
