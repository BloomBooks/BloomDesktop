import TalkingBookTool from "./talkingBook";
import {
    theOneAudioRecorder,
    initializeTalkingBookToolAsync,
    RecordingMode,
    AudioMode,
    getAllAudioModes
} from "./audioRecording";
import {
    SetupTalkingBookUIElements,
    SetupIFrameAsync,
    SetupIFrameFromHtml,
    getFrameElementById,
    StripPlayerSrcNoCacheSuffix,
    StripRecordingMd5
} from "./audioRecordingSpec";
import * as XRegExp from "xregexp"; // Not sure why, but import * as XRegExp works better. import XRegExp causes "xregexp_1.default is undefined" error
import { setSentenceEndingPunctuationForBloom } from "../readers/libSynphony/bloom_xregexp_categories";
import axios from "axios";

describe("talking book tests", () => {
    beforeAll(async () => {
        SetupTalkingBookUIElements();
        await SetupIFrameAsync();
        await initializeTalkingBookToolAsync();
    });

    describe("- de/enshroudPhraseMarkes", () => {
        it("enshroud/deshroud phrase markers", () => {
            // Setup Initial HTML
            const textBox1 =
                '<div class="bloom-editable" id="div1"><p><span id="1.1" class="audio-sentence ui-audioCurrent">This is a test,| this is only a test.</span></p></div>';
            const textBox2 =
                '<div class="bloom-editable" id="div2"><p><span id="2.1" class="audio-sentence">This test is silly | but that is okay.</span></p></div>';
            SetupIFrameFromHtml(`<div id='page1'>${textBox1}${textBox2}</div>`);
            const page = getFrameElementById("page", "page1");
            expect(
                page?.getElementsByClassName("bloom-audio-split-marker")
                    .length ?? 0
            ).toBe(0);
            TalkingBookTool.enshroudPhraseDelimiters(page);
            expect(
                page?.getElementsByClassName("bloom-audio-split-marker").length
            ).toBe(2);
            TalkingBookTool.deshroudPhraseDelimiters(page);
            expect(
                page?.getElementsByClassName("bloom-audio-split-marker")
                    .length ?? 0
            ).toBe(0);
        });
    });

    describe("- updateMarkup()", () => {
        it("moves highlight after focus changes", async () => {
            // Setup Initial HTML
            const textBox1 =
                '<div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" id="div1"><p><span id="1.1" class="audio-sentence ui-audioCurrent">1.1</span></p></div></div>';
            const textBox2 =
                '<div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" id="div2"><p><span id="2.1" class="audio-sentence">2.1</span></p></div></div>';
            SetupIFrameFromHtml(`<div id='page1'>${textBox1}${textBox2}</div>`);

            // Setup talking book tool
            const tbTool = new TalkingBookTool();
            await tbTool.showTool();

            // Simulate a keypress on a different div
            const div2Element = theOneAudioRecorder
                .getPageDocBody()
                ?.querySelector("#div2") as HTMLElement;
            div2Element.tabIndex = -1; // focus() won't work if no tabindex.
            div2Element.focus();

            // System under test - TalkingBook.updateMarkupAsync() is called after typing
            const doUpdate = await tbTool.updateMarkupAsync();
            doUpdate();

            // Verification
            const currentTextBox = theOneAudioRecorder.getCurrentTextBox();
            const currentId = currentTextBox?.getAttribute("id");
            expect(currentId).toBe("div2");
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
            spyOn(axios, "get").and.callFake((url: string) => {
                if (
                    url.includes("/bloom/api/audio/checkForAllRecording?ids=")
                ) {
                    return Promise.resolve({ data: false });
                } else {
                    return Promise.reject(new Error("Fake 404 Error"));
                }
            });
        }

        function setAudioFilesPartiallyPresent(scenario: AudioMode) {
            // Mark that the recording exists.
            // FYI - spies only last for the scope of the "describe" or "it" block in which it was defined.
            let anyPresent: string[] = [];
            let allPresent: string[] = [];
            switch (scenario) {
                case AudioMode.PureSentence:
                case AudioMode.PreTextBox:
                case AudioMode.HardSplitTextBox: {
                    anyPresent = [
                        "ids=1.2",
                        "ids=2.2",
                        "ids=1.1,1.2",
                        "ids=2.1,2.2"
                    ];
                    allPresent = ["ids=1.2", "ids=2.2"];
                    break;
                }
                case AudioMode.PureTextBox:
                case AudioMode.SoftSplitTextBox: {
                    throw new Error(`invalid scenario: ${AudioMode[scenario]}`);
                }
                default: {
                    throw new Error(
                        `Unhandled scenario: ${scenario} (${AudioMode[scenario]})`
                    );
                }
            }

            const urlToResponse = {};
            anyPresent.forEach(params => {
                const url = "/bloom/api/audio/checkForAnyRecording?" + params;
                urlToResponse[url] = Promise.resolve();
            });
            allPresent.forEach(params => {
                const url = "/bloom/api/audio/checkForAllRecording?" + params;
                urlToResponse[url] = Promise.resolve({ data: true });
            });
            spyOn(axios, "get").and.callFake((url: string) => {
                const response = urlToResponse[url];
                if (response) {
                    return response;
                } else {
                    return Promise.reject("Fake 404 error.");
                }
            });
        }

        function getTestMd5s(
            md5ValueSetting: string,
            scenario: AudioMode
        ): string[] {
            if (md5ValueSetting === "missing") {
                return ["undefined", "undefined", "undefined", "undefined"];
            } else if (md5ValueSetting === "differs") {
                return ["001", "002", "003", "004"];
            } else if (md5ValueSetting === "same") {
                switch (scenario) {
                    case AudioMode.PureSentence:
                    case AudioMode.PreTextBox:
                    case AudioMode.HardSplitTextBox: {
                        return [
                            "28e9f104eacb2cf2afe90c7074733103",
                            "4aaf751627dd525fb2a5ebb7928ff353",
                            "3de477f0b8b44a793a313b344d6687d6",
                            "b82352b9566d3bf74be60fb2d1a81a7b"
                        ];
                    }
                    case AudioMode.PureTextBox:
                    case AudioMode.SoftSplitTextBox: {
                        return [
                            "b65fad901ce36fb8251f21f9aca8e2d0",
                            "ad15831c388fb93285cbb18306a4b734"
                        ];
                    }
                    default: {
                        throw new Error("Unknown scenario: " + scenario);
                    }
                }
            } else {
                throw new Error("Unknown checksum setting: " + md5ValueSetting);
            }
        }

        function getPureSentenceModeHtml(checksums: string[]): string {
            const div1Html = `<div class="bloom-editable bloom-visibility-code-on" id="div1" data-audioRecordingMode="Sentence"><p><span id="1.1" class="audio-sentence ui-audioCurrent" recordingmd5="${checksums[0]}">Sentence 1.1၊</span> <span id="1.2" class="audio-sentence" recordingmd5="${checksums[1]}">Sentence 1.2</span></p></div>`;
            const div2Html = `<div class="bloom-editable bloom-visibility-code-on" id="div2" data-audioRecordingMode="Sentence" recordingmd5=""><p><span id="2.1" class="audio-sentence" recordingmd5="${checksums[2]}">Sentence 2.1၊</span> <span id="2.2" class="audio-sentence" recordingmd5="${checksums[3]}">Sentence 2.2</span></p></div>`;
            return `${div1Html}${div2Html}`;
        }

        function getPreTextBoxModeHtml(checksums: string[]): string {
            const div1Html = `<div class="bloom-editable bloom-visibility-code-on ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox"><p><span id="1.1" class="audio-sentence" recordingmd5="${checksums[0]}">Sentence 1.1၊</span> <span id="1.2" class="audio-sentence" recordingmd5="${checksums[1]}">Sentence 1.2</span></p></div>`;
            const div2Html = `<div class="bloom-editable bloom-visibility-code-on" id="div2" data-audiorecordingmode="TextBox"><p><span id="2.1" class="audio-sentence" recordingmd5="${checksums[2]}">Sentence 2.1၊</span> <span id="2.2" class="audio-sentence" recordingmd5="${checksums[3]}">Sentence 2.2</span></p></div>`;
            return `${div1Html}${div2Html}`;
        }

        function setupPureTextBoxModeHtml(checksums: string[]): string {
            const div1Html = `<div class="bloom-editable bloom-visibility-code-on audio-sentence ui-audioCurrent" id="div1" data-audiorecordingmode="TextBox" recordingmd5="${checksums[0]}"><p>Sentence 1.1၊ Sentence 1.2</p></div>`;
            const div2Html = `<div class="bloom-editable bloom-visibility-code-on audio-sentence" id="div2" data-audiorecordingmode="TextBox" recordingmd5="${checksums[1]}"><p>Sentence 2.1၊ Sentence 2.2</p></div>`;
            return `${div1Html}${div2Html}`;
        }

        function getTextBoxHardSplitHtml(checksums: string[]): string {
            let html = "";
            let checksumIndex = 0;
            for (let i = 1; i <= 2; ++i) {
                // FYI: Yes, it is confirmed that in hardSplit, ui-audioCurrent goes on the div, not the span.
                const divStartHtml = `<div class="bloom-editable bloom-visibility-code-on${
                    i === 1 ? " ui-audioCurrent" : ""
                }" id="div${i}" data-audiorecordingmode="TextBox">`;
                const divInnerHtml = `<p><span id="${i}.1" class="audio-sentence" recordingmd5="${
                    checksums[checksumIndex++]
                }">Sentence ${i}.1၊</span> <span id="${i}.2" class="audio-sentence" recordingmd5="${
                    checksums[checksumIndex++]
                }">Sentence ${i}.2</span></p>`;
                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                html += divHtml;
            }
            return html;
        }

        function getTextBoxSoftSplitHtml(checksums: string[]): string {
            let html = "";
            for (let i = 1; i <= 2; ++i) {
                const divStartHtml = `<div class="bloom-editable bloom-visibility-code-on audio-sentence${
                    i === 1 ? " ui-audioCurrent" : ""
                }" id="div${i}" data-audiorecordingmode="TextBox" recordingmd5="${
                    checksums[i - 1]
                }" data-audiorecordingendtimes="1.0 2.0">`;
                const divInnerHtml = `<p><span id="${i}.1" class="bloom-highlightSegment">Sentence ${i}.1၊</span> <span id="${i}.2" class="bloom-highlightSegment">Sentence ${i}.2</span></p>`;

                const divHtml = `${divStartHtml}${divInnerHtml}</div>`;
                html += divHtml;
            }
            return html;
        }

        let originalDiv1Html = "";
        let originalDiv2Html = "";

        function setupSentenceSplitTest(
            scenario: AudioMode,
            checksumSetting: string,
            areRecordingsPresent: string
        ): void {
            const checksums = getTestMd5s(checksumSetting, scenario);

            let innerHtml: string = "";
            switch (scenario) {
                case AudioMode.PureSentence: {
                    innerHtml = getPureSentenceModeHtml(checksums);
                    break;
                }
                case AudioMode.PreTextBox: {
                    innerHtml = getPreTextBoxModeHtml(checksums);
                    break;
                }
                case AudioMode.PureTextBox: {
                    innerHtml = setupPureTextBoxModeHtml(checksums);
                    break;
                }
                case AudioMode.HardSplitTextBox: {
                    innerHtml = getTextBoxHardSplitHtml(checksums);
                    break;
                }
                case AudioMode.SoftSplitTextBox: {
                    innerHtml = getTextBoxSoftSplitHtml(checksums);
                    break;
                }
                default: {
                    throw new Error(
                        `Unhandled scenario: ${scenario} (${AudioMode[scenario]})`
                    );
                }
            }

            SetupIFrameFromHtml(
                `<div id="page1"><div class="bloom-translationGroup">${innerHtml}</div></div>`
            );

            if (scenario === AudioMode.PureSentence) {
                theOneAudioRecorder.recordingMode = RecordingMode.Sentence;
            } else {
                theOneAudioRecorder.recordingMode = RecordingMode.TextBox;
            }

            originalDiv1Html = getFrameElementById("page", "div1")
                ?.outerHTML as string;
            originalDiv2Html = getFrameElementById("page", "div2")
                ?.outerHTML as string;

            if (areRecordingsPresent === "present") {
                setAllAudioFilesPresent();
            } else if (areRecordingsPresent === "partiallyPresent") {
                setAudioFilesPartiallyPresent(scenario);
            } else if (areRecordingsPresent === "missing") {
                setAudioFilesDontExist();
            } else {
                throw new Error(
                    "Unhandled value of areRecordingsPresent: " +
                        areRecordingsPresent
                );
            }
        }

        async function runShowToolAsync(): Promise<void> {
            const tbTool = new TalkingBookTool();
            await tbTool.showTool();
            await tbTool.newPageReady();
        }

        function verifyCommonAspects(scenario: AudioMode) {
            verifyCurrentHighlight(scenario);
            verifySoundForShowToolTests(scenario);
            verifyRecordButtonEnabled(); // Check that we didn't accidentally disable all the toolbox buttons.
        }

        function verifySplitsUpdated(scenario: AudioMode) {
            verifyHtmlUpdated(scenario);
            verifyCommonAspects(scenario);
        }

        function verifySplitsPreserved(scenario: AudioMode) {
            verifyHtmlPreserved(scenario);
            verifyCommonAspects(scenario);
        }

        function verifyHtmlUpdated(scenario: AudioMode) {
            if (
                scenario === AudioMode.PureSentence ||
                scenario === AudioMode.PreTextBox ||
                scenario === AudioMode.HardSplitTextBox
            ) {
                for (let i = 1; i <= 2; ++i) {
                    const spans = getAudioSentenceSpans(`div${i}`);

                    expect(spans).toHaveLength(1);
                    expect(spans[0]).toHaveText(
                        `Sentence ${i}.1၊ Sentence ${i}.2`
                    );
                }
            } else if (scenario === AudioMode.PureTextBox) {
                // There's actually nothing that needs to be updated if it's in TextBox mode.
                // So you should actually get the same result as when you preserve it.
                verifyHtmlPreserved(scenario);
            } else if (scenario === AudioMode.SoftSplitTextBox) {
                // TODO: Maybe now is the time to try changing it.
                //
                // SoftSplit test doesn't have very intuitive results.
                // Historically, it was actually the case that upon Soft Split, it never updated the markup.
                // (That's because the playback mode was identified as text box, and in that case, it never actually runs makeAudioSentenceElementsLeaf())
                // I'm not sure if that was entirely intentional, but it does seem very problematic to risk increasing/decreasing the number of splits while an audio is aligned to it.
                // Now that we do check if it's safe..
                // I guesss now we COULD now update the markup, but it doesn't seem worth the time, complexity, or risk to add that functionality.
                // I guess it's more like if it's not safe, then we do whatever the old behavior was... which in this case, is to not do anything.
                verifyHtmlPreserved(scenario);
            } else {
                throw new Error(
                    `Unhandled scenario: ${scenario} (${AudioMode[scenario]})`
                );
            }
        }

        function verifyHtmlPreserved(_scenario: AudioMode) {
            const currentHtml1 = getFrameElementById("page", "div1")
                ?.outerHTML as string;
            expect(StripRecordingMd5(currentHtml1)).toBe(
                StripRecordingMd5(originalDiv1Html)
            );
            const currentHtml2 = getFrameElementById("page", "div2")
                ?.outerHTML as string;
            expect(StripRecordingMd5(currentHtml2)).toBe(
                StripRecordingMd5(originalDiv2Html)
            );
        }

        function expectMd5(id, expectedMd5) {
            const elem = getFrameElementById("page", id);
            expect(elem).not.toBeNull(`Could not find element "${id}"`);
            if (elem) {
                const md5 = elem.getAttribute("recordingmd5");

                if (md5 === null && expectedMd5 === "undefined") {
                    // Not having recordingmd5 is considered an acceptable result for this expectation.
                    // Just return without performing any more expectations
                    return;
                }

                expect(md5).toBe(
                    expectedMd5,
                    `recordingmd5 does not match for element: ${elem.outerHTML}`
                );
            }
        }

        function verifyCurrentHighlight(scenario: AudioMode) {
            const div = getFrameElementById("page", "div1");
            if (!div) {
                expect(div).not.toBeNull("div1 is null");
                return;
            }

            const page1 = getFrameElementById("page", "page1");
            const numCurrents = page1?.querySelectorAll(".ui-audioCurrent")
                .length;
            expect(numCurrents).toBe(
                1,
                "Only 1 item is allowed to be the current: " + page1?.innerHTML
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
                    throw new Error(
                        `Unhandled scenario: ${scenario} (${AudioMode[scenario]})`
                    );
                }
            }
        }

        function verifySoundForShowToolTests(scenario: AudioMode): void {
            verifySound(scenario, "div1", "1.1");
        }

        // Checks that the audio player's src corresponds to the ID of either the first div or the first span (depending on the scenario)
        function verifySound(
            scenario: AudioMode,
            firstDivId: string,
            firstSpanId: string
        ): void {
            const player = document.getElementById(
                "player"
            ) as HTMLMediaElement | null;
            if (!player) {
                expect(player).not.toBeNull("player is null");
                return;
            }

            let expectedSrc: string = "";
            switch (scenario) {
                case AudioMode.PureSentence:
                case AudioMode.PreTextBox:
                case AudioMode.HardSplitTextBox: {
                    expectedSrc = firstSpanId;
                    break;
                }

                case AudioMode.PureTextBox:
                case AudioMode.SoftSplitTextBox: {
                    expectedSrc = firstDivId;
                    break;
                }

                default: {
                    throw new Error("Unhandled scenario: " + scenario);
                }
            }

            expect(StripPlayerSrcNoCacheSuffix(player.src)).toBe(
                `http://localhost:9876/bloom/api/audio/wavFile?id=audio/${expectedSrc}.wav`
            );
        }

        function verifyChecksumsUpToDate(
            scenario: AudioMode,
            splitUpdateSetting: string
        ) {
            const expectedMd5s = getTestMd5s("same", scenario);
            if (
                scenario === AudioMode.PureSentence ||
                scenario === AudioMode.PreTextBox ||
                scenario === AudioMode.HardSplitTextBox
            ) {
                expectMd5("1.1", expectedMd5s[0]);
                expectMd5("2.1", expectedMd5s[2]);

                if (splitUpdateSetting !== "updated") {
                    // If the splits are updated in this test scenario,
                    // then it will be down to just one sentence each.
                    // These two won't exist. And that's not worrisome.
                    expectMd5("1.2", expectedMd5s[1]);
                    expectMd5("2.2", expectedMd5s[3]);
                }
            } else {
                expectMd5("div1", expectedMd5s[0]);
                expectMd5("div2", expectedMd5s[1]);
            }
        }

        function verifyChecksumsNotUpdated(
            scenario: AudioMode,
            splitUpdateSetting: string
        ) {
            if (
                scenario === AudioMode.PureSentence ||
                scenario === AudioMode.PreTextBox ||
                scenario === AudioMode.HardSplitTextBox
            ) {
                expectMd5("1.1", "undefined");
                expectMd5("2.1", "undefined");

                if (splitUpdateSetting === "preserved") {
                    expectMd5("1.2", "undefined");
                    expectMd5("2.2", "undefined");
                } else if (splitUpdateSetting === "updated") {
                    // Don't need to check 1.2 and 2.2 in this case,
                    // because it's been updated down to just 1 span.
                } else {
                    throw new Error(
                        "Unhandled splitUpdateSetting: " + splitUpdateSetting
                    );
                }
            } else {
                expectMd5("div1", "undefined");
                expectMd5("div2", "undefined");
            }
        }

        function verifyCheckumsStillDiffers(scenario: AudioMode) {
            // Verify that it matches the original value and hasn't been updated
            const expectedMd5s = getTestMd5s("differs", scenario);
            if (
                scenario === AudioMode.PureSentence ||
                scenario === AudioMode.PreTextBox ||
                scenario === AudioMode.HardSplitTextBox
            ) {
                // Expecting differs -> update, which means that there will only be one sentence left. Only need to check the 1st one.
                expectMd5("1.1", expectedMd5s[0]);
                expectMd5("2.1", expectedMd5s[2]);
            } else {
                expectMd5("div1", expectedMd5s[0]);
                expectMd5("div2", expectedMd5s[1]);
            }
        }

        describe("showTool(checksum=missing, audio=missing, scenario=*) => UPDATE", () => {
            getAllAudioModes().forEach(scenario => {
                const scenarioName = AudioMode[scenario];
                it(`showTool(checksum=missing, audio=missing, scenario=${scenarioName}) => UPDATE`, async () => {
                    return runNoChecksumNoAudioTestAsync(scenario);
                });
            });

            const runNoChecksumNoAudioTestAsync = async (
                scenario: AudioMode
            ) => {
                // Setup
                const checksumSetting = "missing";
                const audioPresent = "missing";
                setupSentenceSplitTest(scenario, checksumSetting, audioPresent);

                // System under test
                await runShowToolAsync();

                // Verify
                verifySplitsUpdated(scenario);
                verifyChecksumsNotUpdated(scenario, "updated");
            };
        });

        describe("showTool(checksum=missing, audio=present, scenario=*) => SKIP UPDATE", () => {
            getAllAudioModes().forEach(scenario => {
                const scenarioName = AudioMode[scenario];
                it(`showTool(checksum=missing, audio=present, scenario=${scenarioName}) => SKIP UPDATE`, async () => {
                    return runNoChecksumYesAudioTestAsync(scenario);
                });
            });

            const runNoChecksumYesAudioTestAsync = async (
                scenario: AudioMode
            ) => {
                // Setup
                const checksumSetting = "missing";
                const audioPresent = "present";
                setupSentenceSplitTest(scenario, checksumSetting, audioPresent);

                // System Under Test
                await runShowToolAsync();

                // Verify
                verifySplitsPreserved(scenario);
                verifyChecksumsUpToDate(scenario, "preserved");
            };
        });

        describe("showTool(checksum=same, audio=missing, scenario=*) => UPDATE", () => {
            getAllAudioModes().forEach(scenario => {
                const scenarioName = AudioMode[scenario];
                it(`showTool(checksum=same, audio=missing, scenario=${scenarioName}) => UPDATE`, async () => {
                    return runSameChecksumNoAudioTestAsync(scenario);
                });
            });

            const runSameChecksumNoAudioTestAsync = async (
                scenario: AudioMode
            ) => {
                // Setup
                const checksumSetting = "same";
                const audioPresent = "missing";
                setupSentenceSplitTest(scenario, checksumSetting, audioPresent);

                // System under test
                await runShowToolAsync();

                // Verify
                verifySplitsUpdated(scenario);
                verifyChecksumsUpToDate(scenario, "updated"); // Not really necessary, but make sure it's not messed up
            };
        });

        describe("showTool(checksum=same, audio=partial, scenario=*) => UPDATE", () => {
            // PureTextBox and SoftSplit not applicable, because there's only 1 recording per text box.
            // Can't be partially recorded.
            const scenarios = [
                AudioMode.PureSentence,
                AudioMode.PreTextBox,
                AudioMode.HardSplitTextBox
            ];
            scenarios.forEach(scenario => {
                const scenarioName = AudioMode[scenario];
                it(`showTool(checksum=same, audio=partial, scenario=${scenarioName}) => UPDATE`, async () => {
                    return runSameChecksumPartialAudioTestAsync(scenario);
                });
            });

            const runSameChecksumPartialAudioTestAsync = async (
                scenario: AudioMode
            ) => {
                // Setup
                const checksumSetting = "same";
                const audioPresent = "partiallyPresent";
                setupSentenceSplitTest(scenario, checksumSetting, audioPresent);

                // System under test
                await runShowToolAsync();

                // Verify
                verifySplitsUpdated(scenario);
            };
        });

        describe("showTool(checksum=same, audio=present, scenario=*) => SKIP UPDATE", () => {
            getAllAudioModes().forEach(scenario => {
                const scenarioName = AudioMode[scenario];
                it(`showTool(checksum=same, audio=present, scenario=${scenarioName}) => SKIP UPDATE`, async () => {
                    return runSameChecksumYesAudioTestAsync(scenario);
                });
            });

            const runSameChecksumYesAudioTestAsync = async (
                scenario: AudioMode
            ) => {
                const checksumSetting = "same";
                const audioPresent = "present";
                setupSentenceSplitTest(scenario, checksumSetting, audioPresent);

                // System under test
                await runShowToolAsync();

                // Verify
                verifySplitsPreserved(scenario);
                verifyChecksumsUpToDate(scenario, "updated");
            };
        });

        describe("showTool(checksum=differs, recording=*, scenario=*) => UPDATE", () => {
            getAllAudioModes().forEach(scenario => {
                const scenarioName = AudioMode[scenario];
                it(`showTool(checksum=differs, recording=*, scenario=${scenarioName}) => UPDATE`, async () => {
                    return runDifferentChecksumTestAsync(scenario);
                });
            });

            const runDifferentChecksumTestAsync = async (
                scenario: AudioMode
            ) => {
                // Setup
                const checksumSetting = "differs";
                const audioPresent = "present"; // Value doesn't really matter, but pick the "harder" input.
                setupSentenceSplitTest(scenario, checksumSetting, audioPresent);

                // System under test
                await runShowToolAsync();

                // Verify
                verifySplitsUpdated(scenario);
                verifyCheckumsStillDiffers(scenario);
            };
        });
    });

    describe("phrase level highlighting", () => {
        // This helps power users perform phrase-level highlighting
        // Repro:
        // * Insert | as a delimiter for the phrases. (Note: this is the vertical bar on a standard keyboard, not \u104A)
        // * Record all audio.
        // * Remove the | delimiters.
        it("vertical bar delimits phrases without changing checksum (given fully recorded)", async () => {
            // Setup
            const checksums = [
                "ec04d9efb823169732254a756f9fa641",
                "99070d1ff8cd1dbe8f6273089fce9176"
            ];
            const divHtml = `<div class="bloom-editable" id="div1" data-audioRecordingMode="Sentence"><p><span id="1.1" class="audio-sentence ui-audioCurrent" recordingmd5="${checksums[0]}">Phrase 1|</span> <span id="1.2" class="audio-sentence" recordingmd5="${checksums[1]}">Phrase 2.</span></p></div>`;
            SetupIFrameFromHtml(divHtml);

            setAllAudioFilesPresent();

            // System under test
            // Simulate user deleting the vertical bar character
            // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
            getFrameElementById("page", "1.1")!.innerText = "Phrase 1";
            const tbTool = new TalkingBookTool();
            const doUpdate = await tbTool.updateMarkupAsync();
            doUpdate();

            // Verify - That splits were unchanged.
            const spans = getAudioSentenceSpans("div1");
            expect(spans).toHaveLength(2); // Should preserve the phrase splits instead of converting to sentence splits
            expect(spans[0]).toHaveAttr("recordingmd5", checksums[0]);
            expect(spans[1]).toHaveAttr("recordingmd5", checksums[1]);
        });
    });
});

function getAudioSentenceSpans(divId: string): HTMLSpanElement[] {
    const element = getFrameElementById("page", divId);

    expect(element).toBeTruthy(`${divId} should exist.`);

    const htmlElement = element as HTMLElement;
    const collection = htmlElement.querySelectorAll("span.audio-sentence");
    return Array.from(collection) as HTMLSpanElement[];
}

function verifyRecordButtonEnabled() {
    expect(document.getElementById("audio-record")).not.toHaveClass("disabled");
}

function setAllAudioFilesPresent() {
    // Mark that the recording exists.
    // FYI - spies only last for the scope of the "describe" or "it" block in which it was defined.
    spyOn(axios, "get").and.callFake((url: string) => {
        if (url.includes("/bloom/api/audio/checkForAllRecording?ids=")) {
            return Promise.resolve({ data: true });
        } else if (url.includes("/bloom/api/audio/checkForAnyRecording?ids=")) {
            return Promise.resolve();
        } else {
            return Promise.reject("Fake 404 error");
        }
    });
}
