import { splitIntoGraphemes } from "./textUtils";

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
