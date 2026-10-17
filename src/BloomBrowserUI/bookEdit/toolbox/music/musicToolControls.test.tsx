import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderRoot, unmountRoot } from "../../../utils/reactRender";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { attachInputTransactions } from "../../pageHistory/pageHistoryInput";
import { PageHistory } from "../../pageHistory/pageHistoryTypes";
import { MusicToolControls } from "./musicToolControls";

// What the tests put in the page frame, and the callback of the GET that "Choose..." sends.
const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    chooseFileCallback: undefined as
        | ((result: { data: string }) => void)
        | undefined,
}));
// The tool reaches the page history through the page frame's exports (pageHistoryAccess.ts).
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history ? { pageHistory: () => frames.history } : null,
}));
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    get: (url: string, callback: (result: { data: string }) => void) => {
        if (url === "music/ui/chooseFile") frames.chooseFileCallback = callback;
    },
}));
// Undefined is "no information yet, so not blocked", which keeps the tool's controls live.
vi.mock("../../../react_components/featureStatus", () => ({
    useGetFeatureStatus: () => undefined,
    useGetFeatureAvailabilityMessage: () => "",
}));

// The tool lives in the toolbox frame, and reaches the page through the iframe with the id
// "page" (getPageIFrame in utils/shared.ts), so the tests make that iframe here and attach the
// input transactions with this window, where the tool renders, as one of the other windows.
// As in CanvasToolControls.test.tsx, the user's events are the ones in userEvents, and a test
// says where the browser's task ends by calling finishTask().
describe("MusicToolControls and the page history", () => {
    let history: PageHistory;
    let page: HTMLElement;
    let container: HTMLDivElement;
    let tool: MusicToolControls;
    let detach: () => void;
    let userEvents: WeakSet<Event>;
    let endOfTask: (() => void)[];
    let onUnmarkedChange: ReturnType<typeof vi.fn>;

    beforeEach(() => {
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
        container = document.createElement("div");
        document.body.appendChild(container);
        act(() => {
            renderRoot(
                <MusicToolControls ref={(t) => (tool = t!)} />,
                container,
            );
        });
    });

    afterEach(() => {
        act(() => unmountRoot(container));
        detach();
        history.dispose();
        frames.history = undefined;
        frames.chooseFileCallback = undefined;
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

    /** A key press by the user on target, and the end of its task. */
    function press(target: Element, key: string, keyCode: number): void {
        act(() => {
            const event = new KeyboardEvent("keydown", {
                key,
                bubbles: true,
                cancelable: true,
            });
            // rc-slider reads only keyCode, which jsdom's KeyboardEvent leaves at 0.
            Object.defineProperty(event, "keyCode", { value: keyCode });
            userEvents.add(event);
            target.dispatchEvent(event);
            finishTask();
        });
    }

    function getRadio(value: string): HTMLInputElement {
        const radio = container.querySelector<HTMLInputElement>(
            `input[type="radio"][value="${value}"]`,
        );
        if (!radio) fail(`The tool did not render the ${value} radio button.`);
        return radio;
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

    it("ends the history for a change from the toolbox that is recorded", async () => {
        // The check that the other tests' "the earlier step survives" can fail.
        await makeEarlierStep();
        const button = document.createElement("button");
        button.addEventListener("click", () =>
            page.setAttribute("data-backgroundaudio", "unwrapped.mp3"),
        );
        container.appendChild(button);

        click(button);

        expect(page.getAttribute("data-backgroundaudio")).toBe("unwrapped.mp3");
        expect(history.undoDescriptionL10nKey).toBeUndefined();
    });

    it("makes no step for No Music", async () => {
        await makeEarlierStep();
        expect(page.hasAttribute("data-backgroundaudio")).toBe(false);

        click(getRadio("noMusic"));

        expect(tool.state.activeRadioValue).toBe("noMusic");
        await expectEarlierStepSurvives("data-backgroundaudio", "none");
    });

    it("makes no step for Continue music from previous page", async () => {
        history.doOutsideHistory(() =>
            page.setAttribute("data-backgroundaudio", "song.mp3"),
        );
        act(() => tool.updateBasedOnContentsOfPage());
        expect(tool.state.activeRadioValue).toBe("newMusic");
        await makeEarlierStep();

        click(getRadio("continueMusic"));

        expect(tool.state.activeRadioValue).toBe("continueMusic");
        await expectEarlierStepSurvives("data-backgroundaudio", null);
    });

    it("makes no step for the volume slider", async () => {
        history.doOutsideHistory(() =>
            page.setAttribute("data-backgroundaudio", "song.mp3"),
        );
        act(() => tool.updateBasedOnContentsOfPage());
        await makeEarlierStep();
        expect(page.hasAttribute("data-backgroundaudiovolume")).toBe(false);
        const handle = container.querySelector(
            ".musicVolumeSlider [role='slider']",
        );
        if (!handle) fail("The tool did not render the volume slider.");

        press(handle, "ArrowRight", 39);

        const volume = page.getAttribute("data-backgroundaudiovolume");
        expect(volume).not.toBeNull();
        await expectEarlierStepSurvives("data-backgroundaudiovolume", volume);
    });

    it("makes no step for a file chosen with Choose..., which arrives after the click", async () => {
        await makeEarlierStep();
        const choose = container.querySelector(".musicChooseFile");
        if (!choose) fail("The tool did not render Choose...");
        click(choose);
        if (!frames.chooseFileCallback)
            fail("Choose... did not ask Bloom for a file.");
        expect(page.hasAttribute("data-backgroundaudio")).toBe(false);

        // The answer comes in a later task, with no transaction open.
        act(() => frames.chooseFileCallback!({ data: "song.mp3" }));

        expect(tool.state.musicName).toBe("song");
        await expectEarlierStepSurvives("data-backgroundaudio", "song.mp3");
    });
});
