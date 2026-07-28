import uiFontFacesCss from "../bloomUIFontFaces.css?raw";

const kUiFontFacesStyleId = "bloom-ui-font-faces";

// Matches one @font-face rule. Our rules never nest braces, so [^}]* is a safe body match.
// Prose mentions of "@font-face" in the file's comment block are not followed by "{", so
// they don't match.
const kFontFaceRule = /@font-face\s*\{[^}]*\}/g;
const kFamilyDeclaration = /font-family:\s*([^;}]+)/;

// CSS family names are quoted inconsistently and match case-insensitively, so compare on a
// normalized form.
const normalizeFamily = (family: string) =>
    family.trim().replace(/["']/g, "").toLowerCase();

// Picks out the @font-face rules in cssText whose family is NOT among alreadyDeclaredFamilies.
// Exported so the filtering can be unit tested against the real bloomUIFontFaces.css: vitest
// runs with Vite's default css:false, under which a `?raw` import of a .css file yields an
// EMPTY string, so a test of ensureUiFontFacesInstalled() itself could never see real rules.
export function selectMissingFontFaceRules(
    cssText: string,
    alreadyDeclaredFamilies: Iterable<string>,
): string[] {
    const declared = new Set(
        Array.from(alreadyDeclaredFamilies, normalizeFamily),
    );
    return (cssText.match(kFontFaceRule) ?? []).filter((rule) => {
        const family = rule.match(kFamilyDeclaration);
        if (!family)
            throw new Error(`@font-face rule with no font-family: ${rule}`);
        return !declared.has(normalizeFamily(family[1]));
    });
}

// Makes sure the current document has the @font-face declarations for Bloom's UI fonts
// (Roboto, NotoSans, SymChar, and the Andika fallback of BL-14102), installing the ones -- and
// only the ones -- it is missing.
//
// Why we filter per family rather than injecting all or nothing (BL-15300): if a duplicate
// @font-face for a family that is already rendered gets added to a document, CSS font matching
// switches the rendered text to the new, not-yet-loaded copy of the font, and the text goes
// blank for several frames while it re-loads. Documents can arrive here already declaring SOME
// of our families but not others -- most importantly the book preview documents, whose
// defaultLangStyles.css Bloom prepends the served-font faces (Andika, ABeeZee, pointing at
// /host/fonts/) to, while still lacking Roboto/NotoSans/SymChar. An all-or-nothing check
// would either strand those documents without the UI fonts or hand them a late duplicate
// Andika; injecting only the missing families does neither.
//
// The doc.fonts check is reliable at the time we are called because pending stylesheet
// <link>s block script execution, so any statically-linked declarations -- whether from
// editMode.css and friends or from the book's own defaultLangStyles.css -- are already in the
// font set before any of our code runs.
export function ensureUiFontFacesInstalled(doc: Document = document): void {
    if (doc.getElementById(kUiFontFacesStyleId)) return;
    // doc.fonts is undefined in jsdom (unit tests); there, treat nothing as declared and
    // fall through to injecting everything, which is harmless.
    const declaredFamilies = doc.fonts
        ? Array.from(doc.fonts, (face) => face.family)
        : [];
    const missingRules = selectMissingFontFaceRules(
        uiFontFacesCss,
        declaredFamilies,
    );
    if (missingRules.length === 0) return;
    const style = doc.createElement("style");
    style.id = kUiFontFacesStyleId;
    style.textContent = missingRules.join("\n");
    doc.head.appendChild(style);
}
