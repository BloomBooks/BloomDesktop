import * as React from "react";
import { getLocalization } from "./l10n";

/**
 * React hook to lookup localization
 * Gets and formats the localized string asynchronously using a callback
 * @param english - The English text
 * @param l10nKey - The key to look up in the XLF files. Can be null (not undefined!) if you want us to return the "english" as the translation (useful because hooks cannot be called conditionally).
 * @param l10nComment - Optional. The comment or note
 * @param l10nParam0 - If it is a format string, the argument to pass to the format string to replace {0} or %0
 * @param l10nParam1 - If it is a format string, the argument to pass to the format string to replace {1} or %1
 * @param temporarilyDisableI18nWarning - If true, doesn't warn if the key is missing
 */
export function useL10n(
    english: string,
    // Can be null (not undefined!) if you want us to return the "english" as the translation
    // Why would you even call this? Because useL10n, like all hooks, cannot be called conditionally.
    l10nKey: string | null,
    l10nComment?: string,
    l10nParam0?: string,
    l10nParam1?: string,
    temporarilyDisableI18nWarning?: boolean
) {
    const [localizedText, setLocalizedText] = React.useState(english);
    React.useEffect(() => {
        if (l10nKey == null) {
            window.setTimeout(() => setLocalizedText(english), 0);
        } else {
            getLocalization({
                english,
                l10nKey,
                l10nComment,
                l10nParam0,
                l10nParam1,
                temporarilyDisableI18nWarning,
                // Enhance: if lookupSuccessful is false AND we're in the debug/alpha etc (see l10ncomponents), prefix with *** or something.
                callback: (t, lookupSuccessful) => {
                    if (lookupSuccessful) {
                        setLocalizedText(t);
                    } else {
                        // Enhance: maybe we should do something here if temporarilyDisableI18nWarning is false?
                        // But we should also check for being in debug or possibly alpha...how?
                        // In any case, if we don't set the text to the output of getLocalization(), we don't
                        // get param insertion, which makes things broken even in English
                        setLocalizedText(t);
                    }
                }
            });
        }
    }, [l10nParam0, l10nParam1]); // often the params are coming in later, via an api call. So we need to re-do the localization when that happens.
    return localizedText;
}
