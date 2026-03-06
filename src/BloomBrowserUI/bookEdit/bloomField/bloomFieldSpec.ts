///<reference path="BloomField.ts" />
///<reference path="../../typings/bundledFromTSC.d.ts"/>
import { getTestRoot, removeTestRoot } from "../../utils/testHelper";
import BloomField from "./BloomField";

function WireUp() {
    $(".bloom-editable").each(function() {
        BloomField.ManageField(this);
    });
}

describe("BloomField", () => {
    beforeEach(() => {
        const root = getTestRoot();
        root.innerHTML =
            '<div id="simple" contenteditable="true" class="bloom-editable"></div>';
    });

    // Politely clean up for the next test suite.
    afterAll(removeTestRoot);

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
        const input = '<p><span class="bold">This is a test!</span></p>';
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
        // verify workings of this test code by checking both input and result for original id values
        expect(input).toContainHtml(
            'id="i2b4f74ac-3d71-4692-9793-bb18ff56b6e2"'
        );
        expect(input).toContainHtml(
            'id="i39184f35-39bf-4574-9e55-3eedafb6bde9"'
        );
        expect(result).not.toContainHtml(
            'id="i2b4f74ac-3d71-4692-9793-bb18ff56b6e2"'
        );
        expect(result).not.toContainHtml(
            'id="i39184f35-39bf-4574-9e55-3eedafb6bde9"'
        );

        expect(result.startsWith('<p><span id="')).toBe(true);
        expect(
            result.includes(
                '" class="audio-sentence ui-currentAudio" recordingmd5="db3bd4ec0300c2491de826fc858603e8" data-duration="2.690566">This page was copied and pasted.</span>&nbsp; <span id="'
            )
        ).toBe(true);
        expect(
            result.endsWith(
                '" class="audio-sentence" recordingmd5="21e576d30fb30bef4f09b9c3e051763d" data-duration="5.825206">This paragraph has two sentences, so it should have two audio spans.</span></p>'
            )
        ).toBe(true);
    });

    it("copyAudioFilesWithNewIdsDuringPasting handles emphasized text input", () => {
        const input =
            '<p><span data-duration="2.481632" id="i1ea22c02-9344-4afe-9bbb-5946576d7907" class="audio-sentence">island in the middle of a <em>large</em> lake</span></p>';
        const result = BloomField.copyAudioFilesWithNewIdsDuringPasting(input);
        expect(result).not.toBe(input);
        expect(result).toContainHtml('class="audio-sentence"');
        expect(result).toContainHtml(
            "island in the middle of a <em>large</em> lake"
        );

        // verify workings of this test code by checking both input and result for original id value
        expect(input).toContainHtml(
            'id="i1ea22c02-9344-4afe-9bbb-5946576d7907"'
        );
        expect(result).not.toContainHtml(
            'id="i1ea22c02-9344-4afe-9bbb-5946576d7907"'
        );

        expect(
            result.startsWith('<p><span data-duration="2.481632" id="')
        ).toBe(true);
        expect(
            result.endsWith(
                '" class="audio-sentence">island in the middle of a <em>large</em> lake</span></p>'
            )
        ).toBe(true);
    });

    it("copyAudioFilesWithNewIdsDuringPasting handles text with span markup", () => {
        const input =
            '<p><span data-duration="5.2244" id="i1b625773-f5af-4289-afe6-b45a01d51e0e" class="audio-sentence" recordingmd5="undefined">Part 1: <span class="bloom-linebreak"></span>God, Creation &amp; Fall, Law</span></p>';
        const result = BloomField.copyAudioFilesWithNewIdsDuringPasting(input);
        expect(result).not.toBe(input);
        expect(result).toContainHtml('class="audio-sentence"');
        expect(result).toContainHtml(
            'Part 1: <span class="bloom-linebreak"></span>God, Creation &amp; Fall, Law</span>'
        );

        // verify workings of this test code by checking both input and result for original id value
        expect(input).toContainHtml(
            'id="i1b625773-f5af-4289-afe6-b45a01d51e0e"'
        );
        expect(result).not.toContainHtml(
            'id="i1b625773-f5af-4289-afe6-b45a01d51e0e"'
        );

        expect(result.startsWith('<p><span data-duration="5.2244" id="')).toBe(
            true
        );
        expect(
            result.endsWith(
                '" class="audio-sentence" recordingmd5="undefined">Part 1: <span class="bloom-linebreak"></span>God, Creation &amp; Fall, Law</span></p>'
            )
        ).toBe(true);
    });

    it("copyAudioFilesWithNewIdsDuringPasting handles odd audio span attributes", () => {
        const input =
            '<p><span data-duration="5.2244" class="bloom-uiCurrent audio-sentence bloom-SomethingElse" recordingmd5="undefined" id="i1b625773-f5af-4289-afe6-b45a01d51e0e"><strong>Part 1:</strong> God, Creation &amp; Fall, Law</span></p>';
        const result = BloomField.copyAudioFilesWithNewIdsDuringPasting(input);
        expect(result).not.toBe(input);
        expect(result).toContainHtml(
            'class="bloom-uiCurrent audio-sentence bloom-SomethingElse"'
        );
        expect(result).toContainHtml(
            "<strong>Part 1:</strong> God, Creation &amp; Fall, Law</span>"
        );

        // verify workings of this test code by checking both input and result for original id value
        expect(input).toContainHtml(
            'id="i1b625773-f5af-4289-afe6-b45a01d51e0e"'
        );
        expect(result).not.toContainHtml(
            'id="i1b625773-f5af-4289-afe6-b45a01d51e0e"'
        );

        expect(
            result.startsWith(
                '<p><span data-duration="5.2244" class="bloom-uiCurrent audio-sentence bloom-SomethingElse" recordingmd5="undefined" id="'
            )
        ).toBe(true);
        expect(
            result.endsWith(
                '"><strong>Part 1:</strong> God, Creation &amp; Fall, Law</span></p>'
            )
        ).toBe(true);
    });

    it("copyAudioFilesWithNewIdsDuringPasting ignores malformed audio span attributes", () => {
        const input =
            '<p><span data-duration="5.2244" class="bloom-uiCurrent bloom-SomethingElse" data-type="audio-sentence" recordingmd5="undefined" id="i1b625773-f5af-4289-afe6-b45a01d51e0e"><strong>Part 1:</strong> God, Creation &amp; Fall, Law</span></p>';
        const result = BloomField.copyAudioFilesWithNewIdsDuringPasting(input);
        expect(result).toBe(input);
    });

    it("copyAudioFilesWithNewIdsDuringPasting removes .bloom-highlightSegment span markup", () => {
        const input =
            '<span id="i8fe9322b-47f1-4f8d-98a9-a24c0f709b23" class="bloom-highlightSegment">They are kittens, after all.</span>';
        const result = BloomField.copyAudioFilesWithNewIdsDuringPasting(input);
        expect(result).toBe("They are kittens, after all.");
    });

    it("removeAudioSpanMarkupDuringPasting removes only .audio-sentence and .bloom-highlightSegment span markup [1]", () => {
        const input1 = '<p><span class="bold">This is a test!</span></p>';
        const result1 = BloomField.removeAudioSpanMarkupDuringPasting(input1);
        expect(result1).toBe(input1);
    });

    it("removeAudioSpanMarkupDuringPasting removes .audio-sentence span markup [1]", () => {
        const input2 =
            '<p><span id="i2b4f74ac-3d71-4692-9793-bb18ff56b6e2" class="audio-sentence ui-currentAudio" recordingmd5="db3bd4ec0300c2491de826fc858603e8" data-duration="2.690566">This page was copied and pasted.</span>&nbsp; <span id="i39184f35-39bf-4574-9e55-3eedafb6bde9" class="audio-sentence" recordingmd5="21e576d30fb30bef4f09b9c3e051763d" data-duration="5.825206">This paragraph has two sentences, so it should have two audio spans.</span></p>';
        const result2 = BloomField.removeAudioSpanMarkupDuringPasting(input2);
        expect(result2).toBe(
            "<p>This page was copied and pasted.&nbsp; This paragraph has two sentences, so it should have two audio spans.</p>"
        );
    });

    it("removeAudioSpanMarkupDuringPasting removes .audio-sentence span markup [2]", () => {
        const input3 =
            '<p><span data-duration="2.481632" id="i1ea22c02-9344-4afe-9bbb-5946576d7907" class="audio-sentence">island in the middle of a <em>large</em> lake</span></p>';
        const result3 = BloomField.removeAudioSpanMarkupDuringPasting(input3);
        expect(result3).toBe(
            "<p>island in the middle of a <em>large</em> lake</p>"
        );
    });

    it("removeAudioSpanMarkupDuringPasting removes .audio-sentence span markup [3]", () => {
        const input4 =
            '<p><span data-duration="5.2244" id="i1b625773-f5af-4289-afe6-b45a01d51e0e" class="audio-sentence" recordingmd5="undefined">Part 1: <span class="bloom-linebreak"></span>God, Creation &amp; Fall, Law</span></p>';
        const result4 = BloomField.removeAudioSpanMarkupDuringPasting(input4);
        expect(result4).toBe(
            '<p>Part 1: <span class="bloom-linebreak"></span>God, Creation &amp; Fall, Law</p>'
        ); // wrong, but with regex how to do better?
    });

    it("removeAudioSpanMarkupDuringPasting removes .audio-sentence span markup [4]", () => {
        const input5 =
            '<p><span data-duration="5.2244" class="bloom-uiCurrent audio-sentence bloom-SomethingElse" recordingmd5="undefined" id="i1b625773-f5af-4289-afe6-b45a01d51e0e"><strong>Part 1:</strong> God, Creation &amp; Fall, Law</span></p>';
        const result5 = BloomField.removeAudioSpanMarkupDuringPasting(input5);
        expect(result5).toBe(
            "<p><strong>Part 1:</strong> God, Creation &amp; Fall, Law</p>"
        );
    });

    it("removeAudioSpanMarkupDuringPasting removes only .audio-sentence and .bloom-highlightSegment span markup [2]", () => {
        const input6 =
            '<p><span data-duration="5.2244" class="bloom-uiCurrent bloom-SomethingElse" data-type="audio-sentence" recordingmd5="undefined" id="i1b625773-f5af-4289-afe6-b45a01d51e0e"><strong>Part 1:</strong> God, Creation &amp; Fall, Law</span></p>';
        const result6 = BloomField.removeAudioSpanMarkupDuringPasting(input6);
        expect(result6).toBe(input6); // look closely at the input.  :-)
    });

    it("removeAudioSpanMarkupDuringPasting removes .bloom-highlightSegment span markup [1]", () => {
        const input7 =
            '<p><span id="i8fe9322b-47f1-4f8d-98a9-a24c0f709b23" class="bloom-highlightSegment">They are kittens, after all.</span></p>';
        const result7 = BloomField.removeAudioSpanMarkupDuringPasting(input7);
        expect(result7).toBe("<p>They are kittens, after all.</p>");
    });

    it("removeAudioSpanMarkupDuringPasting removes .bloom-highlightSegment span markup [2]", () => {
        const input8 =
            '<p><span test="123" id="i8fe9322b-47f1-4f8d-98a9-a24c0f709b23" data="xyz" class="bloom-highlightSegment" more="end">They are kittens, after all.</span></p>';
        const result8 = BloomField.removeAudioSpanMarkupDuringPasting(input8);
        expect(result8).toBe("<p>They are kittens, after all.</p>");
    });

    it("removeAudioSpanMarkupDuringPasting removes .bloom-highlightSegment span markup [3]", () => {
        const input9 =
            '<p><span test="123" class="bloom-highlightSegment" data="xyz" id="i8fe9322b-47f1-4f8d-98a9-a24c0f709b23" more="end">They are kittens, after all.</span></p>';
        const result9 = BloomField.removeAudioSpanMarkupDuringPasting(input9);
        expect(result9).toBe("<p>They are kittens, after all.</p>");
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
                const selection = window.getSelection();
                const spanElement = document.getElementById("s2");
                if (selection && spanElement) {
                    const textNode = spanElement.firstChild;
                    if (textNode) {
                        expect(textNode.nodeName).toBe(
                            "#text",
                            "Test setup error - wrong nodeName: " +
                                textNode.nodeName
                        );

                        selection.collapse(textNode, 0);
                    } else {
                        expect(false).toBeTruthy(); // fail on principle; should never happen
                    }
                } else {
                    expect(false).toBeTruthy(); // fail on principle; should never happen
                }
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
            const editable = document.getElementById("simple");
            if (editable) {
                editable.innerHTML = `<p id="p1">${paragraphInnerHtml}</p>`;

                WireUp();

                // Set the cursor to a specific spot
                setSelectionCallback();

                // Now fake a backspace
                const keyEventInit: KeyboardEventInit = {
                    key: "Backspace",
                    cancelable: true // preventDefault only works on cancellable events.
                };

                // FYI: This simulated backspace event doesn't actually modify the text,
                // but you can still check if the event was cancelled.
                const keyboardEvent = new KeyboardEvent(
                    "keydown",
                    keyEventInit
                );
                // DispatchEvent dispatches a synthetic event to target and returns true if either event's
                // cancelable attribute value is false or its preventDefault() method was not invoked,
                // and false otherwise.
                // Cancelable is true. Therefore, it returns true if preventDefault was not invoked.
                // Inverting it means wasCanceled is true if preventDefault WAS invoked.
                const wasCanceled = !editable.dispatchEvent(keyboardEvent);

                // Verification
                const testFailureMessage = isCancellationExpected
                    ? "preventDefault() should be called, but was not"
                    : "preventDefault() should not be called, but it was";
                expect(wasCanceled).toBe(
                    isCancellationExpected,
                    testFailureMessage
                );
            } else {
                expect(false).toBeTruthy(); // fail on principle; should never happen
            }
        }
    });

    function setCursorTo(elementId: string, offset: number) {
        const selection = window.getSelection();
        if (selection) {
            const targetElement = document.getElementById(elementId);
            if (targetElement) {
                selection.collapse(targetElement, offset);
            }
        }
    }

    function runLineBreakInsertionTest(
        paragraphInnerHtml: string,
        setSelectionCallback: () => void
    ) {
        const editable = document.getElementById("simple");
        if (editable) {
            editable.innerHTML = `<p id="p1">${paragraphInnerHtml}</p>`;

            WireUp();

            // Set the cursor to a specific spot
            setSelectionCallback();

            // Now fake Shift+Enter
            const keyEventInit: KeyboardEventInit = {
                key: "Enter",
                shiftKey: true
            };
            // BloomField.MakeShiftEnterInsertLineBreak() traps a "keypress" event.
            const keyboardEvent = new KeyboardEvent("keypress", keyEventInit);
            editable.dispatchEvent(keyboardEvent);

            // Verification
            //console.log(editable.outerHTML);
            expect(editable).toContainElement("span.bloom-linebreak");
            const lineBreakSpan = editable.querySelector(
                "span.bloom-linebreak"
            );
            expect(
                lineBreakSpan && lineBreakSpan.getAttribute("contenteditable")
            ).toBe("false");
        } else {
            expect(false).toBeTruthy(); // fail on principle; should never happen
        }
    }

    describe("insertLineBreakTests", () => {
        it("insert a line break inside a paragraph", () => {
            const paragraphInnerHtml = "Some text here.";

            const setCursor = () => {
                setCursorTo("p1", 1);
            };
            runLineBreakInsertionTest(paragraphInnerHtml, setCursor);
        });
        it("insert a line break inside a span inside a paragraph", () => {
            const paragraphInnerHtml =
                "Some <span id='s1'><em>text</em></span> here.";

            const setCursor = () => {
                setCursorTo("s1", 0);
            };
            runLineBreakInsertionTest(paragraphInnerHtml, setCursor);
        });
    });

    describe("ensureCaretNotInsideLineBreakSpan", () => {
        function callEnsureCaretNotInsideLineBreakSpan() {
            (BloomField as any).EnsureCaretNotInsideLineBreakSpan();
        }

        it("moves a collapsed caret from inside bloom-linebreak span to after the span", () => {
            const editable = document.getElementById("simple");
            if (!editable) {
                fail("Test setup error: expected #simple editable element.");
                return;
            }

            editable.innerHTML =
                '<p id="p1">A<span id="lb" class="bloom-linebreak"></span>B</p>';
            const paragraph = document.getElementById("p1");
            const lineBreakSpan = document.getElementById("lb");
            const selection = window.getSelection();
            if (!paragraph || !lineBreakSpan || !selection) {
                fail(
                    "Test setup error: expected paragraph #p1, linebreak span #lb, and a Selection object."
                );
                return;
            }

            selection.collapse(lineBreakSpan, 0);
            expect(selection.anchorNode).toBe(lineBreakSpan);
            callEnsureCaretNotInsideLineBreakSpan();

            expect(selection.isCollapsed).toBe(true);
            expect(selection.anchorNode).toBe(paragraph);
            expect(selection.anchorOffset).toBe(2);
        });

        it("does nothing when caret is not inside bloom-linebreak span", () => {
            const editable = document.getElementById("simple");
            if (!editable) {
                fail("Test setup error: expected #simple editable element.");
                return;
            }

            editable.innerHTML =
                '<p id="p1">A<span id="lb" class="bloom-linebreak"></span>B</p>';
            const paragraph = document.getElementById("p1");
            const selection = window.getSelection();
            if (!paragraph || !selection) {
                fail(
                    "Test setup error: expected paragraph #p1 and a Selection object."
                );
                return;
            }

            selection.collapse(paragraph, 1);

            expect(selection.isCollapsed).toBe(true);
            expect(selection.anchorNode).toBe(paragraph);
            expect(selection.anchorOffset).toBe(1);

            callEnsureCaretNotInsideLineBreakSpan();

            expect(selection.isCollapsed).toBe(true);
            expect(selection.anchorNode).toBe(paragraph);
            expect(selection.anchorOffset).toBe(1);
        });

        it("does nothing when selection is not collapsed", () => {
            const editable = document.getElementById("simple");
            if (!editable) {
                fail("Test setup error: expected #simple editable element.");
                return;
            }

            editable.innerHTML =
                '<p id="p1">A<span id="lb" class="bloom-linebreak">x</span>B</p>';
            const lineBreakSpan = document.getElementById("lb");
            const selection = window.getSelection();
            if (!lineBreakSpan || !selection) {
                fail(
                    "Test setup error: expected linebreak span #lb and a Selection object."
                );
                return;
            }

            const range = document.createRange();
            range.selectNodeContents(lineBreakSpan);
            selection.removeAllRanges();
            selection.addRange(range);

            expect(selection.anchorNode).toBe(lineBreakSpan);

            callEnsureCaretNotInsideLineBreakSpan();

            expect(selection.isCollapsed).toBe(false);
            expect(selection.anchorNode).toBe(lineBreakSpan);
        });
    });
});

describe("fixPasteData", () => {
    it("fixes google doc fake bold", () => {
        expect(
            BloomField.fixPasteData(
                'Something <b style="font-weight:normal">normal</b> and <b STYLE="font-weight:400">plain</b>'
            )
        ).toBe("Something normal and plain");
    });
    it("removes any other style from bold and converts to strong", () => {
        expect(
            BloomField.fixPasteData(
                '<b style="font-weight:bold">bold</b> and <b STYLE="FONT-WEIGHT:extrabold">bolder</b>'
            )
        ).toBe("<strong>bold</strong> and <strong>bolder</strong>");
    });
    it("changes i to em", () => {
        expect(BloomField.fixPasteData("<i>italic</i>")).toBe(
            "<em>italic</em>"
        );
    });
});

describe("removeUselessSpanMarkup", () => {
    it("removes span with no attributes", () => {
        expect(
            BloomField.removeUselessSpanMarkup(
                "<p><em><span>This is a test.</span></em></p>"
            )
        ).toBe("<p><em>This is a test.</em></p>");
    });
    it("does not remove span with attribute", () => {
        expect(
            BloomField.removeUselessSpanMarkup(
                "<p><span id='s1'>This is a test.</span></p>"
            )
        ).toBe("<p><span id='s1'>This is a test.</span></p>");
    });
    it("handles input without spans", () => {
        expect(
            BloomField.removeUselessSpanMarkup(
                "<p><em>This is a test.</em></p>"
            )
        ).toBe("<p><em>This is a test.</em></p>");
    });
    it("handles span with embedded markup elements", () => {
        expect(
            BloomField.removeUselessSpanMarkup(
                "<p><span>This <strong>is</strong> a <em>test</em>.</span></p>"
            )
        ).toBe("<p>This <strong>is</strong> a <em>test</em>.</p>");
    });
});

describe("normalizeBloomLineBreakSpans", () => {
    it("leaves content unchanged when bloom-linebreak text exists but no span element", () => {
        const input =
            "<p>before  <!-- bloom-linebreak marker -->  after</p><p>         </p>";
        const result = BloomField.normalizeBloomLineBreakSpans(input);
        expect(result).toBe(input);
    });

    it("keeps bloom-linebreak span empty and non-editable", () => {
        const input = "<p>before<span class='bloom-linebreak'></span>after</p>";
        const result = BloomField.normalizeBloomLineBreakSpans(input);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = result;
        const lineBreakSpan = wrapper.querySelector("span.bloom-linebreak");
        expect(lineBreakSpan).toBeTruthy();
        expect(lineBreakSpan?.innerHTML).toBe("");
        expect(lineBreakSpan?.getAttribute("contenteditable")).toBe("false");
    });

    it("moves accidental text out of bloom-linebreak span", () => {
        const input = "<p>A<span class='bloom-linebreak'>Bad text</span>B</p>";
        const result = BloomField.normalizeBloomLineBreakSpans(input);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = result;
        // Should be "<p>ABad text<span class='bloom-linebreak' contenteditable='false'></span>B</p>"
        const lineBreakSpan = wrapper.querySelector("span.bloom-linebreak");
        expect(lineBreakSpan).toBeTruthy();
        expect(lineBreakSpan?.innerHTML).toBe("");
        expect(result.includes("bloom-linebreak'>Bad text")).toBe(false);
        expect(wrapper.textContent).toBe("ABad textB");
    });

    it("moves accidental markup out of bloom-linebreak span", () => {
        const input =
            "<p>A<span class='bloom-linebreak'><em>Bad</em> text</span>B</p>";
        const result = BloomField.normalizeBloomLineBreakSpans(input);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = result;
        // Should be "<p>A<em>Bad</em> text<span class='bloom-linebreak' contenteditable='false'></span>B</p>"
        const lineBreakSpan = wrapper.querySelector("span.bloom-linebreak");
        expect(lineBreakSpan).toBeTruthy();
        expect(lineBreakSpan?.innerHTML).toBe("");
        expect(wrapper.querySelector("span.bloom-linebreak em")).toBeNull();
        expect(wrapper.querySelector("em")?.textContent).toBe("Bad");
        expect(wrapper.textContent).toBe("ABad textB");
    });

    it("behaves decently on nested bloom-linebreak spans", () => {
        const input =
            "<p>A<span id='outer' class='bloom-linebreak'>outer layer text<span id='inner' class='bloom-linebreak'>nested text</span></span>B</p>";
        const result = BloomField.normalizeBloomLineBreakSpans(input);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = result;

        const outer = wrapper.querySelector("#outer") as HTMLSpanElement;
        const inner = wrapper.querySelector("#inner") as HTMLSpanElement;

        expect(outer).toBeTruthy();
        expect(inner).toBeTruthy();
        expect(outer.innerHTML).toBe("");
        expect(inner.innerHTML).toBe("");
        expect(outer.getAttribute("contenteditable")).toBe("false");
        expect(inner.getAttribute("contenteditable")).toBe("false");
        expect(wrapper.textContent).toBe("Aouter layer textnested textB");
    });

    it("fixes the BL-15955 case", () => {
        const bl15955Input =
            '<p><span class="bloom-linebreak">¡Chi maa da kuu in <span style="text-indent: 20pt; text-wrap-mode: initial;">tee ñe\'nu deen, te dani jna\'a ñadi\'i da, chi vetaya deen ña!</span></span></p>';
        const result = BloomField.normalizeBloomLineBreakSpans(bl15955Input);
        const wrapper = document.createElement("div");
        wrapper.innerHTML = result;
        const lineBreakSpan = wrapper.querySelector("span.bloom-linebreak");
        expect(lineBreakSpan).toBeTruthy();
        expect(lineBreakSpan?.innerHTML).toBe("");
        expect(lineBreakSpan?.getAttribute("contenteditable")).toBe("false");
        expect(wrapper.textContent).toBe(
            "¡Chi maa da kuu in tee ñe'nu deen, te dani jna'a ñadi'i da, chi vetaya deen ña!"
        );
        expect(lineBreakSpan?.previousSibling?.textContent).toContain(
            "chi vetaya deen ña!"
        );
    });
});
