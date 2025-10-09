// This is the root file for the spreadsheet bundle

// This exports the functions that should be accessible from other IFrames or from C#.
// For example, spreadsheetBundle.split() can be used.
import { theOneLibSynphony } from "../bookEdit/toolbox/readers/libSynphony/synphony_lib";
import {
    TextFragment,
    addBloomSynphonyExtensions,
} from "../bookEdit/toolbox/readers/libSynphony/bloomSynphonyExtensions";
import AudioRecording from "../bookEdit/toolbox/talkingBook/audioRecording";

// If a string only contains | and whitespace, it is a "segment" with no text
// and should be skipped when matching to audio segments
function isEmptySegment(s: string): boolean {
    return s.search(/[^|\s]/) === -1;
}

export function split(text: string): string {
    const fragments: TextFragment[] = theOneLibSynphony.stringToSentences(text);
    const sentences = fragments.map(
        (f) => (f.isSentence && !isEmptySegment(f.text) ? "s" : " ") + f.text,
    );
    return sentences.join("\n");
}

export function getMd5(input: string): string {
    return AudioRecording.getChecksum(input);
}

// In some contexts, this seems to happen automatically when bloomSynphonyExtensions is loaded, since
// there is immediate-execute code there which calls it. But it doesn't happen for this bundle unless
// I do it explicitly, so here we go. (One of the extensions added is the implementation of stringToSentences.)
addBloomSynphonyExtensions();

// Legacy global exposure: mimic old webpack window["spreadsheetBundle"] contract
interface SpreadsheetBundleApi {
    split: typeof split;
    getMd5: typeof getMd5;
}

declare global {
    interface Window {
        spreadsheetBundle: SpreadsheetBundleApi;
    }
}

window.spreadsheetBundle = {
    split,
    getMd5,
};
