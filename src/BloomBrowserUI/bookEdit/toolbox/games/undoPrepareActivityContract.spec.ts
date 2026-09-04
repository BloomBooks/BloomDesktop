import { describe, it, expect, beforeEach } from "vitest";
import { prepareActivity, undoPrepareActivity } from "bloom-player";

// What the game tool's save path needs from bloom-player, pinned here because getting it wrong is
// invisible from Bloom's side until a user loses work.
//
// GameTool.removeToolMarkup is handed a CLONE of the page when Bloom gathers the page to save it,
// and the live page when the tool is detached. It calls undoPrepareActivity on whichever it is
// given, and needs two things of it:
//
//   * the element it is given comes out with the draggables where the AUTHOR put them, because
//     that element is what gets written into the book; and
//   * no other element is touched, because the live page may still be being played.
//
// Neither was true before bloom-player#441: positions were restored through the element references
// recorded when play began, so undoing a copy put the LIVE page back -- cancelling the tester's
// drags a moment after each one -- while leaving the copy holding the dragged positions, which then
// overwrote the book's authored ones. Bloom worked around the first half by not watching the page
// during play mode. That workaround is gone, and this is what replaced it.

const authoredLeft = "10px";
const authoredTop = "20px";

function makeActivityPage() {
    document.body.innerHTML = "";
    const page = document.createElement("div");
    page.classList.add("bloom-page");
    page.setAttribute("data-activity", "drag-letter-to-target");
    const draggable = document.createElement("div");
    draggable.setAttribute("data-draggable-id", "d1");
    draggable.style.left = authoredLeft;
    draggable.style.top = authoredTop;
    page.appendChild(draggable);
    document.body.appendChild(page);
    return { page, draggable };
}

// An older bloom-player cannot meet this contract. Rather than a wall of red while the dependency
// catches up, say so once and skip: what this needs is whichever alpha first contains
// bloom-player#441.
function installedBloomPlayerConfinesUndo(): boolean {
    const { page, draggable } = makeActivityPage();
    prepareActivity(page, () => {
        /* no change-page action */
    });
    draggable.style.left = "300px";
    undoPrepareActivity(page.cloneNode(true) as HTMLElement);
    const liveWasLeftAlone = draggable.style.left === "300px";
    undoPrepareActivity(page); // leave no session state behind for the real tests
    document.body.innerHTML = "";
    return liveWasLeftAlone;
}

const confinesUndo = installedBloomPlayerConfinesUndo();
if (!confinesUndo) {
    console.warn(
        "Skipping the undoPrepareActivity contract: the installed bloom-player predates " +
            "bloom-player#441, so undoing a copy still reaches into the live page. Bump the " +
            "bloom-player dependency to pick up the fix.",
    );
}

describe.skipIf(!confinesUndo)(
    "what the save path needs from bloom-player's undoPrepareActivity",
    () => {
        beforeEach(() => {
            document.body.innerHTML = "";
        });

        it("gives the copy the authored positions, so that is what the book records", () => {
            const { page, draggable } = makeActivityPage();
            prepareActivity(page, () => {
                /* no change-page action */
            });

            // The tester drags the item onto its target.
            draggable.style.left = "300px";
            draggable.style.top = "400px";
            draggable.classList.add("bloom-draggedToTarget");

            const copy = page.cloneNode(true) as HTMLElement;
            expect(
                copy.querySelector<HTMLElement>("[data-draggable-id]")!.style
                    .left,
                "test setup: the copy starts out holding the dragged position",
            ).toBe("300px");

            undoPrepareActivity(copy);

            const inCopy = copy.querySelector<HTMLElement>(
                "[data-draggable-id]",
            )!;
            expect(inCopy.style.left).toBe(authoredLeft);
            expect(inCopy.style.top).toBe(authoredTop);
            expect(inCopy.classList.contains("bloom-draggedToTarget")).toBe(
                false,
            );
        });

        it("leaves the live page alone, so the game stays playable", () => {
            const { page, draggable } = makeActivityPage();
            prepareActivity(page, () => {
                /* no change-page action */
            });
            draggable.style.left = "300px";
            draggable.style.top = "400px";

            undoPrepareActivity(page.cloneNode(true) as HTMLElement);

            expect(draggable.style.left).toBe("300px");
            expect(draggable.style.top).toBe("400px");
        });

        it("still restores the live page when that is what it is given", () => {
            // Leaving the Play tab, which is the other way the tool calls this.
            const { page, draggable } = makeActivityPage();
            prepareActivity(page, () => {
                /* no change-page action */
            });
            draggable.style.left = "300px";

            undoPrepareActivity(page);

            expect(draggable.style.left).toBe(authoredLeft);
        });

        it("saving during play does not cost the live page its later restore", () => {
            const { page, draggable } = makeActivityPage();
            prepareActivity(page, () => {
                /* no change-page action */
            });
            draggable.style.left = "300px";

            undoPrepareActivity(page.cloneNode(true) as HTMLElement); // a save
            undoPrepareActivity(page); // then leaving Play

            expect(draggable.style.left).toBe(authoredLeft);
        });
    },
);
