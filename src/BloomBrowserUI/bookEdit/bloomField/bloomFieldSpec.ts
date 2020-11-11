///<reference path="BloomField.ts" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import BloomField from "./BloomField";

function WireUp() {
    $(".bloom-editable").each(function() {
        BloomField.ManageField(this);
    });
}

describe("BloomField", () => {
    beforeEach(() => {
        $("body").html(
            '<head></head><div id="simple" contenteditable="true" class="bloom-editable"></div>'
        );
    });

    it("convertStandardFormatVerseMarkersToSuperscript creates superscript when text includes SFM verse marker", () => {
        const result = BloomField.convertStandardFormatVerseMarkersToSuperscript(
            "A \\v 2 B C \\v 3 D E."
        );
        expect(result).toBe("A <sup>2</sup> B C <sup>3</sup> D E.");
    });

    it("convertStandardFormatVerseMarkersToSuperscript does not create superscript when text doesn't include SFM verse marker", () => {
        const result = BloomField.convertStandardFormatVerseMarkersToSuperscript(
            "A v 2 B C \\v D E."
        );
        expect(result).toBe("A v 2 B C \\v D E.");
    });

    it("copyAudioFilesWithNewIdsDuringPasting does not change input when no audio found", () => {
        const input = '<p><span class="bold">This is a test!</span>';
        const result = BloomField.copyAudioFilesWithNewIdsDuringPasting(input);
        expect(result).toBe(input);
    });

    it("copyAudioFilesWithNewIdsDuringPasting changes span ids when audio found", () => {
        const input =
            '<p><span id="i2b4f74ac-3d71-4692-9793-bb18ff56b6e2" class="audio-sentence ui-currentAudio" recordingmd5="db3bd4ec0300c2491de826fc858603e8" data-duration="2.690566">This page was copied and pasted.</span>&nbsp; <span id="i39184f35-39bf-4574-9e55-3eedafb6bde9" class="audio-sentence" recordingmd5="21e576d30fb30bef4f09b9c3e051763d" data-duration="5.825206">This paragraph has two sentences, so it should have two audio spans.</span></p>';
        const result = BloomField.copyAudioFilesWithNewIdsDuringPasting(input);

        expect(result).not.toBe(input);
        expect(result).toContainText("This page was copied and pasted");
        expect(result).toContainText(
            "This paragraph has two sentences, so it should have two audio spans."
        );
        expect(result).toContainHtml('class="audio-sentence"');
        expect(result).not.toContainHtml(
            'id="i2b4f74ac-3d71-4692-9793-bb18ff56b6e2"'
        );
        expect(result).not.toContainHtml(
            'id="i39184f35-39bf-4574-9e55-3eedafb6bde9"'
        );
        expect(result.startsWith('<p><span id="')).toBe(true);
        expect(
            result.includes(
                '" class="audio-sentence">This page was copied and pasted.</span>&nbsp; <span id="'
            )
        ).toBe(true);
        expect(
            result.endsWith(
                '" class="audio-sentence">This paragraph has two sentences, so it should have two audio spans.</span></p>'
            )
        ).toBe(true);
    });

    it("bloom-editable div creates a <p>", () => {
        WireUp();
        expect($("div p").length).toBeGreaterThan(0);
    });

    describe("backspacePreventionTests", () => {
        it("backspace prevented at start of paragraph", () => {
            const shouldBeCanceled = true;

            const paragraphInnerHtml = "Sentence 1. Sentence 2";

            const setCursor = () => {
                setCursorTo("p1", 0);
            };

            runBackspacePreventionTest(
                paragraphInnerHtml,
                setCursor,
                shouldBeCanceled
            );
        });

        it("backspace prevented at start of 1st child", () => {
            const shouldBeCanceled = true;

            const paragraphInnerHtml =
                '<span id="s1">Sentence 1. </span><span id="s2">Sentence 2</span>';

            const setCursor = () => {
                setCursorTo("s1", 0);
            };

            runBackspacePreventionTest(
                paragraphInnerHtml,
                setCursor,
                shouldBeCanceled
            );
        });

        it("backspace NOT prevented at 2nd character of paragraph", () => {
            const shouldBeCanceled = false;

            const paragraphInnerHtml = "Sentence 1. Sentence 2";

            const setCursor = () => {
                setCursorTo("p1", 1); // 0-indexed
            };

            runBackspacePreventionTest(
                paragraphInnerHtml,
                setCursor,
                shouldBeCanceled
            );
        });

        it("backspace NOT prevented at 1st child element if text node precedes it.", () => {
            const shouldBeCanceled = false;

            const paragraphInnerHtml =
                'Sentence 0. <span id="s1">Sentence 1. </span><span id="s2">Sentence 2</span>';

            const setCursor = () => {
                setCursorTo("s1", 0);
            };

            runBackspacePreventionTest(
                paragraphInnerHtml,
                setCursor,
                shouldBeCanceled
            );
        });

        it("backspace NOT prevented at start of 2nd child (element)", () => {
            const shouldBeCanceled = false;

            const paragraphInnerHtml =
                '<span id="s1">Sentence 1. </span><span id="s2">Sentence 2</span>';

            const setCursor = () => {
                setCursorTo("s2", 0);
            };

            runBackspacePreventionTest(
                paragraphInnerHtml,
                setCursor,
                shouldBeCanceled
            );
        });

        it("backspace NOT prevented at start of 2nd child's text node", () => {
            const shouldBeCanceled = false;

            const paragraphInnerHtml =
                '<span id="s1">Sentence 1. </span><span id="s2">Sentence 2</span>';

            const setCursor = () => {
                const selection = window.getSelection()!;
                const spanElement = document.getElementById("s2")!;
                const textNode = spanElement.firstChild!;
                expect(textNode.nodeName).toBe(
                    "#text",
                    "Test setup error - wrong nodeName: " + textNode.nodeName
                );

                selection.collapse(textNode, 0);
            };

            runBackspacePreventionTest(
                paragraphInnerHtml,
                setCursor,
                shouldBeCanceled
            );
        });

        function runBackspacePreventionTest(
            paragraphInnerHtml: string,
            setSelectionCallback: () => void,
            isCancellationExpected: boolean
        ) {
            const editable = document.getElementById("simple")!;
            editable.innerHTML = `<p id="p1">${paragraphInnerHtml}</p>`;

            WireUp();

            // Set the cursor to a specific spot
            setSelectionCallback();

            // Now fake a backspace
            //
            // Our code still checks which, but nowadays it's deprecated in favor of key,
            // so KeyboardEventInit doesn't officially recognize it in the type definition,
            // but we need it anyway, so just force the types to work.
            const keyEventInit = ({
                // 8 is ASCII code for backspace
                which: 8,
                cancelable: true // preventDefault only works on cancellable events.
            } as unknown) as KeyboardEventInit;

            // FYI: This simulated backspace event doesn't actually modify the text,
            // but you can still check if the event was cancelled.
            const keyboardEvent = new KeyboardEvent("keydown", keyEventInit);
            const wasCanceled = !editable.dispatchEvent(keyboardEvent);

            // Verification
            const testFailureMessage = isCancellationExpected
                ? "preventDefault() should be called, but was not"
                : "preventDefault() should not be called, but it was";
            expect(wasCanceled).toBe(
                isCancellationExpected,
                testFailureMessage
            );
        }
    });

    function setCursorTo(elementId: string, offset: number) {
        const selection = window.getSelection()!;
        selection.collapse(document.getElementById(elementId)!, offset);
    }
});
