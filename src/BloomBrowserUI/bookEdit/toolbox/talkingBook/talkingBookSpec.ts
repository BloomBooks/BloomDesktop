import TalkingBookTool from "./talkingBook";
import {
    theOneAudioRecorder,
    initializeTalkingBookToolAsync,
    AudioRecordingMode
} from "./audioRecording";
import {
    SetupTalkingBookUIElements,
    SetupIFrameAsync,
    SetupIFrameFromHtml
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
            tbTool.updateMarkup();

            // Verification
            const currentTextBox = theOneAudioRecorder.getCurrentTextBox()!;
            const currentId = currentTextBox.getAttribute("id");
            expect(currentId).toBe("div2");
            done();
        });
    });

    describe("- tests for sentence splitting()", () => {
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

        it("[Sentence/Sentence] showTool() should update sentence splits if no recordings exist", async done => {
            try {
                const textBox1 =
                    '<div class="bloom-editable" id="div1"><p><span id="1.1" class="audio-sentence ui-audioCurrent">1.1၊</span> <span id="1.2" class="audio-sentence">1.2</span></p></div>';
                SetupIFrameFromHtml(`<div id='page1'>${textBox1}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.Sentence;
                // Mark not exists
                spyOn(axios, "get").and.returnValue(
                    Promise.reject(new Error("Fake 404 Error"))
                );

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const spans = getAudioSentenceSpans("div1");
                const texts = spans.map(elem => {
                    return elem.innerText;
                });
                expect(texts).toEqual(["1.1၊ 1.2"]);
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[Sentence/Sentence] showTool() should not update sentence splits if recordings do exist", async done => {
            try {
                const textBox1 =
                    '<div class="bloom-editable" id="div1"><p><span id="1.1" class="audio-sentence ui-audioCurrent">1.1၊</span> <span id="1.2" class="audio-sentence">1.2</span></p></div>';
                SetupIFrameFromHtml(`<div id='page1'>${textBox1}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.Sentence;
                // Mark that the recording exists.
                // FYI - spies only last for the scope of the "describe" or "it" block in which it was defined.
                spyOn(axios, "get").and.returnValue(Promise.resolve());

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const spans = getAudioSentenceSpans("div1");
                const texts = spans.map(elem => {
                    return elem.innerText;
                });
                expect(texts).toEqual(["1.1၊", "1.2"]);

                verifyRecordButtonEnabled();
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[Text Box/Text Box] showTool() should update sentence splits if no recordings exist", async done => {
            try {
                const divStartHtml =
                    '<div class="bloom-editable audio-sentence ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox">';
                const divInnerHtml = "<p>1.1၊ 1.2</p>";
                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                SetupIFrameFromHtml(`<div id='page1'>${divHtml}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
                // Mark not exists
                spyOn(axios, "get").and.returnValue(
                    Promise.reject(new Error("Fake 404 Error"))
                );

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const paragraphs = getParagraphsOfTextBox("div1");
                const innerHTMLs = paragraphs.map(p => p.innerHTML);
                expect(innerHTMLs).toEqual(["1.1၊ 1.2"]);
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
                const divStartHtml =
                    '<div class="bloom-editable audio-sentence ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox">';
                const divInnerHtml = "<p>1.1၊ 1.2</p>";
                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                SetupIFrameFromHtml(`<div id='page1'>${divHtml}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
                // Mark exists
                spyOn(axios, "get").and.returnValue(Promise.resolve());

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const paragraphs = getParagraphsOfTextBox("div1");
                const innerHTMLs = paragraphs.map(p => p.innerHTML);
                expect(innerHTMLs).toEqual(["1.1၊ 1.2"]);
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Hard Split] showTool() should update sentence splits if no recordings exist", async done => {
            try {
                const divStartHtml =
                    '<div class="bloom-editable ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox">';
                const divInnerHtml =
                    '<p><span class="audio-sentence">1.1၊</span> <span class="audio-sentence">1.2</span></p>';
                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                SetupIFrameFromHtml(`<div id='page1'>${divHtml}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
                // Mark not exists
                spyOn(axios, "get").and.returnValue(
                    Promise.reject(new Error("Fake 404 Error"))
                );

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const spans = getAudioSentenceSpans("div1");
                const texts = spans.map(elem => {
                    return elem.innerText;
                });
                expect(texts).toEqual(["1.1၊ 1.2"]);
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Hard Split] showTool() should NOT update sentence splits if recordings do exist", async done => {
            try {
                const divStartHtml =
                    '<div class="bloom-editable" id="div1" data-audiorecordingmode="TextBox">';
                const divInnerHtml =
                    '<p><span class="audio-sentence">1.1၊</span> <span class="audio-sentence">1.2</span></p>';
                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                SetupIFrameFromHtml(`<div id='page1'>${divHtml}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
                // mark exists
                spyOn(axios, "get").and.returnValue(Promise.resolve());

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const spans = getAudioSentenceSpans("div1");
                const texts = spans.map(elem => {
                    return elem.innerText;
                });
                expect(texts).toEqual(["1.1၊", "1.2"]);

                const div = getFrameElementById("page", "div1");
                expect(div).toHaveClass("ui-audioCurrent");
                expect(spans[0]).not.toHaveClass("ui-audioCurrent");

                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Soft Split] showTool() DOESN'T update sentence splits even if no recordings exist", async done => {
            try {
                const divStartHtml =
                    '<div class="bloom-editable ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0">';
                const divInnerHtml =
                    '<p><span id="1.1" class="bloom-highlightSegment">1.1၊</span> <span id="1.2" class="bloom-highlightSegment">1.2</span></p>';

                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                SetupIFrameFromHtml(`<div id='page1'>${divHtml}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
                // Mark not exists
                spyOn(axios, "get").and.returnValue(
                    Promise.reject(new Error("Fake 404 Error"))
                );

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const spans = getHighlightSegments("div1");
                const texts = spans.map(elem => {
                    return elem.innerText;
                });

                // NOTE: This test doesn't have very intuitive results.
                // It actually preserves the original splits, even though normally, no recordings present means we should update them.
                // Prior to this check-if-recordings-present feature, it was actually the case that upon Soft Split, it never updated the markup.
                // (That's because the playback mode was identified as text box, and in that case, it never actually runs makeAudioSentenceElementsLeaf())
                // I'm not sure if that was entirely intentional, but it does seem very problematic to risk increasing/decreasing the number of splits while an audio is aligned to it.
                // Now that we do check if recordings present...
                // If they're somehow missing, we COULD now update the markup, but it doesn't seem worth the time, complexity, or risk to add that functionality.
                // I guess it's more like if there's no recordings present, then we do whatever the old behavior was... which in this case, is to not do anything.
                expect(texts).toEqual(["1.1၊", "1.2"]);
                done();
            } catch (error) {
                fail(error);
                done();
                throw error;
            }
        });

        it("[TextBox/Sentence Soft Split] showTool() should NOT update sentence splits if recordings do exist", async done => {
            try {
                const divStartHtml =
                    '<div class="bloom-editable ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox" data-audiorecordingendtimes="1.0 2.0">';
                const divInnerHtml =
                    '<p><span id="1.1" class="bloom-highlightSegment">1.1၊</span> <span id="1.2" class="bloom-highlightSegment">1.2</span></p>';
                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                SetupIFrameFromHtml(`<div id='page1'>${divHtml}</div>`);
                theOneAudioRecorder.audioRecordingMode =
                    AudioRecordingMode.TextBox;
                // mark exists
                spyOn(axios, "get").and.returnValue(Promise.resolve());

                // System under test
                const tbTool = new TalkingBookTool();
                await tbTool.showTool();

                // Verification
                const spans = getHighlightSegments("div1");
                const texts = spans.map(elem => {
                    return elem.innerText;
                });
                expect(texts).toEqual(["1.1၊", "1.2"]);
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
    return Array.prototype.map.call(collection, (elem: HTMLSpanElement) => {
        return elem;
    });
}

function getHighlightSegments(divId: string): HTMLSpanElement[] {
    const element = getFrameElementById("page", divId);

    expect(element).toBeTruthy(`${divId} should exist.`);

    const htmlElement = element! as HTMLElement;
    const collection = htmlElement.querySelectorAll(
        "span.bloom-highlightSegment"
    );
    return Array.prototype.map.call(collection, (elem: HTMLSpanElement) => {
        return elem;
    });
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

function getFrameElementById(
    frameId: string,
    elementId: string
): HTMLElement | null {
    const frame = parent.window.document.getElementById(frameId);
    if (!frame) {
        return null;
    }

    return (frame as HTMLIFrameElement).contentDocument!.getElementById(
        elementId
    );
}
