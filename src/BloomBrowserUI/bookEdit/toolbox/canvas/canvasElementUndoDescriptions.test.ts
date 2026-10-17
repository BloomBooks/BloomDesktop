// Tests of the descriptions of the undo steps that act on a canvas element as a whole
// (canvasElementUndoDescriptions.ts): which noun each kind of element gets, and that a command
// chosen from the canvas element menu makes a step described by its template and that noun. The
// menu test uses a real page history, and a stand-in for the page frame's CanvasElementManager.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../../pageHistory/pageHistory";
import {
    formatUndoDescription,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";

const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    active: undefined as HTMLElement | undefined,
}));
// The command runners reach the page history and the CanvasElementManager through the page
// frame's exports (pageHistoryAccess.ts, canvasElementPageBridge.ts).
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history
            ? {
                  pageHistory: () => frames.history,
                  getTheOneCanvasElementManager: () => ({
                      deleteCurrentCanvasElement: () => frames.active?.remove(),
                      redrawBubblesFromPage: () => {},
                  }),
              }
            : null,
}));

import {
    canvasElementTypeUndoNoun,
    canvasElementUndoNoun,
    describeCanvasElementStep,
    kCanvasElementUndoNouns,
    kCanvasElementUndoTemplates,
} from "./canvasElementUndoDescriptions";
import { buildCanvasElementControlRegistryContext } from "./buildCanvasElementControlRegistryContext";
import { canvasElementControlRegistry } from "./canvasElementControlRegistry";
import { getMenuSections } from "./canvasControlResolution";
import type {
    IControlMenuCommandRow,
    IControlRuntime,
} from "./canvasControlTypes";
import { inferCanvasElementType } from "./canvasElementTypeInference";
import { convertControlMenuRows } from "../../js/canvasElementManager/canvasControlMenuRendering";

const kTable =
    '<div class="bloom-canvas-element" id="table"><div class="bloom-table"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p>cell</p></div></div></div></div>';
const kImage =
    '<div class="bloom-canvas-element" id="image"><div class="bloom-imageContainer"><img src="bird.png" alt=""></div></div>';
const kVideo =
    '<div class="bloom-canvas-element" id="video"><div class="bloom-videoContainer"></div></div>';
// The bubble styles are in data-bubble, with backquotes for quotes, as Bloom writes them.
const kTextBox =
    '<div class="bloom-canvas-element" id="textBox" data-bubble="{`version`:`1.0`,`style`:`none`}"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p>text</p></div></div></div>';
const kSpeech =
    '<div class="bloom-canvas-element" id="speech" data-bubble="{`version`:`1.0`,`style`:`speech`}"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p>hi</p></div></div></div>';
const kCaption =
    '<div class="bloom-canvas-element" id="caption" data-bubble="{`version`:`1.0`,`style`:`caption`}"><div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p>a bird</p></div></div></div>';
const kBackground =
    '<div class="bloom-canvas-element bloom-backgroundImage" id="background"><div class="bloom-imageContainer"><img src="sky.png" alt=""></div></div>';
const kGameTarget = '<div id="target" data-target-of="d1"></div>';
const kUnknown = '<div class="bloom-canvas-element" id="unknown"></div>';

let page: HTMLElement;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML = `<div class="bloom-canvas">${[
        kTable,
        kImage,
        kVideo,
        kTextBox,
        kSpeech,
        kCaption,
        kBackground,
        kGameTarget,
        kUnknown,
    ].join("")}</div>`;
    document.body.appendChild(page);
});

afterEach(() => {
    page.remove();
    frames.history?.dispose();
    frames.history = undefined;
    frames.active = undefined;
});

function byId(id: string): HTMLElement {
    const element = page.querySelector<HTMLElement>(`#${id}`);
    expect(element, `test setup: #${id} should be on the page`).not.toBeNull();
    return element!;
}

describe("the noun of a canvas element", () => {
    it.each([
        ["table", "EditTab.Undo.Object.Table"],
        ["image", "EditTab.Undo.Object.Image"],
        ["video", "EditTab.Undo.Object.Video"],
        ["textBox", "EditTab.Undo.Object.TextBox"],
        ["speech", "EditTab.Undo.Object.SpeechBubble"],
        ["caption", "EditTab.Undo.Object.Caption"],
        ["background", "EditTab.Undo.Object.BackgroundImage"],
        ["target", "EditTab.Undo.Object.GameTarget"],
        ["unknown", "EditTab.Undo.Object.CanvasElement"],
    ])("of #%s is %s", (id, noun) => {
        expect(canvasElementUndoNoun(byId(id))).toBe(noun);
    });

    it("tells a text box from a speech bubble, which inferCanvasElementType does not", () => {
        expect(
            [
                inferCanvasElementType(byId("textBox")),
                inferCanvasElementType(byId("speech")),
            ],
            "test setup: both infer as speech",
        ).toEqual(["speech", "speech"]);
        expect(canvasElementUndoNoun(byId("textBox"))).not.toBe(
            canvasElementUndoNoun(byId("speech")),
        );
    });

    it("of a type to be added follows what the add makes", () => {
        expect(canvasElementTypeUndoNoun("table")).toBe(
            "EditTab.Undo.Object.Table",
        );
        expect(canvasElementTypeUndoNoun("none")).toBe(
            "EditTab.Undo.Object.TextBox",
        );
        expect(
            canvasElementTypeUndoNoun(undefined),
            "an add with no type makes a speech bubble",
        ).toBe("EditTab.Undo.Object.SpeechBubble");
    });

    it("is one of a set of distinct ids used only for undo", () => {
        expect(new Set(kCanvasElementUndoNouns).size).toBe(
            kCanvasElementUndoNouns.length,
        );
        kCanvasElementUndoNouns.forEach((noun) =>
            expect(noun).toMatch(/^EditTab\.Undo\.Object\.\w+$/),
        );
    });
});

describe("the description of a step on a canvas element", () => {
    it("is the template's id with the noun as its one argument", () => {
        expect(describeCanvasElementStep("delete", byId("table"))).toEqual({
            l10nId: "EditTab.Undo.Delete",
            args: ["EditTab.Undo.Object.Table"],
        });
        expect(describeCanvasElementStep("add", "image")).toEqual({
            l10nId: "EditTab.Undo.Add",
            args: ["EditTab.Undo.Object.Image"],
        });
        Object.values(kCanvasElementUndoTemplates).forEach((template) =>
            expect(template).toMatch(/^EditTab\.Undo\.\w+$/),
        );
    });
});

describe("a command chosen from the canvas element menu", () => {
    // Choose the row with this id from the menu of element, as a click on the menu does, and
    // return the step that it made, as the log writes it.
    async function choose(
        rowId: string,
        element: HTMLElement,
    ): Promise<string> {
        // Built before there is a page frame, so that it asks no CanvasElementManager.
        const ctx = buildCanvasElementControlRegistryContext(element);
        const history = createPageHistory({ root: page });
        frames.history = history;
        frames.active = element;
        const runtime = { closeMenu: vi.fn() } as unknown as IControlRuntime;
        const rows = getMenuSections(
            canvasElementControlRegistry[ctx.elementType],
            ctx,
            runtime,
        )
            .flat()
            .map((item) => item.menuRow)
            .filter((row): row is IControlMenuCommandRow => row?.id === rowId);
        expect(rows, `test setup: the menu should have ${rowId}`).toHaveLength(
            1,
        );
        const [menuItem] = convertControlMenuRows(rows, ctx, runtime);
        // The handler does not look at the click event.
        menuItem.onClick(undefined as never);
        // runControlCallback runs the command in a transaction whose promise it does not return.
        await vi.waitFor(() => expect(history.hasOpenTransaction).toBe(false));
        const undo = history.getStepDescriptions().undo;
        expect(undo, "the command should have made one step").toHaveLength(1);
        return formatUndoDescription(undo[0].l10nId, undo[0].args);
    }

    it("Delete, on a table, is Delete with the table's noun", async () => {
        const table = byId("table");
        expect(await choose("delete", table)).toBe(
            "EditTab.Undo.Delete(EditTab.Undo.Object.Table)",
        );
        expect(table.isConnected, "the table should be gone").toBe(false);
    });

    it("Delete, on a picture, is Delete with the picture's noun", async () => {
        expect(await choose("delete", byId("image"))).toBe(
            "EditTab.Undo.Delete(EditTab.Undo.Object.Image)",
        );
    });
});
