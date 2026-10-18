// Tests of the undo and redo keys (pageHistoryKeys.ts): docs/undo/MINIMUM.md item 10. The page is
// in this test window; a second window, an iframe, stands for the toolbox or the root window.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "./pageHistory";
import { attachInputTransactions } from "./pageHistoryInput";
import { attachUndoAndRedoKeys, undoOrRedoCommandOf } from "./pageHistoryKeys";

let page: HTMLElement;
let box: HTMLElement;
let otherFrame: HTMLIFrameElement;
let otherWindow: Window;
let commands: {
    undo: ReturnType<typeof vi.fn<() => void>>;
    redo: ReturnType<typeof vi.fn<() => void>>;
    isInChangeLayoutMode: () => boolean;
};
let inChangeLayoutMode: boolean;
let detachers: (() => void)[];

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-editable" id="box" contenteditable="true"><p>Hello</p></div>';
    document.body.appendChild(page);
    box = page.querySelector<HTMLElement>("#box")!;
    otherFrame = document.createElement("iframe");
    document.body.appendChild(otherFrame);
    otherWindow = otherFrame.contentWindow!;
    otherWindow.document.body.innerHTML =
        '<button id="toolButton">Tool</button><input id="field" type="text">' +
        '<input id="untypedField"><input id="numberField" type="number">' +
        '<input id="checkbox" type="checkbox"><input id="radio" type="radio">' +
        '<select id="choice"><option>One</option></select>' +
        '<div role="dialog"><button id="dialogButton">OK</button></div>' +
        '<ul role="listbox"><li id="listOption" role="option">Andika</li></ul>' +
        '<ul role="menu"><li id="menuItem" role="menuitem">Copy</li></ul>';
    inChangeLayoutMode = false;
    commands = {
        undo: vi.fn<() => void>(),
        redo: vi.fn<() => void>(),
        isInChangeLayoutMode: () => inChangeLayoutMode,
    };
    detachers = [];
});

afterEach(() => {
    detachers.forEach((detach) => detach());
    page.remove();
    otherFrame.remove();
});

function attach(): void {
    detachers.push(attachUndoAndRedoKeys(window, [otherWindow], commands));
}

function key(
    target: Element,
    type: "keydown" | "keyup",
    init: KeyboardEventInit,
): KeyboardEvent {
    const view = target.ownerDocument.defaultView!;
    const event = new view.KeyboardEvent(type, {
        bubbles: true,
        cancelable: true,
        ...init,
    });
    target.dispatchEvent(event);
    return event;
}

/** Records the events of this type that reach target in the bubble phase. */
function listenInBubblePhase(target: EventTarget, type: string): Event[] {
    const heard: Event[] = [];
    const listener = (event: Event) => heard.push(event);
    target.addEventListener(type, listener);
    detachers.push(() => target.removeEventListener(type, listener));
    return heard;
}

describe("undoOrRedoCommandOf", () => {
    it("tells the undo key from the redo keys and from everything else", () => {
        const of = (init: KeyboardEventInit) =>
            undoOrRedoCommandOf(new KeyboardEvent("keydown", init));
        expect(of({ key: "z", ctrlKey: true })).toBe("undo");
        expect(of({ key: "Z", ctrlKey: true, shiftKey: true })).toBe("redo");
        expect(of({ key: "y", ctrlKey: true })).toBe("redo");
        expect(of({ key: "z", metaKey: true })).toBe("undo");
        // A layout whose Z key types another letter: the key code still says Z.
        expect(of({ key: "ж", keyCode: 90, ctrlKey: true })).toBe("undo");
        expect(of({ key: "z" })).toBeUndefined();
        // AltGr reports Ctrl and Alt both, and types a character.
        expect(of({ key: "z", ctrlKey: true, altKey: true })).toBeUndefined();
        expect(of({ key: "x", ctrlKey: true })).toBeUndefined();
    });
});

describe("the undo and redo keys in the page frame", () => {
    it("run undo and redo, ahead of every handler in the page's document", () => {
        attach();
        const heardByPage = listenInBubblePhase(document, "keydown");

        const undoKey = key(box, "keydown", { key: "z", ctrlKey: true });
        expect(commands.undo).toHaveBeenCalledTimes(1);
        expect(undoKey.defaultPrevented).toBe(true);

        key(box, "keydown", { key: "y", ctrlKey: true });
        key(box, "keydown", { key: "Z", ctrlKey: true, shiftKey: true });
        expect(commands.redo).toHaveBeenCalledTimes(2);
        expect(
            heardByPage,
            "no handler in the document should see an undo or redo key",
        ).toEqual([]);
    });

    it("leaves other keys alone", () => {
        attach();
        const heardByPage = listenInBubblePhase(document, "keydown");
        const letter = key(box, "keydown", { key: "a" });
        expect(letter.defaultPrevented).toBe(false);
        expect(heardByPage).toEqual([letter]);
        expect(commands.undo).not.toHaveBeenCalled();
    });

    it("leaves the keys to origami in Change Layout mode", () => {
        attach();
        inChangeLayoutMode = true;
        const heardByPage = listenInBubblePhase(document, "keydown");
        const undoKey = key(page, "keydown", { key: "z", ctrlKey: true });
        expect(undoKey.defaultPrevented).toBe(false);
        expect(heardByPage).toEqual([undoKey]);
        expect(commands.undo).not.toHaveBeenCalled();
    });

    it("keeps the keyups of an undo key from the page, until the last modifier goes up", () => {
        attach();
        const heardByPage = listenInBubblePhase(document, "keyup");
        key(box, "keydown", { key: "z", ctrlKey: true });
        key(box, "keyup", { key: "z", ctrlKey: true });
        key(box, "keyup", { key: "Control" });
        expect(
            heardByPage,
            "the keyups of Ctrl+Z would schedule the toolbox's markup pass",
        ).toEqual([]);

        const nextKeyUp = key(box, "keyup", { key: "a" });
        expect(heardByPage).toEqual([nextKeyUp]);
    });

    it("keeps no keyup from the page after the page window loses the focus with Ctrl still down", () => {
        attach();
        const heardByPage = listenInBubblePhase(document, "keyup");
        key(box, "keydown", { key: "z", ctrlKey: true });
        // The user moves to the toolbox and lets go of Ctrl there: the page hears no keyup.
        window.dispatchEvent(new FocusEvent("blur"));

        const nextKeyUp = key(box, "keyup", { key: "A", shiftKey: true });
        expect(heardByPage).toEqual([nextKeyUp]);
    });

    it("keeps no keyup from the page after a keydown with no modifier down", () => {
        attach();
        const heardByPage = listenInBubblePhase(document, "keyup");
        key(box, "keydown", { key: "z", ctrlKey: true });
        key(box, "keydown", { key: "a" });
        const nextKeyUp = key(box, "keyup", { key: "a" });
        expect(heardByPage).toEqual([nextKeyUp]);
    });

    it("still keeps the keyups of an undo key from the page when the focus leaves only an element", () => {
        attach();
        const heardByPage = listenInBubblePhase(document, "keyup");
        key(box, "keydown", { key: "z", ctrlKey: true });
        box.dispatchEvent(new FocusEvent("blur"));
        key(box, "keyup", { key: "z", ctrlKey: true });
        expect(heardByPage).toEqual([]);
    });

    it("leaves the keys to an input, a textarea or a dialog in the page, but not to a text box", () => {
        page.insertAdjacentHTML(
            "beforeend",
            '<textarea id="calendarText"></textarea>' +
                '<div role="dialog"><input id="titleField" type="text"></div>',
        );
        attach();
        const inTextarea = key(
            page.querySelector("#calendarText")!,
            "keydown",
            {
                key: "z",
                ctrlKey: true,
            },
        );
        const inDialog = key(page.querySelector("#titleField")!, "keydown", {
            key: "y",
            ctrlKey: true,
        });
        expect(commands.undo).not.toHaveBeenCalled();
        expect(commands.redo).not.toHaveBeenCalled();
        expect(inTextarea.defaultPrevented).toBe(false);
        expect(inDialog.defaultPrevented).toBe(false);

        const inBox = key(box, "keydown", { key: "z", ctrlKey: true });
        expect(commands.undo).toHaveBeenCalledTimes(1);
        expect(inBox.defaultPrevented).toBe(true);
    });

    it("leaves the keys alone in the Format dialog and in its font list, which is outside the dialog", () => {
        // The Format dialog (StyleEditor.pug) is #format-toolbar with no role. MUI renders the
        // open font list in a portal at the end of the body, and the focus is on an option.
        page.insertAdjacentHTML(
            "beforeend",
            '<div id="format-toolbar" class="bloom-ui bloomDialogContainer">' +
                '<div id="fontSelectComponent"><div id="fontSelect" role="combobox" tabindex="0">Andika</div></div>' +
                '<select id="size-select"><option>12</option></select></div>',
        );
        document.body.insertAdjacentHTML(
            "beforeend",
            '<div id="fontPopover" role="presentation"><ul role="listbox">' +
                '<li id="fontOption" role="option" tabindex="0">Andika</li></ul></div>',
        );
        attach();
        try {
            // A select counts as no text field elsewhere; here the dialog around it counts.
            for (const id of ["fontSelect", "size-select", "fontOption"]) {
                const event = key(document.getElementById(id)!, "keydown", {
                    key: "z",
                    ctrlKey: true,
                });
                expect(event.defaultPrevented, `in #${id}`).toBe(false);
            }
            expect(
                commands.undo,
                "undoing the page under the Format dialog breaks CKEditor's locked selection",
            ).not.toHaveBeenCalled();
        } finally {
            document.getElementById("fontPopover")!.remove();
        }
    });

    it("undoes the page from a checkbox on the page", () => {
        page.insertAdjacentHTML(
            "beforeend",
            '<input id="pageCheckbox" type="checkbox">',
        );
        attach();
        const event = key(page.querySelector("#pageCheckbox")!, "keydown", {
            key: "z",
            ctrlKey: true,
        });
        expect(event.defaultPrevented).toBe(true);
        expect(commands.undo).toHaveBeenCalledTimes(1);
    });

    it("leaves the browser's own undo of an input or textarea in the page alone", () => {
        page.insertAdjacentHTML(
            "beforeend",
            '<textarea id="calendarText"></textarea>',
        );
        attach();
        const event = new InputEvent("beforeinput", {
            bubbles: true,
            cancelable: true,
            inputType: "historyUndo",
        });
        page.querySelector("#calendarText")!.dispatchEvent(event);
        expect(event.defaultPrevented).toBe(false);
    });

    it("cancels the browser's own undo and redo of a text box", () => {
        attach();
        const cancelled = (inputType: string) => {
            const event = new InputEvent("beforeinput", {
                bubbles: true,
                cancelable: true,
                inputType,
            });
            box.dispatchEvent(event);
            return event.defaultPrevented;
        };
        expect(cancelled("historyUndo")).toBe(true);
        expect(cancelled("historyRedo")).toBe(true);
        expect(cancelled("insertText")).toBe(false);
    });

    it("stops listening when detached", () => {
        attach();
        detachers.pop()!();
        key(box, "keydown", { key: "z", ctrlKey: true });
        expect(commands.undo).not.toHaveBeenCalled();
    });
});

describe("the undo and redo keys in another frame", () => {
    const otherById = (id: string) => otherWindow.document.getElementById(id)!;

    it("run undo and redo from anywhere but a text field or a dialog", () => {
        attach();
        const undoKey = key(otherById("toolButton"), "keydown", {
            key: "z",
            ctrlKey: true,
        });
        expect(commands.undo).toHaveBeenCalledTimes(1);
        expect(undoKey.defaultPrevented).toBe(true);
        key(otherWindow.document.body, "keydown", { key: "y", ctrlKey: true });
        expect(commands.redo).toHaveBeenCalledTimes(1);
    });

    it("leave the keys to a text field and to a dialog", () => {
        attach();
        const inField = key(otherById("field"), "keydown", {
            key: "z",
            ctrlKey: true,
        });
        const inDialog = key(otherById("dialogButton"), "keydown", {
            key: "z",
            ctrlKey: true,
        });
        expect(commands.undo).not.toHaveBeenCalled();
        expect(inField.defaultPrevented).toBe(false);
        expect(inDialog.defaultPrevented).toBe(false);
    });

    it("leave the keys to any input that edits text", () => {
        attach();
        for (const id of ["untypedField", "numberField"]) {
            const event = key(otherById(id), "keydown", {
                key: "z",
                ctrlKey: true,
            });
            expect(event.defaultPrevented, `in #${id}`).toBe(false);
        }
        expect(commands.undo).not.toHaveBeenCalled();
    });

    it("undo the page from a checkbox, a radio or a select, which have no undo of their own", () => {
        // After a click on a toolbox checkbox such as Rounded Corners, the focus stays on it.
        attach();
        for (const id of ["checkbox", "radio", "choice"]) {
            const event = key(otherById(id), "keydown", {
                key: "z",
                ctrlKey: true,
            });
            expect(event.defaultPrevented, `in #${id}`).toBe(true);
        }
        expect(commands.undo).toHaveBeenCalledTimes(3);
        key(otherById("checkbox"), "keydown", { key: "y", ctrlKey: true });
        expect(commands.redo).toHaveBeenCalledTimes(1);
    });

    it("leave the keys to an open list or menu", () => {
        attach();
        const inList = key(otherById("listOption"), "keydown", {
            key: "z",
            ctrlKey: true,
        });
        const inMenu = key(otherById("menuItem"), "keydown", {
            key: "y",
            ctrlKey: true,
        });
        expect(commands.undo).not.toHaveBeenCalled();
        expect(commands.redo).not.toHaveBeenCalled();
        expect(inList.defaultPrevented).toBe(false);
        expect(inMenu.defaultPrevented).toBe(false);
    });
});

describe("the undo key after typing", () => {
    it("undoes the typing, which the input transactions end first", async () => {
        const history = createPageHistory({ root: page });
        const endOfTask: (() => void)[] = [];
        detachers.push(
            attachInputTransactions(history, window, [otherWindow], {
                isFromUser: () => true,
                afterCurrentTask: (fn) => endOfTask.push(fn),
            }),
        );
        detachers.push(
            attachUndoAndRedoKeys(window, [otherWindow], {
                undo: () => void history.undo(),
                redo: () => void history.redo(),
                isInChangeLayoutMode: () => false,
            }),
        );
        detachers.push(() => history.dispose());
        const paragraph = box.querySelector("p")!;

        // A key press in the box opens a typing transaction, which stays open between key presses.
        key(box, "keydown", { key: "a" });
        paragraph.textContent = "Helloa";
        expect(
            history.hasOpenTransaction,
            "test setup: typing should hold a transaction open",
        ).toBe(true);

        key(box, "keydown", { key: "z", ctrlKey: true });
        // undo() is async; let it finish.
        await Promise.resolve();
        await new Promise((resolve) => setTimeout(resolve, 0));
        endOfTask.forEach((fn) => fn());

        expect(paragraph.textContent).toBe("Hello");
        expect(history.redoDescriptionL10nKey).toBeDefined();
    });
});
