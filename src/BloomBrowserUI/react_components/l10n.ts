import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

/**
 * Gets and formats the localized string asynchronously using a callback
 * @param english - The English text
 * @param l10nKey - The key to look up in the XLF files
 * @param l10nComment - Optional. The comment or note
 * @param l10nParams - Optional. If it is a format string, the arguments to pass to the format string to replace {0}, {1}, etc. or %0, %1, etc.
 * @param temporarilyDisableI18nWarning - If true, doesn't warn if the key is missing
 * @param callback - A callback that runs after the translation is retrieved and format string substitutions are performed.
 */
export function getLocalization({
    english,
    l10nKey,
    l10nComment,
    l10nParams,
    temporarilyDisableI18nWarning,
    callback
}: {
    english: string;
    l10nKey: string;
    l10nComment?: string;
    l10nParams?: string[];
    temporarilyDisableI18nWarning?: boolean;
    callback: (localizedText: string, success: boolean) => void;
}) {
    if (temporarilyDisableI18nWarning === undefined)
        temporarilyDisableI18nWarning = false;
    theOneLocalizationManager
        .asyncGetTextAndSuccessInfo(
            l10nKey,
            english,
            l10nComment,
            temporarilyDisableI18nWarning
        )
        .done(result => {
            const text = result.text ?? english;
            let incorporatingParameters = theOneLocalizationManager.simpleFormat(
                text,
                l10nParams || []
            );

            // some legacy strings will have an ampersand which winforms interpreted as an accelerator key
            // enhance: we could conceivably implement this, using the html "accesskey" attribute
            if (incorporatingParameters.indexOf("&") == 0) {
                incorporatingParameters = incorporatingParameters.substring(
                    1,
                    9999
                );
            }
            callback(incorporatingParameters, result.success);
        });
}
