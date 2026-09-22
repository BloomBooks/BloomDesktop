import { describe, expect, test } from "vitest";

import { tableCanvasElementControls } from "./canvasElementControlRegistry";
import { IControlAvailability, IControlContext } from "./canvasControlTypes";

// The element toolbar and menu for a table canvas element offer Duplicate only where
// the user may make a table: duplicating one is making one. Delete is offered either
// way, so that a user who cannot edit a table can still get rid of it. See
// installHostHooks in tableEditing.ts for the rest of what "frozen" withholds.

function makeCtx(tablesMayBeRestructured: boolean): IControlContext {
    // Only the fields these two rules read. Cast through unknown because
    // IControlContext is large and the rest is irrelevant here.
    return {
        tablesMayBeRestructured,
        isBackgroundImage: false,
        isSpecialGameElement: false,
        elementType: "table",
    } as unknown as IControlContext;
}

// The rule fields are `boolean | ((ctx) => boolean)`; evaluate either form.
function evaluate(
    rule: IControlAvailability | undefined,
    ctx: IControlContext,
): boolean {
    if (rule === undefined) {
        throw new Error("rule under test is undefined");
    }
    return typeof rule === "function" ? rule(ctx) : rule;
}

describe("tableCanvasElementControls availability", () => {
    const duplicate = tableCanvasElementControls.availabilityRules.duplicate;
    const deleteRule = tableCanvasElementControls.availabilityRules.delete;

    // Sanity: the rules under test exist and are rule objects rather than the
    // "exclude" sentinel, so the assertions below mean something.
    test("setup: duplicate and delete are rule objects", () => {
        expect(duplicate).toBeTruthy();
        expect(duplicate).not.toBe("exclude");
        expect(deleteRule).toBeTruthy();
        expect(deleteRule).not.toBe("exclude");
    });

    test("Duplicate is offered when tables may be restructured", () => {
        const rule = duplicate as Exclude<typeof duplicate, "exclude">;
        expect(evaluate(rule!.visible, makeCtx(true))).toBe(true);
    });

    test("Duplicate is withheld when they may not", () => {
        const rule = duplicate as Exclude<typeof duplicate, "exclude">;
        expect(evaluate(rule!.visible, makeCtx(false))).toBe(false);
    });

    test("Delete stays on the toolbar either way", () => {
        const rule = deleteRule as Exclude<typeof deleteRule, "exclude">;
        const toolbar = rule!.surfacePolicy?.toolbar;
        expect(toolbar).toBeTruthy();
        expect(evaluate(toolbar!.visible, makeCtx(true))).toBe(true);
        expect(evaluate(toolbar!.visible, makeCtx(false))).toBe(true);
    });
});
