import { describe, it, expect } from "vitest";
import {
    beginStep,
    beginTest,
    emptyE2eCaptionState,
    endStep,
    finishTest,
    formatClock,
    formatStepTime,
    stepCounterText,
    stepElapsedMs,
    testElapsedMs,
    visibleSteps,
} from "./e2eCaptionModel";

// The caption's state, exercised without a DOM. Times are handed in rather than read from the
// clock, so these tests say exactly what the strip shows at a given moment.

const t0 = 1_000_000;

describe("e2e caption state", () => {
    it("starts empty", () => {
        expect(emptyE2eCaptionState.steps).toHaveLength(0);
        expect(emptyE2eCaptionState.testStartedAt).toBe(0);
        expect(stepCounterText(emptyE2eCaptionState)).toBe("");
    });

    it("a begun test carries its spec, title and start", () => {
        const state = beginTest("tables-core", "add a table", t0);
        expect(state.specName).toBe("tables-core");
        expect(state.testTitle).toBe("add a table");
        expect(state.testStartedAt).toBe(t0);
        expect(state.steps).toHaveLength(0);
    });

    it("running a step then ending it records the measured duration", () => {
        let state = beginTest("tables-core", "add a table", t0);
        state = beginStep(state, "Open the toolbox", t0 + 500);
        expect(state.steps[0].status).toBe("running");
        // Sanity check the live timer before the step ends.
        expect(stepElapsedMs(state.steps[0], t0 + 2500)).toBe(2000);

        state = endStep(state, "passed", t0 + 3000, 2100);
        expect(state.steps[0].status).toBe("passed");
        expect(state.steps[0].durationMs).toBe(2100);
        // A finished step's time no longer moves with the clock.
        expect(stepElapsedMs(state.steps[0], t0 + 99999)).toBe(2100);
    });

    it("falls back to its own clock when the test side reports no duration", () => {
        let state = beginTest("s", "t", t0);
        state = beginStep(state, "Type into a cell", t0 + 1000);
        state = endStep(state, "passed", t0 + 1900);
        expect(state.steps[0].durationMs).toBe(900);
    });

    it("keeps a failed step failed, and a fixme step fixme", () => {
        let state = beginTest("s", "t", t0);
        state = beginStep(state, "Drag a column boundary", t0);
        state = endStep(state, "failed", t0 + 100);
        state = beginStep(state, "Skipped one", t0 + 200);
        state = endStep(state, "fixme", t0 + 300);
        expect(state.steps.map((step) => step.status)).toEqual([
            "failed",
            "fixme",
        ]);
    });

    it("closes a forgotten running step when the next one starts", () => {
        let state = beginTest("s", "t", t0);
        state = beginStep(state, "First", t0);
        state = beginStep(state, "Second", t0 + 700);
        expect(state.steps[0].status).toBe("passed");
        expect(state.steps[0].durationMs).toBe(700);
        expect(state.steps[1].status).toBe("running");
    });

    it("ending a step with none running changes nothing", () => {
        const state = beginTest("s", "t", t0);
        expect(endStep(state, "passed", t0 + 5)).toBe(state);
    });

    it("shows only the last three steps", () => {
        let state = beginTest("s", "t", t0);
        ["a", "b", "c", "d"].forEach((title, index) => {
            state = beginStep(state, title, t0 + index * 10);
            state = endStep(state, "passed", t0 + index * 10 + 5);
        });
        expect(state.steps).toHaveLength(4);
        expect(visibleSteps(state).map((step) => step.title)).toEqual([
            "b",
            "c",
            "d",
        ]);
    });

    it("counts steps, with the total only when the caller knew it", () => {
        let state = beginTest("s", "t", t0);
        state = beginStep(state, "one", t0);
        expect(stepCounterText(state)).toBe("1");
        let withTotal = beginTest("s", "t", t0, 10);
        withTotal = beginStep(withTotal, "one", t0);
        expect(stepCounterText(withTotal)).toBe("1 / 10");
    });

    it("stops the test clock when the test finishes", () => {
        let state = beginTest("s", "t", t0);
        state = beginStep(state, "one", t0 + 100);
        expect(testElapsedMs(state, t0 + 5000)).toBe(5000);
        state = finishTest(state, t0 + 6000);
        expect(state.steps[0].status).toBe("passed");
        expect(testElapsedMs(state, t0 + 999999)).toBe(6000);
    });
});

describe("e2e caption formatting", () => {
    it("prints a step's time in tenths below ten seconds and whole seconds above", () => {
        expect(formatStepTime(0)).toBe("0.0s");
        expect(formatStepTime(2100)).toBe("2.1s");
        expect(formatStepTime(9999)).toBe("10.0s");
        expect(formatStepTime(11200)).toBe("11s");
    });

    it("prints the header clock as mm:ss", () => {
        expect(formatClock(0)).toBe("00:00");
        expect(formatClock(72000)).toBe("01:12");
        expect(formatClock(3_600_000)).toBe("60:00");
    });
});
