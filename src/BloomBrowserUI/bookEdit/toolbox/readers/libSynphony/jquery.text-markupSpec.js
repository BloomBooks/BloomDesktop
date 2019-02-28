/**
 * jquery.text-markup.test.js
 *
 * Tests function in jquery.text-markup.js
 *
 * Created Apr 24, 2014 by Phil Hopper
 *
 */
import { theOneLibSynphony } from "./synphony_lib";

describe("jquery.text-markup", function() {
    function addDiv(id) {
        var div = document.createElement("div");
        div.id = id;
        document.body.appendChild(div);
        return div;
    }

    var divTextEntry1;
    var divTextEntry2;
    var divTextEntry3;

    beforeEach(function() {
        document.body.innerHTML = "";
        divTextEntry1 = addDiv("text_entry1");
        divTextEntry2 = addDiv("text_entry2");
        divTextEntry3 = addDiv("text_entry3");
    });

    afterEach(function() {
        document.body.innerHTML = "";
    });

    it("checkLeveledReader", function() {
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

    it("checkLeveledReader.handlesDivsWithEmbeddedParas", function() {
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

    it("marks up invalid words", function() {
        var input = "a ae big";
        var out =
            'a <span class="possible-word" data-segment="word">ae</span> <span class="word-not-found" data-segment="word">big</span>';
        $("#text_entry1")
            .html(input)
            .checkDecodableReader({
                focusWords: ["a"],
                knownGraphemes: ["a", "e", "s"]
            });
        var result = $("#text_entry1").html();
        expect(result).toBe(out);
    });

    it("handles the magic word 'word'", function() {
        var input = "a ae word";
        var out =
            'a <span class="possible-word" data-segment="word">ae</span> <span class="word-not-found" data-segment="word">word</span>';
        $("#text_entry1")
            .html(input)
            .checkDecodableReader({
                focusWords: ["a"],
                knownGraphemes: ["a", "e", "s"]
            });
        var result = $("#text_entry1").html();
        expect(result).toBe(out);
    });

    it("getMaxSentenceLength", function() {
        $("#text_entry1").html("Three word sentence. Short sentence.");
        $("#text_entry2").html(
            "Two-word sentence. A really longer six word sentence."
        );
        $("#text_entry3").html(
            "Another four word sentence. A longer five word sentence."
        );

        var result = $("div").getMaxSentenceLength();
        expect(result).toBe(6);
    });

    it("getMaxSentenceLength with tags", function() {
        $("#text_entry1").html(
            'Three <span class="bold">word</span> sentence. Short sentence.'
        );
        $("#text_entry2").html(
            'Two-word sentence. <span class="sentence-too-long" data-segment="sentence">A really longer six word sentence.</span>'
        );
        $("#text_entry3").html(
            "Another four word sentence.<br />A longer five word sentence."
        );

        // check 2 word sentences
        var result = $("div").getMaxSentenceLength();
        expect(result).toBe(6);
    });

    it("getMaxSentenceLength - Thai", function() {
        // This is the same five-word sentence repeated with a space between.
        $("#text_entry1").html("ฉัน​มี​ยุง​ใน​บ้าน ฉัน​มี​ยุง​ใน​บ้าน");

        var extraPunctuationToTest = ["\\u0020", "\\U0020"];

        for (var i = 0; i < extraPunctuationToTest.length; i++) {
            theOneLibSynphony.setExtraSentencePunctuation(
                extraPunctuationToTest[i]
            );

            var result = $("div").getMaxSentenceLength();
            expect(result).toBe(5);
        }

        // Reset it for the next test
        theOneLibSynphony.setExtraSentencePunctuation("");
    });

    it("getTotalWordCount", function() {
        $("#text_entry1").html("Three word sentence. Short sentence.");
        $("#text_entry2").html(
            "Two-word sentence. A really longer six word sentence."
        );
        $("#text_entry3").html(
            "Another four word sentence. A longer five word sentence."
        );

        var result = $("div").getTotalWordCount();
        expect(result).toBe(22);
    });

    it("getTotalWordCount with tags", function() {
        $("#text_entry1").html(
            'Three <span class="bold">word</span> sentence. Short sentence.'
        );
        $("#text_entry2").html(
            'Two-word sentence. <span class="sentence-too-long" data-segment="sentence">A really longer six word sentence.</span>'
        );
        $("#text_entry3").html(
            "Another four word sentence.<br />A longer five word sentence."
        );

        var result = $("div").getTotalWordCount();
        expect(result).toBe(22);
    });
});
