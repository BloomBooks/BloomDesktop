// The page settings that the Book and Page Settings dialog changes are not undoable, and changing
// them, previewing them or cancelling them keeps the page history. The dialog lives in the root
// window, so the tests attach the input transactions with this window, where the dialog renders,
// as one of the other windows, and an empty frame as the page's. The user's events are the ones in
// userEvents, and a test says where the browser's task ends by calling finishTask(), as in
// imageDescriptionUndo.test.tsx.
import * as React from "react";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../utils/reactRender";
import { createPageHistory } from "../pageHistory/pageHistory";
import { attachInputTransactions } from "../pageHistory/pageHistoryInput";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    postJson: vi.fn(),
}));

// The page frame's exports, through which pageHistoryAccess.ts finds the history.
vi.mock("../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../js/workspaceFrames")>()),
    getEditablePageBundleExports: () =>
        mocks.history ? { pageHistory: () => mocks.history } : null,
}));

vi.mock("./BookSettingsConfigrPages", () => ({
    useBookSettingsAreaDefinition: () => ({
        pageKey: "bookArea",
        label: "Book",
        content: null,
        pages: [],
    }),
}));

vi.mock("../../utils/shared", () => ({
    getPageIframeBody: () => document.body,
    getBloomPageElement: () =>
        document.body.querySelector(".bloom-page") as HTMLElement | null,
    whenBloomPageIsReady: (callback: (page: HTMLElement) => void) => {
        const page = document.body.querySelector(".bloom-page") as HTMLElement;
        callback(page);
        return () => {};
    },
}));

vi.mock("../../utils/bloomApi", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("../../utils/bloomApi")>();
    return {
        ...actual,
        post: vi.fn(),
        postBoolean: vi.fn(),
        postJson: mocks.postJson,
        useApiBoolean: () => [true],
        useApiObject: (_endpoint: string, defaultValue: unknown) =>
            defaultValue,
        useApiStringState: () => [
            JSON.stringify({ appearance: { cssThemeName: "default" } }),
        ],
    };
});

vi.mock("../../react_components/l10nHooks", () => ({
    useL10n: (englishText: string) => englishText,
}));

vi.mock("../../react_components/featureStatus", () => ({
    useGetFeatureStatus: () => ({ enabled: true }),
}));

vi.mock("../../react_components/BloomDialog/BloomDialogPlumbing", () => ({
    useSetupBloomDialog: () => ({
        closeDialog: () => {},
        propsForBloomDialog: { open: true },
    }),
}));

vi.mock("../../react_components/BloomDialog/BloomDialog", () => {
    const MockBloomDialog = React.forwardRef<
        HTMLDivElement,
        React.PropsWithChildren<{ onCancel: () => void }>
    >((props, ref) => (
        <div ref={ref}>
            {props.children}
            <button data-testid="dialog-cancel" onClick={props.onCancel}>
                Cancel
            </button>
        </div>
    ));
    MockBloomDialog.displayName = "MockBloomDialog";

    return {
        BloomDialog: MockBloomDialog,
        DialogBottomButtons: (props: React.PropsWithChildren<object>) => (
            <div>{props.children}</div>
        ),
        DialogMiddle: (props: React.PropsWithChildren<object>) => (
            <div>{props.children}</div>
        ),
        DialogTitle: (props: { title: string }) => <div>{props.title}</div>,
    };
});

vi.mock("../../react_components/BloomDialog/commonDialogComponents", () => ({
    DialogOkButton: (props: { onClick: () => void }) => (
        <button data-testid="dialog-ok" onClick={props.onClick}>
            OK
        </button>
    ),
    DialogCancelButton: () => null,
}));

vi.mock("@sillsdev/config-r", () => ({
    ConfigrPane: (props: {
        children: React.ReactNode;
        initialValues: {
            page: Record<string, string>;
        };
        onChange: (settings: unknown) => void;
    }) => (
        <div>
            <button
                data-testid="page-change"
                onClick={() =>
                    props.onChange({
                        ...props.initialValues,
                        page: {
                            ...props.initialValues.page,
                            backgroundColor: "#ABCDEF",
                        },
                    })
                }
            >
                Page Change
            </button>
            <button
                data-testid="page-reset"
                onClick={() =>
                    props.onChange({
                        ...props.initialValues,
                        page: { ...props.initialValues.page },
                    })
                }
            >
                Page Reset
            </button>
            {props.children}
        </div>
    ),
    ConfigrArea: (
        props: React.PropsWithChildren<{ content?: React.ReactNode }>,
    ) => (
        <div>
            {props.content}
            {props.children}
        </div>
    ),
    ConfigrPage: () => null,
    ConfigrGroup: () => null,
}));

import { BookAndPageSettingsDialog } from "./BookAndPageSettingsDialog";

const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";

let history: PageHistory;
let page: HTMLElement;
let img: HTMLImageElement;
let other: HTMLElement;
let container: HTMLDivElement;
let detach: () => void;
let userEvents: WeakSet<Event>;
let endOfTask: (() => void)[];
let onUnmarkedChange: ReturnType<typeof vi.fn>;

beforeEach(async () => {
    document.body.innerHTML =
        // The second picture is a link grid's thumbnail that has not arrived yet (linkGrid.ts).
        '<div class="bloom-page"><div class="marginBox"><div class="bloom-canvas"><img src="a.png"></div><div class="bloom-bookButton"><img id="thumbnail"></div><div id="other"></div></div></div>';
    page = document.body.querySelector(".bloom-page")!;
    img = page.querySelector("img")!;
    other = page.querySelector("#other")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({ root: page, onUnmarkedChange });
    mocks.history = history;
    mocks.postJson.mockReset();
    // A step before the dialog, which must survive it.
    await history.doTransaction(kEarlierKey, () =>
        other.setAttribute("data-earlier", "true"),
    );
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);

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

    container = document.createElement("div");
    document.body.appendChild(container);
    await act(async () => {
        renderRoot(<BookAndPageSettingsDialog />, container);
    });
});

afterEach(() => {
    unmountRoot(container);
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

/** A click by the user on the element with this data-testid, as one task, and the end of it. */
function click(testId: string): void {
    const target = container.querySelector(`[data-testid="${testId}"]`);
    if (!target) fail(`The dialog did not render ${testId}.`);
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

function backgroundColor(): string {
    return page.style.getPropertyValue("--page-background-color");
}

function expectHistoryKept(): void {
    expect(history.hasOpenTransaction).toBe(false);
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    expect(onUnmarkedChange).not.toHaveBeenCalled();
}

describe("The Book and Page Settings dialog's page settings", () => {
    it("preview without making a step or ending the history", () => {
        click("page-change");

        expect(backgroundColor()).toBe("#ABCDEF");
        expect(img.getAttribute("src")).toBe("a.png?transparent=yes");
        expectHistoryKept();

        click("page-reset");

        expect(backgroundColor()).toBe("");
        expect(img.getAttribute("src")).toBe("a.png");
        expectHistoryKept();
    });

    it("are kept by OK, which is no undo step either", async () => {
        click("page-change");
        click("dialog-ok");

        expect(backgroundColor()).toBe("#ABCDEF");
        expect(img.getAttribute("src")).toBe("a.png?transparent=yes");
        expect(mocks.postJson).not.toHaveBeenCalled();
        expectHistoryKept();

        // Undo reverses the earlier step and leaves the page settings alone.
        await act(async () => history.undo());
        expect(other.hasAttribute("data-earlier")).toBe(false);
        expect(backgroundColor()).toBe("#ABCDEF");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("are put back by Cancel, pictures included, keeping the history", () => {
        click("page-change");
        expect(backgroundColor()).toBe("#ABCDEF");
        expect(img.getAttribute("src")).toBe("a.png?transparent=yes");

        click("dialog-cancel");

        expect(backgroundColor()).toBe("");
        expect(img.getAttribute("src")).toBe("a.png");
        expectHistoryKept();
    });

    it("keep a picture that something else replaced while the dialog was open, putting back only its transparency", () => {
        // As when a link grid's thumbnail arrives after the dialog opened (linkGrid.ts).
        history.doOutsideHistory(() => img.setAttribute("src", "b.png"));

        click("page-change");
        expect(img.getAttribute("src")).toBe("b.png?transparent=yes");
        click("page-reset");
        expect(img.getAttribute("src")).toBe("b.png");

        click("page-change");
        click("dialog-cancel");
        expect(backgroundColor()).toBe("");
        expect(img.getAttribute("src")).toBe("b.png");
        expectHistoryKept();
    });

    it("keep a picture that got its first src while the dialog was open, without the transparency of a preview", () => {
        const thumbnail = page.querySelector("#thumbnail")!;
        expect(thumbnail.hasAttribute("src")).toBe(false);
        // The thumbnail arrives, outside the history, as linkGrid.ts writes it.
        history.doOutsideHistory(() =>
            thumbnail.setAttribute("src", "bookButton-1.png"),
        );

        click("page-change");
        expect(thumbnail.getAttribute("src")).toBe(
            "bookButton-1.png?transparent=yes",
        );
        click("page-reset");
        expect(thumbnail.getAttribute("src")).toBe("bookButton-1.png");

        click("page-change");
        click("dialog-cancel");
        expect(backgroundColor()).toBe("");
        expect(thumbnail.getAttribute("src")).toBe("bookButton-1.png");
        expectHistoryKept();
    });
});
