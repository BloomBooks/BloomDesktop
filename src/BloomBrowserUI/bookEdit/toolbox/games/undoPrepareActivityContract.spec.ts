import { describe, it, expect, beforeEach } from "vitest";
import { prepareActivity, undoPrepareActivity } from "bloom-player";

// Pins the one thing about bloom-player that the game tool's save path has to work around.
//
// GameTool.removeToolMarkup() is handed a CLONE of the page when Bloom gathers the page to save
// it, and calls undoPrepareActivity() on it to take play-mode markup off that copy. That reads as
// if it could not affect the live page. It can: prepareActivity() records the live draggables and
// where they started in bloom-player's own module state, and undoPrepareActivity() restores THOSE
// elements, whatever element it is given.
//
// That is right when it is given the live page (leaving the Play tab), and destructive when it is
// given a clone while the user is still playing -- it snaps their dragged items back. Since Bloom
// gathers the page whenever the page changes, and dragging changes the page, that made a drag
// activity unplayable in the editor. The fix is to stop gathering while in play mode
// (setSnapshotsSuspended in bookEdit/js/pageSnapshot.ts).
//
// So this is a dependency test, not a test of our own code: if a bloom-player bump ever confines
// undoPrepareActivity to the element it is given, the first test here fails, and that is the
// signal that the suspension can go.
describe("bloom-player's undoPrepareActivity, as the save path depends on it", () => {
    const authoredLeft = "99px";
    const authoredTop = "88px";

    let livePage: HTMLElement;
    let liveDraggable: HTMLElement;

    beforeEach(() => {
        document.body.innerHTML = `
            <div class="bloom-page" data-activity="drag-letter-to-target">
              <div class="bloom-canvas-element" data-draggable-id="d1"
                   style="left: ${authoredLeft}; top: ${authoredTop};"></div>
            </div>`;
        livePage = document.getElementsByClassName(
            "bloom-page",
        )[0] as HTMLElement;
        liveDraggable = document.querySelector(
            "[data-draggable-id]",
        ) as HTMLElement;

        prepareActivity(livePage, () => {
            /* nothing to do */
        });

        // The user drags the item somewhere and it lands on its target.
        liveDraggable.style.left = "5px";
        liveDraggable.style.top = "6px";
        liveDraggable.classList.add("bloom-draggedToTarget");

        // Sanity check: the drag really did move it, so a restored position below means
        // undoPrepareActivity moved it back rather than that it never moved.
        expect(liveDraggable.style.left).toBe("5px");
        expect(liveDraggable.classList.contains("bloom-draggedToTarget")).toBe(
            true,
        );
    });

    it("undoes the LIVE page even when it is given only a clone", () => {
        undoPrepareActivity(livePage.cloneNode(true) as HTMLElement);

        expect(liveDraggable.style.left).toBe(authoredLeft);
        expect(liveDraggable.style.top).toBe(authoredTop);
        expect(liveDraggable.classList.contains("bloom-draggedToTarget")).toBe(
            false,
        );
    });

    it("puts the live page back where play mode found it when given the live page", () => {
        undoPrepareActivity(livePage);

        expect(liveDraggable.style.left).toBe(authoredLeft);
        expect(liveDraggable.style.top).toBe(authoredTop);
        expect(liveDraggable.classList.contains("bloom-draggedToTarget")).toBe(
            false,
        );
    });
});
