import { AudioRecordingMode } from "./audioRecording";
import axios from "axios";

const kAudioSentence = "audio-sentence";

export default class Recordable {
    private textBox: HTMLElement;

    public constructor(textBox: HTMLElement) {
        this.textBox = textBox;
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
            !this.textBox.classList.contains(kAudioSentence) &&
            this.textBox.getElementsByClassName(kAudioSentence).length === 0
        ) {
            return false;
        }
        if (
            modeAttribute == AudioRecordingMode.Sentence &&
            !this.textBox.classList.contains(kAudioSentence) &&
            this.textBox.getElementsByTagName("SPAN").length === 0
        ) {
            return false;
        }
        return true;
    }

    public getAudioSentences(): HTMLElement[] {
        const audioSentences: HTMLElement[] = [];
        if (this.textBox.classList.contains("audio-sentence")) {
            audioSentences.push(this.textBox);
        } else {
            // This only matches strict descendants, not itself
            const matchingDescendants = this.textBox.querySelectorAll(
                ".audio-sentence"
            );
            matchingDescendants.forEach((element: Element) => {
                audioSentences.push(element as HTMLElement);
            });
        }

        return audioSentences;
    }

    public getAudioSentenceIds(): string[] {
        const ids: string[] = [];
        this.getAudioSentences().forEach((element: Element) => {
            console.assert(
                element.id,
                "Element unexpectedly had falsy ID: " + element.innerHTML
            );
            if (element.id) {
                ids.push(element.id);
            }
        });

        return ids;
    }

    public async areRecordingsPresentAsync(): Promise<boolean> {
        const idsToCheck: string[] = this.getAudioSentenceIds();
        if (!idsToCheck) {
            return false;
        }

        try {
            await axios.get(
                `/bloom/api/audio/checkForAnyRecording?ids=${idsToCheck}`
            );
            return true;
        } catch {
            return false;
        }
    }
}
