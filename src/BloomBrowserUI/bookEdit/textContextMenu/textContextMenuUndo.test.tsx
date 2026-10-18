// Tests that choosing "Do Not Indent This Paragraph" on the text context menu is an undo step that
// says so.
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Code in the page frame finds the page history through the page frame's bundle exports; give it
// this test's.
const pageFrame = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../js/workspaceFrames")>()),
    getEditablePageBundleExports: () =>
        pageFrame.history ? { pageHistory: () => pageFrame.history } : null,
}));

// These imports deliberately come after the vi.mock call above, so that the module graph they
// pull in gets the stubbed module.
import { setupTextContextMenu } from "./TextContextMenu";
import { unmountRoot } from "../../utils/reactRender";
import { kNoIndentClass } from "./noIndent";
import { createPageHistory } from "../pageHistory/pageHistory";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../pageHistory/pageHistoryTypes";

let history: PageHistory;
let paragraph: HTMLElement;

beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-editable" contenteditable="true"><p id="p1">One</p></div>
        </div>`;
    paragraph = document.getElementById("p1")!;
    // The command is offered for a paragraph that has it on already, whatever its indent.
    paragraph.classList.add(kNoIndentClass);
    history = createPageHistory({ root: document.getElementById("page")! });
    pageFrame.history = history;
});

afterEach(() => {
    // The menu's React root, which the body's markup must outlive.
    act(() => {
        unmountRoot(document.getElementById("text-context-menu"));
    });
    pageFrame.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
});

describe("Do Not Indent This Paragraph", () => {
    it("is an undo step described by the command", async () => {
        setupTextContextMenu();
        await act(async () => {
            paragraph.dispatchEvent(
                new MouseEvent("contextmenu", { bubbles: true }),
            );
        });
        // The menu item carries its l10n id as its test id.
        const item = await vi.waitFor(() => {
            const found = document.querySelector<HTMLElement>(
                '[data-testid="EditTab.TextContextMenu.NoIndent"]',
            );
            if (!found) throw new Error("The menu has not shown its item yet.");
            return found;
        });

        // The transaction with the general description that the click opens (pageHistoryInput.ts).
        const standIn = history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        act(() => item.click());
        // A MutationObserver delivers its records in a microtask.
        await Promise.resolve();
        standIn.end();

        expect(paragraph.classList.contains(kNoIndentClass)).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.TextContextMenu.NoIndent",
        );
    });
});
