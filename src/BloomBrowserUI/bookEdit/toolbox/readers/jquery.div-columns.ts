import jQuery from "jquery";

/**
 * jquery.div-columns.js
 *
 * Lines up divs into columns
 *
 * Created Jun 9, 2014 by Hopper
 *
 */

interface JQueryStatic {
    divsToColumns(cssClassName: string): void;
    divsToColumnsBasedOnLongestWord(cssClassName: string, longestWord: string);
}

/**
 * Use an 'Immediately Invoked Function Expression' to make this compatible with jQuery.noConflict().
 * @param {jQuery} $
 */
(($) => {
    $.extend({
        /**
         * Set the width of the column (div) to be a multiple of the min-width.
         * NOTE: left and right margin are added for divs that span more than one column so the columns
         * will line up correctly.
         * @param {String} cssClassName
         */
        divsToColumns: (cssClassName) => {
            const div = $("div." + cssClassName + ":first");
            if (div.length === 0) return;

            const minWidth = parseInt(div.css("min-width"));
            const marginLeft = parseInt(div.css("margin-left"));
            const marginRight = parseInt(div.css("margin-right"));

            // limit the list to elements with text wider than min-width allows
            const elements = $("div." + cssClassName).filter(function () {
                return this.offsetWidth > minWidth;
            });

            elements.css("width", () => {
                let w = this.offsetWidth;
                const i = Math.ceil(w / minWidth);
                w = minWidth * i + (i - 1) * (marginLeft + marginRight);
                return w + "px";
            });
        },

        /**
         * Sets the number of columns based on the width of the longest word.
         * NOTE: This method was added because divsToColumns is too slow for lists of more that a few hundred items.
         * @param {String} cssClassName
         * @param {String} longestWord
         */
        divsToColumnsBasedOnLongestWord: (
            cssClassName: string,
            longestWord: string,
        ) => {
            const div = $("div." + cssClassName + ":first");
            if (div.length === 0) return;

            const parent = div.parent();
            const parentWidth = parent.width() - 20;

            const elements = $("div." + cssClassName);
            if (elements.length === 0) return;

            const maxWidth: number = textWidth(div, longestWord);

            let colCount = Math.floor(parentWidth / maxWidth);
            if (colCount === 0) colCount = 1;
            parent.css("column-count", colCount);
        },
    });

    /**
     * Calculates the width of an element containing the specified text
     * @param {JQuery} div
     * @param {String} text
     * @returns {number}
     */
    function textWidth(div: JQuery, text: string): number {
        const _t = jQuery(div);
        const html_calcS = "<span>" + text + "</span>";
        jQuery("body").append(html_calcS);

        const _lastSpan = jQuery("span").last();
        _lastSpan.css({
            "font-size": _t.css("font-size"),
            "font-family": _t.css("font-family"),
        });

        const width = _lastSpan.width() + 5;

        _lastSpan.remove();
        return width;
    }
})(jQuery);
