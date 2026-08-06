import { describe, it, expect } from "vitest";
import { getChecksum } from "./talkingBookChecksum";
import { getMd5 } from "./md5Util";

// BL-16586
// The vertical-line character ("|") is a temporary phrase delimiter that the user inserts to
// split a sentence into phrases for recording and then deletes once the audio is made. The
// checksum has to ignore every "|" so that deleting the delimiters doesn't make the recording
// look out of date (which would regenerate the markup and lose the phrase-level audio).
describe("getChecksum", () => {
    it("ignores EVERY vertical-line character, not just the first (BL-16586)", () => {
        // A single audio-sentence can contain more than one phrase delimiter.
        const withDelimiters = "one | two | three.";
        // What is left after the user deletes only the "|" characters.
        const withoutDelimiters = "one  two  three.";

        // Sanity check: the two strings really do differ, so matching checksums are meaningful.
        expect(withDelimiters).not.toBe(withoutDelimiters);

        expect(getChecksum(withDelimiters)).toBe(
            getChecksum(withoutDelimiters),
        );
    });

    it("still removes a single vertical-line character", () => {
        expect(getChecksum("This is a test,| this is only a test.")).toBe(
            getChecksum("This is a test, this is only a test."),
        );
    });

    it("computes the md5 of the delimiter-stripped text", () => {
        // Sanity check: the raw (un-stripped) md5 is different, so this asserts stripping happened.
        expect(getMd5("a|b|c")).not.toBe(getMd5("abc"));

        expect(getChecksum("a|b|c")).toBe(getMd5("abc"));
    });
});
