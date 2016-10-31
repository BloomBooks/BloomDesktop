import XRegExp from 'xregexp';
import {_} from 'underscore';

/**
 * synphony_lib.js
 *
 * Functions
 *
 * Created Apr 8, 2014 by Hopper
 *
 */

/**
 * @type LanguageData
 */
// var singletonLanguageData = new LanguageData();
// export var theOneLanguageDataInstance = singletonLanguageData;;

export let theOneLanguageDataInstance = new LanguageData();

export function ResetLanguageDataInstance(){
    theOneLanguageDataInstance = new LanguageData();
}
var alwaysMatch = [];

/**
 * Class to hold language data
 * @param {Object} [optionalObject] Optional. If present, used to initialize the class. (We think not used.)
 * @returns {LanguageData}
 * 
 * Note: we export it because bloomSynphonyExtensions extends it
 */
export function LanguageData(optionalObject) {

    this.LangName = '';
    this.LangID = '';
    this.LanguageSortOrder = [];
    this.ProductivityGPCSequence = [];
    this.Numbers = [0,1,2,3,4,5,6,7,8,9];
    this.GPCS = [];
    this.VocabularyGroupsDescriptions = [];
    this.VocabularyGroups = 1;
    this.group1 = [];
    this.UseFullGPCNotation = false;

    if (typeof optionalObject !== "undefined")
        _.extend(this, optionalObject);
}


export function setLangData(data) {
    theOneLanguageDataInstance = new LanguageData(data);
    theOneLibSynphony.processVocabularyGroups();
}

/**
 * Class that holds Synphony-related functions
 * @returns {LibSynphony}
 */

export var LibSynphony = function() {}

/**
 * Returns a list of words that meet the requested criteria.
 * @param {Array} aDesiredGPCs The list of graphemes targeted by this search
 * @param {Array} aKnownGPCs The list of graphemes known by the reader
 * @param {Boolean} restrictToKnownGPCs If <code>TRUE</code> then words will only contain graphemes in the <code>aKnownGPCs</code> list. If <code>FALSE</code> then words will contain at least one grapheme from the <code>aDesiredGPCs</code> list.
 * @param {Boolean} allowUpperCase
 * @param {Array} aSyllableLengths
 * @param {Array} aSelectedGroups
 * @param {Array} aPartsOfSpeech
 * @returns {Array} An array of WordObject objects
 */
LibSynphony.prototype.selectGPCWordsWithArrayCompare = function(aDesiredGPCs, aKnownGPCs, restrictToKnownGPCs, allowUpperCase, aSyllableLengths, aSelectedGroups, aPartsOfSpeech) {

    var word_already_exists, aSelectedWordObjects, aWordObjects, aVocabKey, aCriteria;
    var groups = this.chooseVocabGroups(aSelectedGroups);

    aWordObjects = [];
    aSelectedWordObjects = [];
    aVocabKey = this.constructSourceArrayNames(aDesiredGPCs, aSyllableLengths); //result: "a__1" or "a_a__1" or "wicc_a_a__1"
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
                for (var j = 0, jlen = theOneLanguageDataInstance.GPCS.length; j < jlen; j++) {
                    if (theOneLanguageDataInstance.GPCS[j]["GPC"] === aKnownGPCs[k]) {
                        if (theOneLanguageDataInstance.GPCS[j]["GPCuc"] !== "") {
                            temp.push(theOneLanguageDataInstance.GPCS[j]["GPCuc"]);
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
            if ((typeof theOneLanguageDataInstance['AlwaysMatch'] !== 'undefined') && (theOneLanguageDataInstance['AlwaysMatch'] !== '')) {
                alwaysMatch = alwaysMatch.concat(theOneLanguageDataInstance['AlwaysMatch']);
            }
            if ((typeof theOneLanguageDataInstance['SyllableBreak'] !== 'undefined') && (theOneLanguageDataInstance['SyllableBreak'] !== '')) {
                alwaysMatch.push(theOneLanguageDataInstance['SyllableBreak']);
            }
            if ((typeof theOneLanguageDataInstance['StressSymbol'] !== 'undefined') && (theOneLanguageDataInstance['StressSymbol'] !== '')) {
                alwaysMatch.push(theOneLanguageDataInstance['StressSymbol']);
            }
            if ((typeof theOneLanguageDataInstance['MorphemeBreak'] !== 'undefined') && (theOneLanguageDataInstance['MorphemeBreak'] !== '')) {
                alwaysMatch.push(theOneLanguageDataInstance['MorphemeBreak']);
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
};

/**
 *
 * @param {Array} aDesiredGPCs An array of strings
 * @param {Array} aSyllableLengths An array of integers
 * @returns {Boolean|Array} An array of names (string)
 */
LibSynphony.prototype.constructSourceArrayNames = function(aDesiredGPCs, aSyllableLengths) {

    var aArrayNames, aName;
    aName = aDesiredGPCs;
    aArrayNames = [];

    if (aSyllableLengths.length === 0) {
        throw new Error("Please select a syllable length checkbox.");
    }

    for (var i = 0, len = aName.length; i < len; i++) {
        for (var s = 0; s < aSyllableLengths.length; s++) {
            aArrayNames.push(aName[i] + '__' + (aSyllableLengths[s]));
        }
    }

    if (aArrayNames.length > 0) {
        return aArrayNames;
    } else {
        console.log('Error: function constructSourceArrayNames returned 0');
    }
};

/**
 * Gets the words belonging to the requested groups
 * @param {Array} aSelectedGroups An array of strings
 * @returns {Array} An array of arrays containing WordObjects
 */
LibSynphony.prototype.chooseVocabGroups = function(aSelectedGroups) {

    var groups = [];

    for (var i = 0; i < aSelectedGroups.length; i++) {
        switch (aSelectedGroups[i]) {
            case 'group1':
                groups.push(theOneLanguageDataInstance.group1);
                break;
            case 'group2':
                groups.push(theOneLanguageDataInstance.group2);
                break;
            case 'group3':
                groups.push(theOneLanguageDataInstance.group3);
                break;
            case 'group4':
                groups.push(theOneLanguageDataInstance.group4);
                break;
            case 'group5':
                groups.push(theOneLanguageDataInstance.group5);
                break;
            case 'group6':
                groups.push(theOneLanguageDataInstance.group6);
                break;
            default:
                break;
        }
    }

    return groups;
};

/**
 * Processes vocabulary and creates indexes to speed lookups.
 * @param {LanguageData} [optionalLangData] Optional. If missing, the default theOneLanguageDataInstance is used.
 */
LibSynphony.prototype.processVocabularyGroups = function(optionalLangData) {

    var data = (typeof optionalLangData === "undefined") ? theOneLanguageDataInstance : optionalLangData;

    var n = data["VocabularyGroups"];
    for (var a = 1; a < (n + 1); a++) {
        var group = "group" + a;
        for (var i = 0, len = data[group].length; i < len; i++) {
            this.buildIndexDataForWord(data[group], i);
        }
    }
};


/**
 * Build our standard index data for a single word in a wordlist.
 * A wordlist is a very subtle trick object. It is fundamentally an array of words, each of which is an
 * object with a Name (the word), a GPCForm (the array of teachable letters that make up the word), and
 * "Syllables", the number of syllables in the word.
 * In addition, a wordlist is an object, with dynamic properties whose names are made up of a
 * teachable letter (GPC), two underlines, and a word length in syllables. Each such key has a list of words
 * which contain that GPC and have that many syllables.
 * This function updates our indexing information for the word at index i. It first adds some properties
 * to that word...a list of unique letters in it (GPCS), a count of those unique letters (GPCcount),
 * and a reversed letter list (Reverse).
 * Then it adds the word to the appropriate dynamic properties (creating them if necessary).
 * @param {} wordList
 * @param {} i
 * @returns {}
 */
LibSynphony.prototype.buildIndexDataForWord = function(wordList, i) {
    //creates a unique array of all gpcs in a word
    var temp = _.clone(wordList[i].GPCForm);
    var syll = wordList[i]["Syllables"];
    var u = _.uniq(temp);
    var gpc;
    wordList[i].GPCS = u;
    wordList[i].GPCcount = u.length;

    //creates a reverse form of the word's gpcs
    wordList[i].Reverse = temp.reverse().join('');

    if (wordList[i].GPCS !== undefined) {
        //creates arrays grouped by gpc and syllable length
        for (var j = 0, jlen = u.length; j < jlen; j++) {
            gpc = u[j].toLowerCase();
            if (!wordList[gpc + '__' + syll]) {
                wordList[gpc + '__' + syll] = [];
            }
            wordList[gpc + '__' + syll].push(wordList[i]);
        }
    }
};

/**
 * Used to convert csv gpc notation back to conventionally spelled text.
 *
 *   ex: handles full gpc: b_b,o_a,l_ll
 *   ex: handles simple gpc: b,a,ll
 *
 * @param {Array} aGPCs An array of GPCs
 * @returns {Array} An array of graphemes with the gpc notation removed
 */
LibSynphony.prototype.fullGPC2Regular = function(aGPCs) {

    var result = [];
    for (var i = 0; i < aGPCs.length; i++) {
        var temp = '';
        //if(/\,/.test(a[i])){
        //    alert('yes');
        //    a[i] = a[i].split(',');
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
};

/**
 * Returns all the words from <code>textHTML</code> without spaces or punctuation.
 * (Also converts to all lower case.)
 *
 * @param {String} textHTML
 * @param {String} [letters]
 * @returns {Array} An array of strings
 */
LibSynphony.prototype.getWordsFromHtmlString = function(textHTML, letters) {

    // replace html break with space
    var regex = /<br><\/br>|<br>|<br \/>|<br\/>|\r?\n/g;
    var s = textHTML.replace(regex, ' ').toLowerCase();

    var punct = "\\p{P}";

    if(letters) {
        // BL-1216 Use negative look-ahead to keep letters from being counted as punctuation
        // even if Unicode says something is a punctuation character when the user
        // has specified it as a letter (like single quote).
        punct = "(?![" + letters + "])" + punct;
    }
    /**************************************************************************
     * Replace punctuation in a sentence with a space.
     *
     * Preserves punctuation marks within a word (ex. hyphen, or an apostrophe
     * in a contraction)
     **************************************************************************/
    regex = XRegExp(
        '(^' + punct + '+)'                             // punctuation at the beginning of a string
        + '|(' + punct + '+[\\s\\p{Z}\\p{C}]+' + punct + '+)' // punctuation within a sentence, between 2 words (word" "word)
        + '|([\\s\\p{Z}\\p{C}]+' + punct + '+)'               // punctuation within a sentence, before a word
        + '|(' + punct + '+[\\s\\p{Z}\\p{C}]+)'               // punctuation within a sentence, after a word
        + '|(' + punct + '+$)',                         // punctuation at the end of a string
        'g');
    s = XRegExp.replace(s, regex, ' ');

    // split into words using Separator and Control characters
    // (ZERO WIDTH SPACE is a Control charactor.  See http://issues.bloomlibrary.org/youtrack/issue/BL-3933.)
    regex = XRegExp('[\\p{Z}\\p{C}]+', 'xg');
    return XRegExp.split(s.trim(), regex);
};

/**
 * Returns all the words from <code>textHTML</code> without spaces or
 * punctuation, with no duplicates.
 * (Also converts to all lower case.)
 *
 * @param {String} textHTML
 * @returns {Array} An array of strings
 */
LibSynphony.prototype.getUniqueWordsFromHtmlString = function(textHTML) {
    return _.uniq(this.getWordsFromHtmlString(textHTML));
};

/**
 *
 * @param {Array} aFocusWordList An array of all the predefined words (aka plainWordList)
 * @param {Array} aWordCumulativeList An array of the accumulated words (aka cumulativeWordList)
 * @param {Array} aGPCsKnown An array of all the predefined GPCs (aka knownGPCs)
 * @param {String} storyHTML  $('story_input').value
 * @param {String} sightWords $('sight_words').value
 * @returns {StoryCheckResults} Statistics
 */
LibSynphony.prototype.checkStory = function(aFocusWordList, aWordCumulativeList, aGPCsKnown, storyHTML, sightWords) {

    var letters;
    var story_vocab;

    if(aGPCsKnown.length > 0) {
        letters = this.fullGPC2Regular(aGPCsKnown).join('|');
        // break the text into words
        story_vocab = this.getWordsFromHtmlString(storyHTML, letters);
    }
    else {
        letters = '';
        // break the text into words
        story_vocab = this.getWordsFromHtmlString(storyHTML);
    }

    // get unique word list
    var story_vocab_compacted = _.uniq(story_vocab);

    // count total words in the story
    var total_words = _.filter(story_vocab, function(word) {
        return isNaN(word) === true;
    }).length;

    // if aGPCsKnown is empty, return now
    // BL-2359: Need to allow stages based on word lists rather than known graphemes
    //if (aGPCsKnown.length === 0)
    //    return new StoryCheckResults([], [], [], [], [], 0, total_words);

    // first we do diffs on aFocusWordList and aWordCumulativeList with story_vocab words
    var story_focus_words = _.intersection(aFocusWordList, story_vocab_compacted);
    var story_cumulative_words = _.intersection(_.pluck(aWordCumulativeList, 'Name'), story_vocab);
    this.array_sort_length(story_focus_words);

    /* TODO: has to handle utf8 */

    // FIRST PASS: we handle words which are currently in focus
    var focus_words = _.intersection(story_focus_words, story_vocab_compacted);
    var remaining_words = _.difference(story_vocab_compacted, focus_words);
    this.array_sort_length(focus_words);

    // SECOND PASS: we handle words which are part of the cumulative word bank
    // aWordCumulativeList is an object that contains the following fields:
    // GPCForm,GPCS,GPCcount,Name,Reverse,SyllShape,Syllables
    var cumulative_words = _.intersection(story_cumulative_words, remaining_words);
    remaining_words = _.difference(remaining_words, cumulative_words);
    this.array_sort_length(cumulative_words);

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
    if ((typeof theOneLanguageDataInstance === 'undefined') || (theOneLanguageDataInstance["UseFullGPCNotation"] === false)) {

        // allow punctuation characters in the words
        // BL-1216 Use negative look-ahead to keep letters from being counted as punctuation
        // even if Unicode says something is a punctuation character when the user
        // has specified it as a letter (like single quote).
        var re = new XRegExp("^((" + letters + ")+((?![" + letters + "])[\\p{P}]*(" + letters + ")*)*)$", "gi");
        possible_words = _.filter(remaining_words, function(word) {
            return word.match(re);
        });

        // BL-1217: exclude words with unknown graphemes, specifically 'aa' when only 'a' is known
        if (typeof theOneLanguageDataInstance !== 'undefined') {

            // get the unknown letters
            var unknownGPCs = _.difference(_.pluck(theOneLanguageDataInstance['GPCS'], 'GPC'), letters.split('|')); // .join('|');
            if (Array.isArray(unknownGPCs) && (unknownGPCs.length > 0)) {

                // remove from the list of unknownGPCs characters used to build multi-graphs in the list aGPCsKnown
                unknownGPCs = _.filter(unknownGPCs, function(gpc) {
                    return letters.indexOf(gpc) === -1;
                });

                re = new XRegExp("(" + unknownGPCs.join('|') + ")+", "gi");
                possible_words = _.filter(possible_words, function(word) {
                    return !word.match(re);
                });
            }
        }

        remaining_words = _.difference(remaining_words, possible_words);
        this.array_sort_length(possible_words);
    }

    // FOURTH PASS: we handle sight words
    // NOTE: Handle sight words after checking for decodability, because a sight word may become decodable.
    var sight_words = [];
    if (sightWords.length > 0) {
        sight_words = _.intersection(sightWords.split(' '), remaining_words);
        remaining_words = _.difference(remaining_words, sight_words);
        this.array_sort_length(sight_words);
    }

    // FIFTH PASS: we handle everything else that's left over

    var readable = focus_words.length + cumulative_words.length + possible_words.length;
    return new StoryCheckResults(focus_words, cumulative_words, possible_words, sight_words, remaining_words, readable, total_words);
};

/**
 * Sorts the array by the length of the string elements, descending
 * @param {Array} arr
 */
LibSynphony.prototype.array_sort_length = function(arr) {

    arr.sort(function(a, b) {
        return b.length - a.length; // ASC -> a - b; DESC -> b - a
    });
};

// function to escape special characters before performing a regular expression check
if (!RegExp.quote) {
    RegExp.quote = function(str) {
        return (str + '').replace(/([.?*+^$[\]\\(){}|-])/g, "\\$1");
    };
}

/**
 * Wraps words in <code>storyHTML</code> that are contained in <code>aWords</code>
 * @param {String} storyHTML
 * @param {Array} aWords
 * @param {String} cssClass
 * @param {String} extra
 * @returns {String}
 */
LibSynphony.prototype.wrap_words_extra = function(storyHTML, aWords, cssClass, extra) {

    if ((aWords === undefined) || (aWords.length === 0)) return storyHTML;

    if (storyHTML.trim().length === 0) return storyHTML;

    // make sure extra starts with a space
    if ((extra.length > 0) && (extra.substring(0, 1) !== ' '))
        extra = ' ' + extra;

    var beforeWord = '(^|>|[\\s\\p{Z}]|\\p{P}|&nbsp;)';  // word beginning delimiter
    var afterWord = '(?=($|<|[\\s\\p{Z}]|\\p{P}+\\s|\\p{P}+<br|[\\s]*&nbsp;|\\p{P}+&nbsp;|\\p{P}+$))';  // word ending delimiter

    // escape special characters
    var escapedWords = aWords.map(RegExp.quote);

    var regex = new XRegExp(beforeWord + '(' + escapedWords.join('|') + ')' + afterWord, 'xgi');

    // We must not replace any occurrences inside <...>. For example, if html is abc <span class='word'>x</span>
    // and we are trying to wrap 'word', we should not change anything.
    // To prevent this we split the string into sections starting at <. If this is valid html, each except the first
    // should have exactly one >. We strip off everything up to the > and do the wrapping within the rest.
    // Finally we put the pieces back together.
    var parts = storyHTML.split('<');
    var modParts = [];
    for (var i = 0; i < parts.length; i++) {
        var text = parts[i];
        var prefix = "";
        if (i != 0) {
            var index = text.indexOf('>');
            prefix = text.substring(0, index + 1);
            text = text.substring(index + 1, text.length);
        }
        modParts.push(prefix + XRegExp.replace(text, regex, '$1<span class="' + cssClass + '"' + extra + '>$2<\/span>'));
    }

    return modParts.join('<');
};

/**
 * Detects if the browser has the localStorage object.
 * @returns {Boolean}
 */
LibSynphony.prototype.supportsHTML5Storage = function() {

    try {
        return 'localStorage' in window && window['localStorage'] !== null;
    } catch (e) {
        return false;
    }
};

/**
 * Gets data previously stored locally in the browser.
 * @param {String} key
 * @returns {Array|Object}
 */
LibSynphony.prototype.dbGet = function(key) {

    if (this.supportsHTML5Storage()) {
        var item = localStorage.getItem(key);
        if (!item)
            return null;
        return JSON.parse(item);
    } else {
        alert('Local storage is not supported in your browser.');
    }
};

/**
 * Stores data locally in the browser.
 * @param {string} key
 * @param {Object} value
 */
LibSynphony.prototype.dbSet = function(key, value) {

    if (this.supportsHTML5Storage()) {
        var json = JSON.stringify(value);
        localStorage.setItem(key, json);
    } else {
        alert('Local storage is not supported in your browser.');
    }
};

/**
 * Construct a "StoryCheckResults" Class
 * 
 * @param {Array} focus_words The words used in the story from the current stage
 * @param {Array} cumulative_words The words used in the story from the previous stages
 * @param {Array} possible_words Other words used in the story that may be decodable
 * @param {Array} sight_words Words used in the story that are given for clairity
 * @param {Array} remaining_words
 * @param {int} readableWordCount
 * @param {int} totalWordCount
 * @returns {StoryCheckResults}
 */
export function StoryCheckResults(focus_words, cumulative_words, possible_words, sight_words, remaining_words, readableWordCount, totalWordCount) {

    // constructor code
    this.focus_words = focus_words;
    this.cumulative_words = cumulative_words;
    this.possible_words = possible_words;
    this.sight_words = sight_words;
    this.remaining_words = remaining_words;
    this.readableWordCount = readableWordCount;
    this.totalWordCount = totalWordCount;
}

/**
 * Searches the remaining_words for words that are numbers.
 * @returns {Array}
 */
StoryCheckResults.prototype.getNumbers = function() {

    var nums = [];
    var regex = new XRegExp('^[\\p{N}\\p{P}]+$', 'g');

    for (var i = 0; i < this.remaining_words.length; i++) {
        if (regex.test(this.remaining_words[i]))
            nums.push(this.remaining_words[i]);
    }

    return nums;
};

/**
 * Parses the langDataString into a theOneLanguageDataInstance object.
 * NOTE: Split into 2 functions, langDataFromString() and parseLangDataString(), for testing.
 * @param {String} langDataString
 * @returns {Boolean}
 */
LibSynphony.prototype.langDataFromString = function(langDataString) {

    theOneLanguageDataInstance = this.parseLangDataString(langDataString);

    theOneLibSynphony.processVocabularyGroups();

    return true;
};


/**
 * Parses the langDataString into a theOneLanguageDataInstance object
 * @param {String} langDataString
 * @returns {LanguageData}
 */
LibSynphony.prototype.parseLangDataString = function(langDataString) {

    // check for setLangData( ... )
    var pos = langDataString.indexOf('{');
    if (pos > 0)
        langDataString = langDataString.substring(pos);

    // should end with } (closing brace)
    pos = langDataString.lastIndexOf('}');
    if (pos < (langDataString.length - 1))
        langDataString = langDataString.substring(0, pos + 1);

    // fix errors and remove extra characters the JSON parser does not like
    langDataString = langDataString.replace('GPCS:', '"GPCS":');     // this name may not be inside double-quotes
    langDataString = langDataString.replace(/\/\/.*\r\n/g, '\r\n');  // remove comments from the file

    // load the data
    var langData = JSON.parse(langDataString);

    // add the functions from LanguageData
    return jQuery.extend(true, new LanguageData(), langData);
};

//TODO: change to something like "theOneLibSynhpony"
export var theOneLibSynphony = new LibSynphony()
