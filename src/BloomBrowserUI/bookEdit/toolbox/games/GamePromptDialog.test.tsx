import { act } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { unmountRoot } from "../../../utils/reactRender";
import { renderGamePromptDialog } from "./GamePromptDialog";

// The source bubbles need qtip, which these tests do not load; the prompt has one language.
vi.mock("../../sourceBubbles/BloomSourceBubbles", () => ({
    default: {
        ProduceSourceBubbles: () => undefined,
        MakeSourceBubblesIntoQtips: () => {},
        removeSourceBubbles: () => {},
    },
}));
vi.mock("../../../react_components/l10nHooks", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    useL10n: (english: string) => english,
}));

/** A letter draggable with this letter (none for an unused one) and its target. */
function letterAndTarget(id: string, letter: string): string {
    const unused = letter ? "" : " bloom-unused-in-lang";
    return (
        `<div class="bloom-canvas-element${unused}" data-draggable-id="${id}" style="left: 1px; top: 1px;">` +
        `<div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on Letter-style" lang="en"><p>${letter}</p></div></div></div>` +
        `<div class="drag-item-target${unused}" data-target-of="${id}" style="left: ${id}0px; top: 50px;"></div>`
    );
}

describe("GamePromptDialog", () => {
    let dialogRoot: HTMLElement | undefined;

    afterEach(() => {
        if (dialogRoot) act(() => unmountRoot(dialogRoot!));
        document.body.innerHTML = "";
    });

    it("Cancel puts back the style of every target, when an unused letter comes before visible ones", async () => {
        // The word "ab" with a third letter box, unused in this language, between its two.
        document.body.innerHTML =
            '<div class="bloom-page" data-activity="drag-letter-to-target"><div class="bloom-canvas">' +
            '<div id="prompt"><div class="bloom-translationGroup"><div class="bloom-editable bloom-visibility-code-on" lang="en" tabindex="0"><p>ab</p></div></div></div>' +
            letterAndTarget("1", "a") +
            letterAndTarget("2", "") +
            letterAndTarget("3", "b") +
            '</div></div><div id="dialogRoot"></div>';
        const targets = Array.from(
            document.querySelectorAll<HTMLElement>("[data-target-of]"),
        );
        const stylesBefore = targets.map((t) => t.getAttribute("style"));
        expect(stylesBefore).toEqual([
            "left: 10px; top: 50px;",
            "left: 20px; top: 50px;",
            "left: 30px; top: 50px;",
        ]);
        dialogRoot = document.getElementById("dialogRoot")!;
        const prompt = document.getElementById("prompt")!;
        await act(async () =>
            renderGamePromptDialog(dialogRoot!, prompt, true),
        );
        // What typing in the dialog does to the targets, which it lines up again.
        targets.forEach((t) => (t.style.left = "500px"));

        const cancelButton = document.querySelector<HTMLElement>(
            '[data-testid="dialog-cancel"]',
        );
        if (!cancelButton)
            throw new Error("The dialog showed no Cancel button.");
        await act(async () => cancelButton.click());

        expect(targets.map((t) => t.getAttribute("style"))).toEqual(
            stylesBefore,
        );
    });
});
