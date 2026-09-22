import {
    IOrthography,
    defaultRegionForLangTag,
    defaultDisplayName,
} from "@ethnolib/language-chooser-react-mui";

export interface ILanguageData {
    // Should be kept in sync with the LanguageChangeEventArgs class
    LanguageTag: string | null;
    DefaultName: string | null;
    DesiredName: string | null;
    IsRtl: boolean | null;
    Country?: string | null;
}

// Everything the language chooser tells us about a selection has to funnel through here on its
// way to C#. Three shipped bugs (BL-15190, BL-13982, BL-14426) were all defects in this mapping,
// so it lives in its own module with its own tests rather than inside the dialog component.
//
// The name precedence below -- the selected script's name for the language, else the language's
// own name -- duplicates what EthnoLib's defaultDisplayName(language, script) already does.
// Unifying the two is tempting, but it is NOT a no-op: defaultDisplayName returns "" for the
// unlisted language and for manually-entered-tag languages even when a script is supplied, where
// this code returns that script's name. Today the chooser never gives either of those a script,
// so nothing observable changes -- but that is a fact about the chooser's current internals, not
// a promise this function is entitled to rely on. languageData.test.ts pins the difference; if
// you do unify them, those tests will fail, and you should make sure the new answer is the one
// you want rather than just updating them.
export function getLanguageData(
    languageTag: string | undefined,
    selection: IOrthography | undefined,
): ILanguageData {
    const nameInScript = selection?.script?.languageNameInScript;
    const defaultName =
        nameInScript ||
        (selection?.language
            ? defaultDisplayName(selection.language) || null
            : null);
    const isRtl = selection?.script?.isRtl;
    // Ensure values are null rather than undefined. Otherwise, the property won't be serialized at all.
    return {
        LanguageTag: languageTag || null,
        DefaultName: defaultName,
        DesiredName: selection?.customDetails?.customDisplayName || defaultName,
        IsRtl: isRtl !== undefined ? isRtl : null,
        Country: languageTag
            ? defaultRegionForLangTag(languageTag, selection?.language)?.name ||
              null
            : null,
    };
}
