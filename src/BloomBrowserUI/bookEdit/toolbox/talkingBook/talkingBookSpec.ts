import TalkingBookTool from "./talkingBook";
import { theOneAudioRecorder } from "./audioRecording";
import {
    SetupTalkingBookUIElements,
    SetupIFrameFromHtml
} from "./audioRecordingSpec";

describe("talking book tests", () => {
    describe(", updateMarkup()", () => {
        it("moves highlight after focus changes", () => {
            // Setup Initial HTML
            SetupTalkingBookUIElements();
            const textBox1 =
                '<div class="bloom-editable" id="div1"><p><span id="1.1" class="audio-sentence ui-audioCurrent">1.1</span></p></div>';
            const textBox2 =
                '<div class="bloom-editable" id="div2"><p><span id="2.1" class="audio-sentence">2.1</span></p></div>';
            SetupIFrameFromHtml(`<div id='page1'>${textBox1}${textBox2}</div>`);

            // Setup talking book tool
            const tbTool = new TalkingBookTool();
            tbTool.showTool();

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
        });
    });
});
