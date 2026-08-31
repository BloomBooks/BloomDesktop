import { describe, expect, test } from "vitest";

import { imageAvailabilityRules } from "./canvasControlAvailabilityRules";
import { IControlAvailability, IControlContext } from "./canvasControlTypes";

// Unit tests for the `editWithAi` availability rule (see canvasControlAvailabilityRules.ts).
// This is the gating logic behind the "Edit with AI..." image menu item: it must stay hidden
// until the experimental feature is on, and be disabled unless there is a real, modifiable
// image whose format the editor can actually edit. The rule is a pure function of
// IControlContext, so we exercise it directly rather than driving the whole menu.

// A context with every flag off/neutral. Each test flips only the flags the rule reads, so a
// change in behavior points at the rule and not at incidental context. Cast through unknown
// because IControlContext is large and these rules only read a handful of fields.
function makeCtx(overrides: Partial<IControlContext>): IControlContext {
    const base: Partial<IControlContext> = {
        aiImageEditingAvailable: false,
        hasImage: false,
        hasRealImage: false,
        canModifyImage: false,
        imageIsAiEditableFormat: false,
    };
    return { ...base, ...overrides } as unknown as IControlContext;
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

describe("imageAvailabilityRules.editWithAi", () => {
    const editWithAi = imageAvailabilityRules.editWithAi;

    // Sanity: the rule we intend to test actually exists and is a real rule object
    // (not the "exclude" sentinel), so the assertions below are meaningful.
    test("setup: editWithAi is a rule object with visible and enabled predicates", () => {
        expect(editWithAi).toBeTruthy();
        expect(editWithAi).not.toBe("exclude");
        const rule = editWithAi as Exclude<typeof editWithAi, "exclude">;
        expect(typeof rule.visible).toBe("function");
        expect(typeof rule.enabled).toBe("function");
    });

    const rule = editWithAi as Exclude<
        typeof editWithAi,
        "exclude" | undefined
    >;

    describe("visible = aiImageEditingAvailable && hasImage", () => {
        test("hidden when the experimental feature is off, even with an image", () => {
            expect(
                evaluate(
                    rule.visible,
                    makeCtx({
                        aiImageEditingAvailable: false,
                        hasImage: true,
                    }),
                ),
            ).toBe(false);
        });

        test("hidden when the feature is on but the element has no image", () => {
            expect(
                evaluate(
                    rule.visible,
                    makeCtx({
                        aiImageEditingAvailable: true,
                        hasImage: false,
                    }),
                ),
            ).toBe(false);
        });

        test("visible when the feature is on and the element has an image", () => {
            expect(
                evaluate(
                    rule.visible,
                    makeCtx({
                        aiImageEditingAvailable: true,
                        hasImage: true,
                    }),
                ),
            ).toBe(true);
        });
    });

    describe("enabled = hasRealImage && canModifyImage && imageIsAiEditableFormat", () => {
        test("disabled when there is only a placeholder (no real image)", () => {
            expect(
                evaluate(
                    rule.enabled,
                    makeCtx({
                        hasRealImage: false,
                        canModifyImage: true,
                        imageIsAiEditableFormat: true,
                    }),
                ),
            ).toBe(false);
        });

        test("disabled when the image may not be modified", () => {
            expect(
                evaluate(
                    rule.enabled,
                    makeCtx({
                        hasRealImage: true,
                        canModifyImage: false,
                        imageIsAiEditableFormat: true,
                    }),
                ),
            ).toBe(false);
        });

        test("disabled when the image format is one the editor cannot edit (e.g. svg)", () => {
            expect(
                evaluate(
                    rule.enabled,
                    makeCtx({
                        hasRealImage: true,
                        canModifyImage: true,
                        imageIsAiEditableFormat: false,
                    }),
                ),
            ).toBe(false);
        });

        test("enabled with a real, modifiable, editable-format image", () => {
            expect(
                evaluate(
                    rule.enabled,
                    makeCtx({
                        hasRealImage: true,
                        canModifyImage: true,
                        imageIsAiEditableFormat: true,
                    }),
                ),
            ).toBe(true);
        });
    });
});
