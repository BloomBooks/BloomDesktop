// Tests of writeToBloomLog: the page history's lines reach Bloom's log, without the repeats that
// typing makes.
import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
    postString: vi.fn<(urlSuffix: string, value: string) => void>(),
}));
vi.mock("../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../utils/bloomApi")>()),
    postString: mocks.postString,
}));

import { writeToBloomLog } from "./pageHistorySetup";
import { kGenericUndoDescriptionL10nKey } from "./pageHistoryTypes";

const kGenericStep = `Page history: step ${kGenericUndoDescriptionL10nKey}`;

function loggedLines(): string[] {
    return mocks.postString.mock.calls.map((call) => {
        expect(call[0]).toBe("common/logger/writeEvent");
        return call[1];
    });
}

describe("writeToBloomLog", () => {
    beforeEach(() => {
        // Start each test after a line that none of them writes.
        writeToBloomLog("Page history: test start");
        mocks.postString.mockClear();
    });

    it("leaves out a step with the general description that repeats the line before it", () => {
        writeToBloomLog(kGenericStep);
        writeToBloomLog(kGenericStep);
        writeToBloomLog(kGenericStep);
        writeToBloomLog("Page history: undo EditTab.CutButton");
        writeToBloomLog(kGenericStep);
        expect(loggedLines()).toEqual([
            kGenericStep,
            "Page history: undo EditTab.CutButton",
            kGenericStep,
        ]);
    });

    it("keeps repeated lines that name other steps", () => {
        writeToBloomLog("Page history: step EditTab.CutButton");
        writeToBloomLog("Page history: step EditTab.CutButton");
        writeToBloomLog(`Page history: undo ${kGenericUndoDescriptionL10nKey}`);
        writeToBloomLog(`Page history: undo ${kGenericUndoDescriptionL10nKey}`);
        expect(loggedLines()).toEqual([
            "Page history: step EditTab.CutButton",
            "Page history: step EditTab.CutButton",
            `Page history: undo ${kGenericUndoDescriptionL10nKey}`,
            `Page history: undo ${kGenericUndoDescriptionL10nKey}`,
        ]);
    });
});
