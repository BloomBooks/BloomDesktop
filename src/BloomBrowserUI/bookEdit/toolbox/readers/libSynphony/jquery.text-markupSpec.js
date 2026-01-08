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
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import $ from "jquery";

describe("jquery.text-markup", function () {
    function addDiv(id) {
        var div = document.createElement("div");
        div.id = id;
        document.body.appendChild(div);
        return div;
    }

    var divTextEntry1;
    var divTextEntry2;
    var divTextEntry3;

    beforeEach(function () {
        document.body.innerHTML = "";
        divTextEntry1 = addDiv("text_entry1");
        divTextEntry2 = addDiv("text_entry2");
        divTextEntry3 = addDiv("text_entry3");
    });

    afterEach(function () {
        document.body.innerHTML = "";
    });

    it("checkLeveledReader", function () {
        var input =
            'Two-word sentence. Thr<span data-cke-bookmark="1" style="display: none;" id="cke_bm_41C">&nbsp;</span>ee <span class="bold">"word"</span> sentence. "This is a six word sentence."';
        var out2 =
            'Two-word sentence. <span class="sentence-too-long" data-segment="sentence">Thr<span data-cke-bookmark="1" style="display: none;" id="cke_bm_41C">&nbsp;</span>ee <span class="bold">"word"</span> sentence.</span> <span class="sentence-too-long" data-segment="sentence">"This is a six word sentence."</span>';
        var out3 =
            'Two-word sentence. Thr<span data-cke-bookmark="1" style="display: none;" id="cke_bm_41C">&nbsp;</span>ee <span class="bold">"word"</span> sentence. <span class="sentence-too-long" data-segment="sentence">"This is a six word sentence."</span>';

        // check 2 word sentences
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 2 });
        var result = $("#text_entry1").html();
        expect(result).toBe(out2);

        // check 3 word sentences
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 3 });
        result = $("#text_entry1").html();
        expect(result).toBe(out3);
    });

    it("checkLeveledReader.handlesDivsWithEmbeddedParas", function () {
        var input =
            '<p>Two-word sentence. Three <span class="bold">"word"</span> sentence.<br></p><p>"This is a six word sentence."</p>';
        var out2 =
            '<p>Two-word sentence. <span class="sentence-too-long" data-segment="sentence">Three <span class="bold">"word"</span> sentence.</span><br></p><p><span class="sentence-too-long" data-segment="sentence">"This is a six word sentence."</span></p>';

        // check 2 word sentences
        $("#text_entry1")
            .html(input)
            .checkLeveledReader({ maxWordsPerSentence: 2 });
        var result = $("#text_entry1").html();
        expect(result).toBe(out2);
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
        var input = "a ae big";
        var out =
            'a <span class="possible-word" data-segment="word">ae</span> <span class="word-not-found" data-segment="word">big</span>';
        $("#text_entry1")
            .html(input)
            .checkDecodableReader({
                focusWords: ["a"],
                knownGraphemes: ["a", "e", "s"],
            });
        var result = $("#text_entry1").html();
        expect(result).toBe(out);
    });

    it("handles the magic word 'word'", function () {
        var input = "a ae word";
        var out =
            'a <span class="possible-word" data-segment="word">ae</span> <span class="word-not-found" data-segment="word">word</span>';
        $("#text_entry1")
            .html(input)
            .checkDecodableReader({
                focusWords: ["a"],
                knownGraphemes: ["a", "e", "s"],
            });
        var result = $("#text_entry1").html();
        expect(result).toBe(out);
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

    it("checkWrapWordsExtraIgnoresEmptyItems", function () {
        const cssWordNotFound = "word-not-found";
        const cssPossibleWord = "possible-word";
        const html = "<p>This is a test.</p>";
        const notFound = ["", "test", "", "is", ""];
        const newHtml = theOneLibSynphony.wrap_words_extra(
            html,
            notFound,
            cssWordNotFound,
            ' data-segment="word"',
        );
        expect(newHtml).toBe(
            '<p>This <span class="word-not-found" data-segment="word">is</span> a <span class="word-not-found" data-segment="word">test</span>.</p>',
        );
    });

    it("checkWrapWordsExtraHandlesExtraWhitespace", function () {
        const cssWordNotFound = "word-not-found";
        const cssPossibleWord = "possible-word";
        const html = "<p> This<em> is </em>a test.</p>";
        const notFound = ["this", "is", ""];
        const newHtml = theOneLibSynphony.wrap_words_extra(
            html,
            notFound,
            cssWordNotFound,
            ' data-segment="word"',
        );
        expect(newHtml).toBe(
            '<p> <span class="word-not-found" data-segment="word">This</span><em> <span class="word-not-found" data-segment="word">is</span> </em>a test.</p>',
        );
    });
});
