import AudioRecording, {
    AudioRecordingMode,
    AudioTextFragment
} from "./audioRecording";

describe("audio recording tests", () => {
    describe(", Next()", () => {
        it("Record=Sentence, last sentence returns disabled for Next button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable' data-audioRecordingMode='Sentence'><p><span id='id1' class='audio-sentence ui-audioCurrent'>Sentence 1.</span></p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;

            const observed = recording.getNextAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=Sentence, last sentence returns disabled for Next button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'><p><span id='id1' class='audio-sentence'>Sentence 1.</span></p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=TextBox, last sentence returns disabled for Next button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'>p>Sentence 1.</p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed).toBeFalsy();
        });

        it("SS -> SS, returns next box's first sentence", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable' data-audioRecordingMode='Sentence'><p><span id='sentence1' class='audio-sentence ui-audioCurrent'>Sentence 1.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable' data-audioRecordingMode='Sentence'><p><span id='sentence2' class='audio-sentence'>Sentence 2.</span><span id='sentence3' class='audio-sentence'>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(`<div id='page1'>${box1Html}${box2Html}</div>`);

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBe("sentence2");
        });

        it("TS -> TT, returns next box", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'><p><span id='sentence1' class='audio-sentence'>Sentence 1.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable audio-sentence' data-audioRecordingMode='TextBox'><p><span id='sentence2' class='audio-sentence'>Sentence 2.</span><span id='sentence3' class=''>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(`<div id='page1'>${box1Html}${box2Html}</div>`);

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBe("box2");
        });

        it("TT -> TT, returns next box", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div id='box1' class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable audio-sentence' data-audioRecordingMode='TextBox'>p>Sentence 2.</p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBe("box2");
        });

        it("Next() skips over empty box, TT -> TT -> TT", () => {
            const boxTemplate = (index: number, extraClasses: string) => {
                return `<div id="box${index}" class="bloom-editable audio-sentence${extraClasses}" data-audioRecordingMode="TextBox">p>Sentence ${index}.</p></div>`;
            };
            const box1Html = boxTemplate(1, " ui-audioCurrent");
            const box2Html =
                '<div id="box2" class="bloom-editable"><p></p></div>';
            const box3Html = boxTemplate(3, "");

            SetupIFrameFromHtml(
                `<div id="page1">${box1Html}${box2Html}${box3Html}</div>`
            );

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getNextAudioElement();

            expect(observed!.id).toBeTruthy(); // Null is definitely the wrong answer here
            expect(observed!.id).toBe("box3");
        });
    });

    describe(", Prev()", () => {
        it("Record=Sentence, first sentence returns disabled for Back button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable' data-audioRecordingMode='Sentence'><p><span id='id1' class='audio-sentence ui-audioCurrent'>Sentence 1.</span></p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;

            const observed = recording.getPreviousAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=Sentence, first sentence returns disabled for Back button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'><p><span id='id1' class='audio-sentence'>Sentence 1.</span></p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed).toBeFalsy();
        });

        it("Record=TextBox/Play=TextBox, first sentence returns disabled for Back button", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'>p>Sentence 1.</p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed).toBeFalsy();
        });

        it("SS <- SS, returns previous box's last sentence", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable' data-audioRecordingMode='Sentence'><p><span id='sentence1' class='audio-sentence'>Sentence 1.</span><span id='sentence2' class='audio-sentence'>Sentence 2.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable' data-audioRecordingMode='Sentence'><p><span id='sentence3' class='audio-sentence ui-audioCurrent'>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(`<div id='page1'>${box1Html}${box2Html}</div>`);

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBe("sentence2");
        });

        it("TS <- TT, returns previous box", () => {
            const box1Html =
                "<div id='box1' class='bloom-editable audio-sentence' data-audioRecordingMode='TextBox'><p><span id='sentence1' class='audio-sentence'>Sentence 1.</span><span id='sentence2' class='audio-sentence'>Sentence 2.</span></p></div>";
            const box2Html =
                "<div id='box2' class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'><p><span id='sentence3' class=''>Sentence 3.</span></p></div>";
            SetupIFrameFromHtml(`<div id='page1'>${box1Html}${box2Html}</div>`);

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBe("box1");
        });

        it("TT <- TT, returns previous box", () => {
            SetupIFrameFromHtml(
                "<div id='page1'><div id='box1' class='bloom-editable audio-sentence' data-audioRecordingMode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'>p>Sentence 2.</p></div></div>"
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBe("box1");
        });

        it("Prev() skips over empty box, TT <- TT <- TT", () => {
            const boxTemplate = (index: number, extraClasses: string) => {
                return `<div id="box${index}" class="bloom-editable audio-sentence${extraClasses}" data-audioRecordingMode="TextBox">p>Sentence ${index}.</p></div>`;
            };
            const box1Html = boxTemplate(1, "");
            const box2Html =
                '<div id="box2" class="bloom-editable"><p></p></div>';
            const box3Html = boxTemplate(3, " ui-audioCurrent");

            SetupIFrameFromHtml(
                `<div id="page1">${box1Html}${box2Html}${box3Html}</div>`
            );

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;

            const observed = recording.getPreviousAudioElement();

            expect(observed!.id).toBeTruthy(); // Null is definitely the wrong answer here
            expect(observed!.id).toBe("box1");
        });
    });

    describe(", PlayingMultipleAudio()", () => {
        it("returns true while in listen to whole page with multiple text boxes", () => {
            SetupTalkingBookUIElements();
            SetupIFrameFromHtml(
                "<div id='page1'><div id='box1' class='bloom-editable audio-sentence' data-audioRecordingMode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'>p>Sentence 2.</p></div></div>"
            );
            const recording = new AudioRecording();
            recording.listen();
            expect(recording.playingAudio()).toBe(true);
        });

        it("returns true while in listen to whole page with only one box", () => {
            SetupTalkingBookUIElements();
            SetupIFrameFromHtml(
                "<div id='page1'><div id='box1' class='bloom-editable audio-sentence' data-audioRecordingMode='TextBox'>p>Sentence 1.</p></div></div>"
            );
            const recording = new AudioRecording();
            recording.listen();
            expect(recording.playingAudio()).toBe(true);
        });

        it("returns false while preloading", () => {
            SetupTalkingBookUIElements();
            SetupIFrameFromHtml(
                "<div id='page1'><div id='box1' class='bloom-editable audio-sentence' data-audioRecordingMode='TextBox'>p>Sentence 1.</p></div><div id='box2' class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'>p>Sentence 2.</p></div></div>"
            );

            const recording = new AudioRecording();
            expect(recording.playingAudio()).toBe(false);

            const player: Element = document.getElementById("player")!;
            player.setAttribute("preload", "auto");
            const nonExistentFilePath = "`[];'/.,<>?;.mp3";
            player.setAttribute("src", nonExistentFilePath);

            expect(recording.playingAudio()).toBe(false);
        });
    });

    describe(", MakeAudioSentenceElements()", () => {
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
            expect(div.text()).toBe(
                "This is a new sentence. This is a sentence."
            );
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

        it("does not create nested spans", () => {
            // This scenario could happen when trying to perform soft-split again on a text box that has already been soft-split previously.
            const p = $(
                '<div class="bloom-editable" data-audiorecordingmode="TextBox" class="audio-sentence"><p><span id="a" class="bloom-highlightSegment">One.</span> <span id="b" class="bloom-highlightSegment">Two.</span> <span id="c" class="bloom-highlightSegment">Three.</span></p></div>'
            );
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;
            recording.makeAudioSentenceElements(p);
            const spans = p.find("span");
            // Should have removed the outer span and left the two inner ones and added a third one.
            expect(spans.length).toBe(3); // If regresses, it would probably show twice as many (i.e. 6) instead of 3.
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

        it("handles hyperlinks", () => {
            const sentence1 = 'This is a <a href="www.google.com">link</a>.';
            const sentence2 = 'This is <a href="www.bing.com">another</a>.';
            const sentence3 = "Click them.";
            const div = $(`<div>${sentence1} ${sentence2} ${sentence3}</div>`);
            const recording = new AudioRecording();
            recording.makeAudioSentenceElements(div);
            const spans = div.find("span");
            expect(spans.length).toBe(3);
            expect(spans[0].innerHTML).toBe(sentence1); // Make sure the anchor is not lost in the HTML
            expect(spans[1].innerHTML).toBe(sentence2); // Make sure the anchor is not lost in the HTML
            expect(spans[2].innerHTML).toBe(sentence3);
            expect(div.text()).toBe(
                "This is a link. This is another. Click them."
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

            expect($(divs[0]).is(".audio-sentence")).toBe(
                true,
                "textbox's class"
            );
            expect($(divs[0]).attr("id")).not.toBe(undefined, "textbox's id");
            expect($(divs[0]).attr("id").length).toBeGreaterThan(
                31,
                "textbox's id"
            ); // GUID without hyphens is 32 chars longs
            expect($(divs[0]).attr("id").length).toBeLessThan(
                38,
                "textbox's id"
            ); // GUID with hyphens adds 4 chars. And we sometimes insert a 1-char prefix, adding up to 37.

            expect($(divs[1]).is(".audio-sentence")).toBe(
                false,
                "formatButton's class"
            );
            expect($(divs[1]).attr("id")).toBe(
                "formatButton",
                "formatButton's id"
            );
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

            expect($(divs[0]).is(".audio-sentence")).toBe(
                true,
                "textbox's class"
            );
            expect(divs[0].id.length).toBeGreaterThan(
                31,
                "textbox's id length"
            );
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

            expect($(divs[0]).is(".audio-sentence")).toBe(
                true,
                "textbox's class"
            );
            expect($(divs[0]).attr("id")).not.toBe(undefined), "textbox's id";
            expect(divs[0].id.length).toBeGreaterThan(
                31,
                "textbox's id length"
            );
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
            recording.audioRecordingMode = AudioRecordingMode.Sentence; // Should be the new mode
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
            expect($(divs[0]).is(".audio-sentence")).toBe(
                true,
                "textbox's class"
            );
            expect($(divs[0]).attr("id").length).toBeGreaterThan(31),
                "textbox's id"; // GUID without hyphens is 32 chars longs
            expect($(divs[0]).attr("id").length).toBeLessThan(38),
                "textbox's id"; // GUID with hyphens adds 4 chars. And we sometimes insert a 1-char prefix, adding up to 37.
            expect($(divs[1]).attr("id")).toBe("formatButton"),
                "formatButton's id";

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
                StripAllGuidIds(
                    StripEmptyClasses(StripAudioCurrent(originalHtml))
                ),
                "Swap back to original"
            );
        });

        it("converts by-text-box into by-sentence (bloom-editable includes format button)", () => {
            SetupTalkingBookUIElements();

            // This tests real input from Bloom that has been marked up in by-text-box mode (e.g., clicking the checkbox from not-by-sentence into by-sentence)
            const textBoxDivHtml =
                '<div id="ee41e518-7855-472a-b8ce-a0c6caa68341" aria-label="false" role="textbox" spellcheck="true" tabindex="0" style="min-height: 24px;" class="bloom-editable cke_editable cke_editable_inline cke_contents_ltr bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style audio-sentence" data-languagetipcontent="English" data-audiorecordingmode="TextBox" lang="en" contenteditable="true">';
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const originalHtml = `<div id="numberedPage">${textBoxDivHtml}<p>Sentence 1. Sentence 2. Sentence 3.<br></p><p>Paragraph 2.<br></p>${formatButtonHtml}</div></div>`;
            SetupIFrameFromHtml(originalHtml);

            const pageFrame = parent.window.document.getElementById("page");
            const div = $(
                (<HTMLIFrameElement>pageFrame).contentDocument!.getElementById(
                    "numberedPage"
                )!
            );

            let recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;
            recording.makeAudioSentenceElements(div);

            expect(div.text()).toBe(
                "Sentence 1. Sentence 2. Sentence 3.Paragraph 2.",
                "div text"
            );

            const spans = div.find("span");
            expect(spans.length).toBe(4, "number of spans");
            spans.each((index, span) => {
                expect(span.id.length).toBeGreaterThan(
                    31,
                    "span " + index + " id"
                );
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

            let parentDiv = $("<div>")
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
            expect(StripAllGuidIds(StripEmptyClasses(parentDiv.html()))).toBe(
                '<div id="numberedPage">' +
                    expectedTextBoxDivHtml +
                    expectedTextBoxInnerHtml +
                    formatButtonHtml +
                    "</div></div>",
                "parent.html"
            );

            expect(parentDiv.html().indexOf('id=""')).toBe(
                -1,
                "IDs should not be set to empty string. (Can easily cause duplicate ID validation errors and prevent saving)"
            );

            // Test that you can switch back and recover more-or-less the original
            recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;
            recording.makeAudioSentenceElements(div);
            parentDiv = $("<div>")
                .append(div)
                .clone();
            expect(StripAllGuidIds(StripEmptyClasses(parentDiv.html()))).toBe(
                StripAllGuidIds(
                    StripEmptyClasses(StripAudioCurrent(originalHtml))
                ),
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
            const div = $(originalHtml);
            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;
            recording.makeAudioSentenceElements(div);

            const parent = $("<div>")
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

    describe(", updateRecordingMode()", () => {
        beforeEach(() => {
            SetupTalkingBookUIElements();
        });

        it("URM(): converts from RecordSentence/PlaySentence to RecordTextBox/PlaySentence", () => {
            const textBoxDivHtml =
                '<div id="textBox1" class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style cke_editable cke_editable_inline cke_contents_ltr" data-languagetipcontent="English" data-audiorecordingmode="Sentence" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" lang="en" contenteditable="true">';
            const paragraphsMarkedBySentenceHtml =
                '<p><span id="sentence1" class="audio-sentence ui-audioCurrent">Sentence 1.</span> <span id="sentence2" class="audio-sentence">Sentence 2.</span><br></p>';
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const originalHtml = `<div id="numberedPage">${textBoxDivHtml}${paragraphsMarkedBySentenceHtml}${formatButtonHtml}</div>`;
            SetupIFrameFromHtml(originalHtml);

            const player = <HTMLMediaElement>document.getElementById("player")!;
            player.src = "sentence1.mp3";

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence; // Should be the old state, updateRecordingMode() will flip the state
            recording.updateRecordingMode();

            // Tests of the state
            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.TextBox
            );
            expect(
                document
                    .getElementById("audio-split-wrapper")!
                    .classList.contains("hide-countable")
            ).toBe(false);

            const pageFrame = parent.window.document.getElementById("page");
            const myDoc = (<HTMLIFrameElement>pageFrame).contentDocument!;
            const textBox1 = myDoc.getElementById("textBox1")!;
            expect(textBox1.getAttribute("data-audioRecordingMode")).toBe(
                AudioRecordingMode.TextBox.toString()
            );
            expect(textBox1.classList.contains("ui-audioCurrent")).toBe(true);
            expect(textBox1.classList.contains("audio-sentence")).toBe(false);

            const sentence1 = myDoc.getElementById("sentence1")!;
            expect(sentence1.classList.contains("ui-audioCurrent")).toBe(false);
            expect(sentence1.classList.contains("audio-sentence")).toBe(true);

            expect(StripPlayerSrcNoCacheSuffix(player.src)).toBe(
                "http://localhost:9876/bloom/api/audio/wavFile?id=audio/sentence1.wav"
            );

            const parentDiv = myDoc.getElementById("numberedPage")!;
            const divs = parentDiv.getElementsByTagName("DIV");
            expect(divs.length).toBe(2, "number of divs");
            expect(divs.item(1)!.id).toBe("formatButton", "formatButton's id");

            expect(
                $(parentDiv)
                    .find(".ui-audioCurrent")
                    .text()
            ).toBe("Sentence 1. Sentence 2.", "Current text box text");
        });

        it("URM(): converts from RecordTextBox/PlaySentence to RecordSentence/PlaySentence", () => {
            const textBoxDivHtml =
                '<div id="textBox1" class="bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style cke_editable cke_editable_inline cke_contents_ltr ui-audioCurrent" data-languagetipcontent="English" data-audiorecordingmode="TextBox" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" lang="en" contenteditable="true">';
            const paragraphsMarkedBySentenceHtml =
                '<p><span id="sentence1" class="audio-sentence">Sentence 1.</span> <span id="sentence2" class="audio-sentence">Sentence 2.</span><br></p>';
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const originalHtml = `<div id="numberedPage">${textBoxDivHtml}${paragraphsMarkedBySentenceHtml}${formatButtonHtml}</div>`;
            SetupIFrameFromHtml(originalHtml);

            const player = <HTMLMediaElement>document.getElementById("player")!;
            player.src = "sentence1.mp3";

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox; // Should be the old state, updateRecordingMode() will flip the state
            recording.updateRecordingMode();

            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.Sentence
            );
            expect(
                document
                    .getElementById("audio-split-wrapper")!
                    .classList.contains("hide-countable")
            ).toBe(true);

            const pageFrame = parent.window.document.getElementById("page");
            const myDoc = (<HTMLIFrameElement>pageFrame).contentDocument!;

            const textBox1 = myDoc.getElementById("textBox1")!;
            expect(textBox1.getAttribute("data-audioRecordingMode")).toBe(
                AudioRecordingMode.Sentence.toString()
            );
            expect(textBox1.classList.contains("ui-audioCurrent")).toBe(false);
            expect(textBox1.classList.contains("audio-sentence")).toBe(false);

            const sentence1 = myDoc.getElementById("sentence1")!;
            expect(sentence1.classList.contains("ui-audioCurrent")).toBe(true);
            expect(sentence1.classList.contains("audio-sentence")).toBe(true);

            expect(StripPlayerSrcNoCacheSuffix(player.src)).toBe(
                "http://localhost:9876/bloom/api/audio/wavFile?id=audio/sentence1.wav"
            );

            const parentDiv = myDoc.getElementById("numberedPage")!;
            const divs = parentDiv.getElementsByTagName("DIV");
            expect(divs.length).toBe(2, "number of divs");
            expect(divs.item(1)!.id).toBe("formatButton", "formatButton's id");

            expect(
                $(parentDiv)
                    .find(".ui-audioCurrent")
                    .text()
            ).toBe("Sentence 1.", "Current sentence text");
        });

        it("URM(): converts from RecordTextBox/PlayTextBox to RecordSentence/PlaySentence", () => {
            const textBoxDivHtml =
                '<div id="textBox1" class="audio-sentence ui-audioCurrent bloom-editable bloom-content1 bloom-contentNational1 bloom-visibility-code-on normal-style cke_editable cke_editable_inline cke_contents_ltr ui-audioCurrent" data-languagetipcontent="English" data-audiorecordingmode="TextBox" style="min-height: 24px;" tabindex="0" spellcheck="true" role="textbox" aria-label="false" lang="en" contenteditable="true">';
            const paragraphHtml = "<p>Sentence 1. Sentence 2.<br></p>";
            const formatButtonHtml =
                '<div id="formatButton" class="bloom-ui" style="bottom: 0px;" contenteditable="false"><img data-cke-saved-src="/bloom/bookEdit/img/cogGrey.svg" contenteditable="false"></div>';
            const originalHtml = `<div id="numberedPage">${textBoxDivHtml}${paragraphHtml}${formatButtonHtml}</div>`;
            SetupIFrameFromHtml(originalHtml);
            SetupTalkingBookUIElements();

            const player = <HTMLMediaElement>document.getElementById("player")!;
            player.src = "textBox1.mp3";

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox; // Should be the old state, updateRecordingMode() will flip the state
            recording.updateRecordingMode();

            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.Sentence
            );
            expect(
                document
                    .getElementById("audio-split-wrapper")!
                    .classList.contains("hide-countable")
            ).toBe(true);

            const pageFrame = parent.window.document.getElementById("page");
            const myDoc = (<HTMLIFrameElement>pageFrame).contentDocument!;

            const textBox1 = myDoc.getElementById("textBox1")!;
            expect(textBox1.getAttribute("data-audioRecordingMode")).toBe(
                AudioRecordingMode.Sentence.toString()
            );
            expect(textBox1.classList.contains("ui-audioCurrent")).toBe(false);
            expect(textBox1.classList.contains("audio-sentence")).toBe(false);

            const sentences = textBox1.getElementsByTagName("SPAN");
            expect(sentences.length).toBe(2);
            const sentence1 = sentences.item(0)!;
            expect(sentence1.classList.contains("ui-audioCurrent")).toBe(true);
            expect(sentence1.classList.contains("audio-sentence")).toBe(true);
            expect(sentence1.id.length).toBeGreaterThan(31);

            expect(StripPlayerSrcNoCacheSuffix(player.src)).toBe(
                `http://localhost:9876/bloom/api/audio/wavFile?id=audio/${
                    sentence1.id
                }.wav`
            );

            const parentDiv = myDoc.getElementById("numberedPage")!;
            const divs = parentDiv.getElementsByTagName("DIV");
            expect(divs.length).toBe(2, "number of divs");
            expect(divs.item(1)!.id).toBe("formatButton", "formatButton's id");

            expect(
                $(parentDiv)
                    .find(".ui-audioCurrent")
                    .text()
            ).toBe("Sentence 1.", "Current sentence text");
        });
    });

    describe("initializeAudioRecordingMode()", () => {
        beforeEach(() => {
            SetupTalkingBookUIElements();
        });

        it("initializeAudioRecordingMode gets mode from current div if available (synchronous) (Text Box)", () => {
            SetupIFrameFromHtml(
                "<div class='bloom-editable' lang='en' data-audioRecordingMode='Sentence'>Sentence 1. Sentence 2.</div><div class='bloom-editable ui-audioCurrent' lang='es' data-audioRecordingMode='TextBox'>Paragraph 2.</div>"
            );

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;
            recording.recordingModeInput = document.createElement("input");

            // Just to make sure that the code under test can read the current div at all.
            const currentTextBox = recording.getCurrentTextBox();
            expect(currentTextBox).toBeTruthy(
                "Could not find currentDiv. Possible test setup problem?"
            );

            // Even though the function is named async, but most cases will actually happen synchronously.
            // We'll only bother testing the synchronous cases.
            recording.initializeAudioRecordingMode();

            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.TextBox
            );
            expect(recording.recordingModeInput.checked).toBe(
                true,
                "Checkbox state"
            );
        });

        it("initializeAudioRecordingMode gets mode from current div if available (synchronous) (Sentence)", () => {
            SetupIFrameFromHtml(
                "<div class='bloom-editable' lang='en' data-audioRecordingMode='TextBox'>Paragraph 1.</div><div class='bloom-editable ui-audioCurrent' lang='es' data-audioRecordingMode='Sentence'>Paragraph 2.</div>"
            );

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;
            recording.recordingModeInput = document.createElement("input");

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(currentDiv).toBeTruthy(
                "Could not find currentDiv. Possible test setup problem?"
            );

            // Even though the function is named async, but most cases will actually happen synchronously.
            // We'll only bother testing the synchronous cases.
            recording.initializeAudioRecordingMode();

            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.Sentence
            );
            expect(recording.recordingModeInput.checked).toBe(
                false,
                "Checkbox state"
            );
        });

        it("initializeAudioRecordingMode gets mode from other divs on page as fallback (synchronous) (TextBox)", () => {
            SetupIFrameFromHtml(
                "<div class='audio-sentence bloom-editable' lang='en' data-audioRecordingMode='TextBox'>Paragraph 1</div><div class='bloom-editable' lang='es'><span id='id2' class='audio-sentence ui-audioCurrent'>Paragraph 2.</span></div>"
            );

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.Sentence;
            recording.recordingModeInput = document.createElement("input");

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(currentDiv).toBeTruthy(
                "Could not find currentDiv. Possible test setup problem?"
            );

            // Even though the function is named async, but most cases will actually happen synchronously.
            // We'll only bother testing the synchronous cases.
            recording.initializeAudioRecordingMode();

            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.TextBox
            );
            expect(recording.recordingModeInput.checked).toBe(
                true,
                "Checkbox state"
            );
        });

        it("initializeAudioRecordingMode gets mode from other divs on page as fallback (synchronous) (Sentence)", () => {
            // The 2nd div doesn't really look well-formed because we're trying to get the test to exercise some fallback cases
            // The first div doesn't look well-formed either but I want the test to exercise that it is getting it from the data-audioRecordingMode attribute not from any of the div's innerHTML markup.
            SetupIFrameFromHtml(
                "<div class='bloom-editable' lang='en' data-audioRecordingMode='Sentence'>Paragraph 1</div><div class='bloom-editable audio-sentence ui-audioCurrent' lang='es'>Paragraph 2.</div>"
            );

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;
            recording.recordingModeInput = document.createElement("input");

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(currentDiv).toBeTruthy(
                "Could not find currentDiv. Possible test setup problem?"
            );

            // Even though the function is named async, but most cases will actually happen synchronously.
            // We'll only bother testing the synchronous cases.
            recording.initializeAudioRecordingMode();

            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.Sentence
            );
            expect(recording.recordingModeInput.checked).toBe(
                false,
                "Checkbox state"
            );
        });

        it("initializeAudioRecordingMode identifies 4.3 audio-sentences (synchronous)", () => {
            SetupIFrameFromHtml(
                "<div class='bloom-editable' lang='en'><span id='id1' class='audio-sentence'>Sentence 1.</span> <span id='id2' class='audio-sentence'>Sentence 2.</span></div><div class='bloom-editable ui-audioCurrent' lang='es'>Paragraph 2.</div>"
            );

            const recording = new AudioRecording();
            recording.audioRecordingMode = AudioRecordingMode.TextBox;
            recording.recordingModeInput = document.createElement("input");

            // Just to make sure that the code under test can read the current div at all.
            const currentDiv = recording.getCurrentTextBox();
            expect(currentDiv).toBeTruthy(
                "Could not find currentDiv. Possible test setup problem?"
            );

            // Even though the function is named async, but most cases will actually happen synchronously.
            // We'll only bother testing the synchronous cases.
            recording.initializeAudioRecordingMode();

            expect(recording.audioRecordingMode).toBe(
                AudioRecordingMode.Sentence
            );
            expect(recording.recordingModeInput.checked).toBe(
                false,
                "Checkbox state"
            );
        });
    });

    it("isRecordableDiv works", () => {
        const recording = new AudioRecording();

        let elem = document.createElement("div");
        document.body.appendChild(elem);
        elem.classList.add("bloom-editable");
        expect(recording.isRecordableDiv(elem)).toBe(false, "Case 1A: no text");

        elem.appendChild(document.createTextNode("Hello world"));
        expect(recording.isRecordableDiv(elem)).toBe(true, "Case 1B: text");

        const parent = document.createElement("div");
        document.body.appendChild(parent);
        parent.classList.add("bloom-noAudio");
        parent.appendChild(elem);
        expect(recording.isRecordableDiv(elem)).toBe(
            false,
            "Case 2: parent is no-audio"
        );

        elem = document.createElement("div");
        document.body.appendChild(elem);
        elem.appendChild(document.createTextNode("Layout: Basic Picture"));
        expect(recording.isRecordableDiv(elem)).toBe(
            false,
            "Case 3: not recordable (no bloom-editable class)"
        );

        elem = document.createElement("div");
        document.body.appendChild(elem);
        elem.style.display = "none";
        elem.classList.add("bloom-editable");
        elem.appendChild(document.createTextNode("Hello world"));
        expect(recording.isRecordableDiv(elem)).toBe(
            false,
            "Case 4: Element not visible"
        );
    });

    it("getCurrentText works", () => {
        SetupIFrameFromHtml("<div class='ui-audioCurrent'>Hello world</div>");

        const recording = new AudioRecording();
        expect(recording.getCurrentHighlight()).toBeTruthy();
        const returnedText = recording.getCurrentText();

        expect(returnedText).toBe("Hello world");
    });

    it("getAutoSegmentLanguageCode works", () => {
        SetupIFrameFromHtml(
            "<div class='ui-audioCurrent' lang='es'>Hello world</div>"
        );

        const recording = new AudioRecording();
        const returnedText = recording.getAutoSegmentLanguageCode();

        expect(returnedText).toBe("es");
    });

    it("extractFragmentsForAudioSegmentation works", () => {
        SetupIFrameFromHtml(
            "<div class='ui-audioCurrent' lang='es'>Sentence 1. Sentence 2.</div>"
        );

        const recording = new AudioRecording();
        const returnedFragmentIds: AudioTextFragment[] = recording.extractFragmentsAndSetSpanIdsForAudioSegmentation();

        expect(returnedFragmentIds.length).toBe(2);
        for (let i = 0; i < returnedFragmentIds.length; ++i) {
            expect(returnedFragmentIds[i].fragmentText).toBe(
                `Sentence ${i + 1}.`
            );
            expect(returnedFragmentIds[i].id).toBeTruthy();
        }
    });

    it("extractFragmentsForAudioSegmentation handles duplicate sentences separately", () => {
        SetupIFrameFromHtml(
            "<div class='ui-audioCurrent' lang='en'>What color is the sky? Blue. What color is the ocean? Blue. Hello. Hello.</p></div>"
        );

        const recording = new AudioRecording();
        recording.audioRecordingMode = AudioRecordingMode.TextBox;
        const returnedFragmentIds: AudioTextFragment[] = recording.extractFragmentsAndSetSpanIdsForAudioSegmentation();

        expect(
            Object.keys(recording.__testonly__sentenceToIdListMap).length
        ).toBe(4); // the number of distinct sentences

        // Check that the stored map actually maps back to the correct ID (even if there are duplicate sentences)
        expect(returnedFragmentIds.length).toBe(6); // the number of sentences
        for (let i = 0; i < returnedFragmentIds.length; ++i) {
            const fragmentText = returnedFragmentIds[i].fragmentText;
            const expectedId = returnedFragmentIds[i].id;
            const idList =
                recording.__testonly__sentenceToIdListMap[fragmentText];
            expect(idList[0]).toBe(
                expectedId,
                `Fragment ${i} (${fragmentText})`
            );

            idList.shift(); // The existing one is all done, move on to next one.
        }
    });

    it("normalizeText() works", () => {
        function testAllFormsMatch(
            rawForm: string, // Directly after typing text, immediately upon clicking AutoSegment, before anything is modified
            processingForm: string, // After clicking AutoSegment and the response is being proc, during MakeAudioSentenceElementsLeaf
            savedForm // After saving the page.
        ) {
            expect(AudioRecording.normalizeText(rawForm)).toBe(
                AudioRecording.normalizeText(processingForm)
            );
            expect(AudioRecording.normalizeText(rawForm)).toBe(
                AudioRecording.normalizeText(savedForm)
            );
            // 3rd check is unnecessary because transitive property
        }
        testAllFormsMatch(
            "John 3:16 (NIV)\n\n\n",
            "John 3:16 (NIV)<br />",
            "John 3:16 (NIV)"
        );
        testAllFormsMatch(
            "\u00a0\u00a0\u00a0 In the beginning...",
            "\u00a0\u00a0\u00a0 In the beginning...",
            "&nbsp;&nbsp;&nbsp; In the beginning..."
        );
        // Test what happens when inserting <em> (italics) in the HTML version.  (This input corresponds to the text() version).
        testAllFormsMatch(
            "Title: The Cat in the Hat .",
            "Title:  The Cat in the Hat  .",
            "Title:  The Cat in the Hat  ."
        );
    });

    it("isInSoftSplitMode() works on positive examples", () => {
        SetupIFrameFromHtml(
            "<div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox' data-audioRecordingEndTimes='1.0 2.0 3.0'><p>One. Two. Three.</p></div>"
        );

        const recording = new AudioRecording();
        const result = recording.isInSoftSplitMode();

        expect(result).toBe(true);
    });

    it("isInSoftSplitMode() works on negative examples", () => {
        const div1 =
            "<div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'><p>One. Two. Three.</p></div>";
        const div2 =
            "<div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='TextBox'><p><span id='s1' class='audioSentence'>One.</span> <span id='s2' class='audioSentence'>Two.</span> <span id='s3' class='audioSentence'>Three.</span></p></div>";
        const div3 =
            "<div class='bloom-editable audio-sentence ui-audioCurrent' data-audioRecordingMode='Sentence'><p><span id='s1' class='audioSentence'>One.</span> <span id='s2' class='audioSentence'>Two.</span> <span id='s3' class='audioSentence'>Three.</span></p></div>";
        SetupIFrameFromHtml(div1 + div2 + div3);

        const recording = new AudioRecording();
        const result = recording.isInSoftSplitMode();

        expect(result).toBe(false);
    });
});

function StripEmptyClasses(html) {
    // Because after running removeAttr, it leaves class="" in the HTML
    return html.replace(/ class=""/g, "");
}

function StripAllIds(html) {
    // Note: add the "g" (global) flag to the end of the search setting if you want to replace all instead.
    return html.replace(/ id="[^"]*"/g, "").replace(/ id=""/g, "");
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

// bodyContentHtml should not contain HTML or Body tags. It should be the innerHtml of the body
// It might look something like this: <div class='ui-audioCurrent'>Hello world</div>
function SetupIFrameFromHtml(
    bodyContentHtml,
    id = "page",
    shouldClearFirst = true
) {
    if (shouldClearFirst) {
        // Wipe out their contents first
        CleanupIframe(id);
    }

    const dummyDiv = parent.window.document.body.appendChild(
        document.createElement("div")
    );
    dummyDiv.insertAdjacentHTML(
        "afterend",
        `<iframe id='${id}'><html><body>${bodyContentHtml}</body></html></iframe>`
    );
    dummyDiv.remove();

    // Dunno how contentWindow.document.body is supposed to get initialized, but inserting stuff into parent.window.document.body does not do it.
    // So insert the same thing again here.
    //
    // (audioRecording references both page.window.document.body and parent.contentWindow.document.body so need to setup both)
    const pageElement = <HTMLIFrameElement>(
        parent.window.document.getElementById(id)
    );
    if (pageElement && pageElement.contentWindow) {
        const dummyDiv2 = pageElement.contentWindow.document.body.appendChild(
            document.createElement("div")
        );
        dummyDiv2.insertAdjacentHTML("afterend", bodyContentHtml);
        dummyDiv2.remove();
    }
}

function CleanupIframe(id = "page") {
    const elem = <HTMLIFrameElement>parent.window.document.getElementById(id);
    if (elem) {
        if (elem.contentWindow) {
            const elem2 = <HTMLIFrameElement>(
                elem.contentWindow.document.getElementById(id)
            );
            if (elem2) {
                elem2.remove();
            }
        }

        elem.remove();
    }
}

// Just sets up some dummy elements so that they're non-null.
function SetupTalkingBookUIElements() {
    document.body.appendChild(document.createElement("div")); // Ensures there is always an element.

    const html =
        '<button id="audio-record" /><button id="audio-play" /><div id="audio-split-wrapper"><button id="audio-split"></div><button id="audio-next" /><button id="audio-prev" /><button id="audio-clear" /><input id="audio-recordingModeControl" /><input id="audio-playbackOrderControl" /><audio id="player" />';
    document.body.firstElementChild!.insertAdjacentHTML("afterend", html);
}

function StripPlayerSrcNoCacheSuffix(url: string): string {
    const index = url.lastIndexOf("&");
    if (index < 0) {
        return url;
    }

    return url.substring(0, index);
}
