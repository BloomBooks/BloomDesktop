/**
 * jquery.text-markup.js
 *
 * Marking text according to various rules
 *
 * Created Apr 24, 2014 by Phil Hopper
 *
 */

import jQuery from "jquery";
import $ from "jquery";
import * as _ from "underscore";
import { theOneLibSynphony, LibSynphony } from "./synphony_lib";
import "./bloomSynphonyExtensions"; //add several functions to LanguageData
import { ReaderToolsModel } from "../readerToolsModel";
import { TextOffsetMap } from "../../../js/textHighlightManager";
import {
    kSentenceTooLongHighlight,
    kSightWordHighlight,
    kWordNotDecodableHighlight,
    kWordTooLongHighlight,
    makeRangesForSpans,
    mapReaderText,
    theOneReaderHighlightManager,
    TextSpan,
    trimSpan,
} from "../readerHighlights";

/**
 * Use an 'Immediately Invoked Function Expression' to make this compatible with jQuery.noConflict().
 * @param {jQuery} $
 */
(($) => {
    const cssDesiredGrapheme = "desired-grapheme";
    const cssTooMuchStuffOnPage = "page-too-many-words-or-sentences";

    /**
     * Checks the innerHTML of an HTML entity (div) using the selected options
     * @param {Object} options
     * @returns {Object}
     */
    $.fn.checkLeveledReader = function (options) {
        let allWords = "";
        const longWords: string[] = [];

        const opts = $.extend(
            {
                maxWordsPerSentence: Infinity,
                maxWordsPerPage: Infinity,
                maxSentencesPerPage: Infinity,
                maxGlyphsPerWord: Infinity,
            },
            options,
        );

        // 0 means unlimited. So convert them to Infinity
        if (opts.maxWordsPerSentence <= 0) {
            opts.maxWordsPerSentence = Infinity;
        }
        if (opts.maxWordsPerPage <= 0) {
            opts.maxWordsPerPage = Infinity;
        }
        if (opts.maxSentencesPerPage <= 0) {
            opts.maxSentencesPerPage = Infinity;
        }

        // Clean out markup spans inserted by earlier versions of Bloom, which used to mark
        // violations by modifying the DOM. Current code highlights without touching the DOM.
        this.removeSynphonyMarkup();

        // initialize words per page
        let totalWordCount = 0;
        // initialize sentences per page
        let totalSentenceCount = 0;

        // What we found in each leaf, kept until we have seen every leaf: the long-word list is
        // cumulative over the whole page, so we can't decide which words to highlight in the
        // first leaf until we have analyzed the last one.
        const leafResults: {
            map: TextOffsetMap;
            sentenceTooLongSpans: TextSpan[];
        }[] = [];

        const checkLeaf = (leaf: HTMLElement) => {
            // The text as the reader sees it, with a note of where in the DOM each character
            // came from so we can highlight our findings later. Analyzing plain text rather than
            // HTML means we don't have to preserve (or work around) bold/italic markup or
            // ckEditor's invisible landmarks: they are simply not in the string.
            const map = mapReaderText(leaf);
            const fragments = theOneLibSynphony.stringToSentences(map.text);
            const sentenceTooLongSpans: TextSpan[] = [];

            // The fragments concatenate to exactly map.text, so a running total tells us where
            // each one starts.
            let offset = 0;
            for (let i = 0; i < fragments.length; i++) {
                const fragment = fragments[i];

                if (fragment.isSpace) {
                    // this is inter-sentence space
                    allWords += " ";
                } else {
                    const words = theOneLibSynphony.getWordsFromHtmlString(
                        fragment.text,
                    );
                    if (opts.maxGlyphsPerWord > 0) {
                        for (const w of words) {
                            if (
                                ReaderToolsModel.getWordLength(w) >
                                opts.maxGlyphsPerWord
                            ) {
                                longWords.push(w);
                            }
                        }
                    }
                    const sentenceWordCount = words.length;
                    totalWordCount += sentenceWordCount;
                    allWords += fragment.text;
                    if (sentenceWordCount) ++totalSentenceCount;

                    // check sentence length
                    if (sentenceWordCount > opts.maxWordsPerSentence) {
                        sentenceTooLongSpans.push(
                            trimSpan(map.text, {
                                start: offset,
                                end: offset + fragment.text.length,
                            }),
                        );
                    }
                }
                offset += fragment.text.length;
            }

            // If this element represents a paragraph, then the overall page text needs a paragraph break here.
            if (leaf.tagName === "P") {
                allWords += "\r\n";
            }

            leafResults.push({ map, sentenceTooLongSpans });
        };

        const checkRoot = (root) => {
            const children = root.children();
            let processedChild = false; // Did we find a significant child?
            for (let i = 0; i < children.length; i++) {
                const child = children[i];
                const name = child.nodeName.toLowerCase();
                // Review: is there a better way to pick out the elements that can occur within content elements?
                if (
                    name != "span" &&
                    name != "br" &&
                    name != "i" &&
                    name != "b" &&
                    name != "u" &&
                    name != "em" &&
                    name != "strong" &&
                    name != "sup"
                ) {
                    processedChild = true;
                    checkRoot($(child));
                }
            }
            if (!processedChild)
                // root is a leaf; process its actual content
                checkLeaf(root.get(0));
            // Review: is there a need to handle elements that contain both sentence text AND child elements with their own text?
        };

        this.each(function () {
            checkRoot($(this));
        });
        // highlight the page for too many words or sentences found
        // (or remove any previous highlighting if it's all okay now)
        let pageDiv: JQuery;
        const page = parent.window.document.getElementById(
            "page",
        ) as HTMLIFrameElement;
        if (!page || !page.contentWindow) {
            pageDiv = $("body").find("div.bloom-page");
        } else {
            pageDiv = $("body", page.contentWindow.document).find(
                "div.bloom-page",
            );
        }
        if (
            totalWordCount > opts.maxWordsPerPage ||
            totalSentenceCount > opts.maxSentencesPerPage
        ) {
            pageDiv.addClass(cssTooMuchStuffOnPage);
        } else {
            pageDiv.removeClass(cssTooMuchStuffOnPage);
        }

        // Now that we know every long word on the page, highlight all of them, along with the
        // sentences we found to be too long.
        theOneReaderHighlightManager.beginPass();
        leafResults.forEach((leafResult) => {
            theOneReaderHighlightManager.addRanges(
                kSentenceTooLongHighlight,
                makeRangesForSpans(
                    leafResult.map,
                    leafResult.sentenceTooLongSpans,
                ),
            );
            theOneReaderHighlightManager.addRanges(
                kWordTooLongHighlight,
                makeRangesForSpans(
                    leafResult.map,
                    theOneLibSynphony.find_words_extra(
                        leafResult.map.text,
                        longWords,
                    ),
                ),
            );
        });
        theOneReaderHighlightManager.endPass(this[0]);

        this["allWords"] = allWords;
        return this;
    };

    /**
     * Checks the innerHTML of an HTML entity (div) using the selected options
     * @param {Object} options
     * @returns {Object}
     */
    $.fn.checkDecodableReader = function (options) {
        const opts = $.extend(
            {
                focusWords: [],
                previousWords: [],
                sightWords: [],
                knownGraphemes: [],
            },
            options,
        );
        let text = "";

        // Clean out markup spans inserted by earlier versions of Bloom, and the inline
        // background-color spans CKEditor made from them (BL-16558).
        this.removeSynphonyMarkup();
        this.removeCkEditorMarkup();

        // Snapshot the text of each element as the reader sees it, and get all the page's text.
        const maps: TextOffsetMap[] = [];
        this.each(function () {
            const map = mapReaderText(this);
            maps.push(map);
            text += " " + map.text;
        });

        /**
         * @type StoryCheckResults
         */
        const results = theOneLibSynphony.checkStory(
            opts.focusWords,
            opts.previousWords,
            opts.knownGraphemes,
            text,
            opts.sightWords.join(" "),
        );

        // remove numbers from list of bad words
        const notDecodable = _.difference(
            results.remaining_words,
            results.getNumbers(),
        ) as string[];

        theOneReaderHighlightManager.beginPass();
        maps.forEach((map) => {
            // ignore empty elements
            if (map.text.trim().length === 0 || text.trim().length === 0) {
                return;
            }
            theOneReaderHighlightManager.addRanges(
                kSightWordHighlight,
                makeRangesForSpans(
                    map,
                    theOneLibSynphony.find_words_extra(
                        map.text,
                        results.sight_words,
                    ),
                ),
            );
            theOneReaderHighlightManager.addRanges(
                kWordNotDecodableHighlight,
                makeRangesForSpans(
                    map,
                    theOneLibSynphony.find_words_extra(map.text, notDecodable),
                ),
            );
        });
        theOneReaderHighlightManager.endPass(this[0]);

        return this;
    };

    /**
     * Finds the maximum word count in the selected sentences.
     * @returns {int}
     */
    $.fn.getMaxSentenceLength = function () {
        let maxWords = 0;

        this.each(function () {
            // split into sentences
            let fragments = theOneLibSynphony.stringToSentences(
                removeAllHtmlMarkupFromString($(this).html()),
            );

            if (!fragments || fragments.length === 0) return;

            // remove inter-sentence space
            fragments = fragments.filter((frag) => {
                return frag.isSentence;
            });

            const subMax = Math.max(
                ...fragments.map((frag) => {
                    return frag.wordCount();
                }),
            );

            if (subMax > maxWords) maxWords = subMax;
        });

        return maxWords;
    };

    /**
     * Returns the count of all words in the selected elements.
     * @returns {int}
     */
    $.fn.getTotalWordCount = function () {
        let wordCount = 0;

        this.each(function () {
            // split into sentences
            let fragments = theOneLibSynphony.stringToSentences(
                removeAllHtmlMarkupFromString($(this).html()),
            );

            // remove inter-sentence space
            fragments = fragments.filter((frag) => {
                return frag.isSentence;
            });

            // sum of word counts
            for (let i = 0; i < fragments.length; i++)
                wordCount += fragments[i].wordCount();
        });

        return wordCount;
    };

    /**
     * Removes all the markup that was inserted by this addin
     */
    $.fn.removeSynphonyMarkup = function () {
        this.each(function () {
            // remove markup for deleted text
            $(this).find("span[data-segment=sentence]:empty").remove();
            $(this).find("span[data-segment=word]:empty").remove();
            $(this).find("span[data-segment=grapheme]:empty").remove();

            // remove previous sentence markup
            $(this).find("span[data-segment=sentence]").contents().unwrap();
            $(this).find("span[data-segment=word]").contents().unwrap();
            $(this).find("span[data-segment=grapheme]").contents().unwrap();
        });

        // remove page markup
        const page = parent.window.document.getElementById(
            "page",
        ) as HTMLIFrameElement;
        if (!page || !page.contentWindow)
            $("body")
                .find("div." + cssTooMuchStuffOnPage)
                .removeClass(cssTooMuchStuffOnPage);
        else
            $("body", page.contentWindow.document)
                .find("div." + cssTooMuchStuffOnPage)
                .removeClass(cssTooMuchStuffOnPage);
    };

    $.fn.removeCkEditorMarkup = function () {
        this.each(function () {
            // remove CKEditor-specific markup inserted when replacing marked text
            $(this)
                .find("span[style]")
                .filter((_index, element) => {
                    const style = (element as HTMLElement).getAttribute(
                        "style",
                    );
                    // CKeditor copies the highlight style as a barebones inline style containing
                    // only the background-color property.
                    return !!style && /^background-color: [^;]*;$/.test(style);
                })
                .contents()
                .unwrap();
            // leave CKEditor-specific hidden spans that serve as bookmarks intact so that
            // the cursor can be restored after marking up the text.  (BL-16490)
        });
    };

    $.extend({
        /**
         * Highlights selected graphemes in a word
         * @param {String} word
         * @param {String[]} gpcForm
         * @param {String[]} desiredGPCs
         * @returns {String}
         */
        markupGraphemes: (word, gpcForm, desiredGPCs) => {
            // for backward compatibility
            if (Array.isArray(word)) return oldMarkup(word, gpcForm);

            let returnVal = "";

            // loop through GPCForm
            for (let i = 0; i < gpcForm.length; i++) {
                const offset = gpcForm[i].length;
                const chars = word.substr(0, offset);

                if (desiredGPCs.indexOf(gpcForm[i]) > -1)
                    returnVal +=
                        '<span class="' +
                        cssDesiredGrapheme +
                        '" data-segment="grapheme">' +
                        chars +
                        "</span>";
                else returnVal += chars;

                word = word.slice(offset);
            }

            return returnVal;
        },
    });

    function oldMarkup(gpcForm, desiredGPCs) {
        let returnVal = "";

        // loop through GPCForm
        for (let i = 0; i < gpcForm.length; i++) {
            if (desiredGPCs.indexOf(gpcForm[i]) > -1)
                returnVal +=
                    '<span class="' +
                    cssDesiredGrapheme +
                    '" data-segment="grapheme">' +
                    gpcForm[i] +
                    "</span>";
            else returnVal += gpcForm[i];
        }

        return returnVal;
    }

    // We used to have to remove the formatButton (a div of UI at the end of the editable text)
    // before scanning, and put it back afterwards, because scanning meant rewriting the
    // element's HTML. Now that we only read the DOM, mapReaderText() simply skips it.
})(jQuery);

/**
 * Strip the HTML markup from a string
 * @param {string} textHtml
 * @returns {string}
 */
export function removeAllHtmlMarkupFromString(textHtml: string): string {
    // ensure spaces after line breaks and paragraph breaks
    const regex = /(<br><\/br>|<br>|<br ?\/>|<p><\/p>|<\/?p>|<p ?\/>|\n)/g;
    textHtml = textHtml.replace(regex, " ");

    // This regex is rather specific to the spans ckeditor sticks in as
    // 'landmarks' so the selection can be restored after manipulating the
    // markup. In principle we could have a more complex regex that would
    // remove all display:none spans, even if there are other explicit styles
    // or with single quotes around the style or with different white space.
    // However, we don't have a current need for it, so the extra
    // complication doesn't seem worthwhile.
    const ckeRegex = /<span [^>]*style="display: none;"[^>]*>[^<]*<\/span>/g;
    textHtml = textHtml.replace(ckeRegex, "");

    // Remove phrase delimiters used by the talking book tool.
    const phraseDelimeterRegex =
        /<span class=["']bloom-audio-split-marker["']>.<\/span>/g;
    textHtml = textHtml.replace(phraseDelimeterRegex, "");

    // Both open and close tags for markup
    const markupRegex = /<\/?(strong|em|sup|u|i|b|a|span)>/g;
    textHtml = textHtml.replace(markupRegex, "");

    // Open tags for more complex markup (ie, span and a tags).
    const complexMarkupRegex = /<(span|a)[ \r\n\t][^>]*>/g;
    textHtml = textHtml.replace(complexMarkupRegex, "");

    // This can sneak in on the current page.
    const divCogRegex = /<div id="formatButton"[^>]*><img[^>]*><\/div>/g;
    textHtml = textHtml.replace(divCogRegex, "");

    return $("<div>" + textHtml + "</div>").text();
}
