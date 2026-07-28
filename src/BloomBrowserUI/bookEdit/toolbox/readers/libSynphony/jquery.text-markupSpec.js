/**
 * jquery.text-markup.test.js
 *
 * Tests function in jquery.text-markup.js
 *
 * Created Apr 24, 2014 by Phil Hopper
 *
 */
import { theOneLibSynphony } from "./synphony_lib";
import { removeAllHtmlMarkupFromString } from "./jquery.text-markup.ts";
import {
    kSentenceTooLongHighlight,
    kWordNotDecodableHighlight,
    kWordTooLongHighlight,
} from "../readerHighlights";
import {
    getHighlightTexts,
    installHighlightPolyfill,
} from "../../../test/highlightTestSupport";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import $ from "jquery";

describe("jquery.text-markup", function () {
    function addDiv(id) {
        var div = document.createElement("div");
        div.id = id;
        document.body.appendChild(div);
        return div;
    }

    // The reader tools paint violations with ::highlight() pseudo-elements over Ranges rather
    // than by inserting spans, so this is how a test sees what got marked.
    function highlighted(highlightName) {
        return getHighlightTexts(window, highlightName);
    }

    var divTextEntry1;
    var divTextEntry2;
    var divTextEntry3;

    beforeEach(function () {
        document.body.innerHTML = "";
        installHighlightPolyfill(window);
        divTextEntry1 = addDiv("text_entry1");
        divTextEntry2 = addDiv("text_entry2");
        divTextEntry3 = addDiv("text_entry3");
    });

    afterEach(function () {
        document.body.innerHTML = "";
    });

    it("checkLeveledReader", function () {
        // The hidden span is one of CKEditor's selection bookmarks, sitting in the middle of
        // "Three". The markup must see through it: "Thr" + "ee" is one three-letter word, not two.
        var input =
            'Two-word sentence. Thr<span data-cke-bookmark="1" style="display: none;" id="cke_bm_41C">&nbsp;</span>ee <span class="bold">"word"</span> sentence. "This is a six word sentence."';
        // A Range's text includes anything invisible inside it, so the highlight over the second
        // sentence reads back with the bookmark's non-breaking space still in the middle of
        // "Three". Only the visible characters are painted.
        var nbsp = String.fromCharCode(0xa0);
        var threeWordSentence = `Thr${nbsp}ee "word" sentence.`;
        var sixWordSentence = '"This is a six word sentence."';

        // check 2 word sentences
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 2 });
        // The DOM must be left exactly as it was; marking it up is what BL-16558 is about.
        expect($("#text_entry1").html()).toBe(input);
        expect(highlighted(kSentenceTooLongHighlight)).toEqual([
            threeWordSentence,
            sixWordSentence,
        ]);

        // check 3 word sentences
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 3 });
        expect($("#text_entry1").html()).toBe(input);
        expect(highlighted(kSentenceTooLongHighlight)).toEqual([
            sixWordSentence,
        ]);
    });

    it("checkLeveledReader.handlesDivsWithEmbeddedParas", function () {
        var input =
            '<p>Two-word sentence. Three <span class="bold">"word"</span> sentence.<br></p><p>"This is a six word sentence."</p>';

        // check 2 word sentences
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 2 });
        expect($("#text_entry1").html()).toBe(input);
        expect(highlighted(kSentenceTooLongHighlight)).toEqual([
            'Three "word" sentence.',
            '"This is a six word sentence."',
        ]);
    });

    it("checkLeveledReader marks words that are too long", function () {
        $("#text_entry1")
            .html("<p>Cat elephant. Dog.</p>")
            .checkLeveledReader({ maxGlyphsPerWord: 5 });

        expect($("#text_entry1").html()).toBe("<p>Cat elephant. Dog.</p>");
        expect(highlighted(kWordTooLongHighlight)).toEqual(["elephant"]);
    });

    it("checkLeveledReader marks a long word wherever it appears on the page", function () {
        // The long-word list is cumulative over the page, so a word first seen in the second
        // paragraph must still be marked in the first one.
        $("#text_entry1")
            .html("<p>An elephant.</p><p>Another elephant.</p>")
            .checkLeveledReader({ maxGlyphsPerWord: 5 });

        expect(highlighted(kWordTooLongHighlight)).toEqual([
            "elephant",
            "Another",
            "elephant",
        ]);
    });

    it("checkLeveledReader stops highlighting once the violation is gone", function () {
        const editable = $("#text_entry1");
        editable
            .html("<p>Cat elephant. Dog.</p>")
            .checkLeveledReader({ maxGlyphsPerWord: 5 });
        // sanity check that there is something to stop highlighting
        expect(highlighted(kWordTooLongHighlight)).toEqual(["elephant"]);

        // Raise the limit, as the user would by choosing a higher level.
        editable.checkLeveledReader({ maxGlyphsPerWord: 20 });
        expect(highlighted(kWordTooLongHighlight)).toEqual([]);
    });

    // check the bug reported in BL-10119
    it("checkLeveledReader.handlesSentencesWithInitialMarkup", function () {
        const input =
            '<p>Short sentences exist. <em>Four</em> <strong>"word"</strong> sentences exist.</p><p>A five word sentence exists. <u>Shorter</u> sentences also exist.</p>';
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 5 });
        const result = $("#text_entry1").html();
        expect(result).toBe(input);
    });

    it("checkLeveledReader.handleDefaults.maxWordsPerSentence", function () {
        var input = "This sentence should have enough words";
        var out = "This sentence should have enough words";

        // check 2 word sentences
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 0 });
        var result = $("#text_entry1").html();
        expect(result).toBe(out);
    });

    it("checkLeveledReader.handleNestedSpans", function () {
        const input =
            '<p><span class="bloom-highlightSegment">This is a test,<span class="bloom-audio-split-marker">|</span></span></p>';
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 6 });
        const result = $("#text_entry1").html();
        expect(result).toBe(input);
    });

    it("marks up invalid words", function () {
        // "a" is decodable and "ae" is decodable-but-uncollected (we no longer mark those);
        // "big" uses graphemes the reader does not know yet.
        var input = "a ae big";
        $("#text_entry1")
            .html(input)
            .checkDecodableReader({
                focusWords: ["a"],
                knownGraphemes: ["a", "e", "s"],
            });
        expect($("#text_entry1").html()).toBe(input);
        expect(highlighted(kWordNotDecodableHighlight)).toEqual(["big"]);
    });

    it("handles the magic word 'word'", function () {
        var input = "a ae word";
        $("#text_entry1")
            .html(input)
            .checkDecodableReader({
                focusWords: ["a"],
                knownGraphemes: ["a", "e", "s"],
            });
        expect($("#text_entry1").html()).toBe(input);
        expect(highlighted(kWordNotDecodableHighlight)).toEqual(["word"]);
    });

    it("getMaxSentenceLength", function () {
        $("#text_entry1").html("Three word sentence. Short sentence.");
        $("#text_entry2").html(
            "Two-word sentence. A really longer six word sentence.",
        );
        $("#text_entry3").html(
            "Another four word sentence. A longer five word sentence.",
        );

        var result = $("div").getMaxSentenceLength();
        expect(result).toBe(6);
    });

    it("getMaxSentenceLength with tags", function () {
        $("#text_entry1").html(
            'Three <span class="bold">word</span> sentence. Short sentence.',
        );
        $("#text_entry2").html(
            'Two-word sentence. <span class="sentence-too-long" data-segment="sentence">A really longer six word sentence.</span>',
        );
        $("#text_entry3").html(
            "Another four word sentence.<br />A longer five word sentence.",
        );

        // check 2 word sentences
        var result = $("div").getMaxSentenceLength();
        expect(result).toBe(6);
    });

    it("getMaxSentenceLength - Thai", function () {
        // This is the same five-word sentence repeated with a space between.
        $("#text_entry1").html("ฉัน​มี​ยุง​ใน​บ้าน ฉัน​มี​ยุง​ใน​บ้าน");

        var extraPunctuationToTest = ["\\u0020", "\\U0020"];

        for (var i = 0; i < extraPunctuationToTest.length; i++) {
            theOneLibSynphony.setExtraSentencePunctuation(
                extraPunctuationToTest[i],
            );

            var result = $("div").getMaxSentenceLength();
            expect(result).toBe(5);
        }

        // Reset it for the next test
        theOneLibSynphony.setExtraSentencePunctuation("");
    });

    it("getTotalWordCount", function () {
        $("#text_entry1").html("Three word sentence. Short sentence.");
        $("#text_entry2").html(
            "Two-word sentence. A really longer six word sentence.",
        );
        $("#text_entry3").html(
            "Another four word sentence. A longer five word sentence.",
        );

        var result = $("div").getTotalWordCount();
        expect(result).toBe(22);
    });

    it("getTotalWordCount with tags", function () {
        $("#text_entry1").html(
            'Three <span class="bold">word</span> sentence. Short sentence.',
        );
        $("#text_entry2").html(
            'Two-word sentence. <span class="sentence-too-long" data-segment="sentence">A really longer six word sentence.</span>',
        );
        $("#text_entry3").html(
            "Another four word sentence.<br />A longer five word sentence.",
        );

        var result = $("div").getTotalWordCount();
        expect(result).toBe(22);
    });

    it("getTotalWordCount in Nepali", function () {
        // Two sentences w/ six words each. Between the two sentences there are 8 zero-width joiners.
        // The second sentence also contains a zero-width non-joiner, which should also not create a word break.
        // Therefore this text should yield a count of 12 words.
        $("#text_entry1").html("चम्‍ब लामाई दिम ब्रुम पङ्‍ज्‍यीम फुप्‍ची।");
        $("#text_entry2").html("बुम पङ्‌प थ्‍यामम्‌ छियम्‍से जम्‍ब खज्‍यी।");

        var result = $("div").getTotalWordCount();
        expect(result).toBe(12);
    });

    it("removeAllHtmlMarkup testing", function () {
        var out1 = removeAllHtmlMarkupFromString(
            '<p>An malipayon na adlaw ni Mando nabalyuh<span data-cke-bookmark="1" style="display: none;" id="cke_bm_78C">&nbsp;</span>an san pagkahanda kan Ondo.<span data-cke-bookmark="1" style="display: none;" id="cke_bm_36C">&nbsp;</span> <span data-cke-bookmark="1" style="display: none;" id="cke_bm_47C"></span></p>',
        );
        expect(out1).toBe(
            " An malipayon na adlaw ni Mando nabalyuhan san pagkahanda kan Ondo.  ",
        );

        var out2 = removeAllHtmlMarkupFromString(
            "<p>This <strong>is</strong> <em>a</em> <u>test</u> of <sup>some</sup> sort.</p>",
        );
        expect(out2).toBe(" This is a test of some sort. ");

        var out3 = removeAllHtmlMarkupFromString(
            "W<p></p>X<p/>Y<p />Z<p>A<br></br>B<br/>C<br />D</p>E",
        );
        expect(out3).toBe("W X Y Z A B C D E");

        var out4 = removeAllHtmlMarkupFromString(
            "A sti<span class='something'>tch</span> in <a href='https://somewhere.com/abcde/'>time</a> saves <i><b>nine</b></i>!",
        );
        expect(out4).toBe("A stitch in time saves nine!");

        var out5 = removeAllHtmlMarkupFromString(
            "<p><span id='xyzzy1' class='bloom-highlightSegment'>This is a test,<span class='bloom-audio-split-marker'>|</span></span> <span id='xyzzy2' class='bloom-highlightSegment'>this is only a test.</span></p>",
        );
        expect(out5).toBe(" This is a test, this is only a test. ");
    });

    it("removeCkEditorMarkup unwraps spans with background-color in style", function () {
        const input =
            '<p>pre <span style="background-color: rgb(255, 255, 155);">new text</span> post</p>';

        $("#text_entry1").html(input).removeCkEditorMarkup();

        expect($("#text_entry1").html()).toBe("<p>pre new text post</p>");
    });

    it("removeCkEditorMarkup unwraps spans with rgba() background-color", function () {
        const input =
            '<p>pre <span style="background-color: rgba(255, 255, 155, 0.5);">new text</span> post</p>';

        $("#text_entry1").html(input).removeCkEditorMarkup();

        expect($("#text_entry1").html()).toBe("<p>pre new text post</p>");
    });

    // We used to remove these, so they could not skew the word markup. But the hidden cke_*
    // spans include the selection bookmarks toolbox.ts inserts before asking us to update the
    // markup and uses afterwards to put the caret back, so removing them sent the insertion
    // point to the start of the text box after every typing pause. mapReaderText() skips them
    // instead, so there is no longer any reason to touch them.
    it("removeCkEditorMarkup leaves hidden cke_ spans alone, so the caret survives", function () {
        const input =
            '<p>pre <span id="cke_1" style="display: none;">hidden</span> post</p>';

        $("#text_entry1").html(input).removeCkEditorMarkup();

        expect($("#text_entry1").html()).toBe(input);
    });

    it("checkDecodableReader leaves the selection bookmarks in place", function () {
        // A bookmark span, holding CKEditor's usual zero-width placeholder, sits in the middle
        // of the word the reader tool is about to mark.
        const zwsp = String.fromCharCode(0x200b);
        const input = `<p>bi<span id="cke_bm_1S" style="display: none;">${zwsp}</span>g</p>`;
        $("#text_entry1")
            .html(input)
            .checkDecodableReader({
                focusWords: ["a"],
                knownGraphemes: ["a", "e", "s"],
            });

        // The bookmark must survive: toolbox.ts needs it to put the caret back afterwards.
        expect($("#text_entry1").html()).toBe(input);
        // One highlight, not two: the analysis saw "big" as a single word despite the bookmark
        // splitting it. (The painted Range spans the bookmark, so its text still contains the
        // invisible character.)
        expect(highlighted(kWordNotDecodableHighlight)).toEqual([`bi${zwsp}g`]);
    });

    // The words find_words_extra located, as substrings of the text it searched, so a test can
    // read its results without doing offset arithmetic.
    function foundWords(text, words) {
        return theOneLibSynphony
            .find_words_extra(text, words)
            .map((span) => text.substring(span.start, span.end));
    }

    it("find_words_extra ignores empty items in the word list", function () {
        const text = "This is a test.";
        expect(foundWords(text, ["", "test", "", "is", ""])).toEqual([
            "is",
            "test",
        ]);
    });

    it("find_words_extra matches case-insensitively and around extra whitespace", function () {
        const text = " This  is  a test.";
        expect(foundWords(text, ["this", "is", ""])).toEqual(["This", "is"]);
    });

    it("find_words_extra reports the offsets of each occurrence", function () {
        const text = "a cat and a cat";
        expect(theOneLibSynphony.find_words_extra(text, ["cat"])).toEqual([
            { start: 2, end: 5 },
            { start: 12, end: 15 },
        ]);
    });

    // The following three tests lock in that the analyzer (getWordsFromHtmlString) and the
    // highlighter (find_words_extra) agree about ZERO WIDTH SPACE (U+200B) being a word
    // boundary. They previously disagreed: the analyzer split words on U+200B while the
    // highlighter's \p{Z}/\p{P} boundaries did not match it (U+200B is Unicode category Cf,
    // not Zs), so a decodable word touching a ZWSP was left unmarked or mis-marked. See
    // BL-16490. We build the invisible characters with String.fromCharCode so this source
    // file stays free of them.
    it("getWordsFromHtmlString splits on ZERO WIDTH SPACE but not ZERO WIDTH JOINER (BL-16490)", function () {
        const zwsp = String.fromCharCode(0x200b);
        const zwj = String.fromCharCode(0x200d);

        // sanity check the test data really contains an invisible ZWSP in the middle
        const input = `cat${zwsp}dog`;
        expect(input.length).toBe(7);
        expect(input.indexOf(zwsp)).toBe(3);

        expect(theOneLibSynphony.getWordsFromHtmlString(input)).toEqual([
            "cat",
            "dog",
        ]);

        // ...but ZERO WIDTH JOINER is legitimate within a word and must NOT split it.
        expect(theOneLibSynphony.getWordsFromHtmlString(`ca${zwj}t`)).toEqual([
            `ca${zwj}t`,
        ]);
    });

    it("find_words_extra matches words bounded by a ZERO WIDTH SPACE (BL-16490)", function () {
        const zwsp = String.fromCharCode(0x200b);
        // A stray ZWSP sits between two words. Because the analyzer splits on it, both
        // "cat" and "dog" end up in the word list, and the highlighter must find both.
        const text = `cat${zwsp}dog`;
        // sanity check: the invisible ZWSP really is present in the input
        expect(text.indexOf(zwsp)).toBeGreaterThan(-1);

        expect(
            theOneLibSynphony.find_words_extra(text, ["cat", "dog"]),
        ).toEqual([
            { start: 0, end: 3 },
            // 4, not 3: the highlight must not swallow the ZWSP between the two words.
            { start: 4, end: 7 },
        ]);
    });

    it("find_words_extra matches a word immediately followed by a ZERO WIDTH SPACE (BL-16490)", function () {
        const zwsp = String.fromCharCode(0x200b);
        // "cat" is decodable; a stray ZWSP sits right after it, before a normal space.
        // Previously the afterWord boundary (\p{Z}/\p{P} only) did not see the ZWSP, so
        // "cat" was left unmarked.
        const text = `cat${zwsp} sat`;

        expect(theOneLibSynphony.find_words_extra(text, ["cat"])).toEqual([
            { start: 0, end: 3 },
        ]);
    });
});
