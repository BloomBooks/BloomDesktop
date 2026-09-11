import { describe, it, expect } from "vitest";
import { findLinkTextBrackets, splitIntoGraphemes } from "./textUtils";

describe("split into grapheme tests", () => {
    it("handles diacritics followed by space", () => {
        const letters = splitIntoGraphemes("ceatṽ̛̆ s");
        console.log(letters);
        expect(letters.length).toBe(7);
        expect(letters[0]).toBe("c");
        expect(letters[1]).toBe("e");
        expect(letters[2]).toBe("a");
        expect(letters[3]).toBe("t");
        expect(letters[4]).toBe("ṽ̛̆");
        expect(letters[5]).toBe(" ");
        expect(letters[6]).toBe("s");
    });
});

describe("findLinkTextBrackets", () => {
    // The marked-up words are the ones a caller turns into a hyperlink.
    const expectLinkText = (text: string, expected: string) => {
        const brackets = findLinkTextBrackets(text);
        if (!brackets) {
            throw new Error(`Found no link brackets at all in "${text}"`);
        }
        expect(text.substring(brackets.open + 1, brackets.close)).toBe(
            expected,
        );
    };

    it("finds the marked words in an ordinary string", () => {
        expectLinkText(
            "Most ePUB readers are very low quality ([see our research and recommendations]).",
            "see our research and recommendations",
        );
    });

    it("finds them when the string opens with the marker", () => {
        expectLinkText(
            "[Some Bloom features] are not supported by most or all ePUB readers.",
            "Some Bloom features",
        );
    });

    it("ignores the wrapper that pseudo-localization adds (BL-16748)", () => {
        // Pseudo-English brackets the whole string, so the first "[" is no longer the
        // link's. Taking the first "[" here would make the link swallow the sentence.
        expectLinkText(
            "[Möost éePÛUB réeåadéers ([séeée öoûur réeséeåarch]).]",
            "séeée öoûur réeséeåarch",
        );
        expectLinkText(
            "[[Söomée Blöoöom féeåatûurées] åarée nöot sûuppöortéed.]",
            "Söomée Blöoöom féeåatûurées",
        );
    });

    it("reports no pair when the string is not marked up", () => {
        expect(findLinkTextBrackets("Nothing to link here.")).toBeUndefined();
        expect(findLinkTextBrackets("Half a pair [ only.")).toBeUndefined();
        expect(
            findLinkTextBrackets("Closing ] with no opener."),
        ).toBeUndefined();
    });
});
