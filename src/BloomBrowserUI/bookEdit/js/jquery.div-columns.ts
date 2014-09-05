/// <reference path="../../lib/jquery.d.ts" />

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
}

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
		 * @param {String} cssClassName
		 */
		divsToColumns: function(cssClassName) {

			var elements = $('div.' + cssClassName);
			var div = elements.first();
			var minWidth = parseInt(div.css('min-width'));
			var marginLeft = parseInt(div.css('margin-left'));
			var marginRight = parseInt(div.css('margin-right'));

			elements.css('width', function() {
				var w = this.offsetWidth;
				var i =  Math.ceil(w / minWidth);
				w = minWidth * i + (i - 1) * (marginLeft + marginRight);
				this.style.width = w + 'px';
			});
		}
	});
}(jQuery));