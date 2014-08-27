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
(function($) {

    /**
     *
     * @param {Function} [callbackDone] Optional function to call when done.
     */
    $.fn.localize = function(callbackDone) {

        // get all the localization keys not already in the dictionary
        var d = {};
        this.each(function() {
            var key = this.dataset.i18n;
            if (!localizationManager.dictionary[key])
                d[key] = $(this).text();
        });

        if (Object.keys(d).length > 0) {
            // get the translations and localize
            localizationManager.loadStrings(d, this, callbackDone);
        }
        else {
            // just localize
            this.each(function() {
                localizationManager.setElementText(this);
            });

            if (typeof callbackDone === 'function') callbackDone();
        }


    };

}(jQuery));
