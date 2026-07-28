import * as fs from "node:fs";
import * as path from "node:path";
import { describe, it, expect } from "vitest";
import { selectMissingFontFaceRules } from "./uiFontFaces";

// These cover the per-family filtering that keeps BL-15300's flicker from coming back through a
// partially-declared document. The interesting case is the book preview documents: Bloom
// prepends the served book fonts (Andika, ABeeZee) to each book's defaultLangStyles.css, so
// those documents already have an Andika face but lack Roboto/NotoSans/SymChar. Injecting the
// whole file there would add a SECOND, late Andika face and blank the already-rendered text;
// injecting nothing would leave the UI fonts missing. Only the missing families may go in.
//
// We read bloomUIFontFaces.css from disk rather than importing it: vitest runs with Vite's
// default css:false, under which `import x from "...css?raw"` yields an empty string. Reading
// the real file also means these tests fail if its shape ever stops matching what the
// filtering assumes.
const uiFontFacesCss = fs.readFileSync(
    path.resolve(__dirname, "../bloomUIFontFaces.css"),
    "utf8",
);

const familiesIn = (rules: string[]) =>
    new Set(
        rules.map((rule) => {
            const match = rule.match(/font-family:\s*([^;}]+)/);
            if (!match) throw new Error(`no font-family in rule: ${rule}`);
            return match[1].trim().replace(/["']/g, "");
        }),
    );

const kAllFamilies = ["Roboto", "NotoSans", "SymChar", "Andika"];

describe("selectMissingFontFaceRules", () => {
    it("finds the real file's rules and no prose (sanity check on the source file)", () => {
        // The file's comment block mentions "@font-face" several times in prose; those must not
        // be picked up as rules. 15 faces: Roboto x3, NotoSans x10, SymChar, Andika.
        const rules = selectMissingFontFaceRules(uiFontFacesCss, []);
        expect(rules.length).toBe(15);
        expect(familiesIn(rules)).toEqual(new Set(kAllFamilies));
        for (const rule of rules) {
            expect(rule.startsWith("@font-face")).toBe(true);
        }
    });

    it("skips a family the document already declares but keeps the rest", () => {
        // This is the book-preview shape: Andika present (from defaultLangStyles.css), the
        // UI-only families absent. ABeeZee is declared there too and is simply irrelevant here.
        const rules = selectMissingFontFaceRules(uiFontFacesCss, [
            "Andika",
            "ABeeZee",
        ]);

        expect(familiesIn(rules)).toEqual(
            new Set(["Roboto", "NotoSans", "SymChar"]),
        );
        // All ten NotoSans faces must survive, not just the first.
        expect(rules.length).toBe(14);
    });

    it("returns nothing when every family is already declared", () => {
        // The edit page shape: editMode.css already carries the whole set, so renderRoot()'s
        // call must add nothing at all.
        expect(
            selectMissingFontFaceRules(uiFontFacesCss, kAllFamilies),
        ).toEqual([]);
    });

    it("matches family names regardless of quoting and case", () => {
        // bloomUIFontFaces.css writes "Roboto" quoted; a document may report it unquoted and/or
        // differently cased, and CSS family matching is case-insensitive.
        const rules = selectMissingFontFaceRules(uiFontFacesCss, [
            '"roboto"',
            "NOTOSANS",
            "symchar",
            "  Andika  ",
        ]);
        expect(rules).toEqual([]);
    });

    it("keeps every rule when the document declares nothing relevant", () => {
        const rules = selectMissingFontFaceRules(uiFontFacesCss, [
            "Times New Roman",
            "ABeeZee",
        ]);
        expect(familiesIn(rules)).toEqual(new Set(kAllFamilies));
    });

    it("throws on an @font-face rule with no font-family rather than silently dropping it", () => {
        expect(() =>
            selectMissingFontFaceRules("@font-face { src: url(x.woff2); }", []),
        ).toThrow(/no font-family/);
    });
});
