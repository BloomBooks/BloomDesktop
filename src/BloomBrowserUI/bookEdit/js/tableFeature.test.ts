import { describe, it, expect, vi } from "vitest";
import { refreshStructuralChrome } from "bloom-table";
import {
    refreshTableFeatureStatus,
    tablesMayBeRestructured,
} from "./tableFeature";

// What the gate answered at the moment the library was told to ask again.
const answersWhenRefreshed: boolean[] = [];

vi.mock("bloom-table", () => ({
    refreshStructuralChrome: vi.fn(() =>
        answersWhenRefreshed.push(tablesMayBeRestructured()),
    ),
}));

vi.mock("../../react_components/featureStatus", () => ({
    getFeatureStatusAsync: vi.fn(() =>
        Promise.resolve({ enabled: true, visible: true }),
    ),
}));

describe("refreshTableFeatureStatus", () => {
    it("has the library re-decide the table chrome once the answer is known", async () => {
        // Until the status arrives, tables may not be restructured.
        expect(tablesMayBeRestructured()).toBe(false);
        expect(refreshStructuralChrome).not.toHaveBeenCalled();

        await refreshTableFeatureStatus();

        expect(tablesMayBeRestructured()).toBe(true);
        // The library was asked to look again after the answer changed, so a
        // pointer already resting on a table sees its "+" buttons and pills.
        expect(answersWhenRefreshed).toEqual([true]);
    });
});
