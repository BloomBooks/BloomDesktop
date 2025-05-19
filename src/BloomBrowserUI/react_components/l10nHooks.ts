import * as React from "react";
import { getLocalization } from "./l10n";

/**
 * React hook to lookup localization
 * Gets and formats the localized string asynchronously using a callback
 * @param english - The English text
 * @param l10nKey - The key to look up in the XLF files. Can be null (not undefined!) if you want us to return the "english" as the translation (useful because hooks cannot be called conditionally).
 * @param l10nComment - Optional. The comment or note
 * @param l10nParam0 - Optional. If it is a format string, the argument to pass to the format string to replace {0} or %0
 * @param l10nParam1 - Optional. If it is a format string, the argument to pass to the format string to replace {1} or %1
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
): string {
    // Create an array of parameters, filtering out undefined values
    const l10nParams: string[] = React.useMemo(() => {
        return [l10nParam0, l10nParam1].filter(
            (param): param is string => !!param
        );
    }, [l10nParam0, l10nParam1]);

    // Use useL10n2 internally with the object parameter format
    return useL10n2({
        english,
        key: l10nKey,
        comment: l10nComment,
        params: l10nParams,
        temporarilyDisableI18nWarning
    });
}

/**
 * React hook to lookup localization with a single parameter object
 * Gets and formats the localized string asynchronously using a callback
 * @param options - Object containing localization options
 * @param options.english - The English text
 * @param options.l10nKey - The key to look up in the XLF files. Can be null (not undefined!) if you want us to return the "english" as the translation (useful because hooks cannot be called conditionally).
 * @param options.l10nComment - Optional. The comment or note
 * @param options.l10nParams - Optional. Array of parameters to pass to the format string
 * @param options.temporarilyDisableI18nWarning - If true, doesn't warn if the key is missing
 */
export function useL10n2(options: {
    english?: string;
    key: string | null;
    comment?: string;
    params?: string[];
    temporarilyDisableI18nWarning?: boolean;
}): string {
    const {
        english,
        key: l10nKey,
        comment: l10nComment,
        params: l10nParams,
        temporarilyDisableI18nWarning
    } = options;
    const stringifiedParams = JSON.stringify(l10nParams);
    const [localizedText, setLocalizedText] = React.useState(english);
    React.useEffect(
        () => {
            if (!l10nKey || l10nKey === "already-localized") {
                window.setTimeout(() => setLocalizedText(english), 0);
            } else {
                getLocalization({
                    english: english || "", // no need actually to duplicate the English when we have to put it in the XLF anyways
                    l10nKey,
                    l10nComment,
                    l10nParams,
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
        },
        // Use JSON.stringify(l10nParams) as a dep instead of l10nParams
        // so we don't have to rerun whenever we don't memoize the params
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [
            l10nKey,
            english,
            l10nComment,
            stringifiedParams,
            temporarilyDisableI18nWarning
        ]
    ); // often the params are coming in later, via an api call. So we need to re-do the localization when that happens.
    return localizedText || "";
}
