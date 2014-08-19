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

    $.fn.localize = function() {

        this.each(function() {
            localizationManager.setElementText(this);
        });
    };

}(jQuery));
