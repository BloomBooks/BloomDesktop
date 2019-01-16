import AudioRecording, { AudioRecordingMode } from "./audioRecording";

describe("audio recording tests", () => {
    it("inserts sentence spans with ids and class when none exist", () => {
        const div = $("<div>This is a sentence. This is another</div>");
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(div);
        const spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe("This is a sentence.");
        expect(spans[1].innerHTML).toBe("This is another");
        expect(div.text()).toBe("This is a sentence. This is another");
        expect(spans.first().attr("id")).not.toBe(
            spans
                .first()
                .next()
                .attr("id")
        );
        expect(spans.first().attr("class")).toBe("audio-sentence");
        expect(spans.last().attr("class")).toBe("audio-sentence");
    });
    it("retains matching sentence spans with same ids.keeps md5s and adds missing ones", () => {
        const div = $(
            '<div><p><span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span> This is another</p></div>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(div);
        const spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe("This is a sentence.");
        expect(spans[1].innerHTML).toBe("This is another");
        expect(div.text()).toBe("This is a sentence. This is another");
        expect(spans.first().attr("id")).toBe("abc");
        expect(spans.first().attr("recordingmd5")).toBe(
            "d15ba5f31fa7c797c093931328581664"
        );
        expect(spans.first().attr("id")).not.toBe(
            spans
                .first()
                .next()
                .attr("id")
        );
        expect(spans.first().attr("class")).toBe("audio-sentence");
        expect(spans.last().attr("class")).toBe("audio-sentence");
    });
    it("retains markup within sentences", () => {
        const div = $(
            '<div><p><span id="abc" class="audio-sentence">This <b>is</b> a sentence.</span> This <i>is</i> another</p></div>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(div);
        const spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe("This <b>is</b> a sentence.");
        expect(spans[1].innerHTML).toBe("This <i>is</i> another");
    });
    it("keeps id with unchanged recorded sentence when new inserted before", () => {
        const div = $(
            '<div><p>This is a new sentence. <span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span></p></div>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(div);
        const spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe("This is a new sentence.");
        expect(spans[1].innerHTML).toBe("This is a sentence.");
        expect(div.text()).toBe("This is a new sentence. This is a sentence.");
        expect(
            spans
                .first()
                .next()
                .attr("id")
        ).toBe("abc"); // with matching md5 id should stay with sentence
        expect(
            spans
                .first()
                .next()
                .attr("recordingmd5")
        ).toBe("d15ba5f31fa7c797c093931328581664");
        expect(spans.first().attr("id")).not.toBe(
            spans
                .first()
                .next()
                .attr("id")
        );
        expect(spans.first().attr("class")).toBe("audio-sentence");
        expect(spans.last().attr("class")).toBe("audio-sentence");
    });
    it("keeps ids and md5s when inserted between", () => {
        const div = $(
            '<div><p><span id="abcd" recordingmd5="qed" class="audio-sentence">This is the first sentence.</span> This is inserted. <span id="abc" recordingmd5="d15ba5f31fa7c797c093931328581664" class="audio-sentence">This is a sentence.</span> Inserted after.</p></div>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(div);
        const spans = div.find("span");
        expect(spans.length).toBe(4);
        expect(spans[0].innerHTML).toBe("This is the first sentence.");
        expect(spans[1].innerHTML).toBe("This is inserted.");
        expect(spans[2].innerHTML).toBe("This is a sentence.");
        expect(spans[3].innerHTML).toBe("Inserted after.");
        expect(div.text()).toBe(
            "This is the first sentence. This is inserted. This is a sentence. Inserted after."
        );
        expect(spans.first().attr("id")).toBe("abcd"); // with matching md5 id should stay with sentence
        expect(
            spans
                .first()
                .next()
                .next()
                .attr("id")
        ).toBe("abc"); // with matching md5 id should stay with sentence
        expect(
            spans
                .first()
                .next()
                .next()
                .attr("recordingmd5")
        ).toBe("d15ba5f31fa7c797c093931328581664");
        // The first span is reused just by position, since its md5 doesn't match, but it should still keep it.
        expect(spans.first().attr("recordingmd5")).toBe("qed");
        expect(spans.first().attr("id")).not.toBe(
            spans
                .first()
                .next()
                .attr("id")
        );
        expect(
            spans
                .first()
                .next()
                .attr("id")
        ).not.toBe(
            spans
                .first()
                .next()
                .next()
                .attr("id")
        );
        expect(
            spans
                .first()
                .next()
                .next()
                .attr("id")
        ).not.toBe(
            spans
                .first()
                .next()
                .next()
                .next()
                .attr("id")
        );
        expect(spans.first().attr("class")).toBe("audio-sentence");
        expect(spans.last().attr("class")).toBe("audio-sentence");
        expect(
            spans
                .first()
                .next()
                .attr("class")
        ).toBe("audio-sentence");
    });

    // We can get something like this when we paste from Word
    it("ignores empty span", () => {
        const div = $(
            '<div><p>This is the first sentence.<span data-cke-bookmark="1" style="display: none;" id="cke_bm_35C"> </span></p></div>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(div);
        const spans = div.find("span");
        expect(spans.length).toBe(2);
        expect(spans[0].innerHTML).toBe("This is the first sentence.");
        expect(spans[1].innerHTML).toBe(" ");
        expect(spans.first().attr("class")).toBe("audio-sentence");
        expect(spans.last().attr("class")).not.toContain("audio-sentence");
    });

    // We can get something like this when we paste from Word
    it("ignores empty span and <br>", () => {
        const p = $(
            '<p><span data-cke-bookmark="1" style="display: none;" id="cke_bm_35C">&nbsp;</span><br></p>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(p);
        const spans = p.find("span");
        expect(spans.length).toBe(1);
        expect(spans[0].innerHTML).toBe("&nbsp;");
        expect(spans.first().attr("class")).not.toContain("audio-sentence");
    });

    it("flattens nested audio spans", () => {
        const p = $(
            '<p><span id="efgh" recordingmd5="xyz" class="audio-sentence"><span id="abcd" recordingmd5="qed" class="audio-sentence">This is the first.</span> <span id="abde" recordingmd5="qef" class="audio-sentence">This is the second.</span> This is the third.</span></p>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(p);
        const spans = p.find("span");
        // Should have removed the outer span and left the two inner ones and added a third one.
        expect(spans.length).toBe(3);
        expect(spans.first().attr("id")).toBe("abcd");
        expect(
            spans
                .first()
                .next()
                .attr("id")
        ).toBe("abde");
        expect(spans[0].innerHTML).toBe("This is the first.");
        expect(spans[1].innerHTML).toBe("This is the second.");
        expect(spans[2].innerHTML).toBe("This is the third.");
        expect(spans.first().attr("class")).toBe("audio-sentence");
        expect(
            spans
                .first()
                .next()
                .attr("class")
        ).toBe("audio-sentence");
        expect(
            spans
                .first()
                .next()
                .next()
                .attr("class")
        ).toBe("audio-sentence");
    });

    it("ensures full span coverage of paragraph", () => {
        // based on BL-6038 user data
        const p = $(
            '<p>Random text <strong><span data-duration="9.400227" id="abcd" class="audio-sentence" recordingmd5="undefined"><u>underlined</u></span></strong> finish the sentence. Another sentence <u><strong>boldunderlined</strong></u> finish the second.</p>'
        );
        const recording = new AudioRecording();
        recording.makeAudioSentenceElements(p);
        const spans = p.find("span");
        // Should have expanded the first span and created one for the second sentence.
        expect(spans.length).toBe(2);
        expect(spans.first().attr("id")).toBe("abcd");
        // expect(spans.first().next().attr("id")).toBe("abde");
        expect(spans[0].innerHTML).toBe(
            "Random text <strong><u>underlined</u></strong> finish the sentence."
        );
        expect(spans[1].innerHTML).toBe(
            "Another sentence <u><strong>boldunderlined</strong></u> finish the second."
        );
        expect(spans.first().attr("class")).toBe("audio-sentence");
        expect(
            spans
                .first()
                .next()
                .attr("class")
        ).toBe("audio-sentence");
    });

    it("converts from unmarked to text-box (bloom-editable includes format button)", () => {
        // This tests real input from Bloom that has not been marked up. (e.g. if the Talking Book dialog is opened up for the first time on an existing page while the Collection default is by-sentence)

        const formatButtonHtml =
            '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
        const textBoxInnerHtml =
            "<p>Paragraph 1 Sentence 1. Paragraph 1, Sentence 2.</p> <p>Paragraph 2, Sentence 1. Paragraph 2, Sentence 2.</p>";
        const originalHtml =
            '<div class="bloom-editable">' +
            textBoxInnerHtml +
            formatButtonHtml +
            "</div>";
        const div = $(originalHtml);
        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);

        expect(div.text()).toBe(
            "Paragraph 1 Sentence 1. Paragraph 1, Sentence 2. Paragraph 2, Sentence 1. Paragraph 2, Sentence 2.",
            "div text"
        );

        const spans = div.find("span");
        expect(spans.length).toBe(
            0,
            "number of spans does not match expected count"
        );

        const parent = $("<div>")
            .append(div)
            .clone();

        const divs = parent.find("div");
        expect(divs.length).toBe(
            2,
            "number of divs does not match expected count"
        );

        expect($(divs[0]).is(".audio-sentence")).toBe(true, "textbox's class");
        expect($(divs[0]).attr("id")).not.toBe(undefined, "textbox's id");
        expect($(divs[0]).attr("id").length).toBeGreaterThan(
            31,
            "textbox's id"
        ); // GUID without hyphens is 32 chars longs
        expect($(divs[0]).attr("id").length).toBeLessThan(38, "textbox's id"); // GUID with hyphens adds 4 chars. And we sometimes insert a 1-char prefix, adding up to 37.

        expect($(divs[1]).is(".audio-sentence")).toBe(
            false,
            "formatButton's class"
        );
        expect($(divs[1]).attr("id")).toBe("formatButton", "formatButton's id");
        expect(divs[1].outerHTML).toBe(
            formatButtonHtml,
            "formatButton's outerHTML"
        );

        const paragraphs = div.find("p");
        expect(paragraphs.length).toBe(2, "number of paragraphs");
        paragraphs.each((index, paragraph) => {
            expect($(paragraph).is(".audio-sentence")).toBe(
                false,
                "paragraph " + index + " class"
            );
            expect(paragraph.id).toBe("", "paragraph " + index + " id"); // If id attribute is not set, this actually returns empty string, which is kinda surprising.
        });

        expect(parent.html().indexOf('id=""')).toBe(
            -1,
            "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)"
        );

        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox"><p>Paragraph 1 Sentence 1. Paragraph 1, Sentence 2.</p> <p>Paragraph 2, Sentence 1. Paragraph 2, Sentence 2.</p>' +
                formatButtonHtml +
                "</div>",
            "Parent HTML"
        );

        recording.audioRecordingMode = AudioRecordingMode.Sentence;
        recording.makeAudioSentenceElements(div);
        expect(div.text).toBe($(originalHtml).text, "Swap back test");
        // Note: It is not expected that going to by-sentence to here will lead back the original HTML structure. (Because we started with unmarked text, not by-sentence)
    });

    it("converts from single unmarked paragraph to text-box", () => {
        // This tests real input from Bloom that has not been marked up. (e.g. if the Talking Book dialog is opened up for the first time on an existing page while the Collection default is by-sentence)
        // Note: a single paragraph does not have any newlines (<br>) tags in it so it could exercise a different path through the recursion.
        const formatButtonHtml =
            '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
        const textBoxInnerHtml = "<p>Hello world</p>";
        const originalHtml =
            '<div class="bloom-editable">' +
            textBoxInnerHtml +
            formatButtonHtml +
            "</div>";
        const div = $(originalHtml);
        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);

        expect(div.text()).toBe("Hello world", "div text");

        const spans = div.find("span");
        expect(spans.length).toBe(0, "number of spans");

        const parent = $("<div>")
            .append(div)
            .clone();
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox"><p>Hello world</p>' +
                formatButtonHtml +
                "</div>",
            "Parent HTML"
        );

        const divs = parent.find("div");
        expect(divs.length).toBe(2, "number of divs");

        expect($(divs[0]).is(".audio-sentence")).toBe(true, "textbox's class");
        expect(divs[0].id.length).toBeGreaterThan(31, "textbox's id length");
        expect(divs[0].id.length).toBeLessThan(38, "textbox's id length");

        expect($(divs[1]).is(".audio-sentence")).toBe(
            false,
            "formatButton's class"
        );
        expect(divs[1].id).toBe("formatButton", "formatButton's id");
        expect(divs[1].outerHTML).toBe(
            formatButtonHtml,
            "formatButton's outerHTML"
        );

        const paragraphs = div.find("p");
        expect(paragraphs.length).toBe(1, "number of paragraphs");
        paragraphs.each((index, paragraph) => {
            expect($(paragraph).is(".audio-sentence")).toBe(
                false,
                "paragraph " + index + " class"
            );
            expect(paragraph.id).toBe("", "paragraph " + index + " id");
        });

        expect(parent.html().indexOf('id=""')).toBe(
            -1,
            "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)"
        );

        recording.audioRecordingMode = AudioRecordingMode.Sentence;
        recording.makeAudioSentenceElements(div);
        expect(div.text).toBe($(originalHtml).text, "Swap back test");
        // Note: It is not expected that going to by-sentence to here will lead back the original HTML structure. (Because we started with unmarked text, not by-sentence)
    });

    it("converts from single marked by-sentence paragraph to text-box", () => {
        // Note: a single paragraph does not have any newlines (<br>) tags in it so it could exercise a different path through the recursion.
        const formatButtonHtml =
            '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
        const textBoxInnerHtml =
            '<p><span id="ef142986-373a-4353-808f-a05d9478c0ed" class="audio-sentence">Hello world</span></p>';
        const originalHtml =
            '<div class="bloom-editable" role="textbox">' +
            textBoxInnerHtml +
            formatButtonHtml +
            "</div>";
        const div = $(originalHtml);
        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);

        expect(div.text()).toBe("Hello world", "div text");

        const parent = $("<div>")
            .append(div)
            .clone();
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            '<div class="bloom-editable audio-sentence" role="textbox" data-audiorecordingmode="TextBox"><p>Hello world</p>' +
                formatButtonHtml +
                "</div>",
            "Parent HTML"
        );

        const divs = parent.find("div");
        expect(divs.length).toBe(2, "number of divs");

        expect($(divs[0]).is(".audio-sentence")).toBe(true, "textbox's class");
        expect($(divs[0]).attr("id")).not.toBe(undefined), "textbox's id";
        expect(divs[0].id.length).toBeGreaterThan(31, "textbox's id length");
        // Enhance: It would be great if it preserve the original one
        //expect(divs[0].id).toBe("ef142986-373a-4353-808f-a05d9478c0ed", "textbox's id");

        expect($(divs[1]).is(".audio-sentence")).toBe(
            false,
            "formatButton's class"
        );
        expect(divs[1].id).toBe("formatButton", "formatButton's id");
        expect(divs[1].outerHTML).toBe(
            formatButtonHtml,
            "formatButton's outerHTML"
        );

        const spans = div.find("span");
        expect(spans.length).toBe(0, "number of spans");

        expect(parent.html().indexOf('id=""')).toBe(
            -1,
            "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)"
        );

        recording.audioRecordingMode = AudioRecordingMode.Sentence;
        recording.makeAudioSentenceElements(div);
        expect(div.text).toBe($(originalHtml).text, "Swap back test");
        // Note: It is not expected that going to by-sentence to here will lead back the original HTML structure. (Because we started with unmarked text, not by-sentence)
    });

    it("converts from single line text-box to by-sentence", () => {
        const originalHtml =
            '<div id="ba497822-afe7-4e16-90e8-91a795242720" class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on cke_editable cke_editable_inline cke_contents_ltr normal-style audio-sentence" data-languagetipcontent="English" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" data-audiorecordingmode="TextBox" lang="en" contenteditable="true"><p>hi<br></p><div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div></div>';
        const div = $(originalHtml);
        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.Sentence;
        recording.makeAudioSentenceElements(div);

        const parent = $("<div>")
            .append(div)
            .clone();

        const spans = parent.find("span");
        expect(spans.length).toBe(1, "number of spans");

        const paragraphs = parent.find("p");
        expect(paragraphs.length).toBe(1, "number of spans");

        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            '<div class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on cke_editable cke_editable_inline cke_contents_ltr normal-style" data-languagetipcontent="English" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" data-audiorecordingmode="Sentence" lang="en" contenteditable="true"><p><span class="audio-sentence">hi<br></span></p><div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div></div>',
            "Parent html"
        );
    });

    it("converts from by-sentence to text-box (bloom-editable includes format button)", () => {
        // This tests real input from Bloom that has already been marked up in by-sentence mode. (i.e., this is executed upon un-clicking the checkbox from by-sentence to not-by-sentence)
        const textBoxDivHtml =
            '<div class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style cke_editable cke_editable_inline cke_contents_ltr" data-languagetipcontent="English" data-audiorecordingmode="Sentence" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" lang="en" contenteditable="true">';
        const paragraphsMarkedBySentenceHtml =
            '<p><span id="i663e4f39-2d34-4624-829f-e927a58e2101" class="audio-sentence ui-audioCurrent">Sentence 1.</span> <span id="d5df952d-dd60-4790-bb9d-e24fb9b5d4da" class="audio-sentence">Sentence 2.</span> <span id="i66e6edf8-49bf-4fb0-b48f-ab8235e3b902" class="audio-sentence">Sentence 3.</span><br></p><p><span id="i828de727-4ef9-45ef-afd6-4841bbe0b3d3" class="audio-sentence">Paragraph 2.</span><br></p>';
        const formatButtonHtml =
            '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
        const originalHtml =
            textBoxDivHtml +
            paragraphsMarkedBySentenceHtml +
            formatButtonHtml +
            "</div>";
        const div = $(originalHtml);

        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);

        let parent = $("<div>")
            .append(div)
            .clone();

        const spans = parent.find("span");
        expect(spans.length).toBe(0, "number of spans");

        const divs = parent.find("div");
        expect(divs.length).toBe(2, "number of divs");
        expect($(divs[0]).is(".audio-sentence")).toBe(true, "textbox's class");
        expect($(divs[0]).attr("id").length).toBeGreaterThan(31),
            "textbox's id"; // GUID without hyphens is 32 chars longs
        expect($(divs[0]).attr("id").length).toBeLessThan(38), "textbox's id"; // GUID with hyphens adds 4 chars. And we sometimes insert a 1-char prefix, adding up to 37.
        expect($(divs[1]).attr("id")).toBe("formatButton"), "formatButton's id";

        const paragraphs = parent.find("p");
        expect(paragraphs.length).toBe(2, "number of paragraphs");
        paragraphs.each((index, paragraph) => {
            expect($(paragraph).attr("id")).toBe(
                undefined,
                "paragraph " + index + " id"
            ); // If id attribute is not set, this actually returns empty string, which is kinda surprising.
            expect($(paragraph).hasClass("audio-sentence")).toBe(
                false,
                "paragraph " + index + " class"
            );
        });

        expect(parent.html().indexOf('id=""')).toBe(
            -1,
            "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)"
        );

        const expectedTextBoxDiv = $(textBoxDivHtml)
            .attr("data-audiorecordingmode", "TextBox")
            .addClass("audio-sentence");
        const expectedTextBoxDivHtml = $("<div>")
            .append(expectedTextBoxDiv)
            .html()
            .replace(/<\/div>/, "");
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            expectedTextBoxDivHtml +
                "<p>Sentence 1. Sentence 2. Sentence 3.<br></p><p>Paragraph 2.<br></p>" +
                StripAllGuidIds(formatButtonHtml) +
                "</div>",
            "Parent HTML"
        );

        recording.audioRecordingMode = AudioRecordingMode.Sentence;
        recording.makeAudioSentenceElements(div);
        parent = $("<div>")
            .append(div)
            .clone();
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            StripAllGuidIds(StripEmptyClasses(StripAudioCurrent(originalHtml))),
            "Swap back to original"
        );
    });

    it("converts by-text-box into by-sentence (bloom-editable includes format button)", () => {
        // This tests real input from Bloom that has been marked up in by-text-box mode (e.g., clicking the checkbox from not-by-sentence into by-sentence)
        const textBoxDivHtml =
            '<div id="ee41e518-7855-472a-b8ce-a0c6caa68341" aria-label="false" role="textbox" spellcheck="true" tabindex="0" style="min-height: 24px;" class="bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style audio-sentence" data-languagetipcontent="English" data-audiorecordingmode="TextBox" lang="en" contenteditable="true">';
        const formatButtonHtml =
            '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
        const originalHtml =
            '<div id="page">' +
            textBoxDivHtml +
            "<p>Sentence 1. Sentence 2. Sentence 3.<br></p><p>Paragraph 2.<br></p>" +
            formatButtonHtml +
            "</div></div>";
        const div = $(originalHtml);

        let recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.Sentence;
        recording.makeAudioSentenceElements(div);

        // TODO: Ideally we would be able to call updateaudioRecordingMode() or UpdateMarkupAndCurrentText() instead of makeAudioSentenceElements().
        // That would enable us to verify that ui-audioCurrent gets set properly.
        // But this relies on calling GetPageFrame(). How do you set up the parent / window from a test case so that GetPageFrame() doesn't return null?
        // recording.updateAudioRecordingMode();
        // expect(div.find("ui-audioCurrent").text()).toBe("Sentence 1.", "Current Sentence text");

        expect(div.text()).toBe(
            "Sentence 1. Sentence 2. Sentence 3.Paragraph 2.",
            "div text"
        );

        const spans = div.find("span");
        expect(spans.length).toBe(4, "number of spans");
        spans.each((index, span) => {
            expect(span.id.length).toBeGreaterThan(31, "span " + index + " id");
            expect($(span).hasClass("audio-sentence")).toBe(
                true,
                "span " + index + " class"
            );
        });

        expect($(spans[0]).text()).toBe("Sentence 1.");
        expect($(spans[3]).text()).toBe("Paragraph 2.");

        const paragraphs = div.find("p");
        expect(paragraphs.length).toBe(2, "number of paragraphs");
        paragraphs.each((index, paragraph) => {
            expect(paragraph.id).toBe("", "paragraph " + index + " id"); // If id attribute is not set, this actually returns empty string, which is kinda surprising.
            expect($(paragraph).hasClass("audio-sentence")).toBe(
                false,
                "paragraph " + index + " class"
            );
        });

        let parent = $("<div>")
            .append(div)
            .clone();
        const expectedTextBoxDiv = $(textBoxDivHtml)
            .attr("data-audioRecordingMode", "Sentence")
            .removeClass("audio-sentence")
            .removeAttr("id");
        const expectedTextBoxDivHtml = $("<div>")
            .append(expectedTextBoxDiv)
            .html()
            .replace(/<\/div>/, "");
        const expectedTextBoxInnerHtml =
            '<p><span class="audio-sentence">Sentence 1.</span> <span class="audio-sentence">Sentence 2.</span> <span class="audio-sentence">Sentence 3.</span><br></p><p><span class="audio-sentence">Paragraph 2.</span><br></p>';
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            '<div id="page">' +
                expectedTextBoxDivHtml +
                expectedTextBoxInnerHtml +
                formatButtonHtml +
                "</div></div>",
            "parent.html"
        );

        expect(parent.html().indexOf('id=""')).toBe(
            -1,
            "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)"
        );

        // Test that you can switch back and recover more-or-less the original
        recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);
        parent = $("<div>")
            .append(div)
            .clone();
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            StripAllGuidIds(StripEmptyClasses(StripAudioCurrent(originalHtml))),
            "Swap back to original"
        );
    });

    it("loads by-text-box without changing anything", () => {
        // This tests real input from Bloom that has been marked up in by-text-box mode (e.g., clicking the checkbox from not-by-sentence into by-sentence)
        const textBoxDivHtml =
            '<div id="ee41e518-7855-472a-b8ce-a0c6caa68341" aria-label="false" role="textbox" spellcheck="true" tabindex="0" style="min-height: 24px;" class="bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style audio-sentence ui-audioCurrent" data-languagetipcontent="English" data-audiorecordingmode="TextBox" lang="en" contenteditable="true">';
        const formatButtonHtml =
            '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
        const originalHtml =
            '<div id="page">' +
            textBoxDivHtml +
            "<p>Sentence 1. Sentence 2. Sentence 3.<br></p><p>Paragraph 2.<br></p>" +
            formatButtonHtml +
            "</div></div>";
        const div = $(originalHtml);

        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);

        const parent = $("<div>")
            .append(div)
            .clone();
        expect(parent.html()).toBe(
            originalHtml,
            "re-load identical content test"
        );
    });

    it("converts from unmarked to text-box (no format button)", () => {
        // The input is hypothetical to exercise corner cases in the code, but these inputs are not actually expected to show up in normal usage.
        const textBoxInnerHtml =
            "<p>Paragraph 1A. Paragraph 1B.<br></p><p>Paragraph 2A. Paragraph 2B.<br></p>";
        const originalHtml =
            '<div class="bloom-editable">' + textBoxInnerHtml + "</div>";
        const div = $(originalHtml);
        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);

        expect(div.text()).toBe(
            "Paragraph 1A. Paragraph 1B.Paragraph 2A. Paragraph 2B.",
            "div text"
        );
        const parent = $("<div>")
            .append(div)
            .clone();
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox">' +
                textBoxInnerHtml +
                "</div>",
            "Parent HTML"
        );
    });

    it("hypothetically converts from by-sentence to text-box (multiple audio-sentence runs, ends with an audio-sentence)", () => {
        // The input is hypothetical to exercise corner cases in the code, but these inputs are not actually expected to show up in normal usage.
        // Note: this test is a lot more boring now that we're no longer trying to come up with the minimal groupings
        const nonAudioRun1Html =
            '<div id="nonAudio1"></div><div id="nonAudio2"></div>';
        const audioRun1Html =
            '<p><span id="audio1" class="audio-sentence">Paragraph 1A.</span> <span id="audio2" class="audio-sentence">Paragraph 1B.</span><br></p><p><span id="audio3" class="audio-sentence">Paragraph 2A.</span> <span id="audio4" class="audio-sentence">Paragraph 2B.</span><br></p>';
        const nonAudioRun2Html =
            '<div id="nonAudio3"></div><div id="nonAudio4"></div>';
        const audioRun2Html =
            '<p><span id="audio5" class="audio-sentence">Paragraph 3A.</span> <span id="audio6" class="audio-sentence">Paragraph 3B.</span><br></p><p><span id="audio7" class="audio-sentence">Paragraph 4A.</span> <span id="audio8" class="audio-sentence">Paragraph 4B.</span><br></p>';
        const originalHtml =
            '<div class="bloom-editable" data-audiorecordingmode="Sentence">' +
            nonAudioRun1Html +
            audioRun1Html +
            nonAudioRun2Html +
            audioRun2Html +
            "</div>";
        let div = $(originalHtml);
        let recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        recording.makeAudioSentenceElements(div);

        let parent = $("<div>")
            .append(div)
            .clone();
        const expectedAudioRun1Html =
            "<p>Paragraph 1A. Paragraph 1B.<br></p><p>Paragraph 2A. Paragraph 2B.<br></p>";
        const expectedAudioRun2Html =
            "<p>Paragraph 3A. Paragraph 3B.<br></p><p>Paragraph 4A. Paragraph 4B.<br></p>";
        const expectedHtml =
            '<div class="bloom-editable audio-sentence" data-audiorecordingmode="TextBox">' +
            nonAudioRun1Html +
            expectedAudioRun1Html +
            nonAudioRun2Html +
            expectedAudioRun2Html +
            "</div>";
        expect(StripAllGuidIds(StripEmptyClasses(parent.html()))).toBe(
            expectedHtml,
            "Parent HTML"
        );
    });
});

function StripEmptyClasses(html) {
    // Because after running removeAttr, it leaves class="" in the HTML
    return html.replace(/ class=""/g, "");
}

function StripAllGuidIds(html) {
    // Note: add the "g" (global) flag to the end of the search setting if you want to replace all instead.
    return html
        .replace(
            / id="i?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"/g,
            ""
        )
        .replace(/ id=""/g, "");
}

function StripAudioCurrent(html) {
    return html
        .replace(/ ui-audioCurrent/g, "")
        .replace(/ class="ui-audioCurrent"/g, "");
}
