import $ from "jquery";
import { beforeEach, describe, expect, test, vi } from "vitest";

// Tests for setupImageDescriptions() when the reply to its
// editView/requestTranslationGroupContent request arrives.
//
// If the Edit tab moves to another page while that request is out (adding a page does this),
// the page frame is loading the new page and has no editablePageBundle yet, so
// getEditablePageBundleExports() returns undefined. The reply is for a container on the page
// being left, and must be ignored rather than crash on makeElement (BL-16933).

const mocks = vi.hoisted(() => ({
    // Replies to post() are held here so each test decides when they arrive.
    pendingReplies: [] as Array<(result: { data: string }) => void>,
    currentPage: undefined as HTMLElement | undefined,
    bloomCanvases: [] as HTMLElement[],
    pageBundle: undefined as
        | {
              makeElement: (html: string) => JQuery;
              SetupElements: (container: HTMLElement) => void;
              attachToCkEditor: (element: HTMLElement) => void;
          }
        | undefined,
}));

vi.mock("../../../utils/bloomApi", () => ({
    post: (_url: string, callback: (result: { data: string }) => void) => {
        mocks.pendingReplies.push(callback);
    },
}));

vi.mock("../toolbox", () => ({
    ToolBox: {
        getPage: () => mocks.currentPage,
    },
}));

vi.mock("../canvas/canvasElementPageBridge", () => ({
    getCanvasElementManager: () => ({
        getAllBloomCanvasesOnPage: () => mocks.bloomCanvases,
    }),
}));

vi.mock("../../js/workspaceFrames", () => ({
    getEditablePageBundleExports: () => mocks.pageBundle,
}));

import { setupImageDescriptions } from "./imageDescription";

const kReplyHtml =
    "<div class='bloom-editable normal-style' lang='en' contenteditable='true'></div>";

// Make a page holding one bloom-canvas that has no image description, in the given document.
function makePage(doc: Document): {
    page: HTMLElement;
    bloomCanvas: HTMLElement;
} {
    const page = doc.createElement("div");
    page.className = "bloom-page";
    const bloomCanvas = doc.createElement("div");
    bloomCanvas.className = "bloom-canvas";
    bloomCanvas.appendChild(doc.createElement("img"));
    page.appendChild(bloomCanvas);
    doc.body.appendChild(page);
    return { page, bloomCanvas };
}

function makePageBundle() {
    return {
        makeElement: vi.fn((html: string) => $(html)),
        SetupElements: vi.fn(),
        attachToCkEditor: vi.fn(),
    };
}

describe("setupImageDescriptions", () => {
    beforeEach(() => {
        mocks.pendingReplies = [];
        mocks.currentPage = undefined;
        mocks.bloomCanvases = [];
        mocks.pageBundle = undefined;
        document.body.innerHTML = "";
    });

    test("adds an image description when the reply arrives on the same page", () => {
        const { page, bloomCanvas } = makePage(document);
        mocks.currentPage = page;
        mocks.bloomCanvases = [bloomCanvas];
        const pageBundle = makePageBundle();
        mocks.pageBundle = pageBundle;
        const doToImageDescriptions = vi.fn();
        const doIfContentAdded = vi.fn();

        setupImageDescriptions(page, doToImageDescriptions, doIfContentAdded);

        expect(mocks.pendingReplies.length).toBe(1);
        expect(
            bloomCanvas.getElementsByClassName("bloom-imageDescription").length,
        ).toBe(0);

        mocks.pendingReplies[0]({ data: kReplyHtml });

        expect(
            bloomCanvas.getElementsByClassName("bloom-imageDescription").length,
        ).toBe(1);
        expect(pageBundle.makeElement).toHaveBeenCalledTimes(1);
        expect(doIfContentAdded).toHaveBeenCalledTimes(1);
        expect(doToImageDescriptions).toHaveBeenCalledTimes(1);
    });

    test("ignores a reply that arrives after the Edit tab moved to another page", () => {
        // The page being left lives in the document the page frame showed before.
        const oldDocument = document.implementation.createHTMLDocument("old");
        const { page: oldPage, bloomCanvas: oldBloomCanvas } =
            makePage(oldDocument);
        mocks.currentPage = oldPage;
        mocks.bloomCanvases = [oldBloomCanvas];
        const doToImageDescriptions = vi.fn();
        const doIfContentAdded = vi.fn();

        setupImageDescriptions(
            oldPage,
            doToImageDescriptions,
            doIfContentAdded,
        );
        expect(mocks.pendingReplies.length).toBe(1);

        // Now the frame holds a new page whose editablePageBundle has not loaded yet.
        const { page: newPage } = makePage(document);
        mocks.currentPage = newPage;
        mocks.pageBundle = undefined;
        expect(oldBloomCanvas.ownerDocument).not.toBe(newPage.ownerDocument);

        mocks.pendingReplies[0]({ data: kReplyHtml });

        expect(
            oldBloomCanvas.getElementsByClassName("bloom-imageDescription")
                .length,
        ).toBe(0);
        expect(doIfContentAdded).not.toHaveBeenCalled();
        expect(doToImageDescriptions).not.toHaveBeenCalled();
    });
});
