import theOneLocalizationManager from "../lib/localizationManager/localizationManager";

export function getLocalization({
    english,
    l10nKey,
    l10nComment,
    l10nParam0,
    l10nParam1,
    callback
}: {
    english: string;
    l10nKey: string;
    l10nComment?: string;
    l10nParam0?: string;
    l10nParam1?: string;
    callback: (localizedText: string, success: boolean) => void;
}) {
    theOneLocalizationManager
        .asyncGetTextAndSuccessInfo(l10nKey, english, l10nComment)
        .done(result => {
            let text = result.text;
            if (l10nParam0) {
                text = text.replace("%0", l10nParam0);
                if (l10nParam1) {
                    text = text.replace("%1", l10nParam1);
                }
            }
            callback(text, result.success);
        });
}
