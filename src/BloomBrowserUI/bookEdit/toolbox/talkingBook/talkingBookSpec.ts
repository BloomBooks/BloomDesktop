import TalkingBookTool from "./talkingBook";
import {
    theOneAudioRecorder,
    initializeTalkingBookToolAsync,
    AudioRecordingMode,
    AudioMode
} from "./audioRecording";
import {
    SetupTalkingBookUIElements,
    SetupIFrameAsync,
    SetupIFrameFromHtml,
    getFrameElementById,
    StripPlayerSrcNoCacheSuffix
} from "./audioRecordingSpec";
import * as XRegExp from "xregexp"; // Not sure why, but import * as XRegExp works better. import XRegExp causes "xregexp_1.default is undefined" error
import { setSentenceEndingPunctuationForBloom } from "../readers/libSynphony/bloom_xregexp_categories";
import axios from "axios";

describe("talking book tests", () => {
    beforeAll(async (done: () => void) => {
        SetupTalkingBookUIElements();
        await SetupIFrameAsync();
        await initializeTalkingBookToolAsync();
        done();
    });

    describe("- updateMarkup()", () => {
        it("moves highlight after focus changes", async done => {
            // Setup Initial HTML
            const textBox1 =
                '<div class="bloom-editable" id="div1"><p><span id="1.1" class="audio-sentence ui-audioCurrent">1.1</span></p></div>';
            const textBox2 =
                '<div class="bloom-editable" id="div2"><p><span id="2.1" class="audio-sentence">2.1</span></p></div>';
            SetupIFrameFromHtml(`<div id='page1'>${textBox1}${textBox2}</div>`);

            // Setup talking book tool
            const tbTool = new TalkingBookTool();
            await tbTool.showTool();

            // Simulate a keypress on a different div
            const div2Element = theOneAudioRecorder
                .getPageDocBody()!
                .querySelector("#div2")! as HTMLElement;
            div2Element.tabIndex = -1; // focus() won't work if no tabindex.
            div2Element.focus();

            // System under test - TalkingBook.updateMarkup() is called after typing
            await tbTool.updateMarkup();

            // Verification
            const currentTextBox = theOneAudioRecorder.getCurrentTextBox()!;
            const currentId = currentTextBox.getAttribute("id");
            expect(currentId).toBe("div2");
            done();
        });
    });

    describe("- tests for sentence splitting", () => {
        // For these tests, we simulate text that was split with \u104A ("၊") as a separator,
        // but now the code has changed so that \u104A is no longer a separator. Only \u104B.
        // We ensure this is the case by setting the SentenceEndingPunctuation again for each test
        beforeEach(() => {
            XRegExp.addUnicodeData([
                {
                    name: "SEP",
                    alias: "Sentence_Ending_Punctuation",
                    bmp: "\u104b"
                }
            ]);
        });

        afterEach(() => {
            // Restore it back to normal.
            setSentenceEndingPunctuationForBloom();
        });

        function setAudioFilesDontExist() {
            // Mark that the recording doesn't exist.
            // FYI - spies only last for the scope of the "describe" or "it" block in which it was defined.
            spyOn(axios, "get").and.returnValue(
                Promise.reject(new Error("Fake 404 Error"))
            );
        }

        function setAudioFilesPresent() {
            // Mark that the recording exists.
            // FYI - spies only last for the scope of the "describe" or "it" block in which it was defined.
            spyOn(axios, "get").and.returnValue(Promise.resolve());
        }

        function setupPureSentenceModeHtml(): string {
            const div1Html =
                '<div class="bloom-editable" id="div1" data-audioRecordingMode="Sentence"><p><span id="1.1" class="audio-sentence ui-audioCurrent">Sentence 1.1၊</span> <span id="1.2" class="audio-sentence">Sentence 1.2</span></p></div>';
            const div2Html =
                '<div class="bloom-editable" id="div2" data-audioRecordingMode="Sentence"><p><span id="2.1" class="audio-sentence">Sentence 2.1၊</span> <span id="2.2" class="audio-sentence">Sentence 2.2</span></p></div>';
            return `${div1Html}${div2Html}`;
        }

        function setupPreTextBoxModeHtml(): string {
            const div1Html =
                '<div class="bloom-editable ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox"><p><span id="1.1" class="audio-sentence">Sentence 1.1၊</span> <span id="1.1" class="audio-sentence">Sentence 1.2</span></p></div>';
            const div2Html =
                '<div class="bloom-editable audio-sentence" id="div2" data-audiorecordingmode="TextBox"><p><span id="2.1" class="audio-sentence">Sentence 2.1၊</span> <span id="2.1" class="audio-sentence">Sentence 2.2</span></p></div>';
            return `${div1Html}${div2Html}`;
        }

        function setupPureTextBoxModeHtml(): string {
            const div1Html =
                '<div class="bloom-editable audio-sentence ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox"><p>Sentence 1.1၊ Sentence 1.2</p></div>';
            const div2Html =
                '<div class="bloom-editable audio-sentence" id="div2" data-audiorecordingmode="TextBox"><p>Sentence 2.1၊ Sentence 2.2</p></div>';
            return `${div1Html}${div2Html}`;
        }

        function setupTextBoxHardSplitHtml(): string {
            let html = "";
            for (let i = 1; i <= 2; ++i) {
                const divStartHtml = `<div class="bloom-editable${
                    i === 1 ? " ui-audioCurrent" : ""
                }" id="div${i}" data-audiorecordingmode="TextBox">`;
                const divInnerHtml = `<p><span id="${i}.1" class="audio-sentence">Sentence ${i}.1၊</span> <span id="${i}.2" class="audio-sentence">Sentence ${i}.2</span></p>`;
                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                html += divHtml;
            }
            return html;
        }

        function setupTextBoxSoftSplitHtml(): string {
            let html = "";
            for (let i = 1; i <= 2; ++i) {
                const divStartHtml = `<div class="bloom-editable audio-sentence${
                    i === 1 ? " ui-audioCurrent" : ""
                }" id="div${i}" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0">`;
                const divInnerHtml = `<p><span id="${i}.1" class="bloom-highlightSegment">Sentence ${i}.1၊</span> <span id="Sentence ${i}.2" class="bloom-highlightSegment">${i}.2</span></p>`;

                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                html += divHtml;
            }
            return html;
        }

        async function runSentenceSplittingTestsAsync(
            scenario: AudioMode,
            areRecordingsPresent: boolean
        ) {
            let pageInnerHtml: string = "";
            switch (scenario) {
                case AudioMode.PureSentence: {
                    pageInnerHtml = setupPureSentenceModeHtml();
                    break;
                }
                case AudioMode.PureTextBox: {
                    pageInnerHtml = setupPureTextBoxModeHtml();
                    break;
                }
                case AudioMode.HardSplitTextBox: {
                    pageInnerHtml = setupTextBoxHardSplitHtml();
                    break;
                }
                case AudioMode.SoftSplitTextBox: {
                    pageInnerHtml = setupTextBoxSoftSplitHtml();
                    break;
                }
                default: {
                    throw new Error("Unknown scenario: " + scenario);
                }
            }

            SetupIFrameFromHtml(`<div id="page1">${pageInnerHtml}</div>`);

            if (scenario === AudioMode.PureSentence) {
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.Sentence;
            } else {
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
            }

            const originalHtml = getFrameElementById("page", "page1")!
                .innerHTML;

            if (areRecordingsPresent) {
                setAudioFilesPresent();
            } else {
                setAudioFilesDontExist();
            }

            // System under test
            const tbTool = new TalkingBookTool();
            await tbTool.showTool();

            // Verification
            verifyHtmlStructure(scenario, areRecordingsPresent, originalHtml);
            verifyCurrentHighlight(scenario);
            verifyRecordButtonEnabled(); // Make sure we didn't accidentally disable all the toolbox buttons
        }

        function verifyHtmlStructure(
            scenario: AudioMode,
            areRecordingsPresent: boolean,
            originalDiv1Html: string
        ) {
            if (
                areRecordingsPresent ||
                scenario === AudioMode.SoftSplitTextBox
            ) {
                // NOTE: SoftSplit test doesn't have very intuitive results.
                // Even if recordings aren't present, it actually preserves the original splits, even though normally, no recordings present means we should update them.
                // Prior to this check-if-recordings-present feature, it was actually the case that upon Soft Split, it never updated the markup.
                // (That's because the playback mode was identified as text box, and in that case, it never actually runs makeAudioSentenceElementsLeaf())
                // I'm not sure if that was entirely intentional, but it does seem very problematic to risk increasing/decreasing the number of splits while an audio is aligned to it.
                // Now that we do check if recordings present...
                // If they're somehow missing, we COULD now update the markup, but it doesn't seem worth the time, complexity, or risk to add that functionality.
                // I guess it's more like if there's no recordings present, then we do whatever the old behavior was... which in this case, is to not do anything.

                // Verify unchanged.
                const currentHtml = getFrameElementById("page", "page1")!
                    .innerHTML;
                expect(currentHtml).toBe(originalDiv1Html);
            } else if (
                scenario === AudioMode.PureSentence ||
                scenario === AudioMode.HardSplitTextBox
            ) {
                for (let i = 1; i <= 2; ++i) {
                    const spans = getAudioSentenceSpans(`div${i}`);
                    const texts = spans.map(elem => {
                        return elem.innerText;
                    });
                    expect(texts).toEqual(
                        [`Sentence ${i}.1၊ Sentence ${i}.2`],
                        `Failure for div${i}`
                    );
                }
            } else if (scenario === AudioMode.PureTextBox) {
                for (let i = 1; i <= 2; ++i) {
                    const paragraphs = getParagraphsOfTextBox(`div${i}`);
                    const innerHTMLs = paragraphs.map(p => p.innerHTML);
                    expect(innerHTMLs).toEqual(
                        [`Sentence ${i}.1၊ Sentence ${i}.2`],
                        `Failure for div${i}`
                    );
                }
            } else {
                throw new Error("Unrecognized scenario: " + scenario);
            }
        }

        function verifyCurrentHighlight(scenario: AudioMode) {
            const div = getFrameElementById("page", "div1");
            if (!div) {
                expect(div).not.toBeNull("div1 is null");
                return;
            }

            const page1 = getFrameElementById("page", "page1")!;
            const numCurrents = page1.querySelectorAll(".ui-audioCurrent")
                .length;
            expect(numCurrents).toBe(
                1,
                "Only 1 item is allowed to be the current: " + page1.innerHTML
            );

            switch (scenario) {
                case AudioMode.PureSentence: {
                    const firstSpan = div.querySelector("span.audio-sentence");
                    expect(firstSpan).toHaveClass("ui-audioCurrent");
                    break;
                }

                case AudioMode.PureTextBox:
                case AudioMode.HardSplitTextBox:
                case AudioMode.SoftSplitTextBox: {
                    expect(div).toHaveClass("ui-audioCurrent");
                    break;
                }

                default: {
                    throw new Error("Unrecognized scenario: " + scenario);
                }
            }
        }

        it("[Sentence/Sentence] showTool() should update sentence splits if no recordings exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.PureSentence,
                    false
                );
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[Sentence/Sentence] showTool() should not update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.PureSentence,
                    true
                );
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[Text Box/Text Box] showTool() should update sentence splits if no recordings exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.PureTextBox,
                    false
                );
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        // This returns the same result as above.
        it("[Text Box/Text Box] showTool() should NOT update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.PureTextBox,
                    true
                );
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Hard Split] showTool() should update sentence splits if no recordings exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.HardSplitTextBox,
                    false
                );
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Hard Split] showTool() should NOT update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.HardSplitTextBox,
                    true
                );

                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Soft Split] showTool() DOESN'T update sentence splits even if no recordings exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.SoftSplitTextBox,
                    false
                );
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Soft Split] showTool() should NOT update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplittingTestsAsync(
                    AudioMode.SoftSplitTextBox,
                    true
                );
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        async function runSentenceSplitTestsForUpdateMarkupAsync(
            scenario: AudioMode,
            areRecordingsPresent: boolean
        ) {
            let pageInnerHtml: string = "";
            let currentElementId: string = "";
            switch (scenario) {
                case AudioMode.PureSentence: {
                    pageInnerHtml = setupPureSentenceModeHtml();
                    currentElementId = "2.1";
                    break;
                }
                case AudioMode.PreTextBox: {
                    pageInnerHtml = setupPreTextBoxModeHtml();
                    currentElementId = "div2";
                    break;
                }
                case AudioMode.PureTextBox: {
                    pageInnerHtml = setupPureTextBoxModeHtml();
                    currentElementId = "div2";
                    break;
                }
                case AudioMode.HardSplitTextBox: {
                    pageInnerHtml = setupTextBoxHardSplitHtml();
                    currentElementId = "div2";
                    break;
                }
                case AudioMode.SoftSplitTextBox: {
                    pageInnerHtml = setupTextBoxSoftSplitHtml();
                    currentElementId = "div2";
                    break;
                }
                default: {
                    throw new Error("Unknown scenario: " + scenario);
                }
            }

            SetupIFrameFromHtml(`<div id="page1">${pageInnerHtml}</div>`);

            if (scenario === AudioMode.PureSentence) {
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.Sentence;
            } else {
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
            }

            const currentElement = getFrameElementById(
                "page",
                currentElementId
            );
            if (!currentElement) {
                throw new Error(
                    `Requested currentElement (id=${currentElementId}) not found.`
                );
            }

            theOneAudioRecorder.setSoundAndHighlightAsync(
                currentElement,
                false
            );

            const originalHtml1 = getFrameElementById("page", "div1")!
                .outerHTML;
            const originalHtml2 = getFrameElementById("page", "div2")!
                .outerHTML;

            if (areRecordingsPresent) {
                setAudioFilesPresent();
            } else {
                setAudioFilesDontExist();
            }

            // System under test
            const tbTool = new TalkingBookTool();
            await tbTool.updateMarkup();

            // Verification
            verifyHtmlStructureForUpdateMarkupTests(
                scenario,
                originalHtml1,
                originalHtml2
            );
            verifyCurrentHighlightForUpdateMarkup(scenario);
            verifySoundForUpdateMarkup(scenario);
            verifyRecordButtonEnabled(); // Make sure we didn't accidentally disable all the toolbox buttons
        }

        function verifyHtmlStructureForUpdateMarkupTests(
            scenario: AudioMode,
            originalHtml1: string,
            originalHtml2: string
        ) {
            // Div 1 should be untouched.
            const currentHtml1 = getFrameElementById("page", "div1")!.outerHTML;
            expect(currentHtml1).toBe(originalHtml1);

            if (scenario === AudioMode.SoftSplitTextBox) {
                // NOTE: SoftSplit test doesn't have very intuitive results.
                // Even if recordings aren't present, it actually preserves the original splits, even though normally, no recordings present means we should update them.
                // Prior to this check-if-recordings-present feature, it was actually the case that upon Soft Split, it never updated the markup.
                // (That's because the playback mode was identified as text box, and in that case, it never actually runs makeAudioSentenceElementsLeaf())
                // I'm not sure if that was entirely intentional, but it does seem very problematic to risk increasing/decreasing the number of splits while an audio is aligned to it.
                // Now that we do check if recordings present...
                // If they're somehow missing, we COULD now update the markup, but it doesn't seem worth the time, complexity, or risk to add that functionality.
                // I guess it's more like if there's no recordings present, then we do whatever the old behavior was... which in this case, is to not do anything.

                // Verify unchanged.
                const currentHtml = getFrameElementById("page", "div2")!
                    .outerHTML;
                expect(currentHtml).toBe(originalHtml2);
            } else if (
                scenario === AudioMode.PureSentence ||
                scenario === AudioMode.PreTextBox ||
                scenario === AudioMode.HardSplitTextBox
            ) {
                // Desired behavior is that after keypress, it will re-do the spans
                // Even if you already have it recorded.
                const spans = getAudioSentenceSpans(`div2`);
                const texts = spans.map(elem => {
                    return elem.innerText;
                });
                expect(texts).toEqual(
                    [`Sentence 2.1၊ Sentence 2.2`],
                    `Failure for div2`
                );
            } else if (scenario === AudioMode.PureTextBox) {
                const paragraphs = getParagraphsOfTextBox("div2");
                const innerHTMLs = paragraphs.map(p => p.innerHTML);
                expect(innerHTMLs).toEqual(
                    ["Sentence 2.1၊ Sentence 2.2"],
                    "Failure for div2"
                );
            } else {
                throw new Error("Unrecognized scenario: " + scenario);
            }
        }

        function verifyCurrentHighlightForUpdateMarkup(
            scenario: AudioMode
        ): void {
            const div = getFrameElementById("page", "div2");
            if (!div) {
                expect(div).not.toBeNull("div2 is null");
                return;
            }

            const page1 = getFrameElementById("page", "page1")!;
            const numCurrents = page1.querySelectorAll(".ui-audioCurrent")
                .length;
            expect(numCurrents).toBe(
                1,
                "Only 1 item is allowed to be the current: " + page1.innerHTML
            );

            switch (scenario) {
                case AudioMode.PureSentence: {
                    const firstSpan = div.querySelector("span.audio-sentence");
                    expect(firstSpan).toHaveClass("ui-audioCurrent");
                    break;
                }

                case AudioMode.PreTextBox:
                case AudioMode.PureTextBox:
                case AudioMode.HardSplitTextBox:
                case AudioMode.SoftSplitTextBox: {
                    expect(div).toHaveClass("ui-audioCurrent");
                    break;
                }

                default: {
                    throw new Error("Unrecognized scenario: " + scenario);
                }
            }
        }

        function verifySoundForUpdateMarkup(scenario: AudioMode): void {
            const player = document.getElementById(
                "player"
            ) as HTMLMediaElement | null;
            if (!player) {
                expect(player).not.toBeNull("player is null");
                return;
            }

            const page1 = getFrameElementById("page", "page1")!;
            const numCurrents = page1.querySelectorAll(".ui-audioCurrent")
                .length;
            expect(numCurrents).toBe(
                1,
                "Only 1 item is allowed to be the current: " + page1.innerHTML
            );

            let expectedSrc: string = "";
            switch (scenario) {
                case AudioMode.PureSentence:
                case AudioMode.PreTextBox:
                case AudioMode.HardSplitTextBox: {
                    expectedSrc = "2.1";
                    break;
                }

                case AudioMode.PureTextBox:
                case AudioMode.SoftSplitTextBox: {
                    expectedSrc = "div2";
                    break;
                }

                default: {
                    throw new Error("Unrecognized scenario: " + scenario);
                }
            }

            expect(StripPlayerSrcNoCacheSuffix(player.src)).toBe(
                `http://localhost:9876/bloom/api/audio/wavFile?id=audio/${expectedSrc}.wav`
            );
        }

        // Although showTool() won't update sentence splits if recordings exist,
        // but we do want that to happen when a keypress happens (i.e. updateMarkup)
        it("[Sentence/Sentence] updateMarkup() will update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplitTestsForUpdateMarkupAsync(
                    AudioMode.PureSentence,
                    true
                );

                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence] updateMarkup() will update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplitTestsForUpdateMarkupAsync(
                    AudioMode.PreTextBox,
                    true
                );

                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/TextBox] updateMarkup() will update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplitTestsForUpdateMarkupAsync(
                    AudioMode.PureTextBox,
                    true
                );

                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Hard Split] updateMarkup() will update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplitTestsForUpdateMarkupAsync(
                    AudioMode.HardSplitTextBox,
                    true
                );

                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Soft Split] updateMarkup() will update sentence splits if recordings do exist", async done => {
            try {
                await runSentenceSplitTestsForUpdateMarkupAsync(
                    AudioMode.SoftSplitTextBox,
                    true
                );

                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });
    });
});

function getAudioSentenceSpans(divId: string): HTMLSpanElement[] {
    const element = getFrameElementById("page", divId);

    expect(element).toBeTruthy(`${divId} should exist.`);

    const htmlElement = element! as HTMLElement;
    const collection = htmlElement.querySelectorAll("span.audio-sentence");
    return Array.from(collection) as HTMLSpanElement[];
}

function getHighlightSegments(divId: string): HTMLSpanElement[] {
    const element = getFrameElementById("page", divId);

    expect(element).toBeTruthy(`${divId} should exist.`);

    const htmlElement = element! as HTMLElement;
    const collection = htmlElement.querySelectorAll(
        "span.bloom-highlightSegment"
    );
    return Array.from(collection) as HTMLSpanElement[];
}

function getParagraphsOfTextBox(divId: string): HTMLParagraphElement[] {
    const element = getFrameElementById("page", divId);

    expect(element).toBeTruthy(`${divId} should exist.`);

    const htmlElement = element! as HTMLElement;
    const collection = htmlElement.querySelectorAll("p");
    return Array.prototype.map.call(
        collection,
        (elem: HTMLParagraphElement) => {
            return elem;
        }
    );
}

function verifyRecordButtonEnabled() {
    expect(document.getElementById("audio-record")).not.toHaveClass("disabled");
}
