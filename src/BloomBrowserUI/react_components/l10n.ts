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
            let text = result.text;
            if (l10nParam0) {
                text = text.replace("%0", l10nParam0);
                text = text.replace("{0}", l10nParam0); // c# style
                if (l10nParam1) {
                    text = text.replace("%1", l10nParam1);
                    text = text.replace("{1}", l10nParam1); // c# style
                }
            }
            // some legacy strings will have an ampersand which winforms interpreted as an accelerator key
            // enhance: we could conceivably implement this, using the html "accesskey" attribute
            if (text.indexOf("&") == 0) {
                text = text.substring(1, 9999);
            }
            callback(text, result.success);
        });
}
