/**
 * Extensions to the LibSynphony class to support Bloom.
 * Converted minimally to ts in hopes of making Vitest happier. A lot more could be done
 * to define and use types.
 */
import XRegExp from "xregexp";
import "./bloom_xregexp_categories.js"; // reviewslog should add PEP to XRegExp, but it's not working
import { theOneLibSynphony, LanguageData, LibSynphony } from "./synphony_lib";
import * as _ from "underscore";
import jQuery from "jquery";

export function clearWordCache() {
    theOneWordCache = null;
}

export let theOneWordCache: WordCache | null;
export function setTheOneWordCache(cache: WordCache) {
    theOneWordCache = cache;
}

/**
 * Grapheme data in LanguageData.GPCS
 * @param {String} optionalGrapheme Optional. The grapheme to initialize the class.
 * @returns {DataGPC}
 */
export class DataGPC {
    public GPC = "";
    public GPCuc = "";
    public Grapheme = "";
    public Phoneme = "";
    public Category = "other";
    public Combining = "false";
    public Frequency = 1;
    public TokenFreq = 1;
    public IPA = "";
    public Alt = [];
    constructor(optionalGrapheme?: string) {
        var s = typeof optionalGrapheme === "undefined" ? "" : optionalGrapheme;

        this.GPC = s;
        this.GPCuc = s.toUpperCase();
        this.Grapheme = s;
        this.Phoneme = s;
    }
}

/**
 * Word data in LanguageData.geoup1
 * @param {String} optionalWord Optional. The word to initialize the class.
 * @returns {DataWord}
 */
export class DataWord {
    public Name = "";
    public Count = 1;
    public Group = 1;
    public PartOfSpeech = "";
    public GPCForm = [];
    public WordShape = "";
    public Syllables = 1;
    public Reverse = [];
    public html = "";
    public isSightWord = false;
    constructor(optionalWord?) {
        var w = typeof optionalWord === "undefined" ? "" : optionalWord;
        this.Name = w;
    }
}

/**
 * Class that holds text fragment information
 * @param {String} str The text of the fragment
 * @param {Boolean} isSpace <code>TRUE</code> if this fragment is inter-sentence space, otherwise <code>FALSE</code>.
 * @returns {TextFragment}
 */
export class TextFragment {
    public text: string;
    public isSentence: boolean;
    public isSpace: boolean;
    public words: string[];

    constructor(str, isSpace) {
        this.text = str;
        this.isSentence = !isSpace;
        this.isSpace = isSpace;
        this.words = theOneLibSynphony
            .getWordsFromHtmlString(
                jQuery(
                    "<div>" +
                        str.replace(/<br><\/br>|<br>|<br \/>|<br\/>/gi, "\n") +
                        "</div>",
                ).text(),
            )
            .filter(function (word) {
                return word != "";
            });
    }
    public wordCount() {
        return this.words.length;
    }
}

export class WordCache {
    public desiredGPCs;
    public knownGPCs;
    public selectedWords;
}

export function addBloomSynphonyExtensions() {}

// normally this just happens...immediately executed...but if that doesn't work for some
// mysterious reason (see spreadsheetBundleRoot.ts) we can call it explicitly.
addBloomSynphonyExtensions();
