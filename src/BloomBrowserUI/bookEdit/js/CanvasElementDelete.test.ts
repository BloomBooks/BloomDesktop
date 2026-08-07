import { describe, expect, test, vi } from "vitest";
import $ from "jquery";
import "../../lib/jquery.qtip.js"; // puts qtip into $.fn

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. deleteCanvasElement
// only needs it to take the element out of the DOM, which is what the real
// deleteBubbleFromFamily does at the end of its work.
vi.mock("comicaljs", () => ({
    Bubble: class {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: () => {},
        deleteBubbleFromFamily: (
            element: HTMLElement,
            container: HTMLElement,
        ) => container.removeChild(element),
    },
}));

// This import deliberately comes after the vi.mock call above, so that the module graph
// it pulls in gets the stubbed comicaljs.
import { CanvasElementManager } from "./CanvasElementManager";

// Attach a qtip the way BloomHintBubbles.makeHintBubbleCore does for a
// data-derived="topic" field: always showing, and rendered into the page scaling
// container rather than inside the element it annotates.
function makeTopicHintBubble(topicField: HTMLElement): void {
    $(topicField).qtip({
        content: "Choose topic",
        position: { container: $("div#page-scaling-container") },
        show: { ready: true },
        hide: { event: false },
        style: { classes: "topic-chooser-hint-bubble" },
    });
}

function setUpTwoTopicCanvasElements(): HTMLElement[] {
    document.body.innerHTML = `
        <div id="page-scaling-container">
            <div class="bloom-page">
                <div class="bloom-canvas">
                    <div class="bloom-canvas-element" id="ce1">
                        <div class="coverBottomBookTopic" data-derived="topic"
                             data-hint="Choose topic">Fiction</div>
                    </div>
                    <div class="bloom-canvas-element" id="ce2">
                        <div class="coverBottomBookTopic" data-derived="topic"
                             data-hint="Choose topic">Fiction</div>
                    </div>
                </div>
            </div>
        </div>`;
    const canvasElements = Array.from(
        document.querySelectorAll<HTMLElement>(".bloom-canvas-element"),
    );
    canvasElements.forEach((canvasElement) =>
        makeTopicHintBubble(
            canvasElement.querySelector("[data-derived]") as HTMLElement,
        ),
    );
    return canvasElements;
}

describe("CanvasElementManager.deleteCanvasElement", () => {
    test("removes the deleted element's topic chooser bubble but not another one's", async () => {
        const [canvasElement1, canvasElement2] = setUpTwoTopicCanvasElements();
        // qtip renders after its default show delay, so let that happen.
        await new Promise((resolve) => setTimeout(resolve, 200));
        const manager = new CanvasElementManager();
        // Rebuilding the editing UI needs the whole editor; it isn't what we're testing.
        manager.refreshCanvasElementEditing = () => {};

        const bubbleOf = (canvasElement: HTMLElement) =>
            document.getElementById(
                canvasElement
                    .querySelector("[data-derived]")!
                    .getAttribute("aria-describedby")!,
            );
        // Sanity check: each topic field starts out with its own bubble in the document.
        const bubble1 = bubbleOf(canvasElement1);
        const bubble2 = bubbleOf(canvasElement2);
        if (!bubble1 || !bubble2 || bubble1 === bubble2) {
            throw new Error(
                "Test setup failed to give each topic field its own qtip bubble",
            );
        }
        expect(document.querySelectorAll("div.qtip").length).toBe(2);

        manager.deleteCanvasElement(canvasElement1);

        expect(canvasElement1.isConnected).toBe(false);
        expect(bubble1.isConnected).toBe(false);
        expect(bubble2.isConnected).toBe(true);
        expect(document.querySelectorAll("div.qtip").length).toBe(1);
    });
});
