/**
 * jquery.div-columns.js
 *
 * Lines up divs into columns
 *
 * Created Jun 9, 2014 by Hopper
 *
 */

/**
 * Use an 'Immediately Invoked Function Expression' to make this compatible with jQuery.noConflict().
 * @param {jQuery} $
 */
(function($) {
    $.extend({

        /**
         * Set the width of the column (div) to be a multiple of the min-width.
         * NOTE: left and right margin are added for divs that span more than one column so the columns
         * will line up correctly.
         * @param {type} cssClassName
         */
        divsToColumns: function(cssClassName) {

            var div = $('div.' + cssClassName).first();
            var minWidth = parseInt(div.css('min-width'));
            var marginLeft = parseInt(div.css('margin-left'));
            var marginRight = parseInt(div.css('margin-right'));

            $('div.' + cssClassName).css('width', function() {

                // only size the divs once
                if ($(this).data('sized') === 1) return;

                // if the div is not visible, offsetWidth will be zero
                if (!this.offsetWidth) return;

                var w = this.offsetWidth;
                var i =  Math.ceil(w / minWidth);
                w = minWidth * i + (i - 1) * (marginLeft + marginRight);
                this.style.width = w + 'px';
                $(this).data('sized', 1);
            });
        }
    });
}(jQuery));