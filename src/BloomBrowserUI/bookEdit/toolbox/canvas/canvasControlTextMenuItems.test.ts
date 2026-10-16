// The Field submenu of a canvas text box, as an undo step of the page history. Choosing a field
// type such as Book Title asks C# for that field's content (editView/getDataBookValue), and the
// answer replaces the content of the visible editable. The tests answer that request themselves,
// through a fake get, so they can look at the page history while it is still pending.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AxiosResponse } from "axios";
import { createPageHistory } from "../../pageHistory/pageHistory";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
// The menu reaches the page history through the page frame's exports (pageHistoryAccess.ts).
// The Field submenu needs no CanvasElementManager.
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history
            ? {
                  pageHistory: () => frames.history,
                  getTheOneCanvasElementManager: () => undefined,
              }
            : null,
}));

// Each request the menu makes, waiting for the test to answer it.
const requests = vi.hoisted(
    () =>
        [] as {
            urlSuffix: string;
            successCallback: (result: AxiosResponse) => void;
        }[],
);
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    get: (
        urlSuffix: string,
        successCallback: (result: AxiosResponse) => void,
    ) => requests.push({ urlSuffix, successCallback }),
}));

import { doTransactionIfAny } from "../../js/bloomEditing";
import { buildCanvasElementControlRegistryContext } from "./buildCanvasElementControlRegistryContext";
import { makeFieldTypeMenuItem } from "./canvasControlTextMenuItems";

const kBookTitleKey = "EditTab.CustomCover.FieldType.BookTitle";
const kCoverCreditsKey = "EditTab.CustomCover.FieldType.CoverCredits";
const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kOldContent = "<p>Some words</p>";
const kTitleContent = "<p>The Big Book</p>";
const kCreditsContent = "<p>By someone</p>";

let history: PageHistory;
let canvasElement: HTMLElement;
let editable: HTMLElement;
let other: HTMLElement;
let onUnmarkedChange: ReturnType<typeof vi.fn<(whatChanged: string) => void>>;

beforeEach(async () => {
    const page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = `<div class="bloom-canvas-element"><div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" lang="en" id="old-id">${kOldContent}</div></div></div><div id="other"></div>`;
    document.body.appendChild(page);
    canvasElement = page.querySelector(".bloom-canvas-element")!;
    editable = page.querySelector(".bloom-editable")!;
    other = page.querySelector("#other")!;
    onUnmarkedChange = vi.fn<(whatChanged: string) => void>();
    history = createPageHistory({ root: page, onUnmarkedChange });
    frames.history = history;
    // A step before the field type, which must survive it.
    await history.doTransaction(kEarlierKey, () =>
        other.setAttribute("data-earlier", "true"),
    );
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    expect(editable.hasAttribute("data-book")).toBe(false);
});

afterEach(() => {
    history.dispose();
    frames.history = undefined;
    requests.length = 0;
    document.body.innerHTML = "";
});

/**
 * Choose the field type whose row has l10nId in the Field submenu, as a click on it does:
 * runControlCallback runs the row's onSelect in doTransactionIfAny. Returns the promise of that
 * step, which settles when the step has closed.
 */
function chooseFieldType(l10nId: string): Promise<void> {
    const ctx = buildCanvasElementControlRegistryContext(canvasElement);
    const row = makeFieldTypeMenuItem(ctx).subMenuItems!.find(
        (subMenuRow) => subMenuRow.l10nId === l10nId,
    );
    if (!row) fail(`The Field submenu has no row for ${l10nId}.`);
    return doTransactionIfAny(l10nId, () =>
        row.onSelect(ctx, { closeMenu: () => {} }),
    );
}

/** Answer the oldest pending request for a field's content with content and an audio id. */
function answer(dataBook: string, content: string, id: string): void {
    const request = requests.shift();
    if (!request) fail("The menu made no request for the field's content.");
    expect(request.urlSuffix).toBe(
        `editView/getDataBookValue?lang=en&dataBook=${dataBook}`,
    );
    request.successCallback({
        data: { content, id, recordingMd5: `${id}-md5` },
    } as AxiosResponse);
}

describe("Choosing a field type", () => {
    it("keeps its step open, and undo does nothing, while the field's content is pending", async () => {
        const chosen = chooseFieldType(kBookTitleKey);

        expect(editable.getAttribute("data-book")).toBe("bookTitle");
        expect(requests.length).toBe(1);
        expect(history.hasOpenTransaction).toBe(true);
        await history.undo();
        expect(editable.getAttribute("data-book")).toBe("bookTitle");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);

        answer("bookTitle", kTitleContent, "title-id");
        await chosen;
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kBookTitleKey);
    });

    it("makes the type and the content one step, which undo and redo reverse", async () => {
        const chosen = chooseFieldType(kBookTitleKey);
        answer("bookTitle", kTitleContent, "title-id");
        await chosen;

        expect(editable.getAttribute("data-book")).toBe("bookTitle");
        expect(editable.classList.contains("Title-On-Cover-style")).toBe(true);
        expect(editable.innerHTML).toBe(kTitleContent);
        expect(editable.id).toBe("title-id");
        expect(editable.getAttribute("recordingmd5")).toBe("title-id-md5");
        expect(history.undoDescriptionL10nKey).toBe(kBookTitleKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(editable.hasAttribute("data-book")).toBe(false);
        expect(editable.classList.contains("Title-On-Cover-style")).toBe(false);
        expect(editable.innerHTML).toBe(kOldContent);
        expect(editable.id).toBe("old-id");
        expect(editable.hasAttribute("recordingmd5")).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(history.redoDescriptionL10nKey).toBe(kBookTitleKey);

        await history.redo();
        expect(editable.getAttribute("data-book")).toBe("bookTitle");
        expect(editable.innerHTML).toBe(kTitleContent);
        expect(editable.id).toBe("title-id");
        expect(editable.getAttribute("recordingmd5")).toBe("title-id-md5");

        await history.undo();
        await history.undo();
        expect(other.hasAttribute("data-earlier")).toBe(false);
    });

    it("does not write the content into a box that was deleted while it was pending", async () => {
        const chosen = chooseFieldType(kBookTitleKey);
        canvasElement.remove();

        answer("bookTitle", kTitleContent, "title-id");
        await chosen;

        expect(editable.innerHTML).toBe(kOldContent);
        expect(editable.id).toBe("old-id");
        expect(history.hasOpenTransaction).toBe(false);
    });

    it("does not write the content of a field type that another choice replaced while it was pending", async () => {
        const titleChosen = chooseFieldType(kBookTitleKey);
        const creditsChosen = chooseFieldType(kCoverCreditsKey);
        expect(editable.getAttribute("data-book")).toBe("smallCoverCredits");
        expect(requests.length).toBe(2);
        // The answers come in the order opposite to the requests.
        const titleRequest = requests.shift()!;
        answer("smallCoverCredits", kCreditsContent, "credits-id");
        await creditsChosen;
        requests.push(titleRequest);
        answer("bookTitle", kTitleContent, "title-id");
        await titleChosen;

        expect(editable.getAttribute("data-book")).toBe("smallCoverCredits");
        expect(editable.innerHTML).toBe(kCreditsContent);
        expect(editable.id).toBe("credits-id");
        expect(history.hasOpenTransaction).toBe(false);
        // The second choice was made inside the step of the first.
        expect(history.undoDescriptionL10nKey).toBe(kBookTitleKey);
        await history.undo();
        expect(editable.hasAttribute("data-book")).toBe(false);
        expect(editable.innerHTML).toBe(kOldContent);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });
});
