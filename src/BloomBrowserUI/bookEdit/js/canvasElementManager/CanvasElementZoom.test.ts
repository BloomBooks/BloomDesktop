import {
    afterAll,
    beforeAll,
    beforeEach,
    describe,
    expect,
    test,
    vi,
} from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us, and nothing here
// draws a bubble.
vi.mock("comicaljs", () => ({
    Bubble: class {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
    },
}));

// Count the realignments instead of doing them; the layout they would measure is not real in
// jsdom anyway.
const alignSpy = vi.hoisted(() => vi.fn());
vi.mock("./CanvasElementSelectionUi", async (importOriginal) => ({
    ...(await importOriginal<typeof import("./CanvasElementSelectionUi")>()),
    alignControlFrameWithActiveElement: alignSpy,
}));

// This import deliberately comes after the vi.mock calls above, so that the module graph
// it pulls in gets the stubs.
import { CanvasElementManager } from "./CanvasElementManager";

// The page has one CanvasElementManager, and making a second one throws.
let manager: CanvasElementManager;

describe("onPageZoomChanged", () => {
    beforeAll(() => {
        // Fake timers, including performance.now(), for the whole file: nothing here waits in
        // real time, and the fake clock only runs forward from one test to the next.
        vi.useFakeTimers({
            toFake: ["setTimeout", "clearTimeout", "Date", "performance"],
        });
        manager = new CanvasElementManager();
    });
    afterAll(() => {
        vi.useRealTimers();
    });
    beforeEach(() => {
        // Let any realignment the last test left pending run, and move a minute past it, so
        // the manager does not treat this test's first zoom change as part of that burst.
        vi.advanceTimersByTime(60000);
        alignSpy.mockClear();
    });

    test("a single zoom change realigns at once, without asking Bloom for handle titles", () => {
        expect(alignSpy).not.toHaveBeenCalled(); // sanity check: nothing aligned before the change

        manager.onPageZoomChanged();

        expect(alignSpy).toHaveBeenCalledTimes(1);
        expect(alignSpy.mock.calls[0][1]).toBe(false);
    });

    test("a burst of zoom changes realigns once at the start and once at the end", () => {
        // What holding Ctrl and spinning the wheel sends while the UI thread is busy: many
        // changes with no time between them.
        for (let i = 0; i < 50; i++) {
            manager.onPageZoomChanged();
        }
        expect(alignSpy).toHaveBeenCalledTimes(1);

        vi.advanceTimersByTime(99);
        expect(alignSpy).toHaveBeenCalledTimes(1);
        vi.advanceTimersByTime(1);
        expect(alignSpy).toHaveBeenCalledTimes(2);
        expect(alignSpy.mock.calls[1][1]).toBe(false);

        // Nothing more is pending once the burst is over.
        vi.advanceTimersByTime(10000);
        expect(alignSpy).toHaveBeenCalledTimes(2);
    });

    test("zoom changes spread over a second realign about every 100ms, not every time", () => {
        // A change every 10ms for one second: 100 changes.
        for (let i = 0; i < 100; i++) {
            manager.onPageZoomChanged();
            vi.advanceTimersByTime(10);
        }
        vi.advanceTimersByTime(1000);

        expect(alignSpy.mock.calls.length).toBeGreaterThanOrEqual(10);
        expect(alignSpy.mock.calls.length).toBeLessThanOrEqual(12);
    });
});
