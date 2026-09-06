import { describe, it, expect, beforeEach, vi } from "vitest";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import ReadersSynphonyWrapper from "../ReadersSynphonyWrapper";
import { initializeReaderSetupDialog } from "./readerSetupDialog";

// The setup dialog is populated by initializeReaderSetupDialog(), which the dialog's iframe
// calls as soon as it loads. If the reader settings never made it into the model, that call
// used to die on a bare "cannot read properties of undefined (reading 'source')", which told
// nobody anything and left the dialog showing the wrong tab with empty fields -- what the user
// experienced as "Set up Levels does nothing". (BL-16732)
describe("initializeReaderSetupDialog", () => {
    beforeEach(() => {
        getTheOneReaderToolsModel().clearForTest();
    });

    it("reports that the settings were not loaded, rather than throwing a TypeError", () => {
        // sanity check: this is the state we mean to test
        expect(getTheOneReaderToolsModel().synphony).toBeUndefined();

        expect(() => initializeReaderSetupDialog()).toThrowError(
            "ReaderToolsModel was not loaded with settings",
        );
    });

    it("sends the settings to the dialog frame once they are loaded", () => {
        const synphony = new ReadersSynphonyWrapper();
        synphony.loadSettings({
            letters: "a b c",
            moreWords: "cat sat",
            stages: [{ letters: "a c", sightWords: "feline" }],
            levels: [{ maxWordsPerPage: 6, thingsToRemember: [""] }],
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
        } as any);
        getTheOneReaderToolsModel().synphony = synphony;
        // sanity check: the model now has something to send
        expect(getTheOneReaderToolsModel().synphony?.source).toBeTruthy();

        // The real settings frame is in the parent document; in this test window.parent is
        // ourselves, so putting the iframe in our own document is enough for the lookup.
        const settingsFrame = document.createElement("iframe");
        settingsFrame.id = "settings_frame";
        document.body.appendChild(settingsFrame);
        const postMessage = vi.fn();
        settingsFrame.contentWindow!.postMessage = postMessage;

        initializeReaderSetupDialog();

        const messages = postMessage.mock.calls.map((call) => call[0]);
        expect(messages.some((m) => m.startsWith("Data\n"))).toBe(true);
        expect(messages.some((m) => m.startsWith("Font\n"))).toBe(true);
    });
});
