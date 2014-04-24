/**
 * bloom_lib.js
 *
 * Support for Bloom
 *
 * Created Apr 14, 2014 by Phil Hopper
 *
 */

/**
 * Class that holds text fragment information
 * @param {String} str The text of the fragment
 * @param {Boolean} isSpace <code>TRUE</code> if this fragment is inter-sentence space, otherwise <code>FALSE</code>.
 * @returns {textFragment}
 */
function textFragment(str, isSpace) {

  // constructor code
  this.text = str;
  this.isSentence = !isSpace;
  this.isSpace = isSpace;
  this.wordDelimiters = /<br>|<br \/>|<br\/>|\s/g;
  this.words = getWordsFromHtmlString(jQuery('<div>' + str.replace(/<br>|<br \/>|<br\/>/gi, '\n') + '</div>').text());

  this.wordCount = function() {
    return this.words.length;
  };
}

/**
 * Takes an HTML string and returns an array of fragments containing sentences and inter-sentence spaces
 * @param {String} textHTML The HTML text to split
 * @returns {Array} An array of <code>textFragment</code> objects
 */
function stringToSentences(textHTML) {

  // place holders
  var delimiter = String.fromCharCode(0);
  var lineBreak = String.fromCharCode(1); // html break tags count as white space
  var nonSentence = String.fromCharCode(2);

  // look for newlines and html break tags, replace them with the lineBreak place holder
  var regex = /(<br>|<br \/>|<br\/>|\r?\n)/g;
  var paragraph = textHTML.replace(regex, lineBreak);

  // look for sentence ending sequences and inter-sentence white space
  regex = XRegExp(
      '([\\p{SEP}]+   # sentence ending punctuation (SEP) \n\
      [\\p{STP}]*)    # characters that can follow the SEP \n\
      ([\\s\001]+)    # white space following all of the above',
      'xg'); // x = extended (allows white space and comments)

  paragraph = XRegExp.replace(paragraph, regex, '$1' + delimiter + nonSentence + '$2' + delimiter);

  // restore line breaks
  regex = /\001/g;
  paragraph = paragraph.replace(regex, '<br />');

  var fragments = paragraph.split(delimiter);
  var returnVal = new Array();

  for (var i = 0; i < fragments.length; i++) {

    var fragment = fragments[i];

    // check to avoid blank segments, especially at the end
    if (fragment.length > 0) {

      // is this space between sentences?
      if (fragment.substring(0, 1) === nonSentence)
        returnVal.push(new textFragment(fragment.substring(1), true));
      else
        returnVal.push(new textFragment(fragment, false));
    }
  }
  return returnVal;
}

/**
 *
 * @param {String} sentencesHTML
 * @param {Array} decodableGPCs
 * @returns {String}
 */
function addDecodableAnalysisMarkup(sentencesHTML, decodableGPCs) {

  // remove existing markup
  var html = removeAllMarkup(sentencesHTML);

  // split into words
  var aWords = getUniqueWordsFromHtmlString(html);

  alert(aWords);

  return _.filter(aWords, function(word) {
    return isNaN(word) === true;
  }).length;
}

/**
 * Removes all html tags and entities from the input string
 * @param {String} sentenceHTML
 * @returns {String}
 */
function removeAllMarkup(sentenceHTML) {

  // preserve spaces after line breaks
  var regex = /(<br>|<br \/>|<br\/>|\n)/g;
  sentenceHTML = sentenceHTML.replace(regex, ' ');

  // doc will be a valid html document, with sentenceHTML as the innerHTML of the body tag
  var parser = new DOMParser();
  var body = parser.parseFromString(sentenceHTML, "text/html").getElementsByTagName('body')[0];

  // textContent will return only the characters that would be visible to the user
  return body.textContent;
}
