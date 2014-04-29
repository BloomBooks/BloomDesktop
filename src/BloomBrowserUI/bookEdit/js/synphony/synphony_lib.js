/**
 * synphony_lib.js
 *
 * Functions
 *
 * Created Apr 8, 2014 by Hopper
 *
 */

var lang_data;
var alwaysMatch = [];

/**
 * Returns a list of words that meet the requested criteria.
 * @param {Array} aDesiredGPCs The list of graphemes targeted by this search
 * @param {Array} aKnownGPCs The list of graphemes known by the reader
 * @param {Boolean} restrictToKnownGPCs If <code>TRUE</code> then words will only contain graphemes in the <code>aKnownGPCs</code> list. If <code>FALSE</code> then words will contain at least one grapheme from the <code>aDesiredGPCs</code> list.
 * @param {Boolean} allowUpperCase
 * @param {int} syllableLength
 * @param {Array} aSelectedGroups
 * @param {Array} aPartsOfSpeech
 * @returns {Array} An array of WordObject objects
 */
function selectGPCWordsWithArrayCompare(aDesiredGPCs, aKnownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLength, aSelectedGroups, aPartsOfSpeech) {

  var word_already_exists, aSelectedWordObjects, aWordObjects, aVocabKey, aCriteria;
  var groups = chooseVocabGroups(aSelectedGroups);

  aWordObjects = [];
  aSelectedWordObjects = [];
  aVocabKey = constructSourceArrayNames(aDesiredGPCs, syllableLength); //result: "a__1" or "a_a__1" or "wicc_a_a__1"
  aCriteria = aKnownGPCs;

  //let's concatenate all vocabulary into 1 array
  for (var g = 0, len = groups.length; g < len; g++) { //eg: group1, group2...
    for (var i = 0, ilen = aVocabKey.length; i < ilen; i++) { //eg: ["a_a__1"]
      if (groups[g][aVocabKey[i]]) {//make sure it exists
        aWordObjects = aWordObjects.concat(groups[g][aVocabKey[i]]);
      }
    }
  }

  //this is the place to branch into checking for taught graphemes vs
  //selecting all words that have the current grapheme.
  if (!restrictToKnownGPCs) {
    //select all words that have the current_gpc
    aSelectedWordObjects = _.uniq(aWordObjects);
  } else {//we start restricting the word list using criteria that include
    //known graphemes, part of speech, etc.

    //add uppercase gpcs to aCriteria***
    if (allowUpperCase) {//if true then we add uppercase
      for (var k = 0, klen = aKnownGPCs.length; k < klen; k++) {
        var temp = [];
        for (var j = 0, jlen = lang_data.GPCS.length; j < jlen; j++) {
          if (lang_data.GPCS[j]["GPC"] === aKnownGPCs[k]) {
            if (lang_data.GPCS[j]["GPCuc"] !== "") {
              temp.push(lang_data.GPCS[j]["GPCuc"]);
            }
          }
        }
        aCriteria = aCriteria.concat(temp);
      }
    }

    //lets add symbols that can be matched at any time
    //these can come from the AlwaysMatch, SyllableBreak, StressSymbol, or MorphemeBreak fields
    //anything else must exist in the aKnownGPCs in order to be accepted
    if (alwaysMatch.length > 0) {
      aCriteria = aCriteria.concat(alwaysMatch);
    } else {
      if (lang_data['AlwaysMatch'] !== '') {
        alwaysMatch = alwaysMatch.concat(lang_data['AlwaysMatch']);
      }
      if (lang_data['SyllableBreak'] !== '') {
        alwaysMatch.push(lang_data['SyllableBreak']);
      }
      if (lang_data['StressSymbol'] !== '') {
        alwaysMatch.push(lang_data['StressSymbol']);
      }
      if (lang_data['MorphemeBreak'] !== '') {
        alwaysMatch.push(lang_data['MorphemeBreak']);
      }
      aCriteria = aCriteria.concat(alwaysMatch);
    }

    //start checking words
    for (var w = 0, wlen = aWordObjects.length; w < wlen; w++) {
      var keep = true;
      //first we check for allowable gpcs
      var gpcform = aWordObjects[w]["GPCForm"];
      var test_word = _.difference(gpcform, aCriteria);
      if (test_word.length > 0) {
        keep = false;
      }

      //then we check for part of speech constraint
      var ps_check = false;
      if (aPartsOfSpeech.length > 0) {
        if (aWordObjects[w]["PartOfSpeech"]) {
          for (var p = 0, plen = aPartsOfSpeech.length; p < plen; p++) {
            if (aWordObjects[w]["PartOfSpeech"] === aPartsOfSpeech[p]) {
              ps_check = true;
            }
          }
        }
        if (ps_check === false)
          keep = false;
      }

      //if keep is still true, then this word object
      //has passed all checks and is suitable for use
      if (keep === true) {
        word_already_exists = false;
        for (var m = 0, mlen = aSelectedWordObjects.length; m < mlen; m++) {
          //check to see that we don't add more than one instance of the word to our list
          if (aSelectedWordObjects[m] === aWordObjects[w]) {
            word_already_exists = true;
          }
        }
        if (word_already_exists === false) {
          aSelectedWordObjects.push(aWordObjects[w]);
        }
      }
    }//end of wordObject loop
  }

  return aSelectedWordObjects;
}

/**
 *
 * @param {Array} aDesiredGPCs An array of strings
 * @param {int} syllableLength
 * @returns {Boolean|Array} An array of names (string)
 */
function constructSourceArrayNames(aDesiredGPCs, syllableLength) {

  var aArrayNames, aName;
  aName = aDesiredGPCs;
  aArrayNames = [];

  if (syllableLength === 0) {
    throw new Error("Please select a syllable length checkbox.");
  }

  for (var i = 0, len = aName.length; i < len; i++) {
    for (var s = 0; s < syllableLength; s++) {
      aArrayNames.push(aName[i] + '__' + (s + 1));
    }
  }

  if (aArrayNames.length > 0) {
    return aArrayNames;
  } else {
    console.log('Error: function constructSourceArrayNames returned 0');
  }
}

/**
 * Gets a list of the checked values from a group of checkboxes.
 * @param {Array} elements An array of checkbox elements
 * @returns {Array} An array of the values of the checkboxes that are checked
 */
function collectCheckedValues(elements) {

  var aValues = [];

  for (var i = 0; i < elements.length; i++) {
    if (elements[i].checked === true) {
      aValues.push(elements[i].value);
    }
  }

  return aValues;
}

/**
 * Gets the words belonging to the requested groups
 * @param {Array} aSelectedGroups An array of strings
 * @returns {Array} An array of arrays containing WordObjects
 */
function chooseVocabGroups(aSelectedGroups) {

  var groups = [];

  for (var i = 0; i < aSelectedGroups.length; i++) {
    switch (aSelectedGroups[i]) {
      case 'group1':
        groups.push(lang_data.group1);
        break;
      case 'group2':
        groups.push(lang_data.group2);
        break;
      case 'group3':
        groups.push(lang_data.group3);
        break;
      case 'group4':
        groups.push(lang_data.group4);
        break;
      case 'group5':
        groups.push(lang_data.group5);
        break;
      case 'group6':
        groups.push(lang_data.group6);
        break;
      default:
        break;
    }
  }

  return groups;
}

/**
 * Called by langname_lang_data.js
 * @param {String} data
 */
function setLangData(data) {

  try {
    lang_data = data;
    processVocabularyGroups();
  }
  catch (e) {

    var div = document.getElementById('loading_data');
    if (div) {
      // this is running in the SynPhony UI, so show the error message
      alert('error');
      div.innerHTML = "Error loading language data: " + e.message;
    }
    else {
      // this is not running in the SynPhony UI, throw the exception
      throw e;
    }
  }
}

/**
 * Processes vocabulary and creates indexes to speed lookups.
 */
function processVocabularyGroups() {

  var n = lang_data["VocabularyGroups"];
  var u, gpc, syll;
  for (var a = 1; a < (n + 1); a++) {
    var group = "group" + a;
    for (var i = 0, len = lang_data[group].length; i < len; i++) {

      //creates a unique array of all gpcs in a word
      var temp = _.clone(lang_data[group][i]["GPCForm"]);
      u = _.uniq(temp);
      lang_data[group][i]["GPCS"] = u;
      lang_data[group][i]["GPCcount"] = u.length;

      //creates a reverse form of the word's gpcs
      lang_data[group][i]["Reverse"] = temp.reverse().join('');

      if (lang_data[group][i]["GPCS"] !== undefined) {
        //creates arrays grouped by gpc and syllable length
        for (var j = 0, jlen = u.length; j < jlen; j++) {
          gpc = u[j].toLowerCase();
          syll = lang_data[group][i]["Syllables"];
          if (!lang_data[group][gpc + '__' + syll]) {
            lang_data[group][gpc + '__' + syll] = [];
          }
          lang_data[group][gpc + '__' + syll].push(lang_data[group][i]);
        }
      }
    }
  }
}

/**
 * Used to convert csv gpc notation back to conventionally spelled text.
 *
 *   ex: handles full gpc: b_b,o_a,l_ll
 *   ex: handles simple gpc: b,a,ll
 *
 * @param {Array} aGPCs An array of GPCs
 * @returns {Array} An array of graphemes with the gpc notation removed
 */
function fullGPC2Regular(aGPCs) {

  var result = [];
  for (var i = 0; i < aGPCs.length; i++) {
    var temp = '';
    //if(/\,/.test(a[i])){
    //	alert('yes');
    //	a[i] = a[i].split(',');
    //}
    if (/_/.test(aGPCs[i]) === true) { //full gpc notation
      var c = aGPCs[i].split('_');
      //alert(a[i] + ' ' + c)
      if (/-/.test(c[1]) === true) { // for split digraphs (ae_a-e);
        var d = c[1].split('-');
        temp += d[0];
        aGPCs[i + 1] += d[1];//appends the part after the '-' to the next gpc
        //alert(a[i+1]);
      } else {
        temp += c[1];
      }
    } else {//simple gpc notation
      temp += aGPCs[i];
    }
    result.push(temp);
  }
  return result;
}

/**
 *
 * @param {Array} focus_words The words used in the story from the current stage
 * @param {Array} cumulative_words The words used in the story from the previous stages
 * @param {Array} possible_words Other words used in the story that may be decodable
 * @param {Array} sight_words Words used in the story that are given for clairity
 * @param {int} readableWordCount
 * @param {int} totalWordCount
 * @returns {checkStoryResults}
 */
function checkStoryResults(focus_words, cumulative_words, possible_words, sight_words, readableWordCount, totalWordCount) {

  // constructor code
  this.focus_words = focus_words;
  this.cumulative_words = cumulative_words;
  this.possible_words = possible_words;
  this.sight_words = sight_words;
  this.readableWordCount = readableWordCount;
  this.totalWordCount = totalWordCount;
}

/**
 * Returns all the words from <code>textHTML</code> without spaces or punctuation.
 * (Also converts to all lower case.)
 *
 * @param {String} textHTML
 * @returns {Array} An array of strings
 */
function getWordsFromHtmlString(textHTML) {

  // replace html break with space
  var regex = /<br>|<br \/>|<br\/>|\r?\n/g;
  var s = textHTML.replace(regex, ' ').toLowerCase();

  /**************************************************************************
   * Replace punctuation in a sentence with a space.
   *
   * Preserves punctuation marks within a word (ex. hyphen, or an apostrophe
   * in a contraction)
   **************************************************************************/
  regex = XRegExp(
      '(^\\p{P}+)                    # punctuation at the beginning of a string                      \n\
      |(\\p{P}+[\\s\\p{Z}]+\\p{P}+)  # punctuation within a sentence, between 2 words (word" "word)  \n\
      |([\\s\\p{Z}]+\\p{P}+)         # punctuation within a sentence, before a word                  \n\
      |(\\p{P}+[\\s\\p{Z}]+)         # punctuation within a sentence, after a word                   \n\
      |(\\p{P}+$)                    # punctuation at the end of a string',
      'xg');
  s = XRegExp.replace(s, regex, ' ');

  // split into words using space characters
  regex = XRegExp('[\\p{Z}]+', 'xg');
  return XRegExp.split(s.trim(), regex);
}

/**
 * Returns all the words from <code>textHTML</code> without spaces or
 * punctuation, with no duplicates.
 * (Also converts to all lower case.)
 *
 * @param {String} textHTML
 * @returns {Array} An array of strings
 */
function getUniqueWordsFromHtmlString(textHTML) {
  return _.uniq(getWordsFromHtmlString(textHTML));
}

/**
 *
 * @param {Array} aFocusWordList An array of all the predefined words (aka plainWordList)
 * @param {Array} aWordCumulativeList An array of the accumulated words (aka cumulativeWordList)
 * @param {Array} aGPCsKnown An array of all the predefined GPCs (aka knownGPCs)
 * @param {String} storyHTML  $('story_input').value
 * @param {String} sightWords $('sight_words').value
 * @returns {checkStoryResults} Statistics
 */
function checkStory(aFocusWordList, aWordCumulativeList, aGPCsKnown, storyHTML, sightWords) {

  // break the text into words
  var story_vocab = getWordsFromHtmlString(storyHTML);

  // get unique word list
  var story_vocab_compacted = _.uniq(story_vocab);

  // count total words in the story
  var total_words = _.filter(story_vocab, function(word) {
    return isNaN(word) === true;
  }).length;

  // if aGPCsKnown is empty, return now
  if (aGPCsKnown.length === 0)
    return new checkStoryResults([], [], [], [], 0, total_words);

  // first we do diffs on aFocusWordList and aWordCumulativeList with story_vocab words
  var story_focus_words = _.intersection(aFocusWordList, story_vocab_compacted);
  var story_cumulative_words = _.intersection(_.pluck(aWordCumulativeList, 'Name'), story_vocab);
  array_sort_length(story_focus_words);

  /* TODO: has to handle utf8 */

  // FIRST PASS: we handle words which are currently in focus
  var focus_words = _.intersection(story_focus_words, story_vocab_compacted);
  var remaining_words = _.difference(story_vocab_compacted, focus_words);
  array_sort_length(focus_words);

  // SECOND PASS: we handle words which are part of the cumulative word bank
  // aWordCumulativeList is an object that contains the following fields:
  // GPCForm,GPCS,GPCcount,Name,Reverse,SyllShape,Syllables
  var cumulative_words = _.intersection(story_cumulative_words, remaining_words);
  remaining_words = _.difference(remaining_words, cumulative_words);
  array_sort_length(cumulative_words);

  // THIRD PASS: we handle words which have not been matched yet to check if they are
  // decodable at this point. This can match words which are longer than the syllable
  // selectors specify but contain all the gpcs. We do this using a regular expression
  // with the array of knownGPCs. This is not the most accurate method; we should
  // first segment the word with all gpcs, then test with known gpcs. This also checks
  // for the possibility that the word is not yet in our database.
  // This only works for simple gpc notation, not complex.
  // Why not for full gpc? Once you have covered the regular spelling patterns (in English)
  // you will match all the other words, so everything gets tagged as 'possible'. Not useful!!
  var possible_words = [];
  if (lang_data["UseFullGPCNotation"] === false) {

    var letters = fullGPC2Regular(aGPCsKnown).join('|');

    // allow punctuation characters in the words
    var re = new XRegExp("^([" + letters + "]+[\\p{P}*[" + letters + "]*]*)$", "gi");
    possible_words = _.filter(remaining_words, function(word) {
      return word.match(re);
    });
    remaining_words = _.difference(remaining_words, possible_words);
    array_sort_length(possible_words);
  }

  // FOURTH PASS: we handle sight words
  var sight_words = [];
  if (sightWords.length > 0) {
    sight_words = _.intersection(sightWords.split(' '), remaining_words);
    remaining_words = _.difference(remaining_words, sight_words);
    array_sort_length(sight_words);
  }

  // FIFTH PASS: we handle everything else that's left over

  var readable = focus_words.length + cumulative_words.length + possible_words.length;
  return new checkStoryResults(focus_words, cumulative_words, possible_words, sight_words, readable, total_words);
}

/**
 *
 * @param {Array} focus_words The words used in the story from the current stage
 * @param {Array} cumulative_words The words used in the story from the previous stages
 * @param {Array} possible_words Other words used in the story that may be decodable
 * @param {Array} sight_words Words used in the story that are given for clairity
 * @param {String} storyHTML
 * @param {Boolean} replaceNewLine Replace \n with break tag
 * @returns {String} The replacement HTML
 */
function hilightStoryText(focus_words, cumulative_words, possible_words, sight_words, storyHTML, replaceNewLine) {

  // replace new lines with <br />
  if (replaceNewLine)
    storyHTML = storyHTML.replace(/\n/g, '<br \/>');

  // wrap focus words with <span class="fo">...<\/span>
  if (focus_words.length > 0)
    storyHTML = wrap_words(storyHTML, focus_words, 'fo');

  // wrap cumulative words with <span class="cu">...<\/span>
  if (cumulative_words.length > 0)
    storyHTML = wrap_words(storyHTML, cumulative_words, 'cu');

  // wrap possible words with <span class="po">...<\/span>
  if (possible_words.length > 0)
    storyHTML = wrap_words(storyHTML, possible_words, 'po');

  // wrap sight words with <span class="sight">...<\/span>
  if (sight_words.length > 0)
    storyHTML = wrap_words(storyHTML, sight_words, 'sight');

  return storyHTML;
}

/**
 * Sorts the array by the length of the string elements, descending
 * @param {Array} arr
 */
function array_sort_length(arr) {

  arr.sort(function(a, b) {
    return b.length - a.length; // ASC -> a - b; DESC -> b - a
  });
}

/**
 * Wraps words in <code>storyHTML</code> that are contained in <code>aWords</code>
 * @param {String} storyHTML
 * @param {Array} aWords
 * @param {String} cssClass
 * @returns {String}
 */
function wrap_words(storyHTML, aWords, cssClass) {

  var beforeWord = '(^|>|[\\s\\p{Z}]|\\p{P})';  // word beginning delimiter
  var afterWord = '(?=($|<|[\\s\\p{Z}]|\\p{P}+\\s|\\p{P}+$))';  // word ending delimiter

  var regex = new XRegExp(beforeWord + '(' + aWords.join('|') + ')' + afterWord, 'xgi');

  return XRegExp.replace(storyHTML, regex, '$1<span class="' + cssClass + '">$2<\/span>');
}
