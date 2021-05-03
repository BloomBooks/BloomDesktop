import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

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
