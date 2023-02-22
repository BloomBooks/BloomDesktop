import { LeveledReaderToolboxTool } from "./leveledReaderToolboxTool";

describe("leveledReaderToolboxTool tests", function() {
    it("remove and restore phrase marker character", function() {
        const testDiv = document.createElement("div");
        testDiv.innerHTML =
            '<p>This is a test,<span class="bloom-audio-split-marker">|</span> this is only a test.</p>';
        expect(
            testDiv.getElementsByClassName("bloom-audio-split-marker").length
        ).toBe(1);
        expect(
            testDiv.innerHTML.includes(
                '<span class="bloom-audio-split-marker">|</span>'
            )
        ).toBe(true);
        expect(
            testDiv.innerHTML.includes(
                '<span class="bloom-audio-split-marker">\u200B</span>'
            )
        ).toBe(false);

        LeveledReaderToolboxTool.disableAudioSplitMarkers(testDiv);

        expect(
            testDiv.getElementsByClassName("bloom-audio-split-marker").length
        ).toBe(1);
        expect(
            testDiv.innerHTML.includes(
                '<span class="bloom-audio-split-marker">|</span>'
            )
        ).toBe(false);
        expect(
            testDiv.innerHTML.includes(
                '<span class="bloom-audio-split-marker">\u200B</span>'
            )
        ).toBe(true);

        LeveledReaderToolboxTool.restoreAudioSplitMarkers(testDiv);

        expect(
            testDiv.getElementsByClassName("bloom-audio-split-marker").length
        ).toBe(1);
        expect(
            testDiv.innerHTML.includes(
                '<span class="bloom-audio-split-marker">|</span>'
            )
        ).toBe(true);
        expect(
            testDiv.innerHTML.includes(
                '<span class="bloom-audio-split-marker">\u200B</span>'
            )
        ).toBe(false);
    });
});
