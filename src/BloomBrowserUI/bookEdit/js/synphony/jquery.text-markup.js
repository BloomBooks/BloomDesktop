/**
 * jquery.text-markup.js
 *
 * Marking text according to various rules
 *
 * Created Apr 24, 2014 by Phil Hopper
 *
 */

/**
 * Use an 'Immediately Invoked Function Expression' to make this compatable with jQuery.noConflict().
 * @param {jQuery} $
 */
(function($) {

  var cssSentenceTooLong = 'sentence-too-long';

  /**
   * Checks the innerHTML of an HTML entity (div) using the selected options
   * @param {Object} options
   * @returns {Object}
   */
  $.fn.checkLeveledReader = function(options) {

    var opts = $.extend({maxWordsPerSentence: 999}, options);

    this.each(function() {

      // remove previous sentence markup
      $(this).find('span[data-segment=sentence]').contents().unwrap();

      // split into sentences
      var fragments = stringToSentences($(this).html());
      var newHtml = '';

      for (var i = 0; i < fragments.length; i++) {

        var fragment = fragments[i];

        if (fragment.isSpace) {

          // this is inter-sentence space
          newHtml += fragment.text;
        }
        else {

          // check sentence length
          if (fragment.wordCount() > opts.maxWordsPerSentence) {

            // tag sentence as having too many words
            newHtml += '<span class="' + cssSentenceTooLong + '" data-segment="sentence">' + fragment.text + '</span>';
          }
          else {

            // nothing to see here
            newHtml += fragment.text;
          }
        }
      }

      // set the html
      $(this).html(newHtml);
    });

    return this;
  };

  $.fn.checkDecodableReader = function(options) {

    var opts = $.extend({focusLetters: '', previousLetters: '', sightWords: '', maxSyllables: 99}, options);

    this.each(function() {

      var elem = $(this);
      var html = elem.html();

      // TODO: do some work here

      // set the html
      elem.html(html);
    });

    return this;
  };
}(jQuery));
