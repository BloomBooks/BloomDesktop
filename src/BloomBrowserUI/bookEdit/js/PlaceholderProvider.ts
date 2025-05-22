/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
// This collectionSettings reference defines the function GetSettings(): ICollectionSettings
// The actual function is injected by C#.
/// <reference path="./collectionSettings.d.ts" />
/// <reference path="bloomQtipUtils.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import bloomQtipUtils from "./bloomQtipUtils";

export default class PlaceholderProvider {
    // "Eager" Placeholders want to grow up to work like this:
    // If we have a translation in L1, we just set that bloom-editable (this is the "eager" part).
    // Else we get the translation in the UI language, or if not then the English language.
    // Then we will show that with opacity 0.5 but we don't actually set the bloom-editable.
    // But for now, we're just making them little tooltip things.
    public static addPlaceholders(container: HTMLElement): void {
        $(container)
            .find("*[data-eager-placeholder-l10n-id]")
            .each(async function() {
                const l10nId = $(this).attr("data-eager-placeholder-l10n-id");
                if (!l10nId) return;

                if (l10nId.length == 0 || $(this).css("display") == "none")
                    return;

                // Enhance to make this actually fil in the text if we have a translation in L1, we need to do the following:
                // 1) we need to know what language we found the translation in (need to use a different function to get the translation)
                // 2) find the div.bloom-editable with class "bloom-content1" (it's expected something has already create the bloom-editable in L1)
                // 3) if it is empty-ish (e.g. empty paragraph) and its lang attribute is the same as the one we found the translation in
                // 4) set the div.bloom-editable value

                const langId = theOneLocalizationManager.getCurrentUILocale(); // TODO we instead want the L1 language
                const result = await theOneLocalizationManager.asyncGetTextInLangCommon(
                    l10nId,
                    "didn't get it",
                    langId,
                    "comment",
                    false, // englishDefault
                    true, // return successInfo
                    false,
                    []
                );

                // TODO for the "eager" implementation: the problem is that the API seems to define "success" as "we got a translation in the UI language or English".
                // Even the lower level lookup just happily gives English with no way for the c# API to know what the retrieved language was.

                if (!result.success) {
                    // we didn't get a translation in the UI lang, but we would still need to know if the L1
                }

                PlaceholderProvider.makeTip($(this), result.text, true);
            });
    }

    // note the particular behavior here isn't necessarily intentional. We just copied it from HintBubble.
    private static async makeTip(
        target: JQuery,
        l10nText: string,
        shouldShowAlways: boolean
    ) {
        const pos = {
            at: "right center",
            my: "left center",
            viewport: $(window),
            adjust: { method: "none" },
            container: bloomQtipUtils.qtipZoomContainer()
        };

        const theClasses = "ui-tooltip-shadow ui-tooltip-plain";
        const hideEvents = shouldShowAlways ? false : "focusout mouseleave";

        target.qtip({
            content: l10nText,
            position: pos,
            show: {
                event: "focusin mouseenter",
                ready: shouldShowAlways //would rather have this kind of dynamic thing, but it isn't right: function(){$(this).is(':empty')}//
            },
            hide: {
                event: hideEvents
            },
            style: {
                classes: theClasses
            }
        });
    }
}
