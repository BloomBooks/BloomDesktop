// The Text Box Properties dialog's changes as undo steps of the page history, described by the
// dialog's title, and the open dialog following an undo or redo. The dialog lives in the page
// frame, so the tests attach the input transactions with this window as the page's. The user's
// events are the ones in userEvents, and a test says where the browser's task ends by calling
// finishTask(), as in imageDescriptionUndo.test.tsx. The tests build only the parts of the dialog
// that they use, and wire them as AttachToBox does.
import $ from "jquery";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../pageHistory/pageHistory";
import { attachInputTransactions } from "../pageHistory/pageHistoryInput";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

// The page frame's exports, through which pageHistoryAccess.ts finds the history.
vi.mock("../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../js/workspaceFrames")>()),
    getEditablePageBundleExports: () =>
        mocks.history ? { pageHistory: () => mocks.history } : null,
}));
// The hint bubble on the page is qtip's, which these tests do not load.
vi.mock("../js/BloomHintBubbles", () => ({
    default: { updateQtipPlacement: () => undefined },
}));

// The page frame loads this jQuery plugin through bloomEditing.ts, which TextBoxProperties uses.
import "../js/jquery.hasAttr.js";
import TextBoxProperties from "./TextBoxProperties";

const kDialogKey = "EditTab.TextBoxProperties.Title";
const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kButtonIds = [
    "align-top",
    "align-center",
    "align-bottom",
    "borderstyle-none",
    "borderstyle-black",
    "borderstyle-black-round",
    "borderstyle-gray",
    "borderstyle-gray-round",
    "background-none",
    "background-light-gray",
    "background-gray",
    "background-black",
    "bordertop",
    "borderleft",
    "borderright",
    "borderbottom",
];

// The private members of TextBoxProperties that the tests use.
interface ITextBoxPropertiesInside {
    boxBeingEdited: HTMLElement;
    setButtonClickActions(): void;
    showPropertiesOfBox(): void;
    followUndoAndRedo(dialog: HTMLElement): void;
    changeLanguageGroup(): void;
    initializeHintText(): void;
}

let history: PageHistory;
let group: HTMLElement;
let other: HTMLElement;
let dialogElement: HTMLElement;
let dialog: ITextBoxPropertiesInside;
let detach: () => void;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];
let onUnmarkedChange: ReturnType<typeof vi.fn>;

beforeEach(async () => {
    document.body.innerHTML =
        '<div class="bloom-page bloom-templateMode"><div class="split-pane-component-inner"><div class="textBox-identifier"></div><div class="bloom-translationGroup"></div></div><div id="other"></div></div>';
    const page = document.body.querySelector<HTMLElement>(".bloom-page")!;
    group = page.querySelector(".bloom-translationGroup")!;
    other = page.querySelector("#other")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({ root: page, onUnmarkedChange });
    mocks.history = history;
    // A step before the dialog, which must survive it.
    await history.doTransaction(kEarlierKey, () =>
        other.setAttribute("data-earlier", "true"),
    );
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);

    userEvents = new WeakSet();
    endOfTask = [];
    detach = attachInputTransactions(history, window, [], {
        isFromUser: (event) => userEvents.has(event),
        afterCurrentTask: (fn) => endOfTask.push(fn),
    });

    // The dialog, as AttachToBox adds it beside the page.
    dialogElement = document.createElement("div");
    dialogElement.id = "text-properties-dialog";
    dialogElement.innerHTML =
        kButtonIds
            .map((id) => `<div id="${id}" class="propButton"></div>`)
            .join("") +
        '<div id="language-group">' +
        ["Auto", "N1", "N2", "V"]
            .map(
                (value) =>
                    `<input type="radio" name="languageRadioGroup" value="${value}">`,
            )
            .join("") +
        "</div>" +
        '<select id="lang-select"><option value="en" selected>English</option></select>' +
        '<div id="hint-content" contenteditable="true"></div>';
    document.body.appendChild(dialogElement);

    dialog = new TextBoxProperties("") as unknown as ITextBoxPropertiesInside;
    dialog.boxBeingEdited = page.querySelector(".textBox-identifier")!;
    $("#language-group").change(() => dialog.changeLanguageGroup());
    dialog.showPropertiesOfBox();
    dialog.setButtonClickActions();
    dialog.followUndoAndRedo(dialogElement);
});

afterEach(() => {
    detach();
    history.dispose();
    mocks.history = undefined;
    document.body.innerHTML = "";
});

function finishTask(): void {
    while (endOfTask.length > 0) endOfTask.shift()!();
}

function mouse(target: Element, type: string, buttons: number): void {
    const event = new MouseEvent(type, {
        bubbles: true,
        cancelable: true,
        buttons,
    });
    userEvents.add(event);
    target.dispatchEvent(event);
}

/** A click by the user: the press, then the release as one task, and the end of that task. */
function click(target: Element): void {
    mouse(target, "pointerdown", 1);
    mouse(target, "mousedown", 1);
    mouse(target, "pointerup", 0);
    mouse(target, "mouseup", 0);
    mouse(target, "click", 0);
    finishTask();
}

/**
 * The user types text into element, one key at a time, each key press with the input that it
 * makes, and then moves the focus away.
 */
function typeThenLeave(element: HTMLElement, text: string): void {
    for (const key of text) {
        const keydown = new KeyboardEvent("keydown", {
            key,
            bubbles: true,
            cancelable: true,
        });
        userEvents.add(keydown);
        element.dispatchEvent(keydown);
        element.textContent += key;
        const input = new InputEvent("input", {
            bubbles: true,
            inputType: "insertText",
            data: key,
        });
        userEvents.add(input);
        element.dispatchEvent(input);
    }
    const focusout = new FocusEvent("focusout", { bubbles: true });
    userEvents.add(focusout);
    element.dispatchEvent(focusout);
    finishTask();
}

function isSelected(buttonId: string): boolean {
    return document
        .getElementById(buttonId)!
        .classList.contains("selectedIcon");
}

function checkedLanguage(): string {
    return (
        document.querySelector<HTMLInputElement>(
            'input[name="languageRadioGroup"]:checked',
        )?.value ?? "none"
    );
}

describe("The Text Box Properties dialog", () => {
    it("makes a button's change one step, named by the dialog, and follows undo and redo", async () => {
        expect(isSelected("background-none")).toBe(true);
        expect(isSelected("background-gray")).toBe(false);

        click(document.getElementById("background-gray")!);

        expect(group.classList.contains("bloom-background-gray")).toBe(true);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kDialogKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(group.classList.contains("bloom-background-gray")).toBe(false);
        expect(isSelected("background-none")).toBe(true);
        expect(isSelected("background-gray")).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(history.redoDescriptionL10nKey).toBe(kDialogKey);

        await history.redo();
        expect(group.classList.contains("bloom-background-gray")).toBe(true);
        expect(isSelected("background-none")).toBe(false);
        expect(isSelected("background-gray")).toBe(true);
        // Following the redo changed nothing on the page, so redo is not cleared.
        expect(history.undoDescriptionL10nKey).toBe(kDialogKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("makes choosing the languages one step, and shows the choice that undo puts back", async () => {
        expect(checkedLanguage()).toBe("Auto");

        click(
            document.querySelector(
                'input[name="languageRadioGroup"][value="N1"]',
            )!,
        );

        expect(group.getAttribute("data-default-languages")).toBe("N1");
        expect(history.undoDescriptionL10nKey).toBe(kDialogKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(group.hasAttribute("data-default-languages")).toBe(false);
        expect(checkedLanguage()).toBe("Auto");

        await history.redo();
        expect(group.getAttribute("data-default-languages")).toBe("N1");
        expect(checkedLanguage()).toBe("N1");
    });

    it("makes typing a hint one step, not one per key", async () => {
        dialog.initializeHintText();
        const hintContent = document.getElementById("hint-content")!;

        typeThenLeave(hintContent, "abc");

        expect(group.querySelector('label[lang="en"]')?.textContent).toBe(
            "abc",
        );
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kDialogKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        // One undo takes out the whole hint, and the dialog shows that.
        await history.undo();
        expect(group.querySelector("label")).toBeNull();
        expect(hintContent.textContent).toBe("");
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);

        await history.redo();
        expect(group.querySelector('label[lang="en"]')?.textContent).toBe(
            "abc",
        );
        expect(hintContent.textContent).toBe("abc");
    });

    it("stops following undo and redo once it is gone", async () => {
        click(document.getElementById("align-bottom")!);
        expect(group.classList.contains("bloom-vertical-align-bottom")).toBe(
            true,
        );
        const showProperties = vi.spyOn(dialog, "showPropertiesOfBox");
        dialogElement.remove();

        await history.undo();
        expect(group.classList.contains("bloom-vertical-align-bottom")).toBe(
            false,
        );
        await history.redo();
        expect(showProperties).not.toHaveBeenCalled();
    });
});
