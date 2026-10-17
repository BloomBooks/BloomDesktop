import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { unmountRoot } from "../../../utils/reactRender";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import { PageHistory } from "../../pageHistory/pageHistoryTypes";
import { requestEndsPageHistory } from "../../../utils/bloomApi";
import { copyContentToTargetAndCleanup } from "../../js/dragActivityRuntimeUtils";
import {
    copyContentToTargetsAfterUndoOrRedo,
    enableDraggingTargets,
    GameTool,
} from "./GameTool";

// What the tests put in the page frame, the calls the tool made to Bloom's API (posts holds the
// POSTs among them), and the canvas element manager with its active element.
const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    apiCalls: [] as string[],
    posts: [] as string[],
    manager: undefined as object | undefined,
    activeElement: undefined as HTMLElement | undefined,
}));
// The tool reaches the page history through the page frame's exports (pageHistoryAccess.ts).
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history ? { pageHistory: () => frames.history } : null,
}));
// Unless a test gives one, there is no canvas element manager, so no canvas element is active.
vi.mock("../canvas/canvasElementPageBridge", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getCanvasElementManager: () => frames.manager,
}));
// Bloom's answers: one built-in sound of each kind, and "mine.mp3" from the Choose dialog.
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getWithPromise: async (url: string) => {
        frames.apiCalls.push(url);
        const prefix = url.includes("correct_") ? "correct" : "wrong";
        return { data: { files: [`${prefix}_bell.mp3`] } };
    },
    postJson: async (url: string) => {
        frames.apiCalls.push(url);
        frames.posts.push(url);
        if (url === "fileIO/chooseFile") return { data: "mine.mp3" };
        return undefined;
    },
    postData: async (url: string) => {
        frames.apiCalls.push(url);
        frames.posts.push(url);
    },
    postString: async (url: string) => {
        frames.apiCalls.push(url);
        frames.posts.push(url);
    },
}));
// Undefined is "no information yet, so not blocked", which keeps the tool's controls live.
vi.mock("../../../react_components/featureStatus", () => ({
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => "",
}));

// The tool lives in the toolbox frame, and reaches the page through the iframe with the id
// "page" (getPageIframeBody in utils/shared.ts), so the tests make that iframe here and attach
// the input transactions with this window, where the tool renders, as one of the other windows.
// As in CanvasToolControls.test.tsx, the user's events are the ones in userEvents, and a test
// says where the browser's task ends by calling finishTask().
describe("GameTool options and sounds and the page history", () => {
    let history: PageHistory;
    let page: HTMLElement;
    let root: HTMLDivElement;
    let tool: GameTool;
    let detach: () => void;
    let userEvents: WeakSet<Event>;
    let endOfTask: (() => void)[];
    let onUnmarkedChange: ReturnType<typeof vi.fn>;

    beforeEach(() => {
        frames.apiCalls = [];
        frames.posts = [];
        const pageFrame = document.createElement("iframe");
        pageFrame.id = "page";
        document.body.appendChild(pageFrame);
        page = pageFrame.contentDocument!.createElement("div");
        page.className = "bloom-page";
        pageFrame.contentDocument!.body.appendChild(page);
        onUnmarkedChange = vi.fn();
        // The filter that the Edit tab's history has.
        history = createPageHistory({
            root: page,
            onUnmarkedChange,
            filter: createEditTimeMarkupFilter(),
        });
        frames.history = history;
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
    });

    afterEach(() => {
        act(() => unmountRoot(root));
        detach();
        history.dispose();
        frames.history = undefined;
        frames.manager = undefined;
        frames.activeElement = undefined;
        document.body.innerHTML = "";
    });

    /** Give the page the game's attributes, then show the tool for it. */
    async function renderTool(attributes: Record<string, string>) {
        history.doOutsideHistory(() => {
            for (const name of Object.keys(attributes))
                page.setAttribute(name, attributes[name]);
        });
        await act(async () => {
            tool = new GameTool();
            root = tool.makeRootElement();
            document.body.appendChild(root);
        });
    }

    /**
     * The tool sent no POST that ends the page history. The tests mock the functions that send
     * requests, so the request interceptor in bloomApi.ts never sees them; this asks it instead.
     */
    function expectPostsKeepTheHistory(): void {
        expect(frames.posts.length).toBeGreaterThan(0);
        for (const url of frames.posts)
            expect(
                requestEndsPageHistory("post", "/bloom/api/" + url),
                `the tool posted ${url}, which ends the page history`,
            ).toBe(false);
    }

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

    /** The clickable box of the option whose icon is imageName. */
    function getOption(imageName: string): HTMLElement {
        const image = root.querySelector(`img[src="images/${imageName}.svg"]`);
        if (!image) fail(`The tool did not render the ${imageName} option.`);
        return image.parentElement!;
    }

    /** Open the menu of the sound for a correct answer and choose the item with the id soundId. */
    function chooseSound(soundId: string): void {
        // MUI keeps a select's value in a hidden native input beside the element that the user
        // presses. The theme menu comes first; the correct sound's menu is the first that holds
        // a sound, and the page has none, so it holds "default".
        const select = Array.from(
            root.querySelectorAll<HTMLInputElement>(".MuiSelect-nativeInput"),
        )
            .find((input) => input.value === "default")
            ?.parentElement?.querySelector(".MuiSelect-select");
        if (!select) fail("The tool did not render the sound menu.");
        click(select);
        const item = document.body.querySelector(
            `li[role="option"][data-value="${soundId}"]`,
        );
        if (!item) fail(`The sound menu has no ${soundId} item.`);
        click(item);
    }

    /** An earlier undo step, which the tests expect the tool's changes to leave in place. */
    async function makeEarlierStep(): Promise<void> {
        await history.doTransaction("Test.EarlierStep", () =>
            page.setAttribute("data-earlier", "true"),
        );
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
    }

    /**
     * The tool's change made no step, the earlier step is still the one to undo, and undoing it
     * leaves the tool's change on the page.
     */
    async function expectEarlierStepSurvives(
        attribute: string,
        value: string | null,
    ): Promise<void> {
        expect(page.getAttribute(attribute)).toBe(value);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe("Test.EarlierStep");
        expect(onUnmarkedChange.mock.calls).toEqual([]);

        await act(async () => {
            await history.undo();
        });
        expect(page.hasAttribute("data-earlier")).toBe(false);
        expect(page.getAttribute(attribute)).toBe(value);
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    }

    const kDragGame = { "data-activity": "drag-image-to-target" };
    // A legacy game shows its sound menus on the Start tab.
    const kLegacyGame = { "data-activity": "simple-checkbox-quiz" };

    it("ends the history for a change from the toolbox that is recorded", async () => {
        // The check that the other tests' "the earlier step survives" can fail.
        await renderTool(kDragGame);
        await makeEarlierStep();
        const button = document.createElement("button");
        button.addEventListener("click", () =>
            page.setAttribute("data-same-size", "false"),
        );
        root.appendChild(button);

        click(button);

        expect(page.getAttribute("data-same-size")).toBe("false");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("describes the step of dragging a target in the page", async () => {
        await renderTool(kDragGame);
        frames.manager = { setActiveElement: () => {} };
        const target = page.ownerDocument.createElement("div");
        target.setAttribute("data-target-of", "d1");
        target.style.left = "5px";
        target.style.top = "5px";
        history.doOutsideHistory(() => page.appendChild(target));
        // jsdom does no layout: the page is drawn at its own size.
        Object.defineProperty(page, "offsetWidth", { value: 100 });
        page.getBoundingClientRect = () => ({ width: 100 }) as DOMRect;
        enableDraggingTargets(target);

        // The press puts the target where the pointer holds it, at the page's corner here.
        mouse(target, "mousedown", 1);
        mouse(target, "mouseup", 0);
        finishTask();

        expect(target.style.left).toBe("0px");
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Undo.Game.MoveTarget",
        );
    });

    it("makes no step for the same size option", async () => {
        await renderTool(kDragGame);
        await makeEarlierStep();
        expect(page.hasAttribute("data-same-size")).toBe(false);

        click(getOption("uniform sized targets"));

        await expectEarlierStepSurvives("data-same-size", "false");
    });

    it("makes no step for the show targets during play option", async () => {
        await renderTool(kDragGame);
        await makeEarlierStep();

        click(getOption("Show Targets During Play"));

        await expectEarlierStepSurvives(
            "data-show-targets-during-play",
            "false",
        );
    });

    it("makes no step for the show answers in targets option", async () => {
        await renderTool(kDragGame);
        await makeEarlierStep();

        click(getOption("Show answers on targets"));

        await expectEarlierStepSurvives("data-show-answers-in-targets", "true");
    });

    it("toggles what the page has, when the tool has not seen the page change", async () => {
        await renderTool({
            ...kDragGame,
            "data-same-size": "true",
            "data-show-targets-during-play": "true",
            "data-show-answers-in-targets": "false",
        });
        // The tool showed these values; now the page has the opposite ones, and the tool
        // has not been told.
        history.doOutsideHistory(() => {
            page.setAttribute("data-same-size", "false");
            page.setAttribute("data-show-targets-during-play", "false");
            page.setAttribute("data-show-answers-in-targets", "true");
        });

        click(getOption("uniform sized targets"));
        click(getOption("Show Targets During Play"));
        click(getOption("Show answers on targets"));

        expect(page.getAttribute("data-same-size")).toBe("true");
        expect(page.getAttribute("data-show-targets-during-play")).toBe("true");
        expect(page.getAttribute("data-show-answers-in-targets")).toBe("false");
    });

    it("sizes the targets to a draggable when same size is turned on with no canvas element active", async () => {
        await renderTool({ ...kDragGame, "data-same-size": "false" });
        history.doOutsideHistory(() => {
            page.innerHTML = `<div class="bloom-canvas-element" data-draggable-id="d1"
                style="width: 100px; height: 50px"></div>
            <div data-target-of="d1" style="width: 60px; height: 30px"></div>`;
        });
        const target = page.querySelector<HTMLElement>("[data-target-of]")!;
        await makeEarlierStep();

        click(getOption("uniform sized targets"));

        expect(page.getAttribute("data-same-size")).toBe("true");
        // The tool found the draggable on the page and made its target the same size.
        expect(target.style.width).toBe("100px");
        expect(target.style.height).toBe("50px");
        await expectEarlierStepSurvives("data-same-size", "true");
        expect(target.style.width).toBe("100px");
    });

    it("makes no step for a built-in sound chosen from the menu", async () => {
        await renderTool(kLegacyGame);
        await makeEarlierStep();

        chooseSound("correct_bell.mp3");
        // The tool copies the sound into the book, then plays it, after the click.
        await act(async () => {});

        expect(frames.apiCalls).toContain("fileIO/copyBuiltInSound");
        expectPostsKeepTheHistory();
        await expectEarlierStepSurvives(
            "data-correct-sound",
            "correct_bell.mp3",
        );
    });

    it("makes no step for a sound from Choose..., which arrives after the dialog", async () => {
        await renderTool(kLegacyGame);
        await makeEarlierStep();

        chooseSound("choose");
        expect(page.hasAttribute("data-correct-sound")).toBe(false);
        // The dialog's answer comes in a later task, with no transaction open.
        await act(async () => {});

        expect(frames.apiCalls).toContain("fileIO/chooseFile");
        expectPostsKeepTheHistory();
        await expectEarlierStepSurvives("data-correct-sound", "mine.mp3");
    });

    /**
     * Put a draggable word and its target on the page, as page setup that no step holds, and make
     * a canvas element manager whose active element is the draggable. The target holds the copy
     * that copyContentToTarget made before the user clicked into the draggable: the box has
     * CKEditor's class for a focused editor, which the copy lacks. That class exists only while
     * editing, so the next copy differs from the target only in it.
     */
    function addDraggableWord(): {
        draggable: HTMLElement;
        target: HTMLElement;
        word: HTMLElement;
    } {
        history.doOutsideHistory(() => {
            page.innerHTML = `<div class="bloom-canvas">
                <div class="bloom-canvas-element" data-draggable-id="d1"
                    style="left: 10px; top: 10px; width: 100px; height: 50px"
                    ><div class="bloom-translationGroup"
                        ><div class="bloom-editable cke_focus" lang="en" contenteditable="true"
                            ><p>cat</p></div></div></div>
                <div data-target-of="d1"
                    style="left: 10px; top: 100px; width: 100px; height: 50px"
                    ><div class="bloom-translationGroup"
                        ><div class="bloom-editable" lang="en"><p>cat</p></div></div></div>
            </div>`;
        });
        const draggable = page.querySelector<HTMLElement>(
            "[data-draggable-id]",
        )!;
        frames.activeElement = draggable;
        frames.manager = {
            getActiveElement: () => frames.activeElement,
            requestCanvasElementChangeNotification: () => {},
            removeDetachedTargets: () => {},
            adjustCanvasElementOrdering: () => {},
        };
        return {
            draggable,
            target: page.querySelector<HTMLElement>("[data-target-of]")!,
            word: draggable.querySelector("p")!,
        };
    }

    /** What listenToPageHistory in bloomEditing.ts does for the targets, whichever tool is current. */
    function listenAsThePageFrameDoes(): void {
        history.onAfterUndoOrRedo(() =>
            copyContentToTargetsAfterUndoOrRedo(page),
        );
    }

    /**
     * The user types an s at the end of the word: one undo step, as a key press makes. The tool's
     * observer watches child lists and attributes, not text, so this replaces the paragraph's
     * text node.
     */
    async function typeS(word: HTMLElement): Promise<void> {
        const typing = history.beginTransaction("Test.Typing");
        word.textContent = "cats";
        // The tool's observer copies the draggable to its target before the key press ends.
        await act(async () => {});
        typing.end();
        expect(history.undoDescriptionL10nKey).toBe("Test.Typing");
    }

    it("keeps redo after undoing typing in a draggable, whose target copies it", async () => {
        const { target, word } = addDraggableWord();
        await renderTool(kDragGame);
        expect(target.textContent).toBe("cat");

        await typeS(word);
        expect(target.textContent).toBe("cats");

        await act(async () => {
            await history.undo();
        });
        expect(word.textContent).toBe("cat");
        expect(target.textContent).toBe("cat");
        // The copy that followed the undo made no change that joins a step or clears redo.
        expect(history.redoDescriptionL10nKey).toBe("Test.Typing");
        expect(onUnmarkedChange.mock.calls).toEqual([]);

        await act(async () => {
            await history.redo();
        });
        expect(word.textContent).toBe("cats");
        expect(target.textContent).toBe("cats");
        expect(history.undoDescriptionL10nKey).toBe("Test.Typing");
        expect(onUnmarkedChange.mock.calls).toEqual([]);
    });

    it("copies a draggable that is no longer selected to its target after undo and redo", async () => {
        const { target, word } = addDraggableWord();
        await renderTool(kDragGame);
        listenAsThePageFrameDoes();
        await typeS(word);
        expect(target.textContent).toBe("cats");

        // The user selects nothing, so the tool stops watching the draggable.
        frames.activeElement = undefined;
        history.doOutsideHistory(() => act(() => tool.setActiveTab(0)));

        await act(async () => {
            await history.undo();
        });
        expect(word.textContent).toBe("cat");
        expect(target.textContent).toBe("cat");
        expect(history.redoDescriptionL10nKey).toBe("Test.Typing");

        await act(async () => {
            await history.redo();
        });
        expect(target.textContent).toBe("cats");
        expect(onUnmarkedChange.mock.calls).toEqual([]);
    });

    it("copies every draggable to its target after an undo that did not change it", async () => {
        const { target } = addDraggableWord();
        // A target that does not show what its draggable has, as after a copy that was missed.
        history.doOutsideHistory(() => {
            target.querySelector("p")!.textContent = "dog";
        });
        frames.activeElement = undefined;
        await renderTool(kDragGame);
        listenAsThePageFrameDoes();
        await makeEarlierStep();
        expect(target.textContent).toBe("dog");

        await act(async () => {
            await history.undo();
        });
        expect(page.hasAttribute("data-earlier")).toBe(false);
        expect(target.textContent).toBe("cat");

        history.doOutsideHistory(() => {
            target.querySelector("p")!.textContent = "dog";
        });
        await act(async () => {
            await history.redo();
        });
        expect(page.getAttribute("data-earlier")).toBe("true");
        expect(target.textContent).toBe("cat");
    });

    it("undoes and redoes a step that copied to a target, after a later copy replaced the target's children", async () => {
        const { draggable, target, word } = addDraggableWord();
        await renderTool(kDragGame);
        const childrenAtStart = Array.from(target.childNodes);
        // A step that copies the draggable to its target as part of what it does, as
        // CanvasElementManager does when it fits a box to its picture.
        await history.doTransaction("Test.Resize", () => {
            draggable.style.width = "120px";
            copyContentToTargetAndCleanup(draggable);
        });
        expect(history.undoDescriptionL10nKey).toBe("Test.Resize");
        // The copy did replace the target's children: the draggable's box has a class that the
        // target lacked.
        expect(target.childNodes[0]).not.toBe(childrenAtStart[0]);
        const childrenAfterResize = Array.from(target.childNodes);

        // The copy that typing makes replaces them again.
        await typeS(word);
        expect(target.childNodes[0]).not.toBe(childrenAfterResize[0]);
        expect(target.textContent).toBe("cats");

        await act(async () => {
            await history.undo();
        });
        await act(async () => {
            await history.undo();
        });
        // Neither undo was refused.
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe("Test.Resize");
        expect(draggable.style.width).toBe("100px");
        expect(word.textContent).toBe("cat");
        expect(target.textContent).toBe("cat");

        await act(async () => {
            await history.redo();
        });
        await act(async () => {
            await history.redo();
        });
        expect(history.undoDescriptionL10nKey).toBe("Test.Typing");
        expect(history.redoDescriptionL10nKey).toBeUndefined();
        expect(draggable.style.width).toBe("120px");
        expect(target.textContent).toBe("cats");
        expect(onUnmarkedChange.mock.calls).toEqual([]);
    });
});

describe("GameTool.newPageReady", () => {
    it("waits for a page when the page frame has none", () => {
        // The toolbox runs newPageReady again 600 ms after the first time
        // (scheduleDelayedNewPageReady in toolbox.ts), and the page frame may then have no page.
        expect(document.getElementById("page")).toBeNull();
        vi.useFakeTimers();
        try {
            expect(() => new GameTool().newPageReady()).not.toThrow();
        } finally {
            vi.clearAllTimers();
            vi.useRealTimers();
        }
    });
});
