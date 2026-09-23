import { describe, it, expect } from "vitest";
import {
    ILanguage,
    IOrthography,
    IScript,
} from "@ethnolib/language-chooser-react-mui";
import { getLanguageData } from "./languageData";

// Minimal stand-ins for what the language chooser hands us. The real objects carry a lot more
// (search-result metadata, alternative tags, region lists); only these fields reach getLanguageData.
function language(fields: Partial<ILanguage>): ILanguage {
    return {
        iso639_3_code: "che",
        languageSubtag: "che",
        autonym: "Нохчийн мотт",
        exonym: "Chechen",
        regionNamesForDisplay: "",
        regionNamesForSearch: [],
        scripts: [],
        alternativeTags: [],
        isMacrolanguage: false,
        names: [],
        ...fields,
    } as unknown as ILanguage;
}

function script(fields: Partial<IScript>): IScript {
    return { code: "Cyrl", name: "Cyrillic", ...fields } as unknown as IScript;
}

describe("getLanguageData", () => {
    it("returns all nulls when nothing is selected", () => {
        const data = getLanguageData(undefined, undefined);
        expect(data).toEqual({
            LanguageTag: null,
            DefaultName: null,
            DesiredName: null,
            IsRtl: null,
            Country: null,
        });
    });

    // BL-14426: the C# side reads these off a DynamicJson, and a field whose value is undefined
    // is dropped during serialization rather than arriving as null.
    it("never leaves a field undefined", () => {
        for (const data of [
            getLanguageData(undefined, undefined),
            getLanguageData("che", {} as IOrthography),
            getLanguageData("che", { language: language({}) } as IOrthography),
        ]) {
            for (const key of [
                "LanguageTag",
                "DefaultName",
                "DesiredName",
                "IsRtl",
                "Country",
            ]) {
                expect(data).toHaveProperty(key);
                expect(data[key as keyof typeof data]).not.toBeUndefined();
            }
        }
    });

    it("uses the language's autonym when no script is selected", () => {
        const data = getLanguageData("che", {
            language: language({}),
        } as IOrthography);
        expect(data.DefaultName).toBe("Нохчийн мотт");
        expect(data.LanguageTag).toBe("che");
    });

    it("falls back to the exonym when there is no autonym", () => {
        const data = getLanguageData("che", {
            language: language({ autonym: undefined }),
        } as IOrthography);
        expect(data.DefaultName).toBe("Chechen");
    });

    // BL-15190: picking a script has to change the name Bloom stores. Before the fix, Bloom kept
    // the language's own autonym and ignored the script the user had just chosen.
    it("prefers the selected script's name for the language over the autonym", () => {
        const data = getLanguageData("che-Arab", {
            language: language({}),
            script: script({
                code: "Arab",
                name: "Arabic",
                languageNameInScript: "نохچийн",
            }),
        } as IOrthography);
        expect(data.DefaultName).toBe("نохچийн");
    });

    it("keeps the autonym when the selected script has no name for the language", () => {
        const data = getLanguageData("che-Cyrl", {
            language: language({}),
            script: script({}),
        } as IOrthography);
        expect(data.DefaultName).toBe("Нохчийн мотт");
    });

    // C# decides whether a name is custom by comparing DefaultName with DesiredName
    // (CollectionSettingsDialog.cs), so these two must differ only when the user typed a name.
    it("reports an untouched name as not custom", () => {
        const data = getLanguageData("che", {
            language: language({}),
        } as IOrthography);
        expect(data.DesiredName).toBe(data.DefaultName);
    });

    it("reports a typed name as custom, leaving the default alongside it", () => {
        const data = getLanguageData("che", {
            language: language({}),
            customDetails: { customDisplayName: "My Chechen" },
        } as IOrthography);
        expect(data.DesiredName).toBe("My Chechen");
        expect(data.DefaultName).toBe("Нохчийн мотт");
        expect(data.DesiredName).not.toBe(data.DefaultName);
    });

    // BL-13982: Bloom stopped persisting right-to-left. false and undefined are different answers
    // here — false means "we know it is left-to-right", null means "the script did not say".
    it("passes the script's direction through, distinguishing false from unknown", () => {
        expect(
            getLanguageData("che-Arab", {
                language: language({}),
                script: script({ code: "Arab", isRtl: true }),
            } as IOrthography).IsRtl,
        ).toBe(true);

        expect(
            getLanguageData("che-Cyrl", {
                language: language({}),
                script: script({ isRtl: false }),
            } as IOrthography).IsRtl,
        ).toBe(false);

        expect(
            getLanguageData("che-Cyrl", {
                language: language({}),
                script: script({}),
            } as IOrthography).IsRtl,
        ).toBeNull();
    });

    it("fills in the country implied by the language tag", () => {
        expect(getLanguageData("en-Latn-US", undefined).Country).toBe(
            "United States of America",
        );
        expect(getLanguageData("uz", undefined).Country).toBe("Uzbekistan");
    });

    it("has no country without a language tag", () => {
        expect(
            getLanguageData(undefined, {
                language: language({}),
            } as IOrthography).Country,
        ).toBeNull();
    });

    // An unlisted language has no real name of its own, so the user is required to supply one and
    // that name is by definition custom.
    it("gives an unlisted language no default name", () => {
        const data = getLanguageData("qaa-x-whatcham", {
            language: language({ iso639_3_code: "qaa", languageSubtag: "qaa" }),
            customDetails: { customDisplayName: "Whatcham" },
        } as IOrthography);
        expect(data.DefaultName).toBeNull();
        expect(data.DesiredName).toBe("Whatcham");
    });
});

// These pin the corners where this function's answer differs from EthnoLib's
// defaultDisplayName(language, script). None of them is reachable through the chooser today: it
// never hands an unlisted or manually-entered-tag language a script, and never a script without a
// language. They exist so that folding the two implementations together is a decision somebody
// makes on purpose, with a failing test in front of them, rather than a silent change of
// behaviour in a mapping that has already produced three shipped bugs. See languageData.ts.
describe("getLanguageData: where it deliberately differs from defaultDisplayName", () => {
    const scriptWithName = () =>
        script({
            code: "Arab",
            name: "Arabic",
            languageNameInScript: "اسم",
        });

    it("still uses the script's name for an unlisted language", () => {
        // defaultDisplayName would return "" here, discarding the script's name.
        const data = getLanguageData("qaa-Arab-x-whatcham", {
            language: language({ iso639_3_code: "qaa", languageSubtag: "qaa" }),
            script: scriptWithName(),
        } as IOrthography);
        expect(data.DefaultName).toBe("اسم");
    });

    it("still uses the script's name for a manually entered language tag", () => {
        // isManuallyEnteredTagLanguage() keys off this iso639_3_code; see EthnoLib's
        // languageForManuallyEnteredTag().
        const data = getLanguageData("zzz-Arab", {
            language: language({
                iso639_3_code: "manuallyEnteredTag",
                languageSubtag: "manuallyEnteredTag",
                autonym: undefined,
                exonym: "",
            }),
            script: scriptWithName(),
        } as IOrthography);
        expect(data.DefaultName).toBe("اسم");
    });

    it("uses the script's name even with no language at all", () => {
        const data = getLanguageData("und-Arab", {
            script: scriptWithName(),
        } as IOrthography);
        expect(data.DefaultName).toBe("اسم");
    });

    it("passes a script name through without stripping search-match markers", () => {
        // EthnoLib runs its own answer through stripDemarcation(), which would remove these
        // brackets. Nothing demarcates languageNameInScript today, so this is latent either way.
        const data = getLanguageData("che-Arab", {
            language: language({}),
            script: script({ code: "Arab", languageNameInScript: "[Ноx]чийн" }),
        } as IOrthography);
        expect(data.DefaultName).toBe("[Ноx]чийн");
    });
});
