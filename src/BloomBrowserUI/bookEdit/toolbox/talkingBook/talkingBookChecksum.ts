// taken out of audioRecording.ts to avoid the need for other
// files to import that big file just to use a little bit of
// code.

import { getMd5 } from "./md5Util";

export function getChecksum(message: string): string {
    if (message === null || message === undefined) {
        // should not normally happen, but seems to in tests.
        // The function is supposed to return a string, and I don't want to change
        // all the callers, so making it return a string that's a bit unique so if
        // we ever see it in production we can search for it.
        return "undefind";
    }
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
