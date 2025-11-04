import XRegExp from "xregexp";
import _ from "underscore";
import jQuery from "jquery";
import {
    DataGPC,
    DataWord,
    setTheOneWordCache,
    TextFragment,
    theOneWordCache,
    WordCache,
} from "./bloomSynphonyExtensions";
import $ from "jquery";

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

export function ResetLanguageDataInstance() {
    theOneLanguageDataInstance = new LanguageData();
}
var alwaysMatch: any[] = [];

/**
 * Class to hold language data
 * @param {Object} [optionalObject] Optional. If present, used to initialize the class. (We think not used.)
 * @returns {LanguageData}
 *
 * Note: we export it because bloomSynphonyExtensions extends it
 */
export class LanguageData {
    public LangName = "";
    public LangID = "";
    public LanguageSortOrder: string[] = [];
    public ProductivityGPCSequence: string[] = [];
    public Numbers: number[] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
    public GPCS: DataGPC[] = [];
    public VocabularyGroupsDescriptions: string[] = [];
    public VocabularyGroups = 1;
    public group1: DataWord[] = [];
    public group2: DataWord[] = [];
    public group3: DataWord[] = [];
    public group4: DataWord[] = [];
    public group5: DataWord[] = [];
    public group6: DataWord[] = [];
    public UseFullGPCNotation = false;
    constructor(optionalObject?) {
        if (typeof optionalObject !== "undefined")
            _.extend(this, optionalObject);
    }
    /**
     * Adds a new DataGPC object to Vocabulary Group
     * @param grapheme Either a single grapheme (string) or an array of graphemes
     */
    public addGrapheme(grapheme) {
        if (!Array.isArray(grapheme)) grapheme = [grapheme];

        for (var i = 0; i < grapheme.length; i++) {
            var g = grapheme[i].toLowerCase();

            // check if the grapheme already exists
            var exists = _.any(this.GPCS, function (item) {
                return item.GPC === g;
            });

            if (!exists) this.GPCS.push(new DataGPC(g));
        }
    }

    /**
     * Adds a new DataWord object to Vocabulary Group
     * @param word Either a single word (string) or an array of words
     * @param {int} [freq] The number of times this word occurs
     */
    public addWord(word, freq?) {
        var sortedGraphemes = _.sortBy(
            _.pluck(this.GPCS, "Grapheme"),
            "length",
        ).reverse();

        // if this is a single word...
        if (!Array.isArray(word)) word = [word];

        for (var i = 0; i < word.length; i++) {
            var w = word[i].toLowerCase();

            // check if the word already exists
            var dw = this.findWord(w);
            if (!dw) {
                dw = new DataWord(w);
                dw.GPCForm = this.getGpcForm(w, sortedGraphemes);
                if (freq) dw.Count = freq;
                this.group1.push(dw);
            } else {
                if (freq) dw.Count += freq;
                else dw.Count += 1;
            }
        }
    }

    /**
     * Searches existing list for a word
     * @param {String} word
     * @returns {DataWord} Returns undefined if not found.
     */
    public findWord(word) {
        for (var i = 1; i <= this.VocabularyGroups; i++) {
            var found = _.find(this["group" + i], function (item) {
                return item.Name === word;
            });

            if (found) return found;
        }

        return null;
    }

    /**
     * Gets the GPCForm value of a word
     * @param {String} word
     * @param {Array} sortedGraphemes Array of all graphemes, sorted by descending length
     * @returns {Array}
     */
    public getGpcForm(word, sortedGraphemes) {
        var gpcForm: any[] = [];
        var hit = 1; // used to prevent infinite loop if word contains letters not in the list

        while (word.length > 0) {
            hit = 0;

            for (var i = 0; i < sortedGraphemes.length; i++) {
                var g = sortedGraphemes[i];

                if (word.substr(g.length * -1) === g) {
                    gpcForm.unshift(g);
                    word = word.substr(0, word.length - g.length);
                    hit = 1;
                    break;
                }
            }

            // If hit = 0, the last character still in the word is not in the list of graphemes.
            // We are adding it now  so the gpcForm returned will be as accurate as possible, and
            // will contain all the characters in the word.
            if (hit === 0) {
                var lastChar = word.substr(-1);
                var lastCharCode = lastChar.charCodeAt(0);
                // JS isn't very good at handling Unicode values beyond \xFFFF
                // We don't want to split up surrogate pairs which JS counts as two separate characters
                if (this.isPartOfSurrogatePair(lastCharCode)) {
                    gpcForm.unshift(word.substr(-2));
                    word = word.substr(0, word.length - 2);
                } else {
                    gpcForm.unshift(lastChar);
                    word = word.substr(0, word.length - 1);
                }
            }
        }

        return gpcForm;
    }

    public isPartOfSurrogatePair(charCode) {
        return 0xd800 <= charCode && charCode <= 0xdfff;
    }

    /**
     * Because of the way SynPhony attaches properties to the group array, the standard JSON.stringify() function
     * does not return a correct representation of the LanguageData object.
     * @param {String} langData
     * @returns {String}
     */
    public toJSON = function (langData) {
        // get the group arrays
        var n = langData["VocabularyGroups"];

        for (var a = 1; a < n + 1; a++) {
            var group = langData["group" + a];
            var index = "groupIndex" + a;
            langData[index] = {};

            // for each group, get the properties (contain '__')
            var keys = _.filter(_.keys(group), function (k) {
                return k.indexOf("__") > -1;
            });
            for (var i = 0; i < keys.length; i++) {
                langData[index][keys[i]] = group[keys[i]];
            }
        }

        return JSON.stringify(langData);
    };

    public fromJSON = function (jsonString) {
        var langData = JSON.parse(jsonString);

        // convert the groupIndex objects back to properties of the group array
        var n = langData["VocabularyGroups"];

        for (var a = 1; a < n + 1; a++) {
            var group = langData["group" + a];
            var index = "groupIndex" + a;
            var keys = _.keys(langData[index]);

            for (var i = 0; i < keys.length; i++) {
                group[keys[i]] = langData[index][keys[i]];
                langData[index][keys[i]] = null;
            }
        }

        return jQuery.extend(true, new LanguageData(), langData);
    };
}
export let theOneLanguageDataInstance = new LanguageData();

export function setLangData(data) {
    theOneLanguageDataInstance = new LanguageData(data);
    theOneLibSynphony.processVocabularyGroups();
}

/**
 * Class that holds Synphony-related functions
 * @returns {LibSynphony}
 */

export class LibSynphony {
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
    public selectGPCWordsWithArrayCompare(
        aDesiredGPCs,
        aKnownGPCs,
        restrictToKnownGPCs,
        allowUpperCase,
        aSyllableLengths,
        aSelectedGroups,
        aPartsOfSpeech,
    ) {
        var word_already_exists,
            aSelectedWordObjects,
            aWordObjects,
            aVocabKey,
            aCriteria;
        var groups = this.chooseVocabGroups(aSelectedGroups);

        aWordObjects = [];
        aSelectedWordObjects = [];
        aVocabKey = this.constructSourceArrayNames(
            aDesiredGPCs,
            aSyllableLengths,
        ); //result: "a__1" or "a_a__1" or "wicc_a_a__1"
        aCriteria = aKnownGPCs;

        //let's concatenate all vocabulary into 1 array
        for (var g = 0, len = groups.length; g < len; g++) {
            //eg: group1, group2...
            for (var i = 0, ilen = aVocabKey.length; i < ilen; i++) {
                //eg: ["a_a__1"]
                if (groups[g][aVocabKey[i]]) {
                    //make sure it exists
                    aWordObjects = aWordObjects.concat(groups[g][aVocabKey[i]]);
                }
            }
        }

        //this is the place to branch into checking for taught graphemes vs
        //selecting all words that have the current grapheme.
        if (!restrictToKnownGPCs) {
            //select all words that have the current_gpc
            aSelectedWordObjects = _.uniq(aWordObjects);
        } else {
            //we start restricting the word list using criteria that include
            //known graphemes, part of speech, etc.

            //add uppercase gpcs to aCriteria***
            if (allowUpperCase) {
                //if true then we add uppercase
                for (var k = 0, klen = aKnownGPCs.length; k < klen; k++) {
                    var temp: any[] = [];
                    for (
                        var j = 0,
                            jlen = theOneLanguageDataInstance.GPCS.length;
                        j < jlen;
                        j++
                    ) {
                        if (
                            theOneLanguageDataInstance.GPCS[j]["GPC"] ===
                            aKnownGPCs[k]
                        ) {
                            if (
                                theOneLanguageDataInstance.GPCS[j]["GPCuc"] !==
                                ""
                            ) {
                                temp.push(
                                    theOneLanguageDataInstance.GPCS[j]["GPCuc"],
                                );
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
                if (
                    typeof theOneLanguageDataInstance["AlwaysMatch"] !==
                        "undefined" &&
                    theOneLanguageDataInstance["AlwaysMatch"] !== ""
                ) {
                    alwaysMatch = alwaysMatch.concat(
                        theOneLanguageDataInstance["AlwaysMatch"],
                    );
                }
                if (
                    typeof theOneLanguageDataInstance["SyllableBreak"] !==
                        "undefined" &&
                    theOneLanguageDataInstance["SyllableBreak"] !== ""
                ) {
                    alwaysMatch.push(
                        theOneLanguageDataInstance["SyllableBreak"],
                    );
                }
                if (
                    typeof theOneLanguageDataInstance["StressSymbol"] !==
                        "undefined" &&
                    theOneLanguageDataInstance["StressSymbol"] !== ""
                ) {
                    alwaysMatch.push(
                        theOneLanguageDataInstance["StressSymbol"],
                    );
                }
                if (
                    typeof theOneLanguageDataInstance["MorphemeBreak"] !==
                        "undefined" &&
                    theOneLanguageDataInstance["MorphemeBreak"] !== ""
                ) {
                    alwaysMatch.push(
                        theOneLanguageDataInstance["MorphemeBreak"],
                    );
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
                        for (
                            var p = 0, plen = aPartsOfSpeech.length;
                            p < plen;
                            p++
                        ) {
                            if (
                                aWordObjects[w]["PartOfSpeech"] ===
                                aPartsOfSpeech[p]
                            ) {
                                ps_check = true;
                            }
                        }
                    }
                    if (ps_check === false) keep = false;
                }

                //if keep is still true, then this word object
                //has passed all checks and is suitable for use
                if (keep === true) {
                    word_already_exists = false;
                    for (
                        var m = 0, mlen = aSelectedWordObjects.length;
                        m < mlen;
                        m++
                    ) {
                        //check to see that we don't add more than one instance of the word to our list
                        if (aSelectedWordObjects[m] === aWordObjects[w]) {
                            word_already_exists = true;
                        }
                    }
                    if (word_already_exists === false) {
                        aSelectedWordObjects.push(aWordObjects[w]);
                    }
                }
            } //end of wordObject loop
        }

        return aSelectedWordObjects;
    }

    /**
     *
     * @param {Array} aDesiredGPCs An array of strings
     * @param {Array} aSyllableLengths An array of integers
     * @returns {Boolean|Array} An array of names (string)
     */
    public constructSourceArrayNames(aDesiredGPCs, aSyllableLengths) {
        var aArrayNames, aName;
        aName = aDesiredGPCs;
        aArrayNames = [];

        if (aSyllableLengths.length === 0) {
            throw new Error("Please select a syllable length checkbox.");
        }

        for (var i = 0, len = aName.length; i < len; i++) {
            for (var s = 0; s < aSyllableLengths.length; s++) {
                aArrayNames.push(aName[i] + "__" + aSyllableLengths[s]);
            }
        }

        if (aArrayNames.length > 0) {
            return aArrayNames;
        } else {
            console.log("Error: function constructSourceArrayNames returned 0");
        }
    }

    /**
     * Gets the words belonging to the requested groups
     * @param {Array} aSelectedGroups An array of strings
     * @returns {Array} An array of arrays containing WordObjects
     */
    public chooseVocabGroups(aSelectedGroups) {
        var groups: any[] = [];

        for (var i = 0; i < aSelectedGroups.length; i++) {
            switch (aSelectedGroups[i]) {
                case "group1":
                    groups.push(theOneLanguageDataInstance.group1);
                    break;
                case "group2":
                    groups.push(theOneLanguageDataInstance.group2);
                    break;
                case "group3":
                    groups.push(theOneLanguageDataInstance.group3);
                    break;
                case "group4":
                    groups.push(theOneLanguageDataInstance.group4);
                    break;
                case "group5":
                    groups.push(theOneLanguageDataInstance.group5);
                    break;
                case "group6":
                    groups.push(theOneLanguageDataInstance.group6);
                    break;
                default:
                    break;
            }
        }

        return groups;
    }

    /**
     * Processes vocabulary and creates indexes to speed lookups.
     * @param {LanguageData} [optionalLangData] Optional. If missing, the default theOneLanguageDataInstance is used.
     */
    public processVocabularyGroups(optionalLangData?) {
        var data =
            typeof optionalLangData === "undefined"
                ? theOneLanguageDataInstance
                : optionalLangData;

        var n = data["VocabularyGroups"];
        for (var a = 1; a < n + 1; a++) {
            var group = "group" + a;
            for (var i = 0, len = data[group].length; i < len; i++) {
                this.buildIndexDataForWord(data[group], i);
            }
        }
    }

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
    public buildIndexDataForWord(wordList, i) {
        //creates a unique array of all gpcs in a word
        var temp = _.clone(wordList[i].GPCForm);
        var syll = wordList[i]["Syllables"];
        var u = _.uniq(temp);
        var gpc;
        wordList[i].GPCS = u;
        wordList[i].GPCcount = u.length;

        //creates a reverse form of the word's gpcs
        wordList[i].Reverse = temp.reverse().join("");

        if (wordList[i].GPCS !== undefined) {
            //creates arrays grouped by gpc and syllable length
            for (var j = 0, jlen = u.length; j < jlen; j++) {
                gpc = u[j].toLowerCase();
                if (!wordList[gpc + "__" + syll]) {
                    wordList[gpc + "__" + syll] = [];
                }
                wordList[gpc + "__" + syll].push(wordList[i]);
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
    public fullGPC2Regular(aGPCs) {
        var result: any[] = [];
        for (var i = 0; i < aGPCs.length; i++) {
            var temp = "";
            //if(/\,/.test(a[i])){
            //    alert('yes');
            //    a[i] = a[i].split(',');
            //}
            if (/_/.test(aGPCs[i]) === true) {
                //full gpc notation
                var c = aGPCs[i].split("_");
                //alert(a[i] + ' ' + c)
                if (/-/.test(c[1]) === true) {
                    // for split digraphs (ae_a-e);
                    var d = c[1].split("-");
                    temp += d[0];
                    aGPCs[i + 1] += d[1]; //appends the part after the '-' to the next gpc
                    //alert(a[i+1]);
                } else {
                    temp += c[1];
                }
            } else {
                //simple gpc notation
                temp += aGPCs[i];
            }
            result.push(temp);
        }
        return result;
    }

    /**
     * Returns all the words from <code>textHTML</code> without spaces or punctuation.
     * (Also converts to all lower case.)
     *
     * @param {String} textHTML
     * @param {String} [lettersRange] set of letters prepared for use in a regex range by escaping special characters (\-])
     * @returns {Array} An array of strings
     */
    public getWordsFromHtmlString(textHTML, lettersRange?) {
        // replace html break with space
        let regex = /<br><\/br>|<br>|<br \/>|<br\/>|\r?\n/g;
        let s = textHTML.replace(regex, " ").toLowerCase();

        let punct = "\\p{P}";

        if (lettersRange) {
            // BL-1216 Use negative look-ahead to keep letters from being counted as punctuation
            // even if Unicode says something is a punctuation character when the user
            // has specified it as a letter (like single quote).
            punct = "(?![" + lettersRange + "])" + punct;
        }
        /**************************************************************************
         * Replace punctuation in a sentence with a space.
         *
         * Preserves punctuation marks within a word (ex. hyphen, or an apostrophe
         * in a contraction)
         **************************************************************************/
        regex = XRegExp(
            "(^" +
                punct +
                "+)" + // punctuation at the beginning of a string
                "|(" +
                punct +
                "+[\\s\\p{Z}\\p{C}]+" +
                punct +
                "+)" + // punctuation within a sentence, between 2 words (word" "word)
                "|([\\s\\p{Z}\\p{C}]+" +
                punct +
                "+)" + // punctuation within a sentence, before a word
                "|(" +
                punct +
                "+[\\s\\p{Z}\\p{C}]+)" + // punctuation within a sentence, after a word
                "|(" +
                punct +
                "+$)", // punctuation at the end of a string
            "g",
        );
        s = XRegExp.replace(s, regex, " ");
        s = s.trim();
        if (!s) return [];

        // Split into words using Separator and SOME Control characters
        // Originally the code had p{C} (all Control characters), but this was too all-encompassing.
        const whitespace = "\\p{Z}";
        const controlChars = "\\p{Cc}"; // "real" Control characters
        // The following constants are Control(format) [p{Cf}] characters that should split words.
        // e.g. ZERO WIDTH SPACE is a Control(format) charactor
        // (See http://issues.bloomlibrary.org/youtrack/issue/BL-3933),
        // but so are ZERO WIDTH JOINER and NON JOINER (See https://issues.bloomlibrary.org/youtrack/issue/BL-7081).
        // See list at: https://www.compart.com/en/unicode/category/Cf
        const zeroWidthSplitters = "\u200b"; // ZERO WIDTH SPACE
        const ltrrtl = "\u200e\u200f"; // LEFT-TO-RIGHT MARK / RIGHT-TO-LEFT MARK
        const directional = "\u202A-\u202E"; // more LTR/RTL/directional markers
        const isolates = "\u2066-\u2069"; // directional "isolate" markers
        // split on whitespace, Control(control) and some Control(format) characters
        regex = XRegExp(
            "[" +
                whitespace +
                controlChars +
                zeroWidthSplitters +
                ltrrtl +
                directional +
                isolates +
                "]+",
            "xg",
        );
        return XRegExp.split(s, regex);
    }

    /**
     * Returns all the words from <code>textHTML</code> without spaces or
     * punctuation, with no duplicates.
     * (Also converts to all lower case.)
     *
     * @param {String} textHTML
     * @returns {Array} An array of strings
     */
    public getUniqueWordsFromHtmlString(textHTML) {
        return _.uniq(this.getWordsFromHtmlString(textHTML));
    }

    /**
     * Prepare a "letter" (possibly multigraph) for inclusion in a regular expression
     * as part of an alternative expression such as (a|b|c)
     * Some users may want ? for glottal or similar nonsense with other special RE characters.
     * See https://issues.bloomlibrary.org/youtrack/issue/BL-7075 and
     * https://issues.bloomlibrary.org/youtrack/issue/BL-10446)
     *
     * @param {String} letter One or more characters treated as a decodable unit
     * @returns {String} input string possibly with \ quoted characters
     */
    public protectRegExpLetters(letter) {
        return letter
            .replace(/\\/g, "\\\\")
            .replace(/\?/g, "\\?")
            .replace(/\+/g, "\\+")
            .replace(/\*/g, "\\*")
            .replace(/\[/g, "\\[")
            .replace(/\]/g, "\\]")
            .replace(/\(/g, "\\(")
            .replace(/\)/g, "\\)")
            .replace(/\|/g, "\\|");
    }

    /**
     *
     * @param {Array} aFocusWordList An array of all the predefined words (aka plainWordList)
     * @param {Array} aWordCumulativeList An array of the accumulated words (aka cumulativeWordList)
     * @param {Array} aGPCsKnown An array of all the predefined GPCs (aka knownGPCs)
     * @param {String} storyHTML  $('story_input').value
     * @param {String} sightWords $('sight_words').value
     * @returns {StoryCheckResults} Statistics
     */
    public checkStory(
        aFocusWordList,
        aWordCumulativeList,
        aGPCsKnown,
        storyHTML,
        sightWords,
    ) {
        var letters;
        var lettersRange;
        var story_vocab;

        if (aGPCsKnown.length > 0) {
            letters = this.fullGPC2Regular(aGPCsKnown)
                .map((val) => this.protectRegExpLetters(val))
                .join("|");
            // When placed in a regex range, the letters don't need to be separated by |,
            // but \ ] and - do need to be quoted as they have special meaning in that context.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-10324.
            lettersRange = this.fullGPC2Regular(aGPCsKnown)
                .join("")
                .replace(/\\/g, "\\\\")
                .replace(/]/g, "\\]")
                .replace(/-/g, "\\-");
            // break the text into words
            story_vocab = this.getWordsFromHtmlString(storyHTML, lettersRange);
        } else {
            letters = "";
            lettersRange = "";
            // break the text into words
            story_vocab = this.getWordsFromHtmlString(storyHTML);
        }

        // get unique word list
        var story_vocab_compacted = _.uniq(story_vocab);

        // count total words in the story
        var total_words = _.filter(story_vocab, function (word) {
            return isNaN(word) === true;
        }).length;

        // if aGPCsKnown is empty, return now
        // BL-2359: Need to allow stages based on word lists rather than known graphemes
        //if (aGPCsKnown.length === 0)
        //    return new StoryCheckResults([], [], [], [], [], 0, total_words);

        // first we do diffs on aFocusWordList and aWordCumulativeList with story_vocab words
        var story_focus_words = _.intersection(
            aFocusWordList,
            story_vocab_compacted,
        );
        var story_cumulative_words = _.intersection(
            _.pluck(aWordCumulativeList, "Name"),
            story_vocab,
        );
        this.array_sort_length(story_focus_words);

        /* TODO: has to handle utf8 */

        // FIRST PASS: we handle words which are currently in focus
        var focus_words = _.intersection(
            story_focus_words,
            story_vocab_compacted,
        );
        var remaining_words = _.difference(story_vocab_compacted, focus_words);
        this.array_sort_length(focus_words);

        // SECOND PASS: we handle words which are part of the cumulative word bank
        // aWordCumulativeList is an object that contains the following fields:
        // GPCForm,GPCS,GPCcount,Name,Reverse,SyllShape,Syllables
        var cumulative_words = _.intersection(
            story_cumulative_words,
            remaining_words,
        );
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
        var possible_words: any[] = [];
        if (
            typeof theOneLanguageDataInstance === "undefined" ||
            theOneLanguageDataInstance["UseFullGPCNotation"] === false
        ) {
            // allow punctuation characters in the words
            // BL-1216 Use negative look-ahead to keep letters from being counted as punctuation
            // even if Unicode says something is a punctuation character when the user
            // has specified it as a letter (like single quote).
            var re = XRegExp(
                "^((" +
                    letters +
                    ")+((?![" +
                    lettersRange +
                    "])[\\p{P}]*(" +
                    letters +
                    ")*)*)$",
                "gi",
            );
            possible_words = _.filter(remaining_words, function (word) {
                return word.match(re);
            });

            // BL-1217: exclude words with unknown graphemes, specifically 'aa' when only 'a' is known
            if (typeof theOneLanguageDataInstance !== "undefined") {
                // get the unknown letters
                var unknownGPCs = _.difference(
                    _.pluck(theOneLanguageDataInstance["GPCS"], "GPC"),
                    letters.split("|"),
                ); // .join('|');
                if (Array.isArray(unknownGPCs) && unknownGPCs.length > 0) {
                    // remove from the list of unknownGPCs characters used to build multi-graphs in the list aGPCsKnown
                    unknownGPCs = _.filter(unknownGPCs, function (gpc) {
                        return letters.indexOf(gpc) === -1;
                    });
                    if (unknownGPCs.length > 0) {
                        let unknownChars = unknownGPCs
                            .map((val) => this.protectRegExpLetters(val))
                            .join("|");
                        re = XRegExp("(" + unknownChars + ")+", "gi");
                        possible_words = _.filter(
                            possible_words,
                            function (word) {
                                return !word.match(re);
                            },
                        );
                    }
                }
            }

            remaining_words = _.difference(remaining_words, possible_words);
            this.array_sort_length(possible_words);
        }

        // FOURTH PASS: we handle sight words
        // NOTE: Handle sight words after checking for decodability, because a sight word may become decodable.
        var sight_words: any[] = [];
        if (sightWords.length > 0) {
            sight_words = _.intersection(
                sightWords.split(" "),
                remaining_words,
            );
            remaining_words = _.difference(remaining_words, sight_words);
            this.array_sort_length(sight_words);
        }

        // FIFTH PASS: we handle everything else that's left over

        var readable =
            focus_words.length +
            cumulative_words.length +
            possible_words.length;
        return new StoryCheckResults(
            focus_words,
            cumulative_words,
            possible_words,
            sight_words,
            remaining_words,
            readable,
            total_words,
        );
    }

    /**
     * Sorts the array by the length of the string elements, descending
     * @param {Array} arr
     */
    public array_sort_length(arr) {
        arr.sort(function (a, b) {
            return b.length - a.length; // ASC -> a - b; DESC -> b - a
        });
    }

    public static extraSentencePunct = "";
    public setExtraSentencePunctuation(extra) {
        // Replace characters that are magic in regexp. As a special case, period is simply removed, since it's already
        // sentence-terminating. If they want to use backslash, they will just have to double it; we can't fix it
        // because we want to allow \u0020 etc. I don't know why <> need to be replaced but they don't work otherwise.
        var extraRe = extra
            .replace(/\\U/g, "\\u") // replace all
            .replace("^", "\\u005E")
            .replace("$", "\\u0024")
            .replace(".", "")
            .replace("*", "\\u002A")
            .replace("~", "\\u007E")
            .replace("[", "\\u005B")
            .replace("]", "\\u005D")
            .replace("<", "\\u003C")
            .replace(">", "\\u003E");
        LibSynphony.extraSentencePunct = extraRe;
        // Although the method name says "add", it actually resets the meaning of "SEP" in regular expressions to whatever we give it here.
        // The literal string is taken from bloom-xregexp_categories.js, copied because I can't figure out how a literal string there
        // can be accessed here.
        XRegExp.addUnicodeData([
            {
                name: "SEP",
                alias: "Sentence_Ending_Punctuation",
                bmp:
                    extraRe +
                    "\u17D4" + // requested for Khmer (BL-12757)
                    "\u0021\u002e\u003f\u055c\u055e\u0589\u061f\u06d4\u0700\u0701\u0702\u0964\u0965\u104b\u1362\u1367\u1368\u166e\u1803\u1809\u1944\u1945\u203c\u203d\u2047\u2048\u2049\u3002\ufe52\ufe56\ufe57\uff01\uff0e\uff1f\uff61\u00a7",
            },
        ]);
    }

    /**
     * Takes an HTML string and returns an array of fragments containing sentences and inter-sentence spaces
     * @param {String} textHTML The HTML text to split
     * @returns {Array} An array of <code>TextFragment</code> objects
     */
    public stringToSentences(textHTML) {
        // place holders
        const delimiter = String.fromCharCode(0);
        // Lets us treat various forms of <br> as a single whitespace character in regexps
        const htmlLineBreakReplacement = String.fromCharCode(1);
        // Lets us treat CRLF as single whitespace character in regexps.
        const windowsLineBreakReplacement = String.fromCharCode(2);
        // Inserted at the start of the text of non-sentence fragments to mark them as such until we actually make a Fragment object.
        const nonSentenceMarker = String.fromCharCode(3);
        // Lets us treat an opening tag as  single character in regexps. A matching list of the actual tags in openTags is used to restore them.
        const openingTagReplacement = String.fromCharCode(4); // u0004 is a replacement character for all other opening html tags
        // Lets us treat a closing tag as  single character in regexps. A matching list of the actual tags in closeTags is used to restore them.
        const closingTagReplacement = String.fromCharCode(5); // u0005 is a replacement character for all other closing html tags
        // Lets us treat a self-closing tag as a single character in regexps. A matching list of the actual tags in selfTags is used to restore them.
        const selfClosingTagReplacement = String.fromCharCode(6); // u0006 is a replacement character for all other self-closing html tags
        // This replaces an empty HTML element, which by the time we use it is openingTagReplacement immediately followed by closingTagReplacement.
        // We convert it back to that sequence before restoring them.
        const emptyTagReplacement = String.fromCharCode(7);
        // Lets us use a single character in regexps in place of "&nbsp;"
        const nbspReplacement = String.fromCharCode(8);
        if (textHTML === null) textHTML = "";
        // look for html break tags, replace them with the htmlLineBreak place holder
        let regex = /(<br><\/br>|<br>|<br \/>|<br\/>)/g;
        textHTML = textHTML.replace(regex, htmlLineBreakReplacement);

        // look for Windows line breaks, replace them with the windowsLineBreak place holder
        regex = /(\r\n)/g;
        textHTML = textHTML.replace(regex, windowsLineBreakReplacement);

        // collect opening html tags and replace with tagHolderOpen place holder
        // Remember, tag names can be as short as a single character!  See BL-10119.
        // We want to match tags like <strong> or <a href="https://sil.org/"> but not
        // <br /> or <img src="image.png"/>.  Self-closing tags are matched later.
        const regexOpenTag = /<[a-zA-Z]+([^<>]*[^\/<>])?>/g;
        const openTags = textHTML.match(regexOpenTag);
        textHTML = textHTML.replace(regexOpenTag, openingTagReplacement);

        // collect closing html tags and replace with tagHolderClose place holder
        // We want to match tags like </span> or </u>.
        const regexCloseTag = /<[\/][a-zA-Z]+>/g;
        const closeTags = textHTML.match(regexCloseTag);
        textHTML = textHTML.replace(regexCloseTag, closingTagReplacement);

        // collect self-closing html tags and replace with tagHolderSelf place holder
        // We want to match tags like <br/> or <img src="picture.jpg" />.
        const regexSelfTag = /<[a-zA-Z]+[^<>]*[\/]>/g;
        const selfTags = textHTML.match(regexSelfTag);
        textHTML = textHTML.replace(regexSelfTag, selfClosingTagReplacement);

        // collect empty html tags and replace with tagHolderEmpty place holder
        const regexEmptyTag = new RegExp(
            openingTagReplacement + closingTagReplacement,
            "g",
        );
        const emptyTags = textHTML.match(regexEmptyTag);
        textHTML = textHTML.replace(regexEmptyTag, emptyTagReplacement);

        // replace &nbsp; with nbsp place holder
        textHTML = textHTML.replace(/&nbsp;/g, nbspReplacement);

        // look for paragraph ending sequences
        regex = XRegExp(
            "[^\\p{PEP}]*[\\p{PEP}]+" + "|[^\\p{PEP}]+$", // break on all paragraph ending punctuation (PEP)
            "g",
        );

        // break the text into paragraphs
        var paragraphs = XRegExp.match(textHTML, regex);

        const zwsp = "\u200B"; // zero-width-space"

        // We require at least one space between sentences, unless things have been configured so that
        // space IS a sentence-ending punctuation. In that case, zero or more.
        // zero-width space does not COUNT as a space (if it's the only thing between period and next upper-case letter),
        // but they may be interspersed.
        // Review: Do selfClosingTagReplacement and emptyTagReplacement belong here or in
        // sentenceSpacePadChars? We have specific unit tests that fail if they are put there:
        // one asserting that an image counts as white space between sentences, and one that
        // an empty span counts. The former strikes me as unlikely (an image is probably not
        // white space) and the latter very strange: an empty span typically isn't visible at
        // all. I'm leaving it this way since we're close to shipping and there may be a good
        // reason I don't know, and a change is not needed to fix the problem.
        const sentenceSpaceChars =
            "\\s\\p{PEP}" +
            selfClosingTagReplacement +
            emptyTagReplacement +
            nbspReplacement;

        // Characters that may be considered part of the white space between sentences, but
        // do not themselves constitute a gap that defines a sentence.
        const sentenceSpacePadChars = zwsp;

        // any number (including zero) of characters we consider white, including the ones that
        // may be included in whitespace but don't COUNT as sentence-breaking white space
        const whiteSpacePattern =
            "[" + sentenceSpaceChars + sentenceSpacePadChars + "]*";

        const intersentenceSpace =
            "(" +
            whiteSpacePattern +
            (LibSynphony.extraSentencePunct &&
            LibSynphony.extraSentencePunct.indexOf("\\u0020") >= 0
                ? "" // nothing more needed
                : // One definite (not zwsp etc.) white character, possibly followed by arbitrarily more space
                  "[" + sentenceSpaceChars + "]" + whiteSpacePattern) +
            ")";
        // Note that categories Pf and Pi can both act as either Ps or Pe
        // (See https://issues.bloomlibrary.org/youtrack/issue/BL-5063.)
        // characters that can follow the SEP: single or double quotes,
        // Pe: close punctuation (closing brackets),
        // Pf: final punctuation (closing quotes)
        // Pi: Initial punctuation (opening quotes)
        var trailingPunct =
            "['\"\\p{Pe}\\p{Pf}\\p{Pi}" + closingTagReplacement + "]";
        // What can follow the separator and be considered part of the preceding sentence.
        // These kinds of spaces are allowed, but only before a trailingPunct; otherwise,
        // they are considered part of the inter-sentence white space.
        // \\u202F: narrow non-breaking space that our long-press inserts for non-breaking space.
        // Using a non-capturing group here, because it's only to allow the space+trailing
        // sequence to repeat; we don't want to use it separately in the result.
        var afterSEP =
            "(?:[" + nbspReplacement + "\\u202F]*" + trailingPunct + ")*";

        // regex to find sentence ending sequences and inter-sentence space
        // \p{SEP} is defined as a list of sentence ending punctuation characters by a call to XRegExp.addUnicodeData
        // in LibSynphony.prototype.setExtraSentencePunctuation (or perhaps elsewhere in tests?)
        regex = XRegExp(
            "([\\p{SEP}]+" + // sentence ending punctuation (SEP)
                afterSEP + // what can follow SEP in same sentence,
                ")" + // then end group for the sentence
                "([" +
                openingTagReplacement +
                "]*)" +
                intersentenceSpace +
                "([" +
                closingTagReplacement +
                "]*)" +
                "(?![^\\p{L}]*" + // may be followed by non-letter chars
                "[\\p{Ll}\\p{SCP}]+)", // first letter following is not lower case. (This works by consuming all the lowercase letters/etc. up until the first uppercase letter/etc)
            "g",
        );

        var returnVal = [];
        for (var i = 0; i < (paragraphs?.length ?? 0); i++) {
            // mark boundaries between sentences and inter-sentence space
            var paragraph = XRegExp.replace(
                paragraphs![i],
                regex,
                "$1" +
                    delimiter +
                    nonSentenceMarker +
                    "$2" +
                    "$3" +
                    "$4" +
                    delimiter,
            );

            // Phrase Delimiting
            // It's possible to use a fancy regex-based approach too (akin to sentence splitting regex)
            // But that's not so intuitive nor easy to obtain/verify correctness
            // due to many corner cases... need to consider sentence continuing punctuation, etc
            // Instead, let's start with a simple, easy to understand approach:
            //    "|" character is a phrase delimiter. Context doesn't matter.
            //    (But collapse empty entries created from having multiple "|" character in a row)
            // It should be easier for us to implement, test, and communicate, and easier for the user to understand.
            paragraph = XRegExp.replace(paragraph, /(\|+)/g, "$1" + delimiter);

            // restore line breaks
            paragraph = paragraph.replace(
                new RegExp(htmlLineBreakReplacement, "g"),
                "<br />",
            );
            paragraph = paragraph.replace(
                new RegExp(windowsLineBreakReplacement, "g"),
                "\r\n",
            );
            // Something seems to be wrong around here for very complex markup involving empty elements and unclosed tags,
            // that can result in fragments with excess closing tags. One example is page 26 of the book in BL-15111.
            // The audio code is now removing empty elements, so it's not a problem; but it may bite us again.

            var unclosedTags: any[] = [];
            // split the paragraph into sentences and
            var fragments = paragraph.split(delimiter);
            var prevFragmentWithoutMarkup = "";
            for (var j = 0; j < fragments.length; j++) {
                var fragment = fragments[j];

                const fragmentWithoutMarkup = fragment.replace(
                    new RegExp(
                        "[" +
                            openingTagReplacement +
                            closingTagReplacement +
                            selfClosingTagReplacement +
                            emptyTagReplacement +
                            "]",
                        "g",
                    ),
                    "",
                );

                // if some tags from earlier segments are still open, reopen at start
                // For example, if earlier segments opened <span...> and then <em> and closed neither,
                // we want to re-open them in that order. They remain in the unclosedTags list.
                // That is, we will insert <span...><em> at the start of the fragment.
                // This should come after the special character that identifies white-space
                // segments, if it is present.
                if (fragment.startsWith(nonSentenceMarker)) {
                    fragment =
                        nonSentenceMarker +
                        unclosedTags.join("") +
                        fragment.substring(1);
                } else {
                    fragment = unclosedTags.join("") + fragment;
                }

                // put the empty html tags back in
                while (fragment.indexOf(emptyTagReplacement) > -1)
                    fragment = fragment.replace(
                        new RegExp(emptyTagReplacement),
                        emptyTags.shift(),
                    );

                // put the opening html tags back in
                while (fragment.indexOf(openingTagReplacement) > -1) {
                    const tag = openTags.shift();
                    fragment = fragment.replace(
                        new RegExp(openingTagReplacement),
                        tag,
                    );
                    unclosedTags.push(tag);
                }

                // put the closing html tags back in
                // (unless this is an empty segment...then just leave them out; already closed at end of previous segment)
                while (fragment.indexOf(closingTagReplacement) > -1) {
                    const closeTag = closeTags.shift();
                    fragment = fragment.replace(
                        new RegExp(closingTagReplacement),
                        closeTag,
                    );
                    unclosedTags.pop();
                }

                // If some tags are still unclosed, close them. (Will reopen next fragment).
                // For example, if <span...> was opened, and later <em>, and neither has been
                // closed, unclosedTags contains <span...>, <em>.
                // We want to append </em></span>

                for (let i = unclosedTags.length - 1; i >= 0; i--) {
                    const tagToClose = unclosedTags[i];
                    // convert from opening tag to corresponding closing tag.
                    // (Drop the opening wedge with substring, prepend '</'.
                    // Then if there is anything after the tag, remove it.
                    // If the regex doesn't match, that makes it something like <em>,
                    // and there's nothing we need to remove; replace just does nothing.)
                    const closingTag =
                        "</" + tagToClose.substring(1).replace(/ .*>/, ">");
                    fragment = fragment + closingTag;
                }

                // put the self-closing html tags back in
                while (fragment.indexOf(selfClosingTagReplacement) > -1)
                    fragment = fragment.replace(
                        new RegExp(selfClosingTagReplacement),
                        selfTags.shift(),
                    );

                // put nbsp back in
                fragment = fragment.replace(
                    new RegExp(nbspReplacement, "g"),
                    "&nbsp;",
                );

                // check to avoid empty segments at the end
                if (
                    j < fragments.length - 1 ||
                    fragmentWithoutMarkup.length > 0
                ) {
                    // is this space between sentences?
                    if (fragment.substring(0, 1) === nonSentenceMarker) {
                        returnVal.push(
                            new TextFragment(fragment.substring(1), true),
                        );
                    } else if (
                        j > 0 &&
                        prevFragmentWithoutMarkup.endsWith("|") &&
                        fragmentWithoutMarkup.match(/^\s/) // current fragment starts with whitespace
                    ) {
                        // Check for a phrase marker at the end of the previous fragment followed by
                        // whitespace at the beginning of this fragment to maintain "interphrase" spacing.
                        // Users can place the phrase marker (|) before or after inter-word spacing (or
                        // even in the middle of a word I suppose).  The regex for splitting on a phrase
                        // marker is intentionally kept simple, but we need to make up for its simplicity
                        // here with this special check and fix.
                        // See https://issues.bloomlibrary.org/youtrack/issue/BL-10569.
                        const leadingSpaceCount = fragment.search(/\S|$/); // get location of first non-whitespace character or end of string
                        // Add the leading whitespace as a space TextFragment
                        returnVal.push(
                            new TextFragment(
                                fragment.substring(0, leadingSpaceCount),
                                true,
                            ),
                        );
                        // If there's anything left over, add the rest of the fragment as a regular sentence TextFragment
                        if (leadingSpaceCount < fragment.length)
                            returnVal.push(
                                new TextFragment(
                                    fragment.substring(leadingSpaceCount),
                                    false,
                                ),
                            );
                    } else {
                        returnVal.push(new TextFragment(fragment, false));
                    }
                }
                prevFragmentWithoutMarkup = fragmentWithoutMarkup;
            }
        }

        return returnVal;
    }

    /**
     * Reads the file passed in the fileInputElement and calls the callback function when finished
     * @param {Element} fileInputElement
     * @param {Function} callback Function with one parameter, which will be TRUE if successful.
     */
    publicloadLanguageData(fileInputElement, callback) {
        var file = fileInputElement.files[0];

        if (!file) return;

        var reader = new FileReader();
        reader.onload = function (e) {
            callback(theOneLibSynphony.langDataFromString(e.target?.result));
        };
        reader.readAsText(file);
    }

    /**
     * Returns just the Name property (the actual word) of the selected DataWord objects.
     * @param {Array} aDesiredGPCs The list of graphemes targeted by this search
     * @param {Array} aKnownGPCs The list of graphemes known by the reader
     * @param {Boolean} restrictToKnownGPCs If <code>TRUE</code> then words will only contain graphemes in the <code>aKnownGPCs</code> list. If <code>FALSE</code> then words will contain at least one grapheme from the <code>aDesiredGPCs</code> list.
     * @param {Boolean} allowUpperCase
     * @param {Array} aSyllableLengths
     * @param {Array} aSelectedGroups
     * @param {Array} aPartsOfSpeech
     * @returns {Array} An array of strings
     */
    public selectGPCWordNamesWithArrayCompare(
        aDesiredGPCs,
        aKnownGPCs,
        restrictToKnownGPCs,
        allowUpperCase,
        aSyllableLengths,
        aSelectedGroups,
        aPartsOfSpeech,
    ) {
        var gpcs = theOneLibSynphony.selectGPCWordsWithArrayCompare(
            aDesiredGPCs,
            aKnownGPCs,
            restrictToKnownGPCs,
            allowUpperCase,
            aSyllableLengths,
            aSelectedGroups,
            aPartsOfSpeech,
        );
        return _.pluck(gpcs, "Name");
    }

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
    selectGPCWordsFromCache(
        aDesiredGPCs,
        aKnownGPCs,
        restrictToKnownGPCs,
        allowUpperCase,
        aSyllableLengths,
        aSelectedGroups,
        aPartsOfSpeech,
    ) {
        // check if the list of graphemes changed
        if (!theOneWordCache) {
            setTheOneWordCache(new WordCache());
        } else {
            if (
                theOneWordCache.desiredGPCs.length !== aDesiredGPCs.length ||
                theOneWordCache.knownGPCs.length !== aKnownGPCs.length ||
                _.intersection(theOneWordCache.desiredGPCs, aDesiredGPCs)
                    .length !== aDesiredGPCs.length ||
                _.intersection(theOneWordCache.knownGPCs, aKnownGPCs).length !==
                    aKnownGPCs.length
            ) {
                setTheOneWordCache(new WordCache());
            } else {
                // return the cached list
                return theOneWordCache.selectedWords;
            }
        }

        theOneWordCache!.desiredGPCs = aDesiredGPCs;
        theOneWordCache!.knownGPCs = aKnownGPCs;
        theOneWordCache!.selectedWords =
            theOneLibSynphony.selectGPCWordsWithArrayCompare(
                aDesiredGPCs,
                aKnownGPCs,
                restrictToKnownGPCs,
                allowUpperCase,
                aSyllableLengths,
                aSelectedGroups,
                aPartsOfSpeech,
            );

        return theOneWordCache!.selectedWords;
    }
    /**
     * Parses the langDataString into a theOneLanguageDataInstance object.
     * NOTE: Split into 2 functions, langDataFromString() and parseLangDataString(), for testing.
     * @param {String} langDataString
     * @returns {Boolean}
     */
    public langDataFromString(langDataString) {
        // Typically langDataString is from a ReaderToolsWords-xxx.json stored in sample texts.
        // An earlier version of this code let the results of parsing this file BECOME
        // theOneLanguageDataInstance. It's not clear to me why that was done. At this point we only
        // want it as a word list...unless we're reading the live word data file, which happens
        // before loading sample words. But if we're loading it as a sample words, we don't want
        // to replace other things in theOneLanguageDataInstance, like the grapheme list, with
        // whatever was current when the word list was created. Nor do we want to lose any sample
        // words already loaded from other sources, which we previously had to go to some trouble
        // to prevent. So I think it's best to keep the original theOneLanguageDataInstance and
        // just use this to add to the word list.
        // When we have a ReaderToolsWords-xxx.json used as sample words, it is not updated as other
        // sample word data is edited. This unfortunately means that any sample words stored in it
        // cannot easily be deleted. If the user deletes words in sample words that were in the original bloompack
        // ReaderToolsWords file, they won't disappear. Even if the user deletes the sample texts
        // version of ReaderToolsWords, Bloom will copy it again from the common data folder where
        // it was put while unpacking the bloompack. To really kill them, the user would have to know
        // to either delete the file from both Sample Texts and the common data folder, or to copy
        // the one from the project root folder into the sample texts folder.
        // But I don't see a better way to fix it while keeping compatibility with older bloompacks
        // (and without a heck of a lot of work for a fairly minor bug that hasn't yet even been
        // noticed by real users.)
        const newLangData = this.parseLangDataString(langDataString);
        if (theOneLanguageDataInstance) {
            if (newLangData && newLangData.group1) {
                for (let i = 0; i < newLangData.group1.length; i++) {
                    if (
                        !theOneLanguageDataInstance.findWord(
                            newLangData.group1[i].Name,
                        )
                    ) {
                        theOneLanguageDataInstance.group1.push(
                            newLangData.group1[i],
                        );
                    }
                }
            }
        } else {
            // I don't think this will ever happen, but for robustness I'm preserving an earlier behavior
            theOneLanguageDataInstance = newLangData;
        }

        theOneLibSynphony.processVocabularyGroups();

        return true;
    }

    /**
     * Parses the langDataString into a theOneLanguageDataInstance object
     * @param {String} langDataString
     * @returns {LanguageData}
     */
    public parseLangDataString(langDataString) {
        // check for setLangData( ... )
        var pos = langDataString.indexOf("{");
        if (pos > 0) langDataString = langDataString.substring(pos);

        // should end with } (closing brace)
        pos = langDataString.lastIndexOf("}");
        if (pos < langDataString.length - 1)
            langDataString = langDataString.substring(0, pos + 1);

        // fix errors and remove extra characters the JSON parser does not like
        langDataString = langDataString.replace("GPCS:", '"GPCS":'); // this name may not be inside double-quotes
        langDataString = langDataString.replace(/\/\/.*\r\n/g, "\r\n"); // remove comments from the file

        // load the data
        var langData = JSON.parse(langDataString);

        // add the functions from LanguageData
        return jQuery.extend(true, new LanguageData(), langData);
    }

    /**
     * Wraps words in <code>storyHTML</code> that are contained in <code>aWords</code>
     * @param {String} storyHTML
     * @param {Array} aWords
     * @param {String} cssClass
     * @param {String} extra
     * @returns {String}
     */
    public wrap_words_extra(storyHTML, aWords, cssClass, extra) {
        if (aWords === undefined || aWords.length === 0) return storyHTML;

        // Remove empty strings from the aWords array.  And if the array is then
        // empty, return the original storyHTML.
        aWords = aWords.filter((x) => x);
        if (aWords.length === 0) return storyHTML;

        if (storyHTML.trim().length === 0) return storyHTML;

        // make sure extra starts with a space
        if (extra.length > 0 && extra.substring(0, 1) !== " ")
            extra = " " + extra;

        var beforeWord = "(^\\s*|>\\s*|[\\s\\p{Z}]|\\p{P}|&nbsp;)"; // word beginning delimiter
        var afterWord =
            "(?=(\\s*$|\\s*<|[\\s\\p{Z}]|\\p{P}+\\s|\\p{P}+<br|[\\s]*&nbsp;|\\p{P}+&nbsp;|\\p{P}+$))"; // word ending delimiter

        // escape special characters
        var escapedWords = aWords.map(RegExp.quote);

        var regex = XRegExp(
            beforeWord + "(" + escapedWords.join("|") + ")" + afterWord,
            "xgi",
        );

        // We must not replace any occurrences inside <...>. For example, if html is abc <span class='word'>x</span>
        // and we are trying to wrap 'word', we should not change anything.
        // To prevent this we split the string into sections starting at <. If this is valid html, each except the first
        // should have exactly one >. We strip off everything up to the > and do the wrapping within the rest.
        // Finally we put the pieces back together.
        var parts = storyHTML.split("<");
        var modParts: any[] = [];
        for (var i = 0; i < parts.length; i++) {
            var text = parts[i];
            var prefix = "";
            if (i != 0) {
                var index = text.indexOf(">");
                prefix = text.substring(0, index + 1);
                text = text.substring(index + 1, text.length);
            }
            modParts.push(
                prefix +
                    XRegExp.replace(
                        text,
                        regex,
                        '$1<span class="' +
                            cssClass +
                            '"' +
                            extra +
                            ">$2</span>",
                    ),
            );
        }

        return modParts.join("<");
    }
}

// Extend RegExpConstructor to include the 'quote' method for TypeScript
declare global {
    interface RegExpConstructor {
        quote?(str: string): string;
    }
}

// function to escape special characters before performing a regular expression check
// # is required when using XRegEx with the 'x' option, which makes # a line comment delimiter
if (!RegExp.quote) {
    RegExp.quote = function (str: string): string {
        return (str + "").replace(/([#.?*+^$[\]\\(){}|-])/g, "\\$1");
    };
}

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
export function StoryCheckResults(
    focus_words,
    cumulative_words,
    possible_words,
    sight_words,
    remaining_words,
    readableWordCount,
    totalWordCount,
) {
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
StoryCheckResults.prototype.getNumbers = function () {
    var nums: any[] = [];
    var regex = XRegExp("^[\\p{N}\\p{P}]+$", "g");

    for (var i = 0; i < this.remaining_words.length; i++) {
        if (regex.test(this.remaining_words[i]))
            nums.push(this.remaining_words[i]);
    }

    return nums;
};

export const theOneLibSynphony = new LibSynphony();
const rubbish = 43;
