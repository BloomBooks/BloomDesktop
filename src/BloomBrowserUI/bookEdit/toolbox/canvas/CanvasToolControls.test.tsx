import { act } from "react";
import tinycolor from "tinycolor2";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../../utils/reactRender";
import { CanvasElementManager } from "../../js/canvasElementManager/CanvasElementManager";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import { PageHistory } from "../../pageHistory/pageHistoryTypes";
import { getColorInfoFromSpecialNameOrColorString } from "../../../react_components/color-picking/bloomPalette";
import {
    DialogResult,
    IColorPickerDialogProps,
} from "../../../react_components/color-picking/colorPickerDialog";
import { ToolBox } from "../toolbox";
import type { CanvasTool } from "./canvasTool";
import CanvasToolControls from "./CanvasToolControls";

// vitest.setup.ts mocks comicaljs without Comical, and with a Bubble that reads nothing.
// Like the real one, this Bubble reads its spec from data-bubble once, when it is made.
// An element with no family has no ancestors.
const comicaljs = vi.hoisted(() => {
    class Bubble {
        public content: HTMLElement;
        private spec: object;
        public constructor(element: HTMLElement) {
            this.content = element;
            this.spec = Bubble.getBubbleSpec(element);
        }
        public static getBubbleSpec(element: HTMLElement) {
            return JSON.parse(
                element.getAttribute("data-bubble")!.replace(/`/g, '"'),
            );
        }
        public getBubbleSpec() {
            return this.spec;
        }
    }
    return {
        Bubble,
        Node: vi.fn(),
        Parser: vi.fn(),
        BubbleSpec: {},
        TailSpec: {},
        Comical: {
            findAncestors: () => [],
        },
    };
});
vi.mock("comicaljs", () => comicaljs);

// What the tests put in the page frame and the root window: the CanvasElementManager stand-in
// (see makeManager), the page history, and the props of the colour picker the panel last showed.
const frames = vi.hoisted(() => ({
    manager: undefined as unknown,
    history: undefined as PageHistory | undefined,
    colorPickerProps: undefined as IColorPickerDialogProps | undefined,
}));
vi.mock("./canvasElementPageBridge", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getCanvasElementManager: () => frames.manager,
}));
// The panel reaches the page history through the page frame's exports (pageHistoryAccess.ts),
// and shows the colour picker through the root window's.
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history ? { pageHistory: () => frames.history } : null,
    getWorkspaceBundleExports: () => ({
        showColorPickerDialog: (props: IColorPickerDialogProps) =>
            (frames.colorPickerProps = props),
        hideColorPickerDialog: () => undefined,
    }),
}));

// canvasTool.tsx as it is after the dev server has loaded it again as a new module: its
// CanvasTool has no theOneCanvasTool. The panel must not depend on it, only on the tool that it is
// given.
vi.mock("./canvasTool", () => ({
    CanvasTool: { theOneCanvasTool: undefined },
}));

vi.mock("../../js/videoUtils", () => ({
    deselectVideoContainers: () => undefined,
}));

// Undefined is "no information yet, so not blocked", which keeps the panel's controls live.
vi.mock("../../../react_components/featureStatus", () => ({
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => "",
}));

const kCaptionSpec = {
    version: "1.0",
    style: "caption",
    tails: [],
    level: 1,
    backgroundColors: ["#ffffff"],
    cornerRadiusX: 8,
    cornerRadiusY: 8,
    outerBorderColor: "yellow",
};

let renderedContainer: HTMLDivElement | undefined;
// The Canvas tool the panel was rendered for, as CanvasTool.makeRootElement passes it.
let canvasTool: CanvasTool;

afterEach(() => {
    if (renderedContainer) {
        act(() => {
            unmountRoot(renderedContainer!);
        });
        renderedContainer.remove();
        renderedContainer = undefined;
    }
    frames.manager = undefined;
    frames.colorPickerProps = undefined;
    document.body.innerHTML = "";
});

// A stand-in for the page frame's CanvasElementManager. The methods that matter here (finding
// the active element's bubble, registering for change notifications, and notifying) are the
// real ones. The ones that change the page write what the real ones write that the history sees:
// data-bubble, and the text colour; the rest return just enough for the panel to render.
function makeManager(activeElement: HTMLElement) {
    const manager = {
        activeElement,
        thingsToNotifyOfCanvasElementChange: [],
        turnOnCanvasElementEditing: () => undefined,
        isActiveElementPictureCanvasElement: () => false,
        isActiveElementVideoCanvasElement: () => false,
        getTextColorInformation: () => ({
            color: activeElement.style.color || "black",
            isDefault: !activeElement.style.color,
        }),
        getTextOutlineColorInformation: () => ({
            color: "",
            isDefault: true,
        }),
        getBackgroundColorArray: (spec: { backgroundColors: string[] }) =>
            spec.backgroundColors,
        getDefaultTailSpec: () => ({
            tipX: 1,
            tipY: 2,
            midpointX: 3,
            midpointY: 4,
        }),
        updateSelectedFamilyBubbleSpec: (props: object) => {
            setBubbleSpec(activeElement, {
                ...comicaljs.Bubble.getBubbleSpec(activeElement),
                ...props,
            });
            return new comicaljs.Bubble(activeElement);
        },
        setTextColor: (color: string) => {
            if (color) activeElement.style.color = color;
            else activeElement.style.removeProperty("color");
        },
        setThingToFocusAfterSettingColor: () => undefined,
        // As the real one does for a bubble: an opacity below 1 goes into the colour.
        setBackgroundColor: (
            colors: string[],
            opacity: number | undefined,
        ): void => {
            manager.updateSelectedFamilyBubbleSpec({
                backgroundColors:
                    opacity !== undefined && opacity < 1
                        ? [tinycolor(colors[0]).setAlpha(opacity).toRgbString()]
                        : colors,
            });
        },
        // Comical's drawing is out of reach in jsdom; this records what data-bubble said each
        // time the panel asked for the bubbles to be drawn again from it.
        dataBubbleWhenRedrawn: [] as (string | null)[],
        redrawBubblesFromPage: (): void => {
            manager.dataBubbleWhenRedrawn.push(
                activeElement.getAttribute("data-bubble"),
            );
        },
    };
    const proto = CanvasElementManager.prototype;
    for (const name of [
        "getActiveElement",
        "getPatriarchBubbleOfActiveElement",
        "getSelectedItemBubbleSpec",
        "requestCanvasElementChangeNotification",
        "detachCanvasElementChangeNotification",
        "doNotifyChange",
    ]) {
        manager[name] = proto[name].bind(manager);
    }
    return manager as typeof manager & { doNotifyChange: () => void };
}

function setBubbleSpec(element: HTMLElement, spec: object) {
    element.setAttribute(
        "data-bubble",
        JSON.stringify(spec).replace(/"/g, "`"),
    );
}

function getBubbleSpec(element: HTMLElement) {
    return comicaljs.Bubble.getBubbleSpec(element);
}

/** A canvas element with spec, in a page of its own, made the active element. */
function makeActiveElement(spec: object): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page";
    const element = document.createElement("div");
    element.className = "bloom-canvas-element";
    setBubbleSpec(element, spec);
    page.appendChild(element);
    document.body.appendChild(page);
    frames.manager = makeManager(element);
    return element;
}

function renderPanel() {
    const container = document.createElement("div");
    document.body.appendChild(container);
    renderedContainer = container;
    // React 18's createRoot renders asynchronously; act() flushes the render and its effects.
    canvasTool = {} as CanvasTool;
    act(() => {
        renderRoot(<CanvasToolControls canvasTool={canvasTool} />, container);
    });
    return container;
}

// A select keeps its value in MUI's hidden native input, which has the name given in inputProps.
function getSelectValue(container: HTMLElement, name: string): string {
    const input = container.querySelector<HTMLInputElement>(
        `input[name="${name}"]`,
    );
    if (!input) {
        fail(`The panel did not render the ${name} select.`);
    }
    return input.value;
}

function getCheckbox(container: HTMLElement, label: string): HTMLInputElement {
    const checkbox = Array.from(
        container.querySelectorAll<HTMLElement>(".bloom-checkbox"),
    ).find((c) => c.textContent?.includes(label));
    if (!checkbox) {
        fail(`The panel did not render the ${label} checkbox.`);
    }
    return checkbox.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
}

describe("CanvasToolControls after an undo or redo", () => {
    it("shows the restored style, tail, rounded corners and outline colour of the active element", () => {
        const element = makeActiveElement(kCaptionSpec);

        const container = renderPanel();
        expect(getSelectValue(container, "style")).toBe("caption");
        expect(getCheckbox(container, "ShowTail").checked).toBe(false);
        expect(getCheckbox(container, "RoundedCorners").checked).toBe(true);
        expect(getSelectValue(container, "outlineColor")).toBe("yellow");

        // An undo puts back the data-bubble that the same element had before. The page history
        // then runs CanvasElementManager.updateAfterUndoOrRedo, which ends by notifying the
        // listeners (canvasElementManagerSpec.ts tests that part).
        setBubbleSpec(element, {
            version: "1.0",
            style: "speech",
            tails: [{ tipX: 1, tipY: 2, midpointX: 3, midpointY: 4 }],
            level: 1,
            backgroundColors: ["#ffffff"],
        });
        // The panel does not see the change on its own, so what follows tests the notification.
        expect(getSelectValue(container, "style")).toBe("caption");
        act(() => {
            (frames.manager as ReturnType<typeof makeManager>).doNotifyChange();
        });

        expect(getSelectValue(container, "style")).toBe("speech");
        expect(getCheckbox(container, "ShowTail").checked).toBe(true);
        expect(getCheckbox(container, "RoundedCorners").checked).toBe(false);
        expect(getSelectValue(container, "outlineColor")).toBe("none");
    });
});

describe("CanvasToolControls on a new page", () => {
    it("registers for the new page's change notifications through the tool it is given", () => {
        makeActiveElement(kCaptionSpec);
        const container = renderPanel();
        expect(canvasTool.callOnNewPageReady).toBeInstanceOf(Function);

        // A new page has a new CanvasElementManager, which the tool tells the panel about.
        document.body
            .querySelectorAll(".bloom-page")
            .forEach((page) => page.remove());
        const element = makeActiveElement({ ...kCaptionSpec, style: "none" });
        const newManager = frames.manager as ReturnType<typeof makeManager>;
        act(() => canvasTool.callOnNewPageReady());
        expect(getSelectValue(container, "style")).toBe("none");

        // An undo on the new page puts back data-bubble and notifies.
        setBubbleSpec(element, { ...kCaptionSpec, style: "speech" });
        act(() => newManager.doNotifyChange());

        expect(getSelectValue(container, "style")).toBe("speech");
    });
});

// The panel lives in the toolbox frame, so the tests attach the input transactions with this
// window, where the panel renders, as one of the other windows, and an empty frame as the page's.
// As in pageHistoryInputSpec.ts, the user's events are the ones in userEvents, and a test says
// where the browser's task ends by calling finishTask().
describe("CanvasToolControls changes as undo steps", () => {
    let history: PageHistory;
    let element: HTMLElement;
    let container: HTMLElement;
    let detach: () => void;
    let userEvents: WeakSet<Event>;
    let endOfTask: (() => void)[];
    let whenClosingTool: (() => void)[];

    beforeEach(() => {
        element = makeActiveElement(kCaptionSpec);
        history = createPageHistory({
            root: element.parentElement!,
            onUnmarkedChange: vi.fn(),
        });
        frames.history = history;
        // What listenToPageHistory does through updateAfterUndoOrRedo.
        history.onAfterUndoOrRedo(() =>
            (frames.manager as ReturnType<typeof makeManager>).doNotifyChange(),
        );
        const pageFrame = document.createElement("iframe");
        document.body.appendChild(pageFrame);
        userEvents = new WeakSet();
        endOfTask = [];
        detach = attachInputTransactions(
            history,
            pageFrame.contentWindow!,
            [window],
            {
                isFromUser: (event) => userEvents.has(event),
                afterCurrentTask: (fn) => endOfTask.push(fn),
            },
        );
        whenClosingTool = [];
        vi.spyOn(ToolBox, "addWhenClosingToolTask").mockImplementation(
            (task) => {
                whenClosingTool.push(task);
            },
        );
        container = renderPanel();
    });

    afterEach(() => {
        detach();
        history.dispose();
        frames.history = undefined;
        vi.restoreAllMocks();
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
        act(() => {
            mouse(target, "pointerdown", 1);
            mouse(target, "mousedown", 1);
        });
        act(() => {
            mouse(target, "pointerup", 0);
            mouse(target, "mouseup", 0);
            mouse(target, "click", 0);
            finishTask();
        });
    }

    /** Open the Element Type select and choose style from its menu. */
    function chooseStyle(style: string): void {
        // inputProps put the id on MUI's hidden native input; the user presses the element
        // beside it that shows the value.
        const select = container
            .querySelector("#canvasElement-style-dropdown")
            ?.parentElement?.querySelector(".MuiSelect-select");
        if (!select) fail("The panel did not render the Element Type select.");
        click(select);
        const item = document.body.querySelector(
            `li[role="option"][data-value="${style}"]`,
        );
        if (!item) fail(`The Element Type menu has no ${style} item.`);
        click(item);
    }

    function openColorPicker(barId: string): IColorPickerDialogProps {
        const bar = container
            .querySelector(`label[for="${barId}"]`)
            ?.parentElement?.querySelector(".MuiInput-formControl");
        if (!bar) fail(`The panel did not render the ${barId}.`);
        click(bar);
        if (!frames.colorPickerProps)
            fail("The panel showed no colour picker.");
        return frames.colorPickerProps;
    }

    async function undo(): Promise<void> {
        await act(async () => {
            await history.undo();
        });
    }

    async function redo(): Promise<void> {
        await act(async () => {
            await history.redo();
        });
    }

    it("makes a style change one step, which undo and redo reverse", async () => {
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        chooseStyle("speech");

        expect(getBubbleSpec(element).style).toBe("speech");
        expect(history.hasOpenTransaction).toBe(false);
        // A step with the control's description, not one that ended the history.
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.ElementType",
        );

        await undo();
        expect(getBubbleSpec(element).style).toBe("caption");
        expect(getSelectValue(container, "style")).toBe("caption");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.ElementType",
        );

        await redo();
        expect(getBubbleSpec(element).style).toBe("speech");
        expect(getSelectValue(container, "style")).toBe("speech");
    });

    it("ends the history for a change from the toolbox that makes no transaction of its own", () => {
        // The check that the other tests' "the history survives" can fail: this is what each
        // panel change did before it had a transaction.
        chooseStyle("speech");
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.ElementType",
        );
        const button = document.createElement("button");
        button.addEventListener("click", () =>
            element.setAttribute("data-unwrapped", "true"),
        );
        container.appendChild(button);

        click(button);

        expect(element.getAttribute("data-unwrapped")).toBe("true");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes two changes two steps", async () => {
        chooseStyle("speech");
        click(getCheckbox(container, "ShowTail"));

        expect(getBubbleSpec(element).tails.length).toBe(1);
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.ShowTail",
        );

        await undo();
        expect(getBubbleSpec(element).tails.length).toBe(0);
        expect(getBubbleSpec(element).style).toBe("speech");
        expect(getCheckbox(container, "ShowTail").checked).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.ElementType",
        );

        await undo();
        expect(getBubbleSpec(element).style).toBe("caption");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    // What ColorPickerDialog does on Cancel (onClose in colorPickerDialog.tsx): put the initial
    // colour back through onChange, then call close.
    function cancel(props: IColorPickerDialogProps): void {
        act(() => {
            props.onChange(props.initialColor);
            props.close!(DialogResult.Cancel);
        });
    }

    it("makes a colour picker session one step, from opening it to OK", async () => {
        expect(element.hasAttribute("style")).toBe(false);
        const props = openColorPicker("text-color-bar");
        // The picker is open, and so is its transaction.
        expect(history.hasOpenTransaction).toBe(true);

        act(() => {
            props.onChange(getColorInfoFromSpecialNameOrColorString("#ff0000"));
            props.onChange(getColorInfoFromSpecialNameOrColorString("#0000ff"));
        });
        expect(element.style.color).toBe("rgb(0, 0, 255)");
        act(() => props.close!(DialogResult.OK));

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.TextColor",
        );
        await undo();
        expect(element.hasAttribute("style")).toBe(false);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("leaves no step for a colour picker session that is cancelled", () => {
        chooseStyle("speech");
        expect(element.hasAttribute("style")).toBe(false);
        const props = openColorPicker("text-color-bar");
        act(() =>
            props.onChange(getColorInfoFromSpecialNameOrColorString("#ff0000")),
        );
        expect(element.style.color).toBe("rgb(255, 0, 0)");

        cancel(props);

        // The colour that was there before is back: the element had none, and Cancel's
        // onChange alone would have given it the initial colour of the picker.
        expect(element.hasAttribute("style")).toBe(false);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.ElementType",
        );
    });

    it("draws the bubbles again from the page after a Cancel puts back a transparent background", () => {
        const manager = frames.manager as ReturnType<typeof makeManager>;
        history.doOutsideHistory(() =>
            setBubbleSpec(element, {
                ...kCaptionSpec,
                style: "none",
                backgroundColors: ["rgba(255, 0, 0, 0)"],
            }),
        );
        act(() => manager.doNotifyChange());
        const original = element.getAttribute("data-bubble");
        expect(getBubbleSpec(element).backgroundColors).toEqual([
            "rgba(255, 0, 0, 0)",
        ]);
        const props = openColorPicker("background-color-bar");
        // BL-9922: the picker starts a fully transparent background as an opaque colour.
        expect(props.initialColor.opacity).toBeGreaterThanOrEqual(1);
        act(() =>
            props.onChange(getColorInfoFromSpecialNameOrColorString("#0000ff")),
        );

        act(() => props.onChange(props.initialColor));
        // What Cancel's onChange drew is not what the page had.
        expect(element.getAttribute("data-bubble")).not.toBe(original);
        expect(manager.dataBubbleWhenRedrawn).toEqual([]);
        act(() => props.close!(DialogResult.Cancel));

        expect(element.getAttribute("data-bubble")).toBe(original);
        // Drawn again once, after the rollback had put the transparent background back.
        expect(manager.dataBubbleWhenRedrawn).toEqual([original]);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("ends the colour picker's step when the tool closes with the picker open", () => {
        const props = openColorPicker("text-color-bar");
        act(() =>
            props.onChange(getColorInfoFromSpecialNameOrColorString("#ff0000")),
        );
        expect(whenClosingTool.length).toBe(1);

        act(() => whenClosingTool.forEach((task) => task()));

        expect(history.hasOpenTransaction).toBe(false);
        expect(element.style.color).toBe("rgb(255, 0, 0)");
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Toolbox.ComicTool.Options.TextColor",
        );
    });
});
