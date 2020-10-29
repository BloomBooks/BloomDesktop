import {
    getTestRoot,
    cleanTestRoot,
    removeTestRoot,
    ensureIdsDontExist
} from "../../utils/testHelper";
import {
    Anchor,
    fixUpDownArrowEventHandler,
    ArrowKeyWorkaroundManager
} from "./arrowKeyWorkaroundManager";

const kArrowUp = "ArrowUp";
const kArrowDown = "ArrowDown";
const kNoAudio = "NoAudio";
const kSentence = "Sentence";

describe("ArrowKeyWorkaroundManager Tests", () => {
    // Debugging tip: ArrowKeyWorkaroundManager.printCharPositions(editable) can help you see if the test case is line-wrapping the way you want it to.

    afterAll(() => {
        // Make sure to clean up at the end of all these... in particular, OverflowSpec is affected if testRoot is still there.
        removeTestRoot();
    });

    // Runs a standard test that sends an up or down arrow
    function runArrowKeyTest(
        setup: () => HTMLElement,
        verify: () => void,
        key: "ArrowUp" | "ArrowDown",
        sendShift: boolean = false
    ) {
        setupCleanSlate();

        const elementToSendEventTo = setup();

        sendKeyboardEvent(elementToSendEventTo, key, sendShift);

        verify();
    }

    function runPreventDefaultTest(
        direction: "ArrowUp" | "ArrowDown",
        setup: () => HTMLElement,
        expectation: boolean
    ) {
        setupCleanSlate();

        const element = setup();

        let myEvent: KeyboardEvent | undefined;
        const spySetup = (event: KeyboardEvent) => {
            myEvent = event;
            spyOn(event, "preventDefault");
        };

        // System under test
        sendKeyboardEvent(element, direction, false, spySetup);

        // Verification
        if (expectation) {
            expect(myEvent!.preventDefault).toHaveBeenCalled();
        } else {
            expect(myEvent!.preventDefault).not.toHaveBeenCalled();
        }
    }

    function runScenarioTest(
        key: "ArrowUp" | "ArrowDown",
        scenario: 1 | 2 | 3 | 4 | 5,
        getAnchorNode: () => Node, // Needs to be a function so that it can be deferred until after setup.
        initialOffset: number,
        expectedText: string,
        talkingBookSetting: "NoAudio" | "Sentence"
    ) {
        setupCleanSlate();

        const setup = () => {
            let editable: HTMLElement;
            if (scenario === 1) {
                editable = setupScenario1(talkingBookSetting);
            } else if (scenario === 2) {
                editable = setupScenario2(talkingBookSetting);
            } else if (scenario === 3) {
                editable = setupScenario3(talkingBookSetting);
            } else if (scenario === 4) {
                editable = setupScenario4(talkingBookSetting);
            } else if (scenario === 5) {
                editable = setupScenario5(talkingBookSetting);
            } else {
                throw new Error("Unrecognized scenario: " + scenario);
            }

            // Optional - Make sure the layout got setup properly.
            const styleInfo = window.getComputedStyle(editable);
            expect(styleInfo.flexWrap).toEqual(
                "wrap",
                "Flexwrap not setup properly."
            );
            expect(styleInfo.textAlign).toEqual(
                "center",
                "text-align not setup properly."
            );

            // Uncomment for help in debugging.
            // ArrowKeyWorkaroundManager.printCharPositions(editable);

            setSelectionTo(getAnchorNode(), initialOffset);

            return editable;
        };

        const verify = () => {
            const sel2 = window.getSelection();
            verifySelectionText(sel2, expectedText);
        };

        runArrowKeyTest(setup, verify, key);
    }

    function runScenario1Test(
        key: "ArrowUp" | "ArrowDown",
        getAnchorNode: () => Node, // Needs to be a function so that it can be deferred until after setup.
        initialOffset: number,
        expectedText: string,
        talkingBookSetting: "NoAudio" | "Sentence"
    ) {
        runScenarioTest(
            key,
            1,
            getAnchorNode,
            initialOffset,
            expectedText,
            talkingBookSetting
        );
    }

    function runScenario2Test(
        key: "ArrowUp" | "ArrowDown",
        getAnchorNode: () => Node, // Needs to be a function so that it can be deferred until after setup.
        initialOffset: number,
        expectedText: string,
        talkingBookSetting: "NoAudio" | "Sentence"
    ) {
        runScenarioTest(
            key,
            2,
            getAnchorNode,
            initialOffset,
            expectedText,
            talkingBookSetting
        );
    }

    function sendKeyboardEvent(
        element: HTMLElement,
        key: "ArrowUp" | "ArrowDown",
        sendShift: boolean,
        // If defined, eventSpySetup will be called after keyEvent is created.
        // This gives you an opportunity to attach a spy to it if you wish to observe what happesn to it
        // e.g if a certain function was called on it, etc.
        eventSpySetup?: (keyEvent: KeyboardEvent) => void
    ) {
        element.onkeydown = fixUpDownArrowEventHandler;

        const event = new KeyboardEvent("keydown", {
            key,
            shiftKey: sendShift
        });
        if (eventSpySetup) {
            eventSpySetup(event);
        }

        element.dispatchEvent(event);
    }

    function verifySelection(
        selection: Selection | null,
        expectedNode: Node,
        expectedOffset?: number,
        expectedFocusNode?: Node,
        expectedFocusOffset?: number
    ) {
        expect(selection).not.toBeNull();
        if (selection) {
            expect(selection.anchorNode).toEqual(
                expectedNode,
                "AnchorNode does not match."
            );
            if (expectedOffset !== undefined) {
                expect(selection.anchorOffset).toEqual(
                    expectedOffset,
                    "anchorOffset does not match."
                );
            }

            if (expectedFocusNode) {
                expect(selection.focusNode).toEqual(
                    expectedFocusNode,
                    "FocusNode does not match."
                );
            }
            if (expectedFocusOffset !== undefined) {
                expect(selection.focusOffset).toEqual(
                    expectedFocusOffset,
                    "FocusOffset does not match."
                );
            }
        }
    }

    function verifySelectionText(
        selection: Selection | null,
        expectedTextFromAnchor: string
    ) {
        expect(selection).not.toBeNull();
        if (selection) {
            const text = selection.anchorNode?.textContent;
            const textFromAnchor = text?.substring(selection.anchorOffset);
            expect(textFromAnchor).toEqual(expectedTextFromAnchor);
        }
    }

    function setupScenario1(
        talkingBookSetting: "NoAudio" | "Sentence",
        isFlex: boolean = true
    ) {
        ensureIdsDontExist(["p1", "p2", "s1", "s3a", "s3b", "s4"]);
        const phrase1 = "111111111111";
        const phrase2 = "222.";
        const p1Text = `${phrase1} ${phrase2}`;

        let p1Inner: string;
        let p2Inner: string;
        if (talkingBookSetting === "NoAudio") {
            p1Inner = p1Text;

            p2Inner = "3A3A. 3B3B. 444444444444.";
        } else if (talkingBookSetting === "Sentence") {
            p1Inner = `<span id="s1" class="audio-sentence">${p1Text}</span>`;
            p2Inner = `<span id="s3a" class="audio-sentence">3A3A.</span> <span id="s3b" class="audio-sentence">3B3B.</span> <span id="s4" class="audio-sentence">444444444444.</span>`;
        } else {
            throw new Error(
                "Unrecognized talkingBookSetting: " + talkingBookSetting
            );
        }

        return setupFromParagraphInnerHtml([p1Inner, p2Inner], isFlex);
    }

    function setupScenario2(
        talkingBookSetting: "NoAudio" | "Sentence",
        isFlex: boolean = true
    ) {
        ensureIdsDontExist([
            "p1",
            "p2",
            "s1",
            "s2a",
            "s2b",
            "s3a",
            "s3b",
            "s4"
        ]);

        const phrase1 = "111111111111";
        const phrase2A = "2A2A";
        const phrase2B = "2B2B.";

        let p1Inner: string;
        let p2Inner: string;
        if (talkingBookSetting === "NoAudio") {
            p1Inner = `${phrase1} ${phrase2A} ${phrase2B}.`;
            p2Inner = "3A3A. 3B3B. 444444444444.";
        } else if (talkingBookSetting === "Sentence") {
            p1Inner = `<span id="s1" class="audio-sentence">${phrase1}</span> <span id="s2a" class="audio-sentence">${phrase2A}</span> <span id="s2b" class="audio-sentence">${phrase2B}</span>`;
            p2Inner = `<span id="s3a" class="audio-sentence">3A3A.</span> <span id="s3b" class="audio-sentence">3B3B.</span> <span id="s4" class="audio-sentence">444444444444.</span>`;
        } else {
            throw new Error(
                "Unrecognized talkingBookSetting: " + talkingBookSetting
            );
        }

        return setupFromParagraphInnerHtml([p1Inner, p2Inner], isFlex);
    }

    // Scenario3 is designed to test a case where the selection is initially pointing to an element, rather than a text node
    function setupScenario3(
        talkingBookSetting: "NoAudio" | "Sentence",
        isFlex: boolean = true
    ) {
        ensureIdsDontExist(["p1", "p2", "s1", "s2"]);
        const p1Text = "1111";
        const p2Text = "2222.";

        let p1Inner: string;
        let p2Inner: string;
        if (talkingBookSetting === "NoAudio") {
            p1Inner = p1Text;
            p2Inner = p2Text;
        } else if (talkingBookSetting === "Sentence") {
            p1Inner = `<span id="s1" class="audio-sentence">${p1Text}</span>`;
            p2Inner = `<span id="s2" class="audio-sentence">${p2Text}</span>`;
        } else {
            throw new Error(
                "Unrecognized talkingBookSetting: " + talkingBookSetting
            );
        }

        return setupFromParagraphInnerHtml([p1Inner, p2Inner], isFlex);
    }

    // Scenario 4 tests mousing down into an empty paragraph
    function setupScenario4(talkingBookSetting: "NoAudio" | "Sentence") {
        if (talkingBookSetting !== "NoAudio") {
            throw new Error("Not implemented.");
        }

        ensureIdsDontExist(["p1", "p2", "p3"]);
        const pInnerHtmls = ["1111\n<br>", "<br>", "3333."];

        return setupFromParagraphInnerHtml(pInnerHtmls, true);
    }

    function setupScenario5(talkingBookSetting: "NoAudio" | "Sentence") {
        if (talkingBookSetting !== "NoAudio") {
            throw new Error("Not implemented.");
        }
        ensureIdsDontExist(["p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8"]);
        const pInnerHtmls = [
            "1111", // ends at 66px
            "22222", // last 2 is from 63-72px. The LEFT edge is closer to 66.
            "3333", // ends at 68px
            "4444", // last 4 is from 59-68px. The right edge is closer to 68.
            "555555555555 6666", // ends at 71px
            "777 77 888888888888", // last 7 is from 69-78 px.  The LEFT edge is closer to 71.
            "999999999999 0000", // ends at 71px
            "1111 222222222222" // last 1 is from 61-69 px. The right
        ];

        return setupFromParagraphInnerHtml(pInnerHtmls, true);
    }

    function setupFromParagraphInnerHtml(
        pInnerHtmls: string[],
        isFlex: boolean
    ) {
        // Not sure why we have to copy these styles and they're not getting applied from our styleshseets.
        // I tried to copy the classes / attributes / etc to make it match the selector rules, but it doesn't seem to be applying.
        // So, manually add things like flex-wrap etc.
        const display = isFlex ? " display: flex; flex-wrap: wrap;" : "";
        const editableStyle = `text-align: center;${display}`;
        const pStyle = isFlex ? "flex-basis: 100%; flex-grow: 0;" : "";

        const editableInnerHtml = pInnerHtmls
            .map((innerHtml, index) => {
                const id = `p${index + 1}`;
                return `<p id="${id}" style="${pStyle}">${innerHtml}</p>`;
            })
            .join("");

        const editableHtml = `<div id="div1" class="bloom-editable bloom-visibility-code-on" style="${editableStyle}">${editableInnerHtml}</div>`;
        createImageContainer(editableHtml);
        const editable = document.getElementById("div1")!;
        return editable;
    }

    const setupS1AndMoveTo = (setting, id, i) => {
        const editable = setupScenario1(setting);
        setSelectionTo(getFirstTextNodeOfElement(id)!, i);
        return editable;
    };

    ["NoAudio", "Sentence"].forEach(setting => {
        it(`Given ArrowUp on non-flexbox (setting = ${setting}), does nothing`, () => {
            const setup = () => {
                const editable = setupScenario1(setting as any, false);

                let id;
                if (setting === kNoAudio) {
                    id = "p2";
                } else {
                    id = "s3a";
                }
                const initialAnchorNode = document.getElementById(id)!
                    .firstChild!;
                const initialOffset = 0;
                setSelectionTo(initialAnchorNode, initialOffset);

                return editable;
            };

            const expectation = false;
            runPreventDefaultTest(kArrowUp, setup, expectation);
        });
    });

    // A bunch of utility functions that can be passed directly without needing to create anonymous arrow functions for them over and over
    const getP1 = () => getFirstTextNodeOfElement("p1")!;
    const getP2 = () => getFirstTextNodeOfElement("p2")!;
    const getP3 = () => getFirstTextNodeOfElement("p3")!;
    const getP5 = () => getFirstTextNodeOfElement("p5")!;
    const getP7 = () => getFirstTextNodeOfElement("p7")!;
    const getS1 = () => getFirstTextNodeOfElement("s1")!;
    const getS2a = () => getFirstTextNodeOfElement("s2a")!;
    const getS2b = () => getFirstTextNodeOfElement("s2b")!;
    const getS3a = () => getFirstTextNodeOfElement("s3a")!;
    const getS3b = () => getFirstTextNodeOfElement("s3b")!;

    describe("Given ArrowUp on non talking book", () => {
        const l2Start = 13;

        describe("preventDefault tests", () => {
            // Test that the event has defaultPrevented IFF on boundary line.
            it(`Given ArrowUp on non talking book, cursor on Paragraph 2, Line 1, default behavior is prevented`, () => {
                const setup = () => setupS1AndMoveTo(kNoAudio, "p2", 0);
                const expectedResult = true;
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });

            it(`Given ArrowUp on non talking book, cursor on Paragraph 2, Line 2, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kNoAudio, "p2", 14);
                const expectedResult = false;
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });

            it(`Given ArrowUp on non talking book, cursor on first line of Paragraph 1, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kNoAudio, "p1", 0);
                const expectedResult = false; // because nothing to move to
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });

            it(`Given ArrowUp on non talking book, cursor on last line of Paragraph 1, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kNoAudio, "p1", l2Start);
                const expectedResult = false; // because not on relevant boundary line
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });
        });

        // The text should be aligned center, so if the line below is longer than the line above, multiple values will move to far-left of the line above.
        [0, 1].forEach(offset => {
            it(`Given ArrowUp on non talking book, cursor on Line 3, Span 2, Offset ${offset}, moves to far-left of Paragraph1, Line 2`, () => {
                runScenario1Test(kArrowUp, getP2, offset, "222.", kNoAudio);
            });
        });

        it(`Given ArrowUp on non talking book, cursor on Line 3, Span 2, Offset 4, moves to 2nd char of Paragraph1, Line 2`, () => {
            runScenario1Test(kArrowUp, getP2, 4, "2.", kNoAudio);
        });

        it(`Given ArrowUp on non talking book, cursor on Line 3 on whitespace between spans, moves to position above`, () => {
            runScenario1Test(kArrowUp, getP2, 5, "2.", kNoAudio);
        });

        it(`Given ArrowUp on non talking book, cursor on Line 3, Offset 6, moves to position above`, () => {
            runScenario1Test(kArrowUp, getP2, 6, ".", kNoAudio);
        });

        // These are all to the right of the last character
        [9, 10].forEach(offset => {
            it(`Given ArrowUp on non talking book, cursor on Line 3, Offset ${offset}, moves to position above`, () => {
                runScenario1Test(kArrowUp, getP2, offset, "", kNoAudio);
            });
        });

        describe("Scenario4 (Empty Paragraph) Tests", () => {
            it(`Given ArrowUp on non talking book (Scenario 4), cursor on third line, moves one line up to an empty paragraph`, () => {
                // Setup
                const setup = () => {
                    const editable = setupScenario4("NoAudio");
                    setSelectionTo(getFirstTextNodeOfElement("p3")!, 1);
                    return editable;
                };

                // Verification
                const verify = () => {
                    // There's no text node we can point into (because it just has a <br> element).
                    // Instead, the expected result is that we point it to the corresponding paragraph element
                    const expectedNode = document.getElementById("p2")!;
                    const sel = window.getSelection();
                    verifySelection(sel, expectedNode, 0);
                };

                runArrowKeyTest(setup, verify, "ArrowUp");
            });

            it(`Given ArrowUp on non talking book (Scenario 4), cursor on empty paragraph, moves one line up to next paragraph`, () => {
                // Setup
                const setup = () => {
                    const editable = setupScenario4("NoAudio");
                    setSelectionTo(document.getElementById("p2")!, 0);
                    return editable;
                };

                // Verification
                const verify = () => {
                    const expectedNode = getFirstTextNodeOfElement("p1")!;
                    const sel = window.getSelection();
                    verifySelection(sel, expectedNode);
                };

                runArrowKeyTest(setup, verify, "ArrowUp");
            });
        });
    });

    describe("Given ArrowDown on non talking book", () => {
        const l2Start = 13;

        describe("preventDefault tests", () => {
            // Test that the event has defaultPrevented IFF on boundary line.
            [0, Math.round(l2Start / 2), l2Start - 1].forEach(i => {
                it(`Given ArrowDown on non talking book, cursor on Paragraph 1, Offset ${i}, default not prevented`, () => {
                    const setup = () => setupS1AndMoveTo(kNoAudio, "p1", i);
                    const expectedResult = false;
                    runPreventDefaultTest(kArrowDown, setup, expectedResult);
                });
            });

            [l2Start + 1].forEach(i => {
                it(`Given ArrowDown on non talking book, cursor on Paragraph 1, Offset ${i}, default behavior is prevented`, () => {
                    const setup = () => setupS1AndMoveTo(kNoAudio, "p1", i);
                    const expectedResult = true;
                    runPreventDefaultTest(kArrowDown, setup, expectedResult);
                });
            });

            it(`Given ArrowDown on non talking book, cursor on first line of Paragraph 2, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kNoAudio, "p2", 0);
                const expectedResult = false; // because not on relevant boundary line
                runPreventDefaultTest(kArrowDown, setup, expectedResult);
            });

            it(`Given ArrowDown on non talking book, cursor on last line of Paragraph 2, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kNoAudio, "p2", 14);
                const expectedResult = false; // because nothing to move to
                runPreventDefaultTest(kArrowDown, setup, expectedResult);
            });
        });

        // Offset 0 is valid too, but it's ambiguous whether that's the end of the 1st line or start fo 2nd, so skip testing that
        it(`Given ArrowDown on non talking book, cursor on Line 2, Offset 1, moves one line down to Line 3`, () => {
            const i = l2Start + 1;
            const expected = "A. 3B3B. 444444444444.";
            runScenario1Test(kArrowDown, getP1, i, expected, kNoAudio);
        });

        it(`Given ArrowDown on non talking book, cursor on Line 2, Offset 4, moves one line down to Line 3`, () => {
            const i = l2Start + 4;
            const expected = "B3B. 444444444444.";
            runScenario1Test(kArrowDown, getP1, i, expected, kNoAudio);
        });

        describe("Scenario4 (Empty Paragraph) Tests", () => {
            it(`Given ArrowDown on non talking book (Scenario 4), cursor on first line, moves one line down to an empty paragraph`, () => {
                // Setup
                const setup = () => {
                    const editable = setupScenario4("NoAudio");
                    setSelectionTo(getP1()!, 1);
                    return editable;
                };

                // Verification
                const verify = () => {
                    // There's no text node we can point into (because it just has a <br> element).
                    // Instead, the expected result is that we point it to the corresponding paragraph element
                    const expectedNode = document.getElementById("p2")!;
                    const sel = window.getSelection();
                    verifySelection(sel, expectedNode, 0);
                };

                runArrowKeyTest(setup, verify, "ArrowDown");
            });

            it(`Given ArrowDown on non talking book (Scenario 4), cursor on empty paragraph, moves one line down to next paragraph`, () => {
                // Setup
                const setup = () => {
                    const editable = setupScenario4("NoAudio");
                    setSelectionTo(document.getElementById("p2")!, 0);
                    return editable;
                };

                // Verification
                const verify = () => {
                    const expectedNode = getFirstTextNodeOfElement("p3")!;
                    const sel = window.getSelection();
                    verifySelection(sel, expectedNode);
                };

                runArrowKeyTest(setup, verify, "ArrowDown");
            });
        });

        describe("Scenario5 (end of line) tests", () => {
            // ENHANCE: Corresponding ArrowUp tests might be nice, but it's less tricky than this case.
            it(`Given ArrowDown on non talking book, Scenario 5, cursor on Paragraph 1 end, moves down to LEFT of last char`, () => {
                const i = 4; // To the right of the last char
                const expected = "2"; // The left edge of the last char of line 2 is closer than its right edge, so this char should be included.
                runScenarioTest(kArrowDown, 5, getP1, i, expected, kNoAudio);
            });

            it(`Given ArrowDown on non talking book, Scenario 5, cursor on Paragraph 3 end, moves down to RIGHT of last char`, () => {
                const i = 4; // To the right of the last char
                const expected = ""; // The right edge of the last char of line 4 is closer than its left edge, so this char should not be included.
                runScenarioTest(kArrowDown, 5, getP3, i, expected, kNoAudio);
            });

            it(`Given ArrowDown on non talking book, Scenario 5, cursor on Paragraph 5 end, moves down to LEFT of last char`, () => {
                const i = 17; // To the right of the last char
                const expected = "7 888888888888"; // The left edge of the last char of line 6 is closer than its right edge, so this char should be included.
                runScenarioTest(kArrowDown, 5, getP5, i, expected, kNoAudio);
            });

            it(`Given ArrowDown on non talking book, Scenario 5, cursor on Paragraph 7 end, moves down to LEFT of last char`, () => {
                const i = 17; // To the right of the last char
                const expected = " 222222222222"; // The left edge of the last char of line 6 is closer than its right edge, so this char should not be included.
                runScenarioTest(kArrowDown, 5, getP7, i, expected, kNoAudio);
            });
        });
    });

    describe("Given ArrowUp on TalkingBookSentenceSplit", () => {
        describe("preventDefault tests", () => {
            const l2Start = 13;

            // Test that the event has defaultPrevented IFF on boundary line.
            it(`Given ArrowUp on TalkingBookSentenceSplit, cursor on Paragraph 2, Line 1, default behavior is prevented`, () => {
                const setup = () => setupS1AndMoveTo(kSentence, "s3a", 0);
                const expectedResult = true;
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit, cursor on Paragraph 2, Line 2, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kSentence, "s4", 1);
                const expectedResult = false;
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit, cursor on first line of Paragraph 1, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kSentence, "s1", 0);
                const expectedResult = false; // because nothing to move to
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit, cursor on last line of Paragraph 1, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kSentence, "s1", l2Start);
                const expectedResult = false; // because not on relevant boundary line
                runPreventDefaultTest(kArrowUp, setup, expectedResult);
            });
        });

        describe("Scenario1 onBoundary tests", () => {
            // These are both to the first of the first character of the line above
            [0, 2].forEach(i => {
                it(`Given ArrowUp on TalkingBookSentenceSplit Scenario1, cursor on Line 3, Span A, Offset ${i}, moves to far-left of Paragraph1, Line 2`, () => {
                    runScenario1Test(kArrowUp, getS3a, i, "222.", kSentence);
                });
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario1, cursor on Line 3, Span 3A, Offset 4, moves to 2nd char of Paragraph1, Line 2`, () => {
                runScenario1Test(kArrowUp, getS3a, 4, "2.", kSentence);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario1, cursor on Line 3 on whitespace between spans, moves to position above`, () => {
                const getNode = () =>
                    document.getElementById("p2")!.childNodes[1];
                runScenario1Test(kArrowUp, getNode, 0, "2.", kSentence);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario1, cursor on Line 3, Span 3B, Offset 0, moves to position above`, () => {
                runScenario1Test(kArrowUp, getS3b, 0, ".", kSentence);
            });

            // These are both to the right of the last character of the line above
            [1, 4].forEach(i => {
                it(`Given ArrowUp on TalkingBookSentenceSplit Scenario1, cursor on Line 3, Span 3B, Offset ${i}, moves to position above`, () => {
                    runScenario1Test(kArrowUp, getS3b, i, "", kSentence);
                });
            });
        });

        describe("Scenario2 onBoundary tests", () => {
            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario2, cursor on Line 3, Span 3A start, moves one line up`, () => {
                runScenario2Test(kArrowUp, getS3a, 0, "2A2A", kSentence);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario2, cursor on Line 3, Span 3A End-1, moves one line up`, () => {
                runScenario2Test(kArrowUp, getS3a, 3, "A", kSentence);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario2, cursor on Line 3 on whitespace between spans, moves one line up`, () => {
                const getNode = () =>
                    document.getElementById("p2")!.childNodes[1];
                runScenario2Test(kArrowUp, getNode, 0, "2B2B.", kSentence);
            });

            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario2, cursor on Line 3, Span 3B start, moves to position above`, () => {
                runScenario2Test(kArrowUp, getS3b, 0, "B2B.", kSentence);
            });

            // These are both to the right of the last character of the line above
            it(`Given ArrowUp on TalkingBookSentenceSplit Scenario2, cursor on Line 3, Span 3B end, moves to position above`, () => {
                runScenario2Test(kArrowUp, getS3b, 4, "", kSentence);
            });
        });
    });

    describe("Given ArrowDown on TalkingBookSentenceSplit", () => {
        const l2Start = 13;

        describe("preventDefault tests", () => {
            // Test that the event has defaultPrevented IFF on boundary line.
            [0, Math.round(l2Start / 2), l2Start - 1].forEach(i => {
                it(`Given ArrowDown on TalkingBookSentenceSplit, cursor on Paragraph 1, Line 1, default not prevented`, () => {
                    const setup = () => setupS1AndMoveTo(kSentence, "s1", i);
                    const expectedResult = false;
                    runPreventDefaultTest(kArrowDown, setup, expectedResult);
                });
            });

            [l2Start + 1].forEach(i => {
                it(`Given ArrowDown on TalkingBookSentenceSplit, cursor on Paragraph 1, Line 2, default behavior is prevented`, () => {
                    const setup = () => setupS1AndMoveTo(kSentence, "s1", i);
                    const expectedResult = true;
                    runPreventDefaultTest(kArrowDown, setup, expectedResult);
                });
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit, cursor on first line of Paragraph 2, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kSentence, "s3a", 0);
                const expectedResult = false; // because not on relevant boundary line
                runPreventDefaultTest(kArrowDown, setup, expectedResult);
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit, cursor on last line of Paragraph 2, default not prevented`, () => {
                const setup = () => setupS1AndMoveTo(kSentence, "s4", 1);
                const expectedResult = false; // because nothing to move to
                runPreventDefaultTest(kArrowDown, setup, expectedResult);
            });
        });

        describe("Scenario1 onBoundary tests", () => {
            // Offset 0 is valid too, but it's ambiguous whether that's the end of the 1st line or start fo 2nd, so skip testing that
            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario1, cursor on Line 2, Offset 1, moves one line down to Line 3`, () => {
                const i = l2Start + 1;
                const expected = "A.";
                runScenario1Test(kArrowDown, getS1, i, expected, kSentence);
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario1, cursor on Line 2, Offset 4, moves one line down to Line 3`, () => {
                const i = l2Start + 4;
                const expected = "B3B.";
                runScenario1Test(kArrowDown, getS1, i, expected, kSentence);
            });
        });

        describe("Scenario2 onBoundary tests", () => {
            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario2, cursor on Line 2, Span 2A start, moves one line down to Line 3`, () => {
                const expected = "3A3A.";
                runScenario2Test(kArrowDown, getS2a, 0, expected, kSentence);
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario2, cursor on Line 2, Span 2A end, moves one line down to Line 3`, () => {
                const expected = "A.";
                runScenario2Test(kArrowDown, getS2a, 3, expected, kSentence);
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario2, cursor on Line 2, whitespace between S2A and S2B, moves one line down to Line 3`, () => {
                const getNode = () => {
                    // 0 = span1
                    // 1 = whitespace
                    // 2 = span2a
                    // 3 = whitespace
                    return document.getElementById("p1")!.childNodes[3];
                };
                const expected = " "; // The space between spans 3A and 3B
                runScenario2Test(kArrowDown, getNode, 0, expected, kSentence);
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario2, cursor on Line 2, Span 2B start, moves one line down to Line 3`, () => {
                const expected = " "; // The space between spans 3A and 3B
                runScenario2Test(kArrowDown, getS2b, 0, expected, kSentence);
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario2, cursor on Line 2, Span 2B middle, moves one line down to Line 3`, () => {
                const expected = "B3B.";
                runScenario2Test(kArrowDown, getS2b, 2, expected, kSentence);
            });

            it(`Given ArrowDown on TalkingBookSentenceSplit Scenario2, cursor on Line 2, Span 2B end, moves one line down to Line 3`, () => {
                const expected = "B.";
                runScenario2Test(kArrowDown, getS2b, 4, expected, kSentence);
            });
        });

        describe("Scenario3 onBoundary tests", () => {
            it(`Given ArrowDown on ElementNode, moves one line down`, () => {
                const getNode = () => document.getElementById("p1")!;
                const expected = "2222.";
                runScenarioTest(kArrowDown, 3, getNode, 0, expected, kSentence);
            });
        });
    });

    describe("Selection tests", () => {
        const sendShift = true;

        it("Given Shift+ArrowDown on boundary, highlights text to the point one line down", () => {
            const setup = () => setupS1AndMoveTo(kSentence, "s1", 15); // 2nd line of span 1
            const verify = () => {
                const sel = window.getSelection();
                verifySelection(sel, getS1(), 15, getS3b(), 0);
            };
            runArrowKeyTest(setup, verify, "ArrowDown", sendShift);
        });

        it("Given Shift+ArrowDown when already selected, highlights text to the point one line down", () => {
            const setup = () => {
                const editable = setupScenario1(kSentence);

                // Reminder: the default event doesn't move anything in the unit tests.
                //   So this was specifically selected so that the focusNode (end of the current selection)
                //   is on a boundary line
                const s1 = getS1();
                setSelectionTo(s1, 0, s1, 15);

                return editable;
            };
            const verify = () => {
                const sel = window.getSelection();
                verifySelection(sel, getS1(), 0, getS3b(), 0);
            };
            runArrowKeyTest(setup, verify, "ArrowDown", sendShift);
        });
    });
});

describe("Anchor Tests", () => {
    it("Given paragraph with no spans and cursor at start, returns offset as indexFromStart", () => {
        setupCleanSlate();

        const html = '<div class="bloom-editable"><p id="p1">S1. S2.</p></div>';
        setupElementFromHtml(html);
        const paragraph = document.getElementById("p1")!;
        const anchorNode = paragraph.firstChild!;
        const anchor = new Anchor(anchorNode, 0);

        const result = anchor.convertToIndexFromStart(paragraph);

        expect(result).toBe(0);
    });

    it("Given paragraph with no spans, returns offset as indexFromStart", () => {
        setupCleanSlate();

        const html = '<div class="bloom-editable"><p id="p1">S1. S2.</p></div>';
        setupElementFromHtml(html);
        const paragraph = document.getElementById("p1")!;
        const anchorNode = paragraph.firstChild!;
        const anchor = new Anchor(anchorNode, 5);

        const result = anchor.convertToIndexFromStart(paragraph);

        expect(result).toBe(5);
    });

    it("Given 2nd span inside paragraph, calculates indexFromStart correctly", () => {
        setupCleanSlate();

        const html =
            '<div class="bloom-editable"><p id="p1"><span id="s1">S1.</span> <span id="s2">S2.</span></p></div>';
        setupElementFromHtml(html);
        const paragraph = document.getElementById("p1")!;
        const anchorNode = document.getElementById("s2")!.firstChild!;
        const anchor = new Anchor(anchorNode, 0);

        const result = anchor.convertToIndexFromStart(paragraph);

        expect(result).toBe(4);
    });
});

// Restores to a blank state
// Ideally should be run before the unit tests, so that previosu HTML (including things from other test suites) are removed.
function setupCleanSlate() {
    cleanTestRoot();
}

function setupElementFromHtml(html: string) {
    const root = getTestRoot();
    root.innerHTML = html;
}

function createImageContainer(
    editableHtml: string,
    left = 0,
    top = 0,
    width = 1000,
    height = 1000
) {
    // For test purposes, we set the image container position using absolute so that they can be manually positioned in such a way to produce easy numbers to work with.
    const textOverPicHtml = `<div class="bloom-textOverPicture" data-bubble="{\'style\':\'caption\'}" style="left: 0%; top: 0%; width: 10%; height: 20%; position: absolute;">${editableHtml}</div>`;
    const html = `<div class="bloom-page customPage"><div class="bloom-imageContainer" style="width: ${width}px; height: ${height}px; left: ${left}px; top: ${top}px; position: absolute;">${textOverPicHtml}</div></div>`;
    setupElementFromHtml(html);
}

function setSelectionTo(
    startNode: Node,
    startOffset: number,
    endNode?: Node,
    endOffset?: number
) {
    const sel1 = window.getSelection()!;
    sel1.setBaseAndExtent(
        startNode,
        startOffset,
        endNode ?? startNode,
        endOffset ?? startOffset
    );
}

function getFirstTextNodeOfElement(id: string) {
    return document.getElementById(id)!.firstChild;
}
