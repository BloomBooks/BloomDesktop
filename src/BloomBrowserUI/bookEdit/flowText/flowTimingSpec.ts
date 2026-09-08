import { beforeEach, describe, expect, it, vi } from "vitest";
import {
    getFlowPassSamples,
    kMetricsPropertyName,
    timePass,
} from "./flowTiming";

describe("timePass", () => {
    beforeEach(() => {
        (window as unknown as Record<string, unknown>)[kMetricsPropertyName] =
            [];
    });

    it("returns what the pass returned and records it", () => {
        expect(timePass("mutation", () => 42)).toBe(42);

        const samples = getFlowPassSamples();
        expect(samples).toHaveLength(1);
        expect(samples[0].reason).toBe("mutation");
        expect(samples[0].ms).toBeGreaterThanOrEqual(0);
    });

    it("keeps the last thirty passes", () => {
        for (let index = 0; index < 35; index++) {
            timePass(`pass-${index}`, () => undefined);
        }

        const samples = getFlowPassSamples();
        expect(samples).toHaveLength(30);
        expect(samples[0].reason).toBe("pass-5");
        expect(samples[29].reason).toBe("pass-34");
    });

    it("records a pass that threw, and lets the error through", () => {
        expect(() =>
            timePass("mutation", () => {
                throw new Error("no");
            }),
        ).toThrow("no");
        expect(getFlowPassSamples()).toHaveLength(1);
    });

    it("warns about a pass that misses the frame budget", () => {
        const warn = vi.spyOn(console, "warn").mockImplementation(() => {
            // The message is what we are asserting on; do not print it.
        });
        const times = [0, 40];
        vi.spyOn(window.performance, "now").mockImplementation(
            () => times.shift() ?? 40,
        );

        timePass("styleChange", () => undefined);

        expect(warn).toHaveBeenCalledOnce();
        expect(warn.mock.calls[0][0]).toContain("styleChange");
        vi.restoreAllMocks();
    });
});
