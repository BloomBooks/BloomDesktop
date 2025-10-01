/// <reference path="localizationManager/localizationManager.ts" />
/// <reference path="../typings/jquery/jquery.d.ts" />
import jQuery from "jquery";
import theOneLocalizationManager from "./localizationManager/localizationManager";

interface JQuery {
    // eslint-disable-next-line  @typescript-eslint/no-unsafe-function-type
    localize(callbackDone?: Function): void;
}
/**
 * jquery.i18n.custom.js
 *
 * L10NSharp LocalizationManager for javascript
 *
 */

/**
 * Use an 'Immediately Invoked Function Expression' to make this compatible with jQuery.noConflict().
 * @param {jQuery} $
 */
(($) => {
    /**
     *
     * @param [callbackDone] Optional function to call when done.
     */
    $.fn.localize = function (callbackDone?: any) {
        // get all the localization keys not already in the dictionary
        const d = {};
        this.each(function () {
            const key = this.dataset["i18n"];
            if (!theOneLocalizationManager.dictionary[key])
                d[key] = $(this).text();
        });

        if (Object.keys(d).length > 0) {
            // get the translations and localize
            theOneLocalizationManager.loadStrings(d, this, callbackDone);
        } else {
            // just localize
            this.each(function () {
                theOneLocalizationManager.setElementText(this);
            });

            if (typeof callbackDone === "function") callbackDone();
        }
    };
})(jQuery);
