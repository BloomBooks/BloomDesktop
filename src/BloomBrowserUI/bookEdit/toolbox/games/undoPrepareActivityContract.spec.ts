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

// The other half of the workaround: knowing when play mode is over.
//
// The page frame stops volunteering snapshots while the game tool is in its Play tab, and starts
// again when it leaves. "Leaves" is not only the user picking another tab -- switching to a
// different tool detaches the game tool straight from Play, and the tool's teardown runs
// undoPrepareActivity on the LIVE page. If that path did not also resume, nothing else the user did
// on that page would ever be volunteered to C#, and quitting would write what it held from before.
//
// removeToolMarkup tells the two apart by whether the element it is given is still in the document:
// the clone taken for a save is detached, the live page is not. This pins that distinction, which
// is what the fix rests on.
describe("telling a save's clone from the live page", () => {
    it("a clone of the body is detached, and the live page is not", () => {
        document.body.innerHTML = `<div class="bloom-page" id="p1"></div>`;
        const livePage = document.getElementsByClassName(
            "bloom-page",
        )[0] as HTMLElement;
        const cloneOfBody = document.body.cloneNode(true) as HTMLElement;
        const pageInClone = cloneOfBody.getElementsByClassName(
            "bloom-page",
        )[0] as HTMLElement;

        expect(livePage.isConnected).toBe(true);
        expect(pageInClone.isConnected).toBe(false);
    });
});
