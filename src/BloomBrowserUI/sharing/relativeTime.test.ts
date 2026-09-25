import { describe, expect, it } from "vitest";
import { formatTimeAgo } from "./relativeTime";

// Noon local time, so that a few hours either way stays on the same day.
const now = new Date(2026, 8, 23, 12, 0, 0);

function daysBefore(days: number, hour = 12): string {
    return new Date(2026, 8, 23 - days, hour, 0, 0).toISOString();
}

describe("formatTimeAgo", () => {
    it("says today for the same calendar day, even early in the morning", () => {
        expect(formatTimeAgo(daysBefore(0, 0), now, "en")).toBe("today");
    });

    it("says today for a date slightly in the future", () => {
        expect(
            formatTimeAgo(new Date(2026, 8, 24, 9).toISOString(), now, "en"),
        ).toBe("today");
    });

    it("counts calendar days, so late yesterday is yesterday", () => {
        expect(formatTimeAgo(daysBefore(1, 23), now, "en")).toBe("yesterday");
    });

    it("uses days, then weeks, months and years", () => {
        expect(formatTimeAgo(daysBefore(4), now, "en")).toBe("4 days ago");
        expect(formatTimeAgo(daysBefore(21), now, "en")).toBe("3 weeks ago");
        expect(formatTimeAgo(daysBefore(65), now, "en")).toBe("2 months ago");
        expect(formatTimeAgo(daysBefore(800), now, "en")).toBe("2 years ago");
    });

    it("localizes", () => {
        expect(formatTimeAgo(daysBefore(0), now, "fr")).toBe("aujourd’hui");
    });
});
