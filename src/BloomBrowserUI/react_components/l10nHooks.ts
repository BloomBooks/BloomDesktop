import React = require("react");
import { getLocalization } from "./l10n";
import { callbackify } from "util";

// React hook to lookup localization
export function useL10n(
    english: string,
    // Can be null (not undefined!) if you want us to return the "english" as the translation
    // Why would you even call this? Because useL10n, like all hooks, cannot be called conditionally.
    l10nKey: string | null,
    l10nComment?: string,
    l10nParam0?: string,
    l10nParam1?: string
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
                // Enhance: if lookupSuccessful is false AND we're in the debug/alpha etc (see l10ncomponents), prefix with *** or something.
                callback: (t, lookupSuccessful) => {
                    if (lookupSuccessful) {
                        setLocalizedText(t);
                    }
                }
            });
        }
    }, [l10nParam0, l10nParam1]); // often the params are coming in later, via an api call. So we need to re-do the localization when that happens.
    return localizedText;
}
