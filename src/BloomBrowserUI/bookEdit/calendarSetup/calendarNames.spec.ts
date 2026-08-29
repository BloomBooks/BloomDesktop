import { describe, expect, it } from "vitest";
import {
    captureCalendarName,
    defaultCalendarYear,
    normalizeCalendarSettings,
    reconcileCalendarName,
} from "./calendarNames";

describe("reconcileCalendarName on the first open of a page in a session", () => {
    const firstOpen = true;

    it("puts the collection's name onto a page that says something else", () => {
        expect(reconcileCalendarName("JANUARY", "January", firstOpen)).toEqual({
            valueForPage: "January",
        });
    });

    it("takes the page's name into a collection that has none", () => {
        expect(reconcileCalendarName("January", "", firstOpen)).toEqual({
            valueForSettings: "January",
        });
    });

    it("does nothing when the two already agree", () => {
        expect(reconcileCalendarName("January", "January", firstOpen)).toEqual(
            {},
        );
    });

    it("does nothing when neither has a name", () => {
        expect(reconcileCalendarName("", "", firstOpen)).toEqual({});
    });

    it("fills an empty page slot from the collection", () => {
        expect(reconcileCalendarName("", "January", firstOpen)).toEqual({
            valueForPage: "January",
        });
    });
});

describe("reconcileCalendarName on a later open of a page in the same session", () => {
    const laterOpen = false;

    it("fills an empty page slot from the collection", () => {
        expect(reconcileCalendarName("", "Mande", laterOpen)).toEqual({
            valueForPage: "Mande",
        });
    });

    it("leaves a name the user typed alone, even when the collection now says something else", () => {
        // "foo" typed on month 1 survives "bar" being typed on month 2.
        expect(reconcileCalendarName("foo", "bar", laterOpen)).toEqual({});
    });

    it("does not take a page's name into the collection", () => {
        // Capture happens when the page is saved, not when it is opened again.
        expect(reconcileCalendarName("foo", "", laterOpen)).toEqual({});
    });
});

describe("captureCalendarName", () => {
    it("asks for the collection to be updated when the user has typed something new", () => {
        expect(captureCalendarName("Mande", "Mon")).toBe("Mande");
        expect(captureCalendarName("Mande", "")).toBe("Mande");
    });
    it("asks for nothing when the collection already says it", () => {
        expect(captureCalendarName("Mande", "Mande")).toBeUndefined();
        expect(captureCalendarName(" Mande ", "Mande")).toBeUndefined();
    });
    it("never empties the collection's name", () => {
        expect(captureCalendarName("", "Mande")).toBeUndefined();
        expect(captureCalendarName("   ", "Mande")).toBeUndefined();
    });
});

describe("normalizeCalendarSettings", () => {
    it("turns a collection that has never held a calendar into empty names", () => {
        const settings = normalizeCalendarSettings(null);
        expect(settings.monthNames.length).toBe(12);
        expect(settings.dayNames.length).toBe(7);
        expect(settings.monthNames.every((n) => n === "")).toBe(true);
        expect(settings.firstDayOfWeek).toBeNull();
    });
    it("keeps the names it is given and pads what is missing", () => {
        const settings = normalizeCalendarSettings({
            monthNames: ["Enero"],
            firstDayOfWeek: 1,
        });
        expect(settings.monthNames[0]).toBe("Enero");
        expect(settings.monthNames[11]).toBe("");
        expect(settings.firstDayOfWeek).toBe(1);
    });
    it("rejects a first day of the week that is not a day", () => {
        expect(
            normalizeCalendarSettings({ firstDayOfWeek: 9 }).firstDayOfWeek,
        ).toBeNull();
    });
});

describe("defaultCalendarYear", () => {
    it("offers this year up to the end of May", () => {
        expect(defaultCalendarYear(new Date(2027, 0, 15))).toBe(2027);
        expect(defaultCalendarYear(new Date(2027, 4, 31))).toBe(2027);
    });
    it("offers next year from June on, when people start making next year's calendar", () => {
        expect(defaultCalendarYear(new Date(2027, 5, 1))).toBe(2028);
        expect(defaultCalendarYear(new Date(2027, 11, 25))).toBe(2028);
    });
});
