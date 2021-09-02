import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

/**
 * Gets and formats the localized string asynchronously using a callback
 * @param english - The English text
 * @param l10nKey - The key to look up in the XLF files
 * @param l10nComment - Optional. The comment or note
 * @param l10nParam0 - If it is a format string, the argument to pass to the format string to replace {0} or %0
 * @param l10nParam1 - If it is a format string, the argument to pass to the format string to replace {1} or %1
 * @param temporarilyDisableI18nWarning - If true, doesn't warn if the key is missing
 * @param callback - A callback that runs after the translation is retrieved and format string substitutions are performed.
 */
export function getLocalization({
    english,
    l10nKey,
    l10nComment,
    l10nParam0,
    l10nParam1,
    temporarilyDisableI18nWarning,
    callback
}: {
    english: string;
    l10nKey: string;
    l10nComment?: string;
    l10nParam0?: string;
    l10nParam1?: string;
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
                [l10nParam0, l10nParam1]
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
