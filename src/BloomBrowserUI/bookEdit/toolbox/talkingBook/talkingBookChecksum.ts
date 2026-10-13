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
    // To perform phrase-level recording, the user inserts a "|" wherever a phrase split should
    // happen. "|" is in the list of sentence delimiters, so each phrase becomes its own
    // audio-sentence and can be recorded separately.
    //
    // The bars then stay in the text; the user does not delete them (much older versions of
    // Bloom did require that). Instead, closing the Talking Book tool replaces each bar with a
    // zero-width <span class="bloom-audio-split-marker">, so the bars are invisible outside the
    // tool, and reopening the tool turns them back into real "|" characters.
    //
    // Either way the bars are markup rather than content, so the checksum has to ignore them:
    // adding, moving or removing a bar does not change the words that were recorded, and we
    // don't want that to make Bloom think a recording no longer matches its text.
    //
    // Note the global replace. A single audio-sentence can hold more than one bar, because a run
    // of adjacent bars is deliberately collapsed into a single split (so "Delta ||| epsilon."
    // gives the chunks "Delta |||" and "epsilon."), and every bar in it has to be
    // stripped (BL-16586).
    const adjustedMessage = message.replace(/\|/g, "");
    return getMd5(adjustedMessage);
}
