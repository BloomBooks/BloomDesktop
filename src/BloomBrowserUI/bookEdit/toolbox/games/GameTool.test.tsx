import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { unmountRoot } from "../../../utils/reactRender";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import { PageHistory } from "../../pageHistory/pageHistoryTypes";
import { GameTool } from "./GameTool";

// What the tests put in the page frame, and the calls the tool made to Bloom's API.
const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    apiCalls: [] as string[],
}));
// The tool reaches the page history through the page frame's exports (pageHistoryAccess.ts).
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history ? { pageHistory: () => frames.history } : null,
}));
// No canvas element is active, and there are none on the test pages.
vi.mock("../canvas/canvasElementPageBridge", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getCanvasElementManager: () => undefined,
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
        if (url === "fileIO/chooseFile") return { data: "mine.mp3" };
        if (url === "fileIO/getSpecialLocation")
            return { data: "C:/book/audio" };
        return undefined;
    },
    postData: async (url: string) => {
        frames.apiCalls.push(url);
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
    let detach: () => void;
    let userEvents: WeakSet<Event>;
    let endOfTask: (() => void)[];
    let onUnmarkedChange: ReturnType<typeof vi.fn>;

    beforeEach(() => {
        frames.apiCalls = [];
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
        document.body.innerHTML = "";
    });

    /** Give the page the game's attributes, then show the tool for it. */
    async function renderTool(attributes: Record<string, string>) {
        history.doOutsideHistory(() => {
            for (const name of Object.keys(attributes))
                page.setAttribute(name, attributes[name]);
        });
        await act(async () => {
            root = new GameTool().makeRootElement();
            document.body.appendChild(root);
        });
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

        expect(frames.apiCalls).toContain("fileIO/copyFile");
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
        await expectEarlierStepSurvives("data-correct-sound", "mine.mp3");
    });
});
